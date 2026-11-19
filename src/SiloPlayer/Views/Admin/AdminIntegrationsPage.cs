using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminIntegrationsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminIntegrationsApi _api;
    private readonly AdminMonitorAuthority _authority;
    private AdminIntegrationSettingsController? _controller;
    private AdminLeaveGuard? _leave;
    private AdminRouteRequest? _location;
    private AdminRoute _route;
    private readonly Dictionary<string, FrameworkElement> _fields = new(StringComparer.Ordinal);
    private readonly StackPanel _alerts = new() { Spacing = 8 };
    private readonly StackPanel _domain = new() { Spacing = 16 };
    private readonly StackPanel _providerEditorHost = new() { Spacing = 12 };
    private readonly TextBlock _domainStatus = AdminSettingsUi.Text("", 13, true);
    private readonly DispatcherQueueTimer _poll;
    private readonly DispatcherQueueTimer _modelDebounce;
    private IReadOnlyList<AdminAiProvider> _providers = [];
    private IReadOnlyList<AdminAiProviderType> _providerTypes = [];
    private AdminRecommendationStatus? _recommendations;
    private AdminJellyfinWebStatus? _jellyfinWeb;
    private AdminJellyfinWebStatus? _acceptedWebOperation;
    private DateTimeOffset? _webReceiptDeadline;
    private AdminAiCapabilities? _capabilities;
    private AdminChatGptStatus? _connection;
    private IReadOnlyList<AdminChatGptModel> _accountModels = [];
    private readonly Dictionary<string, IReadOnlyList<AdminAiModel>> _models = new(StringComparer.Ordinal);
    private bool _active, _retired, _syncing, _busy, _domainReading, _quietRead, _domainLoaded, _refreshAgain, _commandUnknown;
    private string? _domainError;
    private string? _accountModelsError;
    private ContentDialog? _dialog;
    private Task _openTask = Task.CompletedTask, _refreshTask = Task.CompletedTask, _commandTask = Task.CompletedTask;
    private Task _dialogTask = Task.CompletedTask, _saveTask = Task.CompletedTask, _fieldCommitTask = Task.CompletedTask;
    private Task _interactionTask = Task.CompletedTask, _browserTask = Task.CompletedTask, _callbackTask = Task.CompletedTask;
    private Button? _save, _discard;

    public AdminIntegrationsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        _api = App.Services.GetRequiredService<AdminIntegrationsApi>();
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        _poll = DispatcherQueue.CreateTimer(); _poll.Interval = TimeSpan.FromSeconds(5); _poll.IsRepeating = false;
        _poll.Tick += async (_, _) => await ReadDomainAsync(quiet: true);
        _modelDebounce = DispatcherQueue.CreateTimer(); _modelDebounce.Interval = TimeSpan.FromMilliseconds(300); _modelDebounce.IsRepeating = false;
        _modelDebounce.Tick += (_, _) => RequestModelRefresh();
        AutomationProperties.SetAutomationId(this, "AdminIntegrationsPage");
        Loaded += (_, _) => { if (_location != null) Open(); };
        Unloaded += (_, _) => Close();
    }

    public FrameworkElement ScrollContent => ContentRoot;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not AdminRouteRequest request || request.Route is not (AdminRoute.AiSettings or AdminRoute.Recommendations or AdminRoute.CompatibilitySettings)) return;
        if (_location?.Route != request.Route) Close();
        _location = request; _route = request.Route;
        if (IsLoaded) Open();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }

    private void Open()
    {
        if (_active || _location == null) return;
        _active = true; _retired = false;
        _authority.AvailabilityChanged -= OnAuthorityChanged; _authority.AvailabilityChanged += OnAuthorityChanged;
        var keys = _route switch { AdminRoute.AiSettings => AdminIntegrationsSettings.AiKeys, AdminRoute.Recommendations => AdminIntegrationsSettings.RecommendationKeys, _ => AdminIntegrationsSettings.CompatibilityKeys };
        _controller = ActivatorUtilities.CreateInstance<AdminIntegrationSettingsController>(App.Services, keys);
        _leave = new(() => _controller?.State.Owner, () => _controller?.State.DirtyKeys.Count > 0 || _providerDirty, () => { _controller?.Discard(); ClearProviderEditor(); });
        _controller.Changed += QueueRender;
        _controller.Saved += changedKeys => { DispatcherQueue.TryEnqueue(async () => { _models.Clear(); await RefreshDomainAsync(); }); };
        Build(); _openTask = OpenAsync(_controller);
    }
    private async Task OpenAsync(AdminIntegrationSettingsController controller)
    {
        await controller.OpenAsync();
        if (Current(controller)) await RefreshDomainAsync();
    }
    // A page retired by an authority change reopens once admin authority is available again.
    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() => { if (_retired && !_active && IsLoaded && _authority.IsAvailable) Open(); });
    private bool Current(AdminIntegrationSettingsController controller) => _active && ReferenceEquals(controller, _controller) && controller.State.Owner?.IsCurrent == true;
    /// <summary>A foreground domain read holds the controls. The background poll does not: disabling and re-enabling them every five seconds would move keyboard focus.</summary>
    private bool DomainHeld => _domainReading && !_quietRead;
    private bool Ready => _active && _domainLoaded && !_busy && !DomainHeld && !_commandUnknown && _controller?.State is { Loaded: true, Loading: false, Saving: false, Conflict: false, OutcomeUnknown: false, Owner.IsCurrent: true };
    private void Build()
    {
        _fields.Clear(); ContentRoot.Children.Clear(); _alerts.Children.Clear(); _domain.Children.Clear(); _domainShape = ""; _providerEditorHost.Children.Clear();
        ContentRoot.Children.Add(AdminSettingsUi.Title(_route switch { AdminRoute.AiSettings => "AI", AdminRoute.Recommendations => "Recommendations", _ => "Compatibility proxies" }));
        ContentRoot.Children.Add(_alerts); ContentRoot.Children.Add(_domain); ContentRoot.Children.Add(_providerEditorHost); ContentRoot.Children.Add(_domainStatus);
        ContentRoot.Children.Add(BuildSettings());
    }

    private Task RefreshDomainAsync() => ReadDomainAsync(quiet: false);
    private Task ReadDomainAsync(bool quiet)
    {
        if (!_active || _controller is not { } controller || !Current(controller)) return Task.CompletedTask;
        if (_domainReading) return _refreshTask;
        _domainReading = true; _quietRead = quiet; _poll.Stop();
        if (_domainLoaded && !quiet) { Render(controller.State); BuildDomain(); }
        return _refreshTask = RefreshDomainCoreAsync(controller);
    }
    private async Task RefreshDomainCoreAsync(AdminIntegrationSettingsController controller)
    {
        string? loginFailure = null;
        try
        {
            using var lease = controller.CaptureAction();
            if (_route is AdminRoute.AiSettings or AdminRoute.Recommendations)
            {
                var providers = await _api.GetProvidersAsync(lease);
                if (!Current(controller)) return;
                _providers = providers.Providers; _providerTypes = providers.Types;
                if (_route == AdminRoute.AiSettings)
                {
                    var model = Value("ai.chat_model", "gpt-6-luna");
                    var capabilities = await _api.GetCapabilitiesAsync(lease, model);
                    if (!Current(controller)) return;
                    _capabilities = capabilities;
                    if (capabilities.ChatGptSignIn)
                    {
                        var connection = await _api.GetChatGptStatusAsync(lease);
                        if (!Current(controller)) return;
                        _connection = connection;
                        if (connection.Active is { Authenticated: true } account)
                        {
                            try
                            {
                                var models = await _api.GetChatGptModelsAsync(lease, account.ClientId);
                                if (!Current(controller)) return;
                                _accountModels = models; _accountModelsError = null;
                            }
                            catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
                            catch { if (Current(controller)) { _accountModels = []; _accountModelsError = "Could not load this account's models. Retry, test the text connection, or choose another account."; } }
                        }
                        else _accountModels = [];
                        loginFailure = ObserveLogin(connection);
                    }
                    else { _connection = null; _accountModels = []; }
                }
                foreach (var id in SelectedProviderIds().Where(id => id != "").Distinct(StringComparer.Ordinal))
                {
                    if (_models.ContainsKey(id) || !_providers.Any(provider => provider.Id == id)) continue;
                    try
                    {
                        var result = await _api.GetProviderModelsAsync(lease, id);
                        if (!Current(controller)) return;
                        _models[id] = result.Success ? result.Models : [];
                    }
                    catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
                    catch { if (Current(controller)) _models[id] = []; }
                }
                if (_route == AdminRoute.Recommendations)
                {
                    var status = await _api.GetRecommendationsStatusAsync(lease);
                    if (!Current(controller)) return;
                    _recommendations = status;
                }
            }
            else
            {
                var status = await _api.GetJellyfinWebStatusAsync(lease);
                if (!Current(controller)) return;
                _jellyfinWeb = AdminIntegrationPolicy.ReconcileWebOperation(status, _acceptedWebOperation);
                if (_acceptedWebOperation != null && ReferenceEquals(_jellyfinWeb, status))
                {
                    _webReceiptDeadline = null;
                    _acceptedWebOperation = status.OperationRunning ? status : null;
                    if (!status.OperationRunning)
                    {
                        _commandUnknown = false; _domainError = null;
                        await controller.ReloadAsync(true);
                    }
                }
                else if (_webReceiptDeadline is { } deadline && DateTimeOffset.UtcNow >= deadline)
                {
                    _commandUnknown = true;
                    _domainError = "The accepted local operation is not visible on this server. Reload and review before trying another command, or use the server status if it still does not appear.";
                }
                // An operation the server showed and then stopped reporting gets the same wait as one it never showed.
                else if (_acceptedWebOperation != null && _webReceiptDeadline == null) _webReceiptDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
            }
            if (Current(controller)) { _domainLoaded = true; if (!_commandUnknown) _domainError = loginFailure; }
        }
        catch (OperationCanceledException) when (!Current(controller)) { }
        catch (Exception) { if (Current(controller)) { _domainLoaded = false; _domainError = "Could not refresh this page. Try again."; } }
        finally
        {
            _domainReading = false; _quietRead = false;
            if (Current(controller))
            {
                BuildDomain(); Render(controller.State); SchedulePoll();
                if (_refreshAgain) { _refreshAgain = false; DispatcherQueue.TryEnqueue(async () => await RefreshDomainAsync()); }
            }
            else if (_active && _controller is { } replacement && Current(replacement)) DispatcherQueue.TryEnqueue(async () => await RefreshDomainAsync());
        }
    }
    private IEnumerable<string> SelectedProviderIds() => _route == AdminRoute.AiSettings
        ? new[] { Value("ai.text_provider_id"), Value("ai.speech_provider_id") } : new[] { Value("recommendations.embedding_provider_id") };
    private void RequestModelRefresh()
    {
        if (_domainReading) _refreshAgain = true;
        else _ = RefreshDomainAsync();
    }
    private void SchedulePoll()
    {
        if (_active && !_busy && !_domainReading && !_commandUnknown && _domainError == null &&
            (_route == AdminRoute.Recommendations || _jellyfinWeb?.OperationRunning == true || _login != null)) _poll.Start();
        else _poll.Stop();
    }

    private Task RunCommandAsync(Func<MonitorAuthorityLease, Task> command, Func<bool>? allowed = null, string? title = null, string? confirmation = null)
    {
        if (!Ready || allowed?.Invoke() == false || _controller == null) return Task.CompletedTask;
        MonitorAuthorityLease lease;
        try { lease = _controller.CaptureAction(); } catch (OperationCanceledException) { return Task.CompletedTask; }
        _busy = true; _poll.Stop(); Render(_controller.State); BuildDomain();
        return _commandTask = ExecuteCommandAsync(_controller, lease, command, allowed, title, confirmation);
    }
    private async Task ExecuteCommandAsync(AdminIntegrationSettingsController controller, MonitorAuthorityLease lease, Func<MonitorAuthorityLease, Task> command,
        Func<bool>? allowed, string? title, string? confirmation)
    {
        using (lease)
        {
            string? failure = null;
            try
            {
                if (title != null)
                {
                    var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = confirmation, PrimaryButtonText = "Continue", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                    if (await ShowDialogAsync(dialog, lease) != ContentDialogResult.Primary) return;
                }
                lease.ThrowIfRetired();
                if (!Current(controller) || controller.State is { Loading: true } or { Saving: true } or { Conflict: true } or { OutcomeUnknown: true } || allowed?.Invoke() == false) return;
                await command(lease);
            }
            catch (OperationCanceledException) when (!lease.IsCurrent) { }
            catch (AdminIntegrationsOutcomeUnknownException)
            {
                if (Current(controller)) { _commandUnknown = true; _domainError = "The command outcome is unknown. Reload and review before trying again."; _loginDialog?.Hide(); ClearLogin(); }
            }
            catch (Exception) { failure = "The action failed. Review this page before trying again."; }
            finally
            {
                if (Current(controller))
                {
                    _busy = false;
                    // A read that began before the command settled may predate its effect; read once more after it.
                    if (!_commandUnknown) { if (_domainReading) _refreshAgain = true; await RefreshDomainAsync(); }
                    if (failure != null) _domainError = failure;
                    if (_fieldCommits.Count > 0 && !_commandUnknown && !controller.State.Saving) _fieldCommitTask = CommitFieldsCoreAsync();
                    BuildDomain(); Render(controller.State); SchedulePoll();
                }
            }
        }
    }
    private Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, MonitorAuthorityLease lease)
    {
        if (_dialog != null) return Task.FromResult(ContentDialogResult.None);
        lease.ThrowIfRetired(); _dialog = dialog;
        var task = ShowDialogCoreAsync(dialog, lease); _dialogTask = task; return task;
    }
    private async Task<ContentDialogResult> ShowDialogCoreAsync(ContentDialog dialog, MonitorAuthorityLease lease)
    {
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler onUnload = (_, _) => unloaded.TrySetResult(); dialog.Unloaded += onUnload;
        using var registration = lease.Token.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) dialog.Hide(); }));
        try
        {
            var result = await dialog.ShowAsync();
            if (dialog.IsLoaded) await unloaded.Task;
            return result;
        }
        catch (Exception error) when (error.HResult == unchecked((int)0x80000019)) { return ContentDialogResult.None; }
        finally { dialog.Unloaded -= onUnload; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    private void SetDomainStatus(MonitorAuthorityLease lease, string value) { if (_active && lease.IsCurrent) AdminSettingsUi.UpdateLiveText(_domainStatus, value); }
    private void QueueRender() => DispatcherQueue.TryEnqueue(() =>
    {
        var state = _controller?.State;
        if (!_active || state == null) return;
        if (state.Owner == null || !state.Owner.IsCurrent)
        {
            // The authority changed: clear the retired owner's data, then reload under the new authority while still admin.
            Close(retired: true);
            if (IsLoaded && _authority.IsAvailable) Open();
            return;
        }
        Render(state);
        if (_route != AdminRoute.AiSettings) BuildDomain();
    });
    private void Render(AdminIntegrationSettingsState state)
    {
        if (!_active) return;
        _alerts.Children.Clear();
        if (state.Error != null) _alerts.Children.Add(AdminSettingsUi.Alert(state.Error));
        if (_domainError != null) _alerts.Children.Add(AdminSettingsUi.Alert(_domainError));
        else if (_commandUnknown) _alerts.Children.Add(AdminSettingsUi.Alert("The earlier command outcome is still unknown. Reload and review before trying again."));
        if (state.RestartRequired || _jellyfinWeb?.RestartRequired == true) _alerts.Children.Add(AdminSettingsUi.Alert("A server restart is required for some saved settings."));
        var recovery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AddAction(recovery, "Retry", "AdminIntegrationsRetry", !_busy && !DomainHeld && !state.Loading && !state.Saving, async () => { if (!state.Loaded) await ReloadAsync(false); else await RefreshDomainAsync(); });
        AddAction(recovery, "Reload, keep changes", "AdminIntegrationsReloadKeep", !_busy && !DomainHeld && !state.Loading && !state.Saving, () => ReloadAsync(true));
        AddAction(recovery, "Reload, discard changes", "AdminIntegrationsReloadDiscard", !_busy && !DomainHeld && !state.Loading && !state.Saving, () => ReloadAsync(false));
        _alerts.Children.Add(recovery);
        _syncing = true;
        UpdateChoices();
        foreach (var (key, element) in _fields)
        {
            var value = key == "ai.text_provider_id" && Value("ai.auth_mode") == "chatgpt" ? "__chatgpt" : Value(key);
            switch (element)
            {
                case TextBox text when text.Text != value: text.Text = value; break;
                case TextBlock readOnly: readOnly.Text = value != "" ? value : key == "jellyfin_compat.server_id" ? "Created on the next start" : "Not installed"; break;
                case ToggleSwitch toggle: toggle.IsOn = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case ComboBox combo:
                    combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == value);
                    if (combo.IsEditable) combo.Text = value;
                    break;
            }
            if (element is Control control)
            {
                control.IsEnabled = state.Loaded && !state.Loading && !state.Saving && !_busy && !_commandUnknown && !state.Conflict && !state.OutcomeUnknown && !state.ManagedByEnvironment.Contains(key);
                if (control is ToggleSwitch feature && !feature.IsOn && !FeatureReady(key)) control.IsEnabled = false;
                if (key.EndsWith("provider_id", StringComparison.Ordinal)) control.IsEnabled &= _domainLoaded && !DomainHeld;
            }
        }
        _syncing = false;
        foreach (var (key, picker) in _modelPickers)
        {
            picker.IsEnabled = Ready && !state.ManagedByEnvironment.Contains(key) && picker.Items.Count > 0;
            if (_fields.GetValueOrDefault(key) is TextBox input) input.IsReadOnly = key == "ai.chat_model" && Value("ai.auth_mode") == "chatgpt";
        }
        if (_fields.GetValueOrDefault("ai.service_tier") is ComboBox tier && tier.Parent is FrameworkElement tierRow)
            tierRow.Visibility = Value("ai.auth_mode") != "chatgpt" && _providers.Any(provider => provider.Id == Value("ai.text_provider_id") && provider.Type == "openai") && _capabilities?.ServiceTiers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_save != null) _save.IsEnabled = Ready && state.DirtyKeys.Count > 0;
        if (_discard != null) _discard.IsEnabled = !state.Saving && !_busy && state.DirtyKeys.Count > 0;
        if (_completeLogin != null) _completeLogin.IsEnabled = Ready && !_callbackSubmitted;
        foreach (var (kind, button) in _modelChecks)
            button.IsEnabled = Ready && (kind == "ai_chat" ? TextReady || Value("ai.auth_mode") == "chatgpt" && _connection?.Active is { UsageLimited: true, Authenticated: true } && Value("ai.chat_model") != "" : kind == "ai_transcription" ? FeatureReady("subtitle_ai.transcribe_enabled") : AdminIntegrationPolicy.ProviderReady(_providers, Value("recommendations.embedding_provider_id"), "embeddings", Value("recommendations.embedding_model")));
        UpdateProviderEditorEnabled();
    }
    private async Task ReloadAsync(bool preserve, bool adoptServerStatus = false)
    {
        var controller = _controller; if (controller == null || _busy || DomainHeld) return;
        // A background poll leaves this action enabled; let it settle instead of dropping the request.
        if (_domainReading) { await _refreshTask; if (!ReferenceEquals(controller, _controller) || _busy || _domainReading) return; }
        _loginDialog?.Hide(); ClearLogin();
        if (preserve) { if (_providerKey != null) _providerKey.Password = ""; }
        else ClearProviderEditor();
        _models.Clear();
        await controller.ReloadAsync(preserve);
        if (!Current(controller)) return;
        // A plain reload keeps the accepted operation; only the explicit review gives it up, and only once its wait has passed.
        if (adoptServerStatus && WebReceiptOverdue) { _acceptedWebOperation = null; _webReceiptDeadline = null; }
        await RefreshDomainAsync();
        if (Current(controller) && controller.State.Error == null && _domainLoaded && _acceptedWebOperation == null) { _commandUnknown = false; _domainError = null; Render(controller.State); BuildDomain(); }
    }
    internal async Task WhenSettledAsync()
    {
        while (true)
        {
            if (_modelDebounce.IsRunning) { _modelDebounce.Stop(); RequestModelRefresh(); }
            var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DispatcherQueue.TryEnqueue(() => turn.TrySetResult()); await turn.Task;
            var tasks = new[] { _openTask, _refreshTask, _commandTask, _dialogTask, _saveTask, _fieldCommitTask, _interactionTask, _browserTask, _callbackTask }.Where(task => !task.IsCompleted).ToArray();
            if (tasks.Length == 0) return;
            await Task.WhenAll(tasks);
        }
    }
    public Task<bool> CanLeaveAsync() => _leave?.CanLeaveAsync(async owner =>
    {
        if (_dialog != null || !owner.IsCurrent) return null;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Discard changes?", Content = "Your unsaved settings and provider edits will be discarded.", PrimaryButtonText = "Discard", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await ShowDialogAsync(dialog, owner) == ContentDialogResult.Primary;
    }) ?? Task.FromResult(true);
    public void SuspendAdminPage() => Close();
    private static void ClearPrivateTree(DependencyObject root)
    {
        var children = Enumerable.Range(0, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root))
            .Select(index => Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index)).ToArray();
        foreach (var child in children) ClearPrivateTree(child);
        switch (root)
        {
            case PasswordBox secret: secret.Password = ""; break;
            case TextBox input: input.Text = ""; break;
            case TextBlock text: text.Text = ""; break;
            case ComboBox select: select.Items.Clear(); if (select.IsEditable) select.Text = ""; break;
            case ToggleSwitch toggle: toggle.IsOn = false; break;
        }
    }
    private void Close(bool retired = false)
    {
        // Only an authority retirement keeps listening for a reopen; navigation, unload and suspension are final.
        if (!retired) { _retired = false; _authority.AvailabilityChanged -= OnAuthorityChanged; }
        if (!_active) return;
        _active = false; _retired = retired; _poll.Stop(); _modelDebounce.Stop(); _refreshAgain = false;
        _dialog?.Hide(); ClearLogin(); ClearProviderEditor();
        _syncing = true; ClearPrivateTree(ContentRoot); _syncing = false;
        var controller = _controller; _controller = null; _leave = null;
        if (controller != null) { controller.Changed -= QueueRender; controller.Dispose(); }
        _providers = []; _providerTypes = []; _models.Clear(); _accountModels = []; _capabilities = null; _connection = null;
        _recommendations = null; _jellyfinWeb = null; _acceptedWebOperation = null; _webReceiptDeadline = null; _domainLoaded = false; _domainError = null; _accountModelsError = null; _commandUnknown = false; _busy = false;
        _fields.Clear(); _fieldCommits.Clear(); _alerts.Children.Clear(); _domain.Children.Clear(); _domainShape = ""; _providerEditorHost.Children.Clear();
        _domainStatus.Text = ""; ContentRoot.Children.Clear(); _save = null; _discard = null;
        _modelChecks.Clear();
        _modelPickers.Clear();
    }
}
