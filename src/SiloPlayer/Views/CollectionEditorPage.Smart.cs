using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private readonly StackPanel _smartLibraries = new() { Spacing = 4 };
    private readonly Button _smartLibraryPicker = new() { MinHeight = 0, Height = 36, Padding = new(12, 6, 12, 6) };
    private readonly ComboBox _smartScope = new() { MinHeight = 0, Height = 36, MinWidth = 0, FontSize = 14 };
    private readonly TextBox _smartLimit = new() { PlaceholderText = "No limit", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Border _smartPreviewPanel = new();
    private readonly Grid _smartPreviewGrid = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly TextBlock _smartPreviewStatus = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _smartPreviewDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _settingSmartFields;
    private bool _previewPresentationQueued;
    private readonly List<Border> _smartPreviewTiles = [];

    private void ConfigureSmartContents()
    {
        Detach(RulesPanel); Detach(PreviewCountText);
        var heading = new StackPanel { Spacing = 4, Children = {
            new TextBlock { Text = "Rules", FontSize = 17, FontWeight = FontWeights.SemiBold, LineHeight = 25.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
            new TextBlock { Text = "Titles that match are in the collection. New matches join on their own.", FontSize = 13.5, LineHeight = 20.25, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") }
        } };
        foreach (var (key, label) in new[] { ("all", "all titles"), ("video", "movies and shows"), ("movie", "movies"), ("series", "shows"), ("episode", "episodes"), ("audiobook", "audiobooks"), ("ebook", "ebooks"), ("manga", "manga") })
            _smartScope.Items.Add(new ComboBoxItem { Tag = key, Content = label });
        AutomationProperties.SetName(_smartScope, "Kind of titles");
        _smartScope.SelectionChanged += (_, _) =>
        {
            if (_settingSmartFields || _smartScope.SelectedItem is not ComboBoxItem selected) return;
            ViewModel.RuleDefinition.MediaScope = Equals(selected.Tag, "all") ? null : (string)selected.Tag;
            _rulesEditor?.ConfigureCatalogScope(ViewModel.RuleDefinition.MediaScope);
            UpdateDirtyDock(); ScheduleSmartPreview();
        };
        var sentence = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        sentence.Children.Add(new TextBlock { Text = "Show", FontSize = 15, VerticalAlignment = VerticalAlignment.Center }); sentence.Children.Add(_smartScope);
        sentence.Children.Add(new TextBlock { Text = "from", FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        _smartLibraryPicker.Flyout = new Flyout { Content = new ScrollViewer { Content = _smartLibraries, MaxHeight = 320, MinWidth = 200, HorizontalScrollMode = ScrollMode.Disabled } };
        sentence.Children.Add(_smartLibraryPicker);
        SmartRulesSection.Child = new StackPanel { Spacing = 20, Children = { heading, sentence, RulesPanel } };
        _smartLimit.LostFocus += (_, _) => CommitSmartLimit();
        _smartLimit.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; CommitSmartLimit(); } };
        var previewHeading = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        previewHeading.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 55, 190, 128)), VerticalAlignment = VerticalAlignment.Center });
        previewHeading.Children.Add(new TextBlock { Text = "Live preview", FontSize = 14, Foreground = CurrentBrush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center });
        PreviewCountText.FontSize = 14; PreviewCountText.TextWrapping = TextWrapping.Wrap; previewHeading.Children.Add(PreviewCountText);
        _smartPreviewStatus.Foreground = CurrentBrush("SecondaryTextBrush");
        _smartPreviewPanel.Child = new StackPanel { Spacing = 14, Children = { previewHeading, _smartPreviewGrid, _smartPreviewStatus } };
        _smartPreviewGrid.SizeChanged += (_, _) => LayoutSmartPreview();
        _smartPreviewDelay.Tick += async (_, _) => { _smartPreviewDelay.Stop(); if (CanPreviewSmart()) await ViewModel.RefreshPreviewAsync(); };
        Unloaded += (_, _) => { _smartPreviewDelay.Stop(); ViewModel.CancelPreview(); };
    }

    private void PopulateSmartFields()
    {
        _settingSmartFields = true;
        try
        {
            _smartLimit.Text = ViewModel.RuleDefinition.Limit?.ToString(CultureInfo.InvariantCulture) ?? "";
            var scope = ViewModel.RuleDefinition.MediaScope ?? "all";
            var choice = _smartScope.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, scope));
            if (choice == null) { choice = new() { Content = scope, Tag = scope }; _smartScope.Items.Add(choice); }
            _smartScope.SelectedItem = choice;
            _smartLibraries.Children.Clear();
            var all = new CheckBox { Content = "All my libraries", IsChecked = ViewModel.RuleDefinition.LibraryIds.Count == 0 };
            all.Checked += (_, _) => { if (_settingSmartFields) return; ViewModel.RuleDefinition.LibraryIds.Clear(); SyncSmartLibraries(); PopulateSmartFields(); };
            _smartLibraries.Children.Add(all);
            foreach (var library in ViewModel.AvailableLibraries)
            {
                var box = new CheckBox { Content = library.Name, IsChecked = ViewModel.RuleDefinition.LibraryIds.Contains(library.Id) };
                void Change()
                {
                    if (_settingSmartFields) return;
                    if (box.IsChecked == true && !ViewModel.RuleDefinition.LibraryIds.Contains(library.Id)) ViewModel.RuleDefinition.LibraryIds.Add(library.Id);
                    else if (box.IsChecked != true) ViewModel.RuleDefinition.LibraryIds.Remove(library.Id);
                    _settingSmartFields = true; all.IsChecked = ViewModel.RuleDefinition.LibraryIds.Count == 0; _settingSmartFields = false;
                    SyncSmartLibraries();
                }
                box.Checked += (_, _) => Change(); box.Unchecked += (_, _) => Change(); _smartLibraries.Children.Add(box);
            }
            UpdateSmartLibraryLabel();
        }
        finally { _settingSmartFields = false; }
    }

    private void UpdateSmartLibraryLabel()
    {
        var ids = ViewModel.RuleDefinition.LibraryIds;
        var names = ViewModel.AvailableLibraries.Where(l => ids.Contains(l.Id)).Select(l => l.Name).ToArray();
        var label = ids.Count == 0 ? "all my libraries" : names.Length == 1 && ids.Count == 1 ? names[0] : $"{ids.Count} libraries";
        _smartLibraryPicker.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 14 }, WebUiIcon.Create("chevron-down", 14) } };
        AutomationProperties.SetName(_smartLibraryPicker, "Libraries: " + label);
    }

    private void SyncSmartLibraries()
    {
        ViewModel.SelectedLibraryIds.Clear(); foreach (var id in ViewModel.RuleDefinition.LibraryIds) ViewModel.SelectedLibraryIds.Add(id);
        UpdateSmartLibraryLabel(); UpdateDirtyDock(); ScheduleSmartPreview();
    }

    private void CommitSmartLimit()
    {
        if (_settingSmartFields || ViewModel.IsReadOnly || ViewModel.IsSaving || ViewModel.CollectionType != "smart") return;
        var previous = ViewModel.RuleDefinition.Limit;
        var input = _smartLimit.Text.Trim();
        if (input.Length == 0) ViewModel.RuleDefinition.Limit = null;
        else if (input.All(char.IsAsciiDigit) && int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0) ViewModel.RuleDefinition.Limit = limit;
        _smartLimit.Text = ViewModel.RuleDefinition.Limit?.ToString(CultureInfo.InvariantCulture) ?? "";
        UpdateDirtyDock(); if (previous != ViewModel.RuleDefinition.Limit) ScheduleSmartPreview();
    }

    private bool CanPreviewSmart() => _editorActive && !_initializingEditor && ViewModel.CollectionType == "smart" &&
        !ViewModel.IsLoading && !ViewModel.IsLoadUnavailable && !ViewModel.IsReadOnly && _rulesEditor?.IsValid != false;
    private void ScheduleSmartPreview()
    {
        _smartPreviewDelay.Stop(); ViewModel.CancelPreview();
        if (CanPreviewSmart()) _smartPreviewDelay.Start();
    }

    private void QueueSmartPreviewPresentation()
    {
        if (_previewPresentationQueued) return;
        _previewPresentationQueued = true;
        DispatcherQueue.TryEnqueue(() => { _previewPresentationQueued = false; if (IsLoaded) BuildPreviewItemsUI(); });
    }

    private void BuildPreviewItemsUI()
    {
        if (_smartPreviewPanel.Child == null || ViewModel.CollectionType != "smart") return;
        PreviewCountText.Visibility = ViewModel.HasPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewCountText.Text = ViewModel.HasPreview ? $"· {ViewModel.PreviewTotal.ToString("N0", CultureInfo.CurrentCulture)} {(ViewModel.PreviewTotal == 1 ? "title matches" : "titles match")}" +
            (ViewModel.PreviewTotal > ViewModel.PreviewItems.Count && ViewModel.PreviewItems.Count > 0 ? $" · showing {ViewModel.PreviewItems.Count}" : "") : "";
        _smartPreviewTiles.Clear(); _smartPreviewGrid.Children.Clear();
        foreach (var item in ViewModel.PreviewItems)
        {
            var tile = new Border { Tag = item, CornerRadius = new(10), Background = CurrentBrush("SurfaceBrush"), Opacity = ViewModel.IsPreviewing ? .6 : 1 };
            AutomationProperties.SetName(tile, item.Title); ToolTipService.SetToolTip(tile, item.Title);
            var canvas = new Canvas(); var image = new Image { Stretch = Stretch.UniformToFill };
            if (!string.IsNullOrWhiteSpace(item.PosterUrl)) image.Source = (ImageSource)new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(item.PosterUrl, typeof(ImageSource), null!, "");
            canvas.Children.Add(image);
            var caption = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Padding = new(8, 24, 8, 8),
                Background = new LinearGradientBrush
                {
                    StartPoint = new(0, 0), EndPoint = new(0, 1),
                    GradientStops = { new() { Color = Microsoft.UI.Colors.Transparent, Offset = 0 }, new() { Color = Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0), Offset = 1 } }
                },
                Child = new TextBlock { Text = item.Title, FontSize = 11.5, LineHeight = 14.375, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), MaxLines = 2,
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            tile.Child = new Grid { Children = { canvas, caption } };
            tile.SizeChanged += (_, _) => { image.Width = tile.ActualWidth; image.Height = tile.ActualHeight; ClipSmartPreview(tile); };
            _smartPreviewTiles.Add(tile); _smartPreviewGrid.Children.Add(tile);
        }
        if (_smartPreviewTiles.Count == 0)
            for (var n = 0; n < 8; n++) { var ghost = new Border { CornerRadius = new(10), Background = CurrentBrush("SurfaceBrush"), BorderBrush = CurrentBrush("BorderBrush"), BorderThickness = new(1) }; _smartPreviewTiles.Add(ghost); _smartPreviewGrid.Children.Add(ghost); }
        _smartPreviewStatus.Text = ViewModel.PreviewError ?? (ViewModel.HasPreview && ViewModel.PreviewItems.Count == 0 ? "No titles match yet\nYou can still save. Titles that match later join on their own." : "");
        _smartPreviewStatus.Visibility = string.IsNullOrEmpty(_smartPreviewStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
        LayoutSmartPreview();
    }

    private static void ClipSmartPreview(Border tile)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(tile);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry(); geometry.Size = new((float)tile.ActualWidth, (float)tile.ActualHeight); geometry.CornerRadius = new(10, 10);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }

    private void LayoutSmartPreview()
    {
        var count = ActualWidth < 640 ? 4 : ActualWidth < 1024 ? 6 : 8;
        if (_smartPreviewGrid.ColumnDefinitions.Count != count)
        {
            _smartPreviewGrid.ColumnDefinitions.Clear();
            for (var n = 0; n < count; n++) _smartPreviewGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        }
        var rows = (int)Math.Ceiling((double)_smartPreviewTiles.Count / count);
        while (_smartPreviewGrid.RowDefinitions.Count < rows) _smartPreviewGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        while (_smartPreviewGrid.RowDefinitions.Count > rows) _smartPreviewGrid.RowDefinitions.RemoveAt(_smartPreviewGrid.RowDefinitions.Count - 1);
        var width = Math.Max(0, (_smartPreviewGrid.ActualWidth - (count - 1) * 10) / count);
        for (var n = 0; n < _smartPreviewTiles.Count; n++) { var tile = _smartPreviewTiles[n]; Grid.SetColumn(tile, n % count); Grid.SetRow(tile, n / count); tile.Height = width * 1.5; }
    }
}
