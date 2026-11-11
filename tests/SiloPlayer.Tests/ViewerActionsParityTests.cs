using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Viewer-surface parity with official Silo main 4a0aba61 (Nov 10): Continue Watching / Next Up
/// removal on the dedicated page (G1), complete smart-collection rules (G4), Trending worldwide
/// Show (G5), Calendar digital/physical badges (G9), library-type sidebar icons (G10) and the
/// episode link library scope (G12).
/// </summary>
public class ViewerActionsParityTests
{
    // ----- G4: QueryDefinition.HasCompleteRule, cases from the server's own test -----

    private static QueryDefinition Rule(string field, string op, object? value)
        => new() { Groups = [new() { Match = "all", Rules = [new() { Field = field, Op = op, Value = value }] }] };

    public static TheoryData<string, QueryDefinition, bool> ServerRuleCases() => new()
    {
        { "no groups", new QueryDefinition(), false },
        { "a group with no rules", new QueryDefinition { Groups = [new() { Match = "all" }] }, false },
        { "blank value", Rule("genre", "is", "  "), false },
        { "missing value", Rule("genre", "is", null), false },
        { "open range", Rule("year", "between", new object[] { 1990d, "" }), false },
        { "empty range", Rule("year", "between", Array.Empty<object>()), false },
        { "range with a missing end", Rule("year", "between", new object?[] { 1990d, null }), false },
        { "nested empty array", Rule("year", "between", new object[] { 1990d, Array.Empty<object>() }), false },
        { "no field", Rule("", "is", "Comedy"), false },
        { "blank field", Rule("  ", "is", "Comedy"), false },
        { "no operator", Rule("genre", "", "Comedy"), false },
        { "text", Rule("genre", "is", "Comedy"), true },
        { "zero", Rule("year", "is", 0d), true },
        { "integer zero", Rule("year", "is", 0), true },
        { "false", Rule("watched", "is", false), true },
        { "true", Rule("watched", "is", true), true },
        { "range", Rule("year", "between", new object[] { 1990d, 1999d }), true },
        { "date range", Rule("added_at", "between", new[] { "2020-01-01", "2020-12-31" }), true },
        { "one complete rule among blanks", new QueryDefinition { Groups = [
            new() { Match = "all", Rules = [new() { Field = "genre", Op = "is", Value = "" }] },
            new() { Match = "all", Rules = [new() { Field = "genre", Op = "is", Value = "Drama" }] }] }, true },
        { "a complete rule beside a blank one", new QueryDefinition { Groups = [new() { Match = "all", Rules = [
            new() { Field = "genre", Op = "is", Value = "" },
            new() { Field = "year", Op = "gte", Value = 1999 }] }] }, true },
    };

    [Theory]
    [MemberData(nameof(ServerRuleCases))]
    public void CompleteRulePredicateMatchesTheServer(string name, QueryDefinition definition, bool expected)
        => Assert.True(expected == QueryEditing.HasCompleteRule(definition), name);

    [Fact]
    public void NullDefinitionNeedsARule() => Assert.False(QueryEditing.HasCompleteRule(null));

