using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Transport for Manage › Libraries › Libraries. Every call runs under a
/// captured admin lease, checked before sending, after the response and after
/// every read; bodies are size-bounded. Only reads may refresh an expired
/// access token: every write is sent once (the server marks them
/// non_retryable, coalescing or natural_idempotent, and the WebUI sends them
/// with <c>retryAuthentication: false</c>), so an ambiguous outcome surfaces
/// to the caller instead of being replayed.
/// </summary>
public partial class SiloApiClient
{
    /// <summary>Bound on any JSON body this domain reads (a 200-row diagnostics page is well below 1 MiB).</summary>
    internal const int AdminLibraryJsonLimit = 4 * 1024 * 1024;

    /// <summary>Sends one admin library JSON request; a 2xx body is decoded within <see cref="AdminLibraryJsonLimit"/>.</summary>
    internal Task<T> AdminLibrarySendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, object? body = null,
        CancellationToken ct = default) => AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, method, path, body);
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        if (response.StatusCode == HttpStatusCode.NoContent) throw new InvalidDataException("The server answered without the expected library administration body.");
        var bytes = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibraryJsonLimit, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty library administration response.");
        lease.ThrowIfRetired();
        return value;
    });

    /// <summary>Reads a same-origin poster through the authenticated, lease-bound transport, never through the image decoder.</summary>
    internal Task<byte[]> AdminLibraryGetPosterAsync(MonitorAuthorityLease lease, string posterUrl, CancellationToken ct = default) =>
        AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
        {
            lease.ThrowIfRetired();
            var context = lease.Context;
            if (!Uri.TryCreate(context.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin)
                || origin.Scheme is not ("http" or "https")
                || string.IsNullOrWhiteSpace(posterUrl)
                || !Uri.TryCreate(origin, posterUrl, out var uri)
                || uri.Scheme != origin.Scheme || !string.Equals(uri.Authority, origin.Authority, StringComparison.OrdinalIgnoreCase)
                || uri.UserInfo.Length != 0)
                throw new ArgumentException("Library posters must use the connected server origin.", nameof(posterUrl));
            using var request = CreateContextBoundRequest(context, HttpMethod.Get, "", null);
            request.RequestUri = uri;
            using var response = await SendWithRetryAsync(request, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
                await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
            var bytes = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibrariesApi.MaxPosterBytes, token).ConfigureAwait(false);
            if (bytes.Length == 0) throw new InvalidDataException("The library poster is empty.");
            return bytes;
        });

    /// <summary>A write whose only success is 204 No Content (reorder, overrides, ignore, providers, poster removal).</summary>
    internal Task AdminLibrarySendNoContentAsync(MonitorAuthorityLease lease, HttpMethod method, string path, object? body = null,
        CancellationToken ct = default) => AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, method, path, body);
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidDataException("The server did not confirm the library change.");
        return true;
    });

    /// <summary>
    /// <c>PUT /api/v2/libraries/{id}/poster</c> as <c>multipart/form-data</c> with one
    /// <c>poster</c> part. The bytes are read by the caller within the server's 10 MiB limit.
    /// </summary>
    internal Task<T> AdminLibraryPutPosterAsync<T>(MonitorAuthorityLease lease, string path, byte[] poster, string mediaType,
        string fileName, CancellationToken ct = default) => AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Put, path, null);
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(poster);
        part.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(part, "poster", fileName);
        request.Content = form;
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
        }
        var bytes = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibraryJsonLimit, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty library administration response.");
    });

    private static async Task<T> AdminLibraryWithinDeadlineAsync<T>(MonitorAuthorityLease lease, CancellationToken ct, Func<CancellationToken, Task<T>> operation, TimeSpan? limit = null)
    {
        using var deadline = new CancellationTokenSource(limit ?? TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct, deadline.Token);
        try
        {
            var result = await operation(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            lease.ThrowIfRetired();
            return result;
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested && !lease.Token.IsCancellationRequested)
        {
            throw new TaskCanceledException("Library administration request exceeded its deadline.", new TimeoutException("Library administration request timed out.", error), linked.Token);
        }
    }

    private async Task AdminLibraryThrowBoundedErrorAsync(HttpResponseMessage response, HttpRequestMessage request, MonitorAuthorityLease lease, CancellationToken ct)
    {
        using var original = response.Content;
        var bytes = await AdminLibraryReadBoundedAsync(original, lease, AdminLibraryJsonLimit, ct).ConfigureAwait(false);
        response.Content = new ByteArrayContent(bytes);
        await ThrowApiException(response, request, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> AdminLibraryReadBoundedAsync(HttpContent content, MonitorAuthorityLease lease, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } length && length > maxBytes) throw new InvalidDataException("Library administration response exceeds its size limit.");
        using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        using var target = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (read == 0) return target.ToArray();
            if (target.Length + read > maxBytes) throw new InvalidDataException("Library administration response exceeds its size limit.");
            target.Write(buffer, 0, read);
        }
    }
}
