using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class ServerSelectPage : Page
{
    public ServerSelectViewModel ViewModel { get; }
    private CancellationTokenSource? _connectCancellation;
    private bool _isConnecting;

    public ServerSelectPage()
    {
        ViewModel = App.Services.GetRequiredService<ServerSelectViewModel>();
        this.InitializeComponent();

        // Update empty state visibility when servers change
        ViewModel.Servers.CollectionChanged += (_, _) => UpdateEmptyState();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ServerSelectViewModel.IsAddingServer))
                UpdateEmptyState();
        };
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = ViewModel.Servers.Count == 0 && !ViewModel.IsAddingServer
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void ConnectServer_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnecting || sender is not Button button || button.Tag is not ServerEntry server)
            return;

        _isConnecting = true;
        IsHitTestVisible = false;
        _connectCancellation?.Cancel();
        _connectCancellation?.Dispose();
        _connectCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            ViewModel.SelectServerCommand.Execute(server);

            var authService = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>();
            authService.ConfigureServer(server.Url);

            var authApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AuthApi>();
            var setup = await authApi.GetSetupStatusAsync(_connectCancellation.Token);
            var nav = App.Services.GetRequiredService<NavigationService>();
            if (setup.NeedsSetup)
                nav.Navigate<ServerSetupRequiredPage>(server);
            else
                nav.Navigate<LoginPage>(server);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(Frame?.Content, this))
                ViewModel.ErrorMessage = $"Connection to {server.Name} timed out.";
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Couldn't connect to {server.Name}: {ex.Message}";
        }
        finally
        {
            _isConnecting = false;
            IsHitTestVisible = true;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _connectCancellation?.Cancel();
        _connectCancellation?.Dispose();
        _connectCancellation = null;
        base.OnNavigatedFrom(e);
    }

    private void RemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ServerEntry server)
        {
            ViewModel.RemoveServerCommand.Execute(server);
        }
    }
}
