using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.ComponentModel;
using System.Text.Json;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Services;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSettingsDetailPage : Page
{
    public AdminSettingsDetailViewModel ViewModel { get; }

    // Static so the active tab persists across page navigations
    private static string _persistedTab = "General";
    private string _activeTab = _persistedTab;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string requestedTab && SettingsTabs.Any(tab => tab.Label == requestedTab))
        {
            _activeTab = requestedTab;
            _persistedTab = requestedTab;
        }
    }
    private string _settingsSearchQuery = "";
    private Button? _activeTabButton;
    private readonly List<(Button Button, string TabName)> _tabButtons = [];
    private Border? _inlineSaveBar;
    private Grid? _inlineSaveGrid;
    private StackPanel? _inlineStatusStack;
    private TextBlock? _inlineSaveStatusText;
    private Button? _inlineDiscardButton;
    private Button? _inlineSaveButton;
    private Button? _inlineRestartButton;
    private TextBlock? _inlineRestartNotice;
    private TextBlock? _notificationEnabledCountText;
    private bool _viewModelEventsAttached;
    private readonly List<Action> _dirtyStateUpdaters = [];

    // Lookup for rebuilding fields after discard
    private readonly List<Action> _fieldRebuilders = [];
    private readonly SettingsApi _settingsApi;
    private readonly SiloApiClient _apiClient;
    private readonly ToastService _toastService;
    private bool _compactLayout;
    private CancellationTokenSource? _adminThemeVarsSaveCts;
    private CancellationTokenSource? _adminThemeCssSaveCts;

    // Settings sub-nav — matches web/src/pages/admin-settings/AdminSettingsLayout.tsx
    // order and labeling. Each item is (label, Segoe Fluent icon glyph).
    private static readonly (string Label, string Glyph)[] SettingsTabs =
    [
        ("General",               "\uE713"),
        ("Branding",              "\uEB9F"),
        ("Theming",               "\uE790"),
        ("Card Overlays",         "\uE81E"),
        ("Scanner & Matcher",     "\uE721"),
        ("Search",                "\uE721"),
        ("Intro Markers",         "\uED1E"),
        ("Subtitles",             "\uED1E"),
        ("AI Services",           "\uE945"),
        ("Playback",              "\uE768"),
        ("Downloads",             "\uE896"),
        ("Watch Providers",       "\uE753"),
        ("Integrations",          "\uEA86"),
        ("Email",                 "\uE715"),
        ("Notifications",         "\uE7F4"),
        ("Compatibility Proxies", "\uE7F4"),
        ("Rate Limiting",         "\uE9D9"),
        ("Database",              "\uEBD2"),
        ("Storage",               "\uEDA2"),
        ("Log Retention",         "\uE81C"),
    ];

    private static readonly Dictionary<string, string[]> SettingsSearchKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["General"] = ["auth", "token", "logging", "server"],
        ["Branding"] = ["identity", "server name", "login", "accent", "default theme", "logo", "favicon"],
        ["Theming"] = ["theme", "css", "catalog", "tokens"],
        ["Card Overlays"] = ["poster", "badges", "overlay"],
        ["Scanner & Matcher"] = ["scan", "matcher", "metadata", "workers", "image cache"],
        ["Search"] = ["catalog", "postgres", "meilisearch", "semantic", "index"],
        ["Intro Markers"] = ["intro", "recap", "credits", "chapters", "markers"],
        ["Subtitles"] = ["opensubtitles", "subdl", "subsource", "caption", "providers"],
        ["AI Services"] = ["openai", "ollama", "translation", "transcription", "quota"],
        ["Playback"] = ["ffmpeg", "transcode", "hardware", "chapter thumbnails", "resume"],
        ["Downloads"] = ["bandwidth", "limits", "concurrency"],
        ["Watch Providers"] = ["trakt", "scrobble", "history", "favorites"],
        ["Integrations"] = ["mdblist", "plugins"],
        ["Email"] = ["smtp", "mail", "digest", "external url"],
        ["Notifications"] = ["discord", "webhooks", "server channels", "push", "request"],
        ["Compatibility Proxies"] = ["jellyfin", "emby", "compat"],
        ["Rate Limiting"] = ["api keys", "ip", "auth endpoints", "tiers"],
        ["Database"] = ["postgres", "redis", "userdb"],
        ["Storage"] = ["s3", "bucket", "cloudflare", "cdn"],
        ["Log Retention"] = ["audit", "operational", "retention"],
    };

    // Current WebUI search indexes individual setting labels in addition to
    // section titles and broad keywords. Keep the same behavior so searches
    // such as "Trusted Proxies" or "Vector Coverage" find their section.
    private static readonly Dictionary<string, string[]> SettingsSearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["General"] = ["Access Token Expiry", "Refresh Token Expiry", "Log Level", "Quiet Subsystems", "Trusted Proxies"],
        ["Branding"] = ["Server Name", "Login Page Subtitle", "Logo wordmark", "Logo icon", "Favicon", "Login Background", "Brand Accent Color", "Default Theme"],
        ["Theming"] = ["Preview", "Token Overrides", "Custom CSS", "Theme Catalog URL"],
        ["Card Overlays"] = ["Card Overlays Enabled", "Default Configuration", "Default style preset", "Overlay position", "Overlay enabled"],
        ["Scanner & Matcher"] = ["Scanner Workers", "Matcher Workers", "Matcher Batch Size", "Cache Images to S3"],
        ["Search"] = ["Preferred Provider", "URL", "API Key", "Index Prefix", "Timeout ms", "Matching Strategy", "Sync Batch Size", "Rebuild Batch Size", "Rebuild Queue Depth", "Indexed Types", "Semantic Search", "Semantic Ratio", "Embedder", "Vectorized Documents", "Status", "Vector Coverage", "Per-Type Coverage", "Pending Events", "Dead-lettered Events", "Last Sync", "Last Fallback"],
        ["Intro Markers"] = ["Mode", "Fetch Markers at Playback if Missing", "Use for Online Marker Lookup", "Allow Contributions", "Auto-submit Local Markers", "Marker Providers"],
        ["Subtitles"] = ["Provider settings", "Downloaded subtitles", "Subtitle appearance", "Subtitle language", "Subtitle behavior", "Forced subtitles"],
        ["AI Services"] = ["Base URL", "Chat model", "API Key", "Transcription model", "Transcription base URL", "Transcription API key", "Max concurrent jobs", "Subtitle translation", "Subtitle generation from audio", "Description translation", "On-view translation", "Subtitle batch size", "Subtitle context lines", "Transcription chunk length seconds", "Transcription limit per account", "Transcription limit period"],
        ["Playback"] = ["FFmpeg Path", "Transcode Directory", "Hardware Acceleration", "Transcoding Enabled", "Local Transcode Fallback", "Allow 4K Transcoding", "Enable Transcode Throttling", "Throttle Buffer seconds", "Chapter Thumbnail Workers", "Chapter Thumbnail Execution", "Chapter Thumbnail Node Capacity", "HDR Chapter Thumbnail Policy", "Watched Threshold", "Min Resume Threshold"],
        ["Downloads"] = ["Downloads Enabled", "Server Bandwidth Mbps", "Per-User Bandwidth Mbps", "Max Concurrent Downloads Per User", "Max Downloads Per Period", "Period Duration", "Transcode-to-File Enabled", "Artifact Directory", "Max Concurrent Prepares", "Artifact Storage Budget"],
        ["Watch Providers"] = ["Trakt", "Simkl", "Client ID", "Client Secret"],
        ["Integrations"] = ["MDBList", "API Key"],
        ["Email"] = ["Email Enabled", "From Address", "From Name", "Host", "Port", "Security", "Username", "Password", "Verify", "Send test"],
        ["Notifications"] = ["Record events", "Enable release events", "Fan out", "Enable fanout", "Delivery Channels", "In-App", "Web Push", "Silo Push Relay", "Relay URL", "Deployment ID", "Register Relay", "Privacy Disclosure", "Email", "Allow Per-Episode Email", "Digest Hour", "External URL", "Discord", "Client ID", "Client Secret", "Bot Token", "Allow Per-Episode DMs", "Embed Posters", "Personal Webhooks", "Max Webhooks Per Profile", "Deliveries Per Minute Per Profile", "Allow Private Destinations", "Server Channels", "Batch Window seconds", "Mention Requesters on Discord", "Settle Delay seconds", "Max Series Burst", "Max Event Age hours", "Read Notifications days", "Unread Notifications days", "Processed Events days"],
        ["Compatibility Proxies"] = ["Jellyfin", "Audiobookshelf", "Public URL", "Server Name", "Server ID", "Emulated Server Version", "Session TTL", "Playback Session TTL", "Enable Jellyfin Proxy", "Enable Audiobookshelf Proxy"],
        ["Rate Limiting"] = ["Enable Rate Limiting", "Backend", "Global Requests Per Second", "Per-IP Limits", "Requests Per Second", "Requests Per Minute", "Burst", "Standard", "Elevated", "Login", "Signup", "Setup", "Authentication endpoints"],
        ["Database"] = ["Max Connections", "Enable Redis", "Connection URL", "User DB Backend", "Pool Max Open", "Idle Timeout", "Litestream Sync Interval", "Stale Grace Seconds"],
        ["Storage"] = ["Public Assets", "Private Internal", "User DB", "Endpoint", "Region", "Path Style", "Bucket", "Key Prefix", "Access Key", "Secret Key", "URL Auth Method", "Read Endpoint", "Token Secret", "Token Param", "Token TTL seconds"],
        ["Log Retention"] = ["Retention Days", "Max Rows", "Max Size MB", "Decision Log Retention Days", "Decision Log Verbosity", "Scope Sample Rate", "Bucket Overrides"],
    };

    public AdminSettingsDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminSettingsDetailViewModel>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _apiClient = App.Services.GetRequiredService<SiloApiClient>();
        _toastService = App.Services.GetRequiredService<ToastService>();
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth);
        if (!_viewModelEventsAttached)
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModelEventsAttached = true;
        }
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

    // ===== Sidebar Nav =====

    private void BuildTabBar()
    {
        TabBar.Children.Clear();
        _tabButtons.Clear();

        var tabs = FilterSettingsTabs().ToArray();
        var groups = new (string Name, string[] Tabs)[]
        {
            ("Server", ["General", "Branding", "Theming", "Card Overlays"]),
            ("Media", ["Scanner & Matcher", "Search", "Intro Markers", "Subtitles", "AI Services", "Playback", "Downloads"]),
            ("Connections", ["Watch Providers", "Integrations", "Email", "Notifications", "Compatibility Proxies", "Rate Limiting"]),
            ("Data", ["Database", "Storage", "Log Retention"]),
        };
        foreach (var group in groups)
        {
            var groupTabs = tabs.Where(tab => group.Tabs.Contains(tab.Label)).ToArray();
            if (groupTabs.Length == 0) continue;
            TabBar.Children.Add(new TextBlock
            {
                Text = group.Name.ToUpperInvariant(), FontSize = 10, CharacterSpacing = 180,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                Margin = new Thickness(10, 10, 10, 4),
                Tag = "settingsGroupHeader",
            });
            foreach (var (label, glyph) in groupTabs)
            {
                var btn = BuildSidebarNavButton(label, glyph);
                btn.Click += TabButton_Click;
                _tabButtons.Add((btn, label));
                TabBar.Children.Add(btn);
            }
        }

        if (_tabButtons.Count == 0)
        {
            TabBar.Children.Add(new TextBlock
            {
                Text = "No matching settings",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(12, 8, 12, 8),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            var activeEntry = _tabButtons.FirstOrDefault(t => t.TabName == _activeTab);
            if (activeEntry.Button == null)
                activeEntry = _tabButtons[0];
            SetActiveTab(activeEntry.Button, activeEntry.TabName);
        }

        UpdateSettingsSearchStatus(tabs.Length);
        ApplySettingsRailMode();
    }

    private IEnumerable<(string Label, string Glyph)> FilterSettingsTabs()
    {
        var query = _settingsSearchQuery.Trim();
        if (query.Length == 0) return SettingsTabs;

        return SettingsTabs.Where(tab =>
            tab.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || SettingsSearchKeywords.TryGetValue(tab.Label, out var keywords)
            && keywords.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase))
            || SettingsSearchFields.TryGetValue(tab.Label, out var fields)
            && fields.Any(field => field.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var previousActiveTab = _activeTab;
        _settingsSearchQuery = SettingsSearchBox.Text ?? "";
        BuildTabBar();
        if (_tabButtons.Count > 0 && _activeTab != previousActiveTab)
            ShowTab(_activeTab);
    }

    private void UpdateSettingsSearchStatus(int resultCount)
    {
        if (SettingsSearchStatusText == null) return;
        var hasQuery = !string.IsNullOrWhiteSpace(_settingsSearchQuery);
        SettingsSearchStatusText.Text = hasQuery
            ? resultCount == 0 ? "No matching settings" : $"{resultCount} match{(resultCount == 1 ? "" : "es")}"
            : $"{SettingsTabs.Length} settings sections";
    }

    /// <summary>
    /// Builds a single left-sidebar nav button matching the webui layout:
    /// icon + label, full-width, rounded, with an accent pill when active.
    /// </summary>
    private Button BuildSidebarNavButton(string label, string glyph)
    {
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        };
        // Active indicator pill (3x18 px, accent, left-aligned — hidden initially)
        var indicator = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            // Web SideNavItem positions this 12px beyond the content edge. The
            // button's 12px horizontal padding makes that the rail edge.
            Margin = new Thickness(-12, 0, 4, 0),
            Tag = "indicator", // Tag for lookup in SetActiveTab
        };
        content.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
        // Shift existing columns
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(indicator, 0);
        content.Children.Add(indicator);
        content.Children.Add(icon);

        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        };
        Grid.SetColumn(text, 2);
        content.Children.Add(text);

        return new Button
        {
            Content = content,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
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
        var primaryFg = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        foreach (var (btn, _) in _tabButtons)
        {
            btn.Background = new SolidColorBrush(Colors.Transparent);
            if (btn.Content is Grid g)
            {
                foreach (var child in g.Children)
                {
                    if (child is Border ind && ind.Tag as string == "indicator") ind.Visibility = Visibility.Collapsed;
                    if (child is FontIcon ico) ico.Foreground = secondaryFg;
                    if (child is TextBlock tb) tb.Foreground = secondaryFg;
                }
            }
        }

        button.Background = accentBg;
        if (button.Content is Grid ag)
        {
            foreach (var child in ag.Children)
            {
                if (child is Border ind && ind.Tag as string == "indicator") ind.Visibility = Visibility.Visible;
                if (child is FontIcon aico) aico.Foreground = accentFg;
                if (child is TextBlock atb) atb.Foreground = primaryFg;
            }
        }
        _activeTabButton = button;
        _activeTab = tabName;
        _persistedTab = tabName;
    }

    // ===== Show Tab =====

    private void ShowTab(string tabName)
    {
        _inlineSaveBar = null;
        _inlineSaveGrid = null;
        _inlineStatusStack = null;
        _inlineSaveStatusText = null;
        _inlineDiscardButton = null;
        _inlineSaveButton = null;
        _inlineRestartButton = null;
        _inlineRestartNotice = null;
        _notificationEnabledCountText = null;
        ContentPanel.Children.Clear();
        _fieldRebuilders.Clear();
        _dirtyStateUpdaters.Clear();
        // The WebUI lets most settings surfaces fill the content column. Tabs
        // that explicitly use max-w-2xl tighten themselves in their builders.
        ContentPanel.ClearValue(FrameworkElement.MaxWidthProperty);
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

        switch (tabName)
        {
            case "General": BuildGeneralTab(); break;
            case "Branding": BuildBrandingTab(); break;
            case "Theming": BuildThemingTabCurrent(); break;
            case "Card Overlays": BuildOverlaysTab(); break;
            case "Playback": BuildPlaybackTab(); break;
            case "Scanner & Matcher": BuildScannerTab(); break;
            case "Search": BuildSearchTab(); break;
            case "Intro Markers": BuildIntroMarkersTab(); break;
            case "Subtitles": BuildSubtitlesTab(); break;
            case "AI Services": BuildAIServicesTab(); break;
            case "Rate Limiting": BuildRateLimitTab(); break;
            case "Downloads": BuildDownloadsTab(); break;
            case "Watch Providers": BuildWatchProvidersTab(); break;
            case "Integrations": BuildIntegrationsTab(); break;
            case "Email": BuildEmailTab(); break;
            case "Notifications": BuildNotificationsAdminTabCurrent(); break;
            case "Compatibility Proxies": BuildJellyfinTab(); break;
            case "Database": BuildDatabaseTab(); break;
            case "Storage": BuildStorageTab(); break;
            case "Log Retention": BuildLogRetentionTab(); break;
        }

        // Webui renders save/discard inline at the bottom of each tab's content
        // (inside the scrollable area), not as a fixed bottom strip.
        if (tabName is not ("Theming" or "AI Services" or "Watch Providers" or "Integrations" or "Subtitles"))
            AddInlineSaveBar();
    }

    /// <summary>
    /// Adds an inline save/discard bar at the bottom of ContentPanel.
    /// Visibility is bound to HasDirtyChanges.
    /// </summary>
    private void AddInlineSaveBar()
    {
        var bar = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 14, 20, 14),
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Visible,
            Tag = "inlineSaveBar",
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var statusText = new TextBlock
        {
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var restartNotice = new TextBlock
        {
            Text = "Server restart required for changes to take effect.",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            Visibility = Visibility.Collapsed,
        };
        var statusStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        statusStack.Children.Add(restartNotice);
        statusStack.Children.Add(statusText);

        var restartBtn = new Button
        {
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Visibility = Visibility.Collapsed,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "Restart Server" },
                },
            },
        };
        restartBtn.Click += RestartServer_Click;
        Grid.SetColumn(restartBtn, 1);

        var discardBtn = new Button
        {
            Content = "Discard",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
        };
        discardBtn.Click += DiscardButton_Click;
        Grid.SetColumn(discardBtn, 2);

        var saveBtn = new Button
        {
            Content = "Save Changes",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        saveBtn.Click += SaveButton_Click;
        Grid.SetColumn(saveBtn, 3);

        Grid.SetColumn(statusStack, 0);
        grid.Children.Add(statusStack);
        grid.Children.Add(restartBtn);
        grid.Children.Add(discardBtn);
        grid.Children.Add(saveBtn);
        bar.Child = grid;

        _inlineSaveBar = bar;
        _inlineSaveGrid = grid;
        _inlineStatusStack = statusStack;
        _inlineSaveStatusText = statusText;
        _inlineDiscardButton = discardBtn;
        _inlineSaveButton = saveBtn;
        _inlineRestartButton = restartBtn;
        _inlineRestartNotice = restartNotice;
        ApplyInlineSaveLayout();
        UpdateInlineSaveBar();
        ContentPanel.Children.Add(bar);
    }

    private void UpdateInlineSaveBar()
    {
        if (_inlineSaveBar is null || _inlineSaveStatusText is null) return;
        _inlineSaveBar.Visibility = Visibility.Visible;
        var count = ViewModel.DirtyCount;
        _inlineSaveStatusText.Text = count > 0
            ? $"{count} unsaved change{(count != 1 ? "s" : "")}"
            : "";
        if (_inlineDiscardButton is not null) _inlineDiscardButton.IsEnabled = count > 0 && !ViewModel.IsSaving;
        if (_inlineSaveButton is not null)
        {
            _inlineSaveButton.IsEnabled = count > 0 && !ViewModel.IsSaving;
            _inlineSaveButton.Content = ViewModel.IsSaving ? "Saving..." : "Save Changes";
        }
        var restartRequired = ViewModel.LastSaveRequiresRestart;
        if (_inlineRestartButton is not null) _inlineRestartButton.Visibility = restartRequired ? Visibility.Visible : Visibility.Collapsed;
        if (_inlineRestartNotice is not null) _inlineRestartNotice.Visibility = restartRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Tab Builders =====

    private void BuildBrandingTab()
    {
        AddSectionHeader("Identity");
        var identity = BeginCard();
        AddTextBlock(identity, "Your server name appears in the browser tab, on the login page, in the sidebar, and in the installed app. Leave blank for defaults.");
        AddTextField(identity, "Server Name", "branding.server_name", "Silo");
        AddTextField(identity, "Login Page Subtitle", "branding.login_subtitle", "Sign in with an existing account.");
        EndCard(identity);

        AddSectionHeader("Logos & Icons");
        var assets = BeginCard();
        AddTextBlock(assets, "Upload custom images to replace the Silo logo, browser favicon, and login background. Each falls back to the Silo default when not set.");
        var s3Configured = !string.IsNullOrWhiteSpace(ViewModel.GetSetting("s3.public_bucket"));
        if (!s3Configured)
            AddBrandingStorageWarning(assets);
        var assetHost = new StackPanel { Spacing = 8 };
        assets.Children.Add(assetHost);
        _ = LoadBrandingAssetsAsync(assetHost, s3Configured);
        EndCard(assets);

        AddSectionHeader("Brand Accent Color");
        var accent = BeginCard();
        AddTextBlock(accent, "A quick way to recolor the primary buttons, focus rings, and sidebar accent. For full control, use the Theming tab. Also used as the installed app theme color.");
        var accentBox = new TextBox
        {
            Text = ViewModel.GetSetting("branding.accent_color"),
            PlaceholderText = "#4f46e5",
            Width = 118,
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        accentBox.TextChanged += (_, _) => StageBrandAccent(accentBox.Text);
        var palette = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var hex in new[] { "#4f46e5", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444", "#ec4899", "#8b5cf6", "#64748b" })
        {
            var swatch = new Button
            {
                Width = 32, Height = 32, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                Background = new SolidColorBrush(ParseHexColor(hex)),
                Tag = hex,
            };
            ToolTipService.SetToolTip(swatch, $"Use accent {hex}");
            swatch.Click += (_, _) => accentBox.Text = hex;
            palette.Children.Add(swatch);
        }
        var customLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        customLabel.Children.Add(accentBox);
        customLabel.Children.Add(new TextBlock { Text = "Custom", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        palette.Children.Add(customLabel);
        var resetAccent = new Button { Content = "Reset", Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        resetAccent.Click += (_, _) => accentBox.Text = "";
        palette.Children.Add(resetAccent);
        accent.Children.Add(palette);
        _fieldRebuilders.Add(() => accentBox.Text = ViewModel.GetSetting("branding.accent_color"));
        EndCard(accent);

        AddSectionHeader("Default Theme");
        var themes = BeginCard();
        AddTextBlock(themes, "The base theme new users see until they choose their own. Users can always pick a different theme for themselves.");
        var themeButtons = new List<ToggleButton>();
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (id, label) in new[]
        {
            ("", "No default"), ("midnight-cinema", "Cinema Dark"), ("cinema-light", "Cinema Light"),
            ("cobalt-studio", "Cobalt"), ("oxblood-noir", "Oxblood"), ("evergreen-studio", "Evergreen")
        })
        {
            var button = new ToggleButton { Content = label, Tag = id, Padding = new Thickness(12, 8, 12, 8) };
            button.IsChecked = string.Equals(ViewModel.GetSetting("branding.default_theme"), id, StringComparison.Ordinal);
            button.Click += (_, _) =>
            {
                foreach (var other in themeButtons) other.IsChecked = ReferenceEquals(other, button);
                ViewModel.SetSetting("branding.default_theme", id);
                UpdateDirtyCountText();
            };
            themeButtons.Add(button);
            themeRow.Children.Add(button);
        }
        themes.Children.Add(themeRow);
        _fieldRebuilders.Add(() =>
        {
            var value = ViewModel.GetSetting("branding.default_theme");
            foreach (var button in themeButtons) button.IsChecked = string.Equals(button.Tag?.ToString(), value, StringComparison.Ordinal);
        });
        EndCard(themes);
    }

    private void AddBrandingStorageWarning(StackPanel parent)
    {
        var warning = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFB, 0xBF, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFB, 0xBF, 0x24)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 15, Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)) });
        row.Children.Add(new TextBlock
        {
            Text = "Image uploads require S3 object storage. Configure a public bucket in Storage settings to enable custom logos, favicon, and login background.",
            FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap
        });
        warning.Child = row;
        parent.Children.Add(warning);
    }

    private async Task LoadBrandingAssetsAsync(StackPanel host, bool uploadsEnabled)
    {
        var progress = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
        host.Children.Add(progress);
        try
        {
            var branding = await _settingsApi.GetServerBrandingAsync();
            host.Children.Clear();
            AddBrandingAssetRow(host, "Logo (wordmark)", "Wide logo shown in the expanded sidebar.", "wordmark", branding.WordmarkUrl, true, uploadsEnabled);
            AddBrandingAssetRow(host, "Logo (icon)", "Square mark shown in the collapsed sidebar and installed app.", "mark", branding.MarkUrl, false, uploadsEnabled);
            AddBrandingAssetRow(host, "Favicon", "Browser tab icon. PNG, ICO, or SVG.", "favicon", branding.FaviconUrl, false, uploadsEnabled);
            AddBrandingAssetRow(host, "Login Background", "Full-bleed background image for the login and signup pages.", "login_bg", branding.LoginBackgroundUrl, true, uploadsEnabled);
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            AddTextBlock(host, $"Brand assets could not be loaded: {ex.Message}");
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        _compactLayout = width < 980;
        var narrow = width < 600;
        var gutter = narrow ? 16 : _compactLayout ? 24 : 40;
        SettingsPageShell.Padding = new Thickness(gutter, _compactLayout ? 24 : 32, gutter, 40);
        SettingsTitle.FontSize = narrow ? 34 : _compactLayout ? 40 : 48;

        SettingsHeaderGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SettingsHeaderGrid.ColumnDefinitions[1].Width = _compactLayout ? new GridLength(0) : new GridLength(384);
        Grid.SetColumn(SettingsSearchPanel, _compactLayout ? 0 : 1);
        Grid.SetColumnSpan(SettingsSearchPanel, _compactLayout ? 2 : 1);
        Grid.SetRow(SettingsSearchPanel, _compactLayout ? 1 : 0);

        SettingsSurfaceGrid.ColumnDefinitions[0].Width = _compactLayout
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(240);
        SettingsSurfaceGrid.ColumnDefinitions[1].Width = _compactLayout
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SettingsRailBorder, 0);
        Grid.SetColumnSpan(SettingsRailBorder, _compactLayout ? 2 : 1);
        Grid.SetRow(SettingsRailBorder, 0);
        SettingsRailBorder.MaxHeight = double.PositiveInfinity;
        SettingsRailBorder.BorderThickness = _compactLayout
            ? new Thickness(0, 0, 0, 1)
            : new Thickness(0, 0, 1, 0);

        Grid.SetColumn(SettingsContentScroll, _compactLayout ? 0 : 1);
        Grid.SetColumnSpan(SettingsContentScroll, _compactLayout ? 2 : 1);
        Grid.SetRow(SettingsContentScroll, _compactLayout ? 1 : 0);
        SettingsContentScroll.Padding = new Thickness(narrow ? 16 : 24);
        ApplySettingsRailMode();
        ApplyInlineSaveLayout();
    }

    private void ApplySettingsRailMode()
    {
        TabBar.Orientation = _compactLayout ? Orientation.Horizontal : Orientation.Vertical;
        SettingsRailScroll.HorizontalScrollMode = _compactLayout ? ScrollMode.Enabled : ScrollMode.Disabled;
        SettingsRailScroll.HorizontalScrollBarVisibility = _compactLayout
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        SettingsRailScroll.VerticalScrollMode = _compactLayout ? ScrollMode.Disabled : ScrollMode.Enabled;
        SettingsRailScroll.VerticalScrollBarVisibility = _compactLayout
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;

        foreach (var child in TabBar.Children)
        {
            if (child is TextBlock { Tag: "settingsGroupHeader" } header)
                header.Visibility = _compactLayout ? Visibility.Collapsed : Visibility.Visible;
            else if (child is Button button)
                button.HorizontalAlignment = _compactLayout ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        }
    }

    private void ApplyInlineSaveLayout()
    {
        if (_inlineSaveGrid is null || _inlineStatusStack is null ||
            _inlineRestartButton is null || _inlineDiscardButton is null || _inlineSaveButton is null)
            return;

        Grid.SetRow(_inlineStatusStack, 0);
        Grid.SetColumn(_inlineStatusStack, 0);
        Grid.SetColumnSpan(_inlineStatusStack, _compactLayout ? 4 : 1);
        foreach (var (button, column) in new[]
        {
            (_inlineRestartButton, 1), (_inlineDiscardButton, 2), (_inlineSaveButton, 3),
        })
        {
            Grid.SetRow(button, _compactLayout ? 1 : 0);
            Grid.SetColumn(button, column);
        }
        _inlineSaveGrid.RowSpacing = _compactLayout ? 10 : 0;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_viewModelEventsAttached)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModelEventsAttached = false;
        }
        _inlineSaveBar = null;
        _inlineSaveGrid = null;
        _inlineStatusStack = null;
        _inlineSaveStatusText = null;
        _inlineDiscardButton = null;
        _inlineSaveButton = null;
        _inlineRestartButton = null;
        _inlineRestartNotice = null;
        _notificationEnabledCountText = null;
        _adminThemeVarsSaveCts?.Cancel();
        _adminThemeVarsSaveCts?.Dispose();
        _adminThemeVarsSaveCts = null;
        _adminThemeCssSaveCts?.Cancel();
        _adminThemeCssSaveCts?.Dispose();
        _adminThemeCssSaveCts = null;
        ViewModel.CancelLoad();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.HasDirtyChanges) or nameof(ViewModel.DirtyCount)
            or nameof(ViewModel.IsSaving) or nameof(ViewModel.LastSaveRequiresRestart))
        {
            DispatcherQueue.TryEnqueue(UpdateInlineSaveBar);
            foreach (var updater in _dirtyStateUpdaters.ToArray())
                DispatcherQueue.TryEnqueue(() => updater());
        }
    }

    private void AddBrandingAssetRow(StackPanel host, string label, string description, string kind,
        string? initialUrl, bool widePreview, bool uploadsEnabled)
    {
        string? currentUrl = initialUrl;
        var row = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12)
        };
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widePreview ? 112 : 52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var preview = new Border
        {
            Width = widePreview ? 104 : 44, Height = 44, CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        SetBrandingPreview(preview, currentUrl);
        grid.Children.Add(preview);

        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var upload = new Button
        {
            Content = string.IsNullOrWhiteSpace(currentUrl) ? "Upload" : "Replace",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"], IsEnabled = uploadsEnabled
        };
        var remove = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }, Width = 32, Height = 32, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            Visibility = string.IsNullOrWhiteSpace(currentUrl) ? Visibility.Collapsed : Visibility.Visible
        };
        ToolTipService.SetToolTip(remove, $"Remove {label}");

        upload.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");
            if (kind == "favicon") { picker.FileTypeFilter.Add(".ico"); picker.FileTypeFilter.Add(".svg"); }
            else { picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg"); }
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            upload.IsEnabled = false;
            try
            {
                var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
                var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
                var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
                var result = await _settingsApi.UploadBrandingAssetAsync(kind, file.Name, bytes, contentType);
                currentUrl = result.Url;
                SetBrandingPreview(preview, currentUrl);
                upload.Content = "Replace";
                remove.Visibility = Visibility.Visible;
                ShowStatusToast($"{label} uploaded.");
            }
            catch (Exception ex) { ShowStatusToast($"Upload failed: {ex.Message}"); }
            finally { upload.IsEnabled = uploadsEnabled; }
        };

        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            try
            {
                await _settingsApi.DeleteBrandingAssetAsync(kind);
                currentUrl = null;
                SetBrandingPreview(preview, null);
                upload.Content = "Upload";
                remove.Visibility = Visibility.Collapsed;
                ShowStatusToast($"{label} removed.");
            }
            catch (Exception ex) { ShowStatusToast($"Remove failed: {ex.Message}"); }
            finally { remove.IsEnabled = true; }
        };

        actions.Children.Add(upload);
        actions.Children.Add(remove);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        row.Child = grid;
        host.Children.Add(row);
    }

    private void SetBrandingPreview(Border preview, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            preview.Child = new FontIcon { Glyph = "\uEB9F", FontSize = 18, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return;
        }
        var absolute = Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            ? parsed
            : new Uri(_apiClient.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/'));
        preview.Child = new Image { Source = new BitmapImage(absolute), Stretch = Stretch.Uniform };
    }

    private void StageBrandAccent(string value)
    {
        ViewModel.SetSetting("branding.accent_color", value);
        Dictionary<string, string> variables;
        try { variables = JsonSerializer.Deserialize<Dictionary<string, string>>(ViewModel.GetSetting("ui.admin_theme_vars")) ?? []; }
        catch { variables = []; }
        foreach (var key in new[] { "primary", "ring", "sidebar-primary" })
        {
            if (string.IsNullOrWhiteSpace(value)) variables.Remove(key);
            else variables[key] = value;
        }
        ViewModel.SetSetting("ui.admin_theme_vars", JsonSerializer.Serialize(variables));
        UpdateDirtyCountText();
    }

    private static Color ParseHexColor(string hex)
    {
        var value = hex.TrimStart('#');
        return value.Length == 6
            ? Color.FromArgb(0xFF, Convert.ToByte(value[..2], 16), Convert.ToByte(value.Substring(2, 2), 16), Convert.ToByte(value.Substring(4, 2), 16))
            : Colors.Transparent;
    }

    private void BuildSearchTab()
    {
        AddTabHeader("Search", "Configure catalog search provider selection, Meilisearch connectivity, and index status.");

        AddSectionHeader("Provider");
        var provider = BeginCard();
        AddSelectField(provider, "Preferred Provider", "catalog.search.provider",
            [("postgres", "Postgres FTS"), ("meilisearch", "Meilisearch")]);
        EndCard(provider);

        AddSectionHeader("Meilisearch");
        var meili = BeginCard();
        AddTextField(meili, "URL", "catalog.search.meilisearch.url", "http://localhost:7700");
        AddPasswordField(meili, "API Key", "catalog.search.meilisearch.api_key");
        AddTextField(meili, "Index Prefix", "catalog.search.meilisearch.index", "silo_media_items");
        AddNumberField(meili, "Timeout (ms)", "catalog.search.meilisearch.timeout_ms", "800");
        AddSelectField(meili, "Matching Strategy", "catalog.search.meilisearch.matching_strategy",
            [("last", "Last"), ("all", "All")]);
        AddNumberField(meili, "Sync Batch Size", "catalog.search.meilisearch.sync_batch_size", "500");
        AddNumberField(meili, "Rebuild Batch Size", "catalog.search.meilisearch.rebuild_batch_size", "5000");
        AddNumberField(meili, "Rebuild Queue Depth", "catalog.search.meilisearch.rebuild_task_queue_depth", "4");
        AddTextField(meili, "Indexed Types", "catalog.search.meilisearch.index_types", "all, video, or movie,series");
        AddToggleField(meili, "Semantic Search", "catalog.search.meilisearch.semantic_enabled", "Uses recommendation embeddings for hybrid catalog search.");
        AddNumberField(meili, "Semantic Ratio", "catalog.search.meilisearch.semantic_ratio", "0.50");
        AddTextField(meili, "Embedder", "catalog.search.meilisearch.embedder", "silo_recommendations");
        AddToggleField(meili, "Binary Quantized Vectors", "catalog.search.meilisearch.binary_quantized", "Reduces vector storage with a small semantic-relevance cost; changing it requires a full index rebuild.");
        AddConnectionCheckButton(meili, "meilisearch", "Check Connection");
        EndCard(meili);

        AddSectionHeader("Status");
        var status = BeginCard();
        var statusHost = new StackPanel { Spacing = 0 };
        status.Children.Add(statusHost);
        _ = LoadCatalogSearchStatusAsync(statusHost);
        EndCard(status);
    }

    private async Task LoadCatalogSearchStatusAsync(StackPanel host)
    {
        host.Children.Clear();
        host.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 16, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        try
        {
            var status = await _settingsApi.GetCatalogSearchStatusAsync();
            host.Children.Clear();

            AddSearchStatusRow(host, "Active Provider",
                status.ActiveProvider == "meilisearch" ? "Meilisearch" : "Postgres FTS",
                status.ConfiguredProvider);
            AddSearchStatusRow(host, "Health",
                status.Meilisearch.Healthy ? "Healthy" : status.Meilisearch.CircuitState,
                status.Meilisearch.Configured ? "configured" : "not configured");
            AddSearchStatusRow(host, "Active Index",
                string.IsNullOrWhiteSpace(status.Index.ActiveIndexUid) ? "Not built" : status.Index.ActiveIndexUid,
                $"schema {status.Index.SchemaVersion}/{status.Index.ExpectedSchemaVersion}");
            AddSearchStatusRow(host, "Documents", status.Index.DocumentCount.ToString());
            AddSearchStatusRow(host, "Indexed Types",
                status.Meilisearch.IndexTypes.Count == 0 ? "All" : string.Join(", ", status.Meilisearch.IndexTypes));
            AddSearchStatusRow(host, "Binary Quantized", status.Meilisearch.BinaryQuantized ? "Enabled" : "Disabled");
            AddSearchStatusRow(host, "Semantic Search", status.Meilisearch.SemanticEnabled ? "Enabled" : "Disabled",
                status.Meilisearch.Embedder);
            AddSearchStatusRow(host, "Semantic Ratio",
                FormattableString.Invariant($"{status.Meilisearch.SemanticRatio:0.00}"));
            AddSearchStatusRow(host, "Vectorized Documents", status.Index.VectorDocumentCount.ToString());

            if (status.Semantic is { } semantic)
            {
                AddSearchStatusRow(host, "Semantic Readiness", semantic.Ready ? "Ready" : "Not ready",
                    semantic.Ready ? null : semantic.DisabledReason);
                AddSearchStatusRow(host, "Vector Coverage", FormatSearchPercent(semantic.VectorCoverageRatio));
                AddSearchStatusRow(host, "Coverage Updated", FormatSearchStatusDate(semantic.CoverageUpdatedAt));
                AddSearchStatusRow(host, "Embedder Capability",
                    semantic.Capability.Ok ? "OK" : semantic.Capability.Reason ?? "Unavailable",
                    semantic.Capability.Embedder);

                if (semantic.PerType.Count > 0)
                    AddSearchTypeCoverage(host, semantic.PerType);
            }

            AddSearchStatusRow(host, "Pending Events", status.Index.PendingEvents.ToString());
            if (status.Index.DeadLetteredEvents > 0)
                AddSearchStatusRow(host, "Dead-lettered Events", status.Index.DeadLetteredEvents.ToString(), "stale until rebuild");
            AddSearchStatusRow(host, "Last Sync", FormatSearchStatusDate(status.Index.LastSyncAt));
            if (!string.IsNullOrWhiteSpace(status.Meilisearch.LastFallback))
                AddSearchStatusRow(host, "Last Fallback", status.Meilisearch.LastFallback!);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Padding = new Thickness(0, 12, 0, 4)
            };
            actions.Children.Add(CreateSearchTaskButton("Rebuild Index", "rebuild_catalog_search_index", false));
            actions.Children.Add(CreateSearchTaskButton("Sync History", "sync_catalog_search_index", true));
            host.Children.Add(actions);
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            var error = new StackPanel { Spacing = 10, Padding = new Thickness(0, 12, 0, 8) };
            error.Children.Add(new TextBlock
            {
                Text = $"Search status could not be loaded: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            });
            var retry = new Button { Content = "Retry", HorizontalAlignment = HorizontalAlignment.Left };
            retry.Click += async (_, _) => await LoadCatalogSearchStatusAsync(host);
            error.Children.Add(retry);
            host.Children.Add(error);
        }
    }

    private void AddSearchStatusRow(StackPanel host, string label, string value, string? badge = null)
    {
        if (host.Children.Count > 0)
            AddDivider(host);

        var row = new Grid { Padding = new Thickness(0, 12, 0, 12), ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var valuePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        valuePanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "Never" : value,
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            TextAlignment = TextAlignment.Right
        });
        if (!string.IsNullOrWhiteSpace(badge))
            valuePanel.Children.Add(CreateSearchStatusBadge(badge));
        Grid.SetColumn(valuePanel, 1);
        row.Children.Add(valuePanel);
        host.Children.Add(row);
    }

    private static Border CreateSearchStatusBadge(string text) => new()
    {
        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 2, 7, 2),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        }
    };

    private void AddSearchTypeCoverage(StackPanel host, IReadOnlyList<CatalogSearchTypeCoverage> coverage)
    {
        AddDivider(host);
        var section = new StackPanel { Spacing = 4, Padding = new Thickness(0, 12, 0, 12) };
        section.Children.Add(new TextBlock { Text = "Per-Type Coverage", FontSize = 14, FontWeight = FontWeights.SemiBold });
        foreach (var item in coverage)
        {
            var row = new Grid { Padding = new Thickness(0, 6, 0, 6), ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{item.Type}: {item.Vectorized}/{item.Eligible} ({FormatSearchPercent(item.VectorCoverageRatio)})",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var badge = CreateSearchStatusBadge(item.Ready ? "Ready" : "Not ready");
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            section.Children.Add(row);
        }
        host.Children.Add(section);
    }

    private Button CreateSearchTaskButton(string label, string taskKey, bool ghost)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 6, 12, 6),
            Background = ghost ? new SolidColorBrush(Colors.Transparent) : null,
            BorderThickness = ghost ? new Thickness(0) : new Thickness(1)
        };
        button.Click += (_, _) => Frame.Navigate(typeof(AdminTaskDetailPage), taskKey);
        return button;
    }

    private static string FormatSearchPercent(double value) => $"{Math.Round(value * 100):0}%";

    private static string FormatSearchStatusDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, out var parsed)) return "Never";
        return SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(parsed);
    }

    private void BuildThemingTab()
    {
        // Warning banner — matches webui ThemeSettings top banner about server-wide scope.
        var warnBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFB, 0xBF, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFB, 0xBF, 0x24)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var warnRow = new Grid { ColumnSpacing = 10 };
        warnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        warnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var warnIcon = new FontIcon
        {
            Glyph = "\uE7BA",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetColumn(warnIcon, 0);
        warnRow.Children.Add(warnIcon);
        var warnText = new StackPanel { Spacing = 2 };
        warnText.Children.Add(new TextBlock
        {
            Text = "Server-wide theme customization",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
        });
        warnText.Children.Add(new TextBlock
        {
            Text = "These overrides apply to all users as a base layer. Individual users can further customize on top of these settings.",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(warnText, 1);
        warnRow.Children.Add(warnText);
        warnBorder.Child = warnRow;
        ContentPanel.Children.Add(warnBorder);

        AddSectionHeader("Theme Catalog");
        var catalogCard = BeginCard();
        AddTextBlock(catalogCard, "URL of the community theme catalog JSON index. Users browse this in their settings.");
        AddTextField(catalogCard, "Theme Catalog URL", "theme.catalog_url",
            "https://raw.githubusercontent.com/Silo-Server/silo-themes/main/catalog.json");
        EndCard(catalogCard);

        AddSectionHeader("Custom CSS");
        var cssCard = BeginCard();
        AddTextBlock(cssCard, "Raw CSS appended to the admin UI. Use with caution — invalid CSS can break layouts.");
        AddMultilineTextField(cssCard, "Custom CSS", "ui.admin_custom_css",
            "/* Custom CSS applied to every page */\n");
        EndCard(cssCard);

        AddSectionHeader("Token Overrides");

        // Parse existing vars from the JSON setting
        var rawVars = ViewModel.GetSetting("ui.admin_theme_vars");
        Dictionary<string, string> currentVars = new();
        try
        {
            if (!string.IsNullOrWhiteSpace(rawVars))
            {
                var doc = System.Text.Json.JsonDocument.Parse(rawVars);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    currentVars[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        catch { }

        // Token registry matching webui lib/themeTokens.ts THEME_TOKENS
        var tokenGroups = new (string Group, (string Token, string Label, string Type)[] Tokens)[]
        {
            ("Surfaces", new[]
            {
                ("background", "Background", "color"), ("foreground", "Foreground", "color"),
                ("card", "Card", "color"), ("card-foreground", "Card Text", "color"),
                ("surface", "Surface", "color"), ("surface-hover", "Surface Hover", "color"),
                ("surface-raised", "Surface Raised", "color"),
            }),
            ("Interactive", new[]
            {
                ("primary", "Primary", "color"), ("primary-foreground", "Primary Text", "color"),
                ("accent", "Accent", "color"), ("accent-foreground", "Accent Text", "color"),
                ("muted", "Muted", "color"), ("muted-foreground", "Muted Text", "color"),
                ("destructive", "Destructive", "color"), ("destructive-foreground", "Destructive Text", "color"),
                ("ambient", "Ambient Glow", "color"),
            }),
            ("Sidebar", new[]
            {
                ("sidebar", "Sidebar", "color"), ("sidebar-foreground", "Sidebar Text", "color"),
                ("sidebar-primary", "Sidebar Primary", "color"), ("sidebar-accent", "Sidebar Accent", "color"),
                ("sidebar-border", "Sidebar Border", "color"),
            }),
            ("Borders & Focus", new[]
            {
                ("border", "Border", "color"), ("input", "Input Border", "color"), ("ring", "Focus Ring", "color"),
            }),
            ("Shape & Font", new[]
            {
                ("radius", "Border Radius", "radius"), ("font-body", "Font Family", "font"),
            }),
        };

        // Two-column layout: token fields on left, preview card on right
        var tokenLayout = new Grid { ColumnSpacing = 20, Margin = new Thickness(0, 4, 0, 0) };
        tokenLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tokenLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        var tokenEditor = new StackPanel { Spacing = 16 };
        var textInputs = new Dictionary<string, TextBox>();

        // Serialize current token values back to JSON whenever any input changes
        void PersistTokenVars()
        {
            var vars = new Dictionary<string, string>();
            foreach (var (key, box) in textInputs)
            {
                var val = box.Text?.Trim();
                if (!string.IsNullOrEmpty(val)) vars[key] = val;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(vars);
            ViewModel.SetSetting("ui.admin_theme_vars", json);
            UpdateDirtyCountText();
            RefreshThemePreview();
        }

        foreach (var (group, tokens) in tokenGroups)
        {
            var groupPanel = new StackPanel { Spacing = 6 };
            groupPanel.Children.Add(new TextBlock
            {
                Text = group.ToUpperInvariant(),
                FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 80,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                Margin = new Thickness(0, 0, 0, 2),
            });

            foreach (var (token, label, inputType) in tokens)
            {
                var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var tokenLabel = new TextBlock
                {
                    Text = label, FontSize = 12, FontWeight = FontWeights.Medium,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(tokenLabel, 0);
                row.Children.Add(tokenLabel);

                var input = new TextBox
                {
                    Text = currentVars.GetValueOrDefault(token, ""),
                    PlaceholderText = inputType == "color" ? "oklch(...) or #hex" : inputType == "radius" ? "0.5rem" : "Inter, sans-serif",
                    FontSize = 12, CornerRadius = new CornerRadius(6),
                    FontFamily = new FontFamily("Consolas"),
                };
                input.TextChanged += (_, _) => PersistTokenVars();
                textInputs[token] = input;
                Grid.SetColumn(input, 1);
                row.Children.Add(input);

                groupPanel.Children.Add(row);
            }
            tokenEditor.Children.Add(groupPanel);
        }
        Grid.SetColumn(tokenEditor, 0);
        tokenLayout.Children.Add(tokenEditor);

        // Preview card — shows a small sample with accent/bg/fg/card colors applied
        var previewHost = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Spacing = 8 };
        previewHost.Children.Add(new TextBlock
        {
            Text = "PREVIEW",
            FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 80,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
        });
        _themePreviewHost = previewHost;
        RefreshThemePreview();
        Grid.SetColumn(previewHost, 1);
        tokenLayout.Children.Add(previewHost);

        // Validation warning
        var themeWarning = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 90, 90)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _fieldRebuilders.Add(() =>
        {
            var raw = ViewModel.GetSetting("ui.admin_theme_vars");
            themeWarning.Text = ValidateThemeVarsJson(raw);
        });

        var tokenCard = BeginCard();

        // Header row with description + Reset All button
        var tokenHeaderRow = new Grid();
        tokenHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tokenHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tokenHeaderRow.Children.Add(new TextBlock
        {
            Text = "Override individual design tokens. Clear a field to use the theme default.",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var resetAllBtn = new Button
        {
            Content = "Reset All",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Top,
        };
        resetAllBtn.Click += (_, _) =>
        {
            foreach (var (_, box) in textInputs) box.Text = "";
            PersistTokenVars();
        };
        Grid.SetColumn(resetAllBtn, 1);
        tokenHeaderRow.Children.Add(resetAllBtn);
        tokenCard.Children.Add(tokenHeaderRow);

        tokenCard.Children.Add(tokenLayout);
        tokenCard.Children.Add(themeWarning);
        EndCard(tokenCard);
    }

    /// <summary>
    /// Current GitHub WebUI theme settings: warning, live preview, token editor,
    /// autosaved CSS, then the catalog URL saved on blur.
    /// </summary>
    private void BuildThemingTabCurrent()
    {
        var warning = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFB, 0xBF, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFB, 0xBF, 0x24)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
        };
        var warningGrid = new Grid { ColumnSpacing = 12 };
        warningGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        warningGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        warningGrid.Children.Add(new FontIcon
        {
            Glyph = "\uE7BA", FontSize = 16, VerticalAlignment = VerticalAlignment.Top,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
        });
        var warningCopy = new StackPanel { Spacing = 4 };
        warningCopy.Children.Add(new TextBlock
        {
            Text = "Server-wide theme customization", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
        });
        warningCopy.Children.Add(new TextBlock
        {
            Text = "These overrides apply to all users as a base layer. Individual users can further customize on top of these settings.",
            FontSize = 13, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(warningCopy, 1);
        warningGrid.Children.Add(warningCopy);
        warning.Child = warningGrid;
        ContentPanel.Children.Add(warning);

        Dictionary<string, string> variables;
        try { variables = JsonSerializer.Deserialize<Dictionary<string, string>>(ViewModel.GetSetting("ui.admin_theme_vars")) ?? []; }
        catch { variables = []; }

        AddSectionHeader("Preview");
        var previewHost = new StackPanel { Spacing = 8 };
        _themePreviewHost = previewHost;
        RefreshThemePreview();
        ContentPanel.Children.Add(previewHost);

        var resetAll = new Button
        {
            Content = "↶  Reset all", Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12,
        };
        ContentPanel.Children.Add(resetAll);

        TextBox? cssBox = null;
        void UpdateResetVisibility() => resetAll.Visibility = variables.Count > 0 ||
            !string.IsNullOrWhiteSpace(cssBox?.Text ?? ViewModel.GetSetting("ui.admin_custom_css"))
            ? Visibility.Visible : Visibility.Collapsed;

        void PersistVariables()
        {
            ViewModel.SetSetting("ui.admin_theme_vars", JsonSerializer.Serialize(variables));
            RefreshThemePreview();
            UpdateResetVisibility();
            ScheduleAdminThemeSave("ui.admin_theme_vars", 500);
        }

        AddSectionHeader("Token Overrides");
        var tokenCard = BeginCard();
        var tokenGroups = new (string Group, (string Token, string Label)[] Tokens)[]
        {
            ("Surfaces", [("background", "Background"), ("foreground", "Foreground"), ("card", "Card"),
                ("card-foreground", "Card Text"), ("popover", "Popover"), ("popover-foreground", "Popover Text"),
                ("surface", "Surface"), ("surface-hover", "Surface Hover"), ("surface-raised", "Surface Raised")]),
            ("Interactive", [("primary", "Primary"), ("primary-foreground", "Primary Text"),
                ("secondary", "Secondary"), ("secondary-foreground", "Secondary Text"), ("muted", "Muted"),
                ("muted-foreground", "Muted Text"), ("accent", "Accent"), ("accent-foreground", "Accent Text"),
                ("destructive", "Destructive"), ("destructive-foreground", "Destructive Text"), ("ambient", "Ambient Glow")]),
            ("Sidebar", [("sidebar", "Sidebar"), ("sidebar-foreground", "Sidebar Text"),
                ("sidebar-primary", "Sidebar Primary"), ("sidebar-primary-foreground", "Sidebar Primary Text"),
                ("sidebar-accent", "Sidebar Accent"), ("sidebar-accent-foreground", "Sidebar Accent Text"),
                ("sidebar-border", "Sidebar Border"), ("sidebar-section-divider", "Sidebar Section Divider"),
                ("sidebar-ring", "Sidebar Ring")]),
            ("Borders & Focus", [("border", "Border"), ("input", "Input Border"), ("ring", "Focus Ring")]),
        };

        foreach (var (groupName, tokens) in tokenGroups)
        {
            tokenCard.Children.Add(new TextBlock
            {
                Text = groupName.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 100, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                Margin = new Thickness(0, tokenCard.Children.Count == 0 ? 0 : 12, 0, 4),
            });
            foreach (var (token, label) in tokens)
            {
                var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var value = variables.GetValueOrDefault(token);
                var swatch = new Button
                {
                    Width = 32, Height = 32, Padding = new Thickness(0), CornerRadius = new CornerRadius(8),
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1),
                    Background = IsHexThemeColor(value) ? new SolidColorBrush(ParseHexColor(value!)) : (Brush)Application.Current.Resources["SurfaceBrush"],
                };
                ToolTipService.SetToolTip(swatch, label);
                swatch.Click += async (_, _) =>
                {
                    var picker = new ColorPicker
                    {
                        Color = IsHexThemeColor(variables.GetValueOrDefault(token)) ? ParseHexColor(variables[token]) : Colors.Black,
                        IsAlphaEnabled = false, IsColorChannelTextInputVisible = true,
                    };
                    var dialog = new ContentDialog
                    {
                        Title = label, Content = picker, PrimaryButtonText = "Apply", CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot,
                    };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    variables[token] = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
                    PersistVariables();
                    ShowTab("Theming");
                };
                row.Children.Add(swatch);
                var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                copy.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.Medium });
                copy.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? "Theme default" : value,
                    FontSize = 11, FontFamily = new FontFamily("Consolas"),
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                Grid.SetColumn(copy, 1);
                row.Children.Add(copy);
                var reset = new Button
                {
                    Content = "↶", Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    Padding = new Thickness(8), Visibility = variables.ContainsKey(token) ? Visibility.Visible : Visibility.Collapsed,
                };
                ToolTipService.SetToolTip(reset, "Reset to theme default");
                reset.Click += (_, _) => { variables.Remove(token); PersistVariables(); ShowTab("Theming"); };
                Grid.SetColumn(reset, 2);
                row.Children.Add(reset);
                tokenCard.Children.Add(row);
            }
        }

        tokenCard.Children.Add(new TextBlock
        {
            Text = "SHAPE & FONT", FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 100,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"], Margin = new Thickness(0, 12, 0, 4),
        });
        var radiusValue = ParseThemeRadius(variables.GetValueOrDefault("radius"));
        var radiusHeader = new Grid();
        radiusHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        radiusHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        radiusHeader.Children.Add(new TextBlock { Text = "Border Radius", FontSize = 13, FontWeight = FontWeights.Medium });
        var radiusText = new TextBlock { Text = $"{radiusValue:0.##}rem", FontSize = 11, FontFamily = new FontFamily("Consolas") };
        Grid.SetColumn(radiusText, 1); radiusHeader.Children.Add(radiusText); tokenCard.Children.Add(radiusHeader);
        var radiusSlider = new Slider { Minimum = 0, Maximum = 1.5, StepFrequency = 0.05, Value = radiusValue };
        var radiusReady = false;
        radiusSlider.Loaded += (_, _) => radiusReady = true;
        radiusSlider.ValueChanged += (_, _) =>
        {
            if (!radiusReady) return;
            variables["radius"] = $"{radiusSlider.Value:0.##}rem";
            radiusText.Text = variables["radius"];
            PersistVariables();
        };
        tokenCard.Children.Add(radiusSlider);

        tokenCard.Children.Add(new TextBlock { Text = "Font Family", FontSize = 13, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 8, 0, 0) });
        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var font in new[] { "Outfit", "Sora", "Urbanist", "Manrope" })
        {
            var selected = variables.GetValueOrDefault("font-body", "").Contains(font, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = font, FontFamily = new FontFamily(font),
                Style = (Style)Application.Current.Resources[selected ? "AccentButtonStyle" : "OutlineButtonStyle"],
                Padding = new Thickness(12, 6, 12, 6),
            };
            button.Click += (_, _) => { variables["font-body"] = $"\"{font}\", sans-serif"; PersistVariables(); ShowTab("Theming"); };
            fontRow.Children.Add(button);
        }
        tokenCard.Children.Add(fontRow);
        EndCard(tokenCard);

        AddSectionHeader("Custom CSS");
        var cssCard = BeginCard();
        cssBox = new TextBox
        {
            Text = ViewModel.GetSetting("ui.admin_custom_css"), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MinHeight = 220, MaxHeight = 420, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            PlaceholderText = "/* Custom CSS applied to every page */",
        };
        ScrollViewer.SetVerticalScrollBarVisibility(cssBox, ScrollBarVisibility.Auto);
        var cssReady = false;
        cssBox.Loaded += (_, _) => cssReady = true;
        cssBox.TextChanged += (_, _) =>
        {
            if (!cssReady) return;
            ViewModel.SetSetting("ui.admin_custom_css", SanitizeAdminThemeCss(cssBox.Text ?? ""));
            UpdateResetVisibility();
            ScheduleAdminThemeSave("ui.admin_custom_css", 1000);
        };
        cssCard.Children.Add(cssBox);
        EndCard(cssCard);

        AddSectionHeader("Theme Catalog URL");
        var catalogCard = BeginCard();
        AddTextBlock(catalogCard, "URL of the community theme catalog JSON index. Users browse this in their settings.");
        var catalog = new TextBox
        {
            Text = ViewModel.GetSetting("theme.catalog_url"),
            PlaceholderText = "https://raw.githubusercontent.com/Silo-Server/silo-themes/main/catalog.json",
            HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(12),
        };
        if (string.IsNullOrWhiteSpace(catalog.Text)) catalog.Text = "https://raw.githubusercontent.com/Silo-Server/silo-themes/main/catalog.json";
        catalog.LostFocus += async (_, _) =>
        {
            ViewModel.SetSetting("theme.catalog_url", catalog.Text?.Trim() ?? "");
            await ViewModel.SaveSettingsAsync(["theme.catalog_url"]);
        };
        catalogCard.Children.Add(catalog);
        EndCard(catalogCard);

        resetAll.Click += async (_, _) =>
        {
            variables.Clear();
            ViewModel.SetSetting("ui.admin_theme_vars", "{}");
            ViewModel.SetSetting("ui.admin_custom_css", "");
            await ViewModel.SaveSettingsAsync(["ui.admin_theme_vars", "ui.admin_custom_css"]);
            ShowTab("Theming");
        };
        UpdateResetVisibility();
    }

    private void ScheduleAdminThemeSave(string key, int delayMilliseconds)
    {
        var owner = new CancellationTokenSource();
        ref var slot = ref (key == "ui.admin_theme_vars" ? ref _adminThemeVarsSaveCts : ref _adminThemeCssSaveCts);
        var previous = Interlocked.Exchange(ref slot, owner);
        previous?.Cancel();
        _ = SaveAdminThemeSettingAfterDelayAsync(key, delayMilliseconds, owner);
    }

    private async Task SaveAdminThemeSettingAfterDelayAsync(string key, int delayMilliseconds, CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(delayMilliseconds, owner.Token);
            await ViewModel.SaveSettingsAsync([key]);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        finally
        {
            if (key == "ui.admin_theme_vars") Interlocked.CompareExchange(ref _adminThemeVarsSaveCts, null, owner);
            else Interlocked.CompareExchange(ref _adminThemeCssSaveCts, null, owner);
            owner.Dispose();
        }
    }

    private static bool IsHexThemeColor(string? value)
        => value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static double ParseThemeRadius(string? value)
        => double.TryParse(value?.Replace("rem", "", StringComparison.OrdinalIgnoreCase), out var parsed)
            ? Math.Clamp(parsed, 0, 1.5) : 0.5;

    private static string SanitizeAdminThemeCss(string css)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(css,
            "@import\\s+(?:url\\(.*?\\)|['\"].*?['\"])[^;]*;?", "/* [blocked @import] */",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(result,
            "url\\(\\s*(['\"]?)([\\s\\S]*?)\\1\\s*\\)", match =>
            {
                var value = match.Groups[2].Value.Trim().Trim('\'', '\"');
                var safe = value.Length == 0 || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    (value.StartsWith('/') && !value.StartsWith("//")) || value.StartsWith('#') ||
                    (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z][a-z0-9+.-]*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && !value.StartsWith("//"));
                return safe ? match.Value : "/* [blocked external url] */";
            }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Multi-line text area field for larger content like raw CSS / JSON.
    /// Dirty-tracks via ViewModel.SetSetting.
    /// </summary>
    private void AddMultilineTextField(StackPanel parent, string label, string key, string? placeholder = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });

        var box = new TextBox
        {
            Text = ViewModel.GetSetting(key),
            PlaceholderText = placeholder ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 140,
            MaxHeight = 260,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        box.TextChanged += (_, _) =>
        {
            ViewModel.SetSetting(key, box.Text ?? "");
            UpdateDirtyCountText();
        };
        field.Children.Add(box);

        _fieldRebuilders.Add(() => box.Text = ViewModel.GetSetting(key));
        parent.Children.Add(field);
    }

    private void BuildDownloadsTab()
    {
        AddTabHeader("Downloads", "Configure download permissions, bandwidth limits, and quotas.");

        AddSectionHeader("General");
        var genCard = BeginCard();
        AddToggleField(genCard, "Downloads Enabled", "download.enabled", "Allow users to download media files");
        EndCard(genCard);

        AddSectionHeader("Bandwidth Limits");
        var bwCard = BeginCard();
        AddTextField(bwCard, "Server Bandwidth (Mbps)", "download.server_bandwidth_mbps",
            "Total download bandwidth for the entire server in megabits/sec. 0 = unlimited.");
        AddTextField(bwCard, "Per-User Bandwidth (Mbps)", "download.user_bandwidth_mbps",
            "Max download bandwidth per user, shared across active downloads. 0 = unlimited.");
        EndCard(bwCard);

        AddSectionHeader("Quantity Limits");
        var qtyCard = BeginCard();
        AddTextField(qtyCard, "Max Concurrent Downloads Per User", "download.max_concurrent_per_user",
            "How many downloads a user can have active at once. 0 = unlimited.");
        AddTextField(qtyCard, "Max Downloads Per Period", "download.max_per_period",
            "Total downloads a user can create per period. 0 = unlimited.");
        AddTextField(qtyCard, "Period Duration", "download.period_duration",
            "Rolling window for the per-period limit (e.g., 24h, 168h, 720h)");
        EndCard(qtyCard);

        AddSectionHeader("Offline Sync (Prepared Downloads)");
        var offlineCard = BeginCard();
        AddToggleField(offlineCard, "Transcode-to-File Enabled", "download.transcode_enabled",
            "Allow server-side transcode of downloads to a device-friendly file. Requires the per-user download-transcode permission. Downloaded files persist on-device until the user deletes them — there is no expiry or revocation of files already downloaded.");
        AddTextField(offlineCard, "Artifact Directory", "download.artifact_dir",
            "Where prepared (remux/transcode) download files are written. Empty = a 'downloads' subdirectory under the transcode directory.");
        AddTextField(offlineCard, "Max Concurrent Prepares", "download.max_concurrent_prepares",
            "Encode/remux worker-pool size for preparing download files.");
        AddTextField(offlineCard, "Artifact Storage Budget (bytes)", "download.artifact_max_bytes",
            "LRU eviction budget for prepared download files. 0 = unlimited.");
        EndCard(offlineCard);
    }

    private void BuildGeneralTab()
    {
        AddTabHeader("General", "Authentication, token lifetimes, networking, and server logging behavior.");

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

        AddSectionHeader("Network");
        var networkCard = BeginCard();
        AddTextField(networkCard, "Trusted Proxies", "clientip.trusted_proxies",
            "Comma-separated CIDRs of reverse proxies whose X-Forwarded-For is trusted, e.g. 172.16.0.0/12, 203.0.113.7/32. Applies without a restart.");
        var proxyHelp = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 4)
        };
        var proxyHelpContent = new StackPanel { Spacing = 6 };
        proxyHelpContent.Children.Add(new TextBlock { Text = "Choosing trusted proxy ranges", FontSize = 14, FontWeight = FontWeights.SemiBold });
        foreach (var line in new[]
                 {
                     "• Setting this replaces the defaults (private ranges 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 and loopback). Leave it empty to keep them.",
                     "• Recommended: keep the defaults, and only add your proxy's public address as a /32 if it reaches Silo from outside those ranges.",
                     "• CDNs such as Cloudflare connect from many published IP ranges — list all of their CIDRs and keep the list current.",
                     "• Avoid 0.0.0.0/0: any client could spoof its IP with a forged X-Forwarded-For header, affecting rate limits and audit logs."
                 })
            proxyHelpContent.Children.Add(new TextBlock
            {
                Text = line, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            });
        proxyHelp.Child = proxyHelpContent;
        networkCard.Children.Add(proxyHelp);
        EndCard(networkCard);
    }

    private void BuildPlaybackTab()
    {
        AddTabHeader("Playback", "Configure transcoding, segment generation, and watched-state behavior.");

        AddSectionHeader("Transcoding");
        var tcCard = BeginCard();
        AddTextField(tcCard, "FFmpeg Path", "playback.ffmpeg_path");
        AddTextField(tcCard, "Transcode Directory", "playback.transcode_dir");
        AddSelectField(tcCard, "Hardware Acceleration", "playback.hw_accel",
            [("auto", "Auto"), ("qsv", "Intel Quick Sync (QSV)"), ("vaapi", "VA-API"), ("nvenc", "NVIDIA NVENC"), ("none", "Software")],
            onChanged: _ => ShowTab("Playback"));
        if (ViewModel.GetSetting("playback.hw_accel") != "none")
        {
            var hwDetailsHost = new StackPanel { Spacing = 8 };
            hwDetailsHost.Children.Add(new TextBlock { Text = "Detecting hardware...", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
            tcCard.Children.Add(hwDetailsHost);
            _ = LoadHardwareAccelerationDetailsAsync(hwDetailsHost);
        }

        AddToggleField(tcCard, "Transcoding Enabled", "playback.transcode_enabled");
        AddToggleField(tcCard, "Local Transcode Fallback", "playback.local_transcode_fallback",
            "When no eligible transcode node is available, transcode on this server instead. Disable to keep all transcoding on dedicated nodes — playback that requires transcoding fails while no node is eligible.", true);
        AddToggleField(tcCard, "Allow 4K Transcoding", "allow_4k_transcode");
        AddConditionalToggleWithNumberField(
            tcCard,
            "Enable Transcode Throttling", "enable_transcode_throttle",
            "Throttle Buffer (seconds)", "transcode_throttle_seconds",
            "How many seconds ahead FFmpeg transcodes before pausing. Minimum: 60.");
        EndCard(tcCard);

        AddSectionHeader("Segments");
        var segCard = BeginCard();
        AddNumberField(segCard, "Chapter Thumbnail Workers", "playback.chapter_thumbnail_workers",
            "Global chapter thumbnail dispatcher concurrency. Higher values improve throughput but can drive more local or remote extraction work at once.");
        AddSelectField(segCard, "Chapter Thumbnail Execution", "playback.chapter_thumbnail_execution",
            [("local", "Local only"), ("prefer_transcode_nodes", "Prefer transcode nodes"), ("transcode_nodes_only", "Transcode nodes only")],
            "Controls whether chapter thumbnails run on the API node or are offloaded to available transcode nodes.");
        AddNumberField(segCard, "Chapter Thumbnail Node Capacity", "playback.chapter_thumbnail_node_capacity",
            "Per transcode-node budget for chapter thumbnail jobs when remote execution is enabled.");
        AddSelectField(segCard, "HDR Chapter Thumbnail Policy", "playback.chapter_thumbnail_hdr_policy",
            [("best_effort", "Best effort tone mapping"), ("disabled", "Disable HDR/DV thumbnails")],
            "Controls whether chapter thumbnails are generated for HDR or Dolby Vision sources. SDR files are unaffected.",
            _ => ShowTab("Playback"));
        var cpuToneMap = AddToggleField(segCard, "Enable CPU Tone Mapping", "playback.chapter_thumbnail_software_tone_map_enabled",
            "Allows CPU/software tone mapping when hardware HDR chapter-thumbnail extraction is unavailable or fails. Disabled by default because it can be CPU-intensive.");
        cpuToneMap.IsEnabled = ViewModel.GetSetting("playback.chapter_thumbnail_hdr_policy") != "disabled";
        EndCard(segCard);

        AddSectionHeader("Behavior");
        var behCard = BeginCard();
        AddNumberField(behCard, "Watched Threshold (%)", "playback.watched_threshold",
            "Mark as watched after this % is played (default: 90)");
        AddNumberField(behCard, "Min Resume Threshold (%)", "playback.min_resume_threshold",
            "Ignore progress below this % of duration (default: 5)");
        EndCard(behCard);
    }

    private async Task LoadHardwareAccelerationDetailsAsync(StackPanel host)
    {
        try
        {
            var info = await App.Services.GetRequiredService<AdminApi>().GetHWAccelDetectionAsync();
            host.Children.Clear();
            var configured = ViewModel.GetSetting("playback.hw_device")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var hwAccel = ViewModel.GetSetting("playback.hw_accel");
            var isNvenc = hwAccel == "nvenc" || hwAccel == "auto" && info.Resolved == "nvenc";

            if (hwAccel is "auto" or "")
            {
                var resolvedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                resolvedRow.Children.Add(new Border
                {
                    Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(info.Resolved != "none"
                        ? Color.FromArgb(255, 34, 197, 94)
                        : Color.FromArgb(255, 251, 191, 36)),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var resolved = info.Resolved switch
                {
                    "qsv" => "Intel Quick Sync (QSV)", "vaapi" => "VA-API",
                    "nvenc" => "NVIDIA NVENC", "none" => "Software", _ => info.Resolved,
                };
                var firstDevice = info.RenderDevices.FirstOrDefault();
                resolvedRow.Children.Add(new TextBlock
                {
                    Text = resolved + (string.IsNullOrWhiteSpace(firstDevice) ? "" : $" — {firstDevice}") + (info.Source == "transcode_node" ? " (transcode node)" : ""),
                    FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                host.Children.Add(resolvedRow);
            }

            if (isNvenc && configured.Count > 1)
                host.Children.Add(BuildHardwareWarning($"Multi-GPU balancing supports QSV/VA-API only; with NVENC the server uses the first configured device ({configured[0]})."));
            if (isNvenc) return;

            var deviceRows = info.RenderDeviceDetails.Count > 0
                ? info.RenderDeviceDetails.Select(device => (device.Path, device.Description, true)).ToList()
                : info.RenderDevices.Select(path => (path, "GPU", true)).ToList();
            foreach (var missing in configured.Where(path => deviceRows.All(row => row.Item1 != path)))
                deviceRows.Add((missing, "Configured device not detected", false));
            if (deviceRows.Count == 0) return;

            var respondingNodes = info.Nodes.Where(node => string.IsNullOrWhiteSpace(node.Error)).ToList();
            var inventories = respondingNodes.Select(node => string.Join(",", node.RenderDevices.Order())).Distinct().Count();
            host.Children.Add(new TextBlock { Text = "GPU Devices", FontSize = 13, FontWeight = FontWeights.Medium });
            host.Children.Add(new TextBlock
            {
                Text = configured.Count switch
                {
                    0 => "Auto — the first available device handles every transcode. Select devices to pin or balance.",
                    1 => "All transcodes run on the selected device.",
                    _ => "Transcode sessions balance across the selected devices (least loaded first).",
                } + (info.Source == "transcode_node" ? " Devices reported by a transcode node." : ""),
                FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap,
            });
            if (inventories > 1)
                host.Children.Add(BuildHardwareWarning("This setting applies to every transcode node, but the nodes report different devices. Only paths present on all nodes are safe to select."));

            var detectedOrder = deviceRows.Where(row => row.Item3).Select(row => row.Item1).ToList();
            foreach (var (path, description, detected) in deviceRows)
            {
                var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var copy = new StackPanel { Spacing = 2 };
                copy.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = (Brush)Application.Current.Resources[detected ? "PrimaryTextBrush" : "SecondaryTextBrush"] });
                copy.Children.Add(new TextBlock { Text = path, FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
                var missingOn = respondingNodes.Where(node => !node.RenderDevices.Contains(path)).Select(node => node.NodeName ?? node.NodeUrl).ToList();
                if (missingOn.Count > 0) copy.Children.Add(new TextBlock { Text = "Not present on: " + string.Join(", ", missingOn), FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)), TextWrapping = TextWrapping.Wrap });
                var toggle = new ToggleSwitch { IsOn = configured.Contains(path), OnContent = "", OffContent = "", Tag = path };
                toggle.Toggled += (_, _) =>
                {
                    var selected = ViewModel.GetSetting("playback.hw_device").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
                    if (toggle.IsOn) selected.Add(path); else selected.Remove(path);
                    var ordered = detectedOrder.Where(selected.Contains).Concat(selected.Where(value => !detectedOrder.Contains(value)));
                    ViewModel.SetSetting("playback.hw_device", string.Join(",", ordered));
                    UpdateDirtyCountText();
                };
                Grid.SetColumn(toggle, 1); row.Children.Add(copy); row.Children.Add(toggle); host.Children.Add(row);
            }
        }
        catch
        {
            host.Children.Clear();
            host.Children.Add(new TextBlock { Text = "Could not detect hardware acceleration", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
        }
    }

    private static TextBlock BuildHardwareWarning(string text) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),
    };

    private void BuildScannerTab()
    {
        AddTabHeader("Scanner & Matcher",
            "Configure scanner performance and metadata matching. Startup and recurring scans are managed in Scheduled Tasks.");

        AddSectionHeader("Scanner");
        var scanCard = BeginCard();
        AddNumberField(scanCard, "Scanner Workers", "scanner.workers");
        EndCard(scanCard);

        AddSectionHeader("Matcher");
        var matchCard = BeginCard();
        AddNumberField(matchCard, "Matcher Workers", "matcher.workers");
        AddNumberField(matchCard, "Matcher Batch Size", "matcher.batch_size");
        EndCard(matchCard);

        AddSectionHeader("Metadata");
        var metaCard = BeginCard();
        AddToggleField(metaCard, "Cache Images to S3", "metadata.cache_images",
            "Download artwork from metadata providers and store resized variants in public asset S3 storage. Private bucket + presigned URLs is fully supported.");
        EndCard(metaCard);
    }

    private void BuildIntroMarkersTab()
    {
        ContentPanel.MaxWidth = 672;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
        AddTabHeader("Intro Markers",
            "Configure marker lookup, local marker generation, and provider contribution.");

        AddSectionHeader("Detection");
        var markerCard = BeginCard();
        AddSelectField(markerCard, "Mode", "markers.mode",
            [
                ("off", "Off"),
                ("local", "Local"),
                ("both", "Local + Online"),
                ("online", "Online Only"),
            ],
            "Controls whether Silo uses local chapter analysis, online provider markers, or both.");
        AddToggleField(markerCard, "Fetch Markers at Playback if Missing", "markers.lazy_playback",
            "When enabled, playback can ask the server for markers if a file has not been scanned yet.");
        EndCard(markerCard);

        AddSectionHeader("Marker Providers");
        var providersCard = BeginCard();
        var providersHost = new StackPanel { Spacing = 12 };
        providersCard.Children.Add(providersHost);
        _ = LoadMarkerProvidersAsync(providersHost);
        EndCard(providersCard);

        AddSectionHeader("Tasks");
        var taskCard = BeginCard();
        var tasksHost = new StackPanel { Spacing = 0 };
        taskCard.Children.Add(tasksHost);
        _ = LoadIntroTasksAsync(tasksHost);
        EndCard(taskCard);
    }

    private async Task LoadMarkerProvidersAsync(StackPanel host)
    {
        host.Children.Clear();
        host.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center });
        try
        {
            var response = await _settingsApi.GetMarkerProvidersAsync();
            host.Children.Clear();
            if (response.Providers.Count == 0)
            {
                host.Children.Add(new TextBlock { Text = "Marker Providers", FontSize = 14, FontWeight = FontWeights.SemiBold });
                host.Children.Add(new TextBlock
                {
                    Text = "No marker provider plugins are installed or enabled.",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
                });
                return;
            }

            foreach (var provider in response.Providers)
                host.Children.Add(BuildMarkerProviderCard(provider));
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(new TextBlock
            {
                Text = $"Marker providers could not be loaded: {ex.Message}", TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            });
        }
    }

    private Border BuildMarkerProviderCard(MarkerProviderConfig provider)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Provider : provider.DisplayName,
            FontSize = 14, FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Controls online marker lookup and whether locally generated markers can be submitted.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        panel.Children.Add(new TextBlock
        {
            Text = provider.SourceType == "plugin" && !string.IsNullOrWhiteSpace(provider.PluginId)
                ? $"Plugin {provider.PluginId} / {provider.CapabilityId ?? provider.Provider}"
                : provider.Provider,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });

        var fetch = new ToggleSwitch { IsOn = provider.FetchEnabled, OnContent = "", OffContent = "" };
        panel.Children.Add(CreateMarkerProviderToggleRow("Use for Online Marker Lookup", null, fetch));
        var priority = new NumberBox
        {
            Value = provider.FetchPriority, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 160, HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Children.Add(CreateMarkerProviderNumberRow("Fetch Priority", "Lower numbers win when providers overlap.", priority));
        var contribute = new ToggleSwitch
        {
            IsOn = provider.ContributeEnabled, IsEnabled = provider.IsSubmitter, OnContent = "", OffContent = ""
        };
        panel.Children.Add(CreateMarkerProviderToggleRow("Allow Contributions", null, contribute));
        var autoLocal = new ToggleSwitch
        {
            IsOn = provider.ContributeAutoLocal,
            IsEnabled = provider.IsSubmitter && provider.ContributeEnabled,
            OnContent = "", OffContent = ""
        };
        panel.Children.Add(CreateMarkerProviderToggleRow("Auto-submit Local Markers",
            "Scheduled contribution only sends scanner markers that meet the confidence floor.", autoLocal));
        contribute.Toggled += (_, _) =>
        {
            autoLocal.IsEnabled = provider.IsSubmitter && contribute.IsOn;
            if (!contribute.IsOn) autoLocal.IsOn = false;
        };
        var confidence = new NumberBox
        {
            Value = provider.ContributeMinConfidence,
            Minimum = 0, Maximum = 1, SmallChange = 0.01,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            IsEnabled = provider.IsSubmitter,
            Width = 160, HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Children.Add(CreateMarkerProviderNumberRow("Minimum Confidence",
            "Use a decimal from 0 to 1. The default recommendation is 0.95.", confidence));

        var validationHost = new StackPanel { Spacing = 4 };
        panel.Children.Add(validationHost);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (provider.IsSubmitter)
        {
            var validate = new Button { Content = "Validate" };
            validate.Click += async (_, _) =>
            {
                validate.IsEnabled = false;
                try
                {
                    var result = await _settingsApi.ValidateMarkerProviderAsync(provider.Provider);
                    validationHost.Children.Clear();
                    if (result.Valid && result.Stats is { } stats)
                    {
                        validationHost.Children.Add(new TextBlock
                        {
                            Text = $"Total submissions {stats.Total} · Accepted {stats.Accepted} · Pending {stats.Pending} · Rejected {stats.Rejected} · Acceptance rate {Math.Round(stats.AcceptanceRate * 100):0}% · Best streak {stats.BestStreak}",
                            FontSize = 12, TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
                        });
                    }
                    else
                        validationHost.Children.Add(new TextBlock
                        {
                            Text = result.Error ?? "Validation failed.", FontSize = 12,
                            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
                        });
                }
                catch (Exception ex) { ShowStatusToast($"Marker provider validation failed: {ex.Message}"); }
                finally { validate.IsEnabled = true; }
            };
            actions.Children.Add(validate);
        }
        var save = new Button { Content = "Save Provider Settings" };
        save.Click += async (_, _) =>
        {
            if (double.IsNaN(priority.Value) || priority.Value != Math.Truncate(priority.Value))
            {
                ShowStatusToast("Fetch priority must be a whole number.");
                return;
            }
            if (double.IsNaN(confidence.Value) || confidence.Value is < 0 or > 1)
            {
                ShowStatusToast("Minimum confidence must be between 0 and 1.");
                return;
            }
            save.IsEnabled = false;
            try
            {
                await _settingsApi.UpdateMarkerProviderAsync(provider.Provider, new Dictionary<string, object?>
                {
                    ["fetch_enabled"] = fetch.IsOn,
                    ["fetch_priority"] = (int)priority.Value,
                    ["contribute_enabled"] = contribute.IsOn,
                    ["contribute_auto_local"] = contribute.IsOn && autoLocal.IsOn,
                    ["contribute_min_confidence"] = confidence.Value
                });
                ShowStatusToast("Marker provider settings saved.");
            }
            catch (Exception ex) { ShowStatusToast($"Failed to save marker provider settings: {ex.Message}"); }
            finally { save.IsEnabled = true; }
        };
        actions.Children.Add(save);
        panel.Children.Add(actions);

        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12), Child = panel
        };
    }

    private static Grid CreateMarkerProviderToggleRow(string label, string? hint, ToggleSwitch toggle)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium });
        if (!string.IsNullOrWhiteSpace(hint))
            text.Children.Add(new TextBlock
            {
                Text = hint, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            });
        Grid.SetColumn(toggle, 1);
        row.Children.Add(text);
        row.Children.Add(toggle);
        return row;
    }

    private static StackPanel CreateMarkerProviderNumberRow(string label, string hint, NumberBox number)
    {
        var row = new StackPanel { Spacing = 4, Padding = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium });
        row.Children.Add(number);
        row.Children.Add(new TextBlock
        {
            Text = hint, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        return row;
    }

    private async Task LoadIntroTasksAsync(StackPanel host)
    {
        host.Children.Clear();
        host.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center });
        try
        {
            var api = App.Services.GetRequiredService<AdminApi>();
            var tasks = await api.GetTasksAsync();
            host.Children.Clear();
            host.Children.Add(BuildIntroTaskRow(api, tasks.FirstOrDefault(task => task.Key == "detect_intro_markers"),
                "detect_intro_markers", "Populate Markers", "Populates intro and credits markers for opted-in libraries.", host));
            host.Children.Add(BuildIntroTaskRow(api, tasks.FirstOrDefault(task => task.Key == "contribute_markers"),
                "contribute_markers", "Contribute Markers", "Submits high-confidence local intro markers to enabled providers.", host));
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(new TextBlock
            {
                Text = $"Marker tasks could not be loaded: {ex.Message}", TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            });
        }
    }

    private Border BuildIntroTaskRow(AdminApi api, Core.Models.Admin.TaskInfo? task, string key,
        string fallbackName, string fallbackDescription, StackPanel host)
    {
        bool running = task?.State is "running" or "cancelling";
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = task?.Name ?? fallbackName, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = task?.Description ?? fallbackDescription, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        text.Children.Add(new TextBlock
        {
            Text = $"Last result: {FormatSearchStatusDate(task?.LastExecution?.CompletedAt)}", FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        grid.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var history = new Button { Content = "History", IsEnabled = task != null };
        history.Click += (_, _) => Frame.Navigate(typeof(AdminTaskDetailPage), key);
        actions.Children.Add(history);
        var run = new Button { Content = running ? "Running" : "Run Now", IsEnabled = task != null && !running };
        run.Click += async (_, _) =>
        {
            run.IsEnabled = false;
            try
            {
                await api.RunTaskAsync(key);
                ShowStatusToast($"{task?.Name ?? fallbackName} started.");
                await LoadIntroTasksAsync(host);
            }
            catch (Exception ex)
            {
                ShowStatusToast($"Task could not be started: {ex.Message}");
                run.IsEnabled = true;
            }
        };
        actions.Children.Add(run);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 12, 0, 12), Child = grid
        };
    }

    private void BuildSubtitlesTab()
    {
        ContentPanel.MaxWidth = 672;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
        AddTabHeader("Subtitles",
            "Search providers for downloading subtitles. AI translation and transcription live under AI Services.");

        BuildSubtitleProvidersSection();
    }

    private void BuildAIServicesTab()
    {
        ContentPanel.MaxWidth = 672;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
        AddTabHeader("AI Services",
            "Shared AI endpoint and feature toggles for subtitle translation, subtitle generation from audio, and description translation.");

        AddSectionHeader("Endpoint");
        var endpointCard = BeginCard();
        AddTextField(endpointCard, "Base URL", "ai.base_url",
            "https://api.openai.com", "subtitle_ai.base_url");
        AddTextField(endpointCard, "Chat model", "ai.chat_model",
            "Used for subtitle and description translation, e.g. gpt-4o-mini, llama3.1", "subtitle_ai.chat_model");
        AddPasswordField(endpointCard, "API Key", "ai.api_key",
            "Leave blank to keep current. Empty is fine for keyless local servers.", "subtitle_ai.api_key");
        AddDivider(endpointCard);
        endpointCard.Children.Add(new TextBlock { Text = "Transcription", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        endpointCard.Children.Add(new TextBlock
        {
            Text = "Subtitle generation needs a Whisper endpoint that returns segment timestamps. Pick a preset or configure your own:",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        var asrModel = AddTextField(endpointCard, "Transcription model", "ai.asr_model",
            "Whisper model for subtitle generation, e.g. whisper-large-v3-turbo");
        var asrBaseUrl = AddTextField(endpointCard, "Transcription base URL", "ai.asr_base_url",
            "Whisper-capable endpoint with segment timestamps: a self-hosted faster-whisper/speaches server (recommended), api.groq.com/openai, or api.openai.com. Blank uses the base URL — chat-only gateways such as OpenRouter cannot transcribe.");
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var preset in new[]
                 {
                     ("Self-hosted · recommended", "http://localhost:8000", "deepdml/faster-whisper-large-v3-turbo-ct2"),
                     ("Groq · hosted fallback", "https://api.groq.com/openai", "whisper-large-v3-turbo"),
                     ("Groq · most accurate", "https://api.groq.com/openai", "whisper-large-v3"),
                     ("OpenAI", "https://api.openai.com", "whisper-1")
                 })
        {
            var button = new Button { Content = preset.Item1, Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
            button.Click += (_, _) =>
            {
                asrBaseUrl.Text = preset.Item2;
                asrModel.Text = preset.Item3;
            };
            presets.Children.Add(button);
        }
        endpointCard.Children.Add(presets);
        AddPasswordField(endpointCard, "Transcription API key", "ai.asr_api_key",
            "Optional; blank uses the main API key.");
        AddNumberField(endpointCard, "Max concurrent jobs", "ai.max_concurrent_jobs",
            "One shared cap across subtitle translation, transcription, and description translation.", "subtitle_ai.max_concurrent_jobs");
        AddOwnedSettingsSaveButton(endpointCard, "Save Endpoint Settings",
        [
            "ai.base_url", "ai.chat_model", "ai.api_key", "ai.asr_model",
            "ai.asr_base_url", "ai.asr_api_key", "ai.max_concurrent_jobs"
        ]);
        AddTextBlock(endpointCard, "Changes take effect after a server restart.");
        EndCard(endpointCard);

        AddSectionHeader("Features");
        var featuresCard = BeginCard();
        AddToggleField(featuresCard, "Subtitle translation", "subtitle_ai.enabled",
            "Show the “Translate with AI” action in the player.");
        AddToggleField(featuresCard, "Subtitle generation from audio", "subtitle_ai.transcribe_enabled",
            "Whisper transcription — generates subtitle tracks for media with no usable text subtitles.");
        AddToggleField(featuresCard, "Description translation", "metadata_ai.enabled",
            "Translate overviews and taglines from the metadata editor, plus the per-library auto-translate option.");
        AddSelectField(featuresCard, "On-view translation", "metadata_ai.on_view",
            [
                ("off", "Off"),
                ("button", "Translate button on detail pages"),
                ("auto", "Automatic on view"),
            ],
            "Let viewers get descriptions in their profile's metadata language: a Translate button, or automatic translation when they open a detail page. Requires description translation.");
        AddNumberField(featuresCard, "Subtitle batch size", "subtitle_ai.batch_size",
            "Cues per translation request.");
        AddNumberField(featuresCard, "Subtitle context lines", "subtitle_ai.context_neighbors",
            "Preceding source cues sent for scene continuity across batches.");
        AddNumberField(featuresCard, "Transcription chunk length (seconds)", "subtitle_ai.asr_chunk_seconds",
            "60–600. Shorter chunks keep Whisper timestamps tighter on long files, at the cost of more requests and occasional clipped words at chunk boundaries.");
        AddNumberField(featuresCard, "Transcription limit per account", "subtitle_ai.transcribe_quota_jobs",
            "Maximum transcription jobs per user account each period; profiles on an account share the limit. The admin account's primary profile is exempt. 0 = unlimited.");
        AddSelectField(featuresCard, "Transcription limit period", "subtitle_ai.transcribe_quota_period",
            [
                ("day", "Per day (rolling 24 hours)"),
                ("week", "Per week (rolling 7 days)"),
                ("month", "Per month (rolling 30 days)"),
            ],
            "Rolling window the transcription limit counts against.");
        AddOwnedSettingsSaveButton(featuresCard, "Save Feature Settings",
        [
            "subtitle_ai.enabled", "subtitle_ai.transcribe_enabled", "metadata_ai.enabled",
            "metadata_ai.on_view", "subtitle_ai.batch_size", "subtitle_ai.context_neighbors",
            "subtitle_ai.asr_chunk_seconds", "subtitle_ai.transcribe_quota_jobs",
            "subtitle_ai.transcribe_quota_period"
        ]);
        AddTextBlock(featuresCard, "Changes take effect after a server restart.");
        EndCard(featuresCard);
    }

    private void BuildRateLimitTab()
    {
        AddTabHeader("Rate Limiting", "Configure request budgets for API keys, IPs, and authentication endpoints.");

        // Webui constrains rate-limit inner content to max-w-2xl (672px)
        ContentPanel.MaxWidth = 672;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;

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
            enableLabelStack.Children.Add(new TextBlock { Text = "Enable Rate Limiting", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            enableLabelStack.Children.Add(new TextBlock { Text = "When disabled, no rate limits are enforced.", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextWrapping = TextWrapping.Wrap });
            var enableToggle = new ToggleSwitch { IsOn = working.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            enableToggle.Toggled += (s, e) => { working.Enabled = enableToggle.IsOn; MarkDirty(); };
            Grid.SetColumn(enableLabelStack, 0); Grid.SetColumn(enableToggle, 1);
            enableField.Children.Add(enableLabelStack); enableField.Children.Add(enableToggle);
            card.Children.Add(enableField);

            AddDivider(card);

            // Backend select
            var backendField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
            backendField.Children.Add(new TextBlock { Text = "Backend", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var backendCombo = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            backendCombo.Items.Add(new ComboBoxItem { Content = "In-Memory", Tag = "memory" });
            backendCombo.Items.Add(new ComboBoxItem { Content = "Redis", Tag = "redis" });
            for (int i = 0; i < backendCombo.Items.Count; i++)
                if (backendCombo.Items[i] is ComboBoxItem ci && ci.Tag?.ToString() == working.Backend) { backendCombo.SelectedIndex = i; break; }
            if (backendCombo.SelectedIndex < 0) backendCombo.SelectedIndex = 0;
            backendCombo.SelectionChanged += (s, e) => { if (backendCombo.SelectedItem is ComboBoxItem sel) { working.Backend = sel.Tag?.ToString() ?? "memory"; MarkDirty(); } };
            backendField.Children.Add(backendCombo);
            backendField.Children.Add(new TextBlock { Text = "Requires a restart to take effect. Redis is recommended for multi-instance deployments.", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextWrapping = TextWrapping.Wrap, MaxWidth = 448 });
            card.Children.Add(backendField);

            EndCard(card);
        }

        // ---- Global Settings ----
        AddSectionHeader("Global Settings");
        {
            var card = BeginCard();
            var globalField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
            globalField.Children.Add(new TextBlock { Text = "Global Requests Per Second", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var globalRpsBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.GlobalRequestsPerSecond, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            globalRpsBox.ValueChanged += (s, e) => { if (!double.IsNaN(globalRpsBox.Value)) { working.GlobalRequestsPerSecond = (int)globalRpsBox.Value; MarkDirty(); } };
            globalField.Children.Add(globalRpsBox);
            globalField.Children.Add(new TextBlock { Text = "Maximum requests per second across all clients combined.", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"], TextWrapping = TextWrapping.Wrap, MaxWidth = 448 });
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
            rpsField.Children.Add(new TextBlock { Text = "Requests / Second", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpsBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.IpRequestsPerSecond, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpsBox.ValueChanged += (s, e) => { if (!double.IsNaN(rpsBox.Value)) { working.IpRequestsPerSecond = (int)rpsBox.Value; MarkDirty(); } };
            rpsField.Children.Add(rpsBox);

            var rpmField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            rpmField.Children.Add(new TextBlock { Text = "Requests / Minute", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpmBox = new Microsoft.UI.Xaml.Controls.NumberBox { Value = working.IpRequestsPerMinute, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpmBox.ValueChanged += (s, e) => { if (!double.IsNaN(rpmBox.Value)) { working.IpRequestsPerMinute = (int)rpmBox.Value; MarkDirty(); } };
            rpmField.Children.Add(rpmBox);

            var burstField = new StackPanel { Spacing = 4 };
            burstField.Children.Add(new TextBlock { Text = "Burst", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
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
            rpsF.Children.Add(new TextBlock { Text = "Requests / Second", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpsB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = tierCfg.RequestsPerSecond, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpsB.ValueChanged += (s, e) => { if (!double.IsNaN(rpsB.Value)) { tierCfg.RequestsPerSecond = (int)rpsB.Value; MarkDirty(); } };
            rpsF.Children.Add(rpsB);

            var rpmF = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            rpmF.Children.Add(new TextBlock { Text = "Requests / Minute", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            var rpmB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = tierCfg.RequestsPerMinute, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            rpmB.ValueChanged += (s, e) => { if (!double.IsNaN(rpmB.Value)) { tierCfg.RequestsPerMinute = (int)rpmB.Value; MarkDirty(); } };
            rpmF.Children.Add(rpmB);

            var burstF = new StackPanel { Spacing = 4 };
            burstF.Children.Add(new TextBlock { Text = "Burst", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
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
                epRpmF.Children.Add(new TextBlock { Text = "Requests / Minute", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
                var epRpmB = new Microsoft.UI.Xaml.Controls.NumberBox { Value = epCfg.RequestsPerMinute, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact, Minimum = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
                epRpmB.ValueChanged += (s, e) => { if (!double.IsNaN(epRpmB.Value)) { epCfg.RequestsPerMinute = (int)epRpmB.Value; MarkDirty(); } };
                epRpmF.Children.Add(epRpmB);

                var epBurstF = new StackPanel { Spacing = 4 };
                epBurstF.Children.Add(new TextBlock { Text = "Burst", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
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

    private static readonly Dictionary<string, string> SubtitleProviderDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opensubtitles"] = "OpenSubtitles",
        ["subdl"] = "SubDL",
        ["subsource"] = "SubSource",
    };
    private static readonly List<string> SubtitleProviderOrder = ["opensubtitles", "subdl", "subsource"];

    private AdminSubtitleProvidersViewModel? _subsVm;
    private StackPanel? _subsHost;

    private void BuildIntegrationsTab()
    {
        ContentPanel.MaxWidth = 672;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
        AddTabHeader("Integrations", "API keys for external services. Watch provider and subtitle credentials have their own pages in the sidebar.");

        var mdblistCard = BeginCard();
        AddCredentialCardHeader(mdblistCard, "MDBList",
            "Enables list search/browse when users add MDBList collections. Importing a list by URL works without a key — only discovery requires one. Get a free key at mdblist.com/preferences.",
            ViewModel.IsSensitiveConfigured("mdblist.api_key"));
        var fields = new StackPanel();
        AddPasswordField(fields, "API Key", "mdblist.api_key", "Leave blank to keep the current value.");
        AddOwnedSettingsSaveButton(fields, "Save MDBList API Key", ["mdblist.api_key"]);
        mdblistCard.Children.Add(fields);
        EndCard(mdblistCard);
    }

    private void BuildSubtitleProvidersSection()
    {
        AddSectionHeader("Subtitle Providers");
        var card = BeginCard();
        AddTextBlock(card,
            "Configure external subtitle search providers. Credentials are stored securely and never returned by the API.");

        _subsHost = new StackPanel { Spacing = 16 };
        card.Children.Add(_subsHost);

        // Skeleton loading blocks (webui renders shimmer cards while loading)
        for (int sk = 0; sk < 2; sk++)
        {
            var skeleton = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                CornerRadius = new CornerRadius(8),
                Height = 80,
                Opacity = 0.5,
            };
            _subsHost.Children.Add(skeleton);
        }

        EndCard(card);

        // Load providers async
        _subsVm ??= App.Services.GetRequiredService<AdminSubtitleProvidersViewModel>();
        _ = LoadSubtitleProvidersAsync();
    }

    private async Task LoadSubtitleProvidersAsync()
    {
        if (_subsVm == null || _subsHost == null) return;
        try
        {
            await _subsVm.LoadCommand.ExecuteAsync(null);
            RebuildSubtitleProviderCards();
        }
        catch (Exception ex)
        {
            // Show error via toast instead of full takeover (webui pattern)
            ShowStatusToast($"Failed to load providers: {ex.Message}");
            if (_subsHost == null) return;
            _subsHost.Children.Clear();
            var retryBtn = new Button
            {
                Content = "Retry",
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                FontSize = 12,
            };
            retryBtn.Click += async (_, _) => await LoadSubtitleProvidersAsync();
            _subsHost.Children.Add(retryBtn);
        }
    }

    private void RebuildSubtitleProviderCards()
    {
        if (_subsVm == null || _subsHost == null) return;

        _subsHost.Children.Clear();
        var providers = _subsVm.Providers.ToList();
        if (providers.Count == 0)
        {
            // Webui renders a bordered card for the empty state
            var emptyCard = new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 24, 16, 24),
            };
            emptyCard.Child = new TextBlock
            {
                Text = "No subtitle providers configured.",
                FontSize = 14,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _subsHost.Children.Add(emptyCard);
            return;
        }

        providers.Sort((a, b) =>
        {
            int ai = SubtitleProviderOrder.IndexOf(a.ProviderName?.ToLowerInvariant() ?? "");
            int bi = SubtitleProviderOrder.IndexOf(b.ProviderName?.ToLowerInvariant() ?? "");
            if (ai == -1 && bi == -1) return 0;
            if (ai == -1) return 1;
            if (bi == -1) return -1;
            return ai - bi;
        });

        foreach (var p in providers)
            _subsHost.Children.Add(BuildInlineSubtitleProviderCard(p));
    }

    private FrameworkElement BuildInlineSubtitleProviderCard(Core.Models.Admin.SubtitleProviderConfig provider)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 16, 16, 16),
        };
        var layout = new StackPanel { Spacing = 12 };

        bool isOpenSubtitles = string.Equals(provider.ProviderName, "opensubtitles", StringComparison.OrdinalIgnoreCase);
        string displayName = SubtitleProviderDisplayNames.TryGetValue(provider.ProviderName ?? "", out var dn) ? dn : provider.ProviderName ?? "";

        // Header: name + status pill + enabled toggle (right aligned)
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        nameRow.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        bool configured = isOpenSubtitles ? provider.HasCredentials : provider.HasApiKey;
        nameRow.Children.Add(BuildProviderStatusPill(configured));
        Grid.SetColumn(nameRow, 0);
        header.Children.Add(nameRow);

        var enabledToggle = new ToggleSwitch
        {
            IsOn = provider.Enabled,
            OnContent = "Enabled",
            OffContent = "Disabled",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
        };
        Grid.SetColumn(enabledToggle, 1);
        header.Children.Add(enabledToggle);
        layout.Children.Add(header);

        // Credential fields
        TextBox? usernameBox = null;
        PasswordBox? passwordBox = null;
        PasswordBox? apiKeyBox = null;

        if (isOpenSubtitles)
        {
            var userGroup = new StackPanel { Spacing = 3 };
            userGroup.Children.Add(new TextBlock { Text = "Username", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            usernameBox = new TextBox { PlaceholderText = provider.HasCredentials ? "Leave blank to keep current" : "OpenSubtitles username", FontSize = 13 };
            userGroup.Children.Add(usernameBox);
            layout.Children.Add(userGroup);

            var passGroup = new StackPanel { Spacing = 3 };
            passGroup.Children.Add(new TextBlock { Text = "Password", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            passwordBox = new PasswordBox { PlaceholderText = provider.HasCredentials ? "Leave blank to keep current" : "OpenSubtitles password", FontSize = 13 };
            passGroup.Children.Add(passwordBox);
            layout.Children.Add(passGroup);
        }
        else
        {
            var group = new StackPanel { Spacing = 3 };
            group.Children.Add(new TextBlock { Text = "API Key", FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            apiKeyBox = new PasswordBox { PlaceholderText = provider.HasApiKey ? "Leave blank to keep current" : "Enter API key", FontSize = 13 };
            group.Children.Add(apiKeyBox);
            layout.Children.Add(group);
        }

        // Actions row: Test + Save + result
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 0) };

        var testBtn = new Button { Content = "Test Connection", FontSize = 12, Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(6) };
        var saveBtn = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            FontSize = 12,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(6),
        };
        var resultText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        var capturedProvider = provider;
        testBtn.Click += async (_, _) =>
        {
            if (_subsVm == null) return;
            var providerName = capturedProvider.ProviderName;
            if (string.IsNullOrWhiteSpace(providerName)) return;
            testBtn.IsEnabled = false;
            testBtn.Content = "Testing...";
            resultText.Visibility = Visibility.Collapsed;
            try
            {
                var r = await _subsVm.TestProviderAsync(providerName);
                resultText.Text = r?.Success == true ? "Connection successful" : (r?.Error ?? "Connection failed");
                resultText.Foreground = new SolidColorBrush(r?.Success == true
                    ? Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)
                    : Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73));
                resultText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                resultText.Text = $"Test failed: {ex.Message}";
                resultText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73));
                resultText.Visibility = Visibility.Visible;
            }
            finally
            {
                testBtn.IsEnabled = true;
                testBtn.Content = "Test Connection";
            }
        };

        saveBtn.Click += async (_, _) =>
        {
            if (_subsVm == null) return;
            var providerName = capturedProvider.ProviderName;
            if (string.IsNullOrWhiteSpace(providerName)) return;
            saveBtn.IsEnabled = false;
            try
            {
                var req = new Core.Models.Admin.SubtitleProviderUpdateRequest
                {
                    Enabled = enabledToggle.IsOn,
                    ApiKey = isOpenSubtitles || string.IsNullOrWhiteSpace(apiKeyBox?.Password) ? null : apiKeyBox.Password,
                    Username = !isOpenSubtitles || string.IsNullOrWhiteSpace(usernameBox?.Text) ? null : usernameBox.Text,
                    Password = !isOpenSubtitles || string.IsNullOrWhiteSpace(passwordBox?.Password) ? null : passwordBox.Password,
                };
                await _subsVm.UpdateProviderAsync(providerName, req);
                resultText.Text = "Saved.";
                resultText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80));
                resultText.Visibility = Visibility.Visible;
                // Refresh
                await _subsVm.LoadCommand.ExecuteAsync(null);
                RebuildSubtitleProviderCards();
            }
            catch (Exception ex)
            {
                resultText.Text = $"Save failed: {ex.Message}";
                resultText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73));
                resultText.Visibility = Visibility.Visible;
            }
            finally
            {
                saveBtn.IsEnabled = true;
            }
        };

        actions.Children.Add(testBtn);
        actions.Children.Add(saveBtn);
        actions.Children.Add(resultText);
        layout.Children.Add(actions);

        border.Child = layout;
        return border;
    }

    private Border BuildProviderStatusPill(bool configured)
    {
        var bg = configured
            ? Color.FromArgb(0x33, 0x4A, 0xDE, 0x80)
            : Color.FromArgb(0x33, 0xFB, 0xBF, 0x24);
        var fg = configured
            ? Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)
            : Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24);
        return new Border
        {
            Background = new SolidColorBrush(bg),
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = configured ? "Configured" : "Not configured",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void AddCredentialCardHeader(StackPanel parent, string title, string description, bool configured)
    {
        var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        header.Children.Add(copy);

        var status = BuildProviderStatusPill(configured);
        Grid.SetColumn(status, 1);
        header.Children.Add(status);
        parent.Children.Add(header);
    }

    private void AddOwnedSettingsSaveButton(StackPanel parent, string label, IReadOnlyCollection<string> keys)
    {
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        void UpdateEnabled() => button.IsEnabled = !ViewModel.IsSaving
            && ViewModel.GetDirtyKeys().Any(keySet.Contains);
        _dirtyStateUpdaters.Add(UpdateEnabled);
        UpdateEnabled();

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            var saved = await ViewModel.SaveSettingsAsync(keySet);
            if (saved)
            {
                ShowStatusToast("Settings saved successfully.");
                // Rebuild sensitive inputs so submitted secrets are cleared and
                // the configured badge reflects the authoritative state.
                ShowTab(_activeTab);
            }
            else if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
            {
                ShowStatusToast(ViewModel.ErrorMessage);
            }
            UpdateEnabled();
        };
        parent.Children.Add(button);
    }

    private void BuildWatchProvidersTab()
    {
        ContentPanel.MaxWidth = 672;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Left;
        AddTabHeader("Watch Providers",
            "OAuth credentials for watch history and scrobbling services. Users connect their own accounts from their profile settings once a provider is configured here.");

        var traktCard = BeginCard();
        AddCredentialCardHeader(traktCard, "Trakt", "OAuth credentials for profile connections.",
            ViewModel.IsSensitiveConfigured("watchsync.trakt.client_id")
            && ViewModel.IsSensitiveConfigured("watchsync.trakt.client_secret"));
        var traktFields = new StackPanel();
        AddPasswordField(traktFields, "Client ID", "watchsync.trakt.client_id", "Leave blank to keep the current value.");
        AddPasswordField(traktFields, "Client Secret", "watchsync.trakt.client_secret", "Leave blank to keep the current value.");
        AddOwnedSettingsSaveButton(traktFields, "Save Trakt Credentials",
            ["watchsync.trakt.client_id", "watchsync.trakt.client_secret"]);
        traktCard.Children.Add(traktFields);
        EndCard(traktCard);

        var simklCard = BeginCard();
        AddCredentialCardHeader(simklCard, "Simkl", "OAuth credentials for profile connections.",
            ViewModel.IsSensitiveConfigured("watchsync.simkl.client_id")
            && ViewModel.IsSensitiveConfigured("watchsync.simkl.client_secret"));
        var simklFields = new StackPanel();
        AddPasswordField(simklFields, "Client ID", "watchsync.simkl.client_id", "Leave blank to keep the current value.");
        AddPasswordField(simklFields, "Client Secret", "watchsync.simkl.client_secret", "Leave blank to keep the current value.");
        AddOwnedSettingsSaveButton(simklFields, "Save Simkl Credentials",
            ["watchsync.simkl.client_id", "watchsync.simkl.client_secret"]);
        simklCard.Children.Add(simklFields);
        EndCard(simklCard);
    }

    private void BuildEmailTab()
    {
        AddTabHeader("Email",
            "Outbound email via your own SMTP server. Used by features that send mail — notification emails, account flows — once they are enabled.");

        AddSectionHeader("General");
        var generalCard = BeginCard();
        AddToggleField(generalCard, "Email Enabled", "email.enabled", "Master switch for all outbound email");
        AddTextField(generalCard, "From Address", "email.from_address", "The sender address, e.g. silo@example.com");
        AddTextField(generalCard, "From Name", "email.from_name", "Display name on outgoing mail (default \"Silo\")");
        EndCard(generalCard);

        AddSectionHeader("SMTP Server");
        var smtpCard = BeginCard();
        AddTextField(smtpCard, "Host", "email.smtp_host", "SMTP server hostname, e.g. smtp.example.com");
        AddNumberField(smtpCard, "Port", "email.smtp_port", "587 for STARTTLS (typical), 465 for implicit TLS");
        AddSelectField(smtpCard, "Security", "email.smtp_security",
            [
                ("starttls", "STARTTLS"),
                ("tls", "TLS (implicit)"),
                ("none", "None (insecure)"),
            ],
            "STARTTLS upgrades a plain connection; TLS connects encrypted from the start");
        AddTextField(smtpCard, "Username", "email.smtp_username",
            "Leave empty when the server requires no authentication.");
        AddPasswordField(smtpCard, "Password", "email.smtp_password", "Leave blank to keep the current value.");
        EndCard(smtpCard);

        AddSectionHeader("Verify");
        var verifyCard = BeginCard();
        var verifyRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 4) };
        verifyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        verifyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var recipient = new TextBox { PlaceholderText = "you@example.com" };
        verifyRow.Children.Add(recipient);
        var send = new Button { Content = "Send test", IsEnabled = false };
        recipient.TextChanged += (_, _) => send.IsEnabled = !string.IsNullOrWhiteSpace(recipient.Text);
        Grid.SetColumn(send, 1);
        verifyRow.Children.Add(send);
        verifyCard.Children.Add(verifyRow);
        var result = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        verifyCard.Children.Add(result);
        AddTextBlock(verifyCard, "Save your changes before testing — the test uses the stored settings.");
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            send.Content = "Sending...";
            result.Visibility = Visibility.Collapsed;
            try
            {
                var response = await _settingsApi.SendTestEmailAsync(recipient.Text);
                result.Text = response.Ok
                    ? $"Delivered to the SMTP server in {response.DurationMs}ms."
                    : response.Message ?? "Test failed.";
                result.Foreground = new SolidColorBrush(response.Ok
                    ? Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)
                    : Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
                result.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                result.Text = ex.Message;
                result.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71));
                result.Visibility = Visibility.Visible;
            }
            finally
            {
                send.Content = "Send test";
                send.IsEnabled = !string.IsNullOrWhiteSpace(recipient.Text);
            }
        };
        EndCard(verifyCard);
    }

    private void BuildNotificationsAdminTabCurrent()
    {
        AddTabHeader("Notifications",
            "Operational controls for the notification system. All settings apply live — no restart needed. Per-profile preferences live in each user's own notification settings.");

        ContentPanel.Children.Add(BuildNotificationPipeline());

        var channelsHeading = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        channelsHeading.Children.Add(new TextBlock
        {
            Text = "DELIVERY CHANNELS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 220,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
        });
        channelsHeading.Children.Add(new TextBlock
        {
            Text = "Where notifications go once fanout queues them. Channels can be configured while switched off.",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        ContentPanel.Children.Add(channelsHeading);

        var channelList = new StackPanel { Spacing = 0 };
        AddNotificationChannelRow(channelList, "\uE8F2", "In-App",
            "Advertise the notification inbox and preferences to web and client apps.",
            "notifications.ui_enabled");
        AddNotificationChannelRow(channelList, "\uE7F4", "Web Push",
            "Browser push notifications to subscribed devices.",
            "notifications.web_push_enabled");
        AddNotificationChannelRow(channelList, "\uE95A", "Silo Push Relay",
            "Mobile push delivery through Silo's relay. Apple devices use APNs today; Android support will use the same relay when available.",
            "notifications.apple_push_delivery_enabled",
            ViewModel.IsSensitiveConfigured("notifications.push_relay_api_key") ? "Relay configured" : "Registration required",
            BuildPushRelayDetails());

        var emailDetails = new StackPanel { Spacing = 0 };
        AddToggleField(emailDetails, "Allow Per-Episode Email", "notifications.email.allow_per_episode");
        AddNumberField(emailDetails, "Digest Hour", "notifications.email.digest_hour", "0-23, server local time.");
        AddTextField(emailDetails, "External URL", "notifications.email.external_url", "Public URL used in notification links.");
        var digestValue = ViewModel.GetSetting("notifications.email.digest_hour");
        if (string.IsNullOrWhiteSpace(digestValue)) digestValue = "8";
        AddNotificationChannelRow(channelList, "\uE715", "Email",
            "Notifications by email for accounts that opt in, as a daily digest or per episode.",
            "notifications.email_enabled", $"Digest at {digestValue.PadLeft(2, '0')}:00", emailDetails);

        var discordDetails = BuildDiscordNotificationDetails();
        var discordConfigured = ViewModel.IsSensitiveConfigured("discord.bot_token")
            || ViewModel.IsSensitiveConfigured("discord.client_secret");
        AddNotificationChannelRow(channelList, "\uE902", "Discord",
            "Bot DMs for linked accounts, plus appearance for every Discord delivery surface.",
            "notifications.discord_enabled", discordConfigured ? "Credentials configured" : "Credentials required", discordDetails);

        var webhookDetails = new StackPanel { Spacing = 0 };
        AddNumberField(webhookDetails, "Max Webhooks Per Profile", "notifications.webhooks.max_per_profile",
            "How many webhooks a single profile may create (default 10)");
        AddNumberField(webhookDetails, "Deliveries Per Minute Per Profile", "notifications.webhooks.deliveries_per_minute_per_profile",
            "Webhook delivery rate limit; over-limit notifications still reach the inbox (default 60)");
        AddToggleField(webhookDetails, "Allow Private Destinations", "notifications.webhooks.allow_private_destinations",
            "Disables the SSRF guard so webhooks may target private and LAN addresses. Development only.");
        AddNotificationChannelRow(channelList, "\uE774", "Personal Webhooks",
            "User-created webhooks (Discord or generic) that receive their personal notifications — the server sends requests to user-chosen URLs.",
            "notifications.webhooks_enabled", null, webhookDetails);

        var serverChannelDetails = new StackPanel { Spacing = 0 };
        AddNumberField(serverChannelDetails, "Batch Window (seconds)", "notifications.server_channels.batch_seconds");
        AddToggleField(serverChannelDetails, "Mention Requesters on Discord", "notifications.server_channels.mention_requesters",
            "Mention linked requesters in request-related server channel posts.");
        AddDivider(serverChannelDetails);
        var channelsHost = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 4) };
        serverChannelDetails.Children.Add(channelsHost);
        _ = LoadServerNotificationChannelsAsync(channelsHost);
        AddNotificationChannelRow(channelList, "\uE789", "Server Channels",
            "Admin-created broadcasts that post server-wide events (new content, request activity) to shared destinations, like a community Discord channel.",
            "notifications.server_channels_enabled", null, serverChannelDetails);

        ContentPanel.Children.Add(new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(16),
            Child = channelList,
        });

        var advancedHeading = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        advancedHeading.Children.Add(new TextBlock
        {
            Text = "ADVANCED",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 220,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
        });
        advancedHeading.Children.Add(new TextBlock
        {
            Text = "Batching, flood control, and cleanup. The defaults work well for most servers.",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        ContentPanel.Children.Add(advancedHeading);

        var advanced = new Grid { ColumnSpacing = 16 };
        advanced.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        advanced.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fanout = BeginCard();
        fanout.Children.Add(new TextBlock
        {
            Text = "FANOUT TUNING", FontSize = 11, FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 220, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 10),
        });
        AddNumberField(fanout, "Settle Delay (seconds)", "notifications.fanout.settle_seconds",
            "How long an event must sit before fanout claims it, so one scan's episodes batch together (default 30)");
        AddNumberField(fanout, "Max Series Burst", "notifications.fanout.max_series_burst",
            "Max notifications per series per batch; the rest are suppressed to avoid floods (default 3)");
        AddNumberField(fanout, "Max Event Age (hours)", "notifications.fanout.max_event_age_hours",
            "Events older than this are dropped instead of delivered late, e.g. after extended downtime (default 72)");
        advanced.Children.Add(WrapInCard(fanout));

        var retention = BeginCard();
        retention.Children.Add(new TextBlock
        {
            Text = "RETENTION", FontSize = 11, FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 220, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 10),
        });
        AddNumberField(retention, "Read Notifications (days)", "notifications.retention.read_days",
            "How long read inbox entries are kept (default 90)");
        AddNumberField(retention, "Unread Notifications (days)", "notifications.retention.unread_days",
            "How long unread inbox entries are kept (default 180)");
        AddNumberField(retention, "Processed Events (days)", "notifications.retention.event_days",
            "How long processed release events are kept for debugging (default 30)");
        var retentionCard = WrapInCard(retention);
        Grid.SetColumn(retentionCard, 1);
        advanced.Children.Add(retentionCard);
        ContentPanel.Children.Add(advanced);
    }

    private FrameworkElement BuildNotificationPipeline()
    {
        var card = new StackPanel { Spacing = 14 };
        card.Children.Add(new TextBlock
        {
            Text = "PIPELINE", FontSize = 11, FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 220, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
        });
        var stages = new Grid { ColumnSpacing = 28 };
        for (var index = 0; index < 3; index++)
            stages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stages.Children.Add(BuildNotificationPipelineStage("\uE789", "Record events",
            "Log new-content availability during library scans. Off stops notifications at the source.",
            "notifications.release_events_enabled"));
        var fanout = BuildNotificationPipelineStage("\uE8B5", "Fan out",
            "Match recorded events to interested profiles and queue deliveries.",
            "notifications.fanout_enabled");
        Grid.SetColumn(fanout, 1);
        stages.Children.Add(fanout);
        var enabledChannels = new[]
        {
            "notifications.ui_enabled", "notifications.web_push_enabled", "notifications.apple_push_delivery_enabled",
            "notifications.email_enabled", "notifications.discord_enabled", "notifications.webhooks_enabled",
            "notifications.server_channels_enabled",
        }.Count(IsSettingEnabled);
        var deliver = BuildNotificationPipelineStage("\uE7F4", "Deliver",
            "Hand off to the delivery channels below.", null, $"{enabledChannels}/7 channels on");
        Grid.SetColumn(deliver, 2);
        stages.Children.Add(deliver);
        card.Children.Add(stages);
        return new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 18, 20, 18),
            Child = card,
        };
    }

    private FrameworkElement BuildNotificationPipelineStage(string glyph, string title, string description, string? key, string? badge = null)
    {
        var stage = new Grid { ColumnSpacing = 10 };
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stage.Children.Add(new FontIcon
        {
            Glyph = glyph, FontSize = 15, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 2, 0, 0),
        });
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock
        {
            Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(copy, 1);
        stage.Children.Add(copy);
        FrameworkElement control;
        if (key is not null)
        {
            control = CreateStagedSettingToggle(key);
        }
        else
        {
            var badgeControl = BuildNotificationBadge(badge ?? "");
            if (title == "Deliver") _notificationEnabledCountText = (TextBlock)badgeControl.Child;
            control = badgeControl;
        }
        Grid.SetColumn(control, 2);
        stage.Children.Add(control);
        return stage;
    }

    private void AddNotificationChannelRow(StackPanel host, string glyph, string title, string description,
        string key, string? badge = null, StackPanel? details = null)
    {
        if (host.Children.Count > 0)
            host.Children.Add(new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0),
            });
        var header = new Grid { Padding = new Thickness(16, 13, 16, 13), ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            Child = new FontIcon { Glyph = glyph, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(badge)) titleRow.Children.Add(BuildNotificationBadge(badge));
        copy.Children.Add(titleRow);
        copy.Children.Add(new TextBlock
        {
            Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        var toggle = CreateStagedSettingToggle(key);
        Grid.SetColumn(toggle, 2);
        header.Children.Add(toggle);

        if (details is null)
        {
            host.Children.Add(header);
            return;
        }

        host.Children.Add(new Expander
        {
            Header = header,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                Padding = new Thickness(20, 8, 20, 18),
                Child = details,
            },
        });
    }

    private ToggleSwitch CreateStagedSettingToggle(string key)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = IsSettingEnabled(key),
            OnContent = "", OffContent = "", MinWidth = 44,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += (_, _) =>
        {
            ViewModel.SetSetting(key, toggle.IsOn ? "true" : "false");
            UpdateDirtyCountText();
            UpdateNotificationEnabledCount();
        };
        return toggle;
    }

    private bool IsSettingEnabled(string key)
        => string.Equals(ViewModel.GetSetting(key), "true", StringComparison.OrdinalIgnoreCase);

    private void UpdateNotificationEnabledCount()
    {
        if (_notificationEnabledCountText is null) return;
        var count = new[]
        {
            "notifications.ui_enabled", "notifications.web_push_enabled", "notifications.apple_push_delivery_enabled",
            "notifications.email_enabled", "notifications.discord_enabled", "notifications.webhooks_enabled",
            "notifications.server_channels_enabled",
        }.Count(IsSettingEnabled);
        _notificationEnabledCountText.Text = $"{count}/7 channels on";
    }

    private static Border BuildNotificationBadge(string text) => new()
    {
        Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(7, 2, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text, FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        },
    };

    private StackPanel BuildDiscordNotificationDetails()
    {
        var details = new StackPanel { Spacing = 0 };
        AddTextField(details, "Client ID", "discord.client_id", "The Discord application's OAuth2 client ID (used for account linking)");
        AddPasswordField(details, "Client Secret", "discord.client_secret", "The Discord application's OAuth2 client secret");
        AddPasswordField(details, "Bot Token", "discord.bot_token", "The bot user's token (used to send DMs)");
        var test = new Button { Content = "Test bot token", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8) };
        var result = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            try
            {
                var response = await _settingsApi.TestDiscordBotAsync();
                result.Text = $"{(response.Ok ? "Success" : "Failed")} ({response.DurationMs}ms)" +
                              (string.IsNullOrWhiteSpace(response.Message) ? "" : $" — {response.Message}");
                result.Foreground = (SolidColorBrush)Application.Current.Resources[response.Ok ? "SuccessBrush" : "WarningBrush"];
                result.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { ShowStatusToast($"Discord test failed: {ex.Message}"); }
            finally { test.IsEnabled = true; }
        };
        details.Children.Add(test);
        details.Children.Add(result);
        AddToggleField(details, "Allow Per-Episode DMs", "notifications.discord.allow_per_episode",
            "Let users choose a DM per episode instead of the daily digest. Off coerces those accounts to the digest.");
        AddNumberField(details, "Digest Hour", "notifications.discord.digest_hour", "Hour of day (0-23, server time) when daily digest DMs go out (default 8)");
        AddSelectField(details, "Embed Posters", "notifications.discord.poster_mode",
            [("provider", "Provider CDNs only (default)"), ("server", "Provider CDNs + server storage"), ("off", "No images")],
            "Artwork in outgoing Discord messages across personal webhooks, bot DMs, and server channels.");
        return details;
    }

    private StackPanel BuildPushRelayDetails()
    {
        const string defaultRelayUrl = "https://push.siloserver.org";
        var savedUrl = ViewModel.GetSetting("notifications.push_relay_url");
        if (string.IsNullOrWhiteSpace(savedUrl)) savedUrl = defaultRelayUrl;
        var deploymentId = ViewModel.GetSetting("notifications.push_relay_deployment_id");
        var keyPrefix = ViewModel.GetSetting("notifications.push_relay_key_prefix");
        var expiresAt = ViewModel.GetSetting("notifications.push_relay_expires_at");
        var credentialReady = ViewModel.IsSensitiveConfigured("notifications.push_relay_api_key");
        var details = new StackPanel { Spacing = 10 };
        details.Children.Add(new TextBlock
        {
            Text = "Privacy disclosure", FontSize = 14, FontWeight = FontWeights.SemiBold,
        });
        details.Children.Add(new TextBlock
        {
            Text = "Push requests are content-free. The relay never receives notification text, media names, user or profile names, or your server URL. It processes only technical delivery metadata such as an opaque deployment ID, timing/status, app topic, source IP, and a hashed device push token. The app fetches private content directly from your Silo server after receiving a generic push.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        details.Children.Add(new TextBlock { Text = "Relay URL", FontSize = 14, FontWeight = FontWeights.Medium });
        var relayUrl = new TextBox { Text = savedUrl, Style = (Style)Application.Current.Resources["DarkTextBoxStyle"], MaxWidth = 448, HorizontalAlignment = HorizontalAlignment.Left };
        details.Children.Add(relayUrl);
        var status = new TextBlock
        {
            Text = credentialReady
                ? $"Relay configured{(string.IsNullOrWhiteSpace(keyPrefix) ? "" : $" · credential {keyPrefix}")}. {BuildPushRelayRenewalText(expiresAt, true)}"
                : $"Relay registration required. {BuildPushRelayRenewalText(expiresAt, !string.IsNullOrWhiteSpace(deploymentId))}",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        };
        details.Children.Add(status);
        if (!string.IsNullOrWhiteSpace(deploymentId))
            details.Children.Add(new TextBlock
            {
                Text = $"Deployment ID  {deploymentId}", FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            });
        var register = new Button
        {
            Content = string.IsNullOrWhiteSpace(deploymentId) ? "Register relay" : "Rotate credential",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        register.Click += async (_, _) =>
        {
            register.IsEnabled = false;
            var oldContent = register.Content;
            register.Content = "Registering…";
            try
            {
                var registered = await ViewModel.RegisterPushRelayAsync(relayUrl.Text.Trim());
                status.Text = $"Relay configured{(string.IsNullOrWhiteSpace(registered.KeyPrefix) ? "" : $" · credential {registered.KeyPrefix}")}. {BuildPushRelayRenewalText(registered.ExpiresAt, true)}";
                register.Content = "Rotate credential";
            }
            catch (Exception ex)
            {
                register.Content = oldContent;
                ShowStatusToast($"Relay registration failed: {ex.Message}");
            }
            finally { register.IsEnabled = true; }
        };
        details.Children.Add(register);
        return details;
    }

    private void BuildNotificationsAdminTab()
    {
        AddTabHeader("Notifications",
            "Operational controls for the notification system. All settings apply live — no restart needed. Per-profile preferences live in each user's own notification settings.");

        AddSectionHeader("Pipeline");
        var pipelineCard = BeginCard();
        AddToggleField(pipelineCard, "Release Events", "notifications.release_events_enabled",
            "Create notification events for new content and request activity.");
        AddToggleField(pipelineCard, "Fanout Enabled", "notifications.fanout_enabled",
            "Deliver notification events to eligible users and profiles.");
        AddToggleField(pipelineCard, "In-App Inbox", "notifications.ui_enabled");
        AddToggleField(pipelineCard, "Webhooks", "notifications.webhooks_enabled");
        AddToggleField(pipelineCard, "Web Push", "notifications.web_push_enabled");
        EndCard(pipelineCard);

        BuildPushRelayCard();

        AddSectionHeader("Fanout");
        var fanoutCard = BeginCard();
        AddNumberField(fanoutCard, "Settle Delay (seconds)", "notifications.fanout.settle_seconds",
            "How long an event must sit before fanout claims it, so one scan's episodes batch together (default 30)");
        AddNumberField(fanoutCard, "Max Series Burst", "notifications.fanout.max_series_burst",
            "Max notifications per series per batch; the rest are suppressed to avoid floods (default 3)");
        AddNumberField(fanoutCard, "Max Event Age (hours)", "notifications.fanout.max_event_age_hours",
            "Events older than this are dropped instead of delivered late, e.g. after extended downtime (default 72)");
        EndCard(fanoutCard);

        AddSectionHeader("Webhooks");
        var webhooksCard = BeginCard();
        AddNumberField(webhooksCard, "Max Webhooks Per Profile", "notifications.webhooks.max_per_profile",
            "How many webhooks a single profile may create (default 10)");
        AddNumberField(webhooksCard, "Deliveries Per Minute Per Profile", "notifications.webhooks.deliveries_per_minute_per_profile",
            "Webhook delivery rate limit; over-limit notifications still reach the inbox (default 60)");
        AddToggleField(webhooksCard, "Allow Private Destinations", "notifications.webhooks.allow_private_destinations",
            "Disables the SSRF guard so webhooks may target private and LAN addresses. Development only.");
        EndCard(webhooksCard);

        AddSectionHeader("Email Delivery");
        var emailCard = BeginCard();
        AddToggleField(emailCard, "Email Notifications", "notifications.email_enabled");
        AddToggleField(emailCard, "Allow Per-Episode Email", "notifications.email.allow_per_episode");
        AddNumberField(emailCard, "Digest Hour", "notifications.email.digest_hour", "0-23, server local time.");
        AddTextField(emailCard, "External URL", "notifications.email.external_url",
            "Public URL used in notification links.");
        EndCard(emailCard);

        AddSectionHeader("Discord");
        var discordCard = BeginCard();
        AddToggleField(discordCard, "Discord Notifications", "notifications.discord_enabled");
        AddTextField(discordCard, "Client ID", "discord.client_id", "The Discord application's OAuth2 client ID (used for account linking)");
        AddPasswordField(discordCard, "Client Secret", "discord.client_secret", "The Discord application's OAuth2 client secret");
        AddPasswordField(discordCard, "Bot Token", "discord.bot_token", "The bot user's token (used to send DMs)");
        var discordTest = new Button { Content = "Test bot token", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
        var discordTestResult = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        discordTest.Click += async (_, _) =>
        {
            discordTest.IsEnabled = false;
            try
            {
                var response = await _settingsApi.TestDiscordBotAsync();
                discordTestResult.Text = $"{(response.Ok ? "Success" : "Failed")} ({response.DurationMs}ms)" +
                                         (string.IsNullOrWhiteSpace(response.Message) ? "" : $" — {response.Message}");
                discordTestResult.Foreground = new SolidColorBrush(response.Ok
                    ? Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)
                    : Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
                discordTestResult.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { ShowStatusToast($"Discord test failed: {ex.Message}"); }
            finally { discordTest.IsEnabled = true; }
        };
        discordCard.Children.Add(discordTest);
        discordCard.Children.Add(discordTestResult);
        AddToggleField(discordCard, "Allow Per-Episode DMs", "notifications.discord.allow_per_episode",
            "Let users choose a DM per episode instead of the daily digest. Off coerces those accounts to the digest.");
        AddNumberField(discordCard, "Digest Hour", "notifications.discord.digest_hour", "Hour of day (0-23, server time) when daily digest DMs go out (default 8)");
        AddSelectField(discordCard, "Embed Posters", "notifications.discord.poster_mode",
            [
                ("provider", "Provider CDNs only (default)"),
                ("server", "Provider CDNs + server storage"),
                ("off", "No images"),
            ],
            "Artwork in outgoing Discord messages across personal webhooks, bot DMs, and server channels.");
        EndCard(discordCard);

        AddSectionHeader("Server Channels");
        var channelCard = BeginCard();
        AddToggleField(channelCard, "Server Channels Enabled", "notifications.server_channels_enabled");
        AddNumberField(channelCard, "Batch Window (seconds)", "notifications.server_channels.batch_seconds");
        AddToggleField(channelCard, "Mention Requesters on Discord", "notifications.server_channels.mention_requesters",
            "Mention linked requesters in request-related server channel posts.");
        AddDivider(channelCard);
        var channelsHost = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 4) };
        channelCard.Children.Add(channelsHost);
        _ = LoadServerNotificationChannelsAsync(channelsHost);
        EndCard(channelCard);

        AddSectionHeader("Retention");
        var retentionCard = BeginCard();
        AddNumberField(retentionCard, "Read Notifications (days)", "notifications.retention.read_days");
        AddNumberField(retentionCard, "Unread Notifications (days)", "notifications.retention.unread_days");
        AddNumberField(retentionCard, "Processed Events (days)", "notifications.retention.event_days");
        EndCard(retentionCard);
    }

    private async Task LoadServerNotificationChannelsAsync(StackPanel host)
    {
        host.Children.Clear();
        host.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center });
        try
        {
            var response = await _settingsApi.GetServerNotificationChannelsAsync();
            host.Children.Clear();
            foreach (var channel in response.Channels)
                host.Children.Add(BuildServerNotificationChannelCard(channel, host));
            if (response.Channels.Count == 0)
                host.Children.Add(new TextBlock
                {
                    Text = "No server channels yet. Create one to broadcast new content and request activity.",
                    FontSize = 14, TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
                });
            var add = new Button { Content = "Add server channel", HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += async (_, _) => await ShowServerNotificationChannelEditorAsync(null, host);
            host.Children.Add(add);
        }
        catch (Exception ex)
        {
            host.Children.Clear();
            host.Children.Add(new TextBlock
            {
                Text = $"Server channels could not be loaded: {ex.Message}", TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            });
        }
    }

    private Border BuildServerNotificationChannelCard(ServerNotificationChannel channel, StackPanel host)
    {
        var panel = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        identity.Children.Add(new TextBlock { Text = channel.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        identity.Children.Add(CreateSearchStatusBadge(channel.Type));
        identity.Children.Add(new TextBlock
        {
            Text = channel.UrlHost, FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(identity);
        var enabled = new ToggleSwitch
        {
            IsOn = channel.Enabled, OnContent = "Enabled", OffContent = "Disabled",
            VerticalAlignment = VerticalAlignment.Center
        };
        enabled.Toggled += async (_, _) =>
        {
            enabled.IsEnabled = false;
            try
            {
                await _settingsApi.UpdateServerNotificationChannelAsync(channel.Id,
                    new Dictionary<string, object?> { ["enabled"] = enabled.IsOn });
                await LoadServerNotificationChannelsAsync(host);
            }
            catch (Exception ex) { ShowStatusToast($"Channel update failed: {ex.Message}"); enabled.IsEnabled = true; }
        };
        Grid.SetColumn(enabled, 1);
        header.Children.Add(enabled);
        panel.Children.Add(header);

        var events = new List<string>();
        if (channel.NotifyNewMovies) events.Add("New movies");
        if (channel.NotifyNewEpisodes) events.Add("New episodes");
        if (channel.NotifyNewAudiobooks) events.Add("New audiobooks");
        if (channel.NotifyNewEbooks) events.Add("New ebooks");
        if (channel.NotifyRequestSubmitted) events.Add("Request submitted");
        if (channel.NotifyRequestApproved) events.Add("Request approved");
        if (channel.NotifyRequestDeclined) events.Add("Request declined");
        if (channel.NotifyRequestFulfilled) events.Add("Request fulfilled");
        panel.Children.Add(new TextBlock
        {
            Text = events.Count == 0 ? "No events selected" : string.Join(" · ", events),
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        if (!string.IsNullOrWhiteSpace(channel.DisabledReason) || !string.IsNullOrWhiteSpace(channel.LastFailureMessage))
            panel.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(channel.DisabledReason)
                    ? $"Disabled: {channel.DisabledReason} Re-enable the channel to resume from now."
                    : $"Last failure: {channel.LastFailureMessage ?? $"HTTP {channel.LastFailureStatus}"}. Check the destination URL.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24))
            });
        else if (!string.IsNullOrWhiteSpace(channel.LastSuccessAt))
            panel.Children.Add(new TextBlock
            {
                Text = $"Last post: {FormatSearchStatusDate(channel.LastSuccessAt)}", FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            });

        var result = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(result);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var test = new Button { Content = "Test" };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            try
            {
                var testResult = await _settingsApi.TestServerNotificationChannelAsync(channel.Id);
                result.Text = $"Test {(testResult.Ok ? "succeeded" : "failed")}" +
                              (testResult.HttpStatus.HasValue ? $" (HTTP {testResult.HttpStatus}, {testResult.DurationMs}ms)" : $" ({testResult.DurationMs}ms)") +
                              (string.IsNullOrWhiteSpace(testResult.Message) ? "" : $" — {testResult.Message}");
                result.Foreground = new SolidColorBrush(testResult.Ok
                    ? Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)
                    : Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
                result.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { ShowStatusToast($"Test request failed: {ex.Message}"); }
            finally { test.IsEnabled = true; }
        };
        actions.Children.Add(test);
        var edit = new Button { Content = "Edit" };
        edit.Click += async (_, _) => await ShowServerNotificationChannelEditorAsync(channel, host);
        actions.Children.Add(edit);
        if (channel.Type == "generic")
        {
            var rotate = new Button { Content = "Rotate secret" };
            rotate.Click += async (_, _) =>
            {
                rotate.IsEnabled = false;
                try
                {
                    var secret = await _settingsApi.RotateServerNotificationChannelSecretAsync(channel.Id);
                    await ShowSigningSecretAsync(secret.SigningSecret);
                }
                catch (Exception ex) { ShowStatusToast($"Failed to rotate signing secret: {ex.Message}"); }
                finally { rotate.IsEnabled = true; }
            };
            actions.Children.Add(rotate);
        }
        var delete = new Button { Content = "Delete" };
        delete.Click += async (_, _) =>
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = $"Delete \"{channel.Name}\"?",
                Content = "Server events will stop posting to this destination. This cannot be undone.",
                PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                await _settingsApi.DeleteServerNotificationChannelAsync(channel.Id);
                await LoadServerNotificationChannelsAsync(host);
            }
            catch (Exception ex) { ShowStatusToast($"Failed to delete channel: {ex.Message}"); }
        };
        actions.Children.Add(delete);
        panel.Children.Add(actions);

        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Child = panel
        };
    }

    private async Task ShowServerNotificationChannelEditorAsync(ServerNotificationChannel? channel, StackPanel host)
    {
        var content = new StackPanel { Spacing = 10, MinWidth = 440 };
        content.Children.Add(new TextBlock
        {
            Text = "Server channels broadcast server-wide events — every profile sees the same posts. Discord webhook URLs render as native embeds; any other HTTPS endpoint receives signed JSON.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        content.Children.Add(new TextBlock { Text = "Name", FontSize = 14, FontWeight = FontWeights.Medium });
        var name = new TextBox { Text = channel?.Name ?? "", PlaceholderText = "Community #new-content", MaxLength = 64 };
        content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = channel is null ? "URL" : "Replace URL (optional)", FontSize = 14, FontWeight = FontWeights.Medium });
        var url = new TextBox
        {
            PlaceholderText = channel is null ? "https://discord.com/api/webhooks/…" : $"Currently pointing at {channel.UrlHost}"
        };
        content.Children.Add(url);
        var toggles = new Dictionary<string, ToggleSwitch>();
        void AddEvent(string section, string key, string label, bool current)
        {
            if (!content.Children.OfType<TextBlock>().Any(block => block.Text == section))
                content.Children.Add(new TextBlock { Text = section, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            var toggle = new ToggleSwitch { IsOn = current, OnContent = "", OffContent = "" };
            toggles[key] = toggle;
            content.Children.Add(CreateMarkerProviderToggleRow(label, null, toggle));
        }
        AddEvent("New content", "notify_new_movies", "New movies", channel?.NotifyNewMovies ?? true);
        AddEvent("New content", "notify_new_episodes", "New episodes", channel?.NotifyNewEpisodes ?? true);
        AddEvent("New content", "notify_new_audiobooks", "New audiobooks", channel?.NotifyNewAudiobooks ?? true);
        AddEvent("New content", "notify_new_ebooks", "New ebooks", channel?.NotifyNewEbooks ?? true);
        AddEvent("Media requests", "notify_request_submitted", "Request submitted", channel?.NotifyRequestSubmitted ?? false);
        AddEvent("Media requests", "notify_request_approved", "Request approved", channel?.NotifyRequestApproved ?? false);
        AddEvent("Media requests", "notify_request_declined", "Request declined", channel?.NotifyRequestDeclined ?? false);
        AddEvent("Media requests", "notify_request_fulfilled", "Request fulfilled", channel?.NotifyRequestFulfilled ?? false);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = channel is null ? "Add server channel" : $"Edit \"{channel.Name}\"",
            Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = channel is null ? "Create" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text) || (channel is null && string.IsNullOrWhiteSpace(url.Text)))
        {
            ShowStatusToast(channel is null && string.IsNullOrWhiteSpace(url.Text) ? "A webhook URL is required." : "A channel name is required.");
            return;
        }
        var input = toggles.ToDictionary(pair => pair.Key, pair => (object?)pair.Value.IsOn);
        input["name"] = name.Text.Trim();
        if (!string.IsNullOrWhiteSpace(url.Text)) input["url"] = url.Text.Trim();
        try
        {
            var saved = channel is null
                ? await _settingsApi.CreateServerNotificationChannelAsync(input)
                : await _settingsApi.UpdateServerNotificationChannelAsync(channel.Id, input);
            if (!string.IsNullOrWhiteSpace(saved.SigningSecret)) await ShowSigningSecretAsync(saved.SigningSecret);
            await LoadServerNotificationChannelsAsync(host);
        }
        catch (Exception ex) { ShowStatusToast($"Channel could not be saved: {ex.Message}"); }
    }

    private async Task ShowSigningSecretAsync(string secret)
    {
        var box = new TextBox { Text = secret, IsReadOnly = true, IsSpellCheckEnabled = false, MinWidth = 420 };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Copy this signing secret now. It will not be shown again.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(box);
        await new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Signing secret", Content = content, CloseButtonText = "Done"
        }.ShowAsync();
    }

    private void BuildPushRelayCard()
    {
        const string defaultRelayUrl = "https://push.siloserver.org";
        var savedUrl = ViewModel.GetSetting("notifications.push_relay_url");
        if (string.IsNullOrWhiteSpace(savedUrl)) savedUrl = defaultRelayUrl;
        var deploymentId = ViewModel.GetSetting("notifications.push_relay_deployment_id");
        var keyPrefix = ViewModel.GetSetting("notifications.push_relay_key_prefix");
        var expiresAt = ViewModel.GetSetting("notifications.push_relay_expires_at");
        var mustReregister = string.Equals(
            ViewModel.GetSetting("notifications.push_relay_reregistration_required"), "true",
            StringComparison.OrdinalIgnoreCase);
        var credentialReady = ViewModel.IsSensitiveConfigured("notifications.push_relay_api_key");

        AddSectionHeader("Silo Push Relay");
        var card = BeginCard();
        AddToggleField(card, "Mobile Push Delivery", "notifications.apple_push_delivery_enabled",
            "Mobile push through Silo's relay. Apple devices use APNs today; Android support will use the same relay when available.");

        AddDivider(card);
        var privacy = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 10) };
        privacy.Children.Add(new TextBlock
        {
            Text = "Privacy disclosure",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });
        privacy.Children.Add(new TextBlock
        {
            Text = "Push requests are content-free. The relay never receives notification text, media names, user or profile names, or your server URL. It processes only technical delivery metadata such as an opaque deployment ID, timing/status, app topic, source IP, and a hashed device push token. The app fetches private content directly from your Silo server after receiving a generic push.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        card.Children.Add(privacy);

        AddDivider(card);
        var relay = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 10) };
        relay.Children.Add(new TextBlock
        {
            Text = "Relay URL",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });
        var relayUrl = new TextBox
        {
            Text = savedUrl,
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            Width = 448,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        relay.Children.Add(relayUrl);
        relay.Children.Add(new TextBlock
        {
            Text = "Public relay endpoint used by this Silo server; it is stored only when registration succeeds.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
        });

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = mustReregister
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24))
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        };
        var renewal = BuildPushRelayRenewalText(expiresAt, !string.IsNullOrWhiteSpace(deploymentId));
        status.Text = mustReregister
            ? "The current capability was rejected or revoked. Automatic registration is disabled; explicitly re-register to create a new relay deployment."
            : credentialReady
                ? $"Relay configured{(string.IsNullOrWhiteSpace(keyPrefix) ? "" : $" · credential {keyPrefix}")}. {renewal}"
                : $"Relay registration required. {renewal}";
        relay.Children.Add(status);

        if (!string.IsNullOrWhiteSpace(deploymentId))
            relay.Children.Add(new TextBlock
            {
                Text = $"Deployment ID  {deploymentId}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            });

        var resultText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x34, 0xD3, 0x99)),
            Visibility = Visibility.Collapsed,
        };
        var register = new Button
        {
            Content = mustReregister ? "Re-register relay" : !string.IsNullOrWhiteSpace(deploymentId) ? "Rotate credential" : "Register relay",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        register.Click += async (_, _) =>
        {
            if (!register.IsEnabled) return;
            register.IsEnabled = false;
            var oldContent = register.Content;
            register.Content = "Registering…";
            resultText.Visibility = Visibility.Collapsed;
            try
            {
                var result = await ViewModel.RegisterPushRelayAsync(relayUrl.Text.Trim());
                resultText.Text = $"Credential ready for {result.DeploymentId}"
                    + (string.IsNullOrWhiteSpace(result.KeyPrefix) ? "" : $" · key {result.KeyPrefix}")
                    + (string.IsNullOrWhiteSpace(result.RelayRequestId) ? "" : $" · relay {result.RelayRequestId}");
                resultText.Visibility = Visibility.Visible;
                register.Content = "Rotate credential";
                status.Text = $"Relay configured{(string.IsNullOrWhiteSpace(result.KeyPrefix) ? "" : $" · credential {result.KeyPrefix}")}. {BuildPushRelayRenewalText(result.ExpiresAt, true)}";
                status.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
            }
            catch (Exception ex)
            {
                register.Content = oldContent;
                ViewModel.ErrorMessage = $"Relay registration failed: {ex.Message}";
                ShowStatusToast(ViewModel.ErrorMessage);
            }
            finally { register.IsEnabled = true; }
        };
        relay.Children.Add(register);
        relay.Children.Add(resultText);
        card.Children.Add(relay);
        EndCard(card);
    }

    private static string BuildPushRelayRenewalText(string expiresAt, bool configured)
    {
        if (DateTimeOffset.TryParse(expiresAt, out var expiration))
            return expiration <= DateTimeOffset.Now
                ? "Credential expired; Silo will renew before the next delivery when the relay grace period permits."
                : $"Expires {expiration.ToLocalTime():g}; Silo renews automatically during the final week.";
        return configured
            ? "Expiration is unknown; it will refresh on the next credential lifecycle operation."
            : "No relay credential is registered.";
    }

    private void BuildJellyfinTab()
    {
        AddTabHeader("Compatibility Proxies", "Configure protocol-compatible listener surfaces for external client apps.");

        AddSectionHeader("Jellyfin");
        var jellyfinCard = BeginCard();
        AddToggleField(jellyfinCard, "Enable Jellyfin Proxy", "jellyfin_compat.enabled",
            "Starts the Jellyfin-compatible API listener for external Jellyfin clients.", false,
            enabled =>
            {
                if (!enabled) ViewModel.SetSetting("jellyfin_compat.web_enabled", "false");
            });

        var summaryBadges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 8)
        };
        jellyfinCard.Children.Add(summaryBadges);

        var details = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        var detailsToggle = new Button
        {
            Content = "Show settings",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 2, 0, 4)
        };
        detailsToggle.Click += (_, _) =>
        {
            bool show = details.Visibility != Visibility.Visible;
            details.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            detailsToggle.Content = show ? "Hide settings" : "Show settings";
        };
        jellyfinCard.Children.Add(detailsToggle);

        var layers = new Grid { ColumnSpacing = 12 };
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var apiLayer = CreateCompatibilityDescription("API Layer",
            "Provides the Jellyfin-compatible API surface used by most third-party apps for discovery, authentication, browsing, metadata, and playback.");
        var webLayer = CreateCompatibilityDescription("Web Component Layer",
            "Provides the Jellyfin Web UI assets required by Jellyfin native apps and some other clients that expect Jellyfin Web to exist at the server's web route.");
        Grid.SetColumn(webLayer, 1);
        layers.Children.Add(apiLayer);
        layers.Children.Add(webLayer);
        details.Children.Add(layers);

        var statusHost = new StackPanel { Spacing = 0 };
        details.Children.Add(statusHost);

        AddDivider(details);
        details.Children.Add(new TextBlock { Text = "Web Component", FontSize = 14, FontWeight = FontWeights.SemiBold });
        details.Children.Add(new TextBlock
        {
            Text = "The Web Component is separate from the API layer. Disabling the Web UI stops Silo from serving the route while keeping installed assets available for later reactivation.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        AddTextField(details, "Pinned Web Version (Optional)", "jellyfin_compat.web_version",
            "Optional. Leave blank to auto-select the latest compatible patch for the emulated API version.");
        AddTextField(details, "Web Install Directory (Optional)", "jellyfin_compat.web_install_dir",
            "Optional. Defaults to Silo's managed Jellyfin Web install directory.");

        AddDivider(details);
        details.Children.Add(new TextBlock { Text = "Server Identity", FontSize = 14, FontWeight = FontWeights.SemiBold });
        AddTextField(details, "Public URL", "jellyfin_compat.public_url");
        AddTextField(details, "Server Name", "jellyfin_compat.server_name");
        AddTextField(details, "Server ID", "jellyfin_compat.server_id");
        AddTextField(details, "Emulated Server Version", "jellyfin_compat.emulated_server_version");
        AddDurationField(details, "Session TTL", "jellyfin_compat.session_ttl", "e.g. 24h");
        AddDurationField(details, "Playback Session TTL", "jellyfin_compat.playback_session_ttl", "e.g. 6h");
        jellyfinCard.Children.Add(details);
        _ = LoadJellyfinCompatStatusAsync(summaryBadges, statusHost);
        EndCard(jellyfinCard);

        AddSectionHeader("Audiobookshelf");
        var absCard = BeginCard();
        AddToggleField(absCard, "Enable Audiobookshelf Proxy", "audiobookshelf_compat.enabled",
            "Starts the ABS-compatible API listener for external Audiobookshelf clients.");
        EndCard(absCard);
    }

    private static Border CreateCompatibilityDescription(string title, string description)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = content
        };
    }

    private async Task LoadJellyfinCompatStatusAsync(StackPanel summary, StackPanel host)
    {
        summary.Children.Clear();
        host.Children.Clear();
        host.Children.Add(new ProgressRing { IsActive = true, Width = 22, Height = 22, Margin = new Thickness(0, 12, 0, 12) });
        try
        {
            var status = await _settingsApi.GetJellyfinCompatStatusAsync();
            summary.Children.Clear();
            summary.Children.Add(CreateSearchStatusBadge(status.Enabled ? "API enabled" : "API disabled"));
            summary.Children.Add(CreateSearchStatusBadge(status.Enabled && status.WebEnabled ? "Web UI enabled" : "Web UI disabled"));
            summary.Children.Add(CreateSearchStatusBadge($"Assets {FormatCompatStatus(status.WebState)}"));
            if (status.Operation?.State == "running")
                summary.Children.Add(CreateSearchStatusBadge($"{FormatCompatStatus(status.Operation.Kind)} running"));
            if (status.RestartRequired) summary.Children.Add(CreateSearchStatusBadge("Restart required"));

            host.Children.Clear();
            if (!string.IsNullOrWhiteSpace(status.LastError))
            {
                host.Children.Add(new TextBlock
                {
                    Text = status.LastError, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71)),
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }

            AddCompatStatusLine(host, "API state", FormatCompatStatus(status.ApiState));
            AddCompatStatusLine(host, "Listen address", status.Listen);
            AddCompatStatusLine(host, "Public URL", status.PublicUrl);
            AddCompatStatusLine(host, "Emulated version", status.EmulatedServerVersion);
            AddCompatStatusLine(host, "Pinned version", status.PinnedVersion);
            AddCompatStatusLine(host, "Installed version", status.InstalledVersion);
            AddCompatStatusLine(host, "Installer", status.InstallerReady ? "Ready" : "Missing prerequisites");
            AddCompatStatusLine(host, "Operation", status.Operation is null
                ? "Idle"
                : $"{FormatCompatStatus(status.Operation.Kind)} {FormatCompatStatus(status.Operation.State)}");
            AddCompatStatusLine(host, "Source", status.SourceUrl);
            AddCompatStatusLine(host, "Commit", status.CommitSha);
            AddCompatStatusLine(host, "Checksum", status.Checksum);
            AddCompatStatusLine(host, "Install path", status.InstallPath);
            AddCompatStatusLine(host, "License present", status.LicensePresent ? "Yes" : "No");
            AddCompatStatusLine(host, "Provenance present", status.ProvenancePresent ? "Yes" : "No");

            if (status.Operation?.State == "running")
            {
                var operation = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 10) };
                operation.Children.Add(new TextBlock
                {
                    Text = status.Operation.Kind == "remove" ? "Removing Jellyfin Web UI" : "Installing Jellyfin Web UI",
                    FontSize = 14, FontWeight = FontWeights.SemiBold
                });
                operation.Children.Add(new TextBlock
                {
                    Text = status.Operation.Message ?? FormatCompatStatus(status.Operation.Phase),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
                });
                if (status.Operation.ProgressPercent is { } percent)
                    operation.Children.Add(new ProgressBar { Value = Math.Clamp(percent, 0, 100), Minimum = 0, Maximum = 100 });
                host.Children.Add(operation);
            }

            var busy = status.Operation?.State == "running" || status.WebState is "installing" or "removing";
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 8) };
            var install = new Button
            {
                Content = status.WebState == "update_available" ? "Update Web UI" : "Install Web UI",
                IsEnabled = status.InstallerReady && !busy
            };
            install.Click += async (_, _) =>
            {
                try
                {
                    await _settingsApi.InstallJellyfinCompatWebAsync(ViewModel.GetSetting("jellyfin_compat.web_version"));
                    ShowStatusToast("Jellyfin Web install started.");
                    await LoadJellyfinCompatStatusAsync(summary, host);
                }
                catch (Exception ex) { ShowStatusToast($"Jellyfin Web install failed: {ex.Message}"); }
            };
            actions.Children.Add(install);

            if (!string.IsNullOrWhiteSpace(status.InstalledVersion))
            {
                var toggleWeb = new Button
                {
                    Content = status.Enabled && status.WebEnabled ? "Disable Web UI" : "Enable Web UI",
                    IsEnabled = status.Enabled && !busy
                };
                toggleWeb.Click += async (_, _) =>
                {
                    try
                    {
                        await _settingsApi.PatchJellyfinCompatSettingsAsync(
                            new Dictionary<string, object?> { ["web_enabled"] = !(status.Enabled && status.WebEnabled) });
                        await LoadJellyfinCompatStatusAsync(summary, host);
                    }
                    catch (Exception ex) { ShowStatusToast($"Jellyfin Web update failed: {ex.Message}"); }
                };
                actions.Children.Add(toggleWeb);
            }

            var remove = new Button { Content = "Remove Web UI", IsEnabled = status.WebState != "missing" && !busy };
            remove.Click += async (_, _) =>
            {
                try
                {
                    await _settingsApi.RemoveJellyfinCompatWebAsync();
                    ShowStatusToast("Jellyfin Web removal started.");
                    await LoadJellyfinCompatStatusAsync(summary, host);
                }
                catch (Exception ex) { ShowStatusToast($"Jellyfin Web removal failed: {ex.Message}"); }
            };
            actions.Children.Add(remove);
            host.Children.Add(actions);

            var missing = status.Prerequisites.Where(item => !item.Available).Select(item => item.Command).ToArray();
            if (missing.Length > 0)
                host.Children.Add(new TextBlock
                {
                    Text = $"Missing installer prerequisites: {string.Join(", ", missing)}",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
                });
        }
        catch (Exception ex)
        {
            summary.Children.Clear();
            summary.Children.Add(CreateSearchStatusBadge("Status unavailable"));
            host.Children.Clear();
            host.Children.Add(new TextBlock
            {
                Text = $"Jellyfin compatibility status could not be loaded: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            });
        }
    }

    private void AddCompatStatusLine(StackPanel host, string label, string? value)
    {
        AddSearchStatusRow(host, label, string.IsNullOrWhiteSpace(value) ? "Not set" : value);
    }

    private static string FormatCompatStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        return string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
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
        AddConnectionCheckButton(redisCard, "redis", "Check Connection");
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
        // Check if redis.url is managed by environment variable
        bool envManaged = ViewModel.IsManagedByEnv("redis.url");

        if (envManaged)
        {
            // Show "Managed by environment" badge + explainer; no editable fields
            var envBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x60, 0xA5, 0xFA)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 4),
            };
            envBadge.Child = new TextBlock
            {
                Text = "Managed by environment",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            };
            parent.Children.Add(envBadge);
            parent.Children.Add(new TextBlock
            {
                Text = "Redis is configured via the REDIS_URL environment variable. To change it, update the environment variable and restart the server.",
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            return;
        }

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
            Width = 448,
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
        AddTabHeader("Storage", "Configure separate S3-compatible storage for client-facing assets and private internal Silo artifacts.");

        // Sub-tab bar: Public Assets | Private Internal | User DB (disabled)
        var subTabBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 8),
        };

        var publicSubTab = MakeSubTabButton("Public Assets", active: true);
        var privateSubTab = MakeSubTabButton("Private Internal", active: false);
        var udbSubTab = MakeSubTabButton("User DB", active: false, disabled: true,
            tooltip: "Reserved for future Litestream replication");

        subTabBar.Children.Add(publicSubTab);
        subTabBar.Children.Add(privateSubTab);
        subTabBar.Children.Add(udbSubTab);
        ContentPanel.Children.Add(subTabBar);

        // ===== Public Assets container =====
        var publicContainer = new StackPanel { Spacing = 12 };

        var pubCard = new StackPanel { Spacing = 0 };
        AddTextBlock(pubCard, "Stores client-facing assets such as artwork, chapter thumbnails, and subtitle files.");
        AddTextBlock(pubCard, "This bucket does not need to be public. Most installs should keep it private and use presigned URLs. Only use Public or Cloudflare Token modes if you want direct CDN/object access.");
        AddTextField(pubCard, "Endpoint", "s3.public_endpoint");
        AddTextField(pubCard, "Region", "s3.public_region");
        AddToggleField(pubCard, "Path Style", "s3.public_path_style");
        AddTextField(pubCard, "Bucket", "s3.public_bucket");
        AddTextField(pubCard, "Key Prefix", "s3.public_key_prefix",
            "Optional. Stores all Silo objects under this folder inside the bucket. Leave blank to use the bucket root.");
        var locationWarning = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 0xF5, 0x9E, 0x0B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 8, 0, 8),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Storage location change", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B)) },
                    new TextBlock
                    {
                        Text = "Artwork is cached in this bucket. After the server restarts, Silo verifies the cache against the new storage and automatically re-caches anything missing. Uploaded images (custom posters, collection artwork, branding) cannot be re-downloaded — migrate your bucket contents if you want to keep them.",
                        FontSize = 12, TextWrapping = TextWrapping.Wrap,
                        Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    },
                },
            },
        };
        pubCard.Children.Add(locationWarning);
        void UpdateLocationWarning()
        {
            var dirty = ViewModel.GetDirtyKeys();
            locationWarning.Visibility = dirty.Any(key => key is "s3.public_endpoint" or "s3.public_bucket" or "s3.public_key_prefix")
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        _dirtyStateUpdaters.Add(UpdateLocationWarning);
        UpdateLocationWarning();
        AddPasswordField(pubCard, "Access Key", "s3.public_access_key");
        AddPasswordField(pubCard, "Secret Key", "s3.public_secret_key");
        AddConnectionCheckButton(pubCard, "s3_public", "Check Connection");
        publicContainer.Children.Add(WrapInCard(pubCard));

        // Public URL Authentication
        var urlAuthCard = new StackPanel { Spacing = 0 };
        urlAuthCard.Children.Add(new TextBlock
        {
            Text = "Asset URL Authentication",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 10),
        });
        AddTextBlock(urlAuthCard,
            "Controls how client-facing asset URLs are generated. Presigned URLs are recommended and work with private buckets.");
        AddS3UrlAuthFields(urlAuthCard);
        publicContainer.Children.Add(WrapInCard(urlAuthCard));

        ContentPanel.Children.Add(publicContainer);

        // ===== Private Internal container =====
        var privateContainer = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };

        var privCard = new StackPanel { Spacing = 0 };
        AddTextBlock(privCard, "Stores non-public Silo objects such as imports, exports, and internal artifacts.");
        AddTextField(privCard, "Endpoint", "s3.private_endpoint");
        AddTextField(privCard, "Region", "s3.private_region");
        AddToggleField(privCard, "Path Style", "s3.private_path_style");
        AddTextField(privCard, "Bucket", "s3.private_bucket");
        AddTextField(privCard, "Key Prefix", "s3.private_key_prefix",
            "Optional. Stores all Silo objects under this folder inside the bucket. Leave blank to use the bucket root.");
        AddPasswordField(privCard, "Access Key", "s3.private_access_key");
        AddPasswordField(privCard, "Secret Key", "s3.private_secret_key");
        AddConnectionCheckButton(privCard, "s3_private", "Check Connection");
        privateContainer.Children.Add(WrapInCard(privCard));

        ContentPanel.Children.Add(privateContainer);

        // Sub-tab click handlers
        publicSubTab.Click += (_, _) =>
        {
            SetSubTabActive(publicSubTab, true);
            SetSubTabActive(privateSubTab, false);
            publicContainer.Visibility = Visibility.Visible;
            privateContainer.Visibility = Visibility.Collapsed;
        };
        privateSubTab.Click += (_, _) =>
        {
            SetSubTabActive(publicSubTab, false);
            SetSubTabActive(privateSubTab, true);
            publicContainer.Visibility = Visibility.Collapsed;
            privateContainer.Visibility = Visibility.Visible;
        };
    }

    private Border WrapInCard(StackPanel content)
    {
        return new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 18, 20, 18),
            Child = content,
        };
    }

    private void SetSubTabActive(Button btn, bool active)
    {
        btn.Background = active
            ? (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
            : new SolidColorBrush(Colors.Transparent);
        btn.BorderThickness = new Thickness(active ? 1 : 0);
    }

    /// <summary>
    /// Builds a pill-style sub-tab button that matches the webui Tabs component.
    /// Used inside tab panels that have multiple views (e.g. Storage: Public | Private | User DB).
    /// </summary>
    private Button MakeSubTabButton(string label, bool active, bool disabled = false, string? tooltip = null)
    {
        var btn = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
            },
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(18),
            Background = active
                ? (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
                : new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(active ? 1 : 0),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            IsEnabled = !disabled,
            Opacity = disabled ? 0.5 : 1.0,
        };
        if (!string.IsNullOrEmpty(tooltip))
            ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    /// <summary>
    /// URL auth method + conditional fields (Public Endpoint, Cloudflare token fields).
    /// Mirrors the webui "Public URL Authentication" section inside StorageSettings.
    /// </summary>
    private void AddS3UrlAuthFields(StackPanel parent)
    {
        // URL Auth Method dropdown with custom labels
        var authField = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
        authField.Children.Add(new TextBlock
        {
            Text = "URL Auth Method",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });

        var authCombo = new ComboBox
        {
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        authCombo.Items.Add(new ComboBoxItem { Content = "S3 Presigned URLs (Recommended)", Tag = "presigned" });
        authCombo.Items.Add(new ComboBoxItem { Content = "Public (no auth)", Tag = "public" });
        authCombo.Items.Add(new ComboBoxItem { Content = "Cloudflare Token Auth", Tag = "cloudflare_token" });

        string currentAuth = ViewModel.GetSetting("s3.public_url_auth");
        if (string.IsNullOrEmpty(currentAuth)) currentAuth = "presigned";
        for (int i = 0; i < authCombo.Items.Count; i++)
        {
            if (authCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == currentAuth)
            {
                authCombo.SelectedIndex = i;
                break;
            }
        }
        if (authCombo.SelectedIndex < 0) authCombo.SelectedIndex = 0;

        authField.Children.Add(authCombo);
        parent.Children.Add(authField);

        // Conditional container — shown for "public" or "cloudflare_token"
        var publicFieldsContainer = new StackPanel
        {
            Spacing = 0,
            Visibility = currentAuth != "presigned" ? Visibility.Visible : Visibility.Collapsed,
        };
        AddTextField(publicFieldsContainer, "Read Endpoint", "s3.public_read_endpoint", "https://cdn.example.com");
        parent.Children.Add(publicFieldsContainer);

        // Cloudflare-only fields
        var cloudflareFieldsContainer = new StackPanel
        {
            Spacing = 0,
            Visibility = currentAuth == "cloudflare_token" ? Visibility.Visible : Visibility.Collapsed,
        };
        AddPasswordField(cloudflareFieldsContainer, "Token Secret", "s3.public_token_secret");
        AddTextField(cloudflareFieldsContainer, "Token Param", "s3.public_token_param", "verify");
        AddNumberField(cloudflareFieldsContainer, "Token TTL (seconds)", "s3.public_token_ttl", "10800");
        parent.Children.Add(cloudflareFieldsContainer);

        // Wire selection change: update setting + toggle visibility of conditional sections
        authCombo.SelectionChanged += (_, _) =>
        {
            if (authCombo.SelectedItem is not ComboBoxItem sel) return;
            var newVal = (sel.Tag as string) ?? "presigned";
            ViewModel.SetSetting("s3.public_url_auth", newVal);
            publicFieldsContainer.Visibility = newVal != "presigned" ? Visibility.Visible : Visibility.Collapsed;
            cloudflareFieldsContainer.Visibility = newVal == "cloudflare_token" ? Visibility.Visible : Visibility.Collapsed;
            UpdateDirtyCountText();
        };

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting("s3.public_url_auth");
            if (string.IsNullOrEmpty(v)) v = "presigned";
            for (int i = 0; i < authCombo.Items.Count; i++)
            {
                if (authCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == v)
                {
                    authCombo.SelectedIndex = i;
                    break;
                }
            }
            publicFieldsContainer.Visibility = v != "presigned" ? Visibility.Visible : Visibility.Collapsed;
            cloudflareFieldsContainer.Visibility = v == "cloudflare_token" ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // ─── Log Retention ───────────────────────────────────────────────────

    private class BucketRow
    {
        public string Component = "";
        public string Level = "info";
        public int RetentionDays = 1;
        public int MaxRows = 100000;
        public int MaxSizeMb = 128;
    }

    // Must match webui logRetentionPolicy.ts DEFAULT_BUCKET_POLICIES exactly.
    // Webui has 4 rules and 3 levels (info/warn/error — no debug).
    private static readonly (string Component, string Level, int RetentionDays, int MaxRows, int MaxSizeMb)[] DefaultBucketPolicies =
    [
        ("metadata",  "info",  1, 100000, 128),
        ("scanner",   "info",  2, 150000, 192),
        ("metadata",  "warn",  7, 250000, 256),
        ("scanner",   "warn",  7, 250000, 256),
    ];

    private List<BucketRow> _bucketRows = [];

    private void BuildLogRetentionTab()
    {
        AddTabHeader("Log Retention",
            "Prune oldest operational logs by global caps and per-bucket overrides. Bucket rules match on component and level. Cleanup cadence and startup runs are configured in Scheduled Tasks.");

        AddSectionHeader("Global Limits");
        var globalCard = BeginCard();
        AddNumberField(globalCard, "Retention Days", "opslog.retention_days",
            "Logs older than this are pruned first.");
        AddNumberField(globalCard, "Max Rows", "opslog.max_rows",
            "Keeps only the newest rows once this total is exceeded.");
        AddNumberField(globalCard, "Max Size (MB)", "opslog.max_size_mb",
            "Uses estimated log row size. Oldest rows are pruned when the budget is exceeded.");
        EndCard(globalCard);

        AddSectionHeader("Policy Decision Logs");
        var policyCard = BeginCard();
        AddNumberField(policyCard, "Decision Log Retention Days", "policy.decision_log_retention_days",
            "Policy decisions older than this are pruned by the cleanup task.");
        AddSelectField(policyCard, "Decision Log Verbosity", "policy.decision_log_verbosity",
            [("digest", "Digest"), ("verbose", "Verbose")],
            "Digest stores hashes only. Verbose stores sampled input and result payloads.");
        AddNumberField(policyCard, "Scope Sample Rate", "policy.decision_log_scope_sample_rate",
            "Logs one sampled scope decision per N allowed decisions. Denials and errors always log.");
        EndCard(policyCard);

        // Parse bucket rules from current setting
        string? bucketParseError = null;
        try
        {
            _bucketRows = ParseBucketPolicies(ViewModel.GetSetting("opslog.bucket_policies"));
        }
        catch
        {
            bucketParseError = "Existing bucket policy JSON could not be parsed. The editor loaded the recommended defaults instead.";
            _bucketRows = DefaultBucketPolicies.Select(d => new BucketRow
            {
                Component = d.Component, Level = d.Level,
                RetentionDays = d.RetentionDays, MaxRows = d.MaxRows, MaxSizeMb = d.MaxSizeMb
            }).ToList();
        }

        AddSectionHeader("Bucket Overrides");

        // Parse-error recovery banner (webui shows this above the editor when JSON is malformed)
        if (bucketParseError != null)
        {
            var warnBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFB, 0xBF, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFB, 0xBF, 0x24)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
            warnBanner.Child = new TextBlock
            {
                Text = bucketParseError,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
                TextWrapping = TextWrapping.Wrap,
            };
            ContentPanel.Children.Add(warnBanner);
        }
        var bucketCard = BeginCard();

        var headerRow = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new TextBlock
        {
            Text = "Use tighter rules for noisy buckets like metadata/info. Set a bucket limit to 0 to disable that bucket-specific cap.",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(headerText, 0);
        headerRow.Children.Add(headerText);

        var headerBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var restoreBtn = new Button
        {
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            CornerRadius = new CornerRadius(6),
        };
        var restoreContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        restoreContent.Children.Add(new FontIcon { Glyph = "\uE72C", FontSize = 11 });
        restoreContent.Children.Add(new TextBlock { Text = "Restore Recommended Rules" });
        restoreBtn.Content = restoreContent;
        headerBtns.Children.Add(restoreBtn);

        var addBtn = new Button
        {
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            CornerRadius = new CornerRadius(6),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        addContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 11 });
        addContent.Children.Add(new TextBlock { Text = "Add Rule" });
        addBtn.Content = addContent;
        headerBtns.Children.Add(addBtn);

        Grid.SetColumn(headerBtns, 1);
        headerRow.Children.Add(headerBtns);
        bucketCard.Children.Add(headerRow);

        // Rules table
        var tableHost = new StackPanel { Spacing = 0, Margin = new Thickness(0, 8, 0, 0) };

        // Column headers
        var columnHeader = BuildBucketRowGrid(isHeader: true);
        var colHeaderBorder = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Child = columnHeader,
        };
        tableHost.Children.Add(colHeaderBorder);

        // Rows container — rebuilt on changes
        var rowsHost = new StackPanel { Spacing = 0 };
        var rowsBorder = new Border
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = rowsHost,
        };
        tableHost.Children.Add(rowsBorder);

        Action refreshRows = null!;
        refreshRows = () =>
        {
            rowsHost.Children.Clear();
            if (_bucketRows.Count == 0)
            {
                rowsHost.Children.Add(new TextBlock
                {
                    Text = "No bucket overrides configured.",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 18, 0, 18),
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                });
            }
            else
            {
                for (int i = 0; i < _bucketRows.Count; i++)
                {
                    int capturedIndex = i;
                    var rowGrid = BuildBucketRowEditor(_bucketRows[i], () =>
                    {
                        ViewModel.SetSetting("opslog.bucket_policies", SerializeBucketPolicies(_bucketRows));
                        UpdateDirtyCountText();
                    }, () =>
                    {
                        _bucketRows.RemoveAt(capturedIndex);
                        ViewModel.SetSetting("opslog.bucket_policies", SerializeBucketPolicies(_bucketRows));
                        UpdateDirtyCountText();
                        refreshRows();
                    });
                    rowsHost.Children.Add(rowGrid);
                }
            }
        };
        refreshRows();

        addBtn.Click += (_, _) =>
        {
            _bucketRows.Add(new BucketRow());
            ViewModel.SetSetting("opslog.bucket_policies", SerializeBucketPolicies(_bucketRows));
            UpdateDirtyCountText();
            refreshRows();
        };
        restoreBtn.Click += (_, _) =>
        {
            _bucketRows.Clear();
            foreach (var d in DefaultBucketPolicies)
                _bucketRows.Add(new BucketRow
                {
                    Component = d.Component, Level = d.Level,
                    RetentionDays = d.RetentionDays, MaxRows = d.MaxRows, MaxSizeMb = d.MaxSizeMb,
                });
            ViewModel.SetSetting("opslog.bucket_policies", SerializeBucketPolicies(_bucketRows));
            UpdateDirtyCountText();
            refreshRows();
        };

        bucketCard.Children.Add(tableHost);
        AddTextBlock(bucketCard,
            "Matching rows are pruned oldest-first when they exceed the bucket rule. Global caps still apply afterward, so noisy buckets cannot crowd out playback or error logs.");
        EndCard(bucketCard);
    }

    private FrameworkElement BuildBucketRowGrid(bool isHeader)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        if (isHeader)
        {
            string[] labels = ["Component", "Level", "Days", "Max Rows", "Max Size (MB)", ""];
            for (int i = 0; i < labels.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = labels[i],
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
        }
        return grid;
    }

    private FrameworkElement BuildBucketRowEditor(BucketRow row, Action onChange, Action onRemove)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var comp = new TextBox { Text = row.Component, PlaceholderText = "metadata" };
        comp.TextChanged += (_, _) => { row.Component = comp.Text ?? ""; onChange(); };
        Grid.SetColumn(comp, 0);
        grid.Children.Add(comp);

        var level = new ComboBox { Width = 110 };
        // Webui LOG_LEVEL_OPTIONS = ["info", "warn", "error"] — no debug.
        foreach (var lv in new[] { "info", "warn", "error" })
            level.Items.Add(new ComboBoxItem { Content = lv, Tag = lv });
        for (int i = 0; i < level.Items.Count; i++)
            if (level.Items[i] is ComboBoxItem ci && (string)ci.Tag == row.Level) { level.SelectedIndex = i; break; }
        if (level.SelectedIndex < 0) level.SelectedIndex = 1;
        level.SelectionChanged += (_, _) =>
        {
            if (level.SelectedItem is ComboBoxItem sel) { row.Level = (string)sel.Tag; onChange(); }
        };
        Grid.SetColumn(level, 1);
        grid.Children.Add(level);

        var days = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Value = row.RetentionDays, Minimum = 0,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Hidden,
        };
        days.ValueChanged += (_, _) => { if (!double.IsNaN(days.Value)) { row.RetentionDays = (int)days.Value; onChange(); } };
        Grid.SetColumn(days, 2);
        grid.Children.Add(days);

        var maxRows = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Value = row.MaxRows, Minimum = 0,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Hidden,
        };
        maxRows.ValueChanged += (_, _) => { if (!double.IsNaN(maxRows.Value)) { row.MaxRows = (int)maxRows.Value; onChange(); } };
        Grid.SetColumn(maxRows, 3);
        grid.Children.Add(maxRows);

        var maxSize = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Value = row.MaxSizeMb, Minimum = 0,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Hidden,
        };
        maxSize.ValueChanged += (_, _) => { if (!double.IsNaN(maxSize.Value)) { row.MaxSizeMb = (int)maxSize.Value; onChange(); } };
        Grid.SetColumn(maxSize, 4);
        grid.Children.Add(maxSize);

        var delBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
        };
        ToolTipService.SetToolTip(delBtn, "Remove rule");
        delBtn.Click += (_, _) => onRemove();
        Grid.SetColumn(delBtn, 5);
        grid.Children.Add(delBtn);

        return new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
    }

    private static List<BucketRow> ParseBucketPolicies(string? json)
    {
        var rows = new List<BucketRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return rows;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var row = new BucketRow();
                if (el.TryGetProperty("component", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                    row.Component = c.GetString() ?? "";
                if (el.TryGetProperty("level", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.String)
                    row.Level = l.GetString() ?? "info";
                if (el.TryGetProperty("retention_days", out var rd) && rd.ValueKind == System.Text.Json.JsonValueKind.Number)
                    row.RetentionDays = rd.GetInt32();
                if (el.TryGetProperty("max_rows", out var mr) && mr.ValueKind == System.Text.Json.JsonValueKind.Number)
                    row.MaxRows = mr.GetInt32();
                if (el.TryGetProperty("max_size_mb", out var ms) && ms.ValueKind == System.Text.Json.JsonValueKind.Number)
                    row.MaxSizeMb = ms.GetInt32();
                rows.Add(row);
            }
        }
        catch { /* malformed — caller gets an empty list, Restore Recommended recovers */ }
        return rows;
    }

    private static string SerializeBucketPolicies(List<BucketRow> rows)
    {
        var sb = new System.Text.StringBuilder("[");
        bool first = true;
        foreach (var r in rows)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"component\":\"").Append(EscapeJsonString(r.Component)).Append("\",\"level\":\"").Append(r.Level).Append("\",");
            sb.Append("\"retention_days\":").Append(r.RetentionDays).Append(',');
            sb.Append("\"max_rows\":").Append(r.MaxRows).Append(',');
            sb.Append("\"max_size_mb\":").Append(r.MaxSizeMb).Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string EscapeJsonString(string s)
    {
        return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ─── Card Overlays ────────────────────────────────────────────────────

    private sealed record OverlayEditorDef(
        string Id,
        string Label,
        string Description,
        string Sample,
        bool DefaultEnabled,
        string DefaultPosition);

    // Registry mirrors web/src/lib/overlays OVERLAY_REGISTRY and v2 prefs.
    private static readonly OverlayEditorDef[] OverlayRegistry =
    [
        new("resolution",         "Resolution",       "Show video resolution, such as 4K or 1080p.",                  "4K",       true,  "top-left"),
        new("hdr",                "HDR",              "Show HDR or Dolby Vision badges.",                            "DV",       true,  "top-left"),
        new("resolution_hdr",     "Resolution + HDR", "Show one combined resolution/HDR badge.",                     "4K DV",    false, "top-left"),
        new("audio",              "Audio",            "Show Atmos, DTS:X, or lossless audio badges.",                "Atmos",    true,  "top-left"),
        new("audio_channels",     "Audio Channels",   "Show the channel layout when available.",                     "7.1",      false, "top-left"),
        new("video_codec",        "Video Codec",      "Show the video codec, such as HEVC or AV1.",                  "HEVC",     false, "top-left"),
        new("container",          "Container",        "Show the media container.",                                   "MKV",      false, "bottom-left"),
        new("aspect_ratio",       "Aspect Ratio",     "Show the aspect ratio when available.",                       "2.39:1",   false, "bottom-right"),
        new("release_type",       "Release Type",     "Show REMUX, WEB-DL, Blu-ray-style release tags.",             "REMUX",    true,  "bottom-left"),
        new("edition",            "Edition",          "Show edition tags, such as Theatrical or Extended.",          "Extended", false, "bottom-left"),
        new("multi_audio",        "Multi-Audio",      "Show when multiple audio tracks are available.",              "Multi",    false, "bottom-right"),
        new("multi_sub",          "Subtitles",        "Show when multiple subtitle tracks are available.",           "CC",       false, "bottom-right"),
        new("rating_imdb",        "IMDb Rating",      "Show IMDb audience rating.",                                  "8.7",      false, "top-right"),
        new("rating_tmdb",        "TMDb Rating",      "Show TMDb audience rating.",                                  "8.5",      false, "top-right"),
        new("rating_rt",          "Rotten Tomatoes",  "Show Rotten Tomatoes critic score.",                          "96%",      false, "top-right"),
        new("rating_rt_audience", "RT Audience",      "Show Rotten Tomatoes audience score.",                        "92%",      false, "top-right"),
        new("content_rating",     "Content Rating",   "Show the content rating.",                                    "TV-MA",    false, "bottom-right"),
        new("year",               "Year",             "Show release year.",                                          "2026",     false, "bottom-left"),
        new("runtime",            "Runtime",          "Show runtime.",                                               "1h 42m",   false, "bottom-left"),
        new("original_language",  "Language",         "Show the original language code.",                            "EN",       false, "bottom-left"),
        new("studio",             "Studio",           "Show the primary studio.",                                    "Studio",   false, "bottom-right"),
        new("network",            "Network",          "Show the TV network.",                                        "HBO",      false, "bottom-right"),
        new("show_status",        "Show Status",      "Show TV series status, such as Returning or Ended.",          "Ended",    false, "top-right"),
        new("imdb_top_250",       "IMDb Top 250",     "Show IMDb Top 250 status when available.",                    "Top 250",  false, "top-right"),
        new("rt_certified_fresh", "Certified Fresh",  "Show Rotten Tomatoes Certified Fresh status when available.", "Fresh",    false, "top-right"),
    ];

    private static readonly (string Value, string Label)[] OverlayPositions =
    [
        ("top-left",     "Top Left"),
        ("top-right",    "Top Right"),
        ("bottom-left",  "Bottom Left"),
        ("bottom-right", "Bottom Right"),
    ];

    // Parsed per-badge prefs, held in memory while the tab is visible.
    // Shape: { id -> (enabled, position) }
    private Dictionary<string, (bool Enabled, string Position)> _overlayPrefs = new();

    private void BuildOverlaysTab()
    {
        AddTabHeader("Card Overlays",
            "Configure the default overlay badges and style preset shown on poster cards. Users can override these in their personal settings.");

        AddSectionHeader("General");
        var genCard = BeginCard();
        AddToggleField(genCard, "Card Overlays Enabled", "overlays.enabled",
            "When disabled, no overlay badges appear for any user regardless of their personal settings.");
        EndCard(genCard);

        AddSectionHeader("Default Configuration");
        var defCard = BeginCard();
        AddTextBlock(defCard,
            "These defaults apply to users who have not customized their overlay settings.");

        // Parse current prefs from the setting value
        _overlayPrefs = ParseOverlayPrefs(ViewModel.GetSetting("defaults.card_overlays"));

        // Two-column layout: per-badge editor on left, preview poster on right
        var layout = new Grid { ColumnSpacing = 20, Margin = new Thickness(0, 6, 0, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        var editor = new StackPanel { Spacing = 10 };
        var previewHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        Action? refreshPreview = null;

        foreach (var def in OverlayRegistry)
        {
            var row = BuildOverlayDefRow(def, () =>
            {
                // Persist back into dirty setting + refresh preview
                ViewModel.SetSetting("defaults.card_overlays", SerializeOverlayPrefs(_overlayPrefs));
                UpdateDirtyCountText();
                refreshPreview?.Invoke();
            });
            editor.Children.Add(row);
        }
        Grid.SetColumn(editor, 0);
        layout.Children.Add(editor);

        // Preview poster (2:3 ratio, 140x210)
        refreshPreview = () =>
        {
            previewHost.Children.Clear();
            previewHost.Children.Add(BuildOverlayPreview());
        };
        refreshPreview();
        Grid.SetColumn(previewHost, 1);
        layout.Children.Add(previewHost);

        defCard.Children.Add(layout);
        EndCard(defCard);
    }

    private FrameworkElement BuildOverlayDefRow(OverlayEditorDef def, Action onChange)
    {
        var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = def.Label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });
        info.Children.Add(new TextBlock
        {
            Text = def.Description,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(info, 0);
        row.Children.Add(info);

        var current = _overlayPrefs.TryGetValue(def.Id, out var p) ? p : DefaultOverlayState(def);

        var posCombo = new ComboBox
        {
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = current.Enabled,
        };
        foreach (var (value, label) in OverlayPositions)
            posCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        for (int i = 0; i < posCombo.Items.Count; i++)
        {
            if (posCombo.Items[i] is ComboBoxItem ci && (string)ci.Tag == current.Position)
            {
                posCombo.SelectedIndex = i;
                break;
            }
        }
        if (posCombo.SelectedIndex < 0) posCombo.SelectedIndex = 0;
        Grid.SetColumn(posCombo, 1);
        row.Children.Add(posCombo);

        var toggle = new ToggleSwitch
        {
            IsOn = current.Enabled,
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
        };
        Grid.SetColumn(toggle, 2);
        row.Children.Add(toggle);

        posCombo.SelectionChanged += (_, _) =>
        {
            if (posCombo.SelectedItem is ComboBoxItem sel)
            {
                var existing = _overlayPrefs.TryGetValue(def.Id, out var cur) ? cur : DefaultOverlayState(def);
                _overlayPrefs[def.Id] = (existing.Enabled, (string)sel.Tag);
                onChange();
            }
        };
        toggle.Toggled += (_, _) =>
        {
            var existing = _overlayPrefs.TryGetValue(def.Id, out var cur) ? cur : DefaultOverlayState(def);
            _overlayPrefs[def.Id] = (toggle.IsOn, existing.Position);
            posCombo.IsEnabled = toggle.IsOn;
            onChange();
        };

        return row;
    }

    private FrameworkElement BuildOverlayPreview()
    {
        // Outer container: 2:3 poster-shaped card with a dark gradient fill so
        // overlay badges have realistic contrast to sit against.
        var container = new Grid
        {
            Width = 140,
            Height = 210,
            CornerRadius = new CornerRadius(12),
        };

        // Poster-like gradient background (dark teal → near-black, mimicking an
        // actual poster backdrop), with a subtle inner border.
        var bg = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Offset = 0.0, Color = Color.FromArgb(0xFF, 0x1F, 0x29, 0x3A) },
                    new GradientStop { Offset = 0.5, Color = Color.FromArgb(0xFF, 0x14, 0x19, 0x24) },
                    new GradientStop { Offset = 1.0, Color = Color.FromArgb(0xFF, 0x09, 0x0B, 0x10) },
                },
            },
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };
        container.Children.Add(bg);

        // Centered film icon + "Preview" caption so it's clearly a mock poster.
        var centerStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };
        centerStack.Children.Add(new FontIcon
        {
            Glyph = "\uE714", // Video / filmstrip-ish
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0x9C, 0xA3, 0xAF)),
        });
        centerStack.Children.Add(new TextBlock
        {
            Text = "PREVIEW",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0x9C, 0xA3, 0xAF)),
        });
        container.Children.Add(centerStack);

        // Four corner hosts
        var corners = new Dictionary<string, StackPanel>
        {
            ["top-left"]     = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left,  VerticalAlignment = VerticalAlignment.Top,    Margin = new Thickness(6) },
            ["top-right"]    = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,    Margin = new Thickness(6) },
            ["bottom-left"]  = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left,  VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6) },
            ["bottom-right"] = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6) },
        };
        foreach (var panel in corners.Values) container.Children.Add(panel);

        foreach (var def in OverlayRegistry)
        {
            var cur = _overlayPrefs.TryGetValue(def.Id, out var p) ? p : DefaultOverlayState(def);
            if (!cur.Enabled) continue;
            if (ShouldSuppressStandaloneOverlay(def.Id)) continue;
            if (!corners.TryGetValue(cur.Position, out var host)) continue;

            host.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = def.Sample,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                },
            });
        }

        return container;
    }

    /// <summary>
    /// Parses the defaults.card_overlays setting value (JSON object) into a
    /// dictionary of id -> (enabled, position). Accepts the same shape as the
    /// webui serializer: { id: { enabled: bool, position: string } }.
    /// </summary>
    private static Dictionary<string, (bool Enabled, string Position)> ParseOverlayPrefs(string? json)
    {
        var result = new Dictionary<string, (bool, string)>();
        if (string.IsNullOrWhiteSpace(json))
        {
            foreach (var def in OverlayRegistry) result[def.Id] = DefaultOverlayState(def);
            return result;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var source = doc.RootElement;
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Object)
                source = items;

            foreach (var prop in source.EnumerateObject())
            {
                var def = OverlayRegistry.FirstOrDefault(d => d.Id == prop.Name);
                bool enabled = def?.DefaultEnabled ?? true;
                string position = def?.DefaultPosition ?? "top-left";
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("enabled", out var e))
                    {
                        if (e.ValueKind == System.Text.Json.JsonValueKind.True) enabled = true;
                        else if (e.ValueKind == System.Text.Json.JsonValueKind.False) enabled = false;
                    }
                    if (prop.Value.TryGetProperty("position", out var pos) && pos.ValueKind == System.Text.Json.JsonValueKind.String)
                        position = pos.GetString() ?? position;
                }
                result[prop.Name] = (enabled, position);
            }
        }
        catch
        {
            // Fall through — any id without an entry defaults to enabled/top-left below
        }
        foreach (var def in OverlayRegistry)
            if (!result.ContainsKey(def.Id)) result[def.Id] = DefaultOverlayState(def);
        return result;
    }

    private static string SerializeOverlayPrefs(Dictionary<string, (bool Enabled, string Position)> prefs)
    {
        var sb = new System.Text.StringBuilder("""{"version":2,"preset":"classic","order":[],"items":{""");
        bool first = true;
        foreach (var (id, (enabled, position)) in prefs)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(EscapeJsonString(id)).Append("\":{\"enabled\":");
            sb.Append(enabled ? "true" : "false");
            sb.Append(",\"position\":\"").Append(EscapeJsonString(position)).Append("\"}");
        }
        sb.Append("}}");
        return sb.ToString();
    }

    private static (bool Enabled, string Position) DefaultOverlayState(OverlayEditorDef def)
        => (def.DefaultEnabled, def.DefaultPosition);

    private bool ShouldSuppressStandaloneOverlay(string id)
    {
        if (id != "resolution" && id != "hdr") return false;
        return _overlayPrefs.TryGetValue("resolution_hdr", out var combined) && combined.Enabled;
    }

    // ===== Connection Check Helper =====

    /// <summary>
    /// Adds an inline "Check Connection" button that POSTs the current (including
    /// unsaved) settings to /admin/settings/check/{kind} and displays the result
    /// inline. Mirrors the webui ConnectionCheckAction component.
    /// </summary>
    private void AddConnectionCheckButton(StackPanel parent, string kind, string label)
    {
        AddDivider(parent);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 8, 0, 4),
        };

        var button = new Button
        {
            Content = label,
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(6),
            FontSize = 12,
        };

        var resultIcon = new FontIcon
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var result = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            Visibility = Visibility.Collapsed,
        };

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            var originalContent = button.Content;
            button.Content = "Checking...";
            result.Visibility = Visibility.Collapsed;

            try
            {
                var request = new Core.Models.Admin.AdminSettingsConnectionCheckRequest
                {
                    Values = ViewModel.GetEffectiveSettings(),
                    DirtyKeys = ViewModel.GetDirtyKeys(),
                };

                var adminApi = App.Services.GetRequiredService<AdminApi>();
                var response = await adminApi.CheckSettingsConnectionAsync(kind, request);

                result.Text = response.Message;
                var color = response.Success
                    ? Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)  // green-400
                    : Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73); // error red
                result.Foreground = new SolidColorBrush(color);
                result.Visibility = Visibility.Visible;
                resultIcon.Glyph = response.Success ? "\uE73E" : "\uE711"; // check / x
                resultIcon.Foreground = new SolidColorBrush(color);
                resultIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                result.Text = $"Check failed: {ex.Message}";
                var errColor = Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73);
                result.Foreground = new SolidColorBrush(errColor);
                result.Visibility = Visibility.Visible;
                resultIcon.Glyph = "\uE711";
                resultIcon.Foreground = new SolidColorBrush(errColor);
                resultIcon.Visibility = Visibility.Visible;
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        };

        row.Children.Add(button);
        row.Children.Add(resultIcon);
        row.Children.Add(result);
        parent.Children.Add(row);
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

    // Section headers are rendered INSIDE the next card as an UPPERCASE
    // tracked-wide label — matches the webui FieldGroup component layout.
    // AddSectionHeader stashes the label; BeginCard picks it up and uses it
    // as the first child of the card stack panel.
    private string? _pendingSectionHeader;

    private void AddSectionHeader(string text)
    {
        _pendingSectionHeader = text;
    }

    private StackPanel BeginCard()
    {
        var panel = new StackPanel { Spacing = 0 };
        if (!string.IsNullOrEmpty(_pendingSectionHeader))
        {
            panel.Children.Add(new TextBlock
            {
                Text = _pendingSectionHeader.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 220,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                Margin = new Thickness(0, 0, 0, 10),
            });
            _pendingSectionHeader = null;
        }
        return panel;
    }

    private void EndCard(StackPanel cardContent)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 18, 20, 18),
            Child = cardContent,
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

    private string GetSettingValue(string key, string? fallbackKey = null)
    {
        var value = ViewModel.GetSetting(key);
        return string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(fallbackKey)
            ? ViewModel.GetSetting(fallbackKey)
            : value;
    }

    private TextBox AddTextField(StackPanel parent, string label, string key, string? hint = null, string? fallbackKey = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var textBox = new TextBox
        {
            Text = GetSettingValue(key, fallbackKey),
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            Width = 448,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = hint ?? ""
        };
        textBox.TextChanged += (s, e) =>
        {
            ViewModel.SetSetting(key, textBox.Text);
            UpdateDirtyCountText();
        };
        field.Children.Add(textBox);

        _fieldRebuilders.Add(() => textBox.Text = GetSettingValue(key, fallbackKey));

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        parent.Children.Add(field);
        return textBox;
    }

    private void AddPasswordField(StackPanel parent, string label, string key, string? hint = null, string? fallbackKey = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        bool isConfigured = ViewModel.IsSensitiveConfigured(key)
            || !string.IsNullOrWhiteSpace(fallbackKey) && ViewModel.IsSensitiveConfigured(fallbackKey);

        var passwordBox = new PasswordBox
        {
            Password = "",
            Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"],
            Width = 448,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = isConfigured ? "\u2022\u2022\u2022\u2022 configured" : (hint ?? "Not configured"),
            PasswordRevealMode = PasswordRevealMode.Hidden,
        };
        bool isRevealed = false;

        var eyeBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uED1A", FontSize = 14 }, // EyeOff
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(eyeBtn, "Show/hide value");
        eyeBtn.Click += (_, _) =>
        {
            isRevealed = !isRevealed;
            passwordBox.PasswordRevealMode = isRevealed ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            if (isRevealed)
            {
                // Show value; the PasswordBox handles masking, this just swaps the icon.
                ((FontIcon)eyeBtn.Content).Glyph = "\uE7B3"; // Eye
            }
            else
            {
                ((FontIcon)eyeBtn.Content).Glyph = "\uED1A"; // EyeOff
            }
        };

        var inputRow = new Grid { ColumnSpacing = 4 };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(passwordBox, 0);
        Grid.SetColumn(eyeBtn, 1);
        inputRow.Children.Add(passwordBox);
        inputRow.Children.Add(eyeBtn);

        passwordBox.PasswordChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(passwordBox.Password))
                ViewModel.SetSetting(key, passwordBox.Password);
            UpdateDirtyCountText();
        };
        field.Children.Add(inputRow);

        _fieldRebuilders.Add(() => { /* Don't refill passwords on discard */ });

        if (hint != null)
        {
            field.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        parent.Children.Add(field);
    }

    private void AddNumberField(StackPanel parent, string label, string key, string? hint = null, string? fallbackKey = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentVal = GetSettingValue(key, fallbackKey);
        double.TryParse(currentVal, out var numVal);

        var numberBox = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Value = string.IsNullOrEmpty(currentVal) ? double.NaN : numVal,
            SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact,
            Minimum = 0,
            Width = 160,
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
            var v = GetSettingValue(key, fallbackKey);
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
                MaxWidth = 448
            });
        }

        parent.Children.Add(field);
    }

    private ToggleSwitch AddToggleField(StackPanel parent, string label, string key, string? hint = null,
        bool defaultValue = false, Action<bool>? onChanged = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
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
            IsOn = string.IsNullOrWhiteSpace(currentVal)
                ? defaultValue
                : currentVal.Equals("true", StringComparison.OrdinalIgnoreCase),
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (s, e) =>
        {
            ViewModel.SetSetting(key, toggle.IsOn ? "true" : "false");
            onChanged?.Invoke(toggle.IsOn);
            UpdateDirtyCountText();
        };

        _fieldRebuilders.Add(() =>
        {
            var v = ViewModel.GetSetting(key);
            toggle.IsOn = string.IsNullOrWhiteSpace(v)
                ? defaultValue
                : v.Equals("true", StringComparison.OrdinalIgnoreCase);
        });

        Grid.SetColumn(labelStack, 0);
        Grid.SetColumn(toggle, 1);
        field.Children.Add(labelStack);
        field.Children.Add(toggle);

        parent.Children.Add(field);
        return toggle;
    }

    private void AddDurationField(StackPanel parent, string label, string key, string? hint = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var textBox = new TextBox
        {
            Text = ViewModel.GetSetting(key),
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            Width = 448,
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
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        parent.Children.Add(field);
    }

    /// <summary>Overload with explicit (value, label) pairs for human-friendly display.</summary>
    private void AddSelectField(StackPanel parent, string label, string key, (string Value, string Label)[] options, string? hint = null, Action<string>? onChanged = null)
    {
        AddSelectField(parent, label, key, options.Select(o => o.Value).ToArray(), hint,
            options.ToDictionary(o => o.Value, o => o.Label), onChanged);
    }

    private void AddSelectField(StackPanel parent, string label, string key, string[] options, string? hint = null, Dictionary<string, string>? labelMap = null, Action<string>? onChanged = null)
    {
        if (parent.Children.Count > 0) AddDivider(parent);

        var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };

        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var comboBox = new ComboBox
        {
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        foreach (var option in options)
        {
            var displayLabel = (labelMap != null && labelMap.TryGetValue(option, out var mapped))
                ? mapped
                : option.Substring(0, 1).ToUpper() + option.Substring(1);
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = displayLabel,
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
                var nextValue = selected.Tag?.ToString() ?? "";
                ViewModel.SetSetting(key, nextValue);
                UpdateDirtyCountText();
                onChanged?.Invoke(nextValue);
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
                MaxWidth = 448
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
            Width = 160,
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
                MaxWidth = 448
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

    // ===== Theme preview =====

    private StackPanel? _themePreviewHost;

    private void RefreshThemePreview()
    {
        if (_themePreviewHost == null) return;
        _themePreviewHost.Children.Clear();

        Dictionary<string, string> previewVars;
        try { previewVars = JsonSerializer.Deserialize<Dictionary<string, string>>(ViewModel.GetSetting("ui.admin_theme_vars")) ?? []; }
        catch { previewVars = []; }
        Brush PreviewBrush(string token, string fallbackResource)
            => previewVars.TryGetValue(token, out var value) && IsHexThemeColor(value)
                ? new SolidColorBrush(ParseHexColor(value))
                : (Brush)Application.Current.Resources[fallbackResource];

        // Build the same structural preview while applying every native-parsable
        // color immediately; CSS-only color forms remain visible after server save.
        var card = new Border
        {
            Background = PreviewBrush("card", "CardBackgroundBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            BorderBrush = PreviewBrush("border", "BorderBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var cardContent = new StackPanel { Spacing = 8 };
        cardContent.Children.Add(new TextBlock
        {
            Text = "Sample Card",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = PreviewBrush("foreground", "PrimaryTextBrush"),
        });
        cardContent.Children.Add(new TextBlock
        {
            Text = "This shows how your theme tokens affect the UI.",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        // Accent-colored pill
        var pill = new Border
        {
            Background = PreviewBrush("primary", "AccentBackgroundBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        pill.Child = new TextBlock
        {
            Text = "Accent",
            FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = PreviewBrush("primary-foreground", "AccentBrush"),
        };
        cardContent.Children.Add(pill);
        // Destructive pill
        var destructivePill = new Border
        {
            Background = PreviewBrush("destructive", "SurfaceHoverBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        destructivePill.Child = new TextBlock
        {
            Text = "Destructive",
            FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xDC, 0x5A, 0x5A)),
        };
        cardContent.Children.Add(destructivePill);
        card.Child = cardContent;
        _themePreviewHost.Children.Add(card);

        // Note: this preview uses the CURRENT app theme resources, not the
        // tokens being edited (those are CSS vars the server applies). A true
        // live preview would require parsing oklch/hsl values into WinUI
        // Colors, which is complex. This gives a structural preview.
    }

    // ===== Theme validation =====

    /// <summary>
    /// Basic sanitization of theme vars JSON. Returns a warning string if
    /// invalid, or empty string if OK. Checks:
    ///   1. Valid JSON object
    ///   2. All keys start with "--"
    ///   3. No values contain "{", "}", "url(", "expression(", or "@import" (CSS injection vectors)
    /// </summary>
    private static string ValidateThemeVarsJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return "Must be a JSON object (e.g. {\"--accent\": \"#60A5FA\"}).";

            var warnings = new List<string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.StartsWith("--"))
                    warnings.Add($"Key \"{prop.Name}\" should start with \"--\".");

                var val = prop.Value.GetString() ?? "";
                var lower = val.ToLowerInvariant();
                if (lower.Contains('{') || lower.Contains('}') || lower.Contains("url(")
                    || lower.Contains("expression(") || lower.Contains("@import"))
                    warnings.Add($"Value for \"{prop.Name}\" contains potentially unsafe CSS.");
            }
            return string.Join(" ", warnings);
        }
        catch (System.Text.Json.JsonException)
        {
            return "Invalid JSON. Check syntax.";
        }
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
        var clickedButton = sender as Button ?? SaveButton;
        var originalContent = clickedButton.Content;

        // Show "Saving..." on the button while save is in flight
        clickedButton.Content = "Saving...";
        clickedButton.IsEnabled = false;

        try
        {
            if (!ViewModel.IsSaving && ViewModel.HasDirtyChanges)
            {
                await ViewModel.SaveCommand.ExecuteAsync(null);
            }
            else
            {
                while (ViewModel.IsSaving)
                    await Task.Delay(50);
            }

            if (ViewModel.StatusMessage != null)
            {
                ShowTab(_activeTab);
                ShowStatusToast(ViewModel.StatusMessage);
            }
            else if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
            {
                ShowStatusToast(ViewModel.ErrorMessage, isError: true);
            }
        }
        finally
        {
            clickedButton.Content = originalContent;
            clickedButton.IsEnabled = true;
            SaveButton.Content = "Save Changes";
            SaveButton.IsEnabled = true;
            UpdateDirtyCountText();
        }

        if (ViewModel.LastSaveRequiresRestart)
        {
            RestartServerButton.Visibility = Visibility.Visible;
            RestartHintText.Visibility = Visibility.Visible;
        }
    }

    private async void RestartServer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Restart server?",
            Content = "The server will restart to apply configuration changes. Active streams will be interrupted.",
            PrimaryButtonText = "Restart",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var adminApi = App.Services.GetRequiredService<AdminApi>();
            var response = await adminApi.RestartServerAsync();
            ShowStatusToast(string.IsNullOrWhiteSpace(response.Message) ? "Server is restarting..." : response.Message);
            ViewModel.LastSaveRequiresRestart = false;
            RestartServerButton.Visibility = Visibility.Collapsed;
            RestartHintText.Visibility = Visibility.Collapsed;
            if (_inlineRestartButton is not null) _inlineRestartButton.Visibility = Visibility.Collapsed;
            if (_inlineRestartNotice is not null) _inlineRestartNotice.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ShowStatusToast("Could not restart server. Please restart manually.", isError: true);
        }
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasDirtyChanges && ViewModel.DiscardCommand.CanExecute(null))
            ViewModel.DiscardCommand.Execute(null);

        // Rebuild the current tab to reset all field values
        ShowTab(_activeTab);
        UpdateDirtyCountText();
    }

    private void ShowStatusToast(string message, bool? isError = null)
    {
        var error = isError ?? (message.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || message.Contains("must ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("between ", StringComparison.OrdinalIgnoreCase));
        if (error) _toastService.Error(message);
        else _toastService.Success(message);
    }
}
