using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EbookReaderInteropTests
{
    [Fact]
    public async Task ProgressRoundTripsExactCfiAndStringFileIdWithEventTime()
    {
        const string cfi = "epubcfi(/6/4!/4/102[p50],/1:13,/1:35)";
        var timestamp = DateTimeOffset.Parse("2026-09-28T01:02:03Z");
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/v2/ebooks/book-1/progress", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("42", body.RootElement.GetProperty("file_id").GetString());
            Assert.Equal(cfi, body.RootElement.GetProperty("location").GetString());
            Assert.Equal(.51, body.RootElement.GetProperty("progress").GetDouble());
            Assert.Equal(timestamp, body.RootElement.GetProperty("updated_at").GetDateTimeOffset());
            return Json("""{"progress":{"file_id":"42","location":"epubcfi(/6/4!/4/102[p50],/1:13,/1:35)","progress":0.51}}""");
        });
        var client = Client(handler);
        var api = new EbooksApi(client);
        var result = await api.SaveProgressAsync("book-1", new EbookReaderProgressInput { FileId = 42, Location = cfi, Progress = .51, UpdatedAt = timestamp }, api.CaptureContext());
        Assert.Equal(cfi, result.Location);
        Assert.Equal(42, result.FileId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DepartingReaderCannotSaveIntoReplacementProfile()
    {
        var handler = new Handler(_ => Task.FromResult(Json("{}")));
        var client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        var context = api.CaptureContext();
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SaveProgressAsync("book", new() { FileId = 42, Location = "epubcfi(/6/6)", Progress = .5 }, context));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void BookOriginIsAResourceButCannotSendTrustedHostMessages()
    {
        Assert.True(EbookReaderWebPolicy.IsAllowedSubresource("https://silo-book.local/reader-source.pdf"));
        Assert.False(EbookReaderWebPolicy.IsTrustedReaderUri("https://silo-book.local/reader-source.pdf"));
        Assert.False(EbookReaderWebPolicy.IsAllowedSubresource("https://silo-book.local:444/reader-source.pdf"));
        Assert.False(EbookReaderWebPolicy.IsAllowedSubresource("https://someone@silo-book.local/reader-source.pdf"));
        Assert.False(EbookReaderWebPolicy.IsAllowedSubresource("blob:https://silo-book.local/book"));
    }

    private static SiloApiClient Client(Handler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://fixture.invalid");
        return client;
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return send(request); }
    }
}
