namespace SiloPlayer.Core.Models.Playback;

/// <summary>
/// Immutable description of the native media formats verified against the
/// bundled libmpv build. This is intentionally separate from a playback start
/// request so capability evidence can be tested and updated independently.
/// </summary>
public sealed record NativePlaybackCapabilityProfile(
    string VerifiedMpvVersion,
    IReadOnlyList<string> VideoCodecs,
    IReadOnlyList<string> AudioCodecs,
    IReadOnlyList<string> Containers,
    string MaxResolution,
    bool Hdr,
    HdrCapabilityDetails HdrDetails);
