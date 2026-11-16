using System.Text.Json;
using System.Collections.Concurrent;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public sealed class EbooksApi(SiloApiClient client)
{
    private readonly ConcurrentDictionary<string, (ApiRequestContext Context, string ETag)> _revisions = new();
    private static string Content(string contentId) => Uri.EscapeDataString(contentId);

    public Task<byte[]> ReadFileAsync(string contentId, int fileId, CancellationToken ct = default)
        => ReadFileAsync(contentId, fileId, client.CaptureContext(), ct);

    public async Task<byte[]> ReadFileAsync(string contentId, int fileId, ApiRequestContext context, CancellationToken ct = default)
    {
        var owner = client.CaptureIdentityContext();
        var bytes = await client.SendRawBytesRequestAsync(context, HttpMethod.Get,
            $"/api/v2/ebooks/{Content(contentId)}/files/{fileId}/read", ct);
        ct.ThrowIfCancellationRequested();
        // The request checks its authority before sending; returned bytes belong
        // to the same reader even if access/cache refresh retired that request.
        if (!client.IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Reader identity changed.", ct);
        return bytes;
    }

    public event Action? ContextInvalidated
    {
        add => client.ContextInvalidated += value;
        remove => client.ContextInvalidated -= value;
    }

    public async Task<EbookReaderProgress> GetProgressAsync(string contentId, CancellationToken ct = default)
        => (await client.GetAsync<ProgressEnvelope>($"/api/v2/ebooks/{Content(contentId)}/progress", ct)).Progress ?? new();

    public async Task<EbookReaderProgress> SaveProgressAsync(string contentId, EbookReaderProgressInput input, CancellationToken ct = default)
        => (await client.PutAsync<ProgressEnvelope>($"/api/v2/ebooks/{Content(contentId)}/progress", V2Json.Body(input), ct)).Progress ?? new();

    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public ApiIdentityContext CaptureIdentityContext() => client.CaptureIdentityContext();
    public bool IsCurrentIdentityContext(ApiIdentityContext context) => client.IsCurrentIdentityContext(context);

    public ApiRequestContext CaptureContext(ApiIdentityContext owner)
    {
        var context = client.CaptureContext();
        if (!client.IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Reader identity changed.");
        return context;
    }

    public async Task<EbookReaderProgress> SaveProgressAsync(string contentId, EbookReaderProgressInput input, ApiRequestContext context, CancellationToken ct = default)
        => (await client.SendRequestAsync<ProgressEnvelope>(context, HttpMethod.Put,
            $"/api/v2/ebooks/{Content(contentId)}/progress", V2Json.Body(input), ct)).Progress ?? new();

    public async Task<EbookReaderConfigEnvelope> GetReaderConfigAsync(string contentId, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/reader-config";
        var owner = client.CaptureIdentityContext();
        var response = await client.GetWithETagAsync<EbookReaderConfigEnvelope>(path, ct);
        Remember(path, response.ETag, owner, ct);
        return response.Body;
    }

    public async Task<EbookReaderConfigEnvelope> SaveReaderConfigAsync(string contentId, Dictionary<string, object?> config, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/reader-config";
        var owner = client.CaptureIdentityContext();
        var response = await client.PutWithETagResponseAsync<EbookReaderConfigEnvelope>(path, new Dictionary<string, object?> { ["config"] = config }, Revision(path), ct);
        Remember(path, response.ETag, owner, ct);
        return response.Body;
    }
    public bool HasReaderConfigRevision(string contentId)
        => _revisions.TryGetValue($"/api/v2/ebooks/{Content(contentId)}/reader-config", out var revision) && client.IsCurrentContext(revision.Context);

    public async Task<List<EbookReaderAnnotation>> GetAnnotationsAsync(string contentId, CancellationToken ct = default)
    {
        var owner = client.CaptureIdentityContext();
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations";
        var annotations = new List<EbookReaderAnnotation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            // Refresh request authority for each page, but retain reader ownership.
            var query = path + "?limit=50" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await client.SendIdentityBoundRequestAsync<BrowseCollection<EbookReaderAnnotation>>(owner, HttpMethod.Get, query, null, ct);
            EnsureIdentity(owner, ct);
            annotations.AddRange(page.Items);
            if (page.Page?.HasMore != true) break;
            cursor = page.Page.NextCursor;
            if (page.Items.Count == 0 || string.IsNullOrEmpty(cursor) || !seen.Add(cursor))
                throw new InvalidOperationException("The server returned a non-advancing browse cursor.");
        } while (true);
        foreach (var annotation in annotations) Remember(path + "/" + Content(annotation.Id), annotation.ETag, owner, ct);
        return annotations;
    }

    public async Task<EbookReaderAnnotation> CreateAnnotationAsync(string contentId, EbookReaderAnnotationInput input, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations";
        var owner = client.CaptureIdentityContext();
        var annotation = await client.SendIdentityBoundRequestAsync<EbookReaderAnnotation>(owner, HttpMethod.Post, path,
            JsonSerializer.SerializeToElement(input, V2Json.Options), ct);
        Remember(path + "/" + Content(annotation.Id), annotation.ETag, owner, ct);
        return annotation;
    }

    public async Task<EbookReaderAnnotation> CreateAnnotationAsync(string contentId, EbookReaderAnnotationInput input, ApiRequestContext context, CancellationToken ct = default)
    {
        var path = $"/api/v2/ebooks/{Content(contentId)}/annotations";
        var owner = client.CaptureIdentityContext();
        var annotation = await client.SendRequestAsync<EbookReaderAnnotation>(context, HttpMethod.Post, path,
            JsonSerializer.SerializeToElement(input, V2Json.Options), ct);
        Remember(path + "/" + Content(annotation.Id), annotation.ETag, owner, ct);
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

    private void EnsureIdentity(ApiIdentityContext owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Reader identity changed.", ct);
    }

    private void Remember(string path, string? etag, ApiIdentityContext owner, CancellationToken ct)
    {
        var context = client.CaptureContext();
        EnsureIdentity(owner, ct);
        // A completed response still belongs to this reader after request retirement.
        if (!string.IsNullOrEmpty(etag)) _revisions[path] = (context, etag);
        else _revisions.TryRemove(path, out _);
    }

    private sealed class ProgressEnvelope { public EbookReaderProgress? Progress { get; set; } }
}
