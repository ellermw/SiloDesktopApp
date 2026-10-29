using System.Diagnostics;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class BufferingGateNativeFixture
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync()
    {
        using var http = new HttpClient(new RejectNetwork());
        var api = new SiloApiClient(http); api.SetBaseUrl("https://buffering-gate.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        using var player = new PlayerService(new PlaybackApi(api), new CatalogApi(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "settings")), new SettingsApi(api));
        using var mpv = new MpvPlayer();
        Set(player, "_mpv", mpv);
        Set(player, "<State>k__BackingField", PlayerState.Minimized);
        if (Environment.GetEnvironmentVariable("SILO_BUFFERING_GATE_PHASE") == "watchdog")
            await WatchdogAsync(player);
        else if (Environment.GetEnvironmentVariable("SILO_BUFFERING_GATE_PHASE") == "publish")
            await PublicationAsync(player);
        else await ObservationAsync(player);
    }

    private static async Task WatchdogAsync(PlayerService player)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var detector = (PlaybackStallDetector)Get(player, "_stallDetector")!;
        detector.Reset(321);
        Set(player, "_bufferingController", Prompt());
        var reads = 0;
        Set(player, "_readBufferingNetwork", new Func<MpvPlayer, (long?, double?, double, double?, bool, bool)>(_ =>
        {
            Interlocked.Increment(ref reads); entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return (1024, 1000, 1, 0, false, false);
        }));
        var first = Task.Run(() => Invoke(player, "CheckPlaybackStall"));
        try
        {
            Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))), "Watchdog did not reach the held native boundary.");
            var second = Task.Run(() => Invoke(player, "CheckPlaybackStall"));
            Check(await Task.WhenAny(second, Task.Delay(150)) == second, "Overlapping watchdog callback waited instead of skipping.");
            var observedPosition = (double)Get(detector, "_lastPosition")!;
            Program.Log($"Held first watchdog: native reads={reads}, detector position={observedPosition}.");
            Check(reads == 1 && observedPosition == 321, "Overlapping watchdog bypassed sampler ownership and mutated the stall detector.");
            release.Set(); await first;
            Check((double)Get(detector, "_lastPosition")! == 0, "The owning watchdog did not finish its detector observation.");
        }
        finally { release.Set(); await first; }
        // An exception must release the callback-wide claim, not disable future checks.
        reads = 0;
        Set(player, "_readBufferingNetwork", new Func<MpvPlayer, (long?, double?, double, double?, bool, bool)>(_ =>
        { Interlocked.Increment(ref reads); throw new InvalidOperationException("synthetic native read failure"); }));
        Invoke(player, "CheckPlaybackStall"); Invoke(player, "CheckPlaybackStall");
        Check(reads == 2, "A failed watchdog retained single-flight ownership.");
        Program.Log("PASS: actual watchdog callback skips all overlapping work and releases ownership after native read failure.");
    }

    private static BufferingQualityController Prompt()
    {
        var controller = new BufferingQualityController("ask");
        controller.Adopt(new("plan", "adoption", "auto", "1080p", 8000,
            [new() { Label = "720p", BitrateKbps = 4000 }]), 0);
        controller.Observe(new(0, false, false, false, false, false, 0, null));
        controller.Observe(new(11000, true, false, false, false, false, 0, 2000));
        controller.Observe(new(18000, true, false, false, false, false, 0, 2000));
        Check(controller.Pill == "prompt", "Fixture did not produce a real controller prompt.");
        return controller;
    }

    private static async Task PublicationAsync(PlayerService player)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var cleared = new ManualResetEventSlim();
        var calls = new List<string>(); var active = 0; var maximum = 0;
        Set(player, "_bufferingController", Prompt());
        Set(player, "_writeBufferingPill", new Action<MpvPlayer, string>((_, pill) =>
        {
            var count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
            try
            {
                lock (calls) calls.Add(pill);
                if (pill == "prompt") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
                else cleared.Set();
            }
            finally { Interlocked.Decrement(ref active); }
        }));
        var publish = Task.Run(() => player.SetBufferingPillObscured(false));
        Task timer = Task.CompletedTask;
        try
        {
            Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))), "Publication never reached the native boundary.");
            timer = Task.Run(async () => { await Task.Delay(400); release.Set(); });
            // Execute the actual public property on WinUI's STA while the native
            // writer boundary is deliberately held on a worker.
            var clock = Stopwatch.StartNew(); var pill = player.BufferingQualityPill; clock.Stop();
            Program.Log($"Controlled native publication: UI getter={clock.Elapsed.TotalMilliseconds:F1}ms, pill={pill}.");
            Check(clock.ElapsedMilliseconds < 150, "Native pill publication retained the gate needed by the UI.");
            clock.Restart(); player.KeepBufferingQuality(); player.SetBufferingPillObscured(true);
            Invoke(player, "EndBufferingQuality"); clock.Stop();
            Check(clock.ElapsedMilliseconds < 150, "UI pill actions waited on a native send.");
            await publish; await timer;
            Check(await Task.Run(() => cleared.Wait(TimeSpan.FromSeconds(3))), "Latest clear was lost behind an in-flight pill.");
            lock (calls) Check(calls.Count == 2 && calls[0] == "prompt" && calls[1] == "", "Publication was not ordered/coalesced to the latest state.");
            Check(maximum == 1, "Concurrent native pill sends overlapped.");
            Program.Log("PASS: actual STA pill getter/actions remain responsive during held native publication; one worker serializes prompt then latest clear.");
        }
        finally { release.Set(); await Task.WhenAll(publish, timer); }
    }

    private static async Task ObservationAsync(PlayerService player)
    {
        foreach (var replacement in new[] { "plan", "player" })
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var calls = 0; var controller = Prompt();
            Set(player, "_bufferingController", controller); Set(player, "_bufferingLastPosition", (double?)123);
            Set(player, "_readBufferingNetwork", new Func<MpvPlayer, (long?, double?, double, double?, bool, bool)>(_ =>
            {
                Interlocked.Increment(ref calls); entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return (1024, 1000, 1, 0, false, false);
            }));
            var sample = Task.Run(() => Invoke(player, "ObserveBufferingQuality"));
            try
            {
                Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))), "Sample never reached the native read boundary.");
                var second = Task.Run(() => Invoke(player, "ObserveBufferingQuality"));
                Check(await Task.WhenAny(second, Task.Delay(150)) == second && calls == 1, "An overlapping tick sampled native state again.");
                var clock = Stopwatch.StartNew(); _ = player.BufferingQualityPill; clock.Stop();
                Check(clock.ElapsedMilliseconds < 150, "Native read retained the UI gate.");
                if (replacement == "plan") Invoke(player, "AdoptBufferingQualityPlan");
                else Set(player, "_mpv", new MpvPlayer());
                release.Set(); await sample;
                Check((double?)Get(player, "_bufferingLastPosition") == 123, "A displaced sample changed the active controller history.");
                Check((int)Get(player, "_bufferingSampling")! == 0, "Sampler did not release single-flight ownership.");
            }
            finally { release.Set(); await sample; }
        }
        Program.Log("PASS: actual buffering sampler isolates held native reads, suppresses overlapping ticks and discards changed-plan/player samples.");
    }

    private static object? Get(object owner, string name) => owner.GetType().GetField(name, Flags)!.GetValue(owner);
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Flags)!.SetValue(owner, value);
    private static void Invoke(object owner, string name) => owner.GetType().GetMethod(name, Flags)!.Invoke(owner, null);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class RejectNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("Unexpected fixture network request."); }
}
