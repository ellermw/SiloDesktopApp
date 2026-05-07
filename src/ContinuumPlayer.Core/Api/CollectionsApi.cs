using ContinuumPlayer.Core.Models.Collections;

namespace ContinuumPlayer.Core.Api;

public class CollectionsApi(ContinuumApiClient client)
{
    public Task<CollectionsResponse> GetCollectionsAsync(CancellationToken ct = default)
        => client.GetAsync<CollectionsResponse>("/api/v1/collections", ct);

    public Task<CollectionTemplateCatalog> GetCollectionTemplatesAsync(CancellationToken ct = default)
        => client.GetAsync<CollectionTemplateCatalog>("/api/v1/collections/templates", ct);

    public Task<MDBListDiscoveryResponse> SearchMDBListAsync(string query, CancellationToken ct = default)
        => client.GetAsync<MDBListDiscoveryResponse>($"/api/v1/collections/import/mdblist/search?q={Uri.EscapeDataString(query)}", ct);

    public Task<MDBListDiscoveryResponse> GetTopMDBListAsync(CancellationToken ct = default)
        => client.GetAsync<MDBListDiscoveryResponse>("/api/v1/collections/import/mdblist/top", ct);

    public Task<Collection> CreateCollectionAsync(CreateCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<Collection>("/api/v1/collections", request, ct);

    public Task<ImportUserCollectionResponse> ImportUserMDBListCollectionAsync(ImportUserMDBListCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportUserCollectionResponse>("/api/v1/collections/import/mdblist", request, ct);

    public Task<ImportUserCollectionResponse> ImportUserTMDBCollectionAsync(ImportUserTMDBCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportUserCollectionResponse>("/api/v1/collections/import/tmdb", request, ct);

    public Task<ImportUserCollectionResponse> ImportUserTraktCollectionAsync(ImportUserTraktCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportUserCollectionResponse>("/api/v1/collections/import/trakt", request, ct);

    public Task<CollectionPreviewResponse> PreviewCollectionAsync(CollectionPreviewRequest request, CancellationToken ct = default)
        => client.PostAsync<CollectionPreviewResponse>("/api/v1/collections/preview", request, ct);

    public Task<Collection> UpdateCollectionAsync(string id, UpdateCollectionRequest request, CancellationToken ct = default)
        => client.PutAsync<Collection>($"/api/v1/collections/{Uri.EscapeDataString(id)}", request, ct);

    public Task DeleteCollectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/collections/{Uri.EscapeDataString(id)}", ct);

    public Task DeleteCollectionImageAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/collections/{Uri.EscapeDataString(id)}/image?type=poster", ct);

    public Task<UserCollectionSyncResult> SyncCollectionAsync(string id, CancellationToken ct = default)
        => client.PostAsync<UserCollectionSyncResult>(
            $"/api/v1/collections/{Uri.EscapeDataString(id)}/sync",
            new Dictionary<string, object?>(),
            ct);

    public Task<CollectionItemsResponse> GetCollectionItemsAsync(string id, CancellationToken ct = default)
        => client.GetAsync<CollectionItemsResponse>($"/api/v1/collections/{Uri.EscapeDataString(id)}/items", ct);

    public Task AddCollectionItemAsync(string collectionId, string itemId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(itemId)}", null, ct);

    public Task RemoveCollectionItemAsync(string collectionId, string itemId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(itemId)}", ct);
}
