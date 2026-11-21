using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Views.Admin;

internal static partial class AdminMonitorShellNativeFixture
{
    private static async Task ShellBrandingAsync(Window window, Server server)
    {
        var wordmark = new Grid { Width = 240, Height = 48, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        wordmark.Children.Add(new TextBlock { Text = "Fixture Theater", FontSize = 24, FontFamily = new("Georgia"), Foreground = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod), VerticalAlignment = VerticalAlignment.Center });
        window.Content = wordmark;
        await Task.Delay(80);
        await CaptureAsync(wordmark, "synthetic-wordmark.png");
        server.WordmarkBytes = await File.ReadAllBytesAsync(Path.Combine(Program.ResultDirectory, "synthetic-wordmark.png"));
        var frame = new Frame();
        window.Content = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)frame.Content;
        foreach (var width in new[] { 1600, 800 })
        {
            window.AppWindow.ResizeClient(new(width, 1000));
            await Task.Delay(200);
            if (width == 800) typeof(AdminShellPage).GetMethod("OpenDrawer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            await Until(() => Descendants(shell).OfType<Image>().Any(image => AutomationProperties.GetAutomationId(image) == "AdminServerWordmark" && image.Source is BitmapImage { UriSource.IsFile: true }), 1500);
            var image = Descendants(shell).OfType<Image>().FirstOrDefault(image => AutomationProperties.GetAutomationId(image) == "AdminServerWordmark");
            Check(server.BrandingReads == 1, $"{width}: Admin reads the current server branding once per authority");
            Check(image?.Source is BitmapImage { UriSource.IsFile: true }, $"{width}: server branding replaces bundled Silo wordmark in desktop and drawer");
            await CaptureAsync(frame, $"admin-server-branding-{width}.png");
        }
        var services = SiloPlayer.App.Services;
        var auth = (AuthService)services.GetService(typeof(AuthService))!;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? Source() => Descendants(shell).OfType<Image>().FirstOrDefault(i => AutomationProperties.GetAutomationId(i) == "AdminServerWordmark")?.Source is BitmapImage bitmap ? bitmap.UriSource?.AbsoluteUri : null;
        var original = Source();
        try
        {
            server.HeldBranding = held;
            server.BrandingWordmarkUrl = "/retired-wordmark.png";
            var reads = server.BrandingReads;
            typeof(AdminShellPage).GetMethod("RefreshSettingsStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            await Until(() => server.BrandingReads > reads);
            server.BrandingWordmarkUrl = "/successor-wordmark.png";
            auth.SelectProfile("p2", null, new Profile { Id = "p2", Name = "Successor", IsPrimary = true });
            await Until(() => server.BrandingReads >= reads + 2);
            await Until(() => Source() != null && Source() != original && !Source()!.StartsWith("ms-appx:"));
            var successor = Source();
            held.TrySetResult(true);
            await Until(() => server.BrandingInFlight == 0);
            await Task.Delay(100);
            Check(successor != null && Source() == successor && !successor.StartsWith("ms-appx:"), "retired branding cannot replace the successor profile's asset");
        }
        finally { held.TrySetResult(true); await Until(() => server.BrandingInFlight == 0); }

        // BitmapImage cannot decode SVG: a valid vector wordmark (extensionless URL, cached as .img)
        // must render through SvgImageSource instead of failing over to bundled Silo.
        server.BrandingWordmarkUrl = "/vector-wordmark";
        var vectorReads = server.BrandingReads;
        window.AppWindow.ResizeClient(new(1600, 1000));
        typeof(AdminShellPage).GetMethod("RefreshSettingsStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
        await Until(() => server.BrandingReads > vectorReads);
        Image? Vector() => Descendants(shell).OfType<Image>().FirstOrDefault(candidate =>
            AutomationProperties.GetAutomationId(candidate) == "AdminServerWordmark" && candidate.ActualWidth > 0 && candidate.Source is SvgImageSource);
        await Until(() => Vector() != null);
        var matching = 0;
        for (var attempt = 0; attempt < 20 && matching < 100; attempt++)
        {
            await Task.Delay(100);
            if (Vector() is not { } vector) continue;
            var raster = new RenderTargetBitmap();
            await raster.RenderAsync(vector);
            var pixels = (await raster.GetPixelsAsync()).ToArray();
            matching = 0;
            for (var p = 0; p + 3 < pixels.Length; p += 4)
                if (pixels[p + 3] > 200 && pixels[p + 2] > 180 && pixels[p + 1] < 100 && pixels[p] < 130) matching++;
        }
        Check(Vector() != null && matching >= 100, $"SVG server wordmark renders through an SVG-capable source instead of bundled Silo ({matching} expected pixels)");
        await CaptureAsync(frame, "admin-server-branding-svg.png");
    }
}
