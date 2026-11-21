using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal Task<AdminAppearanceAsset> AdminAppearanceUploadAsync(MonitorAuthorityLease lease, string kind, byte[] bytes,
        string mediaType, CancellationToken cancellation) => AdminLibraryWithinDeadlineAsync(lease, cancellation, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Post, "/api/v2/admin/branding/assets/" + kind, null);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, "file", "branding-image");
        request.Content = form;
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
            await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
        var body = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibraryJsonLimit, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return JsonSerializer.Deserialize<AdminAppearanceAsset>(body, JsonOptions)
            ?? throw new InvalidDataException("Empty image assignment acknowledgement.");
    }, AdminAppearanceUploadDeadline(bytes.Length));

    /// <summary>
    /// The web upload has no client deadline. Here an upload gets the usual 30 seconds plus 10 seconds per MiB, so a large image on a slow
    /// uplink is not cut off and then reported as an unknown outcome while the server stores it.
    /// </summary>
    internal static TimeSpan AdminAppearanceUploadDeadline(int bytes) => TimeSpan.FromSeconds(30 + 10 * Math.Ceiling(Math.Max(0, bytes) / (1024d * 1024d)));
}
