using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminShellPage
{
    private ImageSource? _serverWordmark;
    private string? _serverBrandName;
    private CancellationTokenSource? _brandingRead;
    private long _brandingVersion;

    private bool UseLightWordmark
    {
        get
        {
            // The effective sidebar can be light under a Windows contrast theme.
            var background = _sidebarPalette.DrawerBackground.Color;
            return 0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B >= 128;
        }
    }

    private Image ServerWordmark(bool light)
    {
        var fallback = new BitmapImage(new Uri(light
            ? "ms-appx:///Assets/silo-wordmark-sidebar-light.png" : "ms-appx:///Assets/silo-wordmark-sidebar.png"));
        var image = new Image { Source = _serverWordmark ?? fallback, Width = 112, Height = 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(image, "AdminServerWordmark");
        AutomationProperties.SetName(image, _serverBrandName ?? "");
        var failed = false;
        image.ImageFailed += (_, _) => { if (!failed) { failed = true; image.Source = fallback; } };
        return image;
    }

    /// <summary>
    /// The image cache names every body .img and a wordmark URL may have no
    /// extension, so the cached content selects the decoder: BitmapImage cannot
    /// decode SVG. A vector wordmark that fails to load keeps the bundled default.
    /// </summary>
    private static async Task<ImageSource?> WordmarkSourceAsync(string path, CancellationToken ct)
    {
        var svg = await Task.Run<byte[]?>(() =>
        {
            try
            {
                var head = new byte[4096];
                int length;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    length = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
                return SvgArtwork.IsSvgContent(head.AsSpan(0, length)) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        }, ct);
        if (svg == null) return new BitmapImage(new Uri(path));
        var source = new SvgImageSource();
        try
        {
            using var stream = new MemoryStream(svg);
            return await source.SetSourceAsync(stream.AsRandomAccessStream()) == SvgImageSourceLoadStatus.Success ? source : null;
        }
        catch (Exception error) when (error is not OperationCanceledException) { return null; }
    }

    private void RetireBranding()
    {
        _brandingVersion++;
        _brandingRead?.Cancel();
        _brandingRead?.Dispose();
        _brandingRead = null;
        _serverWordmark = null;
        _serverBrandName = null;
    }

    private async Task LoadBrandingAsync(MonitorAuthorityLease lease)
    {
        var settings = App.Services.GetService<SettingsApi>();
        var client = App.Services.GetService<SiloApiClient>();
        var images = App.Services.GetService<ImageService>();
        var http = App.Services.GetService<HttpClient>();
        if (settings == null || client == null || images == null || http == null) return;
        var version = ++_brandingVersion;
        _brandingRead?.Cancel();
        _brandingRead?.Dispose();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        _brandingRead = read;
        read.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var server = lease.Context.BaseUrl;
            var branding = await settings.GetServerBrandingAsync(read.Token);
            if (!lease.IsCurrent || !ReferenceEquals(lease, _lease) || version != _brandingVersion) return;
            var light = ThemeService.IsLightAppearance(App.Services.GetService<ThemeService>()?.CurrentTheme ?? "midnight-cinema");
            var asset = BrandingAssetSelector.Select(branding, light);
            var url = client.ResolveServerUrl(asset.WordmarkUrl);
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? await images.GetImageDiskPathAsync(server, "admin-server-wordmark", url!, http, read.Token) : null;
            if (!lease.IsCurrent || !ReferenceEquals(lease, _lease) || version != _brandingVersion) return;
            var wordmark = string.IsNullOrWhiteSpace(path) ? null : await WordmarkSourceAsync(path, read.Token);
            if (!lease.IsCurrent || !ReferenceEquals(lease, _lease) || version != _brandingVersion) return;
            _serverWordmark = wordmark;
            _serverBrandName = branding.ServerName;
            RefreshSidebars();
        }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException or ApiException or IOException or System.Text.Json.JsonException)
        {
            // Supplemental branding must never block foreground navigation.
        }
        finally { if (ReferenceEquals(_brandingRead, read)) _brandingRead = null; }
    }
}
