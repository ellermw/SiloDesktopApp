using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ServerWebUiUriTests
{
    [Theory]
    [InlineData("https://silo.example/api/v1", "https://silo.example/")]
    [InlineData("https://silo.example/api/v1/", "https://silo.example/")]
    [InlineData("https://silo.example/", "https://silo.example/")]
    [InlineData("https://silo.example/custom/api/v1", "https://silo.example/custom/")]
    public void ApiBaseMapsToTheConnectedWebUiOrigin(string apiBase, string expected)
    {
        Assert.Equal(expected, ServerWebUiUri.FromApiBase(apiBase).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///c:/silo")]
    public void InvalidOrNonHttpApiBasesAreRejected(string apiBase)
    {
        Assert.Throws<ArgumentException>(() => ServerWebUiUri.FromApiBase(apiBase));
    }
}
