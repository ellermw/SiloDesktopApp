using System.Diagnostics;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static partial class SubtitleIdentityNativeFixture
{
    private static async Task CheckPromptCancellationAsync(MpvPlayer mpv, SubtitleServer server)
    {
        mpv.SetSubtitleTrack(1);
        var before = mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet();
        var held = server.HoldNextNative();
        using var cancellation = new CancellationTokenSource();
        var pending = mpv.AddSubtitleUnselectedAsync(server.Origin + "/prompt.srt", "Retired prompt", "en", cancellation.Token);
        NativeGateHold? hold = null;
        SubtitleServer.Gate? next = null; Task<int>? successor = null;
        try
        {
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            hold = await NativeGateHold.EnterAsync(Get(mpv, "_asyncSubtitleGate")!);
            Require(!pending.IsCompleted, "Prompt cancellation control did not retain the actual submitted command.");
            cancellation.Cancel();
            var canceled = false;
            try { await pending.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
            var lane = (SemaphoreSlim)Get(mpv, "_asyncSubtitleAddLane")!;
            Require(canceled && !held.Release.Task.IsCompleted && lane.CurrentCount == 0,
                "Canceled native subtitle caller waited for the held reply or released the add lane prematurely.");
            next = server.HoldNextNative();
            successor = mpv.AddSubtitleUnselectedAsync(server.Origin + "/successor.srt", "Current successor", "en", CancellationToken.None);
            Require(!successor.IsCompleted && server.NativeRequests == 1,
                "A successor native add escaped the retained command lane before the old reply.");
        }
        finally
        {
            held.Release.TrySetResult();
            if (hold != null) await hold.ReleaseAsync();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        }
        try
        {
            await next!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(mpv.GetPropertyDouble("sid") == 1, "Retired native reply replaced the current applied subtitle.");
            next.Release.TrySetResult();
            var sid = await successor!.WaitAsync(TimeSpan.FromSeconds(5));
            Require(sid > 0 && mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet().SetEquals(before.Append(sid)) &&
                mpv.GetPropertyDouble("sid") == 1 && mpv.IsPaused,
                "Late retirement kept an old SID, removed the queued successor, or changed pause/current selection.");
        }
        finally { next?.Release.TrySetResult(); }
        Program.Log("PASS: prompt canceled caller returns while HTTP/reply are held; lane and exact successor cleanup remain owned.");
    }

    private static async Task CheckLateCleanupAsync(MpvPlayer mpv, SubtitleServer server, string assets)
    {
        var generation = (long)Get(mpv, "_asyncSubtitleFileGeneration")!;
        var sid = await mpv.AddSubtitleUnselectedAsync(server.Origin + "/late-owned.srt", "Owned late add", "en", CancellationToken.None);
        Require(sid > 0 && mpv.AddSubtitle(Path.Combine(assets, "japanese.srt"), "Unrelated retained", "ja", select: true),
            "Late-cleanup control requires two real independently added native tracks.");
        var retained = (int)Math.Round(mpv.GetPropertyDouble("sid"));
        await ((Task)Call(mpv, "CleanupRetiredSubtitleAddAsync", Task.FromResult((sid, generation)))!).WaitAsync(TimeSpan.FromSeconds(5));
        Require(!mpv.GetSubtitleTrackIds(externalOnly: true).Contains(sid) && mpv.GetSubtitleTrackIds(externalOnly: true).Contains(retained) &&
            mpv.GetPropertyDouble("sid") == retained && mpv.IsPaused,
            "Same-file late cleanup did not remove only its exact owned SID.");

        var obsolete = await mpv.AddSubtitleUnselectedAsync(server.Origin + "/old-file.srt", "Old-file add", "en", CancellationToken.None);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult();
        mpv.FileLoaded += Loaded;
        try { mpv.LoadFile(Path.Combine(assets, "tracks.mkv")); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { mpv.FileLoaded -= Loaded; }
        mpv.Pause(); await WaitNativeAsync(() => mpv.IsPaused);
        Require((long)Get(mpv, "_asyncSubtitleFileGeneration")! != generation, "File cleanup control did not advance the actual native file epoch.");
        for (var i = 0; i < 16 && !mpv.GetSubtitleTrackIds(externalOnly: true).Contains(obsolete); i++)
        {
            var path = Path.Combine(Program.ResultDirectory, "replacement-" + i + ".srt");
            File.Copy(Path.Combine(assets, "english.srt"), path);
            Require(mpv.AddSubtitle(path, "Replacement " + i, "en", select: true), "Replacement native control failed to add its owned local file.");
        }
        var replacementIds = mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet();
        Require(replacementIds.Contains(obsolete), "Replacement control did not reproduce the old numeric SID.");
        mpv.SetSubtitleTrack(obsolete);
        await ((Task)Call(mpv, "CleanupRetiredSubtitleAddAsync", Task.FromResult((obsolete, generation)))!).WaitAsync(TimeSpan.FromSeconds(5));
        Require(mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet().SetEquals(replacementIds) && mpv.GetPropertyDouble("sid") == obsolete && mpv.IsPaused,
            "An old-file late cleanup removed or changed the successor file's equally numbered SID.");
        Program.Log("PASS: actual late cleanup removes its same-file SID only and preserves a replacement-file reused SID.");
    }

    private static async Task CheckFallbackDeadlineAsync(PlayerService service, PlaybackManager manager, MpvPlayer mpv, SubtitleServer server, Wire wire)
    {
        var prior = manager.CurrentSession!.SubtitleUrls.Single(track => track.Index == 2);
        await service.SetSubtitleTrackAndPersistAsync(3, prior.Language, prior);
        var writes = wire.PreferenceWrites; var priorSid = mpv.GetPropertyDouble("sid");
        var track = new SubtitleTrackInfo { Index = 40, TrackId = "deadline", Source = "external", Codec = "opaque", Language = "en", Url = server.Origin + "/deadline.probe" };
        manager.CurrentSession.SubtitleUrls.Add(track);
        // Exercise the production deadline without a five-minute fixture wait.
        // A successful read beyond the former eight-second budget must survive.
        var clock = new FallbackDeadlineClock();
        Set(service, "_subtitleUrlFallbackTimeProvider", clock);
        var slow = server.HoldNextNative();
        var slowPick = service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track);
        try
        {
            await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(clock.DueTime == TimeSpan.FromMinutes(5), "Native fallback did not use its separate five-minute deadline.");
            clock.Advance(TimeSpan.FromSeconds(9));
            Require(!slowPick.IsCompleted, "A pending native sidecar was refused at the managed text deadline.");
            slow.Release.TrySetResult();
            await slowPick.WaitAsync(TimeSpan.FromSeconds(5));
            Require(service.ActiveSubtitleServerIndex == track.Index && mpv.GetPropertyDouble("sid") > 0 && mpv.IsPaused &&
                wire.PreferenceWrites == writes + 1,
                "A native fallback past eight seconds on the controlled clock failed to apply and save once.");
        }
        finally { slow.Release.TrySetResult(); }
        await service.SetSubtitleTrackAndPersistAsync(3, prior.Language, prior);
        writes = wire.PreferenceWrites;
        // Do not let the success cache bypass the subsequent stalled-read test.
        track = new SubtitleTrackInfo { Index = 41, TrackId = "stalled-deadline", Source = "external", Codec = "opaque",
            Language = "en", Url = server.Origin + "/stalled-deadline.probe" };
        manager.CurrentSession.SubtitleUrls.Add(track);
        var held = server.HoldNextNative();
        var pending = service.SetSubtitleTrackAndPersistAsync(track.Index + 1, track.Language, track);
        NativeGateHold? hold = null;
        try
        {
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var owner = Get(service, "_subtitleReadOperation")!;
            var originalToken = (CancellationToken)owner.GetType().GetProperty("Token")!.GetValue(owner)!;
            hold = await NativeGateHold.EnterAsync(Get(mpv, "_asyncSubtitleGate")!);
            Require(clock.DueTime == TimeSpan.FromMinutes(5), "The stalled fallback lost its production deadline.");
            clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1));
            Require(!pending.IsCompleted, "A current-owner native fallback timed out before its bitmap/oversized budget.");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Require(!held.Release.Task.IsCompleted && !originalToken.IsCancellationRequested && ReferenceEquals(owner, Get(service, "_subtitleReadOperation")) &&
                service.ActiveSubtitleServerIndex == prior.Index && mpv.GetPropertyDouble("sid") == priorSid && mpv.IsPaused &&
                wire.PreferenceWrites == writes && !((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).ContainsKey(track.Index),
                "The native fallback deadline depended on owner retirement/reply release or changed current SID/pause/preferences/cache.");
            Program.Log("PASS: native fallback survives the old text deadline and refuses at its five-minute deadline while HTTP/reply remain held (controlled timer).");
        }
        finally
        {
            Call(service, "CancelSubtitleSelectionRead");
            held.Release.TrySetResult();
            if (hold != null) await hold.ReleaseAsync();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Set(service, "_subtitleUrlFallbackTimeProvider", TimeProvider.System);
        }
        await WaitNativeAsync(() => ((SemaphoreSlim)Get(mpv, "_asyncSubtitleAddLane")!).CurrentCount == 1);
        Require(mpv.GetPropertyDouble("sid") == priorSid && wire.PreferenceWrites == writes,
            "A late reply after refusal revived the unapplied choice or saved its preference.");
    }

    private static async Task CheckCastPollingAsync(PlayerService service, PlaybackManager manager, MpvPlayer mpv, SubtitleServer server, Wire wire)
    {
        var session = manager.CurrentSession!; var original = session.SubtitleUrls;
        var prior = original.Single(track => track.Index == 2);
        await service.SetSubtitleTrackAndPersistAsync(3, prior.Language, prior);
        var reads = server.Requests; var writes = wire.PreferenceWrites;
        session.SubtitleUrls = [prior, .. Enumerable.Range(100, 255).Select(index => new SubtitleTrackInfo {
            Index = index, Source = "embedded", StreamIndex = 1, ContainerTrackId = "2", Language = "en", Codec = "subrip" }),
            new() { Index = 500, Source = "external", Url = server.Origin + "/poll.srt" },
            new() { Index = 501, Source = "embedded", StreamIndex = 99, ContainerTrackId = "99", Language = "zz" },
            new() { Index = 502, Source = "embedded", StreamIndex = 1, ContainerTrackId = "2", Language = "en", Unplayable = true },
            new() { Index = 503, Source = "external", Url = "" }];
        try
        {
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++)
            {
                var snapshot = service.CaptureSiloCastState();
                var ids = snapshot["subtitleTracks"]!.AsArray().Select(row => row!["trackId"]!.GetValue<int>()).ToHashSet();
                Require(ids.Count == 257 && ids.Contains(3) && ids.Contains(501) && !ids.Overlaps([502, 503, 504]) &&
                    snapshot["selectedSubtitleTrackId"]!.GetValue<int>() == 3,
                    "A mixed native/URL poll hid a real route, exposed an unavailable route, or lost current selection.");
            }
            Require(clock.Elapsed < TimeSpan.FromSeconds(5) && server.Requests == reads && wire.PreferenceWrites == writes && mpv.GetPropertyDouble("sid") == 1 && mpv.IsPaused,
                "Bounded SiloCast captures fetched sidecars, wrote preferences, blocked, or changed playback.");
            Program.Log($"PASS: actual mixed 260-row native/URL inventory captured20times within bound ({clock.Elapsed.TotalMilliseconds:F1}ms); source separately verifies one inventory read, not a measured runtime call count.");
        }
        finally { session.SubtitleUrls = original; }
    }

    private static async Task WaitNativeAsync(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Require(ready(), "The actual native lifecycle did not reach its bounded completion state.");
    }

    // Only the URL fallback's one-shot deadline uses this provider. Native
    // HTTP/replies remain genuinely held, and owner cancellation stays real.
    private sealed class FallbackDeadlineClock : TimeProvider
    {
        private DeadlineTimer? timer;
        public TimeSpan DueTime { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Require(period == Timeout.InfiniteTimeSpan, "Fallback deadline must be a one-shot timer.");
            DueTime = dueTime;
            return timer = new DeadlineTimer(callback, state, dueTime);
        }
        public void Advance(TimeSpan elapsed) => timer!.Advance(elapsed);

        private sealed class DeadlineTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private readonly object gate = new();
            private TimeSpan remaining = dueTime;
            private bool disposed;
            public void Advance(TimeSpan elapsed)
            {
                lock (gate)
                {
                    if (disposed || remaining == Timeout.InfiniteTimeSpan) return;
                    remaining -= elapsed;
                    if (remaining > TimeSpan.Zero) return;
                    remaining = Timeout.InfiniteTimeSpan;
                }
                callback(state);
            }
            public bool Change(TimeSpan due, TimeSpan period)
            {
                Require(period == Timeout.InfiniteTimeSpan, "Fallback deadline must remain one-shot.");
                lock (gate)
                {
                    if (disposed) return false;
                    remaining = due;
                    return true;
                }
            }
            public void Dispose() { lock (gate) disposed = true; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class NativeGateHold
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task holder;
        private NativeGateHold(object gate) => holder = Task.Factory.StartNew(() =>
        {
            lock (gate) { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        public static async Task<NativeGateHold> EnterAsync(object gate)
        {
            var hold = new NativeGateHold(gate);
            try { await hold.entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); return hold; }
            catch { hold.release.TrySetResult(); throw; }
        }
        public async Task ReleaseAsync() { release.TrySetResult(); await holder.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
