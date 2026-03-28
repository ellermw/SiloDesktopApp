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
        "Playback",
        "Scanner & Matcher",
        "Rate Limiting",
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
            case "Playback": BuildPlaybackTab(); break;
            case "Scanner & Matcher": BuildScannerTab(); break;
            case "Rate Limiting": BuildRateLimitTab(); break;
            case "Integrations": BuildIntegrationsTab(); break;
            case "Jellyfin Compat": BuildJellyfinTab(); break;
            case "Database": BuildDatabaseTab(); break;
            case "Storage": BuildStorageTab(); break;
            case "Log Retention": BuildLogRetentionTab(); break;
        }
    }

    // ===== Tab Builders =====

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
        AddToggleField(tcCard, "Enable Transcode Throttling", "enable_transcode_throttle");
        AddNumberField(tcCard, "Throttle Buffer (seconds)", "transcode_throttle_seconds", "How many seconds ahead FFmpeg transcodes before pausing. Minimum: 60.");
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

        AddSectionHeader("Global");
        var globalCard = BeginCard();
        AddToggleField(globalCard, "Enable Rate Limiting", "rate_limit.enabled", "When disabled, no rate limits are enforced.");
        AddSelectField(globalCard, "Backend", "rate_limit.backend", ["memory", "redis"], "Requires restart. Redis recommended for multi-instance deployments.");
        AddNumberField(globalCard, "Global Requests Per Second", "rate_limit.global_rps", "Maximum requests per second across all clients combined.");
        EndCard(globalCard);

        AddSectionHeader("Per-IP Limits");
        var ipCard = BeginCard();
        AddNumberField(ipCard, "Requests / Second", "rate_limit.per_ip.rps");
        AddNumberField(ipCard, "Requests / Minute", "rate_limit.per_ip.rpm");
        AddNumberField(ipCard, "Burst", "rate_limit.per_ip.burst");
        EndCard(ipCard);
    }

    private void BuildIntegrationsTab()
    {
        AddTabHeader("Integrations", "External services and API keys for metadata providers.");

        AddSectionHeader("MetaDB");
        var metaCard = BeginCard();
        AddTextField(metaCard, "MetaDB URL", "metadb.url");
        AddPasswordField(metaCard, "MetaDB API Key", "metadb.api_key");
        EndCard(metaCard);

        AddSectionHeader("TMDB");
        var tmdbCard = BeginCard();
        AddPasswordField(tmdbCard, "TMDB API Key", "tmdb.api_key", "Shared by TMDB metadata providers and TMDB collection/trending features.");
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
        AddPasswordField(redisCard, "Connection URL", "redis.url", "redis://host:6379");
        EndCard(redisCard);

        AddSectionHeader("User Database");
        var udbCard = BeginCard();
        AddTextField(udbCard, "User DB Backend", "userdb.backend", "postgres or sqlite");
        AddNumberField(udbCard, "Pool Max Open", "userdb.pool_max_open");
        AddDurationField(udbCard, "Idle Timeout", "userdb.idle_timeout", "e.g. 12h");
        EndCard(udbCard);
    }

    private void BuildStorageTab()
    {
        AddTabHeader("Storage", "S3-compatible object storage for artwork, operational exports, and future replicated data.");

        AddSectionHeader("MetaDB Posters (S3)");
        var metaCard = BeginCard();
        AddTextField(metaCard, "Endpoint", "s3.metadata_endpoint");
        AddTextField(metaCard, "Region", "s3.metadata_region");
        AddToggleField(metaCard, "Path Style", "s3.metadata_path_style");
        AddTextField(metaCard, "Bucket", "s3.metadata_bucket");
        AddPasswordField(metaCard, "Access Key", "s3.metadata_access_key");
        AddPasswordField(metaCard, "Secret Key", "s3.metadata_secret_key");
        AddDurationField(metaCard, "Presign Expiry", "s3.metadata_presign_expiry", "e.g. 4h");
        EndCard(metaCard);

        AddSectionHeader("General Purpose (S3)");
        var opCard = BeginCard();
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
        AddTabHeader("Log Retention", "Prune oldest operational logs by global caps and per-bucket overrides.");

        AddSectionHeader("Global Limits");
        var globalCard = BeginCard();
        AddNumberField(globalCard, "Retention Days", "opslog_retention_days", "Logs older than this are pruned first.");
        AddNumberField(globalCard, "Cleanup Interval (Minutes)", "opslog_cleanup_interval", "How often the retention worker checks caps and prunes oldest rows.");
        AddNumberField(globalCard, "Max Rows", "opslog_max_rows", "Keeps only the newest rows once this total is exceeded.");
        AddNumberField(globalCard, "Max Size (MB)", "opslog_max_size_mb", "Uses estimated log row size. Oldest rows are pruned when the budget is exceeded.");
        EndCard(globalCard);
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
            Text = text.ToUpperInvariant(),
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
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(22),
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
        var passwordBox = new PasswordBox
        {
            Password = currentVal,
            Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"],
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = string.IsNullOrEmpty(currentVal) ? (hint ?? "Not configured") : "configured"
        };
        passwordBox.PasswordChanged += (s, e) =>
        {
            ViewModel.SetSetting(key, passwordBox.Password);
            UpdateDirtyCountText();
        };
        field.Children.Add(passwordBox);

        _fieldRebuilders.Add(() => passwordBox.Password = ViewModel.GetSetting(key));

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
