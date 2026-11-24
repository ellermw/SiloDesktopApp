using System.Globalization;
using System.Net.Http;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Seek-bar previews (trickplay) of the file being played: sprite sheets of thumbnails,
/// one per <see cref="IntervalMs"/> of media time. Thumbnail i shows what plays from
/// i*IntervalMs to (i+1)*IntervalMs and sits on sheet floor(i / (Columns*Rows)), left to
/// right and top to bottom; every sheet has the full grid. Ported from official
/// silo-server 3b2b32168 <c>web/src/player/trickplay.ts</c> and <c>api/v2/trickplay.ts</c>.
/// </summary>
public sealed record SeekPreviewManifest(
    int FileId,
    long IntervalMs,
    int Width,
    int Height,
    int Columns,
    int Rows,
    int Count,
    IReadOnlyList<string> Sheets,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>Bounds that keep one decoded sheet a sane size; a manifest outside them is unusable.</summary>
    public const int MaxThumbnailEdge = 4096;
    public const int MaxGridCells = 10_000;
    /// <summary>One decoded sheet (BGRA, with its spare row) takes at most the cache's byte budget.</summary>
    public const long MaxDecodedSheetBytes = SeekPreviewSheetCache.DefaultMaxBytes;

    public int PerSheet => Columns * Rows;

    /// <summary>The bytes of one decoded sheet file: every tile at its decoded size, BGRA, plus one spare row.</summary>
    public static long DecodedSheetBytes(int width, int height, int columns, int rows)
    {
        var (tileWidth, tileHeight) = SeekPreviewSheetCache.DecodedTileSize(width, height);
        return (long)tileWidth * columns * 4 * ((long)tileHeight * rows + 1);
    }

    /// <summary>
    /// Adapts a v2 manifest (sheets sorted by index, as official <c>trickplayFromV2</c>).
    /// Relative sheet URLs are resolved with <paramref name="resolveUrl"/>. Returns null for
    /// a manifest of another file or with an unusable grid.
    /// </summary>
    public static SeekPreviewManifest? FromResponse(WatchTrickplayResponse? response, int fileId, Func<string?, string?>? resolveUrl = null)
    {
        if (response == null) return null;
        if (!string.IsNullOrEmpty(response.FileId) && response.FileId != fileId.ToString(CultureInfo.InvariantCulture)) return null;
        if (response.IntervalMs <= 0 || response.ThumbnailCount <= 0) return null;
        if (response.ThumbnailWidth is <= 0 or > MaxThumbnailEdge || response.ThumbnailHeight is <= 0 or > MaxThumbnailEdge) return null;
        // Each side is bounded first, so the product cannot overflow.
        if (response.TileColumns is <= 0 or > MaxGridCells || response.TileRows is <= 0 or > MaxGridCells
            || response.TileColumns * response.TileRows > MaxGridCells) return null;
        if (response.ThumbnailCount > int.MaxValue) return null;
        if (DecodedSheetBytes((int)response.ThumbnailWidth, (int)response.ThumbnailHeight, (int)response.TileColumns, (int)response.TileRows)
            > MaxDecodedSheetBytes) return null;
        var sheets = new List<string>();
        foreach (var sheet in (response.Sheets ?? []).OrderBy(sheet => sheet.Index))
        {
            var url = resolveUrl == null ? sheet.Url : resolveUrl(sheet.Url);
            // A gap leaves the later thumbnails without a sheet; trickplayTile then shows none.
            sheets.Add(string.IsNullOrWhiteSpace(url) ? "" : url);
        }
        DateTimeOffset? expires = DateTimeOffset.TryParse(response.ExpiresAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        return new(fileId, response.IntervalMs, (int)response.ThumbnailWidth, (int)response.ThumbnailHeight,
            (int)response.TileColumns, (int)response.TileRows, (int)response.ThumbnailCount, sheets, expires);
    }

    /// <summary>The thumbnail shown for a media time, or null outside the previews (official <c>trickplayTile</c>).</summary>
    public SeekPreviewTile? TileAt(double seconds)
    {
        if (!double.IsFinite(seconds) || Count <= 0 || IntervalMs <= 0) return null;
        var raw = Math.Floor(seconds * 1000 / IntervalMs);
        var index = (int)Math.Min(Math.Max(0, raw), Count - 1);
        var sheet = index / PerSheet;
        if (sheet >= Sheets.Count || string.IsNullOrEmpty(Sheets[sheet])) return null;
        var cell = index % PerSheet;
        return new(index, sheet, Sheets[sheet], cell % Columns * Width, cell / Columns * Height);
    }

    /// <summary>Sheet URLs no longer work at or after <see cref="ExpiresAt"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expires && now >= expires;

    /// <summary>
    /// Two manifests whose decoded sheets are interchangeable: the same file and grid. A
    /// refresh that only renews the URLs keeps the sheets already decoded.
    /// </summary>
    public bool SameSheetsAs(SeekPreviewManifest? other) => other != null && other.FileId == FileId && other.IntervalMs == IntervalMs &&
        other.Width == Width && other.Height == Height && other.Columns == Columns && other.Rows == Rows &&
        other.Count == Count && other.Sheets.Count == Sheets.Count;
}

/// <summary>A thumbnail: its index, sheet, and pixel offset within the sheet at full size.</summary>
public sealed record SeekPreviewTile(int Index, int Sheet, string Url, int X, int Y);

/// <summary>What to do after a failed manifest read: wait <see cref="Delay"/>, then read again.</summary>
public sealed record SeekPreviewRetry(TimeSpan Delay, bool StartsNewRun);

/// <summary>
/// When the manifest is read again, ported from official <c>useWatchTrickplay</c> and
/// WatchPage's <c>handleTrickplayError</c> at 3b2b32168: two quick retries of a transient
/// failure (1 s then 2 s, or the server's Retry-After when longer), then a poll every
/// 60 s (or Retry-After); a terminal failure stops and withdraws the previews. A good
/// manifest is read again five minutes before its URLs expire, never sooner than 60 s.
/// </summary>
public static class SeekPreviewRefreshPolicy
{
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumRefresh = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ErrorPoll = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SheetErrorThrottle = TimeSpan.FromSeconds(60);
    public const int QuickRetries = 2;

    /// <summary>Official <c>isTransientV2Error</c>: 429, 5xx, no answer, timeouts and refreshes that got no answer.</summary>
    public static bool IsTransient(Exception error) => error switch
    {
        ApiException api => api.StatusCode is 0 or 429 or >= 500,
        HttpRequestException http => http.StatusCode is null || (int)http.StatusCode.Value is 429 or >= 500,
        TimeoutException => true,
        // An HttpClient timeout, or an access refresh that retired the request context.
        OperationCanceledException => true,
        IOException => true,
        _ => false,
    };

    public static TimeSpan? RetryAfter(Exception error) => error is ApiException { RetryAfterSeconds: > 0 } api
        ? TimeSpan.FromSeconds(api.RetryAfterSeconds.Value) : null;

    /// <summary>
    /// The wait after a failed read that was attempt <paramref name="attemptInRun"/> (0-based)
    /// of its run, or null when the failure is terminal.
    /// </summary>
    public static SeekPreviewRetry? AfterFailure(int attemptInRun, Exception error)
    {
        if (!IsTransient(error)) return null;
        var retryAfter = RetryAfter(error) ?? TimeSpan.Zero;
        if (attemptInRun < QuickRetries)
        {
            var backoff = TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, attemptInRun), 30_000));
            return new(Max(backoff, retryAfter), false);
        }
        return new(Max(ErrorPoll, retryAfter), true);
    }

    /// <summary>When to read a good manifest again, or null when it has no usable expiry.</summary>
    public static TimeSpan? AfterSuccess(DateTimeOffset? expiresAt, DateTimeOffset now)
        => expiresAt is { } expires ? Max(MinimumRefresh, expires - now - RefreshMargin) : null;

    /// <summary>
    /// How long a failed sheet waits before the manifest is read again: at once, unless the
    /// last read for this file was under 60 s ago, then at the 60 s mark.
    /// </summary>
    public static TimeSpan AfterSheetError(DateTimeOffset? lastRead, DateTimeOffset now)
        => lastRead is { } last && now - last < SheetErrorThrottle ? SheetErrorThrottle - (now - last) : TimeSpan.Zero;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}

