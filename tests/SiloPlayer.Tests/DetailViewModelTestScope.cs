using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>Detail tests construct valid recipients with optional owned catalog responses.</summary>
internal sealed class DetailViewModelTestScope : IDisposable
{
    private readonly HttpClient _http = new(new UnexpectedCatalogRequest());
    private readonly List<HttpClient> _catalogClients = [];
    private readonly List<ItemDetailViewModel> _models = [];

    internal ItemDetailViewModel Create(ItemDetailPrefetchCache cache) => Create(cache, _http);

    internal ItemDetailViewModel CreateWithCatalog(
        Func<string, CancellationToken, Task<MediaItemDetail>> fetch, ItemDetailPrefetchCache? cache = null)
    {
        var http = new HttpClient(new CatalogRequest(fetch));
        _catalogClients.Add(http);
        return Create(cache ?? new ItemDetailPrefetchCache((_, _) =>
            throw new InvalidOperationException("Mounted refresh attempted a speculative cache read.")), http);
    }

    private ItemDetailViewModel Create(ItemDetailPrefetchCache cache, HttpClient http)
    {
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://isolated-detail-tests.invalid");
        var model = new ItemDetailViewModel(new CatalogApi(client), cache);
        _models.Add(model);
        return model;
    }

    public void Dispose()
    {
        foreach (var model in _models) WeakReferenceMessenger.Default.UnregisterAll(model);
        foreach (var http in _catalogClients) http.Dispose();
        _http.Dispose();
    }

    private sealed class CatalogRequest(Func<string, CancellationToken, Task<MediaItemDetail>> fetch) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.StartsWith("/api/v2/catalog/items/", request.RequestUri!.AbsolutePath);
            var id = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Split('/').Last());
            var detail = await fetch(id, cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(detail,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }),
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class UnexpectedCatalogRequest : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Cache-only detail fixture attempted an unexpected catalog request.");
    }
}
