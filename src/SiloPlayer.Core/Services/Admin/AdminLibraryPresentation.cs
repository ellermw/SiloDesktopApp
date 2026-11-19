using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Pure presentation rules for Manage › Libraries, ported from the official WebUI
/// helpers at abd85d0c: <c>libraryTypes.ts</c>, <c>useLibraryForm.ts</c> (content
/// levels, provider-chain transforms), <c>realtimeMonitoring.ts</c>,
/// <c>trickplayStatus.ts</c>, <c>lib/scanRuns.ts</c>, <c>lib/adminJobs.ts</c>,
/// <c>adminCatalogMaintenanceFormatters.ts</c>, <c>useLibraryRows.ts</c> and the
/// diagnostics sections' row models. No I/O, no state.
/// </summary>
public static class AdminLibraryPresentation
{
    private static string T(string key, params (string Name, object? Value)[] values) => AdminLibrariesText.T(key, values);

    // ── Library types (libraryTypes.ts) ──────────────────────────────────

    public sealed record LibraryType(string Value, string Icon, string LabelKey)
    {
        public string Label => AppStrings.Get(LabelKey, fallback: Value);
    }

    public static IReadOnlyList<LibraryType> Types { get; } =
    [
        new("movies", "film", "web.common.libraryType.movies"),
        new("series", "tv", "web.common.libraryType.series"),
        new("mixed", "layers", "web.common.libraryType.mixed"),
        new("audiobooks", "book-headphones", "web.common.libraryType.audiobooks"),
        new("ebooks", "book-open", "web.common.libraryType.ebooks"),
        new("manga", "book-marked", "web.common.libraryType.manga"),
        new("podcasts", "podcast", "web.common.libraryType.podcasts"),
    ];

