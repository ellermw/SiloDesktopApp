using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class SetupWizardPage : Page
{
    public SetupWizardViewModel ViewModel { get; }

    public SetupWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<SetupWizardViewModel>();
        this.InitializeComponent();

        ViewModel.SetupCompleted += OnSetupCompleted;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        PopulateLibraryLanguages();
        BuildStepIndicator();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ServerEntry server)
        {
            ViewModel.ServerUrl = server.Url;
        }
        else if (e.Parameter is string serverUrl)
        {
            ViewModel.ServerUrl = serverUrl;
        }

        // B47: Derive the starting step from current server state instead of always 1.
        await ViewModel.DetermineStartingStepAsync();
        await ViewModel.PrepareStepAsync(ViewModel.CurrentStep);
        SyncServerSelections();
        UpdateStepVisibility();
    }

    private void OnSetupCompleted(bool goToAdmin)
    {
        App.MainWindowInstance?.ShowMainNavigation();
        if (goToAdmin)
        {
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<Admin.AdminShellPage>(typeof(Admin.AdminLibrariesPage));
        }
        else
        {
            App.MainWindowInstance?.NavigateToHome();
        }
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.CurrentStep))
        {
            UpdateStepVisibility();
            await ViewModel.PrepareStepAsync(ViewModel.CurrentStep);
            SyncServerSelections();
        }
    }

    private void UpdateStepVisibility()
    {
        var step = ViewModel.CurrentStep;

        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step5IntegrationsPanel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step6DownloadsPanel.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        Step7RecommendationsPanel.Visibility = step == 6 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 7 ? Visibility.Visible : Visibility.Collapsed;
        Step5Panel.Visibility = step == 8 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = step < ViewModel.TotalSteps ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = step == 7 ? "Continue" : "Next";
        FinishButton.Visibility = step == ViewModel.TotalSteps ? Visibility.Visible : Visibility.Collapsed;
        GoToAdminButton.Visibility = step == ViewModel.TotalSteps ? Visibility.Visible : Visibility.Collapsed;

        UpdateStepIndicator();
    }

    private void BuildStepIndicator()
    {
        StepIndicator.Children.Clear();

        var labels = new[] { "Account", "Profile", "Server", "Integrations", "Downloads", "Recs", "Library", "Finish" };

        for (int i = 0; i < ViewModel.TotalSteps; i++)
        {
            var stepNum = i + 1;

            var stepStack = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Dot
            var dot = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var dotText = new TextBlock
            {
                Text = stepNum.ToString(),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            dot.Child = dotText;
            stepStack.Children.Add(dot);

            // Label
            var label = new TextBlock
            {
                Text = labels[i],
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            stepStack.Children.Add(label);

            StepIndicator.Children.Add(stepStack);

            // Connector line (between dots, not after last)
            if (i < ViewModel.TotalSteps - 1)
            {
                var connector = new Border
                {
                    Width = 24,
                    Height = 2,
                    CornerRadius = new CornerRadius(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20), // offset for label space
                };
                StepIndicator.Children.Add(connector);
            }
        }

        UpdateStepIndicator();
    }

    private void UpdateStepIndicator()
    {
        int childIndex = 0;
        for (int i = 0; i < ViewModel.TotalSteps; i++)
        {
            if (childIndex >= StepIndicator.Children.Count) break;

            var stepStack = StepIndicator.Children[childIndex] as StackPanel;
            if (stepStack == null) { childIndex++; continue; }

            var dot = stepStack.Children[0] as Border;
            var label = stepStack.Children.Count > 1 ? stepStack.Children[1] as TextBlock : null;

            var stepNum = i + 1;
            bool isCurrent = stepNum == ViewModel.CurrentStep;
            bool isCompleted = stepNum < ViewModel.CurrentStep;

            if (dot != null)
            {
                if (isCurrent)
                {
                    dot.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
                    if (dot.Child is TextBlock t)
                        t.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"];
                }
                else if (isCompleted)
                {
                    dot.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
                    dot.Opacity = 0.6;
                    if (dot.Child is TextBlock t)
                        t.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"];
                }
                else
                {
                    dot.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"];
                    dot.Opacity = 1.0;
                    if (dot.Child is TextBlock t)
                        t.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
                }
            }

            if (label != null)
            {
                label.Foreground = isCurrent
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            }

            childIndex++;

            // Connector line
            if (i < ViewModel.TotalSteps - 1 && childIndex < StepIndicator.Children.Count)
            {
                var connector = StepIndicator.Children[childIndex] as Border;
                if (connector != null)
                {
                    connector.Background = isCompleted
                        ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
                        : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"];
                }
                childIndex++;
            }
        }
    }

    private void SetupPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = SetupPasswordBox.Password;
    }

    private void SetupConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmPassword = SetupConfirmPasswordBox.Password;
    }

    private async void LibraryType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string tag)
        {
            ViewModel.LibraryType = tag;
            SyncLibraryTypeChoices();
            await ViewModel.LoadLibraryProviderDefaultsAsync();
        }
    }

    private void PopulateLibraryLanguages()
    {
        LibraryMetadataLanguageCombo.Items.Clear();
        foreach (var language in MediaLanguageCatalog.All)
            LibraryMetadataLanguageCombo.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        SelectComboTag(LibraryMetadataLanguageCombo, ViewModel.LibraryMetadataLanguage);
    }

    private void LibraryMetadataLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryMetadataLanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string code)
            ViewModel.LibraryMetadataLanguage = code;
    }

    private void SyncLibraryTypeChoices()
    {
        foreach (var button in LibraryTypeChoices.Items.OfType<ToggleButton>())
            button.IsChecked = string.Equals(button.Tag as string, ViewModel.LibraryType, StringComparison.OrdinalIgnoreCase);
    }

    private void HardwareAccelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HardwareAccelComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.HardwareAccel = tag;
        }
    }

    private void PublicUrlAuthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PublicUrlAuthComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.S3PublicUrlAuth = tag;
        }
    }

    private void SyncServerSelections()
    {
        SelectComboTag(HardwareAccelComboBox, ViewModel.HardwareAccel);
        SelectComboTag(PublicUrlAuthComboBox, ViewModel.S3PublicUrlAuth);
        SelectComboTag(NodeTypeComboBox, ViewModel.NodeType);
        SelectComboTag(LibraryMetadataLanguageCombo, ViewModel.LibraryMetadataLanguage);
        SyncLibraryTypeChoices();
    }

    private static void SelectComboTag(ComboBox combo, string value)
    {
        foreach (var candidate in combo.Items.OfType<ComboBoxItem>())
        {
            if (candidate.Tag is string tag && string.Equals(tag, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = candidate;
                return;
            }
        }
    }

    private void ProviderPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && box.Tag is SetupSubtitleProviderItem provider)
            provider.Password = box.Password;
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SetupSubtitleProviderItem provider)
            await ViewModel.SaveSubtitleProviderAsync(provider);
    }

    private async void TestProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is SetupSubtitleProviderItem provider)
            await ViewModel.TestSubtitleProviderAsync(provider);
    }

    private void RecommendationToken_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            ViewModel.RecommendationsAuthToken = box.Password;
    }

    private void NodeType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string type)
            ViewModel.NodeType = type;
    }

    private void RemovePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            ViewModel.RemoveLibraryPathCommand.Execute(path);
        }
    }

    private void MoveLibraryProviderUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SetupLibraryProviderEntry entry })
            ViewModel.MoveLibraryProvider(entry, -1);
    }

    private void MoveLibraryProviderDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SetupLibraryProviderEntry entry })
            ViewModel.MoveLibraryProvider(entry, 1);
    }

    private async void BrowseLibraryFolders_Click(object sender, RoutedEventArgs e)
    {
        var selected = await BrowseServerFoldersAsync(
            string.IsNullOrWhiteSpace(ViewModel.NewLibraryPath) ? "/" : ViewModel.NewLibraryPath.Trim());
        foreach (var path in selected)
        {
            if (!ViewModel.LibraryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                ViewModel.LibraryPaths.Add(path);
        }
        ViewModel.NewLibraryPath = "";
    }

    private async Task<IReadOnlyList<string>> BrowseServerFoldersAsync(string initialPath)
    {
        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
        var existing = ViewModel.LibraryPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentPath = initialPath.StartsWith('/') ? initialPath : "/";
        var pathBox = new TextBox { Text = currentPath, PlaceholderText = "/mnt/media" };
        var status = new TextBlock { FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap };
        var entries = new StackPanel { Spacing = 3 };
        var up = new Button { Content = "Up" };
        var go = new Button { Content = "Browse" };
        ContentDialog? dialog = null;

        void UpdateButton()
        {
            if (dialog == null) return;
            dialog.PrimaryButtonText = selected.Count == 0
                ? "Use Current Folder"
                : $"Add {selected.Count} Folder{(selected.Count == 1 ? "" : "s")}";
            dialog.IsPrimaryButtonEnabled = selected.Count > 0 || !existing.Contains(currentPath);
        }

        async Task LoadAsync(string path)
        {
            status.Text = "Loading server folders...";
            entries.Children.Clear();
            try
            {
                var response = await api.BrowseFilesystemAsync(path);
                currentPath = response.Path;
                pathBox.Text = currentPath;
                up.Tag = response.Parent;
                up.IsEnabled = response.Parent != response.Path;
                status.Text = response.Entries.Count == 0 ? "No subfolders found here." : "";
                foreach (var entry in response.Entries)
                {
                    var row = new Grid { ColumnSpacing = 6 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var isExisting = existing.Contains(entry.Path);
                    var check = new CheckBox { IsChecked = isExisting || selected.Contains(entry.Path), IsEnabled = !isExisting };
                    check.Checked += (_, _) => { if (!isExisting) selected.Add(entry.Path); UpdateButton(); };
                    check.Unchecked += (_, _) => { selected.Remove(entry.Path); UpdateButton(); };
                    var open = new Button
                    {
                        Content = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 7,
                            Children =
                            {
                                new FontIcon { Glyph = "\uED25", FontSize = 13 },
                                new TextBlock { Text = entry.Name, FontSize = 13 },
                            },
                        },
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    var capturedPath = entry.Path;
                    open.Click += async (_, _) => await LoadAsync(capturedPath);
                    Grid.SetColumn(open, 1);
                    row.Children.Add(check);
                    row.Children.Add(open);
                    entries.Children.Add(row);
                }
                UpdateButton();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        go.Click += async (_, _) => await LoadAsync(pathBox.Text.Trim());
        up.Click += async (_, _) => { if (up.Tag is string parent) await LoadAsync(parent); };
        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(go, 1);
        pathRow.Children.Add(pathBox);
        pathRow.Children.Add(go);
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(new TextBlock { Text = "Select folders or navigate into one.", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(up, 1);
        toolbar.Children.Add(up);
        var content = new StackPanel { Width = 620, Spacing = 9 };
        content.Children.Add(pathRow);
        content.Children.Add(status);
        content.Children.Add(toolbar);
        content.Children.Add(new Border
        {
            Height = 320,
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new ScrollViewer { Content = entries },
        });
        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Browse Library Folders",
            Content = content,
            PrimaryButtonText = "Use Current Folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        await LoadAsync(currentPath);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return [];
        return selected.Count > 0 ? selected.ToList() : [currentPath];
    }
}
