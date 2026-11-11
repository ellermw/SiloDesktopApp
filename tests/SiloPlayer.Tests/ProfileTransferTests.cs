using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Tests;

public sealed class ProfileTransferTests
{
    private const string Available = """{"state":"available","format":"silo-profile-export","version":1,"max_import_bytes":67108864,"data_types":["watch_history","ratings","favorites","collections","dismissals","preferences","notifications"]}""";
    private const string Unavailable = """{"state":"not_configured","format":"","version":0,"max_import_bytes":0,"data_types":[]}""";

    private const string ExportDocument = """{"format":"silo-profile-export","version":1,"server_id":"test-server","exported_at":"2026-11-11T00:00:00Z","profile_name":"Test","data":{"watch_history":[]},"items":[]}""";

    private const string ImportResult = """{"same_server":true,"data":[{"type":"watch_history","added":5,"updated":2,"unchanged":10,"left_out":1,"notes":["Some note"]},{"type":"ratings","added":3,"updated":0,"unchanged":7,"left_out":0,"notes":[]}],"unmatched":["Unknown Movie"],"hidden":[]}""";

    [Fact]
    public async Task CapabilityDeserializesAllFields()
    {
        var (api, context, _) = Create(Available);
        var cap = await api.GetCapabilityAsync(context);
        Assert.Equal("available", cap.State);
        Assert.Equal("silo-profile-export", cap.Format);
        Assert.Equal(1, cap.Version);
        Assert.Equal(67108864, cap.MaxImportBytes);
        Assert.Equal(7, cap.DataTypes.Count);
        Assert.Equal("watch_history", cap.DataTypes[0]);
        Assert.Equal("notifications", cap.DataTypes[6]);
    }

    [Fact]
    public async Task UnavailableCapabilityReportsState()
    {
        var (api, context, _) = Create(Unavailable);
        var cap = await api.GetCapabilityAsync(context);
        Assert.Equal("not_configured", cap.State);
        Assert.Empty(cap.DataTypes);
    }

    [Fact]
    public async Task ExportReturnsRawJsonBytes()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ExportDocument);
        var (api, context) = CreateWithHandler(handler);
        var bytes = await api.ExportAsync(context);
        Assert.Contains("silo-profile-export", Encoding.UTF8.GetString(bytes));
        Assert.Equal("GET /api/v2/profile/export", handler.Requests[0]);
    }

    [Fact]
    public async Task ImportDeserializesResultWithCountsAndNotes()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        var result = await api.ImportAsync(context, doc, 67108864);
        Assert.True(result.SameServer);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal("watch_history", result.Data[0].Type);
        Assert.Equal(5, result.Data[0].Added);
        Assert.Equal(2, result.Data[0].Updated);
        Assert.Equal(10, result.Data[0].Unchanged);
        Assert.Equal(1, result.Data[0].LeftOut);
        Assert.Single(result.Data[0].Notes);
        Assert.Equal("ratings", result.Data[1].Type);
        Assert.Single(result.Unmatched);
        Assert.Equal("Unknown Movie", result.Unmatched[0]);
        Assert.Empty(result.Hidden);
        Assert.Contains("POST /api/v2/profile/import", handler.Requests[0]);
    }

    [Fact]
    public async Task ImportRejectsOversizedDocument()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ImportAsync(context, doc, 10));
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("too large", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("password_hash")]
    [InlineData("pin_hash")]
    [InlineData("allowed_library_ids")]
    [InlineData("is_primary")]
    [InlineData("library_restrictions_enabled")]
    [InlineData("role")]
    public async Task ImportRejectsDocumentWithAccessFields(string field)
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes($"{{\"{field}\":\"hack\",\"format\":\"silo-profile-export\"}}");
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ImportAsync(context, doc, 67108864));
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("access fields", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ImportRejectsInvalidJson()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes("not json at all");
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ImportAsync(context, doc, 67108864));
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public async Task ImportRejectsNonObjectJson()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes("[1,2,3]");
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ImportAsync(context, doc, 67108864));
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task ImportRejectsWrongFormat()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes("""{"format":"wrong-format","version":1}""");
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ImportAsync(context, doc, 67108864));
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("unrecognized format", ex.Message);
    }

    [Fact]
    public async Task ExportRejectsStaleContext()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ExportDocument);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example");
        client.SetProfile("p1");
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        client.SetProfile("p2");
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.ExportAsync(context));
    }

    [Fact]
    public async Task ImportRejectsStaleContext()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example");
        client.SetProfile("p1");
        var api = new ProfileTransferApi(client);
        var context = api.CaptureContext();
        client.SetProfile("p2");
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.ImportAsync(context, doc, 67108864));
    }

    [Fact]
    public async Task ImportZeroMaxBytesAllowsAnySize()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(ImportResult);
        var (api, context) = CreateWithHandler(handler);
        var doc = Encoding.UTF8.GetBytes(ExportDocument);
        var result = await api.ImportAsync(context, doc, 0);
        Assert.NotNull(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CapabilityRequestIsProfileScoped()
    {
        var handler = new Handler();
        handler.Responses.Enqueue(Available);
        var (api, context) = CreateWithHandler(handler);
        await api.GetCapabilityAsync(context);
        Assert.Equal("GET /api/v2/capabilities/profile-export", Assert.Single(handler.Requests));
    }

    private static (ProfileTransferApi, ApiRequestContext, Handler) Create(string capabilityJson)
    {
        var handler = new Handler();
        handler.Responses.Enqueue(capabilityJson);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example");
        client.SetProfile("p1");
        return (new ProfileTransferApi(client), client.CaptureContext(), handler);
    }

    private static (ProfileTransferApi, ApiRequestContext) CreateWithHandler(Handler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example");
        client.SetProfile("p1");
        return (new ProfileTransferApi(client), client.CaptureContext());
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Queue<string> Responses { get; } = new();
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var json = Responses.Count > 0 ? Responses.Dequeue() : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
