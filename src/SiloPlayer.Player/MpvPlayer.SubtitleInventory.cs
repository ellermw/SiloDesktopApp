using System.Globalization;
using System.Runtime.InteropServices;
using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    /// <summary>Reads actual embedded subtitle IDs, excluding added sidecars.</summary>
    public IReadOnlyList<NativeSubtitleTrack> GetEmbeddedSubtitleTracks()
    {
        ThrowIfNotInitialized();
        using var lease = EnterNative();
        if (!lease.Active) return [];
        if (mpv_get_property_int(_mpvHandle, "track-list/count", MPV_FORMAT_INT64, out var count) < 0) return [];
        var tracks = new List<NativeSubtitleTrack>();
        for (var i = 0; i < count; i++)
        {
            var prefix = $"track-list/{i}/";
            if (ReadString(prefix + "type") != "sub" ||
                ReadString(prefix + "external") == "yes" || ReadInt(prefix + "id") is not > 0) continue;
            var id = ReadInt(prefix + "id")!.Value;
            tracks.Add(new(checked((int)id), ReadInt(prefix + "ff-index") is long stream ? checked((int)stream) : null,
                ReadInt(prefix + "src-id")?.ToString(CultureInfo.InvariantCulture),
                ReadString(prefix + "codec"), ReadString(prefix + "lang"), ReadString(prefix + "title"),
                ReadString(prefix + "forced") == "yes", ReadString(prefix + "hearing-impaired") == "yes"));
        }
        return tracks;

        long? ReadInt(string name) => mpv_get_property_int(_mpvHandle, name, MPV_FORMAT_INT64, out var value) >= 0 ? value : null;
        string? ReadString(string name)
        {
            var pointer = mpv_get_property_string(_mpvHandle, name);
            try { return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer); }
            finally { if (pointer != IntPtr.Zero) mpv_free(pointer); }
        }
    }

    /// <summary>Only libavformat guarantees the server's AVStream index namespace.</summary>
    public bool HasFfmpegTrackIndices()
    {
        using var lease = EnterNative();
        if (!lease.Active) return false;
        var pointer = mpv_get_property_string(_mpvHandle, "current-demuxer");
        try { return Marshal.PtrToStringUTF8(pointer) == "lavf"; }
        finally { if (pointer != IntPtr.Zero) mpv_free(pointer); }
    }
}
