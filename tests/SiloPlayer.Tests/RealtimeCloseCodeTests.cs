using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Official Silo Server #4556 (f31842a6f): the v2 sockets close with a code that says what to do next
/// (internal/api/handlers/socket_close.go, docs/realtime-api.md "Close codes", read at c59798116).
/// </summary>
public sealed class RealtimeCloseCodeTests
{
    [Fact]
    public void TheCodesMatchTheOfficialServer()
    {
        Assert.Equal(4001, RealtimeCloseCodes.AccessChanged);
        Assert.Equal(4002, RealtimeCloseCodes.CredentialInvalid);
        Assert.Equal(4003, RealtimeCloseCodes.LifetimeEnded);
        Assert.Equal(4004, RealtimeCloseCodes.AuthorityUnavailable);
    }

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData(1000, false, 0)]
    [InlineData(1006, false, 0)]
    [InlineData(4001, false, 0)]  // access_changed: reconnect at once
    [InlineData(4002, true, 30)]  // credential_invalid: never with the same credential; the WebUI waits 30 s
    [InlineData(4003, false, 0)]  // lifetime_ended: mint a new ticket and reconnect
    [InlineData(4004, false, 5)]  // authority_unavailable: back off; the WebUI waits 5 s
    public void EachCodeSaysWhetherAndWhenToReconnect(int? closeCode, bool endsCredential, double minimumSeconds)
    {
        Assert.Equal(endsCredential, RealtimeCloseCodes.EndsCredential(closeCode));
        Assert.Equal(TimeSpan.FromSeconds(minimumSeconds), RealtimeCloseCodes.MinimumReconnectDelay(closeCode));
    }

    [Fact]
    public void WatchTogetherStopsAfterCredentialInvalidAndBacksOffAfterAuthorityUnavailable()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "SiloPlayer", "ViewModels", "WatchTogetherViewModels.cs"));
        Assert.Contains("if (result.MessageType == WebSocketMessageType.Close) return (int?)result.CloseStatus;", source);
        Assert.Contains("if (SiloPlayer.Core.Services.RealtimeCloseCodes.EndsCredential(closeCode))", source);
        Assert.Contains("SiloPlayer.Core.Services.RealtimeCloseCodes.MinimumReconnectDelay(closeCode)", source);
    }

    // Home keeps its rows when only a reconnect catch-up moved the read generation (the owner is the same),
    // and retires them for every other change.
    [Fact]
    public void HomeKeepsItsOwnerAcrossATransportCatchUpButNotAnyOtherContextChange()
    {
        using var http = new HttpClient();
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://home-owner.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SelectProfile("alpha", "synthetic-proof-a");
        var home = new HomeApi(client);

        var (loaded, owner) = home.CaptureContextAndOwner();
        Assert.Equal(client.CaptureContext(), loaded);
        Assert.True(client.TryInvalidateAccessContext(loaded, out _, transportCatchUp: true));
        var (afterCatchUp, ownerAfterCatchUp) = home.CaptureContextAndOwner();
        Assert.NotEqual(loaded, afterCatchUp);      // reads from before the reconnect still retire
        Assert.Equal(owner, ownerAfterCatchUp);     // but the same owner keeps what it shows
        Assert.True(client.TryInvalidateAccessContext(afterCatchUp, out _, transportCatchUp: true));
        Assert.Equal(owner, home.CaptureContextAndOwner().Owner);

        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _)); // access changed
        var afterAccess = home.CaptureContextAndOwner();
        Assert.NotEqual(owner, afterAccess.Owner);

        client.AdoptProfileProof(afterAccess.Request, "synthetic-proof-b");
        var afterProof = home.CaptureContextAndOwner();
        Assert.NotEqual(afterAccess.Owner, afterProof.Owner);

        auth.SelectProfile("beta", "synthetic-proof-c");
        Assert.NotEqual(afterProof.Owner, home.CaptureContextAndOwner().Owner);

        var source = File.ReadAllText(FindRepositoryFile("src", "SiloPlayer", "ViewModels", "HomeViewModel.cs"));
        Assert.Contains("var (context, owner) = _homeApi.CaptureContextAndOwner();", source);
        Assert.Contains("if (_loadedContext is { } loaded && loaded != context && _loadedOwner == owner &&", source);
        Assert.Contains("_loadedOwner = owner;", source);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join('/', segments));
    }
}
