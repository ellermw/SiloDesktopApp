using System.Text.Json;

namespace SiloPlayer.Core.Api;

/// <summary>Monitor-only administration endpoints. Pages remain subject to acting-admin authority.</summary>
public sealed partial class AdminMonitorApi(SiloApiClient client)
{
    private static Dictionary<string, string> ProjectEffectiveSettings(Dictionary<string, JsonElement> values,
        Func<string, bool> includeKey)
    {
        var projected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!includeKey(key)) continue;
            if (value.ValueKind == JsonValueKind.String) projected[key] = value.GetString()!;
            else if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                projected[key] = value.GetRawText();
        }
        return projected;
    }
}
