namespace SiloPlayer.Tests;

public sealed class CurrentCollectionsParitySourceTests
{
    [Fact]
    public void CollectionsSurfaceUsesCurrentGroupedPersonalAndServerSections()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionsPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs");

        Assert.Contains("Your collections", xaml);
        Assert.Contains("Server collections", xaml);
        Assert.Contains("Curated shelves from across every library on this server.", xaml);
        Assert.Contains("Add group", xaml);
        Assert.DoesNotContain("Smart Wizard", xaml);
        Assert.Contains("BuildCollectionGroupSection", code);
        Assert.Contains("BuildServerCollectionRows", code);
        Assert.Contains("Move to group", code);
        Assert.Contains("GroupDragPrefix", code);
        Assert.Contains("DropGroupAsync", viewModel);
        Assert.Contains("DropCollectionAsync", viewModel);
        Assert.Contains("CollectionsLoadingRepeater", xaml);
        Assert.Contains("IsLoadingServerCollections", viewModel);
        Assert.Contains("GetServerCollectionsAsync", viewModel);
        Assert.Contains("CreateGroupAsync", viewModel);
        Assert.Contains("MoveCollectionToGroupAsync", viewModel);
    }

    [Fact]
    public void CollectionsApiCoversCurrentGroupAndServerRoutes()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CollectionsApi.cs");

        Assert.Contains("/api/v2/collections/server", api);
        Assert.Contains("/api/v2/collections/groups", api);
        Assert.Contains("/api/v2/collections/groups/order", api);
        Assert.Contains("/api/v2/collections/order", api);
        Assert.Contains("[\"group_id\"] = groupId", api);
    }

    [Fact]
    public void SmartCollectionWizardCoversCurrentFiltersSharingArtworkAndResponsiveFlow()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SmartCollectionWizardPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SmartCollectionWizardPage.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SmartCollectionWizardViewModel.cs");

        Assert.Contains("x:Name=\"ProfileAccessSection\"", xaml);
        Assert.DoesNotContain("AdminBackdropPanel", xaml);
        Assert.Contains("ChoosePoster_Click", xaml);
        Assert.Contains("SmartCollectionWizardPage_SizeChanged", code);
        Assert.Contains("SchedulePreview", code);
        Assert.Contains("(\"Dolby Vision\", \"dolby_vision\")", code);
        Assert.Contains("(\"between\", \"between\")", code);
        Assert.Contains("AllowedProfileIds = IsShared", viewModel);
        Assert.DoesNotContain("AdminApi", viewModel);
        Assert.DoesNotContain("IsAdmin", viewModel);
        Assert.Contains("Only the profile that created this collection can edit it.", viewModel);
    }

    [Fact]
    public void CollectionSurfacesReflowAndBrowseTheCompleteResultSet()
    {
        var listXaml = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionsPage.xaml");
        var listCode = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");
        var browseXaml = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml");
        var browseCode = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml.cs");
        var editorCode = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionEditorPage.xaml.cs");

        Assert.Contains("x:Name=\"CollectionsHeaderActions\"", listXaml);
        Assert.Contains("CollectionsPage_SizeChanged", listCode);
        Assert.Contains("x:Name=\"SortCombo\"", browseXaml);
        Assert.Contains("x:Name=\"MediaScopeCombo\"", browseXaml);
        Assert.Contains("x:Name=\"FiltersSheet\"", browseXaml);
        Assert.Contains("x:Name=\"LoadingPosterRepeater\"", browseXaml);
        Assert.Contains("ViewChanged=\"ContentScroll_ViewChanged\"", browseXaml);
        Assert.Contains("const int PageSize = 60", browseCode);
        Assert.Contains("LoadMoreAsync", browseCode);
        Assert.Contains("response.Total", browseCode);
        Assert.Contains("CollectionBrowsePage_SizeChanged", browseCode);
        Assert.Contains("BuildExtraRules", browseCode);
        Assert.Contains("SetCatalogGridLayout", browseCode);
        Assert.Contains("CollectionEditorPage_SizeChanged", editorCode);
        Assert.Contains("ImportedSourceBanner", ReadRepoFile("src", "SiloPlayer", "Views", "CollectionEditorPage.xaml"));
        Assert.Contains("EditorBodyGrid", ReadRepoFile("src", "SiloPlayer", "Views", "CollectionEditorPage.xaml"));
        Assert.Contains("manual-item:", editorCode);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
