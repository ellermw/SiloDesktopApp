using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminStorageWarning(string Id, string Tone, string Title, string Body, string? Location = null, string? Action = null);
public sealed record AdminDeletePreparedFilesPrompt(string Title, string Description, string InUseWarning, string ConfirmLabel);
public sealed record AdminRevokeDownloadsPrompt(string Title, string Description, string CacheEffect, string StaleEffect, string ConfirmLabel);

/// <summary>Plain presentation rules shared by unit tests and the native page.</summary>
public static class AdminPreparedFilesPresentation
{
    public static string FormatBytes(long bytes)
    {
        var value = Math.Max(bytes, 0);
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB" };
        double scaled = value;
        var index = 0;
        while (scaled >= 999.5 && index < units.Length - 1) { scaled /= 1000; index++; }
        var number = index == 0 ? Math.Round(scaled).ToString("0", CultureInfo.CurrentCulture)
            : scaled < 10 ? scaled.ToString("0.0", CultureInfo.CurrentCulture) : scaled.ToString("0", CultureInfo.CurrentCulture);
        return number + " " + units[index];
    }

    public static IReadOnlyList<AdminStorageWarning> StorageWarnings(AdminDownloadStorage storage)
    {
        var result = new List<AdminStorageWarning>();
        foreach (var location in storage.Locations)
        {
            var fill = DiskFillPercent(location);
            if (fill is { } percent && percent >= storage.DiskCeilingPercent - 5)
                result.Add(new("fill:" + location.Key, "warning", $"{location.Name} is at {Math.Round(percent)}% disk.",
                    location.Usage?.SharesScratch == true
                        ? $"Clean-up deletes cached files early at {storage.DiskCeilingPercent}%. Prepared files share this disk with transcode scratch, and at 95% the node stops taking playback sessions."
                        : $"Clean-up deletes cached files early at {storage.DiskCeilingPercent}%.", location.Key, "edit_location"));
            if (location.Usage?.Ephemeral == true)
                result.Add(new("ephemeral:" + location.Key, "warning", $"{location.Name} keeps prepared files on temporary storage.",
                    "This filesystem does not survive a restart or a container recreation, so every prepared file is prepared again afterwards. Point the directory at a mounted volume.", location.Key, "edit_location"));
            if (location.Kind == "node" && !location.Online && location.InUseBytes + location.CachedBytes > 0)
                result.Add(new("offline:" + location.Key, "info", $"{location.Name} is offline.",
                    $"Its {FormatBytes(location.InUseBytes + location.CachedBytes)} of prepared files can't be reached; {location.WaitingDownloads} downloads are waiting on them.", location.Key, "view_location"));
            if (location.UntrackedBytes > 0)
                result.Add(new("untracked:" + location.Key, "info", $"{FormatBytes(location.UntrackedBytes)} of untracked files on {location.Name}.",
                    "Silo has no record of these files, so clean-up never removes them.", location.Key, "review_untracked"));
            if (location.ReplicasDisagree)
                result.Add(new("replicas:" + location.Key, "warning", "API servers report different download directories.",
                    "Every API server should share one prepared-file volume; a file one server prepared may be missing on another.", location.Key, "edit_location"));
        }
        var stale = storage.Locations.Sum(x => x.StaleWaitingBytes);
        if (stale > 0)
            result.Add(new("stale", "info", $"{FormatBytes(stale)} is waiting on devices not seen in {storage.StaleDeviceDays}+ days.",
                "Revoking those downloads lets the files expire.", null, "review_devices"));
        return result;
    }

    public static double? DiskFillPercent(AdminDownloadStorageLocation location) => location.Usage is { FSTotalBytes: > 0 } usage
        ? Math.Clamp((double)usage.FSUsedBytes / usage.FSTotalBytes * 100, 0, 100) : null;

