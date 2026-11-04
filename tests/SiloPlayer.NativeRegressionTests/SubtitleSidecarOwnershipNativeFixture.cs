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
    internal static async Task RunAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "add-selection-flags", "blocking-add-off", "blocking-add-native", "failed-quality-off", "failed-quality-track", "failed-audio-off", "failed-audio-track", "failed-seek-off", "failed-seek-track", "failed-quality-transport-off", "failed-quality-transport-track", "failed-audio-transport-off", "failed-audio-transport-track", "newer-off", "newer-track", "loaded-bitmap-newer-track", "reload-gap", "manual-reload-gap", "manual-reload-finished", "reload-cleanup", "loaded-file-cleanup", "close", "viewer-pauses", "viewer-resumes", "viewer-seeks", "oversized-chunked", "load-start-retired", "load-start-cached-track", "load-start-off", "loaded-settings-cached-track", "loaded-settings-off", "load-start-retired-bitmap", "burn-load-bitmap-playing", "burn-load-bitmap-paused", "burn-load-off-playing", "burn-load-off-paused", "burn-load-text-playing", "burn-load-text-paused", "load-start-superseded-bitmap-off", "load-start-superseded-bitmap-text", "burn-response-off-playing", "burn-response-text-playing" })
        {
            try
            {
                if (scenario == "add-selection-flags") await CheckAddSelectionFlagsAsync();
                else if (scenario.StartsWith("burn-load-", StringComparison.Ordinal) || scenario.StartsWith("burn-response-", StringComparison.Ordinal)) await CheckBurnInLoadIntentAsync(scenario);
                else if (scenario.StartsWith("load-start-", StringComparison.Ordinal) || scenario.StartsWith("loaded-settings-", StringComparison.Ordinal)) await CheckLoadStartAsync(scenario);
                else await CheckAsync(parent, scenario);
                Program.Log("PASS: sidecar ownership " + scenario);
            }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: " + scenario + ": " + ex); }
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task CheckAddSelectionFlagsAsync()
    {
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        var media = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity", "tracks.mkv"));
        mpv.LoadFile(media); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        mpv.SetProperty("pause", "yes");
        var nativeSid = mpv.GetEmbeddedSubtitleTracks().First().Id;
        var sidecar = Path.Combine(Program.ResultDirectory, "add-selection-flags.srt");
        await File.WriteAllTextAsync(sidecar, "1\n00:00:00,000 --> 00:00:10,000\nSelection flag regression\n");
        foreach (var previousSid in new[] { 0, nativeSid })
        foreach (var title in new string?[] { null, "Selection flag regression" })
        foreach (var select in new[] { false, true })
        {
            mpv.SetSubtitleTrack(previousSid);
            var previousIds = mpv.GetSubtitleTrackIds().ToHashSet();
            Require(mpv.AddSubtitle(sidecar, title, "eng", select), "The selection flag fixture could not add its sidecar.");
            var addedIds = mpv.GetSubtitleTrackIds(externalOnly: true).Where(id => !previousIds.Contains(id)).ToArray();
            Require(addedIds.Length == 1, "The selection flag fixture did not publish exactly one sidecar.");
            Require(mpv.GetPropertyDouble("sid") == (select ? addedIds[0] : previousSid),
                $"sub-add select={select}, title={title ?? "<none>"} did not preserve the requested selection from SID {previousSid}.");
            mpv.RemoveSubtitle(addedIds[0]);
        }
        Program.Log("PASS: sub-add explicitly selects or preserves Off/native SID with and without a title.");
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
                await service.SetSubtitleTrackAndPersistAsync(selected + 1, track?.Language, track);
                wire.ReplanRelease.TrySetResult();
                await queuedChoice.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else await service.SetSubtitleTrackAndPersistAsync(selected + 1, track?.Language, track).WaitAsync(TimeSpan.FromSeconds(5));
            Require(!loaded.Task.IsCompleted && service.IsLoading && wire.Replans > 0,
                "Fixture did not replan while replacement media stayed held.");
            await Until(() => Volatile.Read(ref subtitles.MediaRequests) >= 2);
            var requestedPause = (bool?)Get(service, "_restorePausedAfterLoad");
            subtitles.MediaRelease.TrySetResult();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generationBeforeLoad &&
                Get(service, "_loadedMediaInitCts") == null);
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
            var retired = scenario == "load-start-retired";
            var supersededBitmap = scenario.StartsWith("load-start-superseded-bitmap-", StringComparison.Ordinal);
            var retiredBitmap = scenario == "load-start-retired-bitmap" || supersededBitmap;
            var afterFileLoaded = scenario.StartsWith("loaded-settings-", StringComparison.Ordinal);
            var selectOff = scenario.EndsWith("-off", StringComparison.Ordinal) || retiredBitmap && !supersededBitmap;
            if (retiredBitmap)
            {
                // Use the active inventory row when the burn-in server already
                // supplied it. A detached lookalike is correctly refused before
                // reaching the restart gate and cannot establish this race.
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
                var initialMap = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(!service.IsLoading && Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                    service.ActiveSubtitleServerIndex == 1 && initialMap.TryGetValue(1, out var initialSid) && mpv.GetPropertyDouble("sid") == initialSid,
                    "Subtitles must execute before optional settings reads; fixture missed that loaded/settings-await interval.");
                await Task.Delay(100); seen.Clear();
            }
            if (retiredBitmap)
            {
                if (supersededBitmap)
                {
                    subtitles.Release.TrySetResult();
                    var newer = selectOff ? null : manager.CurrentSession!.SubtitleUrls.Single(track => track.Index == 0);
                    await service.SetSubtitleTrackAndPersistAsync(selectOff ? 0 : 1, newer?.Language, newer);
                }
                heldRestart!.Release(); heldRestart = null;
                await pending!.WaitAsync(TimeSpan.FromSeconds(4));
                await Task.Delay(100);
                pending = supersededBitmap ? Task.CompletedTask : (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
            }
            else if (retired) subtitles.Release.TrySetResult();
            else if (!selectOff)
            {
                var english = manager.CurrentSession!.SubtitleUrls!.Single(track => track.Index == 0);
                pending = Task.Run(() => service.SetSubtitleTrackAndPersistAsync(1, english.Language, english));
            }
            else pending = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!);
            await Task.WhenAny(pending!, Task.Delay(400));
            if (wire.Replans > 0) await Until(() => Volatile.Read(ref subtitles.MediaRequests) >= 2);
            var earlyRegistered = retired && ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count != 0;
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
                wire.RequestedPreferences.LastOrDefault() == selected &&
                (!retiredBitmap || !wire.RequestedPreferences.Contains(2)),
                "The current viewer preference was lost or overwritten, or an unexecuted retired bitmap choice was persisted.");
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
        var blockingAdd = scenario.StartsWith("blocking-add-", StringComparison.Ordinal);
        var failedSwitch = scenario.StartsWith("failed-", StringComparison.Ordinal);
        using var subtitles = new HeldSubtitleServer(scenario == "oversized-chunked");
        using var wire = new Wire(subtitles.Origin) { SupportBurnInReplan = failedSwitch }; using var http = new HttpClient(wire);
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
        var nativeMedia = blockingAdd ? Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity", "tracks.mkv")) : wave;
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", nativeMedia));
        mpv.LoadFile(nativeMedia); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        if (blockingAdd)
        {
            // Unknown extension deliberately bypasses prefetch and holds the
            // synchronous remote sub-add, not the earlier managed download.
            var remote = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
            remote.Url = subtitles.Origin + "/eng.raw";
            remote.Codec = null;
            manager.CurrentSession.SubtitleUrls.Add(new() { Index = 3, Source = "embedded", Codec = "subrip", Language = "ja", StreamIndex = 2, ContainerTrackId = "3" });
        }
        var startPaused = scenario == "viewer-resumes" || failedSwitch;
        service.SetPaused(startPaused); await Until(() => mpv.IsPaused == startPaused);
        // Prime the same-file lifecycle before testing retirement at load start.
        Call(service, "SendSubtitleListToOsc");
        var seen = new ConcurrentQueue<string>();
        var notices = new ConcurrentQueue<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(string[] args)
        {
            if (args.FirstOrDefault() == "silo-sidecar-probe-ready") ready.TrySetResult();
            if (args.FirstOrDefault() == "silo-sidecar-probe-selection" && args.Length > 1) seen.Enqueue(args[1]);
            if (args.FirstOrDefault() == "silo-sidecar-probe-notice" && args.Length > 1) notices.Enqueue(args[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        var script = Path.Combine(Program.ResultDirectory, scenario + "-observe.lua");
        await File.WriteAllTextAsync(script, """
            local mp = require 'mp'
            mp.register_script_message('osc-set-active-subtitle', function(index)
                mp.commandv('script-message', 'silo-sidecar-probe-selection', index)
            end)
            mp.register_script_message('osc-show-notice', function(payload)
                mp.commandv('script-message', 'silo-sidecar-probe-notice', payload)
            end)
            mp.register_script_message('silo-sidecar-probe-drain', function()
                mp.commandv('script-message', 'silo-sidecar-probe-ready')
            end)
            mp.commandv('script-message', 'silo-sidecar-probe-ready')
            """);
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mpv, [new[] { "load-script", script }]);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        var replacementLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ReplacementLoaded() => replacementLoaded.TrySetResult();
        try
        {
            if (failedSwitch)
            {
                subtitles.Release.TrySetResult();
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
                var oldSid = mpv.GetPropertyDouble("sid");
                await Until(() => seen.Contains("1")); seen.Clear(); notices.Clear();
                var preferencesBefore = wire.PreferenceWrites;
                var survivingSession = manager.CurrentSession;
                var failedTransport = scenario.Contains("-transport-", StringComparison.Ordinal);
                wire.HoldNextReplan = true; wire.FailNextReplan = !failedTransport;
                Task switching;
                if (scenario.StartsWith("failed-seek-", StringComparison.Ordinal))
                {
                    Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("remux", subtitles.Origin + "/media.wav"));
                    var seekOwner = new CancellationTokenSource();
                    Set(service, "_seekRestartCts", seekOwner);
                    switching = (Task)Call(service, "RestartTransportForSeekAsync", 12d, false,
                        Get(service, "_seekRestartGeneration"), seekOwner)!;
                }
                else switching = scenario.StartsWith("failed-quality-", StringComparison.Ordinal)
                    ? (Task)Call(service, "SwitchQualityTierAsync", "720p", null, null)!
                    : service.SwitchAudioTrackAsync(0);
                await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var selected = scenario.EndsWith("-off", StringComparison.Ordinal) ? -1 : 0;
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!;
                Require(service.IsLoading && mpv.GetPropertyDouble("sid") == oldSid && wire.PreferenceWrites == preferencesBefore && seen.IsEmpty,
                    "The failed-replan fixture did not defer the subtitle choice on the surviving stream.");
                // The accepted response still has the same file, but no version
                // is available for preparing its transport after adoption.
                if (failedTransport) Property(service, "Versions", new List<FileVersion>());
                wire.ReplanRelease.TrySetResult();
                await switching.WaitAsync(TimeSpan.FromSeconds(6));
                if (failedTransport)
                {
                    await Until(() => seen.LastOrDefault() == "1" &&
                        notices.Any(payload => payload.Contains("Subtitle unavailable", StringComparison.Ordinal)));
                    Require(!ReferenceEquals(manager.CurrentSession, survivingSession) &&
                        !service.IsLoading && service.ActiveSubtitleServerIndex == 1 && manager.SubtitleSelectionForLoad == 1 &&
                        mpv.GetPropertyDouble("sid") == oldSid && mpv.IsPaused &&
                        Get(service, "_pendingSubtitlePreferenceIndex") == null && Get(service, "_pendingSubtitlePreferenceOwner") == null &&
                        Get(service, "_pendingInitialServerSubtitleIndex") == null && wire.PreferenceWrites == preferencesBefore && subtitles.MediaRequests == 0,
                        "Transport preparation failure stranded or persisted an unexecuted subtitle instead of rolling it back with feedback.");
                    return;
                }
                if (scenario.StartsWith("failed-seek-", StringComparison.Ordinal))
                    Require(wire.LastReplanOperation == "seek_reanchor" &&
                        ((SemaphoreSlim)Get(service, "_transportRestartGate")!).CurrentCount == 1 && Get(service, "_seekRestartCts") == null,
                        "Seek reconciliation did not retire its restart owner and release the gate.");
                await Until(() => seen.LastOrDefault() == selected.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(!service.IsLoading && service.ActiveSubtitleServerIndex == selected && manager.SubtitleSelectionForLoad == selected &&
                    (selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid) &&
                    Get(service, "_pendingSubtitlePreferenceIndex") == null && Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                    wire.PreferenceWrites == preferencesBefore + 1 && wire.RequestedPreferences.Last() == selected && mpv.IsPaused,
                    "A rejected replan stranded the deferred subtitle/Off choice or saved it before native execution.");
                return;
            }
            if (scenario == "loaded-bitmap-newer-track")
            {
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
                await Until(() => seen.Contains("1")); seen.Clear();
                var bitmap = new SubtitleTrackInfo { Index = 2, Language = "eng", Codec = "dvdsub", Source = "embedded" };
                manager.CurrentSession.SubtitleUrls.Add(bitmap);
                var gate = (SemaphoreSlim)Get(service, "_transportRestartGate")!;
                await gate.WaitAsync();
                var gateHeld = true;
                Task? stale = null, latest = null;
                try
                {
                    var restartGeneration = (long)Get(service, "_seekRestartGeneration")!;
                    stale = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", 2, false)!);
                    await Until(() => (long)Get(service, "_seekRestartGeneration")! > restartGeneration);
                    var english = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == 0);
                    latest = Task.Run(() => service.SetSubtitleTrackAndPersistAsync(1, english.Language, english));
                    await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(!latest.IsCompleted && service.ActiveSubtitleServerIndex == 0,
                        "Fixture did not hold the newer viewer choice in prefetch.");
                    gate.Release(); gateHeld = false;
                    await stale.WaitAsync(TimeSpan.FromSeconds(4));
                    ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(mpv, [new[] { "script-message", "silo-sidecar-probe-drain" }]);
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(service.ActiveSubtitleServerIndex == 0 && manager.SubtitleSelectionForLoad == 0 &&
                        (int?)Get(service, "_pendingSubtitlePreferenceIndex") == 0 && wire.PreferenceWrites == 1 &&
                        seen.IsEmpty && notices.IsEmpty,
                        "A superseded persist:false bitmap worker rolled back or published over the newer pending choice.");
                    subtitles.Release.TrySetResult();
                    await latest.WaitAsync(TimeSpan.FromSeconds(5));
                    var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    Require(service.ActiveSubtitleServerIndex == 0 && manager.SubtitleSelectionForLoad == 0 &&
                        map.TryGetValue(0, out var sid) && mpv.GetPropertyDouble("sid") == sid &&
                        wire.PreferenceWrites == 2 && wire.RequestedPreferences.LastOrDefault() == 0,
                        "The newer choice did not execute and persist after the stale load-time bitmap worker retired.");
                }
                finally
                {
                    if (gateHeld) gate.Release();
                    subtitles.Release.TrySetResult();
                    if (stale != null) try { await stale.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                    if (latest != null) try { await latest.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                }
                return;
            }
            if (scenario == "manual-reload-finished")
            {
                subtitles.HoldOnlyFirstSubtitle = true;
                var stale = (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                try
                {
                    await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    var generationBeforeLoad = (long)Get(service, "_subtitleSidecarGeneration")!;
                    subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave);
                    var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(),
                        subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
                    await prepare;
                    var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                    Call(service, "ApplyPreparedTransport", prepared);
                    service.ContentLoaded += ReplacementLoaded;
                    Call(service, "WireMpvEvents");
                    Call(service, "BeginMpvLoad", prepared, startPaused);
                    subtitles.MediaRelease.TrySetResult();
                    await replacementLoaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                    await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generationBeforeLoad &&
                        Get(service, "_loadedMediaInitCts") == null);
                    Require(!stale.IsCompleted && wire.PreferenceWrites == 1,
                        "Fixture did not finish the owning load before the original prefetch completed.");
                    subtitles.Release.TrySetResult();
                    await stale.WaitAsync(TimeSpan.FromSeconds(5));
                    var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    Require(Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                        Get(service, "_pendingSubtitlePreferenceIndex") == null && wire.PreferenceWrites == 1 &&
                        service.ActiveSubtitleServerIndex == 0 && map.TryGetValue(0, out var sid) && mpv.GetPropertyDouble("sid") == sid,
                        "A late prefetch refilled an already-consumed selection or preference slot.");
                }
                finally
                {
                    subtitles.Release.TrySetResult();
                    await stale.WaitAsync(TimeSpan.FromSeconds(5));
                }
                return;
            }
            var persist = scenario != "reload-gap" && scenario != "reload-cleanup";
            Task pending;
            if (scenario == "reload-gap")
            {
                // Run the complete first FILE_LOADED initializer, not merely
                // its subtitle helper, before the replacement load cancels it.
                var owner = new CancellationTokenSource();
                Set(service, "_loadedMediaInitCts", owner);
                Set(service, "_pendingInitialServerSubtitleIndex", 0);
                Set(service, "_pendingInitialSubtitleFileId", manager.CurrentSession!.MediaFileId);
                manager.RecordLocalSubtitleSelection(0);
                pending = (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
            }
            else if (scenario == "oversized-chunked") pending = (Task)Call(service, "PrefetchSubtitleSidecarAsync", manager.CurrentSession!.SubtitleUrls![0])!;
            else if (blockingAdd) pending = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, persist)!);
            else pending = (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, persist)!;
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (scenario == "reload-gap")
            {
                // The initial list legitimately highlights its pending choice.
                // Drain it before testing publication by the retired owner.
                await Until(() => seen.Contains("0")); seen.Clear();
            }
            var uiTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Require(parent.DispatcherQueue.TryEnqueue(() => uiTick.TrySetResult()), "UI queue rejected fixture callback.");
            await uiTick.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var generation = (long)Get(service, "_subtitleSidecarGeneration")!;
            if (blockingAdd || scenario is "newer-off" or "newer-track")
            {
                var selected = scenario is "newer-off" or "blocking-add-off" ? -1 : blockingAdd ? 3 : 1;
                await ((Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!).WaitAsync(TimeSpan.FromSeconds(4));
                await Until(() => seen.Contains(selected.ToString()));
                if (blockingAdd)
                    Require(!pending.IsCompleted && service.ActiveSubtitleServerIndex == selected &&
                        (selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : mpv.GetPropertyDouble("sid") == 2),
                        "Fixture did not establish a newer Off/native choice while the synchronous sub-add was blocked.");
            }
            else if (scenario is "reload-gap" or "manual-reload-gap")
            {
                // The product intentionally selects subtitles before settings.
                // Hold a real replacement load before FILE_LOADED instead of
                // relying on the obsolete settings-before-LoadSubtitles order.
                subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave);
                var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(),
                    subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
                await prepare;
                var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                Call(service, "ApplyPreparedTransport", prepared);
                service.ContentLoaded += ReplacementLoaded;
                Call(service, "WireMpvEvents");
                Call(service, "BeginMpvLoad", prepared, startPaused);
                await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(service.IsLoading && !replacementLoaded.Task.IsCompleted &&
                    (long)Get(service, "_subtitleSidecarGeneration")! == generation,
                    "Fixture did not establish the real load-start cancellation gap before FILE_LOADED.");
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
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200); // Drain real libmpv client/Lua message delivery.
            var observations = seen.ToArray();
            if (blockingAdd || scenario is "newer-off" or "newer-track")
            {
                var selected = scenario is "newer-off" or "blocking-add-off" ? -1 : blockingAdd ? 3 : 1;
                Require(service.ActiveSubtitleServerIndex == selected, "The superseded sidecar changed the server selection.");
                Require(observations.LastOrDefault() == selected.ToString(),
                    "Superseded A published stale OSC selection: " + string.Join(",", observations));
                var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : blockingAdd ? mpv.GetPropertyDouble("sid") == 2 :
                    map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid,
                    "Native SID differs from the latest viewer selection.");
                Require(manager.SubtitleSelectionForLoad == selected && wire.PreferenceWrites == 1 &&
                    wire.RequestedPreferences.LastOrDefault() == selected,
                    "Superseded A changed the latest intent or persisted a stale subtitle preference.");
                if (blockingAdd)
                    Require(!map.ContainsKey(0) && mpv.GetSubtitleTrackIds(externalOnly: true).Count == 0,
                        "A retired blocking sub-add left its automatically selected sidecar in the native inventory.");
            }
            else if (scenario is "reload-gap" or "manual-reload-gap" or "reload-cleanup" or "close")
            {
                Require(!observations.Contains("0"), "A retired file initializer published an OSC selection: " + string.Join(",", observations));
                Require(((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).Count == 0, "A retired file initializer added a native subtitle.");
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0, "A retired prefetch registered a temporary file after cleanup.");
                Require(wire.PreferenceWrites == 0,
                    "A retired selection persisted before the replacement could execute it.");
                if (scenario is "reload-gap" or "manual-reload-gap")
                {
                    subtitles.MediaRelease.TrySetResult();
                    await replacementLoaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                    await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generation &&
                        Get(service, "_loadedMediaInitCts") == null);
                    var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    Require(service.ActiveSubtitleServerIndex == 0 && manager.SubtitleSelectionForLoad == 0 &&
                        map.TryGetValue(0, out var sid) && mpv.GetPropertyDouble("sid") == sid &&
                        wire.PreferenceWrites == (scenario == "manual-reload-gap" ? 1 : 0),
                        "The real replacement did not execute the retained choice and persist only the current manual preference.");
                }
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
            subtitles.Release.TrySetResult(); subtitles.MediaRelease.TrySetResult(); wire.SettingsRelease.TrySetResult(); wire.ReplanRelease.TrySetResult();
            service.ContentLoaded -= ReplacementLoaded;
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
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
        public bool HoldOnlyFirstSubtitle;
        private int _englishRequests;
        public HeldSubtitleServer(bool oversized)
        {
            this.oversized = oversized;
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; listener.Prefixes.Add(Origin + "/"); listener.Start(); _ = ListenAsync();
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
                        if ((request.Request.Url!.AbsolutePath is "/eng.srt" or "/eng.raw") &&
                            (Interlocked.Increment(ref _englishRequests) == 1 || !HoldOnlyFirstSubtitle))
                        {
                            Entered.TrySetResult(); await Release.Task;
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
        public int PreferenceWrites;
        public ConcurrentQueue<int> RequestedPreferences = new();
        public bool HoldSettings;
        public bool SupportBurnInReplan;
        public bool StartsBurned;
        public int Replans;
        public bool HoldNextReplan;
        public bool FailNextReplan;
        public string? LastReplanOperation;
        public TaskCompletionSource ReplanEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReplanRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int selectedSubtitle = -1;
        public TaskCompletionSource SettingsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SettingsRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "sidecar-ownership.invalid") throw new InvalidOperationException("Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
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
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                LastReplanOperation = document.RootElement.GetProperty("operation").GetString();
                selectedSubtitle = document.RootElement.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selected) &&
                    selected.ValueKind == JsonValueKind.Object ? selected.GetProperty("index").GetInt32() : -1;
                Replans++;
                if (HoldNextReplan)
                {
                    HoldNextReplan = false; ReplanEntered.TrySetResult(); await ReplanRelease.Task.WaitAsync(ct);
                }
                if (FailNextReplan)
                {
                    FailNextReplan = false;
                    throw new InvalidOperationException("Synthetic replan rejection before adoption.");
                }
            }
            else if (path.EndsWith("/start")) selectedSubtitle = StartsBurned ? 2 : -1;
            else return Json("""{"outcome":"applied"}""");
            var inventory = Version().SubtitleTracks!.Select(t => (object)new { track_id = $"sub:42:{t.Index}", combined_index = t.Index, language = t.Language, codec = t.Codec, source = "external", url = origin + $"/{t.Language}.srt", label = t.Language }).ToList();
            if (SupportBurnInReplan) inventory.Add(new { track_id = "sub:42:2", combined_index = 2, language = "eng", codec = "dvd_subtitle", source = "embedded", delivery = "burn_in_only", label = "English (DVD)" });
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = SupportBurnInReplan ? $"plan-{Replans}" : "plan", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = 42,
                    delivery = "original_http", stream = new { url = origin + (SupportBurnInReplan ? "/stream/direct/replacement.wav" : "/media.wav") }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 }, subtitle = selectedSubtitle < 0 ? null : new { id = $"sub:42:{selectedSubtitle}", index = selectedSubtitle } },
                    subtitle = new { mode = selectedSubtitle == 2 ? "burn_in" : selectedSubtitle < 0 ? "off" : "render", track_id = selectedSubtitle < 0 ? null : $"sub:42:{selectedSubtitle}", inventory },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
