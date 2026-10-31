using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateChannelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "silo-channel-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("synthetic verified installer");
    private AppUpdateService Create(HttpClient http, string installed = "1.2.517") => new(http, root,
        ReleaseVersion.Parse(installed).GetWindowsVersion()) { InstalledReleaseVersion = ReleaseVersion.Parse(installed) };

    [Fact]
    public async Task ExistingClientsDefaultToStableLatestOnly()
    {
        using var wire = new Wire { Stable = Release("1.2.518"), Catalog = [Release("1.2.519-beta.1")] };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.CheckAsync();
        Assert.Equal(AppUpdateChannel.Stable, service.Channel);
        Assert.Equal("1.2.518", service.State.Release?.DisplayVersion);
        Assert.All(wire.Requests, r => Assert.EndsWith("/releases/latest", r.Url));
    }

    [Theory]
    [InlineData("1.2.517", "1.2.517", "1.2.518-beta.12")]
    [InlineData("1.2.518-beta.2", "1.2.517", "1.2.518-beta.12")]
    [InlineData("1.2.518-beta.2", "1.2.518", "1.2.518")]
    [InlineData("1.2.518-beta.12", "1.2.518", "1.2.518")]
    [InlineData("1.2.518", "1.2.518", null)]
    public async Task BetaOffersNewestApplicableSemanticReleaseWithoutIntermediateInstalls(string installed, string stable, string? expected)
    {
        using var wire = new Wire { Stable = Release(stable), Catalog = [Release("1.2.518-beta.2"), Release("1.2.518-beta.12"), Release("1.2.518-beta.1")] };
        using var http = new HttpClient(wire); var service = Create(http, installed);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        Assert.Null(service.State.Error); Assert.Equal(expected, service.State.Release?.DisplayVersion);
        if (expected?.Contains("beta") == true) Assert.Equal(new Version(1, 2, 517, 12), service.State.Release!.Version);
    }

    [Fact]
    public async Task CatalogPaginationAndPrecedenceDoNotDependOnPublicationOrder()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.2")], SecondPage = [Release("1.2.518-beta.12")] };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        Assert.Equal("1.2.518-beta.12", service.State.Release?.DisplayVersion);
        Assert.Contains(wire.Requests, r => r.Url.EndsWith("page=2"));
    }

    [Fact]
    public async Task StableSwitchWaitsWithoutDowngradeAndLaterPromotesTheSameBetaCore()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var service = Create(http, "1.2.518-beta.12");
        await service.CheckAsync(); Assert.Null(service.State.Release);
        Assert.Contains("stable", service.State.ChannelNotice!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("downgrade", service.State.ChannelNotice!, StringComparison.OrdinalIgnoreCase);
        wire.Stable = Release("1.2.518"); await service.CheckAsync();
        Assert.Equal("1.2.518", service.State.Release?.DisplayVersion); Assert.Null(service.State.ChannelNotice);
    }

    [Fact]
    public async Task ConditionalAuthorityIsScopedToEndpointAndChannelAndKeepsExactPreparedDownload()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.12")], Conditional = true };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.CheckAsync(); await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync(); await service.DownloadAsync();
        var prepared = Assert.IsType<string>(service.State.ReadyInstaller);
        await service.CheckAsync(); Assert.Equal(prepared, service.State.ReadyInstaller); Assert.True(await service.ReverifyReadyAsync());
        Assert.Contains(wire.Requests, r => r.Url.Contains("?per_page=") && r.ETag == "\"catalog1\"");
        Assert.DoesNotContain(wire.Requests, r => r.Url.EndsWith("/latest") && r.ETag == "\"catalog1\"");
        await service.SetChannelAsync(AppUpdateChannel.Stable);
        Assert.Null(service.State.Release); Assert.Null(service.State.ReadyInstaller); Assert.False(await service.ReverifyReadyAsync());
        Assert.False(service.ShouldInstallOnExit);
        await service.CheckAsync(); Assert.Null(service.State.Release);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("wrong-flag")]
    [InlineData("major")]
    [InlineData("leading-zero")]
    public async Task IneligibleCatalogEntriesNeverBecomeAnInstallableOffer(string kind)
    {
        var item = Release("1.2.518-beta.12");
        if (kind == "draft") item["draft"] = true;
        if (kind == "wrong-flag") item["prerelease"] = false;
        if (kind == "major") item = Release("2.0.1-beta.1");
        if (kind == "leading-zero") item["tag_name"] = "v1.2.518-beta.012";
        using var wire = new Wire { Catalog = [item] }; using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync(); Assert.Null(service.State.Release);
    }

    [Theory]
    [InlineData("url")]
    [InlineData("digest")]
    [InlineData("duplicate")]
    public async Task ManipulatedChosenBetaAssetsFailAuthorityValidation(string kind)
    {
        var item = Release("1.2.518-beta.12"); var asset = item["assets"]![0]!;
        if (kind == "url") asset["browser_download_url"] = "https://evil.invalid/setup.exe";
        if (kind == "digest") asset["digest"] = "sha256:invalid";
        if (kind == "duplicate") item["assets"]!.AsArray().Add(asset.DeepClone());
        using var wire = new Wire { Catalog = [item] }; using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        Assert.Null(service.State.Release); Assert.NotNull(service.State.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeitherChannelAutomaticallyOffersANewMajor(bool beta)
    {
        using var wire = new Wire { Stable = Release("2.0.0"), Catalog = [Release("2.0.1-beta.1")] };
        using var http = new HttpClient(wire); var service = Create(http);
        if (beta) await service.SetChannelAsync(AppUpdateChannel.Beta);
        await service.CheckAsync(); Assert.Null(service.State.Release); Assert.False(service.ShouldInstallOnExit);
    }

    [Fact]
    public async Task CatalogCannotRedirectPaginationOutsideThePinnedEndpoint()
    {
        using var wire = new Wire { BadLink = true, Catalog = [Release("1.2.518-beta.12")] };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        Assert.NotNull(service.State.Error); Assert.Null(service.State.Release);
        Assert.All(wire.Requests, r => Assert.StartsWith("https://api.github.com/repos/ellermw/SiloDesktopApp/releases", r.Url));
    }

    [Fact]
    public async Task ADelayedBetaCheckCannotRepublishAnOfferAfterStableWasSelected()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.12")] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeCatalog = async _ => { entered.TrySetResult(); await finish.Task; };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); var check = service.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var changing = service.SetChannelAsync(AppUpdateChannel.Stable);
        Assert.True(service.IsChangingChannel); Assert.Null(service.State.Release);
        finish.TrySetResult(); await Task.WhenAll(check, changing).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AppUpdateChannel.Stable, service.Channel); Assert.Null(service.State.Release);
        await service.CheckAsync(); Assert.Null(service.State.Release); Assert.Null(service.State.Error);
    }

    [Fact]
    public async Task SwitchingDuringDownloadCancelsAndDisarmsTheOldChannelPackage()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.12")] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeAsset = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); };
        using var http = new HttpClient(wire); var service = Create(http); service.SetMode(AppUpdateMode.Automatic);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync(); var download = service.DownloadAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await service.SetChannelAsync(AppUpdateChannel.Stable).WaitAsync(TimeSpan.FromSeconds(10)); await download;
        Assert.Null(service.State.Release); Assert.Null(service.State.ReadyInstaller); Assert.Null(service.State.PreparedPackage);
        Assert.False(service.ShouldInstallOnExit); Assert.False(await service.ReverifyReadyAsync());
    }

    [Fact]
    public async Task SwitchingChannelsDoesNotClearTheSharedGitHubCooldown()
    {
        using var wire = new Wire { Limited = true }; using var http = new HttpClient(wire); var service = Create(http);
        await service.CheckAsync(); await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        await service.SetChannelAsync(AppUpdateChannel.Stable); await service.CheckAsync();
        Assert.Single(wire.Requests); Assert.NotNull(service.State.Error);
    }

    [Fact]
    public async Task CancelledSelectionCannotReuseBetaCacheAsStableAuthorityOnAnUnexpected304()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.12")] };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        Assert.True(service.State.Release!.IsBeta);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetChannelAsync(AppUpdateChannel.Stable, stop.Token));
        wire.ForceStableNotModified = true; await service.CheckAsync();
        Assert.Equal(AppUpdateChannel.Stable, service.Channel);
        Assert.Null(service.State.Release); Assert.NotNull(service.State.Error);
    }

    [Fact]
    public async Task LatestSelectionWinsWhenTwoChannelChangesWaitBehindAnOldCheck()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.12")] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeCatalog = async _ => { entered.TrySetResult(); await finish.Task; };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); var check = service.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var first = service.SetChannelAsync(AppUpdateChannel.Stable);
        var second = service.SetChannelAsync(AppUpdateChannel.Beta);
        finish.TrySetResult(); await Task.WhenAll(check, first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AppUpdateChannel.Beta, service.Channel); Assert.False(service.IsChangingChannel); Assert.Null(service.State.Release);
        wire.BeforeCatalog = null; await service.CheckAsync();
        Assert.Equal("1.2.518-beta.12", service.State.Release?.DisplayVersion);
    }

    [Fact]
    public async Task NumericRepositoryPaginationHintStillDispatchesOnlyThePinnedOwnerEndpoint()
    {
        using var wire = new Wire { Catalog = [Release("1.2.518-beta.2")], SecondPage = [Release("1.2.518-beta.12")],
            NextLink = "<https://api.github.com/repositories/123456/releases?page=2&per_page=100>; rel=\"next\"" };
        using var http = new HttpClient(wire); var service = Create(http);
        await service.SetChannelAsync(AppUpdateChannel.Beta); await service.CheckAsync();
        Assert.Null(service.State.Error); Assert.Equal("1.2.518-beta.12", service.State.Release?.DisplayVersion);
        Assert.Contains(wire.Requests, r => r.Url == "https://api.github.com/repos/ellermw/SiloDesktopApp/releases?per_page=100&page=2");
        Assert.DoesNotContain(wire.Requests, r => r.Url.Contains("/repositories/"));
    }

    internal static JsonObject Release(string version)
    {
        var tag = "v" + version;
        return new JsonObject { ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = version.Contains('-'), ["body"] = "Short synthetic release notes.",
            ["published_at"] = "2026-10-29T10:00:00Z", ["assets"] = new JsonArray(new JsonObject { ["name"] = "SiloInstaller-Windows-x64.exe", ["state"] = "uploaded",
                ["size"] = Package.Length, ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant(),
                ["browser_download_url"] = $"https://github.com/ellermw/SiloDesktopApp/releases/download/{tag}/SiloInstaller-Windows-x64.exe" }) };
    }

    private sealed class Wire : HttpMessageHandler
    {
        public JsonObject Stable = Release("1.2.517");
        public JsonObject[] Catalog = [], SecondPage = [];
        public bool Conditional, BadLink, Limited, ForceStableNotModified;
        public string? NextLink;
        public Func<CancellationToken, Task>? BeforeCatalog, BeforeAsset;
        public List<(string Url, string? ETag)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization);
            var url = request.RequestUri!.AbsoluteUri; var etag = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString(); Requests.Add((url, etag));
            if (Limited) return new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)) } };
            if (request.RequestUri.Host == "github.com")
            { if (BeforeAsset is not null) await BeforeAsset(ct); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) }; }
            var latest = url.EndsWith("/latest"); var second = url.EndsWith("page=2"); var validator = latest ? "\"stable\"" : second ? "\"catalog2\"" : "\"catalog1\"";
            if (latest && ForceStableNotModified) return new(HttpStatusCode.NotModified);
            if (Conditional && etag == validator) return new(HttpStatusCode.NotModified);
            if (!latest && BeforeCatalog is not null) await BeforeCatalog(ct);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(latest ? Stable.ToJsonString() : JsonSerializer.Serialize(second ? SecondPage : Catalog), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new EntityTagHeaderValue(validator);
            if (!latest && !second && (SecondPage.Length != 0 || BadLink)) response.Headers.TryAddWithoutValidation("Link", BadLink
                ? "<https://evil.invalid/releases?page=2>; rel=\"next\""
                : NextLink ?? "<https://api.github.com/repos/ellermw/SiloDesktopApp/releases?per_page=100&page=2>; rel=\"next\"");
            return response;
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
