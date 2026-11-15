using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class PasswordRecoveryTests
{
    // Synthetic placeholders only; never a real reset token.
    private const string Server = "https://example.test";
    private const string Lookup = """{"username":"viewer","server_name":"Fixture","expires_at":"2030-01-01T00:00:00Z"}""";
    private const string SignInRequired = """{"status":"completed","username":"viewer","login_status":"sign_in_required"}""";

    [Fact]
    public async Task UnconfirmedCompletionRequiresFreshLookupAndNeverReplays()
    {
        var writes = 0;
        var client = new SiloApiClient(new HttpClient(new Wire(request =>
        {
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri!.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal)) { writes++; throw new HttpRequestException("Disconnected after saving"); }
            return Json(HttpStatusCode.OK, Lookup);
        })));
        client.SetBaseUrl(Server);
        using var auth = new AuthService(client, new AuthApi(client));
        var flow = new PasswordRecovery(auth, client);
        await flow.LookupAsync("abc123");
        await Assert.ThrowsAsync<HttpRequestException>(() => flow.CompleteAsync("abc123", "new-password", "new-password"));
        Assert.True(flow.NeedsReload);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.CompleteAsync("abc123", "new-password", "new-password"));
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData("https://example.test/reset-password/abc_123", "https://example.test", "abc_123")]
    [InlineData("https://example.test/silo/reset-password/abc-123", "https://example.test/silo", "abc-123")]
    [InlineData("https://example.test/reset-password#abc_123", "https://example.test", "abc_123")]
    [InlineData("https://example.test/silo/reset-password#abc-123", "https://example.test/silo", "abc-123")]
    [InlineData("abc-123_XYZ", "https://example.test", "abc-123_XYZ")]
    public void LinkMustBelongToSelectedServer(string link, string server, string token)
        => Assert.Equal(token, PasswordRecovery.ParseToken(link, server));

    [Theory]
    [InlineData("https://another.test/reset-password/abc")]
    [InlineData("https://another.test/reset-password#abc")]
    [InlineData("https://example.test/reset-password/a/extra")]
    [InlineData("https://example.test/reset-password")]
    [InlineData("https://example.test/reset-password#")]
    [InlineData("https://user:password@example.test/reset-password/abc")]
    [InlineData("https://user:password@example.test/reset-password#abc")]
    public void RefusesOtherOriginsAndInvalidLinks(string link)
        => Assert.Throws<InvalidOperationException>(() => PasswordRecovery.ParseToken(link, "https://example.test"));

    [Fact]
    public async Task LookupAndCompletionSendTheTokenOnlyInRequestBodies()
    {
        var seen = new List<(string Method, string Url, string Body)>();
        using var auth = Create(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            seen.Add((request.Method.Method, url, request.Content?.ReadAsStringAsync().Result ?? ""));
            Assert.Null(request.Headers.Authorization);
            return Json(HttpStatusCode.OK, url.EndsWith("/lookup", StringComparison.Ordinal) ? Lookup : SignInRequired);
        }, out var client);
        var flow = new PasswordRecovery(auth, client);

        await flow.LookupAsync("https://example.test/reset-password#synthetic_token-1");
        var completion = await flow.CompleteAsync("https://example.test/reset-password#synthetic_token-1", "new-password", "new-password");

        Assert.Equal("sign_in_required", completion.LoginStatus);
        Assert.Collection(seen,
            lookup =>
            {
                Assert.Equal("POST", lookup.Method);
                Assert.Equal("https://example.test/api/v2/password-resets/lookup", lookup.Url);
                Assert.Equal("synthetic_token-1", JsonDocument.Parse(lookup.Body).RootElement.GetProperty("token").GetString());
            },
            complete =>
            {
                Assert.Equal("POST", complete.Method);
                Assert.Equal("https://example.test/api/v2/password-resets/complete", complete.Url);
                using var body = JsonDocument.Parse(complete.Body);
                Assert.Equal("synthetic_token-1", body.RootElement.GetProperty("token").GetString());
                Assert.Equal("new-password", body.RootElement.GetProperty("password").GetString());
            });
        // The bearer token stays out of every URL the server or a proxy in front of it can log.
        Assert.DoesNotContain(seen, request => request.Url.Contains("synthetic_token-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LegacyPathLinkIsStillAcceptedButRedeemedThroughTheBody()
    {
        var urls = new List<string>();
        using var auth = Create(request => { urls.Add(request.RequestUri!.AbsolutePath); return Json(HttpStatusCode.OK, Lookup); }, out var client);
        await new PasswordRecovery(auth, client).LookupAsync("https://example.test/reset-password/legacy-token");
        Assert.Equal(["/api/v2/password-resets/lookup"], urls);
    }

    [Fact]
    public async Task ReplacementLinkReplacesTheVerifiedToken()
    {
        var tokens = new List<string>();
        using var auth = Create(request =>
        {
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
            tokens.Add(body.RootElement.GetProperty("token").GetString()!);
            return Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal) ? Lookup : SignInRequired);
        }, out var client);
        var flow = new PasswordRecovery(auth, client);
        await flow.LookupAsync("https://example.test/reset-password#first-token");
        await flow.LookupAsync("https://example.test/reset-password#second-token");
        // A password cannot be saved against a link other than the one verified last.
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.CompleteAsync("https://example.test/reset-password#first-token", "new-password", "new-password"));
        await flow.CompleteAsync("https://example.test/reset-password#second-token", "new-password", "new-password");
        Assert.Equal(["first-token", "second-token", "second-token"], tokens);
    }

    [Fact]
    public async Task DeadLinkIsANotFoundProblemNotAnOldServer()
    {
        var methods = new List<string>();
        var fixture = File.ReadAllText(FixturePath("password_reset_lookup_not_found"));
        using var auth = Create(request => { methods.Add(request.Method.Method); return Json(HttpStatusCode.NotFound, fixture, "application/problem+json"); }, out var client);
        var error = await Assert.ThrowsAsync<ApiException>(() => new PasswordRecovery(auth, client).LookupAsync("dead-token"));
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("not_found", error.ProblemType);
        Assert.Equal(["POST"], methods);
    }

    [Fact]
    public async Task ServerWithoutBodyOperationsFallsBackToThePathFormsAndKeepsThatTransport()
    {
        var seen = new List<string>();
        using var auth = Create(request =>
        {
            seen.Add(request.Method.Method + " " + request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/api/v2/password-resets/lookup" => Json(HttpStatusCode.MethodNotAllowed, """{"type":"https://siloserver.org/docs/api/v2/problems/method_not_allowed","title":"Method not allowed","status":405}""", "application/problem+json"),
                "/api/v2/password-resets/old-token" => Json(HttpStatusCode.OK, Lookup),
                "/api/v2/password-resets/old-token/complete" => Json(HttpStatusCode.OK, SignInRequired),
                _ => throw new InvalidOperationException("Unexpected request " + request.RequestUri.AbsolutePath),
            };
        }, out var client);
        var flow = new PasswordRecovery(auth, client);
        await flow.LookupAsync("old-token");
        await flow.CompleteAsync("old-token", "new-password", "new-password");
        Assert.Equal(["POST /api/v2/password-resets/lookup", "GET /api/v2/password-resets/old-token", "POST /api/v2/password-resets/old-token/complete"], seen);
    }

    [Fact]
    public async Task CompletionIsNeverRetriedOnAnotherRouteAfterARefusal()
    {
        var writes = 0;
        using var auth = Create(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, Lookup);
            writes++;
            return Json(HttpStatusCode.MethodNotAllowed, """{"type":"https://siloserver.org/docs/api/v2/problems/method_not_allowed","title":"Method not allowed","status":405}""", "application/problem+json");
        }, out var client);
        var flow = new PasswordRecovery(auth, client);
        await flow.LookupAsync("body-token");
        await Assert.ThrowsAsync<ApiException>(() => flow.CompleteAsync("body-token", "new-password", "new-password"));
        Assert.Equal(1, writes);
        Assert.True(flow.NeedsReload);
    }

    [Fact]
    public async Task ExpiredLinkOnCompletionKeepsItsNotFoundStatusForTheUnavailablePage()
    {
        var fixture = File.ReadAllText(FixturePath("password_reset_completed_not_found"));
        using var auth = Create(request => request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, Lookup)
            : Json(HttpStatusCode.NotFound, fixture, "application/problem+json"), out var client);
        var flow = new PasswordRecovery(auth, client);
        await flow.LookupAsync("spent-token");
        var error = await Assert.ThrowsAsync<ApiException>(() => flow.CompleteAsync("spent-token", "new-password", "new-password"));
        Assert.Equal(404, error.StatusCode);
        Assert.True(flow.NeedsReload);
    }

    private static AuthService Create(Func<HttpRequestMessage, HttpResponseMessage> send, out SiloApiClient client)
    {
        client = new SiloApiClient(new HttpClient(new Wire(send)));
        client.SetBaseUrl(Server);
        return new AuthService(client, new AuthApi(client));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string mediaType = "application/json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static string FixturePath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Auth", name + ".json")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Auth", name + ".json");
    }

    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(send(r)); }
}
