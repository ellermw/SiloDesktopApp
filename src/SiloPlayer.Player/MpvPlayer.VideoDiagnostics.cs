using System.Runtime.InteropServices;

namespace SiloPlayer.Player;

/// <summary>Measured decode/render state, distinct from a catalog HDR label or a display certification.</summary>
public sealed record VideoPipelineSnapshot(
    string? EngineVersion, string? FfmpegVersion, string? LibplaceboVersion,
    string? Renderer, string? HardwareDecoder, int? DolbyVisionProfile,
    string? SourceTransfer, string? SourcePrimaries,
    string? TargetTransfer, string? TargetPrimaries, string? TargetPixelFormat,
    bool DolbyVisionReshaping, bool EnhancementLayerApplied);

public sealed partial class MpvPlayer
{
    /// <summary>
    /// Reads only fixed video metadata and renderer pass classifications. Never
    /// reads track URLs, titles, filenames or authentication/header properties.
    /// </summary>
    public VideoPipelineSnapshot? GetVideoPipelineSnapshot()
    {
        if (_disposed || _mpvHandle == IntPtr.Zero) return null;
        string? Read(string property) => ReadUpscalingProperty(property);
        int? profile = null;
        var trackCount = Math.Clamp((int)GetPropertyDouble("track-list/count"), 0, 128);
        for (var index = 0; index < trackCount; index++)
        {
            if (Read($"track-list/{index}/type") != "video" || Read($"track-list/{index}/selected") != "yes") continue;
            if (int.TryParse(Read($"track-list/{index}/dolby-vision-profile"), out var value) && value > 0)
                profile = value;
            break;
        }
        var (reshaping, enhancement) = ReadRenderPassFeatures();
        return new(Read("mpv-version"), Read("ffmpeg-version"), Read("libplacebo-version"),
            Read("current-vo"), Read("hwdec-current"), profile,
            Read("video-params/gamma"), Read("video-params/primaries"),
            Read("video-target-params/gamma"), Read("video-target-params/primaries"), Read("video-target-params/pixelformat"),
            reshaping, enhancement);
    }

    private (bool Reshaping, bool Enhancement) ReadRenderPassFeatures()
    {
        // vo-passes exposes a single native node; unlike track-list it does
        // not implement nested property paths such as vo-passes/fresh/count.
        if (ReadVideoNode(_mpvHandle, "vo-passes", 6, out var root) < 0) return (false, false);
        try
        {
            var reshaping = false;
            var enhancement = false;
            foreach (var kind in new[] { "fresh", "redraw" })
            {
                if (!TryVideoMapValue(root, kind, out var passes) || passes.Format != 7 || passes.Pointer == IntPtr.Zero) continue;
                var list = Marshal.PtrToStructure<VideoNodeList>(passes.Pointer);
                for (var index = 0; index < Math.Clamp(list.Count, 0, 128); index++)
                {
                    var pass = Marshal.PtrToStructure<VideoNode>(list.Values + index * Marshal.SizeOf<VideoNode>());
                    if (!TryVideoMapValue(pass, "desc", out var description) || description.Format != 1 || description.Pointer == IntPtr.Zero) continue;
                    var text = Marshal.PtrToStringUTF8(description.Pointer) ?? "";
                    reshaping |= text.Contains("reshaping", StringComparison.OrdinalIgnoreCase);
                    enhancement |= text.Contains("enhancement layer", StringComparison.OrdinalIgnoreCase);
                }
            }
            return (reshaping, enhancement);
        }
        finally { FreeVideoNode(ref root); }
    }

    private static bool TryVideoMapValue(VideoNode map, string key, out VideoNode value)
    {
        value = default;
        if (map.Format != 8 || map.Pointer == IntPtr.Zero) return false;
        var list = Marshal.PtrToStructure<VideoNodeList>(map.Pointer);
        for (var index = 0; index < Math.Clamp(list.Count, 0, 128); index++)
            if (Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(list.Keys, index * IntPtr.Size)) == key)
            {
                value = Marshal.PtrToStructure<VideoNode>(list.Values + index * Marshal.SizeOf<VideoNode>());
                return true;
            }
        return false;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct VideoNode
    {
        [FieldOffset(0)] public IntPtr Pointer;
        [FieldOffset(8)] public int Format;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct VideoNodeList { public int Count; public IntPtr Values, Keys; }
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    private static extern int ReadVideoNode(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out VideoNode node);
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_free_node_contents")]
    private static extern void FreeVideoNode(ref VideoNode node);
}
