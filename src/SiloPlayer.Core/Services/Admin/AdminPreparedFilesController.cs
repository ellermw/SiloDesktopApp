using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminPreparedPagedState<T>
{
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public IReadOnlyList<T> Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public int PageCount { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlySet<string> SeenCursors { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public string? Error { get; init; }
    public AdminFailureKind? ErrorKind { get; init; }
}

public sealed record AdminPreparedValueState<T>
{
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? ErrorKind { get; init; }
}

public enum AdminActionOutcome { Applied, NoChange, Partial, Unknown, Failed }
public sealed record AdminPreparedActionState(string Kind, AdminActionOutcome Outcome, string Message);

public sealed record AdminPreparedFilesSnapshot
{
    public MonitorAuthorityLease? Owner { get; init; }
    public string ActiveTab { get; init; } = "storage";
    public AdminPreparedValueState<AdminDownloadStorage> Storage { get; init; } = new();
    public bool PreparationsLoading { get; init; }
    public bool PreparationsLoaded { get; init; }
    public IReadOnlyList<AdminDownloadPreparation> Preparations { get; init; } = [];
    public AdminDownloadPreparationCounts PreparationCounts { get; init; } = new();
    public string? PreparationsError { get; init; }
    public AdminPreparedFileQuery FileQuery { get; init; } = new();
    public AdminPreparedPagedState<AdminPreparedFile> Files { get; init; } = new();
    public AdminDownloadDeviceQuery DeviceQuery { get; init; } = new();
    public AdminPreparedPagedState<AdminDownloadDevice> Devices { get; init; } = new();
    public IReadOnlyDictionary<string, AdminPreparedPagedState<AdminDownloadEntry>> Entries { get; init; } = new Dictionary<string, AdminPreparedPagedState<AdminDownloadEntry>>();
    public AdminDownloadHistoryQuery HistoryQuery { get; init; } = new();
    public AdminPreparedPagedState<AdminDownloadStorageEvent> History { get; init; } = new();
    public bool ActionPending { get; init; }
    public AdminPreparedActionState? LastAction { get; init; }
}

/// <summary>Visit-owned, bounded state for the five official `/admin/downloads` tabs.</summary>
public sealed class AdminPreparedFilesController : IDisposable
{
    public const int MaxRetainedPages = 10;
    private readonly AdminPreparedFilesApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminPreparedFilesSnapshot _state = new();
    private CancellationTokenSource? _storageCts, _preparationsCts, _filesCts, _devicesCts, _historyCts, _entriesCts;
    private long _storageSequence, _preparationsSequence, _filesSequence, _devicesSequence, _historySequence, _entriesSequence;
    private bool _disposed;

    public AdminPreparedFilesController(AdminPreparedFilesApi api, AdminMonitorAuthority authority)
    {
        _api = api;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnRetired;
    }

    public event Action? Changed;
    public event Action? Retired;
    public AdminPreparedFilesSnapshot State { get { lock (_gate) return _state; } }
    public AdminPreparedFilesApi Api => _api;
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();
    public bool IsActionCurrent(MonitorAuthorityLease? lease) => !_disposed && _lifetime.IsCurrent(lease);

    public async Task OpenAsync(string? tab, string? location, bool staleDevices = false)
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        var active = NormalizeTab(tab);
        lock (_gate)
        {
            if (_disposed) return;
            _state = new AdminPreparedFilesSnapshot
            {
                Owner = owner, ActiveTab = active,
                FileQuery = new AdminPreparedFileQuery { Location = AdminPreparedFilesContract.IsLocation(location) ? location : null },
                DeviceQuery = new AdminDownloadDeviceQuery { Stale = staleDevices },
            };
        }
        Raise();
        var storage = LoadStorageAsync(owner);
        var activeLoad = LoadActiveAsync(owner, active);
        await Task.WhenAll(storage, activeLoad).ConfigureAwait(false);
    }

    public async Task SetActiveTabAsync(string tab)
    {
        var active = NormalizeTab(tab);
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner) || _state.ActiveTab == active) return;
            _state = _state with { ActiveTab = active };
        }
        Raise();
        await LoadActiveAsync(owner, active).ConfigureAwait(false);
    }

    public Task ReloadStorageAsync() => Current(owner => LoadStorageAsync(owner));
    public Task ReloadPreparationsAsync() => Current(owner => LoadPreparationsAsync(owner));
    public Task ReloadFilesAsync() => Current(owner => LoadFilesAsync(owner, append: false));
    public Task ReloadDevicesAsync() => Current(owner => LoadDevicesAsync(owner, append: false));
    public Task ReloadHistoryAsync() => Current(owner => LoadHistoryAsync(owner, append: false));

    public Task SetFileQueryAsync(AdminPreparedFileQuery query)
    {
        var normalized = query.Normalize();
        bool active;
        lock (_gate)
        {
            if (_state.FileQuery == normalized) return Task.CompletedTask;
            _state = _state with { FileQuery = normalized, Files = new() };
            active = _state.ActiveTab == "files";
        }
        Raise();
        return active ? ReloadFilesAsync() : Task.CompletedTask;
    }

    public Task SetDeviceQueryAsync(AdminDownloadDeviceQuery query)
    {
        var normalized = query.Normalize();
        bool active;
        lock (_gate)
        {
            if (_state.DeviceQuery == normalized) return Task.CompletedTask;
            _state = _state with { DeviceQuery = normalized, Devices = new(), Entries = new Dictionary<string, AdminPreparedPagedState<AdminDownloadEntry>>() };
            active = _state.ActiveTab == "devices";
        }
        Raise();
        return active ? ReloadDevicesAsync() : Task.CompletedTask;
    }

    public Task SetHistoryQueryAsync(AdminDownloadHistoryQuery query)
    {
        var normalized = query.Normalize();
        bool active;
        lock (_gate)
        {
            if (_state.HistoryQuery == normalized) return Task.CompletedTask;
            _state = _state with { HistoryQuery = normalized, History = new() };
            active = _state.ActiveTab == "history";
        }
        Raise();
        return active ? ReloadHistoryAsync() : Task.CompletedTask;
    }

    public Task LoadMoreFilesAsync() => Current(owner => LoadFilesAsync(owner, append: true));
    public Task LoadMoreDevicesAsync() => Current(owner => LoadDevicesAsync(owner, append: true));
    public Task LoadMoreHistoryAsync() => Current(owner => LoadHistoryAsync(owner, append: true));

    public Task LoadEntriesAsync(AdminDownloadDevice device, bool append = false) => Current(owner => LoadEntriesAsync(owner, device, append));

    public Task RunPreparationActionAsync(string action, IReadOnlyList<AdminDownloadPreparation> items) => RunActionAsync("preparation-" + action,
        async lease =>
        {
            var result = await _api.RunPreparationActionAsync(lease, action, items.Select(x => x.Id).ToArray(), lease.Token).ConfigureAwait(false);
            var applied = result.Count(x => x.Outcome == "applied");
            var unchanged = result.Count(x => x.Outcome == "unchanged");
            var missing = result.Count(x => x.Outcome == "not_found");
            var inapplicable = result.Count(x => x.Outcome == "not_applicable");
            var verb = action switch { "pause" => "Paused", "resume" => "Resumed", "cancel" => "Canceled", _ => "Updated" };
            var parts = new List<string>();
            if (applied > 0) parts.Add($"{verb} {applied} {(applied == 1 ? "job" : "jobs")}.");
            if (unchanged > 0) parts.Add(action == "pause" ? $"{unchanged} already paused." : action == "resume" ? $"{unchanged} not paused." : $"{unchanged} already ended.");
            if (missing > 0) parts.Add($"{missing} no longer {(missing == 1 ? "exists" : "exist")}.");
            if (inapplicable > 0) parts.Add($"{inapplicable} {(inapplicable == 1 ? "was" : "were")} not applicable.");
            var skipped = unchanged + missing + inapplicable;
            return new AdminPreparedActionState("preparation-" + action,
                applied > 0 ? skipped > 0 ? AdminActionOutcome.Partial : AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                parts.Count == 0 ? "Nothing changed." : string.Join(' ', parts));
        }, async () => { await Task.WhenAll(ReloadPreparationsAsync(), ReloadStorageAsync()).ConfigureAwait(false); });

    public Task DeleteFilesAsync(IReadOnlyList<AdminPreparedFile> files, bool includeInUse) => RunActionAsync("delete-files",
        async lease =>
        {
            var results = await _api.DeleteFilesAsync(lease, files.Select(x => x.Id).ToArray(), includeInUse, lease.Token).ConfigureAwait(false);
            var removed = results.Count(x => x.Outcome is "deleted" or "requeued" or "not_found");
            var skipped = includeInUse ? 0 : results.Count(x => x.Outcome == "in_use");
            var failed = results.Count - removed - skipped;
            var outcome = failed > 0 ? removed > 0 ? AdminActionOutcome.Partial : AdminActionOutcome.Failed
                : removed > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange;
            var message = $"Deleted {removed} prepared {(removed == 1 ? "file" : "files")}.";
            if (skipped > 0) message += $" Skipped {skipped} in-use {(skipped == 1 ? "file" : "files")}.";
            if (failed > 0) message += $" {failed} could not be deleted.";
            return new("delete-files", outcome, message);
        }, async () => { await Task.WhenAll(ReloadFilesAsync(), ReloadStorageAsync()).ConfigureAwait(false); });

    public Task CleanupLocationAsync(AdminDownloadStorageLocation location) => RunActionAsync("cleanup",
        async lease =>
        {
            var result = await _api.CleanupLocationAsync(lease, location.Key, lease.Token).ConfigureAwait(false);
            return new("cleanup", result.FreedBytes > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                result.FreedBytes > 0 ? $"Freed {AdminPreparedFilesPresentation.FormatBytes(result.FreedBytes)} on {location.Name}." : $"Nothing to clean up on {location.Name}.");
        }, ReloadStorageAsync);

    public Task DeleteUntrackedAsync(AdminDownloadStorageLocation location) => RunActionAsync("delete-untracked",
        async lease =>
        {
            var result = await _api.DeleteUntrackedAsync(lease, location.Key, lease.Token).ConfigureAwait(false);
            return new("delete-untracked", result.FailedFiles == 0 ? result.Files > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange : result.Files > 0 ? AdminActionOutcome.Partial : AdminActionOutcome.Failed,
                result.FailedFiles == 0 ? result.Files == 0 ? "No untracked files were left."
                    : $"Deleted {result.Files} untracked {(result.Files == 1 ? "file" : "files")} ({AdminPreparedFilesPresentation.FormatBytes(result.Bytes)})."
                    : $"{result.FailedFiles} untracked files on {location.Name} could not be deleted. Check the directory's permissions.");
        }, async () => { await Task.WhenAll(ReloadStorageAsync(), ReloadHistoryAsync()).ConfigureAwait(false); });

    public Task RevokeAsync(AdminDownloadRevokeTarget target, string reason) => RunActionAsync("revoke",
        async lease =>
        {
            var result = await _api.RevokeAsync(lease, target, reason, lease.Token).ConfigureAwait(false);
            return new("revoke", result.Revoked > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                result.Revoked > 0 ? $"Revoked {result.Revoked} {(result.Revoked == 1 ? "download" : "downloads")}." : "Those downloads were already revoked.");
        }, async () => { await Task.WhenAll(ReloadDevicesAsync(), ReloadStorageAsync(), ReloadHistoryAsync()).ConfigureAwait(false); });

    public Task<IReadOnlyList<AdminPreparedFilesNode>> ListNodesAsync(MonitorAuthorityLease action) => _api.ListNodesAsync(action, action.Token);

    public async Task<AdminVersioned<AdminPreparedFilesNode>?> SaveNodeLocationAsync(MonitorAuthorityLease action, AdminPreparedFilesNode node,
        string? directory, long? budget)
    {
        if (!IsActionCurrent(action)) return null;
        lock (_gate)
        {
            if (_state.ActionPending) return null;
            _state = _state with { ActionPending = true, LastAction = null };
        }
        Raise();
        try
        {
            var saved = await _api.UpdateNodeLocationAsync(action, node, directory, budget, action.Token).ConfigureAwait(false);
            if (!IsActionCurrent(action)) return null;
            lock (_gate) _state = _state with { ActionPending = false, LastAction = new("save-location", AdminActionOutcome.Applied, $"Saved the prepared-file location for {node.Name}.") };
            Raise();
            await ReloadStorageAsync().ConfigureAwait(false);
            return saved;
        }
        catch (Exception error)
        {
            if (!IsActionCurrent(action)) return null;
            var uncertain = AdminFailures.Classify(error, action) is AdminFailureKind.Transport or AdminFailureKind.Server or AdminFailureKind.Unknown;
            lock (_gate) _state = _state with { ActionPending = false, LastAction = new("save-location", uncertain ? AdminActionOutcome.Unknown : AdminActionOutcome.Failed,
                uncertain ? "The location save result is unknown. Reload the node before trying again." : "The location was not saved. Reload the node before trying again.") };
            Raise();
            try { await ReloadStorageAsync().ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private Task LoadActiveAsync(MonitorAuthorityLease owner, string tab) => tab switch
    {
        "preparation" => LoadPreparationsAsync(owner), "files" => LoadFilesAsync(owner, false),
        "devices" => LoadDevicesAsync(owner, false), "history" => LoadHistoryAsync(owner, false), _ => Task.CompletedTask,
    };

    private async Task LoadStorageAsync(MonitorAuthorityLease owner)
    {
        var (sequence, token) = Begin(ref _storageCts, ref _storageSequence, owner.Token, s => _state = _state with { Storage = _state.Storage with { Loading = true, Error = null, ErrorKind = null } });
        try
        {
            var value = await _api.GetStorageAsync(owner, token).ConfigureAwait(false);
            Publish(sequence, _storageSequence, owner, () => _state = _state with { Storage = new() { Loaded = true, Value = value } });
        }
        catch (Exception error) { PublishFailure(sequence, _storageSequence, owner, error, () => _state = _state with { Storage = _state.Storage with { Loading = false, Loaded = true, Error = "Download storage could not be read.", ErrorKind = AdminFailures.Classify(error, owner) } }); }
    }

    private async Task LoadPreparationsAsync(MonitorAuthorityLease owner)
    {
        var (sequence, token) = Begin(ref _preparationsCts, ref _preparationsSequence, owner.Token, _ => _state = _state with { PreparationsLoading = true, PreparationsError = null });
        try
        {
            var value = await _api.ListPreparationsAsync(owner, token).ConfigureAwait(false);
            Publish(sequence, _preparationsSequence, owner, () => _state = _state with { PreparationsLoading = false, PreparationsLoaded = true, Preparations = value.Items, PreparationCounts = value.Counts });
        }
        catch (Exception error) { PublishFailure(sequence, _preparationsSequence, owner, error, () => _state = _state with { PreparationsLoading = false, PreparationsLoaded = true, PreparationsError = "Preparation jobs could not be read." }); }
    }

    private async Task LoadFilesAsync(MonitorAuthorityLease owner, bool append)
    {
        AdminPreparedFileQuery query; string? cursor; int pages;
        lock (_gate) { query = _state.FileQuery; cursor = append ? _state.Files.NextCursor : null; pages = append ? _state.Files.PageCount : 0; }
        if (append && (cursor == null || pages >= MaxRetainedPages))
        {
            lock (_gate) if (pages >= MaxRetainedPages) _state = _state with { Files = _state.Files with { Truncated = true, NextCursor = null } };
            Raise(); return;
        }
        var (sequence, token) = Begin(ref _filesCts, ref _filesSequence, owner.Token, _ => _state = _state with { Files = _state.Files with { Loading = true, Error = null, ErrorKind = null } });
        try
        {
            var page = await _api.ListFilesAsync(owner, query, cursor, token).ConfigureAwait(false);
            Publish(sequence, _filesSequence, owner, () =>
            {
                if (_state.FileQuery != query) return;
                var items = append ? Merge(_state.Files.Items, page.Items, x => x.Id) : page.Items;
                var count = pages + 1;
                var cursors = NextCursors(append ? _state.Files.SeenCursors : null, page.NextCursor);
                _state = _state with { Files = new() { Loaded = true, Items = items, PageCount = count,
                    NextCursor = count >= MaxRetainedPages ? null : page.NextCursor, Truncated = count >= MaxRetainedPages && page.HasMore, SeenCursors = cursors } };
            });
        }
        catch (Exception error) { PublishFailure(sequence, _filesSequence, owner, error, () => _state = _state with { Files = _state.Files with { Loading = false, Loaded = true, Error = "Prepared files could not be read.", ErrorKind = AdminFailures.Classify(error, owner) } }); }
    }

    private async Task LoadDevicesAsync(MonitorAuthorityLease owner, bool append)
    {
        AdminDownloadDeviceQuery query; string? cursor; int pages;
        lock (_gate) { query = _state.DeviceQuery; cursor = append ? _state.Devices.NextCursor : null; pages = append ? _state.Devices.PageCount : 0; }
        if (append && (cursor == null || pages >= MaxRetainedPages)) { MarkTruncated("devices", pages); return; }
        var (sequence, token) = Begin(ref _devicesCts, ref _devicesSequence, owner.Token, _ => _state = _state with { Devices = _state.Devices with { Loading = true, Error = null } });
        try
        {
            var page = await _api.ListDevicesAsync(owner, query, cursor, token).ConfigureAwait(false);
            Publish(sequence, _devicesSequence, owner, () =>
            {
                if (_state.DeviceQuery != query) return;
                var count = pages + 1; var items = append ? Merge(_state.Devices.Items, page.Items, DeviceKey) : page.Items;
                var cursors = NextCursors(append ? _state.Devices.SeenCursors : null, page.NextCursor);
                _state = _state with { Devices = new() { Loaded = true, Items = items, PageCount = count, NextCursor = count >= MaxRetainedPages ? null : page.NextCursor, Truncated = count >= MaxRetainedPages && page.HasMore, SeenCursors = cursors } };
            });
        }
        catch (Exception error) { PublishFailure(sequence, _devicesSequence, owner, error, () => _state = _state with { Devices = _state.Devices with { Loading = false, Loaded = true, Error = "Devices could not be read.", ErrorKind = AdminFailures.Classify(error, owner) } }); }
    }

    private async Task LoadHistoryAsync(MonitorAuthorityLease owner, bool append)
    {
        AdminDownloadHistoryQuery query; string? cursor; int pages;
        lock (_gate) { query = _state.HistoryQuery; cursor = append ? _state.History.NextCursor : null; pages = append ? _state.History.PageCount : 0; }
        if (append && (cursor == null || pages >= MaxRetainedPages)) { MarkTruncated("history", pages); return; }
        var (sequence, token) = Begin(ref _historyCts, ref _historySequence, owner.Token, _ => _state = _state with { History = _state.History with { Loading = true, Error = null } });
        try
        {
            var page = await _api.ListHistoryAsync(owner, query, cursor, token).ConfigureAwait(false);
            Publish(sequence, _historySequence, owner, () =>
            {
                if (_state.HistoryQuery != query) return;
                var count = pages + 1; var items = append ? Merge(_state.History.Items, page.Items, x => x.Id) : page.Items;
                var cursors = NextCursors(append ? _state.History.SeenCursors : null, page.NextCursor);
                _state = _state with { History = new() { Loaded = true, Items = items, PageCount = count, NextCursor = count >= MaxRetainedPages ? null : page.NextCursor, Truncated = count >= MaxRetainedPages && page.HasMore, SeenCursors = cursors } };
            });
        }
        catch (Exception error) { PublishFailure(sequence, _historySequence, owner, error, () => _state = _state with { History = _state.History with { Loading = false, Loaded = true, Error = "History could not be read.", ErrorKind = AdminFailures.Classify(error, owner) } }); }
    }

    private async Task LoadEntriesAsync(MonitorAuthorityLease owner, AdminDownloadDevice device, bool append)
    {
        var key = DeviceKey(device); AdminPreparedPagedState<AdminDownloadEntry> current;
        lock (_gate) current = _state.Entries.TryGetValue(key, out var value) ? value : new();
        if (append && (current.NextCursor == null || current.PageCount >= MaxRetainedPages)) return;
        var (sequence, token) = Begin(ref _entriesCts, ref _entriesSequence, owner.Token, _ => UpdateEntries(key, current with { Loading = true, Error = null }));
        try
        {
            var page = await _api.ListEntriesAsync(owner, new(device.UserId, device.ProfileId, device.DeviceId), append ? current.NextCursor : null, token).ConfigureAwait(false);
            Publish(sequence, _entriesSequence, owner, () =>
            {
                var count = (append ? current.PageCount : 0) + 1;
                var cursors = NextCursors(append ? current.SeenCursors : null, page.NextCursor);
                UpdateEntries(key, new() { Loaded = true, Items = append ? Merge(current.Items, page.Items, x => x.Id) : page.Items,
                    PageCount = count, NextCursor = count >= MaxRetainedPages ? null : page.NextCursor, Truncated = count >= MaxRetainedPages && page.HasMore, SeenCursors = cursors });
            });
        }
        catch (Exception error) { PublishFailure(sequence, _entriesSequence, owner, error, () => UpdateEntries(key, current with { Loading = false, Loaded = true, Error = "Downloads could not be read.", ErrorKind = AdminFailures.Classify(error, owner) })); }
    }

    private async Task RunActionAsync(string kind, Func<MonitorAuthorityLease, Task<AdminPreparedActionState>> send, Func<Task> reconcile)
    {
        MonitorAuthorityLease action;
        try { action = CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.ActionPending) return; _state = _state with { ActionPending = true, LastAction = null }; }
            Raise();
            try
            {
                var result = await send(action).ConfigureAwait(false);
                if (IsActionCurrent(action)) { lock (_gate) _state = _state with { ActionPending = false, LastAction = result }; Raise(); await reconcile().ConfigureAwait(false); }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error)
            {
                if (!IsActionCurrent(action)) return;
                var uncertain = AdminFailures.Classify(error, action) is AdminFailureKind.Transport or AdminFailureKind.Server or AdminFailureKind.Unknown;
                lock (_gate) _state = _state with { ActionPending = false, LastAction = new(kind, uncertain ? AdminActionOutcome.Unknown : AdminActionOutcome.Failed,
                    uncertain ? "The result is unknown. Check the current list before trying again." : "The action failed. Reload and try again.") };
                Raise();
                try { await reconcile().ConfigureAwait(false); } catch { }
            }
        }
    }

    private (long Sequence, CancellationToken Token) Begin(ref CancellationTokenSource? cts, ref long sequence,
        CancellationToken owner, Action<long> mutate)
    {
        CancellationToken token; long value;
        lock (_gate)
        {
            cts?.Cancel(); cts?.Dispose(); cts = CancellationTokenSource.CreateLinkedTokenSource(owner);
            token = cts.Token; value = ++sequence; mutate(value);
        }
        Raise(); return (value, token);
    }

    private void Publish(long sequence, long current, MonitorAuthorityLease owner, Action mutate)
    {
        lock (_gate) { if (_disposed || sequence != current || !_lifetime.IsCurrent(owner)) return; mutate(); }
        Raise();
    }

    private void PublishFailure(long sequence, long current, MonitorAuthorityLease owner, Exception error, Action mutate)
    {
        var kind = AdminFailures.Classify(error, owner);
        if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
        Publish(sequence, current, owner, mutate);
    }

    private void UpdateEntries(string key, AdminPreparedPagedState<AdminDownloadEntry> value)
    {
        var copy = new Dictionary<string, AdminPreparedPagedState<AdminDownloadEntry>>(_state.Entries, StringComparer.Ordinal) { [key] = value };
        _state = _state with { Entries = copy };
    }

    private void MarkTruncated(string list, int pages)
    {
        lock (_gate)
        {
            if (pages < MaxRetainedPages) return;
            _state = list == "devices" ? _state with { Devices = _state.Devices with { Truncated = true, NextCursor = null } }
                : _state with { History = _state.History with { Truncated = true, NextCursor = null } };
        }
        Raise();
    }

    private Task Current(Func<MonitorAuthorityLease, Task> run)
    {
        MonitorAuthorityLease? owner;
        lock (_gate) owner = _state.Owner;
        return owner != null && _lifetime.IsCurrent(owner) ? run(owner) : Task.CompletedTask;
    }

    private void OnRetired() { Clear(); Retired?.Invoke(); }
    private void Clear()
    {
        lock (_gate) { CancelReads(); _state = new(); }
        Raise();
    }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; CancelReads(); _state = new(); }
        _lifetime.Dispose(); Changed = null; Retired = null;
    }
    private void CancelReads()
    {
        foreach (var cts in new[] { _storageCts, _preparationsCts, _filesCts, _devicesCts, _historyCts, _entriesCts }) { try { cts?.Cancel(); } catch { } cts?.Dispose(); }
        _storageCts = _preparationsCts = _filesCts = _devicesCts = _historyCts = _entriesCts = null;
    }
    private void Raise() => Changed?.Invoke();
    private static string NormalizeTab(string? tab) => tab is "preparation" or "files" or "devices" or "history" ? tab : "storage";
    public static string DeviceKey(AdminDownloadDevice device) => device.UserId + "\n" + device.ProfileId + "\n" + device.DeviceId;
    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> current, IReadOnlyList<T> incoming, Func<T, string> key)
    {
        var result = current.ToList(); var known = current.Select(key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in incoming) if (known.Add(key(item))) result.Add(item);
        return result;
    }
    private static IReadOnlySet<string> NextCursors(IReadOnlySet<string>? current, string? next)
    {
        var result = current == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(current, StringComparer.Ordinal);
        if (next != null && !result.Add(next)) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
        return result;
    }
}
