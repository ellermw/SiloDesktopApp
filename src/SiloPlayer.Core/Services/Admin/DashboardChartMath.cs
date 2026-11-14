using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A sample on a chart's time grid; a null value is a missing sample and breaks the line.</summary>
public readonly record struct DashboardChartPoint(DateTimeOffset T, double? Value);

/// <summary>One stacked band of a line chart (egress by node).</summary>
public sealed record DashboardChartBand(string Key, string Label, int? SeriesSlot, bool Neutral, IReadOnlyList<DashboardChartPoint> Points);

/// <summary>One column of a stacked column chart, values in fixed series order.</summary>
public sealed record DashboardChartColumn(DateTimeOffset T, IReadOnlyList<double> Segments)
{
    public double Total => Segments.Sum(value => value > 0 && double.IsFinite(value) ? value : 0);
}

/// <summary>Pure chart geometry and shaping shared by the dashboard charts.</summary>
public static class DashboardChartMath
{
    public const int SeriesSlots = 5;

    /// <summary>Clamped categorical slot (0-based); charts never cycle hues.</summary>
    public static int SeriesSlot(int index) => Math.Min(Math.Max(index, 0), SeriesSlots - 1);

    /// <summary>Axis ticks at human numbers, ascending and covering [min, max].</summary>
    public static IReadOnlyList<double> NiceTicks(double min, double max, int count = 4, double? minStep = null)
    {
        var lo = double.IsFinite(min) ? min : 0;
        var hi = double.IsFinite(max) ? max : 0;
        if (lo > hi) (lo, hi) = (hi, lo);
        if (lo == hi) hi = lo + (lo == 0 ? 1 : Math.Abs(lo));
        var target = Math.Max(2, count);
        var rawStep = NiceNumber(NiceNumber(hi - lo, false) / (target - 1), true);
        var step = minStep is { } floor && double.IsFinite(floor) && floor > 0 ? Math.Max(rawStep, floor) : rawStep;
        var decimals = Math.Max(0, Math.Min(10, -(int)Math.Floor(Math.Log10(step))));
        var start = Math.Floor(lo / step) * step;
        var end = Math.Ceiling(hi / step) * step;
        var ticks = new List<double>();
        for (var i = 0; i <= 1000; i++)
        {
            var value = Math.Round(start + i * step, decimals, MidpointRounding.AwayFromZero);
            ticks.Add(value);
            if (value >= end) break;
        }
        return ticks;
    }

    private static double NiceNumber(double range, bool roundToNearest)
    {
        var exponent = Math.Floor(Math.Log10(range));
        var fraction = range / Math.Pow(10, exponent);
        double nice = roundToNearest
            ? fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10
            : fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * Math.Pow(10, exponent);
    }

    /// <summary>Contiguous runs of plotted points; a null y breaks the line rather than reading as zero.</summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Runs(IReadOnlyList<(double X, double? Y)> points)
    {
        var runs = new List<IReadOnlyList<(double, double)>>();
        var current = new List<(double, double)>();
        foreach (var (x, y) in points)
        {
            if (y is { } value && double.IsFinite(value) && double.IsFinite(x))
            {
                current.Add((x, value));
                continue;
            }
            if (current.Count > 0) { runs.Add(current); current = []; }
        }
        if (current.Count > 0) runs.Add(current);
        return runs;
    }

