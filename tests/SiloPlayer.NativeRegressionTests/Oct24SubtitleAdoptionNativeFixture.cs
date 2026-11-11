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

internal static class Oct24SubtitleAdoptionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var subtitles = new SubtitleServer();
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://oct24-adoption.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "oct24-subtitle-adoption")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "episode", SeriesId = "series", Versions = [Version(42), Version(43), Version(44)] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "episode");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        manager.WatchDetail.EffectiveSubtitleLanguage = "en";
        manager.WatchDetail.EffectiveSubtitleMode = "auto";
        manager.WatchDetail.EffectiveShowForcedSubtitles = false;
        Call(service, "ResolveInitialSubtitleSelection", manager.WatchDetail, manager.WatchDetail.Versions[0], manager.CurrentSession, null, null, null);
        Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == -1,
            "Startup Auto did not compare the actual granted English audio; it used the old Japanese preview default.");
        Set(service, "_pendingInitialServerSubtitleIndex", null); Set(service, "_pendingSubtitleSelection", null);
        manager.RecordLocalSubtitleSelection(0);
        var wave = Path.Combine(Program.ResultDirectory, "oct24-subtitle-adoption.wav");
        using (var writer = new BinaryWriter(File.Create(wave)))
        {
            const int samples = 60 * 8000;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
        }
        Call(service, "WireMpvEvents");
        try
        {
            await Load(0, 0);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_READY_ONLY") == "1")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                var session = manager.CurrentSession!;
                var intent = manager.CaptureLocalSubtitleIntent();
                var writes = wire.PreferenceWrites;
                var sid = mpv.GetPropertyDouble("sid");
                var originalTransportFile = Get(service, "_fileLoadGeneration");
                using var announcement = JsonDocument.Parse("""
                    {"file_id":42,"subtitle_id":77,"track":{"track_id":"downloaded:77","stored_subtitle_id":77,"combined_index":9,"source":"downloaded","language":"ita","label":"Canonical bitmap","codec":"pgs","delivery":"burn_in_only","unplayable":true}}
                    """);
                Call(service, "HandleWebSocketEvent", new PlaybackRealtimeEvent { Name = "subtitle_ready", SessionId = "", Payload = announcement.RootElement });
                Require(!manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 77), "An announcement without any session identity merged into current playback.");
                Call(service, "HandleWebSocketEvent", new PlaybackRealtimeEvent { Name = "subtitle_ready", SessionId = "old-session", Payload = announcement.RootElement });
                Require(!manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 77), "A stale event envelope merged into current playback.");
                Call(service, "HandleWebSocketEvent", new PlaybackRealtimeEvent { Name = "subtitle_ready", SessionId = "fixture", Payload = announcement.RootElement });
                await Until(() => manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 77));
                var added = manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 77);
                Require(added.Index == 9 && added.TrackId == "downloaded:77" && added.Delivery == "burn_in_only" && added.Unplayable && added.Url == "",
                    "Canonical announcement lost its stored ID, assigned ordinal or delivery restrictions.");
                Require(SubtitleSyncPolicy.Key(added) == null, "A stored row ID invented timing-sync support.");
                using var payloadOnly = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    session_id = "fixture", file_id = 42, subtitle_id = 77,
                    track = announcement.RootElement.GetProperty("track"),
                }));
                added.Label = "Awaiting payload-only announcement";
                Call(service, "HandleWebSocketEvent", new PlaybackRealtimeEvent { Name = "subtitle_ready", SessionId = "old-session", Payload = payloadOnly.RootElement });
                Require(added.Label == "Awaiting payload-only announcement", "A conflicting envelope changed canonical subtitle metadata.");
                Call(service, "HandleWebSocketEvent", new PlaybackRealtimeEvent { Name = "subtitle_ready", SessionId = "", Payload = payloadOnly.RootElement });
                await Until(() => added.Label == "Canonical bitmap");
                Require(ReferenceEquals(added, manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 77)),
                    "A payload-only session announcement replaced its canonical row identity.");
                Require(manager.CaptureLocalSubtitleIntent() == intent && mpv.GetPropertyDouble("sid") == sid && wire.PreferenceWrites == writes,
                    "Another viewer's added subtitle changed the native choice or saved preferences.");
                wire.DownloadedAvailable = true;
                await service.RefreshSubtitlesAfterAiAsync(42);
                await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!;
                Require(ReferenceEquals(added, manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 77)) &&
                    added.Index == 9 && added.Delivery == "burn_in_only" && added.Unplayable && added.Label == "Canonical bitmap" && added.Url == "",
                    "A stored-list refresh downgraded canonical metadata or replaced its row identity.");
                // A canonical download already occupies the first downloaded slot.
                // The next stored row must use downloaded-base + list position, not
                // max(canonical) + list position. Exercise both refresh entry points.
                added.Index = 2;
                wire.AdditionalDownloadedAvailable = true;
                foreach (var ownerRefresh in new[] { false, true })
                {
                    if (ownerRefresh) await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!;
                    else await service.RefreshSubtitlesAfterAiAsync(42);
                    var appended = manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78);
                    Require(appended.Index == 3 && appended.Url == "/stream/fixture/subtitles/3" && !appended.Unplayable && added.Index == 2,
                        "A canonical stored row shifted the newly completed subtitle ordinal or URL.");
                }
                added.Index = 9;
                foreach (var ownerRefresh in new[] { false, true })
                {
                    if (ownerRefresh) await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!;
                    else await service.RefreshSubtitlesAfterAiAsync(42);
                    var uncertain = manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78);
                    Require(uncertain.Unplayable && uncertain.Url == "" && added.Index == 9,
                        "A noncanonical stored-list layout invented an executable subtitle URL.");
                }
                wire.AdditionalDownloadedAvailable = false;
                using var stale = JsonDocument.Parse("""{"session_id":"old-session","file_id":42,"subtitle_id":77}""");
                await (Task)Call(service, "ApplySubtitleReadyAsync", stale.RootElement, "fixture")!;
                await (Task)Call(service, "ApplySubtitleReadyAsync", stale.RootElement, "")!;
                Require(wire.Replans.Count == 0, "A stale session announcement replanned current playback.");
                Require(Equals(originalTransportFile, Get(service, "_fileLoadGeneration")), "A local subtitle merge reloaded mpv.");
                using var unresolved = JsonDocument.Parse("""{"session_id":"fixture","file_id":42,"subtitle_id":77}""");
                service.SetPaused(true);
                Set(service, "_switchingContent", true);
                // Payload-only identity must also survive deferral and the later session fences.
                await (Task)Call(service, "ApplySubtitleReadyAsync", unresolved.RootElement, "")!;
                Require(wire.Replans.Count == 0, "An inventory refresh interrupted an active native load.");
                await Load(0, 0); // Actual FILE_LOADED releases the deferred inventory read.
                await Until(() => wire.Replans.Count == 1 && !service.IsLoading && Get(service, "_loadedMediaInitCts") == null && service.ActiveSubtitleServerIndex == 0);
                Require(wire.Replans.Count == 1 && wire.Replans[0].GetProperty("operation").GetString() == "track_change" &&
                    wire.Replans[0].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("index").GetInt32() == 0 &&
                    manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 77).Index == 2 && service.IsPaused && mpv.IsPaused &&
                    manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes,
                    "No-track inventory refresh did not retain choice/pause or adopt authoritative combined index.");
                using var invalid = JsonDocument.Parse("""{"session_id":"fixture","file_id":42,"subtitle_id":77,"track":{"combined_index":"invalid"}}""");
                var inventoryNativeFile = Get(service, "_fileLoadGeneration");
                var inventorySid = mpv.GetPropertyDouble("sid");
                var buffering = new BufferingQualityController("ask");
                buffering.Adopt(new("buffering-plan", "buffering-adoption", "auto", "1080p", 8000,
                    [new() { Label = "720p", BitrateKbps = 4000 }]), 0);
                buffering.Observe(new(0, false, false, false, false, false, 0, null));
                buffering.Observe(new(11000, true, false, false, false, false, 0, 3000));
                buffering.Observe(new(12500, false, false, false, false, false, 0, 3000));
                buffering.Observe(new(13000, true, false, false, false, false, 0, 3000));
                buffering.Observe(new(14500, false, false, false, false, false, 0, 3000));
                Require(buffering.Pill == "prompt" && buffering.Summary.StallCount == 2,
                    "The inventory regression did not establish stall evidence and a Lower-quality prompt.");
                Set(service, "_bufferingController", buffering); Set(service, "_bufferingManager", manager);
                Set(service, "_bufferingSession", manager.CurrentSession);
                Set(service, "_bufferingLastDataAt", 12345L); Set(service, "_bufferingLastPosition", (double?)12);
                var summary = buffering.Summary; var bufferingPlan = buffering.Plan;
                var grace = Get(buffering, "_grace"); var promptRemaining = buffering.PillRemainingMs;
                var meter = (NativeMediaThroughputMeter)Get(service, "_nativeMediaMeter")!;
                meter.Reset(); meter.Observe(0, 0, false, false); meter.Observe(1000, 1000000, false, false);
                await (Task)Call(service, "ApplySubtitleReadyAsync", invalid.RootElement, "fixture")!;
                await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null);
                Require(Equals(inventoryNativeFile, Get(service, "_fileLoadGeneration")) && mpv.GetPropertyDouble("sid") == inventorySid,
                    "An unchanged-transport inventory refresh reopened mpv or replaced the active subtitle SID.");
                Require(wire.Replans.Count == 2 && wire.PreferenceWrites == writes && manager.CaptureLocalSubtitleIntent() == intent,
                    "Invalid optional track metadata did not recover through an intent-preserving authoritative replan.");
                Require(ReferenceEquals(buffering, Get(service, "_bufferingController")) &&
                    ReferenceEquals(summary, buffering.Summary) && ReferenceEquals(bufferingPlan, buffering.Plan) &&
                    buffering.Summary.StallCount == 2 && buffering.Pill == "prompt" && buffering.PillRemainingMs == promptRemaining &&
                    Equals(grace, Get(buffering, "_grace")) && ((Queue<long>)Get(buffering, "_stalls")!).Count == 2 &&
                    ReferenceEquals(manager.CurrentSession, Get(service, "_bufferingSession")) &&
                    (long)Get(service, "_bufferingLastDataAt")! == 12345 && (double?)Get(service, "_bufferingLastPosition") == 12 &&
                    meter.Observe(2000, 2000000, false, false) == 8000,
                    "A subtitle-only inventory adoption reset buffering history, grace, prompt, throughput or buffering-action ownership.");
                Set(service, "_bufferingController", null); Set(service, "_bufferingManager", null); Set(service, "_bufferingSession", null);
                // Retire the transport snapshot after publication but before
                // the native cache remap. The retry's valid track object must
                // not bypass the outstanding application of the new inventory.
                var beforeRemap = manager.CurrentSession!;
                var remapFile = Get(service, "_fileLoadGeneration");
                var remapSid = mpv.GetPropertyDouble("sid");
                var remapFiles = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
                var remapPath = remapFiles[0];
                wire.ReindexReadyInventory = true;
                wire.HoldReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReplanEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var reordered = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    session_id = "fixture", file_id = 42, subtitle_id = 77,
                    track = new { track_id = "downloaded:77", stored_subtitle_id = 77, combined_index = 0,
                        source = "downloaded", delivery = "sidecar", url = subtitles.Origin + "/ita.srt" },
                }));
                var remapping = Task.Run(() => (Task)Call(service, "ApplySubtitleReadyAsync", reordered.RootElement, "fixture")!);
                try
                {
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    lock (Get(service, "_subtitleSyncGate")!)
                    {
                        wire.HoldReplan.TrySetResult();
                        Require(SpinWait.SpinUntil(() => !ReferenceEquals(beforeRemap, manager.CurrentSession), TimeSpan.FromSeconds(4)),
                            "The inventory replacement was not published while native application was held.");
                        Set(service, "_seekRestartGeneration", (long)Get(service, "_seekRestartGeneration")! + 1);
                    }
                    await remapping.WaitAsync(TimeSpan.FromSeconds(6));
                    await Until(() => service.ActiveSubtitleServerIndex == 4 && NativeSidMatches(4) &&
                        ((SemaphoreSlim)Get(service, "_subtitleReadyGuard")!).CurrentCount == 1);
                    Require(remapFiles.GetValueOrDefault(4) == remapPath && !remapFiles.ContainsKey(0) &&
                        mpv.GetPropertyDouble("sid") == remapSid && Equals(remapFile, Get(service, "_fileLoadGeneration")) &&
                        manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 77).Index == 0 &&
                        manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes && service.IsPaused && mpv.IsPaused,
                        "A post-publication seek lost the native remap or reused another subtitle's ordinal on retry.");
                }
                finally
                {
                    wire.HoldReplan.TrySetResult();
                    wire.HoldReplan = null; wire.ReplanEntered = null; wire.ReindexReadyInventory = false;
                }
                await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                await Until(() => Get(service, "_loadedMediaInitCts") == null);

                // Both announcements must survive a plan frozen before either
                // addition. Valid metadata must not merge into the outgoing plan.
                wire.DownloadedAvailable = false;
                using var first = JsonDocument.Parse("""{"session_id":"fixture","file_id":42,"subtitle_id":88,"track":{"track_id":"downloaded:88","combined_index":8,"source":"downloaded","delivery":"burn_in_only","unplayable":true}}""");
                using var second = JsonDocument.Parse("""{"session_id":"fixture","file_id":42,"subtitle_id":89,"track":{"track_id":"downloaded:89","combined_index":9,"source":"downloaded","delivery":"burn_in_only","unplayable":true}}""");
                Set(service, "_switchingContent", true);
                await (Task)Call(service, "ApplySubtitleReadyAsync", first.RootElement, "fixture")!;
                await (Task)Call(service, "ApplySubtitleReadyAsync", second.RootElement, "fixture")!;
                Require(!manager.CurrentSession!.SubtitleUrls.Any(row => row.Id is 88 or 89), "Busy announcements mutated the outgoing inventory.");
                await manager.ReplanQualityAsync("720p", 0);
                Require(!manager.CurrentSession!.SubtitleUrls.Any(row => row.Id is 88 or 89), "The frozen-plan control unexpectedly included an announcement.");
                await Load(0, 0);
                await Until(() => Get(service, "_loadedMediaInitCts") == null &&
                    manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 88) && manager.CurrentSession.SubtitleUrls.Any(row => row.Id == 89));

                // Invalidation recovery has no busy claim until its response
                // arrives. Also cover a manager replan with no UI busy flag at
                // all: a guard-waiting refresh must retry the replacement owner.
                foreach (var replanKind in new[] { "invalidation", "manager", "superseded-invalidation" })
                foreach (var needsReplan in new[] { false, true })
                {
                    wire.AdditionalDownloadedAvailable = false;
                    await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                    await Until(() => Get(service, "_loadedMediaInitCts") == null);
                    var replansBefore = wire.Replans.Count;
                    wire.HoldReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.ReplanEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var outgoingPlanId = manager.CurrentSession!.PlanId!;
                    Task recovering = replanKind == "invalidation"
                        ? (Task)Call(service, "HandlePlanInvalidationAsync",
                            new PlaybackPlanInvalidation(outgoingPlanId, "policy_changed"))!
                        : manager.ReplanQualityAsync("720p", 0);
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    if (replanKind == "superseded-invalidation")
                    {
                        // This invalidation becomes a no-op behind the quality
                        // replan: its finally must release payloads without a load.
                        var checking = (Task)Call(service, "HandlePlanInvalidationAsync",
                            new PlaybackPlanInvalidation(outgoingPlanId, "policy_changed"))!;
                        recovering = Task.WhenAll(recovering, checking);
                    }
                    Require(Get(service, "_switchingContent") is false && !service.IsLoading,
                        "The fixture did not exercise a replan without a busy flag.");
                    wire.AdditionalDownloadedAvailable = true;
                    using var duringReplan = JsonDocument.Parse(JsonSerializer.Serialize(new
                    {
                        session_id = "fixture", file_id = 42, subtitle_id = 78,
                        track = needsReplan ? null : new { track_id = "downloaded:78", combined_index = 3, source = "downloaded", delivery = "sidecar", url = subtitles.Origin + "/spa.srt" },
                    }));
                    var refreshing = (Task)Call(service, "ApplySubtitleReadyAsync", duringReplan.RootElement, "fixture")!;
                    try
                    {
                        Require(!manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 78),
                            "An announcement mutated the outgoing inventory during a non-busy replan.");
                        Require(wire.Replans.Count == replansBefore + 1,
                            "An announcement bypassed the in-flight replan guard.");
                    }
                    finally { wire.HoldReplan.TrySetResult(); }
                    await Task.WhenAll(recovering, refreshing).WaitAsync(TimeSpan.FromSeconds(8));
                    await Until(() => !service.IsLoading && Get(service, "_loadedMediaInitCts") == null &&
                        manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 78));
                    Require(manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78).Index == 3,
                        "A deferred announcement did not reach the replacement inventory.");
                    wire.HoldReplan = null; wire.ReplanEntered = null;
                }

                // B must wait for A's frozen response, then either merge into
                // its replacement or replan from that replacement (not S0).
                foreach (var needsReplan in new[] { false, true })
                {
                    wire.AdditionalDownloadedAvailable = false;
                    await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                    await Until(() => Get(service, "_loadedMediaInitCts") == null);
                    wire.HoldReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.ReplanEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var refreshing = (Task)Call(service, "ApplySubtitleReadyAsync", unresolved.RootElement, "fixture")!;
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    wire.AdditionalDownloadedAvailable = true;
                    using var concurrent = JsonDocument.Parse(JsonSerializer.Serialize(new
                    {
                        session_id = "fixture", file_id = 42, subtitle_id = 78,
                        track = needsReplan ? null : new { track_id = "downloaded:78", combined_index = 3, source = "downloaded", delivery = "sidecar", url = subtitles.Origin + "/spa.srt" },
                    }));
                    var queued = (Task)Call(service, "ApplySubtitleReadyAsync", concurrent.RootElement, "fixture")!;
                    try
                    {
                        Require(!queued.IsCompleted && !manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 78),
                            "An announcement bypassed the in-flight inventory publication.");
                    }
                    finally { wire.HoldReplan.TrySetResult(); }
                    await Task.WhenAll(refreshing, queued).WaitAsync(TimeSpan.FromSeconds(6));
                    Require(manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 78),
                        "A concurrent announcement was lost when its outgoing session was replaced.");
                    wire.HoldReplan = null; wire.ReplanEntered = null;
                }
                Require(manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes && service.IsPaused && mpv.IsPaused,
                    "Deferred/concurrent announcements changed the viewer's choice, preference or pause intent.");

                // Legacy inventory refreshes use the envelope owner too.
                var legacySession = manager.CurrentSession!;
                legacySession.ProtocolVersion = 2; wire.DownloadedAvailable = true;
                using var legacy = JsonDocument.Parse("""{"file_id":42,"subtitle_id":77}""");
                try
                {
                    await (Task)Call(service, "ApplySubtitleReadyAsync", legacy.RootElement, "fixture")!;
                    Require(legacySession.SubtitleUrls.Any(row => row.Id == 77), "An envelope-only v2 announcement did not refresh stored subtitles.");
                }
                finally { legacySession.ProtocolVersion = 3; }
                // A reload or a refused busy claim can finish without replacing
                // CurrentSession. Its FILE_LOADED/release happens before the held
                // refresh returns, so the announcement must arrange its own retry.
                using var sameSessionReady = JsonDocument.Parse("""{"session_id":"fixture","file_id":42,"subtitle_id":78}""");
                foreach (var transportChange in new[] { "reload", "busy-release" })
                {
                    wire.AdditionalDownloadedAvailable = false;
                    await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                    await Until(() => Get(service, "_loadedMediaInitCts") == null);
                    var outgoing = manager.CurrentSession;
                    var replansBefore = wire.Replans.Count;
                    wire.HoldReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.ReplanEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var refreshing = (Task)Call(service, "ApplySubtitleReadyAsync", sameSessionReady.RootElement, "fixture")!;
                    await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    try
                    {
                        wire.AdditionalDownloadedAvailable = true;
                        if (transportChange == "reload")
                        {
                            await Load(0, 0);
                            await Until(() => Get(service, "_loadedMediaInitCts") == null);
                        }
                        else
                        {
                            var busy = Call(service, "BeginPlaybackBusy", true, null)!;
                            Require(Call(service, "ReleasePlaybackBusy", busy, null) != null,
                                "The fixture's temporary busy claim was not released.");
                        }
                        Require(ReferenceEquals(outgoing, manager.CurrentSession) && !service.IsLoading &&
                            Get(service, "_switchingContent") is false,
                            "The fixture did not finish its transport change on the same idle session.");
                    }
                    finally { wire.HoldReplan.TrySetResult(); }
                    var transportFile = Get(service, "_fileLoadGeneration");
                    await refreshing.WaitAsync(TimeSpan.FromSeconds(6));
                    await Until(() => wire.Replans.Count == replansBefore + 2 &&
                        manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 78));
                    Require(manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78).Index == 3 &&
                        Equals(transportFile, Get(service, "_fileLoadGeneration")) &&
                        manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes && service.IsPaused && mpv.IsPaused,
                        "A same-session transport change dropped the announcement or changed playback intent on retry.");
                    wire.HoldReplan = null; wire.ReplanEntered = null;
                }
                // A stored-list read can straddle an announcement for the same file.
                // A row appended during the GET must not abort the refresh, and one
                // rewritten while the refresh waits for the commit gates must not be
                // overwritten from the facts read before that merge.
                foreach (var rewrite in new[] { false, true })
                {
                    wire.DownloadedAvailable = rewrite; wire.AdditionalDownloadedAvailable = false;
                    await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                    await Until(() => Get(service, "_loadedMediaInitCts") == null);
                    wire.DownloadedAvailable = wire.AdditionalDownloadedAvailable = true;
                    if (rewrite) await service.RefreshSubtitlesAfterAiAsync(42);
                    var straddled = manager.CurrentSession!;
                    var storedOnly = straddled.SubtitleUrls.SingleOrDefault(row => row.Id == 78);
                    Require(rewrite ? (storedOnly is { Index: 3, TrackId: null, Delivery: null })
                        : storedOnly == null && !straddled.SubtitleUrls.Any(row => row.Id == 77),
                        "The straddled-read regression did not establish its stored-list-only or absent rows.");
                    wire.HoldStoredList = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.StoredListEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var reading = Task.Run(() => service.RefreshSubtitlesAfterAiAsync(42));
                    try
                    {
                        await wire.StoredListEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        lock (Get(service, "_subtitleSyncGate")!)
                        lock (Get(service, "_subtitleTrackStateLock")!)
                        {
                            // Let the refresh take its response up to these gates first.
                            if (rewrite) { wire.HoldStoredList.TrySetResult(); Thread.Sleep(250); }
                            Require(manager.TryApplySubtitleTrack(straddled, new()
                            {
                                StoredSubtitleId = 78, TrackId = "downloaded:78", CombinedIndex = 3,
                                Source = "downloaded", Delivery = "sidecar", Url = subtitles.Origin + "/spa.srt",
                            }), "The announcement did not merge while the stored-list read was in flight.");
                        }
                    }
                    finally { wire.HoldStoredList.TrySetResult(); }
                    await reading.WaitAsync(TimeSpan.FromSeconds(6));
                    wire.HoldStoredList = null; wire.StoredListEntered = null;
                    var merged = straddled.SubtitleUrls.Single(row => row.Id == 78);
                    Require(ReferenceEquals(straddled, manager.CurrentSession) && (!rewrite || ReferenceEquals(merged, storedOnly)) &&
                        merged.Index == 3 && merged.TrackId == "downloaded:78" && merged.Delivery == "sidecar" &&
                        merged.Url == subtitles.Origin + "/spa.srt" && !merged.Unplayable,
                        "A stored-list refresh downgraded or retired a row announced while its read was in flight.");
                    Require(rewrite || straddled.SubtitleUrls.Single(row => row.Id == 77) is { Index: 2, Unplayable: false, Url: "/stream/fixture/subtitles/2" },
                        "An announcement merged during the stored-list read aborted that refresh.");
                    Require(manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes && service.IsPaused && mpv.IsPaused,
                        "A straddled stored-list refresh changed the viewer's choice, preference or pause intent.");
                }
                // The list can also be read before the announced subtitle is stored
                // in it. Its absence there cannot retire the row that merged while
                // the read was in flight, through either refresh entry point.
                foreach (var ownerRefresh in new[] { false, true })
                {
                    wire.DownloadedAvailable = wire.AdditionalDownloadedAvailable = false;
                    await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                    await Until(() => Get(service, "_loadedMediaInitCts") == null);
                    var unlisted = manager.CurrentSession!;
                    Require(!unlisted.SubtitleUrls.Any(row => row.Id is 77 or 78),
                        "The unlisted-announcement regression did not establish an inventory without downloads.");
                    wire.DownloadedAvailable = true;
                    wire.HoldStoredList = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.StoredListEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var unlistedRead = Task.Run(() => ownerRefresh
                        ? (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!
                        : service.RefreshSubtitlesAfterAiAsync(42));
                    try
                    {
                        await wire.StoredListEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        lock (Get(service, "_subtitleSyncGate")!)
                        lock (Get(service, "_subtitleTrackStateLock")!)
                            Require(manager.TryApplySubtitleTrack(unlisted, new()
                            {
                                StoredSubtitleId = 78, TrackId = "downloaded:78", CombinedIndex = 3,
                                Source = "downloaded", Delivery = "sidecar", Url = subtitles.Origin + "/spa.srt",
                            }), "The announcement did not merge while the stored-list read was in flight.");
                    }
                    finally { wire.HoldStoredList.TrySetResult(); }
                    await unlistedRead.WaitAsync(TimeSpan.FromSeconds(6));
                    wire.HoldStoredList = null; wire.StoredListEntered = null;
                    var announced = unlisted.SubtitleUrls.SingleOrDefault(row => row.Id == 78);
                    Require(ReferenceEquals(unlisted, manager.CurrentSession) &&
                        announced is { Index: 3, TrackId: "downloaded:78", Delivery: "sidecar", Unplayable: false } &&
                        announced.Url == subtitles.Origin + "/spa.srt",
                        "A stored list read before an announcement landed retired the announced row.");
                    Require(unlisted.SubtitleUrls.SingleOrDefault(row => row.Id == 77) is { Index: 2, Unplayable: false, Url: "/stream/fixture/subtitles/2" },
                        "A row announced during the stored-list read aborted or displaced that refresh.");
                    Require(manager.CaptureLocalSubtitleIntent() == intent && wire.PreferenceWrites == writes && service.IsPaused && mpv.IsPaused,
                        "A stored-list refresh that kept an announced row changed the viewer's choice, preference or pause intent.");
                }
                // An announcement without track metadata replans. Its inventory can
                // replace the session while the refresh meant to select that stored
                // subtitle is still reading the list. Once the inventory is applied
                // in place the refresh must start over on it, not drop the selection.
                // Cover the AI-completion and the Add Subtitles entry points.
                foreach (var ownerRefresh in new[] { false, true })
                {
                    wire.DownloadedAvailable = wire.AdditionalDownloadedAvailable = false;
                    await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                    await Until(() => Get(service, "_loadedMediaInitCts") == null);
                    var retiredSession = manager.CurrentSession!;
                    var retiredFile = Get(service, "_fileLoadGeneration");
                    Require(!retiredSession.SubtitleUrls.Any(row => row.Id == 77),
                        "The superseded-refresh regression did not establish an inventory without the stored subtitle.");
                    wire.DownloadedAvailable = true;
                    wire.HoldStoredList = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.StoredListEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var selecting = Task.Run(() => ownerRefresh
                        ? (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, (long?)77, null, null, null)!
                        : service.RefreshSubtitlesAfterAiAsync(42, 77));
                    try
                    {
                        await wire.StoredListEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                        await ((Task)Call(service, "ApplySubtitleReadyAsync", unresolved.RootElement, "fixture")!).WaitAsync(TimeSpan.FromSeconds(6));
                        Require(!ReferenceEquals(retiredSession, manager.CurrentSession) &&
                            manager.CurrentSession!.SubtitleUrls.Any(row => row.Id == 77) &&
                            Equals(retiredFile, Get(service, "_fileLoadGeneration")),
                            "The announcement did not replace the reading refresh's session in place.");
                    }
                    finally { wire.HoldStoredList.TrySetResult(); }
                    await selecting.WaitAsync(TimeSpan.FromSeconds(6));
                    wire.HoldStoredList = null; wire.StoredListEntered = null;
                    await Until(() => service.ActiveSubtitleServerIndex == 2 && NativeSidMatches(2));
                    var selectedRow = manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 77);
                    Require(selectedRow.Index == 2 && selectedRow.TrackId == "downloaded:77" && !selectedRow.Unplayable &&
                        Equals(retiredFile, Get(service, "_fileLoadGeneration")),
                        "A refresh superseded by an announcement's inventory did not select its stored subtitle on that inventory.");
                    // The restarted refresh is detached. Let it finish, then return
                    // the next case to the first external subtitle.
                    var restarted = (HashSet<Action<PlaybackStartResponse>>)Get(service, "_storedSubtitleListRefreshes")!;
                    await Until(() => { lock (Get(service, "_subtitleReadyRefreshLock")!) return restarted.Count == 0; });
                    manager.RecordLocalSubtitleSelection(0);
                }
                // A stored-list-only download owns valid local bytes even when
                // a later list order disagrees with the canonical reserved row.
                wire.DownloadedAvailable = true; wire.AdditionalDownloadedAvailable = false;
                await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                wire.AdditionalDownloadedAvailable = true;
                await service.RefreshSubtitlesAfterAiAsync(42);
                var playingDownload = manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78);
                Require(playingDownload.Index == 3 && !playingDownload.Unplayable &&
                    playingDownload.TrackId == null && playingDownload.Delivery == null,
                    "The playing download regression did not establish a stored-list-only row.");
                playingDownload.Url = subtitles.Origin + "/spa.srt";
                await service.SetSubtitleTrackAndPersistAsync(4, playingDownload.Language, playingDownload);
                await Until(() => service.ActiveSubtitleServerIndex == 3 && NativeSidMatches(3));
                var playingSid = mpv.GetPropertyDouble("sid");
                var playingIntent = manager.CaptureLocalSubtitleIntent();
                var playingWrites = wire.PreferenceWrites;
                var playingFile = Get(service, "_fileLoadGeneration");
                var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
                var playingPath = files[playingDownload.Index];
                foreach (var ownerRefresh in new[] { false, true })
                {
                    wire.ReverseDownloadedOrder = false;
                    await service.RefreshSubtitlesAfterAiAsync(42);
                    wire.ReverseDownloadedOrder = true;
                    if (ownerRefresh) await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!;
                    else await service.RefreshSubtitlesAfterAiAsync(42);
                    Require(ReferenceEquals(playingDownload, manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78)) &&
                        playingDownload.Index == 4 && !playingDownload.Unplayable && playingDownload.Url == "" &&
                        service.ActiveSubtitleServerIndex == 4 && NativeSidMatches(4) && mpv.GetPropertyDouble("sid") == playingSid &&
                        ReferenceEquals(playingDownload, Get(service, "_executedSubtitleTrack")) &&
                        files.GetValueOrDefault(4) == playingPath && File.Exists(playingPath),
                        "An uncertain stored-list ordinal retired the playing download or its loaded SID/bytes.");
                    Require(manager.CaptureLocalSubtitleIntent() == playingIntent && wire.PreferenceWrites == playingWrites &&
                        Equals(playingFile, Get(service, "_fileLoadGeneration")) && service.IsPaused && mpv.IsPaused,
                        "Re-indexing a playing download changed playback intent, preferences or transport.");
                    var menu = Call(service, "CaptureSubtitleMenuSnapshot")!;
                    Require(((IReadOnlyList<(SubtitleTrackInfo Track, bool CanSelect)>)menu.GetType().GetProperty("Entries")!.GetValue(menu)!)
                        .Single(row => ReferenceEquals(row.Track, playingDownload)).CanSelect,
                        "A playing download kept for its loaded SID/bytes was published as unavailable.");
                }
                // Those loaded bytes are now the row's only route. Off and back must
                // reuse the cached SID without a replan or another download.
                var playingReplans = wire.Replans.Count;
                Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!,
                    "Subtitles could not be turned off while a re-indexed download was playing.");
                await Until(() => service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0);
                Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 4, true)!,
                    "A download kept for its loaded SID/bytes could not be selected again.");
                await Until(() => service.ActiveSubtitleServerIndex == 4 && NativeSidMatches(4));
                Require(mpv.GetPropertyDouble("sid") == playingSid && files.GetValueOrDefault(4) == playingPath &&
                    ReferenceEquals(playingDownload, Get(service, "_executedSubtitleTrack")) &&
                    wire.Replans.Count == playingReplans && Equals(playingFile, Get(service, "_fileLoadGeneration")),
                    "Re-selecting a cached download replanned, reloaded or replaced its loaded SID/bytes.");
                wire.ReverseDownloadedOrder = false;
                wire.AdditionalDownloadedAvailable = false;
                manager.RecordLocalSubtitleSelection(0);
                await manager.ReplanQualityAsync("720p", 0); await Load(0, 0);
                await Until(() => Get(service, "_loadedMediaInitCts") == null);
                var planOnlyDownload = manager.CurrentSession!.SubtitleUrls.Single(row => row.Source == "downloaded");
                // Match the pinned playback_start_opaque_ids.json shape: a
                // downloaded sidecar has an opaque TrackId but no stored keys.
                // Remove only the extra identities injected by this fake server.
                planOnlyDownload.Id = null; planOnlyDownload.SyncKey = null;
                await service.SetSubtitleTrackAndPersistAsync(planOnlyDownload.Index + 1, planOnlyDownload.Language, planOnlyDownload);
                await Until(() => service.ActiveSubtitleServerIndex == planOnlyDownload.Index && NativeSidMatches(planOnlyDownload.Index));
                var planOnlySid = mpv.GetPropertyDouble("sid");
                var planOnlyIntent = manager.CaptureLocalSubtitleIntent(); var planOnlyWrites = wire.PreferenceWrites;
                var planOnlyFile = Get(service, "_fileLoadGeneration"); var planOnlyUrl = planOnlyDownload.Url;
                var planOnlyPath = files[planOnlyDownload.Index];
                foreach (var ownerRefresh in new[] { false, true })
                {
                    if (ownerRefresh) await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!;
                    else await service.RefreshSubtitlesAfterAiAsync(42);
                    var refreshedDownload = manager.CurrentSession!.SubtitleUrls.Where(row => row.Source == "downloaded").ToArray();
                    Require(refreshedDownload.Length == 1 && ReferenceEquals(planOnlyDownload, refreshedDownload[0]) &&
                        planOnlyDownload.Id == null && planOnlyDownload.SyncKey == null && !planOnlyDownload.Unplayable &&
                        planOnlyDownload.Url == planOnlyUrl && planOnlyDownload.Delivery == "sidecar" &&
                        ReferenceEquals(planOnlyDownload, Get(service, "_executedSubtitleTrack")) &&
                        NativeSidMatches(planOnlyDownload.Index) && mpv.GetPropertyDouble("sid") == planOnlySid &&
                        files.GetValueOrDefault(planOnlyDownload.Index) == planOnlyPath && File.Exists(planOnlyPath),
                        "A stored-list refresh replaced a plan-only download with an unplayable duplicate or retired its SID/bytes.");
                    Require(manager.CaptureLocalSubtitleIntent() == planOnlyIntent && wire.PreferenceWrites == planOnlyWrites &&
                        Equals(planOnlyFile, Get(service, "_fileLoadGeneration")) && service.IsPaused && mpv.IsPaused,
                        "Refreshing a plan-only download changed playback intent, preferences or transport.");
                }
                // The stored-list shape of that playing download (no Delivery, a
                // positional URL) is replaced in place by its canonical plan row.
                // The applied SID must stay mapped and visible, but stale.
                var canonicalSession = manager.CurrentSession!;
                var storedShape = new PlaybackStartResponse
                {
                    SessionId = canonicalSession.SessionId, MediaFileId = canonicalSession.MediaFileId,
                    SubtitleUrls = [.. canonicalSession.SubtitleUrls.Select(row => !ReferenceEquals(row, planOnlyDownload) ? row : new SubtitleTrackInfo
                    {
                        TrackId = row.TrackId, Index = row.Index, MediaFileId = row.MediaFileId, Language = row.Language,
                        Codec = row.Codec, Label = row.Label, Source = row.Source, Url = $"/stream/fixture/subtitles/{row.Index}",
                    })],
                };
                lock (Get(service, "_subtitleSyncGate")!)
                lock (Get(service, "_subtitleTrackStateLock")!)
                    Call(service, "AdoptSubtitleInventoryInPlace", storedShape, canonicalSession);
                Require(service.ActiveSubtitleServerIndex == planOnlyDownload.Index && NativeSidMatches(planOnlyDownload.Index) &&
                    mpv.GetPropertyDouble("sid") == planOnlySid && ReferenceEquals(planOnlyDownload, Get(service, "_executedSubtitleTrack")) &&
                    ((HashSet<int>)Get(service, "_staleExternalSubtitleIndices")!).Contains(planOnlyDownload.Index) &&
                    !files.ContainsKey(planOnlyDownload.Index),
                    "Adopting a canonical row with a changed Url/Delivery dropped the playing SID or left its old bytes reusable.");
                // A different stored subtitle sorted onto that plan-only row's
                // ordinal is not the row. It must stay visible as its own entry,
                // and the row's positional URL can no longer be trusted.
                wire.AdditionalDownloadedAvailable = true; wire.ReverseDownloadedOrder = true;
                foreach (var ownerRefresh in new[] { false, true })
                {
                    if (ownerRefresh) await (Task)Call(service, "RefreshSubtitlesAfterAiForOwnerAsync", 42, null, null, null, null)!;
                    else await service.RefreshSubtitlesAfterAiAsync(42);
                    var displacing = manager.CurrentSession!.SubtitleUrls.Single(row => row.Id == 78);
                    Require(!ReferenceEquals(displacing, planOnlyDownload) && displacing.Language == "spa" &&
                        displacing.Index != planOnlyDownload.Index && displacing.Unplayable && displacing.Url == "",
                        "A stored entry sorted onto a plan-only row's ordinal was swallowed or made executable.");
                    Require(manager.CurrentSession.SubtitleUrls.Contains(planOnlyDownload) && planOnlyDownload.Id == null &&
                        planOnlyDownload.SyncKey == null && planOnlyDownload.Language == "ita" &&
                        planOnlyDownload.Url == "" && !planOnlyDownload.Unplayable &&
                        ReferenceEquals(planOnlyDownload, Get(service, "_executedSubtitleTrack")) &&
                        NativeSidMatches(planOnlyDownload.Index) && mpv.GetPropertyDouble("sid") == planOnlySid,
                        "A displaced plan-only row kept a stale positional URL or lost its playing SID.");
                    Require(manager.CaptureLocalSubtitleIntent() == planOnlyIntent && wire.PreferenceWrites == planOnlyWrites &&
                        Equals(planOnlyFile, Get(service, "_fileLoadGeneration")) && service.IsPaused && mpv.IsPaused,
                        "Displacing a plan-only download changed playback intent, preferences or transport.");
                }
                wire.ReverseDownloadedOrder = false; wire.AdditionalDownloadedAvailable = false;
                var accepted = manager.CurrentSession;
                var nativeFile = Get(service, "_fileLoadGeneration");
                wire.HoldReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.ReplanEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var held = (Task)Call(service, "ApplySubtitleReadyAsync", unresolved.RootElement, "fixture")!;
                await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                api.SetProfile("replacement"); wire.HoldReplan.TrySetResult(); await held;
                Require(ReferenceEquals(accepted, manager.CurrentSession) && Equals(nativeFile, Get(service, "_fileLoadGeneration")),
                    "A retired announcement owner adopted or loaded its late refresh.");
                Program.Log("PASS: canonical subtitle_ready identity/delivery, no-select native SID/preferences, metadata non-downgrade, session guard, authoritative no-track replan and late-owner retirement.");
                return;
            }
            var spanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
            await service.SetSubtitleTrackAndPersistAsync(2, "spa", spanish);
            await Until(() => service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") > 0);
            service.SetPaused(true);
            await manager.ReplanQualityAsync("480p-low", 12);
            Require(wire.Replans.Last().GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString() == "sub:42:1", "Actual local subtitle pick did not reach quality replan.");
            await Load(1, 12);
            Require(service.IsPaused && mpv.GetPropertyFlag("pause") && Math.Abs(mpv.Position - 12) < 2, "Quality reload changed pause or canonical position.");

            // The published outgoing menu can queue intent before adoption,
            // but cannot execute or save it yet. English -> Spanish must leave
            // only the latest owner to rebind to the replacement's index 0.
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            var preferencesBeforeSwitch = wire.PreferenceWrites;
            var spanishSid = mpv.GetPropertyDouble("sid");
            Set(service, "_switchingContent", true);
            Set(service, "_subtitleVersionSwitchActive", true);
            Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!,
                "The published outgoing menu could not queue a choice while a version switch was preparing.");
            var supersededChoice = Get(service, "_subtitleSelectionOperation");
            Require(manager.SubtitleSelectionForLoad == 0 && Get(service, "_pendingSubtitlePreferenceIndex") is 0 &&
                wire.PreferenceWrites == preferencesBeforeSwitch && mpv.GetPropertyDouble("sid") == spanishSid,
                "A queued outgoing-menu choice executed or saved before adoption.");
            Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!,
                "A newer Spanish choice could not supersede the queued English choice.");
            var latestChoice = Get(service, "_subtitleSelectionOperation");
            Require(latestChoice != null && !ReferenceEquals(latestChoice, supersededChoice) &&
                manager.SubtitleSelectionForLoad == 1 && Get(service, "_pendingSubtitlePreferenceIndex") is 1 &&
                wire.PreferenceWrites == preferencesBeforeSwitch && mpv.GetPropertyDouble("sid") == spanishSid,
                "The latest queued choice lost ownership or changed native captions/preferences prematurely.");
            // A previously reserved raw index must not outrank rebinding by
            // identity: index 1 means English on version 44, not Spanish.
            Set(service, "_pendingInitialServerSubtitleIndex", 1);
            Set(service, "_pendingInitialSubtitleFileId", 42);
            await manager.StartReplacementSessionAsync(44, 12);
            Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!,
                "The outgoing menu was interpreted against the newly adopted file.");
            Require(ReferenceEquals(latestChoice, Get(service, "_subtitleSelectionOperation")) &&
                manager.SubtitleSelectionForLoad == 0 && wire.PreferenceWrites == preferencesBeforeSwitch &&
                mpv.GetPropertyDouble("sid") == spanishSid,
                "A stale menu row replaced the queued Spanish owner after adoption.");
            await Load(0, 12); // Rebind the unique Spanish row across file-local IDs.
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            Require(manager.CaptureLocalSubtitleIntent().Track?.TrackId == "sub:42:1" &&
                Get(service, "_pendingInitialServerSubtitleIndex") == null &&
                Get(service, "_pendingSubtitlePreferenceIndex") == null &&
                wire.PreferenceWrites == preferencesBeforeSwitch + 1,
                "The replacement did not execute and save only the latest mapped Spanish choice exactly once.");
            await manager.StartReplacementSessionAsync(42, 12); await Load(1, 12);
            await manager.StartReplacementSessionAsync(43, 13); await Load(-1, 13);
            await manager.StartReplacementSessionAsync(42, 14); await Load(1, 14);
            await manager.StartReplacementSessionAsync(44, 15); await Load(0, 15);
            var replansBeforeOff = wire.Replans.Count;
            await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            await manager.ReplanQualityAsync("360p", 16); await Load(-1, 16);
            Require(wire.Replans.Count == replansBeforeOff + 2 &&
                wire.Replans[replansBeforeOff].GetProperty("operation").GetString() == "track_change" &&
                !wire.Replans[replansBeforeOff].GetProperty("selected_tracks").TryGetProperty("subtitle", out _) &&
                wire.Replans[^1].GetProperty("operation").GetString() == "quality_change" &&
                manager.CurrentSession!.SubtitleMode == "off", "Explicit Off never reached the server before quality change.");
            Require(subtitles.Requests > 0, "Native player never fetched the served subtitle bytes.");
            Require(wire.PreferenceWrites == 3, "Automatic version fallback/adoption saved a viewer preference.");

            // Model the next-item boundary: PlayCoreAsync invalidates the old
            // menu, and StartSessionAsync (not replacement) resets track intent.
            // Old index 1 is Spanish on episode A but English on episode B.
            await manager.StartSessionAsync(42); await Load(-1, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            Property(service, "ContentId", "next-episode");
            manager.UseWatchDetail(new() { ContentId = "next-episode", SeriesId = "series", Versions = [Version(44)] });
            Property(service, "Versions", manager.WatchDetail!.Versions);
            Set(service, "_subtitleMenuPending", true);
            Set(service, "_subtitleMenuFileId", null);
            Set(service, "_subtitleVersionSwitchActive", false);
            Set(service, "_switchingContent", true);
            await manager.StartSessionAsync(44);
            Call(service, "ResolveInitialSubtitleSelection", manager.WatchDetail, Version(44), manager.CurrentSession, null, -1, null);
            Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)! &&
                manager.SubtitleSelectionForLoad == -1 && Get(service, "_pendingSubtitlePreferenceIndex") == null && wire.PreferenceWrites == 3,
                "A next-item load interpreted the outgoing episode's menu index against the new inventory.");
            await Load(-1, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null && !(bool)Get(service, "_subtitleMenuPending")!);
            Require(!(bool)Get(service, "_subtitleMenuPending")!, "Publishing the next episode's inventory did not enable its menu.");
            Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)! &&
                service.ActiveSubtitleServerIndex == 0 && wire.PreferenceWrites == 4 &&
                manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0).Language == "spa",
                "The newly published next-item menu did not select its own Spanish row.");

            manager.WatchDetail!.Versions.Add(Version(42));
            Property(service, "Versions", manager.WatchDetail.Versions);
            foreach (var entry in new[] { "overlay", "osc", "cast" })
            {
                var currentSpanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Language == "spa");
                await service.SetSubtitleTrackAndPersistAsync(currentSpanish.Index + 1, currentSpanish.Language, currentSpanish);
                var writes = wire.PreferenceWrites;
                var oldSid = mpv.GetPropertyDouble("sid");
                Set(service, "_switchingContent", true);
                Set(service, "_subtitleVersionSwitchActive", true);
                if (entry == "overlay") await service.SetSubtitleTrackAndPersistAsync(0, null, null);
                else if (entry == "osc")
                    Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!,
                        "OSC Off was refused during a version switch.");
                else await service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track" });
                Require(manager.SubtitleSelectionForLoad == -1 && wire.PreferenceWrites == writes && mpv.GetPropertyDouble("sid") == oldSid,
                    "Off during a version switch was not recorded and deferred without a premature preference write.");
                var nextFile = manager.CurrentSession.MediaFileId == 44 ? 42 : 44;
                await manager.StartReplacementSessionAsync(nextFile, 12);
                await Load(-1, 12);
                await Until(() => Get(service, "_loadedMediaInitCts") == null && wire.PreferenceWrites == writes + 1);
                Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                    wire.PreferenceWrites == writes + 1 && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                    "The replacement did not execute Off and save its preference exactly once.");
            }
            // New downloads first arrive without a plan TrackId. Then a later
            // subtitle_ready refresh must preserve the accepted row's identity.
            await manager.StartSessionAsync(42); await Load(-1, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            wire.DownloadedAvailable = true;
            await service.RefreshSubtitlesAfterAiAsync(42);
            var downloaded = manager.CurrentSession!.SubtitleUrls.Single(t => t.Source == "downloaded");
            Require(downloaded.Id == 77 && downloaded.TrackId == null && downloaded.SyncKey == "stored-77",
                "The download fixture did not establish an ID-less stored subtitle.");
            downloaded.Url = subtitles.Origin + "/ita.srt"; // Serve refresh bytes without external networking.
            await service.SetSubtitleTrackAndPersistAsync(downloaded.Index + 1, downloaded.Language, downloaded);
            var downloadedIntent = manager.CaptureLocalSubtitleIntent();
            await manager.ReplanQualityAsync("720p", 12); await Load(2, 12);
            Require(manager.CurrentSession!.SelectedSubtitleTrackId == "downloaded:77" &&
                manager.SubtitleSelectionForLoad == 2 && manager.CaptureLocalSubtitleIntent() == downloadedIntent &&
                service.IsPaused && Math.Abs(mpv.Position - 12) < 2,
                "A quality replan lost the newly downloaded subtitle across the TrackId boundary.");
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            var plannedDownload = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == 2);
            await service.SetSubtitleTrackAndPersistAsync(3, plannedDownload.Language, plannedDownload);
            downloadedIntent = manager.CaptureLocalSubtitleIntent();
            await service.RefreshSubtitlesAfterAiAsync(42);
            downloaded = manager.CurrentSession.SubtitleUrls.Single(t => t.Source == "downloaded");
            Require(downloaded.TrackId == "downloaded:77" && downloaded.SyncKey == "stored-77" && downloaded.Id == 77,
                "A subtitle_ready refresh discarded the plan's stored subtitle identity.");
            await manager.ReplanAudioAsync(1, 13); await Load(2, 13);
            Require(wire.Replans[^1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString() == "downloaded:77" &&
                manager.SubtitleSelectionForLoad == 2 && manager.CaptureLocalSubtitleIntent() == downloadedIntent &&
                service.IsPaused && Math.Abs(mpv.Position - 13) < 2,
                "An audio replan lost the selected downloaded subtitle after refresh.");
            wire.DownloadedAvailable = false;

            // A local CC cycle is explicit viewer intent, but not a saved
            // preference change. Cycled Off must survive the next Auto grant.
            await manager.StartSessionAsync(42, subtitleTrackIndex: 0);
            var automaticWatch = new WatchDetailResponse { EffectiveSubtitleMode = "always", EffectiveSubtitleLanguage = "eng" };
            Call(service, "ResolveInitialSubtitleSelection", automaticWatch, Version(42), manager.CurrentSession, null, null, null);
            await Load(0, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            var cycleWrites = wire.PreferenceWrites;
            var cycleReplans = wire.Replans.Count;
            Require(manager.IsSubtitleSelectionAutomatic, "Cycle fixture did not establish startup Auto.");
            Call(service, "CycleSubtitleBackward");
            Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                !manager.IsSubtitleSelectionAutomatic && !manager.CaptureLocalSubtitleIntent().IsAutomatic &&
                manager.SubtitleSelectionForLoad == -1 && wire.PreferenceWrites == cycleWrites,
                "The local caption cycle did not record explicit Off without saving a preference.");
            wire.AutomaticSubtitleSelection = 0;
            await manager.StartReplacementSessionAsync(44, 0);
            Require(wire.Replans.Count == cycleReplans + 1 && manager.SubtitleSelectionForLoad == -1 &&
                !manager.IsSubtitleSelectionAutomatic &&
                wire.Replans[^1].GetProperty("operation").GetString() == "track_change" &&
                !wire.Replans[^1].GetProperty("selected_tracks").TryGetProperty("subtitle", out _) &&
                manager.CurrentSession!.SubtitleMode == "off",
                "A version switch lost cycled explicit Off to the new file's Auto grant.");
            await Load(-1, 0);
            Require(wire.PreferenceWrites == cycleWrites && mpv.IsPaused,
                "Local caption cycling/version adoption saved a preference or resumed playback.");
            Program.Log("PASS: actual libmpv sid and PlayerService adopt subtitle identity across quality/version changes, refuse outgoing next-item indices until publication, retain explicit local caption cycling across Auto grants and persist only executed viewer choices.");
        }
        finally
        {
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }

        async Task Load(int expected, double position)
        {
            var type = typeof(PlayerService).GetNestedType("PreparedPlaybackTransport", BindingFlags.NonPublic)!;
            var prepared = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [PlaybackTransportPlanner.Plan("direct", wave), wave, 0d, position, 0d, (double?)60d, true], null)!;
            Set(service, "_requestedMediaFileId", manager.CurrentSession!.MediaFileId);
            Call(service, "ApplyPreparedTransport", prepared);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready() => loaded.TrySetResult(); service.ContentLoaded += Ready;
            try
            {
                Call(service, "BeginMpvLoad", prepared, true);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await Until(() => service.ActiveSubtitleServerIndex == expected &&
                    (expected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : NativeSidMatches(expected)));
                Call(service, "StopPlaybackStallWatchdog");
            }
            finally { service.ContentLoaded -= Ready; }
        }
        bool NativeSidMatches(int expected)
        {
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            lock (Get(service, "_subtitleTrackStateLock")!)
                return map.TryGetValue(expected, out var sid) && mpv.GetPropertyDouble("sid") == sid && sid > 0;
        }
    }

    private static FileVersion Version(int file)
    {
        var spanishFirst = file == 44;
        return new()
        {
            FileId = file, Duration = 60, EffectiveAudioTrackIndex = 1,
            AudioTracks = [new() { Language = "en", Default = false }, new() { Language = "jpn", Default = true }],
            SubtitleTracks = file == 43 ? [new() { Index = 0, Language = "eng", Codec = "srt", External = true }]
                : [new() { Index = 0, Language = spanishFirst ? "spa" : "eng", Codec = "srt", External = true }, new() { Index = 1, Language = spanishFirst ? "eng" : "spa", Codec = "srt", External = true }],
        };
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native subtitle sid/adoption did not settle."); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private sealed class SubtitleServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public int Requests;
        public SubtitleServer()
        {
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext request;
                    try { request = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    Interlocked.Increment(ref Requests);
                    var media = request.Request.Url!.AbsolutePath == "/media.wav";
                    var bytes = media ? File.ReadAllBytes(Path.Combine(Program.ResultDirectory, "oct24-subtitle-adoption.wav"))
                        : Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nNative subtitle adoption fixture\n");
                    request.Response.ContentType = media ? "audio/wav" : "application/x-subrip"; request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                }
            });
        }
        public void Dispose() => _listener.Close();
    }
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        private int _file = 42, _subtitle = 0;
        public List<JsonElement> Replans = [];
        public int PreferenceWrites;
        public int? AutomaticSubtitleSelection;
        public bool DownloadedAvailable;
        public bool AdditionalDownloadedAvailable;
        public bool ReverseDownloadedOrder;
        public bool ReindexReadyInventory;
        public TaskCompletionSource? HoldReplan, ReplanEntered;
        public TaskCompletionSource? HoldStoredList, StoredListEntered;
        private string SubtitleId => _subtitle == 2 ? "downloaded:77" : $"sub:{_file}:{_subtitle}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "oct24-adoption.invalid") throw new InvalidOperationException("Native adoption fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("-prefs/", StringComparison.Ordinal)) { PreferenceWrites++; return Json("{}"); }
            if (path == "/api/v2/subtitles/42" && DownloadedAvailable)
            {
                // Freeze the list at request entry, as for a held plan below.
                var stored = AdditionalDownloadedAvailable
                    ? ReverseDownloadedOrder
                        ? """{"subtitles":[{"id":78,"language":"spa","format":"srt","title":"New translation"},{"id":77,"language":"ita","format":"srt","title":"Italian downloaded"}]}"""
                        : """{"subtitles":[{"id":77,"language":"ita","format":"srt","title":"Italian downloaded"},{"id":78,"language":"spa","format":"srt","title":"New translation"}]}"""
                    : """{"subtitles":[{"id":77,"language":"ita","format":"srt","title":"Italian downloaded"}]}""";
                StoredListEntered?.TrySetResult();
                if (HoldStoredList != null) await HoldStoredList.Task.WaitAsync(ct);
                return Json(stored);
            }
            if (path.Contains("/settings/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = doc.RootElement;
            // Freeze inventory at request entry so held responses cannot see
            // subtitles announced while that request was in flight.
            var downloadedAvailable = DownloadedAvailable;
            var additionalDownloadedAvailable = AdditionalDownloadedAvailable;
            var reindexReadyInventory = ReindexReadyInventory;
            if (path.EndsWith("/start"))
            {
                _file = int.Parse(body.GetProperty("file_id").GetString()!);
                _subtitle = body.TryGetProperty("subtitle_track_index", out var initial) ? initial.GetInt32()
                    : AutomaticSubtitleSelection ?? (_file == 42 && Replans.Count == 0 ? 0 : -1);
            }
            else
            {
                Replans.Add(body.Clone());
                ReplanEntered?.TrySetResult();
                if (HoldReplan != null) await HoldReplan.Task.WaitAsync(ct);
                if (body.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selection))
                    _subtitle = selection.GetProperty("id").GetString() == $"sub:{_file}:0" ? 0 : selection.GetProperty("index").GetInt32();
                else if (body.GetProperty("operation").GetString() == "track_change") _subtitle = -1;
            }
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = $"plan-{_file}-{Replans.Count}", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = origin + "/media.wav" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = $"file:{_file}:audio:0", index = 0 }, subtitle = _subtitle < 0 ? null : new { id = SubtitleId, index = reindexReadyInventory && _subtitle == 0 ? 4 : _subtitle } },
                    subtitle = new { mode = _subtitle < 0 ? "off" : "sidecar", track_id = _subtitle < 0 ? null : SubtitleId,
                        inventory = Version(_file).SubtitleTracks!.Select(t => new { track_id = $"sub:{_file}:{t.Index}", combined_index = reindexReadyInventory && t.Index == 0 ? 4 : t.Index, language = t.Language, codec = t.Codec, source = "external", url = origin + $"/{t.Language}.srt", label = t.Language })
                            .Cast<object>().Concat(downloadedAvailable ? new object[]
                            {
                                new { track_id = "downloaded:77", stored_subtitle_id = 77, sync_key = "stored-77", combined_index = reindexReadyInventory ? 0 : 2, language = "ita", codec = "subrip", source = "downloaded", delivery = "sidecar", url = origin + "/ita.srt", label = "Italian downloaded" },
                            } : []).Concat(additionalDownloadedAvailable ? new object[]
                            {
                                new { track_id = "downloaded:78", stored_subtitle_id = 78, sync_key = "stored-78", combined_index = 3, language = "spa", codec = "subrip", source = "downloaded", delivery = "sidecar", url = origin + "/spa.srt", label = "New translation" },
                            } : []) },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
