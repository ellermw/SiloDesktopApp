using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public sealed class HomeLayoutLibrary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}
public sealed class HomeLayoutPage
{
    public string Scope { get; set; } = "home";
    public int? LibraryId { get; set; }
    public List<SectionOverride> Overrides { get; set; } = [];
    /// <summary>
    /// Opaque server-keyed tokens for every server row on this page at export time (official
    /// <c>server_section_tokens</c>, #4648). Null on an export from before tokens existed and on a
    /// page whose baseline could not be read; ignored on cross-server imports.
    /// </summary>
    [JsonConverter(typeof(HomeLayoutTokenListConverter))] public List<string>? ServerSectionTokens { get; set; }
    /// <summary>The importing server's name for this page; set on planned pages only.</summary>
    [JsonIgnore] public string Label { get; set; } = "";
    /// <summary>
    /// False on a same-server page whose every file override was skipped: checked rows to hide are merged
    /// into the destination's existing overrides without clearing unrelated customizations.
    /// </summary>
    [JsonIgnore] public bool ReplaceExisting { get; set; } = true;
}
public sealed class HomeLayoutFile
{
    public string Format { get; set; } = "silo-home-layout";
    public int Version { get; set; } = 1;
    public string ExportedAt { get; set; } = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    public string ServerId { get; set; } = "";
    public List<HomeLayoutLibrary> Libraries { get; set; } = [];
    public bool? HideWatchedItems { get; set; }
    public List<HomeLayoutPage> Pages { get; set; } = [];
}
public sealed record HomeLayoutTarget(string ServerId, IReadOnlyList<HomeLayoutLibrary> Libraries,
    IReadOnlyDictionary<string, bool> RecipeAdminOnly, bool AllowAdminOnly, IReadOnlySet<string> Collections, IReadOnlySet<string> Profiles);

public enum HomeLayoutSkipReason { UnknownRecipe, CustomDisabled, Library, Collection, Profile }
public sealed record HomeLayoutSkippedSection(string Page, string Title, HomeLayoutSkipReason Reason)
{
    public string ReasonText => HomeLayoutTransfer.ReasonText(Reason);
    public override string ToString() => $"{Title} ({Page}): {ReasonText}";
}

public sealed class HomeLayoutPlan
{
    public bool SameServer { get; init; }
    /// <summary>Pages this import saves, labelled with the importing server's names.</summary>
    public List<HomeLayoutPage> Pages { get; } = [];
    /// <summary>
    /// Same-server pages whose library matched where every file override was skipped. Saved only if
    /// the viewer leaves a server row checked to hide on them, which keeps their existing overrides.
    /// </summary>
    public List<HomeLayoutPage> UnappliedPages { get; } = [];
    /// <summary>Library pages with no matching library here, by their exported name.</summary>
    public List<string> SkippedPages { get; } = [];
    public List<HomeLayoutSkippedSection> SkippedSections { get; } = [];
    /// <summary>Changes to the exporting server's own sections, which another server doesn't have.</summary>
    public int SkippedServerSectionChanges { get; set; }
    /// <summary>Everything this import leaves out, as one line each.</summary>
    public IReadOnlyList<string> Skipped => [.. SkippedPages.Select(HomeLayoutTransfer.NoLibraryMatchesText),
        .. SkippedSections.Select(row => row.ToString()),
        .. Enumerable.Repeat(HomeLayoutTransfer.ServerChangesText(1), SkippedServerSectionChanges)];
    public bool? HideWatchedItems { get; init; }
    /// <summary>Server rows added since the export that the preview offers to hide (same-server pages with tokens).</summary>
    public List<HomeLayoutHideOption> HideOptions { get; } = [];
    /// <summary>How many pages carried tokens; a row's name shows its page when more than one did.</summary>
    public int PagesWithTokens { get; set; }
    /// <summary>Unapplied pages that have at least one row checked to hide; the import saves these too.</summary>
    public IEnumerable<HomeLayoutPage> CheckedUnappliedPages => UnappliedPages.Where(page => HideIds(page).Count > 0);
    public bool HasChanges => Pages.Count > 0 || CheckedUnappliedPages.Any() || HideWatchedItems.HasValue;
    /// <summary>The checked rows to hide on a page, in the order offered.</summary>
    public List<string> HideIds(HomeLayoutPage page)
    {
        var key = HomeLayoutBaseline.PageKey(page.Scope, page.LibraryId);
        return HideOptions.Where(option => option.Checked && option.PageKey == key).Select(option => option.Row.Id).ToList();
    }
}

