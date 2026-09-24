using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Player;

/// <summary>Bounded flight recorder. Playback threads only append in-memory facts.</summary>
internal sealed class PlaybackDiagnostics : IDisposable
{
    private const int Capacity = 2048;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private static readonly object DiskGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly Queue<Entry> _history = new();
    private readonly Queue<Entry> _pending = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Timer _timer;
    private readonly string _directory;
    private readonly Func<object?>? _sample;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private Entry[]? _before;
    private readonly List<Entry> _after = new();
    private string? _incidentFile;
    private long _incidentStarted;
    private long _previousTick;
    private long _dropped;
    private int _queued;
    private bool _disposed;
    private string? _writeError;
    private sealed record Entry(string Trace, DateTimeOffset Utc, double ElapsedMs, string Event, object? Data);
    public string DirectoryPath => _directory;

    public PlaybackDiagnostics(Func<object?>? sample = null, string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer"), "diagnostics");
        _sample = sample;
        Record("trace_start", new { schema = 1, build = typeof(PlaybackDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            runtime = Environment.Version.ToString(), process = Environment.ProcessId, historyLimit = Capacity });
        _previousTick = _started;
        _timer = new Timer(_ => Tick(), null, 1000, 1000);
    }

    // Callers provide only numeric state, fixed classifications and validated
    // network addresses. Never pass URLs, headers, exception messages or tokens.
    public void Record(string kind, object? data = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var entry = new Entry(_id, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(_started).TotalMilliseconds, kind, data);
            if (_history.Count == Capacity) _history.Dequeue();
            _history.Enqueue(entry);
            if (_pending.Count == Capacity) { _pending.Dequeue(); _dropped++; }
            _pending.Enqueue(entry);
            if (_before != null && _after.Count < Capacity) _after.Add(entry);
        }
    }

    public void Incident(string reason)
    {
        Record("incident", new { reason });
        lock (_gate)
        {
            if (_disposed) return;
            if (_before == null)
            {
                _before = _history.ToArray();
                _after.Clear();
                _incidentStarted = Stopwatch.GetTimestamp();
                _incidentFile = $"stall-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{_id[..8]}.json";
            }
        }
        RequestFlush();
    }

    private void Tick()
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            var interval = Stopwatch.GetElapsedTime(Interlocked.Exchange(ref _previousTick, now), now).TotalMilliseconds;
            Record("sample", new { intervalMs = interval, player = _sample?.Invoke(),
                managedBytes = GC.GetTotalMemory(false), gcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds,
                threadPoolThreads = ThreadPool.ThreadCount, threadPoolPending = ThreadPool.PendingWorkItemCount });
            RequestFlush();
        }
        catch (Exception ex) { Record("sampler_error", new { type = ex.GetType().Name }); }
    }

    private void RequestFlush()
    {
        if (Interlocked.Exchange(ref _queued, 1) != 0) return;
        _ = Task.Run(() => { try { FlushCore(); } finally { Volatile.Write(ref _queued, 0); } });
    }

    internal Task FlushAsync() => Task.Run(FlushCore);

    private void FlushCore()
    {
        lock (_writeGate)
        {
            Entry[] batch;
            object? incident;
            string? incidentFile;
            long dropped;
            bool complete;
            lock (_gate)
            {
                batch = _pending.ToArray();
                _pending.Clear();
                dropped = _dropped;
                incidentFile = _incidentFile;
                complete = _disposed || Stopwatch.GetElapsedTime(_incidentStarted).TotalSeconds >= 30;
                incident = _before == null ? null : new { schema = 1, trace = _id,
                    complete,
                    dropped, writeError = _writeError, before = _before, after = _after.ToArray() };
            }
            try
            {
                lock (DiskGate)
                {
                    Directory.CreateDirectory(_directory);
                    var rolling = Path.Combine(_directory, "playback-current.jsonl");
                    if (File.Exists(rolling) && new FileInfo(rolling).Length >= 4 * 1024 * 1024)
                        File.Move(rolling, rolling + ".1", overwrite: true);
                    if (batch.Length > 0)
                        File.AppendAllLines(rolling, batch.Select(entry => JsonSerializer.Serialize(entry, JsonOptions)));
                    if (incident != null && incidentFile != null)
                    {
                        var path = Path.Combine(_directory, incidentFile);
                        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(incident, JsonOptions));
                        File.Move(path + ".tmp", path, overwrite: true);
                        if (complete) lock (_gate) { _before = null; _after.Clear(); _incidentFile = null; }
                        foreach (var old in new DirectoryInfo(_directory).GetFiles("stall-*.json")
                            .OrderByDescending(file => file.LastWriteTimeUtc).Skip(8)) old.Delete();
                    }
                    File.WriteAllText(Path.Combine(_directory, "recorder-status.json"), JsonSerializer.Serialize(new {
                        utc = DateTimeOffset.UtcNow, trace = _id, active = !_disposed, dropped, previousWriteError = _writeError }));
                    _writeError = null;
                }
            }
            catch (Exception ex)
            {
                _writeError = ex.GetType().Name;
                lock (_gate) _dropped += batch.Length;
            }
        }
    }

    public void Dispose()
    {
        Record("trace_end");
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _timer.Dispose();
        // No disk I/O or waiting on the playback/native thread.
        _ = Task.Run(async () => {
            // Retry final incident writes after transient sharing/disk errors, without
            // keeping the player alive or blocking shutdown. Memory remains bounded.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                FlushCore();
                if (_writeError == null) break;
                await Task.Delay(1000 * (attempt + 1)).ConfigureAwait(false);
            }
        });
    }
}
