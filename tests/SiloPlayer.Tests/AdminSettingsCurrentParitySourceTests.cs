namespace SiloPlayer.Tests;

public class AdminSettingsCurrentParitySourceTests
{
    private static readonly string RepoRoot = FindRepositoryRoot();
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));
    private static string SettingsApi => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer.Core", "Api", "SettingsApi.cs"));
    private static string AdminApi => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));
    private static string ViewModel => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminSettingsDetailViewModel.cs"));

    [Fact]
    public void ThemingUsesCurrentPreviewFirstAutosaveAndSiloCatalogContract()
    {
        Assert.Contains("case \"Theming\": BuildThemingTabCurrent()", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("tabName is not (\"Theming\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Preview\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Token Overrides\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Custom CSS\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Theme Catalog URL\")", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ScheduleAdminThemeSave(\"ui.admin_theme_vars\", 500)", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ScheduleAdminThemeSave(\"ui.admin_custom_css\", 1000)", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("SanitizeAdminThemeCss", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("https://raw.githubusercontent.com/Silo-Server/silo-themes/main/catalog.json", CodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ContinuumApp/continuum-themes", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsUsesCurrentTwentySectionGroupedRail()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Padding=\"40,32,40,40\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        foreach (var group in new[] { "Server", "Media", "Connections", "Data" })
            Assert.Contains($"(\"{group}\",", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"Branding\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"Search\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ContentPanel.ClearValue(FrameworkElement.MaxWidthProperty)", CodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentPanel.MaxWidth = 768", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRailHeaderAndSaveBarReflowWithoutClipping()
    {
        foreach (var name in new[] { "SettingsPageShell", "SettingsHeaderGrid", "SettingsSearchPanel", "SettingsSurfaceGrid", "SettingsRailBorder", "SettingsRailScroll", "SettingsContentScroll" })
            Assert.Contains($"x:Name=\"{name}\"", Markup, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplySettingsRailMode", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("TabBar.Orientation = _compactLayout ? Orientation.Horizontal", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyInlineSaveLayout", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsLoadsAreCancelledOnNavigationAndFeedbackUsesGlobalToasts()
    {
        Assert.Contains("CancellationTokenSource? _loadCts", ViewModel, StringComparison.Ordinal);
        Assert.Contains("public void CancelLoad()", ViewModel, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CancelLoad()", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ToastService", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_toastService.Error(message)", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSearchIndexesCurrentIndividualFieldLabels()
    {
        Assert.Contains("SettingsSearchFields", CodeBehind, StringComparison.Ordinal);
        foreach (var label in new[]
                 {
                     "Trusted Proxies", "Vector Coverage", "Transcription limit per account",
                     "Silo Push Relay", "Compatibility Proxies", "Decision Log Verbosity"
                 })
            Assert.Contains($"\"{label}\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("fields.Any(field => field.Contains(query", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationSettingsUseCurrentPipelineChannelsAndAdvancedLayout()
    {
        Assert.Contains("BuildNotificationsAdminTabCurrent", CodeBehind, StringComparison.Ordinal);
        foreach (var text in new[]
                 {
                     "Record events", "Fan out", "Hand off to the delivery channels below.",
                     "DELIVERY CHANNELS", "Silo Push Relay", "Personal Webhooks", "Server Channels",
                     "ADVANCED", "FANOUT TUNING", "RETENTION"
                 })
            Assert.Contains($"\"{text}\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateNotificationEnabledCount", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineSaveBarUsesOneDetachedPageLevelSubscription()
    {
        Assert.Contains("ViewModel.PropertyChanged += ViewModel_PropertyChanged", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PropertyChanged -= ViewModel_PropertyChanged", CodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.PropertyChanged += (_, args)", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_inlineSaveBar.Visibility = Visibility.Visible", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_inlineDiscardButton.IsEnabled = count > 0", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_inlineSaveButton.IsEnabled = count > 0", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartRequiredSaveBarUsesCurrentServerRestartEndpoint()
    {
        Assert.Contains("Server restart required for changes to take effect.", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Title = \"Restart server?\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("RestartServerAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("/api/v1/admin/server/restart", AdminApi, StringComparison.Ordinal);
    }

    [Fact]
    public void AiSettingsReadLegacyValuesButAlwaysWriteCurrentKeys()
    {
        foreach (var legacy in new[]
                 {
                     "subtitle_ai.base_url", "subtitle_ai.chat_model", "subtitle_ai.api_key",
                     "subtitle_ai.max_concurrent_jobs"
                 })
            Assert.Contains($"\"{legacy}\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("GetSettingValue", CodeBehind, StringComparison.Ordinal);
        foreach (var current in new[]
                 {
                     "ai.base_url", "ai.chat_model", "ai.api_key", "ai.asr_model",
                     "ai.asr_base_url", "ai.asr_api_key", "ai.max_concurrent_jobs"
                 })
            Assert.Contains($"\"{current}\"", CodeBehind, StringComparison.Ordinal);
        var endpointSaveStart = CodeBehind.IndexOf("AddOwnedSettingsSaveButton(endpointCard", StringComparison.Ordinal);
        Assert.True(endpointSaveStart >= 0, "Expected the AI endpoint save payload.");
        var endpointSaveEnd = CodeBehind.IndexOf("]);", endpointSaveStart, StringComparison.Ordinal);
        Assert.True(endpointSaveEnd > endpointSaveStart, "Expected the AI endpoint save payload to terminate.");
        var endpointSave = CodeBehind[endpointSaveStart..endpointSaveEnd];
        Assert.DoesNotContain("subtitle_ai.base_url", endpointSave, StringComparison.Ordinal);
        Assert.DoesNotContain("subtitle_ai.chat_model", endpointSave, StringComparison.Ordinal);
        Assert.DoesNotContain("subtitle_ai.api_key", endpointSave, StringComparison.Ordinal);
        Assert.DoesNotContain("subtitle_ai.max_concurrent_jobs", endpointSave, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandingAndSearchAreIndependentCurrentSettingsSurfaces()
    {
        Assert.Contains("BuildBrandingTab", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("branding.accent_color", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("branding.default_theme", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildSearchTab", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("catalog.search.meilisearch.semantic_enabled", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("catalog.search.meilisearch.binary_quantized", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandingSupportsCurrentAssetsAccentAndThemeCatalog()
    {
        foreach (var kind in new[] { "wordmark", "mark", "favicon", "login_bg" })
            Assert.Contains($"\"{kind}\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("UploadBrandingAssetAsync", SettingsApi, StringComparison.Ordinal);
        Assert.Contains("DeleteBrandingAssetAsync", SettingsApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/admin/branding/assets/", SettingsApi, StringComparison.Ordinal);
        foreach (var token in new[] { "primary", "ring", "sidebar-primary" })
            Assert.Contains($"\"{token}\"", CodeBehind, StringComparison.Ordinal);
        foreach (var theme in new[] { "midnight-cinema", "cinema-light", "cobalt-studio", "oxblood-noir", "evergreen-studio" })
            Assert.Contains(theme, CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchIncludesConnectionHealthSemanticCoverageAndTaskHistory()
    {
        Assert.Contains("GetCatalogSearchStatusAsync", SettingsApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/admin/catalog/search/status", SettingsApi, StringComparison.Ordinal);
        Assert.Contains("AddConnectionCheckButton(meili, \"meilisearch\", \"Check Connection\")", CodeBehind, StringComparison.Ordinal);
        foreach (var label in new[]
                 {
                     "Active Provider", "Health", "Active Index", "Documents", "Indexed Types",
                     "Binary Quantized", "Semantic Search", "Semantic Ratio", "Vectorized Documents",
                     "Semantic Readiness", "Vector Coverage", "Coverage Updated", "Embedder Capability",
                     "Per-Type Coverage", "Pending Events", "Dead-lettered Events", "Last Sync", "Last Fallback"
                 })
            Assert.Contains($"\"{label}\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("rebuild_catalog_search_index", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("sync_catalog_search_index", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentGeneralPlaybackDownloadsAndLoggingFieldsReplaceStaleControls()
    {
        foreach (var key in new[]
                 {
                     "clientip.trusted_proxies", "download.transcode_enabled", "download.artifact_dir",
                     "download.max_concurrent_prepares", "download.artifact_max_bytes",
                     "playback.local_transcode_fallback", "policy.decision_log_retention_days",
                     "policy.decision_log_verbosity", "policy.decision_log_scope_sample_rate"
                 })
            Assert.Contains($"\"{key}\"", CodeBehind, StringComparison.Ordinal);
        foreach (var staleKey in new[]
                 {
                     "playback.allow_hevc_encoding", "playback.transcode_ahead_segments",
                     "playback.segment_duration", "scanner.file_removal_grace"
                 })
            Assert.DoesNotContain($"\"{staleKey}\"", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityIntroEmailAndNotificationActionsUseLiveAdminApis()
    {
        foreach (var route in new[]
                 {
                     "/api/v1/admin/jellyfin-compat/status", "/api/v1/admin/jellyfin-compat/web/install",
                     "/api/v1/admin/jellyfin-compat/web/remove", "/api/v1/admin/markers/providers",
                     "/api/v1/admin/email/test", "/api/v1/admin/notifications/discord/test",
                     "/api/v1/admin/notifications/server-channels"
                 })
            Assert.Contains(route, SettingsApi, StringComparison.Ordinal);
        foreach (var label in new[]
                 {
                     "Enable Jellyfin Proxy", "Enable Audiobookshelf Proxy", "Marker Providers",
                     "Save Provider Settings", "Send test", "Test bot token", "Add server channel",
                     "Rotate secret", "Per-Type Coverage"
                 })
            Assert.Contains($"\"{label}\"", CodeBehind, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "SiloPlayer.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
