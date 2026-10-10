using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Controls;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private readonly TextBlock _manualCaption = new() { FontSize = 13.5, LineHeight = 20.25, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap };
    private readonly Border _manualDraftTag = new();
    private readonly TextBlock _manualSearchScope = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid _manualEmpty = new();
    private readonly Border _manualSearchResults = new() { CornerRadius = new(12), BorderThickness = new(1), Padding = new(6) };
    private readonly TextBlock _manualSearchStatus = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _manualSearchProblem = new() { Spacing = 8, Orientation = Orientation.Horizontal };
    private readonly Border _editorFooter = new() { MaxWidth = 768, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(16), BorderThickness = new(1), Padding = new(16, 8, 8, 8) };
    private readonly Grid _editorFooterGrid = new() { ColumnSpacing = 8 };
    private readonly TextBlock _editorFooterStatus = new() { FontSize = 14, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, MaxLines = 2, VerticalAlignment = VerticalAlignment.Center };

    private void ConfigureManualContents()
    {
        foreach (var element in new FrameworkElement[] { SearchBox, SearchResultsPanel, ItemsSeparator, AddedItemsHeader, ManualItemsPanel, NoItemsText }) Detach(element);
        var body = new StackPanel { Spacing = 16 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new TextBlock { Text = "Titles", FontSize = 17, LineHeight = 25.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold });
        _manualDraftTag.Child = new TextBlock { Text = "Not created yet", FontSize = 12, FontWeight = FontWeights.Medium };
        _manualDraftTag.Background = CurrentBrush("SurfaceBrush"); _manualDraftTag.Padding = new(8, 2, 8, 2); _manualDraftTag.CornerRadius = new(6); _manualDraftTag.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(_manualDraftTag);
        _manualCaption.Foreground = CurrentBrush("SecondaryTextBrush");
        body.Children.Add(new StackPanel { Spacing = 4, Children = { heading, _manualCaption } });
        SearchBox.PlaceholderText = "Add a title"; SearchBox.MaxWidth = double.PositiveInfinity;
        SearchBox.MinHeight = 0; SearchBox.Height = 42; SearchBox.Padding = new(0, 8, 0, 8); SearchBox.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); SearchBox.BorderThickness = new(0);
        AutomationProperties.SetName(SearchBox, "Add a title");
        var search = new Grid { ColumnSpacing = 10 };
        search.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); search.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); search.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = WebUiIcon.Create("search", 16, CurrentBrush("SecondaryTextBrush")); icon.VerticalAlignment = VerticalAlignment.Center; search.Children.Add(icon);
        Grid.SetColumn(SearchBox, 1); search.Children.Add(SearchBox); Grid.SetColumn(_manualSearchScope, 2); search.Children.Add(_manualSearchScope);
        body.Children.Add(new Border { Child = search, Height = 44, Padding = new(12, 0, 12, 0), BorderBrush = CurrentBrush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(12), Background = CurrentBrush("AppBackgroundBrush") });
        SearchResultsPanel.Spacing = 2;
        _manualSearchResults.Child = new ScrollViewer { Content = SearchResultsPanel, MaxHeight = 320, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _manualSearchResults.Background = CurrentBrush("CardBackgroundBrush"); _manualSearchResults.BorderBrush = CurrentBrush("BorderBrush");
        body.Children.Add(_manualSearchStatus);
        _manualSearchStatus.Foreground = CurrentBrush("SecondaryTextBrush");
        _manualSearchProblem.Children.Add(new TextBlock { Text = "The search didn't work.", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var retry = new Button { Content = "Try again", Style = (Style)Application.Current.Resources["OutlineButtonStyle"], Height = 32, MinHeight = 0 };
        retry.Click += async (_, _) => await ViewModel.SearchItemsCommand.ExecuteAsync(SearchBox.Text);
        _manualSearchProblem.Children.Add(retry); body.Children.Add(_manualSearchProblem);
        body.Children.Add(_manualSearchResults); body.Children.Add(ManualItemsPanel);
        // A dashed outline is an actual shape; it never changes the empty copy's measure.
        _manualEmpty.Children.Add(new Rectangle { Stroke = CurrentBrush("BorderBrush"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = 12, RadiusY = 12 });
        NoItemsText.Text = "No titles yet. Search above to add the first."; NoItemsText.FontSize = 13.5; NoItemsText.LineHeight = 20.25; NoItemsText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; NoItemsText.TextWrapping = TextWrapping.Wrap; NoItemsText.HorizontalAlignment = HorizontalAlignment.Stretch; NoItemsText.Margin = new(17, 21, 17, 21);
        _manualEmpty.Children.Add(NoItemsText); body.Children.Add(_manualEmpty);
        var order = new Grid { ColumnSpacing = 16 };
        order.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); order.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        order.Children.Add(new TextBlock { Text = "Order", FontSize = 14.5, LineHeight = 21.75, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold });
        var orderCopy = new TextBlock { Text = "Your order. Drag titles to change it.", FontSize = 13.5, LineHeight = 20.25, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(orderCopy, 1); order.Children.Add(orderCopy);
        body.Children.Add(new Border { BorderBrush = CurrentBrush("BorderBrush"), BorderThickness = new(0, 1, 0, 0), Padding = new(0, 16, 0, 0), Child = order });
        ManualItemsSection.Child = body;
    }

    private void UpdateManualContents(bool phone)
    {
        _manualDraftTag.Visibility = ViewModel.IsEditing ? Visibility.Collapsed : Visibility.Visible;
        _manualCaption.Text = ViewModel.IsEditing ? "In the order you see them. Titles save as you add, remove or drag them." : "Pick titles now. They're added when you press Create collection.";
        _manualSearchScope.Text = ViewModel.SelectedLibraryIds.Count == 0 ? "All your libraries" : string.Join(", ", ViewModel.AvailableLibraries.Where(l => ViewModel.SelectedLibraryIds.Contains(l.Id)).Select(l => l.Name));
        _manualSearchScope.Foreground = CurrentBrush("SecondaryTextBrush"); _manualSearchScope.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
        _manualSearchResults.Visibility = _manualSearchOpen && ViewModel.SearchResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var searching = _manualSearchOpen && !string.IsNullOrWhiteSpace(SearchBox.Text);
        var waiting = ViewModel.IsSearchingItems || _manualSearchDelay.IsEnabled;
        _manualSearchStatus.Text = waiting ? "Searching…" : "No titles match.";
        _manualSearchStatus.Visibility = searching && ViewModel.ItemSearchError == null && (waiting || ViewModel.SearchResults.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        _manualSearchProblem.Visibility = searching && ViewModel.ItemSearchError != null ? Visibility.Visible : Visibility.Collapsed;
        ManualItemsPanel.Visibility = ViewModel.ManualItems.Count == 0 && ViewModel.LastRemovedItem == null ? Visibility.Collapsed : Visibility.Visible;
        _manualEmpty.Visibility = ViewModel.ManualItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureEditorFooter()
    {
        Detach(EditorActions);
        _editorFooterGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _editorFooterGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _editorFooterGrid.Children.Add(_editorFooterStatus); Grid.SetColumn(EditorActions, 1); _editorFooterGrid.Children.Add(EditorActions);
        _editorFooter.Child = _editorFooterGrid;
        ((Grid)Content).Children.Add(_editorFooter); Canvas.SetZIndex(_editorFooter, 40);
        _editorFooter.Background = CurrentBrush("CardBackgroundBrush"); _editorFooter.BorderBrush = CurrentBrush("BorderBrush");
        AutomationProperties.SetName(_editorFooter, "Collection changes");
    }

    private void UpdateEditorFooter(bool phone)
    {
        var draft = CaptureDraft();
        var dirty = _draftBaseline?.Count(pair => draft.GetValueOrDefault(pair.Key) != pair.Value) ?? 0;
        _editorFooter.Visibility = !ViewModel.IsReadOnly && !_initializingEditor && !ViewModel.IsLoading && !ViewModel.IsNotFound && !ViewModel.IsLoadUnavailable && (!ViewModel.IsEditing || dirty > 0 || ViewModel.ErrorMessage != null) ? Visibility.Visible : Visibility.Collapsed;
        _editorFooter.Width = Math.Max(0, Math.Min(768, ActualWidth - (phone ? 32 : ActualWidth < 1024 ? 48 : 80)));
        _editorFooter.Margin = new(0, 0, 0, ActualWidth < 1024 ? 12 : 18);
        _editorFooterStatus.Text = ViewModel.ErrorMessage ?? (ViewModel.IsEditing ? $"{dirty} unsaved change{(dirty == 1 ? "" : "s")}" : phone ? "Not created yet" : _newSynced ? "Not created yet   Pick a list, then create it." : "Not created yet   Name it, then create it.");
        EditorActions.Spacing = 8;
        foreach (var button in EditorActions.Children.OfType<Button>()) { button.Height = ActualWidth < 1024 ? 44 : 32; button.MinHeight = 0; button.Padding = new(12, 0, 12, 0); }
        var cancel = EditorActions.Children.OfType<Button>().First(); cancel.Visibility = ViewModel.IsEditing ? Visibility.Collapsed : Visibility.Visible;
        DiscardButton.Visibility = ViewModel.IsEditing ? Visibility.Visible : Visibility.Collapsed;
        SaveButtonText.Text = ViewModel.IsSaving ? "Saving..." : !ViewModel.IsEditing ? "Create collection" : ViewModel.ErrorMessage != null ? "Try again" : "Save";
    }
}
