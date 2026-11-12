using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace SiloPlayer.Services;

// Still-watching gate, finish-time format and the loading cover for the
// production OSC (Silo Server #3917 and #3993 at 0b579aa6).
public partial class PlayerService
{
    private readonly StillWatchingSession _stillWatching = new();
    private readonly object _loadingCoverGate = new();
    private LoadingCoverOwner? _loadingCover;
    private long _loadingCoverGeneration;

    /// <summary>The client-local threshold; never read from or written to the server.</summary>
    public int StillWatchingThresholdSetting
    {
        get
        {
            try { return StillWatchingThreshold.Normalize(_settingsService.Load().StillWatchingThreshold); }
            catch { return StillWatchingThreshold.Default; }
        }
    }

    /// <summary>Whether the next unattended advance must wait for the viewer.</summary>
    public bool IsStillWatchingGated => !IsWatchTogetherPlayback && _stillWatching.IsGated(StillWatchingThresholdSetting);

    /// <summary>Whether an unattended advance from the current title would start now.</summary>
    public bool CanAdvanceAutomatically => !IsWatchTogetherPlayback &&
        _stillWatching.CanBeginAutomaticAdvance(ContentId ?? "", StillWatchingThresholdSetting, IsWatchTogetherPlayback);

    /// <summary>Raised whenever the gate may have changed (count or threshold).</summary>
    public event Action? StillWatchingChanged;

    /// <summary>Saves this device's threshold and applies it to the current session.</summary>
    public void SetStillWatchingThreshold(int threshold)
    {
        var value = StillWatchingThreshold.Normalize(threshold);
        var settings = _settingsService.Load();
        if (settings.StillWatchingThreshold == value) return;
        settings.StillWatchingThreshold = value;
        _settingsService.Save(settings);
        PublishStillWatchingGate();
    }

    private void PublishStillWatchingGate()
    {
        _mpv?.SendScriptMessage("osc-set-autoplay-gate", IsStillWatchingGated ? "true" : "false");
        InvokeSubscribersSafely(StillWatchingChanged, nameof(StillWatchingChanged));
    }

    // Seam for the localized label: callers resolve PlaybackEndTime.LabelKey
    // with argument "time" once the shared catalog lands; the OSC substitutes
    // "{time}" with its live clock text.
    internal static Func<string> EndTimeLabelTemplate { get; set; } = () => PlaybackEndTime.FallbackLabelTemplate;

    private void PublishPlaybackHudPreferences(SiloPlayer.Player.MpvPlayer mpv)
    {
        string clock;
        try { clock = PlaybackEndTime.ResolveClock(_settingsService.Load().UiTimeFormat, CultureInfo.CurrentCulture); }
        catch { clock = PlaybackEndTime.ResolveClock(null, CultureInfo.CurrentCulture); }
        mpv.SendScriptMessage("osc-set-end-time-format", clock, EndTimeLabelTemplate());
        mpv.SendScriptMessage("osc-set-autoplay-gate", IsStillWatchingGated ? "true" : "false");
    }

    /// <summary>Owner, backdrop URL and thumbhash for the shell's plan-read cover.</summary>
    public event Action<PlaybackLoadingBackdrop>? LoadingBackdropChanged;

    private sealed class LoadingCoverOwner(string key, string contentId, CancellationTokenSource cts)
    {
        public string Key { get; } = key;
        public string ContentId { get; } = contentId;
        public CancellationTokenSource Cts { get; } = cts;
        public byte[]? Pixels;
        public int Width, Height, Stride;
        public int FrameWidth = LoadingCoverBitmap.MaximumWidth, FrameHeight = LoadingCoverBitmap.MaximumHeight;
        public readonly List<string> Files = [];
    }

    // Called once the watch detail names the title's backdrop: the shell
    // paints it under the plan read and the OSC under the first frame.
    private void BeginLoadingCover(WatchDetailResponse detail, object loadingOwner, CancellationToken playbackToken)
    {
        RetireLoadingCover();
        var url = _apiClient.ResolveServerUrl(detail.BackdropUrl);
        var thumbhash = string.IsNullOrWhiteSpace(detail.BackdropThumbhash) ? null : detail.BackdropThumbhash;
        InvokeSubscribersSafely(LoadingBackdropChanged, new PlaybackLoadingBackdrop(loadingOwner, url, thumbhash), nameof(LoadingBackdropChanged));
        if (url == null && thumbhash == null) return;
        var key = $"{detail.ContentId}-{Interlocked.Increment(ref _loadingCoverGeneration)}";
        var owner = new LoadingCoverOwner(key, detail.ContentId, CancellationTokenSource.CreateLinkedTokenSource(playbackToken));
        lock (_loadingCoverGate) _loadingCover = owner;
        _ = LoadLoadingCoverAsync(owner, url, thumbhash);
    }

