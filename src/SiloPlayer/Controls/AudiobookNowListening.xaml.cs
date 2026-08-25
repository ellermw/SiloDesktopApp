using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public sealed partial class AudiobookNowListening : UserControl
{
    private readonly PlayerService _player;
    private readonly SettingsService _settings;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _coverCts;
    private bool _active;
    private bool _suppressSeek;
    private bool _suppressVolume;
    private double _rate = 1;
    private int _timeMode;

    public AudiobookNowListening()
    {
        _player = App.Services.GetRequiredService<PlayerService>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        InitializeComponent();
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;
        _player.PauseChanged += OnPauseChanged;
        _player.AudiobookPresentationChanged += OnPresentationChanged;
        _player.AudiobookPlaybackRateChanged += OnPlaybackRateChanged;
        _suppressVolume = true;
        VolumeSlider.Value = _player.Volume;
        _suppressVolume = false;
        _rate = _player.AudiobookPlaybackRate;
        SpeedButton.Content = $"{_rate:0.##}×";
        var config = _settings.Load();
        UpdateSkipButtonLabels(config);
        BuildFlyouts();
        UpdatePresentation();
        UpdatePlayback();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _player.PauseChanged -= OnPauseChanged;
        _player.AudiobookPresentationChanged -= OnPresentationChanged;
        _player.AudiobookPlaybackRateChanged -= OnPlaybackRateChanged;
        if (_timer != null) _timer.Tick -= Timer_Tick;
        _timer?.Stop();
        _timer = null;
        try { _coverCts?.Cancel(); } catch { }
        _coverCts?.Dispose();
        _coverCts = null;
    }

    private void Timer_Tick(object? sender, object e) => UpdatePlayback();
    private void OnPauseChanged(bool _) => DispatcherQueue.TryEnqueue(UpdatePlayback);
    private void OnPresentationChanged() => DispatcherQueue.TryEnqueue(UpdatePresentation);
    private void OnPlaybackRateChanged(double rate) => DispatcherQueue.TryEnqueue(() =>
    {
        _rate = rate;
        SpeedButton.Content = $"{rate:0.##}×";
        BuildFlyouts();
        UpdatePlayback();
    });

    private void UpdatePresentation()
    {
        TitleText.Text = _player.Title;
        AuthorText.Text = _player.AudiobookAuthor ?? "";
        AuthorText.Visibility = string.IsNullOrWhiteSpace(AuthorText.Text) ? Visibility.Collapsed : Visibility.Visible;
        NarratorText.Text = string.IsNullOrWhiteSpace(_player.AudiobookNarrator)
            ? ""
            : $"Narrated by  {_player.AudiobookNarrator}";
        NarratorText.Visibility = string.IsNullOrWhiteSpace(NarratorText.Text) ? Visibility.Collapsed : Visibility.Visible;
        _ = LoadCoverAsync();
    }

    private void UpdatePlayback()
    {
        var position = _player.Position;
        var duration = _player.Duration;
        _suppressSeek = true;
        SeekSlider.Maximum = Math.Max(1, duration);
        SeekSlider.Value = Math.Clamp(position, 0, Math.Max(1, duration));
        _suppressSeek = false;
        CurrentTimeText.Text = FormatTime(position);
        var remaining = Math.Max(0, duration - position);
        RightTimeButton.Content = _timeMode switch
        {
            1 => $"−{FormatTime(remaining)}",
            2 when Math.Abs(_rate - 1) > 0.001 => $"−{FormatTime(remaining / _rate)} at {_rate:0.##}×",
            _ => FormatTime(duration),
        };
        PlayPauseIcon.Glyph = _player.IsPaused ? "\uE768" : "\uE769";
        var playPauseLabel = _player.IsPaused ? "Play" : "Pause";
        AutomationProperties.SetName(PlayPauseButton, playPauseLabel);
        ToolTipService.SetToolTip(PlayPauseButton, $"{playPauseLabel} (space)");
        var chapter = _player.CurrentAudiobookChapter;
        var hasChapters = _player.AudiobookChapters.Count > 0;
        PreviousChapterButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        NextChapterButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChaptersButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        NextChapterButton.IsEnabled = chapter != null && chapter.Index + 1 < _player.AudiobookChapters.Count;
        ChapterPanel.Visibility = chapter == null ? Visibility.Collapsed : Visibility.Visible;
        if (chapter != null)
        {
            ChapterNumberText.Text = $"CHAPTER {chapter.Index + 1}";
            ChapterTitleText.Text = chapter.Title;
        }
        UpdateVolumeIcon();
        UpdateSleepLabel();
    }

    private async Task LoadCoverAsync()
    {
        try { _coverCts?.Cancel(); } catch { }
        _coverCts?.Dispose();
        _coverCts = new CancellationTokenSource();
        var ct = _coverCts.Token;
        CoverImage.Source = null;
        CoverPlaceholder.Visibility = Visibility.Visible;
        var url = _player.AudiobookPosterUrl;
        var contentId = _player.ContentId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(contentId)) return;
        try
        {
            var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(
                contentId, "poster", url, App.Services.GetRequiredService<HttpClient>(), ct);
            if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(path)) return;
            CoverImage.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 720 };
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void BuildFlyouts()
    {
        var speed = new MenuFlyout();
        foreach (var value in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 })
        {
            var item = new ToggleMenuFlyoutItem { Text = $"{value:0.##}×", IsChecked = Math.Abs(value - _rate) < 0.001 };
            item.Click += (_, _) =>
            {
                _rate = _player.SetAudiobookPlaybackRate(value);
                SpeedButton.Content = $"{value:0.##}×";
                BuildFlyouts();
            };
            speed.Items.Add(item);
        }
        SpeedButton.Flyout = speed;

        var chapters = new MenuFlyout();
        foreach (var chapter in _player.AudiobookChapters)
        {
            var item = new MenuFlyoutItem { Text = $"{chapter.Index + 1}. {chapter.Title}   {FormatTime(chapter.StartSeconds)}" };
            item.Click += (_, _) => _player.SeekTo(chapter.StartSeconds);
            chapters.Items.Add(item);
        }
        ChaptersButton.Flyout = chapters;
        ChaptersButton.IsEnabled = chapters.Items.Count > 0;

        var sleep = new MenuFlyout();
        var off = new MenuFlyoutItem { Text = "Off" };
        off.Click += (_, _) => _player.ClearAudiobookSleepTimer();
        sleep.Items.Add(off);
        foreach (var minutes in new[] { 5, 15, 30, 45, 60 })
        {
            var item = new MenuFlyoutItem { Text = $"{minutes} minutes" };
            item.Click += (_, _) => _player.SetAudiobookSleepTimer(TimeSpan.FromMinutes(minutes), null);
            sleep.Items.Add(item);
        }
        var end = new MenuFlyoutItem { Text = "End of chapter" };
        end.Click += (_, _) => _player.SetAudiobookSleepTimer(null, _player.CurrentAudiobookChapter?.EndSeconds);
        sleep.Items.Add(end);
        SleepButton.Flyout = sleep;

        var settings = new MenuFlyout();
        var smart = new ToggleMenuFlyoutItem { Text = "Smart rewind", IsChecked = _settings.Load().AudiobookSmartRewind };
        smart.Click += (_, _) =>
        {
            var config = _settings.Load();
            config.AudiobookSmartRewind = smart.IsChecked;
            _settings.Save(config);
        };
        settings.Items.Add(smart);
        var config = _settings.Load();
        settings.Items.Add(BuildSkipSubmenu("Skip back", true, config.AudiobookSkipBackSeconds));
        settings.Items.Add(BuildSkipSubmenu("Skip forward", false, config.AudiobookSkipForwardSeconds));
        SettingsButton.Flyout = settings;
    }

    private void UpdateSleepLabel()
    {
        var remaining = _player.GetAudiobookSleepRemaining();
        SleepTimerText.Text = remaining.HasValue
            ? $"Sleep {(int)remaining.Value.TotalMinutes}:{remaining.Value.Seconds:00}"
            : "Sleep";
    }

    private MenuFlyoutSubItem BuildSkipSubmenu(string label, bool backward, int selected)
    {
        var submenu = new MenuFlyoutSubItem { Text = label };
        foreach (var seconds in new[] { 5, 10, 15, 30, 45, 60, 90 })
        {
            var item = new ToggleMenuFlyoutItem { Text = $"{seconds} seconds", IsChecked = seconds == selected };
            item.Click += (_, _) =>
            {
                var config = _settings.Load();
                if (backward) config.AudiobookSkipBackSeconds = seconds;
                else config.AudiobookSkipForwardSeconds = seconds;
                _settings.Save(config);
                UpdateSkipButtonLabels(config);
                BuildFlyouts();
            };
            submenu.Items.Add(item);
        }
        return submenu;
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => _player.Minimize();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => _player.ToggleAudiobookPlayback();
    private void PreviousChapter_Click(object sender, RoutedEventArgs e) => _player.SeekToPreviousAudiobookChapter();
    private void NextChapter_Click(object sender, RoutedEventArgs e) => _player.SeekToNextAudiobookChapter();
    private void SkipBack_Click(object sender, RoutedEventArgs e) =>
        _player.SeekTo(Math.Max(0, _player.Position - Math.Clamp(_settings.Load().AudiobookSkipBackSeconds, 5, 120)));
    private void SkipForward_Click(object sender, RoutedEventArgs e) =>
        _player.SeekTo(Math.Min(_player.Duration, _player.Position + Math.Clamp(_settings.Load().AudiobookSkipForwardSeconds, 5, 120)));

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_suppressSeek) _player.SeekTo(e.NewValue);
    }

    private void RightTime_Click(object sender, RoutedEventArgs e)
    {
        _timeMode = _rate == 1 ? (_timeMode + 1) % 2 : (_timeMode + 1) % 3;
        UpdatePlayback();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_player.Mpv == null) return;
        _player.IsMuted = !_player.Mpv.GetMute();
        _player.Mpv.SetMute(_player.IsMuted);
        _player.SaveVolumeState();
        UpdateVolumeIcon();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolume || _player.Mpv == null) return;
        _player.Mpv.SetVolume(e.NewValue);
        _player.Volume = e.NewValue;
        if (_player.IsMuted && e.NewValue > 0)
        {
            _player.IsMuted = false;
            _player.Mpv.SetMute(false);
        }
        _player.SaveVolumeState();
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        VolumeIcon.Glyph = _player.IsMuted || VolumeSlider.Value <= 0
            ? "\uE74F"
            : VolumeSlider.Value < 50 ? "\uE993" : "\uE767";
        var label = _player.IsMuted || VolumeSlider.Value <= 0 ? "Unmute" : "Mute";
        AutomationProperties.SetName(MuteButton, label);
        ToolTipService.SetToolTip(MuteButton, label);
    }

    private void UpdateSkipButtonLabels(AppSettings settings)
    {
        var back = Math.Clamp(settings.AudiobookSkipBackSeconds, 5, 120);
        var forward = Math.Clamp(settings.AudiobookSkipForwardSeconds, 5, 120);
        SkipBackText.Text = back.ToString();
        SkipForwardText.Text = forward.ToString();
        AutomationProperties.SetName(SkipBackButton, $"Back {back} seconds");
        AutomationProperties.SetName(SkipForwardButton, $"Forward {forward} seconds");
        ToolTipService.SetToolTip(SkipBackButton, $"Back {back} seconds (Left)");
        ToolTipService.SetToolTip(SkipForwardButton, $"Forward {forward} seconds (Right)");
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }
}