    /// <summary>Cumulative offsets of one stacked column; negative or missing values count as zero.</summary>
    public static IReadOnlyList<(int Index, double Value, double Start, double End)> StackSegments(IReadOnlyList<double> values)
    {
        var offset = 0d;
        var result = new List<(int, double, double, double)>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var value = double.IsFinite(values[i]) && values[i] > 0 ? values[i] : 0;
            var start = offset;
            offset += value;
            result.Add((i, value, start, offset));
        }
        return result;
    }

    /// <summary>
    /// Zero-filled buckets from <paramref name="from"/> to <paramref name="to"/>
    /// inclusive. Samples outside the window are ignored; the later sample wins a bucket.
    /// </summary>
    public static IReadOnlyList<(long T, TValue Value, bool Present)> TimeBuckets<TValue>(long from, long to, long stepMs,
        IEnumerable<(long T, TValue Value)> samples, TValue empty)
    {
        if (stepMs <= 0 || to < from) return [];
        var count = (int)Math.Min((to - from) / stepMs + 1, 10_000);
        var buckets = new (long, TValue, bool)[count];
        for (var i = 0; i < count; i++) buckets[i] = (from + i * stepMs, empty, false);
        foreach (var (t, value) in samples)
        {
            if (t < from) continue;
            var index = (t - from) / stepMs;
            if (index >= count) continue;
            buckets[index] = (buckets[index].Item1, value, true);
        }
        return buckets;
    }

    /// <summary>
    /// Dense minute series for the response window: missing minutes are null,
    /// a sampled minute with nothing streaming is a real zero.
    /// </summary>
    public static IReadOnlyList<DashboardChartPoint> TimeseriesPoints(DashboardTimeseries? series, Func<DashboardTimeseriesPoint, double> select)
    {
        if (series == null) return [];
        var resolution = series.ResolutionSeconds > 0 ? series.ResolutionSeconds : 60;
        var stepMs = resolution * 1000;
        var samples = (series.Points ?? []).Select(point =>
        {
            var value = select(point);
            return (T: point.T.ToUnixTimeMilliseconds(), Value: double.IsFinite(value) ? (double?)value : null);
        }).ToList();
        if (series.From is not { } from || series.To is not { } to || to < from)
            return samples.Select(sample => new DashboardChartPoint(DateTimeOffset.FromUnixTimeMilliseconds(sample.T), sample.Value)).ToArray();
        var start = FloorDiv(from.ToUnixTimeMilliseconds(), stepMs) * stepMs;
        var end = FloorDiv(to.ToUnixTimeMilliseconds(), stepMs) * stepMs;
        return TimeBuckets<double?>(start, end, stepMs, samples, null)
            .Select(bucket => new DashboardChartPoint(DateTimeOffset.FromUnixTimeMilliseconds(bucket.T), bucket.Present ? bucket.Value : null))
            .ToArray();
    }

    /// <summary>The newest sample, only while fresh enough to describe "now".</summary>
    public static DashboardTimeseriesPoint? LatestFreshPoint(DashboardTimeseries? series, TimeSpan maxAge, DateTimeOffset now)
    {
        var point = series?.Points is { Count: > 0 } points ? points[^1] : null;
        if (point == null) return null;
        return now - point.T > maxAge ? null : point;
    }

    private const int MaxNamedBands = 4;
    /// <summary>Band slots skip slot 1 (teal), which the total view uses for Downloads.</summary>
    private static readonly int[] BandColorSlots = [0, 2, 3, 4];

    /// <summary>
    /// Egress split into stacked bands per source, bottom first, or null when no
    /// stream node served egress (nothing to split). Up to four named sources;
    /// the rest plus unattributed egress share "Other" so the top edge is the total.
    /// </summary>
    public static IReadOnlyList<DashboardChartBand>? EgressBands(DashboardTimeseries? series)
    {
        var sources = series?.EgressSources ?? [];
        if (series == null || !sources.Any(source => source.Kind == "node")) return null;
        var points = series.Points ?? [];
        var volume = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var point in points)
            foreach (var (key, kbps) in point.EgressBySource ?? [])
                volume[key] = volume.GetValueOrDefault(key) + kbps;
        var kept = sources
            .OrderByDescending(source => source.EgressBytes ?? 0)
            .ThenByDescending(source => volume.GetValueOrDefault(source.Key))
            .ThenBy(source => source.Key, StringComparer.Ordinal)
            .Take(MaxNamedBands)
            .Select(source => source.Key)
            .ToHashSet(StringComparer.Ordinal);
        var keptSources = sources.Where(source => kept.Contains(source.Key)).ToList();
        var colors = AssignColors(keptSources);
        var labels = BandLabels(keptSources);
        var named = keptSources.Select(source => new DashboardChartBand(
            source.Key, labels[source.Key], colors.GetValueOrDefault(source.Key, BandColorSlots[0]), false,
            TimeseriesPoints(series, point => (point.EgressBySource?.GetValueOrDefault(source.Key) ?? 0) / 1000d))).ToList();
        var other = TimeseriesPoints(series, point =>
        {
            var keptKbps = (point.EgressBySource ?? []).Where(pair => kept.Contains(pair.Key)).Sum(pair => (double)pair.Value);
            return Math.Max(point.EgressKbps - keptKbps, 0) / 1000d;
        });
        if (other.Any(point => (point.Value ?? 0) > 0))
            named.Insert(0, new DashboardChartBand("other", DashboardText.T("web.admin.dashboard.widgets.egress.otherBand"), null, true, other));
        return named;
    }

    private static Dictionary<string, string> BandLabels(IReadOnlyList<DashboardEgressSource> sources)
    {
        var uses = sources.GroupBy(source => source.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return sources.ToDictionary(source => source.Key, source => uses[source.Name] > 1 && source.NodeId != null
            ? DashboardText.T("web.admin.dashboard.widgets.egress.duplicateNodeName", ("name", source.Name), ("id", source.NodeId))
            : source.Name, StringComparer.Ordinal);
    }

    private static int PreferredSlot(DashboardEgressSource source)
    {
        if (source.Kind == "api") return 0;
        return long.TryParse(source.NodeId, out var id) && id >= 0 ? (int)(id % BandColorSlots.Length) : 0;
    }

    private static Dictionary<string, int> AssignColors(IReadOnlyList<DashboardEgressSource> sources)
    {
        static double NodeNumber(DashboardEgressSource source) => double.TryParse(source.NodeId, out var n) ? n : 0;
        var byIdentity = sources.OrderByDescending(source => source.Kind == "api").ThenBy(NodeNumber).ToList();
        var taken = new HashSet<int>();
        var colors = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in byIdentity)
        {
            var preferred = PreferredSlot(source);
            for (var offset = 0; offset < BandColorSlots.Length; offset++)
            {
                var index = (preferred + offset) % BandColorSlots.Length;
                if (taken.Add(index))
                {
                    colors[source.Key] = BandColorSlots[index];
                    break;
                }
            }
        }
        return colors;
    }

    public const long DefaultPlaybackBucketSeconds = 3600;

    /// <summary>
    /// The columns of the playback window, oldest first, zero-filled on the
    /// bucket width the server grouped by. Both edges snap to that grid, so the
    /// leading bucket is the one containing now − hours.
    /// </summary>
    public static IReadOnlyList<DashboardChartColumn> PlaybackColumns(IReadOnlyList<DashboardPlaybackBucket>? buckets,
        int hours, long bucketSeconds, DateTimeOffset now)
    {
        var stepMs = (bucketSeconds > 0 ? bucketSeconds : DefaultPlaybackBucketSeconds) * 1000;
        var nowMs = now.ToUnixTimeMilliseconds();
        var to = FloorDiv(nowMs, stepMs) * stepMs;
        var from = Math.Min(to, FloorDiv(nowMs - Math.Max(0, hours) * 3_600_000L, stepMs) * stepMs);
        var samples = (buckets ?? []).Select(bucket => (
            T: FloorDiv(bucket.Hour.ToUnixTimeMilliseconds(), stepMs) * stepMs,
            Value: (IReadOnlyList<double>)[bucket.Direct, bucket.Remux, bucket.Transcode]));
        return TimeBuckets<IReadOnlyList<double>>(from, to, stepMs, samples, [0d, 0d, 0d])
            .Select(bucket => new DashboardChartColumn(DateTimeOffset.FromUnixTimeMilliseconds(bucket.T), bucket.Value))
            .ToArray();
    }

    private static long FloorDiv(long value, long divisor) => (long)Math.Floor(value / (double)divisor);
}