    [Theory]
    [InlineData("""{"groups":[]}""", false)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"genre","op":"is","value":""}]}]}""", false)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"genre","op":"is","value":null}]}]}""", false)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"genre","op":"is"}]}]}""", false)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"year","op":"between","value":[1990,""]}]}]}""", false)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"year","op":"between","value":[]}]}]}""", false)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"year","op":"is","value":0}]}]}""", true)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"watched","op":"is","value":false}]}]}""", true)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"year","op":"between","value":[1990,1999]}]}]}""", true)]
    [InlineData("""{"groups":[{"match":"all","rules":[{"field":"genre","op":"is","value":"Drama"}]}]}""", true)]
    public void CompleteRuleSurvivesTheWireRoundTrip(string json, bool expected)
    {
        // A saved definition arrives as decoded JSON (JsonElement values) and is cloned by the
        // editor before it is checked; both forms must agree with the server.
        var received = JsonSerializer.Deserialize<QueryDefinition>(json, V2Json.Options)!;
        Assert.Equal(expected, QueryEditing.HasCompleteRule(received));
        Assert.Equal(expected, QueryEditing.HasCompleteRule(QueryEditing.Clone(received)));
    }

    [Fact]
    public void WizardPopulationKeepsAnOpenRangeIncompleteBecauseOnlyBlankTextIsDropped()
    {
        var query = Rule("year", "between", new object[] { 1990d, "" });
        query.Groups = QueryEditing.PopulatedGroups(query);
        Assert.False(QueryEditing.HasCompleteRule(query));
        var blank = Rule("genre", "contains", "");
        blank.Groups = QueryEditing.PopulatedGroups(blank);
        Assert.Empty(blank.Groups);
        Assert.False(QueryEditing.HasCompleteRule(blank));
    }

    // ----- G1: dedicated Continue Watching / Next Up page -----

    [Theory]
    [InlineData("in_progress", "continue_watching")]
    [InlineData("continue_watching", "continue_watching")]
    [InlineData("next_up", "next_up")]
    [InlineData("recently_added", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ServerItemSourceMapsToTheHomeRowVocabulary(string? serverSource, string? expected)
        => Assert.Equal(expected, HomeDismissalPolicy.SurfaceForServerItemSource(serverSource));

    [Fact]
    public void SectionCardsKeepThePageSourceUnlessTheServerMarkedARemovableRow()
    {
        Assert.Equal("continue_watching", HomeDismissalPolicy.SectionCardItemSource("section", "in_progress"));
        Assert.Equal("next_up", HomeDismissalPolicy.SectionCardItemSource("section", "next_up"));
        // The row-type spelling maps to itself, so a card normalized once keeps its surface.
        Assert.Equal("continue_watching", HomeDismissalPolicy.SectionCardItemSource("section", "continue_watching"));
        Assert.Equal("section", HomeDismissalPolicy.SectionCardItemSource("section", null));
        Assert.Equal("section", HomeDismissalPolicy.SectionCardItemSource("section", "something_else"));
    }

    [Fact]
    public void DismissalNeedsTheIdentityTheRequestSends()
    {
        // Mirrors homeDismissalForCard: Continue Watching needs progress_updated_at, Next Up a series.
        Assert.True(HomeDismissalPolicy.CanDismiss(new MediaItem { ItemSource = "continue_watching", ProgressUpdatedAt = "2026-11-09T10:00:00Z" }));
        Assert.False(HomeDismissalPolicy.CanDismiss(new MediaItem { ItemSource = "continue_watching" }));
        Assert.False(HomeDismissalPolicy.CanDismiss(new MediaItem { ItemSource = "continue_watching", ProgressUpdatedAt = " " }));
        Assert.True(HomeDismissalPolicy.CanDismiss(new MediaItem { Type = "episode", ItemSource = "next_up", SeriesId = "show" }));
        Assert.False(HomeDismissalPolicy.CanDismiss(new MediaItem { ItemSource = "next_up", ProgressUpdatedAt = "2026-11-09T10:00:00Z" }));
        Assert.False(HomeDismissalPolicy.CanDismiss(new MediaItem { ItemSource = "section", ProgressUpdatedAt = "2026-11-09T10:00:00Z", SeriesId = "show" }));
    }

    [Fact]
    public void ADismissedShowLeavesEveryCardOfThatShow()
    {
        var episode = new MediaItem { ContentId = "show-s1e2", SeriesId = "show" };
        var series = new MediaItem { ContentId = "show", Type = "series" };
        var movie = new MediaItem { ContentId = "movie" };
        var other = new MediaItem { ContentId = "other-ep", SeriesId = "other" };

        Assert.True(HomeDismissalPolicy.IsRemovedBy(episode, "show-s1e2", "show"));
        Assert.True(HomeDismissalPolicy.IsRemovedBy(series, "show-s1e2", "show"));
        Assert.True(HomeDismissalPolicy.IsRemovedBy(new MediaItem { ContentId = "show-s1e3", SeriesId = "show" }, "show-s1e2", "show"));
        Assert.False(HomeDismissalPolicy.IsRemovedBy(other, "show-s1e2", "show"));
        Assert.False(HomeDismissalPolicy.IsRemovedBy(movie, "show-s1e2", "show"));
        // A movie has no series: only that title leaves.
        Assert.True(HomeDismissalPolicy.IsRemovedBy(movie, "movie", null));
        Assert.False(HomeDismissalPolicy.IsRemovedBy(other, "movie", null));
    }

    [Theory]
    [InlineData("audiobook")]
    [InlineData("ebook")]
    [InlineData("manga")]
    public void DismissingABookWithASeriesIdRemovesOnlyThatTitle(string type)
    {
        var selected = new MediaItem { ContentId = "book-one", Type = type, SeriesId = "books",
            ItemSource = HomeDismissalPolicy.ContinueWatching, ProgressUpdatedAt = "2026-11-09T10:00:00Z" };
        var sibling = new MediaItem { ContentId = "book-two", Type = type, SeriesId = "books" };
        var seriesId = HomeDismissalPolicy.SeriesIdentity(selected);
        Assert.Null(seriesId);
        Assert.True(HomeDismissalPolicy.CanDismiss(selected));
        Assert.True(HomeDismissalPolicy.IsRemovedBy(selected, selected.ContentId, seriesId));
        Assert.False(HomeDismissalPolicy.IsRemovedBy(sibling, selected.ContentId, seriesId));
        selected.ItemSource = HomeDismissalPolicy.NextUp;
        Assert.False(HomeDismissalPolicy.CanDismiss(selected));
    }

    [Fact]
    public void ShowIdentityUsesTheEpisodeParentOrTheSeriesContentId()
    {
        Assert.Equal("show", HomeDismissalPolicy.SeriesIdentity(new MediaItem { Type = "episode", ContentId = "ep", SeriesId = "show" }));
        Assert.Equal("show", HomeDismissalPolicy.SeriesIdentity(new MediaItem { Type = "series", ContentId = "show", SeriesId = "unrelated" }));
        Assert.True(HomeDismissalPolicy.CanDismiss(new MediaItem { Type = "series", ContentId = "show", ItemSource = HomeDismissalPolicy.NextUp }));
    }

    [Fact]
    public void SectionBrowseWireCarriesTheFieldsTheDismissalNeeds()
    {
        const string json = """
            {"items":[
              {"content_id":"film","type":"movie","title":"Film","item_source":"in_progress","progress_updated_at":"2026-11-09T10:00:00Z"},
              {"content_id":"ep","type":"episode","title":"Ep","series_id":"show","item_source":"next_up"}],
             "total":2,"has_more":false,"total_exact":true}
            """;
        var response = JsonSerializer.Deserialize<CatalogResponse>(json, V2Json.Options)!;
        foreach (var card in response.Items)
            card.ItemSource = HomeDismissalPolicy.SectionCardItemSource("section", card.ItemSource);

        Assert.Equal(["continue_watching", "next_up"], response.Items.Select(card => card.ItemSource));
        Assert.All(response.Items, card => Assert.True(HomeDismissalPolicy.CanDismiss(card)));
    }

    // ----- G5: Trending worldwide Show -----

    [Theory]
    [InlineData(null, "Trending on TMDB this week, from what you have")]
    [InlineData("movie", "Movies trending on TMDB this week, from what you have")]
    [InlineData("series", "TV shows trending on TMDB this week, from what you have")]
    [InlineData("episode", "Trending on TMDB this week, from what you have")]
    public void TrendingWorldwideNamesItsKind(string? filterType, string expected)
    {
        var config = new Dictionary<string, object> { ["window"] = "week" };
        if (filterType != null) config["filter_type"] = filterType;
        Assert.Equal(expected, HomeRecipePresentation.Describe(new() { SectionType = "trending_discover", Config = config }));
    }

    [Fact]
    public void TrendingWorldwideKindReadsStoredJsonAndKeepsToday()
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, object>>("""{"window":"day","filter_type":"series"}""")!;
        Assert.Equal("TV shows trending on TMDB today, from what you have",
            HomeRecipePresentation.Describe(new() { SectionType = "trending_discover", Config = config }));
    }

    // ----- G9: digital and physical release badges -----

    [Theory]
    [InlineData("series_premiere", "Premiere")]
    [InlineData("season_premiere", "New Season")]
    [InlineData("finale", "Finale")]
    [InlineData("digital", "Digital")]
    [InlineData("physical", "Physical")]
    [InlineData("future_badge", "future_badge")]
    public void UpcomingBadgesHaveTheWebUiLabels(string badge, string label)
        => Assert.Equal(label, UpcomingBadgePresentation.Label(badge));

    [Fact]
    public void ReleaseBadgesUseTheWebUiTailwindPalettes()
    {
        // bg-*-950/90, border-*-800/55, text-*-50 (alpha 230 / 140 of 255).
        Assert.Equal(new UpcomingBadgePresentation.Palette(0xE6431407, 0x8C9A3412, 0xFFFFF7ED), UpcomingBadgePresentation.FixedPalette("finale"));
        Assert.Equal(new UpcomingBadgePresentation.Palette(0xE6082F49, 0x8C075985, 0xFFF0F9FF), UpcomingBadgePresentation.FixedPalette("digital"));
        Assert.Equal(new UpcomingBadgePresentation.Palette(0xE6022C22, 0x8C065F46, 0xFFECFDF5), UpcomingBadgePresentation.FixedPalette("physical"));
        Assert.Null(UpcomingBadgePresentation.FixedPalette("series_premiere"));
        Assert.Null(UpcomingBadgePresentation.FixedPalette("future_badge"));
    }

    // ----- G10: sidebar library icons -----

    [Theory]
    [InlineData("movies", "film")]
    [InlineData("movie", "film")]
    [InlineData("Movies", "film")]
    [InlineData("series", "tv")]
    [InlineData("tv", "tv")]
    [InlineData("show", "tv")]
    [InlineData("tvshows", "tv")]
    [InlineData("mixed", "layers")]
    [InlineData("audiobooks", "book-headphones")]
    [InlineData("audiobook", "book-headphones")]
    [InlineData("ebooks", "book-open")]
    [InlineData("ebook", "book-open")]
    [InlineData("manga", "book-marked")]
    [InlineData("podcasts", "podcast")]
    [InlineData("podcast", "podcast")]
    [InlineData(" Mixed ", "layers")]
    [InlineData("homevideos", "library")]
    [InlineData("", "library")]
    [InlineData(null, "library")]
    public void LibraryTypesResolveTheirAliasesToSidebarIcons(string? type, string icon)
        => Assert.Equal(icon, LibraryTypeIcons.IconName(type));

    [Theory]
    [InlineData("movies"), InlineData("series"), InlineData("mixed"), InlineData("audiobooks"),
     InlineData("ebooks"), InlineData("manga"), InlineData("podcasts")]
    public void EveryMappedSidebarIconShipsInTheApp(string type)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "SiloPlayer", "Assets", "Icons", LibraryTypeIcons.IconName(type) + ".svg");
        Assert.True(File.Exists(path), path);
        Assert.Contains("<svg", File.ReadAllText(path));
    }

    // ----- G12: episode links keep the library only for an episode with a file -----

    [Fact]
    public void AnEpisodeWithNoFileOpensWithoutTheLibrary()
    {
        var withFile = new Episode { ContentId = "ep1", Files = [new EpisodeFile { FileId = 1 }] };
        var withoutFile = new Episode { ContentId = "ep2", Files = [] };
        Assert.Equal(7, EpisodeLinkLibraryPolicy.LibraryFor(withFile, 7));
        Assert.Null(EpisodeLinkLibraryPolicy.LibraryFor(withoutFile, 7));
        Assert.Null(EpisodeLinkLibraryPolicy.LibraryFor(withFile, null));
        Assert.Null(EpisodeLinkLibraryPolicy.LibraryFor(withoutFile, null));
    }

    [Fact]
    public void EpisodeListWireWithoutFilesDropsTheLibrary()
    {
        const string json = """
            {"episodes":[
              {"content_id":"a","season_number":1,"episode_number":1,"files":[{"file_id":4,"resolution":"1080p"}]},
              {"content_id":"b","season_number":1,"episode_number":2,"files":[]},
              {"content_id":"c","season_number":1,"episode_number":3}]}
            """;
        var episodes = JsonSerializer.Deserialize<EpisodesResponse>(json, V2Json.Options)!.Episodes;
        Assert.Equal(new int?[] { 3, null, null }, episodes.Select(episode => EpisodeLinkLibraryPolicy.LibraryFor(episode, 3)).ToArray());
    }

    // ----- Source wiring: each surface reaches the shared rule instead of a copy -----

    [Fact]
    public void SurfacesUseTheSharedRules()
    {
        var root = RepositoryRoot();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, "src", .. parts]));
        var poster = Source("SiloPlayer", "Controls", "PosterCard.xaml.cs");
        Assert.DoesNotContain("MediaItemMenu.Surface.Default", poster);
        Assert.Equal(2, poster.Split("MediaItemMenu.SurfaceFor(item)").Length - 1);
        var catalog = Source("SiloPlayer", "Views", "CatalogPage.xaml.cs");
        Assert.Contains("HomeDismissalPolicy.SectionCardItemSource(_source, item.ItemSource)", catalog);
        Assert.Contains("MediaSurfaceChangeKind.HomeDismissed", catalog);
        Assert.Contains("IRecipient<HomeDismissalApplied>", catalog);
        Assert.Contains("UndoDismissalAsync", catalog);
        Assert.Contains("AutoHideUndoBannerAsync", catalog);
        Assert.Contains("ClearUndoState", catalog);
        // An Undo accepted on the dedicated page tells Home, whose rows the dismissal emptied.
        Assert.Contains("WeakReferenceMessenger.Default.Send(new HomeDismissalUndone(surface, itemId, owner))", catalog);
        var home = Source("SiloPlayer", "ViewModels", "HomeViewModel.cs");
        Assert.Contains("IRecipient<HomeDismissalUndone>", home);
        Assert.Contains("WeakReferenceMessenger.Default.Register<HomeDismissalUndone>(this)", home);
        Assert.Contains("_progressRemovals.Remove(message.ItemId)", home);
        Assert.Contains("QueueRealtimeRefresh(\"home_dismissal_undo\", message.Context)", home);
        var catalogXaml = Source("SiloPlayer", "Views", "CatalogPage.xaml");
        Assert.Contains("x:Name=\"UndoBanner\"", catalogXaml);
        Assert.Contains("x:Name=\"UndoButton\"", catalogXaml);
        Assert.Contains("LibraryTypeIcons.IconName(type)", Source("SiloPlayer", "MainWindow.xaml.cs"));
        Assert.Contains("UpcomingBadgePresentation.Label(badge)", Source("SiloPlayer", "Views", "CalendarPage.xaml.cs"));
        Assert.Contains("UpcomingBadgePresentation.FixedPalette(badge)", Source("SiloPlayer", "Views", "CalendarPage.xaml.cs"));
        Assert.Contains("EpisodeLinkLibraryPolicy.LibraryFor(episode, ViewModel.LibraryId)", Source("SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        // Smart collection editor and wizard both gate preview and save on the server's predicate.
        var editor = Source("SiloPlayer", "ViewModels", "CollectionEditorViewModel.cs");
        Assert.Contains("QueryEditing.HasCompleteRule(RuleDefinition)", editor);
        Assert.Contains("SyncSkippedTitles = null", editor);
        Assert.Contains("if (SmartNeedsRule) { ClearPreview(); return; }", Source("SiloPlayer", "ViewModels", "CollectionEditorViewModel.Preview.cs"));
        var wizard = Source("SiloPlayer", "ViewModels", "SmartCollectionWizardViewModel.cs");
        Assert.Contains("QueryEditing.HasCompleteRule(_previewQuery)", wizard);
        Assert.Contains("if (NeedsRule)", wizard);
        Assert.Contains("!ViewModel.NeedsRule", Source("SiloPlayer", "Views", "SmartCollectionWizardPage.xaml.cs"));
        // Trending worldwide offers the Show choice that writes the optional filter_type.
        var recipes = Source("SiloPlayer", "Views", "Dialogs", "RecipeGalleryDialog.cs");
        Assert.Contains("case \"trending_discover\":", recipes);
        Assert.Contains("SetOptional(config, \"filter_type\", value)", recipes);
    }

    // ----- G11: failed sync clears the skipped-title count -----

    [Fact]
    public void FailedSyncClearCodeMatchesWebUiErrorCallback()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "CollectionEditorViewModel.cs"));
        var catchIndex = source.IndexOf("Sync failed: {ex.Message}", StringComparison.Ordinal);
        Assert.True(catchIndex > 0, "the catch block with the error message exists");
        var clearIndex = source.IndexOf("SyncSkippedTitles = null", catchIndex - 200, StringComparison.Ordinal);
        Assert.True(clearIndex > 0 && clearIndex < catchIndex, "SyncSkippedTitles is cleared before the error message in the catch block");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
