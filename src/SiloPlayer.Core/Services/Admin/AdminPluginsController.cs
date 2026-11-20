using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminPluginsState
{
    public IReadOnlyList<AdminPlugin> Installations { get; init; } = [];
    public IReadOnlyList<AdminPlugin> Catalog { get; init; } = [];
    public IReadOnlyList<AdminPluginRepository> Repositories { get; init; } = [];
    public AdminVersioned<AdminPluginCatalogSettings>? CatalogSettings { get; init; }
    public AdminPluginCatalogStatus? CatalogStatus { get; init; }
    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Busy { get; init; }
    public bool CatalogReviewRequired { get; init; }
    public string TaskState { get; init; } = "";
    public string Notice { get; init; } = "";
}

/// <summary>One visible page, bounded refresh, captured acting-admin ownership and single-send commands. No background polling.</summary>
public sealed class AdminPluginsController : IDisposable
{
    private readonly AdminPluginsApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private CancellationTokenSource? _read;
    private long _generation;
    private int _busy;
    private int _taskRead;
    private string? _detailPluginId;
    private bool _disposed;
    private AdminPluginsState _state = new();
    public AdminPluginsState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease? Owner => _lifetime.Owner;
    public event Action? Changed;
    public event Action? Retired;
    public AdminPluginsController(AdminPluginsApi api, AdminMonitorAuthority authority)
    {
        _api = api; _lifetime = new(authority); _lifetime.Retired += OnRetired;
    }
    private void OnRetired()
    {
        lock (_gate) { _read?.Cancel(); _generation++; _state = new(); }
        Changed?.Invoke(); Retired?.Invoke();
    }
    public async Task OpenAsync(string? detailPluginId = null)
    {
        _detailPluginId = detailPluginId;
        try { _lifetime.Open(); await ReloadAsync(); }
        catch (OperationCanceledException) { OnRetired(); }
    }
    public bool IsCurrent(AdminPlugin row) => Owner is { IsCurrent: true } && State.Installations.Any(p => ReferenceEquals(p, row));
    public bool IsCurrent(AdminPluginRepository row) => Owner is { IsCurrent: true } && State.Repositories.Any(p => ReferenceEquals(p, row));
    public bool IsCatalogCurrent(AdminPlugin row) => Owner is { IsCurrent: true } && State.Catalog.Any(p => ReferenceEquals(p, row));

