using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

/// <summary>
/// The viewer's Home rows as the official WebUI has them (silo-server 3b2b32168; #4410, #4471, #4702), with synthetic data at two widths:
/// Home with a row of collections among ordinary rows, Settings > Home Screen with a server row and an own row, the collections row editor
/// with one collection no longer available, and the collection picker with its library filter.
/// </summary>
internal static class HomeRowsCollectionsNativeFixture
{
    private static readonly string[] PosterKeys = ["m1", "m2", "m3", "m4", "m5", "m6", "m7", "c-mcu", "c-pixar", "c-anime", "c-sitcom", "c-weekend", "c-horror"];

    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        var posters = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var index = 0; index < PosterKeys.Length; index++) posters[PosterKeys[index]] = await PngAsync(index);
        using var http = new HttpClient(new Wire(posters));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-collections.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("p-me");
        var settingsApi = new SettingsApi(client);
        var navigation = new NavigationService { Frame = new Frame() };
        var opened = new List<CollectionBrowsePage.NavArgs>();
        navigation.NavigationRequestHandler = (type, argument) =>
        {
            if (type != typeof(CollectionBrowsePage)) throw new InvalidOperationException("A collection card navigated outside the collection page.");
            opened.Add((CollectionBrowsePage.NavArgs)argument!); return true;
        };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(AuthApi)] = new AuthApi(client), [typeof(HttpClient)] = http,
            [typeof(SettingsApi)] = settingsApi, [typeof(NavigationService)] = navigation, [typeof(UICustomizationService)] = new UICustomizationService(settingsApi),
        }));
        var loader = RecipeGalleryDialog.CollectionOptionsLoader;
        RecipeGalleryDialog.CollectionOptionsLoader = _ => Task.FromResult(Synthetic());
        try
        {
            foreach (var width in new[] { 1440, 800 })
            {
                window.AppWindow.ResizeClient(new(width, 1500)); await Task.Delay(300);
                Check(Math.Abs(parent.XamlRoot.Size.Width - width) < 1, $"the fixture window is clamped at {parent.XamlRoot.Size.Width}, not {width}");
                await HomeAsync(parent, width, opened);
                await SettingsRowsAsync(parent, width);
                await EditorAsync(parent, width);
                await ServerRowAsync(parent, width);
            }
            await TimedOutLoadAsync(parent);
            Program.Log("PASS: row of collections on Home, Settings > Home Screen origin tags, the collections row editor and its library filter.");
        }
        finally
        {
            RecipeGalleryDialog.CollectionOptionsLoader = loader;
            field.SetValue(null, previous);
        }
    }

    // ── Home ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] Expected = ["mcu", "pixar", "ghibli", "anime", "sitcom", "weekend", "horror"];

    private static async Task HomeAsync(StackPanel parent, int width, List<CollectionBrowsePage.NavArgs> opened)
    {
        MediaItem Movie(int index, string title) => new() { ContentId = "m" + index, Title = title, Type = "movie", Year = 2020 + index, PosterUrl = "https://posters.fixture/m" + index };
        var recent = new HomeSectionWithItems { Id = "recent", SectionType = "recently_added", Title = "Recently Added", ItemLimit = 20, TotalCount = 7, LoadCompleted = true,
            Items = [Movie(1, "Dune: Part Two"), Movie(2, "Past Lives"), Movie(3, "Poor Things"), Movie(4, "The Holdovers"), Movie(5, "Oppenheimer"), Movie(6, "Barbie"), Movie(7, "Anatomy of a Fall")] };
        var trending = new HomeSectionWithItems { Id = "trend", SectionType = "trending_on_server", Title = "Trending on this server", ItemLimit = 20, TotalCount = 4, LoadCompleted = true,
            Items = [Movie(4, "The Holdovers"), Movie(2, "Past Lives"), Movie(7, "Anatomy of a Fall"), Movie(1, "Dune: Part Two")] };
        var collections = new HomeSectionWithItems { Id = "shelves", SectionType = "collection_posters", Title = "Collections", ItemLimit = 20, TotalCount = 7, LoadCompleted = true, IsCustom = true,
            Collections =
            [
                new() { Id = "mcu", Source = "library", Title = "Marvel Cinematic Universe", PosterUrl = "https://posters.fixture/c-mcu", ItemCount = 33, LibraryId = 2 },
                new() { Id = "pixar", Source = "library", Title = "Pixar", PosterUrl = "https://posters.fixture/c-pixar", ItemCount = 24, LibraryId = 2 },
                new() { Id = "ghibli", Source = "library", Title = "Studio Ghibli, the complete works", PosterUrl = "", ItemCount = 21, LibraryId = 2 },
                new() { Id = "anime", Source = "library", Title = "Anime nights", PosterUrl = "https://posters.fixture/c-anime", ItemCount = 12, LibraryId = 1 },
                new() { Id = "sitcom", Source = "library", Title = "Top rated sitcoms", PosterUrl = "https://posters.fixture/c-sitcom", ItemCount = 1204, LibraryId = 1 },
                new() { Id = "weekend", Source = "user", Title = "Weekend picks", PosterUrl = "https://posters.fixture/c-weekend", PosterIsCollage = true, ItemCount = 8, CreatorProfileId = "p-me" },
                new() { Id = "horror", Source = "user", Title = "Horror marathon", PosterUrl = "https://posters.fixture/c-horror", ItemCount = 11, CreatorProfileId = "p-sam" },
            ] };
        var surface = new Border { Width = width, Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], Padding = new Thickness(0, 24, 0, 0) };
        var stack = new StackPanel { Width = width };
        var rows = new List<SectionRow>();
        foreach (var section in new[] { recent, collections, trending })
        {
            var row = new SectionRow { Section = section, Width = width, HorizontalAlignment = HorizontalAlignment.Left, UseSectionBrowseLinks = true, OnViewAll = () => throw new InvalidOperationException("Explore all was offered or opened on a row it does not belong to.") };
            rows.Add(row); stack.Children.Add(row);
        }
        surface.Child = stack; parent.Children.Add(surface);
        try
        {
            await Until(() => All<CollectionPosterCard>(stack).Count() >= 4);
            await Task.Delay(1500); surface.UpdateLayout();
            var row = rows[1];
            Check(row.Visibility == Visibility.Visible, "a row of collections was hidden as an empty row");
            Check(((FrameworkElement)row.FindName("ExploreAllBtn")).Visibility == Visibility.Collapsed && ((FrameworkElement)row.FindName("TitleLinkBtn")).Visibility == Visibility.Collapsed,
                "a row of collections offered Explore all or a title link");
            Check(((FrameworkElement)row.FindName("PinSectionBtn")).Visibility == Visibility.Collapsed, "a row of collections offered a pin");
            Check(All<TextBlock>(row).Any(text => text.Text == "Collections"), "the row did not show its title");
            await Capture(surface, $"home-collections-row-{width}.png");
            // The cards the row has realized, now and after it scrolls to its end, are in the row's order and carry what the WebUI's card carries.
            var seen = new HashSet<string>(); var order = new List<string>();
            void Note()
            {
                foreach (var card in All<CollectionPosterCard>(row).OrderBy(card => card.TransformToVisual(row).TransformPoint(new(0, 0)).X))
                { if (!order.Contains(card.Card!.Id)) order.Add(card.Card.Id); }
                foreach (var text in All<TextBlock>(row)) seen.Add(text.Text);
            }
            Note();
            var scroller = (ScrollViewer)row.FindName("CardsScrollViewer");
            scroller.ChangeView(scroller.ScrollableWidth, null, null, true);
            await Until(() => All<CollectionPosterCard>(row).Any(card => card.Card!.Id == "horror")); await Task.Delay(1200); Note();
            Check(order.OrderBy(id => Array.IndexOf(Expected, id)).SequenceEqual(order) && order.Count == 7 && order.SequenceEqual(Expected), "collection cards are not in the row's order: " + string.Join(",", order));
            Check(seen.Contains("Marvel Cinematic Universe") && seen.Contains("33") && seen.Contains("1,204"), "a collection card lost its name or its title count");
            Check(seen.Contains("by Sam") || seen.Contains("by Another profile"), "another profile's shared collection did not name its owner");
            Check(!seen.Contains("by Alex"), "the viewing profile's own collection named its owner");
            await Capture(surface, $"home-collections-row-end-{width}.png");
            // Each card opens its collection: a server collection in its library, a personal one on its own.
            var weekend = All<CollectionPosterCard>(row).Single(card => card.Card!.Id == "weekend");
            Open(weekend);
            scroller.ChangeView(0, null, null, true);
            await Until(() => All<CollectionPosterCard>(row).Any(card => card.Card!.Id == "mcu"));
            Open(All<CollectionPosterCard>(row).Single(card => card.Card!.Id == "mcu"));
            Check(opened.Count == 2 && opened[0] is { CollectionId: "weekend", IsUserCollection: true, LibraryId: null }
                && opened[1] is { CollectionId: "mcu", IsUserCollection: false, LibraryId: 2, Title: "Marvel Cinematic Universe" }, "a collection card did not open its collection");
            // A row of collections with no cards after a completed read is an empty row, as for any other row.
            var emptied = new HomeSectionWithItems { Id = "empty", SectionType = "collection_posters", Title = "Empty", LoadCompleted = true };
            row.Section = emptied; await Task.Delay(100);
            Check(row.Visibility == Visibility.Collapsed, "a row with neither titles nor collections stayed visible");
            row.Section = collections; await Task.Delay(300);
            Check(row.Visibility == Visibility.Visible && All<CollectionPosterCard>(row).Any(), "the row did not come back with its collections");
        }
        finally { parent.Children.Remove(surface); opened.Clear(); }
    }

    private static void Open(CollectionPosterCard card)
    {
        var button = All<Button>(card).First();
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    }

    // ── Settings > Home Screen: the row list ──────────────────────────────────────────────────────────────────────────────

    private static async Task SettingsRowsAsync(StackPanel parent, int width)
    {
        var page = new SettingsPage { Width = 400, Height = 100, Opacity = 0 };
        parent.Children.Add(page); await Task.Delay(150);
        var surface = new Border { Width = Math.Min(width - 48, 768) + 48, Padding = new Thickness(24), Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        var list = new StackPanel { Spacing = 8 };
        surface.Child = list; parent.Children.Add(surface);
        try
        {
            page.ViewModel.CanEditHomeSections = true;
            var build = typeof(SettingsPage).GetMethod("BuildHomeSectionRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Dictionary<string, object> Refs(params string[] ids) => HomeRowCollections.WithRefs(null, ids.Select(id => new HomeRowCollectionRef("library", id)));
            var entries = new[]
            {
                new SettingsSectionEntry { Id = "s1", SectionType = "continue_watching", Title = "Continue Watching", ItemLimit = 20, Position = 0, Config = [] },
                new SettingsSectionEntry { Id = "s2", SectionType = "recently_added", Title = "Recently Added", ItemLimit = 20, Position = 1, Config = [] },
                new SettingsSectionEntry { Id = "own-1", SectionType = "collection_posters", Title = "My shelves", ItemLimit = 20, IsCustom = true, Customized = true, Position = 2, Config = Refs("mcu", "pixar", "ghibli") },
                new SettingsSectionEntry { Id = "own-2", SectionType = "collection", Title = "Pixar picks", ItemLimit = 20, IsCustom = true, Customized = true, Position = 3, Config = new() { ["library_collection_id"] = "pixar" } },
            };
            foreach (var entry in entries) list.Children.Add((Border)build.Invoke(page, [entry])!);
            await Task.Delay(300); surface.UpdateLayout();
            var texts = All<TextBlock>(list).Select(text => text.Text).ToList();
            Check(texts.Count(text => text == "Server") == 2, "the server's own rows were not tagged Server: " + string.Join("|", texts));
            Check(texts.Count(text => text == "Yours") == 2, "the profile's own rows were not tagged Yours");
            Check(texts.Contains("3 collections") && !texts.Any(text => text.StartsWith("3 collections ·")), "a row of collections named a title limit or did not count its collections");
            Check(texts.Contains("A collection · 20 titles"), "an ordinary row lost its title limit");
            await Capture(surface, $"settings-home-rows-origin-{width}.png");
        }
        finally { parent.Children.Remove(surface); parent.Children.Remove(page); }
    }

    // ── The collections row editor ──────────────────────────────────────────────────────────────────────────────

    private static RecipeCatalogResponse Catalog() => new()
    {
        AllowAdminOnlyRecipes = true,
        Categories = new()
        {
            ["hand_picked"] =
            [
                new() { Type = "collection", Category = "hand_picked", Presets = [new() { Key = "collection_pick", DisplayName = "A collection", Icon = "📁", DescriptionShort = "Show one collection." }] },
                new() { Type = "collection_posters", Category = "hand_picked", Presets = [new() { Key = "collection_posters_pick", DisplayName = "A row of collections", Icon = "🗂️",
                    DescriptionShort = "Show several collections as posters. Each one opens its collection.", DefaultParams = new() { ["collections"] = new List<object>() } }] },
            ],
        },
    };

    private static async Task<(Task<RecipeConfigurationResult?> Showing, FrameworkElement Root)> OpenEditorAsync(StackPanel parent, SettingsSectionEntry section)
    {
        var showing = RecipeGalleryDialog.ShowEditorAsync(parent.XamlRoot, Catalog(), section, "Home");
        await Until(() => Popups(parent.XamlRoot).Any(popup => All<Grid>(popup).Any(grid => grid.Name == "HomeRowForm")));
        var root = Popups(parent.XamlRoot).Single(popup => All<Grid>(popup).Any(grid => grid.Name == "HomeRowForm"));
        await Task.Delay(500); root.UpdateLayout();
        return (showing, root);
    }

    private static async Task CloseAsync(Task<RecipeConfigurationResult?> showing, FrameworkElement root)
    {
        var cancel = All<Button>(root).Single(button => Equals(button.Content, "Cancel"));
        ((IInvokeProvider)new ButtonAutomationPeer(cancel).GetPattern(PatternInterface.Invoke)!).Invoke();
        await showing;
    }

    private static async Task EditorAsync(StackPanel parent, int width)
    {
        var own = new SettingsSectionEntry { Id = "own-1", SectionType = "collection_posters", Title = "My shelves", ItemLimit = 20, IsCustom = true, Customized = true,
            Config = HomeRowCollections.WithRefs(null, [new("library", "mcu"), new("user", "weekend"), new("library", "gone-collection")]) };
        var (showing, root) = await OpenEditorAsync(parent, own);
        try
        {
            var editor = All<StackPanel>(root).Single(panel => panel.Name == "HomeRowCollectionsEditor");
            var chosen = All<StackPanel>(editor).Single(panel => panel.Name == "HomeRowCollectionsChosen");
            await Until(() => All<TextBlock>(chosen).Any(text => text.Text == "No longer available"));
            var chosenTexts = () => All<TextBlock>(chosen).Select(text => text.Text).ToList();
            Check(chosenTexts().Contains("Marvel Cinematic Universe") && chosenTexts().Contains("Weekend picks") && chosenTexts().Contains("No longer available"), "the chosen list lost a collection or its unavailable mark");
            Check(chosenTexts().Any(text => text.Contains("Yours")), "a personal collection of this profile was not marked Yours");
            Check(All<TextBlock>(editor).Any(text => text.Text == "3 collections, shown in this order"), "the chosen count line is missing");
            Check(All<TextBlock>(editor).Any(text => text.Text == "Collections in this row") && All<TextBlock>(editor).Any(text => text.Text == "Add collections"), "the editor lost its two headings");
            // The more options of a row of collections do not exist: it has no title limit and cannot be the hero.
            Check(!All<Expander>(root).Any(expander => expander.Visibility == Visibility.Visible), "a row of collections offered more options");
            Check(All<TextBlock>(root).Any(text => text.Text == "Yours") && !All<TextBlock>(root).Any(text => text.Name == "HomeRowServerNote"), "the profile's own row was not tagged Yours or carried the server note");
            await Capture(root, $"editor-collections-row-{width}.png");

            // Move the first collection down, then back up: only that pair swaps.
            string Order() => string.Join(",", HomeRowCollections.RefsOf(CurrentConfig(editor)).Select(entry => entry.Id));
            Press(root, "Move Marvel Cinematic Universe down"); await Task.Delay(120);
            Check(Order() == "weekend,mcu,gone-collection", "Move down did not swap the pair: " + Order());
            Press(root, "Move Marvel Cinematic Universe up"); await Task.Delay(120);
            Check(Order() == "mcu,weekend,gone-collection", "Move up did not restore the order: " + Order());
            Check(!IsEnabled(root, "Move Marvel Cinematic Universe up") && !IsEnabled(root, "Move No longer available down"), "an edge entry could still move past the list");
            // Remove the one that is gone: the others stay.
            Press(root, "Remove No longer available"); await Task.Delay(120);
            Check(Order() == "mcu,weekend", "Remove did not take out the unavailable collection: " + Order());

            // The chooser lists the collections with their library, type and count; adding one is one press.
            var results = All<StackPanel>(editor).Single(panel => panel.Name == "HomeRowCollectionsResults");
            await Until(() => All<Button>(results).Count() >= 7);
            Check(All<TextBlock>(results).Any(text => text.Text == "Movies · Manual · 33 titles"), "the chooser did not read a collection with its library, type and count");
            Check(All<TextBlock>(results).Any(text => text.Text.Contains("by Sam")) || All<TextBlock>(results).Any(text => text.Text.Contains("by Another profile")), "another profile's collection did not carry its owner");
            Press(root, "Pixar"); await Task.Delay(120);
            Check(Order() == "mcu,weekend,pixar", "adding a collection did not append it: " + Order());
            Press(root, "Pixar"); await Task.Delay(120);
            Check(Order() == "mcu,weekend", "pressing an added collection did not take it out again: " + Order());
            Press(root, "Pixar"); await Task.Delay(300); root.UpdateLayout();
            await Capture(root, $"editor-collections-row-added-{width}.png");

            // The library filter: offered for two libraries, it narrows the list; with a type that library does not have, the list says so.
            var library = All<DropDownButton>(editor).Single(button => button.Name == "HomeRowCollectionsLibrary");
            Check(library.Visibility == Visibility.Visible, "the library filter was not offered for two libraries");
            library.Flyout.ShowAt(library); await Task.Delay(500);
            var presenter = Popups(parent.XamlRoot).SelectMany(popup => All<MenuFlyoutPresenter>(popup)).Single();
            Check(All<MenuFlyoutItem>(presenter).Select(item => item.Text).SequenceEqual(["All libraries", "Movies", "Shows"]), "the library menu did not list every library by name");
            await Capture(presenter, $"picker-library-filter-open-{width}.png");
            Invoke(All<MenuFlyoutItem>(presenter).Single(item => item.Text == "Movies")); await Task.Delay(400); root.UpdateLayout();
            Check(All<TextBlock>(results).All(text => !text.Text.StartsWith("Shows")) && All<Button>(results).Count() == 3, "the library filter did not narrow the list to that library: " + All<Button>(results).Count());
            Check(Equals(library.Content, "Movies"), "the library button does not name the picked library");
            await Capture(root, $"picker-library-movies-{width}.png");
            Toggle(All<ToggleButton>(editor).Single(toggle => toggle.Name == "HomeRowCollectionsKind_synced")); await Task.Delay(400); root.UpdateLayout();
            var status = All<TextBlock>(editor).Single(text => text.Name == "HomeRowCollectionsStatus");
            Check(status.Visibility == Visibility.Visible && status.Text == "No collections of this type in this library.", "an empty library and type did not say so: " + status.Text);
            await Capture(root, $"picker-none-in-library-{width}.png");
            Toggle(All<ToggleButton>(editor).Single(toggle => toggle.Name == "HomeRowCollectionsKind_all")); await Task.Delay(300);
            var search = All<TextBox>(editor).Single(box => box.Name == "HomeRowCollectionsSearch");
            search.Text = "zzz"; await Task.Delay(200);
            Check(status.Text == "No collections match \"zzz\".", "a search with no match did not say so: " + status.Text);
            search.Text = "";

            // A row with no collections cannot be saved, and the limit stops further additions.
            while (HomeRowCollections.RefsOf(CurrentConfig(editor)).Count > 0)
            {
                var remove = All<Button>(chosen).First(button => (AutomationProperties.GetName(button) ?? "").StartsWith("Remove "));
                ((IInvokeProvider)new ButtonAutomationPeer(remove).GetPattern(PatternInterface.Invoke)!).Invoke(); await Task.Delay(120);
            }
            Check(HomeRowCollections.RefsOf(CurrentConfig(editor)).Count == 0 && All<TextBlock>(editor).Any(text => text.Text == "Add at least one collection below."), "an empty row did not ask for a collection");
            var save = All<Button>(root).Single(button => Equals(button.Content, "Save"));
            ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)!).Invoke(); await Task.Delay(150);
            Check(All<TextBlock>(root).Any(text => text.Text == "Add a collection to see the row."), "saving an empty row of collections was not refused with the official wording");
        }
        finally { await CloseAsync(showing, root); }
    }

    // ── A row the server manages ────────────────────────────────────────────────────────────────────────────────

    private static async Task ServerRowAsync(StackPanel parent, int width)
    {
        var server = new SettingsSectionEntry { Id = "s2", SectionType = "collection_posters", Title = "Franchises", ItemLimit = 20, IsCustom = false,
            Config = HomeRowCollections.WithRefs(null, [new("library", "mcu"), new("library", "pixar")]) };
        var (showing, root) = await OpenEditorAsync(parent, server);
        try
        {
            Check(All<TextBlock>(root).Any(text => text.Text == "Server"), "a server row's form did not carry the Server tag");
            Check(All<TextBlock>(root).Any(text => text.Name == "HomeRowServerNote" && text.Text.StartsWith("Managed by this server.")), "a server row's form did not carry the server note");
            Check(!All<TextBlock>(root).Any(text => text.Text == "Yours"), "a server row's form carried the Yours tag");
            await Capture(root, $"editor-server-row-{width}.png");
        }
        finally { await CloseAsync(showing, root); }
    }

    // ── A read that timed out ───────────────────────────────────────────────────────────────────────────────────

    private static async Task TimedOutLoadAsync(StackPanel parent)
    {
        // A read that times out arrives as a cancellation the editor did not ask for: the chooser says the collections didn't load.
        var loader = RecipeGalleryDialog.CollectionOptionsLoader;
        RecipeGalleryDialog.CollectionOptionsLoader = _ => Task.FromException<HomeRowCollectionLoad>(new TaskCanceledException());
        try
        {
            var own = new SettingsSectionEntry { Id = "own-1", SectionType = "collection_posters", Title = "My shelves", ItemLimit = 20, IsCustom = true, Customized = true,
                Config = HomeRowCollections.WithRefs(null, [new("library", "mcu")]) };
            var (showing, root) = await OpenEditorAsync(parent, own);
            try
            {
                var status = All<TextBlock>(root).Single(text => text.Name == "HomeRowCollectionsStatus");
                var failed = HomeRowCollections.EmptyText(false, true, 0, "", false);
                Check(failed != HomeRowCollections.EmptyText(true, false, 0, "", false), "the failed and the loading text are the same");
                await Until(() => status.Visibility == Visibility.Visible && status.Text == failed);
                Check(!All<TextBlock>(root).Any(text => text.Text == "No longer available"), "a collection was called unavailable though the collections did not load");
            }
            finally { await CloseAsync(showing, root); }
        }
        finally { RecipeGalleryDialog.CollectionOptionsLoader = loader; }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object> CurrentConfig(DependencyObject editor)
    {
        // The editor writes the row's config in place; read it back through the state it hangs on its panel.
        var state = ((FrameworkElement)editor).Tag!;
        return (Dictionary<string, object>)state.GetType().GetProperty("Config", BindingFlags.Instance | BindingFlags.Public)!.GetValue(state)!;
    }

    private static void Press(DependencyObject root, string name)
        => ((IInvokeProvider)new ButtonAutomationPeer(All<Button>(root).First(button => AutomationProperties.GetName(button) == name)).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static bool IsEnabled(DependencyObject root, string name) => All<Button>(root).First(button => AutomationProperties.GetName(button) == name).IsEnabled;
    private static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void Toggle(ToggleButton toggle) { if (toggle.IsChecked != true) ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)!).Toggle(); }
    private static IEnumerable<FrameworkElement> Popups(XamlRoot root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(popup => popup.Child).OfType<FrameworkElement>();
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var item in All<T>(VisualTreeHelper.GetChild(root, index))) yield return item;
    }
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 200 && !ready(); n++) await Task.Delay(25); Check(ready(), "the native Home rows operation did not settle"); }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(150);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered nothing");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
        Program.Log($"TRACE {name} {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    private static HomeRowCollectionLoad Synthetic()
    {
        var shows = new SiloPlayer.Core.Models.Catalog.Library { Id = 1, Name = "Shows" }; var movies = new SiloPlayer.Core.Models.Catalog.Library { Id = 2, Name = "Movies" };
        SiloPlayer.Core.Models.Catalog.LibraryCollection Library(string id, string title, string type, int items) => new() { Id = id, Title = title, CollectionType = type, ItemCount = items, PosterUrl = "https://posters.fixture/c-" + id };
        var options = HomeRowCollections.BuildOptions([movies, shows],
            [new() { Id = "weekend", Name = "Weekend picks", CollectionType = "manual", ItemCount = 8, CreatorProfileId = "p-me", PosterUrl = "https://posters.fixture/c-weekend" },
             new() { Id = "horror", Name = "Horror marathon", CollectionType = "manual", ItemCount = 11, CreatorProfileId = "p-sam", PosterUrl = "https://posters.fixture/c-horror" }],
            [new[] { Library("mcu", "Marvel Cinematic Universe", "manual", 33), Library("pixar", "Pixar", "manual", 24), Library("ghibli", "Studio Ghibli", "manual", 21) },
             new[] { Library("anime", "Anime nights", "manual", 12), Library("sitcom", "Top rated sitcoms", "tmdb_list", 40) }]);
        return new(options, false);
    }

    private static async Task<byte[]> PngAsync(int seed)
    {
        const int width = 200, height = 300;
        var pixels = new byte[width * height * 4];
        double Hue(int offset) => (seed * 47 + offset) % 360;
        (byte R, byte G, byte B) Rgb(double hue, double lightness)
        {
            double Channel(double n) { var k = (n + hue / 30) % 12; var a = 0.55 * Math.Min(lightness, 1 - lightness); return lightness - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)); }
            return ((byte)(Channel(0) * 255), (byte)(Channel(8) * 255), (byte)(Channel(4) * 255));
        }
        var top = Rgb(Hue(0), 0.55); var bottom = Rgb(Hue(40), 0.22);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var t = y / (double)height; var index = (y * width + x) * 4;
                pixels[index] = (byte)(top.B + (bottom.B - top.B) * t); pixels[index + 1] = (byte)(top.G + (bottom.G - top.G) * t); pixels[index + 2] = (byte)(top.R + (bottom.R - top.R) * t); pixels[index + 3] = 255;
            }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0); var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> overrides) : IServiceProvider
    { public object? GetService(Type type) => overrides.TryGetValue(type, out var value) ? value : fallback.GetService(type); }

    private sealed class Wire(Dictionary<string, byte[]> posters) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "posters.fixture")
            {
                var key = uri.AbsolutePath.Trim('/');
                return Task.FromResult(posters.TryGetValue(key, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("image/png") } } }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (uri.AbsolutePath.EndsWith("/api/v2/profiles", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = new[] { new { id = "p-me", name = "Alex" }, new { id = "p-sam", name = "Sam" } } })) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
