using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Models.Catalog;

public class MediaItemDetail
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public int Year { get; set; }
    public string Overview { get; set; } = "";
    public string? Tagline { get; set; }
    public int Runtime { get; set; }
    public string? ContentRating { get; set; }
    public List<string> Genres { get; set; } = [];
    public double? RatingTmdb { get; set; }
    public int? RatingRtCritic { get; set; }
    public int? RatingRtAudience { get; set; }
    public string? ImdbId { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public string? LogoUrl { get; set; }
    public List<CastMember> Cast { get; set; } = [];
    public UserState? UserState { get; set; }
}

public class CastMember
{
    public string Name { get; set; } = "";
    public string? Character { get; set; }
    public int Order { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhotoThumbhash { get; set; }
}
