using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Tests;

// Official Silo fixtures pinned to c80c5169f8e58f354fba35551e0c2bbcabb70b8a.
public sealed class AuthV2ContractTests
{
    [Fact]
    public async Task LoginReadsStringAccountIdAndOmitsAbsentProvider()
    {
        var (api, handler) = Create("login_ok");
        var login = await api.LoginAsync("laura", "example-password");
        Assert.Equal("/api/v2/auth/login", handler.Path);
        Assert.Equal("1", login.User.Id.ToString());
        Assert.DoesNotContain("provider", handler.Body);
    }

    [Fact]
    public async Task ProfilesAndProvidersReadCollectionEnvelopesAndStringIds()
    {
        var (api, _) = Create("list_profiles_ok");
        var profiles = await api.GetProfilesAsync();
        Assert.Equal(3, Assert.Single(Assert.Single(profiles.Profiles).AllowedLibraryIds!));
        var (providersApi, _) = Create("list_auth_providers_ok");
        var providers = await providersApi.GetAuthProvidersAsync();
        Assert.Equal(3, providers[1].InstallationId);
    }

    [Fact]
    public async Task ProfileEditUsesPatchAndExplicitNullToRemovePin()
    {
        var (api, handler) = Create("update_profile_ok");
        await api.UpdateProfileAsync("p-owner", "Laura", "");
        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal("/api/v2/profiles/p-owner", handler.Path);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("pin").ValueKind);
    }

    [Fact]
    public async Task DevicePollReadsNestedTokens()
    {
        var (api, handler) = Create("poll_device_login_ok");
        var poll = await api.DevicePollAsync("device-fixture");
        Assert.Equal("/api/v2/auth/device/poll", handler.Path);
        Assert.Equal("acc", poll.AccessToken);
        Assert.Equal("laura", poll.User?.Username);
    }

    [Fact]
    public async Task CurrentAccountAndSetupUseTheirV2Domains()
    {
        var (api, handler) = Create("get_current_user_ok");
        await api.GetMeAsync();
        Assert.Equal("/api/v2/account/me", handler.Path);
        var (setup, setupHandler) = Create("get_setup_status_ok");
        await setup.GetSetupStatusAsync();
        Assert.Equal("/api/v2/system/setup", setupHandler.Path);
    }

    [Fact]
    public async Task ProfileCreationOmitsUnsetQualityAndWritesLibraryIdsAsStrings()
    {
        var (api, handler) = Create("update_profile_ok");
        await api.CreateProfileAsync(new CreateProfileRequest { Name = "Laura", AllowedLibraryIds = [3] });
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("3", body.RootElement.GetProperty("allowed_library_ids")[0].GetString());
        Assert.False(body.RootElement.TryGetProperty("max_playback_quality", out _));
        Assert.False(body.RootElement.TryGetProperty("pin", out _));

        await api.UpdateProfileAsync("p-owner", new CreateProfileRequest { Name = "Laura", Pin = "" });
        using var update = JsonDocument.Parse(handler.Body);
        Assert.Equal(JsonValueKind.Null, update.RootElement.GetProperty("max_playback_quality").ValueKind);
        Assert.Equal(JsonValueKind.Null, update.RootElement.GetProperty("pin").ValueKind);
    }

    [Theory]
    [InlineData("verify_profile_pin_ok", true)]
    [InlineData("verify_profile_pin_wrong", false)]
    public async Task PinVerificationAcceptsNullableExpiry(string fixture, bool valid)
    {
        var (api, handler) = Create(fixture);
        Assert.Equal(valid, (await api.VerifyPinAsync("p-owner", "1234")).Valid);
        Assert.Equal("/api/v2/profiles/p-owner/verify-pin", handler.Path);
    }

    [Fact]
    public async Task HouseholdSessionsAdaptV2ResourceIdAndNumericStringFields()
    {
        var (api, handler) = Create("list_household_sessions_ok");
        var sessions = await api.GetHouseholdSessionsAsync();
        Assert.Equal(2, sessions.Count);
        var session = sessions[0];
        Assert.False(string.IsNullOrEmpty(session.SessionId));
        Assert.True(session.UserId > 0);
        Assert.True(session.MediaFileId > 0);
        Assert.Equal("/api/v2/profiles/household/sessions", handler.Path);
    }

    [Theory]
    [InlineData("invitation_accepted", "signed_in", true)]
    [InlineData("invitation_accepted_sign_in_required", "sign_in_required", false)]
    public async Task InvitationAcceptanceSeparatesAccountCreationFromTokenIssuance(string fixture, string status, bool hasTokens)
    {
        var (api, handler) = Create(fixture);
        var acceptance = await api.AcceptInvitationAsync("fixture-invitation", "example-password");
        Assert.Equal(status, acceptance.LoginStatus);
        Assert.Equal(hasTokens, acceptance.Tokens != null);
        Assert.Equal("/api/v2/invitations/fixture-invitation/accept", handler.Path);
    }

    [Fact]
    public async Task SessionListFollowsEscapedOpaqueCursor()
    {
        var responses = new Queue<string>([
            """{"items":[{"id":"session-1"}],"page":{"has_more":true,"next_cursor":"a+b/=cursor"}}""",
            """{"items":[{"id":"session-2"}],"page":{"has_more":false}}""",
        ]);
        var paths = new List<string>();
        var client = new SiloApiClient(new HttpClient(new SessionHandler(responses, paths)));
        client.SetBaseUrl("https://silo.example.test");
        var sessions = await new AuthApi(client).GetSessionsAsync();
        Assert.Equal(new[] { "session-1", "session-2" }, sessions.Sessions.Select(s => s.Id));
        Assert.Equal("/api/v2/auth/sessions?limit=100&cursor=a%2Bb%2F%3Dcursor", paths[1]);
    }

    private sealed class SessionHandler(Queue<string> responses, List<string> paths) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses.Dequeue()) });
        }
    }

    private static (AuthApi, FixtureHandler) Create(string fixture)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Auth", fixture + ".json")))
            directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Auth", fixture + ".json");
        var handler = new FixtureHandler(File.ReadAllText(path));
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example.test");
        return (new AuthApi(client), handler);
    }

    private sealed class FixtureHandler(string response) : HttpMessageHandler
    {
        public string Path { get; private set; } = "";
        public string Body { get; private set; } = "";
        public HttpMethod? Method { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Method = request.Method;
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
