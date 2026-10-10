using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionArtworkValidationTests
{
    [Fact]
    public void OversizePosterCannotReplaceAnExistingDraft()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client));
        var vm = Editor(client, auth);
        var accepted = new byte[10 * 1024 * 1024];
        vm.SetPosterFile("accepted.png", accepted, "image/png");
        Assert.Throws<ArgumentException>(() => vm.SetPosterFile("oversize.png", new byte[accepted.Length + 1], "image/png"));
        Assert.Same(accepted, vm.PosterFileBytes); Assert.Equal("accepted.png", vm.PosterFileName);
    }

    [Fact]
    public async Task RejectedPosterKeepsExistingArtworkAndDraftForRetry()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = Editor(client, auth); await vm.LoadExistingCommand.ExecuteAsync("created");
        var poster = new byte[] { 1, 2, 3 }; vm.SetPosterFile("broken.png", poster, "image/png");
        var saved = 0; vm.Saved += () => saved++;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("supported image", vm.ErrorMessage); Assert.Same(poster, vm.PosterFileBytes);
        Assert.Equal("https://artwork.invalid/existing.png", vm.CurrentPosterUrl); Assert.Equal(0, saved);
        wire.RejectPoster = false; await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Null(vm.PosterFileBytes); Assert.Equal(1, saved);
    }

    [Fact]
    public async Task ArtworkRejectionAfterCreationRetriesWithoutCreatingADuplicate()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = Editor(client, auth); vm.Name = "New draft";
        vm.SetPosterFile("broken.png", [1, 2, 3], "image/png");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage); Assert.Equal("created", vm.CollectionId); Assert.True(vm.IsEditing);
        wire.RejectPoster = false; await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.Creates);
    }

    private static CollectionEditorViewModel Editor(SiloApiClient client, AuthService auth)
        => new(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);

    private sealed class Wire : HttpMessageHandler
    {
        public bool RejectPoster = true; public int Creates;
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://poster-validation.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/v2/collections") Creates++;
            if (RejectPoster && request.Content?.Headers.ContentType?.MediaType == "multipart/form-data")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"code\":\"validation_failed\",\"message\":\"The file is not a supported image.\"}") });
            var json = path == "/api/v2/collections" || path == "/api/v2/collections/created"
                ? "{\"id\":\"created\",\"creator_profile_id\":\"mine\",\"name\":\"Draft\",\"collection_type\":\"manual\",\"poster_url\":\"https://artwork.invalid/existing.png\"}" : "{\"items\":[]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }; response.Headers.ETag = new("\"poster-revision\"");
            return Task.FromResult(response);
        }
    }
}