    public static (string Main, string Detail) FileRecipe(AdminPreparedFile file)
    {
        if (file.Format == "remux") return ("Remux to MP4", "original quality");
        var codec = file.VideoCodec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ? "HEVC" : file.VideoCodec.ToUpperInvariant();
        var main = string.Join(' ', new[] { file.Resolution, codec }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return (string.IsNullOrEmpty(main) ? "Transcode" : main, file.BitrateKbps is > 0 ? Math.Round(file.BitrateKbps.Value / 1000d) + " Mbps" : "");
    }

    public static string FileUsage(AdminPreparedFile file)
    {
        var parts = new List<string>();
        if (file.Downloading > 0) parts.Add($"{file.Downloading} downloading");
        if (file.Waiting > 0) parts.Add($"{file.Waiting} waiting");
        if (file.Finished > 0) parts.Add($"{file.Finished} finished");
        return parts.Count == 0 ? "No downloads" : string.Join(" · ", parts);
    }

    public static string HistoryReason(string reason) => reason switch
    {
        "cache_expired" => "Expired from cache", "budget" => "Over budget", "disk_ceiling" => "Disk ceiling reached",
        "admin_delete" => "Deleted by an administrator", "untracked" => "Deleted untracked files", "missing" => "Files found missing",
        "revoked" => "Revoked downloads", "device_removed" => "Removed from device", _ => reason,
    };

    public static AdminDeletePreparedFilesPrompt DeletePrompt(IReadOnlyList<AdminPreparedFile> files, bool includeInUse)
    {
        var cached = files.Count(x => x.State != "in_use");
        var inUse = files.Count - cached;
        var description = $"Delete {cached} cached {(cached == 1 ? "file" : "files")}" + (inUse > 0 ? $"; {inUse} in use." : ".");
        var warning = inUse > 0 ? "Files in use are prepared again and those downloads wait for the new copy." : "";
        var applied = cached + (includeInUse ? inUse : 0);
        return new($"Delete {files.Count} prepared {(files.Count == 1 ? "file" : "files")}?", description, warning,
            includeInUse ? $"Delete {applied} {(applied == 1 ? "file" : "files")}" : cached == 1 ? "Delete cached file" : $"Delete {cached} cached files");
    }

    public static AdminRevokeDownloadsPrompt RevokePrompt(string device, string profile, int count, long bytes, bool stale, int cacheHours)
    {
        var possessive = string.IsNullOrWhiteSpace(profile) ? device : profile + "'s " + device;
        return new($"Revoke {count} {(count == 1 ? "download" : "downloads")} on {device}?",
            $"The Silo app on {possessive} deletes {(count == 1 ? "it" : "them")} the next time it connects. Frees {FormatBytes(bytes)} on the device.",
            cacheHours > 0 ? $"Prepared files only these downloads needed become cached and expire after {cacheHours} hours. Preparations nothing else needs are canceled."
                : "Prepared files only these downloads needed become cached and expire after the cache period. Preparations nothing else needs are canceled.",
            stale ? "It was last seen a long time ago, so this may take a while." : "", count == 1 ? "Revoke download" : $"Revoke {count} downloads");
    }

    public static string PreparationTitle(AdminDownloadPreparation item) => item.SeasonNumber is { } season && item.EpisodeNumber is { } episode
        ? string.IsNullOrWhiteSpace(item.EpisodeName) ? $"S{season:00}E{episode:00}" : item.EpisodeName
        : string.IsNullOrWhiteSpace(item.MediaTitle) ? "File " + item.MediaFileId : item.MediaTitle;

    public static string PreparationSubtitle(AdminDownloadPreparation item) => item.SeasonNumber is { } season && item.EpisodeNumber is { } episode
        ? $"S{season:00}E{episode:00} · {item.SeriesName}" : item.MediaType == "movie" ? "Movie" : item.MediaType;

    public static string Relative(DateTimeOffset? value, DateTimeOffset? now = null)
    {
        if (value == null) return "Never";
        var age = (now ?? DateTimeOffset.UtcNow) - value.Value;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{Math.Round(age.TotalMinutes)} min ago";
        if (age.TotalDays < 2) return $"{Math.Round(age.TotalHours)} h ago";
        return $"{Math.Round(age.TotalDays)} d ago";
    }
}
