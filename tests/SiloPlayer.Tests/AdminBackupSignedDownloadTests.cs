using System.Net;
using System.Text;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Signed backup object downloads: unauthenticated, bounded, verified and atomic. Synthetic data only.</summary>
public sealed class AdminBackupSignedDownloadTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "silo-backup-download-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-16T12:00:00Z");

    public AdminBackupSignedDownloadTests() => Directory.CreateDirectory(_directory);
    public void Dispose() { try { Directory.Delete(_directory, true); } catch (IOException) { } }

    private sealed class ObjectServer : HttpMessageHandler
    {
        internal readonly List<HttpRequestMessage> Requests = [];
        internal Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("test")) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private static AdminBackup Backup(bool encrypted = false) => new()
    {
        Id = BackupJson.BackupId, Encrypted = encrypted,
        Objects = encrypted
            ? [new() { Name = "db.dump.age", SizeBytes = 4, Sha256 = BackupJson.Sha }, new() { Name = "restore-set.tar.age", SizeBytes = 10, Sha256 = BackupJson.Sha }]
            : [new() { Name = "db.dump", SizeBytes = 4, Sha256 = BackupJson.Sha }, new() { Name = "restore-set.tar", SizeBytes = 4, Sha256 = BackupJson.Sha }],
    };

    private static AdminBackupDownloadLink Link(string name = "db.dump", long size = 4, string? url = null, DateTimeOffset? expires = null) => new()
    {
        Name = name, SizeBytes = size, ExpiresAt = expires ?? Now.AddMinutes(15),
        Url = url ?? "/api/v2/admin/backups/" + BackupJson.BackupId + "/objects/" + name + "?exp=1763294400&sig=AbC-_d",
    };

    private (AdminMonitorKit Kit, AdminMonitorAuthority Authority, MonitorAuthorityLease Lease) Authority()
    {
        var kit = AdminMonitorKit.Create();
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        return (kit, authority, authority.Capture());
    }

    [Theory]
    [InlineData("/api/v2/admin/backups/20261115T040000Z-nightly/objects/db.dump?exp=1&sig=abc", "https://silo.test/api/v2/admin/backups/20261115T040000Z-nightly/objects/db.dump?exp=1&sig=abc")]
    [InlineData("https://bucket.s3.example.invalid/db.dump?X-Amz-Signature=x", "https://bucket.s3.example.invalid/db.dump?X-Amz-Signature=x")]
    [InlineData("/api/v2/admin/backups/20261114T040000Z-manual/objects/db.dump?exp=1&sig=abc", null)]
    [InlineData("/api/v2/admin/backups/20261115T040000Z-nightly/objects/blobs.tar?exp=1&sig=abc", null)]
    [InlineData("/api/v2/admin/settings?exp=1&sig=abc", null)]
    [InlineData("/api/v2/admin/backups/20261115T040000Z-nightly/objects/db.dump?exp=1&sig=abc&redirect=x", null)]
    [InlineData("//evil.example/db.dump?exp=1&sig=abc", null)]
    [InlineData("http://bucket.example.invalid/db.dump", null)]
    [InlineData("https://user:pass@bucket.example.invalid/db.dump", null)]
    [InlineData("file:///C:/Windows/win.ini", null)]
    [InlineData("javascript:alert(1)", null)]
    public void OnlyThisObjectsSignedPathOrAnHttpsPresignedUrlIsAccepted(string url, string? expected) =>
        Assert.Equal(expected, AdminBackupSignedDownload.ResolveLink("https://silo.test", BackupJson.BackupId, Link(url: url))?.AbsoluteUri);

    [Fact]
    public void AnHttpServerMayUseHttpPresignedLinksAndAPathPrefixIsKept()
    {
        Assert.NotNull(AdminBackupSignedDownload.ResolveLink("http://silo.lan:8080", BackupJson.BackupId, Link(url: "http://minio.lan/db.dump?sig=x")));
        Assert.Equal("https://host.example/silo/api/v2/admin/backups/20261115T040000Z-nightly/objects/db.dump?exp=1&sig=abc",
            AdminBackupSignedDownload.ResolveLink("https://host.example/silo/", BackupJson.BackupId, Link(url: "/api/v2/admin/backups/20261115T040000Z-nightly/objects/db.dump?exp=1&sig=abc"))!.AbsoluteUri);
    }

    [Fact]
    public async Task AVerifiedObjectIsSavedWithoutAnyAccountHeaders()
    {
        var (kit, authority, lease) = Authority();
        using (kit) using (authority) using (lease)
        {
            var server = new ObjectServer();
            using var download = new AdminBackupSignedDownload(server, () => Now);
            var path = Path.Combine(_directory, "db.dump");
            var result = await download.SaveAsync(lease, Backup(), Link(), "full", path);
            Assert.True(result.HashVerified);
            Assert.Equal("test", await File.ReadAllTextAsync(path));
            var request = Assert.Single(server.Requests);
            Assert.Null(request.Headers.Authorization);
            Assert.DoesNotContain(request.Headers, header => header.Key.StartsWith("X-Profile", StringComparison.OrdinalIgnoreCase) || header.Key.StartsWith("X-Device", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(["db.dump"], Directory.GetFiles(_directory).Select(Path.GetFileName));
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("redirect")]
    [InlineData("expired")]
    public async Task AFailedDownloadKeepsTheExistingFileAndLeavesNoPartial(string failure)
    {
        var (kit, authority, lease) = Authority();
        using (kit) using (authority) using (lease)
        {
            var server = new ObjectServer
            {
                Respond = _ => failure switch
                {
                    "hash" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("TEST")) },
                    "short" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("tes")) },
                    "long" => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("tests")) },
                    "redirect" => new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/") } },
                    _ => new(HttpStatusCode.NotFound),
                },
            };
            using var download = new AdminBackupSignedDownload(server, () => Now);
            var path = Path.Combine(_directory, "db.dump");
            await File.WriteAllTextAsync(path, "previous");
            Exception error = await Assert.ThrowsAnyAsync<Exception>(() => download.SaveAsync(lease, Backup(), Link(), "full", path));
            if (failure == "expired") Assert.IsType<AdminBackupLinkExpiredException>(error);
            if (failure is "hash" or "short" or "long") Assert.IsType<AdminBackupDownloadMismatchException>(error);
            Assert.Equal("previous", await File.ReadAllTextAsync(path));
            Assert.Equal(["db.dump"], Directory.GetFiles(_directory).Select(Path.GetFileName));
            Assert.Single(server.Requests);
        }
    }

    [Fact]
    public async Task ALinkPastItsWindowIsNeverFetched()
    {
        var (kit, authority, lease) = Authority();
        using (kit) using (authority) using (lease)
        {
            var server = new ObjectServer();
            using var download = new AdminBackupSignedDownload(server, () => Now);
            await Assert.ThrowsAsync<AdminBackupLinkExpiredException>(() => download.SaveAsync(lease, Backup(), Link(expires: Now.AddSeconds(5)), "full", Path.Combine(_directory, "x")));
            Assert.Empty(server.Requests);
        }
    }

    [Fact]
    public async Task AnEncryptedRestoreSetMayBeSmallerThanItsStoredSizeButNeverLarger()
    {
        var (kit, authority, lease) = Authority();
        using (kit) using (authority) using (lease)
        {
            var server = new ObjectServer();
            using var download = new AdminBackupSignedDownload(server, () => Now);
            var link = Link("restore-set.tar", 10);
            var saved = await download.SaveAsync(lease, Backup(encrypted: true), link, "restore-set", Path.Combine(_directory, "restore-set.tar"));
            Assert.False(saved.HashVerified);
            Assert.Equal(4, saved.Bytes);
            server.Respond = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[11]) };
            await Assert.ThrowsAsync<AdminBackupDownloadMismatchException>(() => download.SaveAsync(lease, Backup(encrypted: true), link, "restore-set", Path.Combine(_directory, "restore-set.tar")));
            Assert.Null(AdminBackupSignedDownload.Expectation(Backup(), Link("blobs.tar", 4), "full"));
        }
    }
}
