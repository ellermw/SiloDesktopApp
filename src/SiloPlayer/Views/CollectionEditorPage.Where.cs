using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private readonly StackPanel _usageRows = new() { Spacing = 8 };
    private readonly TextBlock _unshareNote = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Button _addHomeRow = new() { Content = "Add as a row", MinHeight = 32 };
    private string? _usageTargetsKey;
    private bool _savedShared;
    private CancellationTokenSource? _usageCancellation;

    private Grid CollectionSettingRow(string label, string help, FrameworkElement control)
    {
        // These sections have already moved out of Content, but have not been
        // realized yet. Detach from their logical owner before reparenting.
        RemoveFrom(_where, control);
        Detach(control);
        var row = new Grid { ColumnSpacing = 16, Padding = new(0, 16, 0, 16) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium });
        copy.Children.Add(new TextBlock { Text = help, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") });
        row.Children.Add(copy); Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control); return row;
    }

    private void ConfigureWhereRows()
    {
        _where.Children[1] = CollectionSettingRow("Show to other profiles", "Other profiles on this account can browse it and add it to their Home.", ImportedSharedToggle);
        _where.Children[2] = CollectionSettingRow("Show on the Collections tab", "For you and anyone you share it with", IncludeInServerCollectionsToggle);
        _unshareNote.Foreground = CurrentBrush("SecondaryTextBrush"); _unshareNote.Visibility = Visibility.Collapsed; _where.Children.Add(_unshareNote);
        _where.Children.Add(new TextBlock { Text = "Rows that show it", FontSize = 14, FontWeight = FontWeights.Medium }); _where.Children.Add(_usageRows);
        _addHomeRow.Style = (Style)Application.Current.Resources["OutlineButtonStyle"]; _addHomeRow.HorizontalAlignment = HorizontalAlignment.Left;
        _where.Children.Add(_addHomeRow);
        _usageRows.Children.Add(new TextBlock { Text = "None yet. Add it to your Home or a library page once it's created.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") });
    }

    private void UpdateWhereRows()
    {
        _addHomeRow.Visibility = ViewModel.IsEditing ? Visibility.Visible : Visibility.Collapsed; _addHomeRow.IsEnabled = !ViewModel.IsSaving;
        var others = ViewModel.AvailableProfiles.Where(profile => profile.Id != App.Services.GetRequiredService<AuthService>().SelectedProfileId).Select(profile => profile.Name).ToArray();
        _unshareNote.Visibility = _savedShared && !ViewModel.IsShared && others.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _unshareNote.Text = string.Join(", ", others) + (others.Length == 1 ? " loses" : " lose") + " it, including rows they made from it.";
        var key = ViewModel.CollectionId + "|" + string.Join(",", ViewModel.SelectedLibraryIds.Order()) + "|" + string.Join(",", ViewModel.AvailableLibraries.Select(library => library.Id));
        if (_usageTargetsKey != key)
        {
            _usageTargetsKey = key;
            var menu = new MenuFlyout();
            void Add(string label, int? libraryId)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, _) => { if (ViewModel.CollectionId is { } id) App.Services.GetRequiredService<NavigationService>().Navigate<CustomizeHomePage>(new CustomizeHomeNavigationArgs(id, ViewModel.Name, libraryId)); };
                menu.Items.Add(item);
            }
            Add("My Home", null);
            foreach (var library in ViewModel.AvailableLibraries.Where(library => ViewModel.SelectedLibraryIds.Count == 0 || ViewModel.SelectedLibraryIds.Contains(library.Id))) Add("My " + library.Name + " page", library.Id);
            _addHomeRow.Flyout = menu;
        }
    }

    private async Task LoadUsageRowsAsync(string collectionId)
    {
        _usageCancellation?.Cancel(); _usageCancellation?.Dispose();
        var owner = _usageCancellation = new CancellationTokenSource();
        _usageRows.Children.Clear(); _usageRows.Children.Add(new TextBlock { Text = "Loading rows…", FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush") });
        try
        {
            var rows = await new CollectionHomeRowUsageService(App.Services.GetRequiredService<CatalogApi>(), App.Services.GetRequiredService<SettingsApi>()).GetAsync(collectionId, owner.Token);
            if (!_editorActive || owner.IsCancellationRequested || ViewModel.CollectionId != collectionId) return;
            _usageRows.Children.Clear();
            if (rows.Count == 0) _usageRows.Children.Add(new TextBlock { Text = "None of your Home or library page rows show it yet.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") });
            foreach (var row in rows) _usageRows.Children.Add(new TextBlock { Text = $"{row.PageName} › {row.RowTitle}{(row.Hidden ? " · Hidden" : "")}", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") });
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!_editorActive || owner.IsCancellationRequested || ViewModel.CollectionId != collectionId) return;
            _usageRows.Children.Clear(); _usageRows.Children.Add(new TextBlock { Text = "Couldn't load the rows that show it.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            var retry = new Button { Content = "Retry", Style = (Style)Application.Current.Resources["OutlineButtonStyle"] }; retry.Click += async (_, _) => await LoadUsageRowsAsync(collectionId); _usageRows.Children.Add(retry);
        }
    }
}
