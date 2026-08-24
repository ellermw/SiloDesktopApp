using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Views.Admin;

namespace SiloPlayer.Controls;

public sealed record AdminCommandTarget(
    string Group,
    string Label,
    string Description,
    string Keywords,
    Type PageType,
    object? Parameter = null);

/// <summary>
/// Dashboard-only Ctrl+K palette matching the WebUI AdminSectionCommandDialog.
/// It searches admin routes and the settings sections/field keywords routed
/// through those routes, while preserving mouse and keyboard selection.
/// </summary>
public sealed class AdminCommandPaletteDialog : ContentDialog
{
    private readonly TextBox _search = new();
    private readonly ListView _results = new();
    private readonly TextBlock _count = new();
    private readonly IReadOnlyList<AdminCommandTarget> _targets;
    private List<AdminCommandTarget> _visible = [];

    public AdminCommandTarget? SelectedTarget { get; private set; }

    public AdminCommandPaletteDialog(IReadOnlyList<AdminCommandTarget> targets)
    {
        _targets = targets;
        Title = "Search admin sections";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.None;
        MinWidth = 560;

        _search.PlaceholderText = "Search admin sections...";
        AutomationProperties.SetName(_search, "Search admin sections");
        _search.TextChanged += (_, _) => RebuildResults();
        _search.KeyDown += Search_KeyDown;

        _results.MaxHeight = 400;
        _results.SelectionMode = ListViewSelectionMode.Single;
        _results.IsItemClickEnabled = true;
        _results.ItemClick += (_, args) => Pick(args.ClickedItem as AdminCommandTarget);
        _results.KeyDown += Results_KeyDown;

        _count.FontSize = 12;
        _count.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _count.Margin = new Thickness(2, 4, 2, 0);

        Content = new StackPanel
        {
            Spacing = 10,
            Children = { _search, _results, _count }
        };
        Opened += (_, _) =>
        {
            RebuildResults();
            _search.Focus(FocusState.Programmatic);
            _search.SelectAll();
        };
    }

    private void RebuildResults()
    {
        var terms = (_search.Text ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _visible = _targets.Where(target => terms.All(term => SearchText(target).Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        _results.ItemsSource = _visible;
        _results.ItemTemplate = BuildTemplate();
        _results.SelectedIndex = _visible.Count > 0 ? 0 : -1;
        _count.Text = terms.Length > 0
            ? $"{_visible.Count} {(_visible.Count == 1 ? "match" : "matches")}"
            : $"{_targets.Count} admin sections";
    }

    private static string SearchText(AdminCommandTarget target)
        => $"{target.Group} {target.Label} {target.Description} {target.Keywords}";

    private static DataTemplate BuildTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Padding="10,8" ColumnSpacing="10">
                <Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <FontIcon Glyph="&#xE721;" FontSize="13" Foreground="{StaticResource SecondaryTextBrush}" VerticalAlignment="Top" Margin="0,3,0,0"/>
                <StackPanel Grid.Column="1" Spacing="3">
                  <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="{Binding Label}" FontSize="13" FontWeight="SemiBold"/>
                    <TextBlock Text="{Binding Group}" FontSize="10" Foreground="{StaticResource TertiaryTextBrush}" VerticalAlignment="Center"/>
                  </StackPanel>
                  <TextBlock Text="{Binding Description}" FontSize="11" Foreground="{StaticResource SecondaryTextBrush}" TextWrapping="Wrap"/>
                </StackPanel>
              </Grid>
            </DataTemplate>
            """;
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Down && _visible.Count > 0)
        {
            _results.SelectedIndex = Math.Min(_visible.Count - 1, _results.SelectedIndex + 1);
            _results.ScrollIntoView(_results.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Up && _visible.Count > 0)
        {
            _results.SelectedIndex = _results.SelectedIndex <= 0 ? _visible.Count - 1 : _results.SelectedIndex - 1;
            _results.ScrollIntoView(_results.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Pick(_results.SelectedItem as AdminCommandTarget);
            e.Handled = true;
        }
    }

    private void Results_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        Pick(_results.SelectedItem as AdminCommandTarget);
        e.Handled = true;
    }

    private void Pick(AdminCommandTarget? target)
    {
        if (target is null) return;
        SelectedTarget = target;
        Hide();
    }
}
