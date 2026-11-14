using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal async Task<ApiResponse<T>> MonitorLogsSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, string? etag = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        if (etag != null) LoggingDraft.RequireStrongETag(etag);
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                throw new LoggingSettingsConflictException(response.Headers.ETag?.ToString());
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
            }
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, 8 * 1024 * 1024, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            var result = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty Monitor response.");
            lease.ThrowIfRetired();
            return new(result, response.Headers.ETag?.ToString());
        }
        finally { lease.ThrowIfRetired(); }
    }

    internal async Task MonitorLogsDeleteAsync(MonitorAuthorityLease lease, string path, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Delete, path, null);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        finally { lease.ThrowIfRetired(); }
    }

    internal async Task MonitorLogsDownloadAsync(MonitorAuthorityLease lease, string path, Stream destination, long maxBytes, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        if (!destination.CanWrite) throw new ArgumentException("A writable destination is required.", nameof(destination));
        if (maxBytes < 3 || maxBytes > 256L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Get, path, null);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/gzip"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() != "application/gzip")
                throw new InvalidDataException("The diagnostic bundle has an unexpected format.");
            if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
                throw new InvalidDataException("The diagnostic bundle exceeds the download limit.");
            using var source = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            var buffer = new byte[64 * 1024];
            var prefix = 0;
            while (prefix < 3)
            {
                var read = await source.ReadAsync(buffer.AsMemory(prefix, 3 - prefix), linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
                if (read == 0) throw new InvalidDataException("The diagnostic bundle is not gzip data.");
                prefix += read;
            }
            if (buffer[0] != 0x1f || buffer[1] != 0x8b || buffer[2] != 8) throw new InvalidDataException("The diagnostic bundle is not gzip data.");
            long total = prefix;
            lease.ThrowIfRetired();
            await destination.WriteAsync(buffer.AsMemory(0, prefix), linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            while (true)
            {
                var read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
                if (read == 0) break;
                total += read;
                if (total > maxBytes) throw new InvalidDataException("The diagnostic bundle exceeds the download limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
            }
            if (response.Content.Headers.ContentLength is { } expected && total != expected)
                throw new InvalidDataException("The diagnostic bundle is incomplete.");
            await destination.FlushAsync(linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        finally { lease.ThrowIfRetired(); }
    }

    private static async Task<byte[]> MonitorLogsReadBoundedAsync(HttpContent content, MonitorAuthorityLease lease, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } length && length > maxBytes) throw new InvalidDataException("Monitor response exceeds its size limit.");
        using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        using var target = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (read == 0) return target.ToArray();
            if (target.Length + read > maxBytes) throw new InvalidDataException("Monitor response exceeds its size limit.");
            target.Write(buffer, 0, read);
        }
    }
}