    private async Task LoadLoadingCoverAsync(LoadingCoverOwner owner, string? url, string? thumbhash)
    {
        var token = owner.Cts.Token;
        try
        {
            if (thumbhash != null)
            {
                // Thumbhash paints at once; a malformed hash costs only the placeholder.
                try
                {
                    var image = ThumbhashDecoder.Decode(thumbhash);
                    var bgra = new byte[image.Width * image.Height * 4];
                    for (var i = 0; i < image.Width * image.Height; i++)
                    {
                        bgra[i * 4] = image.Rgba[i * 4 + 2];
                        bgra[i * 4 + 1] = image.Rgba[i * 4 + 1];
                        bgra[i * 4 + 2] = image.Rgba[i * 4];
                        bgra[i * 4 + 3] = 255;
                    }
                    await PublishLoadingCoverAsync(owner, bgra, image.Width, image.Height, image.Width * 4, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogToFile("state_trace.txt", $"Loading cover thumbhash skipped: {ex.Message}");
                }
            }
            if (url == null) return;
            var http = App.Services.GetRequiredService<HttpClient>();
            var path = await App.Services.GetRequiredService<ImageService>()
                .GetImageDiskPathAsync($"loading_{owner.ContentId}", "backdrop", url, http, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path)) return;
            token.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var scale = Math.Min(1d, LoadingCoverBitmap.MaximumWidth / (double)Math.Max(1, decoder.OrientedPixelWidth));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            token.ThrowIfCancellationRequested();
            var width = (int)transform.ScaledWidth;
            var height = (int)transform.ScaledHeight;
            await PublishLoadingCoverAsync(owner, data.DetachPixelData(), width, height, width * 4, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Loading cover backdrop unavailable: {ex.Message}");
        }
    }

    private async Task PublishLoadingCoverAsync(LoadingCoverOwner owner, byte[] pixels, int width, int height, int stride,
        CancellationToken token)
    {
        int frameWidth, frameHeight;
        lock (_loadingCoverGate)
        {
            if (!ReferenceEquals(_loadingCover, owner)) return;
            owner.Pixels = pixels; owner.Width = width; owner.Height = height; owner.Stride = stride;
            frameWidth = owner.FrameWidth; frameHeight = owner.FrameHeight;
        }
        await ComposeLoadingCoverAsync(owner, frameWidth, frameHeight, token).ConfigureAwait(false);
    }

    private async Task ComposeLoadingCoverAsync(LoadingCoverOwner owner, int frameWidth, int frameHeight, CancellationToken token)
    {
        byte[]? source; int width, height, stride;
        lock (_loadingCoverGate)
        {
            if (!ReferenceEquals(_loadingCover, owner) || owner.Pixels == null) return;
            source = owner.Pixels; width = owner.Width; height = owner.Height; stride = owner.Stride;
        }
        var (targetWidth, targetHeight) = LoadingCoverBitmap.TargetSize(frameWidth, frameHeight);
        if (targetWidth <= 0) return;
        // OSC ui_scale: 1 up to 1920 wide, then sqrt(width/1920); expressed in bitmap pixels.
        var oscScale = frameWidth <= 1920 ? 1d : Math.Sqrt(frameWidth / 1920d);
        var layoutScale = oscScale * targetWidth / (double)frameWidth;
        var composed = await Task.Run(() => LoadingCoverBitmap.Compose(source, width, height, stride, targetWidth, targetHeight, layoutScale), token)
            .ConfigureAwait(false);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "LoadingCover");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{targetWidth}x{targetHeight}.bgra");
        await File.WriteAllBytesAsync(path, composed, token).ConfigureAwait(false);
        var mpv = _mpv;
        lock (_loadingCoverGate)
        {
            if (!ReferenceEquals(_loadingCover, owner) || token.IsCancellationRequested || mpv == null)
            {
                TryDelete(path);
                return;
            }
            owner.Files.Add(path);
        }
        mpv.SendScriptMessage("osc-set-loading-backdrop", owner.Key, path,
            targetWidth.ToString(CultureInfo.InvariantCulture), targetHeight.ToString(CultureInfo.InvariantCulture),
            (targetWidth * 4).ToString(CultureInfo.InvariantCulture),
            frameWidth.ToString(CultureInfo.InvariantCulture), frameHeight.ToString(CultureInfo.InvariantCulture));
    }

    // The OSC asks again when the frame it covers changes size.
    private void RequestLoadingCoverSize(string key, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || frameWidth > 16384 || frameHeight > 16384) return;
        LoadingCoverOwner? owner;
        lock (_loadingCoverGate)
        {
            owner = _loadingCover;
            if (owner == null || owner.Key != key) return;
            if (owner.FrameWidth == frameWidth && owner.FrameHeight == frameHeight && owner.Files.Count > 0) return;
            owner.FrameWidth = frameWidth; owner.FrameHeight = frameHeight;
        }
        _ = ComposeLoadingCoverAsync(owner, frameWidth, frameHeight, owner.Cts.Token).ContinueWith(task =>
        {
            if (task.Exception != null && !owner.Cts.IsCancellationRequested)
                LogToFile("state_trace.txt", $"Loading cover resize failed: {task.Exception.GetBaseException().Message}");
        }, TaskScheduler.Default);
    }

    private void RetireLoadingCover()
    {
        LoadingCoverOwner? owner;
        lock (_loadingCoverGate)
        {
            owner = _loadingCover;
            _loadingCover = null;
        }
        if (owner == null) return;
        try { owner.Cts.Cancel(); } catch (ObjectDisposedException) { }
        _mpv?.SendScriptMessage("osc-clear-loading-backdrop", owner.Key);
        string[] files;
        lock (_loadingCoverGate) files = owner.Files.ToArray();
        // The OSC may still map the previous bitmap until its overlay-remove runs.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            foreach (var file in files) TryDelete(file);
            owner.Cts.Dispose();
        }, TaskScheduler.Default);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort cache cleanup */ }
    }
}

/// <summary>The backdrop a playback request loads behind; <see cref="Owner"/> is its loading surface owner.</summary>
public sealed record PlaybackLoadingBackdrop(object Owner, string? Url, string? Thumbhash);
