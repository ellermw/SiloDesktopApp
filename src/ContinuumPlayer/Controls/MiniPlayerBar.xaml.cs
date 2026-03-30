// src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.Controls;

[ComImport]
[Guid("905a0fef-bc53-11df-8c49-001e4fc686da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMiniBufferByteAccess
{
    void Buffer(out IntPtr buffer);
}

public sealed partial class MiniPlayerBar : UserControl
{
    private readonly PlayerService _playerService;
    private WriteableBitmap? _miniBitmap;
    private bool _active;
    private bool _suppressSeek;
    private bool _isMuted;

    // Snap buffer for mini frame (render thread -> UI thread)
    private byte[]? _snapBuffer;
    private int _snapW, _snapH, _snapStride;
    private volatile bool _snapReady;
    private volatile bool _uiBusy;

    private DispatcherTimer? _uiTimer;

    public MiniPlayerBar()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        this.InitializeComponent();

        // Hover effect on video thumbnail (deferred until Loaded because Parent is null during init)
        this.Loaded += (_, _) =>
        {
            if (MiniVideoFrame.Parent is Grid thumbGrid)
            {
                thumbGrid.PointerEntered += (_, _) => ExpandOverlay.Opacity = 1;
                thumbGrid.PointerExited += (_, _) => ExpandOverlay.Opacity = 0;
            }
        };
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;

        // Tell mpv to render at mini resolution
        _playerService.Mpv?.UpdateRenderSize(160, 90);

        // Subscribe to frames
        _playerService.FrameReady += OnFrameReady;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.PauseChanged += OnPauseChanged;

        // Update display
        TitleText.Text = _playerService.Title;
        SubtitleText.Text = _playerService.Subtitle ?? "";
        UpdatePlayPauseIcon();

        // Start UI timer for seek bar
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;

        _playerService.FrameReady -= OnFrameReady;
        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.PauseChanged -= OnPauseChanged;

        _uiTimer?.Stop();
        _uiTimer = null;
    }

    // -- Frame rendering ------------------------------------------------------

    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        if (!_active || _uiBusy) return;

        int size = stride * height;
        if (_snapBuffer == null || _snapBuffer.Length < size)
            _snapBuffer = new byte[size];
        Buffer.BlockCopy(buffer, 0, _snapBuffer, 0, size);
        _snapW = width;
        _snapH = height;
        _snapStride = stride;
        _snapReady = true;

        DispatcherQueue?.TryEnqueue(PresentFrame);
    }

    private void PresentFrame()
    {
        if (!_active || !_snapReady || _snapBuffer == null) return;
        _uiBusy = true;
        _snapReady = false;

        try
        {
            int w = _snapW, h = _snapH, srcStride = _snapStride;
            int dstStride = w * 4;

            if (_miniBitmap == null || _miniBitmap.PixelWidth != w || _miniBitmap.PixelHeight != h)
            {
                _miniBitmap = new WriteableBitmap(w, h);
                MiniVideoFrame.Source = _miniBitmap;
            }

            var pixelBuffer = _miniBitmap.PixelBuffer;
            if (srcStride == dstStride)
            {
                int copyLen = Math.Min(dstStride * h, (int)pixelBuffer.Length);
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                    .CopyTo(_snapBuffer, 0, pixelBuffer, 0, copyLen);
            }
            else
            {
                int rowBytes = Math.Min(srcStride, dstStride);
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                        .CopyTo(_snapBuffer, y * srcStride, pixelBuffer, (uint)(y * dstStride), rowBytes);
                }
            }
            _miniBitmap.Invalidate();
        }
        finally
        {
            _uiBusy = false;
        }
    }

    // -- UI updates -----------------------------------------------------------

    private void UiTimer_Tick(object? sender, object e)
    {
        if (!_active) return;

        var pos = _playerService.Position;
        var dur = _playerService.Duration;

        _suppressSeek = true;
        if (dur > 0) SeekSlider.Maximum = dur;
        SeekSlider.Value = pos;
        _suppressSeek = false;
    }

    private void OnPositionChanged(double pos)
    {
        // Handled by UI timer to avoid flooding
    }

    private void OnPauseChanged(bool paused)
    {
        DispatcherQueue?.TryEnqueue(UpdatePlayPauseIcon);
    }

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Glyph = _playerService.IsPaused ? "\uE768" : "\uE769";
    }

    // -- Controls -------------------------------------------------------------

    private void VideoThumbnail_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _playerService.Expand();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Mpv?.TogglePause();
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSeek || _playerService.Mpv == null) return;
        _playerService.Mpv.Seek(e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.Mpv == null) return;
        _isMuted = !_playerService.Mpv.GetMute();
        _playerService.Mpv.SetMute(_isMuted);
        UpdateVolumeIcon();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_playerService.Mpv == null) return;
        _playerService.Mpv.SetVolume(e.NewValue);
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _playerService.Mpv.SetMute(false);
        }
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        if (_isMuted || VolumeSlider.Value <= 0)
            VolumeIcon.Glyph = "\uE74F";
        else if (VolumeSlider.Value < 50)
            VolumeIcon.Glyph = "\uE993";
        else
            VolumeIcon.Glyph = "\uE767";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _ = _playerService.CloseAsync();
    }
}
