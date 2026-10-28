using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NativeApiCutoverTests
{
    [Theory]
    [InlineData("/api/v1/plugins/7/proxy/stream?st=fixture")]
    [InlineData("https://server.test/api/v1/stream/session?st=fixture")]
    [InlineData("https://node.test/silo/api/v1/stream/session?st=fixture")]
    [InlineData("https://node.test/silo/API/V1/stream/session?st=fixture")]
    [InlineData("https://node.test/silo/api%2Fv1/stream/session?st=fixture")]
    public void RetiringDeliveryRequiresANewPlanInsteadOfReusingTheBridge(string value)
    {
        var error = Assert.Throws<InvalidDataException>(() => PlaybackDeliveryUrl.Resolve("https://server.test", value));
        Assert.Contains("restart", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fixture", error.Message);
    }

    [Theory]
    [InlineData("https://node.test/stream/v3/session?st=a%2fb%2Bc")]
    [InlineData("https://node.test/stream/direct/signed%2Fpath?st=a%2fb%2Bc")]
    [InlineData("https://node.test/stream/direct/signed%2Fapi%2Fv1%2Fpath?st=a%2fb%2Bc")]
    [InlineData("https://node.test/downloads/file/signed?dt=a%2Fb")]
    [InlineData("https://node.test/stream/v3/session?st=%2Fapi%2Fv1%2Fopaque")]
    public void CurrentDistributedDeliveryKeepsItsExactOriginPathAndSignedBytes(string value)
        => Assert.Equal(value, PlaybackDeliveryUrl.Resolve("https://server.test", value));

    [Theory]
    [InlineData("/api/v1/branding/assets/mark?v=fixture")]
    [InlineData("https://server.test/silo/api/v1/plugins/7/assets/logo.svg")]
    public void RetiringAssetsCannotBeReturnedToImageConsumers(string value)
    {
        var api = Client();
        Assert.Null(api.ResolveServerUrl(value));
    }

    [Fact]
    public async Task CatalogArtworkRefusesRetiredNativeResourcesBeforeCacheOrNetwork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SiloPlayer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var images = new ImageService(directory);
            using var handler = new ImageWire();
            using var http = new HttpClient(handler);
            const string old = "https://server.test/api/v1/images/poster?st=synthetic";
            Assert.Null(await images.GetImageAsync("title", "poster", old, http));
            Assert.Null(await images.GetImageDiskPathAsync("title", "poster", old, http));
            Assert.Equal(0, handler.Reads);
            const string current = "https://cdn.test/images/poster?st=%2Fapi%2Fv1%2Fopaque";
            Assert.NotNull(await images.GetImageAsync("title", "poster", current, http));
            Assert.Equal(1, handler.Reads);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("/api/v1/downloads/7/file")]
    [InlineData("https://server.test/api/v1/stream/session")]
    public void AuthenticatedTransfersRefuseRetiredNativeRoutesBeforeDispatch(string value)
    {
        var api = Client();
        Assert.Throws<InvalidDataException>(() => api.CreateAuthenticatedRequest(HttpMethod.Get, value));
    }

    [Fact]
    public void CapturedPluginHeadersFollowTokenRefreshButCannotBorrowAnotherProfilesProof()
    {
        var api = Client();
        api.BeginAuthenticationSession("original");
        api.SetProfile("owner", "owner-proof");
        var owner = api.CaptureContext();
        api.SetAccessToken("refreshed");
        using var request = api.CreateAuthenticatedRequest(owner, HttpMethod.Get, "/api/v2/plugin-content/plugins/7/");
        Assert.Equal("refreshed", request.Headers.Authorization!.Parameter);
        Assert.Equal("owner-proof", Assert.Single(request.Headers.GetValues("X-Profile-Token")));
        api.SetProfile("replacement", "replacement-proof");
        Assert.Throws<OperationCanceledException>(() => api.CreateAuthenticatedRequest(owner, HttpMethod.Get, "/api/v2/plugin-content/plugins/7/"));
    }

    [Theory]
    [InlineData("https://SERVER.test/api/v1/", "https://server.test")]
    [InlineData("https://server.test/silo/api/v2?old=1", "https://server.test/silo")]
    [InlineData("https://server.test/my-api/v1", "https://server.test/my-api/v1")]
    public void SavedApiSuffixesNormalizeWithoutRemovingReverseProxyPrefixes(string value, string expected)
        => Assert.Equal(expected, ServerUrlIdentity.Normalize(value));

    [Fact]
    public void SavedApiSuffixMigrationPreservesRefreshAndPinGrantAtTheCanonicalServer()
    {
        var credentials = new MemoryCredentialStore();
        const string saved = "https://server.test/silo/api/v1/";
        credentials.SaveCredential(saved, "refresh_token", "synthetic-refresh");
        credentials.SaveCredential(saved, "profile_id", "synthetic-profile");
        credentials.SaveCredential(saved, "profile_token", "synthetic-proof");
        var resolved = new SavedSessionCredentialResolver(credentials).Resolve(saved)!;
        Assert.Equal("https://server.test/silo", resolved.ServerUrl);
        Assert.Equal("synthetic-refresh", credentials.LoadCredential(resolved.ServerUrl, "refresh_token"));
        Assert.Equal("synthetic-profile", credentials.LoadCredential(resolved.ServerUrl, "profile_id"));
        Assert.Equal("synthetic-proof", credentials.LoadCredential(resolved.ServerUrl, "profile_token"));
        Assert.Null(credentials.LoadCredential(saved, "refresh_token"));
    }

    private static SiloApiClient Client()
    {
        var api = new SiloApiClient(new HttpClient());
        api.SetBaseUrl("https://server.test");
        return api;
    }

    private sealed class MemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<(string, string), string> _values = new();
        public void SaveCredential(string serverUrl, string key, string value) => _values[(serverUrl, key)] = value;
        public string? LoadCredential(string serverUrl, string key) => _values.GetValueOrDefault((serverUrl, key));
        public void DeleteCredential(string serverUrl, string key) => _values.Remove((serverUrl, key));
        public void DeleteAllForServer(string serverUrl)
        { foreach (var key in _values.Keys.Where(key => key.Item1 == serverUrl).ToArray()) _values.Remove(key); }
    }

    private sealed class ImageWire : HttpMessageHandler
    {
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }
}
