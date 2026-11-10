using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static partial class RetainedLibraryNativeFixture
{
    private static async Task CheckReportedStateOrderingsAsync(StackPanel parent, NavigationService navigation, Wire wire, SettingsApi settings)
    {
        string[] scenarios = [ "opt-out-genre-history", "opt-out-shell-refresh", "opt-out-cross-library", "failed-shell-refresh", "failed-cross-library", "same-library-genre-back", "shell-refresh", "any-genre", "any-genre-shell-refresh", "genre-empty-groups", "audiobook-author", "audiobook-narrator", "audiobook-series", "access-opt-out-mounted", "access-opt-out-detached", "access-held-read", "reconnect-mounted", "reconnect-detached", "reconnect-in-flight", "cross-library-held", "cross-library-failed" ];
        var requestedScenario = Environment.GetEnvironmentVariable("SILO_NATIVE_LIBRARY_STATE_ORDERING_CASE");
        if (!string.IsNullOrEmpty(requestedScenario))
        {
            if (!scenarios.Contains(requestedScenario, StringComparer.Ordinal)) throw new InvalidOperationException("Unknown library-state ordering scenario.");
            scenarios = [requestedScenario];
        }
        foreach (var scenario in scenarios)
        {
            wire.LibraryId++; wire.Remember = !scenario.StartsWith("opt-out-", StringComparison.Ordinal) && !scenario.StartsWith("access-opt-out-", StringComparison.Ordinal);
            wire.Search = "?tab=library"; wire.FailPut = false;
            wire.PutErrorMessage = "State ordering save failure: " + scenario;
            if (scenario is "any-genre" or "any-genre-shell-refresh")
                wire.Search += "&match=any&groups[0][match]=all&groups[0][rules][0][field]=year&groups[0][rules][0][op]=gte&groups[0][rules][0][value]=2000" +
                    "&groups[1][match]=all&groups[1][rules][0][field]=rating_imdb&groups[1][rules][0][op]=gte&groups[1][rules][0][value]=7";
            if (scenario == "genre-empty-groups")
                wire.Search += "&groups[0][match]=all&groups[0][rules][0][field]=genre&groups[0][rules][0][value]=Drama" +
                    "&groups[1][match]=all&groups[1][rules][0][field]=year&groups[1][rules][0][op]=gte&groups[1][rules][0][value]=2000";
            var audiobookAxis = scenario.StartsWith("audiobook-", StringComparison.Ordinal) ? scenario[10..] : null;
            if (audiobookAxis != null) wire.Search += "&type=" + (audiobookAxis == "series" ? "series" : audiobookAxis + "s");
            settings.LibraryPageState.Invalidate();
            var frame = new Frame { Width = 1000, Height = 700, CacheSize = 2 };
            parent.Children.Add(frame); navigation.Frame = frame;
            var library = new Library { Id = wire.LibraryId, Name = "State ordering fixture", Type = audiobookAxis == null ? "movie" : "audiobook" };
            wire.LibraryTypes[library.Id] = library.Type;
            var originalSearch = wire.Search; var initialWrites = wire.StateWrites;
            LibraryPage? page = null;
            try
            {
                var explicitGenre = scenario is "opt-out-genre-history" or "opt-out-shell-refresh" or "opt-out-cross-library" or
                    "failed-shell-refresh" or "failed-cross-library" or "same-library-genre-back" or "shell-refresh" or "any-genre" or "any-genre-shell-refresh" or
                    "genre-empty-groups" or "access-opt-out-mounted" or "access-opt-out-detached";
                frame.Navigate(typeof(LibraryPage), explicitGenre
                    ? new LibraryPage.NavigationArgs(library, "Library", "Crime") : (object)library);
                page = (LibraryPage)frame.Content;
                await Ready();
                await Until(() => audiobookAxis == null ? page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading :
                    !Field<bool>(page, "_isLoadingAudiobookGroups"));
                await Task.Delay(750);

                if (scenario == "opt-out-genre-history")
                {
                    var writes = wire.StateWrites;
                    var scroll = (ScrollViewer)page.FindName("LibraryScroller");
                    scroll.ChangeView(null, 400, null, true);
                    await Until(() => scroll.VerticalOffset > 350);
                    var offset = scroll.VerticalOffset; var catalogReads = wire.CatalogReads;
                    await Leave(); frame.GoBack(); await Ready(); await Task.Delay(200);
                    Require(Tab() == "Library" && page.ViewModel.SelectedGenres.Contains("Crime") &&
                        wire.CatalogReads == catalogReads && Math.Abs(scroll.VerticalOffset - offset) < 2,
                        "Opt-out detail/Back discarded the explicit genre, retained grid or scroll position.");
                    frame.GoForward(); await Until(() => !page.IsLoaded);
                    frame.GoBack(); await Ready();
                    Require(page.ViewModel.SelectedGenres.Contains("Crime"), "Repeated opt-out history navigation lost the genre.");
                    Click("CollectionsTab"); await Until(() => Tab() == "Collections" && !page.ViewModel.IsCollectionsLoading);
                    await Leave(); frame.GoBack(); await Ready();
                    Require(Tab() == "Collections" && wire.StateWrites == writes, "Opt-out Back lost the edited tab or wrote preferences.");
                }
                else if (scenario is "opt-out-shell-refresh" or "opt-out-cross-library" or "failed-shell-refresh" or "failed-cross-library")
                {
                    var writes = wire.StateWrites;
                    var crossLibrary = scenario.EndsWith("cross-library", StringComparison.Ordinal);
                    if (crossLibrary)
                    {
                        frame.Navigate(typeof(LibraryPage), new Library { Id = library.Id + 10000, Name = "Library B", Type = "movie" });
                        Require(ReferenceEquals(page, frame.Content), "Opt-out cross-library route did not reuse the cached page.");
                        await Ready();
                        Require(Tab() == "Recommended", "Library B inherited the explicit genre route.");
                    }
                    wire.FailHeldRead = scenario.StartsWith("failed-", StringComparison.Ordinal);
                    settings.LibraryPageState.Invalidate();
                    if (crossLibrary) frame.GoBack();
                    else
                    {
                        var previous = page;
                        Require(navigation.RefreshCurrentEntry(), "Explicit genre refresh was rejected.");
                        page = (LibraryPage)frame.Content;
                        Require(!ReferenceEquals(previous, page), "Refresh did not replace the page instance.");
                    }
                    await Ready();
                    await Until(() => page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                    await Task.Delay(750);
                    Require(Tab() == "Library" && page.ViewModel.SelectedGenres.SequenceEqual(new[] { "Crime" }) &&
                        Uri.UnescapeDataString(wire.LastCatalogRequest).Contains("Crime", StringComparison.Ordinal) && wire.StateWrites == writes,
                        "Opt-out/unavailable restore lost the entry's genre route or scheduled a route-replay save.");
                }
                else if (scenario == "same-library-genre-back")
                {
                    await Leave();
                    frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(library, "Library", "Drama"));
                    await Ready();
                    await Until(() => page.ViewModel.SelectedGenres.Contains("Drama") && !page.ViewModel.IsLoading);
                    await Task.Delay(750);
                    await Leave(); frame.GoBack(); await Ready();
                    Require(page.ViewModel.SelectedGenres.Contains("Drama"), "Return to the same entry lost its genre.");
                    frame.GoBack(); await Until(() => !page.IsLoaded);
                    var catalogReads = wire.CatalogReads; var writes = wire.StateWrites;
                    frame.GoBack(); await Ready();
                    await Until(() => page.ViewModel.SelectedGenres.Contains("Crime") && !page.ViewModel.IsLoading);
                    await Task.Delay(750);
                    Require(wire.CatalogReads > catalogReads && !page.ViewModel.SelectedGenres.Contains("Drama") &&
                        Uri.UnescapeDataString(wire.LastCatalogRequest).Contains("Crime", StringComparison.Ordinal) && wire.StateWrites == writes,
                        "Back to an older explicit entry retained the newer genre, skipped its catalog reload, or saved replayed args.");
                }
                else if (audiobookAxis != null)
                {
                    RequireGroupToolbar();
                    await Leave(); frame.GoBack(); await Ready();
                    RequireGroupToolbar();
                    Require(navigation.RefreshCurrentEntry(), "Audiobook axis refresh was rejected.");
                    page = (LibraryPage)frame.Content; await Ready();
                    RequireGroupToolbar();
                    Click("AudiobookBooksButton");
                    Require(new[] { "SortComboBox", "OrderComboBox", "OpenFiltersButton" }.All(name =>
                        ((FrameworkElement)page.FindName(name)).Visibility == Visibility.Visible), "Books axis did not restore its catalog toolbar.");
                }
                else if (scenario is "access-opt-out-mounted" or "access-opt-out-detached")
                {
                    var scroll = (ScrollViewer)page.FindName("LibraryScroller");
                    scroll.ChangeView(null, 400, null, true); await Until(() => scroll.VerticalOffset > 350);
                    var offset = scroll.VerticalOffset; var reads = wire.StateReads;
                    var writes = wire.StateWrites;
                    if (scenario == "access-opt-out-detached") await Leave();
                    ((SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloApiClient))!).InvalidateAccessContext();
                    settings.LibraryPageState.Invalidate();
                    if (scenario == "access-opt-out-mounted")
                    {
                        await Until(() => wire.StateReads > reads && !Field<bool>(page, "_stateRefreshQueued"));
                        await Leave();
                    }
                    frame.GoBack(); await Ready();
                    await Until(() => page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                    await Task.Delay(200);
                    // Catalog reads must still reauthorize after access changes; the
                    // retained visit must keep its route/filter and position meanwhile.
                    Require(Tab() == "Library" && page.ViewModel.SelectedGenres.Contains("Crime") &&
                        Math.Abs(scroll.VerticalOffset - offset) < 2 && wire.StateWrites == writes,
                        "An access-only generation change discarded the opt-out view or position.");
                    wire.Remember = true; wire.Search = "?tab=collections";
                    settings.LibraryPageState.Invalidate(); await Until(() => Tab() == "Collections");
                    wire.Remember = false; settings.LibraryPageState.Invalidate();
                    await Until(() => Field<LibraryPageStateSnapshot?>(page, "_stateSnapshot") is { Remember: false });
                    Require(Tab() == "Collections", "Mounted settings invalidation stopped working after an access change.");
                }
                else if (scenario == "access-held-read")
                {
                    wire.Search = "?tab=collections"; var reads = wire.StateReads;
                    wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var retired = wire.ReadGate;
                    settings.LibraryPageState.Invalidate(); await Until(() => wire.StateReads > reads);
                    ((SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloApiClient))!).InvalidateAccessContext();
                    settings.LibraryPageState.Invalidate();
                    wire.ReadGate = null; retired.SetResult();
                    await Until(() => Tab() == "Collections" && wire.StateReads >= reads + 2);
                    wire.Remember = false; settings.LibraryPageState.Invalidate();
                    await Until(() => Field<LibraryPageStateSnapshot?>(page, "_stateSnapshot") is { Remember: false });
                    Require(Tab() == "Collections", "A retired settings read disabled later mounted state refreshes.");
                }
                else if (scenario == "shell-refresh")
                {
                    Click("ClearAllFiltersButton");
                    await Until(() => page.ViewModel.SelectedGenres.Count == 0 && !page.ViewModel.IsLoading);
                    Click("CollectionsTab");
                    await Until(() => wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                    await Task.Delay(200);
                    var previous = page; var writes = wire.StateWrites; var search = wire.Search;
                    Require(navigation.RefreshCurrentEntry(), "The real shell refresh was rejected.");
                    page = (LibraryPage)frame.Content; await Ready(); await Task.Delay(750);
                    Require(!ReferenceEquals(previous, page) && Tab() == "Collections" && page.ViewModel.SelectedGenres.Count == 0 &&
                        wire.StateWrites == writes && wire.Search == search,
                        "RefreshCurrentEntry replayed InitialTab/InitialGenre or overwrote the saved view.");
                }
                else if (scenario is "any-genre" or "any-genre-shell-refresh")
                {
                    if (scenario == "any-genre-shell-refresh")
                    {
                        var previous = page; var catalogReads = wire.CatalogReads;
                        ((SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloApiClient))!).InvalidateAccessContext();
                        settings.LibraryPageState.Invalidate();
                        Require(navigation.RefreshCurrentEntry(), "Temporary genre refresh was rejected.");
                        page = (LibraryPage)frame.Content;
                        Require(!ReferenceEquals(previous, page), "Temporary genre refresh did not replace the page instance.");
                        await Ready();
                        await Until(() => wire.CatalogReads > catalogReads && page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                        await Task.Delay(750);
                        Require(Tab() == "Library" && page.ViewModel.SelectedGenres.SequenceEqual(new[] { "Crime" }) &&
                            wire.StateWrites == initialWrites && wire.Search == originalSearch,
                            "Shell refresh discarded the temporary genre view or overwrote the saved OR query.");
                    }
                    var query = wire.LastCatalogRequest.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                        .Where(pair => pair.Length == 2).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                    Require(query.GetValueOrDefault("match") == "any", "Explicit genre rewrote the saved OR operator.");
                    using var groups = JsonDocument.Parse(query["groups"]);
                    var rules = groups.RootElement.EnumerateArray().SelectMany(group => group.GetProperty("rules").EnumerateArray()).ToArray();
                    Require(groups.RootElement.GetArrayLength() == 1 && rules.Length == 1 &&
                        rules[0].GetProperty("field").GetString() == "genre" && rules[0].GetProperty("value").GetString() == "Crime",
                        "The temporary genre browse was not a clean genre-only query.");
                    await Leave(); frame.GoBack(); await Ready(); await Task.Delay(750);
                    Require(wire.StateWrites == initialWrites && wire.Search == originalSearch && page.ViewModel.AdvancedRulesMatch == "any",
                        "Genre activation/departure persisted a replacement for the user's saved OR query.");
                    ((ComboBox)page.FindName("OrderComboBox")).SelectedIndex = 0;
                    await Until(() => page.ViewModel.SelectedOrder == "desc");
                    await Task.Delay(750); // Existing save debounce must have had an opportunity to run.
                    Require(wire.StateWrites == initialWrites && wire.Search == originalSearch &&
                        page.ViewModel.AdvancedRulesMatch == "any" && page.ViewModel.SelectedGenres.Contains("Crime"),
                        "Editing the temporary genre view replaced the persisted OR query or changed the current route.");
                    await Leave();
                    frame.Navigate(typeof(LibraryPage), library); page = (LibraryPage)frame.Content; await Ready();
                    Require(!page.ViewModel.SelectedGenres.Contains("Crime") && page.ViewModel.AdvancedGroups.Count == 2 &&
                        page.ViewModel.AdvancedRulesMatch == "any" && wire.Search == originalSearch,
                        "A new unqualified entry retained the temporary genre route instead of the preserved saved OR query.");
                }
                else if (scenario == "genre-empty-groups")
                {
                    Require(page.ViewModel.AdvancedGroups.Count == 2 && page.ViewModel.AdvancedGroups.All(group => group.Rules.Count > 0) &&
                        page.ViewModel.AdvancedGroups.SelectMany(group => group.Rules).Count(rule => rule.Field == "genre") == 1,
                        "Replacing a genre left an empty advanced group or duplicate genre rules.");
                    Require(!Uri.UnescapeDataString(wire.Search).Contains("Drama", StringComparison.Ordinal), "The replaced genre remained in saved state.");
                }
                else if (scenario is "cross-library-held" or "cross-library-failed")
                {
                    wire.FailPut = scenario == "cross-library-failed";
                    wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var writes = wire.StateWrites;
                    Click("CollectionsTab"); await Until(() => wire.StateWrites > writes);
                    frame.Navigate(typeof(LibraryPage), new Library { Id = library.Id + 10000, Name = "Library B", Type = "movie" });
                    Require(ReferenceEquals(page, frame.Content), "Cross-library test did not reuse the cached page.");
                    await Ready();
                    Require(Tab() == "Recommended", "Library B inherited library A's pending state.");
                    if (scenario == "cross-library-failed")
                    {
                        wire.PutGate.SetResult(); wire.PutGate = null;
                        await Until(SaveError);
                        wire.FailPut = false;
                        wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    frame.GoBack(); await Ready();
                    Require(Tab() == "Collections" && !wire.PutGate.Task.IsCompleted,
                        "A/B/A restored the older confirmed state over A's unsaved or in-flight edit.");
                    wire.PutGate.SetResult(); wire.PutGate = null;
                    await Until(() => wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                    await Leave(); frame.GoBack(); await Ready();
                    Require(Tab() == "Collections", "A later retained return kept a stale view after the write completed.");
                }
                else
                {
                    var client = (SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloApiClient))!;
                    wire.FailPut = true;
                    var writes = wire.StateWrites;
                    if (scenario == "reconnect-in-flight") wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Click("CollectionsTab"); await Until(() => wire.StateWrites > writes);
                    if (scenario != "reconnect-in-flight") await Until(SaveError);
                    if (scenario == "reconnect-detached") await Leave();
                    client.InvalidateAccessContext();
                    settings.LibraryPageState.Invalidate();
                    if (scenario == "reconnect-in-flight")
                    {
                        wire.FailPut = false; wire.PutGate!.SetResult(); wire.PutGate = null;
                        await Until(() => wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                        Require(wire.StateWrites > writes + 1, "A retired read generation discarded the in-flight failed save instead of rebinding it.");
                    }
                    else
                    {
                        if (scenario == "reconnect-mounted")
                        {
                            var previous = page;
                            Require(navigation.RefreshCurrentEntry(), "Catch-up refresh was rejected.");
                            page = (LibraryPage)frame.Content;
                            Require(!ReferenceEquals(previous, page), "Catch-up did not construct a new page instance.");
                        }
                        else frame.GoBack();
                        await Ready();
                        Require(Tab() == "Collections", "Catch-up or detached return discarded the unsaved view for the same identity.");
                        await Task.Delay(200); // Let the activation's failed retry release its write gate.
                        if (scenario == "reconnect-mounted")
                        {
                            // Departure can retry through the old page. Start a
                            // distinct failed retry only after the replacement
                            // is mounted, so its own visible-intent check owns
                            // this unique feedback rather than an old toast.
                            wire.PutErrorMessage += " mounted-replacement-retry";
                            var beforeMountedRetry = wire.StateWrites;
                            Program.Log($"Replacement retry checkpoint: pageEdit={Field<long>(page, "_stateEdit")}, writes={beforeMountedRetry}, tab={Tab()}.");
                            settings.LibraryPageState.Invalidate();
                            await Until(() => wire.StateWrites > beforeMountedRetry);
                            await Until(SaveError);
                        }
                        writes = wire.StateWrites; wire.FailPut = false;
                        settings.LibraryPageState.Invalidate();
                        await Until(() => wire.StateWrites > writes && wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                    }
                    Require(Tab() == "Collections", "Successful recovery reverted the visible view.");
                }
                Program.Log("PASS: actual Library state ordering: " + scenario + ".");
            }
            finally
            {
                wire.FailPut = false; wire.PutGate?.TrySetResult(); wire.PutGate = null;
                wire.FailHeldRead = false; wire.ReadGate?.TrySetResult(); wire.ReadGate = null;
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                if (page != null) await Until(() => !page.IsLoaded);
                await Task.Delay(150);
            }

            void RequireGroupToolbar()
            {
                Require(Field<string>(page!, "_currentAudiobookAxis") == audiobookAxis &&
                    ((ScrollViewer)page!.FindName("AudiobookGroupsPanel")).Visibility == Visibility.Visible &&
                    new[] { "SortComboBox", "OrderComboBox", "OpenFiltersButton", "ResultCountText" }.All(name =>
                        ((FrameworkElement)page.FindName(name)).Visibility == Visibility.Collapsed),
                    "Fresh/retained/refreshed audiobook groups exposed book-only catalog controls: " + audiobookAxis);
            }
            string Tab() => Field<string>(page!, "_currentTab");
            void Click(string name) => ((IInvokeProvider)new ButtonAutomationPeer((Button)page!.FindName(name)).GetPattern(PatternInterface.Invoke)).Invoke();
            async Task Ready() => await Until(() => page!.IsLoaded && Field<bool>(page, "_libraryStateApplied"));
            async Task Leave() { frame.Navigate(typeof(Page)); await Until(() => !page!.IsLoaded); }
            bool SaveError()
            {
                var path = Path.Combine(Program.ResultDirectory, "errors.txt");
                return File.Exists(path) && File.ReadAllText(path).Contains(wire.PutErrorMessage, StringComparison.Ordinal);
            }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
