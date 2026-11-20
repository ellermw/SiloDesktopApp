using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Draft construction and naming rules of the official Add row / Edit row form (217ff25 web/src/lib/homeRows/rowDraft.ts and payloads.ts).</summary>
public static class AdminHomeSectionsDraftFactory
{
    public const int DefaultItemLimit = 20;
    public const int MaxFormItemLimit = 100;

    public static AdminHomeRecipe? FindRecipe(AdminHomeSectionCatalog? catalog, string sectionType) =>
        catalog?.Categories.SelectMany(group => group.Recipes).FirstOrDefault(recipe => recipe.Type == sectionType);

    private static bool IsRuleKind(string type) => type is "custom_filter" or "genre";

    /// <summary>A new row from a picked preset: exactly the preset's params. A collection row is named after its collection once one is picked; a rule row starts with no rules in the shape the rule editor saves.</summary>
    public static AdminHomeSectionDraft ForPreset(AdminHomeRecipe def, AdminHomePreset? preset)
    {
        var config = preset?.DefaultParams.DeepClone().AsObject() ?? new JsonObject();
        var collection = def.Type == "collection";
        var name = def.Type == "collection_posters" ? AdminHomeSectionsKinds.Name(def.Type) : preset?.DisplayName is { Length: > 0 } display ? display : AdminHomeSectionsKinds.Name(def.Type);
        if (IsRuleKind(def.Type)) AdminHomeSectionsRules.NormalizeQueryInPlace(config);
        return new()
        {
            SectionType = def.Type, Title = collection ? "" : name, Config = config, ItemLimit = DefaultItemLimit, Featured = false, Enabled = true,
            TitleFollowsVariant = collection || AdminHomeSectionsKinds.Family(def.Type) != null
        };
    }

    /// <summary>An existing row as the form starts it. Opening a row never changes its config; the item limit is the raw saved one.</summary>
    public static AdminHomeSectionDraft FromRow(AdminHomeSection row, AdminHomeSectionCatalog? catalog)
    {
        var draft = AdminHomeSectionDraft.From(row);
        var name = AdminHomeSectionsKinds.PresetName(catalog, row.SectionType, AdminHomeSectionsRules.VariantOf(row.SectionType, draft.Config));
        draft.TitleFollowsVariant = name != null && name == row.Title;
        return draft;
    }

    /// <summary>The collection a collection row's config points at, or "".</summary>
    public static string CollectionIdOf(JsonObject config) => config["library_collection_id"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : "";

    /// <summary>
    /// Picks the collection a collection row shows: both id keys are replaced by <c>library_collection_id</c> and every other key stays. The name follows the pick
    /// while it is blank, still following, or still the old collection's name.
    /// </summary>
    public static void WithCollection(AdminHomeSectionDraft draft, string id, string title, string? currentTitle = null)
    {
        var follows = draft.TitleFollowsVariant || draft.Title.Trim() == "" || currentTitle != null && draft.Title == currentTitle;
        if (CollectionIdOf(draft.Config) != id) { draft.Config.Remove("user_collection_id"); draft.Config["library_collection_id"] = id; }
        if (follows) { draft.Title = title; draft.TitleFollowsVariant = true; }
    }

    /// <summary>Picks another variant; the name follows it until the admin typed their own.</summary>
    public static void WithVariant(AdminHomeSectionDraft draft, AdminHomeSectionCatalog? catalog, string presetKey)
    {
        var name = AdminHomeSectionsKinds.PresetName(catalog, draft.SectionType, presetKey);
        draft.ApplyVariant(presetKey);
        if (draft.TitleFollowsVariant && name != null) draft.Title = name;
    }

    public static void WithTitle(AdminHomeSectionDraft draft, string title) { draft.Title = title; draft.TitleFollowsVariant = false; }

    /// <summary>The name a save sends: the typed name, the fallback (a collection row's collection), the variant's preset name, then the kind's plain name, never a raw type key.</summary>
    public static string SavedTitle(AdminHomeSectionDraft draft, AdminHomeSectionCatalog? catalog, string? fallback = null)
    {
        var typed = draft.Title.Trim() is { Length: > 0 } text ? text : fallback?.Trim();
        if (!string.IsNullOrEmpty(typed)) return typed;
        var def = FindRecipe(catalog, draft.SectionType);
        var variant = AdminHomeSectionsRules.VariantOf(draft.SectionType, draft.Config);
        return AdminHomeSectionsKinds.PresetName(catalog, draft.SectionType, variant) ?? (def?.Presets.Count == 1 ? def.Presets[0].DisplayName : null) ?? AdminHomeSectionsKinds.Name(draft.SectionType);
    }

    /// <summary>Why a draft has nothing to preview yet, or null.</summary>
    public static string? PreviewWaitText(AdminHomeSectionDraft draft)
    {
        if (draft.SectionType == "collection" && CollectionIdOf(draft.Config) == "") return AdminHomeSectionsText.T("web.home.rows.draft.pickCollection");
        if (draft.SectionType == "admin_curated_list" && draft.Config["item_ids"] is not JsonArray { Count: > 0 }) return AdminHomeSectionsText.T("web.home.rows.draft.addTitles");
        if (draft.SectionType == "collection_posters" && AdminHomeSectionsDescription.CollectionIds(draft.Config).Count == 0) return AdminHomeSectionsText.T("web.home.rows.draft.addCollection");
        return null;
    }

    // ── Reload after a stale save ────────────────────────────

    private static readonly string[] DraftFields = ["title", "shows", "itemLimit", "hero"];

    /// <summary>Canonical JSON with object keys in ordinal order, so equal configs compare equal whatever their key order.</summary>
    public static string StableJson(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => System.Text.Json.JsonSerializer.Serialize(pair.Key) + ":" + StableJson(pair.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(StableJson)) + "]",
        _ => node.ToJsonString()
    };

    private static string FieldValue(AdminHomeSectionDraft draft, string field) => field switch
    {
        "title" => draft.Title, "shows" => draft.SectionType + "\n" + StableJson(draft.Config), "itemLimit" => draft.ItemLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => draft.Featured + ":" + draft.WebOnly
    };
    private static List<string> ChangedFields(AdminHomeSectionDraft a, AdminHomeSectionDraft b) => DraftFields.Where(field => FieldValue(a, field) != FieldValue(b, field)).ToList();
    private static void CopyField(AdminHomeSectionDraft target, AdminHomeSectionDraft source, string field)
    {
        switch (field)
        {
            case "title": target.Title = source.Title; target.TitleFollowsVariant = source.TitleFollowsVariant; break;
            case "shows": target.SectionType = source.SectionType; target.Config = (JsonObject)source.Config.DeepClone(); break;
            case "itemLimit": target.ItemLimit = source.ItemLimit; break;
            default: target.Featured = source.Featured; target.WebOnly = source.WebOnly; break;
        }
    }

    /// <summary>
    /// After a save is refused because the row changed elsewhere: every field the admin changed keeps their value, every other field takes the row's new value
    /// (applied to <paramref name="edited"/>). Returns the fields that changed elsewhere, including those where the admin's value won.
    /// </summary>
    public static IReadOnlyList<string> MergeReloaded(AdminHomeSectionDraft original, AdminHomeSectionDraft edited, AdminHomeSectionDraft upstream)
    {
        var userChanged = ChangedFields(original, edited).ToHashSet(StringComparer.Ordinal);
        foreach (var field in DraftFields) if (!userChanged.Contains(field)) CopyField(edited, upstream, field);
        return ChangedFields(original, upstream);
    }
}
