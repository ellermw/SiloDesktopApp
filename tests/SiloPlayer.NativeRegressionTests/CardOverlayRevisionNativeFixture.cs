using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static class CardOverlayRevisionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        var service = new CardOverlayService(new SettingsApi(new SiloApiClient(new HttpClient())));
        Set(service, "_initialized", true);
        typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 26);
        var prefs = OverlayRegistry.DefaultPrefs;
        foreach (var key in prefs.Keys.ToArray()) prefs[key] = prefs[key] with { Enabled = false };
        foreach (var id in new[] { "rating_imdb", "rating_tmdb", "rating_rt", "rating_rt_audience", "show_status" })
            prefs[id] = new(true, OverlayPosition.TopRight);
        foreach (var id in new[] { "resolution", "hdr", "audio", "video_codec" })
            prefs[id] = new(true, OverlayPosition.TopLeft);
        Set(service, "_document", new CardOverlayPrefs(2, "classic", [], prefs));
        servicesField.SetValue(null, new FixtureServices(previous, service));
        var surface = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Padding = new Thickness(8), Width = 1000 };
        parent.Children.Insert(0, surface);
        try
        {
            var item = new MediaItem
            {
                ContentId = "overlay-fixture", Type = "series", Title = "Overlay rails and status", Year = 2026,
                RatingImdb = 8.7, RatingTmdb = 8.4, RatingRtCritic = 96, RatingRtAudience = 92, ShowStatus = "Returning",
                OverlaySummary = new() { Resolution = "2160p", Hdr = "DV HDR10", Audio = "Atmos", VideoCodec = "HEVC" },
                UserState = new() { WatchedCount = 1, UnplayedCount = 3 },
            };
            var poster = new PosterCard { SuppressImageLoading = true, MediaItem = item };
            poster.SetCatalogGridLayout(185);
            var library = new LibraryGridCard();
            library.Bind(item, null);
            var wide = new LandscapeCard { MediaItem = item };
            surface.Children.Add(poster); surface.Children.Add(library); surface.Children.Add(wide);
            await Task.Delay(150); surface.UpdateLayout();
            CheckQuickRefresh(poster, library, item, service);
            CheckExternalCards(service);
            foreach (var card in new FrameworkElement[] { poster, wide })
            {
                var right = card.FindName("OverlayRightEdge") as StackPanel;
                Check(right is not null && right.Children.Count == 4, card.GetType().Name + " maps legacy top-right to a four-badge rail");
                Check(((Panel)card.FindName("OverlayTopRight")).Children.Count == 0, "status corner has no preference badges");
                Check(((Panel)card.FindName("OverlayTopLeft")).Children.Count == 3, "corners cap at three badges");
                var areaHeight = card == poster ? 185 * 1.5 : 177;
                var top = right!.TransformToVisual(card).TransformPoint(new Windows.Foundation.Point()).Y;
                Check(Math.Abs(top + right.ActualHeight / 2 - areaHeight / 2) < 2, "right rail is vertically centred");
            }
            var libraryRight = (StackPanel)Get(library, "_overlayRightEdge")!;
            Check(libraryRight.Children.Count == 4, "LibraryGridCard uses the same rail cap");
            var status = Status(poster);
            Check(status.Visibility == Visibility.Visible && AutomationProperties.GetName(status) == "3 episodes left", "poster exposes remaining episodes");
            Check(!status.IsHitTestVisible, "status never intercepts quick actions");
            var row = (StackPanel)poster.FindName("OverlayTopLeft");
            Check(row.MaxWidth <= poster.Width - status.ActualWidth - 2 * status.Margin.Right, "top-left row reserves status corner");
            Check(AutomationProperties.GetName((FrameworkElement)Get(library, "_watchStatus")!) == "3 episodes left", "library poster exposes status");
            Check(wide.FindName("WatchStatus") is null, "wide cards have no watch-status indicator");
            await MediaParityNativeFixture.CaptureAsync(surface, "overlay-revisions-rails.png");

            prefs["resolution_hdr_audio"] = new(true, Enum.Parse<OverlayPosition>("LeftEdge"));
            poster.RefreshState(); library.RefreshState();
            Check(((Panel)poster.FindName("OverlayTopLeft")).Children.Count == 1, "combined tech badge suppresses repeated parts and retains video codec");
            var leftRail = (StackPanel)poster.FindName("OverlayLeftEdge");
            Check(leftRail.Children.Count == 1 && ((StackPanel)((Border)leftRail.Children[0]).Child).Children.OfType<TextBlock>().Single().Text == "4K DV ATMOS",
                "combined badge uses available resolution, HDR and audio");
            Set(service, "_manifestUnavailable", true);
            typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 0);
            try
            {
                poster.RefreshState(); library.RefreshState();
                Check(((Panel)poster.FindName("OverlayTopLeft")).Children.Count == 1 &&
                    ((Panel)Get(library, "_overlayTopLeft")!).Children.Count == 1,
                    "unavailable capabilities still suppress standalone technical badges on both poster hosts");
                Check(leftRail.Children.Count == 1 && ((Panel)Get(library, "_overlayLeftEdge")!).Children.Count == 1,
                    "unavailable capabilities retain the combined technical badge");
            }
            finally
            {
                Set(service, "_manifestUnavailable", false);
                typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 26);
            }
            foreach (var width in new[] { 96d, 300d, 185d })
            {
                poster.SetCatalogGridLayout(width);
                await Task.Delay(40); surface.UpdateLayout();
                Check(Math.Abs(status.ActualHeight - 22 * (width - 2) / 185) < 1, "status scales with poster width " + width);
            }
            item.UserState.UnplayedCount = 1234;
            poster.RefreshState();
            await Task.Delay(40); surface.UpdateLayout();
            Check(row.MaxWidth + status.ActualWidth + 2 * status.Margin.Right < poster.Width, "top row reserves full width of large remaining counts");
            await CheckEditorAsync(service);

            Set(service, "_enabled", false);
            item.UserState.Played = true;
            poster.RefreshState(); library.RefreshState();
            Check(status.Visibility == Visibility.Visible && AutomationProperties.GetName(status) == "Watched", "watched status survives disabled badges");
            Check(((Panel)poster.FindName("OverlayTopLeft")).Children.Count == 0, "badge disable actually removes badges");
            poster.Focus(FocusState.Programmatic);
            await Task.Delay(60);
            var actions = (StackPanel)poster.FindName("QuickActions");
            var button = (Button)poster.FindName("QuickWatchedButton");
            Check(actions.Opacity == 1 && actions.IsHitTestVisible && button.Visibility == Visibility.Visible && button.IsEnabled,
                "watched quick action remains available on focus alongside status");
            await MediaParityNativeFixture.CaptureAsync(surface, "overlay-revisions-watched-disabled.png");
            item.UserState.Played = false; item.UserState.WatchedCount = 0;
            poster.RefreshState(); library.RefreshState();
            Check(status.Visibility == Visibility.Collapsed && ((FrameworkElement)Get(library, "_watchStatus")!).Visibility == Visibility.Collapsed,
                "untouched shows do not display remaining episodes");
            poster.DeferOverlayLoading = true;
            poster.MediaItem = new() { ContentId = "deferred", Type = "movie", Title = "Deferred badges", UserState = new() { Played = true } };
            Check(status.Visibility == Visibility.Visible && AutomationProperties.GetName(status) == "Watched", "status does not wait for deferred badges");
            poster.MediaItem = null;
            Check(status.Visibility == Visibility.Collapsed, "recycled placeholder clears status");
            library.Reset();
            Check(((FrameworkElement)Get(library, "_watchStatus")!).Visibility == Visibility.Collapsed, "library placeholder clears status");
            Program.Log("PASS: overlay revision cards, rail limits/centering, status/disable/recycle and quick-action interaction.");
        }
        finally
        {
            parent.Children.Remove(surface);
            await Task.Delay(100);
            servicesField.SetValue(null, previous);
        }
    }

    private static void CheckQuickRefresh(PosterCard poster, LibraryGridCard library, MediaItem item, CardOverlayService service)
    {
        var posterRow = (StackPanel)poster.FindName("OverlayTopLeft");
        var libraryRow = (StackPanel)Get(library, "_overlayTopLeft")!;
        var posterWidth = posterRow.MaxWidth; var libraryWidth = libraryRow.MaxWidth;
        item.UserState!.UnplayedCount = 12345;
        typeof(PosterCard).GetMethod("UpdateQuickActionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(poster, [item, false]);
        typeof(LibraryGridCard).GetMethod("UpdateQuickActionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(library, [item]);
        Check(posterRow.MaxWidth < posterWidth && libraryRow.MaxWidth < libraryWidth, "quick action refresh recomputes remaining-count reservations");
        var posterRail = (StackPanel)poster.FindName("OverlayRightEdge");
        var libraryRail = (StackPanel)Get(library, "_overlayRightEdge")!;
        Check(posterRail.Children.Count > 0 && posterRail.Margin.Right == 0 && libraryRail.Children.Count > 0 && libraryRail.Margin.Right == 0,
            "quick action refresh keeps classic rail badges flush with the card edge");
        typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 24);
        poster.RefreshState(); library.RefreshState();
        item.UserState.WatchedCount = item.UserState.UnplayedCount = 0;
        typeof(PosterCard).GetMethod("UpdateQuickActionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(poster, [item, false]);
        typeof(LibraryGridCard).GetMethod("UpdateQuickActionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(library, [item]);
        Check(((StackPanel)poster.FindName("OverlayTopRight")).Margin.Top == posterRow.Margin.Top &&
            ((StackPanel)Get(library, "_overlayTopRight")!).Margin.Top == libraryRow.Margin.Top, "legacy corner shift resets after the status disappears");
        Check(Math.Abs(posterRow.MaxWidth - (poster.Width - 2 * posterRow.Margin.Left)) < 0.01 &&
            Math.Abs(libraryRow.MaxWidth - (library.Width - 2 - 2 * libraryRow.Margin.Left)) < 0.01,
            "no-count cards use the full top-left badge width without a hidden status reservation or gap");
        item.UserState.WatchedCount = 1; item.UserState.UnplayedCount = 3;
        typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 26);
        poster.RefreshState(); library.RefreshState();
    }

    private static void CheckExternalCards(CardOverlayService service)
    {
        var old = Get(service, "_document")!;
        try
        {
            var prefs = OverlayRegistry.DefaultPrefs;
            foreach (var key in prefs.Keys.ToArray()) prefs[key] = prefs[key] with { Enabled = false };
            prefs["year"] = new(true, OverlayPosition.LeftEdge);
            prefs["rating_tmdb"] = new(true, OverlayPosition.TopRight);
            Set(service, "_document", new CardOverlayPrefs(2, "classic", [], prefs));
            foreach (var revision in new[] { 24, 25, 26 })
            {
                typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, revision);
                var card = ExternalTitleCard.Build(new SiloPlayer.Core.Models.Requests.WatchlistTitle
                    { Title = "External rails", Year = 2026, VoteAverage = 8.2 }, 185, watchlistCard: true);
                var poster = (Grid)card.Children.OfType<Border>().First().Child;
                var stacks = poster.Children.OfType<StackPanel>().ToArray();
                Check(stacks.Any(p => p.HorizontalAlignment == HorizontalAlignment.Left && p.VerticalAlignment == (revision >= 25 ? VerticalAlignment.Center : VerticalAlignment.Top)),
                    "external watchlist left edge matches revision " + revision);
                Check(stacks.Any(p => p.HorizontalAlignment == HorizontalAlignment.Right && p.VerticalAlignment == (revision >= 25 ? VerticalAlignment.Center : VerticalAlignment.Top)),
                    "external watchlist top-right migration matches revision " + revision);
            }
        }
        finally { Set(service, "_document", old); typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 26); }
    }

    private static FrameworkElement Status(PosterCard card) => (FrameworkElement)(card.FindName("WatchStatus")
        ?? throw new InvalidOperationException("PosterCard has no watch-status corner."));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Program.Log("PASS: " + message);
    }
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static Task CheckEditorAsync(CardOverlayService service)
    {
        var page = new SettingsPage();
        Set(page, "_overlayShownRatings", new HashSet<string> { "imdb", "tmdb", "rt_critic", "rt_audience" });
        Set(page, "_cardOverlayPreviewVariant", "show");
        var rowBuilder = typeof(SettingsPage).GetMethod("BuildCardOverlayRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var controlsBuilder = typeof(SettingsPage).GetMethod("BuildCardOverlayControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var preview = typeof(SettingsPage).GetMethod("UpdateCardOverlayPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var revision in new[] { 25, 26, 24 })
        {
            typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, revision);
            var document = service.GetDocument();
            Set(page, "_cardOverlayDraft", document);
            controlsBuilder.Invoke(page, null);
            var texts = Texts((Panel)page.FindName("CardOverlayControlsContainer")).ToArray();
            Check(texts.Contains("Resolution + HDR + Audio (combined)") == (revision >= 26), "editor offers combined badge only at revision 26");
            var row = (Grid)rowBuilder.Invoke(page, [OverlayRegistry.All.Single(d => d.Id == "rating_imdb"), document.Items["rating_imdb"]])!;
            var combo = row.Children.OfType<StackPanel>().Last().Children.OfType<ComboBox>().Single();
            Check(combo.Items.Count == (revision >= 25 ? 5 : 4) &&
                ((ComboBoxItem)combo.SelectedItem).Tag!.Equals(document.Items["rating_imdb"].Position), "editor position choices match manifest and effective default");
            preview.Invoke(page, null);
            Check(((Panel)page.FindName(revision >= 25 ? "OverlayPreviewRightEdge" : "OverlayPreviewTopRight")).Children.Count == (revision >= 25 ? 4 : 3),
                "preview uses the same rail and corner caps");
        }
        typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, 26);
        return Task.CompletedTask;
    }
    private static IEnumerable<string> Texts(UIElement element)
    {
        if (element is TextBlock text) yield return text.Text;
        var children = element is Panel panel ? panel.Children.Cast<UIElement>()
            : element is Border { Child: { } child } ? new[] { child }
            : Enumerable.Empty<UIElement>();
        foreach (var next in children)
        foreach (var value in Texts(next)) yield return value;
    }
    private sealed class FixtureServices(IServiceProvider fallback, CardOverlayService overlays) : IServiceProvider
    {
        private readonly SiloPlayer.Helpers.NavigationService navigation = new();
        public object? GetService(Type type) => type == typeof(CardOverlayService) ? overlays : type == typeof(SiloPlayer.Helpers.NavigationService) ? navigation : fallback.GetService(type);
    }
}
