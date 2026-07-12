namespace SiloPlayer.Core.Models.Playback;

/// <summary>
/// The server's semantic playback decision. This describes why the selected
/// file is being delivered and must not be used as a proxy for the wire
/// transport: a remux can be delivered either progressively or over HLS.
/// </summary>
public enum PlaybackSemanticMethod
{
    Direct,
    Remux,
    Transcode,
}

/// <summary>The concrete transport the native player must open.</summary>
public enum PlaybackTransportKind
{
    DirectProgressive,
    RemuxProgressive,
    RemuxHls,
    TranscodeHls,
}

/// <summary>
/// A transport decision derived from a playback-start response.
/// </summary>
public sealed record PlaybackTransportPlan(
    PlaybackSemanticMethod SemanticMethod,
    PlaybackTransportKind TransportKind,
    bool RequiresTranscodeStartPreparation)
{
    public bool IsHls => TransportKind is PlaybackTransportKind.RemuxHls
        or PlaybackTransportKind.TranscodeHls;
}
