using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.HistoryImport;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models.Plugins;
using ContinuumPlayer.Services;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    // Start suppressed — handlers that fire during XAML parse (before all sibling
    // x:Name fields are assigned) would otherwise null-ref on their forward references
    // and surface as a cryptic "Failed to assign to RangeBase.Value" XamlParseException.
    // Set back to false after the page finishes loading.
    private bool _suppressEvents = true;

    // Language options for audio (spoken language)
    private static readonly (string Tag, string Label)[] AudioLanguageOptions =
    [
        ("", "Profile default"),
        ("original", "Original Language"),
        ("en", "English"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("zh", "Chinese"),
        ("ru", "Russian"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
    ];

    // Language options for subtitles (includes "None")
    private static readonly (string Tag, string Label)[] SubtitleLanguageOptions =
    [
        ("", "Profile default"),
        ("none", "None"),
        ("en", "English"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("zh", "Chinese"),
        ("ru", "Russian"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
    ];

    private static readonly (string Tag, string Label)[] SubtitleModeOptions =
    [
        ("", "Profile default"),
        ("auto", "Auto"),
        ("always", "Always"),
        ("off", "Off"),
    ];

    private static readonly (string Tag, string Label)[] ForcedSubtitleOptions =
    [
        ("", "Profile default"),
        ("on", "On"),
        ("off", "Off"),
    ];

    // Subtitle font color options
    private static readonly (string Hex, string Label)[] SubtitleColorOptions =
    [
        ("#FFFFFF", "White"),
        ("#FFFF00", "Yellow"),
        ("#00FF00", "Green"),
        ("#00FFFF", "Cyan"),
        ("#5599FF", "Blue"),
    ];

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        this.InitializeComponent();

        ViewModel.LibraryCards.CollectionChanged += LibraryCards_CollectionChanged;
        ViewModel.HomeSections.CollectionChanged += HomeSections_CollectionChanged;
        ViewModel.ImportRuns.CollectionChanged += ImportRuns_CollectionChanged;
        ViewModel.PluginSettingsList.CollectionChanged += PluginSettings_CollectionChanged;

        BuildSubtitleColorSwatches();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        await ViewModel.LoadCommand.ExecuteAsync(null);
        SyncComboBoxes();
        SyncSubtitleAppearanceControls();
        BuildThemeCards();
        UpdateCurrentThemeDisplay();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Stop the history_import event channel subscription when leaving Settings.
        StopImportEventSubscription();
        base.OnNavigatedFrom(e);
    }

    private void SyncComboBoxes()
    {
        _suppressEvents = true;

        SelectComboBoxByTag(QualityComboBox, ViewModel.QualityPreference);
        SelectComboBoxByTag(SubtitleLanguageComboBox, ViewModel.SubtitleLanguage);
        SelectComboBoxByTag(SubtitleModeComboBox, ViewModel.SubtitleMode);
        SelectComboBoxByTag(NextUpModeComboBox, ViewModel.NextUpMode);

        // Spoken language
        SelectComboBoxByTag(SpokenLanguageComboBox, "en");

        _suppressEvents = false;
    }

    private void SyncSubtitleAppearanceControls()
    {
        _suppressEvents = true;
        SelectComboBoxByTag(SubtitleFontSizeComboBox, ViewModel.SubFontSize);
        SelectComboBoxByTag(SubtitleFontFamilyComboBox, ViewModel.SubFontFamily);
        SelectComboBoxByTag(SubtitleBgStyleComboBox, ViewModel.SubBackgroundStyle);
        SelectComboBoxByTag(SubtitlePositionComboBox, ViewModel.SubPosition);
        SubtitleOutlineToggle.IsOn = ViewModel.SubOutlineEnabled;
        // B55: SubBackgroundOpacity is now int 0-100 (matching webui) — no
        // more /*100 scaling.
        SubtitleBgOpacitySlider.Value = ViewModel.SubBackgroundOpacity;
        SubtitleBgOpacityLabel.Text = $"{ViewModel.SubBackgroundOpacity}%";
        UpdateSubtitleColorSelection();
        UpdateSubtitlePreview();
        _suppressEvents = false;
    }

    private static void SelectComboBoxByTag(ComboBox combo, string tagValue)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag is string tag && tag == tagValue)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        // If no match, try selecting first item
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    // ===== Tab switching =====
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not string tag)
            return;

        var tabs = new[] { AppearanceTab, PlaybackTab, LibrariesTab, SubtitlesTab, HomeScreenTab, ImportTab, PluginsTab, SessionsTab };
        foreach (var tab in tabs)
        {
            tab.Style = (Style)Resources["InactiveTabStyle"];
        }

        clickedButton.Style = (Style)Resources["ActiveTabStyle"];

        AppearancePanel.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PlaybackPanel.Visibility = tag == "Playback" ? Visibility.Visible : Visibility.Collapsed;
        LibrariesPanel.Visibility = tag == "Libraries" ? Visibility.Visible : Visibility.Collapsed;
        SubtitlesPanel.Visibility = tag == "Subtitles" ? Visibility.Visible : Visibility.Collapsed;
        HomeScreenPanel.Visibility = tag == "HomeScreen" ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = tag == "Import" ? Visibility.Visible : Visibility.Collapsed;
        PluginsPanel.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        SessionsPanel.Visibility = tag == "Sessions" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "Sessions")
        {
            _ = LoadSessionsAsync();
        }
        else if (tag == "HomeScreen")
        {
            _ = ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
        }
        else if (tag == "Import")
        {
            _ = EnsureImportTabLoadedAsync();
        }
        else if (tag == "Plugins")
        {
            _ = ViewModel.LoadPluginSettingsCommand.ExecuteAsync(null);
        }
    }

    // ===== Theme cards =====
    private void BuildThemeCards()
    {
        ThemeCardsContainer.Items.Clear();

        var themeService = App.Services.GetRequiredService<ThemeService>();
        var currentTheme = themeService.CurrentTheme;
        var allThemes = ThemeService.GetAllThemeInfos();

        bool addedCuratedHeader = false;
        bool addedOthersHeader = false;

        foreach (var themeInfo in allThemes)
        {
            // Add group headers
            if (themeInfo.IsCurated && !addedCuratedHeader)
            {
                ThemeCardsContainer.Items.Add(BuildSectionHeader("Featured"));
                addedCuratedHeader = true;
            }
            else if (!themeInfo.IsCurated && !addedOthersHeader)
            {
                ThemeCardsContainer.Items.Add(BuildSectionHeader("All Themes"));
                addedOthersHeader = true;
            }

            var isActive = themeInfo.Id == currentTheme;
            var card = BuildThemeCard(themeInfo.Id, themeInfo.Label, themeInfo.Description,
                themeInfo.PreviewAccent, themeInfo.PreviewBackground, isActive);
            ThemeCardsContainer.Items.Add(card);
        }
    }

    private static TextBlock BuildSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 12, 0, 4),
        };
    }

    private Border BuildThemeCard(string themeId, string displayName, string description,
        string accentHex, string bgHex, bool isActive)
    {
        var accentColor = ColorFromHex(accentHex);
        var bgColor = ColorFromHex(bgHex);

        // Outer card border
        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Background = isActive
                ? new SolidColorBrush(ColorFromHex("#1D2A3B"))
                : (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = isActive
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x4D, accentColor.R, accentColor.G, accentColor.B))
                : (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var outerStack = new StackPanel { Spacing = 12 };

        // Header row: name + check + swatch
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameStack.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (isActive)
        {
            nameStack.Children.Add(new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        Grid.SetColumn(nameStack, 0);
        headerRow.Children.Add(nameStack);

        // Color swatch
        var swatchStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(accentColor),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
        };
        swatchStack.Children.Add(swatch);
        Grid.SetColumn(swatchStack, 1);
        headerRow.Children.Add(swatchStack);

        outerStack.Children.Add(headerRow);

        // Description
        if (!string.IsNullOrEmpty(description))
        {
            outerStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Mini preview panel (simulating the web UI)
        var previewBorder = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Height = 70,
        };

        var previewStack = new StackPanel { Spacing = 8 };

        // First row: accent dot + line
        var previewRow1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        previewRow1.Children.Add(new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(accentColor),
        });
        previewRow1.Children.Add(new Border
        {
            Width = 64, Height = 8, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB3, 255, 255, 255)),
        });
        previewStack.Children.Add(previewRow1);

        // Second row: simulated content blocks
        var previewRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        previewRow2.Children.Add(new Border
        {
            Width = 80, Height = 28, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x18, 255, 255, 255)),
        });
        previewRow2.Children.Add(new Border
        {
            Width = 50, Height = 28, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x12, 255, 255, 255)),
        });
        previewStack.Children.Add(previewRow2);

        previewBorder.Child = previewStack;
        outerStack.Children.Add(previewBorder);

        card.Child = outerStack;

        // Click handler
        card.Tapped += (_, _) =>
        {
            ViewModel.UiTheme = themeId;
            _ = ViewModel.SaveUiThemeCommand.ExecuteAsync(null);
            BuildThemeCards();
            UpdateCurrentThemeDisplay();
        };

        return card;
    }

    private void UpdateCurrentThemeDisplay()
    {
        var themeService = App.Services.GetRequiredService<ThemeService>();
        var info = ThemeService.GetThemeInfo(themeService.CurrentTheme);
        CurrentThemeName.Text = info?.Label ?? ThemeService.GetDisplayName(themeService.CurrentTheme);
        CurrentThemeDescription.Text = info?.Description ?? "";
    }

    private void ResetTheme_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UiTheme = "midnight-cinema";
        _ = ViewModel.SaveUiThemeCommand.ExecuteAsync(null);
        BuildThemeCards();
        UpdateCurrentThemeDisplay();
    }

    // ===== Library cards =====
    private void LibraryCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLibraryCards();
    }

    private void RebuildLibraryCards()
    {
        LibraryCardsContainer.Children.Clear();

        if (ViewModel.LibraryCards.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "No libraries found.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            };
            LibraryCardsContainer.Children.Add(emptyText);
            return;
        }

        foreach (var card in ViewModel.LibraryCards)
        {
            // When user toggles library visibility, refresh sidebar immediately
            card.LibraryVisibilityChanged += () =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    App.MainWindowInstance?.UpdateLibraryNavItems();
                });
            };
            LibraryCardsContainer.Children.Add(BuildLibraryCard(card));
        }
    }

    private Border BuildLibraryCard(LibraryCardViewModel vm)
    {
        var cardBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20),
            BorderThickness = new Thickness(0),
        };

        var outerStack = new StackPanel { Spacing = 12 };

        // === Row 1: Name + badges on left, visibility toggle on right ===
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        headerRow.Children.Add(new TextBlock
        {
            Text = vm.LibraryName,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Type badge
        var typeBadge = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        typeBadge.Child = new TextBlock
        {
            Text = vm.LibraryType,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 11,
        };
        headerRow.Children.Add(typeBadge);

        // Custom badge
        var customBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = vm.HasCustomOverrides ? Visibility.Visible : Visibility.Collapsed,
        };
        customBadge.Child = new TextBlock
        {
            Text = "Custom",
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 11,
        };
        headerRow.Children.Add(customBadge);

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LibraryCardViewModel.HasCustomOverrides))
            {
                customBadge.Visibility = vm.HasCustomOverrides ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        Grid.SetColumn(headerRow, 0);
        headerGrid.Children.Add(headerRow);

        // Visibility toggle
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        var visibilityLabel = new TextBlock
        {
            Text = vm.IsEnabled ? "Visible on navigation" : "Hidden on navigation",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        togglePanel.Children.Add(visibilityLabel);

        var visibilityToggle = new ToggleSwitch
        {
            IsOn = vm.IsEnabled,
            MinWidth = 0,
            MinHeight = 0,
            OnContent = "",
            OffContent = "",
        };
        visibilityToggle.Toggled += (s, e) =>
        {
            vm.IsEnabled = visibilityToggle.IsOn;
            visibilityLabel.Text = visibilityToggle.IsOn ? "Visible on navigation" : "Hidden on navigation";
        };
        togglePanel.Children.Add(visibilityToggle);

        Grid.SetColumn(togglePanel, 1);
        headerGrid.Children.Add(togglePanel);

        outerStack.Children.Add(headerGrid);

        // === Row 2: Summary text ===
        var summaryText = new TextBlock
        {
            Text = vm.SummaryText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LibraryCardViewModel.SummaryText))
                summaryText.Text = vm.SummaryText;
        };
        outerStack.Children.Add(summaryText);

        // === Row 3: Edit button ===
        var editButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Padding = new Thickness(0, 4, 0, 4),
            FontSize = 12,
        };
        var editButtonText = new TextBlock();
        UpdateEditButtonText(editButtonText, vm.IsExpanded);
        editButton.Content = editButtonText;

        // === Expandable section ===
        var expandPanel = new StackPanel
        {
            Spacing = 16,
            Visibility = vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };

        editButton.Click += (s, e) =>
        {
            vm.ToggleExpandedCommand.Execute(null);
            expandPanel.Visibility = vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateEditButtonText(editButtonText, vm.IsExpanded);
        };

        outerStack.Children.Add(editButton);

        // --- Spoken language dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Spoken language", AudioLanguageOptions, vm.AudioLanguage,
            (tag) => vm.AudioLanguage = tag));

        // --- Subtitle language dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Subtitle language", SubtitleLanguageOptions, vm.SubtitleLanguage,
            (tag) => vm.SubtitleLanguage = tag));

        // --- Subtitle behavior dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Subtitle behavior", SubtitleModeOptions, vm.SubtitleMode,
            (tag) => vm.SubtitleMode = tag));

        // --- Forced subtitles dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Forced subtitles", ForcedSubtitleOptions, vm.ForcedSubtitles,
            (tag) => vm.ForcedSubtitles = tag));

        // --- Reset button ---
        var resetButton = new Button
        {
            Content = "Reset to profile defaults",
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        };
        resetButton.Click += (s, e) => vm.ResetToDefaultsCommand.Execute(null);

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(LibraryCardViewModel.AudioLanguage)
                or nameof(LibraryCardViewModel.SubtitleLanguage)
                or nameof(LibraryCardViewModel.SubtitleMode)
                or nameof(LibraryCardViewModel.ForcedSubtitles))
            {
                SyncExpandPanelCombos(expandPanel, vm);
            }
        };

        expandPanel.Children.Add(resetButton);
        outerStack.Children.Add(expandPanel);

        cardBorder.Child = outerStack;
        return cardBorder;
    }

    private static void UpdateEditButtonText(TextBlock textBlock, bool isExpanded)
    {
        textBlock.Text = isExpanded ? "\u25BC Hide playback overrides" : "\u25B6 Edit playback overrides";
    }

    private StackPanel BuildDropdownRow(string label, (string Tag, string Label)[] options, string currentValue, Action<string> onChanged)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontWeight = FontWeights.Medium,
            FontSize = 12,
        });

        var combo = new ComboBox { Width = 300 };
        foreach (var (tag, lbl) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = lbl, Tag = tag });
        }

        SelectComboBoxByTag(combo, currentValue);

        bool ready = false;
        combo.Loaded += (s, e) => ready = true;

        combo.SelectionChanged += (s, e) =>
        {
            if (!ready) return;
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                onChanged(tag);
            }
        };

        row.Children.Add(combo);
        return row;
    }

    private static void SyncExpandPanelCombos(StackPanel expandPanel, LibraryCardViewModel vm)
    {
        string[] values = [vm.AudioLanguage, vm.SubtitleLanguage, vm.SubtitleMode, vm.ForcedSubtitles];
        int idx = 0;
        foreach (var child in expandPanel.Children)
        {
            if (child is StackPanel rowPanel && idx < values.Length)
            {
                foreach (var rowChild in rowPanel.Children)
                {
                    if (rowChild is ComboBox combo)
                    {
                        SelectComboBoxByTag(combo, values[idx]);
                        break;
                    }
                }
                idx++;
            }
        }
    }

    // ===== ComboBox change handlers =====
    private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (QualityComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.QualityPreference = val;
            _ = ViewModel.SaveQualityPreferenceCommand.ExecuteAsync(null);
        }
    }

    private void SpokenLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
    }

    private void SubtitleLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleLanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubtitleLanguage = val;
            _ = ViewModel.SaveSubtitleLanguageCommand.ExecuteAsync(null);
        }
    }

    private void SubtitleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubtitleMode = val;
            _ = ViewModel.SaveSubtitleModeCommand.ExecuteAsync(null);
        }
    }

    private void NextUpModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (NextUpModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.NextUpMode = val;
            _ = ViewModel.SaveNextUpModeCommand.ExecuteAsync(null);
        }
    }

    // ===== Subtitle appearance handlers =====

    private void BuildSubtitleColorSwatches()
    {
        SubtitleColorSwatches.Children.Clear();
        foreach (var (hex, label) in SubtitleColorOptions)
        {
            var color = ColorFromHex(hex);
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Tag = hex,
            };
            ToolTipService.SetToolTip(swatch, label);
            swatch.Tapped += (_, _) =>
            {
                ViewModel.SubFontColor = hex;
                UpdateSubtitleColorSelection();
                UpdateSubtitlePreview();
            };
            SubtitleColorSwatches.Children.Add(swatch);
        }
    }

    private void UpdateSubtitleColorSelection()
    {
        foreach (var child in SubtitleColorSwatches.Children)
        {
            if (child is Border border && border.Tag is string hex)
            {
                var isSelected = hex.Equals(ViewModel.SubFontColor, StringComparison.OrdinalIgnoreCase);
                border.BorderThickness = new Thickness(isSelected ? 3 : 1);
                border.BorderBrush = isSelected
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 255, 255, 255));
            }
        }
    }

    private void SubtitleFontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleFontSizeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubFontSize = val;
            UpdateSubtitlePreview();
        }
    }

    private void SubtitleFontFamily_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleFontFamilyComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubFontFamily = val;
            UpdateSubtitlePreview();
        }
    }

    private void SubtitleBgStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleBgStyleComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubBackgroundStyle = val;
            UpdateSubtitlePreview();
        }
    }

    private void SubtitleOutline_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ViewModel.SubOutlineEnabled = SubtitleOutlineToggle.IsOn;
        UpdateSubtitlePreview();
    }

    private void SubtitleBgOpacity_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        // B55: 0-100 integer scale
        ViewModel.SubBackgroundOpacity = (int)Math.Round(SubtitleBgOpacitySlider.Value);
        SubtitleBgOpacityLabel.Text = $"{ViewModel.SubBackgroundOpacity}%";
        UpdateSubtitlePreview();
    }

    private void SubtitlePosition_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitlePositionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubPosition = val;
            UpdateSubtitlePreview();
        }
    }

    private void UpdateSubtitlePreview()
    {
        // Font size
        double fontSize = ViewModel.SubFontSize switch
        {
            "small" => 13,
            "large" => 20,
            "xlarge" => 26,
            _ => 16,
        };
        SubtitlePreviewLine1.FontSize = fontSize;
        SubtitlePreviewLine2.FontSize = fontSize;

        // Font color
        var fontColor = ColorFromHex(ViewModel.SubFontColor);
        var fontBrush = new SolidColorBrush(fontColor);
        SubtitlePreviewLine1.Foreground = fontBrush;
        SubtitlePreviewLine2.Foreground = fontBrush;

        // Font family
        if (ViewModel.SubFontFamily == "monospace")
        {
            var ff = new FontFamily("Consolas");
            SubtitlePreviewLine1.FontFamily = ff;
            SubtitlePreviewLine2.FontFamily = ff;
        }
        else if (ViewModel.SubFontFamily == "serif")
        {
            var ff = new FontFamily("Times New Roman");
            SubtitlePreviewLine1.FontFamily = ff;
            SubtitlePreviewLine2.FontFamily = ff;
        }
        else
        {
            var ff = new FontFamily("Segoe UI");
            SubtitlePreviewLine1.FontFamily = ff;
            SubtitlePreviewLine2.FontFamily = ff;
        }

        // Background
        if (ViewModel.SubBackgroundStyle == "box")
        {
            var bgColor = ColorFromHex(ViewModel.SubBackgroundColor);
            // B55: SubBackgroundOpacity is 0-100 integer — convert to 0-255 alpha.
            byte alpha = (byte)Math.Round(ViewModel.SubBackgroundOpacity * 2.55);
            var bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, bgColor.R, bgColor.G, bgColor.B));
            SubtitlePreviewBg1.Background = bgBrush;
            SubtitlePreviewBg2.Background = bgBrush;
        }
        else if (ViewModel.SubBackgroundStyle == "shadow")
        {
            SubtitlePreviewBg1.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0, 0, 0));
            SubtitlePreviewBg2.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0, 0, 0));
        }
        else
        {
            SubtitlePreviewBg1.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            SubtitlePreviewBg2.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        // Position
        SubtitlePreviewStack.VerticalAlignment = ViewModel.SubPosition == "top"
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
    }

    private void SubtitleSave_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveSubtitleAppearanceCommand.ExecuteAsync(null);
    }

    private void SubtitleReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetSubtitleAppearanceCommand.Execute(null);
        SyncSubtitleAppearanceControls();
    }

    // ===== Home Screen Section Handlers =====

    private void HomeSections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildHomeSectionItems();
    }

    private void RebuildHomeSectionItems()
    {
        HomeSectionItemsContainer.Children.Clear();

        if (ViewModel.HomeSections.Count == 0)
        {
            HomeSectionItemsContainer.Children.Add(new TextBlock
            {
                Text = "No sections configured.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        foreach (var section in ViewModel.HomeSections)
        {
            HomeSectionItemsContainer.Children.Add(BuildHomeSectionRow(section));
        }
    }

    private Border BuildHomeSectionRow(SettingsSectionEntry section)
    {
        var row = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // grip icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // title + type
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // actions

        // Grip icon
        var gripIcon = new FontIcon
        {
            Glyph = "\uE700",
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(gripIcon, 0);
        grid.Children.Add(gripIcon);

        // Title + type badge
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = section.Hidden
                ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
                : (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        var typeBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
        };
        typeBadge.Child = new TextBlock
        {
            Text = section.SectionType,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        titleStack.Children.Add(typeBadge);

        Grid.SetColumn(titleStack, 1);
        grid.Children.Add(titleStack);

        // Action buttons
        var actionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        // Move up
        var upBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70E", FontSize = 12 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
        };
        upBtn.Click += (_, _) =>
        {
            ViewModel.MoveSectionUp(section);
        };
        actionStack.Children.Add(upBtn);

        // Move down
        var downBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
        };
        downBtn.Click += (_, _) =>
        {
            ViewModel.MoveSectionDown(section);
        };
        actionStack.Children.Add(downBtn);

        // Visibility toggle
        var eyeIcon = section.Hidden ? "\uED1A" : "\uE7B3"; // Eye off : Eye
        var visBtn = new Button
        {
            Content = new FontIcon { Glyph = eyeIcon, FontSize = 14 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
        };
        visBtn.Click += (_, _) =>
        {
            ViewModel.ToggleSectionVisibility(section);
        };
        actionStack.Children.Add(visBtn);

        // Delete (only custom sections)
        // B58: Confirm before destructive removal. Web copy distinguishes
        // "Delete custom section?" from "Remove section?" — only IsCustom
        // sections expose this button so we use the "Delete custom section" copy.
        if (section.IsCustom)
        {
            var delBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] },
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Padding = new Thickness(6),
            };
            delBtn.Click += async (_, _) =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Delete custom section",
                    Content = $"Delete \"{section.Title}\"? This action cannot be undone.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close,
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ViewModel.RemoveSection(section);
                }
            };
            actionStack.Children.Add(delBtn);
        }

        Grid.SetColumn(actionStack, 2);
        grid.Children.Add(actionStack);

        row.Child = grid;
        return row;
    }

    private void HomeSectionsSave_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    // B57: Confirm before destructive reset of all section customizations.
    private async void HomeSectionsReset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset section customizations",
            Content = "Reset all section customizations to defaults? This action cannot be undone.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ResetHomeSectionsCommand.ExecuteAsync(null);
        }
    }

    // ===================================================================
    // ===== Import Handlers (rebuilt to match WebUI 2026-04-10) =====
    // ===================================================================

    /// <summary>
    /// Styles for the three source cards — highlights the currently-selected one
    /// with an accent border. Called whenever the source type changes.
    /// </summary>
    private void UpdateImportSourceCardStyles()
    {
        var selected = ViewModel.ImportSourceType;
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var border = (Brush)Application.Current.Resources["BorderBrush"];
        ImportSourceEmbyCard.BorderBrush = selected == "emby" ? accent : border;
        ImportSourceEmbyCard.BorderThickness = new Thickness(selected == "emby" ? 2 : 1);
        ImportSourceJellyfinCard.BorderBrush = selected == "jellyfin" ? accent : border;
        ImportSourceJellyfinCard.BorderThickness = new Thickness(selected == "jellyfin" ? 2 : 1);
        ImportSourcePlexCard.BorderBrush = selected == "plex" ? accent : border;
        ImportSourcePlexCard.BorderThickness = new Thickness(selected == "plex" ? 2 : 1);
    }

    /// <summary>Clicked on one of the three source cards (Emby/Jellyfin/Plex).</summary>
    private void ImportSourceCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string source) return;
        ViewModel.ImportSourceType = source;
        UpdateImportPanelVisibility();
    }

    /// <summary>Emby sub-mode buttons — Connect / Saved.</summary>
    private void ImportEmbyMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string mode) return;
        ViewModel.ImportEmbyMode = mode;
        UpdateImportPanelVisibility();
    }

    /// <summary>Plex sub-mode buttons — OAuth / Saved.</summary>
    private void ImportPlexMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string mode) return;
        ViewModel.ImportPlexMode = mode;
        UpdateImportPanelVisibility();
    }

    /// <summary>
    /// Syncs visibility of the source-card highlight, mode selectors, and per-mode
    /// auth panels to the current ViewModel source/mode state.
    /// </summary>
    private void UpdateImportPanelVisibility()
    {
        UpdateImportSourceCardStyles();

        var source = ViewModel.ImportSourceType;

        // Mode selectors
        ImportEmbyModeSelector.Visibility = source == "emby" ? Visibility.Visible : Visibility.Collapsed;
        ImportPlexModeSelector.Visibility = source == "plex" ? Visibility.Visible : Visibility.Collapsed;

        // Style Emby mode buttons as accent/secondary based on ImportEmbyMode
        var emodeAccent = (Style)Application.Current.Resources["AccentButtonStyle"];
        var emodeSecondary = (Style)Application.Current.Resources["SecondaryButtonStyle"];
        ImportEmbyConnectModeBtn.Style = ViewModel.ImportEmbyMode == "connect" ? emodeAccent : emodeSecondary;
        ImportEmbySavedModeBtn.Style = ViewModel.ImportEmbyMode == "saved" ? emodeAccent : emodeSecondary;

        // Style Plex mode buttons
        ImportPlexOAuthModeBtn.Style = ViewModel.ImportPlexMode == "oauth" ? emodeAccent : emodeSecondary;
        ImportPlexSavedModeBtn.Style = ViewModel.ImportPlexMode == "saved" ? emodeAccent : emodeSecondary;

        // Auth panels
        ImportEmbyConnectPanel.Visibility = (source == "emby" && ViewModel.ImportEmbyMode == "connect") ? Visibility.Visible : Visibility.Collapsed;
        ImportEmbySavedPanel.Visibility   = (source == "emby" && ViewModel.ImportEmbyMode == "saved")   ? Visibility.Visible : Visibility.Collapsed;
        ImportPlexOAuthPanel.Visibility   = (source == "plex" && ViewModel.ImportPlexMode == "oauth")   ? Visibility.Visible : Visibility.Collapsed;
        ImportPlexSavedPanel.Visibility   = (source == "plex" && ViewModel.ImportPlexMode == "saved")   ? Visibility.Visible : Visibility.Collapsed;
        ImportJellyfinPanel.Visibility    = source == "jellyfin" ? Visibility.Visible : Visibility.Collapsed;

        // Plex OAuth sub-states
        PlexAuthPendingPanel.Visibility = ViewModel.PlexAuthPending ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthPendingText.Text = string.IsNullOrEmpty(ViewModel.PlexAuthStatus)
            ? "Waiting for approval in browser..."
            : ViewModel.PlexAuthStatus;
        PlexAuthErrorPanel.Visibility = !string.IsNullOrEmpty(ViewModel.PlexAuthError) ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthErrorText.Text = ViewModel.PlexAuthError ?? "";

        bool plexConnected = ViewModel.PlexOAuthServers.Count > 0 && !ViewModel.PlexAuthPending;
        PlexAuthConnectedPanel.Visibility = plexConnected ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthSignInButton.Visibility = (plexConnected || ViewModel.PlexAuthPending || !string.IsNullOrEmpty(ViewModel.PlexAuthError))
            ? Visibility.Collapsed : Visibility.Visible;

        // Emby Connect connected panel
        bool embyConnected = ViewModel.EmbyConnectServers.Count > 0;
        EmbyConnectConnectedPanel.Visibility = embyConnected ? Visibility.Visible : Visibility.Collapsed;

        // Start button enabled state
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Password box handlers (PasswordBox can't x:Bind directly to VM) -----
    private void EmbyConnectPassword_Changed(object sender, RoutedEventArgs e)
        => ViewModel.EmbyConnectPassword = EmbyConnectPasswordBox.Password;
    private void EmbySavedPassword_Changed(object sender, RoutedEventArgs e)
        => ViewModel.EmbySavedPassword = EmbySavedPasswordBox.Password;
    private void PlexSavedToken_Changed(object sender, RoutedEventArgs e)
        => ViewModel.PlexSavedToken = PlexSavedTokenBox.Password;
    private void JellyfinPassword_Changed(object sender, RoutedEventArgs e)
        => ViewModel.JellyfinPassword = JellyfinPasswordBox.Password;

    // ----- Emby Connect login -----
    private async void EmbyConnectLogin_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.EmbyConnectLoginCommand.ExecuteAsync(null);
        // Populate server combo from VM state.
        EmbyConnectServerCombo.Items.Clear();
        foreach (var server in ViewModel.EmbyConnectServers)
            EmbyConnectServerCombo.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server });
        if (EmbyConnectServerCombo.Items.Count > 0) EmbyConnectServerCombo.SelectedIndex = 0;
        UpdateImportPanelVisibility();
    }

    private void EmbyConnectServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmbyConnectServerCombo.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportConnectServer srv)
            ViewModel.SelectedEmbyConnectServer = srv;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Emby Saved -----
    private void EmbySavedSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmbySavedSourceCombo.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportSource src)
            ViewModel.SelectedEmbySavedSource = src;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Plex OAuth -----
    private async void PlexAuthStart_Click(object sender, RoutedEventArgs e)
    {
        // Show pending panel immediately.
        UpdateImportPanelVisibility();
        await ViewModel.PlexAuthStartCommand.ExecuteAsync(null);
        // Populate server combo and refresh UI.
        PlexOAuthServerCombo.Items.Clear();
        foreach (var s in ViewModel.PlexOAuthServers)
            PlexOAuthServerCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        if (PlexOAuthServerCombo.Items.Count > 0) PlexOAuthServerCombo.SelectedIndex = 0;
        UpdateImportPanelVisibility();
    }

    private void PlexOAuthServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexOAuthServerCombo.SelectedItem is ComboBoxItem item && item.Tag is PlexServer srv)
            ViewModel.SelectedPlexOAuthServer = srv;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Plex Saved -----
    private void PlexSavedSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexSavedSourceCombo.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportSource src)
            ViewModel.SelectedPlexSavedSource = src;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Profile -----
    private void ImportProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImportProfileCombo.SelectedItem is ComboBoxItem item && item.Tag is string profileId)
            ViewModel.ImportProfileId = profileId;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Start import -----
    private async void StartImport_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartImportCommand.ExecuteAsync(null);
        UpdateImportPanelVisibility();
    }

    // ----- Saved sources + profiles combo population -----
    private void RebuildImportSourcesCombos()
    {
        // Emby saved sources
        EmbySavedSourceCombo.Items.Clear();
        foreach (var s in ViewModel.EmbySavedSources)
            EmbySavedSourceCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        if (EmbySavedSourceCombo.Items.Count > 0 && EmbySavedSourceCombo.SelectedIndex < 0)
            EmbySavedSourceCombo.SelectedIndex = 0;

        // Plex saved sources
        PlexSavedSourceCombo.Items.Clear();
        foreach (var s in ViewModel.PlexSavedSources)
            PlexSavedSourceCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        if (PlexSavedSourceCombo.Items.Count > 0 && PlexSavedSourceCombo.SelectedIndex < 0)
            PlexSavedSourceCombo.SelectedIndex = 0;
    }

    private void RebuildImportProfilesCombo()
    {
        ImportProfileCombo.Items.Clear();
        foreach (var p in ViewModel.ImportProfiles)
            ImportProfileCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        // Preselect the VM's current profile id.
        for (int i = 0; i < ImportProfileCombo.Items.Count; i++)
        {
            if (ImportProfileCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == ViewModel.ImportProfileId)
            {
                ImportProfileCombo.SelectedIndex = i;
                break;
            }
        }
        if (ImportProfileCombo.SelectedIndex < 0 && ImportProfileCombo.Items.Count > 0)
            ImportProfileCombo.SelectedIndex = 0;
    }

    // ----- Run history list -----
    private void ImportRuns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildImportRunCards();
    }

    private void RebuildImportRunCards()
    {
        ImportRunsContainer.Children.Clear();

        if (ViewModel.ImportRuns.Count == 0)
        {
            ImportRunsContainer.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Child = new TextBlock
                {
                    Text = "No imports have been started yet.",
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                },
            });
            return;
        }

        foreach (var run in ViewModel.ImportRuns)
        {
            ImportRunsContainer.Children.Add(BuildHistoryRunCard(run));
        }
    }

    /// <summary>Build one card for the Import history list. Click selects the run
    /// for display in the summary card above.</summary>
    private Border BuildHistoryRunCard(HistoryImportRun run)
    {
        var isActive = ViewModel.SelectedRunId == run.Id;

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources[isActive ? "AccentBrush" : "BorderBrush"],
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
        };

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Source letter icon
        var (iconLetter, iconBg, iconFg) = run.SourceType switch
        {
            "emby"     => ("E", "#1DCFA1", Microsoft.UI.Colors.White),
            "jellyfin" => ("J", "#00A4DC", Microsoft.UI.Colors.White),
            "plex"     => ("P", "#E5A00D", Microsoft.UI.Colors.Black),
            _          => ("?", "#6B7280", Microsoft.UI.Colors.White),
        };
        var iconBorder = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(HexToColor(iconBg)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = iconLetter,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(iconFg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(iconBorder, 0);
        row.Children.Add(iconBorder);

        // Title + meta
        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = $"{CapitalizeSource(run.SourceType)} import",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        var metaBits = new List<string> { FormatRelativeTime(run.CreatedAt) };
        if (run.Matched > 0) metaBits.Add($"{run.Matched} matched");
        textStack.Children.Add(new TextBlock
        {
            Text = string.Join("  \u00b7  ", metaBits),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        // Status badge
        var badge = BuildRunStatusBadge(run.Status);
        Grid.SetColumn(badge, 2);
        row.Children.Add(badge);

        card.Child = row;
        card.Tapped += (_, _) =>
        {
            ViewModel.SelectRunForDisplay(run);
            RebuildRunSummaryCard();
            RebuildImportRunCards(); // re-render to highlight active
        };
        return card;
    }

    private Border BuildRunStatusBadge(string status)
    {
        var (label, glyph, fg, bg) = status switch
        {
            "queued"    => ("Queued",    "\uE916", "#78AEFC", "#143056"),
            "running"   => ("Running",   "\uE895", "#FBBF24", "#3B2A0E"),
            "completed" => ("Completed", "\uE73E", "#4ADE80", "#0E2E18"),
            "failed"    => ("Failed",    "\uE711", "#F87171", "#3A1313"),
            "cancelled" => ("Cancelled", "\uE7A7", "#9CA3AF", "#1F2126"),
            _           => (status,      "\uE916", "#9CA3AF", "#1F2126"),
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(HexToColor(bg)),
            BorderBrush = new SolidColorBrush(HexToColor(fg) with { A = 0x55 }),
            BorderThickness = new Thickness(1),
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 11, Foreground = new SolidColorBrush(HexToColor(fg)) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(HexToColor(fg)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        badge.Child = row;
        return badge;
    }

    // ----- Run summary card -----
    private void RebuildRunSummaryCard()
    {
        RunSummaryContainer.Children.Clear();
        var run = ViewModel.DisplayRun;

        // Title/subtitle state
        RunSummaryTitle.Text = ViewModel.SelectedRunId != null ? "Selected import" : "Latest import";
        RunSummarySubtitle.Text = ViewModel.SelectedRunId != null
            ? "Details from the selected import run."
            : "Results from the most recent import run.";

        if (run == null)
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = "Import summaries will appear here after you start a run.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            return;
        }

        // Header: source + status
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = $"{CapitalizeSource(run.SourceType)} import",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(BuildRunStatusBadge(run.Status));
        RunSummaryContainer.Children.Add(header);

        var relative = FormatRelativeTime(run.CreatedAt);
        if (!string.IsNullOrEmpty(relative))
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = relative,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        // Progress bar (when running and fetched > 0)
        bool isActive = run.Status == "running" || run.Status == "queued";
        if (isActive && run.Fetched > 0)
        {
            var progressValue = 100.0 * (run.Matched + run.Unmatched + run.Skipped) / Math.Max(1, run.Fetched);
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(progressValue, 0, 100),
                Height = 6,
                Foreground = new SolidColorBrush(HexToColor("#FBBF24")),
                Background = new SolidColorBrush(HexToColor("#1F2937")),
            };
            RunSummaryContainer.Children.Add(progressBar);
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = $"{run.Matched + run.Unmatched + run.Skipped} / {run.Fetched} processed",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        // Metrics grid (6 boxes)
        var metricsGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < 6; i++) metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void AddMetric(int col, string label, int value, string? accentColor = null)
        {
            var box = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
            };
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            sp.Children.Add(new TextBlock
            {
                Text = value.ToString("N0"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (accentColor != null && value > 0)
                    ? new SolidColorBrush(HexToColor(accentColor))
                    : (Brush)Application.Current.Resources["PrimaryTextBrush"],
            });
            box.Child = sp;
            Grid.SetColumn(box, col);
            metricsGrid.Children.Add(box);
        }
        AddMetric(0, "Fetched",  run.Fetched);
        AddMetric(1, "Matched",  run.Matched,         "#4ADE80");
        AddMetric(2, "Unmatched",run.Unmatched,       "#FBBF24");
        AddMetric(3, "Progress", run.ProgressUpdated, "#4ADE80");
        AddMetric(4, "History",  run.HistoryCreated,  "#4ADE80");
        AddMetric(5, "Skipped",  run.Skipped);
        RunSummaryContainer.Children.Add(metricsGrid);

        // Error box
        if (!string.IsNullOrEmpty(run.ErrorMessage))
        {
            var errBorder = new Border
            {
                Background = new SolidColorBrush(HexToColor("#1A0E0E")),
                BorderBrush = new SolidColorBrush(HexToColor("#3A1313")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
            };
            var errRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            errRow.Children.Add(new FontIcon { Glyph = "\uE711", FontSize = 14, Foreground = new SolidColorBrush(HexToColor("#F87171")) });
            errRow.Children.Add(new TextBlock
            {
                Text = run.ErrorMessage,
                FontSize = 13,
                Foreground = new SolidColorBrush(HexToColor("#F87171")),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 540,
            });
            errBorder.Child = errRow;
            RunSummaryContainer.Children.Add(errBorder);
        }

        // Warnings
        if (run.Warnings != null && run.Warnings.Count > 0)
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = "Warnings",
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                Margin = new Thickness(0, 6, 0, 0),
            });
            foreach (var w in run.Warnings)
            {
                var wRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                wRow.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 12, Foreground = new SolidColorBrush(HexToColor("#FBBF24")), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
                wRow.Children.Add(new TextBlock
                {
                    Text = w,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                });
                RunSummaryContainer.Children.Add(wRow);
            }
        }

        // Unmatched samples
        if (run.UnmatchedSamples != null && run.UnmatchedSamples.Count > 0)
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = "Unmatched examples",
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                Margin = new Thickness(0, 6, 0, 0),
            });
            foreach (var s in run.UnmatchedSamples)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                };
                var sp = new StackPanel { Spacing = 2 };
                var titleText = s.Year > 0 ? $"{s.Title} ({s.Year})" : s.Title;
                sp.Children.Add(new TextBlock
                {
                    Text = titleText,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"{s.Kind} · {s.Reason}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                card.Child = sp;
                RunSummaryContainer.Children.Add(card);
            }
        }
    }

    // ----- Event channel wiring -----
    private Core.Services.EventChannelClient? _importEventsClient;
    private bool _importTabInitialized;

    private async Task EnsureImportTabLoadedAsync()
    {
        if (_importTabInitialized) return;
        _importTabInitialized = true;

        await ViewModel.LoadImportTabCommand.ExecuteAsync(null);
        RebuildImportSourcesCombos();
        RebuildImportProfilesCombo();
        RebuildRunSummaryCard();
        RebuildImportRunCards();
        UpdateImportPanelVisibility();

        // Start the event channel subscription for live run updates.
        StartImportEventSubscription();
    }

    private void StartImportEventSubscription()
    {
        try
        {
            _importEventsClient ??= App.Services.GetRequiredService<Core.Services.EventChannelClient>();
            _importEventsClient.SnapshotReceived -= OnImportEventSnapshot;
            _importEventsClient.EventReceived -= OnImportEventFrame;
            _importEventsClient.SnapshotReceived += OnImportEventSnapshot;
            _importEventsClient.EventReceived += OnImportEventFrame;
            _importEventsClient.Start("history_import");
        }
        catch { /* Best-effort — polling fallback is via LoadImportRunsAsync on entry */ }
    }

    private void StopImportEventSubscription()
    {
        if (_importEventsClient == null) return;
        _importEventsClient.SnapshotReceived -= OnImportEventSnapshot;
        _importEventsClient.EventReceived -= OnImportEventFrame;
        _importEventsClient.Stop();
    }

    private void OnImportEventSnapshot(string channel, System.Text.Json.JsonElement data)
    {
        if (channel != "history_import" || data.ValueKind != System.Text.Json.JsonValueKind.Array) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var element in data.EnumerateArray())
            {
                var run = DeserializeHistoryImportRun(element);
                if (run != null) ViewModel.ApplyImportRunUpdate(run);
            }
            RebuildRunSummaryCard();
            RebuildImportRunCards();
        });
    }

    private void OnImportEventFrame(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "history_import") return;
        DispatcherQueue.TryEnqueue(() =>
        {
            var run = DeserializeHistoryImportRun(data);
            if (run != null) ViewModel.ApplyImportRunUpdate(run);
            RebuildRunSummaryCard();
            RebuildImportRunCards();
        });
    }

    private static HistoryImportRun? DeserializeHistoryImportRun(System.Text.Json.JsonElement el)
    {
        if (el.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        try
        {
            // Reuse the API client's snake-case options.
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            };
            return System.Text.Json.JsonSerializer.Deserialize<HistoryImportRun>(el.GetRawText(), options);
        }
        catch
        {
            return null;
        }
    }

    // ----- Helpers -----
    private static string CapitalizeSource(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static Windows.UI.Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        uint value = Convert.ToUInt32(hex, 16);
        return Windows.UI.Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private static string FormatRelativeTime(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return "";
        var diff = DateTimeOffset.UtcNow - dt.ToUniversalTime();
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return dt.ToLocalTime().ToString("MMM d, yyyy");
    }

    // ===== Plugin Settings Handlers =====

    private void PluginSettings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildPluginCards();
    }

    private void RebuildPluginCards()
    {
        PluginCardsContainer.Children.Clear();

        if (ViewModel.PluginSettingsList.Count == 0)
        {
            PluginCardsContainer.Children.Add(new TextBlock
            {
                Text = "No plugins with user settings installed.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            return;
        }

        foreach (var plugin in ViewModel.PluginSettingsList)
        {
            PluginCardsContainer.Children.Add(BuildPluginCard(plugin));
        }
    }

    private Border BuildPluginCard(PluginSettingsSummary plugin)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(24, 20, 24, 20),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Spacing = 16 };

        // Header: plugin ID + version
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = plugin.PluginId,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        headerRow.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"v{plugin.Version}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            },
        });
        stack.Children.Add(headerRow);

        // Get current values for this plugin
        ViewModel.PluginSettingsValues.TryGetValue(plugin.Id, out var currentValues);
        currentValues ??= new Dictionary<string, string>();

        // Dynamic form fields based on UserConfigSchema
        foreach (var schema in plugin.UserConfigSchema)
        {
            if (schema.AdminForm?.Fields == null) continue;

            foreach (var field in schema.AdminForm.Fields)
            {
                var fieldStack = new StackPanel { Spacing = 4 };

                // Label
                fieldStack.Children.Add(new TextBlock
                {
                    Text = field.Label,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                });

                if (!string.IsNullOrEmpty(field.Description))
                {
                    fieldStack.Children.Add(new TextBlock
                    {
                        Text = field.Description,
                        FontSize = 12,
                        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        TextWrapping = TextWrapping.Wrap,
                    });
                }

                currentValues.TryGetValue(field.Key, out var currentVal);

                switch (field.Control.ToUpperInvariant())
                {
                    case "SELECT" when field.Options != null:
                    {
                        var combo = new ComboBox { Width = 300 };
                        foreach (var opt in field.Options)
                        {
                            var comboItem = new ComboBoxItem { Content = opt.Label, Tag = opt.Value };
                            combo.Items.Add(comboItem);
                            if (opt.Value == currentVal)
                                combo.SelectedItem = comboItem;
                        }
                        var capturedKey = field.Key;
                        var capturedId = plugin.Id;
                        combo.SelectionChanged += (_, _) =>
                        {
                            if (combo.SelectedItem is ComboBoxItem selItem && selItem.Tag is string val)
                            {
                                if (ViewModel.PluginSettingsValues.TryGetValue(capturedId, out var vals))
                                    vals[capturedKey] = val;
                            }
                        };
                        fieldStack.Children.Add(combo);
                        break;
                    }
                    case "TOGGLE":
                    {
                        var toggle = new ToggleSwitch
                        {
                            IsOn = currentVal == "true",
                            OnContent = "",
                            OffContent = "",
                        };
                        var capturedKey = field.Key;
                        var capturedId = plugin.Id;
                        toggle.Toggled += (_, _) =>
                        {
                            if (ViewModel.PluginSettingsValues.TryGetValue(capturedId, out var vals))
                                vals[capturedKey] = toggle.IsOn ? "true" : "false";
                        };
                        fieldStack.Children.Add(toggle);
                        break;
                    }
                    default: // TEXT, TEXTAREA
                    {
                        var textBox = new TextBox
                        {
                            Text = currentVal ?? "",
                            PlaceholderText = field.Placeholder ?? "",
                            Width = 360,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
                        };
                        if (field.Secret)
                        {
                            // Use PasswordBox-like styling
                        }
                        if (field.Multiline)
                        {
                            textBox.AcceptsReturn = true;
                            textBox.TextWrapping = TextWrapping.Wrap;
                            textBox.MinHeight = 80;
                        }
                        var capturedKey = field.Key;
                        var capturedId = plugin.Id;
                        textBox.TextChanged += (_, _) =>
                        {
                            if (ViewModel.PluginSettingsValues.TryGetValue(capturedId, out var vals))
                                vals[capturedKey] = textBox.Text;
                        };
                        fieldStack.Children.Add(textBox);
                        break;
                    }
                }

                stack.Children.Add(fieldStack);
            }
        }

        // Save button
        var saveBtn = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(16, 8, 16, 8),
        };
        var pluginId = plugin.Id;
        saveBtn.Click += async (_, _) =>
        {
            await ViewModel.SavePluginSettingsCommand.ExecuteAsync(pluginId);
        };
        stack.Children.Add(saveBtn);

        card.Child = stack;
        return card;
    }

    // ===== Sessions =====
    private async Task LoadSessionsAsync()
    {
        await ViewModel.LoadSessionsCommand.ExecuteAsync(null);
        RebuildSessionCards();
    }

    private void RebuildSessionCards()
    {
        SessionCardsContainer.Children.Clear();

        if (ViewModel.Sessions.Count == 0)
        {
            SessionCardsContainer.Children.Add(new TextBlock
            {
                Text = "No active sessions found.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        foreach (var session in ViewModel.Sessions)
        {
            SessionCardsContainer.Children.Add(BuildSessionCard(session));
        }
    }

    private Border BuildSessionCard(ContinuumPlayer.Core.Models.Auth.AuthSession session)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = session.IsCurrent
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: session info
        var infoStack = new StackPanel { Spacing = 4 };

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new FontIcon
        {
            Glyph = "\uE7F7",
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        nameRow.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(session.DeviceName) ? "Unknown Device" : session.DeviceName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (session.IsCurrent)
        {
            var currentBadge = new Border
            {
                Background = (Brush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            currentBadge.Child = new TextBlock
            {
                Text = "Current",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentForegroundBrush"],
            };
            nameRow.Children.Add(currentBadge);
        }

        infoStack.Children.Add(nameRow);

        // IP and date
        var detailsText = $"IP: {session.IpAddress}";
        if (!string.IsNullOrEmpty(session.CreatedAt))
        {
            if (DateTime.TryParse(session.CreatedAt, out var created))
                detailsText += $"  |  Created: {created.ToLocalTime():g}";
            else
                detailsText += $"  |  Created: {session.CreatedAt}";
        }

        infoStack.Children.Add(new TextBlock
        {
            Text = detailsText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        Grid.SetColumn(infoStack, 0);
        grid.Children.Add(infoStack);

        // Right: revoke button (not for current session)
        if (!session.IsCurrent)
        {
            var revokeButton = new Button
            {
                Content = "Revoke",
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            revokeButton.Click += async (_, _) =>
            {
                await ViewModel.RevokeSessionCommand.ExecuteAsync(session.Id);
                RebuildSessionCards();
            };
            Grid.SetColumn(revokeButton, 1);
            grid.Children.Add(revokeButton);
        }

        card.Child = grid;
        return card;
    }

    // ===== Helper =====
    private static Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 0xFF;
        byte r, g, b;

        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[..2], 16);
            r = Convert.ToByte(hex[2..4], 16);
            g = Convert.ToByte(hex[4..6], 16);
            b = Convert.ToByte(hex[6..8], 16);
        }
        else if (hex.Length == 6)
        {
            r = Convert.ToByte(hex[..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
        }
        else
        {
            r = g = b = 0;
        }

        return Windows.UI.Color.FromArgb(a, r, g, b);
    }
}
