using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Tests;

public sealed class CollectionsParitySourceTests
{
    [Fact]
    public void LibraryTabResponseDeserializesGroupedAndUserCollections()
    {
        var json = """
        {
          "library_id": 12,
          "groups": [
            {
              "id": "group-user",
              "name": "User Collections",
              "kind": "user_collections",
              "sort_mode": "manual",
              "sort_order": 1,
              "collections": [
                {
                  "id": "user-1",
                  "title": "Mike's Picks",
                  "poster_url": "https://example.test/user.jpg",
                  "poster_thumbhash": "abc",
                  "item_count": 7,
                  "creator_profile_id": "profile-1"
                }
              ]
            }
          ],
          "ungrouped": {
            "sort_order": 99,
            "collections": [
              {
                "id": "admin-1",
                "title": "Top Movies",
                "poster_url": "https://example.test/admin.jpg",
                "item_count": 42,
                "featured": true
              }
            ]
          }
        }
        """;

        var response = JsonSerializer.Deserialize<LibraryTabResponse>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        Assert.NotNull(response);
        Assert.Equal(12, response.LibraryId);
        Assert.Single(response.Groups);
        Assert.Equal("user_collections", response.Groups[0].Kind);
        Assert.Single(response.Groups[0].Collections);
        Assert.Equal("Mike's Picks", response.Groups[0].Collections[0].Title);
        Assert.NotNull(response.Ungrouped);
        Assert.Single(response.Ungrouped.Collections);
        Assert.True(response.Ungrouped.Collections[0].Featured);
    }

    [Fact]
    public void LibraryCollectionsTabUsesGroupedServerResponse()
    {
        var root = FindRepositoryRoot();
        var catalogApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "CatalogApi.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "LibraryViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LibraryPage.xaml.cs"));

        Assert.Contains("Task<LibraryTabResponse> GetLibraryCollectionsAsync", catalogApi);
        Assert.Contains("CollectionSections", viewModel);
        Assert.Contains("LibraryTabSection", viewModel);
        Assert.Contains("IsUserCollection", page);
        Assert.Contains("BuildCollectionSection", page);
    }

    [Fact]
    public void CollectionModelsExposeImportedCollectionFields()
    {
        var root = FindRepositoryRoot();
        var collectionModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Collections", "Collection.cs"));
        var createModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Collections", "CreateCollectionRequest.cs"));
        var updateModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Collections", "UpdateCollectionRequest.cs"));

        Assert.Contains("Description", collectionModel);
        Assert.Contains("SourceUrl", collectionModel);
        Assert.Contains("LastSyncStatus", collectionModel);
        Assert.Contains("ItemCount", collectionModel);
        Assert.Contains("IncludeInServerCollections", collectionModel);
        Assert.Contains("PosterUrl", collectionModel);
        Assert.Contains("PosterThumbhash", collectionModel);
        Assert.Contains("DisplayQueryDefinition", collectionModel);

        Assert.Contains("IncludeInServerCollections", createModel);
        Assert.Contains("PosterSourceUrl", createModel);
        Assert.Contains("MaxItems", updateModel);
        Assert.Contains("SourceUrl", updateModel);
        Assert.Contains("LibraryIds", updateModel);
        Assert.Contains("DisplayQueryDefinition", updateModel);
    }

