using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using Text = SiloPlayer.Core.Services.Admin.AdminPreparedFilesText;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminStorageWarning(string Id, string Tone, string Title, string Body, string? Location = null, string? Action = null);
public sealed record AdminMeterSegment(string Kind, string Label, long Bytes, double Fraction);
public sealed record AdminStorageTotals(long OnDisk, long InUse, long Cached, long Untracked, long Files);
public sealed record AdminFileRecipe(string Main, string Detail);

/// <summary>
/// Presentation rules of the official Downloads admin page (<c>downloadStoragePresentation.ts</c>, Silo Server
/// 66b836f82), shared by unit tests and the native page. Every label comes from the pinned catalog.
/// </summary>
public static class AdminPreparedFilesPresentation
{
    /// <summary>Fill at which a node stops taking playback sessions (scratch pressure).</summary>
    public const int ScratchAdmissionPercent = 95;

    public static IReadOnlyDictionary<string, object?> Count(long count, string? text = null) => Text.Count(count, text);

    /// <summary>Plural values whose number prints with the interface locale's digit grouping.</summary>
    public static IReadOnlyDictionary<string, object?> LocaleCount(long count) => Text.Count(count, count.ToString("N0", AppStrings.FormatCulture));

    public static Dictionary<string, object?> With(IReadOnlyDictionary<string, object?> values, params (string Key, object? Value)[] extra)
    {
        var result = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        foreach (var (key, value) in extra) result[key] = value;
        return result;
    }

    /// <summary>
    /// A decimal number in the format locale without digit grouping, as the WebUI's <c>formatDecimal</c>: rounded from the exact
    /// binary value (9.95 prints "9.9" with one digit, as <c>toFixed</c> does), then trailing zeros beyond
    /// <paramref name="minFractionDigits"/> dropped.
    /// </summary>
    public static string FormatDecimal(double value, int maxFractionDigits, int? minFractionDigits = null)
    {
        var text = value.ToString("F" + maxFractionDigits, CultureInfo.InvariantCulture);
        var min = minFractionDigits ?? maxFractionDigits;
        if (maxFractionDigits > min && text.Contains('.'))
        {
            var keep = text.IndexOf('.') + 1 + min;
            text = text.TrimEnd('0');
            if (text.Length < keep) text = text.PadRight(keep, '0');
            if (text.EndsWith('.')) text = text[..^1];
        }
        var separator = AppStrings.FormatCulture.NumberFormat.NumberDecimalSeparator;
        return separator == "." ? text : text.Replace(".", separator, StringComparison.Ordinal);
    }
    /// <summary>Bytes in decimal units, as drives are sold. Zero reads "0 B", not blank.</summary>
    public static string FormatBytes(long? bytes)
    {
        var value = (double)Math.Max(bytes ?? 0, 0);
        string[] units = ["web.common.format.size.bytes", "web.common.format.size.kb", "web.common.format.size.mb",
            "web.common.format.size.gb", "web.common.format.size.tb", "web.common.format.size.pb"];
        var index = 0;
        // 999.5 and up would round to "1000", so it moves to the next unit.
        while (value >= 999.5 && index < units.Length - 1) { value /= 1000; index++; }
        var size = index == 0 ? FormatDecimal(Math.Round(value), 0) : FormatDecimal(value, value >= 10 ? 0 : 1);
        return AppStrings.Get(units[index], new Dictionary<string, object?> { ["size"] = size }, fallback: size);
    }

