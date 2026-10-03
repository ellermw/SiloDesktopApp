using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class ApiV2TransportTests
{
    [Theory]
    [InlineData("body.password")]
    [InlineData("body.directory_password")]
    public async Task ValidationProblemsPreserveTheFirstRejectedField(string location)
    {
        var client = Client(_ => new(HttpStatusCode.BadRequest) { Content = Json($$"""{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","detail":"Password rejected","errors":[{"location":"{{location}}","message":"Wrong password"},{"location":"body.username"}]}""") });
        var error = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<JsonElement>("/api/v2/account/identities/link-credentials", new { password = "fixture" }));
        Assert.Equal("validation_failed", error.ErrorCode);
        Assert.Equal("Password rejected", error.Message);
        Assert.Equal(location, error.ErrorLocation);
    }

    [Theory]
    [InlineData("installation_changed", 409)]
    [InlineData("profile_unverified", 403)]
    public async Task ProblemDetailsRetainsMachineCodeAndProfileVerification(string code, int status)
    {
        var client = Client(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = Json($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{code}}","title":"Refused","status":{{status}},"detail":"The current context changed"}""") });
        client.SetProfile("profile");
        ProfileVerificationContext? notified = null;
        client.SetProfileVerificationRequiredHandler(context => notified = context);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<JsonElement>("/api/v2/playback/capabilities"));
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal("The current context changed", error.Message);
        Assert.Equal(status == 403, notified.HasValue);
    }

    [Fact]
    public async Task JsonElementRequestPreservesExplicitWireTypesAndNulls()
    {
        string? sent = null;
        var client = Client(async request => { sent = await request.Content!.ReadAsStringAsync(); return new(HttpStatusCode.NoContent); });
        using var body = JsonDocument.Parse("""{"file_id":"42","pin":null,"sequence":7}""");
        await client.PostNoContentAsync("/api/v2/test", body.RootElement);
        using var actual = JsonDocument.Parse(sent!);
        Assert.Equal("42", actual.RootElement.GetProperty("file_id").GetString());
        Assert.Equal(JsonValueKind.Null, actual.RootElement.GetProperty("pin").ValueKind);
        Assert.Equal(7, actual.RootElement.GetProperty("sequence").GetInt64());
    }

    [Fact]
    public async Task StringEncodedNumericIdsCanBeReadWithoutWeakeningOutgoingTypes()
    {
        var client = Client(_ => new(HttpStatusCode.OK) { Content = Json("""{"id":"42"}""") });
        Assert.Equal(42, (await client.GetAsync<NumericItem>("/api/v2/test")).Id);
    }

    private sealed class NumericItem { public int Id { get; set; } }

    [Fact]
    public void StreamingDownloadUsesAllIdentityHeadersAndRejectsAnotherOrigin()
    {
        var client = Client(_ => new(HttpStatusCode.NoContent));
        client.SetAccessToken("access");
        client.SetProfile("profile", "proof");
        client.SetDeviceMetadata("device", "desktop", "windows");
        using var request = client.CreateAuthenticatedRequest(HttpMethod.Get, "/api/v2/downloads/one/file");
        Assert.Equal("access", request.Headers.Authorization!.Parameter);
        Assert.Equal("profile", request.Headers.GetValues("X-Profile-Id").Single());
        Assert.Equal("proof", request.Headers.GetValues("X-Profile-Token").Single());
        Assert.Equal("device", request.Headers.GetValues("X-Silo-Device-Id").Single());
        Assert.Throws<ArgumentException>(() => client.CreateAuthenticatedRequest(HttpMethod.Get, "https://other.invalid/file"));
    }

    [Fact]
    public async Task TokenRefreshPreservesRevisionAndRoomProofWithoutLeakingToOtherRequests()
    {
        var count = 0;
        var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/other"))
            {
                Assert.False(request.Headers.Contains("X-Room-Token"));
                Assert.Empty(request.Headers.IfMatch);
                return new(HttpStatusCode.NoContent);
            }
            Assert.Equal("proof", request.Headers.GetValues("X-Room-Token").Single());
            Assert.Equal("\"revision\"", request.Headers.IfMatch.Single().Tag);
            return new(++count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent);
        });
        client.SetAccessToken("old");
        client.SetTokenRefresher(_ => { client.SetAccessToken("new"); return Task.FromResult(true); });
        await client.SendNoContentRequestAsync(HttpMethod.Patch, "/api/v2/room", new { position = 2 },
            new Dictionary<string, string> { ["X-Room-Token"] = "proof", ["If-Match"] = "\"revision\"" });
        await client.DeleteAsync("/api/v2/other");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PaginationRejectsResultsFromAReplacedProfile()
    {
        SiloApiClient? client = null;
        client = Client(_ =>
        {
            client!.SetProfile("replacement");
            return new(HttpStatusCode.OK) { Content = Json("""{"items":[{"id":"42"}],"page":{"has_more":true,"next_cursor":"next"}}""") };
        });
        client.SetProfile("original");
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetAllItemsAsync<NumericItem>("/api/v2/items"));
    }

    [Fact]
    public async Task PaginationRejectsRepeatedCursorInsteadOfLooping()
    {
        var calls = 0;
        var client = Client(_ =>
        {
            calls++;
            return new(HttpStatusCode.OK) { Content = Json("""{"items":[],"page":{"has_more":true,"next_cursor":"same"}}""") };
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAllItemsAsync<NumericItem>("/api/v2/items"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConditionalWriteRetainsTheReturnedRevision()
    {
        var client = Client(request =>
        {
            Assert.Equal("\"old\"", request.Headers.IfMatch.Single().Tag);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{}") };
            response.Headers.ETag = new("\"new\"");
            return response;
        });
        var result = await client.PutWithETagResponseAsync<JsonElement>("/api/v2/config", new { theme = "dark" }, "\"old\"");
        Assert.Equal("\"new\"", result.ETag);
    }
    private static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");
    private static SiloApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> handle)
        => Client(request => Task.FromResult(handle(request)));
    private static SiloApiClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle)
    {
        var client = new SiloApiClient(new HttpClient(new Handler(handle)));
        client.SetBaseUrl("https://fixture.invalid");
        return client;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
}
