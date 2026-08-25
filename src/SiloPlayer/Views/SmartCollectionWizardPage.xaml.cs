using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
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

    public bool CanSave => !ViewModel.IsSaving && !ViewModel.IsLoading
        && !ViewModel.IsReadOnly && !string.IsNullOrWhiteSpace(ViewModel.Title);
    public string PreviewCountText
        => ViewModel.PreviewTotal > 0
            ? $"{ViewModel.PreviewTotal:N0} matched"
            : "Preview the first matching items before saving.";

    public SmartCollectionWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<SmartCollectionWizardViewModel>();
        this.InitializeComponent();

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
        _args = e.Parameter as SmartCollectionWizardNavigationArgs;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        await ViewModel.ConfigureAsync(_args);
        PopulateStaticCombos();
        ApplyModeVisibility();
        BuildLibrariesPanel();
        BuildProfilesPanel();
        BuildRulesPanel();
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
        if (!ViewModel.IsAdmin)
        {
            sortOptions.Add(("Progress", "progress"));
            sortOptions.Add(("Date Viewed", "date_viewed"));
            sortOptions.Add(("Plays", "plays"));
        }
        AddComboItems(SortFieldCombo, sortOptions, ViewModel.SortField);

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

    private void ApplyModeVisibility()
    {
        SharedToggle.Visibility = ViewModel.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        IncludeServerToggle.Visibility = ViewModel.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        FeaturedToggle.Visibility = ViewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        DescriptionPanel.Visibility = ViewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        ProfileAccessSection.Visibility = !ViewModel.IsAdmin && ViewModel.IsShared
            ? Visibility.Visible
            : Visibility.Collapsed;
        AdminVisibilityPanel.Visibility = ViewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        AdminBackdropPanel.Visibility = ViewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        VisibilityCombo.SelectedIndex = string.Equals(ViewModel.Visibility, "hidden", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
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
        LibrariesPanel.Children.Clear();

        foreach (var library in ViewModel.Libraries)
        {
            var check = new CheckBox
            {
                Content = library.Name,
                Tag = library.Id,
                IsChecked = ViewModel.SelectedLibraryIds.Contains(library.Id),
                FontSize = 13
            };
            check.Checked += LibraryCheck_Changed;
            check.Unchecked += LibraryCheck_Changed;
            LibrariesPanel.Children.Add(check);
        }
    }

    private void LibraryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check || check.Tag is not int libraryId)
            return;

        if (check.IsChecked == true)
        {
            if (!ViewModel.SelectedLibraryIds.Contains(libraryId))
                ViewModel.SelectedLibraryIds.Add(libraryId);
        }
        else
        {
            ViewModel.SelectedLibraryIds.Remove(libraryId);
        }
        SchedulePreview();
        UpdateContinueState();
    }

    private void BuildRulesPanel()
    {
        RulesPanel.Children.Clear();
        foreach (var rule in ViewModel.Rules)
            RulesPanel.Children.Add(BuildRuleRow(rule));
    }

    private Grid BuildRuleRow(QueryRule rule)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var fieldCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var (label, value) in RuleFields)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            fieldCombo.Items.Add(item);
            if (value == rule.Field)
                fieldCombo.SelectedItem = item;
        }
        if (fieldCombo.SelectedItem == null)
            fieldCombo.SelectedIndex = 0;
        fieldCombo.SelectionChanged += (_, _) =>
        {
            if (fieldCombo.SelectedItem is ComboBoxItem item && item.Tag is string field)
            {
                rule.Field = field;
                rule.Op = GetRuleOperators(field)[0].Value;
                rule.Value = IsBooleanField(field) ? false : "";
                BuildRulesPanel();
                SchedulePreview();
            }
        };

        var opCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var (label, value) in GetRuleOperators(rule.Field))
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            opCombo.Items.Add(item);
            if (value == rule.Op)
                opCombo.SelectedItem = item;
        }
        if (opCombo.SelectedItem == null)
            opCombo.SelectedIndex = 0;
        opCombo.SelectionChanged += (_, _) =>
        {
            if (opCombo.SelectedItem is ComboBoxItem item && item.Tag is string op)
            {
                rule.Op = op;
                SchedulePreview();
            }
        };

        var valueEditor = BuildRuleValueEditor(rule);

        var removeButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(removeButton, "Remove rule");
        removeButton.Click += (_, _) => ViewModel.RemoveRule(rule);

        Grid.SetColumn(fieldCombo, 0);
        Grid.SetColumn(opCombo, 1);
        Grid.SetColumn(valueEditor, 2);
        Grid.SetColumn(removeButton, 3);
        row.Children.Add(fieldCombo);
        row.Children.Add(opCombo);
        row.Children.Add(valueEditor);
        row.Children.Add(removeButton);

        return row;
    }

    private FrameworkElement BuildRuleValueEditor(QueryRule rule)
    {
        if (IsBooleanField(rule.Field))
        {
            var combo = new ComboBox { CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add(new ComboBoxItem { Content = "True", Tag = true });
            combo.Items.Add(new ComboBoxItem { Content = "False", Tag = false });
            var current = rule.Value is bool boolean
                ? boolean
                : bool.TryParse(rule.Value?.ToString(), out var parsed) && parsed;
            combo.SelectedIndex = current ? 0 : 1;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: bool value })
                {
                    rule.Value = value;
                    SchedulePreview();
                }
            };
            return combo;
        }

        var selectValues = GetRuleSelectValues(rule.Field);
        if (selectValues.Count > 0)
        {
            var combo = new ComboBox { CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var value in selectValues)
            {
                var item = new ComboBoxItem { Content = value.Label, Tag = value.Value };
                combo.Items.Add(item);
                if (string.Equals(rule.Value?.ToString(), value.Value, StringComparison.OrdinalIgnoreCase))
                    combo.SelectedItem = item;
            }
            if (combo.SelectedItem == null) combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: string value })
                {
                    rule.Value = value;
                    SchedulePreview();
                }
            };
            return combo;
        }

        var valueBox = new TextBox
        {
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            PlaceholderText = rule.Op == "between" ? "Start, end" : rule.Op == "in_last" ? "Example: 30 days" : "Value",
            Text = FormatRuleValue(rule.Value)
        };
        valueBox.TextChanged += (_, _) =>
        {
            rule.Value = valueBox.Text;
            SchedulePreview();
        };
        valueBox.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
                _ = RunPreviewAsync();
        };
        return valueBox;
    }

    private static string FormatRuleValue(object? value)
        => value is System.Collections.IEnumerable values and not string
            ? string.Join(", ", values.Cast<object?>().Select(item => item?.ToString()))
            : value?.ToString() ?? "";

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
            ViewModel.MediaScope = value;
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
        FiltersAdvancedPanel.Visibility = FiltersAdvancedPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void LimitTextBox_TextChanged(object sender, TextChangedEventArgs e) => SchedulePreview();

    private void UpdateContinueState()
    {
        if (ContinueButton == null) return;
        ContinueButton.IsEnabled = (!ViewModel.IsAdmin || ViewModel.SelectedLibraryIds.Count > 0)
            && (ViewModel.IsPreviewing || ViewModel.PreviewTotal > 0);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        await RunPreviewAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveAsync();
    }

    private void ContinueToDetails_Click(object sender, RoutedEventArgs e) => ShowStep(2);

    private void BackToFilters_Click(object sender, RoutedEventArgs e) => ShowStep(1);

    private void ShowStep(int step)
    {
        _currentStep = step;
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
            ProfileAccessSection.Visibility = !ViewModel.IsAdmin && SharedToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void VisibilityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanges && VisibilityCombo.SelectedItem is ComboBoxItem { Tag: string value })
            ViewModel.Visibility = value;
    }

    private async void ChoosePoster_Click(object sender, RoutedEventArgs e)
        => await ChooseArtworkAsync("poster");

    private async void ChooseBackdrop_Click(object sender, RoutedEventArgs e)
        => await ChooseArtworkAsync("backdrop");

    private async Task ChooseArtworkAsync(string type)
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
                SetArtworkStatus(type, "Image must be smaller than 20 MB.");
                return;
            }
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            if (type == "poster") ViewModel.SetPosterFile(file.Name, bytes, file.ContentType);
            else ViewModel.SetBackdropFile(file.Name, bytes, file.ContentType);
            SetArtworkStatus(type, file.Name);
        }
        catch (Exception ex)
        {
            SetArtworkStatus(type, ex.Message);
        }
    }

    private void SetArtworkStatus(string type, string message)
    {
        if (type == "poster") PosterFileStatusText.Text = message;
        else BackdropFileStatusText.Text = message;
    }

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

    private async Task RunPreviewAsync()
    {
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
            previewTask = ViewModel.PreviewAsync(owner.Token);
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
        _compactLayout = e.NewSize.Width < 900;
        var horizontalPadding = e.NewSize.Width < 600 ? 16 : _compactLayout ? 24 : 48;
        WizardPageShell.Padding = new Thickness(horizontalPadding, _compactLayout ? 20 : 24, horizontalPadding, _compactLayout ? 36 : 48);
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

    private static readonly (string Label, string Value)[] RuleFields =
    [
        ("Genre", "genre"),
        ("Year", "year"),
        ("Studio", "studio"),
        ("Actor", "actor"),
        ("Director", "director"),
        ("Writer", "writer"),
        ("Producer", "producer"),
        ("Network", "network"),
        ("Country", "country"),
        ("Content Rating", "content_rating"),
        ("Type", "type"),
        ("Match Status", "status"),
        ("IMDb Rating", "rating_imdb"),
        ("Added", "added_at"),
        ("Release Date", "release_date"),
        ("Watched", "watched"),
        ("Favorited", "favorited"),
        ("In Watchlist", "in_watchlist"),
        ("In Progress", "in_progress"),
        ("Resolution", "resolution"),
        ("HDR", "hdr"),
        ("Dolby Vision", "dolby_vision"),
        ("Bitrate", "bitrate")
    ];

    private static readonly (string Label, string Value)[] RuleOperators =
    [
        ("is", "is"),
        ("is not", "is_not"),
        ("contains", "contains"),
        (">=", "gte"),
        ("<=", "lte"),
        (">", "gt"),
        ("<", "lt"),
        ("between", "between"),
        ("in the last", "in_last")
    ];

    private static IReadOnlyList<(string Label, string Value)> GetRuleOperators(string field) => field switch
    {
        "type" or "studio" or "network" or "country" or "content_rating" or "actor" or "director" or "writer" or "producer" or "resolution" or "status"
            => [("is", "is"), ("is not", "is_not")],
        "genre" => [("is", "is"), ("is not", "is_not"), ("contains", "contains")],
        "year" => [("equals", "is"), (">=", "gte"), ("<=", "lte"), (">", "gt"), ("<", "lt"), ("between", "between")],
        "rating_imdb" or "bitrate" => [(">=", "gte"), ("<=", "lte"), (">", "gt"), ("<", "lt"), ("between", "between")],
        "added_at" or "release_date" => [("after", "gt"), ("before", "lt"), ("between", "between"), ("in the last", "in_last")],
        "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision" => [("is", "is")],
        _ => RuleOperators,
    };

    private static bool IsBooleanField(string field)
        => field is "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision";

    private static IReadOnlyList<(string Label, string Value)> GetRuleSelectValues(string field) => field switch
    {
        "type" => [("Movie", "movie"), ("Series", "series")],
        "status" => [("Pending", "pending"), ("Matched", "matched"), ("Unmatched", "unmatched")],
        "resolution" => [("480p", "480p"), ("720p", "720p"), ("1080p", "1080p"), ("2160p", "2160p"), ("4320p", "4320p")],
        _ => [],
    };
}
