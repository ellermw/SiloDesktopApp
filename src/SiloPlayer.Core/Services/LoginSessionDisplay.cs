using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

public static class LoginSessionDisplay
{
    /// <summary>A device-code sign-in sees no other session metadata, and receives its own dates as placeholders.</summary>
    public static bool HasRedactedDetails(AuthSession session, AuthSession? current)
        => current is not null && !session.IsCurrent && session.Id != current.Id &&
            string.IsNullOrEmpty(session.DeviceId) && string.IsNullOrEmpty(session.DeviceName) &&
            string.IsNullOrEmpty(session.DevicePlatform) && string.IsNullOrEmpty(session.IpAddress) &&
            session.LastSeenAt is null && !string.IsNullOrEmpty(current.CreatedAt) && !string.IsNullOrEmpty(current.ExpiresAt) &&
            session.CreatedAt == current.CreatedAt && session.ExpiresAt == current.ExpiresAt;

    public static string Name(AuthSession session)
    {
        var raw = session.DeviceName?.Trim() ?? "";
        if (raw.Length == 0) return "Unknown device";
        if (!string.IsNullOrWhiteSpace(session.DevicePlatform)) return raw;
        var platform = Platform(raw);
        var app = Match(raw, @"\bSilo\b", true) ? "Silo app" : Match(raw, @"Edg(?:e|A|iOS)?/") ? "Edge"
            : Match(raw, @"(?:Firefox|FxiOS)/") ? "Firefox" : Match(raw, @"(?:OPR|Opera)/") ? "Opera"
            : Match(raw, @"(?:Chrome|CriOS|HeadlessChrome)/") ? "Chrome" : Match(raw, @"Safari/") ? "Safari" : "";
        if (app.Length == 0 && !Match(raw, @"^[\w.-]+/\S")) return raw;
        return app.Length > 0 ? platform.Length > 0 ? $"{app} on {platform}" : app : platform.Length > 0 ? $"Device on {platform}" : raw;
    }

    public static string LastSeen(string? value, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(value, out var seen)) return "Last seen not recorded yet";
        var minutes = Math.Max(0, (now - seen).TotalMinutes);
        return minutes < 2 ? "Active now" : minutes < 60 ? $"Last seen {(int)minutes} min ago"
            : minutes < 1440 ? $"Last seen {(int)(minutes / 60)} hr ago" : $"Last seen {(int)(minutes / 1440)} days ago";
    }

    private static bool Match(string value, string pattern, bool insensitive = false)
        => Regex.IsMatch(value, pattern, insensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
    private static string Platform(string value)
        => Match(value, @"tvOS|Apple ?TV", true) ? "Apple TV" : Match(value, @"Android.*TV|Android TV", true) ? "Android TV"
            : Match(value, "Android", true) ? "Android" : Match(value, "iPad", true) ? "iPad" : Match(value, "iPhone", true) ? "iPhone"
            : Match(value, "iOS", true) ? "iOS" : Match(value, "Windows", true) ? "Windows" : Match(value, "Macintosh|macOS|Mac OS X", true) ? "macOS"
            : Match(value, "Linux", true) ? "Linux" : "";
}
