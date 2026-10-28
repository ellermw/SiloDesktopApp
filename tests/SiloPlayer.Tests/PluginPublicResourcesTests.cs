using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PluginPublicResourcesTests
{
    [Theory]
    [InlineData("POST", "https://cdn.invalid/asset", "https://server.invalid")]
    [InlineData("DELETE", "https://cdn.invalid/asset", "https://server.invalid")]
    [InlineData("GET", "file:///C:/asset", "https://server.invalid")]
    [InlineData("GET", "https://user:secret@cdn.invalid/asset", "https://server.invalid")]
    [InlineData("GET", "http://cdn.invalid/asset", "https://server.invalid")]
    [InlineData("GET", "https://server.invalid/api/v2/account/me", "https://server.invalid")]
    public async Task PublicAssetBoundaryRefusesWritesUnsafeAddressesAndServerReentryBeforeNetwork(
        string method, string target, string server)
    {
        using var resources = new PluginPublicResources();
        await Assert.ThrowsAsync<InvalidDataException>(() => resources.ReadAsync(new(target), new(server), method,
            new Dictionary<string, string>(), () => true, CancellationToken.None));
    }

    [Fact]
    public async Task RetiredOwnerCannotStartAPublicResourceRead()
    {
        using var resources = new PluginPublicResources();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resources.ReadAsync(new("https://cdn.invalid/asset"),
            new("https://server.invalid"), "GET", new Dictionary<string, string>(), () => false, CancellationToken.None));
    }
}
