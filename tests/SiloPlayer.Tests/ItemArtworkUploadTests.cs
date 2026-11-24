using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Official silo-server #3088: upload a poster, backdrop or logo from Edit metadata > Images.</summary>
public sealed class ItemArtworkUploadTests
{
    [Theory]
    [InlineData("image/jpeg", 1L, ArtworkUploadRefusal.None)]
    [InlineData("image/png", 10L * 1024 * 1024, ArtworkUploadRefusal.None)]
    [InlineData("image/webp", 10L * 1024 * 1024 + 1, ArtworkUploadRefusal.TooLarge)]
    [InlineData("image/gif", 1L, ArtworkUploadRefusal.WrongType)]
    [InlineData("image/svg+xml", 1L, ArtworkUploadRefusal.WrongType)]
    [InlineData("application/pdf", 1L, ArtworkUploadRefusal.WrongType)]
    [InlineData("IMAGE/PNG", 1L, ArtworkUploadRefusal.WrongType)]
    // A type the system cannot name goes to the server, which checks the bytes.
    [InlineData("", 1L, ArtworkUploadRefusal.None)]
    [InlineData(null, 5L, ArtworkUploadRefusal.None)]
    [InlineData("", 10L * 1024 * 1024 + 1, ArtworkUploadRefusal.TooLarge)]
    // The type is checked before the size.
    [InlineData("image/gif", 11L * 1024 * 1024, ArtworkUploadRefusal.WrongType)]
    public void FilesAreRefusedBeforeSendingByTypeThenSize(string? contentType, long length, ArtworkUploadRefusal expected)
        => Assert.Equal(expected, ItemArtworkUpload.Check(contentType, length));

    [Fact]
    public void OfficialEnglishTexts()
    {
        Assert.Equal("Choose a JPEG, PNG or WebP image", ItemArtworkUpload.RefusalMessage(ArtworkUploadRefusal.WrongType));
        Assert.Equal("Images can be at most 10 MiB", ItemArtworkUpload.RefusalMessage(ArtworkUploadRefusal.TooLarge));
        Assert.Equal("Upload", ItemArtworkUpload.ButtonText(false));
        Assert.Equal("Uploading...", ItemArtworkUpload.ButtonText(true));
        Assert.Equal("Upload poster", ItemArtworkUpload.UploadName("poster"));
        Assert.Equal("Upload backdrop", ItemArtworkUpload.UploadName("backdrop"));
        Assert.Equal("Upload logo", ItemArtworkUpload.UploadName("logo"));
        Assert.Equal("Image uploaded", ItemArtworkUpload.SuccessMessage());
        Assert.Equal("Failed to upload image", ItemArtworkUpload.GenericFailureMessage());
        Assert.Equal(10L * 1024 * 1024, ItemArtworkUpload.MaxBytes);
        Assert.Equal(["image/jpeg", "image/png", "image/webp"], ItemArtworkUpload.AcceptedTypes);
    }

    [Fact]
    public void MoviesAndSeriesTakeThreeSlotsAndSeasonsTakePosterAndBackdrop()
    {
        Assert.Equal(["poster", "backdrop", "logo"], ItemArtworkUpload.SlotsFor("movie"));
        Assert.Equal(["poster", "backdrop", "logo"], ItemArtworkUpload.SlotsFor("series"));
        Assert.Equal(["poster", "backdrop"], ItemArtworkUpload.SlotsFor("season"));
        Assert.False(ItemArtworkUpload.IsSlot("banner"));
    }

    [Fact]
    public void AValidationProblemShowsItsFirstFieldDetailAndOtherErrorsShowTheirOwnText()
    {
        var refused = new ApiException("validation_failed", "The request did not pass validation", 422)
        {
            FieldErrors = [new ApiFieldError("body.image", "unsupported_image", "The file is not a JPEG, PNG or WebP image"), new ApiFieldError("body.type", "x", "Second")],
        };
        Assert.Equal("The file is not a JPEG, PNG or WebP image", ItemArtworkUpload.FailureMessage(refused));
        Assert.Equal("Service Unavailable", ItemArtworkUpload.FailureMessage(new ApiException("unavailable", "Service Unavailable", 503)));
        Assert.Equal("The connection was lost", ItemArtworkUpload.FailureMessage(new HttpRequestException("The connection was lost")));
        Assert.Equal("Failed to upload image", ItemArtworkUpload.FailureMessage(new ApiException("error", "", 500)));
    }

