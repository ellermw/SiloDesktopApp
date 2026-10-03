using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
}
public sealed class HomeLayoutFile
{
    public string Format { get; set; } = "silo-home-layout";
    public int Version { get; set; } = 1;
    public string ExportedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string ServerId { get; set; } = "";
    public List<HomeLayoutLibrary> Libraries { get; set; } = [];
    public bool? HideWatchedItems { get; set; }
    public List<HomeLayoutPage> Pages { get; set; } = [];
}
public sealed record HomeLayoutTarget(string ServerId, IReadOnlyList<HomeLayoutLibrary> Libraries,
    IReadOnlyDictionary<string, bool> RecipeAdminOnly, bool AllowAdminOnly, IReadOnlySet<string> Collections, IReadOnlySet<string> Profiles);
public sealed class HomeLayoutPlan
{
    public bool SameServer { get; init; }
    public List<HomeLayoutPage> Pages { get; } = [];
    public List<string> Skipped { get; } = [];
    public bool? HideWatchedItems { get; init; }
}

public static class HomeLayoutTransfer
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };
    public static string Serialize(HomeLayoutFile file)
    {
        var text = JsonSerializer.Serialize(file, Json);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Layout exceeds the 5 MiB limit.");
        return text;
    }
    public static HomeLayoutFile Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Layout exceeds the 5 MiB limit.");
        var file = JsonSerializer.Deserialize<HomeLayoutFile>(text, Json) ?? throw new InvalidDataException("Empty layout.");
        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("format", out _) || !document.RootElement.TryGetProperty("version", out _) ||
            !document.RootElement.TryGetProperty("server_id", out var serverId) || serverId.ValueKind != JsonValueKind.String ||
            !document.RootElement.TryGetProperty("pages", out _) || file.Format != "silo-home-layout" || file.Version != 1 ||
            file.Pages == null || file.Libraries == null || file.Libraries.Any(library => library == null || library.Id <= 0 ||
                library.Name == null || library.Type == null) || file.Pages.Any(page => page == null || page.Scope is not ("home" or "library") ||
                page.Scope == "library" && page.LibraryId is not > 0 || page.Overrides == null) ||
            file.Pages.GroupBy(page => (page.Scope, page.LibraryId)).Any(group => group.Count() > 1))
            throw new InvalidDataException("This is not a supported Silo Home layout file.");
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
    private static string? ReferenceProblem(Dictionary<string, object>? config, HomeLayoutTarget target, Dictionary<int, HomeLayoutLibrary> map)
    {
        if (config == null) return null;
        if (Text(config, "user_collection_id") is { Length: > 0 } collection && !target.Collections.Contains(collection)) return "collection unavailable";
        if (Text(config, "profile_id") is { Length: > 0 } profile && !target.Profiles.Contains(profile)) return "profile unavailable";
        var primary = new List<int>();
        foreach (var key in new[] { "filter_library_ids", "filter_library_id" })
            if (config.TryGetValue(key, out var value)) { if (Id(value, out var id)) primary.Add(id); else primary.AddRange(Values(value).Select(v => Id(v, out var n) ? n : 0).Where(n => n > 0)); }
        if (primary.Count == 0 && config.TryGetValue("library_ids", out var fallback)) primary.AddRange(Values(fallback).Select(v => Id(v, out var n) ? n : 0).Where(n => n > 0));
        if (primary.Count > 0 && !primary.Any(map.ContainsKey)) return "library unavailable";
        foreach (var key in new[] { "filter_library", "library_id", "generated_library_id" })
            if (config.TryGetValue(key, out var value))
            {
                var ids = Id(value, out var single) ? [single] : Values(value).Select(v => Id(v, out var n) ? n : 0).Where(n => n > 0).ToArray();
                if (ids.Length > 0 && !ids.Any(map.ContainsKey)) return "library unavailable";
            }
        return null;
    }
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
        foreach (var page in file.Pages)
        {
            if (page.Scope == "library" && !map.ContainsKey(page.LibraryId!.Value))
            { plan.Skipped.Add($"Library page {file.Libraries.FirstOrDefault(l => l.Id == page.LibraryId)?.Name ?? page.LibraryId.ToString()}: no unique match"); continue; }
            var planned = new HomeLayoutPage { Scope = page.Scope, LibraryId = page.Scope == "library" ? map[page.LibraryId!.Value].Id : null };
            foreach (var original in page.Overrides)
            {
                var row = JsonSerializer.Deserialize<SectionOverride>(JsonSerializer.Serialize(original, Json), Json)!;
                var config = row.UserConfig ?? row.Config;
                var title = row.UserTitle ?? row.Title ?? row.UserSectionType ?? row.SectionType ?? "Section";
                if (!string.IsNullOrEmpty(row.SectionId))
                {
                    if (!same) { plan.Skipped.Add($"{title}: server section belongs to another server"); continue; }
                    row.Id = Guid.NewGuid().ToString();
                    if (ReferenceProblem(row.Config, target, map) != null) row.Config = null;
                    planned.Overrides.Add(row); continue;
                }
                if (row.Removed == true) continue;
                var type = row.UserSectionType ?? row.SectionType ?? "";
                var filter = type is "custom_filter" or "filter";
                string? reason = HomeSectionWritePolicy.IsTrakt(config) ? "Trakt-backed sections cannot be added again" : null;
                if (!target.RecipeAdminOnly.TryGetValue(type, out var adminOnly) && !filter && !same) reason ??= "unknown recipe";
                if ((adminOnly || filter) && !target.AllowAdminOnly) reason ??= "custom sections disabled";
                if (same) reason ??= ReferenceProblem(config, target, map);
                else if (!string.IsNullOrEmpty(Text(config, "profile_id")) || !string.IsNullOrEmpty(Text(config, "user_collection_id")) || !string.IsNullOrEmpty(Text(config, "library_collection_id")))
                    reason ??= "account or collection reference cannot transfer between servers";
                if (!same && config != null)
                {
                    var remapped = Remap(config, map);
                    if (remapped == null) reason ??= "library unavailable";
                    else if (row.UserConfig != null) { row.UserConfig = remapped; row.Config = Remap(row.Config, map); }
                    else row.Config = remapped;
                }
                if (reason != null) { plan.Skipped.Add($"{title}: {reason}"); continue; }
                row.Id = Guid.NewGuid().ToString(); planned.Overrides.Add(row);
            }
            if (planned.Overrides.Count > 0) plan.Pages.Add(planned);
        }
        return plan;
    }
}