    /// <summary>Whole percent, the way df prints it.</summary>
    public static string FormatPercent(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", AppStrings.FormatCulture) + "%";

    /// <summary>"12 min ago" style label for a past instant, rounded, or null when the instant is missing.</summary>
    public static string? Relative(DateTimeOffset? value, DateTimeOffset? now = null)
    {
        if (value is null) return null;
        var elapsed = (now ?? DateTimeOffset.UtcNow) - value.Value;
        var minutes = (long)Math.Round(elapsed.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 1) return AppStrings.Get("web.common.date.justNow");
        if (minutes < 60) return Ago("web.common.date.minutesAgo", minutes);
        var hours = (long)Math.Round(elapsed.TotalHours, MidpointRounding.AwayFromZero);
        if (hours < 24) return Ago("web.common.date.hoursAgo", hours);
        return Ago("web.common.date.daysAgo", (long)Math.Round(elapsed.TotalDays, MidpointRounding.AwayFromZero));

        static string Ago(string key, long count) => AppStrings.Get(key, Text.Count(count, count.ToString(AppStrings.FormatCulture)));
    }

    // ---- Storage overview -------------------------------------------------------------------------

    private static long DirectoryBytes(AdminDownloadStorageUsage usage) => usage.Bytes + usage.PartialBytes + usage.OtherBytes;

    /// <summary>Bytes Silo's records say the location's prepared files hold.</summary>
    public static long PreparedBytes(AdminDownloadStorageLocation location) => location.InUseBytes + location.CachedBytes;

    public static IReadOnlyList<AdminMeterSegment>? MeterSegments(AdminDownloadStorageLocation location)
    {
        var usage = location.Usage;
        if (usage == null || usage.FSTotalBytes <= 0) return null;
        var total = usage.FSTotalBytes;
        var onDisk = Math.Max(DirectoryBytes(usage), 0);
        var inUse = Math.Max(location.InUseBytes, 0);
        var cached = Math.Max(location.CachedBytes, 0);
        var untracked = Math.Min(Math.Max(location.UntrackedBytes, 0), onDisk);
        var tracked = inUse + cached;
        var room = Math.Max(onDisk - untracked, 0);
        if (tracked > room && tracked > 0)
        {
            inUse = (long)Math.Round((double)inUse / tracked * room, MidpointRounding.AwayFromZero);
            cached = room - inUse;
        }
        var used = Math.Min(Math.Max(usage.FSUsedBytes, 0), total);
        var other = Math.Max(used - inUse - cached - untracked, 0);
        var free = Math.Max(total - used, 0);
        (string Kind, long Bytes, string Key)[] parts =
        [
            ("in_use", inUse, "inUse"), ("cached", cached, "cached"), ("untracked", untracked, "untracked"),
            ("other", other, "otherData"), ("free", free, "free"),
        ];
        return parts.Select(p => new AdminMeterSegment(p.Kind, Text.T("web.admin.downloads.presentation.segment." + p.Key), p.Bytes, (double)p.Bytes / total)).ToArray();
    }

    /// <summary>How full the location's filesystem is, 0..100, or null when unmeasured.</summary>
    public static double? DiskFillPercent(AdminDownloadStorageLocation location) =>
        location.Usage is { FSTotalBytes: > 0 } usage ? (double)usage.FSUsedBytes / usage.FSTotalBytes * 100 : null;

    public static IReadOnlyList<AdminStorageWarning> StorageWarnings(AdminDownloadStorage storage)
    {
        var warnings = new List<AdminStorageWarning>();
        var ceiling = storage.DiskCeilingPercent;
        foreach (var location in storage.Locations)
        {
            var fill = DiskFillPercent(location);
            var usage = location.Usage;
            if (fill is { } percent && percent >= ceiling - 5)
                warnings.Add(new("fill:" + location.Key, "warning",
                    Text.T("web.admin.downloads.presentation.warning.fill.title", new { name = location.Name, percent = FormatPercent(percent) }),
                    usage?.SharesScratch == true
                        ? Text.T("web.admin.downloads.presentation.warning.fill.bodyScratch", new { ceiling, scratch = ScratchAdmissionPercent })
                        : Text.T("web.admin.downloads.presentation.warning.fill.body", new { ceiling }),
                    location.Key, "edit_location"));
            if (usage?.Ephemeral == true)
                warnings.Add(new("ephemeral:" + location.Key, "warning",
                    Text.T("web.admin.downloads.presentation.warning.ephemeral.title", new { name = location.Name }),
                    string.IsNullOrEmpty(usage.FSType)
                        ? Text.T("web.admin.downloads.presentation.warning.ephemeral.body")
                        : Text.T("web.admin.downloads.presentation.warning.ephemeral.bodyWithType", new { fs_type = usage.FSType }),
                    location.Key, "edit_location"));
            if (location.Kind == "node" && !location.Online)
            {
                var held = PreparedBytes(location);
                if (held > 0)
                    warnings.Add(new("offline:" + location.Key, "info",
                        Text.T("web.admin.downloads.presentation.warning.offline.title", new { name = location.Name }),
                        Text.T("web.admin.downloads.presentation.warning.offline.body", With(Count(location.WaitingDownloads), ("size", FormatBytes(held)))),
                        location.Key, "view_location"));
            }
            if (location.UntrackedBytes > 0)
                warnings.Add(new("untracked:" + location.Key, "info",
                    Text.T("web.admin.downloads.presentation.warning.untracked.title", new { size = FormatBytes(location.UntrackedBytes), name = location.Name }),
                    Text.T("web.admin.downloads.presentation.warning.untracked.body"), location.Key, "review_untracked"));
            if (location.ReplicasDisagree)
                warnings.Add(new("replicas:" + location.Key, "warning",
                    Text.T("web.admin.downloads.presentation.warning.replicas.title"),
                    Text.T("web.admin.downloads.presentation.warning.replicas.body"), location.Key, "edit_location"));
        }
        var stale = storage.Locations.Sum(x => x.StaleWaitingBytes);
        if (stale > 0)
            warnings.Add(new("stale", "info",
                Text.T("web.admin.downloads.presentation.warning.stale.title", new { size = FormatBytes(stale), days = storage.StaleDeviceDays, days_text = storage.StaleDeviceDays }),
                Text.T("web.admin.downloads.presentation.warning.stale.body"), null, "review_devices"));
        return warnings;
    }

    public static AdminStorageTotals StorageTotals(AdminDownloadStorage storage) => new(
        storage.Locations.Sum(l => l.Usage == null ? 0 : DirectoryBytes(l.Usage)),
        storage.Locations.Sum(l => l.InUseBytes), storage.Locations.Sum(l => l.CachedBytes), storage.Locations.Sum(l => l.UntrackedBytes),
        storage.Locations.Sum(l => (long)(l.Usage?.Files ?? l.InUseFiles + l.CachedFiles)));

    /// <summary>The directory, or what the node uses when none is configured or known yet.</summary>
    public static string LocationDirectoryLabel(AdminDownloadStorageLocation location) =>
        string.IsNullOrEmpty(location.Dir) ? Text.T("web.admin.downloads.presentation.directoryDefault") : location.Dir;

    public static string BudgetLabel(AdminDownloadStorageLocation location) => location.BudgetSource == "none" || location.BudgetBytes <= 0
        ? Text.T("web.admin.downloads.presentation.budget.none")
        : Text.T("web.admin.downloads.presentation.budget.amount", new { size = FormatBytes(location.BudgetBytes) });

    public static string BudgetSourceLabel(AdminDownloadStorageLocation location) => location.BudgetSource switch
    {
        "override" => Text.T("web.admin.downloads.presentation.budget.sourceCustom"),
        "setting" => Text.T("web.admin.downloads.presentation.budget.sourceDefault"),
        _ => "",
    };

    /// <summary>Records and disk agree when they are within 1 GB or 2% of each other; otherwise the signed difference.</summary>
    public static long RecordsDrift(AdminDownloadStorageLocation location)
    {
        if (location.Usage is not { } usage) return 0;
        var indexed = PreparedBytes(location);
        var drift = usage.Bytes - indexed;
        var tolerance = Math.Max(1e9, indexed * 0.02);
        return Math.Abs(drift) > tolerance ? drift : 0;
    }

    // ---- Prepared files ---------------------------------------------------------------------------

    public static string FileTitle(AdminPreparedFile file) =>
        string.IsNullOrEmpty(file.Title) ? Text.T("web.admin.downloads.presentation.file.unknownTitle") : file.Title;

    public static string FileSubtitle(AdminPreparedFile file)
    {
        if (file.SeasonNumber != null && file.EpisodeNumber != null)
        {
            var values = new Dictionary<string, object?> { ["season"] = file.SeasonNumber, ["episode"] = file.EpisodeNumber };
            if (!string.IsNullOrEmpty(file.EpisodeTitle)) values["title"] = file.EpisodeTitle;
            return Text.T(string.IsNullOrEmpty(file.EpisodeTitle)
                ? "web.admin.downloads.presentation.file.episode" : "web.admin.downloads.presentation.file.episodeWithTitle", values);
        }
        return file.Year is { } year && year != 0 ? year.ToString(CultureInfo.InvariantCulture) : "";
    }

    public static AdminFileRecipe FileRecipe(AdminPreparedFile file)
    {
        if (file.Format == "remux")
            return new(Text.T("web.admin.downloads.presentation.recipe.remux"), Text.T("web.admin.downloads.presentation.recipe.originalQuality"));
        var codec = file.VideoCodec == "hevc" ? "HEVC" : file.VideoCodec.ToUpperInvariant();
        var main = string.Join(' ', new[] { file.Resolution, codec }.Where(x => !string.IsNullOrEmpty(x)));
        var detail = file.BitrateKbps is > 0
            ? Text.T("web.admin.downloads.presentation.recipe.bitrate", new { mbps = (long)Math.Round(file.BitrateKbps.Value / 1000d, MidpointRounding.AwayFromZero) })
            : "";
        return new(main.Length == 0 ? Text.T("web.admin.downloads.presentation.recipe.transcode") : main, detail);
    }

    /// <summary>Who a file serves, in a few words.</summary>
    public static string FileUsage(AdminPreparedFile file)
    {
        var parts = new List<string>();
        if (file.Downloading > 0) parts.Add(Text.T("web.admin.downloads.presentation.usage.downloading", Count(file.Downloading)));
        if (file.Waiting > 0) parts.Add(Text.T("web.admin.downloads.presentation.usage.waiting", Count(file.Waiting)));
        if (file.Finished > 0) parts.Add(Text.T("web.admin.downloads.presentation.usage.finished", Count(file.Finished)));
        return parts.Count == 0 ? Text.T("web.admin.downloads.presentation.usage.none") : string.Join(" · ", parts);
    }

    /// <summary>"in 22 h", "in 3 d", or "now" for a cached file's expiry; empty when there is none.</summary>
    public static string FormatExpiresIn(DateTimeOffset? value, DateTimeOffset? now = null)
    {
        if (value is null) return "";
        var ms = (value.Value - (now ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
        if (ms <= 60_000) return Text.T("web.admin.downloads.presentation.expires.now");
        var hours = ms / 3_600_000;
        if (hours < 1) return Text.T("web.admin.downloads.presentation.expires.minutes", Count((long)Math.Round(ms / 60_000, MidpointRounding.AwayFromZero)));
        if (hours < 48) return Text.T("web.admin.downloads.presentation.expires.hours", Count((long)Math.Round(hours, MidpointRounding.AwayFromZero)));
        return Text.T("web.admin.downloads.presentation.expires.days", Count((long)Math.Round(hours / 24, MidpointRounding.AwayFromZero)));
    }

    // ---- History ----------------------------------------------------------------------------------

    public static readonly IReadOnlyList<string> StorageEventReasons =
        ["cache_expired", "budget", "disk_ceiling", "admin_delete", "untracked", "missing", "revoked", "device_removed"];

    /// <summary>A history reason's label; an unknown reason shows as the server sent it.</summary>
    public static string StorageEventReasonLabel(string reason) => reason switch
    {
        "cache_expired" => Text.T("web.admin.downloads.presentation.reason.cacheExpired"),
        "budget" => Text.T("web.admin.downloads.presentation.reason.budget"),
        "disk_ceiling" => Text.T("web.admin.downloads.presentation.reason.diskCeiling"),
        "admin_delete" => Text.T("web.admin.downloads.presentation.reason.adminDelete"),
        "untracked" => Text.T("web.admin.downloads.presentation.reason.untracked"),
        "missing" => Text.T("web.admin.downloads.presentation.reason.missing"),
        "revoked" => Text.T("web.admin.downloads.presentation.reason.revoked"),
        "device_removed" => Text.T("web.admin.downloads.presentation.reason.deviceRemoved"),
        _ => reason,
    };

    /// <summary>The item column of a history row.</summary>
    public static string StorageEventItem(AdminDownloadStorageEvent item)
    {
        if (item.Reason is "revoked" or "device_removed")
        {
            var device = string.IsNullOrEmpty(item.LocationName) ? Text.T("web.admin.downloads.presentation.event.unknownDevice") : item.LocationName;
            var owner = item.Account?.Username;
            return string.IsNullOrEmpty(owner)
                ? Text.T("web.admin.downloads.presentation.event.downloads", With(Count(item.Count), ("device", device)))
                : Text.T("web.admin.downloads.presentation.event.downloadsWithOwner", With(Count(item.Count), ("device", device), ("owner", owner)));
        }
        var first = item.Titles.FirstOrDefault();
        if (item.Count == 1 && !string.IsNullOrEmpty(first)) return first;
        if (!string.IsNullOrEmpty(item.Detail) && item.Reason == "untracked") return item.Detail;
        return Text.T("web.admin.downloads.presentation.event.files", Count(item.Count));
    }

    public static string StorageEventLocation(AdminDownloadStorageEvent item) => item.Location switch
    {
        "device" => Text.T("web.admin.downloads.presentation.event.locationDevice"),
        "server" => Text.T("web.admin.downloads.presentation.event.locationServer"),
        _ => string.IsNullOrEmpty(item.LocationName) ? item.Location : item.LocationName,
    };

    // ---- Device copies ----------------------------------------------------------------------------

    public static string EntryTitle(AdminDownloadEntry entry) => entry.Episode is { } episode
        ? Text.T("web.admin.downloads.deviceCopies.episodeTitle", new
        {
            series = string.IsNullOrEmpty(entry.Title) ? Text.T("web.admin.downloads.deviceCopies.unknownSeries") : entry.Title,
            season = episode.SeasonNumber, episode = episode.EpisodeNumber,
        })
        : string.IsNullOrEmpty(entry.Title) ? Text.T("web.admin.downloads.deviceCopies.unknownTitle") : entry.Title;

    /// <summary>"Original", or a bitrate preset such as "10mbps" as "10 Mbps".</summary>
    public static string QualityLabel(string quality)
    {
        if (quality == "original") return Text.T("web.admin.downloads.deviceCopies.qualityOriginal");
        var match = System.Text.RegularExpressions.Regex.Match(quality, "^([0-9]+)mbps$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? Text.T("web.admin.downloads.deviceCopies.qualityBitrate", new { mbps = match.Groups[1].Value }) : quality;
    }

    /// <summary>Label and tone (<c>neutral</c>, <c>active</c>, <c>warning</c>, <c>danger</c>, <c>muted</c>) of a device entry's status.</summary>
    public static (string Label, string Tone) EntryStatus(AdminDownloadEntry entry) => entry.Status switch
    {
        "completed" => (Text.T("web.admin.downloads.deviceCopies.status.finished"), "neutral"),
        "queued" or "preparing" or "ready" => (Text.T("web.admin.downloads.deviceCopies.status.waiting"), "active"),
        "downloading" => (Text.T("web.admin.downloads.deviceCopies.status.downloading"), "active"),
        "revoked" => (Text.T("web.admin.downloads.deviceCopies.status.revoked"), "warning"),
        "failed" => (Text.T("web.admin.downloads.deviceCopies.status.failed"), "danger"),
        "cancelled" => (Text.T("web.admin.downloads.deviceCopies.status.cancelled"), "muted"),
        _ => (entry.Status, "muted"),
    };

    /// <summary>The finished/waiting/failed summary of a device row.</summary>
    public static string DeviceSummary(AdminDownloadDevice device)
    {
        var parts = new List<string>();
        if (device.Finished != 0) parts.Add(Text.T("web.admin.downloads.deviceCopies.finished", Count(device.Finished)));
        if (device.Waiting != 0) parts.Add(Text.T("web.admin.downloads.deviceCopies.waiting", Count(device.Waiting)));
        if (device.Failed != 0) parts.Add(Text.T("web.admin.downloads.deviceCopies.failed", Count(device.Failed)));
        return parts.Count == 0 ? Text.T("web.admin.downloads.deviceCopies.noneActive") : string.Join(", ", parts);
    }
}
