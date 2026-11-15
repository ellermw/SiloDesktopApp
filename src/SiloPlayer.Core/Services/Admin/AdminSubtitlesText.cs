using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Text and presentation rules for Libraries › Subtitle Files. Keys and
/// English values are copied verbatim from the official WebUI catalog
/// (<c>web/src/locales/en.json</c> at fd0d8c6c09af11aa2b165034e6c553a45334f2fd);
/// the generated interface catalogs exclude <c>web.admin.*</c>, so this
/// domain owns its English table exactly like <see cref="AdminText"/>. Keys
/// outside <c>web.admin.</c> (<c>common.*</c>, <c>web.common.*</c>,
/// <c>web.shell.*</c>, <c>language.*</c>) come from the localized
/// <see cref="AppStrings"/> catalog. The few <c>admin.native.subtitles.*</c>
/// keys describe native-only states (bounded picker, byte cap, in-place
/// reload) that the WebUI has no message for; they are English only.
/// </summary>
public static class AdminSubtitlesText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.common.subtitles.delete.close"] = "Close",
        ["web.admin.common.subtitles.delete.confirm"] = "Delete",
        ["web.admin.common.subtitles.delete.confirmFailed"] = "Unable to confirm deletion.",
        ["web.admin.common.subtitles.delete.deleted"] = "Subtitle metadata deleted",
        ["web.admin.common.subtitles.delete.description"] = "Remove {language} subtitle “{name}”? Metadata is removed first; stored-file cleanup is best effort.",
        ["web.admin.common.subtitles.delete.errorHint"] = "{error} Close and review the current subtitle before another attempt.",
        ["web.admin.common.subtitles.delete.loadFailed"] = "Unable to load subtitle.",
        ["web.admin.common.subtitles.delete.loading"] = "Loading the current subtitle before confirmation…",
        ["web.admin.common.subtitles.delete.title"] = "Delete subtitle?",
        ["web.admin.common.subtitles.edit.description"] = "Update stored metadata for this subtitle record. File content is not replaced.",
        ["web.admin.common.subtitles.edit.failureHint"] = "{error} Your edits remain here. Close and reopen to review current metadata before another save.",
        ["web.admin.common.subtitles.edit.hearingImpaired"] = "Hearing impaired",
        ["web.admin.common.subtitles.edit.hearingImpairedHelp"] = "Marks this track as SDH/CC.",
        ["web.admin.common.subtitles.edit.language"] = "Language",
        ["web.admin.common.subtitles.edit.languageChanged"] = "Updates stored language label; file content unchanged.",
        ["web.admin.common.subtitles.edit.loadFailed"] = "Unable to load subtitle metadata.",
        ["web.admin.common.subtitles.edit.loadHint"] = "Load current metadata before making changes.",
        ["web.admin.common.subtitles.edit.loading"] = "Loading subtitle metadata…",
        ["web.admin.common.subtitles.edit.releaseName"] = "Release name",
        ["web.admin.common.subtitles.edit.saveChanges"] = "Save changes",
        ["web.admin.common.subtitles.edit.saveFailed"] = "Save was not confirmed.",
        ["web.admin.common.subtitles.edit.title"] = "Edit subtitle",
        ["web.admin.common.subtitles.filters.allLanguages"] = "All languages",
        ["web.admin.common.subtitles.filters.allUploaders"] = "All uploaders",
        ["web.admin.common.subtitles.filters.languageLabel"] = "Filter by language",
        ["web.admin.common.subtitles.filters.languagePlaceholder"] = "Language",
        ["web.admin.common.subtitles.filters.providerGroup"] = "Filter by subtitle provider",
        ["web.admin.common.subtitles.filters.searchLabel"] = "Search subtitle release name",
        ["web.admin.common.subtitles.filters.searchPlaceholder"] = "Search release name…",
        ["web.admin.common.subtitles.filters.uploaderLabel"] = "Filter by uploader",
        ["web.admin.common.subtitles.filters.uploaderPlaceholder"] = "Uploader",
        ["web.admin.common.subtitles.languageCode"] = "{code} · {language}",
        ["web.admin.common.subtitles.provider.all"] = "All",
        ["web.admin.common.subtitles.provider.upload"] = "Upload",
        ["web.admin.common.subtitles.resetFilters"] = "Reset filters",
        ["web.admin.common.subtitles.table.column.actions"] = "Actions",
        ["web.admin.common.subtitles.table.column.added"] = "Added",
        ["web.admin.common.subtitles.table.column.file"] = "File",
        ["web.admin.common.subtitles.table.column.format"] = "Format",
        ["web.admin.common.subtitles.table.column.hearingImpaired"] = "HI",
        ["web.admin.common.subtitles.table.column.language"] = "Language",
        ["web.admin.common.subtitles.table.column.media"] = "Media",
        ["web.admin.common.subtitles.table.column.provider"] = "Provider",
        ["web.admin.common.subtitles.table.column.release"] = "Release",
        ["web.admin.common.subtitles.table.column.uploader"] = "Uploader",
        ["web.admin.common.subtitles.table.delete"] = "Delete subtitle {id}",
        ["web.admin.common.subtitles.table.download"] = "Download subtitle {id}",
        ["web.admin.common.subtitles.table.downloadFailed"] = "Failed to download subtitle",
        ["web.admin.common.subtitles.table.downloaded"] = "Subtitle downloaded",
        ["web.admin.common.subtitles.table.edit"] = "Edit subtitle {id}",
        ["web.admin.common.subtitles.table.empty"] = "No stored subtitles yet",
        ["web.admin.common.subtitles.table.emptyFiltered"] = "No subtitles match these filters",
        ["web.admin.common.subtitles.table.emptyFilteredHelp"] = "Try widening the provider, language, or uploader filters to see more results.",
        ["web.admin.common.subtitles.table.emptyHelp"] = "User uploads and provider downloads will appear here.",
        ["web.admin.common.subtitles.table.episode"] = "Episode",
        ["web.admin.common.subtitles.table.hearingImpaired"] = "HI",
        ["web.admin.common.subtitles.table.reloadBeforeEdit"] = "Reload subtitles before editing.",
        ["web.admin.common.subtitles.unknownMedia"] = "Unknown media",
        ["web.admin.errors.subtitleDeleteAttempted"] = "This deletion was already attempted. Close and review the subtitle again.",
        ["web.admin.errors.subtitleDownloadFailed"] = "Subtitle download failed ({status}).",
        ["web.admin.errors.subtitleDownloadUnexpected"] = "Unexpected subtitle download response.",
        ["web.admin.errors.subtitleIdentityChanged"] = "Subtitle identity changed. Reload the list.",
        ["web.admin.errors.subtitleListInvalid"] = "Invalid subtitle list. Reload the page.",
        ["web.admin.errors.subtitleValidatorRequired"] = "A strong subtitle edit validator is required. Reopen the editor.",
        ["web.admin.subtitles.page.loadFailed"] = "Unable to load subtitles. {error}",
        ["web.admin.subtitles.page.next"] = "Next",
        ["web.admin.subtitles.page.pageNumber"] = "Page {page}",
        ["web.admin.subtitles.page.pageSizeOption"] = "{count, plural, one {# / page} other {# / page}}",
        ["web.admin.subtitles.page.previous"] = "Previous",
        ["web.admin.subtitles.page.subtitle"] = "Manage stored subtitle files across the library — user uploads and provider downloads.",
        ["web.admin.subtitles.page.summary"] = "{count, plural, one {# subtitle on this page; {total} currently match} other {# subtitles on this page; {total} currently match}}",
        ["web.admin.subtitles.page.title"] = "Subtitles",
        ["web.admin.subtitles.stats.languagesOnPage"] = "Languages on page",
        ["web.admin.subtitles.stats.providerDownloads"] = "Provider downloads",
        ["web.admin.subtitles.stats.totalStored"] = "Total stored",
        ["web.admin.subtitles.stats.userUploads"] = "User uploads",
        ["web.admin.subtitles.toast.updated"] = "Subtitle updated",

        // Native-only states (no WebUI message exists). English only; see the class remarks.
        ["admin.native.subtitles.uploadersTruncated"] = "Only the first {count_text} accounts are listed.",
        ["admin.native.subtitles.uploadersFailed"] = "Uploaders could not be loaded.",
        ["admin.native.subtitles.uploaderId"] = "Account {id}",
        ["admin.native.subtitles.reload"] = "Reload current metadata",
        ["admin.native.subtitles.downloadTooLarge"] = "The subtitle file is larger than the {megabytes} MB download limit.",
        ["admin.native.subtitles.fileType"] = "Subtitle file",
        ["admin.native.subtitles.retired"] = "The admin session changed. Reload and try again.",
        ["admin.native.subtitles.transport"] = "The server could not be reached. Try again.",
    };

    /// <summary>Every key this domain owns, for catalog-coverage tests.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = English.Keys.ToArray();

    public static bool Has(string key) => English.ContainsKey(key);

    /// <summary>Formats a domain key from the English table, or any other key from the localized interface catalog.</summary>
    public static string T(string key, params (string Name, object? Value)[] values)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values) arguments[name] = value;
        if (English.TryGetValue(key, out var pattern)) return AdminText.Format(pattern, arguments);
        return AppStrings.Get(key, arguments, fallback: key);
    }

    // ── Providers (subtitleAdminStyles.ts) ────────────────────────────────

    public sealed record ProviderOption(string Value, string? LabelKey, string? FixedLabel)
    {
        /// <summary>Provider names stay as the provider spells them; the page's own options translate.</summary>
        public string Label => LabelKey != null ? T(LabelKey) : FixedLabel ?? Value;
    }

    public static IReadOnlyList<ProviderOption> ProviderOptions { get; } =
    [
        new(AdminSubtitleIds.FilterAll, "web.admin.common.subtitles.provider.all", null),
        new("upload", "web.admin.common.subtitles.provider.upload", null),
        new("opensubtitles", null, "OpenSubtitles"),
        new("subdl", null, "SubDL"),
        new("subsource", null, "SubSource"),
    ];

    public static string ProviderLabel(string provider) =>
        ProviderOptions.FirstOrDefault(option => option.Value == provider)?.Label ?? provider;

    /// <summary>Provider badge tone (WebUI amber/sky/emerald/violet/neutral), as an ARGB tone the view tints.</summary>
    public static uint ProviderTone(string provider) => provider switch
    {
        "upload" => 0xFFF59E0B,
        "opensubtitles" => 0xFF0EA5E9,
        "subdl" => 0xFF10B981,
        "subsource" => 0xFF8B5CF6,
        _ => 0,
    };

    // ── Languages (player/utils/languageNames.ts COMMON_LANGUAGE_CODES) ──

    private static readonly string[] CommonLanguageCodes =
    [
        "en", "es", "fr", "de", "it", "pt", "pt-BR", "pt-PT", "nl", "pl", "ru", "zh", "ja", "ko", "ar", "tr", "sv", "da", "no", "fi",
        "hu", "cs", "ro", "he", "th", "vi", "el", "bg", "hr", "sk", "sl", "uk", "id", "ms", "hi", "ta", "te", "bn", "fa",
    ];

    public sealed record LanguageOption(string Code, string Label);

    /// <summary>The WebUI's common-language list, named and sorted in the interface language.</summary>
    public static IReadOnlyList<LanguageOption> LanguageOptions()
    {
        var culture = AppStrings.FormatCulture;
        return CommonLanguageCodes.Select(code => new LanguageOption(code, LanguageName(code)))
            .OrderBy(option => option.Label, StringComparer.Create(culture, ignoreCase: false)).ToArray();
    }

    /// <summary>A language's display name; region tags keep their region, unknown codes stay as stored.</summary>
    public static string LanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var exact = MediaLanguageCatalog.All.FirstOrDefault(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));
        if (exact != null && !exact.Code.Contains('-'))
            return AppStrings.Get("language." + exact.Code, fallback: exact.Label);
        if (exact != null) return exact.Label;
        var normalized = MediaLanguageCatalog.Normalize(code);
        var primary = MediaLanguageCatalog.All.FirstOrDefault(language => language.Code == normalized);
        return primary == null ? code : AppStrings.Get("language." + primary.Code, fallback: primary.Label);
    }

    /// <summary>"EN · English" (WebUI <c>web.admin.common.subtitles.languageCode</c>).</summary>
    public static string LanguageCode(string code, string? label = null) =>
        T("web.admin.common.subtitles.languageCode", ("code", code.ToUpperInvariant()), ("language", label ?? LanguageName(code)));

    // ── Rows ─────────────────────────────────────────────────────────────

    /// <summary>The file name of an administrator source path ("—" when blank).</summary>
    public static string Basename(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return "—";
        var parts = filePath.Split('/', '\\');
        return parts[^1].Length > 0 ? parts[^1] : filePath;
    }

    /// <summary>A subtitle format as a file extension (".srt"), exactly as stored.</summary>
    public static string FormatExtension(string format) => "." + format;

    /// <summary>
    /// WebUI <c>formatRelativeTime(value, {rounding: "floor", absoluteAfterDays: 30})</c>:
    /// just now, minutes, hours, days, then an absolute date.
    /// </summary>
    public static string Relative(DateTimeOffset value, DateTimeOffset now, Func<DateTimeOffset, string> absolute)
    {
        var delta = now - value;
        var minutes = Math.Floor(delta.TotalMinutes);
        if (minutes < 1) return AppStrings.Get("web.common.date.justNow", fallback: "just now");
        if (minutes < 60) return Ago("web.common.date.minutesAgo", minutes, "m ago");
        var hours = Math.Floor(delta.TotalHours);
        if (hours < 24) return Ago("web.common.date.hoursAgo", hours, "h ago");
        var days = Math.Floor(delta.TotalDays);
        if (days < 30) return Ago("web.common.date.daysAgo", days, "d ago");
        return absolute(value);

        static string Ago(string key, double count, string suffix)
        {
            var whole = (long)count;
            return AppStrings.Get(key, new Dictionary<string, object?> { ["count"] = whole, ["count_text"] = whole.ToString(AppStrings.FormatCulture) },
                fallback: whole.ToString(AppStrings.FormatCulture) + suffix);
        }
    }

    /// <summary>The page-size option label ("25 / page").</summary>
    public static string PageSizeOption(int size) => T("web.admin.subtitles.page.pageSizeOption", ("count", size));
}
