using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class SiloCastPlaybackAuthorityTests
{
    [Fact]
    public void SeparatePlaybackAuthorityDoesNotRepointLocalCredentials()
    {
        using var localHttp = new HttpClient();
        using var remoteHttp = new HttpClient();
        var localApi = new SiloApiClient(localHttp);
        localApi.SetBaseUrl("https://local.example");
        localApi.SetAccessToken("local-access");
        localApi.SetProfile("local-profile", "local-proof");
        using var localAuth = new AuthService(localApi, new AuthApi(localApi));
        var local = new PlaybackAuthorityScope(localApi, localAuth);
        var remoteApi = new SiloApiClient(remoteHttp);
        remoteApi.SetBaseUrl("https://remote.example");
        remoteApi.SetAccessToken("remote-access");
        remoteApi.SetProfile("remote-profile", "remote-proof");
        using var remoteAuth = new AuthService(remoteApi, new AuthApi(remoteApi));
        var remote = new PlaybackAuthorityScope(remoteApi, remoteAuth);
        Assert.Equal("https://local.example", local.ApiClient.BaseUrl);
        Assert.Equal("local-access", local.ApiClient.AccessToken);
        Assert.Equal("local-profile", local.ApiClient.ProfileId);
        Assert.Equal("remote-profile", remote.ApiClient.ProfileId);
        Assert.NotSame(local.PlaybackApi, remote.PlaybackApi);
    }
}
