namespace SiloPlayer.Core.Models.Catalog;

public class EpisodesResponse
{
    public List<Episode> Episodes { get; set; } = [];
}

public class Episode
{
    public string ContentId { get; set; } = "";
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = "";
    public string? Overview { get; set; }
    public string? AirDate { get; set; }
    public int Runtime { get; set; }
    public string? StillUrl { get; set; }
    public string? StillThumbhash { get; set; }
    public EpisodeUserData? UserData { get; set; }
    public List<EpisodeFile> Files { get; set; } = [];
}

public class EpisodeUserData
{
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool Played { get; set; }
}

public class EpisodeFile
{
    public int FileId { get; set; }
    public string Resolution { get; set; } = "";
    public string CodecVideo { get; set; } = "";
    public bool Hdr { get; set; }
    public int AudioChannels { get; set; }
    public string Container { get; set; } = "";
    public long FileSize { get; set; }
}
