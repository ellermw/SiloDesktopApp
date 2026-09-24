using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DownloadFileTransferTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SiloDownloadTests-" + Guid.NewGuid().ToString("N"));

    public DownloadFileTransferTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("Movie.mp4", "video/mp4", "Movie.mp4")]
    [InlineData("Movie.mkv", "video/x-matroska", "Movie.mkv")]
    [InlineData("Prepared.mp4", "application/octet-stream", "Prepared.mp4")]
    [InlineData("../../Movie: Final?.mp4", "video/mp4", "Movie_ Final_.mp4")]
    [InlineData("C:\\private\\Movie.webm", "video/webm", "Movie.webm")]
    [InlineData("CON.mkv", "video/x-matroska", "_CON.mkv")]
    [InlineData("Movie", "video/mp4", "Movie.mp4")]
    [InlineData(null, "video/mp4", "download.mp4")]
    [InlineData(null, "video/x-matroska", "download.mkv")]
    [InlineData(null, "video/webm", "download.webm")]
    [InlineData(null, "application/octet-stream", "download.bin")]
    public void SuggestsSanitizedResponseFilenameAndActualContainer(string? filename, string mime, string expected)
    {
        using var response = Response([1, 2, 3]);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        if (filename != null)
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = filename };

        Assert.Equal(expected, DownloadFileTransfer.GetSuggestedFileName(response.Content.Headers));
    }

    [Fact]
    public void PrefersInternationalFilenameOverAsciiFallback()
    {
        using var response = Response([1]);
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = "fallback.mp4", FileNameStar = "Amélie.mp4"
        };
        Assert.Equal("Amélie.mp4", DownloadFileTransfer.GetSuggestedFileName(response.Content.Headers));
    }

    [Fact]
    public async Task CompleteTransferReplacesExistingFileWithoutTrailingBytes()
    {
        var path = Path.Combine(_directory, "chosen-by-user.mp4");
        await File.WriteAllBytesAsync(path, [9, 9, 9, 9, 9]);
        using var response = Response([1, 2, 3]);

        await DownloadFileTransfer.SaveAsync(response, path);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task RejectedResponseDoesNotCreateLocalOutput(HttpStatusCode status)
    {
        using var response = Response([1, 2, 3]);
        response.StatusCode = status;
        await Assert.ThrowsAsync<HttpRequestException>(() => DownloadFileTransfer.SaveAsync(response, Path.Combine(_directory, "movie.mp4")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ShortResponsePreservesPreviousFileAndRemovesPartialOutput()
    {
        var path = Path.Combine(_directory, "movie.mp4");
        await File.WriteAllBytesAsync(path, [9]);
        using var response = Response([1, 2, 3]);
        response.Content.Headers.ContentLength = 20;

        await Assert.ThrowsAsync<IOException>(() => DownloadFileTransfer.SaveAsync(response, path));

        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task MidStreamFailurePreservesPreviousFileAndRemovesPartialOutput()
    {
        var path = Path.Combine(_directory, "movie.mp4");
        await File.WriteAllBytesAsync(path, [9]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream()) };

        await Assert.ThrowsAsync<IOException>(() => DownloadFileTransfer.SaveAsync(response, path));

        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CancellationDuringTransferIsNotAnErrorAndRemovesPartialOutput()
    {
        using var cancellation = new CancellationTokenSource();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new InterruptedStream(cancellation))
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DownloadFileTransfer.SaveAsync(response, Path.Combine(_directory, "movie.mp4"), cancellation.Token));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task DestinationWriteFailureRemovesPartialOutput()
    {
        var path = Path.Combine(_directory, "movie.mp4");
        await File.WriteAllBytesAsync(path, [9]);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var response = Response([1, 2, 3]);

        var failure = await Record.ExceptionAsync(() => DownloadFileTransfer.SaveAsync(response, path));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());

        Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(9, locked.ReadByte());
    }

    [Fact]
    public async Task MissingDestinationDirectoryReportsWriteFailureWithoutClaimingPartialRemains()
    {
        using var response = Response([1, 2, 3]);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => DownloadFileTransfer.SaveAsync(response,
            Path.Combine(_directory, "missing-folder", "movie.mp4")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ContextChangeBeforeCommitPreservesPreviousFile()
    {
        var path = Path.Combine(_directory, "movie.mp4");
        await File.WriteAllBytesAsync(path, [9]);
        using var response = Response([1, 2, 3]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadFileTransfer.SaveAsync(response, path,
            beforeCommit: () => throw new OperationCanceledException()));

        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CleanupFailureIdentifiesPartialFileAndRetainsCancellationState()
    {
        using var response = Response([1, 2, 3]);
        FileStream? lockedPartial = null;
        try
        {
            var error = await Assert.ThrowsAsync<DownloadCleanupException>(() => DownloadFileTransfer.SaveAsync(response,
                Path.Combine(_directory, "movie.mp4"), beforeCommit: () =>
                {
                    lockedPartial = new FileStream(Assert.Single(Directory.GetFiles(_directory)), FileMode.Open,
                        FileAccess.Read, FileShare.None);
                    throw new OperationCanceledException();
                }));

            Assert.True(error.WasCanceled);
            Assert.EndsWith(".partial", error.PartialPath);
            Assert.Equal(Assert.Single(Directory.GetFiles(_directory)), error.PartialPath);
            Assert.False(File.Exists(Path.Combine(_directory, "movie.mp4")));
        }
        finally { lockedPartial?.Dispose(); }
    }

    [Fact]
    public void SaveErrorDoesNotExposeServerRequestOrCredentialDetails()
    {
        var error = new HttpRequestException("Failed https://server/download?token=secret", null, HttpStatusCode.Forbidden);
        var message = DownloadFileTransfer.GetSaveErrorMessage(error);
        Assert.Contains("permission", message);
        Assert.DoesNotContain("secret", message);
        Assert.DoesNotContain("https://", message);
    }

    [Fact]
    public void EmptyPickerPlaceholderIsExplicitlyIdentifiedWithoutDeletingIt()
    {
        var path = Path.Combine(_directory, "movie.mp4");
        File.WriteAllBytes(path, []);
        Assert.Contains(path, DownloadFileTransfer.GetEmptyDestinationNotice(path));
        Assert.True(File.Exists(path));
        File.WriteAllBytes(path, [9]);
        Assert.Equal("", DownloadFileTransfer.GetEmptyDestinationNotice(path));
        Assert.Equal("", DownloadFileTransfer.GetEmptyDestinationNotice(null));
    }

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class InterruptedStream(CancellationTokenSource? cancellation = null) : Stream
    {
        private bool _started;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_started) throw new IOException("Injected interrupted network stream");
            _started = true;
            buffer.Span[0] = 1;
            cancellation?.Cancel();
            return ValueTask.FromResult(1);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
