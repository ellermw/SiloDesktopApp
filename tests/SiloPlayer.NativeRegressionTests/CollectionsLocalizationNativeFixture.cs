using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

// Task 5 collections/Home-customization localization on actual controls over a synthetic API: the Collections wall
// (Show pills, cards, owner menu), the collection editor's smart rule controls and the Add row gallery and form. Each
// switches the retained controls between German, Japanese and Arabic, keeps names people or the server typed exactly
// as they were (including a mixed-direction title) and proves an Add row name that is saved stays English.
internal static class CollectionsLocalizationNativeFixture
{
    private const string MixedTitle = "Noches de cine — ليالي السينما";

    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)serviceField.GetValue(null)!;
        var locales = LocaleService.Shared; var before = locales.Current;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collections-localization.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("fixture", "fixture", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("me", profile: new() { Id = "me", Name = "Fixture", IsPrimary = true });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client);
        var navigation = new NavigationService(); var presentation = new UICustomizationService(settings);
        serviceField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth, [typeof(CatalogApi)] = () => catalog,
            [typeof(CollectionsApi)] = () => collections, [typeof(SettingsApi)] = () => settings, [typeof(AuthApi)] = () => new AuthApi(client),
            [typeof(NavigationService)] = () => navigation, [typeof(ToastService)] = () => new ToastService(), [typeof(UICustomizationService)] = () => presentation,
            [typeof(HttpClient)] = () => http, [typeof(PeopleApi)] = () => new PeopleApi(client),
            [typeof(CollectionsViewModel)] = () => new CollectionsViewModel(collections, catalog, new AuthApi(client), client),
            [typeof(CollectionEditorViewModel)] = () => new CollectionEditorViewModel(collections, catalog, settings, auth),
        }));
        try
        {
            Apply("en");
            await WallAsync(parent, navigation);
            await EditorAsync(parent, navigation);
            await RecipeGalleryAsync(parent);
        }
        finally
        {
            parent.Children.Clear(); await Task.Delay(100);
            locales.Apply(locales.Create(before.Resolution));
            serviceField.SetValue(null, previous);
        }
    }

    // ===== Collections wall: pills, cards, owner menu =====
    private static async Task WallAsync(StackPanel parent, NavigationService navigation)
    {
        var frame = new Frame { Width = 1280, Height = 900 }; parent.Children.Add(frame); navigation.Frame = frame;
        frame.Navigate(typeof(CollectionsPage));
        var page = (CollectionsPage)frame.Content;
        await Layout(frame, 1280, 900);
        await Until(() => Pills(page).Length == 6 && OpenButtons(page).Count() >= 3 + 3, () => string.Join("|", Pills(page).Select(Name)));
        Check(Pills(page).Select(Name).Take(4).SequenceEqual(["All", "Mine", "Shared", "Server"]), "English pills: " + string.Join("|", Pills(page).Select(Name)));

        foreach (var language in new[] { "de", "ja", "ar" })
        {
            Apply(language); await Settle(frame);
            var expected = new[] { "web.collections.page.filterAll", "web.collections.page.filterMine", "web.collections.page.filterShared", "web.collections.page.filterServer" }.Select(key => AppStrings.Get(key)).ToArray();
            await Until(() => Pills(page).Select(Name).Take(4).SequenceEqual(expected), () => language + ": " + string.Join("|", Pills(page).Select(Name)));
            // Some labels are the same word in a language (German "Server"); "All" and "Mine" never are.
            Check(expected[0] != "All" && expected[1] != "Mine", $"{language} catalog lacks translated pill labels");
            // Library pills are the server's library names, never translated.
            Check(Pills(page).Skip(4).Select(Name).SequenceEqual(["Movies", "Shows"]), $"{language}: library pills changed: " + string.Join("|", Pills(page).Skip(4).Select(Name)));
            var subtitle = (TextBlock)page.FindName("CollectionsSubtitle");
            Check(subtitle.Text == AppStrings.Get("web.collections.page.subtitleShared"), $"{language}: retained subtitle '{subtitle.Text}'");
            var title = (TextBlock)page.FindName("CollectionsTitle");
            Check(title.Text == AppStrings.Get("web.collections.page.title"), $"{language}: retained XAML title '{title.Text}'");
            // Saved names, including a mixed-direction one, are shown exactly as typed.
            Check(OpenButtons(page).Any(button => Name(button) == "Open " + MixedTitle) && Descendants<TextBlock>(page).Any(text => text.Text == MixedTitle),
                $"{language}: mixed-direction collection name changed");
            var manual = AppStrings.Get("web.collections.types.kindManual");
            Check(Descendants<TextBlock>(page).Any(text => text.Text.StartsWith(manual + " · ", StringComparison.Ordinal)), $"{language}: card meta did not use '{manual}'");
            Program.Log($"PASS: Collections wall pills, retained heading/subtitle and card metadata in {language}; names and library pills unchanged.");
        }

        // The owner menu is keyed before it is ever opened and follows the language while open.
        Apply("de"); await Settle(frame);
        var more = Descendants<Button>(page).First(button => Name(button) == AppStrings.Get("web.collections.admin.moreFor", new Dictionary<string, object?> { ["name"] = MixedTitle }));
        Invoke(more);
        await Until(() => OpenMenu(page) != null);
        var presenter = OpenMenu(page)!;
        await Until(() => Descendants<MenuFlyoutItem>(presenter).Any(item => item.Text == AppStrings.Get("web.collections.admin.editCollection")),
            () => string.Join("|", Descendants<MenuFlyoutItem>(presenter).Select(item => item.Text)));
        var items = Descendants<MenuFlyoutItem>(presenter).ToList();
        var edit = items.First(item => item.Text == AppStrings.Get("web.collections.admin.editCollection"));
        Check(items.Any(item => item.Text == AppStrings.Get("web.collections.actions.delete")), "German owner menu lacks the translated Delete…");
        Check(edit.FlowDirection == FlowDirection.LeftToRight, "German owner menu is not left-to-right");
        Apply("ar"); await Settle(frame);
        await Until(() => edit.Text == AppStrings.Get("web.collections.admin.editCollection") && edit.FlowDirection == FlowDirection.RightToLeft,
            () => edit.Text + " " + edit.FlowDirection);
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)) popup.IsOpen = false;
        Program.Log("PASS: open owner menu entries switch German → Arabic text and right-to-left direction while open.");

        // A long German action keeps its whole label inside the narrow layout's create button.
        Apply("de"); frame.Width = 460; await Layout(frame, 460, 900);
        var dock = Descendants<Button>((Border)page.FindName("CreateCollectionDock")).Single();
        Check(Equals(dock.Content, AppStrings.Get("web.collections.copy.newCollection")) && dock.ActualWidth <= 460, $"narrow create action '{dock.Content}' width {dock.ActualWidth}");
        parent.Children.Remove(frame); Apply("en");
    }

    // ===== Collection editor: smart rules and query rule controls =====
    private static async Task EditorAsync(StackPanel parent, NavigationService navigation)
    {
        var frame = new Frame { Width = 1280, Height = 900 }; parent.Children.Add(frame); navigation.Frame = frame;
        Apply("de");
        frame.Navigate(typeof(CollectionEditorPage), new CollectionEditorNavigationArgs("smart"));
        var page = (CollectionEditorPage)frame.Content;
        await Layout(frame, 1280, 900);
        await Until(() => Descendants<TextBlock>(page).Any(text => text.Text == AppStrings.Get("web.collections.editor.rulesHeading")),
            () => string.Join("|", Descendants<TextBlock>(page).Select(text => text.Text).Where(text => text.Length > 0).Take(40)));
        var addRule = Descendants<Button>(page).First(button => Descendants<TextBlock>(button).Any(text => text.Text == AppStrings.Get("web.home.addRow.rules.addRule")));
        Invoke(addRule); await Settle(frame);
        ComboBox Operator() => Descendants<ComboBox>(page).First(combo => Name(combo) == AppStrings.Get("web.catalog.rules.condition"));
        await Until(() => Descendants<ComboBox>(page).Any(combo => Name(combo) == AppStrings.Get("web.catalog.rules.condition")));
        var rules = (SiloPlayer.Controls.QueryRulesEditor)typeof(CollectionEditorPage).GetField("_rulesEditor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        Check(rules.Query.Groups.Single().Rules.Single().Op == "is", "the rule's wire operator changed");
        Check(Operator().SelectedItem?.ToString() == AppStrings.Get("web.collections.builder.opIs"), $"German operator label '{Operator().SelectedItem}'");
        var scope = (ComboBox)typeof(CollectionEditorPage).GetField("_smartScope", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        Check(scope.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string).Contains("movie"), "scope wire values changed");
        foreach (var language in new[] { "ja", "ar" })
        {
            Apply(language); await Settle(frame);
            await Until(() => Operator().SelectedItem?.ToString() == AppStrings.Get("web.collections.builder.opIs"), () => language + ": " + Operator().SelectedItem);
            Check(rules.Query.Groups.Single().Rules.Single() is { Op: "is", Field: "genre" }, $"{language}: redrawing the rules changed the saved rule");
            Check(Descendants<TextBlock>(page).Any(text => text.Text == AppStrings.Get("web.collections.editor.rulesHeading")), $"{language}: retained rules heading");
            var movies = scope.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "movie"));
            await Until(() => Equals(movies.Content, AppStrings.Get("web.home.addRow.rules.scopeMovie")), () => language + " scope " + movies.Content
                + "; loaded=" + scope.IsLoaded + "; locale=" + AppStrings.Language + "; rows=" + scope.Items.Count
                + "; contents=" + string.Join(",", scope.Items.OfType<ComboBoxItem>().Select(item => item.Tag + "=" + item.Content)));
            Program.Log($"PASS: smart editor rule operator, heading and scope choices redraw in {language} with the saved rule unchanged.");
        }
        // A TMDB chart collection's generated name is saved, so it never follows the interface language.
        var chartName = (string)typeof(CollectionEditorPage).GetMethod("ChartNameEnglish", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["top_rated"])!;
        Check(chartName == "Top rated", $"generated chart name followed the interface language: '{chartName}'");
        Program.Log("PASS: generated chart collection name stays English under Arabic.");
        // Leaving would ask to discard the unsaved rule; detaching the editor is enough here.
        parent.Children.Remove(frame); await Task.Delay(150); Apply("en");
    }

    // ===== Add row gallery and form =====
    private static async Task RecipeGalleryAsync(StackPanel parent)
    {
        var host = new Grid { Width = 1280, Height = 900 }; parent.Children.Add(host); await Layout(host, 1280, 900);
        var catalog = new RecipeCatalogResponse
        {
            Categories = new()
            {
                ["discovery"] =
                [
                    new RecipeDefinition
                    {
                        Type = "most_watched", Category = "discovery",
                        Presets =
                        [
                            new GalleryPreset { Key = "mw_week", DisplayName = "Most Watched This Week", DisplayNameKey = "home.recipe.mwWeek.name", Icon = "🔥", DescriptionShort = "What everyone watched lately.", DefaultParams = new() { ["window"] = "week" } },
                            new GalleryPreset { Key = "mw_month", DisplayName = "Most Watched This Month", DisplayNameKey = "home.recipe.mwMonth.name", Icon = "🔥", DescriptionShort = "The month's favorites.", DefaultParams = new() { ["window"] = "month" } },
                        ],
                    },
                ],
            },
        };
        Apply("de");
        var shown = RecipeGalleryDialog.ShowAsync(host.XamlRoot, catalog, "Home");
        ContentDialog? gallery = null;
        await Until(() => (gallery = Dialogs(host.XamlRoot).FirstOrDefault(dialog => Descendants<TextBox>(dialog).Any())) != null,
            () => string.Join(" / ", Dialogs(host.XamlRoot).Select(dialog => dialog.Title?.ToString() ?? "(untitled)")));
        ToggleButton Chip(string key) => Descendants<ToggleButton>(gallery!).First(chip => Equals(chip.Content, AppStrings.Get(key)));
        await Until(() => Descendants<ToggleButton>(gallery!).Any(chip => Equals(chip.Content, AppStrings.Get("web.home.rows.groups.popular"))),
            () => string.Join("|", Descendants<ToggleButton>(gallery!).Select(chip => chip.Content)));
        var card = Descendants<Button>(gallery!).First(button => Name(button) == AppStrings.ServerText("home.recipe.mwWeek.name", null, "Most Watched This Week"));
        Check(Name(card) != "Most Watched This Week", "German gallery card shows the English recipe name");
        Check(gallery!.FlowDirection == FlowDirection.LeftToRight, "German gallery is not left-to-right");
        foreach (var language in new[] { "ja", "ar" })
        {
            Apply(language); await Settle(gallery);
            await Until(() => Descendants<ToggleButton>(gallery).Any(chip => Equals(chip.Content, AppStrings.Get("web.home.rows.groups.popular"))),
                () => language + ": " + string.Join("|", Descendants<ToggleButton>(gallery).Select(chip => chip.Content)));
            Check(Chip("web.collections.templateGallery.allThemes").IsChecked == true, $"{language}: active category lost on redraw");
            Check(gallery.FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), $"{language}: open gallery direction {gallery.FlowDirection}");
            Program.Log($"PASS: open Add row gallery chips, cards and direction follow {language}.");
        }
        Apply("de"); await Settle(gallery);
        card = Descendants<Button>(gallery).First(button => Name(button) == AppStrings.ServerText("home.recipe.mwWeek.name", null, "Most Watched This Week"));
        // The form's title and save action are worded by the caller when it opens (German here).
        var saveLabel = AppStrings.Get("web.home.addRow.addRow");
        Invoke(card);
        Popup? form = null;
        await Until(() => (form = VisualTreeHelper.GetOpenPopupsForXamlRoot(host.XamlRoot).FirstOrDefault(popup => popup.Child is Grid overlay && Descendants<Grid>(overlay).Any(grid => grid.Name == "HomeRowForm"))) != null);
        bool IsName(TextBox box) => Equals(box.Header, AppStrings.Get("web.home.addRow.form.rowName")) || Equals(box.Header, "Row name");
        await Until(() => Descendants<TextBox>(form!.Child).Any(IsName), () => string.Join("|", Descendants<TextBox>(form!.Child).Select(box => box.Header)));
        var name = Descendants<TextBox>(form!.Child).First(IsName);
        await Until(() => Equals(name.Header, AppStrings.Get("web.home.addRow.form.rowName")), () => "header " + name.Header);
        // The name a row saves starts as the preset's English name whatever the interface language.
        Check(name.Text == "Most Watched This Week", $"German form prefilled a translated row name '{name.Text}'");
        var period = Descendants<RadioButton>(form.Child).First(button => Equals(button.Tag, "mw_month"));
        Check(Name(period) == AppStrings.Get("web.home.rows.variants.options.thisMonth"), $"variant label '{Name(period)}'");
        Check(Descendants<TextBlock>(form.Child).Any(text => text.Text == AppStrings.Get("web.home.rows.variants.controls.period")), "variant family label not translated");
        period.IsChecked = true; await Settle(host);
        Check(name.Text == "Most Watched This Month", $"variant wrote a translated row name '{name.Text}'");
        var overlayRoot = (FrameworkElement)form.Child;
        Apply("ar"); await Settle(host);
        await Until(() => overlayRoot.FlowDirection == FlowDirection.RightToLeft && Equals(name.Header, AppStrings.Get("web.home.addRow.form.rowName")), () => overlayRoot.FlowDirection + " " + name.Header);
        Check(Descendants<Button>(form.Child).Any(button => Equals(button.Content, AppStrings.Get("common.cancel"))), "open form Cancel did not follow Arabic");
        var save = Descendants<Button>(form.Child).First(button => Equals(button.Content, saveLabel));
        Invoke(save);
        var result = await shown.WaitAsync(TimeSpan.FromSeconds(5));
        Check(result is { SectionType: "most_watched", Title: "Most Watched This Month" }, $"saved row '{result?.Title}' ({result?.SectionType})");
        Check(JsonSerializer.Serialize(result!.Config).Contains("\"window\":\"month\""), "variant wire value changed: " + JsonSerializer.Serialize(result.Config));
        Program.Log("PASS: Add row form translates its header, variant labels and direction while the saved row name and variant value stay English/wire.");
        parent.Children.Remove(host); Apply("en");
    }

    // ===== Helpers =====
    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static Button[] Pills(CollectionsPage page) => Descendants<Button>((StackPanel)page.FindName("CollectionsFilterPills")).ToArray();
    private static IEnumerable<Button> OpenButtons(CollectionsPage page) => Descendants<Button>((StackPanel)page.FindName("CollectionsGrid")).Where(button => Name(button).StartsWith("Open ", StringComparison.Ordinal));
    private static FrameworkElement? OpenMenu(FrameworkElement owner) => VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot)
        .Select(popup => popup.Child is MenuFlyoutPresenter own ? own : Descendants<MenuFlyoutPresenter>(popup.Child).FirstOrDefault()).FirstOrDefault(found => found != null);
    private static IEnumerable<ContentDialog> Dialogs(XamlRoot root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(popup => popup.Child is ContentDialog own ? new[] { own } : Descendants<ContentDialog>(popup.Child));
    private static string Name(DependencyObject element) => AutomationProperties.GetName(element) ?? "";
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, Func<string>? state = null)
    {
        for (var count = 0; count < 240 && !ready(); count++) await Task.Delay(25);
        Check(ready(), "Collections localization operation did not settle." + (state == null ? "" : " State: " + state()));
    }
    private static async Task Settle(FrameworkElement element) { await Task.Delay(120); element.UpdateLayout(); await Task.Delay(40); }
    private static async Task Layout(FrameworkElement element, double width, double height) { element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout(); await Task.Delay(220); element.UpdateLayout(); }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider
    {
        public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type);
    }

    private sealed class Wire : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "collections-localization.invalid") throw new InvalidOperationException("Collections localization attempted external network.");
            var path = request.RequestUri.AbsolutePath;
            if (request.Content != null) await request.Content.ReadAsStringAsync(ct);
            object body = path switch
            {
                "/api/v2/collections" => new
                {
                    items = new object[]
                    {
                        Collection("own-1", "Weekend picks", "me", "manual", shared: true),
                        Collection("own-2", MixedTitle, "me", "manual"),
                        Collection("own-3", "Smart picks", "me", "smart"),
                        Collection("sh-1", "Family movie night", "other", "manual", shared: true),
                    },
                    groups = Array.Empty<object>(),
                },
                "/api/v2/collections/server" => new
                {
                    libraries = new object[]
                    {
                        new { library_id = 1, library_name = "Movies", total_count = 2, collections = new[] { Card("srv-1", "Criterion"), Card("srv-2", "A24") } },
                        new { library_id = 2, library_name = "Shows", total_count = 1, collections = new[] { Card("srv-3", "Anime") } },
                    },
                },
                "/api/v2/collections/capabilities" => new { item_reorder = true, imports = true, import_sources = new[] { "mdblist", "tmdb" } },
                "/api/v2/user/libraries" => new { items = new object[] { new { id = 1, name = "Movies", type = "movie", enabled = true }, new { id = 2, name = "Shows", type = "show", enabled = true } } },
                "/api/v2/profiles" => new { items = new[] { new { id = "me", name = "Fixture" }, new { id = "other", name = "Other profile" } } },
                "/api/v2/catalog" => new { items = Array.Empty<object>(), total = 0, total_exact = true, page = new { has_more = false } },
                _ => new { items = Array.Empty<object>() },
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
        }

        private static object Collection(string id, string name, string creator, string type, bool shared = false)
            => new { id, creator_profile_id = creator, profile_id = creator, name, collection_type = type, is_shared = shared, item_count = 5 };
        private static object Card(string id, string title) => new { id, title, item_count = 8 };
    }
}
