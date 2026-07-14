using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Specialized;
using SiloPlayer.ViewModels.Admin;
using Windows.System;
using Windows.UI.Core;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminDevicesPage : Page
{
    public AdminDevicesViewModel ViewModel { get; } = App.Services.GetRequiredService<AdminDevicesViewModel>();
    private bool _settingsSubscribed;

    public AdminDevicesPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_settingsSubscribed)
        {
            ViewModel.Settings.CollectionChanged += Settings_CollectionChanged;
            _settingsSubscribed = true;
        }
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_settingsSubscribed)
        {
            ViewModel.Settings.CollectionChanged -= Settings_CollectionChanged;
            _settingsSubscribed = false;
        }
        ViewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void Settings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildSettingControls();

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.K ||
            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == 0)
            return;

        DeviceSearchBox.Focus(FocusState.Programmatic);
        DeviceSearchBox.SelectAll();
        e.Handled = true;
    }

    private void DeviceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasSearch = !string.IsNullOrEmpty(DeviceSearchBox.Text);
        ClearSearchButton.Visibility = hasSearch ? Visibility.Visible : Visibility.Collapsed;
        SearchShortcutHint.Visibility = hasSearch ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceSearchBox.Text = "";
        DeviceSearchBox.Focus(FocusState.Programmatic);
    }

    private async void DeviceList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AdminDeviceCardViewModel device)
            await ViewModel.SelectDeviceAsync(device);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ViewModel.CloseDetail();

    private void GroupBy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.Tag is not string groupBy) return;
        GroupUserButton.IsChecked = ReferenceEquals(selected, GroupUserButton);
        GroupPlatformButton.IsChecked = ReferenceEquals(selected, GroupPlatformButton);
        GroupActivityButton.IsChecked = ReferenceEquals(selected, GroupActivityButton);
        ViewModel.SetGroupBy(groupBy);
    }

    private void DeviceScope_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.Tag is not string scope) return;
        AllDevicesButton.IsChecked = scope == "all";
        OverridesButton.IsChecked = scope == "overrides";
        ViewModel.OverridesOnly = scope == "overrides";
    }

    private void SavedView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string view })
            ViewModel.SetSavedView(view);
    }

    private void Facet_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string tag } checkBox) return;
        var separator = tag.IndexOf(':');
        if (separator <= 0) return;
        ViewModel.SetFacet(tag[..separator], tag[(separator + 1)..], checkBox.IsChecked == true);
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ClearFacetChecks(this);
        AllDevicesButton.IsChecked = true;
        OverridesButton.IsChecked = false;
        ViewModel.ClearFilters();
    }

    private static void ClearFacetChecks(DependencyObject root)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
            if (child is CheckBox checkBox && checkBox.Tag is string)
                checkBox.IsChecked = false;
            ClearFacetChecks(child);
        }
    }

    private async void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Reset overrides for {ViewModel.SelectedProfile.Name}?",
            Content = "Every device override for this profile will return to its inherited fallback. This cannot be undone.",
            PrimaryButtonText = "Reset all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.ResetProfileAsync();
    }

    private void RebuildSettingControls()
    {
        SettingsHost.Children.Clear();
        foreach (var row in ViewModel.Settings)
            SettingsHost.Children.Add(BuildSettingRow(row));
    }

    private FrameworkElement BuildSettingRow(AdminDeviceSettingRow row)
    {
        var root = new Grid { ColumnSpacing = 18 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = row.Label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        copy.Children.Add(new TextBlock { Text = row.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush") });
        copy.Children.Add(new TextBlock { Text = row.IsOverride ? $"Device override  ·  {row.Updated}" : "Inherited default", FontSize = 10, Foreground = row.IsOverride ? Brush("AccentBrush") : Brush("SecondaryTextBrush") });
        root.Children.Add(copy);

        var editor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement control;
        if (row.Control == "switch")
        {
            var toggle = new ToggleSwitch
            {
                IsOn = row.Value == "true",
                Tag = row,
                OnContent = "",
                OffContent = "",
                MinWidth = 44,
            };
            toggle.Toggled += async (s, _) => await ViewModel.SaveSettingAsync(row, ((ToggleSwitch)s).IsOn ? "true" : "false");
            control = toggle;
        }
        else if (row.Control == "select")
        {
            var combo = new ComboBox { Width = 190, ItemsSource = row.Options, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = row.Value, Tag = row };
            combo.SelectionChanged += async (s, _) =>
            {
                if (((ComboBox)s).SelectedValue is string value && value != row.Value)
                    await ViewModel.SaveSettingAsync(row, value);
            };
            control = combo;
        }
        else
        {
            var box = new TextBox { Width = 190, Text = row.Value, Tag = row, AcceptsReturn = row.Control == "json", TextWrapping = TextWrapping.Wrap };
            box.KeyDown += async (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && !((TextBox)s).AcceptsReturn)
                    await ViewModel.SaveSettingAsync(row, ((TextBox)s).Text);
            };
            box.LostFocus += async (s, _) =>
            {
                var value = ((TextBox)s).Text;
                if (value != row.Value) await ViewModel.SaveSettingAsync(row, value);
            };
            control = box;
        }
        editor.Children.Add(control);
        if (row.IsOverride)
        {
            var reset = new Button { Content = "Reset", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            reset.Click += async (_, _) => await ViewModel.ResetSettingAsync(row);
            editor.Children.Add(reset);
        }
        Grid.SetColumn(editor, 1);
        root.Children.Add(editor);
        return new Border { Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = root };
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
