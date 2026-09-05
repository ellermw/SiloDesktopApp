using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Maps source audio ordinals to the tracks actually delivered to mpv.</summary>
public static class NativeAudioSelection
{
    public static void Apply(PlaybackTransportKind transport, int sourceIndex,
        IReadOnlyList<int> audioTrackIds, Action<int> select, Func<int> readSelected)
    {
        if (audioTrackIds.Count == 0)
            return; // Silent video is valid.

        // original_http carries the source inventory unchanged. Packaged
        // transports already contain the audio chosen by the server.
        var ordinal = transport == PlaybackTransportKind.DirectProgressive ? sourceIndex : 0;
        if (ordinal < 0 || ordinal >= audioTrackIds.Count)
            throw new InvalidOperationException("The selected audio track is missing from the loaded stream.");

        var id = audioTrackIds[ordinal];
        select(id);
        if (readSelected() != id)
            throw new InvalidOperationException("The player could not activate the selected audio track.");
    }
}
