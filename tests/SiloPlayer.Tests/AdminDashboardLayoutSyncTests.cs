using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
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

    private const string EgressOnly = """{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3,"range":"day"}]}""";
    private const string EgressNodes = """{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3,"range":"day","view":"nodes"}]}""";

    private static string Users(int span, int rows = 4) => $$"""{"version":1,"entries":[{"id":"users","span":{{span}},"rows":{{rows}}}]}""";

    private static string AccountScope => DashboardLayoutSync.Scope(Origin, "1");

    private DashboardLayoutPendingDraft? StoredPending() => DashboardLayoutDocument.ReadPending(_cache.Read(AccountScope));

    /// <summary>
    /// Opens the dashboard with <paramref name="cached"/> in this account's cache and the
    /// first layout read held, then makes an edit (by default users span 5 to 7) before it returns.
    /// </summary>
    private (DashboardLayoutSync Sync, Task Load, TaskCompletionSource<DashboardLayoutRead> Read) OpenWithReadHeld(
        string cached = UsersOnly, Action<DashboardLayoutSync>? edit = null)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _cache.Write(AccountScope, cached);
        var read = new TaskCompletionSource<DashboardLayoutRead>();
        _server.OnRead = _ => read.Task;
        var sync = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        var load = sync.LoadAsync();
        edit ??= s => s.ResizeWidget("users", 7, null);
        edit(sync);
        return (sync, load, read);
    }

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
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"C\"", null));
        held.SetResult("\"B\"");
        await sync.PendingWrites;
        Assert.Equal(1, _server.Resets);
        Assert.Single(_server.Saves);
        Assert.False(sync.ServerSaveBlocked);
        sync.RemoveWidget("users");
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(2, _server.Saves.Count);
        Assert.Equal("\"C\"", _server.Saves[1].ETag);
        Assert.Equal(DashboardLayoutDocument.Serialize(sync.Entries), _server.Saves[1].Document);
    }

    [Fact]
    public async Task ResetValidatorReadPreservesAndSavesEditsMadeWhileItWaits()
    {
        var (sync, _) = await Loaded();
        var read = new TaskCompletionSource<DashboardLayoutRead>();
        _server.OnRead = _ => read.Task;
        sync.ResetLayout();
        sync.RemoveWidget("users");
        Assert.True(sync.ServerSaveBlocked);
        read.SetResult(DashboardFakeLayoutServer.Layout("\"reset\"", null));
        await sync.PendingWrites;
        Assert.False(sync.ServerSaveBlocked);
        Assert.DoesNotContain(sync.Entries, entry => entry.Id == "users");
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"reset\"", save.ETag);
        Assert.Equal(DashboardLayoutDocument.Serialize(sync.Entries), save.Document);
    }

    [Fact]
    public async Task FailedResetValidatorReadKeepsSubsequentEditsLocal()
    {
        var (sync, _) = await Loaded();
        var unconfirmed = 0;
        sync.ResetUnconfirmed += () => unconfirmed++;
        _server.OnRead = _ => Task.FromException<DashboardLayoutRead>(new HttpRequestException("lost"));
        sync.ResetLayout();
        await sync.PendingWrites;
        Assert.Equal(1, _server.Resets);
        Assert.Equal(1, unconfirmed);
        Assert.True(sync.ServerSaveBlocked);
        sync.RemoveWidget("users");
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
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
    public async Task TransportCatchUpKeepsThePendingLayoutEditAndItsSave()
    {
        var (sync, lease) = await Loaded();
        sync.ResizeWidget("users", 7, null);
        Assert.True(_kit.Client.TryInvalidateAccessContext(lease.Context, out var updated, transportCatchUp: true));
        Assert.True(lease.IsCurrent);
        Assert.Equal(updated, lease.Context);
        Assert.Equal(7, sync.Entries[0].Span);
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", save.ETag);
        Assert.Equal(DashboardLayoutDocument.Serialize(sync.Entries), save.Document);
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

    // ---- Pre-read edits (WebUI 6cfa95e3 / #4167): saved only over the base or no layout ----

    [Theory]
    [InlineData(UsersOnly)]
    [InlineData(null)]
    [InlineData("""{"version":2,"entries":[]}""")]
    public async Task SavesAnEditMadeBeforeTheFirstReadAgainstTheVersionThatReadReturns(string? serverJson)
    {
        var (sync, load, read) = OpenWithReadHeld();
        // Like the WebUI, an edit waiting for the first read is not reported as unsaved.
        Assert.False(sync.ServerSaveBlocked);
        Assert.NotNull(StoredPending());
        Debounce();
        Assert.Empty(_server.Saves);

        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", serverJson));
        await load;
        Assert.Equal(7, sync.Entries[0].Span);
        Assert.False(sync.ServerSaveBlocked);
        Assert.Null(StoredPending());
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", save.ETag);
        Assert.Equal(Users(7), save.Document);

        sync.ResizeWidget("users", 8, null);
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(2, _server.Saves.Count);
        Assert.Equal("\"S1\"", _server.Saves[1].ETag);
        Assert.False(sync.ServerSaveBlocked);
    }

    [Fact]
    public async Task WithoutACacheTheDefaultLayoutIsTheBaseOfAPreReadEdit()
    {
        var read = new TaskCompletionSource<DashboardLayoutRead>();
        _server.OnRead = _ => read.Task;
        SynchronizationContext.SetSynchronizationContext(null);
        var sync = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        var load = sync.LoadAsync();
        sync.RemoveWidget("users");
        Assert.Equal(DashboardRegistry.DefaultLayout, StoredPending()!.Base);
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", DashboardLayoutDocument.Serialize(DashboardRegistry.DefaultLayout)));
        await load;
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", save.ETag);
        Assert.DoesNotContain("\"users\"", save.Document);
    }

    [Fact]
    public async Task DoesNotOverwriteADifferentServerLayoutWithAnEditMadeBeforeTheFirstRead()
    {
        var (sync, load, read) = OpenWithReadHeld();
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", Users(8)));
        await load;
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.Equal(7, sync.Entries[0].Span);
        Assert.True(sync.ServerSaveBlocked);
        Assert.NotNull(StoredPending());

        // A later edit also sends nothing and keeps the draft marked as unsaved.
        sync.ResizeWidget("users", null, 5);
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.Equal((7, 5), (sync.Entries[0].Span, sync.Entries[0].Rows));
        Assert.Equal("users:5x4", string.Join(";", StoredPending()!.Base.Select(e => $"{e.Id}:{e.Span}x{e.Rows}")));

        // Reload adopts the server copy, clears the marker and resumes saving under its validator.
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"C\"", Users(8)));
        await sync.ReloadServerLayoutAsync();
        Assert.False(sync.ServerSaveBlocked);
        Assert.Equal(8, sync.Entries[0].Span);
        Assert.Null(StoredPending());
        Assert.Equal(Users(8), _cache.Read(AccountScope));
        sync.ResizeWidget("users", 6, null);
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"C\"", save.ETag);
        Assert.Equal(Users(6), save.Document);
    }

    [Fact]
    public async Task AServerLayoutThatDiffersOnlyInItsViewIsADifferentLayout()
    {
        var (sync, load, read) = OpenWithReadHeld(EgressNodes, s => s.SetWidgetRange("egress-24h", DashboardWidgetRange.Month));
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", EgressOnly));
        await load;
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.True(sync.ServerSaveBlocked);
        Assert.Equal(new DashboardLayoutEntry("egress-24h", 6, 3, DashboardWidgetRange.Month, "nodes"), sync.Entries[0]);
    }

    [Fact]
    public async Task DoesNotSendAnEditMadeBeforeTheFirstReadUnderAnAuthorityThatRetired()
    {
        var (sync, load, read) = OpenWithReadHeld();
        _kit.SetRole("user");
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", UsersOnly));
        await load;
        Debounce();
        await sync.DepartAsync();
        Assert.Empty(_server.Saves);
        Assert.False(sync.IsLoaded);
        // The retired owner neither sends nor consumes the account's unsaved draft.
        Assert.NotNull(StoredPending());
        Assert.Contains("\"span\":7", _cache.Read(AccountScope));
    }

    [Fact]
    public async Task ReportsAnEditMadeBeforeAFailedFirstReadAsUnsaved()
    {
        var (sync, load, read) = OpenWithReadHeld();
        read.SetException(new ApiException("unavailable", "down", 503));
        await load;
        Assert.True(sync.ServerSaveBlocked);
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.Equal(7, sync.Entries[0].Span);
        Assert.NotNull(StoredPending());
    }

    // ---- Leaving during the first read (WebUI 419c1175 / #4227): reconciled on the next visit ----

    [Fact]
    public async Task SavesAnEditMadeBeforeTheFirstReadOnceTheDashboardIsOpenedAgain()
    {
        var (first, firstLoad, firstRead) = OpenWithReadHeld();
        await first.DepartAsync();
        Assert.Empty(_server.Saves);
        Assert.NotNull(StoredPending());

        // The departed visit's read arriving late neither writes nor consumes the draft.
        firstRead.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", UsersOnly));
        await firstLoad;
        Assert.Empty(_server.Saves);
        Assert.NotNull(StoredPending());

        var (second, _) = await Loaded(UsersOnly);
        Assert.Equal(7, second.Entries[0].Span);
        Assert.False(second.ServerSaveBlocked);
        Assert.Null(StoredPending());
        Debounce();
        await second.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"A\"", save.ETag);
        Assert.Equal(Users(7), save.Document);
        Assert.DoesNotContain("pending", save.Document);
    }

    [Fact]
    public async Task ReportsAnEditMadeBeforeTheFirstReadAsUnsavedOnReturnWhenTheServerLayoutChanged()
    {
        var (first, _, _) = OpenWithReadHeld();
        await first.DepartAsync();

        var (second, _) = await Loaded(Users(9), "\"C\"");
        Debounce();
        await second.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.Equal(7, second.Entries[0].Span);
        Assert.True(second.ServerSaveBlocked);

        // Leaving without reloading keeps reporting the edit rather than adopting over it.
        await second.DepartAsync();
        var (third, _) = await Loaded(Users(9), "\"C\"");
        Debounce();
        await third.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.Equal(7, third.Entries[0].Span);
        Assert.True(third.ServerSaveBlocked);
        Assert.NotNull(StoredPending());
    }

    [Fact]
    public async Task ReportsAnEarlierVisitsDraftAsUnsavedWhenTheNextFirstReadFails()
    {
        var (first, _, _) = OpenWithReadHeld();
        await first.DepartAsync();
        SynchronizationContext.SetSynchronizationContext(null);
        _server.OnRead = _ => Task.FromException<DashboardLayoutRead>(new ApiException("unavailable", "down", 503));
        var second = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        Assert.Equal(7, second.Entries[0].Span);
        await second.LoadAsync();
        Assert.True(second.ServerSaveBlocked);
        Debounce();
        await second.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.NotNull(StoredPending());
    }

    [Fact]
    public async Task KeepsAnEarlierVisitsEditUnsavedUnderADifferentProfile()
    {
        var (first, _, _) = OpenWithReadHeld();
        await first.DepartAsync();

        _kit.Auth.SelectProfile("p2", null, new Profile { Id = "p2", Name = "Other", IsPrimary = true });
        var (other, _) = await Loaded(UsersOnly);
        Debounce();
        await other.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.Equal(7, other.Entries[0].Span);
        Assert.True(other.ServerSaveBlocked);

        // The other profile's own edit neither saves nor drops the pending one.
        other.ResizeWidget("users", null, 5);
        Debounce();
        await other.DepartAsync();
        Assert.Empty(_server.Saves);
        Assert.NotNull(StoredPending());

        _kit.Auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        var (back, _) = await Loaded(UsersOnly);
        Debounce();
        await back.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal(Users(7, 5), save.Document);
        Assert.Equal("\"A\"", save.ETag);
    }

    [Fact]
    public async Task SavesTheEditAnotherWindowLeftPendingNotThisWindowsOlderCopy()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _cache.Write(AccountScope, UsersOnly);
        var held = new List<TaskCompletionSource<DashboardLayoutRead>>();
        _server.OnRead = _ =>
        {
            var pending = new TaskCompletionSource<DashboardLayoutRead>();
            held.Add(pending);
            return pending.Task;
        };
        var waiting = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        var waitingLoad = waiting.LoadAsync();
        var editing = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        _ = editing.LoadAsync();
        editing.ResizeWidget("users", 7, null);
        await editing.DepartAsync();
        Assert.Equal(5, waiting.Entries[0].Span);

        held[0].SetResult(DashboardFakeLayoutServer.Layout("\"A\"", null));
        await waitingLoad;
        Debounce();
        await waiting.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal(Users(7), save.Document);
        Assert.Equal(7, waiting.Entries[0].Span);
    }

    [Fact]
    public async Task AnotherAccountNeverReconcilesOrUploadsAPendingEdit()
    {
        var (first, _, _) = OpenWithReadHeld();
        await first.DepartAsync();

        using var otherKit = AdminMonitorKit.Create(accountId: "2");
        using var otherAuthority = new AdminMonitorAuthority(otherKit.Client, otherKit.Auth);
        SynchronizationContext.SetSynchronizationContext(null);
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"A\"", null));
        var other = new DashboardLayoutSync(otherAuthority.Capture(), _server, _cache, Origin, _time);
        Assert.Equal(DashboardRegistry.DefaultLayout, other.Entries);
        await other.LoadAsync();
        Debounce();
        await other.PendingWrites;
        Assert.Empty(_server.Saves);
        Assert.False(other.ServerSaveBlocked);
        Assert.Equal(DashboardRegistry.DefaultLayout, other.Entries);
        Assert.NotNull(StoredPending());
    }

    [Fact]
    public async Task ResetRemovesAHeldDraftAndSavesWithoutAMarker()
    {
        var (sync, load, read) = OpenWithReadHeld();
        read.SetResult(DashboardFakeLayoutServer.Layout("\"A\"", Users(9)));
        await load;
        Assert.NotNull(StoredPending());
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"R\"", null));
        sync.ResetLayout();
        Assert.Null(_cache.Read(AccountScope));
        await sync.PendingWrites;
        Assert.Equal(1, _server.Resets);
        Assert.False(sync.ServerSaveBlocked);
        sync.ResizeWidget("users", 6, null);
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal("\"R\"", save.ETag);
        Assert.DoesNotContain("pending", save.Document);
        Assert.Null(StoredPending());
    }

    [Fact]
    public async Task AnOlderCacheDocumentWithoutAMarkerIsAdoptedOverAsBefore()
    {
        _cache.Write(AccountScope, UsersOnly);
        var (sync, _) = await Loaded(Users(8));
        Assert.Equal(8, sync.Entries[0].Span);
        Assert.False(sync.ServerSaveBlocked);
        Assert.Empty(_server.Saves);
        Assert.Equal(Users(8), _cache.Read(AccountScope));
    }

    // ---- Per-entry views (WebUI 2c062d9c / #4062) ----

    [Fact]
    public async Task ViewChangesRideOneDebouncedSaveAndPickingTheShownViewSavesNothing()
    {
        var (sync, _) = await Loaded(EgressOnly);
        var shown = sync.Entries;
        sync.SetWidgetView("egress-24h", DashboardRegistry.EgressViewTotal);
        Assert.Same(shown, sync.Entries);
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);

        sync.SetWidgetView("egress-24h", DashboardRegistry.EgressViewNodes);
        sync.SetWidgetView("egress-24h", "sideways");
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(EgressNodes, Assert.Single(_server.Saves).Document);
        Assert.Equal(EgressNodes, _cache.Read(AccountScope));

        // The next visit paints the stored view from the cache.
        var reopened = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        Assert.Equal("nodes", reopened.Entries[0].View);
    }

    [Fact]
    public async Task EditsOnAServerLayoutThatStoresAViewKeepTheView()
    {
        const string server = """{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3,"range":"day","view":"nodes"},{"id":"users","span":5,"rows":4}]}""";
        var (sync, _) = await Loaded(server);
        Assert.Equal("nodes", sync.Entries[0].View);
        sync.MoveWidget("users", "egress-24h");
        Debounce();
        await sync.PendingWrites;
        sync.SetWidgetRange("egress-24h", DashboardWidgetRange.Month);
        Debounce();
        await sync.PendingWrites;
        sync.ResizeWidget("egress-24h", 8, null);
        sync.AddWidget("scanner");
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(3, _server.Saves.Count);
        Assert.All(_server.Saves, save => Assert.Contains("\"view\":\"nodes\"", save.Document));
        Assert.Equal(
            """{"version":1,"entries":[{"id":"users","span":5,"rows":4},{"id":"egress-24h","span":8,"rows":3,"range":"month","view":"nodes"},{"id":"scanner","span":4,"rows":3}]}""",
            _server.Saves[^1].Document);
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
