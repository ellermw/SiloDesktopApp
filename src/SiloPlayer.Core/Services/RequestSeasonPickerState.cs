using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

/// <summary>Preserves the distinction between an untouched server choice and an explicit season selection.</summary>
public sealed class RequestSeasonPickerState
{
    private readonly RequestMediaDetail _item;
    private readonly DateOnly _today;
    private bool _changed;
    public RequestSeasonPickerState(RequestMediaDetail item, DateOnly today)
    {
        _item = item; _today = today;
        Seasons = item.Seasons ?? [];
        Choices = item.Request.Requestable ? Seasons.Where(Requestable).ToList() : [];
        Selected = Choices.Where(Aired).Select(season => season.SeasonNumber).Distinct().Order().ToList();
        var regular = Seasons.Where(season => season.SeasonNumber > 0).ToList();
        var aired = regular.Where(Aired).OrderByDescending(season => season.SeasonNumber).FirstOrDefault();
        var latest = aired ?? regular.Where(season => !string.IsNullOrWhiteSpace(season.AirDate)).OrderBy(season => season.SeasonNumber).FirstOrDefault();
        Latest = latest != null && Requestable(latest) && item.Request.Requestable ? latest.SeasonNumber : null;
        Upcoming = Choices.Where(season => season.SeasonNumber > 0 && !Aired(season)).Select(season => season.SeasonNumber).Order().ToList();
    }
    public List<RequestMediaSeason> Seasons { get; }
    public List<RequestMediaSeason> Choices { get; }
    public List<int> Selected { get; private set; }
    public int? Latest { get; }
    public List<int> Upcoming { get; }
    public bool ServerChooses => !_changed && (Seasons.Count == 0 || Selected.Count > 0 || _item.Availability != "available");
    public bool CanSubmit => _item.Request.Requestable && (ServerChooses || Selected.Count > 0);
    public bool AllSelected => Choices.Count > 0 && Choices.All(season => Selected.Contains(season.SeasonNumber));
    public List<int>? SubmissionSeasons => ServerChooses ? null : Selected.ToList();
    public string SubmitLabel => Selected.Count > 0 ? "Request " + FormatSeasons(Selected) : ServerChooses ? "Request series" : "Request";
    public string? Description => Seasons.Count == 0 ? "TMDB lists no seasons for this series yet; the request covers the whole series."
        : !_changed && Selected.Count == 0 && Choices.Count > 0 ? _item.Availability == "available" ? "Pick the upcoming seasons to request." : "No season has aired yet, so the request covers the whole series unless you pick seasons." : null;
    public bool IsChoosable(RequestMediaSeason season) => Choices.Contains(season);
    public void SetSeason(int number, bool selected)
    {
        _changed = true;
        Selected = selected ? Selected.Append(number).Where(value => Choices.Any(season => season.SeasonNumber == value)).Distinct().Order().ToList() : Selected.Where(value => value != number).ToList();
    }
    public void SelectAll(bool selected) => Select(selected ? Choices.Select(season => season.SeasonNumber) : []);
    public void Select(IEnumerable<int> seasons) { _changed = true; Selected = seasons.Where(value => Choices.Any(season => season.SeasonNumber == value)).Distinct().Order().ToList(); }
    private static bool Requestable(RequestMediaSeason season) => season.SeasonNumber >= 0 && season.Availability != "available" && !season.Requested;
    private bool Aired(RequestMediaSeason season) => RequestViewerPolicy.HasAired(season, _today);
    public static string? Status(RequestMediaSeason season, DateOnly today) => season.Availability == "available" ? "In library" : season.Requested ? "Requested" : season.Availability == "partial" ? "Partly in library" : RequestViewerPolicy.HasAired(season, today) ? null : !string.IsNullOrWhiteSpace(season.AirDate) && season.EpisodeCount > 0 ? "Not aired yet" : "Not announced";
    public static string Meta(RequestMediaSeason season) => string.Join(" · ", new[] { season.AirDate is { Length: >= 4 } date ? date[..4] : null, season.EpisodeCount > 0 ? $"{season.EpisodeCount} episode{(season.EpisodeCount == 1 ? "" : "s")}" : null }.Where(value => value != null)) is { Length: > 0 } text ? text : "Not announced";
    private static string FormatSeasons(List<int> seasons)
    {
        var runs = new List<string>();
        for (var start = 0; start < seasons.Count;)
        {
            var end = start; while (end + 1 < seasons.Count && seasons[end + 1] == seasons[end] + 1) end++;
            runs.Add(end == start ? seasons[start].ToString() : $"{seasons[start]}–{seasons[end]}"); start = end + 1;
        }
        return $"{(seasons.Count == 1 ? "Season" : "Seasons")} {string.Join(", ", runs)}";
    }
}
