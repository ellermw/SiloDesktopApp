using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi
{
    private readonly SemaphoreSlim _subtitlePreferenceWrites = new(1, 1);

    public Task SaveSubtitlePrefsAsync(ApiRequestContext owner, string preferenceId, SubtitlePreferenceRequest request, CancellationToken ct = default)
        => WriteSubtitlePreferenceAsync(owner, HttpMethod.Put, preferenceId, BuildSubtitlePreferenceBody(request), ct);

    public Task DeleteSubtitlePrefsAsync(ApiRequestContext owner, string preferenceId, CancellationToken ct = default)
        => WriteSubtitlePreferenceAsync(owner, HttpMethod.Delete, preferenceId, null, ct);

    private async Task WriteSubtitlePreferenceAsync(ApiRequestContext owner, HttpMethod method,
        string preferenceId, Dictionary<string, object?>? body, CancellationToken ct)
    {
        await _subtitlePreferenceWrites.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await client.SendNoContentRequestAsync(owner, method,
                $"/api/v2/subtitle-prefs/{Uri.EscapeDataString(preferenceId)}", body, ct).ConfigureAwait(false);
            if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Subtitle preference owner changed.", ct);
        }
        finally { _subtitlePreferenceWrites.Release(); }
    }
}
