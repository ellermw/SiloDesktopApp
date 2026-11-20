using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminPreparedFilesPresentationTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1000, "1.0 KB")]
    [InlineData(14_200_000_000, "14 GB")]
    public void StorageBytesUseOfficialDecimalUnits(long bytes, string expected) =>
        Assert.Equal(expected, AdminPreparedFilesPresentation.FormatBytes(bytes));

    [Fact]
    public void StorageWarningsPrioritizeDiskTemporaryOfflineUntrackedAndStaleDevicePressure()
    {
        var storage = new AdminDownloadStorage
        {
            DiskCeilingPercent = 85, StaleDeviceDays = 14,
            Locations =
            [
                new() { Key = "server", Kind = "server", Name = "Server", Online = true, InUseBytes = 10, CachedBytes = 5,
                    Usage = new() { FSTotalBytes = 100, FSUsedBytes = 84, Bytes = 15, Ephemeral = true } },
                new() { Key = "node:9", Kind = "node", Name = "gpu-1", Online = false, InUseBytes = 20, WaitingDownloads = 2,
                    UntrackedBytes = 7, StaleWaitingBytes = 6 },
            ],
        };

        Assert.Equal(["fill:server", "ephemeral:server", "offline:node:9", "untracked:node:9", "stale"],
            AdminPreparedFilesPresentation.StorageWarnings(storage).Select(w => w.Id));
    }

    [Fact]
    public void FileRecipeUsageAndHistoryReasonMatchOfficialVisibleCopy()
    {
        var file = new AdminPreparedFile
        {
            Format = "transcode", Resolution = "1080p", VideoCodec = "h264", BitrateKbps = 10_000,
            Downloading = 1, Waiting = 2, Finished = 3,
        };
        Assert.Equal(("1080p H264", "10 Mbps"), AdminPreparedFilesPresentation.FileRecipe(file));
        Assert.Equal("1 downloading · 2 waiting · 3 finished", AdminPreparedFilesPresentation.FileUsage(file));
        Assert.Equal("Revoked downloads", AdminPreparedFilesPresentation.HistoryReason("revoked"));
        Assert.Equal("future_reason", AdminPreparedFilesPresentation.HistoryReason("future_reason"));
    }

    [Fact]
    public void ConfirmationCopyMakesInUseRepreparationAndDeviceDeletionExplicit()
    {
        var cached = new AdminPreparedFile { Id = "cached", State = "cached", Title = "Dune", Bytes = 10 };
        var inUse = new AdminPreparedFile { Id = "used", State = "in_use", Title = "Heat", Bytes = 20 };
        var delete = AdminPreparedFilesPresentation.DeletePrompt([cached, inUse], includeInUse: false);
        Assert.Contains("1 cached", delete.Description);
        Assert.Contains("1 in use", delete.Description);
        Assert.Contains("prepared again", delete.InUseWarning);
        Assert.Equal("Delete cached file", delete.ConfirmLabel);

        var revoke = AdminPreparedFilesPresentation.RevokePrompt("Pixel 8", "Maya", 2, 1000, stale: true, cacheHours: 72);
        Assert.Contains("deletes them the next time it connects", revoke.Description);
        Assert.Contains("72 hours", revoke.CacheEffect);
        Assert.Contains("may take a while", revoke.StaleEffect);
    }
}
