using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRecommendationsPage : Page
{
    public AdminRecommendationsViewModel ViewModel { get; }

    private DispatcherTimer? _pollTimer;

    public AdminRecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminRecommendationsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        ApplyStatus(ViewModel.Status);
        UpdatePollTimer();
        RebuildSettingsSections();
    }

    // ===== Status rendering =====

    private void ApplyStatus(RecommendationsStatus? status)
    {
        if (status is null)
        {
            JobStatusSection.Visibility = Visibility.Collapsed;
            return;
        }

        JobStatusSection.Visibility = Visibility.Visible;

        SetJobCard(
            EmbeddingsCountText, RunEmbeddingsButton, RunEmbeddingsIcon, RunEmbeddingsText,
            EmbeddingsProgressPanel, EmbeddingsProgressBar, EmbeddingsPctText,
            status.Embeddings.Count, status.Embeddings.Total, status.Embeddings.Running);

        SetJobCard(
            TasteProfilesCountText, RunTasteProfilesButton, RunTasteProfilesIcon, RunTasteProfilesText,
            null, null, null,
            status.TasteProfiles.Count, status.TasteProfiles.Total, status.TasteProfiles.Running);

        SetJobCard(
            CowatchCountText, RunCowatchButton, RunCowatchIcon, RunCowatchText,
            CowatchProgressPanel, CowatchProgressBar, CowatchPctText,
            status.Cowatch.Count, status.Cowatch.Total, status.Cowatch.Running);

        SetJobCard(
            RecommendationsCountText, RunRecommendationsButton, RunRecommendationsIcon, RunRecommendationsText,
            RecommendationsProgressPanel, RecommendationsProgressBar, RecommendationsPctText,
            status.Recommendations.Count, status.Recommendations.Total, status.Recommendations.Running);
    }

    /// <summary>
    /// Updates a single job card's count text, progress bar, and button state.
    /// Matches the web UI RecJobStatusCard component exactly:
    ///   - If total > 0: "{count} / {total} items"
    ///   - Else: "{count} entry" or "{count} entries"
    ///   - When running: button shows "Running..." with spinner icon, disabled
    ///   - Progress bar shown only when total > 0
    /// </summary>
    private static void SetJobCard(
        TextBlock countText,
        Button runButton,
        FontIcon runIcon,
        TextBlock runLabel,
        StackPanel? progressPanel,
        ProgressBar? progressBar,
        TextBlock? pctText,
        int count,
        int? total,
        bool running)
    {
        bool hasProgress = total.HasValue && total.Value > 0;

        // Count subtitle text
        if (hasProgress)
        {
            countText.Text = $"{count:N0} / {total:N0} items";
        }
        else
        {
            countText.Text = $"{count:N0} {(count == 1 ? "entry" : "entries")}";
        }

        // Button state
        runButton.IsEnabled = !running;
        if (running)
        {
            runIcon.Glyph = "\uE895"; // Sync/spinner-like glyph
            runLabel.Text = "Running...";
        }
        else
        {
            runIcon.Glyph = "\uE768"; // Play
            runLabel.Text = "Run";
        }

        // Progress bar
        if (progressPanel != null && progressBar != null && pctText != null)
        {
            if (hasProgress)
            {
                int pct = (int)Math.Round((double)count / total!.Value * 100.0);
                pct = Math.Clamp(pct, 0, 100);
                progressBar.Value = pct;
                pctText.Text = $"{pct}%";
                progressPanel.Visibility = Visibility.Visible;
            }
            else
            {
                progressPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ===== Poll timer =====

    private void UpdatePollTimer()
    {
        if (ViewModel.AnyJobRunning)
        {
            if (_pollTimer is null)
            {
                _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _pollTimer.Tick += PollTimer_Tick;
            }
            if (!_pollTimer.IsEnabled)
                _pollTimer.Start();
        }
        else
        {
            _pollTimer?.Stop();
        }
    }

    private async void PollTimer_Tick(object? sender, object e)
    {
        await ViewModel.RefreshStatusAsync();
        ApplyStatus(ViewModel.Status);
        if (!ViewModel.AnyJobRunning)
            _pollTimer?.Stop();
    }

    // ===== Button click handlers =====

    private async void RunEmbeddingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(RunEmbeddingsButton, RunEmbeddingsIcon, RunEmbeddingsText,
            ViewModel.RunEmbeddingsCommand);
    }

    private async void RunTasteProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(RunTasteProfilesButton, RunTasteProfilesIcon, RunTasteProfilesText,
            ViewModel.RunTasteProfilesCommand);
    }

    private async void RunCowatchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(RunCowatchButton, RunCowatchIcon, RunCowatchText,
            ViewModel.RunCowatchCommand);
    }

    private async void RunRecommendationsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(RunRecommendationsButton, RunRecommendationsIcon, RunRecommendationsText,
            ViewModel.RunRecommendationsCommand);
    }

    private async Task RunJobAsync(
        Button button,
        FontIcon icon,
        TextBlock label,
        CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
    {
        button.IsEnabled = false;
        icon.Glyph = "\uE895";
        label.Text = "Starting...";

        try
        {
            await command.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // Non-fatal — just show the error in status
            _ = ex;
        }
        finally
        {
            ApplyStatus(ViewModel.Status);
            UpdatePollTimer();
        }
    }

    // ===== Settings sections =====

    /// <summary>
    /// Rebuilds the settings sections panel from loaded server settings.
    /// Produces: Embedding Lock card (if present) + collapsible section panels
    /// matching the web UI's buildRecommendationSections() output.
    /// </summary>
    private void RebuildSettingsSections()
    {
        SettingsSectionsPanel.Children.Clear();

        // Embedding lock card
        var lockCard = BuildEmbeddingLockCard(ViewModel.GetSetting("recommendations.embedding_lock"));
        if (lockCard != null)
            SettingsSectionsPanel.Children.Add(lockCard);

        // Collapsible sections
        var sections = new (string Title, (string Key, string Label, SettingFieldType Type, string? Hint)[] Fields)[]
        {
            ("General", new[]
            {
                ("recommendations.enabled", "Enable Recommendations", SettingFieldType.Toggle, (string?)null),
            }),
            ("Embedding Configuration", new (string, string, SettingFieldType, string?)[]
            {
                ("recommendations.embedding_base_url", "Base URL", SettingFieldType.Text, "e.g. http://ollama:11434"),
                ("recommendations.embedding_model", "Model", SettingFieldType.Text, "e.g. text-embedding-3-large"),
                ("recommendations.embedding_auth_token", "Auth Token", SettingFieldType.Password, "Optional bearer token"),
            }),
            ("Schedule", new (string, string, SettingFieldType, string?)[]
            {
                ("recommendations.embeddings_cron", "Embeddings Cron", SettingFieldType.Text, "Cron expression, e.g. 0 3 * * *"),
                ("recommendations.taste_profiles_cron", "Taste Profiles Cron", SettingFieldType.Text, "e.g. 0 4 * * *"),
                ("recommendations.cowatch_cron", "Co-Watch Cron", SettingFieldType.Text, "e.g. 30 4 * * *"),
                ("recommendations.recommendations_cron", "Recommendations Cron", SettingFieldType.Text, "e.g. 0 5 * * *"),
            }),
            ("Advanced", new (string, string, SettingFieldType, string?)[]
            {
                ("recommendations.taste_decay_half_life_days", "Time Decay Half-Life (days)", SettingFieldType.Number, "How fast old signals lose weight. Default: 180"),
                ("recommendations.diversity_lambda", "Diversity Lambda", SettingFieldType.Text, "0 = max diversity, 1 = max relevance. Default: 0.7"),
            }),
        };

        foreach (var (title, fields) in sections)
        {
            SettingsSectionsPanel.Children.Add(BuildCollapsibleSection(title, fields));
        }
    }

    private enum SettingFieldType { Text, Number, Password, Toggle }

    /// <summary>
    /// Builds the Embedding Lock card — web UI RecEmbeddingLockCard.
    /// surface-panel, max-w-3xl, rounded-[1.7rem], border-0.
    /// Shows: model, source dimensions, storage dimensions, note.
    /// Returns null if the lock JSON is absent or invalid.
    /// </summary>
    private Border? BuildEmbeddingLockCard(string? rawLock)
    {
        if (string.IsNullOrEmpty(rawLock))
            return null;

        string? model = null;
        int? sourceDim = null;
        int? storageDim = null;

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(rawLock);
            var root = doc.RootElement;
            if (root.TryGetProperty("model", out var m)) model = m.GetString()?.Trim();
            if (root.TryGetProperty("source_dimensions", out var sd) && sd.ValueKind == System.Text.Json.JsonValueKind.Number)
                sourceDim = sd.GetInt32();
            if (root.TryGetProperty("storage_dimensions", out var strd) && strd.ValueKind == System.Text.Json.JsonValueKind.Number)
                storageDim = strd.GetInt32();
        }
        catch { return null; }

        if (string.IsNullOrEmpty(model) || sourceDim is null)
            return null;

        storageDim ??= 3072;
        const string note = "Changing this config requires a manual reset, which is not currently supported in-product.";

        // Width + left-alignment come from SettingsSectionsPanel (MaxWidth=768, Left).
        var card = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 16, 20, 16),
        };

        var outer = new StackPanel { Spacing = 16 };

        // Header row: title + "Locked" badge
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerText = new StackPanel { Spacing = 4 };
        headerText.Children.Add(new TextBlock
        {
            Text = "Embedding Lock",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        headerText.Children.Add(new TextBlock
        {
            Text = "This installation is locked to a specific embedding space after the first successful embed.",
            FontSize = 13,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(headerText, 0);

        var badge = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeBackgroundBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Top,
        };
        badge.Child = new TextBlock
        {
            Text = "Locked",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
        };
        Grid.SetColumn(badge, 1);
        headerGrid.Children.Add(headerText);
        headerGrid.Children.Add(badge);

        outer.Children.Add(headerGrid);

        // dl grid: Model / Source dimensions / Storage dimensions
        var dlGrid = new Grid { ColumnSpacing = 16 };
        dlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        dlGrid.Children.Add(BuildDlItem("MODEL", model!, 0));
        dlGrid.Children.Add(BuildDlItem("SOURCE DIMENSIONS", sourceDim.ToString()!, 1));
        dlGrid.Children.Add(BuildDlItem("STORAGE DIMENSIONS", storageDim.ToString()!, 2));
        outer.Children.Add(dlGrid);

        // Note
        outer.Children.Add(new TextBlock
        {
            Text = note,
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        card.Child = outer;
        return card;
    }

    private static UIElement BuildDlItem(string label, string value, int column)
    {
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            CharacterSpacing = 100,
        });
        sp.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        Grid.SetColumn(sp, column);
        return sp;
    }

    /// <summary>
    /// Builds a collapsible settings section panel.
    /// Web UI: surface-panel, max-w-3xl, rounded-[1.7rem], border-0.
    /// Header button toggles open/collapsed; fields shown when open.
    /// </summary>
    private Border BuildCollapsibleSection(
        string title,
        (string Key, string Label, SettingFieldType Type, string? Hint)[] fields)
    {
        // Width + left-alignment come from SettingsSectionsPanel (MaxWidth=768, Left).
        var card = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(16),
        };

        var outer = new StackPanel();

        // ---- Toggle header button ----
        var headerBtn = new Button
        {
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(20, 16, 20, 16),
            CornerRadius = new CornerRadius(16),
        };

        var headerContent = new Grid();
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(titleBlock, 0);

        var chevronIcon = new FontIcon
        {
            Glyph = "\uE972", // ChevronDown
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chevronIcon, 1);

        headerContent.Children.Add(titleBlock);
        headerContent.Children.Add(chevronIcon);
        headerBtn.Content = headerContent;

        outer.Children.Add(headerBtn);

        // ---- Fields panel (initially visible — sections start open, matching web UI) ----
        var fieldsPanel = new StackPanel
        {
            Padding = new Thickness(20, 0, 20, 16),
        };

        // Top border separator
        var separator = new Border
        {
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, 4),
        };
        fieldsPanel.Children.Add(separator);

        if (title == "Embedding Configuration")
            fieldsPanel.Children.Add(BuildProviderPresets());

        foreach (var (key, label, type, hint) in fields)
        {
            fieldsPanel.Children.Add(BuildSettingField(key, label, type, hint));
        }

        if (title == "Embedding Configuration")
            fieldsPanel.Children.Add(BuildConnectionCheck());

        outer.Children.Add(fieldsPanel);
        card.Child = outer;

        // Wire toggle
        bool isOpen = true;
        headerBtn.Click += (_, _) =>
        {
            isOpen = !isOpen;
            fieldsPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            chevronIcon.Glyph = isOpen ? "\uE972" : "\uE974"; // ChevronDown / ChevronRight
        };

        return card;
    }

    private UIElement BuildProviderPresets()
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(new TextBlock { Text = "Provider Presets", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose a provider to fill the base URL and model.",
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(BuildPresetButton("Gemini", "Recommended", "https://generativelanguage.googleapis.com", "gemini-embedding-001"));
        buttons.Children.Add(BuildPresetButton("Ollama", "Local", "http://ollama:11434", "qwen3-embedding:latest"));
        buttons.Children.Add(BuildPresetButton("OpenAI", "", "https://api.openai.com", "text-embedding-3-large"));
        panel.Children.Add(buttons);
        return panel;
    }

    private Button BuildPresetButton(string label, string tag, string baseUrl, string model)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(tag))
            text.Children.Add(new TextBlock { Text = tag, FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });

        var button = new Button { Content = text, MinWidth = 136, HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            await ViewModel.UpdateSettingAsync("recommendations.embedding_base_url", baseUrl);
            await ViewModel.UpdateSettingAsync("recommendations.embedding_model", model);
            RestartBanner.Visibility = Visibility.Visible;
            RebuildSettingsSections();
        };
        return button;
    }

    private UIElement BuildConnectionCheck()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(0, 12, 0, 0) };
        var result = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var button = new Button { Content = "Check Connection" };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            button.Content = "Checking...";
            try
            {
                var keys = new[]
                {
                    "recommendations.enabled",
                    "recommendations.embedding_base_url",
                    "recommendations.embedding_model",
                    "recommendations.embedding_auth_token",
                };
                var request = new AdminSettingsConnectionCheckRequest
                {
                    Values = keys.ToDictionary(key => key, ViewModel.GetSetting),
                };
                var response = await App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>()
                    .CheckSettingsConnectionAsync("recommendations_embedding", request);
                result.Text = response.Message;
                result.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[response.Success ? "SuccessBrush" : "ErrorBrush"];
            }
            catch (Exception ex)
            {
                result.Text = ex.Message;
                result.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"];
            }
            finally
            {
                button.Content = "Check Connection";
                button.IsEnabled = true;
            }
        };
        panel.Children.Add(button);
        panel.Children.Add(result);
        return panel;
    }

    /// <summary>
    /// Builds a single setting field row matching the web UI RecSettingField component.
    /// Toggle: flex row with label + switch (ToggleSwitch).
    /// Password: label + PasswordBox.
    /// Number: label + narrow TextBox.
    /// Text: label + TextBox.
    /// </summary>
    private UIElement BuildSettingField(string key, string label, SettingFieldType type, string? hint)
    {
        var container = new StackPanel
        {
            Padding = new Thickness(0, 12, 0, 12),
        };

        if (type == SettingFieldType.Toggle)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelStack = new StackPanel { Spacing = 2 };
            labelStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            });
            if (!string.IsNullOrEmpty(hint))
            {
                labelStack.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            Grid.SetColumn(labelStack, 0);

            var toggle = new ToggleSwitch
            {
                IsOn = string.Equals(ViewModel.GetSetting(key), "true", StringComparison.OrdinalIgnoreCase),
                OnContent = "",
                OffContent = "",
                Margin = new Thickness(0),
            };
            Grid.SetColumn(toggle, 1);
            toggle.Toggled += (_, _) => CommitSetting(key, toggle.IsOn ? "true" : "false");

            row.Children.Add(labelStack);
            row.Children.Add(toggle);
            container.Children.Add(row);
            return container;
        }

        // Label
        container.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (type == SettingFieldType.Password)
        {
            bool isConfigured = ViewModel.IsSensitiveConfigured(key);
            var pb = new PasswordBox
            {
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["DarkPasswordBoxStyle"],
                PlaceholderText = isConfigured ? "configured" : (hint ?? "Not configured"),
                MaxWidth = 448,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            pb.LostFocus += (_, _) =>
            {
                if (!string.IsNullOrEmpty(pb.Password))
                    CommitSetting(key, pb.Password);
            };
            container.Children.Add(pb);
        }
        else if (type == SettingFieldType.Number)
        {
            var tb = new TextBox
            {
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["DarkTextBoxStyle"],
                Text = ViewModel.GetSetting(key),
                Width = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            string serverVal = ViewModel.GetSetting(key);
            tb.LostFocus += (_, _) =>
            {
                if (tb.Text != serverVal)
                    CommitSetting(key, tb.Text);
            };
            container.Children.Add(tb);
            if (!string.IsNullOrEmpty(hint))
            {
                container.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }
        else // Text / Duration
        {
            string serverVal = ViewModel.GetSetting(key);
            var tb = new TextBox
            {
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["DarkTextBoxStyle"],
                Text = serverVal,
                PlaceholderText = hint ?? "",
                MaxWidth = 448,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            tb.LostFocus += (_, _) =>
            {
                if (tb.Text != serverVal)
                    CommitSetting(key, tb.Text);
            };
            container.Children.Add(tb);
            if (!string.IsNullOrEmpty(hint))
            {
                container.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        return container;
    }

    private async void CommitSetting(string key, string value)
    {
        await ViewModel.UpdateSettingAsync(key, value);
        RestartBanner.Visibility = Visibility.Visible;
    }
}
