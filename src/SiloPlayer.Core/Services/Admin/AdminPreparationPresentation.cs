using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;
using Text = SiloPlayer.Core.Services.Admin.AdminPreparedFilesText;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminPreparationWorker(string Key, string Label, string Name);
public sealed record AdminCancelPreparationsPrompt(string Title, string Description, string ConfirmLabel);

/// <summary>
/// Preparation-queue presentation of the official Downloads admin page
/// (<c>adminDownloadPreparationPresentation.ts</c>, Silo Server 66b836f82).
/// </summary>
public static class AdminPreparationPresentation
{
    private const string K = "web.admin.downloads.preparation";

    public static readonly IReadOnlyList<string> States = ["running", "queued", "retrying", "paused", "failed"];

    /// <summary>Jobs still to finish: running, queued and retrying. Paused and failed jobs wait on an administrator.</summary>
    public static int ActiveCount(AdminDownloadPreparationCounts? counts) => counts == null ? 0 : counts.Running + counts.Queued + counts.Retrying;

    public static int StateCount(AdminDownloadPreparationCounts counts, string state) => state switch
    {
        "running" => counts.Running, "queued" => counts.Queued, "retrying" => counts.Retrying, "paused" => counts.Paused,
        "failed" => counts.FailedRecent, _ => 0,
    };

    public static string StateLabel(string state) => Text.T($"{K}.state.{state}");

    public static bool CanPause(AdminDownloadPreparation job) => job.State is "running" or "queued" or "retrying";
    public static bool CanResume(AdminDownloadPreparation job) => job.State == "paused";

    /// <summary>The running verb: a remux copies streams, so it is not "encoding".</summary>
    public static string RunningLabel(AdminDownloadPreparation job) => Text.T(job.Format == "remux" ? $"{K}.running.remuxing" : $"{K}.running.encoding");

    public static bool IsEpisode(AdminDownloadPreparation job) =>
        !string.IsNullOrEmpty(job.SeriesName) && job.SeasonNumber != null && job.EpisodeNumber != null;

    private static string EpisodeCode(AdminDownloadPreparation job) => Text.T($"{K}.episodeCode", new
    {
        season = (job.SeasonNumber ?? 0).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0'),
        episode = (job.EpisodeNumber ?? 0).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0'),
    });

    /// <summary>Row title: the episode's own name, or the movie title.</summary>
    public static string Title(AdminDownloadPreparation job)
    {
        if (IsEpisode(job)) return string.IsNullOrEmpty(job.EpisodeName) ? EpisodeCode(job) : job.EpisodeName;
        return string.IsNullOrEmpty(job.MediaTitle) ? Text.T($"{K}.fileFallback", new { id = job.MediaFileId }) : job.MediaTitle;
    }

    /// <summary>Row subtitle: "S02E01 · Severance" for episodes, "Movie" otherwise.</summary>
    public static string Subtitle(AdminDownloadPreparation job)
    {
        if (IsEpisode(job)) return Text.T($"{K}.subtitle.episode", new { code = EpisodeCode(job), series = job.SeriesName });
        return job.MediaType == "movie" ? Text.T($"{K}.subtitle.movie") : job.MediaType;
    }

    /// <summary>One-line title for tight spaces: "Severance S02E01" or the movie title.</summary>
    public static string CompactTitle(AdminDownloadPreparation job) =>
        IsEpisode(job) ? Text.T($"{K}.compactEpisodeTitle", new { series = job.SeriesName, code = EpisodeCode(job) }) : Title(job);

    /// <summary>Bytes as the media pages print them (1024-based, SI labels, one decimal).</summary>
    public static string FormatFileSize(long? bytes)
    {
        if (bytes is not > 0) return "";
        var value = bytes.Value;
        string Size(double scaled) => Presentation.FormatDecimal(scaled, 1);
        string Get(string key, string size) => AppStrings.Get(key, new Dictionary<string, object?> { ["size"] = size });
        if (value >= 1L << 30) return Get("web.common.format.size.gb", Size(value / (double)(1L << 30)));
        if (value >= 1L << 20) return Get("web.common.format.size.mb", Size(value / (double)(1L << 20)));
        if (value >= 1L << 10) return Get("web.common.format.size.kb", Size(value / 1024d));
        return Get("web.common.format.size.bytes", value.ToString(AppStrings.FormatCulture));
    }

