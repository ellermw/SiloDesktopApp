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
            await CollectionAsync(frame, wire, client);
            client.SetProfile("me");
            await PersonAsync(frame, wire, client);
        }
        finally
        {
            wire.RatingsGate?.TrySetResult();
            if (frame.Content is PersonDetailPage person) person.ViewModel.Cancel();
            frame.Content = null;
            // Cards unload while this fixture's services still resolve, not after the host disposes its own.
            window.Close(); await Task.Delay(300);
            field.SetValue(null, previous);
        }
    }

    private static async Task CollectionAsync(Frame frame, Wire wire, SiloApiClient client)
    {
        // The first items read starts while the ratings answer is still held.
        wire.RatingsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = new CollectionBrowsePage.NavArgs { CollectionId = "kept", Title = "Kept collection", IsUserCollection = true };
        frame.Navigate(typeof(CollectionBrowsePage), args);
        var page = (CollectionBrowsePage)frame.Content;
        var scroll = (ScrollViewer)page.FindName("ContentScroll");
        var skeleton = (FrameworkElement)page.FindName("LoadingSkeleton");
        await Until(() => wire.RatingsRequests > 0 && Items(page).Count >= 60 && scroll.Visibility == Visibility.Visible, "the collection's items waited for the ratings answer");
        Check(!wire.RatingsGate.Task.IsCompleted && skeleton.Visibility == Visibility.Collapsed, "cards must show while the ratings answer is held");
        var readsBeforeRatings = wire.CatalogReads("kept");
        wire.RatingsGate.SetResult();
        await Task.Delay(400);
        Check(wire.CatalogReads("kept") == readsBeforeRatings, "the ratings answer reread the collection although its sort is still offered");
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
        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "kept-3", null));
        await Task.Delay(100);
        frame.GoBack();
        await Until(() => frame.Content is CollectionBrowsePage { IsLoaded: true }, "Back did not return to the collection");
        await Until(() => wire.CatalogReads("kept") > reads && Items(page).Count == count && Items(page)[3].UserState?.Played == true && !Busy(page),
            "a change made while away did not reread the kept collection in place");
        await Task.Delay(150);
        Check(ReferenceEquals(frame.Content, page) && ReferenceEquals(Items(page)[0], first) && Math.Abs(scroll.VerticalOffset - offset) <= 1 && skeleton.Visibility == Visibility.Collapsed,
            $"the in-place reread must keep the cards and the offset: offset={scroll.VerticalOffset:0.#}/{offset:0.#}.");
        Program.Log("PASS: a title marked watched while away shows after Back; the collection rereads in place at the same offset.");

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

        // Another collection gets its own page.
        frame.Navigate(typeof(CollectionBrowsePage), new CollectionBrowsePage.NavArgs { CollectionId = "next", Title = "Next collection", IsUserCollection = true });
        await Until(() => frame.Content is CollectionBrowsePage { IsLoaded: true } next && !ReferenceEquals(next, other) && Items(next).Count > 0, "another collection reused the kept page");
        Check(Items((CollectionBrowsePage)frame.Content)[0].ContentId == "next-0", "another collection showed the kept collection's cards");
        Program.Log("PASS: another collection gets a new page.");
    }

    private static async Task PersonAsync(Frame frame, Wire wire, SiloApiClient client)
    {
        frame.Navigate(typeof(PersonDetailPage), "person-7");
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
        wire.Played.Add("film-3");
        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "film-3", null));
        await Task.Delay(100);
        frame.GoBack();
        await Until(() => frame.Content is PersonDetailPage { IsLoaded: true }, "Back did not return to the person");
        await Until(() => wire.FilmographyReads > filmographyReads && page.ViewModel.Filmography.Count == count && page.ViewModel.Filmography[3].UserState?.Played == true,
            "a change made while away did not reread the kept person in place");
        await Task.Delay(150);
        Check(ReferenceEquals(frame.Content, page) && ReferenceEquals(page.ViewModel.Filmography[0], first) && Math.Abs(scroll.VerticalOffset - offset) <= 1 && !page.ViewModel.IsLoading,
            $"the person's in-place reread must keep the cards and the offset: offset={scroll.VerticalOffset:0.#}/{offset:0.#}.");
        Program.Log("PASS: a title marked watched while away shows after Back; the person rereads in place at the same offset.");

        frame.Navigate(typeof(Page)); await Task.Delay(100);
        client.SetProfile("other");
        var before = wire.PersonReads;
        frame.GoBack();
        await Until(() => frame.Content is PersonDetailPage { IsLoaded: true } fresh && !ReferenceEquals(fresh, page), "another profile was handed the kept person");
        var other = (PersonDetailPage)frame.Content;
        await Until(() => other.ViewModel.Person != null && wire.PersonReads > before, "the new profile's person did not load");
        Program.Log("PASS: after a profile change Back loads the person fresh instead of showing the kept page.");
        other.ViewModel.Cancel();
    }

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
        internal readonly HashSet<string> Played = [];
        internal TaskCompletionSource? RatingsGate;
        internal int RatingsRequests, PersonReads, FilmographyReads;

        internal int CatalogReads(string collection) { lock (_catalogReads) return _catalogReads.GetValueOrDefault(collection); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "kept-pages.invalid") throw new InvalidOperationException("Kept pages fixture attempted external network.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/capabilities/ratings")
            {
                Interlocked.Increment(ref RatingsRequests);
                if (RatingsGate is { } gate) await gate.Task.WaitAsync(ct);
                return Reply(new { state = "available", sources = new[] { new { source = "imdb" } } });
            }
            if (path == "/api/v2/catalog/people/person-7")
            {
                Interlocked.Increment(ref PersonReads);
                return Reply(new { id = "person-7", name = "Kept Person", bio = "A complete synthetic biography.", birth_date = "1970-01-01",
                    photo_url = "https://kept-pages.invalid/missing-photo.png" });
            }
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2))
                    .ToDictionary(part => part[0], part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : "");
                var seek = query.TryGetValue("seek", out var seekText) ? int.Parse(seekText) : 0;
                var limit = int.Parse(query["limit"]);
                string prefix; int total;
                if (query.GetValueOrDefault("source") == "person") { Interlocked.Increment(ref FilmographyReads); prefix = "film-"; total = 200; }
                else
                {
                    var collection = query.GetValueOrDefault("collection_id", "none");
                    lock (_catalogReads) _catalogReads[collection] = _catalogReads.GetValueOrDefault(collection) + 1;
                    prefix = collection + "-"; total = 240;
                }
                var count = Math.Max(0, Math.Min(limit, total - seek));
                return Reply(new
                {
                    items = Enumerable.Range(seek, count).Select(index => new
                    {
                        content_id = prefix + index, title = "Synthetic title " + index, type = "movie", year = 1990 + index % 30,
                        user_state = new { played = Played.Contains(prefix + index) },
                    }),
                    total, total_exact = true, window_cursor = prefix + "window",
                    page = new { has_more = seek + count < total },
                });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"status\":404,\"detail\":\"No synthetic value\"}", System.Text.Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage Reply(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    }
}
