using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Transport for Libraries › Subtitle Files. Every call runs under a captured
/// admin lease: the lease is checked before sending, after the response and
/// after every read, responses are size-bounded, and guarded writes never
/// refresh tokens or replay (the server marks them non-retryable).
/// </summary>
public partial class SiloApiClient
{
    /// <summary>Bound on any JSON body this domain reads (a 100-row page is a few hundred KiB).</summary>
    internal const int AdminSubtitleJsonLimit = 4 * 1024 * 1024;

    /// <summary>Sends one admin subtitle JSON request; a 2xx body is decoded within <see cref="AdminSubtitleJsonLimit"/>.</summary>
    internal async Task<ApiResponse<T>> AdminSubtitleSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, string? ifMatch = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        if (ifMatch != null) AdminValidators.RequireStrong(ifMatch);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            ifMatch == null ? null : new Dictionary<string, string> { ["If-Match"] = ifMatch });
        // Reads may refresh an expired access token; guarded writes are never re-sent.
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        var bytes = await AdminSubtitleReadBoundedAsync(response.Content, lease, AdminSubtitleJsonLimit, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty subtitle administration response.");
        lease.ThrowIfRetired();
        return new(value, response.Headers.ETag?.ToString());
    }

    /// <summary>A guarded DELETE expecting 204. A 404 is a failure here: the reviewed record is gone.</summary>
    internal async Task AdminSubtitleDeleteAsync(MonitorAuthorityLease lease, string path, string ifMatch, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        AdminValidators.RequireStrong(ifMatch);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Delete, path, null,
            new Dictionary<string, string> { ["If-Match"] = ifMatch });
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidDataException("The server did not confirm subtitle deletion.");
    }

    /// <summary>
    /// Reads a complete stored subtitle attachment into memory, within
    /// <paramref name="maxBytes"/>. Only a 200 with one of the declared subtitle
    /// media types is accepted; the declared length must match what arrived.
    /// Content-Disposition is never interpreted.
    /// </summary>
    internal async Task<byte[]> AdminSubtitleDownloadAsync(MonitorAuthorityLease lease, string path, int maxBytes, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Get, path, null);
        foreach (var accepted in AdminSubtitleDownloadMediaTypes) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accepted));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        if (response.StatusCode != HttpStatusCode.OK) throw new AdminSubtitleDownloadException(AdminSubtitleDownloadProblem.UnexpectedStatus, (int)response.StatusCode);
        var media = response.Content.Headers.ContentType?.MediaType?.Trim().ToLowerInvariant();
        if (media == null || !AdminSubtitleDownloadMediaTypes.Contains(media)) throw new AdminSubtitleDownloadException(AdminSubtitleDownloadProblem.UnexpectedType);
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes) throw new AdminSubtitleDownloadException(AdminSubtitleDownloadProblem.TooLarge);
        byte[] bytes;
        try { bytes = await AdminSubtitleReadBoundedAsync(response.Content, lease, maxBytes, linked.Token).ConfigureAwait(false); }
        catch (InvalidDataException) { throw new AdminSubtitleDownloadException(AdminSubtitleDownloadProblem.TooLarge); }
        lease.ThrowIfRetired();
        if (response.Content.Headers.ContentLength is { } expected && expected != bytes.Length)
            throw new AdminSubtitleDownloadException(AdminSubtitleDownloadProblem.Incomplete);
        return bytes;
    }

    /// <summary>The attachment media types the server declares for this operation (WebUI allow-list).</summary>
    internal static readonly IReadOnlyList<string> AdminSubtitleDownloadMediaTypes =
        ["application/x-subrip", "text/vtt", "text/x-ssa", "application/octet-stream"];

    private static async Task<byte[]> AdminSubtitleReadBoundedAsync(HttpContent content, MonitorAuthorityLease lease, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } length && length > maxBytes) throw new InvalidDataException("Subtitle administration response exceeds its size limit.");
        using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        using var target = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (read == 0) return target.ToArray();
            if (target.Length + read > maxBytes) throw new InvalidDataException("Subtitle administration response exceeds its size limit.");
            target.Write(buffer, 0, read);
        }
    }
}

public enum AdminSubtitleDownloadProblem { UnexpectedStatus, UnexpectedType, TooLarge, Incomplete }

/// <summary>A subtitle attachment that was refused before anything was published.</summary>
public sealed class AdminSubtitleDownloadException(AdminSubtitleDownloadProblem problem, int status = 0)
    : IOException($"The subtitle download was refused ({problem}).")
{
    public AdminSubtitleDownloadProblem Problem { get; } = problem;
    public int Status { get; } = status;
}
