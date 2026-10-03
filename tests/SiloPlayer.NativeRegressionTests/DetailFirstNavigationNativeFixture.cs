using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class DetailFirstNavigationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)servicesField.GetValue(null)!;
        using var fixture = new Services(original);
        servicesField.SetValue(null, fixture);
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "series-loading")
            {
                foreach (var scenario in new (int? Metadata, int Returned)[] { (1, 1), (null, 1), (1, 2), (1, 0) })
                {
                    using var loading = new Services(original);
                    servicesField.SetValue(null, loading);
                    await CheckSeriesLoadingAsync(parent, loading, scenario.Metadata, scenario.Returned);
                }
                return;
            }
            foreach (var type in new[] { "series", "season", "episode", "movie" })
            foreach (var width in new[] { 1280d, 460d })
            {
                // A completed card prefetch makes OnNavigatedTo paint before
                // Frame attaches its new page. Previously all TV fixtures
                // attached/measured the page before calling UpdateUI.
                var id = type + "-" + width;
                fixture.Item = new MediaItemDetail
                {
                    ContentId = id, Type = type, Title = "Coven Academy fixture",
                    Overview = "The catalog response is already available before the page loads.",
                    UserState = new(), UserRating = 3,
                    PlayContentId = type is "series" or "season" ? "episode-target" : id
                };
                await fixture.Cache.PrefetchAsync(id);
                var frame = new Frame(); // deliberately not yet in a XamlRoot
                if (!frame.Navigate(typeof(ItemDetailPage), id)) throw new InvalidOperationException("Initial detail navigation rejected.");
                var page = (ItemDetailPage)frame.Content;
                await UntilAsync(() => ((TextBlock)page.FindName("TitleText")).Text == fixture.Item.Title);
                if (page.XamlRoot != null || page.ActualHeight != 0)
                    throw new InvalidOperationException("Fixture missed the pre-attachment first-navigation boundary.");
                Program.Log($"PASS: {type}/{width} paints metadata before first attachment without aborting navigation.");
                frame.Width = width; frame.Height = 720;
                parent.Children.Add(frame);
                try
                {
                    await UntilAsync(() => page.XamlRoot != null && page.ActualHeight > 0);
                    page.UpdateLayout();
                    var title = (TextBlock)page.FindName("TitleText");
                    var overview = (FrameworkElement)page.FindName("OverviewText");
                    if (title.ActualHeight <= 0 || overview.ActualHeight <= 0)
                        throw new InvalidOperationException("Loaded detail is blank despite successful item/playback resolution.");
                    if (type is "series" or "season" or "episode")
                    {
                        var viewport = (Grid)typeof(ItemDetailPage).GetField("_tvViewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                        if (viewport.ActualHeight <= 0 || width < 1024 && !double.IsNaN(viewport.Height))
                            throw new InvalidOperationException("Deferred first TV layout did not size from the attached window.");
                    }
                    await MediaParityNativeFixture.CaptureAsync(frame, $"detail-first-navigation-{type}-{width}.png");
                    if (!frame.Navigate(typeof(Page))) throw new InvalidOperationException("Leaving detail rejected.");
                    frame.GoBack();
                    await UntilAsync(() => frame.Content is ItemDetailPage revisit && ((TextBlock)revisit.FindName("TitleText")).Text == fixture.Item.Title);
                    Program.Log($"PASS: {type}/{width} displays title/overview after attachment and survives Back revisit.");
                }
                finally
                {
                    frame.Navigate(typeof(Page));
                    frame.BackStack.Clear(); frame.ForwardStack.Clear();
                    parent.Children.Remove(frame);
                    await Task.Delay(100); // drain Loaded/Unloaded before disposing the isolated service scope
                }
            }
            // Release detached WinRT page wrappers while the native test's
            // dispatcher/window are still alive, rather than during CLR exit.
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
        finally { servicesField.SetValue(null, original); }
    }

    private static async Task CheckSeriesLoadingAsync(StackPanel parent, Services fixture, int? metadataCount, int returnedCount)
    {
        fixture.Transport.HoldSeries = true;
        fixture.Transport.ReturnedSeasons = returnedCount;
        fixture.Item = new MediaItemDetail
        {
            ContentId = "single-series", Type = "series", Title = "Single-season fixture", SeasonCount = metadataCount, EpisodeCount = 1,
            Overview = "Already-known series metadata must choose the same layout while its episodes load.",
            UserState = new(), UserRating = 3, PlayContentId = "episode-target"
        };
        await fixture.Cache.PrefetchAsync(fixture.Item.ContentId);
        var frame = new Frame { Width = 1280, Height = 720 };
        frame.Navigate(typeof(ItemDetailPage), fixture.Item.ContentId);
        parent.Children.Add(frame);
        try
        {
            var page = (ItemDetailPage)frame.Content;
            await UntilAsync(() => page.ActualHeight > 0 && fixture.Transport.SeasonsStarted);
            page.UpdateLayout(); await Task.Delay(100);
            var viewport = (Grid)typeof(ItemDetailPage).GetField("_tvViewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var title = (FrameworkElement)page.FindName("TitleText");
            var poster = (FrameworkElement)page.FindName("HeroPosterContainer");
            var initialTitle = title.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            var initialPoster = poster.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            var initialHeight = viewport.Height;
            Program.Log($"TRACE: metadata={metadataCount}, returned={returnedCount}, pending seasons: viewport={initialHeight}, titleY={initialTitle.Y}, posterY={initialPoster.Y}.");
            await MediaParityNativeFixture.CaptureAsync(frame, $"series-loading-{metadataCount}-{returnedCount}-before.png");
            fixture.Transport.SeasonsRelease.TrySetResult();
            await UntilAsync(() => !page.ViewModel.IsSeasonsLoading && page.ViewModel.Seasons.Count == returnedCount
                && (returnedCount != 1 || ((Grid)page.FindName("EpisodesPanel")).Children.Count > 0));
            page.UpdateLayout(); await Task.Delay(100);
            var resolvedTitle = title.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            var resolvedPoster = poster.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            if (double.IsNaN(viewport.Height) != (returnedCount != 1))
                throw new InvalidOperationException("Authoritative season result did not update the TV layout without an unrelated response.");
            // A later unrelated recommendations response currently forces the
            // missing responsive update, producing the visible snap.
            fixture.Transport.SimilarRelease.TrySetResult();
            await UntilAsync(() => ((Panel)page.FindName("SimilarPanel")).Children.Count > 0);
            page.UpdateLayout(); await Task.Delay(150);
            var finalTitle = title.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            var finalPoster = poster.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
            Program.Log($"TRACE: completed seasons/similar: viewport={viewport.Height}, titleY={finalTitle.Y}, posterY={finalPoster.Y}.");
            await MediaParityNativeFixture.CaptureAsync(frame, $"series-loading-{metadataCount}-{returnedCount}-after.png");
            if (Math.Abs(resolvedTitle.Y - finalTitle.Y) > 2 || Math.Abs(resolvedPoster.Y - finalPoster.Y) > 2)
                throw new InvalidOperationException("Unrelated recommendations response changed the resolved series hero composition.");
            if (metadataCount == 1 && returnedCount == 1 && (double.IsNaN(initialHeight) || Math.Abs(initialTitle.Y - finalTitle.Y) > 2 || Math.Abs(initialPoster.Y - finalPoster.Y) > 2))
                throw new InvalidOperationException("Known single-season detail first uses a different hero composition, then snaps when companion results repaint it.");
            Program.Log($"PASS: metadata={metadataCount}, returned={returnedCount}: season result owns layout; recommendations preserve it; known single-season first paint stays stable.");
        }
        finally
        {
            fixture.Transport.SeasonsRelease.TrySetResult(); fixture.Transport.SimilarRelease.TrySetResult();
            frame.Navigate(typeof(Page)); frame.BackStack.Clear(); parent.Children.Remove(frame);
            await Task.Delay(100); GC.Collect(); GC.WaitForPendingFinalizers();
        }
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        for (var n = 0; n < 150; n++) { if (ready()) return; await Task.Delay(20); }
        throw new InvalidOperationException("First-navigation detail did not paint/attach within three seconds.");
    }

    private sealed class Services : IServiceProvider, IDisposable
    {
        private readonly IServiceProvider _original;
        private readonly HttpClient _http;
        internal readonly Wire Transport = new();
        private readonly CatalogApi _catalog;
        private readonly SettingsApi _settings;
        internal MediaItemDetail Item = new();
        internal ItemDetailPrefetchCache Cache { get; }
        internal Services(IServiceProvider original)
        {
            _http = new(Transport);
            _original = original;
            var api = new SiloApiClient(_http); api.SetBaseUrl("https://first-navigation-fixture.invalid");
            _catalog = new(api); _settings = new(api);
            Cache = new((_, _) => Task.FromResult(Item));
        }
        public object? GetService(Type type)
        {
            if (type == typeof(ItemDetailViewModel)) return new ItemDetailViewModel(_catalog, Cache);
            if (type == typeof(CatalogApi)) return _catalog;
            if (type == typeof(SettingsApi)) return _settings;
            if (type == typeof(ItemDetailPrefetchCache)) return Cache;
            return _original.GetService(type);
        }
        public void Dispose() => _http.Dispose();
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool HoldSeries, SeasonsStarted;
        internal int ReturnedSeasons = 1;
        internal readonly TaskCompletionSource SeasonsRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SimilarRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (HoldSeries && path.EndsWith("/seasons"))
            {
                SeasonsStarted = true; await SeasonsRelease.Task.WaitAsync(ct);
                return Reply(System.Text.Json.JsonSerializer.Serialize(new { items = Enumerable.Range(1, ReturnedSeasons).Select(n => new { content_id = "single-season-" + n, title = "Season " + n, season_number = n, episode_count = 1 }) }));
            }
            if (HoldSeries && path.Contains("/similar/"))
            {
                await SimilarRelease.Task.WaitAsync(ct);
                return Reply("{\"items\":[{\"content_id\":\"other-series\",\"type\":\"series\",\"title\":\"Another series\"}]}");
            }
            var json = path.EndsWith("/episodes")
                ? "{\"items\":[{\"content_id\":\"episode-target\",\"title\":\"First episode\",\"episode_number\":1,\"season_number\":1}]}"
                : path.Contains("/settings/") ? "{\"settings\":[]}" : "{\"items\":[]}";
            return Reply(json);
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }
}
