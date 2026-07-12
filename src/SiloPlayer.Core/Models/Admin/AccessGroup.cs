namespace SiloPlayer.Core.Models.Admin;

public sealed class AccessGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<int>? LibraryIds { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public bool DownloadAllowed { get; set; }
    public bool DownloadTranscodeAllowed { get; set; }
    public int MaxStreams { get; set; }
    public int MaxTranscodes { get; set; }
    public List<string>? AllowedPermissions { get; set; }
    public bool RequestsAllowed { get; set; }
    public bool IsDefault { get; set; }
    public int MemberCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UpdateAccessGroupRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<int>? LibraryIds { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public bool DownloadAllowed { get; set; }
    public bool DownloadTranscodeAllowed { get; set; }
    public int MaxStreams { get; set; }
    public int MaxTranscodes { get; set; }
    public List<string>? AllowedPermissions { get; set; }
    public bool RequestsAllowed { get; set; }
    public bool IsDefault { get; set; }
}
