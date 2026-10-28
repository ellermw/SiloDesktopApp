using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class QueuedLocalLogTests
{
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
