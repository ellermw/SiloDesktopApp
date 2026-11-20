using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminLibrarySettingProblem { Blank, NotInteger, OutOfRange, InvalidChoice }

public sealed record AdminLibrarySettingError(AdminLibrarySettingProblem Problem, long Min = 0, long Max = 0)
{
    public override string ToString() => nameof(AdminLibrarySettingError);
}

/// <summary>One external rating source the server or an enabled metadata plugin declares.</summary>
public sealed record AdminRatingSource(string Source, string Label, string Name, bool AlwaysShown, string? Provider)
{
    public override string ToString() => nameof(AdminRatingSource);
}

public sealed record AdminRatingSourceGroup(string Provider, IReadOnlyList<AdminRatingSource> Sources);

/// <summary>
/// Manage &gt; Libraries &gt; Settings keys, defaults, visibility and validators. Mirrored from official
/// <c>LibraryMetadataSettings.tsx</c>, <c>settingsWorkerDefaults.ts</c>, <c>ratingSources.ts</c>,
/// <c>playbackSettings.utils.ts</c> and <c>internal/config/admin_settings.go</c> at silo-server 217ff25a.
/// The server remains authoritative; local validation only prevents writes the pinned server rejects.
/// </summary>
public static partial class AdminLibrarySettingsRules
{
    public const string ScopeVersionsToLibrary = "catalog.scope_versions_to_library";
    public const string FallbackLanguage = "metadata.fallback_language";
    public const string RealtimeMonitoring = "scanner.realtime_monitoring";
    public const string ScannerWorkers = "scanner.workers";
    public const string MatcherWorkers = "matcher.workers";
    public const string ImageWorkers = "metadata.image_workers";
    public const string PersonRefreshPerMinute = "metadata.person_refresh_per_minute";
    public const string MatcherBatchSize = "matcher.batch_size";
    public const string UnratedContent = "access.unrated_content";
    public const string ExtraRatingSources = "catalog.extra_rating_sources";
    public const string SubtitleAutoSync = "subtitles.auto_sync";
    public const string SubtitleSyncExecution = "subtitles.sync_execution";
    public const string SubtitleSyncNodeCapacity = "subtitles.sync_node_capacity";

    public const string DefaultSubtitleSyncExecution = "prefer_transcode_nodes";

    public static IReadOnlyList<string> GeneralKeys { get; } = Array.AsReadOnly([ScopeVersionsToLibrary, FallbackLanguage]);
    public static IReadOnlyList<string> ScannerAdvancedKeys { get; } = Array.AsReadOnly([PersonRefreshPerMinute, MatcherBatchSize]);
    public static IReadOnlyList<string> ScannerKeys { get; } = Array.AsReadOnly([ScannerWorkers, MatcherWorkers, ImageWorkers, .. ScannerAdvancedKeys]);
    public static IReadOnlyList<string> ScanningKeys { get; } = Array.AsReadOnly([RealtimeMonitoring, .. ScannerKeys]);
    public static IReadOnlyList<string> MaturityKeys { get; } = Array.AsReadOnly([UnratedContent]);
    public static IReadOnlyList<string> RatingsKeys { get; } = Array.AsReadOnly([ExtraRatingSources]);
    public static IReadOnlyList<string> SubtitleAdvancedKeys { get; } = Array.AsReadOnly([SubtitleSyncExecution, SubtitleSyncNodeCapacity]);
    public static IReadOnlyList<string> SubtitleKeys { get; } = Array.AsReadOnly([SubtitleAutoSync, .. SubtitleAdvancedKeys]);
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly([.. GeneralKeys, .. ScanningKeys, .. MaturityKeys, .. RatingsKeys, .. SubtitleKeys]);

