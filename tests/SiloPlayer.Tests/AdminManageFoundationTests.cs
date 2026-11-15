using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>G0 shared Manage page guards, authority lifetime and bounded list/validator/failure contracts. Synthetic data only.</summary>
public sealed class AdminManageFoundationTests
{
    // ── Leave guard ───────────────────────────────────────────

    private sealed class Draft
    {
        public bool Dirty = true;
        public int Discards;
        public int Asks;
    }

    [Fact]
    public async Task CleanOrRetiredDraftLeavesWithoutAsking()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var owner = authority.Capture();
        var draft = new Draft { Dirty = false };
        var guard = new AdminLeaveGuard(() => owner, () => draft.Dirty, () => draft.Discards++);
        Task<bool?> Ask(MonitorAuthorityLease _) { draft.Asks++; return Task.FromResult<bool?>(false); }

        Assert.False(guard.RequiresConfirmation);
        Assert.True(await guard.CanLeaveAsync(Ask));
        draft.Dirty = true;
        Assert.True(guard.RequiresConfirmation);
        kit.SetRole("user");
        Assert.False(guard.RequiresConfirmation);
        Assert.True(await guard.CanLeaveAsync(Ask));
        Assert.Equal(0, draft.Asks);
        Assert.Equal(0, draft.Discards);
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 0)]
    [InlineData(null, false, 0)] // the confirmation could not be shown (dialog collision): keep the draft
    public async Task DirtyDraftAsksAndOnlyDiscardConfirmationLeaves(bool? answer, bool leaves, int discards)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var owner = authority.Capture();
        var draft = new Draft();
        var guard = new AdminLeaveGuard(() => owner, () => draft.Dirty, () => { draft.Discards++; draft.Dirty = false; });

        Assert.Equal(leaves, await guard.CanLeaveAsync(lease => { Assert.Same(owner, lease); return Task.FromResult(answer); }));
        Assert.Equal(discards, draft.Discards);
        Assert.False(guard.IsConfirming);
    }

    [Fact]
    public async Task SecondLeaveWhileAskingKeepsTheDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var owner = authority.Capture();
        var draft = new Draft();
        var guard = new AdminLeaveGuard(() => owner, () => draft.Dirty, () => draft.Discards++);
        var answer = new TaskCompletionSource<bool?>();

        var first = guard.CanLeaveAsync(_ => answer.Task);
        Assert.True(guard.IsConfirming);
        Assert.False(await guard.CanLeaveAsync(_ => Task.FromResult<bool?>(true)));
        answer.SetResult(false);
        Assert.False(await first);
        Assert.Equal(0, draft.Discards);
    }

    [Fact]
    public async Task OwnerRetiringWhileAskingReleasesWithoutAcceptingTheDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var owner = authority.Capture();
        var draft = new Draft();
        var guard = new AdminLeaveGuard(() => owner, () => draft.Dirty, () => draft.Discards++);
        var answer = new TaskCompletionSource<bool?>();

        var leave = guard.CanLeaveAsync(_ => answer.Task);
        kit.SetRole("user");
        answer.SetResult(true);
        Assert.True(await leave);
        Assert.Equal(0, draft.Discards);

        kit.SetRole("admin");
        owner = authority.Capture();
        var cancelled = guard.CanLeaveAsync(lease => Task.FromCanceled<bool?>(new CancellationToken(true)));
        Assert.False(await cancelled); // cancelled while the owner is still current: stay
    }

    // ── Authority lifetime ────────────────────────────────────

    [Fact]
    public void LifetimeBindsActionsToTheCurrentOwnerAndRetiresOnAuthorityChange()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lifetime = new AdminAuthorityLifetime(authority);
        var retired = 0;
        lifetime.Retired += () => retired++;

        Assert.Throws<MonitorAuthorityRetiredException>(() => lifetime.CaptureAction());
        var owner = lifetime.Open();
        using var action = lifetime.CaptureAction();
        Assert.True(lifetime.IsCurrent(owner));
        Assert.True(lifetime.IsCurrent(action));

        var replaced = lifetime.Open();
        Assert.False(owner.IsCurrent);
        Assert.False(action.IsCurrent);
        Assert.True(lifetime.IsCurrent(replaced));

        kit.SetRole("user");
        Assert.True(retired >= 1);
        Assert.Null(lifetime.Owner);
        Assert.False(lifetime.IsCurrent(replaced));
        Assert.Throws<MonitorAuthorityRetiredException>(() => lifetime.CaptureAction());
        Assert.Throws<MonitorAuthorityRetiredException>(() => lifetime.Open());

        kit.SetRole("admin");
        var fresh = lifetime.Open();
        Assert.True(fresh.Epoch > replaced.Epoch);
        Assert.False(replaced.IsCurrent);
    }

    [Fact]
    public void LifetimeRetireEndsTheOwnerAndDisposeStopsListening()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var lifetime = new AdminAuthorityLifetime(authority);
        var owner = lifetime.Open();
        lifetime.Retire();
        Assert.False(owner.IsCurrent);
        Assert.Null(lifetime.Owner);
        lifetime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lifetime.Open());
    }


    [Fact]
    public void ExplicitDepartureDoesNotRequestRestart()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lifetime = new AdminAuthorityLifetime(authority);
        lifetime.Open();
        var restarts = 0;
        lifetime.Retired += () => restarts++;
        lifetime.Retire();
        kit.SetRole("user"); kit.SetRole("admin");
        Assert.Equal(0, restarts);
        Assert.Throws<MonitorAuthorityRetiredException>(() => lifetime.Open());
        lifetime.Resume();
        Assert.True(lifetime.Open().IsCurrent);
    }

    [Fact]
    public void RouteStateCannotBeMutatedByItsCaller()
    {
        var query = new Dictionary<string, string> { ["tab"] = "invitations" };
        var route = new AdminRouteRequest(AdminRoute.Users, query);
        var hash = route.GetHashCode();
        query["tab"] = "different";
        Assert.Equal("invitations", route.Get("tab"));
        Assert.Equal(hash, route.GetHashCode());
        var assigned = route with { Query = query };
        query["tab"] = "again";
        Assert.Equal("different", assigned.Get("tab"));
        var insensitive = new AdminRouteRequest(AdminRoute.Users,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TAB"] = "invitations" });
        Assert.Equal(route.Equals(insensitive), insensitive.Equals(route));
        Assert.NotEqual(route, insensitive);
    }

    [Fact]
    public void InvalidListResponsesAreNotTransportFailures() =>
        Assert.Equal(AdminFailureKind.InvalidResponse,
            AdminFailures.Classify(new AdminListInconsistentException(AdminListProblem.InvalidPage)));

    // ── Bounded pages and walks ───────────────────────────────

    private static ApiCollectionPage<Row> Page(IEnumerable<string> ids, bool hasMore = false, string? next = null) => new()
    {
        Items = ids.Select(id => new Row(id)).ToList(),
        Page = new ApiPageInfo { HasMore = hasMore, NextCursor = next },
    };

    private sealed record Row(string? Id);

    [Fact]
    public void PagesAppendEscapedBoundedCursors()
    {
        Assert.Equal("/api/v2/admin/devices?limit=50", AdminPaging.WithPage("/api/v2/admin/devices", 50, null));
        Assert.Equal("/api/v2/admin/subtitles?provider=x&limit=25&cursor=a%2Bb%3D%2F",
            AdminPaging.WithPage("/api/v2/admin/subtitles?provider=x", 25, "a+b=/"));
        Assert.Throws<ArgumentException>(() => AdminPaging.WithPage("/x", 1, new string('c', AdminPaging.MaxCursorLength + 1)));
        Assert.Throws<ArgumentException>(() => AdminPaging.WithPage("/x", 1, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdminPaging.WithPage("/x", 0, null));
        Assert.Equal(200, AdminPaging.ClampLimit(1000, 200));
        Assert.Equal(1, AdminPaging.ClampLimit(-3, 200));
        Assert.Equal(25, AdminPaging.ClampLimit(25, 100));
    }

    [Fact]
    public void PageValidationRejectsInconsistentEnvelopes()
    {
        Assert.Equal(AdminListProblem.InvalidPage,
            Assert.Throws<AdminListInconsistentException>(() => AdminPaging.ReadPage<Row>(new ApiCollectionPage<Row>(), 10)).Problem);
        Assert.Equal(AdminListProblem.InvalidPage, Assert.Throws<AdminListInconsistentException>(() => AdminPaging.ReadPage<Row>(null, 10)).Problem);
        Assert.Equal(AdminListProblem.OversizedPage,
            Assert.Throws<AdminListInconsistentException>(() => AdminPaging.ReadPage(Page(["a", "b", "c"]), 2)).Problem);
        Assert.Equal(AdminListProblem.MissingContinuation,
            Assert.Throws<AdminListInconsistentException>(() => AdminPaging.ReadPage(Page(["a"], hasMore: true), 2)).Problem);
        Assert.Equal(AdminListProblem.MissingContinuation,
            Assert.Throws<AdminListInconsistentException>(() => AdminPaging.ReadPage(Page(["a"], true, new string('n', AdminPaging.MaxCursorLength + 1)), 2)).Problem);
        var last = AdminPaging.ReadPage(Page(["a"], hasMore: false, next: "ignored"), 2);
        Assert.False(last.HasMore);
        Assert.Null(last.NextCursor);
    }

    private sealed class Pages(params ApiCollectionPage<Row>[] pages)
    {
        public List<(string? Cursor, int Limit)> Requests { get; } = [];
        public Action? OnFetch { get; set; }

        public Task<ApiCollectionPage<Row>?> Fetch(string? cursor, int limit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add((cursor, limit));
            OnFetch?.Invoke();
            return Task.FromResult<ApiCollectionPage<Row>?>(pages[Requests.Count - 1]);
        }
    }

    [Fact]
    public async Task CompleteWalkYieldsASnapshotBoundToItsAuthority()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var pages = new Pages(Page(["1", "2"], true, "c1"), Page(["3"], true, "c2"), Page([]));

        var read = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"), new AdminWalkLimits(2, 5, 10), pages.Fetch, row => row.Id);

        Assert.True(read.IsComplete);
        Assert.Equal(["1", "2", "3"], read.Items.Select(row => row.Id));
        Assert.Equal(new (string?, int)[] { (null, 2), ("c1", 2), ("c2", 2) }, pages.Requests);
        Assert.NotNull(read.Snapshot);
        Assert.Same(read.Items, read.Snapshot!.Items);
        Assert.True(read.Snapshot.IsCurrentFor(lease, new AdminCollectionScope("/rows")));
        kit.SetRole("user");
        kit.SetRole("admin");
        using var later = authority.Capture();
        Assert.False(read.Snapshot.IsCurrentFor(later, new AdminCollectionScope("/rows")));
    }

    [Fact]
    public async Task WalkCapsReturnTruthfulTruncatedReadsWithoutSnapshot()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();

        var byPages = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"), new AdminWalkLimits(1, 2, 100),
            new Pages(Page(["1"], true, "a"), Page(["2"], true, "b"), Page(["3"])).Fetch, row => row.Id);
        Assert.Equal(AdminListCompleteness.TruncatedByPages, byPages.Completeness);
        Assert.Equal(["1", "2"], byPages.Items.Select(row => row.Id));
        Assert.Null(byPages.Snapshot);

        var byItems = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"), new AdminWalkLimits(3, 5, 2),
            new Pages(Page(["1", "2", "3"])).Fetch, row => row.Id);
        Assert.Equal(AdminListCompleteness.TruncatedByItems, byItems.Completeness);
        Assert.Equal(2, byItems.Items.Count);
        Assert.Null(byItems.Snapshot);

        var exactlyAtCap = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"), new AdminWalkLimits(2, 5, 2),
            new Pages(Page(["1", "2"])).Fetch, row => row.Id);
        Assert.True(exactlyAtCap.IsComplete);
        Assert.NotNull(exactlyAtCap.Snapshot);
    }

    [Theory]
    [InlineData("repeat", AdminListProblem.RepeatedContinuation)]
    [InlineData("duplicate", AdminListProblem.DuplicateIdentity)]
    [InlineData("blank", AdminListProblem.MissingIdentity)]
    [InlineData("missing-cursor", AdminListProblem.MissingContinuation)]
    public async Task InconsistentWalksPublishNothing(string mode, AdminListProblem problem)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var pages = mode switch
        {
            "repeat" => new Pages(Page(["1"], true, "same"), Page(["2"], true, "same"), Page(["3"])),
            "duplicate" => new Pages(Page(["1"], true, "a"), Page(["1"])),
            "blank" => new Pages(Page(["1", ""])),
            _ => new Pages(Page(["1"], true, null)),
        };
        var error = await Assert.ThrowsAsync<AdminListInconsistentException>(() =>
            AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"), new AdminWalkLimits(5, 10, 100), pages.Fetch, row => row.Id));
        Assert.Equal(problem, error.Problem);
    }

    [Fact]
    public async Task WalkStopsWhenAuthorityRetiresMidRead()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var pages = new Pages(Page(["1"], true, "a"), Page(["2"]));
        pages.OnFetch = () => { if (pages.Requests.Count == 1) kit.SetRole("user"); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"), new AdminWalkLimits(1, 10, 10), pages.Fetch, row => row.Id));
        Assert.Single(pages.Requests);
    }

    [Fact]
    public void PagerRestartsOnScopeChangeAndRejectsBadContinuations()
    {
        var pager = new AdminCursorPager(maxDepth: 2);
        Assert.True(pager.SetScope("s1"));
        Assert.False(pager.SetScope("s1"));
        Assert.Null(pager.Cursor);
        Assert.False(pager.CanGoBack);
        Assert.True(pager.TryAdvance(pager.Capture(), "c1"));
        Assert.True(pager.TryAdvance(pager.Capture(), "c2"));
        Assert.Equal(2, pager.PageIndex);
        Assert.False(pager.TryAdvance(pager.Capture(), "c3")); // depth cap
        Assert.True(pager.Back());
        Assert.Equal("c1", pager.Cursor);
        Assert.False(pager.TryAdvance(pager.Capture(), "c1")); // repeated continuation
        Assert.False(pager.TryAdvance(pager.Capture(), ""));
        Assert.False(pager.TryAdvance(pager.Capture(), new string('x', AdminPaging.MaxCursorLength + 1)));
        Assert.True(pager.SetScope("s2"));
        Assert.Equal(0, pager.PageIndex);
        Assert.Null(pager.Cursor);
        Assert.False(pager.Back());
    }

    [Fact]
    public void PagerScopeIncludesAuthorityPageSizeAndEveryFilterUnambiguously()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var scope = AdminCursorPager.ScopeOf(lease, 25, "opensubtitles", "en", null, "q");
        Assert.Equal(scope, AdminCursorPager.ScopeOf(lease, 25, "opensubtitles", "en", null, "q"));
        Assert.NotEqual(scope, AdminCursorPager.ScopeOf(lease, 50, "opensubtitles", "en", null, "q"));
        Assert.NotEqual(scope, AdminCursorPager.ScopeOf(lease, 25, "opensubtitles", "en", "", "q"));
        Assert.NotEqual(AdminCursorPager.ScopeOf(lease, 25, "a|b", "c"), AdminCursorPager.ScopeOf(lease, 25, "a", "b|c"));
        kit.SetRole("user");
        kit.SetRole("admin");
        using var next = authority.Capture();
        Assert.NotEqual(scope, AdminCursorPager.ScopeOf(next, 25, "opensubtitles", "en", null, "q"));
    }


    [Fact]
    public async Task FilteredCompletionNeverAuthorizesWholeCollectionWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var read = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows", "search=one"),
            new AdminWalkLimits(10, 2, 20), new Pages(Page(["one"])).Fetch, row => row.Id);
        Assert.True(read.IsComplete);
        Assert.Null(read.Snapshot);
        var all = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("/rows"),
            new AdminWalkLimits(10, 2, 20), new Pages(Page(["one"])).Fetch, row => row.Id);
        Assert.True(all.Snapshot!.IsCurrentFor(lease, new AdminCollectionScope("/rows")));
        Assert.False(all.Snapshot.IsCurrentFor(lease, new AdminCollectionScope("/different")));
    }

    [Fact]
    public void LateCursorCannotCrossFilterOrNavigationChanges()
    {
        var pager = new AdminCursorPager();
        pager.SetScope("first");
        var first = pager.Capture();
        pager.SetScope("second");
        Assert.False(pager.TryAdvance(first, "old"));
        var second = pager.Capture();
        Assert.True(pager.TryAdvance(second, "new"));
        pager.Back();
        Assert.False(pager.TryAdvance(second, "stale-after-back"));
        Assert.Null(pager.Cursor);
    }

    // ── Validators and failures ───────────────────────────────

    [Theory]
    [InlineData("\"v1\"", true)]
    [InlineData("W/\"v1\"", false)]
    [InlineData("*", false)]
    [InlineData("v1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyStrongValidatorsPermitGuardedWrites(string? etag, bool strong)
    {
        Assert.Equal(strong, AdminValidators.IsStrong(etag));
        Assert.Equal(strong, new AdminVersioned<int>(1, etag).CanWrite);
        if (strong) Assert.Equal(etag, AdminValidators.RequireStrong(etag));
        else Assert.Throws<InvalidOperationException>(() => AdminValidators.RequireStrong(etag));
    }

    [Theory]
    [InlineData("profile_verification_required", 403, AdminFailureKind.ProfileVerificationRequired)]
    [InlineData("profile_unverified", 403, AdminFailureKind.ProfileVerificationRequired)]
    [InlineData("demo_restricted", 403, AdminFailureKind.DemoRestricted)]
    [InlineData("permission_denied", 403, AdminFailureKind.Forbidden)]
    [InlineData("capability_disabled", 409, AdminFailureKind.CapabilityUnavailable)]
    [InlineData("capability_not_configured", 409, AdminFailureKind.CapabilityUnavailable)]
    [InlineData("capability_unsupported", 501, AdminFailureKind.CapabilityUnavailable)]
    [InlineData("dependency_unavailable", 503, AdminFailureKind.Server)]
    [InlineData("conflict", 409, AdminFailureKind.Conflict)]
    [InlineData("precondition_failed", 412, AdminFailureKind.PreconditionFailed)]
    [InlineData("unknown", 412, AdminFailureKind.PreconditionFailed)]
    [InlineData("precondition_required", 428, AdminFailureKind.PreconditionRequired)]
    [InlineData("not_found", 404, AdminFailureKind.NotFound)]
    [InlineData("authentication_required", 401, AdminFailureKind.Unauthorized)]
    [InlineData("validation_failed", 422, AdminFailureKind.Validation)]
    [InlineData("malformed_request", 400, AdminFailureKind.Validation)]
    [InlineData("rate_limited", 429, AdminFailureKind.RateLimited)]
    [InlineData("internal_error", 500, AdminFailureKind.Server)]
    public void ApiFailuresStayDistinct(string code, int status, AdminFailureKind kind) =>
        Assert.Equal(kind, AdminFailures.Classify(new ApiException(code, "synthetic", status)));

    [Fact]
    public void ProblemTypeWinsOverLegacyCodeAndTransportStatesAreSeparate()
    {
        Assert.Equal(AdminFailureKind.CapabilityUnavailable,
            AdminFailures.Classify(new ApiException("forbidden", "synthetic", 409) { ProblemType = "capability_disabled" }));
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var lease = authority.Capture();
        Assert.Equal(AdminFailureKind.Cancelled, AdminFailures.Classify(new OperationCanceledException(), lease));
        Assert.Equal(AdminFailureKind.Transport, AdminFailures.Classify(new TaskCanceledException("timeout", new TimeoutException())));
        Assert.Equal(AdminFailureKind.Transport, AdminFailures.Classify(new HttpRequestException("offline")));
        Assert.Equal(AdminFailureKind.InvalidResponse, AdminFailures.Classify(new AdminListInconsistentException(AdminListProblem.RepeatedContinuation)));
        Assert.Equal(AdminFailureKind.Retired, AdminFailures.Classify(new MonitorAuthorityRetiredException()));
        kit.SetRole("user");
        Assert.Equal(AdminFailureKind.Retired, AdminFailures.Classify(new OperationCanceledException(), lease));
        Assert.Equal(AdminFailureKind.Unknown, AdminFailures.Classify(new InvalidOperationException()));
    }
}
