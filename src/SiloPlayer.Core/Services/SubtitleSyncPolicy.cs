using System.Globalization;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class SubtitleSyncPolicy
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan PollLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ApplyingLifetime = TimeSpan.FromSeconds(8);
    public static bool IsKey(string key) => Regex.IsMatch(key, "^(stored-[1-9][0-9]*|external-[0-9a-f]{64})$", RegexOptions.CultureInvariant);
    public static bool InProgress(SubtitleSyncJob? job) => job?.Status is "pending" or "running";
    public static bool CanApplyObservation(long sentRevision, long currentRevision) => sentRevision == currentRevision;
    public static bool ShouldAnnounceAppliedTiming(SubtitleSyncState state, string? watchedJobId, string? announcedJobId, SubtitleTiming? loadedTiming)
        => state.Sync is { Status: "synced" } job && job.Id == watchedJobId && job.Id == announcedJobId &&
            state.Timing == loadedTiming && job.Result == loadedTiming;
    public static bool ShouldPoll(SubtitleSyncJob? job, DateTimeOffset started, DateTimeOffset? pushed, DateTimeOffset now)
        => InProgress(job) && now - started < PollLifetime && (!pushed.HasValue || now - pushed.Value >= TimeSpan.FromSeconds(4));
    public static bool AcceptJob(SubtitleSyncJob? current, SubtitleSyncJob? incoming)
    {
        if (current == null) return true;
        if (incoming == null) return false;
        if (current.Id != incoming.Id) return incoming.CreatedAt > current.CreatedAt ||
            (incoming.CreatedAt == current.CreatedAt && long.TryParse(incoming.Id, out var nextId) && long.TryParse(current.Id, out var oldId) && nextId > oldId);
        static int Stage(SubtitleSyncJob job) => job.Status switch { "pending" => 0, "running" => 1, _ => 2 };
        if (Stage(incoming) < Stage(current)) return false;
        if (Stage(incoming) == Stage(current) && InProgress(current))
        {
            static int Phase(string? value) => value switch { "matching" => 2, "analyzing" => 1, _ => 0 };
            if (Phase(incoming.Phase) < Phase(current.Phase)) return false;
            if (incoming.Phase == current.Phase && incoming.Progress < current.Progress) return false;
        }
        return true;
    }
    public static string? Key(SubtitleTrackInfo track)
    {
        if (track.Source is not ("downloaded" or "external")) return null;
        if (!string.IsNullOrWhiteSpace(track.SyncKey)) return IsKey(track.SyncKey) ? track.SyncKey : null;
        if (track.Source != "downloaded") return null;
        var match = Regex.Match(track.Url ?? "", @"[?&]downloaded_subtitle_id=([1-9][0-9]*)(?:&|$)");
        return match.Success ? "stored-" + match.Groups[1].Value : null;
    }
    public static string Failure(string? failure) => failure switch
    {
        "subtitle_changed" => "The subtitle changed while it was syncing. Try again.",
        "no_audio" => "This video has no audio Silo can read.",
        "unavailable" => "The server is busy. Try again in a few minutes.",
        _ => "Sync failed. Try again."
    };
    public static string Phase(SubtitleSyncJob job) => job.Phase switch
    {
        "analyzing" => "Listening to the audio…", "matching" => "Matching lines to speech…", _ => "Waiting to start…"
    };
    public static string Describe(SubtitleTiming timing)
    {
        var parts = new List<string>();
        if (timing.OffsetMs != 0) parts.Add((timing.OffsetMs > 0 ? "+" : "−") + (Math.Abs((double)timing.OffsetMs) / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s");
        if (Math.Abs(timing.Scale - 1) >= 0.000001)
        {
            double[] frames = [23.976, 24, 25, 29.97, 30, 50, 59.94, 60];
            string? ratio = null;
            foreach (var from in frames) foreach (var to in frames)
                if (from != to && Math.Abs(from / to - timing.Scale) < 0.000001)
                    ratio ??= from.ToString(CultureInfo.InvariantCulture) + "→" + to.ToString(CultureInfo.InvariantCulture) + " fps";
            parts.Add(ratio ?? "×" + timing.Scale.ToString("0.####", CultureInfo.InvariantCulture));
        }
        return parts.Count == 0 ? "Original timing" : string.Join(" · ", parts);
    }
    public static string Status(SubtitleSyncState state) => state.Sync?.Status switch
    {
        "pending" or "running" => Phase(state.Sync),
        "already_synced" => "Already in sync", "no_match" => "Doesn't match this video",
        "failed" => "Sync failed", "synced" when !state.Timing.IsIdentity => "Synced " + Describe(state.Timing),
        _ => state.Timing.IsIdentity ? "Original timing" : "Timing adjusted " + Describe(state.Timing)
    };
}
