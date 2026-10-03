using System.Net;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class ManualCollectionMutationBehaviorTests
{
    [Fact]
    public async Task ExistingManualAddPersistsImmediatelyAndFailedRemoveKeepsDisplayedItem()
    {
        using var wire = new Wire(); var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth) { CollectionId = "manual-1", CollectionType = "manual", IsEditing = true };
        var add = Assert.IsAssignableFrom<IAsyncRelayCommand<MediaItem>>(vm.AddManualItemCommand);
        await add.ExecuteAsync(new MediaItem { ContentId = "movie-7", Title = "Seven", Type = "movie" });
        Assert.Contains("PUT /api/v2/collections/manual-1/items/movie-7", wire.Mutations);
        var remove = Assert.IsAssignableFrom<IAsyncRelayCommand<CollectionItem>>(vm.RemoveManualItemCommand);
        wire.FailRemove = true;
        await remove.ExecuteAsync(vm.ManualItems.Single());
        Assert.Single(vm.ManualItems);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task FailedManualAddDoesNotDisplayUnsavedMembership()
    {
        using var wire = new Wire { FailAdd = true }; var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth) { CollectionId = "manual-1", CollectionType = "manual", IsEditing = true };
        var add = Assert.IsAssignableFrom<IAsyncRelayCommand<MediaItem>>(vm.AddManualItemCommand);
        await add.ExecuteAsync(new MediaItem { ContentId = "movie-7", Title = "Seven", Type = "movie" });
        Assert.Empty(vm.ManualItems);
        Assert.NotNull(vm.ErrorMessage);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool FailAdd { get; set; }
        public bool FailRemove { get; set; }
        public List<string> Mutations { get; } = [];
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://manual-fixture.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get) Mutations.Add($"{request.Method} {path}");
            var fail = request.Method == HttpMethod.Put && FailAdd || request.Method == HttpMethod.Delete && FailRemove;
            return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(fail ? "{\"error\":\"unavailable\",\"message\":\"Try again\"}" : "{\"items\":[],\"page\":{\"has_more\":false}}") });
        }
    }
}
