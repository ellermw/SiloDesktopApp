using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class ImageService : IDisposable
{
    private readonly string _diskCacheDir;
    private readonly long _maxMemoryBytes;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private long _currentMemoryBytes;
    private readonly SemaphoreSlim _downloadLock = new(10);

    public ImageService(string diskCacheDir, long maxMemoryCacheBytes = 200 * 1024 * 1024)
    {
        _diskCacheDir = diskCacheDir;
        _maxMemoryBytes = maxMemoryCacheBytes;
        Directory.CreateDirectory(diskCacheDir);
    }

    public async Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = $"{contentId}_{imageType}";

        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var diskPath = GetDiskPath(cacheKey);
        if (File.Exists(diskPath))
        {
            var diskBytes = await File.ReadAllBytesAsync(diskPath, ct);
            AddToMemoryCache(cacheKey, diskBytes);
            return diskBytes;
        }

        await _downloadLock.WaitAsync(ct);
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cached))
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
        _currentMemoryBytes = 0;
    }

    private void AddToMemoryCache(string key, byte[] data)
    {
        if (data.Length > _maxMemoryBytes) return;
        while (Interlocked.Read(ref _currentMemoryBytes) + data.Length > _maxMemoryBytes && !_memoryCache.IsEmpty)
        {
            var firstKey = _memoryCache.Keys.FirstOrDefault();
            if (firstKey != null && _memoryCache.TryRemove(firstKey, out var removed))
                Interlocked.Add(ref _currentMemoryBytes, -removed.Length);
        }
        if (_memoryCache.TryAdd(key, data))
            Interlocked.Add(ref _currentMemoryBytes, data.Length);
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
