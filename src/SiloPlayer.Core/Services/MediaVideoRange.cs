using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Produces the same compact dynamic-range vocabulary as the current Silo
/// WebUI: DV, DV HDR10+, DV HDR10, DV HLG, HDR10+, HDR10, HLG, or HDR.
/// </summary>
public static class MediaVideoRange
{
    public static string Label(FileVersion? version)
    {
        if (version == null)
            return "";

        var hasDolbyVision = false;
        var hdrType = "";
        foreach (var track in version.VideoTracks ?? [])
        {
            var rangeType = track.VideoRangeType?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(track.DolbyVision) ||
                track.DvProfile is > 0 ||
                rangeType.StartsWith("DOVI", StringComparison.OrdinalIgnoreCase))
                hasDolbyVision = true;

            if (hdrType.Length == 0)
                hdrType = TrackHdrType(track, rangeType);
        }

        if (hasDolbyVision)
            return hdrType.Length > 0 ? $"DV {hdrType}" : "DV";
        if (hdrType.Length > 0)
            return hdrType;
        return version.Hdr ? "HDR" : "";
    }

    private static string TrackHdrType(VersionVideoTrack track, string rangeType)
    {
        if (track.Hdr10Plus == true ||
            rangeType.Contains("HDR10Plus", StringComparison.OrdinalIgnoreCase))
            return "HDR10+";
        if (rangeType.Equals("HDR10", StringComparison.OrdinalIgnoreCase) ||
            rangeType.EndsWith("WithHDR10", StringComparison.OrdinalIgnoreCase))
            return "HDR10";
        if (rangeType.Equals("HLG", StringComparison.OrdinalIgnoreCase) ||
            rangeType.EndsWith("WithHLG", StringComparison.OrdinalIgnoreCase))
            return "HLG";

        var transfer = track.ColorTransfer ?? "";
        if (transfer.Contains("smpte2084", StringComparison.OrdinalIgnoreCase))
            return "HDR10";
        if (transfer.Contains("arib-std-b67", StringComparison.OrdinalIgnoreCase))
            return "HLG";
        return "";
    }
}
