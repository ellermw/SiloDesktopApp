using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Capability profile measured from the libmpv binary shipped with the app.
/// Decoder names match ffprobe/Silo codec names rather than marketing names.
/// </summary>
public static class MpvNativePlaybackCapabilities
{
    public const string VerifiedMpvVersion = "mpv v0.41.0-243-g05fac7f21";
    public const string VerifiedFfmpegVersion = "N-123079-ge869426a8";

    // mpv decoder-list was queried through the client API from the bundled
    // libmpv-2.dll. Keep this list explicit: advertising a codec that the
    // binary cannot decode would incorrectly suppress a server transcode.
    private static readonly string[] VideoCodecs =
    [
        "h264", "hevc", "av1", "vp9", "vp8", "vc1", "vvc",
        "mpeg1video", "mpeg2video", "mpeg4",
        "msmpeg4v1", "msmpeg4v2", "msmpeg4v3",
        "wmv1", "wmv2", "wmv3",
        "prores", "dnxhd", "dvvideo", "ffv1", "huffyuv", "utvideo",
        "mjpeg", "theora", "dirac", "flv1",
        "rv10", "rv20", "rv30", "rv40", "svq1", "svq3", "cinepak",
        "h263", "h261", "avs2", "avs3",
    ];

    private static readonly string[] AudioCodecs =
    [
        "aac", "aac_latm", "ac3", "eac3", "truehd", "mlp", "dts",
        "flac", "alac", "opus", "vorbis", "mp1", "mp2", "mp3",
        "ape", "wavpack", "tak", "tta", "shorten",
        "wmav1", "wmav2", "wmapro", "wmalossless", "wmavoice",
        "cook", "atrac3", "atrac3p", "amr_nb", "amr_wb",
        "ra_144", "ra_288", "sipr", "nellymoser", "qdm2", "qcelp",
        "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_f64le",
        "pcm_s16be", "pcm_s24be", "pcm_s32be", "pcm_bluray", "pcm_dvd",
        "dsd_lsbf", "dsd_msbf", "dsd_lsbf_planar", "dsd_msbf_planar",
    ];

    // The Silo scanner normalizes the primary formats to mp4/mkv/avi/ts/flv/
    // ogg/wmv. The additional aliases cover existing or imported catalog rows
    // while mapping to demuxers present in the bundled FFmpeg build.
    private static readonly string[] Containers =
    [
        "mp4", "mkv", "webm", "mov", "avi", "ts", "m2ts",
        "mpeg", "mpg", "flv", "ogg", "ogm", "wmv", "asf", "rm",
        "wav", "mp3", "flac", "aac", "ape", "wv", "tta",
    ];

    private static readonly HashSet<string> BitstreamCodecAllowList =
        new(StringComparer.OrdinalIgnoreCase) { "ac3", "eac3", "dts", "truehd" };

    /// <summary>
    /// Creates a fresh profile. Dolby Vision profiles 5 and 8 are single-layer
    /// sources handled by gpu-next/libplacebo reshaping. Profile 7 is omitted:
    /// its enhancement layer requires dual-layer decoding that this client has
    /// not verified and must not claim.
    /// </summary>
    public static NativePlaybackCapabilityProfile CreateProfile() => new(
        VerifiedMpvVersion,
        [.. VideoCodecs],
        [.. AudioCodecs],
        [.. Containers],
        MaxResolution: "2160p",
        Hdr: true,
        HdrDetails: new HdrCapabilityDetails
        {
            Hdr10 = true,
            Hdr10Plus = true,
            Hlg = true,
            DolbyVisionProfiles = [5, 8],
        });

    /// <summary>
    /// Applies the verified native profile to an existing playback request.
    /// Passthrough is deliberately absent unless the caller supplies codecs
    /// verified for both the active sink and the configured mpv output path.
    /// </summary>
    public static void ApplyTo(
        PlaybackStartRequest request,
        AudioPassthroughCapabilities? verifiedPassthrough = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = CreateProfile();
        request.CodecsVideo = [.. profile.VideoCodecs];
        request.CodecsAudio = [.. profile.AudioCodecs];
        request.Containers = [.. profile.Containers];
        request.MaxResolution = profile.MaxResolution;
        request.Hdr = profile.Hdr;
        request.HdrDetails = new HdrCapabilityDetails
        {
            Hdr10 = profile.HdrDetails.Hdr10,
            Hdr10Plus = profile.HdrDetails.Hdr10Plus,
            Hlg = profile.HdrDetails.Hlg,
            DolbyVisionProfiles = [.. profile.HdrDetails.DolbyVisionProfiles],
        };
        request.AudioPassthrough = SanitizeVerifiedPassthrough(verifiedPassthrough);
    }

    /// <summary>
    /// Restricts externally probed sink data to bitstream formats understood by
    /// both mpv's audio-spdif option and Silo's ffprobe codec naming.
    /// </summary>
    public static AudioPassthroughCapabilities? SanitizeVerifiedPassthrough(
        AudioPassthroughCapabilities? capabilities)
    {
        if (capabilities == null)
            return null;

        var codecs = capabilities.PassthroughCodecs
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .Select(codec => codec.Trim().ToLowerInvariant())
            .Where(BitstreamCodecAllowList.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (codecs.Count == 0)
            return null;

        return new AudioPassthroughCapabilities
        {
            PassthroughCodecs = codecs,
            SpatializerEnabled = capabilities.SpatializerEnabled,
            MaxChannels = Math.Clamp(capabilities.MaxChannels, 0, 32),
        };
    }
}
