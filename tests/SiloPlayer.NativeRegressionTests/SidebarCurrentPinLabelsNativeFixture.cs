using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class SidebarReviewNativeFixture
{
    private static bool IsCurrentPinLabelScenario => Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "sidebar-review-pin-labels";
    private static readonly TaskCompletionSource PinTitlesRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int PinLayoutReads, PinCollectionReads, InaccessiblePinReads;
    private static string CurrentPinRowTitle = "Current row title", CurrentPinCollectionTitle = "Current collection title";
    private static bool CurrentPinTargetsRemoved;

    private static object CurrentPinMenu() => new { items = new object[] {
        new { type = "builtin", destination = "home" },
        new { type = "section", library_id = 1, section_id = "recent", label = "Saved row" },
        new { type = "collection", library_id = 1, collection_id = "films", label = "Saved collection" },
        new { type = "collection", collection_id = "personal", label = "Personal collection" },
        new { type = "section", library_id = 1, section_id = "removed", label = "Removed row fallback" },
        new { type = "section", library_id = 999, section_id = "inaccessible", label = "Inaccessible row" },
    } };

    private static async Task<HttpResponseMessage?> CurrentPinResponseAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/api/v2/library/999/", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref InaccessiblePinReads);
            return new(HttpStatusCode.Forbidden);
        }
        if (path == "/api/v2/library/1/layout")
        {
            Interlocked.Increment(ref PinLayoutReads);
            await PinTitlesRelease.Task.WaitAsync(ct);
            return Json(new { sections = CurrentPinTargetsRemoved ? Array.Empty<object>() : new object[] { new { id = "recent", title = CurrentPinRowTitle, section_type = "custom" } } });
        }
        if (path == "/api/v2/library/1/collections")
        {
            Interlocked.Increment(ref PinCollectionReads);
            await PinTitlesRelease.Task.WaitAsync(ct);
            return Json(new { library_id = 1, collections = CurrentPinTargetsRemoved ? Array.Empty<object>() : new object[] { new { id = "films", title = CurrentPinCollectionTitle } } });
        }
        return null;
    }

    private static async Task CheckCurrentPinLabelsAsync(MainWindow window, IServiceProvider provider, NavigationView nav)
    {
        Pins = new Dictionary<string, object> { ["1"] = new object[] {
            new { type = "section", id = "recent", label = "Saved row" },
            new { type = "collection", id = "films", label = "Saved collection" },
        } };
        typeof(MainWindow).GetField("_criticalContentLoaded", Instance)!.SetValue(window, false);
        window.ShowMainNavigation();
        await window.RefreshSidebarPinsAsync();
        await Until(() => LibraryRows(nav).Length == 2 && PrimaryRows(nav).Length == 5 && LibraryRows(nav)[0].MenuItems.Count == 2);
        var library = LibraryRows(nav)[0];
        var section = (NavigationViewItem)library.MenuItems[0];
        var collection = (NavigationViewItem)library.MenuItems[1];
        static string Label(NavigationViewItem item) => ((Grid)item.Content).Children.OfType<TextBlock>().Single().Text;
        Check(Label(section) == "Saved row" && Label(collection) == "Saved collection", "pending title reads preserve saved sidebar labels");
        Check(PrimaryRows(nav).Any(item => Equals(item.Content, "Saved row")), "pending title reads preserve saved primary-menu labels");
        Check(PinLayoutReads == 0 && PinCollectionReads == 0, "pin metadata waits for first content");
        var navigation = provider.GetRequiredService<NavigationService>();
        // The gate was reset above on an already-settled window. Mounting any non-Home route is the
        // shell's first-content signal; a blank page keeps the main sidebar (Settings would swap in its
        // own section sidebar). The parameter makes this a real route change from the window's current
        // blank page, which NavigationService otherwise treats as a no-op.
        navigation.Navigate<Page>("pin-title-first-content");
        try { await Until(() => PinLayoutReads == 1 && PinCollectionReads == 1); }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException($"Pin title reads after first content: layout={PinLayoutReads}, collections={PinCollectionReads}, " +
                $"criticalContent={typeof(MainWindow).GetField("_criticalContentLoaded", Instance)!.GetValue(window)}, page={navigation.Frame?.Content?.GetType().Name}, " +
                $"libraries={provider.GetRequiredService<MainViewModel>().Libraries.Count}.");
        }
        library.IsExpanded = true;
        nav.SelectedItem = section;
        section.Focus(FocusState.Keyboard);
        await Drain();
        PinTitlesRelease.TrySetResult();
        await Until(() => Equals(PrimaryRows(nav)[1].Content, "Current row title") && Label(collection) == "Current collection title");
        Check(PinLayoutReads == 1 && PinCollectionReads == 1, "section and collection pins share one bounded title read per accessible library and type");
        Check(InaccessiblePinReads == 0, "inaccessible pin library has no metadata reads");
        Check(Label(section) == "Current row title" && Label(collection) == "Current collection title", "sidebar pins use current row and collection titles");
        Check(AutomationProperties.GetName(section) == "Current row title" && AutomationProperties.GetName(PrimaryRows(nav)[2]) == "Current collection title",
            "pin navigation accessibility follows the displayed titles");
        Check(Equals(PrimaryRows(nav)[1].Content, "Current row title") && Equals(PrimaryRows(nav)[2].Content, "Current collection title"), "primary menu uses the same current pin titles");
        Check(Equals(PrimaryRows(nav)[3].Content, "Personal collection") && Equals(PrimaryRows(nav)[4].Content, "Removed row fallback"), "personal and removed pins retain their saved fallback");
        Check(ReferenceEquals(library.MenuItems[0], section) && ReferenceEquals(library.MenuItems[1], collection) && ReferenceEquals(nav.SelectedItem, section) && library.IsExpanded,
            "pin title refresh preserves native nodes selection and expansion");
        var unpin = ((Grid)section.Content).Children.OfType<Button>().Single();
        Check(AutomationProperties.GetName(unpin).Contains("Current row title", StringComparison.Ordinal), "unpin accessible name follows the displayed current title");
        navigation.Navigate<SettingsPage>();
        await Until(() => navigation.Frame!.Content is SettingsPage page && page.IsLoaded && !page.ViewModel.IsLoading);
        var settings = (SettingsPage)navigation.Frame!.Content;
        settings.SelectSettingsSection("Interface");
        await Drain();
        var labels = All<TextBlock>((FrameworkElement)settings.FindName("InterfacePrimaryMenuHost")).Select(item => item.Text).ToArray();
        Check(labels.Any(text => text.Contains("Current row title", StringComparison.Ordinal)) && labels.Any(text => text.Contains("Current collection title", StringComparison.Ordinal)),
            "Settings menu editor names pins by the same current titles");
        Check(provider.GetRequiredService<UICustomizationService>().PrimaryMenu!.Items.Any(item => item.Label == "Saved row"), "display title resolution never writes over the saved fallback");
        var host = (FrameworkElement)settings.FindName("InterfacePrimaryMenuHost");
        var retainedText = All<TextBlock>(host).First(item => item.Text.Contains("Current row title", StringComparison.Ordinal));
        var retainedAction = All<Button>(host).First(item => AutomationProperties.GetName(item).Contains("Current row title", StringComparison.Ordinal));
        retainedAction.Focus(FocusState.Keyboard);
        await Drain();
        CurrentPinRowTitle = "Renamed row";
        CurrentPinCollectionTitle = "Renamed collection";
        var catalog = provider.GetRequiredService<CatalogApi>();
        await Task.WhenAll(catalog.GetLibraryLayoutAsync(1), catalog.GetLibraryCollectionsAsync(1));
        await Until(() => retainedText.Text.Contains("Renamed row", StringComparison.Ordinal));
        Check(All<TextBlock>(host).Contains(retainedText) && All<Button>(host).Contains(retainedAction) && retainedAction.FocusState == FocusState.Keyboard,
            "live Settings title updates retain menu editor nodes and keyboard focus");
        Check(AutomationProperties.GetName(retainedAction).Contains("Renamed row", StringComparison.Ordinal), "menu editor action accessibility follows a later title rename");
        Check(All<TextBlock>(host).Any(item => item.Text.Contains("Renamed collection", StringComparison.Ordinal)), "Collections tab reads refresh retained menu editor labels");
        CurrentPinTargetsRemoved = true;
        await Task.WhenAll(catalog.GetLibraryLayoutAsync(1), catalog.GetLibraryCollectionsAsync(1));
        await Until(() => retainedText.Text.Contains("Saved row", StringComparison.Ordinal));
        Check(All<TextBlock>(host).Any(item => item.Text.Contains("Saved collection", StringComparison.Ordinal)), "removed targets restore saved menu fallback labels");
        Check(provider.GetRequiredService<UICustomizationService>().PrimaryMenu!.Items.Any(item => item.Label == "Saved row"), "subsequent reads never persist resolved display titles");
    }
}
