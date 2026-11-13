using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

public static partial class MediaItemCardActions
{
    internal static string RequestStatus(string? status, string? outcome = null, string? state = null, string? availability = null)
    {
        var value = !string.IsNullOrWhiteSpace(state) ? state : outcome is "declined" or "cancelled" or "failed" ? outcome : status switch
        { "queued" or "downloading" => "processing", "completed" => "available", _ => status };
        var key = value switch
        {
            "pending" => "web.requests.status.pending", "approved" => "web.requests.status.approved",
            "processing" => "web.requests.status.processing", "partially_available" => "web.requests.status.partiallyAvailable",
            "available" => "web.requests.status.available", "declined" => "web.requests.status.declined",
            "cancelled" => "web.requests.status.cancelled", "failed" => "web.requests.status.failed",
            _ => availability == "available" ? "web.requests.status.available" : string.IsNullOrEmpty(value)
                ? "web.requests.status.reason.unavailable" : "web.requests.status.requested"
        };
        return AppStrings.Get(key, fallback: RequestViewerPolicy.Label(status, outcome, state, availability));
    }

    internal static string RequestReason(string? reason) => AppStrings.Get(reason switch
    {
        "already_requested" => "web.requests.status.reason.alreadyRequested",
        "already_available" => "web.requests.status.reason.alreadyAvailable",
        "requests_disabled" => "web.requests.status.reason.requestsDisabled",
        "blocked" => "web.requests.status.reason.blocked", "quota_exceeded" => "web.requests.status.reason.quotaExceeded",
        _ => "web.requests.status.reason.unavailable"
    }, fallback: RequestViewerPolicy.Reason(reason));

    internal static string SeasonProgress(IEnumerable<RequestSeasonProgress>? progress)
    {
        var seasons = progress?.ToArray() ?? [];
        var aired = seasons.Sum(season => Math.Max(0, season.EpisodesAired));
        var have = seasons.Sum(season => Math.Clamp(season.EpisodesAvailable, 0, Math.Max(0, season.EpisodesAired)));
        return aired == 0 ? "" : AppStrings.Get("web.requests.status.seasonProgress", new Dictionary<string, object?>
        { ["aired"] = aired, ["have"] = have.ToString("N0", AppStrings.FormatCulture), ["aired_text"] = aired.ToString("N0", AppStrings.FormatCulture) },
            fallback: RequestViewerPolicy.SeasonProgressLabel(seasons));
    }

