using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminInsightsPersonPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// The person panel (official <c>PersonDetailSheet.tsx</c> and <c>useInsightsPerson.ts</c> at c59798116): its three scoped reads, heading and
/// description, the profile picker including an unlisted profile, KPI values, Top titles and Clients, and where each action goes.
/// Synthetic data only.
/// </summary>
public sealed class AdminInsightsPersonTests
{
    public AdminInsightsPersonTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    private static AdminInsightsActivity Activity() => JsonSerializer.Deserialize<AdminInsightsActivity>(AdminInsightsWire.Activity)!;

    private static AdminInsightsFilterState Filters(params (string, string)[] query)
        => AdminInsightsFilterState.Parse(query.ToDictionary(pair => pair.Item1, pair => pair.Item2), "UTC", new DateOnly(2026, 11, 26));

    private static readonly AdminHistoryProfileOption[] Profiles = [new("p1", "Kids"), new("p2", "Movie Night")];

    // ── The scoped reads ──────────────────────────────────────

    [Fact]
    public void ThePanelReadsActivityTitlesAndDevicesForThePersonAndTheirProfile()
    {
        var filters = Filters(("range", "90d"), ("tz", "UTC"), ("library_id", "3"), ("user_id", "99"));
        var window = filters.ToWindow(person: new AdminInsightsPersonScope(7, "p1"));
        Assert.Equal("/api/v2/admin/insights/activity?range=90d&tz=UTC&library_id=3&user_id=7&profile_id=p1", AdminInsightsApi.ActivityPath(window));
        Assert.Equal("/api/v2/admin/insights/titles?range=90d&tz=UTC&library_id=3&user_id=7&profile_id=p1&sort=plays&limit=8",
            AdminInsightsApi.TitlesPath(window, AdminInsightsTitleSort.Plays, AdminInsightsLimits.PersonTopTitles));
        // Every client counts for a person, not only those with five sessions.
        Assert.Equal("/api/v2/admin/insights/devices?range=90d&tz=UTC&library_id=3&user_id=7&profile_id=p1&min_sessions=1",
            AdminInsightsApi.DevicesPath(window, AdminInsightsLimits.PersonDevicesMinSessions));
        // The whole account when no profile is chosen.
        var account = filters.ToWindow(person: new AdminInsightsPersonScope(7));
        Assert.Equal("/api/v2/admin/insights/activity?range=90d&tz=UTC&library_id=3&user_id=7", AdminInsightsApi.ActivityPath(account));
    }

    [Fact]
    public void ThePanelHonoursOnlyTheFiltersThePageShowedOnItsTab()
    {
        // From People (no account filter) an account in the URL is hidden, and from Bandwidth (no library) so is the library.
        var people = Filters(("tab", "people"), ("user_id", "99"), ("library_id", "3")).ForDetail();
        Assert.Null(people.UserId);
        Assert.Equal(3, people.LibraryId);
        var bandwidth = Filters(("tab", "bandwidth"), ("library_id", "3")).ForDetail();
        Assert.Null(bandwidth.LibraryId);
    }

    // ── Heading, description and the picker ───────────────────

    [Fact]
    public void TheHeadingIsTheProfileWhenOneIsChosenElseTheAccountElseInsights()
    {
        Assert.Equal("Kids", P.Heading(AdminInsightsPersonNames.NameOf(new AdminInsightsPersonScope(7, "p1"), "dad", Profiles)));
        Assert.Equal("dad", P.Heading(AdminInsightsPersonNames.NameOf(new AdminInsightsPersonScope(7), "dad", Profiles)));
        Assert.Equal("Insights", P.Heading(AdminInsightsPersonNames.NameOf(new AdminInsightsPersonScope(7), null, null)));
        // A profile the account no longer lists keeps the People tab's label rather than reading as the account.
        Assert.Equal("Profile gone1234", P.Heading(AdminInsightsPersonNames.NameOf(new AdminInsightsPersonScope(7, "gone1234-zzz"), "dad", Profiles)));
    }

    [Fact]
    public void TheDescriptionNamesTheAccountOfAProfileAndTheWindowOnceKnown()
    {
        var activity = Activity();
        activity.From = "2026-10-27T00:00:00Z";
        activity.To = "2026-11-26T00:00:00Z";
        activity.Tz = "UTC";
        Assert.Equal("dad · Oct 27, 2026 – Nov 25, 2026", P.Description(new AdminInsightsPersonScope(7, "p1"), "dad", activity));
        Assert.Equal("Oct 27, 2026 – Nov 25, 2026", P.Description(new AdminInsightsPersonScope(7), "dad", activity));
        Assert.Equal("dad", P.Description(new AdminInsightsPersonScope(7, "p1"), "dad", null));
        Assert.Equal("", P.Description(new AdminInsightsPersonScope(7), "dad", null));
    }

