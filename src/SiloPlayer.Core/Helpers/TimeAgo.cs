using System.Globalization;

namespace SiloPlayer.Core.Helpers;

/// <summary>
/// Single source of truth for relative-time formatting. WebUI has two formats:
/// <list type="bullet">
/// <item><c>FormatAdded</c> mirrors the current Silo WebUI time display —
/// "Added 5 minutes ago" with pluralization, returns null past 30 days, used by
/// ItemCard for "Added X ago" surfaces.</item>
/// <item><c>FormatShort</c> mirrors the inline <c>formatRelative</c> in
/// AdminPlaybackHistory / AdminUserDetail / AdminTasks — "5m ago" / "3h ago" /
/// "2d ago", no null threshold, used by admin tables.</item>
/// </list>
///
/// B29: Replaces three scattered ad-hoc implementations in
/// AdminPlaybackHistoryViewModel, AdminUserDetailViewModel, and AdminTasksPage that
/// each had subtly different output (e.g., negative time clamp missing).
/// </summary>
public static class TimeAgo
{
    private const int MINUTE = 60;
    private const int HOUR = 3600;
    private const int DAY = 86400;

    /// <summary>
    /// Long form: "Added 5 minutes ago" with pluralization. Returns <c>null</c>
    /// for unparseable input or dates older than 30 days — caller should fall
    /// back to an absolute date.
    /// </summary>
    public static string? FormatAdded(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        if (!TryParseServerTime(iso, out var date)) return null;

        var seconds = (long)(DateTimeOffset.UtcNow - date).TotalSeconds;
        if (seconds < 0) return "Added just now";

        if (seconds < MINUTE) return "Added just now";
        if (seconds < HOUR)
        {
            var m = (int)(seconds / MINUTE);
            return $"Added {m} {(m == 1 ? "minute" : "minutes")} ago";
        }
        if (seconds < DAY)
        {
            var h = (int)(seconds / HOUR);
            return $"Added {h} {(h == 1 ? "hour" : "hours")} ago";
        }

        var days = (int)(seconds / DAY);
        if (days <= 30)
        {
            return $"Added {days} {(days == 1 ? "day" : "days")} ago";
        }

        return null;
    }

    /// <summary>
    /// Short form: "5m ago" / "3h ago" / "2d ago" / "just now". Used by admin
    /// tables. Negative differences (clock skew) clamp to "just now". Returns
    /// the input string unchanged if it can't be parsed.
    /// </summary>
    public static string FormatShort(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (!TryParseServerTime(iso, out var date)) return iso;

        var diffMinutes = Math.Max(0, (long)(DateTimeOffset.UtcNow - date).TotalMinutes);
        if (diffMinutes < 1) return "just now";
        if (diffMinutes < 60) return $"{diffMinutes}m ago";
        var diffHours = diffMinutes / 60;
        if (diffHours < 24) return $"{diffHours}h ago";
        var diffDays = diffHours / 24;
        return $"{diffDays}d ago";
    }

    private static bool TryParseServerTime(string value, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out timestamp);
}