    [Fact]
    public void CollectionsApiExposesUserImportTemplateAndSyncEndpoints()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "CollectionsApi.cs"));

        Assert.Contains("/api/v1/collections/templates", api);
        Assert.Contains("/api/v1/collections/import/mdblist/search", api);
        Assert.Contains("/api/v1/collections/import/mdblist/top", api);
        Assert.Contains("/api/v1/collections/import/mdblist", api);
        Assert.Contains("/api/v1/collections/import/tmdb", api);
        Assert.Contains("/api/v1/collections/import/trakt", api);
        Assert.Contains("/sync", api);
        Assert.Contains("/image?type=poster", api);
        Assert.Contains("/collections/capabilities", api);
        Assert.Contains("PutJsonWithFileAsync", api);
    }

    [Fact]
    public void CollectionsPageLabelsAndSyncsImportedCollections()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs"));

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
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionsPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs"));

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
    public void AdminCollectionsUsesCurrentAdminTemplatesAndBundleApplyFlow()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminCollectionsPage.xaml.cs"));
        var api = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));
        var models = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Collections", "CollectionImports.cs"));

        Assert.Contains("ShowAdminTemplateGalleryAsync", page);
        Assert.Contains("ShowBundleApplyViewAsync", page);
        Assert.Contains("ShowAdminTemplateConfigAsync", page);
        Assert.Contains("RenderBundleResult", page);
        Assert.DoesNotContain("Navigate<CollectionsPage>(new CollectionsNavigationArgs(OpenTemplates", page);

        Assert.Contains("/api/v1/admin/collections/templates", api);
        Assert.Contains("/api/v1/admin/collections/template-bundles", api);
        Assert.Contains("/apply-job", api);
        Assert.Contains("CollectionTemplateBundleCatalog", models);
        Assert.Contains("ApplyCollectionTemplateBundleFeaturedRequest", models);
        Assert.Contains("CollectionTemplateTmdbDiscoverSpec", models);
    }

    [Fact]
    public void SmartCollectionWizardIsAvailableForUserAndAdminCollections()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var collectionsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionsPage.xaml"));
        var collectionsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs"));
        var adminCollectionsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminCollectionsPage.xaml.cs"));
        var wizardPage = Path.Combine(root, "src", "SiloPlayer", "Views", "SmartCollectionWizardPage.xaml.cs");
        var wizardViewModel = Path.Combine(root, "src", "SiloPlayer", "ViewModels", "SmartCollectionWizardViewModel.cs");

        Assert.True(File.Exists(wizardPage));
        Assert.True(File.Exists(wizardViewModel));
        Assert.Contains("SmartCollectionWizardViewModel", app);
        Assert.Contains("SmartCollectionWizardPage", documentTitle);
        Assert.Contains("New Collection", collectionsXaml);
        Assert.DoesNotContain("Smart Wizard", collectionsXaml);
        Assert.Contains("Navigate<SmartCollectionWizardPage>", collectionsPage);
        Assert.Contains("Navigate<SmartCollectionWizardPage>", adminCollectionsPage);
        Assert.Contains("MediaScope", File.ReadAllText(wizardViewModel));
        Assert.Contains("\"episode\"", File.ReadAllText(wizardViewModel));
    }

    [Fact]
    public void AdminCollectionGroupsCanBeManagedAndReordered()
    {
        var root = FindRepositoryRoot();
        var adminApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminCollectionsViewModel.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminCollectionsPage.xaml.cs"));

        Assert.Contains("GetCollectionGroupsAsync", adminApi);
        Assert.Contains("CreateCollectionGroupAsync", adminApi);
        Assert.Contains("UpdateCollectionGroupAsync", adminApi);
        Assert.Contains("DeleteCollectionGroupAsync", adminApi);
        Assert.Contains("ReorderCollectionGroupsAsync", adminApi);
        Assert.Contains("ReorderCollectionsInGroupAsync", adminApi);
        Assert.Contains("CollectionGroups", viewModel);
        Assert.Contains("MoveCollectionInGroupAsync", viewModel);
        Assert.Contains("BuildCollectionGroupBoard", page);
        Assert.Contains("OpenCreateGroupDialogAsync", page);
        Assert.Contains("MoveGroupAsync", page);
    }

    [Fact]
    public void CollectionEditorExposesImportedCollectionFields()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionEditorPage.xaml"));
        var page = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionEditorPage.xaml.cs"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "CollectionEditorViewModel.cs"));

        Assert.Contains("DescriptionTextBox", xaml);
        Assert.Contains("ImportedSourceSection", xaml);
        Assert.Contains("SourceUrlTextBox", xaml);
        Assert.Contains("MaxItemsTextBox", xaml);
        Assert.Contains("IncludeInServerCollectionsToggle", xaml);
        Assert.Contains("SyncScheduleTextBlock", xaml);
        Assert.Contains("ImportedLibrariesPanel", xaml);
        Assert.Contains("WatchFilterCombo", xaml);
        Assert.Contains("MediaFilterCombo", xaml);
        Assert.Contains("ImportedProfilesPanel", xaml);
        Assert.Contains("ChoosePoster_Click", page);
        Assert.Contains("SyncNow_Click", page);
        Assert.Contains("DeleteCollection_Click", page);

        Assert.Contains("ImportedSourceSection.Visibility", page);
        Assert.Contains("IsImportedCollection", page);

        Assert.Contains("Description", vm);
        Assert.Contains("SourceUrl", vm);
        Assert.Contains("MaxItemsText", vm);
        Assert.Contains("SyncSchedule", vm);
        Assert.Contains("IncludeInServerCollections", vm);
        Assert.Contains("PosterSourceUrl", vm);
        Assert.Contains("SelectedLibraryIds", vm);
        Assert.Contains("AllowedProfileIds", vm);
        Assert.Contains("BuildDisplayQueryDefinition", vm);
        Assert.Contains("SyncNowAsync", vm);
        Assert.Contains("RemovePosterAsync", vm);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
