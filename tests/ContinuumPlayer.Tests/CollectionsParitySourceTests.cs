namespace ContinuumPlayer.Tests;

public sealed class CollectionsParitySourceTests
{
    [Fact]
    public void CollectionModelsExposeImportedCollectionFields()
    {
        var root = FindRepositoryRoot();
        var collectionModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Collections", "Collection.cs"));
        var createModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Collections", "CreateCollectionRequest.cs"));
        var updateModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Collections", "UpdateCollectionRequest.cs"));

        Assert.Contains("Description", collectionModel);
        Assert.Contains("SourceUrl", collectionModel);
        Assert.Contains("LastSyncStatus", collectionModel);
        Assert.Contains("ItemCount", collectionModel);
        Assert.Contains("IncludeInServerCollections", collectionModel);
        Assert.Contains("PosterUrl", collectionModel);
        Assert.Contains("PosterThumbhash", collectionModel);

        Assert.Contains("IncludeInServerCollections", createModel);
        Assert.Contains("PosterSourceUrl", createModel);
        Assert.Contains("MaxItems", updateModel);
        Assert.Contains("SourceUrl", updateModel);
    }

    [Fact]
    public void CollectionsApiExposesUserImportTemplateAndSyncEndpoints()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "CollectionsApi.cs"));

        Assert.Contains("/api/v1/collections/templates", api);
        Assert.Contains("/api/v1/collections/import/mdblist/search", api);
        Assert.Contains("/api/v1/collections/import/mdblist/top", api);
        Assert.Contains("/api/v1/collections/import/mdblist", api);
        Assert.Contains("/api/v1/collections/import/tmdb", api);
        Assert.Contains("/api/v1/collections/import/trakt", api);
        Assert.Contains("/sync", api);
        Assert.Contains("/image?type=poster", api);
    }

    [Fact]
    public void CollectionsPageLabelsAndSyncsImportedCollections()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "CollectionsPage.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "CollectionsViewModel.cs"));

        Assert.Contains("FormatCollectionType", page);
        Assert.Contains("CanSyncCollection", page);
        Assert.Contains("Sync now", page);
        Assert.Contains("ItemCount", page);
        Assert.Contains("SyncCollectionAsync", vm);
    }

    [Fact]
    public void CollectionsPageExposesFullUserTemplateImportFlow()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "CollectionsPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "CollectionsPage.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "CollectionsViewModel.cs"));

        Assert.Contains("BrowseTemplatesButton", xaml);
        Assert.Contains("Browse Templates", xaml);
        Assert.Contains("Start from a template", xaml);

        Assert.Contains("ShowCollectionTemplateGalleryAsync", page);
        Assert.Contains("BuildTemplateGalleryDialog", page);
        Assert.Contains("BuildTemplateConfigPanel", page);
        Assert.Contains("BuildMDBListBrowser", page);
        Assert.Contains("ImportTemplateAsync", page);

        Assert.Contains("TemplateGroups", vm);
        Assert.Contains("TemplateImportDraft", vm);
        Assert.Contains("LoadTemplateFlowAsync", vm);
        Assert.Contains("SearchMDBListAsync", vm);
        Assert.Contains("LoadTopMDBListAsync", vm);
        Assert.Contains("ImportTemplateAsync", vm);
    }

    [Fact]
    public void CollectionEditorExposesImportedCollectionFields()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "CollectionEditorPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "CollectionEditorPage.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "CollectionEditorViewModel.cs"));

        Assert.Contains("DescriptionTextBox", xaml);
        Assert.Contains("ImportedSourceSection", xaml);
        Assert.Contains("SourceUrlTextBox", xaml);
        Assert.Contains("MaxItemsTextBox", xaml);
        Assert.Contains("IncludeInServerCollectionsToggle", xaml);
        Assert.Contains("SyncScheduleTextBlock", xaml);

        Assert.Contains("ImportedSourceSection.Visibility", page);
        Assert.Contains("IsImportedCollection", page);

        Assert.Contains("Description", vm);
        Assert.Contains("SourceUrl", vm);
        Assert.Contains("MaxItemsText", vm);
        Assert.Contains("SyncSchedule", vm);
        Assert.Contains("IncludeInServerCollections", vm);
        Assert.Contains("PosterSourceUrl", vm);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
