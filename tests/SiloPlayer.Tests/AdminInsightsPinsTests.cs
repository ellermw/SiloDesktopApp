using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// "Add to dashboard" on the Insights cards and its one-writer rule (official <c>InsightsDashboardPins</c> and
/// <c>useDashboardLayoutWrites</c>, c59798116): hidden until the layout loaded and saving works, an "On dashboard" link once placed, the card's
/// window opened on the widget, and the Dashboard's first read waiting for a pending add. Synthetic layouts only.
/// </summary>
public sealed class AdminInsightsPinsTests : IDisposable
{
    private const string Origin = "https://silo.test";
    private const string UsersOnly = """{"version":1,"entries":[{"id":"users","span":5,"rows":4}]}""";
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly DashboardManualTime _time = new();
    private readonly DashboardFakeLayoutServer _server = new();
    private readonly DashboardMemoryLayoutCache _cache = new();

    public AdminInsightsPinsTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
        // A server that keeps what it was sent, as the real one does.
        string? stored = UsersOnly;
        var etag = 0;
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout($"\"R{etag}\"", stored));
        _server.OnSave = count =>
        {
            stored = _server.Saves[count - 1].Document;
            return Task.FromResult($"\"R{++etag}\"");
        };
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private (AdminInsightsDashboardPins Pins, DashboardLayoutSync Sync) Open(DashboardFakeLayoutServer? server = null)
    {
        // The layout owner marshals through the creating context; tests drive it context-free.
        SynchronizationContext.SetSynchronizationContext(null);
        var sync = new DashboardLayoutSync(_authority.Capture(), server ?? _server, _cache, Origin, _time);
        return (new AdminInsightsDashboardPins(sync), sync);
    }

    private void Debounce() => _time.Advance(DashboardLayoutSync.SaveDebounce + TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task NothingIsOfferedUntilTheLayoutHasLoaded()
    {
        var read = new TaskCompletionSource<DashboardLayoutRead>();
        _server.OnRead = _ => read.Task;
        var (pins, sync) = Open();
        var load = sync.LoadAsync();
        Assert.False(pins.CanAdd);
        Assert.Equal(AdminInsightsPinState.Hidden, pins.StateOf(DashboardRegistry.InsightsPlays));
        Assert.False(pins.Add(DashboardRegistry.InsightsPlays, AdminInsightsRange.Days30));
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", UsersOnly));
        await load;
        Assert.True(pins.CanAdd);
        Assert.Equal(AdminInsightsPinState.Add, pins.StateOf(DashboardRegistry.InsightsPlays));
        Assert.Empty(_server.Saves);
    }

    [Fact]
    public async Task AFailedFirstReadKeepsEveryButtonHidden()
    {
        _server.OnRead = _ => Task.FromException<DashboardLayoutRead>(new HttpRequestException("offline"));
        var (pins, sync) = Open();
        await sync.LoadAsync();
        Assert.False(pins.CanAdd);
        // Before the layout loads the cached or default arrangement stands, so a default widget is already a link; nothing can be added.
        Assert.Equal(AdminInsightsPinState.OnDashboard, pins.StateOf(DashboardRegistry.TopTitles));
        Assert.Equal(AdminInsightsPinState.Hidden, pins.StateOf(DashboardRegistry.InsightsPlays));
    }

    [Fact]
    public async Task AnAddPlacesTheWidgetOnTheCardsWindowAndBecomesALinkToTheDashboard()
    {
        var (pins, sync) = Open();
        await sync.LoadAsync();
        var changes = 0;
        pins.Changed += () => changes++;
        Assert.Equal(AdminInsightsPinState.Add, pins.StateOf(DashboardRegistry.InsightsPlays));
        Assert.True(pins.Add(DashboardRegistry.InsightsPlays, AdminInsightsRange.Days90));
        Assert.True(changes > 0);
        Assert.Equal(AdminInsightsPinState.OnDashboard, pins.StateOf(DashboardRegistry.InsightsPlays));
        Assert.Equal(DashboardWidgetRange.Quarter, sync.Entries.Single(entry => entry.Id == DashboardRegistry.InsightsPlays).Range);
        // Once placed there is nothing to add again.
        Assert.False(pins.Add(DashboardRegistry.InsightsPlays, AdminInsightsRange.Days7));
        Assert.Equal(DashboardWidgetRange.Quarter, sync.Entries.Single(entry => entry.Id == DashboardRegistry.InsightsPlays).Range);
        // One debounced write carries both the add and its window.
        Debounce();
        await sync.PendingWrites;
        var saved = Assert.Single(_server.Saves);
        Assert.Contains("insights-plays", saved.Document);
        Assert.Contains("\"range\":\"quarter\"", saved.Document);
        Assert.Equal("Added Playback over time to your dashboard", AdminInsightsDashboardPins.AddedMessage("Playback over time"));
    }

    [Theory]
    [InlineData(AdminInsightsRange.Days7, "insights-weekday", DashboardWidgetRange.Week)]
    [InlineData(AdminInsightsRange.Days30, "insights-hour-of-day", DashboardWidgetRange.Month)]
    [InlineData(AdminInsightsRange.Year1, "insights-library-coverage", DashboardWidgetRange.Year)]
    [InlineData(AdminInsightsRange.All, "insights-active-people", DashboardWidgetRange.All)]
    [InlineData(AdminInsightsRange.Year1, "top-titles", DashboardWidgetRange.Year)]
    public void TheCardsWindowBecomesTheWidgetsRangeWhenItOffersIt(AdminInsightsRange range, string widget, DashboardWidgetRange expected)
        => Assert.Equal(expected, AdminInsightsDashboardPins.WidgetRange(widget, range));

    [Fact]
    public void ACustomWindowOrAWidgetWithoutRangesLeavesTheDefault()
    {
        Assert.Null(AdminInsightsDashboardPins.WidgetRange(DashboardRegistry.InsightsPlays, AdminInsightsRange.Custom));
        // A sampled widget never offers a quarter, a year or all of history.
        Assert.Null(AdminInsightsDashboardPins.WidgetRange(DashboardRegistry.Playback24h, AdminInsightsRange.Year1));
        Assert.Null(AdminInsightsDashboardPins.WidgetRange(DashboardRegistry.Users, AdminInsightsRange.Days7));
        Assert.Null(AdminInsightsDashboardPins.WidgetRange("not-a-widget", AdminInsightsRange.Days7));
    }

    [Fact]
    public async Task ANonRangedOrUnknownWidgetIsNeverOffered()
    {
        var (pins, sync) = Open();
        await sync.LoadAsync();
        Assert.Equal(AdminInsightsPinState.Hidden, pins.StateOf("not-a-widget"));
        // A widget already on the dashboard is a link even before anything can be added.
        Assert.Equal(AdminInsightsPinState.OnDashboard, pins.StateOf(DashboardRegistry.Users));
    }

    [Fact]
    public async Task ABlockedSaveHidesTheAddButtonsButNotTheLinksToWhatIsPlaced()
    {
        _server.OnSave = _ => Task.FromException<string>(new HttpRequestException("412"));
        var (pins, sync) = Open();
        var unconfirmed = 0;
        pins.SaveUnconfirmed += () => unconfirmed++;
        await sync.LoadAsync();
        Assert.True(pins.Add(DashboardRegistry.InsightsPlays, AdminInsightsRange.Days30));
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(1, unconfirmed);
        Assert.False(pins.CanAdd);
        // The widget that was added locally is still a link; no other card offers an add that could not reach the server.
        Assert.Equal(AdminInsightsPinState.OnDashboard, pins.StateOf(DashboardRegistry.InsightsPlays));
        Assert.Equal(AdminInsightsPinState.Hidden, pins.StateOf(DashboardRegistry.InsightsWeekday));
        Assert.False(pins.Add(DashboardRegistry.InsightsWeekday, AdminInsightsRange.Days30));
    }

    [Fact]
    public async Task ASaveBlockedBeforeTheFirstAddAlsoHidesTheButtons()
    {
        // Another device holds a newer layout: the pre-read edit this page left behind cannot be saved.
        _cache.Write(DashboardLayoutSync.Scope(Origin, "1"), DashboardLayoutDocument.SerializeCached(
            DashboardRegistry.DefaultLayout, new DashboardLayoutPendingDraft(DashboardRegistry.DefaultLayout, "another-profile")));
        var (pins, sync) = Open();
        await sync.LoadAsync();
        Assert.True(sync.ServerSaveBlocked);
        Assert.False(pins.CanAdd);
        Assert.Equal(AdminInsightsPinState.Hidden, pins.StateOf(DashboardRegistry.InsightsPlays));
    }

    [Fact]
    public async Task OpeningTheDashboardAfterAnAddWaitsForItsSaveAndThenReadsTheNewLayout()
    {
        var writes = new DashboardLayoutWrites();
        var release = new TaskCompletionSource<string>();
        var originalSave = _server.OnSave;
        _server.OnSave = count => count == 1 ? release.Task.ContinueWith(_ => originalSave(count).Result) : originalSave(count);
        var (pins, sync) = Open();
        await sync.LoadAsync();
        pins.Add(DashboardRegistry.InsightsPlays, AdminInsightsRange.Days30);
        Assert.Empty(_server.Saves);

        // The Insights page is left with the add still queued: its departure is registered, as the page does.
        pins.Dispose();
        var departure = writes.Track(sync.DepartAsync());
        Assert.True(writes.IsRunning);
        var readsBeforeDashboard = _server.Reads;

        // The dashboard's first read waits.
        var dashboardLease = _authority.Capture();
        var dashboard = new DashboardLayoutSync(dashboardLease, _server, _cache, Origin, _time);
        var dashboardLoad = Task.Run(async () =>
        {
            await writes.WaitForIdleAsync();
            await dashboard.LoadAsync();
        });
        await Task.Delay(100);
        Assert.False(dashboardLoad.IsCompleted);
        Assert.Equal(readsBeforeDashboard, _server.Reads);
        // It already paints from the cache, which the add updated at once.
        Assert.Contains(dashboard.Entries, entry => entry.Id == DashboardRegistry.InsightsPlays);

        release.SetResult("\"R1\"");
        await departure;
        await dashboardLoad.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(writes.IsRunning);
        Assert.Equal(readsBeforeDashboard + 1, _server.Reads);
        var saved = Assert.Single(_server.Saves);
        Assert.Contains("insights-plays", saved.Document);
        Assert.Contains(dashboard.Entries, entry => entry.Id == DashboardRegistry.InsightsPlays);
        // The dashboard read the validator of the add's save, so its own edits are accepted rather than refused.
        Assert.False(dashboard.ServerSaveBlocked);
        dashboard.ResizeWidget(DashboardRegistry.InsightsPlays, 8, null);
        Debounce();
        await dashboard.PendingWrites;
        Assert.Equal(2, _server.Saves.Count);
        Assert.Equal("\"R1\"", _server.Saves[1].ETag);
        dashboard.Dispose();
    }

    [Fact]
    public async Task TheWriteGateIgnoresAFailedDepartureAndIdlesWhenNothingIsRunning()
    {
        var writes = new DashboardLayoutWrites();
        await writes.WaitForIdleAsync();
        var failing = writes.Track(Task.FromException(new InvalidOperationException("write failed")));
        await writes.WaitForIdleAsync();
        Assert.False(writes.IsRunning);
        Assert.True(failing.IsFaulted);

        var gate = new TaskCompletionSource();
        writes.Track(gate.Task);
        var second = writes.Track(Task.Delay(10));
        var waiting = writes.WaitForIdleAsync();
        await second;
        Assert.False(waiting.IsCompleted);
        gate.SetResult();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(writes.IsRunning);
    }

    [Fact]
    public async Task AWaitingReaderStopsWaitingWhenItsLeaseIsCancelled()
    {
        var writes = new DashboardLayoutWrites();
        writes.Track(new TaskCompletionSource().Task);
        using var cts = new CancellationTokenSource();
        var waiting = writes.WaitForIdleAsync(cts.Token);
        cts.Cancel();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(waiting.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task APinsOwnerThatWasDisposedOffersNothingAndRaisesNothing()
    {
        var (pins, sync) = Open();
        await sync.LoadAsync();
        var changes = 0;
        pins.Changed += () => changes++;
        pins.Dispose();
        sync.AddWidget(DashboardRegistry.InsightsPlays);
        Assert.Equal(0, changes);
        Assert.Equal(AdminInsightsPinState.Hidden, pins.StateOf(DashboardRegistry.InsightsWeekday));
        Assert.False(pins.CanAdd);
    }
}
