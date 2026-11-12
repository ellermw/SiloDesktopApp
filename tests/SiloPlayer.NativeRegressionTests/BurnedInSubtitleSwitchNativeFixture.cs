using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

/// <summary>
/// A DVD/DVB track is composited into the video by the server (v3 plan
/// subtitle mode "burn_in"). Turning subtitles off, or choosing a text track,
/// must replan without it through the actual PlayerService caller, and a text
/// pick must survive a later quality reload.
/// </summary>
internal static class BurnedInSubtitleSwitchNativeFixture
{
    private const int English = 0, Spanish = 1, Dvd = 2;

    internal static async Task RunAsync(StackPanel parent)
    {
        using var media = new MediaServer(Path.Combine(Program.ResultDirectory, "burned-in-subtitle.wav"));
        using var wire = new Wire(media.Origin); using var http = new HttpClient(wire);
        var ownedAi = Environment.GetEnvironmentVariable("SILO_NATIVE_BURN_FAILURE") is "ai-unburn-owned" or "ai-unburn-retired" or "ai-unburn-session";
        wire.AiDownload = ownedAi;
        var api = new SiloApiClient(http); api.SetBaseUrl(ownedAi ? media.Origin : "https://burn-in.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "burned-in-subtitle")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42, subtitleTrackIndex: Dvd);
        Require(manager.CurrentSession!.IsSubtitleBurnedIn, "Fixture did not start with a burned-in DVD subtitle.");
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        Call(service, "WireMpvEvents");
        try
        {
            var lifetimeCase = Environment.GetEnvironmentVariable("SILO_NATIVE_BURN_FAILURE");
            if (ownedAi)
            {
                await Loaded(async () =>
                {
                    var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(), manager.StreamUrl!, 0d, CancellationToken.None)!;
                    await prepare;
                    Call(service, "ApplyPreparedTransport", prepare.GetType().GetProperty("Result")!.GetValue(prepare)!);
                    Set(service, "_pendingInitialServerSubtitleIndex", Dvd);
                    Call(service, "BeginMpvLoad", prepare.GetType().GetProperty("Result")!.GetValue(prepare)!, true);
                });
                await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading && service.ActiveSubtitleServerIndex == Dvd);
                var previous = manager.CurrentSession;
                var generation = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                Task RefreshOwned()
                {
                    var factory = typeof(PlayerService).GetMethod("CaptureSubtitleOperationOwner", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (factory == null) return service.RefreshSubtitlesAfterAiAsync(42, 91);
                    var owner = factory.Invoke(service, [manager.CurrentSession!]);
                    return (Task)typeof(PlayerService).GetMethod("RefreshSubtitlesAfterAiForOwnerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(service, [42, 91L, owner])!;
                }
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (lifetimeCase == "ai-unburn-retired") wire.BeforeTrackChange = ct => { entered.TrySetResult(); return release.Task.WaitAsync(ct); };
                try
                {
                    if (lifetimeCase == "ai-unburn-session")
                    {
                        var gate = (SemaphoreSlim)Get(manager, "_replanGuard")!;
                        await gate.WaitAsync();
                        Task? refresh = null;
                        var foreign = new PlaybackStartResponse { ProtocolVersion = 3, MediaFileId = 42, SessionId = "foreign",
                            SubtitleMode = "burn_in", SelectedSubtitleTrackId = previous!.SelectedSubtitleTrackId,
                            SelectedSubtitleTrackIndex = previous.SelectedSubtitleTrackIndex, SubtitleUrls = previous.SubtitleUrls };
                        try
                        {
                            refresh = RefreshOwned();
                            await Until(() => service.IsLoading && Get(service, "_pendingSubtitlePreferenceOwner") != null);
                            Require(!refresh.IsCompleted && wire.Replans.Count == replans, "Owned AI session control did not queue at the real manager gate.");
                            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, foreign);
                        }
                        finally { gate.Release(); }
                        if (refresh != null) await refresh.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(ReferenceEquals(foreign, manager.CurrentSession) && Equals(generation, Get(service, "_fileLoadGeneration")) &&
                            mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == writes && wire.Replans.Count == replans,
                            "Queued AI selection borrowed a foreign session to issue/reload/persist its unburn plan.");
                    }
                    else if (lifetimeCase == "ai-unburn-retired")
                    {
                        var refresh = RefreshOwned();
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        api.SetProfile("replacement"); release.TrySetResult();
                        await refresh.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(ReferenceEquals(previous, manager.CurrentSession) && manager.CurrentSession!.IsSubtitleBurnedIn &&
                            Equals(generation, Get(service, "_fileLoadGeneration")) && mpv.GetPropertyDouble("sid") <= 0 &&
                            wire.PreferenceWrites == writes && wire.Replans.Count == replans + 1,
                            "Retired AI unburn response adopted a foreign plan, started a load or persisted an unexecuted pick.");
                    }
                    else
                    {
                        await Loaded(RefreshOwned);
                        await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null &&
                            service.ActiveSubtitleServerIndex == 3 && Get(service, "_pendingSubtitlePreferenceOwner") == null);
                        Require(!manager.CurrentSession!.IsSubtitleBurnedIn && !ReferenceEquals(previous, manager.CurrentSession) &&
                            NativeSidMatches(service, mpv, 3) && mpv.IsPaused && service.IsPaused && wire.PreferenceWrites == writes + 1 &&
                            wire.LastSubtitlePreferenceIndex == 3 && wire.Replans.Count == replans + 1 &&
                            !(bool)Get(service, "_prematureEofRecoveryActive")!,
                            "Owned AI preferred text rejected its own accepted unburn grant or lost native execution/pause/preference.");
                    }
                    Program.Log($"PASS: {lifetimeCase} transfers only its own accepted AI unburn plan and preserves native/pause/preference ownership.");
                }
                finally { release.TrySetResult(); wire.BeforeTrackChange = null; }
                return;
            }
            if (lifetimeCase is "queued-cancel" or "queued-owner")
            {
                var gate = (SemaphoreSlim)Get(service, "_streamRecoveryGate")!;
                using var lifetime = new CancellationTokenSource();
                await gate.WaitAsync();
                try
                {
                    var queued = (Task)Call(service, "RunStreamRecoveryAsync", 0d, "subtitle-switch-failed", manager, false, lifetime.Token, null)!;
                    if (lifetimeCase == "queued-cancel")
                    {
                        lifetime.Cancel();
                        await queued.WaitAsync(TimeSpan.FromSeconds(2));
                        Require(gate.CurrentCount == 0, "A canceled waiter released another recovery's ownership.");
                    }
                    else
                    {
                        Set(service, "_playbackManager", null);
                        gate.Release();
                        await queued.WaitAsync(TimeSpan.FromSeconds(2));
                        await gate.WaitAsync();
                    }
                    Require(wire.Replans.Count == 0, "A canceled or retired queued recovery replanned playback.");
                    Program.Log($"PASS: subtitle recovery {lifetimeCase} retires without changing playback or releasing another owner's gate.");
                    return;
                }
                finally { gate.Release(); }
            }
            // Off while burned in: the caller must replan, not only clear mpv's sid.
            await Loaded(() => service.SetSubtitleTrackAndPersistAsync(0, null, null));
            Require(wire.Replans.Count == 1 && IsTrackChangeWithoutSubtitle(wire.Replans[0]),
                "Off on a burned-in subtitle did not replan without the subtitle.");
            Require(!manager.CurrentSession!.IsSubtitleBurnedIn, "The burned-in subtitle stayed in the plan after Off.");

            // Burn the DVD track in again through the actual caller.
            var dvd = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Dvd);
            await Loaded(() => service.SetSubtitleTrackAndPersistAsync(Dvd + 1, dvd.Language, dvd));
            Require(manager.CurrentSession!.IsSubtitleBurnedIn, "Choosing the DVD track did not burn it in.");
            // The reload's post-load selection reselects the burned-in track; it
            // must not replan again (each replan reloads and reselects it).
            var replansAfterBurn = wire.Replans.Count;
            await Task.Delay(1500);
            Require(wire.Replans.Count == replansAfterBurn,
                $"Burn-in reloaded in a loop: {wire.Replans.Count - replansAfterBurn} further replans after the reload.");

