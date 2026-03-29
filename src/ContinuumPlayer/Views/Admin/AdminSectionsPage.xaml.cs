using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminSectionsPage : Page
{
    public AdminSectionsViewModel ViewModel { get; }

    private bool _suppressPickerChange;
    private string _currentScope = "home";
    private bool _rebuildPending;

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
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        // ---- Move up/down buttons (column 0) ----
        var movePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var capturedSection = section;
        var upBtn = MakeSmallIconButton("\uE70E", "Move up");
        var downBtn = MakeSmallIconButton("\uE70D", "Move down");
        upBtn.Click += async (_, _) => await ViewModel.MoveSectionAsync(capturedSection, -1);
        downBtn.Click += async (_, _) => await ViewModel.MoveSectionAsync(capturedSection, +1);
        movePanel.Children.Add(upBtn);
        movePanel.Children.Add(downBtn);

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
        // Badge 2+: media scope (Movies/Series) as outline
        // Badge 3+: library filter names as outline
        // Badge 4: collection name as outline (for collection type)
        var typePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var typeLabel = SectionTypeLabels.TryGetValue(section.SectionType, out var lbl) ? lbl : section.SectionType;
        typePanel.Children.Add(MakeSecondaryBadge(typeLabel));

        // Extract media_scope from Config
        string? mediaScope = GetConfigString(section, "media_scope");
        if (mediaScope == "movie")
            typePanel.Children.Add(MakeOutlineBadge("Movies"));
        else if (mediaScope == "series")
            typePanel.Children.Add(MakeOutlineBadge("Series"));

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
                // Show collection ID shortened or just indicate "Collection" badge
                typePanel.Children.Add(MakeOutlineBadge("Collection"));
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

        Grid.SetColumn(movePanel, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetColumn(typePanel, 2);
        Grid.SetColumn(itemsBlock, 3);
        Grid.SetColumn(featuredCell, 4);
        Grid.SetColumn(enabledBadge, 5);
        Grid.SetColumn(actionsPanel, 6);

        row.Children.Add(movePanel);
        row.Children.Add(titleBlock);
        row.Children.Add(typePanel);
        row.Children.Add(itemsBlock);
        row.Children.Add(featuredCell);
        row.Children.Add(enabledBadge);
        row.Children.Add(actionsPanel);

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

        var dialogContent = new StackPanel { Width = 380, Spacing = 16 };

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

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody) = BuildSectionForm(null);

        var dialog = new ContentDialog
        {
            Title = "Add Section",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var body = getBody();
            if (body == null) return;
            try
            {
                await ViewModel.CreateSectionCommand.ExecuteAsync(body);
                ShowStatus(ViewModel.StatusMessage ?? "Section created.");
            }
            catch { }
        }
    }

    // ===== Edit Dialog =====

    private async Task OpenEditDialogAsync(AdminSection section)
    {
        var (formContent, getBody) = BuildSectionForm(section);

        var dialog = new ContentDialog
        {
            Title = "Edit Section",
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var body = getBody();
            if (body == null) return;
            try
            {
                await ViewModel.UpdateSectionCommand.ExecuteAsync((section.Id, body));
                ShowStatus(ViewModel.StatusMessage ?? "Section updated.");
            }
            catch { }
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
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var kvp in SectionTypeLabels)
            typeCombo.Items.Add(new ComboBoxItem { Content = kvp.Value, Tag = kvp.Key });
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
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var featuredSwitch = new ToggleSwitch
        {
            IsOn = existing?.Featured ?? false,
            OnContent = "Featured (Hero Banner)",
            OffContent = "Not featured"
        };

        var enabledSwitch = new ToggleSwitch
        {
            IsOn = existing?.Enabled ?? true,
            OnContent = "Enabled",
            OffContent = "Disabled"
        };

        var form = new StackPanel { Width = 380, Spacing = 14 };

        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
            group.Children.Add(control);
            form.Children.Add(group);
        }

        AddField("Title", titleBox);
        AddField("Section Type", typeCombo);
        AddField("Item Limit", itemLimitBox);

        // Featured — label + switch in a row matching web UI "flex items-center justify-between"
        var featuredRow = new Grid();
        featuredRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        featuredRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        var featuredLabel = new TextBlock
        {
            Text = "Featured (Hero Banner)",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        var featuredSwitchSmall = new ToggleSwitch
        {
            IsOn = existing?.Featured ?? false,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(featuredLabel, 0);
        Grid.SetColumn(featuredSwitchSmall, 1);
        featuredRow.Children.Add(featuredLabel);
        featuredRow.Children.Add(featuredSwitchSmall);
        form.Children.Add(featuredRow);

        // Enabled — same layout
        var enabledRow = new Grid();
        enabledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enabledRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        var enabledLabel = new TextBlock
        {
            Text = "Enabled",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        var enabledSwitchSmall = new ToggleSwitch
        {
            IsOn = existing?.Enabled ?? true,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(enabledLabel, 0);
        Grid.SetColumn(enabledSwitchSmall, 1);
        enabledRow.Children.Add(enabledLabel);
        enabledRow.Children.Add(enabledSwitchSmall);
        form.Children.Add(enabledRow);

        object? GetBody()
        {
            string title = titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            string sectionType = "recently_added";
            if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string t)
                sectionType = t;

            int itemLimit = double.IsNaN(itemLimitBox.Value) ? 20 : (int)itemLimitBox.Value;

            return ViewModel.BuildCreateBody(title, sectionType, itemLimit, featuredSwitchSmall.IsOn, enabledSwitchSmall.IsOn);
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
