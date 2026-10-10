using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private string _syncedTheme = "all";
    private readonly WrapPanel _syncedThemes = new() { HorizontalSpacing = 8, VerticalSpacing = 8 };
    private readonly Border _syncedChoiceFrame = new() { CornerRadius = new(12), BorderThickness = new(1) };
    private StackPanel? _syncedLinkField;
    private readonly Button _syncedMatchButton = new() { MinHeight = 0, Height = 36, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 6, 12, 6) };
    private Grid? _syncedOrderGrid;

    private RadioButton SyncedSourceTab(string label, bool selected)
    {
        var button = new RadioButton { Content = label, GroupName = "synced-source-" + GetHashCode(), IsChecked = selected,
            MinWidth = 0, MinHeight = 0, Height = 29, Padding = new(14, 0, 14, 0), FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Medium,
            Foreground = CurrentBrush(selected ? "PrimaryTextBrush" : "SecondaryTextBrush") };
        button.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
              <Grid>
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CheckStates">
                    <VisualState x:Name="Unchecked" />
                    <VisualState x:Name="Checked"><VisualState.Setters><Setter Target="Selection.Opacity" Value="1" /></VisualState.Setters></VisualState>
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <Border Background="Transparent" />
                <Border x:Name="Selection" CornerRadius="8" Background="{StaticResource AccentBackgroundBrush}" BorderThickness="1" BorderBrush="{StaticResource BorderBrush}" Opacity="0" />
                <ContentPresenter Content="{TemplateBinding Content}" Padding="{TemplateBinding Padding}" VerticalContentAlignment="Center" HorizontalContentAlignment="Center" />
              </Grid>
            </ControlTemplate>
            """);
        button.Checked += (_, _) => { button.FontWeight = FontWeights.SemiBold; button.Foreground = CurrentBrush("PrimaryTextBrush"); };
        button.Unchecked += (_, _) => { button.FontWeight = FontWeights.Medium; button.Foreground = CurrentBrush("SecondaryTextBrush"); };
        AutomationProperties.SetName(button, label); return button;
    }

    private void BuildSyncedThemes()
    {
        _syncedChoiceFrame.Background = CurrentBrush("AppBackgroundBrush"); _syncedChoiceFrame.BorderBrush = CurrentBrush("BorderBrush");
        _syncedThemes.Children.Clear();
        var groups = _syncedImports?.TemplateGroups.Where(group => group.Templates.Any(t => t.Source == "mdblist" && !string.IsNullOrWhiteSpace(t.Mdblist?.Url))).ToArray() ?? [];
        _syncedThemes.Visibility = groups.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, label) in new[] { ("all", "All") }.Concat(groups.Select(g => (g.Category, g.Label))))
        {
            var selected = key == _syncedTheme;
            var button = new Button { Content = label, Tag = key, MinHeight = 0, Height = 32, CornerRadius = new(16), BorderThickness = new(1), Padding = new(12, 0, 12, 0), FontSize = 13, FontWeight = FontWeights.Medium,
                Background = CurrentBrush(selected ? "PrimaryTextBrush" : "AppBackgroundBrush"), Foreground = CurrentBrush(selected ? "AppBackgroundBrush" : "PrimaryTextBrush"), BorderBrush = CurrentBrush("BorderBrush") };
            button.Click += (_, _) => { _syncedTheme = key; RenderSyncedSource(); };
            AutomationProperties.SetName(button, label); AutomationProperties.SetHelpText(button, selected ? "Selected theme" : "Filter popular picks");
            _syncedThemes.Children.Add(button);
        }
    }

    private StackPanel SyncedGroupHeading(string title, string help) => new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(14, 12, 14, 4), Children = {
        new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold },
        new TextBlock { Text = help, FontSize = 13, Foreground = CurrentBrush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis }
    } };

    private Border SyncedDivider(FrameworkElement child) => new() { Child = child, Padding = new(0, 16, 0, 0), BorderThickness = new(0, 1, 0, 0), BorderBrush = CurrentBrush("BorderBrush") };

    private FrameworkElement BuildSyncedOrder()
    {
        Detach(ImportedDefaultSortCombo); Detach(_titleLimit);
        RemoveFrom(_syncedBody, ImportedDefaultSortCombo); RemoveFrom(_syncedBody, _titleLimit);
        ImportedDefaultSortCombo.MinHeight = 0; ImportedDefaultSortCombo.Height = 36; ImportedDefaultSortCombo.FontSize = 14; ImportedDefaultSortCombo.Header = null;
        AutomationProperties.SetName(ImportedDefaultSortCombo, "Default sort");
        _titleLimit.PlaceholderText = "Whole list"; _titleLimit.FontSize = 14; _titleLimit.MinHeight = 0; _titleLimit.Height = 42;
        AutomationProperties.SetName(_titleLimit, "Max titles");
        var limit = new Grid(); limit.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); limit.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _titleLimit.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); _titleLimit.BorderThickness = new(0);
        limit.Children.Add(_titleLimit);
        var unit = new Border { Padding = new(12, 0, 12, 0), BorderBrush = CurrentBrush("BorderBrush"), BorderThickness = new(1, 0, 0, 0), Child = new TextBlock { Text = "max titles", FontSize = 13, Foreground = CurrentBrush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center } };
        Grid.SetColumn(unit, 1); limit.Children.Add(unit);
        var limitBorder = new Border { Height = 44, CornerRadius = new(8), BorderThickness = new(1), BorderBrush = CurrentBrush("BorderBrush"), Child = limit };
        _syncedOrderGrid = new() { ColumnSpacing = 10, RowSpacing = 10 };
        _syncedOrderGrid.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) }); _syncedOrderGrid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
        _syncedOrderGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); _syncedOrderGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _syncedOrderGrid.Children.Add(ImportedDefaultSortCombo); _syncedOrderGrid.Children.Add(limitBorder);
        UpdateSyncedResponsive();
        return SyncedDivider(new StackPanel { Spacing = 12, Children = {
            new TextBlock { Text = "Order", FontSize = 14.5, LineHeight = 21.75, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold }, _syncedOrderGrid,
            new TextBlock { Text = "Blank takes the whole list, up to 500. A profile that picks its own sort while browsing keeps that choice.", FontSize = 13, LineHeight = 19.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap }
        } });
    }

    private void CommitSyncedLimit()
    {
        if (ViewModel.IsReadOnly || ViewModel.IsSaving || ViewModel.CollectionType == "trakt") return;
        var input = _titleLimit.Text.Trim();
        if (input.Length == 0) ViewModel.MaxItemsText = null;
        else if (input.All(char.IsAsciiDigit) && int.TryParse(input, out var limit) && limit > 0) ViewModel.MaxItemsText = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _titleLimit.Text = ViewModel.MaxItemsText ?? "";
    }

    private void UpdateSyncedResponsive()
    {
        var phone = ActualWidth < 640;
        LayoutSyncedChartCards();
        _newSchedule.Width = phone ? double.NaN : 280; _newSchedule.HorizontalAlignment = phone ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        _savedSchedule.Width = phone ? double.NaN : 280; _savedSchedule.HorizontalAlignment = phone ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (_savedWhoDecides != null) { Grid.SetColumn((FrameworkElement)_savedWhoDecides.Children[1], phone ? 0 : 1); Grid.SetRow((FrameworkElement)_savedWhoDecides.Children[1], phone ? 1 : 0); Grid.SetColumnSpan((FrameworkElement)_savedWhoDecides.Children[0], phone ? 2 : 1); Grid.SetColumnSpan((FrameworkElement)_savedWhoDecides.Children[1], phone ? 2 : 1); }
        if (_savedSyncStrip != null) for (var n = 0; n < _savedSyncStrip.Children.Count; n++) { var cell = (Border)_savedSyncStrip.Children[n]; Grid.SetColumn(cell, phone ? 0 : n); Grid.SetRow(cell, phone ? n : 0); Grid.SetColumnSpan(cell, phone ? 3 : 1); cell.BorderThickness = n == 0 ? new(0) : phone ? new(0, 1, 0, 0) : new(1, 0, 0, 0); }
        if (_syncedOrderGrid == null) return;
        Grid.SetColumn((FrameworkElement)_syncedOrderGrid.Children[1], phone ? 0 : 1); Grid.SetRow((FrameworkElement)_syncedOrderGrid.Children[1], phone ? 1 : 0);
        Grid.SetColumnSpan((FrameworkElement)_syncedOrderGrid.Children[0], phone ? 2 : 1); Grid.SetColumnSpan((FrameworkElement)_syncedOrderGrid.Children[1], phone ? 2 : 1);
        _syncedOrderGrid.RowSpacing = phone ? 10 : 0;
    }

    private void UpdateSyncedMatchLabel()
    {
        var ids = ViewModel.SelectedLibraryIds;
        var first = ids.Count > 0 ? ViewModel.AvailableLibraries.FirstOrDefault(l => l.Id == ids[0])?.Name ?? "1 library" : "";
        var label = ids.Count == 0 ? "All my libraries" : ids.Count == 1 ? first : $"{first} +{ids.Count - 1} more";
        _syncedMatchButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = {
            new TextBlock { Text = "Match into", FontSize = 14.5, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = label, FontSize = 14 }, WebUiIcon.Create("chevron-down", 14)
        } };
        AutomationProperties.SetName(_syncedMatchButton, "Match into: " + label);
    }
}
