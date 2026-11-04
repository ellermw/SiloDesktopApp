using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static partial class RetainedLibraryNativeFixture
{
    private static async Task CheckActivationAndDepartureAsync(StackPanel parent, NavigationService navigation, Wire wire, SettingsApi settings)
    {
        foreach (var scenario in new[] { "same-tab", "other-tab", "library-switch", "explicit", "reload", "year-debounce", "rule-debounce", "language", "genre-history", "year-before-refresh", "rule-before-refresh" })
        {
            wire.LibraryId++; wire.Remember = true; wire.Search = "?tab=recommended";
            settings.LibraryPageState.Invalidate();
            var frame = new Frame { Width = 1000, Height = 700, CacheSize = 2 };
            parent.Children.Add(frame); navigation.Frame = frame;
            LibraryPage? page = null;
            try
            {
                var library = new Library { Id = wire.LibraryId, Name = "Activation fixture", Type = "movie" };
                if (scenario is "reload" or "year-debounce" or "rule-debounce" or "year-before-refresh" or "rule-before-refresh") wire.Search = "?tab=library";
                if (scenario == "library-switch") wire.Search = "?tab=library&genre=Drama";
                if (scenario is "same-tab" or "other-tab" or "explicit") wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var reads = wire.StateReads;
                frame.Navigate(typeof(LibraryPage), scenario is "explicit" or "genre-history" ? new LibraryPage.NavigationArgs(library, "Library", "Crime") : (object)library);
                page = (LibraryPage)frame.Content;
                await Until(() => page.IsLoaded && wire.StateReads > reads);

                if (scenario == "library-switch")
                {
                    await Ready();
                    Require(page.ViewModel.SelectedGenres.Contains("Drama"), "Library A's filters were not loaded.");
                    await Leave();
                    wire.LibraryId++; wire.Search = "?tab=library&type=authors&genre=Crime";
                    library = new Library { Id = wire.LibraryId, Name = "Library B", Type = "audiobook" };
                    settings.LibraryPageState.Invalidate();
                    wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously); reads = wire.StateReads;
                    frame.Navigate(typeof(LibraryPage), library);
                    Require(ReferenceEquals(page, frame.Content), "Library switch did not reuse the actual cached page.");
                    await Until(() => page.IsLoaded && wire.StateReads > reads);
                }

                if (wire.ReadGate != null)
                {
                    var button = (Button)page.FindName(scenario == "same-tab" ? "RecommendedTab" : "LibraryTab");
                    Require(!button.IsEnabled && !((ComboBox)page.FindName("SortComboBox")).IsEnabled,
                        "Unapplied library controls accepted input during a held restore.");
                    var edit = Field<long>(page, "_stateEdit"); var writes = wire.StateWrites;
                    try { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); }
                    catch (Exception) when (!button.IsEnabled) { /* Disabled native controls reject invocation. */ }
                    await Task.Delay(650);
                    Require(wire.StateWrites == writes && Field<long>(page, "_stateEdit") == edit,
                        "A provisional tab click edited or saved the previous library's view.");
                    wire.ReadGate.SetResult(); wire.ReadGate = null;
                    await Ready();
                    Require(page.IsEnabled, "Controls were not re-enabled after applying library state.");
                    if (scenario == "library-switch")
                        Require(Tab() == "Library" && Field<string>(page, "_currentAudiobookAxis") == "author" &&
                            page.ViewModel.SelectedGenres.Contains("Crime") && !page.ViewModel.SelectedGenres.Contains("Drama"),
                            "Library B did not configure its audiobook axis and own filters.");
                    else if (scenario == "explicit")
                    {
                        await Until(() => page.ViewModel.GetWindowItem(0) != null);
                        Require(Tab() == "Library" && page.ViewModel.SelectedGenres.Contains("Crime"), "Held restore dropped explicit navigation.");
                    }
                    else
                    {
                        await Until(() => Field<bool>(page, "_recommendedLoaded"));
                        Require(Tab() == "Recommended", "Blocked input replaced the restored tab.");
                        var currentEdit = Field<long>(page, "_stateEdit");
                        Click("RecommendedTab");
                        Require(Field<long>(page, "_stateEdit") == currentEdit, "Same-tab click marked the view edited.");
                    }
                }
                else if (scenario == "genre-history")
                {
                    await Ready();
                    await Until(() => page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                    await Task.Delay(750);
                    var writes = wire.StateWrites;
                    Click("ClearAllFiltersButton");
                    await Until(() => page.ViewModel.SelectedGenres.Count == 0 && !page.ViewModel.IsLoading && wire.StateWrites > writes);
                    await Task.Delay(150);
                    var scroll = (ScrollViewer)page.FindName("LibraryScroller");
                    scroll.ChangeView(null, 400, null, true);
                    await Until(() => scroll.VerticalOffset > 350);
                    var offset = scroll.VerticalOffset;
                    var catalogReads = wire.CatalogReads;
                    await Leave(); frame.GoBack(); await Until(() => page.IsLoaded);
                    await Task.Delay(200);
                    Require(ReferenceEquals(page, frame.Content) && page.ViewModel.SelectedGenres.Count == 0 &&
                        wire.CatalogReads == catalogReads && Math.Abs(scroll.VerticalOffset - offset) < 2,
                        "Back replayed the original genre parameter, reset the retained catalog/offset, or discarded the edited filter.");
                    Click("CollectionsTab");
                    await Until(() => wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                    await Leave(); frame.GoBack(); await Until(() => page.IsLoaded); await Task.Delay(200);
                    Require(Tab() == "Collections", "Back replayed InitialTab after a later tab edit.");
                }
                else if (scenario is "year-before-refresh" or "rule-before-refresh")
                {
                    await Ready(); await Until(() => page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                    NumberBox? yearRule = null;
                    var year = (TextBox)page.FindName("YearMinBox");
                    QueryFilterEditor? editor = null;
                    if (scenario == "year-before-refresh") year.Text = "1999";
                    else
                    {
                        Click("OpenFiltersButton");
                        await Until(() => ((SlideSheet)page.FindName("FiltersSheet")).IsOpen);
                        editor = Field<QueryFilterEditor>(page, "_sharedFilterEditor");
                        yearRule = Descendants<NumberBox>(editor).First();
                        yearRule.Value = 1999;
                    }
                    var refreshReads = wire.StateReads;
                    wire.Search = "?tab=recommended";
                    settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                    await Until(() => wire.StateReads > refreshReads);
                    await Task.Delay(100); // Inspect before the 400/500 ms draft debounce commits.
                    Require(Tab() == "Library" && (yearRule == null ? year.Text == "1999" :
                        yearRule.Value == 1999 && ReferenceEquals(editor, Field<QueryFilterEditor>(page, "_sharedFilterEditor"))),
                        "An invalidation delivered after a local draft overwrote that draft before its debounce.");
                    await Until(() => Uri.UnescapeDataString(wire.Search).Contains("1999", StringComparison.Ordinal));
                    if (editor != null) ((SlideSheet)page.FindName("FiltersSheet")).IsOpen = false;
                }
                else if (scenario is "reload" or "year-debounce" or "rule-debounce")
                {
                    await Ready(); await Until(() => page.ViewModel.GetWindowItem(0) != null && Field<bool>(page, "_libraryCatalogLoaded"));
                    var writes = wire.StateWrites;
                    var catalogReads = wire.CatalogReads;
                    if (scenario == "reload")
                    {
                        wire.CatalogGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        ((ComboBox)page.FindName("OrderComboBox")).SelectedIndex = 0;
                        await Until(() => page.ViewModel.IsLoading && wire.CatalogReads > catalogReads);
                    }
                    else if (scenario == "year-debounce")
                        ((TextBox)page.FindName("YearMinBox")).Text = "1999";
                    else
                    {
                        Click("OpenFiltersButton");
                        await Until(() => ((SlideSheet)page.FindName("FiltersSheet")).IsOpen);
                        var editor = Field<QueryFilterEditor>(page, "_sharedFilterEditor");
                        Descendants<NumberBox>(editor).First().Value = 2001;
                        await Until(() => Field<object?>(page, "_advancedFilterDebounceTimer") != null);
                    }
                    await Leave();
                    var expected = scenario == "reload" ? "order=desc" : scenario == "year-debounce" ? "1999" : "2001";
                    await Until(() => wire.StateWrites > writes && Uri.UnescapeDataString(wire.Search).Contains(expected, StringComparison.Ordinal));
                    Require(Field<object?>(page, "_yearDebounceTimer") == null && Field<object?>(page, "_advancedFilterDebounceTimer") == null,
                        "Filter debounce survived departure.");
                    catalogReads = wire.CatalogReads;
                    frame.GoBack();
                    await Until(() => page.IsLoaded && wire.CatalogReads > catalogReads);
                    wire.CatalogGate?.SetResult(); wire.CatalogGate = null;
                    await Until(() => page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading && Field<bool>(page, "_libraryCatalogLoaded"));
                }
                else
                {
                    await Ready();
                    settings.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                    wire.Remember = false; settings.NotifyLocalizedSettingChanged("ui.remember_library_page_state");
                    await Until(() => Field<LibraryPageStateSnapshot?>(page, "_stateSnapshot") is { Remember: false });
                    Require(Field<long>(page, "_stateLanguage") == settings.LocalizedReads.Generation,
                        "Mounted language refresh retired the entire state visit.");
                    wire.Remember = true; wire.Search = "?tab=collections";
                    settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                    await Until(() => Tab() == "Collections");
                }
                Program.Log("PASS: actual Library activation/departure regression: " + scenario + ".");
            }
            finally
            {
                wire.ReadGate?.TrySetResult(); wire.ReadGate = null;
                wire.CatalogGate?.TrySetResult(); wire.CatalogGate = null;
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                if (page != null) await Until(() => !page.IsLoaded);
                await Task.Delay(150);
            }

            string Tab() => Field<string>(page!, "_currentTab");
            void Click(string name) => ((IInvokeProvider)new ButtonAutomationPeer((Button)page!.FindName(name)).GetPattern(PatternInterface.Invoke)).Invoke();
            async Task Ready() => await Until(() => Field<bool>(page!, "_libraryStateApplied"));
            async Task Leave() { frame.Navigate(typeof(Page)); await Until(() => !page!.IsLoaded); }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
