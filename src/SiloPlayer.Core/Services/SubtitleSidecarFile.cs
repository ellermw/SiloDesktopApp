namespace SiloPlayer.Core.Services;

/// <summary>
/// Rules for downloading a subtitle sidecar to a local file before mpv
/// loads it. A synchronous libmpv <c>sub-add</c> of an HTTP URL blocks its
/// caller for the whole download; adding a local file does not.
/// </summary>
public static class SubtitleSidecarFile
{
    public const int MaxBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Local download suffix, including PGS. Unknown formats use a generic
    /// subtitle suffix so mpv can probe the bytes without fetching a URL itself.
    /// </summary>
    public static string DownloadExtension(string? mediaType, Uri url, string? codec)
        => Extension(mediaType, url, codec) ??
            (Path.GetExtension(url.AbsolutePath).Equals(".sup", StringComparison.OrdinalIgnoreCase) ||
             codec?.Trim().ToLowerInvariant() is "pgs" or "hdmv_pgs_subtitle" or "sup" ? ".sup" : ".sub");

    /// <summary>
    /// The local file extension mpv needs to identify the downloaded cues, or
    /// null when the sidecar is not a known text format.
    /// </summary>
    public static string? Extension(string? mediaType, Uri url, string? codec)
    {
        var fromType = mediaType?.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "text/vtt" => ".vtt",
            "text/x-ssa" or "text/x-ass" or "text/ass" => ".ass",
            "application/x-subrip" or "text/srt" or "text/x-srt" => ".srt",
            _ => null,
        };
        if (fromType != null) return fromType;
        var fromPath = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        if (fromPath is ".vtt" or ".srt" or ".ass" or ".ssa") return fromPath;
        return codec?.Trim().ToLowerInvariant() switch
        {
            "webvtt" or "vtt" => ".vtt",
            "subrip" or "srt" => ".srt",
            "ass" => ".ass",
            "ssa" => ".ssa",
            _ => null,
        };
    }
}
