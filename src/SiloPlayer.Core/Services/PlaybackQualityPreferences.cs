using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>A captured profile/device quality intent shared by source selection and session creation.</summary>
public sealed record PlaybackQualityPreferences(ApiRequestContext Context, string Quality, int? BandwidthCapKbps)
{
    public string BufferingQualityMode { get; init; } = "never";
    internal (ApiIdentityContext Owner, PlaybackCapabilityV3 Capability)? CapabilitySnapshot { get; init; }
    public static IReadOnlyList<PlaybackQualityV3> MenuOptions(IEnumerable<PlaybackQualityV3> advertised)
    {
        var fixedOptions = advertised.Where(option => !option.PreservesSource &&
            option.Label.ToLowerInvariant() is not ("auto" or "original" or "source" or "max")).ToArray();
        return fixedOptions.Length == 0 ? [] : new[] { new PlaybackQualityV3 { Label = "auto", DisplayName = "Auto", PreservesSource = true } }.Concat(fixedOptions).ToArray();
    }
    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "original" or "source" or "max" or "auto" => "auto",
            "2160p" or "4k" or "uhd" => "2160p",
            "1080p" or "fhd" => "1080p",
            "720p" or "hd" => "720p",
            "480p" or "sd" => "480p",
            "480p-low" or "360p" => normalized,
            "2160p-high" or "2160p-medium" or "2160p-low" or
            "1080p-high" or "1080p-medium" or "1080p-low" or
            "720p-high" or "720p-medium" or "720p-low" => normalized,
            _ => "auto",
        };
    }

    public static async Task<PlaybackQualityPreferences> ReadAsync(SiloApiClient client, string? fallback, CancellationToken ct, bool includeBuffering = false,
        ApiRequestContext? expectedContext = null, bool metered = false)
    {
        var context = expectedContext ?? client.CaptureContext();
        var quality = Normalize(fallback);
        int? cap = null;
        var buffering = "never";
        try
        {
            var settings = new SettingsApi(client);
            if (metered)
            {
                try
                {
                    var capability = await settings.GetContractCapabilitiesAsync(context, ct).ConfigureAwait(false);
                    metered = capability.ApiVersion == 1 && capability.Revision >= 20 && capability.SupportsBatchedEffective;
                }
                // HttpClient timeouts are cancellation-shaped, but do not cancel playback.
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && ex.InnerException is TimeoutException)
                { metered = false; }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not ApiException { StatusCode: 401 or 403 })
                {
                    ct.ThrowIfCancellationRequested();
                    // This auxiliary probe must not prevent playback with the main settings.
                    metered = false;
                }
            }
            var qualityKey = metered ? "playback.cellular_preferred_quality" : "playback.preferred_quality";
            var bitrateKey = metered ? "playback.cellular_max_bitrate_kbps" : "playback.max_bitrate_kbps";
            if (metered) { quality = "720p"; cap = 2000; }
            ContractEffectiveSettingsResponse response;
            try { response = await settings.GetContractEffectiveSettingsAsync(context,
                includeBuffering ? [qualityKey, bitrateKey, "playback.buffering_quality_mode"] :
                    [qualityKey, bitrateKey], ct: ct).ConfigureAwait(false); }
            catch (ApiException ex) when (includeBuffering && ex.StatusCode is not (401 or 403))
            { response = await settings.GetContractEffectiveSettingsAsync(context,
                [qualityKey, bitrateKey], ct: ct).ConfigureAwait(false); }
            var preferred = response.Settings.FirstOrDefault(entry => entry.Key == qualityKey);
            if (preferred?.Value.ValueKind == JsonValueKind.String) quality = Normalize(preferred.Value.GetString());
            var bandwidth = response.Settings.FirstOrDefault(entry => entry.Key == bitrateKey);
            if (bandwidth?.Value.ValueKind == JsonValueKind.Null) cap = null;
            if (bandwidth?.Value.ValueKind == JsonValueKind.Number && bandwidth.Value.TryGetInt32(out var value) && value is >= 100 and <= 200000)
                cap = value;
            var mode = response.Settings.FirstOrDefault(entry => entry.Key == "playback.buffering_quality_mode");
            if (mode?.Value.ValueKind == JsonValueKind.String && mode.Value.GetString() is "always" or "ask" or "never") buffering = mode.Value.GetString()!;
        }
        catch (ApiException ex) when (metered && ex.StatusCode == 422)
        {
            // If cellular keys are unavailable despite the advertised contract, use main settings.
            return await ReadAsync(client, fallback, ct, includeBuffering, context, metered: false).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501)
        {
            // Older servers without effective settings retain their profile preference.
            quality = Normalize(fallback);
            cap = null;
        }
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Playback quality owner changed.", ct);
        return new(context, quality, cap) { BufferingQualityMode = buffering };
    }
}
