using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;
using X = SiloPlayer.Core.Services.Admin.AdminNetworkAccessText;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure › Server › Network access (official NetworkAccessSettings.tsx): every installed
/// overlay-network provider with its state on each host, and per-host Connect/Disconnect.
/// A tool page: no settings draft or SaveBar. Rows are updated in place across polls so focus
/// and hover survive, and the status poll runs only while this page and its window are in front.
/// </summary>
public sealed partial class AdminNetworkAccessSettingsPage : Page, IAdminGutterScrollPage, IAdminPageSuspension
{
    private sealed class ScrollPosition { internal double Offset; }
    private static readonly ConditionalWeakTable<AdminRouteRequest, ScrollPosition> ScrollPositions = new();
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _header = new() { Spacing = 8, Margin = new(0, 0, 0, 32) };
    private readonly TextBlock _intro = U.Text(X.T("web.admin.settings.network.page.intro"), 14, true);
    private readonly StackPanel _loading = new() { Spacing = 12 };
    private readonly StackPanel _alert = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _alertText = U.Text("", 14);
    private readonly Button _retry = U.Action(X.T("configure.native.network.retry"), "NetworkAccessRetry");
    private readonly AdminSettingsGroup _empty = new(X.T("web.admin.settings.network.page.noProvidersTitle"));
    private readonly StackPanel _groups = new();
    private readonly Dictionary<string, NetworkAccessProviderView> _views = new(StringComparer.Ordinal);
    private readonly AdminMonitorAuthority _authority;
    private AdminNetworkAccessController? _controller;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _navigated, _hostVisible = true, _hostFocused = true, _opening;

    public FrameworkElement ScrollContent => _scrollContent;
    public AdminNetworkAccessController? Controller => _controller;
    /// <summary>
    /// Opens a guarded external link. Production uses the system launcher; native fixtures replace it so a
    /// synthetic enrollment URL is captured and never opened.
    /// </summary>
    public Func<Uri, Task<bool>> Launcher { get; set; } = async uri => await Windows.System.Launcher.LaunchUriAsync(uri);

    public AdminNetworkAccessSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminNetworkAccessSettingsPage");
        AutomationProperties.SetName(_scroller, X.T("web.admin.settings.layout.regionLabel", new { label = X.T("web.admin.settings.search.networkAccess.label") }));
        _header.Children.Add(U.Title(X.T("web.admin.settings.network.page.title")));
        var subtitle = U.Text(X.T("web.admin.settings.search.networkAccess.description"), 14, true);
        subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        _header.Children.Add(subtitle);
        _body.Children.Add(_header);
        // max-w-prose, text-sm muted, then the space-y-6 gap before the providers.
        _intro.MaxWidth = 722; _intro.LineHeight = 20; _intro.HorizontalAlignment = HorizontalAlignment.Left; _intro.Margin = new(0, 0, 0, 24);
        AutomationProperties.SetAutomationId(_intro, "NetworkAccessIntro");
        _body.Children.Add(_intro);

        AutomationProperties.SetName(_loading, X.T("configure.native.network.loadingProviders"));
        AutomationProperties.SetLiveSetting(_loading, AutomationLiveSetting.Polite);
        _loading.Children.Add(NetworkAccessUi.FractionSkeleton(24, 1d / 3));
        _loading.Children.Add(U.Skeleton(80));
        _body.Children.Add(_loading);

        AutomationProperties.SetAutomationId(_alertText, "NetworkAccessProvidersError");
        _alertText.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetLiveSetting(_alertText, AutomationLiveSetting.Assertive);
        _retry.HorizontalAlignment = HorizontalAlignment.Left;
        _retry.Click += async (_, _) => { if (_controller != null) await _controller.RetryCapabilitiesAsync(); };
        _alert.Children.Add(_alertText); _alert.Children.Add(_retry);
        _body.Children.Add(_alert);

