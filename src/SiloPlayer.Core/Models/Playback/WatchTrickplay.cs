namespace SiloPlayer.Core.Models.Playback;

/// <summary>
/// The seek-bar previews of one file: <c>WatchTrickplay</c> from
/// <c>GET /api/v2/watch/{id}/trickplay</c> (getWatchTrickplay, official
/// silo-server 3b2b32168). Sheet URLs carry their own authorization and stop
/// working at <see cref="ExpiresAt"/>.
/// </summary>
public sealed class WatchTrickplayResponse
{
    public string? FileId { get; set; }
    public long IntervalMs { get; set; }
    public long ThumbnailWidth { get; set; }
    public long ThumbnailHeight { get; set; }
    public long TileColumns { get; set; }
    public long TileRows { get; set; }
    public long ThumbnailCount { get; set; }
    public List<WatchTrickplaySheet>? Sheets { get; set; }
    public string? ExpiresAt { get; set; }
}

public sealed class WatchTrickplaySheet
{
    public long Index { get; set; }
    public string? Url { get; set; }
}
