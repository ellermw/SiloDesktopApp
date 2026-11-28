using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>What the title panel shows for one movie or series (official <c>TitleDetailSheet.tsx</c>, c59798116).</summary>
public static class AdminInsightsTitlePanelPresentation
{
    /// <summary>The panel's heading: the title with its year, or "Insights" before the read has an answer.</summary>
    public static string Heading(AdminInsightsTitle? data) => data == null
        ? AdminInsightsText.T("web.admin.insights.title")
        : AdminInsightsRules.FormatTitle(data.Title, data.Year);

    /// <summary>"Movie · Oct 26, 2026 – Nov 24, 2026": the kind and the window the numbers cover.</summary>
    public static string Description(AdminInsightsTitle? data)
    {
        if (data == null) return "";
        var (from, to) = AdminInsightsRules.FormatWindowDates(data.From, data.To, data.Tz);
        return string.Join(" · ",
            AdminInsightsText.T(data.Kind == "series" ? "web.admin.insights.titleDetail.kind.series" : "web.admin.insights.titleDetail.kind.movie"),
            AdminInsightsText.T("web.admin.insights.detail.window", ("from", from), ("to", to)));
    }

    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsTitle data)
    {
        var summary = data.Summary;
        return
        [
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.plays"), AdminInsightsNumbers.Count(summary.Plays),
                AdminInsightsText.T("web.admin.insights.devices.kpi.directPlayDetail", ("direct", AdminInsightsNumbers.Count(summary.DirectPlays)),
                    ("remux", AdminInsightsNumbers.Count(summary.RemuxPlays)), ("transcode", AdminInsightsNumbers.Count(summary.TranscodePlays)))),
            new(AdminInsightsText.T("web.admin.insights.detail.viewers"), AdminInsightsNumbers.Count(summary.Viewers)),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.watchTime"), DashboardFormat.WatchTime(summary.WatchSeconds),
                AdminInsightsRules.SessionCount(summary.Sessions)),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.completionRate"),
                AdminInsightsNumbers.Percent(summary.Plays > 0 ? (double)summary.CompletedPlays / summary.Plays : 0),
                AdminInsightsText.T("web.admin.insights.activity.kpi.completionDetail",
                    ("completed", AdminInsightsNumbers.Count(summary.CompletedPlays)), ("total", AdminInsightsNumbers.Count(summary.Plays))),
                ActivityTone.Success),
        ];
    }

    /// <summary>
    /// "View history": the title's own sessions in the panel's window. A series filters by its series id, a movie by its media item; the
    /// panel's library and account (or the person it is scoped to) carry over.
    /// </summary>
    public static AdminRouteRequest History(AdminInsightsTitle data, AdminInsightsFilterState filters, AdminInsightsPersonScope? person)
        => AdminInsightsRules.BuildHistoryDrilldown(new AdminInsightsHistoryDrilldown
        {
            SeriesId = data.Kind == "series" ? data.ContentId : null,
            MediaItemId = data.Kind == "series" ? null : data.ContentId,
            LibraryId = filters.LibraryId?.ToString(CultureInfo.InvariantCulture),
            UserId = (person?.UserId ?? filters.UserId)?.ToString(CultureInfo.InvariantCulture),
            ProfileId = person?.ProfileId,
            StartedAfter = data.From,
            StartedBefore = data.To,
        });

    /// <summary>"Plays by Kids": the banner of a panel scoped to a person; the name falls back to the account id until it is known.</summary>
    public static string ScopedBanner(string? name, AdminInsightsPersonScope person)
        => AdminInsightsText.T("web.admin.insights.titleDetail.scopedToPerson",
            ("name", string.IsNullOrEmpty(name) ? person.UserId.ToString(CultureInfo.InvariantCulture) : name));

    /// <summary>One profile that played the title.</summary>
    public sealed record ViewerRow(string Name, string Detail, string Plays, string Meta, AdminInsightsPersonScope? Person);

    /// <summary>
    /// "Last: S02E04 · Title" for a series' viewer, "Reached 1h 12m of 2h 4m" for a movie's (without the runtime when it is unknown),
    /// else nothing.
    /// </summary>
    public static string ViewerProgress(AdminInsightsTitleViewer viewer, AdminInsightsTitle title)
    {
        if (viewer.LastEpisode is { } episode)
        {
            var label = string.Join(" · ", new[] { EpisodeCode(episode.SeasonNumber, episode.EpisodeNumber), episode.Title }.Where(part => part.Length > 0));
            return AdminInsightsText.T("web.admin.insights.titleDetail.viewers.lastEpisode", ("episode", label));
        }
        if (viewer.FurthestPositionSeconds is > 0 and { } position)
        {
            var reached = AdminPlaybackHistoryPresentation.FormatDuration(position);
            return title.DurationSeconds is > 0 and { } duration
                ? AdminInsightsText.T("web.admin.insights.titleDetail.viewers.furthest", ("position", reached), ("duration", AdminPlaybackHistoryPresentation.FormatDuration(duration)))
                : AdminInsightsText.T("web.admin.insights.titleDetail.viewers.furthestNoDuration", ("position", reached));
        }
        return "";
    }

    public static string EpisodeCode(long season, long episode)
        => AdminPlaybackHistoryPresentation.EpisodeCode(ClampInt(season), ClampInt(episode));

    private static int ClampInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    /// <summary>The viewers, most recent first as the server sent them.</summary>
    public static IReadOnlyList<ViewerRow> Viewers(AdminInsightsTitle data)
        => data.Viewers.Select(viewer => new ViewerRow(
            viewer.ProfileName,
            string.Join(" · ", new[] { viewer.Username, ViewerProgress(viewer, data) }.Where(part => !string.IsNullOrEmpty(part))),
            AdminInsightsRules.PlayCount(viewer.Plays),
            string.Join(" · ", DashboardFormat.WatchTime(viewer.WatchSeconds), AdminInsightsRules.FormatShortCalendarDate(viewer.LastPlayedAt)),
            AdminInsightsFilters.ParsePositiveId(viewer.UserId) is { } user ? new AdminInsightsPersonScope(user, string.IsNullOrEmpty(viewer.ProfileId) ? null : viewer.ProfileId) : null)).ToArray();

    /// <summary>One episode that had a play: "S02E04", its title, "3 plays", "2 viewers".</summary>
    public sealed record EpisodeRow(string ContentId, string Code, string Title, string Plays, string Viewers);

    public static IReadOnlyList<EpisodeRow> Episodes(AdminInsightsTitle data)
        => data.Episodes.Select(episode => new EpisodeRow(episode.ContentId, EpisodeCode(episode.SeasonNumber, episode.EpisodeNumber), episode.Title,
            AdminInsightsRules.PlayCount(episode.Plays), AdminInsightsRules.ViewerCount(episode.Viewers))).ToArray();

    /// <summary>Plays per bucket across the panel's window with quiet buckets filled, or null when there is no play (official <c>InsightsPlaysChart</c>).</summary>
    public static DashboardInsightsColumns? PlaysColumns(AdminInsightsTitle data)
    {
        if (!data.Buckets.Any(bucket => bucket.Plays > 0)) return null;
        var byDate = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var bucket in data.Buckets) byDate[bucket.LabelDate] = bucket.Plays;
        var dates = AdminInsightsRules.ChartDates(data.From, data.To, data.Tz, data.BucketUnit, data.Buckets.Select(bucket => bucket.LabelDate));
        var columns = dates.Select(date => (Time: DashboardInsightsPresentation.BucketTime(date), Date: date)).Where(item => item.Time != null)
            .Select(item => new DashboardChartColumn(item.Time!.Value, [byDate.GetValueOrDefault(item.Date)])).ToArray();
        return columns.Length == 0 ? null
            : new DashboardInsightsColumns(columns, [AdminInsightsText.T("web.admin.insights.activity.volume.metricPlays")]);
    }
}