    [Fact]
    public void TheProfilePickerShowsForSeveralProfilesOrAnUnlistedOne()
    {
        var account = new AdminInsightsPersonScope(7);
        Assert.True(P.ShowsProfilePicker(account, Profiles));
        Assert.False(P.ShowsProfilePicker(account, [new("p1", "Only")]));
        Assert.False(P.ShowsProfilePicker(account, null));
        // One profile, but the scoped one is not it: the picker keeps "All profiles" reachable.
        Assert.True(P.ShowsProfilePicker(new AdminInsightsPersonScope(7, "ghost"), [new("p1", "Only")]));
        Assert.False(P.ShowsProfilePicker(new AdminInsightsPersonScope(7, "p1"), [new("p1", "Only")]));
    }

    [Fact]
    public void ThePickerListsAllProfilesEachProfileAndTheUnlistedOneOfTheScope()
    {
        Assert.Equal([("", "All profiles"), ("p1", "Kids"), ("p2", "Movie Night")], P.ProfileOptions(new AdminInsightsPersonScope(7, "p2"), Profiles));
        var unlisted = P.ProfileOptions(new AdminInsightsPersonScope(7, "ghost-id-12345"), Profiles);
        Assert.Equal(("ghost-id-12345", "Profile ghost-id"), unlisted[^1]);
        Assert.Equal(4, unlisted.Count);
    }

    [Fact]
    public void ChoosingAProfileKeepsTheAccountAndAllProfilesDropsTheProfile()
    {
        var person = new AdminInsightsPersonScope(7, "p1");
        Assert.Equal(new AdminInsightsPersonScope(7, "p2"), P.WithProfile(person, "p2"));
        Assert.Equal(new AdminInsightsPersonScope(7), P.WithProfile(person, P.AllProfiles));
        var request = new AdminRouteRequest(AdminRoute.Insights, new Dictionary<string, string> { ["tab"] = "people", ["person"] = "7", ["profile"] = "p1" });
        Assert.Equal("/admin/insights?person=7&profile=p2&tab=people", AdminInsightsFilters.WithPerson(request, P.WithProfile(person, "p2")).Href);
        Assert.Equal("/admin/insights?person=7&tab=people", AdminInsightsFilters.WithPerson(request, P.WithProfile(person, P.AllProfiles)).Href);
    }

    // ── Figures ───────────────────────────────────────────────

    [Fact]
    public void FourKpiCardsCarryTheOfficialValuesAndSubtexts()
    {
        var kpis = P.Kpis(Activity());
        Assert.Equal(["Plays", "Watch time", "Completion rate", "Direct play rate"], kpis.Select(kpi => kpi.Label));
        Assert.Equal("12", kpis[0].Value);
        Assert.Equal("12 movies · 12 episodes", kpis[0].Subtext);
        Assert.Equal(DashboardFormat.WatchTime(3600), kpis[1].Value);
        Assert.Equal("12 sessions", kpis[1].Subtext);
        Assert.Equal("75%", kpis[2].Value);
        Assert.Equal("12 of 12 completed", kpis[2].Subtext);
        Assert.Equal(ActivityTone.Success, kpis[2].Accent);
        // Direct plays over plays, not the server-wide direct rate.
        Assert.Equal("100%", kpis[3].Value);
        Assert.Equal("12 direct · 12 direct stream · 12 transcode", kpis[3].Subtext);
        Assert.Equal(ActivityTone.Info, kpis[3].Accent);
    }

    [Fact]
    public void ADirectPlayRateWithNoPlaysIsZeroNotADivisionByZero()
    {
        var data = Activity();
        data.Summary.Plays = 0;
        data.Summary.DirectPlays = 0;
        Assert.Equal("0%", P.Kpis(data)[3].Value);
    }

    [Fact]
    public void PlaysOverTimeFillsQuietBucketsAndIsEmptyWithoutAPlay()
    {
        var data = Activity();
        data.BucketUnit = "day";
        data.From = "2026-11-20T00:00:00Z";
        data.To = "2026-11-24T00:00:00Z";
        data.Tz = "UTC";
        data.Buckets = [new() { LabelDate = "2026-11-21", Plays = 4 }, new() { LabelDate = "2026-11-23", Plays = 2 }];
        var shape = P.PlaysColumns(data)!;
        Assert.Equal(["Plays"], shape.SeriesLabels);
        Assert.Equal([0d, 4d, 0d, 2d], shape.Columns.Select(column => column.Segments[0]));
        data.Buckets = [new() { LabelDate = "2026-11-21", Plays = 0 }];
        Assert.Null(P.PlaysColumns(data));
    }

