using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public sealed class EbooksApi(SiloApiClient client)
{
    private static string Content(string contentId) => Uri.EscapeDataString(contentId);

    public Task<byte[]> ReadFileAsync(string contentId, int fileId, CancellationToken ct = default)
        => client.GetBytesAsync($"/api/v1/ebooks/{Content(contentId)}/files/{fileId}/read", ct);

    public Task<EbookReaderProgress> GetProgressAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<EbookReaderProgress>($"/api/v1/ebooks/{Content(contentId)}/progress", ct);

    public Task<EbookReaderProgress> SaveProgressAsync(string contentId, EbookReaderProgressInput input, CancellationToken ct = default)
        => client.PutAsync<EbookReaderProgress>($"/api/v1/ebooks/{Content(contentId)}/progress", input, ct);

    public Task<EbookReaderConfigEnvelope> GetReaderConfigAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<EbookReaderConfigEnvelope>($"/api/v1/ebooks/{Content(contentId)}/reader-config", ct);

    public Task<EbookReaderConfigEnvelope> SaveReaderConfigAsync(string contentId, Dictionary<string, object?> config, CancellationToken ct = default)
        => client.PutAsync<EbookReaderConfigEnvelope>($"/api/v1/ebooks/{Content(contentId)}/reader-config", new { config }, ct);

    public async Task<List<EbookReaderAnnotation>> GetAnnotationsAsync(string contentId, CancellationToken ct = default)
        => (await client.GetAsync<EbookReaderAnnotationsEnvelope>($"/api/v1/ebooks/{Content(contentId)}/annotations", ct)).Items;

    public Task<EbookReaderAnnotation> CreateAnnotationAsync(string contentId, EbookReaderAnnotationInput input, CancellationToken ct = default)
        => client.PostAsync<EbookReaderAnnotation>($"/api/v1/ebooks/{Content(contentId)}/annotations", input, ct);

    public Task<EbookReaderAnnotation> UpdateAnnotationAsync(string contentId, string annotationId, object patch, CancellationToken ct = default)
        => client.PatchAsync<EbookReaderAnnotation>($"/api/v1/ebooks/{Content(contentId)}/annotations/{Uri.EscapeDataString(annotationId)}", patch, ct);

    public Task DeleteAnnotationAsync(string contentId, string annotationId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/ebooks/{Content(contentId)}/annotations/{Uri.EscapeDataString(annotationId)}", ct);
}
