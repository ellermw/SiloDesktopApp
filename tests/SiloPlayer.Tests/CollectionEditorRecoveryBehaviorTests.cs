using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionEditorRecoveryBehaviorTests
{
    [Fact]
    public async Task FailedExistingLoadShowsUnavailableAndRetryRestoresEditor()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture"); Assert.True(vm.IsLoadUnavailable); Assert.False(vm.IsNotFound);
        wire.Status = HttpStatusCode.OK; await vm.LoadExistingCommand.ExecuteAsync("fixture");
        Assert.False(vm.IsLoadUnavailable); Assert.False(vm.IsNotFound); Assert.Equal("Recovered", vm.Name); Assert.Null(vm.ErrorMessage);
    }
    [Fact]
    public async Task MissingCollectionShowsNotFoundWithoutEditableDraft()
    {
        using var wire = new Wire { Status = HttpStatusCode.NotFound }; var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture"); Assert.True(vm.IsNotFound); Assert.Equal("Collection not found.", vm.ErrorMessage);
    }
    private sealed class Wire : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.ServiceUnavailable;
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://collection-editor.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var collection = request.RequestUri!.AbsolutePath == "/api/v2/collections/fixture";
            var status = collection ? Status : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(collection && status == HttpStatusCode.OK ? "{\"id\":\"fixture\",\"name\":\"Recovered\",\"collection_type\":\"smart\"}" : status == HttpStatusCode.OK ? "{\"items\":[]}" : "{\"message\":\"collection unavailable\"}") });
        }
    }
}
