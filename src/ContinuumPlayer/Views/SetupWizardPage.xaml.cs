using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class SetupWizardPage : Page
{
    public SetupWizardViewModel ViewModel { get; }

    public SetupWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<SetupWizardViewModel>();
        this.InitializeComponent();

        ViewModel.SetupCompleted += OnSetupCompleted;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

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
        UpdateStepVisibility();
    }

    private void OnSetupCompleted()
    {
        // Show main navigation and go to home
        App.MainWindowInstance?.ShowMainNavigation();
        App.MainWindowInstance?.NavigateToHome();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.CurrentStep))
        {
            UpdateStepVisibility();
        }
    }

    private void UpdateStepVisibility()
    {
        var step = ViewModel.CurrentStep;

        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5Panel.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = step < ViewModel.TotalSteps ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = step == ViewModel.TotalSteps ? Visibility.Visible : Visibility.Collapsed;

        UpdateStepIndicator();
    }

    private void BuildStepIndicator()
    {
        StepIndicator.Children.Clear();

        var labels = new[] { "Account", "Profile", "Library", "Server", "Metadata" };

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

    private void LibraryTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.LibraryType = tag;
        }
    }

    private void HardwareAccelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HardwareAccelComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.HardwareAccel = tag;
        }
    }

    private void MetadataProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MetadataProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.SelectedProvider = tag;
        }
    }

    private void RemovePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            ViewModel.RemoveLibraryPathCommand.Execute(path);
        }
    }
}
