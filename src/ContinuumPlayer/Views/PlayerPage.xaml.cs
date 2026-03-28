using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ContinuumPlayer.Views;

public sealed partial class PlayerPage : Page
{
    private PlayerViewModel? _vm;
    private MediaPlayer? _mediaPlayer;
    private MediaPlaybackItem? _mediaPlaybackItem;
    private bool _isDisposed;
    private string? _contentId;
    private bool _isFullscreen;
    private bool _statsVisible;
    private double _resumePosition;
    private bool _suppressSeek; // prevents seek feedback loop from timer updates
    private bool _isMuted;

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _hideTimer;

    public PlayerPage()
    {
        this.InitializeComponent();
    }

    // -- Page lifecycle -------------------------------------------------------

    private bool _playFromStart;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string param && !string.IsNullOrEmpty(param))
        {
            if (param.EndsWith("|fromstart"))
            {
                _contentId = param.Replace("|fromstart", "");
                _playFromStart = true;
            }
            else
            {
                _contentId = param;
            }
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_contentId))
        {
            ShowError("No content specified for playback.");
            return;
        }

        // Focus the page so keyboard shortcuts work immediately
        this.Focus(FocusState.Programmatic);

        try
        {
            await InitializePlaybackAsync(_contentId);
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ShowError($"Player error: {ex.Message}");
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CleanupAsync();
    }

    private async void CleanupAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _uiTimer?.Stop();
        _hideTimer?.Stop();

        // Exit fullscreen if active
        if (_isFullscreen)
            SetFullscreen(false);

        // Show nav again
        if (App.MainWindowInstance is MainWindow mw)
            mw.ShowMainNavigation();

        // Dispose MediaPlayer
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Pause();
            VideoPlayer.SetMediaPlayer(null);
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        // Stop playback session on the server
        if (_vm?.Manager != null)
        {
            try { await _vm.Manager.StopSessionAsync(); }
            catch { }
        }
    }

    // -- Playback initialization ----------------------------------------------

    private async Task InitializePlaybackAsync(string contentId)
    {
        try
        {
            // 1. Hide nav for immersive experience
            if (App.MainWindowInstance is MainWindow mw)
                mw.HideMainNavigation();

            // Create view model via DI
            _vm = App.Services.GetRequiredService<PlayerViewModel>();
            _vm.IsLoading = true;

            // 2. Get watch detail
            var watchDetail = await _vm.Manager.GetWatchDetailAsync(contentId);

            // Build title
            if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
                _vm.Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
            else
                _vm.Title = watchDetail.Title;

            // Populate versions
            foreach (var v in watchDetail.Versions)
                _vm.Versions.Add(v);

            // 3. Select best version
            var bestVersion = _vm.Manager.SelectBestVersion(watchDetail.Versions);
            if (bestVersion == null)
            {
                ShowError("No playable version found.");
                return;
            }

            _vm.Resolution = bestVersion.Resolution;

            // Determine start position from user data (resume support)
            double startPosition = 0;
            if (!_playFromStart && watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            _resumePosition = startPosition;

            // 4. Start session on the server
            var session = await _vm.Manager.StartSessionAsync(bestVersion.FileId, startPosition);
            _vm.PlayMethod = session.PlayMethod;

            // If the server returned a position (from saved progress), use that
            if (session.Position > 0 && _resumePosition == 0)
                _resumePosition = session.Position;

            // 5. Get the stream URL (may be overridden by HLS transcode below)
            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL available.");
                return;
            }

            // 6. HLS transcode fallback: if play_method is "transcode", start transcode session
            if (session.PlayMethod == "transcode")
            {
                try
                {
                    var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
                    var transcodeResponse = await playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                    {
                        SessionId = session.SessionId,
                        SeekSeconds = startPosition,
                        TargetResolution = bestVersion.Resolution,
                        TargetCodecVideo = "h264",
                        TargetCodecAudio = "aac",
                        TargetBitrateKbps = 8000,
                        SegmentDuration = 2,
                        SubtitleTrackIndex = -1,
                        SubtitleBurnIn = false
                    });

                    // Build HLS manifest URL
                    var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
                    var baseUrl = apiClient.BaseUrl;
                    var manifestPath = transcodeResponse.ManifestUrl;
                    if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                        manifestPath = "/api/v1" + manifestPath;
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    // HLS endpoints don't need auth -- session UUID is the token

                    _resumePosition = transcodeResponse.PlayerStartSeconds;

                    LogToFile("player_transcode.txt",
                        $"TranscodeStatus: {transcodeResponse.Status}\nManifestURL: {streamUrl}\nStartSeconds: {transcodeResponse.PlayerStartSeconds}\nSwitchedFile: {transcodeResponse.SwitchedFileId}");
                }
                catch (Exception ex)
                {
                    LogToFile("player_transcode_error.txt", ex.ToString());
                    // Fall through to try direct stream URL as a last resort
                }
            }

            // Log init info for debugging
            LogToFile("player_init.txt",
                $"StreamURL: {streamUrl}\nPlayMethod: {_vm.PlayMethod}\nResolution: {_vm.Resolution}\nSession: {_vm.Manager.SessionId}\nResumePos: {_resumePosition}");

            // 7. Create MediaPlayer with proper audio category
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.AudioCategory = MediaPlayerAudioCategory.Movie;

            // 8. Create MediaSource from stream URL, load subtitles, wrap in MediaPlaybackItem
            var mediaSource = MediaSource.CreateFromUri(new Uri(streamUrl));
            LoadSubtitles(mediaSource);
            _mediaPlaybackItem = new MediaPlaybackItem(mediaSource);
            _mediaPlayer.Source = _mediaPlaybackItem;

            // 9. Attach to MediaPlayerElement
            VideoPlayer.SetMediaPlayer(_mediaPlayer);

            // 10. Subscribe to media events
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            // 11. Start playback immediately
            _mediaPlayer.Play();

            // 12. Set title and playback info, populate flyouts
            TitleText.Text = _vm.Title;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();
            PopulateSubtitleFlyout();
            PopulateAudioFlyout();

            // 13. Start UI update timer (250ms)
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            // 14. Start controls auto-hide timer (3s)
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideTimer.Tick += HideTimer_Tick;
            _hideTimer.Start();
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ShowError($"Failed to start playback: {ex.Message}");
        }
    }

    // -- Media event handlers -------------------------------------------------

    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed || _vm == null) return;

            _vm.IsLoading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Resume to saved position if needed
            if (_resumePosition > 0 && _mediaPlayer != null)
            {
                _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(_resumePosition);
                _resumePosition = 0; // Only seek once
            }
        });
    }

    private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            NavigateBack();
        });
    }

    private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            var message = args.ErrorMessage ?? "Unknown playback error";
            LogToFile("player_error.txt", $"MediaFailed: {message}\nExtendedCode: {args.ExtendedErrorCode}");
            ShowError($"Playback failed: {message}");
        });
    }

    // -- UI update timer (position, seek bar, play/pause icon, skip markers) --

    private void UiTimer_Tick(object? sender, object e)
    {
        if (_isDisposed || _mediaPlayer == null || _vm == null) return;

        var session = _mediaPlayer.PlaybackSession;
        var pos = session.Position.TotalSeconds;
        var dur = session.NaturalDuration.TotalSeconds;

        // Update seek slider without triggering seek
        _suppressSeek = true;
        if (dur > 0) SeekSlider.Maximum = dur;
        SeekSlider.Value = pos;
        _suppressSeek = false;

        // Update time labels
        PositionText.Text = PlayerViewModel.FormatTime(pos);
        DurationText.Text = PlayerViewModel.FormatTime(dur);

        // Update play/pause icon based on actual playback state
        bool isPlaying = session.PlaybackState == MediaPlaybackState.Playing;
        PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";

        // Report progress to server
        _vm.Manager.UpdatePosition(pos, !isPlaying);

        // Check skip markers
        UpdateSkipButtons(pos);

        // Update stats if visible
        if (_statsVisible) UpdateStats();
    }

    private void UpdateSkipButtons(double pos)
    {
        if (_vm == null) return;

        var intro = _vm.Manager.WatchDetail?.Intro;
        bool showIntro = intro != null && pos >= intro.Start && pos < intro.End;
        SkipIntroButton.Visibility = showIntro ? Visibility.Visible : Visibility.Collapsed;

        var credits = _vm.Manager.WatchDetail?.Credits;
        bool showCredits = credits != null && pos >= credits.Start && pos < credits.End;
        SkipCreditsButton.Visibility = showCredits ? Visibility.Visible : Visibility.Collapsed;
    }

    // -- Controls auto-hide ---------------------------------------------------

    private void HideTimer_Tick(object? sender, object e)
    {
        _hideTimer?.Stop();

        // Only hide if playing (keep visible when paused)
        if (_mediaPlayer != null &&
            _mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            ControlsOverlay.Opacity = 0;
            ControlsOverlay.IsHitTestVisible = false;
        }
    }

    private void ShowControls()
    {
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.IsHitTestVisible = true;

        // Restart the hide timer
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
    }

    private void Page_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ShowControls();
        // Ensure focus for keyboard shortcuts
        this.Focus(FocusState.Programmatic);
    }

    // -- Keyboard shortcuts ---------------------------------------------------

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isDisposed || _mediaPlayer == null) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                TogglePlayPause();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Left:
                SeekRelative(-10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Right:
                SeekRelative(10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up:
                AdjustVolume(5);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down:
                AdjustVolume(-5);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.F:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape:
                if (_isFullscreen)
                    SetFullscreen(false);
                else
                    ExitPlayer();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.M:
                ToggleMute();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.I:
                ToggleStats();
                e.Handled = true;
                break;
        }

        ShowControls();
    }

    // -- Playback controls ----------------------------------------------------

    private void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;

        if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            _mediaPlayer.Pause();
        else
            _mediaPlayer.Play();
    }

    private void SeekRelative(double seconds)
    {
        if (_mediaPlayer == null) return;

        var session = _mediaPlayer.PlaybackSession;
        var newPos = session.Position.TotalSeconds + seconds;
        newPos = Math.Max(0, Math.Min(newPos, session.NaturalDuration.TotalSeconds));
        session.Position = TimeSpan.FromSeconds(newPos);
    }

    private void AdjustVolume(double delta)
    {
        var newVolume = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
        VolumeSlider.Value = newVolume;
    }

    private void ToggleMute()
    {
        if (_mediaPlayer == null) return;

        _isMuted = !_isMuted;
        _mediaPlayer.IsMuted = _isMuted;
        UpdateVolumeIcon();
    }

    private void ToggleFullscreen()
    {
        SetFullscreen(!_isFullscreen);
    }

    private void SetFullscreen(bool fs)
    {
        var appWindow = App.MainWindowInstance?.AppWindow;
        if (appWindow == null) return;

        if (fs)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            if (App.MainWindowInstance is MainWindow mw)
                mw.HideMainNavigation();
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
            // Don't show nav -- player stays immersive until exited
        }

        _isFullscreen = fs;
        FullscreenIcon.Glyph = fs ? "\uE73F" : "\uE740";
    }

    private void ToggleStats()
    {
        _statsVisible = !_statsVisible;

        if (_statsVisible)
        {
            UpdateStats();
            StatsOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            StatsOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // -- Button click handlers ------------------------------------------------

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.Volume = e.NewValue / 100.0;
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _mediaPlayer.IsMuted = false;
        }
        UpdateVolumeIcon();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        ExitPlayer();
    }

    private void GoBack_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        ToggleStats();
    }

    private void SkipIntro_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null || _vm?.IntroEnd == null) return;
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(_vm.IntroEnd.Value);
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null || _vm?.CreditsEnd == null) return;
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(_vm.CreditsEnd.Value);
    }

    // -- Seek slider interaction (suppressed feedback loop pattern) ------------

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // If the change came from our programmatic update in the timer, ignore it
        if (_suppressSeek || _mediaPlayer == null) return;

        // User clicked or dragged the slider -- seek to the new position
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
        ShowControls();
    }

    // -- Quality / version switching ------------------------------------------

    private void PopulateQualityFlyout()
    {
        QualityFlyout.Items.Clear();
        if (_vm == null) return;

        foreach (var version in _vm.Versions)
        {
            var label = $"{version.Resolution}";
            if (version.Hdr) label += " HDR";
            label += $" ({version.CodecVideo.ToUpperInvariant()})";

            var item = new MenuFlyoutItem
            {
                Text = label,
                Tag = version
            };
            item.Click += QualityItem_Click;

            // Mark current version
            if (version.Resolution == _vm.Resolution)
                item.FontWeight = Microsoft.UI.Text.FontWeights.Bold;

            QualityFlyout.Items.Add(item);
        }
    }

    private async void QualityItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is FileVersion version)
        {
            await SwitchVersionAsync(version);
        }
    }

    private async Task SwitchVersionAsync(FileVersion version)
    {
        if (_mediaPlayer == null || _vm == null) return;

        var currentPos = _mediaPlayer.PlaybackSession.Position.TotalSeconds;

        _mediaPlayer.Pause();
        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            await _vm.Manager.StopSessionAsync();
            var session = await _vm.Manager.StartSessionAsync(version.FileId, currentPos);
            _vm.PlayMethod = session.PlayMethod;
            _vm.Resolution = version.Resolution;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();
            PopulateSubtitleFlyout();
            PopulateAudioFlyout();

            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl)) return;

            // Handle transcode fallback on version switch
            if (session.PlayMethod == "transcode")
            {
                try
                {
                    var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
                    var transcodeResponse = await playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                    {
                        SessionId = session.SessionId,
                        SeekSeconds = currentPos,
                        TargetResolution = version.Resolution,
                        TargetCodecVideo = "h264",
                        TargetCodecAudio = "aac",
                        TargetBitrateKbps = 8000,
                        SegmentDuration = 2,
                        SubtitleTrackIndex = -1,
                        SubtitleBurnIn = false
                    });

                    var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
                    var baseUrl = apiClient.BaseUrl;
                    var manifestPath = transcodeResponse.ManifestUrl;
                    if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                        manifestPath = "/api/v1" + manifestPath;
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    currentPos = transcodeResponse.PlayerStartSeconds;
                }
                catch (Exception ex)
                {
                    LogToFile("player_transcode_error.txt", ex.ToString());
                }
            }

            _resumePosition = currentPos;
            var mediaSource = MediaSource.CreateFromUri(new Uri(streamUrl));
            LoadSubtitles(mediaSource);
            _mediaPlaybackItem = new MediaPlaybackItem(mediaSource);
            _mediaPlayer.Source = _mediaPlaybackItem;
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            ShowError($"Failed to switch quality: {ex.Message}");
        }
    }

    // -- Subtitle selection ---------------------------------------------------

    private void LoadSubtitles(MediaSource mediaSource)
    {
        if (_vm?.Manager.CurrentSession == null) return;

        var subtitleUrls = _vm.Manager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                continue; // bitmap subs need burn-in via transcode

            try
            {
                var tts = TimedTextSource.CreateFromUri(new Uri(fullUrl));
                tts.Resolved += (s, e) =>
                {
                    if (e.Tracks.Count > 0)
                        e.Tracks[0].Label = track.Label;
                };
                mediaSource.ExternalTimedTextSources.Add(tts);
            }
            catch { }
        }
    }

    private void PopulateSubtitleFlyout()
    {
        SubtitleFlyout.Items.Clear();

        // "Off" option to disable subtitles
        var offItem = new MenuFlyoutItem { Text = "Off" };
        offItem.Click += (_, _) =>
        {
            if (_mediaPlaybackItem == null) return;
            try
            {
                for (uint i = 0; i < _mediaPlaybackItem.TimedMetadataTracks.Count; i++)
                    _mediaPlaybackItem.TimedMetadataTracks.SetPresentationMode(
                        i, TimedMetadataTrackPresentationMode.Disabled);
            }
            catch { }
        };
        SubtitleFlyout.Items.Add(offItem);
        SubtitleFlyout.Items.Add(new MenuFlyoutSeparator());

        // Subtitle tracks from the session
        var subtitleUrls = _vm?.Manager.GetSubtitleUrls() ?? [];
        int trackOffset = 0;
        for (int idx = 0; idx < subtitleUrls.Count; idx++)
        {
            var (track, _) = subtitleUrls[idx];
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
            {
                trackOffset++;
                continue; // skip bitmap subs (they were not loaded)
            }

            var label = !string.IsNullOrEmpty(track.Label) ? track.Label : track.Language;
            if (string.IsNullOrEmpty(label)) label = $"Track {idx + 1}";
            if (track.Forced) label += " [Forced]";

            var item = new MenuFlyoutItem { Text = label };
            int externalIndex = idx - trackOffset; // index into external timed text sources
            item.Click += (_, _) => SelectSubtitleTrack(externalIndex);
            SubtitleFlyout.Items.Add(item);
        }
    }

    private void SelectSubtitleTrack(int index)
    {
        try
        {
            if (_mediaPlaybackItem == null) return;

            // First disable all
            for (uint i = 0; i < _mediaPlaybackItem.TimedMetadataTracks.Count; i++)
                _mediaPlaybackItem.TimedMetadataTracks.SetPresentationMode(
                    i, TimedMetadataTrackPresentationMode.Disabled);

            // Then enable the selected one
            if (index >= 0 && (uint)index < _mediaPlaybackItem.TimedMetadataTracks.Count)
                _mediaPlaybackItem.TimedMetadataTracks.SetPresentationMode(
                    (uint)index, TimedMetadataTrackPresentationMode.ApplicationPresented);
        }
        catch { }
    }

    // -- Audio track switching ------------------------------------------------

    private void PopulateAudioFlyout()
    {
        AudioFlyout.Items.Clear();

        var currentSession = _vm?.Manager.CurrentSession;
        if (currentSession == null || _vm == null) return;

        // Get audio tracks from the version that matches the current session
        var version = _vm.Versions.FirstOrDefault(v => v.FileId == currentSession.MediaFileId);
        if (version?.AudioTracks == null) return;

        for (int i = 0; i < version.AudioTracks.Count; i++)
        {
            var at = version.AudioTracks[i];
            var label = at.Language ?? "Unknown";
            if (!string.IsNullOrEmpty(at.Title)) label += $" - {at.Title}";
            if (!string.IsNullOrEmpty(at.Codec)) label += $" ({at.Codec.ToUpperInvariant()})";
            if (at.Channels.HasValue) label += $" {at.Channels}ch";
            if (at.Default) label += " \u2605";

            // Bold the currently active audio track
            var item = new MenuFlyoutItem { Text = label };
            if (i == currentSession.AudioTrackIndex)
                item.FontWeight = Microsoft.UI.Text.FontWeights.Bold;

            int trackIndex = i;
            item.Click += async (_, _) => await SwitchAudioTrackAsync(trackIndex);
            AudioFlyout.Items.Add(item);
        }
    }

    private async Task SwitchAudioTrackAsync(int trackIndex)
    {
        if (_mediaPlayer == null || _vm == null) return;

        var currentPos = _mediaPlayer.PlaybackSession.Position.TotalSeconds;

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
            var response = await playbackApi.ChangeAudioTrackAsync(
                _vm.Manager.SessionId!, trackIndex, currentPos);

            // Build the new stream URL
            var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
            var baseUrl = apiClient.BaseUrl;
            var token = apiClient.AccessToken;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
                streamPath = "/api/v1" + streamPath;
            var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
            if (token != null)
                url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            // Update play method in case it changed
            _vm.PlayMethod = response.PlayMethod;
            UpdatePlaybackInfo();
            PopulateAudioFlyout();

            // Reload stream at the current position
            _resumePosition = currentPos;
            var mediaSource = MediaSource.CreateFromUri(new Uri(url));
            LoadSubtitles(mediaSource);
            _mediaPlaybackItem = new MediaPlaybackItem(mediaSource);
            _mediaPlayer.Source = _mediaPlaybackItem;
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            LogToFile("player_audio_switch_error.txt", ex.ToString());
            ShowError($"Failed to switch audio: {ex.Message}");
        }
    }

    // -- Stats overlay --------------------------------------------------------

    private void UpdateStats()
    {
        if (_vm == null) return;

        var currentSession = _vm.Manager.CurrentSession;
        var version = _vm.Versions.FirstOrDefault(v => v.FileId == currentSession?.MediaFileId)
                   ?? _vm.Versions.FirstOrDefault();

        StatsResolution.Text = $"Resolution:  {_vm.Resolution}";
        StatsCodec.Text = $"Video:       {currentSession?.PlaybackInfo?.VideoCodec ?? version?.CodecVideo ?? "?"}\nAudio:       {currentSession?.PlaybackInfo?.AudioCodec ?? version?.CodecAudio ?? "?"}";
        StatsPlayMethod.Text = $"Play Method: {_vm.PlayMethod}";
        StatsBitrate.Text = $"Bitrate:     {(version?.Bitrate > 0 ? $"{version.Bitrate / 1000.0:F1} Mbps" : "?")}";

        var hdr = "SDR";
        if (version is { Hdr: true })
        {
            var codec = (version.CodecVideo ?? "").ToLowerInvariant();
            hdr = codec.Contains("dovi") || codec.Contains("dolby") ? "Dolby Vision" : "HDR10";
        }
        StatsHdr.Text = $"HDR:         {hdr}";
        StatsPosition.Text = $"Position:    {PlayerViewModel.FormatTime(_mediaPlayer?.PlaybackSession.Position.TotalSeconds ?? 0)} / {PlayerViewModel.FormatTime(_mediaPlayer?.PlaybackSession.NaturalDuration.TotalSeconds ?? 0)}";
        StatsSession.Text = $"Session:     {_vm.Manager.SessionId ?? "?"}";
    }

    // -- Helpers --------------------------------------------------------------

    private void UpdateVolumeIcon()
    {
        if (_isMuted || VolumeSlider.Value <= 0)
            VolumeIcon.Glyph = "\uE74F"; // Mute
        else if (VolumeSlider.Value < 50)
            VolumeIcon.Glyph = "\uE993"; // Volume1
        else
            VolumeIcon.Glyph = "\uE767"; // Volume3
    }

    private void UpdatePlaybackInfo()
    {
        if (_vm == null) return;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_vm.Resolution))
            parts.Add(_vm.Resolution);
        if (!string.IsNullOrEmpty(_vm.PlayMethod))
            parts.Add(_vm.PlayMethod.ToUpperInvariant());

        PlaybackInfoText.Text = string.Join(" \u2022 ", parts);
    }

    private void ExitPlayer()
    {
        _uiTimer?.Stop();
        _hideTimer?.Stop();

        if (_mediaPlayer != null)
        {
            _mediaPlayer.Pause();
            VideoPlayer.SetMediaPlayer(null);
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        NavigateBack();
    }

    private void NavigateBack()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    private void ShowError(string message)
    {
        if (_vm != null)
        {
            _vm.IsLoading = false;
            _vm.ErrorMessage = message;
        }
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Visible;
        ErrorText.Text = message;
    }

    private static void LogToFile(string fileName, string content)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, $"{DateTime.Now}\n{content}\n");
        }
        catch { }
    }
}
