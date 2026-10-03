using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

/// <summary>Viewer-facing rules mirrored from the official WebUI; server mutations remain authoritative.</summary>
public static class RequestViewerPolicy
{
    public sealed record TitlePresentation(string Badge, string Caption, int Rank, bool Requestable);

    public static TitlePresentation WatchlistPresentation(WatchlistTitle title, DateOnly? on = null)
    {
        if (title.Status == "needs_review") return new("Needs attention", "TMDB lists it twice", 4, false);
        if (title.Status == "removed") return new("Not on TMDB", "No longer listed on TMDB", 4, false);
        if (title.Request.Download is { } download) return new(DownloadLabel(download), DownloadLabel(download), 0, false);
        var today = on ?? DateOnly.FromDateTime(DateTime.Now);
        var release = DateOnly.TryParseExact(title.ReleaseDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) && date > today
            ? $"Out {date.ToString(date.Year == today.Year ? "MMM d" : "MMM d, yyyy")}" : null;
        var label = Label(title.Request.Status, null, title.Request.State);
        return label switch
        {
            "Pending" => new(release ?? "Awaiting approval", release == null ? "Awaiting approval" : $"{release} · awaiting approval", release == null ? 2 : 1, false),
            "Approved" => new(release ?? "Approved", release == null ? "Approved · waiting for a download" : $"{release} · approved", 1, false),
            "Processing" => new(label, label, 0, false),
            "Available" or "Partially available" => new(label, label, 1, false),
            _ => new("Not requested", title.Request.Requestable ? release is null ? "Not requested" : $"Not requested · {release.ToLowerInvariant()}" : $"Not requested · {Reason(title.Request.Reason).ToLowerInvariant()}", 3, title.Request.Requestable)
        };
    }

    public static string Reason(string? reason) => reason switch
    { "already_requested" => "Already requested", "already_available" => "Available", "requests_disabled" => "Requests disabled", "blocked" => "Blocked", "quota_exceeded" => "Request limit reached", _ => "Unavailable" };
    public static bool CanCancel(MediaRequest request) => request.Outcome == "active" &&
        (request.Status == "pending" || request.Status == "approved" && (request.Targets?.Count ?? 0) == 0);

    public static bool CanFollow(RequestFeatureStatus features, RequestState request) =>
        features.FollowSupported && request.Reason == "already_requested" && request.RequestedByViewer == false &&
        (!string.IsNullOrEmpty(request.Status) || !string.IsNullOrEmpty(request.State)) && Label(request.Status, null, request.State) != "Available";

    public static bool ShowAutoRequestControl(RequestFeatureStatus features, bool profileValue) =>
        features.RequestsEnabled && features.WatchlistTitlesSupported && features.Allowed == true &&
        (features.WatchlistRequests || !profileValue);

    public static bool SeasonRequestable(RequestMediaSeason season) =>
        season.SeasonNumber > 0 && season.Availability != "available" && !season.Requested;

    public static bool HasAired(RequestMediaSeason season, DateOnly today) =>
        season.EpisodeCount > 0 && DateOnly.TryParse(season.AirDate, out var date) && date <= today;

    public static List<int> DefaultSeasons(IEnumerable<RequestMediaSeason> seasons, DateOnly today) =>
        seasons.Where(s => SeasonRequestable(s) && HasAired(s, today)).Select(s => s.SeasonNumber).ToList();

    public static string Label(string? status, string? outcome, string? state, string? availability = null)
    {
        var value = !string.IsNullOrWhiteSpace(state) ? state : outcome is "declined" or "cancelled" or "failed" ? outcome : status switch
        { "queued" or "downloading" => "processing", "completed" => "available", _ => status };
        return value switch
        {
            "pending" => "Pending", "approved" => "Approved", "processing" => "Processing",
            "partially_available" => "Partially available", "available" => "Available", "declined" => "Declined",
            "cancelled" => "Cancelled", "failed" => "Failed",
            _ => availability == "available" ? "Available" : string.IsNullOrEmpty(value) ? "Unavailable" : "Requested"
        };
    }

    public static int? DownloadPercent(RequestDownload? download) => download?.Phase is "queued" or "downloading" or "paused" or "stalled" or "importing" or "import_blocked"
        ? download.Percent is int percent ? Math.Clamp(percent, 0, 100) : null : null;

    public static string DownloadLabel(RequestDownload? download, DateTimeOffset? at = null)
    {
        if (download == null) return "";
        var phase = download.Phase switch
        { "queued" => "Waiting to download", "paused" => "Download paused", "stalled" => "Download stalled", "importing" => "Importing", "import_blocked" => "Waiting for import", _ => "Downloading" };
        if (download.Phase != "downloading") return phase;
        var label = phase + (DownloadPercent(download) is int percent ? $" · {percent}%" : "");
        var now = at ?? DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(download.EstimatedCompletionAt, out var eta) && eta > now &&
            DateTimeOffset.TryParse(download.UpdatedAt, out var heard) && now - heard <= TimeSpan.FromMinutes(10))
            label += $" · about {Math.Max(1, (int)Math.Ceiling((eta - now).TotalMinutes))} min left";
        return label;
    }

    public static string SeasonProgressLabel(IEnumerable<RequestSeasonProgress>? progress)
    {
        var seasons = progress?.ToArray() ?? [];
        var aired = seasons.Sum(s => Math.Max(0, s.EpisodesAired));
        var available = seasons.Sum(s => Math.Clamp(s.EpisodesAvailable, 0, Math.Max(0, s.EpisodesAired)));
        return aired == 0 ? "" : $"{available} of {aired} episode{(aired == 1 ? "" : "s")} in the library";
    }
}