    /// <summary>"7.1", "5.1", "Stereo", or "N ch"; empty without a channel count.</summary>
    public static string FormatChannels(int? channels) => channels is not > 0 ? ""
        : channels == 8 ? "7.1" : channels == 6 ? "5.1"
        : channels == 2 ? AppStrings.Get("web.common.format.channels.stereo")
        : AppStrings.Get("web.common.format.channels.count", Text.Count(channels.Value, channels.Value.ToString(AppStrings.FormatCulture)));

    private static string Codec(string? codec, string fallback = "") => AdminActivityPresentation.FormatCodec(codec) ?? fallback;

    public static string FormatSource(AdminDownloadPreparation job)
    {
        var video = string.Join(' ', new[] { job.Source.Resolution, Codec(job.Source.VideoCodec) }.Where(x => !string.IsNullOrEmpty(x)));
        return string.Join(" · ", new[]
        {
            job.Source.Container?.ToUpperInvariant(),
            string.Join(' ', new[] { video, job.Source.Hdr ? "HDR" : "" }.Where(x => !string.IsNullOrEmpty(x))),
            FormatFileSize(job.Source.FileSize),
        }.Where(x => !string.IsNullOrEmpty(x)));
    }

    public static string FormatBitrate(int? kbps)
    {
        if (kbps is not > 0) return "";
        var mbps = kbps.Value / 1000d;
        return Text.T($"{K}.bitrate", new { value = Presentation.FormatDecimal(mbps, mbps % 1 == 0 ? 0 : 1) });
    }

    private static string VideoTarget(AdminDownloadPreparation job) =>
        string.Join(' ', new[] { job.Output.Resolution, Codec(job.Output.VideoCodec) }.Where(x => !string.IsNullOrEmpty(x)));

    /// <summary>What the prepared file will be, e.g. "1080p H.264 · 10 Mbps · HDR → SDR".</summary>
    public static string FormatOutput(AdminDownloadPreparation job)
    {
        if (job.Format == "remux") return Text.T($"{K}.output.remux", new { container = job.Output.Container.ToUpperInvariant() });
        var video = job.Output.VideoCodec == "copy" ? Text.T($"{K}.output.videoCopied") : VideoTarget(job);
        return string.Join(" · ", new[]
        {
            video, FormatBitrate(job.Output.BitrateKbps),
            string.IsNullOrEmpty(job.Output.ToneMapMode) ? "" : Text.T($"{K}.output.hdrToSdr"),
        }.Where(x => !string.IsNullOrEmpty(x)));
    }

    public static string FormatVideoOutput(AdminDownloadPreparation job)
    {
        if (job.Output.VideoCodec == "copy") return Text.T($"{K}.videoOutput.copied");
        var bitrate = FormatBitrate(job.Output.BitrateKbps);
        return string.Join(" · ", new[] { VideoTarget(job), bitrate.Length > 0 ? Text.T($"{K}.videoOutput.upTo", new { bitrate }) : "" }
            .Where(x => !string.IsNullOrEmpty(x)));
    }

    public static string? FormatToneMap(AdminDownloadPreparation job) => job.Output.ToneMapMode switch
    {
        "hardware" => Text.T($"{K}.toneMap.hardware"),
        "software" => Text.T($"{K}.toneMap.software"),
        null or "" => null,
        _ => Text.T($"{K}.toneMap.unnamed"),
    };

