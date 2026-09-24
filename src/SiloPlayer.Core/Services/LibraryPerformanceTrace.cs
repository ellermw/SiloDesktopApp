using System.Diagnostics;

namespace SiloPlayer.Core.Services;

/// <summary>Opt-in timings for a local browsing diagnosis; never logs URLs or item data.</summary>
public static class LibraryPerformanceTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SILOPLAYER_LIBRARY_DIAGNOSTICS") == "1";

    public static Scope Measure(string? operation, double minimumMilliseconds = 0) =>
        Enabled && operation != null ? new Scope(operation, minimumMilliseconds) : default;

    public readonly struct Scope : IDisposable
    {
        private readonly string? _operation;
        private readonly double _minimumMilliseconds;
        private readonly long _started;
        private readonly DateTime _startedUtc;

        internal Scope(string operation, double minimumMilliseconds)
        {
            _operation = operation;
            _minimumMilliseconds = minimumMilliseconds;
            _started = Stopwatch.GetTimestamp();
            _startedUtc = DateTime.UtcNow;
        }

        public void Dispose()
        {
            if (_operation == null) return;
            var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            if (elapsed < _minimumMilliseconds) return;
            var line = $"pid={Environment.ProcessId} start_utc={_startedUtc:O} phase={_operation} elapsed_ms={elapsed:F1}";
            // Disk logging must not become additional work on the UI thread.
            _ = Task.Run(() => LocalLog.AppendLine("library_performance.txt", line));
        }
    }
}
