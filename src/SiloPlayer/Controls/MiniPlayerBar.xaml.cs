using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public sealed partial class MiniPlayerBar : UserControl
{
    public static double AudiobookHeightForWidth(double width) => width < 640 ? 119 : 121;
    private readonly PlayerService _playerService;
    private readonly SettingsService _settingsService;
    private bool _active;
    private bool _suppressSeek;
    private bool _suppressVolume;
    private bool _isMuted;
    private double _playbackRate = 1;
    private DispatcherTimer? _uiTimer;
    private CancellationTokenSource? _coverCts;
    private int _coverGeneration;

    public MiniPlayerBar()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        InitializeComponent();
        AudiobookTransportAppearance.Apply(AudiobookPlayPauseButton, AudiobookPlayPauseIcon, 24);
        AudiobookSettingsButton.Content = WebUiIcon.Create("settings-2", 16);
        AudiobookSliderAppearance.Apply(AudiobookSeekSlider);
        AudiobookSliderAppearance.Apply(AudiobookVolumeSlider, volume: true);
        SizeChanged += (_, _) => UpdateResponsiveAudio();
    }

    private void UpdateResponsiveAudio()
    {
        var width = ActualWidth;
        if (width <= 0) return;
        if (AudiobookBar.Visibility == Visibility.Visible) Height = AudiobookHeightForWidth(width);
        var chapters = width >= 640 && _playerService.AudiobookChapters.Count > 0;
        AudiobookPreviousChapterButton.Visibility = AudiobookNextChapterButton.Visibility = chapters ? Visibility.Visible : Visibility.Collapsed;
        AudiobookMuteButton.Visibility = AudiobookVolumeSlider.Visibility = width >= 768 ? Visibility.Visible : Visibility.Collapsed;
        AudiobookLayout.Padding = new Thickness(width < 640 ? 12 : 24, 8, width < 640 ? 12 : 24, 8);
        AudiobookControlsRow.ColumnSpacing = width < 640 ? 12 : 20;
        AudiobookTitleText.MaxWidth = double.PositiveInfinity;
        AudiobookTitleText.FontSize = width < 640 ? 14 : 15;
        AudiobookCoverButton.Width = 36; AudiobookCoverButton.Height = 54;
        AudiobookTransportRow.Spacing = width < 640 ? 8 : 12;
        AudiobookSkipBackButton.Width = AudiobookSkipBackButton.Height = AudiobookSkipForwardButton.Width = AudiobookSkipForwardButton.Height = width < 640 ? 40 : 44;
        AudiobookPreviousChapterButton.Width = AudiobookPreviousChapterButton.Height = AudiobookNextChapterButton.Width = AudiobookNextChapterButton.Height = width < 640 ? 40 : 44;
        AudiobookPlayPauseButton.Width = AudiobookPlayPauseButton.Height = width < 640 ? 48 : 56;
        AudiobookPlayPauseButton.CornerRadius = new CornerRadius(width < 640 ? 24 : 28);
        AudiobookSleepText.Visibility = Visibility.Visible;
        foreach (var button in new[] { AudiobookSleepButton, AudiobookChaptersButton, AudiobookSettingsButton, AudiobookCloseButton })
        {
            button.Padding = new Thickness(width < 640 ? 4 : 8);
            button.MinWidth = width < 640 ? 22 : 0;
        }
        AudiobookSpeedButton.Padding = width < 640 ? new Thickness(4, 6, 4, 6) : new Thickness(8, 6, 8, 6);
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.PauseChanged += OnPauseChanged;
        _playerService.SeekPreferencesChanged += OnSeekPreferencesChanged;
        _playerService.AudiobookPresentationChanged += OnAudiobookPresentationChanged;
        _playerService.AudiobookPlaybackRateChanged += OnAudiobookPlaybackRateChanged;

        _suppressVolume = true;
        VolumeSlider.Value = AudiobookVolumeSlider.Value = _playerService.Volume;
        _suppressVolume = false;
        _isMuted = _playerService.IsMuted;

        if (_playerService.IsAudiobook)
        {
            _playbackRate = _playerService.AudiobookPlaybackRate;
            AudiobookSpeedButton.Content = $"{_playbackRate:0.##}×";
            ActivateAudiobookMode();
        }
        else
            ActivateVideoMode();

        UpdateResponsiveAudio();
        UpdatePlayPauseIcon();
        UpdateVolumeIcon();
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.PauseChanged -= OnPauseChanged;
        _playerService.SeekPreferencesChanged -= OnSeekPreferencesChanged;
        _playerService.AudiobookPresentationChanged -= OnAudiobookPresentationChanged;
        _playerService.AudiobookPlaybackRateChanged -= OnAudiobookPlaybackRateChanged;
        if (_uiTimer != null) _uiTimer.Tick -= UiTimer_Tick;
        _uiTimer?.Stop();
        _uiTimer = null;
        try { _coverCts?.Cancel(); } catch { }
        _coverCts = null;
        ++_coverGeneration;
    }

    public void ResetSleepTimer()
    {
        _playerService.ClearAudiobookSleepTimer();
    }

    private void ActivateVideoMode()
    {
        Height = 132;
        VideoBar.Visibility = Visibility.Visible;
        AudiobookBar.Visibility = Visibility.Collapsed;
        TitleText.Text = _playerService.Title;
        SubtitleText.Text = _playerService.Subtitle ?? "";
    }

    private void OnSeekPreferencesChanged() { if (_active && _playerService.IsAudiobook) ActivateAudiobookMode(); }

    private void ActivateAudiobookMode()
    {
        Height = AudiobookHeightForWidth(ActualWidth);
        VideoBar.Visibility = Visibility.Collapsed;
        AudiobookBar.Visibility = Visibility.Visible;
        var settings = _settingsService.Load();
        SkipBackText.Text = Math.Clamp(_playerService.SeekIntervals.AudiobookBack, 5, 120).ToString();
        SkipForwardText.Text = Math.Clamp(_playerService.SeekIntervals.AudiobookForward, 5, 120).ToString();
        UpdateAudiobookSkipButtonLabels(settings);
        BuildAudiobookFlyouts();
        UpdateAudiobookPresentation();
        UpdateResponsiveAudio();
    }

    private void UiTimer_Tick(object? sender, object e)
    {
        if (!_active) return;
        var pos = _playerService.Position;
        var dur = _playerService.Duration;
        _suppressSeek = true;
        if (_playerService.IsAudiobook)
        {
            AudiobookSeekSlider.Maximum = Math.Max(1, dur);
            AudiobookSeekSlider.Value = Math.Clamp(pos, 0, Math.Max(1, dur));
            AudiobookTimeText.Text = $"{FormatTime(pos)}  /  {FormatTime(dur)}";
            var chapter = _playerService.CurrentAudiobookChapter;
            AudiobookChapterText.Text = chapter?.Title ?? "";
            UpdateAudiobookChapterButtons(chapter);
            UpdateAudiobookSleepLabel();
        }
        else
        {
            SeekSlider.Maximum = Math.Max(1, dur);
            SeekSlider.Value = Math.Clamp(pos, 0, Math.Max(1, dur));
        }
        _suppressSeek = false;
    }

    private void OnPositionChanged(double _) { }

    private void OnPauseChanged(bool paused) => DispatcherQueue?.TryEnqueue(UpdatePlayPauseIcon);

    private void OnAudiobookPresentationChanged() =>
        DispatcherQueue?.TryEnqueue(UpdateAudiobookPresentation);

    private void OnAudiobookPlaybackRateChanged(double rate) => DispatcherQueue?.TryEnqueue(() =>
    {
        _playbackRate = rate;
        AudiobookSpeedButton.Content = $"{rate:0.##}×";
        BuildSpeedFlyout();
    });

    private void UpdatePlayPauseIcon()
    {
        var glyph = _playerService.IsPaused ? "\uE768" : "\uE769";
        PlayPauseIcon.Glyph = glyph;
        AudiobookPlayPauseIcon.Glyph = glyph;
        var label = _playerService.IsPaused ? "Play" : "Pause";
        AutomationProperties.SetName(VideoPlayPauseButton, label);
        AutomationProperties.SetName(AudiobookPlayPauseButton, label);
        ToolTipService.SetToolTip(VideoPlayPauseButton, label);
        ToolTipService.SetToolTip(AudiobookPlayPauseButton, $"{label} (space)");
    }

    private void UpdateAudiobookPresentation()
    {
        if (!_active) return;
        if (!_playerService.IsAudiobook)
        {
            if (VideoBar.Visibility != Visibility.Visible)
                ActivateVideoMode();
            return;
        }
        if (AudiobookBar.Visibility != Visibility.Visible)
        {
            _playbackRate = _playerService.AudiobookPlaybackRate;
            AudiobookSpeedButton.Content = $"{_playbackRate:0.##}×";
            ActivateAudiobookMode();
            return;
        }

        AudiobookTitleText.Text = _playerService.Title;
        var chapter = _playerService.CurrentAudiobookChapter;
        AudiobookChapterText.Text = chapter?.Title ?? _playerService.AudiobookAuthor ?? "";
        UpdateAudiobookChapterButtons(chapter);
        _ = LoadAudiobookCoverAsync();
    }

    private async Task LoadAudiobookCoverAsync()
    {
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _coverCts, owner);
        try { previous?.Cancel(); } catch { }
        var generation = ++_coverGeneration;
        var ct = owner.Token;
        AudiobookCoverImage.Source = null;
        AudiobookCoverPlaceholder.Visibility = Visibility.Visible;
        var url = _playerService.AudiobookPosterUrl;
        var contentId = _playerService.ContentId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(contentId)) return;
        try
        {
            var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(
                contentId, "poster", url, App.Services.GetRequiredService<HttpClient>(), ct);
            if (ct.IsCancellationRequested
                || generation != _coverGeneration
                || !_active
                || !string.Equals(contentId, _playerService.ContentId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(path)) return;
            AudiobookCoverImage.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 160 };
            AudiobookCoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            Interlocked.CompareExchange(ref _coverCts, null, owner);
            owner.Dispose();
        }
    }

    private void BuildAudiobookFlyouts()
    {
        BuildSpeedFlyout();
        BuildChapterFlyout();
        BuildSleepFlyout();
        BuildSettingsFlyout();
    }

    private void BuildSpeedFlyout()
    {
        var flyout = new MenuFlyout();
        foreach (var rate in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 })
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = $"{rate:0.##}×",
                IsChecked = Math.Abs(rate - _playbackRate) < 0.001,
            };
            item.Click += (_, _) =>
            {
                _playbackRate = _playerService.SetAudiobookPlaybackRate(rate);
                AudiobookSpeedButton.Content = $"{rate:0.##}×";
                BuildSpeedFlyout();
            };
            flyout.Items.Add(item);
        }
        AudiobookSpeedButton.Flyout = flyout;
    }

    private void BuildChapterFlyout()
    {
        var flyout = new MenuFlyout();
        foreach (var chapter in _playerService.AudiobookChapters)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{chapter.Index + 1}. {chapter.Title}   {FormatTime(chapter.StartSeconds)}",
            };
            item.Click += (_, _) => _playerService.RequestUserSeek(chapter.StartSeconds);
            flyout.Items.Add(item);
        }
        AudiobookChaptersButton.IsEnabled = flyout.Items.Count > 0;
        AudiobookChaptersButton.Visibility = flyout.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AudiobookChaptersButton.Flyout = flyout;
    }

    private void BuildSleepFlyout()
    {
        var flyout = new MenuFlyout();
        var off = new MenuFlyoutItem { Text = "Off" };
        off.Click += (_, _) => _playerService.ClearAudiobookSleepTimer();
        flyout.Items.Add(off);
        foreach (var minutes in new[] { 5, 15, 30, 45, 60 })
        {
            var item = new MenuFlyoutItem { Text = $"{minutes} minutes" };
            item.Click += (_, _) => _playerService.SetAudiobookSleepTimer(TimeSpan.FromMinutes(minutes), null);
            flyout.Items.Add(item);
        }
        var endChapter = new MenuFlyoutItem { Text = "End of chapter" };
        endChapter.Click += (_, _) => _playerService.SetAudiobookSleepTimer(null, _playerService.CurrentAudiobookChapter?.EndSeconds);
        flyout.Items.Add(endChapter);
        AudiobookSleepButton.Flyout = flyout;
    }

    private void UpdateAudiobookSleepLabel()
    {
        var remaining = _playerService.GetAudiobookSleepRemaining();
        AudiobookSleepText.Text = remaining.HasValue
            ? $"Sleep {(int)remaining.Value.TotalMinutes}:{remaining.Value.Seconds:00}"
            : "Sleep";
    }

    private void BuildSettingsFlyout()
    {
        AudiobookSettingsButton.Flyout = AudiobookSettingsFlyout.Create(_playerService, _settingsService);
    }

    private MenuFlyoutSubItem BuildSkipSubmenu(string label, bool backward, int selected)
    {
        var submenu = new MenuFlyoutSubItem { Text = label };
        foreach (var seconds in new[] { 5, 10, 15, 30, 45, 60, 90 })
        {
            var item = new ToggleMenuFlyoutItem { Text = $"{seconds} seconds", IsChecked = seconds == selected };
            item.Click += async (_, _) =>
            {
                var settings = _settingsService.Load();
                await _playerService.SaveSeekIntervalAsync(true, backward, seconds);
                SkipBackText.Text = _playerService.SeekIntervals.AudiobookBack.ToString();
                SkipForwardText.Text = _playerService.SeekIntervals.AudiobookForward.ToString();
                UpdateAudiobookSkipButtonLabels(settings);
                BuildSettingsFlyout();
            };
            submenu.Items.Add(item);
        }
        return submenu;
    }

    private void VideoThumbnail_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => _playerService.Expand();
    private void Expand_Click(object sender, RoutedEventArgs e) => _playerService.Expand();

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.IsAudiobook) _playerService.ToggleAudiobookPlayback();
        else _playerService.RequestUserPauseToggle();
    }

    private void PreviousChapter_Click(object sender, RoutedEventArgs e) => _playerService.SeekToPreviousAudiobookChapter();
    private void NextChapter_Click(object sender, RoutedEventArgs e) => _playerService.SeekToNextAudiobookChapter();

    private void AudiobookSkipBack_Click(object sender, RoutedEventArgs e)
    {
        var seconds = Math.Clamp(_playerService.SeekIntervals.AudiobookBack, 5, 120);
        _playerService.RequestUserSeek(Math.Max(0, _playerService.Position - seconds));
    }

    private void AudiobookSkipForward_Click(object sender, RoutedEventArgs e)
    {
        var seconds = Math.Clamp(_playerService.SeekIntervals.AudiobookForward, 5, 120);
        _playerService.RequestUserSeek(Math.Min(_playerService.Duration, _playerService.Position + seconds));
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_suppressSeek && _playerService.Mpv != null) _playerService.RequestUserSeek(e.NewValue);
    }

    private void AudiobookSeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_suppressSeek && _playerService.Mpv != null) _playerService.RequestUserSeek(e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.Mpv == null) return;
        _isMuted = !_playerService.Mpv.GetMute();
        _playerService.Mpv.SetMute(_isMuted);
        _playerService.IsMuted = _isMuted;
        _playerService.SaveVolumeState();
        UpdateVolumeIcon();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => SetVolume(e.NewValue);
    private void AudiobookVolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => SetVolume(e.NewValue);

    private void SetVolume(double value)
    {
        if (_suppressVolume || _playerService?.Mpv == null) return;
        _suppressVolume = true;
        VolumeSlider.Value = AudiobookVolumeSlider.Value = value;
        _suppressVolume = false;
        _playerService.Mpv.SetVolume(value);
        _playerService.Volume = value;
        if (_isMuted && value > 0)
        {
            _isMuted = false;
            _playerService.Mpv.SetMute(false);
            _playerService.IsMuted = false;
        }
        _playerService.SaveVolumeState();
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        var value = _playerService.IsAudiobook ? AudiobookVolumeSlider.Value : VolumeSlider.Value;
        var glyph = _isMuted || value <= 0 ? "\uE74F" : value < 50 ? "\uE993" : "\uE767";
        VolumeIcon.Glyph = glyph;
        AudiobookVolumeIcon.Glyph = glyph;
        var label = _isMuted || value <= 0 ? "Unmute" : "Mute";
        AutomationProperties.SetName(VideoMuteButton, label);
        AutomationProperties.SetName(AudiobookMuteButton, label);
        ToolTipService.SetToolTip(VideoMuteButton, label);
        ToolTipService.SetToolTip(AudiobookMuteButton, label);
    }

    private void UpdateAudiobookChapterButtons(AudiobookChapterInfo? chapter)
    {
        var hasChapters = _playerService.AudiobookChapters.Count > 0;
        AudiobookPreviousChapterButton.Visibility = hasChapters && ActualWidth >= 640 ? Visibility.Visible : Visibility.Collapsed;
        AudiobookNextChapterButton.Visibility = hasChapters && ActualWidth >= 640 ? Visibility.Visible : Visibility.Collapsed;
        AudiobookChaptersButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        AudiobookNextChapterButton.IsEnabled = chapter != null && chapter.Index + 1 < _playerService.AudiobookChapters.Count;
    }

    private void UpdateAudiobookSkipButtonLabels(AppSettings settings)
    {
        var back = Math.Clamp(_playerService.SeekIntervals.AudiobookBack, 5, 120);
        var forward = Math.Clamp(_playerService.SeekIntervals.AudiobookForward, 5, 120);
        AutomationProperties.SetName(AudiobookSkipBackButton, $"Back {back} seconds");
        AutomationProperties.SetName(AudiobookSkipForwardButton, $"Forward {forward} seconds");
        ToolTipService.SetToolTip(AudiobookSkipBackButton, $"Back {back} seconds (Left)");
        ToolTipService.SetToolTip(AudiobookSkipForwardButton, $"Forward {forward} seconds (Right)");
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        ResetSleepTimer();
        _ = _playerService.CloseAsync();
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }
}
