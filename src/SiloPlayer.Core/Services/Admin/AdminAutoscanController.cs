using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminAutoscanState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loaded { get; init; }
    public bool Loading { get; init; }
    public bool Busy { get; init; }
    public bool Stale { get; init; }
    public string? Error { get; init; }
    public string? Notice { get; init; }
    public AdminAutoscanSettings? Settings { get; init; }
    public IReadOnlyList<AdminAutoscanSource> Sources { get; init; } = [];
    public bool SourcesComplete { get; init; }
    public IReadOnlyList<AdminAutoscanAvailableSource> Available { get; init; } = [];
    public IReadOnlyList<AdminAutoscanConnection> Connections { get; init; } = [];
    public IReadOnlyList<AdminLibrary> Libraries { get; init; } = [];
    public bool OptionsReady { get; init; }
    public string? OptionsError { get; init; }
}

/// <summary>One visit, bounded reads and serialized single-send changes. No timer or catalog scan.</summary>
public sealed class AdminAutoscanController : IDisposable
{
    private readonly AdminAutoscanApi _api;
    private readonly AdminLibrariesApi _libraries;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminAutoscanState _state = new();
    private long _revision;
    private bool _disposed;
    public event Action? Changed;
    public event Action? Retired;
    public AdminAutoscanController(AdminAutoscanApi api, AdminMonitorAuthority authority, AdminLibrariesApi libraries)
    { _api = api; _libraries = libraries; _lifetime = new(authority); _lifetime.Retired += OnRetired; }
    // Kept for callers using the domain API alone; the API owns the same client.
    public AdminAutoscanController(AdminAutoscanApi api, AdminMonitorAuthority authority) : this(api, authority, api.Libraries) { }
    public AdminAutoscanState State { get { lock (_gate) return _state; } }
    public bool IsCurrent(MonitorAuthorityLease? owner)
    { lock (_gate) return !_disposed && ReferenceEquals(owner, _state.Owner) && _lifetime.IsCurrent(owner); }
    public MonitorAuthorityLease CaptureAction(MonitorAuthorityLease owner)
    { lock (_gate) { if (!IsCurrent(owner)) throw new MonitorAuthorityRetiredException(); return _lifetime.CaptureAction(); } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Close(); return; }
        lock (_gate) { if (_disposed) return; ClearSecrets(); _state = new() { Owner = owner }; _revision++; }
        Changed?.Invoke(); await ReloadAsync().ConfigureAwait(false);
    }

    private static async Task<T?> Read<T>(Task<T> task) where T : class
    { try { return await task.ConfigureAwait(false); } catch (Exception ex) when (ex is not OutOfMemoryException) { return null; } }

    public Task ReloadAsync() => ReloadAsync(reconcilingWrite: false);
    private async Task ReloadAsync(bool reconcilingWrite)
    {
        MonitorAuthorityLease owner; long revision;
        lock (_gate)
        {
            if (_disposed || _state.Busy && !reconcilingWrite || _state.Loading || !IsCurrent(_state.Owner)) return;
            owner = _state.Owner!; revision = ++_revision;
            _state = _state with { Loading = true, Error = null };
        }
        Changed?.Invoke();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, deadline.Token);
        // At most four concurrent, bounded administrative reads; no media enumeration.
        var settings = Read(_api.GetSettingsAsync(owner, linked.Token));
        var sources = Read(_api.ListSourcesAsync(owner, linked.Token));
        var available = Read(_api.ListAvailableSourcesAsync(owner, linked.Token));
        var connections = Read(_api.ListConnectionsAsync(owner, linked.Token));
        await Task.WhenAll(settings, sources, available, connections).ConfigureAwait(false);
        var libraries = await Read(_libraries.ListAsync(owner, linked.Token)).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || revision != _revision || !IsCurrent(owner)) return;
            var fresh = settings.Result != null && sources.Result != null;
            var options = available.Result is { IsComplete: true } && connections.Result is { IsComplete: true } && libraries is { IsComplete: true };
            _state = _state with
            {
                Loaded = _state.Loaded || sources.Result != null, Loading = false,
                Settings = settings.Result ?? _state.Settings,
                Sources = sources.Result?.Items ?? _state.Sources,
                SourcesComplete = sources.Result?.IsComplete ?? _state.SourcesComplete,
                Stale = !fresh,
                Error = fresh ? null : AdminAutoscanText.T(_state.Loaded ? "sources.refreshFailed" : "sources.loadFailed"),
                Available = available.Result?.Items ?? _state.Available,
                Connections = connections.Result?.Items ?? _state.Connections,
                Libraries = libraries?.Items ?? _state.Libraries,
                OptionsReady = options,
                OptionsError = options ? null : "Source types, servers or library paths could not be loaded completely. Reload before editing.",
            };
        }
        Changed?.Invoke();
    }

    public Task<AdminAutoscanSettingsResult?> SetEnabledAsync(bool enabled, MonitorAuthorityLease owner) => MutateAsync(owner, null, async (lease, state) =>
    {
        var saved = state.Settings!;
        return await _api.UpdateSettingsAsync(lease, new() { Enabled = enabled, DefaultPollIntervalSeconds = saved.DefaultPollIntervalSeconds, DebounceSeconds = saved.DebounceSeconds }).ConfigureAwait(false);
    }, result => result.RescheduleState == "failed" ? "Settings saved; poll-task rescheduling failed on this process. Review Activity before running again."
        : result.RescheduleState == "not_configured" ? "Settings saved; poll-task rescheduling is not configured on this process." : "Settings saved; rescheduling applied on this process.");

    public Task<AdminAutoscanTriggerReceipt?> TriggerAsync(MonitorAuthorityLease owner) => MutateAsync(owner, null,
        (lease, _) => _api.TriggerAsync(lease), _ => "Autoscan poll task accepted on this process. This is not scan completion; inspect Activity for per-source outcomes.");
    public Task<object?> DeleteAsync(string id, MonitorAuthorityLease owner) => MutateAsync<object>(owner, id, async (lease, _) =>
    { await _api.DeleteSourceAsync(lease, id).ConfigureAwait(false); return new object(); }, _ => "Source deleted. Already-running work may continue.");
    public Task<AdminAutoscanSource?> SetSourceEnabledAsync(string id, bool enabled, MonitorAuthorityLease owner) => MutateAsync(owner, id,
        (lease, state) => _api.UpdateSourceAsync(lease, id, AdminAutoscanSourceDraft.FromSource(state.Sources.Single(s => s.Id == id), enabled)), _ => "Source saved.");
    public Task<AdminAutoscanSource?> CreateSourceAsync(string pluginId, string capabilityId, AdminAutoscanSourceWrite body, MonitorAuthorityLease owner) =>
        MutateAsync(owner, null, (lease, _) => _api.CreateSourceAsync(lease, pluginId, capabilityId, body), _ => "Source created.");
    public Task<AdminAutoscanSource?> UpdateSourceAsync(string id, AdminAutoscanSourceWrite body, MonitorAuthorityLease owner, AdminAutoscanSource? baseline = null) =>
        MutateAsync(owner, id, (lease, state) =>
        {
            var live = state.Sources.Single(s => s.Id == id);
            if (baseline != null && !AdminAutoscanEditorDraft.SameConfiguration(live, baseline))
                throw new InvalidOperationException("The source changed. Close and reopen Edit to review it.");
            // Enabled belongs to the live row; the editor never overwrites its switch.
            body.Enabled = live.Enabled;
            return _api.UpdateSourceAsync(lease, id, body);
        }, _ => "Source saved.");
    public Task<AdminAutoscanSource?> ConfigureWebhookAsync(string id, bool rotate, MonitorAuthorityLease owner) => MutateAsync(owner, id,
        (lease, state) => state.Sources.Single(s => s.Id == id).DeliveryMode == "webhook" ? _api.WebhookAsync(lease, id, rotate) : throw new InvalidOperationException(),
        _ => rotate ? "Webhook URL rotated. Update the connected service with the new URL." : "Webhook URL generated.");
    public Task<AdminAutoscanConnection?> CreateConnectionAsync(AdminAutoscanConnectionCreate body, MonitorAuthorityLease owner) =>
        MutateAsync(owner, null, (lease, _) => _api.CreateConnectionAsync(lease, body), _ => "Server added.");

    public async Task<AdminAutoscanConnectionCheck?> TestConnectionAsync(AdminAutoscanConnectionTest body, MonitorAuthorityLease owner, CancellationToken ct = default)
    {
        if (!IsCurrent(owner)) return null;
        try { using var action = CaptureAction(owner); var result = await _api.TestConnectionAsync(action, body, ct).ConfigureAwait(false); return IsCurrent(owner) && !ct.IsCancellationRequested ? result : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return IsCurrent(owner) ? new() { Ok = false } : null; }
    }
    public async Task<IReadOnlyList<AdminAutoscanRequestIntegration>?> ReusableIntegrationsAsync(MonitorAuthorityLease owner, CancellationToken ct = default)
    {
        if (!IsCurrent(owner)) return null;
        try
        {
            using var action = CaptureAction(owner);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, ct);
            var rows = await _api.ReusableIntegrationsAsync(action, linked.Token).ConfigureAwait(false);
            return IsCurrent(owner) && !ct.IsCancellationRequested ? rows : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
    public async Task<AdminAutoscanRewriteSuggestions?> SuggestionsAsync(string id, MonitorAuthorityLease owner, CancellationToken ct = default)
    {
        if (!IsCurrent(owner) || State.Stale || !State.Sources.Any(s => s.Id == id && s.ConnectionId != null)) return null;
        try { using var action = CaptureAction(owner); var result = await _api.SuggestionsAsync(action, id, ct).ConfigureAwait(false); return IsCurrent(owner) && !ct.IsCancellationRequested ? result : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public string? SafeWebhookUrl(string id, MonitorAuthorityLease owner)
    {
        lock (_gate)
        {
            if (!IsCurrent(owner) || _state.Busy || _state.Loading || _state.Stale) return null;
            var source = _state.Sources.FirstOrDefault(s => s.Id == id);
            return source?.WebhookConfigured == true ? AdminAutoscanPresentation.WebhookUrl(source.WebhookUrl, owner.Context.BaseUrl) : null;
        }
    }

    private async Task<T?> MutateAsync<T>(MonitorAuthorityLease owner, string? id, Func<MonitorAuthorityLease, AdminAutoscanState, Task<T>> operation, Func<T, string> receipt) where T : class
    {
        AdminAutoscanState snapshot;
        lock (_gate)
        {
            snapshot = _state;
            if (!IsCurrent(owner) || snapshot.Busy || snapshot.Loading || snapshot.Stale || !snapshot.Loaded || snapshot.Settings == null ||
                id != null && (!snapshot.SourcesComplete || !snapshot.Sources.Any(s => s.Id == id))) return null;
            _state = snapshot with { Busy = true, Error = null, Notice = null }; ++_revision;
        }
        Changed?.Invoke(); T? result = null;
        try { using var action = CaptureAction(owner); result = await operation(action, snapshot).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* Never echo provider errors or replay a write. */ }
        // Readback settles current state, never proves an unknown create/rotation receipt.
        // Keep Busy through readback and receipt publication: another mutation
        // cannot enter the gap between its predecessor's write and reconciliation.
        await ReloadAsync(reconcilingWrite: true).ConfigureAwait(false);
        lock (_gate)
        {
            if (!IsCurrent(owner)) return null;
            _state = _state with { Busy = false, Notice = result != null ? receipt(result) : null,
                Error = result == null ? "The change could not be confirmed. Review the reloaded state before another explicit submission." : _state.Error };
        }
        Changed?.Invoke(); return result;
    }
    private void OnRetired() { Clear(); Retired?.Invoke(); }
    private void ClearSecrets()
    { foreach (var source in _state.Sources) { source.WebhookUrl = null; source.SourceConfig.Clear(); } }
    private void Clear() { lock (_gate) { _revision++; ClearSecrets(); _state = new(); } Changed?.Invoke(); }
    public void Close() { _lifetime.Retire(); Clear(); }
    public void Dispose()
    { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Retired -= OnRetired; _lifetime.Dispose(); Clear(); Changed = null; Retired = null; }
}