        var emptyBody = U.Text(X.T("web.admin.settings.network.page.noProvidersBody"), 14, true);
        AutomationProperties.SetAutomationId(emptyBody, "NetworkAccessNoProviders");
        _empty.Add(emptyBody);
        _empty.Visibility = Visibility.Collapsed;
        _body.Children.Add(_empty);
        _body.Children.Add(_groups);

        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; _root.Children.Add(_scroller);
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        Content = _root;
        Loaded += (_, _) => { FitScrollContent(); if (_navigated) Activate(); };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) Close(); });
        Render(AdminNetworkAccessState.Closed);
    }

    private void FitScrollContent()
    {
        var available = _scroller.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (available <= 0) return;
        _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, available);
        // The settings column is left-aligned and always 1120 wide when space allows, whatever its content (loading or empty).
        _body.Width = Math.Min(1120, _scrollContent.Width);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && ScrollPositions.TryGetValue(_location, out var position) ? position.Offset : 0;
        _navigated = true;
        base.OnNavigatedTo(e);
        if (IsLoaded) Activate();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); _navigated = false; base.OnNavigatedFrom(e); }

    /// <summary>Outer Admin departure: retire reads, commands and enrollment links.</summary>
    public void SuspendAdminPage() => Close();

    private void Activate()
    {
        if (_active) return;
        _active = true;
        var player = App.Services.GetService<PlayerService>();
        _controller = new AdminNetworkAccessController(App.Services.GetRequiredService<AdminNetworkAccessApi>(), _authority,
            deferBackground: () => player?.IsLoading == true);
        _controller.Changed += QueueRender;
        _controller.AuthorityRetired += OnAuthorityRetired;
        _controller.CommandFailed += OnCommandFailed;
        if (App.MainWindowInstance is { } window) { window.Activated += OnWindowActivated; window.VisibilityChanged += OnWindowVisibilityChanged; _hostVisible = window.Visible; }
        if (XamlRoot != null) { XamlRoot.Changed += OnXamlRootChanged; _hostVisible &= XamlRoot.IsHostVisible; }
        _controller.SetActive(_hostVisible && _hostFocused);
        OpenController();
    }

    private void OpenController()
    {
        if (_opening || !_active || _controller == null || !_authority.IsAvailable) return;
        _opening = true;
        try { _controller.Open(); }
        finally { _opening = false; }
    }

    private void OnAuthorityRetired() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        // A replaced authority never inherits rows, busy hosts or enrollment links; reopen only if still allowed.
        ClearRows();
        OpenController();
    });

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _hostFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        _controller?.SetActive(_active && _hostVisible && _hostFocused);
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _hostVisible = args.Visible && (XamlRoot?.IsHostVisible ?? true);
        _controller?.SetActive(_active && _hostVisible && _hostFocused);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _hostVisible = sender.IsHostVisible && (App.MainWindowInstance?.Visible ?? true);
        _controller?.SetActive(_active && _hostVisible && _hostFocused);
    }

    /// <summary>Window activation and visibility as the page observes them; native fixtures drive this directly.</summary>
    public void SetHostActivity(bool visible, bool focused)
    {
        _hostVisible = visible; _hostFocused = focused;
        _controller?.SetActive(_active && visible && focused);
    }

    private void QueueRender()
    {
        var controller = _controller;
        if (controller == null) return;
        DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(controller.State); });
    }

    private void Render(AdminNetworkAccessState state)
    {
        var current = state.Owner?.IsCurrent == true && state.IsOpen;
        _loading.Visibility = current && state.CapabilitiesLoading ? Visibility.Visible : Visibility.Collapsed;
        var failed = current && state.CapabilitiesFailure != null;
        _alert.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed) U.UpdateLiveText(_alertText, CapabilitiesMessage(state));
        _retry.IsEnabled = failed && !state.CapabilitiesLoading;
        _empty.Visibility = current && state.CapabilitiesLoaded && state.Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderProviders(current ? state.Providers : []);
        if (current && state.CapabilitiesLoaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); } });
        }
    }

    private void RenderProviders(IReadOnlyList<AdminNetworkAccessProviderState> providers)
    {
        var wanted = providers.Select(provider => provider.Summary.Provider).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _views.Keys.Where(key => !wanted.Contains(key)).ToArray())
        {
            _groups.Children.Remove(_views[gone].Group);
            _views[gone].Clear();
            _views.Remove(gone);
        }
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index];
            if (!_views.TryGetValue(provider.Summary.Provider, out var view))
            {
                view = new NetworkAccessProviderView(provider.Summary, OnCommand, OnLink);
                _views[provider.Summary.Provider] = view;
            }
            // Move a group only when the server's order changed; unchanged groups keep their elements and focus.
            var existing = _groups.Children.IndexOf(view.Group);
            if (existing != index)
            {
                if (existing >= 0) _groups.Children.RemoveAt(existing);
                _groups.Children.Insert(Math.Min(index, _groups.Children.Count), view.Group);
            }
            view.Render(provider);
        }
    }

    private async void OnCommand(string provider, string hostId, AdminNetworkAccessCommandKind kind)
    {
        var controller = _controller;
        if (!_active || controller == null) return;
        if (kind == AdminNetworkAccessCommandKind.Connect) await controller.ConnectAsync(provider, hostId);
        else await controller.DisconnectAsync(provider, hostId);
    }

    /// <summary>
    /// A deliberate link activation. The URL is re-read from the current owner's state at click time, must still be
    /// a safe HTTP(S) link (and, for enrollment, belong to a host waiting for authorization), and is never logged.
    /// </summary>
    private async void OnLink(string provider, string hostId, NetworkAccessLink link)
    {
        var controller = _controller;
        if (!_active || controller == null) return;
        Uri uri;
        var allowed = link == NetworkAccessLink.Enrollment
            ? controller.TryGetEnrollmentUri(provider, hostId, out uri)
            : controller.TryGetOriginUri(provider, hostId, out uri);
        if (!allowed) return;
        try { await Launcher(uri); } catch (Exception) { /* A launcher failure exposes nothing and changes no state. */ }
    }

    private void OnCommandFailed(AdminNetworkAccessCommandFeedback feedback)
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || !ReferenceEquals(controller, _controller) || controller?.State.Owner?.IsCurrent != true) return;
            App.Services.GetService<ToastService>()?.Error(CommandMessage(feedback));
        });
    }

    internal static string CommandMessage(AdminNetworkAccessCommandFeedback feedback)
    {
        var connect = feedback.Kind == AdminNetworkAccessCommandKind.Connect;
        if (feedback.Outcome == AdminNetworkAccessCommandOutcome.Unknown)
            return X.T(connect ? "configure.native.network.connectUnknown" : "configure.native.network.disconnectUnknown");
        return feedback.Failure switch
        {
            AdminFailureKind.Validation => X.T("configure.native.network.unknownHost"),
            AdminFailureKind.NotFound => X.T("configure.native.network.providerNotFound"),
            _ when FailureMessage(feedback.Failure) is { } message => message,
            _ => X.T(connect ? "web.admin.settings.hooks.networkAccess.connectFailed" : "web.admin.settings.hooks.networkAccess.disconnectFailed"),
        };
    }

    internal static string? FailureMessage(AdminFailureKind? failure) => failure switch
    {
        AdminFailureKind.Unauthorized => X.T("configure.native.network.authenticationRequired"),
        AdminFailureKind.Forbidden or AdminFailureKind.DemoRestricted => X.T("configure.native.network.forbidden"),
        AdminFailureKind.ProfileVerificationRequired => X.T("configure.native.network.profileVerification"),
        AdminFailureKind.CapabilityUnavailable => X.T("configure.native.network.unavailable"),
        AdminFailureKind.RateLimited => X.T("configure.native.network.rateLimited"),
        AdminFailureKind.InvalidResponse => X.T("configure.native.network.invalidResponse"),
        AdminFailureKind.Transport => X.T("configure.native.network.transport"),
        _ => null,
    };

    private static string CapabilitiesMessage(AdminNetworkAccessState state) => state.CapabilitiesLimit is { } limit
        ? X.T("configure.native.network.limit", new { limit })
        : FailureMessage(state.CapabilitiesFailure) is { } message && state.CapabilitiesFailure is not (AdminFailureKind.InvalidResponse or AdminFailureKind.Transport)
            ? message : X.T("web.admin.settings.network.page.providersFailed");

    private void ClearRows()
    {
        foreach (var view in _views.Values) view.Clear();
        _views.Clear();
        _groups.Children.Clear();
    }

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (_location != null) ScrollPositions.GetValue(_location, static _ => new ScrollPosition()).Offset = _scroller.VerticalOffset;
        if (App.MainWindowInstance is { } window) { window.Activated -= OnWindowActivated; window.VisibilityChanged -= OnWindowVisibilityChanged; }
        if (XamlRoot != null) XamlRoot.Changed -= OnXamlRootChanged;
        // Disposing retires the owner: pending reads and commands are cancelled and their late results discarded.
        if (_controller != null)
        {
            _controller.Changed -= QueueRender; _controller.AuthorityRetired -= OnAuthorityRetired; _controller.CommandFailed -= OnCommandFailed;
            _controller.Dispose(); _controller = null;
        }
        ClearRows();
        Render(AdminNetworkAccessState.Closed);
    }
}
