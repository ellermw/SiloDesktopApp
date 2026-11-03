using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class LibraryLettersNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window, bool verifyImmediateFeedback = false, bool verifyOwnership = false,
        bool verifySecondBoundary = false, bool verifyCursorRecovery = false)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://library-letters.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture");
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "images"));
        var navigation = new NavigationService();
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings), [typeof(EventChannelClient)] = events,
            [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
        }, () => new LibraryViewModel(catalog)));
        Frame? frame = null;
        var dispatchFailures = new List<Exception>();
        try
        {
            var runs = verifySecondBoundary
                ? new[] { ("movie", 1280, 1000, "newer"), ("movie", 1280, 1000, "query"), ("movie", 1280, 1000, "manual"), ("movie", 1280, 1000, "visit") }
                : verifyCursorRecovery ? new[] { ("movie", 1280, 1000, "success"), ("movie", 1280, 1000, "failure") }
                : new[] { ("movie", 1280, 1000, ""), ("audiobook", 960, 550, "") };
            foreach (var (type, width, height, dispatchCase) in runs)
            {
                wire.ItemType = type;
                wire.ScopeVersion = 0; wire.MPosition = 600; wire.RejectFresh = false; wire.InvalidCursors = 0;
                wire.LetterIndexGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                window.AppWindow.ResizeClient(new(width, height));
                frame = new Frame { Width = width, Height = height }; parent.Children.Add(frame); navigation.Frame = frame;
                frame.Navigate(typeof(LibraryPage), new Library { Id = wire.LibraryId++, Name = "Fixture library", Type = type });
                var page = (LibraryPage)frame.Content;
                await Until(() => page.IsLoaded && page.ViewModel.Library is not null);
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => page.ViewModel.GetWindowItem(0) is not null);
                var scroll = (ScrollViewer)page.FindName("LibraryScroller");
                var rail = (Border)page.FindName("LibraryLetterRail");
                var letters = (Grid)page.FindName("LibraryLetterButtons");
                page.UpdateLayout();
                await Until(() => scroll.ViewportWidth > 0);
                await Task.Delay(150);
                var pendingIndexWidth = scroll.ViewportWidth;
                var railColumn = (ColumnDefinition)page.FindName("LibraryLetterColumn");
                if (!page.ViewModel.IsLetterIndexLoading || railColumn.Width.Value <= 0 ||
                    page.ViewModel.LetterIndex is not null || letters.Children.Cast<Button>().Any(button => button.IsEnabled))
                    throw new InvalidOperationException("The pending index must reserve space without enabling guessed letters.");
                wire.LetterIndexGate.TrySetResult();
                await page.ViewModel.EnsureLetterIndexAsync();
                await Until(() => rail.Visibility == Visibility.Visible && letters.Children.Count == 27);
                page.UpdateLayout();
                await Task.Delay(100);
                if (Math.Abs(scroll.ViewportWidth - pendingIndexWidth) > .1)
                    throw new InvalidOperationException("Index arrival changed the catalog viewport width.");
                var buttons = letters.Children.Cast<Button>().ToArray();
                Button Letter(string letter) => buttons.Single(button => (string)button.Tag == letter);
                if (Letter("B").IsEnabled || !Letter("Z").IsEnabled || buttons.Count(button => button.IsTabStop) != 1)
                    throw new InvalidOperationException("Letter availability or single tab stop is incorrect.");
                if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(Letter("Z")) != "Z, 1 title")
                    throw new InvalidOperationException("Letter title count is missing from its accessible name.");
                if (verifyImmediateFeedback)
                {
                    wire.CatalogWindowGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.CatalogWindowStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                ((IInvokeProvider)new ButtonAutomationPeer(Letter("M")).GetPattern(PatternInterface.Invoke)).Invoke();
                if (verifyImmediateFeedback)
                {
                    try
                    {
                        await wire.CatalogWindowStarted!.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        await Until(() => Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("M")) == "Loading titles");
                        if (!page.ViewModel.IsLetterJumpInProgress || !Letter("M").IsEnabled)
                            throw new InvalidOperationException("The pending letter must remain selected and available for repeated activation.");
                        var status = page.FindName("LibraryLetterStatus") as TextBlock;
                        if (status is not { Visibility: Visibility.Visible } || !status.Text.Contains("M", StringComparison.Ordinal))
                            throw new InvalidOperationException("The held letter must have visible feedback naming its pending choice.");
                        // If the page has moved to the target, only actual target-owned
                        // content or inert placeholders may occupy those card slots.
                        var canvas = (Canvas)page.FindName("VirtualGridCanvas");
                        foreach (var card in canvas.Children.OfType<LibraryGridCard>())
                            if (card.Tag is int index && card.MediaItem is { } item && item.ContentId != "item-" + index)
                                throw new InvalidOperationException("A pending target exposes another position's clickable card.");
                        await CaptureFeedbackAsync(page, $"library-letter-pending-{width}.png");
                    }
                    finally { wire.CatalogWindowGate!.TrySetResult(); }
                }
                await Until(() => scroll.VerticalOffset > 10000 && page.ViewModel.GetWindowItem(600) is not null);
                if (!wire.Seeks.Contains(600) || wire.InvalidCursors != 0 || page.ViewModel.ErrorMessage is not null)
                    throw new InvalidOperationException("Letter activation did not seek to its exact catalog position.");
                var layout = typeof(LibraryPage).GetMethod("GetGridLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                var columns = (int)layout.GetType().GetProperty("Columns")!.GetValue(layout)!;
                var rowHeight = (double)layout.GetType().GetProperty("RowHeight")!.GetValue(layout)!;
                if (verifySecondBoundary || verifyCursorRecovery)
                {
                    try
                    {
                        if (verifySecondBoundary) await VerifySecondBoundaryAsync(page, frame, scroll, wire, Letter, dispatchCase);
                        else await VerifyCursorRecoveryAsync(page, frame, scroll, wire, Letter, dispatchCase, columns, rowHeight);
                    }
                    catch (Exception ex) { dispatchFailures.Add(ex); Program.Log($"DISPATCH FAILURE {dispatchCase}: {ex.Message}"); }
                    frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                    await Until(() => !page.IsLoaded); await Task.Delay(100); frame = null;
                    continue;
                }
                if (verifyImmediateFeedback || verifyOwnership)
                    await VerifyOwnershipAsync(page, scroll, wire, client, Letter, columns, rowHeight);
                scroll.ChangeView(null, (900 / columns) * rowHeight, null, true);
                await Until(() => Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("A")) == "Current letter");
                ((IInvokeProvider)new ButtonAutomationPeer(Letter("Z")).GetPattern(PatternInterface.Invoke)).Invoke();
                try
                {
                    await Until(() => Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 2 &&
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z")) == "Current letter");
                }
                catch (TimeoutException)
                {
                    object? Field(string name) => typeof(LibraryPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                    Program.Log($"DIAGNOSTIC: Z end clamp native={scroll.VerticalOffset:F3} maximum={scroll.ScrollableHeight:F3} viewport={scroll.ViewportHeight:F3} extent={scroll.ExtentHeight:F3} logical={Field("_libraryVerticalOffset")} pending={Field("_pendingLibraryOffset")} chosen={Field("_chosenLibraryEndLetter")} active={Field("_activeLibraryLetter")} columns={columns} rowHeight={rowHeight:F3} targetLoaded={page.ViewModel.GetWindowItem(999) is not null} Zhelp={Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z"))}");
                    throw;
                }
                await Task.Delay(150);
                if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z")) != "Current letter")
                    throw new InvalidOperationException("The final letter was lost at the clamped bottom row.");
                page.UpdateLayout();
                var content = (Grid)page.FindName("LibraryContentArea");
                var scrollerRight = scroll.TransformToVisual(content).TransformPoint(new()).X + scroll.ActualWidth;
                var railLeft = rail.TransformToVisual(content).TransformPoint(new()).X;
                if (scrollerRight > railLeft || (height < 600 ? letters.ColumnDefinitions.Count != 2 : letters.ColumnDefinitions.Count != 1))
                    throw new InvalidOperationException("Letter rail overlaps the catalog or uses the wrong responsive columns.");
                foreach (var button in buttons)
                {
                    var point = button.TransformToVisual(content).TransformPoint(new());
                    if (button.ActualHeight < 10 || point.Y < 0 || point.Y + button.ActualHeight > content.ActualHeight + 1)
                        throw new InvalidOperationException("A letter is clipped by the native content viewport.");
                }
                var cachedWidth = scroll.ViewportWidth;
                wire.LetterIndexGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                page.ViewModel.SuspendCatalogLoads();
                var reopen = (Task)typeof(LibraryPage).GetMethod("EnsureLibraryCatalogLoadedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                await reopen.WaitAsync(TimeSpan.FromSeconds(1));
                await Task.Delay(100); page.UpdateLayout();
                if (page.ViewModel.LetterIndex is not null || Math.Abs(scroll.ViewportWidth - cachedWidth) > .1)
                    throw new InvalidOperationException("A cached return waited for the index or changed the catalog width.");
                wire.LetterIndexGate.TrySetResult();
                await page.ViewModel.EnsureLetterIndexAsync();
                await Until(() => rail.Visibility == Visibility.Visible);
                page.UpdateLayout(); await Task.Delay(100);
                if (Math.Abs(scroll.ViewportWidth - cachedWidth) > .1)
                    throw new InvalidOperationException("A replacement index changed the cached catalog width.");
                page.ViewModel.SelectedSort = "year";
                await Until(() => rail.Visibility == Visibility.Collapsed);
                page.ViewModel.SelectedSort = "title";
                await page.ViewModel.EnsureLetterIndexAsync();
                await Until(() => rail.Visibility == Visibility.Visible);
                if (type == "audiobook")
                {
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("AudiobookAuthorsButton")).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Until(() => rail.Visibility == Visibility.Collapsed);
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("AudiobookBooksButton")).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Until(() => rail.Visibility == Visibility.Visible);
                }
                page.UpdateLayout(); await Task.Delay(100);
                var beforeFailureWidth = scroll.ViewportWidth;
                var reservedWidth = railColumn.Width.Value;
                wire.FailIndex = true;
                wire.LetterIndexGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                page.ViewModel.SuspendCatalogLoads();
                var failedIndex = page.ViewModel.EnsureLetterIndexAsync();
                await Until(() => page.ViewModel.IsLetterIndexLoading && railColumn.Width.Value > 0);
                wire.LetterIndexGate.TrySetResult();
                await failedIndex;
                await Until(() => !page.ViewModel.IsLetterIndexLoading && railColumn.Width.Value == 0 &&
                    rail.Visibility == Visibility.Collapsed);
                page.UpdateLayout(); await Task.Delay(100);
                if (Math.Abs(scroll.ViewportWidth - beforeFailureWidth - reservedWidth) > .1)
                    throw new InvalidOperationException("A failed index left an empty gutter instead of restoring the full viewport.");
                wire.FailIndex = false;
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RecommendedTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => rail.Visibility == Visibility.Collapsed);
                Program.Log($"PASS: actual {type} Library alphabet rail, exact distant jump, bottom selection, availability, accessible counts, visibility, failed-index gutter recovery and bounds at {width}x{height}.");
                if (verifyImmediateFeedback)
                {
                    Invoke((Button)page.FindName("LibraryTab"));
                    await page.ViewModel.EnsureLetterIndexAsync();
                    await Until(() => rail.Visibility == Visibility.Visible);
                    var departed = wire.HoldNextWindow(fail: true); Invoke(Letter("M"));
                    try
                    {
                        await departed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        frame.Navigate(typeof(Page));
                        await Until(() => !page.IsLoaded);
                        departed.Release.TrySetResult(); await Task.Delay(100);
                        if (page.ViewModel.IsLetterJumpInProgress || page.ViewModel.ErrorMessage is not null)
                            throw new InvalidOperationException("A departed Library kept its letter request or accepted its late error.");
                    }
                    finally { departed.Release.TrySetResult(); }
                }
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                await Until(() => !page.IsLoaded); await Task.Delay(100); frame = null;
            }
            if (dispatchFailures.Count > 0) throw new AggregateException(dispatchFailures);
        }
        finally
        {
            wire.LetterIndexGate?.TrySetResult();
            wire.CatalogWindowGate?.TrySetResult();
            var ownedPage = frame?.Content as LibraryPage;
            frame?.Navigate(typeof(Page)); if (frame is not null) parent.Children.Remove(frame);
            if (ownedPage is not null) await Until(() => !ownedPage.IsLoaded);
            await Task.Delay(150); navigation.Frame = null; field.SetValue(null, previous);
        }
    }
    private static async Task VerifyCursorRecoveryAsync(LibraryPage page, Frame frame, ScrollViewer scroll, Wire wire,
        Func<string, Button> letter, string outcome, int columns, double rowHeight)
    {
        wire.ScopeVersion = 1; wire.MPosition = 900; wire.RejectFresh = outcome == "failure";
        Invoke(letter("M"));
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && !page.ViewModel.IsLoading);
        if (outcome == "failure")
        {
            await Until(() => page.ViewModel.ErrorMessage is not null || page.ViewModel.LetterJumpError is not null);
            await Task.Delay(200); // Scoped Retry must survive ordinary viewport debounce callbacks.
            await (Task)typeof(LibraryPage).GetMethod("FillViewportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            var previousHeight = scroll.ViewportHeight;
            frame.Height = 900; page.UpdateLayout();
            await Until(() => Math.Abs(scroll.ViewportHeight - previousHeight) > 20);
            await Task.Delay(100);
            frame.Height = 1000; page.UpdateLayout();
            await Until(() => Math.Abs(scroll.ViewportHeight - previousHeight) < 1);
            await Task.Delay(100);
            var retry = (Button)page.FindName("LibraryLetterRetry");
            if (page.ViewModel.ErrorMessage is not null || page.ViewModel.LetterJumpError is null ||
                page.ViewModel.GetWindowItem(600) is not null || page.ViewModel.GetWindowItem(0) is not null)
                throw new InvalidOperationException("Signed letter recovery lost scoped Retry or restored retired cards.");
            await Until(() => retry.Visibility == Visibility.Visible);
            wire.ScopeVersion = 2; wire.MPosition = 720; wire.RejectFresh = false;
            Invoke(retry);
            await Until(() => page.ViewModel.GetWindowItem(720) is not null && Math.Abs(scroll.VerticalOffset - (720 / columns) * rowHeight) < 1);
        }
        else
            await Until(() => page.ViewModel.GetWindowItem(900) is not null && Math.Abs(scroll.VerticalOffset - (900 / columns) * rowHeight) < 1);
        if (page.ViewModel.ErrorMessage is not null || page.ViewModel.LetterJumpError is not null || wire.InvalidCursors != 1)
            throw new InvalidOperationException("Signed scope recovery did not finish under current exact letter authority.");
        Program.Log($"PASS: actual letter signed-scope {outcome}, moved M600→M900/revalidated Retry720, retired cards and scoped outcomes.");
    }
    private static async Task VerifySecondBoundaryAsync(LibraryPage page, Frame frame, ScrollViewer scroll, Wire wire,
        Func<string, Button> letter, string scenario)
    {
        var interleaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Wire.WindowReadGate? held = null;
        var before = scroll.VerticalOffset;
        void AfterFirstQueue(int position)
        {
            if (position != 20) return;
            // Page's first dispatcher callback was enqueued before this one.
            // Its second callback is therefore queued after our interleaver.
            page.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var pending = typeof(LibraryPage).GetField("_pendingLibraryOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                    if (pending is not double) throw new InvalidOperationException("The first letter callback did not establish its second pending boundary.");
                    if (scenario == "newer") { held = wire.HoldNextWindow(); Invoke(letter("M")); }
                    else if (scenario == "query") page.ViewModel.SelectedGenre = "Comedy";
                    else if (scenario == "manual") { scroll.ChangeView(null, before + .5, null, true); page.UpdateLayout(); }
                    else
                    {
                        frame.Navigate(typeof(Page)); frame.GoBack();
                        if (!ReferenceEquals(frame.Content, page)) throw new InvalidOperationException("The same cached Library visit was not restored.");
                        frame.UpdateLayout(); page.UpdateLayout();
                        // The old numeric offset is the same as this cached
                        // instance's pending value. A current visit's held M
                        // must still win over the departed A callback.
                        held = wire.HoldNextWindow();
                        _ = page.ViewModel.JumpToLetterCommand.ExecuteAsync("M");
                    }
                    Program.Log($"DISPATCH INTERLEAVE {scenario}: oldPending={pending} observed={scroll.VerticalOffset:F3} before={before:F3}");
                    interleaved.TrySetResult();
                }
                catch (Exception ex) { interleaved.TrySetException(ex); }
            });
        }
        page.ViewModel.LetterJumpRequested += AfterFirstQueue;
        try
        {
            Invoke(letter("A"));
            await interleaved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (held is not null) await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            var expected = before + (scenario == "manual" ? .5 : 0);
            if (Math.Abs(scroll.VerticalOffset - expected) > .2)
                throw new InvalidOperationException($"A stale second dispatch overrode {scenario}: expected {expected:F3}, observed {scroll.VerticalOffset:F3}.");
            Program.Log($"PASS: second native letter dispatch retires after {scenario} at unchanged/half-pixel offset.");
        }
        finally { page.ViewModel.LetterJumpRequested -= AfterFirstQueue; held?.Release.TrySetResult(); }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static string Help(Button button) => Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(button);

    private static async Task VerifyOwnershipAsync(LibraryPage page, ScrollViewer scroll, Wire wire, SiloApiClient client,
        Func<string, Button> letter, int columns, double rowHeight)
    {
        // Let A succeed synchronously, but choose held M before A's queued
        // page callback can run. It must not move the current native viewport.
        Wire.WindowReadGate? queued = null;
        var beforeQueued = scroll.VerticalOffset;
        void ChooseNewer(int position)
        {
            if (position != 20) return;
            queued = wire.HoldNextWindow(); Invoke(letter("M"));
        }
        page.ViewModel.LetterJumpRequested += ChooseNewer;
        try
        {
            Invoke(letter("A"));
            await Until(() => queued is not null);
            await queued!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            if (!page.ViewModel.IsLetterJumpInProgress || Math.Abs(scroll.VerticalOffset - beforeQueued) > 1)
                throw new InvalidOperationException("A queued older success moved the viewport after a newer letter choice.");
        }
        finally
        {
            page.ViewModel.LetterJumpRequested -= ChooseNewer;
            queued?.Release.TrySetResult();
        }
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && page.ViewModel.GetWindowItem(600) is not null);

        // Actual native manual movement must retire the held seek before its
        // failed response is allowed to restore windows or show a stale error.
        var manual = wire.HoldNextWindow(fail: true);
        Invoke(letter("M"));
        try
        {
            await manual.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var requested = (200 / columns) * rowHeight;
            scroll.ChangeView(null, requested, null, true);
            await Until(() => Math.Abs(scroll.VerticalOffset - requested) < 2 && !page.ViewModel.IsLetterJumpInProgress);
            await Until(() => page.ViewModel.GetWindowItem(200) is not null);
            var actual = scroll.VerticalOffset;
            manual.Release.TrySetResult(); await Task.Delay(150);
            if (Math.Abs(scroll.VerticalOffset - actual) > 1 || page.ViewModel.ErrorMessage is not null)
                throw new InvalidOperationException("A retired failed letter overrode the reader's native manual viewport.");
        }
        finally { manual.Release.TrySetResult(); }

        manual = wire.HoldNextWindow(fail: true); Invoke(letter("M"));
        try
        {
            await manual.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var requested = scroll.VerticalOffset + .5;
            scroll.ChangeView(null, requested, null, true);
            await Until(() => Math.Abs(scroll.VerticalOffset - requested) < .2 && !page.ViewModel.IsLetterJumpInProgress);
            manual.Release.TrySetResult(); await Task.Delay(100);
            if (Math.Abs(scroll.VerticalOffset - requested) > .2 || page.ViewModel.ErrorMessage is not null)
                throw new InvalidOperationException("A subpixel manual scroll failed to retire the pending letter.");
        }
        finally { manual.Release.TrySetResult(); }

        var feedback = (StackPanel)page.FindName("LibraryLetterFeedback");
        var status = (TextBlock)page.FindName("LibraryLetterStatus");
        var retry = (Button)page.FindName("LibraryLetterRetry");
        var older = wire.HoldNextWindow(); Invoke(letter("M"));
        await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = wire.HoldNextWindow(); Invoke(letter("A"));
        try
        {
            await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => Help(letter("A")) == "Loading titles" && status.Text.Contains("A", StringComparison.Ordinal));
            newer.Release.TrySetResult();
            await Until(() => !page.ViewModel.IsLetterJumpInProgress && Help(letter("A")) == "Current letter");
            var current = scroll.VerticalOffset;
            older.Release.TrySetResult(); await Task.Delay(150);
            if (Math.Abs(scroll.VerticalOffset - current) > 1 || Help(letter("A")) != "Current letter" ||
                feedback.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("An older reversed letter completion replaced the latest choice.");
        }
        finally { older.Release.TrySetResult(); newer.Release.TrySetResult(); }

        older = wire.HoldNextWindow(fail: true); Invoke(letter("M"));
        await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        newer = wire.HoldNextWindow(); Invoke(letter("M"));
        try
        {
            await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            older.Release.TrySetResult(); await Task.Delay(100);
            if (Help(letter("M")) != "Loading titles" || !page.ViewModel.IsLetterJumpInProgress ||
                retry.Visibility == Visibility.Visible || page.ViewModel.ErrorMessage is not null)
                throw new InvalidOperationException("A same-letter old failure cleared the newer pending choice.");
            newer.Release.TrySetResult();
            await Until(() => !page.ViewModel.IsLetterJumpInProgress && Help(letter("M")) == "Current letter");
        }
        finally { older.Release.TrySetResult(); newer.Release.TrySetResult(); }

        var failed = wire.HoldNextWindow(fail: true); Invoke(letter("A"));
        try
        {
            await failed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); failed.Release.TrySetResult();
            await Until(() => retry.Visibility == Visibility.Visible && feedback.Visibility == Visibility.Visible);
            if (!status.Text.Contains("A", StringComparison.Ordinal) || page.ViewModel.ErrorMessage is not null)
                throw new InvalidOperationException("Letter failure was not scoped to its current choice.");
            await CaptureFeedbackAsync(page, $"library-letter-failure-{page.ActualWidth:F0}.png");
            var retried = wire.HoldNextWindow(); Invoke(retry);
            try
            {
                await retried.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => Help(letter("A")) == "Loading titles" && retry.Visibility == Visibility.Collapsed);
                retried.Release.TrySetResult();
                await Until(() => Help(letter("A")) == "Current letter" && feedback.Visibility == Visibility.Collapsed);
            }
            finally { retried.Release.TrySetResult(); }
        }
        finally { failed.Release.TrySetResult(); }
        foreach (var change in new[] { "profile", "language", "filter" })
        {
            var retired = wire.HoldNextWindow(fail: true); Invoke(letter("M"));
            try
            {
                await retired.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (change == "profile") client.SetProfile("scoped-synthetic-" + page.ActualWidth);
                else if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
                else page.ViewModel.SelectedGenre = "Comedy";
                retired.Release.TrySetResult();
                await Until(() => !page.ViewModel.IsLetterJumpInProgress && feedback.Visibility == Visibility.Collapsed);
                if (page.ViewModel.ErrorMessage is not null || page.ViewModel.GetWindowItem(600) is not null)
                    throw new InvalidOperationException($"A retired {change} accepted a late letter target or error.");
                await page.ViewModel.LoadCommand.ExecuteAsync(null); await page.ViewModel.EnsureLetterIndexAsync();
                await Until(() => letter("M").IsEnabled);
            }
            finally { retired.Release.TrySetResult(); }
        }
        Program.Log("PASS: actual Library controls retire queued success, delayed failure after native manual/subpixel scroll and profile/language/filter changes; preserve reversed/repeated choices and scoped failure/Retry.");
    }
    private static async Task CaptureFeedbackAsync(LibraryPage page, string name)
    {
        var canvas = (Canvas)page.FindName("VirtualGridCanvas");
        await Until(() =>
        {
            var cards = canvas.Children.OfType<LibraryGridCard>()
                .Where(card => card.Visibility == Visibility.Visible && card.Tag is int).ToArray();
            return cards.Any(card => page.ViewModel.GetWindowItem((int)card.Tag) is not null) &&
                cards.All(card => ReferenceEquals(card.MediaItem, page.ViewModel.GetWindowItem((int)card.Tag)));
        });
        page.UpdateLayout();
        await MediaParityNativeFixture.CaptureAsync(page, name);
    }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 300; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Library letter native state did not settle."); }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int InvalidCursors, LibraryId = 11;
        internal bool FailIndex;
        internal int ScopeVersion, MPosition = 600;
        internal bool RejectFresh;
        internal string ItemType = "movie";
        internal TaskCompletionSource? LetterIndexGate;
        internal TaskCompletionSource? CatalogWindowGate, CatalogWindowStarted;
        private readonly System.Collections.Concurrent.ConcurrentQueue<WindowReadGate> heldReads = new();
        internal sealed class WindowReadGate(bool fail)
        {
            internal bool Fail => fail;
            internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        internal WindowReadGate HoldNextWindow(bool fail = false)
        { var gate = new WindowReadGate(fail); heldReads.Enqueue(gate); return gate; }
        internal List<int> Seeks { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/catalog/search/capabilities") return Reply(new { letter_index = true });
            if (path == "/api/v2/catalog/letters") return CompleteLetterIndexAsync(Reply(new
            {
                total = 1000,
                letters = new[] { new { letter = "#", count = 20, position = 0 }, new { letter = "A", count = MPosition == 600 ? 679 : MPosition - 20, position = 20 }, new { letter = "M", count = MPosition == 600 ? 300 : 999 - MPosition, position = MPosition }, new { letter = "Z", count = 1, position = 999 } },
                runs = MPosition == 600
                    ? new[] { new { letter = "#", position = 0 }, new { letter = "A", position = 20 }, new { letter = "M", position = 600 }, new { letter = "A", position = 900 }, new { letter = "Z", position = 999 } }
                    : new[] { new { letter = "#", position = 0 }, new { letter = "A", position = 20 }, new { letter = "M", position = MPosition }, new { letter = "Z", position = 999 } },
            }), ct);
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                var limit = int.Parse(query["limit"]); var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
                if (query.ContainsKey("seek")) Seeks.Add(offset);
                if (query.TryGetValue("cursor", out var cursor))
                {
                    var scope = cursor.Split(':');
                    if (scope.Length != 4 || scope[0] != query["library_id"] || scope[1] != query["limit"] || scope[3] != ScopeVersion.ToString())
                    {
                        InvalidCursors++;
                        return Reply(new { status = 400, type = "https://fixture.invalid/docs/api/v2/problems/invalid_cursor", detail = "The cursor is malformed, tampered with, or belongs to a different query." }, HttpStatusCode.BadRequest);
                    }
                    if (!query.ContainsKey("seek")) offset = int.Parse(scope[2]);
                }
                else if (RejectFresh) return Reply(new { detail = "Synthetic failed fresh scope" }, HttpStatusCode.ServiceUnavailable);
                var count = Math.Min(limit, 1000 - offset);
                WindowReadGate? held = null;
                if (query.ContainsKey("skip_total")) heldReads.TryDequeue(out held);
                return CompleteCatalogWindowAsync(Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = "item-" + index, title = "Fixture " + index, type = ItemType }),
                    total = 1000, total_exact = !query.ContainsKey("skip_total"), window_cursor = query["library_id"] + ":" + limit + ":0:" + ScopeVersion,
                    page = new { has_more = offset + count < 1000, next_cursor = query["library_id"] + ":" + limit + ":" + (offset + count) + ":" + ScopeVersion } }), ct, held);
            }
            if (path.EndsWith("/layout") || path.EndsWith("/sections")) return Reply(new { sections = Array.Empty<object>() });
            if (path.EndsWith("/filters")) return Reply(new { genres = Array.Empty<string>() });
            return Reply(new { status = 404, detail = "No isolated fixture value" }, HttpStatusCode.NotFound);
        }
        private async Task<HttpResponseMessage> CompleteLetterIndexAsync(Task<HttpResponseMessage> reply, CancellationToken ct)
        {
            if (LetterIndexGate is { } gate) await gate.Task.WaitAsync(ct);
            if (FailIndex) return await Reply(new { detail = "Synthetic unavailable index" }, HttpStatusCode.ServiceUnavailable);
            return await reply;
        }
        private async Task<HttpResponseMessage> CompleteCatalogWindowAsync(Task<HttpResponseMessage> reply, CancellationToken ct, WindowReadGate? held)
        {
            if (held is not null)
            {
                held.Started.TrySetResult(); await held.Release.Task; // Deliberately late transport.
                if (held.Fail) return await Reply(new { detail = "Synthetic failed target" }, HttpStatusCode.ServiceUnavailable);
            }
            if (CatalogWindowGate is { } gate)
            {
                CatalogWindowStarted?.TrySetResult();
                await gate.Task.WaitAsync(ct);
            }
            return await reply;
        }
        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
    }
}
