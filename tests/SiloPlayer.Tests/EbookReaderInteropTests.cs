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
    public async Task DownloadedBookIsRejectedAfterTheOwningProfileChanges()
    {
        SiloApiClient? client = null;
        var handler = new Handler(_ =>
        {
            client!.SetProfile("replacement");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("book bytes"u8.ToArray()) });
        });
        client = Client(handler);
        client.SetProfile("original");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EbooksApi(client).ReadFileAsync("book", 42));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadedBookSurvivesRequestContextRetirement(bool transportCatchUp)
    {
        SiloApiClient? client = null;
        var handler = new Handler(request =>
        {
            Assert.True(client!.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("book bytes"u8.ToArray()) });
        });
        client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        var owner = api.CaptureIdentityContext();
        var bytes = await api.ReadFileAsync("book", 42, api.CaptureContext(owner));
        Assert.Equal("book bytes"u8.ToArray(), bytes);
        Assert.True(api.IsCurrentIdentityContext(owner));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ReaderPreferencesSurviveTransportCatchUpDuringOpen()
    {
        SiloApiClient? client = null;
        var handler = new Handler(request =>
        {
            Assert.True(client!.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
            var response = Json("""{"config":{"theme":"sepia"}}""");
            response.Headers.ETag = new("\"config-revision\"");
            return Task.FromResult(response);
        });
        client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        var owner = api.CaptureIdentityContext();

        var result = await api.GetReaderConfigAsync("book");

        Assert.Equal("sepia", result.Config["theme"].GetString());
        Assert.True(api.IsCurrentIdentityContext(owner));
        Assert.True(api.HasReaderConfigRevision("book"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ReaderAnnotationsSurviveTransportCatchUpAcrossPagesDuringOpen()
    {
        SiloApiClient? client = null;
        var page = 0;
        var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                Assert.Equal("\"first\"", request.Headers.IfMatch.Single().ToString());
                return Task.FromResult(Json("{}"));
            }
            Assert.True(client!.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
            page++;
            if (page == 1)
                return Task.FromResult(Json("""{"items":[{"id":"first","etag":"\"first\""}],"page":{"has_more":true,"next_cursor":"next"}}"""));
            Assert.Contains("cursor=next", request.RequestUri!.Query);
            return Task.FromResult(Json("""{"items":[{"id":"second","etag":"\"second\""}],"page":{"has_more":false}}"""));
        });
        client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);

        var result = await api.GetAnnotationsAsync("book");

        Assert.Equal(new[] { "first", "second" }, result.Select(annotation => annotation.Id));
        await api.DeleteAnnotationAsync("book", "first");
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnnotationCanBeDeletedAfterRequestContextRetirement(bool transportCatchUp)
    {
        var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Json("""{"items":[{"id":"first","etag":"\"first\""}]}"""));
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/v2/ebooks/book/annotations/first", request.RequestUri!.AbsolutePath);
            Assert.Equal("\"first\"", request.Headers.IfMatch.Single().ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        await api.GetAnnotationsAsync("book");
        Assert.True(client.TryInvalidateAccessContext(api.CaptureContext(), out _, transportCatchUp));

        await api.DeleteAnnotationAsync("book", "first");

        Assert.Equal(2, handler.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DeleteAnnotationAsync("book", "first"));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnotationRevisionCannotBeUsedAfterReaderIdentityChanges(bool returnToOriginalProfile)
    {
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json("""{"items":[{"id":"first","etag":"\"first\""}]}"""));
        });
        var client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        await api.GetAnnotationsAsync("book");
        client.SetProfile("replacement");
        if (returnToOriginalProfile) client.SetProfile("original");

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DeleteAnnotationAsync("book", "first"));

        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("config")]
    [InlineData("annotations")]
    [InlineData("create")]
    public async Task ReaderResponsesAreRejectedIfIdentityChangesDuringTheOperation(string operation)
    {
        SiloApiClient? client = null;
        var handler = new Handler(_ =>
        {
            client!.SetProfile("replacement");
            return Task.FromResult(Json(operation == "annotations" ? """{"items":[]}""" : """{"id":"created","config":{}}"""));
        });
        client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (operation == "config") await api.GetReaderConfigAsync("book");
            else if (operation == "annotations") await api.GetAnnotationsAsync("book");
            else await api.CreateAnnotationAsync("book", new(), api.CaptureContext());
        });
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CommittedAnnotationSurvivesRequestRetirementWithoutRepeatingThePost()
    {
        SiloApiClient? client = null;
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.True(client!.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
            return Task.FromResult(Json("""{"id":"created","kind":"bookmark","etag":"\"created\""}"""));
        });
        client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);

        var annotation = await api.CreateAnnotationAsync("book", new() { Location = "fraction:0.5" }, api.CaptureContext());

        Assert.Equal("created", annotation.Id);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DownloadCannotStartWithARetiredProfileContext()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        var context = api.CaptureContext();
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ReadFileAsync("book", 42, context));
        Assert.Equal(0, handler.Calls);
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentReaderIdentityCanSaveAfterRequestContextRetirement(bool transportCatchUp)
    {
        var handler = new Handler(_ => Task.FromResult(Json("{}")));
        var client = Client(handler);
        client.SetProfile("original");
        var api = new EbooksApi(client);
        var owner = api.CaptureIdentityContext();
        Assert.True(client.TryInvalidateAccessContext(api.CaptureContext(), out _, transportCatchUp));
        Assert.True(api.IsCurrentIdentityContext(owner));
        await api.SaveProgressAsync("book", new() { FileId = 42, Location = "fraction:0.5", Progress = .5 }, api.CaptureContext(owner));
        Assert.Equal(1, handler.Calls);
        client.SetProfile("replacement");
        Assert.ThrowsAny<OperationCanceledException>(() => api.CaptureContext(owner));
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