    public async Task ReloadAsync(bool reviewed = false)
    {
        var owner = Owner;
        if (owner is not { IsCurrent: true } || _disposed) return;
        long generation; CancellationToken token;
        lock (_gate)
        {
            _read?.Cancel(); _read?.Dispose(); _read = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            _read.CancelAfter(TimeSpan.FromSeconds(60)); token = _read.Token; generation = ++_generation;
            _state = _state with { Loading = true, CatalogReviewRequired = reviewed ? false : _state.CatalogReviewRequired };
        }
        Changed?.Invoke();
        var failures = new Dictionary<string, string>();
        async Task<T?> Get<T>(string key, Func<Task<T>> load)
        {
            try { return await load().ConfigureAwait(false); }
            catch (OperationCanceledException) { if (owner.IsCurrent) lock (failures) failures[key] = "The read timed out. Retry."; return default; }
            catch (Exception error) { lock (failures) failures[key] = FailureText(error); return default; }
        }
        var installed = Get("Installed", () => _api.InstallationsAsync(owner, token));
        async Task<IReadOnlyList<AdminPlugin>?> DetailCatalog()
        {
            var rows = await installed.ConfigureAwait(false);
            // The official detail page avoids fetching remote catalog indexes for an installed modern manifest.
            if (rows == null || rows.Any(p => p.PluginId == _detailPluginId && p.Presentation != null)) return [];
            return await Get("Catalog", () => _api.CatalogAsync(owner, token)).ConfigureAwait(false);
        }
        var catalog = _detailPluginId == null ? Get("Catalog", () => _api.CatalogAsync(owner, token)) : DetailCatalog();
        var repositories = _detailPluginId == null ? Get("Repositories", () => _api.RepositoriesAsync(owner, token)) : Task.FromResult<IReadOnlyList<AdminPluginRepository>?>([]);
        var settings = _detailPluginId == null ? Get("Catalog settings", () => _api.CatalogSettingsAsync(owner, token)) : Task.FromResult<AdminVersioned<AdminPluginCatalogSettings>?>(null);
        var status = _detailPluginId == null ? Get("Catalog status", () => _api.CatalogStatusAsync(owner, token)) : Task.FromResult<AdminPluginCatalogStatus?>(null);
        var task = _detailPluginId == null ? Get("Update check", () => _api.UpdateTaskAsync(owner, false, token)) : Task.FromResult(default(JsonElement));
        await Task.WhenAll(installed, catalog, repositories, settings, status, task).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || !owner.IsCurrent || !ReferenceEquals(owner, Owner) || generation != _generation) return;
            _state = _state with { Installations = installed.Result ?? [], Catalog = catalog.Result ?? [], Repositories = repositories.Result ?? [],
                CatalogSettings = settings.Result, CatalogStatus = status.Result, TaskState = TaskState(task.Result),
                Errors = failures, Loading = false, Loaded = true };
        }
        Changed?.Invoke();
    }
    private static string TaskState(JsonElement task) => task.ValueKind == JsonValueKind.Object && task.TryGetProperty("key", out var key) && key.GetString() == "check_plugin_updates" && task.TryGetProperty("state", out var state) ? state.GetString() ?? "" : "";

    private async Task<bool> ExecuteAsync(Func<bool> targetCurrent, Func<MonitorAuthorityLease, Task<string>> send, bool reconcile = true)
    {
        if (_disposed || !targetCurrent() || State.Loading || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        MonitorAuthorityLease? action = null;
        try
        {
            action = _lifetime.CaptureAction();
            if (!targetCurrent()) return false;
            Set(s => s with { Busy = true, Notice = "" });
            var notice = await send(action).ConfigureAwait(false);
            if (!_lifetime.IsCurrent(action)) return false;
            Set(s => s with { Notice = notice });
            if (reconcile) await ReloadAsync().ConfigureAwait(false); return true;
        }
        catch (Exception error)
        {
            if (action == null || !_lifetime.IsCurrent(action)) return false;
            var conflict = error is ApiException { StatusCode: 412 or 428 };
            var rejected = error is ApiException { StatusCode: 400 or 403 or 404 or 409 or 422 };
            Set(s => s with { CatalogReviewRequired = s.CatalogReviewRequired || conflict, Notice = conflict
                ? "Catalog settings changed. Reload and review before editing; the request was not replayed."
                : rejected ? FailureText(error)
                : reconcile ? "Command outcome is unconfirmed. Current state has been reloaded for review; the command was not replayed."
                : "Connection check outcome is unconfirmed. The check was not replayed." });
            if (!conflict && reconcile) await ReloadAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            action?.Dispose(); Interlocked.Exchange(ref _busy, 0);
            if (!_disposed) Set(s => s with { Busy = false });
        }
    }
    private void Set(Func<AdminPluginsState, AdminPluginsState> change) { lock (_gate) { if (_disposed) return; _state = change(_state); } Changed?.Invoke(); }

    public Task<bool> InstallationCommandAsync(AdminPlugin row, AdminPluginCommand command) => ExecuteAsync(() => IsCurrent(row), async lease =>
    {
        if (command == AdminPluginCommand.Update && !AdminPluginPresentation.HasUpdate(row) || command == AdminPluginCommand.Restart && (!row.Enabled || row.Runtime is not { Resident: true })) throw new ArgumentException("This command is not available.");
        if (command == AdminPluginCommand.Uninstall) { await _api.UninstallAsync(lease, row.Id); return "Plugin uninstalled."; }
        var result = await _api.InstallationAsync(lease, row.Id, command);
        if (result.PluginId != row.PluginId) throw new InvalidDataException();
        return result.Runtime?.State == "failed" ? "The command completed, but the plugin failed to start. Review its process status." : command == AdminPluginCommand.Restart ? "Restart requested. Review the process status." : "Plugin updated.";
    });
    public Task<bool> InstallAsync(AdminPlugin row) => ExecuteAsync(() => IsCatalogCurrent(row), async lease =>
    {
        await _api.InstallAsync(lease, new() { RepositoryId = row.RepositoryId, PluginId = row.PluginId, Version = row.Version }); return "Plugin installed.";
    });
    public Task<bool> RepositoryAsync(AdminPluginRepository? row, string name, string url, bool enabled, bool delete = false) => ExecuteAsync(
        () => Owner is { IsCurrent: true } && (row == null || IsCurrent(row) && !row.Managed), async lease =>
        { await _api.RepositoryAsync(lease, row, name, url, enabled, delete); return delete ? "Repository removed." : "Repository saved."; });
    public Task<bool> SetCommunityAsync(AdminVersioned<AdminPluginCatalogSettings> reviewed, bool include) => ExecuteAsync(
        () => Owner is { IsCurrent: true } && ReferenceEquals(State.CatalogSettings, reviewed) && !State.CatalogReviewRequired,
        async lease => { await _api.SetCatalogSettingsAsync(lease, reviewed, include); return "Plugin catalog settings updated."; });
    public async Task<bool> SaveConfigAsync(AdminPlugin row, AdminPluginConfigWrite input)
    {
        var copy = Clone(input);
        try { return await ExecuteAsync(() => IsCurrent(row), async lease => { await _api.SaveConfigAsync(lease, row.Id, copy); return "Configuration saved."; }).ConfigureAwait(false); }
        finally { copy.Value.Clear(); copy.ClearSecrets.Clear(); }
    }
    public async Task<bool> TestConfigAsync(AdminPlugin row, AdminPluginConfigWrite input)
    {
        var copy = Clone(input);
        try { return await ExecuteAsync(() => IsCurrent(row), async lease => { var result = await _api.TestConfigAsync(lease, row.Id, copy); return result.Success ? "Connection check succeeded." : "Connection check failed. Review the configuration."; }, reconcile: false).ConfigureAwait(false); }
        finally { copy.Value.Clear(); copy.ClearSecrets.Clear(); }
    }
    public Task<bool> AuthBindingAsync(AdminPlugin row, AdminPluginAuthBinding input)
    {
        var copy = Clone(input);
        return ExecuteAsync(() => IsCurrent(row), async lease => { await _api.AuthBindingAsync(lease, row.Id, copy); return "Sign-in binding saved. No restart is required."; });
    }
    public Task<bool> TaskBindingAsync(AdminPlugin row, AdminPluginTaskBinding input)
    {
        var copy = Clone(input);
        return ExecuteAsync(() => IsCurrent(row), async lease => await _api.TaskBindingAsync(lease, row.Id, copy) ? "Task binding saved. A server restart is required." : "Task binding saved.");
    }
    public Task<bool> CheckUpdatesAsync() => ExecuteAsync(() => Owner is { IsCurrent: true } && State.TaskState == "idle", async lease =>
    { var result = await _api.UpdateTaskAsync(lease, true); return TaskState(result) is "running" or "cancelling" ? "Plugin update check started on this server process." : "Update check acknowledged. Refresh to review the outcome."; });
    public async Task PollUpdateCheckAsync(bool refreshInventory)
    {
        var owner = Owner;
        if (_disposed || owner is not { IsCurrent: true } || State.Busy || State.Loading || Interlocked.CompareExchange(ref _taskRead, 1, 0) != 0) return;
        try
        {
            var result = await _api.UpdateTaskAsync(owner, false).ConfigureAwait(false); var state = TaskState(result);
            if (!owner.IsCurrent || !ReferenceEquals(owner, Owner)) return;
            var finished = (State.TaskState is "running" or "cancelling") && state == "idle";
            Set(s => s with { TaskState = state });
            if (finished && refreshInventory) await ReloadAsync().ConfigureAwait(false);
            else if (finished) Set(s => s with { Notice = "Plugin update check finished. Refresh after reviewing your current edits." });
        }
        catch (Exception error) { if (owner.IsCurrent && ReferenceEquals(owner, Owner)) Set(s => s with { Notice = FailureText(error), TaskState = "" }); }
        finally { Interlocked.Exchange(ref _taskRead, 0); }
    }
    public Task<bool> UploadAsync(Stream stream, string filename, long length, IProgress<int>? progress = null) => ExecuteAsync(() => Owner is { IsCurrent: true }, async lease =>
    { await _api.UploadAsync(lease, stream, filename, length, progress, lease.Token); return "Plugin installed from file."; });
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AdminPluginsApi.WireOptions), AdminPluginsApi.WireOptions)!;
    private static string FailureText(Exception error) => error switch
    {
        ApiException { ErrorCode: "profile_unverified" } => "Verify the selected administrator profile, then reload Plugins.",
        ApiException { ErrorCode: "demo_restricted" } => "This command is unavailable on a demo server.",
        ApiException { StatusCode: 401 } => "Sign in again, then reload Plugins.",
        ApiException { StatusCode: 403 } => "Permission to manage plugins is required. Reload after access is restored.",
        ApiException { StatusCode: 404 } => "This plugin resource is unavailable. Reload the inventory.",
        ApiException { StatusCode: 429 } => "The server asked you to wait. Retry after the rate limit expires.",
        ApiException { StatusCode: 409 } => "The plugin state conflicts with this command. Reload and review before trying again.",
        ApiException { StatusCode: 400 or 422 } => "The server rejected these values. Review the plugin settings and try again.",
        ApiException { StatusCode: >= 500 } => "Plugin management is temporarily unavailable. Retry the read before submitting another command.",
        AdminPluginsInventoryLimitException limit => "This plugin collection exceeds the " + limit.Limit + "-entry client limit. It was not published as a partial inventory.",
        InvalidDataException or JsonException or ArgumentException => "The server returned invalid or oversized plugin data. Retry before editing.",
        _ => "The plugin read did not finish. Check the connection and retry.",
    };
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _generation++; _read?.Cancel(); _read?.Dispose(); _read = null; _state = new(); }
        _lifetime.Retired -= OnRetired; _lifetime.Dispose(); Changed = null; Retired = null;
    }
}
