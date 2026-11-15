using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

/// <summary>
/// The parser mirrors the official WebUI (<c>web/src/lib/linkToken.ts</c>): the token is the fragment of a current link
/// and the path segment of an older one. Every value here is a synthetic placeholder.
/// </summary>
public sealed class AccountLinkTokenTests
{
    private const string Server = "https://silo.example.test";

    [Theory]
    [InlineData("https://silo.example.test/reset-password#tok-1_A", "reset-password", Server, "tok-1_A")]
    [InlineData("https://silo.example.test/reset-password/#tok-1_A", "reset-password", Server, "tok-1_A")]
    [InlineData("https://silo.example.test/reset-password/tok-1_A", "reset-password", Server, "tok-1_A")]
    [InlineData("https://silo.example.test/reset-password/tok-1_A/", "reset-password", Server, "tok-1_A")]
    [InlineData("https://silo.example.test/invite#tok-1_A", "invite", Server, "tok-1_A")]
    [InlineData("https://silo.example.test/invite/tok-1_A", "invite", Server, "tok-1_A")]
    // Server base path, trailing slash and default port are all the same origin.
    [InlineData("https://silo.example.test/base/invite#tok-1_A", "invite", "https://silo.example.test/base/", "tok-1_A")]
    [InlineData("https://silo.example.test:443/invite#tok-1_A", "invite", Server, "tok-1_A")]
    [InlineData("HTTPS://SILO.EXAMPLE.TEST/invite#tok-1_A", "invite", Server, "tok-1_A")]
    [InlineData("http://lan.test:8096/reset-password#tok-1_A", "reset-password", "http://lan.test:8096", "tok-1_A")]
    // A query on the page does not matter; the fragment is the token.
    [InlineData("https://silo.example.test/invite?ref=mail#tok-1_A", "invite", Server, "tok-1_A")]
    // A path token wins over a fragment: the path form has no use for one.
    [InlineData("https://silo.example.test/invite/path-tok#fragment-tok", "invite", Server, "path-tok")]
    // Percent-encoding of valid characters is decoded before validation.
    [InlineData("https://silo.example.test/invite#tok%2D1%5FA", "invite", Server, "tok-1_A")]
    // A pasted link may carry whitespace or quotes from the shell or a mail client.
    [InlineData("  \"https://silo.example.test/invite#tok-1_A\"  ", "invite", Server, "tok-1_A")]
    // A raw token is accepted without a server.
    [InlineData("tok-1_A", "invite", Server, "tok-1_A")]
    [InlineData("tok-1_A", "reset-password", null, "tok-1_A")]
    public void AcceptsFragmentPathAndBareTokens(string value, string page, string? server, string token)
    {
        Assert.True(AccountLinkToken.TryParse(value, page, server, out var parsed, out var failure));
        Assert.Equal(token, parsed);
        Assert.Equal(AccountLinkFailure.None, failure);
    }

