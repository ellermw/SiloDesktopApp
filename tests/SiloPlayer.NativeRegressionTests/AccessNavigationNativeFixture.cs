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
