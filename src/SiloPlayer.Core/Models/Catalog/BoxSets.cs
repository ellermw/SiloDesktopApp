using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Home;
namespace SiloPlayer.Core.Models.Catalog;
public class BoxSetSummary
{
    public string Id { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Overview { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public int MovieCount { get; set; }
    public int PlayedCount { get; set; }
    public int FirstYear { get; set; }
    public int LastYear { get; set; }
    [JsonIgnore] public string Years => FirstYear <= 0 ? "" : LastYear > FirstYear ? $"{FirstYear}–{LastYear}" : FirstYear.ToString();
    [JsonIgnore] public string ProgressText => $"{Math.Clamp(PlayedCount, 0, Math.Max(0, MovieCount))} / {MovieCount} watched";
}
public sealed class BoxSetDetail : BoxSetSummary
{
    public List<MediaItem> Items { get; set; } = [];
    [JsonIgnore] public MediaItem? PlayTarget => Items.FirstOrDefault(x => IsMovie(x) && x.UserState?.Played != true) ?? Items.FirstOrDefault(IsMovie);
    private static bool IsMovie(MediaItem x) => x.Type == "movie" && !string.IsNullOrWhiteSpace(x.ContentId) && !x.ContentId.StartsWith("box_set:", StringComparison.Ordinal);
    [JsonIgnore] public string PlayLabel => PlayedCount > 0 && PlayedCount < MovieCount ? "Continue" : "Play";
}
public sealed class BoxSetList { public List<BoxSetSummary> Items { get; set; } = []; }
public sealed class ItemBoxSetResponse { public BoxSetDetail? BoxSet { get; set; } }