    [Theory]
    // Other servers, schemes, credentials and unselected servers.
    [InlineData("https://other.example.test/invite#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("http://silo.example.test/invite#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test:8443/invite#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://user:pw@silo.example.test/invite#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/invite#tok", "invite", "", AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/invite#tok", "invite", null, AccountLinkFailure.WrongServer)]
    [InlineData("ftp://silo.example.test/invite#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("silo://invite?server=https%3A%2F%2Fsilo.example.test&token=tok", "invite", Server, AccountLinkFailure.WrongServer)]
    // The other page, a different page that shares a prefix, and a different base path.
    [InlineData("https://silo.example.test/reset-password#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/invite#tok", "reset-password", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/invite-codes/abc", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/invites#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/other/invite#tok", "invite", "https://silo.example.test/base", AccountLinkFailure.WrongServer)]
    [InlineData("https://silo.example.test/library#tok", "invite", Server, AccountLinkFailure.WrongServer)]
    // The page without its token, as when something on the way dropped the fragment.
    [InlineData("https://silo.example.test/invite", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite/", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite#", "invite", Server, AccountLinkFailure.InvalidToken)]
    // More than one path segment is not a token.
    [InlineData("https://silo.example.test/invite/tok/extra", "invite", Server, AccountLinkFailure.InvalidToken)]
    // Malformed or unsafe encodings and characters.
    [InlineData("https://silo.example.test/invite#tok%zz", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite#tok%2Fx", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite#tok%", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite#to k", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite#tok#again", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("https://silo.example.test/invite/tok%2Fx", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("tok.with.dots", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("tök", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("#tok", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData("   ", "invite", Server, AccountLinkFailure.InvalidToken)]
    [InlineData(null, "invite", Server, AccountLinkFailure.InvalidToken)]
    public void RefusesLinksThatAreNotThisServersAndTokensThatAreNotTokens(string? value, string page, string? server, AccountLinkFailure failure)
    {
        Assert.False(AccountLinkToken.TryParse(value, page, server, out var token, out var actual));
        Assert.Equal("", token);
        Assert.Equal(failure, actual);
    }

    [Fact]
    public void TokenLengthFollowsTheServersBodyBound()
    {
        var longest = new string('a', AccountLinkToken.MaxLength);
        Assert.True(AccountLinkToken.TryParse($"{Server}/invite#{longest}", "invite", Server, out var token, out _));
        Assert.Equal(longest, token);
        Assert.False(AccountLinkToken.TryParse($"{Server}/invite#{longest}a", "invite", Server, out _, out var failure));
        Assert.Equal(AccountLinkFailure.InvalidToken, failure);
        Assert.False(AccountLinkToken.TryParse(longest + "a", "invite", Server, out _, out _));
    }

    [Theory]
    [InlineData("https://silo.example.test/invite#tok", "invite", true)]
    [InlineData("https://elsewhere.test/base/invite/tok", "invite", true)]
    [InlineData("\"https://silo.example.test/reset-password#tok\"", "reset-password", true)]
    [InlineData("https://silo.example.test/invites#tok", "invite", false)]
    [InlineData("https://silo.example.test/reset-password#tok", "invite", false)]
    [InlineData("silo://invite?server=https%3A%2F%2Fsilo.example.test&token=tok", "invite", false)]
    [InlineData("tok", "invite", false)]
    [InlineData(null, "invite", false)]
    public void RouteDetectionIgnoresTheServerSoActivationCanWaitForOne(string? value, string page, bool expected)
        => Assert.Equal(expected, AccountLinkToken.LooksLikeLink(value, page));

    [Fact]
    public void NativeInviteHandoffKeepsItsOwnContract()
    {
        Assert.True(InviteDeepLink.TryParse("silo://invite?server=https%3A%2F%2Fsilo.example.test%2F&token=tok-1", out var navigation));
        Assert.Equal(new InviteClaimNavigation("https://silo.example.test", "tok-1"), navigation);
        // The selected server does not matter to the native handoff: it names its own.
        Assert.True(InviteDeepLink.TryParse("silo://invite?server=https%3A%2F%2Fsilo.example.test&token=tok-1", "https://different.test", out var named));
        Assert.Equal("https://silo.example.test", named.ServerUrl);
        Assert.False(InviteDeepLink.TryParse("silo://invite?server=https%3A%2F%2Fsilo.example.test", out _));
        Assert.False(InviteDeepLink.TryParse("silo://reset-password?server=https%3A%2F%2Fsilo.example.test&token=tok-1", out _));
    }

    [Theory]
    [InlineData("https://silo.example.test/invite#tok-1_A", Server, "https://silo.example.test", "tok-1_A")]
    [InlineData("https://silo.example.test/invite/tok-1_A", Server, "https://silo.example.test", "tok-1_A")]
    [InlineData("https://silo.example.test/base/invite#tok-1_A", "https://silo.example.test/base/", "https://silo.example.test/base", "tok-1_A")]
    [InlineData("\"https://silo.example.test/invite#tok-1_A\"", Server, "https://silo.example.test", "tok-1_A")]
    public void SelectedServerWebInviteLinkOpensTheClaimFlow(string link, string selected, string serverUrl, string token)
    {
        Assert.True(InviteDeepLink.TryParse(link, selected, out var navigation));
        Assert.Equal(new InviteClaimNavigation(serverUrl, token), navigation);
    }

    [Theory]
    [InlineData("https://other.example.test/invite#tok-1_A", Server)]
    [InlineData("http://silo.example.test/invite#tok-1_A", Server)]
    [InlineData("https://silo.example.test/invite#tok-1_A", "")]
    [InlineData("https://silo.example.test/invite#tok-1_A", null)]
    [InlineData("https://silo.example.test/invite", Server)]
    [InlineData("https://silo.example.test/invite#", Server)]
    [InlineData("https://silo.example.test/reset-password#tok-1_A", Server)]
    // A bare token names no server, so it is never an activation route.
    [InlineData("tok-1_A", Server)]
    [InlineData("", Server)]
    [InlineData(null, Server)]
    public void WebInviteLinkOfAnotherServerOrWithoutATokenIsRefused(string? link, string? selected)
        => Assert.False(InviteDeepLink.TryParse(link, selected, out _));

    [Fact]
    public void WebInviteRouteIsHeldUntilAServerIsSelected()
    {
        Assert.True(InviteDeepLink.IsServerLinkRoute("https://silo.example.test/invite#tok"));
        Assert.True(InviteDeepLink.IsServerLinkRoute("https://silo.example.test/invite/tok"));
        Assert.False(InviteDeepLink.IsServerLinkRoute("silo://invite?server=https%3A%2F%2Fsilo.example.test&token=tok"));
        Assert.False(InviteDeepLink.IsServerLinkRoute("https://silo.example.test/reset-password#tok"));
    }
}
