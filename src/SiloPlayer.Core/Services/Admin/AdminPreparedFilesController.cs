using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;
using Text = SiloPlayer.Core.Services.Admin.AdminPreparedFilesText;

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

/// <summary>
/// The outcome notice of the last action. <paramref name="Error"/> marks a result the official page reports as an error toast even
/// though some of the work was done (for example "Deleted 2, 1 failed"); a part the server refused on purpose is not an error.
/// </summary>
public sealed record AdminPreparedActionState(string Kind, AdminActionOutcome Outcome, string Message, bool Error = false)
{
    public bool IsError => Error || Outcome is AdminActionOutcome.Failed or AdminActionOutcome.Unknown;
}

/// <summary>The answer of a node location save: stored, refused (correct and retry), stale (reload the node) or unknown.</summary>
public sealed record AdminNodeSaveResult(AdminActionOutcome Outcome, string Message, AdminVersioned<AdminPreparedFilesNode>? Saved = null, bool Stale = false);

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
    public IReadOnlySet<string> PendingPreparationIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public AdminPreparedActionState? LastAction { get; init; }
    /// <summary>A write's outcome is unknown: destructive actions stay off until the lists are read again.</summary>
    public bool DestructiveBlocked { get; init; }
}

/// <summary>
/// Visit-owned, bounded state for the five official <c>/admin/downloads</c> tabs (Silo Server 66b836f82).
/// Every read has its own cancellation and sequence guard; a response is applied only while it is the newest read of its
/// data and the page's authority is unchanged. The page keeps the preparation queue current from the realtime
/// <c>download_preparations</c> and <c>downloads</c> channels plus the official bounded polls (storage overview every
/// 60 s; preparations every 60 s while work is in flight and once when the oldest listed failure ages out).
/// </summary>
public sealed class AdminPreparedFilesController : IDisposable
{
    public const int MaxRetainedPages = 10;
    public static readonly TimeSpan StoragePoll = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan PreparationMaintenance = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan EventCoalescing = TimeSpan.FromSeconds(2);
    public static readonly string[] Channels = ["download_preparations", "downloads"];

    private sealed class Coalescer { public DateTimeOffset? Last; public IDisposable? Timer; }

    private sealed class ReadSlot
    {
        public CancellationTokenSource? Cts;
        public long Sequence;
    }

    private readonly AdminPreparedFilesApi _api;
    private readonly AdminMonitorApi? _monitor;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly IAdminRealtimeChannel? _realtime;
    private readonly IAdminScheduler _scheduler;
    private readonly object _gate = new();
    private readonly ReadSlot _storageSlot = new(), _preparationsSlot = new(), _filesSlot = new(), _devicesSlot = new(), _historySlot = new();
    // One read slot per device: reading one device's downloads must never cancel or supersede another device's read.
    private readonly Dictionary<string, ReadSlot> _entrySlots = new(StringComparer.Ordinal);
    private AdminPreparedFilesSnapshot _state = new();
    private IDisposable? _channelLease;
    private IDisposable? _storageTimer, _maintenanceTimer, _failureTimer;
    private readonly Coalescer _storageRefresh = new(), _preparationRefresh = new();
    private long _blockEpoch;
    private readonly HashSet<string> _blockNeeds = new(StringComparer.Ordinal);
    private bool _disposed;

    public AdminPreparedFilesController(AdminPreparedFilesApi api, AdminMonitorAuthority authority,
        IAdminRealtimeChannel? realtime = null, IAdminScheduler? scheduler = null, AdminMonitorApi? monitor = null)
    {
        _api = api;
        _monitor = monitor;
        _realtime = realtime;
        _scheduler = scheduler ?? new SystemAdminScheduler();
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnRetired;
    }

    public event Action? Changed;
    public event Action? Retired;
    public AdminPreparedFilesSnapshot State { get { lock (_gate) return _state; } }
    public AdminPreparedFilesApi Api => _api;
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();
    public bool IsActionCurrent(MonitorAuthorityLease? lease) => !_disposed && _lifetime.IsCurrent(lease);

    // ---- Opening and navigation -------------------------------------------------------------------

