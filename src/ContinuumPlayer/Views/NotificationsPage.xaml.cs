using ContinuumPlayer.Core.Models.Notifications;
using ContinuumPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ContinuumPlayer.Views;

public sealed partial class NotificationsPage : Page
{
    public NotificationsViewModel ViewModel { get; }

    public NotificationsPage()
    {
        ViewModel = App.Services.GetRequiredService<NotificationsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += NotificationsPage_Loaded;
    }

    private async void NotificationsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Notifications.Count == 0)
            await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void NotificationsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppNotification notification)
            await ViewModel.MarkReadCommand.ExecuteAsync(notification);
    }
}
