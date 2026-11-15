using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Wire contract of the invitation claim operations at official pin 7115ee70 (#4118): <c>token_in_body</c> servers receive
/// the token in a JSON body only; older servers keep the deprecated path operations. Tokens are synthetic placeholders.
/// </summary>
public sealed class InvitationLinkTransportTests
{
    private const string Token = "synthetic-invite_token-1";

    [Fact]
    public async Task BodyCapableServerReceivesTheTokenOnlyInRequestBodies()
    {
        var wire = new Wire(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/invitations/capabilities" => Fixture("invitation_capabilities"),
            "/api/v2/invitations/lookup" => Fixture("invitation_lookup"),
            "/api/v2/invitations/accept" => Fixture("invitation_accepted", HttpStatusCode.Created),
            _ => throw new InvalidOperationException("Unexpected request " + request.RequestUri.AbsolutePath),
        });
        var api = Api(wire);

        var (invitation, tokenInBody) = await api.LookupInvitationAsync(Token);
        Assert.True(tokenInBody);
        Assert.Equal("invitee@example.invalid", invitation.Email);
        Assert.False(invitation.EmailRequired);
        Assert.True(invitation.AcceptanceAvailable);
        var acceptance = await api.AcceptInvitationAsync(Token, "example-password", null, tokenInBody);
        Assert.Equal("signed_in", acceptance.LoginStatus);

        Assert.Equal(
            ["GET /api/v2/invitations/capabilities", "POST /api/v2/invitations/lookup", "POST /api/v2/invitations/accept"],
            wire.Requests.Select(request => request.Method + " " + request.Path));
        Assert.Equal(Token, wire.Requests[1].Json.GetProperty("token").GetString());
        var accept = wire.Requests[2].Json;
        Assert.Equal(Token, accept.GetProperty("token").GetString());
        Assert.Equal("example-password", accept.GetProperty("password").GetString());
        Assert.False(accept.TryGetProperty("email", out _));
        // No URL, path or query a proxy could log names the token.
        Assert.DoesNotContain(wire.Requests, request => request.Url.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinkInvitationSendsTheEnteredAddressWithTheTokenAndPassword()
    {
        var wire = new Wire(_ => Fixture("invitation_accepted", HttpStatusCode.Created));
        await Api(wire).AcceptInvitationAsync(Token, "example-password", "invitee@example.invalid", tokenInBody: true);
        var body = wire.Requests.Single().Json;
        Assert.Equal(["token", "email", "password"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal("invitee@example.invalid", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task SignInRequiredIsACommittedAcceptanceOnEitherTransport()
    {
        foreach (var tokenInBody in new[] { true, false })
        {
            var wire = new Wire(_ => Fixture("invitation_accepted_sign_in_required", HttpStatusCode.Created));
            var acceptance = await Api(wire).AcceptInvitationAsync(Token, "example-password", null, tokenInBody);
            Assert.Equal("sign_in_required", acceptance.LoginStatus);
            Assert.Null(acceptance.Tokens);
            Assert.Single(wire.Requests);
        }
    }

    [Theory]
    [InlineData("""{"revision":"1","state":"available","default_profile":true,"profileless":true}""")]
    [InlineData("""{"revision":"1","state":"available","default_profile":true,"profileless":true,"token_in_body":false}""")]
    public async Task ServerThatOmitsTokenInBodyKeepsTheDeprecatedPathOperations(string capabilities)
    {
        var wire = new Wire(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/invitations/capabilities" => Json(capabilities),
            "/api/v2/invitations/" + Token => Fixture("invitation_lookup"),
            "/api/v2/invitations/" + Token + "/accept" => Fixture("invitation_accepted", HttpStatusCode.Created),
            _ => throw new InvalidOperationException("Unexpected request " + request.RequestUri.AbsolutePath),
        });
        var api = Api(wire);
        var (_, tokenInBody) = await api.LookupInvitationAsync(Token);
        Assert.False(tokenInBody);
        await api.AcceptInvitationAsync(Token, "example-password", null, tokenInBody);

        Assert.Equal(
            ["GET /api/v2/invitations/capabilities", "GET /api/v2/invitations/" + Token, "POST /api/v2/invitations/" + Token + "/accept"],
            wire.Requests.Select(request => request.Method + " " + request.Path));
        // The path form has no token in its body.
        Assert.Equal(["password"], wire.Requests[2].Json.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task ServerWithoutACapabilityRouteIsOldAndUsesThePathOperations()
    {
        var wire = new Wire(request => request.RequestUri!.AbsolutePath == "/api/v2/invitations/capabilities"
            ? Json("""{"type":"https://siloserver.org/docs/api/v2/problems/not_found","title":"Not found","status":404}""", HttpStatusCode.NotFound)
            : Fixture("invitation_lookup"));
        var (_, tokenInBody) = await Api(wire).LookupInvitationAsync(Token);
        Assert.False(tokenInBody);
        Assert.Equal("/api/v2/invitations/" + Token, wire.Requests[1].Path);
    }

    [Fact]
    public async Task FailedCapabilityReadIsReportedNotSilentlyDowngradedToAUrlToken()
    {
        var wire = new Wire(_ => Json("""{"title":"Server error","status":500}""", HttpStatusCode.InternalServerError));
        var error = await Assert.ThrowsAsync<ApiException>(() => Api(wire).LookupInvitationAsync(Token));
        Assert.Equal(500, error.StatusCode);
        Assert.DoesNotContain(wire.Requests, request => request.Url.Contains(Token, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"state":"unavailable","token_in_body":true}""")]
    [InlineData("""{"state":"disabled","token_in_body":true}""")]
    public async Task UnavailableAcceptanceStopsBeforeAnyTokenIsSent(string capabilities)
    {
        var wire = new Wire(_ => Json(capabilities));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Api(wire).LookupInvitationAsync(Token));
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task DeadInvitationIs404NotAHintThatTheServerIsOld()
    {
        var wire = new Wire(request => request.RequestUri!.AbsolutePath == "/api/v2/invitations/capabilities"
            ? Fixture("invitation_capabilities")
            : Json(File.ReadAllText(FixturePath("invitation_lookup_not_found")), HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<ApiException>(() => Api(wire).LookupInvitationAsync(Token));
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("not_found", error.ProblemType);
        // The lookup was not repeated through the path form.
        Assert.Equal(["/api/v2/invitations/capabilities", "/api/v2/invitations/lookup"], wire.Requests.Select(request => request.Path));
    }

    [Fact]
    public async Task AcceptanceIsSentOnceAndNeverReplayedOnFailure()
    {
        var wire = new Wire(_ => Json("""{"type":"https://siloserver.org/docs/api/v2/problems/internal_error","title":"Internal error","status":500}""", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<ApiException>(() => Api(wire).AcceptInvitationAsync(Token, "example-password", null, tokenInBody: true));
        Assert.Single(wire.Requests.Where(request => request.Method == "POST" && request.Path.EndsWith("/accept", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{}", false)]
    [InlineData("{\"status\":\"accepted\"}", true)]
    [InlineData("{\"status\":\"accepted\",\"username\":\"\"}", false)]
    [InlineData("{\"status\":\"unknown\",\"username\":\"viewer\"}", true)]
    public async Task MalformedSuccessfulAcceptanceRequiresRecoveryWithoutReplay(string payload, bool tokenInBody)
    {
        var wire = new Wire(_ => Json(payload));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Api(wire).AcceptInvitationAsync(Token, "example-password", null, tokenInBody));
        Assert.Equal(InvitationAcceptFailure.Uncertain, InvitationAcceptOutcome.Classify(error));
        Assert.Single(wire.Requests);
    }

    [Theory]
    [InlineData("rate_limited", 429, InvitationAcceptFailure.RateLimited, true)]
    [InlineData("capability_unsupported", 501, InvitationAcceptFailure.ProfileUnavailable, true)]
    [InlineData("conflict", 409, InvitationAcceptFailure.EmailTaken, true)]
    [InlineData("validation_failed", 422, InvitationAcceptFailure.Validation, true)]
    [InlineData("weak_password", 422, InvitationAcceptFailure.Validation, true)]
    [InlineData("local_login_disabled", 403, InvitationAcceptFailure.LocalLoginDisabled, false)]
    [InlineData("not_found", 404, InvitationAcceptFailure.LinkUnavailable, false)]
    [InlineData("already_used", 409, InvitationAcceptFailure.LinkUnavailable, false)]
    // Anything else, a 5xx or an authentication failure included, may have created the account.
    [InlineData("internal_error", 500, InvitationAcceptFailure.Uncertain, false)]
    [InlineData("dependency_unavailable", 503, InvitationAcceptFailure.Uncertain, false)]
    [InlineData("invalid_token", 401, InvitationAcceptFailure.Uncertain, false)]
    [InlineData("unknown", 502, InvitationAcceptFailure.Uncertain, false)]
    public void OnlyDefiniteUncommittedRefusalsMayBeRetried(string problem, int status, InvitationAcceptFailure expected, bool retryable)
    {
        var failure = InvitationAcceptOutcome.Classify(new ApiException(problem, "synthetic", status) { ProblemType = problem });
        Assert.Equal(expected, failure);
        Assert.Equal(retryable, InvitationAcceptOutcome.IsRetryable(failure));
    }

    [Fact]
    public void LostResponsesAndTimeoutsAreUncertainNotRefusals()
    {
        foreach (Exception lost in new Exception[] { new HttpRequestException("connection reset"), new TaskCanceledException(), new TimeoutException(), new InvalidOperationException("incomplete") })
        {
            var failure = InvitationAcceptOutcome.Classify(lost);
            Assert.Equal(InvitationAcceptFailure.Uncertain, failure);
            Assert.False(InvitationAcceptOutcome.IsRetryable(failure));
        }
        // A 429 whose problem type was not parsed is still a refusal before commit.
        Assert.Equal(InvitationAcceptFailure.RateLimited, InvitationAcceptOutcome.Classify(new ApiException("unknown", "x", 429)));
    }

    private static AuthApi Api(Wire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire));
        client.SetBaseUrl("https://silo.example.test");
        return new AuthApi(client);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Fixture(string name, HttpStatusCode status = HttpStatusCode.OK)
        => Json(File.ReadAllText(FixturePath(name)), status);

    private static string FixturePath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Auth", name + ".json")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Auth", name + ".json");
    }

    private sealed record Seen(string Method, string Path, string Url, JsonElement Json);

    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            Assert.Null(request.Headers.Authorization);
            Requests.Add(new Seen(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.AbsoluteUri, JsonDocument.Parse(body.Length == 0 ? "{}" : body).RootElement.Clone()));
            return respond(request);
        }
    }
}
