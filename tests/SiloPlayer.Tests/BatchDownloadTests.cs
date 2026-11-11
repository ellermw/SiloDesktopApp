using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Downloads;

namespace SiloPlayer.Tests;

public sealed class BatchDownloadTests
{
    [Fact]
    public async Task SeriesPageSendsTemplateFileIdAcrossCursorPages()
    {
        using var server = new Server(
            new Reply("download_batch_page1"),
            new Reply("download_batch_page2"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            FileId = 99,
            Quality = "original",
        };

        var result = await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        Assert.Equal(3, result.Downloads.Count);
        Assert.Single(result.Skipped);
        Assert.Equal("No downloadable file for this episode.", result.Skipped[0].Reason);

        Assert.Equal(2, server.Requests.Count);
        var body1 = JsonDocument.Parse(server.Requests[0].Body!);
        Assert.Equal("series-1", body1.RootElement.GetProperty("content_id").GetString());
        Assert.True(body1.RootElement.GetProperty("series").GetBoolean());
        Assert.Equal("99", body1.RootElement.GetProperty("media_file_id").GetString());
        Assert.False(body1.RootElement.TryGetProperty("season_number", out _));
        Assert.False(body1.RootElement.TryGetProperty("expected_revision", out _));
        Assert.False(body1.RootElement.TryGetProperty("expected_entries", out _));

        var req1Path = server.Requests[0].Uri.AbsolutePath;
        Assert.Equal("/api/v2/downloads", req1Path);

        var req2 = server.Requests[1];
        Assert.Equal("/api/v2/downloads", req2.Uri.AbsolutePath);
        Assert.Contains("cursor=", req2.Uri.Query);
        var body2 = JsonDocument.Parse(req2.Body!);
        Assert.Equal("99", body2.RootElement.GetProperty("media_file_id").GetString());
        Assert.True(body2.RootElement.GetProperty("series").GetBoolean());
        Assert.Equal("series-1", body2.RootElement.GetProperty("content_id").GetString());
    }

    [Fact]
    public async Task SeasonPageSendsSeasonNumberAndTemplate()
    {
        using var server = new Server(new Reply("download_batch_season"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            SeasonNumber = 2,
            FileId = 55,
            Quality = "10mbps",
        };

        var result = await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        var sent = server.Requests.Single();
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("series-1", body.RootElement.GetProperty("content_id").GetString());
        Assert.True(body.RootElement.GetProperty("series").GetBoolean());
        Assert.Equal(2, body.RootElement.GetProperty("season_number").GetInt32());
        Assert.Equal("55", body.RootElement.GetProperty("media_file_id").GetString());
        Assert.Equal("10mbps", body.RootElement.GetProperty("quality").GetString());
        Assert.True(body.RootElement.TryGetProperty("batch_id", out var batchNode));
        Assert.False(string.IsNullOrEmpty(batchNode.GetString()));
        Assert.False(body.RootElement.TryGetProperty("expected_revision", out _));
    }

    [Fact]
    public async Task SpecialsPageSendsSeasonNumberZero()
    {
        using var server = new Server(new Reply("download_batch_specials"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            SeasonNumber = 0,
            Quality = "original",
        };

        var result = await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        var sent = server.Requests.Single();
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal(0, body.RootElement.GetProperty("season_number").GetInt32());
        Assert.True(body.RootElement.GetProperty("series").GetBoolean());
        Assert.Equal("dl-special-1", Assert.Single(result.Downloads).Id);
    }

    [Fact]
    public async Task OldServerOmitsTemplateFileId()
    {
        using var server = new Server(new Reply("download_batch_specials"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
        };

        var result = await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        var sent = server.Requests.Single();
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.False(body.RootElement.TryGetProperty("media_file_id", out _));
        Assert.False(body.RootElement.TryGetProperty("expected_revision", out _));
    }

    [Fact]
    public async Task BatchIdIsStableAcrossPages()
    {
        using var server = new Server(
            new Reply("download_batch_page1"),
            new Reply("download_batch_page2"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
        };

        await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        Assert.Equal(2, server.Requests.Count);
        using var body1 = JsonDocument.Parse(server.Requests[0].Body!);
        using var body2 = JsonDocument.Parse(server.Requests[1].Body!);
        var batchId1 = body1.RootElement.GetProperty("batch_id").GetString();
        var batchId2 = body2.RootElement.GetProperty("batch_id").GetString();
        Assert.False(string.IsNullOrEmpty(batchId1));
        Assert.Equal(batchId1, batchId2);
    }

    [Fact]
    public async Task SeriesPageNeverSendsExpectedRevision()
    {
        using var server = new Server(new Reply("download_batch_specials"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
        };

        await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        using var body = JsonDocument.Parse(server.Requests.Single().Body!);
        Assert.False(body.RootElement.TryGetProperty("expected_revision", out _));
        Assert.False(body.RootElement.TryGetProperty("expected_download_id", out _));
    }

    [Fact]
    public async Task SingleDownloadCanStillSendExpectedRevisionZero()
    {
        using var server = new Server(new Reply("download_create_single"));

        var request = new DownloadRequest
        {
            ContentId = "movie",
            FileId = 42,
            ExpectedRevision = 0,
        };

        await new DownloadsApi(server.Client).CreateDownloadAsync(request);

        using var body = JsonDocument.Parse(server.Requests.Single().Body!);
        Assert.Equal(0, body.RootElement.GetProperty("expected_revision").GetInt32());
    }

    [Fact]
    public async Task ExpectedEntriesSerializesCorrectly()
    {
        using var server = new Server(new Reply("download_batch_specials"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
            ExpectedEntries = new Dictionary<string, DownloadEntryExpectation>
            {
                ["ep-1"] = new() { Id = "dl-ep1", Revision = 3 },
                ["ep-2"] = new() { Revision = 0 },
            },
        };

        await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        using var body = JsonDocument.Parse(server.Requests.Single().Body!);
        var entries = body.RootElement.GetProperty("expected_entries");
        Assert.Equal(JsonValueKind.Object, entries.ValueKind);
        var ep1 = entries.GetProperty("ep-1");
        Assert.Equal("dl-ep1", ep1.GetProperty("id").GetString());
        Assert.Equal(3, ep1.GetProperty("revision").GetInt32());
        var ep2 = entries.GetProperty("ep-2");
        Assert.Equal(0, ep2.GetProperty("revision").GetInt32());
    }

    [Fact]
    public void CapabilityDeserializesBatchVersionField()
    {
        var json = Server.ReadFixture("download_capability_batch");
        var cap = JsonSerializer.Deserialize<DownloadCapability>(json, V2Json.Options)!;

        Assert.True(cap.BatchVersion);
        Assert.True(cap.BulkQuality);
        Assert.True(cap.SeasonDownload);
        Assert.Equal(3, cap.QualityPresets.Count);
        Assert.Contains("10mbps", cap.QualityPresets);
    }

    [Fact]
    public void OldServerCapabilityOmitsBatchVersionField()
    {
        var json = Server.ReadFixture("download_capability_no_batch");
        var cap = JsonSerializer.Deserialize<DownloadCapability>(json, V2Json.Options)!;

        Assert.False(cap.BatchVersion);
        Assert.False(cap.BulkQuality);
        Assert.True(cap.SeasonDownload);
    }

    [Fact]
    public async Task ForeignTemplateFileReturns404()
    {
        using var server = new Server(new Reply(Status: HttpStatusCode.NotFound,
            Json: """{"type":"not_found","title":"Not found","detail":"The requested file does not belong to this series."}"""));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            FileId = 999,
            Quality = "original",
        };

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => new DownloadsApi(server.Client).CreateDownloadsAsync(request));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task RefusedFirstPageReportsNoAcceptedPage()
    {
        using var server = new Server(new Reply(Status: HttpStatusCode.NotFound,
            Json: """{"type":"not_found","title":"Not found","detail":"The requested file does not belong to this series."}"""));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            FileId = 999,
            Quality = "original",
        };
        var accepted = 0;

        await Assert.ThrowsAsync<ApiException>(
            () => new DownloadsApi(server.Client).CreateDownloadsAsync(request, pageAccepted: () => accepted++));
        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task FailureAfterAnAcceptedPageReportsThatPage()
    {
        using var server = new Server(
            new Reply("download_batch_page1"),
            new Reply(Status: HttpStatusCode.NotFound, Json: """{"type":"not_found","title":"Not found"}"""));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
        };
        var accepted = 0;

        await Assert.ThrowsAsync<ApiException>(
            () => new DownloadsApi(server.Client).CreateDownloadsAsync(request, pageAccepted: () => accepted++));
        Assert.Equal(1, accepted);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task ContextChangeAbortsMidPage()
    {
        using var server = new ContextSwitchServer();
        server.Client.SetProfile("profile-a");

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new DownloadsApi(server.Client).CreateDownloadsAsync(request));
        Assert.Equal(1, server.RequestCount);
    }

    private sealed class ContextSwitchServer : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public SiloApiClient Client { get; }
        public int RequestCount { get; private set; }
        public ContextSwitchServer()
        {
            _http = new HttpClient(this, disposeHandler: false);
            Client = new(_http);
            Client.SetBaseUrl("https://fixture.invalid");
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            Client.SetProfile("different-profile");
            var json = Server.ReadFixture("download_batch_page1");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }

    [Fact]
    public async Task AllSeriesOmitsSeasonNumber()
    {
        using var server = new Server(new Reply("download_batch_specials"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "original",
        };

        await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        using var body = JsonDocument.Parse(server.Requests.Single().Body!);
        Assert.False(body.RootElement.TryGetProperty("season_number", out _));
    }

    [Fact]
    public async Task QualityIsSentOnBatchPage()
    {
        using var server = new Server(new Reply("download_batch_specials"));

        var request = new DownloadRequest
        {
            ContentId = "series-1",
            Series = true,
            Quality = "5mbps",
        };

        await new DownloadsApi(server.Client).CreateDownloadsAsync(request);

        using var body = JsonDocument.Parse(server.Requests.Single().Body!);
        Assert.Equal("5mbps", body.RootElement.GetProperty("quality").GetString());
    }

    [Fact]
    public void SkippedDownloadDeserializesCorrectly()
    {
        var json = """
        {
          "items": [],
          "skipped": [
            {
              "content_id": "series-1",
              "episode_id": "ep-5",
              "reason": "quality_unavailable"
            }
          ],
          "page": { "has_more": false }
        }
        """;

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var page = JsonSerializer.Deserialize<BatchPage>(json, options)!;
        Assert.Empty(page.Items);
        var skip = Assert.Single(page.Skipped);
        Assert.Equal("ep-5", skip.EpisodeId);
        Assert.Equal("quality_unavailable", skip.Reason);
    }

    [Fact]
    public void DownloadRequestFileIdUsesMediaFileIdPropertyName()
    {
        var request = new DownloadRequest { ContentId = "s", FileId = 42, Series = true };
        var body = V2Json.Body(request);
        var json = body.ToString();
        Assert.Contains("\"media_file_id\"", json);
        Assert.DoesNotContain("\"file_id\"", json);
    }

    [Fact]
    public void NullExpectedRevisionIsOmittedFromJson()
    {
        var request = new DownloadRequest { ContentId = "s", Series = true };
        var body = V2Json.Body(request);
        var json = body.ToString();
        Assert.DoesNotContain("expected_revision", json);
        Assert.DoesNotContain("expected_download_id", json);
        Assert.DoesNotContain("expected_entries", json);
    }

    [Fact]
    public void ExplicitExpectedRevisionZeroIsSerialized()
    {
        var request = new DownloadRequest { ContentId = "m", FileId = 1, ExpectedRevision = 0 };
        var body = V2Json.Body(request);
        using var doc = JsonDocument.Parse(body.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("expected_revision").GetInt32());
    }

    private sealed class BatchPage
    {
        public List<Download> Items { get; set; } = [];
        public List<SkippedDownload> Skipped { get; set; } = [];
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
            var json = reply.Fixture == null ? reply.Json ?? "{}" : ReadFixture(reply.Fixture);
            var response = new HttpResponseMessage(reply.Status) { Content = new StringContent(json) };
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
