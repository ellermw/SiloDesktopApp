using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A toast the page shows: official title and optional description.</summary>
public sealed record AdminBackupNotice(string Title, string? Description, bool Error);

public sealed record AdminBackupsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public AdminBackupCapabilities? Capabilities { get; init; }
    public AdminBackupList? List { get; init; }
    /// <summary>The list read failed; the last good list stays shown when there is one.</summary>
    public Exception? ListError { get; init; }
    public IReadOnlyList<AdminBackupUpload>? Uploads { get; init; }
    public DateTimeOffset? TaskNextRunAt { get; init; }
    /// <summary>The time ("04:00") the backup task's daily trigger holds, as the server last answered.</summary>
    public string? TaskDailyTime { get; init; }
    /// <summary>
    /// A Start time saved in the last 30 s that the task's daily trigger has not taken yet. Until the server answering has moved the trigger,
    /// the task's next run belongs to the old schedule, so the page forecasts none (official useScheduleSettling).
    /// </summary>
    public bool ScheduleSettling { get; init; }
    public DateTimeOffset ListReadAt { get; init; }

    public bool SettingsLoaded { get; init; }
    public bool SettingsLoading { get; init; }
    public bool SettingsLoadFailed { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();
    public bool SecretConfigured { get; init; }
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool SettingsOutcomeUnknown { get; init; }
    public string? SettingsError { get; init; }

    public bool StartingBackup { get; init; }
    /// <summary>A command whose acknowledgement never arrived; its control stays blocked until an explicit refresh.</summary>
    public string? UnknownOutcome { get; init; }
    public string? DeletingBackupId { get; init; }
    public string? DeletingUploadId { get; init; }
    public string? CheckingUploadId { get; init; }
    public string? PreparingDownloadId { get; init; }
    public bool AcknowledgingKey { get; init; }
    public override string ToString() => nameof(AdminBackupsState);
}

/// <summary>
/// Page-owned Configure > Server > Backups state (official AdminBackups.tsx and its hooks at
/// 8d253adbf). Reads run only while the page is active and its window visible: the list every
/// 3 s while anything holds the backup lock and every 30 s otherwise, capabilities again once
/// 60 s stale, uploads every 3 s while one is being checked. A background tick never queues
/// behind a read in flight and waits while <c>deferBackground</c> says playback is starting;
/// foreground refreshes coalesce into one read after the one in flight. Every response is
/// applied only to its own owner and read generation. Commands are captured under a per-action
/// lease, sent once, and an unknown outcome blocks its control until an explicit refresh.
/// Events may arrive on any thread.
/// </summary>
public sealed class AdminBackupsController : IDisposable
{
    /// <summary>Keys the shared settings snapshot reads for this page (the S3 secret only as configured status).</summary>
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(Present.SettingKeys.Concat(Present.S3Keys).Append(Present.PrivateBucketKey).ToArray());
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly([Present.S3SecretKey]);
    internal static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CapabilitiesStale = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan CheckingInterval = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan DeferRetry = TimeSpan.FromSeconds(1);
    /// <summary>How long a saved Start time is waited for, and how often the task is read meanwhile.</summary>
    internal static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan SettleInterval = TimeSpan.FromSeconds(2);

    private readonly AdminBackupsApi _api;
    private readonly AdminServerSettingsApi _settingsApi;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly IAdminScheduler _scheduler;
    private readonly AdminBackupSignedDownload _download;
    private readonly bool _ownsDownload;
    private readonly object _gate = new();
    private AdminBackupsState _state = new();
    private AdminSettingsForm? _form;
    private AdminSettingsSnapshot? _snapshot;
    private IDisposable? _listTimer, _uploadTimer, _settleTimer;
    private string? _settleTime;
    private DateTimeOffset _settleUntil;
    private long _generation, _listReadSequence;
    private long? _reconcileAfterRead;
    private bool _listInFlight, _refreshAgain, _visible = true, _disposed;
    private bool _capabilitiesInFlight, _capabilitiesAgain, _uploadsInFlight, _uploadsAgain;
    private DateTimeOffset _capabilitiesReadAt;
    private (string Id, DateTimeOffset At)? _started;

