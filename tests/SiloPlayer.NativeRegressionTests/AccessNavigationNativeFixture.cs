using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using System.Net;

internal static class AccessNavigationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        await CheckPrefetchAuthorityAsync();
        var frame = new Frame { CacheSize = 4 };
        parent.Children.Add(frame);
        var navigation = new NavigationService { Frame = frame };
        try
        {
            string? active = null;
            navigation.Navigated += (_, e) => active = e.Parameter as string;
            navigation.Navigate<Page>("earlier");
            navigation.Navigate<Page>("active");
            navigation.Navigate<Page>("forward");
            navigation.GoBackImmediately();
            await Task.Delay(30);
            var old = (Page)frame.Content;
            old.NavigationCacheMode = NavigationCacheMode.Required;
            var refresh = typeof(NavigationService).GetMethod("RefreshCurrentEntry", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Access changes cannot refresh the active route without losing navigation history.");
            if (!(bool)refresh.Invoke(navigation, null)!) throw new InvalidOperationException("Active route refresh was rejected.");
            await Task.Delay(30);
            if (ReferenceEquals(old, frame.Content) || active != "active")
                throw new InvalidOperationException("Refresh reused the stale required-cache page.");
            if (frame.BackStack.Count != 1 || (string?)frame.BackStack[0].Parameter != "earlier" ||
                frame.ForwardStack.Count != 1 || (string?)frame.ForwardStack[0].Parameter != "forward")
                throw new InvalidOperationException("Refresh changed Back/Forward history.");
            navigation.GoBackImmediately();
            if (active != "earlier") throw new InvalidOperationException("Back no longer returns to the previous route.");
            Program.Log("PASS: access refresh replaces a cached active page and preserves Back/Forward route parameters.");
        }
        finally { navigation.Frame = null; parent.Children.Remove(frame); }
        await CheckSkippedAndCancelledRefreshAsync(parent);
    }

    private static void Require(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) throw new InvalidOperationException(label);
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, int milliseconds)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
        return condition();
    }

    /// <summary>
    /// An automatic refresh of the current route (reconnect catch-up, access or language change) is not a departure. While the page holds unsaved edits (the shell's
    /// answer, stood in for here by a flag) it is skipped: no navigation, no question, and nothing runs later by itself. A refresh that a Navigating handler cancels
    /// commits nothing, leaves the stacks alone and answers false.
    /// </summary>
    private static async Task CheckSkippedAndCancelledRefreshAsync(StackPanel parent)
    {
        var frame = new Frame { CacheSize = 4 };
        parent.Children.Add(frame);
        var navigation = new NavigationService { Frame = frame };
        var holds = false; var navigating = 0; var navigated = 0;
        frame.Navigating += (_, _) => navigating++;
        navigation.Navigated += (_, _) => navigated++;
        navigation.CurrentPageHoldsUnsavedEdits = () => holds;
        try
        {
            navigation.Navigate<Page>("earlier");
            navigation.Navigate<Page>("active");
            navigation.Navigate<Page>("forward");
            navigation.GoBackImmediately();
            await Task.Delay(50);
            // A page without unsaved edits is refreshed at once, as before.
            var plain = frame.Content;
            Require(navigation.RefreshCurrentEntry(), "a refresh of a page without unsaved edits is accepted");
            await Task.Delay(30);
            Require(!ReferenceEquals(plain, frame.Content), "a page without unsaved edits is reread at once");

            // A page with a draft: no navigation (so no dialog can open), the page and the stacks are kept.
            holds = true;
            var draftPage = frame.Content; var navigatingBefore = navigating; var navigatedBefore = navigated;
            var accepted = navigation.RefreshCurrentEntry();
            Require(!accepted && navigating == navigatingBefore && navigated == navigatedBefore && ReferenceEquals(draftPage, frame.Content),
                "a refresh of a page with unsaved edits does not navigate (so it cannot open the unsaved-changes dialog) and returns false");
            Require(frame.BackStack.Count == 1 && frame.ForwardStack.Count == 1 && (string?)frame.ForwardStack[0].Parameter == "forward", "the Back and Forward stacks are untouched");
            Require(!navigation.RefreshCurrentEntry() && navigating == navigatingBefore, "a second refresh request while the draft is held is also skipped");

            // Nothing is remembered: when the draft is gone, nothing navigates by itself (no timer). The page rereads when it is asked again.
            holds = false;
            await Task.Delay(1500);
            Require(ReferenceEquals(draftPage, frame.Content) && navigating == navigatingBefore && navigated == navigatedBefore,
                "after the draft is gone nothing navigates by itself: a skipped refresh is not performed later");
            Require(navigation.RefreshCurrentEntry() && !ReferenceEquals(draftPage, frame.Content), "the next refresh request, with no draft held, rereads the page");
            await Task.Delay(30);
            Require(frame.BackStack.Count == 1 && (string?)frame.BackStack[0].Parameter == "earlier" && frame.ForwardStack.Count == 1 && (string?)frame.ForwardStack[0].Parameter == "forward",
                "after that refresh Back and Forward history is as before");

            // A skipped refresh leaves nothing behind for the next page.
            holds = true;
            Require(!navigation.RefreshCurrentEntry(), "a refresh is skipped again for the next page with a draft");
            navigation.Navigate<Page>("elsewhere");
            await Task.Delay(50);
            holds = false;
            var afterLeaving = navigated; var elsewhere = frame.Content; await Task.Delay(1200);
            Require(navigated == afterLeaving && ReferenceEquals(elsewhere, frame.Content), "a refresh skipped for a page that was left is never performed on the next one");

            // A viewer page (no guard installed) is refreshed as before.
            navigation.CurrentPageHoldsUnsavedEdits = null;
            var viewer = frame.Content;
            Require(navigation.RefreshCurrentEntry() && !ReferenceEquals(viewer, frame.Content), "a page with no guard is refreshed exactly as before");
            navigation.CurrentPageHoldsUnsavedEdits = () => holds;

            // A refresh that a Navigating handler cancels (a guard nobody told the navigation service about): nothing committed, stacks alone, false, nothing armed.
            var current = frame.Content; var back = frame.BackStack.Count; var forwardStack = frame.ForwardStack.Count; var committed = navigated;
            NavigatingCancelEventHandler cancel = (_, args) => args.Cancel = true;
            frame.Navigating += cancel;
            Exception? failure = null; var result = true;
            try { result = navigation.RefreshCurrentEntry(); } catch (Exception thrown) { failure = thrown; }
            frame.Navigating -= cancel;
            Require(failure == null && !result && ReferenceEquals(current, frame.Content) && navigated == committed && frame.BackStack.Count == back && frame.ForwardStack.Count == forwardStack,
                "a refresh cancelled by a Navigating handler throws nothing, answers false, commits nothing and leaves the stacks alone");
            // The replacement recorded for the cancelled refresh is not left armed: the same entry opened again by the Frame keeps its Back entry.
            frame.Navigate(typeof(Page), "elsewhere");
            await Task.Delay(50);
            Require(frame.BackStack.Count == back + 1 && (string?)frame.BackStack[^1].Parameter == "elsewhere",
                "the replacement of a cancelled refresh does not wait for a later navigation (Back keeps the entry it should)");
            Program.Log("PASS: an automatic refresh is skipped while unsaved edits are held and nothing runs later; a cancelled refresh commits nothing and arms nothing.");
        }
        finally { navigation.CurrentPageHoldsUnsavedEdits = null; navigation.Frame = null; parent.Children.Remove(frame); }
    }

    private static async Task CheckPrefetchAuthorityAsync()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://access-native-fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("isolated-token", "isolated-refresh", 86400); auth.SetCurrentUser(new() { Id = "account" });
        auth.SelectProfile("profile", "isolated-pin");
        var catalog = new CatalogApi(api);
        var items = new ItemDetailPrefetchCache(catalog, auth);
        using var player = new PlayerService(new PlaybackApi(api), catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "access-settings")), new SettingsApi(api));
        var firstItem = await items.GetAsync("title", CancellationToken.None);
        var firstWatch = await player.GetOrFetchWatchDetailAsync("title");
        await items.GetAsync("title", CancellationToken.None); await player.GetOrFetchWatchDetailAsync("title");
        if (wire.ItemReads != 1 || wire.WatchReads != 1) throw new InvalidOperationException("Same-authority prefetch failed to coalesce.");
        var invalidate = typeof(SiloApiClient).GetMethod("InvalidateAccessContext");
        if (invalidate != null) invalidate.Invoke(api, null);
        else api.SetProfile("profile", "isolated-new-pin"); // Old payload: same-profile authority change reproduces the cache omission.
        var nextItem = await items.GetAsync("title", CancellationToken.None);
        var nextWatch = await player.GetOrFetchWatchDetailAsync("title");
        if (wire.ItemReads != 2 || wire.WatchReads != 2 || ReferenceEquals(firstItem, nextItem) || ReferenceEquals(firstWatch, nextWatch))
            throw new InvalidOperationException($"Access revision retained old item/watch preparation: items={wire.ItemReads}, watch={wire.WatchReads}.");
        wire.Hold = true;
        var pending = player.GetOrFetchWatchDetailAsync("delayed");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        invalidate!.Invoke(api, null);
        wire.Release.TrySetResult();
        try
        {
            await pending;
            throw new InvalidOperationException("Retired watch preparation completed under new access authority.");
        }
        catch (OperationCanceledException) { }
        Program.Log("PASS: actual item/watch prefetch shares one request within authority and rereads both after access changes.");
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal int ItemReads, WatchReads;
        internal bool Hold;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v2/watch/title" or "/api/v2/watch/delayed") Interlocked.Increment(ref WatchReads);
            else if (path == "/api/v2/catalog/items/title") Interlocked.Increment(ref ItemReads);
            else throw new InvalidOperationException("Unexpected access preparation path: " + path);
            if (Hold) { Started.TrySetResult(); await Release.Task; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"content_id\":\"" + (path.EndsWith("/delayed") ? "delayed" : "title") + "\",\"type\":\"movie\",\"title\":\"Fixture\",\"versions\":[]}") };
        }
    }
}
