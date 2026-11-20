using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed record AdminHomeSectionScope(string Kind = "home", string? LibraryId = null)
{
    public static AdminHomeSectionScope Home { get; } = new();
    public string Query
    {
        get
        {
            if (Kind == "home" && LibraryId == null) return "?scope=home";
            if (Kind == "library" && long.TryParse(LibraryId, out var id) && id > 0 && LibraryId!.All(char.IsAsciiDigit)) return "?scope=library&library_id=" + Uri.EscapeDataString(LibraryId);
            throw new ArgumentException("Invalid section page.");
        }
    }
    public bool Matches(AdminHomeSection row) => row.Scope == Kind && row.LibraryId == LibraryId;
}

public sealed record AdminHomeSectionSession(AdminHomeSection Row, string ETag, MonitorAuthorityLease Owner);
public sealed class AdminHomeSectionCapabilities
{
    public bool Available { get; set; }
    public bool Preview { get; set; }
    public bool ResetProfiles { get; set; }
    public bool WebOnly { get; set; }
}
public sealed class AdminHomeRuleCapabilities
{
    public bool ExtendedQueryRules { get; set; }
    public bool ShowTitleRules { get; set; }
    public bool FacetValueSearch { get; set; }
    public bool PeopleMediaScope { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string> ShownRatingSources { get; set; } = new HashSet<string>();
}
public sealed record AdminHomeRuleSuggestion(string Value, string Label);
public sealed class AdminHomeSectionCatalog { public List<AdminHomeRecipeGroup> Categories { get; set; } = []; }
public sealed class AdminHomeRecipeGroup { public string Category { get; set; } = ""; public List<AdminHomeRecipe> Recipes { get; set; } = []; }
public sealed class AdminHomeRecipe
{
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public bool AdminOnly { get; set; }
    public List<AdminHomePreset> Presets { get; set; } = [];
}
public sealed class AdminHomePreset
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DisplayNameKey { get; set; } = "";
    public string DescriptionShort { get; set; } = "";
    public JsonObject DefaultParams { get; set; } = new();
}
public sealed class AdminHomeSectionChoices { public List<AdminHomeSectionChoice> Items { get; set; } = []; public ApiPageInfo? Page { get; set; } }
public sealed class AdminHomeSectionChoice
{
    public string Id { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Name { get; set; } = "";
    public string CollectionType { get; set; } = "";
    public string Type { get; set; } = "";
    public string Visibility { get; set; } = "visible";
    public string PosterUrl { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Identity => string.IsNullOrEmpty(ContentId) ? Id : ContentId;
    public string Label => string.IsNullOrEmpty(Title) ? Name : Title;
}
public sealed class AdminHomeSectionPreview
{
    public List<AdminHomeSectionChoice> Items { get; set; } = [];
    public List<AdminHomeSectionChoice> Collections { get; set; } = [];
    public int TotalCount { get; set; }
}
public sealed class AdminHomeSeasonalCatalog
{
    public List<AdminHomeSeasonalTheme> Themes { get; set; } = [];
    public int MaxCustomHolidays { get; set; } = 10;
}
public sealed class AdminHomeSeasonalTheme
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string When { get; set; } = "";
    public bool DatesEditable { get; set; }
    public List<AdminHomeSeasonalVibe> Vibes { get; set; } = [];
}
public sealed class AdminHomeSeasonalVibe
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Editor-owned clone. Unknown saved config members survive a rename or an unchanged source.</summary>
public sealed class AdminHomeSectionDraft
{
    public static IReadOnlyList<string> VariantKeys(string type) => SiloPlayer.Core.Services.Admin.AdminHomeSectionsKinds.VariantKeys(type);
    /// <summary>Switches this row to another preset of its kind; only the family's discriminator keys change.</summary>
    public void ApplyVariant(string presetKey) => Config = SiloPlayer.Core.Services.Admin.AdminHomeSectionsKinds.ApplyVariant(SectionType, Config, presetKey);
    public bool CanCopy => SiloPlayer.Core.Services.Admin.AdminHomeSectionsRules.CanCopyToLibraries(SectionType, Config);
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The name is still the preset's, so picking another variant renames the row. Not part of any write.</summary>
    public bool TitleFollowsVariant { get; set; }
    public bool Featured { get; set; }
    public bool WebOnly { get; set; }
    public bool Enabled { get; set; } = true;
    public int ItemLimit { get; set; } = 20;
    public JsonObject Config { get; set; } = new();
    public static AdminHomeSectionDraft From(AdminHomeSection row) => new()
    {
        SectionType = row.SectionType, Title = row.Title, Featured = row.Featured, WebOnly = row.Featured && row.WebOnly,
        Enabled = row.Enabled, ItemLimit = row.ItemLimit,
        Config = row.Config.ValueKind == JsonValueKind.Object ? JsonNode.Parse(row.Config.GetRawText())!.AsObject() : new()
    };
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 500 || string.IsNullOrWhiteSpace(SectionType) || SectionType.Length > 100)
            throw new ArgumentException("Supply a row name of up to 500 characters.");
        if (ItemLimit is < 1 or > 500) throw new ArgumentException("The number of titles must be between 1 and 500.");
        if (Featured && SectionType == "collection_posters") throw new ArgumentException("A row of collections cannot be a hero banner.");
        if (SectionType == "collection" && string.IsNullOrWhiteSpace(Config["library_collection_id"]?.ToString()))
            throw new ArgumentException("Choose a collection.");
        if (SectionType == "collection_posters" && (Config["collections"] is not JsonArray { Count: > 0 and <= 30 } refs ||
            refs.Any(entry => entry is not JsonObject obj || string.IsNullOrWhiteSpace(obj["library_collection_id"]?.ToString()) || obj.ContainsKey("user_collection_id"))))
            throw new ArgumentException("Choose between 1 and 30 server collections.");
        if (SectionType == "admin_curated_list" && Config["item_ids"] is not JsonArray { Count: > 0 and <= 500 })
            throw new ArgumentException("Add at least one title.");
        if (SectionType == "seasonal_themed") ValidateSeasonal();
        if (Config.ToJsonString().Length > 128 * 1024) throw new ArgumentException("Row settings exceed their size limit.");
    }
    /// <summary>
    /// The write body. A create (<paramref name="saved"/> null) sends the config as drafted; an update sends the library-filter
    /// finalized config, the saved <c>enabled</c>, and <c>web_only</c> only when a hero's choice differs from the saved row, as the WebUI does.
    /// </summary>
    public Dictionary<string, object?> Body(bool webOnlySupported, AdminHomeSection? saved = null)
    {
        var result = new Dictionary<string, object?>
        {
            ["section_type"] = SectionType, ["title"] = Title.Trim(), ["featured"] = Featured,
            ["enabled"] = saved?.Enabled ?? Enabled, ["item_limit"] = ItemLimit,
            ["config"] = saved == null ? Config.DeepClone() : SiloPlayer.Core.Services.Admin.AdminHomeSectionsRules.UpdateConfig(SectionType, Config)
        };
        if (webOnlySupported && Featured && WebOnly != (saved != null && saved.Featured && saved.WebOnly)) result["web_only"] = WebOnly;
        return result;
    }
    private void ValidateSeasonal()
    {
        var custom = Config["custom_holidays"] as JsonArray;
        if (Config["enabled_themes"] is JsonArray { Count: 0 } && custom is not { Count: > 0 }) throw new ArgumentException(SiloPlayer.Core.Services.Admin.AdminHomeSectionsText.T("web.home.addRow.seasonal.pickOne"));
        if (Config["theme_dates"] is JsonObject windows)
            foreach (var entry in windows)
            {
                if (entry.Value is not JsonObject dates) throw new ArgumentException(SiloPlayer.Core.Services.Admin.AdminHomeSectionsText.T("web.home.rows.seasonal.ownDatesInvalid"));
                if (SiloPlayer.Core.Services.Admin.AdminHomeSectionsRules.HolidayDatesProblem(dates["start"]?.ToString(), dates["end"]?.ToString()) is { } problem) throw new ArgumentException(problem);
            }
        if (custom == null) return;
        foreach (var entry in custom.OfType<JsonObject>())
            if (SiloPlayer.Core.Services.Admin.AdminHomeSectionsRules.CustomHolidayProblem(entry) is { } problem) throw new ArgumentException(problem);
    }
}
