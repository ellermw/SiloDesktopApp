using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Downloads;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Tests;

public sealed class PersonalV2ContractTests
{
    [Fact]
    public async Task DownloadsCreationUsesGuardStringFileIdAndItemsEnvelope()
    {
        using var server = new Server(new Reply("download_create_single"));
        var download = await new DownloadsApi(server.Client).CreateDownloadAsync(new DownloadRequest { ContentId = "movie", FileId = 42 });
        Assert.Equal("entry", download.Id);
        Assert.Equal(42, download.MediaFileId);
        var sent = server.Requests.Single();
        Assert.Equal("/api/v2/downloads", sent.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("42", body.RootElement.GetProperty("media_file_id").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("expected_revision").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("file_id", out _));
        Assert.Equal("/api/v2/downloads/entry/file-proxy", DownloadsApi.GetDownloadFilePath("entry"));
        Assert.Equal("/api/v2/direct-download-proxy?file_id=42", DownloadsApi.GetDirectDownloadPath(42));
    }

    [Fact]
    public async Task DownloadsAndCapabilityReadCanonicalRoutes()
    {
        using var server = new Server(new Reply("downloads_empty"), new Reply("download_capability"));
        var api = new DownloadsApi(server.Client);
        Assert.Empty((await api.GetDownloadsAsync()).Downloads);
        await api.GetCapabilityAsync();
        Assert.Equal("/api/v2/capabilities/downloads", server.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task RequestCreationKeepsExternalNumericIdsAndOmitsNullFields()
    {
        using var server = new Server(new Reply("create_request_ok"));
        var request = await new RequestsApi(server.Client).CreateAsync(new CreateMediaRequestInput { MediaType = "movie", TmdbId = 949, Title = "Heat" });
        Assert.NotEmpty(request.Id);
        Assert.Equal("/api/v2/requests", server.Requests[0].Uri.AbsolutePath);
        using var body = JsonDocument.Parse(server.Requests[0].Body!);
        Assert.Equal(949, body.RootElement.GetProperty("tmdb_id").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("tvdb_id", out _));
    }

    [Fact]
    public async Task EbookProgressRetainsEventTimeOnRetryAndSendsStringFileId()
    {
        using var server = new Server(new Reply("ebook_progress_saved"), new Reply("ebook_progress_saved"));
        var api = new EbooksApi(server.Client);
        var input = new EbookReaderProgressInput { FileId = 42, Location = "chapter1", Progress = .25 };
        await api.SaveProgressAsync("book", input);
        await api.SaveProgressAsync("book", input);
        Assert.Equal(server.Requests[0].Body, server.Requests[1].Body);
        using var body = JsonDocument.Parse(server.Requests[0].Body!);
        Assert.Equal("42", body.RootElement.GetProperty("file_id").GetString());
        Assert.Equal(input.UpdatedAt, body.RootElement.GetProperty("updated_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task EbookConfigRetainsTheMutationEtagAndRejectsCrossProfileReuse()
    {
        using var server = new Server(new Reply("ebook_config_default", ETag: "\"one\""),
            new Reply("ebook_config_saved", ETag: "\"two\""), new Reply("ebook_config_saved", ETag: "\"three\""));
        var api = new EbooksApi(server.Client);
        await api.GetReaderConfigAsync("book");
        await api.SaveReaderConfigAsync("book", new() { ["theme"] = "dark" });
        await api.SaveReaderConfigAsync("book", new() { ["theme"] = "light" });
        Assert.Equal("\"one\"", server.Requests[1].IfMatch);
        Assert.Equal("\"two\"", server.Requests[2].IfMatch);
        server.Client.SetProfile("different");
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.SaveReaderConfigAsync("book", new()));
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task AnnotationIdentityIsStableAndDeleteUsesReturnedValidator()
    {
        using var server = new Server(new Reply("ebook_annotation_created"), new Reply("ebook_annotation_created"), new Reply(Json: "", Status: HttpStatusCode.NoContent));
        var api = new EbooksApi(server.Client);
        var input = new EbookReaderAnnotationInput { Id = "intent", Kind = "note", Note = "saved", Location = "chapter1" };
        var annotation = await api.CreateAnnotationAsync("book", input);
        await api.CreateAnnotationAsync("book", input);
        await api.DeleteAnnotationAsync("book", annotation.Id);
        Assert.Equal(server.Requests[0].Body, server.Requests[1].Body);
        Assert.Equal(annotation.ETag, server.Requests[2].IfMatch);
        Assert.Equal(HttpMethod.Delete, server.Requests[2].Method);
    }

    [Fact]
    public async Task AnnotationPagesAreFullyReadBeforeReturning()
    {
        var annotation = Server.ReadFixture("ebook_annotation_created");
        using var server = new Server(new Reply(Json: "{\"items\":[" + annotation + "],\"page\":{\"has_more\":true,\"next_cursor\":\"signed\"}}"),
            new Reply(Json: "{\"items\":[],\"page\":{\"has_more\":false}}"));
        var annotations = await new EbooksApi(server.Client).GetAnnotationsAsync("book");
        Assert.Single(annotations);
        Assert.Contains("cursor=signed", server.Requests[1].Uri.Query);
        Assert.All(server.Requests, request => Assert.Contains("limit=50", request.Uri.Query));
    }

    [Fact]
    public async Task ReadAllUsesOriginalInboxCutoffAndBlocksProfileChanges()
    {
        using var server = new Server(new Reply("notification_list_notifications"), new Reply(Json: "", Status: HttpStatusCode.NoContent));
        var api = new NotificationsApi(server.Client);
        var inbox = await api.GetNotificationsAsync();
        Assert.Single(inbox.Notifications);
        Assert.NotEmpty(inbox.NextCursor!);
        await api.MarkAllReadAsync();
        using var body = JsonDocument.Parse(server.Requests[1].Body!);
        Assert.Equal(inbox.ReadCutoff, body.RootElement.GetProperty("through").GetString());
        server.Client.SetProfile("other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.MarkAllReadAsync());
    }

    [Fact]
    public async Task WebhookDeleteUsesObservedRowEtagWithoutRefetch()
    {
        using var server = new Server(new Reply("notification_webhooks"), new Reply(Json: "", Status: HttpStatusCode.NoContent));
        var api = new NotificationsApi(server.Client);
        var hooks = await api.GetWebhooksAsync();
        await api.DeleteWebhookAsync(hooks[0].Id);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(hooks[0].ETag, server.Requests[1].IfMatch);
    }

    [Fact]
    public async Task PreferenceUpdateDoesNotSendResponseOnlyProfileId()
    {
        using var server = new Server(new Reply(Json: "{}"));
        await new NotificationsApi(server.Client).UpdatePreferencesAsync(new NotificationPreferences { ProfileId = "profile", Enabled = true });
        using var body = JsonDocument.Parse(server.Requests[0].Body!);
        Assert.False(body.RootElement.TryGetProperty("profile_id", out _));
        Assert.True(body.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task EmailVerificationRetainsIntentAfterUncertainTransportFailure()
    {
        using var server = new Server(new Reply(Failure: true), new Reply(Json: "{\"current\":true}"), new Reply("notification_email_preferences"));
        var api = new NotificationsApi(server.Client);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.RequestEmailAddressAsync("fixture@example.test"));
        await api.RequestEmailAddressAsync("fixture@example.test");
        using var first = JsonDocument.Parse(server.Requests[0].Body!);
        using var second = JsonDocument.Parse(server.Requests[1].Body!);
        Assert.Equal(first.RootElement.GetProperty("verification_id").GetString(), second.RootElement.GetProperty("verification_id").GetString());
        Assert.Equal(HttpMethod.Get, server.Requests[2].Method);
    }

    private sealed record Reply(string? Fixture = null, string? Json = null, string? ETag = null, HttpStatusCode Status = HttpStatusCode.OK, bool Failure = false);
    private sealed record Sent(Uri Uri, HttpMethod Method, string? Body, string? IfMatch);
    private sealed class Server : HttpMessageHandler
    {
        private readonly Queue<Reply> _replies;
        private readonly HttpClient _http;
        public SiloApiClient Client { get; }
        public List<Sent> Requests { get; } = [];
        public Server(params Reply[] replies)
        {
            _replies = new(replies);
            _http = new HttpClient(this, disposeHandler: false);
            Client = new(_http);
            Client.SetBaseUrl("https://fixture.invalid");
            Client.SetProfile("profile");
        }
        public static string ReadFixture(string name)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "PersonalV2"))) root = root.Parent;
            return File.ReadAllText(Path.Combine(root!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "PersonalV2", name + ".json"));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.RequestUri!, request.Method, request.Content == null ? null : await request.Content.ReadAsStringAsync(ct), request.Headers.IfMatch.FirstOrDefault()?.ToString()));
            var reply = _replies.Dequeue();
            if (reply.Failure) throw new HttpRequestException("Fixture transport uncertainty");
            var response = new HttpResponseMessage(reply.Status) { Content = new StringContent(reply.Fixture == null ? reply.Json ?? "{}" : ReadFixture(reply.Fixture)) };
            if (reply.ETag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(reply.ETag);
            return response;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
