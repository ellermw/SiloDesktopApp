namespace SiloPlayer.Tests;

public class AdminHistoryImportParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminHistoryImportPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminHistoryImportPage.xaml.cs"));

    private static string Model => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer.Core", "Models", "HistoryImport", "HistoryImportSource.cs"));

    private static string MappingModel => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer.Core", "Models", "HistoryImport", "HistoryImportMapping.cs"));

    private static string RunModel => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer.Core", "Models", "HistoryImport", "HistoryImportRun.cs"));

    [Fact]
    public void HistoryImportUsesCurrentHeadingAndSourceBar()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("external servers into Silo user profiles", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceUrlText\"", Markup, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Set API key\"", Markup, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Edit server\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoSourcesCard\"", Markup, StringComparison.Ordinal);
        Assert.Contains("No source servers", Markup, StringComparison.Ordinal);
        Assert.Contains("Add the Jellyfin, Emby, or Plex server", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MissingTokenCallout\"", Markup, StringComparison.Ordinal);
        Assert.Contains("No admin API key configured", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryImportUsesRealApiKeyStatusAndCurrentMappingEmptyState()
    {
        Assert.Contains("public bool HasAdminToken", Model, StringComparison.Ordinal);
        Assert.Contains("_selectedSource.HasAdminToken", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("API key configured", Markup, StringComparison.Ordinal);
        Assert.Contains("No user mappings yet. Discover users on the server to create mappings.", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Discover users\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentServerMappingContractUsesSiloFieldNames()
    {
        Assert.Contains("[JsonPropertyName(\"external_user_name\")]", MappingModel, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"silo_user_id\")]", MappingModel, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"silo_username\")]", MappingModel, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"silo_profile_id\")]", MappingModel, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"silo_profile_name\")]", MappingModel, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverUsersRequiresExplicitUserAndProfileMapping()
    {
        Assert.Contains("Map {capturedUser.Name} to a Silo user and profile", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Save mapping", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("GetUserProfilesAsync", CodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoMatch", CodeBehind, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecentImportsMatchCurrentFiltersDetailsAndCancellation()
    {
        Assert.Contains("x:Name=\"RunFilterAll\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RunFilterAdmin\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RunFilterUser\"", Markup, StringComparison.Ordinal);
        Assert.Contains("_expandedRunId", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("CancelRunAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Watchlist", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Unmatched samples", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("No issues.", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int UserId", RunModel, StringComparison.Ordinal);
        Assert.Contains("public int WatchlistAdded", RunModel, StringComparison.Ordinal);
        Assert.Contains("public int FavoritesImported", RunModel, StringComparison.Ordinal);
        Assert.Contains("Favorites", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("run.FavoritesImported", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationCancelsPageAndSelectionLoads()
    {
        Assert.Contains("_isPageActive = false", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_pageLoadCts?.Cancel()", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_selectionLoadCts?.Cancel()", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_runsLoadCts?.Cancel()", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MappingFailureKeepsTheEmptyStateAndDoesNotBlockRunHistory()
    {
        Assert.Contains("_mappings = [];", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Match the WebUI's query behavior", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_runs = await _adminApi.GetAdminRunsAsync", CodeBehind, StringComparison.Ordinal);
    }
}
