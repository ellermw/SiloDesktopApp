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

    // Section type human-readable labels
    private static readonly Dictionary<string, string> SectionTypeLabels = new()
    {
        { "trending",                        "Trending" },
        { "recently_added",                  "Recently Added" },
        { "continue_watching",               "Continue Watching" },
        { "next_up",                         "Next Up" },
        { "popular",                         "Popular" },
        { "genre",                           "Genre" },
        { "collection",                      "Collection" },
        { "recommendations_for_you",         "For You" },
        { "recommendations_because_watched", "Because You Watched" },
    };

    private static readonly string[] SectionTypeValues =
    [
        "trending",
        "recently_added",
        "continue_watching",
        "next_up",
        "popular",
        "genre",
        "collection",
        "recommendations_for_you",
        "recommendations_because_watched",
    ];

    public AdminSectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminSectionsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Sections.CollectionChanged += (_, _) => BuildSectionRows();
            SetScopeActive("home");
            await ViewModel.LoadCommand.ExecuteAsync(null);
            PopulateLibraryPicker();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
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
        // Trigger reload using the currently selected library
        if (LibraryPicker.SelectedItem is ComboBoxItem item && item.Tag is int libId)
        {
            ViewModel.SelectedLibraryId = libId;
        }
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    // ===== Library Picker =====

    private void PopulateLibraryPicker()
    {
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

        if (ViewModel.Sections.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        // ---- Drag handle column ----
        var dragHandle = new FontIcon
        {
            Glyph = "\uE700",
            FontSize = 14,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Title column ----
        var titleBlock = new TextBlock
        {
            Text = section.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // ---- Type badge column ----
        var typeLabel = SectionTypeLabels.TryGetValue(section.SectionType, out var lbl)
            ? lbl : section.SectionType;
        var typeBadge = MakeOutlineBadge(typeLabel);

        // ---- Items column ----
        var itemsBlock = new TextBlock
        {
            Text = section.ItemLimit.ToString(),
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Featured column ----
        var featuredColor = section.Featured
            ? Color.FromArgb(255, 255, 200, 0)
            : Color.FromArgb(80, 150, 150, 150);
        var featuredIcon = new FontIcon
        {
            Glyph = "\uE735",
            FontSize = 16,
            Foreground = new SolidColorBrush(featuredColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Enabled toggle ----
        var enabledSwitch = new ToggleSwitch
        {
            IsOn = section.Enabled,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        var capturedSection = section;
        enabledSwitch.Toggled += async (_, _) =>
        {
            try { await ViewModel.ToggleEnabledAsync(capturedSection); }
            catch { enabledSwitch.IsOn = capturedSection.Enabled; }
        };

        // ---- Actions column ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var editBtn = MakeIconButton("\uE70F", "Edit section");
        var deleteBtn = MakeIconButton("\uE74D", "Delete section", Color.FromArgb(255, 220, 90, 90));

        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedSection);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedSection);

        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(dragHandle, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetColumn(typeBadge, 2);
        Grid.SetColumn(itemsBlock, 3);
        Grid.SetColumn(featuredIcon, 4);
        Grid.SetColumn(enabledSwitch, 5);
        Grid.SetColumn(actionsPanel, 6);

        row.Children.Add(dragHandle);
        row.Children.Add(titleBlock);
        row.Children.Add(typeBadge);
        row.Children.Add(itemsBlock);
        row.Children.Add(featuredIcon);
        row.Children.Add(enabledSwitch);
        row.Children.Add(actionsPanel);

        return row;
    }

    // ===== Header Buttons =====

    private async void AddSectionButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    private async void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Restore Default Sections",
            Content = "Restore default sections? This will reset all custom sections for the current scope.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.RestoreDefaultsCommand.ExecuteAsync(null);
                ShowStatus(ViewModel.StatusMessage ?? "Default sections restored.");
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
            PrimaryButtonText = "Save",
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
            PrimaryButtonText = "Save",
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
            Title = "Delete Section",
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
        foreach (var type in SectionTypeValues)
        {
            var label = SectionTypeLabels.TryGetValue(type, out var l) ? l : type;
            typeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = type });
        }
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
            OnContent = "Featured",
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

        var featuredGroup = new StackPanel { Spacing = 6 };
        featuredGroup.Children.Add(new TextBlock
        {
            Text = "Featured (Hero Banner)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        featuredGroup.Children.Add(featuredSwitch);
        form.Children.Add(featuredGroup);

        var enabledGroup = new StackPanel { Spacing = 6 };
        enabledGroup.Children.Add(new TextBlock
        {
            Text = "Enabled",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        enabledGroup.Children.Add(enabledSwitch);
        form.Children.Add(enabledGroup);

        object? GetBody()
        {
            string title = titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            string sectionType = "recently_added";
            if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string t)
                sectionType = t;

            int itemLimit = double.IsNaN(itemLimitBox.Value) ? 20 : (int)itemLimitBox.Value;

            return ViewModel.BuildCreateBody(title, sectionType, itemLimit, featuredSwitch.IsOn, enabledSwitch.IsOn);
        }

        return (form, GetBody);
    }

    // ===== Helpers =====

    private static Border MakeOutlineBadge(string text)
    {
        var badge = new Border
        {
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
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

    private static Button MakeIconButton(string glyph, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = fg
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
