using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
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

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        this.InitializeComponent();

        ViewModel.LibraryCards.CollectionChanged += LibraryCards_CollectionChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        await ViewModel.LoadCommand.ExecuteAsync(null);
        SyncComboBoxes();
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

        // Spoken language is stored on profile.Language
        // We'll try to set it from the loaded profile data
        // For now, select "en" as default if no specific language is set
        SelectComboBoxByTag(SpokenLanguageComboBox, "en");

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

        var tabs = new[] { AppearanceTab, PlaybackTab, LibrariesTab, SubtitlesTab, HomeScreenTab };
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
    }

    // ===== Theme cards =====
    private void BuildThemeCards()
    {
        ThemeCardsContainer.Items.Clear();

        var themeService = App.Services.GetRequiredService<ThemeService>();
        var currentTheme = themeService.CurrentTheme;

        // Theme descriptions matching the web UI
        var themeDescriptions = new Dictionary<string, string>
        {
            ["midnight-cinema"] = "Monochromatic cinema -- content is the color",
            ["cinema-light"] = "Light monochromatic cinema -- content is the color",
            ["cobalt-studio"] = "Cool blue graphite with crisp contrast",
            ["oxblood-noir"] = "Deep red-black with restrained luxury warmth",
            ["ember-slate"] = "Smoked charcoal with ember-red accents",
            ["evergreen-studio"] = "Refined evergreen accents on dense graphite",
            ["verdant-ink"] = "Cool green-black with softer luminous contrast",
            ["catppuccin"] = "Pastel purple on warm dark blue",
            ["gruvbox"] = "Warm retro with golden accent",
            ["void-space"] = "Cool blue on deep space black",
            ["charcoal-studio"] = "Apple-inspired blue on dark gray",
            ["graphite-pro"] = "Vibrant purple on zinc",
            ["obsidian-depth"] = "Cyan accent on true dark",
        };

        // Theme accent colors for the swatch
        var themeAccents = new Dictionary<string, string>
        {
            ["midnight-cinema"] = "#E8E8EC",
            ["cinema-light"] = "#1A1A1E",
            ["cobalt-studio"] = "#78AEFC",
            ["oxblood-noir"] = "#D16A78",
            ["ember-slate"] = "#F07B62",
            ["evergreen-studio"] = "#5BC39D",
            ["verdant-ink"] = "#86D4B6",
            ["catppuccin"] = "#CBA6F7",
            ["gruvbox"] = "#FABD2F",
            ["void-space"] = "#58A6FF",
            ["charcoal-studio"] = "#0A84FF",
            ["graphite-pro"] = "#A855F7",
            ["obsidian-depth"] = "#00D4AA",
        };

        // Theme background colors for mini preview
        var themeBgs = new Dictionary<string, string>
        {
            ["midnight-cinema"] = "#141417",
            ["cinema-light"] = "#F4F4F6",
            ["cobalt-studio"] = "#101722",
            ["oxblood-noir"] = "#171113",
            ["ember-slate"] = "#151213",
            ["evergreen-studio"] = "#101715",
            ["verdant-ink"] = "#0D1513",
            ["catppuccin"] = "#1E1E2E",
            ["gruvbox"] = "#282828",
            ["void-space"] = "#0D1117",
            ["charcoal-studio"] = "#1C1C1E",
            ["graphite-pro"] = "#18181B",
            ["obsidian-depth"] = "#0F0F0F",
        };

        // Build a two-column grid of theme cards
        var wrapGrid = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 330,
            ItemHeight = 190,
            MaximumRowsOrColumns = 2,
        };

        foreach (var themeId in themeService.AvailableThemeIds)
        {
            var isActive = themeId == currentTheme;
            var displayName = ThemeService.GetDisplayName(themeId);
            var description = themeDescriptions.GetValueOrDefault(themeId, "");
            var accentHex = themeAccents.GetValueOrDefault(themeId, "#78AEFC");
            var bgHex = themeBgs.GetValueOrDefault(themeId, "#101722");

            var card = BuildThemeCard(themeId, displayName, description, accentHex, bgHex, isActive);
            ThemeCardsContainer.Items.Add(card);
        }

        // Use a WrapGrid panel template
        ThemeCardsContainer.ItemsPanel = null; // reset first
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
        CurrentThemeName.Text = ThemeService.GetDisplayName(themeService.CurrentTheme);

        var descriptions = new Dictionary<string, string>
        {
            ["midnight-cinema"] = "Monochromatic cinema -- content is the color",
            ["cinema-light"] = "Light monochromatic cinema -- content is the color",
            ["cobalt-studio"] = "Cool blue graphite with crisp contrast",
            ["oxblood-noir"] = "Deep red-black with restrained luxury warmth",
            ["ember-slate"] = "Smoked charcoal with ember-red accents",
            ["evergreen-studio"] = "Refined evergreen accents on dense graphite",
            ["verdant-ink"] = "Cool green-black with softer luminous contrast",
            ["catppuccin"] = "Pastel purple on warm dark blue",
            ["gruvbox"] = "Warm retro with golden accent",
            ["void-space"] = "Cool blue on deep space black",
            ["charcoal-studio"] = "Apple-inspired blue on dark gray",
            ["graphite-pro"] = "Vibrant purple on zinc",
            ["obsidian-depth"] = "Cyan accent on true dark",
        };
        CurrentThemeDescription.Text = descriptions.GetValueOrDefault(themeService.CurrentTheme, "");
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
        // Web: surface-panel rounded-[1.5rem] border-0
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

        // Type badge (web: variant="outline")
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

        // Visibility toggle (web: "hidden on navigation" label + toggle switch on right)
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
        // Spoken language changes are saved via profile update
        // For now, this is a visual placeholder -- the web UI saves via profile.language
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
    private void SubtitleFontSize_Changed(object sender, SelectionChangedEventArgs e) { }
    private void SubtitleFontFamily_Changed(object sender, SelectionChangedEventArgs e) { }
    private void SubtitleBgStyle_Changed(object sender, SelectionChangedEventArgs e) { }
    private void SubtitleSave_Click(object sender, RoutedEventArgs e) { }
    private void SubtitleReset_Click(object sender, RoutedEventArgs e) { }

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
