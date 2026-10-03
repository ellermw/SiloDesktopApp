using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Services;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class SmartCollectionWizardPage : Page
{
    public SmartCollectionWizardViewModel ViewModel { get; }

    private SmartCollectionWizardNavigationArgs? _args;
    private bool _loaded;
    private bool _suppressSelectionChanges;
    private bool _compactLayout;
    private DispatcherTimer? _previewDebounceTimer;
    private CancellationTokenSource? _previewCts;
    private Task _previewTask = Task.CompletedTask;
    private readonly SemaphoreSlim _previewLifecycleGate = new(1, 1);
    private int _currentStep = 1;
    private QueryFilterEditor? _rulesEditor;
    private CancellationTokenSource? _filterOptionsCts;
    private IReadOnlySet<string> _shownRatingSources = new HashSet<string>();
    private CatalogSortChoices.Choice[] _wizardSortChoices = [];
    private int _pageLifetime;

    public bool CanSave => !ViewModel.IsSaving && !ViewModel.IsLoading
        && !ViewModel.IsReadOnly && !string.IsNullOrWhiteSpace(ViewModel.Title) && _rulesEditor?.IsValid != false;
    public string PreviewCountText
        => ViewModel.PreviewTotal > 0
            ? $"{ViewModel.PreviewTotal:N0} matched"
            : "Preview the first matching items before saving.";

    public SmartCollectionWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<SmartCollectionWizardViewModel>();
        this.InitializeComponent();
        WizardRemoveSelectedPoster.Content = WebUiIcon.Create("x", 12);
        WizardDeletePoster.Content = WebUiIcon.Create("x", 12);
        WizardPosterFrame.SizeChanged += (_, _) => ClipPosterPreview();
        WizardPosterFrame.Loaded += (_, _) => ClipPosterPreview();

        ViewModel.Saved += OnSaved;
        ViewModel.Rules.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(BuildRulesPanel);
        ViewModel.PreviewMediaItems.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePreviewState);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.IsSaving)
                or nameof(ViewModel.IsLoading)
                or nameof(ViewModel.IsPreviewing)
                or nameof(ViewModel.PreviewTotal)
                or nameof(ViewModel.IsReadOnly)
                or nameof(ViewModel.Title))
            {
                Bindings.Update();
                UpdateContinueState();
                UpdatePreviewState();
            }
        };
        SizeChanged += SmartCollectionWizardPage_SizeChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ++_pageLifetime;
        _args = e.Parameter as SmartCollectionWizardNavigationArgs;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        var lifetime = _pageLifetime;
        var catalogApi = App.Services.GetRequiredService<CatalogApi>();
        var context = catalogApi.CaptureContext();
        await ViewModel.ConfigureAsync(_args);
        if (!IsLoaded || lifetime != _pageLifetime || context != catalogApi.CaptureContext()) return;
        _shownRatingSources = await CatalogSortChoices.LoadShownSourcesAsync(catalogApi);
        if (!IsLoaded || lifetime != _pageLifetime || context != catalogApi.CaptureContext()) return;
        PopulateStaticCombos();
        ApplyReadOnlyState();
        BuildLibrariesPanel();
        BuildProfilesPanel();
        BuildRulesPanel();
        WizardDeleteButton.Visibility = !string.IsNullOrWhiteSpace(_args?.CollectionId) && !ViewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        await UpdatePosterPreviewAsync();
        UpdatePreviewState();
        UpdateContinueState();
        WizardTitle.Text = string.IsNullOrWhiteSpace(_args?.CollectionId)
            ? "New Collection"
            : ViewModel.Title;
        SaveButtonText.Text = string.IsNullOrWhiteSpace(_args?.CollectionId)
            ? "Create Collection"
            : "Save Collection";
        ShowStep(1);
        Bindings.Update();
        await RunPreviewAsync();
    }

    private void PopulateStaticCombos()
    {
        _suppressSelectionChanges = true;

        AddComboItems(MediaScopeCombo, [
            ("All Media", ""),
            ("Movies & Series", "video"),
            ("Movies", "movie"),
            ("Series", "series"),
            ("Episodes", "episode"),
            ("Audiobooks", "audiobook"),
            ("Ebooks", "ebook"),
            ("Manga", "manga")
        ], ViewModel.MediaScope);

        SheetMediaScopeCombo.Items.Clear();
        foreach (var item in MediaScopeCombo.Items.OfType<ComboBoxItem>())
            SheetMediaScopeCombo.Items.Add(new ComboBoxItem { Content = item.Content, Tag = item.Tag });
        SheetMediaScopeCombo.SelectedIndex = MediaScopeCombo.SelectedIndex;

        var sortOptions = new List<(string Label, string Value)>
        {
            ("Date Added", "added_at"),
            ("Title", "title"),
            ("Release Date", "release_date"),
            ("Latest Episode Air Date", "last_air_date"),
            ("Latest Episode Added", "latest_episode_added"),
            ("Year", "year"),
            ("Content Rating", "content_rating"),
            ("Duration", "runtime"),
            ("IMDb Rating", "rating_imdb"),
            ("TMDB Rating", "rating_tmdb"),
            ("RT Critic Rating", "rating_rt_critic"),
            ("RT Audience Rating", "rating_rt_audience"),
            ("Resolution", "resolution"),
            ("Bitrate", "bitrate"),
            ("Author", "author"),
            ("Narrator", "narrator"),
            ("Series", "series")
        };
        sortOptions.Add(("Progress", "progress"));
        sortOptions.Add(("Date Viewed", "date_viewed"));
        sortOptions.Add(("Plays", "plays"));
        AddComboItems(SortFieldCombo, sortOptions, ViewModel.SortField);
        _wizardSortChoices = CatalogSortChoices.Capture(SortFieldCombo);
        RefreshSortChoices();

        AddComboItems(SortOrderCombo, [
            ("Descending", "desc"),
            ("Ascending", "asc")
        ], ViewModel.SortOrder);

        AddComboItems(MatchModeCombo, [
            ("Match all rules", "all"),
            ("Match any rule", "any")
        ], ViewModel.MatchMode);

        _suppressSelectionChanges = false;
    }

    private static void AddComboItems(ComboBox combo, IEnumerable<(string Label, string Value)> items, string selected)
    {
        combo.Items.Clear();
        foreach (var (label, value) in items)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            combo.Items.Add(item);
            if (value == selected)
                combo.SelectedItem = item;
        }

        if (combo.SelectedItem == null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void ApplyReadOnlyState()
    {
        SharedToggle.Visibility = Visibility.Visible;
        IncludeServerToggle.Visibility = Visibility.Visible;
        DescriptionPanel.Visibility = Visibility.Collapsed;
        ProfileAccessSection.Visibility = ViewModel.IsShared
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetDescendantControlsEnabled(DetailsStepPanel, !ViewModel.IsReadOnly);
    }

    private void BuildProfilesPanel()
    {
        ProfilesPanel.Children.Clear();
        foreach (var profile in ViewModel.Profiles)
        {
            var check = new CheckBox
            {
                Content = profile.IsPrimary ? $"{profile.Name} · Primary" : profile.Name,
                Tag = profile.Id,
                IsChecked = ViewModel.AllowedProfileIds.Contains(profile.Id),
                FontSize = 13,
                IsEnabled = !ViewModel.IsReadOnly
            };
            check.Checked += ProfileCheck_Changed;
            check.Unchecked += ProfileCheck_Changed;
            ProfilesPanel.Children.Add(check);
        }
    }

    private void ProfileCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string profileId } check) return;
        if (check.IsChecked == true)
        {
            if (!ViewModel.AllowedProfileIds.Contains(profileId))
                ViewModel.AllowedProfileIds.Add(profileId);
        }
        else
        {
            ViewModel.AllowedProfileIds.Remove(profileId);
        }
    }

    private void BuildLibrariesPanel()
    {
        var flyout = new MenuFlyout();
        var all = new MenuFlyoutItem { Text = "All Libraries", IsEnabled = ViewModel.SelectedLibraryIds.Count > 0 };
        all.Click += (_, _) =>
        {
            ViewModel.SelectedLibraryIds.Clear();
            BuildLibrariesPanel(); _ = RefreshRuleScopeAsync(); SchedulePreview(); UpdateContinueState();
        };
        flyout.Items.Add(all); flyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var library in ViewModel.Libraries)
        {
            var item = new ToggleMenuFlyoutItem { Text = library.Name, Tag = library.Id, IsChecked = ViewModel.SelectedLibraryIds.Contains(library.Id) };
            item.Click += LibraryToggle_Changed; flyout.Items.Add(item);
        }
        LibraryPickerButton.Flyout = flyout;
        UpdateLibraryPickerCaption();
    }

    private void UpdateLibraryPickerCaption()
    {
        var selected = ViewModel.Libraries.Where(library => ViewModel.SelectedLibraryIds.Contains(library.Id)).Select(library => library.Name).ToArray();
        LibraryPickerLabel.Text = selected.Length == 0 ? "All Libraries" : selected.Length == 1 ? selected[0] : $"{selected.Length} Libraries";
    }

    private void LibraryToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item || item.Tag is not int libraryId) return;
        if (item.IsChecked)
        {
            if (!ViewModel.SelectedLibraryIds.Contains(libraryId)) ViewModel.SelectedLibraryIds.Add(libraryId);
        }
        else ViewModel.SelectedLibraryIds.Remove(libraryId);
        UpdateLibraryPickerCaption();
        if (LibraryPickerButton.Flyout is MenuFlyout flyout && flyout.Items.FirstOrDefault() is MenuFlyoutItem all) all.IsEnabled = ViewModel.SelectedLibraryIds.Count > 0;
        _ = RefreshRuleScopeAsync(); SchedulePreview(); UpdateContinueState();
    }

    private void BuildRulesPanel()
    {
        RulesPanel.Children.Clear();
        ViewModel.RuleDefinition.Match = ViewModel.MatchMode;
        var editor = new SiloPlayer.Controls.QueryFilterEditor { IsEnabled = !ViewModel.IsReadOnly };
        _rulesEditor = editor;
        WizardFiltersSheet.ConfigureFilterHeader(editor.DetachModeSelector(), "Refine your catalog results");
        editor.Load(ViewModel.RuleDefinition, ViewModel.MediaScope, ViewModel.SelectedLibraryIds.FirstOrDefault() is var libraryId && libraryId > 0 ? libraryId : null);
        editor.Changed += () => { ViewModel.MatchMode = ViewModel.RuleDefinition.Match; Bindings.Update(); UpdateContinueState(); if (editor.IsValid) SchedulePreview(); };
        RulesPanel.Children.Add(editor);
        MatchModeCombo.Visibility = Visibility.Collapsed;
        _ = RefreshRuleScopeAsync();
    }

    private void RefreshSortChoices()
    {
        if (_wizardSortChoices.Length == 0) return;
        var suppressed = _suppressSelectionChanges;
        _suppressSelectionChanges = true;
        try
        {
            ViewModel.SortField = CatalogSortChoices.Apply(SortFieldCombo, _wizardSortChoices,
                _shownRatingSources, null, ViewModel.SortField, keepSavedEditorSort: true);
        }
        finally { _suppressSelectionChanges = suppressed; }
    }

    private async Task RefreshRuleScopeAsync()
    {
        RefreshSortChoices();
        if (_rulesEditor is not { } editor) return;
        var scope = ViewModel.MediaScope;
        int? libraryId = ViewModel.SelectedLibraryIds.FirstOrDefault() is var selected && selected > 0 ? selected : null;
        var owner = new CancellationTokenSource(); Interlocked.Exchange(ref _filterOptionsCts, owner)?.Cancel();
        editor.Load(ViewModel.RuleDefinition, scope, libraryId);
        try
        {
            var filters = await App.Services.GetRequiredService<CatalogApi>().GetFiltersAsync(libraryId, owner.Token, source: "query", type: string.IsNullOrWhiteSpace(scope) ? null : scope);
            if (_filterOptionsCts == owner && !owner.IsCancellationRequested && _rulesEditor == editor)
                editor.Load(ViewModel.RuleDefinition, scope, libraryId, filters);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) { if (_filterOptionsCts == owner) App.Services.GetRequiredService<ToastService>().Error($"Could not load filter options: {ex.Message}"); }
        finally { Interlocked.CompareExchange(ref _filterOptionsCts, null, owner); owner.Dispose(); }
    }

    private void UpdatePreviewState()
    {
        if (PreviewCountTextBlock == null || PreviewEmptyPanel == null || PreviewItemsRepeater == null)
            return;

        PreviewCountTextBlock.Text = ViewModel.IsPreviewing && ViewModel.PreviewMediaItems.Count == 0
            ? "Loading items…"
            : $"{ViewModel.PreviewTotal:N0} item{(ViewModel.PreviewTotal == 1 ? "" : "s")}";
        PreviewEmptyPanel.Visibility = !ViewModel.IsPreviewing && ViewModel.PreviewMediaItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviewItemsRepeater.Visibility = ViewModel.PreviewMediaItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NextSummaryText.Text = ViewModel.PreviewTotal > 0
            ? $"{ViewModel.PreviewTotal:N0} item{(ViewModel.PreviewTotal == 1 ? "" : "s")} match these filters"
            : "Choose filters to preview matching titles";
        UpdateContinueState();
    }

    private void MediaScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && MediaScopeCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
        {
            ViewModel.MediaScope = value;
            _suppressSelectionChanges = true; SheetMediaScopeCombo.SelectedIndex = MediaScopeCombo.SelectedIndex; _suppressSelectionChanges = false;
            _ = RefreshRuleScopeAsync();
        }
        SchedulePreview();
    }

    private void SortFieldCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && SortFieldCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.SortField = value;
        SchedulePreview();
    }

    private void SortOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && SortOrderCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.SortOrder = value;
        SchedulePreview();
    }

    private void MatchModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && MatchModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            ViewModel.MatchMode = value;
        SchedulePreview();
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddRule();
        SchedulePreview();
    }

    private void FiltersButton_Click(object sender, RoutedEventArgs e)
    {
        WizardFiltersSheet.PreferredWidth = Math.Min(ActualWidth * .75, ActualWidth >= 640 ? 448 : double.PositiveInfinity);
        WizardFiltersSheet.IsOpen = true;
    }

    private void DoneWizardFilters_Click(object sender, RoutedEventArgs e) => WizardFiltersSheet.IsOpen = false;

    private void ClearWizardFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedLibraryIds.Clear(); ViewModel.RuleDefinition.Groups.Clear();
        ViewModel.RuleDefinition.Match = "all"; ViewModel.MatchMode = "all"; ViewModel.Rules.Clear();
        BuildLibrariesPanel(); BuildRulesPanel(); SchedulePreview();
    }

    private void SheetMediaScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges) MediaScopeCombo.SelectedIndex = SheetMediaScopeCombo.SelectedIndex;
    }

    private void LimitTextBox_TextChanged(object sender, TextChangedEventArgs e) => SchedulePreview();

    private void UpdateContinueState()
    {
        if (ContinueButton == null) return;
        ContinueButton.IsEnabled = _rulesEditor?.IsValid != false && (ViewModel.IsPreviewing || ViewModel.PreviewTotal > 0);
    }

    private async void PreviewScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || _currentStep != 1 || ViewModel.IsPreviewing || !ViewModel.PreviewHasMore) return;
        if (PreviewScroll.ExtentHeight - PreviewScroll.VerticalOffset - PreviewScroll.ViewportHeight < 600)
            await ViewModel.LoadMorePreviewAsync();
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        await RunPreviewAsync(retry: true);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSave) return;
        await ViewModel.SaveAsync();
    }

    private void ContinueToDetails_Click(object sender, RoutedEventArgs e) => ShowStep(2);

    private void BackToFilters_Click(object sender, RoutedEventArgs e) => ShowStep(1);

    private void ShowStep(int step)
    {
        _currentStep = step;
        WizardFiltersSheet.IsOpen = false;
        var filtersVisible = step == 1;
        DetailsStepPanel.Visibility = filtersVisible ? Visibility.Collapsed : Visibility.Visible;
        FiltersStepRulesPanel.Visibility = filtersVisible ? Visibility.Visible : Visibility.Collapsed;
        FiltersStepPreviewPanel.Visibility = filtersVisible ? Visibility.Visible : Visibility.Collapsed;
        FilterActionBar.Visibility = filtersVisible ? Visibility.Visible : Visibility.Collapsed;
        ContinueButton.Visibility = filtersVisible ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        BackToFiltersButton.Visibility = filtersVisible ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = filtersVisible ? Visibility.Collapsed : Visibility.Visible;

        WizardSubtitle.Text = filtersVisible
            ? "Tune the filters until the cards below show the collection you want."
            : string.IsNullOrWhiteSpace(_args?.CollectionId)
                ? "Give your new collection a name, artwork, and sharing rules."
                : "Update naming, artwork, and sharing for this collection.";
        FiltersStepBadge.Background = (Brush)Application.Current.Resources[
            filtersVisible ? "AccentBackgroundBrush" : "SurfaceBrush"];
        DetailsStepBadge.Background = (Brush)Application.Current.Resources[
            filtersVisible ? "SurfaceBrush" : "AccentBackgroundBrush"];
        FiltersStepText.Text = filtersVisible ? "1  Filters" : "✓  Filters";
        FiltersStepText.Foreground = (Brush)Application.Current.Resources[
            filtersVisible ? "AccentBrush" : "SecondaryTextBrush"];
        DetailsStepText.Foreground = (Brush)Application.Current.Resources[
            filtersVisible ? "SecondaryTextBrush" : "AccentBrush"];
        UpdateWorkspaceLayout();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<NavigationService>().GoBack();
    }

    private void OnSaved()
    {
        DispatcherQueue.TryEnqueue(() =>
            App.Services.GetRequiredService<NavigationService>().GoBack());
    }

    private void SharedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ProfileAccessSection != null)
            ProfileAccessSection.Visibility = SharedToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void ChoosePoster_Click(object sender, RoutedEventArgs e)
        => await ChooseArtworkAsync();

    private async Task ChooseArtworkAsync()
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
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 20 * 1024 * 1024)
            {
                SetArtworkStatus("Image must be smaller than 20 MB.");
                return;
            }
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            ViewModel.SetPosterFile(file.Name, bytes, file.ContentType);
            SetArtworkStatus(file.Name);
            await UpdatePosterPreviewAsync();
        }
        catch (Exception ex)
        {
            SetArtworkStatus(ex.Message);
        }
    }

    private async Task UpdatePosterPreviewAsync()
    {
        if (ViewModel.PosterFileBytes is { Length: > 0 } bytes)
        {
            var bitmap = new BitmapImage(); using var stream = new MemoryStream(bytes); await bitmap.SetSourceAsync(stream.AsRandomAccessStream()); WizardPosterImage.Source = bitmap;
        }
        else if (Uri.TryCreate(ViewModel.CurrentPosterUrl, UriKind.Absolute, out var uri)) WizardPosterImage.Source = new BitmapImage(uri);
        else WizardPosterImage.Source = null;
        WizardPosterImage.Visibility = WizardPosterImage.Source == null ? Visibility.Collapsed : Visibility.Visible;
        WizardPosterFrame.Visibility = WizardPosterImage.Visibility;
        WizardPosterDropTarget.Visibility = WizardPosterImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
        var selected = ViewModel.PosterFileBytes is { Length: > 0 };
        WizardRemoveSelectedPoster.Visibility = selected && !ViewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        WizardDeletePoster.Visibility = !selected && WizardPosterImage.Source != null && !ViewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        ClipPosterPreview();
    }

    private void ClipPosterPreview()
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(WizardPosterFrame);
        var shape = visual.Compositor.CreateRoundedRectangleGeometry();
        shape.Size = new((float)WizardPosterFrame.ActualWidth, (float)WizardPosterFrame.ActualHeight);
        shape.CornerRadius = new(12, 12);
        visual.Clip = visual.Compositor.CreateGeometricClip(shape);
    }

    private async void RemoveSelectedPoster_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.IsSaving) return;
        ViewModel.ClearPosterFile();
        SetArtworkStatus("Upload a JPG, PNG, or WebP (max 20 MB).");
        await UpdatePosterPreviewAsync();
    }

    private async void DeletePoster_Click(object sender, RoutedEventArgs e)
    {
        var lifetime = _pageLifetime;
        WizardDeletePoster.IsEnabled = false;
        try { await ViewModel.DeletePosterAsync(); if (IsLoaded && lifetime == _pageLifetime) await UpdatePosterPreviewAsync(); }
        finally { WizardDeletePoster.IsEnabled = true; }
    }

    private void Poster_DragOver(object sender, DragEventArgs e) { e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) && !ViewModel.IsReadOnly ? DataPackageOperation.Copy : DataPackageOperation.None; }
    private async void Poster_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsReadOnly || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try
        {
            var file = (await e.DataView.GetStorageItemsAsync()).OfType<Windows.Storage.StorageFile>().FirstOrDefault();
            if (file == null || !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(file.FileType.ToLowerInvariant())) { SetArtworkStatus("Choose a JPG, PNG, or WebP image."); return; }
            if ((await file.GetBasicPropertiesAsync()).Size > 20 * 1024 * 1024) { SetArtworkStatus("Image must be smaller than 20 MB."); return; }
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file); ViewModel.SetPosterFile(file.Name, buffer.ToArray(), file.ContentType); SetArtworkStatus(file.Name); await UpdatePosterPreviewAsync();
        }
        catch (Exception ex) { SetArtworkStatus(ex.Message); }
    }

    private async void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsReadOnly || string.IsNullOrWhiteSpace(_args?.CollectionId)) return;
        var confirm = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete collection?", Content = $"Delete {ViewModel.Title}? Media files remain in your library.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        WizardDeleteButton.IsEnabled = false;
        try { await App.Services.GetRequiredService<CollectionsApi>().DeleteCollectionAsync(_args.CollectionId); OnSaved(); }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        finally { WizardDeleteButton.IsEnabled = true; }
    }

    private void SetArtworkStatus(string message) => PosterFileStatusText.Text = message;

    private void SchedulePreview()
    {
        if (!_loaded || _suppressSelectionChanges) return;
        _previewDebounceTimer?.Stop();
        _previewDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _previewDebounceTimer.Tick += async (_, _) =>
        {
            _previewDebounceTimer?.Stop();
            _previewDebounceTimer = null;
            await RunPreviewAsync();
        };
        _previewDebounceTimer.Start();
    }

    private async Task RunPreviewAsync(bool retry = false)
    {
        if (_rulesEditor?.IsValid == false) return;
        CancellationTokenSource owner;
        Task previewTask;

        await _previewLifecycleGate.WaitAsync();
        try
        {
            var previousCts = _previewCts;
            var previousTask = _previewTask;
            try { previousCts?.Cancel(); } catch { }
            try { await previousTask.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch { }
            previousCts?.Dispose();

            owner = new CancellationTokenSource();
            previewTask = retry ? ViewModel.RetryPreviewAsync(owner.Token) : ViewModel.PreviewAsync(owner.Token);
            _previewCts = owner;
            _previewTask = previewTask;
        }
        finally
        {
            _previewLifecycleGate.Release();
        }

        try
        {
            await previewTask;
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _previewLifecycleGate.WaitAsync();
            try
            {
                if (ReferenceEquals(_previewCts, owner))
                {
                    _previewCts = null;
                    _previewTask = Task.CompletedTask;
                    owner.Dispose();
                }
            }
            finally
            {
                _previewLifecycleGate.Release();
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ++_pageLifetime;
        Interlocked.Exchange(ref _filterOptionsCts, null)?.Cancel();
        _previewDebounceTimer?.Stop();
        _previewDebounceTimer = null;
        _ = CancelPreviewAsync();
        ViewModel.Saved -= OnSaved;
        base.OnNavigatedFrom(e);
    }

    private async Task CancelPreviewAsync()
    {
        await _previewLifecycleGate.WaitAsync();
        try
        {
            var cts = _previewCts;
            var task = _previewTask;
            _previewCts = null;
            _previewTask = Task.CompletedTask;
            try { cts?.Cancel(); } catch { }
            try { await task.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch { }
            cts?.Dispose();
        }
        finally
        {
            _previewLifecycleGate.Release();
        }
    }

    private void SmartCollectionWizardPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        WizardFiltersSheet.PreferredWidth = Math.Min(e.NewSize.Width * .75, e.NewSize.Width >= 640 ? 448 : double.PositiveInfinity);
        _compactLayout = e.NewSize.Width < 900;
        var width = e.NewSize.Width;
        var horizontalPadding = width < 640 ? 16 : width < 1024 ? 24 : width < 1280 ? 40 : 48;
        WizardPageShell.Padding = new Thickness(horizontalPadding, width < 640 ? 64 : 72, horizontalPadding, 96);
        WizardBackButton.Margin = new Thickness(8, width < 640 ? 16 : 24, 0, 0);
        WizardTitle.FontSize = Math.Clamp(width * .04, 32, 48);
        WizardTitle.LineHeight = WizardTitle.FontSize * 1.25; WizardTitle.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        NextSummaryText.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
        FilterActionBar.Margin = new Thickness(0, 0, horizontalPadding, 16);
        UpdateWorkspaceLayout();
    }

    private void UpdateWorkspaceLayout()
    {
        var filtersVisible = _currentStep == 1;
        WizardWorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        WizardWorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(0);
        Grid.SetColumn(WizardPrimaryPanel, 0);
        Grid.SetRow(WizardPrimaryPanel, 0);
        Grid.SetColumnSpan(WizardPrimaryPanel, 2);
        FiltersStepPreviewPanel.Visibility = Visibility.Collapsed;
        DetailsGrid.ColumnDefinitions[1].Width = _compactLayout ? new GridLength(0) : new GridLength(300);
        Grid.SetColumn(PosterDetailsPanel, _compactLayout ? 0 : 1);
        Grid.SetRow(PosterDetailsPanel, _compactLayout ? 1 : 0);
    }

    private static void SetDescendantControlsEnabled(DependencyObject root, bool enabled)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Control control) control.IsEnabled = enabled;
            SetDescendantControlsEnabled(child, enabled);
        }
    }


}
