using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSectionsPage : Page
{
    public AdminSectionsViewModel ViewModel { get; }

    private bool _suppressPickerChange;
    private string _currentScope = "home";
    private bool _rebuildPending;
    private AdminSection? _editingSection;
    private Func<object?>? _sectionEditorGetBody;

    // Section type labels — matches sectionTypes.ts exactly
    private static readonly Dictionary<string, string> SectionTypeLabels = new()
    {
        { "recently_added",    "Recently Added" },
        { "recently_released", "Recently Released" },
        { "genre",             "Genre" },
        { "custom_filter",     "Custom Filter" },
        { "random",            "Random" },
        { "continue_watching", "Continue Watching" },
        { "watchlist",         "Watchlist" },
        { "favorites",         "Favorites" },
        { "recommended_for_you", "Recommended for You" },
        { "collection",        "Collection" },
    };

    // Destructive red for delete button
    private static readonly Color DestructiveColor = Color.FromArgb(255, 220, 90, 90);

    // Yellow-500 for featured star (fill-yellow-500 text-yellow-500)
    private static readonly Color YellowStarColor = Color.FromArgb(255, 234, 179, 8);

    public AdminSectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminSectionsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Sections.CollectionChanged += (_, _) => ScheduleRebuild();
            SetScopeActive("home");
            await ViewModel.LoadCommand.ExecuteAsync(null);
            PopulateLibraryPicker();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildPending = false;
            BuildSectionRows();
        });
    }

    // ===== Scope Tabs =====

    private void SetScopeActive(string scope)
    {
        _currentScope = scope;
        ViewModel.Scope = scope;

        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var surfaceBg = new SolidColorBrush(Colors.Transparent);
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        bool isHome = scope == "home";

        ScopeHomeButton.Background = isHome ? accentBg : surfaceBg;
        ScopeHomeButton.Foreground = isHome ? accentFg : secondaryFg;

        ScopeLibraryButton.Background = isHome ? surfaceBg : accentBg;
        ScopeLibraryButton.Foreground = isHome ? secondaryFg : accentFg;

        LibraryPickerPanel.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;
        var canManage = isHome || ViewModel.Libraries.Count > 0;
        RestoreDefaultsButton.IsEnabled = canManage;
        AddFromGalleryButton.IsEnabled = canManage;
        AddSectionButton.IsEnabled = canManage;

        // Subtitle varies by scope — matches web UI
        SubtitleText.Text = isHome
            ? "Configure the shelves visitors see on the front page."
            : "Configure the shelves for library views.";
    }

    private async void ScopeHomeButton_Click(object sender, RoutedEventArgs e)
    {
        SetScopeActive("home");
        ViewModel.SelectedLibraryId = null;
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void ScopeLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        SetScopeActive("library");
        if (LibraryPicker.SelectedItem is ComboBoxItem item && item.Tag is int libId)
            ViewModel.SelectedLibraryId = libId;
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    // ===== Library Picker =====

    private void PopulateLibraryPicker()
    {
        if (ViewModel.Libraries.Count == 0)
        {
            LibraryPicker.Visibility = Visibility.Collapsed;
            NoLibrariesText.Visibility = Visibility.Visible;
            return;
        }

        LibraryPicker.Visibility = Visibility.Visible;
        NoLibrariesText.Visibility = Visibility.Collapsed;

        _suppressPickerChange = true;
        LibraryPicker.Items.Clear();
        foreach (var lib in ViewModel.Libraries)
            LibraryPicker.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = lib.Id });

        if (LibraryPicker.Items.Count > 0)
        {
            LibraryPicker.SelectedIndex = 0;
            if (LibraryPicker.Items[0] is ComboBoxItem first && first.Tag is int id)
                ViewModel.SelectedLibraryId = id;
        }
        _suppressPickerChange = false;
    }

    private async void LibraryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPickerChange) return;
        if (LibraryPicker.SelectedItem is ComboBoxItem item && item.Tag is int libId)
        {
            ViewModel.SelectedLibraryId = libId;
            if (_currentScope == "library")
                await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    // ===== Table Builder =====

    private void BuildSectionRows()
    {
        SectionsPanel.Children.Clear();

        // Show/hide reorder hint
        ReorderHintText.Visibility = ViewModel.Sections.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (ViewModel.Sections.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateText.Text = $"No sections configured for {_currentScope} scope.";
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var section in ViewModel.Sections)
        {
            if (!isFirst)
            {
                SectionsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            SectionsPanel.Children.Add(BuildSectionRow(section));
        }
    }

    private FrameworkElement BuildSectionRow(AdminSection section)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 8, 20, 8),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        // ---- Drag grip (column 0) ----
        var dragGrip = new FontIcon
        {
            Glyph = "\uE712",
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            CanDrag = true
        };

        var capturedSection = section;
        ToolTipService.SetToolTip(dragGrip, $"Drag {section.Title}");
        dragGrip.DragStarting += (_, args) =>
        {
            args.Data.SetText(capturedSection.Id);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        row.AllowDrop = true;
        row.DragOver += (_, args) =>
        {
            if (args.DataView.Contains(StandardDataFormats.Text))
                args.AcceptedOperation = DataPackageOperation.Move;
        };
        row.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            var sourceId = await args.DataView.GetTextAsync();
            await ViewModel.MoveSectionToAsync(sourceId, capturedSection.Id);
        };

        // ---- Title column (column 1) ----
        var titleBlock = new TextBlock
        {
            Text = section.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // ---- Type column (column 2) — multiple badges ----
        // Badge 1: section type label (secondary/filled style)
        // Badge 2+: media scope (Movies/Series/Episodes) as outline
        // Badge 3+: library filter names as outline
        // Badge 4: collection name as outline (for collection type)
        var typePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var typeLabel = ViewModel.RecipeLabels.TryGetValue(section.SectionType, out var recipeLabel)
            ? recipeLabel
            : SectionTypeLabels.TryGetValue(section.SectionType, out var lbl) ? lbl : section.SectionType;
        typePanel.Children.Add(MakeSecondaryBadge(typeLabel));

        // Extract media_scope from Config
        string? mediaScope = GetConfigString(section, "media_scope");
        if (mediaScope == "movie")
            typePanel.Children.Add(MakeOutlineBadge("Movies"));
        else if (mediaScope == "series")
            typePanel.Children.Add(MakeOutlineBadge("Series"));
        else if (mediaScope == "episode")
            typePanel.Children.Add(MakeOutlineBadge("Episodes"));
        else if (mediaScope == "audiobook")
            typePanel.Children.Add(MakeOutlineBadge("Audiobooks"));
        else if (mediaScope == "ebook")
            typePanel.Children.Add(MakeOutlineBadge("Ebooks"));

        if (section.SectionType == "continue_watching")
        {
            var continueType = GetConfigString(section, "continue_type");
            if (continueType == "listening") typePanel.Children.Add(MakeOutlineBadge("Listening"));
            else if (continueType == "reading") typePanel.Children.Add(MakeOutlineBadge("Reading"));
            else if (continueType == "watching") typePanel.Children.Add(MakeOutlineBadge("Watching"));
        }

        // Extract library_ids from Config (library filter badges)
        var libraryIds = GetConfigLibraryIds(section);
        foreach (var libId in libraryIds)
        {
            var lib = ViewModel.Libraries.FirstOrDefault(l => l.Id == libId);
            if (lib != null)
                typePanel.Children.Add(MakeOutlineBadge(lib.Name));
        }

        // Collection badge
        if (section.SectionType == "collection")
        {
            string? collectionId = GetConfigString(section, "library_collection_id");
            if (!string.IsNullOrEmpty(collectionId))
            {
                // Show actual collection name (from cached labels) instead of generic "Collection".
                var label = ViewModel.CollectionLabels.TryGetValue(collectionId, out var name)
                    ? name : "Collection";
                typePanel.Children.Add(MakeOutlineBadge(label));
            }
        }

        // ---- Items column (column 3) ----
        var itemsBlock = new TextBlock
        {
            Text = section.ItemLimit.ToString(),
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Featured column (column 4) ----
        // Only show filled yellow star when featured=true; nothing when false
        FrameworkElement featuredCell;
        if (section.Featured)
        {
            featuredCell = new FontIcon
            {
                // Segoe MDL2: \uE735 = StarLegacy (filled); \uE734 = StarEmpty
                // For filled star matching fill-yellow-500 text-yellow-500:
                Glyph = "\uE735",
                FontSize = 16,
                Foreground = new SolidColorBrush(YellowStarColor),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            // Empty placeholder so layout is stable
            featuredCell = new TextBlock { Text = "" };
        }

        // ---- Enabled column (column 5) — badge "On"/"Off" ----
        var enabledBadge = section.Enabled
            ? MakeFilledBadge("On")
            : MakeSecondaryBadge("Off");

        // ---- Actions column (column 6) ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 28x28 ghost buttons matching h-7 w-7
        var editBtn = MakeActionButton("\uE70F", "Edit section");
        var deleteBtn = MakeActionButton("\uE74D", "Delete section", DestructiveColor);

        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedSection);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedSection);

        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(dragGrip, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetColumn(typePanel, 2);
        Grid.SetColumn(itemsBlock, 3);
        Grid.SetColumn(featuredCell, 4);
        Grid.SetColumn(enabledBadge, 5);
        Grid.SetColumn(actionsPanel, 6);

        row.Children.Add(dragGrip);
        row.Children.Add(titleBlock);
        row.Children.Add(typePanel);
        row.Children.Add(itemsBlock);
        row.Children.Add(featuredCell);
        row.Children.Add(enabledBadge);
        row.Children.Add(actionsPanel);

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        return row;
    }

    // ===== Config helpers =====

    private static string? GetConfigString(AdminSection section, string key)
    {
        if (section.Config == null) return null;
        if (section.Config.TryGetValue(key, out var val))
            return val?.ToString();
        return null;
    }

    private static List<int> GetConfigLibraryIds(AdminSection section)
    {
        if (section.Config == null) return [];
        if (!section.Config.TryGetValue("library_ids", out var val)) return [];

        // May be deserialized as List<object>, JsonElement, etc.
        var result = new List<int>();
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
            {
                if (el.TryGetInt32(out int id))
                    result.Add(id);
            }
        }
        else if (val is IEnumerable<object> list)
        {
            foreach (var item in list)
                if (item != null && int.TryParse(item.ToString(), out int id))
                    result.Add(id);
        }
        return result;
    }

    // ===== Header Buttons =====

    private async void AddFromGalleryButton_Click(object sender, RoutedEventArgs e)
    {
        var catalog = ViewModel.RecipeCatalog;
        if (catalog == null)
        {
            ViewModel.ErrorMessage = "Section gallery is unavailable.";
            return;
        }

        var choices = catalog.Categories
            .SelectMany(category => category.Value)
            .SelectMany(definition => definition.Presets.Select(preset => new GalleryRecipeChoice(definition, preset)))
            .OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var searchBox = new TextBox
        {
            PlaceholderText = "Search section gallery",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var recipeList = new ListView
        {
            Height = 340,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(GalleryRecipeChoice.DisplayName),
            ItemsSource = choices
        };
        var description = new TextBlock
        {
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 38
        };
        var sectionTitle = new TextBox
        {
            Header = "Section title",
            PlaceholderText = "Section title",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var itemLimit = new NumberBox
        {
            Header = "Items",
            Minimum = 1,
            Maximum = 100,
            Value = 20,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var featured = new CheckBox { Content = "Featured section" };

        var content = new StackPanel { Spacing = 12, Width = 680 };
        content.Children.Add(new TextBlock
        {
            Text = "Start from a Silo section recipe, then customize its title and presentation.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(searchBox);
        content.Children.Add(recipeList);
        content.Children.Add(description);
        content.Children.Add(sectionTitle);
        content.Children.Add(itemLimit);
        content.Children.Add(featured);

        var dialog = new ContentDialog
        {
            Title = "Add from Gallery",
            Content = content,
            PrimaryButtonText = "Add Section",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot
        };

        recipeList.SelectionChanged += (_, _) =>
        {
            if (recipeList.SelectedItem is not GalleryRecipeChoice choice) return;
            dialog.IsPrimaryButtonEnabled = true;
            description.Text = choice.Description;
            sectionTitle.Text = choice.DisplayName;
            if (TryGetDefaultLimit(choice.Preset.DefaultParams, out var limit)) itemLimit.Value = limit;
        };
        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text.Trim();
            recipeList.ItemsSource = string.IsNullOrWhiteSpace(query)
                ? choices
                : choices.Where(choice =>
                    choice.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    choice.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            recipeList.SelectedItem is not GalleryRecipeChoice selected)
            return;

        var config = new Dictionary<string, object?>(selected.Preset.DefaultParams
            .Where(entry => entry.Key != "item_limit")
            .ToDictionary(entry => entry.Key, entry => (object?)entry.Value));
        var body = ViewModel.BuildCreateBody(
            string.IsNullOrWhiteSpace(sectionTitle.Text) ? selected.DisplayName : sectionTitle.Text.Trim(),
            selected.Definition.Type,
            double.IsNaN(itemLimit.Value) ? 20 : (int)itemLimit.Value,
            featured.IsChecked == true,
            true,
            config);
        await ViewModel.CreateSectionCommand.ExecuteAsync(body);
        ShowStatus(ViewModel.ErrorMessage ?? ViewModel.StatusMessage ?? "Section added.");
    }

    private static bool TryGetDefaultLimit(IReadOnlyDictionary<string, object> parameters, out int limit)
    {
        limit = 20;
        if (!parameters.TryGetValue("item_limit", out var value) || value == null) return false;
        if (value is int intValue) { limit = intValue; return true; }
        if (value is long longValue) { limit = (int)longValue; return true; }
        if (value is System.Text.Json.JsonElement element && element.TryGetInt32(out var jsonValue))
        { limit = jsonValue; return true; }
        return int.TryParse(value.ToString(), out limit);
    }

    private sealed record GalleryRecipeChoice(RecipeDefinition Definition, GalleryPreset Preset)
    {
        public string DisplayName => Preset.DisplayName;
        public string Description => Preset.DescriptionLong ?? Preset.DescriptionShort;
    }

    private async void AddSectionButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    private async void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        // Matches web UI: "Restore Default Sections" dialog with "reset profiles" toggle
        bool resetProfiles = false;

        var resetProfilesSwitch = new ToggleSwitch
        {
            IsOn = false,
            OnContent = "",
            OffContent = "",
            MinWidth = 0
        };

        var dialogContent = new StackPanel { Width = 512, Spacing = 16 };

        dialogContent.Children.Add(new TextBlock
        {
            Text = $"This will replace all {(_currentScope == "home" ? "home" : "library")} sections with the defaults. Any custom sections will be removed.",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        var switchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        switchRow.Children.Add(resetProfilesSwitch);
        switchRow.Children.Add(new TextBlock
        {
            Text = "Also reset all user customizations for this scope",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        dialogContent.Children.Add(switchRow);

        var dialog = new ContentDialog
        {
            Title = "Restore Default Sections",
            PrimaryButtonText = "Restore Defaults",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = dialogContent,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            resetProfiles = resetProfilesSwitch.IsOn;
            try
            {
                await ViewModel.RestoreDefaultsCommand.ExecuteAsync(resetProfiles);
                ShowStatus(ViewModel.StatusMessage ?? "Sections restored to defaults.");
            }
            catch { }
        }
    }

    // ===== Create Dialog =====

    private Task OpenCreateDialogAsync()
    {
        var (formContent, getBody) = BuildSectionForm(null);
        OpenSectionEditor(null, formContent, getBody);
        return Task.CompletedTask;
    }

    // ===== Edit Dialog =====

    private Task OpenEditDialogAsync(AdminSection section)
    {
        var (formContent, getBody) = BuildSectionForm(section);
        OpenSectionEditor(section, formContent, getBody);
        return Task.CompletedTask;
    }

    private void OpenSectionEditor(AdminSection? section, FrameworkElement content, Func<object?> getBody)
    {
        _editingSection = section;
        _sectionEditorGetBody = getBody;
        SectionEditorTitle.Text = section == null ? "Add Section" : "Edit Section";
        SectionEditorDescription.Text = section == null ? "Configure a new section." : "Modify this section's settings";
        SaveSectionEditorButton.Content = section == null ? "Add Section" : "Save";
        SectionEditorContent.Content = content;
        SectionEditorError.Text = "";
        SectionEditorError.Visibility = Visibility.Collapsed;
        SectionEditorOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSectionEditor_Click(object sender, RoutedEventArgs e) => CloseSectionEditor();

    private void CloseSectionEditor()
    {
        SectionEditorOverlay.Visibility = Visibility.Collapsed;
        SectionEditorContent.Content = null;
        _editingSection = null;
        _sectionEditorGetBody = null;
    }

    private async void SaveSectionEditor_Click(object sender, RoutedEventArgs e)
    {
        var body = _sectionEditorGetBody?.Invoke();
        if (body == null) return;

        SaveSectionEditorButton.IsEnabled = false;
        SectionEditorError.Visibility = Visibility.Collapsed;
        try
        {
            if (_editingSection == null)
                await ViewModel.CreateSectionCommand.ExecuteAsync(body);
            else
                await ViewModel.UpdateSectionCommand.ExecuteAsync((_editingSection.Id, body));

            if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
                throw new InvalidOperationException(ViewModel.ErrorMessage);

            var message = ViewModel.StatusMessage ?? (_editingSection == null ? "Section created." : "Section updated.");
            CloseSectionEditor();
            ShowStatus(message);
        }
        catch (Exception ex)
        {
            SectionEditorError.Text = ex.Message;
            SectionEditorError.Visibility = Visibility.Visible;
        }
        finally
        {
            SaveSectionEditorButton.IsEnabled = true;
        }
    }

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(AdminSection section)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete section",
            Content = $"Delete section \"{section.Title}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.DeleteSectionCommand.ExecuteAsync(section.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Section deleted.");
            }
            catch { }
        }
    }

    // ===== Form Builder =====

    private (FrameworkElement Content, Func<object?> GetBody) BuildSectionForm(AdminSection? existing)
    {
        var titleBox = new TextBox
        {
            PlaceholderText = "Section title",
            Text = existing?.Title ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };

        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(6),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var addedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ViewModel.RecipeCatalog?.Categories.Count > 0)
        {
            foreach (var category in ViewModel.RecipeCatalog.Categories)
            {
                foreach (var definition in category.Value)
                {
                    if (!addedTypes.Add(definition.Type)) continue;
                    var preset = definition.Presets.FirstOrDefault();
                    var label = !string.IsNullOrWhiteSpace(preset?.DisplayName)
                        ? preset.DisplayName
                        : SectionTypeLabels.GetValueOrDefault(definition.Type, definition.Type.Replace('_', ' '));
                    typeCombo.Items.Add(new ComboBoxItem
                    {
                        Content = string.IsNullOrWhiteSpace(preset?.Icon) ? label : $"{preset.Icon} {label}",
                        Tag = definition.Type
                    });
                }
            }
        }
        foreach (var kvp in SectionTypeLabels)
        {
            if (addedTypes.Add(kvp.Key))
                typeCombo.Items.Add(new ComboBoxItem { Content = kvp.Value, Tag = kvp.Key });
        }
        if (existing != null && addedTypes.Add(existing.SectionType))
            typeCombo.Items.Insert(0, new ComboBoxItem
            {
                Content = ViewModel.RecipeLabels.GetValueOrDefault(existing.SectionType, existing.SectionType.Replace('_', ' ')),
                Tag = existing.SectionType
            });
        typeCombo.SelectedIndex = 0;
        if (existing != null)
        {
            foreach (ComboBoxItem item in typeCombo.Items)
            {
                if (item.Tag as string == existing.SectionType)
                {
                    typeCombo.SelectedItem = item;
                    break;
                }
            }
        }

        var itemLimitBox = new NumberBox
        {
            Value = existing?.ItemLimit ?? 20,
            Minimum = 1,
            Maximum = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };

        var form = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };

        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
            });
            group.Children.Add(control);
            form.Children.Add(group);
        }

        AddField("Section Type", typeCombo);
        AddField("Title", titleBox);
        AddField("Item Limit", itemLimitBox);

        Border MakeSettingCard(UIElement content) => new()
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = content
        };

        var featuredRow = new Grid { ColumnSpacing = 16 };
        featuredRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        featuredRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        var featuredCopy = new StackPanel { Spacing = 4 };
        featuredCopy.Children.Add(new TextBlock
        {
            Text = "Featured", FontSize = 14, FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        featuredCopy.Children.Add(new TextBlock
        {
            Text = "Use this section as the hero banner on the home screen.", FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        var featuredSwitchSmall = new ToggleSwitch
        {
            IsOn = existing?.Featured ?? false, OnContent = "", OffContent = "", MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(featuredCopy, 0);
        Grid.SetColumn(featuredSwitchSmall, 1);
        featuredRow.Children.Add(featuredCopy);
        featuredRow.Children.Add(featuredSwitchSmall);
        form.Children.Add(MakeSettingCard(featuredRow));

        var enabledRow = new Grid { ColumnSpacing = 16 };
        enabledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enabledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        enabledRow.Children.Add(new TextBlock
        {
            Text = "Enabled", FontSize = 14, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        var enabledSwitchSmall = new ToggleSwitch
        {
            IsOn = existing?.Enabled ?? true, OnContent = "", OffContent = "", MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(enabledSwitchSmall, 1);
        enabledRow.Children.Add(enabledSwitchSmall);
        form.Children.Add(MakeSettingCard(enabledRow));
        form.Children.Add(new Border
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0)
        });

        // === Conditional fields based on section type ===
        var filterTypes = new HashSet<string> { "genre", "custom_filter" };

        // Collection picker (shown when type = "collection")
        var collectionPicker = new ComboBox
        {
            CornerRadius = new CornerRadius(6), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Choose collection",
        };
        foreach (var c in ViewModel.Collections)
        {
            var libName = ViewModel.Libraries.FirstOrDefault(l => l.Id == c.LibraryId)?.Name;
            var label = libName != null ? $"{c.Title} ({libName})" : c.Title;
            collectionPicker.Items.Add(new ComboBoxItem { Content = label, Tag = c.Id });
        }
        var collectionField = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        collectionField.Children.Add(new TextBlock
        {
            Text = "Collection", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        collectionField.Children.Add(collectionPicker);
        form.Children.Add(collectionField);

        // Pre-select existing collection
        if (existing != null)
        {
            var existingCollId = AdminSectionsViewModel.GetConfigCollectionId(existing);
            if (existingCollId != null)
            {
                for (int i = 0; i < collectionPicker.Items.Count; i++)
                    if (collectionPicker.Items[i] is ComboBoxItem ci && (string)ci.Tag == existingCollId)
                    { collectionPicker.SelectedIndex = i; break; }
            }
        }

        // Media scope (shown when type is NOT collection and NOT filter)
        var mediaScopeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(6), FontSize = 13, Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        mediaScopeCombo.Items.Add(new ComboBoxItem { Content = "All Media", Tag = "" });
        mediaScopeCombo.Items.Add(new ComboBoxItem { Content = "Movies", Tag = "movie" });
        mediaScopeCombo.Items.Add(new ComboBoxItem { Content = "Series", Tag = "series" });
        mediaScopeCombo.Items.Add(new ComboBoxItem { Content = "Episodes", Tag = "episode" });
        mediaScopeCombo.Items.Add(new ComboBoxItem { Content = "Audiobooks", Tag = "audiobook" });
        mediaScopeCombo.Items.Add(new ComboBoxItem { Content = "Ebooks", Tag = "ebook" });
        mediaScopeCombo.SelectedIndex = 0;
        var mediaScopeField = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        mediaScopeField.Children.Add(new TextBlock
        {
            Text = "Media Scope", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        mediaScopeField.Children.Add(mediaScopeCombo);
        form.Children.Add(mediaScopeField);

        // Pre-select existing media scope
        if (existing != null)
        {
            var existingScope = AdminSectionsViewModel.GetConfigMediaScope(existing) ?? "";
            for (int i = 0; i < mediaScopeCombo.Items.Count; i++)
                if (mediaScopeCombo.Items[i] is ComboBoxItem ci && (string)ci.Tag == existingScope)
                { mediaScopeCombo.SelectedIndex = i; break; }
        }

        // Library multi-select (checkboxes, shown alongside media scope)
        var libraryChecks = new StackPanel { Spacing = 4 };
        var libraryCheckboxes = new List<(int LibId, CheckBox Check)>();
        var existingLibIds = existing != null ? AdminSectionsViewModel.GetConfigLibraryIds(existing) : [];
        foreach (var lib in ViewModel.Libraries)
        {
            var cb = new CheckBox
            {
                Content = lib.Name, FontSize = 12,
                IsChecked = existingLibIds.Contains(lib.Id),
            };
            libraryChecks.Children.Add(cb);
            libraryCheckboxes.Add((lib.Id, cb));
        }
        var libraryField = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        libraryField.Children.Add(new TextBlock
        {
            Text = "Libraries", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        libraryField.Children.Add(libraryChecks);
        form.Children.Add(libraryField);

        // Genre input (shown when type = "genre")
        var genreInput = new TextBox
        {
            PlaceholderText = "Genre name (e.g. Action, Comedy)",
            CornerRadius = new CornerRadius(6), FontSize = 13,
        };
        // Pre-fill from existing config
        if (existing?.Config != null)
        {
            try
            {
                if (existing.Config.TryGetValue("groups", out var groupsObj) &&
                    groupsObj is System.Text.Json.JsonElement groupsEl &&
                    groupsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var first = groupsEl.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        first.TryGetProperty("rules", out var rulesEl) &&
                        rulesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var rule = rulesEl.EnumerateArray().FirstOrDefault();
                        if (rule.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            rule.TryGetProperty("value", out var valEl))
                            genreInput.Text = valEl.GetString() ?? "";
                    }
                }
            }
            catch { }
        }
        var genreField = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        genreField.Children.Add(new TextBlock
        {
            Text = "Genre", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        genreField.Children.Add(genreInput);
        form.Children.Add(genreField);

        var continueTypeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(6), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        continueTypeCombo.Items.Add(new ComboBoxItem { Content = "Watching", Tag = "watching" });
        continueTypeCombo.Items.Add(new ComboBoxItem { Content = "Listening", Tag = "listening" });
        var existingContinueType = existing == null
            ? "watching"
            : AdminSectionsViewModel.GetConfigString(existing, "continue_type") ?? "watching";
        continueTypeCombo.SelectedIndex = existingContinueType == "listening" ? 1 : 0;
        var continueTypeField = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        continueTypeField.Children.Add(new TextBlock
        {
            Text = "Continue type", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        continueTypeField.Children.Add(continueTypeCombo);
        form.Children.Add(continueTypeField);

        // Visibility toggling based on type selection
        void UpdateConditionalFields()
        {
            var selType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            collectionField.Visibility = selType == "collection" ? Visibility.Visible : Visibility.Collapsed;
            bool isFilter = filterTypes.Contains(selType);
            bool isKnownRecipe = ViewModel.RecipeCatalog?.Categories.Values
                .SelectMany(definitions => definitions)
                .Any(definition => definition.Type == selType) == true;
            bool showScopeAndLibrary = selType != "collection" && !isFilter && !isKnownRecipe;
            mediaScopeField.Visibility = showScopeAndLibrary ? Visibility.Visible : Visibility.Collapsed;
            libraryField.Visibility = showScopeAndLibrary ? Visibility.Visible : Visibility.Collapsed;
            genreField.Visibility = selType == "genre" ? Visibility.Visible : Visibility.Collapsed;
            continueTypeField.Visibility = selType == "continue_watching" ? Visibility.Visible : Visibility.Collapsed;
        }
        typeCombo.SelectionChanged += (_, _) => UpdateConditionalFields();
        UpdateConditionalFields();

        object? GetBody()
        {
            string title = titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            string sectionType = "recently_added";
            if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string t)
                sectionType = t;

            int itemLimit = double.IsNaN(itemLimitBox.Value) ? 20 : (int)itemLimitBox.Value;

            // Build config based on section type
            Dictionary<string, object?>? config = existing?.SectionType != sectionType || existing.Config == null
                ? null
                : existing.Config.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);

            if (config == null && ViewModel.RecipeCatalog != null)
            {
                var recipe = ViewModel.RecipeCatalog.Categories.Values
                    .SelectMany(definitions => definitions)
                    .FirstOrDefault(definition => definition.Type == sectionType);
                var defaults = recipe?.Presets.FirstOrDefault()?.DefaultParams;
                if (defaults?.Count > 0)
                    config = defaults.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
            }

            if (sectionType == "collection")
            {
                var selectedCollId = (collectionPicker.SelectedItem as ComboBoxItem)?.Tag as string;
                if (!string.IsNullOrEmpty(selectedCollId))
                    config = new() { ["library_collection_id"] = selectedCollId };
            }
            else if (sectionType == "genre")
            {
                var genreVal = genreInput.Text?.Trim();
                if (!string.IsNullOrEmpty(genreVal))
                {
                    config = new()
                    {
                        ["match"] = "all",
                        ["groups"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["rules"] = new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        ["field"] = "genre",
                                        ["operator"] = "contains",
                                        ["value"] = genreVal,
                                    }
                                }
                            }
                        }
                    };
                }
            }
            else if (!filterTypes.Contains(sectionType))
            {
                // Non-collection, non-filter: include media scope + library IDs
                var scope = (mediaScopeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                var selectedLibIds = libraryCheckboxes
                    .Where(x => x.Check.IsChecked == true)
                    .Select(x => (object)x.LibId)
                    .ToList();

                if (!string.IsNullOrEmpty(scope) || selectedLibIds.Count > 0)
                {
                    config = new();
                    if (!string.IsNullOrEmpty(scope)) config["media_scope"] = scope;
                    if (selectedLibIds.Count > 0) config["library_ids"] = selectedLibIds;
                }
            }

            if (sectionType == "continue_watching")
            {
                config ??= new();
                config["continue_type"] = (continueTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "watching";
            }

            return ViewModel.BuildCreateBody(title, sectionType, itemLimit, featuredSwitchSmall.IsOn, enabledSwitchSmall.IsOn, config);
        }

        return (form, GetBody);
    }

    // ===== Badge Helpers =====

    // Secondary badge: filled with CardBackground bg, secondary text (like Badge variant="secondary")
    private static Border MakeSecondaryBadge(string text)
    {
        var badge = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };
        return badge;
    }

    // Outline badge: border only, no background (like Badge variant="outline")
    private static Border MakeOutlineBadge(string text)
    {
        var badge = new Border
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };
        return badge;
    }

    // Filled badge: accent-like appearance (like Badge variant="default")
    private static Border MakeFilledBadge(string text)
    {
        var badge = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"]
        };
        return badge;
    }

    // ===== Button Helpers =====

    // 28x28 ghost action button (h-7 w-7 p-0 ghost) matching the web
    private static Button MakeActionButton(string glyph, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        var btn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 13,
                Foreground = fg
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    // Small 24x24 icon button for move up/down
    private static Button MakeSmallIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
