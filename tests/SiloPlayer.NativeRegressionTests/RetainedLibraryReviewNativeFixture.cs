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
        var failures = new List<string>();
        foreach (var scenario in new[] { "opt-out", "save-error" })
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
                        Check(Tab() == "Recommended", "Known opt-out survives failed detached revalidation, return " + (attempt + 1) + ".");
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
                else
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
            }
            finally
            {
                wire.PutGate?.TrySetResult(); wire.PutGate = null; wire.FailPut = false; wire.FailHeldRead = false;
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
