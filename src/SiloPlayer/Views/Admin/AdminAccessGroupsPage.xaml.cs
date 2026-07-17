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
        NavigationCacheMode = NavigationCacheMode.Enabled;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout();
        await ViewModel.LoadAsync();
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var width = ActualWidth;
        var compact = width < 640;
        var side = compact ? 16 : width < 1024 ? 24 : 32;
        AccessGroupsPageShell.Padding = new Thickness(side, compact ? 16 : 24, side, 48);

        Grid.SetColumn(NewGroupButton, compact ? 0 : 1);
        Grid.SetRow(NewGroupButton, compact ? 1 : 0);
        NewGroupButton.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        CreateGroupPanel.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        NewGroupNameBox.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        ConfigureTwoColumnGrid(GroupIdentityGrid, compact);
        ConfigureTwoColumnGrid(StreamLimitsGrid, compact);
        Grid.SetColumn(SaveGroupButton, compact ? 0 : 1);
        Grid.SetRow(SaveGroupButton, compact ? 1 : 0);
        SaveGroupButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        if (GroupsGridView.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
        {
            var available = Math.Max(280, GroupsGridView.ActualWidth);
            var columns = width >= 1200 ? 3 : width >= 720 ? 2 : 1;
            wrapGrid.MaximumRowsOrColumns = columns;
            wrapGrid.ItemWidth = Math.Max(280, Math.Floor(available / columns) - 14);
        }
    }

    private static void ConfigureTwoColumnGrid(Grid grid, bool compact)
    {
        if (grid.Children.Count < 2) return;
        var first = (FrameworkElement)grid.Children[0];
        var second = (FrameworkElement)grid.Children[1];
        Grid.SetColumnSpan(first, compact ? 2 : 1);
        Grid.SetColumn(second, compact ? 0 : 1);
        Grid.SetRow(second, compact ? 1 : 0);
        Grid.SetColumnSpan(second, compact ? 2 : 1);
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
