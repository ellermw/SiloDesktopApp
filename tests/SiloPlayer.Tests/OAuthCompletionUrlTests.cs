using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class OAuthCompletionUrlTests
{
    [Fact]
    public void TryGetCode_AcceptsCompletionFromConfiguredServerOrigin()
    {
        var accepted = OAuthCompletionUrl.TryGetCode(
            "https://silo.example/login/oauth-complete?code=one%20two",
            "https://silo.example",
            out var code);

        Assert.True(accepted);
        Assert.Equal("one two", code);
    }

    [Theory]
    [InlineData("https://attacker.example/login/oauth-complete?code=stolen")]
    [InlineData("http://silo.example/login/oauth-complete?code=downgraded")]
    [InlineData("https://silo.example:8443/login/oauth-complete?code=wrong-port")]
    public void TryGetCode_RejectsCompletionFromAnotherOrigin(string callback)
    {
        Assert.False(OAuthCompletionUrl.TryGetCode(
            callback,
            "https://silo.example",
            out _));
    }

    [Fact]
    public void TryGetCode_RejectsMalformedPercentEncodingWithoutThrowing()
    {
        var accepted = OAuthCompletionUrl.TryGetCode(
            "https://silo.example/login/oauth-complete?code=%ZZ",
            "https://silo.example",
            out var code);

        Assert.False(accepted);
        Assert.Empty(code);
    }
}
