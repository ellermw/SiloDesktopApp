using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;

internal static class WatchPartyLayoutNativeFixture
{
    internal static async Task RunAsync(MainWindow window, NavigationService navigation)
    {
        var scale = Environment.GetEnvironmentVariable("SILO_NATIVE_PARTY_TEXT_SCALE") ?? "default";
        var settings = App.Services.GetRequiredService<SettingsService>();
        var preferences = settings.Load(); preferences.UiTextScale = scale; settings.Save(preferences);
        var failures = new List<string>();
        var sizes = new List<string>();
        string phase = "origin";
        var rasterScale = ((FrameworkElement)window.Content).XamlRoot.RasterizationScale;
        if (double.TryParse(Environment.GetEnvironmentVariable("SILO_NATIVE_EXPECTED_RASTER_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var expectedScale) && Math.Abs(rasterScale - expectedScale) > .01)
            throw new InvalidOperationException($"Fixture raster scale {rasterScale} does not match required {expectedScale}.");
        Program.Log($"Watch Party fixture: raster scale {rasterScale}, origin {Environment.GetEnvironmentVariable("SILO_NATIVE_PARTY_ORIGIN") ?? "blank"}.");
        Microsoft.UI.Xaml.UnhandledExceptionEventHandler onUnhandled = (_, args) =>
        {
            failures.Add(args.Exception?.GetType().Name + " HRESULT=" + args.Exception?.HResult.ToString("x"));
            if (args.Message.Contains("Layout cycle", StringComparison.OrdinalIgnoreCase)) failures.Add("Layout cycle detected");
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "watch-party-layout-failure.json"), JsonSerializer.Serialize(new { phase, rasterScale, failures, sizes }));
            args.Handled = true;
        };
        Application.Current.UnhandledException += onUnhandled;
        long lastTick = Stopwatch.GetTimestamp();
        long sizeChanges = 0;
        string lastSize = "not arranged";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => Interlocked.Exchange(ref lastTick, Stopwatch.GetTimestamp());
        timer.Start();
        using var watchdog = new System.Threading.Timer(_ =>
        {
            var gap = Stopwatch.GetElapsedTime(Interlocked.Read(ref lastTick)).TotalMilliseconds;
            if (gap < 3000) return;
            var result = new { passed = false, gapMs = gap, sizeChanges = Interlocked.Read(ref sizeChanges), lastSize = Volatile.Read(ref lastSize), scope = "Synthetic full-frame Watch Party layout; watchdog exits only this test process" };
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "watch-party-layout.json"), JsonSerializer.Serialize(result));
            Program.Log("FAIL: Watch Party layout stopped servicing its dispatcher for over 3000ms.");
            Environment.Exit(1);
        }, null, 3000, 500);
        try
        {
            var origin = Environment.GetEnvironmentVariable("SILO_NATIVE_PARTY_ORIGIN") ?? "blank";
            if (origin == "home") { navigation.Navigate<HomePage>(); await Task.Delay(700); }
            else if (origin == "library")
            {
                navigation.Navigate<LibraryPage>(new LibraryPage.NavigationArgs(new SiloPlayer.Core.Models.Catalog.Library { Id = 901, Name = "Fixture library", Type = "movie" }, "Library"));
                await Task.Delay(700);
            }
            phase = "watch-party";
            navigation.Navigate<WatchTogetherJoinPage>();
            var page = (WatchTogetherJoinPage)navigation.Frame!.Content;
            typeof(WatchTogetherJoinPage).GetProperty("RecentPartyDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, Path.Combine(Program.ResultDirectory, "synthetic-recent-parties"));
            var panel = (Border)page.FindName("CreatePartyPanel");
            panel.SizeChanged += (_, args) =>
            {
                Interlocked.Increment(ref sizeChanges);
                Volatile.Write(ref lastSize, $"{args.NewSize.Width:F3}x{args.NewSize.Height:F3}");
                var glow = (Image)page.FindName("CreatePartyGlow");
                if (sizes.Count < 30) sizes.Add($"{lastSize}; image.Height={glow.Height:F3}; desired={glow.DesiredSize.Height:F3}; source={glow.Source != null}");
            };
            await Task.Delay(3500);
            if (!page.IsLoaded || panel.ActualWidth <= 0 || panel.ActualHeight <= 0)
                throw new InvalidOperationException("Watch Party did not acquire real frame bounds.");
            var decoration = (Image)page.FindName("CreatePartyGlow");
            if (decoration.Source == null || decoration.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Watch Party glow did not render.");
            // Lock down the root property even on CI displays at100%: a
            // decorative bitmap resize must not change the card's desired size.
            var stableWidth = panel.ActualWidth; var stableHeight = panel.ActualHeight;
            var originalWidth = decoration.Width; var originalHeight = decoration.Height;
            try
            {
                decoration.Width += 32; decoration.Height += 48;
                page.UpdateLayout(); await Task.Delay(100);
                if (Math.Abs(panel.ActualWidth - stableWidth) > .1 || Math.Abs(panel.ActualHeight - stableHeight) > .1)
                    throw new InvalidOperationException("Decorative glow resize changed the auto-sized Watch Party card.");
            }
            finally { decoration.Width = originalWidth; decoration.Height = originalHeight; }
            page.UpdateLayout(); await Task.Delay(50);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_PARTY_RESIZE") == "1")
            {
                foreach (var dimensions in new[] { (900, 600), (1920, 1080), (3440, 1440), (3840, 2160), (2560, 1369) })
                {
                    window.AppWindow.ResizeClient(new(dimensions.Item1, dimensions.Item2));
                    await Task.Delay(250);
                    if (failures.Count > 0 || panel.ActualHeight <= 0 || decoration.Source == null)
                        throw new InvalidOperationException("Watch Party layout failed while resizing.");
                }
            }
            var completed = new { passed = failures.Count == 0, scale, failures, sizeChanges, sizes, lastSize, rasterScale = page.XamlRoot.RasterizationScale, pageWidth = page.ActualWidth, pageHeight = page.ActualHeight,
                glowWidth = ((Image)page.FindName("CreatePartyGlow")).ActualWidth,
                glowHeight = ((Image)page.FindName("CreatePartyGlow")).ActualHeight };
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            phase = "navigation-away";
            navigation.Navigate<LibraryPage>(new LibraryPage.NavigationArgs(new SiloPlayer.Core.Models.Catalog.Library { Id = 901, Name = "Fixture library", Type = "movie" }, "Library"));
            await Task.Delay(200);
            if (navigation.Frame.Content is not LibraryPage library || !library.IsLoaded || library.ActualHeight <= 0)
                throw new InvalidOperationException("Library did not render after leaving Watch Party.");
            navigation.Navigate<HomePage>(); await Task.Delay(200);
            if (navigation.Frame.Content is not HomePage home || !home.IsLoaded || home.ActualHeight <= 0)
                throw new InvalidOperationException("Home did not render after leaving Watch Party.");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "watch-party-layout.json"), JsonSerializer.Serialize(completed));
            Program.Log("PASS: Watch Party layout settles with a visible glow, decoration cannot resize its card, and Library/Home render after navigation.");
        }
        finally
        {
            timer.Stop();
            Application.Current.UnhandledException -= onUnhandled;
        }
    }
}
