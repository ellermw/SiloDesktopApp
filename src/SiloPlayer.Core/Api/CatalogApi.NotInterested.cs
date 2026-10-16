using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public sealed class NotInterestedMark
{
    public string ItemId { get; set; } = "";
    public DateTimeOffset MarkedAt { get; set; }
}

public partial class CatalogApi
{
    private sealed class NotInterestedCapability { public string State { get; set; } = "unsupported"; }
    private (ApiRequestContext Context, bool Available)? _notInterestedCapability;
    private readonly SemaphoreSlim _notInterestedCapabilityGate = new(1, 1);

    public Task<bool> SupportsNotInterestedAsync(CancellationToken ct = default)
        => SupportsNotInterestedAsync(client.CaptureContext(), ct);

    public async Task<bool> SupportsNotInterestedAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        await _notInterestedCapabilityGate.WaitAsync(ct);
        try
        {
            EnsureNotInterestedContext(context, ct);
            if (_notInterestedCapability is { } cached && cached.Context == context) return cached.Available;
            bool available;
            try
            {
                var capability = await client.SendRequestAsync<NotInterestedCapability>(context, HttpMethod.Get,
                    "/api/v2/capabilities/not-interested", null, ct);
                available = capability.State == "available";
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501) { available = false; }
            EnsureNotInterestedContext(context, ct);
            _notInterestedCapability = (context, available);
            return available;
        }
        finally { _notInterestedCapabilityGate.Release(); }
    }

    public async Task<CatalogResponse> GetNotInterestedAsync(ApiRequestContext context, string? cursor = null, CancellationToken ct = default)
    {
        EnsureNotInterestedContext(context, ct);
        var path = "/api/v2/not-interested?limit=50";
        if (!string.IsNullOrEmpty(cursor)) path += "&cursor=" + Uri.EscapeDataString(cursor);
        var page = await client.SendRequestAsync<CatalogResponse>(context, HttpMethod.Get, path, null, ct);
        EnsureNotInterestedContext(context, ct);
        return page;
    }

    public async Task<NotInterestedMark?> GetNotInterestedMarkAsync(ApiRequestContext context, string itemId, CancellationToken ct = default)
    {
        try
        {
            var mark = await client.SendRequestAsync<NotInterestedMark>(context, HttpMethod.Get,
                "/api/v2/not-interested/" + Uri.EscapeDataString(itemId), null, ct);
            EnsureNotInterestedContext(context, ct);
            return mark;
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { EnsureNotInterestedContext(context, ct); return null; }
    }

    public async Task SetNotInterestedAsync(ApiRequestContext context, string itemId, string type, bool marked, CancellationToken ct = default)
    {
        if (type is not ("movie" or "series")) throw new ArgumentException("Only movies and series can be marked Not interested.", nameof(type));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("A catalog item is required.", nameof(itemId));
        if (!await SupportsNotInterestedAsync(context, ct)) throw new NotSupportedException("Not interested is unavailable on this server.");
        EnsureNotInterestedContext(context, ct);
        await client.SendNoContentRequestAsync(context, marked ? HttpMethod.Put : HttpMethod.Delete,
            "/api/v2/not-interested/" + Uri.EscapeDataString(itemId), null, ct);
        EnsureNotInterestedContext(context, ct);
    }

    private void EnsureNotInterestedContext(ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Not interested profile changed.", ct);
    }
}
