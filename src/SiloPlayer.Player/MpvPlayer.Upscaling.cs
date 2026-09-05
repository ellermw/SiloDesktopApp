using System.Globalization;
using System.Runtime.InteropServices;
using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    private const string UpscalingFilterLabel = "@silo-rtx";
    private readonly object _upscalingLock = new();
    private bool _upscalingEnabled;
    private bool _upscalingFileLoaded;
    private bool _upscalingFailed;
    private double _upscalingScale = 1;
    private double _pendingUpscalingScale = 1;
    private long _upscalingChangedAt;
    private long _upscalingNextCheck;
    private string _upscalingStatus = "Off";

    /// <summary>Requested processing is not evidence of driver-level AI activation.</summary>
    public string UpscalingStatus => _upscalingStatus;
    public event Action<string>? UpscalingStatusChanged;

    private void ConfigureUpscaling(bool enabled)
    {
        var adapter = enabled ? RtxVideoAdapter.Name : null;
        _upscalingEnabled = adapter != null;
        if (!enabled) return;
        if (adapter == null)
        {
            SetUpscalingStatus("Unavailable: no NVIDIA RTX adapter detected");
            return;
        }

        // A driver may expose an integrated and a discrete adapter. Use the
        // same explicit DXGI adapter for both mpv's decoder and D3D11 renderer.
        SetOption("d3d11-adapter", adapter);
        SetUpscalingStatus($"Ready on {adapter}; NVIDIA Video Super Resolution must be enabled in NVIDIA App");
    }

    private void SetUpscalingStatus(string status)
    {
        if (_upscalingStatus == status) return;
        _upscalingStatus = status;
        InvokeSafely(UpscalingStatusChanged, status, nameof(UpscalingStatusChanged));
    }

    private string? ReadUpscalingProperty(string name)
    {
        var ptr = mpv_get_property_string(_mpvHandle, name);
        try { return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr); }
        finally { if (ptr != IntPtr.Zero) mpv_free(ptr); }
    }

    private void ResetUpscalingForLoad()
    {
        lock (_upscalingLock)
        {
            _upscalingFileLoaded = false;
            RemoveUpscalingFilter();
            _upscalingFailed = false;
            _pendingUpscalingScale = 1;
            _upscalingNextCheck = 0;
            if (_upscalingEnabled) SetUpscalingStatus("Waiting for eligible SDR video");
        }
    }

    private void RemoveUpscalingFilter()
    {
        if (_upscalingScale <= 1) return;
        Command("vf", "remove", UpscalingFilterLabel);
        _upscalingScale = 1;
    }

    private void DisableUpscalingForFile(string reason)
    {
        _upscalingFailed = true;
        RemoveUpscalingFilter();
        SetUpscalingStatus($"Normal scaling: {reason}");
    }

    // Runs only on the event thread, at most twice a second. No GPU/filter work
    // is scheduled on WinUI's dispatcher. Resizes settle before rebuilding vf.
    private void UpdateUpscaling()
    {
        if (!_upscalingEnabled) return;
        lock (_upscalingLock)
        {
            var now = Environment.TickCount64;
            if (!_upscalingFileLoaded || _upscalingFailed || now < _upscalingNextCheck) return;
            _upscalingNextCheck = now + 500;

            // video-params describes the unfiltered input; using output width
            // here would create a double-upscale / remove-filter feedback loop.
            var width = (int)GetPropertyDouble("video-params/w");
            var height = (int)GetPropertyDouble("video-params/h");
            var scale = VideoUpscalingPolicy.GetScale(true, width, height,
                (int)GetPropertyDouble("osd-width"), (int)GetPropertyDouble("osd-height"),
                ReadUpscalingProperty("video-params/gamma"),
                GetPropertyDouble("video-params/par"), (int)GetPropertyDouble("video-params/rotate"));
            if (scale != _pendingUpscalingScale)
            {
                _pendingUpscalingScale = scale;
                _upscalingChangedAt = now;
                return;
            }
            if (scale == 1 && _upscalingScale == 1)
            {
                SetUpscalingStatus("Normal scaling: source or display size is not eligible for the SDR upscaling preview");
                return;
            }
            if (scale == _upscalingScale || now - _upscalingChangedAt < 750) return;

            if (scale <= 1)
            {
                RemoveUpscalingFilter();
                SetUpscalingStatus("Normal scaling: source or display size is not eligible for the SDR upscaling preview");
                return;
            }

            // A labeled add replaces just our own filter. Preserve all other
            // filters, subtitles, tone mapping and playback state.
            var filter = $"{UpscalingFilterLabel}:d3d11vpp=scale={scale.ToString("0.###", CultureInfo.InvariantCulture)}:scaling-mode=nvidia";
            if (!Command("vf", "add", filter))
            {
                DisableUpscalingForFile("the video filter is unavailable");
                return;
            }
            _upscalingScale = scale;
            SetUpscalingStatus($"RTX processing requested: {width}x{height} at {scale:0.###}x; confirm activation in NVIDIA App");
        }
    }

    private void HandleUpscalingLog(MpvEvent ev)
    {
        if (!_upscalingEnabled || ev.Data == IntPtr.Zero) return;
        var message = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
        var prefix = Marshal.PtrToStringUTF8(message.Prefix) ?? "";
        lock (_upscalingLock)
        {
            // Input-view failures are warning-level and drop frames without
            // necessarily ending playback. Treat warnings from our requested
            // processor as a failure too, before they can freeze the picture.
            if (VideoUpscalingPolicy.IsProcessingFailure(prefix, message.LogLevel, _upscalingScale > 1))
                DisableUpscalingForFile("NVIDIA video processing failed; check NVIDIA App and driver support");
        }
    }
}
