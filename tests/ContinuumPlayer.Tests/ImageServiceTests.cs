using System.Net;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Tests;

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
            var firstCompletion = await Task.WhenAny(first, Task.Delay(250));
            releaseContent.SetResult();

            Assert.Same(first, firstCompletion);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            var secondPath = await second.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(secondPath));
            Assert.Equal(1, requestCount);
        }
        finally
        {
            DeleteTempCacheDir(cacheDir);
        }
    }

    private static string CreateTempCacheDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "ContinuumPlayer.Tests", Guid.NewGuid().ToString("N"));
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
