using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>Whole-second editing preserves untouched detector provenance and explicit marker removals.</summary>
public static class MarkerEditPlan
{
    public static readonly string[] Kinds = ["intro", "recap", "credits", "preview"];
    public static string Label(string kind) => kind switch { "intro" => "Intro", "recap" => "Recap", "credits" => "Credits / Outro", "preview" => "Preview", _ => throw new ArgumentException("Unknown marker kind.") };
    public static double? Edge(JsonElement marker, string edge)
        => marker.ValueKind == JsonValueKind.Object && marker.TryGetProperty(edge + "_seconds", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    public static string Format(double? seconds)
    {
        if (seconds == null) return "";
        var total = Math.Max(0, Math.Floor(seconds.Value + .5));
        var hours = Math.Floor(total / 3600); var minutes = Math.Floor(total / 60) % 60; var remainder = total % 60;
        return hours > 0 ? $"{hours:0}:{minutes:00}:{remainder:00}" : $"{Math.Floor(total / 60):0}:{remainder:00}";
    }
    public static double? Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Trim().Split(':'); if (parts.Length > 3) throw new FormatException();
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0) throw new FormatException();
            total = total * 60 + number;
        }
        if (!double.IsFinite(total)) throw new FormatException();
        return total;
    }
    public static Dictionary<string, object?> Build(JsonElement original, IReadOnlyDictionary<string, (string Start, string End)> fields)
    {
        var body = new Dictionary<string, object?>();
        foreach (var kind in Kinds)
        {
            if (!fields.TryGetValue(kind, out var field)) continue;
            var marker = original.ValueKind == JsonValueKind.Object && original.TryGetProperty(kind, out var segment) ? segment : default;
            var oldStart = Edge(marker, "start"); var oldEnd = Edge(marker, "end");
            if (string.IsNullOrWhiteSpace(field.Start) && string.IsNullOrWhiteSpace(field.End)) { if (oldStart != null || oldEnd != null) body[kind] = null; continue; }
            double? start, end;
            try { start = Parse(field.Start); end = Parse(field.End); }
            catch (FormatException) { throw new ArgumentException($"{Label(kind)}: enter both start and end (e.g. 1:30)."); }
            if (start == null || end == null) throw new ArgumentException($"{Label(kind)}: enter both start and end (e.g. 1:30).");
            if (end <= start) throw new ArgumentException($"{Label(kind)}: end must be after start.");
            if (oldStart != null && oldEnd != null && Math.Floor(oldStart.Value + .5) == start && Math.Floor(oldEnd.Value + .5) == end) continue;
            body[kind] = new Dictionary<string, double> { ["start_seconds"] = start.Value, ["end_seconds"] = end.Value };
        }
        return body;
    }
}
