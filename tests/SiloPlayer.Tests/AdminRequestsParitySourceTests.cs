namespace SiloPlayer.Tests;

public class AdminRequestsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminRequestsPage.xaml.cs"));

    private static string PageMarkup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminRequestsPage.xaml"));

    [Fact]
    public void IntegrationsUseInlinePluginSchemaCardsInsteadOfRawJsonDialog()
    {
        Assert.Contains("RenderIntegrationEditors();", PageSource, StringComparison.Ordinal);
        Assert.Contains("BuildIntegrationSchema(", PageSource, StringComparison.Ordinal);
        Assert.Contains("BuildIntegrationSchemaField(", PageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugin configuration (JSON)", PageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EditIntegrationAsync", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationSchemaSupportsDynamicOptionsAndCurrentControlTypes()
    {
        Assert.Contains("LoadRequestIntegrationOptionsAsync", PageSource, StringComparison.Ordinal);
        Assert.Contains("field.DynamicOptions", PageSource, StringComparison.Ordinal);
        Assert.Contains("\"MULTI_SELECT\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("field.ShowWhen", PageSource, StringComparison.Ordinal);
        Assert.Contains("descriptor.Sections", PageSource, StringComparison.Ordinal);
        Assert.Contains("ApplyIntegrationExclusivity", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionCardsUseWebUiTwoColumnLayoutAndActions()
    {
        Assert.Contains("grid.ColumnDefinitions.Add", PageSource, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(card, index % 2)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Create connection", PageSource, StringComparison.Ordinal);
        Assert.Contains("Connection saved", PageSource, StringComparison.Ordinal);
        Assert.Contains("Delete connection", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueRowsPreserveWebUiLinksAndWrappedActions()
    {
        Assert.Contains("Frame.Navigate(typeof(AdminUserDetailPage), userId)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Navigate<ItemDetailPage>(contentId)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(decline, 1)", PageSource, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(retry, 1)", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAndOverridesMatchWebUiCardWidthAndControlLayout()
    {
        Assert.Contains("x:Name=\"SettingsPanel\" Visibility=\"Collapsed\" MaxWidth=\"768\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OverridesPanel\" Visibility=\"Collapsed\" MaxWidth=\"768\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("Always fulfill in both 1080p and 4K", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\" SelectionChanged=\"OverrideUserPicker_SelectionChanged\"", PageMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueUsesCurrentResponsiveCanvasAndHorizontalTableOverflow()
    {
        Assert.Contains("x:Name=\"RequestsPageShell\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QueueTableScroll\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1120\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("AdminRequestsPage_SizeChanged", PageSource, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout(ActualWidth)", PageSource, StringComparison.Ordinal);
        Assert.Contains("ApplyTwoColumnFormLayout(SettingsFieldsGrid, compact)", PageSource, StringComparison.Ordinal);
        Assert.Contains("ApplyTwoColumnFormLayout(OverrideFieldsGrid, compact)", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TabsUseWebUiNaturalWidthAndMutationsSurfaceFeedback()
    {
        Assert.Contains("<ColumnDefinition Width=\"Auto\" /><ColumnDefinition Width=\"Auto\" />", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("SurfaceViewModelMutationResult", PageSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonStyle = (Style)Application.Current.Resources[\"DestructiveButtonStyle\"]", PageSource, StringComparison.Ordinal);
        Assert.Contains("FixIntegrationHeaderButton", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginAdminFormModelCarriesConditionalSectionMetadata()
    {
        var model = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Models", "Plugins", "PluginCapability.cs"));

        Assert.Contains("PluginAdminFormSection", model, StringComparison.Ordinal);
        Assert.Contains("DynamicOptions", model, StringComparison.Ordinal);
        Assert.Contains("ShowWhen", model, StringComparison.Ordinal);
        Assert.Contains("ExclusiveGroupField", model, StringComparison.Ordinal);
        Assert.Contains("PluginAdminFormValidation", model, StringComparison.Ordinal);
    }
}
