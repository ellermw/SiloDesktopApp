using System.Text.Json;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static partial class RetainedLibraryNativeFixture
{
    private static async Task CheckReportedStateOrderingsAsync(StackPanel parent, NavigationService navigation, Wire wire, SettingsApi settings)
    {
        foreach (var scenario in new[] { "opt-out-genre-history", "shell-refresh", "any-genre", "reconnect-mounted", "reconnect-detached", "reconnect-in-flight", "cross-library-held", "cross-library-failed" })
        {
            wire.LibraryId++; wire.Remember = scenario != "opt-out-genre-history";
            wire.Search = "?tab=library"; wire.FailPut = false;
            wire.PutErrorMessage = "State ordering save failure: " + scenario;
            if (scenario == "any-genre")
                wire.Search += "&match=any&groups[0][match]=all&groups[0][rules][0][field]=year&groups[0][rules][0][op]=gte&groups[0][rules][0][value]=2000" +
                    "&groups[1][match]=all&groups[1][rules][0][field]=rating_imdb&groups[1][rules][0][op]=gte&groups[1][rules][0][value]=7";
            settings.LibraryPageState.Invalidate();
            var frame = new Frame { Width = 1000, Height = 700, CacheSize = 2 };
            parent.Children.Add(frame); navigation.Frame = frame;
            var library = new Library { Id = wire.LibraryId, Name = "State ordering fixture", Type = "movie" };
            LibraryPage? page = null;
            try
            {
                frame.Navigate(typeof(LibraryPage), scenario is "opt-out-genre-history" or "shell-refresh" or "any-genre"
                    ? new LibraryPage.NavigationArgs(library, "Library", "Crime") : (object)library);
                page = (LibraryPage)frame.Content;
                await Ready();
                await Until(() => page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
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
                else if (scenario == "any-genre")
                {
                    var query = wire.LastCatalogRequest.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                        .Where(pair => pair.Length == 2).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                    Require(query.GetValueOrDefault("match") == "all", "Explicit genre remained OR-combined with restored advanced groups.");
                    using var groups = JsonDocument.Parse(query["groups"]);
                    Require(groups.RootElement.GetArrayLength() == 3 && groups.RootElement.EnumerateArray().Any(group =>
                        group.GetProperty("rules").EnumerateArray().Any(rule => rule.GetProperty("field").GetString() == "genre" &&
                            rule.GetProperty("value").GetString() == "Crime")),
                        "The actual catalog request omitted the genre constraint or the restored groups.");
                    await Until(() => wire.Search.Contains("match=all", StringComparison.Ordinal) && wire.Search.Contains("Crime", StringComparison.Ordinal));
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
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                if (page != null) await Until(() => !page.IsLoaded);
                await Task.Delay(150);
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
