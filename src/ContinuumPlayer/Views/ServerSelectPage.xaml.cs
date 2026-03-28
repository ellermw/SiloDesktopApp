using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class ServerSelectPage : Page
{
    public ServerSelectViewModel ViewModel { get; }

    public ServerSelectPage()
    {
        ViewModel = App.Services.GetRequiredService<ServerSelectViewModel>();
        this.InitializeComponent();

        // Update empty state visibility when servers change
        ViewModel.Servers.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = ViewModel.Servers.Count == 0 && !ViewModel.IsAddingServer
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ConnectServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ServerEntry server)
        {
            ViewModel.SelectServerCommand.Execute(server);

            // Set the API client base URL to the selected server
            var apiClient = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.ContinuumApiClient>();
            apiClient.SetBaseUrl(server.Url);

            // Navigate to login page with server info
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<LoginPage>(server);
        }
    }

    private void RemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ServerEntry server)
        {
            ViewModel.RemoveServerCommand.Execute(server);
        }
    }
}
