using System.Net;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ImageServiceTests
{
    [Fact]
    public async Task GetImageDiskPathAsync_ReusesPoster_WhenOnlySignedQueryChanges()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            using var service = new ImageService(cacheDir, maxMemoryCacheBytes: 1024 * 1024, maxDiskCacheBytes: 1024 * 1024);
            using var http = new HttpClient(new DelegateHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(ImageResponse());
            }));

            var firstPath = await service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Date=20260430T000000Z&X-Amz-Expires=900&X-Amz-Signature=aaa",
                http);

            var secondPath = await service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Date=20260430T001500Z&X-Amz-Expires=900&X-Amz-Signature=bbb",
                http);

            Assert.Equal(firstPath, secondPath);
            Assert.Equal(1, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    [Fact]
    public async Task GetImageDiskPathAsync_RefreshesPoster_WhenStableVersionQueryChanges()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            using var service = new ImageService(cacheDir, maxMemoryCacheBytes: 1024 * 1024, maxDiskCacheBytes: 1024 * 1024);
            using var http = new HttpClient(new DelegateHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(ImageResponse());
            }));

            var firstPath = await service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?v=1&X-Amz-Signature=aaa",
                http);

            var secondPath = await service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?v=2&X-Amz-Signature=bbb",
                http);

            Assert.NotEqual(firstPath, secondPath);
            Assert.Equal(2, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    [Fact]
    public async Task GetImageDiskPathAsync_KeepsSharedDownload_WhenFirstWaiterCancels()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseContent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var service = new ImageService(cacheDir, maxMemoryCacheBytes: 1024 * 1024, maxDiskCacheBytes: 1024 * 1024);
            using var http = new HttpClient(new DelegateHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new BlockingImageContent(readStarted, releaseContent)
                });
            }));

            using var firstWaiter = new CancellationTokenSource();
            var first = service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Signature=aaa",
                http,
                firstWaiter.Token);

            await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Signature=aaa",
                http,
                CancellationToken.None);

            firstWaiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.WaitAsync(TimeSpan.FromSeconds(5)));
            releaseContent.SetResult();
            var secondPath = await second.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(secondPath));
            Assert.Equal(1, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    [Fact]
    public async Task GetImageDiskPathAsync_DoesNotRepeatMissingImageRequestWithinNegativeCacheWindow()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            using var service = new ImageService(cacheDir, maxMemoryCacheBytes: 1024 * 1024, maxDiskCacheBytes: 1024 * 1024);
            using var http = new HttpClient(new DelegateHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }));
            const string url = "https://cdn.example.test/posters/missing.jpg?X-Amz-Signature=aaa";

            var firstPath = await service.GetImageDiskPathAsync("missing-movie", "poster", url, http);
            var secondPath = await service.GetImageDiskPathAsync("missing-movie", "poster", url, http);

            Assert.Null(firstPath);
            Assert.Null(secondPath);
            Assert.Equal(1, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    [Fact]
    public async Task GetImageDiskPathAsync_RetriesMissingImageWhenSignedUrlChanges()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            using var service = new ImageService(cacheDir, maxMemoryCacheBytes: 1024 * 1024, maxDiskCacheBytes: 1024 * 1024);
            using var http = new HttpClient(new DelegateHandler((_, _) =>
            {
                var attempt = Interlocked.Increment(ref requestCount);
                return Task.FromResult(attempt == 1
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : ImageResponse());
            }));

            var firstPath = await service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Signature=expired",
                http);
            var secondPath = await service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Signature=fresh",
                http);

            Assert.Null(firstPath);
            Assert.True(File.Exists(secondPath));
            Assert.Equal(2, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    [Fact]
    public async Task GetImageDiskPathAsync_FreshSignedUrlDoesNotJoinExpiredInflightFailure()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            var expiredRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExpiredRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var service = new ImageService(cacheDir, maxMemoryCacheBytes: 1024 * 1024, maxDiskCacheBytes: 1024 * 1024);
            using var http = new HttpClient(new DelegateHandler(async (request, ct) =>
            {
                Interlocked.Increment(ref requestCount);
                if (request.RequestUri!.Query.Contains("expired", StringComparison.Ordinal))
                {
                    expiredRequestStarted.TrySetResult();
                    await releaseExpiredRequest.Task.WaitAsync(ct);
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return ImageResponse();
            }));

            var expired = service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Signature=expired",
                http);
            await expiredRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var fresh = service.GetImageDiskPathAsync(
                "movie-1",
                "poster",
                "https://cdn.example.test/posters/movie-1.jpg?X-Amz-Signature=fresh",
                http);
            releaseExpiredRequest.SetResult();

            Assert.Null(await expired.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(File.Exists(await fresh.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Equal(2, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    [Fact]
    public async Task GetImageDiskPathAsync_BoundsNegativeCacheAcrossDistinctMissingUrls()
    {
        var cacheDir = CreateTempCacheDir();
        try
        {
            var requestCount = 0;
            using var service = new ImageService(
                cacheDir,
                maxMemoryCacheBytes: 1024 * 1024,
                maxDiskCacheBytes: 1024 * 1024,
                maxNegativeCacheEntries: 2);
            using var http = new HttpClient(new DelegateHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }));

            await service.GetImageDiskPathAsync("movie-1", "poster", "https://cdn.example.test/1.jpg?token=secret-1", http);
            await service.GetImageDiskPathAsync("movie-2", "poster", "https://cdn.example.test/2.jpg?token=secret-2", http);
            await service.GetImageDiskPathAsync("movie-3", "poster", "https://cdn.example.test/3.jpg?token=secret-3", http);
            await service.GetImageDiskPathAsync("movie-1", "poster", "https://cdn.example.test/1.jpg?token=secret-1", http);

            Assert.Equal(4, requestCount);
            var field = typeof(ImageService).GetField(
                "_negativeCache",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var cache = Assert.IsType<System.Collections.Concurrent.ConcurrentDictionary<string, long>>(
                field?.GetValue(service));
            Assert.True(cache.Count <= 2);
            Assert.All(cache.Keys, key => Assert.DoesNotContain("secret-", key, StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    private static string CreateTempCacheDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "SiloPlayer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempCacheDir(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }

    private static HttpResponseMessage ImageResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 })
        };
    }

    private sealed class BlockingImageContent : HttpContent
    {
        private readonly TaskCompletionSource _readStarted;
        private readonly TaskCompletionSource _releaseContent;

        public BlockingImageContent(TaskCompletionSource readStarted, TaskCompletionSource releaseContent)
        {
            _readStarted = readStarted;
            _releaseContent = releaseContent;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new BlockingImageStream(_readStarted, _releaseContent));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new BlockingImageStream(_readStarted, _releaseContent));
    }

    private sealed class BlockingImageStream : Stream
    {
        private readonly TaskCompletionSource _readStarted;
        private readonly TaskCompletionSource _releaseContent;
        private int _readCount;

        public BlockingImageStream(TaskCompletionSource readStarted, TaskCompletionSource releaseContent)
        {
            _readStarted = readStarted;
            _releaseContent = releaseContent;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _readCount, 1) != 0)
                return 0;

            _readStarted.TrySetResult();
            await _releaseContent.Task.WaitAsync(cancellationToken);

            var bytesToCopy = Math.Min(count, 4);
            Array.Copy(new byte[] { 1, 2, 3, 4 }, 0, buffer, offset, bytesToCopy);
            return bytesToCopy;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _readCount, 1) != 0)
                return 0;

            _readStarted.TrySetResult();
            await _releaseContent.Task.WaitAsync(cancellationToken);

            var bytes = new byte[] { 1, 2, 3, 4 };
            var bytesToCopy = Math.Min(buffer.Length, bytes.Length);
            bytes.AsSpan(0, bytesToCopy).CopyTo(buffer.Span);
            return bytesToCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
