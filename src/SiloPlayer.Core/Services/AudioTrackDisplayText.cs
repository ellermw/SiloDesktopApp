namespace SiloPlayer.Core.Services;

public static class AudioTrackDisplayText
{
    /// <summary>Source audio-track bitrates arrive from Silo in kilobits per second.</summary>
    public static string FormatBitrate(int? kbps) => kbps is > 0 ? $"{kbps.Value:0.#} kbps" : "";
}
