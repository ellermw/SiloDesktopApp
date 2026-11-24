using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Helpers;

/// <summary>
/// Pure presentation rules of the current WebUI ebook detail page
/// (Silo-Server web/src/pages/ItemDetail/EbookContent.tsx, redesigned in
/// #4645 to share the audiobook layout). Kept free of WinUI so the rules are
/// unit-tested; the detail page only lays the results out.
/// </summary>
public static class EbookDetailPresentation
{
    /// <summary>One fact of the bordered facts row.</summary>
    public sealed record Fact(string LabelKey, string EnglishLabel, string Value);

    /// <summary>
    /// What the side column's actions show. The Read button needs a
    /// reader-supported file; with none, Download takes its place when the
    /// profile may download; with both, Download joins Mark read in the
    /// secondary row.
    /// </summary>
    public sealed record ActionState(
        bool ShowRead,
        bool ContinueLabel,
        bool ShowPrimaryDownload,
        bool ShowSecondaryDownload,
        bool ShowProgress,
        bool IsRead,
        int ProgressPercent);

    /// <summary>The 1-99 percentage shown while a book is partway through.</summary>
    public static int ClampInProgressPercent(double ratio)
        => Math.Min(99, Math.Max(1, (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero)));

    /// <summary>
    /// Continue and the bar need a visible reader-supported file with an
    /// unfinished saved position, so a finished book or a scoped library
    /// missing the saved edition never advertises a resume that starts at
    /// zero. <paramref name="progress"/> is already tied to the read file.
    /// </summary>
    public static ActionState BuildActions(bool isRead, bool canRead, double progress, bool canDownload)
    {
        var inProgress = !isRead && canRead && double.IsFinite(progress) && progress > 0 && progress < 1;
        return new ActionState(
            ShowRead: canRead,
            ContinueLabel: inProgress,
            ShowPrimaryDownload: !canRead && canDownload,
            ShowSecondaryDownload: canRead && canDownload,
            ShowProgress: inProgress || isRead,
            IsRead: isRead,
            ProgressPercent: isRead ? 100 : inProgress ? ClampInProgressPercent(progress) : 0);
    }

    /// <summary>The extension of the file, else its container: how the reader and the Format fact name it.</summary>
    public static string FileFormat(FileVersion version)
    {
        var extension = Path.GetExtension(version.FileName ?? version.FilePath ?? "").TrimStart('.');
        return (string.IsNullOrWhiteSpace(extension) ? version.Container ?? "" : extension).Trim().TrimStart('.');
    }

    /// <summary>"EPUB, PDF": each distinct file format once, in file order.</summary>
    public static string Formats(IEnumerable<FileVersion> versions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var formats = new List<string>();
        foreach (var version in versions)
        {
            var raw = FileFormat(version).ToUpperInvariant();
            if (raw.Length > 0 && seen.Add(raw)) formats.Add(raw);
        }
        return string.Join(", ", formats);
    }

    /// <summary>
    /// formatFileSize: 1024-based, one decimal, GB/MB/KB labels; nothing for
    /// an unknown size.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "";
        string Size(double value) => value.ToString("F1", AppStrings.FormatCulture);
        if (bytes >= 1024L * 1024 * 1024)
            return AppStrings.Get("web.common.format.size.gb", new Dictionary<string, object?> { ["size"] = Size(bytes / (1024d * 1024 * 1024)) }, fallback: Size(bytes / (1024d * 1024 * 1024)) + " GB");
        if (bytes >= 1024L * 1024)
            return AppStrings.Get("web.common.format.size.mb", new Dictionary<string, object?> { ["size"] = Size(bytes / (1024d * 1024)) }, fallback: Size(bytes / (1024d * 1024)) + " MB");
        if (bytes >= 1024)
            return AppStrings.Get("web.common.format.size.kb", new Dictionary<string, object?> { ["size"] = Size(bytes / 1024d) }, fallback: Size(bytes / 1024d) + " KB");
        return AppStrings.Get("web.common.format.size.bytes", new Dictionary<string, object?> { ["size"] = bytes.ToString(AppStrings.FormatCulture) }, fallback: bytes + " B");
    }

    /// <summary>
    /// The facts row: Format, File Size, Released, Publisher; a fact with no
    /// value is left out and a book with none shows no row.
    /// </summary>
    public static IReadOnlyList<Fact> Facts(IEnumerable<FileVersion> versions, FileVersion? primaryFile, int year, string? publisher)
    {
        var facts = new List<Fact>();
        var formats = Formats(versions);
        if (formats.Length > 0) facts.Add(new("web.detail.mediaInfo.row.format", "Format", formats));
        var size = primaryFile is { FileSize: > 0 } ? FormatFileSize(primaryFile.FileSize) : "";
        if (size.Length > 0) facts.Add(new("web.detail.mediaInfo.row.fileSize", "File Size", size));
        if (year > 0) facts.Add(new("web.audiobooks.detail.released", "Released", year.ToString(AppStrings.FormatCulture)));
        if (!string.IsNullOrWhiteSpace(publisher)) facts.Add(new("web.audiobooks.detail.publisher", "Publisher", publisher));
        return facts;
    }

    /// <summary>The book's publisher: the ebook extension's, else the first studio.</summary>
    public static string? Publisher(string? extensionPublisher, IReadOnlyList<string>? studios)
        => !string.IsNullOrWhiteSpace(extensionPublisher) ? extensionPublisher
            : studios is { Count: > 0 } && !string.IsNullOrWhiteSpace(studios[0]) ? studios[0] : null;

    /// <summary>
    /// relatedStatus: a related book's line under its title: Read when
    /// finished, the reading percentage while partway through, else its
    /// publication year.
    /// </summary>
    public static string? RelatedStatus(AudiobookRelatedItem entry)
    {
        if (entry.Played == true)
            return AppStrings.Get("web.detail.manga.readStatus", fallback: "Read");
        if (entry.ProgressRatio is > 0)
            return AppStrings.Get("web.home.continueWatching.percentRead",
                new Dictionary<string, object?> { ["percent"] = ClampInProgressPercent(entry.ProgressRatio.Value) },
                fallback: $"{ClampInProgressPercent(entry.ProgressRatio.Value)}% read");
        return entry.Year is > 0 ? entry.Year.Value.ToString(AppStrings.FormatCulture) : null;
    }

    /// <summary>The cover progress bar fraction; none once finished.</summary>
    public static double? RelatedProgress(AudiobookRelatedItem entry)
        => entry.Played == true || entry.ProgressRatio is not > 0 ? null : Math.Clamp(entry.ProgressRatio.Value, 0, 1);
}
