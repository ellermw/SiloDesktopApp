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

    // Matches web SUBTITLE_PROVIDER_NAMES map in IntegrationsSettings.tsx
    private static readonly Dictionary<string, string> ProviderDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opensubtitles"] = "OpenSubtitles",
        ["subdl"] = "SubDL",
        ["subsource"] = "SubSource",
    };

    // Matches web SUBTITLE_PROVIDER_ORDER constant
    private static readonly List<string> ProviderOrder = new() { "opensubtitles", "subdl", "subsource" };

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

        // Sort by known order, putting unknown providers at end (matches web)
        var sorted = ViewModel.Providers.ToList();
        sorted.Sort((a, b) =>
        {
            int ai = ProviderOrder.IndexOf(a.ProviderName?.ToLowerInvariant() ?? "");
            int bi = ProviderOrder.IndexOf(b.ProviderName?.ToLowerInvariant() ?? "");
            if (ai == -1 && bi == -1) return 0;
            if (ai == -1) return 1;
            if (bi == -1) return -1;
            return ai - bi;
        });

        foreach (var provider in sorted)
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

        bool isOpenSubtitles = string.Equals(provider.ProviderName, "opensubtitles", StringComparison.OrdinalIgnoreCase);
        string displayName = ProviderDisplayNames.TryGetValue(provider.ProviderName ?? "", out var dn) ? dn : provider.ProviderName ?? "";

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        nameRow.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });

        // Configured/Not-configured status matches web SubtitleCredentialStatus
        bool configured = isOpenSubtitles ? provider.HasCredentials : provider.HasApiKey;
        if (configured)
            nameRow.Children.Add(MakeBadge("Configured", Color.FromArgb(40, 34, 197, 94), Color.FromArgb(255, 34, 197, 94)));
        else
            nameRow.Children.Add(MakeBadge("Not configured", Color.FromArgb(40, 234, 179, 8), Color.FromArgb(255, 234, 179, 8)));

        if (provider.Enabled)
            nameRow.Children.Add(MakeBadge("Enabled", Color.FromArgb(40, 59, 130, 246), Color.FromArgb(255, 96, 165, 250)));
        else
            nameRow.Children.Add(MakeBadge("Disabled", Color.FromArgb(40, 120, 120, 120), Color.FromArgb(255, 160, 160, 160)));

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

        // Enabled toggle (shown above credential fields to match web layout)
        var enabledToggle = new ToggleSwitch { IsOn = provider.Enabled, Header = "Enabled" };
        layout.Children.Add(enabledToggle);

        // Credential fields — OpenSubtitles uses username/password, others use API key
        var apiKeyBox = new PasswordBox
        {
            PlaceholderText = provider.HasApiKey ? "Leave blank to keep current" : "Enter API key",
            CornerRadius = new CornerRadius(8), FontSize = 13
        };
        var usernameBox = new TextBox
        {
            PlaceholderText = provider.HasCredentials ? "Leave blank to keep current" : "OpenSubtitles username",
            CornerRadius = new CornerRadius(8), FontSize = 13
        };
        var passwordBox = new PasswordBox
        {
            PlaceholderText = provider.HasCredentials ? "Leave blank to keep current" : "OpenSubtitles password",
            CornerRadius = new CornerRadius(8), FontSize = 13
        };

        if (isOpenSubtitles)
        {
            var userGroup = new StackPanel { Spacing = 4 };
            userGroup.Children.Add(new TextBlock
            {
                Text = "Username", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
            userGroup.Children.Add(usernameBox);
            layout.Children.Add(userGroup);

            var passGroup = new StackPanel { Spacing = 4 };
            passGroup.Children.Add(new TextBlock
            {
                Text = "Password", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
            passGroup.Children.Add(passwordBox);
            layout.Children.Add(passGroup);
        }
        else
        {
            var apiKeyGroup = new StackPanel { Spacing = 4 };
            apiKeyGroup.Children.Add(new TextBlock
            {
                Text = "API Key", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
            apiKeyGroup.Children.Add(apiKeyBox);
            layout.Children.Add(apiKeyGroup);
        }

        // Actions row — Test + Save buttons side-by-side at left (matches web)
        var actionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var saveBtn = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(16, 8, 16, 8),
            Content = new TextBlock { Text = "Save" }
        };
        saveBtn.Click += async (_, _) =>
        {
            saveBtn.IsEnabled = false;
            var request = new SubtitleProviderUpdateRequest
            {
                Enabled = enabledToggle.IsOn,
                ApiKey = isOpenSubtitles || string.IsNullOrWhiteSpace(apiKeyBox.Password) ? null : apiKeyBox.Password,
                Username = !isOpenSubtitles || string.IsNullOrWhiteSpace(usernameBox.Text) ? null : usernameBox.Text,
                Password = !isOpenSubtitles || string.IsNullOrWhiteSpace(passwordBox.Password) ? null : passwordBox.Password
            };
            await ViewModel.UpdateProviderAsync(capturedProvider.ProviderName, request);
            ShowStatus($"Provider \"{displayName}\" saved.");
            saveBtn.IsEnabled = true;
        };

        // Move test button out of header — web shows it in actions row next to Save
        actionsRow.Children.Add(saveBtn);
        layout.Children.Add(actionsRow);

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
