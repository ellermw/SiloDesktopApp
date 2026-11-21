using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

namespace SiloPlayer.Views;

/// <summary>
/// Admin "Plugin Apps" mode. WebUI <c>navigateToPluginRoute</c> launches the plugin
/// (<c>POST /api/v2/auth/plugin-launch</c>, which sets the five-minute HttpOnly
/// <c>silo_plugin_access</c> cookie on the plugin-content parent path) and then loads the plugin
/// page as a full page. This host gives the page exactly that cookie and nothing else: an
/// InPrivate profile of its own, no host objects, no web messages, no account headers, no
/// downloads, popups, permissions or external protocols, and only this installation's pages
/// and assets on the connected server.
/// </summary>
public sealed partial class PluginRoutePage
{
    /// <summary>Opens an Admin plugin app captured under one Admin authority epoch.</summary>
    public sealed record AdminNavigationArgs(int InstallationId, string RoutePath, string Label, long AdminEpoch);

    internal enum AdminContentState { Loading, Ready, Unavailable, SignInRequired, Forbidden, ServerError, LoadFailed, TimedOut, AuthorityChanged }

    private static TimeSpan s_adminLoadDeadline = TimeSpan.FromSeconds(30); // Shortened only by the native fixture.

    private static readonly TimeSpan AdminAvailabilityInterval = TimeSpan.FromSeconds(30);
    private AdminNavigationArgs? _adminArgs;
    private MonitorAuthorityLease? _adminLease;
    private CancellationTokenRegistration _adminRetired;
    private CancellationTokenSource? _adminAttempt;
    private AdminPluginContentScope? _adminScope;
    private CoreWebView2? _adminCore;
    private bool _adminCoreLost;
    private DispatcherTimer? _adminDeadline;
    private DispatcherTimer? _adminAvailability;
    private bool _adminTornDown;
    private bool _adminAllowBlank;
    private bool _adminLoaded;
    private bool _adminChecking;
    private readonly AdminPluginPageNavigations _adminNavigations = new();
    private readonly HashSet<ulong> _adminRefused = [];
    private Grid? _adminStateHost;
    private TextBlock? _adminStateTitle;
    private TextBlock? _adminStateDetail;
    private Button? _adminReload;
    private Button? _adminReturn;

    /// <summary>The state the page is showing (native regression fixture).</summary>
    internal AdminContentState AdminState { get; private set; } = AdminContentState.Loading;

