using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminSettingsDetailPage : Page
{
    public AdminSettingsDetailViewModel ViewModel { get; }

    private string _activeTab = "General";
    private Button? _activeTabButton;
    private readonly List<(Button Button, string TabName)> _tabButtons = [];

    // Lookup for rebuilding fields after discard
    private readonly List<Action> _fieldRebuilders = [];

    private static readonly string[] TabNames =
    [
        "General",
        "Theming",
        "Playback",
        "Scanner & Matcher",
        "Rate Limiting",
        "Downloads",
        "Integrations",
        "Jellyfin Compat",
        "Database",
        "Storage",
        "Log Retention"
    ];

    public AdminSettingsDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminSettingsDetailViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        BuildTabBar();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            ShowTab(_activeTab);
            UpdateDirtyCountText();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    // ===== Tab Bar =====

    private void BuildTabBar()
    {
        TabBar.Children.Clear();
        _tabButtons.Clear();

        foreach (var tabName in TabNames)
        {
            var btn = new Button
            {
                Content = tabName,
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(10),
                FontSize = 13,
                FontWeight = FontWeights.Medium
            };
            btn.Click += TabButton_Click;
            _tabButtons.Add((btn, tabName));
            TabBar.Children.Add(btn);
        }

        // Activate first tab
        if (_tabButtons.Count > 0)
        {
            SetActiveTab(_tabButtons[0].Button, _tabButtons[0].TabName);
        }
    }

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var entry = _tabButtons.Find(t => t.Button == btn);
            if (entry.Button != null)
            {
                SetActiveTab(btn, entry.TabName);
                ShowTab(entry.TabName);
            }
        }
    }

    private void SetActiveTab(Button button, string tabName)
    {
        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        foreach (var (btn, _) in _tabButtons)
        {
            btn.Background = new SolidColorBrush(Colors.Transparent);
            btn.Foreground = secondaryFg;
        }

        button.Background = accentBg;
        button.Foreground = accentFg;
        _activeTabButton = button;
        _activeTab = tabName;
    }

    // ===== Show Tab =====

    private void ShowTab(string tabName)
    {
        ContentPanel.Children.Clear();
        _fieldRebuilders.Clear();

        switch (tabName)
        {
            case "General": BuildGeneralTab(); break;
            case "Theming": BuildThemingTab(); break;
            case "Playback": BuildPlaybackTab(); break;
            case "Scanner & Matcher": BuildScannerTab(); break;
            case "Rate Limiting": BuildRateLimitTab(); break;
            case "Downloads": BuildDownloadsTab(); break;
            case "Integrations": BuildIntegrationsTab(); break;
            case "Jellyfin Compat": BuildJellyfinTab(); break;
            case "Database": BuildDatabaseTab(); break;
            case "Storage": BuildStorageTab(); break;
            case "Log Retention": BuildLogRetentionTab(); break;
        }
    }

    // ===== Tab Builders =====

    private void BuildThemingTab()
    {
        AddTabHeader("Theming", "Customize server branding, catalog themes, and login page appearance.");

        AddSectionHeader("Branding");
        var brandCard = BeginCard();
        AddTextField(brandCard, "Server Name", "branding.server_name", "e.g. My Media Server");
        AddTextField(brandCard, "Login Subtitle", "branding.login_subtitle", "Shown below server name on login page");
        EndCard(brandCard);

        AddSectionHeader("Catalog Theme");
        var themeCard = BeginCard();
        AddTextField(themeCard, "Theme Catalog URL", "theme.catalog_url", "URL to a remote theme catalog JSON");
        EndCard(themeCard);
    }

    private void BuildDownloadsTab()
    {
        AddTabHeader("Downloads", "Control offline download availability, bandwidth limits, and concurrency.");

        AddSectionHeader("Downloads");
        var dlCard = BeginCard();
        AddToggleField(dlCard, "Enable Downloads", "download.enabled");
        AddTextField(dlCard, "Server Bandwidth (Mbps)", "download.server_bandwidth_mbps", "Total server bandwidth for downloads");
        AddTextField(dlCard, "User Bandwidth (Mbps)", "download.user_bandwidth_mbps", "Per-user bandwidth limit");
        AddTextField(dlCard, "Max Concurrent Per User", "download.max_concurrent_per_user", "Simultaneous downloads per user");
        AddTextField(dlCard, "Max Per Period", "download.max_per_period", "Download count limit per period");
        AddTextField(dlCard, "Period Duration", "download.period_duration", "e.g. 24h, 7d");
        EndCard(dlCard);
    }

    private void BuildGeneralTab()
    {
        AddTabHeader("General", "Authentication, token lifetimes, and server logging behavior.");

        AddSectionHeader("Authentication");
        var authCard = BeginCard();
        AddDurationField(authCard, "Access Token Expiry", "auth.access_token_expiry", "e.g. 1h, 30m");
        AddDurationField(authCard, "Refresh Token Expiry", "auth.refresh_token_expiry", "e.g. 30d, 720h");
        EndCard(authCard);

        AddSectionHeader("Logging");
        var logCard = BeginCard();
        AddSelectField(logCard, "Log Level", "server.log_level", ["debug", "info", "warn", "error"]);
        AddTextField(logCard, "Quiet Subsystems", "server.log_quiet", "Comma-separated subsystem prefixes to silence");
        EndCard(logCard);
    }

    private void BuildPlaybackTab()
    {
        AddTabHeader("Playback", "Configure transcoding, segment generation, and watched-state behavior.");

        AddSectionHeader("Transcoding");
        var tcCard = BeginCard();
        AddTextField(tcCard, "FFmpeg Path", "playback.ffmpeg_path");
        AddTextField(tcCard, "Transcode Directory", "playback.transcode_dir");
        AddTextField(tcCard, "Hardware Acceleration", "playback.hw_accel", "auto, vaapi, nvenc, qsv, none");
        AddToggleField(tcCard, "Transcoding Enabled", "playback.transcode_enabled");
        AddToggleField(tcCard, "Allow HEVC Encoding", "playback.allow_hevc_encoding");
        AddToggleField(tcCard, "Allow 4K Transcoding", "allow_4k_transcode");
        AddConditionalToggleWithNumberField(
            tcCard,
            "Enable Transcode Throttling", "enable_transcode_throttle",
            "Throttle Buffer (seconds)", "transcode_throttle_seconds",
            "How many seconds ahead FFmpeg transcodes before pausing. Minimum: 60.");
        EndCard(tcCard);

        AddSectionHeader("Segments");
        var segCard = BeginCard();
        AddNumberField(segCard, "Transcode Ahead Segments", "playback.transcode_ahead_segments");
        AddNumberField(segCard, "Segment Duration", "playback.segment_duration");
        EndCard(segCard);

        AddSectionHeader("Behavior");
        var behCard = BeginCard();
        AddNumberField(behCard, "Watched Threshold (%)", "playback.watched_threshold", "Mark as watched after this % is played (default: 90)");
        EndCard(behCard);
    }

    private void BuildScannerTab()
    {
        AddTabHeader("Scanner & Matcher", "Schedule library scans and control how aggressively metadata matching runs.");

        AddSectionHeader("Scanner");
        var scanCard = BeginCard();
        AddTextField(scanCard, "Scanner Schedule", "scanner.schedule", "Cron expression, e.g. */15 * * * *");
        AddNumberField(scanCard, "Scanner Workers", "scanner.workers");
        AddDurationField(scanCard, "File Removal Grace", "scanner.file_removal_grace", "e.g. 24h");
        EndCard(scanCard);

        AddSectionHeader("Matcher");
        var matchCard = BeginCard();
        AddNumberField(matchCard, "Matcher Workers", "matcher.workers");
        AddNumberField(matchCard, "Matcher Batch Size", "matcher.batch_size");
        EndCard(matchCard);
    }

    private void BuildRateLimitTab()
    {
        AddTabHeader("Rate Limiting", "Configure request budgets for API keys, IPs, and authentication endpoints.");

        var cfg = ViewModel.DirtyRateLimitConfig ?? ViewModel.RateLimitConfig;

        if (cfg == null)
        {
            var errCard = BeginCard();
            AddTextBlock(errCard, "Rate limit configuration could not be loaded. Check server connectivity.");
            EndCard(errCard);
            return;
        }

        // Work on a mutable copy
        var working = new Core.Models.Admin.RateLimitConfig
        {
            Enabled = cfg.Enabled,
            Backend = cfg.Backend,
            GlobalRequestsPerSecond = cfg.GlobalRequestsPerSecond,
            IpRequestsPerSecond = cfg.IpRequestsPerSecond,
            IpRequestsPerMinute = cfg.IpRequestsPerMinute,
            IpBurst = cfg.IpBurst,
            Tiers = new System.Collections.Generic.Dictionary<string, Core.Models.Admin.RateLimitTierConfig>(cfg.Tiers),
            AuthEndpoints = new System.Collections.Generic.Dictionary<string, Core.Models.Admin.RateLimitAuthEndpointConfig>(cfg.AuthEndpoints),
        };

        void MarkDirty() { ViewModel.DirtyRateLimitConfig = working; UpdateDirtyCountText(); }

        // ---- Enable + Backend ----
        {
            var card = BeginCard();

            // Enable Rate Limiting toggle
            var enableField = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            enableField.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            enableField.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var enableLabelStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            enableLabelStack.Children.Add(new TextBlock { Text = "Enable Rate Limiting", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            enableLabelStack.Children.Add(new TextBlock { Text = "When disabled, no rate limits are enforced.", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextWrapping = TextWrapping.Wrap });
            var enableToggle = new ToggleSwitch { IsOn = working.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            enableToggle.Toggled += (s, e) => { working.Enabled = enableToggle.IsOn; MarkDirty(); };
            Grid.SetColumn(enableLabelStack, 0); Grid.SetColumn(enableToggle, 1);
            enableField.Children.Add(enableLabelStack); enableField.Children.Add(enableToggle);
            card.Children.Add(enableField);

            AddDivider(card);

            // Backend select
            var backendField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
            backendField.Children.Add(new TextBlock { Text = "Backend", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var backendCombo = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            backendCombo.Items.Add(new ComboBoxItem { Content = "In-Memory", Tag = "memory" });
            backendCombo.Items.Add(new ComboBoxItem { Content = "Redis", Tag = "redis" });
            for (int i = 0; i < backendCombo.Items.Count; i++)
                if (backendCombo.Items[i] is ComboBoxItem ci && ci.Tag?.ToString() == working.Backend) { backendCombo.SelectedIndex = i; break; }
            if (backendCombo.SelectedIndex < 0) backendCombo.SelectedIndex = 0;
            backendCombo.SelectionChanged += (s, e) => { if (backendCombo.SelectedItem is ComboBoxItem sel) { working.Backend = sel.Tag?.ToString() ?? "memory"; MarkDirty(); } };
            backendField.Children.Add(backendCombo);
            backendField.Children.Add(new TextBlock { Text = "Requires a restart to take effect. Redis is recommended for multi-instance deployments.", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
            card.Children.Add(backendField);

            EndCard(card);
        }

        // ---- Global Settings ----
        AddSectionHeader("Global Settings");
        {
            var card = BeginCard();
            var globalField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
            globalField.Children.Add(new TextBlock { Text = "Global Requests Per Second", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var globalRpsBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.GlobalRequestsPerSecond, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            globalRpsBox.ValueChanged += (s, e) => { if (!double.IsNaN(globalRpsBox.Value)) { working.GlobalRequestsPerSecond = (int)globalRpsBox.Value; MarkDirty(); } };
            globalField.Children.Add(globalRpsBox);
            globalField.Children.Add(new TextBlock { Text = "Maximum requests per second across all clients combined.", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
            card.Children.Add(globalField);
            EndCard(card);
        }

        // ---- Per-IP Limits ----
        AddSectionHeader("Per-IP Limits");
        {
            var card = BeginCard();
            AddTextBlock(card, "Applied to all authenticated requests from a single IP address.");

            AddDivider(card);
            var ipGrid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            ipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rpsField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            rpsField.Children.Add(new TextBlock { Text = "Requests / Second", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpsBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.IpRequestsPerSecond, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpsBox.ValueChanged += (s, e) => { if (!double.IsNaN(rpsBox.Value)) { working.IpRequestsPerSecond = (int)rpsBox.Value; MarkDirty(); } };
            rpsField.Children.Add(rpsBox);

            var rpmField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            rpmField.Children.Add(new TextBlock { Text = "Requests / Minute", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpmBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.IpRequestsPerMinute, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpmBox.ValueChanged += (s, e) => { if (!double.IsNaN(rpmBox.Value)) { working.IpRequestsPerMinute = (int)rpmBox.Value; MarkDirty(); } };
            rpmField.Children.Add(rpmBox);

            var burstField = new StackPanel { Spacing = 4 };
            burstField.Children.Add(new TextBlock { Text = "Burst", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var burstBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.IpBurst, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            burstBox.ValueChanged += (s, e) => { if (!double.IsNaN(burstBox.Value)) { working.IpBurst = (int)burstBox.Value; MarkDirty(); } };
            burstField.Children.Add(burstBox);

            Grid.SetColumn(rpsField, 0); Grid.SetColumn(rpmField, 1); Grid.SetColumn(burstField, 2);
            ipGrid.Children.Add(rpsField); ipGrid.Children.Add(rpmField); ipGrid.Children.Add(burstField);
            card.Children.Add(ipGrid);
            EndCard(card);
        }

        // ---- Tier Settings ----
        string[] tiers = ["standard", "elevated"];
        string[] tierLabels = ["Standard Tier", "Elevated Tier"];
        string[] tierDescs = ["Per API key limits for the standard tier.", "Per API key limits for the elevated tier."];
        for (int t = 0; t < tiers.Length; t++)
        {
            var tierKey = tiers[t];
            var tierLabel = tierLabels[t];
            var tierDesc = tierDescs[t];
            if (!working.Tiers.TryGetValue(tierKey, out var tierCfg))
                tierCfg = new Core.Models.Admin.RateLimitTierConfig { RequestsPerSecond = 10, RequestsPerMinute = 600, Burst = 20 };
            working.Tiers[tierKey] = tierCfg;

            AddSectionHeader(tierLabel);
            var card = BeginCard();
            AddTextBlock(card, tierDesc);
            AddDivider(card);

            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rpsF = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            rpsF.Children.Add(new TextBlock { Text = "Requests / Second", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpsB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = tierCfg.RequestsPerSecond, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpsB.ValueChanged += (s, e) => { if (!double.IsNaN(rpsB.Value)) { tierCfg.RequestsPerSecond = (int)rpsB.Value; MarkDirty(); } };
            rpsF.Children.Add(rpsB);

            var rpmF = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            rpmF.Children.Add(new TextBlock { Text = "Requests / Minute", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpmB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = tierCfg.RequestsPerMinute, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpmB.ValueChanged += (s, e) => { if (!double.IsNaN(rpmB.Value)) { tierCfg.RequestsPerMinute = (int)rpmB.Value; MarkDirty(); } };
            rpmF.Children.Add(rpmB);

            var burstF = new StackPanel { Spacing = 4 };
            burstF.Children.Add(new TextBlock { Text = "Burst", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var burstB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = tierCfg.Burst, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            burstB.ValueChanged += (s, e) => { if (!double.IsNaN(burstB.Value)) { tierCfg.Burst = (int)burstB.Value; MarkDirty(); } };
            burstF.Children.Add(burstB);

            Grid.SetColumn(rpsF, 0); Grid.SetColumn(rpmF, 1); Grid.SetColumn(burstF, 2);
            grid.Children.Add(rpsF); grid.Children.Add(rpmF); grid.Children.Add(burstF);
            card.Children.Add(grid);
            EndCard(card);
        }

        // ---- Auth Endpoint Limits ----
        AddSectionHeader("Auth Endpoint Limits");
        {
            var card = BeginCard();
            AddTextBlock(card, "Per-IP limits for authentication endpoints to prevent brute-force attacks.");

            string[] endpoints = ["login", "signup", "setup"];
            string[] endpointLabels = ["Login", "Signup", "Setup"];
            for (int ep = 0; ep < endpoints.Length; ep++)
            {
                var epKey = endpoints[ep];
                var epLabel = endpointLabels[ep];
                if (!working.AuthEndpoints.TryGetValue(epKey, out var epCfg))
                    epCfg = new Core.Models.Admin.RateLimitAuthEndpointConfig { RequestsPerMinute = 20, Burst = 10 };
                working.AuthEndpoints[epKey] = epCfg;

                AddDivider(card);

                var epHeader = new TextBlock { Text = epLabel, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"], Margin = new Thickness(0, 8, 0, 4) };
                card.Children.Add(epHeader);

                var epGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                epGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                epGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var epRpmF = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
                epRpmF.Children.Add(new TextBlock { Text = "Requests / Minute", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
                var epRpmB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = epCfg.RequestsPerMinute, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
                epRpmB.ValueChanged += (s, e) => { if (!double.IsNaN(epRpmB.Value)) { epCfg.RequestsPerMinute = (int)epRpmB.Value; MarkDirty(); } };
                epRpmF.Children.Add(epRpmB);

                var epBurstF = new StackPanel { Spacing = 4 };
                epBurstF.Children.Add(new TextBlock { Text = "Burst", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
                var epBurstB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = epCfg.Burst, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
                epBurstB.ValueChanged += (s, e) => { if (!double.IsNaN(epBurstB.Value)) { epCfg.Burst = (int)epBurstB.Value; MarkDirty(); } };
                epBurstF.Children.Add(epBurstB);

                Grid.SetColumn(epRpmF, 0); Grid.SetColumn(epBurstF, 1);
                epGrid.Children.Add(epRpmF); epGrid.Children.Add(epBurstF);
                card.Children.Add(epGrid);
            }
            EndCard(card);
        }
    }

    private void BuildIntegrationsTab()
    {
        AddTabHeader("Integrations", "External services, subtitle providers, and metadata providers.");

        AddSectionHeader("MetaDB");
        var metaCard = BeginCard();
        AddTextField(metaCard, "URL", "metadb.url");
        AddPasswordField(metaCard, "API Key", "metadb.api_key");
        EndCard(metaCard);

        AddSectionHeader("TMDB");
        var tmdbCard = BeginCard();
        AddPasswordField(tmdbCard, "API Key", "tmdb.api_key", "Shared by TMDB metadata providers and TMDB collection/trending features.");
        EndCard(tmdbCard);
    }

    private void BuildJellyfinTab()
    {
        AddTabHeader("Jellyfin Compat", "Tune the compatibility layer exposed to Jellyfin-compatible clients.");

        AddSectionHeader("Server Identity");
        var idCard = BeginCard();
        AddTextField(idCard, "Public URL", "jellyfin_compat.public_url");
        AddTextField(idCard, "Server Name", "jellyfin_compat.server_name");
        AddTextField(idCard, "Server ID", "jellyfin_compat.server_id");
        AddTextField(idCard, "Emulated Server Version", "jellyfin_compat.emulated_server_version");
        EndCard(idCard);

        AddSectionHeader("Session Lifetimes");
        var sessCard = BeginCard();
        AddDurationField(sessCard, "Session TTL", "jellyfin_compat.session_ttl", "e.g. 24h");
        AddDurationField(sessCard, "Playback Session TTL", "jellyfin_compat.playback_session_ttl", "e.g. 6h");
        EndCard(sessCard);
    }

    private void BuildDatabaseTab()
    {
        AddTabHeader("Database", "Configure connection pooling, Redis, and user database replication behavior.");

        AddSectionHeader("Main Database");
        var mainCard = BeginCard();
        AddNumberField(mainCard, "Max Connections", "database.max_connections");
        EndCard(mainCard);

        AddSectionHeader("Redis");
        var redisCard = BeginCard();
        AddRedisSection(redisCard);
        EndCard(redisCard);

        AddSectionHeader("User Database");
        var udbCard = BeginCard();
        AddTextField(udbCard, "User DB Backend", "userdb.backend", "postgres or sqlite");
        AddNumberField(udbCard, "Pool Max Open", "userdb.pool_max_open");
        AddDurationField(udbCard, "Idle Timeout", "userdb.idle_timeout", "e.g. 12h");
        AddDurationField(udbCard, "Litestream Sync Interval", "userdb.litestream_sync", "e.g. 1s");
        AddNumberField(udbCard, "Stale Grace Seconds", "userdb.stale_grace_seconds");
        EndCard(udbCard);
    }

    private void AddRedisSection(StackPanel parent)
    {
        // Toggle to enable Redis (derived from whether redis.url is non-empty)
        var redisUrl = ViewModel.GetSetting("redis.url");
        bool redisEnabled = !string.IsNullOrWhiteSpace(redisUrl);

        // We need a reference to the URL field container so we can show/hide it
        StackPanel? urlFieldContainer = null;

        if (parent.Children.Count > 0) AddDivider(parent);

        var toggleField = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        toggleField.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleField.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock
        {
            Text = "Enable Redis",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        labelStack.Children.Add(new TextBlock
        {
            Text = "Leave disabled to run without Redis",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        var toggle = new ToggleSwitch
        {
            IsOn = redisEnabled,
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(labelStack, 0);
        Grid.SetColumn(toggle, 1);
        toggleField.Children.Add(labelStack);
        toggleField.Children.Add(toggle);
        parent.Children.Add(toggleField);

        // URL field (only visible when redis is enabled)
        AddDivider(parent);
        urlFieldContainer = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8), Visibility = redisEnabled ? Visibility.Visible : Visibility.Collapsed };

        urlFieldContainer.Children.Add(new TextBlock
        {
            Text = "Connection URL",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentUrl = ViewModel.GetSetting("redis.url");
        bool isUrlConfigured = ViewModel.IsSensitiveConfigured("redis.url");

        var passwordBox = new PasswordBox
        {
            Password = "",
            Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"],
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = isUrlConfigured ? "\u2022\u2022\u2022\u2022 configured" : "redis://host:6379"
        };

        if (isUrlConfigured)
        {
            var badge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BadgeResolutionBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "configured",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            var labelTextBlock = (urlFieldContainer.Children[0] as TextBlock)!;
            urlFieldContainer.Children.RemoveAt(0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            row.Children.Add(labelTextBlock);
            row.Children.Add(badge);
            urlFieldContainer.Children.Insert(0, row);
        }

        passwordBox.PasswordChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(passwordBox.Password))
                ViewModel.SetSetting("redis.url", passwordBox.Password);
            UpdateDirtyCountText();
        };
        urlFieldContainer.Children.Add(passwordBox);

        urlFieldContainer.Children.Add(new TextBlock
        {
            Text = "redis://host:6379",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        parent.Children.Add(urlFieldContainer);

        // Wire toggle to show/hide URL field and clear value when disabled
        toggle.Toggled += (s, e) =>
        {
            if (toggle.IsOn)
            {
                urlFieldContainer.Visibility = Visibility.Visible;
            }
            else
            {
                urlFieldContainer.Visibility = Visibility.Collapsed;
                ViewModel.SetSetting("redis.url", "");
                UpdateDirtyCountText();
            }
        };

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting("redis.url");
            bool isOn = !string.IsNullOrWhiteSpace(v);
            toggle.IsOn = isOn;
            urlFieldContainer.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void BuildStorageTab()
    {
        AddTabHeader("Storage", "S3-compatible object storage for artwork, operational exports, and future replicated data.");

        AddSectionHeader("MetaDB Posters");
        var metaCard = BeginCard();
        AddTextBlock(metaCard, "Used by MetaDB for storing and serving poster/artwork images. Generates presigned URLs for clients to fetch images directly from S3.");
        AddTextField(metaCard, "Endpoint", "s3.metadata_endpoint");
        AddTextField(metaCard, "Region", "s3.metadata_region");
        AddToggleField(metaCard, "Path Style", "s3.metadata_path_style");
        AddTextField(metaCard, "Bucket", "s3.metadata_bucket");
        AddPasswordField(metaCard, "Access Key", "s3.metadata_access_key");
        AddPasswordField(metaCard, "Secret Key", "s3.metadata_secret_key");
        AddDurationField(metaCard, "Presign Expiry", "s3.metadata_presign_expiry", "e.g. 4h");
        EndCard(metaCard);

        AddSectionHeader("General Purpose");
        var opCard = BeginCard();
        AddTextBlock(opCard, "General-purpose storage for operational tasks such as catalog import/export.");
        AddTextField(opCard, "Endpoint", "s3.operational_endpoint");
        AddTextField(opCard, "Region", "s3.operational_region");
        AddToggleField(opCard, "Path Style", "s3.operational_path_style");
        AddTextField(opCard, "Bucket", "s3.operational_bucket");
        AddPasswordField(opCard, "Access Key", "s3.operational_access_key");
        AddPasswordField(opCard, "Secret Key", "s3.operational_secret_key");
        EndCard(opCard);
    }

    private void BuildLogRetentionTab()
    {
        AddTabHeader("Log Retention", "Prune oldest operational logs by global caps and per-bucket overrides. Bucket rules match on component and level.");

        AddSectionHeader("Global Limits");
        var globalCard = BeginCard();
        AddNumberField(globalCard, "Retention Days", "opslog.retention_days", "Logs older than this are pruned first.");
        AddNumberField(globalCard, "Cleanup Interval (Minutes)", "opslog.cleanup_interval_minutes", "How often the retention worker checks caps and prunes oldest rows.");
        AddNumberField(globalCard, "Max Rows", "opslog.max_rows", "Keeps only the newest rows once this total is exceeded.");
        AddNumberField(globalCard, "Max Size (MB)", "opslog.max_size_mb", "Uses estimated log row size. Oldest rows are pruned when the budget is exceeded.");
        EndCard(globalCard);

        AddSectionHeader("Bucket Overrides");
        var bucketCard = BeginCard();
        AddTextBlock(bucketCard, "Per-bucket overrides are stored as a JSON array in the setting key opslog.bucket_policies. Edit the server settings directly to configure bucket-level rules, or use the web UI for the full bucket rule editor.");
        EndCard(bucketCard);
    }

    // ===== UI Builder Helpers =====

    private void AddTabHeader(string title, string description)
    {
        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };

        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        header.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        ContentPanel.Children.Add(header);
    }

    private void AddSectionHeader(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["SectionHeaderTextStyle"],
            Margin = new Thickness(4, 8, 0, 0)
        };
        ContentPanel.Children.Add(header);
    }

    private StackPanel BeginCard()
    {
        return new StackPanel { Spacing = 0 };
    }

    private void EndCard(StackPanel cardContent)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(20, 16, 20, 16),
            Child = cardContent
        };
        ContentPanel.Children.Add(border);
    }

    private void AddDivider(StackPanel parent)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            Margin = new Thickness(0, 4, 0, 4),
            Opacity = 0.5
        });
    }

    private void AddTextBlock(StackPanel parent, string text)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        });
    }

    // ===== Field Builders =====

    private void AddTextField(StackPanel parent, string label, string key, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var textBox = new TextBox
        {
            Text = ViewModel.GetSetting(key),
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = hint ?? ""
        };
        textBox.TextChanged += (s, e) =>
        {
            ViewModel.SetSetting(key, textBox.Text);
            UpdateDirtyCountText();
        };
        field.Children.Add(textBox);

        _fieldRebuilders.Add(() => textBox.Text = ViewModel.GetSetting(key));

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        parent.Children.Add(field);
    }

    private void AddPasswordField(StackPanel parent, string label, string key, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentVal = ViewModel.GetSetting(key);
        bool isConfigured = ViewModel.IsSensitiveConfigured(key);

        // For sensitive fields: don't pre-fill the actual value (API returns empty for secrets).
        // Show "configured" placeholder if the server reports it's set.
        var passwordBox = new PasswordBox
        {
            Password = "",
            Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"],
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = isConfigured ? "\u2022\u2022\u2022\u2022 configured" : (hint ?? "Not configured")
        };

        // Show a "configured" indicator badge next to the label
        if (isConfigured)
        {
            var badge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BadgeResolutionBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "configured",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            // Wrap label + badge in a horizontal panel
            var labelRow = (field.Children[0] as TextBlock);
            if (labelRow != null)
            {
                field.Children.RemoveAt(0);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
                row.Children.Add(labelRow);
                row.Children.Add(badge);
                field.Children.Insert(0, row);
            }
        }

        passwordBox.PasswordChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(passwordBox.Password))
                ViewModel.SetSetting(key, passwordBox.Password);
            UpdateDirtyCountText();
        };
        field.Children.Add(passwordBox);

        _fieldRebuilders.Add(() => { /* Don't refill passwords on discard */ });

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        parent.Children.Add(field);
    }

    private void AddNumberField(StackPanel parent, string label, string key, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentVal = ViewModel.GetSetting(key);
        double.TryParse(currentVal, out var numVal);

        var numberBox = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Value = string.IsNullOrEmpty(currentVal) ? double.NaN : numVal,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact,
            Minimum = 0,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        numberBox.ValueChanged += (s, e) =>
        {
            var val = double.IsNaN(numberBox.Value) ? "" : ((int)numberBox.Value).ToString();
            ViewModel.SetSetting(key, val);
            UpdateDirtyCountText();
        };
        field.Children.Add(numberBox);

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting(key);
            numberBox.Value = double.TryParse(v, out var n) ? n : double.NaN;
        });

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            });
        }

        parent.Children.Add(field);
    }

    private void AddToggleField(StackPanel parent, string label, string key, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        if (hint != null)
        {
            labelStack.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        var currentVal = ViewModel.GetSetting(key);
        var toggle = new ToggleSwitch
        {
            IsOn = currentVal.Equals("true", StringComparison.OrdinalIgnoreCase),
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (s, e) =>
        {
            ViewModel.SetSetting(key, toggle.IsOn ? "true" : "false");
            UpdateDirtyCountText();
        };

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting(key);
            toggle.IsOn = v.Equals("true", StringComparison.OrdinalIgnoreCase);
        });

        Grid.SetColumn(labelStack, 0);
        Grid.SetColumn(toggle, 1);
        field.Children.Add(labelStack);
        field.Children.Add(toggle);

        parent.Children.Add(field);
    }

    private void AddDurationField(StackPanel parent, string label, string key, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var textBox = new TextBox
        {
            Text = ViewModel.GetSetting(key),
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = hint ?? "e.g. 1h, 30m, 24h"
        };
        textBox.TextChanged += (s, e) =>
        {
            ViewModel.SetSetting(key, textBox.Text);
            UpdateDirtyCountText();
        };
        field.Children.Add(textBox);

        _fieldRebuilders.Add(() => textBox.Text = ViewModel.GetSetting(key));

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        parent.Children.Add(field);
    }

    private void AddSelectField(StackPanel parent, string label, string key, string[] options, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var comboBox = new ComboBox
        {
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        foreach (var option in options)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = option.Substring(0, 1).ToUpper() + option.Substring(1),
                Tag = option
            });
        }

        // Set current value
        var currentVal = ViewModel.GetSetting(key);
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == currentVal)
            {
                comboBox.SelectedIndex = i;
                break;
            }
        }
        if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;

        comboBox.SelectionChanged += (s, e) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem selected)
            {
                ViewModel.SetSetting(key, selected.Tag?.ToString() ?? "");
                UpdateDirtyCountText();
            }
        };

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting(key);
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == v)
                {
                    comboBox.SelectedIndex = i;
                    break;
                }
            }
        });

        field.Children.Add(comboBox);

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            });
        }

        parent.Children.Add(field);
    }

    /// <summary>
    /// A toggle field that conditionally shows a number field when the toggle is enabled.
    /// Mirrors the web pattern: {form.getValue(toggleKey) === "true" && &lt;SettingField .../&gt;}
    /// </summary>
    private void AddConditionalToggleWithNumberField(
        StackPanel parent,
        string toggleLabel, string toggleKey,
        string numberLabel, string numberKey,
        string? numberHint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var toggleField = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        toggleField.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleField.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock
        {
            Text = toggleLabel,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentToggleVal = ViewModel.GetSetting(toggleKey);
        var toggle = new ToggleSwitch
        {
            IsOn = currentToggleVal.Equals("true", StringComparison.OrdinalIgnoreCase),
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(labelStack, 0);
        Grid.SetColumn(toggle, 1);
        toggleField.Children.Add(labelStack);
        toggleField.Children.Add(toggle);
        parent.Children.Add(toggleField);

        // Number field shown only when toggle is on
        var divider = new Border
        {
            Height = 1,
            Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            Margin = new Thickness(0, 4, 0, 4),
            Opacity = 0.5,
            Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed
        };
        parent.Children.Add(divider);

        var numberField = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 8, 0, 8),
            Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed
        };
        numberField.Children.Add(new TextBlock
        {
            Text = numberLabel,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentNumVal = ViewModel.GetSetting(numberKey);
        double.TryParse(currentNumVal, out var numVal);
        var numberBox = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Value = string.IsNullOrEmpty(currentNumVal) ? double.NaN : numVal,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact,
            Minimum = 0,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        numberBox.ValueChanged += (s, e) =>
        {
            var val = double.IsNaN(numberBox.Value) ? "" : ((int)numberBox.Value).ToString();
            ViewModel.SetSetting(numberKey, val);
            UpdateDirtyCountText();
        };
        numberField.Children.Add(numberBox);

        if (numberHint != null)
        {
            numberField.Children.Add(new TextBlock
            {
                Text = numberHint,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            });
        }
        parent.Children.Add(numberField);

        toggle.Toggled += (s, e) =>
        {
            ViewModel.SetSetting(toggleKey, toggle.IsOn ? "true" : "false");
            divider.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            numberField.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            UpdateDirtyCountText();
        };

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting(toggleKey);
            toggle.IsOn = v.Equals("true", StringComparison.OrdinalIgnoreCase);
            divider.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            numberField.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

            var nv = ViewModel.GetSetting(numberKey);
            numberBox.Value = double.TryParse(nv, out var n) ? n : double.NaN;
        });
    }

    // ===== Save/Discard handlers =====

    private void UpdateDirtyCountText()
    {
        var count = ViewModel.DirtyCount;
        DirtyCountText.Text = count > 0
            ? $"{count} unsaved change{(count != 1 ? "s" : "")}"
            : "";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // The command binding handles the save; after save, refresh the tab
        await Task.Delay(100); // small delay to let binding update
        if (ViewModel.StatusMessage != null)
        {
            ShowTab(_activeTab);
            ShowStatusToast(ViewModel.StatusMessage);
        }
        UpdateDirtyCountText();
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        // Rebuild the current tab to reset all field values
        ShowTab(_activeTab);
        UpdateDirtyCountText();
    }

    private async void ShowStatusToast(string message)
    {
        StatusToastText.Text = message;
        StatusToast.Visibility = Visibility.Visible;

        await Task.Delay(3000);
        StatusToast.Visibility = Visibility.Collapsed;
    }
}
