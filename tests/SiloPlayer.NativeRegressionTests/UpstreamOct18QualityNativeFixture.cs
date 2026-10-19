using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

internal static class UpstreamOct18QualityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var player = SiloPlayer.App.Services.GetRequiredService<PlayerService>();
        var playerType = typeof(PlayerService);
        var field = playerType.GetField("_playbackManager", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = field.GetValue(player);
        var client = (SiloApiClient)playerType.GetProperty("_apiClient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
        var auth = (AuthService)playerType.GetProperty("_authService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
        using var manager = new PlaybackManager(new PlaybackApi(client), new CatalogApi(client), auth, client);
        field.SetValue(player, manager);
        var overlay = new PlayerOverlay { Width = 900, Height = 600 };
        parent.Children.Add(overlay);
        try
        {
            foreach (var legacy in new[] { false, true })
            {
                var qualities = new List<PlaybackQualityV3> { new() { Label = "1080p-medium", Height = 1080, BitrateKbps = 6000 } };
                if (legacy) qualities.Add(new() { Label = "original", PreservesSource = true });
                typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, new PlaybackStartResponse { ProtocolVersion = 3, AvailableQualities = qualities });
                playerType.GetField("_activeQualityTier", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, "auto");
                typeof(PlayerOverlay).GetMethod("PopulateQualityFlyout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, null);
                var panel = (StackPanel)overlay.FindName("QualityListPanel");
                var flyout = (Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase)overlay.FindName("QualityFlyout");
                flyout.ShowAt(overlay); await Task.Delay(80); panel.UpdateLayout();
                foreach (var button in panel.Children.OfType<Button>()) { button.ApplyTemplate(); button.UpdateLayout(); }
                await Task.Delay(80); panel.UpdateLayout();
                Program.Log($"TRACE quality panel {panel.ActualWidth}x{panel.ActualHeight}, children={panel.Children.Count}, logical={string.Join(";", panel.Children.OfType<Button>().Select(button => $"{button.ActualWidth}x{button.ActualHeight}/{button.Content?.GetType().Name}/{button.Visibility}"))}");
                await MediaParityNativeFixture.CaptureAsync(panel, $"oct18-quality-probe-{legacy}.png");
                var labels = All<TextBlock>(panel).Select(text => text.Text).ToArray();
                if (labels.Count(label => label == "Auto") != 1 || labels.Contains("Original") || !labels.Contains("1080p-medium"))
                    throw new InvalidOperationException("Actual quality picker must offer exactly one Auto plus fixed tiers on both old and current servers: " + string.Join(",", labels));
                await MediaParityNativeFixture.CaptureAsync(panel, $"oct18-quality-legacy-{legacy}.png"); flyout.Hide();
                for (var closing = 0; closing < 40 && panel.IsLoaded; ++closing) await Task.Delay(20);
                if (((TextBlock)overlay.FindName("QualityButtonText")).Text != "Auto") throw new InvalidOperationException("Current Auto intent label missing.");
                var cast = player.CaptureSiloCastState();
                var ids = cast["qualityOptions"]!.AsArray().Select(option => option!["id"]!.GetValue<string>()).ToArray();
                if (!ids.SequenceEqual(new[] { "auto", "1080p-medium" }) || cast["activeQualityId"]!.GetValue<string>() != "auto")
                    throw new InvalidOperationException("SiloCast must publish the same normalized quality choices and intent as the native picker.");
            }
            Program.Log("PASS: actual native quality picker offers one Auto and fixed tiers for current/legacy plans, with no Original choice.");
        }
        finally { overlay.Deactivate(); parent.Children.Remove(overlay); field.SetValue(player, previous); }
    }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T target) yield return target;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            foreach (var item in All<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
