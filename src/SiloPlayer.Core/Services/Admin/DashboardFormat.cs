using System.Globalization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Number, size, rate and time formatting with the WebUI dashboard's rules.</summary>
public static class DashboardFormat
{
    public const string Dash = "—";
    public const string EnDash = "–";

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Integer(long value) => value.ToString("N0", Culture);

    /// <summary>Fixed fraction digits without grouping (WebUI formatDecimal).</summary>
    public static string Decimal(double value, int maxFractionDigits, int? minFractionDigits = null)
    {
        var min = minFractionDigits ?? maxFractionDigits;
        var rounded = Math.Round(value, maxFractionDigits, MidpointRounding.AwayFromZero);
        var format = "0" + (maxFractionDigits > 0 ? "." + new string('0', min) + new string('#', maxFractionDigits - min) : "");
        return rounded.ToString(format, Culture);
    }

    public static string FileCount(long? count)
        => count is { } value ? DashboardText.T("web.admin.dashboard.format.fileCount", DashboardText.Count(value)) : Dash;

    /// <summary>Sub-hour totals stay in minutes so a short session does not read "0.0h".</summary>
    public static string WatchTime(long? totalSeconds)
    {
        if (totalSeconds is not { } seconds || seconds <= 0) return "0m";
        if (seconds < 3600) return Math.Max(1, (long)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero)) + "m";
        var hours = seconds / 3600.0;
        return Math.Round(hours, 1, MidpointRounding.AwayFromZero).ToString("#,0.#", Culture) + "h";
    }

    /// <summary>Small rates keep one decimal so a trickle does not read as idle.</summary>
    public static string MbpsValue(double mbps)
    {
        if (double.IsNaN(mbps) || double.IsInfinity(mbps)) return Dash;
        var decimals = mbps > 0 && mbps < 10 ? 1 : 0;
        return Math.Round(mbps, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 1 ? "#,0.0" : "#,0", Culture);
    }

    public static string Mbps(double mbps)
        => double.IsNaN(mbps) || double.IsInfinity(mbps) ? Dash : MbpsValue(mbps) + " Mbps";

    private static readonly string[] DataUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Decimal units with one decimal below 100; unit chosen after rounding.</summary>
    public static string DataTotal(double? bytes)
    {
        if (bytes is not { } value || double.IsNaN(value) || double.IsInfinity(value) || value < 0) return Dash;
        var unit = 0;
        while (true)
        {
            var decimals = unit > 0 && value < 100 ? 1 : 0;
            var rounded = Math.Round(value, decimals, MidpointRounding.ToEven);
            rounded = double.Parse(value.ToString("F" + decimals, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (rounded >= 1000 && unit < DataUnits.Length - 1)
            {
                value /= 1000;
                unit++;
                continue;
            }
            var shown = unit > 0 && rounded < 100 ? 1 : 0;
            return rounded.ToString(shown == 1 ? "#,0.0" : "#,0", Culture) + " " + DataUnits[unit];
        }
    }

    /// <summary>WebUI formatFileSize: 1024-based steps, one decimal, SI or IEC unit names.</summary>
    public static string FileSize(double? bytes, string fallback = "", bool iec = false)
    {
        if (bytes is not { } value || value <= 0 || double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        var keys = iec
            ? ("web.common.format.size.gib", "web.common.format.size.mib", "web.common.format.size.kib")
            : ("web.common.format.size.gb", "web.common.format.size.mb", "web.common.format.size.kb");
        string Size(double scaled) => Decimal(scaled, 1);
        if (value >= 1024d * 1024 * 1024) return DashboardText.T(keys.Item1, ("size", Size(value / (1024d * 1024 * 1024))));
        if (value >= 1024d * 1024) return DashboardText.T(keys.Item2, ("size", Size(value / (1024d * 1024))));
        if (value >= 1024) return DashboardText.T(keys.Item3, ("size", Size(value / 1024)));
        return DashboardText.T("web.common.format.size.bytes", ("size", (long)value));
    }

    /// <summary>Storage tile: TB from one binary terabyte up, otherwise whole GB.</summary>
    public static string Storage(long bytes)
    {
        var gigabytes = bytes / (1024d * 1024 * 1024);
        var terabytes = gigabytes / 1024;
        return terabytes >= 1 ? Decimal(terabytes, 1) + " TB" : Decimal(gigabytes, 0) + " GB";
    }

    public static string DurationSeconds(long totalSeconds)
    {
        if (totalSeconds < 60) return totalSeconds + "s";
        if (totalSeconds < 3600)
        {
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return seconds == 0 ? minutes + "m" : $"{minutes}m {seconds}s";
        }
        var hours = totalSeconds / 3600;
        var rest = totalSeconds % 3600 / 60;
        return rest == 0 ? hours + "h" : $"{hours}h {rest}m";
    }

    /// <summary>A finished scan's span, or how long a running one has gone.</summary>
    public static string ScanDuration(DateTimeOffset? startedAt, DateTimeOffset? completedAt, DateTimeOffset now)
    {
        if (startedAt is not { } started) return Dash;
        var ended = completedAt ?? now;
        if (ended < started) return Dash;
        return DurationSeconds((long)Math.Round((ended - started).TotalSeconds, MidpointRounding.AwayFromZero));
    }

    public static string Uptime(DateTimeOffset? startedAt, DateTimeOffset now)
    {
        if (startedAt is not { } started) return Dash;
        var seconds = Math.Max(0, (long)Math.Floor((now - started).TotalSeconds));
        if (seconds < 86_400) return DurationSeconds(seconds);
        var days = seconds / 86_400;
        var hours = seconds % 86_400 / 3600;
        return hours == 0 ? days + "d" : $"{days}d {hours}h";
    }

    public static string Latency(double latencyMs)
    {
        if (double.IsNaN(latencyMs) || double.IsInfinity(latencyMs)) return Dash;
        return Math.Round(latencyMs, latencyMs < 10 ? 2 : 0, MidpointRounding.AwayFromZero).ToString("#,0.##", Culture) + " ms";
    }

    /// <summary>"just now", "5m ago", "3h ago", "2d ago" with floor or round arithmetic.</summary>
    public static string? RelativeTime(DateTimeOffset? value, DateTimeOffset now, bool floor = false, string? justNowLabel = null)
    {
        if (value is not { } date) return null;
        var diff = (now - date).TotalMilliseconds;
        double Unit(double x) => floor ? Math.Floor(x) : Math.Round(x, MidpointRounding.AwayFromZero);
        var minutes = Unit(diff / 60_000);
        if (minutes < 1) return justNowLabel ?? DashboardText.T("web.common.date.justNow");
        if (minutes < 60) return DashboardText.T("web.common.date.minutesAgo", DashboardText.Count((long)minutes));
        var hours = Unit(diff / 3_600_000);
        if (hours < 24) return DashboardText.T("web.common.date.hoursAgo", DashboardText.Count((long)hours));
        var days = Unit(diff / 86_400_000);
        return DashboardText.T("web.common.date.daysAgo", DashboardText.Count((long)days));
    }

    /// <summary>Clock time for a timestamp in the viewer's zone ("3:45 PM").</summary>
    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("t", Culture);

    /// <summary>Hour tick without minutes ("3 PM").</summary>
    public static string HourTick(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        var pattern = Culture.DateTimeFormat.ShortTimePattern;
        var hourOnly = pattern.Contains('H') ? "HH" : "h tt";
        return local.ToString(hourOnly, Culture).Trim();
    }

    /// <summary>"Aug 26": short enough for an axis tick.</summary>
    public static string DayLabel(DateTimeOffset time) => time.ToLocalTime().ToString("MMM d", Culture);

    public static string RangeTimestamp(DashboardWidgetRange range, DateTimeOffset time, bool withTime)
    {
        if (range is DashboardWidgetRange.Hour or DashboardWidgetRange.Day) return Time(time);
        var day = DayLabel(time);
        return withTime ? DashboardText.T("web.admin.dashboard.range.dayWithTime", ("day", day), ("time", Time(time))) : day;
    }

    /// <summary>The header's "Updated … ago" line.</summary>
    public static string UpdatedLabel(DateTimeOffset now, DateTimeOffset updatedAt)
    {
        var minutes = (long)Math.Floor(Math.Max(0, (now - updatedAt).TotalMilliseconds) / 60_000);
        if (minutes < 1) return DashboardText.T("web.admin.dashboard.page.updatedJustNow");
        return DashboardText.T("web.admin.dashboard.page.updatedMinutesAgo", ("minutes", minutes), ("minutes_text", Integer(minutes)));
    }

    /// <summary>WebUI formatBitsPerSecond: decimal network units.</summary>
    public static string? BitsPerSecond(double? bps)
    {
        if (bps is not { } value || double.IsNaN(value) || value < 0) return null;
        if (value >= 1e9) return Decimal(value / 1e9, 1) + " Gbps";
        if (value >= 1e6) return Decimal(value / 1e6, 1) + " Mbps";
        if (value >= 1e3) return Math.Round(value / 1e3, MidpointRounding.AwayFromZero) + " kbps";
        return Math.Round(value, MidpointRounding.AwayFromZero) + " bps";
    }

    public static int ClampPercent(double value) => (int)Math.Min(100, Math.Max(0, Math.Round(value, MidpointRounding.AwayFromZero)));
}
