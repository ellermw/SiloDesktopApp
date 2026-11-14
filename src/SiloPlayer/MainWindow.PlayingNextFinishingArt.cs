using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer;

/// <summary>
/// The finishing slot of Playing Next. While the episode still plays, the live native video
/// covers the slot. At true EOF that window is hidden, so the slot shows the finished title's
/// own backdrop instead of standing empty; with no usable picture the slot leaves the screen.
/// The picture is the watch detail's backdrop (a series backdrop for an episode): never an
/// episode still, and never anything of the next item.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record FinishingArt(string ContentId, string Url, ApiIdentityContext Owner, BitmapImage Image)
    {
        /// <summary>Decoded: only then may it replace the hidden video.</summary>
        public bool Opened { get; set; }
        public bool Failed { get; set; }
    }

    // The slot is 44% of the window, at most 832 wide; decode once for the largest it can become.
    private const double PlayingNextFinishingSlotMaxWidth = 832;
    private FinishingArt? _finishingArt;
    // Owns the open presentation's load: closing or reopening Playing Next retires its result.
    private long _finishingArtGeneration;
    private bool _finishingArtLoading;
    private Brush? _finishingSlotLiveBackground;
    private string? _finishingArtFailureLogged;

    /// <summary>The finished title's backdrop, while its watch detail is still the current playback.</summary>
    private (string ContentId, string Url)? FinishingArtSource()
    {
        var detail = _playerService.WatchDetail;
        if (detail is null || string.IsNullOrEmpty(detail.ContentId) || _playerService.IsSwitchingContent ||
            !string.Equals(detail.ContentId, _playerService.ContentId, StringComparison.Ordinal))
            return null;
        return _apiClient.ResolveServerUrl(detail.BackdropUrl) is { Length: > 0 } url ? (detail.ContentId, url) : null;
    }

    private bool IsCurrentFinishingArt(FinishingArt art) =>
        !art.Failed && FinishingArtSource() is { } source && art.ContentId == source.ContentId && art.Url == source.Url &&
        _apiClient.IsCurrentIdentityContext(art.Owner);

    /// <summary>A new presentation: nothing of the previous one remains, and its own picture starts loading.</summary>
    private void BeginPlayingNextFinishingArt()
    {
        RetirePlayingNextFinishingArt();
        _ = LoadPlayingNextFinishingArtAsync();
    }

    /// <summary>
    /// True EOF under an open presentation. The picture prepared during the live preview shows in the
    /// same frame the native window is hidden; a picture that failed earlier gets one final attempt.
    /// </summary>
    private void ShowPlayingNextFinishingArtAtEnd()
    {
        if (_finishingArt is { } prepared && !IsCurrentFinishingArt(prepared)) _finishingArt = null;
        if (_finishingArt is null && !_finishingArtLoading) _ = LoadPlayingNextFinishingArtAsync();
        UpdatePlayingNextFinishingArt();
    }

    /// <summary>Drops the picture and any load still running for it, and restores the live slot.</summary>
    private void RetirePlayingNextFinishingArt()
    {
        _finishingArtGeneration++;
        _finishingArtLoading = false;
        _finishingArt = null;
        _finishingSlotLiveBackground ??= PlayingNextFinishingSlot.Background;
        ApplyPlayingNextFinishingSlot(art: null, revealed: false, live: true, fade: false);
        PlayingNextFinishingPanel.Visibility = Visibility.Visible;
    }

    private void OnPlayingNextOverlayVisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (PlayingNextOverlay.Visibility != Visibility.Visible) RetirePlayingNextFinishingArt();
    }

    private int PlayingNextFinishingDecodeWidth() => Math.Clamp(
        (int)Math.Ceiling(PlayingNextFinishingSlotMaxWidth * (Content?.XamlRoot?.RasterizationScale ?? 1)), 320, 1664);

    private async Task LoadPlayingNextFinishingArtAsync()
    {
        if (FinishingArtSource() is not { } source) return;
        var generation = _finishingArtGeneration;
        var owner = _apiClient.CaptureIdentityContext();
        _finishingArtLoading = true;
        FinishingArt? art = null;
        var opened = false;
        string? failure = null;
        void Opened(object sender, RoutedEventArgs e)
        {
            opened = true;
            if (art is null) return;
            art.Opened = true;
            if (ReferenceEquals(_finishingArt, art)) UpdatePlayingNextFinishingArt();
        }
        void Failed(object sender, ExceptionRoutedEventArgs e)
        {
            // A decode that fails while the bitmap is being created is recorded for the art that follows.
            if (art is null) failure ??= e.ErrorMessage ?? "";
            else OnPlayingNextFinishingArtFailed(art, e.ErrorMessage);
        }
        BitmapImage? image = null;
        try
        {
            // The loading cover fetched this file when playback began; the same cache entry serves both.
            image = await LoadEpisodePreviewImageAsync(PlayerService.WatchBackdropCacheId(source.ContentId), source.Url, hide: false,
                PlayingNextFinishingDecodeWidth(), bitmap => { bitmap.ImageOpened += Opened; bitmap.ImageFailed += Failed; },
                PlayerService.WatchBackdropImageType);
        }
        catch (Exception ex)
        {
            failure ??= ex.GetType().Name;
        }
        // Playing Next closed or reopened meanwhile: this result belongs to nobody.
        if (generation != _finishingArtGeneration) return;
        _finishingArtLoading = false;
        if (image is not null && failure is null)
        {
            art = new FinishingArt(source.ContentId, source.Url, owner, image) { Opened = opened || image.PixelWidth > 0 };
            // The playback or the profile may have moved on under an open screen.
            if (IsCurrentFinishingArt(art)) _finishingArt = art;
        }
        // A result refused because the profile changed is not a failed picture.
        else if (_apiClient.IsCurrentIdentityContext(owner)) LogPlayingNextFinishingArtFailure(source, failure ?? "unavailable");
        UpdatePlayingNextFinishingArt();
    }

    private void LogPlayingNextFinishingArtFailure((string ContentId, string Url) source, string? error)
    {
        var key = source.ContentId + "\n" + source.Url;
        if (key == _finishingArtFailureLogged) return;
        _finishingArtFailureLogged = key;
        LocalLog.AppendLine("catalog_errors.txt", "playing_next_finishing_artwork_unavailable " + LocalLog.RedactDiagnosticText(error));
    }

    /// <summary>A picture that cannot be decoded never shows: the slot falls back to leaving the screen.</summary>
    private void OnPlayingNextFinishingArtFailed(FinishingArt art, string? error)
    {
        if (art.Failed) return;
        art.Failed = true;
        LogPlayingNextFinishingArtFailure((art.ContentId, art.Url), error);
        if (!ReferenceEquals(_finishingArt, art)) return;
        _finishingArt = null;
        UpdatePlayingNextFinishingArt();
    }

    private void PlayingNextFinishingArt_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_finishingArt is { } art && ReferenceEquals(PlayingNextFinishingArt.Source, art.Image))
            OnPlayingNextFinishingArtFailed(art, e.ErrorMessage);
    }

    /// <summary>
    /// Puts the slot in the state its presentation calls for. Safe to call whenever the playback, the
    /// profile or the picture may have changed: a picture that no longer belongs is dropped here.
    /// </summary>
    private void UpdatePlayingNextFinishingArt()
    {
        if (PlayingNextOverlay.Visibility != Visibility.Visible) return;
        if (_finishingArt is { } prepared && !IsCurrentFinishingArt(prepared)) _finishingArt = null;
        var art = _finishingArt;
        var panel = PlayingNextFinishingPanel;
        if (!_playerService.IsPostRollVideoEnded)
        {
            // The episode is still playing: the native preview owns the slot. The picture waits
            // underneath, decoded but unseen, so it can take over in the frame the video ends.
            ApplyPlayingNextFinishingSlot(art, revealed: false, live: true, fade: false);
            panel.Visibility = Visibility.Visible;
            return;
        }
        if (art is { Opened: true })
        {
            // A picture that arrives after the video has gone fades in; one already waiting does not.
            var late = panel.Visibility == Visibility.Visible && PlayingNextFinishingTitle.Opacity == 0;
            panel.Visibility = Visibility.Visible;
            ApplyPlayingNextFinishingSlot(art, revealed: true, live: false, fade: late && ArtworkReveal.AnimationsEnabled);
            return;
        }
        ApplyPlayingNextFinishingSlot(art, revealed: false, live: false, fade: false);
        // While the picture is on its way the slot keeps its place, unseen, so the screen does not
        // jump twice. With nothing to show there is no rectangle: the slot leaves the layout.
        panel.Visibility = art is not null || _finishingArtLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPlayingNextFinishingSlot(FinishingArt? art, bool revealed, bool live, bool fade)
    {
        var image = PlayingNextFinishingArt;
        if (!ReferenceEquals(image.Source, art?.Image)) image.Source = art?.Image;
        _finishingSlotLiveBackground ??= PlayingNextFinishingSlot.Background;
        // Only the live preview has a dark surface behind it; an ended slot is its picture or nothing.
        PlayingNextFinishingSlot.Background = live ? _finishingSlotLiveBackground : null;
        var transition = fade ? new ScalarTransition { Duration = ArtworkReveal.RevealDuration } : null;
        image.OpacityTransition = transition;
        PlayingNextFinishingTitle.OpacityTransition = transition;
        image.Opacity = revealed ? 1 : 0;
        PlayingNextFinishingTitle.Opacity = live || revealed ? 1 : 0;
    }
}
