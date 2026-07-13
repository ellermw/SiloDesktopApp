namespace SiloPlayer.Tests;

public class AdminSettingsCurrentParitySourceTests
{
    private static readonly string RepoRoot = FindRepositoryRoot();
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));
    private static string SettingsApi => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer.Core", "Api", "SettingsApi.cs"));

    [Fact]
    public void SettingsUsesCurrentTwentySectionGroupedRail()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        foreach (var group in new[] { "Server", "Media", "Connections", "Data" })
            Assert.Contains($"(\"{group}\",", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"Branding\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"Search\"", CodeBehind, StringComparison.Ordinal);
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
