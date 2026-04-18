using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class ImageService : IDisposable
{
    private readonly string _diskCacheDir;
    private readonly long _maxMemoryBytes;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private readonly ConcurrentQueue<string> _evictionOrder = new();
    private long _currentMemoryBytes;
    private readonly SemaphoreSlim _downloadLock = new(10);
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _inflightDownloads = new();

    public ImageService(string diskCacheDir, long maxMemoryCacheBytes = 200 * 1024 * 1024)
    {
        _diskCacheDir = diskCacheDir;
        _maxMemoryBytes = maxMemoryCacheBytes;
        Directory.CreateDirectory(diskCacheDir);
    }

    public Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = $"{contentId}_{imageType}";

        // 1. Memory cache hit — synchronous fast path. No I/O, safe to run
        //    from any thread including the UI thread.
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return Task.FromResult<byte[]?>(cached);

        // 2/3. Disk or network path — ALL sync I/O (File.Exists, disk reads,
        //      disk writes) is done inside a Task.Run so the UI thread never
        //      blocks on the filesystem. Under contention (many downloads
        //      completing concurrently), File.Exists alone can take 50-100ms
        //      per call; times N visible cards that's a multi-second freeze.
        return Task.Run(async () =>
        {
            var diskPath = GetDiskPath(cacheKey);
            if (File.Exists(diskPath))
            {
                var diskBytes = await File.ReadAllBytesAsync(diskPath, ct).ConfigureAwait(false);
                AddToMemoryCache(cacheKey, diskBytes);
                return (byte[]?)diskBytes;
            }

            var task = _inflightDownloads.GetOrAdd(cacheKey, _ => DownloadAndCacheAsync(cacheKey, diskPath, url, http, ct));
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                _inflightDownloads.TryRemove(cacheKey, out _);
            }
        }, ct);
    }

    /// <summary>
    /// Ensures the image for the given cache key exists on disk (downloading
    /// if necessary) and returns the local path. Lets callers point
    /// <c>BitmapImage.UriSource</c> at a file:// URI, which is significantly
    /// faster than <c>SetSourceAsync</c> on a <c>MemoryStream</c> because
    /// WinUI's native decoder does the work off the UI thread without the
    /// COM interop overhead of <c>AsRandomAccessStream</c>.
    /// Returns null when download fails.
    /// </summary>
    public Task<string?> GetImageDiskPathAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = $"{contentId}_{imageType}";

        // All sync I/O (File.Exists) goes on the thread pool so the UI thread
        // never blocks on disk ops. Fast-path: if download is already inflight,
        // reuse its Task.
        return Task.Run(async () =>
        {
            var diskPath = GetDiskPath(cacheKey);
            if (File.Exists(diskPath)) return (string?)diskPath;

            var task = _inflightDownloads.GetOrAdd(cacheKey, _ => DownloadAndCacheAsync(cacheKey, diskPath, url, http, ct));
            try
            {
                var bytes = await task.ConfigureAwait(false);
                return bytes != null ? diskPath : null;
            }
            finally
            {
                _inflightDownloads.TryRemove(cacheKey, out _);
            }
        }, ct);
    }

    private async Task<byte[]?> DownloadAndCacheAsync(string cacheKey, string diskPath, string url, HttpClient http, CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_memoryCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(diskPath, bytes, ct).ConfigureAwait(false);
            AddToMemoryCache(cacheKey, bytes);
            return bytes;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public void ClearMemoryCache()
    {
        _memoryCache.Clear();
        while (_evictionOrder.TryDequeue(out _)) { }
        _currentMemoryBytes = 0;
    }

    private void AddToMemoryCache(string key, byte[] data)
    {
        if (data.Length > _maxMemoryBytes) return;

        while (Interlocked.Read(ref _currentMemoryBytes) + data.Length > _maxMemoryBytes)
        {
            if (!_evictionOrder.TryDequeue(out var oldKey)) break;
            if (_memoryCache.TryRemove(oldKey, out var removed))
                Interlocked.Add(ref _currentMemoryBytes, -removed.Length);
        }

        if (_memoryCache.TryAdd(key, data))
        {
            Interlocked.Add(ref _currentMemoryBytes, data.Length);
            _evictionOrder.Enqueue(key);
        }
        // If key already exists, don't enqueue again (prevents counter drift on eviction)
    }

    private string GetDiskPath(string cacheKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(_diskCacheDir, $"{hash}.img");
    }

    public void Dispose()
    {
        _downloadLock.Dispose();
        _memoryCache.Clear();
    }
}
