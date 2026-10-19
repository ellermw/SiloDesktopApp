using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>A captured profile/device quality intent shared by source selection and session creation.</summary>
public sealed record PlaybackQualityPreferences(ApiRequestContext Context, string Quality, int? BandwidthCapKbps)
{
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
            "2160p-high" or "2160p-medium" or "2160p-low" or
            "1080p-high" or "1080p-medium" or "1080p-low" or
            "720p-high" or "720p-medium" or "720p-low" => normalized,
            _ => "auto",
        };
    }

    internal static async Task<PlaybackQualityPreferences> ReadAsync(SiloApiClient client, string? fallback, CancellationToken ct)
    {
        var context = client.CaptureContext();
        var quality = Normalize(fallback);
        int? cap = null;
        try
        {
            var response = await new SettingsApi(client).GetContractEffectiveSettingsAsync(context,
                ["playback.preferred_quality", "playback.max_bitrate_kbps"], ct: ct).ConfigureAwait(false);
            var preferred = response.Settings.FirstOrDefault(entry => entry.Key == "playback.preferred_quality");
            if (preferred?.Value.ValueKind == JsonValueKind.String) quality = Normalize(preferred.Value.GetString());
            var bandwidth = response.Settings.FirstOrDefault(entry => entry.Key == "playback.max_bitrate_kbps");
            if (bandwidth?.Value.ValueKind == JsonValueKind.Number && bandwidth.Value.TryGetInt32(out var value) && value is >= 100 and <= 200000)
                cap = value;
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501) { /* Older servers retain their profile preference. */ }
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Playback quality owner changed.", ct);
        return new(context, quality, cap);
    }
}
