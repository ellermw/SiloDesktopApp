using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using U =SiloPlayer.Controls.AdminSettingsUi;
using X = SiloPlayer.Core.Services.Admin.AdminNetworkAccessText;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure › Server › Network access (official NetworkAccessSettings.tsx): every installed
/// overlay-network provider with its state on each host, and per-host Connect/Disconnect.
/// A tool page: no settings draft or SaveBar. Rows are updated in place across polls so focus
/// and hover survive, and the status poll runs only while this page and its window are in front.
/// </summary>
public sealed partial class AdminNetworkAccessSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
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
    private MonitorAuthorityLease? _renderOwner;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _navigated, _hostVisible = true, _hostFocused = true, _opening, _directOpening;
    // The Direct connections section: its own controller (settings form and status), the shared save bar and the leave guard.
    private readonly NetworkAccessDirectSection _direct;
    private readonly AdminSettingsSaveBar _saveBar = new("NetworkAccess");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Border _scrim = new() { Height = 160, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private AdminDirectConnectionsController? _directController;
    private string? _shownSaveMessage;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private static readonly Lazy<LoggingDisclosurePreferences> DefaultPreferences = new(() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "monitor-ui")));

    public FrameworkElement ScrollContent => _scrollContent;
    public AdminNetworkAccessController? Controller => _controller;
    public AdminDirectConnectionsController? DirectController => _directController;
    internal NetworkAccessDirectSection DirectSection => _direct;
    internal AdminSettingsSaveBar SaveBar => _saveBar;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    public Task<bool> CanLeaveAsync()
    {
        if (_directController?.State is { Saving: true, Owner.IsCurrent: true }) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }
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
        // After the providers, so the intro above stays next to the list it describes (official: DirectConnectionsSection last).
        _direct = new NetworkAccessDirectSection(() => _directController, () => App.Services.GetService<LoggingDisclosurePreferences>() ?? DefaultPreferences.Value);
        _body.Children.Add(_direct);
        _body.Children.Add(_spacer);

        _scrollContent.Children.Add(_body); _scroller.Content = _scrollContent; _root.Children.Add(_scroller);
        _scroller.SizeChanged += (_, _) => FitScrollContent();
        var background = AdminMonitorUi.ColorOf("AppBackgroundBrush", Microsoft.UI.Colors.Black);
        _scrim.Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(0, 1), GradientStops =
            {
                new GradientStop { Color = AdminMonitorUi.WithAlpha(background, 0), Offset = 0 },
                new GradientStop { Color = background, Offset = 1 },
            },
        };
        _root.Children.Add(_scrim); _root.Children.Add(_saveBar);
        _saveBar.Save.Click += async (_, _) => { if (_directController != null) await _directController.SaveAsync(); };
        _saveBar.Discard.Click += (_, _) => _directController?.Discard();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, (XamlRoot?.Size.Width ?? ActualWidth) >= 1024 ? -8 : 8); };
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
        _directController = App.Services.GetRequiredService<AdminDirectConnectionsController>();
        _leave = new(() => _directController?.State.Owner, () => _directController?.State.DirtyKeys.Count > 0, () => _directController?.Discard());
        _directController.Changed += QueueDirectRender; _directController.Saved += OnDirectSaved; _directController.AuthorityRetired += OnDirectAuthorityRetired;
        _directController.SetActive(_hostVisible && _hostFocused);
        OpenController();
        _ = OpenDirectAsync();
    }

    private async Task OpenDirectAsync()
    {
        if (_directOpening || !_active || _directController == null || !_authority.IsAvailable) return;
        _directOpening = true;
        try { await _directController.OpenAsync(); }
        finally { _directOpening = false; }
    }

    private void OnDirectAuthorityRetired() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        _direct.Reset();
        _ = OpenDirectAsync();
    });

    private void QueueDirectRender()
    {
        var controller = _directController;
        if (controller == null) return;
        var state = controller.State;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && ReferenceEquals(controller, _directController) && ReferenceEquals(state, controller.State)) RenderDirect(state);
        });
    }

    private void RenderDirect(AdminDirectConnectionsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        _direct.Render(state);
        // The official save reports a refusal as a toast; the message also stays in the section, next to what was staged.
        if (_direct.SaveMessage != _shownSaveMessage)
        {
            _shownSaveMessage = _direct.SaveMessage;
            if (_shownSaveMessage != null) App.Services.GetService<ToastService>()?.Error(_shownSaveMessage);
        }
        var dirty = current && state.SectionVisible ? state.DirtyKeys.Count : 0;
        _saveBar.State(dirty, state.Saving, current && state.CanSave);
        _saveBar.SetAvailableWidth(ActualWidth);
        _spacer.Visibility = _scrim.Visibility = _saveBar.Visibility;
    }

    private void OnDirectSaved(IReadOnlyCollection<string> keys)
    {
        var controller = _directController;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active || controller == null || !ReferenceEquals(controller, _directController) || controller.State.Owner?.IsCurrent != true) return;
            if (App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus();
        });
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
        _controller?.SetActive(_active && _hostVisible && _hostFocused); _directController?.SetActive(_active && _hostVisible && _hostFocused);
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _hostVisible = args.Visible && (XamlRoot?.IsHostVisible ?? true);
        _controller?.SetActive(_active && _hostVisible && _hostFocused); _directController?.SetActive(_active && _hostVisible && _hostFocused);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _hostVisible = sender.IsHostVisible && (App.MainWindowInstance?.Visible ?? true);
        _controller?.SetActive(_active && _hostVisible && _hostFocused); _directController?.SetActive(_active && _hostVisible && _hostFocused);
    }

    /// <summary>Window activation and visibility as the page observes them; native fixtures drive this directly.</summary>
    public void SetHostActivity(bool visible, bool focused)
    {
        _hostVisible = visible; _hostFocused = focused;
        _controller?.SetActive(_active && visible && focused); _directController?.SetActive(_active && visible && focused);
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
        if (!ReferenceEquals(_renderOwner, state.Owner)) { ClearRows(); _renderOwner = state.Owner; }
        _loading.Visibility = current && state.CapabilitiesLoading ? Visibility.Visible : Visibility.Collapsed;
        var failed = current && state.CapabilitiesFailure != null;
        _alert.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed) U.UpdateLiveText(_alertText, CapabilitiesMessage(state));
        _retry.IsEnabled = failed && !state.CapabilitiesLoading;
        _empty.Visibility = current && state.CapabilitiesLoaded && state.Providers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderProviders(current ? state.Providers : [], current ? state.Owner : null);
        if (current && state.CapabilitiesLoaded && _restoreOffset is { } offset)
        {
            _restoreOffset = null;
            DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroller.ChangeView(null, offset, null, true); } });
        }
    }

    private void RenderProviders(IReadOnlyList<AdminNetworkAccessProviderState> providers, MonitorAuthorityLease? owner)
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
                view = new NetworkAccessProviderView(provider.Summary,
                    (providerId, hostId, kind) => OnCommand(owner, providerId, hostId, kind),
                    (providerId, hostId, link) => OnLink(owner, providerId, hostId, link));
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

    private bool IsRenderedOwnerCurrent(MonitorAuthorityLease? owner) =>
        _active && owner?.IsCurrent == true && ReferenceEquals(owner, _controller?.State.Owner);

    private async void OnCommand(MonitorAuthorityLease? owner, string provider, string hostId, AdminNetworkAccessCommandKind kind)
    {
        var controller = _controller;
        if (controller == null || !IsRenderedOwnerCurrent(owner)) return;
        if (kind == AdminNetworkAccessCommandKind.Connect) await controller.ConnectAsync(provider, hostId, owner);
        else await controller.DisconnectAsync(provider, hostId, owner);
    }

    /// <summary>
    /// A deliberate link activation. The URL is re-read from the current owner's state at click time, must still be
    /// a safe HTTP(S) link (and, for enrollment, belong to a host waiting for authorization), and is never logged.
    /// </summary>
    private async void OnLink(MonitorAuthorityLease? owner, string provider, string hostId, NetworkAccessLink link)
    {
        var controller = _controller;
        if (controller == null || !IsRenderedOwnerCurrent(owner)) return;
        Uri uri;
        var allowed = link == NetworkAccessLink.Enrollment
            ? controller.TryGetEnrollmentUri(provider, hostId, out uri)
            : controller.TryGetOriginUri(provider, hostId, out uri);
        if (!allowed || !IsRenderedOwnerCurrent(owner)) return;
        try { await Launcher(uri); } catch (Exception) { /* A launcher failure exposes nothing and changes no state. */ }
    }

    private void OnCommandFailed(AdminNetworkAccessCommandFeedback feedback)
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(controller, _controller) || !IsRenderedOwnerCurrent(feedback.Owner)) return;
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
        // The Direct connections controller owns its own reads, polling and draft; disposing retires them all.
        _dialog?.Hide(); _dialog = null;
        if (_directController != null)
        {
            _directController.Changed -= QueueDirectRender; _directController.Saved -= OnDirectSaved; _directController.AuthorityRetired -= OnDirectAuthorityRetired;
            _directController.Dispose(); _directController = null;
        }
        _leave = null; _directOpening = false;
        _direct.Reset();
        _saveBar.Visibility = _scrim.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
