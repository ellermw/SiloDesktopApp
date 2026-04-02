using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminSubtitleProvidersPage : Page
{
    public AdminSubtitleProvidersViewModel ViewModel { get; }
    private bool _rebuildPending;

    public AdminSubtitleProvidersPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminSubtitleProvidersViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Providers.CollectionChanged += (_, _) => ScheduleRebuild();
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildPending = false; RebuildCards(); });
    }

    private void RebuildCards()
    {
        ProvidersPanel.Children.Clear();
        if (ViewModel.Providers.Count == 0) { EmptyState.Visibility = Visibility.Visible; return; }
        EmptyState.Visibility = Visibility.Collapsed;

        foreach (var provider in ViewModel.Providers)
        {
            ProvidersPanel.Children.Add(BuildProviderCard(provider));
        }
    }

    private FrameworkElement BuildProviderCard(SubtitleProviderConfig provider)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(20, 16, 20, 16)
        };

        var layout = new StackPanel { Spacing = 12 };

        // Header row: name + status indicators
        var headerRow = new Grid { ColumnSpacing = 12 };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        nameRow.Children.Add(new TextBlock
        {
            Text = provider.ProviderName,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        if (provider.Enabled)
            nameRow.Children.Add(MakeBadge("Enabled", Color.FromArgb(40, 34, 197, 94), Color.FromArgb(255, 34, 197, 94)));
        else
            nameRow.Children.Add(MakeBadge("Disabled", Color.FromArgb(40, 120, 120, 120), Color.FromArgb(255, 160, 160, 160)));

        if (provider.HasApiKey)
            nameRow.Children.Add(MakeBadge("API Key", Color.FromArgb(40, 59, 130, 246), Color.FromArgb(255, 96, 165, 250)));

        if (provider.HasCredentials)
            nameRow.Children.Add(MakeBadge("Credentials", Color.FromArgb(40, 59, 130, 246), Color.FromArgb(255, 96, 165, 250)));

        Grid.SetColumn(nameRow, 0);
        headerRow.Children.Add(nameRow);

        // Test button
        var capturedProvider = provider;
        var testBtn = new Button
        {
            Content = "Test",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        testBtn.Click += async (_, _) =>
        {
            testBtn.IsEnabled = false;
            testBtn.Content = "Testing...";
            var result = await ViewModel.TestProviderAsync(capturedProvider.ProviderName);
            testBtn.Content = result?.Success == true ? "Passed" : "Failed";
            if (result != null) ShowStatus(result.Success ? "Test passed." : $"Test failed: {result.Error}");
            await Task.Delay(2000);
            testBtn.Content = "Test";
            testBtn.IsEnabled = true;
        };
        Grid.SetColumn(testBtn, 1);
        headerRow.Children.Add(testBtn);
        layout.Children.Add(headerRow);

        // Config fields
        var fieldsGrid = new Grid { ColumnSpacing = 12 };
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var enabledToggle = new ToggleSwitch { IsOn = provider.Enabled, Header = "Enabled" };
        Grid.SetColumn(enabledToggle, 0);
        fieldsGrid.Children.Add(enabledToggle);

        var apiKeyBox = new PasswordBox
        {
            PlaceholderText = provider.HasApiKey ? "(set)" : "Enter API key",
            CornerRadius = new CornerRadius(8), FontSize = 13
        };
        var apiKeyGroup = new StackPanel { Spacing = 4 };
        apiKeyGroup.Children.Add(new TextBlock
        {
            Text = "API Key", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        apiKeyGroup.Children.Add(apiKeyBox);
        Grid.SetColumn(apiKeyGroup, 1);
        fieldsGrid.Children.Add(apiKeyGroup);

        var usernameBox = new TextBox
        {
            PlaceholderText = provider.HasCredentials ? "(set)" : "Username",
            CornerRadius = new CornerRadius(8), FontSize = 13
        };
        var passwordBox = new PasswordBox
        {
            PlaceholderText = provider.HasCredentials ? "(set)" : "Password",
            CornerRadius = new CornerRadius(8), FontSize = 13
        };
        var credGroup = new StackPanel { Spacing = 4 };
        credGroup.Children.Add(new TextBlock
        {
            Text = "Credentials", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        credGroup.Children.Add(usernameBox);
        credGroup.Children.Add(passwordBox);
        Grid.SetColumn(credGroup, 2);
        fieldsGrid.Children.Add(credGroup);

        layout.Children.Add(fieldsGrid);

        // Save button
        var saveBtn = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        saveBtn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = "\uE74E", FontSize = 12 },
                new TextBlock { Text = "Save" }
            }
        };
        saveBtn.Click += async (_, _) =>
        {
            saveBtn.IsEnabled = false;
            var request = new SubtitleProviderUpdateRequest
            {
                Enabled = enabledToggle.IsOn,
                ApiKey = string.IsNullOrWhiteSpace(apiKeyBox.Password) ? null : apiKeyBox.Password,
                Username = string.IsNullOrWhiteSpace(usernameBox.Text) ? null : usernameBox.Text,
                Password = string.IsNullOrWhiteSpace(passwordBox.Password) ? null : passwordBox.Password
            };
            await ViewModel.UpdateProviderAsync(capturedProvider.ProviderName, request);
            ShowStatus($"Provider \"{capturedProvider.ProviderName}\" saved.");
            saveBtn.IsEnabled = true;
        };
        layout.Children.Add(saveBtn);

        card.Child = layout;
        return card;
    }

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
        };
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { StatusBanner.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }
}
