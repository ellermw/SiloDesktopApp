using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAccessGroupsPage : Page
{
    public AdminAccessGroupsViewModel ViewModel { get; } =
        App.Services.GetRequiredService<AdminAccessGroupsViewModel>();

    public AdminAccessGroupsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginCreate();
        NewGroupNameBox.Focus(FocusState.Programmatic);
    }

    private void CancelCreate_Click(object sender, RoutedEventArgs e) => ViewModel.CancelCreate();

    private async void NewGroupNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;
        e.Handled = true;
        await ViewModel.CreateAsync();
    }

    private void GroupGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AccessGroupCardViewModel group)
            ViewModel.SelectGroup(group);
    }

    private void AllGroups_Click(object sender, RoutedEventArgs e) => ViewModel.CloseEditor();

    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is not { } selected || !ViewModel.CanDelete)
            return;

        var memberMessage = selected.Source.MemberCount > 0
            ? $"{selected.Source.MemberCount} {(selected.Source.MemberCount == 1 ? "member" : "members")} will move to no group and fall back to the built-in defaults. Their own restrictions are unchanged."
            : "This group has no members. This can't be undone.";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete “{selected.Name}”?",
            Content = memberMessage,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteSelectedAsync();
    }
}