            // Text while burned in: the server grants text instead of burn-in,
            // then native selection follows the accepted plan after reload.
            var spanish = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Spanish);
            var failureMode = Environment.GetEnvironmentVariable("SILO_NATIVE_BURN_FAILURE");
            var failSwitch = !string.IsNullOrEmpty(failureMode);
            var startPaused = failureMode?.Contains("paused-", StringComparison.Ordinal) == true;
            var selectOff = failureMode?.EndsWith("off", StringComparison.Ordinal) == true;
            var overlapRecovery = failureMode?.StartsWith("overlap-", StringComparison.Ordinal) == true;
            if (failSwitch)
            {
                service.SetPaused(startPaused);
                await Until(() => mpv.IsPaused == startPaused && service.IsPaused == startPaused);
                wire.FailNextTrackChange = true;
            }
            var replansBeforeText = wire.Replans.Count;
            var survivingSession = manager.CurrentSession;
            var survivingIntent = manager.CaptureLocalSubtitleIntent();
            var writesBeforeText = wire.PreferenceWrites;
            var loadBeforeText = Get(service, "_fileLoadGeneration");
            SemaphoreSlim? heldRecovery = null;
            try
            {
                async Task SwitchSubtitle()
                {
                    if (overlapRecovery)
                    {
                        heldRecovery = (SemaphoreSlim)Get(service, "_streamRecoveryGate")!;
                        await heldRecovery.WaitAsync();
                    }
                    await (selectOff
                        ? service.SetSubtitleTrackAndPersistAsync(0, null, null)
                        : service.SetSubtitleTrackAndPersistAsync(Spanish + 1, spanish.Language, spanish));
                    if (heldRecovery != null) { heldRecovery.Release(); heldRecovery = null; }
                }
                if (failSwitch) await SwitchSubtitle();
                else await Loaded(SwitchSubtitle);
            }
            finally { heldRecovery?.Release(); }
            if (failSwitch)
            {
                await Until(() => !service.IsLoading && mpv.IsPaused == startPaused && service.IsPaused == startPaused);
                await Task.Delay(500);
                Require(ReferenceEquals(manager.CurrentSession, survivingSession) && manager.CurrentSession!.IsSubtitleBurnedIn &&
                    manager.CaptureLocalSubtitleIntent() == survivingIntent && manager.SubtitleSelectionForLoad == Dvd &&
                    service.ActiveSubtitleServerIndex == Dvd && mpv.GetPropertyDouble("sid") <= 0,
                    "A rejected subtitle switch did not retain the outgoing burn-in plan and executed intent.");
                Require(wire.Replans.Count == replansBeforeText + 1 && wire.PreferenceWrites == writesBeforeText &&
                    Equals(Get(service, "_fileLoadGeneration"), loadBeforeText) && Get(service, "_playbackTerminalState") == null &&
                    Get(service, "_pendingSubtitlePreferenceIndex") == null && !(bool)Get(service, "_prematureEofRecoveryActive")! &&
                    !(bool)Get(service, "_switchingContent")!,
                    "A rejected subtitle switch retried, reloaded, entered terminal recovery or saved an unexecuted preference.");
                Program.Log($"PASS: rejected burned-in subtitle switch ({failureMode}) keeps healthy native playback, previous selection and viewer pause without retrying.");
                return;
            }
            Require(wire.Replans.Count == replansBeforeText + 1 && SelectedSubtitle(wire.Replans[^1]) == Spanish,
                "Text on a burned-in subtitle did not replan to the requested text track.");
            await Until(() => service.ActiveSubtitleServerIndex == Spanish && NativeSidMatches(service, mpv, Spanish));
            Require(manager.SubtitleSelectionForLoad == Spanish,
                $"The Off replan replaced the text pick as the viewer's choice (next load: {manager.SubtitleSelectionForLoad}).");

            // A later quality reload must carry and restore the text pick.
            await Loaded(() => (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!);
            Require(SelectedSubtitle(wire.Replans[^1]) == Spanish, "The quality replan dropped the text subtitle.");
            await Until(() => service.ActiveSubtitleServerIndex == Spanish && NativeSidMatches(service, mpv, Spanish));
            Require(media.SubtitleRequests > 0, "Native player never fetched the served subtitle bytes.");
            Program.Log("PASS: actual PlayerService replans a burned-in DVD subtitle away for Off and for a text track, and the text pick survives a quality reload with a matching native sid.");

            // A text pick made while a quality reply is in flight belongs to
            // the outgoing inventory. Persist only its executed rebound row,
            // even when another track acquires that outgoing combined index.
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            var english = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == English);
            await service.SetSubtitleTrackAndPersistAsync(English + 1, english.Language, english);
            var deferredSpanish = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Spanish);
            var deferredWrites = wire.PreferenceWrites;
            var qualityEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var qualityRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.BeforeQualityChange = ct => { qualityEntered.TrySetResult(); return qualityRelease.Task.WaitAsync(ct); };
            try
            {
                await Loaded(async () =>
                {
                    var quality = (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!;
                    try
                    {
                        await qualityEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(service.IsLoading && service.IsSwitchingContent,
                            "The deferred text fixture did not hold a quality replacement.");
                        await service.SetSubtitleTrackAndPersistAsync(Spanish + 1, deferredSpanish.Language, deferredSpanish);
                        Require((int?)Get(service, "_pendingSubtitlePreferenceIndex") == Spanish &&
                            Get(service, "_pendingSubtitlePreferenceOwner") != null && wire.PreferenceWrites == deferredWrites,
                            "A deferred text pick was not retained without an early preference write.");
                        wire.ReindexSpanish = wire.ReuseSpanishIndex = true;
                    }
                    finally { qualityRelease.TrySetResult(); }
                    await quality.WaitAsync(TimeSpan.FromSeconds(6));
                });
                await Until(() => Get(service, "_loadedMediaInitCts") == null &&
                    service.ActiveSubtitleServerIndex == Spanish + 10 && NativeSidMatches(service, mpv, Spanish + 10));
                Require(wire.PreferenceWrites == deferredWrites + 1 && wire.LastSubtitlePreferenceIndex == Spanish + 10 &&
                    Get(service, "_pendingSubtitlePreferenceIndex") == null && Get(service, "_pendingSubtitlePreferenceOwner") == null,
                    "The deferred text preference was not saved exactly once for its executed rebound identity.");
                var unrelated = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Spanish);
                Require(unrelated.TrackId != deferredSpanish.TrackId &&
                    await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", unrelated.Index, false)!,
                    "The deferred text fixture did not execute a different row at the outgoing index.");
                Require(wire.PreferenceWrites == deferredWrites + 1 && wire.LastSubtitlePreferenceIndex == Spanish + 10,
                    "A stale deferred preference was saved against an unrelated row at the outgoing index.");
            }
            finally
            {
                qualityRelease.TrySetResult(); wire.BeforeQualityChange = null;
                wire.ReindexSpanish = wire.ReuseSpanishIndex = false;
            }
            await Loaded(() => (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!);
            await Until(() => Get(service, "_loadedMediaInitCts") == null && service.ActiveSubtitleServerIndex == Spanish);
            Program.Log("PASS: a deferred manual text pick survives quality-plan reindexing, saves its executed index exactly once and cannot persist a different row at its outgoing index.");

            // Cancel an actual seek replan before it submits a native load.
            // A rejected bitmap pick must resume the surviving stream; a newer
            // deferred text pick must still execute if bitmap work is superseded
            // during the drain (there will be no FILE_LOADED to consume it).
            foreach (var paused in new[] { false, true })
            foreach (var superseded in new[] { false, true })
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                var baseline = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Spanish);
                await service.SetSubtitleTrackAndPersistAsync(Spanish + 1, baseline.Language, baseline);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.BeforeSeek = async ct =>
                {
                    entered.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        canceled.TrySetResult();
                        if (superseded) await release.Task;
                        throw;
                    }
                };
                var plan = Get(service, "_activeTransportPlan");
                var retained = manager.CurrentSession;
                var intent = manager.CaptureLocalSubtitleIntent();
                var generation = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                Task<bool>? pick = null;
                try
                {
                    Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("remux", media.Origin + "/media.wav"));
                    Call(service, "QueueTransportRestartForSeek", 20d, false);
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(service.IsLoading && service.IsSwitchingContent && Get(service, "_fileLoadTimeoutCts") == null,
                        "Seek cancellation fixture did not hold a seek before native loading.");
                    wire.FailNextTrackChange = !superseded;
                    var bitmap = retained!.SubtitleUrls.Single(t => t.Index == Dvd);
                    pick = (Task<bool>)Call(service, "TrySetSubtitleTrackAndPersistAsync", Dvd + 1, bitmap.Language, bitmap)!;
                    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    if (superseded)
                    {
                        var latest = retained.SubtitleUrls.Single(t => t.Index == English);
                        await service.SetSubtitleTrackAndPersistAsync(English + 1, latest.Language, latest);
                        Require(!pick.IsCompleted && (int?)Get(service, "_pendingSubtitlePreferenceIndex") == English &&
                            wire.PreferenceWrites == writes, "Newer text pick was not deferred during the seek drain.");
                    }
                    release.TrySetResult();
                    Require(!await pick.WaitAsync(TimeSpan.FromSeconds(6)),
                        "Rejected or superseded bitmap selection reported success after canceling the seek.");
                    var expected = superseded ? English : Spanish;
                    await Until(() => !service.IsLoading && !service.IsSwitchingContent &&
                        mpv.IsPaused == paused && service.IsPaused == paused && service.ActiveSubtitleServerIndex == expected);
                    Require(ReferenceEquals(manager.CurrentSession, retained) && NativeSidMatches(service, mpv, expected) &&
                        (superseded ? manager.SubtitleSelectionForLoad == English : manager.CaptureLocalSubtitleIntent() == intent) &&
                        wire.PreferenceWrites == writes + (superseded ? 1 : 0) && wire.Replans.Count == replans + (superseded ? 1 : 2) &&
                        Equals(Get(service, "_fileLoadGeneration"), generation) && Get(service, "_fileLoadTimeoutCts") == null &&
                        Get(service, "_seekRestartCts") == null && Get(service, "_pendingSubtitlePreferenceOwner") == null &&
                        Get(service, "_restorePausedAfterLoad") == null && !(bool)Get(service, "_prematureEofRecoveryActive")! &&
                        Get(service, "_playbackTerminalState") == null,
                        "Canceled seek left orphaned loading/pause state, lost the latest pick, or persisted/reloaded a refused bitmap.");
                }
                finally
                {
                    release.TrySetResult(); wire.BeforeSeek = null; wire.FailNextTrackChange = false;
                    if (pick != null) try { await pick.WaitAsync(TimeSpan.FromSeconds(6)); } catch (Exception) { }
                    Set(service, "_activeTransportPlan", plan);
                }
            }
            var restoreSpanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Spanish);
            await service.SetSubtitleTrackAndPersistAsync(Spanish + 1, restoreSpanish.Language, restoreSpanish);
            Program.Log("PASS: rejected and superseded bitmap picks after canceling an in-flight seek retire orphaned loading, preserve viewer pause and execute newer deferred text without a native reload.");

            // Reject a bitmap pick while a quality replacement is still held
            // before FILE_LOADED. The subtitle request does not own that load's
            // pause slot, loading flags, timeout, or overlay.
            foreach (var paused in new[] { false, true })
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                media.BeforeMediaResponse = () => { entered.TrySetResult(); return release.Task; };
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                try
                {
                    await Loaded(async () =>
                    {
                        await (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!;
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        var generation = Get(service, "_fileLoadGeneration");
                        var timeoutOwner = Get(service, "_fileLoadTimeoutCts");
                        var accepted = manager.CurrentSession;
                        var intent = manager.CaptureLocalSubtitleIntent();
                        Require(service.IsLoading && service.IsSwitchingContent &&
                            (bool?)Get(service, "_restorePausedAfterLoad") == paused,
                            "The pending-load rejection fixture did not hold the replacement load's pause ownership.");
                        wire.FailNextTrackChange = true;
                        var bitmap = accepted!.SubtitleUrls.Single(t => t.Index == Dvd);
                        await service.SetSubtitleTrackAndPersistAsync(Dvd + 1, bitmap.Language, bitmap);
                        Require(service.IsLoading && service.IsSwitchingContent &&
                            (bool?)Get(service, "_restorePausedAfterLoad") == paused &&
                            Equals(Get(service, "_fileLoadGeneration"), generation) &&
                            ReferenceEquals(Get(service, "_fileLoadTimeoutCts"), timeoutOwner) &&
                            ReferenceEquals(manager.CurrentSession, accepted) && manager.CaptureLocalSubtitleIntent() == intent &&
                            wire.PreferenceWrites == writes && wire.Replans.Count == replans + 2,
                            "Rejected burn-in stole a pending replacement load's state, pause ownership, intent or preference.");
                        release.TrySetResult();
                    });
                    await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null &&
                        mpv.IsPaused == paused && service.IsPaused == paused);
                    Require(Get(service, "_restorePausedAfterLoad") == null && service.ActiveSubtitleServerIndex == Spanish &&
                        NativeSidMatches(service, mpv, Spanish) && wire.PreferenceWrites == writes &&
                        wire.Replans.Count == replans + 2,
                        "The held FILE_LOADED failed to consume its original pause and subtitle state after burn-in rejection.");
                }
                finally { release.TrySetResult(); media.BeforeMediaResponse = null; wire.FailNextTrackChange = false; }
            }
            Program.Log("PASS: rejected bitmap replans during held quality loads preserve loading and pause ownership until the original FILE_LOADED completes.");

            // Start the pick before a quality replan has reached BeginMpvLoad,
            // then reject it only after the new load owns a watchdog/pause slot.
            foreach (var paused in new[] { false, true })
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var qualityReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseQuality = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var rejectionReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseRejection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var mediaReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseMedia = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.BeforeQualityChange = ct => { qualityReady.TrySetResult(); return releaseQuality.Task.WaitAsync(ct); };
                wire.BeforeTrackRejection = ct => { rejectionReady.TrySetResult(); return releaseRejection.Task.WaitAsync(ct); };
                media.BeforeMediaResponse = () => { mediaReady.TrySetResult(); return releaseMedia.Task; };
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                var retainedIntent = manager.CaptureLocalSubtitleIntent();
                try
                {
                    await Loaded(async () =>
                    {
                        var quality = (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!;
                        await qualityReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(Get(service, "_fileLoadTimeoutCts") == null && service.IsLoading && service.IsSwitchingContent,
                            "The queued-pick fixture did not hold quality before its native load.");
                        wire.FailNextTrackChange = true;
                        var bitmap = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Dvd);
                        var pick = service.SetSubtitleTrackAndPersistAsync(Dvd + 1, bitmap.Language, bitmap);
                        Require(!pick.IsCompleted && Get(service, "_subtitleReplanPauseIntent") != null,
                            "The bitmap pick did not queue behind the held quality replan.");
                        releaseQuality.TrySetResult();
                        await quality.WaitAsync(TimeSpan.FromSeconds(6));
                        await mediaReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        await rejectionReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        var timeoutOwner = Get(service, "_fileLoadTimeoutCts") as CancellationTokenSource;
                        var generation = Get(service, "_fileLoadGeneration");
                        Require(timeoutOwner != null && !timeoutOwner.IsCancellationRequested && mpv.IsPaused,
                            "Quality did not begin a held load before rejecting the queued bitmap pick.");
                        releaseRejection.TrySetResult();
                        await pick.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(service.IsLoading && service.IsSwitchingContent && mpv.IsPaused &&
                            (bool?)Get(service, "_restorePausedAfterLoad") == paused &&
                            ReferenceEquals(Get(service, "_fileLoadTimeoutCts"), timeoutOwner) && timeoutOwner?.IsCancellationRequested == false &&
                            Equals(Get(service, "_fileLoadGeneration"), generation) && manager.CaptureLocalSubtitleIntent() == retainedIntent &&
                            service.ActiveSubtitleServerIndex == Spanish && wire.PreferenceWrites == writes && wire.Replans.Count == replans + 2,
                            "A queued rejected bitmap pick stole the later quality load's flags, watchdog, pause or subtitle state.");
                        releaseMedia.TrySetResult();
                    });
                    await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading &&
                        mpv.IsPaused == paused && service.IsPaused == paused);
                    Require(Get(service, "_restorePausedAfterLoad") == null && Get(service, "_fileLoadTimeoutCts") == null &&
                        service.ActiveSubtitleServerIndex == Spanish && NativeSidMatches(service, mpv, Spanish) && wire.PreferenceWrites == writes,
                        "FILE_LOADED lost its original pause/subtitle state after rejecting the earlier queued bitmap pick.");
                }
                finally
                {
                    releaseQuality.TrySetResult(); releaseRejection.TrySetResult(); releaseMedia.TrySetResult();
                    wire.BeforeQualityChange = null; wire.BeforeTrackRejection = null;
                    media.BeforeMediaResponse = null; wire.FailNextTrackChange = false;
                }
            }
            Program.Log("PASS: bitmap picks queued behind quality replans cannot steal the later load's watchdog/loading/pause ownership when rejected before FILE_LOADED.");

            // Recovery owns transient state before it has any native-load CTS.
            // Reject a bitmap pick while the real replacement start is held.
            foreach (var paused in new[] { false, true })
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var startReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.BeforeStart = ct => { startReady.TrySetResult(); return releaseStart.Task.WaitAsync(ct); };
                wire.AutomaticSubtitle = Spanish;
                var writes = wire.PreferenceWrites;
                try
                {
                    await Loaded(async () =>
                    {
                        var recovery = (Task)Call(service, "RecoverInterruptedStreamCoreAsync", 12d, "progress-reporting-failed", null)!;
                        await startReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        var position = Get(service, "_prematureEofRecoveryPosition");
                        var generation = Get(service, "_fileLoadGeneration");
                        var intent = manager.CaptureLocalSubtitleIntent();
                        Require(service.IsLoading && service.IsSwitchingContent && (bool)Get(service, "_prematureEofRecoveryActive")! &&
                            Get(service, "_fileLoadTimeoutCts") == null,
                            "The recovery rejection fixture did not hold a replacement start before native loading.");
                        wire.FailNextTrackChange = true;
                        var bitmap = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Dvd);
                        await service.SetSubtitleTrackAndPersistAsync(Dvd + 1, bitmap.Language, bitmap);
                        Require(!recovery.IsCompleted && service.IsLoading && service.IsSwitchingContent &&
                            (bool)Get(service, "_prematureEofRecoveryActive")! && Equals(Get(service, "_prematureEofRecoveryPosition"), position) &&
                            Get(service, "_fileLoadTimeoutCts") == null && Equals(Get(service, "_fileLoadGeneration"), generation) &&
                            manager.CaptureLocalSubtitleIntent() == intent && service.ActiveSubtitleServerIndex == Spanish &&
                            wire.PreferenceWrites == writes,
                            "Rejected burn-in cleared another recovery's transient flags/position or changed its subtitle intent.");
                        releaseStart.TrySetResult();
                        await recovery.WaitAsync(TimeSpan.FromSeconds(6));
                    });
                    await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading &&
                        mpv.IsPaused == paused && service.IsPaused == paused);
                    Require(service.ActiveSubtitleServerIndex == Spanish && NativeSidMatches(service, mpv, Spanish) &&
                        wire.PreferenceWrites == writes && Get(service, "_playbackTerminalState") == null,
                        "The surviving recovery failed to finish with the original pause/subtitle after bitmap rejection.");
                }
                finally
                {
                    releaseStart.TrySetResult(); wire.BeforeStart = null;
                    wire.AutomaticSubtitle = -1; wire.FailNextTrackChange = false;
                }
            }
            Program.Log("PASS: rejected bitmap picks preserve an in-flight stream recovery's transient ownership before its load/watchdog exists, and recovery restores the original pause/subtitle.");

            // Persistent rejection (both terminal decisions and request errors)
            // must never turn a healthy stream into terminal playback recovery.
            foreach (var terminalRejection in new[] { false, true })
            foreach (var paused in new[] { false, true })
            foreach (var requested in new[] { Dvd, -1, Spanish })
            {
                var baseline = requested == Dvd ? Spanish : Dvd;
                var baselineTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == baseline);
                if (service.ActiveSubtitleServerIndex != baseline)
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(baseline + 1, baselineTrack.Language, baselineTrack));
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var retainedSession = manager.CurrentSession;
                var retainedIntent = manager.CaptureLocalSubtitleIntent();
                var retainedUrl = manager.StreamUrl;
                var retainedSid = mpv.GetPropertyDouble("sid");
                var loadGeneration = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                wire.RejectTrackChanges = true;
                wire.TerminalTrackRejection = terminalRejection;
                try
                {
                    for (var pick = 0; pick < 2; pick++)
                    {
                        var positionBeforePick = mpv.Position;
                        Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", requested, true)!,
                            "A rejected subtitle was reported as executed.");
                        await Until(() => !service.IsLoading && mpv.IsPaused == paused && service.IsPaused == paused);
                        await Task.Delay(100);
                        if (!paused) await Until(() => mpv.Position > positionBeforePick + 0.02);
                        else Require(Math.Abs(mpv.Position - positionBeforePick) < 0.5,
                            "A rejected subtitle switch moved the paused playback position.");
                        Require(ReferenceEquals(manager.CurrentSession, retainedSession) && manager.StreamUrl == retainedUrl &&
                            manager.CaptureLocalSubtitleIntent() == retainedIntent && manager.SubtitleSelectionForLoad == baseline &&
                            service.ActiveSubtitleServerIndex == baseline && mpv.GetPropertyDouble("sid") == retainedSid &&
                            Equals(Get(service, "_fileLoadGeneration"), loadGeneration) && wire.PreferenceWrites == writes &&
                            wire.Replans.Count == replans + pick + 1 && Get(service, "_playbackTerminalState") == null &&
                            Get(service, "_pendingSubtitlePreferenceIndex") == null && Get(service, "_pendingSubtitlePreferenceOwner") == null &&
                            Get(service, "_pendingInitialServerSubtitleIndex") == null && !(bool)Get(service, "_switchingContent")! &&
                            !(bool)Get(service, "_prematureEofRecoveryActive")!,
                            "Persistent subtitle rejection changed the stream/intent/pause, saved a preference, retried or entered terminal recovery.");
                    }
                }
                finally { wire.RejectTrackChanges = false; }
            }
            Program.Log("PASS: persistent terminal/request rejection of bitmap, Off and text picks retains the executed stream, intent and pause without reloads, retries or preference writes.");

            // A failed room replan can leave its pause slot populated without
            // a pending native load. It must not make later rejection preserve
            // this request's temporary freeze/loading state indefinitely.
            foreach (var paused in new[] { false, true })
            foreach (var requested in new[] { Dvd, -1 })
            {
                var baseline = requested == Dvd ? Spanish : Dvd;
                if (service.ActiveSubtitleServerIndex != baseline)
                {
                    var baselineTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == baseline);
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(baseline + 1, baselineTrack.Language, baselineTrack));
                }
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetWatchTogetherPlayback(true);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var retainedSession = manager.CurrentSession;
                var retainedIntent = manager.CaptureLocalSubtitleIntent();
                var retainedSid = mpv.GetPropertyDouble("sid");
                var generation = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                Require(!service.IsLoading && !service.IsSwitchingContent && Get(service, "_fileLoadTimeoutCts") == null,
                    "The stale-pause fixture unexpectedly has a pending native load.");
                Set(service, "_restorePausedAfterLoad", !paused);
                wire.RejectTrackChanges = true;
                try
                {
                    Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", requested, true)!,
                        "A rejected subtitle with a stale pause slot was reported as executed.");
                    Require(!service.IsLoading && !service.IsSwitchingContent && Get(service, "_restorePausedAfterLoad") == null,
                        "A stale room pause slot preserved loading without a pending FILE_LOADED.");
                    await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                    Require(ReferenceEquals(manager.CurrentSession, retainedSession) && manager.CaptureLocalSubtitleIntent() == retainedIntent &&
                        service.ActiveSubtitleServerIndex == baseline && mpv.GetPropertyDouble("sid") == retainedSid &&
                        Equals(Get(service, "_fileLoadGeneration"), generation) && Get(service, "_fileLoadTimeoutCts") == null &&
                        Get(service, "_pendingSubtitlePreferenceOwner") == null && wire.PreferenceWrites == writes && wire.Replans.Count == replans + 1,
                        "Rejected burn-in with a stale pause slot changed the stream, subtitle intent, pause or preference.");
                }
                finally { wire.RejectTrackChanges = false; service.SetWatchTogetherPlayback(false); }
            }
            Program.Log("PASS: rejected bitmap and Off replans ignore stale room pause slots without pending native loads and restore the surviving stream and room pause intent.");

            // Rejection must resolve transport intent at rollback, not from the
            // pause captured before the request. Hold the actual wire request
            // while room authority or the viewer changes that intent.
            foreach (var room in new[] { false, true })
            foreach (var paused in new[] { false, true })
            foreach (var requested in new[] { Dvd, -1, Spanish })
            {
                var baseline = requested == Dvd ? Spanish : Dvd;
                if (service.ActiveSubtitleServerIndex != baseline)
                {
                    var baselineTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == baseline);
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(baseline + 1, baselineTrack.Language, baselineTrack));
                }
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                Require(manager.SubtitleSelectionForLoad == baseline && service.ActiveSubtitleServerIndex == baseline &&
                    (baseline == Dvd ? mpv.GetPropertyDouble("sid") <= 0 : NativeSidMatches(service, mpv, baseline)),
                    "Rejected-pause fixture baseline was not applied before starting the held request.");
                service.SetWatchTogetherPlayback(room);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var retainedSession = manager.CurrentSession;
                var retainedIntent = manager.CaptureLocalSubtitleIntent();
                var retainedSid = mpv.GetPropertyDouble("sid");
                var loadGeneration = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.RejectTrackChanges = true;
                wire.TerminalTrackRejection = requested == Dvd;
                wire.TimeoutTrackRejection = requested == -1;
                wire.BeforeTrackRejection = ct => { entered.TrySetResult(); return release.Task.WaitAsync(ct); };
                try
                {
                    var pick = (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", requested, true)!;
                    try
                    {
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        await Until(() => service.IsLoading && service.IsSwitchingContent && mpv.IsPaused && service.IsPaused);
                        if (room) service.SetPaused(!paused); // newest host pause/play
                        else service.RequestUserPauseToggle(); // ignore the temporary native freeze
                    }
                    finally { release.TrySetResult(); }
                    Require(!await pick.WaitAsync(TimeSpan.FromSeconds(4)), "A rejected in-flight subtitle pick was reported as executed.");
                    await Until(() => !service.IsLoading && mpv.IsPaused == !paused && service.IsPaused == !paused);
                    var observedIntent = manager.CaptureLocalSubtitleIntent();
                    Program.Log($"Rejected-pause invariant: room={room}, initialPaused={paused}, requested={requested}, sessionSame={ReferenceEquals(manager.CurrentSession, retainedSession)}, intentSame={observedIntent == retainedIntent}, intentIndex={observedIntent.Track?.Index}, intentFile={observedIntent.MediaFileId}, intentAuto={observedIntent.IsAutomatic}, active={service.ActiveSubtitleServerIndex}, expectedActive={baseline}, sid={mpv.GetPropertyDouble("sid")}, expectedSid={retainedSid}, loadGeneration={Get(service, "_fileLoadGeneration")}, expectedLoadGeneration={loadGeneration}, writes={wire.PreferenceWrites}, expectedWrites={writes}, replans={wire.Replans.Count}, expectedReplans={replans + 1}, terminal={Get(service, "_playbackTerminalState") != null}, restorePause={Get(service, "_restorePausedAfterLoad") ?? "null"}, replanPause={Get(service, "_subtitleReplanPauseIntent") ?? "null"}, prefOwner={Get(service, "_pendingSubtitlePreferenceOwner") != null}, switching={service.IsSwitchingContent}, paused={mpv.IsPaused}, servicePaused={service.IsPaused}.");
                    Require(ReferenceEquals(manager.CurrentSession, retainedSession) &&
                        manager.CaptureLocalSubtitleIntent() == retainedIntent && service.ActiveSubtitleServerIndex == baseline &&
                        mpv.GetPropertyDouble("sid") == retainedSid && Equals(Get(service, "_fileLoadGeneration"), loadGeneration) &&
                        wire.PreferenceWrites == writes && wire.Replans.Count == replans + 1 &&
                        Get(service, "_restorePausedAfterLoad") == null && Get(service, "_subtitleReplanPauseIntent") == null &&
                        Get(service, "_pendingSubtitlePreferenceOwner") == null && !service.IsSwitchingContent,
                        "Rejected subtitle rollback lost current room/viewer pause intent, changed selection, reloaded or saved an unexecuted preference.");
                }
                finally
                {
                    release.TrySetResult();
                    wire.BeforeTrackRejection = null;
                    wire.RejectTrackChanges = wire.TerminalTrackRejection = wire.TimeoutTrackRejection = false;
                    service.SetWatchTogetherPlayback(false);
                }
            }
            Program.Log("PASS: room pause/play and viewer toggles during rejected bitmap/Off/text replans retain the latest intent without reloads or preference writes.");

            // A rejected Off request owns no replacement load. It must leave
            // the predecessor's still-pending FILE_LOADED pause slot intact.
            if (service.ActiveSubtitleServerIndex != Dvd)
            {
                var predecessorDvd = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Dvd);
                await Loaded(() => service.SetSubtitleTrackAndPersistAsync(Dvd + 1, predecessorDvd.Language, predecessorDvd));
            }
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            service.SetPaused(true);
            var predecessor = manager.CurrentSession!;
            var preparePending = (Task)Call(service, "PreparePlaybackTransportAsync", predecessor, Version(), manager.StreamUrl!, 0d, CancellationToken.None)!;
            await preparePending;
            var pendingTransport = preparePending.GetType().GetProperty("Result")!.GetValue(preparePending)!;
            var predecessorLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void PendingLoaded() => predecessorLoaded.TrySetResult();
            service.ContentLoaded += PendingLoaded;
            media.HoldNextMedia = true;
            try
            {
                Call(service, "ApplyPreparedTransport", pendingTransport);
                Call(service, "BeginMpvLoad", pendingTransport, true);
                await media.PendingMediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var pendingGeneration = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                wire.RejectTrackChanges = true;
                var rejected = false;
                try { await service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = null }); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected && !predecessorLoaded.Task.IsCompleted && Equals(Get(service, "_restorePausedAfterLoad"), true) &&
                    Equals(Get(service, "_fileLoadGeneration"), pendingGeneration) && wire.PreferenceWrites == writes,
                    "Rejected SiloCast Off cleared or replaced its predecessor's pending paused load.");
                media.PendingMediaRelease.TrySetResult();
                await predecessorLoaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await Until(() => Get(service, "_loadedMediaInitCts") == null && mpv.IsPaused && service.IsPaused);
            }
            finally
            {
                wire.RejectTrackChanges = false;
                media.PendingMediaRelease.TrySetResult();
                service.ContentLoaded -= PendingLoaded;
            }
            Program.Log("PASS: rejected SiloCast Off preserves an actual held predecessor load and its paused FILE_LOADED intent.");

            // Pre-play choices must change the composited video plan before
            // its first load, without saving an automatic/startup preference.
            await Until(() => Get(service, "_pendingSubtitlePreferenceIndex") == null);
            // A rejected startup refinement must not discard the playable plan
            // or retry the failed choice from FILE_LOADED (including Auto).
            foreach (var prePlaySelection in new int?[] { -1, Spanish, null })
            {
                var initial = await manager.StartSessionAsync(42, subtitleTrackIndex: Dvd);
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                var streamUrl = manager.StreamUrl;
                var watch = new WatchDetailResponse { EffectiveSubtitleMode = "always", EffectiveSubtitleLanguage = "spa" };
                Call(service, "ResolveInitialSubtitleSelection", watch, Version(), initial, null, prePlaySelection, null);
                Require(manager.SubtitleSelectionForLoad == (prePlaySelection ?? Spanish),
                    "Failure fixture did not establish a different pre-play subtitle choice.");
                wire.FailNextTrackChange = true;
                var retained = await (Task<PlaybackStartResponse>)Call(service, "ReconcileInitialSubtitlePlanAsync", initial, CancellationToken.None)!;
                Require(ReferenceEquals(retained, initial) && ReferenceEquals(manager.CurrentSession, initial) &&
                    manager.StreamUrl == streamUrl && retained.IsSubtitleBurnedIn &&
                    manager.SubtitleSelectionForLoad == Dvd && manager.IsSubtitleSelectionAutomatic &&
                    service.ActiveSubtitleServerIndex == Dvd && Get(service, "_pendingAutomaticSubtitleSelection") == null &&
                    Get(service, "_pendingSubtitleSelection") == null,
                    "A failed pre-play replan discarded the accepted plan or retained its failed subtitle intent.");
                await Loaded(async () =>
                {
                    var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", retained, Version(), manager.StreamUrl!, 0d, CancellationToken.None)!;
                    await prepare;
                    var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                    Call(service, "ApplyPreparedTransport", prepared);
                    Call(service, "BeginMpvLoad", prepared, false);
                });
                await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading &&
                    service.ActiveSubtitleServerIndex == Dvd && !mpv.IsPaused);
                Require(ReferenceEquals(manager.CurrentSession, initial) && wire.Replans.Count == replans + 1 &&
                    wire.PreferenceWrites == writes && Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                    Get(service, "_pendingSubtitlePreferenceIndex") == null && mpv.GetPropertyDouble("duration") > 0,
                    "Playback did not start on the retained plan, retried the failed subtitle or saved an unexecuted preference.");
            }
            Program.Log("PASS: failed explicit Off/text and automatic pre-play subtitle replans retain the playable burn-in plan and start native playback without retries or preference writes.");

            foreach (var prePlaySelection in new[] { -1, Spanish })
            {
                var initial = await manager.StartSessionAsync(42, subtitleTrackIndex: Dvd);
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                Require(initial.IsSubtitleBurnedIn, "Pre-play fixture did not establish the automatic burn-in plan.");
                Call(service, "ResolveInitialSubtitleSelection", manager.WatchDetail!, Version(), initial, null, prePlaySelection, null);
                var reconciled = await (Task<PlaybackStartResponse>)Call(service, "ReconcileInitialSubtitlePlanAsync", initial, CancellationToken.None)!;
                Require(!reconciled.IsSubtitleBurnedIn && wire.Replans.Count == replans + 1 &&
                    SelectedSubtitle(wire.Replans[^1]) == prePlaySelection && manager.SubtitleSelectionForLoad == prePlaySelection,
                    "Pre-play Off/text failed to remove the server-composited subtitle before loading video.");
                await Loaded(async () =>
                {
                    var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", reconciled, Version(), manager.StreamUrl!, 0d, CancellationToken.None)!;
                    await prepare;
                    var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                    Call(service, "ApplyPreparedTransport", prepared);
                    Call(service, "BeginMpvLoad", prepared, true);
                });
                await Until(() => service.ActiveSubtitleServerIndex == prePlaySelection &&
                    (prePlaySelection < 0 ? mpv.GetPropertyDouble("sid") <= 0 : NativeSidMatches(service, mpv, Spanish)) &&
                    Get(service, "_loadedMediaInitCts") == null);
                Require(wire.PreferenceWrites == writes && mpv.IsPaused,
                    "Pre-play subtitle reconciliation persisted an automatic choice or resumed paused playback.");
            }
            Program.Log("PASS: pre-play Off and text remove an automatic burn-in plan before native loading, execute the requested route and do not save preferences.");

            // A rejected refinement after start adoption is not a playback
            // failure. Exercise both real replacement callers, including the
            // requested-version state used by the next progress restart.
            manager.WatchDetail!.Versions.Add(Version(44));
            foreach (var rejection in new[] { "error", "terminal", "timeout" })
            foreach (var burnedGrant in new[] { false, true })
            foreach (var requested in new[] { -1, Spanish })
            {
                wire.RejectTrackChanges = false;
                var requestedTrack = manager.CurrentSession!.SubtitleUrls.FirstOrDefault(t => t.Index == requested);
                // Leaving the previous iteration's burn-in grant reloads the
                // transport, and FILE_LOADED releases the busy flags before its
                // initializer exists. Await that load's own subtitle epoch, or
                // the version switch below retires this pick mid-execution.
                if (manager.CurrentSession.IsSubtitleBurnedIn)
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(requested + 1, requestedTrack?.Language, requestedTrack));
                else await service.SetSubtitleTrackAndPersistAsync(requested + 1, requestedTrack?.Language, requestedTrack);
                await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null &&
                    service.ActiveSubtitleServerIndex == requested);
                service.SetPaused(true);
                await Until(() => mpv.IsPaused && service.IsPaused);
                var target = Version(manager.CurrentSession!.MediaFileId == 42 ? 44 : 42);
                wire.AutomaticSubtitle = burnedGrant ? Dvd : English;
                wire.RejectTrackChanges = true;
                wire.TerminalTrackRejection = rejection == "terminal";
                wire.TimeoutTrackRejection = rejection == "timeout";
                var starts = wire.Starts.Count;
                var replans = wire.Replans.Count;
                var writes = wire.PreferenceWrites;
                try
                {
                    await Loaded(() => service.SwitchVersionAsync(target));
                    await SettledReplacement();
                    Require((int?)Get(service, "_requestedMediaFileId") == target.FileId,
                        "A rejected subtitle refinement left the requested version on the outgoing file.");
                    await Loaded(() => (Task)Call(service, "RecoverInterruptedStreamCoreAsync", 0d, "progress-reporting-failed", null)!);
                    await SettledReplacement();
                    Require(wire.Starts.Count == starts + 2 && wire.Replans.Count == replans + 2 &&
                        wire.Starts.Skip(starts).All(start => start.GetProperty("file_id").GetString() == target.FileId.ToString()) &&
                        wire.Replans.Skip(replans).All(replan => replan.GetProperty("operation").GetString() == "track_change"),
                        "Replacement subtitle rejection retried, invoked failure recovery or restarted the old version.");
                }
                finally
                {
                    wire.RejectTrackChanges = false;
                    wire.TerminalTrackRejection = false;
                    wire.TimeoutTrackRejection = false;
                }

                async Task SettledReplacement()
                {
                    // Unique cross-file metadata can execute locally after a
                    // rejected refinement, without replacing the source intent.
                    var expected = burnedGrant ? Dvd : requested;
                    await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null &&
                        service.ActiveSubtitleServerIndex == expected);
                    Require(manager.CurrentSession!.MediaFileId == target.FileId && mpv.GetPropertyDouble("duration") > 0 &&
                        (requested != Spanish || manager.CaptureLocalSubtitleIntent().Track?.TrackId !=
                            manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Spanish).TrackId) &&
                        (expected == Spanish ? NativeSidMatches(service, mpv, Spanish) : mpv.GetPropertyDouble("sid") <= 0) &&
                        mpv.IsPaused && service.IsPaused && Get(service, "_playbackTerminalState") == null &&
                        !manager.CurrentSession.SubtitleReconciliationFailed && wire.PreferenceWrites == writes &&
                        Get(service, "_pendingSubtitlePreferenceIndex") == null,
                        "A rejected replacement refinement discarded playable video, retried the choice, lost pause or saved an unexecuted preference.");
                }
            }
            wire.AutomaticSubtitle = -1;
            await Loaded(() => service.SwitchVersionAsync(Version()));
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            Program.Log("PASS: rejected/terminal/timed-out replacement subtitle refinements load the accepted version and progress-restart plans, keep local picks or burn-in grants, show feedback and preserve version/pause without failure recovery or preference writes.");

            var automaticWrites = wire.PreferenceWrites;
            manager.RecordLocalSubtitleSelection(Dvd, isAutomatic: true);
            await Loaded(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", Dvd, false)!);
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            Require(manager.CurrentSession!.IsSubtitleBurnedIn && manager.IsSubtitleSelectionAutomatic &&
                manager.CaptureLocalSubtitleIntent().IsAutomatic && wire.PreferenceWrites == automaticWrites,
                "The load worker's automatic bitmap replan became an explicit subtitle preference.");

            // A playable track_change can decline burn-in, or retain it after
            // Off/text. Its FILE_LOADED must refuse the unexecuted choice, not
            // start another replan/load cycle. Exercise manual and Auto callers.
            foreach (var automatic in new[] { false, true })
            foreach (var requested in new[] { Dvd, -1, Spanish })
            {
                wire.DeclineTrackChanges = false;
                var baseline = requested == Dvd ? -1 : Dvd;
                if (manager.CurrentSession!.IsSubtitleBurnedIn != (baseline == Dvd))
                {
                    var baselineTrack = manager.CurrentSession.SubtitleUrls.FirstOrDefault(t => t.Index == baseline);
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(baseline + 1, baselineTrack?.Language, baselineTrack));
                }
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetPaused(true);
                await Until(() => mpv.IsPaused && service.IsPaused);
                var replansBeforeDecline = wire.Replans.Count;
                var writesBeforeDecline = wire.PreferenceWrites;
                wire.DeclineTrackChanges = true;
                await Loaded(() =>
                {
                    if (automatic)
                    {
                        manager.RecordLocalSubtitleSelection(requested < 0 ? null : requested, isAutomatic: true);
                        return (Task)Call(service, "SelectSubtitleByServerIndexAsync", requested, false)!;
                    }
                    var requestedTrack = manager.CurrentSession!.SubtitleUrls.FirstOrDefault(t => t.Index == requested);
                    return service.SetSubtitleTrackAndPersistAsync(requested + 1, requestedTrack?.Language, requestedTrack);
                });
                await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading);
                await Task.Delay(500);
                Require(wire.Replans.Count == replansBeforeDecline + 1,
                    $"Declined {(automatic ? "Auto" : "manual")} subtitle {requested} entered a replan/reload loop.");
                Require(manager.CurrentSession!.IsSubtitleBurnedIn == (baseline == Dvd) &&
                    service.ActiveSubtitleServerIndex == baseline && mpv.IsPaused && service.IsPaused &&
                    wire.PreferenceWrites == writesBeforeDecline && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                    "A declined subtitle change did not retain the executed selection/pause or saved an unexecuted preference.");
            }
            wire.DeclineTrackChanges = false;
            Program.Log("PASS: declined manual/Auto bitmap, Off and text changes settle after exactly one replan without saving unexecuted choices or changing viewer pause.");

            // Adoption is not execution: if A is accepted after the viewer
            // picks B, a rejected/unmatched retry must recover before A can be
            // picked again and persisted as though its pixels were loaded.
            foreach (var missingLatest in new[] { false, true })
            foreach (var paused in new[] { false, true })
            {
                var baselineEnglish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == English);
                if (manager.CurrentSession.IsSubtitleBurnedIn)
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(English + 1, baselineEnglish.Language, baselineEnglish));
                else await service.SetSubtitleTrackAndPersistAsync(English + 1, baselineEnglish.Language, baselineEnglish);
                await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading);
                service.SetPaused(paused);
                await Until(() => mpv.IsPaused == paused && service.IsPaused == paused);
                var retainedSession = manager.CurrentSession;
                var retainedIntent = manager.CaptureLocalSubtitleIntent();
                var generation = Get(service, "_fileLoadGeneration");
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                var starts = wire.Starts.Count;
                var bitmap = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == Dvd);
                var latest = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Spanish);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var mediaEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var mediaRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.BeforeTrackChange = async ct =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                    if (!missingLatest)
                        wire.BeforeTrackChange = _ => { wire.FailNextTrackChange = true; return Task.CompletedTask; };
                };
                var recoveryGate = (SemaphoreSlim)Get(service, "_streamRecoveryGate")!;
                await recoveryGate.WaitAsync();
                var recoveryHeld = true;
                Task? pick = null;
                try
                {
                    pick = service.SetSubtitleTrackAndPersistAsync(Dvd + 1, bitmap.Language, bitmap);
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await service.SetSubtitleTrackAndPersistAsync(Spanish + 1, latest.Language, latest);
                    wire.OmitSpanish = missingLatest;
                    release.TrySetResult();
                    await pick.WaitAsync(TimeSpan.FromSeconds(6));
                    Require(!ReferenceEquals(manager.CurrentSession, retainedSession) && manager.CurrentSession!.IsSubtitleBurnedIn &&
                        manager.CaptureLocalSubtitleIntent() == retainedIntent && service.ActiveSubtitleServerIndex == English &&
                        Get(service, "_pendingSubtitlePreferenceOwner") == null && wire.PreferenceWrites == writes &&
                        service.IsLoading && service.IsSwitchingContent && mpv.IsPaused &&
                        (bool)Get(service, "_prematureEofRecoveryActive")! && Equals(Get(service, "_fileLoadGeneration"), generation),
                        "A failed retry after burn-in adoption resumed an unloaded plan instead of rolling back and queuing recovery.");

                    // The missing row only affects the abandoned first plan;
                    // the recovery response supplies the complete inventory again.
                    wire.OmitSpanish = false;
                    bitmap = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Dvd);
                    await service.SetSubtitleTrackAndPersistAsync(Dvd + 1, bitmap.Language, bitmap);
                    Require(wire.PreferenceWrites == writes && (int?)Get(service, "_pendingSubtitlePreferenceIndex") == Dvd &&
                        service.IsLoading && service.IsSwitchingContent && Equals(Get(service, "_fileLoadGeneration"), generation),
                        "Re-picking the adopted bitmap persisted a burn-in grant before any matching transport load.");
                    media.BeforeMediaResponse = () => { mediaEntered.TrySetResult(); return mediaRelease.Task; };
                    await Loaded(async () =>
                    {
                        recoveryGate.Release(); recoveryHeld = false;
                        await mediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        Require(service.IsLoading && !Equals(Get(service, "_fileLoadGeneration"), generation) && wire.PreferenceWrites == writes,
                            "Recovery saved the re-picked bitmap before the replacement FILE_LOADED.");
                        mediaRelease.TrySetResult();
                    });
                    await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading &&
                        service.ActiveSubtitleServerIndex == Dvd && wire.PreferenceWrites == writes + 1);
                    Require(manager.CurrentSession!.IsSubtitleBurnedIn && manager.SubtitleSelectionForLoad == Dvd &&
                        mpv.IsPaused == paused && service.IsPaused == paused && wire.LastSubtitlePreferenceIndex == Dvd &&
                        wire.Replans.Count == replans + (missingLatest ? 2 : 3) && wire.Starts.Count == starts &&
                        Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_playbackTerminalState") == null &&
                        !(bool)Get(service, "_prematureEofRecoveryActive")!,
                        "Recovery did not load the re-picked bitmap exactly once with its executed preference and original viewer pause.");
                }
                finally
                {
                    release.TrySetResult(); mediaRelease.TrySetResult();
                    if (recoveryHeld) recoveryGate.Release();
                    if (pick != null) try { await pick.WaitAsync(TimeSpan.FromSeconds(6)); } catch (Exception) { }
                    wire.BeforeTrackChange = null; media.BeforeMediaResponse = null;
                    wire.OmitSpanish = wire.FailNextTrackChange = false;
                }
            }
            Program.Log("PASS: accepted burn-in plans followed by rejected/null latest-intent retries roll back and recover; re-picks save only after a matching FILE_LOADED and preserve viewer pause.");

            // A newer pick made during the first burn-in replan still refers
            // to the outgoing inventory. Its retry must use the accepted row's
            // TrackId, not send that outgoing combined index to the manager.
            foreach (var latest in new[] { Spanish, Dvd })
            {
                if (!manager.CurrentSession!.IsSubtitleBurnedIn)
                {
                    var baseline = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Dvd);
                    await Loaded(() => service.SetSubtitleTrackAndPersistAsync(Dvd + 1, baseline.Language, baseline));
                }
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                service.SetPaused(true);
                await Until(() => mpv.IsPaused && service.IsPaused);
                var latestTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == latest);
                var replans = wire.Replans.Count;
                var starts = wire.Starts.Count;
                var writes = wire.PreferenceWrites;
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.BeforeTrackChange = ct => { entered.TrySetResult(); return release.Task.WaitAsync(ct); };
                try
                {
                    await Loaded(async () =>
                    {
                        var off = service.SetSubtitleTrackAndPersistAsync(0, null, null);
                        Task? newer = null;
                        try
                        {
                            await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                            newer = service.SetSubtitleTrackAndPersistAsync(latest + 1, latestTrack.Language, latestTrack);
                            // Text admission now awaits sidecar readiness. Keep the
                            // old reply held until that asynchronous admission, not
                            // merely until the picker returns its pending Task.
                            await Until(() => manager.CaptureLocalSubtitleIntent().Track?.TrackId == latestTrack.TrackId);
                            Require(!off.IsCompleted && wire.PreferenceWrites == writes &&
                                manager.CaptureLocalSubtitleIntent().Track?.TrackId == latestTrack.TrackId,
                                "The held burn-in replan did not record the newer pick before adoption.");
                            wire.ReindexSpanish = wire.ReindexDvd = true;
                        }
                        finally { release.TrySetResult(); }
                        await off.WaitAsync(TimeSpan.FromSeconds(6));
                        if (newer != null) await newer.WaitAsync(TimeSpan.FromSeconds(6));
                    });
                    await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading &&
                        service.ActiveSubtitleServerIndex == latest + 10 && Get(service, "_pendingSubtitlePreferenceIndex") == null);
                    Require(wire.Replans.Count == replans + 2 && wire.Starts.Count == starts &&
                        wire.Replans.Skip(replans).All(p => p.GetProperty("operation").GetString() == "track_change") &&
                        SelectedSubtitle(wire.Replans[^1]) == latest + 10 &&
                        wire.Replans[^1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString() == latestTrack.TrackId &&
                        manager.SubtitleSelectionForLoad == latest + 10 &&
                        (latest == Dvd ? manager.CurrentSession!.IsSubtitleBurnedIn && mpv.GetPropertyDouble("sid") <= 0 :
                            !manager.CurrentSession!.IsSubtitleBurnedIn && NativeSidMatches(service, mpv, latest + 10)) &&
                        wire.PreferenceWrites == writes + 1 && wire.LastSubtitlePreferenceIndex == latest + 10 &&
                        mpv.IsPaused && service.IsPaused && !(bool)Get(service, "_prematureEofRecoveryActive")! &&
                        Get(service, "_playbackTerminalState") == null,
                        "A newer reindexed subtitle pick lost identity, pause or its preference, or escalated to playback recovery.");
                }
                finally { release.TrySetResult(); wire.BeforeTrackChange = null; }
                wire.ReindexSpanish = wire.ReindexDvd = false;
                await Loaded(() => (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!);
                await Until(() => Get(service, "_loadedMediaInitCts") == null && service.ActiveSubtitleServerIndex == latest);
            }
            Program.Log("PASS: newer text/bitmap picks during burn-in replans retry by retained TrackId against reindexed inventories without session recovery, saving only the executed rebound preference.");

            // A server may retain TrackId while rebuilding combined indices.
            // Both text-after-burn and burn-in loads must consume that accepted
            // identity, including the preference saved only after execution.
            foreach (var requested in new[] { Spanish, Dvd })
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                var requestedTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == requested);
                var writes = wire.PreferenceWrites;
                var replans = wire.Replans.Count;
                if (requested == Spanish) wire.ReindexSpanish = true;
                else wire.ReindexDvd = true;
                await Loaded(() => service.SetSubtitleTrackAndPersistAsync(requested + 1, requestedTrack.Language, requestedTrack));
                var reboundIndex = requested + 10;
                await Until(() => Get(service, "_loadedMediaInitCts") == null &&
                    service.ActiveSubtitleServerIndex == reboundIndex && Get(service, "_pendingSubtitlePreferenceIndex") == null);
                Require(wire.Replans.Count == replans + 1 && manager.SubtitleSelectionForLoad == reboundIndex &&
                    manager.CurrentSession!.SelectedSubtitleTrackId == requestedTrack.TrackId &&
                    (requested == Dvd ? manager.CurrentSession.IsSubtitleBurnedIn && mpv.GetPropertyDouble("sid") <= 0 :
                        !manager.CurrentSession.IsSubtitleBurnedIn && NativeSidMatches(service, mpv, reboundIndex)) &&
                    wire.PreferenceWrites == writes + 1 && wire.LastSubtitlePreferenceIndex == reboundIndex && mpv.IsPaused,
                    "A rebound burn-in/text plan loaded the outgoing index, replanned again, or lost its executed preference/pause.");
            }
            Program.Log("PASS: burn-in replans rebind text and bitmap identities to new combined indices, execute once and save the rebound preference while preserving pause.");

            // Older plans omit IDs. Only the accepted combined index can then
            // attest which bitmap is actually composited into the current video.
            var legacySession = manager.CurrentSession!;
            var legacyDvd = legacySession.SubtitleUrls.Single(t => t.Index == Dvd + 10);
            legacySession.SelectedSubtitleTrackId = "";
            legacyDvd.TrackId = "";
            var otherBitmap = new SubtitleTrackInfo { Index = 99, TrackId = "", Source = "embedded",
                Codec = "dvd_subtitle", Language = "spa", Label = "Spanish (DVD)", Delivery = "burn_in_only" };
            legacySession.SubtitleUrls.Add(otherBitmap);
            Require((bool)Call(service, "SelectSubtitleTrack", legacyDvd)! &&
                !(bool)Call(service, "SelectSubtitleTrack", otherBitmap)!,
                "Blank IDs claimed that a different bitmap was already burned in, or refused the granted index.");
            legacySession.SelectedSubtitleTrackIndex = null;
            Require(!(bool)Call(service, "SelectSubtitleTrack", legacyDvd)!,
                "A bitmap with neither a granted ID nor index was reported as executed.");
            legacySession.SelectedSubtitleTrackIndex = legacyDvd.Index;
            var legacyWrites = wire.PreferenceWrites;
            var legacyReplans = wire.Replans.Count;
            var legacyIntent = manager.CaptureLocalSubtitleIntent();
            wire.RejectTrackChanges = true;
            try
            {
                Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", otherBitmap.Index, true)!,
                    "A different blank-ID bitmap was reported as selected without an accepted replan.");
                Require(wire.Replans.Count == legacyReplans + 1 && wire.PreferenceWrites == legacyWrites &&
                    service.ActiveSubtitleServerIndex == legacyDvd.Index && manager.CaptureLocalSubtitleIntent() == legacyIntent &&
                    ReferenceEquals(manager.CurrentSession, legacySession),
                    "A blank-ID bitmap pick skipped replanning or persisted an unexecuted choice.");
                Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", legacyDvd.Index, false)! &&
                    wire.Replans.Count == legacyReplans + 1 && wire.PreferenceWrites == legacyWrites,
                    "Load-time selection failed to recognize the granted legacy burn-in index without replanning.");
            }
            finally { wire.RejectTrackChanges = false; }
            Program.Log("PASS: blank bitmap IDs require the granted index; a different pick replans and a rejection retains the actual burned-in selection without persisting it.");
        }
        finally
        {
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }

        async Task Loaded(Func<Task> action)
        {
            var subtitleGeneration = (long)Get(service, "_subtitleSidecarGeneration")!;
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready() => loaded.TrySetResult(); service.ContentLoaded += Ready;
            try
            {
                await action();
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8));
                // ContentLoaded is raised before the initializer installs its
                // CTS. Require its new subtitle epoch before accepting null CTS.
                await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > subtitleGeneration &&
                    !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
                Call(service, "StopPlaybackStallWatchdog");
            }
            finally { service.ContentLoaded -= Ready; }
        }
    }

    private static bool IsTrackChangeWithoutSubtitle(JsonElement replan)
        => replan.GetProperty("operation").GetString() == "track_change" && SelectedSubtitle(replan) < 0;

    private static int SelectedSubtitle(JsonElement replan)
        => replan.GetProperty("selected_tracks").TryGetProperty("subtitle", out var subtitle) &&
            subtitle.ValueKind == JsonValueKind.Object ? subtitle.GetProperty("index").GetInt32() : -1;

    private static bool NativeSidMatches(PlayerService service, MpvPlayer mpv, int expected)
    {
        var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
        lock (Get(service, "_subtitleTrackStateLock")!)
            return map.TryGetValue(expected, out var sid) && mpv.GetPropertyDouble("sid") == sid && sid > 0;
    }

    private static FileVersion Version(int fileId = 42) => new()
    {
        FileId = fileId, Duration = 60,
        AudioTracks = [new() { Language = "en", Default = true }],
        SubtitleTracks =
        [
            new() { Index = English, Language = "eng", Codec = "srt", External = true },
            new() { Index = Spanish, Language = "spa", Codec = "srt", External = true },
            new() { Index = Dvd, Language = "eng", Codec = "dvd_subtitle" },
        ],
    };

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native subtitle state did not settle."); }
    private static object? Call(object target, string method, params object?[] args)
    {
        var member = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameters = member.GetParameters();
        if (args.Length < parameters.Length)
        {
            var supplied = args.ToList();
            foreach (var parameter in parameters.Skip(args.Length))
            {
                if (!parameter.HasDefaultValue) throw new TargetParameterCountException($"Missing required fixture argument {parameter.Name} for {method}.");
                supplied.Add(parameter.DefaultValue);
            }
            args = supplied.ToArray();
        }
        return member.Invoke(target, args);
    }
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);

    /// <summary>Loopback origin for the plan's stream (a silent WAV) and the text sidecars.</summary>
    private sealed class MediaServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public int SubtitleRequests;
        public bool HoldNextMedia;
        public TaskCompletionSource PendingMediaEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PendingMediaRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Task>? BeforeMediaResponse;
        public MediaServer(string wavePath)
        {
            const int samples = 60 * 8000;
            using (var writer = new BinaryWriter(File.Create(wavePath)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
            }
            var wave = File.ReadAllBytes(wavePath);
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    var isMedia = context.Request.Url!.AbsolutePath == "/media.wav";
                    if (isMedia && HoldNextMedia)
                    {
                        HoldNextMedia = false;
                        PendingMediaEntered.TrySetResult();
                        await PendingMediaRelease.Task;
                    }
                    if (isMedia && Interlocked.Exchange(ref BeforeMediaResponse, null) is { } beforeMedia)
                        await beforeMedia();
                    if (!isMedia) Interlocked.Increment(ref SubtitleRequests);
                    // The origin ignores Range and returns the complete file.
                    var bytes = isMedia ? wave
                        : Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nBurned-in subtitle fixture\n");
                    context.Response.ContentType = isMedia ? "audio/wav" : "application/x-subrip";
                    context.Response.ContentLength64 = bytes.Length;
                    try
                    {
                        if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes);
                        context.Response.Close();
                    }
                    catch (HttpListenerException) { /* mpv closed the connection while seeking or reloading. */ }
                }
            });
        }
        public void Dispose() { PendingMediaRelease.TrySetResult(); _listener.Close(); }
    }

    /// <summary>Synthetic v3 server: track_change and quality_change replans keep the requested subtitle.</summary>
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        public bool AiDownload;
        private string SubtitleId(int index) => AiDownload && index == 3 ? "stored-91" : $"sub:{_file}:{index}";
        private int _subtitle = -1, _file = 42;
        public int AutomaticSubtitle = -1;
        public bool FailNextTrackChange;
        public bool RejectTrackChanges, TerminalTrackRejection, TimeoutTrackRejection;
        public Func<CancellationToken, Task>? BeforeTrackRejection;
        public Func<CancellationToken, Task>? BeforeTrackChange;
        public Func<CancellationToken, Task>? BeforeQualityChange;
        public Func<CancellationToken, Task>? BeforeSeek;
        public Func<CancellationToken, Task>? BeforeStart;
        public bool DeclineTrackChanges;
        public bool ReindexSpanish, ReindexDvd, ReuseSpanishIndex;
        public bool OmitSpanish;
        public int PreferenceWrites;
        public int LastSubtitlePreferenceIndex = -2;
        public List<JsonElement> Starts = [], Replans = [];
        private int CombinedIndex(int track)
            => track == English && ReindexSpanish && ReuseSpanishIndex ? Spanish :
                (track == Spanish && ReindexSpanish || track == Dvd && ReindexDvd) ? track + 10 : track;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "burn-in.invalid" && !(AiDownload && request.RequestUri?.Host == new Uri(origin).Host))
                throw new InvalidOperationException("Burn-in fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (AiDownload && path == "/api/v2/subtitles/42") return Json("""{"subtitles":[{"id":91,"language":"fra","format":"srt"}]}""");
            if (path.Contains("-prefs/", StringComparison.Ordinal))
            {
                PreferenceWrites++;
                if (request.Content is { } content)
                {
                    using var preference = JsonDocument.Parse(await content.ReadAsStringAsync(ct));
                    if (preference.RootElement.TryGetProperty("subtitle_track_index", out var index)) LastSubtitlePreferenceIndex = index.GetInt32();
                }
                return Json("{}");
            }
            if (path.Contains("/settings/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = doc.RootElement;
            if (path.EndsWith("/start"))
            {
                Starts.Add(body.Clone());
                if (BeforeStart is { } beforeStart)
                {
                    BeforeStart = null;
                    await beforeStart(ct);
                }
                _file = int.Parse(body.GetProperty("file_id").GetString()!);
                _subtitle = body.TryGetProperty("subtitle_track_index", out var initial) && initial.ValueKind == JsonValueKind.Number ? initial.GetInt32() : AutomaticSubtitle;
            }
            else
            {
                Replans.Add(body.Clone());
                if (body.GetProperty("operation").GetString() == "seek_reanchor" && BeforeSeek is { } beforeSeek)
                {
                    BeforeSeek = null;
                    await beforeSeek(ct);
                }
                if (body.GetProperty("operation").GetString() == "quality_change" && BeforeQualityChange is { } beforeQuality)
                {
                    BeforeQualityChange = null;
                    await beforeQuality(ct);
                }
                if (body.GetProperty("operation").GetString() == "track_change" && BeforeTrackChange is { } beforeChange)
                {
                    BeforeTrackChange = null;
                    await beforeChange(ct);
                }
                if ((FailNextTrackChange || RejectTrackChanges) && body.GetProperty("operation").GetString() == "track_change")
                {
                    FailNextTrackChange = false;
                    if (BeforeTrackRejection is { } beforeRejection) await beforeRejection(ct);
                    if (TerminalTrackRejection)
                        return Json("""{"protocol_version":3,"outcome":"not_playable","terminal":{"reason":"subtitle_unavailable","message":"Synthetic persistent subtitle rejection.","retryable":true}}""");
                    if (TimeoutTrackRejection) throw new TaskCanceledException("Synthetic subtitle request timeout.");
                    throw new InvalidOperationException("Synthetic subtitle replan failure before adoption.");
                }
                if (!DeclineTrackChanges || body.GetProperty("operation").GetString() != "track_change")
                {
                    var selected = SelectedSubtitle(body);
                    var selectedId = body.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selection)
                        ? selection.GetProperty("id").GetString() : null;
                    // Stable IDs take precedence over outgoing/rebound combined
                    // indices, including when restoring the fixture inventory.
                    _subtitle = selectedId == $"sub:{_file}:{Spanish}" ? Spanish :
                        selectedId == $"sub:{_file}:{Dvd}" ? Dvd :
                        selected == CombinedIndex(Spanish) ? Spanish : selected == CombinedIndex(Dvd) ? Dvd : selected;
                }
            }
            var burned = _subtitle == Dvd;
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = $"plan-{Starts.Count}-{Replans.Count}", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = origin + "/media.wav" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = $"file:{_file}:audio:0", index = 0 }, subtitle = _subtitle < 0 ? null : new { id = SubtitleId(_subtitle), index = CombinedIndex(_subtitle) } },
                    subtitle = new
                    {
                        mode = burned ? "burn_in" : _subtitle < 0 ? "off" : "render",
                        track_id = _subtitle < 0 ? null : SubtitleId(_subtitle),
                        inventory = new object[]
                        {
                            new { track_id = $"sub:{_file}:0", combined_index = CombinedIndex(English), language = "eng", codec = "srt", source = "external", url = origin + "/eng.srt", label = "English", delivery = "sidecar" },
                            new { track_id = $"sub:{_file}:1", combined_index = CombinedIndex(Spanish), language = "spa", codec = "srt", source = "external", url = origin + "/spa.srt", label = "Spanish", delivery = "sidecar" },
                            new { track_id = $"sub:{_file}:2", combined_index = CombinedIndex(Dvd), language = "eng", codec = "dvd_subtitle", source = "embedded", label = "English (DVD)", delivery = "burn_in_only" },
                        }.Concat(AiDownload ? new object[] { new { track_id = "stored-91", sync_key = "stored-91", combined_index = 3,
                            language = "fra", codec = "srt", source = "downloaded", url = origin + "/ai.srt", label = "AI French", delivery = "sidecar" } } : [])
                            .Where((_, index) => !OmitSpanish || index != Spanish).ToArray(),
                    },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
