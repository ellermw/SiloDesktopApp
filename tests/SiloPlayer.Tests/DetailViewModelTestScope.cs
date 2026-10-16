using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>Cache-only detail tests still construct valid messenger recipients.</summary>
internal sealed class DetailViewModelTestScope : IDisposable
{
    private readonly HttpClient _http = new(new UnexpectedCatalogRequest());
    private readonly List<ItemDetailViewModel> _models = [];

    internal ItemDetailViewModel Create(ItemDetailPrefetchCache cache)
    {
        var client = new SiloApiClient(_http);
        client.SetBaseUrl("https://isolated-detail-tests.invalid");
        var model = new ItemDetailViewModel(new CatalogApi(client), cache);
        _models.Add(model);
        return model;
    }

    public void Dispose()
    {
        foreach (var model in _models) WeakReferenceMessenger.Default.UnregisterAll(model);
        _http.Dispose();
    }

    private sealed class UnexpectedCatalogRequest : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Cache-only detail fixture attempted an unexpected catalog request.");
    }
}
