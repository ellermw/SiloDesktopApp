using System.IO.Compression;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class EbookPackageExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "silo-ebook-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ReextractingIntoTheSameDirectoryDropsPagesFromThePreviousPackage()
    {
        var target = Path.Combine(_root, "book");
        EbookPackageExtractor.Extract(Comic("001.png", "002.png", "003.png"), target, "cbz");

        var book = EbookPackageExtractor.Extract(Comic("001.png"), target, "cbz");

        Assert.Equal(["001.png"], book.Chapters.Select(chapter => chapter.RelativePath));
    }

    [Fact]
    public void SvgComicPagesAreShownThroughAnInertImageWrapper()
    {
        // The reader navigates to each page on its trusted origin, where a
        // top-level SVG document would run its scripts.
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>chrome.webview.postMessage('{\"type\":\"reader-error\"}')</script></svg>");
        var target = Path.Combine(_root, "svg");

        var book = EbookPackageExtractor.Extract(Zip(("001.png", [0x89, 0x50, 0x4E, 0x47]), ("pages/0 2&.svg", svg)), target, "cbz");

        Assert.Equal("001.png", book.Chapters[0].RelativePath);
        var wrapper = book.Chapters[1].RelativePath;
        Assert.EndsWith(".html", wrapper);
        var html = File.ReadAllText(Path.Combine(target, wrapper));
        Assert.Contains("<img src=\"pages/0%202%26.svg\"", html);
        Assert.Contains("default-src 'none'", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<svg", html);
    }

    [Fact]
    public void ArchiveWithMoreEntriesThanTheLimitIsRejected()
    {
        var target = Path.Combine(_root, "many");

        var error = Assert.Throws<InvalidDataException>(() =>
            EbookPackageExtractor.Extract(Comic("1.png", "2.png", "3.png"), target, "cbz", maxBytes: 1_000_000, maxEntries: 2));

        Assert.Contains("too many files", error.Message);
    }

    [Fact]
    public void ArchiveThatExpandsBeyondTheByteLimitIsRejectedWhileCopying()
    {
        var target = Path.Combine(_root, "large");
        var package = Zip(("page.png", new byte[256 * 1024]));
        Assert.True(package.Length < 64 * 1024, "zero-filled entry should compress well below the limit");

        var error = Assert.Throws<InvalidDataException>(() =>
            EbookPackageExtractor.Extract(package, target, "cbz", maxBytes: 64 * 1024, maxEntries: 10));

        Assert.Contains("supported size", error.Message);
        var written = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
        Assert.True(written <= 64 * 1024, $"extraction wrote {written} bytes past the limit");
    }

    [Fact]
    public void ArchiveEntryEscapingTheTargetIsStillRejected()
    {
        var target = Path.Combine(_root, "slip");

        Assert.Throws<InvalidDataException>(() =>
            EbookPackageExtractor.Extract(Zip(("../escape.png", [1])), target, "cbz"));

        Assert.False(File.Exists(Path.Combine(_root, "escape.png")));
    }

    [Fact]
    public void ReaderCacheReleasesItsDirectoryAndPrunesOnlyUnownedDirectories()
    {
        var active = EbookReaderCache.Allocate(_root);
        Directory.CreateDirectory(active);
        var released = EbookReaderCache.Allocate(_root);
        Directory.CreateDirectory(released);
        var orphan = Path.Combine(_root, "legacy-content-id", "42");
        Directory.CreateDirectory(orphan);

        EbookReaderCache.Release(released);
        EbookReaderCache.PruneInactive(_root);

        Assert.True(Directory.Exists(active));
        Assert.False(Directory.Exists(released));
        Assert.True(Directory.Exists(Path.Combine(_root, "legacy-content-id")));
        EbookReaderCache.Release(active);
        Assert.False(Directory.Exists(active));
    }

    [Fact]
    public void PruningPreservesAnotherProcessesLockedLeaseAndDeletesOnlyOrphanedLeases()
    {
        var leases = Directory.CreateDirectory(Path.Combine(_root, ".leases")).FullName;
        var activeId = Guid.NewGuid().ToString("N");
        var orphanId = Guid.NewGuid().ToString("N");
        var active = Directory.CreateDirectory(Path.Combine(_root, activeId)).FullName;
        var orphan = Directory.CreateDirectory(Path.Combine(_root, orphanId)).FullName;
        using var foreignOwner = new FileStream(Path.Combine(leases, activeId + ".lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        File.WriteAllText(Path.Combine(leases, orphanId + ".lease"), "");
        EbookReaderCache.PruneInactive(_root);
        Assert.True(Directory.Exists(active));
        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void ReleasingUnknownDirectoryDoesNotDeleteIt()
    {
        var unknown = Directory.CreateDirectory(Path.Combine(_root, "not-owned")).FullName;
        EbookReaderCache.Release(unknown);
        Assert.True(Directory.Exists(unknown));
    }

    [Fact]
    public async Task ReleaseWaitsUntilTheWriterStopsTouchingTheDirectory()
    {
        var directory = EbookReaderCache.Allocate(_root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(async () =>
        {
            await gate.Task;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "last-write.txt"), "fixture");
        });
        var release = EbookReaderCache.ReleaseAfterAsync(directory, writer);
        EbookReaderCache.PruneInactive(_root);
        Assert.True(Directory.Exists(directory));
        Assert.False(release.IsCompleted);
        gate.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task FailedWriterStillReleasesItsLease()
    {
        var directory = EbookReaderCache.Allocate(_root);
        await EbookReaderCache.ReleaseAfterAsync(directory, Task.FromException(new IOException("fixture failure")));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void PruningAMissingCacheRootIsANoOp()
    {
        EbookReaderCache.PruneInactive(Path.Combine(_root, "missing"));
    }

    private static byte[] Comic(params string[] pages)
        => Zip(pages.Select(page => (page, new byte[] { 0x89, 0x50, 0x4E, 0x47 })).ToArray());

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(data);
            }
        }
        return stream.ToArray();
    }
}
