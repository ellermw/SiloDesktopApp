using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public sealed record MarkerDetailsPreferences(bool Supported = false, bool ShowConfidence = false, bool ShowSource = false)
{
    public const string ConfidenceKey = "playback.show_marker_confidence";
    public const string SourceKey = "playback.show_marker_source";
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { ConfidenceKey, SourceKey });
    public static bool Supports(SettingsContractCapabilities capability) => capability.ApiVersion == 1 && capability.Revision >= 24
        && capability.SupportsBatchedEffective && capability.SupportsIdempotentWrites;

    public static MarkerDetailsPreferences Read(SettingsContractCapabilities capability, ContractEffectiveSettingsResponse response)
    {
        if (!Supports(capability) || !Keys.All(key => response.Settings.Any(item => item.Key == key &&
            item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False))) return new();
        bool Enabled(string key) => response.Settings.First(item => item.Key == key).Value.GetBoolean();
        return new(true, Enabled(ConfidenceKey), Enabled(SourceKey));
    }
}
