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
    private bool _suppressEvents;

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
        SubtitleBgOpacitySlider.Value = ViewModel.SubBackgroundOpacity * 100;
        SubtitleBgOpacityLabel.Text = $"{(int)(ViewModel.SubBackgroundOpacity * 100)}%";
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
            _ = ViewModel.LoadImportRunsCommand.ExecuteAsync(null);
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
        ViewModel.SubBackgroundOpacity = SubtitleBgOpacitySlider.Value / 100.0;
        SubtitleBgOpacityLabel.Text = $"{(int)SubtitleBgOpacitySlider.Value}%";
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
            byte alpha = (byte)(ViewModel.SubBackgroundOpacity * 255);
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
        if (section.IsCustom)
        {
            var delBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] },
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Padding = new Thickness(6),
            };
            delBtn.Click += (_, _) =>
            {
                ViewModel.RemoveSection(section);
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

    private void HomeSectionsReset_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ResetHomeSectionsCommand.ExecuteAsync(null);
    }

    // ===== Import Handlers =====

    private void ImportSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string source) return;

        ViewModel.ImportSourceType = source;

        // Update button styles
        ImportEmbyBtn.Style = source == "emby"
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : (Style)Application.Current.Resources["SecondaryButtonStyle"];
        ImportJellyfinBtn.Style = source == "jellyfin"
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : (Style)Application.Current.Resources["SecondaryButtonStyle"];
        ImportPlexBtn.Style = source == "plex"
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : (Style)Application.Current.Resources["SecondaryButtonStyle"];

        // Show/hide connection panels
        EmbyConnectionPanel.Visibility = source == "emby" ? Visibility.Visible : Visibility.Collapsed;
        JellyfinConnectionPanel.Visibility = source == "jellyfin" ? Visibility.Visible : Visibility.Collapsed;
        PlexConnectionPanel.Visibility = source == "plex" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void EmbyConnect_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.EmbyEmail = EmbyEmailBox.Text;
        ViewModel.EmbyPassword = EmbyPasswordBox.Password;
        await ViewModel.EmbyConnectCommand.ExecuteAsync(null);

        if (ViewModel.EmbyServers.Count > 0)
        {
            EmbyServerList.Visibility = Visibility.Visible;
            EmbyServerComboBox.Items.Clear();
            foreach (var server in ViewModel.EmbyServers)
            {
                EmbyServerComboBox.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server });
            }
            if (EmbyServerComboBox.Items.Count > 0)
                EmbyServerComboBox.SelectedIndex = 0;
        }
    }

    private async void PlexSignIn_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PlexSignInCommand.ExecuteAsync(null);

        if (ViewModel.PlexServers.Count > 0)
        {
            PlexServerList.Visibility = Visibility.Visible;
            PlexServerComboBox.Items.Clear();
            foreach (var server in ViewModel.PlexServers)
            {
                PlexServerComboBox.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server });
            }
            if (PlexServerComboBox.Items.Count > 0)
                PlexServerComboBox.SelectedIndex = 0;
        }
    }

    private async void StartImport_Click(object sender, RoutedEventArgs e)
    {
        // Set selected server from combo boxes
        if (ViewModel.ImportSourceType == "emby" && EmbyServerComboBox.SelectedItem is ComboBoxItem embyItem)
        {
            ViewModel.SelectedEmbyServer = embyItem.Tag as HistoryImportConnectServer;
        }
        else if (ViewModel.ImportSourceType == "jellyfin")
        {
            ViewModel.JellyfinUrl = JellyfinUrlBox.Text;
            ViewModel.JellyfinUsername = JellyfinUsernameBox.Text;
            ViewModel.JellyfinPassword = JellyfinPasswordBox.Password;
        }
        else if (ViewModel.ImportSourceType == "plex" && PlexServerComboBox.SelectedItem is ComboBoxItem plexItem)
        {
            ViewModel.SelectedPlexServer = plexItem.Tag as PlexServer;
        }

        await ViewModel.StartImportCommand.ExecuteAsync(null);
    }

    private void ImportRuns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildImportRunCards();
    }

    private void RebuildImportRunCards()
    {
        ImportRunsContainer.Children.Clear();

        if (ViewModel.ImportRuns.Count == 0)
        {
            ImportRunsContainer.Children.Add(new TextBlock
            {
                Text = "No import runs yet.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            return;
        }

        foreach (var run in ViewModel.ImportRuns)
        {
            ImportRunsContainer.Children.Add(BuildImportRunCard(run));
        }
    }

    private static Border BuildImportRunCard(HistoryImportRun run)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
        };

        var stack = new StackPanel { Spacing = 4 };

        // Header: source type + status
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = run.SourceType.ToUpperInvariant(),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });

        var statusColor = run.Status switch
        {
            "completed" => (Brush)Application.Current.Resources["AccentBrush"],
            "failed" => (Brush)Application.Current.Resources["ErrorBrush"],
            _ => (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };

        var statusBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
        };
        statusBadge.Child = new TextBlock
        {
            Text = run.Status,
            FontSize = 11,
            Foreground = statusColor,
        };
        headerRow.Children.Add(statusBadge);
        stack.Children.Add(headerRow);

        // Stats
        var statsText = $"Matched: {run.Matched} | Unmatched: {run.Unmatched} | Skipped: {run.Skipped}";
        stack.Children.Add(new TextBlock
        {
            Text = statsText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        // Created date
        if (DateTime.TryParse(run.CreatedAt, out var created))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Started: {created.ToLocalTime():g}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            });
        }

        // Error message if any
        if (!string.IsNullOrEmpty(run.ErrorMessage))
        {
            stack.Children.Add(new TextBlock
            {
                Text = run.ErrorMessage,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        card.Child = stack;
        return card;
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
