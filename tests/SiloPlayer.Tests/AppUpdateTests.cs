using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class AppUpdateTests
{
    internal const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal const string AssetUrl = "https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.335/SiloInstaller-Windows-x64.exe";

    [Fact]
    public async Task PublishedStableReleaseProvidesInAppNotesWithoutSiloCredentials()
    {
        using var wire = new Wire(Release()); using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334, 0));
        await service.CheckAsync();
        var update = Assert.IsType<AppRelease>(service.State.Release);
        Assert.Equal(new Version(1, 2, 335, 0), update.Version);
        Assert.Equal("## Fixed\nPlayback and navigation", update.Notes);
        Assert.Equal(Digest, update.Sha256);
        Assert.Equal("https://api.github.com/repos/ellermw/SiloDesktopApp/releases/latest", wire.Request!.RequestUri!.AbsoluteUri);
        Assert.Null(wire.Request.Headers.Authorization);
        Assert.Null(service.State.Error);
    }

    [Theory]
    [InlineData("v1.2.334")]
    [InlineData("v1.2.334.0")]
    [InlineData("v1.2.333")]
    public async Task EqualOrOlderVersionsDoNotOfferAnUpdate(string tag)
    {
        using var http = new HttpClient(new Wire(Release(tag: tag)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334, 0));
        await service.CheckAsync();
        Assert.Null(service.State.Release); Assert.Null(service.State.Error);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DraftAndPreviewReleasesAreNotOffered(bool draft, bool preview)
    {
        using var http = new HttpClient(new Wire(Release(draft: draft, preview: preview)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.Null(service.State.Release);
    }

    [Theory]
    [InlineData("https://evil.invalid/setup.exe", "sha256:" + Digest, 128)]
    [InlineData("http://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.335/SiloInstaller-Windows-x64.exe", "sha256:" + Digest, 128)]
    [InlineData(AssetUrl + "?other=1", "sha256:" + Digest, 128)]
    [InlineData(AssetUrl, "", 128)]
    [InlineData(AssetUrl, "sha256:not-a-digest", 128)]
    [InlineData(AssetUrl, "sha256:" + Digest, 0)]
    [InlineData(AssetUrl, "sha256:" + Digest, 536870913)]
    public async Task UnverifiableOrUntrustedPackagesNeverBecomeAvailable(string url, string digest, long size)
    {
        using var http = new HttpClient(new Wire(Release(url: url, digest: digest, size: size)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.Null(service.State.Release); Assert.NotNull(service.State.Error);
    }

    [Fact]
    public async Task OversizedMetadataIsRejectedBeforeParsing()
    {
        using var http = new HttpClient(new Wire(new string(' ', 524289)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.Null(service.State.Release); Assert.NotNull(service.State.Error);
    }

    [Fact]
    public async Task HttpFailureDoesNotEscapeTheUpdateBoundary()
    {
        using var http = new HttpClient(new Wire("", HttpStatusCode.Forbidden));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.NotNull(service.State.Error); Assert.False(service.State.IsChecking);
    }

    [Fact]
    public void ExistingSettingsDefaultToNotificationOnly()
        => Assert.Equal(AppUpdateMode.NotifyOnly, new AppSettings().UpdateMode);

    [Fact]
    public async Task HugeChangelogsAreBoundedForResponsiveNativePresentation()
    {
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(Release())!;
        metadata["body"] = new string('x', 128000);
        using var http = new HttpClient(new Wire(metadata.ToJsonString()));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync();
        Assert.NotNull(service.State.Release);
        Assert.InRange(service.State.Release!.Notes.Length, 1, 33000);
        Assert.Contains("GitHub", service.State.Release.Notes);
    }

    internal static string Release(string tag = "v1.2.335", string url = AssetUrl, string digest = "sha256:" + Digest,
        long size = 128, bool draft = false, bool preview = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag, body = "## Fixed\nPlayback and navigation", published_at = "2026-10-28T10:00:00Z",
            draft, prerelease = preview, assets = new[] { new { name = "SiloInstaller-Windows-x64.exe", browser_download_url = url, digest, size, state = "uploaded" } }
        });

    internal sealed class Wire(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
