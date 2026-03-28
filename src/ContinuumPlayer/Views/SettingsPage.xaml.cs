using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Services;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private bool _suppressEvents;

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        this.InitializeComponent();
        PopulateThemeComboBox();
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