    public static IReadOnlyDictionary<string, string> WorkerDefaults { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ScannerWorkers] = "8",
            [ImageWorkers] = "0",
            [PersonRefreshPerMinute] = "120",
            [MatcherWorkers] = "8",
            [MatcherBatchSize] = "500",
        });

    public static bool HasWorkerOverrides(Func<string, string> getValue) => WorkerDefaults.Any(entry =>
        getValue(entry.Key).Trim() is { Length: > 0 } value && value != entry.Value);

    public static bool ShowRealtimeMonitoring(string persistedValue, bool dirty) => persistedValue == "false" || dirty;
    public static bool IsNodeBackedExecution(string value) => value is "prefer_transcode_nodes" or "transcode_nodes_only";
    public static bool ShowSubtitleCapacity(string execution, bool capacityDirty) => IsNodeBackedExecution(execution) || capacityDirty;

    public static IReadOnlyDictionary<string, AdminLibrarySettingError> Validate(
        IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirty)
    {
        var errors = new Dictionary<string, AdminLibrarySettingError>(StringComparer.Ordinal);
        foreach (var key in dirty)
        {
            var value = values.GetValueOrDefault(key, "");
            var error = key switch
            {
                ScannerWorkers or MatcherWorkers => Integer(value, 1, 1024),
                ImageWorkers => Integer(value, 0, 256),
                PersonRefreshPerMinute => Integer(value, 1, 6000),
                MatcherBatchSize => Integer(value, 1, 100000),
                SubtitleSyncNodeCapacity => Integer(value, 1, 1024),
                ScopeVersionsToLibrary or RealtimeMonitoring or SubtitleAutoSync => Choice(value, "true", "false"),
                UnratedContent => Choice(value, "hide", "allow"),
                SubtitleSyncExecution => Choice(value, "local", "prefer_transcode_nodes", "transcode_nodes_only"),
                FallbackLanguage => FallbackLanguageValue(value),
                ExtraRatingSources => RatingSourcesValue(value),
                _ => null,
            };
            if (error != null) errors[key] = error;
        }
        return new ReadOnlyDictionary<string, AdminLibrarySettingError>(errors);
    }

    public static string Describe(AdminLibrarySettingError error) => error.Problem switch
    {
        AdminLibrarySettingProblem.Blank => AdminText.T("configure.native.librarySettings.error.numberRequired"),
        AdminLibrarySettingProblem.NotInteger => AdminText.T("configure.native.librarySettings.error.wholeNumber"),
        AdminLibrarySettingProblem.OutOfRange => AdminText.T("configure.native.librarySettings.error.integerRange",
            new { min = AdminText.FormatNumber(error.Min), max = AdminText.FormatNumber(error.Max) }),
        _ => AdminText.T("configure.native.librarySettings.error.choice"),
    };

    private static AdminLibrarySettingError? Integer(string raw, long min, long max)
    {
        var value = raw.Trim();
        if (value.Length == 0) return new(AdminLibrarySettingProblem.Blank, min, max);
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            var digits = value.TrimStart('+', '-');
            return new(digits.Length > 0 && digits.All(char.IsAsciiDigit) && value.Length - digits.Length <= 1
                ? AdminLibrarySettingProblem.OutOfRange : AdminLibrarySettingProblem.NotInteger, min, max);
        }
        return parsed < min || parsed > max ? new(AdminLibrarySettingProblem.OutOfRange, min, max) : null;
    }

    private static AdminLibrarySettingError? Choice(string value, params string[] choices) =>
        choices.Contains(value, StringComparer.Ordinal) ? null : new(AdminLibrarySettingProblem.InvalidChoice);

    private static AdminLibrarySettingError? FallbackLanguageValue(string value)
    {
        if (value is "" or "original") return null;
        return AdminLibraryPresentation.MetadataLanguages().Any(option => option.Code == value)
            ? null : new(AdminLibrarySettingProblem.InvalidChoice);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex RatingSourcePattern();

    private static AdminLibrarySettingError? RatingSourcesValue(string value) =>
        // The server lowercases each name before it checks the grammar (splitRatingSourceList).
        ParseRatingSources(value).All(source => RatingSourcePattern().IsMatch(source.ToLowerInvariant()))
            ? null : new(AdminLibrarySettingProblem.InvalidChoice);

    public static IReadOnlyList<string> ParseRatingSources(string? value) => (value ?? "").Split(',')
        .Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToArray();

    public static string ToggleRatingSource(string? value, string source, bool on)
    {
        // Like the official toggle this never rejects a name: a malformed saved entry must stay removable.
        var rest = ParseRatingSources(value).Where(entry => entry != source).ToList();
        if (on) rest.Add(source);
        return string.Join(',', rest);
    }

    public static IReadOnlyList<AdminRatingSourceGroup> GroupRatingSources(IEnumerable<AdminRatingSource> sources)
    {
        var groups = new Dictionary<string, List<AdminRatingSource>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source.AlwaysShown || string.IsNullOrWhiteSpace(source.Provider)) continue;
            if (!groups.TryGetValue(source.Provider, out var entries)) groups[source.Provider] = entries = [];
            entries.Add(source);
        }
        return groups.Select(group => new AdminRatingSourceGroup(group.Key, group.Value.AsReadOnly())).ToArray();
    }

    public static IReadOnlyList<string> UndeclaredRatingSources(IEnumerable<string> turnedOn, IEnumerable<AdminRatingSource> sources)
    {
        var declared = sources.Select(source => source.Source).ToHashSet(StringComparer.Ordinal);
        return turnedOn.Distinct(StringComparer.Ordinal).Where(source => !declared.Contains(source)).ToArray();
    }
}
