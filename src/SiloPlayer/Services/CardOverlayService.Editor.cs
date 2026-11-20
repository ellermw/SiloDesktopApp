using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiloPlayer.Services;

public partial class CardOverlayService
{
    // Pure staging adapters: no service state, account changes or settings writes.
    internal static string BuiltInEditorDefaults => SerializePrefs(BuildDefaultDocument());
    internal static string BuiltInEditorDefaultsForRevision(int revision) => SerializeForRevision(BuildDefaultDocument(), revision);

    internal static CardOverlayPrefs ParseEditorDocument(string document) => ParsePrefs(document) ?? BuildDefaultDocument();

    /// <summary>
    /// Uses the renderer's canonical schema, applying only edited fields to a stored
    /// document. Opaque newer values, unknown items and unedited legacy positions survive.
    /// A null style explicitly resets the look; an empty value map clears its assignments.
    /// </summary>
    internal static string SerializeEditorDocument(CardOverlayPrefs draft, int manifestRevision)
    {
        var canonical = JsonNode.Parse(SerializeForRevision(draft, manifestRevision))!.AsObject();
        if (draft.OriginalDocument is not { ValueKind: JsonValueKind.Object } original) return canonical.ToJsonString();
        var previous = ParsePrefs(original.GetRawText())!;
        var root = (JsonObject)ToNode(original)!;
        // A legacy flat document is migrated once, keeping opaque properties and
        // carrying the legacy item attributes into the canonical items container.
        if (root["items"] is not JsonObject)
        {
            var migrated = (JsonObject)canonical["items"]!.DeepClone();
            foreach (var pair in root.ToArray())
                if (pair.Value is JsonObject entry && (OverlayRegistry.All.Any(definition => definition.Id == pair.Key) ||
                    entry.ContainsKey("enabled") || entry.ContainsKey("position")))
                {
                    migrated[pair.Key] = entry.DeepClone();
                    root.Remove(pair.Key);
                }
            root["version"] = draft.Version;
            root["preset"] = canonical["preset"]?.DeepClone();
            root["order"] = canonical["order"]?.DeepClone();
            root["items"] = migrated;
        }
        var items = root["items"]!.AsObject();
        foreach (var definition in OverlayRegistry.All)
        {
            if (manifestRevision < IntroducedInRevision(definition.Id)) continue;
            var before = previous.Items[definition.Id];
            var after = draft.Items[definition.Id];
            var value = canonical["items"]![definition.Id]!.AsObject();
            void Patch(string key, bool changed)
            {
                if (!changed) return;
                var item = items[definition.Id] as JsonObject ?? new JsonObject();
                if (value.TryGetPropertyValue(key, out var field)) item[key] = field?.DeepClone();
                else item.Remove(key);
                items[definition.Id] = item;
            }
            Patch("enabled", before.Enabled != after.Enabled);
            Patch("position", before.Position != after.Position);
            Patch("accentColor", before.AccentColor != after.AccentColor);
            Patch("showIcon", before.ShowIcon != after.ShowIcon);
            if (manifestRevision < 25) continue;
            Patch("image", before.Image != after.Image);
            if (!SameValueImages(before.ValueImages, after.ValueImages))
            {
                var item = items[definition.Id] as JsonObject ?? new JsonObject();
                // Preserve opaque value-map entries, changing only string assignments.
                var map = item["valueImages"] as JsonObject ?? new JsonObject();
                foreach (var key in before.ValueImages?.Keys ?? Enumerable.Empty<string>())
                    if (after.ValueImages?.ContainsKey(key) != true) map.Remove(key);
                foreach (var pair in after.ValueImages ?? new Dictionary<string, string>()) map[pair.Key] = pair.Value;
                if (map.Count == 0) item.Remove("valueImages"); else item["valueImages"] = map;
                items[definition.Id] = item;
            }
        }
        if (draft.Preset != previous.Preset && (manifestRevision >= 25 || draft.Preset is not ("plate" or "band")))
            root["preset"] = canonical["preset"]?.DeepClone();
        if (!draft.Order.SequenceEqual(previous.Order)) root["order"] = canonical["order"]?.DeepClone();
        if (manifestRevision >= 25 && draft.Style != previous.Style)
        {
            if (draft.Style is null) root.Remove("style");
            else
            {
                draft.Style.WriteTo(root);
                if (root["style"] is JsonObject { Count: 0 }) root.Remove("style");
            }
        }
        return root.ToJsonString();
    }

    private static bool SameValueImages(IReadOnlyDictionary<string, string>? first, IReadOnlyDictionary<string, string>? second) =>
        (first?.Count ?? 0) == (second?.Count ?? 0) && (first is null || first.All(pair => second?.GetValueOrDefault(pair.Key) == pair.Value));

    internal static bool EditorOffersOverlay(string id, int revision, IReadOnlySet<string> shownRatingSources) =>
        revision >= IntroducedInRevision(id) && (id switch
        {
            "rating_rt" => shownRatingSources.Contains("rt_critic"),
            "rating_rt_audience" => shownRatingSources.Contains("rt_audience"),
            _ => true,
        });
}
