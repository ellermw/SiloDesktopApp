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
    public static IReadOnlyList<string> VariantKeys(string type) => type switch
    {
        "continue_watching" => ["continue_type"], "trending_on_server" or "most_watched" => ["window"], "trending_discover" => ["source", "window"],
        "mood_collection" => ["mood"], "seasonal_themed" => ["enabled_themes", "theme"], "editorial_spotlight" => ["subject_type", "subject"],
        "format_showcase" => ["format", "sort"], _ => []
    };
    public void ApplyVariant(AdminHomePreset preset)
    {
        if (SectionType == "seasonal_themed")
        {
            Config.Remove("theme");
            if (preset.Key == "se_family_movie_night")
            { Config.Remove("custom_holidays"); Config.Remove("theme_dates"); Config["enabled_themes"] = new JsonArray("family_movie_night"); }
            else Config["enabled_themes"] = new JsonArray("halloween", "christmas", "new_year", "valentines", "st_patricks", "easter", "thanksgiving", "summer_blockbuster", "saturday_morning");
            return;
        }
        if (SectionType == "editorial_spotlight")
        {
            var rotating = Config["auto_rotate"]?.ToString() == "true" && Config["subject_type"]?.ToString() is "director" or "actor" or "studio";
            Config.Remove("subject");
            if (preset.Key == "es_era_80s") { Config.Remove("auto_rotate"); Config.Remove("rotation_cadence"); }
            else if (!rotating) { Config["auto_rotate"] = true; Config["rotation_cadence"] = "weekly"; }
        }
        foreach (var key in VariantKeys(SectionType)) { Config.Remove(key); if (preset.DefaultParams[key] != null) Config[key] = preset.DefaultParams[key]!.DeepClone(); }
    }
    public bool CanCopy => SectionType is not ("collection" or "collection_posters" or "custom_filter" or "admin_curated_list" or "genre" or "award_winners") &&
        !new[] { "filter_library_ids", "filter_library_id", "library_ids", "generated_library_id", "library_id" }.Any(key => Config[key] is JsonArray { Count: > 0 } || int.TryParse(Config[key]?.ToString(), out var id) && id > 0);
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public bool WebOnly { get; set; }
    public bool Enabled { get; set; } = true;
    public int ItemLimit { get; set; } = 20;
    public JsonObject Config { get; set; } = new();
    public static AdminHomeSectionDraft From(AdminHomeSection row) => new()
    {
        SectionType = row.SectionType, Title = row.Title, Featured = row.Featured, WebOnly = row.WebOnly,
        Enabled = row.Enabled, ItemLimit = row.ItemLimit > 0 ? row.ItemLimit : 20,
        Config = row.Config.ValueKind == JsonValueKind.Object ? JsonNode.Parse(row.Config.GetRawText())!.AsObject() : new()
    };
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 500 || string.IsNullOrWhiteSpace(SectionType) || SectionType.Length > 100 ||
            ItemLimit is < 1 or > 500) throw new ArgumentException("Supply a row name and between 1 and 500 titles.");
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
    public Dictionary<string, object?> Body(bool webOnlySupported)
    {
        var result = new Dictionary<string, object?>
        {
            ["section_type"] = SectionType, ["title"] = Title.Trim(), ["featured"] = Featured,
            ["enabled"] = Enabled, ["item_limit"] = ItemLimit, ["config"] = Config.DeepClone()
        };
        if (webOnlySupported) result["web_only"] = Featured && WebOnly;
        return result;
    }
    private void ValidateSeasonal()
    {
        var custom = Config["custom_holidays"] as JsonArray;
        if (Config["enabled_themes"] is JsonArray { Count: 0 } && custom is not { Count: > 0 }) throw new ArgumentException("Pick at least one holiday or add your own.");
        if (Config["theme_dates"] is JsonObject windows)
            foreach (var entry in windows) if (entry.Value is not JsonObject dates || !MonthDay(dates["start"]?.ToString()) || !MonthDay(dates["end"]?.ToString()))
                throw new ArgumentException("Enter complete holiday dates as MM-DD.");
        if (custom == null) return;
        foreach (var entry in custom)
        {
            if (entry is not JsonObject holiday || string.IsNullOrWhiteSpace(holiday["name"]?.ToString()) || !MonthDay(holiday["start"]?.ToString()) || !MonthDay(holiday["end"]?.ToString()))
                throw new ArgumentException("Each holiday needs a name and valid MM-DD dates.");
            var raw = holiday["list_url"]?.ToString()?.Trim() ?? "";
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || url.UserInfo.Length != 0 || raw.Split('/').ElementAtOrDefault(2)?.Contains('@') == true ||
                url.Host.TrimEnd('.').ToLowerInvariant() is not ("mdblist.com" or "www.mdblist.com") || url.Port is not (80 or 443) || !url.AbsolutePath.StartsWith("/lists/", StringComparison.Ordinal))
                throw new ArgumentException("Use a public MDBList list URL for each holiday.");
        }
    }
    private static bool MonthDay(string? text) => text is { Length: 5 } && text[2] == '-' &&
        DateTime.TryParseExact("2000-" + text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
}
