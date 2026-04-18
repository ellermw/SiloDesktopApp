using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminSettingsDetailPage : Page
{
    public AdminSettingsDetailViewModel ViewModel { get; }

    // Static so the active tab persists across page navigations
    private static string _persistedTab = "General";
    private string _activeTab = _persistedTab;
    private Button? _activeTabButton;
    private readonly List<(Button Button, string TabName)> _tabButtons = [];

    // Lookup for rebuilding fields after discard
    private readonly List<Action> _fieldRebuilders = [];

    // Settings sub-nav — matches web/src/pages/admin-settings/AdminSettingsLayout.tsx
    // order and labeling. Each item is (label, Segoe Fluent icon glyph).
    private static readonly (string Label, string Glyph)[] SettingsTabs =
    [
        ("General",          "\uE713"), // Settings
        ("Theming",          "\uE790"), // Brush
        ("Playback",         "\uE768"), // Play
        ("Scanner & Matcher","\uE721"), // Zoom/Find
        ("Rate Limiting",    "\uE9D9"), // Gauge/Speed
        ("Downloads",        "\uE896"), // Download
        ("Integrations",     "\uEA86"), // Puzzle
        ("Jellyfin Compat",  "\uE7F4"), // TVMonitor
        ("Database",         "\uEBD2"), // Database/Drive
        ("Storage",          "\uEDA2"), // HardDrive
        ("Log Retention",    "\uE81C"), // Document
        ("Card Overlays",    "\uE81E"), // Layers/stack
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

    // ===== Sidebar Nav =====

    private void BuildTabBar()
    {
        TabBar.Children.Clear();
        _tabButtons.Clear();

        foreach (var (label, glyph) in SettingsTabs)
        {
            var btn = BuildSidebarNavButton(label, glyph);
            btn.Click += TabButton_Click;
            _tabButtons.Add((btn, label));
            TabBar.Children.Add(btn);
        }

        // Activate first tab
        if (_tabButtons.Count > 0)
        {
            SetActiveTab(_tabButtons[0].Button, _tabButtons[0].TabName);
        }
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
            FontSize = 16,
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
            Margin = new Thickness(-8, 0, 4, 0), // Pull left into padding
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
            Padding = new Thickness(14, 9, 12, 9),
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
        ContentPanel.Children.Clear();
        _fieldRebuilders.Clear();
        // Reset per-tab layout overrides
        ContentPanel.MaxWidth = double.PositiveInfinity;
        ContentPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

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
            case "Card Overlays": BuildOverlaysTab(); break;
        }

        // Webui renders save/discard inline at the bottom of each tab's content
        // (inside the scrollable area), not as a fixed bottom strip.
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
            Visibility = ViewModel.HasDirtyChanges ? Visibility.Visible : Visibility.Collapsed,
            Tag = "inlineSaveBar",
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusText = new TextBlock
        {
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };

        var discardBtn = new Button
        {
            Content = "Discard",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
        };
        discardBtn.Click += DiscardButton_Click;
        Grid.SetColumn(discardBtn, 1);

        var saveBtn = new Button
        {
            Content = "Save Changes",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        saveBtn.Click += SaveButton_Click;
        Grid.SetColumn(saveBtn, 2);

        Grid.SetColumn(statusText, 0);
        grid.Children.Add(statusText);
        grid.Children.Add(discardBtn);
        grid.Children.Add(saveBtn);
        bar.Child = grid;

        // Register for dirty-state changes to update visibility and text
        void UpdateBar()
        {
            bar.Visibility = ViewModel.HasDirtyChanges ? Visibility.Visible : Visibility.Collapsed;
            var count = ViewModel.DirtyCount;
            statusText.Text = count > 0
                ? $"{count} unsaved change{(count != 1 ? "s" : "")}"
                : "";
        }
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.HasDirtyChanges) or nameof(ViewModel.DirtyCount))
                DispatcherQueue.TryEnqueue(UpdateBar);
        };
        UpdateBar();

        ContentPanel.Children.Add(bar);
    }

    // ===== Tab Builders =====

    private void BuildThemingTab()
    {
        AddTabHeader("Theming", "Customize server branding, catalog themes, and login page appearance.");

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

        AddSectionHeader("Branding");
        var brandCard = BeginCard();
        AddTextBlock(brandCard, "Customize the server name and login page text. Leave blank for defaults.");
        AddTextField(brandCard, "Server Name", "branding.server_name", "Continuum");
        AddTextField(brandCard, "Login Subtitle", "branding.login_subtitle", "Sign in with an existing account.");
        EndCard(brandCard);

        AddSectionHeader("Theme Catalog");
        var catalogCard = BeginCard();
        AddTextBlock(catalogCard, "URL of the community theme catalog JSON index. Users browse this in their settings.");
        AddTextField(catalogCard, "Theme Catalog URL", "theme.catalog_url",
            "https://raw.githubusercontent.com/ContinuumApp/continuum-themes/main/catalog.json");
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
        AddSelectField(tcCard, "Hardware Acceleration", "playback.hw_accel",
            [("auto", "Auto (Recommended)"), ("qsv", "Intel Quick Sync (QSV)"), ("vaapi", "VA-API"), ("nvenc", "NVIDIA NVENC"), ("none", "None (CPU only)")]);

        // HW-accel resolved indicator (webui: green/amber dot + resolved method + device)
        if (ViewModel.GetSetting("playback.hw_accel") is "auto" or "" or null)
        {
            var hwInfoPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, -4, 0, 8) };
            hwInfoPanel.Children.Add(new TextBlock
            {
                Text = "Detecting...",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            });
            tcCard.Children.Add(hwInfoPanel);

            // Async fetch hw-accel info
            _ = Task.Run(async () =>
            {
                try
                {
                    var adminApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.AdminApi>();
                    var info = await adminApi.GetHWAccelInfoAsync();
                    var resolved = "none";
                    if (info.TryGetValue("resolved", out var r) && r is System.Text.Json.JsonElement re && re.ValueKind == System.Text.Json.JsonValueKind.String)
                        resolved = re.GetString() ?? "none";
                    string? device = null;
                    if (info.TryGetValue("render_devices", out var rd) && rd is System.Text.Json.JsonElement rde && rde.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var first = rde.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == System.Text.Json.JsonValueKind.String)
                            device = first.GetString();
                    }
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        hwInfoPanel.Children.Clear();
                        bool isHealthy = resolved != "none";
                        hwInfoPanel.Children.Add(new Border
                        {
                            Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(isHealthy
                                ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
                                : Windows.UI.Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                        var label = resolved switch
                        {
                            "vaapi" => "VA-API",
                            "qsv" => "Intel Quick Sync",
                            "nvenc" => "NVIDIA NVENC",
                            "none" => "No acceleration available",
                            _ => resolved,
                        };
                        if (device != null) label += $" -- {device}";
                        hwInfoPanel.Children.Add(new TextBlock
                        {
                            Text = label,
                            FontSize = 11,
                            Foreground = (SolidColorBrush)Application.Current.Resources[isHealthy ? "PrimaryTextBrush" : "SecondaryTextBrush"],
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                    });
                }
                catch
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        hwInfoPanel.Children.Clear();
                        hwInfoPanel.Children.Add(new TextBlock
                        {
                            Text = "Could not detect hardware acceleration",
                            FontSize = 11,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                        });
                    });
                }
            });
        }

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
        AddNumberField(segCard, "Chapter Thumbnail Workers", "playback.chapter_thumbnail_workers",
            "Global chapter thumbnail dispatcher concurrency. Higher values improve throughput but can drive more local or remote extraction work at once.");
        AddSelectField(segCard, "Chapter Thumbnail Execution", "playback.chapter_thumbnail_execution",
            ["local", "prefer_transcode_nodes", "transcode_nodes_only"],
            "Controls whether chapter thumbnails run on the API node or are offloaded to available transcode nodes.");
        AddNumberField(segCard, "Chapter Thumbnail Node Capacity", "playback.chapter_thumbnail_node_capacity",
            "Per transcode-node budget for chapter thumbnail jobs when remote execution is enabled.");
        AddSelectField(segCard, "HDR Chapter Thumbnail Policy", "playback.chapter_thumbnail_hdr_policy",
            ["best_effort", "disabled"],
            "Controls whether chapter thumbnails are generated for HDR or Dolby Vision sources. SDR files are unaffected.");
        EndCard(segCard);

        AddSectionHeader("Behavior");
        var behCard = BeginCard();
        AddNumberField(behCard, "Watched Threshold (%)", "playback.watched_threshold",
            "Mark as watched after this % is played (default: 90)");
        AddNumberField(behCard, "Min Resume Threshold (%)", "playback.min_resume_threshold",
            "Ignore progress below this % of duration (default: 5)");
        EndCard(behCard);
    }

    private void BuildScannerTab()
    {
        AddTabHeader("Scanner & Matcher",
            "Configure scanner performance and metadata matching. Startup and recurring scans are managed in Scheduled Tasks.");

        AddSectionHeader("Scanner");
        var scanCard = BeginCard();
        AddNumberField(scanCard, "Scanner Workers", "scanner.workers");
        AddDurationField(scanCard, "File Removal Grace", "scanner.file_removal_grace", "e.g. 24h");
        EndCard(scanCard);

        AddSectionHeader("Matcher");
        var matchCard = BeginCard();
        AddNumberField(matchCard, "Matcher Workers", "matcher.workers");
        AddNumberField(matchCard, "Matcher Batch Size", "matcher.batch_size");
        EndCard(matchCard);

        AddSectionHeader("Metadata");
        var metaCard = BeginCard();
        AddToggleField(metaCard, "Cache Images to S3", "metadata.cache_images",
            "When enabled, artwork fetched from metadata providers is resized and cached to your S3 storage bucket.");
        EndCard(metaCard);
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
        AddTabHeader("Integrations", "Subtitle providers");

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
            testBtn.IsEnabled = false;
            testBtn.Content = "Testing...";
            resultText.Visibility = Visibility.Collapsed;
            try
            {
                var r = await _subsVm.TestProviderAsync(capturedProvider.ProviderName);
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
                await _subsVm.UpdateProviderAsync(capturedProvider.ProviderName, req);
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
            MaxWidth = 448,
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
        AddTabHeader("Storage", "S3-compatible object storage for artwork, imports/exports, and future replicated data.");

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
        AddTextBlock(pubCard, "Stores client-facing assets: artwork, chapter thumbnails, and subtitle files.");
        AddTextField(pubCard, "Endpoint", "s3.public_endpoint");
        AddTextField(pubCard, "Region", "s3.public_region");
        AddToggleField(pubCard, "Path Style", "s3.public_path_style");
        AddTextField(pubCard, "Bucket", "s3.public_bucket");
        AddTextField(pubCard, "Key Prefix", "s3.public_key_prefix",
            "Optional. Stores all objects under this folder inside the bucket. Leave blank for bucket root.");
        AddPasswordField(pubCard, "Access Key", "s3.public_access_key");
        AddPasswordField(pubCard, "Secret Key", "s3.public_secret_key");
        AddConnectionCheckButton(pubCard, "s3_public", "Check Connection");
        publicContainer.Children.Add(WrapInCard(pubCard));

        // Public URL Authentication
        var urlAuthCard = new StackPanel { Spacing = 0 };
        urlAuthCard.Children.Add(new TextBlock
        {
            Text = "ASSET URL AUTHENTICATION",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 10),
        });
        AddTextBlock(urlAuthCard,
            "Controls how read URLs are generated for cached images. Use Cloudflare Token for R2 custom domains.");
        AddS3UrlAuthFields(urlAuthCard);
        publicContainer.Children.Add(WrapInCard(urlAuthCard));

        ContentPanel.Children.Add(publicContainer);

        // ===== Private Internal container =====
        var privateContainer = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };

        var privCard = new StackPanel { Spacing = 0 };
        AddTextBlock(privCard, "Stores non-public Continuum objects: imports, exports, and internal artifacts.");
        AddTextField(privCard, "Endpoint", "s3.private_endpoint");
        AddTextField(privCard, "Region", "s3.private_region");
        AddToggleField(privCard, "Path Style", "s3.private_path_style");
        AddTextField(privCard, "Bucket", "s3.private_bucket");
        AddTextField(privCard, "Key Prefix", "s3.private_key_prefix",
            "Optional. Stores all private objects under this folder inside the bucket.");
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

    // Registry mirrors web/src/lib/cardOverlays.ts OVERLAY_REGISTRY.
    // Each entry: (id, label, description, sample badge text).
    private static readonly (string Id, string Label, string Description, string Sample)[] OverlayRegistry =
    [
        ("resolution",       "Resolution",       "Show video resolution (e.g. 2160P, 1080P).",                 "2160P"),
        ("hdr",              "HDR",              "Show HDR / Dolby Vision badge.",                              "DV HDR10"),
        ("audio",            "Audio",            "Show Atmos / DTS:X / TrueHD when present.",                  "Atmos"),
        ("release_type",     "Release Type",     "Show REMUX / WEB-DL / Blu-ray-style release tag.",           "REMUX"),
        ("rating_imdb",      "IMDb Rating",      "Show IMDb audience rating.",                                  "8.7"),
        ("rating_tmdb",      "TMDb Rating",      "Show TMDb audience rating.",                                  "8.5"),
        ("rating_rt",        "Rotten Tomatoes",  "Show Rotten Tomatoes critic score.",                          "96%"),
        ("rating_rt_audience","RT Audience",     "Show Rotten Tomatoes audience score.",                        "92%"),
        ("original_language","Language",         "Show the original language code (e.g. EN, FR).",              "EN"),
        ("edition",          "Edition",          "Show the edition tag (e.g. Standard, Theatrical, Extended).", "Standard"),
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
            "Configure the default overlay badges shown on poster cards. Users can override these in their personal settings.");

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

    private FrameworkElement BuildOverlayDefRow((string Id, string Label, string Description, string Sample) def, Action onChange)
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

        var current = _overlayPrefs.TryGetValue(def.Id, out var p) ? p : (Enabled: true, Position: "top-left");

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
                var existing = _overlayPrefs.TryGetValue(def.Id, out var cur) ? cur : (Enabled: true, Position: "top-left");
                _overlayPrefs[def.Id] = (existing.Enabled, (string)sel.Tag);
                onChange();
            }
        };
        toggle.Toggled += (_, _) =>
        {
            var existing = _overlayPrefs.TryGetValue(def.Id, out var cur) ? cur : (Enabled: true, Position: "top-left");
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
            var cur = _overlayPrefs.TryGetValue(def.Id, out var p) ? p : (Enabled: true, Position: "top-left");
            if (!cur.Enabled) continue;
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
            // Default: everything enabled at top-left
            foreach (var def in OverlayRegistry) result[def.Id] = (true, "top-left");
            return result;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                bool enabled = true;
                string position = "top-left";
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("enabled", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.False) enabled = false;
                    if (prop.Value.TryGetProperty("position", out var pos) && pos.ValueKind == System.Text.Json.JsonValueKind.String)
                        position = pos.GetString() ?? "top-left";
                }
                result[prop.Name] = (enabled, position);
            }
        }
        catch
        {
            // Fall through — any id without an entry defaults to enabled/top-left below
        }
        foreach (var def in OverlayRegistry)
            if (!result.ContainsKey(def.Id)) result[def.Id] = (true, "top-left");
        return result;
    }

    private static string SerializeOverlayPrefs(Dictionary<string, (bool Enabled, string Position)> prefs)
    {
        var sb = new System.Text.StringBuilder("{");
        bool first = true;
        foreach (var (id, (enabled, position)) in prefs)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(id).Append("\":{\"enabled\":");
            sb.Append(enabled ? "true" : "false");
            sb.Append(",\"position\":\"").Append(position).Append("\"}");
        }
        sb.Append('}');
        return sb.ToString();
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
            FontSize = 18,
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
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
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

    private void AddTextField(StackPanel parent, string label, string key, string? hint = null)
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
            MaxWidth = 448,
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
                FontSize = 12,
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
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var currentVal = ViewModel.GetSetting(key);
        bool isConfigured = ViewModel.IsSensitiveConfigured(key);

        // Webui uses explicit Eye/EyeOff toggle, not native PasswordBox reveal.
        // We use a TextBox (masked via FontFamily trick) + toggle button.
        var inputBox = new TextBox
        {
            Text = "",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            MaxWidth = 448,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = isConfigured ? "\u2022\u2022\u2022\u2022 configured" : (hint ?? "Not configured"),
            FontFamily = new FontFamily("Consolas"), // monospace for secrets
        };
        // Start masked
        bool isRevealed = false;
        var originalFont = inputBox.FontFamily;

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
            if (isRevealed)
            {
                // Show value — already in TextBox, just change icon
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
        Grid.SetColumn(inputBox, 0);
        Grid.SetColumn(eyeBtn, 1);
        inputRow.Children.Add(inputBox);
        inputRow.Children.Add(eyeBtn);

        // Alias for backward compat with the rest of this method
        var passwordBox = inputBox;

        passwordBox.TextChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(passwordBox.Text))
                ViewModel.SetSetting(key, passwordBox.Text);
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

    private void AddNumberField(StackPanel parent, string label, string key, string? hint = null)
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

        var currentVal = ViewModel.GetSetting(key);
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
                MaxWidth = 448
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
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        var textBox = new TextBox
        {
            Text = ViewModel.GetSetting(key),
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            MaxWidth = 448,
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
    private void AddSelectField(StackPanel parent, string label, string key, (string Value, string Label)[] options, string? hint = null)
    {
        AddSelectField(parent, label, key, options.Select(o => o.Value).ToArray(), hint,
            options.ToDictionary(o => o.Value, o => o.Label));
    }

    private void AddSelectField(StackPanel parent, string label, string key, string[] options, string? hint = null, Dictionary<string, string>? labelMap = null)
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
        // Remove old preview card (keep the "PREVIEW" header)
        while (_themePreviewHost.Children.Count > 1)
            _themePreviewHost.Children.RemoveAt(1);

        // Build a small sample card showing accent color + text on background
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Width = 160,
        };
        var cardContent = new StackPanel { Spacing = 8 };
        cardContent.Children.Add(new TextBlock
        {
            Text = "Sample Card",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
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
            Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        pill.Child = new TextBlock
        {
            Text = "Accent",
            FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
        };
        cardContent.Children.Add(pill);
        // Destructive pill
        var destructivePill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xDC, 0x5A, 0x5A)),
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

    // Keys whose change requires a server restart to take effect.
    private static readonly HashSet<string> RestartRequiredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "redis.url", "redis.sentinel_master", "redis.sentinel_addrs",
        "database.url", "database.max_connections",
        "server.mode", "server.bind_address", "server.port",
    };

    private bool _restartRequired;

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if any dirty keys require restart BEFORE saving (dirty set clears after save).
        bool needsRestart = ViewModel.GetDirtyKeys().Any(k => RestartRequiredKeys.Contains(k));

        // Show "Saving..." on the button while save is in flight
        SaveButton.Content = "Saving...";
        SaveButton.IsEnabled = false;

        // The command binding handles the save; after save, refresh the tab
        await Task.Delay(100); // small delay to let binding update
        if (ViewModel.StatusMessage != null)
        {
            ShowTab(_activeTab);
            ShowStatusToast(ViewModel.StatusMessage);
        }
        SaveButton.Content = "Save Changes";
        SaveButton.IsEnabled = true;
        UpdateDirtyCountText();

        if (needsRestart)
        {
            _restartRequired = true;
            RestartServerButton.Visibility = Visibility.Visible;
            RestartHintText.Visibility = Visibility.Visible;
        }
    }

    private async void RestartServer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Restart required",
            Content = "Some settings require a server restart to take effect. Active streams will be interrupted. Please restart the server process manually.",
            PrimaryButtonText = "OK",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Primary,
        };
        await dialog.ShowAsync();
        _restartRequired = false;
        RestartServerButton.Visibility = Visibility.Collapsed;
        RestartHintText.Visibility = Visibility.Collapsed;
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
