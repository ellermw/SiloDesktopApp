using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiloPlayer.Services;

/// <summary>
/// Pure staging adapters for the Admin Appearance overlay editor (official
/// AppearanceSettings.tsx at Silo Server 66b836f82). They hold no service state,
/// never touch the viewer's profile and never write settings.
/// </summary>
public partial class CardOverlayService
{
    /// <summary>
    /// The official BUILT_IN_OVERLAY_DEFAULTS: serializeOverlayPrefs(buildDefaultPrefs()),
    /// derived from the registry so a new overlay is covered without a literal.
    /// </summary>
    internal static string BuiltInEditorDefaults => SerializePrefs(BuildDefaultDocument());

    internal static CardOverlayPrefs ParseEditorDocument(string? document) =>
        string.IsNullOrWhiteSpace(document) ? BuildDefaultDocument() : ParsePrefs(document) ?? BuildDefaultDocument();

    /// <summary>
    /// The official overlayDefaultsAreBuiltIn: the stored document (or the built-in one when
    /// unset) reads as exactly the built-in configuration. Fields the editor does not
    /// understand do not count, as the official parser drops them too.
    /// </summary>
    internal static bool IsBuiltInEditorDocument(string? document) =>
        SerializePrefs(ParseEditorDocument(document) with { OriginalDocument = null }) == BuiltInEditorDefaults;

    /// <summary>
    /// The staged defaults.card_overlays text. The server stores this admin default as
    /// any JSON (internal/config NormalizeAdminSetting), so the whole canonical document
    /// is written whatever the settings manifest revision, as the official page does.
    /// Only edited fields change: fields, items and values this editor does not
    /// understand survive, and a null style or emptied value map really removes them.
    /// </summary>
    // The official editor writes with JSON.stringify, which leaves "+" and non-ASCII text as they are; the default .NET encoder escapes them.
    private static readonly JsonSerializerOptions EditorJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static string SerializeEditorDocument(CardOverlayPrefs draft)
    {
        var canonical = JsonNode.Parse(SerializePrefs(draft with { OriginalDocument = null }))!.AsObject();
        if (draft.OriginalDocument is not { ValueKind: JsonValueKind.Object } original) return canonical.ToJsonString(EditorJson);
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
            root["version"] = 2;
            root["preset"] = canonical["preset"]?.DeepClone();
            root["order"] = canonical["order"]?.DeepClone();
            root["items"] = migrated;
        }
        var items = root["items"]!.AsObject();
        foreach (var definition in OverlayRegistry.All)
        {
            var before = previous.Items[definition.Id];
            var after = draft.Items[definition.Id];
            var value = canonical["items"]![definition.Id]!.AsObject();
            // An item the stored document never had is written whole, as the official
            // serializer writes every registry item.
            if (items[definition.Id] is not JsonObject)
            {
                items[definition.Id] = value.DeepClone();
                continue;
            }
            void Patch(string key, bool changed)
            {
                if (!changed) return;
                var item = (JsonObject)items[definition.Id]!;
                if (value.TryGetPropertyValue(key, out var field)) item[key] = field?.DeepClone();
                else item.Remove(key);
            }
            Patch("enabled", before.Enabled != after.Enabled);
            Patch("position", before.Position != after.Position);
            Patch("accentColor", before.AccentColor != after.AccentColor);
            Patch("showIcon", before.ShowIcon != after.ShowIcon);
            Patch("image", before.Image != after.Image);
            if (!SameValueImages(before.ValueImages, after.ValueImages))
            {
                var item = (JsonObject)items[definition.Id]!;
                // Preserve opaque value-map entries, changing only string assignments.
                var map = item["valueImages"] as JsonObject ?? new JsonObject();
                foreach (var key in before.ValueImages?.Keys ?? Enumerable.Empty<string>())
                    if (after.ValueImages?.ContainsKey(key) != true) map.Remove(key);
                foreach (var pair in after.ValueImages ?? new Dictionary<string, string>()) map[pair.Key] = pair.Value;
                if (map.Count == 0) item.Remove("valueImages"); else item["valueImages"] = map;
            }
        }
        if (draft.Preset != previous.Preset) root["preset"] = canonical["preset"]?.DeepClone();
        if (!draft.Order.SequenceEqual(previous.Order)) root["order"] = canonical["order"]?.DeepClone();
        if (draft.Style != previous.Style)
        {
            if (draft.Style is null) root.Remove("style");
            else
            {
                draft.Style.WriteTo(root);
                if (root["style"] is JsonObject { Count: 0 }) root.Remove("style");
            }
        }
        return root.ToJsonString(EditorJson);
    }

    private static bool SameValueImages(IReadOnlyDictionary<string, string>? first, IReadOnlyDictionary<string, string>? second) =>
        (first?.Count ?? 0) == (second?.Count ?? 0) && (first is null || first.All(pair => second?.GetValueOrDefault(pair.Key) == pair.Value));

    /// <summary>
    /// The official isOverlayOffered: a rating badge only while the server shows its
    /// source. The admin page offers every other registry overlay at any manifest revision.
    /// </summary>
    internal static bool EditorOffersOverlay(string id, IReadOnlySet<string> shownRatingSources) => id switch
    {
        "rating_rt" => shownRatingSources.Contains("rt_critic"),
        "rating_rt_audience" => shownRatingSources.Contains("rt_audience"),
        _ => true,
    };
}
