using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private long _playingNextArtworkGeneration;
    private long _playingNextOverviewGeneration;
    private long _playingNextOnDeckGeneration;
    private void NotifyEpisodeSpoilerSettingFromRealtime(string channel, string eventName, JsonElement data)
    {
        if (channel != "user_settings" || eventName != "user_settings.changed" || data.ValueKind != JsonValueKind.Object) return;
        if (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
            && profile.GetString() is { Length: > 0 } changed && changed != _authService.SelectedProfileId) return;
        if (!data.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String
            || key.GetString() is not ("catalog.hide_unwatched_episode_images" or "catalog.hide_unwatched_episode_overviews")) return;
        var preferences = App.Services.GetRequiredService<EpisodeSpoilerPreferences>();
        preferences.Invalidate();
        _ = RefreshEpisodeSpoilerPreferencesAsync(preferences);
    }

    private static async Task RefreshEpisodeSpoilerPreferencesAsync(EpisodeSpoilerPreferences preferences)
    {
        try { await preferences.EnsureLoadedAsync(); }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "episode_spoiler_preferences"); }
    }

    private void OnEpisodeSpoilerPreferencesChanged(object? sender, EventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (PlayingNextOverlay.Visibility != Visibility.Visible) return;
            _preparedPlayingNextArt = null;
            ClearPlayingNextArt();
            PlayingNextOverviewText.Text = "";
            PlayingNextOverviewText.Visibility = Visibility.Collapsed;
            PlayingNextOnDeckRepeater.ItemsSource = null;
            _ = UpdatePlayingNextOverviewAsync(_playingNextPresentationGeneration);
            _ = LoadPlayingNextPosterAsync();
            _ = LoadPlayingNextOnDeckAsync();
        });
    }

    private async Task UpdatePlayingNextOverviewAsync(long owner)
    {
        var overviewOwner = Interlocked.Increment(ref _playingNextOverviewGeneration);
        var identity = _apiClient.CaptureIdentityContext();
        await Task.Yield();
        try
        {
            var preferences = App.Services.GetRequiredService<EpisodeSpoilerPreferences>();
            await preferences.EnsureLoadedAsync();
            if (!_apiClient.IsCurrentIdentityContext(identity) || overviewOwner != _playingNextOverviewGeneration || owner != _playingNextPresentationGeneration || PlayingNextOverlay.Visibility != Visibility.Visible) return;
            ShowPlayingNextOverview(preferences.HideOverviews);
        }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "playing_next_overview"); }
    }

    /// <summary>Shows the next item's overview unless it is an unwatched episode and overviews are hidden.</summary>
    private void ShowPlayingNextOverview(bool hideOverviews)
    {
        var protectedEpisode = (!_playerService.IsShufflePlayback || _playerService.ShuffleNext?.Type == "episode")
            && EpisodeSpoilerPolicy.IsUnwatched(_playerService.NextEpisodePlayed,
                _playerService.NextEpisodeIsInProgress, _playerService.NextEpisodePositionSeconds);
        var overview = protectedEpisode && hideOverviews ? "" : _playerService.NextEpisodeOverview ?? "";
        PlayingNextOverviewText.Text = overview;
        PlayingNextOverviewText.Visibility = string.IsNullOrWhiteSpace(overview) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary><paramref name="beforeSource"/> runs before the bitmap gets its source, so no decode failure is missed.</summary>
    private async Task<BitmapImage?> LoadEpisodePreviewImageAsync(string contentId, string url, bool hide, int decodeWidth = 640,
        Action<BitmapImage>? beforeSource = null)
    {
        var identity = _apiClient.CaptureIdentityContext();
        var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(contentId,
            "playing-next", url, App.Services.GetRequiredService<HttpClient>());
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!_apiClient.IsCurrentIdentityContext(identity)) return null;
        var bytes = hide ? await ArtworkEffects.BlurEpisodeStillAsync(await File.ReadAllBytesAsync(path)) : null;
        // Create the bitmap through the card pipeline's decoder: a preload during playback
        // must not contend with library artwork on the UI thread.
        using var decode = await new ArtworkDecodeGate(PlayingNextHero, static () => true, ArtworkDecodeGate.BitmapCreateLock)
            .EnterAsync(CancellationToken.None);
        // The decode size must be set before the source, or the first decode runs at full size.
        if (bytes is null)
        {
            var uriImage = new BitmapImage { DecodePixelWidth = decodeWidth };
            beforeSource?.Invoke(uriImage);
            uriImage.UriSource = new Uri(path);
            return uriImage;
        }
        var image = new BitmapImage();
        beforeSource?.Invoke(image);
        using var stream = new MemoryStream(bytes);
        await image.SetSourceAsync(stream.AsRandomAccessStream());
        if (!_apiClient.IsCurrentIdentityContext(identity)) return null;
        return image;
    }
}
