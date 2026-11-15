using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>
/// One stored subtitle row (v2 <c>AdminStoredSubtitle</c>, official
/// internal/apiv2/admin_subtitle_list.go). IDs stay opaque decimal strings;
/// they can exceed a double's integer range.
/// </summary>
public sealed class AdminStoredSubtitle
{
    public string Id { get; set; } = "";
    public string MediaFileId { get; set; } = "";
    public string? MediaContentId { get; set; }
    public string Provider { get; set; } = "";
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public double Score { get; set; }
    public bool HearingImpaired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? DownloadedBy { get; set; }
    public string UploaderUsername { get; set; } = "";
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    /// <summary>Administrator-only source path; shown as a basename with the full path as a tooltip.</summary>
    public string FilePath { get; set; } = "";
}

/// <summary>v2 <c>AdminStoredSubtitleCollection</c>: one cursor page plus counts for the whole filtered set.</summary>
public sealed class AdminStoredSubtitleCollection
{
    public List<AdminStoredSubtitle> Items { get; set; } = [];
    public Api.ApiPageInfo? Page { get; set; }
    public int Total { get; set; }
    public int Uploads { get; set; }
    public int ProviderDownloads { get; set; }
}

/// <summary>v2 <c>AdminSubtitleMetadata</c>: the editable record read with its strong validator.</summary>
public sealed class AdminSubtitleMetadata
{
    public string Id { get; set; } = "";
    public string MediaFileId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public double Score { get; set; }
    public bool HearingImpaired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? DownloadedBy { get; set; }
}

/// <summary>Uploader picker entry projected from v2 <c>AdminUser</c> (id and username only).</summary>
public sealed class AdminSubtitleUploader
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
}

/// <summary>
/// The list filters exactly as WebUI <c>AdminSubtitles</c> sends them: blank
/// values are omitted, <c>q</c> is trimmed, the page size is 25, 50 or 100.
/// </summary>
public sealed record AdminSubtitleListQuery
{
    public static readonly IReadOnlyList<int> PageSizes = [25, 50, 100];
    public const int DefaultPageSize = 25;
    public const int MaxProviderLength = 128;
    public const int MaxLanguageLength = 128;
    public const int MaxSearchLength = 1024;

    public string? Provider { get; init; }
    public string? Language { get; init; }
    public string? UserId { get; init; }
    public string? Search { get; init; }
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Normalizes to the values the request sends and rejects values the server contract cannot accept.</summary>
    public AdminSubtitleListQuery Normalize()
    {
        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) || value == AdminSubtitleIds.FilterAll ? null : value;
        var provider = Blank(Provider)?.Trim();
        var language = Blank(Language)?.Trim();
        var user = Blank(UserId)?.Trim();
        var search = Blank(Search)?.Trim();
        if (provider is { Length: > MaxProviderLength }) provider = provider[..MaxProviderLength];
        if (language is { Length: > MaxLanguageLength }) language = language[..MaxLanguageLength];
        if (search is { Length: > MaxSearchLength }) search = search[..MaxSearchLength];
        // A malformed uploader id would be a server validation failure; drop it like an unknown Select value.
        if (user != null && !AdminSubtitleIds.IsId(user)) user = null;
        return new AdminSubtitleListQuery
        {
            Provider = provider, Language = language, UserId = user, Search = search,
            PageSize = PageSizes.Contains(PageSize) ? PageSize : DefaultPageSize,
        };
    }

    public bool HasActiveFilters => Provider != null || Language != null || UserId != null || !string.IsNullOrWhiteSpace(Search);

    /// <summary>The query string for one page (always after <see cref="Normalize"/>).</summary>
    public string ToQueryString(string? cursor)
    {
        var parts = new List<string>();
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) parts.Add(key + "=" + Uri.EscapeDataString(value)); }
        Add("provider", Provider);
        Add("language", Language);
        Add("user_id", UserId);
        Add("q", Search);
        parts.Add("limit=" + PageSize);
        Add("cursor", cursor);
        return "?" + string.Join('&', parts);
    }
}

/// <summary>A changed-fields-only metadata patch. Unchanged fields are omitted, never sent as null.</summary>
public sealed record AdminSubtitlePatch(string? Language, string? ReleaseName, bool? HearingImpaired)
{
    public const int MaxReleaseNameLength = 4096;

    public bool IsEmpty => Language == null && ReleaseName == null && HearingImpaired == null;

    public static AdminSubtitlePatch Between(AdminSubtitleMetadata baseline, string language, string releaseName, bool hearingImpaired) => new(
        string.Equals(language, baseline.Language, StringComparison.Ordinal) ? null : language,
        string.Equals(releaseName, baseline.ReleaseName, StringComparison.Ordinal) ? null : releaseName,
        hearingImpaired == baseline.HearingImpaired ? null : hearingImpaired);

    /// <summary>The JSON body: only supplied keys, sent verbatim (a dictionary bypasses reflection naming).</summary>
    public Dictionary<string, object?> ToBody()
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (Language != null) body["language"] = Language;
        if (ReleaseName != null) body["release_name"] = ReleaseName;
        if (HearingImpaired is { } hearing) body["hearing_impaired"] = hearing;
        return body;
    }
}

/// <summary>Identifier and value rules shared by the subtitle API, controller and page.</summary>
public static partial class AdminSubtitleIds
{
    /// <summary>The WebUI's "all" sentinel for provider/language/uploader filters.</summary>
    public const string FilterAll = "all";

    /// <summary>A positive decimal id without leading zeros (WebUI <c>/^[1-9]\d*$/</c>).</summary>
    public static bool IsId(string? value) => value != null && IdPattern().IsMatch(value);

    /// <summary>The server's PATCH language pattern (BCP 47 / ISO 639, any case, surrounding space allowed).</summary>
    public static bool IsPatchLanguage(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= AdminSubtitleListQuery.MaxLanguageLength && PatchLanguagePattern().IsMatch(value);

    /// <summary>Subtitle formats the server names downloads with; anything else becomes <c>.bin</c>.</summary>
    public static readonly IReadOnlyList<string> DownloadFormats = ["srt", "vtt", "ass", "ssa", "sub"];

    /// <summary>Deterministic download filename (WebUI <c>subtitle-{id}.{format}</c>); never derived from a response header.</summary>
    public static string DownloadFileName(AdminStoredSubtitle subtitle)
    {
        if (!IsId(subtitle.Id)) throw new ArgumentException("Invalid subtitle ID.", nameof(subtitle));
        return "subtitle-" + subtitle.Id + "." + DownloadExtension(subtitle.Format);
    }

    public static string DownloadExtension(string? format) => format != null && DownloadFormats.Contains(format, StringComparer.Ordinal) ? format : "bin";

    [GeneratedRegex("^[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[\s\v\u0085\p{Z}]*(?:[A-Za-z]{2,3}(?:[-_][0-9A-Za-z]{1,8})*|[XxIi](?:[-_][0-9A-Za-z]{1,8})+)[\s\v\u0085\p{Z}]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PatchLanguagePattern();
}
