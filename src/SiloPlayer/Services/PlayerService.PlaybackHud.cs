using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace SiloPlayer.Services;

// Still-watching gate, finish-time format and the loading cover for the
// production OSC (Silo Server #3917 and #3993 at 0b579aa6).
public partial class PlayerService
{
    public void PositionPostRollPreview(double x, double y, double width, double height)
    {
        _videoWindow?.SetPostRollPreviewBounds(x, y, width, height);
        if (_postRollActive && !_postRollVideoEnded) _videoWindow?.EnterPostRollPreview();
    }

    private int WatchBackdropWidth
    {
        get
        {
            try
            {
                var window = _videoWindow?.Hwnd ?? (App.MainWindowInstance is { } main ? WinRT.Interop.WindowNative.GetWindowHandle(main) : IntPtr.Zero);
                if (window != IntPtr.Zero) return Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(window), Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).OuterBounds.Width;
            }
            catch { /* A prefetch can precede the first native window. */ }
            return 1920;
        }
    }
    private readonly StillWatchingSession _stillWatching = new();
    private readonly object _loadingCoverGate = new();
    private LoadingCoverOwner? _loadingCover;
    private long _loadingCoverGeneration;
    // Each step rewrites a full-frame bitmap of up to 8MB while the stream is starting.
    private const int LoadingCoverFadeSteps = 5;

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

    // PlaybackEndTime.LabelKey with argument "time" left as a placeholder; the
    // OSC substitutes "{time}" with its live clock text.
    private static string EndTimeLabelTemplate()
    {
        var template = AppStrings.Get(PlaybackEndTime.LabelKey,
            new Dictionary<string, object?> { ["time"] = "{time}" }, fallback: PlaybackEndTime.FallbackLabelTemplate);
        return template.Contains("{time}", StringComparison.Ordinal) ? template : PlaybackEndTime.FallbackLabelTemplate;
    }

    // Interface text for playback chrome; raw server metadata never enters it.
    private static string HudText(string key, string fallback) => AppStrings.Get(key, fallback: fallback);

    private void PublishPlaybackHudPreferences(SiloPlayer.Player.MpvPlayer mpv)
    {
        // The explicit profile clock wins; "auto" follows the display culture.
        string clock;
        try { clock = PlaybackEndTime.ResolveClock(_settingsService.Load().UiTimeFormat, AppStrings.FormatCulture); }
        catch { clock = PlaybackEndTime.ResolveClock(null, AppStrings.FormatCulture); }
        PublishOscLocalization(mpv);
        mpv.SendScriptMessage("osc-set-end-time-format", clock, EndTimeLabelTemplate());
        mpv.SendScriptMessage("osc-set-hud-wait-labels",
            HudText("web.player.connection.reconnecting", "Reconnecting…"), HudText("web.player.room.catchingUp", "Catching up to the party"));
        mpv.SendScriptMessage("osc-set-autoplay-gate", IsStillWatchingGated ? "true" : "false");
    }

    /// <summary>Owner, backdrop URL and thumbhash for the shell's plan-read cover.</summary>
    public event Action<PlaybackLoadingBackdrop>? LoadingBackdropChanged;

    // One cached file serves the loading cover and the ended screen's finishing
    // slot: the picture fetched when playback began needs no request at its end.
    internal const string WatchBackdropImageType = "backdrop";
    internal static string WatchBackdropCacheId(string contentId) => $"loading_{contentId}";

    private sealed class LoadingCoverOwner(string key, string contentId, CancellationTokenSource cts)
    {
        public string Key { get; } = key;
        public string ContentId { get; } = contentId;
        public CancellationTokenSource Cts { get; } = cts;
        public byte[]? Pixels;
        public int Width, Height, Stride;
        public int FrameWidth = LoadingCoverBitmap.MaximumWidth, FrameHeight = LoadingCoverBitmap.MaximumHeight;
        public readonly List<string> Files = [];
        public readonly SemaphoreSlim ComposeGate = new(1, 1);
        public int Revision, PublishedFrames;
        public bool FadeImage;
        public byte[]? Composed;
        public int ComposedWidth, ComposedHeight;
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
                .GetImageDiskPathAsync(WatchBackdropCacheId(owner.ContentId), WatchBackdropImageType, url, http, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path)) return;
            token.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var scale = Math.Min(1d, Math.Min(LoadingCoverBitmap.MaximumWidth / (double)Math.Max(1, decoder.OrientedPixelWidth),
                LoadingCoverBitmap.MaximumHeight / (double)Math.Max(1, decoder.OrientedPixelHeight)));
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
            await PublishLoadingCoverAsync(owner, data.DetachPixelData(), width, height, width * 4, token, fade: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Loading cover backdrop unavailable: {ex.Message}");
        }
    }

    private async Task PublishLoadingCoverAsync(LoadingCoverOwner owner, byte[] pixels, int width, int height, int stride,
        CancellationToken token, bool fade = false)
    {
        int frameWidth, frameHeight;
        lock (_loadingCoverGate)
        {
            if (!ReferenceEquals(_loadingCover, owner)) return;
            owner.FadeImage = fade;
            owner.Pixels = pixels; owner.Width = width; owner.Height = height; owner.Stride = stride;
            frameWidth = owner.FrameWidth; frameHeight = owner.FrameHeight;
        }
        await ComposeLoadingCoverAsync(owner, frameWidth, frameHeight, token).ConfigureAwait(false);
    }

    private async Task ComposeLoadingCoverAsync(LoadingCoverOwner owner, int frameWidth, int frameHeight, CancellationToken token)
    {
        var revision = Interlocked.Increment(ref owner.Revision);
        await owner.ComposeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            byte[] source; int width, height, stride; bool fade;
            bool Current() => !token.IsCancellationRequested && ReferenceEquals(_loadingCover, owner) && revision == Volatile.Read(ref owner.Revision);
            lock (_loadingCoverGate)
            {
                if (!Current() || owner.Pixels == null) return;
                source = owner.Pixels; width = owner.Width; height = owner.Height; stride = owner.Stride; fade = owner.FadeImage;
            }
            var (targetWidth, targetHeight) = LoadingCoverBitmap.TargetSize(frameWidth, frameHeight);
            if (targetWidth <= 0) return;
            var layoutScale = (frameWidth <= 1920 ? 1d : Math.Sqrt(frameWidth / 1920d)) * targetWidth / frameWidth;
            var composed = await Task.Run(() => LoadingCoverBitmap.Compose(source, width, height, stride, targetWidth, targetHeight, layoutScale), token).ConfigureAwait(false);
            if (!Current()) return;
            var directory = PlaybackArtworkDirectory("LoadingCover");
            Directory.CreateDirectory(directory);
            lock (_loadingCoverGate)
            {
                if (!Current()) return;
                if (owner.Files.Count == 0) for (var i = 0; i < 2; i++) owner.Files.Add(Path.Combine(directory, $"{Guid.NewGuid():N}-{i}.bgra"));
            }
            // The picture fades in once, from the frame already shown at this size
            // (or from nothing). A resize, and any recompose after the fade, writes
            // the one final frame instead of dropping the cover and fading again.
            var previous = owner.ComposedWidth == targetWidth && owner.ComposedHeight == targetHeight ? owner.Composed : null;
            if (previous == null && owner.PublishedFrames > 0) fade = false;
            // A few full-frame writes over the 500ms transition, blended into a
            // single scratch buffer. Only two raw files are reused; neither frame
            // arrays nor files accumulate per step.
            byte[]? scratch = null;
            if (fade)
            {
                previous ??= new byte[composed.Length];
                scratch = new byte[composed.Length];
            }
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var step = fade ? 1 : LoadingCoverFadeSteps; step <= LoadingCoverFadeSteps; step++)
            {
                if (fade)
                {
                    // The frame on screen is step zero; each later step keeps its place in the 500ms.
                    var wait = TimeSpan.FromMilliseconds(500d * step / LoadingCoverFadeSteps) - System.Diagnostics.Stopwatch.GetElapsedTime(started);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
                }
                if (!Current()) return;
                var frame = composed;
                if (step < LoadingCoverFadeSteps)
                {
                    LoadingCoverBitmap.BlendFrame(previous!, composed, scratch!, step / (double)LoadingCoverFadeSteps);
                    frame = scratch!;
                }
                var serial = ++owner.PublishedFrames;
                var path = owner.Files[serial % 2];
                await File.WriteAllBytesAsync(path, frame, token).ConfigureAwait(false);
                lock (_loadingCoverGate)
                {
                    if (!Current() || _mpv == null) return;
                    _mpv.SendScriptMessage("osc-set-loading-backdrop", owner.Key, path,
                        targetWidth.ToString(CultureInfo.InvariantCulture), targetHeight.ToString(CultureInfo.InvariantCulture), (targetWidth * 4).ToString(CultureInfo.InvariantCulture),
                        frameWidth.ToString(CultureInfo.InvariantCulture), frameHeight.ToString(CultureInfo.InvariantCulture), serial.ToString(CultureInfo.InvariantCulture));
                }
            }
            lock (_loadingCoverGate)
            {
                if (Current())
                {
                    owner.Composed = composed; owner.ComposedWidth = targetWidth; owner.ComposedHeight = targetHeight;
                    // This picture is in; only a newer one fades again.
                    if (ReferenceEquals(owner.Pixels, source)) owner.FadeImage = false;
                }
            }
        }
        finally { owner.ComposeGate.Release(); }
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
        lock (_loadingCoverGate) { files = owner.Files.ToArray(); owner.Pixels = null; owner.Composed = null; }
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

    private static string PlaybackArtworkDirectory(string kind) => Path.Combine(
        Environment.GetEnvironmentVariable(LocalLog.LogDirectoryEnvironmentVariable) is { Length: > 0 } fixture
            ? fixture : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer"), kind);
}

/// <summary>The backdrop a playback request loads behind; <see cref="Owner"/> is its loading surface owner.</summary>
public sealed record PlaybackLoadingBackdrop(object Owner, string? Url, string? Thumbhash);
