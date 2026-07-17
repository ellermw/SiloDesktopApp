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

    public CollectionEditorPage()
    {
        ViewModel = App.Services.GetRequiredService<CollectionEditorViewModel>();
        this.InitializeComponent();

        ViewModel.Saved += OnSaved;
        ViewModel.Deleted += OnSaved;

        ViewModel.Rules.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildRulesUI);

        ViewModel.ManualItems.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildManualItemsUI);

        ViewModel.SearchResults.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildSearchResultsUI);

        ViewModel.PreviewItems.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildPreviewItemsUI);
        ViewModel.SelectedLibraryIds.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(UpdateEditorSummary);
        ViewModel.AllowedProfileIds.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(UpdateEditorSummary);
        ViewModel.PropertyChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(UpdateEditorSummary);
        SizeChanged += CollectionEditorPage_SizeChanged;
    }

    private void CollectionEditorPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var horizontalMargin = e.NewSize.Width < 600 ? 16 : e.NewSize.Width < 900 ? 24 : 48;
        CollectionEditorShell.Margin = new Thickness(horizontalMargin, e.NewSize.Width < 640 ? 16 : 24, horizontalMargin, 48);
        var stacked = e.NewSize.Width < 1100;
        EditorSidebarColumn.Width = stacked ? new GridLength(0) : new GridLength(352);
        EditorBodyGrid.ColumnSpacing = stacked ? 0 : 32;
        Grid.SetColumn(EditorSidebar, stacked ? 0 : 1);
        Grid.SetRow(EditorSidebar, stacked ? 1 : 0);
        EditorSidebar.Margin = stacked ? new Thickness(0, 24, 0, 0) : new Thickness(0);

        var compactBanner = e.NewSize.Width < 760;
        Grid.SetColumn(SourceBannerActions, compactBanner ? 1 : 2);
        Grid.SetRow(SourceBannerActions, compactBanner ? 1 : 0);
        SourceBannerActions.HorizontalAlignment = compactBanner ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SourceBannerActions.Margin = compactBanner ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string collectionId && !string.IsNullOrEmpty(collectionId))
        {
            // Editing existing collection
            await ViewModel.LoadExistingCommand.ExecuteAsync(collectionId);
            PageTitle.Text = ViewModel.IsImportedCollection
                ? ViewModel.Name
                : $"Edit {ViewModel.Name}";
            PageSubtitle.Text = ViewModel.IsImportedCollection
                ? "Edit what's local — name, libraries, sharing. Source-managed details (URL, schedule, item ordering) are locked."
                : ViewModel.CollectionType == "manual"
                    ? "Manual collections are curated by adding titles directly."
                    : "Tune the collection settings and preview its matching titles.";
            SaveButtonText.Text = "Save Collection";
            // Disable type switching when editing
            ManualTypeButton.IsEnabled = false;
            SmartTypeButton.IsEnabled = false;
            UpdateTypeToggleUI();
            UpdateSectionVisibility();
            BuildImportedOptionsUI();
            ApplyReadOnlyState();
            UpdateSourceBanner();
            UpdateEditorSummary();
        }
        else
        {
            await ViewModel.LoadReferenceDataCommand.ExecuteAsync(null);
            BuildImportedOptionsUI();
            UpdateEditorSummary();
        }
    }

    private void OnSaved()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.GoBack();
        });
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
        ManualItemsSection.Visibility = ViewModel.CollectionType == "manual" ? Visibility.Visible : Visibility.Collapsed;
        SmartRulesSection.Visibility = ViewModel.CollectionType == "smart" ? Visibility.Visible : Visibility.Collapsed;
        ImportedSourceSection.Visibility = ViewModel.IsImportedCollection ? Visibility.Visible : Visibility.Collapsed;
        ImportedSourceBanner.Visibility = ViewModel.IsImportedCollection ? Visibility.Visible : Visibility.Collapsed;
        TypeSection.Visibility = ViewModel.IsImportedCollection ? Visibility.Collapsed : Visibility.Visible;
        BasicInfoTitle.Text = ViewModel.IsImportedCollection ? "Display" : "Basics";
        SourceUrlTextBox.IsEnabled = ViewModel.CollectionType == "mdblist";
        SourceUrlSection.Visibility = ViewModel.CollectionType == "mdblist"
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateEditorSummary();
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
        foreach (var profile in ViewModel.AvailableProfiles)
        {
            var check = new CheckBox
            {
                Content = profile.IsPrimary ? $"{profile.Name} · Primary" : profile.Name,
                Tag = profile.Id,
                IsChecked = ViewModel.AllowedProfileIds.Contains(profile.Id)
            };
            check.Checked += ImportedProfile_Checked;
            check.Unchecked += ImportedProfile_Checked;
            ImportedProfilesPanel.Children.Add(check);
        }

        SelectByTag(WatchFilterCombo, ViewModel.WatchFilter);
        SelectByTag(MediaFilterCombo, ViewModel.MediaFilter);
        LastSyncSummaryText.Text = ViewModel.LastSyncSummary ?? "Not synced yet";
        RemovePosterButton.Visibility = string.IsNullOrWhiteSpace(ViewModel.CurrentPosterUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ImportedProfileAccessSection.Visibility = ViewModel.IsShared ? Visibility.Visible : Visibility.Collapsed;
        UpdateSourceBanner();
        UpdateEditorSummary();
    }

    private void UpdateEditorSummary()
    {
        if (SummaryModeText == null) return;
        SummaryModeText.Text = ViewModel.CollectionType switch
        {
            "smart" => "Smart",
            "mdblist" => "MDBList",
            "tmdb" => "TMDB",
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
        var source = ViewModel.CollectionType.ToLowerInvariant();
        var (label, initials, tagline) = source switch
        {
            "tmdb" => ("TMDB", "Tm", "The Movie Database"),
            "trakt" => ("TRAKT", "Tk", "Trakt.tv"),
            _ => ("MDBLIST", "Mb", "mdblist.com"),
        };
        SourceBrandLabel.Text = label;
        SourceBrandInitials.Text = initials;
        SourcePresetLabel.Text = ViewModel.Name;
        SourceBannerDescription.Text = $"Synced from {tagline} — items, posters, and ordering are managed by the source.";
        SourceBannerSyncStatus.Text = ViewModel.LastSyncSummary ?? "Not yet synced";
        OpenSourceButton.Visibility = Uri.TryCreate(ViewModel.SourceUrl, UriKind.Absolute, out _)
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
        SetDescendantControlsEnabled(ManualItemsSection, editable);
        SetDescendantControlsEnabled(SmartRulesSection, editable);
        SaveButton.IsEnabled = editable;
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

    private void SharedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ImportedProfileAccessSection != null)
            ImportedProfileAccessSection.Visibility = SharedToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
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
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 20 * 1024 * 1024)
            {
                PosterFileStatusText.Text = "Image must be smaller than 20 MB.";
                return;
            }
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
            ViewModel.SetPosterFile(file.Name, bytes, file.ContentType);
            PosterSourceUrlTextBox.Text = "";
            PosterFileStatusText.Text = file.Name;
        }
        catch (Exception ex) { PosterFileStatusText.Text = ex.Message; }
    }

    private async void RemovePoster_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove poster?",
            Content = "The collection will return to its generated artwork.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.RemovePosterCommand.ExecuteAsync(null);
        RemovePosterButton.Visibility = Visibility.Collapsed;
        PosterFileStatusText.Text = "Poster removed";
    }

    private async void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete collection",
            Content = $"Delete collection \"{ViewModel.Name}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteCommand.ExecuteAsync(null);
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
        nav.GoBack();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    // ===== Smart Rules =====

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddRuleCommand.Execute(null);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviewCommand.ExecuteAsync(null);
    }

    private void BuildRulesUI()
    {
        RulesPanel.Children.Clear();
        NoRulesText.Visibility = ViewModel.Rules.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        foreach (var rule in ViewModel.Rules)
        {
            RulesPanel.Children.Add(BuildRuleRow(rule));
        }
    }

    private Grid BuildRuleRow(QueryRule rule)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Field selector
        var fieldCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"]
        };

        var fields = new[] { "title", "genre", "year", "studio", "network", "country", "content_rating", "type" };
        foreach (var field in fields)
        {
            var item = new ComboBoxItem { Content = FormatFieldName(field), Tag = field };
            fieldCombo.Items.Add(item);
            if (field == rule.Field)
                fieldCombo.SelectedItem = item;
        }
        if (fieldCombo.SelectedItem == null && fieldCombo.Items.Count > 0)
            fieldCombo.SelectedIndex = 0;

        fieldCombo.SelectionChanged += (s, _) =>
        {
            if (fieldCombo.SelectedItem is ComboBoxItem item && item.Tag is string f)
                rule.Field = f;
        };

        // Operator selector
        var opCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"]
        };

        var ops = new[] { "is", "is_not", "contains", "greater_than", "less_than" };
        foreach (var op in ops)
        {
            var item = new ComboBoxItem { Content = FormatOperatorName(op), Tag = op };
            opCombo.Items.Add(item);
            if (op == rule.Op)
                opCombo.SelectedItem = item;
        }
        if (opCombo.SelectedItem == null && opCombo.Items.Count > 0)
            opCombo.SelectedIndex = 0;

        opCombo.SelectionChanged += (s, _) =>
        {
            if (opCombo.SelectedItem is ComboBoxItem item && item.Tag is string o)
                rule.Op = o;
        };

        // Value input
        var valueBox = new TextBox
        {
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            PlaceholderText = "Value",
            Text = rule.Value?.ToString() ?? "",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        valueBox.TextChanged += (s, _) => rule.Value = valueBox.Text;

        // Remove button
        var removeBtn = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Content = new FontIcon
            {
                Glyph = "\uE74D",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(removeBtn, "Remove rule");
        removeBtn.Click += (_, _) => ViewModel.RemoveRuleCommand.Execute(rule);

        Grid.SetColumn(fieldCombo, 0);
        Grid.SetColumn(opCombo, 1);
        Grid.SetColumn(valueBox, 2);
        Grid.SetColumn(removeBtn, 3);

        row.Children.Add(fieldCombo);
        row.Children.Add(opCombo);
        row.Children.Add(valueBox);
        row.Children.Add(removeBtn);

        return row;
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
            addBtn.Click += (_, _) =>
            {
                ViewModel.AddManualItemCommand.Execute(capturedItem);
                addBtn.Content = "Added";
                addBtn.IsEnabled = false;
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
            CanDrag = !ViewModel.IsReadOnly,
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
        removeBtn.Click += (_, _) => ViewModel.RemoveManualItemCommand.Execute(capturedItem);
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
            AllowDrop = !ViewModel.IsReadOnly,
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
                ViewModel.ManualItems.Move(oldIndex, newIndex);
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

    private static string FormatFieldName(string field) => field switch
    {
        "title" => "Title",
        "genre" => "Genre",
        "year" => "Year",
        "studio" => "Studio",
        "network" => "Network",
        "country" => "Country",
        "content_rating" => "Content Rating",
        "type" => "Type",
        _ => field
    };

    private static string FormatOperatorName(string op) => op switch
    {
        "is" => "Is",
        "is_not" => "Is Not",
        "contains" => "Contains",
        "greater_than" => "Greater Than",
        "less_than" => "Less Than",
        _ => op
    };
}
