using SiloPlayer.Core.Models.HistoryImport;

namespace SiloPlayer.Core.Services;

/// <summary>The History Import form rules shared with the official WebUI (<c>HistoryImportSettings.utils.ts</c>).</summary>
public static class HistoryImportRules
{
    public const string EmbyConnect = "connect";
    public const string EmbyDirect = "direct";
    public const string EmbySaved = "saved";

    /// <summary>The server confirms direct Emby server URLs: an available import capability that says <c>emby_custom_server</c>. A missing member is false.</summary>
    public static bool SupportsEmbyDirect(HistoryImportCapability? capability) =>
        capability is { State: "available", EmbyCustomServer: true };

    /// <summary>The Emby mode in effect: Server URL falls back to Connect when the server does not (or no longer does) support it.</summary>
    public static string EffectiveEmbyMode(string mode, bool supportsDirect) => mode == EmbyDirect && !supportsDirect ? EmbyConnect : mode;

    /// <summary>The Emby modes offered, in order: Connect, Server URL only when supported, Saved.</summary>
    public static IReadOnlyList<string> EmbyModes(bool supportsDirect) => supportsDirect ? [EmbyConnect, EmbyDirect, EmbySaved] : [EmbyConnect, EmbySaved];

    /// <summary>A direct sign-in needs a server URL and a username; the password may be empty (an Emby account can have none).</summary>
    public static bool CanStartEmbyDirect(string? serverUrl, string? username) =>
        !string.IsNullOrWhiteSpace(serverUrl) && !string.IsNullOrWhiteSpace(username);

    /// <summary>An http or https address with a host and no user info, query or fragment (a base path is allowed).</summary>
    public static bool IsValidHttpServerUrl(string? raw)
    {
        var trimmed = (raw ?? "").Trim();
        if (!(trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            || trimmed.Contains('?') || trimmed.Contains('#')) return false;
        var authority = trimmed[(trimmed.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
        if (authority.Contains('@')) return false;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https" && parsed.Host.Length > 0 && parsed.UserInfo.Length == 0 && parsed.Query.Length == 0 && parsed.Fragment.Length == 0;
    }

    /// <summary>The part of the form a start error belongs to: the source, plus its mode for Emby and Plex.</summary>
    public static string FormScope(string source, string embyMode, string plexMode) => source switch
    {
        "emby" => "emby:" + embyMode,
        "plex" => "plex:" + plexMode,
        _ => "jellyfin",
    };
}
