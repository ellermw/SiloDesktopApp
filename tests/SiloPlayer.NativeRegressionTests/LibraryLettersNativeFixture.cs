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
        bool verifySecondBoundary = false, bool verifyCursorRecovery = false, bool verifyPolicy = false, bool verifyScanRefresh = false)
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
            var runs = verifyScanRefresh
                ? new[] { ("movie", 1280, 1000, "land"), ("movie", 1280, 1000, "newer"), ("movie", 1280, 1000, "access"), ("movie", 1280, 1000, "visit"), ("movie", 1280, 1000, "failure"), ("movie", 1280, 1000, "refresh-error"), ("movie", 1280, 1000, "extent") }
                : verifySecondBoundary
                ? new[] { ("movie", 1280, 1000, "newer"), ("movie", 1280, 1000, "query"), ("movie", 1280, 1000, "manual"), ("movie", 1280, 1000, "visit") }
                : verifyCursorRecovery ? new[] { ("movie", 1280, 1000, "success"), ("movie", 1280, 1000, "failure"), ("movie", 1280, 1000, "manual") }
                : verifyPolicy ? new[] { ("movie", 1280, 1000, ""), ("mixed", 960, 550, "") }
                : new[] { ("movie", 1280, 1000, ""), ("audiobook", 960, 550, "") };
            if (verifyScanRefresh && Environment.GetEnvironmentVariable("SILO_LIBRARY_SCAN_CASE") is { Length: > 0 } scanCase)
                runs = runs.Where(run => run.Item4 == scanCase).ToArray();
            foreach (var (type, width, height, dispatchCase) in runs)
            {
                wire.ItemType = type;
                wire.ScopeVersion = 0; wire.MPosition = 600; wire.RejectFresh = false;
                wire.InvalidCursors = wire.ExpectedRetiredCursors = 0;
                wire.CurrentType = type; wire.CurrentLibraryId = wire.LibraryId;
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
                if (!page.ViewModel.IsLetterIndexLoading || railColumn.Width.Value <= 0 || rail.Visibility != Visibility.Visible ||
                    page.ViewModel.LetterIndex is not null || letters.Children.Cast<Button>().Any(button => button.IsEnabled))
                    throw new InvalidOperationException("The pending index must show the greyed rail without enabling guessed letters.");
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
                if (verifyScanRefresh)
                {
                    await VerifyScanRefreshAsync(page, frame, parent, scroll, wire, client, Letter, dispatchCase);
                    frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
                    await Until(() => !page.IsLoaded); frame = null;
                    continue;
                }
                await VerifyFailedViewportWindowRailAsync(page, scroll, rail, railColumn, wire);
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
                if (!wire.Seeks.Contains(600) || wire.InvalidCursors != wire.ExpectedRetiredCursors || page.ViewModel.ErrorMessage is not null)
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
                {
                    await VerifyScrollIntentAsync(page, frame, scroll, wire, Letter, columns, rowHeight);
                    await VerifyOwnershipAsync(page, scroll, wire, client, Letter, columns, rowHeight);
                }
                scroll.ChangeView(null, (900 / columns) * rowHeight, null, true);
                await Until(() => Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("A")) == "Current letter");
                ((IInvokeProvider)new ButtonAutomationPeer(Letter("Z")).GetPattern(PatternInterface.Invoke)).Invoke();
                try
                {
                    await Until(() => Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 2 &&
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z")) == "Current letter");
                }
                catch
                {
                    object? Field(string name) => typeof(LibraryPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                    Program.Log($"DIAGNOSTIC: Z end clamp native={scroll.VerticalOffset:F3} maximum={scroll.ScrollableHeight:F3} viewport={scroll.ViewportHeight:F3} extent={scroll.ExtentHeight:F3} logical={Field("_libraryVerticalOffset")} pending={Field("_pendingLibraryOffset")} chosen={Field("_chosenLibraryEndLetter")} active={Field("_activeLibraryLetter")} columns={columns} rowHeight={rowHeight:F3} targetLoaded={page.ViewModel.GetWindowItem(999) is not null} Zhelp={Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z"))}");
                    Program.Log($"TRACE bottom offset={scroll.VerticalOffset} extent={scroll.ScrollableHeight} viewport={scroll.ViewportHeight} Z={Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z"))} total={page.ViewModel.TotalCount} window={page.ViewModel.WindowStartIndex}..{page.ViewModel.WindowEndIndex} jump={page.ViewModel.IsLetterJumpInProgress} error={page.ViewModel.ErrorMessage}");
                    await CaptureFeedbackAsync(page, $"bottom-diagnostic-{width}.png");
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
                // A failed index keeps the greyed rail in place with a Retry: it neither vanishes
                // silently nor shifts the cards sideways.
                var indexRetry = (Button)page.FindName("LibraryLetterIndexRetry");
                await Until(() => !page.ViewModel.IsLetterIndexLoading && page.ViewModel.LetterIndexFailed &&
                    rail.Visibility == Visibility.Visible && indexRetry.Visibility == Visibility.Visible);
                page.UpdateLayout(); await Task.Delay(100);
                if (Math.Abs(railColumn.Width.Value - reservedWidth) > .1 || Math.Abs(scroll.ViewportWidth - beforeFailureWidth) > .1)
                    throw new InvalidOperationException("A failed index moved the rail gutter or the cards.");
                if (letters.Children.Cast<Button>().Any(button => button.IsEnabled || button.IsTabStop))
                    throw new InvalidOperationException("A failed index left guessed letters enabled.");
                wire.FailIndex = false;
                ((IInvokeProvider)new ButtonAutomationPeer(indexRetry).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => !page.ViewModel.LetterIndexFailed && page.ViewModel.LetterIndex is not null &&
                    indexRetry.Visibility == Visibility.Collapsed && letters.Children.Cast<Button>().Any(button => button.IsEnabled));
                page.UpdateLayout(); await Task.Delay(100);
                if (Math.Abs(scroll.ViewportWidth - beforeFailureWidth) > .1)
                    throw new InvalidOperationException("Retrying the index moved the cards.");
                Program.Log("PASS: a failed letter index keeps the greyed rail and its gutter, offers Retry, and Retry restores the letters.");
                if (verifyPolicy)
                {
                    await VerifyFailedLibraryFlagsReadAsync(page, wire, catalog);
                    if (type == "mixed") await VerifyRestoredBrowseTypeAsync(frame, page, wire, settings);
                }
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
            wire.CatalogWindowGate?.TrySetResult(); wire.CountGate?.TrySetResult();
            var ownedPage = frame?.Content as LibraryPage;
            frame?.Navigate(typeof(Page)); if (frame is not null) parent.Children.Remove(frame);
            if (ownedPage is not null) await Until(() => !ownedPage.IsLoaded);
            await Task.Delay(150); navigation.Frame = null; field.SetValue(null, previous);
        }
    }
    private static async Task VerifyFailedViewportWindowRailAsync(LibraryPage page, ScrollViewer scroll, Border rail,
        ColumnDefinition railColumn, Wire wire)
    {
        page.UpdateLayout();
        var viewportWidth = scroll.ViewportWidth;
        var reservedWidth = railColumn.Width.Value;
        var index = page.ViewModel.LetterIndex;
        var firstCard = page.ViewModel.GetWindowItem(0);
        if (index is null || firstCard is null || rail.Visibility != Visibility.Visible || reservedWidth <= 0)
            throw new InvalidOperationException("The viewport failure fixture requires loaded cards and a loaded letter rail.");
        void VerifyLayout()
        {
            page.UpdateLayout();
            if (rail.Visibility != Visibility.Visible || Math.Abs(railColumn.Width.Value - reservedWidth) > .1 ||
                Math.Abs(scroll.ViewportWidth - viewportWidth) > .1 ||
                !ReferenceEquals(index, page.ViewModel.LetterIndex) || !ReferenceEquals(firstCard, page.ViewModel.GetWindowItem(0)))
                throw new InvalidOperationException("A later viewport window failure or retry moved the rail gutter or retained cards.");
        }
        // The uncached range fails twice: each attempt clears ErrorMessage before
        // fetching, then sets it again without replacing the currently loaded cards.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var held = wire.HoldNextWindow(fail: true);
            var load = page.ViewModel.LoadWindowAsync(300, 60);
            try
            {
                await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(100);
                if (!page.ViewModel.IsLoading || page.ViewModel.ErrorMessage is not null)
                    throw new InvalidOperationException("The held viewport retry did not clear the previous catalog error.");
                VerifyLayout();
                held.Release.TrySetResult();
                await load;
                await Until(() => page.ViewModel.ErrorMessage is not null);
                await Task.Delay(100);
                VerifyLayout();
            }
            finally { held.Release.TrySetResult(); await load; }
        }
        Program.Log("PASS: later viewport failures and retries keep the loaded letter rail, gutter and retained cards in place.");
    }

    private static async Task VerifyCursorRecoveryAsync(LibraryPage page, Frame frame, ScrollViewer scroll, Wire wire,
        Func<string, Button> letter, string outcome, int columns, double rowHeight)
    {
        wire.ScopeVersion = 1; wire.MPosition = 900; wire.RejectFresh = outcome != "success";
        Invoke(letter("M"));
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && !page.ViewModel.IsLoading);
        if (outcome != "success")
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
            if (outcome == "manual")
            {
                wire.RejectFresh = false;
                var requested = scroll.VerticalOffset + .5;
                ManualScroll(page, scroll, requested);
                await Until(() => Math.Abs(scroll.VerticalOffset - requested) < .2 &&
                    page.ViewModel.GetWindowItem(600) is not null && page.ViewModel.LetterJumpError is null);
                if (page.ViewModel.ErrorMessage is not null || wire.InvalidCursors != 1)
                    throw new InvalidOperationException("A sub-row scroll did not reload the retired signed-scope viewport.");
                Program.Log("PASS: sub-row manual scroll releases failed signed-scope recovery and reloads the unchanged visible range.");
                return;
            }
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
        EventHandler<object>? afterFrame = null;
        var before = scroll.VerticalOffset;
        void AfterFirstQueue(int position)
        {
            if (position != 20) return;
            // The page registers its one-frame observer in the preceding
            // callback. Register ours after it, so its target callback runs
            // first and its native ChangeView remains behind our interleave.
            page.DispatcherQueue.TryEnqueue(() =>
            {
                afterFrame = (_, _) =>
                {
                    Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= afterFrame;
                    afterFrame = null;
                    page.DispatcherQueue.TryEnqueue(Interleave);
                };
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += afterFrame;
            });
            void Interleave()
            {
                try
                {
                    var pending = typeof(LibraryPage).GetField("_pendingLibraryOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
                    if (pending is not double) throw new InvalidOperationException("The first letter callback did not establish its second pending boundary.");
                    if (Math.Abs(scroll.VerticalOffset - before) > .2)
                        throw new InvalidOperationException("The second-boundary interleave ran after native movement.");
                    if (scenario == "newer") { held = wire.HoldNextWindow(); Invoke(letter("M")); }
                    else if (scenario == "query") page.ViewModel.SelectedGenre = "Comedy";
                    else if (scenario == "manual") { ManualScroll(page, scroll, before + .5); page.UpdateLayout(); }
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
            }
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
        finally
        {
            page.ViewModel.LetterJumpRequested -= AfterFirstQueue;
            if (afterFrame is not null) Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= afterFrame;
            held?.Release.TrySetResult();
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async Task VerifyScanRefreshAsync(LibraryPage page, Frame frame, StackPanel parent, ScrollViewer scroll, Wire wire,
        SiloApiClient client, Func<string, Button> letter, string scenario)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(LibraryPage).GetField(name, Private)!.GetValue(page);
        void Call(string name, params object[] args) => typeof(LibraryPage).GetMethod(name, Private)!.Invoke(page, args);
        void Scan(string eventName, object payload)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            Call("OnRecommendedRealtimeEvent", "catalog", eventName, document.RootElement);
            // Document disposal before the dispatch also tests synchronous parsing.
        }
        void Storm()
        {
            foreach (var name in new[] { "catalog.library.changed", "library.changed", "library.item_added", "catalog.item.changed", "metadata.updated" })
                Scan(name, new { library_id = wire.CurrentLibraryId });
        }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Scan refresh: " + message); }
        async Task Drain() { await Task.Delay(120); }

        await Until(() => !page.ViewModel.IsLoading && Field("_pendingLibraryScroll") is null);
        if (scenario == "extent")
        {
            // The requested final row can be clamped by the old native extent.
            // Grow that extent between ChangeView and its landing continuation.
            var oldMaximum = scroll.ScrollableHeight;
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Call("QueueLibraryOffset", oldMaximum + 1000, (Func<bool>)(() => true));
            Call("ApplyPendingLibraryOffset");
            page.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var request = Field("_pendingLibraryScroll")!;
                    var requested = (double)request.GetType().GetProperty("Offset")!.GetValue(request)!;
                    Check(Math.Abs(requested - oldMaximum) < 2, "owned landing did not retain the accepted clamp");
                    var canvas = (Canvas)page.FindName("VirtualGridCanvas");
                    canvas.Height += 1500;
                    page.UpdateLayout();
                    observed.TrySetResult();
                }
                catch (Exception ex) { observed.TrySetException(ex); }
            });
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => Field("_pendingLibraryScroll") is null);
            Program.Log("PASS: a clamped owned landing releases after the native extent grows.");
            return;
        }
        Scan("catalog.library.changed", new { library_id = wire.CurrentLibraryId + 100 });
        await Drain();
        Check(!(bool)Field("_catalogContentDirty")!, "another numeric library scope dirtied this grid");
        var held = wire.HoldNextWindow(fail: scenario == "failure");
        Invoke(letter("M"));
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstReads = wire.WindowSeeks.Count;
        var before = scroll.VerticalOffset;
        Storm();
        Scan("catalog.library.changed", new { library_id = "malformed" });
        Scan("metadata.updated", new { });
        await Task.Delay(2200);
        Storm();
        await Task.Delay(2200);
        Check(!held.Cancelled && wire.WindowSeeks.Count == firstReads && page.ViewModel.IsLetterJumpInProgress,
            "events across two batch windows canceled or duplicated the distant M read");

        if (scenario == "access")
        {
            client.InvalidateAccessContext();
            var access = client.CaptureContext();
            Check(client.TryInvalidateAccessContext(access, out _, transportCatchUp: true), "transport did not advance after access loss");
            Call("OnLibraryAccessChanged", access);
            var cards = ((Canvas)page.FindName("VirtualGridCanvas")).Children.OfType<LibraryGridCard>();
            Check(cards.All(card => card.Visibility == Visibility.Collapsed), "access loss did not hide bound cards synchronously");
            Check(held.Cancelled, "access loss did not cancel query work");
            held.Release.TrySetResult(); await Drain();
            Check(page.ViewModel.GetRenderableWindowItem(600) is null, "late access-lost target was exposed");
            Program.Log("PASS: explicit access loss bypasses scan/letter holds, hides bound cards and rejects the late read.");
            return;
        }

        if (scenario == "visit")
        {
            frame.Navigate(typeof(Page));
            Check(held.Cancelled, "navigation did not cancel the actual held query");
            Check(Field("_catalogContentTimer") is not DispatcherTimer { IsEnabled: true }, "navigation left the batching timer running");
            var revisit = wire.HoldNextWindow();
            frame.GoBack(); frame.UpdateLayout(); page.UpdateLayout();
            Check(ReferenceEquals(frame.Content, page), "the cached page was not reused");
            var pendingBatch = revisit;
            try
            {
                await revisit.Started.Task.WaitAsync(TimeSpan.FromSeconds(7));
                var owner = (long)Field("_catalogContentOwner")!;
                Program.Log($"TRACE scan revisit first owner={owner} dirty={Field("_catalogContentDirty")} loading={page.ViewModel.IsLoading}");
                if (owner == 0)
                {
                    // Returning may first restore its foreground viewport. That
                    // query is distinct from the retained two-second scan batch.
                    pendingBatch = wire.HoldNextWindow(freshOnly: true);
                    revisit.Release.TrySetResult();
                    await pendingBatch.Started.Task.WaitAsync(TimeSpan.FromSeconds(7));
                    owner = (long)Field("_catalogContentOwner")!;
                }
                Program.Log($"TRACE scan revisit batch owner={owner} dirty={Field("_catalogContentDirty")} ready={Field("_catalogContentBatchReady")} pendingOffset={Field("_pendingLibraryScroll") is not null}");
                Check(owner != 0, "retained dirty batch did not resume after foreground restoration");
                held.Release.TrySetResult(); await Drain();
                Check((long)Field("_catalogContentOwner")! == owner, "old finally released the new visit's refresh");
                var visit = (long)Field("_libraryVisit")!;
                Call("LibraryPage_Unloaded", page, null!);
                Check(page.IsLoaded && (long)Field("_libraryVisit")! == visit &&
                    (long)Field("_catalogContentOwner")! == owner && !pendingBatch.Cancelled,
                    "a stale unload retired the newly attached visit");
                parent.Children.Remove(frame);
                await Until(() => !page.IsLoaded);
                await Until(() => pendingBatch.Cancelled);
                Check((long)Field("_libraryVisit")! != visit, "actual visual detachment did not retire the visit");
                parent.Children.Add(frame);
                await Until(() => page.IsLoaded);
            }
            finally { held.Release.TrySetResult(); revisit.Release.TrySetResult(); pendingBatch.Release.TrySetResult(); }
            await Until(() => (long)Field("_catalogContentOwner")! == 0);
            Program.Log("PASS: unload cancels query work; owned dirty state resumes once and stale completion cannot release the new owner.");
            return;
        }

        var target = 600;
        if (scenario == "newer")
        {
            var newer = wire.HoldNextWindow();
            Invoke(letter("Z"));
            await newer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            held.Release.TrySetResult();
            held = newer; target = 999;
            var reads = wire.WindowSeeks.Count;
            Storm(); await Task.Delay(2200);
            Check(!newer.Cancelled && wire.WindowSeeks.Count == reads && page.ViewModel.PendingLetter == "Z",
                "events or superseded M completion canceled/duplicated Z");
        }

        // The first observer is installed after the page's jump callback. It
        // interleaves at the first queue, then behind the native frame before
        // ApplyPendingLibraryOffset's second dispatch can move the ScrollViewer.
        var boundaries = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? afterFrame = null;
        void OnTarget(int position)
        {
            if (position != target) return;
            page.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var reads = wire.WindowSeeks.Count;
                    Check((long)Field("_queuedLetterJumpVersion")! != 0, "first landing boundary was not queued");
                    Call("CatalogContentTimer_Tick", page, new object());
                    Check(wire.WindowSeeks.Count == reads && (long)Field("_catalogContentOwner")! == 0,
                        "scan started before the native frame boundary");
                    afterFrame = (_, _) =>
                    {
                        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= afterFrame;
                        afterFrame = null;
                        page.DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                Check(Field("_pendingLibraryOffset") is double, "second native scroll boundary was not pending");
                                Call("CatalogContentTimer_Tick", page, new object());
                                Check(wire.WindowSeeks.Count == reads && (long)Field("_catalogContentOwner")! == 0 &&
                                    Math.Abs(scroll.VerticalOffset - before) < 2, "scan refreshed before native scroll landing");
                                boundaries.TrySetResult();
                            }
                            catch (Exception ex) { boundaries.TrySetException(ex); }
                        });
                    };
                    Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += afterFrame;
                }
                catch (Exception ex) { boundaries.TrySetException(ex); }
            });
        }
        page.ViewModel.LetterJumpRequested += OnTarget;
        var refresh = wire.HoldNextWindow(fail: scenario == "refresh-error");
        Wire.WindowReadGate? trailing = null;
        var freshReads = 0;
        try
        {
            held.Release.TrySetResult();
            if (scenario != "failure") await boundaries.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await refresh.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check((long)Field("_catalogContentOwner")! != 0 && refresh.Fresh, "one pending batch did not start a fresh snapshot");
            var layout = typeof(LibraryPage).GetMethod("GetGridLayout", Private)!.Invoke(page, null)!;
            var columns = (int)layout.GetType().GetProperty("Columns")!.GetValue(layout)!;
            var rowHeight = (double)layout.GetType().GetProperty("RowHeight")!.GetValue(layout)!;
            var landed = scenario == "failure" ? before : Math.Min((target / columns) * rowHeight, scroll.ScrollableHeight);
            Check(Math.Abs(scroll.VerticalOffset - landed) < 2, "pending batch ran before the accepted native position");
            var visible = (int)(scroll.VerticalOffset / rowHeight) * columns;
            Check(refresh.Offset <= visible && refresh.Offset >= Math.Max(0, visible - columns * 2), "refresh did not start at the landed viewport");
            Check(!page.ViewModel.RequiresCatalogConfirmation, "healthy scan quarantined authorized cards");
            var reads = wire.WindowSeeks.Count;
            await Task.Delay(2200);
            Check(!refresh.Cancelled && wire.WindowSeeks.Count == reads, "held refresh canceled/restarted itself across a batch window");
            if (scenario == "land")
            {
                Storm(); await Task.Delay(2200);
                Storm(); await Task.Delay(2200);
                Check(!refresh.Cancelled && wire.WindowSeeks.Count == reads, "scan storm interrupted the running content refresh");
                freshReads = wire.FreshWindowSeeks.Count;
                trailing = wire.HoldNextWindow(freshOnly: true);
            }
        }
        finally
        {
            held.Release.TrySetResult(); refresh.Release.TrySetResult();
            page.ViewModel.LetterJumpRequested -= OnTarget;
            if (afterFrame is not null) Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= afterFrame;
        }
        if (trailing is not null)
        {
            try
            {
                await trailing.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(wire.FreshWindowSeeks.Count == freshReads + 1, "storm did not coalesce into exactly one trailing snapshot");
            }
            finally { trailing.Release.TrySetResult(); }
        }
        await Until(() => (long)Field("_catalogContentOwner")! == 0);
        if (scenario == "refresh-error")
            Check(page.ViewModel.ErrorMessage is null && page.CanPreserveOnReconnect &&
                page.ViewModel.GetRenderableWindowItem(target)?.ContentId == "item-" + target && scroll.VerticalOffset > 0,
                "failed content refresh lost its healthy viewport or reconnect preservation");
        var complete = wire.FreshWindowSeeks.Count;
        await Task.Delay(2200);
        Check(wire.FreshWindowSeeks.Count == complete, "a consumed dirty batch refreshed itself again");
        Program.Log($"PASS: scan {scenario} holds distant reads and performs one bounded refresh at the accepted viewport.");
    }
    private static string Help(Button button) => Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(button);

    // Supply the manipulation-start signal as well as native movement. A bare
    // ChangeView is automatic and must no longer stand in for new user input.
    private static void ManualScroll(LibraryPage page, ScrollViewer scroll, double offset)
    {
        typeof(LibraryPage).GetMethod("LibraryScroller_DirectManipulationStarted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new object[] { scroll, new object() });
        scroll.ChangeView(null, offset, null, true);
    }

    private static async Task VerifyScrollIntentAsync(LibraryPage page, Frame frame, ScrollViewer scroll, Wire wire,
        Func<string, Button> letter, int columns, double rowHeight)
    {
        var top = (Button)page.FindName("ScrollToTopButton");
        foreach (var outcome in new[] { "held-success", "held-failure", "failed" })
        {
            Invoke(letter("M"));
            await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1);
            var held = wire.HoldNextWindow(fail: outcome != "held-success");
            Invoke(letter("M"));
            try
            {
                await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (outcome == "failed")
                {
                    held.Release.TrySetResult();
                    await Until(() => page.ViewModel.LetterJumpError is not null);
                }
                await Until(() => top.Visibility == Visibility.Visible);
                Invoke(top);
                await Until(() => scroll.VerticalOffset < 1 && !page.ViewModel.IsLetterJumpInProgress &&
                    page.ViewModel.LetterJumpError is null && page.ViewModel.GetWindowItem(0) is not null);
                held.Release.TrySetResult(); await Task.Delay(150);
                if (scroll.VerticalOffset > 1 || page.ViewModel.LetterJumpError is not null || page.ViewModel.ErrorMessage is not null)
                    throw new InvalidOperationException($"Scroll to top did not retire the {outcome} letter jump.");
            }
            finally { held.Release.TrySetResult(); }
        }

        // An immediate response can queue its target before the native
        // animation publishes its first intermediate ViewChanged event.
        var fastFrames = 0;
        void ObserveFast(object? sender, ScrollViewerViewChangedEventArgs args) => fastFrames++;
        scroll.ViewChanged += ObserveFast;
        try
        {
            scroll.ChangeView(null, scroll.VerticalOffset + 3 * rowHeight, null, disableAnimation: false);
            Invoke(letter("M"));
            if (fastFrames != 0)
                throw new InvalidOperationException("The immediate response regression did not precede the first native frame.");
            // Observe the old native animation's whole bounded lifetime, not
            // just a momentary exact offset before its remaining frames run.
            await Task.Delay(600);
            await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1);
            Program.Log($"PASS: immediate letter response starts before the first native frame and retains exact target after {fastFrames} frames.");
        }
        finally { scroll.ViewChanged -= ObserveFast; }
        Invoke(top);
        await Until(() => scroll.VerticalOffset < 1 && !page.ViewModel.IsLetterJumpInProgress && page.ViewModel.GetWindowItem(0) is not null);

        // Continue a native animated scroll that began before the choice. Its
        // intermediate frames have no new input authority over the held letter.
        var coasting = wire.HoldNextWindow();
        var origin = scroll.VerticalOffset;
        scroll.ChangeView(null, origin + 3 * rowHeight, null, disableAnimation: false);
        Invoke(letter("M"));
        try
        {
            await coasting.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => Math.Abs(scroll.VerticalOffset - origin) > 1);
            await Task.Delay(300);
            if (!page.ViewModel.IsLetterJumpInProgress || page.ViewModel.PendingLetter != "M")
                throw new InvalidOperationException("Earlier native animation cancelled the latest letter choice.");
        }
        finally { coasting.Release.TrySetResult(); }
        try { await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1); }
        catch (TimeoutException ex)
        {
            var currentLayout = typeof(LibraryPage).GetMethod("GetGridLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            object? Field(string name) => typeof(LibraryPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
            throw new InvalidOperationException(JsonSerializer.Serialize(new
            {
                phase = "coasting-completion", offset = scroll.VerticalOffset, expected = (600 / columns) * rowHeight,
                columns, rowHeight, currentLayout, pending = page.ViewModel.IsLetterJumpInProgress,
                letter = page.ViewModel.PendingLetter, error = page.ViewModel.LetterJumpError,
                queuedOffset = Field("_pendingLibraryOffset"), logicalOffset = Field("_libraryVerticalOffset"),
                queuedVersion = Field("_queuedLetterJumpVersion"), total = page.ViewModel.TotalCount,
                extent = scroll.ScrollableHeight
            }), ex);
        }

        scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        await Until(() => Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1);
        var beforeClamp = scroll.VerticalOffset;
        var clampHeight = frame.Height;
        var clampViewport = scroll.ViewportHeight;
        var clamped = wire.HoldNextWindow(); Invoke(letter("M"));
        try
        {
            await clamped.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Widening an already-max-column grid grows its posters and
            // extent. A taller viewport creates the intended bottom clamp.
            frame.Height = clampHeight + 100; page.UpdateLayout();
            await Until(() => scroll.VerticalOffset < beforeClamp - 1);
            if (scroll.ViewportHeight <= clampViewport || Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) > 1)
                throw new InvalidOperationException("The resize regression did not create a native bottom clamp.");
            if (!page.ViewModel.IsLetterJumpInProgress || page.ViewModel.PendingLetter != "M")
                throw new InvalidOperationException("A resize-driven native clamp cancelled the held letter choice.");
            Program.Log($"PASS: actual resize clamp retains held M: offset {beforeClamp:F3}->{scroll.VerticalOffset:F3}, viewport {clampViewport:F3}->{scroll.ViewportHeight:F3}.");
        }
        finally { frame.Height = clampHeight; page.UpdateLayout(); clamped.Release.TrySetResult(); }
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1);

        var failed = wire.HoldNextWindow(fail: true); Invoke(letter("A"));
        var height = frame.Height;
        try
        {
            await failed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); failed.Release.TrySetResult();
            await Until(() => page.ViewModel.LetterJumpError is not null);
            var error = page.ViewModel.LetterJumpError;
            // Expose more than the target's bounded 100-card window holds.
            frame.Height = height + 45 * rowHeight; page.UpdateLayout();
            var layout = typeof(LibraryPage).GetMethod("GetGridLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            var range = ((int StartIndex, int EndIndex))typeof(LibraryPage).GetMethod("GetVisibleItemRange", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, new[] { layout })!;
            if (page.ViewModel.GetWindowItem(range.EndIndex) is not null)
                throw new InvalidOperationException("Resize failure regression did not expose any uncached rows.");
            await Until(() => page.ViewModel.GetWindowItem(range.EndIndex) is not null);
            await Until(() => ((Button)page.FindName("LibraryLetterRetry")).Visibility == Visibility.Visible);
            if (page.ViewModel.LetterJumpError != error || page.ViewModel.ErrorMessage is not null)
                throw new InvalidOperationException("Ordinary resize loading cleared or globalized scoped Retry.");
        }
        finally { frame.Height = height; page.UpdateLayout(); failed.Release.TrySetResult(); }
        // A failed seek cancels the pending read for the still-visible range.
        // It must restart that same range without any subsequent user input.
        Invoke(letter("M"));
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1);
        var cancelledViewport = wire.HoldNextWindow();
        Wire.WindowReadGate? failedTarget = null;
        try
        {
            var requested = (400 / columns) * rowHeight + .25;
            ManualScroll(page, scroll, requested);
            await cancelledViewport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (page.ViewModel.GetWindowItem(400) is not null)
                throw new InvalidOperationException("The failed-letter regression must start with an uncached visible range.");
            failedTarget = wire.HoldNextWindow(fail: true); Invoke(letter("A"));
            await failedTarget.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            failedTarget.Release.TrySetResult();
            await Until(() => !page.ViewModel.IsLetterJumpInProgress && page.ViewModel.LetterJumpError is not null &&
                page.ViewModel.GetWindowItem(400) is not null);
            if (Math.Abs(scroll.VerticalOffset - requested) > .2 || page.ViewModel.ErrorMessage is not null)
                throw new InvalidOperationException("Failed letter recovery moved the viewport or published a global error.");
            var canvas = (Canvas)page.FindName("VirtualGridCanvas");
            await Until(() => canvas.Children.OfType<LibraryGridCard>().Any(card => card.Tag is int index &&
                index == 400 && ReferenceEquals(card.MediaItem, page.ViewModel.GetWindowItem(index))));
            Program.Log("PASS: failed letter reloads the cancelled visible range and binds its cards without new scroll/resize input.");
        }
        finally { cancelledViewport.Release.TrySetResult(); failedTarget?.Release.TrySetResult(); }
        Invoke(letter("M"));
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1);
        var viewport = wire.HoldNextWindow();
        Wire.WindowReadGate? target = null;
        try
        {
            var requested = (400 / columns) * rowHeight + .25;
            ManualScroll(page, scroll, requested);
            await viewport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            target = wire.HoldNextWindow(); Invoke(letter("M"));
            await target.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ManualScroll(page, scroll, requested + .25);
            // Neither late read is released: the same row must get a new read.
            await Until(() => Math.Abs(scroll.VerticalOffset - requested - .25) < .2 &&
                !page.ViewModel.IsLetterJumpInProgress && page.ViewModel.GetWindowItem(400) is not null);
        }
        finally { viewport.Release.TrySetResult(); target?.Release.TrySetResult(); }
        Invoke(letter("M"));
        await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (600 / columns) * rowHeight) < 1);
        Program.Log("PASS: Scroll to top retires held/failed letters; prior animation and resize clamps preserve pending intent; resize retains Retry; sub-row input reloads a cancelled viewport read.");
    }

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
            ManualScroll(page, scroll, requested);
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
            ManualScroll(page, scroll, requested);
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
            // Each case must start with the target absent. A preceding case's
            // ordinary viewport read can otherwise retain a valid M window.
            Invoke(letter("A"));
            await Until(() => !page.ViewModel.IsLetterJumpInProgress && Math.Abs(scroll.VerticalOffset - (20 / columns) * rowHeight) < 1);
            if (page.ViewModel.GetWindowItem(600) is not null)
                throw new InvalidOperationException($"The retired {change} regression already had its target before the held request.");
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
    private static async Task VerifyFailedLibraryFlagsReadAsync(LibraryPage page, Wire wire, CatalogApi catalog)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var refresh = typeof(LibraryPage).GetMethod("RefreshLibraryFlagsAsync", flags)!;
        var ensure = typeof(LibraryPage).GetMethod("EnsureLibraryCatalogLoadedAsync", flags)!;
        var dirty = typeof(LibraryPage).GetField("_libraryFlagsDirty", flags)!;
        var reads = wire.LibraryReads;
        try
        {
            wire.FailLibraries = true;
            // Returning navigation awaits this same policy read before opening
            // either the saved Library tab or another tab with a later selection.
            await (Task)refresh.Invoke(page, [false])!;
            if (!(bool)dirty.GetValue(page)! || wire.LibraryReads != reads + 1)
                throw new InvalidOperationException("A failed policy read must remain dirty for a later visit.");
            await (Task)ensure.Invoke(page, null)!;
            await (Task)ensure.Invoke(page, null)!;
            if (wire.LibraryReads != reads + 1)
                throw new InvalidOperationException("Opening Library repeated this visit's failed policy read and error toast.");

            wire.FailLibraries = false;
            catalog.InvalidateLibraryCache();
            await Until(() => !(bool)dirty.GetValue(page)!);
            if (wire.LibraryReads != reads + 2)
                throw new InvalidOperationException("A policy invalidation must still permit recovery in the same visit.");
            Program.Log("PASS: opening Library does not repeat a failed policy read; dirty policy can still recover on invalidation.");
        }
        finally { wire.FailLibraries = false; }
    }

    private static async Task VerifyRestoredBrowseTypeAsync(Frame frame, LibraryPage page, Wire wire, SettingsApi settings)
    {
        page.ViewModel.SelectedSort = "year"; page.ViewModel.SelectedType = "movie";
        var scroll = (ScrollViewer)page.FindName("LibraryScroller");
        scroll.ChangeView(null, 0, null, true);
        await page.ViewModel.LoadCommand.ExecuteAsync(null);
        await Until(() => page.ViewModel.TotalCount > 0 && !page.ViewModel.IsLoading && scroll.VerticalOffset < 1);
        int PendingSaves() => ((System.Collections.ICollection)typeof(LibraryPage)
            .GetProperty("_pendingStateSaves", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).Count;
        async Task<int> ReturnAfterRemoteChangeAsync(string search)
        {
            frame.Navigate(typeof(Page));
            // This visit's own departure save lands first. A still-pending local
            // save rightly outranks any server snapshot on return.
            await Until(() => !page.IsLoaded && PendingSaves() == 0);
            // Another client then changes the saved browse scope while this exact
            // LibraryPage and its completed catalog are cached. Its realtime
            // settings change retires the confirmed state document.
            wire.SavedSearch = search;
            settings.LibraryPageState.Invalidate();
            var before = wire.CatalogScopes.Count;
            frame.GoBack();
            return before;
        }
        // The server accepts this visit's departure save of its current scope.
        wire.SavedSearch = "?tab=library&sort=year&type=movie";
        try
        {
            foreach (var restoredType in new string?[] { "episode", "movie", null })
            {
                var reads = await ReturnAfterRemoteChangeAsync("?tab=library&sort=year" + (restoredType == null ? "" : "&type=" + restoredType));
                await Until(() => ReferenceEquals(frame.Content, page) && page.IsLoaded && page.ViewModel.SelectedType == restoredType &&
                    wire.CatalogScopes.Count > reads && page.ViewModel.GetWindowItem(0) != null && !page.ViewModel.IsLoading);
                if (page.ViewModel.SelectedSort != "year" || page.ViewModel.TotalCount != 1000 ||
                    wire.CatalogScopes.Skip(reads).Any(scope => scope.Type != restoredType || scope.Group is not null))
                    throw new InvalidOperationException("Cached Library return did not reload under its restored non-title browse scope.");
                await Until(() => Descendants(page).OfType<SiloPlayer.Controls.LibraryGridCard>().Any(card => card.MediaItem != null));
                if (((TextBlock)page.FindName("LibraryEmptyText")).Visibility == Visibility.Visible)
                    throw new InvalidOperationException("Changed restored browse type displayed an empty Library instead of reloaded cards.");
            }
            foreach (var (sort, order, genre) in new (string Sort, string Order, string? Genre)[]
            {
                ("title", "asc", null), // Sort only; media type remains unchanged.
                ("title", "desc", null), // Direction only.
                ("title", "desc", "Drama"), // Guided filter only.
            })
            {
                var oldItem = page.ViewModel.GetWindowItem(0);
                var reads = await ReturnAfterRemoteChangeAsync($"?tab=library&sort={sort}&order={order}" + (genre is null ? "" : "&genre=" + genre));
                await Until(() => ReferenceEquals(frame.Content, page) && page.IsLoaded &&
                    page.ViewModel.SelectedSort == sort && page.ViewModel.SelectedOrder == order && page.ViewModel.SelectedGenre == genre &&
                    wire.CatalogScopes.Count > reads && page.ViewModel.GetWindowItem(0) is not null && !page.ViewModel.IsLoading);
                var expectedSort = order == "desc" ? "-" + sort : sort;
                var requests = wire.CatalogScopes.Skip(reads).ToArray();
                if (requests[0].Cursor is not null || requests.Any(scope => scope.Sort != expectedSort || scope.Genre != genre || scope.Type is not null) ||
                    ReferenceEquals(oldItem, page.ViewModel.GetWindowItem(0)))
                    throw new InvalidOperationException("Cached Library return reused cards/cursors from before a restored sort, direction or genre change.");
            }
            Program.Log("PASS: cached mixed Library returns reload changed type, sort, direction and guided filters without reusing the previous query's cursor or cards.");
        }
        finally { wire.SavedSearch = null; }
    }

    private static async Task InvokeLetterAsync(Button button, LibraryViewModel model)
    {
        var jump = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Jumped(int _) => jump.TrySetResult();
        model.LetterJumpRequested += Jumped;
        try
        {
            await Until(() => button.IsLoaded && button.IsEnabled);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await jump.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // The page applies that model event on its dispatcher, then WinUI settles ChangeView.
            await Task.Delay(150);
        }
        finally { model.LetterJumpRequested -= Jumped; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++) { var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static async Task Until(Func<bool> ready, int attempts = 300)
    { for (var i = 0; i < attempts; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Library letter native state did not settle."); }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int InvalidCursors, ExpectedRetiredCursors, LibraryId = 11;
        internal bool RejectNextCursor;
        internal int CurrentLibraryId;
        internal int CatalogIndexShift;
        internal string CurrentType = "movie";
        internal string? LastOrder, LastGenre;
        internal bool FailIndex, OmitWindowTotals, FailLibraries;
        internal int LibraryReads;
        internal int ScopeVersion, MPosition = 600;
        internal bool RejectFresh;
        internal string ItemType = "movie";
        internal TaskCompletionSource? LetterIndexGate, CatalogWindowGate, CountGate, CatalogWindowStarted;
        private readonly System.Collections.Concurrent.ConcurrentQueue<WindowReadGate> heldReads = new();
        internal sealed class WindowReadGate(bool fail, bool freshOnly = false)
        {
            internal bool Fail => fail;
            internal bool FreshOnly => freshOnly;
            internal volatile bool Cancelled;
            internal bool Fresh;
            internal int Offset;
            internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        internal WindowReadGate HoldNextWindow(bool fail = false, bool freshOnly = false)
        { var gate = new WindowReadGate(fail, freshOnly); heldReads.Enqueue(gate); return gate; }
        internal List<int> Seeks { get; } = [];
        internal List<int> WindowSeeks { get; } = [];
        internal List<int> FreshWindowSeeks { get; } = [];
        internal string? SavedSearch;
        internal List<(string? Type, string? Group, string? Sort, string? Genre, string? Cursor)> CatalogScopes { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (SavedSearch != null && path == "/api/v2/settings/values/effective")
                return Reply(new { items = new object[] { new { key = "ui.remember_library_page_state", value = (object)true },
                    new { key = "ui.library_page_state", value = (object)new { version = 1, libraries = new Dictionary<string, object> { [CurrentLibraryId.ToString()] = new { search = SavedSearch } } } } } });
            if (SavedSearch != null && path.EndsWith("/settings/values/ui.library_page_state") && request.Method == HttpMethod.Put)
                return Reply(new { key = "ui.library_page_state", value = new { } });
            if (path == "/api/v2/user/libraries")
            {
                LibraryReads++;
                if (FailLibraries) return Reply(new { detail = "Synthetic unavailable libraries" }, HttpStatusCode.ServiceUnavailable);
                return Reply(new { items = new[] { new { id = CurrentLibraryId, type = CurrentType, enabled = true } } });
            }
            if (path == "/api/v2/catalog/search/capabilities") return Reply(new { letter_index = true });
            if (path == "/api/v2/catalog/letters")
            {
                return CompleteLetterIndexAsync(Reply(new
            {
                total = 1000,
                letters = new[] { new { letter = "#", count = 20, position = 0 }, new { letter = "A", count = MPosition == 600 ? 679 : MPosition - 20, position = 20 }, new { letter = "M", count = MPosition == 600 ? 300 : 999 - MPosition, position = MPosition }, new { letter = "Z", count = 1, position = 999 } },
                runs = MPosition == 600
                    ? new[] { new { letter = "#", position = 0 }, new { letter = "A", position = 20 }, new { letter = "M", position = 600 }, new { letter = "A", position = 900 }, new { letter = "Z", position = 999 } }
                    : new[] { new { letter = "#", position = 0 }, new { letter = "A", position = 20 }, new { letter = "M", position = MPosition }, new { letter = "Z", position = 999 } },
            }), ct);
            }
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                LastOrder = query.GetValueOrDefault("sort"); LastGenre = query.GetValueOrDefault("genre");
                CatalogScopes.Add((query.GetValueOrDefault("type"), query.GetValueOrDefault("group"),
                    LastOrder, LastGenre, query.GetValueOrDefault("cursor")));
                var limit = int.Parse(query["limit"]); var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
                if (query.ContainsKey("seek")) Seeks.Add(offset);
                if (query.TryGetValue("cursor", out var cursor))
                {
                    if (RejectNextCursor)
                    {
                        RejectNextCursor = false; InvalidCursors++; ExpectedRetiredCursors++;
                        return Reply(new { status = 400, type = "https://fixture.invalid/docs/api/v2/problems/invalid_cursor", detail = "Synthetic policy retired this cursor." }, HttpStatusCode.BadRequest);
                    }
                    var scope = cursor.Split(':');
                    if (scope.Length != 5 || scope[0] != query["library_id"] || scope[1] != query["limit"] ||
                        scope[3] != query.GetValueOrDefault("group", "") || scope[4] != ScopeVersion.ToString())
                    {
                        InvalidCursors++;
                        return Reply(new { status = 400, type = "https://fixture.invalid/docs/api/v2/problems/invalid_cursor", detail = "The cursor is malformed, tampered with, or belongs to a different query." }, HttpStatusCode.BadRequest);
                    }
                    if (!query.ContainsKey("seek")) offset = int.Parse(scope[2]);
                }
                else if (RejectFresh) return Reply(new { detail = "Synthetic failed fresh scope" }, HttpStatusCode.ServiceUnavailable);
                var count = Math.Min(limit, 1000 - offset);
                var skipTotal = query.ContainsKey("skip_total");
                WindowReadGate? held = null;
                if (skipTotal)
                {
                    WindowSeeks.Add(offset);
                    if (!query.ContainsKey("cursor")) FreshWindowSeeks.Add(offset);
                    if (heldReads.TryPeek(out var candidate) && (!candidate.FreshOnly || !query.ContainsKey("cursor")))
                        heldReads.TryDequeue(out held);
                    if (held is not null) { held.Offset = offset; held.Fresh = !query.ContainsKey("cursor"); }
                }
                var cursorScope = query.GetValueOrDefault("group", "") + ":" + ScopeVersion;
                var reply = Reply(new { items = Enumerable.Range(offset, count).Select(index => index + CatalogIndexShift).Select(index => new { content_id = "item-" + index, title = "Fixture " + index, type = ItemType == "mixed" ? "movie" : ItemType }),
                    total = OmitWindowTotals && skipTotal ? 0 : 1000, total_exact = !skipTotal, window_cursor = query["library_id"] + ":" + limit + ":0:" + cursorScope,
                    page = new { has_more = offset + count < 1000, next_cursor = query["library_id"] + ":" + limit + ":" + (offset + count) + ":" + cursorScope } });
                return CompleteCatalogAsync(held is null ? reply : CompleteCatalogWindowAsync(reply, ct, held), skipTotal, ct);
            }
            if (path.EndsWith("/layout") || path.EndsWith("/sections")) return Reply(new { sections = Array.Empty<object>() });
            if (path.EndsWith("/filters")) return Reply(new { genres = Array.Empty<string>() });
            return Reply(new { status = 404, detail = "No isolated fixture value" }, HttpStatusCode.NotFound);
        }
        private async Task<HttpResponseMessage> CompleteCatalogAsync(Task<HttpResponseMessage> reply, bool skipTotal, CancellationToken ct)
        {
            if (skipTotal && CatalogWindowGate is { } windowGate)
            {
                CatalogWindowStarted?.TrySetResult();
                await windowGate.Task.WaitAsync(ct);
            }
            else if (!skipTotal && CountGate is { } countGate)
                await countGate.Task.WaitAsync(ct);
            return await reply;
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
                using var cancellation = ct.Register(() => held.Cancelled = true);
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
