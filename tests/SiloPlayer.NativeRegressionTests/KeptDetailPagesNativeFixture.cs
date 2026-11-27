using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Back to a collection or a person keeps the actual page as it was left (same cards, same scroll
/// offset, no reread) while nothing changed; rereads it in place after a title is marked watched
/// elsewhere; and never hands it to another profile. A collection's first items read starts
/// without waiting for the ratings capability. Synthetic server only; writes
/// kept-collection-back-1440.png and kept-person-back-1440.png.
/// Dispatch: SILO_NATIVE_TEST_ONLY=kept-detail-pages.
/// </summary>
internal static class KeptDetailPagesNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync()
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://kept-pages.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("me", profile: new() { Id = "me", Name = "Fixture", IsPrimary = true });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client);
        var people = new PeopleApi(client); var presentation = new UICustomizationService(settings); var overlays = new CardOverlayService(settings);
        var localSettings = new SettingsService(Path.Combine(Program.ResultDirectory, "kept-pages-settings"));
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "kept-pages-images"));
        using var events = new EventChannelClient(client, auth);
        var prefetch = new ItemDetailPrefetchCache(catalog, auth); var toast = new ToastService();
        var frame = new Frame(); var owner = new Grid(); owner.Children.Add(frame);
        var navigation = new NavigationService { Frame = frame };
        var window = new Window { Content = owner };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth, [typeof(AuthApi)] = () => new AuthApi(client),
            [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections, [typeof(SettingsApi)] = () => settings,
            [typeof(PeopleApi)] = () => people, [typeof(NavigationService)] = () => navigation, [typeof(ToastService)] = () => toast,
            [typeof(SettingsService)] = () => localSettings, [typeof(UICustomizationService)] = () => presentation,
            [typeof(CardOverlayService)] = () => overlays, [typeof(EventChannelClient)] = () => events, [typeof(ImageService)] = () => images,
            [typeof(HttpClient)] = () => http, [typeof(ItemDetailPrefetchCache)] = () => prefetch,
            [typeof(PersonDetailViewModel)] = () => new PersonDetailViewModel(people, catalog, client),
        }));
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); window.AppWindow.Show(false);
            await Until(() => frame.ActualWidth > 0, "the fixture window did not lay out");
            await CollectionAsync(frame, wire, client, collections);
            await UnavailableSavedSortAsync(frame, wire, catalog);
            client.SetProfile("me");
            await PersonAsync(frame, navigation, wire, client);
        }
        finally
        {
            wire.RatingsGate?.TrySetResult();
            wire.HeaderGate?.TrySetResult();
            wire.CatalogGate?.TrySetResult();
            wire.SortSaveGate?.TrySetResult();
            wire.SyncGate?.TrySetResult();
            if (frame.Content is PersonDetailPage person) person.ViewModel.Cancel();
            frame.Content = null;
            // Cards unload while this fixture's services still resolve, not after the host disposes its own.
            window.Close(); await Task.Delay(300);
            field.SetValue(null, previous);
        }
    }

    private static async Task CollectionAsync(Frame frame, Wire wire, SiloApiClient client, CollectionsApi collections)
    {
        // The first items read starts while the ratings answer is still held.
        wire.RatingsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.HeaderGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = new CollectionBrowsePage.NavArgs { CollectionId = "kept", Title = "Kept collection", IsUserCollection = true };
        frame.Navigate(typeof(CollectionBrowsePage), args);
        var page = (CollectionBrowsePage)frame.Content;
        var scroll = (ScrollViewer)page.FindName("ContentScroll");
        var skeleton = (FrameworkElement)page.FindName("LoadingSkeleton");
        await Until(() => wire.RatingsRequests > 0 && Items(page).Count >= 60 && scroll.Visibility == Visibility.Visible, "the collection's items waited for the ratings answer");
        Check(!wire.RatingsGate.Task.IsCompleted && skeleton.Visibility == Visibility.Collapsed, "cards must show while the ratings answer is held");
        var readsBeforeRatings = wire.CatalogReads("kept");
        await Until(() => wire.HeaderReads > 0, "the independent header request did not start");
        frame.Navigate(typeof(Page));
        wire.RatingsGate.SetResult();
        frame.GoBack();
        wire.HeaderGate.SetResult();
        await Until(() => wire.HeaderReads >= 2 && HasSort(page, "rating_rt_critic")
            && Visible(page, "CollectionEditButton"), "Back did not restore the interrupted owner header and rating capability");
        Check(ReferenceEquals(frame.Content, page) && wire.CatalogReads("kept") == readsBeforeRatings,
            "restoring the header and ratings must not reread an unchanged catalog order");
        Program.Log("PASS: a collection's first items read starts at once; the late ratings answer does not reread it.");

        // Back keeps the cards and the scroll offset, with no reread.
        await Until(() => scroll.ScrollableHeight > 1400, "the collection did not grow tall enough to scroll");
        scroll.ChangeView(null, 1200, null, true);
        await Until(() => Math.Abs(scroll.VerticalOffset - 1200) < 1, "the collection did not scroll");
        await Until(() => !Busy(page), "the collection's next page did not settle");
        await Task.Delay(200);
        var offset = scroll.VerticalOffset; var count = Items(page).Count; var reads = wire.CatalogReads("kept"); var first = Items(page)[0];
        frame.Navigate(typeof(Page)); await Task.Delay(150);
        frame.GoBack();
        await Until(() => frame.Content is CollectionBrowsePage { IsLoaded: true }, "Back did not return to the collection");
        await Task.Delay(250);
        Check(ReferenceEquals(frame.Content, page) && wire.CatalogReads("kept") == reads && Items(page).Count == count && ReferenceEquals(Items(page)[0], first)
            && Math.Abs(scroll.VerticalOffset - offset) <= 1 && skeleton.Visibility == Visibility.Collapsed && scroll.Visibility == Visibility.Visible,
            $"Back to a collection must keep it as it was: same={ReferenceEquals(frame.Content, page)}, reads={wire.CatalogReads("kept") - reads}, items={Items(page).Count}/{count}, offset={scroll.VerticalOffset:0.#}/{offset:0.#}.");
        await MediaParityNativeFixture.CaptureAsync(page, "kept-collection-back-1440.png");
        Program.Log($"PASS: Back to a collection keeps the page, {count} cards and offset {offset:0} without a reread.");

        // A title marked watched elsewhere while the page was away shows after Back, rereading in place.
        frame.Navigate(typeof(Page)); await Task.Delay(100);
        wire.Played.Add("kept-3");
        using var realtime = new RealtimeMediaStateRefresh(client, action => { page.DispatcherQueue.TryEnqueue(() => action()); });
        SendWatched(realtime, "kept-3");
        frame.GoBack();
        await Until(() => frame.Content is CollectionBrowsePage { IsLoaded: true }, "Back did not return to the collection");
        await Until(() => wire.CatalogReads("kept") > reads && Items(page).Count == count && Items(page)[3].UserState?.Played == true && !Busy(page),
            "a change made while away did not reread the kept collection in place");
        await Task.Delay(150);
        Check(ReferenceEquals(frame.Content, page) && ReferenceEquals(Items(page)[0], first) && Math.Abs(scroll.VerticalOffset - offset) <= 1 && skeleton.Visibility == Visibility.Collapsed,
            $"the in-place reread must keep the cards and the offset: offset={scroll.VerticalOffset:0.#}/{offset:0.#}.");
        Program.Log("PASS: a title marked watched while away shows after Back; the collection rereads in place at the same offset.");

        // A rejected snapshot is replaced once without clearing the retained rows.
        var beforeCursor = wire.CatalogReads("kept");
        wire.RejectKeptCursor = true;
        await (Task)typeof(CollectionBrowsePage).GetMethod("LoadMoreAsync", Private)!.Invoke(page, null)!;
        Check(!wire.RejectKeptCursor && wire.CatalogReads("kept") >= beforeCursor + 2 && ReferenceEquals(Items(page)[0], first),
            "a rejected paging cursor did not reopen the mounted collection window");

        // Sync started by the previous visit must still refresh if it settles after Back.
        wire.SyncGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var hero = typeof(CollectionBrowsePage).GetField("_hero", Private)!.GetValue(page);
        var sync = (Task)typeof(CollectionBrowsePage).GetMethod("SyncAsync", Private)!.Invoke(page, [hero])!;
        await Until(() => wire.SyncRequests > 0, "sync did not start");
        frame.Navigate(typeof(Page));
        frame.GoBack();
        wire.SyncGate.SetResult();
        await sync;
        Check(ReferenceEquals(frame.Content, page) && Items(page)[4].ContentId == "kept-synced", "sync from the previous visit did not refresh membership");

        // Another profile never gets the kept page.
        frame.Navigate(typeof(Page)); await Task.Delay(100);
        client.SetProfile("other");
        var beforeIdentity = wire.CatalogReads("kept");
        frame.GoBack();
        await Until(() => frame.Content is CollectionBrowsePage { IsLoaded: true } fresh && !ReferenceEquals(fresh, page), "another profile was handed the kept collection");
        var other = (CollectionBrowsePage)frame.Content;
        await Until(() => Items(other).Count > 0 && wire.CatalogReads("kept") > beforeIdentity, "the new profile's collection did not load");
        Check(((ScrollViewer)other.FindName("ContentScroll")).VerticalOffset < 1, "a fresh collection page must start at the top");
        Program.Log("PASS: after a profile change Back loads the collection fresh instead of showing the kept page.");

        // A save finishing while away must not clear the kept cards or leave Back on a skeleton.
        wire.SortSaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var sort = (ComboBox)other.FindName("SortCombo");
        sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "title"));
        await Until(() => wire.SortSaves > 0, "the collection sort save did not start");
        frame.Navigate(typeof(Page));
        wire.SortSaveGate.SetResult();
        frame.GoBack();
        await Until(() => wire.LastSort("kept") == "title" && Items(other).Count > 0 && !Busy(other), "Back did not load the accepted sort");
        Check(ReferenceEquals(frame.Content, other) && !Visible(other, "LoadingSkeleton"), "a sort save replaced the retained collection or left a skeleton");

        frame.Navigate(typeof(Page));
        wire.DeniedHeader = "kept";
        SendWatched(realtime, "kept-4");
        frame.GoBack();
        await Until(() => Visible(other, "ErrorText"), "personal collection resource denial did not settle");
        Check(Items(other).Count == 0 && !other.HoldsEntry(args) && !Visible(other, "ContentScroll") && !Visible(other, "CollectionEditButton"),
            "a denied personal header left collection data or actions visible");

        // Another collection gets its own page.
        var nextArgs = new CollectionBrowsePage.NavArgs { CollectionId = "next", Title = "Next collection", IsUserCollection = true };
        frame.Navigate(typeof(CollectionBrowsePage), nextArgs);
        await Until(() => frame.Content is CollectionBrowsePage { IsLoaded: true } next && !ReferenceEquals(next, other) && Items(next).Count > 0, "another collection reused the kept page");
        var nextPage = (CollectionBrowsePage)frame.Content;
        Check(Items(nextPage)[0].ContentId == "next-0", "another collection showed the kept collection's cards");
        Program.Log("PASS: another collection gets a new page.");

        frame.Navigate(typeof(Page));
        await collections.UpdateCollectionAsync("next", new() { IsShared = false });
        wire.DeniedCatalog = "next";
        Check(!nextPage.HoldsEntry(nextArgs), "a known sharing change did not retire the entry before Back");
        frame.GoBack();
        await Until(() => frame.Content is CollectionBrowsePage denied && !ReferenceEquals(denied, nextPage) && Visible(denied, "ErrorText"),
            "a sharing change replayed the old entry or the catalog refusal was not shown");
    }

    private static async Task UnavailableSavedSortAsync(Frame frame, Wire wire, CatalogApi catalog)
    {
        wire.RatingSource = "imdb";
        foreach (var ratingsFirst in new[] { true, false })
        {
            catalog.InvalidateRatingCapability();
            var id = ratingsFirst ? "saved-early" : "saved-late";
            var ratingsBefore = wire.RatingsRequests;
            wire.RatingsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.CatalogGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!ratingsFirst) wire.CatalogGate.SetResult();
            frame.Navigate(typeof(CollectionBrowsePage), new CollectionBrowsePage.NavArgs { CollectionId = id, Title = id, IsUserCollection = true });
            var page = (CollectionBrowsePage)frame.Content;
            await Until(() => wire.RatingsRequests > ratingsBefore && wire.CatalogReads(id) > 0, "both independent reads did not start");
            if (!ratingsFirst)
                await Until(() => Items(page).Count > 0 && HasSort(page, "rating_rt_critic"), "late-capability setup did not adopt the provisional sort");
            wire.RatingsGate.SetResult();
            if (ratingsFirst)
            {
                await Until(() => (bool)typeof(CollectionBrowsePage).GetField("_ratingSourcesKnown", Private)!.GetValue(page)!, "the early capability did not apply");
                wire.CatalogGate.SetResult();
            }
            await Until(() => wire.LastSort(id) == "title" && Items(page).Count > 0 && !Busy(page), "unavailable saved sort did not reread with an explicit fallback");
            Check(SelectedTag(page, "SortCombo") == "title" && SelectedTag(page, "OrderCombo") == "asc"
                && ((ComboBox)page.FindName("OrderCombo")).IsEnabled && !HasSort(page, "rating_rt_critic"), "fallback controls do not match the request");
        }
        Program.Log("PASS: an unavailable saved rating sort falls back to explicit ascending Title in both response orders.");
    }

    private static async Task PersonAsync(Frame frame, NavigationService navigation, Wire wire, SiloApiClient client)
    {
        navigation.Navigate<PersonDetailPage>("person-7");
        var page = (PersonDetailPage)frame.Content;
        var scroll = (ScrollViewer)page.FindName("ContentScroll");
        await Until(() => page.ViewModel.Person != null && !page.ViewModel.IsLoadingFilmography && page.ViewModel.Filmography.Count >= 60 && scroll.Visibility == Visibility.Visible, "the person did not load");
        await Until(() => scroll.ScrollableHeight > 1400, "the person page did not grow tall enough to scroll");
        scroll.ChangeView(null, 1200, null, true);
        await Until(() => Math.Abs(scroll.VerticalOffset - 1200) < 1, "the person page did not scroll");
        await Until(() => !page.ViewModel.IsLoadingMoreFilmography, "the filmography's next page did not settle");
        await Task.Delay(200);
        var offset = scroll.VerticalOffset; var count = page.ViewModel.Filmography.Count; var first = page.ViewModel.Filmography[0];
        var personReads = wire.PersonReads; var filmographyReads = wire.FilmographyReads;
        frame.Navigate(typeof(Page)); await Task.Delay(150);
        frame.GoBack();
        await Until(() => frame.Content is PersonDetailPage { IsLoaded: true }, "Back did not return to the person");
        await Task.Delay(250);
        Check(ReferenceEquals(frame.Content, page) && wire.PersonReads == personReads && wire.FilmographyReads == filmographyReads
            && page.ViewModel.Filmography.Count == count && ReferenceEquals(page.ViewModel.Filmography[0], first) && Math.Abs(scroll.VerticalOffset - offset) <= 1 && scroll.Visibility == Visibility.Visible,
            $"Back to a person must keep the page as it was: same={ReferenceEquals(frame.Content, page)}, reads={wire.PersonReads - personReads}/{wire.FilmographyReads - filmographyReads}, items={page.ViewModel.Filmography.Count}/{count}, offset={scroll.VerticalOffset:0.#}/{offset:0.#}.");
        await MediaParityNativeFixture.CaptureAsync(page, "kept-person-back-1440.png");
        Program.Log($"PASS: Back to a person keeps the page, {count} filmography cards and offset {offset:0} without a reread.");

        frame.Navigate(typeof(Page)); await Task.Delay(100);
        using var realtime = new RealtimeMediaStateRefresh(client, action => { page.DispatcherQueue.TryEnqueue(() => action()); });
        wire.FailFilmography = true;
        SendWatched(realtime, "film-3");
        frame.GoBack();
        await Until(() => wire.FilmographyReads > filmographyReads
            && typeof(PersonDetailViewModel).GetField("_keptRefresh", Private)!.GetValue(page.ViewModel) == null,
            "remote watched change did not trigger a filmography reread");
        Check(ReferenceEquals(frame.Content, page) && ReferenceEquals(page.ViewModel.Filmography[0], first), "transient failure discarded the person window");

        frame.Navigate(typeof(Page));
        wire.FailFilmography = false;
        wire.Played.Add("film-3");
        filmographyReads = wire.FilmographyReads;
        frame.GoBack();
        await Until(() => frame.Content is PersonDetailPage { IsLoaded: true }, "Back did not return to the person");
        await Until(() => wire.FilmographyReads > filmographyReads && page.ViewModel.Filmography.Count == count && page.ViewModel.Filmography[3].UserState?.Played == true,
            "a change made while away did not reread the kept person in place");
        await Task.Delay(150);
        Check(ReferenceEquals(frame.Content, page) && ReferenceEquals(page.ViewModel.Filmography[0], first) && Math.Abs(scroll.VerticalOffset - offset) <= 1 && !page.ViewModel.IsLoading,
            $"the person's in-place reread must keep the cards and the offset: offset={scroll.VerticalOffset:0.#}/{offset:0.#}.");
        Program.Log("PASS: stale person refresh retries after a transient failure and keeps the scroll offset.");

        frame.Navigate(typeof(Page));
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WeakReferenceMessenger.Default.Send(new PlaybackProgressUpdated("film-5", 100, 100, true) { Persisted = persisted.Task });
        var readsWhileWriting = wire.FilmographyReads;
        frame.GoBack();
        await Task.Delay(200);
        Check(wire.FilmographyReads == readsWhileWriting && ReferenceEquals(frame.Content, page)
            && Visible(page, "ContentScroll"), "Back read filmography before the final playback write settled");
        wire.Played.Add("film-5");
        persisted.SetResult();
        await Until(() => page.ViewModel.Filmography[5].UserState?.Played == true, "filmography did not reflect the settled playback write");

        frame.Navigate(typeof(Page)); await Task.Delay(100);
        client.SetProfile("other");
        var before = wire.PersonReads;
        frame.GoBack();
        await Until(() => frame.Content is PersonDetailPage { IsLoaded: true } fresh && !ReferenceEquals(fresh, page), "another profile was handed the kept person");
        var other = (PersonDetailPage)frame.Content;
        await Until(() => other.ViewModel.Person != null && wire.PersonReads > before, "the new profile's person did not load");
        Program.Log("PASS: after a profile change Back loads the person fresh instead of showing the kept page.");

        // A fresh Back reattaches the delayed observer without an immediate filmography reread.
        await Until(() => Kept(other), "the new-profile window did not finish");
        frame.Navigate(typeof(Page));
        wire.PersonName = "Updated metadata";
        var beforeObserve = wire.FilmographyReads;
        frame.GoBack();
        await Until(() => other.ViewModel.Person?.Name == wire.PersonName, "fresh Back did not restart metadata observation");
        Check(ReferenceEquals(frame.Content, other) && wire.FilmographyReads == beforeObserve, "metadata observation unnecessarily reloaded filmography");

        // A second visit to the same ID must not be replayed for an older history entry.
        frame.Navigate(typeof(Page));
        navigation.Navigate<PersonDetailPage>("person-7");
        var newer = (PersonDetailPage)frame.Content;
        await Until(() => newer.ViewModel.FilmographyWindowLoaded, "the second person visit did not load");
        await newer.ViewModel.FilterCommand.ExecuteAsync("movie");
        frame.GoBack();
        frame.GoBack();
        await Until(() => frame.Content is PersonDetailPage earlier && Kept(earlier),
            "Back to the older person visit did not load");
        var earlierPage = (PersonDetailPage)frame.Content;
        Check(!ReferenceEquals(earlierPage, newer) && earlierPage.ViewModel.SelectedTypeFilter == "all",
            "the older person history entry replayed the newer visit's filter");

        frame.Navigate(typeof(Page));
        wire.PersonStatus = HttpStatusCode.NotFound;
        SendWatched(realtime, "film-6");
        frame.GoBack();
        await Until(() => Visible(earlierPage, "PersonUnavailableState"), "authoritative person 404 did not show unavailable");
        Check(earlierPage.ViewModel.Person == null && earlierPage.ViewModel.Filmography.Count == 0 && !Visible(earlierPage, "PersonActions"),
            "a removed person retained protected data or actions");

        frame.Navigate(typeof(Page));
        wire.PersonStatus = HttpStatusCode.ServiceUnavailable;
        navigation.Navigate<PersonDetailPage>("person-7");
        var retry = (PersonDetailPage)frame.Content;
        await Until(() => Visible(retry, "PersonRetryButton") && Visible(retry, "PersonUnavailableState"), "initial person failure did not offer Retry");
        wire.PersonStatus = null;
        typeof(PersonDetailPage).GetMethod("RetryPerson_Click", Private)!.Invoke(retry, [retry, null]);
        await Until(() => Kept(retry) && Visible(retry, "ContentScroll"), "Retry did not recover the person page");
        frame.Navigate(typeof(Page));
        Check(retry.HoldsEntry(frame.BackStack.Last().Parameter), "successful Retry was not recorded for Back");
        frame.GoBack();
        Check(ReferenceEquals(frame.Content, retry), "Back discarded the successfully retried page");
    }

    private static void SendWatched(RealtimeMediaStateRefresh realtime, string id) => realtime.ReceiveRealtime("user_state", "user_state.changed",
        JsonSerializer.SerializeToElement(new { change = "watched", content_id = id }));
    private static bool Kept(PersonDetailPage page) => page.HoldsEntry(typeof(PersonDetailPage).GetField("_entry", Private)!.GetValue(page));
    private static bool Visible(FrameworkElement page, string name) => ((FrameworkElement)page.FindName(name)).Visibility == Visibility.Visible;
    private static bool HasSort(CollectionBrowsePage page, string tag) =>
        ((ComboBox)page.FindName("SortCombo")).Items.OfType<ComboBoxItem>().Any(item => Equals(item.Tag, tag));
    private static string? SelectedTag(CollectionBrowsePage page, string name) =>
        (((ComboBox)page.FindName(name)).SelectedItem as ComboBoxItem)?.Tag as string;

    private static ObservableCollection<MediaItem> Items(CollectionBrowsePage page) =>
        (ObservableCollection<MediaItem>)typeof(CollectionBrowsePage).GetField("_items", Private)!.GetValue(page)!;
    private static bool Busy(CollectionBrowsePage page) => (bool)typeof(CollectionBrowsePage).GetField("_isLoadingMore", Private)!.GetValue(page)!;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Kept detail pages: " + message); }
    private static async Task Until(Func<bool> ready, string message)
    {
        for (var attempt = 0; attempt < 240; attempt++) { if (ready()) return; await Task.Delay(25); }
        if (!ready()) throw new TimeoutException("Kept detail pages: " + message);
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider
    { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _catalogReads = [];
        private readonly Dictionary<string, string?> _lastSort = [];
        internal readonly HashSet<string> Played = [];
        internal TaskCompletionSource? RatingsGate, HeaderGate, CatalogGate, SortSaveGate, SyncGate;
        internal int RatingsRequests, HeaderReads, SortSaves, SyncRequests, PersonReads, FilmographyReads;
        internal string RatingSource = "rt_critic", PersonName = "Kept Person";
        internal string? DeniedHeader, DeniedCatalog;
        internal bool FailFilmography, RejectKeptCursor;
        private bool _synced;
        internal HttpStatusCode? PersonStatus;

        internal int CatalogReads(string collection) { lock (_catalogReads) return _catalogReads.GetValueOrDefault(collection); }
        internal string? LastSort(string collection) { lock (_catalogReads) return _lastSort.GetValueOrDefault(collection); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "kept-pages.invalid") throw new InvalidOperationException("Kept pages fixture attempted external network.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/capabilities/ratings")
            {
                Interlocked.Increment(ref RatingsRequests);
                if (RatingsGate is { } gate) await gate.Task.WaitAsync(ct);
                return Reply(new { state = "available", sources = new[] { new { source = RatingSource } } });
            }
            if (path == "/api/v2/collections/sort-preference")
            {
                Interlocked.Increment(ref SortSaves);
                if (SortSaveGate is { } gate) await gate.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/api/v2/collections/kept/sync")
            {
                Interlocked.Increment(ref SyncRequests);
                if (SyncGate is { } gate) await gate.Task.WaitAsync(ct);
                _synced = true;
                return Reply(new { status = "success", items_matched = 1 });
            }
            if (path == "/api/v2/collections/capabilities") return Reply(new { imports = true });
            if (path.StartsWith("/api/v2/collections/", StringComparison.Ordinal))
            {
                var id = path["/api/v2/collections/".Length..];
                if (id == "kept")
                {
                    Interlocked.Increment(ref HeaderReads);
                    if (HeaderGate is { } gate) await gate.Task.WaitAsync(ct);
                }
                if (id == DeniedHeader) return Problem(HttpStatusCode.Forbidden);
                var response = Reply(new { id, name = id, description = "A successful collection header", collection_type = id == "kept" ? "tmdb" : "manual",
                    creator_profile_id = id == "next" ? "another" : "me", is_shared = request.Method == HttpMethod.Get });
                response.Headers.TryAddWithoutValidation("ETag", "\"fixture\"");
                return response;
            }
            if (path == "/api/v2/catalog/people/person-7")
            {
                Interlocked.Increment(ref PersonReads);
                if (PersonStatus is { } status) return Problem(status);
                return Reply(new { id = "person-7", name = PersonName, bio = "A complete synthetic biography.", birth_date = "1970-01-01",
                    photo_url = "https://kept-pages.invalid/missing-photo.png" });
            }
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2))
                    .ToDictionary(part => part[0], part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : "");
                var seek = query.TryGetValue("seek", out var seekText) ? int.Parse(seekText) : 0;
                var limit = int.Parse(query["limit"]);
                string prefix; int total;
                var sort = query.GetValueOrDefault("sort");
                if (query.GetValueOrDefault("source") == "person")
                {
                    Interlocked.Increment(ref FilmographyReads);
                    if (FailFilmography) return Problem(HttpStatusCode.ServiceUnavailable);
                    prefix = "film-"; total = 200;
                }
                else
                {
                    var collection = query.GetValueOrDefault("collection_id", "none");
                    lock (_catalogReads)
                    {
                        _catalogReads[collection] = _catalogReads.GetValueOrDefault(collection) + 1;
                        _lastSort[collection] = sort;
                    }
                    if (collection == DeniedCatalog) return Problem(HttpStatusCode.Forbidden);
                    if (collection == "kept" && RejectKeptCursor && seek > 0 && query.ContainsKey("cursor"))
                    {
                        RejectKeptCursor = false;
                        return Problem(HttpStatusCode.BadRequest, "invalid_cursor");
                    }
                    if (collection.StartsWith("saved-", StringComparison.Ordinal))
                    {
                        if (CatalogGate is { } gate) await gate.Task.WaitAsync(ct);
                        sort ??= "-rating_rt_critic";
                    }
                    prefix = collection + "-"; total = 240;
                }
                var count = Math.Max(0, Math.Min(limit, total - seek));
                return Reply(new
                {
                    items = Enumerable.Range(seek, count).Select(index => new
                    {
                        content_id = _synced && prefix == "kept-" && index == 4 ? "kept-synced" : prefix + index,
                        title = "Synthetic title " + index, type = "movie", year = 1990 + index % 30,
                        user_state = new { played = Played.Contains(prefix + index) },
                    }),
                    total, total_exact = true, window_cursor = prefix + "window",
                    effective_sort = sort == null ? null : new { field = sort.TrimStart('-'), order = sort.StartsWith('-') ? "desc" : "asc" },
                    page = new { has_more = seek + count < total },
                });
            }
            return Problem(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Problem(HttpStatusCode status, string code = "synthetic") => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { type = "https://kept-pages.invalid/problems/" + code,
                status = (int)status, detail = "Synthetic error" }), System.Text.Encoding.UTF8, "application/problem+json"),
        };

        private static HttpResponseMessage Reply(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    }
}
