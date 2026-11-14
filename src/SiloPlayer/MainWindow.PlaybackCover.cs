
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer;

// The plan-read half of PlayerLoadingCover (Silo Server #3993): the native
// video window stays hidden until the plan is ready, so the shell's loading
// surface paints the title's backdrop until the OSC's first-frame cover takes
// over with the same picture.
public sealed partial class MainWindow
{
    private const double LoadingBackdropOpacity = 0.22;
    private long _loadingBackdropGeneration;
    private object? _loadingBackdropOwner;
    private Storyboard? _loadingBackdropFade;

    private void OnLoadingBackdropChanged(PlaybackLoadingBackdrop backdrop)
        => DispatcherQueue.TryEnqueue(() => ApplyLoadingBackdrop(backdrop));

    private async void ApplyLoadingBackdrop(PlaybackLoadingBackdrop backdrop)
    {
        // Only the request that owns the loading surface may paint on it.
        if (!ReferenceEquals(Volatile.Read(ref _loadingOverlayOwner), backdrop.Owner)) return;
        ClearLoadingBackdrop();
        if (backdrop.Url == null && backdrop.Thumbhash == null) return;
        var generation = _loadingBackdropGeneration;
        _loadingBackdropOwner = backdrop.Owner;
        LoadingBackdropLayer.Visibility = Visibility.Visible;
        if (backdrop.Url is { } url &&
            new Converters.UrlToImageSourceConverter().Convert(url, typeof(ImageSource), null!, "") is ImageSource picture)
        {
            if (picture is BitmapImage bitmap)
            {
                bitmap.ImageOpened += (_, _) =>
                {
                    if (generation == _loadingBackdropGeneration) FadeInLoadingBackdrop();
                };
                LoadingBackdropImage.Source = bitmap;
                if (bitmap.PixelWidth > 0) FadeInLoadingBackdrop();
            }
            else
            {
                LoadingBackdropImage.Source = picture;
                FadeInLoadingBackdrop();
            }
        }
        if (backdrop.Thumbhash is { } hash)
        {
            // A malformed hash costs only the placeholder, never the cover.
            ImageSource? placeholder = null;
            try { placeholder = await ThumbhashBitmaps.GetAsync(hash, dim: false); }
            catch { }
            if (generation != _loadingBackdropGeneration) return;
            LoadingBackdropPlaceholder.Source = placeholder;
        }
    }

    private void FadeInLoadingBackdrop()
    {
        _loadingBackdropFade?.Stop();
        var fade = new DoubleAnimation
        {
            To = LoadingBackdropOpacity,
            Duration = TimeSpan.FromMilliseconds(500),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(fade, LoadingBackdropImage);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _loadingBackdropFade = new Storyboard();
        _loadingBackdropFade.Children.Add(fade);
        _loadingBackdropFade.Begin();
    }

    // Retires every painted picture and any image still loading for it.
    private void ClearLoadingBackdrop()
    {
        _loadingBackdropGeneration++;
        _loadingBackdropOwner = null;
        _loadingBackdropFade?.Stop();
        _loadingBackdropFade = null;
        LoadingBackdropImage.Source = null;
        LoadingBackdropImage.Opacity = 0;
        LoadingBackdropPlaceholder.Source = null;
        LoadingBackdropLayer.Visibility = Visibility.Collapsed;
    }

    private void RetireLoadingBackdropUnlessOwnedBy(object? owner)
    {
        if (_loadingBackdropOwner != null && !ReferenceEquals(_loadingBackdropOwner, owner)) ClearLoadingBackdrop();
    }
}

// Still-watching gate on the ended screen (Silo Server #3917 PlayingNextScreen):
// past the limit the card stays up without a countdown, while the autoplay
// switch keeps showing the viewer's saved setting.
public sealed partial class MainWindow
{
    private bool PlayingNextCountdownAllowed => _playingNextAutoPlay && !_playerService.IsStillWatchingGated;

    private void OnStillWatchingChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        EnsurePlayingNextTextKeys();
        if (PlayingNextOverlay.Visibility != Visibility.Visible || _playingNextFinishedCountdown) return;
        if (!PlayingNextCountdownAllowed && _playingNextTimer != null)
        {
            StopPlayingNextCountdown();
            _playingNextRemaining = PlayingNextCountdownSeconds;
            PlayingNextCountdownText.Text = PlayingNextCountdown(_playingNextRemaining);
            PlayingNextCountdownRing.Value = _playingNextRemaining;
        }
        else if (PlayingNextCountdownAllowed && _playingNextTimer == null &&
                 _playerService.IsPostRollVideoEnded && _playerService.HasNextEpisodeForCurrentPlayback)
        {
            StartPlayingNextCountdown();
        }
        UpdatePlayingNextAutoPlayVisuals();
    });
}
