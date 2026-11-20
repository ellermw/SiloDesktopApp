using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using U = SiloPlayer.Controls.AdminSettingsUi;
using C = SiloPlayer.Core.Services.Admin.AdminNotificationSettingsController;

namespace SiloPlayer.Views.Admin;

/// <summary>Configure > Integrations > Notifications. Page-owned lifetime, existing gutter/leave
/// guard and no navigation motion. Official NotificationsAdminSettings.tsx / ServerNotificationChannels.tsx.</summary>
public sealed partial class AdminNotificationSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed class Position { internal double Offset; }
    private sealed record HiddenDraft(long Epoch, string AccountId, Dictionary<string, string> Values, string? RelayUrl, bool Credentials);
    private static readonly ConditionalWeakTable<AdminRouteRequest, Position> Positions = new();
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 20 };
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly TextBlock _title = U.Title(N("title"));
    private readonly StackPanel _loading = new() { Spacing = 16 };
    private readonly TextBlock _error = U.Text("", 13), _paused = U.Text("", 13);
    private readonly Border _errorAlert;
    private readonly Button _reload = U.Action(N("reload"), "NotificationsReload");
    private readonly AdminSettingsSaveBar _saveBar = new("Notifications");
    private readonly Border _spacer = new() { Height = 112, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _descriptions = new(StringComparer.Ordinal), _secretSynced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _chips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expander> _advanced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ToggleSwitch Toggle, string[] Keys, Expander? Expander)> _channels = new(StringComparer.Ordinal);
    private readonly List<(Button Button, Func<AdminNotificationSettingsState, bool> Enabled)> _actions = [];
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminSettingsGroup _delivery = new(N("delivery"), N("deliveryDescription"));
    private readonly AdminSettingsGroup _processing = new(N("processing.label"), N("processing.description"));
    private AdminNotificationSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private ContentDialog? _dialog;
    private XamlRoot? _visibilityRoot;
    private Window? _visibilityWindow;
    private long? _visibilityToken;
    private bool _windowVisible = true;
    private HiddenDraft? _hiddenDraft;
    private System.Action? _clearDialogPrivate;
    private AdminRouteRequest? _location;
    private double? _restoreOffset;
    private bool _active, _sync, _opening, _hideDeferred;
    public C? Controller => _controller;
    public FrameworkElement ScrollContent => _scrollContent;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true || HeldTyping != null || LeaveRefused;
    // A write the server may already have, a signing secret not yet dismissed and an open dialog keep the page on
    // every departure path: Back to App and window close must reach CanLeaveAsync as in-admin navigation does.
    private bool LeaveRefused => _dialog != null || _secretShown || _controller?.State is { Saving: true } or { OperationPending: true } || _controller?.HasSigningSecret == true;
    // Typing held across a host hide stays this authority's unsaved work until it is restaged; another authority never inherits it.
    private HiddenDraft? HeldTyping => _hiddenDraft is { } draft && (draft.Values.Count > 0 || draft.RelayUrl != null) && _authority.IsAvailable && _authority.Epoch == draft.Epoch ? draft : null;

    public AdminNotificationSettingsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminNotificationSettingsPage");
        AutomationProperties.SetName(_scroll, N("title"));
        _body.Children.Add(_title);
        _loading.Children.Add(U.Skeleton(28, 176)); for (var i = 0; i < 4; i++) _loading.Children.Add(U.Skeleton(72));
        _body.Children.Add(_loading);
        AutomationProperties.SetAutomationId(_error, "NotificationsError"); _errorAlert = U.Alert(_error); _errorAlert.Visibility = Visibility.Collapsed; _body.Children.Add(_errorAlert);
        _reload.Click += async (_, _) => { if (_controller != null) await _controller.ReloadAsync(true); }; _reload.Visibility = Visibility.Collapsed; _body.Children.Add(_reload);
        AutomationProperties.SetLiveSetting(_paused, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); _fields.Children.Add(_paused);
        _fields.Children.Add(_delivery); BuildDelivery(); _fields.Children.Add(_processing); BuildProcessing();
        _fields.Visibility = Visibility.Collapsed; _body.Children.Add(_fields); _body.Children.Add(_spacer); _scrollContent.Children.Add(_body); _scroll.Content = _scrollContent;
        _root.Children.Add(_scroll); _root.Children.Add(_saveBar); Content = _root;
        _saveBar.Save.Click += async (_, _) => { if (_controller != null) await _controller.SaveAsync(); };
        _saveBar.Discard.Click += (_, _) => _controller?.DiscardPage();
        _scroll.SizeChanged += (_, _) => Fit();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); _saveBar.Margin = new(16, 0, 16, 8); };
        Loaded += (_, _) => { Fit(); ObserveHost(); }; Unloaded += (_, _) => { StopObservingHost(); Close(); };
    }
    private static string N(string key, params (string Name, object? Value)[] values) => AdminNotificationText.T("web.admin.settings.notifications." + key, values);
    private static string T(string key, params (string Name, object? Value)[] values) => AdminNotificationText.T(key, values);
    private void Fit()
    {
        var width = _scroll.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (width > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, width);
    }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreOffset = e.NavigationMode == NavigationMode.Back && _location != null && Positions.TryGetValue(_location, out var pos) ? pos.Offset : 0;
        base.OnNavigatedTo(e);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { StopObservingHost(); Close(); base.OnNavigatedFrom(e); }
    private bool HostVisible => IsLoaded && Visibility == Visibility.Visible && _visibilityRoot?.IsHostVisible == true && _windowVisible;
    private void ObserveHost()
    {
        StopObservingHost();
        _visibilityRoot = XamlRoot;
        if (_visibilityRoot != null) _visibilityRoot.Changed += OnHostRootChanged;
        if (App.MainWindowInstance is { Content: FrameworkElement content } window && ReferenceEquals(content.XamlRoot, XamlRoot))
        {
            _visibilityWindow = window; _windowVisible = window.Visible;
            window.VisibilityChanged += OnHostWindowVisibilityChanged;
        }
        _visibilityToken = RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => ApplyHostVisibility());
        ApplyHostVisibility();
    }
    private void StopObservingHost()
    {
        if (_visibilityRoot != null) _visibilityRoot.Changed -= OnHostRootChanged;
        if (_visibilityWindow != null) _visibilityWindow.VisibilityChanged -= OnHostWindowVisibilityChanged;
        if (_visibilityToken is { } token) UnregisterPropertyChangedCallback(VisibilityProperty, token);
        _visibilityRoot = null; _visibilityWindow = null; _visibilityToken = null; _windowVisible = true;
        // Only a hide/restore of the observed host carries typing over; every departure drops it.
        _hiddenDraft = null;
    }
    private void OnHostRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyHostVisibility();
    private void OnHostWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _windowVisible = args.Visible; ApplyHostVisibility();
    }
    private void ApplyHostVisibility()
    {
        if (HostVisible)
        {
            // A page kept through a hide now shows what its owner received meanwhile.
            if (_hideDeferred) { _hideDeferred = false; QueueRender(); }
            Activate(); return;
        }
        // Hiding retires the owner and all private UI. Nonsecret typing is held for the same unchanged authority only.
        if (_active && _controller?.State is { Loaded: true, Owner: { IsCurrent: true } owner } state)
        {
            // A write the server may already have, and a signing secret only this owner can show, are never
            // retired by a hide: the page stays owned until its host is visible again.
            if (_hideDeferred || C.InterruptedWrite(state) != null || _secretShown || _controller.HasSigningSecret) { _hideDeferred = true; return; }
            var values = state.DirtyKeys.Where(k => !C.Secrets.Contains(k)).ToDictionary(k => k, k => state.Values.GetValueOrDefault(k, ""), StringComparer.Ordinal);
            // Typed credentials are dropped; that is reported on return.
            var credentials = state.DirtyKeys.Any(C.Secrets.Contains) || UnflushedSecrets();
            if (values.Count > 0 || state.RelayUrlDirty || credentials) _hiddenDraft = new(owner.Epoch, owner.AccountId, values, state.RelayUrlDirty ? state.RelayUrl : null, credentials);
        }
        Close();
    }
    private void Activate()
    {
        if (_active || !HostVisible) return; _active = true; _controller = App.Services.GetRequiredService<C>();
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.Dirty == true || UnflushedSecrets(), () => _controller?.Discard());
        _controller.Changed += QueueRender; _controller.Saved += OnSaved; _authority.AvailabilityChanged += AuthorityChanged; _ = OpenAsync();
    }
    private bool UnflushedSecrets() => _controls.Any(p => p.Value is PasswordBox box && box.Password != _secretSynced.GetValueOrDefault(p.Key, ""));
    private async Task OpenAsync()
    {
        if (_opening || !_active || _controller == null || !_authority.IsAvailable) return;
        _opening = true;
        try { await _controller.OpenAsync(); }
        finally { _opening = false; if (_active && _authority.IsAvailable && _controller?.State.Owner == null) DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); }); }
    }
    private void AuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active || _controller?.State.Owner != null) return;
        // A page kept through a hide has nothing left to deliver once its authority is gone; it reopens when visible.
        if (!HostVisible) { Close(); return; }
        RetirePrivateUi(); _ = OpenAsync();
    });
    private void QueueRender()
    {
        var controller = _controller; if (controller == null) return; var state = controller.State;
        DispatcherQueue.TryEnqueue(() => { if (_active && IsLoaded && ReferenceEquals(controller, _controller) && ReferenceEquals(state, controller.State)) Render(state); });
    }
    private static bool On(AdminNotificationSettingsState s, string key) => C.Display(key, s.Values.GetValueOrDefault(key)) == "true";
    private void Render(AdminNotificationSettingsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) RetirePrivateUi();
        if (current && state.Loaded && !state.Loading && _hiddenDraft is { } draft)
        {
            _hiddenDraft = null;
            if (state.Owner!.Epoch == draft.Epoch && state.Owner.AccountId == draft.AccountId)
            {
                // Restage before drawing; this snapshot is stale once the held typing returns.
                foreach (var (name, staged) in draft.Values) _controller?.Set(name, staged);
                if (draft.RelayUrl != null) _controller?.SetRelayUrl(draft.RelayUrl);
                _controller?.RestoreInterrupted(null, draft.Credentials);
                QueueRender(); return;
            }
        }
        _loading.Visibility = state.Loading && !state.Loaded ? Visibility.Visible : Visibility.Collapsed;
        _title.Visibility = _loading.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _fields.Visibility = current && state.Loaded && !state.AccessBlocked ? Visibility.Visible : Visibility.Collapsed;
        var message = state.SensitiveStatusError ? T("configure.native.settings.managedStatusUnavailable") : state.Error ?? "";
        if (state.AccessBlocked) message = T(state.Failure == AdminFailureKind.Unauthorized ? "configure.native.settings.authenticationRequired" : "configure.native.settings.forbidden");
        U.UpdateLiveText(_error, message); _errorAlert.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _reload.Visibility = message.Length > 0 || state.OperationUnknown ? Visibility.Visible : Visibility.Collapsed;
        _reload.IsEnabled = current && !state.Saving && !state.OperationPending && !state.Loading && !state.ChannelsLoading;
        var editable = current && state.Loaded && !state.Loading && !state.AccessBlocked;
        _sync = true;
        try
        {
            foreach (var (key, control) in _controls)
            {
                var value = current ? state.Values.GetValueOrDefault(key, "") : "";
                if (control is PasswordBox password)
                {
                    if (!current || password.Password == _secretSynced.GetValueOrDefault(key, "")) { password.Password = value; _secretSynced[key] = value; }
                    password.PlaceholderText = state.Configured.Contains(key) ? "••••••••••••" : T("web.settings.secretField.notConfigured");
                }
                else if (control is TextBox text)
                {
                    // A staged value shows verbatim, and a display default never replaces a box emptied for typing.
                    var display = state.DirtyKeys.Contains(key) ? value : C.Display(key, value);
                    if (text.Text != display && (value.Length > 0 || text.Text.Length > 0 || text.FocusState == FocusState.Unfocused)) text.Text = display;
                }
                else if (control is ComboBox combo) combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == C.Display(key, value));
                else if (control is ToggleSwitch toggle) toggle.IsOn = C.Display(key, value) == "true";
                control.IsEnabled = editable && !state.ManagedByEnv.Contains(key) && (!C.DiscordKeys.Contains(key) || !state.SensitiveStatusError && !state.Saving && !state.OperationPending);
                var managed = state.ManagedByEnv.Contains(key);
                var description = managed ? T("configure.native.settings.managedByEnvironment") : _descriptions.GetValueOrDefault(key, "");
                _rows[key].Description.Text = description; _rows[key].Description.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                _rows[key].State(state.DirtyKeys.Contains(key) && !C.Secrets.Contains(key), state.RestartKeys.Has(key));
            }
            foreach (var (_, channel) in _channels)
            {
                channel.Toggle.IsOn = channel.Keys.Any(k => On(state, k));
                channel.Toggle.IsEnabled = editable && channel.Keys.All(k => !state.ManagedByEnv.Contains(k));
            }
            if (_relayUrl.Text != state.RelayUrl) _relayUrl.Text = state.RelayUrl;
            _relayUrl.IsEnabled = editable && !state.SensitiveStatusError && !state.ManagedByEnv.Contains("notifications.push_relay_url");
        }
        finally { _sync = false; }
        U.UpdateLiveText(_paused, !On(state, "notifications.release_events_enabled") ? N("pausedRecording") : !On(state, "notifications.fanout_enabled") ? N("pausedSending") : "");
        _paused.Visibility = _paused.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _delivery.State(state.DirtyKeys.Any(k => !IsProcessing(k)) || state.RelayUrlDirty, N("deliveryDescription"));
        _processing.State(state.DirtyKeys.Any(IsProcessing), N("processing.description"));
        foreach (var (id, expander) in _advanced)
        {
            if (state.DirtyKeys.Any(AdvancedKeys(id).Contains) || id == "processing" && _paused.Text.Length > 0 ||
                id == "relay" && (state.RelayUrlDirty || state.Values.GetValueOrDefault("notifications.push_relay_reregistration_required") == "true")) expander.IsExpanded = true;
        }
        RenderSetup(state); RenderChannels(state);
        foreach (var chip in _chips.Values) if (chip.Parent is Border border) border.Visibility = chip.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, enabled) in _actions) button.IsEnabled = editable && !state.ChannelsLoading && !state.Saving && !state.OperationPending && !state.Conflict && !state.OutcomeUnknown && !state.OperationUnknown && enabled(state);
        var pageDirty = state.DirtyKeys.Count(C.PageKeys.Contains);
        _saveBar.State(pageDirty, state.Saving && !state.SavingDiscord, editable && !state.Saving && !state.ChannelsLoading && !state.OperationPending && !state.Conflict && !state.OutcomeUnknown && !state.OperationUnknown);
        _saveBar.Discard.IsEnabled = !state.Saving && !state.OperationPending;
        _spacer.Visibility = _saveBar.Visibility;
        if (state.Loaded && _restoreOffset is { } offset) { _restoreOffset = null; DispatcherQueue.TryEnqueue(() => { if (_active) { _root.UpdateLayout(); _scroll.ChangeView(null, offset, null, true); } }); }
        // A minted secret stays with the controller until its dialog has opened; a hidden host or another dialog only delays it.
        // Each snapshot is offered once, so a dialog that cannot open is not retried in a loop.
        if (_controller?.HasSigningSecret != true) _secretTried = null;
        else if (HostVisible && _dialog == null && !state.OperationPending && !ReferenceEquals(state, _secretTried)) { _secretTried = state; _ = ShowSecretAsync(); }
    }
    private static bool IsProcessing(string key) => key is "notifications.release_events_enabled" or "notifications.fanout_enabled" || key.StartsWith("notifications.fanout.", StringComparison.Ordinal) || key.StartsWith("notifications.retention.", StringComparison.Ordinal);
    private void Set(string key, string value) { if (!_sync) _controller?.Set(key, value); }
    private void OnSaved(IReadOnlyCollection<string> keys) => DispatcherQueue.TryEnqueue(() => { if (_active && _controller?.State.Owner?.IsCurrent == true && App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus(); });
    public Task<bool> CanLeaveAsync()
    {
        if (LeaveRefused)
        {
            // With no dialog in front the refusal would be silent; say what the page is waiting for.
            if (_dialog == null) App.Services.GetService<SiloPlayer.Services.ToastService>()?.Info((_controller?.State is { Saving: true } or { OperationPending: true })
                ? "Notifications is still waiting for the server. Stay on this page until it answers."
                : "A new signing secret is waiting to be shown. Save it before leaving Notifications.");
            // A secret whose dialog could not open is offered again on each attempt to leave.
            if (_dialog == null) { _secretTried = null; QueueRender(); }
            return Task.FromResult(false);
        }
        if (HeldTyping is { } held) return CanDropHeldAsync(held);
        return _leave?.CanLeaveAsync(owner => U.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }
    private async Task<bool> CanDropHeldAsync(HiddenDraft held)
    {
        // The hide retired the page owner, so the confirmation runs under a lease of the same unchanged authority.
        MonitorAuthorityLease owner;
        try { owner = _authority.Capture(); } catch (OperationCanceledException) { return true; }
        using (owner)
        {
            if (owner.Epoch != held.Epoch || owner.AccountId != held.AccountId) return true;
            return await new AdminLeaveGuard(() => owner, () => ReferenceEquals(_hiddenDraft, held), () => _hiddenDraft = null)
                .CanLeaveAsync(lease => U.ConfirmDiscardAsync(this, lease, () => _dialog, dialog => _dialog = dialog));
        }
    }
    public void SuspendAdminPage() { StopObservingHost(); Close(); }
    private void RetirePrivateUi()
    {
        _clearDialogPrivate?.Invoke();
        _dialog?.Hide();
        _sync = true;
        foreach (var (key, control) in _controls)
        {
            if (control is PasswordBox secret) { secret.Password = ""; _secretSynced[key] = ""; }
            else if (control is TextBox input) input.Text = "";
            control.IsEnabled = false;
        }
        _relayUrl.Text = ""; _deployment.Text = "";
        _relayBusy.IsActive = false;
        foreach (var ring in _channelBusy.Values) ring.IsActive = false;
        _relayStatus.Text = _emailStatus.Text = _discordStatus.Text = _discordTestResult.Text = "";
        foreach (var chip in _chips.Values) chip.Text = "";
        foreach (var result in _channelResults.Values) result.Text = "";
        _sync = false;
    }
    private void Close()
    {
        if (!_active) return; _active = false; _hideDeferred = false;
        if (_location != null) Positions.GetValue(_location, static _ => new()).Offset = _scroll.VerticalOffset;
        _authority.AvailabilityChanged -= AuthorityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= OnSaved; _controller.Dispose(); _controller = null; }
        RetirePrivateUi(); _leave = null; _secretTried = null; _channelList.Children.Clear(); _renderedChannels = null;
        _fields.Visibility = _saveBar.Visibility = _spacer.Visibility = Visibility.Collapsed;
    }
}
