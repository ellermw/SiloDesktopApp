using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorDiagnosticsFileTests
{
    [Fact]
    public async Task RetiredDownloadNeverReplacesAnExistingDestination()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var folder = Path.Combine(Path.GetTempPath(), "silo-logs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "report.tar.gz");
        try
        {
            await File.WriteAllTextAsync(target, "existing");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiagnosticsFileTransfer.SaveAsync(lease, target, async (destination, cancellation) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellation);
                authority.Retire();
                return true;
            }));
            Assert.Equal("existing", await File.ReadAllTextAsync(target));
            Assert.Equal(new[] { target }, Directory.GetFiles(folder));
        }
        finally { File.Delete(target); Directory.Delete(folder); }
    }

    [Fact]
    public async Task CompleteDownloadPublishesExactlyOnceAfterFlush()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var folder = Path.Combine(Path.GetTempPath(), "silo-logs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "report.tar.gz");
        try
        {
            Assert.True(await DiagnosticsFileTransfer.SaveAsync(lease, target, async (destination, cancellation) => { await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellation); return true; }));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { File.Delete(target); Directory.Delete(folder); }
    }
}
