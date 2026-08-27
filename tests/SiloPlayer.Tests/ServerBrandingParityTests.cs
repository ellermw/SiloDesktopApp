using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ServerBrandingParityTests
{
    [Fact]
    public void BrandingResponseReadsCurrentLightAppearanceAssets()
    {
        var response = JsonSerializer.Deserialize<ServerBrandingResponse>("""
            {
              "server_name": "My Silo",
              "wordmark_url": "/dark-wordmark.png",
              "wordmark_light_url": "/light-wordmark.png",
              "mark_url": "/dark-mark.png",
              "mark_light_url": "/light-mark.png"
            }
            """);

        Assert.NotNull(response);
        Assert.Equal("/light-wordmark.png", response.WordmarkLightUrl);
        Assert.Equal("/light-mark.png", response.MarkLightUrl);
    }

    [Fact]
    public void LightAppearancePrefersLightAssets()
    {
        var choice = BrandingAssetSelector.Select(CreateBranding(), isLightAppearance: true);

        Assert.Equal("/light-wordmark.png", choice.WordmarkUrl);
        Assert.Equal("/light-mark.png", choice.MarkUrl);
    }

    [Fact]
    public void LightAppearanceFallsBackToMainCustomAssets()
    {
        var response = CreateBranding();
        response.WordmarkLightUrl = null;
        response.MarkLightUrl = null;

        var choice = BrandingAssetSelector.Select(response, isLightAppearance: true);

        Assert.Equal("/dark-wordmark.png", choice.WordmarkUrl);
        Assert.Equal("/dark-mark.png", choice.MarkUrl);
    }

    [Fact]
    public void DarkAppearanceUsesMainAssets()
    {
        var choice = BrandingAssetSelector.Select(CreateBranding(), isLightAppearance: false);

        Assert.Equal("/dark-wordmark.png", choice.WordmarkUrl);
        Assert.Equal("/dark-mark.png", choice.MarkUrl);
    }

    [Fact]
    public void ShellReappliesBrandingAfterTheProfileThemeIsKnown()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("BrandingAssetSelector.Select", source);
        Assert.Contains("ThemeService.IsLightAppearance", source);
        var themeSync = source.IndexOf("await _themeService.SyncFromServerAsync", StringComparison.Ordinal);
        var brandingRefresh = source.IndexOf("await LoadShellBrandingAsync", themeSync, StringComparison.Ordinal);
        Assert.True(themeSync >= 0 && brandingRefresh > themeSync);
    }

    private static ServerBrandingResponse CreateBranding() => new()
    {
        WordmarkUrl = "/dark-wordmark.png",
        WordmarkLightUrl = "/light-wordmark.png",
        MarkUrl = "/dark-mark.png",
        MarkLightUrl = "/light-mark.png",
    };

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException();
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
