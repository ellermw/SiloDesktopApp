namespace SiloPlayer.Tests;

/// <summary>
/// Wiring checks for the Collections wall and the collection page hero that behavior tests cannot reach.
/// The real controls are exercised by the collections-presentation native fixture.
/// </summary>
public sealed class CollectionsPresentationSourceTests
{
    [Fact]
    public void WallIsOneFullWidthPosterGridFollowingThePosterSizeSetting()
    {
        var wall = Read("src", "SiloPlayer", "Views", "CollectionsPage.Wall.cs");
        var posters = Read("src", "SiloPlayer", "Views", "CollectionsPage.Posters.cs");
        var page = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");
        Assert.Contains("UICustomizationService.PosterColumnCount(PosterSize, ContentWidth)", wall);
        Assert.Contains("PosterSize == \"large\" ? 16 : 12", wall);
        Assert.Contains("ColumnSpacing = PosterGap", posters);
        Assert.Contains("Changed -= PosterSizeChanged", page);   // the wall re-flows when the viewer changes the setting
        Assert.DoesNotContain("CollectionsPageShell.MaxWidth", page);
        // The retired fixed 7/5/3 columns and the capped shell are gone.
        Assert.DoesNotContain("ActualWidth >= 1024 ? 7 : ActualWidth >= 640 ? 5 : 3", wall + posters);
        Assert.DoesNotContain("CollectionsPageShell.Width", page);
    }

    [Fact]
    public void EmptyCollectionsPageHasNoDashedNewCollectionTileAndOneCreateAction()
    {
        var posters = Read("src", "SiloPlayer", "Views", "CollectionsPage.Posters.cs");
        var wall = Read("src", "SiloPlayer", "Views", "CollectionsPage.Wall.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml");
        Assert.DoesNotContain("NewCollectionCard", posters + wall);
        Assert.DoesNotContain("StrokeDashArray", posters + wall);
        Assert.Contains("x:Name=\"CreateButton\"", xaml);
        Assert.Contains("CollectionsWall.EmptyMessage(", wall);
        Assert.Contains("ViewModel.PersonalLoaded, ViewModel.ProfilesLoaded, ViewModel.ServerLoaded", wall);
    }

    [Fact]
    public void WallKeepsServerPaginationOwnerAndAccessSemantics()
    {
        var wall = Read("src", "SiloPlayer", "Views", "CollectionsPage.Wall.cs");
        var vm = Read("src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs");
        Assert.Contains("ViewModel.GetServerCollectionCards(picked?.LibraryId)", wall);
        Assert.Contains("\"Load more server collections\"", wall);
        Assert.Contains("ViewModel.NextServerLibrary", wall);
        Assert.Contains("BuildSharedCollectionCard(entry.Collection, entry.Owner)", wall);
        Assert.Contains("PersonalCollectionOwnership.Split(", wall);
        Assert.Contains("_loadGeneration", vm);
        Assert.Contains("Task.WhenAll(PersonalAsync(), ServerAsync())", vm);   // neither read waits for the other
    }

