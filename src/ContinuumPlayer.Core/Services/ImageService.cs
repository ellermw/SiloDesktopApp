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

    public async Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = $"{contentId}_{imageType}";

        // 1. Memory cache hit
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // 2. Disk cache hit
        var diskPath = GetDiskPath(cacheKey);
        if (File.Exists(diskPath))
        {
            var diskBytes = await File.ReadAllBytesAsync(diskPath, ct);
            AddToMemoryCache(cacheKey, diskBytes);
            return diskBytes;
        }

        // 3. Download with deduplication
        var task = _inflightDownloads.GetOrAdd(cacheKey, _ => DownloadAndCacheAsync(cacheKey, diskPath, url, http, ct));
        try
        {
            return await task;
        }
        finally
        {
            _inflightDownloads.TryRemove(cacheKey, out _);
        }
    }

    private async Task<byte[]?> DownloadAndCacheAsync(string cacheKey, string diskPath, string url, HttpClient http, CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_memoryCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(diskPath, bytes, ct);
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
