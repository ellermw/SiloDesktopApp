using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static partial class RetainedLibraryNativeFixture
{
    private static async Task CheckReviewAsync(StackPanel parent, NavigationService navigation, Wire wire, SettingsApi settings, AuthService auth)
    {
        await CheckActivationAndDepartureAsync(parent, navigation, wire, settings);
        await CheckReportedStateOrderingsAsync(parent, navigation, wire, settings);
        var failures = new List<string>();
        foreach (var scenario in new[] { "opt-out", "save-error", "two-edits", "two-edits-failed", "two-edits-confirmed-during-return", "departure-save-error" })
        {
            wire.LibraryId++; wire.Remember = true; wire.Search = "?tab=recommended";
            wire.FailHeldRead = false; wire.FailPut = false;
            settings.NotifyLocalizedSettingChanged("ui.library_page_state");
            var frame = new Frame { Width = 1000, Height = 700, CacheSize = 2 };
            parent.Children.Add(frame); navigation.Frame = frame;
            LibraryPage? page = null;
            var library = new Library { Id = wire.LibraryId, Name = "Review fixture", Type = "movie" };
            try
            {
                frame.Navigate(typeof(LibraryPage), library); page = (LibraryPage)frame.Content;
                await Until(() => page.IsLoaded && Field<LibraryPageStateSnapshot?>(page, "_stateSnapshot") != null);
                await Settle();
                if (scenario == "opt-out")
                {
                    Click("CollectionsTab"); await Settle();
                    wire.Remember = false; settings.NotifyLocalizedSettingChanged("ui.remember_library_page_state");
                    await Until(() => Field<LibraryPageStateSnapshot?>(page, "_stateSnapshot") is { Remember: false });
                    Check(Tab() == "Collections", "Confirmed opt-out preserves active Collections.");
                    wire.FailHeldRead = true; wire.ErrorMessage = "Review failed opt-out revalidation";
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        await Leave(); settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                        var reads = wire.StateReads; frame.GoBack(); await Until(() => page.IsLoaded && wire.StateReads > reads); await Settle();
                        Check(Tab() == "Collections", "Opt-out history return preserves the live tab despite failed revalidation.");
                        await Leave(); settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                        reads = wire.StateReads; frame.Navigate(typeof(LibraryPage), library);
                        await Until(() => page.IsLoaded && wire.StateReads > reads); await Settle();
                        Check(Tab() == "Recommended", "Known opt-out survives failed revalidation on unqualified New activation " + (attempt + 1) + ".");
                        Click("CollectionsTab"); await Settle();
                    }
                    await Leave(); settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                    frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(library, "Library", "Crime"));
                    await Until(() => page.IsLoaded && page.ViewModel.SelectedGenres.Contains("Crime")); await Settle();
                    Check(Tab() == "Library" && page.ViewModel.SelectedGenres.Contains("Crime"), "Explicit tab and genre win over known opt-out with failed revalidation.");
                    await Leave(); settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                    frame.Navigate(typeof(LibraryPage), library); await Until(() => page.IsLoaded); await Settle();
                    Check(Tab() == "Recommended", "Known opt-out remains after an explicit activation with failed revalidation.");

                    // A new owner with an unavailable first read has unknown preference, not the old owner's false.
                    await Leave(); auth.SelectProfile("review-successor"); wire.Remember = true;
                    frame.Navigate(typeof(LibraryPage), library); await Until(() => page.IsLoaded); await Settle();
                    Check(Tab() == "Recommended", "Retired owner's view does not survive the successor's failed first read.");
                    Click("CollectionsTab"); await Settle();
                    await Leave(); settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                    frame.GoBack(); await Until(() => page.IsLoaded); await Settle();
                    Check(Tab() == "Collections", "Old owner's opt-out does not leak into unknown successor preference on return.");
                }
                else if (scenario == "save-error")
                {
                    wire.PutErrorMessage = "Review obsolete save A failure"; wire.FailPut = true;
                    wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var writes = wire.StateWrites; Click("CollectionsTab"); await Until(() => wire.StateWrites > writes);
                    Click("LibraryTab"); await Until(() => Tab() == "Library" && page.ViewModel.GetWindowItem(0) != null);
                    await Task.Delay(650); // B is accepted while A's serialized write is held.
                    wire.FailPut = false; wire.PutGate.SetResult(); wire.PutGate = null;
                    await Until(() => wire.StateWrites >= writes + 2 && wire.Search.Contains("tab=library", StringComparison.Ordinal));
                    await Settle();
                    Check(!Errors().Contains("Review obsolete save A failure", StringComparison.Ordinal), "Failed save A cannot notify after edit B, and B persists successfully.");
                    wire.FailPut = true; wire.PutErrorMessage = "Review current save failure";
                    Click("CollectionsTab"); await Until(() => Errors().Contains("Review current save failure", StringComparison.Ordinal));
                    Check(Tab() == "Collections", "A current save failure still emits its error and preserves the active view.");
                }
                else if (scenario is "two-edits" or "two-edits-failed" or "two-edits-confirmed-during-return")
                {
                    var writes = wire.StateWrites;
                    Click("CollectionsTab");
                    await Until(() => wire.StateWrites > writes && wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                    await Settle(); // First save B completes without any realtime echo.
                    writes = wire.StateWrites;
                    wire.FailPut = scenario == "two-edits-failed";
                    wire.PutErrorMessage = "Review second edit departure failure";
                    wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Click("LibraryTab");
                    await Until(() => wire.StateWrites > writes && page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                    var scroll = (ScrollViewer)page.FindName("LibraryScroller");
                    scroll.ChangeView(null, 400, null, true);
                    await Until(() => scroll.VerticalOffset > 350);
                    var offset = scroll.VerticalOffset;
                    var catalogReads = wire.CatalogReads;
                    await Leave();
                    if (scenario == "two-edits-failed")
                    {
                        wire.PutGate.SetResult(); wire.PutGate = null;
                        await Until(() => Errors().Contains(wire.PutErrorMessage, StringComparison.Ordinal));
                        wire.FailPut = false;
                    }
                    var ratings = wire.RatingReads;
                    if (scenario == "two-edits-confirmed-during-return")
                    {
                        wire.RatingsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        ((CatalogApi)SiloPlayer.App.Services.GetService(typeof(CatalogApi))!).InvalidateRatingCapability();
                    }
                    frame.GoBack(); await Until(() => page.IsLoaded);
                    if (scenario == "two-edits-confirmed-during-return")
                    {
                        await Until(() => wire.RatingReads > ratings);
                        wire.PutGate!.SetResult(); wire.PutGate = null;
                        await Until(() => Field<LibraryPageStateSnapshot?>(page, "_stateSnapshot")?.Search?.Contains("tab=library", StringComparison.Ordinal) == true);
                        wire.RatingsGate!.SetResult(); wire.RatingsGate = null;
                    }
                    await Task.Delay(200);
                    Check(Tab() == "Library" && wire.CatalogReads == catalogReads && Math.Abs(scroll.VerticalOffset - offset) < 2,
                        "Second local save (" + scenario + ") retains C rather than restoring the older confirmed B.");
                    wire.PutGate?.SetResult(); wire.PutGate = null;
                    await Until(() => wire.Search.Contains("tab=library", StringComparison.Ordinal));
                    await Settle();
                }
                else
                {
                    wire.FailPut = true; wire.PutErrorMessage = "Review departure flush failure";
                    wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var writes = wire.StateWrites;
                    Click("CollectionsTab");
                    await Until(() => Tab() == "Collections");
                    await Leave(); // Flush the queued edit before its save debounce.
                    await Until(() => wire.StateWrites > writes);
                    wire.PutGate.SetResult(); wire.PutGate = null;
                    await Until(() => Errors().Contains(wire.PutErrorMessage, StringComparison.Ordinal));
                    wire.FailPut = false; writes = wire.StateWrites;
                    frame.GoBack(); await Until(() => page.IsLoaded);
                    await Until(() => wire.StateWrites > writes && wire.Search.Contains("tab=collections", StringComparison.Ordinal));
                    Check(Tab() == "Collections", "A failed departure flush reports its error and retries the unchanged dirty view on return.");
                }
            }
            finally
            {
                wire.PutGate?.TrySetResult(); wire.PutGate = null; wire.FailPut = false; wire.FailHeldRead = false;
                wire.RatingsGate?.TrySetResult(); wire.RatingsGate = null;
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                if (page != null) await Until(() => !page.IsLoaded);
                await Task.Delay(150);
            }

            void Click(string name) => ((IInvokeProvider)new ButtonAutomationPeer((Button)page!.FindName(name)).GetPattern(PatternInterface.Invoke)).Invoke();
            string Tab() => Field<string>(page!, "_currentTab");
            async Task Leave() { frame.Navigate(typeof(Page)); await Until(() => !page!.IsLoaded); }
            async Task Settle() { await Task.Delay(1100); await Until(() => !page!.ViewModel.IsLoading && !page.ViewModel.IsCollectionsLoading && !Field<bool>(page, "_recommendationsLoading")); }
            void Check(bool condition, string description)
            {
                if (condition) Program.Log("PASS: " + description);
                else { failures.Add(description); Program.Log("FAIL: " + description); }
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));

        static string Errors()
        {
            var path = Path.Combine(Program.ResultDirectory, "errors.txt");
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
    }
}
