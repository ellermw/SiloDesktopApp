using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One library page a row can be copied to (id as the server's string, plus its library type).</summary>
public sealed record AdminHomeSectionLibraryPage(string Id, string Label, string? Type);

/// <summary>
/// Ports of the official Home rows rules (217ff25 web/src/lib/sectionLibraryFilter.ts, homeRows/bulkCopy.ts,
/// homeRows/variants.ts) so the native editor reads and writes the config exactly as the WebUI does.
/// </summary>
public static class AdminHomeSectionsRules
{
    public const int BulkLibraryLimit = 100;
    private static readonly HashSet<string> LibraryFilterTypes = new(StringComparer.Ordinal) { "recently_added", "recently_released" };

    /// <summary>Kinds whose settings belong to the page they were made on.</summary>
    private static readonly HashSet<string> PageBoundTypes = new(StringComparer.Ordinal)
    { "collection", "collection_posters", "custom_filter", "admin_curated_list", "genre", "award_winners" };

    /// <summary>A positive whole number in the node, or null (a string, fraction, zero or negative is not one).</summary>
    public static long? PositiveInteger(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var whole)) return whole > 0 ? whole : null;
        if (value.TryGetValue<double>(out var number) && number > 0 && number == Math.Floor(number) && number < long.MaxValue) return (long)number;
        return null;
    }

    private static List<long> PositiveIds(JsonNode? node)
    {
        var ids = new List<long>();
        if (node is not JsonArray array) return ids;
        foreach (var item in array)
            if (PositiveInteger(item) is { } id && !ids.Contains(id)) ids.Add(id);
        return ids;
    }

    private static List<long>? FlatFilterIds(JsonObject config)
    {
        var hasList = config["filter_library_ids"] is JsonArray { Count: > 0 };
        var ids = PositiveIds(config["filter_library_ids"]);
        long? single = null;
        if (config["filter_library_id"] is JsonValue value)
        {
            if (value.TryGetValue<long>(out var whole)) single = whole;
            else if (value.TryGetValue<double>(out var number) && number == Math.Floor(number) && Math.Abs(number) < long.MaxValue) single = (long)number;
        }
        var hasSingle = single != null;
        if (!hasList && !hasSingle) return null;
        if (single is long id && id > 0 && !ids.Contains(id)) ids.Add(id);
        return ids;
    }

    private static bool UsesQueryDefinitionShape(JsonObject config) => config["library_ids"] is JsonArray || config["media_scope"] is JsonValue { } v && v.TryGetValue<string>(out _);

    /// <summary>The library ids the backend filters a row to; empty means every library.</summary>
    public static IReadOnlyList<long> LibraryFilterIds(JsonObject config) => FlatFilterIds(config) ?? PositiveIds(config["library_ids"]);

    private static long? GeneratedOwnerId(JsonObject config)
    {
        if (config["generated_source"] is not JsonValue source || !source.TryGetValue<string>(out var text) || text == "") return null;
        foreach (var key in new[] { "generated_library_id", "filter_library_id" })
            if (PositiveInteger(config[key]) is { } id) return id;
        return null;
    }

    /// <summary>The config filtered to <paramref name="libraryIds"/> (empty means all libraries), as the WebUI writes it.</summary>
    public static JsonObject WithLibraryFilterIds(JsonObject config, IEnumerable<long> libraryIds)
    {
        var ids = libraryIds.Where(id => id > 0).Distinct().ToList();
        var next = (JsonObject)config.DeepClone();
        if (GeneratedOwnerId(config) is { } owner) next["generated_library_id"] = owner;
        if (FlatFilterIds(config) == null && UsesQueryDefinitionShape(config))
        {
            if (ids.Count > 0) next["library_ids"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
            else next.Remove("library_ids");
            return next;
        }
        next.Remove("filter_library_id");
        if (ids.Count > 0) next["filter_library_ids"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        else { next.Remove("filter_library_ids"); next.Remove("library_ids"); }
        return next;
    }

    /// <summary>Detaches a generated row only when its saved selection excludes its library.</summary>
    public static JsonObject FinalizeLibraryFilter(JsonObject config)
    {
        var copy = (JsonObject)config.DeepClone();
        var owner = GeneratedOwnerId(config); var ids = LibraryFilterIds(config);
        if (owner is { } id && ids.Count > 0 && !ids.Contains(id)) copy["generated_library_id"] = null;
        return copy;
    }

    /// <summary>The config an update sends: library-filter kinds finalize their generated owner; rule rows send their own config.</summary>
    public static JsonObject UpdateConfig(string sectionType, JsonObject config) =>
        sectionType is "collection" or "collection_posters" or "custom_filter" or "genre" ? (JsonObject)config.DeepClone()
        : LibraryFilterTypes.Contains(sectionType) ? FinalizeLibraryFilter(config) : (JsonObject)config.DeepClone();

    public static bool UsesLibraryFilter(string sectionType) => LibraryFilterTypes.Contains(sectionType);

    private static bool NamesLibrary(JsonObject config) => LibraryFilterIds(config).Count > 0 ||
        PositiveInteger(config["generated_library_id"]) != null || PositiveInteger(config["library_id"]) != null;

    /// <summary>Whether a row can be copied to other library pages as it is (the bulk create sends one config everywhere).</summary>
    public static bool CanCopyToLibraries(string sectionType, JsonObject config) => !PageBoundTypes.Contains(sectionType) && !NamesLibrary(config);

    private static string? LibraryKind(string? type)
    {
        var text = type?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text)) return null;
        return text switch
        {
            "movie" or "movies" => "movie", "series" or "tv" or "show" or "tvshows" => "tv", "audiobook" or "audiobooks" => "audiobook",
            "ebook" or "ebooks" => "ebook", "podcast" or "podcasts" => "podcast", _ => text
        };
    }

    private static string Lower(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim().ToLowerInvariant() : "";

    private static string? RowLibraryKind(string sectionType, JsonObject config)
    {
        if (config["media_scope"] is JsonValue scope && scope.TryGetValue<string>(out var mediaScope) && mediaScope != "") return "page";
        if (sectionType == "trending_discover")
            return config["filter_type"]?.ToString() == "movie" ? "movie" : config["filter_type"]?.ToString() == "series" ? "tv" : null;
        if (sectionType == "next_in_series") return "audiobook";
        if (sectionType != "continue_watching") return null;
        var continueType = Lower(config["continue_type"]);
        if (continueType == "listening") return "audiobook";
        if (continueType == "reading") return "ebook";
        if (continueType != "") return null;
        var filterType = Lower(config["filter_type"]);
        return filterType is "audiobook" or "ebook" ? filterType : null;
    }

    /// <summary>The library pages a row on page <paramref name="currentId"/> can go to, that page included.</summary>
    public static IReadOnlyList<AdminHomeSectionLibraryPage> CopyTargetPages(string sectionType, JsonObject config,
        IReadOnlyList<AdminHomeSectionLibraryPage> pages, string currentId)
    {
        var rowKind = RowLibraryKind(sectionType, config);
        if (rowKind == null) return pages.ToArray();
        var kind = rowKind == "page" ? LibraryKind(pages.FirstOrDefault(page => page.Id == currentId)?.Type) : rowKind;
        return pages.Where(page => page.Id == currentId || kind != null && LibraryKind(page.Type) == kind).ToArray();
    }

    /// <summary>The other library pages an Add row draft also goes to: none on Home, for a hero or page-bound row, never this page.</summary>
    public static IReadOnlyList<string> LibraryCopyIds(AdminHomeSectionDraft draft, AdminHomeSectionScope page,
        IReadOnlyList<AdminHomeSectionLibraryPage> pages, IEnumerable<string> requested)
    {
        if (page.Kind != "library" || page.LibraryId == null || draft.Featured || !CanCopyToLibraries(draft.SectionType, draft.Config)) return [];
        var fits = CopyTargetPages(draft.SectionType, draft.Config, pages, page.LibraryId).Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        return requested.Distinct(StringComparer.Ordinal).Where(id => id != page.LibraryId && fits.Contains(id)).ToArray();
    }

    // ── Seasonal holidays ────────────────────────────────────

    /// <summary>MM-DD with Feb 29 allowed (a leap-year day table), as the server reads a holiday boundary.</summary>
    public static bool ValidMonthDay(string? text) => text is { Length: 5 } && text[2] == '-' && text[..2].All(char.IsAsciiDigit) && text[3..].All(char.IsAsciiDigit) &&
        DateTime.TryParseExact("2000-" + text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    /// <summary>Why the server would refuse a built-in holiday's own dates, or null when both are complete.</summary>
    public static string? HolidayDatesProblem(string? start, string? end) =>
        ValidMonthDay(start) && ValidMonthDay(end) ? null : AdminHomeSectionsText.T("web.home.rows.seasonal.ownDatesInvalid");

    /// <summary>The server's ValidateMDBListURL rules: http(s), no userinfo, port 80/443 or none, mdblist.com, a /lists/ path.</summary>
    public static bool IsMdbListList(string? value)
    {
        var raw = value?.Trim() ?? "";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http")) return false;
        // Uri also accepts "https:/host" and "http:host"; the server needs the authority written out after "://".
        if (!raw.StartsWith(url.Scheme + "://", StringComparison.OrdinalIgnoreCase)) return false;
        var authority = raw[(url.Scheme.Length + 3)..].Split('/', '?', '#')[0];
        return !authority.Contains('@') && url.UserInfo.Length == 0 && url.Port is 80 or 443 && url.Host.TrimEnd('.').ToLowerInvariant() is "mdblist.com" or "www.mdblist.com" &&
            url.AbsolutePath.StartsWith("/lists/", StringComparison.Ordinal);
    }

    /// <summary>Why the server would refuse this custom holiday, or null when it is complete.</summary>
    public static string? CustomHolidayProblem(JsonObject holiday)
    {
        if (string.IsNullOrWhiteSpace(Text(holiday["name"]))) return AdminHomeSectionsText.T("web.home.rows.seasonal.nameMissing");
        if (!ValidMonthDay(Text(holiday["start"])) || !ValidMonthDay(Text(holiday["end"]))) return AdminHomeSectionsText.T("web.home.rows.seasonal.datesInvalid");
        return IsMdbListList(Text(holiday["list_url"])) ? null : AdminHomeSectionsText.T("web.home.rows.seasonal.listInvalid");
    }

    // ── Rule rows ────────────────────────────────────────────

    private static readonly string[] MediaScopes = ["movie", "series", "episode", "audiobook", "ebook", "manga"];

    private static long? WholeNumber(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var whole)) return whole;
        return value.TryGetValue<double>(out var number) && number == Math.Floor(number) && Math.Abs(number) < long.MaxValue ? (long)number : null;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// A rule row's config in the query-definition shape the rule editor saves (<c>withQueryDefinition(config, queryDefinitionFromSectionConfig(config))</c>):
    /// legacy filter_type, filter_library_id(s) and order fold in, and recipe metadata the rules do not own stays. Edits the object in place so
    /// the groups array an editor holds stays the same instance.
    /// </summary>
    public static void NormalizeQueryInPlace(JsonObject config)
    {
        var libraries = new List<long>();
        foreach (var key in new[] { "library_ids", "filter_library_ids" })
            if (config[key] is JsonArray list) foreach (var item in list) if (WholeNumber(item) is { } id && !libraries.Contains(id)) libraries.Add(id);
        if (WholeNumber(config["filter_library_id"]) is { } single && !libraries.Contains(single)) libraries.Add(single);
        string? scope = null;
        foreach (var name in MediaScopes) if (Text(config["media_scope"]) == name || Text(config["filter_type"]) == name) { scope = name; break; }
        scope ??= Text(config["media_scope"]) == "video" ? "video" : null;
        var match = Text(config["match"]) == "any" ? "any" : "all";
        var groups = config["groups"] as JsonArray ?? new JsonArray();
        foreach (var group in groups.OfType<JsonObject>()) group["match"] = Text(group["match"]) == "any" ? "any" : "all";
        string field, order;
        if (config["sort"] is JsonObject sort) { field = Text(sort["field"]) is { Length: > 0 } f ? f : "added_at"; order = Text(sort["order"]) is "asc" or "desc" ? Text(sort["order"])! : DefaultSortOrder(field); }
        else { field = Text(config["sort"]) is { Length: > 0 } f ? f : "added_at"; order = Text(config["order"]) is "asc" or "desc" ? Text(config["order"])! : DefaultSortOrder(field); }
        config.Remove("filter_type"); config.Remove("filter_library_id"); config.Remove("filter_library_ids"); config.Remove("order");
        config["library_ids"] = new JsonArray(libraries.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        if (scope == null) config.Remove("media_scope"); else config["media_scope"] = scope;
        config["match"] = match;
        if (!ReferenceEquals(config["groups"], groups)) config["groups"] = groups;
        config["sort"] = new JsonObject { ["field"] = field, ["order"] = order };
    }

    public static string DefaultSortOrder(string field) => field is "title" or "author" or "series" or "narrator" ? "asc" : "desc";

    // ── Variants ─────────────────────────────────────────────

    /// <summary>The preset key a row's config matches exactly, or null. Never a fallback.</summary>
    public static string? VariantOf(string sectionType, JsonObject config)
    {
        var family = AdminHomeSectionsKinds.Family(sectionType);
        if (family == null) return null;
        if (sectionType == "seasonal_themed")
        {
            if (config["enabled_themes"] is JsonArray themes)
                return themes.Count == 1 && themes[0]?.ToString() == "family_movie_night" ? "se_family_movie_night" : "se_auto";
            if (config["enabled_themes"] == null && config["theme"]?.ToString() == "family_movie_night") return "se_family_movie_night";
            if (config["enabled_themes"] == null && !(config["theme"] is JsonValue themeValue && themeValue.TryGetValue<string>(out _)) && config["custom_holidays"] is JsonArray) return "se_auto";
            return null;
        }
        if (sectionType == "editorial_spotlight")
            return config["subject_type"]?.ToString() switch
            {
                "director" => "es_director_auto", "actor" => "es_actor", "studio" => "es_studio",
                "era" => config["subject"]?.ToString() == "1980s" ? "es_era_80s" : null, _ => null
            };
        static bool Blank(JsonNode? node) => node == null || node is JsonValue value && value.TryGetValue<string>(out var text) && text == "";
        static bool Same(JsonNode? a, JsonNode? b) => Blank(a) && Blank(b) || !Blank(a) && !Blank(b) && JsonNode.DeepEquals(a, b);
        return family.Options.FirstOrDefault(option => family.Keys.All(key => Same(config[key], option.Values.GetValueOrDefault(key))))?.PresetKey;
    }
}
