using System.Collections.Concurrent;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class ArtworkPrefetchQueueTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private sealed class Probe
    {
        public readonly ConcurrentDictionary<int, TaskCompletionSource> Gates = new();
        public readonly ConcurrentQueue<int> Started = new();
        public readonly ConcurrentQueue<int> Finished = new();
        public readonly ConcurrentQueue<int> Canceled = new();
        private int _running;
        public int Peak;

        public TaskCompletionSource Gate(int item) =>
            Gates.GetOrAdd(item, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public async Task Work(int item, CancellationToken ct)
        {
            var running = Interlocked.Increment(ref _running);
            InterlockedMax(ref Peak, running);
            Started.Enqueue(item);
            try
            {
                await Gate(item).Task.WaitAsync(ct);
                if (item < 0) throw new InvalidOperationException("one item fails");
                Finished.Enqueue(item);
            }
            catch (OperationCanceledException) { Canceled.Enqueue(item); throw; }
            finally { Interlocked.Decrement(ref _running); }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not reached");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task Runs_every_item_nearest_first_with_at_most_the_worker_count_at_once()
    {
        var probe = new Probe();
        var queue = new ArtworkPrefetchQueue<int>(workers: 4, capacity: 100, probe.Work);
        queue.Replace(Enumerable.Range(1, 10));
        await Until(() => probe.Started.Count == 4);
        Assert.Equal([1, 2, 3, 4], probe.Started.Order());
        Assert.Equal(6, queue.PendingCount);
        foreach (var item in Enumerable.Range(1, 10)) probe.Gate(item).TrySetResult();
        await Until(() => probe.Finished.Count == 10 && queue.RunningCount == 0);
        Assert.Equal(4, probe.Peak);
    }

    [Fact]
    public async Task Replace_drops_work_not_started_and_lets_started_work_finish()
    {
        var probe = new Probe();
        var queue = new ArtworkPrefetchQueue<int>(workers: 2, capacity: 100, probe.Work);
        queue.Replace([1, 2, 3, 4]);
        await Until(() => probe.Started.Count == 2);
        queue.Replace([10, 11]);
        Assert.Equal(2, queue.PendingCount);
        probe.Gate(1).TrySetResult(); probe.Gate(2).TrySetResult();
        probe.Gate(10).TrySetResult(); probe.Gate(11).TrySetResult();
        await Until(() => probe.Finished.Count == 4 && queue.RunningCount == 0);
        Assert.Equal([1, 2, 10, 11], probe.Finished.Order());
        Assert.DoesNotContain(3, probe.Started);
        Assert.DoesNotContain(4, probe.Started);
        Assert.Empty(probe.Canceled);
    }

    [Fact]
    public async Task Clear_cancels_running_work_and_newer_work_still_runs_on_every_worker()
    {
        var probe = new Probe();
        var queue = new ArtworkPrefetchQueue<int>(workers: 2, capacity: 100, probe.Work);
        queue.Replace([1, 2, 3]);
        await Until(() => probe.Started.Count == 2);
        queue.Clear();
        await Until(() => probe.Canceled.Count == 2);
        Assert.Equal(0, queue.PendingCount);
        // The cleared workers go on with the newer neighborhood instead of leaving it waiting.
        queue.Replace([20, 21, 22]);
        await Until(() => probe.Started.Count == 4);
        probe.Gate(20).TrySetResult(); probe.Gate(21).TrySetResult(); probe.Gate(22).TrySetResult();
        await Until(() => probe.Finished.Count == 3 && queue.RunningCount == 0);
        Assert.DoesNotContain(3, probe.Started);
        Assert.Equal(2, probe.Peak);
    }

    [Fact]
    public async Task A_failing_item_never_stops_the_others()
    {
        var probe = new Probe();
        var queue = new ArtworkPrefetchQueue<int>(workers: 1, capacity: 100, probe.Work);
        queue.Replace([-1, 2]);
        probe.Gate(-1).TrySetResult(); probe.Gate(2).TrySetResult();
        await Until(() => probe.Finished.Contains(2) && queue.RunningCount == 0);
    }

    [Fact]
    public async Task Keeps_at_most_the_capacity()
    {
        var probe = new Probe();
        var queue = new ArtworkPrefetchQueue<int>(workers: 1, capacity: 3, probe.Work);
        queue.Replace(Enumerable.Range(1, 50));
        await Until(() => probe.Started.Count == 1);
        Assert.Equal(2, queue.PendingCount);
        foreach (var item in Enumerable.Range(1, 50)) probe.Gate(item).TrySetResult();
        await Until(() => queue.RunningCount == 0);
        Assert.Equal([1, 2, 3], probe.Finished.Order());
    }

    [Fact]
    public void Rejects_an_empty_lane()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArtworkPrefetchQueue<int>(0, 1, (_, _) => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArtworkPrefetchQueue<int>(1, 0, (_, _) => Task.CompletedTask));
    }
}
