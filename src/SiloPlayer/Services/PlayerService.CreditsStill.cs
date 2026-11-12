using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using Windows.Graphics.Imaging;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private CancellationTokenSource? _creditsStillCts;
    private string? _creditsStillKey;
    private long _creditsStillGeneration;
    private EpisodeSpoilerPreferences? _creditsSpoilers;
    private string? _creditsStillPath;
    private int _creditsStillLoading;
    private readonly object _creditsStillGate = new();
    private (string ContentId, string Title)? _creditsEpisodeTitle;

    private void PublishCreditsEpisodeDetail(bool artworkOnly = false)
        => PublishCreditsEpisodeDetailCore(artworkOnly, null);

    private void PublishCreditsEpisodeDetailCore(bool artworkOnly, string? rawEpisodeTitle)
    {
        lock (_creditsStillGate)
        {
            var next = EpisodeNavigation.Next;
            // Navigation labels may already contain S1:E2. Keep the raw server
            // title for the card, which has its own season/episode line.
            var title = rawEpisodeTitle ?? (_creditsEpisodeTitle is { } saved && saved.ContentId == next?.ContentId
                ? saved.Title : next?.Title ?? "");
            RetireCreditsStill();
            if (next == null) return;
            _creditsEpisodeTitle = (next.ContentId, title);
            _creditsStillCts = CancellationTokenSource.CreateLinkedTokenSource(_playbackCts?.Token ?? CancellationToken.None, _authorityLifetimeCts.Token);
            _creditsStillKey = $"{Interlocked.Increment(ref _creditsStillGeneration)}:{next.ContentId}";
            _creditsSpoilers = App.Services.GetService<EpisodeSpoilerPreferences>();
            if (_creditsSpoilers != null) _creditsSpoilers.Changed += OnCreditsSpoilersChanged;
            _mpv?.SendScriptMessage(artworkOnly ? "osc-update-next-episode-still-source" : "osc-set-next-episode-detail", System.Text.Json.JsonSerializer.Serialize(new
            {
                season_number = next.SeasonNumber, episode_number = next.EpisodeNumber, title,
                still_key = _creditsStillKey, still_url = next.PosterUrl,
            }));
        }
    }

    private void OnCreditsSpoilersChanged(object? sender, EventArgs e)
    {
        // Clear the visible raw bitmap before any asynchronous preference read.
        // A newly hidden still can never remain unblurred during the replacement.
        var dispatcher = _nativeMenuDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
        dispatcher?.TryEnqueue(() =>
        {
            if (_creditsStillKey == null) return;
            PublishCreditsEpisodeDetail(artworkOnly: true);
            // Keep an already running countdown; replacing its artwork is not a
            // new episode/countdown. The OSC owns that functional clock.
        });
    }

    private async Task LoadCreditsStillAsync(string key)
    {
        CancellationTokenSource cts; CancellationToken token;
        lock (_creditsStillGate)
        {
            if (key != _creditsStillKey || _creditsStillCts == null || Interlocked.Exchange(ref _creditsStillLoading, 1) != 0) return;
            cts = _creditsStillCts; token = cts.Token;
        }
        var next = EpisodeNavigation.Next;
        var mpv = _mpv;
        var identity = _activeAuthority.ApiClient.CaptureIdentityContext();
        bool Current() => !token.IsCancellationRequested && key == _creditsStillKey && ReferenceEquals(mpv, _mpv) &&
            _activeAuthority.ApiClient.IsCurrentIdentityContext(identity) && next?.ContentId == NextEpisodeContentId;
        string? rawPath = null;
        try
        {
            if (next == null || string.IsNullOrWhiteSpace(next.PosterUrl) || mpv == null) return;
            var preferences = _creditsSpoilers;
            if (preferences == null) return; // Unknown privacy state never exposes an episode still.
            await preferences.EnsureLoadedAsync(token).ConfigureAwait(false);
            if (!Current() || !preferences.IsLoadedForCurrentContext) return;
            var hide = EpisodeSpoilerPolicy.HideImage("episode", next.PosterIsEpisodeStill, next.Played, next.IsInProgress, next.PositionSeconds, preferences.HideImages);
            var url = _activeAuthority.ApiClient.ResolveServerUrl(next.PosterUrl);
            if (url == null) return;
            var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(next.ContentId,
                "credits-still", url, App.Services.GetRequiredService<HttpClient>(), token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path) || !Current()) return;
            var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            if (hide) bytes = await ArtworkEffects.BlurEpisodeStillAsync(bytes, token).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            // Bound the decode and cover-crop to the actual 16:9 card surface.
            var scale = Math.Max(224d / decoder.OrientedPixelWidth, 126d / decoder.OrientedPixelHeight);
            var scaledWidth = (uint)Math.Max(224, Math.Ceiling(decoder.OrientedPixelWidth * scale));
            var scaledHeight = (uint)Math.Max(126, Math.Ceiling(decoder.OrientedPixelHeight * scale));
            var transform = new BitmapTransform { ScaledWidth = scaledWidth, ScaledHeight = scaledHeight,
                Bounds = new BitmapBounds { X = (scaledWidth - 224) / 2, Y = (scaledHeight - 126) / 2, Width = 224, Height = 126 } };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            if (!Current()) return;
            var directory = PlaybackArtworkDirectory("CreditsStill"); Directory.CreateDirectory(directory);
            rawPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".bgra");
            var pixels = data.DetachPixelData();
            for (var y = 0; y < 126; y++)
            for (var x = 0; x < 224; x++)
            {
                var dx = Math.Max(16 - x, x - 207); var dy = Math.Max(16 - y, y - 109);
                if (dx > 0 && dy > 0 && dx * dx + dy * dy > 256) Array.Clear(pixels, (y * 224 + x) * 4, 4);
            }
            await File.WriteAllBytesAsync(rawPath, pixels, token).ConfigureAwait(false);
            lock (_creditsStillGate)
            {
                if (!Current()) return;
                _creditsStillPath = rawPath;
                mpv.SendScriptMessage("osc-set-next-episode-still", key, rawPath, "224", "126", "896");
                rawPath = null;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Credits still unavailable: {ex.Message}"); }
        finally
        {
            if (rawPath != null) TryDelete(rawPath);
            if (ReferenceEquals(cts, _creditsStillCts)) Interlocked.Exchange(ref _creditsStillLoading, 0);
        }
    }

    private void RetireCreditsStill()
    {
        lock (_creditsStillGate)
        {
            if (_creditsSpoilers != null) _creditsSpoilers.Changed -= OnCreditsSpoilersChanged;
            _creditsSpoilers = null;
            _creditsEpisodeTitle = null;
            var cts = Interlocked.Exchange(ref _creditsStillCts, null);
            cts?.Cancel(); cts?.Dispose();
            var key = _creditsStillKey; _creditsStillKey = null;
            if (key != null) _mpv?.SendScriptMessage("osc-set-next-episode-still", key, "", "0", "0", "0");
            Interlocked.Exchange(ref _creditsStillLoading, 0);
            var path = Interlocked.Exchange(ref _creditsStillPath, null);
            if (path != null) _ = Task.Delay(1000).ContinueWith(_ => TryDelete(path), TaskScheduler.Default);
        }
    }
}
