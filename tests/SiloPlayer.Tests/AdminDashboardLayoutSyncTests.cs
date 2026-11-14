using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Server layout persistence: the WebUI's useDashboardLayout and dashboardLayoutRevision contract.</summary>
public sealed class AdminDashboardLayoutSyncTests : IDisposable
{
    private const string Origin = "https://silo.test";
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly DashboardManualTime _time = new();
    private readonly DashboardFakeLayoutServer _server = new();
    private readonly DashboardMemoryLayoutCache _cache = new();
    private const string UsersOnly = """{"version":1,"entries":[{"id":"users","span":5,"rows":4}]}""";

    public AdminDashboardLayoutSyncTests()
    {
        // Controllers marshal through the creating context; tests drive them context-free.
        SynchronizationContext.SetSynchronizationContext(null);
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private async Task<(DashboardLayoutSync Sync, MonitorAuthorityLease Lease)> Loaded(string? serverJson = UsersOnly, string etag = "\"A\"")
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout(etag, serverJson));
        var lease = _authority.Capture();
        var sync = new DashboardLayoutSync(lease, _server, _cache, Origin, _time);
        await sync.LoadAsync();
        return (sync, lease);
    }

    private void Debounce() => _time.Advance(DashboardLayoutSync.SaveDebounce + TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task AdoptsServerLayoutAndMirrorsItToTheAccountScopedCache()
    {
        var (sync, _) = await Loaded();
        Assert.Equal(["users"], sync.Entries.Select(e => e.Id));
        Assert.Contains("\"users\"", _cache.Read(DashboardLayoutSync.Scope(Origin, "1")));
        Assert.Null(_cache.Read(DashboardLayoutSync.Scope(Origin, "2")));
        Assert.Null(_cache.Read(DashboardLayoutSync.Scope("https://other.test", "1")));
        Assert.False(sync.ServerSaveBlocked);
    }

    [Fact]
    public async Task PaintsFromCacheThenMigratesALocalOnlyLayoutExactlyOnce()
    {
        _cache.Write(DashboardLayoutSync.Scope(Origin, "1"), UsersOnly);
        var (sync, _) = await Loaded(serverJson: null);
        Assert.Equal(["users"], sync.Entries.Select(e => e.Id));
        await sync.PendingWrites;
        Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", _server.Saves[0].ETag);
        await sync.LoadAsync();
        Assert.Single(_server.Saves);
    }

    [Fact]
    public async Task NoCacheAndNoServerLayoutKeepsDefaultsWithoutWriting()
    {
        var (sync, _) = await Loaded(serverJson: null);
        Assert.Equal(DashboardRegistry.DefaultLayout, sync.Entries);
        Assert.Empty(_server.Saves);
    }

    [Fact]
    public async Task DebouncesEditsIntoOneSaveOfTheWholeLayoutUnderTheReadValidator()
    {
        var (sync, _) = await Loaded();
        sync.ResizeWidget("users", 6, null);
        sync.ResizeWidget("users", 7, null);
        sync.AddWidget("scanner");
        Assert.Empty(_server.Saves);
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", save.ETag);
        Assert.Equal("""{"version":1,"entries":[{"id":"users","span":7,"rows":4},{"id":"scanner","span":4,"rows":3}]}""", save.Document);
    }

    [Fact]
    public async Task RetainedNewerEditWaitsForTheInFlightAcknowledgementAndUsesItsValidator()
    {
        var first = new TaskCompletionSource<string>();
        var (sync, _) = await Loaded();
        _server.OnSave = n => n == 1 ? first.Task : Task.FromResult("\"D\"");
        sync.ResizeWidget("users", 6, null);
        Debounce();
        sync.ResizeWidget("users", 7, null);
        Debounce();
        Assert.Single(_server.Saves);
        first.SetResult("\"B\"");
        await sync.PendingWrites;
        Assert.Equal(2, _server.Saves.Count);
        Assert.Equal("\"B\"", _server.Saves[1].ETag);
        Assert.Contains("\"span\":7", _server.Saves[1].Document);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(428)]
    [InlineData(0)]
    public async Task ConflictOrLostResponseBlocksWritesUntilAnExplicitReload(int status)
    {
        var (sync, _) = await Loaded();
        var saveUnconfirmed = 0;
        sync.SaveUnconfirmed += () => saveUnconfirmed++;
        _server.OnSave = _ => status == 0
            ? Task.FromException<string>(new HttpRequestException("lost"))
            : Task.FromException<string>(new DashboardLayoutConflictException(status, "\"C\"", "stale"));
        sync.ResizeWidget("users", 6, null);
        Debounce();
        await sync.PendingWrites;
        Assert.True(sync.ServerSaveBlocked);
        Assert.Equal(1, saveUnconfirmed);
        sync.ResizeWidget("users", 7, null);
        Debounce();
        await sync.PendingWrites;
        Assert.Single(_server.Saves);
        Assert.Equal(7, sync.Entries[0].Span);

        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"C\"", """{"version":1,"entries":[{"id":"users","span":8,"rows":4}]}"""));
        _server.OnSave = _ => Task.FromResult("\"E\"");
        await sync.ReloadServerLayoutAsync();
        Assert.False(sync.ServerSaveBlocked);
        Assert.Equal(8, sync.Entries[0].Span);
        Assert.Single(_server.Saves);
        sync.ResizeWidget("users", 6, null);
        Debounce();
        await sync.PendingWrites;
        Assert.Equal("\"C\"", _server.Saves[^1].ETag);
    }

    [Fact]
    public async Task ReloadDoesNotAdoptOverAnEditMadeWhileItWaits()
    {
        var (sync, _) = await Loaded();
        var read = new TaskCompletionSource<DashboardLayoutRead>();
        _server.OnRead = _ => read.Task;
        var reload = sync.ReloadServerLayoutAsync();
        sync.ResizeWidget("users", 7, null);
        read.SetResult(DashboardFakeLayoutServer.Layout("\"C\"", """{"version":1,"entries":[{"id":"users","span":8,"rows":4}]}"""));
        await reload;
        Assert.Equal(7, sync.Entries[0].Span);
        Debounce();
        await sync.PendingWrites;
        Assert.Equal("\"A\"", _server.Saves[^1].ETag);
    }

    [Fact]
    public async Task ResetDropsTheQueuedSaveRestoresDefaultsAndRunsAfterAnInFlightWrite()
    {
        var held = new TaskCompletionSource<string>();
        var (sync, _) = await Loaded();
        _server.OnSave = _ => held.Task;
        sync.ResizeWidget("users", 6, null);
        Debounce();
        sync.ResizeWidget("users", 7, null);
        sync.ResetLayout();
        Assert.Equal(DashboardRegistry.DefaultLayout, sync.Entries);
        Assert.Null(_cache.Read(DashboardLayoutSync.Scope(Origin, "1")));
        Assert.Equal(0, _server.Resets);
        held.SetResult("\"B\"");
        await sync.PendingWrites;
        Assert.Equal(1, _server.Resets);
        Assert.Single(_server.Saves);
        Assert.True(sync.ServerSaveBlocked);
        sync.RemoveWidget("users");
        Debounce();
        await sync.PendingWrites;
        Assert.Single(_server.Saves);
    }

    [Fact]
    public async Task DepartureSendsTheQueuedEditAndKeepsANewerOneBehindTheInFlightWrite()
    {
        var held = new TaskCompletionSource<string>();
        var (sync, _) = await Loaded();
        sync.ResizeWidget("users", 6, 5);
        var departed = sync.DepartAsync();
        await departed;
        Assert.Equal(("""{"version":1,"entries":[{"id":"users","span":6,"rows":5}]}""", "\"A\""), _server.Saves.Single());

        var (second, _) = await Loaded();
        _server.Saves.Clear();
        _server.OnSave = n => n == 1 ? held.Task : Task.FromResult("\"C\"");
        second.ResizeWidget("users", 6, null);
        Debounce();
        second.ResizeWidget("users", 7, null);
        var leaving = second.DepartAsync();
        Assert.Single(_server.Saves);
        held.SetResult("\"B\"");
        await leaving;
        Assert.Equal(2, _server.Saves.Count);
        Assert.Equal("\"B\"", _server.Saves[1].ETag);
        Assert.Contains("\"span\":7", _server.Saves[1].Document);
    }

    [Fact]
    public async Task RetiredAuthorityNeverSendsAQueuedEditAndRefusesNewEdits()
    {
        var (sync, lease) = await Loaded();
        sync.ResizeWidget("users", 6, null);
        _kit.SetRole("user");
        Assert.False(lease.IsCurrent);
        Debounce();
        await sync.DepartAsync();
        Assert.Empty(_server.Saves);
        var before = sync.Entries;
        sync.ResizeWidget("users", 8, null);
        sync.ResetLayout();
        Assert.Same(before, sync.Entries);
        Assert.Equal(0, _server.Resets);
    }

    [Fact]
    public async Task AdminLostAndRegainedNeverRevivesTheOldLease()
    {
        var (sync, lease) = await Loaded();
        sync.ResizeWidget("users", 6, null);
        _kit.SetRole("user");
        _kit.SetRole("admin");
        Assert.True(_authority.IsAvailable);
        Assert.False(lease.IsCurrent);
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
    }

    [Theory]
    [InlineData(UsersOnly)]
    [InlineData(null)]
    public async Task AnEditMadeBeforeTheServerReadArrivesIsNotOverwrittenAndIsSaved(string? serverJson)
    {
        var read = new TaskCompletionSource<DashboardLayoutRead>();
        _server.OnRead = _ => read.Task;
        SynchronizationContext.SetSynchronizationContext(null);
        var sync = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        var load = sync.LoadAsync();
        sync.RemoveWidget("users");
        Assert.True(sync.ServerSaveBlocked);
        var resumed = false;
        sync.Changed += () => { if (!sync.ServerSaveBlocked) resumed = true; };
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", serverJson));
        await load;
        Assert.DoesNotContain(sync.Entries, entry => entry.Id == "users");
        Assert.Contains(sync.Entries, entry => entry.Id == "libraries");
        Assert.False(sync.ServerSaveBlocked);
        Assert.True(resumed);
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", save.ETag);
        Assert.Equal(DashboardLayoutDocument.Serialize(sync.Entries), save.Document);

        sync.RemoveWidget("libraries");
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(2, _server.Saves.Count);
        Assert.Equal("\"S1\"", _server.Saves[1].ETag);
        Assert.False(sync.ServerSaveBlocked);
    }

    [Fact]
    public async Task FailedLayoutReadLeavesEditsLocalWithTheSaveBlockedNotice()
    {
        _server.OnRead = _ => Task.FromException<DashboardLayoutRead>(new ApiException("unavailable", "down", 503));
        SynchronizationContext.SetSynchronizationContext(null);
        var sync = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        await sync.LoadAsync();
        sync.RemoveWidget("users");
        Debounce();
        await sync.PendingWrites;
        Assert.True(sync.ServerSaveBlocked);
        Assert.Empty(_server.Saves);
    }

    [Fact]
    public async Task RangeChangesRideTheSameDebouncedSave()
    {
        var (sync, _) = await Loaded("""{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3}]}""");
        sync.SetWidgetRange("egress-24h", DashboardWidgetRange.Month);
        sync.SetWidgetRange("egress-24h", DashboardWidgetRange.Hour);
        Debounce();
        await sync.PendingWrites;
        Assert.Contains("\"range\":\"hour\"", Assert.Single(_server.Saves).Document);
    }
}
