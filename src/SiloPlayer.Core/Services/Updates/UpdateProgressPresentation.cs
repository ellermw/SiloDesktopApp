using System.Globalization;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>Text for the update progress window. Counts describe only what is downloaded.</summary>
public static class UpdateProgressPresentation
{
    public const string DownloadingText = "Downloading updates…";
    public static string Megabytes(long bytes) => (Math.Max(0, bytes) / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);
    public static string Files(int done, int total) => $"{Math.Clamp(done, 0, Math.Max(0, total))} / {Math.Max(0, total)}";
    public static string Size(long done, long total) => $"{Megabytes(Math.Clamp(done, 0, Math.Max(0, total)))} / {Megabytes(total)} MB";
    public static string Speed(double bytesPerSecond) => bytesPerSecond >= 1048576
        ? (bytesPerSecond / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s"
        : (Math.Max(0, bytesPerSecond) / 1024).ToString("0", CultureInfo.InvariantCulture) + " KB/s";
}

/// <summary>Download speed over a short sliding window, so the readout does not jump with every chunk.</summary>
public sealed class UpdateSpeedMeter(TimeSpan window)
{
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromMilliseconds(500);
    private readonly Queue<(TimeSpan At, long Bytes)> samples = new();

    /// <summary>Records the cumulative byte count at a monotonic time; returns bytes per second once enough time has passed.</summary>
    public double? Add(TimeSpan at, long bytes)
    {
        if (samples.Count > 0 && (bytes < samples.Last().Bytes || at < samples.Last().At)) samples.Clear();
        samples.Enqueue((at, bytes));
        // Keep the newest sample at or before the window start as the baseline.
        while (samples.Count > 2 && at - samples.ElementAt(1).At >= window) samples.Dequeue();
        var first = samples.Peek();
        var span = at - first.At;
        return span < MinimumSpan ? null : (bytes - first.Bytes) / span.TotalSeconds;
    }

    public void Reset() => samples.Clear();
}
