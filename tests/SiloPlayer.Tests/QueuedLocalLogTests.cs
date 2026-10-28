using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class QueuedLocalLogTests
{
    [Fact]
    public void PeriodicFlushSkipsAnAlreadyDequeuedBlockedWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-periodic-log-" + Guid.NewGuid().ToString("N"));
        var log = new QueuedLocalLog(directory);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var fileGate = typeof(LocalLog).GetField("s_gate", flags)!.GetValue(null)!;
        var queue = typeof(QueuedLocalLog).GetField("_queue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(log)!;
        var isEmpty = queue.GetType().GetProperty("IsEmpty")!;
        var tryFlush = typeof(QueuedLocalLog).GetMethod("TryFlush");
        Task first = Task.CompletedTask;
        Task<bool> periodic = Task.FromResult(true);
        using var started = new ManualResetEventSlim();
        try
        {
            log.Enqueue("periodic.txt", "one event");
            Monitor.Enter(fileGate);
            try
            {
                first = Task.Run(log.Flush);
                Assert.True(SpinWait.SpinUntil(() => (bool)isEmpty.GetValue(queue)!, TimeSpan.FromSeconds(3)));
                periodic = Task.Run(() =>
                {
                    started.Set();
                    // Before the repair, PlayerService's periodic callback calls Flush.
                    if (tryFlush == null) { log.Flush(); return true; }
                    return (bool)tryFlush.Invoke(log, null)!;
                });
                Assert.True(started.Wait(TimeSpan.FromSeconds(3)));
                Assert.True(periodic.Wait(TimeSpan.FromMilliseconds(100)), "Periodic flush waited behind a stalled disk write.");
                Assert.False(periodic.Result);
            }
            finally { Monitor.Exit(fileGate); }
            Assert.True(Task.WaitAll([first, periodic], TimeSpan.FromSeconds(3)));
            Assert.EndsWith("one event", Assert.Single(File.ReadAllLines(Path.Combine(directory, "periodic.txt"))));
        }
        finally
        {
            Task.WaitAll([first, periodic], TimeSpan.FromSeconds(3));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExplicitFlushWaitsForAnAlreadyDequeuedBlockedWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-blocked-log-" + Guid.NewGuid().ToString("N"));
        var log = new QueuedLocalLog(directory);
        var fileGate = typeof(LocalLog).GetField("s_gate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var queue = typeof(QueuedLocalLog).GetField("_queue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(log)!;
        var isEmpty = queue.GetType().GetProperty("IsEmpty")!;
        Task first = Task.CompletedTask, second = Task.CompletedTask;
        using var secondStarted = new ManualResetEventSlim();
        try
        {
            log.Enqueue("blocked.txt", "first");
            Monitor.Enter(fileGate);
            try
            {
                first = Task.Run(log.Flush);
                Assert.True(SpinWait.SpinUntil(() => (bool)isEmpty.GetValue(queue)!, TimeSpan.FromSeconds(3)), "First flush did not dequeue its batch.");
                second = Task.Run(() => { secondStarted.Set(); log.Flush(); });
                Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(3)));
                Assert.False(second.Wait(TimeSpan.FromMilliseconds(100)), "Explicit flush returned while the prior batch was unwritten.");
                Assert.False(File.Exists(Path.Combine(directory, "blocked.txt")));
            }
            finally { Monitor.Exit(fileGate); }
            Assert.True(Task.WaitAll([first, second], TimeSpan.FromSeconds(3)));
            Assert.EndsWith("first", Assert.Single(File.ReadAllLines(Path.Combine(directory, "blocked.txt"))));
        }
        finally
        {
            Task.WaitAll([first, second], TimeSpan.FromSeconds(3));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LinesKeepTheTimeTheyWereRecordedRatherThanTheFlushTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), "silo-queued-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 10, 28, 10, 0, 0, 123);
            var log = new QueuedLocalLog(dir, () => now);
            log.Enqueue("state_trace.txt", "seek requested");
            now = now.AddSeconds(2.5);
            log.Enqueue("state_trace.txt", "first frame");
            log.Flush();

            var lines = File.ReadAllLines(Path.Combine(dir, "state_trace.txt"));
            Assert.Equal(["[10:00:00.123] seek requested", "[10:00:02.623] first frame"], lines);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentFlushesKeepQueuedLinesInOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "silo-queued-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new QueuedLocalLog(dir);
            const int count = 20_000;
            using var done = new CancellationTokenSource();
            var flushers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                while (!done.IsCancellationRequested) log.Flush();
            })).ToArray();
            for (var i = 0; i < count; i++) log.Enqueue("order.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            done.Cancel();
            await Task.WhenAll(flushers);
            log.Flush();

            var numbers = File.ReadAllLines(Path.Combine(dir, "order.txt"))
                .Select(line => int.Parse(line[(line.IndexOf(']') + 2)..], System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            Assert.Equal(Enumerable.Range(0, count), numbers);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
