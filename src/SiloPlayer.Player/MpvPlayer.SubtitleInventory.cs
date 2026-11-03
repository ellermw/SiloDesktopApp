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

    /// <summary>Actual subtitle IDs, including sidecars, for verifying sub-add.</summary>
    public IReadOnlyList<int> GetSubtitleTrackIds(bool externalOnly = false)
    {
        ThrowIfNotInitialized();
        using var lease = EnterNative();
        if (!lease.Active) return [];
        if (mpv_get_property_int(_mpvHandle, "track-list/count", MPV_FORMAT_INT64, out var count) < 0) return [];
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (GetString($"track-list/{i}/type") != "sub" ||
                externalOnly && GetString($"track-list/{i}/external") != "yes") continue;
            if (mpv_get_property_int(_mpvHandle, $"track-list/{i}/id", MPV_FORMAT_INT64, out var id) >= 0 && id > 0)
                ids.Add(checked((int)id));
        }
        return ids;

        string? GetString(string name)
        {
            var pointer = mpv_get_property_string(_mpvHandle, name);
            try { return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer); }
            finally { if (pointer != IntPtr.Zero) mpv_free(pointer); }
        }
    }

    /// <summary>Only the builtin Matroska demuxer attests container TrackNumbers here.</summary>
    public bool HasContainerTrackIds() => CurrentDemuxer() == "mkv";

    /// <summary>Only libavformat guarantees the server's AVStream index namespace.</summary>
    public bool HasFfmpegTrackIndices() => CurrentDemuxer() == "lavf";

    private string? CurrentDemuxer()
    {
        using var lease = EnterNative();
        if (!lease.Active) return null;
        var pointer = mpv_get_property_string(_mpvHandle, "current-demuxer");
        try { return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer); }
        finally { if (pointer != IntPtr.Zero) mpv_free(pointer); }
    }
}
