using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
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
        PopulateThemeComboBox();

        ViewModel.LibraryCards.CollectionChanged += LibraryCards_CollectionChanged;
    }

    private void PopulateThemeComboBox()
    {
        var themeService = App.Services.GetRequiredService<ThemeService>();
        foreach (var themeId in themeService.AvailableThemeIds)
        {
            ThemeComboBox.Items.Add(new ComboBoxItem
            {
                Content = ThemeService.GetDisplayName(themeId),
                Tag = themeId
            });
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        await ViewModel.LoadCommand.ExecuteAsync(null);
        SyncComboBoxes();
    }

    private void SyncComboBoxes()
    {
        _suppressEvents = true;

        SelectComboBoxByTag(ThemeComboBox, ViewModel.UiTheme);
        SelectComboBoxByTag(QualityComboBox, ViewModel.QualityPreference);
        SelectComboBoxByTag(MaxQualityComboBox, ViewModel.MaxPlaybackQuality);
        SelectComboBoxByTag(SubtitleLanguageComboBox, ViewModel.SubtitleLanguage);
        SelectComboBoxByTag(SubtitleModeComboBox, ViewModel.SubtitleMode);
        SelectComboBoxByTag(NextUpModeComboBox, ViewModel.NextUpMode);

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
            tab.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            tab.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            tab.BorderThickness = new Thickness(0);
        }

        clickedButton.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        clickedButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        clickedButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        clickedButton.BorderThickness = new Thickness(0, 0, 0, 2);

        AppearancePanel.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PlaybackPanel.Visibility = tag == "Playback" ? Visibility.Visible : Visibility.Collapsed;
        LibrariesPanel.Visibility = tag == "Libraries" ? Visibility.Visible : Visibility.Collapsed;
        SubtitlesPanel.Visibility = tag == "Subtitles" ? Visibility.Visible : Visibility.Collapsed;
        HomeScreenPanel.Visibility = tag == "HomeScreen" ? Visibility.Visible : Visibility.Collapsed;
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
            LibraryCardsContainer.Children.Add(BuildLibraryCard(card));
        }
    }

    private Border BuildLibraryCard(LibraryCardViewModel vm)
    {
        var cardBorder = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(20),
        };

        var outerStack = new StackPanel { Spacing = 12 };

        // === Row 1: Name + type badge + custom badge ===
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        headerRow.Children.Add(new TextBlock
        {
            Text = vm.LibraryName,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Type badge
        var typeBadge = new Border
        {
            Style = (Style)Application.Current.Resources["BadgeStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        typeBadge.Child = new TextBlock
        {
            Text = vm.LibraryType,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeTextBrush"],
            FontSize = 11,
        };
        headerRow.Children.Add(typeBadge);

        // Custom badge (bound to HasCustomOverrides)
        var customBadge = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkOrange),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = vm.HasCustomOverrides ? Visibility.Visible : Visibility.Collapsed,
        };
        customBadge.Child = new TextBlock
        {
            Text = "Custom",
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            FontSize = 11,
        };
        headerRow.Children.Add(customBadge);

        // Keep a reference so we can update visibility when HasCustomOverrides changes
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LibraryCardViewModel.HasCustomOverrides))
            {
                customBadge.Visibility = vm.HasCustomOverrides ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        outerStack.Children.Add(headerRow);

        // === Row 2: Summary text ===
        var summaryText = new TextBlock
        {
            Text = vm.SummaryText,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
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
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
            Padding = new Thickness(0, 4, 0, 4),
            FontSize = 13,
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
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            Margin = new Thickness(0, 4, 0, 0),
        };
        resetButton.Click += (s, e) => vm.ResetToDefaultsCommand.Execute(null);

        // When reset happens, update all combos in this card
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
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
        });

        var combo = new ComboBox { Width = 300 };
        foreach (var (tag, lbl) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = lbl, Tag = tag });
        }

        // Select current value
        SelectComboBoxByTag(combo, currentValue);

        // Suppress during initial selection
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

    /// <summary>Re-syncs the combo boxes inside the expand panel when the VM values change (e.g., after reset).</summary>
    private static void SyncExpandPanelCombos(StackPanel expandPanel, LibraryCardViewModel vm)
    {
        // The expand panel has: [spoken row, subtitle row, mode row, forced row, reset button]
        // Each row is a StackPanel with [TextBlock, ComboBox]
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
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.UiTheme = val;
            _ = ViewModel.SaveUiThemeCommand.ExecuteAsync(null);
        }
    }

    private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (QualityComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.QualityPreference = val;
            _ = ViewModel.SaveQualityPreferenceCommand.ExecuteAsync(null);
        }
    }

    private void MaxQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (MaxQualityComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.MaxPlaybackQuality = val;
            _ = ViewModel.SaveMaxPlaybackQualityCommand.ExecuteAsync(null);
        }
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
}
