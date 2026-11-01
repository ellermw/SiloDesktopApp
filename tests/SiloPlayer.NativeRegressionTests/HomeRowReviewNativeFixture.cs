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
        await Case("promoted-hero-whole", PromotedHeroAsync);
        await Case("translated-prefix-and-tail", LanguageAsync);
        await Case("real-menu-undo-vs-refresh", () => MenuAsync(parent, true));
        await Case("real-menu-series-tail-removal", () => MenuAsync(parent, false));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
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
        using var session = new Session(new Wire()); var vm = session.Vm; var wire = session.Wire;
        await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
        wire.Hero = true; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
        await Until(() => wire.Requests.Count >= 2); await Task.Delay(100);
        Require(vm.FeaturedSections.Count == 1 && vm.FeaturedSections[0].Featured && vm.FeaturedSections[0].Items.Count == 35,
            "Promoted first-featured row must recover35 cards; count=" + vm.FeaturedSections.FirstOrDefault()?.Items.Count);
        Require(wire.Requests.Last().Query == "", "Promoted hero must issue an uncapped read.");
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
        public bool Batch, Hero, HoldRefresh, HoldWhole, Dismissed; public string Language = "EN"; public int UndoCalls;
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
            if (path.EndsWith("/capabilities")) return Json("""{"row_item_limit_max":100}""");
            if (path.EndsWith("/layout")) return Json(JsonSerializer.Serialize(new { sections = Enumerable.Range(1, Batch ? 6 : 1).Select(i => new {
                id = Batch ? $"row{i}" : "row", title = "Progress", section_type = "continue_watching", featured = Hero, item_limit = 50 }) }));
            var id = path.Split('/')[5]; var capped = request.RequestUri.Query == "?item_limit=20";
            var dismissed = Dismissed; var language = Language; var hero = Hero;
            var account = request.Headers.Authorization?.Parameter == "initial-fixture-account" ? "initial-fixture"
                : request.Headers.Authorization?.Parameter == "replacement-fixture-account" ? "replacement-fixture" : "missing";
            Requests.Enqueue(new(id, request.RequestUri.Host, profile, request.RequestUri.Query, account));
            if (Batch && id != "row6") { if (Requests.Count == 5) FirstFive.TrySetResult(); await ReleaseFirst.Task; }
            if (capped && Interlocked.Increment(ref _capped) > 1 && HoldRefresh) { RefreshStarted.TrySetResult(); await ReleaseRefresh.Task; }
            if (!capped && HoldWhole) { WholeStarted.TrySetResult(); await ReleaseWhole.Task; }
            return Json(JsonSerializer.Serialize(new { id, title = "Progress", section_type = "continue_watching", featured = hero, item_limit = capped ? 20 : 50,
                total_count = 35, has_more = capped, items = Enumerable.Range(1, capped ? 20 : 35).Where(i => !dismissed || i is not (1 or 21)).Select(i => new {
                    content_id = i is 1 or 21 ? $"episode{i}" : $"movie{i}", series_id = i is 1 or 21 ? "show" : null, type = i is 1 or 21 ? "episode" : "movie",
                    title = language == "FR" ? $"FR Carte{i}" : $"EN Card{i}", overview = language == "FR" ? $"FR Résumé{i}" : $"EN Overview{i}",
                    progress_updated_at = "2026-11-01T00:00:00Z" }) }));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json") };
    }
}