    public static string FormatAudioOutput(AdminDownloadPreparation job)
    {
        var copied = job.Output.AudioCodec == "copy";
        if (job.Output.AllAudioTracks)
        {
            var count = job.Source.AudioTracks.Count;
            if (copied) return Text.T($"{K}.audioOutput.allTracksCopied", new { count });
            var codec = job.Output.AudioCodec == "aac" ? Text.T($"{K}.audioOutput.stereoAac") : Codec(job.Output.AudioCodec, "—");
            return Text.T($"{K}.audioOutput.allTracksConverted", new { count, codec });
        }
        return copied ? Text.T($"{K}.audioOutput.copied") : Codec(job.Output.AudioCodec, "—");
    }

    public static string FormatSourceAudio(AdminDownloadPreparation job)
    {
        var tracks = job.Source.AudioTracks;
        if (tracks.Count == 0) return "—";
        var labels = tracks.Select(t => string.Join(' ', new[] { Codec(t.Codec), FormatChannels(t.Channels) }.Where(x => !string.IsNullOrEmpty(x)))).ToArray();
        var shown = string.Join(", ", labels.Take(3));
        var more = labels.Length > 3 ? $" +{labels.Length - 3}" : "";
        return Text.T($"{K}.sourceAudio.tracks", new { count = tracks.Count, codecs = shown + more });
    }

    public static AdminPreparationWorker? Worker(AdminDownloadPreparation job)
    {
        if (job.Worker == null) return null;
        if (job.Worker.Kind == "node")
            return new("node:" + (job.Worker.NodeId ?? job.Worker.Name), Text.T($"{K}.worker.node"),
                !string.IsNullOrEmpty(job.Worker.Name) ? job.Worker.Name
                : job.Worker.NodeId != null ? Text.T($"{K}.worker.nodeWithId", new { id = job.Worker.NodeId }) : Text.T($"{K}.worker.node"));
        return new("server:" + job.Worker.Name, Text.T($"{K}.worker.server"),
            string.IsNullOrEmpty(job.Worker.Name) ? Text.T($"{K}.worker.localServer") : job.Worker.Name);
    }

    /// <summary>Percent complete in [0, 100], or null when progress or duration is unknown.</summary>
    public static double? Percent(AdminDownloadPreparation job)
    {
        var progress = job.Progress;
        if (progress == null || progress.DurationSeconds <= 0) return null;
        return Math.Max(0, Math.Min(100, progress.EncodedSeconds / progress.DurationSeconds * 100));
    }

    /// <summary>Seconds of wall time left at the current speed, or null when unknown.</summary>
    public static double? RemainingSeconds(AdminDownloadPreparation job)
    {
        var progress = job.Progress;
        if (progress == null || progress.DurationSeconds <= 0 || progress.Speed <= 0) return null;
        return Math.Max(0, progress.DurationSeconds - progress.EncodedSeconds) / progress.Speed;
    }

