using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Settings > API Keys (official silo-server 3b2b32168, #4411): the four requests and their shapes, paging with a repeated
/// cursor, creating exactly once, the scope and limit rules, and ownership of replies. Every key value here is made up.
/// </summary>
public sealed class PersonalApiKeysTests
{
    private const string FixtureSecret = "silo_fixture_not_a_real_key_0123456789";

    private sealed record Seen(HttpMethod Method, string Path, string Query, string? Profile, string Body);

    private sealed class Server
    {
        public readonly List<Seen> Requests = [];
        public Func<Seen, Task<HttpResponseMessage>> Handle = _ => Task.FromResult(Reply(HttpStatusCode.OK, "{}"));
        public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && r.Path == path);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string json, string type = "application/json")
        => new(status) { Content = new StringContent(json, Encoding.UTF8, type) };

    private static (SiloApiClient Client, Server Server, ApiKeysApi Api) Setup(string profile = "p1")
    {
        var server = new Server();
        var client = new SiloApiClient(new HttpClient(new Handler(server)));
        client.SetBaseUrl("https://fixture.invalid");
        client.SetAccessToken("fixture-access");
        client.SetProfile(profile);
        return (client, server, new ApiKeysApi(client));
    }

    private sealed class Handler(Server server) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            request.Headers.TryGetValues("X-Profile-Id", out var profile);
            var seen = new Seen(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, profile?.FirstOrDefault(), body);
            lock (server.Requests) server.Requests.Add(seen);
            return await server.Handle(seen);
        }
    }

    private static string Key(string id, string? profile = null, string? used = null, string label = "Fixture key")
        => "{\"id\":\"" + id + "\",\"user_id\":\"1\",\"label\":\"" + label + "\",\"key_prefix\":\"silo_fixt\",\"rate_tier\":\"standard\",\"scopes\":[\"watch:read\",\"catalog:read\"],"
           + (profile == null ? "" : "\"profile_id\":\"" + profile + "\",") + (used == null ? "" : "\"last_used_at\":\"" + used + "\",")
           + "\"created_at\":\"2026-11-01T10:00:00.000Z\"}";

    private static string Page(string[] keys, string? next = null)
        => "{\"items\":[" + string.Join(",", keys) + "],\"page\":{\"has_more\":" + (next != null ? "true" : "false") + (next != null ? ",\"next_cursor\":\"" + next + "\"" : "") + "}}";

    private const string EnabledScopes = """
        {"available":true,"user_keys_enabled":true,"max_user_keys":3,"scopes":[
          {"name":"catalog:read","description":"Browse","audience":"user"},
          {"name":"watch:read","description":"Read watch","audience":"user"},
          {"name":"admin:all","description":"Everything","audience":"admin"}]}
        """;

    private static PersonalApiKeysModel NewModel(SiloApiClient client, ApiKeysApi api, params Profile[] profiles)
        => new(api, client, _ => Task.FromResult<IReadOnlyList<Profile>>(profiles));

    private static readonly PersonalApiKeysViewer Primary = new("p1", "Primary", true, false);

    /// <summary>A server that answers scopes and the first page, and anything else with the supplied handler.</summary>
    private static void Serve(Server server, string scopes, string firstPage, Func<Seen, Task<HttpResponseMessage>>? other = null)
        => Serve(server, scopes, _ => firstPage, other);

    /// <summary>The first-page read of the Nth time (0 first) answers <c>firstPage(N)</c>; a reload after a write can differ from the opening read.</summary>
    private static void Serve(Server server, string scopes, Func<int, string> firstPage, Func<Seen, Task<HttpResponseMessage>>? other = null)
    {
        var reads = 0;
        server.Handle = seen => seen.Path switch
        {
            "/api/v2/api-keys/scopes" => Task.FromResult(Reply(HttpStatusCode.OK, scopes)),
            "/api/v2/api-keys" when seen.Method == HttpMethod.Get && !seen.Query.Contains("cursor") => Task.FromResult(Reply(HttpStatusCode.OK, firstPage(Interlocked.Increment(ref reads) - 1))),
            _ => other?.Invoke(seen) ?? Task.FromResult(Reply(HttpStatusCode.NotFound, "{}")),
        };
    }

    // ---------- requests and their shapes ----------

    [Fact]
    public async Task ScopesRequestReadsCapabilityAndScopeAudiences()
    {
        var (client, server, api) = Setup();
        server.Handle = _ => Task.FromResult(Reply(HttpStatusCode.OK, EnabledScopes));
        var scopes = await api.GetScopesAsync(client.CaptureIdentityContext());
        var seen = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Get, "/api/v2/api-keys/scopes", "p1"), (seen.Method, seen.Path, seen.Profile));
        Assert.True(scopes.Available && scopes.UserKeysEnabled);
        Assert.Equal(3, scopes.MaxUserKeys);
        Assert.Equal(["user", "user", "admin"], scopes.Scopes.Select(s => s.Audience));
    }

    [Fact]
    public async Task ListRequestAsksForFiftyAndEscapesTheCursor()
    {
        var (client, server, api) = Setup();
        server.Handle = _ => Task.FromResult(Reply(HttpStatusCode.OK, Page([Key("7", profile: "p2", used: "2026-11-02T08:30:00.000Z")], next: "n/+=")));
        var first = await api.GetPageAsync(client.CaptureIdentityContext());
        var again = await api.GetPageAsync(client.CaptureIdentityContext(), "c/+ 1");
        Assert.Equal("?limit=50", server.Requests[0].Query);
        Assert.Equal("?limit=50&cursor=c%2F%2B%201", server.Requests[1].Query);
        Assert.Equal("n/+=", first.NextCursor);
        var key = Assert.Single(first.Items);
        Assert.Equal(("7", "Fixture key", "p2", "2026-11-02T08:30:00.000Z"), (key.Id, key.Label, key.ProfileId, key.LastUsedAt));
        Assert.Equal(["watch:read", "catalog:read"], key.Scopes);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task ARepeatedCursorEndsThePage()
    {
        var (client, server, api) = Setup();
        server.Handle = _ => Task.FromResult(Reply(HttpStatusCode.OK, Page([Key("1")], next: "same")));
        var page = await api.GetPageAsync(client.CaptureIdentityContext(), "same");
        Assert.Null(page.NextCursor);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task APageWithoutMoreHasNoCursorEvenIfOneIsSent()
    {
        var (client, server, api) = Setup();
        server.Handle = _ => Task.FromResult(Reply(HttpStatusCode.OK, "{\"items\":[],\"page\":{\"has_more\":false,\"next_cursor\":\"ignored\"}}"));
        Assert.Null((await api.GetPageAsync(client.CaptureIdentityContext())).NextCursor);
    }

    [Fact]
    public async Task CreateSendsLabelScopesAndTheProfileOnlyWhenBound()
    {
        var (client, server, api) = Setup();
        server.Handle = _ => Task.FromResult(Reply(HttpStatusCode.Created, "{\"id\":\"9\",\"user_id\":\"1\",\"label\":\"L\",\"key\":\"" + FixtureSecret + "\",\"scopes\":[\"watch:read\"],\"rate_tier\":\"standard\",\"created_at\":\"2026-11-01T10:00:00.000Z\"}"));
        await api.CreateAsync(client.CaptureIdentityContext(), "Backup script", ["watch:read", "catalog:read"], "p2");
        await api.CreateAsync(client.CaptureIdentityContext(), "Whole", ["watch:read"], null);
        using var bound = JsonDocument.Parse(server.Requests[0].Body);
        using var whole = JsonDocument.Parse(server.Requests[1].Body);
        Assert.Equal((HttpMethod.Post, "/api/v2/api-keys"), (server.Requests[0].Method, server.Requests[0].Path));
        Assert.Equal("Backup script", bound.RootElement.GetProperty("label").GetString());
        Assert.Equal(["watch:read", "catalog:read"], bound.RootElement.GetProperty("scopes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("p2", bound.RootElement.GetProperty("profile_id").GetString());
        Assert.False(whole.RootElement.TryGetProperty("profile_id", out _));
    }

    [Fact]
    public async Task RevokeDeletesTheEscapedId()
    {
        var (client, server, api) = Setup();
        server.Handle = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await api.RevokeAsync(client.CaptureIdentityContext(), "12");
        var seen = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Delete, "/api/v2/api-keys/12", "p1"), (seen.Method, seen.Path, seen.Profile));
    }

    [Fact]
    public void TheCreatedKeyNeverPrintsItsSecret()
    {
        var created = new PersonalApiKeyCreated { Key = FixtureSecret, Label = "L" };
        Assert.DoesNotContain(FixtureSecret, created.ToString());
        Assert.DoesNotContain(FixtureSecret, new PersonalApiKeyCreateResult(PersonalApiKeyCreateStatus.Created, secret: FixtureSecret).ToString());
    }

    // ---------- the model: paging ----------

    [Fact]
    public async Task LoadMoreFollowsTheCursorAndStopsOnARepeatedOne()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, Page([Key("1"), Key("2")], next: "A"), seen => Task.FromResult(Reply(HttpStatusCode.OK, seen.Query.Contains("cursor=A")
            ? Page([Key("3"), Key("2")], next: "B")
            : seen.Query.Contains("cursor=B") ? Page([Key("4")], next: "A") : "{}")));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.True(model.HasNextPage);
        await model.LoadMoreAsync();
        Assert.Equal(["1", "2", "3"], model.Rows.Select(r => r.Id));
        Assert.True(model.HasNextPage);
        await model.LoadMoreAsync();
        // The server pointed back at cursor A, already used: the list ends instead of looping.
        Assert.Equal(["1", "2", "3", "4"], model.Rows.Select(r => r.Id));
        Assert.False(model.HasNextPage);
        Assert.Equal(1, server.Requests.Count(r => r.Query.Contains("cursor=A")));
        Assert.Equal(1, server.Requests.Count(r => r.Query.Contains("cursor=B")));
    }

    [Fact]
    public async Task AFailedLoadMoreShowsTheFailureAndTriesAgainFromTheSameCursor()
    {
        var (client, server, api) = Setup();
        var fail = true;
        Serve(server, EnabledScopes, Page([Key("1")], next: "A"), seen => Task.FromResult(fail
            ? Reply(HttpStatusCode.InternalServerError, "{}")
            : Reply(HttpStatusCode.OK, Page([Key("2")]))));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        await model.LoadMoreAsync();
        Assert.True(model.ListFailed);
        Assert.True(model.HasNextPage);
        fail = false;
        await model.LoadMoreAsync();
        Assert.False(model.ListFailed);
        Assert.Equal(["1", "2"], model.Rows.Select(r => r.Id));
        Assert.Equal(2, server.Requests.Count(r => r.Query.Contains("cursor=A")));
    }

    [Fact]
    public async Task LoadingEndsWithProfileNamesAndNeverBlocksOnThem()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, Page([Key("1", profile: "p2")]));
        var model = new PersonalApiKeysModel(api, client, _ => throw new HttpRequestException("names unavailable"));
        await model.LoadAsync(Primary);
        Assert.False(model.IsLoading);
        Assert.Equal("p2", model.ProfileName("p2"));
        Assert.Equal("Primary", model.ProfileName("p1"));
        var named = NewModel(client, api, new Profile { Id = "p2", Name = "Kids" });
        await named.LoadAsync(Primary);
        Assert.Equal("Kids", named.ProfileName("p2"));
    }

    // ---------- the model: states and rules ----------

    [Fact]
    public async Task OnlyUserScopesAreOfferedAndTheServerCanSwitchCreationOff()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, Page([]));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.Equal(["catalog:read", "watch:read"], model.UserScopes.Select(s => s.Name));
        Assert.True(model.Enabled && model.CanCreate);

        Serve(server, "{\"available\":true,\"user_keys_enabled\":false,\"max_user_keys\":3,\"scopes\":[]}", Page([]));
        await model.LoadAsync(Primary);
        Assert.False(model.Enabled);
        Assert.False(model.CanCreate);

        Serve(server, "{\"available\":false,\"user_keys_enabled\":true,\"max_user_keys\":3,\"scopes\":[]}", Page([]));
        await model.LoadAsync(Primary);
        Assert.False(model.Enabled);
    }

    [Fact]
    public async Task ScopesAndListFailuresAreSeparateStates()
    {
        var (client, server, api) = Setup();
        server.Handle = seen => Task.FromResult(seen.Path.EndsWith("/scopes") ? Reply(HttpStatusCode.InternalServerError, "{}") : Reply(HttpStatusCode.OK, Page([Key("1")])));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.True(model.CapabilityFailed);
        Assert.False(model.ListFailed);
        Assert.False(model.CanCreate);
        server.Handle = seen => Task.FromResult(seen.Path.EndsWith("/scopes") ? Reply(HttpStatusCode.OK, EnabledScopes) : Reply(HttpStatusCode.Forbidden, "{}"));
        await model.LoadAsync(Primary);
        Assert.False(model.CapabilityFailed);
        Assert.True(model.ListFailed);
        Assert.False(model.CanCreate);
    }

    [Theory]
    [InlineData(true, false, 3, false, 3, true)]   // primary profile holding the limit
    [InlineData(true, false, 3, false, 2, false)]  // below the limit
    [InlineData(true, true, 3, false, 3, false)]   // server admin: the server decides
    [InlineData(false, false, 3, false, 3, false)] // another profile learns the limit from the refusal
    [InlineData(true, false, 0, false, 5, false)]  // no limit set
    [InlineData(true, false, 1, true, 1, false)]   // more pages: the count is not the account's yet
    public async Task LimitReachedFollowsTheOfficialRule(bool primary, bool admin, int max, bool morePages, int keys, bool expected)
    {
        var (client, server, api) = Setup();
        var scopes = "{\"available\":true,\"user_keys_enabled\":true,\"max_user_keys\":" + max + ",\"scopes\":[]}";
        Serve(server, scopes, Page(Enumerable.Range(1, keys).Select(i => Key(i.ToString())).ToArray(), morePages ? "next" : null));
        var model = NewModel(client, api);
        await model.LoadAsync(new("p1", "Primary", primary, admin));
        Assert.Equal(expected, model.AtLimit);
        Assert.Equal(!expected, model.CanCreate);
    }

    [Fact]
    public async Task OnlyThePrimaryProfileChoosesWhoTheKeyActsAs()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, Page([]));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.Equal(PersonalApiKeysModel.WholeAccount, model.DefaultTarget);
        await model.LoadAsync(new("kid", "Kid", false, false));
        Assert.Equal("kid", model.DefaultTarget);
    }

    [Fact]
    public async Task ACreateIsNotOfferedWithoutAProfileOrALoadedList()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, "{}");
        server.Handle = seen => Task.FromResult(seen.Path.EndsWith("/scopes") ? Reply(HttpStatusCode.OK, EnabledScopes) : Reply(HttpStatusCode.InternalServerError, "{}"));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.True(model.Enabled);
        Assert.False(model.ListLoaded);
        Assert.False(model.CanCreate);
        Serve(server, EnabledScopes, Page([]));
        await model.LoadAsync(new("", "", false, false));
        Assert.False(model.CanCreate);
    }

    // ---------- creating: once, never replayed ----------

    private static async Task<(PersonalApiKeysModel Model, Server Server, SiloApiClient Client, int[] Refreshes)> Ready(Func<Seen, Task<HttpResponseMessage>> onCreate)
    {
        var (client, server, api) = Setup();
        var refreshes = new[] { 0 };
        client.SetTokenRefresher(_ => { refreshes[0]++; return Task.FromResult(true); });
        Serve(server, EnabledScopes, read => read == 0 ? Page([Key("1")]) : Page([Key("1"), Key("2")]), seen => seen.Method == HttpMethod.Post ? onCreate(seen) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        return (model, server, client, refreshes);
    }

    private static HttpResponseMessage Created(string secret = FixtureSecret)
        => Reply(HttpStatusCode.Created, "{\"id\":\"2\",\"user_id\":\"1\",\"label\":\"Backup\",\"key\":\"" + secret + "\",\"scopes\":[\"watch:read\"],\"rate_tier\":\"standard\",\"created_at\":\"2026-11-23T10:00:00.000Z\"}");

    [Fact]
    public async Task CreateReturnsTheSecretOnceAndReloadsTheList()
    {
        var (model, server, _, _) = await Ready(_ => Task.FromResult(Created()));
        var result = await model.CreateAsync("Backup", ["watch:read"], "p2");
        Assert.Equal(PersonalApiKeyCreateStatus.Created, result.Status);
        Assert.Equal(FixtureSecret, result.Secret);
        Assert.Equal("p2", result.Target);
        await Until(() => model.Rows.Count == 2);
        Assert.Equal(1, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
        // The listed keys never carry the secret.
        Assert.DoesNotContain(FixtureSecret, string.Join("|", model.Rows.Select(r => r.Label + r.KeyPrefix)));
    }

    [Fact]
    public async Task ACreateRefusedByTheServerShowsItsReasonAndIsNotRepeated()
    {
        var (model, server, _, refreshes) = await Ready(_ => Task.FromResult(Reply(HttpStatusCode.Conflict,
            "{\"type\":\"https://fixture.invalid/docs/api/v2/problems/conflict\",\"title\":\"Conflict\",\"status\":409,\"detail\":\"You can't create more API keys.\"}", "application/problem+json")));
        var result = await model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        Assert.Equal(PersonalApiKeyCreateStatus.Refused, result.Status);
        Assert.Contains("can't create more API keys", result.Message);
        Assert.Null(result.Secret);
        Assert.Equal(1, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
        Assert.Equal(0, refreshes[0]);
    }

    [Fact]
    public async Task AnExpiredTokenOnCreateIsNeverRefreshedAndReplayed()
    {
        var (model, server, _, refreshes) = await Ready(_ => Task.FromResult(Reply(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\",\"status\":401}", "application/problem+json")));
        var result = await model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        Assert.Equal(PersonalApiKeyCreateStatus.Refused, result.Status);
        Assert.Equal(0, refreshes[0]);
        Assert.Equal(1, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
    }

    [Theory]
    [InlineData("server-error")]
    [InlineData("connection-lost")]
    [InlineData("unreadable-answer")]
    [InlineData("answer-without-a-key")]
    public async Task AnUnknownOutcomeIsUncertainAndNothingIsResent(string how)
    {
        var (model, server, _, refreshes) = await Ready(_ => how switch
        {
            "server-error" => Task.FromResult(Reply(HttpStatusCode.BadGateway, "{}")),
            "connection-lost" => throw new HttpRequestException("connection reset"),
            "unreadable-answer" => Task.FromResult(Reply(HttpStatusCode.Created, "not json")),
            _ => Task.FromResult(Reply(HttpStatusCode.Created, "{\"id\":\"2\"}")),
        });
        var result = await model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        Assert.Equal(PersonalApiKeyCreateStatus.Uncertain, result.Status);
        Assert.Null(result.Secret);
        Assert.Equal(1, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
        Assert.Equal(0, refreshes[0]);
        // The list is read again: an uncertain create may have made the key.
        await Until(() => server.Requests.Count(r => r.Method == HttpMethod.Get && r.Path == "/api/v2/api-keys") >= 2);
    }

    [Fact]
    public async Task ASecondCreateWhileOneIsRunningIsNotSent()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (model, server, _, _) = await Ready(_ => gate.Task);
        var first = model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        await Until(() => server.Count(HttpMethod.Post, "/api/v2/api-keys") == 1);
        Assert.True(model.IsCreating);
        var second = await model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        Assert.Equal(PersonalApiKeyCreateStatus.Dropped, second.Status);
        gate.SetResult(Created());
        Assert.Equal(PersonalApiKeyCreateStatus.Created, (await first).Status);
        Assert.Equal(1, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
    }

    [Fact]
    public async Task ACreateNeedsAtLeastOneScope()
    {
        var (model, server, _, _) = await Ready(_ => Task.FromResult(Created()));
        var result = await model.CreateAsync("Backup", [], PersonalApiKeysModel.WholeAccount);
        Assert.Equal(PersonalApiKeyCreateStatus.Dropped, result.Status);
        Assert.Equal(0, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
    }

    // ---------- ownership of replies ----------

    [Fact]
    public async Task ACreateAnsweredAfterAProfileSwitchIsDroppedAndTheSecretNeverShown()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (model, server, client, _) = await Ready(_ => gate.Task);
        var pending = model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        await Until(() => server.Count(HttpMethod.Post, "/api/v2/api-keys") == 1);
        client.SetProfile("p2");
        gate.SetResult(Created());
        var result = await pending;
        Assert.Equal(PersonalApiKeyCreateStatus.Dropped, result.Status);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ACreateStartedAfterASwitchIsRefusedBeforeItIsSent()
    {
        var (model, server, client, _) = await Ready(_ => Task.FromResult(Created()));
        client.SetProfile("p2");
        var result = await model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        Assert.Equal(PersonalApiKeyCreateStatus.Dropped, result.Status);
        Assert.Equal(0, server.Count(HttpMethod.Post, "/api/v2/api-keys"));
    }

    [Fact]
    public async Task ALoadAnsweredAfterAnAccountOrProfileSwitchChangesNothing()
    {
        var (client, server, api) = Setup();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Handle = seen => seen.Path.EndsWith("/scopes") ? Task.FromResult(Reply(HttpStatusCode.OK, EnabledScopes)) : gate.Task;
        var model = NewModel(client, api);
        var changes = 0;
        model.Changed += () => changes++;
        var loading = model.LoadAsync(Primary);
        Assert.True(model.IsLoading);
        var before = changes;
        client.BeginAuthenticationSession("another-account");
        gate.SetResult(Reply(HttpStatusCode.OK, Page([Key("1")])));
        await loading;
        Assert.Empty(model.Rows);
        Assert.Equal(before, changes);
        Assert.Null(model.Capability);
    }

    [Fact]
    public async Task LeavingThePageDropsWhatIsStillInFlight()
    {
        var (client, server, api) = Setup();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Handle = seen => seen.Path.EndsWith("/scopes") ? Task.FromResult(Reply(HttpStatusCode.OK, EnabledScopes)) : gate.Task;
        var model = NewModel(client, api);
        var loading = model.LoadAsync(Primary);
        model.Deactivate();
        gate.TrySetResult(Reply(HttpStatusCode.OK, Page([Key("1")])));
        await loading;
        Assert.Empty(model.Rows);
        Assert.False(model.ListLoaded);
    }

    [Fact]
    public async Task ACreateInFlightWhenThePageIsLeftIsCancelledNotShown()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (model, server, _, _) = await Ready(_ => gate.Task);
        var pending = model.CreateAsync("Backup", ["watch:read"], PersonalApiKeysModel.WholeAccount);
        await Until(() => server.Count(HttpMethod.Post, "/api/v2/api-keys") == 1);
        model.Deactivate();
        gate.SetResult(Created());
        Assert.Equal(PersonalApiKeyCreateStatus.Dropped, (await pending).Status);
    }

    // ---------- revoking ----------

    [Fact]
    public async Task RevokeDeletesTheKeyThenReloadsTheList()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, read => read == 0 ? Page([Key("1"), Key("2")]) : Page([Key("2")]),
            seen => Task.FromResult(new HttpResponseMessage(seen.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.NotFound)));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.Equal(PersonalApiKeyRevokeStatus.Revoked, await model.RevokeAsync("1"));
        Assert.Equal(1, server.Count(HttpMethod.Delete, "/api/v2/api-keys/1"));
        await Until(() => model.Rows.Count == 1);
        Assert.Equal("2", model.Rows[0].Id);
    }

    [Fact]
    public async Task AFailedRevokeIsReportedAndTheListReloads()
    {
        var (client, server, api) = Setup();
        Serve(server, EnabledScopes, Page([Key("1")]), seen => Task.FromResult(seen.Method == HttpMethod.Delete
            ? Reply(HttpStatusCode.NotFound, "{}")
            : Reply(HttpStatusCode.OK, Page([Key("1")]))));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        Assert.Equal(PersonalApiKeyRevokeStatus.Failed, await model.RevokeAsync("1"));
        Assert.Equal(1, server.Count(HttpMethod.Delete, "/api/v2/api-keys/1"));
        await Until(() => server.Requests.Count(r => r.Method == HttpMethod.Get && r.Path == "/api/v2/api-keys") >= 2);
    }

    [Fact]
    public async Task ARevokeAnsweredAfterASwitchIsDropped()
    {
        var (client, server, api) = Setup();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Serve(server, EnabledScopes, Page([Key("1")]), seen => seen.Method == HttpMethod.Delete ? gate.Task : Task.FromResult(Reply(HttpStatusCode.OK, Page([Key("1")]))));
        var model = NewModel(client, api);
        await model.LoadAsync(Primary);
        var revoking = model.RevokeAsync("1");
        await Until(() => server.Count(HttpMethod.Delete, "/api/v2/api-keys/1") == 1);
        client.SetProfile("p2");
        gate.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        Assert.Equal(PersonalApiKeyRevokeStatus.Dropped, await revoking);
    }

    private static async Task Until(Func<bool> ready)
    {
        for (var i = 0; i < 200; i++)
        {
            if (ready()) return;
            await Task.Delay(10);
        }
        Assert.True(ready(), "The expected state was not reached.");
    }
}
