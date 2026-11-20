using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>Policy samples are diagnostic data, never an alternative way to display credentials.</summary>
public static class AdminAccessRedaction
{
    public static string Json(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) return "—";
        var node = JsonNode.Parse(value.GetRawText());
        if (node is JsonValue text && text.TryGetValue<string>(out var literal)) node = JsonValue.Create(SafeText(literal)); else Scrub(node);
        return LocalLog.RedactDiagnosticText(node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");
    }
    private static void Scrub(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var (key, value) in obj.ToArray())
            {
                var normalized = key.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
                if (normalized.Contains("password") || normalized.Contains("secret") || normalized.Contains("token") || normalized.Contains("apikey") || normalized is "authorization" or "cookie" or "setcookie") obj[key] = "<redacted>";
                else if (value is JsonValue text && text.TryGetValue<string>(out var literal))
                {
                    obj[key] = SafeText(literal);
                }
                else Scrub(value);
            }
        else if (node is JsonArray array)
            for (var index = 0; index < array.Count; index++)
                if (array[index] is JsonValue text && text.TryGetValue<string>(out var literal)) array[index] = SafeText(literal); else Scrub(array[index]);
    }
    private static string SafeText(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        ? new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path)
        : LocalLog.RedactDiagnosticText(text);
}
