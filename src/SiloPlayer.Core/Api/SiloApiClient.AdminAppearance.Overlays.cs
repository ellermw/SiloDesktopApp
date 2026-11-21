using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    // One small receipt, never an entire library or arbitrary server body.
    private const int OverlayImageReceiptLimit = 16 * 1024;

    internal Task<OverlayLibraryImage> AdminAppearanceUploadOverlayAsync(MonitorAuthorityLease lease, byte[] bytes,
        string mediaType, string name, CancellationToken cancellation) => AdminLibraryWithinDeadlineAsync(lease, cancellation, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Post, "/api/v2/admin/overlay-images", null);
        var form = new MultipartFormDataContent();
        request.Content = form;
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, "file", "badge-image");
        form.Add(new StringContent(name), "name");
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
            await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidDataException("The server did not confirm the badge image upload.");
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new InvalidDataException("Invalid badge image acknowledgement content.");
        var body = await AdminLibraryReadBoundedAsync(response.Content, lease, OverlayImageReceiptLimit, token).ConfigureAwait(false);
        var receipt = JsonSerializer.Deserialize<OverlayImageReceipt>(body, JsonOptions)
            ?? throw new InvalidDataException("Empty badge image acknowledgement.");
        var image = receipt.Image;
        if (!IsOverlayImageRef(image.Ref) || image.Name != name || image.Width is < 1 or > 600 || image.Height is < 1 or > 300 ||
            receipt.Url != "/api/v2/overlay-images/" + image.Ref ||
            response.Headers.Location?.OriginalString != "/api/v2/admin/overlay-images/" + image.Ref)
            throw new InvalidDataException("The badge image upload was not acknowledged.");
        lease.ThrowIfRetired();
        return image;
    }, AdminAppearanceUploadDeadline(bytes.Length));

    private sealed class OverlayImageReceipt
    {
        public string Ref { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public OverlayLibraryImage Image => new() { Ref = Ref, Name = Name, Width = Width, Height = Height };
    }
}
