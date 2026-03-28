using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Player;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class PlayerPage : Page
{
    private PlayerViewModel? _vm;
    private MpvPlayer? _player;
    private bool _isDisposed;
    private string? _contentId;

    public PlayerPage()
    {
        this.InitializeComponent();
    }

    // ── Page lifecycle ───────────────────────────────────────────────────

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_contentId))
        {
            ShowError("No content specified for playback.");
            return;
        }

        try
        {
            await InitializePlaybackAsync(_contentId);
        }
        catch (Exception ex)
        {
            var crashLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "player_crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
            File.WriteAllText(crashLog, $"{DateTime.Now}\n{ex}\n");

            ShowError($"Player error: {ex.Message}");
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string contentId && !string.IsNullOrEmpty(contentId))
            _contentId = contentId;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CleanupAsync();
    }

    private async void CleanupAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Show nav again
        if (App.MainWindowInstance is MainWindow mw)
            mw.ShowMainNavigation();

        // Stop playback session on the server
        if (_vm?.Manager != null)
        {
            try { await _vm.Manager.StopSessionAsync(); }
            catch { }
        }

        // Dispose mpv (closes the mpv window)
        _player?.Dispose();
        _player = null;
    }

    // ── Playback initialization ──────────────────────────────────────────

    private async Task InitializePlaybackAsync(string contentId)
    {
        try
        {
            // Hide nav for immersive experience
            if (App.MainWindowInstance is MainWindow mw)
                mw.HideMainNavigation();

            // Create view model via DI
            _vm = App.Services.GetRequiredService<PlayerViewModel>();
            _vm.IsLoading = true;

            // 1. Get watch detail
            var watchDetail = await _vm.Manager.GetWatchDetailAsync(contentId);

            // Build title
            if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
                _vm.Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
            else
                _vm.Title = watchDetail.Title;

            // Populate versions
            foreach (var v in watchDetail.Versions)
                _vm.Versions.Add(v);

            // 2. Select best version
            var bestVersion = _vm.Manager.SelectBestVersion(watchDetail.Versions);
            if (bestVersion == null)
            {
                ShowError("No playable version found.");
                return;
            }

            _vm.Resolution = bestVersion.Resolution;

            // Determine start position from user data
            double startPosition = 0;
            if (watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            // 3. Start session on the server
            var session = await _vm.Manager.StartSessionAsync(bestVersion.FileId, startPosition);
            _vm.PlayMethod = session.PlayMethod;

            // 4. Initialize mpv as a standalone fullscreen window
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);

            _player = new MpvPlayer();
            _player.Initialize(windowHandle);

            // 5. Subscribe to mpv events (must marshal to UI thread)
            var dq = DispatcherQueue;

            _player.PositionChanged += pos =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.UpdatePosition(pos);
                });
            };

            _player.DurationChanged += dur =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.UpdateDuration(dur);
                });
            };

            _player.PauseChanged += paused =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.IsPlaying = !paused;
                });
            };

            _player.PlaybackEnded += () =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed) return;
                    NavigateBack();
                });
            };

            _player.FileLoaded += () =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.IsLoading = false;

                    // Switch to "now playing" view
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    PlayingOverlay.Visibility = Visibility.Visible;
                    NowPlayingText.Text = _vm.Title;
                    UpdatePlaybackInfo();

                    // Load subtitles AFTER file is loaded (sub-add fails before file load)
                    LoadSubtitlesAfterFileLoaded();

                    // Auto-play
                    _player?.Play();
                });
            };

            _player.Error += msg =>
            {
                // Log all errors to file for debugging
                try
                {
                    var logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ContinuumPlayer", "mpv_error.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    File.AppendAllText(logPath, $"{DateTime.Now}: {msg}\n");
                }
                catch { }

                // Only show fatal errors to the user (not subtitle loading failures)
                if (msg.Contains("sub-add") || msg.Contains("sub_add"))
                    return;

                dq.TryEnqueue(() =>
                {
                    if (_isDisposed) return;
                    ShowError($"Player error: {msg}");
                });
            };

            // 6. Load file
            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL available.");
                return;
            }

            // Log init info for debugging
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ContinuumPlayer", "player_init.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"{DateTime.Now}\nStreamURL: {streamUrl}\nPlayMethod: {_vm.PlayMethod}\nResolution: {_vm.Resolution}\nSession: {_vm.Manager.SessionId}\n");
            }
            catch { }

            _player.LoadFile(streamUrl);
        }
        catch (Exception ex)
        {
            var crashLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "player_crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
            File.WriteAllText(crashLog, $"{DateTime.Now}\n{ex}\n");

            ShowError($"Failed to start playback: {ex.Message}\n\nDetails logged to:\n{crashLog}");
        }
    }

    // ── Button click handlers ────────────────────────────────────────────

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Dispose mpv first (closes mpv window), then navigate back
        _player?.Dispose();
        _player = null;
        NavigateBack();
    }

    private void GoBackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    // ── Playback info ────────────────────────────────────────────────────

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

    // ── Navigation ───────────────────────────────────────────────────────

    private void NavigateBack()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    // ── Subtitle loading (after file is loaded) ────────────────────────

    private void LoadSubtitlesAfterFileLoaded()
    {
        if (_player == null || _vm == null) return;

        var subtitleUrls = _vm.Manager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            try
            {
                var codec = track.Codec?.ToLowerInvariant() ?? "";
                if (codec == "pgs" || codec == "pgssub" || codec == "dvdsub" || codec == "vobsub")
                    continue;
                _player.AddSubtitle(fullUrl, track.Label, track.Language);
            }
            catch { }
        }
    }

    // ── Error display ────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        if (_vm != null)
        {
            _vm.IsLoading = false;
            _vm.ErrorMessage = message;
        }
        LoadingOverlay.Visibility = Visibility.Collapsed;
        PlayingOverlay.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Visible;
        ErrorText.Text = message;
    }
}
