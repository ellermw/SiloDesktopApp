using System.Text.Json;
using System.Collections.Concurrent;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public sealed class EbooksApi(SiloApiClient client)
{
    private readonly ConcurrentDictionary<string, (ApiRequestContext Context, string ETag)> _revisions = new();
    private static string Content(string contentId) => Uri.EscapeDataString(contentId);

    public Task<byte[]> ReadFileAsync(string contentId, int fileId, CancellationToken ct = default)
        => client.GetBytesAsync($"/api/v2/ebooks/{Content(contentId)}/files/{fileId}/read", ct);

    public async Task<EbookReaderProgress> GetProgressAsync(string contentId, CancellationToken ct = default)
        => (await client.GetAsync<ProgressEnvelope>($"/api/v2/ebooks/{Content(contentId)}/progress", ct)).Progress ?? new();

    public async Task<EbookReaderProgress> SaveProgressAsync(string contentId, EbookReaderProgressInput input, CancellationToken ct = default)
        => (await client.PutAsync<ProgressEnvelope>($"/api/v2/ebooks/{Content(contentId)}/progress", V2Json.Body(input), ct)).Progress ?? new();

    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);

    public async Task<EbookReaderProgress> SaveProgressAsync(string contentId, EbookReaderProgressInput input, ApiRequestContext context, CancellationToken ct = default)
        => (await client.SendRequestAsync<ProgressEnvelope>(context, HttpMethod.Put,
            $"/api/v2/ebooks/{Content(contentId)}/progress", V2Json.Body(input), ct)).Progress ?? new();

    public async Task<EbookReaderConfigEnvelope> GetReaderConfigAsync(string contentId, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/reader-config";
        var context = client.CaptureContext();
        var response = await client.GetWithETagAsync<EbookReaderConfigEnvelope>(path, ct);
        Remember(path, response.ETag, context, ct);
        return response.Body;
    }

    public async Task<EbookReaderConfigEnvelope> SaveReaderConfigAsync(string contentId, Dictionary<string, object?> config, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/reader-config";
        var context = client.CaptureContext();
        var response = await client.PutWithETagResponseAsync<EbookReaderConfigEnvelope>(path, new Dictionary<string, object?> { ["config"] = config }, Revision(path), ct);
        Remember(path, response.ETag, context, ct);
        return response.Body;
    }
    public bool HasReaderConfigRevision(string contentId)
        => _revisions.TryGetValue($"/api/v2/ebooks/{Content(contentId)}/reader-config", out var revision) && client.IsCurrentContext(revision.Context);

    public async Task<List<EbookReaderAnnotation>> GetAnnotationsAsync(string contentId, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations";
        var annotations = await BrowseV2.AllAsync<EbookReaderAnnotation>(client, path, ct, limit: 50);
        foreach (var annotation in annotations) Remember(path + "/" + Content(annotation.Id), annotation.ETag, context, ct);
        return annotations;
    }

    public async Task<EbookReaderAnnotation> CreateAnnotationAsync(string contentId, EbookReaderAnnotationInput input, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations";
        var context = client.CaptureContext();
        var annotation = await client.PostAsync<EbookReaderAnnotation>(path, JsonSerializer.SerializeToElement(input, V2Json.Options), ct);
        Remember(path + "/" + Content(annotation.Id), annotation.ETag, context, ct);
        return annotation;
    }

    public async Task<EbookReaderAnnotation> CreateAnnotationAsync(string contentId, EbookReaderAnnotationInput input, ApiRequestContext context, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations";
        var annotation = await client.SendRequestAsync<EbookReaderAnnotation>(context, HttpMethod.Post, path,
            JsonSerializer.SerializeToElement(input, V2Json.Options), ct);
        Remember(path + "/" + Content(annotation.Id), annotation.ETag, context, ct);
        return annotation;
    }

    public async Task<EbookReaderAnnotation> UpdateAnnotationAsync(string contentId, string annotationId, object patch, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations/{Content(annotationId)}";
        var context = client.CaptureContext();
        var annotation = await client.PatchWithETagAsync<EbookReaderAnnotation>(path, patch, Revision(path), ct);
        Remember(path, annotation.ETag, context, ct);
        return annotation;
    }

    public async Task DeleteAnnotationAsync(string contentId, string annotationId, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations/{Content(annotationId)}";
        await client.DeleteWithETagAsync(path, Revision(path), ct);
        _revisions.TryRemove(path, out _);
    }

    private string Revision(string path)
        => _revisions.TryGetValue(path, out var revision) && client.IsCurrentContext(revision.Context)
            ? revision.ETag : throw new InvalidOperationException("Reload the reader before saving changes.");

    private void Remember(string path, string? etag, ApiRequestContext context, CancellationToken ct)
    {
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Reader context changed.", ct);
        if (!string.IsNullOrEmpty(etag)) _revisions[path] = (context, etag);
        else _revisions.TryRemove(path, out _);
    }

    private sealed class ProgressEnvelope { public EbookReaderProgress? Progress { get; set; } }
}
