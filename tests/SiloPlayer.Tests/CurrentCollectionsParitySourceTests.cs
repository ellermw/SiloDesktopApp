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
        Assert.Contains("Move group earlier", code);
        Assert.Contains("GetServerCollectionsAsync", viewModel);
        Assert.Contains("CreateGroupAsync", viewModel);
        Assert.Contains("MoveCollectionToGroupAsync", viewModel);
    }

    [Fact]
    public void CollectionsApiCoversCurrentGroupAndServerRoutes()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "CollectionsApi.cs");

        Assert.Contains("/api/v1/collections/server", api);
        Assert.Contains("/api/v1/collections/groups", api);
        Assert.Contains("/api/v1/collections/groups/order", api);
        Assert.Contains("/api/v1/collections/order", api);
        Assert.Contains("[\"group_id\"] = groupId", api);
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
