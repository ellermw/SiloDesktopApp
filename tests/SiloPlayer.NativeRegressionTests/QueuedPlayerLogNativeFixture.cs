using System.Globalization;
using System.Reflection;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

internal static class QueuedPlayerLogNativeFixture
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Program.ResultDirectory, "isolated-player-log");
        using var scope = LocalLog.BeginOperation("queued-player-fixture", directory);
        var type = typeof(PlayerService);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var timer = (Timer)type.GetField("_logFlushTimer", flags)!.GetValue(null)!;
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        var write = type.GetMethod("LogToFile", flags)!;
        var flush = type.GetMethod("FlushLogs", flags)!;
        flush.Invoke(null, null);
        var recorded = DateTime.Now;
        write.Invoke(null, ["fixture-timing.txt", "event-one https://fixture.invalid/media?token=synthetic-queued-secret"]);
        await Task.Delay(350);
        var secondRecorded = DateTime.Now;
        write.Invoke(null, ["fixture-timing.txt", "event-two"]);
        await Task.Delay(250);
        flush.Invoke(null, null);
        var lines = File.ReadAllLines(Path.Combine(directory, "fixture-timing.txt"));
        if (lines.Length != 2 || lines.Any(line => line.Contains("synthetic-queued-secret", StringComparison.Ordinal)))
            throw new InvalidOperationException("Actual PlayerService queued trace lost lines or redaction.");
        for (var i = 0; i < lines.Length; i++)
        {
            var actual = TimeSpan.ParseExact(lines[i].Substring(1, 12), @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            var expected = (i == 0 ? recorded : secondRecorded).TimeOfDay;
            // Compare time-of-day across midnight without depending on the test date.
            var delta = Math.Abs((actual - expected).TotalMilliseconds);
            delta = Math.Min(delta, Math.Abs(TimeSpan.FromDays(1).TotalMilliseconds - delta));
            Program.Log($"Actual PlayerService queued event {i + 1}: timestamp delta={delta:F1}ms.");
            if (delta > 80) throw new InvalidOperationException("Player trace used flush time instead of event time.");
        }
        Program.Log("PASS: actual PlayerService queues event timestamps across a delayed flush, retains redaction and the reflection FlushLogs entry point.");
        RunPeriodic();
    }

    internal static Task RunPeriodicAsync() { RunPeriodic(); return Task.CompletedTask; }

    private static void RunPeriodic()
    {
        using var scope = LocalLog.BeginOperation("periodic-player-fixture", Path.Combine(Program.ResultDirectory, "isolated-periodic-log"));
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var type = typeof(PlayerService);
        var timer = (Timer)type.GetField("_logFlushTimer", flags)!.GetValue(null)!;
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        var callback = FindCallback(timer, 0);
        var log = type.GetField("_queuedLog", flags)!.GetValue(null)!;
        var queue = log.GetType().GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(log)!;
        var isEmpty = queue.GetType().GetProperty("IsEmpty")!;
        var flush = type.GetMethod("FlushLogs", flags)!;
        var write = type.GetMethod("LogToFile", flags)!;
        flush.Invoke(null, null);
        write.Invoke(null, ["periodic.txt", "synthetic periodic event"]);
        var gate = typeof(LocalLog).GetField("s_gate", flags)!.GetValue(null)!;
        Task first = Task.CompletedTask, periodic = Task.CompletedTask;
        using var started = new ManualResetEventSlim();
        try
        {
            Monitor.Enter(gate);
            try
            {
                first = Task.Run(() => flush.Invoke(null, null));
                if (!SpinWait.SpinUntil(() => (bool)isEmpty.GetValue(queue)!, TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("Actual player flush did not dequeue the blocked batch.");
                periodic = Task.Run(() => { started.Set(); callback(null); });
                if (!started.Wait(TimeSpan.FromSeconds(3)) || !periodic.Wait(TimeSpan.FromMilliseconds(100)))
                    throw new InvalidOperationException("Actual PlayerService timer callback waited behind a blocked writer.");
            }
            finally { Monitor.Exit(gate); }
            if (!Task.WaitAll([first, periodic], TimeSpan.FromSeconds(3))) throw new InvalidOperationException("Player log write did not finish after disk gate release.");
            Program.Log("PASS: actual PlayerService TimerCallback skips a blocked flush while explicit FlushLogs completes its pending write.");
        }
        finally { Task.WaitAll([first, periodic], TimeSpan.FromSeconds(3)); }
    }

    private static TimerCallback FindCallback(object timer, int depth)
    {
        if (depth > 3) throw new InvalidOperationException("Unsupported fixture timer runtime.");
        var fields = timer.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var field in fields)
            if (field.GetValue(timer) is TimerCallback callback) return callback;
        var next = fields.Single(field => field.Name == "_timer").GetValue(timer)!;
        return FindCallback(next, depth + 1);
    }
}
