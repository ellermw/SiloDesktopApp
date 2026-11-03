using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Controls;

internal static partial class RetainedLibraryNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window, bool recoveryOnly = false, bool reviewOnly = false)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://retained-library.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture");
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "retained-images"));
        var navigation = new NavigationService();
        servicesField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "retained-settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings), [typeof(EventChannelClient)] = events,
            [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
            [typeof(ToastService)] = new ToastService(),
        }, () => new LibraryViewModel(catalog)));
        var frame = new Frame { Width = 1000, Height = 700, CacheSize = 2 };
        LibraryPage? page = null;
        var failures = new List<string>();
        try
        {
            if (reviewOnly) { await CheckReviewAsync(parent, navigation, wire, settings, auth); return; }
            if (recoveryOnly) { await CheckRecoveryAsync(); return; }
            parent.Children.Add(frame); navigation.Frame = frame;
            frame.Navigate(typeof(LibraryPage), new Library { Id = 11, Name = "Retained fixture", Type = "movie" });
            page = (LibraryPage)frame.Content;
            await Until(() => page.IsLoaded && page.ViewModel.GetWindowItem(0) is not null);
            await Task.Delay(750); // Let the existing save debounce settle before holding the return read.
            var scroll = (ScrollViewer)page.FindName("LibraryScroller");
            scroll.ChangeView(null, 400, null, true);
            await Until(() => scroll.VerticalOffset > 350);
            var offset = scroll.VerticalOffset;
            frame.Navigate(typeof(Page));
            await Until(() => !page.IsLoaded);
            var reads = wire.StateReads;
            var ratings = wire.RatingReads;
            catalog.InvalidateRatingCapability();
            wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            frame.GoBack();
            await Until(() => page.IsLoaded);
            if (!ReferenceEquals(page, frame.Content)) throw new InvalidOperationException("The fixture did not retain the actual LibraryPage.");
            await Task.Delay(150);
            if (wire.StateReads != reads)
                throw new InvalidOperationException("Valid retained Library Back blocked on a redundant held effective-settings restoration.");
            if (wire.RatingReads <= ratings || Math.Abs(scroll.VerticalOffset - offset) > 2)
                throw new InvalidOperationException("Retained return lost scroll position or skipped ordinary metadata revalidation.");
            if (Field<List<LibraryGridCard>>(page, "_libraryCardSlots").Count > 40 || !((Button)page.FindName("LibraryTab")).Focus(FocusState.Programmatic))
                throw new InvalidOperationException("Retained grid lost bounded realization or usable control focus.");
            wire.ReadGate.SetResult(); wire.ReadGate = null;
            void Click(string name) => ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName(name)).GetPattern(PatternInterface.Invoke)).Invoke();
            async Task Back()
            {
                frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
                frame.GoBack(); await Until(() => page.IsLoaded);
            }
            void Notify(string key = "ui.library_page_state") => settings.NotifyLocalizedSettingChanged(key);
            async Task Settled() { await Task.Delay(1100); await Until(() => !page.ViewModel.IsLoading && !page.ViewModel.IsCollectionsLoading); }
            Program.Log("PASS: cached actual Library skips held settings restoration, preserves position, and still revalidates ratings metadata.");

            wire.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click("CollectionsTab"); await Until(() => wire.PutStarted);
            ratings = wire.RatingReads; catalog.InvalidateRatingCapability();
            await Back(); await Task.Delay(120);
            if (wire.RatingReads <= ratings || Field<string>(page, "_currentTab") != "Collections")
                throw new InvalidOperationException("Pending local write blocked retained activation or restored older server state.");
            wire.PutGate.SetResult(); await Settled(); wire.PutGate = null;
            reads = wire.StateReads; var writes = wire.StateWrites;
            Notify(); Notify(); Notify();
            await Until(() => wire.StateReads > reads); await Task.Delay(150);
            if (Field<string>(page, "_currentTab") != "Collections" || wire.StateWrites != writes)
                throw new InvalidOperationException("Delayed own echo reapplied old state or created a write loop.");
            Program.Log("PASS: pending and accepted local write, burst/delayed echo and unchanged Back preserve latest tab without write loops.");

            wire.Search = "?tab=library"; Notify();
            await Until(() => Field<string>(page, "_currentTab") == "Library"); await Settled();
            Click("OpenFiltersButton");
            await Until(() => ((SlideSheet)page.FindName("FiltersSheet")).IsOpen);
            var editor = Field<QueryFilterEditor>(page, "_sharedFilterEditor");
            var year = Descendants<NumberBox>(editor).First();
            wire.Search = "?tab=recommended";
            wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously); reads = wire.StateReads;
            Notify(); await Until(() => wire.StateReads > reads);
            var editBefore = Field<long>(page, "_stateEdit");
            year.Value = 1999;
            await Until(() => Field<long>(page, "_stateEdit") > editBefore);
            wire.ReadGate.SetResult(); wire.ReadGate = null; await Task.Delay(100);
            if (year.Value != 1999 || !ReferenceEquals(editor, Field<QueryFilterEditor>(page, "_sharedFilterEditor")) || Field<string>(page, "_currentTab") != "Library")
                throw new InvalidOperationException("Held external refresh overwrote the active native filter draft.");
            ((SlideSheet)page.FindName("FiltersSheet")).IsOpen = false;
            await Settled();
            Program.Log("PASS: newer actual text draft wins before its filter debounce commits.");

            wire.Search = "?tab=recommended"; wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var obsoleteRead = wire.ReadGate; reads = wire.StateReads; Notify();
            await Until(() => wire.StateReads > reads);
            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            wire.ReadGate = null;
            frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = 11, Name = "Retained fixture", Type = "movie" }, "Collections"));
            try { await Until(() => page.IsLoaded && Field<string>(page, "_currentTab") == "Collections"); }
            finally { obsoleteRead.TrySetResult(); }
            await Task.Delay(150);
            if (Field<string>(page, "_currentTab") != "Collections") throw new InvalidOperationException("Older same-context visit overwrote explicit successor tab.");
            await Settled();
            Program.Log("PASS: same-context successor completes before ignored-cancellation obsolete read and keeps explicit tab.");

            wire.Remember = false; reads = wire.StateReads; Notify("ui.remember_library_page_state");
            await Until(() => wire.StateReads > reads); await Task.Delay(100);
            if (Field<string>(page, "_currentTab") != "Collections") throw new InvalidOperationException("Opt-out reset active user intent.");
            writes = wire.StateWrites;
            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            frame.Navigate(typeof(LibraryPage), new Library { Id = 11, Name = "Retained fixture", Type = "movie" });
            await Until(() => Field<string>(page, "_currentTab") == "Recommended"); await Settled();
            if (wire.StateWrites != writes) throw new InvalidOperationException("Opt-out activation wrote state.");
            wire.Remember = true; wire.Search = "?tab=library"; Notify("ui.remember_library_page_state");
            await Until(() => Field<string>(page, "_currentTab") == "Library"); await Settled();
            Program.Log("PASS: actual opt-out preserves current view, resets next activation and opt-in revalidates.");

            wire.CollectionsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var collections = wire.CollectionsReads; wire.Search = "?tab=collections"; Notify();
            await Until(() => wire.CollectionsReads > collections);
            wire.Search = "?tab=library"; Notify(); await Task.Delay(80);
            wire.CollectionsGate.SetResult(); wire.CollectionsGate = null;
            await Until(() => Field<string>(page, "_currentTab") == "Library");
            Program.Log("PASS: second external invalidation during held content reload is coalesced and applied.");

            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            wire.Search = "?tab=library"; Notify(); catalog.InvalidateRatingCapability();
            wire.RatingsGate = new(TaskCreationOptions.RunContinuationsAsynchronously); ratings = wire.RatingReads;
            frame.Navigate(typeof(LibraryPage), new Library { Id = 11, Name = "Retained fixture", Type = "movie" });
            await Until(() => wire.RatingReads > ratings);
            wire.Search = "?tab=collections"; Notify();
            await Until(() => Field<string>(page, "_currentTab") == "Collections");
            wire.RatingsGate.SetResult(); wire.RatingsGate = null; await Task.Delay(150);
            if (Field<string>(page, "_currentTab") != "Collections")
                throw new InvalidOperationException("Activation metadata continuation overwrote a newer authoritative settings refresh.");
            Program.Log("PASS: settings invalidation during held activation metadata retires the older restore.");

            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            wire.Search = "?tab=collections";
            client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
            frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = 11, Name = "Retained fixture", Type = "movie" }, "Library", "Crime"));
            await Until(() => page.IsLoaded && Field<string>(page, "_currentTab") == "Library" && page.ViewModel.SelectedGenres.Contains("Crime"));
            if (page.ViewModel.SelectedYearMin != null) throw new InvalidOperationException("Language retirement retained an obsolete query.");
            await Settled();
            Program.Log("PASS: detached language generation retires retained scope; explicit genre/tab wins.");

            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            wire.LastCatalogRequest = "";
            frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = 11, Name = "Retained fixture", Type = "movie" }, "Library", "Drama"));
            await Until(() => page.IsLoaded && page.ViewModel.SelectedGenres.Contains("Drama")); await Settled();
            if (!Uri.UnescapeDataString(wire.LastCatalogRequest).Contains("Drama", StringComparison.Ordinal))
                failures.Add("Same-scope explicit genre changed controls without reloading the actual catalog query.");
            else Program.Log("PASS: same-scope explicit genre changes actual catalog request.");

            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            wire.LibraryId = 22; wire.Search = "?tab=library&type=authors"; Notify();
            frame.Navigate(typeof(LibraryPage), new Library { Id = 22, Name = "Retained audiobooks", Type = "audiobook" });
            await Until(() => page.IsLoaded && Field<string>(page, "_currentAudiobookAxis") == "author" && !Field<bool>(page, "_isLoadingAudiobookGroups"));
            var search = (TextBox)page.FindName("AudiobookGroupSearchBox"); search.Text = "Keeper";
            await Settled();
            var groups = (ScrollViewer)page.FindName("AudiobookGroupsPanel");
            groups.ChangeView(null, 400, null, true); await Until(() => groups.VerticalOffset > 350);
            var groupOffset = groups.VerticalOffset; reads = wire.StateReads;
            wire.GroupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Back(); await Task.Delay(150);
            wire.GroupGate.SetResult(); wire.GroupGate = null; await Settled();
            if (wire.StateReads != reads || search.Text != "Keeper" || Field<string>(page, "_currentAudiobookAxis") != "author" || Math.Abs(groups.VerticalOffset - groupOffset) > 2)
                failures.Add($"Retained audiobook return lost author axis/search/position: reads {reads}/{wire.StateReads}, axis {Field<string>(page, "_currentAudiobookAxis")}, search {search.Text}, offset {groupOffset}/{groups.VerticalOffset}.");
            else Program.Log("PASS: actual retained audiobook group preserves axis/search/position while its metadata revalidates.");

            foreach (var change in new[] { "profile", "session", "access", "device", "library", "server" })
            {
                await Settled();
                wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.FailHeldRead = true;
                var retired = wire.ReadGate; reads = wire.StateReads; Notify(); await Until(() => wire.StateReads > reads);
                frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
                switch (change)
                {
                    case "profile": auth.SelectProfile("next-profile"); break;
                    case "session": client.ClearAuth(); break;
                    case "access": client.InvalidateAccessContext(); break;
                    case "device": client.SetDeviceMetadata("next-device", "Fixture device", "windows"); break;
                    case "library": wire.LibraryId = 33; break;
                    case "server": client.SetBaseUrl("https://retained-next.invalid"); break;
                }
                wire.ReadGate = null; wire.FailHeldRead = false; wire.Search = "?tab=library";
                var genre = "Scope " + change;
                frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = wire.LibraryId, Name = "Owner fixture", Type = "movie" }, "Library", genre));
                try { await Until(() => page.IsLoaded && page.ViewModel.SelectedGenres.Contains(genre)); }
                finally { retired.TrySetResult(); }
                await Task.Delay(120);
                if (!page.ViewModel.SelectedGenres.Contains(genre)) throw new InvalidOperationException("Retired owner response changed successor filters: " + change);
                var errors = Path.Combine(Program.ResultDirectory, "errors.txt");
                if (File.Exists(errors) && File.ReadAllText(errors).Contains("Retired synthetic settings failure", StringComparison.Ordinal))
                    throw new InvalidOperationException("Retired settings failure emitted a toast: " + change);
                Program.Log("PASS: actual " + change + " retirement admits successor before held failure and suppresses stale notification.");
            }
            await Settled();
            wire.ErrorMessage = "Current synthetic settings failure";
            wire.FailHeldRead = true; wire.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            reads = wire.StateReads; Notify(); await Until(() => wire.StateReads > reads);
            wire.ReadGate.SetResult(); wire.ReadGate = null; wire.FailHeldRead = false; await Task.Delay(150);
            writes = wire.StateWrites; Click("CollectionsTab"); await Settled();
            if (wire.StateWrites == writes) failures.Add("A transient restore failure was treated as a known remember=false opt-out and suppressed the next user save.");
            else Program.Log("PASS: current restore failure preserves input and does not invent an opt-out.");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
            frame.Navigate(typeof(Page)); await Until(() => !page.IsLoaded);
            await CheckRecoveryAsync();
            await CheckReviewAsync(parent, navigation, wire, settings, auth);
        }
        finally
        {
            wire.ReadGate?.TrySetResult();
            wire.PutGate?.TrySetResult(); wire.CollectionsGate?.TrySetResult();
            wire.RatingsGate?.TrySetResult(); wire.GroupGate?.TrySetResult();
            await Task.Delay(200);
            var unloaded = new TaskCompletionSource();
            if (page is { IsLoaded: true }) page.Unloaded += (_, _) => unloaded.TrySetResult();
            else unloaded.TrySetResult();
            frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
            await unloaded.Task;
            await Task.Delay(150);
            navigation.Frame = null; servicesField.SetValue(null, previous);
        }

        async Task CheckRecoveryAsync()
        {
            foreach (var search in new[] { "failure", "?tab=library&groups[999999999999999999][match]=all", "?tab=library&groups[0][rules][0][field]=year&groups[0][rules][0][value][999999999999999999]=2000" })
            {
                wire.LibraryId++; wire.Search = search; wire.FailHeldRead = search == "failure";
                wire.ErrorMessage = "Current synthetic settings failure";
                settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                var layouts = wire.LayoutReads;
                var freshFrame = new Frame { Width = 1000, Height = 700 };
                parent.Children.Add(freshFrame); navigation.Frame = freshFrame;
                LibraryPage? fresh = null;
                try
                {
                    freshFrame.Navigate(typeof(LibraryPage), new Library { Id = wire.LibraryId, Name = "Recovery fixture", Type = "movie" });
                    fresh = (LibraryPage)freshFrame.Content;
                    await Until(() => fresh.IsLoaded && wire.LayoutReads > layouts && !Field<bool>(fresh, "_recommendationsLoading"));
                    if (Field<string>(fresh, "_currentTab") != "Recommended") throw new InvalidOperationException("Failed first restore did not load usable default recommendations.");
                    wire.FailHeldRead = false; wire.Search = "?tab=library";
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)fresh.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Until(() => fresh.ViewModel.GetWindowItem(0) != null);
                    Program.Log("PASS: fresh " + (search == "failure" ? "settings HTTP failure" : "malformed saved query") + " loads defaults and allows native catalog browsing.");
                    if (search == "failure")
                    {
                        await Task.Delay(650);
                        wire.Search = "?tab=collections&groups[999999999999999999][match]=all";
                        var readsBefore = wire.StateReads; settings.NotifyLocalizedSettingChanged("ui.library_page_state");
                        await Until(() => wire.StateReads > readsBefore); await Task.Delay(150);
                        if (Field<string>(fresh, "_currentTab") != "Library") throw new InvalidOperationException("Malformed realtime state reset the active view.");
                        Program.Log("PASS: malformed realtime saved query preserves current native browsing.");
                    }
                }
                finally
                {
                    freshFrame.Navigate(typeof(Page)); parent.Children.Remove(freshFrame);
                    if (fresh != null) await Until(() => !fresh.IsLoaded);
                    await Task.Delay(100);
                }
            }
        }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 250; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Retained Library control did not settle."); }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int StateReads, StateWrites, RatingReads, CollectionsReads, LayoutReads, CatalogReads, LibraryId = 11;
        internal bool Remember = true, PutStarted, FailHeldRead, FailPut;
        internal string Search = "?tab=library";
        internal string LastCatalogRequest = "", ErrorMessage = "Retired synthetic settings failure";
        internal string PutErrorMessage = "Synthetic save failure";
        internal TaskCompletionSource? ReadGate, PutGate, CollectionsGate, RatingsGate, GroupGate, CatalogGate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host is not ("retained-library.invalid" or "retained-next.invalid")) throw new InvalidOperationException("External fixture request.");
            var path = request.RequestUri.AbsolutePath;
            object body = new { items = Array.Empty<object>() };
            if (path.EndsWith("/settings/values/effective"))
            {
                var search = Search; var remember = Remember; var failure = FailHeldRead; var message = ErrorMessage;
                if (request.RequestUri.Query.Contains("ui.library_page_state"))
                {
                    StateReads++;
                    if (ReadGate is { } gate) await gate.Task;
                    if (failure) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(JsonSerializer.Serialize(new { message })) };
                }
                body = new { items = new object[] { new { key = "ui.remember_library_page_state", value = (object)remember },
                    new { key = "ui.library_page_state", value = (object)new { version = 1, libraries = new Dictionary<string, object> { [LibraryId.ToString()] = new { search } } } } } };
            }
            else if (path.EndsWith("/ui.library_page_state") && request.Method == HttpMethod.Put)
            {
                var failure = FailPut; var message = PutErrorMessage;
                StateWrites++; PutStarted = true; if (PutGate is { } gate) await gate.Task;
                if (failure) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(JsonSerializer.Serialize(new { message })) };
                var payload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
                Search = payload.GetProperty("value").GetProperty("libraries").GetProperty(LibraryId.ToString()).GetProperty("search").GetString()!;
            }
            else if (path == "/api/v2/capabilities/ratings") { RatingReads++; if (RatingsGate is { } gate) await gate.Task; body = new { state = "available", sources = Array.Empty<object>() }; }
            else if (path == "/api/v2/catalog/audiobook-groups") { if (GroupGate is { } gate) await gate.Task; body = new { items = Enumerable.Range(0, 40).Select(i => new { name = "Keeper " + i, item_count = 1, poster_urls = Array.Empty<string>() }), total = 40, page = new { has_more = false } }; }
            else if (path.Contains("/collections")) { CollectionsReads++; if (CollectionsGate is { } gate) await gate.Task; body = new { collections = Array.Empty<object>() }; }
            else if (path is "/api/v2/catalog" or "/api/v2/catalog/query")
            {
                LastCatalogRequest = request.RequestUri.Query + (request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
                CatalogReads++;
                if (CatalogGate is { } gate) await gate.Task;
                body = new { items = Enumerable.Range(0, 30).Select(i => new { content_id = "item-" + i, title = "Fixture " + i, type = "movie" }), total = 30, page = new { has_more = false } };
            }
            else if (path.EndsWith("/layout") || path.EndsWith("/sections")) { LayoutReads++; body = new { sections = Array.Empty<object>() }; }
            else if (path.EndsWith("/filters")) body = new { genres = new[] { "Drama", "Crime" } };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
