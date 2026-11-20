using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Whole-source update and source-editor validation rules from sourceForm.ts.</summary>
public static class AdminAutoscanSourceDraft
{
    public static string DefaultDelivery(AdminAutoscanDescriptor? descriptor) =>
        descriptor?.DeliveryModes.Contains("poll") != false ? "poll" : descriptor.DeliveryModes.FirstOrDefault() ?? "poll";

    public static bool ConnectionRequired(AdminAutoscanDescriptor? descriptor, string deliveryMode) =>
        deliveryMode != "webhook" && descriptor?.Connection == "required";

    public static bool MatchesConnection(AdminAutoscanDescriptor? descriptor, string kind) =>
        descriptor == null || descriptor.ConnectionKinds.Count == 0 || descriptor.ConnectionKinds.Contains(kind);

    public static bool TryInterval(string value, out int? seconds)
    {
        seconds = null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return true;
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed) || parsed < 1 || parsed > int.MaxValue || Math.Truncate(parsed) != parsed) return false;
        seconds = (int)parsed;
        return true;
    }

    public static AdminAutoscanSourceWrite FromSource(AdminAutoscanSource source, bool enabled) => new()
    {
        ConnectionId = source.ConnectionId,
        Enabled = enabled,
        DeliveryMode = source.DeliveryMode,
        PollIntervalSeconds = source.PollIntervalSeconds,
        // Trimmed, complete rewrites only: the shape the server stores (sourceUpdateBody / cleanRewrites).
        PathRewrites = source.PathRewrites.Select(row => new AdminAutoscanPathRewrite { From = row.From.Trim(), To = row.To.Trim() }).Where(row => row.From.Length > 0 && row.To.Length > 0).ToList(),
        SourceConfig = new(source.SourceConfig),
        Label = source.Label,
    };

    public static bool ValidMappings(IReadOnlyList<AdminAutoscanPathRewrite> rows, bool webhook, bool hadMappings, bool creating)
    {
        var usable = 0;
        foreach (var row in rows)
        {
            var from = !string.IsNullOrWhiteSpace(row.From);
            var to = !string.IsNullOrWhiteSpace(row.To);
            // A new webhook source starts with seeded rows that only carry the Silo side; until touched they are suggestions, not unfinished input.
            if (from != to && !(creating && webhook)) return false;
            if (from) usable++;
        }
        return !webhook || usable > 0 || (!creating && !hadMappings);
    }
}
