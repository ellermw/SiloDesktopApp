namespace SiloPlayer.Tests;

public sealed class CurrentAdminParitySourceTests
{
    [Fact]
    public void AdminShellUsesSingleActivityControlAndCurrentSideNavGeometry()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var adminShell = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs"));

        Assert.Contains("ContentFrame.Content is Views.Admin.AdminShellPage", mainWindow);
        Assert.Contains("MainServerActivityButton.SetHostVisibility(false)", mainWindow);
        Assert.Contains("ApplyWebUiNavigationGeometry", adminShell);
        Assert.Contains("button.Padding = new Thickness(12, 10, 12, 10)", adminShell);
        Assert.Contains("bar.Height = 18", adminShell);
        Assert.Contains("Margin = new Thickness(12, 0, 12, 20)", adminShell);
    }

    [Fact]
    public void AdminLibrariesMatchesCurrentDiagnosticsAndEditorContracts()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("Fill=\"{StaticResource SuccessBrush}\"", xaml);
        Assert.Contains("Unmatched Items", xaml);
        Assert.Contains("Ambiguous Roots", xaml);
        Assert.Contains("Troubleshooting", xaml);
        Assert.Contains("Stale External IDs", xaml);
        Assert.True(
            code.IndexOf("var skippedRootsTask", StringComparison.Ordinal) <
            code.IndexOf("await ViewModel.LoadLibrariesAsync()", StringComparison.Ordinal));
        Assert.Contains("selectedPaths = new HashSet<string>", code);
        Assert.Contains("Add {selectedPaths.Count} Folder", code);
        Assert.Contains("Auto-translate descriptions", code);
        Assert.Contains("Generate chapter thumbnails", code);
        Assert.Contains("Detect intro markers", code);
        Assert.Contains("Trailer & extras types", code);
        Assert.Contains("Provider Priority", code);
    }

    [Fact]
    public void AdminDashboardUsesCurrentCardAndLoadingPresentation()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml.cs"));

        Assert.Contains("Style=\"{StaticResource OutlineButtonStyle}\"", xaml);
        Assert.Contains("<StackPanel Spacing=\"36\">", xaml);
        Assert.Contains("RecentActivitySection.Visibility = Visibility.Visible", code);
        Assert.Contains("for (var i = 0; i < 4; i++)", code);
        Assert.Contains("AdminPageContent.Spacing = contentWidth >= 1024 ? 32 : 24", code);
    }

    [Fact]
    public void AdminActivityUsesCurrentLoadingGeometryAndSessionCommandContract()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminActivityViewModel.cs"));
        var adminApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));

        Assert.Contains("Text=\"Loading activity...\"", xaml);
        Assert.Contains("CornerRadius=\"16\"", xaml);
        Assert.Contains("AdminPageContent.Spacing = width >= 1024 ? 24 : 20", code);
        Assert.Contains("session.HasPlaybackControl != false", code);
        Assert.Contains("fallback_scheduled", code);
        Assert.Contains("if (ViewModel.HasLoaded)", code);
        Assert.Contains("public bool HasLoaded", viewModel);
        Assert.Contains("Task<AdminSessionCommandResponse> TerminateSessionAsync", adminApi);
    }

    [Fact]
    public void AdminLogsUsesCurrentSurfaceGeometryAndIncrementalRealtimeRows()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLogsPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLogsPage.xaml.cs"));
        var stream = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "AdminLogStreamClient.cs"));

        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"16\" />", xaml);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"12\" />", xaml);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"8\" />", xaml);
        Assert.Contains("_suspendAppCollectionRebuild = true", code);
        Assert.Contains("AppLogsPanel_Rows.Children.Insert(0, BuildAppLogRow(entry))", code);
        Assert.Contains("AuditLogsPanel_Rows.Children.Insert(0, BuildAuditLogRow(entry))", code);
        Assert.Contains("ws.Abort()", stream);
        Assert.DoesNotContain("CloseAsync(WebSocketCloseStatus.NormalClosure, \"stopping\"", stream);
    }

    [Fact]
    public void AdminMaintenanceMatchesCurrentImportWorkflowAndPagePresentation()
    {
        var root = FindRepositoryRoot();
        var pageXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminMaintenancePage.xaml"));
        var pageCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminMaintenancePage.xaml.cs"));
        var dialogXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "CatalogImportDialog.xaml"));
        var dialogCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "CatalogImportDialog.xaml.cs"));

        Assert.DoesNotContain("x:Name=\"StatusBanner\"", pageXaml);
        Assert.Contains("CornerRadius=\"8\"", pageXaml);
        Assert.Contains("GetRequiredService<ToastService>().Success", pageCode);
        Assert.Contains("PrimaryButtonText=\"Import Catalog\"", dialogXaml);
        Assert.Contains("Text=\"Detected Files\"", dialogXaml);
        Assert.Contains("RefreshLocalSources_Click", dialogXaml);
        Assert.Contains("RefreshBucketSources_Click", dialogXaml);
        Assert.Contains("PlaceholderText = \"/srv/media\"", dialogCode);
        Assert.Contains("await _vm.SubmitImportAsync(BuiltRequest)", dialogCode);
        Assert.Contains("PrimaryButtonText = \"Importing...\"", dialogCode);
    }

    [Fact]
    public void AdminTasksKeepsRowNavigationSeparateFromActionsAndMatchesCurrentFeedback()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminTasksPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminTasksPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminTasksViewModel.cs"));

        Assert.Contains("FontWeight=\"ExtraBold\"", xaml);
        Assert.Contains("var nameBlock = new HyperlinkButton", code);
        Assert.DoesNotContain("var rowButton = new Button", code);
        Assert.Contains("labelBlock.Text = \"Starting...\"", code);
        Assert.Contains("TimeSpan.FromSeconds(1)", code);
        Assert.Contains("Text = \"Overdue\"", code);
        Assert.Contains("\"Refresh Backlog\"", code);
        Assert.Contains("\"Next Claim Timeout\"", code);
        Assert.DoesNotContain("await Task.Delay(300)", viewModel);
    }

    [Fact]
    public void AdminTaskDetailIncludesCurrentQueueHealthAndSilentRealtimeRefresh()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminTaskDetailPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminTaskDetailPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminTaskDetailViewModel.cs"));

        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"16\" />", xaml);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"12\" />", xaml);
        Assert.Contains("Style=\"{StaticResource OutlineButtonStyle}\"", xaml);
        Assert.Contains("\"REFRESH BACKLOG\"", code);
        Assert.Contains("\"NEXT CLAIM TIMEOUT\"", code);
        Assert.Contains("BuildDueSamplesCard(metrics.DueSamples)", code);
        Assert.Contains("Text = \"No recent queue errors.\"", code);
        Assert.Contains("Text = \"No due items right now.\"", code);
        Assert.Contains("LoadAsync(silent: true)", code);
        Assert.Contains("RefreshSilentAsync", viewModel);
        Assert.DoesNotContain("Task.Delay(300)", viewModel);
    }

    [Fact]
    public void AdminNodesMatchesCurrentFormUnitsOptionalCapsAndToastSurface()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminNodesPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminNodesPage.xaml.cs"));

        Assert.Contains("Spacing=\"24\"", xaml);
        Assert.Contains("FontWeight=\"ExtraBold\"", xaml);
        Assert.DoesNotContain("x:Name=\"StatusBanner\"", xaml);
        Assert.Contains("\"Max Streams\" : \"Max Transcodes\"", code);
        Assert.Contains("\"Max Egress Bandwidth (Mbps)\"", code);
        Assert.Contains("box.Value * 1000d", code);
        Assert.Contains("node.MaxBandwidthKbps is int maxBandwidth", code);
        Assert.Contains("GetRequiredService<ToastService>().Success", code);
    }

    [Fact]
    public void AdminApiKeysUsesCurrentPersistentPaginationDatesAndToastSurface()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminApiKeysPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminApiKeysPage.xaml.cs"));

        Assert.Contains("FontWeight=\"ExtraBold\"", xaml);
        Assert.DoesNotContain("x:Name=\"StatusBanner\"", xaml);
        Assert.Contains("PaginationBar.Visibility = Visibility.Visible", code);
        Assert.Contains("ToString(\"g\")", code);
        Assert.Contains("Revoke API key {key.Label}", code);
        Assert.Contains("GetRequiredService<ToastService>().Success", code);
    }

    [Fact]
    public void AdminAutoscanUsesCurrentLazyTabsLiveQueueAndWebhookContracts()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminAutoscanViewModel.cs"));

        Assert.Contains("Queue counts are active, but live scan details have not arrived yet.", viewModel);
        Assert.Contains("scan.Trigger.Equals(\"autoscan\"", viewModel);
        Assert.Contains("CancelLibraryScansAsync", viewModel);
        Assert.Contains("DeliveryMode = isWebhook ? \"webhook\" : \"poll\"", viewModel);
        Assert.Contains("[\"webhook_provider\"] = \"auto\"", viewModel);
        Assert.Contains("EnsureActivityLoadedAsync", code);
        Assert.Contains("Subscribe(\"scans\")", code);
        Assert.Contains("ShowPathRewritesDialogAsync", code);
        Assert.Contains("Webhook endpoint &amp; provider", xaml);
        Assert.Contains("Not needed — Sonarr/Radarr deliver directly", xaml);
        Assert.Contains("Text=\"PROGRESS\"", xaml);
        Assert.Contains("ShowConnectionsEmpty", xaml);
        Assert.DoesNotContain("ViewModel.HasFeedback", xaml);
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