    private async Task OpenAdminContentAsync(AdminNavigationArgs args)
    {
        _adminArgs = args;
        BuildAdminStateHost();
        ErrorPanel.Visibility = Visibility.Collapsed;
        if (App.MainWindowInstance is MainWindow window)
            window.SetDynamicTitle(AdminPluginText.Plain(args.Label));
        try
        {
            var authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
            _adminLease = authority.Capture(_resourceStop.Token);
        }
        catch (Exception) { ShowAdminState(AdminContentState.AuthorityChanged); return; }
        var lease = _adminLease;
        if (lease.Epoch != args.AdminEpoch || !lease.IsCurrent)
        {
            // A Back/Forward entry from an earlier Admin authority never reopens under a new one.
            ShowAdminState(AdminContentState.AuthorityChanged);
            return;
        }
        _adminRetired = lease.Token.Register(() => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || _adminTornDown) return;
            TeardownAdminContent();
            ClosePluginWebView();
            ShowAdminState(AdminContentState.AuthorityChanged);
        }));
        _adminAvailability = new DispatcherTimer { Interval = AdminAvailabilityInterval };
        _adminAvailability.Tick += async (_, _) => await CheckAdminAvailabilityAsync();
        _adminAvailability.Start();
        await LoadAdminContentAsync();
    }

    /// <summary>One load: re-read the inventory, launch, set the cookie, navigate. Reload runs it again.</summary>
    private async Task LoadAdminContentAsync()
    {
        if (!_active || _adminTornDown || _adminArgs is not { } args || _adminLease is not { } lease) return;
        _adminAttempt?.Cancel();
        _adminAttempt?.Dispose();
        var attempt = _adminAttempt = CancellationTokenSource.CreateLinkedTokenSource(_resourceStop.Token, lease.Token);
        var token = attempt.Token;
        _adminLoaded = false;
        StopAdminDeadline();
        ShowAdminState(AdminContentState.Loading);
        try
        {
            var api = App.Services.GetRequiredService<AdminPluginAppsApi>();
            // Reconcile with the current inventory instead of trusting a detached
            // sidebar entry or a Back/Forward parameter from an earlier read.
            var apps = await api.GetAppsAsync(lease, token);
            if (!IsCurrentAttempt(attempt)) return;
            var app = apps.FirstOrDefault(candidate => candidate.InstallationId == args.InstallationId && candidate.RoutePath == args.RoutePath);
            if (app == null) { BlankAdminContent(); ShowAdminState(AdminContentState.Unavailable); return; }
            var scope = AdminPluginContentScope.Create(lease.Context.BaseUrl, app);
            var cookie = await api.LaunchAsync(lease, scope, token);
            if (!IsCurrentAttempt(attempt)) return;
            var core = await EnsureAdminCoreAsync(lease);
            if (core == null || !IsCurrentAttempt(attempt)) return;
            _adminScope = scope;
            var cookies = core.CookieManager;
            cookies.DeleteAllCookies();
            var issued = cookies.CreateCookie(cookie.Name, cookie.Value, scope.Origin.Host, cookie.Path);
            issued.IsHttpOnly = true;
            issued.IsSecure = cookie.Secure;
            issued.SameSite = CoreWebView2CookieSameSiteKind.Lax;
            issued.Expires = DateTimeOffset.UtcNow.AddSeconds(cookie.MaxAgeSeconds).ToUnixTimeSeconds();
            cookies.AddOrUpdateCookie(issued);
            _adminAllowBlank = false;
            StartAdminDeadline();
            core.Navigate(scope.StartUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            if (!IsCurrentAttempt(attempt)) return;
            BlankAdminContent();
            ShowAdminState(ex switch
            {
                ApiException { StatusCode: 401 } => AdminContentState.SignInRequired,
                ApiException { StatusCode: 403 } => AdminContentState.Forbidden,
                ApiException { StatusCode: 404 } => AdminContentState.Unavailable,
                ApiException { StatusCode: >= 500 or 429 } => AdminContentState.ServerError,
                OperationCanceledException when !lease.IsCurrent => AdminContentState.AuthorityChanged,
                OperationCanceledException => AdminContentState.TimedOut,
                _ => AdminContentState.LoadFailed,
            });
        }
    }

    private bool IsCurrentAttempt(CancellationTokenSource attempt) =>
        _active && !_adminTornDown && ReferenceEquals(_adminAttempt, attempt) && !attempt.IsCancellationRequested && _adminLease?.IsCurrent == true;

    private async Task<CoreWebView2?> EnsureAdminCoreAsync(MonitorAuthorityLease lease)
    {
        if (_adminCore != null) return _adminCore;
        if (_adminCoreLost) ReplaceAdminWebView();
        // The default environment is shared with the viewer's plugin host, but this profile is
        // its own and InPrivate: no cookies, storage or cache survive it, and nothing from the
        // viewer profile or the desktop app's account stores is reachable from it.
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, null, null);
        if (!_active || _adminTornDown || !lease.IsCurrent) return null;
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = "SiloAdminPluginApps";
        options.IsInPrivateModeEnabled = true;
        await PluginWebView.EnsureCoreWebView2Async(environment, options);
        if (!_active || _adminTornDown || !lease.IsCurrent) return null;
        var core = PluginWebView.CoreWebView2;
        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.ClientCertificateRequested += (_, e) => { e.Cancel = true; e.Handled = true; };
        // A crashed or unresponsive web view process shows nothing: drop the plugin session and offer Reload.
        core.ProcessFailed += OnAdminProcessFailed;
        core.NavigationStarting += OnAdminNavigationStarting;
        core.FrameNavigationStarting += OnAdminFrameNavigationStarting;
        core.NavigationCompleted += OnAdminNavigationCompleted;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnAdminWebResourceRequested;
        _adminCore = core;
        return core;
    }

    private void OnAdminProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_adminTornDown) return;
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                // Nothing of the exited browser process can be used again: the next load builds a new web view.
                _adminCore = null;
                _adminCoreLost = true;
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                // The page's own process: the web view itself still works and loads again on Reload.
                break;
            default:
                // GPU, utility and frame processes: the browser recovers by itself and the page is still shown.
                return;
        }
        BlankAdminContent();
        ShowAdminState(AdminContentState.LoadFailed);
    }

    /// <summary>The control whose browser process exited is closed for good; a new one takes its place for the next load.</summary>
    private void ReplaceAdminWebView()
    {
        _adminCoreLost = false;
        if (_webViewClosed || Content is not Grid root) return;
        var lost = PluginWebView;
        var index = root.Children.IndexOf(lost);
        if (index < 0) return;
        var fresh = new Microsoft.UI.Xaml.Controls.WebView2
        {
            FlowDirection = lost.FlowDirection,
            HorizontalAlignment = lost.HorizontalAlignment,
            VerticalAlignment = lost.VerticalAlignment,
            Visibility = Visibility.Collapsed,
        };
        try { lost.Close(); }
        catch (Exception) { }
        root.Children.RemoveAt(index);
        root.Children.Insert(index, fresh);
        PluginWebView = fresh;
    }

    private void OnAdminNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_adminAllowBlank && e.Uri == "about:blank") return;
        if (!_active || _adminTornDown || _adminLease?.IsCurrent != true || _adminScope is not { } scope ||
            !Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) || !scope.AllowsDocument(target))
        {
            // Outside this installation's pages and assets: cancelled, the page stays as it was.
            e.Cancel = true;
            if (_adminRefused.Count >= 64) _adminRefused.Clear();
            _adminRefused.Add(e.NavigationId);
            return;
        }
        // While the page is still opening, the navigations a newer one takes over from are remembered: a same-document
        // navigation (a plugin setting its fragment at startup, once or several times) hands the decision back to them.
        _adminNavigations.Started(e.NavigationId, opening: !_adminLoaded);
        StartAdminDeadline();
    }

    private void OnAdminFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Local frames have no network document of their own; their requests are filtered below.
        if (e.Uri is "about:blank" or "about:srcdoc") return;
        if (_adminScope is not { } scope || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) || !scope.AllowsDocument(target))
            e.Cancel = true;
    }

    private void OnAdminWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var request = e.Request;
        if (_active && !_adminTornDown && _adminLease?.IsCurrent == true && _adminScope is { } scope &&
            Uri.TryCreate(request.Uri, UriKind.Absolute, out var target) && scope.AllowsResource(target, request.Method))
            return; // Sent by the browser exactly as written, with only the launch cookie the server scoped here.
        e.Response = sender.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "Cache-Control: no-store");
    }

    private void OnAdminNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_active || _adminTornDown || _adminLease?.IsCurrent != true) return;
        // A refused link leaves the shown page as it was. A refused redirect of the page being opened is a failure
        // that leaves nothing to show.
        var refused = _adminRefused.Remove(e.NavigationId);
        var completed = refused
            ? new AdminPluginPageNavigations.Result(false, 0)
            : new AdminPluginPageNavigations.Result(e.IsSuccess, e.HttpStatusCode, e.WebErrorStatus == CoreWebView2WebErrorStatus.Timeout);
        // Only the navigation this page started or allowed decides its state; a late completion of
        // an earlier blank or superseded navigation never reports the new load as finished. A same-document
        // navigation made while the page is still opening does not decide either: the navigation that opens the
        // page does, with the result it already completed with, or else when it completes.
        if (_adminAllowBlank || _adminNavigations.Completed(e.NavigationId, completed, opening: !_adminLoaded) is not { } result) return;
        StopAdminDeadline();
        if (result.IsSuccess && result.HttpStatusCode is >= 200 and < 400)
        {
            _adminLoaded = true;
            ShowAdminState(AdminContentState.Ready);
            return;
        }
        // The status decides the page being opened. A page that is already shown ends only as KeepsAdminPage says.
        if (_adminLoaded && (refused || KeepsAdminPage(e))) return;
        BlankAdminContent();
        ShowAdminState(result.HttpStatusCode switch
        {
            401 => AdminContentState.SignInRequired,
            403 => AdminContentState.Forbidden,
            404 or 410 => AdminContentState.Unavailable,
            >= 500 => AdminContentState.ServerError,
            _ => result.TimedOut ? AdminContentState.TimedOut : AdminContentState.LoadFailed,
        });
    }

    /// <summary>Whether a later navigation of a page that is already shown leaves it shown, as the browser tab does.</summary>
    private static bool KeepsAdminPage(CoreWebView2NavigationCompletedEventArgs e) => e.HttpStatusCode switch
    {
        // No HTTP exchange: a same-document navigation (fragment, history) succeeds, and a cancelled one never replaced the page.
        // Anything else is a connection failure with nothing to show.
        0 => e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled,
        // The launch cookie is no longer accepted: only a new launch (Reload) continues.
        401 => false,
        // Every other response is the plugin's to show, its own error pages included.
        _ => true,
    };

    /// <summary>The WebUI drops the entry once the installation is disabled, removed or loses the route; an open page follows.</summary>
    private async Task CheckAdminAvailabilityAsync()
    {
        if (_adminChecking || !_active || _adminTornDown || _adminArgs is not { } args || _adminLease is not { IsCurrent: true } lease) return;
        if (AdminState is not (AdminContentState.Ready or AdminContentState.Loading)) return;
        _adminChecking = true;
        try
        {
            var apps = await App.Services.GetRequiredService<AdminPluginAppsApi>().GetAppsAsync(lease, _resourceStop.Token);
            if (!_active || _adminTornDown || !lease.IsCurrent) return;
            if (!apps.Any(app => app.InstallationId == args.InstallationId && app.RoutePath == args.RoutePath))
            {
                _adminAttempt?.Cancel();
                BlankAdminContent();
                ShowAdminState(AdminContentState.Unavailable);
            }
        }
        catch (Exception) { /* A failed background check keeps the page; the next tick tries again. */ }
        finally { _adminChecking = false; }
    }

    /// <summary>Clears the plugin session and shows nothing from the plugin until a successful reload.</summary>
    private void BlankAdminContent()
    {
        StopAdminDeadline();
        _adminNavigations.Clear();
        _adminLoaded = false;
        _adminScope = null;
        if (_adminCore is not { } core || _webViewClosed) return;
        try
        {
            core.Stop();
            core.CookieManager.DeleteAllCookies();
            _adminAllowBlank = true;
            core.Navigate("about:blank");
        }
        catch (Exception) { }
    }

    private void StartAdminDeadline()
    {
        _adminDeadline ??= CreateAdminDeadline();
        _adminDeadline.Stop();
        _adminDeadline.Start();
    }

    private DispatcherTimer CreateAdminDeadline()
    {
        var timer = new DispatcherTimer { Interval = s_adminLoadDeadline };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_active || _adminTornDown || (AdminState != AdminContentState.Loading && _adminNavigations.Pending == null)) return;
            _adminAttempt?.Cancel();
            BlankAdminContent();
            ShowAdminState(AdminContentState.TimedOut);
        };
        return timer;
    }

    private void StopAdminDeadline() => _adminDeadline?.Stop();

    private void BuildAdminStateHost()
    {
        if (_adminStateHost != null || Content is not Grid root) return;
        _adminStateTitle = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, TextAlignment = TextAlignment.Center };
        _adminStateDetail = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 520, TextAlignment = TextAlignment.Center };
        if (Application.Current.Resources.TryGetValue("SecondaryTextBrush", out var brush) && brush is Microsoft.UI.Xaml.Media.Brush secondary)
            _adminStateDetail.Foreground = secondary;
        _adminReload = new Button { Content = AdminText.T("admin.native.pluginApps.reload"), HorizontalAlignment = HorizontalAlignment.Center };
        _adminReload.Click += async (_, _) => await LoadAdminContentAsync();
        _adminReturn = new Button { Content = AdminText.T("admin.native.pluginApps.returnToAdmin"), HorizontalAlignment = HorizontalAlignment.Center };
        _adminReturn.Click += (_, _) => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Dashboard));
        AutomationProperties.SetAutomationId(_adminStateTitle, "AdminPluginStateTitle");
        AutomationProperties.SetAutomationId(_adminStateDetail, "AdminPluginStateDetail");
        AutomationProperties.SetAutomationId(_adminReload, "AdminPluginReload");
        AutomationProperties.SetAutomationId(_adminReturn, "AdminPluginReturn");
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        panel.Children.Add(_adminStateTitle);
        panel.Children.Add(_adminStateDetail);
        panel.Children.Add(_adminReload);
        panel.Children.Add(_adminReturn);
        _adminStateHost = new Grid { Visibility = Visibility.Collapsed, Background = Background, Tag = "AdminPluginState", Children = { panel } };
        root.Children.Add(_adminStateHost);
    }

    private void ShowAdminState(AdminContentState state)
    {
        AdminState = state;
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingRing.Visibility = state == AdminContentState.Loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = state == AdminContentState.Loading;
        if (!_webViewClosed) PluginWebView.Visibility = state == AdminContentState.Ready ? Visibility.Visible : Visibility.Collapsed;
        if (_adminStateHost == null || _adminStateTitle == null || _adminStateDetail == null || _adminReload == null || _adminReturn == null) return;
        if (state is AdminContentState.Loading or AdminContentState.Ready) { _adminStateHost.Visibility = Visibility.Collapsed; return; }
        var (title, detail) = state switch
        {
            AdminContentState.Unavailable => ("admin.native.pluginApps.unavailable.title", "admin.native.pluginApps.unavailable.detail"),
            AdminContentState.SignInRequired => ("web.common.shell.pluginOpenFailed", "admin.native.pluginApps.signIn.detail"),
            AdminContentState.Forbidden => ("web.common.shell.pluginOpenFailed", "admin.native.pluginApps.forbidden.detail"),
            AdminContentState.ServerError => ("web.common.shell.pluginOpenFailed", "admin.native.pluginApps.server.detail"),
            AdminContentState.TimedOut => ("web.common.shell.pluginOpenFailed", "admin.native.pluginApps.timedOut.detail"),
            AdminContentState.AuthorityChanged => ("admin.native.pluginApps.authority.title", "admin.native.pluginApps.authority.detail"),
            _ => ("web.common.shell.pluginOpenFailed", "admin.native.pluginApps.loadFailed.detail"),
        };
        _adminStateTitle.Text = AdminText.T(title);
        _adminStateDetail.Text = AdminText.T(detail);
        var reloadable = state != AdminContentState.AuthorityChanged && !_adminTornDown;
        _adminReload.Visibility = reloadable ? Visibility.Visible : Visibility.Collapsed;
        _adminReturn.Visibility = reloadable ? Visibility.Collapsed : Visibility.Visible;
        _adminStateHost.Visibility = Visibility.Visible;
    }

    /// <summary>Leaving, unloading or an authority change: stop everything and drop the plugin session.</summary>
    private void TeardownAdminContent()
    {
        if (_adminArgs == null || _adminTornDown) return;
        _adminTornDown = true;
        _adminAvailability?.Stop();
        StopAdminDeadline();
        _adminAttempt?.Cancel();
        _adminAttempt?.Dispose();
        _adminAttempt = null;
        _adminRetired.Dispose();
        _adminScope = null;
        if (_adminCore is { } core && !_webViewClosed)
        {
            try { core.Stop(); core.CookieManager.DeleteAllCookies(); }
            catch (Exception) { }
        }
        _adminLease?.Dispose();
        _adminLease = null;
    }
}
