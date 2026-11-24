using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi
{
    /// <summary>
    /// getWatchTrickplay: the seek-bar previews of <paramref name="fileId"/>, one of
    /// <paramref name="contentId"/>'s versions. Read only for a version whose
    /// <c>trickplay_available</c> is true. A reply for another account or profile
    /// than <paramref name="context"/> is discarded.
    /// </summary>
    public async Task<WatchTrickplayResponse> GetWatchTrickplayAsync(string contentId, int fileId, ApiRequestContext context,
        CancellationToken ct = default)
    {
        var path = $"/api/v2/watch/{Uri.EscapeDataString(contentId)}/trickplay?file_id="
            + fileId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var manifest = await client.SendRequestAsync<WatchTrickplayResponse>(context, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Seek preview context changed.", ct);
        return manifest;
    }
}
