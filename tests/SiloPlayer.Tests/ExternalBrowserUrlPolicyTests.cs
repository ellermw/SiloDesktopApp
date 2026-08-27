using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ExternalBrowserUrlPolicyTests
{
    [Theory]
    [InlineData("https://app.plex.tv/auth#?code=123", "app.plex.tv")]
    [InlineData("http://provider.example/activate", "provider.example")]
    public void TryGetSafeUri_AllowsHttpOrigins(string input, string expectedHost)
    {
        Assert.True(ExternalBrowserUrlPolicy.TryGetSafeUri(input, out var uri));
        Assert.Equal(expectedHost, uri.Host);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("silo-custom://activate")]
    [InlineData("https://example.test/ok\r\nfile:///bad")]
    [InlineData("not a url")]
    [InlineData("")]
    public void TryGetSafeUri_RejectsNonWebAndMalformedTargets(string input)
    {
        Assert.False(ExternalBrowserUrlPolicy.TryGetSafeUri(input, out _));
    }
}