/// <summary>
/// Which decoded sheets the player keeps. Sheets load on demand: the one under the pointer
/// and at most the next one. Decoded sheets are kept least-recently-used within a count and
/// byte budget; the sheets being looked at are never evicted for others.
/// </summary>
public sealed class SeekPreviewSheetCache(int maxSheets = SeekPreviewSheetCache.DefaultMaxSheets, long maxBytes = SeekPreviewSheetCache.DefaultMaxBytes)
{
    public const int DefaultMaxSheets = 3;
    public const long DefaultMaxBytes = 64L * 1024 * 1024;
    /// <summary>Decoded thumbnails are at most this wide; the tooltip draws them 176 to 250 px wide.</summary>
    public const int MaxDecodedTileWidth = 256;

    private readonly LinkedList<(int Sheet, long Bytes, string Path)> _entries = new();

    public int Count => _entries.Count;
    public long Bytes => _entries.Sum(entry => entry.Bytes);
    public IReadOnlyList<int> Sheets => _entries.Select(entry => entry.Sheet).ToArray();

    /// <summary>The sheets to have for a pointer over <paramref name="focus"/>: that sheet and the next.</summary>
    public static IReadOnlyList<int> Wanted(int focus, int sheetCount)
    {
        if (focus < 0 || focus >= sheetCount) return [];
        return focus + 1 < sheetCount ? [focus, focus + 1] : [focus];
    }

