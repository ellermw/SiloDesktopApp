using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// What the person panel shows for one account or one of its household profiles (official <c>PersonDetailSheet.tsx</c> and
/// <c>useInsightsPerson.ts</c>, c59798116). It reads Activity, Titles and Devices scoped to the person and shows only person-scoped figures:
/// the server-wide peak stays on the Playback tab.
/// </summary>
public static class AdminInsightsPersonPresentation
{
    /// <summary>The profile picker's value for the whole account.</summary>
    public const string AllProfiles = "";

    /// <summary>The panel's heading: the profile's name when scoped to one, else the account's, else "Insights" until a name is known.</summary>
    public static string Heading(string? name) => string.IsNullOrEmpty(name) ? AdminInsightsText.T("web.admin.insights.title") : name;

    /// <summary>
    /// "Dad · Oct 26, 2026 – Nov 24, 2026": the account's name when the heading is one of its profiles, then the window the numbers cover
    /// (known once the Activity read has answered).
    /// </summary>
    public static string Description(AdminInsightsPersonScope person, string? accountName, AdminInsightsActivity? activity)
    {
        var window = "";
        if (activity != null)
        {
            var (from, to) = AdminInsightsRules.FormatWindowDates(activity.From, activity.To, activity.Tz);
            window = AdminInsightsText.T("web.admin.insights.detail.window", ("from", from), ("to", to));
        }
        return string.Join(" · ", new[] { string.IsNullOrEmpty(person.ProfileId) ? null : accountName, window }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>
    /// The picker lists the account's profiles, plus the scoped one when the account's store no longer lists it, so "All profiles" stays
    /// reachable. It shows for more than one profile, or for a scoped profile that is not listed.
    /// </summary>
    public static bool ShowsProfilePicker(AdminInsightsPersonScope person, IReadOnlyList<AdminHistoryProfileOption>? profiles)
        => (profiles?.Count ?? 0) > 1 || (!string.IsNullOrEmpty(person.ProfileId) && !IsListed(person, profiles));

    public static bool IsListed(AdminInsightsPersonScope person, IReadOnlyList<AdminHistoryProfileOption>? profiles)
        => !string.IsNullOrEmpty(person.ProfileId) && profiles?.Any(profile => string.Equals(profile.Id, person.ProfileId, StringComparison.Ordinal)) == true;

    /// <summary>The picker's choices: All profiles, each profile of the account, and the scoped profile when it is not listed.</summary>
    public static IReadOnlyList<(string Value, string Label)> ProfileOptions(AdminInsightsPersonScope person, IReadOnlyList<AdminHistoryProfileOption>? profiles)
    {
        var options = new List<(string, string)> { (AllProfiles, AdminInsightsText.T("web.admin.insights.personDetail.allProfiles")) };
        options.AddRange((profiles ?? []).Select(profile => (profile.Id, profile.Name)));
        if (!string.IsNullOrEmpty(person.ProfileId) && !IsListed(person, profiles))
            options.Add((person.ProfileId, AdminInsightsPersonNames.ProfileLabel(person, profiles) ?? person.ProfileId));
        return options;
    }

    /// <summary>The panel's location after a choice in the picker: the account with that profile, or all of its profiles.</summary>
    public static AdminInsightsPersonScope WithProfile(AdminInsightsPersonScope person, string value)
        => new(person.UserId, string.IsNullOrEmpty(value) ? null : value);

    /// <summary>"View history": the person's own sessions in the panel's window (known once Activity has answered, so the link is never unbounded).</summary>
    public static AdminRouteRequest History(AdminInsightsPersonScope person, AdminInsightsFilterState filters, AdminInsightsActivity activity)
        => AdminInsightsRules.BuildHistoryDrilldown(new AdminInsightsHistoryDrilldown
        {
            UserId = person.UserId.ToString(CultureInfo.InvariantCulture),
            ProfileId = person.ProfileId,
            LibraryId = filters.LibraryId?.ToString(CultureInfo.InvariantCulture),
            StartedAfter = activity.From,
            StartedBefore = activity.To,
        });

    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsActivity data)
    {
        var summary = data.Summary;
        return
        [
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.plays"), AdminInsightsNumbers.Count(summary.Plays),
                AdminInsightsText.T("web.admin.insights.activity.kpi.playsBreakdown", ("movies", AdminInsightsNumbers.Count(summary.Movies)), ("movie_count", summary.Movies),
                    ("episodes", AdminInsightsNumbers.Count(summary.Episodes)), ("episode_count", summary.Episodes))),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.watchTime"), DashboardFormat.WatchTime(summary.WatchSeconds), AdminInsightsRules.SessionCount(summary.Sessions)),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.completionRate"), AdminInsightsNumbers.Percent(summary.CompletionRate),
                AdminInsightsText.T("web.admin.insights.activity.kpi.completionDetail", ("completed", AdminInsightsNumbers.Count(summary.CompletedPlays)),
                    ("total", AdminInsightsNumbers.Count(summary.Plays))), ActivityTone.Success),
            new(AdminInsightsText.T("web.admin.insights.devices.kpi.directPlayRate"),
                AdminInsightsNumbers.Percent(summary.Plays > 0 ? (double)summary.DirectPlays / summary.Plays : 0),
                AdminInsightsText.T("web.admin.insights.devices.kpi.directPlayDetail", ("direct", AdminInsightsNumbers.Count(summary.DirectPlays)),
                    ("remux", AdminInsightsNumbers.Count(summary.RemuxPlays)), ("transcode", AdminInsightsNumbers.Count(summary.TranscodePlays))), ActivityTone.Info),
        ];
    }

    /// <summary>Plays per bucket across the window with quiet buckets filled, or null when the person has no play in it.</summary>
    public static DashboardInsightsColumns? PlaysColumns(AdminInsightsActivity data)
    {
        if (!data.Buckets.Any(bucket => bucket.Plays > 0)) return null;
        var byDate = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var bucket in data.Buckets) byDate[bucket.LabelDate] = bucket.Plays;
        var dates = AdminInsightsRules.ChartDates(data.From, data.To, data.Tz, data.BucketUnit, data.Buckets.Select(bucket => bucket.LabelDate));
        var columns = dates.Select(date => (Time: DashboardInsightsPresentation.BucketTime(date), Date: date)).Where(item => item.Time != null)
            .Select(item => new DashboardChartColumn(item.Time!.Value, [byDate.GetValueOrDefault(item.Date)])).ToArray();
        return columns.Length == 0 ? null : new DashboardInsightsColumns(columns, [AdminInsightsText.T("web.admin.insights.activity.volume.metricPlays")]);
    }

    /// <summary>
    /// The eight titles the person played most: movies and shows together, by plays and then watch time. A row opens the title panel with the
    /// person kept, so the panel shows this person's plays and closing it returns here.
    /// </summary>
    public static IReadOnlyList<AdminInsightsBarRow> TopTitles(AdminInsightsTitles data)
        => data.Movies.Concat(data.Shows).OrderByDescending(row => row.Plays).ThenByDescending(row => row.WatchSeconds)
            .Take(AdminInsightsLimits.PersonTopTitles)
            .Select(row => new AdminInsightsBarRow(row.ContentId, AdminInsightsRules.FormatTitle(row.Title, row.Year), row.Plays,
                DashboardFormat.WatchTime(row.WatchSeconds), TitleId: row.ContentId)).ToArray();

    /// <summary>The six clients the person used most (the read has no sample floor), with each one's direct-play share.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> Clients(AdminInsightsDevices data)
        => data.Matrix.Take(AdminInsightsLimits.PersonTopClients)
            .Select(row => new AdminInsightsBarRow(row.ClientName + ":" + row.DevicePlatform,
                string.Join(" · ", new[] { AdminInsightsDevicesPresentation.ClientLabel(row.ClientName), row.DevicePlatform }.Where(part => !string.IsNullOrEmpty(part))),
                row.TotalSessions,
                AdminInsightsText.T("web.admin.insights.personDetail.clientDirectRate", ("percent", AdminInsightsRules.FormatRatePercentNumber(row.DirectRate))))).ToArray();
}
