using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SettingsApi
{
    public async Task<MarkerDetailsPreferences> GetMarkerDetailsPreferencesAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var capability = await GetContractCapabilitiesAsync(context, ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Marker settings owner changed.", ct);
        if (!MarkerDetailsPreferences.Supports(capability)) return new();
        var response = await GetContractEffectiveSettingsAsync(context, MarkerDetailsPreferences.Keys, ct: ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Marker settings owner changed.", ct);
        return MarkerDetailsPreferences.Read(capability, response);
    }
}
