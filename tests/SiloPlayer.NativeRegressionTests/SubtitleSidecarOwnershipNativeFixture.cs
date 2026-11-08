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
        await CheckVersionReorderedPickAsync();
        Program.Log("PASS: a pick queued during a version switch is remapped and saved exactly once.");
        await CheckBusyAsync(parent, "missing-native-503");
        Program.Log("PASS: missing native identity uses bounded async503 sidecar without remote sub-add.");
    }
    internal static async Task RunAsync(StackPanel parent)
    {
        var only = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_OWNERSHIP_CASE") ?? "";
        if (only.Length > 0)
        {
            if (only is not ("applied-newer-busy" or "applied-newer-applied" or "applied-failed-retry" or "applied-raw-labels"))
                throw new InvalidOperationException("Unknown applied preference scope.");
            await CheckAppliedPreferenceAsync(only["applied-".Length..]);
            Program.Log("PASS: applied subtitle preference " + only);
            return;
        }
        var failures = new List<string>();
        foreach (var scenario in new[] { "legacy-direct-reopen", "budget-large-pgs-manual", "budget-large-pgs-loaded", "budget-slow-manual", "budget-slow-loaded", "dispatcher-pgs", "dispatcher-failed-text", "refresh-manual", "refresh-post-load", "refresh-reindex-manual", "refresh-reindex-post-load", "refresh-removed-manual", "refresh-removed-post-load", "add-selection-flags", "blocking-add-off", "blocking-add-native", "failed-quality-off", "failed-quality-track", "failed-audio-off", "failed-audio-track", "failed-seek-off", "failed-seek-track", "failed-quality-transport-off", "failed-quality-transport-track", "failed-audio-transport-off", "failed-audio-transport-track", "newer-off", "newer-track", "loaded-bitmap-newer-track", "reload-gap", "manual-reload-gap", "manual-reload-finished", "reload-cleanup", "loaded-file-cleanup", "close", "viewer-pauses", "viewer-resumes", "viewer-seeks", "oversized-chunked", "load-start-retired", "load-start-retired-with-prior", "load-start-retired-newer-off", "load-start-cached-track", "load-start-off", "loaded-settings-cached-track", "loaded-settings-off", "load-start-retired-bitmap", "burn-load-bitmap-playing", "burn-load-bitmap-paused", "burn-load-off-playing", "burn-load-off-paused", "burn-load-text-playing", "burn-load-text-paused", "load-start-superseded-bitmap-off", "load-start-superseded-bitmap-text", "burn-response-off-playing", "burn-response-text-playing", "busy-reload-newer-pick", "initializer-timing-change" })
        {
            try
            {
                if (scenario == "initializer-timing-change") await CheckInitializerTimingChangeAsync();
                else if (scenario.StartsWith("budget-", StringComparison.Ordinal)) await CheckSidecarBudgetAsync(parent, scenario);
                else if (scenario == "busy-reload-newer-pick") await CheckLoadedSidecarFailureAsync(scenario);
                else if (scenario.StartsWith("dispatcher-", StringComparison.Ordinal)) await CheckDispatcherSidecarAsync(parent, scenario);
                else if (scenario == "add-selection-flags") await CheckAddSelectionFlagsAsync();
                else if (scenario.StartsWith("burn-load-", StringComparison.Ordinal) || scenario.StartsWith("burn-response-", StringComparison.Ordinal)) await CheckBurnInLoadIntentAsync(scenario);
                else if (scenario.StartsWith("load-start-", StringComparison.Ordinal) || scenario.StartsWith("loaded-settings-", StringComparison.Ordinal)) await CheckLoadStartAsync(scenario);
                else await CheckAsync(parent, scenario);
                Program.Log("PASS: sidecar ownership " + scenario);
            }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: " + scenario + ": " + ex); }
        }
        foreach (var scenario in new[] { "quality-failed-pick", "audio-failed-pick", "version-failed-pick", "version-reordered-pick", "seek-failed-pick", "invalidation-noop-pick", "invalidation-noop-read", "inventory-removed", "inventory-unplayable" })
        {
            try
            {
                if (scenario == "version-reordered-pick") await CheckVersionReorderedPickAsync();
                else await CheckSwitchRaceAsync(scenario);
                Program.Log("PASS: subtitle switch race " + scenario);
            }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: " + scenario + ": " + ex); }
        }
        foreach (var scenario in new[] { "newer-busy", "newer-applied", "failed-retry", "raw-labels" })
        {
            try
            {
                await CheckAppliedPreferenceAsync(scenario);
                Program.Log("PASS: applied subtitle preference " + scenario);
            }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: " + scenario + ": " + ex); }
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task CheckAppliedPreferenceAsync(string scenario)
    {
        using var subtitles = new HeldSubtitleServer(false) { BusyResponses = 100 };
        subtitles.Release.TrySetResult();
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "applied-preference-" + scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] }); await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        var wave = Path.Combine(Program.ResultDirectory, "applied-preference-" + scenario + ".wav"); WriteWave(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0); service.SetPaused(true);
        try
        {
            var spanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
            if (scenario == "raw-labels")
            {
                spanish.NativeTitle = null; spanish.Label = "spa";
                await service.SetSubtitleTrackAndPersistAsync(1, spanish.Language, spanish);
                spanish.NativeTitle = "Server title"; spanish.Label = "Display only";
                await service.SetSubtitleTrackAndPersistAsync(1, spanish.Language, spanish);
                manager.CurrentSession.ProtocolVersion = 2;
                spanish.NativeTitle = null; spanish.Label = "Legacy title";
                await service.SetSubtitleTrackAndPersistAsync(1, spanish.Language, spanish);
                Require(wire.RequestedLabels.SequenceEqual(new string?[] { null, "Server title", "Legacy title" }),
                    "Saved signatures must use the raw v3 label (including null), while preserving legacy labels.");
                return;
            }
            wire.FailNextPreference = scenario == "failed-retry";
            wire.HoldNextPreference = scenario != "failed-retry";
            var first = service.SetSubtitleTrackAndPersistAsync(1, spanish.Language, spanish);
            if (scenario == "failed-retry")
            {
                await first.WaitAsync(TimeSpan.FromSeconds(4));
                var choice = Get(service, "_subtitleSelectionOperation")!;
                Require(wire.CommittedPreferences.IsEmpty && service.ActiveSubtitleServerIndex == 1 &&
                    (int)choice.GetType().GetField("PreferenceClaimed")!.GetValue(choice)! == 0,
                    "A failed preference write consumed its retry claim or undid applied captions.");
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, false)!;
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, false)!;
                Require(wire.RequestedPreferences.SequenceEqual(new[] { 1, 1 }) && wire.CommittedPreferences.SequenceEqual(new[] { 1 }),
                    "The failed applied preference must retry successfully once, without duplicate successful saves.");
                return;
            }
            await wire.PreferenceEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var priorSid = mpv.GetPropertyDouble("sid");
            Require(priorSid > 0 && service.ActiveSubtitleServerIndex == 1 && !first.IsCompleted,
                "The held preference must belong to a verified, already applied choice.");
            var newer = (Task)Call(service, "SelectSubtitleByServerIndexAsync", scenario == "newer-busy" ? 0 : -1, true)!;
            if (scenario == "newer-busy")
            {
                await newer.WaitAsync(TimeSpan.FromSeconds(6));
                Require(service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") == priorSid && !first.IsCompleted &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 1 }),
                    "A refused newer pick canceled or replaced the applied preference's in-flight save.");
            }
            else
            {
                await Until(() => service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0);
                Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 && !newer.IsCompleted &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 1 }),
                    "A newer executed preference must wait behind the earlier dispatched write.");
            }
            wire.PreferenceRelease.TrySetResult();
            await Task.WhenAll(first, newer).WaitAsync(TimeSpan.FromSeconds(4));
            Require(wire.CommittedPreferences.SequenceEqual(scenario == "newer-busy" ? new[] { 1 } : new[] { 1, -1 }),
                "Applied preference writes were lost or committed out of execution order.");
        }
        finally
        {
            wire.PreferenceRelease.TrySetResult();
            Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    internal static async Task RunBusyAsync(StackPanel parent)
    {
        foreach (var scenario in new[] { "exhausted", "recovered", "recovered-playing", "viewer-pauses", "viewer-resumes", "viewer-seeks", "newer-off", "newer-track", "superseded-exhausted", "aba", "load", "profile", "close", "translation-completed", "timeout", "oversized-declared", "oversized-chunked", "not-found", "not-found-automatic", "no-context-load", "no-context-sync-stop", "oversized-recovered", "fallback-newer-off", "fallback-profile", "fallback-load", "fallback-sync-stop" })
        {
            await CheckBusyAsync(parent, scenario);
            Program.Log("PASS: busy sidecar " + scenario);
        }
        foreach (var scenario in new[] { "busy-external", "busy-missing-native", "busy-extensionless-mov-text", "error", "error-recovered", "extensionless-unknown-codec", "sync-stop", "busy-reload-same-native", "busy-reload-other-native", "busy-reload-other-sidecar", "busy-reload-newer-pick", "error-reload-other-native" })
        {
            await CheckLoadedSidecarFailureAsync(scenario);
            Program.Log("PASS: loaded sidecar " + scenario);
        }
    }

    private static async Task CheckVersionReorderedPickAsync()
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "version-reordered-pick")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var replacement = Version(); replacement.FileId = 43;
        replacement.SubtitleTracks!.Reverse();
        for (var i = 0; i < replacement.SubtitleTracks.Count; i++) replacement.SubtitleTracks[i].Index = i;
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version(), replacement] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, "version-reordered-pick.wav"); WriteWave(wave);
        subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave); subtitles.Release.TrySetResult();
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0); service.SetPaused(true);
        Call(service, "SendSubtitleListToOsc"); Call(service, "WireMpvEvents");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult(); service.ContentLoaded += Loaded;
        try
        {
            var subtitleGeneration = (long)Get(service, "_subtitleSidecarGeneration")!;
            wire.ReorderVersionOnStart = true;
            var switching = service.SwitchVersionAsync(replacement);
            await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(service.IsSwitchingContent && manager.CurrentSession!.MediaFileId == 42,
                "The fixture missed the outgoing version's switch window.");
            await ((Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!).WaitAsync(TimeSpan.FromSeconds(1));
            Require(wire.PreferenceWrites == 0 && Get(service, "_pendingInitialServerSubtitleIndex") is 1,
                "The outgoing Spanish pick did not queue without saving.");
            wire.ReplanRelease.TrySetResult();
            await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(wire.PreferenceWrites == 0, "The queued pick saved before replacement execution.");
            subtitles.MediaRelease.TrySetResult();
            await switching.WaitAsync(TimeSpan.FromSeconds(5));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > subtitleGeneration &&
                !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
            var mapped = manager.CurrentSession!.SubtitleUrls.Single(t => t.Language == "spa");
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            Require(manager.CurrentSession.MediaFileId == 43 && mapped.Index == 0 && service.ActiveSubtitleServerIndex == mapped.Index &&
                map.TryGetValue(mapped.Index, out var sid) && mpv.GetPropertyDouble("sid") == sid && mpv.IsPaused &&
                wire.RequestedPreferences.SequenceEqual(new[] { mapped.Index }),
                "A queued version-switch pick applied the outgoing ordinal or failed to save the mapped Spanish track exactly once.");
            Call(service, "StartLoadedMediaInitialization");
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            Require(service.ActiveSubtitleServerIndex == mapped.Index && wire.RequestedPreferences.SequenceEqual(new[] { mapped.Index }),
                "Repeated initialization duplicated the queued preference or lost the mapped choice.");
        }
        finally
        {
            wire.ReplanRelease.TrySetResult(); subtitles.MediaRelease.TrySetResult(); service.ContentLoaded -= Loaded;
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckSwitchRaceAsync(string scenario)
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] }); await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0); service.SetPaused(true);
        Call(service, "SendSubtitleListToOsc");
        await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
        var priorSid = mpv.GetPropertyDouble("sid");
        Require(priorSid > 0 && wire.RequestedPreferences.SequenceEqual(new[] { 1 }), "Prior choice was not applied and saved.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new ConcurrentQueue<string>();
        void Observe(string[] message)
        {
            if (message.FirstOrDefault() == "silo-switch-race-ready") ready.TrySetResult();
            if (message.FirstOrDefault() == "silo-switch-race-drained" && message.Length > 1) drained.TrySetResult(message[1]);
            if (message.FirstOrDefault() == "silo-switch-race-notice" && message.Length > 1) notices.Enqueue(message[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        var probe = Path.Combine(Program.ResultDirectory, scenario + ".lua");
        await File.WriteAllTextAsync(probe, """
            local mp = require 'mp'
            local active = '0' -- optimistic pending choice
            mp.register_script_message('osc-set-active-subtitle', function(index) active = index end)
            mp.register_script_message('osc-show-notice', function(payload)
                mp.commandv('script-message', 'silo-switch-race-notice', payload)
            end)
            mp.register_script_message('silo-switch-race-barrier', function()
                mp.commandv('script-message', 'silo-switch-race-drained', active)
            end)
            mp.commandv('script-message', 'silo-switch-race-ready')
            """);
        Call(mpv, "Command", (object)new[] { "load-script", probe });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        var gate = (SemaphoreSlim)Get(manager, "_replanGuard")!;
        var gateHeld = false;
        try
        {
            if (scenario.StartsWith("inventory-", StringComparison.Ordinal))
            {
                var pending = (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var replacement = new PlaybackStartResponse
                {
                    ProtocolVersion = 3, SessionId = "replacement", MediaFileId = 42,
                    SubtitleUrls = [manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1)],
                };
                if (scenario == "inventory-unplayable")
                    replacement.SubtitleUrls.Add(new() { Index = 0, TrackId = "sub:42:0", Unplayable = true });
                Property(manager, "CurrentSession", replacement);
                subtitles.Release.TrySetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).ContainsKey(0),
                    "A retired session's read registered bytes in the replacement inventory.");
            }
            else if (scenario == "invalidation-noop-read")
            {
                Property(service, "IsLoading", true);
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                Property(service, "IsLoading", false);
                var owner = new CancellationTokenSource(); Set(service, "_loadedMediaInitCts", owner);
                var pending = (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
                await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                await gate.WaitAsync(); gateHeld = true;
                var invalidation = (Task)Call(service, "HandlePlanInvalidationAsync", new PlaybackPlanInvalidation("obsolete", "fixture"))!;
                Require(service.IsSwitchingContent, "No-op invalidation did not cover the initializer read.");
                subtitles.Release.TrySetResult();
                await Until(() => ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).ContainsKey(0));
                await Task.Delay(150);
                Require(!pending.IsCompleted && wire.RequestedPreferences.SequenceEqual(new[] { 1 }),
                    "The initializer dropped or saved its choice inside the no-op switch window.");
                gate.Release(); gateHeld = false;
                await invalidation.WaitAsync(TimeSpan.FromSeconds(4)); await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Require(service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 1, 0 }), "No-op invalidation stranded the initializer choice.");
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, false)!;
                Require(wire.RequestedPreferences.SequenceEqual(new[] { 1, 0 }), "Initializer retry saved the choice twice.");
                return;
            }
            else
            {
                Task switching;
                if (scenario == "invalidation-noop-pick")
                {
                    await gate.WaitAsync(); gateHeld = true;
                    switching = (Task)Call(service, "HandlePlanInvalidationAsync", new PlaybackPlanInvalidation("obsolete", "fixture"))!;
                }
                else
                {
                    wire.RejectReplan = scenario != "version-failed-pick"; wire.RejectStart = !wire.RejectReplan;
                    if (scenario == "seek-failed-pick")
                    {
                        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("remux", wave));
                        service.SeekTo(15);
                        switching = Task.CompletedTask; // The seek restart owns its asynchronous lifetime.
                    }
                    else
                    {
                        switching = scenario == "quality-failed-pick" ? service.SelectQualityAsync("720p") :
                            scenario == "audio-failed-pick" ? service.SwitchAudioTrackAsync(0) : service.SwitchVersionAsync(Version());
                    }
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                }
                Require(service.IsSwitchingContent, "The fixture missed the transport switch window.");
                await ((Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!).WaitAsync(TimeSpan.FromSeconds(1));
                Require(Get(service, "_pendingInitialServerSubtitleIndex") is 0 && wire.PreferenceWrites == 1,
                    "The pick was not queued without saving during the switch.");
                if (gateHeld) { gate.Release(); gateHeld = false; }
                wire.ReplanRelease.TrySetResult(); await switching.WaitAsync(TimeSpan.FromSeconds(5));
                if (scenario == "seek-failed-pick")
                    await Until(() => Get(service, "_seekRestartCts") == null);
            }
            mpv.SendScriptMessage("silo-switch-race-barrier");
            Require(await drained.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "1" && notices.Any(n => n.Contains("Subtitle unavailable", StringComparison.Ordinal)),
                "Refusal failed to restore the applied OSC choice with feedback.");
            Require(service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") == priorSid &&
                manager.SubtitleSelectionForLoad == 1 && Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                wire.RequestedPreferences.SequenceEqual(new[] { 1 }), "A refused pick changed applied captions, manager intent, or preferences, or survived for a later load.");
            var choice = Get(service, "_subtitleSelectionOperation")!;
            Require(!(bool)choice.GetType().GetProperty("Accepted")!.GetValue(choice)!, "The refused choice retained save authority.");
            await (Task)Call(service, "SelectSubtitleByServerIndexAsync", manager.SubtitleSelectionForLoad, false)!;
            Require(service.ActiveSubtitleServerIndex == 1 && wire.RequestedPreferences.SequenceEqual(new[] { 1 }),
                "A later automatic selection revived the refused pick's preference.");
        }
        finally
        {
            if (gateHeld) gate.Release();
            subtitles.Release.TrySetResult(); wire.ReplanRelease.TrySetResult();
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= Observe; Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckInitializerTimingChangeAsync()
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "initializer-timing-change")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] }); await manager.StartSessionAsync(42);
        var track = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
        var key = "external-" + new string('b', 64);
        manager.CurrentSession.SubtitleUrls.Single(t => t.Index == 1).SyncKey = key;
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        var wave = Path.Combine(Program.ResultDirectory, "initializer-timing-change.wav"); WriteWave(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(wave);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(4)); mpv.Pause();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new ConcurrentQueue<string>();
        void Observe(string[] message)
        {
            if (message.FirstOrDefault() == "silo-timing-read-ready") ready.TrySetResult();
            if (message.FirstOrDefault() == "silo-timing-read-drained" && message.Length > 1) drained.TrySetResult(message[1]);
            if (message.FirstOrDefault() == "silo-timing-read-notice" && message.Length > 1) notices.Enqueue(message[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        Task? pending = null;
        try
        {
            var probe = Path.Combine(Program.ResultDirectory, "initializer-timing-change.lua");
            await File.WriteAllTextAsync(probe, """
                local mp = require 'mp'
                local active = '-1'
                mp.register_script_message('osc-set-active-subtitle', function(index) active = index end)
                mp.register_script_message('osc-show-notice', function(payload)
                    mp.commandv('script-message', 'silo-timing-read-notice', payload)
                end)
                mp.register_script_message('silo-timing-read-barrier', function()
                    mp.commandv('script-message', 'silo-timing-read-drained', active)
                end)
                mp.commandv('script-message', 'silo-timing-read-ready')
                """);
            Call(mpv, "Command", (object)new[] { "load-script", probe });
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Call(service, "SendSubtitleListToOsc");
            lock (Get(service, "_subtitleSyncGate")!)
                Call(service, "ApplySubtitleSyncState", new SubtitleSyncState
                {
                    Key = key, MediaFileId = 42, Source = "external", Format = "srt", Timing = new(),
                }, CancellationToken.None);
            Property(service, "IsLoading", true);
            await service.SetSubtitleTrackAndPersistAsync(1, track.Language, track);
            Property(service, "IsLoading", false);
            var choice = Get(service, "_subtitleSelectionOperation")!;
            var owner = new CancellationTokenSource(); Set(service, "_loadedMediaInitCts", owner);
            pending = (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!pending.IsCompleted && mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0,
                "The timing regression did not hold the initializer before subtitle execution.");
            var generation = (long)Get(service, "_subtitleSidecarGeneration")!;
            using var push = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                session_id = manager.SessionId, file_id = 42, sync_key = key,
                timing = new { offset_ms = 75, scale = 1 },
            }));
            // A different viewer's timing update for an inactive key invalidates
            // this file's read generation without changing its manual owner.
            Call(service, "ApplySubtitleSyncEvent", push.RootElement, false);
            Require((long)Get(service, "_subtitleSidecarGeneration")! > generation &&
                ReferenceEquals(choice, Get(service, "_subtitleSelectionOperation")),
                "The timing event failed to invalidate only the current file's cues.");
            await pending.WaitAsync(TimeSpan.FromSeconds(4));
            mpv.SendScriptMessage("silo-timing-read-barrier");
            Require(await drained.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "-1" &&
                service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                service.CaptureSiloCastState()["selectedSubtitleTrackId"] == null && wire.PreferenceWrites == 0 &&
                Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null &&
                !(bool)choice.GetType().GetProperty("Accepted")!.GetValue(choice)! &&
                (int)choice.GetType().GetField("PreferenceClaimed")!.GetValue(choice)! == 0 &&
                ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0 &&
                notices.Any(n => n.Contains("could not be loaded", StringComparison.Ordinal)),
                "Timing retirement stranded an active subtitle/pending preference or failed to offer retry feedback.");
            subtitles.Release.TrySetResult();
            Require(await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!).WaitAsync(TimeSpan.FromSeconds(5)) &&
                service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 &&
                wire.RequestedPreferences.SequenceEqual(new[] { 0 }) && subtitles.EnglishRequests == 2 &&
                !ReferenceEquals(choice, Get(service, "_subtitleSelectionOperation")),
                "An explicit retry after timing retirement did not apply and save its fresh owner exactly once.");
        }
        finally
        {
            subtitles.Release.TrySetResult(); Call(service, "CancelPendingLoadedMediaInitialization");
            if (pending != null) try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Call(service, "StopSubtitleSync"); mpv.ScriptMessageReceived -= Observe;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckLoadedSidecarFailureAsync(string scenario)
    {
        var reloads = scenario.Contains("-reload-", StringComparison.Ordinal);
        var newerPick = scenario == "busy-reload-newer-pick";
        var skipsPrefetch = scenario == "extensionless-unknown-codec";
        var urlFallbackRecovers = scenario == "error-recovered" || skipsPrefetch;
        var expectedRequests = 2;
        using var subtitles = new HeldSubtitleServer(false)
        {
            BusyResponses = scenario.StartsWith("busy-", StringComparison.Ordinal) ? 100 : 0,
            ReadFailure = scenario is "error" or "error-reload-other-native" ? "not-found" : scenario == "error-recovered" ? "fallback-recovered" : null,
            OpaqueEnglish = skipsPrefetch,
        };
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "silo-loaded-sidecar-" + scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] }); await manager.StartSessionAsync(42);
        var track = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
        if (scenario == "busy-missing-native")
        {
            track.Source = "embedded"; track.StreamIndex = 99; track.ContainerTrackId = "99";
        }
        if (scenario == "busy-extensionless-mov-text" || skipsPrefetch)
        {
            // Unknown server metadata must still let mpv probe the actual sidecar bytes.
            track.Codec = skipsPrefetch ? "fixture-unknown" : "mov_text";
            track.Url = subtitles.Origin + "/api/v2/stream/fixture/subtitles/2";
        }
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
        var media = Path.Combine(assets, "tracks.mkv");
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", media));
        mpv.SetProperty("sub-auto", "no"); mpv.SetProperty("sid", "auto"); mpv.SetProperty("subs-fallback", "default");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(media);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.Pause();
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var autoSkipConfigured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new ConcurrentQueue<string>();
        void Observe(string[] message)
        {
            if (message.FirstOrDefault() == "silo-loaded-sidecar-auto-skip") autoSkipConfigured.TrySetResult();
            if (message.Length < 2) return;
            if (message[0] == "silo-loaded-sidecar-ready") ready.TrySetResult(message[1]);
            if (message[0] == "silo-loaded-sidecar-drained") drained.TrySetResult(message[1]);
            if (message[0] == "silo-loaded-sidecar-notice") notices.Enqueue(message[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        try
        {
            var probe = Path.Combine(Program.ResultDirectory, "silo-loaded-sidecar-" + scenario + ".lua");
            await File.WriteAllTextAsync(probe, """
                local mp = require 'mp'
                local active = '-1'
                local intro_mode_ready = false
                mp.register_script_message('osc-set-intro-mode', function() intro_mode_ready = true end)
                mp.register_script_message('osc-set-auto-skip', function()
                    if intro_mode_ready then mp.commandv('script-message', 'silo-loaded-sidecar-auto-skip') end
                end)
                local default_selected = false
                for _, track in ipairs(mp.get_property_native('track-list', {})) do
                    if track.type == 'sub' and track.default and track.id == mp.get_property_native('sid') then
                        default_selected = true
                    end
                end
                mp.register_script_message('osc-set-active-subtitle', function(index) active = index end)
                mp.register_script_message('silo-loaded-sidecar-pending', function() active = '0' end)
                mp.register_script_message('osc-show-notice', function(payload)
                    mp.commandv('script-message', 'silo-loaded-sidecar-notice', payload)
                end)
                mp.register_script_message('silo-loaded-sidecar-barrier', function()
                    mp.commandv('script-message', 'silo-loaded-sidecar-drained', active)
                end)
                mp.commandv('script-message', 'silo-loaded-sidecar-ready', tostring(default_selected))
                """);
            Call(mpv, "Command", (object)new[] { "load-script", probe });
            Require(await ready.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "true" && mpv.GetPropertyDouble("sid") > 0,
                "The load regression must start with an actual auto-selected, default-flagged embedded subtitle.");
            var writesBeforeLoad = wire.PreferenceWrites;
            if (reloads)
            {
                var prior = scenario == "busy-reload-same-native" ? track : manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
                if (scenario is not ("busy-reload-other-sidecar" or "busy-reload-newer-pick"))
                {
                    prior.Source = "embedded"; prior.Codec = "subrip"; prior.Language = "en";
                    prior.StreamIndex = 1; prior.ContainerTrackId = "2";
                }
                await service.SetSubtitleTrackAndPersistAsync(prior.Index + 1, prior.Language, prior);
                Require(service.ActiveSubtitleServerIndex == prior.Index && mpv.GetPropertyDouble("sid") > 0 &&
                    ReferenceEquals(Get(service, "_executedSubtitleTrack"), prior) && wire.PreferenceWrites == writesBeforeLoad + 1,
                    "Reload control did not establish a genuinely applied and saved native/sidecar subtitle.");
                // Hold the applied sidecar's re-read on the same endpoint as
                // the newer pick, without changing its stable track identity.
                if (newerPick) prior.Url = track.Url;
                writesBeforeLoad = wire.PreferenceWrites;
                var replacementLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ReplacementLoaded() => replacementLoaded.TrySetResult();
                mpv.FileLoaded += ReplacementLoaded;
                try { mpv.LoadFile(media); await replacementLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally { mpv.FileLoaded -= ReplacementLoaded; }
                mpv.Pause();
                // The real native file has changed. HLS cannot reuse the old
                // embedded route, even when the effective server file is equal.
                Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("transcode", subtitles.Origin + "/master.m3u8"));
            }
            // Queue a manual choice before entering the real FILE_LOADED worker.
            var initialTrack = newerPick ? manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1) : track;
            Property(service, "IsLoading", true);
            await service.SetSubtitleTrackAndPersistAsync(initialTrack.Index + 1, initialTrack.Language, initialTrack).WaitAsync(TimeSpan.FromSeconds(1));
            var choice = Get(service, "_subtitleSelectionOperation")!;
            Require(wire.PreferenceWrites == writesBeforeLoad, "A queued choice saved before execution.");
            Property(service, "IsLoading", false);
            var owner = new CancellationTokenSource(); Set(service, "_loadedMediaInitCts", owner);
            var pending = (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (reloads)
                Require(mpv.GetPropertyDouble("sid") <= 0, "Reload did not clear the outgoing native captions before reading the sidecar.");
            else
                Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0,
                    "Default native captions remained visible during the loaded-file sidecar read.");
            await autoSkipConfigured.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!pending.IsCompleted && !subtitles.Release.Task.IsCompleted,
                "Auto-skip settings must reach the OSC while the initial sidecar read is still held.");
            Task<bool>? newerSelection = null;
            if (newerPick)
            {
                Require(service.ActiveSubtitleServerIndex == initialTrack.Index &&
                    ReferenceEquals(Get(service, "_executedSubtitleTrack"), initialTrack),
                    "The reload control missed the unapplied prior choice's held re-read.");
                mpv.SendScriptMessage("silo-loaded-sidecar-pending");
                newerSelection = (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, true)!;
                await Until(() => subtitles.EnglishRequests >= 2);
                Require(!newerSelection.IsCompleted && mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == writesBeforeLoad,
                    "The newer read applied or saved before readiness.");
            }
            if (scenario == "sync-stop")
            {
                mpv.SendScriptMessage("silo-loaded-sidecar-pending");
                Call(service, "StopSubtitleSync");
            }
            if (skipsPrefetch)
                Require(subtitles.EngRequests == 1 && subtitles.SyncRequests == 0 && wire.PreferenceWrites == writesBeforeLoad,
                    "An opaque extensionless codec bypassed its held managed header probe or wrote a preference prematurely.");
            subtitles.Release.TrySetResult();
            if (urlFallbackRecovers)
            {
                await subtitles.FallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(!pending.IsCompleted && service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == writesBeforeLoad,
                    "Loaded-file URL fallback selected or saved before readiness.");
                subtitles.FallbackRelease.TrySetResult();
            }
            if (newerSelection != null)
                Require(!await newerSelection.WaitAsync(TimeSpan.FromSeconds(12)), "A busy pre-acceptance pick reported success.");
            await pending.WaitAsync(TimeSpan.FromSeconds(12));
            mpv.SendScriptMessage("silo-loaded-sidecar-barrier");
            if (urlFallbackRecovers)
            {
                Require(await drained.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "0" &&
                    service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 0 }) && subtitles.EngRequests == expectedRequests && subtitles.SyncRequests == 0,
                    "Loaded-file fallback did not select the verified URL SID and save the queued choice exactly once.");
                Require(!notices.Any(n => n.Contains("could not be loaded", StringComparison.Ordinal)),
                    "Successful loaded-file fallback reported refusal.");
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, false)!;
                Require(wire.RequestedPreferences.SequenceEqual(new[] { 0 }) && subtitles.EngRequests == expectedRequests,
                    "Repeated initialization redownloaded or saved the URL fallback twice.");
                return;
            }
            Require(await drained.Task.WaitAsync(TimeSpan.FromSeconds(4)) == "-1" &&
                service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == writesBeforeLoad,
                "Failed loaded-file read changed actual Off, the applied OSC highlight, or saved preferences.");
            Require(Get(service, "_executedSubtitleTrack") == null && service.CaptureSiloCastState()["selectedSubtitleTrackId"] == null,
                "An unloaded prior subtitle remained executed or selected in SiloCast after a failed reload read.");
            var busy = scenario.StartsWith("busy-", StringComparison.Ordinal);
            Require(notices.Any(n => n.Contains(busy ? "still preparing" : "could not be loaded", StringComparison.Ordinal)),
                "The current loaded-file read failed without actionable feedback.");
            Require(subtitles.EngRequests == (busy ? newerPick ? 5 : 4 : scenario.StartsWith("error", StringComparison.Ordinal) ? 2 : 1) &&
                subtitles.SyncRequests == 0,
                "Loaded-file failure exceeded its read/fallback budget or a busy read reached mpv.");
            if (newerPick)
            {
                var refused = Get(service, "_subtitleSelectionOperation")!;
                Require(!ReferenceEquals(choice, refused) && !(bool)refused.GetType().GetProperty("Accepted")!.GetValue(refused)! &&
                    manager.SubtitleSelectionForLoad == initialTrack.Index && Get(service, "_pendingSubtitlePreferenceOwner") == null,
                    "The refused newer read retained acceptance, changed manager intent or revived the retired initializer's preference.");
            }
            if (scenario == "sync-stop")
            {
                Require(ReferenceEquals(choice, Get(service, "_subtitleSelectionOperation")) &&
                    (bool)choice.GetType().GetProperty("Accepted")!.GetValue(choice)! &&
                    (int)choice.GetType().GetField("PreferenceClaimed")!.GetValue(choice)! == 0 && manager.SubtitleSelectionForLoad == 0,
                    "Read-only retirement consumed the still-current queued manual preference authority.");
                Call(service, "StartLoadedMediaInitialization");
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                Require(service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 &&
                    wire.RequestedPreferences.SequenceEqual(new[] { 0 }), "Retry lost the queued choice or did not save it once after execution.");
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, false)!;
                Require(wire.RequestedPreferences.SequenceEqual(new[] { 0 }), "Repeated initialization duplicated the manual preference.");
            }
        }
        finally
        {
            subtitles.Release.TrySetResult(); subtitles.FallbackRelease.TrySetResult();
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= Observe; Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckBusyAsync(StackPanel parent, string scenario)
    {
        var recovers = scenario.StartsWith("recovered", StringComparison.Ordinal) || scenario.StartsWith("viewer-", StringComparison.Ordinal);
        var urlFallback = scenario is "timeout" or "oversized-recovered" || scenario.StartsWith("fallback-", StringComparison.Ordinal);
        var fallbackRetired = scenario.StartsWith("fallback-", StringComparison.Ordinal);
        var readFailure = urlFallback || scenario is "oversized-declared" or "oversized-chunked" or "not-found" or "not-found-automatic";
        var translationCompleted = scenario == "translation-completed";
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
        var startsPaused = scenario is not ("recovered-playing" or "viewer-pauses");
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
            if (urlFallback)
            {
                await subtitles.FallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Require(!pending.IsCompleted && service.ActiveSubtitleServerIndex == 1 &&
                    mpv.GetPropertyDouble("sid") == priorSid && wire.RequestedPreferences.SequenceEqual(new[] { 1 }),
                    "The pending URL fallback changed or saved the applied choice before readiness.");
                var fallbackTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                parent.DispatcherQueue.TryEnqueue(() =>
                {
                    // Exercise the same lock used by load/cleanup on the UI
                    // while the native remote read is deliberately held.
                    lock (Get(service, "_subtitleTrackStateLock")!) fallbackTick.TrySetResult();
                });
                await fallbackTick.Task.WaitAsync(TimeSpan.FromSeconds(1));
                if (scenario == "fallback-newer-off") await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                if (scenario == "fallback-profile") api.SetProfile("replacement");
                if (scenario == "fallback-load") Call(service, "LoadSubtitles");
                if (scenario == "fallback-sync-stop") Call(service, "StopSubtitleSync");
                subtitles.FallbackRelease.TrySetResult();
            }
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
            if (readFailure && !urlFallback || scenario is "no-context-sync-stop" or "fallback-load" or "fallback-sync-stop")
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
            if (readFailure && !urlFallback || translationCompleted || scenario is "exhausted" or "profile" or "superseded-exhausted" or "missing-native-503" or "no-context-sync-stop" or "fallback-profile" or "fallback-load" or "fallback-sync-stop")
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
            if (urlFallback && !fallbackRetired)
            {
                Require(service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyDouble("sid") > 0 && mpv.GetPropertyDouble("sid") != priorSid &&
                    manager.SubtitleSelectionForLoad == 0 && wire.RequestedPreferences.SequenceEqual(new[] { 1, 0 }),
                    "A non-busy prefetch failure did not apply and save its supported URL fallback exactly once.");
                Require(!notices.Any(n => n.Contains("could not be loaded", StringComparison.Ordinal)),
                    "A successful URL fallback reported refusal.");
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 2,
                    "Managed fallback lost its downloaded file or the prior cached file.");
            }
            if (scenario == "fallback-newer-off") Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                wire.RequestedPreferences.SequenceEqual(new[] { 1, -1 }) && manager.SubtitleSelectionForLoad == -1,
                "A retired URL completion replaced or saved over newer Off.");
            if (scenario == "newer-off") Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0, "Off lost to a pending busy selection.");
            if (scenario == "newer-track") Require(service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") == priorSid, "New track lost to a pending busy selection.");
            if (scenario == "aba") Require(wire.RequestedPreferences.ToArray().SequenceEqual(new[] { 1, -1, 0 }), "Retired same-track A persisted after the newer A.");
            if (translationCompleted)
                Require((int)Get(service, "_liveSubtitleSid")! == priorSid && (Get(service, "_liveSubtitlePath") as string) == livePath && File.Exists(livePath),
                    "Refused stored translation removed the live SID or its cue file.");
            if (translationCompleted || scenario is "exhausted" or "missing-native-503") Require(subtitles.EngRequests == 4, "Exhausted busy read exceeded its attempt budget.");
            if (readFailure) Require(subtitles.EngRequests == 2, "Non-busy failure did not use exactly one managed fallback read.");
            if (scenario is "newer-off" or "newer-track" or "load" or "profile" or "close" or "no-context-sync-stop") Require(subtitles.EngRequests == 1, "Retired busy read continued requesting.");
            using var logReader = new StreamReader(new FileStream(Path.Combine(Program.ResultDirectory, "mpv_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var nativeLog = logReader.ReadToEnd();
            Require(!nativeLog.Split('\n').Any(line => line.Contains("Run command: sub-add", StringComparison.Ordinal) && line.Contains(subtitles.Origin, StringComparison.Ordinal)),
                "A busy text selection or managed fallback issued a remote native sub-add.");
            Require(subtitles.SyncRequests == 0, "A busy read or managed fallback issued a remote native sub-add.");
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
            subtitles.MediaRelease.TrySetResult(); subtitles.FallbackRelease.TrySetResult();
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

    internal static async Task RunBurnReadinessAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var choice in new[] { "busy-text", "busy-text-loading", "busy-text-switching", "ready-then-busy", "ready-then-busy-new-session", "changed-file-busy", "changed-file-newer" })
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
        using var wire = new Wire(subtitles.Origin) { SupportBurnInReplan = true, StartsBurned = choice != "bitmap", HoldNextReplan = true,
            ChangeSessionOnReplan = choice == "ready-then-busy-new-session" };
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
        var selected = choice == "bitmap" ? 2 : choice == "off" ? -1 : 0;
        var track = selected < 0 ? null : manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == selected);
        if (choice.StartsWith("busy-text", StringComparison.Ordinal) || choice is "ready-then-busy" or "ready-then-busy-new-session" or "changed-file-busy" or "changed-file-newer")
        {
            wire.HoldNextReplan = false;
            wire.SidecarReadCount = () => subtitles.EngRequests;
            var busyText = choice.StartsWith("busy-text", StringComparison.Ordinal);
            subtitles.BusyResponses = busyText ? 100 : 0;
            subtitles.BusyAfter = choice.StartsWith("ready-then-busy", StringComparison.Ordinal) ? 1 : int.MaxValue;
            wire.ChangeFileOnReplan = choice.StartsWith("changed-file-", StringComparison.Ordinal);
            wire.HoldNextReplan = choice == "changed-file-newer";
            subtitles.BusyChangedFile = choice == "changed-file-busy";
            subtitles.MediaRelease.TrySetResult();
            Set(service, "_activeSubtitleServerIndex", 2); manager.RecordLocalSubtitleSelection(2);
            var noticeLogStart = new FileInfo(Path.Combine(Program.ResultDirectory, "mpv_log.txt")).Length;
            try
            {
                if (choice == "busy-text-loading") Property(service, "IsLoading", true);
                if (choice == "busy-text-switching") Set(service, "_switchingContent", true);
                var originalChoice = service.SetSubtitleTrackAndPersistAsync(1, track!.Language, track);
                Task? newerChoice = null;
                if (choice == "changed-file-newer")
                {
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    var spanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
                    newerChoice = service.SetSubtitleTrackAndPersistAsync(2, spanish.Language, spanish);
                    wire.ReplanRelease.TrySetResult();
                }
                await originalChoice.WaitAsync(TimeSpan.FromSeconds(12));
                if (choice == "busy-text-loading") Property(service, "IsLoading", false);
                if (choice == "busy-text-switching") Set(service, "_switchingContent", false);
                if (newerChoice != null) await newerChoice.WaitAsync(TimeSpan.FromSeconds(12));
                await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
                if (busyText)
                {
                    Require(wire.Replans == 0 && manager.CurrentSession!.IsSubtitleBurnedIn && service.ActiveSubtitleServerIndex == 2 &&
                        manager.SubtitleSelectionForLoad == 2 && wire.PreferenceWrites == 0 && subtitles.EngRequests == 4,
                        "A busy text read committed a replacement over the applied burned subtitle or persisted an unapplied choice.");
                }
                else if (choice.StartsWith("ready-then-busy", StringComparison.Ordinal))
                {
                    Require(!wire.ChangeSessionOnReplan || manager.SessionId == "fixture-1",
                        "The carry regression did not replace the subtitle-sync session.");
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
                    var exhaustedOwner = Get(service, "_subtitleSelectionOperation")!;
                    Require(!(bool)exhaustedOwner.GetType().GetProperty("Accepted")!.GetValue(exhaustedOwner)! &&
                        (int)exhaustedOwner.GetType().GetField("PreferenceClaimed")!.GetValue(exhaustedOwner)! == 0,
                        "The exhausted manual operation retained preference acceptance or claimed an unapplied completion.");
                    subtitles.BusyChangedFile = false;
                    // Re-run automatic selection from the retained valid grant,
                    // not a new viewer preference. It may execute successfully
                    // without reviving the exhausted manual save authority.
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", manager.SubtitleSelectionForLoad, false)!;
                    Require(service.ActiveSubtitleServerIndex == 7 && mpv.GetPropertyDouble("sid") > 0 &&
                        wire.PreferenceWrites == 0 && wire.Replans == 1,
                        $"Changed-file owner was redundantly replanned or its exhausted preference revived: replans={wire.Replans}, writes={wire.PreferenceWrites}, active={service.ActiveSubtitleServerIndex}, sid={mpv.GetPropertyDouble("sid")}.");
                    Require(ReferenceEquals(exhaustedOwner, Get(service, "_subtitleSelectionOperation")) &&
                        !(bool)exhaustedOwner.GetType().GetProperty("Accepted")!.GetValue(exhaustedOwner)! &&
                        (int)exhaustedOwner.GetType().GetField("PreferenceClaimed")!.GetValue(exhaustedOwner)! == 0,
                        "Automatic grant selection adopted or completed a retired manual operation.");
                    var retryTrack = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 7);
                    await service.SetSubtitleTrackAndPersistAsync(8, retryTrack.Language, retryTrack);
                    var retryOwner = Get(service, "_subtitleSelectionOperation")!;
                    Require(!ReferenceEquals(exhaustedOwner, retryOwner) &&
                        (bool)retryOwner.GetType().GetProperty("Accepted")!.GetValue(retryOwner)! &&
                        (int)retryOwner.GetType().GetField("PreferenceClaimed")!.GetValue(retryOwner)! == 1 &&
                        wire.RequestedPreferences.SequenceEqual(new[] { 7 }),
                        "A fresh explicit retry did not persist the successfully executed granted-file choice once.");
                    using var logStream = new FileStream(Path.Combine(Program.ResultDirectory, "mpv_log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    logStream.Seek(noticeLogStart, SeekOrigin.Begin);
                    using var logReader = new StreamReader(logStream);
                    var log = logReader.ReadToEnd();
                    Require(log.Contains("Subtitle still preparing", StringComparison.Ordinal), "A busy new-file read had no actionable native notice.");
                }
                else
                {
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

    private static async Task CheckSidecarBudgetAsync(StackPanel parent, string scenario)
    {
        var pgs = scenario.Contains("large-pgs", StringComparison.Ordinal);
        var automatic = scenario.EndsWith("-loaded", StringComparison.Ordinal);
        using var subtitles = new HeldSubtitleServer(false);
        if (pgs)
        {
            subtitles.SubtitleBytes = CreateLargePgs();
            subtitles.SubtitleMediaType = "application/octet-stream";
            Require(subtitles.SubtitleBytes.Length > SubtitleSidecarFile.MaxBytes,
                "PGS regression must exceed the old text download limit.");
        }
        using var wire = new Wire(subtitles.Origin) { PublishProfileSettings = automatic }; using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        if (automatic) Property(auth, "SelectedProfileId", "fixture");
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        var track = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
        if (pgs) { track.Codec = "hdmv_pgs_subtitle"; track.Url = subtitles.Origin + "/eng.raw"; }
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0); mpv.Pause();
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("remux", subtitles.Origin + "/media.wav"));
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        Call(service, "SendSubtitleListToOsc");
        Task<bool>? selection = null;
        var probeReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settingsPublished = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var introModePublished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveSettings(string[] args)
        {
            if (args.FirstOrDefault() == "silo-budget-settings-ready") probeReady.TrySetResult();
            if (args.FirstOrDefault() == "silo-budget-auto-skip" && args.Length == 4 && args[1] == "true")
                settingsPublished.TrySetResult(args[1..]);
            if (args.FirstOrDefault() == "silo-budget-intro-mode" && args.Length == 2)
                introModePublished.TrySetResult(args[1]);
        }
        mpv.ScriptMessageReceived += ObserveSettings;
        try
        {
            if (automatic)
            {
                var script = Path.Combine(Program.ResultDirectory, scenario + "-settings.lua");
                await File.WriteAllTextAsync(script, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-set-auto-skip', function(intro, recap, credits)
                        mp.commandv('script-message', 'silo-budget-auto-skip', intro, recap, credits)
                    end)
                    mp.register_script_message('osc-set-intro-mode', function(mode)
                        mp.commandv('script-message', 'silo-budget-intro-mode', mode)
                    end)
                    mp.commandv('script-message', 'silo-budget-settings-ready')
                    """);
                typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(mpv, [new[] { "load-script", script }]);
                await probeReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                // Exercise the actual FILE_LOADED event and its initialization
                // worker, with no manual pending owner to force feedback/saving.
                manager.RecordLocalSubtitleSelection(0, isAutomatic: true);
                Set(service, "_restorePausedAfterLoad", (bool?)true);
                Call(service, "WireMpvEvents"); mpv.LoadFile(wave);
            }
            else
            {
                var started = new TaskCompletionSource<Task<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
                Require(parent.DispatcherQueue.TryEnqueue(() =>
                {
                    try { started.TrySetResult((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!); }
                    catch (Exception ex) { started.TrySetException(ex); }
                }), "Dispatcher rejected the manual budget regression.");
                selection = await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (automatic)
            {
                var flags = await settingsPublished.Task.WaitAsync(TimeSpan.FromSeconds(4));
                var introMode = await introModePublished.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(flags.SequenceEqual(new[] { "true", "false", "true" }) && introMode == "always" &&
                    !subtitles.Release.Task.IsCompleted && Get(service, "_loadedMediaInitCts") != null,
                    "FILE_LOADED did not publish the profile's auto-skip and intro mode while the sidecar remained held.");
            }
            if (!pgs)
            {
                // No headers (and hence no first byte) before the old 8s limit.
                await Task.Delay(TimeSpan.FromSeconds(9));
                Require(automatic ? Get(service, "_loadedMediaInitCts") != null : !selection!.IsCompleted,
                    "Slow extraction was abandoned at the old eight-second deadline.");
                // The bounded text probe expires after eight seconds; the
                // non-busy disk fallback owns one fresh, longer-lived read.
                // Unlike PGS, this path intentionally makes two HTTP requests.
                await Until(() => subtitles.EnglishRequests == 2);
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0 &&
                    ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).Count == 0 &&
                    mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == 0,
                    "The slow text probe/fallback applied or saved captions before readiness.");
            }
            var pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Require(parent.DispatcherQueue.TryEnqueue(() => pulse.TrySetResult()), "Dispatcher rejected the download pulse.");
            await pulse.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var gate = Get(service, "_subtitleAddGate")!;
            Require(Monitor.TryEnter(gate), "The sidecar download held the native add gate."); Monitor.Exit(gate);
            subtitles.Release.TrySetResult();
            if (selection != null) Require(await selection.WaitAsync(TimeSpan.FromSeconds(20)), "Manual sidecar budget regression did not execute.");
            else
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (Get(service, "_loadedMediaInitCts") != null && DateTime.UtcNow < deadline) await Task.Delay(20);
                Require(Get(service, "_loadedMediaInitCts") == null, "FILE_LOADED sidecar initialization did not finish.");
            }
            var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
            var sids = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            Require(files.TryGetValue(0, out var path) && File.Exists(path) &&
                (!pgs || new FileInfo(path).Length == subtitles.SubtitleBytes!.Length) &&
                sids.TryGetValue(0, out var sid) && sid > 0 && mpv.GetPropertyDouble("sid") == sid &&
                service.ActiveSubtitleServerIndex == 0 && mpv.GetPropertyFlag("pause") &&
                subtitles.EnglishRequests == (pgs ? 1 : 2) && wire.PreferenceWrites == (automatic ? 0 : 1),
                "Large/slow sidecar did not retain its bytes, actual SID, highlight, pause or preference ownership.");
        }
        finally
        {
            subtitles.Release.TrySetResult();
            if (selection != null) try { await selection.WaitAsync(TimeSpan.FromSeconds(20)); } catch (Exception) { }
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= ObserveSettings;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static byte[] CreateLargePgs()
    {
        // Valid SUP display sets: one full-frame solid object, palette, window,
        // composition and END. Repeated cues model a long bitmap track, not
        // a text body mislabeled as PGS or unparseable size padding.
        using var stream = new MemoryStream();
        var pixels = new byte[1080 * 6];
        for (var y = 0; y < 1080; y++)
            new byte[] { 0, 0xc7, 0x80, 1, 0, 0 }.CopyTo(pixels, y * 6);
        var objectLength = pixels.Length + 4;
        var objectData = new byte[pixels.Length + 11];
        new byte[] { 0, 0, 0, 0xc0, (byte)(objectLength >> 16), (byte)(objectLength >> 8), (byte)objectLength,
            0x07, 0x80, 0x04, 0x38 }.CopyTo(objectData, 0);
        pixels.CopyTo(objectData, 11);
        uint pts = 90000;
        ushort composition = 0;
        while (stream.Length <= SubtitleSidecarFile.MaxBytes)
        {
            Packet(0x16, [0x07, 0x80, 0x04, 0x38, 0x10, (byte)(composition >> 8), (byte)composition,
                0x80, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]);
            Packet(0x17, [1, 0, 0, 0, 0, 0, 0x07, 0x80, 0x04, 0x38]);
            Packet(0x14, [0, 0, 1, 235, 128, 128, 255]);
            Packet(0x15, objectData);
            Packet(0x80, []);
            pts += 90000; composition++;
        }
        return stream.ToArray();

        void Packet(byte type, byte[] payload)
        {
            stream.Write([0x50, 0x47, (byte)(pts >> 24), (byte)(pts >> 16), (byte)(pts >> 8), (byte)pts,
                0, 0, 0, 0, type, (byte)(payload.Length >> 8), (byte)payload.Length]);
            stream.Write(payload);
        }
    }

    private static async Task CheckDispatcherSidecarAsync(StackPanel parent, string scenario)
    {
        var pgs = scenario == "dispatcher-pgs";
        using var subtitles = new HeldSubtitleServer(false) { FailSubtitleRequests = !pgs };
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        if (pgs)
        {
            var track = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
            track.Codec = "hdmv_pgs_subtitle"; track.Url = subtitles.Origin + "/eng.raw";
        }
        Call(service, "SendSubtitleListToOsc");
        var started = new TaskCompletionSource<Task<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Supervise off-dispatcher so even a broken synchronous URL add is
        // released on timeout instead of hanging the native regression runner.
        var monitor = Task.Run(async () =>
        {
            try
            {
                await (pgs ? subtitles.Entered.Task : subtitles.FirstFailure.Task).WaitAsync(TimeSpan.FromSeconds(4));
                if (!pgs)
                {
                    var pending = await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await Task.WhenAny(pending, subtitles.Entered.Task).WaitAsync(TimeSpan.FromSeconds(4));
                }
                var pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Require(parent.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (pgs) await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                        pulse.TrySetResult();
                    }
                    catch (Exception ex) { pulse.TrySetException(ex); }
                }), "Dispatcher rejected the sidecar responsiveness probe.");
                await pulse.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var gate = Get(service, "_subtitleAddGate")!;
                Require(Monitor.TryEnter(gate), "Remote download held the add gate needed by realtime/local cues.");
                Monitor.Exit(gate);
                var selection = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Require(subtitles.EnglishRequests == (pgs ? 1 : 2) && subtitles.SyncRequests == 0,
                    "The held or failed download exceeded its managed read/fallback budget or issued a remote native sub-add.");
                if (pgs) Require(!await selection.WaitAsync(TimeSpan.FromSeconds(2)) && service.ActiveSubtitleServerIndex == -1,
                    "Held PGS download blocked a newer dispatcher Off choice or failed to retire promptly.");
                else Require(!await selection.WaitAsync(TimeSpan.FromSeconds(2)), "Failed prefetch claimed an executed selection.");
            }
            finally { subtitles.Release.TrySetResult(); }
        });
        try
        {
            // This is the same dispatcher entry used by OSC picks, not Task.Run.
            Require(parent.DispatcherQueue.TryEnqueue(() =>
            {
                try { started.TrySetResult((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!); }
                catch (Exception ex) { started.TrySetException(ex); }
            }), "Dispatcher rejected the subtitle pick.");
            await monitor;
            var selection = await started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await selection.WaitAsync(TimeSpan.FromSeconds(5));
            Require(mpv.GetPropertyDouble("sid") <= 0 && wire.PreferenceWrites == (pgs ? 1 : 0) &&
                ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).Count == 0,
                "A failed or superseded download changed SID or persisted an unexecuted choice.");
        }
        finally
        {
            subtitles.Release.TrySetResult();
            try { await (await started.Task.WaitAsync(TimeSpan.FromSeconds(5))).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
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
        // A readable duration does not mean the event pump consumed FILE_LOADED.
        // Drain the setup load before wiring the service, or its late callback
        // can unpause/complete the held replacement as if that load were ready.
        var initialLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void InitialLoaded() => initialLoaded.TrySetResult();
        mpv.FileLoaded += InitialLoaded;
        try
        {
            mpv.LoadFile(wave);
            await initialLoaded.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await Until(() => mpv.GetPropertyDouble("duration") > 0);
        }
        finally { mpv.FileLoaded -= InitialLoaded; }
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
                if (afterFileLoaded)
                {
                    // Establish the prior choice through the real picker as well
                    // as its pending slot: the manager retains identity, not just
                    // the private ordinal used by older versions of this fixture.
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!;
                    Require(manager.SubtitleSelectionForLoad == 1 && service.ActiveSubtitleServerIndex == 1,
                        "Fixture did not establish the retained Spanish choice before reloading.");
                }
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
                // Settings and subtitle execution now run concurrently. Entering
                // the held settings request does not mean the local selection
                // has finished; wait for actual execution while settings stay held.
                await Until(() =>
                {
                    lock (Get(service, "_subtitleTrackStateLock")!)
                        return !wire.SettingsRelease.Task.IsCompleted && !service.IsLoading &&
                            Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                            service.ActiveSubtitleServerIndex == 1 && initialMap.TryGetValue(1, out var sid) &&
                            mpv.GetPropertyDouble("sid") == sid;
                });
                Require(!service.IsLoading && Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                    service.ActiveSubtitleServerIndex == 1 && initialMap.TryGetValue(1, out var initialSid) && mpv.GetPropertyDouble("sid") == initialSid,
                    "Subtitles must execute while optional settings reads remain held; fixture missed that loaded/settings-await interval.");
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
            Require(!retiredBitmap || !wire.RequestedPreferences.Contains(2),
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
        var blockingAdd = scenario.StartsWith("blocking-add-", StringComparison.Ordinal);
        var legacyDirectReopen = scenario == "legacy-direct-reopen";
        var failedSwitch = scenario.StartsWith("failed-", StringComparison.Ordinal);
        var refreshing = scenario.StartsWith("refresh-", StringComparison.Ordinal);
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
        var nativeMedia = blockingAdd || legacyDirectReopen ? Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity", "tracks.mkv")) : wave;
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", nativeMedia));
        var initialLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void InitialLoaded() => initialLoaded.TrySetResult();
        mpv.FileLoaded += InitialLoaded;
        try
        {
            mpv.LoadFile(nativeMedia);
            await initialLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => mpv.GetPropertyDouble("duration") > 0);
        }
        finally { mpv.FileLoaded -= InitialLoaded; }
        if (legacyDirectReopen)
        {
            var session = manager.CurrentSession!;
            session.ProtocolVersion = 2;
            var track = session.SubtitleUrls.Single(t => t.Index == 1);
            var native = mpv.GetEmbeddedSubtitleTracks().Last();
            track.TrackId = null; track.Source = "embedded"; track.Url = "";
            track.Codec = native.Codec; track.Language = native.Language!;
            track.NativeTitle = native.Title; track.Label = native.Title ?? "";
            track.StreamIndex = native.StreamIndex; track.ContainerTrackId = native.ContainerTrackId;
            track.Forced = native.Forced; track.HearingImpaired = native.HearingImpaired;
        }
        if (blockingAdd)
        {
            // Unknown formats must also use the bounded managed download.
            // Hold its response while a newer Off/native choice executes.
            var remote = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
            remote.Url = subtitles.Origin + "/eng.raw";
            remote.Codec = null;
            manager.CurrentSession.SubtitleUrls.Add(new() { Index = 3, Source = "embedded", Codec = "subrip", Language = "ja", StreamIndex = 2, ContainerTrackId = "3" });
        }
        if (refreshing)
        {
            var download = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0);
            download.Index = 2; download.Source = "downloaded";
            download.Id = 100; download.SyncKey = "stored-100";
            wire.DownloadedInventory = new() { Subtitles = [new() { Id = 100, Language = "eng", Format = "srt" }] };
        }
        var startPaused = scenario == "viewer-resumes" || failedSwitch || refreshing || legacyDirectReopen;
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
            if (legacyDirectReopen)
            {
                var session = manager.CurrentSession!;
                var track = session.SubtitleUrls.Single(t => t.Index == 1);
                await service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track);
                var nativeSid = mpv.GetPropertyDouble("sid");
                Require(nativeSid > 0 && service.ActiveSubtitleServerIndex == track.Index &&
                    !manager.CaptureLocalSubtitleIntent().HasSelection &&
                    Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                    wire.RequestedPreferences.SequenceEqual(new[] { track.Index }),
                    "Legacy reopen must start with an executed native choice, without manager intent or a pending initial slot.");
                subtitles.ReplacementMedia = await File.ReadAllBytesAsync(nativeMedia);
                var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", session, Version(),
                    subtitles.Origin + "/stream/direct/replacement.wav", 0d, CancellationToken.None)!;
                await prepare;
                var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
                Call(service, "ApplyPreparedTransport", prepared);
                service.ContentLoaded += ReplacementLoaded;
                Call(service, "WireMpvEvents");
                var generationBeforeLoad = (long)Get(service, "_subtitleSidecarGeneration")!;
                // Exercise the same BeginMpvLoad/FILE_LOADED boundary used by
                // legacy direct-stream recovery, retaining its exact session.
                Call(service, "BeginMpvLoad", prepared, true);
                await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(service.IsLoading && !replacementLoaded.Task.IsCompleted && mpv.GetPropertyDouble("sid") <= 0,
                    "Legacy reopen did not clear the outgoing SID while the replacement file was held.");
                subtitles.MediaRelease.TrySetResult();
                await replacementLoaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generationBeforeLoad &&
                    Get(service, "_loadedMediaInitCts") == null);
                Require(ReferenceEquals(session, manager.CurrentSession) &&
                    service.ActiveSubtitleServerIndex == track.Index && mpv.GetPropertyDouble("sid") == nativeSid && mpv.IsPaused &&
                    ReferenceEquals(Get(service, "_executedSubtitleTrack"), track) &&
                    service.CaptureSiloCastState()["selectedSubtitleTrackId"]!.GetValue<int>() == track.Index + 1 &&
                    !manager.CaptureLocalSubtitleIntent().HasSelection && wire.Replans == 0 &&
                    wire.RequestedPreferences.SequenceEqual(new[] { track.Index }) && subtitles.EnglishRequests == 0,
                    "Legacy direct reopen lost the executed native subtitle, changed intent, downloaded a sidecar, or saved it again.");
                await Until(() => seen.LastOrDefault() == track.Index.ToString());
                return;
            }
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
            if (refreshing)
            {
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                var preferencesBefore = wire.PreferenceWrites;
                var postLoad = scenario.EndsWith("post-load", StringComparison.Ordinal);
                var reindex = scenario.Contains("reindex", StringComparison.Ordinal);
                var removed = scenario.Contains("removed", StringComparison.Ordinal);
                var original = manager.CurrentSession!.SubtitleUrls.Single(t => t.Id == 100);
                if (postLoad)
                {
                    Property(service, "IsLoading", true);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 2, true)!;
                    Property(service, "IsLoading", false);
                    Call(service, "LoadSubtitles");
                }
                var refreshSelection = (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 2, !postLoad)!;
                try
                {
                    await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    var refreshGeneration = (long)Get(service, "_subtitleSidecarGeneration")!;
                    if (reindex || removed)
                        wire.DownloadedInventory!.Subtitles.Insert(0, new() { Id = 200, Language = "eng", Format = "srt" });
                    if (removed) wire.DownloadedInventory!.Subtitles.RemoveAll(t => t.Id == 100);
                    await service.RefreshSubtitlesAfterAiAsync(42);
                    // Duplicate event/dialog refreshes must not retire the held pick either.
                    await service.RefreshSubtitlesAfterAiAsync(42);
                    Require(!refreshSelection.IsCompleted && (long)Get(service, "_subtitleSidecarGeneration")! == refreshGeneration,
                        "An inventory refresh was mistaken for a file load during held prefetch.");
                    if (!removed)
                        Require(ReferenceEquals(original, manager.CurrentSession.SubtitleUrls.Single(t => t.Id == 100)),
                            "A retained download lost the identity object owned by the in-flight selection.");
                    subtitles.Release.TrySetResult();
                    var executed = await refreshSelection.WaitAsync(TimeSpan.FromSeconds(5));
                    var expected = removed ? -1 : reindex ? 3 : 2;
                    await Until(() => seen.LastOrDefault() == expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                    Require(executed == !removed && service.ActiveSubtitleServerIndex == expected &&
                        manager.SubtitleSelectionForLoad == expected && mpv.IsPaused &&
                        (removed ? mpv.GetPropertyDouble("sid") <= 0 && !map.ContainsKey(2) :
                            map.TryGetValue(expected, out var sid) && mpv.GetPropertyDouble("sid") == sid),
                        "The refreshed selection did not execute its retained identity or roll back the removed choice.");
                    Require(wire.PreferenceWrites == preferencesBefore + (removed ? 0 : 1) &&
                        Get(service, "_pendingSubtitlePreferenceOwner") == null && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                        "Refresh stranded a pending preference or persisted an unexecuted selection.");
                    if (removed)
                        await Until(() => notices.Any(payload => payload.Contains("Subtitle unavailable", StringComparison.Ordinal)));
                    else
                        Require(notices.IsEmpty && wire.RequestedPreferences.Last() == expected &&
                            service.CaptureSiloCastState()["selectedSubtitleTrackId"]!.GetValue<int>() == expected + 1,
                            "Refresh disagreed with the executed SiloCast selection or showed a false refusal.");
                }
                finally
                {
                    subtitles.Release.TrySetResult();
                    await refreshSelection.WaitAsync(TimeSpan.FromSeconds(5));
                }
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
                        seen.IsEmpty && notices.IsEmpty && subtitles.EnglishRequests == 1,
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
            Task<bool>? blockedSelection = null;
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
            else if (blockingAdd) pending = blockedSelection = Task.Run(() => (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, persist)!);
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
            if (blockingAdd || scenario is "newer-off" or "newer-track")
            {
                var selected = scenario is "newer-off" or "blocking-add-off" ? -1 : blockingAdd ? 3 : 1;
                if (blockingAdd)
                    Require(!pending.IsCompleted && !subtitles.Release.Task.IsCompleted,
                        "Fixture did not hold the managed sidecar read before superseding it.");
                await ((Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!).WaitAsync(TimeSpan.FromSeconds(4));
                await Until(() => seen.Contains(selected.ToString()));
                if (blockingAdd)
                {
                    // Remote reads are now cancellable managed downloads, not
                    // synchronous native sub-adds. Supersession must retire the
                    // old caller promptly, without waiting for the held server.
                    Require(!await blockedSelection!.WaitAsync(TimeSpan.FromSeconds(2)) &&
                        !subtitles.Release.Task.IsCompleted && service.ActiveSubtitleServerIndex == selected &&
                        (selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : mpv.GetPropertyDouble("sid") == 2),
                        "A superseded managed read did not retire promptly while the newer Off/native choice applied.");
                    Require(!((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).ContainsKey(0) &&
                        mpv.GetSubtitleTrackIds(externalOnly: true).Count == 0 &&
                        wire.RequestedPreferences.SequenceEqual(new[] { selected }),
                        "The canceled managed read registered a sidecar or persisted before its held response was released.");
                }
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
                        "A retired managed read left a cached or native sidecar behind.");
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
        public TaskCompletionSource FallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FallbackRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] ReplacementMedia { get; set; } = [];
        public byte[]? SubtitleBytes { get; set; }
        public string SubtitleMediaType { get; set; } = "application/x-subrip";
        public int MediaRequests;
        public int BusyResponses, EngRequests, SyncRequests;
        public int BusyAfter = int.MaxValue;
        public bool BusyChangedFile, OpaqueEnglish;
        public string? ReadFailure;
        public bool HoldOnlyFirstSubtitle;
        public bool FailSubtitleRequests;
        public TaskCompletionSource FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int EnglishRequests => Volatile.Read(ref EngRequests);
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
                        var english = request.Request.Url!.AbsolutePath is "/eng.srt" or "/eng.raw" or "/changed-eng.srt" or "/api/v2/stream/fixture/subtitles/2";
                        if (english)
                        {
                            var count = Interlocked.Increment(ref EngRequests);
                            var native = request.Request.UserAgent?.Contains("mpv", StringComparison.OrdinalIgnoreCase) == true || request.Request.UserAgent?.Contains("Lavf", StringComparison.OrdinalIgnoreCase) == true;
                            if (native) Interlocked.Increment(ref SyncRequests);
                            if (FailSubtitleRequests)
                            {
                                request.Response.StatusCode = 404; request.Response.Close(); FirstFailure.TrySetResult(); return;
                            }
                            if (count == 1 || !HoldOnlyFirstSubtitle) { Entered.TrySetResult(); await Release.Task; }
                            if (OpaqueEnglish && count > 1) { FallbackEntered.TrySetResult(); await FallbackRelease.Task; }
                            if (count <= BusyResponses || count > BusyAfter || BusyChangedFile && request.Request.Url.AbsolutePath == "/changed-eng.srt") { request.Response.StatusCode = 503; request.Response.Headers["Retry-After"] = "1"; request.Response.Close(); return; }
                            var fallbackCase = ReadFailure is "timeout" or "oversized-recovered" || ReadFailure?.StartsWith("fallback-", StringComparison.Ordinal) == true;
                            if (fallbackCase && count > 1) { FallbackEntered.TrySetResult(); await FallbackRelease.Task; }
                            if (ReadFailure?.StartsWith("fallback-", StringComparison.Ordinal) == true && count == 1) { request.Response.StatusCode = 500; request.Response.Close(); return; }
                            if (ReadFailure is "not-found" or "not-found-automatic") { request.Response.StatusCode = 404; request.Response.Close(); return; }
                            if (ReadFailure == "timeout" && count == 1) await Task.Delay(TimeSpan.FromSeconds(10));
                            if (ReadFailure is "oversized-declared" or "oversized-chunked" || ReadFailure == "oversized-recovered" && count == 1)
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
                        var bytes = SubtitleBytes ?? Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nNative ownership fixture\n");
                        request.Response.ContentType = OpaqueEnglish && english ? "application/octet-stream" : SubtitleMediaType;
                        request.Response.ContentLength64 = bytes.Length;
                        await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                    }
                    catch (Exception ex) when (!listener.IsListening || ex is IOException or HttpListenerException) { }
                });
            }
        }
        public void Dispose() { Release.TrySetResult(); MediaRelease.TrySetResult(); FallbackRelease.TrySetResult(); listener.Close(); }
    }
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        public int PreferenceWrites;
        public ConcurrentQueue<int> RequestedPreferences = new();
        public ConcurrentQueue<int> CommittedPreferences = new();
        public ConcurrentQueue<string?> RequestedLabels = new();
        public bool HoldNextPreference, FailNextPreference;
        public TaskCompletionSource PreferenceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PreferenceRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SubtitleListResponse? DownloadedInventory;
        public bool PublishProfileSettings;
        public bool HoldSettings;
        public bool SupportBurnInReplan;
        public bool StartsBurned;
        public bool ChangeFileOnReplan;
        public bool ChangeSessionOnReplan;
        public bool RejectReplan, RejectStart;
        public bool ReorderVersionOnStart;
        public int Replans;
        public Func<int>? SidecarReadCount;
        public List<int> ReadsAtReplan = [];
        public List<int> ReplanSelections = [];
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
            if (request.RequestUri is not { } uri || uri.Host != "sidecar-ownership.invalid" && uri.GetLeftPart(UriPartial.Authority) != origin)
                throw new InvalidOperationException("Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/subtitles/42")
                return DownloadedInventory != null
                    ? Json(JsonSerializer.Serialize(DownloadedInventory,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))
                    : Json("""{"subtitles":[{"id":73,"media_file_id":42,"language":"eng","format":"srt","source":"downloaded"}]}""");
            if (path.Contains("-prefs/", StringComparison.Ordinal))
            {
                using var preference = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var index = preference.RootElement.GetProperty("subtitle_track_index").GetInt32();
                RequestedPreferences.Enqueue(index);
                if (preference.RootElement.TryGetProperty("track_signature", out var signature) && signature.ValueKind == JsonValueKind.Object)
                    RequestedLabels.Enqueue(signature.GetProperty("label").GetString());
                Interlocked.Increment(ref PreferenceWrites);
                if (HoldNextPreference)
                {
                    HoldNextPreference = false; PreferenceEntered.TrySetResult(); await PreferenceRelease.Task.WaitAsync(ct);
                }
                if (FailNextPreference)
                {
                    FailNextPreference = false;
                    return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
                }
                CommittedPreferences.Enqueue(index);
                return Json("{}");
            }
            if (path.Contains("/settings/", StringComparison.Ordinal))
            {
                if (HoldSettings) { SettingsEntered.TrySetResult(); await SettingsRelease.Task.WaitAsync(ct); }
                if (PublishProfileSettings && request.RequestUri.Query.Contains("playback.intro_skip_mode", StringComparison.Ordinal))
                    return Json("""{"items":[{"key":"playback.intro_skip_mode","value":"always","source":"profile"}]}""");
                return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }
            if (PublishProfileSettings && path == "/api/v2/profiles")
            {
                await Task.Yield();
                return Json("""{"items":[{"id":"fixture","name":"Fixture","auto_skip_intro":true,"auto_skip_recap":false,"auto_skip_credits":true}]}""");
            }
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (RejectReplan && path.EndsWith("/replan") || RejectStart && path.EndsWith("/start"))
            {
                ReplanEntered.TrySetResult();
                await ReplanRelease.Task.WaitAsync(ct);
                return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"message\":\"Fixture switch refused\"}") };
            }
            if (SupportBurnInReplan && path.EndsWith("/replan"))
            {
                if (SidecarReadCount != null) ReadsAtReplan.Add(SidecarReadCount());
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                LastReplanOperation = document.RootElement.GetProperty("operation").GetString();
                selectedSubtitle = document.RootElement.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selected) &&
                    selected.ValueKind == JsonValueKind.Object ? selected.GetProperty("index").GetInt32() : -1;
                ReplanSelections.Add(selectedSubtitle);
                if (ChangeFileOnReplan && Replans > 0 && selectedSubtitle >= 7) selectedSubtitle -= 7;
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
            else if (path.EndsWith("/start"))
            {
                if (ReorderVersionOnStart)
                {
                    ReplanEntered.TrySetResult(); await ReplanRelease.Task.WaitAsync(ct);
                }
                selectedSubtitle = StartsBurned ? 2 : -1;
            }
            else return Json("""{"outcome":"applied"}""");
            var file = ReorderVersionOnStart || ChangeFileOnReplan && Replans > 0 ? 43 : 42;
            var offset = file == 43 && !ReorderVersionOnStart ? 7 : 0;
            var appliedSubtitle = selectedSubtitle < 0 ? -1 : selectedSubtitle + offset;
            var inventory = Version().SubtitleTracks!.Select(t => (object)new { track_id = $"sub:{file}:{(ReorderVersionOnStart ? 1 - t.Index : t.Index) + offset}", combined_index = (ReorderVersionOnStart ? 1 - t.Index : t.Index) + offset, language = t.Language, codec = t.Codec, source = "external", url = origin + $"/{(file == 43 ? "changed-" : "")}{t.Language}.srt", label = t.Language }).ToList();
            if (SupportBurnInReplan) inventory.Add(new { track_id = $"sub:{file}:{2 + offset}", combined_index = 2 + offset, language = "eng", codec = "dvd_subtitle", source = "embedded", delivery = "burn_in_only", label = "English (DVD)" });
            var sessionId = ChangeSessionOnReplan && Replans > 0 ? $"fixture-{Replans}" : "fixture";
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = sessionId,
                playback_plan = new
                {
                    plan_id = SupportBurnInReplan ? $"plan-{Replans}" : "plan", plan_attempt_key = "key", session_id = sessionId, effective_media_file_id = file,
                    delivery = "original_http", stream = new { url = origin + (SupportBurnInReplan || ReorderVersionOnStart ? "/stream/direct/replacement.wav" : "/media.wav") }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = $"file:{file}:audio:0", index = 0 }, subtitle = appliedSubtitle < 0 ? null : new { id = $"sub:{file}:{appliedSubtitle}", index = appliedSubtitle } },
                    subtitle = new { mode = selectedSubtitle == 2 ? "burn_in" : selectedSubtitle < 0 ? "off" : "render", track_id = appliedSubtitle < 0 ? null : $"sub:{file}:{appliedSubtitle}", inventory },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