    [Fact]
    public void CollectionPageUsesTheHeroHeaderWithOwnerActionsAndTheCountInTheFilterBar()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml");
        var header = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.Header.cs");
        var page = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml.cs");
        Assert.Contains("controls:CollectionHeroBackdrop", xaml);
        Assert.Contains("Text=\"COLLECTION\"", xaml);
        Assert.Contains("controls:ExpandableDescription", xaml);
        Assert.Contains("x:Name=\"CollectionEditButton\"", xaml);
        Assert.Contains("x:Name=\"CollectionAddToHomeButton\"", xaml);
        Assert.DoesNotContain("Refine the archive", xaml + page);
        Assert.DoesNotContain("x:Name=\"CountPanel\"", xaml);
        Assert.Contains("\"title\" : \"titles\"", page);
        Assert.Contains("CollectionPageHeaderLoader.LoadAsync", header);
        Assert.Contains("_currentArgs != args", header);                       // navigation ownership
        Assert.Contains("_apiClient.IsCurrentContext(context)", header);       // profile/account ownership
        Assert.Contains("ServerCollection: !args.IsUserCollection", header);
    }

    [Fact]
    public void AddToMyHomeCanNameAServerCollectionRow()
    {
        var args = Read("src", "SiloPlayer", "Views", "CustomizeHomePage.cs");
        var editor = Read("src", "SiloPlayer", "Views", "SettingsPage.HomeLayout.cs");
        Assert.Contains("bool ServerCollection = false", args);
        Assert.Contains("args.ServerCollection ? \"library_collection_id\" : \"user_collection_id\"", editor);
    }

    [Fact]
    public void HeroMoreActionsShareTheCardMenuPolicyAndConfirmation()
    {
        var actions = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.Actions.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml");
        var cards = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs");
        Assert.Contains("x:Name=\"CollectionMoreButton\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"More actions\"", xaml);
        // One policy, one set of words and one confirmation for the hero and the card menu.
        Assert.Contains("CollectionHeroActions.Menu(access)", actions);
        Assert.Contains("CollectionDeleteDialog.ShowAsync(", actions);
        Assert.Contains("CollectionDeleteDialog.ShowAsync(", cards);
        Assert.DoesNotContain("Delete \\\"{collection.Name}", cards);   // the confirmation text lives in one place
        Assert.Contains("CollectionHeroActions.CanSync(collection)", cards);
        Assert.Contains("CollectionHeroActions.SyncFailure(result)", viewModel);
        Assert.Contains("CollectionHeroActions.SyncFailure(result!)", actions);
        // Delete and Sync exist only on the owner's menu; a read-only viewer gets the library pages.
        Assert.Contains("plan.ShowSync", actions); Assert.Contains("plan.ShowDelete && access.Personal is { } own", actions);
        Assert.Contains("CustomizeHomeNavigationArgs(scope.Args.CollectionId, scope.Args.Title, library.Id, ServerCollection: !scope.Args.IsUserCollection)", actions);
        Assert.Contains("NavigateReplacingCurrentEntry<CollectionsPage>()", actions);
        Assert.DoesNotContain("Open in", actions);   // an administrator-only entry; Desktop has no administrator access to collections
    }

    [Fact]
    public void HeroActionsAreBoundToTheHeaderTheyWereBuiltFor()
    {
        var actions = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.Actions.cs");
        var header = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.Header.cs");
        var page = Read("src", "SiloPlayer", "Views", "CollectionBrowsePage.xaml.cs");
        Assert.Contains("scope.Generation == _navigationGeneration", actions);
        Assert.Contains("_currentArgs == scope.Args", actions);
        Assert.Contains("_apiClient.IsCurrentContext(scope.Context)", actions);
        Assert.Contains("!scope.Header.IsCancellationRequested", actions);
        foreach (var handler in new[] { "AddToLibraryPage", "SyncAsync", "DeleteAsync" })
        {
            var start = actions.IndexOf("private " + (handler == "AddToLibraryPage" ? "void " : "async Task ") + handler + "(", StringComparison.Ordinal);
            Assert.True(start >= 0, handler);
            Assert.Contains("if (!HeroCurrent(scope)", actions[start..(start + 220)]);
        }
        // A late sync result or a delete confirmed after the page was left cannot repaint or navigate another page.
        Assert.Contains("// The page was left, or the account changed, while the server worked", actions);
        Assert.Contains("if (!deleted || !HeroCurrent(scope)) return;", actions);
        Assert.Contains("if (!HeroCurrent(scope)) return \"This collection's page is no longer open.\";", actions);
        Assert.Contains("_hero = new HeroScope(args, navigationGeneration, context, owner);", header);
        Assert.Contains("ClearHeroActions();", page);   // leaving the page drops the menu and its status
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
