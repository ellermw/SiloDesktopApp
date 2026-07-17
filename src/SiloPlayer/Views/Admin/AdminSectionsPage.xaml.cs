using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSectionsPage : Page
{
    public AdminSectionsViewModel ViewModel { get; }

    private bool _suppressPickerChange;
    private string _currentScope = "home";
    private bool _rebuildPending;
    private AdminShellPage? _adminShell;
    private AdminSection? _editingSection;
    private Func<object?>? _sectionEditorGetBody;
    private TaskCompletionSource<GalleryRecipeChoice?>? _galleryCompletion;
    private DispatcherTimer? _sectionPreviewTimer;
    private Func<Task>? _sectionPreviewAction;

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
            _adminShell = FindAncestor<AdminShellPage>(this);
            _adminShell?.AttachPageOverlay(GalleryOverlay);
            _adminShell?.AttachPageOverlay(SectionEditorOverlay);
            ViewModel.Sections.CollectionChanged += (_, _) => ScheduleRebuild();
            SetScopeActive("home");
            BuildLoadingSkeletons();
            await ViewModel.LoadCommand.ExecuteAsync(null);
            PopulateLibraryPicker();
            BuildSectionRows();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CloseGalleryOverlay(null);
        CloseSectionEditor();
        _adminShell?.DetachPageOverlay(GalleryOverlay);
        _adminShell?.DetachPageOverlay(SectionEditorOverlay);
        _adminShell = null;
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
        }
        return null;
    }

    private void ScheduleRebuild()
    {
        if (ViewModel.IsLoading) return;
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
        BuildLoadingSkeletons();
        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildSectionRows();
    }

    private async void ScopeLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        SetScopeActive("library");
        if (LibraryPicker.SelectedItem is ComboBoxItem item && item.Tag is int libId)
            ViewModel.SelectedLibraryId = libId;
        BuildLoadingSkeletons();
        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildSectionRows();
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
            {
                BuildLoadingSkeletons();
                await ViewModel.LoadCommand.ExecuteAsync(null);
                BuildSectionRows();
            }
        }
    }

    private void BuildLoadingSkeletons()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ReorderHintText.Visibility = Visibility.Collapsed;
        SectionsPanel.Children.Clear();
        for (var index = 0; index < 6; index++)
        {
            SectionsPanel.Children.Add(new Border
            {
                Height = 44,
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                Opacity = index % 2 == 0 ? 0.52 : 0.36,
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = index == 0 ? new Thickness(0) : new Thickness(0, 1, 0, 0),
            });
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
            .ToList();

        var categoryLabels = new Dictionary<string, string>
        {
            ["library_staples"] = "Library staples",
            ["personalized"] = "Personalized",
            ["discovery"] = "Discovery",
            ["editorial"] = "Editorial",
            ["seasonal"] = "Seasonal",
            ["mood"] = "Mood",
            ["hand_picked"] = "Hand-picked",
            ["social"] = "Social",
            ["custom"] = "Custom",
        };

        var searchBox = new TextBox
        {
            PlaceholderText = "\U0001F50D Search recipes...",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var categoryBar = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
        };
        var currentCategoryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        categoryBar.Children.Add(currentCategoryRow);
        double currentCategoryRowWidth = 0;
        var categoryScroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = categoryBar,
        };
        var cardsGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var cardsScroll = new ScrollViewer
        {
            Height = 500,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = cardsGrid,
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
        var enabled = new CheckBox { Content = "Enabled", IsChecked = true };
        var applyToLibraries = new CheckBox
        {
            Content = "Apply to selected libraries",
            IsChecked = false,
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Add a section",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var closeGalleryButton = new Button
        {
            Content = "\u2715",
            FontSize = 15,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
        };
        Grid.SetColumn(closeGalleryButton, 1);
        header.Children.Add(closeGalleryButton);

        var content = new StackPanel { Spacing = 14, Width = 752 };
        content.Children.Add(header);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["BorderBrush"],
        });
        content.Children.Add(searchBox);
        content.Children.Add(categoryScroll);
        content.Children.Add(cardsScroll);

        GalleryRecipeChoice? selected = null;
        closeGalleryButton.Click += (_, _) => CloseGalleryOverlay(null);

        var activeCategory = "";
        var categoryButtons = new List<(Button Button, string Category)>();
        void UpdateCategoryButtons()
        {
            foreach (var (button, category) in categoryButtons)
            {
                var isActive = category == activeCategory;
                button.Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(255, 99, 102, 241))
                    : (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
                button.Foreground = isActive
                    ? new SolidColorBrush(Colors.White)
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"];
            }
        }

        void ApplyGalleryFilters()
        {
            var query = searchBox.Text.Trim();
            var filtered = choices
                .Where(choice => string.IsNullOrWhiteSpace(activeCategory) || choice.Definition.Category == activeCategory)
                .Select(choice => (Choice: choice, Score: ScoreGalleryChoice(query, choice)))
                .Where(entry => string.IsNullOrWhiteSpace(query) || entry.Score > 0)
                .OrderByDescending(entry => string.IsNullOrWhiteSpace(query) ? 0 : entry.Score)
                .Select(entry => entry.Choice)
                .ToList();

            cardsGrid.Children.Clear();
            cardsGrid.RowDefinitions.Clear();
            if (filtered.Count == 0)
            {
                cardsGrid.RowDefinitions.Add(new RowDefinition());
                var empty = new StackPanel
                {
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 56, 0, 56),
                };
                empty.Children.Add(new TextBlock
                {
                    Text = "\uE721",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                });
                empty.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(query)
                        ? "No recipes match this filter."
                        : $"No recipes match \"{query}\".",
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                var clear = new Button
                {
                    Content = "Clear filters",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                };
                clear.Click += (_, _) =>
                {
                    searchBox.Text = "";
                    activeCategory = "";
                    UpdateCategoryButtons();
                    ApplyGalleryFilters();
                };
                empty.Children.Add(clear);
                Grid.SetColumnSpan(empty, 3);
                cardsGrid.Children.Add(empty);
                return;
            }

            for (var index = 0; index < filtered.Count; index++)
            {
                var choice = filtered[index];
                var row = index / 3;
                if (cardsGrid.RowDefinitions.Count <= row)
                    cardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cardContent = new StackPanel { Spacing = 5 };
                cardContent.Children.Add(new TextBlock
                {
                    Text = choice.Preset.Icon,
                    FontSize = 20,
                });
                cardContent.Children.Add(new TextBlock
                {
                    Text = choice.DisplayName,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                });
                cardContent.Children.Add(new TextBlock
                {
                    Text = choice.Preset.DescriptionShort,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 3,
                });
                cardContent.Children.Add(new TextBlock
                {
                    Text = categoryLabels.GetValueOrDefault(
                        choice.Definition.Category,
                        choice.Definition.Category.Replace('_', ' ')).ToUpperInvariant(),
                    FontSize = 10,
                    CharacterSpacing = 75,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    Margin = new Thickness(0, 4, 0, 0),
                });
                var card = new Button
                {
                    Content = cardContent,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 132,
                    Padding = new Thickness(14),
                    Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Tag = choice,
                };
                card.Click += (_, _) =>
                {
                    CloseGalleryOverlay(choice);
                };
                Grid.SetRow(card, row);
                Grid.SetColumn(card, index % 3);
                cardsGrid.Children.Add(card);
            }
        }

        void AddCategoryButton(string label, string category)
        {
            var button = new Button
            {
                Content = label,
                FontSize = 12,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(0),
            };
            button.Click += (_, _) =>
            {
                activeCategory = category;
                UpdateCategoryButtons();
                ApplyGalleryFilters();
            };
            categoryButtons.Add((button, category));
            var estimatedWidth = label.Length * 7 + 32;
            if (currentCategoryRowWidth > 0 && currentCategoryRowWidth + estimatedWidth > 752)
            {
                currentCategoryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                categoryBar.Children.Add(currentCategoryRow);
                currentCategoryRowWidth = 0;
            }
            currentCategoryRow.Children.Add(button);
            currentCategoryRowWidth += estimatedWidth;
        }

        AddCategoryButton("All", "");
        foreach (var category in catalog.Categories.Keys)
            AddCategoryButton(
                categoryLabels.GetValueOrDefault(category, category.Replace('_', ' ')),
                category);
        UpdateCategoryButtons();
        ApplyGalleryFilters();
        searchBox.TextChanged += (_, _) => ApplyGalleryFilters();

        var galleryCompletion = new TaskCompletionSource<GalleryRecipeChoice?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _galleryCompletion = galleryCompletion;
        GalleryOverlayContent.Content = content;
        GalleryOverlayPanel.MaxHeight = Math.Max(560, ActualHeight * 0.8);
        cardsScroll.Height = Math.Max(400, GalleryOverlayPanel.MaxHeight - 170);
        if (_adminShell is not null)
            _adminShell.SetPageOverlayVisible(GalleryOverlay, true);
        else
            GalleryOverlay.Visibility = Visibility.Visible;
        searchBox.Focus(FocusState.Programmatic);
        try
        {
            selected = await galleryCompletion.Task;
        }
        finally
        {
            if (_adminShell is not null)
                _adminShell.SetPageOverlayVisible(GalleryOverlay, false);
            else
                GalleryOverlay.Visibility = Visibility.Collapsed;
            GalleryOverlayContent.Content = null;
            if (ReferenceEquals(_galleryCompletion, galleryCompletion)) _galleryCompletion = null;
        }
        if (selected == null) return;

        var config = new Dictionary<string, object?>(selected.Preset.DefaultParams
            .Where(entry => entry.Key != "item_limit")
            .ToDictionary(entry => entry.Key, entry => (object?)entry.Value));
        description.Text = selected.Description;
        sectionTitle.Text = selected.DisplayName;
        if (TryGetDefaultLimit(selected.Preset.DefaultParams, out var defaultLimit)) itemLimit.Value = defaultLimit;
        var configContent = new StackPanel { Spacing = 14, Width = 500 };
        configContent.Children.Add(new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(2, 0, 0, 0),
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            Padding = new Thickness(12, 10, 12, 10),
            Child = description,
        });
        configContent.Children.Add(sectionTitle);
        ComboBox? collectionPicker = null;
        var isAutoManagedTraktRecipe = selected.Definition.Type == "collection" &&
            GetGalleryConfigString(config, "source_provider") == "trakt" &&
            (GetGalleryConfigString(config, "source_preset") is "trending" or "popular") &&
            (GetGalleryConfigString(config, "media_type") is "movie" or "tv");
        if (selected.Definition.Type == "collection")
        {
            if (isAutoManagedTraktRecipe)
            {
                configContent.Children.Add(new TextBlock
                {
                    Text = $"A synced Trakt {GetGalleryConfigString(config, "source_preset")} " +
                        $"{(GetGalleryConfigString(config, "media_type") == "tv" ? "shows" : "movies")} collection will be created automatically.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
            }
            else
            {
                collectionPicker = new ComboBox
                {
                    Header = "Collection",
                    PlaceholderText = "Choose a synced collection",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                foreach (var collection in ViewModel.Collections)
                    collectionPicker.Items.Add(new ComboBoxItem
                    {
                        Content = ViewModel.CollectionLabels.GetValueOrDefault(collection.Id, collection.Title),
                        Tag = new GalleryCollectionChoice(collection.Id, IsUserCollection: false),
                    });
                foreach (var collection in ViewModel.UserCollections)
                    collectionPicker.Items.Add(new ComboBoxItem
                    {
                        Content = ViewModel.CollectionLabels.GetValueOrDefault(collection.Id, collection.Name),
                        Tag = new GalleryCollectionChoice(collection.Id, IsUserCollection: true),
                    });
                configContent.Children.Add(collectionPicker);
            }
        }
        ComboBox? continueTypePicker = null;
        if (selected.Definition.Type == "continue_watching")
        {
            continueTypePicker = new ComboBox
            {
                Header = "Continue type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            continueTypePicker.Items.Add(new ComboBoxItem { Content = "Watching", Tag = "watching" });
            continueTypePicker.Items.Add(new ComboBoxItem { Content = "Listening", Tag = "listening" });
            continueTypePicker.SelectedIndex = GetGalleryConfigString(config, "continue_type") == "listening" ? 1 : 0;
            configContent.Children.Add(continueTypePicker);
        }

        ComboBox? personalMediaPicker = null;
        ComboBox? personalSortPicker = null;
        var personalLibraryChecks = new List<(CheckBox Check, int LibraryId)>();
        if (selected.Definition.Type is "watchlist" or "favorites")
        {
            personalMediaPicker = new ComboBox
            {
                Header = "Media type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var option in new[]
            {
                ("All Media", ""), ("Movies", "movie"), ("TV Shows", "series"), ("Audiobooks", "audiobook")
            })
                personalMediaPicker.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            var currentMediaType = GetGalleryConfigString(config, "filter_type") ?? "";
            personalMediaPicker.SelectedIndex = Math.Max(0, new[] { "", "movie", "series", "audiobook" }.ToList().IndexOf(currentMediaType));
            configContent.Children.Add(personalMediaPicker);

            configContent.Children.Add(new TextBlock
            {
                Text = "Libraries",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            var selectedLibraryIds = GetGalleryConfigInts(config, "filter_library_ids").ToHashSet();
            var libraryWrap = new StackPanel { Spacing = 4 };
            foreach (var library in ViewModel.Libraries)
            {
                var check = new CheckBox
                {
                    Content = library.Name,
                    IsChecked = selectedLibraryIds.Contains(library.Id),
                };
                personalLibraryChecks.Add((check, library.Id));
                libraryWrap.Children.Add(check);
            }
            configContent.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Child = libraryWrap,
            });

            personalSortPicker = new ComboBox
            {
                Header = "Sort",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var sortOptions = new[]
            {
                ("List order (default)", ""),
                ("Date added (newest first)", "added_at:desc"),
                ("Date added (oldest first)", "added_at:asc"),
                ("Title (A-Z)", "title:asc"),
                ("Title (Z-A)", "title:desc"),
                ("Release date (newest first)", "release_date:desc"),
                ("Release date (oldest first)", "release_date:asc"),
                ("IMDb rating (highest first)", "rating_imdb:desc"),
            };
            foreach (var option in sortOptions)
                personalSortPicker.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            var currentSort = GetGalleryConfigString(config, "sort") ?? "";
            var currentOrder = GetGalleryConfigString(config, "order") ?? (currentSort == "title" ? "asc" : "desc");
            var currentSortValue = string.IsNullOrWhiteSpace(currentSort) ? "" : $"{currentSort}:{currentOrder}";
            personalSortPicker.SelectedIndex = Math.Max(0, Array.FindIndex(sortOptions, option => option.Item2 == currentSortValue));
            configContent.Children.Add(personalSortPicker);
        }

        var seasonalThemeInputs = new List<(string Key, CheckBox Check, TextBox Title)>();
        if (selected.Definition.Type == "seasonal_themed")
        {
            var enabledThemes = GetGalleryConfigStrings(config, "enabled_themes").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var legacyTheme = GetGalleryConfigString(config, "theme");
            if (enabledThemes.Count == 0 && !string.IsNullOrWhiteSpace(legacyTheme)) enabledThemes.Add(legacyTheme);
            var themeTitles = GetGalleryConfigStringDictionary(config, "theme_titles");
            configContent.Children.Add(new TextBlock
            {
                Text = "Holidays to celebrate",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            var themePanel = new StackPanel { Spacing = 8 };
            var themes = new[]
            {
                ("valentines", "Valentine's Day", "Feb 7-14", "\U0001F49D"),
                ("st_patricks", "St. Patrick's Day", "Mar 15-17", "\U0001F340"),
                ("thanksgiving", "Thanksgiving", "Nov 22-30", "\U0001F983"),
                ("christmas", "Christmas", "Dec 1-31", "\U0001F384"),
                ("halloween", "Halloween", "All October", "\U0001F383"),
                ("saturday_morning", "Saturday Morning Cartoons", "Saturday before 1pm", "\U0001F4FA"),
                ("family_movie_night", "Family Movie Night", "Fri & Sat from 5pm", "\U0001F37F"),
                ("summer_blockbuster", "Summer Blockbusters", "June - August", "\U0001F334"),
            };
            foreach (var theme in themes)
            {
                var check = new CheckBox
                {
                    Content = $"{theme.Item4}  {theme.Item2}   {theme.Item3}",
                    IsChecked = enabledThemes.Contains(theme.Item1),
                };
                var titleInput = new TextBox
                {
                    PlaceholderText = $"Section title in season - defaults to \"{theme.Item2}\"",
                    Text = themeTitles.GetValueOrDefault(theme.Item1, ""),
                    Margin = new Thickness(28, 0, 0, 0),
                    Visibility = check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
                };
                check.Checked += (_, _) => titleInput.Visibility = Visibility.Visible;
                check.Unchecked += (_, _) => titleInput.Visibility = Visibility.Collapsed;
                seasonalThemeInputs.Add((theme.Item1, check, titleInput));
                themePanel.Children.Add(check);
                themePanel.Children.Add(titleInput);
            }
            configContent.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Child = themePanel,
            });
        }

        ComboBox? editorialSubjectType = null;
        CheckBox? editorialAutoRotate = null;
        ComboBox? editorialCadence = null;
        TextBox? editorialSubject = null;
        if (selected.Definition.Type == "editorial_spotlight")
        {
            editorialSubjectType = new ComboBox { Header = "Subject type", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in new[] { ("Director", "director"), ("Studio", "studio"), ("Actor", "actor"), ("Era", "era") })
                editorialSubjectType.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            editorialSubjectType.SelectedIndex = Math.Max(0, new[] { "director", "studio", "actor", "era" }.ToList()
                .IndexOf(GetGalleryConfigString(config, "subject_type") ?? "director"));
            editorialAutoRotate = new CheckBox
            {
                Content = "Auto-rotate",
                IsChecked = GetGalleryConfigBool(config, "auto_rotate") ?? string.IsNullOrWhiteSpace(GetGalleryConfigString(config, "subject")),
            };
            editorialCadence = new ComboBox { Header = "Rotation cadence", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in new[] { ("Daily", "daily"), ("Weekly (default)", "weekly"), ("Monthly", "monthly") })
                editorialCadence.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            editorialCadence.SelectedIndex = Math.Max(0, new[] { "daily", "weekly", "monthly" }.ToList()
                .IndexOf(GetGalleryConfigString(config, "rotation_cadence") ?? "weekly"));
            editorialSubject = new TextBox
            {
                Header = "Subject",
                PlaceholderText = "e.g. Christopher Nolan",
                Text = GetGalleryConfigString(config, "subject") ?? "",
            };
            void UpdateEditorialVisibility()
            {
                var automatic = editorialAutoRotate.IsChecked == true;
                editorialCadence.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
                editorialSubject.Visibility = automatic ? Visibility.Collapsed : Visibility.Visible;
            }
            editorialAutoRotate.Checked += (_, _) => UpdateEditorialVisibility();
            editorialAutoRotate.Unchecked += (_, _) => UpdateEditorialVisibility();
            configContent.Children.Add(editorialSubjectType);
            configContent.Children.Add(editorialAutoRotate);
            configContent.Children.Add(editorialCadence);
            configContent.Children.Add(editorialSubject);
            UpdateEditorialVisibility();
        }

        TextBox? anchorItemInput = null;
        if (selected.Definition.Type == "because_you_watched")
        {
            anchorItemInput = new TextBox
            {
                Header = "Anchor item",
                PlaceholderText = "Auto-pick latest watched (leave blank)",
                Text = GetGalleryConfigString(config, "anchor_item_id") ?? "",
            };
            configContent.Children.Add(anchorItemInput);
        }

        TextBox? tasteGenreInput = null;
        if (selected.Definition.Type == "taste_match")
        {
            tasteGenreInput = new TextBox
            {
                Header = "Genre (optional)",
                PlaceholderText = "Auto-pick your strongest genre (leave blank)",
                Text = GetGalleryConfigString(config, "genre") ?? "",
            };
            configContent.Children.Add(tasteGenreInput);
        }

        var curatedItems = new List<GalleryCuratedItem>();
        ContentDialog? configDialog = null;
        if (selected.Definition.Type == "admin_curated_list")
        {
            curatedItems.AddRange(GetGalleryConfigStrings(config, "item_ids")
                .Select(id => new GalleryCuratedItem(id, id)));
            var catalogSearch = new TextBox { PlaceholderText = "Search your catalog..." };
            var searchButton = new Button { Content = "Search" };
            var searchRow = new Grid { ColumnSpacing = 8 };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition());
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.Children.Add(catalogSearch);
            Grid.SetColumn(searchButton, 1);
            searchRow.Children.Add(searchButton);
            var searchResults = new StackPanel { Spacing = 4 };
            var pickedItems = new StackPanel { Spacing = 4 };

            void RebuildCuratedPickedItems()
            {
                pickedItems.Children.Clear();
                if (curatedItems.Count == 0)
                {
                    pickedItems.Children.Add(new TextBlock
                    {
                        Text = "Search above and add at least one title.",
                        FontSize = 12,
                        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        Margin = new Thickness(4, 8, 4, 8),
                    });
                    if (configDialog != null) configDialog.IsPrimaryButtonEnabled = false;
                    return;
                }
                for (var index = 0; index < curatedItems.Count; index++)
                {
                    var item = curatedItems[index];
                    var itemIndex = index;
                    var row = new Grid { ColumnSpacing = 6 };
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock
                    {
                        Text = item.Label,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    var up = new Button { Content = "\u2191", Padding = new Thickness(8, 2, 8, 2), IsEnabled = itemIndex > 0 };
                    var down = new Button { Content = "\u2193", Padding = new Thickness(8, 2, 8, 2), IsEnabled = itemIndex < curatedItems.Count - 1 };
                    var remove = new Button { Content = "\u2715", Padding = new Thickness(8, 2, 8, 2) };
                    up.Click += (_, _) =>
                    {
                        var moved = curatedItems[itemIndex];
                        curatedItems.RemoveAt(itemIndex);
                        curatedItems.Insert(itemIndex - 1, moved);
                        RebuildCuratedPickedItems();
                    };
                    down.Click += (_, _) =>
                    {
                        var moved = curatedItems[itemIndex];
                        curatedItems.RemoveAt(itemIndex);
                        curatedItems.Insert(itemIndex + 1, moved);
                        RebuildCuratedPickedItems();
                    };
                    remove.Click += (_, _) =>
                    {
                        curatedItems.RemoveAt(itemIndex);
                        RebuildCuratedPickedItems();
                    };
                    Grid.SetColumn(up, 1);
                    Grid.SetColumn(down, 2);
                    Grid.SetColumn(remove, 3);
                    row.Children.Add(up);
                    row.Children.Add(down);
                    row.Children.Add(remove);
                    pickedItems.Children.Add(row);
                }
                if (configDialog != null) configDialog.IsPrimaryButtonEnabled = true;
            }

            async Task SearchCuratedCatalogAsync()
            {
                var query = catalogSearch.Text.Trim();
                if (string.IsNullOrWhiteSpace(query)) return;
                searchButton.IsEnabled = false;
                searchResults.Children.Clear();
                searchResults.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
                try
                {
                    var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
                    var result = await catalogApi.SearchAsync(query, 10);
                    searchResults.Children.Clear();
                    if (result.Items.Count == 0)
                    {
                        searchResults.Children.Add(new TextBlock
                        {
                            Text = "No matches.",
                            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        });
                    }
                    foreach (var item in result.Items)
                    {
                        var label = item.Year > 0 ? $"{item.Title} ({item.Year})" : item.Title;
                        var resultRow = new Grid { ColumnSpacing = 8 };
                        resultRow.ColumnDefinitions.Add(new ColumnDefinition());
                        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        resultRow.Children.Add(new TextBlock
                        {
                            Text = $"{label}  \u00B7  {item.Type}",
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                        var add = new Button
                        {
                            Content = curatedItems.Any(entry => entry.Id == item.ContentId) ? "Added" : "Add",
                            IsEnabled = curatedItems.All(entry => entry.Id != item.ContentId),
                            Padding = new Thickness(10, 3, 10, 3),
                        };
                        add.Click += (_, _) =>
                        {
                            if (curatedItems.Any(entry => entry.Id == item.ContentId)) return;
                            curatedItems.Add(new GalleryCuratedItem(item.ContentId, label));
                            add.Content = "Added";
                            add.IsEnabled = false;
                            RebuildCuratedPickedItems();
                        };
                        Grid.SetColumn(add, 1);
                        resultRow.Children.Add(add);
                        searchResults.Children.Add(resultRow);
                    }
                }
                catch
                {
                    searchResults.Children.Clear();
                    searchResults.Children.Add(new TextBlock { Text = "Search failed - try again." });
                }
                finally
                {
                    searchButton.IsEnabled = true;
                }
            }

            searchButton.Click += async (_, _) => await SearchCuratedCatalogAsync();
            catalogSearch.KeyDown += async (_, eventArgs) =>
            {
                if (eventArgs.Key == Windows.System.VirtualKey.Enter)
                    await SearchCuratedCatalogAsync();
            };
            configContent.Children.Add(new TextBlock
            {
                Text = "Add titles",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            configContent.Children.Add(searchRow);
            configContent.Children.Add(searchResults);
            configContent.Children.Add(new TextBlock
            {
                Text = "Curated list (shown in this order)",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            configContent.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Child = pickedItems,
            });
            RebuildCuratedPickedItems();
            _ = HydrateCuratedItemLabelsAsync(curatedItems, RebuildCuratedPickedItems);
        }
        NumberBox? recipeNumber = null;
        string? recipeNumberKey = selected.Definition.Type switch
        {
            "returning_shows" => "lookback_days",
            "short_watches" => "max_minutes",
            "anniversaries" => "milestone_years",
            _ => null,
        };
        if (recipeNumberKey != null)
        {
            recipeNumber = new NumberBox
            {
                Header = recipeNumberKey switch
                {
                    "lookback_days" => "Lookback window (days)",
                    "max_minutes" => "Maximum runtime (minutes)",
                    _ => "Milestone (years)",
                },
                Minimum = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = TryGetGalleryConfigInt(config, recipeNumberKey) ?? double.NaN,
            };
            configContent.Children.Add(recipeNumber);
        }
        configContent.Children.Add(itemLimit);
        configContent.Children.Add(featured);
        configContent.Children.Add(applyToLibraries);
        configContent.Children.Add(enabled);
        configDialog = new ContentDialog
        {
            Title = $"{selected.Preset.Icon} {selected.DisplayName}".Trim(),
            Content = new ScrollViewer
            {
                MaxHeight = 680,
                VerticalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = configContent,
            },
            PrimaryButtonText = "Add section",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            IsPrimaryButtonEnabled = collectionPicker == null &&
                (selected.Definition.Type != "admin_curated_list" || curatedItems.Count > 0),
        };
        if (collectionPicker != null)
            collectionPicker.SelectionChanged += (_, _) =>
                configDialog.IsPrimaryButtonEnabled = collectionPicker.SelectedItem != null;
        if (await configDialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (collectionPicker?.SelectedItem is ComboBoxItem selectedCollection &&
            selectedCollection.Tag is GalleryCollectionChoice collectionChoice)
        {
            config.Remove(collectionChoice.IsUserCollection ? "library_collection_id" : "user_collection_id");
            config[collectionChoice.IsUserCollection ? "user_collection_id" : "library_collection_id"] =
                collectionChoice.Id;
        }
        if (continueTypePicker?.SelectedItem is ComboBoxItem selectedContinueType)
            config["continue_type"] = selectedContinueType.Tag as string ?? "watching";
        if (recipeNumberKey != null && recipeNumber != null)
        {
            if (double.IsNaN(recipeNumber.Value)) config.Remove(recipeNumberKey);
            else config[recipeNumberKey] = Math.Max(1, (int)recipeNumber.Value);
        }
        if (personalMediaPicker?.SelectedItem is ComboBoxItem selectedMediaType)
        {
            var mediaType = selectedMediaType.Tag as string ?? "";
            if (string.IsNullOrWhiteSpace(mediaType)) config.Remove("filter_type");
            else config["filter_type"] = mediaType;

            var selectedLibraries = personalLibraryChecks
                .Where(entry => entry.Check.IsChecked == true)
                .Select(entry => (object)entry.LibraryId)
                .ToList();
            if (selectedLibraries.Count == 0) config.Remove("filter_library_ids");
            else config["filter_library_ids"] = selectedLibraries;
        }
        if (personalSortPicker?.SelectedItem is ComboBoxItem selectedSort)
        {
            var value = selectedSort.Tag as string ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                config.Remove("sort");
                config.Remove("order");
            }
            else
            {
                var parts = value.Split(':', 2);
                config["sort"] = parts[0];
                config["order"] = parts.Length > 1 ? parts[1] : "desc";
            }
        }
        if (seasonalThemeInputs.Count > 0)
        {
            var enabledThemeKeys = seasonalThemeInputs
                .Where(entry => entry.Check.IsChecked == true)
                .Select(entry => (object)entry.Key)
                .ToList();
            var customThemeTitles = seasonalThemeInputs
                .Where(entry => entry.Check.IsChecked == true && !string.IsNullOrWhiteSpace(entry.Title.Text))
                .ToDictionary(entry => entry.Key, entry => (object)entry.Title.Text.Trim());
            config["enabled_themes"] = enabledThemeKeys;
            if (customThemeTitles.Count == 0) config.Remove("theme_titles");
            else config["theme_titles"] = customThemeTitles;
            config["theme"] = "";
            config["mode"] = "";
        }
        if (editorialSubjectType?.SelectedItem is ComboBoxItem selectedSubjectType &&
            editorialAutoRotate != null && editorialCadence != null && editorialSubject != null)
        {
            config["subject_type"] = selectedSubjectType.Tag as string ?? "director";
            var automatic = editorialAutoRotate.IsChecked == true;
            config["auto_rotate"] = automatic;
            if (automatic)
            {
                config["rotation_cadence"] = (editorialCadence.SelectedItem as ComboBoxItem)?.Tag as string ?? "weekly";
                config.Remove("subject");
            }
            else
            {
                config.Remove("rotation_cadence");
                config["subject"] = editorialSubject.Text.Trim();
            }
        }
        if (anchorItemInput != null)
        {
            if (string.IsNullOrWhiteSpace(anchorItemInput.Text)) config.Remove("anchor_item_id");
            else config["anchor_item_id"] = anchorItemInput.Text.Trim();
        }
        if (tasteGenreInput != null)
        {
            if (string.IsNullOrWhiteSpace(tasteGenreInput.Text)) config.Remove("genre");
            else config["genre"] = tasteGenreInput.Text.Trim();
        }
        if (selected.Definition.Type == "admin_curated_list")
            config["item_ids"] = curatedItems.Select(item => (object)item.Id).ToList();
        var title = string.IsNullOrWhiteSpace(sectionTitle.Text) ? selected.DisplayName : sectionTitle.Text.Trim();
        var limit = double.IsNaN(itemLimit.Value) ? 20 : (int)itemLimit.Value;
        var isFeatured = featured.IsChecked == true;
        var isEnabled = enabled.IsChecked == true;
        var targetLibraryIds = applyToLibraries.IsChecked == true
            ? await ChooseGalleryLibrariesAsync()
            : [];
        if (applyToLibraries.IsChecked == true && targetLibraryIds.Count == 0) return;

        var traktPreset = GetGalleryConfigString(config, "source_provider") == "trakt"
            ? GetGalleryConfigString(config, "source_preset")
            : null;
        var traktMediaType = GetGalleryConfigString(config, "media_type");
        var isManagedTrakt = selected.Definition.Type == "collection" &&
            (traktPreset == "trending" || traktPreset == "popular") &&
            (traktMediaType == "movie" || traktMediaType == "tv") &&
            string.IsNullOrWhiteSpace(GetGalleryConfigString(config, "library_collection_id"));

        try
        {
            if (targetLibraryIds.Count > 0 && isManagedTrakt)
            {
                foreach (var libraryId in targetLibraryIds)
                {
                    var collection = await ViewModel.EnsureManagedTraktCollectionAsync(
                        libraryId, title, traktPreset!, traktMediaType!, limit, isFeatured);
                    var scopedConfig = new Dictionary<string, object?>(config)
                    {
                        ["library_collection_id"] = collection.Id,
                    };
                    await ViewModel.CreateLibrarySectionAsync(
                        libraryId, title, selected.Definition.Type, limit, isFeatured, isEnabled, scopedConfig);
                }
                await ViewModel.RefreshAfterGalleryMutationAsync(
                    $"Created {targetLibraryIds.Count} section{(targetLibraryIds.Count == 1 ? "" : "s")}.");
            }
            else if (targetLibraryIds.Count > 0)
            {
                await ViewModel.BulkCreateSectionsAsync(
                    targetLibraryIds, title, selected.Definition.Type, limit, isFeatured, isEnabled, config);
            }
            else
            {
                if (isManagedTrakt)
                {
                    var targetLibrary = ViewModel.SelectedLibraryId.HasValue
                        ? ViewModel.Libraries.FirstOrDefault(library => library.Id == ViewModel.SelectedLibraryId.Value)
                        : ViewModel.Libraries.FirstOrDefault(library =>
                            library.Type == (traktMediaType == "tv" ? "series" : "movies"))
                            ?? ViewModel.Libraries.FirstOrDefault();
                    if (targetLibrary == null)
                        throw new InvalidOperationException("Choose a library before adding this Trakt section.");
                    var collection = await ViewModel.EnsureManagedTraktCollectionAsync(
                        targetLibrary.Id, title, traktPreset!, traktMediaType!, limit, isFeatured);
                    config["library_collection_id"] = collection.Id;
                }

                var body = ViewModel.BuildCreateBody(
                    title, selected.Definition.Type, limit, isFeatured, isEnabled, config);
                await ViewModel.CreateSectionCommand.ExecuteAsync(body);
            }
            ShowStatus(ViewModel.ErrorMessage ?? ViewModel.StatusMessage ?? "Section added.");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
        }
    }

    private async Task<List<int>> ChooseGalleryLibrariesAsync()
    {
        var choices = new StackPanel { Spacing = 8, Width = 380 };
        var checks = ViewModel.Libraries.Select(library =>
        {
            var check = new CheckBox { Content = library.Name, IsChecked = true };
            choices.Children.Add(check);
            return (library.Id, Check: check);
        }).ToList();
        var dialog = new ContentDialog
        {
            Title = "Apply to which libraries?",
            Content = choices,
            PrimaryButtonText = $"Apply ({checks.Count})",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        void UpdateCount() => dialog.PrimaryButtonText =
            $"Apply ({checks.Count(entry => entry.Check.IsChecked == true)})";
        foreach (var entry in checks)
        {
            entry.Check.Checked += (_, _) => UpdateCount();
            entry.Check.Unchecked += (_, _) => UpdateCount();
        }
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? checks.Where(entry => entry.Check.IsChecked == true).Select(entry => entry.Id).ToList()
            : [];
    }

    private static string? GetGalleryConfigString(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value == null) return null;
        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.String)
            return element.GetString();
        return value.ToString();
    }

    private static int? TryGetGalleryConfigInt(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value == null) return null;
        if (value is System.Text.Json.JsonElement element && element.TryGetInt32(out var jsonValue)) return jsonValue;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? GetGalleryConfigBool(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value == null) return null;
        if (value is bool boolean) return boolean;
        if (value is System.Text.Json.JsonElement element &&
            element.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            return element.GetBoolean();
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static List<string> GetGalleryConfigStrings(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value == null) return [];
        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
            return element.EnumerateArray()
                .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        if (value is IEnumerable<object> objects)
            return objects.Select(item => item?.ToString() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        return [];
    }

    private static List<int> GetGalleryConfigInts(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value == null) return [];
        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
            return element.EnumerateArray().Where(item => item.TryGetInt32(out _)).Select(item => item.GetInt32()).ToList();
        if (value is IEnumerable<object> objects)
            return objects.Select(item => int.TryParse(item?.ToString(), out var parsed) ? parsed : int.MinValue)
                .Where(item => item != int.MinValue)
                .ToList();
        return [];
    }

    private static Dictionary<string, string> GetGalleryConfigStringDictionary(
        IReadOnlyDictionary<string, object?> config,
        string key)
    {
        if (!config.TryGetValue(key, out var value) || value == null) return new();
        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object)
            return element.EnumerateObject()
                .Where(property => property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "");
        if (value is IDictionary<string, object> dictionary)
            return dictionary.ToDictionary(entry => entry.Key, entry => entry.Value?.ToString() ?? "");
        if (value is IDictionary<string, string> stringDictionary)
            return new Dictionary<string, string>(stringDictionary);
        return new();
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

    private static int ScoreGalleryChoice(string query, GalleryRecipeChoice choice)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        var normalized = query.Trim();
        if (choice.DisplayName.Equals(normalized, StringComparison.CurrentCultureIgnoreCase)) return 100;
        if (choice.DisplayName.StartsWith(normalized, StringComparison.CurrentCultureIgnoreCase)) return 80;
        if (choice.DisplayName.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)) return 60;
        if (choice.Preset.DescriptionShort.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)) return 30;
        return 0;
    }

    private sealed record GalleryCollectionChoice(string Id, bool IsUserCollection);

    private sealed record GalleryCuratedItem(string Id, string Label);

    private static async Task HydrateCuratedItemLabelsAsync(
        List<GalleryCuratedItem> items,
        Action rebuild)
    {
        var pending = items.Where(item => item.Label == item.Id).ToList();
        if (pending.Count == 0) return;
        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
        foreach (var pendingItem in pending)
        {
            try
            {
                var detail = await api.GetItemDetailAsync(pendingItem.Id);
                var index = items.FindIndex(item => item.Id == pendingItem.Id);
                if (index >= 0)
                    items[index] = new GalleryCuratedItem(
                        pendingItem.Id,
                        detail.Year > 0 ? $"{detail.Title} ({detail.Year})" : detail.Title);
            }
            catch
            {
                // The raw id remains a stable fallback when a saved item no longer resolves.
            }
        }
        rebuild();
    }

    private void CloseGalleryOverlay(GalleryRecipeChoice? choice)
        => _galleryCompletion?.TrySetResult(choice);

    private void GalleryOverlayDismiss_Click(object sender, RoutedEventArgs e)
        => CloseGalleryOverlay(null);

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
        if (_adminShell is not null)
            _adminShell.SetPageOverlayVisible(SectionEditorOverlay, true);
        else
            SectionEditorOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSectionEditor_Click(object sender, RoutedEventArgs e) => CloseSectionEditor();

    private void CloseSectionEditor()
    {
        _sectionPreviewTimer?.Stop();
        _sectionPreviewAction = null;
        if (_adminShell is not null)
            _adminShell.SetPageOverlayVisible(SectionEditorOverlay, false);
        else
            SectionEditorOverlay.Visibility = Visibility.Collapsed;
        SectionEditorContent.Content = null;
        _editingSection = null;
        _sectionEditorGetBody = null;
    }

    private void ConfigureSectionPreview(Func<Task> action)
    {
        _sectionPreviewTimer?.Stop();
        _sectionPreviewAction = action;
        _sectionPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _sectionPreviewTimer.Tick += async (_, _) =>
        {
            _sectionPreviewTimer?.Stop();
            if (_sectionPreviewAction != null) await _sectionPreviewAction();
        };
    }

    private void ScheduleSectionPreview()
    {
        if (_sectionPreviewTimer == null || _sectionPreviewAction == null) return;
        _sectionPreviewTimer.Stop();
        _sectionPreviewTimer.Start();
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

    private sealed record RecipeParameterEditor(
        FrameworkElement Content,
        Func<Dictionary<string, object?>> GetConfig);

    private sealed class EditableFilterGroup
    {
        public string Match { get; set; } = "all";
        public List<EditableFilterRule> Rules { get; } = [];
    }

    private sealed class EditableFilterRule
    {
        public string Field { get; set; } = "genre";
        public string Operator { get; set; } = "contains";
        public string Value { get; set; } = "";
    }

    private RecipeParameterEditor BuildLegacyFilterEditor(
        IReadOnlyDictionary<string, object?> seed,
        bool seedGenreRule)
    {
        var config = seed.ToDictionary(entry => entry.Key, entry => entry.Value);
        var groups = ReadEditableFilterGroups(config);
        if (groups.Count == 0)
        {
            var initial = new EditableFilterGroup();
            if (seedGenreRule) initial.Rules.Add(new EditableFilterRule());
            groups.Add(initial);
        }
        var outerMatch = GetGalleryConfigString(config, "match") == "any" ? "any" : "all";
        var advanced = groups.Count > 1 || outerMatch == "any";
        var root = new StackPanel { Spacing = 10 };
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var easyButton = new Button { Content = "Easy", Padding = new Thickness(10, 3, 10, 3) };
        var advancedButton = new Button { Content = "Advanced", Padding = new Thickness(10, 3, 10, 3) };
        modeRow.Children.Add(easyButton);
        modeRow.Children.Add(advancedButton);
        root.Children.Add(modeRow);
        var editorHost = new StackPanel { Spacing = 10 };
        root.Children.Add(editorHost);

        void ApplyModeButtons()
        {
            var active = new SolidColorBrush(Color.FromArgb(255, 99, 102, 241));
            var inactive = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
            easyButton.Background = advanced ? inactive : active;
            advancedButton.Background = advanced ? active : inactive;
            easyButton.Foreground = advanced
                ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
                : new SolidColorBrush(Colors.White);
            advancedButton.Foreground = advanced
                ? new SolidColorBrush(Colors.White)
                : (Brush)Application.Current.Resources["SecondaryTextBrush"];
        }

        void ApplyTemplate(params EditableFilterRule[] rules)
        {
            groups.Clear();
            var group = new EditableFilterGroup();
            group.Rules.AddRange(rules);
            groups.Add(group);
            outerMatch = "all";
            RebuildEditor();
        }

        FrameworkElement BuildRuleRow(EditableFilterGroup group, EditableFilterRule rule)
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fields = new[]
            {
                ("Genre", "genre"), ("Year", "year"), ("Rating (IMDb)", "rating_imdb"),
                ("Director", "director"), ("Studio", "studio"), ("Cast", "cast"),
                ("Library", "library"), ("Has been watched", "watched"), ("Language", "language"),
                ("Runtime (min)", "runtime"), ("Keyword", "keyword"),
            };
            var field = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in fields) field.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            field.SelectedIndex = Math.Max(0, Array.FindIndex(fields, option => option.Item2 == rule.Field));
            var operators = new[]
            {
                ("is", "is"), ("is not", "is_not"), (">=", "gte"), ("<=", "lte"),
                ("between", "between"), ("contains", "contains"),
            };
            var op = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in operators) op.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            op.SelectedIndex = Math.Max(0, Array.FindIndex(operators, option => option.Item2 == rule.Operator));
            var value = new TextBox { Text = rule.Value, PlaceholderText = rule.Operator == "between" ? "start, end" : "value" };
            var remove = new Button { Content = "\u2715", Padding = new Thickness(8, 3, 8, 3) };
            field.SelectionChanged += (_, _) => rule.Field = (field.SelectedItem as ComboBoxItem)?.Tag as string ?? "genre";
            op.SelectionChanged += (_, _) =>
            {
                rule.Operator = (op.SelectedItem as ComboBoxItem)?.Tag as string ?? "contains";
                value.PlaceholderText = rule.Operator == "between" ? "start, end" : "value";
            };
            value.TextChanged += (_, _) => rule.Value = value.Text;
            remove.Click += (_, _) => { group.Rules.Remove(rule); RebuildEditor(); };
            Grid.SetColumn(field, 0); Grid.SetColumn(op, 1); Grid.SetColumn(value, 2); Grid.SetColumn(remove, 3);
            row.Children.Add(field); row.Children.Add(op); row.Children.Add(value); row.Children.Add(remove);
            return row;
        }

        FrameworkElement BuildGroup(EditableFilterGroup group, int index)
        {
            var body = new StackPanel { Spacing = 7 };
            if (advanced)
            {
                var header = new Grid { ColumnSpacing = 8 };
                header.ColumnDefinitions.Add(new ColumnDefinition());
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(new TextBlock
                {
                    Text = $"Group {index + 1}",
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var match = new ComboBox { Width = 100 };
                match.Items.Add(new ComboBoxItem { Content = "Match all", Tag = "all" });
                match.Items.Add(new ComboBoxItem { Content = "Match any", Tag = "any" });
                match.SelectedIndex = group.Match == "any" ? 1 : 0;
                match.SelectionChanged += (_, _) => group.Match = (match.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
                var removeGroup = new Button { Content = "Remove", Padding = new Thickness(8, 3, 8, 3), IsEnabled = groups.Count > 1 };
                removeGroup.Click += (_, _) => { groups.Remove(group); RebuildEditor(); };
                Grid.SetColumn(match, 1); Grid.SetColumn(removeGroup, 2);
                header.Children.Add(match); header.Children.Add(removeGroup);
                body.Children.Add(header);
            }
            foreach (var rule in group.Rules.ToList()) body.Children.Add(BuildRuleRow(group, rule));
            var addRule = new Button { Content = "+ Add filter", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 4) };
            addRule.Click += (_, _) => { group.Rules.Add(new EditableFilterRule()); RebuildEditor(); };
            body.Children.Add(addRule);
            return new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10),
                Child = body,
            };
        }

        void RebuildEditor()
        {
            ApplyModeButtons();
            editorHost.Children.Clear();
            if (!advanced)
            {
                editorHost.Children.Add(new TextBlock
                {
                    Text = "QUICK-START TEMPLATES",
                    FontSize = 10,
                    CharacterSpacing = 80,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                });
                var templates = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                void AddTemplate(string label, Action action)
                {
                    var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), FontSize = 11 };
                    button.Click += (_, _) => action();
                    templates.Children.Add(button);
                }
                AddTemplate("Highly rated", () => ApplyTemplate(new EditableFilterRule { Field = "rating_imdb", Operator = "gte", Value = "7.5" }));
                AddTemplate("By decade", () => ApplyTemplate(new EditableFilterRule { Field = "year", Operator = "between", Value = "1990, 1999" }));
                AddTemplate("Genre + decade", () => ApplyTemplate(
                    new EditableFilterRule { Field = "genre", Operator = "contains", Value = "Sci-Fi" },
                    new EditableFilterRule { Field = "year", Operator = "between", Value = "1980, 1989" }));
                AddTemplate("Empty", () => ApplyTemplate());
                editorHost.Children.Add(new ScrollViewer
                {
                    HorizontalScrollMode = ScrollMode.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    Content = templates,
                });
                var match = new ComboBox { Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
                match.Items.Add(new ComboBoxItem { Content = "Match all", Tag = "all" });
                match.Items.Add(new ComboBoxItem { Content = "Match any", Tag = "any" });
                match.SelectedIndex = groups[0].Match == "any" ? 1 : 0;
                match.SelectionChanged += (_, _) => groups[0].Match = (match.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
                editorHost.Children.Add(match);
            }
            else
            {
                var match = new ComboBox { Width = 145, HorizontalAlignment = HorizontalAlignment.Left };
                match.Items.Add(new ComboBoxItem { Content = "Match all groups", Tag = "all" });
                match.Items.Add(new ComboBoxItem { Content = "Match any group", Tag = "any" });
                match.SelectedIndex = outerMatch == "any" ? 1 : 0;
                match.SelectionChanged += (_, _) => outerMatch = (match.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
                editorHost.Children.Add(match);
            }
            for (var index = 0; index < groups.Count; index++) editorHost.Children.Add(BuildGroup(groups[index], index));
            if (advanced)
            {
                var addGroup = new Button { Content = "+ Add group", HorizontalAlignment = HorizontalAlignment.Left };
                addGroup.Click += (_, _) => { groups.Add(new EditableFilterGroup()); RebuildEditor(); };
                editorHost.Children.Add(addGroup);
            }
        }

        easyButton.Click += (_, _) =>
        {
            advanced = false;
            if (groups.Count > 1) groups.RemoveRange(1, groups.Count - 1);
            outerMatch = "all";
            RebuildEditor();
        };
        advancedButton.Click += (_, _) => { advanced = true; RebuildEditor(); };
        RebuildEditor();

        Dictionary<string, object?> ReadConfig()
        {
            config["match"] = advanced ? outerMatch : "all";
            config["groups"] = groups.Select(group => (object)new Dictionary<string, object?>
            {
                ["match"] = group.Match,
                ["rules"] = group.Rules.Select(rule => (object)new Dictionary<string, object?>
                {
                    ["field"] = rule.Field,
                    ["op"] = rule.Operator,
                    ["value"] = ParseFilterRuleValue(rule),
                }).ToList(),
            }).ToList();
            if (!config.ContainsKey("sort"))
                config["sort"] = new Dictionary<string, object?> { ["field"] = "added_at", ["order"] = "desc" };
            return config.ToDictionary(entry => entry.Key, entry => entry.Value);
        }
        return new RecipeParameterEditor(root, ReadConfig);
    }

    private static object ParseFilterRuleValue(EditableFilterRule rule)
    {
        var value = rule.Value.Trim();
        if (rule.Operator == "between")
        {
            var parts = value.Split([',', '-'], 2, StringSplitOptions.TrimEntries);
            return parts.Length == 2
                ? new object[] { ParseScalarFilterValue(rule.Field, parts[0]), ParseScalarFilterValue(rule.Field, parts[1]) }
                : new object[] { value, value };
        }
        return ParseScalarFilterValue(rule.Field, value);
    }

    private static object ParseScalarFilterValue(string field, string value)
    {
        if (field == "watched" && bool.TryParse(value, out var boolean)) return boolean;
        if (field is "year" or "runtime" && int.TryParse(value, out var integer)) return integer;
        if (field == "rating_imdb" && double.TryParse(value, out var number)) return number;
        return value;
    }

    private static List<EditableFilterGroup> ReadEditableFilterGroups(IReadOnlyDictionary<string, object?> config)
    {
        var groups = new List<EditableFilterGroup>();
        if (!config.TryGetValue("groups", out var raw) || raw is not System.Text.Json.JsonElement element ||
            element.ValueKind != System.Text.Json.JsonValueKind.Array) return groups;
        foreach (var groupElement in element.EnumerateArray())
        {
            if (groupElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            var group = new EditableFilterGroup
            {
                Match = groupElement.TryGetProperty("match", out var match) && match.GetString() == "any" ? "any" : "all",
            };
            if (groupElement.TryGetProperty("rules", out var rules) && rules.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var ruleElement in rules.EnumerateArray())
                {
                    if (ruleElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    var op = ruleElement.TryGetProperty("op", out var opElement)
                        ? opElement.GetString()
                        : ruleElement.TryGetProperty("operator", out var legacyOp) ? legacyOp.GetString() : null;
                    group.Rules.Add(new EditableFilterRule
                    {
                        Field = ruleElement.TryGetProperty("field", out var field) ? field.GetString() ?? "genre" : "genre",
                        Operator = op ?? "contains",
                        Value = ruleElement.TryGetProperty("value", out var value) ? FormatFilterRuleValue(value) : "",
                    });
                }
            }
            groups.Add(group);
        }
        return groups;
    }

    private static string FormatFilterRuleValue(System.Text.Json.JsonElement value)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Array)
            return string.Join(", ", value.EnumerateArray().Select(FormatFilterRuleValue));
        return value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private RecipeParameterEditor BuildRecipeParameterEditor(
        string sectionType,
        IReadOnlyDictionary<string, object?> seed)
    {
        var config = seed.ToDictionary(entry => entry.Key, entry => entry.Value);
        var panel = new StackPanel { Spacing = 12 };

        if (sectionType == "continue_watching")
        {
            var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.Items.Add(new ComboBoxItem { Content = "Watching", Tag = "watching" });
            picker.Items.Add(new ComboBoxItem { Content = "Listening", Tag = "listening" });
            picker.SelectedIndex = GetGalleryConfigString(config, "continue_type") == "listening" ? 1 : 0;
            picker.SelectionChanged += (_, _) =>
                config["continue_type"] = (picker.SelectedItem as ComboBoxItem)?.Tag as string ?? "watching";
            AddRecipeField(panel, "Continue type", picker);
        }

        if (sectionType is "watchlist" or "favorites")
        {
            var media = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var mediaOptions = new[]
            {
                ("All Media", ""), ("Movies", "movie"), ("TV Shows", "series"), ("Audiobooks", "audiobook")
            };
            foreach (var option in mediaOptions)
                media.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            media.SelectedIndex = Math.Max(0, Array.FindIndex(mediaOptions,
                option => option.Item2 == (GetGalleryConfigString(config, "filter_type") ?? "")));
            media.SelectionChanged += (_, _) =>
            {
                var value = (media.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (string.IsNullOrWhiteSpace(value)) config.Remove("filter_type");
                else config["filter_type"] = value;
            };
            AddRecipeField(panel, "Media type", media);

            var selectedLibraryIds = GetGalleryConfigInts(config, "filter_library_ids").ToHashSet();
            var libraryPanel = new StackPanel { Spacing = 3 };
            var libraryChecks = new List<(int Id, CheckBox Check)>();
            void CommitPersonalLibraries()
            {
                var ids = libraryChecks.Where(entry => entry.Check.IsChecked == true)
                    .Select(entry => (object)entry.Id).ToList();
                if (ids.Count == 0) config.Remove("filter_library_ids");
                else config["filter_library_ids"] = ids;
            }
            foreach (var library in ViewModel.Libraries)
            {
                var check = new CheckBox { Content = library.Name, IsChecked = selectedLibraryIds.Contains(library.Id) };
                libraryChecks.Add((library.Id, check));
                check.Checked += (_, _) => CommitPersonalLibraries();
                check.Unchecked += (_, _) => CommitPersonalLibraries();
                libraryPanel.Children.Add(check);
            }
            AddRecipeField(panel, "Libraries", new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Child = libraryPanel,
            });

            var sort = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var sortOptions = new[]
            {
                ("List order (default)", ""),
                ("Date added (newest first)", "added_at:desc"),
                ("Date added (oldest first)", "added_at:asc"),
                ("Title (A-Z)", "title:asc"),
                ("Title (Z-A)", "title:desc"),
                ("Release date (newest first)", "release_date:desc"),
                ("Release date (oldest first)", "release_date:asc"),
                ("IMDb rating (highest first)", "rating_imdb:desc"),
            };
            foreach (var option in sortOptions)
                sort.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            var sortField = GetGalleryConfigString(config, "sort") ?? "";
            var sortOrder = GetGalleryConfigString(config, "order") ?? (sortField == "title" ? "asc" : "desc");
            var sortValue = string.IsNullOrWhiteSpace(sortField) ? "" : $"{sortField}:{sortOrder}";
            sort.SelectedIndex = Math.Max(0, Array.FindIndex(sortOptions, option => option.Item2 == sortValue));
            sort.SelectionChanged += (_, _) =>
            {
                var value = (sort.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (string.IsNullOrWhiteSpace(value))
                {
                    config.Remove("sort");
                    config.Remove("order");
                }
                else
                {
                    var parts = value.Split(':', 2);
                    config["sort"] = parts[0];
                    config["order"] = parts.Length > 1 ? parts[1] : "desc";
                }
            };
            AddRecipeField(panel, "Sort", sort);
        }

        if (sectionType == "seasonal_themed")
        {
            var enabledThemes = GetGalleryConfigStrings(config, "enabled_themes").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var legacyTheme = GetGalleryConfigString(config, "theme");
            if (enabledThemes.Count == 0 && !string.IsNullOrWhiteSpace(legacyTheme)) enabledThemes.Add(legacyTheme);
            var themeTitles = GetGalleryConfigStringDictionary(config, "theme_titles");
            var themePanel = new StackPanel { Spacing = 7 };
            var themeInputs = new List<(string Key, CheckBox Check, TextBox Title)>();
            var themes = new[]
            {
                ("valentines", "Valentine's Day", "Feb 7-14", "\U0001F49D"),
                ("st_patricks", "St. Patrick's Day", "Mar 15-17", "\U0001F340"),
                ("thanksgiving", "Thanksgiving", "Nov 22-30", "\U0001F983"),
                ("christmas", "Christmas", "Dec 1-31", "\U0001F384"),
                ("halloween", "Halloween", "All October", "\U0001F383"),
                ("saturday_morning", "Saturday Morning Cartoons", "Saturday before 1pm", "\U0001F4FA"),
                ("family_movie_night", "Family Movie Night", "Fri & Sat from 5pm", "\U0001F37F"),
                ("summer_blockbuster", "Summer Blockbusters", "June - August", "\U0001F334"),
            };
            void CommitThemes()
            {
                config["enabled_themes"] = themeInputs.Where(entry => entry.Check.IsChecked == true)
                    .Select(entry => (object)entry.Key).ToList();
                var titles = themeInputs
                    .Where(entry => entry.Check.IsChecked == true && !string.IsNullOrWhiteSpace(entry.Title.Text))
                    .ToDictionary(entry => entry.Key, entry => (object)entry.Title.Text.Trim());
                if (titles.Count == 0) config.Remove("theme_titles");
                else config["theme_titles"] = titles;
                config["theme"] = "";
                config["mode"] = "";
            }
            foreach (var theme in themes)
            {
                var check = new CheckBox
                {
                    Content = $"{theme.Item4}  {theme.Item2}   {theme.Item3}",
                    IsChecked = enabledThemes.Contains(theme.Item1),
                };
                var title = new TextBox
                {
                    PlaceholderText = $"Section title in season - defaults to \"{theme.Item2}\"",
                    Text = themeTitles.GetValueOrDefault(theme.Item1, ""),
                    Margin = new Thickness(28, 0, 0, 0),
                    Visibility = check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
                };
                themeInputs.Add((theme.Item1, check, title));
                check.Checked += (_, _) => { title.Visibility = Visibility.Visible; CommitThemes(); };
                check.Unchecked += (_, _) => { title.Visibility = Visibility.Collapsed; CommitThemes(); };
                title.TextChanged += (_, _) => CommitThemes();
                themePanel.Children.Add(check);
                themePanel.Children.Add(title);
            }
            AddRecipeField(panel, "Holidays to celebrate", new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Child = themePanel,
            });
        }

        if (sectionType == "editorial_spotlight")
        {
            var subjectType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var subjectOptions = new[] { ("Director", "director"), ("Studio", "studio"), ("Actor", "actor"), ("Era", "era") };
            foreach (var option in subjectOptions)
                subjectType.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            subjectType.SelectedIndex = Math.Max(0, Array.FindIndex(subjectOptions,
                option => option.Item2 == (GetGalleryConfigString(config, "subject_type") ?? "director")));
            var autoRotate = new CheckBox
            {
                Content = "Auto-rotate",
                IsChecked = GetGalleryConfigBool(config, "auto_rotate") ?? string.IsNullOrWhiteSpace(GetGalleryConfigString(config, "subject")),
            };
            var cadence = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var cadenceOptions = new[] { ("Daily", "daily"), ("Weekly (default)", "weekly"), ("Monthly", "monthly") };
            foreach (var option in cadenceOptions)
                cadence.Items.Add(new ComboBoxItem { Content = option.Item1, Tag = option.Item2 });
            cadence.SelectedIndex = Math.Max(0, Array.FindIndex(cadenceOptions,
                option => option.Item2 == (GetGalleryConfigString(config, "rotation_cadence") ?? "weekly")));
            var subject = new TextBox
            {
                PlaceholderText = "e.g. Christopher Nolan",
                Text = GetGalleryConfigString(config, "subject") ?? "",
            };
            var cadenceField = AddRecipeField(panel, "Rotation cadence", cadence);
            var subjectField = AddRecipeField(panel, "Subject", subject);
            void CommitEditorial()
            {
                config["subject_type"] = (subjectType.SelectedItem as ComboBoxItem)?.Tag as string ?? "director";
                var automatic = autoRotate.IsChecked == true;
                config["auto_rotate"] = automatic;
                cadenceField.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
                subjectField.Visibility = automatic ? Visibility.Collapsed : Visibility.Visible;
                if (automatic)
                {
                    config["rotation_cadence"] = (cadence.SelectedItem as ComboBoxItem)?.Tag as string ?? "weekly";
                    config.Remove("subject");
                }
                else
                {
                    config.Remove("rotation_cadence");
                    config["subject"] = subject.Text.Trim();
                }
            }
            subjectType.SelectionChanged += (_, _) => CommitEditorial();
            autoRotate.Checked += (_, _) => CommitEditorial();
            autoRotate.Unchecked += (_, _) => CommitEditorial();
            cadence.SelectionChanged += (_, _) => CommitEditorial();
            subject.TextChanged += (_, _) => CommitEditorial();
            AddRecipeField(panel, "Subject type", subjectType, insertAt: 0);
            panel.Children.Insert(1, autoRotate);
            CommitEditorial();
        }

        if (sectionType is "because_you_watched" or "taste_match")
        {
            var key = sectionType == "because_you_watched" ? "anchor_item_id" : "genre";
            var input = new TextBox
            {
                PlaceholderText = sectionType == "because_you_watched"
                    ? "Auto-pick latest watched (leave blank)"
                    : "Auto-pick your strongest genre (leave blank)",
                Text = GetGalleryConfigString(config, key) ?? "",
            };
            input.TextChanged += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(input.Text)) config.Remove(key);
                else config[key] = input.Text.Trim();
            };
            AddRecipeField(panel, sectionType == "because_you_watched" ? "Anchor item" : "Genre (optional)", input);
        }

        var numberKey = sectionType switch
        {
            "returning_shows" => "lookback_days",
            "short_watches" => "max_minutes",
            "anniversaries" => "milestone_years",
            _ => null,
        };
        if (numberKey != null)
        {
            var number = new NumberBox
            {
                Minimum = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = TryGetGalleryConfigInt(config, numberKey) ?? double.NaN,
            };
            number.ValueChanged += (_, _) =>
            {
                if (double.IsNaN(number.Value)) config.Remove(numberKey);
                else config[numberKey] = Math.Max(1, (int)number.Value);
            };
            AddRecipeField(panel, numberKey switch
            {
                "lookback_days" => "Lookback window (days)",
                "max_minutes" => "Maximum runtime (minutes)",
                _ => "Milestone (years)",
            }, number);
        }

        if (sectionType == "admin_curated_list")
            BuildCuratedRecipeFields(panel, config);

        return new RecipeParameterEditor(panel, () =>
            config.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    private static StackPanel AddRecipeField(
        StackPanel panel,
        string label,
        FrameworkElement control,
        int? insertAt = null)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        field.Children.Add(control);
        if (insertAt.HasValue) panel.Children.Insert(insertAt.Value, field);
        else panel.Children.Add(field);
        return field;
    }

    private void BuildCuratedRecipeFields(StackPanel panel, Dictionary<string, object?> config)
    {
        var curatedItems = GetGalleryConfigStrings(config, "item_ids")
            .Select(id => new GalleryCuratedItem(id, id)).ToList();
        var search = new TextBox { PlaceholderText = "Search your catalog..." };
        var searchButton = new Button { Content = "Search" };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition());
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(search);
        Grid.SetColumn(searchButton, 1);
        searchRow.Children.Add(searchButton);
        var results = new StackPanel { Spacing = 4 };
        var picked = new StackPanel { Spacing = 4 };

        void CommitCurated() => config["item_ids"] = curatedItems.Select(item => (object)item.Id).ToList();
        void RebuildPicked()
        {
            picked.Children.Clear();
            if (curatedItems.Count == 0)
            {
                picked.Children.Add(new TextBlock
                {
                    Text = "Search above and add at least one title.",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                CommitCurated();
                return;
            }
            for (var index = 0; index < curatedItems.Count; index++)
            {
                var itemIndex = index;
                var item = curatedItems[index];
                var row = new Grid { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                for (var column = 0; column < 3; column++)
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = item.Label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var up = new Button { Content = "\u2191", IsEnabled = itemIndex > 0, Padding = new Thickness(8, 2, 8, 2) };
                var down = new Button { Content = "\u2193", IsEnabled = itemIndex < curatedItems.Count - 1, Padding = new Thickness(8, 2, 8, 2) };
                var remove = new Button { Content = "\u2715", Padding = new Thickness(8, 2, 8, 2) };
                up.Click += (_, _) => { var value = curatedItems[itemIndex]; curatedItems.RemoveAt(itemIndex); curatedItems.Insert(itemIndex - 1, value); RebuildPicked(); };
                down.Click += (_, _) => { var value = curatedItems[itemIndex]; curatedItems.RemoveAt(itemIndex); curatedItems.Insert(itemIndex + 1, value); RebuildPicked(); };
                remove.Click += (_, _) => { curatedItems.RemoveAt(itemIndex); RebuildPicked(); };
                Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); Grid.SetColumn(remove, 3);
                row.Children.Add(up); row.Children.Add(down); row.Children.Add(remove);
                picked.Children.Add(row);
            }
            CommitCurated();
        }

        async Task SearchAsync()
        {
            var query = search.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;
            searchButton.IsEnabled = false;
            results.Children.Clear();
            results.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
            try
            {
                var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
                var response = await api.SearchAsync(query, 10);
                results.Children.Clear();
                if (response.Items.Count == 0)
                    results.Children.Add(new TextBlock { Text = "No matches." });
                foreach (var result in response.Items)
                {
                    var label = result.Year > 0 ? $"{result.Title} ({result.Year})" : result.Title;
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{label}  \u00B7  {result.Type}",
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    var add = new Button
                    {
                        Content = curatedItems.Any(item => item.Id == result.ContentId) ? "Added" : "Add",
                        IsEnabled = curatedItems.All(item => item.Id != result.ContentId),
                        Padding = new Thickness(10, 3, 10, 3),
                    };
                    add.Click += (_, _) =>
                    {
                        if (curatedItems.Any(item => item.Id == result.ContentId)) return;
                        curatedItems.Add(new GalleryCuratedItem(result.ContentId, label));
                        add.Content = "Added";
                        add.IsEnabled = false;
                        RebuildPicked();
                    };
                    Grid.SetColumn(add, 1);
                    row.Children.Add(add);
                    results.Children.Add(row);
                }
            }
            catch
            {
                results.Children.Clear();
                results.Children.Add(new TextBlock { Text = "Search failed - try again." });
            }
            finally { searchButton.IsEnabled = true; }
        }
        searchButton.Click += async (_, _) => await SearchAsync();
        search.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key == Windows.System.VirtualKey.Enter) await SearchAsync();
        };
        AddRecipeField(panel, "Add titles", searchRow);
        panel.Children.Add(results);
        AddRecipeField(panel, "Curated list (shown in this order)", new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = picked,
        });
        RebuildPicked();
        _ = HydrateCuratedItemLabelsAsync(curatedItems, RebuildPicked);
    }

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
        var editorCategoryLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["library_staples"] = "Library",
            ["personalized"] = "For You",
            ["discovery"] = "Discovery",
            ["editorial"] = "Editorial",
            ["seasonal"] = "Seasonal",
            ["mood"] = "Mood",
            ["social"] = "Social",
            ["hand_picked"] = "Hand Picked",
            ["custom"] = "Custom",
        };
        if (ViewModel.RecipeCatalog?.Categories.Count > 0)
        {
            foreach (var category in ViewModel.RecipeCatalog.Categories)
            {
                var headerAdded = false;
                foreach (var definition in category.Value)
                {
                    if (!addedTypes.Add(definition.Type)) continue;
                    if (!headerAdded)
                    {
                        typeCombo.Items.Add(new ComboBoxItem
                        {
                            Content = editorCategoryLabels.GetValueOrDefault(category.Key, category.Key.Replace('_', ' ')).ToUpperInvariant(),
                            IsEnabled = false,
                            FontSize = 10,
                            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                        });
                        headerAdded = true;
                    }
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
        var desiredSectionType = existing?.SectionType ?? "recently_added";
        foreach (ComboBoxItem item in typeCombo.Items)
        {
            if (item.Tag as string == desiredSectionType)
            {
                typeCombo.SelectedItem = item;
                break;
            }
        }
        if (typeCombo.SelectedItem == null)
            typeCombo.SelectedItem = typeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.IsEnabled);

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
            collectionPicker.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = new GalleryCollectionChoice(c.Id, IsUserCollection: false),
            });
        }
        foreach (var c in ViewModel.UserCollections)
            collectionPicker.Items.Add(new ComboBoxItem
            {
                Content = c.Name,
                Tag = new GalleryCollectionChoice(c.Id, IsUserCollection: true),
            });
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
            var existingUserCollId = AdminSectionsViewModel.GetConfigUserCollectionId(existing);
            var existingCollId = existingUserCollId ?? AdminSectionsViewModel.GetConfigCollectionId(existing);
            if (existingCollId != null)
            {
                for (int i = 0; i < collectionPicker.Items.Count; i++)
                    if (collectionPicker.Items[i] is ComboBoxItem ci &&
                        ci.Tag is GalleryCollectionChoice choice &&
                        choice.Id == existingCollId &&
                        choice.IsUserCollection == (existingUserCollId != null))
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

        var filterFields = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        form.Children.Add(filterFields);
        RecipeParameterEditor? activeFilterEditor = null;
        string? activeFilterType = null;

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

        var recipeFields = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        form.Children.Add(recipeFields);
        RecipeParameterEditor? activeRecipeEditor = null;
        string? activeRecipeType = null;

        var previewStatus = new TextBlock
        {
            Text = "Preview · waiting for configuration",
            FontSize = 10,
            CharacterSpacing = 80,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        var previewItems = new TextBlock
        {
            Text = "",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        var refreshPreview = new Button
        {
            Content = "Refresh preview",
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var previewHeader = new Grid();
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewHeader.Children.Add(previewStatus);
        Grid.SetColumn(refreshPreview, 1);
        previewHeader.Children.Add(refreshPreview);
        var previewContent = new StackPanel { Spacing = 6 };
        previewContent.Children.Add(previewHeader);
        previewContent.Children.Add(previewItems);
        var previewBox = new Border
        {
            Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x63, 0x66, 0xF1)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x16, 0x63, 0x66, 0xF1)),
            Child = previewContent,
        };
        form.Children.Add(previewBox);

        async Task RunPreviewAsync()
        {
            if (GetBody() is not Dictionary<string, object?> body) return;
            var sectionType = body.GetValueOrDefault("section_type")?.ToString() ?? "";
            if (sectionType == "collection")
            {
                previewBox.Visibility = Visibility.Collapsed;
                return;
            }
            previewBox.Visibility = Visibility.Visible;
            previewStatus.Text = "Loading…";
            previewItems.Text = "";
            previewItems.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
            refreshPreview.IsEnabled = false;
            try
            {
                var config = body.GetValueOrDefault("config") as Dictionary<string, object?> ?? [];
                var libraryId = body.GetValueOrDefault("library_id") is int selectedLibraryId ? selectedLibraryId : (int?)null;
                var preview = await App.Services.GetRequiredService<AdminApi>().PreviewSectionAsync(new AdminSectionPreviewRequest
                {
                    SectionType = sectionType,
                    Config = config,
                    ItemLimit = body.GetValueOrDefault("item_limit") is int limit ? limit : null,
                    LibraryId = libraryId,
                    LibraryIds = ExtractPreviewLibraryIds(config),
                });
                previewStatus.Text = $"Preview · {preview.TotalCount} items match";
                previewItems.Text = string.Join(" · ", preview.Items.Take(10).Select(item => string.IsNullOrWhiteSpace(item.Title) ? item.ContentId : item.Title));
                if (string.IsNullOrWhiteSpace(previewItems.Text)) previewItems.Text = "no matches";
            }
            catch (Exception ex)
            {
                previewStatus.Text = "Error";
                previewItems.Text = ex.Message;
                previewItems.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
            }
            finally { refreshPreview.IsEnabled = true; }
        }
        ConfigureSectionPreview(RunPreviewAsync);
        refreshPreview.Click += async (_, _) => await RunPreviewAsync();

        // Visibility toggling based on type selection
        void UpdateConditionalFields()
        {
            var selType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            titleBox.PlaceholderText = ViewModel.RecipeLabels.GetValueOrDefault(
                selType,
                SectionTypeLabels.GetValueOrDefault(selType, selType.Replace('_', ' ')));
            collectionField.Visibility = selType == "collection" ? Visibility.Visible : Visibility.Collapsed;
            SaveSectionEditorButton.IsEnabled = selType != "collection" || collectionPicker.SelectedItem != null;
            bool isFilter = filterTypes.Contains(selType);
            bool isKnownRecipe = ViewModel.RecipeCatalog?.Categories.Values
                .SelectMany(definitions => definitions)
                .Any(definition => definition.Type == selType) == true;
            bool showScopeAndLibrary = isFilter || (selType != "collection" && !isKnownRecipe);
            mediaScopeField.Visibility = showScopeAndLibrary ? Visibility.Visible : Visibility.Collapsed;
            libraryField.Visibility = showScopeAndLibrary ? Visibility.Visible : Visibility.Collapsed;
            genreField.Visibility = Visibility.Collapsed;
            filterFields.Visibility = isFilter ? Visibility.Visible : Visibility.Collapsed;
            if (isFilter && activeFilterType != selType)
            {
                IReadOnlyDictionary<string, object?> filterSeed = new Dictionary<string, object?>();
                if (activeFilterType == null && existing?.SectionType == selType && existing.Config != null)
                    filterSeed = existing.Config.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
                activeFilterEditor = BuildLegacyFilterEditor(filterSeed, seedGenreRule: selType == "genre");
                activeFilterType = selType;
                filterFields.Children.Clear();
                filterFields.Children.Add(activeFilterEditor.Content);
                AttachSectionPreviewTriggers(activeFilterEditor.Content, ScheduleSectionPreview);
            }
            continueTypeField.Visibility = Visibility.Collapsed;
            recipeFields.Visibility = isKnownRecipe && selType != "collection"
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (recipeFields.Visibility == Visibility.Visible && activeRecipeType != selType)
            {
                IReadOnlyDictionary<string, object?> seed = new Dictionary<string, object?>();
                if (activeRecipeType == null && existing?.SectionType == selType && existing.Config != null)
                    seed = existing.Config.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
                else
                {
                    var defaults = ViewModel.RecipeCatalog?.Categories.Values
                        .SelectMany(definitions => definitions)
                        .FirstOrDefault(definition => definition.Type == selType)
                        ?.Presets.FirstOrDefault()?.DefaultParams;
                    if (defaults != null)
                        seed = defaults.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
                }
                activeRecipeEditor = BuildRecipeParameterEditor(selType, seed);
                activeRecipeType = selType;
                recipeFields.Children.Clear();
                recipeFields.Children.Add(activeRecipeEditor.Content);
                AttachSectionPreviewTriggers(activeRecipeEditor.Content, ScheduleSectionPreview);
            }
            previewBox.Visibility = selType == "collection" ? Visibility.Collapsed : Visibility.Visible;
        }
        typeCombo.SelectionChanged += (_, _) => { UpdateConditionalFields(); ScheduleSectionPreview(); };
        collectionPicker.SelectionChanged += (_, _) =>
        {
            var selectedType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (selectedType == "collection")
                SaveSectionEditorButton.IsEnabled = collectionPicker.SelectedItem != null;
            ScheduleSectionPreview();
        };
        itemLimitBox.ValueChanged += (_, _) => ScheduleSectionPreview();
        mediaScopeCombo.SelectionChanged += (_, _) => ScheduleSectionPreview();
        continueTypeCombo.SelectionChanged += (_, _) => ScheduleSectionPreview();
        genreInput.TextChanged += (_, _) => ScheduleSectionPreview();
        foreach (var entry in libraryCheckboxes) entry.Check.Click += (_, _) => ScheduleSectionPreview();
        UpdateConditionalFields();

        object? GetBody()
        {
            string sectionType = "recently_added";
            if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string t)
                sectionType = t;
            string title = titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title))
                title = ViewModel.RecipeLabels.GetValueOrDefault(
                    sectionType,
                    SectionTypeLabels.GetValueOrDefault(sectionType, sectionType.Replace('_', ' ')));

            int itemLimit = double.IsNaN(itemLimitBox.Value) ? 20 : (int)itemLimitBox.Value;
            bool isKnownRecipe = ViewModel.RecipeCatalog?.Categories.Values
                .SelectMany(definitions => definitions)
                .Any(definition => definition.Type == sectionType) == true;

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
                var selectedCollection = (collectionPicker.SelectedItem as ComboBoxItem)?.Tag as GalleryCollectionChoice;
                if (selectedCollection == null) return null;
                config = new()
                {
                    [selectedCollection.IsUserCollection ? "user_collection_id" : "library_collection_id"] = selectedCollection.Id,
                };
            }
            else if (filterTypes.Contains(sectionType) &&
                     activeFilterType == sectionType && activeFilterEditor != null)
            {
                config = activeFilterEditor.GetConfig();
                var scope = (mediaScopeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (string.IsNullOrWhiteSpace(scope)) config.Remove("media_scope");
                else config["media_scope"] = scope;
                var selectedLibraryIds = libraryCheckboxes
                    .Where(entry => entry.Check.IsChecked == true)
                    .Select(entry => (object)entry.LibId)
                    .ToList();
                if (selectedLibraryIds.Count == 0) config.Remove("library_ids");
                else config["library_ids"] = selectedLibraryIds;
            }
            else if (isKnownRecipe && activeRecipeType == sectionType && activeRecipeEditor != null)
            {
                config = activeRecipeEditor.GetConfig();
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

            return ViewModel.BuildCreateBody(title, sectionType, itemLimit, featuredSwitchSmall.IsOn, enabledSwitchSmall.IsOn, config);
        }

        ScheduleSectionPreview();
        return (form, GetBody);
    }

    private static List<int>? ExtractPreviewLibraryIds(IReadOnlyDictionary<string, object?> config)
    {
        if (!config.TryGetValue("library_ids", out var value) || value == null)
            return null;

        var result = new List<int>();

        static void AddIfValid(List<int> target, object? candidate)
        {
            if (candidate == null) return;
            if (candidate is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonNumber))
                    target.Add(jsonNumber);
                else if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var jsonText))
                    target.Add(jsonText);
                return;
            }

            if (candidate is int number)
            {
                target.Add(number);
                return;
            }

            if (int.TryParse(candidate.ToString(), out var parsed))
                target.Add(parsed);
        }

        if (value is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in json.EnumerateArray()) AddIfValid(result, entry);
        }
        else if (value is System.Collections.IEnumerable entries && value is not string)
        {
            foreach (var entry in entries) AddIfValid(result, entry);
        }
        else
        {
            AddIfValid(result, value);
        }

        return result.Count == 0 ? null : result.Distinct().ToList();
    }

    private static void AttachSectionPreviewTriggers(FrameworkElement root, Action trigger)
    {
        switch (root)
        {
            case TextBox textBox:
                textBox.TextChanged += (_, _) => trigger();
                break;
            case ComboBox comboBox:
                comboBox.SelectionChanged += (_, _) => trigger();
                break;
            case NumberBox numberBox:
                numberBox.ValueChanged += (_, _) => trigger();
                break;
            case CheckBox checkBox:
                checkBox.Click += (_, _) => trigger();
                break;
            case ToggleSwitch toggleSwitch:
                toggleSwitch.Toggled += (_, _) => trigger();
                break;
        }

        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<FrameworkElement>())
                    AttachSectionPreviewTriggers(child, trigger);
                break;
            case Border border when border.Child is FrameworkElement child:
                AttachSectionPreviewTriggers(child, trigger);
                break;
            case ScrollViewer scrollViewer when scrollViewer.Content is FrameworkElement child:
                AttachSectionPreviewTriggers(child, trigger);
                break;
            case ContentControl contentControl when contentControl.Content is FrameworkElement child:
                AttachSectionPreviewTriggers(child, trigger);
                break;
        }
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
