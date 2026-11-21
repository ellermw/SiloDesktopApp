using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Bounded, lease-owned transport for Manage > Downloads > Prepared Files.</summary>
public partial class SiloApiClient
{
    internal const int AdminPreparedFilesJsonLimit = 4 * 1024 * 1024;

    internal async Task<ApiResponse<T>> AdminPreparedFilesSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, string? ifMatch = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        if (ifMatch != null) AdminValidators.RequireStrong(ifMatch);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct, deadline.Token);
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            ifMatch == null ? null : new Dictionary<string, string> { ["If-Match"] = ifMatch });
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                throw new InvalidDataException("The server answered without the expected prepared-file administration body.");
            var bytes = await ReadBoundedAsync(response.Content, lease, linked.Token).ConfigureAwait(false);
            var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Empty prepared-file administration response.");
            lease.ThrowIfRetired();
            return new(value, response.Headers.ETag?.ToString());
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested && !lease.Token.IsCancellationRequested)
        {
            throw new TaskCanceledException("Prepared-file administration request exceeded its deadline.",
                new TimeoutException("Prepared-file administration request timed out.", error), linked.Token);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, MonitorAuthorityLease lease, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } length && length > AdminPreparedFilesJsonLimit)
            throw new InvalidDataException("Prepared-file administration response exceeds its size limit.");
        using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var target = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (read == 0) return target.ToArray();
            if (target.Length + read > AdminPreparedFilesJsonLimit)
                throw new InvalidDataException("Prepared-file administration response exceeds its size limit.");
            target.Write(buffer, 0, read);
        }
    }
}
