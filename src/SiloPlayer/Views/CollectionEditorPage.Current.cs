using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed record CollectionEditorNavigationArgs(string Kind = "manual")
{
    // The journal retains this object after a creation has become durable.
    public string? CollectionId { get; set; }
    public string? PosterUrl { get; init; }
    public bool PosterIsCollage { get; init; }
}

public sealed partial class CollectionEditorPage
{
    private readonly TextBlock _kindTag = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _syncedNameHint = new() { Text = "Filled in from the list. Picking another list keeps anything you typed.", FontSize = 13, LineHeight = 19.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Image _headerPoster = new() { Stretch = Stretch.UniformToFill };
    private readonly Border _headerCover = new() { Width = 64, Height = 96, CornerRadius = new(10), VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _showOnly = new() { Spacing = 8 };
    private readonly StackPanel _where = new() { Spacing = 16 };
    private readonly Border _wherePanel = new();
    private readonly Border _lookPanel = new();
    private readonly StackPanel _look = new();
    private readonly Button _lookToggle = new();
    private readonly Grid _currentHeader = new() { ColumnSpacing = 20, RowSpacing = 12 };
    private readonly StackPanel _headerTags = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _headerActions = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _lookSummary = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Image _lookThumbnail = new() { Stretch = Stretch.UniformToFill };
    private readonly Border _lookCover = new() { Width = 32, Height = 48, CornerRadius = new(6) };
    private bool _lookOpen;
    private string? _headerPosterUrl;
    private bool _usedNavigationPoster;
    private readonly ComboBox _savedSchedule = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
    private bool _settingSchedule;

    private static Brush CurrentBrush(string name) => (Brush)Application.Current.Resources[name];
    private void Detach(FrameworkElement element)
    {
        if (RemoveFrom(Content, element)) return;
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is Border border) border.Child = null;
        else if (element.Parent is ContentControl content) content.Content = null;
    }
    private static bool RemoveFrom(object? root, FrameworkElement target)
    {
        // Collapsed XAML branches have no visual Parent before realization.
        // Walk their logical content so reparenting also works on first paint.
        if (root is Panel panel)
        {
            if (panel.Children.Contains(target)) { panel.Children.Remove(target); return true; }
            foreach (var child in panel.Children.ToArray()) if (RemoveFrom(child, target)) return true;
        }
        else if (root is Border border)
        {
            if (ReferenceEquals(border.Child, target)) { border.Child = null; return true; }
            return RemoveFrom(border.Child, target);
        }
        else if (root is UserControl user)
        {
            if (ReferenceEquals(user.Content, target)) { user.Content = null; return true; }
            return RemoveFrom(user.Content, target);
        }
        else if (root is ContentControl content)
        {
            if (ReferenceEquals(content.Content, target)) { content.Content = null; return true; }
            return RemoveFrom(content.Content, target);
        }
        return false;
    }
    private StackPanel Field(string label, FrameworkElement control)
    {
        Detach(control);
        return new() { Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 14.5, LineHeight = 21.75, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold }, control } };
    }
    private static void PanelStyle(Border panel, double padding)
    {
        panel.Background = CurrentBrush("CardBackgroundBrush"); panel.BorderBrush = CurrentBrush("BorderBrush");
        panel.BorderThickness = new(1); panel.CornerRadius = new(22); panel.Padding = new(padding);
    }
    private static StackPanel Body(Border section)
    {
        if (section.Child is StackPanel stack) return stack;
        var grid = (Grid)section.Child;
        var content = grid.Children.OfType<FrameworkElement>().Last();
        grid.Children.Remove(content);
        var body = content as StackPanel ?? new StackPanel { Children = { content } };
        section.Child = body; return body;
    }
    private void ConfigureCurrentEditor()
    {
        var header = (StackPanel)CollectionEditorShell.Children[0];
        Detach(PageTitle); Detach(PageSubtitle); header.Children.Clear();
        BackButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            Children = { WebUiIcon.Create("back", 16), new TextBlock { Text = "Collections", FontSize = 14, LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight } } };
        BackButton.Width = double.NaN; BackButton.Height = 21; BackButton.MinHeight = 0; BackButton.Padding = new(0); Detach(BackButton); header.Children.Add(BackButton);
        header.Margin = new(0, 0, 0, 8);
        _currentHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _currentHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _headerCover.Child = _headerPoster; _currentHeader.Children.Add(_headerCover);
        var title = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom };
        title.Children.Add(_headerTags);
        PageTitle.TextWrapping = TextWrapping.Wrap; PageTitle.MaxLines = 2; PageTitle.FontWeight = FontWeights.Bold;
        PageTitle.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        PageSubtitle.FontSize = 12.5; PageSubtitle.MaxWidth = double.PositiveInfinity;
        title.Children.Add(PageTitle); title.Children.Add(PageSubtitle); Grid.SetColumn(title, 1); _currentHeader.Children.Add(title); header.Children.Add(_currentHeader); header.Spacing = 16;
        _currentHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _currentHeader.RowDefinitions.Add(new() { Height = GridLength.Auto }); _currentHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _currentHeader.Children.Add(_headerActions);
        var details = new StackPanel { Spacing = 16 };
        var nameField = Field("Name", NameTextBox);
        nameField.Children.Add(_syncedNameHint);
        details.Children.Add(nameField); details.Children.Add(Field("Description · Optional", DescriptionTextBox));
        NameTextBox.MaxWidth = DescriptionTextBox.MaxWidth = double.PositiveInfinity; NameTextBox.HorizontalAlignment = DescriptionTextBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        NameTextBox.MinHeight = 36; DescriptionTextBox.Height = 78;
        NameTextBox.PlaceholderText = DescriptionTextBox.PlaceholderText = "";
        Detach(DisplayFilterGrid); _showOnly.Children.Add(new TextBlock { Text = "Show only", FontSize = 14.5, LineHeight = 21.75, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold });
        WatchFilterCombo.Header = MediaFilterCombo.Header = null; _showOnly.Children.Add(DisplayFilterGrid);
        DisplayFilterGrid.ColumnSpacing = 8; DisplayFilterGrid.RowSpacing = 0;
        _showOnly.Children.Add(new TextBlock { Text = "Hides titles while browsing, for example the ones you've finished. Nothing is removed.", FontSize = 12.5, LineHeight = 17.1875, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        details.Children.Add(_showOnly);
        var list = Body(ImportedSourceSection); list.Children.Insert(0, new TextBlock { Text = "Synced list", FontSize = 17, FontWeight = FontWeights.SemiBold });
        Detach(ImportedLibrariesPanel); list.Children.Add(Field("Libraries", ImportedLibrariesPanel));
        Detach(ImportedDefaultSortCombo); list.Children.Add(Field("Order", ImportedDefaultSortCombo));
        Detach(SyncScheduleTextBlock);
        foreach (var (value, label) in new[] { ("", "Manual only"), ("daily", "Daily"), ("weekly", "Weekly"), ("monthly", "Monthly"), ("custom", "Custom schedule (current)") })
            _savedSchedule.Items.Add(new ComboBoxItem { Tag = value, Content = label });
        _savedSchedule.SelectionChanged += (_, _) => { if (!_settingSchedule) ViewModel.SyncSchedule = (_savedSchedule.SelectedItem as ComboBoxItem)?.Tag?.ToString(); };
        list.Children.Add(Field("Sync", _savedSchedule));
        BasicInfoSection.Child = details;
        ConfigureManualContents();
        ConfigureEditorFooter();
        ConfigureSmartContents();
        var shared = Body(ImportedSharingSection); var visible = Body(ImportedVisibilitySection);
        ImportedSharingSection.Child = null; ImportedVisibilitySection.Child = null;
        _where.Children.Add(new TextBlock { Text = "Where it shows", FontSize = 17, FontWeight = FontWeights.SemiBold }); _where.Children.Add(shared); _where.Children.Add(visible); _wherePanel.Child = _where;
        ConfigureWhereRows();
        var art = Body(ImportedPosterSection); ImportedPosterSection.Child = null; _look.Children.Add(art);
        _lookToggle.Style = (Style)Application.Current.Resources["GhostButtonStyle"]; _lookToggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        _lookToggle.HorizontalContentAlignment = HorizontalAlignment.Stretch; _lookToggle.Click += (_, _) => { _lookOpen = !_lookOpen; UpdateCurrentEditor(); _lookToggle.Focus(FocusState.Programmatic); };
        _look.Children.Insert(0, _lookToggle); _lookPanel.Child = _look;
        EditorPrimaryColumn.Children.Clear(); EditorPrimaryColumn.Children.Add(BasicInfoSection);
        EditorPrimaryColumn.Children.Add(ManualItemsSection); EditorPrimaryColumn.Children.Add(SmartRulesSection); EditorPrimaryColumn.Children.Add(_smartPreviewPanel); EditorPrimaryColumn.Children.Add(ImportedSourceSection);
        EditorPrimaryColumn.Children.Add(_wherePanel); EditorPrimaryColumn.Children.Add(_lookPanel);
        EditorSidebar.Visibility = Visibility.Collapsed; EditorSidebarColumn.Width = new(0); EditorBodyGrid.ColumnSpacing = 0;
        ImportedEditorSurface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); ImportedEditorSurface.BorderThickness = new(0);
        foreach (var text in EditorActions.Children.OfType<TextBlock>()) text.Visibility = Visibility.Collapsed;
        UpdateCurrentEditor();
    }
    private void UpdateCurrentEditor()
    {
        if (_wherePanel.Child == null) return;
        var phone = ActualWidth < 640;
        _syncedNameHint.Foreground = CurrentBrush("SecondaryTextBrush");
        _syncedNameHint.Text = _syncedKeptFields ?? "Filled in from the list. Picking another list keeps anything you typed.";
        _syncedNameHint.Visibility = _newSynced && (_syncedKeptFields != null || (ViewModel.Name.Length > 0 && ViewModel.Name == _syncedFilledName)) ? Visibility.Visible : Visibility.Collapsed;
        CollectionEditorShell.MaxWidth = 768; CollectionEditorShell.Spacing = 16;
        CollectionEditorScroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        CollectionEditorScroll.HorizontalScrollMode = ScrollMode.Disabled;
        CollectionEditorScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        CollectionEditorShell.HorizontalAlignment = HorizontalAlignment.Center;
        var gutter = phone ? 16 : ActualWidth < 1024 ? 24 : 40;
        CollectionEditorShell.Width = Math.Max(0, Math.Min(768, ActualWidth - gutter * 2));
        CollectionEditorShell.Margin = new(0, phone ? 32 : ActualWidth < 1024 ? 40 : 56, 0, 112);
        EditorPrimaryColumn.Spacing = 16; EditorSidebar.Visibility = ImportedSourceBanner.Visibility = Visibility.Collapsed;
        EditorSidebarColumn.Width = new(0); EditorBodyGrid.ColumnSpacing = 0;
        ImportedEditorSurface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); ImportedEditorSurface.BorderThickness = new(0);
        foreach (var panel in new[] { BasicInfoSection, ManualItemsSection, SmartRulesSection, _smartPreviewPanel, ImportedSourceSection, _wherePanel, _lookPanel }) PanelStyle(panel, phone ? 20 : 24);
        if (!_lookOpen) _lookPanel.Padding = new(0);
        PageTitle.FontSize = phone ? 26 : 32; PageTitle.LineHeight = PageTitle.FontSize * 1.15; PageTitle.Text = string.IsNullOrEmpty(ViewModel.Name) ? "New collection" : ViewModel.Name;
        _kindTag.Text = ViewModel.CollectionType switch { "manual" => "Manual", "smart" => "Smart", _ => "Synced list" };
        PageSubtitle.Text = ViewModel.IsEditing ? $"{ViewModel.SourceItemCountText} titles" + (ViewModel.IsImportedCollection ? " · " + SavedSourceLabel() : "") : _newSynced ? "Pick the list, check the details, then press Create collection." : "Not created yet";
        PageSubtitle.Visibility = ViewModel.IsEditing || _newSynced ? Visibility.Visible : Visibility.Collapsed;
        _headerCover.Visibility = string.IsNullOrWhiteSpace(ViewModel.CurrentPosterUrl) ? Visibility.Collapsed : Visibility.Visible;
        if (_headerPosterUrl != ViewModel.CurrentPosterUrl)
        {
            _headerPosterUrl = ViewModel.CurrentPosterUrl;
            _headerPoster.Source = string.IsNullOrWhiteSpace(_headerPosterUrl) ? null : (ImageSource)new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(_headerPosterUrl, typeof(ImageSource), null!, "");
        }
        _showOnly.Visibility = ViewModel.CollectionType == "smart" ? Visibility.Collapsed : Visibility.Visible;
        _where.Children[1].Visibility = ViewModel.AvailableProfiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateWhereRows();
        ImportedProfileAccessSection2.Visibility = Visibility.Collapsed;
        ManualItemsSection.Visibility = ViewModel.CollectionType == "manual" ? Visibility.Visible : Visibility.Collapsed;
        SmartRulesSection.Visibility = ViewModel.CollectionType == "smart" ? Visibility.Visible : Visibility.Collapsed;
        _smartPreviewPanel.Visibility = SmartRulesSection.Visibility; LayoutSmartPreview();
        foreach (var control in ImportedLibrariesPanel.Children.OfType<Control>()) control.IsEnabled = ViewModel.CollectionType != "trakt";
        MaxItemsTextBox.IsEnabled = ViewModel.CollectionType != "trakt";
        _titleLimit.IsEnabled = _syncedMatchButton.IsEnabled = ViewModel.CollectionType != "trakt" && !ViewModel.IsSaving && !ViewModel.IsReadOnly;
        ImportedSourceSection.Visibility = ViewModel.IsImportedCollection ? Visibility.Visible : Visibility.Collapsed;
        CollectionDirtyDock.Visibility = Visibility.Collapsed; EditorActions.Visibility = Visibility.Visible;
        SaveButtonText.Text = ViewModel.IsEditing ? "Save changes" : "Create collection";
        EditorPrimaryColumn.IsHitTestVisible = !ViewModel.IsSaving;
        EditorEditGuard.IsEnabled = !_initializingEditor && !ViewModel.IsLoading && !ViewModel.IsSaving && !ViewModel.IsReadOnly;
        SaveButton.IsEnabled = !_initializingEditor && !ViewModel.IsLoading && !ViewModel.IsSaving && !ViewModel.IsLoadUnavailable;
        SaveButton.IsEnabled &= !string.IsNullOrWhiteSpace(ViewModel.Name);
        UpdateManualContents(phone);
        UpdateEditorFooter(phone);
        WatchFilterCombo.MinHeight = MediaFilterCombo.MinHeight = 36;
        _syncedCreatePanel.Visibility = _newSynced ? Visibility.Visible : Visibility.Collapsed;
        PanelStyle(_syncedCreatePanel, phone ? 20 : 24);
        UpdateSyncedResponsive();
        UpdateHeaderActions(phone);
        UpdateLookPresentation(phone);
        _look.Children[1].Visibility = _lookOpen ? Visibility.Visible : Visibility.Collapsed;
        _settingSchedule = true;
        _savedSchedule.SelectedItem = _savedSchedule.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, ViewModel.SyncSchedule ?? ""));
        _savedSchedule.IsEnabled = ViewModel.CanEditSavedSyncSchedule && !ViewModel.IsSaving && !ViewModel.IsReadOnly;
        _settingSchedule = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_lookToggle, _lookOpen ? "Done changing artwork" : "Change artwork");
    }
}
