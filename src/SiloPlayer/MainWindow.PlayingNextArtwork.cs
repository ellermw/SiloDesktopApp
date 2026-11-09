using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer;

/// <summary>
/// Playing Next opens with its artwork already in place. The next episode's picture is
/// fetched while the current one plays, the hero has its final size before any image
/// arrives, and the poster and its backdrop appear together: at once when ready, with
/// one short fade when they were late.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record PlayingNextArt(string ContentId, string Url, bool Hide, int DecodeWidth,
        ApiIdentityContext Owner, BitmapImage Image, string FailureKey)
    {
        /// <summary>Set once the picture failed, so the bitmap's and the Image's failure count once.</summary>
        public bool Failed { get; set; }
    }

    private const double PlayingNextBackdropOpacity = 0.22;
    private const double PlayingNextPreloadRemainingSeconds = 300;
    private const long PlayingNextPreloadCheckMilliseconds = 2000;
    private const long PlayingNextPreloadRetryMilliseconds = 15_000;
    private const long PlayingNextPreloadMaxRetryMilliseconds = 300_000;
    private PlayingNextArt? _preparedPlayingNextArt;
    private long _playingNextPreloadCheckedAt;
    private int _playingNextPreloadQueued;
    private string? _playingNextPreloadFailedKey;
    private int _playingNextPreloadFailures;
    private long _playingNextPreloadRetryAt;
    private long _playingNextArtRequested;
    private PlayingNextArt? _playingNextShownArt;

    /// <summary>Raised on mpv's thread: arm the preload once playback nears its end.</summary>
    private void OnPlaybackPositionForPlayingNext(double position)
    {
        var duration = _playerService.Duration;
        if (duration <= 0 || (position < duration / 2 && duration - position > PlayingNextPreloadRemainingSeconds)) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _playingNextPreloadCheckedAt) < PlayingNextPreloadCheckMilliseconds) return;
        Interlocked.Exchange(ref _playingNextPreloadCheckedAt, now);
        // The latch stays set until the preload finishes, so attempts never overlap.
        if (Interlocked.Exchange(ref _playingNextPreloadQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() => _ = PreloadPlayingNextArtAsync()))
            Interlocked.Exchange(ref _playingNextPreloadQueued, 0);
    }

    /// <summary>
    /// One background attempt per next item; a failed picture is retried with growing gaps
    /// and logged once. Opening Playing Next still makes its own final attempt.
    /// </summary>
    private async Task PreloadPlayingNextArtAsync()
    {
        var contentId = _playerService.NextEpisodeContentId;
        var url = _playerService.NextEpisodePosterUrl;
        var key = contentId + "\n" + url;
        try
        {
            if (string.IsNullOrEmpty(url) ||
                (key == _playingNextPreloadFailedKey && Environment.TickCount64 < _playingNextPreloadRetryAt))
                return;
            if (await PreparePlayingNextArtAsync() is not null)
            {
                if (key == _playingNextPreloadFailedKey) _playingNextPreloadFailedKey = null;
            }
            else if (contentId == _playerService.NextEpisodeContentId && url == _playerService.NextEpisodePosterUrl)
                NotePlayingNextPreloadFailure(key);
        }
        catch (Exception ex)
        {
            if (NotePlayingNextPreloadFailure(key) == 1)
                LocalLog.AppendException("catalog_errors.txt", ex, "playing_next_artwork_preload");
        }
        finally { Interlocked.Exchange(ref _playingNextPreloadQueued, 0); }
    }

    private int NotePlayingNextPreloadFailure(string key)
    {
        _playingNextPreloadFailures = key == _playingNextPreloadFailedKey ? _playingNextPreloadFailures + 1 : 1;
        _playingNextPreloadFailedKey = key;
        var delay = Math.Min(PlayingNextPreloadMaxRetryMilliseconds,
            PlayingNextPreloadRetryMilliseconds << Math.Min(_playingNextPreloadFailures - 1, 10));
        _playingNextPreloadRetryAt = Environment.TickCount64 + delay;
        return _playingNextPreloadFailures;
    }

    private bool PlayingNextHidesArt(EpisodeSpoilerPreferences preferences) => EpisodeSpoilerPolicy.HideImage(
        _playerService.IsShufflePlayback ? _playerService.ShuffleNext?.Type ?? "episode" : "episode",
        _playerService.NextEpisodePosterIsEpisodeStill, _playerService.NextEpisodePlayed,
        _playerService.NextEpisodeIsInProgress, _playerService.NextEpisodePositionSeconds, preferences.HideImages);

    /// <summary>Decodes at the hero's real pixel width so the picture is sharp on scaled displays.</summary>
    private int PlayingNextDecodeWidth()
    {
        var width = PlayingNextHero.Width is > 0 and var set && !double.IsNaN(set) ? set : 820;
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1;
        return Math.Clamp((int)Math.Ceiling(width * scale), 320, 1640);
    }

    /// <summary>A null width accepts any decode size: the Image scales the picture to the hero.</summary>
    private bool IsCurrentPlayingNextArt(PlayingNextArt art, string? contentId, string url, bool hide, int? width) =>
        art.ContentId == (contentId ?? url) && art.Url == url && art.Hide == hide && (width is null || art.DecodeWidth == width)
        && _apiClient.IsCurrentIdentityContext(art.Owner);

    private async Task<PlayingNextArt?> PreparePlayingNextArtAsync()
    {
        var contentId = _playerService.NextEpisodeContentId;
        var url = _playerService.NextEpisodePosterUrl;
        if (string.IsNullOrEmpty(url)) return null;
        var preferences = App.Services.GetRequiredService<EpisodeSpoilerPreferences>();
        await preferences.EnsureLoadedAsync();
        if (contentId != _playerService.NextEpisodeContentId || url != _playerService.NextEpisodePosterUrl) return null;
        var hide = PlayingNextHidesArt(preferences);
        var width = PlayingNextDecodeWidth();
        if (_preparedPlayingNextArt is { } ready && IsCurrentPlayingNextArt(ready, contentId, url, hide, width)) return ready;
        var owner = _apiClient.CaptureIdentityContext();
        var image = await LoadEpisodePreviewImageAsync(contentId ?? url, url, hide, width);
        if (image is null || !_apiClient.IsCurrentIdentityContext(owner)) return null;
        // No pixels yet is not a failure: SetSourceAsync throws for an unreadable stream, a later
        // decode failure raises ImageFailed, and ShowPlayingNextArt waits for ImageOpened.
        var art = new PlayingNextArt(contentId ?? url, url, hide, width, owner, image, contentId + "\n" + url);
        image.ImageFailed += (_, e) => OnPlayingNextArtFailed(art, e.ErrorMessage);
        if (contentId == _playerService.NextEpisodeContentId && url == _playerService.NextEpisodePosterUrl)
            _preparedPlayingNextArt = art;
        return art;
    }

    /// <summary>
    /// A picture that cannot be decoded is never reused: the hero falls back to its own
    /// background, the failure is logged once, and the next attempt loads it afresh.
    /// </summary>
    private void OnPlayingNextArtFailed(PlayingNextArt art, string? error)
    {
        if (art.Failed) return;
        art.Failed = true;
        if (ReferenceEquals(_preparedPlayingNextArt, art)) _preparedPlayingNextArt = null;
        if (ReferenceEquals(_playingNextShownArt, art)) ClearPlayingNextArt();
        if (NotePlayingNextPreloadFailure(art.FailureKey) == 1)
            LocalLog.AppendLine("catalog_errors.txt", "playing_next_artwork_decode_failed " + LocalLog.RedactDiagnosticText(error));
    }

    /// <summary>Shows prepared artwork in the same frame as the overlay, or starts loading it.</summary>
    private void BeginPlayingNextArt(bool hasNextEpisode)
    {
        _playingNextArtRequested = Stopwatch.GetTimestamp();
        var imageOwner = Interlocked.Increment(ref _playingNextArtworkGeneration);
        var url = _playerService.NextEpisodePosterUrl;
        var preferences = App.Services.GetRequiredService<EpisodeSpoilerPreferences>();
        // A resize since the preload changes the decode width; the prepared picture still
        // opens with the overlay rather than being dropped for a late re-decode.
        if (hasNextEpisode && !string.IsNullOrEmpty(url) && preferences.IsLoadedForCurrentContext &&
            _preparedPlayingNextArt is { } ready &&
            IsCurrentPlayingNextArt(ready, _playerService.NextEpisodeContentId, url, PlayingNextHidesArt(preferences), null))
        {
            ShowPlayingNextArt(ready);
            return;
        }
        ClearPlayingNextArt();
        if (hasNextEpisode) _ = LoadPlayingNextPosterAsync(imageOwner);
    }

    private async Task LoadPlayingNextPosterAsync(long? reservedOwner = null)
    {
        var imageOwner = reservedOwner ?? Interlocked.Increment(ref _playingNextArtworkGeneration);
        await Task.Yield();
        var contentId = _playerService.NextEpisodeContentId;
        var owner = _playingNextPresentationGeneration;
        try
        {
            var art = await PreparePlayingNextArtAsync();
            if (art is null || !string.Equals(contentId, _playerService.NextEpisodeContentId, StringComparison.Ordinal) ||
                imageOwner != _playingNextArtworkGeneration || owner != _playingNextPresentationGeneration ||
                PlayingNextOverlay.Visibility != Visibility.Visible)
                return;
            ShowPlayingNextArt(art);
        }
        catch { /* Poster is cosmetic */ }
    }

    /// <summary>
    /// When the next item changes under an open overlay, the previous item's picture leaves the
    /// hero at once: it falls back to its own background until the new picture, if any, arrives.
    /// </summary>
    private void DropStalePlayingNextArt()
    {
        var contentId = _playerService.NextEpisodeContentId;
        var url = _playerService.NextEpisodePosterUrl;
        bool IsForNextItem(PlayingNextArt art) => !string.IsNullOrEmpty(url) && art.ContentId == (contentId ?? url) && art.Url == url;
        if (_preparedPlayingNextArt is { } prepared && !IsForNextItem(prepared)) _preparedPlayingNextArt = null;
        if (_playingNextShownArt is { } shown ? !IsForNextItem(shown) : PlayingNextPoster.Source is not null) ClearPlayingNextArt();
    }

    private void ClearPlayingNextArt()
    {
        _playingNextShownArt = null;
        foreach (var image in new[] { PlayingNextPoster, PlayingNextBackdrop })
        {
            image.OpacityTransition = null;
            image.Opacity = 0;
            image.Source = null;
        }
    }

    /// <summary>A picture that fails once it is in the hero falls back to the hero's background.</summary>
    private void OnPlayingNextPosterFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_playingNextShownArt is { } art) OnPlayingNextArtFailed(art, e.ErrorMessage);
    }

    private void ShowPlayingNextArt(PlayingNextArt art)
    {
        var image = art.Image;
        if (ReferenceEquals(_playingNextShownArt, art) && PlayingNextPoster.Opacity > 0) return;
        _playingNextShownArt = art;
        PlayingNextPoster.ImageFailed -= OnPlayingNextPosterFailed;
        PlayingNextPoster.ImageFailed += OnPlayingNextPosterFailed;
        PlayingNextPoster.Source = image;
        PlayingNextBackdrop.Source = image;
        // An already decoded picture shows in this frame; a new one waits until it opens.
        if (image.PixelWidth > 0) { RevealPlayingNextArt(); return; }
        foreach (var element in new[] { PlayingNextPoster, PlayingNextBackdrop }) { element.OpacityTransition = null; element.Opacity = 0; }
        // Listen on the shared bitmap: with two Images on one source, only one of them raises ImageOpened.
        void Opened(object sender, RoutedEventArgs e)
        {
            image.ImageOpened -= Opened;
            if (ReferenceEquals(_playingNextShownArt, art)) RevealPlayingNextArt();
        }
        image.ImageOpened += Opened;
        if (image.PixelWidth > 0) Opened(image, new RoutedEventArgs());
    }

    private void RevealPlayingNextArt()
    {
        var fade = Stopwatch.GetElapsedTime(_playingNextArtRequested) > ArtworkReveal.InstantReveal && ArtworkReveal.AnimationsEnabled;
        var transition = fade ? new ScalarTransition { Duration = ArtworkReveal.RevealDuration } : null;
        PlayingNextPoster.OpacityTransition = transition;
        PlayingNextBackdrop.OpacityTransition = transition;
        PlayingNextPoster.Opacity = 1;
        PlayingNextBackdrop.Opacity = PlayingNextBackdropOpacity;
    }
}
