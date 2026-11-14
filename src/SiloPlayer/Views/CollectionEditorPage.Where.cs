using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private readonly StackPanel _usageRows = new() { Spacing = 8 };
    private readonly TextBlock _unshareNote = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Button _addHomeRow = CollectionText.KeyedContent(new Button { Content = "Add as a row", MinHeight = 32 }, "web.collections.copy.addAsARow");
    // The last read of the rows that show this collection, so a language change can draw it again without reading.
    private IReadOnlyList<CollectionHomeRowUsage>? _usageList;
    private bool _usageLoading, _usageFailed;
    private string? _usageCollectionId;
    private string? _usageTargetsKey;
    private bool _savedShared;
    private CancellationTokenSource? _usageCancellation;

    private Grid CollectionSettingRow(string labelKey, string label, string? helpKey, string help, FrameworkElement control)
    {
        // These sections have already moved out of Content, but have not been
        // realized yet. Detach from their logical owner before reparenting.
        RemoveFrom(_where, control);
        Detach(control);
        var row = new Grid { ColumnSpacing = 16, Padding = new(0, 16, 0, 16) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(CollectionText.Keyed(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium }, labelKey));
        var helpText = new TextBlock { Text = help, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") };
        copy.Children.Add(helpKey == null ? helpText : CollectionText.Keyed(helpText, helpKey));
        row.Children.Add(copy); Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control); return row;
    }

    private void ConfigureWhereRows()
    {
        // The sharing help has no catalog sentence with this meaning ("browse it and add it to their Home"), so it stays English.
        _where.Children[1] = CollectionSettingRow("web.collections.copy.showToOtherProfilesLabel", "Show to other profiles", null, "Other profiles on this account can browse it and add it to their Home.", ImportedSharedToggle);
        _where.Children[2] = CollectionSettingRow("web.collections.copy.showOnTabLabel", "Show on the Collections tab", "web.collections.copy.personalTabHelp", "For you and anyone you share it with", IncludeInServerCollectionsToggle);
        _unshareNote.Foreground = CurrentBrush("SecondaryTextBrush"); _unshareNote.Visibility = Visibility.Collapsed; _where.Children.Add(_unshareNote);
        _where.Children.Add(CollectionText.Keyed(new TextBlock { Text = "Rows that show it", FontSize = 14, FontWeight = FontWeights.Medium }, "web.collections.copy.rowsThatShowIt")); _where.Children.Add(_usageRows);
        _addHomeRow.Style = (Style)Application.Current.Resources["OutlineButtonStyle"]; _addHomeRow.HorizontalAlignment = HorizontalAlignment.Left;
        _where.Children.Add(_addHomeRow);
        RenderUsageRows();
    }

    private void UpdateWhereRows()
    {
        _addHomeRow.Visibility = ViewModel.IsEditing ? Visibility.Visible : Visibility.Collapsed; _addHomeRow.IsEnabled = !ViewModel.IsSaving;
        var others = ViewModel.AvailableProfiles.Where(profile => profile.Id != App.Services.GetRequiredService<AuthService>().SelectedProfileId).Select(profile => profile.Name).ToArray();
        _unshareNote.Visibility = _savedShared && !ViewModel.IsShared && others.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var names = CollectionsPage.JoinNames(others);
        _unshareNote.Text = CollectionText.Get("web.collections.copy.unshareConsequence", names + (others.Length == 1 ? " loses" : " lose") + " it, including rows they made from it.", ("count", others.Length), ("names", names));
        var key = SiloPlayer.Core.Localization.AppStrings.Current.Generation + "|" + ViewModel.CollectionId + "|" + string.Join(",", ViewModel.SelectedLibraryIds.Order()) + "|" + string.Join(",", ViewModel.AvailableLibraries.Select(library => library.Id));
        if (_usageTargetsKey != key)
        {
            _usageTargetsKey = key;
            var menu = CollectionText.Direct(new MenuFlyout());
            void Add(string label, int? libraryId)
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, _) => { if (ViewModel.CollectionId is { } id) App.Services.GetRequiredService<NavigationService>().Navigate<CustomizeHomePage>(new CustomizeHomeNavigationArgs(id, ViewModel.Name, libraryId)); };
                menu.Items.Add(item);
            }
            Add(CollectionText.Get("web.collections.rows.myHome", "My Home"), null);
            foreach (var library in ViewModel.AvailableLibraries.Where(library => ViewModel.SelectedLibraryIds.Count == 0 || ViewModel.SelectedLibraryIds.Contains(library.Id)))
                Add(CollectionText.Get("web.collections.rows.myLibraryPage", "My " + library.Name + " page", ("library", library.Name)), library.Id);
            _addHomeRow.Flyout = menu;
        }
    }

    private async Task LoadUsageRowsAsync(string collectionId)
    {
        _usageCancellation?.Cancel(); _usageCancellation?.Dispose();
        var owner = _usageCancellation = new CancellationTokenSource();
        _usageCollectionId = collectionId; _usageList = null; _usageFailed = false; _usageLoading = true; RenderUsageRows();
        try
        {
            var rows = await new CollectionHomeRowUsageService(App.Services.GetRequiredService<CatalogApi>(), App.Services.GetRequiredService<SettingsApi>()).GetAsync(collectionId, owner.Token);
            if (!_editorActive || owner.IsCancellationRequested || ViewModel.CollectionId != collectionId) return;
            _usageLoading = false; _usageList = rows; RenderUsageRows();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!_editorActive || owner.IsCancellationRequested || ViewModel.CollectionId != collectionId) return;
            _usageLoading = false; _usageFailed = true; RenderUsageRows();
        }
    }

    /// <summary>The rows that show this collection, from the last read, in the interface language. Page and row names stay as saved.</summary>
    private void RenderUsageRows()
    {
        _usageRows.Children.Clear();
        TextBlock Line(string text, double size = 12.5) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") };
        if (_usageCollectionId == null)
            _usageRows.Children.Add(Line(CollectionText.Get("web.collections.copy.myRowsOnceCreated", "None yet. Add it to your Home or a library page once it's created.")));
        else if (_usageLoading)
            _usageRows.Children.Add(Line(CollectionText.Get("web.collections.copy.checkingRows", "Loading rows…")));
        else if (_usageFailed)
        {
            var problem = Line(CollectionText.Get("web.collections.copy.rowsFailed", "Couldn't load the rows that show it.")); problem.ClearValue(TextBlock.ForegroundProperty);
            _usageRows.Children.Add(problem);
            var collectionId = _usageCollectionId;
            var retry = new Button { Content = CollectionText.Get("common.retry", "Retry"), Style = (Style)Application.Current.Resources["OutlineButtonStyle"] }; retry.Click += async (_, _) => await LoadUsageRowsAsync(collectionId); _usageRows.Children.Add(retry);
        }
        else if (_usageList is { } rows)
        {
            if (rows.Count == 0) _usageRows.Children.Add(Line(CollectionText.Get("web.collections.copy.noMyRowsYet", "None of your Home or library page rows show it yet.")));
            foreach (var row in rows)
            {
                var page = row.Scope == "home" ? CollectionText.Get("web.collections.rows.home", row.PageName) : row.PageName;
                var hidden = row.Hidden ? " · " + CollectionText.Get("web.collections.rows.hidden", "Hidden") : "";
                _usageRows.Children.Add(Line($"{page} › {row.RowTitle}{hidden}", 13));
            }
        }
    }
}
