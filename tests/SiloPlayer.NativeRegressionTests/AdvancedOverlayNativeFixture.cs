using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;
using Windows.Foundation;

internal static class AdvancedOverlayNativeFixture
{
    private static readonly MethodInfo Build = typeof(PosterCard).GetMethod("BuildAdvancedBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo Services = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
    internal static async Task RunAsync(StackPanel parent)
    {
        var previous = (IServiceProvider)Services.GetValue(null)!;
        using var wire = new ImageWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid"); client.SetProfile("overlay-fixture", "synthetic-profile-token");
        var service = new CardOverlayService(new SettingsApi(client)); await service.EnsureLoadedAsync();
        Services.SetValue(null, new FixtureServices(previous, service, client));
        var surface = new StackPanel { Width = 1000, Spacing = 12, Padding = new Thickness(8), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 40, 48, 62)) };
        parent.Children.Insert(0, surface);
        try
        {
            await CheckStylesAsync(surface);
            foreach (var preset in new[] { "minimal", "classic", "vibrant", "pill", "square", "plate", "band" })
            {
                surface.Children.Clear();
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
                surface.Children.Add(new TextBlock { Text = preset }); surface.Children.Add(row);
                foreach (var width in new[] { 128d, 185, 280 })
                {
                    Configure(service, preset);
                    var item = Item();
                    var poster = new PosterCard { SuppressImageLoading = true, MediaItem = item };
                    poster.SetCatalogGridLayout(width); row.Children.Add(poster);
                    await PauseAsync(surface);
                    Check(!Descendants(poster).OfType<Border>().Any(badge => badge.Tag is string && badge.IsHitTestVisible), "badges do not intercept card actions");
                    var rail = (Panel)poster.FindName("OverlayLeftEdge");
                    Check(rail.Children.Count == 2, "actual poster rail retains source badges " + preset + "/" + width);
                    if (preset == "plate")
                        Check(Texts(rail).Contains("IMDB") && Texts(rail).Contains("8.7"), "plate renders a separate source chip and complete score");
                    if (preset == "band")
                    {
                        var marks = Descendants(poster).OfType<Panel>().Where(panel => panel.GetType().Name == "OverlayBandMarks").ToArray();
                        Check(marks.Length >= 2, "band uses shared top and bottom strips");
                        Check(marks.SelectMany(mark => mark.Children.OfType<FrameworkElement>()).Any(mark => mark.Opacity == 0), "band drops complete overflow marks " + width);
                    }
                    else
                    {
                        var rows = Descendants(poster).OfType<Panel>().Single(panel => panel.GetType().Name == "OverlayBottomRows");
                        Check(rows.Children.Count == 4, "bottom corners share one measured rows panel");
                        CheckRows(rows);
                        if (width == 128)
                        {
                            Check(Enumerable.Range(0, 2).Any(index =>
                            {
                                var left = (FrameworkElement)rows.Children[index];
                                var right = (FrameworkElement)rows.Children[index + 2];
                                var leftTop = left.TransformToVisual(rows).TransformPoint(new Point()).Y;
                                var rightTop = right.TransformToVisual(rows).TransformPoint(new Point()).Y;
                                return rightTop >= leftTop + left.ActualHeight - .5;
                            }), "narrow bottom pair wraps into distinct complete rows");
                        }
                    }
                }
                await MediaParityNativeFixture.CaptureAsync(surface, "advanced-presets-" + preset + ".png");
            }
            foreach (var preset in new[] { "plate", "band" }) await CheckHostTypesAsync(surface, service, preset);
            await CheckImagesAsync(surface, service, wire, client);
            await CheckSettingsAsync(surface, service, wire, client);
        }
        finally { parent.Children.Remove(surface); await Task.Delay(40); Services.SetValue(null, previous); }
        await CardOverlayRevisionNativeFixture.RunAsync(parent);
        await OverlayPillNativeFixture.RunAsync(parent);
    }

    private static async Task CheckStylesAsync(StackPanel surface)
    {
        var style = new OverlayStyle { Size = 1.5, Shape = "square", Background = "#10203080", TextColor = "#abcdef", BorderColor = "#ff000080", BorderWidth = 3, FontWeight = "regular", Uppercase = false, Shadow = true };
        var badge = Badge("Mixed Case", "year", new(true, OverlayPosition.TopLeft), "plate", style: style);
        surface.Children.Add(badge); await PauseAsync(surface);
        var text = Descendants(badge).OfType<TextBlock>().Single(text => text.Text == "Mixed Case");
        Check(text.Text == "Mixed Case" && text.FontSize == 18 && text.FontWeight.Weight == 400, "native style size, regular weight and uppercase false");
        Check(badge.CornerRadius.TopLeft == 2 && badge.BorderThickness.Top == 3, "native style square shape and border width");
        Check(((SolidColorBrush)badge.Background).Color.A == 128 && ((SolidColorBrush)badge.BorderBrush).Color.R == 255, "native RGBA fill and border use last-byte alpha");
        Check(((SolidColorBrush)text.Foreground).Color.B == 239 && badge.Shadow is not null, "native text color and enabled shadow");
        var plain = Badge("Case", "year", new(true, OverlayPosition.TopLeft), "plate", style: style with { Shape = "pill", FontWeight = "extrabold", Uppercase = true, Shadow = false });
        surface.Children.Add(plain); await PauseAsync(surface);
        Check(Descendants(plain).OfType<TextBlock>().Single(text => text.Text == "CASE").FontWeight.Weight == 800 && plain.Shadow is null, "native extrabold weight and disabled shadow");
        Check(Math.Abs(plain.CornerRadius.TopLeft - plain.ActualHeight / 2) < .1, "style pill follows measured circular ends");
        await MediaParityNativeFixture.CaptureAsync(surface, "advanced-style-overrides.png"); surface.Children.Clear();
    }

    private static async Task CheckHostTypesAsync(StackPanel surface, CardOverlayService service, string preset)
    {
        surface.Children.Clear(); Configure(service, preset);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 }; surface.Children.Add(row);
        var library = new LibraryGridCard(); library.Bind(Item(), null); row.Children.Add(library);
        var wide = new LandscapeCard { MediaItem = Item() }; row.Children.Add(wide);
        var external = ExternalTitleCard.Build(new WatchlistTitle { Title = "External watchlist", Year = 2026, VoteAverage = 8.2, ContentRating = "TV-MA" }, 185, watchlistCard: true);
        row.Children.Add(external);
        var episodeHost = new Grid { Width = 300, Height = 169, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 50, 60, 70)) };
        surface.Children.Add(episodeHost);
        var detail = new ItemDetailPage();
        typeof(ItemDetailPage).GetMethod("AddEpisodeCardOverlays", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(detail,
            [episodeHost, new Episode { Runtime = 1800, OverlaySummary = new() { Resolution = "2160p", Hdr = "DV HDR10", Audio = "Atmos", ReleaseType = "WEB-DL", Edition = "Episode version" } }]);
        var watchlist = new WatchlistPage();
        watchlist.ViewModel.ExternalTitles.Clear();
        watchlist.ViewModel.ExternalTitles.Add(new WatchlistTitle { Title = "Dedicated watchlist", Year = 2026, VoteAverage = 8.2, ContentRating = "TV-MA" });
        var repeater = (ItemsRepeater)watchlist.FindName("ExternalRepeater");
        Detach((DependencyObject)watchlist.Content, repeater); repeater.Visibility = Visibility.Visible;
        surface.Children.Add(repeater);
        await PauseAsync(surface);
        var dedicated = (FrameworkElement)repeater.GetOrCreateElement(0);
        await PauseAsync(surface);
        foreach (var host in new FrameworkElement[] { library, wide, external, episodeHost })
            Check(Descendants(host).Any(element => element.GetType().Name == "OverlayBottomRows"), host.GetType().Name + " uses real shared bottom layout");
        Check(Descendants(dedicated).Any(element => element.GetType().Name == "OverlayBottomRows"), "dedicated watchlist realizes its actual shared overlay host");
        Check(Descendants(wide).OfType<TextBlock>().Any(text => text.FontSize == (preset == "band" ? 12.5 : 12)), "wide badge geometry stays fixed for " + preset);
        if (preset == "band")
            foreach (var host in new FrameworkElement[] { library, wide, episodeHost })
                Check(Descendants(host).Any(element => element.GetType().Name == "OverlayBandMarks"), host.GetType().Name + " uses actual band strips");
        await MediaParityNativeFixture.CaptureAsync(surface, "advanced-host-types-" + preset + ".png"); surface.Children.Clear();
    }

    private static async Task CheckImagesAsync(StackPanel surface, CardOverlayService service, ImageWire wire, SiloApiClient client)
    {
        var config = new OverlayItemConfig(true, OverlayPosition.TopLeft) { Image = wire.Ref };
        var icon = Badge("8.7", "rating_imdb", config, "plate", images: service);
        surface.Children.Add(icon); await UntilAsync(() => Descendants(icon).OfType<Image>().Any(image => image.Source is not null));
        Check(Texts(icon).SequenceEqual(new[] { "8.7" }), "uploaded source image replaces source chip and retains complete score");
        var whole = Badge("4K", "resolution", config with { ValueImages = new Dictionary<string, string> { ["2160p"] = wire.Ref } }, "plate", valueKey: "2160p", images: service);
        surface.Children.Add(whole); await UntilAsync(() => whole.Child is Image);
        Check(whole.Padding.Left == 0 && whole.BorderThickness.Top == 0 && ((Image)whole.Child).Height == 32, "exact raw value image replaces full badge at official 1.6x height");
        Check(whole.CornerRadius.TopLeft == 0 && whole.Background is null && whole.Shadow is null, "whole-value artwork stays unframed and unshadowed");
        Check(wire.Reads == 1, "real source and value badges coalesce authenticated wire image reads");
        var noMatch = Badge("4K", "resolution", new(true, OverlayPosition.TopLeft) { ValueImages = new Dictionary<string, string> { ["4K"] = wire.Ref } }, "plate", valueKey: "2160p", images: service);
        surface.Children.Add(noMatch); await PauseAsync(surface);
        Check(noMatch.Child is StackPanel && !Descendants(noMatch).OfType<Image>().Any(), "display label cannot accidentally match raw value image");
        wire.Supports = false; await service.RefreshPreferencesAsync();
        var pending = Badge("Ready", "year", config, "plate", images: service); surface.Children.Add(pending); await PauseAsync(surface);
        Check(!Descendants(pending).OfType<Image>().Any(), "pending image capability leaves the live control plain");
        wire.Supports = true; await service.RefreshPreferencesAsync();
        await UntilAsync(() => Descendants(pending).OfType<Image>().Any(image => image.Source is not null));
        Check(true, "live control retries its image after capability refresh without recreation");
        foreach (var failure in new[] { "missing", "unauthorized", "decode", "oversize", "dimensions" })
        {
            wire.Failure = failure; await service.RefreshPreferencesAsync();
            var reference = wire.CurrentRef;
            var fallback = Badge("1080p", "resolution", new(true, OverlayPosition.TopLeft) { Image = reference, ValueImages = new Dictionary<string, string> { ["1080p"] = reference } }, "plate", valueKey: "1080p", images: service);
            surface.Children.Add(fallback); await PauseAsync(surface); await Task.Delay(100);
            Check(fallback.Child is StackPanel && Texts(fallback).Contains("1080P"), "image " + failure + " failure retains the plain badge");
        }
        wire.Failure = null; await service.RefreshPreferencesAsync();
        wire.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = Badge("Owner", "year", config, "plate", images: service); surface.Children.Add(stale);
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.SetProfile("next-synthetic-profile", "next-synthetic-token"); wire.Hold.TrySetResult(); await Task.Delay(100);
        Check(!Descendants(stale).OfType<Image>().Any(), "late image cannot cross profile ownership");
        await MediaParityNativeFixture.CaptureAsync(surface, "advanced-images-fallback.png");
        client.SetProfile("overlay-fixture", "synthetic-profile-token"); await service.RefreshPreferencesAsync(); surface.Children.Clear();
    }

    private static async Task CheckSettingsAsync(StackPanel surface, CardOverlayService service, ImageWire wire, SiloApiClient client)
    {
        Configure(service, "plate");
        var page = new SettingsPage();
        Set(page, "_cardOverlayDraftOwner", client.CaptureIdentityContext());
        Set(page, "_overlayShownRatings", new HashSet<string> { "imdb", "tmdb", "rt_critic", "rt_audience" });
        var update = typeof(SettingsPage).GetMethod("UpdateCardOverlayPreview", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var presets = typeof(SettingsPage).GetMethod("BuildCardOverlayPresetCards", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var revision in new[] { 24, 25, 26 })
        {
            typeof(CardOverlayService).GetProperty("ManifestRevision")!.SetValue(service, revision);
            Set(page, "_cardOverlayDraft", service.GetDocument()); presets.Invoke(page, null); update.Invoke(page, null);
            var choices = (Panel)page.FindName("CardOverlayPresetHost"); var looks = (Panel)page.FindName("AdvancedOverlayLookHost");
            Check(choices.Children.Count == (revision >= 25 ? 7 : 5), "settings advanced preset gating revision " + revision);
            Check((looks.Children.Count > 0) == (revision >= 25), "settings advanced style gating revision " + revision);
        }
        var presetHost = (Panel)page.FindName("CardOverlayPresetHost");
        Detach((DependencyObject)page.Content, presetHost);
        surface.Children.Add(presetHost);
        await PauseAsync(surface);
        foreach (var preset in new[] { "minimal", "classic", "vibrant", "pill", "square", "band", "plate" })
        {
            Invoke(presetHost.Children.OfType<Button>().Single(button => button.Tag as string == preset));
            await UntilAsync(() => ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Preset == preset, "native preset selection did not update the draft");
        }
        Check(true, "all seven actual preset controls update the draft and preview");
        surface.Children.Remove(presetHost);
        var preview = (FrameworkElement)page.FindName("NavigationPreviewPoster");
        Detach((DependencyObject)page.Content, preview); surface.Children.Add(preview);
        await PauseAsync(surface);
        Check(Descendants(preview).Any(element => element.GetType().Name == "OverlayBottomRows"), "settings preview invokes the real shared layout");
        var lookHost = (Panel)page.FindName("AdvancedOverlayLookHost");
        // TextBox.TextChanged is delivered through the mounted native control.
        // Exercise the visible editor instead of editing an unmounted page tree.
        Detach((DependencyObject)page.Content, lookHost);
        surface.Children.Add(new ScrollViewer { Content = lookHost, Width = 420, Height = 320 });
        await UntilAsync(() => lookHost.IsLoaded, "settings look controls did not mount");
        await PauseAsync(surface);
        lookHost.Children.OfType<NumberBox>().First().Value = 1.75;
        Check(((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style?.Size == 1.75, "actual native size control updates the style draft and preview");
        var fill = lookHost.Children.OfType<TextBox>().First();
        fill.Text = "#112233";
        await Task.Delay(40);
        var rejectedRgbFill = ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style?.Background is null;
        Check(rejectedRgbFill, "native fill editor rejects a six-digit RGB fill");
        fill.Text = "#11223380";
        await UntilAsync(() => ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style?.Background == "#11223380", "native fill editor did not accept eight-digit RGBA");
        var acceptedRgbaFill = ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style?.Background == "#11223380";
        var borderWidth = lookHost.Children.OfType<NumberBox>().Last();
        Check(borderWidth.SmallChange == 1, "border-width spinner uses the official integer step");
        borderWidth.Value = 1;
        borderWidth.Value += borderWidth.SmallChange;
        Check(((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style?.BorderWidth == 2, "border-width increment stays integral");
        borderWidth.Value = 1.25;
        Check(borderWidth.Value == 1 && ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style?.BorderWidth == 1,
            "typed fractional border width normalizes to an integer before saving");
        borderWidth.Value = 3;
        void Select(string header, string tag)
        {
            var choice = lookHost.Children.OfType<ComboBox>().Single(box => box.Header as string == header);
            choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == tag);
        }
        Select("Shape", "square"); Select("Font weight", "regular"); Select("Uppercase", "false"); Select("Shadow", "true");
        lookHost.Children.OfType<TextBox>().Single(box => box.Header as string == "Text color").Text = "#abcdef";
        lookHost.Children.OfType<TextBox>().Single(box => box.Header as string == "Border color").Text = "#ff000080";
        await UntilAsync(() => ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style is { BorderWidth: 3, Shape: "square", FontWeight: "regular", Uppercase: false, Shadow: true, TextColor: "#abcdef", BorderColor: "#ff000080" }, "native style editors did not update the draft");
        await PauseAsync(surface);
        var previewBadge = Descendants(preview).OfType<Border>().First(border => border.Tag as string == "hdr");
        Check(previewBadge.Shadow is not null && previewBadge.BorderThickness.Top > 0
            && Descendants(previewBadge).OfType<TextBlock>().Any(text => text.FontWeight.Weight == 400 && ((SolidColorBrush)text.Foreground).Color.B == 239), "actual style editors drive the shared rendered preview");
        Invoke(lookHost.Children.OfType<Button>().Single(button => button.Content as string == "Use preset look"));
        await UntilAsync(() => ((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Style == new OverlayStyle(), "native preset-look reset did not clear style overrides");
        Check(true, "actual preset-look reset restores all preset defaults");
        var rowBuilder = typeof(SettingsPage).GetMethod("BuildCardOverlayRow", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var row = (Grid)rowBuilder.Invoke(page, [OverlayRegistry.All.Single(def => def.Id == "resolution"), service.GetDocument().Items["resolution"]])!;
        surface.Children.Add(row); await PauseAsync(surface);
        var images = row.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<Button>()).Single(button => button.Content as string == "Images");
        var flyout = (Flyout)images.Flyout; flyout.ShowAt(images); await Task.Delay(80);
        var imageControls = (StackPanel)((ScrollViewer)flyout.Content).Content;
        var imageChoice = imageControls.Children.OfType<ComboBox>().First(); imageChoice.SelectedIndex = 1;
        Check(((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Items["resolution"].Image == service.Images[0].Ref, "actual image-library selector updates the image preference");
        imageControls.Children.OfType<TextBox>().Single().Text = "2160p";
        imageControls.Children.OfType<ComboBox>().Last().SelectedIndex = 1;
        var add = imageControls.Children.OfType<Button>().Single(button => button.Content as string == "Add value image");
        Check(add.IsEnabled, "exact raw key and image enable explicit native assignment");
        Invoke(add);
        await Task.Delay(40);
        Check(((CardOverlayPrefs)Get(page, "_cardOverlayDraft")!).Items["resolution"].ValueImages!["2160p"] == service.Images[0].Ref, "actual value-image selector assigns an exact raw key");
        flyout.Hide();
        await UntilAsync(() => Descendants(preview).OfType<Border>().Any(border => border.Tag as string == "resolution" && border.Child is Image));
        ((CancellationTokenSource?)Get(page, "_overlaySaveCts"))?.Cancel();
        await MediaParityNativeFixture.CaptureAsync(surface, "advanced-settings-preview.png");
        var writes = wire.Saves;
        lookHost.Children.OfType<NumberBox>().First().Value = 1.5;
        client.SetProfile("retired-editor-profile", "synthetic-retired-token");
        lookHost.Children.OfType<NumberBox>().First().Value = 2;
        var draft = (CardOverlayPrefs)Get(page, "_cardOverlayDraft")!;
        typeof(SettingsPage).GetMethod("SetCardOverlayPreset", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, ["band"]);
        typeof(SettingsPage).GetMethod("UpdateCardOverlayItem", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, ["resolution", draft.Items["resolution"] with { Image = null }]);
        await Task.Delay(350);
        var retained = (CardOverlayPrefs)Get(page, "_cardOverlayDraft")!;
        Check(wire.Saves == writes && retained.Style?.Size == 1.5 && retained.Preset == draft.Preset && retained.Items["resolution"].Image == draft.Items["resolution"].Image, "retired controls, preset/item callbacks and delayed save cannot edit a successor profile");
        Check(rejectedRgbFill && acceptedRgbaFill, "native fill editor writes only the official eight-digit RGBA schema");
        client.SetProfile("overlay-fixture", "synthetic-profile-token"); await service.RefreshPreferencesAsync();
        surface.Children.Clear();
    }

    private static void Configure(CardOverlayService service, string preset)
    {
        var prefs = OverlayRegistry.DefaultPrefs;
        foreach (var id in prefs.Keys.ToArray()) prefs[id] = prefs[id] with { Enabled = false };
        foreach (var id in new[] { "resolution", "hdr", "audio" }) prefs[id] = new(true, OverlayPosition.TopLeft);
        foreach (var id in new[] { "rating_imdb", "rating_tmdb" }) prefs[id] = new(true, OverlayPosition.LeftEdge);
        foreach (var id in new[] { "release_type", "edition" }) prefs[id] = new(true, OverlayPosition.BottomLeft);
        foreach (var id in new[] { "studio", "content_rating" }) prefs[id] = new(true, OverlayPosition.BottomRight);
        Set(service, "_document", new CardOverlayPrefs(2, preset, [], prefs));
    }
    private static MediaItem Item() => new()
    {
        ContentId = "advanced-overlays-fixture", Type = "series", Title = "Advanced overlays", Year = 2026, RatingImdb = 8.7, RatingTmdb = 8.4,
        ContentRating = "TV-MA", Studios = ["A Studio With A Longer Name"], UserState = new() { WatchedCount = 2, UnplayedCount = 3 },
        OverlaySummary = new() { Resolution = "2160p", Hdr = "DV HDR10", Audio = "Atmos", ReleaseType = "WEB-DL", Edition = "Director's Cut Edition" },
    };
    private static Border Badge(string text, string id, OverlayItemConfig config, string preset, OverlayStyle? style = null, string? valueKey = null, CardOverlayService? images = null)
        => (Border)Build.Invoke(null, [text, id, config, preset, 1d, style, valueKey, images])!;
    private static void CheckRows(Panel rows)
    {
        var children = rows.Children.OfType<FrameworkElement>().ToArray();
        foreach (var child in children)
        {
            var origin = child.TransformToVisual(rows).TransformPoint(new Point());
            Check(origin.X >= -.5 && origin.X + child.ActualWidth <= rows.ActualWidth + .5, "bottom badge stays in shared available width");
            foreach (var other in children.Where(other => other != child))
            {
                var point = other.TransformToVisual(rows).TransformPoint(new Point());
                var overlapX = Math.Min(origin.X + child.ActualWidth, point.X + other.ActualWidth) - Math.Max(origin.X, point.X);
                var overlapY = Math.Min(origin.Y + child.ActualHeight, point.Y + other.ActualHeight) - Math.Max(origin.Y, point.Y);
                Check(overlapX <= .5 || overlapY <= .5, "bottom pair wraps without overlap");
            }
        }
    }
    private static async Task PauseAsync(FrameworkElement surface) { await Task.Delay(40); surface.UpdateLayout(); }
    private static bool Detach(DependencyObject root, FrameworkElement target)
    {
        if (root is Panel panel)
        {
            if (panel.Children.Contains(target)) { panel.Children.Remove(target); return true; }
            return panel.Children.OfType<DependencyObject>().Any(child => Detach(child, target));
        }
        var content = root switch { Border border => border.Child, ScrollViewer scroll => scroll.Content, ContentControl control => control.Content, _ => null };
        return content is DependencyObject childRoot && Detach(childRoot, target);
    }
    private static async Task UntilAsync(Func<bool> condition, string failure = "Native uploaded image never decoded into its actual control.")
    { for (var attempt = 0; attempt < 50; attempt++) { if (condition()) return; await Task.Delay(30); } throw new InvalidOperationException(failure); }
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static IEnumerable<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(text => text.Text);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);
    private static object? Get(object owner, string name) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);
    private static void Invoke(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
        .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); Program.Log("PASS: " + message); }
    private sealed class FixtureServices(IServiceProvider fallback, CardOverlayService service, SiloApiClient client) : IServiceProvider
    {
        private readonly WatchlistViewModel _watchlist = new(new CatalogApi(client), new RequestsApi(client));
        private readonly SiloPlayer.Helpers.NavigationService _navigation = new();
        public object? GetService(Type type) => type == typeof(CardOverlayService) ? service : type == typeof(WatchlistViewModel) ? _watchlist
            : type == typeof(SiloPlayer.Helpers.NavigationService) ? _navigation : fallback.GetService(type);
    }

    private sealed class ImageWire : HttpMessageHandler
    {
        internal readonly byte[] Bytes = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");
        internal string Ref => Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant() + ".webp";
        internal string? Failure;
        internal int Reads, Saves;
        internal bool Supports = true;
        internal TaskCompletionSource? Hold;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[] CurrentBytes
        {
            get
            {
                if (Failure == "decode") return [0, 1, 2];
                if (Failure == "oversize") return new byte[SiloApiClient.MaxOverlayImageBytes + 1];
                if (Failure != "dimensions") return Bytes;
                var oversized = Bytes.ToArray(); oversized[26] = 89; oversized[27] = 2; return oversized;
            }
        }
        internal string CurrentRef => Convert.ToHexString(SHA256.HashData(CurrentBytes)).ToLowerInvariant() + ".webp";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put) Saves++;
            if (path.StartsWith("/api/v2/overlay-images/"))
            {
                Reads++; Entered.TrySetResult(); if (Hold is not null) await Hold.Task;
                var response = new HttpResponseMessage(Failure == "missing" ? HttpStatusCode.NotFound : Failure == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = new ByteArrayContent(CurrentBytes) };
                response.Content.Headers.ContentType = new("image/webp"); return response;
            }
            object body = path.EndsWith("/contract/capabilities") ? new { api_version = 1, manifest_revision = 26, supports_overlay_images = Supports, supports_batched_effective = true }
                : path.EndsWith("/overlay-config") ? new { enabled = true, images = new[] { new { @ref = Ref, name = "Synthetic image", width = 1, height = 1, url = "https://outside.invalid/not-used" } } }
                : path.EndsWith("/values/ui.card_overlays") ? new { key = "ui.card_overlays", value = new { version = 2, preset = "plate", items = new { } }, scope = "profile" }
                : path.EndsWith("/effective") ? new { items = Array.Empty<object>() } : new { };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
}

