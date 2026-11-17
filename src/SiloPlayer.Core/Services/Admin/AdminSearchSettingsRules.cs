using System.Globalization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Configure &gt; Server &gt; Search keys, defaults, tiers and display rules, mirrored from the official
/// <c>SearchSettings.tsx</c> and <c>SearchStatusPanel.tsx</c> (silo-server 17d5376c). The server remains the
/// authority for every value; these helpers only decide what the page shows.
/// </summary>
public static class AdminSearchSettingsRules
{
    public const string Provider = "catalog.search.provider";
    public const string MeilisearchUrl = "catalog.search.meilisearch.url";
    public const string MeilisearchApiKey = "catalog.search.meilisearch.api_key";
    public const string MatchingStrategy = "catalog.search.meilisearch.matching_strategy";
    public const string SemanticEnabled = "catalog.search.meilisearch.semantic_enabled";
    public const string Index = "catalog.search.meilisearch.index";
    public const string TimeoutMs = "catalog.search.meilisearch.timeout_ms";
    public const string SyncBatchSize = "catalog.search.meilisearch.sync_batch_size";
    public const string SemanticRatio = "catalog.search.meilisearch.semantic_ratio";

    public const string Postgres = "postgres";
    public const string Meilisearch = "meilisearch";
    public const string RebuildTaskKey = "rebuild_catalog_search_index";
    public const string SyncTaskKey = "sync_catalog_search_index";
    public static string RebuildTaskHref => "/admin/tasks/" + RebuildTaskKey;
    public static string SyncTaskHref => "/admin/tasks/" + SyncTaskKey;

    /// <summary>Status is re-read this often while an index rebuild is required (official <c>refetchInterval</c>).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    /// <summary>A status read is reused for this long when the page asks for it again (official <c>staleTime</c>).</summary>
    public static readonly TimeSpan StatusStaleAfter = TimeSpan.FromSeconds(15);

    /// <summary>Reaching Meilisearch and the two choices that change what a search returns (shown once Meilisearch is selected).</summary>
    public static IReadOnlyList<string> ShownKeys { get; } = Array.AsReadOnly([MeilisearchUrl, MeilisearchApiKey, MatchingStrategy, SemanticEnabled]);
    /// <summary>Index name, timing and ranking mix: tuning behind Advanced.</summary>
    public static IReadOnlyList<string> AdvancedKeys { get; } = Array.AsReadOnly([Index, TimeoutMs, SyncBatchSize, SemanticRatio]);
    /// <summary>Every Meilisearch key, in the order the connection check sends them.</summary>
    public static IReadOnlyList<string> MeilisearchKeys { get; } = Array.AsReadOnly(ShownKeys.Concat(AdvancedKeys).ToArray());
    /// <summary>The page's keys. The hidden tier (rebuild batch, task queue depth, index types, embedder, quantization) is deliberately not edited.</summary>
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { Provider }.Concat(MeilisearchKeys).ToArray());
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly([MeilisearchApiKey]);

    /// <summary>The environment variable that sets each key on every server start; the server refuses saves of that key meanwhile.</summary>
    public static string? EnvironmentVariable(string key) => key switch
    {
        Provider => "SILO_SEARCH_PROVIDER",
        MeilisearchUrl => "SILO_MEILISEARCH_URL",
        MeilisearchApiKey => "SILO_MEILISEARCH_API_KEY",
        _ => null,
    };

    /// <summary>The value the field shows while nothing is saved (official <c>form.getValue(key) || default</c>).</summary>
    public static string Default(string key) => key switch
    {
        Provider => Postgres,
        MatchingStrategy => "last",
        SemanticEnabled => "false",
        Index => "silo_media_items",
        TimeoutMs => "800",
        SyncBatchSize => "500",
        SemanticRatio => "0.50",
        _ => "",
    };

    public static string Display(string key, string? value) => string.IsNullOrEmpty(value) ? Default(key) : value;

    public static bool IsMeilisearch(IReadOnlyDictionary<string, string> values) =>
        Display(Provider, values.GetValueOrDefault(Provider, "")) == Meilisearch;

    /// <summary>True while a staged edit to any Meilisearch key keeps its fields reachable even after switching back to Postgres.</summary>
    public static bool AnyMeilisearchDirty(IEnumerable<string> dirty) => dirty.Any(MeilisearchKeys.Contains);
    public static bool AnyAdvancedDirty(IEnumerable<string> dirty) => dirty.Any(AdvancedKeys.Contains);

    /// <summary>Enabling semantic search changes the index format, so the page warns before the admin saves it.</summary>
    public static bool IsEnablingSemanticSearch(IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> persisted, IReadOnlyCollection<string> dirty) =>
        dirty.Contains(SemanticEnabled) && persisted.GetValueOrDefault(SemanticEnabled, "") != "true" && values.GetValueOrDefault(SemanticEnabled, "") == "true";

    /// <summary>Official <c>formatSemanticRatio</c>: two decimals, culture-formatted.</summary>
    public static string FormatSemanticRatio(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? "0" : value.ToString("F2", CultureInfo.CurrentCulture);

    /// <summary>Official <c>formatDateTime</c> (numeric date, then time with seconds) in local time; empty for a missing value.</summary>
    public static string FormatDateTime(DateTimeOffset? value, TimeZoneInfo? zone = null)
    {
        if (value is not { } instant) return "";
        var local = TimeZoneInfo.ConvertTime(instant, zone ?? TimeZoneInfo.Local);
        return local.ToString("d", CultureInfo.CurrentCulture) + ", " + local.ToString("T", CultureInfo.CurrentCulture);
    }

    /// <summary>Official <c>formatPercent</c>: the ratio as a whole percent, 0 for a missing value.</summary>
    public static long Percent(double ratio) => double.IsNaN(ratio) || double.IsInfinity(ratio) ? 0 : (long)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
}
