using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using Windows.Graphics.Imaging;

namespace SiloPlayer.Services;

/// <summary>
/// Seek-bar previews (trickplay) for the file being played, as official silo-server
/// 3b2b32168 WatchPage/SeekBar: the manifest is read once per played file, only when its
/// version reports <c>trickplay_available</c>, and read again before its sheet URLs expire.
/// The OSC reports the sheet under the pointer; this owner downloads that sheet and the
/// next, decodes each to one premultiplied BGRA file the OSC maps tile by tile with
/// <c>overlay-add</c> offset and stride, and keeps a small LRU of them. Files are deleted
/// when the file's playback ends, the player closes, or the app exits.
/// </summary>
public partial class PlayerService
{
    private readonly object _seekPreviewGate = new();
    private SeekPreviewOwner? _seekPreview;
    private long _seekPreviewGeneration;
    private static int _seekPreviewSweepStarted;
    private long _seekPreviewFocusSequence;

    /// <summary>Starts, keeps or retires the previews for the file that just loaded.</summary>
    private void SyncSeekPreviews()
    {
        var mpv = _mpv;
        var authority = _activeAuthority;
        var version = ActiveVersion;
        var contentId = ContentId;
        var sessionId = _playbackManager?.SessionId;
        var wanted = mpv != null && !_closing && !IsAudiobook && version is { TrickplayAvailable: true, FileId: > 0 }
            && !string.IsNullOrWhiteSpace(contentId);
        lock (_seekPreviewGate)
        {
            var current = _seekPreview;
            if (wanted && current != null && current.Matches(mpv!, authority, contentId!, version!.FileId, sessionId))
            {
                // A transport reload of the same file (remux/transcode replan) keeps its previews.
                current.Republish();
                return;
            }
            RetireSeekPreviewsLocked();
            if (!wanted)
            {
                mpv?.SendScriptMessage("osc-set-seek-preview", "");
                return;
            }
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_playbackCts?.Token ?? CancellationToken.None, _authorityLifetimeCts.Token);
            var owner = new SeekPreviewOwner(mpv!, authority, contentId!, version!.FileId, sessionId,
                authority.ApiClient.CaptureIdentityContext(), cts, Interlocked.Increment(ref _seekPreviewGeneration), SeekPreviewDirectory());
            _seekPreview = owner;
            _ = Task.Run(owner.RunAsync);
        }
    }

    /// <summary>The OSC moved the pointer onto another sheet, or off the bar (-1).</summary>
    private void OnSeekPreviewFocus(string key, string sheet)
    {
        if (!int.TryParse(sheet, NumberStyles.Integer, CultureInfo.InvariantCulture, out var focus)) return;
        SeekPreviewOwner? owner;
        lock (_seekPreviewGate) owner = _seekPreview;
        if (owner == null) return;
        // Off the mpv event thread; the sequence keeps a late older report from winning.
        var sequence = Interlocked.Increment(ref _seekPreviewFocusSequence);
        _ = Task.Run(() => owner.Focus(key, focus, sequence));
    }

    private void RetireSeekPreviews()
    {
        lock (_seekPreviewGate) RetireSeekPreviewsLocked();
    }

    private void RetireSeekPreviewsLocked()
    {
        var owner = _seekPreview;
        _seekPreview = null;
        owner?.Retire(ReferenceEquals(owner.Mpv, _mpv));
    }

    private static string SeekPreviewDirectory()
    {
        var root = PlaybackArtworkDirectory("SeekPreviews");
        var directory = Path.Combine(root, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        // A previous process that exited without its cleanup (crash, power loss) left its
        // sheets behind. Folders still mapped by a live instance refuse deletion.
        if (Interlocked.Exchange(ref _seekPreviewSweepStarted, 1) == 0)
            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var stale in Directory.Exists(root) ? Directory.GetDirectories(root) : [])
                        if (!string.Equals(stale, directory, StringComparison.OrdinalIgnoreCase))
                            try { Directory.Delete(stale, recursive: true); } catch { /* in use or gone */ }
                }
                catch { /* best effort */ }
            });
        return directory;
    }

    private sealed class SeekPreviewOwner(
        MpvPlayer mpv,
        PlaybackAuthorityScope authority,
        string contentId,
        int fileId,
        string? sessionId,
        ApiIdentityContext identity,
        CancellationTokenSource cts,
        long generation,
        string directory)
    {
        /// <summary>A JPEG sheet larger than this is refused before decoding.</summary>
        private const long MaxSheetDownloadBytes = 32L * 1024 * 1024;

        internal MpvPlayer Mpv { get; } = mpv;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
        private readonly SeekPreviewSheetCache _cache = new();
        private readonly HashSet<int> _failed = [];
        private readonly Dictionary<int, CancellationTokenSource> _loading = [];
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private SeekPreviewManifest? _manifest;
        private string _key = "";
        private int _sheetsVersion;
        private int _focus = -1;
        private long _focusSequence;
        private DateTimeOffset? _lastRead;
        private bool _lastReadFailed;
        private DateTimeOffset? _readAt;
        private bool _retired;

        internal bool Matches(MpvPlayer player, PlaybackAuthorityScope scope, string content, int file, string? session)
            => ReferenceEquals(player, Mpv) && ReferenceEquals(scope, authority) && content == contentId && file == fileId &&
               session == sessionId && authority.ApiClient.IsCurrentIdentityContext(identity) && !cts.IsCancellationRequested;

        private bool Current => !_retired && !cts.IsCancellationRequested && authority.ApiClient.IsCurrentIdentityContext(identity);

        internal async Task RunAsync()
        {
            var token = cts.Token;
            var attempt = 0;
            while (!token.IsCancellationRequested)
            {
                TimeSpan? delay;
                try
                {
                    lock (_gate) if (!Current) return;
                    var context = authority.ApiClient.CaptureContext();
                    var response = await authority.PlaybackApi.GetWatchTrickplayAsync(contentId, fileId, context, token).ConfigureAwait(false);
                    var manifest = SeekPreviewManifest.FromResponse(response, fileId, authority.ApiClient.ResolveServerUrl);
                    lock (_gate)
                    {
                        // A reply for a retired file, session, profile or account is dropped.
                        if (!Current) return;
                        if (manifest == null)
                        {
                            LogToFile("state_trace.txt", "Seek previews: unusable manifest; previews withdrawn.");
                            WithdrawLocked();
                            return;
                        }
                        AdoptLocked(manifest);
                    }
                    attempt = 0;
                    delay = SeekPreviewRefreshPolicy.AfterSuccess(manifest.ExpiresAt, DateTimeOffset.UtcNow);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    var retry = SeekPreviewRefreshPolicy.AfterFailure(attempt, ex);
                    LogToFile("state_trace.txt", $"Seek previews read failed ({Describe(ex)}); {(retry == null ? "withdrawn" : "retrying")}.");
                    lock (_gate)
                    {
                        if (!Current) return;
                        _lastReadFailed = true;
                        // A terminal answer withdraws the previews; a transient one keeps them.
                        if (retry == null) { WithdrawLocked(); return; }
                    }
                    attempt = retry.StartsNewRun ? 0 : attempt + 1;
                    delay = retry.Delay;
                }
                try { await WaitAsync(delay, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>Sleeps until the next scheduled read, or sooner when a sheet asks for fresh URLs.</summary>
        private async Task WaitAsync(TimeSpan? delay, CancellationToken token)
        {
            var until = delay is { } d ? DateTimeOffset.UtcNow + d : DateTimeOffset.MaxValue;
            while (true)
            {
                DateTimeOffset due;
                lock (_gate)
                {
                    due = _readAt is { } forced && forced < until ? forced : until;
                    if (DateTimeOffset.UtcNow >= due) { _readAt = null; return; }
                }
                var wait = due == DateTimeOffset.MaxValue ? Timeout.InfiniteTimeSpan
                    : TimeSpan.FromMilliseconds(Math.Clamp((due - DateTimeOffset.UtcNow).TotalMilliseconds, 1, int.MaxValue - 1));
                await _wake.WaitAsync(wait, token).ConfigureAwait(false);
            }
        }

        private void AdoptLocked(SeekPreviewManifest manifest)
        {
            _lastRead = DateTimeOffset.UtcNow;
            _lastReadFailed = false;
            _readAt = null;
            var same = manifest.SameSheetsAs(_manifest);
            _manifest = manifest;
            // Fresh URLs retry sheets that failed with the old ones.
            _failed.Clear();
            if (!same)
            {
                CancelLoadsLocked(null);
                DeleteLater(_cache.Clear());
                _key = $"{generation}.{++_sheetsVersion}";
            }
            PublishLocked();
        }

        internal void Republish()
        {
            lock (_gate) if (Current && _manifest != null) PublishLocked();
        }

        private void PublishLocked()
        {
            var manifest = _manifest!;
            // Never URLs: the OSC needs the grid and the sheet count only.
            Mpv.SendScriptMessage("osc-set-seek-preview", System.Text.Json.JsonSerializer.Serialize(new
            {
                key = _key,
                interval_ms = manifest.IntervalMs,
                width = manifest.Width,
                height = manifest.Height,
                columns = manifest.Columns,
                rows = manifest.Rows,
                count = manifest.Count,
                sheets = manifest.Sheets.Count,
            }));
        }

        private void WithdrawLocked()
        {
            _manifest = null;
            CancelLoadsLocked(null);
            DeleteLater(_cache.Clear());
            _key = "";
            Mpv.SendScriptMessage("osc-set-seek-preview", "");
        }

        /// <summary>Asks for the manifest again, at most once a minute (official handleTrickplayError).</summary>
        private void RequestReadLocked()
        {
            if (_lastReadFailed) return; // The read loop already owns retry and polling delays.
            var at = DateTimeOffset.UtcNow + SeekPreviewRefreshPolicy.AfterSheetError(_lastRead, DateTimeOffset.UtcNow);
            if (_readAt is { } pending && pending <= at) return;
            _readAt = at;
            _wake.Release();
        }

        internal void Focus(string key, int focus, long sequence)
        {
            var start = new List<(int Sheet, string Url, CancellationTokenSource Load)>();
            lock (_gate)
            {
                if (!Current || _manifest is not { } manifest || key != _key || sequence < _focusSequence) return;
                _focusSequence = sequence;
                _focus = focus;
                var wanted = SeekPreviewSheetCache.Wanted(focus, manifest.Sheets.Count);
                CancelLoadsLocked(wanted);
                if (wanted.Count == 0) return;
                if (manifest.IsExpired(DateTimeOffset.UtcNow))
                {
                    // Expired URLs cannot load; the OSC asks again for the fresh manifest.
                    RequestReadLocked();
                    return;
                }
                for (var i = wanted.Count - 1; i >= 0; i--)
                {
                    var sheet = wanted[i];
                    if (_cache.TryGet(sheet, out var path)) { SendSheetLocked(sheet, "ready", path); continue; }
                    if (_failed.Contains(sheet) || _loading.ContainsKey(sheet)) continue;
                    if (string.IsNullOrEmpty(manifest.Sheets[sheet])) { _failed.Add(sheet); SendSheetLocked(sheet, "failed", ""); continue; }
                    var load = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    _loading[sheet] = load;
                    start.Add((sheet, manifest.Sheets[sheet], load));
                }
            }
            foreach (var (sheet, url, load) in start)
                _ = Task.Run(() => LoadSheetAsync(sheet, url, load));
        }

        private void CancelLoadsLocked(IReadOnlyList<int>? keep)
        {
            foreach (var (sheet, load) in _loading.ToArray())
            {
                if (keep?.Contains(sheet) == true) continue;
                _loading.Remove(sheet);
                load.Cancel();
            }
        }

        private async Task LoadSheetAsync(int sheet, string url, CancellationTokenSource load)
        {
            var token = load.Token;
            string? rawPath = null;
            SeekPreviewManifest? manifest;
            lock (_gate) manifest = _manifest;
            try
            {
                if (manifest == null) return;
                var (tileWidth, tileHeight) = SeekPreviewSheetCache.DecodedTileSize(manifest.Width, manifest.Height);
                var sheetWidth = checked(tileWidth * manifest.Columns);
                var sheetHeight = checked(tileHeight * manifest.Rows);
                var stride = checked(sheetWidth * 4);
                var http = App.Services.GetRequiredService<HttpClient>();
                byte[] bytes;
                // The URL carries its own authorization; it is never logged.
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException(null, null, response.StatusCode);
                    if (response.Content.Headers.ContentLength > MaxSheetDownloadBytes) throw new InvalidDataException("Sheet too large.");
                    bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                }
                if (bytes.Length > MaxSheetDownloadBytes) throw new InvalidDataException("Sheet too large.");
                token.ThrowIfCancellationRequested();
                byte[] pixels;
                using (var stream = new MemoryStream(bytes).AsRandomAccessStream())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    // Scale the whole sheet so every cell is exactly one decoded tile.
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)sheetWidth, ScaledHeight = (uint)sheetHeight,
                        InterpolationMode = BitmapInterpolationMode.Fant,
                    };
                    var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
                    pixels = data.DetachPixelData();
                }
                if (pixels.Length < (long)stride * sheetHeight) throw new InvalidDataException("Sheet decode was short.");
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(directory);
                rawPath = Path.Combine(directory, $"{generation}-{sheet}-{Guid.NewGuid():N}.bgra");
                // One spare row: overlay-add maps offset + h*stride bytes, which for a tile in the
                // sheet's last row but not its first column runs past the pixels.
                await using (var file = new FileStream(rawPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16, FileOptions.Asynchronous))
                {
                    await file.WriteAsync(pixels.AsMemory(0, stride * sheetHeight), token).ConfigureAwait(false);
                    await file.WriteAsync(new byte[stride], token).ConfigureAwait(false);
                }
                lock (_gate)
                {
                    if (!Current || !_loading.TryGetValue(sheet, out var owner) || !ReferenceEquals(owner, load) || !ReferenceEquals(manifest, _manifest)
                        && !manifest.SameSheetsAs(_manifest)) return;
                    _loading.Remove(sheet);
                    _files.Add(rawPath);
                    var evicted = _cache.Add(sheet, (long)stride * (sheetHeight + 1), rawPath, SeekPreviewSheetCache.Wanted(_focus, manifest.Sheets.Count));
                    SendSheetLocked(sheet, "ready", rawPath, tileWidth, tileHeight, stride);
                    rawPath = null;
                    foreach (var (dropped, path) in evicted)
                    {
                        SendSheetLocked(dropped, "evicted", "");
                        DeleteLater([path]);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                LogToFile("state_trace.txt", $"Seek preview sheet {sheet} unavailable ({Describe(ex)}).");
                lock (_gate)
                {
                    if (Current && _loading.TryGetValue(sheet, out var owner) && ReferenceEquals(owner, load))
                    {
                        _loading.Remove(sheet);
                        _failed.Add(sheet);
                        SendSheetLocked(sheet, "failed", "");
                        // Most often an expired URL: read the previews again.
                        RequestReadLocked();
                    }
                }
            }
            finally
            {
                if (rawPath != null) TryDelete(rawPath);
                load.Dispose();
            }
        }

        private void SendSheetLocked(int sheet, string status, string path, int tileWidth = 0, int tileHeight = 0, int stride = 0)
        {
            if (status == "ready" && tileWidth == 0 && _manifest is { } manifest)
            {
                (tileWidth, tileHeight) = SeekPreviewSheetCache.DecodedTileSize(manifest.Width, manifest.Height);
                stride = tileWidth * manifest.Columns * 4;
            }
            Mpv.SendScriptMessage("osc-set-seek-preview-sheet", _key, sheet.ToString(CultureInfo.InvariantCulture), status, path,
                tileWidth.ToString(CultureInfo.InvariantCulture), tileHeight.ToString(CultureInfo.InvariantCulture),
                stride.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Stops every read and load, clears the OSC and deletes the files.</summary>
        internal void Retire(bool clearOsc)
        {
            lock (_gate)
            {
                if (_retired) return;
                _retired = true;
                CancelLoadsLocked(null);
                _cache.Clear();
                if (clearOsc) Mpv.SendScriptMessage("osc-set-seek-preview", "");
                DeleteLater([.. _files]);
            }
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
            {
                cts.Dispose();
                // Anything mpv still mapped at the first attempt; the folder goes with the last file.
                DeleteNow();
                try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
                catch { /* another owner is using it */ }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Deletes after the OSC has had time to remove an overlay that maps the file:
        /// Windows refuses to delete a mapped file. A refused file is retried at retirement.
        /// </summary>
        private void DeleteLater(IReadOnlyCollection<string> paths)
        {
            if (paths.Count == 0) return;
            _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
            {
                foreach (var path in paths)
                {
                    try { File.Delete(path); lock (_gate) _files.Remove(path); }
                    catch { /* still mapped; retried at retirement */ }
                }
            }, TaskScheduler.Default);
        }

        private void DeleteNow()
        {
            string[] paths;
            lock (_gate) paths = [.. _files];
            foreach (var path in paths)
            {
                try { File.Delete(path); lock (_gate) _files.Remove(path); }
                catch { /* the process-start sweep removes it */ }
            }
        }

        private static string Describe(Exception ex) => ex switch
        {
            ApiException api => "HTTP " + api.StatusCode.ToString(CultureInfo.InvariantCulture),
            HttpRequestException { StatusCode: { } status } => "HTTP " + ((int)status).ToString(CultureInfo.InvariantCulture),
            _ => ex.GetType().Name,
        };
    }
}
