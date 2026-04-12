using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage : Page
{
    public AdminCollectionsViewModel ViewModel { get; }

    // Track whether the picker is being updated programmatically so we don't re-trigger a load
    private bool _suppressPickerChange;
    private bool _rebuildPending;

    public AdminCollectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminCollectionsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Collections.CollectionChanged += (_, _) => ScheduleRebuild();
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
            BuildCollectionRows();
        });
    }

    // ===== Library Picker =====

    private void PopulateLibraryPicker()
    {
        _suppressPickerChange = true;

        LibraryPicker.Items.Clear();
        LibraryPicker.Items.Add(new ComboBoxItem { Content = "All Libraries", Tag = (int?)null });

        foreach (var lib in ViewModel.Libraries)
            LibraryPicker.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = (int?)lib.Id });

        LibraryPicker.SelectedIndex = 0;
        _suppressPickerChange = false;
    }

    private async void LibraryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPickerChange) return;
        if (LibraryPicker.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedLibraryId = item.Tag as int?;
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    // ===== Table Builder =====

    private void BuildCollectionRows()
    {
        CollectionsPanel.Children.Clear();

        if (ViewModel.Collections.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var col in ViewModel.Collections)
        {
            if (!isFirst)
            {
                CollectionsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            CollectionsPanel.Children.Add(BuildCollectionRow(col));
        }
    }

    private FrameworkElement BuildCollectionRow(LibraryCollection col)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // ---- Title column ----
        var titlePanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(new TextBlock
        {
            Text = col.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Featured badge (default/accent style)
        if (col.Featured)
        {
            titleRow.Children.Add(MakeBadgeDefault("Featured"));
        }

        // Visibility badge (secondary/neutral style)
        titleRow.Children.Add(MakeBadgeSecondary(col.Visibility));

        titlePanel.Children.Add(titleRow);

        titlePanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(col.Description) ? "No summary provided." : col.Description,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 300
        });

        // ---- Source column ----
        var sourcePanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        sourcePanel.Children.Add(MakeBadgeOutline(col.CollectionType));

        if (!string.IsNullOrEmpty(col.SourceUrl))
        {
            sourcePanel.Children.Add(new TextBlock
            {
                Text = col.SourceUrl,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 220
            });
        }
        else
        {
            sourcePanel.Children.Add(new TextBlock
            {
                Text = "Local metadata",
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }

        // ---- Items column ----
        string itemsText = col.CollectionType == "smart" ? "\u2014" : col.ItemCount.ToString();
        var itemsBlock = new TextBlock
        {
            Text = itemsText,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Sync Status column ----
        var syncPanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        syncPanel.Children.Add(MakeBadgeOutline(col.LastSyncStatus));

        string syncMsg = string.IsNullOrEmpty(col.LastSyncMessage) ? "Not synced yet" : col.LastSyncMessage;
        syncPanel.Children.Add(new TextBlock
        {
            Text = syncMsg,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220
        });

        // ---- Schedule column ----
        var schedulePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(col.SyncSchedule))
        {
            schedulePanel.Children.Add(new TextBlock
            {
                Text = col.SyncSchedule,
                FontSize = 12,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!string.IsNullOrEmpty(col.NextSyncAt) && DateTime.TryParse(col.NextSyncAt, out var nextDt))
            {
                schedulePanel.Children.Add(new TextBlock
                {
                    Text = $"Next: {nextDt.ToLocalTime():g}",
                    FontSize = 11,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
        }
        else
        {
            schedulePanel.Children.Add(new TextBlock
            {
                Text = "\u2014",
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }

        // ---- Updated column ----
        string updatedText = "";
        if (DateTime.TryParse(col.UpdatedAt, out var updatedDt))
            updatedText = updatedDt.ToLocalTime().ToString("g");
        else
            updatedText = col.UpdatedAt;

        var updatedBlock = new TextBlock
        {
            Text = updatedText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        // ---- Actions column ----
        // 3 ghost icon buttons: Sync (RefreshCw), Edit (Pencil), Delete (Trash2)
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var syncBtn = MakeIconButton("\uE72C", "Sync collection");
        var editBtn = MakeIconButton("\uE70F", "Edit collection");
        var deleteBtn = MakeIconButton("\uE74D", "Delete collection");

        var capturedCol = col;
        syncBtn.Click += async (_, _) =>
        {
            syncBtn.IsEnabled = false;
            try
            {
                await ViewModel.SyncCollectionCommand.ExecuteAsync(capturedCol.Id);
            }
            finally { syncBtn.IsEnabled = true; }
        };
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedCol);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedCol);

        actionsPanel.Children.Add(syncBtn);
        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(titlePanel, 0);
        Grid.SetColumn(sourcePanel, 1);
        Grid.SetColumn(itemsBlock, 2);
        Grid.SetColumn(syncPanel, 3);
        Grid.SetColumn(schedulePanel, 4);
        Grid.SetColumn(updatedBlock, 5);
        Grid.SetColumn(actionsPanel, 6);

        row.Children.Add(titlePanel);
        row.Children.Add(sourcePanel);
        row.Children.Add(itemsBlock);
        row.Children.Add(syncPanel);
        row.Children.Add(schedulePanel);
        row.Children.Add(updatedBlock);
        row.Children.Add(actionsPanel);

        return row;
    }

    // ===== Header Button =====

    private async void AddCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    // ===== Create Dialog =====

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody) = BuildCollectionForm(null);

        var dialog = new ContentDialog
        {
            Title = "Add Collection",
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
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }
            catch { }
        }
    }

    // ===== Edit Dialog =====

    private async Task OpenEditDialogAsync(LibraryCollection col)
    {
        var (formContent, getBody) = BuildCollectionForm(col);

        var dialog = new ContentDialog
        {
            Title = "Edit Collection",
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
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }
            catch { }
        }
    }

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(LibraryCollection col)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Collection",
            Content = $"Delete collection \"{col.Title}\"? This action cannot be undone.",
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
                await ViewModel.DeleteCollectionCommand.ExecuteAsync(col.Id);
            }
            catch { }
        }
    }

    // ===== Form Builder =====

    private (FrameworkElement Content, Func<ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest?> GetBody) BuildCollectionForm(LibraryCollection? existing)
    {
        var titleBox = new TextBox
        {
            PlaceholderText = "Collection title",
            Text = existing?.Title ?? "",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var descBox = new TextBox
        {
            PlaceholderText = "Description (optional)",
            Text = existing?.Description ?? "",
            AcceptsReturn = false,
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var sourceUrlBox = new TextBox
        {
            PlaceholderText = "Source URL (MDBList / TMDB, optional)",
            Text = existing?.SourceUrl ?? "",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Manual", Tag = "manual" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Smart", Tag = "smart" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "MDBList", Tag = "mdblist" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "TMDB", Tag = "tmdb" });
        typeCombo.SelectedIndex = 0;
        if (existing != null)
        {
            foreach (ComboBoxItem item in typeCombo.Items)
                if (item.Tag is string t && t == existing.CollectionType) { typeCombo.SelectedItem = item; break; }
        }

        var visibilityCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        visibilityCombo.Items.Add(new ComboBoxItem { Content = "Visible", Tag = "visible" });
        visibilityCombo.Items.Add(new ComboBoxItem { Content = "Hidden", Tag = "hidden" });
        visibilityCombo.SelectedIndex = 0;
        if (existing != null)
        {
            foreach (ComboBoxItem item in visibilityCombo.Items)
                if (item.Tag is string v && v == existing.Visibility) { visibilityCombo.SelectedItem = item; break; }
        }

        var featuredSwitch = new ToggleSwitch
        {
            IsOn = existing?.Featured ?? false,
            OnContent = "Featured",
            OffContent = "Not featured"
        };

        // Library selector from loaded libraries
        var libCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        libCombo.Items.Add(new ComboBoxItem { Content = "— None —", Tag = (int?)null });
        foreach (var lib in ViewModel.Libraries)
            libCombo.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = (int?)lib.Id });

        // Pre-select the collection's library or the currently filtered library
        int? preSelectLibId = existing?.LibraryId ?? ViewModel.SelectedLibraryId;
        if (preSelectLibId.HasValue)
        {
            foreach (ComboBoxItem item in libCombo.Items)
            {
                if (item.Tag is int id && id == preSelectLibId.Value)
                {
                    libCombo.SelectedItem = item;
                    break;
                }
            }
        }
        if (libCombo.SelectedItem == null) libCombo.SelectedIndex = 0;

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
        AddField("Description", descBox);
        AddField("Library", libCombo);
        AddField("Type", typeCombo);
        AddField("Visibility", visibilityCombo);
        AddField("Source URL", sourceUrlBox);

        var featuredGroup = new StackPanel { Spacing = 6 };
        featuredGroup.Children.Add(new TextBlock
        {
            Text = "Featured",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        featuredGroup.Children.Add(featuredSwitch);
        form.Children.Add(featuredGroup);

        ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest? GetBody()
        {
            string title = titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            string type = "manual";
            if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string t)
                type = t;

            string visibility = "visible";
            if (visibilityCombo.SelectedItem is ComboBoxItem visItem && visItem.Tag is string v)
                visibility = v;

            int? libraryId = null;
            if (libCombo.SelectedItem is ComboBoxItem libItem && libItem.Tag is int lid)
                libraryId = lid;

            return new ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest
            {
                Title = title,
                Description = string.IsNullOrEmpty(descBox.Text) ? null : descBox.Text.Trim(),
                LibraryId = libraryId,
                CollectionType = type,
                Visibility = visibility,
                SourceUrl = string.IsNullOrEmpty(sourceUrlBox.Text) ? null : sourceUrlBox.Text.Trim(),
                Featured = featuredSwitch.IsOn
            };
        }

        return (form, GetBody);
    }

    // ===== Badge Helpers =====

    /// <summary>Default/accent badge — used for Featured.</summary>
    private static Border MakeBadgeDefault(string text)
    {
        var bg = Color.FromArgb(40, 99, 102, 241);   // indigo-ish accent tint
        var fg = Color.FromArgb(255, 139, 142, 255);
        var border = Color.FromArgb(80, 99, 102, 241);
        return MakeBadgeColored(text, bg, fg, border);
    }

    /// <summary>Secondary/neutral badge — used for visibility.</summary>
    private static Border MakeBadgeSecondary(string text)
    {
        var bg = Color.FromArgb(30, 130, 130, 130);
        var fg = Color.FromArgb(255, 160, 160, 170);
        var border = Color.FromArgb(60, 130, 130, 130);
        return MakeBadgeColored(text, bg, fg, border);
    }

    /// <summary>Outline badge — used for collection type and sync status.</summary>
    private static Border MakeBadgeOutline(string text)
    {
        var bg = Color.FromArgb(0, 0, 0, 0);         // transparent
        var fg = Color.FromArgb(255, 150, 150, 160);
        var border = Color.FromArgb(80, 150, 150, 160);
        return MakeBadgeColored(text, bg, fg, border);
    }

    private static Border MakeBadgeColored(string text, Color bg, Color fg, Color borderColor)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg)
        };
        return badge;
    }

    /// <summary>Ghost icon button, 28×28 — matches web h-7 w-7.</summary>
    private static Button MakeIconButton(string glyph, string tooltip)
    {
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
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }
}
