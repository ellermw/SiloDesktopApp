using System.Collections.Concurrent;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Defers log file writes off hot paths. Lines keep the time they were
/// recorded, so a delayed flush cannot misdate them against logs written
/// directly to the same file, and serialized flushes keep their own order.
/// </summary>
public sealed class QueuedLocalLog(string? logDirectory = null, Func<DateTime>? clock = null)
{
    private readonly ConcurrentQueue<(string File, DateTime RecordedAt, string Line)> _queue = new();
    private readonly object _flushGate = new();
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);

    public void Enqueue(string fileName, string line) => _queue.Enqueue((fileName, _clock(), line));

    public void Flush()
    {
        // Wait rather than skip: an explicit flush must not return while a
        // timer flush still holds dequeued lines that are not yet written.
        lock (_flushGate)
        {
            if (_queue.IsEmpty)
                return;
            var batches = new Dictionary<string, List<(DateTime, string)>>();
            while (_queue.TryDequeue(out var entry))
            {
                if (!batches.TryGetValue(entry.File, out var list))
                    batches[entry.File] = list = [];
                list.Add((entry.RecordedAt, entry.Line));
            }
            foreach (var (fileName, lines) in batches)
                LocalLog.AppendTimestampedLines(fileName, lines, logDirectory);
        }
    }
}
