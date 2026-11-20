using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUserViewAsTests
{
    [Theory]
    [InlineData("https://silo.test", "")]
    [InlineData("https://silo.test/silo", "/silo")]
    [InlineData("https://silo.test/silo/", "/silo")]
    public async Task ScopedViewAsUsesOnlyItsOwnInMemoryIdentityAndRetiresWithOwner(string baseUrl, string prefix)
    {
        using var owner = new CancellationTokenSource();
        var handler = new PreviewHandler();
        var pair = JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"2"}}""")!.AsObject();
        using var preview = new AdminUserViewAs(baseUrl, "2", pair, owner.Token, () => !owner.IsCancellationRequested, handler);
        await preview.ReadAsync("/api/v2/profiles");
        Assert.Equal("GET " + prefix + "/api/v2/profiles", handler.LastRequest);
        Assert.Equal("synthetic-target", handler.Bearer);
        Assert.Equal("", handler.Profile);
        await preview.SelectProfileAsync("target-profile", null);
        await preview.ReadAsync("/api/v2/catalog?limit=25");
        Assert.Equal("target-profile", handler.Profile);
        Assert.Equal("GET " + prefix + "/api/v2/catalog", handler.LastRequest);
        owner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.ReadAsync("/api/v2/catalog?limit=25"));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("https://silo.test", "")]
    [InlineData("https://silo.test/silo", "/silo")]
    [InlineData("https://silo.test/silo/", "/silo")]
    public async Task ExplicitReturnRevokesOnlyTemporarySessionAndDisposeIsIdempotent(string baseUrl, string prefix)
    {
        var handler = new PreviewHandler();
        var pair = JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"2"}}""")!.AsObject();
        var preview = new AdminUserViewAs(baseUrl, "2", pair, CancellationToken.None, () => true, handler);
        await preview.EndAsync();
        Assert.Equal("POST " + prefix + "/api/v2/auth/logout", handler.LastRequest);
        Assert.Equal("synthetic-target", handler.Bearer);
        Assert.False(handler.HadBody);
        preview.Dispose(); preview.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.ReadAsync("/api/v2/profiles"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RetiredOwnerDoesNotPreventEndingItsTemporarySession()
    {
        using var owner = new CancellationTokenSource();
        var handler = new PreviewHandler();
        var pair = JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"2"}}""")!.AsObject();
        using var preview = new AdminUserViewAs("https://silo.test", "2", pair, owner.Token, () => !owner.IsCancellationRequested, handler);
        await preview.SelectProfileAsync("target-profile", null);
        owner.Cancel();

        await preview.EndAsync();

        Assert.Equal("POST /api/v2/auth/logout", handler.LastRequest);
        Assert.Equal("synthetic-target", handler.Bearer);
        Assert.Equal("", handler.Profile);
        Assert.False(handler.HadBody);
        Assert.Equal(1, handler.Calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.ReadAsync("/api/v2/profiles"));
    }

    [Fact]
    public async Task DiscardingPreviewRevokesItsSessionOnceWithoutRestoringAccess()
    {
        var handler = new PreviewHandler();
        var pair = JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"2"}}""")!.AsObject();
        var preview = new AdminUserViewAs("https://silo.test", "2", pair, CancellationToken.None, () => true, handler);

        preview.Dispose();
        preview.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.ReadAsync("/api/v2/profiles"));
        await handler.LogoutAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await preview.EndAsync();
        Assert.Equal("POST /api/v2/auth/logout", handler.LastRequest);
        Assert.Equal("synthetic-target", handler.Bearer);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DiscardingPreviewCancelsPendingReadButNotItsIndependentLogout()
    {
        var handler = new PreviewHandler { HoldReads = true };
        var pair = JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"2"}}""")!.AsObject();
        var preview = new AdminUserViewAs("https://silo.test", "2", pair, CancellationToken.None, () => true, handler);
        var read = preview.ReadAsync("/api/v2/profiles");
        await handler.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        preview.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await handler.LogoutAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await preview.EndAsync();
        Assert.Equal("POST /api/v2/auth/logout", handler.LastRequest);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void WrongTargetPairNeverBecomesAViewAsSession()
    {
        var pair = JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"3"}}""")!.AsObject();
        Assert.Throws<InvalidDataException>(() => new AdminUserViewAs("https://silo.test", "2", pair, CancellationToken.None, () => true));
    }

    private sealed class PreviewHandler : HttpMessageHandler
    {
        public string? Bearer { get; private set; }
        public string Profile { get; private set; } = "";
        public int Calls { get; private set; }
        public string LastRequest { get; private set; } = "";
        public bool HadBody { get; private set; }
        public bool HoldReads { get; init; }
        public TaskCompletionSource LogoutAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Bearer = request.Headers.Authorization?.Parameter;
            LastRequest = request.Method + " " + request.RequestUri!.AbsolutePath; HadBody = request.Content != null;
            Profile = request.Headers.TryGetValues("X-Profile-Id", out var values) ? values.Single() : "";
            if (request.RequestUri.AbsolutePath.EndsWith("/api/v2/auth/logout", StringComparison.Ordinal)) LogoutAttempt.TrySetResult();
            else if (HoldReads)
            {
                ReadStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false,"next_cursor":null}}""", HttpStatusCode.OK);
        }
    }
}