    public async Task OpenAsync(string? tab, string? location, bool staleDevices = false)
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        var active = NormalizeTab(tab);
        lock (_gate)
        {
            if (_disposed) return;
            StopLiveUpdates();
            _blockNeeds.Clear();
            _state = new AdminPreparedFilesSnapshot
            {
                Owner = owner, ActiveTab = active,
                FileQuery = new AdminPreparedFileQuery { Location = AdminPreparedFilesContract.IsLocation(location) ? location : null },
                DeviceQuery = new AdminDownloadDeviceQuery { Stale = staleDevices },
            };
        }
        Raise();
        StartLiveUpdates(owner);
        // The overview feeds every tab and the queue feeds the tab badge, so both are read whichever tab opens.
        var loads = new List<Task> { LoadStorageAsync(owner), LoadPreparationsAsync(owner) };
        if (active is not ("storage" or "preparation")) loads.Add(LoadActiveAsync(owner, active));
        await Task.WhenAll(loads).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows <paramref name="tab"/> with the given starting queries (the official tabs remount with their own defaults). The queries
    /// of the tabs being left are reset without any read, so leaving a filtered tab never waits on, or starts, a reload of it; only
    /// the list of the tab that is shown is read.
    /// </summary>
    public Task ActivateTabAsync(string tab, AdminPreparedFileQuery files, AdminDownloadDeviceQuery devices, AdminDownloadHistoryQuery history)
    {
        var active = NormalizeTab(tab);
        files = files.Normalize(); devices = devices.Normalize(); history = history.Normalize();
        MonitorAuthorityLease? owner;
        bool reload;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return Task.CompletedTask;
            var next = _state with { ActiveTab = active };
            var queryChanged = false;
            if (next.FileQuery != files) { next = next with { FileQuery = files, Files = new() }; queryChanged |= active == "files"; }
            if (next.DeviceQuery != devices)
            {
                next = next with { DeviceQuery = devices, Devices = new(), Entries = new Dictionary<string, AdminPreparedPagedState<AdminDownloadEntry>>() };
                queryChanged |= active == "devices";
            }
            if (next.HistoryQuery != history) { next = next with { HistoryQuery = history, History = new() }; queryChanged |= active == "history"; }
            reload = _state.ActiveTab != active || queryChanged;
            if (next == _state) return Task.CompletedTask;
            _state = next;
        }
        Raise();
        return reload ? LoadActiveAsync(owner, active) : Task.CompletedTask;
    }

    public Task ReloadStorageAsync() => Current(owner => LoadStorageAsync(owner));
    public Task ReloadPreparationsAsync() => Current(owner => LoadPreparationsAsync(owner));
    public Task ReloadFilesAsync() => Current(owner => LoadFilesAsync(owner, append: false));
    public Task ReloadDevicesAsync() => Current(owner => LoadDevicesAsync(owner, append: false));
    public Task ReloadHistoryAsync() => Current(owner => LoadHistoryAsync(owner, append: false));

    /// <summary>Reads again everything an unknown outcome left in doubt, which lifts the destructive block once each read succeeds.</summary>
    public Task ReloadBlockedAsync() => Current(owner =>
    {
        string[] needs;
        lock (_gate) needs = _blockNeeds.ToArray();
        var loads = new List<Task>();
        foreach (var need in needs.Append("storage").Distinct())
            loads.Add(need switch
            {
                "storage" => LoadStorageAsync(owner),
                "preparations" => LoadPreparationsAsync(owner),
                "files" => LoadFilesAsync(owner, false),
                "devices" => LoadDevicesAsync(owner, false),
                "history" => LoadHistoryAsync(owner, false),
                "nodes" => ReadNodesForBlockAsync(),
                _ => Task.CompletedTask,
            });
        return Task.WhenAll(loads);
    });

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

    // ---- Actions ----------------------------------------------------------------------------------

    /// <summary>Pauses, resumes or cancels exactly the listed jobs (the rows the administrator confirmed or selected).</summary>
    public Task RunPreparationActionAsync(string action, IReadOnlyList<AdminDownloadPreparation> items, MonitorAuthorityLease? lease = null) => RunActionAsync("preparation-" + action,
        destructive: action == "cancel", blockNeeds: ["preparations"], pendingIds: items.Select(x => x.Id).ToArray(), lease: lease,
        send: async lease =>
        {
            var result = await _api.RunPreparationActionAsync(lease, action, items.Select(x => x.Id).ToArray(), lease.Token).ConfigureAwait(false);
            var applied = result.Any(x => x.Outcome == "applied");
            return new AdminPreparedActionState("preparation-" + action, applied ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                AdminPreparationPresentation.SummarizeAction(action, result));
        },
        failure: _ => Text.T($"web.admin.downloads.preparation.actionFailed.{action}"),
        reconcile: () => Task.WhenAll(ReloadPreparationsAsync(), ReloadStorageAsync()));

    public Task DeleteFilesAsync(IReadOnlyList<AdminPreparedFile> files, bool includeInUse, MonitorAuthorityLease? lease = null) => RunActionAsync("delete-files",
        destructive: true, blockNeeds: ["files"], pendingIds: null, lease: lease,
        send: async lease =>
        {
            var results = await _api.DeleteFilesAsync(lease, files.Select(x => x.Id).ToArray(), includeInUse, lease.Token).ConfigureAwait(false);
            var deleted = results.Where(r => r.Outcome is "deleted" or "requeued").ToArray();
            var failed = results.Count(r => r.Outcome == "failed");
            var refused = results.Count - deleted.Length - failed;
            var freed = deleted.Sum(r => r.Bytes);
            const string P = "web.admin.downloads.preparedFiles.";
            if (failed > 0)
                return new AdminPreparedActionState("delete-files", deleted.Length > 0 ? AdminActionOutcome.Partial : AdminActionOutcome.Failed,
                    deleted.Length > 0
                        ? Text.T(P + "deleteFailedSome", new
                        {
                            failed = Text.T(P + "deleteFailedPart", Presentation.Count(failed)),
                            deleted = Text.T(P + "deleteDeletedPart", Presentation.Count(deleted.Length)),
                        })
                        : Text.T(P + "deleteFailed", Presentation.Count(failed)), Error: true);
            if (deleted.Length > 0)
            {
                var message = Text.T(P + "deleted", Presentation.With(Presentation.Count(deleted.Length), ("size", Presentation.FormatBytes(freed))));
                return new("delete-files", refused > 0 ? AdminActionOutcome.Partial : AdminActionOutcome.Applied,
                    refused > 0 ? Text.T(P + "deletedSkipped", new { deleted = message, skipped = Text.T(P + "skippedPart", Presentation.Count(refused)) }) : message);
            }
            return new("delete-files", AdminActionOutcome.NoChange, Text.T(P + "nothingDeleted"));
        },
        failure: _ => Text.T("web.admin.downloads.preparedFiles.deleteError"),
        reconcile: () => Task.WhenAll(ReloadFilesAsync(), ReloadStorageAsync()));

    public Task CleanupLocationAsync(AdminDownloadStorageLocation location, MonitorAuthorityLease? lease = null) => RunActionAsync("cleanup",
        destructive: true, blockNeeds: [], pendingIds: null, lease: lease,
        send: async lease =>
        {
            var result = await _api.CleanupLocationAsync(lease, location.Key, lease.Token).ConfigureAwait(false);
            return new("cleanup", result.FreedBytes > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                result.FreedBytes > 0
                    ? Text.T("web.admin.downloads.storage.cleanUp.freed", new { size = Presentation.FormatBytes(result.FreedBytes), name = location.Name })
                    : Text.T("web.admin.downloads.storage.cleanUp.nothing", new { name = location.Name }));
        },
        failure: _ => Text.T("web.admin.downloads.storage.cleanUp.failed", new { name = location.Name }),
        reconcile: () => ReloadStorageAsync());

    public Task DeleteUntrackedAsync(AdminDownloadStorageLocation location, MonitorAuthorityLease? lease = null) => RunActionAsync("delete-untracked",
        destructive: true, blockNeeds: ["history"], pendingIds: null, lease: lease,
        send: async lease =>
        {
            var result = await _api.DeleteUntrackedAsync(lease, location.Key, lease.Token).ConfigureAwait(false);
            const string P = "web.admin.downloads.storage.untracked.";
            var deleted = Text.T(P + "deleted", Presentation.With(Presentation.Count(result.Files), ("size", Presentation.FormatBytes(result.Bytes))));
            if (result.FailedFiles > 0)
            {
                var failed = Text.T(P + "failed", Presentation.With(Presentation.Count(result.FailedFiles), ("name", location.Name)));
                return new("delete-untracked", result.Files > 0 ? AdminActionOutcome.Partial : AdminActionOutcome.Failed,
                    result.Files > 0 ? Text.T(P + "failedAndDeleted", new { failed, deleted }) : failed, Error: true);
            }
            return new("delete-untracked", result.Files > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                result.Files > 0 ? deleted : Text.T(P + "noneLeft"));
        },
        failure: error => error is ApiException { StatusCode: 503 }
            ? Text.T("web.admin.downloads.storage.untracked.listFailed", new { name = location.Name })
            : Text.T("web.admin.downloads.storage.untracked.deleteFailed"),
        // A 503 says the directory was never listed, so nothing was deleted and there is nothing in doubt.
        definite: error => error is ApiException { StatusCode: 503 },
        reconcile: () => Task.WhenAll(ReloadStorageAsync(), ReloadHistoryAsync()));

    public Task RevokeAsync(AdminDownloadRevokeTarget target, string reason, string deviceName, MonitorAuthorityLease? lease = null) => RunActionAsync("revoke",
        destructive: true, blockNeeds: ["devices", "history"], pendingIds: null, lease: lease,
        send: async lease =>
        {
            var result = await _api.RevokeAsync(lease, target, reason, lease.Token).ConfigureAwait(false);
            return new("revoke", result.Revoked > 0 ? AdminActionOutcome.Applied : AdminActionOutcome.NoChange,
                result.Revoked > 0
                    ? Text.T("web.admin.downloads.deviceCopies.revokeDialog.revoked", Presentation.With(Presentation.Count(result.Revoked),
                        ("device", string.IsNullOrEmpty(deviceName) ? Text.T("web.admin.downloads.deviceCopies.revokeDialog.toastDevice") : deviceName)))
                    : Text.T("web.admin.downloads.deviceCopies.revokeDialog.alreadyRevoked"));
        },
        failure: _ => Text.T("web.admin.downloads.deviceCopies.revokeDialog.failed"),
        reconcile: async () =>
        {
            string[] keys;
            lock (_gate) keys = _state.Entries.Keys.ToArray();
            await Task.WhenAll(ReloadDevicesAsync(), ReloadStorageAsync(), ReloadHistoryAsync()).ConfigureAwait(false);
            foreach (var key in keys) await ReloadEntriesForKeyAsync(key).ConfigureAwait(false);
        });

    /// <summary>Reads the node list once so an unknown node write stops blocking; a failed read leaves the block and the Reload control in place.</summary>
    private async Task ReadNodesForBlockAsync()
    {
        try
        {
            using var action = CaptureAction();
            await ListNodesCoreAsync(action).ConfigureAwait(false);
        }
        catch (Exception) { /* the block stays until a read succeeds */ }
    }
    public Task<IReadOnlyList<AdminPreparedFilesNode>> ListNodesAsync(MonitorAuthorityLease action) => ListNodesCoreAsync(action);

    private async Task<IReadOnlyList<AdminPreparedFilesNode>> ListNodesCoreAsync(MonitorAuthorityLease action)
    {
        var nodes = await _api.ListNodesAsync(action, action.Token).ConfigureAwait(false);
        if (IsActionCurrent(action)) SatisfyBlock("nodes", _blockEpoch);
        return nodes;
    }

    /// <summary>
    /// Saves the changed node location fields once. A refusal or stale validator leaves everything as it was and keeps the draft;
    /// a timeout or transport failure after sending is an unknown outcome that blocks further writes until the node is read again.
    /// </summary>
    public async Task<AdminNodeSaveResult> SaveNodeLocationAsync(MonitorAuthorityLease action, AdminPreparedFilesNode node, AdminNodeLocationChange change)
    {
        if (!IsActionCurrent(action)) return new(AdminActionOutcome.Failed, "");
        lock (_gate)
        {
            if (_state.ActionPending || _state.DestructiveBlocked)
                return new(AdminActionOutcome.Failed, Text.T("configure.native.preparedFiles.blocked"));
            _state = _state with { ActionPending = true, LastAction = null };
        }
        Raise();
        try
        {
            var saved = await _api.UpdateNodeLocationAsync(action, node, change, action.Token).ConfigureAwait(false);
            // The write is known to have been applied, so it is reported even when the editor was dismissed (its lease ended) before the answer arrived.
            if (!OwnerCurrent()) return new(AdminActionOutcome.Failed, "");
            var message = Text.T("web.admin.nodes.hooks.nodes.saved");
            lock (_gate) _state = _state with { ActionPending = false, LastAction = new("save-location", AdminActionOutcome.Applied, message) };
            Raise();
            await ReloadStorageAsync().ConfigureAwait(false);
            return new(AdminActionOutcome.Applied, message, saved);
        }
        catch (Exception error)
        {
            // A retired page authority cleared the state with it; a dismissed editor (its lease ended) after the request was sent is an unknown outcome, never silently dropped.
            if (!OwnerCurrent()) return new(AdminActionOutcome.Failed, "");
            var kind = AdminFailures.Classify(error, action);
            var stale = kind is AdminFailureKind.PreconditionFailed or AdminFailureKind.PreconditionRequired or AdminFailureKind.Conflict;
            if (IsRefusal(error, kind))
            {
                var message = stale ? Text.T("configure.native.preparedFiles.nodeStale") : ProblemMessage(error, Text.T("web.admin.nodes.hooks.nodes.changeUnconfirmed"));
                lock (_gate) _state = _state with { ActionPending = false, LastAction = new("save-location", AdminActionOutcome.Failed, message) };
                Raise();
                return new(AdminActionOutcome.Failed, message, Stale: stale);
            }
            var unknown = Text.T("web.admin.nodes.hooks.nodes.changeUnconfirmed") + " " + Text.T("configure.native.preparedFiles.unknownOutcome");
            long epoch;
            lock (_gate)
            {
                epoch = ++_blockEpoch;
                _blockNeeds.Clear(); _blockNeeds.Add("storage"); _blockNeeds.Add("nodes");
                _state = _state with { ActionPending = false, DestructiveBlocked = true, LastAction = new("save-location", AdminActionOutcome.Unknown, unknown) };
            }
            Raise();
            try { await ReloadStorageAsync().ConfigureAwait(false); } catch (Exception) { /* the Reload control stays available */ }
            return new(AdminActionOutcome.Unknown, unknown, Stale: true);
        }
    }

    /// <summary>The newest FFmpeg rows logged for one preparation job (details panel console).</summary>
    public async Task<IReadOnlyList<AdminActivityLogRow>> ReadFfmpegRowsAsync(string logSessionId)
    {
        if (_monitor == null) throw new InvalidOperationException("FFmpeg logs are unavailable.");
        MonitorAuthorityLease? owner;
        lock (_gate) owner = _state.Owner;
        if (owner == null || !_lifetime.IsCurrent(owner)) throw new MonitorAuthorityRetiredException();
        var rows = await _monitor.ListActivityFfmpegRowsAsync(owner, logSessionId, owner.Token).ConfigureAwait(false);
        if (!_lifetime.IsCurrent(owner)) throw new MonitorAuthorityRetiredException();
        return rows;
    }

    public void DismissNotice()
    {
        lock (_gate) { if (_state.LastAction == null) return; _state = _state with { LastAction = null }; }
        Raise();
    }

    // ---- Reads ------------------------------------------------------------------------------------

    private Task LoadActiveAsync(MonitorAuthorityLease owner, string tab) => tab switch
    {
        "preparation" => LoadPreparationsAsync(owner), "files" => LoadFilesAsync(owner, false),
        "devices" => LoadDevicesAsync(owner, false), "history" => LoadHistoryAsync(owner, false), _ => LoadStorageAsync(owner),
    };

    private async Task LoadStorageAsync(MonitorAuthorityLease owner)
    {
        var (sequence, token, epoch) = Begin(_storageSlot, owner.Token, () => _state = _state with { Storage = _state.Storage with { Loading = true, Error = null, ErrorKind = null } });
        try
        {
            var value = await _api.GetStorageAsync(owner, token).ConfigureAwait(false);
            if (Publish(_storageSlot, sequence, owner, () => _state = _state with { Storage = new() { Loaded = true, Value = value } }))
                SatisfyBlock("storage", epoch);
        }
        catch (Exception error)
        {
            PublishFailure(_storageSlot, sequence, owner, error, () => _state = _state with
            {
                Storage = _state.Storage with { Loading = false, Loaded = true, Error = Text.T("web.admin.downloads.storage.loadError"), ErrorKind = AdminFailures.Classify(error, owner) },
            });
        }
        finally { ArmStoragePoll(owner); }
    }

    private async Task LoadPreparationsAsync(MonitorAuthorityLease owner)
    {
        var (sequence, token, epoch) = Begin(_preparationsSlot, owner.Token, () => _state = _state with { PreparationsLoading = true, PreparationsError = null });
        try
        {
            var value = await _api.ListPreparationsAsync(owner, token).ConfigureAwait(false);
            if (Publish(_preparationsSlot, sequence, owner, () => _state = _state with
            {
                PreparationsLoading = false, PreparationsLoaded = true, Preparations = value.Items, PreparationCounts = value.Counts, PreparationsError = null,
            }))
            {
                SatisfyBlock("preparations", epoch);
                ArmPreparationTimers(owner);
            }
        }
        catch (Exception error)
        {
            PublishFailure(_preparationsSlot, sequence, owner, error, () => _state = _state with
            {
                PreparationsLoading = false, PreparationsLoaded = true, PreparationsError = Text.T("web.admin.downloads.preparation.unavailable"),
            });
            // A failed read never stops the cadence: an expired failure must still age out and the queue is read again.
            ArmPreparationTimers(owner);
        }
    }

    private async Task LoadFilesAsync(MonitorAuthorityLease owner, bool append)
    {
        AdminPreparedFileQuery query; string? cursor; int pages, walk;
        lock (_gate) { query = _state.FileQuery; cursor = append ? _state.Files.NextCursor : null; pages = append ? _state.Files.PageCount : 0; walk = Math.Max(1, _state.Files.PageCount); }
        if (append && (cursor == null || pages >= MaxRetainedPages)) { MarkTruncated("files", pages); return; }
        var (sequence, token, epoch) = Begin(_filesSlot, owner.Token, () => _state = _state with { Files = _state.Files with { Loading = true, Error = null, ErrorKind = null } });
        try
        {
            var first = await _api.ListFilesAsync(owner, query, cursor, token).ConfigureAwait(false);
            // A re-read (not "Show more") reads every page the administrator had loaded, as the official invalidation refetches them all.
            var walked = append ? null : await WalkAsync(first, walk, next => _api.ListFilesAsync(owner, query, next, token), x => x.Id).ConfigureAwait(false);
            if (Publish(_filesSlot, sequence, owner, () =>
            {
                if (_state.FileQuery != query) return;
                _state = _state with { Files = walked ?? NextPaged(_state.Files, first, pages, x => x.Id) };
            })) SatisfyBlock("files", epoch);
        }
        catch (Exception error) { PublishFailure(_filesSlot, sequence, owner, error, () => _state = _state with { Files = FailedPaged(_state.Files, Text.T("web.admin.downloads.preparedFiles.loadError"), error, owner) }); }
    }

    private async Task LoadDevicesAsync(MonitorAuthorityLease owner, bool append)
    {
        AdminDownloadDeviceQuery query; string? cursor; int pages, walk;
        lock (_gate) { query = _state.DeviceQuery; cursor = append ? _state.Devices.NextCursor : null; pages = append ? _state.Devices.PageCount : 0; walk = Math.Max(1, _state.Devices.PageCount); }
        if (append && (cursor == null || pages >= MaxRetainedPages)) { MarkTruncated("devices", pages); return; }
        var (sequence, token, epoch) = Begin(_devicesSlot, owner.Token, () => _state = _state with { Devices = _state.Devices with { Loading = true, Error = null, ErrorKind = null } });
        try
        {
            var first = await _api.ListDevicesAsync(owner, query, cursor, token).ConfigureAwait(false);
            var walked = append ? null : await WalkAsync(first, walk, next => _api.ListDevicesAsync(owner, query, next, token), DeviceKey).ConfigureAwait(false);
            if (Publish(_devicesSlot, sequence, owner, () =>
            {
                if (_state.DeviceQuery != query) return;
                _state = _state with { Devices = walked ?? NextPaged(_state.Devices, first, pages, DeviceKey) };
            })) SatisfyBlock("devices", epoch);
        }
        catch (Exception error) { PublishFailure(_devicesSlot, sequence, owner, error, () => _state = _state with { Devices = FailedPaged(_state.Devices, Text.T("web.admin.downloads.deviceCopies.loadError"), error, owner) }); }
    }

    private async Task LoadHistoryAsync(MonitorAuthorityLease owner, bool append)
    {
        AdminDownloadHistoryQuery query; string? cursor; int pages, walk;
        lock (_gate) { query = _state.HistoryQuery; cursor = append ? _state.History.NextCursor : null; pages = append ? _state.History.PageCount : 0; walk = Math.Max(1, _state.History.PageCount); }
        if (append && (cursor == null || pages >= MaxRetainedPages)) { MarkTruncated("history", pages); return; }
        var (sequence, token, epoch) = Begin(_historySlot, owner.Token, () => _state = _state with { History = _state.History with { Loading = true, Error = null, ErrorKind = null } });
        try
        {
            var first = await _api.ListHistoryAsync(owner, query, cursor, token).ConfigureAwait(false);
            var walked = append ? null : await WalkAsync(first, walk, next => _api.ListHistoryAsync(owner, query, next, token), x => x.Id).ConfigureAwait(false);
            if (Publish(_historySlot, sequence, owner, () =>
            {
                if (_state.HistoryQuery != query) return;
                _state = _state with { History = walked ?? NextPaged(_state.History, first, pages, x => x.Id) };
            })) SatisfyBlock("history", epoch);
        }
        catch (Exception error) { PublishFailure(_historySlot, sequence, owner, error, () => _state = _state with { History = FailedPaged(_state.History, Text.T("web.admin.downloads.history.loadFailed"), error, owner) }); }
    }

    private Task ReloadEntriesForKeyAsync(string key) => Current(owner =>
    {
        AdminDownloadDevice? device;
        lock (_gate) device = _state.Devices.Items.FirstOrDefault(x => DeviceKey(x) == key);
        return device == null ? Task.CompletedTask : LoadEntriesAsync(owner, device, append: false);
    });

    private ReadSlot EntrySlot(string key)
    {
        lock (_gate)
        {
            if (!_entrySlots.TryGetValue(key, out var slot)) _entrySlots[key] = slot = new ReadSlot();
            return slot;
        }
    }

    private async Task LoadEntriesAsync(MonitorAuthorityLease owner, AdminDownloadDevice device, bool append)
    {
        var key = DeviceKey(device); AdminPreparedPagedState<AdminDownloadEntry> current;
        lock (_gate) current = _state.Entries.TryGetValue(key, out var value) ? value : new();
        if (append && (current.NextCursor == null || current.PageCount >= MaxRetainedPages)) return;
        var walk = Math.Max(1, current.PageCount);
        var (sequence, token, _) = Begin(EntrySlot(key), owner.Token, () => UpdateEntries(key, current with { Loading = true, Error = null, ErrorKind = null }));
        var slot = EntrySlot(key);
        try
        {
            var id = new AdminDownloadEntryQuery(device.UserId, device.ProfileId, device.DeviceId);
            var first = await _api.ListEntriesAsync(owner, id, append ? current.NextCursor : null, token).ConfigureAwait(false);
            var walked = append ? null : await WalkAsync(first, walk, next => _api.ListEntriesAsync(owner, id, next, token), x => x.Id).ConfigureAwait(false);
            Publish(slot, sequence, owner, () => UpdateEntries(key, walked ?? NextPaged(current, first, current.PageCount, x => x.Id)));
        }
        catch (Exception error)
        {
            PublishFailure(slot, sequence, owner, error, () => UpdateEntries(key, FailedPaged(current, Text.T("configure.native.preparedFiles.entriesLoadError"), error, owner)));
        }
    }

    /// <summary>
    /// Re-reads up to <paramref name="pages"/> pages from the first (each continues from the cursor the previous answer gave), so a
    /// live refresh keeps every page the administrator had loaded instead of collapsing the list to its first page.
    /// </summary>
    private async Task<AdminPreparedPagedState<T>> WalkAsync<T>(AdminCursorPage<T> first, int pages, Func<string?, Task<AdminCursorPage<T>>> read, Func<T, string> key)
    {
        var result = NextPaged<T>(null, first, 0, key);
        for (var index = 1; index < pages && result.NextCursor != null; index++)
        {
            var page = await read(result.NextCursor).ConfigureAwait(false);
            result = NextPaged(result, page, result.PageCount, key);
        }
        return result;
    }

    private AdminPreparedPagedState<T> NextPaged<T>(AdminPreparedPagedState<T>? existing, AdminCursorPage<T> page, int previousPages, Func<T, string> key)
    {
        var count = previousPages + 1;
        var cursors = NextCursors(existing?.SeenCursors, page.NextCursor);
        return new()
        {
            Loaded = true, Items = existing == null ? page.Items : Merge(existing.Items, page.Items, key), PageCount = count,
            NextCursor = count >= MaxRetainedPages ? null : page.NextCursor, Truncated = count >= MaxRetainedPages && page.HasMore, SeenCursors = cursors,
        };
    }

    private static AdminPreparedPagedState<T> FailedPaged<T>(AdminPreparedPagedState<T> current, string message, Exception error, MonitorAuthorityLease owner) =>
        current with { Loading = false, Loaded = true, Error = message, ErrorKind = AdminFailures.Classify(error, owner) };

    // ---- Action plumbing --------------------------------------------------------------------------

    /// <summary>
    /// Runs one write under <paramref name="lease"/> when the caller supplies one (a confirmation passes the authority it was opened
    /// under, and the caller owns and disposes it), otherwise under a lease captured now. A lease that is no longer current sends nothing.
    /// </summary>
    private async Task RunActionAsync(string kind, bool destructive, string[] blockNeeds, string[]? pendingIds,
        Func<MonitorAuthorityLease, Task<AdminPreparedActionState>> send, Func<Exception, string> failure, Func<Task> reconcile,
        Func<Exception, bool>? definite = null, MonitorAuthorityLease? lease = null)
    {
        MonitorAuthorityLease action;
        if (lease != null) action = lease;
        else
        {
            try { action = CaptureAction(); }
            catch (OperationCanceledException) { return; }
        }
        try
        {
            if (!IsActionCurrent(action)) return;
            lock (_gate)
            {
                if (_state.ActionPending || destructive && _state.DestructiveBlocked) return;
                _state = _state with { ActionPending = true, LastAction = null, PendingPreparationIds = pendingIds == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(pendingIds, StringComparer.Ordinal) };
            }
            Raise();
            try
            {
                var result = await send(action).ConfigureAwait(false);
                // Only a retired page authority drops the answer (the state was cleared with it); a lease that ended on its own still reports what the server did.
                if (!OwnerCurrent()) return;
                lock (_gate) _state = _state with { ActionPending = false, PendingPreparationIds = new HashSet<string>(StringComparer.Ordinal), LastAction = result };
                Raise();
                await reconcile().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (!OwnerCurrent()) return;
                var errorKind = AdminFailures.Classify(error, action);
                var refusal = IsRefusal(error, errorKind) || definite?.Invoke(error) == true;
                var message = failure(error);
                if (refusal && error is ApiException api && !(definite?.Invoke(error) == true))
                    message = ProblemMessage(error, message);
                AdminPreparedActionState state;
                if (refusal) state = new(kind, AdminActionOutcome.Failed, message);
                else
                {
                    state = new(kind, AdminActionOutcome.Unknown, message + " " + Text.T("configure.native.preparedFiles.unknownOutcome"));
                    lock (_gate)
                    {
                        _blockEpoch++;
                        _blockNeeds.Clear(); _blockNeeds.Add("storage");
                        foreach (var need in blockNeeds) _blockNeeds.Add(need);
                    }
                }
                lock (_gate)
                {
                    _state = _state with
                    {
                        ActionPending = false, PendingPreparationIds = new HashSet<string>(StringComparer.Ordinal), LastAction = state,
                        DestructiveBlocked = !refusal || _state.DestructiveBlocked,
                    };
                }
                Raise();
                try { await reconcile().ConfigureAwait(false); } catch (Exception) { /* Reload stays reachable */ }
            }
        }
        finally { if (lease == null) action.Dispose(); }
    }

    /// <summary>Whether the page's own authority (the one that owns every read) is still current.</summary>
    private bool OwnerCurrent()
    {
        lock (_gate) return !_disposed && _state.Owner != null && _lifetime.IsCurrent(_state.Owner);
    }

    /// <summary>
    /// A definite refusal: the server answered 4xx (other than 408), so it did not apply the write. Everything else, including
    /// 5xx answers, timeouts and transport failures after sending, leaves the outcome unknown.
    /// </summary>
    internal static bool IsRefusal(Exception error, AdminFailureKind kind) =>
        error is ArgumentException or AdminValidatorException || // refused locally, before anything was sent
        error is ApiException { StatusCode: >= 400 and < 500 and not 408 } ||
        kind is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden or AdminFailureKind.NotFound or AdminFailureKind.Conflict
            or AdminFailureKind.PreconditionFailed or AdminFailureKind.PreconditionRequired or AdminFailureKind.Validation
            or AdminFailureKind.RateLimited or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted
            && error is ApiException { StatusCode: < 500 and not 408 };

    private static string ProblemMessage(Exception error, string fallback)
    {
        if (error is not ApiException api) return fallback;
        var text = ApiProblemText.Display(api);
        return string.IsNullOrWhiteSpace(text) ? fallback : fallback + " " + text;
    }

    private void SatisfyBlock(string need, long epoch)
    {
        bool changed;
        lock (_gate)
        {
            if (epoch != _blockEpoch || !_blockNeeds.Remove(need)) return;
            var blocked = _blockNeeds.Count > 0;
            changed = _state.DestructiveBlocked != blocked;
            if (changed) _state = _state with { DestructiveBlocked = blocked };
        }
        if (changed) Raise();
    }

    private (long Sequence, CancellationToken Token, long Epoch) Begin(ReadSlot slot, CancellationToken owner, Action mutate)
    {
        CancellationToken token; long value, epoch;
        lock (_gate)
        {
            slot.Cts?.Cancel(); slot.Cts?.Dispose(); slot.Cts = CancellationTokenSource.CreateLinkedTokenSource(owner);
            token = slot.Cts.Token; value = ++slot.Sequence; epoch = _blockEpoch; mutate();
        }
        Raise(); return (value, token, epoch);
    }

    private bool Publish(ReadSlot slot, long sequence, MonitorAuthorityLease owner, Action mutate)
    {
        lock (_gate)
        {
            if (_disposed || sequence != slot.Sequence || !_lifetime.IsCurrent(owner)) return false;
            mutate();
        }
        Raise();
        return true;
    }

    private void PublishFailure(ReadSlot slot, long sequence, MonitorAuthorityLease owner, Exception error, Action mutate)
    {
        var kind = AdminFailures.Classify(error, owner);
        // A superseded, cancelled or retired read raises no error: only the newest read of this data may report a failure.
        if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
        Publish(slot, sequence, owner, mutate);
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
            _state = list switch
            {
                "files" => _state with { Files = _state.Files with { Truncated = true, NextCursor = null } },
                "devices" => _state with { Devices = _state.Devices with { Truncated = true, NextCursor = null } },
                _ => _state with { History = _state.History with { Truncated = true, NextCursor = null } },
            };
        }
        Raise();
    }

    private Task Current(Func<MonitorAuthorityLease, Task> run)
    {
        MonitorAuthorityLease? owner;
        lock (_gate) owner = _state.Owner;
        return owner != null && _lifetime.IsCurrent(owner) ? run(owner) : Task.CompletedTask;
    }

    // ---- Live updates -----------------------------------------------------------------------------

    private void StartLiveUpdates(MonitorAuthorityLease owner)
    {
        if (_realtime == null) return;
        lock (_gate)
        {
            if (_disposed || _channelLease != null) return;
            _realtime.SnapshotReceived += OnSnapshot;
            _realtime.EventReceived += OnEvent;
            _channelLease = _realtime.Subscribe(Channels);
        }
    }

    private void StopLiveUpdates()
    {
        IDisposable?[] timers;
        IDisposable? lease;
        lock (_gate)
        {
            timers = [_storageTimer, _maintenanceTimer, _failureTimer, _storageRefresh.Timer, _preparationRefresh.Timer];
            _storageTimer = _maintenanceTimer = _failureTimer = null;
            _storageRefresh.Timer = _preparationRefresh.Timer = null;
            _storageRefresh.Last = _preparationRefresh.Last = null;
            lease = _channelLease; _channelLease = null;
        }
        foreach (var timer in timers) timer?.Dispose();
        if (_realtime != null)
        {
            _realtime.SnapshotReceived -= OnSnapshot;
            _realtime.EventReceived -= OnEvent;
        }
        lease?.Dispose();
    }
    // A subscription or reconnect means events may have been missed: read again.
    private void OnSnapshot(string channel, JsonElement data)
    {
        if (channel == "download_preparations") { RefreshPreparations(); RefreshStorageViews(); }
        else if (channel == "downloads") RefreshStorageViews();
    }

    private void OnEvent(string channel, string name, JsonElement data)
    {
        switch (channel)
        {
            case "download_preparations":
                if (name == "download_storage.changed") { RefreshStorageViews(); return; }
                if (name == "download_preparation.progress" && TryPatchProgress(data)) return;
                RefreshPreparations();
                return;
            case "downloads":
                RefreshStorageViews();
                return;
        }
    }

    /// <summary>Patches a progress reading into a running row; false when the list must be read again.</summary>
    private bool TryPatchProgress(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String ||
            !data.TryGetProperty("progress", out var p) || p.ValueKind != JsonValueKind.Object) return false;
        AdminDownloadPreparationProgress progress;
        try
        {
            if (!p.TryGetProperty("encoded_seconds", out var encoded) || encoded.ValueKind != JsonValueKind.Number ||
                !p.TryGetProperty("duration_seconds", out var duration) || duration.ValueKind != JsonValueKind.Number ||
                !p.TryGetProperty("speed", out var speed) || speed.ValueKind != JsonValueKind.Number ||
                !p.TryGetProperty("updated_at", out var updated) || updated.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(updated.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
                return false;
            progress = new() { EncodedSeconds = encoded.GetDouble(), DurationSeconds = duration.GetDouble(), Speed = speed.GetDouble(), UpdatedAt = at };
        }
        catch (FormatException) { return false; }
        var id = idElement.GetString();
        var visible = false;
        lock (_gate)
        {
            if (_disposed || _state.Owner == null || !_state.PreparationsLoaded) return true; // nothing showing the list: nothing to refresh
            var current = _state.Preparations.FirstOrDefault(x => x.Id == id);
            if (current == null || current.State != "running") return false;
            var items = _state.Preparations.Select(x => x.Id == id ? CloneWithProgress(x, progress) : x).ToArray();
            _state = _state with { Preparations = items };
            visible = _state.ActiveTab == "preparation";
        }
        // A reading for a queue nobody is looking at changes only the retained list: nothing is told to draw.
        if (visible) Raise();
        return true;
    }

    private static AdminDownloadPreparation CloneWithProgress(AdminDownloadPreparation job, AdminDownloadPreparationProgress progress) => new()
    {
        Id = job.Id, State = job.State, Format = job.Format, QueuePosition = job.QueuePosition, LogSessionId = job.LogSessionId, MediaFileId = job.MediaFileId,
        ContentId = job.ContentId, EpisodeId = job.EpisodeId, MediaTitle = job.MediaTitle, MediaType = job.MediaType, SeriesName = job.SeriesName,
        EpisodeName = job.EpisodeName, SeasonNumber = job.SeasonNumber, EpisodeNumber = job.EpisodeNumber, Source = job.Source, Output = job.Output,
        Worker = job.Worker, Attempts = job.Attempts, MaxAttempts = job.MaxAttempts, Error = job.Error, CreatedAt = job.CreatedAt, StartedAt = job.StartedAt,
        FailedAt = job.FailedAt, NextRetryAt = job.NextRetryAt, PausedAt = job.PausedAt, Progress = progress, ProgressUnavailable = false, Requesters = job.Requesters,
    };

    /// <summary>Leading-plus-trailing coalescing, so a burst of events costs at most two reads.</summary>
    private void Coalesce(Coalescer coalescer, Func<MonitorAuthorityLease, Task> read)
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return;
            var now = _scheduler.UtcNow;
            if (coalescer.Last is { } previous && now - previous < EventCoalescing)
            {
                if (coalescer.Timer != null) return;
                var captured = owner;
                coalescer.Timer = _scheduler.Schedule(previous + EventCoalescing - now, () =>
                {
                    lock (_gate)
                    {
                        coalescer.Timer = null;
                        if (_disposed || !ReferenceEquals(_state.Owner, captured) || !_lifetime.IsCurrent(captured)) return;
                        coalescer.Last = _scheduler.UtcNow;
                    }
                    _ = ReadQuietlyAsync(read, captured);
                });
                return;
            }
            coalescer.Last = now;
        }
        _ = ReadQuietlyAsync(read, owner);
    }

    private static async Task ReadQuietlyAsync(Func<MonitorAuthorityLease, Task> read, MonitorAuthorityLease owner)
    {
        try { await read(owner).ConfigureAwait(false); }
        catch (Exception) { /* a failed refresh keeps the data on screen and the cadence running */ }
    }

    private void RefreshStorageViews() => Coalesce(_storageRefresh, owner =>
    {
        string tab;
        lock (_gate) tab = _state.ActiveTab;
        var loads = new List<Task> { LoadStorageAsync(owner) };
        switch (tab)
        {
            case "files": loads.Add(LoadFilesAsync(owner, false)); break;
            case "devices":
                loads.Add(LoadDevicesAsync(owner, false));
                string[] keys; lock (_gate) keys = _state.Entries.Keys.ToArray();
                loads.AddRange(keys.Select(ReloadEntriesForKeyAsync));
                break;
            case "history": loads.Add(LoadHistoryAsync(owner, false)); break;
        }
        return Task.WhenAll(loads);
    });

    private void RefreshPreparations() => Coalesce(_preparationRefresh, owner => LoadPreparationsAsync(owner));
    private void ArmStoragePoll(MonitorAuthorityLease owner)
    {
        IDisposable? replaced;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_state.Owner, owner) || !_lifetime.IsCurrent(owner)) return;
            replaced = _storageTimer;
            _storageTimer = _scheduler.Schedule(StoragePoll, () =>
            {
                lock (_gate) { if (_disposed || !ReferenceEquals(_state.Owner, owner) || !_lifetime.IsCurrent(owner)) return; }
                _ = LoadStorageAsync(owner);
            });
        }
        replaced?.Dispose();
    }

    /// <summary>
    /// A plain timer rather than a reset-on-update interval: progress patches never postpone it. It runs only while jobs are in
    /// flight, plus once when the oldest listed failure leaves the server's 24 hour list (a minute later if already expired).
    /// </summary>
    private void ArmPreparationTimers(MonitorAuthorityLease owner)
    {
        IDisposable? oldMaintenance, oldFailure;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_state.Owner, owner) || !_lifetime.IsCurrent(owner)) return;
            oldFailure = _failureTimer; _failureTimer = null;
            oldMaintenance = null;
            if (AdminPreparationPresentation.ActiveCount(_state.PreparationCounts) > 0)
            {
                if (_maintenanceTimer == null)
                    _maintenanceTimer = _scheduler.Schedule(PreparationMaintenance, () =>
                    {
                        lock (_gate) { if (_disposed || !ReferenceEquals(_state.Owner, owner)) return; _maintenanceTimer = null; }
                        _ = LoadPreparationsAsync(owner);
                    });
            }
            else { oldMaintenance = _maintenanceTimer; _maintenanceTimer = null; }
            if (FailureReadDelay(_state.Preparations, _scheduler.UtcNow) is { } delay)
            {
                _failureTimer = _scheduler.Schedule(delay, () =>
                {
                    lock (_gate) { if (_disposed || !ReferenceEquals(_state.Owner, owner)) return; _failureTimer = null; }
                    _ = LoadPreparationsAsync(owner);
                });
            }
        }
        oldFailure?.Dispose();
        oldMaintenance?.Dispose();
    }

    /// <summary>
    /// How long until the oldest listed failure leaves the server's 24 hour list and the queue should be read once more; null when no
    /// failure is listed. A timestamp the clock cannot place (already past, further than two days away, or too close to the end of
    /// time to add a day to) falls back to the maintenance cadence, so no server time can throw here or schedule a tight loop.
    /// </summary>
    internal static TimeSpan? FailureReadDelay(IEnumerable<AdminDownloadPreparation> jobs, DateTimeOffset now)
    {
        if (AdminPreparationPresentation.NextFailureExpiry(jobs) is not { } expiry) return null;
        // A listed failure can expire at most a day away; a larger distance means this clock disagrees with the server's, so read on the maintenance cadence instead.
        var remaining = expiry - now;
        return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromDays(2) ? remaining + TimeSpan.FromSeconds(1) : PreparationMaintenance;
    }

    // ---- Lifetime ---------------------------------------------------------------------------------

    private void OnRetired() { Clear(); Retired?.Invoke(); }

    private void Clear()
    {
        lock (_gate) { CancelReads(); _blockNeeds.Clear(); _state = new(); }
        StopLiveUpdates();
        Raise();
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; CancelReads(); _state = new(); }
        StopLiveUpdates();
        _lifetime.Dispose(); Changed = null; Retired = null;
    }

    private void CancelReads()
    {
        foreach (var slot in new[] { _storageSlot, _preparationsSlot, _filesSlot, _devicesSlot, _historySlot }.Concat(_entrySlots.Values))
        {
            try { slot.Cts?.Cancel(); } catch (ObjectDisposedException) { }
            slot.Cts?.Dispose(); slot.Cts = null; slot.Sequence++;
        }
    }

    private void Raise() => Changed?.Invoke();
    private static string NormalizeTab(string? tab) => tab is "preparation" or "files" or "devices" or "history" ? tab : "storage";
    public static string DeviceKey(AdminDownloadDevice device) => device.UserId + "/" + device.ProfileId + "/" + device.DeviceId;

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