    internal static string DownloadLabel(RequestDownload? download)
    {
        if (download == null) return "";
        var key = download.Phase switch
        {
            "queued" => "web.requests.download.waiting", "paused" => "web.requests.download.paused",
            "stalled" => "web.requests.download.stalled", "importing" => "web.requests.download.importing",
            "import_blocked" => "web.requests.download.waitingForImport", _ => "web.requests.download.downloading"
        };
        var label = AppStrings.Get(key, fallback: RequestViewerPolicy.DownloadLabel(download));
        if (download.Phase != "downloading") return label;
        if (RequestViewerPolicy.DownloadPercent(download) is int percent) label += " · " + (percent / 100d).ToString("P0", AppStrings.FormatCulture);
        var now = DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(download.EstimatedCompletionAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var eta) && eta > now
            && DateTimeOffset.TryParse(download.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var heard) && now - heard <= TimeSpan.FromMinutes(10))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((eta - now).TotalMinutes));
            var time = AppStrings.Get("web.common.format.runtime.minutes", new Dictionary<string, object?> { ["count_text"] = (minutes).ToString("N0", SiloPlayer.Core.Localization.AppStrings.FormatCulture),  ["count"] = minutes }, fallback: $"{minutes} min");
            label += " · " + AppStrings.Get("web.requests.download.timeLeft", new Dictionary<string, object?> { ["time"] = time }, fallback: $"about {time} left");
        }
        return label;
    }

    internal static (string Badge, string Caption) WatchlistText(WatchlistTitle title)
    {
        if (title.Status == "needs_review") return (AppStrings.Get("web.library.watchlist.statusNeedsAttention"), AppStrings.Get("web.library.watchlist.statusListedTwice"));
        if (title.Status == "removed") return (AppStrings.Get("web.library.watchlist.statusNotOnTmdb"), AppStrings.Get("web.library.watchlist.statusNoLongerListed"));
        if (title.Request.Download is { } download) return (DownloadLabel(download), DownloadLabel(download));
        var today = DateOnly.FromDateTime(DateTime.Now);
        var upcoming = DateOnly.TryParseExact(title.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date > today;
        var dateText = upcoming ? date.ToString(date.Year == today.Year ? "m" : "d", AppStrings.FormatCulture) : "";
        var args = new Dictionary<string, object?> { ["date"] = dateText };
        var release = AppStrings.Get("web.library.watchlist.statusOut", args);
        var value = !string.IsNullOrEmpty(title.Request.State) ? title.Request.State : title.Request.Status;
        if (value == "pending") return (upcoming ? release : AppStrings.Get("web.library.watchlist.statusAwaitingApproval"),
            upcoming ? AppStrings.Get("web.library.watchlist.statusOutAwaitingApproval", args) : AppStrings.Get("web.library.watchlist.statusAwaitingApproval"));
        if (value == "approved") return (upcoming ? release : AppStrings.Get("web.requests.status.approved"),
            upcoming ? AppStrings.Get("web.library.watchlist.statusOutApproved", args) : AppStrings.Get("web.library.watchlist.statusApprovedWaiting"));
        if (value is "processing" or "queued" or "downloading" or "available" or "completed" or "partially_available")
        { var label = RequestStatus(title.Request.Status, state: title.Request.State); return (label, label); }
        return (AppStrings.Get("web.library.watchlist.statusNotRequested"), !title.Request.Requestable
            ? AppStrings.Get("web.library.watchlist.statusNotRequestedReason", new Dictionary<string, object?> { ["reason"] = RequestReason(title.Request.Reason) })
            : upcoming ? AppStrings.Get("web.library.watchlist.statusNotRequestedOut", args) : AppStrings.Get("web.library.watchlist.statusNotRequested"));
    }
    internal static string SeasonName(RequestMediaSeason season) => AppStrings.Get("web.requests.seasons.season", new Dictionary<string, object?> { ["number"] = season.SeasonNumber });
    internal static string? SeasonStatus(RequestMediaSeason season, DateOnly today)
    {
        var key = season.Availability == "available" ? "web.requests.seasons.inLibrary" : season.Requested ? "web.requests.seasons.requested"
            : season.Availability == "partial" ? "web.requests.seasons.partlyInLibrary" : RequestViewerPolicy.HasAired(season, today) ? null
            : !string.IsNullOrWhiteSpace(season.AirDate) && season.EpisodeCount > 0 ? "web.requests.seasons.notAired" : "web.requests.seasons.notAnnounced";
        return key == null ? null : AppStrings.Get(key);
    }
    internal static string SeasonMeta(RequestMediaSeason season)
    {
        var values = new List<string>();
        if (season.AirDate is { Length: >= 4 } date) values.Add(date[..4]);
        if (season.EpisodeCount > 0) values.Add(AppStrings.Get("web.requests.status.seasonEpisodes", new Dictionary<string, object?> { ["count_text"] = (season.EpisodeCount).ToString("N0", SiloPlayer.Core.Localization.AppStrings.FormatCulture),  ["count"] = season.EpisodeCount }));
        return values.Count == 0 ? AppStrings.Get("web.requests.seasons.notAnnounced") : string.Join(" · ", values);
    }
    internal static string RequestSeasons(IReadOnlyList<int> seasons)
    {
        var sorted = seasons.Distinct().Order().ToArray();
        var runs = new List<string>();
        for (var index = 0; index < sorted.Length; index++)
        {
            var first = sorted[index];
            while (index + 1 < sorted.Length && sorted[index + 1] == sorted[index] + 1) index++;
            var start = first.ToString(AppStrings.FormatCulture);
            runs.Add(first == sorted[index] ? start : start + "–" + sorted[index].ToString(AppStrings.FormatCulture));
        }
        return AppStrings.Get("web.requests.status.seasonList", new Dictionary<string, object?> { ["count"] = sorted.Length, ["list"] = string.Join(", ", runs) });
    }
    internal static string SeasonSubmitLabel(RequestSeasonPickerState state) => state.Selected.Count > 0
        ? AppStrings.Get("web.requests.seasons.requestSeasons", new Dictionary<string, object?> { ["seasons"] = RequestSeasons(state.Selected) })
        : AppStrings.Get(state.ServerChooses ? "web.requests.seasons.requestSeries" : "web.requests.request");

}
