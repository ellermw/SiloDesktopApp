using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.Services.SiloCast;

internal static partial class SubtitleIdentityNativeFixture
{
    private static async Task CheckAppliedSidecarReloadAsync(PlayerService service, PlaybackManager manager,
        MpvPlayer mpv, SubtitleServer server, Wire wire, string assets)
    {
        var original = manager.CurrentSession!;
        foreach (var manual in new[] { true, false })
        {
            var track = new SubtitleTrackInfo { Index = manual ? 600 : 610, TrackId = "carry-" + manual,
                SyncKey = "stable-" + manual, Source = "external", Language = "en", Codec = "srt",
                Url = server.Origin + "/carry-" + manual + ".srt" };
            original.SubtitleUrls.Add(track);
            try
            {
                if (manual) await service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track);
                else
                {
                    Call(service, "BeginSubtitleSelection");
                    Set(service, "_subtitleSelectionOperation", null);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)!;
                    manager.RecordExecutedSubtitleSelection(track, original.MediaFileId);
                }
                Require(service.ActiveSubtitleServerIndex == track.Index && mpv.GetPropertyDouble("sid") > 0,
                    "Reload carry control did not establish applied external captions.");
                var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
                var path = files[track.Index];
                var after = new SubtitleTrackInfo { Index = track.Index + 1, TrackId = track.TrackId,
                    SyncKey = track.SyncKey, Source = track.Source, Language = track.Language, Codec = track.Codec, Url = track.Url };
                var granted = new PlaybackStartResponse { SessionId = original.SessionId, ProtocolVersion = 3,
                    MediaFileId = original.MediaFileId, PlayMethod = "remux", SelectedSubtitleTrackId = track.TrackId,
                    SubtitleUrls = [after] };
                typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, granted);
                Set(service, "_pendingInitialServerSubtitleIndex", null);
                var reads = server.Requests; var writes = wire.PreferenceWrites;
                server.Fail = true;
                // Exercise the common audio/quality handoff, not the burn-in helper.
                Call(service, "PrepareAppliedSubtitleFileForReload");
                Require(Get(service, "_preparedSubtitleReload") != null, "An applied same-file subtitle had no prepared carry.");
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Loaded() => loaded.TrySetResult();
                mpv.FileLoaded += Loaded;
                try { mpv.LoadFile(System.IO.Path.Combine(assets, "tracks.mkv")); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally { mpv.FileLoaded -= Loaded; }
                mpv.Pause();
                using var initializer = new CancellationTokenSource();
                Set(service, "_loadedMediaInitCts", initializer);
                try
                {
                    Call(service, "LoadSubtitles");
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", after.Index, false)!;
                }
                finally { Set(service, "_loadedMediaInitCts", null); }
                Require(service.ActiveSubtitleServerIndex == after.Index && mpv.GetPropertyDouble("sid") > 0 &&
                    files.TryGetValue(after.Index, out var retained) && retained == path && File.Exists(path) &&
                    server.Requests == reads && wire.PreferenceWrites == writes && Get(service, "_preparedSubtitleReload") == null,
                    "A same-file reload re-read busy sidecar bytes, lost captions, duplicated preferences, or failed its one-use handoff.");
                granted.MediaFileId++;
                Call(service, "PrepareAppliedSubtitleFileForReload");
                Require(Get(service, "_preparedSubtitleReload") == null, "A changed-file grant borrowed applied sidecar bytes.");
            }
            finally
            {
                server.Fail = false;
                typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, original);
                original.SubtitleUrls.Remove(track);
            }
        }
        Program.Log("PASS: manual and automatic applied bytes cross same-file reindexed reloads once without a busy re-read; changed-file grants refuse the carry.");
    }

    private static async Task CheckRetiredAddBeforeFileLoadAsync(MpvPlayer mpv, SubtitleServer server, string assets)
    {
        mpv.SetSubtitleTrack(0);
        var held = server.HoldNextNative();
        var pending = mpv.AddSubtitleUnselectedAsync(server.Origin + "/retired-file.srt", "Retired file", "en", CancellationToken.None);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult();
        mpv.FileLoaded += Loaded;
        try
        {
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var clock = Stopwatch.StartNew();
            mpv.LoadFile(System.IO.Path.Combine(assets, "tracks.mkv"));
            Require(clock.Elapsed < TimeSpan.FromSeconds(1), "Retiring a URL add blocked the file-load caller.");
            held.Release.TrySetResult();
            Require(await pending.WaitAsync(TimeSpan.FromSeconds(5)) == 0, "An outgoing add returned a selectable SID after a replacement request.");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mpv.Pause();
            Require(mpv.GetPropertyDouble("sid") <= 0 && mpv.GetSubtitleTrackIds(externalOnly: true).Count == 0,
                "A retired native add attached foreign captions to a replacement file while Off.");
        }
        finally { held.Release.TrySetResult(); mpv.FileLoaded -= Loaded; }
        Program.Log("PASS: a held native add retires before replacement; Off and the successor inventory remain clean.");
    }

    private static async Task CheckCastSubtitleLaneAsync(PlayerService service, PlaybackManager manager,
        MpvPlayer mpv, SubtitleServer server, Wire wire, DispatcherQueue dispatcher)
    {
        var prior = manager.CurrentSession!.SubtitleUrls.Single(track => track.Index == 2);
        await service.SetSubtitleTrackAndPersistAsync(prior.Index + 1, prior.Language, prior);
        mpv.Play();
        await WaitNativeAsync(() => !mpv.IsPaused);
        var track = new SubtitleTrackInfo { Index = 620, TrackId = "cast-held", Source = "external", Codec = "opaque",
            Language = "en", Url = server.Origin + "/cast-held.probe" };
        manager.CurrentSession.SubtitleUrls.Add(track);
        var adapter = new SiloCastPlayerAdapter(service, dispatcher);
        var authority = service.ActiveAuthority;
        await using var receiver = new SiloCastReceiverService(
            new SettingsService(System.IO.Path.Combine(Program.ResultDirectory, "cast-lane-settings")),
            authority.Auth, authority.ApiClient, service);
        Set(receiver, "_adapter", adapter); Set(receiver, "_remoteScope", authority);
        var operations = (SemaphoreSlim)Get(receiver, "_operations")!;
        using var lifetime = new CancellationTokenSource();
        async Task ControlAsync(JsonObject command)
        {
            // Match ActionLoopAsync's production operation ownership.
            await operations.WaitAsync(lifetime.Token);
            try { Require(await adapter.ControlAsync(command, () => true, lifetime.Token), "The current cast control was refused."); }
            finally { operations.Release(); }
        }
        var writes = wire.PreferenceWrites;
        var held = server.HoldNextNative();
        try
        {
            await ControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = track.Index + 1 }).WaitAsync(TimeSpan.FromSeconds(2));
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ControlAsync(new() { ["name"] = "pause" }).WaitAsync(TimeSpan.FromSeconds(2));
            await WaitNativeAsync(() => mpv.IsPaused);
            await ControlAsync(new() { ["name"] = "select_subtitle_track", ["trackId"] = prior.Index + 1 }).WaitAsync(TimeSpan.FromSeconds(2));
            await WaitNativeAsync(() => service.ActiveSubtitleServerIndex == prior.Index && wire.PreferenceWrites == writes + 1);
            Require(!held.Release.Task.IsCompleted && !((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).ContainsKey(track.Index),
                "A newer cast pick needed the held read or cached its retired SID.");
            await ((Task)Call(receiver, "EndRemotePlaybackAsync")!).WaitAsync(TimeSpan.FromSeconds(2));
            Require(service.State == PlayerState.Idle && operations.CurrentCount == 1 && !held.Release.Task.IsCompleted,
                "Remote teardown waited for the held read or retained the operation lane.");
        }
        finally { lifetime.Cancel(); held.Release.TrySetResult(); }
        await WaitNativeAsync(() => ((SemaphoreSlim)Get(mpv, "_asyncSubtitleAddLane")!).CurrentCount == 1);
        Require(wire.PreferenceWrites == writes + 1, "A retired cast completion saved its unapplied choice.");
        Program.Log("PASS: held cast fallback leaves pause, newer selection and remote teardown prompt, with no stale preference.");
    }
}
