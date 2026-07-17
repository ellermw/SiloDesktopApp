using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

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
        SizeChanged += CollectionEditorPage_SizeChanged;
    }

    private void CollectionEditorPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var horizontalMargin = e.NewSize.Width < 600 ? 16 : e.NewSize.Width < 900 ? 24 : 48;
        CollectionEditorShell.Margin = new Thickness(horizontalMargin, 24, horizontalMargin, 48);
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
        }
        else
        {
            await ViewModel.LoadReferenceDataCommand.ExecuteAsync(null);
            BuildImportedOptionsUI();
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
        TypeSection.Visibility = ViewModel.IsImportedCollection ? Visibility.Collapsed : Visibility.Visible;
        BasicInfoTitle.Text = ViewModel.IsImportedCollection ? "Display" : "Basics";
        SourceUrlTextBox.IsEnabled = ViewModel.CollectionType == "mdblist";
        SourceUrlSection.Visibility = ViewModel.CollectionType == "mdblist"
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        // Reorder buttons
        var reorderPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var upBtn = MakeSmallIconButton("\uE74A", "Move up");
        upBtn.IsEnabled = index > 0;
        var capturedItem = item;
        upBtn.Click += (_, _) => ViewModel.MoveItemUpCommand.Execute(capturedItem);

        var downBtn = MakeSmallIconButton("\uE74B", "Move down");
        downBtn.IsEnabled = index < ViewModel.ManualItems.Count - 1;
        downBtn.Click += (_, _) => ViewModel.MoveItemDownCommand.Execute(capturedItem);

        reorderPanel.Children.Add(upBtn);
        reorderPanel.Children.Add(downBtn);

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
        actionPanel.Children.Add(reorderPanel);
        actionPanel.Children.Add(removeBtn);

        Grid.SetColumn(posText, 0);
        Grid.SetColumn(titlePanel, 1);
        Grid.SetColumn(reorderPanel, 2);
        Grid.SetColumn(removeBtn, 3);

        // Actually place in proper columns
        row.Children.Clear();
        row.ColumnDefinitions.Clear();

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(posText, 0);
        Grid.SetColumn(titlePanel, 1);
        Grid.SetColumn(actionPanel, 2);

        row.Children.Add(posText);
        row.Children.Add(titlePanel);
        row.Children.Add(actionPanel);

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
