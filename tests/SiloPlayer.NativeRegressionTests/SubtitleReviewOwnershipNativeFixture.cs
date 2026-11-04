using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static partial class SubtitleIdentityNativeFixture
{
    private static async Task ReviewOwnershipAsync(string assets, string scenario)
    {
        var cue = scenario.StartsWith("review-cue-", StringComparison.Ordinal);
        using var server = new SubtitleServer(cue && scenario.EndsWith("cleanup", StringComparison.Ordinal)
            ? new byte[] { 0, 1, 2, 3, 0, 255 } : await File.ReadAllBytesAsync(Path.Combine(assets, "english.srt")))
            { MediaBytes = await File.ReadAllBytesAsync(Path.Combine(assets, "tracks.mkv")) };
        using var wire = new Wire { InitialPlaybackUrl = server.Origin + "/media.mkv", InventoryHost = new Uri(server.Origin).Host };
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl(server.Origin); api.SetProfile("review-fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        var version = new FileVersion { FileId = 42, Duration = 60, Container = "mkv", AudioTracks = [new() { Language = "eng", Default = true }] };
        manager.UseWatchDetail(new() { ContentId = "review-movie", Versions = [version] });
        var session = await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void InitialLoaded() => loaded.TrySetResult();
        mpv.FileLoaded += InitialLoaded;
        try { mpv.LoadFile(Path.Combine(assets, "tracks.mkv")); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { mpv.FileLoaded -= InitialLoaded; }
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "review-movie");
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("Versions")!.SetValue(service, new List<FileVersion> { version });
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
        try
        {
            if (cue) await ReviewCorrectedCueAsync(assets, scenario, server, api, manager, service, mpv);
            else if (scenario == "review-owned-foreign-grant")
            {
                var owner = Call(service, "CaptureSubtitleOperationOwner", session)!;
                bool CanUse(PlaybackStartResponse candidate) => (bool)owner.GetType()
                    .GetMethod("CanUseSession", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, [candidate])!;
                bool Adopt(PlaybackStartResponse previous, PlaybackStartResponse replacement) => (bool)owner.GetType()
                    .GetMethod("TryAdoptSession", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, [previous, replacement])!;
                manager.RecordLocalSubtitleSelection(null);
                wire.ReplanFileId = 43;
                var accepted = await manager.ReplanCurrentSubtitleForOwnerAsync(0, CanUse, Adopt);
                Require(accepted is { MediaFileId: 43 } && ReferenceEquals(accepted, manager.CurrentSession) && CanUse(accepted) &&
                    !(bool)owner.GetType().GetProperty("IsCurrentForAi", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)! &&
                    wire.Replans == 1 && wire.PreferenceWrites == 0,
                    "An owned alternate-file grant was rolled back, or incorrectly retained the old file's AI job.");
                Program.Log("PASS: guarded alternate-file grant is adopted while its outgoing AI lifetime retires.");
            }
            else if (scenario is "review-ai-aba" or "review-ai-same-file")
            {
                // Accepted/running jobs may have no live-cue state at all. The
                // retained monitor owner must still remember a departed file.
                var owner = Call(service, "CaptureSubtitleOperationOwner", session)!;
                if (scenario == "review-ai-aba")
                {
                    typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                        new PlaybackStartResponse { SessionId = "foreign", MediaFileId = 43, ProtocolVersion = 3 });
                    typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                        new PlaybackStartResponse { SessionId = "returned", MediaFileId = 42, ProtocolVersion = 3 });
                }
                else
                    typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                        new PlaybackStartResponse { SessionId = "same-file", MediaFileId = 42, ProtocolVersion = 3 });
                var current = (bool)owner.GetType().GetProperty("IsCurrentForAi", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
                Require(current == (scenario == "review-ai-same-file"),
                    "A monitor-only AI owner revived after file42→43→42, or lost a legitimate same-file response replacement.");
                Require((bool)owner.GetType().GetProperty("IsCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)! == current,
                    "A rejected AI epoch rebound the ordinary exact-response owner receipt.");
                Require(wire.PreferenceWrites == 0 && Get(service, "_liveSubtitleOperationOwner") == null,
                    "Monitor owner inspection unexpectedly selected, saved or created a live buffer.");
                Program.Log($"PASS: {scenario} independently fences the accepted monitor owner across media-file publication.");
            }
            else await ReviewAiHeldLoadAsync(scenario, server, manager, service, mpv, version);
        }
        finally
        {
            server.BeforeMediaResponse = null;
            Call(service, "ClearLiveSubtitleTranslation", false); Call(service, "StopSubtitleSync");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task ReviewAiHeldLoadAsync(string scenario, SubtitleServer server, PlaybackManager manager,
        PlayerService service, MpvPlayer mpv, FileVersion version)
    {
        var owner = Call(service, "CaptureSubtitleOperationOwner", manager.CurrentSession!)!;
        using var buffer = new CancellationTokenSource();
        var path = Path.Combine(Program.ResultDirectory, scenario + ".vtt");
        await File.WriteAllTextAsync(path, "WEBVTT\n\n");
        // Seed exactly the synthetic downstream state established by Begin's
        // temporary playing→buffer pause, without touching real app-data paths.
        Set(service, "_liveSubtitleOperationOwner", owner); Set(service, "_liveSubtitleJobId", 77L);
        Set(service, "_liveSubtitleFileId", 42); Set(service, "_liveSubtitlePath", path);
        Set(service, "_liveSubtitleResumeCts", buffer); Set(service, "_resumeAfterLiveSubtitleBuffer", true);
        Set(service, "_liveSubtitleFileLoadGeneration", Get(service, "_fileLoadGeneration"));
        mpv.Pause();
        var replacement = await manager.ReplanQualityAsync("720p", 0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.BeforeMediaResponse = () => { entered.TrySetResult(); return release.Task; };
        Call(service, "WireMpvEvents");
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded() => reloaded.TrySetResult(); service.ContentLoaded += OnLoaded;
        var sidecarGeneration = (long)Get(service, "_subtitleSidecarGeneration")!;
        try
        {
            var prepared = (Task)Call(service, "PreparePlaybackTransportAsync", replacement, version,
                server.Origin + "/media.mkv", 0d, CancellationToken.None)!;
            await prepared;
            Call(service, "ApplyPreparedTransport", prepared.GetType().GetProperty("Result")!.GetValue(prepared)!);
            Call(service, "BeginMpvLoad", prepared.GetType().GetProperty("Result")!.GetValue(prepared)!, false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(service.IsLoading && mpv.IsPaused && !reloaded.Task.IsCompleted,
                "AI load control did not hold the replacement while its outgoing native file stayed paused.");
            Require((bool)Get(service, "_resumeAfterLiveSubtitleBuffer")! && Get(service, "_liveSubtitleResumeCts") != null,
                "Held-load control lost its temporary AI resume intent before invoking the real completion.");
            if (scenario == "review-ai-load")
            {
                using var payload = JsonDocument.Parse("""{"job_id":77,"cues":[{"start":0,"end":5,"text":"held cue"}]}""");
                Call(service, "ApplySubtitleTranslationCues", payload.RootElement);
                Require((await File.ReadAllTextAsync(path)).Contains("held cue", StringComparison.Ordinal), "Same-file load lost its accepted cue data.");
            }
            else Call(service, "ResumeAfterLiveSubtitleBuffer", 77L);
            var nativePaused = mpv.GetPropertyFlag("pause");
            Program.Log($"AI held-load completion {scenario}: loading={service.IsLoading}, loaded={reloaded.Task.IsCompleted}, nativePaused={nativePaused}, restore={Get(service, "_restorePausedAfterLoad")}, bufferResume={Get(service, "_resumeAfterLiveSubtitleBuffer")}, bufferOwner={(long)Get(service, "_liveSubtitleJobId")!}.");
            Require(service.IsLoading && !reloaded.Task.IsCompleted && nativePaused && Equals(Get(service, "_restorePausedAfterLoad"), false),
                "AI buffering completion resumed the outgoing native stream before FILE_LOADED.");
            release.TrySetResult();
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (((long)Get(service, "_subtitleSidecarGeneration")! <= sidecarGeneration || service.IsLoading ||
                Get(service, "_loadedMediaInitCts") != null) && DateTime.UtcNow < deadline) await Task.Delay(20);
            Require((long)Get(service, "_subtitleSidecarGeneration")! > sidecarGeneration && !service.IsLoading &&
                Get(service, "_loadedMediaInitCts") == null && !mpv.GetPropertyFlag("pause"),
                "The final native load did not restore the accepted playing intent.");
            Program.Log($"PASS: {scenario} retains cue/job state while FILE_LOADED exclusively resumes playback.");
        }
        finally { release.TrySetResult(); service.ContentLoaded -= OnLoaded; }
    }

    private static async Task ReviewCorrectedCueAsync(string assets, string scenario, SubtitleServer server, SiloApiClient api,
        PlaybackManager manager, PlayerService service, MpvPlayer mpv)
    {
        var track = new SubtitleTrackInfo { Index = 0, SyncKey = "external-" + new string('b', 64), Source = "external", Language = "en", Codec = "srt", Url = server.Origin + "/captions.srt" };
        manager.CurrentSession!.SubtitleUrls = [track];
        Require(mpv.AddSubtitle(Path.Combine(assets, "english.srt"), "Prior", "en", true), "Cannot establish prior applied cue.");
        var previous = (int)Math.Round(mpv.GetPropertyDouble("sid"));
        Set(service, "_activeSubtitleServerIndex", 0); mpv.Pause();
        ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!)[0] = previous;
        using var lifetime = new CancellationTokenSource();
        Set(service, "_subtitleSyncCts", lifetime); Set(service, "_subtitleSyncOwner", Get(service, "_activeAuthority"));
        Set(service, "_subtitleSyncAuthority", api.CaptureContext()); Set(service, "_subtitleSyncSession", manager.SessionId); Set(service, "_subtitleSyncFile", 42);
        var entryType = typeof(PlayerService).GetNestedType("SyncEntry", BindingFlags.NonPublic)!;
        var entry = Activator.CreateInstance(entryType, nonPublic: true)!;
        entryType.GetField("State")!.SetValue(entry, new SubtitleSyncState { Key = track.SyncKey, MediaFileId = 42, Source = "external", Format = "srt", Language = "en", Timing = new() { OffsetMs = 75 } });
        var entries = Get(service, "_subtitleSyncEntries")!; entries.GetType().GetMethod("Add")!.Invoke(entries, [track.SyncKey, entry]);
        var injected = 0;
        void AddUnrelated(string message)
        {
            if (injected != 0 || !message.Contains("mpv_command [sub-add", StringComparison.Ordinal)) return;
            injected = -1;
            var before = mpv.GetSubtitleTrackIds().ToHashSet();
            Require(mpv.AddSubtitle(Path.Combine(assets, "japanese.srt"), "Unrelated owner", "ja", false), "Cannot establish concurrent native add.");
            injected = mpv.GetSubtitleTrackIds(externalOnly: true).Single(id => !before.Contains(id));
            mpv.SetSubtitleTrack(previous);
        }
        if (scenario == "review-cue-owned-cleanup") mpv.Error += AddUnrelated;
        try
        {
            await ((Task)Call(service, "ReloadCorrectedSubtitleAsync", entry, lifetime.Token, false)!).WaitAsync(TimeSpan.FromSeconds(5));
            var mapped = ((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!)[0];
            if (scenario == "review-cue-owned-cleanup")
            {
                Require(injected > 0, "Failed-cue control never reached the real native add error callback.");
                Require(mpv.GetSubtitleTrackIds(externalOnly: true).Contains(injected) && mapped == previous && mpv.GetPropertyDouble("sid") == previous &&
                    entryType.GetField("LastAppliedTiming")!.GetValue(entry) == null && entryType.GetField("Error")!.GetValue(entry) != null,
                    "Failed corrected cue removed an unrelated native SID or falsely committed its timing/selection.");
            }
            else Require(mapped > 0 && mapped != previous && mpv.GetPropertyDouble("sid") == mapped && !mpv.GetSubtitleTrackIds().Contains(previous) &&
                entryType.GetField("LastAppliedTiming")!.GetValue(entry) != null && entryType.GetField("Error")!.GetValue(entry) == null,
                "A valid corrected cue failed its actual add/select/timing replacement.");
            Require(mpv.IsPaused, "Corrected cue changed genuine viewer pause intent.");
            Program.Log($"PASS: {scenario} verifies native cue ownership before cleanup, selection and timing publication.");
        }
        finally { mpv.Error -= AddUnrelated; lifetime.Cancel(); Call(service, "StopSubtitleSync"); }
    }
}
