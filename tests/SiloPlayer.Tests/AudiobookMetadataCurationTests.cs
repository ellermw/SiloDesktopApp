using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AudiobookMetadataCurationTests
{
    public static IEnumerable<object[]> ActionMatrix()
    {
        foreach (var type in new[] { "movie", "series", "season", "episode", "audiobook", "ebook", "manga", "box_set" })
        foreach (var actor in new[] { "admin", "curator", "viewer", "admin-child", "admin-child-curator", "unresolved-admin", "unresolved-curator", "unresolved-admin-curator", "profileless-admin", "profileless-curator", "signed-out" })
            yield return [type, actor];
    }

    [Theory]
    [MemberData(nameof(ActionMatrix))]
    public void CardAndDetailActionsFollowTypeAndResolvedProfile(string type, string actor)
    {
        var user = actor == "signed-out" ? null : new UserInfo
        {
            Role = actor.Contains("admin") ? "admin" : "user",
            Permissions = actor.Contains("curator") ? [AuthorizationPolicy.MetadataCuration] : [],
        };
        var unresolved = actor.StartsWith("unresolved");
        var profileless = actor.StartsWith("profileless");
        var profile = unresolved || profileless ? null : new Profile { IsPrimary = !actor.Contains("child") };
        var canCurate = AuthorizationPolicy.CanCurateMetadata(user, profile, !profileless);
        var expectedCurator = actor is "admin" or "curator" or "admin-child-curator" or "profileless-admin" or "profileless-curator";
        Assert.Equal(expectedCurator, canCurate);

        var matchable = type is "movie" or "series" or "audiobook";
        Assert.Equal(expectedCurator && matchable, ItemMaintenanceActionPolicy.CanEditCard(canCurate, type));
        Assert.Equal(expectedCurator && (matchable || type is "season" or "episode"), ItemMaintenanceActionPolicy.CanEditDetail(canCurate, type));
        var maintenance = ItemMaintenanceActionPolicy.Resolve(canCurate, type);
        Assert.Equal(expectedCurator && matchable, maintenance.CanMatch);
        Assert.Equal(expectedCurator && type != "box_set", maintenance.CanRefreshMetadata);
    }

    [Fact]
    public void AudiobookFieldsHaveTheUpstreamSectionsAndNoVideoControls()
    {
        Assert.Equal(new[] { "title", "sort_title", "original_title", "overview", "tagline", "content_rating" }, MetadataEditorFormPolicy.FieldsFor("audiobook", "general"));
        Assert.Equal(new[] { "year", "release_date" }, MetadataEditorFormPolicy.FieldsFor("audiobook", "dates"));
        Assert.Equal(new[] { "genres", "studios", "countries" }, MetadataEditorFormPolicy.FieldsFor("audiobook", "tags"));
        Assert.Empty(MetadataEditorFormPolicy.FieldsFor("audiobook", "ids"));
        Assert.False(MetadataEditorFormPolicy.CanEditImages("audiobook", isAdmin: true));
        Assert.False(MetadataEditorFormPolicy.CanEditImages("audiobook", isAdmin: false));
        Assert.False(MetadataEditorFormPolicy.CanResetToProvider("audiobook"));
        Assert.True(MetadataEditorFormPolicy.CanResetToProvider("movie"));
    }

    [Fact]
    public void AudiobookEditsLockTitleYearAndTagGroupsAndPreserveForeignLocks()
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "audiobook", Title = "Book", Year = 2019, ReleaseDate = "2019-01-02", Genres = ["Fiction"], LockedFields = [10, 42] });
        state.Set("title", "Edited Book");
        state.Set("year", 2001d);
        state.Set("release_date", "");
        state.Set("genres", new[] { "Fantasy" });
        state.Set("studios", new[] { "Publisher" });
        state.Set("countries", new[] { "UK" });

        var changes = state.Changes([10, 42]);
        Assert.Equal("Edited Book", changes["title"]);
        Assert.Equal(2001d, changes["year"]);
        Assert.Null(changes["release_date"]);
        Assert.Equal(new[] { 0, 2, 3, 8, 10, 13, 42 }, changes["locked_fields"]);
        Assert.Equal(new[] { "countries", "genres", "locked_fields", "release_date", "studios", "title", "year" }, changes.Keys.Order());
    }

    [Fact]
    public void ProductionSurfacesUseTheTestedPoliciesAndKeepTheAudiobookMoreButtonReachable()
    {
        var menu = Source("src/SiloPlayer/Controls/MediaItemMenu.cs");
        var page = Source("src/SiloPlayer/Views/ItemDetailPage.xaml.cs");
        var editor = Source("src/SiloPlayer/Views/Dialogs/EditMetadataDialog.cs");
        Assert.Contains("ItemMaintenanceActionPolicy.CanEditCard(canCurateMetadata, item.Type)", menu);
        Assert.Contains("ItemMaintenanceActionPolicy.CanEditDetail(", page);
        // The audiobook page is its own layout (BookLayout); its curator menu offers Edit (through the
        // shared fresh-open path), Match and Refresh, as the WebUI AudiobookContent menu does.
        var bookLayout = Source("src/SiloPlayer/Views/ItemDetailPage.BookLayout.cs");
        Assert.Contains("AudiobookMoreButton.Visibility = canCurate ? Visibility.Visible : Visibility.Collapsed;", bookLayout);
        Assert.Contains("edit.Click += async (_, _) => await OpenFreshEditMetadataAsync(item, edit);", bookLayout);
        Assert.Contains("match.Click += MatchButton_Click;", bookLayout);
        Assert.Contains("refresh.Click += async (_, _) => await ShowRefreshMetadataDialogAsync();", bookLayout);
        Assert.Contains("edit.Click += async (_, _) => await OpenFreshEditMetadataAsync(item, edit);", page);
        Assert.Contains("var detail = await App.Services.GetRequiredService<CatalogApi>().GetItemDetailAsync(item.ContentId, loading.Token);", page);
        Assert.Contains("BuildFields(general, \"general\")", editor);
        Assert.Contains("BuildFields(dates, \"dates\")", editor);
        Assert.Contains("MetadataEditorFormPolicy.FieldsFor(_item.Type, section)", editor);
        Assert.Contains("MetadataEditorFormPolicy.FieldsFor(item.Type, \"tags\")", editor);
        Assert.Contains("MetadataEditorFormPolicy.FieldsFor(item.Type, \"ids\").Count > 0", editor);
        Assert.Contains("MetadataEditorFormPolicy.CanEditImages(item.Type", editor);
        Assert.Contains("_reset.Visibility = MetadataEditorFormPolicy.CanResetToProvider(item.Type)", editor);
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine((directory ?? throw new InvalidOperationException("Repository root not found")).FullName, relative));
    }
}
