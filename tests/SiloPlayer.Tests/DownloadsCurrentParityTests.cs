using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Downloads;

namespace SiloPlayer.Tests;

public sealed class DownloadsCurrentParityTests
{
    [Fact]
    public void ManagedDownloadResponseUsesCurrentStringIdAndPreparationFields()
    {
        var json = """
        {
          "downloads": [{
            "id": "download-uuid",
            "content_id": "movie-1",
            "media_file_id": 42,
            "file_size": 9000000000,
            "bytes_sent": 1048576,
            "kind": "movie",
            "status": "ready",
            "quality": "10mbps",
            "effective_quality": "10mbps",
            "delivery_format": "transcode",
            "target_bitrate_kbps": 10000,
            "revision": 3,
            "created_at": "2026-07-16T12:00:00Z"
          }]
        }
        """;
        var response = JsonSerializer.Deserialize<DownloadsResponse>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var download = Assert.Single(response!.Downloads);
        Assert.Equal("download-uuid", download.Id);
        Assert.Equal("movie-1", download.ContentId);
        Assert.Equal("transcode", download.DeliveryFormat);
        Assert.Equal(10_000, download.TargetBitrateKbps);
    }

    [Fact]
    public void DesktopUsesCurrentCapabilityManagedAndDirectDownloadContracts()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "DownloadsApi.cs"));
        var itemPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var downloadsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "DownloadsPage.xaml.cs"));

        Assert.Contains("/api/v1/downloads/capability", api);
        Assert.Contains("/api/v1/direct-download?file_id=", api);
        Assert.Contains("SaveDirectDownloadAsync", itemPage);
        Assert.Contains("Choose a file to download", itemPage);
        Assert.Contains("CanCurrentUserDownload", itemPage);
        Assert.DoesNotContain("MediaFileId = _selectedVersion.FileId", itemPage);
        Assert.Contains("dl.EffectiveQuality", downloadsPage);
        Assert.Contains("dl.DeliveryFormat", downloadsPage);
        Assert.Contains("dl.BytesSent", downloadsPage);
        Assert.Contains("AuthenticationHeaderValue", downloadsPage);
        Assert.Contains("new HttpRequestMessage(HttpMethod.Get, url)", downloadsPage, StringComparison.Ordinal);
        Assert.Contains("request.Headers.Authorization", downloadsPage, StringComparison.Ordinal);
        Assert.Contains("DownloadsApi.GetDownloadFilePath(downloadId)", downloadsPage, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?i)[?&]token\s*="), downloadsPage);
        Assert.DoesNotContain("AppendToken", downloadsPage, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?i)[?&]token\s*="), api);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
