using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;

internal static partial class ArtworkMotionNativeFixture
{
    private static async Task OverlayFirstMountCasesAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        foreach (var variant in new[] { "custom", "profile-disabled", "server-disabled" })
        {
            using var wire = new OverlaySettingsWire(variant);
            using var http = new HttpClient(wire);
            var client = new SiloApiClient(http); client.SetBaseUrl("https://overlay-first-mount.invalid");
            var overlays = new CardOverlayService(new SettingsApi(client));
            field.SetValue(null, new OverlayServices(previous, overlays));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            MediaItem Item(string id) => new()
            {
                ContentId = "overlay-first-mount-" + variant + "-" + id,
                Title = id, Type = "movie", Year = 2025,
                PosterUrl = "https://artwork-motion.invalid/art/overlay-" + variant + "-" + id,
                BackdropUrl = "https://artwork-motion.invalid/art/overlay-" + variant + "-" + id,
                OverlaySummary = new() { Resolution = "2160p" }, UserState = new(),
            };
            // The first card owns the shared initialization, then really unloads
            // while two other pre-Loaded binds still wait for its settings result.
            var retired = new PosterCard { MediaItem = Item("retired") };
            var poster = new PosterCard { MediaItem = Item("poster") };
            var landscape = new LandscapeCard { MediaItem = Item("landscape") };
            try
            {
                await Until(() => wire.Waiting);
                Check(!overlays.IsLoaded && BadgeText(poster).Contains("4K") && BadgeText(landscape).Contains("4K"),
                    variant + " starts with visible default badges before overlay preferences load");
                row.Children.Add(retired); row.Children.Add(poster); row.Children.Add(landscape);
                parent.Children.Insert(0, row);
                await Until(() => retired.IsLoaded && poster.IsLoaded && landscape.IsLoaded);
                row.Visibility = Visibility.Collapsed;
                row.Visibility = Visibility.Visible; // Artwork activity must not retire overlay waits.
                row.Children.Remove(retired);
                await Until(() => !retired.IsLoaded);
                await Task.Delay(100);
                wire.Release.TrySetResult();
                await Until(() => overlays.IsLoaded);
                await Task.Delay(150); // Drain the card continuations after service publication.
                var expected = variant == "custom" ? "2025" : "";
                Check(BadgeText(poster) == expected, variant + " PosterCard applies loaded overlay preferences without rebind");
                Check(BadgeText(landscape) == expected, variant + " LandscapeCard applies loaded overlay preferences without rebind");
                Check(((Button)poster.FindName("QuickWatchedButton")).Visibility == Visibility.Collapsed &&
                    ((Button)poster.FindName("QuickFavoriteButton")).Visibility == Visibility.Collapsed,
                    variant + " PosterCard applies disabled quick actions after initialization");
                Check(wire.CanceledRequests == 0 && wire.EffectiveRequests == 1,
                    variant + " mounting/activity/unloading never cancels or restarts shared settings initialization");
                Check(BadgeText(retired).Contains("4K"), variant + " real unload retires only that card's overlay wait");
                row.Children.Insert(0, retired);
                await Until(() => retired.IsLoaded);
                Check(BadgeText(retired) == expected, variant + " retained card refreshes overlays on reload without rebind");
            }
            finally
            {
                wire.Release.TrySetResult();
                await overlays.EnsureLoadedAsync();
                parent.Children.Remove(row);
                await Task.Delay(150);
                field.SetValue(null, previous);
            }
        }
    }

    private static string BadgeText(FrameworkElement card) => string.Join("|",
        new[] { "OverlayTopLeft", "OverlayTopRight", "OverlayBottomLeft", "OverlayBottomRight" }
            .SelectMany(name => ((Panel)card.FindName(name)).Children.OfType<Border>())
            .SelectMany(badge => ((StackPanel)badge.Child).Children.OfType<TextBlock>())
            .Select(text => text.Text));

    private sealed class OverlayServices(IServiceProvider fallback, CardOverlayService overlays) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(CardOverlayService) ? overlays : fallback.GetService(type);
    }

    private sealed class OverlaySettingsWire(string variant) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool Waiting;
        internal int CanceledRequests, EffectiveRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (path.EndsWith("/contract/capabilities"))
                body = new { api_version = 1, manifest_revision = 17, supports_batched_effective = true };
            else if (path.EndsWith("/overlay-config"))
                body = new { enabled = variant != "server-disabled" };
            else if (path.EndsWith("/values/ui.card_overlays"))
                body = new { key = "ui.card_overlays", value = new { version = 2, preset = "classic", items = new
                {
                    resolution = new { enabled = false, position = "top-left" },
                    year = new { enabled = true, position = "bottom-right" },
                } } };
            else if (path.EndsWith("/values/effective"))
            {
                Interlocked.Increment(ref EffectiveRequests);
                Waiting = true;
                using var registration = ct.Register(() => Interlocked.Increment(ref CanceledRequests));
                await Release.Task.WaitAsync(ct);
                body = new { items = new object[]
                {
                    new { key = "ui.card_overlays_enabled", value = variant != "profile-disabled" },
                    new { key = "ui.card_quick_actions_enabled", value = false },
                    new { key = "ui.card_quick_actions", value = "none" },
                } };
            }
            else throw new InvalidOperationException("Unexpected overlay fixture request: " + request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
        }
    }
}
