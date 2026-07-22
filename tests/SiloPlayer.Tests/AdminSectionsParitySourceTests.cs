namespace SiloPlayer.Tests;

public class AdminSectionsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSectionsPage.xaml.cs"));

    private static string ViewModelSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminSectionsViewModel.cs"));

    [Fact]
    public void SectionsPaintsSkeletonRowsBeforeEveryScopeLoad()
    {
        Assert.True(PageSource.Split("BuildLoadingSkeletons();", StringSplitOptions.None).Length >= 4);
        Assert.Contains("if (ViewModel.IsLoading) return;", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionsLoadsReferenceDataAndRowsInParallel()
    {
        Assert.Contains("var librariesTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var recipesTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var collectionsTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("var sectionsTask", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("await Task.WhenAll(requests)", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestScopeSelectionWinsOverOlderRequests()
    {
        Assert.Contains("Interlocked.Increment(ref _loadVersion)", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (loadVersion != _loadVersion) return;", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryDoesNotRequireAnUndefinedComboBoxResource()
    {
        Assert.DoesNotContain(
            "Application.Current.Resources[\"DarkComboBoxStyle\"]",
            PageSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryUsesCenteredPageOverlayForThreeRecipeColumns()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSectionsPage.xaml"));
        Assert.Contains("x:Name=\"GalleryOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"800\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GalleryOverlayContent.Content = content", PageSource, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(card, index % 3)", PageSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy(choice => choice.DisplayName", PageSource, StringComparison.Ordinal);
        Assert.Contains("ScoreGalleryChoice(query, choice)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Content = \"Clear filters\"", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GallerySupportsCurrentRecipeSpecificConfiguration()
    {
        foreach (var key in new[]
        {
            "user_collection_id", "filter_library_ids", "enabled_themes", "theme_titles",
            "subject_type", "auto_rotate", "rotation_cadence", "anchor_item_id", "item_ids"
        })
            Assert.Contains($"\"{key}\"", PageSource, StringComparison.Ordinal);

        Assert.Contains("SearchCuratedCatalogAsync", PageSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.UserCollections", PageSource, StringComparison.Ordinal);
        Assert.Contains("_collectionsApi.GetCollectionsAsync()", ViewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionEditorDistinguishesUserAndLibraryCollections()
    {
        Assert.Contains("GetConfigUserCollectionId(existing)", PageSource, StringComparison.Ordinal);
        Assert.Contains("new GalleryCollectionChoice(c.Id, IsUserCollection: true)", PageSource, StringComparison.Ordinal);
        Assert.Contains("selectedCollection.IsUserCollection ? \"user_collection_id\" : \"library_collection_id\"", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardSectionEditorUsesRecipeSpecificFieldsToo()
    {
        Assert.Contains("BuildRecipeParameterEditor(selType, seed)", PageSource, StringComparison.Ordinal);
        Assert.Contains("activeRecipeEditor.GetConfig()", PageSource, StringComparison.Ordinal);
        Assert.Contains("BuildCuratedRecipeFields(panel, config)", PageSource, StringComparison.Ordinal);
        Assert.Contains("sectionType is \"watchlist\" or \"favorites\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("sectionType == \"seasonal_themed\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("sectionType == \"editorial_spotlight\"", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFilterEditorSupportsEasyAndAdvancedRuleGroups()
    {
        Assert.Contains("BuildLegacyFilterEditor(filterSeed", PageSource, StringComparison.Ordinal);
        Assert.Contains("Content = \"Easy\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("Content = \"Advanced\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("Content = \"+ Add group\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("[\"op\"] = rule.Operator", PageSource, StringComparison.Ordinal);
        Assert.Contains("[\"sort\"] = new Dictionary<string, object?>", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardEditorDefaultsAndValidationMatchWebUi()
    {
        Assert.Contains("existing?.SectionType ?? \"recently_added\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("editorCategoryLabels", PageSource, StringComparison.Ordinal);
        Assert.Contains("title = ViewModel.RecipeLabels.GetValueOrDefault", PageSource, StringComparison.Ordinal);
        Assert.Contains("if (selectedCollection == null) return null", PageSource, StringComparison.Ordinal);
        Assert.Contains("SaveSectionEditorButton.IsEnabled = selType != \"collection\"", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CuratedRecipeRequiresItemsAndHydratesSavedLabels()
    {
        Assert.Contains("selected.Definition.Type != \"admin_curated_list\" || curatedItems.Count > 0", PageSource, StringComparison.Ordinal);
        Assert.Contains("HydrateCuratedItemLabelsAsync", PageSource, StringComparison.Ordinal);
        Assert.Contains("GetItemDetailAsync(pendingItem.Id)", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionEditorUsesCurrentDebouncedLivePreviewContract()
    {
        var api = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));
        var model = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Models", "Admin", "AdminSection.cs"));

        Assert.Contains("/api/v1/admin/sections/preview", api, StringComparison.Ordinal);
        Assert.Contains("AdminSectionPreviewRequest", model, StringComparison.Ordinal);
        Assert.Contains("AdminSectionPreviewResponse", model, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(300)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Preview · {preview.TotalCount} items match", PageSource, StringComparison.Ordinal);
        Assert.Contains("AttachSectionPreviewTriggers", PageSource, StringComparison.Ordinal);
        Assert.Contains("ExtractPreviewLibraryIds", PageSource, StringComparison.Ordinal);
        Assert.Contains("preview.Items.Take(10)", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionsUsesResponsiveWebUiCanvasAndSingleGlobalFeedbackSurface()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSectionsPage.xaml"));

        Assert.Contains("x:Name=\"SectionsPageShell\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1400\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout(ActualWidth)", PageSource, StringComparison.Ordinal);
        Assert.Contains("_toastService.Success(message)", PageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"StatusBanner\"", xaml, StringComparison.Ordinal);
    }
}