    public AdminBackupsController(AdminBackupsApi api, AdminServerSettingsApi settingsApi, AdminServerSettingsStore store, AdminMonitorAuthority authority,
        IAdminScheduler? scheduler = null, AdminBackupSignedDownload? download = null)
    {
        _api = api; _settingsApi = settingsApi; _store = store; _scheduler = scheduler ?? new SystemAdminScheduler();
        _ownsDownload = download == null;
        _download = download ?? new AdminBackupSignedDownload();
        _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    public event Action<AdminBackupNotice>? Notified;
    /// <summary>Raised after an acknowledged settings save so the shell can refresh restart status.</summary>
    public event Action? Saved;
    /// <summary>True while background reads should wait (playback starting). Never consulted for foreground reads.</summary>
    public Func<bool> DeferBackground { get; set; } = static () => false;
    public AdminBackupsState State { get { lock (_gate) return _state; } }
    public bool IsCurrent(MonitorAuthorityLease? lease) => _lifetime.IsCurrent(lease);

    // ── Lifetime ─────────────────────────────────────────────────────

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            CancelTimers(); _settleTime = null; _form?.Discard(); _form = null; _snapshot = null; _started = null; _generation++;
            _listInFlight = false; _refreshAgain = false; _reconcileAfterRead = null;
            _capabilitiesInFlight = false; _capabilitiesAgain = false; _uploadsInFlight = false; _uploadsAgain = false;
            _state = new() { Owner = owner, Loading = true, SettingsLoading = true };
        }
        Changed?.Invoke();
        _store.Invalidate();
        var settings = LoadSettingsAsync(owner, preserveDraft: false);
        var capabilities = ReadCapabilitiesAsync(owner);
        var list = RefreshListAsync(owner, background: false);
        var schedule = ReadScheduleAsync(owner);
        await Task.WhenAll(settings, capabilities, list, schedule).ConfigureAwait(false);
    }

    /// <summary>Whether the page's window is visible; hidden pauses the timers and visible reads at once.</summary>
    public void SetVisible(bool visible)
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            if (_visible == visible) return;
            _visible = visible;
            owner = _lifetime.Owner;
            if (!visible) { CancelTimers(); return; }
        }
        if (owner is { IsCurrent: true })
        {
            _ = RefreshListAsync(owner, background: false);
            if (State.Capabilities is { Upload: true }) _ = ReadUploadsAsync(owner);
            // A Start time still being waited for resumes its reads; the read ends the wait if its 30 s passed while hidden.
            lock (_gate) { if (_settleTime == null) return; }
            _ = ReadScheduleAsync(owner);
        }
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }

    private void Clear()
    {
        lock (_gate)
        {
            CancelTimers(); _settleTime = null; _form?.Discard(); _form = null; _snapshot = null; _started = null; _generation++;
            _listInFlight = false; _refreshAgain = false; _reconcileAfterRead = null; _state = new();
            _capabilitiesInFlight = false; _capabilitiesAgain = false; _uploadsInFlight = false; _uploadsAgain = false;
        }
        Changed?.Invoke();
    }

    private void CancelTimers()
    {
        _listTimer?.Dispose(); _listTimer = null; _uploadTimer?.Dispose(); _uploadTimer = null;
        _settleTimer?.Dispose(); _settleTimer = null;
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Clear(); if (_ownsDownload) _download.Dispose(); Changed = null; Notified = null; Saved = null;
    }

    // ── Reads ────────────────────────────────────────────────────────

    /// <summary>An explicit refresh: only a successful list read started afterwards releases an unknown-outcome block.</summary>
    public Task RefreshAsync()
    {
        var owner = _lifetime.Owner;
        if (owner is not { IsCurrent: true }) return Task.CompletedTask;
        lock (_gate)
        {
            if (!_lifetime.IsCurrent(owner)) return Task.CompletedTask;
            if (_state.UnknownOutcome != null) _reconcileAfterRead = _listReadSequence;
        }
        return Task.WhenAll(RefreshListAsync(owner, background: false), ReadCapabilitiesAsync(owner));
    }

    private async Task RefreshListAsync(MonitorAuthorityLease owner, bool background)
    {
        long generation, readSequence;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            if (_listInFlight)
            {
                // A background tick never queues; a foreground refresh runs once after the read in flight.
                if (!background) _refreshAgain = true;
                return;
            }
            if (background && (!_visible || DeferBackground())) { ScheduleList(owner, DeferRetry); return; }
            _listInFlight = true; _refreshAgain = false;
            generation = _generation; readSequence = ++_listReadSequence;
        }
        AdminBackupList? list = null;
        Exception? failure = null;
        try { list = await _api.ListAsync(owner).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { failure = error; }
        var finishedNotice = (AdminBackupNotice?)null;
        bool again;
        lock (_gate)
        {
            if (_disposed || generation != _generation || !_lifetime.IsCurrent(owner)) return;
            _listInFlight = false;
            again = _refreshAgain; _refreshAgain = false;
            if (list != null)
            {
                _state = _state with { List = list, ListError = null, Loading = false, ListReadAt = _scheduler.UtcNow };
                if (_reconcileAfterRead is { } after && readSequence > after)
                {
                    _state = _state with { UnknownOutcome = null };
                    _reconcileAfterRead = null;
                }
                finishedNotice = StartedOutcome(list);
            }
            else if (failure != null) _state = _state with { ListError = failure, Loading = false };
        }
        Changed?.Invoke();
        if (finishedNotice != null) Notified?.Invoke(finishedNotice);
        var interval = list is { } read && (read.Busy == true || read.Active != null || Running(read)) ? BusyInterval : IdleInterval;
        if (failure != null) interval = _state.List is { } kept && (kept.Busy == true || kept.Active != null) ? BusyInterval : IdleInterval;
        if (again) _ = RefreshListAsync(owner, background: false);
        else ScheduleList(owner, interval);
        if (_scheduler.UtcNow - _capabilitiesReadAt >= CapabilitiesStale) _ = ReadCapabilitiesAsync(owner, background: true);
        if (_state.TaskNextRunAt is { } next && next <= _scheduler.UtcNow) _ = ReadScheduleAsync(owner);
    }

    /// <summary>Whether a backup is being taken now, on any server (official backupRunning).</summary>
    public static bool Running(AdminBackupList? list) => list != null && (list.Active?.Operation == "backup" || (list.Items ?? []).Any(item => item.Status == "in_progress"));

    /// <summary>How the backup this page started ended, read only from a list fetched after the start.</summary>
    private AdminBackupNotice? StartedOutcome(AdminBackupList list)
    {
        if (_started is not { } pending || _scheduler.UtcNow <= pending.At) return null;
        if (list.Busy == true || list.Active != null || Running(list)) return null;
        _started = null;
        if (list.LastRun?.Id != pending.Id) return null;
        return list.LastRun.Status switch
        {
            "complete" => new(AdminText.T("web.admin.backups.toast.finished"), null, false),
            "complete_with_warnings" => new(AdminText.T("web.admin.backups.toast.finishedWithWarnings"), AdminText.T("web.admin.backups.toast.finishedWithWarningsDescription"), false),
            _ => new(AdminText.T("web.admin.backups.toast.didntFinish"), list.LastRun.Error is { Length: > 0 } error ? AdminBackupValidation.SafeServerText(error) : null, true),
        };
    }

    private void ScheduleList(MonitorAuthorityLease owner, TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed || !_visible || !_lifetime.IsCurrent(owner)) return;
            _listTimer?.Dispose();
            _listTimer = _scheduler.Schedule(delay, () => _ = RefreshListAsync(owner, background: true));
        }
    }

    private async Task ReadCapabilitiesAsync(MonitorAuthorityLease owner, bool background = false)
    {
        long generation;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner) || background && !_visible) return;
            if (_capabilitiesInFlight) { if (!background) _capabilitiesAgain = true; return; }
            _capabilitiesInFlight = true;
            generation = _generation;
        }
        try
        {
            var capabilities = await _api.GetCapabilitiesAsync(owner).ConfigureAwait(false);
            var uploads = capabilities.Available && capabilities.Upload == true;
            Update(owner, generation, () => { _state = _state with { Capabilities = capabilities }; _capabilitiesReadAt = _scheduler.UtcNow; });
            if (uploads) await ReadUploadsAsync(owner).ConfigureAwait(false);
            else Update(owner, generation, () => _state = _state with { Uploads = null });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception)
        {
            // Without capabilities the page stays fail-closed: no upload or restore controls appear.
            Update(owner, generation, () => _capabilitiesReadAt = _scheduler.UtcNow);
        }
        finally
        {
            var again = false;
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _capabilitiesInFlight = false;
                    again = _capabilitiesAgain && !_disposed && _lifetime.IsCurrent(owner);
                    _capabilitiesAgain = false;
                }
            }
            if (again) _ = ReadCapabilitiesAsync(owner);
        }
    }

    private async Task ReadUploadsAsync(MonitorAuthorityLease owner, bool background = false)
    {
        long generation;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner) || background && !_visible) return;
            if (_uploadsInFlight) { if (!background) _uploadsAgain = true; return; }
            if (background && DeferBackground()) { ScheduleUploads(owner, DeferRetry); return; }
            _uploadsInFlight = true;
            generation = _generation;
        }
        try
        {
            var uploads = await _api.ListUploadsAsync(owner).ConfigureAwait(false);
            Update(owner, generation, () => _state = _state with { Uploads = uploads });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { /* Keep the last good uploads list after a transient failure. */ }
        finally
        {
            var again = false;
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _uploadsInFlight = false;
                    again = _uploadsAgain && !_disposed && _lifetime.IsCurrent(owner);
                    _uploadsAgain = false;
                    if (_state.Uploads?.Any(upload => upload.Status == "checking") == true) ScheduleUploads(owner, CheckingInterval);
                    else { _uploadTimer?.Dispose(); _uploadTimer = null; }
                }
            }
            if (again) _ = ReadUploadsAsync(owner);
        }
    }

    private void ScheduleUploads(MonitorAuthorityLease owner, TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed || !_visible || !_lifetime.IsCurrent(owner)) return;
            _uploadTimer?.Dispose();
            _uploadTimer = _scheduler.Schedule(delay, () => _ = ReadUploadsAsync(owner, background: true));
        }
    }

    /// <summary>Reads the uploads list again (after the upload dialog changed it).</summary>
    public Task RefreshUploadsAsync()
    {
        var owner = _lifetime.Owner;
        return owner is { IsCurrent: true } && _state.Capabilities is { Upload: true } ? ReadUploadsAsync(owner) : Task.CompletedTask;
    }

    private async Task ReadScheduleAsync(MonitorAuthorityLease owner)
    {
        long generation;
        lock (_gate) generation = _generation;
        try
        {
            var schedule = await _api.GetBackupScheduleAsync(owner).ConfigureAwait(false);
            Update(owner, generation, () => { _state = _state with { TaskNextRunAt = schedule?.NextRunAt, TaskDailyTime = schedule?.DailyTime }; Settle(); });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { Update(owner, generation, () => { _state = _state with { TaskNextRunAt = null, TaskDailyTime = null }; Settle(); }); }
    }

    // ── Settings (guarded shared settings API) ───────────────────────

    private async Task<bool> LoadSettingsAsync(MonitorAuthorityLease owner, bool preserveDraft, bool requireSensitiveStatus = false)
    {
        long generation;
        lock (_gate) generation = _generation;
        AdminSettingsSensitiveStatus? status = null;
        try { status = await _store.GetSensitiveStatusAsync(owner).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return false; }
        catch { }
        var loaded = false;
        try
        {
            if (requireSensitiveStatus && status == null) throw new InvalidDataException("Sensitive settings status is unavailable.");
            var snapshot = await _store.GetEffectiveAsync(owner).ConfigureAwait(false);
            Update(owner, generation, () =>
            {
                _snapshot = snapshot;
                loaded = true;
                if (preserveDraft && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft: true);
                else _form = new(Present.SettingKeys, [], snapshot.Values, snapshot.ETag);
                _state = _state with
                {
                    SettingsLoaded = true, SettingsLoading = false, SettingsLoadFailed = false, Conflict = false, SettingsOutcomeUnknown = false, SettingsError = null,
                    SecretConfigured = status?.Configured.Contains(Present.S3SecretKey) == true,
                    ManagedByEnv = Array.AsReadOnly((status?.ManagedByEnv ?? []).Where(Keys.Contains).ToArray()),
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { Update(owner, generation, () => _state = _state with { SettingsLoading = false, SettingsLoadFailed = !_state.SettingsLoaded }); }
        return loaded;
    }

    /// <summary>Explicit dialog-local rebase. The caller retains its draft and must test and review it again.</summary>
    public async Task<bool> ReloadS3SettingsAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return false; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Saving || _state.SettingsLoading) return false;
                _state = _state with { SettingsLoading = true };
            }
            Changed?.Invoke();
            _store.Invalidate();
            return await LoadSettingsAsync(action, preserveDraft: true, requireSensitiveStatus: true).ConfigureAwait(false);
        }
    }

    /// <summary>Explicit settings reload (Try again, Reload after a conflict or an unknown save).</summary>
    public async Task ReloadSettingsAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Saving || _state.SettingsLoading) return;
                _state = _state with { SettingsLoading = true };
            }
            Changed?.Invoke();
            _store.Invalidate();
            await LoadSettingsAsync(action, preserveDraft).ConfigureAwait(false);
        }
    }

    public void Set(string key, string value)
    {
        if (!Present.SettingKeys.Contains(key)) throw new ArgumentException("This setting is not owned by the Backups page.", nameof(key));
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_form != null && !_state.Saving && !_state.ManagedByEnv.Contains(key)) _form.Set(key, value); });
    }

    public void Reset(string key)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_form != null && !_state.Saving) _form.Reset(key); });
    }

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Saving) _form?.Discard(); });
    }

    /// <summary>Local checks the server would also make: keep 1–60, a 24-hour time.</summary>
    public static IReadOnlyDictionary<string, string> Validate(IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirty)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dirty.Contains(Present.KeepKey) && (!int.TryParse(values.GetValueOrDefault(Present.KeepKey), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var keep) || keep < 1 || keep > 60))
            errors[Present.KeepKey] = "keep";
        if (dirty.Contains(Present.TimeKey) && !IsTime(values.GetValueOrDefault(Present.TimeKey) ?? "")) errors[Present.TimeKey] = "time";
        return errors;
    }

    public static bool IsTime(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 2 && parts[0].Length is 1 or 2 && parts[1].Length == 2 && parts.All(part => part.All(char.IsAsciiDigit)) &&
               int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) < 24 && int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) < 60;
    }

    public async Task SaveAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminSettingsSaveIntent intent;
            string? sentTime;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.SettingsLoading || _state.Saving || _state.Conflict || _state.SettingsOutcomeUnknown) return;
                if (_state.Errors.Count > 0) return;
                intent = _form.CaptureSave(_state.ManagedByEnv);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                // The Start time this save carries, read now: an acknowledged intent no longer holds its values.
                sentTime = intent.Values.TryGetValue(Present.TimeKey, out var time) ? time : null;
                _state = _state with { Saving = true, SettingsError = null };
            }
            Changed?.Invoke();
            var saved = false;
            try
            {
                var result = await _settingsApi.PutAsync(action, intent.Values, intent.ETag, Present.SettingKeys, []).ConfigureAwait(false);
                Update(action, () =>
                {
                    MergeSettingsSnapshot(intent, result);
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false };
                });
                saved = _lifetime.IsCurrent(action);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true }; }); }
            catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, SettingsOutcomeUnknown = true }; }); }
            catch (Exception error)
            {
                Update(action, () =>
                {
                    _form?.EndSave(intent);
                    _state = _state with { Saving = false, SettingsError = error is ApiException { StatusCode: 409 } ? AdminText.T("web.admin.backups.backUpNow.keyNotConfirmed") : Present.ErrorMessage(error) };
                });
            }
            if (!saved) return;
            _store.Invalidate();
            if (ReadOwner(action) is { } owner)
            {
                if (sentTime != null) BeginSettle(owner, sentTime);
                _ = RefreshListAsync(owner, background: false);
                _ = ReadScheduleAsync(owner);
            }
            try { Saved?.Invoke(); } catch { /* An observer cannot change an acknowledged write. */ }
        }
    }

    private static string ClockTime(string? value) => value is { Length: 8 } && value[5] == ':' ? value[..5] : value ?? "";

    /// <summary>Starts waiting for the task's daily trigger to take a Start time that was just saved (caller holds no lock).</summary>
    private void BeginSettle(MonitorAuthorityLease owner, string time)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            _settleTime = ClockTime(time); _settleUntil = _scheduler.UtcNow + SettleWindow;
            Settle();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Settles or keeps waiting; the caller holds the gate. Settling ends when the trigger holds the saved time, or after 30 s, when a server
    /// that still has not moved it is taken to run the old schedule. While it lasts the task is read again every 2 s.
    /// </summary>
    private void Settle()
    {
        var owner = _lifetime.Owner;
        var settling = _settleTime != null && ClockTime(_state.TaskDailyTime) != _settleTime && _scheduler.UtcNow < _settleUntil;
        if (!settling) { _settleTime = null; _settleTimer?.Dispose(); _settleTimer = null; }
        else if (owner != null && _visible)
        {
            _settleTimer?.Dispose();
            _settleTimer = _scheduler.Schedule(SettleInterval, () => { if (owner.IsCurrent) _ = ReadScheduleAsync(owner); });
        }
        if (_state.ScheduleSettling != settling) _state = _state with { ScheduleSettling = settling };
    }

    // ── Commands ─────────────────────────────────────────────────────

    /// <summary>Back up now: sent once. 409 explains itself; an unknown outcome blocks the button until a refresh.</summary>
    public async Task BackUpNowAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.StartingBackup || _state.UnknownOutcome != null || _state.List == null) return;
                _state = _state with { StartingBackup = true };
            }
            Changed?.Invoke();
            AdminBackupNotice notice;
            try
            {
                var started = await _api.StartBackupAsync(action).ConfigureAwait(false);
                lock (_gate) if (_lifetime.IsCurrent(action)) _started = (started.Id!, _scheduler.UtcNow);
                notice = new(AdminText.T("web.admin.backups.toast.started"), AdminText.T("web.admin.backups.toast.startedDescription"), false);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminBackupOutcomeUnknownException)
            {
                Update(action, () => { _reconcileAfterRead = null; _state = _state with { UnknownOutcome = "backup" }; });
                notice = new(AdminText.T("web.admin.backups.toast.startFailed"), AdminText.T("configure.native.backups.unknownOutcome"), true);
            }
            catch (Exception error) { notice = new(AdminText.T("web.admin.backups.toast.startFailed"), Present.ErrorMessage(error), true); }
            finally { Update(action, () => _state = _state with { StartingBackup = false }); }
            if (!_lifetime.IsCurrent(action)) return;
            Notified?.Invoke(notice);
            if (ReadOwner(action) is { } owner) _ = RefreshListAsync(owner, background: false);
        }
    }

    /// <summary>Deletes one confirmed backup. Delete is idempotent: an unknown outcome is reconciled by the next list read.</summary>
    public async Task DeleteBackupAsync(string backupId, MonitorAuthorityLease? expectedOwner = null)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || expectedOwner != null && (!expectedOwner.IsCurrent || !ReferenceEquals(expectedOwner, _state.Owner)) ||
                    _state.DeletingBackupId != null || _state.List?.Items?.Any(item => item.Id == backupId) != true) return;
                _state = _state with { DeletingBackupId = backupId };
            }
            Changed?.Invoke();
            AdminBackupNotice notice;
            try { await _api.DeleteBackupAsync(action, backupId).ConfigureAwait(false); notice = new(AdminText.T("web.admin.backups.table.deleted"), null, false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminBackupOutcomeUnknownException) { notice = new(AdminText.T("web.admin.backups.table.deleteFailed"), AdminText.T("configure.native.backups.unknownOutcome"), true); }
            catch (Exception error) { notice = new(AdminText.T("web.admin.backups.table.deleteFailed"), Present.ErrorMessage(error), true); }
            finally { Update(action, () => _state = _state with { DeletingBackupId = null }); }
            if (!_lifetime.IsCurrent(action)) return;
            Notified?.Invoke(notice);
            if (ReadOwner(action) is { } owner) _ = RefreshListAsync(owner, background: false);
        }
    }

    /// <summary>Fresh signed links for one backup; they live only in the dialog that asked. A failure refreshes the list.</summary>
    public async Task<IReadOnlyList<AdminBackupDownloadLink>?> CreateDownloadLinksAsync(string backupId, string scope)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return null; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.PreparingDownloadId != null) return null;
                _state = _state with { PreparingDownloadId = backupId };
            }
            Changed?.Invoke();
            try
            {
                var links = await _api.CreateDownloadLinksAsync(action, backupId, scope).ConfigureAwait(false);
                return _lifetime.IsCurrent(action) ? links : null;
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return null; }
            catch (Exception error)
            {
                if (_lifetime.IsCurrent(action))
                {
                    Notified?.Invoke(new(AdminText.T(scope == "restore-set" ? "web.admin.backups.download.restoreSetFailed" : "web.admin.backups.table.downloadFailed"), Present.ErrorMessage(error), true));
                    if (ReadOwner(action) is { } owner) _ = RefreshListAsync(owner, background: false);
                }
                return null;
            }
            finally { Update(action, () => _state = _state with { PreparingDownloadId = null }); }
        }
    }

    /// <summary>
    /// Saves one linked object to <paramref name="destinationPath"/>. An expired link is replaced once by a
    /// fresh one for the same object (minting links is idempotent; the GET is safe), never by another name.
    /// </summary>
    public async Task<AdminBackupDownloadResult> DownloadAsync(AdminBackup backup, AdminBackupDownloadLink link, string scope, string destinationPath,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        using var action = _lifetime.CaptureAction();
        try
        {
            try { return await _download.SaveAsync(action, backup, link, scope, destinationPath, progress, ct).ConfigureAwait(false); }
            catch (AdminBackupLinkExpiredException)
            {
                var fresh = await _api.CreateDownloadLinksAsync(action, backup.Id!, scope, ct).ConfigureAwait(false);
                var same = fresh.FirstOrDefault(candidate => candidate.Name == link.Name) ?? throw new AdminBackupLinkExpiredException();
                return await _download.SaveAsync(action, backup, same, scope, destinationPath, progress, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && action.IsCurrent)
        {
            throw new TimeoutException("The download timed out. Nothing was saved; any existing destination was kept.");
        }
    }

    public async Task<AdminBackupS3TestResult?> TestS3Async(AdminBackupS3Fields fields)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return null; }
        using (action)
        {
            try { return await _api.TestS3Async(action, Present.S3TestBody(fields)).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { return null; }
            catch (Exception error) { return new(false, null, AdminBackupValidation.SafeServerText(Present.ErrorMessage(error), fields.SecretKey, fields.AccessKey.Trim())); }
        }
    }

    /// <summary>Confirms the shown key check. Returns null on success, else the message to show.</summary>
    public async Task<string?> AcknowledgeKeyAsync(string keyCheck)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return null; }
        using (action) return await AcknowledgeKeyAsync(action, keyCheck).ConfigureAwait(false);
    }

    private async Task<string?> AcknowledgeKeyAsync(MonitorAuthorityLease action, string keyCheck)
    {
        lock (_gate)
        {
            if (!_lifetime.IsCurrent(action) || _state.AcknowledgingKey || _state.List?.KeyCheck != keyCheck) return AdminText.T("web.admin.backups.key.changedError");
            _state = _state with { AcknowledgingKey = true };
        }
        Changed?.Invoke();
        try
        {
            await _api.AcknowledgeKeyAsync(action, keyCheck).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { return AdminText.T("web.admin.backups.error.noAnswer"); }
        catch (ApiException error) when (error.StatusCode == 409) { return AdminText.T("web.admin.backups.key.changedError"); }
        catch (Exception error) { return error is AdminBackupOutcomeUnknownException ? AdminText.T("configure.native.backups.unknownOutcome") : Present.ErrorMessage(error); }
        finally
        {
            Update(action, () => _state = _state with { AcknowledgingKey = false });
            if (ReadOwner(action) is { } owner) _ = RefreshListAsync(owner, background: false);
        }
    }

    /// <summary>
    /// Official S3 setup save: confirm the key first, then one guarded settings write of the bucket and the
    /// destination. Returns null on success; on a 412 the dialog retains its draft for an explicit reload/review.
    /// The typed secret is sent at most once per explicit save and never kept by this controller.
    /// </summary>
    public async Task<string?> SaveS3Async(AdminBackupS3Fields fields, string keyCheck)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return AdminText.T("web.admin.backups.error.noAnswer"); }
        using (action)
        {
            var keyProblem = await AcknowledgeKeyAsync(action, keyCheck).ConfigureAwait(false);
            if (keyProblem != null) return keyProblem;
            AdminSettingsForm s3Form;
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _snapshot == null || _state.Saving || _state.SettingsLoading) return AdminText.T("configure.native.backups.settingsBusy");
                if (_state.Conflict || _state.SettingsOutcomeUnknown) return AdminText.T("configure.native.backups.settingsChanged");
                s3Form = new([Present.DestinationKey, .. Present.S3Keys], [Present.S3SecretKey], _snapshot.Values, _snapshot.ETag);
                // The official dialog writes the whole bucket batch, unchanged values included.
                foreach (var (key, value) in Present.S3SettingsValues(fields)) s3Form.Set(key, value, stage: true);
                intent = s3Form.CaptureSave();
                _state = _state with { Saving = true };
            }
            Changed?.Invoke();
            try
            {
                var result = await _settingsApi.PutAsync(action, intent.Values, intent.ETag, [Present.DestinationKey, .. Present.S3Keys], [Present.S3SecretKey]).ConfigureAwait(false);
                Update(action, () =>
                {
                    MergeSettingsSnapshot(intent, result);
                    _form?.Reload(_snapshot!.Values, _snapshot.ETag, preserveDraft: true);
                    _form?.Reset(Present.DestinationKey);
                });
                // Keep settings operations blocked until the read has rebased the draft and reset the destination.
                _store.Invalidate();
                await LoadSettingsAsync(action, preserveDraft: true).ConfigureAwait(false);
                Update(action, () => _form?.Reset(Present.DestinationKey));
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return null; }
            catch (AdminSettingsConflictException) { s3Form.EndSave(intent); Update(action, () => _state = _state with { Conflict = true }); return AdminText.T("configure.native.backups.settingsChanged"); }
            catch (AdminSettingsOutcomeUnknownException) { s3Form.EndSave(intent); Update(action, () => _state = _state with { SettingsOutcomeUnknown = true }); return AdminText.T("configure.native.backups.unknownOutcome"); }
            catch (Exception error)
            {
                s3Form.EndSave(intent);
                return error is ApiException { StatusCode: 409 } ? AdminText.T("web.admin.backups.backUpNow.keyNotConfirmed") : Present.ErrorMessage(error);
            }
            finally
            {
                s3Form.Discard();
                Update(action, () => _state = _state with { Saving = false });
            }
            if (ReadOwner(action) is { } owner) _ = RefreshListAsync(owner, background: false);
            if (_lifetime.IsCurrent(action))
                Notified?.Invoke(new(AdminText.T("web.admin.backups.s3.saved"), AdminText.T("web.admin.backups.s3.savedDescription"), false));
            return null;
        }
    }

    public async Task DeleteUploadAsync(string uploadId, MonitorAuthorityLease? expectedOwner = null)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || expectedOwner != null && (!expectedOwner.IsCurrent || !ReferenceEquals(expectedOwner, _state.Owner)) ||
                    _state.DeletingUploadId != null) return;
                _state = _state with { DeletingUploadId = uploadId };
            }
            Changed?.Invoke();
            AdminBackupNotice notice;
            try { await _api.DeleteUploadAsync(action, uploadId).ConfigureAwait(false); notice = new(AdminText.T("web.admin.backups.uploads.deleted"), null, false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (Exception error) { notice = new(AdminText.T("web.admin.backups.uploads.deleteFailed"), error is AdminBackupOutcomeUnknownException ? AdminText.T("configure.native.backups.unknownOutcome") : Present.ErrorMessage(error), true); }
            finally { Update(action, () => _state = _state with { DeletingUploadId = null }); }
            if (!_lifetime.IsCurrent(action)) return;
            Notified?.Invoke(notice);
            if (ReadOwner(action) is { } owner) await ReadUploadsAsync(owner).ConfigureAwait(false);
        }
    }

    /// <summary>Checks a fully received upload again (completion is idempotent). Sent only on this explicit request.</summary>
    public async Task CheckUploadAgainAsync(string uploadId)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.CheckingUploadId != null) return;
                _state = _state with { CheckingUploadId = uploadId };
            }
            Changed?.Invoke();
            try { await _api.CompleteUploadAsync(action, uploadId).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (Exception error)
            {
                if (_lifetime.IsCurrent(action)) Notified?.Invoke(new(AdminText.T("web.admin.backups.uploads.checkFailed"), Present.ErrorMessage(error), true));
            }
            finally { Update(action, () => _state = _state with { CheckingUploadId = null }); }
            if (ReadOwner(action) is { } owner) await ReadUploadsAsync(owner).ConfigureAwait(false);
        }
    }

    /// <summary>A lease for the upload dialog's session; it dies with this page's owner.</summary>
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();

    private void MergeSettingsSnapshot(AdminSettingsSaveIntent intent, AdminSettingsSaveResult result)
    {
        var baseline = new Dictionary<string, string>(_snapshot!.Values, StringComparer.Ordinal);
        foreach (var key in intent.Values.Keys.Where(key => key != Present.S3SecretKey))
            baseline[key] = result.Values.GetValueOrDefault(key, intent.Values[key]);
        _snapshot = new(baseline, result.ETag);
    }

    // ── State plumbing ───────────────────────────────────────────────

    /// <summary>Follow-up reads and their timers belong to the page, not the disposable command lease.</summary>
    private MonitorAuthorityLease? ReadOwner(MonitorAuthorityLease action)
    {
        lock (_gate) return _lifetime.IsCurrent(action) ? _state.Owner : null;
    }

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        long generation;
        lock (_gate) generation = _generation;
        Update(owner, generation, update);
    }

    private void Update(MonitorAuthorityLease owner, long generation, Action update)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || !_lifetime.IsCurrent(owner)) return;
            update();
            if (_form != null)
            {
                var values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Present.SettingKeys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal));
                var persisted = Keys.Where(key => key != Present.S3SecretKey).ToDictionary(key => key, key => Present.SettingKeys.Contains(key)
                    ? _form.GetPersisted(key) : _snapshot?.Values.GetValueOrDefault(key, "") ?? "", StringComparer.Ordinal);
                var dirty = Array.AsReadOnly(_form.DirtyKeys.ToArray());
                _state = _state with
                {
                    Values = values, PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(persisted),
                    DirtyKeys = dirty, Errors = Validate(values, dirty),
                };
            }
        }
        Changed?.Invoke();
    }
}
