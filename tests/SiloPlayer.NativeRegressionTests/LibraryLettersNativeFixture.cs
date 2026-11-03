using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class LibraryLettersNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window, bool grouped = false)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire { Grouped = grouped }; using var http = new HttpClient(wire);
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
        try
        {
            foreach (var (type, width, height) in (grouped ? new[] { ("movie", 1280, 1000), ("mixed", 960, 550) } : new[] { ("movie", 1280, 1000), ("audiobook", 960, 550) }))
            {
                wire.Grouped = grouped; wire.CurrentType = type; wire.CurrentLibraryId = wire.LibraryId;
                wire.LetterIndexGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                window.AppWindow.ResizeClient(new(width, height));
                frame = new Frame { Width = width, Height = height }; parent.Children.Add(frame); navigation.Frame = frame;
                frame.Navigate(typeof(LibraryPage), new Library { Id = wire.LibraryId++, Name = "Fixture library", Type = type, BoxSetsEnabled = grouped });
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
                if (grouped)
                {
                    wire.RejectNextCursor = true;
                    await page.ViewModel.LoadWindowAsync(300, 60, force: true);
                    await page.ViewModel.EnsureLetterIndexAsync();
                    if (wire.MissingGroup != 0 || wire.InvalidCursors != wire.ExpectedRetiredCursors || page.ViewModel.GetWindowItem(300)?.BoxSet is null)
                        throw new InvalidOperationException("Grouped invalid-cursor recovery lost its aggregate/count/index scope.");
                    Program.Log("PASS: actual grouped cursor retirement keeps recovery/count/index and following letter scopes.");
                }
                ((IInvokeProvider)new ButtonAutomationPeer(Letter("M")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => scroll.VerticalOffset > 10000 && page.ViewModel.GetWindowItem(600) is not null);
                if (!wire.Seeks.Contains(600) || wire.InvalidCursors != wire.ExpectedRetiredCursors || page.ViewModel.ErrorMessage is not null)
                    throw new InvalidOperationException("Letter activation did not seek to its exact catalog position.");
                if (grouped)
                {
                    if (wire.MissingGroup != 0) throw new InvalidOperationException("Grouped browse/index omitted group=box_set.");
                    await Until(() => Descendants(page).OfType<SiloPlayer.Controls.BoxSetCard>().Any());
                    var aggregate = Descendants(page).OfType<SiloPlayer.Controls.BoxSetCard>().First();
                    if (!aggregate.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Grouped library aggregate is not keyboard accessible.");
                    if (Descendants(page).OfType<SiloPlayer.Controls.LibraryGridCard>().Count() > 200) throw new InvalidOperationException("Grouped virtual slots grew without bound.");
                    wire.PlayedCount = 2;
                    page.Receive(new SiloPlayer.Messaging.MediaSurfaceChanged(SiloPlayer.Messaging.MediaSurfaceChangeKind.WatchedMarked, "member", context: client.CaptureContext()));
                    await Until(() => page.ViewModel.GetWindowItem(600)?.BoxSet?.PlayedCount == 2);
                    var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    page.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("member", 100, 100, true) { Persisted = persisted.Task });
                    wire.PlayedCount = 3; persisted.TrySetResult();
                    await Until(() => page.ViewModel.GetWindowItem(600)?.BoxSet?.PlayedCount == 3);
                    page.ViewModel.SelectedGenre = "Drama";
                    var orderControl = (ComboBox)page.FindName("OrderComboBox"); orderControl.SelectedIndex = 0;
                    await Until(() => page.ViewModel.SelectedOrder == "desc" && !page.ViewModel.IsLoading && page.ViewModel.LetterIndex is not null);
                    await page.ViewModel.EnsureLetterIndexAsync(); Letter("M").Focus(FocusState.Keyboard);
                    await InvokeLetterAsync(Letter("M"), page.ViewModel);
                    await Until(() => scroll.VerticalOffset > 10000 && page.ViewModel.GetWindowItem(600) is not null && !page.ViewModel.IsLetterJumpInProgress);
                    if (wire.LastOrder != "-title" || wire.LastGenre != "Drama" || wire.LastIndexOrder != "desc") throw new InvalidOperationException("Grouped filtered reverse jump changed query scope.");
                    try { await Until(() => ((Queue<int>)typeof(LibraryPage).GetField("_pendingCardBinds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).Count == 0 &&
                        Descendants(page).OfType<SiloPlayer.Controls.LibraryGridCard>().All(card => card.MediaItem is null || card.Tag is not int index || ReferenceEquals(card.MediaItem, page.ViewModel.GetWindowItem(index))) &&
                        Descendants(page).OfType<SiloPlayer.Controls.BoxSetCard>().Any() &&
                        Descendants(page).OfType<SiloPlayer.Controls.BoxSetCard>().All(card => card.Summary?.PlayedCount == 3) &&
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("M")) == "Current letter"); }
                    catch
                    {
                        var pending = (Queue<int>)typeof(LibraryPage).GetField("_pendingCardBinds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                        var stale = Descendants(page).OfType<SiloPlayer.Controls.LibraryGridCard>().Count(card => card.MediaItem is not null && card.Tag is int index && !ReferenceEquals(card.MediaItem, page.ViewModel.GetWindowItem(index)));
                        var counts = string.Join(",", Descendants(page).OfType<SiloPlayer.Controls.BoxSetCard>().Select(card => card.Summary?.PlayedCount));
                        Program.Log($"TRACE settled width={width} pending={pending.Count} stale={stale} counts={counts} M={Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("M"))} offset={scroll.VerticalOffset} internal={typeof(LibraryPage).GetField("_libraryVerticalOffset", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)} row={typeof(LibraryPage).GetField("_currentFirstRow", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)} window={page.ViewModel.WindowStartIndex}..{page.ViewModel.WindowEndIndex}");
                        await GroupedLibraryCardsNativeFixture.CaptureAsync(page, $"settled-diagnostic-{width}.png");
                        throw;
                    }
                    await GroupedLibraryCardsNativeFixture.CaptureAsync(page, $"grouped-library-{width}x{height}.png");
                    orderControl.SelectedIndex = 1;
                    await Until(() => page.ViewModel.SelectedOrder == "asc" && !page.ViewModel.IsLoading && page.ViewModel.LetterIndex is not null);
                    await page.ViewModel.EnsureLetterIndexAsync(); Letter("M").Focus(FocusState.Keyboard);
                    await InvokeLetterAsync(Letter("M"), page.ViewModel);
                    await Until(() => scroll.VerticalOffset > 10000 && page.ViewModel.GetWindowItem(600) is not null && !page.ViewModel.IsLetterJumpInProgress);
                    Program.Log("PASS: grouped far-letter jump uses aggregate native cards, exact scoped requests, reverse filters, watched/final-write refresh and keyboard focus.");
                }
                var layout = typeof(LibraryPage).GetMethod("GetGridLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                var columns = (int)layout.GetType().GetProperty("Columns")!.GetValue(layout)!;
                var rowHeight = (double)layout.GetType().GetProperty("RowHeight")!.GetValue(layout)!;
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
                    Program.Log($"TRACE bottom group={grouped} offset={scroll.VerticalOffset} extent={scroll.ScrollableHeight} viewport={scroll.ViewportHeight} Z={Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(Letter("Z"))} total={page.ViewModel.TotalCount} window={page.ViewModel.WindowStartIndex}..{page.ViewModel.WindowEndIndex} jump={page.ViewModel.IsLetterJumpInProgress} error={page.ViewModel.ErrorMessage}");
                    await GroupedLibraryCardsNativeFixture.CaptureAsync(page, $"bottom-diagnostic-{grouped}-{width}.png");
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
                if (grouped)
                {
                    await VerifyGroupedRefreshViewportAsync(page, wire, client);
                    wire.Grouped = false; catalog.InvalidateLibraryCache();
                    await Until(() => page.ViewModel.Library is { BoxSetsEnabled: false } && page.ViewModel.GetWindowItem(0)?.Type == "movie");
                    await Until(() => !Descendants(page).OfType<SiloPlayer.Controls.BoxSetCard>().Any());
                    Program.Log("PASS: library disable retires grouped windows/cards/index and reloads ordinary catalog.");
                }
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RecommendedTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => rail.Visibility == Visibility.Collapsed);
                Program.Log($"PASS: actual {type} Library alphabet rail, exact distant jump, bottom selection, availability, accessible counts, visibility, failed-index gutter recovery and bounds at {width}x{height}.");
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame); await Task.Delay(100); frame = null;
            }
        }
        finally
        {
            wire.LetterIndexGate?.TrySetResult();
            wire.CatalogWindowGate?.TrySetResult(); wire.CountGate?.TrySetResult();
            frame?.Navigate(typeof(Page)); if (frame is not null) parent.Children.Remove(frame);
            await Task.Delay(150); navigation.Frame = null; field.SetValue(null, previous);
        }
    }
    private static async Task VerifyGroupedRefreshViewportAsync(LibraryPage page, Wire wire, SiloApiClient client)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var model = page.ViewModel;
        var scroll = (ScrollViewer)page.FindName("LibraryScroller");
        wire.OmitWindowTotals = true;
        model.SelectedSort = "year"; // No alphabet index can repair a collapsed estimate.
        scroll.ChangeView(null, 0, null, true);
        await model.LoadCommand.ExecuteAsync(null);
        await Until(() => model.DisplayTotalCount == 1000 && !model.IsLoading && scroll.VerticalOffset < 1);
        // Build the same contiguous window as successive nearby viewport requests.
        for (var start = 60; start <= 780; start += 60)
            await model.EnsureRangeLoadedAsync(start, start + 59);
        if (model.WindowStartIndex != 0 || model.WindowEndIndex < 839)
            throw new InvalidOperationException("Grouped refresh regression requires a merged window starting behind the viewport.");
        var layout = typeof(LibraryPage).GetMethod("GetGridLayout", flags)!.Invoke(page, null)!;
        var columns = (int)layout.GetType().GetProperty("Columns")!.GetValue(layout)!;
        var rowHeight = (double)layout.GetType().GetProperty("RowHeight")!.GetValue(layout)!;
        for (var index = 60; index <= 720; index += 60)
        {
            var offset = (index / columns) * rowHeight;
            scroll.ChangeView(null, offset, null, true);
            await Until(() => Math.Abs(scroll.VerticalOffset - offset) < 1);
        }
        var range = ((int StartIndex, int EndIndex))typeof(LibraryPage).GetMethod("GetVisibleItemRange", flags)!.Invoke(page, [layout])!;
        range = ((int, int))typeof(LibraryPage).GetMethod("ClampRealizedRange", flags)!.Invoke(page, [range, layout])!;
        var cards = (Dictionary<int, SiloPlayer.Controls.LibraryGridCard>)typeof(LibraryPage).GetField("_visibleLibraryCards", flags)!.GetValue(page)!;
        var indices = Enumerable.Range(range.StartIndex, range.EndIndex - range.StartIndex + 1).ToArray();
        await Until(() => indices.All(index => cards.TryGetValue(index, out var card) &&
            card.MediaItem is not null && ReferenceEquals(card.MediaItem, model.GetWindowItem(index)) && card.IsHitTestVisible));
        if (model.WindowStartIndex != 0 || range.StartIndex < 600)
            throw new InvalidOperationException("Grouped viewport must remain far below the merged window start.");
        var oldCards = indices.ToDictionary(index => index, index => cards[index]);
        var oldItems = indices.ToDictionary(index => index, index => cards[index].MediaItem);
        var savedOffset = scroll.VerticalOffset;
        wire.PlayedCount = 2;
        wire.CatalogWindowStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.CatalogWindowGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.CountGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            page.Receive(new SiloPlayer.Messaging.MediaSurfaceChanged(SiloPlayer.Messaging.MediaSurfaceChangeKind.WatchedMarked, "member", context: client.CaptureContext()));
            await wire.CatalogWindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(150); // Let the reset's queued native render run while the response is held.
            if (model.TotalCount != 1000 || model.DisplayTotalCount != 1000 || Math.Abs(scroll.VerticalOffset - savedOffset) >= 1 ||
                indices.Any(index => !ReferenceEquals(cards[index], oldCards[index]) ||
                    !ReferenceEquals(cards[index].MediaItem, oldItems[index]) || !cards[index].IsHitTestVisible))
                throw new InvalidOperationException("Grouped refresh unmounted visible cards or changed the extent/offset before replacement.");
            wire.CatalogWindowGate.TrySetResult();
            await Until(() => !model.IsLoading && indices.All(index => model.GetWindowItem(index) is { } item &&
                !ReferenceEquals(item, oldItems[index]) && cards.TryGetValue(index, out var card) &&
                ReferenceEquals(card.MediaItem, item) && card.IsHitTestVisible && (item.BoxSet is null || item.BoxSet.PlayedCount == 2)));
            if (model.TotalCount != 1000 || model.DisplayTotalCount != 1000 || Math.Abs(scroll.VerticalOffset - savedOffset) >= 1)
                throw new InvalidOperationException("A no-total grouped refresh shrank the canvas or lost the viewport before the exact count.");
            Program.Log("PASS: merged grouped window refresh repopulates the visible range in place, keeps mounted cards while held, and preserves extent/offset without index or count assistance.");
        }
        finally
        {
            wire.CatalogWindowGate.TrySetResult(); wire.CountGate.TrySetResult();
            wire.CatalogWindowGate = null; wire.CountGate = null; wire.OmitWindowTotals = false;
        }
    }

    private static async Task InvokeLetterAsync(Button button, LibraryViewModel model)
    {
        var jump = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Jumped(int _) => jump.TrySetResult();
        model.LetterJumpRequested += Jumped;
        try
        {
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await jump.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // The page applies that model event on its dispatcher, then WinUI settles ChangeView.
            await Task.Delay(150);
        }
        finally { model.LetterJumpRequested -= Jumped; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++) { var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 300; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Library letter native state did not settle."); }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int InvalidCursors, ExpectedRetiredCursors, MissingGroup, LibraryId = 11;
        internal bool RejectNextCursor;
        internal bool Grouped;
        internal int CurrentLibraryId, PlayedCount = 1;
        internal string CurrentType = "movie";
        internal string? LastOrder, LastGenre, LastIndexOrder;
        internal bool FailIndex, OmitWindowTotals;
        internal TaskCompletionSource? LetterIndexGate, CatalogWindowGate, CountGate, CatalogWindowStarted;
        internal List<int> Seeks { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/user/libraries") return Reply(new { items = new[] { new { id = CurrentLibraryId, type = CurrentType, enabled = true, box_sets_enabled = Grouped } } });
            if (path == "/api/v2/catalog/search/capabilities") return Reply(new { letter_index = true, box_sets = Grouped });
            if (path == "/api/v2/catalog/letters")
            {
                if (Grouped) { using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult()); LastIndexOrder = body.RootElement.GetProperty("order").GetString(); }
                if (Grouped && !request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult().Contains("\"group\":\"box_set\"")) MissingGroup++;
                return CompleteLetterIndexAsync(Reply(new
            {
                total = 1000,
                letters = new[] { new { letter = "#", count = 20, position = 0 }, new { letter = "A", count = 679, position = 20 }, new { letter = "M", count = 300, position = 600 }, new { letter = "Z", count = 1, position = 999 } },
                runs = new[] { new { letter = "#", position = 0 }, new { letter = "A", position = 20 }, new { letter = "M", position = 600 }, new { letter = "A", position = 900 }, new { letter = "Z", position = 999 } },
            }), ct);
            }
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                LastOrder = query.GetValueOrDefault("sort"); LastGenre = query.GetValueOrDefault("genre");
                if (Grouped && query.GetValueOrDefault("group") != "box_set") MissingGroup++;
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
                    if (scope.Length != 4 || scope[0] != query["library_id"] || scope[1] != query["limit"] || scope[3] != query.GetValueOrDefault("group", ""))
                    {
                        InvalidCursors++;
                        return Reply(new { status = 400, type = "https://fixture.invalid/docs/api/v2/problems/invalid_cursor", detail = "The cursor is malformed, tampered with, or belongs to a different query." }, HttpStatusCode.BadRequest);
                    }
                    if (!query.ContainsKey("seek")) offset = int.Parse(scope[2]);
                }
                var count = Math.Min(limit, 1000 - offset);
                var skipTotal = query.ContainsKey("skip_total");
                return CompleteCatalogAsync(Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = "item-" + index, title = "Fixture " + index, type = Grouped && index % 2 == 0 ? "box_set" : "movie", box_set = Grouped && index % 2 == 0 ? new { id = index.ToString(), library_id = query["library_id"], title = "Box set " + index, movie_count = 3, played_count = PlayedCount } : null }),
                    total = OmitWindowTotals && skipTotal ? 0 : 1000, total_exact = !skipTotal, window_cursor = query["library_id"] + ":" + limit + ":0:" + query.GetValueOrDefault("group", ""),
                    page = new { has_more = offset + count < 1000, next_cursor = query["library_id"] + ":" + limit + ":" + (offset + count) + ":" + query.GetValueOrDefault("group", "") } }), skipTotal, ct);
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
        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
    }
}
