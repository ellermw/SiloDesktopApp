using System.Net;
using System.Text.Json;
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
        await RetryAndRemovalAsync();
        await ConcurrentRefreshAsync();
        await HiddenPartialAsync();
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

    private static async Task HiddenPartialAsync()
    {
        using var wire = new Wire { DuplicateFirst = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth, new SettingsApi(client));
        try
        {
            await vm.LoadCommand.ExecuteAsync(null);
            await Until(() => vm.Sections.Count == 2 && vm.Sections[1].Items.Count == 15);
            Require(wire.Whole == 1 && vm.Sections[1].Items.First().ContentId == "alpha-21" && !vm.Sections[1].HasMore,
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
        public string Title = "Progress";
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/capabilities")) return Json("""{"row_item_limit_max":100,"section_item_limit_max":100}""");
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
                Whole++;
                if (FailWhole) return new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"status":400,"detail":"fixture failure"}""") };
                if (HoldWhole) { Started.TrySetResult(); await Release.Task; }
            }
            return Json(JsonSerializer.Serialize(new { id = "row", title = Title, section_type = DuplicateFirst ? "hidden_gems" : "continue_watching", item_limit = capped ? 20 : 50,
                total_count = 35, has_more = capped, items = Enumerable.Range(1, capped ? 20 : 35).Select(i => new { content_id = $"{profile}-{i}", title = $"Card{i}", type = "movie" }) }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