    /// <summary>The decoded size of one tile: the thumbnail, scaled down to <see cref="MaxDecodedTileWidth"/>.</summary>
    public static (int Width, int Height) DecodedTileSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        if (width <= MaxDecodedTileWidth) return (width, height);
        return (MaxDecodedTileWidth, Math.Max(1, (int)Math.Round(height * (double)MaxDecodedTileWidth / width)));
    }

    public bool TryGet(int sheet, out string path)
    {
        for (var node = _entries.First; node != null; node = node.Next)
        {
            if (node.Value.Sheet != sheet) continue;
            _entries.Remove(node); _entries.AddFirst(node);
            path = node.Value.Path; return true;
        }
        path = ""; return false;
    }

    public bool Contains(int sheet) => _entries.Any(entry => entry.Sheet == sheet);

    /// <summary>Adds a decoded sheet and returns the paths evicted to stay in budget, never one in <paramref name="keep"/>.</summary>
    public IReadOnlyList<(int Sheet, string Path)> Add(int sheet, long bytes, string path, IReadOnlyCollection<int> keep)
    {
        var evicted = new List<(int, string)>();
        for (var node = _entries.First; node != null; node = node.Next)
        {
            if (node.Value.Sheet != sheet) continue;
            if (node.Value.Path != path) evicted.Add((sheet, node.Value.Path));
            _entries.Remove(node); break;
        }
        _entries.AddFirst((sheet, bytes, path));
        var node2 = _entries.Last;
        while (node2 != null && (_entries.Count > maxSheets || Bytes > maxBytes))
        {
            var previous = node2.Previous;
            if (node2.Value.Sheet != sheet && !keep.Contains(node2.Value.Sheet))
            {
                evicted.Add((node2.Value.Sheet, node2.Value.Path));
                _entries.Remove(node2);
            }
            node2 = previous;
        }
        return evicted;
    }

    /// <summary>Forgets every sheet and returns their paths for deletion.</summary>
    public IReadOnlyList<string> Clear()
    {
        var paths = _entries.Select(entry => entry.Path).ToArray();
        _entries.Clear();
        return paths;
    }
}