    private static readonly IReadOnlyDictionary<string, string> TypeAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["movie"] = "movies", ["tv"] = "series", ["show"] = "series", ["tvshows"] = "series", ["audiobook"] = "audiobooks",
        ["ebook"] = "ebooks", ["podcast"] = "podcasts",
    };

    /// <summary>The library type a stored type string means, or null for an unknown type.</summary>
    public static LibraryType? FindType(string? type)
    {
        var kind = (type ?? "").Trim().ToLowerInvariant();
        var value = TypeAliases.TryGetValue(kind, out var alias) ? alias : kind;
        return Types.FirstOrDefault(entry => entry.Value == value);
    }

    public static LibraryType TypeMeta(string? type) => FindType(type) ?? Types[0];

    /// <summary>A stored type in the interface language; an unknown type shows as stored.</summary>
    public static string TypeLabel(string? type) => FindType(type)?.Label ?? type ?? "";

    public const int DefaultBoxSetMinMovies = 2;
    public static IReadOnlyList<int> BoxSetMinMovieOptions { get; } = [2, 3, 4, 5];

    /// <summary>The offered sizes plus a saved size outside them, so it stays selected.</summary>
    public static IReadOnlyList<int> BoxSetOptions(int current) =>
        BoxSetMinMovieOptions.Append(current).Distinct().Order().ToArray();

    public sealed record SettingSupport(bool Trailers, bool ChapterThumbnails, bool Trickplay, bool IntroDetection, bool CreditsOnlyDetection,
        bool MovieCreditsDetection, bool BoxSets);

    /// <summary>WebUI <c>librarySettingSupport</c>: which editor fields a library type offers.</summary>
    public static SettingSupport Support(string? type)
    {
        var kind = (type ?? "").Trim().ToLowerInvariant();
        var video = kind is not ("audiobook" or "audiobooks" or "ebook" or "ebooks" or "manga" or "podcast" or "podcasts");
        return new SettingSupport(
            Trailers: video,
            ChapterThumbnails: video,
            Trickplay: kind is "movie" or "movies" or "series" or "tv" or "show" or "tvshows" or "mixed",
            IntroDetection: kind is "series" or "mixed" or "movies",
            CreditsOnlyDetection: kind == "movies",
            MovieCreditsDetection: kind is "movies" or "mixed",
            BoxSets: kind is "movie" or "movies" or "mixed");
    }

    /// <summary>WebUI <c>markerDetectionCopy</c>: the Advanced section's marker-detection card text.</summary>
    public static (string Title, string Description) MarkerDetectionCopy(SettingSupport support)
    {
        if (support.CreditsOnlyDetection)
            return (T("web.admin.common.libraryEditor.advanced.creditsTitle"), T("web.admin.common.libraryEditor.advanced.creditsDescription"));
        return (T("web.admin.common.libraryEditor.advanced.introCreditsTitle"),
            T(support.MovieCreditsDetection ? "web.admin.common.libraryEditor.advanced.introCreditsMixedDescription" : "web.admin.common.libraryEditor.advanced.introCreditsDescription"));
    }

    // ── Trailer kinds (lib/extraKinds.ts PROVIDER_TRAILER_KINDS) ─────────

    public static IReadOnlyList<string> ProviderTrailerKinds { get; } =
        ["trailer", "teaser", "featurette", "clip", "behind_the_scenes", "bloopers", "other"];

    public static string TrailerKindLabel(string kind) => kind switch
    {
        "trailer" => AppStrings.Get("web.detail.extras.kindGroup.trailer", fallback: kind),
        "teaser" => AppStrings.Get("web.detail.extras.kindGroup.teaser", fallback: kind),
        "featurette" => AppStrings.Get("web.detail.extras.kindGroup.featurette", fallback: kind),
        "clip" => AppStrings.Get("web.detail.extras.kindGroup.clip", fallback: kind),
        "behind_the_scenes" => AppStrings.Get("web.detail.extras.kindGroup.behindTheScenes", fallback: kind),
        "bloopers" => AppStrings.Get("web.detail.extras.kindGroup.bloopers", fallback: kind),
        "deleted_scene" => AppStrings.Get("web.detail.extras.kindGroup.deletedScene", fallback: kind),
        _ => AppStrings.Get("web.detail.extras.kindGroup.otherKind", fallback: kind),
    };

    // ── Metadata languages ───────────────────────────────────────────────

    /// <summary>The library metadata language picker: bare ISO 639-1 codes only (the server rejects regional tags).</summary>
    public static IReadOnlyList<AdminSubtitlesText.LanguageOption> MetadataLanguages() =>
        AdminSubtitlesText.LanguageOptions().Where(option => option.Code.Length == 2 && option.Code.All(char.IsAsciiLetterLower)).ToArray();

    /// <summary>The translate panel's target picker: the full common-language list.</summary>
    public static IReadOnlyList<AdminSubtitlesText.LanguageOption> TranslationLanguages() => AdminSubtitlesText.LanguageOptions();

    // ── Content levels and provider chains (useLibraryForm.ts) ──────────

    public static IReadOnlyList<string> ContentLevels(string? type) => type switch
    {
        "series" => ["series", "season", "episode"],
        "movies" => ["movie"],
        "mixed" => ["movie", "series", "season", "episode", "audiobook", "ebook"],
        "audiobooks" => ["audiobook"],
        "ebooks" or "ebook" => ["ebook"],
        "manga" => ["manga"],
        "podcasts" => ["podcast", "podcast_episode"],
        _ => [],
    };

    /// <summary>A content level's name; an unknown level is spelled from its id ("Podcast Episode").</summary>
    public static string ContentLevelLabel(string level)
    {
        var key = level switch
        {
            "movie" => "web.admin.common.libraryEditor.contentLevel.movie",
            "series" => "web.admin.common.libraryEditor.contentLevel.series",
            "season" => "web.admin.common.libraryEditor.contentLevel.season",
            "episode" => "web.admin.common.libraryEditor.contentLevel.episode",
            "audiobook" => "web.admin.common.libraryEditor.contentLevel.audiobook",
            "ebook" => "web.admin.common.libraryEditor.contentLevel.ebook",
            "manga" => "web.admin.common.libraryEditor.contentLevel.manga",
            "podcast" => "web.admin.common.libraryEditor.contentLevel.podcast",
            "podcast_episode" => "web.admin.common.libraryEditor.contentLevel.podcastEpisode",
            _ => null,
        };
        if (key != null) return T(key);
        return string.Join(" ", level.Split('_').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    /// <summary>WebUI <c>levelChainsFromResponse</c>: per-level lists in server priority order with local entries first.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> LevelChains(AdminProviderChains? response)
    {
        var mapped = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>(StringComparer.Ordinal);
        foreach (var level in response?.Levels ?? [])
            mapped[level.ContentLevel] = level.Entries
                .OrderBy(entry => entry.Local ? 0 : 1).ThenBy(entry => entry.Priority)
                .Select(entry => new AdminProviderChainItem(entry.PluginInstallationId, entry.CapabilityId, entry.ProviderSlug, entry.Enabled, entry.Local))
                .ToArray();
        return mapped;
    }

    /// <summary>WebUI <c>remoteLevelChains</c>: the local entries are one pinned switch, not a row per level.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> RemoteChains(IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> chains) =>
        chains.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AdminProviderChainItem>)pair.Value.Where(item => !item.Local).ToArray(), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> SetLocalEnabled(IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> chains, bool enabled) =>
        chains.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AdminProviderChainItem>)pair.Value.Select(item => item.Local ? item with { Enabled = enabled } : item).ToArray(), StringComparer.Ordinal);

    /// <summary>WebUI <c>withLocalFirst</c>: edits to the remote order can never move local metadata.</summary>
    public static IReadOnlyList<AdminProviderChainItem> WithLocalFirst(IReadOnlyList<AdminProviderChainItem> items, IReadOnlyList<AdminProviderChainItem> remote) =>
        items.Where(item => item.Local).Concat(remote).ToArray();

    /// <summary>WebUI <c>mergeChainWithDefaults</c>: levels the saved chain does not cover take the server's defaults.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> MergeWithDefaults(IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> chain,
        IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> defaults, string type)
    {
        var merged = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>>(chain, StringComparer.Ordinal);
        foreach (var level in ContentLevels(type))
            if (!merged.TryGetValue(level, out var items) || items.Count == 0)
                merged[level] = defaults.TryGetValue(level, out var fallback) ? fallback : [];
        return merged;
    }

    public static bool HasProviders(IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> chains) => chains.Values.Any(items => items.Count > 0);

    /// <summary>Moves one remote provider a single place; null at either end.</summary>
    public static IReadOnlyList<AdminProviderChainItem>? MoveProvider(IReadOnlyList<AdminProviderChainItem> remote, int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= remote.Count || target < 0 || target >= remote.Count) return null;
        var next = remote.ToArray();
        (next[index], next[target]) = (next[target], next[index]);
        return next;
    }

    // ── List order (pages/adminLibraryOrder.ts) ──────────────────────────

    /// <summary>The order after moving one library a single place up (-1) or down (1); null when missing or at that end.</summary>
    public static IReadOnlyList<T>? MoveByOne<T>(IReadOnlyList<T> items, Func<T, string> id, string targetId, int direction)
    {
        var from = -1;
        for (var index = 0; index < items.Count; index++) if (id(items[index]) == targetId) { from = index; break; }
        var to = from + direction;
        if (from < 0 || to < 0 || to >= items.Count) return null;
        var next = items.ToArray();
        (next[from], next[to]) = (next[to], next[from]);
        return next;
    }

    /// <summary>The order after dragging the library at <paramref name="from"/> to <paramref name="to"/> (dnd-kit <c>arrayMove</c>).</summary>
    public static IReadOnlyList<T>? ArrayMove<T>(IReadOnlyList<T> items, int from, int to)
    {
        if (from < 0 || from >= items.Count || to < 0 || to >= items.Count || from == to) return null;
        var next = items.ToList();
        var moved = next[from];
        next.RemoveAt(from);
        next.Insert(to, moved);
        return next;
    }

    // ── Realtime monitoring (realtimeMonitoring.ts) ──────────────────────

    public static string MonitoringStateLabel(string state) => T(state switch
    {
        "server_disabled" => "web.admin.common.libraryEditor.monitoringState.serverDisabled",
        "library_disabled" => "web.admin.common.libraryEditor.monitoringState.libraryDisabled",
        "monitoring_off" => "web.admin.common.libraryEditor.monitoringState.monitoringOff",
        "not_reporting" => "web.admin.common.libraryEditor.monitoringState.notReporting",
        "starting" => "web.admin.common.libraryEditor.monitoringState.starting",
        "monitoring" => "web.admin.common.libraryEditor.monitoringState.monitoring",
        "unsupported_filesystem" => "web.admin.common.libraryEditor.monitoringState.unsupportedFilesystem",
        "unsupported_platform" => "web.admin.common.libraryEditor.monitoringState.unsupportedPlatform",
        "limit_reached" => "web.admin.common.libraryEditor.monitoringState.limitReached",
        "root_unavailable" => "web.admin.common.libraryEditor.monitoringState.rootUnavailable",
        _ => "web.admin.common.libraryEditor.monitoringState.error",
    });

    /// <summary>Whether the list should flag this library's monitoring (deployment-wide states are not flagged).</summary>
    public static bool MonitoringNeedsAttention(AdminLibraryRealtimeMonitoringEntry? entry) =>
        entry != null && entry.State is "limit_reached" or "root_unavailable" or "unsupported_filesystem" or "error";

    public static string MonitoringStatusText(AdminLibraryRealtimeMonitoringEntry entry)
    {
        string summary;
        if (entry.State != "monitoring") summary = MonitoringStateLabel(entry.State);
        else if (entry.Backend.Length > 0)
            summary = T("web.admin.common.libraryEditor.monitoringStatus.foldersWithBackend", AdminLibrariesText.Count(entry.Directories, ("backend", entry.Backend)));
        else summary = T("web.admin.common.libraryEditor.monitoringStatus.folders", AdminLibrariesText.Count(entry.Directories));
        var detail = entry.Detail.Trim();
        return detail.Length > 0 ? T("web.admin.common.libraryEditor.monitoringStatus.withDetail", ("summary", summary), ("detail", detail)) : summary;
    }

    // ── Seek previews (trickplayStatus.ts) ───────────────────────────────

    public static string TrickplayLabel(AdminTrickplayLibrary library)
    {
        var total = library.Pending + library.Running + library.Ready + library.Unusable;
        var done = library.Pending == 0 && library.Running == 0;
        if (total > 0 && done && library.Unusable == 0) return T("web.admin.common.trickplay.badge.ready");
        return T("web.admin.common.trickplay.badge.percent", ("percent", total == 0 ? 0 : library.Ready * 100 / total));
    }

    public static string TrickplayDetail(AdminTrickplayLibrary library)
    {
        var parts = new List<string> { T("web.admin.common.trickplay.badge.readyCount", AdminLibrariesText.Count(library.Ready)) };
        if (library.Pending > 0) parts.Add(T("web.admin.common.trickplay.badge.waitingCount", AdminLibrariesText.Count(library.Pending)));
        if (library.Running > 0) parts.Add(T("web.admin.common.trickplay.badge.runningCount", AdminLibrariesText.Count(library.Running)));
        if (library.Unusable > 0) parts.Add(T("web.admin.common.trickplay.badge.failedCount", AdminLibrariesText.Count(library.Unusable)));
        parts.Add(DashboardFormat.FileSize(library.SheetBytes, AppStrings.Get("web.common.format.size.bytes", new Dictionary<string, object?> { ["size"] = 0 }, "0 B")));
        return T("web.admin.common.trickplay.badge.detail", ("details", string.Join(" · ", parts)));
    }

    // ── Scan runs (lib/scanRuns.ts) ──────────────────────────────────────

    /// <summary>WebUI <c>compareActiveScans</c>: running first, then mode in its fixed rank, then path.</summary>
    public static int CompareScans(AdminLibraryScanRun left, AdminLibraryScanRun right)
    {
        var rank = (left.IsRunning ? 0 : 1).CompareTo(right.IsRunning ? 0 : 1);
        if (rank != 0) return rank;
        var mode = ModeRank(left.Mode).CompareTo(ModeRank(right.Mode));
        if (mode == 0) mode = string.CompareOrdinal(left.Mode, right.Mode);
        if (mode != 0) return mode;
        return string.Compare(left.Path ?? "", right.Path ?? "", StringComparison.CurrentCulture);
        static int ModeRank(string value) => value switch { "library" => 0, "file" => 1, "subtree" => 2, _ => 3 };
    }

    public static string ScanMode(string mode) => mode switch
    {
        "library" => T("web.admin.common.scans.mode.library"),
        "subtree" => T("web.admin.common.scans.mode.subtree"),
        "file" => T("web.admin.common.scans.mode.file"),
        _ => mode,
    };

    /// <summary>The last path segment, or "Entire library" for a library scan.</summary>
    public static string ScanTarget(string? path)
    {
        if (string.IsNullOrEmpty(path)) return T("web.admin.common.scans.entireLibrary");
        var segments = path.TrimEnd('/').Split('/');
        return segments[^1].Length > 0 ? segments[^1] : path;
    }

    public static string ScanTrigger(string trigger) => trigger switch
    {
        "autoscan" => T("web.admin.common.scans.trigger.autoscan"),
        "library_created" => T("web.admin.common.scans.trigger.created"),
        "library_paths_changed" or "library_updated" => T("web.admin.common.scans.trigger.updated"),
        "library_id" or "manual" => T("web.admin.common.scans.trigger.manual"),
        "task:scan_libraries" or "task_scan_libraries" => T("web.admin.common.scans.trigger.scheduled"),
        "realtime_monitor" => T("web.admin.common.scans.trigger.fileChange"),
        _ => trigger.Replace('_', ' '),
    };

    public static string ScanStarted(DateTimeOffset? started, DateTimeOffset now)
    {
        if (started is not { } value) return T("web.admin.common.scans.started");
        var seconds = Math.Max(0, (long)Math.Floor((now - value).TotalSeconds));
        if (seconds < 60) return T("web.admin.common.scans.startedJustNow");
        if (seconds < 3600) return T("web.admin.common.scans.startedMinutesAgo", ("count", seconds / 60));
        if (seconds < 86400) return T("web.admin.common.scans.startedHoursAgo", ("count", seconds / 3600));
        return T("web.admin.common.scans.startedDaysAgo", AdminLibrariesText.Count(seconds / 86400));
    }

    public static string ScanProgress(AdminLibraryScanRun scan)
    {
        if (scan.TotalFiles is > 0 && scan.FilesProcessed is > 0)
        {
            var percent = (int)Math.Clamp(Math.Round(scan.FilesProcessed.Value * 100d / scan.TotalFiles.Value), 0, 100);
            return T("web.admin.common.scans.progress",
                ("message", scan.Message ?? T("web.admin.common.scans.processingFiles")),
                ("processed", scan.FilesProcessed.Value.ToString("N0", AppStrings.FormatCulture)),
                ("total", scan.TotalFiles.Value.ToString("N0", AppStrings.FormatCulture)),
                ("percent", percent));
        }
        return scan.Message ?? "";
    }

    /// <summary>Parses one <c>scans</c> channel item (snapshot element or event payload); rows without id or status are dropped.</summary>
    public static AdminLibraryScanRun? ParseScan(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = Text(item, "id");
        var status = Text(item, "status");
        var library = Text(item, "library_id");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(status) || string.IsNullOrEmpty(library)) return null;
        DateTimeOffset? started = DateTimeOffset.TryParse(Text(item, "started_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
        long? total = null, processed = null;
        string? message = null;
        if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            total = Number(result, "total_files");
            processed = Number(result, "files_processed");
            message = Text(result, "message");
        }
        return new AdminLibraryScanRun(id, library, Text(item, "mode") ?? "", Text(item, "path"), status, Text(item, "trigger"), started, total, processed, message);

        static string? Text(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var value) ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            } : null;
        static long? Number(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    }

    // ── Jobs (api/v2/libraries.ts adminJobFromV2, lib/adminJobs.ts) ─────

    /// <summary>The job state as the WebUI shows it: succeeded→completed, canceled→cancelled, canceling stays running.</summary>
    public static string JobStatus(AdminLibraryJob job) => job.State switch
    {
        "succeeded" => "completed",
        "canceled" => "cancelled",
        "canceling" => "running",
        _ => job.State,
    };

    public static bool IsActiveJob(AdminLibraryJob job) => JobStatus(job) is "queued" or "running";

    /// <summary>The refresh job's library: <c>library_id</c>, else the refresh result's, else the only listed library.</summary>
    public static string? JobLibraryId(AdminLibraryJob job)
    {
        if (AdminLibraryIds.IsId(job.LibraryId)) return job.LibraryId;
        if (AdminLibraryIds.IsId(job.RefreshResult?.LibraryId)) return job.RefreshResult!.LibraryId;
        return job.LibraryIds is [var only] && AdminLibraryIds.IsId(only) ? only : null;
    }

    /// <summary>An active job's line: "Cancellation requested" while canceling, otherwise the queued fallback.</summary>
    public static string JobMessage(AdminLibraryJob job) =>
        job.State == "canceling" ? AppStrings.Get("web.common.job.cancellationRequested", fallback: "Cancellation requested")
            : T("web.admin.libraries.activeWork.refreshQueued");

    /// <summary>WebUI <c>formatJobProgress</c>.</summary>
    public static string JobProgress(AdminLibraryJob job)
    {
        if (job.Progress is { Total: > 0 } progress)
            return progress.Current.ToString("N0", AppStrings.FormatCulture) + " / " + progress.Total.ToString("N0", AppStrings.FormatCulture);
        return JobStatus(job) switch
        {
            "queued" => T("web.admin.catalogMaintenance.progress.queued"),
            "completed" => T("web.admin.catalogMaintenance.progress.complete"),
            "cancelled" => T("web.admin.catalogMaintenance.progress.cancelled"),
            _ => T("web.admin.catalogMaintenance.progress.starting"),
        };
    }

    // ── Rows (useLibraryRows.ts, LibraryRowParts.tsx) ───────────────────

    public static bool HasScanWarning(AdminLibrary library) => library.ScanWarningCode is "empty_root" or "dead_root" or "partial_walk";

    public static bool CanConfirmCleanup(AdminLibrary library) => library.ScanWarningCode is "empty_root" or "dead_root";

    /// <summary>WebUI <c>formatMountCheckMessage</c>.</summary>
    public static string MountCheckMessage(AdminLibraryMountCheck result)
    {
        var failure = result.Roots.FirstOrDefault(root => !root.Reachable);
        if (!result.Healthy && failure != null)
            return string.IsNullOrEmpty(failure.ErrorMessage)
                ? T("web.admin.libraries.mountCheck.failure", ("summary", result.Summary), ("path", failure.Path))
                : T("web.admin.libraries.mountCheck.failureWithError", ("summary", result.Summary), ("path", failure.Path), ("error", failure.ErrorMessage));
        return result.Summary;
    }

    public static (string Title, string Hint) ScanWarningText(AdminLibrary library) => library.ScanWarningCode switch
    {
        "partial_walk" => (T("web.admin.libraries.list.partialScan"), library.ScanWarningMessage ?? T("web.admin.libraries.warning.partialScanHint")),
        "dead_root" => (T("web.admin.libraries.warning.deadRoot"), library.ScanWarningMessage ?? T("web.admin.libraries.warning.deadRootHint")),
        _ => (T("web.admin.libraries.warning.emptyRoot"), library.ScanWarningMessage ?? T("web.admin.libraries.warning.emptyRootHint")),
    };

    /// <summary>WebUI <c>formatDateTime</c>: the numeric date and the time with seconds, in the interface language.</summary>
    public static string DateTimeText(DateTimeOffset value)
    {
        var culture = AppStrings.FormatCulture;
        var local = value.ToLocalTime();
        return local.ToString("d", culture) + ", " + local.ToString("T", culture);
    }

    /// <summary>The last segment of a filesystem path, which names a root in the diagnostics lists.</summary>
    public static string PathLeaf(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : path;
    }

    // ── Diagnostics (MetadataMatcherQueuesSection, AmbiguousRootsSection) ─

    /// <summary>One queued movie, series root or raw file in a library's matcher detail list.</summary>
    public sealed record MatcherEntry(string Key, string Path, string State, string? FailureKind, string? Message, string? Rejection,
        IReadOnlyList<MatcherCandidate> Candidates);

    public sealed record MatcherCandidate(string Title, string Score, string? MatchedTitle, string? Reasons, string? Sources);

    public static IReadOnlyList<MatcherEntry> MatcherEntries(AdminMatchQueueDetail detail)
    {
        var entries = new List<MatcherEntry>();
        foreach (var movie in detail.Movies)
            entries.Add(QueueEntry("movie-" + movie.MediaFileId, movie.FilePath ?? "", movie));
        foreach (var series in detail.Series)
            entries.Add(QueueEntry("series-" + series.LibraryId + "-" + series.ObservedRootPath, series.ObservedRootPath ?? "", series));
        foreach (var raw in detail.RawFiles)
        {
            var identity = string.Join(" ", new[]
            {
                raw.BaseTitle ?? "", raw.BaseYear is > 0 ? "(" + raw.BaseYear + ")" : "", string.IsNullOrEmpty(raw.BaseType) ? "" : "[" + raw.BaseType + "]",
            }.Where(part => part.Length > 0));
            entries.Add(new MatcherEntry("raw-" + raw.MediaFileId, raw.FilePath, "pending", null,
                identity.Length > 0 ? T("web.admin.common.libraryEditor.matcher.awaitingMatchFor", ("identity", identity)) : T("web.admin.common.libraryEditor.matcher.awaitingMatch"),
                null, []));
        }
        return entries;
    }

    private static MatcherEntry QueueEntry(string key, string path, AdminMatchQueueEntry entry)
    {
        string? message = entry.LastError;
        string? rejection = null;
        var candidates = new List<MatcherCandidate>();
        if (entry.FailureDetail is { ValueKind: JsonValueKind.Object } detail)
        {
            if (detail.TryGetProperty("message", out var detailMessage) && detailMessage.ValueKind == JsonValueKind.String) message = detailMessage.GetString();
            if (detail.TryGetProperty("decision", out var decision) && decision.ValueKind == JsonValueKind.Object)
            {
                rejection = Rejection(decision);
                var bar = ScoreBar(decision);
                if (decision.TryGetProperty("top_candidates", out var top) && top.ValueKind == JsonValueKind.Array)
                    foreach (var candidate in top.EnumerateArray().Take(10))
                    {
                        if (candidate.ValueKind != JsonValueKind.Object) continue;
                        var title = Str(candidate, "title") ?? "";
                        var year = candidate.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var parsedYear) && parsedYear > 0 ? parsedYear : (int?)null;
                        var score = candidate.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0;
                        var scoreText = score.ToString("0.0", AppStrings.FormatCulture);
                        var matched = Str(candidate, "matched_title");
                        candidates.Add(new MatcherCandidate(year is { } shownYear ? title + " (" + shownYear + ")" : title,
                            bar is { } threshold
                                ? T("web.admin.common.libraryEditor.matcher.scoreOfBar", ("score", scoreText), ("bar", threshold.ToString(AppStrings.FormatCulture)))
                                : T("web.admin.common.libraryEditor.matcher.score", ("score", scoreText)),
                            matched != null && matched != title ? T("web.admin.common.libraryEditor.matcher.matchedTitle", ("title", matched)) : null,
                            List(candidate, "reasons"),
                            List(candidate, "sources") is { } sources ? T("web.admin.common.libraryEditor.matcher.via", ("sources", sources)) : null));
                    }
            }
        }
        return new MatcherEntry(key, path, entry.State, entry.FailureKind, message, rejection, candidates);

        static string? Str(JsonElement owner, string name) =>
            owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        static string? List(JsonElement owner, string name)
        {
            if (!owner.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
            var items = value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Take(20).ToArray();
            return items.Length == 0 ? null : string.Join(", ", items);
        }
    }

    private static string? Rejection(JsonElement decision)
    {
        if (!decision.TryGetProperty("outcome", out var outcome) || outcome.GetString() != "candidate_rejected") return null;
        var rejection = decision.TryGetProperty("rejection", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return rejection switch
        {
            "below_floor" => T("web.admin.common.libraryEditor.matcher.belowFloor"),
            "lone_candidate" => T("web.admin.common.libraryEditor.matcher.loneCandidate"),
            "year_mismatch" => T("web.admin.common.libraryEditor.matcher.yearMismatch"),
            "ambiguous" => T("web.admin.common.libraryEditor.matcher.ambiguous"),
            "trusted_id_missing" => T("web.admin.common.libraryEditor.matcher.trustedIdMissing"),
            _ => null,
        };
    }

    private static double? ScoreBar(JsonElement decision)
    {
        if (decision.TryGetProperty("required_score", out var required) && required.ValueKind == JsonValueKind.Number && required.GetDouble() != 0) return required.GetDouble();
        var hasRejection = decision.TryGetProperty("rejection", out var rejection) && rejection.ValueKind == JsonValueKind.String && rejection.GetString()!.Length > 0;
        if (hasRejection) return null;
        return decision.TryGetProperty("threshold", out var threshold) && threshold.ValueKind == JsonValueKind.Number ? threshold.GetDouble() : null;
    }

    public static string MatcherState(string state) => state switch
    {
        "pending" => T("web.admin.common.libraryEditor.matcher.statePending"),
        "parked" => T("web.admin.common.libraryEditor.matcher.stateParked"),
        _ => state,
    };

    /// <summary>WebUI <c>buildRootEvidenceSummary</c>.</summary>
    public static string? RootEvidence(AdminLibraryRoot root)
    {
        if (root.EvidenceJson is not { ValueKind: JsonValueKind.Object } evidence) return null;
        var parts = new List<string>();
        if (evidence.TryGetProperty("has_folder_ids", out var ids) && ids.ValueKind is JsonValueKind.True or JsonValueKind.False)
            parts.Add(T(ids.GetBoolean() ? "web.admin.libraries.ambiguousRoots.evidence.folderIds" : "web.admin.libraries.ambiguousRoots.evidence.noFolderIds"));
        void Count(string name, string key)
        {
            if (evidence.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count > 0)
                parts.Add(T(key, AdminLibrariesText.Count(count)));
        }
        Count("season_structure_files", "web.admin.libraries.ambiguousRoots.evidence.seasonFiles");
        Count("movie_evidence_files", "web.admin.libraries.ambiguousRoots.evidence.movieFiles");
        Count("wrapper_collapses", "web.admin.libraries.ambiguousRoots.evidence.wrapperCollapses");
        Count("ancestor_promotions", "web.admin.libraries.ambiguousRoots.evidence.ancestorPromotions");
        return string.Join(" · ", parts);
    }

    /// <summary>The content types an admin can mark as intentionally unmatched (books keep their own enrichment).</summary>
    public static bool CanIgnore(AdminUnmatchedItem item) => item.ContentType is "movie" or "series";

    /// <summary>Failure text for this domain's reads and writes; never a raw stack or response body.</summary>
    public static string Describe(Exception error, string fallbackKey) => error switch
    {
        AdminLibraryIdentityException or AdminListInconsistentException or InvalidDataException or JsonException => T("admin.native.libraries.invalid"),
        AdminLibraryPosterException => T("admin.native.libraries.posterInvalid"),
        MonitorAuthorityRetiredException => T("admin.native.libraries.retired"),
        ApiException api => ApiProblemText.Display(api),
        OperationCanceledException { InnerException: TimeoutException } => T("admin.native.libraries.outcomeUnknown"),
        HttpRequestException or IOException => T("admin.native.libraries.transport"),
        _ => T(fallbackKey),
    };

    /// <summary>
    /// A failed write whose outcome cannot be known (transport loss or timeout
    /// after sending, or a success answer that could not be interpreted): the
    /// change may have been applied, so the page reloads state instead of
    /// reporting a clean failure or replaying the write.
    /// </summary>
    public static bool IsUnknownOutcome(Exception error) =>
        error is HttpRequestException or InvalidDataException or JsonException or AdminLibraryIdentityException
        || error is IOException and not AdminLibraryPosterException
        || error is OperationCanceledException { InnerException: TimeoutException };
}
