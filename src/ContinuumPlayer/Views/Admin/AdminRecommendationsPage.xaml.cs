using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminRecommendationsPage : Page
{
    public AdminRecommendationsViewModel ViewModel { get; }

    public AdminRecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminRecommendationsViewModel>();
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Page is static — no data to load
    }

    // ===== Button click handlers =====

    private async void RunEmbeddingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(
            RunEmbeddingsButton,
            RunEmbeddingsText,
            ViewModel.RunEmbeddingsCommand);
    }

    private async void RunTasteProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(
            RunTasteProfilesButton,
            RunTasteProfilesText,
            ViewModel.RunTasteProfilesCommand);
    }

    private async void RunCowatchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(
            RunCowatchButton,
            RunCowatchText,
            ViewModel.RunCowatchCommand);
    }

    private async void RunRecommendationsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunJobAsync(
            RunRecommendationsButton,
            RunRecommendationsText,
            ViewModel.RunRecommendationsCommand);
    }

    // ===== Helpers =====

    private async Task RunJobAsync(
        Button button,
        TextBlock label,
        CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
    {
        button.IsEnabled = false;
        label.Text = "Running...";

        try
        {
            await command.ExecuteAsync(null);
            if (ViewModel.StatusMessage != null)
                ShowStatus(ViewModel.StatusMessage);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
            label.Text = "Run";
        }
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
