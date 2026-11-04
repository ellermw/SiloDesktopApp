using System.Globalization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services.PlaybackMenus;

internal enum PlaybackQualityMenuOptionKind { Information, Version, Quality }

internal sealed record PlaybackQualityMenuOption(string Id, PlaybackQualityMenuOptionKind Kind,
    string Title, string Metadata, IReadOnlyList<string> Badges, bool IsCurrent = false,
    bool IsPending = false, bool IsEnabled = true, int? FileId = null, string? QualityId = null,
    string DisabledReason = "");

/// <summary>Presentation of accepted plans and viewer intent; never executes playback or settings work.</summary>
internal static class PlaybackQualityMenuModel
{
    internal static IReadOnlyList<PlaybackQualityMenuOption> Build(IReadOnlyList<FileVersion> versions,
        PlaybackStartResponse session, string selectedQualityMenuId, int? requestedFileId,
        bool isRoom, bool switching, string bufferingLimit)
    {
        var rows = new List<PlaybackQualityMenuOption>();
        if (isRoom)
            rows.Add(new("room-version", PlaybackQualityMenuOptionKind.Information, "Shared version",
                "The room controls the source version. Streaming quality is your own choice.", [], IsEnabled: false));
        else if (versions.Count > 1)
            foreach (var version in versions)
            {
                var playing = version.FileId == session.MediaFileId;
                var requested = version.FileId == requestedFileId && !playing;
                var badges = new List<string>();
                if (playing) badges.Add("Playing");
                if (requested) badges.Add("Requested");
                rows.Add(new("file:" + version.FileId.ToString(CultureInfo.InvariantCulture),
                    PlaybackQualityMenuOptionKind.Version,
                    $"{version.Resolution} {version.CodecVideo?.ToUpperInvariant()}{(version.Hdr ? " HDR" : "")}".Trim(),
                    "Source version", badges, playing, requested && switching, !switching, FileId: version.FileId));
            }

        // For protocol 3 the server ladder is authoritative, including an empty ladder.
        var qualities = session.ProtocolVersion >= 3
            ? PlaybackQualityPreferences.MenuOptions(session.AvailableQualities)
            : LegacyOptions(versions.FirstOrDefault(v => v.FileId == session.MediaFileId)?.Resolution);
        foreach (var quality in qualities)
        {
            var auto = quality.Label == "auto";
            // The current backend normalizes unknown preference IDs to Auto.
            // Keep a future advertised option visible without issuing a different intent.
            var supported = string.Equals(PlaybackQualityPreferences.Normalize(quality.Label), quality.Label, StringComparison.OrdinalIgnoreCase)
                || (session.ProtocolVersion < 3 && quality.Label is "original" or "420p");
            var title = quality.DisplayName ?? quality.Label;
            if (auto && bufferingLimit.Length > 0) title += " · limited after buffering";
            var details = new List<string>();
            if (quality.BitrateKbps is > 0)
                details.Add(quality.BitrateKbps >= 1000
                    ? $"{quality.BitrateKbps.Value / 1000d:0.#} Mbps" : $"{quality.BitrateKbps} kbps");
            if (auto && !string.IsNullOrWhiteSpace(session.ActiveQuality)) details.Add($"Playing: {session.ActiveQuality}");
            if (auto && bufferingLimit.Length > 0) details.Add(bufferingLimit);
            rows.Add(new("quality:" + quality.Label, PlaybackQualityMenuOptionKind.Quality, title,
                string.Join(" · ", details), [], quality.Label == selectedQualityMenuId,
                IsEnabled: !switching && supported, QualityId: quality.Label,
                DisabledReason: supported ? "" : "This quality option requires a newer player."));
        }
        return rows;
    }

    private static IReadOnlyList<PlaybackQualityV3> LegacyOptions(string? sourceResolution)
    {
        var sourceHeight = Height(sourceResolution);
        (string Id, string Title, int Height, int Bitrate)[] legacy =
        [("original", "Original", 0, 0), ("auto", "Auto", 0, 0),
         ("1080p-high", "1080p High", 1080, 10000), ("1080p", "1080p", 1080, 5000),
         ("720p-high", "720p High", 720, 4000), ("720p", "720p", 720, 2000),
         ("480p", "480p", 480, 1500), ("420p", "420p", 420, 720)];
        return legacy.Where(t => t.Height == 0 || sourceHeight == 0 || t.Height < sourceHeight)
            .Select(t => new PlaybackQualityV3 { Label = t.Id, DisplayName = t.Title,
                Height = t.Height > 0 ? t.Height : null, BitrateKbps = t.Bitrate > 0 ? t.Bitrate : null }).ToArray();
    }

    private static int Height(string? resolution) => resolution switch
    { "2160p" => 2160, "1440p" => 1440, "1080p" => 1080, "720p" => 720,
      "480p" => 480, "420p" => 420, "360p" => 360, _ => 0 };
}