    [Fact]
    public void TopTitlesMergeMoviesAndShowsByPlaysThenWatchTimeAndStopAtEight()
    {
        static AdminInsightsTitleRow Row(string id, string title, long plays, long watch, long? year = 2000) => new() { ContentId = id, Title = title, Plays = plays, WatchSeconds = watch, Year = year };
        var data = new AdminInsightsTitles
        {
            Movies = [Row("m1", "Movie", 5, 100), Row("m2", "", 9, 100, null), Row("m3", "Tie", 5, 200)],
            Shows = [Row("s1", "Show", 7, 50), Row("s2", "A", 1, 1), Row("s3", "B", 1, 1), Row("s4", "C", 1, 1), Row("s5", "D", 1, 1), Row("s6", "E", 1, 1), Row("s7", "F", 1, 1)],
        };
        var rows = P.TopTitles(data);
        Assert.Equal(8, rows.Count);
        Assert.Equal(["m2", "s1", "m3", "m1"], rows.Take(4).Select(row => row.Id));
        Assert.Equal(["Removed title", "Show (2000)", "Tie (2000)", "Movie (2000)"], rows.Take(4).Select(row => row.Label));
        Assert.Equal([9d, 7d, 5d, 5d], rows.Take(4).Select(row => row.Value));
        Assert.Equal(DashboardFormat.WatchTime(50), rows[1].Secondary);
        // A row opens the title panel and the person stays in the location, so closing the title returns to the person.
        Assert.All(rows, row => Assert.Equal(row.Id, row.TitleId));
    }

    [Fact]
    public void ClientsAreTheFirstSixOfTheMatrixWithTheirDirectPlayShare()
    {
        static AdminInsightsClientMatrixRow Row(string client, string platform, long sessions, double rate) => new() { ClientName = client, DevicePlatform = platform, TotalSessions = sessions, DirectRate = rate };
        var data = new AdminInsightsDevices
        {
            Matrix = [Row("Silo Desktop", "Windows", 30, 0.9), Row("", "Roku", 12, 0.04), Row("Web", "", 9, 1), Row("A", "x", 8, 0), Row("B", "x", 7, 0), Row("C", "x", 6, 0), Row("D", "x", 5, 0)],
        };
        var rows = P.Clients(data);
        Assert.Equal(6, rows.Count);
        Assert.Equal(["Silo Desktop · Windows", "Unknown client · Roku", "Web", "A · x", "B · x", "C · x"], rows.Select(row => row.Label));
        Assert.Equal([30d, 12d, 9d, 8d, 7d, 6d], rows.Select(row => row.Value));
        Assert.Equal(["90% direct play", "4.0% direct play", "100% direct play"], rows.Take(3).Select(row => row.Secondary));
    }

    // ── Actions ───────────────────────────────────────────────

    [Fact]
    public void ViewHistoryOpensThePersonsSessionsInTheWindowTheActivityReadResolved()
    {
        var activity = Activity();
        activity.From = "2026-10-27T00:00:00Z";
        activity.To = "2026-11-26T00:00:00Z";
        var history = P.History(new AdminInsightsPersonScope(7, "p1"), Filters(("library_id", "3")), activity);
        Assert.Equal("/admin/history?library_id=3&profile_id=p1&started_after=2026-10-27T00%3A00%3A00Z&started_before=2026-11-26T00%3A00%3A00Z&user_id=7", history.Href);
        Assert.DoesNotContain("profile_id", P.History(new AdminInsightsPersonScope(7), Filters(), activity).Href);
    }

    [Fact]
    public void AViewerOfATitleOpensThePersonAndLeavesTheTitle()
    {
        // The title panel's viewer link (official insightsHrefWith({ title: null, person, profile })).
        var title = new AdminRouteRequest(AdminRoute.Insights, new Dictionary<string, string> { ["tab"] = "titles", ["title"] = "m1", ["library_id"] = "2" });
        var person = AdminInsightsFilters.WithPerson(AdminInsightsFilters.CloseTitle(title), new AdminInsightsPersonScope(7, "p1"));
        var detail = AdminInsightsFilters.ParseDetail(person);
        Assert.Null(detail.TitleId);
        Assert.Equal(new AdminInsightsPersonScope(7, "p1"), detail.Person);
        Assert.Equal("2", person.Query!["library_id"]);
        // Closing the person closes everything; the page keeps its tab and filters.
        Assert.False(AdminInsightsFilters.ParseDetail(AdminInsightsFilters.CloseForDetail(person)).IsOpen);
    }
}
