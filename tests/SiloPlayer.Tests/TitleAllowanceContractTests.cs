using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class TitleAllowanceContractTests
{
    [Fact]
    public async Task NineOperationsUseExactPathsBodilessWritesAndCopyBody()
    {
        using var wire = new Wire(); var client = Client(wire); var api = new TitleAllowancesApi(client); var owner = client.CaptureContext();
        wire.Json = "{\"state\":\"available\"}";
        Assert.Equal("available", (await api.GetCapabilityAsync(owner)).State);
        wire.Json = "{\"items\":[{\"profile_id\":\"kid\",\"max_content_rating\":\"PG\",\"max_advisory_age\":10,\"state\":\"allowed_by_collection\"}]}";
        Assert.Equal("allowed_by_collection", (await api.GetTitleProfilesAsync(owner, "movie:a/b")).Items.Single().State);
        wire.Json = "{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"next+one\"}}";
        Assert.True((await api.GetAllowedTitlesAsync(owner, "kid/1", "cursor+=/")).Page!.HasMore);
        wire.Json = "{\"items\":[{\"collection_id\":\"c\",\"name\":\"Family\",\"item_count\":9,\"live\":false,\"allowed_at\":\"2026-11-01T01:02:03Z\"}]}";
        var collection = (await api.GetAllowedCollectionsAsync(owner, "kid/1")).Items.Single();
        Assert.False(collection.Live); Assert.Equal(9, collection.ItemCount);
        await api.SetTitleAsync(owner, "kid/1", "movie:a/b", true);
        await api.SetTitleAsync(owner, "kid/1", "movie:a/b", false);
        await api.SetCollectionAsync(owner, "kid/1", "c/1", true);
        await api.SetCollectionAsync(owner, "kid/1", "c/1", false);
        wire.Json = "{\"added\":7}";
        Assert.Equal(7, (await api.CopyCollectionAsync(owner, "kid/1", "c/1")).Added);
        Assert.Equal(new[] {
            "GET /api/v2/capabilities/title-allowances", "GET /api/v2/catalog/items/movie%3Aa%2Fb/title-allowances",
            "GET /api/v2/profiles/kid%2F1/allowed-titles?limit=50&cursor=cursor%2B%3D%2F", "GET /api/v2/profiles/kid%2F1/allowed-collections",
            "PUT /api/v2/profiles/kid%2F1/allowed-titles/movie%3Aa%2Fb", "DELETE /api/v2/profiles/kid%2F1/allowed-titles/movie%3Aa%2Fb",
            "PUT /api/v2/profiles/kid%2F1/allowed-collections/c%2F1", "DELETE /api/v2/profiles/kid%2F1/allowed-collections/c%2F1",
            "POST /api/v2/profiles/kid%2F1/allowed-titles/copy-collection"
        }, wire.Calls.Select(call => call.Method + " " + call.Path));
        Assert.All(wire.Calls.Take(8), call => Assert.Null(call.Body));
        var copy = JsonDocument.Parse(wire.Calls.Last().Body!).RootElement;
        Assert.Equal("c/1", copy.GetProperty("collection_id").GetString()); Assert.Single(copy.EnumerateObject());
        Assert.All(wire.Calls, call => { Assert.Equal("primary", call.Profile); Assert.Equal("fixture-proof", call.Proof); });
    }

    [Theory]
    [InlineData("within_limits", false, false, false)]
    [InlineData("above_limit", false, true, true)]
    [InlineData("allowed", true, true, true)]
    [InlineData("allowed_by_collection", true, false, true)]
    [InlineData("no_library_access", false, false, false)]
    [InlineData("future_state", false, false, false)]
    public void ServerStandingDrivesRowsWithoutMaturityCalculation(string state, bool check, bool enabled, bool offer)
    {
        var row = TitleAllowanceSession.RowState(state);
        Assert.Equal(check, row.Checked); Assert.Equal(enabled, row.Enabled);
        Assert.Equal(offer, TitleAllowanceSession.HasAllowableProfile([new() { State = state }]));
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public async Task CapabilityIsLazyAfterPrimaryAndLimitedSibling(bool primary, bool limited, int reads)
    {
        using var wire = new Wire { Household = limited ? Household : "{\"items\":[{\"id\":\"primary\",\"is_primary\":true}]}" };
        var client = Client(wire); var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = primary }, () => true);
        Assert.Equal(primary && limited, await session.LoadAsync());
        Assert.Equal(reads, wire.Calls.Count);
    }

    [Theory]
    [InlineData(404)] [InlineData(405)] [InlineData(501)]
    public async Task OldCapabilityDoesNotOfferManagement(int status)
    {
        using var wire = new Wire { Status = (HttpStatusCode)status }; var client = Client(wire);
        Assert.Equal("unsupported", (await new TitleAllowancesApi(client).GetCapabilityAsync(client.CaptureContext())).State);
    }

    [Fact]
    public async Task TransientFailureRemainsRetryableAndDeniedPrimaryIsNotHiddenAsUnsupported()
    {
        using var wire = new Wire { Status = HttpStatusCode.InternalServerError }; var client = Client(wire); var api = new TitleAllowancesApi(client);
        await Assert.ThrowsAsync<ApiException>(() => api.GetCapabilityAsync(client.CaptureContext()));
        wire.Status = HttpStatusCode.OK; wire.Json = "{\"state\":\"available\"}";
        Assert.Equal("available", (await api.GetCapabilityAsync(client.CaptureContext())).State);
        wire.Status = HttpStatusCode.Forbidden;
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => api.SetTitleAsync(client.CaptureContext(), "kid", "movie:x", true))).StatusCode);
    }

    [Theory]
    [InlineData("profile")] [InlineData("access")] [InlineData("account")] [InlineData("navigation")]
    public async Task RetiredOwnerCannotPublishReadOrSendMutation(string change)
    {
        using var wire = new Wire(); var client = Client(wire); var navigation = true;
        var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = true }, () => navigation);
        wire.After = () => { if (change == "profile") client.SetProfile("other"); else if (change == "access") client.InvalidateAccessContext(); else if (change == "account") client.SetBaseUrl("https://other.fixture.invalid"); else navigation = false; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync((api, owner, ct) => api.GetTitleProfilesAsync(owner, "movie:x", ct)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync((api, owner, ct) => api.SetTitleAsync(owner, "kid", "movie:x", true, ct)));
        Assert.Single(wire.Calls);
    }

    [Fact]
    public async Task CopyIsNeverAutomaticallyRetriedOnAuthenticationFailure()
    {
        using var wire = new Wire { Status = HttpStatusCode.Unauthorized }; var client = Client(wire); var refreshes = 0;
        client.SetTokenRefresher(_ => { ++refreshes; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => new TitleAllowancesApi(client).CopyCollectionAsync(client.CaptureContext(), "kid", "c"));
        Assert.Equal(0, refreshes); Assert.Single(wire.Calls);
    }

    [Fact]
    public async Task HouseholdDemotionRefusesCapabilityAndSearchUsesTheServerCatalog()
    {
        using var wire = new Wire { Household = "{\"items\":[{\"id\":\"primary\",\"is_primary\":false},{\"id\":\"kid\",\"max_content_rating\":\"PG\"}]}" };
        var client = Client(wire);
        var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = true }, () => true);
        Assert.False(await session.LoadAsync()); Assert.Single(wire.Calls);
        wire.Json = "{\"items\":[{\"content_id\":\"movie:high\",\"title\":\"High rating\",\"type\":\"movie\",\"content_rating\":\"R\"}]}";
        var found = await new TitleAllowancesApi(client).SearchAsync(client.CaptureContext(), "  A & B  ");
        Assert.Equal("movie:high", found.Items.Single().ContentId);
        Assert.Equal("/api/v2/catalog?source=query&limit=10&seek=0&q=A%20%26%20B", wire.Calls.Last().Path);
    }

    [Theory]
    [InlineData("", null, false)] [InlineData(" ", 0, false)]
    [InlineData("PG", null, true)] [InlineData("", 10, true)]
    public void LimitedSiblingMatchesOfficialPredicate(string rating, int? age, bool limited)
        => Assert.Equal(limited, TitleAllowanceSession.HasLimit(new() { MaxContentRating = rating, MaxAdvisoryAge = age }));

    [Theory]
    [InlineData("title")] [InlineData("collection")] [InlineData("copy")]
    public async Task MutationResponseCannotPublishAfterAccessChanges(string operation)
    {
        using var wire = new Wire { Json = "{\"added\":2}" }; var client = Client(wire); var api = new TitleAllowancesApi(client);
        var owner = client.CaptureContext(); wire.After = () => client.InvalidateAccessContext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            if (operation == "title") await api.SetTitleAsync(owner, "kid", "movie:x", true);
            else if (operation == "collection") await api.SetCollectionAsync(owner, "kid", "c", true);
            else await api.CopyCollectionAsync(owner, "kid", "c");
        });
        Assert.Single(wire.Calls);
    }

    private const string Household = "{\"items\":[{\"id\":\"primary\",\"is_primary\":true},{\"id\":\"kid\",\"max_advisory_age\":10}]}";
    private static SiloApiClient Client(Wire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://allowances.fixture.invalid"); client.SetProfile("primary", "fixture-proof"); return client;
    }
    private sealed record Call(string Method, string Path, string? Body, string Profile, string Proof);
    private sealed class Wire : HttpMessageHandler
    {
        internal string Json = "{\"state\":\"available\",\"items\":[]}", Household = TitleAllowanceContractTests.Household;
        internal HttpStatusCode Status = HttpStatusCode.OK; internal Action? After; internal List<Call> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            Calls.Add(new(request.Method.Method, path, request.Content == null ? null : await request.Content.ReadAsStringAsync(ct),
                request.Headers.GetValues("X-Profile-Id").Single(), request.Headers.GetValues("X-Profile-Token").Single()));
            After?.Invoke();
            return new(request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete ? Status == HttpStatusCode.OK ? HttpStatusCode.NoContent : Status : Status)
                { Content = new StringContent(Status == HttpStatusCode.OK ? path == "/api/v2/profiles" ? Household : Json : "{\"title\":\"Refused\",\"status\":" + (int)Status + "}") };
        }
    }
}
