using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    private readonly SemaphoreSlim _audioPreferenceWrites = new(1, 1);

    public Task SetAudioPrefsAsync(ApiRequestContext owner, string preferenceId, AudioPreference request, CancellationToken ct = default)
        => WriteAudioPreferenceAsync(owner, HttpMethod.Put, preferenceId, request, ct);

    public Task DeleteAudioPrefsAsync(ApiRequestContext owner, string preferenceId, CancellationToken ct = default)
        => WriteAudioPreferenceAsync(owner, HttpMethod.Delete, preferenceId, null, ct);

    private async Task WriteAudioPreferenceAsync(ApiRequestContext owner, HttpMethod method,
        string preferenceId, AudioPreference? request, CancellationToken ct)
    {
        await _audioPreferenceWrites.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await client.SendNoContentRequestAsync(owner, method,
                $"/api/v2/audio-prefs/{Uri.EscapeDataString(preferenceId)}", request, ct).ConfigureAwait(false);
            if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Audio preference owner changed.", ct);
        }
        finally { _audioPreferenceWrites.Release(); }
    }
}
