using System.Collections.Concurrent;
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

internal static class SubtitleSidecarOwnershipNativeFixture
{
    internal static async Task RunReconciliationAsync(StackPanel parent)
    {
        foreach (var scenario in new[] { "load-start-queued-return", "load-start-queued-aba", "load-start-queued-off", "load-start-retired-newer-off" })
        {
            await CheckLoadStartAsync(scenario);
            Program.Log("PASS: reconciled subtitle lifecycle " + scenario);
        }
        await CheckBusyAsync(parent, "missing-native-503");
        Program.Log("PASS: missing native identity uses bounded async503 sidecar without remote sub-add.");
    }
    internal static async Task RunAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "newer-off", "newer-track", "reload-gap", "manual-reload-gap", "reload-cleanup", "loaded-file-cleanup", "close", "viewer-pauses", "viewer-resumes", "viewer-seeks", "oversized-chunked", "load-start-retired", "load-start-retired-with-prior", "load-start-retired-newer-off", "load-start-cached-track", "load-start-off", "loaded-settings-cached-track", "loaded-settings-off", "load-start-retired-bitmap", "burn-load-bitmap-playing", "burn-load-bitmap-paused", "burn-load-off-playing", "burn-load-off-paused", "burn-load-text-playing", "burn-load-text-paused", "load-start-superseded-bitmap-off", "load-start-superseded-bitmap-text", "burn-response-off-playing", "burn-response-text-playing" })
        {
            try
            {
                if (scenario.StartsWith("burn-load-", StringComparison.Ordinal) || scenario.StartsWith("burn-response-", StringComparison.Ordinal)) await CheckBurnInLoadIntentAsync(scenario);
                else if (scenario.StartsWith("load-start-", StringComparison.Ordinal) || scenario.StartsWith("loaded-settings-", StringComparison.Ordinal)) await CheckLoadStartAsync(scenario);
                else await CheckAsync(parent, scenario);
                Program.Log("PASS: sidecar ownership " + scenario);
            }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: " + scenario + ": " + ex); }
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    internal static async Task RunBusyAsync(StackPanel parent)
    {
        foreach (var scenario in new[] { "exhausted", "recovered", "recovered-playing", "viewer-pauses", "viewer-resumes", "viewer-seeks", "newer-off", "newer-track", "superseded-exhausted", "aba", "load", "profile", "close", "translation-completed", "timeout", "oversized-declared", "oversized-chunked", "not-found", "not-found-automatic", "no-context-load", "no-context-sync-stop" })
        {
            await CheckBusyAsync(parent, scenario);
            Program.Log("PASS: busy sidecar " + scenario);
        }
    }

    internal static async Task RunAiRefreshOwnershipAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "translation-profile", "translation-newer-off", "translation-newer-track", "translation-before-completion-off", "translation-cycle", "translation-newer-claim", "translation-buffer-off", "translation-buffer-user-pause", "translation-arbitration-admission", "translation-arbitration-mutation", "translation-read-lock-order" })
        {
            try { await CheckBusyAsync(parent, scenario); Program.Log("PASS: AI completion ownership " + scenario); }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: AI completion " + scenario + ": " + ex); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task CheckBusyAsync(StackPanel parent, string scenario)
    {
        var recovers = scenario.StartsWith("recovered", StringComparison.Ordinal) || scenario.StartsWith("viewer-", StringComparison.Ordinal);
        var readFailure = scenario is "timeout" or "oversized-declared" or "oversized-chunked" or "not-found" or "not-found-automatic";
        var translationCompleted = scenario.StartsWith("translation-", StringComparison.Ordinal);
        var noContext = scenario.StartsWith("no-context-", StringComparison.Ordinal);
        using var subtitles = new HeldSubtitleServer(false) { BusyResponses = readFailure ? 0 : recovers ? 2 : 100,
            ReadFailure = readFailure ? scenario : null };
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl(translationCompleted ? subtitles.Origin : "https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "busy-" + scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] }); await manager.StartSessionAsync(42);
        if (scenario == "missing-native-503")
        {
            var absent = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
            absent.StreamIndex = 99; absent.ContainerTrackId = "99";
        }
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        var wave = Path.Combine(Program.ResultDirectory, "busy-" + scenario + ".wav"); WriteWave(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        var startsPaused = scenario is not ("recovered-playing" or "viewer-pauses" or "translation-buffer-off" or "translation-buffer-user-pause");
        service.SetPaused(startsPaused); await Until(() => mpv.IsPaused == startsPaused);
        subtitles.Release.TrySetResult();
        object? replacement = null;
        if (scenario == "no-context-load")
        {
            subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave);
            var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(),
                subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
            await prepare;
            replacement = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
        }
        var notices = new ConcurrentQueue<string>();
        var noticeReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var noticeDrained = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveNotice(string[] message)
        {
            if (message.FirstOrDefault() == "silo-busy-notice-ready") noticeReady.TrySetResult();
            if (message.FirstOrDefault() == "silo-busy-notice-drained" && message.Length > 1) noticeDrained.TrySetResult(message[1]);
            if (message.FirstOrDefault() == "silo-busy-notice" && message.Length > 1) notices.Enqueue(message[1]);
        }
        mpv.ScriptMessageReceived += ObserveNotice;
        var noticeProbe = Path.Combine(Program.ResultDirectory, "silo-busy-notice-" + scenario + ".lua");
        await File.WriteAllTextAsync(noticeProbe, """
            local mp = require 'mp'
            local active_subtitle = '-1'
            mp.register_script_message('osc-set-active-subtitle', function(index)
                active_subtitle = index
            end)
            mp.register_script_message('busy-set-pending-subtitle', function(index)
                active_subtitle = index
            end)
            mp.register_script_message('osc-show-notice', function(payload)
                mp.commandv('script-message', 'silo-busy-notice', payload)
            end)
            mp.register_script_message('silo-busy-notice-barrier', function()
                mp.commandv('script-message', 'silo-busy-notice-drained', active_subtitle)
            end)
            mp.commandv('script-message', 'silo-busy-notice-ready')
            """);
        Call(mpv, "Command", (object)new[] { "load-script", noticeProbe });
        await noticeReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
        try
        {
            await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
            var priorSid = mpv.GetPropertyDouble("sid");
            Require(priorSid > 0 && wire.RequestedPreferences.Last() == 1, "Prior subtitle was not actually applied and persisted.");
            string? livePath = null;
            if (translationCompleted)
            {
                livePath = Path.Combine(Program.ResultDirectory, "busy-live-translation.vtt");
                await File.WriteAllTextAsync(livePath, "WEBVTT\n\n00:00:00.000 --> 00:00:59.000\nLive translation\n");
                Require(mpv.AddSubtitle(livePath, "AI translation", "en", select: true), "Live translation failed to attach.");
                priorSid = mpv.GetPropertyDouble("sid");
                Set(service, "_liveSubtitleSid", (int)priorSid); Set(service, "_liveSubtitlePath", livePath);
                Set(service, "_liveSubtitleJobId", 73L); Set(service, "_liveSubtitleFileId", 42);
            }
            if (scenario == "translation-profile")
            {
                wire.HoldSubtitleList = true;
                var originalInventory = manager.CurrentSession!.SubtitleUrls;
                var refresh = service.RefreshSubtitlesAfterAiAsync(42, 73);
                await wire.SubtitleListEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                api.SetProfile("replacement-profile");
                wire.SubtitleListRelease.TrySetResult();
                await refresh.WaitAsync(TimeSpan.FromSeconds(8));
                Require(ReferenceEquals(originalInventory, manager.CurrentSession.SubtitleUrls) &&
                    service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") == priorSid &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 1 }) &&
                    (int)Get(service, "_liveSubtitleSid")! == priorSid && File.Exists(livePath),
                    "Held AI completion refresh published old-profile inventory, selected/persisted a track or removed current live cues after profile retirement.");
                Program.Log("PASS: held AI completion inventory GET cannot mutate replacement-profile inventory/SID/preferences/live cues.");
                return;
            }
            if (scenario == "translation-read-lock-order")
            {
                var stateGate = Get(service, "_subtitleTrackStateLock")!;
                var begin = service.GetType().GetMethod("BeginSubtitleRead", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var oldRead = begin.Invoke(service, begin.GetParameters().Length == 0 ? [] : [null])!;
                var oldToken = (CancellationToken)oldRead.GetType().GetProperty("Token")!.GetValue(oldRead)!;
                var callbackRan = false;
                var callbackHeldState = false;
                using var registration = oldToken.Register(() =>
                {
                    callbackRan = true;
                    callbackHeldState = Monitor.IsEntered(stateGate);
                });
                var cachedTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
                await ((Task)Call(service, "PrefetchSubtitleSidecarForSelectionAsync", cachedTrack, (Func<bool>)(() => true))!)
                    .WaitAsync(TimeSpan.FromSeconds(4));
                Require(callbackRan && !callbackHeldState,
                    "Subtitle read replacement invoked synchronous cancellation while owning the state lock.");
                Program.Log("PASS: actual cached prefetch replaces and cancels its prior read outside the state lock.");
                return;
            }
            if (scenario == "translation-arbitration-admission")
            {
                var successor = (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var read = Get(service, "_subtitleReadOperation")!;
                var readToken = (CancellationToken)read.GetType().GetProperty("Token")!.GetValue(read)!;
                var staleTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
                var rejected = (Task<bool>)Call(service, "TrySetSubtitleTrackAndPersistAsync", 1, staleTrack.Language, staleTrack, false, (Func<bool>)(() => false))!;
                Require(!await rejected && ReferenceEquals(read, Get(service, "_subtitleReadOperation")) && !readToken.IsCancellationRequested,
                    "Rejected automatic attachment canceled or replaced the newer manual sidecar reader before admission.");
                subtitles.BusyResponses = 0;
                await successor.WaitAsync(TimeSpan.FromSeconds(8));
                Require(service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 && wire.RequestedPreferences.SequenceEqual(new[] { 1, 0 }),
                    "The newer held manual choice did not execute and persist after rejecting an older attachment.");
                Program.Log("PASS: rejected stale autoattachment leaves the actual newer manual sidecar read alive through execution.");
                return;
            }
            if (scenario == "translation-arbitration-mutation")
            {
                var stateGate = Get(service, "_subtitleTrackStateLock")!;
                var intent = service.BeginSubtitleActivationIntent();
                var boundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var release = new ManualResetEventSlim();
                var outsideChecks = 0;
                bool Guard()
                {
                    var accepted = intent();
                    if (!Monitor.IsEntered(stateGate) && Interlocked.Increment(ref outsideChecks) == 2)
                    {
                        boundary.TrySetResult();
                        if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("Selection boundary was not released.");
                    }
                    return accepted;
                }
                var staleTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
                var older = Task.Run(async () => await (Task<bool>)Call(service, "TrySetSubtitleTrackAndPersistAsync", 1, staleTrack.Language, staleTrack, false, (Func<bool>)Guard)!);
                try
                {
                    await boundary.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                }
                finally { release.Set(); }
                Require(!await older.WaitAsync(TimeSpan.FromSeconds(8)) && service.ActiveSubtitleServerIndex == -1 &&
                    manager.SubtitleSelectionForLoad == -1 && mpv.GetPropertyDouble("sid") <= 0 && wire.RequestedPreferences.SequenceEqual(new[] { 1, -1 }),
                    "An older true pre-lock owner snapshot overwrote newer Off in the accepted-intent mutation lock.");
                Program.Log("PASS: real owner-callback barrier between precheck and mutation cannot overwrite a later committed manual Off.");
                return;
            }
            if (scenario is "translation-buffer-off" or "translation-buffer-user-pause")
            {
                var intent = service.BeginSubtitleActivationIntent();
                service.RegisterSubtitleAiActivationIntent(73, 42, intent);
                // Seed only this fixture's owned cue file/job, then perform the
                // same real native helper pause as BeginLiveSubtitleTranslation.
                Set(service, "_resumeAfterLiveSubtitleBuffer", true);
                mpv.Pause();
                await Until(() => mpv.IsPaused);
                var genuinePause = scenario == "translation-buffer-user-pause";
                if (genuinePause) service.SetPaused(true);
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                Require(mpv.IsPaused == genuinePause && service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0,
                    "Manual Off retained the AI helper's temporary pause or discarded a genuine viewer pause.");
                service.SetPaused(true);
                Call(service, "ResumeAfterLiveSubtitleBuffer", 73L);
                Require(mpv.IsPaused, "Retired AI buffer completion resumed the viewer's later genuine pause.");
                Program.Log("PASS: actual native AI helper pause retires for manual Off while real viewer pause remains authoritative.");
                return;
            }
            if (scenario == "translation-newer-claim")
            {
                var intent = service.BeginSubtitleActivationIntent();
                service.RegisterSubtitleAiActivationIntent(73, 42, intent);
                var refresh = service.RefreshSubtitlesAfterAiAsync(42, 73, mayActivate: intent);
                await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var newer = service.BeginSubtitleActivationIntent();
                service.RegisterSubtitleAiActivationIntent(0, 42, newer);
                subtitles.BusyResponses = 0;
                await refresh.WaitAsync(TimeSpan.FromSeconds(8));
                Require(service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") == priorSid &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 1 }) && File.Exists(livePath),
                    "An older stored subtitle read executed after a newer AI/download activation intent claimed ownership.");
                Program.Log("PASS: a newer accepted AI/download intent retires an older pending stored-sidecar activation without cancelling its server job.");
                return;
            }
            if (scenario is "translation-newer-off" or "translation-newer-track" or "translation-before-completion-off" or "translation-cycle")
            {
                subtitles.BusyResponses = 0;
                wire.HoldSubtitleList = true;
                if (scenario == "translation-newer-track")
                {
                    manager.CurrentSession!.SubtitleUrls.Add(new SubtitleTrackInfo
                        { Index = 9, Id = 88, MediaFileId = 42, Source = "downloaded", Language = "spa", Codec = "srt", Url = subtitles.Origin + "/retained.srt" });
                    wire.IncludeRetainedDownloaded = true;
                }
                var originalInventory = manager.CurrentSession!.SubtitleUrls;
                var begin = typeof(PlayerService).GetMethod("BeginSubtitleActivationIntent");
                var intent = begin?.Invoke(service, null) as Func<bool> ?? (() => true);
                typeof(PlayerService).GetMethod("RegisterSubtitleAiActivationIntent")?.Invoke(service, [73L, 42, intent]);
                using var completion = JsonDocument.Parse("""{"job_id":73,"file_id":42,"subtitle_id":73}""");
                var beforeCompletion = scenario == "translation-before-completion-off";
                var selected = scenario == "translation-newer-track" ? 9 : -1;
                if (beforeCompletion)
                {
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!;
                    LateCue();
                }
                Task? refresh = null;
                if (beforeCompletion)
                    Call(service, "ApplySubtitleTranslationCompleted", completion.RootElement.Clone());
                else
                {
                    var method = typeof(PlayerService).GetMethod("RefreshSubtitlesAfterAiAsync")!;
                    object?[] arguments = method.GetParameters().Length switch
                    {
                        4 => [42, (long?)73, null, intent],
                        3 => [42, (long?)73, null],
                        _ => [42, (long?)73],
                    };
                    refresh = (Task)method.Invoke(service, arguments)!;
                    await wire.SubtitleListEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    if (scenario == "translation-cycle") Call(service, "CycleSubtitleBackward");
                    else await (Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!;
                    LateCue();
                }
                var staleCompletionStartedRead = beforeCompletion && wire.SubtitleListEntered.Task.IsCompleted;
                wire.SubtitleListRelease.TrySetResult();
                if (refresh != null) await refresh.WaitAsync(TimeSpan.FromSeconds(8));
                var selectedSid = mpv.GetPropertyDouble("sid");
                Require(!staleCompletionStartedRead && ReferenceEquals(originalInventory, manager.CurrentSession.SubtitleUrls) &&
                    service.ActiveSubtitleServerIndex == selected &&
                    (selected != 9 || manager.CurrentSession.SubtitleUrls.Single(t => t.Index == 9).Id == 88) &&
                    (selected < 0 ? selectedSid <= 0 : selectedSid > 0 && selectedSid != priorSid) &&
                    wire.RequestedPreferences.SequenceEqual(scenario == "translation-cycle" ? new[] { 1 } : new[] { 1, selected }) && mpv.IsPaused,
                    "An older AI completion refresh overrode the later manual subtitle intent, applied SID, current checkmark or saved preference.");
                Program.Log("PASS: older stored AI completion cannot supersede later manual selection before completion or during its held inventory GET.");
                return;

                void LateCue()
                {
                    using var cue = JsonDocument.Parse("""{"job_id":73,"cues":[{"start":1,"end":2,"text":"A late old-job cue"}]}""");
                    Call(service, "ApplySubtitleTranslationCues", cue.RootElement.Clone());
                }
            }
            var pending = translationCompleted ? service.RefreshSubtitlesAfterAiAsync(42, 73) :
                noContext ? Task.Run(() =>
                {
                    Require(SynchronizationContext.Current == null, "OSC regression must run without a synchronization context.");
                    return (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                }) : (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, scenario != "not-found-automatic")!;
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(150);
            Require(clock.Elapsed < TimeSpan.FromMilliseconds(900), "Busy read blocked the native dispatcher.");
            if (!readFailure) Require(!pending.IsCompleted, "Busy selection did not wait for Retry-After.");
            var responsive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            parent.DispatcherQueue.TryEnqueue(() => responsive.TrySetResult());
            await responsive.Task.WaitAsync(TimeSpan.FromSeconds(1));
            if (scenario == "newer-off" || scenario == "aba") await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
            if (scenario == "newer-track") await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
            if (scenario == "viewer-pauses") service.SetPaused(true);
            if (scenario == "viewer-resumes") service.SetPaused(false);
            if (scenario == "viewer-seeks") { mpv.Seek(15); await Until(() => mpv.GetPropertyDouble("time-pos") >= 14); }
            if (scenario == "superseded-exhausted") await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
            if (scenario == "load") Call(service, "LoadSubtitles");
            if (scenario == "profile") api.SetProfile("replacement");
            if (scenario == "close") await service.CloseAsync();
            if (noContext)
            {
                var readOwner = Get(service, "_subtitleReadOperation")!;
                var readToken = (CancellationToken)readOwner.GetType().GetProperty("Token")!.GetValue(readOwner)!;
                var readGeneration = (long)Get(service, "_loadedMediaInitGeneration")!;
                long cancellationGeneration = -1;
                var cancellationSawLoading = false;
                using var registration = readToken.Register(() =>
                {
                    cancellationGeneration = (long)Get(service, "_loadedMediaInitGeneration")!;
                    cancellationSawLoading = service.IsLoading;
                });
                if (scenario == "no-context-load")
                {
                    Call(service, "ApplyPreparedTransport", replacement); Call(service, "WireMpvEvents");
                    await Task.Run(() => Call(service, "BeginMpvLoad", replacement, true));
                    Require(cancellationGeneration > readGeneration && cancellationSawLoading,
                        "Read cancellation observed the old generation or an unpublished reload.");
                    await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(!pending.IsCompleted && wire.RequestedPreferences.SequenceEqual(new[] { 1 }),
                        "An OSC choice was dropped or persisted before replacement media loaded.");
                    subtitles.BusyResponses = 0;
                    subtitles.MediaRelease.TrySetResult();
                }
                else
                {
                    // Model the OSC shortcut's optimistic pending highlight.
                    mpv.SendScriptMessage("busy-set-pending-subtitle", "0");
                    await Task.Run(() => Call(service, "StopSubtitleSync"));
                    Require(cancellationGeneration == readGeneration, "Sync-stop regression unexpectedly changed the load generation.");
                }
            }
            if (scenario == "aba")
            {
                subtitles.BusyResponses = 0;
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
            }
            await pending.WaitAsync(TimeSpan.FromSeconds(12));
            if (scenario == "no-context-load")
            {
                await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
                Require(service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 1, 0 }) && manager.SubtitleSelectionForLoad == 0,
                    "A no-context OSC choice did not survive reload and persist exactly once after execution.");
            }
            if (scenario != "close")
            {
                mpv.SendScriptMessage("silo-busy-notice-barrier");
                await noticeDrained.Task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            if (readFailure || scenario == "no-context-sync-stop")
            {
                Require(notices.Any(n => n.Contains("could not be loaded", StringComparison.Ordinal)) &&
                    !notices.Any(n => n.Contains("still preparing", StringComparison.Ordinal)),
                    "Failed or interrupted subtitle read was missing actionable feedback or mislabeled as busy.");
            }
            if (scenario is "exhausted" or "missing-native-503")
                Require(notices.Any(n => n.Contains("still preparing", StringComparison.Ordinal)) &&
                    !notices.Any(n => n.Contains("could not be loaded", StringComparison.Ordinal)), "Busy exhaustion lost its distinct retry notice.");
            if (scenario == "no-context-sync-stop")
                Require(await noticeDrained.Task == "1", "A still-current retired read did not restore the applied OSC highlight.");
            if (scenario is "no-context-load" or "newer-off" or "newer-track" or "profile")
                Require(!notices.Any(n => n.Contains("still preparing", StringComparison.Ordinal) || n.Contains("could not be loaded", StringComparison.Ordinal)),
                    "A superseded or reload-retired read emitted a busy/error notice.");
            if (readFailure || translationCompleted || scenario is "exhausted" or "profile" or "superseded-exhausted" or "missing-native-503" or "no-context-sync-stop")
            {
                Require(service.ActiveSubtitleServerIndex == 1, "Busy failure replaced the accepted server subtitle selection.");
                Require(mpv.GetPropertyDouble("sid") == priorSid, "Busy failure changed the accepted native SID.");
                Require(wire.RequestedPreferences.ToArray().SequenceEqual(new[] { 1 }), "Busy failure persisted an unapplied choice.");
                Require(manager.SubtitleSelectionForLoad == 1, "Busy failure replaced the accepted manager intent.");
            }
            if (recovers)
            {
                Require(subtitles.EngRequests == 3, "Busy read did not retry twice asynchronously.");
                Require(service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") != priorSid, "Recovered read did not apply the requested native subtitle.");
                Require(wire.RequestedPreferences.ToArray().SequenceEqual(new[] { 1, 0 }), "Recovered read did not persist its accepted choice once.");
            }
            if (scenario == "newer-off") Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0, "Off lost to a pending busy selection.");
            if (scenario == "newer-track") Require(service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") == priorSid, "New track lost to a pending busy selection.");
            if (scenario == "aba") Require(wire.RequestedPreferences.ToArray().SequenceEqual(new[] { 1, -1, 0 }), "Retired same-track A persisted after the newer A.");
            if (translationCompleted)
                Require((int)Get(service, "_liveSubtitleSid")! == priorSid && (Get(service, "_liveSubtitlePath") as string) == livePath && File.Exists(livePath),
                    "Refused stored translation removed the live SID or its cue file.");
            if (translationCompleted || scenario is "exhausted" or "missing-native-503") Require(subtitles.EngRequests == 4, "Exhausted busy read exceeded its attempt budget.");
            if (readFailure) Require(subtitles.EngRequests == 1, "Failed text read was retried outside the bounded reader.");
            if (scenario is "newer-off" or "newer-track" or "load" or "profile" or "close" or "no-context-sync-stop") Require(subtitles.EngRequests == 1, "Retired busy read continued requesting.");
            using var logReader = new StreamReader(new FileStream(Path.Combine(Program.ResultDirectory, "mpv_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var nativeLog = logReader.ReadToEnd();
            Require(!nativeLog.Split('\n').Any(line => line.Contains("Run command: sub-add", StringComparison.Ordinal) && line.Contains(subtitles.Origin, StringComparison.Ordinal)), "Text selection issued a remote native sub-add.");
            Require(subtitles.SyncRequests == 0, "Busy prefetch fell through to a synchronous remote mpv sub-add.");
            if (scenario == "viewer-seeks") Require(mpv.GetPropertyDouble("time-pos") >= 14, "Busy completion undid the viewer's seek.");
            var expectedPaused = scenario is not ("recovered-playing" or "viewer-resumes");
            if (scenario != "close") Require(mpv.IsPaused == expectedPaused, "Busy recovery changed viewer pause intent.");
            if (translationCompleted)
            {
                subtitles.BusyResponses = 0;
                await service.RefreshSubtitlesAfterAiAsync(42, 73).WaitAsync(TimeSpan.FromSeconds(4));
                Require(service.ActiveSubtitleServerIndex == 2 && mpv.GetPropertyDouble("sid") > 0 && mpv.GetPropertyDouble("sid") != priorSid &&
                    (int)Get(service, "_liveSubtitleSid")! == 0 && !File.Exists(livePath) && !mpv.GetSubtitleTrackIds().Contains((int)priorSid) &&
                    wire.RequestedPreferences.ToArray().SequenceEqual(new[] { 1, 2 }) && mpv.IsPaused,
                    "Successful stored translation retry did not replace and clean up the retained live cues exactly once.");
            }
        }
        finally
        {
            subtitles.MediaRelease.TrySetResult();
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout"); Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= ObserveNotice;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    internal static async Task RunAcceptedReloadAsync(StackPanel parent)
    {
        foreach (var choice in new[] { "bitmap", "off", "text", "newer-manual" })
        {
            await CheckAcceptedReloadAsync(parent, choice);
            Program.Log("PASS: accepted subtitle reload " + choice);
        }
    }

    internal static async Task RunBitmapOwnershipAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var choice in new[] { "bitmap-stolen-busy", "bitmap-retired-profile", "bitmap-access-refresh" })
        {
            try { await CheckAcceptedReloadAsync(parent, choice); Program.Log("PASS: bitmap callback ownership " + choice); }
            catch (Exception ex) { failures.Add(choice + ": " + ex.Message); Program.Log("CASE FAIL: bitmap ownership " + choice + ": " + ex); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    internal static async Task RunBurnReadinessAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var choice in new[] { "busy-text", "ready-then-busy", "changed-file-busy", "changed-file-newer", "changed-file-cast", "changed-file-cast-newer-off" })
        {
            try
            {
                await CheckAcceptedReloadAsync(parent, choice);
                Program.Log("PASS: burned subtitle readiness " + choice);
            }
            catch (Exception ex) { failures.Add(choice + ": " + ex.Message); Program.Log("CASE FAIL: burned readiness " + choice + ": " + ex); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task CheckAcceptedReloadAsync(StackPanel parent, string choice)
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin) { SupportBurnInReplan = true, StartsBurned = choice != "bitmap" && !choice.StartsWith("bitmap-", StringComparison.Ordinal), HoldNextReplan = true };
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "accepted-" + choice)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var version = Version(); version.SubtitleTracks!.Add(new() { Index = 2, Language = "eng", Codec = "dvd_subtitle" });
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [version] }); await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie"); Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, "accepted-" + choice + ".wav"); WriteWave(wave);
        subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave); subtitles.Release.TrySetResult();
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        service.SetPaused(true); Call(service, "SendSubtitleListToOsc"); Call(service, "WireMpvEvents");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult(); service.ContentLoaded += Loaded;
        var castChoice = choice.StartsWith("changed-file-cast", StringComparison.Ordinal);
        var selected = choice == "bitmap" || choice.StartsWith("bitmap-", StringComparison.Ordinal) ? 2 : choice == "off" ? -1 : castChoice ? 1 : 0;
        var track = selected < 0 ? null : manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == selected);
        if (choice is "busy-text" or "ready-then-busy" or "changed-file-busy" or "changed-file-newer" || castChoice)
        {
            wire.HoldNextReplan = false;
            wire.SidecarReadCount = () => subtitles.EngRequests;
            subtitles.BusyResponses = choice == "busy-text" ? 100 : 0;
            subtitles.BusyAfter = choice == "ready-then-busy" ? 1 : int.MaxValue;
            wire.ChangeFileOnReplan = choice.StartsWith("changed-file-", StringComparison.Ordinal);
            wire.HoldNextReplan = choice is "changed-file-newer" or "changed-file-cast-newer-off";
            subtitles.BusyChangedFile = choice == "changed-file-busy";
            subtitles.MediaRelease.TrySetResult();
            Set(service, "_activeSubtitleServerIndex", 2); manager.RecordLocalSubtitleSelection(2);
            var noticeLogStart = new FileInfo(Path.Combine(Program.ResultDirectory, "mpv_log.txt")).Length;
            try
            {
                var originalChoice = castChoice
                    ? service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = track!.Index + 1 })
                    : service.SetSubtitleTrackAndPersistAsync(1, track!.Language, track);
                Task? newerChoice = null;
                if (choice == "changed-file-newer")
                {
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    var spanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
                    newerChoice = service.SetSubtitleTrackAndPersistAsync(2, spanish.Language, spanish);
                    wire.ReplanRelease.TrySetResult();
                }
                if (choice == "changed-file-cast-newer-off")
                {
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    newerChoice = service.SetSubtitleTrackAndPersistAsync(0, null, null);
                    wire.ReplanRelease.TrySetResult();
                }
                Exception? commandError = null;
                try { await originalChoice.WaitAsync(TimeSpan.FromSeconds(12)); }
                catch (ArgumentException error) when (castChoice) { commandError = error; }
                if (newerChoice != null) await newerChoice.WaitAsync(TimeSpan.FromSeconds(12));
                await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
                if (castChoice)
                {
                    var superseded = choice.EndsWith("-newer-off", StringComparison.Ordinal);
                    var expectedIndex = superseded ? -1 : 8;
                    Program.Log($"Changed-file Cast: superseded={superseded}, error={commandError?.GetType().Name ?? "none"}, file={manager.CurrentSession!.MediaFileId}, active={service.ActiveSubtitleServerIndex}, prefs={string.Join(',', wire.RequestedPreferences)}, sid={mpv.GetPropertyDouble("sid")}.");
                    Require(superseded ? commandError is ArgumentException : commandError == null,
                        "SiloCast rejected an accepted remapped subtitle or acknowledged a request superseded by newer Off.");
                    Require(manager.CurrentSession!.MediaFileId == 43 && service.ActiveSubtitleServerIndex == expectedIndex &&
                        manager.SubtitleSelectionForLoad == expectedIndex && wire.RequestedPreferences.SequenceEqual(new[] { expectedIndex }) &&
                        (superseded ? mpv.GetPropertyDouble("sid") <= 0 : mpv.GetPropertyDouble("sid") > 0),
                        "Changed-file SiloCast acknowledgement lost actual SID, latest intent, or exactly-once granted-file preference.");
                    using var castLog = new FileStream(Path.Combine(Program.ResultDirectory, "mpv_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    castLog.Seek(noticeLogStart, SeekOrigin.Begin);
                    using var castReader = new StreamReader(castLog);
                    Require(!castReader.ReadToEnd().Contains("Reconnecting playback", StringComparison.Ordinal),
                        "A valid changed-file SiloCast choice unnecessarily entered transport recovery.");
                }
                else if (choice == "busy-text")
                {
                    Require(wire.Replans == 0 && manager.CurrentSession!.IsSubtitleBurnedIn && service.ActiveSubtitleServerIndex == 2 &&
                        manager.SubtitleSelectionForLoad == 2 && wire.PreferenceWrites == 0,
                        "A busy text read committed a replacement over the applied burned subtitle or persisted an unapplied choice.");
                }
                else if (choice == "ready-then-busy")
                {
                    Require(wire.ReadsAtReplan.SequenceEqual(new[] { 1 }), "The burned transport was replanned before the advertised text sidecar was ready.");
                    var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    Require(subtitles.EngRequests == 1 && map.TryGetValue(0, out var sid) && mpv.GetPropertyDouble("sid") == sid &&
                        service.ActiveSubtitleServerIndex == 0 && wire.RequestedPreferences.SequenceEqual(new[] { 0 }),
                        "A ready same-file subtitle was discarded and requested again after committing the replacement.");
                }
                else if (choice == "changed-file-busy")
                {
                    Require(manager.CurrentSession!.MediaFileId == 43 && manager.CurrentSession.SelectedSubtitleTrackId == "sub:43:7",
                        "A new-file grant was rolled back to an obsolete server plan.");
                    Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0,
                        "A busy new-file read highlighted an obsolete index or persisted an unapplied preference.");
                    Require(Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                        "Exhausted manual selection retained unapplied preference authority.");
                    subtitles.BusyChangedFile = false;
                    // Re-run automatic selection from the retained valid grant,
                    // not a new viewer preference. It may execute successfully
                    // without reviving the exhausted manual save authority.
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", manager.SubtitleSelectionForLoad, false)!;
                    Require(service.ActiveSubtitleServerIndex == 7 && mpv.GetPropertyDouble("sid") > 0 &&
                        wire.PreferenceWrites == 0 && wire.Replans == 1,
                        $"Changed-file owner was redundantly replanned or its exhausted preference revived: replans={wire.Replans}, writes={wire.PreferenceWrites}, active={service.ActiveSubtitleServerIndex}, sid={mpv.GetPropertyDouble("sid")}.");
                    Require(Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                        "Automatic grant selection revived retired manual preference authority.");
                    var retryTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 7);
                    await service.SetSubtitleTrackAndPersistAsync(8, retryTrack.Language, retryTrack);
                    Require(Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null &&
                        service.ActiveSubtitleServerIndex == 7 && mpv.GetPropertyDouble("sid") > 0 &&
                        wire.RequestedPreferences.SequenceEqual(new[] { 7 }),
                        "Fresh explicit retry did not execute and persist its granted-file preference exactly once.");
                    using var logStream = new FileStream(Path.Combine(Program.ResultDirectory, "mpv_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    logStream.Seek(noticeLogStart, SeekOrigin.Begin);
                    using var logReader = new StreamReader(logStream);
                    var log = logReader.ReadToEnd();
                    Require(log.Contains("Subtitle still preparing", StringComparison.Ordinal), "A busy new-file read had no actionable native notice.");
                }
                else
                {
                    typeof(PlayerService).GetMethod("FlushLogs", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                    Program.Log($"Changed-file convergence: requests={string.Join(',', wire.ReplanSelections)}, file={manager.CurrentSession!.MediaFileId}, active={service.ActiveSubtitleServerIndex}, desired={manager.SubtitleSelectionForLoad}, prefs={string.Join(',', wire.RequestedPreferences)}, sid={mpv.GetPropertyDouble("sid")}.");
                    using var recoveryLog = new FileStream(Path.Combine(Program.ResultDirectory, "mpv_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    recoveryLog.Seek(noticeLogStart, SeekOrigin.Begin);
                    using var recoveryReader = new StreamReader(recoveryLog);
                    Require(!recoveryReader.ReadToEnd().Contains("Reconnecting playback", StringComparison.Ordinal),
                        "A stale old-file combined index forced recovery instead of direct latest-choice convergence.");
                    Require(wire.ReplanSelections.SequenceEqual(new[] { 0, 8 }) && wire.Replans == 2,
                        "The latest old-file choice did not converge directly using its remapped new-file identity.");
                    Require(!File.Exists(Path.Combine(Program.ResultDirectory, "player_subtitle_error.txt")),
                        "A stale old-file combined index triggered subtitle-switch recovery.");
                    Require(manager.CurrentSession!.MediaFileId == 43 && service.ActiveSubtitleServerIndex == 8 &&
                        manager.SubtitleSelectionForLoad == 8 && wire.RequestedPreferences.SequenceEqual(new[] { 8 }) && mpv.GetPropertyDouble("sid") > 0,
                        $"A newer manual choice was lost or persisted in the obsolete file's index space: file={manager.CurrentSession!.MediaFileId}, active={service.ActiveSubtitleServerIndex}, next={manager.SubtitleSelectionForLoad}, prefs={string.Join(',', wire.RequestedPreferences)}, sid={mpv.GetPropertyDouble("sid")}.");
                }
                Require(mpv.IsPaused, "Burned-to-text readiness changed viewer pause intent.");
            }
            finally
            {
                service.ContentLoaded -= Loaded;
                wire.ReplanRelease.TrySetResult();
                Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
                Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
                Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            }
            return;
        }
        if (choice.StartsWith("bitmap-", StringComparison.Ordinal))
        {
            var notices = new ConcurrentQueue<string>();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(string[] message)
            {
                if (message.FirstOrDefault() == "silo-bitmap-ready") ready.TrySetResult();
                if (message.FirstOrDefault() == "silo-bitmap-notice" && message.Length > 1) notices.Enqueue(message[1]);
                if (message.FirstOrDefault() == "silo-bitmap-drained") drained.TrySetResult();
            }
            mpv.ScriptMessageReceived += Observe;
            var script = Path.Combine(Program.ResultDirectory, choice + "-probe.lua");
            await File.WriteAllTextAsync(script, """
                local mp = require 'mp'
                mp.register_script_message('osc-show-notice', function(payload) mp.commandv('script-message','silo-bitmap-notice',payload) end)
                mp.register_script_message('bitmap-drain', function() mp.commandv('script-message','silo-bitmap-drained') end)
                mp.commandv('script-message','silo-bitmap-ready')
                """);
            Call(mpv, "Command", (object)new[] { "load-script", script });
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var beforeFile = (long)Get(service, "_playbackFileGeneration")!;
            try
            {
                var bitmapPending = service.SetSubtitleTrackAndPersistAsync(3, track!.Language, track);
                await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                if (choice == "bitmap-stolen-busy") Call(service, "BeginPlaybackBusy", true, null);
                else if (choice == "bitmap-access-refresh")
                    Require(api.TryInvalidateAccessContext(api.CaptureContext(), out _), "Bitmap fixture did not advance the read cache epoch.");
                else api.SetProfile("replacement-profile");
                var successorRevision = (long)Get(service, "_playbackBusyRevision")!;
                wire.ReplanRelease.TrySetResult();
                await bitmapPending.WaitAsync(TimeSpan.FromSeconds(8));
                mpv.SendScriptMessage("bitmap-drain"); await drained.Task.WaitAsync(TimeSpan.FromSeconds(4));
                if (choice == "bitmap-access-refresh")
                {
                    Require((long)Get(service, "_playbackFileGeneration")! > beforeFile,
                        "A read-cache refresh retired accepted bitmap playback despite unchanged identity.");
                    subtitles.MediaRelease.TrySetResult();
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                    await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
                    Require(service.ActiveSubtitleServerIndex == 2 && manager.CurrentSession!.IsSubtitleBurnedIn && mpv.IsPaused && wire.PreferenceWrites == 0,
                        "Accepted bitmap playback lost its applied choice/pause or reused an expired settings-write context after read refresh.");
                    return;
                }
                Require((long)Get(service, "_playbackFileGeneration")! == beforeFile && wire.PreferenceWrites == 0,
                    "Retired bitmap callback loaded a transport or persisted a choice after its owner departed.");
                if (choice == "bitmap-stolen-busy")
                    Require((long)Get(service, "_playbackBusyRevision")! == successorRevision && service.IsLoading &&
                        (bool)Get(service, "_switchingContent")!, "Old bitmap completion replaced or released its successor's busy claim.");
                else
                    Require(!service.IsLoading && !(bool)Get(service, "_switchingContent")! && notices.IsEmpty,
                        "Profile-retired bitmap work stranded its own flags or published feedback to the replacement profile.");
            }
            finally
            {
                wire.ReplanRelease.TrySetResult(); subtitles.MediaRelease.TrySetResult();
                service.ContentLoaded -= Loaded; mpv.ScriptMessageReceived -= Observe;
                Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
                Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
                Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            }
            return;
        }
        var loadGeneration = (long)Get(service, "_fileLoadGeneration")!;
        var held = new HeldContinuation(parent.DispatcherQueue, () => (long)Get(service, "_fileLoadGeneration")! > loadGeneration);
        Task pending;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(held);
            pending = service.SetSubtitleTrackAndPersistAsync(selected + 1, track?.Language, track);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4)); wire.ReplanRelease.TrySetResult();
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The transport has been accepted, but its caller's persistence
            // continuation stays held until the real FILE_LOADED initializer.
            // Supersede before execution: the initializer now owns saving a
            // successfully applied choice, even while its caller is held.
            if (choice == "newer-manual") await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            subtitles.MediaRelease.TrySetResult();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await Until(() => Get(service, "_loadedMediaInitCts") == null && !service.IsLoading);
            held.Release(); await pending.WaitAsync(TimeSpan.FromSeconds(5));
            var expected = choice == "newer-manual" ? -1 : selected;
            Require(wire.RequestedPreferences.ToArray().SequenceEqual(new[] { expected }),
                "FILE_LOADED retired an accepted manual preference or revived a superseded choice: " + string.Join(',', wire.RequestedPreferences));
        }
        finally
        {
            held.Release(); wire.ReplanRelease.TrySetResult(); subtitles.MediaRelease.TrySetResult(); service.ContentLoaded -= Loaded;
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout"); Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private sealed class HeldContinuation(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<bool> shouldHold) : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool released;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (callbacks)
            {
                if (!released && shouldHold()) { callbacks.Enqueue((callback, state)); Entered.TrySetResult(); return; }
                Dispatch(callback, state);
            }
        }
        public void Release()
        {
            lock (callbacks)
            {
                released = true;
                while (callbacks.TryDequeue(out var item)) Dispatch(item.Callback, item.State);
            }
        }
        private void Dispatch(SendOrPostCallback callback, object? state)
            => dispatcher.TryEnqueue(() =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { callback(state); }
                finally { SetSynchronizationContext(previous); }
            });
    }

    private static async Task CheckBurnInLoadIntentAsync(string scenario)
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin)
        {
            SupportBurnInReplan = true,
            StartsBurned = scenario.StartsWith("burn-load-off-", StringComparison.Ordinal) || scenario.StartsWith("burn-load-text-", StringComparison.Ordinal),
        };
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var version = Version(); version.SubtitleTracks!.Add(new() { Index = 2, Language = "eng", Codec = "dvd_subtitle" });
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [version] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave); subtitles.Release.TrySetResult();
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        var expectedPaused = scenario.EndsWith("-paused", StringComparison.Ordinal);
        service.SetPaused(expectedPaused); await Until(() => mpv.IsPaused == expectedPaused);
        Call(service, "SendSubtitleListToOsc"); Call(service, "WireMpvEvents");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult(); service.ContentLoaded += Loaded;
        Task? queuedChoice = null;
        Task? appliedChoice = null;
        try
        {
            var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, version,
                subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
            await prepare; var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
            Call(service, "ApplyPreparedTransport", prepared);
            var generationBeforeLoad = (long)Get(service, "_subtitleSidecarGeneration")!;
            Call(service, "BeginMpvLoad", prepared, expectedPaused);
            await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await Until(() => mpv.IsPaused);
            var responseRace = scenario.StartsWith("burn-response-", StringComparison.Ordinal);
            var selected = scenario.Contains("-bitmap-", StringComparison.Ordinal) ? 2 :
                scenario.Contains("-text-", StringComparison.Ordinal) ? 0 : -1;
            var track = selected < 0 ? null : manager.CurrentSession!.SubtitleUrls.Single(item => item.Index == selected);
            if (responseRace)
            {
                wire.HoldNextReplan = true;
                var dvd = manager.CurrentSession!.SubtitleUrls.Single(item => item.Index == 2);
                queuedChoice = service.SetSubtitleTrackAndPersistAsync(3, dvd.Language, dvd);
                await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                appliedChoice = service.SetSubtitleTrackAndPersistAsync(selected + 1, track?.Language, track);
                wire.ReplanRelease.TrySetResult();
                await queuedChoice.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else appliedChoice = service.SetSubtitleTrackAndPersistAsync(selected + 1, track?.Language, track);
            await Until(() => wire.Replans > 0);
            Require(!loaded.Task.IsCompleted && service.IsLoading && wire.Replans > 0,
                "Fixture did not replan while replacement media stayed held.");
            await Until(() => Volatile.Read(ref subtitles.MediaRequests) >= 2);
            var requestedPause = (bool?)Get(service, "_restorePausedAfterLoad");
            subtitles.MediaRelease.TrySetResult();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generationBeforeLoad &&
                Get(service, "_loadedMediaInitCts") == null);
            await appliedChoice!.WaitAsync(TimeSpan.FromSeconds(5));
            Call(service, "StopPlaybackStallWatchdog");
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            var selectedNative = selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : selected == 2 ? manager.CurrentSession!.IsSubtitleBurnedIn :
                map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid;
            Program.Log($"Burn-load {scenario}: restore={requestedPause}, expected={expectedPaused}, native-paused={mpv.IsPaused}, active={service.ActiveSubtitleServerIndex}, replans={wire.Replans}, plan-burned={manager.CurrentSession!.IsSubtitleBurnedIn}, ordered-prefs={string.Join(',', wire.RequestedPreferences)}.");
            Require(requestedPause == expectedPaused && mpv.IsPaused == expectedPaused,
                "Subtitle replan mistook the pending load's internal pause for viewer pause intent.");
            Require(service.ActiveSubtitleServerIndex == selected && selectedNative && wire.Replans == (responseRace ? 2 : 1) &&
                (selected == 2 || !manager.CurrentSession!.IsSubtitleBurnedIn),
                "The subtitle transport choice did not settle on its matching native selection.");
            Require(wire.PreferenceWrites == 1 && wire.RequestedPreferences.LastOrDefault() == selected,
                "The subtitle replan lost or overwrote its latest preference.");
        }
        finally
        {
            subtitles.MediaRelease.TrySetResult(); wire.ReplanRelease.TrySetResult(); service.ContentLoaded -= Loaded;
            if (queuedChoice != null) try { await queuedChoice.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            if (appliedChoice != null) try { await appliedChoice.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckLoadStartAsync(string scenario)
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin) { SupportBurnInReplan = scenario == "load-start-superseded-bitmap-text" };
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        Call(service, "SendSubtitleListToOsc");
        var seen = new ConcurrentQueue<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(string[] args)
        {
            if (args.FirstOrDefault() == "silo-sidecar-probe-ready") ready.TrySetResult();
            if (args.FirstOrDefault() == "silo-sidecar-probe-selection" && args.Length > 1) seen.Enqueue(args[1]);
        }
        void Loaded() => loaded.TrySetResult();
        mpv.ScriptMessageReceived += Observe; service.ContentLoaded += Loaded;
        var script = Path.Combine(Program.ResultDirectory, scenario + "-observe.lua");
        await File.WriteAllTextAsync(script, """
            local mp = require 'mp'
            mp.register_script_message('osc-set-active-subtitle', function(index)
                mp.commandv('script-message', 'silo-sidecar-probe-selection', index)
            end)
            mp.commandv('script-message', 'silo-sidecar-probe-ready')
            """);
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mpv, [new[] { "load-script", script }]);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Task? pending = null;
        SemaphoreSlim? heldRestart = null;
        try
        {
            var retired = scenario is "load-start-retired" or "load-start-retired-with-prior" or "load-start-retired-newer-off";
            var supersededBitmap = scenario.StartsWith("load-start-superseded-bitmap-", StringComparison.Ordinal);
            var retiredBitmap = scenario == "load-start-retired-bitmap" || supersededBitmap;
            var afterFileLoaded = scenario.StartsWith("loaded-settings-", StringComparison.Ordinal);
            var selectOff = scenario.EndsWith("-off", StringComparison.Ordinal) || retiredBitmap && !supersededBitmap;
            if (retiredBitmap)
            {
                var bitmap = manager.CurrentSession!.SubtitleUrls.SingleOrDefault(track => track.Index == 2);
                if (bitmap == null)
                {
                    bitmap = new SubtitleTrackInfo { Index = 2, Language = "eng", Codec = "dvdsub", Source = "embedded" };
                    manager.CurrentSession.SubtitleUrls.Add(bitmap);
                }
                heldRestart = (SemaphoreSlim)Get(service, "_transportRestartGate")!;
                await heldRestart.WaitAsync();
                var restartGeneration = (long)Get(service, "_seekRestartGeneration")!;
                pending = Task.Run(() => service.SetSubtitleTrackAndPersistAsync(3, bitmap.Language, bitmap));
                await Until(() => (long)Get(service, "_seekRestartGeneration")! > restartGeneration);
                // A competing accepted session changes the helper's captured
                // session while its initial cancel-and-drain wait is held.
                if (!wire.SupportBurnInReplan) await manager.StartSessionAsync(42);
            }
            else if (retired)
            {
                if (scenario == "load-start-retired-with-prior")
                {
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
                    Require(service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") > 0 && wire.RequestedPreferences.Last() == 1,
                        "The reload did not start with a genuinely accepted prior choice.");
                }
                // Run off the fixture dispatcher so a broken synchronous
                // sub-add cannot prevent releasing the held media response.
                pending = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!);
                await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            else
            {
                subtitles.Release.TrySetResult();
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                if (!selectOff) await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                // An earlier burn-in/source choice must not outrank the
                // viewer's newer track/Off request during this load.
                Set(service, "_pendingInitialServerSubtitleIndex", 1);
            }
            await Task.Delay(100); seen.Clear();
            var preferencesBefore = wire.PreferenceWrites;
            var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(),
                subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
            await prepare;
            var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
            Call(service, "ApplyPreparedTransport", prepared);
            Call(service, "WireMpvEvents");
            var generationBeforeLoad = (long)Get(service, "_subtitleSidecarGeneration")!;
            Call(service, "BeginMpvLoad", prepared, false);
            await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!loaded.Task.IsCompleted && service.IsLoading, "Fixture did not hold the replacement before FILE_LOADED.");
            await Until(() => mpv.IsPaused);
            if (afterFileLoaded)
            {
                wire.HoldSettings = true;
                subtitles.MediaRelease.TrySetResult();
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await wire.SettingsEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(!service.IsLoading && Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                    service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") > 0,
                    "Fixture missed the executed-subtitle/optional-settings-await interval.");
                await Task.Delay(100); seen.Clear();
            }
            if (retiredBitmap)
            {
                Task? newerChoice = null;
                if (supersededBitmap)
                {
                    subtitles.Release.TrySetResult();
                    var newer = selectOff ? null : manager.CurrentSession!.SubtitleUrls.Single(track => track.Index == 0);
                    newerChoice = service.SetSubtitleTrackAndPersistAsync(selectOff ? 0 : 1, newer?.Language, newer);
                }
                heldRestart!.Release(); heldRestart = null;
                var retiredChoice = pending!;
                if (!supersededBitmap)
                    newerChoice = (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                await retiredChoice.WaitAsync(TimeSpan.FromSeconds(4));
                await Task.Delay(100);
                pending = newerChoice!;
            }
            else if (retired)
            {
                subtitles.Release.TrySetResult();
                if (scenario == "load-start-retired-newer-off")
                    await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            }
            else if (!selectOff)
            {
                var english = manager.CurrentSession!.SubtitleUrls!.Single(track => track.Index == 0);
                pending = Task.Run(() => service.SetSubtitleTrackAndPersistAsync(1, english.Language, english));
            }
            else pending = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!);
            if (scenario is "load-start-queued-return" or "load-start-queued-aba" or "load-start-queued-off")
            {
                await pending!.WaitAsync(TimeSpan.FromSeconds(1));
                Require(wire.PreferenceWrites == preferencesBefore, "A queued choice persisted before actual FILE_LOADED execution.");
                if (scenario == "load-start-queued-aba")
                {
                    await service.SetSubtitleTrackAndPersistAsync(0, null, null).WaitAsync(TimeSpan.FromSeconds(1));
                    var latest = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
                    await service.SetSubtitleTrackAndPersistAsync(1, latest.Language, latest).WaitAsync(TimeSpan.FromSeconds(1));
                    Require(wire.PreferenceWrites == preferencesBefore, "A retired queued ABA choice persisted before execution.");
                }
            }
            await Task.WhenAny(pending!, Task.Delay(400));
            if (wire.Replans > 0) await Until(() => Volatile.Read(ref subtitles.MediaRequests) >= 2);
            var earlyRegistered = retired && ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).ContainsKey(0);
            var earlyPublication = seen.ToArray();
            var pausedWhileLoading = mpv.IsPaused;
            subtitles.MediaRelease.TrySetResult();
            wire.SettingsRelease.TrySetResult();
            await pending!.WaitAsync(TimeSpan.FromSeconds(6));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            Call(service, "StopPlaybackStallWatchdog");
            var selected = selectOff ? -1 : 0;
            await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generationBeforeLoad &&
                Get(service, "_loadedMediaInitCts") == null);
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            var selectedNative = selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 :
                map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid;
            Program.Log($"Load-start {scenario}: early-cache={earlyRegistered}, early-OSC={string.Join(',', earlyPublication)}, " +
                $"active={service.ActiveSubtitleServerIndex}, next-load={manager.SubtitleSelectionForLoad}, sid={mpv.GetPropertyDouble("sid")}, prefs={wire.PreferenceWrites - preferencesBefore}, ordered-prefs={string.Join(',', wire.RequestedPreferences)}, replans={wire.Replans}.");
            Require(!earlyRegistered, "A pre-load sidecar registered after BeginMpvLoad retired its owner.");
            if (!afterFileLoaded)
            {
                Require(earlyPublication.Length == 0, "A subtitle completion published before the replacement FILE_LOADED: " + string.Join(',', earlyPublication));
                Require(pausedWhileLoading, "Subtitle selection resumed the replacement before FILE_LOADED.");
            }
            Require(service.ActiveSubtitleServerIndex == selected && manager.SubtitleSelectionForLoad == selected && selectedNative,
                "The replacement FILE_LOADED overwrote the latest viewer track/Off selection.");
            Require(wire.PreferenceWrites - preferencesBefore == 1 &&
                wire.RequestedPreferences.LastOrDefault() == selected, "The current viewer preference was lost or overwritten by a superseded choice.");
            if (scenario.StartsWith("load-start-queued-", StringComparison.Ordinal))
            {
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, false)!;
                Require(wire.PreferenceWrites - preferencesBefore == 1, "A repeated successful initializer duplicated the queued preference write.");
            }
            Require(!retiredBitmap || supersededBitmap || !wire.RequestedPreferences.Contains(2),
                "A retired bitmap absent from the replacement inventory persisted despite never applying.");
            Require(!supersededBitmap || wire.Replans == 0, "A superseded bitmap request replanned after the newer viewer choice.");
            await Until(() => !mpv.IsPaused);
        }
        finally
        {
            subtitles.Release.TrySetResult(); subtitles.MediaRelease.TrySetResult();
            wire.SettingsRelease.TrySetResult();
            heldRestart?.Release();
            if (pending != null) try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= Observe; service.ContentLoaded -= Loaded;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckAsync(StackPanel parent, string scenario)
    {
        using var subtitles = new HeldSubtitleServer(scenario == "oversized-chunked");
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        var startPaused = scenario == "viewer-resumes";
        service.SetPaused(startPaused); await Until(() => mpv.IsPaused == startPaused);
        // Prime the same-file lifecycle, so the replacement initializer's settings
        // await genuinely precedes its next LoadSubtitles generation change.
        Call(service, "SendSubtitleListToOsc");
        var seen = new ConcurrentQueue<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(string[] args)
        {
            if (args.FirstOrDefault() == "silo-sidecar-probe-ready") ready.TrySetResult();
            if (args.FirstOrDefault() == "silo-sidecar-probe-selection" && args.Length > 1) seen.Enqueue(args[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        var script = Path.Combine(Program.ResultDirectory, scenario + "-observe.lua");
        await File.WriteAllTextAsync(script, """
            local mp = require 'mp'
            mp.register_script_message('osc-set-active-subtitle', function(index)
                mp.commandv('script-message', 'silo-sidecar-probe-selection', index)
            end)
            mp.commandv('script-message', 'silo-sidecar-probe-ready')
            """);
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mpv, [new[] { "load-script", script }]);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        try
        {
            var persist = scenario != "reload-gap" && scenario != "reload-cleanup";
            Task pending;
            if (scenario == "reload-gap")
            {
                // Run the complete first FILE_LOADED initializer, not merely
                // its subtitle helper, before the replacement load cancels it.
                var owner = new CancellationTokenSource();
                Set(service, "_loadedMediaInitCts", owner);
                manager.RecordLocalSubtitleSelection(0, isAutomatic: true);
                pending = (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
            }
            else if (scenario == "oversized-chunked") pending = (Task)Call(service, "PrefetchSubtitleSidecarAsync", manager.CurrentSession!.SubtitleUrls![0], null)!;
            else pending = (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, persist)!;
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (scenario == "reload-gap")
            {
                // Pending execution must not publish a successful choice.
                await Task.Delay(80);
                Require(!seen.Contains("0"), "An initializer highlighted an unread subtitle before execution.");
                seen.Clear();
            }
            var uiTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Require(parent.DispatcherQueue.TryEnqueue(() => uiTick.TrySetResult()), "UI queue rejected fixture callback.");
            await uiTick.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var generation = (long)Get(service, "_subtitleSidecarGeneration")!;
            if (scenario is "newer-off" or "newer-track")
            {
                var selected = scenario == "newer-off" ? -1 : 1;
                await ((Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!).WaitAsync(TimeSpan.FromSeconds(4));
                await Until(() => seen.Contains(selected.ToString()));
            }
            else if (scenario is "reload-gap" or "manual-reload-gap")
            {
                // PR75 executes subtitles before optional settings. Hold an
                // actual replacement media read before FILE_LOADED instead.
                subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave);
                var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(),
                    subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
                await prepare;
                var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                Call(service, "ApplyPreparedTransport", prepared); Call(service, "WireMpvEvents");
                Call(service, "BeginMpvLoad", prepared, mpv.IsPaused);
                await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require((long)Get(service, "_subtitleSidecarGeneration")! == generation,
                    "Replacement already reached LoadSubtitles; fixture missed the cancellation gap.");
            }
            else if (scenario == "reload-cleanup") Call(service, "LoadSubtitles");
            else if (scenario == "close") await service.CloseAsync().WaitAsync(TimeSpan.FromSeconds(4));
            else if (scenario == "viewer-seeks")
            {
                mpv.Seek(15);
                await Until(() => mpv.GetPropertyDouble("time-pos") >= 14);
            }
            else if (scenario is "viewer-pauses" or "viewer-resumes")
            {
                service.SetPaused(!startPaused);
                await Until(() => mpv.IsPaused == !startPaused);
            }
            var pauseBeforeCompletion = mpv.IsPaused;
            subtitles.Release.TrySetResult();
            if (scenario == "manual-reload-gap")
            {
                await Task.Delay(150);
                Require(!seen.Contains("0") && wire.PreferenceWrites == 0,
                    "A pending manual choice was applied or persisted before replacement initialization finished.");
                subtitles.MediaRelease.TrySetResult();
            }
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200); // Drain real libmpv client/Lua message delivery.
            var observations = seen.ToArray();
            if (scenario is "newer-off" or "newer-track")
            {
                var selected = scenario == "newer-off" ? -1 : 1;
                Require(service.ActiveSubtitleServerIndex == selected, "The superseded sidecar changed the server selection.");
                Require(observations.LastOrDefault() == selected.ToString(),
                    "Superseded A published stale OSC selection: " + string.Join(",", observations));
                var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid,
                    "Native SID differs from the latest viewer selection.");
                Require(wire.PreferenceWrites == 1, "Superseded A persisted a stale subtitle preference.");
            }
            else if (scenario == "manual-reload-gap")
            {
                var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(service.ActiveSubtitleServerIndex == 0 && map.TryGetValue(0, out var sid) && mpv.GetPropertyDouble("sid") == sid,
                    "The latest pending manual choice was not adopted after compatible replacement initialization.");
                Require(wire.RequestedPreferences.ToArray().SequenceEqual(new[] { 0 }),
                    "The adopted manual preference was not persisted exactly once after application.");
            }
            else if (scenario is "reload-gap" or "reload-cleanup" or "close")
            {
                Require(!observations.Contains("0"), "A retired file initializer published an OSC selection: " + string.Join(",", observations));
                Require(((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).Count == 0, "A retired file initializer added a native subtitle.");
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0, "A retired prefetch registered a temporary file after cleanup.");
                Require(wire.PreferenceWrites == 0, "A retired initializer persisted a subtitle preference.");
            }
            else if (scenario == "oversized-chunked")
            {
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0,
                    "A chunked response exceeding the 16 MiB cap was registered.");
                Require(((List<string>)Get(service, "_subtitleSyncFiles")!).Count == 0,
                    "The rejected chunked response left a cleanup file.");
            }
            else if (scenario == "loaded-file-cleanup")
            {
                var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
                Require(files.Count == 1 && File.Exists(files[0]), "Successful prefetch did not register its file.");
                var path = files[0]; Call(service, "LoadSubtitles");
                Require(files.Count == 0 && !File.Exists(path), "Same-session file load retained the superseded cached file.");
                Require(!((List<string>)Get(service, "_subtitleSyncFiles")!).Contains(path), "Retired cache stayed in the cleanup registry.");
            }
            else if (scenario == "viewer-seeks")
            {
                Require(mpv.GetPropertyDouble("time-pos") >= 14, "Prefetch completion undid the viewer's newer seek.");
                Require(observations.LastOrDefault() == "0", "The current selection was not published.");
            }
            else
            {
                Require(mpv.IsPaused == pauseBeforeCompletion && mpv.IsPaused == !startPaused, "Prefetch completion undid the viewer's newer pause/resume input.");
                Require(observations.LastOrDefault() == "0", "The current selection was not published.");
            }
        }
        finally
        {
            subtitles.Release.TrySetResult(); subtitles.MediaRelease.TrySetResult(); wire.SettingsRelease.TrySetResult();
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout"); Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= Observe;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static FileVersion Version() => new()
    {
        FileId = 42, Duration = 60, EffectiveAudioTrackIndex = 0,
        AudioTracks = [new() { Language = "eng", Default = true }],
        SubtitleTracks = [new() { Index = 0, Language = "eng", Codec = "srt", External = true }, new() { Index = 1, Language = "spa", Codec = "srt", External = true }],
    };
    private static void WriteWave(string path)
    {
        using var writer = new BinaryWriter(File.Create(path)); const int samples = 60 * 8000;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native fixture did not settle."); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private sealed class HeldSubtitleServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly bool oversized;
        public string Origin { get; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MediaEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MediaRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] ReplacementMedia { get; set; } = [];
        public int MediaRequests;
        public int BusyResponses, EngRequests, SyncRequests;
        public int BusyAfter = int.MaxValue;
        public bool BusyChangedFile;
        public string? ReadFailure;
        public HeldSubtitleServer(bool oversized)
        {
            this.oversized = oversized;
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; listener.Prefixes.Add(Origin + "/"); listener.Start(); _ = Task.Run(ListenAsync);
        }
        private async Task ListenAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext request;
                try { request = await listener.GetContextAsync(); } catch (Exception) when (!listener.IsListening) { break; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (request.Request.Url!.AbsolutePath is "/replacement.wav" or "/stream/direct/replacement.wav")
                        {
                            Interlocked.Increment(ref MediaRequests);
                            MediaEntered.TrySetResult(); await MediaRelease.Task;
                            var from = 0; var to = ReplacementMedia.Length - 1;
                            request.Response.ContentType = "audio/wav"; request.Response.Headers["Accept-Ranges"] = "bytes";
                            if (request.Request.Headers["Range"] is string range)
                            {
                                var pair = range[6..].Split('-'); from = int.Parse(pair[0]);
                                if (pair.Length > 1 && int.TryParse(pair[1], out var end)) to = Math.Min(to, end);
                                if (from >= ReplacementMedia.Length) { request.Response.StatusCode = 416; request.Response.Close(); return; }
                                request.Response.StatusCode = 206;
                                request.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{ReplacementMedia.Length}";
                            }
                            request.Response.ContentLength64 = to - from + 1;
                            if (request.Request.HttpMethod != "HEAD") await request.Response.OutputStream.WriteAsync(ReplacementMedia.AsMemory(from, to - from + 1));
                            request.Response.Close(); return;
                        }
                        if (request.Request.Url!.AbsolutePath is "/eng.srt" or "/changed-eng.srt" or "/api/v2/stream/fixture/subtitles/2")
                        {
                            var count = Interlocked.Increment(ref EngRequests);
                            if (request.Request.UserAgent?.Contains("mpv", StringComparison.OrdinalIgnoreCase) == true || request.Request.UserAgent?.Contains("Lavf", StringComparison.OrdinalIgnoreCase) == true) Interlocked.Increment(ref SyncRequests);
                            Entered.TrySetResult(); await Release.Task;
                            if (count <= BusyResponses || count > BusyAfter || BusyChangedFile && request.Request.Url.AbsolutePath == "/changed-eng.srt") { request.Response.StatusCode = 503; request.Response.Headers["Retry-After"] = "1"; request.Response.Close(); return; }
                            if (ReadFailure is "not-found" or "not-found-automatic") { request.Response.StatusCode = 404; request.Response.Close(); return; }
                            if (ReadFailure == "timeout") await Task.Delay(TimeSpan.FromSeconds(10));
                            if (ReadFailure is "oversized-declared" or "oversized-chunked")
                            {
                                request.Response.ContentType = "application/x-subrip";
                                if (ReadFailure == "oversized-chunked") request.Response.SendChunked = true;
                                else request.Response.ContentLength64 = SubtitleSidecarFile.MaxBytes + 1L;
                                var chunk = new byte[64 * 1024];
                                for (var i = 0; i < 256; i++) await request.Response.OutputStream.WriteAsync(chunk);
                                await request.Response.OutputStream.WriteAsync(new byte[1]);
                                request.Response.Close(); return;
                            }
                        }
                        if (oversized)
                        {
                            request.Response.ContentType = "application/x-subrip"; request.Response.SendChunked = true;
                            var chunk = new byte[64 * 1024];
                            for (var i = 0; i < 257; i++) await request.Response.OutputStream.WriteAsync(chunk);
                            request.Response.Close(); return;
                        }
                        var bytes = Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nNative ownership fixture\n");
                        request.Response.ContentType = "application/x-subrip"; request.Response.ContentLength64 = bytes.Length;
                        await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                    }
                    catch (Exception ex) when (!listener.IsListening || ex is IOException or HttpListenerException) { }
                });
            }
        }
        public void Dispose() { Release.TrySetResult(); MediaRelease.TrySetResult(); listener.Close(); }
    }
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        public bool HoldSubtitleList;
        public bool IncludeRetainedDownloaded;
        public TaskCompletionSource SubtitleListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SubtitleListRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PreferenceWrites;
        public ConcurrentQueue<int> RequestedPreferences = new();
        public bool HoldSettings;
        public bool SupportBurnInReplan;
        public bool StartsBurned;
        public bool ChangeFileOnReplan;
        public int Replans;
        public Func<int>? SidecarReadCount;
        public List<int> ReadsAtReplan = [];
        public List<int> ReplanSelections = [];
        public bool HoldNextReplan;
        public TaskCompletionSource ReplanEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReplanRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int selectedSubtitle = -1;
        public TaskCompletionSource SettingsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SettingsRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri is not { } uri || uri.Host != "sidecar-ownership.invalid" && uri.GetLeftPart(UriPartial.Authority) != origin)
                throw new InvalidOperationException("Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/subtitles/42")
            {
                if (HoldSubtitleList) { SubtitleListEntered.TrySetResult(); await SubtitleListRelease.Task.WaitAsync(ct); }
                return Json(IncludeRetainedDownloaded
                    ? """{"subtitles":[{"id":73,"media_file_id":42,"language":"eng","format":"srt","source":"downloaded"},{"id":88,"media_file_id":42,"language":"spa","format":"srt","source":"downloaded"}]}"""
                    : """{"subtitles":[{"id":73,"media_file_id":42,"language":"eng","format":"srt","source":"downloaded"}]}""");
            }
            if (path.Contains("-prefs/", StringComparison.Ordinal))
            {
                using var preference = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                RequestedPreferences.Enqueue(preference.RootElement.GetProperty("subtitle_track_index").GetInt32());
                Interlocked.Increment(ref PreferenceWrites); return Json("{}");
            }
            if (path.Contains("/settings/", StringComparison.Ordinal))
            {
                if (HoldSettings) { SettingsEntered.TrySetResult(); await SettingsRelease.Task.WaitAsync(ct); }
                return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (SupportBurnInReplan && path.EndsWith("/replan"))
            {
                if (SidecarReadCount != null) ReadsAtReplan.Add(SidecarReadCount());
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                selectedSubtitle = document.RootElement.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selected) &&
                    selected.ValueKind == JsonValueKind.Object ? selected.GetProperty("index").GetInt32() : -1;
                ReplanSelections.Add(selectedSubtitle);
                if (ChangeFileOnReplan && Replans > 0 && selectedSubtitle >= 7) selectedSubtitle -= 7;
                Replans++;
                if (HoldNextReplan)
                {
                    HoldNextReplan = false; ReplanEntered.TrySetResult(); await ReplanRelease.Task.WaitAsync(ct);
                }
            }
            else if (path.EndsWith("/start")) selectedSubtitle = StartsBurned ? 2 : -1;
            else return Json("""{"outcome":"applied"}""");
            var file = ChangeFileOnReplan && Replans > 0 ? 43 : 42;
            var offset = file == 43 ? 7 : 0;
            var appliedSubtitle = selectedSubtitle < 0 ? -1 : selectedSubtitle + offset;
            var inventory = Version().SubtitleTracks!.Select(t => (object)new { track_id = $"sub:{file}:{t.Index + offset}", combined_index = t.Index + offset, language = t.Language, codec = t.Codec, source = "external", url = origin + $"/{(file == 43 ? "changed-" : "")}{t.Language}.srt", label = t.Language }).ToList();
            if (SupportBurnInReplan) inventory.Add(new { track_id = $"sub:{file}:{2 + offset}", combined_index = 2 + offset, language = "eng", codec = "dvd_subtitle", source = "embedded", delivery = "burn_in_only", label = "English (DVD)" });
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = SupportBurnInReplan ? $"plan-{Replans}" : "plan", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = file,
                    delivery = "original_http", stream = new { url = origin + (SupportBurnInReplan ? "/stream/direct/replacement.wav" : "/media.wav") }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = $"file:{file}:audio:0", index = 0 }, subtitle = appliedSubtitle < 0 ? null : new { id = $"sub:{file}:{appliedSubtitle}", index = appliedSubtitle } },
                    subtitle = new { mode = selectedSubtitle == 2 ? "burn_in" : selectedSubtitle < 0 ? "off" : "render", track_id = appliedSubtitle < 0 ? null : $"sub:{file}:{appliedSubtitle}", inventory },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
