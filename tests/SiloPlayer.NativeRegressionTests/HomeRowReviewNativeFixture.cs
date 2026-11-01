using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class HomeRowReviewNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var failures = new List<string>();
        async Task Case(string name, Func<Task> run)
        {
            try { await run(); Program.Log("PASS: review " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Program.Log("FAIL: review " + name + ": " + ex); }
        }
        await Case("queued-profile-headers", () => QueuedOwnerAsync("profile"));
        await Case("queued-server-headers", () => QueuedOwnerAsync("server"));
        await Case("queued-account-headers", () => QueuedOwnerAsync("account"));
        await Case("queued-token-refresh-same-owner", () => QueuedOwnerAsync("token-refresh"));
        await Case("null-initial-mounted-row", () => NullRowAsync(parent, false));
        await Case("null-whole-mounted-hero", () => NullRowAsync(parent, true));
        await Case("queued-row-ui-collection-ownership", QueuedUiOwnershipAsync);
        await Case("promoted-hero-whole", PromotedHeroAsync);
        await Case("featured-configured-budget-with-capabilities", FeaturedReadsKeepConfiguredBudgetAsync);
        await Case("expanded-refresh-with-capabilities", () => ExpandedRefreshWithCapabilitiesAsync(50));
        await Case("expanded-refresh-below-card-count", () => ExpandedRefreshWithCapabilitiesAsync(20));
        await Case("secondary-featured-completion-after-refresh", SecondaryFeaturedCompletionAfterRefreshAsync);
        await Case("expanded-hero-round-trip", ExpandedHeroRoundTripAsync);
        await Case("expanded-hero-below-card-count", ExpandedHeroBelowCardCountAsync);
        await Case("library-row-without-completion-consumer", () => LibraryRowWithoutCompletionConsumerAsync(parent));
        await Case("translated-prefix-and-tail", LanguageAsync);
        await Case("real-menu-undo-vs-refresh", () => MenuAsync(parent, true));
        await Case("real-menu-series-tail-removal", () => MenuAsync(parent, false));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task NullRowAsync(StackPanel parent, bool hero)
    {
        using var session = new Session(new Wire { NullItems = true, Hero = hero });
        var page = new HomePage { Width = 900, Height = 700 }; parent.Children.Add(page);
        try
        {
            await Until(() => session.Vm.FeaturedSections.Concat(session.Vm.Sections).FirstOrDefault()?.LoadCompleted == true);
            var row = session.Vm.FeaturedSections.Concat(session.Vm.Sections).Single();
            Require(!row.LoadFailed && row.Id == "row" && row.Title == "Progress", "Null item JSON must preserve the successful mounted layout slot.");
        }
        finally { parent.Children.Remove(page); }
    }

    private static async Task QueuedUiOwnershipAsync()
    {
        using var session = new Session(new Wire { Batch = true });
        await session.Vm.LoadCommand.ExecuteAsync(null); await session.Wire.FirstFive.Task.WaitAsync(TimeSpan.FromSeconds(4));
        // Guard the real collections' backing stores, without replacing the VM or
        // its validity predicate. This deterministically detects off-dispatcher
        // enumeration at the real queued continuation rather than relying on a race.
        var guards = new[] { session.Vm.FeaturedSections, session.Vm.Sections }.Select(collection =>
        {
            var field = typeof(System.Collections.ObjectModel.Collection<HomeSectionWithItems>)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == typeof(IList<HomeSectionWithItems>));
            var guard = new UiOwnedList((IList<HomeSectionWithItems>)field.GetValue(collection)!, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            field.SetValue(collection, guard); return guard;
        }).ToArray();
        await Task.Run(() => session.Wire.ReleaseFirst.TrySetResult());
        await Until(() => session.Vm.Sections.All(row => row.LoadCompleted));
        Require(guards.Sum(g => g.WorkerReads) == 0, "Queued validity check enumerated UI-owned collections from a worker.");
        Require(session.Vm.Sections.Single(row => row.Id == "row6").Items.Count == 20, "Healthy queued row must complete.");
    }

    private sealed class UiOwnedList(IList<HomeSectionWithItems> inner, Microsoft.UI.Dispatching.DispatcherQueue dispatcher) : IList<HomeSectionWithItems>
    {
        public int WorkerReads;
        public IEnumerator<HomeSectionWithItems> GetEnumerator()
        {
            if (!dispatcher.HasThreadAccess) { Interlocked.Increment(ref WorkerReads); throw new InvalidOperationException("UI collection enumerated from a worker."); }
            return inner.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public HomeSectionWithItems this[int index] { get => inner[index]; set => inner[index] = value; }
        public int Count => inner.Count; public bool IsReadOnly => inner.IsReadOnly;
        public void Add(HomeSectionWithItems item) => inner.Add(item);
        public void Clear() => inner.Clear(); public bool Contains(HomeSectionWithItems item) => inner.Contains(item);
        public void CopyTo(HomeSectionWithItems[] array, int index) => inner.CopyTo(array, index);
        public int IndexOf(HomeSectionWithItems item) => inner.IndexOf(item);
        public void Insert(int index, HomeSectionWithItems item) => inner.Insert(index, item);
        public bool Remove(HomeSectionWithItems item) => inner.Remove(item); public void RemoveAt(int index) => inner.RemoveAt(index);
    }

    private static async Task QueuedOwnerAsync(string change)
    {
        using var session = new Session(new Wire { Batch = true });
        var wire = session.Wire; var vm = session.Vm;
        await vm.LoadCommand.ExecuteAsync(null); await wire.FirstFive.Task.WaitAsync(TimeSpan.FromSeconds(4));
        vm.SetActive(false);
        if (change == "server") session.Client.SetBaseUrl("https://replacement.invalid");
        else if (change == "account")
        {
            session.Auth.SetTokens("replacement-fixture-account", "synthetic-refresh", 0);
            session.Auth.SelectProfile("alpha");
        }
        else if (change == "token-refresh") session.Client.SetAccessToken("replacement-fixture-account");
        else session.Auth.SelectProfile("beta");
        wire.ReleaseFirst.TrySetResult(); await Task.Delay(250);
        var requests = wire.Requests.ToArray();
        Require(requests.Take(5).All(r => r.Host == "home-review.invalid" && r.Profile == "alpha" && r.Account == "initial-fixture"), "Initial row requests must carry the initiating host/profile/account headers.");
        if (change == "token-refresh")
        {
            Require(requests.Any(r => r.Id == "row6" && r.Account == "replacement-fixture"), "Same-account token refresh must not retire valid queued reads.");
            return;
        }
        Require(!requests.Any(r => r.Id == "row6"), "Retired layout row6 was requested: " + string.Join(",", requests.Select(r => $"{r.Id}@{r.Host}/{r.Profile}/{r.Account}")));
    }

    private static async Task PromotedHeroAsync()
    {
        using var session = new Session(new Wire { Capabilities = "{\"row_item_limit_max\":100,\"section_item_limit_max\":100}" });
        var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
        wire.Hero = true; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => wire.Requests.Count >= 2); await Task.Delay(100);
        Require(vm.FeaturedSections.Count == 1 && vm.FeaturedSections[0].Featured && vm.FeaturedSections[0].Items.Count == 35,
            "Promoted first-featured row must recover35 cards; count=" + vm.FeaturedSections.FirstOrDefault()?.Items.Count);
        Require(wire.Requests.Last().Query == "", "Promoted hero must issue an uncapped read.");
    }

    private static async Task FeaturedReadsKeepConfiguredBudgetAsync()
    {
        using var session = new Session(new Wire { Hero = true, SectionCount = 2, ConfiguredItemLimit = 10,
            Capabilities = "{\"row_item_limit_max\":100,\"section_item_limit_max\":100}" });
        var vm = session.Vm; var wire = session.Wire;
        void CheckRows()
        {
            Require(vm.FeaturedSections.Count == 1 && vm.Sections.Count == 1,
                "The first featured section must be the hero; the additional featured section must remain a row.");
            Require(vm.FeaturedSections.Concat(vm.Sections).All(row => row.Featured && row.ItemLimit == 10 && row.Items.Count == 10),
                "Advertised section maxima must not inflate either featured section's configured ten-card budget.");
            Require(wire.Requests.All(read => read.Query == ""), "Featured initial, retry and refresh reads must be uncapped.");
        }
        await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.FeaturedSections.Concat(vm.Sections).Count(row => row.LoadCompleted) == 2);
        CheckRows();
        await vm.RetrySectionAsync("row1"); await vm.RetrySectionAsync("row2");
        CheckRows();
        wire.Title = "Featured refresh"; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.FeaturedSections.Concat(vm.Sections).All(row => row.Title == wire.Title));
        CheckRows();
        Require(wire.Requests.Count == 6, "Each featured section must be read once per initial, retry and refresh cycle.");
    }

    private static async Task ExpandedRefreshWithCapabilitiesAsync(int configuredLimit)
    {
        using var session = new Session(new Wire { ConfiguredItemLimit = configuredLimit,
            Capabilities = "{\"row_item_limit_max\":100,\"section_item_limit_max\":100}" });
        var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
        await vm.CompleteSectionAsync("row");
        var order = vm.Sections[0].CompletedOrder;
        var expectedCount = Math.Min(35, configuredLimit);
        Require(vm.Sections[0].Items.Count == expectedCount && order > 0 && !vm.Sections[0].CanComplete,
            "Explicit uncapped completion must recover the configured row and exhaust further demand.");
        wire.Title = "Expanded refresh"; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.Sections.FirstOrDefault()?.Title == wire.Title);
        Require(vm.Sections[0].Items.Count == expectedCount && vm.Sections[0].Items.Last().ContentId == $"movie{expectedCount}",
            "Expanded refresh must recover the configured row without an initial query cap.");
        await vm.RetrySectionAsync("row");
        Require(vm.Sections[0].Items.Count == expectedCount && vm.Sections[0].ItemLimit == configuredLimit
            && vm.Sections[0].ShownCount == 20 && vm.Sections[0].CompletedOrder == order,
            "Expanded refresh and retry must keep configured membership and initial/tail priority.");
        Require(wire.Requests.Select(read => read.Query).SequenceEqual(new[] { "?item_limit=20", "", "", "" }),
            "Expanded refresh and retry must reuse explicit completion's uncapped read.");
    }

    private static async Task SecondaryFeaturedCompletionAfterRefreshAsync()
    {
        using var session = new Session(new Wire { Hero = true, SectionCount = 2, ConfiguredItemLimit = 20,
            Capabilities = "{\"row_item_limit_max\":100,\"section_item_limit_max\":100}" });
        var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.FeaturedSections.Concat(vm.Sections).Count(row => row.LoadCompleted) == 2);
        Require(!vm.Sections.Single().CanComplete && vm.Sections[0].HasMore && vm.Sections[0].HasReadWhole,
            "An already uncapped secondary featured row must not offer an identical completion query.");
        var reads = wire.Requests.Count;
        await vm.CompleteSectionAsync("row2");
        var order = vm.Sections[0].CompletedOrder;
        Require(vm.Sections[0].Items.Count == 20 && order == 0 && wire.Requests.Count == reads,
            "Featured tail demand must not repeat an already uncapped read.");
        wire.ConfiguredItemLimit = 35;
        wire.Title = "Featured partial refresh"; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.FeaturedSections.Concat(vm.Sections).All(row => row.Title == wire.Title));
        Require(vm.Sections[0].Items.Count == 35 && !vm.Sections[0].CanComplete && vm.Sections[0].HasReadWhole,
            "An expanded secondary featured refresh must retain all35 cards rather than restore the display budget.");
        await vm.RetrySectionAsync("row2");
        Require(vm.Sections[0].Items.Count == 35 && !vm.Sections[0].CanComplete && vm.Sections[0].HasReadWhole,
            "Expanded secondary featured retry must retain every recovered card without another completion gesture.");
        await vm.CompleteSectionAsync("row2");
        Require(vm.Sections[0].Items.Count == 35 && vm.Sections[0].Items.Last().ContentId == "movie35"
            && vm.Sections[0].ShownCount == null && vm.Sections[0].CompletedOrder == order,
            "All35 cards must remain available after a secondary featured refresh and retry.");
        Require(wire.Requests.Where(read => read.Id == "row2").Select(read => read.Query)
            .SequenceEqual(new[] { "", "", "" }),
            "Only refresh and retry may reissue the uncapped featured resource.");
        Require(vm.FeaturedSections.Single().Items.Count == 35, "The hero refresh must also adopt the new configured budget.");
    }

    private static async Task ExpandedHeroRoundTripAsync()
    {
        using var session = new Session(new Wire()); var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
        await vm.CompleteSectionAsync("row");
        var order = vm.Sections[0].CompletedOrder;
        Require(vm.Sections[0].ShownCount == 20 && order > 0, "Fixture must start with a completed bounded row.");

        wire.Hero = true; wire.Title = "Featured refresh";
        vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.FeaturedSections.FirstOrDefault()?.Title == wire.Title);
        var featured = vm.FeaturedSections.Single();
        Require(featured.Items.Count == 35 && featured.ShownCount == 20 && featured.CompletedOrder == order,
            "Promotion must preserve the expanded row's initial/tail boundary and completion order.");
        var later = new HomeSectionWithItems { Id = "later", SectionType = "continue_watching",
            Items = new() { new() { ContentId = "episode21" } } };
        var projected = HomeSectionDedupe.Project([new() { Id = "row", Featured = true }, new() { Id = "later" }],
            new Dictionary<string, HomeSectionWithItems> { ["row"] = featured, ["later"] = later },
            new HashSet<string> { "continue_watching" });
        Require(projected["later"].Items.Count == 1 && projected["row"].Items.All(item => item.ContentId != "episode21"),
            "A featured expansion tail must still yield to a later row's initial cards.");

        wire.Hero = false; wire.Title = "Demoted refresh";
        vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.Sections.FirstOrDefault()?.Title == wire.Title);
        var row = vm.Sections.Single();
        Require(vm.FeaturedSections.Count == 0 && row.Items.Count == 35 && row.ShownCount == 20 && row.CompletedOrder == order,
            "Demotion must keep the previously expanded row whole and retain its priority boundary.");
        Require(wire.Requests.Count == 4 && wire.Requests.Skip(1).All(read => read.Query == ""),
            "Completion, promotion and demotion must each use one uncapped read.");
    }

    private static async Task ExpandedHeroBelowCardCountAsync()
    {
        using var session = new Session(new Wire { ConfiguredItemLimit = 20,
            Capabilities = "{\"row_item_limit_max\":100,\"section_item_limit_max\":100}" });
        var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
        await vm.CompleteSectionAsync("row");
        var order = vm.Sections[0].CompletedOrder;
        Require(vm.Sections[0].Items.Count == 20 && order > 0 && !vm.Sections[0].CanComplete,
            "Fixture must exhaust an unchanged uncapped completion at its configured20-card budget.");

        wire.Hero = true; wire.Title = "Expanded hero refresh";
        vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => vm.FeaturedSections.FirstOrDefault()?.Title == wire.Title);
        await vm.RetrySectionAsync("row");
        var hero = vm.FeaturedSections.Single();
        Require(hero.Featured && hero.Items.Count == 20 && hero.Items.Last().ContentId == "movie20"
            && hero.ItemLimit >= hero.Items.Count && hero.ShownCount == 20 && hero.CompletedOrder == order && !hero.CanComplete,
            "Promotion and retry must preserve the configured row and exhausted demand state.");
        Require(wire.Requests.Select(read => read.Query).SequenceEqual(
            new[] { "?item_limit=20", "", "", "" }),
            "Cached expansion must keep reads uncapped independently of featured placement.");
    }

    private static async Task LibraryRowWithoutCompletionConsumerAsync(StackPanel parent)
    {
        using var session = new Session(new Wire());
        var page = new LibraryPage();
        var section = new HomeSectionWithItems { Id = "recommended", Title = "Recommended", SectionType = "recommended",
            LoadCompleted = true, HasMore = true, Items = new(Enumerable.Range(1, 20).Select(i => new MediaItem {
                ContentId = $"library-{i}", Title = $"Card{i}", Type = "movie" })) };
        var row = (SectionRow)typeof(LibraryPage).GetMethod("CreateLibrarySectionRow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [section, 11])!;
        row.Width = 900;
        parent.Children.Add(row);
        try
        {
            await Task.Delay(120); row.UpdateLayout();
            var scroll = (ScrollViewer)row.FindName("CardsScrollViewer");
            var next = (Button)row.FindName("ScrollRightBtn");
            Require(row.OnNearEnd == null && scroll.ScrollableWidth > 0 && next.IsEnabled,
                "Library caller must retain ordinary navigation through its loaded cards without a completion consumer.");
            var reads = session.Wire.Requests.Count;
            scroll.ChangeView(scroll.ScrollableWidth, null, null, true); await Task.Delay(100);
            Require(!next.IsEnabled && ((UIElement)row.FindName("RightFadeGradient")).Opacity == 0,
                "Library's truncated shared model must not advertise forward completion at the loaded tail.");
            typeof(SectionRow).GetMethod("ScrollByPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(row, [1]);
            await Task.Delay(100);
            Require(session.Wire.Requests.Count == reads && section.Items.Count == 20,
                "Forward demand without a Library completion consumer must not start a Home tail request.");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task LanguageAsync()
    {
        using var session = new Session(new Wire { HoldRefresh = true }); var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
        Require(vm.Sections[0].Items[0].Title == "EN Card1", "Initial fixture must render English text.");
        wire.Language = "FR"; session.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language"); vm.InvalidateLocalizedReads();
        await vm.LoadCommand.ExecuteAsync(null); await wire.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await vm.CompleteSectionAsync("row"); wire.ReleaseRefresh.TrySetResult(); await Task.Delay(100);
        Require(vm.Sections[0].Items.Count == 35 && vm.Sections[0].Items.All(i => i.Title.StartsWith("FR Carte") && i.Overview.StartsWith("FR Résumé")),
            "New-language whole response retained old-language prefix: " + vm.Sections[0].Items[0].Title + " / " + vm.Sections[0].Items.Last().Title);
        Require(wire.Requests.All(r => r.Profile == "alpha" && r.Host == "home-review.invalid"), "Language switch must retain profile/server headers.");
    }

    private static async Task MenuAsync(StackPanel parent, bool undo)
    {
        using var session = new Session(new Wire { HoldRefresh = undo, HoldWhole = !undo });
        var vm = session.Vm; var wire = session.Wire;
        var page = new HomePage { Width = 900, Height = 700 };
        var toasts = new ToastContainer(); parent.Children.Add(toasts); session.Toast.Register(toasts, parent.DispatcherQueue);
        parent.Children.Add(page);
        try
        {
            await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            Task? whole = null;
            if (!undo) { whole = vm.CompleteSectionAsync("row"); await wire.WholeStarted.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            var owner = new Button { Content = "Menu fixture" }; parent.Children.Add(owner);
            try
            {
                var menu = MediaItemMenu.Build(vm.Sections[0].Items[0], MediaItemMenu.Surface.ContinueWatching, owner: owner);
                menu.ShowAt(owner); await Task.Delay(40);
                var drop = menu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == "Remove from Continue Watching");
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(drop).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => wire.Dismissed && vm.ShowUndoBanner && vm.Sections[0].Items.All(i => i.SeriesId != "show"));
                Require(wire.MutationProfiles.All(p => p == "alpha"), "Menu mutation must use the current profile header.");
                if (undo)
                {
                    await wire.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("UndoButton")).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Until(() => wire.UndoCalls == 1 && vm.Sections[0].Items.Any(i => i.ContentId == "episode1"));
                    wire.ReleaseRefresh.TrySetResult(); await Task.Delay(150);
                    Require(vm.Sections[0].Items.Any(i => i.ContentId == "episode1"), "A pre-Undo refresh reversed the real menu Undo.");
                }
                else
                {
                    wire.ReleaseWhole.TrySetResult(); await whole!;
                    Require(!vm.Sections[0].Items.Any(i => i.SeriesId == "show"), "Completion revived unseen episode21 from the dismissed series.");
                }
                menu.Hide();
            }
            finally { parent.Children.Remove(owner); }
        }
        finally { wire.ReleaseRefresh.TrySetResult(); wire.ReleaseWhole.TrySetResult(); session.Toast.Unregister(); parent.Children.Remove(page); parent.Children.Remove(toasts); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 250; i++) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Review fixture did not reach its expected state.");
    }

    private sealed class Session : IDisposable
    {
        public Wire Wire; public HttpClient Http; public SiloApiClient Client; public AuthService Auth; public HomeViewModel Vm; public ToastService Toast = new();
        private readonly EventChannelClient _events;
        private readonly FieldInfo _field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly IServiceProvider _previous;
        public Session(Wire wire)
        {
            Wire = wire; Http = new(wire); Client = new(Http); Client.SetBaseUrl("https://home-review.invalid");
            Auth = new(Client, new AuthApi(Client)); Auth.SetTokens("initial-fixture-account", "synthetic-refresh", 0); Auth.SelectProfile("alpha");
            var home = new HomeApi(Client); Vm = new(home, Auth); _events = new(Client, Auth);
            _previous = (IServiceProvider)_field.GetValue(null)!;
            _field.SetValue(null, new Services(_previous, new Dictionary<Type, object> {
                [typeof(SiloApiClient)] = Client, [typeof(HomeApi)] = home, [typeof(HomeViewModel)] = Vm,
                [typeof(AuthService)] = Auth, [typeof(CatalogApi)] = new CatalogApi(Client), [typeof(ToastService)] = Toast,
                [typeof(LibraryViewModel)] = new LibraryViewModel(new CatalogApi(Client)),
                [typeof(EventChannelClient)] = _events }));
        }
        public void Dispose()
        {
            Wire.ReleaseFirst.TrySetResult(); Wire.ReleaseRefresh.TrySetResult(); Wire.ReleaseWhole.TrySetResult();
            Vm.SetActive(false); WeakReferenceMessenger.Default.UnregisterAll(Vm); _field.SetValue(null, _previous); _events.Dispose(); Auth.Dispose(); Http.Dispose();
        }
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> overrides) : IServiceProvider
    { public object? GetService(Type type) => overrides.TryGetValue(type, out var service) ? service : fallback.GetService(type); }
    private sealed record Read(string Id, string Host, string Profile, string Query, string Account);
    private sealed class Wire : HttpMessageHandler
    {
        public bool Batch, Hero, HoldRefresh, HoldWhole, Dismissed, NullItems; public string Language = "EN", Title = "Progress"; public int UndoCalls;
        public string Capabilities = "{\"row_item_limit_max\":100}";
        public int SectionCount = 1, ConfiguredItemLimit = 50;
        public ConcurrentQueue<Read> Requests = new(); public ConcurrentQueue<string> MutationProfiles = new();
        public TaskCompletionSource FirstFive = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously),
            RefreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously),
            WholeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseWhole = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _capped;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var profile = request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : "missing";
            if (request.Method != HttpMethod.Get)
            {
                MutationProfiles.Enqueue(profile);
                if (request.Method == HttpMethod.Put) Dismissed = true; else { Dismissed = false; UndoCalls++; }
                return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/capabilities")) return Json(Capabilities);
            if (path.EndsWith("/layout")) return Json(JsonSerializer.Serialize(new { sections = Enumerable.Range(1, Batch ? 6 : SectionCount).Select(i => new {
                id = Batch || SectionCount > 1 ? $"row{i}" : "row", title = "Progress", section_type = "continue_watching", featured = Hero, item_limit = ConfiguredItemLimit }) }));
            var id = path.Split('/')[5]; var capped = request.RequestUri.Query == "?item_limit=20";
            var itemLimit = request.RequestUri.Query.StartsWith("?item_limit=", StringComparison.Ordinal)
                ? int.Parse(request.RequestUri.Query["?item_limit=".Length..]) : ConfiguredItemLimit;
            var dismissed = Dismissed; var language = Language; var hero = Hero;
            var account = request.Headers.Authorization?.Parameter == "initial-fixture-account" ? "initial-fixture"
                : request.Headers.Authorization?.Parameter == "replacement-fixture-account" ? "replacement-fixture" : "missing";
            Requests.Enqueue(new(id, request.RequestUri.Host, profile, request.RequestUri.Query, account));
            if (Batch && id != "row6") { if (Requests.Count == 5) FirstFive.TrySetResult(); await ReleaseFirst.Task; }
            if (capped && Interlocked.Increment(ref _capped) > 1 && HoldRefresh) { RefreshStarted.TrySetResult(); await ReleaseRefresh.Task; }
            if (!capped && HoldWhole) { WholeStarted.TrySetResult(); await ReleaseWhole.Task; }
            if (NullItems) return Json("null");
            return Json(JsonSerializer.Serialize(new { id, title = Title, section_type = "continue_watching", featured = hero, item_limit = itemLimit,
                total_count = 35, has_more = itemLimit < 35, items = Enumerable.Range(1, Math.Min(35, itemLimit)).Where(i => !dismissed || i is not (1 or 21)).Select(i => new {
                    content_id = i is 1 or 21 ? $"episode{i}" : $"movie{i}", series_id = i is 1 or 21 ? "show" : null, type = i is 1 or 21 ? "episode" : "movie",
                    title = language == "FR" ? $"FR Carte{i}" : $"EN Card{i}", overview = language == "FR" ? $"FR Résumé{i}" : $"EN Overview{i}",
                    progress_updated_at = "2026-11-01T00:00:00Z" }) }));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json") };
    }
}
