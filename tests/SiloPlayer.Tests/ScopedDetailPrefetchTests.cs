using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Library-scoped detail pages consume same-scope card intent through the
/// shared bounded cache, and Library prerequisites start concurrently.
/// </summary>
public sealed class ScopedDetailPrefetchTests : IDisposable
{
    private readonly DetailViewModelTestScope _scope = new();
    public void Dispose() => _scope.Dispose();

    private static ItemDetailPrefetchCache ScopedCache(List<(string Id, int? LibraryId)> calls) =>
        new((id, libraryId, _) =>
        {
            lock (calls) calls.Add((id, libraryId));
            return Task.FromResult(new MediaItemDetail
            {
                ContentId = id, Type = "movie", Title = $"{id}@{libraryId}", UserState = new(), UserData = new(),
            });
        });

    [Fact]
    public async Task LibraryScopedDetailConsumesTheSameScopePrefetchWithoutAnotherRead()
    {
        var calls = new List<(string Id, int? LibraryId)>();
        var cache = ScopedCache(calls);
        cache.Prefetch("film", 7);
        // The fixture's catalog transport rejects every direct detail read.
        var vm = _scope.Create(cache);

        await vm.LoadInLibraryAsync("film", 7).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("film@7", vm.Item!.Title);
        lock (calls) Assert.Equal(new (string, int?)[] { ("film", 7) }, calls.ToArray());
    }

    [Fact]
    public async Task UnscopedPrefetchIsNeverServedToALibraryScopedDetail()
    {
        var calls = new List<(string Id, int? LibraryId)>();
        var cache = ScopedCache(calls);
        cache.Prefetch("film");
        var vm = _scope.Create(cache);

        await vm.LoadInLibraryAsync("film", 7).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("film@7", vm.Item!.Title);
        lock (calls) Assert.Single(calls, call => call == ("film", 7));
    }

    [Fact]
    public void LibraryPrerequisiteReadsStartBeforeEitherIsAwaited()
    {
        var page = Read("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        var start = page.IndexOf("var shownSources = CatalogSortChoices.LoadShownSourcesAsync(ratingsApi);", StringComparison.Ordinal);
        var restore = page.IndexOf("await RestoreLibraryStateAsync(library.Id, visit, identity);", start, StringComparison.Ordinal);
        var join = page.IndexOf("await shownSources;", start, StringComparison.Ordinal);
        Assert.True(start >= 0, "The rating-capability read must start before the page-state restore.");
        Assert.True(restore > start && join > restore, "Both prerequisite reads must be in flight before either is awaited.");
        Assert.DoesNotContain("await CatalogSortChoices.LoadShownSourcesAsync(App.Services.GetRequiredService<CatalogApi>());", page, StringComparison.Ordinal);
        Assert.Contains("LibraryPerformanceTrace.Measure(\"library-prerequisites\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void CardDetailIntentAlwaysCarriesTheNavigationLibraryScope()
    {
        // An unscoped speculative read can never be consumed by a scoped
        // navigation, so every detail warm passes the scope it navigates with.
        var unscopedPrefetch = new Regex(@"\.Prefetch\((?:item|MediaItem|season|episode)\.ContentId\)|\.Prefetch\(contentId\)");
        foreach (var path in new[]
                 {
                     new[] { "src", "SiloPlayer", "Controls", "LibraryGridCard.cs" },
                     new[] { "src", "SiloPlayer", "Controls", "PosterCard.xaml.cs" },
                     new[] { "src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs" },
                     new[] { "src", "SiloPlayer", "Controls", "RecommendationPosterCard.cs" },
                     new[] { "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs" },
                 })
        {
            var source = Read(path);
            Assert.DoesNotMatch(unscopedPrefetch, source);
            Assert.DoesNotContain("if (!libraryId.HasValue) App.Services.GetRequiredService<ItemDetailPrefetchCache>()", source, StringComparison.Ordinal);
        }

        Assert.Contains(".PrefetchWatchDetail(item.ContentId, libraryId);", Read("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains(".PrefetchWatchDetail(item.ContentId, LibraryId);", Read("src", "SiloPlayer", "Controls", "HeroCarousel.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(140, dwell.Token);", Read("src", "SiloPlayer", "Controls", "RecommendationPosterCard.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DetailViewModelReadsScopedDetailThroughTheCacheOnlyWhenItServesThatScope()
    {
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "ItemDetailViewModel.cs");

        Assert.Contains("_detailPrefetchCache.SupportsLibraryScope", viewModel, StringComparison.Ordinal);
        Assert.Contains("_detailPrefetchCache.GetSnapshotAsync(contentId, libraryId, ct)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_catalogApi.GetItemDetailAsync(contentId, libraryId, ct)", viewModel, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory)
               && !File.Exists(Path.Combine(directory, "SiloPlayer.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("Could not locate repository root.");

        return File.ReadAllText(Path.Combine([directory, .. parts]));
    }
}
