using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed class SyncEntry
    {
        public SubtitleSyncState State = new();
        public long Revision;
        public bool Busy, Forbidden, Unsupported, PollStopped, Reading, ReadAgain, WatchOnRead;
        public string? Error, WatchedJobId, AnnouncedJobId;
        public DateTimeOffset Started, LastReload;
        public SubtitleTiming? LastReloadTiming;
        public SubtitleTiming? LastAppliedTiming;
        public DateTimeOffset? LastPush;
    }
    private readonly object _subtitleSyncGate = new();
    private readonly Dictionary<string, SyncEntry> _subtitleSyncEntries = new(StringComparer.Ordinal);
    private CancellationTokenSource? _subtitleSyncCts;
    private ApiRequestContext? _subtitleSyncAuthority;
    private string? _subtitleSyncSession;
    private int _subtitleSyncFile;
    private SubtitleSyncCapability? _subtitleSyncCapability;
    private bool _subtitleSyncInventoryError;
    private string? _subtitleSyncApplyingKey, _subtitleSyncApplyingJob;
    private readonly List<string> _subtitleSyncFiles = [];
    private static readonly HttpClient SubtitleSyncDownloadClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    private bool SyncCurrent(CancellationToken ct) => !ct.IsCancellationRequested && !_closing &&
        _subtitleSyncAuthority is { } authority && _apiClient.IsCurrentContext(authority) &&
        _playbackManager?.SessionId == _subtitleSyncSession && ActiveMediaFileId == _subtitleSyncFile;

    private void StopSubtitleSync()
    {
        lock (_subtitleSyncGate)
        {
            _subtitleSyncCts?.Cancel(); _subtitleSyncCts?.Dispose(); _subtitleSyncCts = null;
            _subtitleSyncSession = null; _subtitleSyncAuthority = null; _subtitleSyncCapability = null;
            _subtitleSyncInventoryError = false;
            _subtitleSyncApplyingKey = _subtitleSyncApplyingJob = null;
            _subtitleSyncEntries.Clear();
            foreach (var file in _subtitleSyncFiles) { try { File.Delete(file); } catch { } }
            _subtitleSyncFiles.Clear();
            _mpv?.SendScriptMessage("osc-subtitle-sync-notice", "{}");
            _mpv?.SendScriptMessage("osc-set-subtitle-sync", "[]");
        }
    }

    private void EnsureSubtitleSync()
    {
        lock (_subtitleSyncGate)
        {
            if (_playbackManager?.SessionId is not { } session || ActiveMediaFileId is not > 0) return;
            if (_subtitleSyncSession == session && _subtitleSyncFile == ActiveMediaFileId && _subtitleSyncCts != null)
            {
                foreach (var key in (_playbackManager.CurrentSession?.SubtitleUrls ?? []).Select(SubtitleSyncPolicy.Key)
                    .Where(key => key != null && !_subtitleSyncEntries.ContainsKey(key)).Distinct().ToArray())
                    _ = ReadSubtitleSyncAsync(key!, _subtitleSyncCts.Token);
                return;
            }
            StopSubtitleSync();
            _subtitleSyncSession = session; _subtitleSyncFile = ActiveMediaFileId.Value;
            _subtitleSyncAuthority = _apiClient.CaptureContext();
            _subtitleSyncCts = new();
            _ = RunSubtitleSyncAsync(_subtitleSyncCts.Token);
        }
    }

    private async Task RunSubtitleSyncAsync(CancellationToken ct)
    {
        try
        {
            await ReloadSubtitleSyncInventoryAsync(ct);
            while (SyncCurrent(ct))
            {
                await Task.Delay(SubtitleSyncPolicy.PollInterval, ct);
                string[] keys;
                lock (_subtitleSyncGate)
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var entry in _subtitleSyncEntries.Values)
                        if (!entry.PollStopped && SubtitleSyncPolicy.InProgress(entry.State.Sync) && now - entry.Started >= SubtitleSyncPolicy.PollLifetime)
                        {
                            entry.PollStopped = true; entry.Error = "Sync is taking longer than expected. Reload its status to check again.";
                            if (entry.WatchedJobId == entry.State.Sync?.Id) SendSubtitleSyncNotice("Sync is taking longer than expected", "Reload its status to check again.", "warning");
                        }
                    keys = _subtitleSyncEntries.Where(pair => !pair.Value.PollStopped &&
                        SubtitleSyncPolicy.ShouldPoll(pair.Value.State.Sync, pair.Value.Started, pair.Value.LastPush, now))
                        .Select(pair => pair.Key).ToArray();
                    SendSubtitleSyncState();
                }
                foreach (var key in keys) await ReadSubtitleSyncAsync(key, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* Capability/list failure is exposed as Reload in the menu. */ }
    }

    private async Task ReloadSubtitleSyncInventoryAsync(CancellationToken ct)
    {
        if (!SyncCurrent(ct) || _subtitleSyncAuthority is not { } authority) return;
        var api = new SubtitleSyncApi(_apiClient);
        Dictionary<string, long> revisions;
        lock (_subtitleSyncGate) revisions = _subtitleSyncEntries.ToDictionary(pair => pair.Key, pair => pair.Value.Revision);
        try
        {
            var capabilityTask = ReadCapabilityAsync();
            var inventoryTask = api.GetInventoryAsync(authority, _subtitleSyncFile, ct);
            await Task.WhenAll(capabilityTask, inventoryTask);
            var capability = await capabilityTask;
            var inventory = await inventoryTask;
            lock (_subtitleSyncGate)
            {
                if (!SyncCurrent(ct)) return;
                _subtitleSyncCapability = capability;
                _subtitleSyncInventoryError = false;
                foreach (var state in inventory.Subtitles)
                {
                    if (state.MediaFileId != _subtitleSyncFile || !SubtitleSyncPolicy.IsKey(state.Key)) continue;
                    if (_subtitleSyncEntries.TryGetValue(state.Key, out var existing) &&
                        (!revisions.TryGetValue(state.Key, out var revision) || existing.Revision != revision)) continue;
                    ApplySubtitleSyncState(state, ct);
                }
                foreach (var pair in _subtitleSyncEntries.ToArray())
                    if (!inventory.Subtitles.Any(state => state.Key == pair.Key) && revisions.TryGetValue(pair.Key, out var revision) && revision == pair.Value.Revision)
                    {
                        pair.Value.Revision++; pair.Value.ReadAgain = false;
                        _subtitleSyncEntries.Remove(pair.Key);
                        if (_subtitleSyncApplyingKey == pair.Key)
                        { _subtitleSyncApplyingKey = _subtitleSyncApplyingJob = null; _mpv?.SendScriptMessage("osc-subtitle-sync-notice", "{}"); }
                    }
                SendSubtitleSyncState();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (SyncCurrent(ct)) { _subtitleSyncInventoryError = true; SendSubtitleSyncState(); }
        }
        async Task<SubtitleSyncCapability?> ReadCapabilityAsync()
        {
            try { return await api.GetCapabilityAsync(authority, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }
    }

    private async Task ReadSubtitleSyncAsync(string key, CancellationToken ct, bool watch = false)
    {
        if (!SyncCurrent(ct) || _subtitleSyncAuthority is not { } authority) return;
        SyncEntry entry;
        long sentRevision = 0;
        lock (_subtitleSyncGate)
        {
            if (!_subtitleSyncEntries.TryGetValue(key, out entry!))
                _subtitleSyncEntries[key] = entry = new() { State = new() { Key = key, MediaFileId = _subtitleSyncFile } };
            entry.WatchOnRead |= watch;
            if (entry.Reading) { entry.ReadAgain = true; return; }
            entry.Reading = true;
        }
        try
        {
            do
            {
                long revision;
                lock (_subtitleSyncGate) { entry.ReadAgain = false; sentRevision = revision = entry.Revision; }
                var result = await new SubtitleSyncApi(_apiClient).GetAsync(authority, _subtitleSyncFile, key, ct);
                lock (_subtitleSyncGate)
                {
                    if (!SyncCurrent(ct)) return;
                    if (!_subtitleSyncEntries.TryGetValue(key, out var observedEntry) || !ReferenceEquals(observedEntry, entry)) return;
                    if (!SubtitleSyncPolicy.CanApplyObservation(revision, entry.Revision)) { entry.ReadAgain = true; continue; }
                    if (entry.WatchOnRead && result.Body.Subtitle.Sync != null) entry.WatchedJobId = result.Body.Subtitle.Sync.Id;
                    entry.WatchOnRead = false;
                    ApplySubtitleSyncState(result.Body.Subtitle, ct);
                }
            } while (entry.ReadAgain && SyncCurrent(ct));
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            lock (_subtitleSyncGate) if (SyncCurrent(ct))
            {
                if (!SubtitleSyncPolicy.CanApplyObservation(sentRevision, entry.Revision)) entry.ReadAgain = true;
                else if (_subtitleSyncEntries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    _subtitleSyncEntries.Remove(key);
                    if (entry.WatchedJobId != null && entry.WatchedJobId == entry.State.Sync?.Id)
                    { _subtitleSyncApplyingKey = _subtitleSyncApplyingJob = null; _mpv?.SendScriptMessage("osc-subtitle-sync-notice", "{}"); }
                }
            }
        }
        catch
        { lock (_subtitleSyncGate) if (SyncCurrent(ct))
            {
                entry.PollStopped = true; entry.Error = "Couldn't check sync status. Reload to try again.";
                if (entry.WatchedJobId == entry.State.Sync?.Id) SendSubtitleSyncNotice("Couldn't check sync status", "Reload to try again.", "warning");
            }
        }
        finally
        {
            lock (_subtitleSyncGate)
            {
                entry.Reading = false;
                if (SyncCurrent(ct)) { SendSubtitleSyncState(); if (entry.ReadAgain) _ = ReadSubtitleSyncAsync(key, ct); }
            }
        }
    }

    private void ApplySubtitleSyncState(SubtitleSyncState state, CancellationToken ct)
    {
        if (!SyncCurrent(ct) || state.MediaFileId != _subtitleSyncFile || !SubtitleSyncPolicy.IsKey(state.Key)) return;
        var existed = _subtitleSyncEntries.TryGetValue(state.Key, out var entry);
        entry ??= new();
        if (state.Sync != null && !SubtitleSyncPolicy.AcceptJob(entry.State.Sync, state.Sync)) return;
        var changed = existed && entry.State.Timing != state.Timing;
        if (!existed || entry.State.Sync?.Id != state.Sync?.Id) { entry.Started = DateTimeOffset.UtcNow; entry.PollStopped = false; }
        entry.State = state; entry.Revision++;
        _subtitleSyncEntries[state.Key] = entry;
        var job = state.Sync;
        var watched = job != null && job.Id == entry.WatchedJobId;
        if (watched && SubtitleSyncPolicy.InProgress(job))
            SendSubtitleSyncNotice("Syncing " + (state.Language.Length > 0 ? state.Language : "selected") + " subtitles",
                SubtitleSyncPolicy.Phase(job!), "progress", (int)Math.Round(Math.Clamp(job!.Progress ?? 0, 0, 1) * 100));
        else if (watched && entry.AnnouncedJobId != job!.Id)
        {
            entry.AnnouncedJobId = job.Id;
            if (job.Status == "synced" && IsActiveSyncKey(state.Key))
            {
                _subtitleSyncApplyingKey = state.Key; _subtitleSyncApplyingJob = job.Id;
                SendSubtitleSyncNotice("Applying new timing…", "", "progress", 100, applying: true);
                _ = FinishSubtitleApplyingAfterBoundAsync(state.Key, job.Id, ct);
                _ = ReloadCorrectedSubtitleAsync(entry, ct, own: true);
            }
            else SendSubtitleSyncOutcome(state);
        }
        if (changed && IsActiveSyncKey(state.Key) && !(watched && job?.Status == "synced"))
            _ = ReloadCorrectedSubtitleAsync(entry, ct, own: false);
        SendSubtitleSyncState();
    }

    private bool IsActiveSyncKey(string key) => _playbackManager?.CurrentSession?.SubtitleUrls?
        .Any(track => track.Index == _activeSubtitleServerIndex && SubtitleSyncPolicy.Key(track) == key) == true;

    private async Task SubtitleSyncActionAsync(string key, string action)
    {
        EnsureSubtitleSync();
        if (_subtitleSyncCts is not { } owner || _subtitleSyncAuthority is not { } authority) return;
        var ct = owner.Token;
        if (action is "reload" or "refresh")
        {
            lock (_subtitleSyncGate)
                foreach (var entry in _subtitleSyncEntries.Values) { entry.Error = null; entry.PollStopped = false; entry.Started = DateTimeOffset.UtcNow; entry.LastReloadTiming = null; }
            await ReloadSubtitleSyncInventoryAsync(ct);
            SyncEntry? active;
            lock (_subtitleSyncGate) active = _subtitleSyncEntries.Values.FirstOrDefault(entry => IsActiveSyncKey(entry.State.Key));
            if (action == "reload" && active != null) await ReloadCorrectedSubtitleAsync(active, ct, own: false);
            return;
        }
        SyncEntry target;
        long sentRevision;
        lock (_subtitleSyncGate)
        {
            if (!SyncCurrent(ct) || !_subtitleSyncEntries.TryGetValue(key, out target!) || target.Busy || target.Forbidden ||
                SubtitleSyncPolicy.InProgress(target.State.Sync) || !IsActiveSyncKey(key)) return;
            if (action == "sync" && (target.Unsupported || _subtitleSyncCapability?.State != "available" ||
                (target.State.Source == "external" && !_subtitleSyncCapability.External))) return;
            if (action == "reset" && target.State.Timing.IsIdentity) return;
            target.Busy = true; target.Error = null; target.Revision++; SendSubtitleSyncState();
            sentRevision = target.Revision;
        }
        try
        {
            var api = new SubtitleSyncApi(_apiClient);
            SubtitleSyncState state;
            if (action == "reset")
            {
                var current = await api.GetAsync(authority, _subtitleSyncFile, key, ct);
                if (!SyncCurrent(ct)) return;
                lock (_subtitleSyncGate) sentRevision = target.Revision;
                state = (await api.ResetAsync(authority, _subtitleSyncFile, key, current.ETag ?? "", ct)).Body.Subtitle;
            }
            else state = (await api.StartAsync(authority, _subtitleSyncFile, key, ct)).Subtitle;
            lock (_subtitleSyncGate)
            {
                if (!SyncCurrent(ct)) return;
                if (!_subtitleSyncEntries.TryGetValue(key, out var observedEntry) || !ReferenceEquals(observedEntry, target)) return;
                if (action == "sync")
                {
                    target.WatchedJobId = state.Sync?.Id;
                    // A terminal push can beat the 202 response. Observe that
                    // already-newer state now that this viewer owns the notice.
                    if (!SubtitleSyncPolicy.CanApplyObservation(sentRevision, target.Revision) ||
                        (state.Sync != null && target.State.Sync?.Id == state.Sync.Id && !SubtitleSyncPolicy.AcceptJob(target.State.Sync, state.Sync))) state = target.State;
                }
                else if (!SubtitleSyncPolicy.CanApplyObservation(sentRevision, target.Revision)) return;
                ApplySubtitleSyncState(state, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex)
        {
            lock (_subtitleSyncGate)
            {
                if (!SyncCurrent(ct)) return;
                if (ex.StatusCode == 403) target.Forbidden = true;
                else if (ex.StatusCode == 422) { target.Unsupported = true; target.Error = "This format can't be synced."; }
                else target.Error = ex.StatusCode == 412 ? "The subtitle changed. Try again." : "Couldn't update subtitle timing. Try again.";
            }
        }
        catch { lock (_subtitleSyncGate) if (SyncCurrent(ct)) target.Error = "Couldn't update subtitle timing. Try again."; }
        finally { lock (_subtitleSyncGate) { target.Busy = false; if (SyncCurrent(ct)) SendSubtitleSyncState(); } }
    }

    private void ApplySubtitleSyncEvent(JsonElement payload, bool timingChanged)
    {
        if (_subtitleSyncCts is not { } owner) return;
        var ct = owner.Token;
        var key = TryGetPayloadString(payload, "sync_key");
        if (!SyncCurrent(ct) || key == null || !SubtitleSyncPolicy.IsKey(key) ||
            TryGetPayloadString(payload, "session_id") != _subtitleSyncSession ||
            !TryGetPayloadInt(payload, "file_id", out var file) || file != _subtitleSyncFile) return;
        lock (_subtitleSyncGate)
        {
            if (!_subtitleSyncEntries.TryGetValue(key, out var entry))
            { _ = ReadSubtitleSyncAsync(key, ct); return; }
            entry.LastPush = DateTimeOffset.UtcNow;
            if (timingChanged) { entry.Revision++; _ = ReadSubtitleSyncAsync(key, ct); return; }
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
            try
            {
                var next = new SubtitleSyncState { Key = key, MediaFileId = file, Source = entry.State.Source,
                    Language = entry.State.Language, Format = entry.State.Format, Label = entry.State.Label,
                    StoredSubtitleId = entry.State.StoredSubtitleId,
                    Timing = payload.GetProperty("timing").Deserialize<SubtitleTiming>(options) ?? entry.State.Timing,
                    Sync = payload.TryGetProperty("job", out var job) ? job.Deserialize<SubtitleSyncJob>(options) : entry.State.Sync };
                ApplySubtitleSyncState(next, ct);
            }
            catch (JsonException) { _ = ReadSubtitleSyncAsync(key, ct); }
            catch (KeyNotFoundException) { _ = ReadSubtitleSyncAsync(key, ct); }
        }
    }

    private async Task ReloadCorrectedSubtitleAsync(SyncEntry entry, CancellationToken ct, bool own)
    {
        var state = entry.State;
        var timing = state.Timing;
        if (!SyncCurrent(ct) || _subtitleSyncAuthority is not { } authority) return;
        // Result + timing-change events often arrive together. Re-reading a job
        // must not repeatedly attach the same corrected cues.
        if (entry.LastReloadTiming == timing && DateTimeOffset.UtcNow - entry.LastReload < TimeSpan.FromSeconds(5))
        {
            if (own && IsApplyingOwnedTiming(entry, entry.LastAppliedTiming))
                SendSubtitleSyncOutcome(entry.State);
            return;
        }
        entry.LastReload = DateTimeOffset.UtcNow;
        entry.LastReloadTiming = timing;
        string? path = null;
        try
        {
            var pair = _playbackManager?.GetSubtitleUrls().FirstOrDefault(candidate => SubtitleSyncPolicy.Key(candidate.Track) == state.Key);
            if (pair == null || string.IsNullOrWhiteSpace(pair.Value.FullUrl)) return;
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = _apiClient.CreateAuthenticatedRequest(HttpMethod.Get, pair.Value.FullUrl);
            if (!SyncCurrent(ct) || !_apiClient.IsCurrentContext(authority)) return;
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var response = await SubtitleSyncDownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(bounded.Token);
            if (bytes.Length == 0) throw new InvalidDataException("Empty subtitle cues.");
            var extension = state.Format.ToLowerInvariant() switch { "ass" => ".ass", "ssa" => ".ssa", "vtt" => ".vtt", _ => ".srt" };
            path = Path.Combine(Path.GetTempPath(), "silo-subtitle-sync-" + Guid.NewGuid().ToString("N") + extension);
            await File.WriteAllBytesAsync(path, bytes, bounded.Token);
            lock (_subtitleSyncGate)
            lock (_subtitleTrackStateLock)
            {
                if (!SyncCurrent(ct) || entry.State.Timing != timing || !IsActiveSyncKey(state.Key) || _mpv == null) return;
                var index = pair.Value.Track.Index;
                _loadedExternalSubtitleSids.TryGetValue(index, out var oldSid);
                _mpv.AddSubtitle(path, pair.Value.Track.Label, pair.Value.Track.Language, select: true);
                var newSid = (int)Math.Round(_mpv.GetPropertyDouble("sid"));
                if (newSid <= 0 || newSid == oldSid) throw new InvalidDataException("Corrected subtitle did not load.");
                _loadedExternalSubtitleSids[index] = newSid;
                if (oldSid > 0) _mpv.RemoveSubtitle(oldSid);
                _subtitleSyncFiles.Add(path); path = null;
                entry.LastAppliedTiming = timing;
                if (IsApplyingOwnedTiming(entry, timing)) SendSubtitleSyncOutcome(entry.State);
                else if (!own && !(entry.State.Sync is { Trigger: "auto", Status: "synced" } job && job.Result == timing))
                    SendSubtitleSyncNotice("Subtitle timing updated", "", "info");
            }
        }
        catch (Exception) when (SyncCurrent(ct))
        {
            lock (_subtitleSyncGate)
            {
                entry.Error = "Timing changed, but the new cues couldn't load. Reload to try again.";
                SendSubtitleSyncState();
                if (_subtitleSyncApplyingKey == state.Key && _subtitleSyncApplyingJob == state.Sync?.Id) SendSubtitleSyncNotice("Couldn't load the synced subtitles", "Your current subtitles are still showing.", "warning");
            }
        }
        catch { /* Playback ownership changed while a cue download was finishing. */ }
        finally { if (path != null) try { File.Delete(path); } catch { } }
    }

    private void SendSubtitleSyncState()
    {
        lock (_subtitleSyncGate)
        {
            var rows = (_playbackManager?.CurrentSession?.SubtitleUrls ?? []).Select(track =>
            {
                var key = SubtitleSyncPolicy.Key(track);
                _subtitleSyncEntries.TryGetValue(key ?? "", out var entry);
                var running = SubtitleSyncPolicy.InProgress(entry?.State.Sync);
                return new Dictionary<string, object?> { ["index"] = track.Index, ["key"] = key,
                    ["status"] = entry?.Forbidden == true ? "This server doesn't allow changing subtitle timing." : entry?.Error ??
                        (entry == null ? "" : entry.State.Sync?.Status == "failed" ? SubtitleSyncPolicy.Failure(entry.State.Sync.Failure) :
                            entry.State.Sync?.Status == "no_match" ? "Doesn't match this video's audio; probably for another release." :
                            entry.State.Sync == null && entry.State.Timing.IsIdentity && _subtitleSyncCapability?.State == "available"
                                ? "Matches the timing to the audio for everyone watching." : SubtitleSyncPolicy.Status(entry.State)),
                    ["sync"] = entry != null && !entry.Forbidden && !entry.Unsupported && _subtitleSyncCapability?.State == "available" &&
                        (entry.State.Source != "external" || _subtitleSyncCapability.External),
                    ["reset"] = entry != null && !entry.Forbidden && !entry.State.Timing.IsIdentity,
                    ["busy"] = entry?.Busy == true || running, ["reload"] = key != null && (_subtitleSyncInventoryError || entry?.Error != null) };
            }).ToArray();
            _mpv?.SendScriptMessage("osc-set-subtitle-sync", JsonSerializer.Serialize(rows));
        }
    }

    private void SendSubtitleSyncOutcome(SubtitleSyncState state)
    {
        var name = state.Language.Length > 0 ? state.Language + " subtitles" : "Subtitles";
        switch (state.Sync?.Status)
        {
            case "synced": SendSubtitleSyncNotice(IsActiveSyncKey(state.Key) ? "Subtitles synced" : name + " synced", SubtitleSyncPolicy.Describe(state.Timing), "success"); break;
            case "already_synced": SendSubtitleSyncNotice(name + " already match the audio", "", "info"); break;
            case "no_match": SendSubtitleSyncNotice(name + " don't match the audio", "They're probably for another release. The timing wasn't changed.", "warning"); break;
            case "failed": SendSubtitleSyncNotice("Couldn't sync " + name, SubtitleSyncPolicy.Failure(state.Sync.Failure), "warning"); break;
        }
    }
    private bool IsApplyingOwnedTiming(SyncEntry entry, SubtitleTiming? loadedTiming)
        => _subtitleSyncApplyingKey == entry.State.Key && _subtitleSyncApplyingJob == entry.State.Sync?.Id &&
            SubtitleSyncPolicy.ShouldAnnounceAppliedTiming(entry.State, entry.WatchedJobId, entry.AnnouncedJobId, loadedTiming);

    private async Task FinishSubtitleApplyingAfterBoundAsync(string key, string jobId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(SubtitleSyncPolicy.ApplyingLifetime, ct);
            lock (_subtitleSyncGate)
            {
                if (!SyncCurrent(ct) || _subtitleSyncApplyingKey != key || _subtitleSyncApplyingJob != jobId ||
                    !_subtitleSyncEntries.TryGetValue(key, out var entry) || entry.WatchedJobId != jobId || entry.State.Sync?.Id != jobId) return;
                if (entry.Error != null) SendSubtitleSyncNotice("Couldn't load the synced subtitles", "Your current subtitles are still showing.", "warning");
                else SendSubtitleSyncOutcome(entry.State);
            }
        }
        catch (OperationCanceledException) { }
    }
    private void SendSubtitleSyncNotice(string title, string detail, string tone, int? percent = null, bool applying = false)
    {
        if (!applying) _subtitleSyncApplyingKey = _subtitleSyncApplyingJob = null;
        _mpv?.SendScriptMessage("osc-subtitle-sync-notice", JsonSerializer.Serialize(new Dictionary<string, object?>
        { ["title"] = title, ["detail"] = detail, ["tone"] = tone, ["percent"] = percent }));
    }
}