/// <summary>
/// The names of a person scope (official <c>useInsightsPerson</c>): the account's username, its household profiles, and the scoped
/// profile's name. A profile the account's store no longer lists keeps the People tab's "Profile …" label rather than reading as the account.
/// The title panel (Plays by …) and the person panel share it.
/// </summary>
public static class AdminInsightsPersonNames
{
    /// <summary>
    /// The profile's name when the scope is one profile (its listed name, else "Profile ab12cd34"), else the account's username; null
    /// when neither is known yet.
    /// </summary>
    public static string? NameOf(AdminInsightsPersonScope person, string? accountName, IReadOnlyList<AdminHistoryProfileOption>? profiles)
    {
        var profileLabel = ProfileLabel(person, profiles);
        return profileLabel ?? (string.IsNullOrEmpty(accountName) ? null : accountName);
    }

    /// <summary>The scoped profile's label, or null when the scope is the whole account.</summary>
    public static string? ProfileLabel(AdminInsightsPersonScope person, IReadOnlyList<AdminHistoryProfileOption>? profiles)
    {
        if (string.IsNullOrEmpty(person.ProfileId)) return null;
        var listed = profiles?.FirstOrDefault(profile => string.Equals(profile.Id, person.ProfileId, StringComparison.Ordinal));
        return !string.IsNullOrEmpty(listed?.Name) ? listed.Name : AdminInsightsRules.UnnamedProfileLabel(person.ProfileId);
    }
}
