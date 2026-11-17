using System.Net.Http.Headers;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>A bounded Backups response: status, raw body and the headers the contract names.</summary>
internal sealed record AdminBackupWireResponse(int Status, byte[] Body, string? Location, string? ETag)
{
    public override string ToString() => nameof(AdminBackupWireResponse);
}

/// <summary>A Backups problem that keeps the response's <c>Location</c> (the active restore job of a 409).</summary>
public sealed class AdminBackupApiException : ApiException
{
    internal AdminBackupApiException(ApiException inner, string? location)
        : base(inner.ErrorCode, inner.Message, inner.StatusCode, inner.ErrorLocation, inner.RetryAfterSeconds)
    {
        ProblemType = inner.ProblemType; ProblemTitle = inner.ProblemTitle; ProblemDetail = inner.ProblemDetail;
        FieldErrors = inner.FieldErrors; Location = location;
    }
    public string? Location { get; }
}

public partial class SiloApiClient
{
    /// <summary>
    /// One lease-bound Backups request. Commands are never authenticated again or resent;
    /// only an explicitly idempotent operation may pass <paramref name="allowTokenRefresh"/>.
    /// The body (success or problem) is read under <paramref name="maxBytes"/>.
    /// </summary>
    internal async Task<AdminBackupWireResponse> AdminBackupSendAsync(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body, CancellationToken ct, int maxBytes = 16 * 1024 * 1024, bool allowTokenRefresh = false)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, method, path, body);
        return await AdminBackupExchangeAsync(lease, request, linked.Token, maxBytes, allowTokenRefresh).ConfigureAwait(false);
    }

    /// <summary>One upload part as <c>application/octet-stream</c>. The part PUT is naturally idempotent, so a refreshed token may resend it.</summary>
    internal async Task<AdminBackupWireResponse> AdminBackupPutPartAsync(MonitorAuthorityLease lease, string path, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Put, path, null);
        request.Content = new ReadOnlyMemoryContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await AdminBackupExchangeAsync(lease, request, linked.Token, 1024 * 1024, allowTokenRefresh: true).ConfigureAwait(false);
    }

    private async Task<AdminBackupWireResponse> AdminBackupExchangeAsync(MonitorAuthorityLease lease, HttpRequestMessage request,
        CancellationToken ct, int maxBytes, bool allowTokenRefresh)
    {
        try
        {
            using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh).ConfigureAwait(false);
            lease.ThrowIfRetired();
            var location = response.Headers.Location?.OriginalString;
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, maxBytes, ct).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
            {
                response.Content.Dispose();
                response.Content = new ByteArrayContent(bytes);
                try { await ThrowApiException(response, request, ct).ConfigureAwait(false); }
                catch (ApiException error) { throw new AdminBackupApiException(error, location); }
                throw new InvalidDataException("Backup request failed without a problem.");
            }
            return new((int)response.StatusCode, bytes, location, response.Headers.ETag?.ToString());
        }
        finally { lease.ThrowIfRetired(); }
    }
}
