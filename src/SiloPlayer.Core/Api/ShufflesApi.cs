using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public sealed class ShufflesApi(SiloApiClient client)
{
    public Task<Shuffle> CreateAsync(ShuffleScopeRequest scope, CancellationToken ct = default)
        => CreateAsync(scope, client.CaptureContext(), ct);
    public Task<Shuffle> GetAsync(string id, CancellationToken ct = default)
        => GetAsync(id, client.CaptureContext(), ct);
    public Task<Shuffle> AdvanceAsync(string id, string fromContentId, CancellationToken ct = default)
        => AdvanceAsync(id, fromContentId, client.CaptureContext(), ct);
    public Task<Shuffle> SkipAsync(string id, string nextContentId, CancellationToken ct = default)
        => SkipAsync(id, nextContentId, client.CaptureContext(), ct);
    public Task DeleteAsync(string id, CancellationToken ct = default)
        => DeleteAsync(id, client.CaptureContext(), ct);

    internal Task<Shuffle> CreateAsync(ShuffleScopeRequest scope, ApiRequestContext context, CancellationToken ct)
    {
        if (scope.Kind is not ("library" or "series" or "season" or "library_collection" or "user_collection"))
            throw new ArgumentException("Unsupported shuffle scope.", nameof(scope));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Id);
        return client.SendRequestAsync<Shuffle>(context, HttpMethod.Post, "/api/v2/shuffles?image_size=large", new { scope }, ct);
    }
    internal Task<Shuffle> GetAsync(string id, ApiRequestContext context, CancellationToken ct)
        => client.SendRequestAsync<Shuffle>(context, HttpMethod.Get, Path(id) + "?image_size=large", null, ct);
    internal Task<Shuffle> AdvanceAsync(string id, string fromContentId, ApiRequestContext context, CancellationToken ct)
        => client.SendRequestAsync<Shuffle>(context, HttpMethod.Post, Path(id) + "/advance?image_size=large", new { from_content_id = fromContentId }, ct);
    internal Task<Shuffle> SkipAsync(string id, string nextContentId, ApiRequestContext context, CancellationToken ct)
        => client.SendRequestAsync<Shuffle>(context, HttpMethod.Post, Path(id) + "/skip?image_size=large", new { next_content_id = nextContentId }, ct);
    internal Task DeleteAsync(string id, ApiRequestContext context, CancellationToken ct)
        => client.SendNoContentRequestAsync(context, HttpMethod.Delete, Path(id), null, ct);
    private static string Path(string id) => "/api/v2/shuffles/" + Uri.EscapeDataString(id);
}
