using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    private static readonly HttpRequestOptionsKey<bool> AdminAutoscanSensitiveOption = new("Silo.AdminAutoscanSensitive");
    // Autoscan writes have no replay identity or ETag precondition. Admit exactly
    // the documented HTTP acknowledgement; a generic 2xx is insufficient.
    internal Task<T> AdminAutoscanSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        HttpStatusCode expected, object? body = null, CancellationToken ct = default) =>
        AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
        {
            lease.ThrowIfRetired();
            if (body != null && JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions).Length > 64 * 1024)
                throw new ArgumentException("The source configuration exceeds 64 KiB.", nameof(body));
            using var request = CreateContextBoundRequest(lease.Context, method, path, body);
            request.Options.Set(AdminAutoscanSensitiveOption, true);
            using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
                await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
            if (response.StatusCode != expected) throw new InvalidDataException("Unexpected Autoscan acknowledgement.");
            if (expected == HttpStatusCode.NoContent) return default!;
            var bytes = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibraryJsonLimit, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty Autoscan response.");
        });
}
