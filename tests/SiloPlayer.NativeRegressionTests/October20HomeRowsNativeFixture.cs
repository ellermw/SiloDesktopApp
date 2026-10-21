using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class October20HomeRowsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var http = new HttpClient(new Wire());
        var client = new SiloApiClient(http); client.SetBaseUrl("https://oct20-rows.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture-rows");
        using var events = new EventChannelClient(client, auth);
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        var home = new HomeViewModel(new HomeApi(client), auth);
        var navigation = new NavigationService { Frame = new Frame() };
        CatalogNavigation? captured = null;
        navigation.NavigationRequestHandler = (type, parameter) =>
        {
            if (type != typeof(CatalogPage)) throw new InvalidOperationException("A section navigated outside CatalogPage.");
            captured = (CatalogNavigation)parameter!; return true;
        };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(EventChannelClient)] = events,
            [typeof(HomeViewModel)] = home, [typeof(LibraryViewModel)] = new LibraryViewModel(catalog),
            [typeof(CatalogApi)] = catalog, [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
        }));
        try
        {
            var homePage = new HomePage(); var libraryPage = new LibraryPage();
            foreach (var type in new[] { "continue_watching", "next_up", "recommended_for_you", "seasonal_themed", "trending_on_server", "recently_added" })
            foreach (var library in new[] { false, true })
            {
                var section = new HomeSectionWithItems { Id = "short-" + type, SectionType = type, Title = "Short " + type,
                    ItemLimit = 20, TotalCount = 1, LoadCompleted = true, Items = [new() { ContentId = "fixture-one", Title = "One", Type = "movie" }] };
                var row = library
                    ? (SectionRow)typeof(LibraryPage).GetMethod("CreateLibrarySectionRow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(libraryPage, [section, 23])!
                    : (SectionRow)typeof(HomePage).GetMethod("CreateSectionRow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(homePage, [section])!;
                row.Width = 900; parent.Children.Add(row); await Task.Delay(30); row.UpdateLayout();
                try
                {
                    foreach (var name in new[] { "TitleLinkBtn", "ExploreAllBtn" })
                    {
                        var button = (Button)row.FindName(name);
                        if (button.Visibility != Visibility.Visible) throw new InvalidOperationException($"{type}/{library}/{name} unavailable for a short row.");
                        captured = null;
                        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
                        await Task.Delay(20);
                        if (captured is not { Source: "section" } || captured.Scope != (library ? "library" : "home") || captured.SectionId != section.Id || captured.LibraryId != (library ? 23 : null))
                            throw new InvalidOperationException($"{type}/{library}/{name} lost its section route.");
                    }
                    if (((FrameworkElement)row.FindName("TitleViewSuffix")).Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Explore all duplicated a View suffix.");
                    if (library && ((FrameworkElement)row.FindName("PinSectionBtn")).Visibility != Visibility.Visible)
                        throw new InvalidOperationException($"Library {type} row cannot pin.");
                }
                finally { parent.Children.Remove(row); }
            }
            await SeasonalFieldsAsync(parent);
            Program.Log("PASS: every short Home/library row title and Explore all route, pin availability, Seasonal Picks wire edits.");
        }
        finally
        {
            home.SetActive(false);
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(home);
            field.SetValue(null, previous);
        }
    }

    private static async Task SeasonalFieldsAsync(StackPanel parent)
    {
        var config = new Dictionary<string, object> { ["enabled_themes"] = new[] { "halloween" }, ["filter_library_ids"] = new[] { 23 } };
        var host = new StackPanel { Width = 700 };
        var catalog = new SeasonalThemeCatalog { Themes = [new() { Key = "halloween", Name = "Halloween", When = "October", DatesEditable = true,
            Start = "10-01", End = "10-31", Vibes = [new() { Key = "family", Name = "Family", Title = "Family Halloween" }, new() { Key = "horror", Name = "Horror", Title = "Halloween Horror" }] }] };
        typeof(RecipeGalleryDialog).GetMethod("BuildSeasonalCatalogFields", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [config, host, catalog]);
        parent.Children.Add(host); await Task.Delay(40);
        try
        {
            var vibe = Descendants(host).OfType<ComboBox>().First(combo => combo.Header?.ToString() == "Vibe");
            vibe.SelectedIndex = 1;
            if (SeasonalRowConfiguration.Read(config, "vibes", new Dictionary<string, string>()).GetValueOrDefault("halloween") != "horror")
                throw new InvalidOperationException("Choosing a vibe did not write its stable key.");
            var source = Descendants(host).OfType<ComboBox>().First(combo => combo.Header?.ToString() == "Picks from");
            source.SelectedIndex = 1;
            if (config["source"].ToString() != "lists") throw new InvalidOperationException("List-only source was not selected.");
            var add = Descendants(host).OfType<Button>().First(button => button.Content?.ToString() == "Add my own holiday");
            ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Task.Delay(30);
            var holiday = AssertSingle(SeasonalRowConfiguration.Read(config, "custom_holidays", new List<SeasonalCustomHoliday>()));
            if (holiday.Name != "" || !config.ContainsKey("filter_library_ids")) throw new InvalidOperationException("Adding a custom holiday lost unrelated row config.");
            if (SeasonalRowConfiguration.Validate(config) == null) throw new InvalidOperationException("An incomplete custom holiday was accepted.");
        }
        finally { parent.Children.Remove(host); }
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values) => values.Count == 1 ? values[0] : throw new InvalidOperationException("Expected one custom holiday.");
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> overrides) : IServiceProvider
    { public object? GetService(Type type) => overrides.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}