public static class HomeLayoutTransfer
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    /// <summary>The most overrides one page's save accepts (SectionOverrideSet in the v2 contract).</summary>
    public const int MaximumPageOverrides = 500;
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };

    /// <summary>Catalog text for a transfer line; <paramref name="english"/> is the official English, already filled in, shown when the interface language has no message.</summary>
    public static string T(string key, string english, params (string Name, object? Value)[] arguments)
        => AppStrings.Get(key, arguments.Length == 0 ? null : arguments.ToDictionary(pair => pair.Name, pair => pair.Value), english);

    /// <summary>"N changes to sections defined by the other server's admin don't apply here."</summary>
    public static string ServerChangesText(int count)
        => T("web.home.layout.skippedServerChanges", count == 1 ? "1 change to a section defined by the other server's admin doesn't apply here."
            : $"{count} changes to sections defined by the other server's admin don't apply here.", ("count", count));
    public static string NoLibraryMatchesText(string libraries)
        => T("web.home.layout.noLibraryMatches", $"No library here matches: {libraries}.", ("libraries", libraries));

    public static string ReasonText(HomeLayoutSkipReason reason) => reason switch
    {
        HomeLayoutSkipReason.UnknownRecipe => T("web.home.layout.transfer.skip.unknownRecipe", "This server doesn't offer this section type"),
        HomeLayoutSkipReason.CustomDisabled => T("web.home.layout.transfer.skip.customDisabled", "Only an admin can add this kind of row"),
        HomeLayoutSkipReason.Library => T("web.home.layout.transfer.skip.library", "Uses a library this profile doesn't have here"),
        HomeLayoutSkipReason.Collection => T("web.home.layout.transfer.skip.collection", "Uses a collection this profile can't see here"),
        _ => T("web.home.layout.transfer.skip.profile", "Follows a profile outside this account"),
    };

    public static string Serialize(HomeLayoutFile file)
    {
        var text = JsonSerializer.Serialize(file, Json);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException(T("web.home.layout.transfer.tooLargeToExport", "This home layout is too large to export. Remove some custom sections and try again."));
        return text;
    }

    /// <summary>
    /// The write shape of a stored override, without its ID and timestamps (official overrideToWrite):
    /// a flag is written only when set, text only when non-empty.
    /// </summary>
    public static SectionOverride ToWrite(RawSectionOverride row)
    {
        var write = HomeSectionWritePolicy.FromRaw(row);
        write.Id = null;
        if (write.Hidden != true) write.Hidden = null;
        if (write.Removed != true) write.Removed = null;
        if (write.IsUserAdded != true) write.IsUserAdded = null;
        return write;
    }

    private static InvalidDataException Invalid(string key, string english) => new(T(key, english));
    public static HomeLayoutFile Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw Invalid("web.home.layout.tooLarge", "This file is too large to be a home layout export.");
        JsonDocument document;
        try { document = JsonDocument.Parse(text); }
        catch (JsonException) { throw Invalid("web.home.layout.transfer.invalidJson", "This isn't valid JSON."); }
        using var disposable = document;
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "silo-home-layout")
            throw Invalid("web.home.layout.transfer.notExport", "This isn't a Silo home layout export.");
        if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetDouble(out var number) && number > 1)
            throw Invalid("web.home.layout.transfer.newerVersion", "This export comes from a newer version of Silo.");
        InvalidDataException Damaged() => Invalid("web.home.layout.transfer.damaged", "This home layout export is incomplete or damaged.");
        HomeLayoutFile file;
        try { file = JsonSerializer.Deserialize<HomeLayoutFile>(text, Json) ?? throw Damaged(); }
        catch (JsonException) { throw Damaged(); }
        if (!root.TryGetProperty("version", out _) ||
            !root.TryGetProperty("server_id", out var serverId) || serverId.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array ||
            pages.EnumerateArray().Any(page => page.ValueKind != JsonValueKind.Object ||
                !page.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String ||
                !page.TryGetProperty("overrides", out var overrides) || overrides.ValueKind != JsonValueKind.Array) ||
            file.Format != "silo-home-layout" || file.Version != 1 ||
            file.Pages == null || file.Libraries == null || file.Libraries.Any(library => library == null || library.Id <= 0 ||
                library.Name == null || library.Type == null) || file.Pages.Any(page => page == null || page.Scope is not ("home" or "library") ||
                page.Scope == "library" && page.LibraryId is not > 0 || page.Overrides == null || page.Overrides.Any(row => row == null)) ||
            file.Pages.GroupBy(page => (page.Scope, page.LibraryId)).Any(group => group.Count() > 1))
            throw Damaged();
        return file;
    }
    private static Dictionary<string, object>? CloneConfig(Dictionary<string, object>? config)
        => config == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(config));
    private static string? Text(Dictionary<string, object>? config, string key)
        => config?.TryGetValue(key, out var value) == true ? value?.ToString() : null;
    private static bool Id(object? value, out int id)
    {
        id = 0;
        return value is int number && (id = number) > 0 || value is JsonElement { ValueKind: JsonValueKind.Number } json && json.TryGetInt32(out id) && id > 0;
    }
    private static object[] Values(object? value) => value switch
    { JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray().Select(e => (object)e.Clone()).ToArray(),
      System.Collections.IEnumerable list when value is not string => list.Cast<object>().ToArray(), _ => [] };

    /// <summary>
    /// The collections a row of collections lists, in order (official collectionRefsOf, #4410): each entry names
    /// one server (<c>library_collection_id</c>) or personal (<c>user_collection_id</c>) collection. Entries it
    /// cannot read are skipped.
    /// </summary>
    public static IReadOnlyList<(bool Personal, string Id)> CollectionRefs(Dictionary<string, object>? config)
    {
        if (config == null || !config.TryGetValue("collections", out var list)) return [];
        var refs = new List<(bool, string)>();
        foreach (var entry in Values(list))
        {
            string? Read(string name) => entry switch
            {
                JsonElement { ValueKind: JsonValueKind.Object } json => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                IDictionary<string, object> map => map.TryGetValue(name, out var value) ? value as string ?? (value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : null) : null,
                _ => null,
            };
            if (Read("library_collection_id") is { Length: > 0 } library) refs.Add((false, library));
            else if (Read("user_collection_id") is { Length: > 0 } user) refs.Add((true, user));
        }
        return refs;
    }

    private static readonly string[] SingleLibraryKeys = ["filter_library_id", "generated_library_id", "library_id"];
    private static readonly string[] ListLibraryKeys = ["filter_library", "filter_library_ids", "library_ids"];
    private static Dictionary<string, object>? Remap(Dictionary<string, object>? config, Dictionary<int, HomeLayoutLibrary> map)
    {
        var next = CloneConfig(config); if (next == null) return null;
        foreach (var key in SingleLibraryKeys)
            if (next.TryGetValue(key, out var value) && Id(value, out var id))
            { if (!map.TryGetValue(id, out var library)) return null; next[key] = library.Id; }
        foreach (var key in ListLibraryKeys)
            if (next.TryGetValue(key, out var value))
            {
                var items = Values(value);
                for (var i = 0; i < items.Length; i++)
                    if (Id(items[i], out var id)) { if (!map.TryGetValue(id, out var library)) return null; items[i] = library.Id; }
                if (items.Length > 0) next[key] = items;
            }
        return next;
    }
    /// <summary>What in a same-server config the importing profile can't use: no library it can open, a personal collection (alone or listed by a row of collections) it can't see, or a pinned profile outside its account.</summary>
    private static HomeLayoutSkipReason? ReferenceProblem(Dictionary<string, object>? config, HomeLayoutTarget target, Dictionary<int, HomeLayoutLibrary> map)
    {
        if (config == null) return null;
        var primary = new List<int>();
        foreach (var key in new[] { "filter_library_ids", "filter_library_id" })
            if (config.TryGetValue(key, out var value)) { if (Id(value, out var id)) primary.Add(id); else primary.AddRange(Values(value).Select(v => Id(v, out var n) ? n : 0).Where(n => n > 0)); }
        if (primary.Count == 0 && config.TryGetValue("library_ids", out var fallback)) primary.AddRange(Values(fallback).Select(v => Id(v, out var n) ? n : 0).Where(n => n > 0));
        if (primary.Count > 0 && !primary.Any(map.ContainsKey)) return HomeLayoutSkipReason.Library;
        foreach (var key in new[] { "filter_library", "library_id", "generated_library_id" })
            if (config.TryGetValue(key, out var value))
            {
                var ids = Id(value, out var single) ? [single] : Values(value).Select(v => Id(v, out var n) ? n : 0).Where(n => n > 0).ToArray();
                if (ids.Length > 0 && !ids.Any(map.ContainsKey)) return HomeLayoutSkipReason.Library;
            }
        if (Text(config, "user_collection_id") is { Length: > 0 } collection && !target.Collections.Contains(collection)) return HomeLayoutSkipReason.Collection;
        if (CollectionRefs(config).Any(item => item.Personal && !target.Collections.Contains(item.Id))) return HomeLayoutSkipReason.Collection;
        if (Text(config, "profile_id") is { Length: > 0 } profile && !target.Profiles.Contains(profile)) return HomeLayoutSkipReason.Profile;
        return null;
    }
    private static string SectionTitle(SectionOverride row)
        => FirstText(row.UserTitle, row.Title, row.UserSectionType, row.SectionType) ?? T("web.home.layout.transfer.untitledSection", "Untitled section");
    private static string? FirstText(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value));

    public static HomeLayoutPlan Plan(HomeLayoutFile file, HomeLayoutTarget target)
    {
        var same = !string.IsNullOrEmpty(file.ServerId) && file.ServerId == target.ServerId;
        var plan = new HomeLayoutPlan { SameServer = same, HideWatchedItems = file.HideWatchedItems };
        var map = new Dictionary<int, HomeLayoutLibrary>();
        string Key(HomeLayoutLibrary library) => library.Type + "\0" + library.Name.Trim().ToLowerInvariant();
        if (same) foreach (var library in target.Libraries) map[library.Id] = library;
        else foreach (var library in file.Libraries)
        {
            var matches = target.Libraries.Where(other => Key(other) == Key(library)).ToList();
            if (matches.Count == 1 && file.Libraries.Count(other => Key(other) == Key(library)) == 1) map[library.Id] = matches[0];
        }
        var homeLabel = T("web.home.rows.pages.home", "Home");
        foreach (var page in file.Pages)
        {
            var sourceLabel = homeLabel;
            if (page.Scope == "library")
            {
                sourceLabel = file.Libraries.FirstOrDefault(l => l.Id == page.LibraryId)?.Name
                    ?? T("web.home.layout.transfer.libraryFallback", $"Library {page.LibraryId}", ("id", page.LibraryId.ToString()));
                if (!map.ContainsKey(page.LibraryId!.Value)) { plan.SkippedPages.Add(sourceLabel); continue; }
            }
            var planned = new HomeLayoutPage { Scope = page.Scope, LibraryId = page.Scope == "library" ? map[page.LibraryId!.Value].Id : null,
                Label = page.Scope == "library" ? map[page.LibraryId!.Value].Name : homeLabel };
            if (same && page.ServerSectionTokens != null) planned.ServerSectionTokens = page.ServerSectionTokens;
            foreach (var original in page.Overrides)
            {
                var row = JsonSerializer.Deserialize<SectionOverride>(JsonSerializer.Serialize(original, Json), Json)!;
                var config = row.UserConfig ?? row.Config;
                var title = SectionTitle(row);
                if (!string.IsNullOrEmpty(row.SectionId))
                {
                    if (!same) { plan.SkippedServerSectionChanges++; continue; }
                    row.Id = Guid.NewGuid().ToString();
                    // The editor saves an admin section's config with the profile's other changes. One the importing
                    // profile can't use is dropped, so the section falls back to the admin's own config.
                    if (ReferenceProblem(row.Config, target, map) != null) row.Config = null;
                    planned.Overrides.Add(row); continue;
                }
                if (row.Removed == true) continue;
                var type = row.UserSectionType ?? row.SectionType ?? "";
                HomeLayoutSkipReason? reason = null;
                // The server refuses a new next_up row: it adds the only one itself.
                if (type == "next_up") reason = HomeLayoutSkipReason.UnknownRecipe;
                var filter = type is "genre" or "custom_filter" or "filter";
                var known = target.RecipeAdminOnly.TryGetValue(type, out var adminOnly);
                if (reason == null && !known && !filter && !same) reason = HomeLayoutSkipReason.UnknownRecipe;
                if (reason == null && (adminOnly || filter) && !target.AllowAdminOnly) reason = HomeLayoutSkipReason.CustomDisabled;
                if (reason == null && same) reason = ReferenceProblem(config, target, map);
                else if (reason == null)
                {
                    if (!string.IsNullOrEmpty(Text(config, "library_collection_id")) || !string.IsNullOrEmpty(Text(config, "user_collection_id")) || CollectionRefs(config).Count > 0)
                        reason = HomeLayoutSkipReason.Collection;
                    else if (!string.IsNullOrEmpty(Text(config, "profile_id"))) reason = HomeLayoutSkipReason.Profile;
                }
                if (reason == null && !same && config != null)
                {
                    var remapped = Remap(config, map);
                    if (remapped == null) reason = HomeLayoutSkipReason.Library;
                    else if (row.UserConfig != null) { row.UserConfig = remapped; row.Config = Remap(row.Config, map); }
                    else row.Config = remapped;
                }
                if (reason != null) { plan.SkippedSections.Add(new(sourceLabel, title, reason.Value)); continue; }
                row.Id = Guid.NewGuid().ToString(); planned.Overrides.Add(row);
            }
            // A page exported without overrides showed the server's rows. On the same server importing it restores
            // them; another server's rows differ, so there it carries nothing.
            if (planned.Overrides.Count > 0 || same && page.Overrides.Count == 0) plan.Pages.Add(planned);
            else if (same) { planned.ReplaceExisting = false; plan.UnappliedPages.Add(planned); }
        }
        return plan;
    }
}
