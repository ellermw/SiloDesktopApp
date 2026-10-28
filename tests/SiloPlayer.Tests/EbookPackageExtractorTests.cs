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
        Assert.False(Directory.Exists(Path.Combine(_root, "legacy-content-id")));
        EbookReaderCache.Release(active);
        Assert.False(Directory.Exists(active));
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
