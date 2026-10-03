using System.Security.Cryptography;
using System.Text;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NativeOAuthHandshakeTests
{
    private static NativeOAuthHandshake Start(bool linking = false) => new(
        "https://Silo.Example:443/proxy", "deployment-fixture", 3,
        "/api/v2/auth/oauth/3/native/start", selectAccount: true, linkTicket: linking ? "ticket-fixture" : null);
    private static string Callback(NativeOAuthHandshake attempt, string suffix = "code=one-time") =>
        $"org.siloserver.silo:/auth/callback?iss=https%3A%2F%2Fsilo.example&server=deployment-fixture&state={attempt.State}&{suffix}";

    [Fact]
    public void Start_PreservesSavedReverseProxyBaseAndS256Binding()
    {
        var attempt = Start();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.CodeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(43, attempt.CodeVerifier.Length);
        Assert.StartsWith("https://silo.example/proxy/api/v2/auth/oauth/3/native/start?", attempt.StartUri.AbsoluteUri);
        Assert.Contains("code_challenge=" + challenge, attempt.StartUri.Query);
        Assert.Contains("code_challenge_method=S256", attempt.StartUri.Query);
        Assert.Contains("prompt=select_account", attempt.StartUri.Query);
        Assert.DoesNotContain(attempt.CodeVerifier, attempt.StartUri.Query);
        Assert.NotEqual(attempt.State, Start().State);
    }

    [Theory]
    [InlineData("iss=https%3A%2F%2Fsilo.example", "iss=https%3A%2F%2Fforeign.example")]
    [InlineData("iss=https%3A%2F%2Fsilo.example", "iss=http%3A%2F%2Fsilo.example")]
    [InlineData("server=deployment-fixture", "server=another-deployment")]
    [InlineData("/auth/callback?", "/another/callback?")]
    [InlineData("code=one-time", "code=%ZZ")]
    [InlineData("code=one-time", "code=one-time&code=duplicate")]
    [InlineData("code=one-time", "code=one-time&error=provider_unavailable")]
    [InlineData("code=one-time", "code=one-time&link=1")]
    public void ForeignOrMalformedCallback_DoesNotConsumePendingAttempt(string before, string after)
    {
        var attempt = Start();
        Assert.False(attempt.TryConsumeCallback(Callback(attempt).Replace(before, after), out _));
        Assert.True(attempt.TryConsumeCallback(Callback(attempt), out var result));
        Assert.Equal("one-time", result.Code);
        Assert.False(attempt.TryConsumeCallback(Callback(attempt), out _));
    }

    [Fact]
    public void WrongStateAndCanceledAttempt_CannotComplete()
    {
        var attempt = Start();
        Assert.False(attempt.TryConsumeCallback(Callback(attempt).Replace(attempt.State, Start().State), out _));
        attempt.Cancel();
        Assert.False(attempt.TryConsumeCallback(Callback(attempt), out _));
    }

    [Fact]
    public void LinkSuccess_IsBoundToLinkRoleAndFailureUsesOfficialUnmarkedCallback()
    {
        var attempt = Start(linking: true);
        Assert.False(attempt.TryConsumeCallback(Callback(attempt), out _));
        Assert.True(attempt.TryConsumeCallback(Callback(attempt, "code=link-code&link=1"), out _));
        var failed = Start(linking: true);
        Assert.True(failed.TryConsumeCallback(Callback(failed, "error=provider_unavailable"), out var error));
        Assert.Equal("provider_unavailable", error.Error);
        Assert.Empty(error.Code);
    }

    [Theory]
    [InlineData("https://foreign.example/native/start")]
    [InlineData("/api/v2/auth/oauth/4/native/start")]
    [InlineData("")]
    public void ProviderWithoutCanonicalNativeStart_CannotOpenFlow(string path) =>
        Assert.Throws<ArgumentException>(() => new NativeOAuthHandshake("https://silo.example", "deployment", 3, path));
}
