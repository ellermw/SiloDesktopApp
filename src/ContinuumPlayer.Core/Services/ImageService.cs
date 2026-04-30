using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class ImageService : IDisposable
{
    private readonly string _diskCacheDir;
    private readonly long _maxMemoryBytes;
    private readonly long _maxDiskBytes;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private readonly ConcurrentQueue<string> _evictionOrder = new();
    private long _currentMemoryBytes;
    private readonly SemaphoreSlim _downloadLock = new(16);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inflightDownloads = new();
    private static readonly TimeSpan DiskTrimInterval = TimeSpan.FromSeconds(30);
    private long _lastDiskTrimTicks;
    private int _diskTrimQueued;

    public ImageService(
        string diskCacheDir,
        long maxMemoryCacheBytes = 200 * 1024 * 1024,
        long maxDiskCacheBytes = 2L * 1024 * 1024 * 1024)
    {
        _diskCacheDir = diskCacheDir;
        _maxMemoryBytes = maxMemoryCacheBytes;
        _maxDiskBytes = maxDiskCacheBytes;
        Directory.CreateDirectory(diskCacheDir);
    }

    public Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(contentId, imageType, url);

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
                TouchDiskFile(diskPath);
                AddToMemoryCache(cacheKey, diskBytes);
                return (byte[]?)diskBytes;
            }

            var downloadedPath = await GetOrStartDownloadAsync(cacheKey, diskPath, url, http)
                .WaitAsync(ct)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(downloadedPath) || !File.Exists(downloadedPath))
                return null;

            var downloadedBytes = await File.ReadAllBytesAsync(downloadedPath, ct).ConfigureAwait(false);
            TouchDiskFile(downloadedPath);
            AddToMemoryCache(cacheKey, downloadedBytes);
            return downloadedBytes;
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
        var cacheKey = BuildCacheKey(contentId, imageType, url);

        // All sync I/O (File.Exists) goes on the thread pool so the UI thread
        // never blocks on disk ops. Fast-path: if download is already inflight,
        // reuse its Task.
        return Task.Run(async () =>
        {
            var diskPath = GetDiskPath(cacheKey);
            if (File.Exists(diskPath))
            {
                TouchDiskFile(diskPath);
                return (string?)diskPath;
            }

            return await GetOrStartDownloadAsync(cacheKey, diskPath, url, http)
                .WaitAsync(ct)
                .ConfigureAwait(false);
        }, ct);
    }

    private Task<string?> GetOrStartDownloadAsync(string cacheKey, string diskPath, string url, HttpClient http)
    {
        Lazy<Task<string?>>? lazyDownload = null;
        lazyDownload = _inflightDownloads.GetOrAdd(cacheKey, _ => new Lazy<Task<string?>>(() =>
        {
            var task = DownloadAndCacheToDiskAsync(diskPath, url, http, CancellationToken.None);
            task.ContinueWith(
                _ => RemoveInflightDownload(cacheKey, lazyDownload),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyDownload.Value;
    }

    private void RemoveInflightDownload(string cacheKey, Lazy<Task<string?>>? lazyDownload)
    {
        if (lazyDownload == null)
            return;

        ((ICollection<KeyValuePair<string, Lazy<Task<string?>>>>)_inflightDownloads)
            .Remove(new KeyValuePair<string, Lazy<Task<string?>>>(cacheKey, lazyDownload));
    }

    private async Task<string?> DownloadAndCacheToDiskAsync(string diskPath, string url, HttpClient http, CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            // Double-check after acquiring lock
            if (File.Exists(diskPath))
            {
                TouchDiskFile(diskPath);
                return diskPath;
            }

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            tempPath = $"{diskPath}.{Guid.NewGuid():N}.tmp";
            await using (var remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await remoteStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
                await fileStream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, diskPath, overwrite: true);
            tempPath = null;
            ScheduleTrimDiskCache();
            return diskPath;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { }
            }
            throw;
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

    private static string BuildCacheKey(string contentId, string imageType, string url)
    {
        var normalizedUrl = NormalizeImageUrlForCache(url);
        var urlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)))[..16];
        return $"{contentId}_{imageType}_{urlHash}";
    }

    private static string NormalizeImageUrlForCache(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim();

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Query = BuildStableQuery(uri.Query)
        };

        return builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }

    private static string BuildStableQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        var stablePairs = new List<(string Name, string Pair)>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var rawName = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var name = Uri.UnescapeDataString(rawName.Replace("+", " "));
            if (IsVolatileImageQueryParameter(name))
                continue;

            stablePairs.Add((name, pair));
        }

        stablePairs.Sort((left, right) =>
        {
            var nameCompare = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameCompare != 0
                ? nameCompare
                : StringComparer.Ordinal.Compare(left.Pair, right.Pair);
        });

        return string.Join('&', stablePairs.Select(pair => pair.Pair));
    }

    private static bool IsVolatileImageQueryParameter(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        return name.StartsWith("X-Amz-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("X-Goog-", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Signature", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Expires", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AWSAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Key-Pair-Id", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Policy", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("jwt", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sig", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("se", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("spr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sv", StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleTrimDiskCache()
    {
        if (_maxDiskBytes <= 0)
            return;

        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastDiskTrimTicks);
        if (lastTicks != 0 && nowTicks - lastTicks < DiskTrimInterval.Ticks)
            return;

        if (Interlocked.Exchange(ref _diskTrimQueued, 1) == 1)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                TrimDiskCache();
                Interlocked.Exchange(ref _lastDiskTrimTicks, DateTime.UtcNow.Ticks);
            }
            finally
            {
                Interlocked.Exchange(ref _diskTrimQueued, 0);
            }
        });
    }

    private void TrimDiskCache()
    {
        try
        {
            if (_maxDiskBytes <= 0) return;

            var files = Directory.GetFiles(_diskCacheDir, "*.img")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderBy(file => file.LastAccessTimeUtc)
                .ToList();

            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= _maxDiskBytes) break;
                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void TouchDiskFile(string diskPath)
    {
        try { File.SetLastAccessTimeUtc(diskPath, DateTime.UtcNow); }
        catch { }
    }

    public void Dispose()
    {
        _downloadLock.Dispose();
        _memoryCache.Clear();
    }
}
