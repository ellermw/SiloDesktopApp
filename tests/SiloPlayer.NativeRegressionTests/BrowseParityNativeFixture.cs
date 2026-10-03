using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class BrowseParityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        var wire = new Handler(); using var client = new HttpClient(wire); var api = new SiloApiClient(client); api.SetBaseUrl("https://browse-native.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("browse-fixture", profile: new() { Id = "browse-fixture", Name = "Fixture" });
        var catalog = new CatalogApi(api); var settings = new SettingsApi(api); var collections = new CollectionsApi(api); var people = new PeopleApi(api);
        var presentation = new UICustomizationService(settings);
        var overrides = new Dictionary<Type, Func<object>>
        {
            [typeof(CatalogApi)] = () => catalog, [typeof(SettingsApi)] = () => settings, [typeof(CollectionsApi)] = () => collections,
            [typeof(PeopleApi)] = () => people, [typeof(AuthService)] = () => auth, [typeof(UICustomizationService)] = () => presentation,
            [typeof(RequestsApi)] = () => new RequestsApi(api),
            [typeof(SiloApiClient)] = () => api,
            [typeof(LibraryViewModel)] = () => new LibraryViewModel(catalog),
            [typeof(EventChannelClient)] = () => new EventChannelClient(api, auth),
            [typeof(PersonDetailViewModel)] = () => new PersonDetailViewModel(people, catalog, api),
            [typeof(CollectionEditorViewModel)] = () => new CollectionEditorViewModel(collections, catalog, settings, auth),
            [typeof(SmartCollectionWizardViewModel)] = () => new SmartCollectionWizardViewModel(catalog, collections, new AuthApi(api), auth),
            [typeof(SearchViewModel)] = () => new SearchViewModel(catalog, people, new RequestsApi(api), settings),
            [typeof(CalendarViewModel)] = () => new CalendarViewModel(catalog, previous.GetRequiredService<SettingsService>()) { HasLoaded = true },
            [typeof(NavigationService)] = () => new NavigationService(),
        };
        servicesField.SetValue(null, new OverrideServices(previous, overrides));
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_HERO_ICONS") == "1")
            {
                await HeroesAsync(parent);
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_GUIDED_STYLE") == "1")
            {
                await GuidedInputStyleAsync();
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_GUIDED_RESPONSIVE") == "1")
            {
                await GuidedResponsiveGroupsAsync();
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_GUIDED_DIAGNOSTIC") == "1")
            {
                await GuidedInputHeightAsync(parent);
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CHIPS") == "1")
            {
                await CatalogChipRemovalAsync(parent);
                await SearchCollectionChipsAsync(parent);
                return;
            }
            await FiltersAsync(parent);
            await PersonAsync(parent);
            await CalendarAsync(parent, presentation);
            await CollectionsAsync(parent);
            await CollectionPagingAsync(parent, wire);
            await SearchAsync(parent, wire);
            await RequestsAsync(parent, wire, presentation);
            await HeroesAsync(parent);
            await QuickSearchAsync(parent, wire);
            await GuidedInputHeightAsync(parent);
            await CatalogChipRemovalAsync(parent);
            await SearchCollectionChipsAsync(parent);
            await GuidedResponsiveGroupsAsync();
            Program.Log("PASS: browse native grouped filters/ranges, person responsive bio/actions, calendar sizes/watched/focus, collection sections, search shared sheet rendered.");
        }
        finally { parent.Children.Clear(); servicesField.SetValue(null, previous); }
    }

    private static async Task FiltersAsync(StackPanel parent)
    {
        var draft = new QueryDefinition { Match = "any", Groups = [new() { Match = "all", Rules = [new() { Field = "year", Op = "between", Value = new[] { 1980d, 1999d } }, new() { Field = "actor", Op = "is", Value = "Al Pacino" }] }, new() { Match = "any", Rules = [new() { Field = "genre", Op = "is", Value = "Crime" }] }] };
        var editor = new QueryFilterEditor { Width = 460, Height = 640 }; editor.Load(draft, "video", 11);
        parent.Children.Add(editor); await LayoutAsync(editor, 460, 640);
        // Invoke the same mode transition used by the native button without synthesizing unsupported input.
        Invoke(editor, "ShowMode", true); await LayoutAsync(editor, 460, 640);
        var boxes = Descendants<TextBox>(editor).Where(box => box.PlaceholderText is "From" or "To").ToList();
        if (boxes.Count != 2 || !editor.IsValid) throw new InvalidOperationException("Native range inputs missing or invalid on load.");
        boxes.Single(box => box.PlaceholderText == "To").Text = "2001";
        // WinUI delivers TextChanged on its dispatcher; observe the same settled
        // state a user has before applying the filter, rather than the setter.
        await LayoutAsync(editor, 460, 640);
        var editedRange = QueryRuleValues.Format(draft.Groups[0].Rules[0].Value);
        if (editedRange != "1980, 2001") throw new InvalidOperationException($"Native range edit lost numeric pair: field={draft.Groups[0].Rules[0].Field}, op={draft.Groups[0].Rules[0].Op}, value={editedRange}, from={boxes[0].Text}, to={boxes[1].Text}, valid={editor.IsValid}.");
        Invoke(editor, "ShowMode", false); Invoke(editor, "ShowMode", true);
        if (draft.Match != "any" || draft.Groups.Count != 2 || draft.Groups[0].Rules[1].Value?.ToString() != "Al Pacino") throw new InvalidOperationException("Native mode switch lost grouped query state.");
        await CaptureAsync(editor, "browse-query-editor-narrow.png");
        var yearField = Descendants<ComboBox>(editor).Single(combo => combo.SelectedItem?.ToString() == "Year");
        var rangeInputs = Descendants<TextBox>(editor).Where(box => box.PlaceholderText is "From" or "To").ToArray();
        var fieldTop = yearField.TransformToVisual(editor).TransformPoint(new(0, 0)).Y;
        var rangeTops = rangeInputs.Select(box => box.TransformToVisual(editor).TransformPoint(new(0, 0)).Y).ToArray();
        Program.Log($"Grouped filter row: fieldY={fieldTop}, rangeY={string.Join(",", rangeTops)}, fieldHeight={yearField.ActualHeight}, rangeHeight={string.Join(",", rangeInputs.Select(box => box.ActualHeight))}.");
        if (rangeTops.Any(top => Math.Abs(top - fieldTop) > .5) || yearField.ActualHeight > 32.5 || rangeInputs.Any(box => box.ActualHeight > 32.5))
            throw new InvalidOperationException("Native grouped range controls must share the compact32px field/operator row.");
        parent.Children.Remove(editor);
        var guidedEditor = new QueryFilterEditor();
        foreach (var scope in new[] { "video", "audiobook" })
        {
            guidedEditor.Load(new(), scope, 11);
            var guided = Field<StackPanel>(guidedEditor, "_guided");
            parent.Children.Add(guidedEditor); await LayoutAsync(guidedEditor, 345, 900);
            var labels = Descendants<Control>(guided).Select(child => child switch { AutoSuggestBox box => box.Header is TextBlock text ? text.Text : box.Header?.ToString(), ComboBox box => box.Header?.ToString(), NumberBox box => box.Header?.ToString(), _ => null }).Where(label => label != null).ToArray();
            var expected = scope == "video" ? new[] { "Genres", "Decade", "Year From", "Year To", "Minimum IMDb Rating", "Content Rating", "Original Language", "Actor" } : new[] { "Genres", "Decade", "Year From", "Year To", "Original Language", "Author", "Narrator", "Series" };
            Program.Log($"Guided {scope}: {string.Join(",", labels)}.");
            if (!labels.Take(expected.Length).SequenceEqual(expected)) throw new InvalidOperationException($"Guided {scope} form order differs from current official source.");
            if (scope == "audiobook" && labels.Any(label => label is "Minimum IMDb Rating" or "Content Rating" or "Resolution" or "Actor" or "HDR" or "Studio" or "Network")) throw new InvalidOperationException("Audiobook guided filters expose irrelevant video fields.");
            foreach (var label in expected)
            {
                var header = Descendants<TextBlock>(guided).First(text => text.Text == label);
                if (Math.Abs(header.ActualHeight - 14) > .5) throw new InvalidOperationException($"Guided {label} rendered label must have14px leading; actual={header.ActualHeight}.");
            }
            parent.Children.Remove(guidedEditor);
        }
    }

    private static async Task GuidedInputStyleAsync()
    {
        foreach (var width in new[] { 460, 1280 })
        {
            var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], RequestedTheme = ElementTheme.Dark };
            var window = new Window { Content = owner };
            try
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
                window.AppWindow.Show(false); await Task.Delay(120);
                var scale = owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                await Task.Delay(120);
                var editor = new QueryFilterEditor { Width = width == 460 ? 345 : 416, Height = 900 };
                editor.Load(new() { Groups = [new() { Rules = [new() { Field = "rating_imdb", Op = "gte", Value = 7.3 }] }] }, "video", 22);
                owner.Children.Add(editor); await LayoutAsync(editor, editor.Width, 900);
                var genre = Descendants<AutoSuggestBox>(editor).First();
                var genreInput = Descendants<TextBox>(genre).Single();
                var chrome = Descendants<Border>(genreInput).Single(border => border.Name == "BorderElement");
                var rating = Descendants<NumberBox>(editor).Single(control => control.Header?.ToString() == "Minimum IMDb Rating");
                var ratingInput = Descendants<TextBox>(rating).Single();
                Program.Log($"Guided {width}: actual chrome radius={chrome.CornerRadius}, brush={chrome.Background}; numerical font={ratingInput.FontSize}, step={rating.SmallChange}, spin={rating.SpinButtonPlacementMode}, value={rating.Value}.");
                if (Math.Abs(chrome.CornerRadius.TopLeft - 10) > .01)
                    throw new InvalidOperationException("Actual guided input chrome must render the official10px radius.");
                var background = (SolidColorBrush)Application.Current.Resources["AppBackgroundBrush"];
                if (chrome.Background is not SolidColorBrush actual || actual.Color != background.Color)
                    throw new InvalidOperationException("Actual guided input body must use bg-background.");
                if (ratingInput.FontSize != (width < 768 ? 16 : 14) || rating.SmallChange != .1 || rating.SpinButtonPlacementMode == Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Inline || rating.Value != 7.3)
                    throw new InvalidOperationException("Actual guided IMDb input must preserve decimals, step by0.1, use responsive numerical typography and avoid permanent inline spinner chrome.");
                await CaptureAsync(editor, $"browse-guided-style-{width}.png");
            }
            finally { window.Close(); }
        }
    }

    private static async Task GuidedResponsiveGroupsAsync()
    {
        foreach (var width in new[] { 460, 1280 })
        {
            var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], RequestedTheme = ElementTheme.Dark };
            var window = new Window { Content = owner };
            try
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
                window.AppWindow.Show(false); await Task.Delay(120);
                var scale = owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                await Task.Delay(120);
                var editor = new QueryFilterEditor { Width = width == 460 ? 345 : 416, Height = 900 };
                editor.Load(new(), "audiobook", 11); owner.Children.Add(editor); await LayoutAsync(editor, editor.Width, 900);
                var decade = Descendants<ComboBox>(editor).Single(control => control.Header?.ToString() == "Decade");
                var from = Descendants<NumberBox>(editor).Single(control => control.Header?.ToString() == "Year From");
                var to = Descendants<NumberBox>(editor).Single(control => control.Header?.ToString() == "Year To");
                var decadeOrigin = decade.TransformToVisual(editor).TransformPoint(new(0, 0));
                var fromOrigin = from.TransformToVisual(editor).TransformPoint(new(0, 0));
                var toOrigin = to.TransformToVisual(editor).TransformPoint(new(0, 0));
                Program.Log($"Guided actual viewport={owner.XamlRoot.Size}, decade={decadeOrigin}, from={fromOrigin}, to={toOrigin}.");
                if (width >= 768 ? Math.Abs(fromOrigin.Y - decadeOrigin.Y) > .5 || Math.Abs(toOrigin.Y - decadeOrigin.Y) > .5 || fromOrigin.X <= decadeOrigin.X || toOrigin.X <= fromOrigin.X : fromOrigin.Y <= decadeOrigin.Y || toOrigin.Y <= fromOrigin.Y)
                    throw new InvalidOperationException("Guided decade/year controls must share the desktop three-column row and stack below the768px viewport breakpoint.");
                await CaptureAsync(editor, $"browse-guided-responsive-{width}.png");
            }
            finally { window.Close(); }
        }
    }

    private static async Task GuidedInputHeightAsync(StackPanel parent)
    {
        var editor = new QueryFilterEditor { Width = 345, Height = 900 }; editor.Load(new(), "audiobook", 11);
        parent.Children.Add(editor);
        try
        {
            await LayoutAsync(editor, 345, 900);
            var suggestion = Descendants<AutoSuggestBox>(editor).First();
            var input = Descendants<TextBox>(suggestion).Single();
            Program.Log($"Guided audiobook genre input actual height={input.ActualHeight}.");
            foreach (var part in Descendants<FrameworkElement>(input))
                Program.Log($"Guided input part {part.GetType().Name} name={part.Name}, origin={part.TransformToVisual(input).TransformPoint(new(0, 0))}, size={part.ActualWidth}x{part.ActualHeight}.");
            var inputBorder = Descendants<Border>(input).Single(part => part.Name == "BorderElement");
            if (Math.Abs(inputBorder.ActualHeight - 36) > .5) throw new InvalidOperationException($"Guided fields require official36px input chrome; genre border actual={inputBorder.ActualHeight}, total including header={input.ActualHeight}.");
        }
        finally { parent.Children.Remove(editor); }
    }

    private static async Task SearchCollectionChipsAsync(StackPanel parent)
    {
        var search = new SearchPage { Width = 460, Height = 800 };
        var collection = new CollectionBrowsePage { Width = 460, Height = 800 };
        foreach (var page in new Page[] { search, collection })
        {
            var query = page is SearchPage ? search.ViewModel.AdvancedQuery : Field<QueryDefinition>(collection, "_browseQuery");
            query.Groups.Clear();
            query.Groups.Add(new() { Rules = [new() { Field = "genre", Op = "is", Value = "Crime" }, new() { Field = "year", Op = "gte", Value = 1980 }, new() { Field = "year", Op = "lte", Value = 1999 }] });
            parent.Children.Add(page);
            try
            {
                await LayoutAsync(page, 460, 800);
                // A constructed SearchPage starts on its empty-search surface.
                // Publish the real queried state before measuring descendants;
                // a visible badge inside collapsed ResultsState is not rendered.
                if (page is SearchPage)
                {
                    search.ViewModel.Query = "Crime";
                    Invoke(search, "UpdateResultsState");
                }
                Invoke(page, page is SearchPage ? "UpdateActiveResultFilters" : "UpdateActiveFilterBadge");
                await LayoutAsync(page, 460, 800);
                var panel = (Panel)page.FindName(page is SearchPage ? "ActiveResultFiltersPanel" : "ActiveFiltersPanel");
                for (var attempt = 0; attempt < 20 && panel.Children.OfType<FrameworkElement>().Any(badge => badge.ActualHeight == 0); attempt++)
                    await LayoutAsync(page, 460, 800);
                if (page is SearchPage && ((FrameworkElement)page.FindName("ResultsState")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Search chip visual fixture did not enter its actual queried surface.");
                var chips = ChipRemoveButtons(panel);
                AssertChipVisuals(panel);
                Program.Log($"{page.GetType().Name} active chips: {string.Join(";", chips.Select(button => button.Content))}.");
                if (chips.Length != 2 || !chips.Any(button => AutomationProperties.GetName(button) == "Remove Year: 1980–1999"))
                    throw new InvalidOperationException("Search and collection browse must share readable, combined year chips with native removal actions.");
                var year = chips.Single(button => AutomationProperties.GetName(button) == "Remove Year: 1980–1999");
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(year).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await LayoutAsync(page, 460, 800);
                if (query.Groups[0].Rules.Count != 1 || query.Groups[0].Rules[0].Field != "genre" || panel.Children.Count != 1)
                    throw new InvalidOperationException("Native search/collection year removal changed unrelated query refinement or did not update count.");
            }
            finally { parent.Children.Remove(page); }
        }
    }

    private static async Task CatalogChipRemovalAsync(StackPanel parent)
    {
        foreach (var source in new[] { "library", "favorites", "history" })
        {
            var page = new CatalogPage { Width = 460, Height = 800 }; parent.Children.Add(page);
            Set(page, "_source", source);
            try
            {
                await LayoutAsync(page, 460, 800);
                Invoke(page, "InitializeQueryFilters", new CatalogFiltersResponse());
                var query = Field<QueryDefinition>(page, "_catalogQuery"); query.Groups.Clear();
                query.Groups.Add(new() { Rules = [new() { Field = "genre", Op = "is", Value = "Crime" }, new() { Field = "year", Op = "gte", Value = 1980 }, new() { Field = "year", Op = "lte", Value = 1999 }, new() { Field = "rating_imdb", Op = "gte", Value = 7 }] });
                Invoke(page, "UpdateQueryFilterChips"); await LayoutAsync(page, 460, 800);
                var chips = ChipRemoveButtons(Field<WrapPanel>(page, "_queryChips"));
                AssertChipVisuals(Field<WrapPanel>(page, "_queryChips"));
                Program.Log($"Catalog {source} chips={string.Join(";", chips.Select(button => button.Content))}, count={((TextBlock)page.FindName("FilterCountText")).Text}.");
                if (chips.Length != 3 || !chips.Any(button => AutomationProperties.GetName(button) == "Remove Genre: Crime") || !chips.Any(button => AutomationProperties.GetName(button) == "Remove Year: 1980–1999"))
                    throw new InvalidOperationException("Native catalog chips must combine year bounds, use readable labels/count and expose direct remove controls.");
                var genre = chips.Single(button => AutomationProperties.GetName(button) == "Remove Genre: Crime");
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(genre).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await LayoutAsync(page, 460, 800);
                if (query.Groups[0].Rules.Any(rule => rule.Field == "genre") || query.Groups[0].Rules.Count != 3 || ((TextBlock)page.FindName("FilterCountText")).Text != "2") throw new InvalidOperationException("Removing one native catalog chip changed unrelated refinements or count.");
                var year = ChipRemoveButtons(Field<WrapPanel>(page, "_queryChips")).Single(button => AutomationProperties.GetName(button) == "Remove Year: 1980–1999");
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(year).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await LayoutAsync(page, 460, 800);
                if (query.Groups[0].Rules.Count != 1 || query.Groups[0].Rules[0].Field != "rating_imdb" || ((TextBlock)page.FindName("FilterCountText")).Text != "1") throw new InvalidOperationException("Removing the year badge must remove both bounds and retain IMDb refinement.");
                query.Groups.Clear();
                query.Groups.Add(new() { Match = "any", Rules = [new() { Field = "year", Op = "gte", Value = 1980 }, new() { Field = "year", Op = "lte", Value = 1999 }] });
                Invoke(page, "UpdateQueryFilterChips"); await LayoutAsync(page, 460, 800);
                if (Field<WrapPanel>(page, "_queryChips").Children.Count != 2) throw new InvalidOperationException("Independent OR bounds must not be presented as one AND range.");
                Invoke(page, "ClearQueryFilters"); await LayoutAsync(page, 460, 800);
                if (Field<WrapPanel>(page, "_queryChips").Children.Count != 0 || ((FrameworkElement)page.FindName("FilterCountBadge")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Catalog clear-all retained chips/count.");
            }
            finally { parent.Children.Remove(page); }
        }
    }

    private static Button[] ChipRemoveButtons(Panel panel) => Descendants<Button>(panel)
        .Where(button => AutomationProperties.GetName(button).StartsWith("Remove ", StringComparison.Ordinal)).ToArray();

    private static void AssertChipVisuals(Panel panel)
    {
        foreach (var badge in panel.Children.OfType<FrameworkElement>())
        {
            Program.Log($"Filter badge actual {badge.GetType().Name} {badge.ActualWidth:F3}×{badge.ActualHeight:F3}.");
            // Exact official DOM: 16px line + 2px padding each side + subpixel
            // transparent border = 21.333px. Native device-pixel rounding may add <1px.
            if (Math.Abs(badge.ActualHeight - 21.333333) > 1)
                throw new InvalidOperationException("Filter badge chrome must match the official 21.333px secondary badge, instead of a full-size action button.");
            var caption = Descendants<TextBlock>(badge).FirstOrDefault(text => text.Text.Contains(":", StringComparison.Ordinal));
            if (caption == null || caption.FontSize != 12 || caption.LineHeight != 16 || caption.FontWeight.Weight != 500)
                throw new InvalidOperationException("Filter badge caption must render at Outfit 12px/16px, weight 500.");
            var remove = ChipRemoveButtons(panel).Single(button => Descendants<Button>(badge).Contains(button) || ReferenceEquals(button, badge));
            if (remove.ActualWidth != 16 || remove.ActualHeight != 16)
                throw new InvalidOperationException("Filter badge removal must have its own 16px hit target.");
        }
    }

    private static async Task PersonAsync(StackPanel parent)
    {
        var page = new PersonDetailPage(); Set(page, "_isActive", true);
        page.ViewModel.Person = new Person { Id = "person-fixture", Name = "A Performer With a Longer Name", Bio = string.Join(" ", Enumerable.Repeat("A complete biography remains readable without an artificial line clamp.", 24)) };
        parent.Children.Add(page);
        foreach (var width in new[] { 1280d, 460d })
        {
            page.Width = width; page.Height = 800; Invoke(page, "ApplyResponsiveLayout", width); Invoke(page, "UpdateUI"); await LayoutAsync(page, width, 800);
            var info = (FrameworkElement)page.FindName("PersonInfo"); var bio = (TextBlock)page.FindName("BioText");
            if (bio.MaxLines != 0 || bio.MaxWidth != 672 || Grid.GetRow(info) != (width < 1024 ? 1 : 0)) throw new InvalidOperationException("Person biography or responsive stack differs from source contract.");
            if (((Button)page.FindName("EditPersonButton")).Visibility != Visibility.Collapsed || ((Button)page.FindName("RefreshPersonButton")).Visibility != Visibility.Visible) throw new InvalidOperationException("Ordinary person viewer actions are wrong.");
            await CaptureAsync(page, $"browse-person-{width}.png");
        }
        parent.Children.Remove(page); page.ViewModel.Cancel();
    }

    private static async Task CalendarAsync(StackPanel parent, UICustomizationService presentation)
    {
        var page = new CalendarPage { Width = 1280, Height = 800 }; parent.Children.Add(page);
        page.ViewModel.Days.Add(new() { Date = "2026-10-01", Items = [new() { ContentId = "fixture-movie", Type = "movie", Title = "Watched Feature", Watched = true }, new() { ContentId = "fixture-episode", Type = "episode", SeriesId = "fixture-series", Title = "Upcoming Episode", Watched = false }] });
        foreach (var (size, width, expected) in new[] { ("compact", 460d, 120d), ("standard", 900d, 160d), ("large", 1280d, 220d) })
        {
            typeof(UICustomizationService).GetProperty("CardPresentation")!.SetValue(presentation, new CardPresentation { PosterSize = size });
            page.Width = width; Invoke(page, "ApplyCalendarLayout", width); Invoke(page, "BuildDayRows"); Invoke(page, "SelectDay", "2026-10-01", true); await LayoutAsync(page, width, 800);
            if (Field<double>(page, "_eventCardWidth") != expected) throw new InvalidOperationException("Calendar ignores poster-size preference.");
            if (!Descendants<TextBlock>(page).Any(text => text.Text == "FOCUSED")) throw new InvalidOperationException("Calendar focus badge missing.");
            if (!Descendants<Border>(page).Any(border => border.Width == 32 && border.Height == 32 && border.Child is FontIcon)) throw new InvalidOperationException("Watched calendar badge missing.");
            await CaptureAsync(page, $"browse-calendar-{size}.png");
        }
        parent.Children.Remove(page);
    }

    private static async Task CollectionsAsync(StackPanel parent)
    {
        var page = new CollectionEditorPage { Width = 460, Height = 800 }; parent.Children.Add(page);
        page.ViewModel.CollectionType = "manual"; Invoke(page, "UpdateSectionVisibility"); await LayoutAsync(page, 460, 800);
        if (((FrameworkElement)page.FindName("ImportedDisplayOptions")).Visibility != Visibility.Visible) throw new InvalidOperationException("Manual collection display filters are hidden.");
        await CaptureAsync(page, "browse-manual-collection-narrow.png"); parent.Children.Remove(page);
        var wizard = new SmartCollectionWizardPage { Width = 1280, Height = 800 }; Set(wizard, "_loaded", true); parent.Children.Add(wizard);
        Invoke(wizard, "PopulateStaticCombos"); Invoke(wizard, "BuildRulesPanel"); Invoke(wizard, "UpdatePreviewState"); Invoke(wizard, "ShowStep", 1); await LayoutAsync(wizard, 1280, 800);
        if (Field<QueryFilterEditor>(wizard, "_rulesEditor") == null) throw new InvalidOperationException("Wizard lacks shared guided/advanced editor.");
        await CaptureAsync(wizard, "browse-smart-wizard-wide.png"); parent.Children.Remove(wizard);
        var scoped = new SmartCollectionWizardPage { Width = 460, Height = 900 }; Set(scoped, "_loaded", true);
        parent.Children.Add(scoped); Invoke(scoped, "PopulateStaticCombos"); Invoke(scoped, "BuildRulesPanel"); Invoke(scoped, "UpdatePreviewState"); Invoke(scoped, "ShowStep", 1);
        ((ComboBox)scoped.FindName("MediaScopeCombo")).SelectedIndex = 5; await LayoutAsync(scoped, 460, 900);
        var shared = Field<QueryFilterEditor>(scoped, "_rulesEditor");
        if (Field<string?>(shared, "_scope") != "audiobook") throw new InvalidOperationException("Wizard scope change leaves filter editor suggestions scoped to previous media.");
        scoped.ViewModel.Libraries.Add(new() { Id = 11, Name = "Audiobooks", Type = "audiobook" }); Invoke(scoped, "BuildLibrariesPanel");
        var libraryPicker = (Button)scoped.FindName("LibraryPickerButton");
        var libraryOption = ((MenuFlyout)libraryPicker.Flyout).Items.OfType<ToggleMenuFlyoutItem>().Single(item => item.Tag is int id && id == 11);
        libraryOption.IsChecked = true; Invoke(scoped, "LibraryToggle_Changed", libraryOption, new RoutedEventArgs()); await LayoutAsync(scoped, 460, 900);
        if (((TextBlock)scoped.FindName("LibraryPickerLabel")).Text != "Audiobooks") throw new InvalidOperationException("Wizard library picker does not reflect its scoped selection.");
        if (Field<int?>(shared, "_libraryId") != 11) throw new InvalidOperationException("Wizard library change leaves suggestions scoped to previous library.");
        await CaptureAsync(scoped, "browse-smart-wizard-scoped-narrow.png");
        Invoke(scoped, "FiltersButton_Click", scoped, new RoutedEventArgs()); await LayoutAsync(scoped, 460, 900); await Task.Delay(250);
        var sheet = (SlideSheet)scoped.FindName("WizardFiltersSheet");
        if (!sheet.IsOpen || Math.Abs(sheet.PreferredWidth - 345) > .5 || !Descendants<QueryFilterEditor>(sheet).Any()) throw new InvalidOperationException("Wizard Filters action must open the shared editor in its345px right sheet.");
        var guidedMode = Descendants<Button>(sheet).Single(button => button.Content?.ToString() == "Guided");
        var mediaSelector = (FrameworkElement)scoped.FindName("SheetMediaScopeCombo");
        if (guidedMode.TransformToVisual(sheet).TransformPoint(new(0, 0)).Y >= mediaSelector.TransformToVisual(sheet).TransformPoint(new(0, 0)).Y) throw new InvalidOperationException("Wizard mode selector belongs in the sheet header above media scope.");
        await CaptureAsync(scoped, "browse-smart-wizard-sheet-narrow.png");
        Invoke(scoped, "ClearWizardFilters_Click", scoped, new RoutedEventArgs()); await LayoutAsync(scoped, 460, 900);
        if (scoped.ViewModel.SelectedLibraryIds.Count != 0 || scoped.ViewModel.RuleDefinition.Groups.Count != 0 || scoped.ViewModel.MediaScope != "audiobook") throw new InvalidOperationException("Wizard Clear All must clear secondary filters/libraries and preserve media scope.");
        Invoke(scoped, "DoneWizardFilters_Click", scoped, new RoutedEventArgs());
        if (sheet.IsOpen) throw new InvalidOperationException("Wizard Done must close its filter sheet.");
        parent.Children.Remove(scoped);
    }

    private static async Task SearchAsync(StackPanel parent, Handler wire)
    {
        var page = new SearchPage { Width = 460, Height = 800 }; parent.Children.Add(page); await LayoutAsync(page, 460, 800);
        if (page.FindName("SearchQueryFilters") is not QueryFilterEditor) throw new InvalidOperationException("Search lacks shared filter editor.");
        Set(page, "_isNavigated", true); Set(page, "_navigationGeneration", 1L);
        wire.SearchQueries.Clear();
        await (Task)Invoke(page, "OpenQueryAsync", "TypedFixture", 1L, "series")!;
        if (page.ViewModel.MediaType != "series" || wire.SearchQueries.Count != 1 || !wire.SearchQueries[0].Contains("type=series"))
            throw new InvalidOperationException("Typed search must apply the whole-series route scope before its sole initial catalog query.");
        await LayoutAsync(page, 460, 800); await CaptureAsync(page, "browse-search-typed-series-narrow.png");
        page.ViewModel.Query = ""; ((TextBox)page.FindName("SearchBox")).Text = ""; Field<DispatcherTimer?>(page, "_searchDebounce")?.Stop(); Invoke(page, "UpdateResultsState");
        await CaptureAsync(page, "browse-search-empty-narrow.png");
        Invoke(page, "OpenResultFilters_Click", page, new RoutedEventArgs()); await LayoutAsync(page, 460, 800); await Task.Delay(250);
        var searchSheet = (SlideSheet)page.FindName("ResultFiltersSheet");
        AssertFilterHeader(searchSheet);
        await CaptureAsync(page, "browse-search-filters-narrow.png"); searchSheet.IsOpen = false;
        Set(page, "_isNavigated", false); parent.Children.Remove(page); page.ViewModel.CancelPendingSearch();

        var catalog = new CatalogPage { Width = 460, Height = 800 }; parent.Children.Add(catalog); await LayoutAsync(catalog, 460, 800);
        Invoke(catalog, "InitializeQueryFilters", new CatalogFiltersResponse());
        var catalogSheet = (SlideSheet)catalog.FindName("FiltersSheet"); catalogSheet.IsOpen = true;
        await LayoutAsync(catalog, 460, 800); await Task.Delay(250); AssertFilterHeader(catalogSheet);
        await CaptureAsync(catalog, "browse-catalog-filters-narrow.png"); catalogSheet.IsOpen = false; parent.Children.Remove(catalog);
    }

    private static void AssertFilterHeader(SlideSheet sheet)
    {
        var modes = Descendants<Button>(sheet).Where(button => button.Content?.ToString() is "Guided" or "Advanced").ToArray();
        if (modes.Length != 2 || Math.Abs(sheet.PreferredWidth - 345) > .5) throw new InvalidOperationException("Filter sheet must have one shared mode pair and345px width at460.");
        var descriptions = Descendants<TextBlock>(sheet).Where(text => text.Text == "Refine your catalog results" && text.Visibility == Visibility.Visible).ToArray();
        if (descriptions.Length != 1) throw new InvalidOperationException("Filter sheet rendered duplicate descriptions.");
        var editor = Descendants<QueryFilterEditor>(sheet).Single();
        if (modes[0].TransformToVisual(sheet).TransformPoint(new(0, 0)).Y >= editor.TransformToVisual(sheet).TransformPoint(new(0, 0)).Y)
            throw new InvalidOperationException("Filter modes must render in the sheet header above its editor.");
    }

    private static async Task RequestsAsync(StackPanel parent, Handler wire, UICustomizationService presentation)
    {
        var page = new RequestBrowsePage { Width = 460, Height = 800 };
        Set(page, "_navigation", new RequestBrowseNavigation("genre", "drama")); Set(page, "_initialized", true);
        parent.Children.Add(page); await LayoutAsync(page, 460, 800);
        await (Task)Invoke(page, "LoadAsync")!;
        var session = Field<RequestBrowseSession>(page, "_browse");
        if (session.Results.Count != 1 || !session.HasMore) throw new InvalidOperationException("Request browse did not traverse empty first page.");
        await session.LoadMoreAsync(); await LayoutAsync(page, 460, 800);
        if (session.MoreError == null || session.Results.Count != 1 || ((Button)page.FindName("LoadMoreButton")).Content?.ToString() != "Try again") throw new InvalidOperationException("Later request failure lost results or footer retry.");
        await CaptureAsync(page, "browse-request-later-error-narrow.png");
        wire.FailMore = false; await session.LoadMoreAsync();
        if (session.Results.Count != 2 || session.HasMore) throw new InvalidOperationException("Request footer retry duplicated earlier results or failed to finish.");
        foreach (var (size, width, expectedColumns) in new[] { ("compact", 1280d, 10), ("standard", 900d, 5), ("large", 460d, 2) })
        {
            typeof(UICustomizationService).GetProperty("CardPresentation")!.SetValue(presentation, new CardPresentation { PosterSize = size });
            page.Width = width; Invoke(page, "ApplyLayout", width); await LayoutAsync(page, width, 800);
            var gutter = width < 640 ? 16 : width < 1024 ? 24 : width < 1280 ? 40 : 48;
            var gap = size == "large" ? 16 : 12;
            var expected = Math.Floor((width - gutter * 2 - (expectedColumns - 1) * gap) / expectedColumns);
            if (Field<double>(page, "_browseCardWidth") != expected || ((ComboBox)page.FindName("SortCombo")).Width != 160) throw new InvalidOperationException("Request preference grid or sort geometry differs.");
            var tile = (FrameworkElement)page.FindName("HeaderTile");
            var back = (FrameworkElement)page.FindName("BackButton");
            var tileOrigin = tile.TransformToVisual(page).TransformPoint(new(0, 0));
            var backOrigin = back.TransformToVisual(page).TransformPoint(new(0, 0));
            Program.Log($"Request {size}: page={page.ActualWidth}, tile={tileOrigin.X},{tileOrigin.Y} {tile.ActualWidth}x{tile.ActualHeight}, back={backOrigin.X},{backOrigin.Y} {back.ActualWidth}x{back.ActualHeight}, card={Field<double>(page, "_browseCardWidth")}.");
            if (Math.Abs(tileOrigin.X - gutter) > .5 || Math.Abs(tileOrigin.Y - (width < 640 ? 64 : 72)) > .5 || Math.Abs(backOrigin.X - 8) > .5 || Math.Abs(backOrigin.Y - (width < 640 ? 16 : 24)) > .5)
                throw new InvalidOperationException("Request header/back rendered offsets differ from paired WebUI.");
            await CaptureAsync(page, $"browse-request-{size}.png");
        }
        session.Cancel(); Field<CancellationTokenSource>(page, "_lifetime").Cancel(); parent.Children.Remove(page);
    }

    private static async Task CollectionPagingAsync(StackPanel parent, Handler wire)
    {
        var page = new CollectionBrowsePage { Width = 460, Height = 800 };
        Set(page, "_currentArgs", new CollectionBrowsePage.NavArgs { CollectionId = "paging-fixture", IsUserCollection = true });
        await (Task)Invoke(page, "LoadFirstPageAsync")!;
        await (Task)Invoke(page, "LoadMoreAsync")!;
        if (!wire.CollectionQueries.Last().Contains("cursor=first-window"))
            throw new InvalidOperationException("Collection subsequent window omitted the captured server window cursor.");
        wire.CollectionQueries.Clear();
        await (Task)Invoke(page, "LoadFirstPageAsync")!;
        var heldContext = new HeldContext();
        var uiContext = SynchronizationContext.Current;
        Task oldPage;
        SynchronizationContext.SetSynchronizationContext(heldContext);
        try { oldPage = (Task)Invoke(page, "LoadMoreAsync")!; }
        finally { SynchronizationContext.SetSynchronizationContext(uiContext); }
        await heldContext.Enqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // SiloApiClient.GetAsync captures the caller's context too. Complete
        // that wrapper first; the page continuation is now queued with a
        // successfully parsed response before its owner is superseded.
        heldContext.DrainOne();
        if (!heldContext.HasCallbacks) throw new InvalidOperationException("Collection fixture did not hold its page continuation.");
        wire.NewCollectionWindow = true;
        await (Task)Invoke(page, "LoadFirstPageAsync")!;
        heldContext.Drain(); await oldPage;
        var items = Field<System.Collections.ObjectModel.ObservableCollection<MediaItem>>(page, "_items");
        if (items.Count != 1 || items[0].ContentId != "new-window")
            throw new InvalidOperationException("A superseded collection continuation appended into the replacement window.");
        if (Field<bool>(page, "_isLoadingMore"))
            throw new InvalidOperationException("Superseded collection paging kept the replacement window busy.");
        Field<CancellationTokenSource>(page, "_loadCts").Cancel();
        wire.NewCollectionWindow = false;
        Program.Log("PASS: collection paging retains the server cursor and rejects an already completed old-window continuation after reset.");
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];
        internal TaskCompletionSource Enqueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_callbacks) _callbacks.Enqueue((callback, state));
            Enqueued.TrySetResult();
        }
        internal bool HasCallbacks { get { lock (_callbacks) return _callbacks.Count > 0; } }
        internal void DrainOne()
        {
            (SendOrPostCallback Callback, object? State) entry;
            lock (_callbacks) entry = _callbacks.Dequeue();
            entry.Callback(entry.State);
        }
        internal void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) entry;
                lock (_callbacks) { if (_callbacks.Count == 0) return; entry = _callbacks.Dequeue(); }
                entry.Callback(entry.State);
            }
        }
    }

    private static async Task HeroesAsync(StackPanel parent)
    {
        var oldWidth = parent.Width; var oldHeight = parent.Height;
        try
        {
            foreach (var width in new[] { 1280d, 460d })
            {
                parent.Width = width; parent.Height = 900; await LayoutAsync(parent, width, 900);
                var hero = new HeroCarousel { Width = width, ItemsSource = new List<MediaItem> { new() { ContentId = "long-title", Type = "movie", Year = 2026, Title = "A Long Feature Title That Remains Readable Across Every Line of the Home Hero", Overview = "A hero description with native responsive spacing." } } };
                parent.Children.Add(hero); await LayoutAsync(hero, width, 600); Invoke(hero, "UpdateHeightFromWindow");
                var ratio = width >= 1024 ? .66 : .54;
                if (Math.Abs(hero.Height - Math.Clamp(parent.ActualHeight * ratio, 380, 760)) > 1) throw new InvalidOperationException("Home hero viewport height differs.");
                if (((TextBlock)hero.FindName("HeroTitle")).MaxLines != 0) throw new InvalidOperationException("Hero title still clamped.");
                var title = (TextBlock)hero.FindName("HeroTitle");
                Program.Log($"Hero {width}: title={title.ActualWidth}x{title.ActualHeight}; font={title.FontFamily.Source}; size={title.FontSize}; line={title.LineHeight}.");
                var expectedLeading = width < 640 ? 40 : title.FontSize;
                if (title.LineHeight != expectedLeading) throw new InvalidOperationException($"Hero title line height differs from corrected-CSS official heading leading: actual={title.LineHeight}, expected={expectedLeading}.");
                var play = (Button)hero.FindName("PlayButton");
                var playVector = Descendants<Viewbox>(play).FirstOrDefault(vector => vector.Width == 16 && vector.Height == 16 && Descendants<Microsoft.UI.Xaml.Shapes.Path>(vector).Any(path => path.Fill is SolidColorBrush));
                var information = (Button)hero.FindName("MoreInfoButton");
                var infoVector = Descendants<Viewbox>(information).FirstOrDefault(vector => vector.Width == 16 && vector.Height == 16 && Descendants<Microsoft.UI.Xaml.Shapes.Path>(vector).Any(path => path.StrokeThickness == 2));
                Program.Log($"Hero rendered icons: filled play16={playVector != null}, info16={infoVector != null}.");
                if (playVector == null || infoVector == null) throw new InvalidOperationException("Hero actions must render the official filled16px Play and16px stroked Info vectors.");
                Program.Log($"Hero capsule ends: play={play.CornerRadius.TopLeft}/{play.ActualHeight}, info={information.CornerRadius.TopLeft}/{information.ActualHeight}.");
                if (Math.Abs(play.CornerRadius.TopLeft - play.ActualHeight / 2) > .5 || Math.Abs(information.CornerRadius.TopLeft - information.ActualHeight / 2) > .5) throw new InvalidOperationException("Actual Home Hero CTA must use circular capsule ends instead of full-width ellipses.");
                if (width < 640)
                {
                    var primary = (Button)hero.FindName("PlayButton"); var secondary = (Button)hero.FindName("MoreInfoButton");
                    Program.Log($"Hero pills actual={primary.ActualWidth}x{primary.ActualHeight},{secondary.ActualWidth}x{secondary.ActualHeight}.");
                    if (Math.Abs(primary.ActualHeight - 45.115) > 1 || Math.Abs(secondary.ActualHeight - 46.448) > 1)
                        throw new InvalidOperationException("Hero pill heights differ from corrected-CSS actual browser45.115/46.448px.");
                }
                await CaptureAsync(hero, $"browse-home-hero-{width}.png"); parent.Children.Remove(hero);
                var listening = new NowListeningHero { Width = width }; listening.Bind(new() { ContentId = "audio-fixture", Type = "audiobook", Title = "An Audiobook With a Longer Title", PositionSeconds = 1200, DurationSeconds = 7200 });
                parent.Children.Add(listening); await LayoutAsync(listening, width, 600);
                var cover = (Border)listening.FindName("CoverBorder"); var info = (FrameworkElement)listening.FindName("DeckInfo");
                if (cover.Width != (width < 640 ? 144 : 224) || Grid.GetRow(info) != (width < 640 ? 1 : 0)) throw new InvalidOperationException("Now Listening cover/stack geometry differs.");
                await CaptureAsync(listening, $"browse-now-listening-{width}.png"); parent.Children.Remove(listening);
            }
        }
        finally { parent.Width = oldWidth; parent.Height = oldHeight; }
    }

    private static async Task QuickSearchAsync(StackPanel parent, Handler wire)
    {
        // The surrounding browse controls use bounded page captures within a
        // wide host. A ContentDialog sizes itself from XamlRoot, so a narrow
        // page alone cannot verify its actual mobile viewport or placement.
        var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        var window = new Window { Content = owner };
        GlobalSearchDialog? dialog = null;
        Windows.Foundation.IAsyncOperation<ContentDialogResult>? showing = null;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 900));
            window.AppWindow.Show(false);
            await Task.Delay(160);
            var scale = owner.XamlRoot.RasterizationScale;
            if (Math.Abs(scale - 1) > .001)
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(460 * scale), (int)Math.Round(900 * scale)));
                await Task.Delay(160);
            }
            if (Math.Abs(owner.XamlRoot.Size.Width - 460) > .5 || Math.Abs(owner.XamlRoot.Size.Height - 900) > .5)
                throw new InvalidOperationException($"Quick-search fixture client viewport is not 460x900: {owner.XamlRoot.Size}.");
            dialog = new GlobalSearchDialog { XamlRoot = owner.XamlRoot };
            showing = dialog.ShowAsync();
            await Task.Delay(120);
            await Field<Task>(dialog, "_scopeLoadTask");
            var editableInput = (TextBox)dialog.FindName("SearchBox");
            var placeholder = Descendants<TextBlock>(editableInput).Single(text => text.Name == "PlaceholderTextContentPresenter");
            if (placeholder.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(placeholder.Text)) throw new InvalidOperationException("Quick empty native input lost its visible placeholder.");
            editableInput.Text = "Fixture"; await Task.Delay(120);
            if (placeholder.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Quick typed input did not hide its native placeholder.");
            Field<DispatcherTimer?>(dialog, "_debounceTimer")?.Stop();
            Field<List<MediaItem>>(dialog, "_results").AddRange([new() { ContentId = "movie-fixture", Type = "movie", Title = "Fixture movie" }, new() { ContentId = "episode-fixture", Type = "episode", Title = "Fixture episode" }]);
            Field<List<Person>>(dialog, "_peopleResults").Add(new() { Id = "person-fixture", Name = "Fixture Person" });
            Invoke(dialog, "Render"); await Task.Delay(120);
            var surface = (FrameworkElement)dialog.FindName("SearchSurface"); var scroll = (ScrollViewer)dialog.FindName("ResultsScroll");
            await CaptureAsync(dialog, "browse-quick-search-results.png");
            var origin = surface.TransformToVisual(null).TransformPoint(new(0, 0));
            Program.Log($"Quick search actual460: root={owner.XamlRoot.Size}, surface={origin.X},{origin.Y} {surface.ActualWidth}x{surface.ActualHeight}, configuredTop={dialog.Margin.Top}.");
            var frame = Descendants<Border>(dialog).Single(border => border.Name == "BackgroundElement" && border.Child is Grid { Name: "DialogSpace" });
            var frameOrigin = frame.TransformToVisual(null).TransformPoint(new(0, 0));
            if (Math.Abs(frameOrigin.X - 16) > 1 || Math.Abs(frameOrigin.Y - 180) > 1 || Math.Abs(frame.ActualWidth - 428) > 1 || scroll.MaxHeight > 352) throw new InvalidOperationException($"Quick search actual frame differs: frame={frameOrigin.X},{frameOrigin.Y} {frame.ActualWidth}x{frame.ActualHeight}, surface={origin.X},{origin.Y} {surface.ActualWidth}, root={owner.XamlRoot.Size.Width}x{owner.XamlRoot.Size.Height}; expected16,180,428.");
            var input = (TextBox)dialog.FindName("SearchBox");
            var character = input.GetRectFromCharacterIndex(0, false);
            var textView = Descendants<FrameworkElement>(input).Single(element => element.Name == "ScrollContentPresenter");
            var textOrigin = textView.TransformToVisual(input).TransformPoint(new(0, 0));
            var renderedCharacterY = textOrigin.Y + character.Y;
            Program.Log($"Quick focused input: character={character}, text view origin={textOrigin.X},{textOrigin.Y} size={textView.ActualWidth}x{textView.ActualHeight}, renderedCharacterY={renderedCharacterY}, frame radius={frame.CornerRadius.TopLeft}.");
            // The native tree's observed text/caret host is inside the
            // ScrollContentPresenter at y14. Character bounds are relative to
            // that host; include its actual transform into the48px input row.
            if (renderedCharacterY < 12 || renderedCharacterY > 17 || frame.CornerRadius.TopLeft != 8)
                throw new InvalidOperationException($"Quick focused input is not centered/chrome-free: renderedCharacterY={renderedCharacterY}, outer radius={frame.CornerRadius.TopLeft}; expected centered48px row and radius8.");
            if (editableInput.PlaceholderText != "Search library or find titles to request...") throw new InvalidOperationException($"Quick request-enabled native input lost its discovery caption: {editableInput.PlaceholderText}.");
            if (!Descendants<Border>(dialog).Any(border => border.Width == 40 && border.Height == 40 && border.CornerRadius.TopLeft == 20 && border.Child is TextBlock { Text: "FP" })) throw new InvalidOperationException("Quick search circular initials fallback missing.");
            wire.RequestSearchCalls = 0; Set(dialog, "_mediaScope", "audiobook");
            await (Task)typeof(GlobalSearchDialog).GetMethod("RunSearchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!; await Task.Delay(120);
            if (wire.RequestSearchCalls != 1) throw new InvalidOperationException("Quick discovery lookup must remain enabled independently of audiobook catalog scope.");
            dialog.Hide(); await showing; showing = null;
            wire.RequestsEnabled = false;
            dialog = new GlobalSearchDialog { XamlRoot = owner.XamlRoot }; showing = dialog.ShowAsync(); await Field<Task>(dialog, "_scopeLoadTask"); await Task.Delay(120);
            if (((TextBox)dialog.FindName("SearchBox")).PlaceholderText != "Search library...") throw new InvalidOperationException("Disabled discovery exposed its request caption.");
        }
        finally { wire.RequestsEnabled = true; dialog?.Hide(); if (showing != null) await showing; window.Close(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static async Task LayoutAsync(FrameworkElement element, double width, double height) { element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout(); await Task.Delay(120); }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        // A Page's surrounding template paints its Background in the window;
        // RenderTargetBitmap of the Page alone omits that template backdrop.
        // Paint the same brush within the captured panel, then restore it.
        var pagePanel = element switch { Page page => page.Content as Panel, UserControl control => control.Content as Panel, _ => null };
        var previousBackground = pagePanel?.Background;
        var bitmap = new RenderTargetBitmap();
        try
        {
            if (pagePanel != null && previousBackground == null) pagePanel.Background = element is Page page ? page.Background : (Brush)Application.Current.Resources["AppBackgroundBrush"];
            await bitmap.RenderAsync(element);
        }
        finally { if (pagePanel != null) pagePanel.Background = previousBackground; }
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Browse fixture was not rendered.");
        var pixels = await bitmap.GetPixelsAsync(); var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private sealed class OverrideServices(IServiceProvider fallback, Dictionary<Type, Func<object>> overrides) : IServiceProvider { public object? GetService(Type type) => overrides.TryGetValue(type, out var create) ? create() : fallback.GetService(type); }
    private sealed class Handler : HttpMessageHandler
    {
        internal bool FailMore = true;
        internal bool NewCollectionWindow;
        internal bool RequestsEnabled = true;
        internal int RequestSearchCalls;
        internal List<string> CollectionQueries { get; } = [];
        internal List<string> SearchQueries { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "browse-native.invalid") throw new InvalidOperationException("Native browse fixture attempted external network.");
            var path = request.RequestUri!.AbsolutePath; var body = "{\"items\":[],\"page\":{\"has_more\":false}}";
            if (path == "/api/v2/catalog" && request.RequestUri.Query.Contains("q=TypedFixture")) SearchQueries.Add(request.RequestUri.Query);
            if (path.EndsWith("/capabilities")) body = "{\"people_media_scope\":true,\"item_reorder\":false,\"imports\":false}";
            if (path == "/api/v2/catalog/filters") body = "{\"genres\":[\"Crime\",\"Drama\"],\"studios\":[],\"countries\":[],\"networks\":[],\"content_ratings\":[\"R\"],\"resolutions\":[\"2160p\"],\"original_languages\":[\"en\",\"fr\"],\"audio_languages\":[\"en\"]}";
            if (path == "/api/v2/profiles") body = "{\"items\":[]}";
            if (path == "/api/v2/settings/values/effective") body = "{\"items\":[{\"key\":\"ui.remember_library_page_state\",\"value\":false}]}";
            if (path == "/api/v2/requests/status") body = RequestsEnabled ? "{\"requests_enabled\":true,\"watchlist_titles_supported\":true}" : "{\"requests_enabled\":false}";
            if (path == "/api/v2/requests/search") RequestSearchCalls++;
            if (path == "/api/v2/catalog" && request.RequestUri.Query.Contains("collection_id=paging-fixture"))
            {
                CollectionQueries.Add(request.RequestUri.Query);
                var more = request.RequestUri.Query.Contains("seek=1");
                body = System.Text.Json.JsonSerializer.Serialize(new { items = new[] { new { content_id = NewCollectionWindow ? "new-window" : more ? "old-next" : "old-first", type = "movie", title = "Fixture window" } }, total = 2, window_cursor = NewCollectionWindow ? "new-window" : "first-window", page = new { has_more = !more } });
                // Ensure the page's await is genuinely asynchronous, allowing
                // the fixture to pause its UI continuation after HTTP success.
                return ReplyCollectionAsync(body);
            }
            if (path.StartsWith("/api/v2/requests/discover/browse/"))
            {
                var page = request.RequestUri.Query.Contains("page=3") ? 3 : request.RequestUri.Query.Contains("page=2") ? 2 : 1;
                if (page == 3 && FailMore) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"later page unavailable\"}") });
                body = System.Text.Json.JsonSerializer.Serialize(new { display_name = "Drama", total_pages = 3, results = page == 1 ? Array.Empty<object>() : page == 2 ? new object[] { new { tmdb_id = 7, media_type = "movie", title = "A retained discovery title", request = new { requestable = true } } } : new object[] { new { tmdb_id = 7, media_type = "movie", title = "Duplicate" }, new { tmdb_id = 7, media_type = "series", title = "Different type", request = new { requestable = true } } } });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
        private static async Task<HttpResponseMessage> ReplyCollectionAsync(string body)
        {
            await Task.Delay(15).ConfigureAwait(false);
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}

