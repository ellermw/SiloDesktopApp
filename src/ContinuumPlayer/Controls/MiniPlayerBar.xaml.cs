// src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.Controls;

public sealed partial class MiniPlayerBar : UserControl
{
    private readonly PlayerService _playerService;
    private bool _active;
    private bool _suppressSeek;
    private bool _isMuted;

    private DispatcherTimer? _uiTimer;

    public MiniPlayerBar()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        this.InitializeComponent();

        // No hover effect needed — expand button is always visible
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;

        _playerService.PositionChanged += OnPositionChanged;
        _playerService.PauseChanged += OnPauseChanged;

        // Sync volume/mute state from PlayerService (shared with overlay)
        VolumeSlider.Value = _playerService.Volume;
        _isMuted = _playerService.IsMuted;

        // Update display
        TitleText.Text = _playerService.Title;
        SubtitleText.Text = _playerService.Subtitle ?? "";
        UpdatePlayPauseIcon();
        UpdateVolumeIcon();

        // Start UI timer for seek bar
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;

        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.PauseChanged -= OnPauseChanged;

        _uiTimer?.Stop();
        _uiTimer = null;
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
        _playerService.IsMuted = _isMuted;
        UpdateVolumeIcon();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_playerService?.Mpv == null) return;
        _playerService.Mpv.SetVolume(e.NewValue);
        _playerService.Volume = e.NewValue;
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _playerService.Mpv.SetMute(false);
            _playerService.IsMuted = false;
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