    [Fact]
    public async Task ReadingStopsAtTheLimitEvenWhenTheFileGrew()
    {
        var small = new ArtworkUploadFile("a.png", "image/png", 3, _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        Assert.Equal([1, 2, 3], await ItemArtworkUpload.ReadAsync(small, CancellationToken.None));
        var opened = false;
        var declaredLarge = new ArtworkUploadFile("a.png", "image/png", ItemArtworkUpload.MaxBytes + 1, _ => { opened = true; return Task.FromResult<Stream>(new MemoryStream()); });
        Assert.Null(await ItemArtworkUpload.ReadAsync(declaredLarge, CancellationToken.None));
        Assert.False(opened);
        var grew = new ArtworkUploadFile("a.png", "image/png", 3, _ => Task.FromResult<Stream>(new MemoryStream(new byte[ItemArtworkUpload.MaxBytes + 1])));
        Assert.Null(await ItemArtworkUpload.ReadAsync(grew, CancellationToken.None));
    }

    [Fact]
    public async Task OneMultipartPostCarriesTheSlotThenTheImage()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"content_id":"movie/1","stored_path":"local/x","thumbhash":"t"}""");
        var (client, api) = Create(handler);
        var owner = client.CaptureIdentityContext();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        await api.UploadImageAsync(owner, "movie/1", "backdrop", png, "my art.png", "image/png");

        Assert.Equal(1, handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/v2/admin/items/movie%2F1/images/upload", handler.Uri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.StartsWith("multipart/form-data", handler.ContentType);
        Assert.Collection(handler.Parts,
            type => { Assert.Equal("type", type.Name); Assert.Null(type.FileName); Assert.Equal("backdrop", Encoding.UTF8.GetString(type.Bytes)); },
            image => { Assert.Equal("image", image.Name); Assert.Equal("my art.png", image.FileName); Assert.Equal("image/png", image.ContentType); Assert.Equal(png, image.Bytes); });
    }

    [Fact]
    public async Task AnUnnamedFileTypeIsSentAsOctetStreamAndOnlyTheNameIsKept()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var (client, api) = Create(handler);
        await api.UploadImageAsync(client.CaptureIdentityContext(), "movie-1", "logo", [1], @"C:\Users\someone\Pictures\logo", "");
        var image = handler.Parts.Single(part => part.Name == "image");
        Assert.Equal("application/octet-stream", image.ContentType);
        Assert.Equal("logo", image.FileName);
        Assert.DoesNotContain("Users", Encoding.UTF8.GetString(handler.Raw!));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task AFailedUploadIsNeverSentAgainNorReplayedAfterATokenRefresh(HttpStatusCode status)
    {
        var handler = new Handler(status, """{"title":"Nope","status":0}""");
        var (client, api) = Create(handler);
        var refreshes = 0;
        client.SetTokenRefresher(_ => { refreshes++; client.SetAccessToken("refreshed"); return Task.FromResult(true); });

        await Assert.ThrowsAsync<ApiException>(() => api.UploadImageAsync(client.CaptureIdentityContext(), "movie-1", "poster", [1, 2, 3], "a.png", "image/png"));
        await Task.Delay(50);

        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task AValidationProblemKeepsItsFieldDetailForTheToast()
    {
        var handler = new Handler((HttpStatusCode)422, """{"type":"https://silo.invalid/problems/validation","title":"Validation failed","status":422,"detail":"The request did not pass validation","errors":[{"location":"body.image","code":"image_unsupported","detail":"The file is not a JPEG, PNG or WebP image"}]}""", "application/problem+json");
        var (client, api) = Create(handler);
        var failure = await Assert.ThrowsAsync<ApiException>(() => api.UploadImageAsync(client.CaptureIdentityContext(), "movie-1", "poster", [1], "a.gif", ""));
        Assert.Equal("The file is not a JPEG, PNG or WebP image", ItemArtworkUpload.FailureMessage(failure));
    }

    [Fact]
    public async Task AnOwnerWhoIsGoneSendsNothing()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var (client, api) = Create(handler);
        var owner = client.CaptureIdentityContext();
        client.ClearAuth();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UploadImageAsync(owner, "movie-1", "poster", [1], "a.png", "image/png"));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task AReplyAfterAnAccountSwitchIsDropped()
    {
        SiloApiClient? switching = null;
        var handler = new Handler(HttpStatusCode.OK, """{"content_id":"movie-1","stored_path":"p","thumbhash":"t"}""") { OnRequest = () => switching!.ClearAuth() };
        var (client, api) = Create(handler);
        switching = client;
        var owner = client.CaptureIdentityContext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UploadImageAsync(owner, "movie-1", "poster", [1], "a.png", "image/png"));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ACanceledUploadStops()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var (client, api) = Create(handler);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UploadImageAsync(client.CaptureIdentityContext(), "movie-1", "poster", [1], "a.png", "image/png", cancel.Token));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task OnlyKnownSlotsAndAnItemAreAccepted()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var (client, api) = Create(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => api.UploadImageAsync(client.CaptureIdentityContext(), "movie-1", "banner", [1], "a.png", "image/png"));
        await Assert.ThrowsAsync<ArgumentException>(() => api.UploadImageAsync(client.CaptureIdentityContext(), " ", "poster", [1], "a.png", "image/png"));
        Assert.Equal(0, handler.Requests);
    }

    private static (SiloApiClient Client, MediaMaintenanceApi Api) Create(Handler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        client.SetAccessToken("access-token");
        return (client, new MediaMaintenanceApi(client));
    }

    private sealed record Part(string Name, string? FileName, string? ContentType, byte[] Bytes);

    private sealed class Handler(HttpStatusCode status, string json, string mediaType = "application/json") : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public Action? OnRequest { get; init; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }
        public string? ContentType { get; private set; }
        public byte[]? Raw { get; private set; }
        public List<Part> Parts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            Method = request.Method; Uri = request.RequestUri; Authorization = request.Headers.Authorization;
            if (request.Content != null)
            {
                ContentType = request.Content.Headers.ContentType?.ToString();
                Raw = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                Parts.AddRange(await ReadParts(request.Content));
            }
            OnRequest?.Invoke();
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
        }

        private static async Task<IEnumerable<Part>> ReadParts(HttpContent content)
        {
            var result = new List<Part>();
            var multipart = (MultipartFormDataContent)content;
            foreach (var part in multipart)
            {
                var disposition = part.Headers.ContentDisposition!;
                result.Add(new(disposition.Name!.Trim('"'), disposition.FileName?.Trim('"'), part.Headers.ContentType?.MediaType, await part.ReadAsByteArrayAsync()));
            }
            return result;
        }
    }
}
