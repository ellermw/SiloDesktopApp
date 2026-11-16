namespace SiloPlayer.Core.Models.Catalog;

public class SeasonsResponse
{
    public List<Season> Seasons { get; set; } = [];
}

public class Season
{
    public string ContentId { get; set; } = "";
    public int SeasonNumber { get; set; }
    public string Title { get; set; } = "";
    public List<string>? MachineTranslatedFields { get; set; }
    public string? AirDate { get; set; }
    public int EpisodeCount { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public SeasonUserData? UserData { get; set; }
    /// <summary>Episodes in the library out of known episodes; sent only while the profile's badges
    /// include the availability overlay, and absent for specials or incomplete episode metadata.</summary>
    public SiloPlayer.Core.Models.Home.CatalogAvailability? Availability { get; set; }
}

public class SeasonUserData
{
    public bool Played { get; set; }
    public int WatchedCount { get; set; }
    public int InProgressCount { get; set; }
    public int UnplayedCount { get; set; }
}