    public static string FormatRemaining(double? seconds)
    {
        if (seconds is null) return "";
        if (seconds < 60) return Text.T($"{K}.remaining.underOneMinute");
        var minutes = (long)Math.Round(seconds.Value / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return Text.T($"{K}.remaining.minutes", new { minutes });
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0
            ? Text.T($"{K}.remaining.hours", new { hours, hours_text = hours })
            : Text.T($"{K}.remaining.hoursMinutes", new { hours, hours_text = hours, minutes = rest });
    }

    public static string FormatSpeed(double? speed) =>
        speed is not > 0 ? "" : (speed >= 10 ? Math.Round(speed.Value, MidpointRounding.AwayFromZero).ToString("0", AppStrings.FormatCulture) : Presentation.FormatDecimal(speed.Value, 1)) + "×";

    /// <summary>"m:ss" or "h:mm:ss".</summary>
    public static string FormatClock(double seconds)
    {
        var safe = (long)Math.Max(0, Math.Floor(double.IsFinite(seconds) ? seconds : 0));
        var h = safe / 3600;
        var m = safe % 3600 / 60;
        var s = safe % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    public static double? SecondsSince(DateTimeOffset? at, DateTimeOffset? now = null) =>
        at is null ? null : Math.Max(0, ((now ?? DateTimeOffset.UtcNow) - at.Value).TotalSeconds);

    /// <summary>"in 45 s", "in 3 min", or "now" for a retry time; empty when there is none.</summary>
    public static string FormatRetryIn(DateTimeOffset? at, DateTimeOffset? now = null)
    {
        if (at is null) return "";
        var seconds = (long)Math.Round((at.Value - (now ?? DateTimeOffset.UtcNow)).TotalSeconds, MidpointRounding.AwayFromZero);
        if (seconds <= 0) return Text.T($"{K}.retryIn.now");
        if (seconds < 60) return Text.T($"{K}.retryIn.seconds", new { seconds });
        var minutes = (long)Math.Round(seconds / 60d, MidpointRounding.AwayFromZero);
        return Text.T($"{K}.retryIn.minutes", new { minutes, minutes_text = minutes });
    }

    /// <summary>"12 min ago" style age for a past instant.</summary>
    public static string FormatAgo(DateTimeOffset? at, DateTimeOffset? now = null)
    {
        var seconds = SecondsSince(at, now);
        if (seconds is null) return "";
        if (seconds < 60) return Text.T($"{K}.ago.justNow");
        var minutes = (long)Math.Round(seconds.Value / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return Text.T($"{K}.ago.minutes", new { minutes });
        var hours = (long)Math.Round(minutes / 60d, MidpointRounding.AwayFromZero);
        return Text.T($"{K}.ago.hours", new { hours, hours_text = hours });
    }

    public static string RequesterName(AdminDownloadPreparationRequester requester) =>
        string.IsNullOrEmpty(requester.Username) ? Text.T($"{K}.requester.userFallback", new { id = requester.UserId }) : requester.Username;

    public static string RequesterDevice(AdminDownloadPreparationRequester requester)
    {
        if (string.IsNullOrEmpty(requester.DeviceId)) return Text.T($"{K}.requester.webDownload");
        if (!string.IsNullOrEmpty(requester.DeviceName)) return requester.DeviceName;
        return string.IsNullOrEmpty(requester.DevicePlatform) ? Text.T($"{K}.requester.unnamedDevice") : requester.DevicePlatform;
    }

    /// <summary>The requester summary of a compact row.</summary>
    public static string RequesterSummary(AdminDownloadPreparation job)
    {
        if (job.Requesters.Count == 0) return Text.T($"{K}.requesters.none");
        var first = job.Requesters[0];
        return string.Join(" · ", new[] { RequesterName(first), RequesterDevice(first), job.Requesters.Count > 1 ? $"+{job.Requesters.Count - 1}" : "" }
            .Where(x => !string.IsNullOrEmpty(x)));
    }

    /// <summary>Text a filter box matches: title, series, requesters and worker (shown label and raw kind).</summary>
    public static string SearchText(AdminDownloadPreparation job)
    {
        var worker = Worker(job);
        return string.Join(' ', new[]
            {
                job.MediaTitle, job.SeriesName, job.EpisodeName,
                worker == null ? "" : $"{job.Worker?.Kind} {worker.Label} {worker.Name}",
            }.Concat(job.Requesters.SelectMany(r => new[] { r.Username, r.ProfileName, r.DeviceName }))
            .Where(x => !string.IsNullOrEmpty(x))).ToLower(CultureInfo.CurrentCulture);
    }

    /// <summary>Plural values for a job count: the number selects the form, the text is how it prints.</summary>
    public static IReadOnlyDictionary<string, object?> JobCount(long count) => Presentation.LocaleCount(count);

    /// <summary>One sentence per outcome of an action, e.g. "Paused 3 jobs. Skipped 1 job that failed."</summary>
    public static string SummarizeAction(string action, IReadOnlyList<AdminPreparationActionResult> results)
    {
        int Count(string outcome) => results.Count(r => r.Outcome == outcome);
        var applied = Count("applied");
        var unchanged = Count("unchanged");
        var notApplicable = Count("not_applicable");
        var notFound = Count("not_found");
        var parts = new List<string>();
        if (applied > 0) parts.Add(Text.T(action switch { "pause" => $"{K}.summary.paused", "resume" => $"{K}.summary.resumed", _ => $"{K}.summary.canceled" }, JobCount(applied)));
        if (unchanged > 0) parts.Add(Text.T(action == "pause" ? $"{K}.summary.alreadyPaused" : $"{K}.summary.notPaused", JobCount(unchanged)));
        if (notApplicable > 0) parts.Add(Text.T($"{K}.summary.skippedFailed", JobCount(notApplicable)));
        if (notFound > 0) parts.Add(Text.T($"{K}.summary.alreadyFinished", JobCount(notFound)));
        return parts.Count == 0 ? Text.T($"{K}.summary.nothingChanged") : string.Join(' ', parts);
    }

    /// <summary>Confirmation copy for canceling jobs, naming what the requesters lose.</summary>
    public static AdminCancelPreparationsPrompt CancelPrompt(IReadOnlyList<AdminDownloadPreparation> targets)
    {
        var waiting = targets.Sum(job => job.Requesters.Count(r => r.Status == "preparing"));
        var encoding = targets.Any(job => job.State == "running");
        var description = string.Join(' ', new[]
        {
            encoding ? Text.T($"{K}.cancelPrompt.encodingLost") : "",
            waiting > 0
                ? Text.T($"{K}.cancelPrompt.waitingFail", Text.Count(waiting, waiting.ToString("N0", AppStrings.FormatCulture)))
                : Text.T($"{K}.cancelPrompt.noneWaiting", new { count = targets.Count }),
        }.Where(x => !string.IsNullOrEmpty(x)));
        if (targets.Count > 1)
            return new(Text.T($"{K}.cancelPrompt.titleMany", JobCount(targets.Count)), description,
                Text.T($"{K}.cancelPrompt.confirmMany", JobCount(targets.Count)));
        var first = targets.FirstOrDefault();
        var name = first == null ? "" : CompactTitle(first);
        if (first?.State == "failed")
            return new(Text.T($"{K}.cancelPrompt.removeFailedTitle", new { name }), description, Text.T($"{K}.cancelPrompt.removeConfirm"));
        return new(Text.T($"{K}.cancelPrompt.cancelOneTitle", new { name }), description, Text.T($"{K}.cancelPrompt.cancelOneConfirm"));
    }

    /// <summary>Job status line of the details panel.</summary>
    public static string JobStatus(AdminDownloadPreparation job)
    {
        switch (job.State)
        {
            case "running":
                return Text.T($"{K}.status.running", new { label = RunningLabel(job), attempt = Math.Max(job.Attempts, 1), max = job.MaxAttempts });
            case "queued":
                return job.QueuePosition is > 0 ? Text.T($"{K}.status.queuedPosition", new { position = job.QueuePosition }) : Text.T($"{K}.status.queued");
            case "retrying":
                return Text.T($"{K}.status.retrying", new { retry_in = FormatRetryIn(job.NextRetryAt), attempt = job.Attempts + 1, max = job.MaxAttempts });
            case "paused":
            {
                var ago = FormatAgo(job.PausedAt);
                return ago.Length > 0 ? Text.T($"{K}.status.pausedAgo", new { ago }) : Text.T($"{K}.status.paused");
            }
            default:
            {
                var ago = FormatAgo(job.FailedAt);
                return ago.Length > 0 ? Text.T($"{K}.status.failedAgo", new { ago }) : Text.T($"{K}.status.failed");
            }
        }
    }

    /// <summary>When the first listed failure leaves the server's 24 hour list, or null.</summary>
    public static DateTimeOffset? NextFailureExpiry(IEnumerable<AdminDownloadPreparation> jobs)
    {
        DateTimeOffset? next = null;
        var window = TimeSpan.FromHours(24);
        foreach (var job in jobs)
        {
            // A failure time within a day of the end of time has no expiry that can be computed (adding the day would throw),
            // and no real failure expires then, so it never arms a timer.
            if (job.State != "failed" || job.FailedAt is not { } failedAt || failedAt > DateTimeOffset.MaxValue - window) continue;
            var expiry = failedAt + window;
            if (next == null || expiry < next) next = expiry;
        }
        return next;
    }
}
