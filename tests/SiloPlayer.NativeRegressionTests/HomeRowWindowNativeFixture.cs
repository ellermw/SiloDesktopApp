using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

internal static class HomeRowWindowNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        await ScrollAndRefreshAsync(parent);
        await AuthoritativeRefreshAsync(legacy: true, count: 3);
        await AuthoritativeRefreshAsync(legacy: true, count: 35);
        await AuthoritativeRefreshAsync(legacy: false, count: 35);
        await WideInitialLayoutAsync(parent);
        foreach (var interruption in new[] { "refresh", "error", "null", "persistent-error", "persistent-null" })
            await DemandRecoveryAsync(parent, interruption);
        await TruncatedCompletionAsync(parent);
        await RetryAndRemovalAsync();
        await ConcurrentRefreshAsync();
        await HiddenPartialAsync();
        await HiddenPartialAsync(failCompletion: true);
        await RetiredOwnerAsync();
        Program.Log("PASS: bounded Home actual carousel, single completion, refresh, retry, removal and retired owner.");
    }

    private static async Task ScrollAndRefreshAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        SectionRow? row = null;
        try
        {
            await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            Require(wire.Capped == 1 && wire.Whole == 0, "Initial non-featured read must ask for20.");
            row = new SectionRow { Width = 900, Section = vm.Sections[0], OnNearEnd = section => _ = vm.CompleteSectionAsync(section.Id) };
            parent.Children.Add(row); await Task.Delay(120); row.UpdateLayout();
            var scroll = (ScrollViewer)row.FindName("CardsScrollViewer");
            Require(scroll.ScrollableWidth > 0, "Actual carousel must have horizontal scroll range.");
            wire.HoldWhole = true;
            scroll.ChangeView(scroll.ScrollableWidth, null, null, true);
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await vm.CompleteSectionAsync("row"); // duplicate demand returns while owned read is pending
            Require(wire.Whole == 1, "Concurrent near-end demand must share one whole read.");
            wire.Release.TrySetResult();
            await Until(() => vm.Sections[0].Items.Count == 35);
            row.Section = vm.Sections[0];
            Require(vm.Sections[0].Items.Last().ContentId == "alpha-35" && !vm.Sections[0].HasMore, "All35cards must remain reachable.");
            await vm.CompleteSectionAsync("row"); Require(wire.Whole == 1, "A completed row must not refetch on demand.");
            var dismissed = vm.Sections[0].Items[2]; dismissed.ProgressUpdatedAt = "2026-11-01T00:00:00Z";
            await vm.DismissItemCommand.ExecuteAsync(new DismissRequest { Section = vm.Sections[0], Item = dismissed });
            Require(vm.Sections[0].Items.Count == 34 && vm.Sections[0].ShownCount == 19, "Dismissal must retain the initial prefix boundary.");
            await vm.UndoDismissalCommand.ExecuteAsync(null);
            Require(vm.Sections[0].Items.Count == 35 && vm.Sections[0].ShownCount == 20 && ReferenceEquals(vm.Sections[0].Items[2], dismissed), "Undo must restore card and initial prefix boundary.");
            var first = vm.Sections[0].Items[0];
            wire.Title = "Refreshed";
            vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => wire.Whole == 2 && vm.Sections[0].Title == "Refreshed" && vm.Sections[0].Items.Count == 35);
            Require(wire.Capped == 1 && ReferenceEquals(first, vm.Sections[0].Items[0]), "Expanded refresh must remain whole and retain cards.");
            Program.Log("PASS: actual SectionRow ViewChanged recovered cards21-35 once; expanded refresh retained first card.");
        }
        finally { if (row != null) parent.Children.Remove(row); Retire(vm); }
    }

    private static async Task AuthoritativeRefreshAsync(bool legacy, int count)
    {
        using var wire = new Wire { UnsupportedCapabilities = legacy, CardCount = count, RowItemLimit = count };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        try
        {
            await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections.FirstOrDefault()?.LoadCompleted == true);
            if (!legacy) await vm.CompleteSectionAsync("row");
            var row = vm.Sections[0];
            Require(row.Items.Count == count && row.HasReadWhole && !row.CanComplete, "Fixture must start with a whole row.");
            Require(HomeRowWindow.IsExpanded(row) == !legacy && row.ShownCount == (legacy ? (int?)null : 20),
                "Only a bounded-to-whole completion may establish an expansion boundary.");
            var completionOrder = row.CompletedOrder;
            var oldFirst = row.Items[0];
            var unchanged = row.Items[2];
            // Stay at item_limit: the new head must replace the dropped card,
            // rather than being appended and discarded by a completion merge.
            wire.CardsOverride = new[] { count + 1, 1 }.Concat(Enumerable.Range(3, count - 2)).ToArray();
            wire.Title = "Authoritative refresh";
            vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections[0].Title == wire.Title);
            row = vm.Sections[0];
            Require(row.Items.Select(item => item.ContentId).SequenceEqual(wire.CardsOverride.Select(i => $"alpha-{i}")),
                "Whole refresh must adopt server membership and order, including removal and a new first card.");
            var updated = row.Items[1];
            Require(updated.ContentId == oldFirst.ContentId && updated.PositionSeconds == 123 && updated.Title == "Updated card1"
                && updated.UserState?.IsFavorite == true && updated.ProgressUpdatedAt == "2026-11-01T15:50:00Z"
                && !ReferenceEquals(updated, oldFirst), "Retained cards must receive fresh server progress and metadata.");
            Require(ReferenceEquals(unchanged, row.Items[2]), "Unchanged mounted cards should retain their identity.");
            Require(row.ShownCount == (legacy ? (int?)null : 20) && row.CompletedOrder == completionOrder && wire.Capped == (legacy ? 0 : 1),
                "Refresh must preserve only the completed row's priority boundary/order, without expanding old-server rows.");

            wire.CardsOverride = new[] { 1 }; wire.Title = "Shrunk refresh";
            vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections[0].Title == wire.Title);
            row = vm.Sections[0];
            Require(row.Items.Count == 1 && row.Items[0].ContentId == "alpha-1"
                && row.ShownCount == (legacy ? (int?)null : 1) && row.CompletedOrder == completionOrder,
                "A shrinking authoritative refresh must clamp the initial boundary and retain completion order.");
        }
        finally { Retire(vm); }
    }

    private static async Task WideInitialLayoutAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        SectionRow? row = null;
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            row = new SectionRow { Width = 2560, Section = vm.Sections[0], OnNearEnd = section => _ = vm.CompleteSectionAsync(section.Id) };
            parent.Children.Add(row); await Task.Delay(150); row.UpdateLayout(); await Task.Delay(100);
            var scroll = (ScrollViewer)row.FindName("CardsScrollViewer");
            Require(scroll.ScrollableWidth > 0 && scroll.ScrollableWidth <= scroll.ViewportWidth,
                "Wide-layout fixture must start within one viewport of the partial tail.");
            Require(wire.Capped == 1 && wire.Whole == 0, "Initial wide layout must not immediately reread the whole row.");
            scroll.ChangeView(scroll.ScrollableWidth, null, null, true);
            await Until(() => vm.Sections[0].Items.Count == 35);
            Require(wire.Whole == 1, "Forward scrolling must still complete the wide row.");
        }
        finally { if (row != null) parent.Children.Remove(row); Retire(vm); }
    }

    private static async Task DemandRecoveryAsync(StackPanel parent, string interruption)
    {
        using var wire = new Wire { HoldFirstWhole = interruption == "refresh", FailFirstWhole = interruption == "error",
            NullFirstWhole = interruption == "null", FailWhole = interruption == "persistent-error", NullWhole = interruption == "persistent-null" };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth); vm.SetActive(true);
        SectionRow? row = null;
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            row = new SectionRow { Width = 900, Section = vm.Sections[0], OnNearEnd = section => _ = vm.CompleteSectionAsync(section.Id) };
            parent.Children.Add(row); await Task.Delay(120); row.UpdateLayout();
            var scroll = (ScrollViewer)row.FindName("CardsScrollViewer");
            Require(scroll.ScrollableWidth > 0, "Recovery fixture must have a scrollable partial row.");
            scroll.ChangeView(scroll.ScrollableWidth, null, null, true);
            if (interruption == "refresh")
            {
                await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
                vm.QueueRealtimeRefresh("playback_progress", client.CaptureContext());
                await Until(() => wire.Capped == 2);
            }
            else if (interruption is "persistent-error" or "persistent-null")
            {
                await Until(() => wire.Whole == 2); await Task.Delay(150);
                Require(vm.Sections[0].HasMore && vm.Sections[0].Items.Count == 20 && wire.Whole == 2,
                    "Persistent errors must retain the partial row and bound automatic retries.");
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    var capped = wire.Capped;
                    vm.QueueRealtimeRefresh("playback_progress", client.CaptureContext());
                    await Until(() => wire.Capped == capped + 1); await Task.Delay(150);
                    row.Section = HomeSectionDedupe.Snapshot(vm.Sections[0]);
                    row.Width = 940 + repeat * 40;
                    row.UpdateLayout(); await Task.Delay(100);
                    Require(wire.Whole == 2,
                        "Refreshes and near-end size/presentation events must not restart an exhausted completion cycle.");
                }
                wire.FailWhole = false; wire.NullWhole = false;
                var next = (Button)row.FindName("ScrollRightBtn");
                Require(next.IsEnabled, "Forward navigation must remain available at a partial row's maximum offset.");
                ((IInvokeProvider)new ButtonAutomationPeer(next).GetPattern(PatternInterface.Invoke)).Invoke();
            }
            // No further scrolling (or left-and-back gesture) after the first
            // demand: unchanged refreshes and transient failures must recover.
            await Until(() => vm.Sections[0].Items.Count == 35);
            Require(!vm.Sections[0].HasMore && wire.Whole >= 2, "Completion demand was lost after " + interruption);
        }
        finally { wire.Release.TrySetResult(); if (row != null) parent.Children.Remove(row); Retire(vm); }
    }

    private static async Task TruncatedCompletionAsync(StackPanel parent)
    {
        using var wire = new Wire { PartialWhole = true, HoldWhole = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        SectionRow? row = null;
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            var demands = 0;
            row = new SectionRow { Width = 900, Section = vm.Sections[0], OnNearEnd = section => { demands++; _ = vm.CompleteSectionAsync(section.Id); } };
            parent.Children.Add(row); await Task.Delay(120); row.UpdateLayout();
            var scroll = (ScrollViewer)row.FindName("CardsScrollViewer");
            Require(scroll.ScrollableWidth > 0, "Truncated row must have a scrollable carousel.");
            scroll.ChangeView(scroll.ScrollableWidth, null, null, true);
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            wire.Release.TrySetResult();
            await Until(() => vm.Sections[0].HasReadWhole); await Task.Delay(100);
            Require(vm.Sections[0].HasMore && vm.Sections[0].Items.Count == 25 && vm.Sections[0].ShownCount == 20,
                "A truncated whole response must retain has_more and the initial-card boundary.");
            var order = vm.Sections[0].CompletedOrder;
            var initialDemands = demands;
            for (var repeat = 0; repeat < 3; repeat++)
            {
                row.Section = HomeSectionDedupe.Snapshot(vm.Sections[0]);
                row.UpdateLayout();
                scroll.ChangeView(Math.Max(0, scroll.ScrollableWidth - 100), null, null, true); await Task.Delay(40);
                scroll.ChangeView(scroll.ScrollableWidth, null, null, true); await Task.Delay(40);
                await vm.CompleteSectionAsync("row");
            }
            Require(vm.Sections[0].HasReadWhole && !vm.Sections[0].CanComplete && wire.Whole == 1 && demands == initialDemands,
                "Repeated near-end events and presentation updates must not request the same truncated whole row again.");
            Require(!((Button)row.FindName("ScrollRightBtn")).IsEnabled
                && ((Microsoft.UI.Xaml.UIElement)row.FindName("RightFadeGradient")).Opacity == 0,
                "A whole-read row must disable its forward arrow and fade at the end, despite server has_more.");

            wire.Title = "Still truncated"; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections[0].Title == "Still truncated"); await Task.Delay(150);
            await vm.CompleteSectionAsync("row");
            Require(vm.Sections[0].HasMore && vm.Sections[0].HasReadWhole && !vm.Sections[0].CanComplete
                && vm.Sections[0].Items.Count == 25 && wire.Whole == 2 && wire.Capped == 1,
                "An expanded truncated refresh must make one uncapped request, not rearm completion.");

            wire.PartialWhole = false; wire.ReverseWhole = true; wire.Title = "Full";
            vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections[0].Title == "Full");
            Require(!vm.Sections[0].HasMore && vm.Sections[0].Items.Count == 35 && vm.Sections[0].ShownCount == 20
                && vm.Sections[0].CompletedOrder == order && wire.Whole == 3,
                "A later whole refresh must recover new cards while preserving the initial-card budget and completion order.");
            Require(vm.Sections[0].Items.Select(item => item.ContentId).SequenceEqual(
                Enumerable.Range(1, 35).Reverse().Select(i => $"alpha-{i}")),
                "A whole refresh must adopt the server's new ranking rather than merge cached cards.");
            var later = new HomeSectionWithItems { Id = "later", SectionType = "continue_watching",
                Items = new() { new() { ContentId = "alpha-1" }, new() { ContentId = "alpha-25" } } };
            var projected = HomeSectionDedupe.Project([new() { Id = "row" }, new() { Id = "later" }],
                new Dictionary<string, HomeSectionWithItems> { ["row"] = vm.Sections[0], ["later"] = later },
                new HashSet<string> { "continue_watching" });
            Require(projected["later"].Items.Select(item => item.ContentId).SequenceEqual(new[] { "alpha-1" })
                && projected["row"].Items.Take(20).Select(item => item.ContentId).SequenceEqual(Enumerable.Range(16, 20).Reverse().Select(i => $"alpha-{i}")),
                "The refreshed initial-card budget must retain duplicate priority while the refreshed tail yields to another row's initial cards.");
        }
        finally { wire.Release.TrySetResult(); if (row != null) parent.Children.Remove(row); Retire(vm); }
    }

    private static async Task RetryAndRemovalAsync()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            wire.FailWhole = true; await vm.CompleteSectionAsync("row");
            Require(vm.Sections[0].Items.Count == 20 && vm.Sections[0].HasMore && !vm.Sections[0].LoadFailed, "Completion error must retain partial row for retry.");
            wire.FailWhole = false; wire.HoldWhole = true;
            var pending = vm.CompleteSectionAsync("row"); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            vm.Receive(new SiloPlayer.Messaging.MediaSurfaceChanged(SiloPlayer.Messaging.MediaSurfaceChangeKind.WatchedMarked, "alpha-3", context: client.CaptureContext()));
            wire.Release.TrySetResult(); await pending;
            Require(!vm.Sections[0].Items.Any(i => i.ContentId == "alpha-3") && vm.Sections[0].Items.Count == 34, "A card removed during completion must stay removed.");
            Require(vm.Sections[0].Items.Select(i => i.ContentId).Distinct().Count() == 34, "Whole merge must not duplicate shown cards.");
        }
        finally { Retire(vm); }
    }

    private static async Task RetiredOwnerAsync()
    {
        using var wire = new Wire { HoldWhole = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            var pending = vm.CompleteSectionAsync("row"); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            auth.SelectProfile("beta"); await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections.FirstOrDefault()?.Items.FirstOrDefault()?.ContentId == "beta-1");
            wire.Release.TrySetResult(); await pending;
            Require(vm.Sections[0].Items.Count == 20 && vm.Sections[0].Items.All(i => i.ContentId.StartsWith("beta-")), "Slow old-profile completion cannot publish into new owner.");
            client.LocalizedReads.NotifySettingChanged("catalog.metadata_language"); vm.InvalidateLocalizedReads();
            Require(vm.Sections[0].Items.All(i => i.ContentId.StartsWith("beta-")), "Language retirement must preserve owner content until refresh.");
        }
        finally { wire.Release.TrySetResult(); Retire(vm); }
    }

    private static async Task ConcurrentRefreshAsync()
    {
        using var wire = new Wire { HoldWhole = true, HoldRefresh = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); await Until(() => vm.Sections.FirstOrDefault()?.Items.Count == 20);
            vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null); await wire.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var pending = vm.CompleteSectionAsync("row"); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            vm.Receive(new SiloPlayer.Messaging.MediaSurfaceChanged(SiloPlayer.Messaging.MediaSurfaceChangeKind.WatchedMarked, "alpha-3", context: client.CaptureContext()));
            wire.Release.TrySetResult(); await pending;
            wire.RefreshRelease.TrySetResult(); await Task.Delay(150);
            Require(vm.Sections[0].Items.Count == 34 && !vm.Sections[0].HasMore && !vm.Sections[0].Items.Any(i => i.ContentId == "alpha-3"),
                "A late bounded refresh must neither shrink completed rows nor restore a concurrent dismissal.");
        }
        finally { wire.Release.TrySetResult(); wire.RefreshRelease.TrySetResult(); Retire(vm); }
    }

    private static async Task HiddenPartialAsync(bool failCompletion = false)
    {
        using var wire = new Wire { DuplicateFirst = true, FailWhole = failCompletion }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth, new SettingsApi(client));
        try
        {
            await vm.LoadCommand.ExecuteAsync(null);
            if (failCompletion)
            {
                await Until(() => wire.Whole == 2); await Task.Delay(150);
                Require(vm.Sections.Count == 2 && vm.Sections[1].Items.Count == 0,
                    "Failed hidden-row recovery must keep the duplicate-filtered row hidden.");
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    var capped = wire.Capped;
                    vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
                    await Until(() => wire.Capped == capped + 1); await Task.Delay(150);
                    Require(wire.Whole == 2, "Hidden-row projection must back off after its retry budget is exhausted.");
                }
                wire.FailWhole = false;
                await vm.CompleteSectionAsync("row");
            }
            await Until(() => vm.Sections.Count == 2 && vm.Sections[1].Items.Count == 15);
            Require(wire.Whole == (failCompletion ? 3 : 1) && vm.Sections[1].Items.First().ContentId == "alpha-21" && !vm.Sections[1].HasMore,
                "A fully duplicate-filtered partial row must recover its hidden remaining cards without a carousel.");
        }
        finally { Retire(vm); }
    }

    private static void Retire(HomeViewModel vm) { vm.SetActive(false); CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 250; i++) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Home row state did not settle.");
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Capped, Whole; public bool HoldWhole, FailWhole, HoldRefresh, DuplicateFirst;
        public bool HoldFirstWhole, FailFirstWhole, NullFirstWhole, NullWhole, PartialWhole, ReverseWhole, UnsupportedCapabilities;
        public int CardCount = 35, RowItemLimit = 50;
        public int[]? CardsOverride;
        public string Title = "Progress";
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/capabilities")) return UnsupportedCapabilities
                ? new(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
                : Json("""{"row_item_limit_max":100,"section_item_limit_max":100}""");
            if (path.EndsWith("/layout")) return Json(DuplicateFirst
                ? """{"sections":[{"id":"anchor","title":"First","section_type":"plain","item_limit":20},{"id":"row","title":"More","section_type":"hidden_gems","item_limit":50}]}"""
                : """{"sections":[{"id":"row","title":"Progress","section_type":"continue_watching","item_limit":50}]}""");
            if (path.EndsWith("/recipes")) return Json("""{"categories":[{"category":"one","recipes":[{"type":"hidden_gems","avoid_duplicates":true}]}]}""");
            var profile = request.Headers.TryGetValues("X-Profile-ID", out var ids) ? ids.Single() : "alpha";
            var capped = request.RequestUri.Query == "?item_limit=20";
            if (path.Contains("/anchor/")) return Json(JsonSerializer.Serialize(new { id = "anchor", section_type = "plain", item_limit = 20,
                items = Enumerable.Range(1, 20).Select(i => new { content_id = $"{profile}-{i}", title = $"Card{i}", type = "movie" }) }));
            if (capped)
            {
                Capped++;
                if (HoldRefresh && Capped > 1) { RefreshStarted.TrySetResult(); await RefreshRelease.Task; }
            }
            else
            {
                var attempt = Interlocked.Increment(ref Whole);
                if (FailWhole || (FailFirstWhole && attempt == 1)) return new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"status":400,"detail":"fixture failure"}""") };
                if (NullWhole || (NullFirstWhole && attempt == 1)) return Json("null");
                if (HoldFirstWhole && attempt == 1) { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
                else if (HoldWhole) { Started.TrySetResult(); await Release.Task; }
            }
            IEnumerable<int> cards = CardsOverride ?? Enumerable.Range(1, capped ? Math.Min(20, CardCount) : PartialWhole ? 25 : CardCount);
            if (!capped && ReverseWhole) cards = cards.Reverse();
            return Json(JsonSerializer.Serialize(new { id = "row", title = Title, section_type = DuplicateFirst ? "hidden_gems" : "continue_watching", item_limit = capped ? 20 : RowItemLimit,
                total_count = CardCount, has_more = capped || PartialWhole, items = cards.Select(i => new { content_id = $"{profile}-{i}",
                    title = CardsOverride != null && i == 1 ? "Updated card1" : $"Card{i}", type = "movie",
                    position_seconds = CardsOverride != null && i == 1 ? 123 : 0,
                    progress_updated_at = CardsOverride != null && i == 1 ? "2026-11-01T15:50:00Z" : null,
                    user_state = new { is_favorite = CardsOverride != null && i == 1 } }) }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
