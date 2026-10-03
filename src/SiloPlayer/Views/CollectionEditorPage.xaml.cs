using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage : Page
{
    public CollectionEditorViewModel ViewModel { get; }
    private Dictionary<string, string>? _draftBaseline;
    private string? _editingCollectionId;
    private bool _editorActive;
    private int _editorLoadGeneration;
    private SiloPlayer.Controls.QueryRulesEditor? _rulesEditor;
    private byte[]? _renderedPosterBytes;
    private string? _renderedPosterUrl;
    private int _posterPreviewGeneration;
    private bool _suppressImportedSortEvents = true;
    private readonly SiloPlayer.Controls.CatalogSortChoices.Choice[] _importedSortChoices;
    private IReadOnlySet<string> _shownRatingSources = new HashSet<string>();

    public CollectionEditorPage()
    {
        ViewModel = App.Services.GetRequiredService<CollectionEditorViewModel>();
        this.InitializeComponent();
        ImportedRemoveSelectedPoster.Content = SiloPlayer.Controls.WebUiIcon.Create("x", 12);
        ImportedRemovePosterButton.Content = SiloPlayer.Controls.WebUiIcon.Create("x", 12);
        _importedSortChoices = SiloPlayer.Controls.CatalogSortChoices.Capture(ImportedDefaultSortCombo);
        SiloPlayer.Controls.CatalogSortChoices.Apply(ImportedDefaultSortCombo, _importedSortChoices, _shownRatingSources, null, "");
        _suppressImportedSortEvents = false;
        foreach (var image in new[] { ImportedPosterPreviewFrame, PosterDraftPreviewFrame })
        {
            image.SizeChanged += (_, _) => ClipPosterPreview(image);
            image.Loaded += (_, _) => ClipPosterPreview(image);
        }

        ViewModel.Saved += OnSaved;
        ViewModel.Deleted += OnDeleted;

        ViewModel.Rules.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildRulesUI);

        ViewModel.ManualItems.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildManualItemsUI);

        ViewModel.SearchResults.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildSearchResultsUI);

        ViewModel.PreviewItems.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildPreviewItemsUI);
        ViewModel.SelectedLibraryIds.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => { UpdateEditorSummary(); UpdateDirtyDock(); });
        ViewModel.AllowedProfileIds.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => { UpdateEditorSummary(); UpdateDirtyDock(); });
        ViewModel.PropertyChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => { UpdateEditorSummary(); UpdateDirtyDock(); UpdateRecoveryShell(); });
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.PosterFileBytes) or nameof(ViewModel.CurrentPosterUrl))
                DispatcherQueue.TryEnqueue(async () => await RefreshPosterPreviewAsync());
        };
        SizeChanged += CollectionEditorPage_SizeChanged;
    }

    private void CollectionEditorPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var horizontalMargin = e.NewSize.Width < 640 ? 16 : e.NewSize.Width < 1024 ? 24 : 40;
        CollectionEditorShell.Margin = new Thickness(horizontalMargin, e.NewSize.Width < 640 ? 16 : 24, horizontalMargin, 48);
        var stacked = e.NewSize.Width < 1280;
        EditorSidebarColumn.Width = stacked ? new GridLength(0) : new GridLength(352);
        EditorBodyGrid.ColumnSpacing = stacked ? 0 : 32;
        Grid.SetColumn(EditorSidebar, stacked ? 0 : 1);
        Grid.SetRow(EditorSidebar, stacked ? 1 : 0);
        EditorSidebar.Margin = stacked ? new Thickness(0, 24, 0, 0) : new Thickness(0);

        foreach (var section in EditorPrimaryColumn.Children.OfType<Border>())
        {
            if (section.Child is not Grid grid || grid.ColumnDefinitions.Count != 2 || grid.ColumnDefinitions[0].Width.Value is not (220 or 224 or 0)) continue;
            var compact = e.NewSize.Width < 1024;
            if (grid.RowDefinitions.Count == 0) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowSpacing = 18; }
            grid.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(220);
            foreach (var element in grid.Children.OfType<FrameworkElement>())
            {
                var content = Grid.GetColumn(element) == 1 || Grid.GetRow(element) == 1;
                Grid.SetColumn(element, compact ? 0 : content ? 1 : 0); Grid.SetRow(element, compact && content ? 1 : 0); Grid.SetColumnSpan(element, compact ? 2 : 1);
            }
        }
        var compactBanner = e.NewSize.Width < 760;
        Grid.SetColumn(SourceBannerActions, compactBanner ? 0 : 2);
        Grid.SetColumnSpan(SourceBannerActions, compactBanner ? 3 : 1);
        Grid.SetRow(SourceBannerActions, compactBanner ? 1 : 0);
        SourceBannerActions.HorizontalAlignment = compactBanner ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SourceBannerActions.Margin = compactBanner ? new Thickness(0, 28, 0, 0) : new Thickness(0);
        ApplyImportedPresentation(e.NewSize.Width);
    }

    private void ApplyImportedPresentation(double width)
    {
        if (!ViewModel.IsImportedCollection || width <= 0) return;
        PageTitle.FontSize = Math.Clamp(width * .04, 32, 48);
        PageTitle.LineHeight = PageTitle.FontSize;
        PageTitle.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        PageSubtitle.FontSize = width < 640 ? 14 : 16;
        PageSubtitle.MaxWidth = double.PositiveInfinity;
        NameTextBox.MaxWidth = DescriptionTextBox.MaxWidth = double.PositiveInfinity;
        NameTextBox.HorizontalAlignment = DescriptionTextBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        NameTextBox.Height = 44; NameTextBox.FontSize = 15.2;
        DescriptionTextBox.Height = 88;
        DescriptionTextBox.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ImportedSourceBanner.Padding = new Thickness(width < 640 ? 24 : 32);
        ImportedSourceBanner.CornerRadius = new CornerRadius(27.2);
        SourceBrandMark.Width = SourceBrandMark.Height = width < 640 ? 64 : 72;
        SourceBannerDescription.FontSize = 14;
        SourceBannerDescription.LineHeight = 22.75;
        SourceBannerDescription.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        OpenSourceButton.Height = SourceSyncButton.Height = 32;
        OpenSourceButton.FontSize = SourceSyncButton.FontSize = 14;
        var narrowFilters = width < 640;
        Grid.SetColumn(MediaFilterCombo, narrowFilters ? 0 : 1);
        Grid.SetRow(MediaFilterCombo, narrowFilters ? 1 : 0);
        Grid.SetColumnSpan(WatchFilterCombo, narrowFilters ? 2 : 1);
        Grid.SetColumnSpan(MediaFilterCombo, narrowFilters ? 2 : 1);
        WatchFilterCombo.MinHeight = MediaFilterCombo.MinHeight = 44;
        SourceSpecSheet.CornerRadius = new CornerRadius(22.4);
        SourceSpecSheet.Padding = new Thickness(20);
        SidebarTitle.Text = "SOURCE DETAILS"; SidebarTitle.FontSize = 11; SidebarTitle.CharacterSpacing = 200;
        foreach (var text in new[] { SummaryModeLabel, SummaryLibrariesLabel, SummarySharedLabel, SummaryProfilesLabel }) text.FontSize = 11;
        foreach (var text in new[] { SummaryModeText, SummaryLibrariesText, SummarySharedText, SummaryProfilesText }) text.FontSize = 12;
        CollectionDirtyDock.CornerRadius = new CornerRadius(999);
        CollectionDirtyDock.HorizontalAlignment = HorizontalAlignment.Center;
        CollectionDirtyDock.Margin = new Thickness(16, 0, 16, 20);
        CollectionDirtyDock.Padding = new Thickness(14, 8, 14, 8);
        CollectionDirtyCount.FontSize = 12;
        foreach (var button in Descendants<Button>(CollectionDirtyDock))
        { button.Height = 32; button.FontSize = 12; button.Padding = new Thickness(12, 0, 12, 0); }
        EditorPrimaryColumn.Spacing = 0;
        ImportedEditorSurface.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        ImportedEditorSurface.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        ImportedEditorSurface.BorderThickness = new Thickness(1);
        ImportedEditorSurface.CornerRadius = new CornerRadius(24);
        var first = true;
        foreach (var section in new[] { BasicInfoSection, ImportedSourceSection, ImportedSharingSection, ImportedVisibilitySection, ImportedPosterSection })
        {
            section.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            section.CornerRadius = new CornerRadius(0);
            section.BorderThickness = first ? new Thickness(0) : new Thickness(0, 1, 0, 0);
            section.Padding = width < 640 ? new Thickness(24, 28, 24, 28) : new Thickness(32);
            first = false;
            if (section.Child is Grid sectionGrid)
            {
                sectionGrid.ColumnSpacing = 40;
                sectionGrid.RowSpacing = 24;
                if (sectionGrid.ColumnDefinitions.Count == 2)
                    sectionGrid.ColumnDefinitions[0].Width = width < 1024 ? new GridLength(0) : new GridLength(224);
            }
            foreach (var text in Descendants<TextBlock>(section))
            {
                if (text.FontSize == 18) text.FontSize = 16;
                if (text.Text is "Name" or "Description" or "Default Sort")
                { text.Text = text.Text.ToUpperInvariant(); text.FontSize = 11; text.CharacterSpacing = 140; }
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e); _editorActive = true;

        if (e.Parameter is string collectionId && !string.IsNullOrEmpty(collectionId))
        {
            _editingCollectionId = collectionId;
            await LoadEditorAsync(collectionId);
        }
        else
        {
            CollectionNotFoundState.Visibility = Visibility.Collapsed;
            await ViewModel.LoadReferenceDataCommand.ExecuteAsync(null);
            BuildImportedOptionsUI();
            UpdateEditorSummary();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    { _editorActive = false; ++_editorLoadGeneration; base.OnNavigatedFrom(e); }

    private async Task LoadEditorAsync(string collectionId)
    {
        var generation = ++_editorLoadGeneration;
        await ViewModel.LoadExistingCommand.ExecuteAsync(collectionId);
        _shownRatingSources = await SiloPlayer.Controls.CatalogSortChoices.LoadShownSourcesAsync(App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>());
        if (!_editorActive || generation != _editorLoadGeneration) return;
        UpdateRecoveryShell();
        if (ViewModel.IsNotFound || ViewModel.IsLoadUnavailable) return;
            PageTitle.Text = ViewModel.IsImportedCollection
                ? ViewModel.Name
                : $"Edit {ViewModel.Name}";
            PageSubtitle.Text = ViewModel.IsImportedCollection
                ? "Edit what's local — name, libraries, sharing. Source-managed details (URL, schedule, item ordering) are locked."
                : ViewModel.CollectionType == "manual"
                    ? "Manual collections are curated by adding titles directly."
                    : "Tune the collection settings and preview its matching titles.";
            SaveButtonText.Text = "Save changes";
            // Disable type switching when editing
            ManualTypeButton.IsEnabled = false;
            SmartTypeButton.IsEnabled = false;
            UpdateTypeToggleUI();
            UpdateSectionVisibility();
            BuildImportedOptionsUI();
            ApplyReadOnlyState();
            UpdateSourceBanner();
            UpdateEditorSummary();
            _draftBaseline = CaptureDraft(); UpdateDirtyDock();
    }
    private async void RetryCollection_Click(object sender, RoutedEventArgs e)
    { if (!ViewModel.IsLoading && _editingCollectionId is { } id) await LoadEditorAsync(id); }
    private void AllCollections_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().Navigate<CollectionsPage>();
    private void UpdateRecoveryShell()
    {
        CollectionNotFoundState.Visibility = ViewModel.IsNotFound ? Visibility.Visible : Visibility.Collapsed;
        CollectionUnavailableState.Visibility = ViewModel.IsLoadUnavailable ? Visibility.Visible : Visibility.Collapsed;
        CollectionEditorScroll.Visibility = ViewModel.IsLoading || ViewModel.IsNotFound || ViewModel.IsLoadUnavailable ? Visibility.Collapsed : Visibility.Visible;
        if (ViewModel.IsNotFound || ViewModel.IsLoadUnavailable || ViewModel.IsLoading) CollectionDirtyDock.Visibility = Visibility.Collapsed;
    }

    private void OnSaved()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (!_editorActive) return;
            if (ViewModel.IsImportedCollection && ViewModel.CollectionId is { } id)
                await LoadEditorAsync(id);
            else OnDeleted();
        });
    }

    private void OnDeleted() => DispatcherQueue.TryEnqueue(() =>
        App.Services.GetRequiredService<NavigationService>().Navigate<CollectionsPage>());

    private async void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CollectionId))
        {
            App.Services.GetRequiredService<NavigationService>().GoBack();
            return;
        }

        await ViewModel.LoadExistingCommand.ExecuteAsync(ViewModel.CollectionId);
        UpdateTypeToggleUI();
        UpdateSectionVisibility();
        BuildImportedOptionsUI();
        ApplyReadOnlyState();
        UpdateSourceBanner();
        UpdateEditorSummary();
        _draftBaseline = CaptureDraft(); UpdateDirtyDock();
    }

    private Dictionary<string, string> CaptureDraft() => new()
    {
        ["name"] = ViewModel.Name, ["description"] = ViewModel.Description ?? "", ["url"] = ViewModel.SourceUrl ?? "",
        ["limit"] = ViewModel.MaxItemsText ?? "", ["schedule"] = ViewModel.SyncSchedule ?? "", ["poster"] = ViewModel.PosterSourceUrl ?? "",
        ["poster_file"] = ViewModel.PosterFileName ?? "", ["libraries"] = string.Join(",", ViewModel.SelectedLibraryIds.Order()),
        ["profiles"] = string.Join(",", ViewModel.AllowedProfileIds.Order()), ["shared"] = ViewModel.IsShared.ToString(),
        ["visible"] = ViewModel.IncludeInServerCollections.ToString(), ["watch"] = ViewModel.WatchFilter, ["media"] = ViewModel.MediaFilter,
        ["sort"] = ViewModel.DefaultSortValue,
    };

    private void UpdateDirtyDock()
    {
        if (CollectionDirtyDock == null) return;
        var count = _draftBaseline == null ? 0 : CaptureDraft().Count(pair => _draftBaseline.GetValueOrDefault(pair.Key) != pair.Value);
        var imported = ViewModel.IsImportedCollection;
        EditorActions.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        CollectionDirtyDock.Visibility = imported && count > 0 && !ViewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        CollectionDirtyCount.Text = $"{count} change{(count == 1 ? "" : "s")}";
        DockSaveButton.IsEnabled = !ViewModel.IsSaving;
        CollectionEditorScroll.Padding = new Thickness(0, 0, 0, imported && count > 0 ? 100 : 0);
    }

    // ===== Type Toggle =====

    private void ManualType_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CollectionType = "manual";
        UpdateTypeToggleUI();
        UpdateSectionVisibility();
    }

    private void SmartType_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CollectionType = "smart";
        UpdateTypeToggleUI();
        UpdateSectionVisibility();
    }

    private void UpdateTypeToggleUI()
    {
        bool isManual = ViewModel.CollectionType == "manual";
        bool isSmart = ViewModel.CollectionType == "smart";

        ManualTypeButton.Style = isManual
            ? (Style)Resources["TypeToggleActiveStyle"]
            : (Style)Resources["TypeToggleInactiveStyle"];

        SmartTypeButton.Style = isSmart
            ? (Style)Resources["TypeToggleActiveStyle"]
            : (Style)Resources["TypeToggleInactiveStyle"];

        TypeDescription.Text = ViewModel.CollectionType switch
        {
            "manual" => "Manually add items to this collection.",
            "smart" => "Automatically match items based on rules.",
            "mdblist" => "Synced from MDBList.",
            "tmdb" => "Synced from TMDB.",
            "trakt" => "Synced from Trakt.",
            _ => "Synced collection."
        };
    }

    private void UpdateSectionVisibility()
    {
        var imported = ViewModel.IsImportedCollection;
        ManualItemsSection.Visibility = ViewModel.CollectionType == "manual" ? Visibility.Visible : Visibility.Collapsed;
        SmartRulesSection.Visibility = ViewModel.CollectionType == "smart" ? Visibility.Visible : Visibility.Collapsed;
        ImportedSourceSection.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        ImportedSourceBanner.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        ImportedDisplayOptions.Visibility = imported || ViewModel.CollectionType == "manual" ? Visibility.Visible : Visibility.Collapsed;
        ImportedSharingSection.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        ImportedVisibilitySection.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        ImportedPosterSection.Visibility = imported && !ViewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        DeleteCollectionButton.Visibility = imported && !ViewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        TypeSection.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        PosterSection.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        SharingSection.Visibility = imported ? Visibility.Collapsed : Visibility.Visible;
        ImportedProfileAccessSection.Visibility = imported
            ? Visibility.Collapsed
            : ViewModel.IsShared ? Visibility.Visible : Visibility.Collapsed;
        DisplaySectionNumber.Visibility = imported ? Visibility.Visible : Visibility.Collapsed;
        BasicInfoTitle.Text = imported ? "Display" : "Basics";
        SourceUrlTextBox.IsEnabled = ViewModel.HasEditableSourceUrl;
        SourceUrlSection.Visibility = ViewModel.HasEditableSourceUrl
            ? Visibility.Visible
            : Visibility.Collapsed;
        SourcePresetSection.Visibility = imported && !ViewModel.HasEditableSourceUrl
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateEditorSummary();
        ApplyImportedPresentation(ActualWidth);
    }

    private void BuildImportedOptionsUI()
    {
        ImportedLibrariesPanel.Children.Clear();
        foreach (var library in ViewModel.AvailableLibraries)
        {
            var check = new CheckBox
            {
                Content = $"{library.Name} · {library.Type}",
                Tag = library.Id,
                IsChecked = ViewModel.SelectedLibraryIds.Contains(library.Id)
            };
            check.Checked += ImportedLibrary_Checked;
            check.Unchecked += ImportedLibrary_Checked;
            ImportedLibrariesPanel.Children.Add(check);
        }

        ImportedProfilesPanel.Children.Clear();
        ImportedProfilesPanel2.Children.Clear();
        foreach (var profile in ViewModel.AvailableProfiles)
        {
            CheckBox BuildProfileCheck() => new()
            {
                Content = profile.IsPrimary ? $"{profile.Name} · Primary" : profile.Name,
                Tag = profile.Id,
                IsChecked = ViewModel.AllowedProfileIds.Contains(profile.Id)
            };
            var legacyCheck = BuildProfileCheck();
            legacyCheck.Checked += ImportedProfile_Checked;
            legacyCheck.Unchecked += ImportedProfile_Checked;
            ImportedProfilesPanel.Children.Add(legacyCheck);
            var importedCheck = BuildProfileCheck();
            importedCheck.Checked += ImportedProfile_Checked;
            importedCheck.Unchecked += ImportedProfile_Checked;
            ImportedProfilesPanel2.Children.Add(importedCheck);
        }

        SelectByTag(WatchFilterCombo, ViewModel.WatchFilter);
        SelectByTag(MediaFilterCombo, ViewModel.MediaFilter);
        _suppressImportedSortEvents = true;
        try
        {
            SiloPlayer.Controls.CatalogSortChoices.Apply(ImportedDefaultSortCombo, _importedSortChoices,
                _shownRatingSources, null, ViewModel.DefaultSortValue, keepSavedEditorSort: true);
        }
        finally { _suppressImportedSortEvents = false; }
        LastSyncSummaryText.Text = ViewModel.LastSyncSummary ?? "Not synced yet";
        RemovePosterButton.Visibility = string.IsNullOrWhiteSpace(ViewModel.CurrentPosterUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ImportedRemovePosterButton.Visibility = RemovePosterButton.Visibility;
        ImportedProfileAccessSection.Visibility = !ViewModel.IsImportedCollection && ViewModel.IsShared
            ? Visibility.Visible
            : Visibility.Collapsed;
        ImportedProfileAccessSection2.Visibility = ViewModel.IsImportedCollection && ViewModel.IsShared
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSourceBanner();
        UpdateEditorSummary();
    }

    private void UpdateEditorSummary()
    {
        if (SummaryModeText == null) return;
        if (ViewModel.IsImportedCollection)
        {
            SidebarTitle.Text = "Source details";
            SidebarSubtitle.Text = "Captured when the collection was imported.";
            SummaryModeLabel.Text = $"{ViewModel.SourceProviderLabel} preset";
            SummaryModeText.Text = ViewModel.SourcePresetSummary;
            SummaryLibrariesLabel.Text = "Items";
            SummaryLibrariesText.Text = ViewModel.SourceItemCountText;
            SummarySharedLabel.Text = "Last note";
            SummarySharedText.Text = ViewModel.SourceLastNote ?? "—";
            SummaryProfilesLabel.Text = "Created";
            SummaryProfilesText.Text = ViewModel.CreatedDisplayText;
            SummaryLibraryTabLabel.Text = "Source URL";
            SummaryLibraryTabText.Text = ViewModel.SourceUrl ?? "";
            SummaryLibraryTabText.TextTrimming = TextTrimming.CharacterEllipsis;
            SummaryLibraryTabText.MaxWidth = 192;
            SummaryLibraryTabLabel.Visibility = SummaryLibraryTabText.Visibility = string.IsNullOrWhiteSpace(ViewModel.SourceUrl) ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetRow(SummaryLibraryTabLabel, 1); Grid.SetRow(SummaryLibraryTabText, 1);
            Grid.SetRow(SummaryLibrariesLabel, 2); Grid.SetRow(SummaryLibrariesText, 2);
            Grid.SetRow(SummarySharedLabel, 3); Grid.SetRow(SummarySharedText, 3);
            Grid.SetRow(SummaryProfilesLabel, 4); Grid.SetRow(SummaryProfilesText, 4);
            SummaryCollectionLabel.Visibility = Visibility.Collapsed;
            SummaryCollectionText.Visibility = Visibility.Collapsed;
            return;
        }

        SidebarTitle.Text = "Collection Summary";
        SidebarSubtitle.Text = "Preview and sharing stay visible while you edit.";
        SummaryLibraryTabLabel.Visibility = Visibility.Visible;
        SummaryLibraryTabText.Visibility = Visibility.Visible;
        SummaryCollectionLabel.Visibility = Visibility.Visible;
        SummaryCollectionText.Visibility = Visibility.Visible;
        SummaryModeText.Text = ViewModel.CollectionType switch
        {
            "smart" => "Smart",
            "mdblist" => "MDBList",
            "tmdb" => "TMDB",
            "tmdb_list" => "TMDB List",
            "trakt" => "Trakt",
            _ => "Manual",
        };
        var selectedLibraries = ViewModel.AvailableLibraries
            .Where(library => ViewModel.SelectedLibraryIds.Contains(library.Id))
            .Select(library => library.Name)
            .ToList();
        SummaryLibrariesText.Text = selectedLibraries.Count == 0
            ? "All libraries"
            : string.Join(", ", selectedLibraries);
        SummarySharedText.Text = ViewModel.IsShared ? "Yes" : "No";
        SummaryProfilesText.Text = ViewModel.AllowedProfileIds.Count == 0
            ? "All profiles"
            : $"{ViewModel.AllowedProfileIds.Count} selected";
        SummaryLibraryTabText.Text = ViewModel.IncludeInServerCollections ? "Yes" : "No";
        SummaryCollectionText.Text = string.IsNullOrWhiteSpace(ViewModel.Name) ? "New" : ViewModel.Name;
    }

    private void UpdateSourceBanner()
    {
        if (!ViewModel.IsImportedCollection || SourceBrandLabel == null) return;
        var source = ViewModel.SourceKind.ToLowerInvariant();
        var (label, initials, tagline) = source switch
        {
            "tmdb" => ("TMDB", "Tm", "The Movie Database"),
            "tmdb_list" => ("TMDB", "Tm", "a public TMDB list"),
            "trakt" => ("TRAKT", "Tk", "Trakt.tv"),
            _ => ("MDBLIST", "Mb", "mdblist.com"),
        };
        SourceBrandLabel.Text = label;
        SourceBrandInitials.Text = initials;
        SourcePresetLabel.Text = ViewModel.SourcePresetSummary;
        SourcePresetHeader.Text = $"{label} PRESET";
        SourcePresetReadoutText.Text = ViewModel.SourcePresetSummary;
        SourcePresetLockText.Text = label;
        SourceBannerDescription.Text = $"Synced from {tagline} — items, posters, and ordering are managed by the source.";
        SourceBannerSyncStatus.Text = ViewModel.LastSyncSummary ?? "Not yet synced";
        var accent = source switch { "tmdb" or "tmdb_list" => Windows.UI.Color.FromArgb(255, 34, 211, 238), "trakt" => Windows.UI.Color.FromArgb(255, 251, 113, 133), _ => Windows.UI.Color.FromArgb(255, 251, 191, 36) };
        var soft = accent; soft.A = 32;
        SourceBrandMark.BorderBrush = SourceBrandInitials.Foreground = SourceBrandLabel.Foreground = new SolidColorBrush(accent);
        SourceBrandBadge.Background = new SolidColorBrush(soft);
        SourceBrandMark.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        SourceSyncButton.Background = new SolidColorBrush(accent);
        SourceSyncButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 23, 34));
        var haloFrom = source switch { "tmdb" or "tmdb_list" => Windows.UI.Color.FromArgb(82, 56, 189, 248), "trakt" => Windows.UI.Color.FromArgb(77, 244, 63, 94), _ => Windows.UI.Color.FromArgb(82, 251, 191, 36) };
        var haloTo = source switch { "tmdb" or "tmdb_list" => Windows.UI.Color.FromArgb(46, 16, 185, 129), "trakt" => Windows.UI.Color.FromArgb(46, 251, 146, 60), _ => Windows.UI.Color.FromArgb(46, 249, 115, 22) };
        ImportedSourceBanner.Background = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1), GradientStops = { new() { Color = haloFrom, Offset = 0 }, new() { Color = Microsoft.UI.Colors.Transparent, Offset = .38 }, new() { Color = Microsoft.UI.Colors.Transparent, Offset = .62 }, new() { Color = haloTo, Offset = 1 } } };
        ImportedSourceBanner.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        OpenSourceButton.Visibility = source is "mdblist" or "tmdb_list" && Uri.TryCreate(ViewModel.SourceUrl, UriKind.Absolute, out _)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(ViewModel.SourceUrl, UriKind.Absolute, out var sourceUri))
            await Windows.System.Launcher.LaunchUriAsync(sourceUri);
    }

    private void ApplyReadOnlyState()
    {
        var editable = !ViewModel.IsReadOnly;
        SetDescendantControlsEnabled(BasicInfoSection, editable);
        SetDescendantControlsEnabled(ImportedSourceSection, editable);
        SetDescendantControlsEnabled(ImportedSharingSection, editable);
        SetDescendantControlsEnabled(ImportedVisibilitySection, editable);
        SetDescendantControlsEnabled(ImportedPosterSection, editable);
        SetDescendantControlsEnabled(ManualItemsSection, editable);
        SetDescendantControlsEnabled(SmartRulesSection, editable);
        SaveButton.IsEnabled = editable;

        // Editing never permits changing the collection's fundamental type,
        // and only MDBList sources expose an editable source URL.
        var isExistingCollection = !string.IsNullOrWhiteSpace(ViewModel.CollectionId);
        ManualTypeButton.IsEnabled = editable && !isExistingCollection;
        SmartTypeButton.IsEnabled = editable && !isExistingCollection;
        SourceUrlTextBox.IsEnabled = editable
            && ViewModel.HasEditableSourceUrl;
        if (!editable) PageTitle.Text = $"{ViewModel.Name} · Read-only";
    }

    private static void SetDescendantControlsEnabled(DependencyObject root, bool enabled)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control control) control.IsEnabled = enabled;
            SetDescendantControlsEnabled(child, enabled);
        }
    }

    private static void SelectByTag(ComboBox combo, string value)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, value, StringComparison.Ordinal));
    }

    private void ImportedLibrary_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: int id } check) return;
        if (check.IsChecked == true)
        {
            if (!ViewModel.SelectedLibraryIds.Contains(id)) ViewModel.SelectedLibraryIds.Add(id);
        }
        else ViewModel.SelectedLibraryIds.Remove(id);
    }

    private void ImportedProfile_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } check) return;
        if (check.IsChecked == true)
        {
            if (!ViewModel.AllowedProfileIds.Contains(id)) ViewModel.AllowedProfileIds.Add(id);
        }
        else ViewModel.AllowedProfileIds.Remove(id);
    }

    private void WatchFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (WatchFilterCombo.SelectedItem is ComboBoxItem { Tag: string value }) ViewModel.WatchFilter = value;
    }

    private void MediaFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MediaFilterCombo.SelectedItem is ComboBoxItem { Tag: string value }) ViewModel.MediaFilter = value;
    }

    private void ImportedDefaultSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressImportedSortEvents) return;
        if (ImportedDefaultSortCombo.SelectedItem is ComboBoxItem { Tag: string value })
            ViewModel.DefaultSortValue = value;
    }

    private void SharedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ImportedProfileAccessSection != null)
            ImportedProfileAccessSection.Visibility = !ViewModel.IsImportedCollection && SharedToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ImportedSharedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ImportedProfileAccessSection2 != null)
            ImportedProfileAccessSection2.Visibility = ImportedSharedToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SyncNowCommand.ExecuteAsync(null);
        LastSyncSummaryText.Text = ViewModel.LastSyncSummary ?? "Not synced yet";
    }

    private async void ChoosePoster_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp" })
                picker.FileTypeFilter.Add(extension);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            await ApplyPosterFileAsync(file);
        }
        catch (Exception ex)
        {
            PosterFileStatusText.Text = ex.Message;
            ImportedPosterFileStatusText.Text = ex.Message;
        }
    }

    private async Task ApplyPosterFileAsync(Windows.Storage.StorageFile file)
    {
            if (file.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return;
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 20 * 1024 * 1024)
            {
                PosterFileStatusText.Text = "Image must be smaller than 20 MB.";
                ImportedPosterFileStatusText.Text = PosterFileStatusText.Text;
                return;
            }
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            ViewModel.SetPosterFile(file.Name, bytes, file.ContentType);
            PosterSourceUrlTextBox.Text = "";
            ImportedPosterSourceUrlTextBox.Text = "";
            PosterFileStatusText.Text = file.Name;
            ImportedPosterFileStatusText.Text = file.Name;
            await RefreshPosterPreviewAsync();
    }

    private void PosterDrop_DragOver(object sender, DragEventArgs e)
    { e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None; e.Handled = true; }

    private async void PosterDrop_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            await ApplyPosterDropAsync(e.DataView);
        }
        catch (Exception ex) { ImportedPosterFileStatusText.Text = ex.Message; }
        finally { deferral.Complete(); }
    }

    private async Task ApplyPosterDropAsync(DataPackageView data)
    {
        if (data.Contains(StandardDataFormats.StorageItems) && (await data.GetStorageItemsAsync()).FirstOrDefault() is Windows.Storage.StorageFile file)
            await ApplyPosterFileAsync(file);
    }

    private void RemoveSelectedPoster_Click(object sender, RoutedEventArgs e) => ViewModel.ClearPosterFile();

    private static void ClipPosterPreview(Border image)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(image);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new((float)image.ActualWidth, (float)image.ActualHeight); geometry.CornerRadius = new(12, 12);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }

    private async Task RefreshPosterPreviewAsync()
    {
        var bytes = ViewModel.PosterFileBytes;
        var url = bytes is { Length: > 0 } ? null : ViewModel.CurrentPosterUrl;
        if (ReferenceEquals(bytes, _renderedPosterBytes) && url == _renderedPosterUrl) return;
        _renderedPosterBytes = bytes; _renderedPosterUrl = url; var generation = ++_posterPreviewGeneration;
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? bitmap = null;
        if (bytes is { Length: > 0 })
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
            stream.Seek(0); bitmap = new(); await bitmap.SetSourceAsync(stream);
        }
        else if (!string.IsNullOrWhiteSpace(url)) bitmap = new(new Uri(App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().ResolveServerUrl(url)));
        if (generation != _posterPreviewGeneration) return;
        ImportedPosterPreview.Source = bitmap; PosterDraftPreview.Source = bitmap;
        ImportedPosterPreview.Visibility = PosterDraftPreview.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
        ImportedPosterPreviewFrame.Visibility = PosterDraftPreviewFrame.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
        ImportedPosterDropTarget.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
        ImportedRemoveSelectedPoster.Visibility = bytes is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        ImportedRemovePosterButton.Visibility = bytes is not { Length: > 0 } && !string.IsNullOrWhiteSpace(url) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RemovePoster_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "The collection will return to its generated artwork.",
            TextWrapping = TextWrapping.Wrap
        });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove poster?",
            Content = content,
            PrimaryButtonText = "Remove",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Removing...";
                error.Visibility = Visibility.Collapsed;
                await ViewModel.RemovePosterCommand.ExecuteAsync(null);
                if (string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
                {
                    RemovePosterButton.Visibility = Visibility.Collapsed;
                    ImportedRemovePosterButton.Visibility = Visibility.Collapsed;
                    PosterFileStatusText.Text = "Poster removed";
                    ImportedPosterFileStatusText.Text = "Poster removed";
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The poster could not be removed.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Remove";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private async void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete collection \"{ViewModel.Name}\"? This action cannot be undone.",
            TextWrapping = TextWrapping.Wrap
        });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete collection",
            Content = content,
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Deleting...";
                error.Visibility = Visibility.Collapsed;
                await ViewModel.DeleteCommand.ExecuteAsync(null);
                if (string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage;
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Delete";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    // ===== Navigation =====

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.GoBack();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (ViewModel.IsImportedCollection) nav.Navigate<CollectionsPage>();
        else nav.GoBack();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_rulesEditor?.IsValid == false) return;
        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    // ===== Smart Rules =====

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddRuleCommand.Execute(null);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_rulesEditor?.IsValid == false) return;
        await ViewModel.PreviewCommand.ExecuteAsync(null);
    }

    private void BuildRulesUI()
    {
        RulesPanel.Children.Clear();
        NoRulesText.Visibility = ViewModel.RuleDefinition.Groups.Sum(group => group.Rules.Count) == 0
            ? Visibility.Visible : Visibility.Collapsed;
        var editor = new SiloPlayer.Controls.QueryRulesEditor { IsEnabled = !ViewModel.IsReadOnly };
        _rulesEditor = editor;
        editor.Load(ViewModel.RuleDefinition); RulesPanel.Children.Add(editor);
    }

    private void BuildPreviewItemsUI()
    {
        PreviewItemsPanel.Children.Clear();

        if (ViewModel.PreviewItems.Count > 0)
        {
            PreviewCountText.Text = $"{ViewModel.PreviewTotal} items matched";
            PreviewCountText.Visibility = Visibility.Visible;

            foreach (var item in ViewModel.PreviewItems)
            {
                PreviewItemsPanel.Children.Add(BuildPreviewItemRow(item));
            }

            if (ViewModel.PreviewTotal > ViewModel.PreviewItems.Count)
            {
                PreviewItemsPanel.Children.Add(new TextBlock
                {
                    Text = $"...and {ViewModel.PreviewTotal - ViewModel.PreviewItems.Count} more",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    Margin = new Thickness(8, 4, 0, 0)
                });
            }
        }
        else
        {
            PreviewCountText.Visibility = Visibility.Collapsed;
        }
    }

    private Border BuildPreviewItemRow(CollectionPreviewItem item)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(8, 6, 8, 6)
        };

        // Type badge
        var typeBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = item.Type.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"]
            }
        };

        var titleText = new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        row.Children.Add(typeBadge);
        row.Children.Add(titleText);

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = row
        };
    }

    // ===== Manual Items =====

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = ViewModel.SearchItemsCommand.ExecuteAsync(SearchBox.Text);
        }
    }

    private async void SearchItems_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SearchItemsCommand.ExecuteAsync(SearchBox.Text);
    }

    private void BuildSearchResultsUI()
    {
        SearchResultsPanel.Children.Clear();

        foreach (var item in ViewModel.SearchResults)
        {
            SearchResultsPanel.Children.Add(BuildSearchResultRow(item));
        }
    }

    private Border BuildSearchResultRow(MediaItem item)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 6, 8, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        var typeBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = item.Type.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"]
            }
        };

        var titleText = new TextBlock
        {
            Text = $"{item.Title} ({item.Year})",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        infoPanel.Children.Add(typeBadge);
        infoPanel.Children.Add(titleText);

        // Check if already added
        bool alreadyAdded = ViewModel.ManualItems.Any(m =>
            m.MediaItemId == item.ContentId || m.ContentId == item.ContentId);

        Button addBtn;
        if (alreadyAdded)
        {
            addBtn = new Button
            {
                Content = "Added",
                IsEnabled = false,
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            addBtn = new Button
            {
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Content = "Add"
            };
            var capturedItem = item;
            addBtn.Click += async (_, _) =>
            {
                addBtn.IsEnabled = false;
                await ViewModel.AddManualItemCommand.ExecuteAsync(capturedItem);
                var added = ViewModel.ManualItems.Any(candidate => candidate.MediaItemId == capturedItem.ContentId);
                addBtn.Content = added ? "Added" : "Add"; addBtn.IsEnabled = !added;
            };
        }

        Grid.SetColumn(infoPanel, 0);
        Grid.SetColumn(addBtn, 1);
        row.Children.Add(infoPanel);
        row.Children.Add(addBtn);

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = row
        };

        border.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };
        border.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = null;
        };

        return border;
    }

    private void BuildManualItemsUI()
    {
        ManualItemsPanel.Children.Clear();

        bool hasItems = ViewModel.ManualItems.Count > 0;
        NoItemsText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ItemsSeparator.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        AddedItemsHeader.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        AddedItemsHeader.Text = $"Added Items ({ViewModel.ManualItems.Count})";

        for (int i = 0; i < ViewModel.ManualItems.Count; i++)
        {
            ManualItemsPanel.Children.Add(BuildManualItemRow(ViewModel.ManualItems[i], i));
        }
    }

    private Border BuildManualItemRow(CollectionItem item, int index)
    {
        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 6, 8, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var grip = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CanDrag = ViewModel.CanReorderManualItems && !ViewModel.IsManualMutationPending,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = "\uE700",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
            }
        };
        grip.DragStarting += (_, args) =>
        {
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText($"manual-item:{item.MediaItemId}");
        };

        // Position number
        var posText = new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Title info
        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrEmpty(item.Type))
        {
            titlePanel.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = item.Type.ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["AccentBrush"]
                }
            });
        }

        string displayTitle = item.Title ?? item.ContentId ?? item.MediaItemId;
        if (item.Year > 0)
            displayTitle += $" ({item.Year})";

        titlePanel.Children.Add(new TextBlock
        {
            Text = displayTitle,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var capturedItem = item;

        // Remove button
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var removeBtn = MakeSmallIconButton("\uE74D", "Remove");
        removeBtn.Click += async (_, _) => await ViewModel.RemoveManualItemCommand.ExecuteAsync(capturedItem);
        actionPanel.Children.Add(removeBtn);

        // Actually place in proper columns
        row.Children.Clear();
        row.ColumnDefinitions.Clear();

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(grip, 0);
        Grid.SetColumn(posText, 1);
        Grid.SetColumn(titlePanel, 2);
        Grid.SetColumn(actionPanel, 3);

        row.Children.Add(grip);
        row.Children.Add(posText);
        row.Children.Add(titlePanel);
        row.Children.Add(actionPanel);

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            AllowDrop = ViewModel.CanReorderManualItems && !ViewModel.IsManualMutationPending,
            Child = row
        };
        border.DragOver += (_, args) => args.AcceptedOperation = DataPackageOperation.Move;
        border.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            args.Handled = true;
            var payload = await args.DataView.GetTextAsync();
            const string prefix = "manual-item:";
            if (!payload.StartsWith(prefix, StringComparison.Ordinal)) return;
            var sourceId = payload[prefix.Length..];
            var source = ViewModel.ManualItems.FirstOrDefault(candidate => candidate.MediaItemId == sourceId);
            if (source == null) return;
            var oldIndex = ViewModel.ManualItems.IndexOf(source);
            var newIndex = ViewModel.ManualItems.IndexOf(item);
            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                await ViewModel.MoveManualItemAsync(oldIndex, newIndex);
        };

        border.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };
        border.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        };

        return border;
    }

    // ===== Helpers =====

    private static Button MakeSmallIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }


}
