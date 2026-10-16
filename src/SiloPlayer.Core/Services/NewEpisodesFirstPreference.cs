using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public sealed record NewEpisodesFirstSnapshot(ApiRequestContext Context, bool Supported, bool Value, string Source);

/// <summary>One profile's server-ordered Home preference, available from manifest18.</summary>
public sealed class NewEpisodesFirstPreference(SettingsApi api)
{
    public const string Key = "ui.next_up_new_releases_first";

    public async Task<NewEpisodesFirstSnapshot> LoadAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        EnsureCurrent(context, ct);
        SettingsContractCapabilities? capabilities;
        try { capabilities = await api.GetContractCapabilitiesAsync(context, ct); }
        catch (ApiException ex) when (ex.StatusCode == 404) { capabilities = null; }
        EnsureCurrent(context, ct);
        if (capabilities is not { ApiVersion: 1, Revision: >= 18 }) return new(context, false, false, "default");
        var response = await api.GetContractEffectiveSettingsAsync(context, [Key], ct: ct);
        EnsureCurrent(context, ct);
        var entry = response.Settings.FirstOrDefault(item => item.Key == Key);
        return new(context, true, entry?.Value.ValueKind == JsonValueKind.True, entry?.Source ?? "default");
    }

    public async Task SaveAsync(NewEpisodesFirstSnapshot snapshot, bool value, CancellationToken ct = default)
    {
        EnsureCurrent(snapshot.Context, ct);
        if (!snapshot.Supported) throw new NotSupportedException("This server does not support the Home preference.");
        await api.SetContractSettingValueAsync(snapshot.Context, Key, "profile", value, ct: ct);
        EnsureCurrent(snapshot.Context, ct);
    }

    private void EnsureCurrent(ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!api.IsCurrentContext(context)) throw new OperationCanceledException("The Home preference owner changed.", ct);
    }
}
