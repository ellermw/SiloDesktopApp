using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Views;
using ContinuumPlayer.ViewModels;
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
            ViewModel.CollectionGroups.CollectionChanged += (_, _) => ScheduleRebuild();
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
        CreateGroupButton.Visibility = ViewModel.SelectedLibraryId.HasValue ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPanel.Children.Clear();

        if (ViewModel.Collections.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        if (ViewModel.SelectedLibraryId.HasValue)
        {
            BuildCollectionGroupBoard();
            return;
        }

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

    private void BuildCollectionGroupBoard()
    {
        var groupedCollections = ViewModel.CollectionGroups
            .OrderBy(g => g.SortOrder)
            .Select(group => (Group: group, Items: ViewModel.Collections
                .Where(c => c.GroupId == group.Id)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Title)
                .ToList()))
            .ToList();

        foreach (var (group, items) in groupedCollections)
        {
            CollectionsPanel.Children.Add(BuildGroupHeader(group.Name, group));
            if (items.Count == 0)
            {
                CollectionsPanel.Children.Add(BuildEmptyGroupRow("No collections in this group."));
            }
            else
            {
                foreach (var collection in items)
                    CollectionsPanel.Children.Add(BuildCollectionRow(collection));
            }
        }

        var ungrouped = ViewModel.Collections
            .Where(c => string.IsNullOrEmpty(c.GroupId))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Title)
            .ToList();

        CollectionsPanel.Children.Add(BuildGroupHeader("Ungrouped", null));
        if (ungrouped.Count == 0)
        {
            CollectionsPanel.Children.Add(BuildEmptyGroupRow("No ungrouped collections."));
        }
        else
        {
            foreach (var collection in ungrouped)
                CollectionsPanel.Children.Add(BuildCollectionRow(collection));
        }
    }

    private FrameworkElement BuildGroupHeader(string title, LibraryCollectionGroup? group)
    {
        var header = new Grid
        {
            Padding = new Thickness(20, 16, 20, 12),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        if (group != null)
            titlePanel.Children.Add(MakeBadgeSecondary(group.DefaultSortMode));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (group != null)
        {
            var up = MakeIconButton("\uE70E", "Move group up");
            var down = MakeIconButton("\uE70D", "Move group down");
            var edit = MakeIconButton("\uE70F", "Rename group");
            var delete = MakeIconButton("\uE74D", "Delete group");

            up.Click += async (_, _) => await MoveGroupAsync(group, -1);
            down.Click += async (_, _) => await MoveGroupAsync(group, 1);
            edit.Click += async (_, _) => await OpenEditGroupDialogAsync(group);
            delete.Click += async (_, _) => await OpenDeleteGroupDialogAsync(group);

            actions.Children.Add(up);
            actions.Children.Add(down);
            actions.Children.Add(edit);
            actions.Children.Add(delete);
        }

        Grid.SetColumn(titlePanel, 0);
        Grid.SetColumn(actions, 1);
        header.Children.Add(titlePanel);
        header.Children.Add(actions);
        return header;
    }

    private static FrameworkElement BuildEmptyGroupRow(string text)
    {
        return new TextBlock
        {
            Text = text,
            Padding = new Thickness(20, 14, 20, 14),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
        };
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

        if (ViewModel.SelectedLibraryId.HasValue)
        {
            var upBtn = MakeIconButton("\uE70E", "Move collection up");
            var downBtn = MakeIconButton("\uE70D", "Move collection down");
            upBtn.Click += async (_, _) => await MoveCollectionInGroupAsync(capturedCol, -1);
            downBtn.Click += async (_, _) => await MoveCollectionInGroupAsync(capturedCol, 1);
            actionsPanel.Children.Add(upBtn);
            actionsPanel.Children.Add(downBtn);
        }
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

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        return row;
    }

    // ===== Header Button =====

    private async void AddCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    private void SmartCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<SmartCollectionWizardPage>(
            new SmartCollectionWizardNavigationArgs(IsAdmin: true, LibraryId: ViewModel.SelectedLibraryId));
    }

    private async void CreateGroupButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateGroupDialogAsync();
    }

    private async Task OpenCreateGroupDialogAsync()
    {
        if (!ViewModel.SelectedLibraryId.HasValue)
        {
            ViewModel.ErrorMessage = "Select a library before creating a collection group.";
            return;
        }

        var nameBox = new TextBox
        {
            PlaceholderText = "Group name",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            Width = 360
        };

        var dialog = new ContentDialog
        {
            Title = "New Collection Group",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = nameBox,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            try { await ViewModel.CreateGroupAsync(nameBox.Text); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async Task OpenEditGroupDialogAsync(LibraryCollectionGroup group)
    {
        var nameBox = new TextBox
        {
            Text = group.Name,
            PlaceholderText = "Group name",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
            Width = 360
        };

        var dialog = new ContentDialog
        {
            Title = "Rename Collection Group",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = nameBox,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            try { await ViewModel.UpdateGroupAsync(group, nameBox.Text); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async Task OpenDeleteGroupDialogAsync(LibraryCollectionGroup group)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Collection Group",
            Content = $"Delete group \"{group.Name}\"? Collections in the group will move back to Ungrouped.",
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try { await ViewModel.DeleteGroupAsync(group); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async Task MoveGroupAsync(LibraryCollectionGroup group, int delta)
    {
        try { await ViewModel.MoveGroupAsync(group, delta); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    private async Task MoveCollectionInGroupAsync(LibraryCollection collection, int delta)
    {
        try { await ViewModel.MoveCollectionInGroupAsync(collection, delta); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
    }

    // ===== Create Dialog =====

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody, getPosterFile, getBackdropFile) = BuildCollectionForm(null);

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
                var adminApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.AdminApi>();
                var created = await adminApi.CreateCollectionAsync(body);

                // Upload poster/backdrop files if selected
                await UploadCollectionImagesAsync(adminApi, created.Id, getPosterFile(), getBackdropFile());

                await ViewModel.LoadCommand.ExecuteAsync(null);
            }
            catch { }
        }
    }

    // ===== Edit Dialog =====

    private async Task OpenEditDialogAsync(LibraryCollection col)
    {
        var (formContent, getBody, getPosterFile, getBackdropFile) = BuildCollectionForm(col);

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
                var adminApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.AdminApi>();
                await adminApi.UpdateCollectionAsync(col.Id, body);

                // Upload poster/backdrop files if selected
                await UploadCollectionImagesAsync(adminApi, col.Id, getPosterFile(), getBackdropFile());

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
                await ViewModel.DeleteCollectionCommand.ExecuteAsync(col.Id);
            }
            catch { }
        }
    }

    // ===== Form Builder =====

    private (FrameworkElement Content, Func<ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest?> GetBody,
        Func<(byte[]? Bytes, string? Name, string? ContentType)> GetPosterFile,
        Func<(byte[]? Bytes, string? Name, string? ContentType)> GetBackdropFile) BuildCollectionForm(LibraryCollection? existing)
    {
        var titleBox = new TextBox
        {
            PlaceholderText = "Collection title",
            Text = existing?.Title ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };

        var descBox = new TextBox
        {
            PlaceholderText = "Description (optional)",
            Text = existing?.Description ?? "",
            AcceptsReturn = false,
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };

        var sourceUrlBox = new TextBox
        {
            PlaceholderText = "Source URL (MDBList / TMDB, optional)",
            Text = existing?.SourceUrl ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };

        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(6),
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
            CornerRadius = new CornerRadius(6),
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

        var form = new StackPanel { Width = 420, Spacing = 14 };

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

        AddField("Title", titleBox);
        AddField("Description", descBox);
        // Library multi-select (webui supports library_ids[] array)
        var libCheckPanel = new StackPanel { Spacing = 4 };
        var libCheckboxes = new List<(int LibId, CheckBox Check)>();
        var existingLibIds = existing?.LibraryIds ?? (existing?.LibraryId > 0 ? [existing.LibraryId] : []);
        foreach (var lib in ViewModel.Libraries)
        {
            var cb = new CheckBox
            {
                Content = lib.Name, FontSize = 12,
                IsChecked = existingLibIds.Contains(lib.Id),
            };
            libCheckPanel.Children.Add(cb);
            libCheckboxes.Add((lib.Id, cb));
        }
        // If no libraries checked and we have a pre-selected lib, check it
        if (libCheckboxes.All(x => x.Check.IsChecked != true) && ViewModel.SelectedLibraryId.HasValue)
        {
            var match = libCheckboxes.FirstOrDefault(x => x.LibId == ViewModel.SelectedLibraryId.Value);
            if (match.Check != null) match.Check.IsChecked = true;
        }
        AddField("Libraries", libCheckPanel);

        ComboBox? groupCombo = null;
        if (ViewModel.SelectedLibraryId.HasValue && ViewModel.CollectionGroups.Count > 0)
        {
            groupCombo = new ComboBox
            {
                CornerRadius = new CornerRadius(6),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            groupCombo.Items.Add(new ComboBoxItem { Content = "Ungrouped", Tag = null });
            foreach (var group in ViewModel.CollectionGroups.OrderBy(g => g.SortOrder))
                groupCombo.Items.Add(new ComboBoxItem { Content = group.Name, Tag = group.Id });
            groupCombo.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(existing?.GroupId))
            {
                foreach (ComboBoxItem item in groupCombo.Items)
                {
                    if (item.Tag is string tag && tag == existing.GroupId)
                    {
                        groupCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            AddField("Group", groupCombo);
        }

        AddField("Type", typeCombo);
        AddField("Visibility", visibilityCombo);
        AddField("Source URL", sourceUrlBox);

        // Sync Schedule (webui: SyncScheduleField — dropdown with common intervals)
        var syncCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(6), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        syncCombo.Items.Add(new ComboBoxItem { Content = "None (manual only)", Tag = "" });
        syncCombo.Items.Add(new ComboBoxItem { Content = "Every hour", Tag = "0 * * * *" });
        syncCombo.Items.Add(new ComboBoxItem { Content = "Every 6 hours", Tag = "0 */6 * * *" });
        syncCombo.Items.Add(new ComboBoxItem { Content = "Every 12 hours", Tag = "0 */12 * * *" });
        syncCombo.Items.Add(new ComboBoxItem { Content = "Daily", Tag = "0 0 * * *" });
        syncCombo.Items.Add(new ComboBoxItem { Content = "Weekly", Tag = "0 0 * * 0" });
        syncCombo.SelectedIndex = 0;
        if (existing?.SyncSchedule != null)
        {
            bool found = false;
            for (int i = 0; i < syncCombo.Items.Count; i++)
            {
                if (syncCombo.Items[i] is ComboBoxItem ci && (string)ci.Tag == existing.SyncSchedule)
                { syncCombo.SelectedIndex = i; found = true; break; }
            }
            if (!found && !string.IsNullOrEmpty(existing.SyncSchedule))
            {
                // Custom cron — add as-is
                syncCombo.Items.Add(new ComboBoxItem { Content = $"Custom: {existing.SyncSchedule}", Tag = existing.SyncSchedule });
                syncCombo.SelectedIndex = syncCombo.Items.Count - 1;
            }
        }
        AddField("Sync Schedule", syncCombo);

        // === Source Config: conditional fields for MDBList/TMDB types ===

        // MDBList config: limit
        var mdbLimitBox = new NumberBox
        {
            Value = 100, Minimum = 1, Maximum = 10000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var mdbConfigPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        mdbConfigPanel.Children.Add(new TextBlock { Text = "Item Limit", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        mdbConfigPanel.Children.Add(mdbLimitBox);
        form.Children.Add(mdbConfigPanel);

        // TMDB config: preset + media type
        var tmdbPresetCombo = new ComboBox { CornerRadius = new CornerRadius(6), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (val, label) in new[] {
            ("trending", "Trending"), ("popular", "Popular"), ("top_rated", "Top Rated"),
            ("now_playing", "Now Playing"), ("upcoming", "Upcoming"), ("airing_today", "Airing Today"),
            ("on_the_air", "On The Air") })
            tmdbPresetCombo.Items.Add(new ComboBoxItem { Content = label, Tag = val });
        tmdbPresetCombo.SelectedIndex = 0;

        var tmdbMediaCombo = new ComboBox { CornerRadius = new CornerRadius(6), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        tmdbMediaCombo.Items.Add(new ComboBoxItem { Content = "Movies", Tag = "movie" });
        tmdbMediaCombo.Items.Add(new ComboBoxItem { Content = "TV Shows", Tag = "tv" });
        tmdbMediaCombo.SelectedIndex = 0;

        var tmdbConfigPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        var tmdbPresetField = new StackPanel { Spacing = 4 };
        tmdbPresetField.Children.Add(new TextBlock { Text = "Preset", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        tmdbPresetField.Children.Add(tmdbPresetCombo);
        tmdbConfigPanel.Children.Add(tmdbPresetField);
        var tmdbMediaField = new StackPanel { Spacing = 4 };
        tmdbMediaField.Children.Add(new TextBlock { Text = "Media Type", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        tmdbMediaField.Children.Add(tmdbMediaCombo);
        tmdbConfigPanel.Children.Add(tmdbMediaField);
        form.Children.Add(tmdbConfigPanel);

        // Pre-populate from existing source config
        if (existing?.SourceConfig != null)
        {
            string GetConfigStr(string key)
            {
                if (existing.SourceConfig.TryGetValue(key, out var v))
                {
                    if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String) return je.GetString() ?? "";
                    return v?.ToString() ?? "";
                }
                return "";
            }
            int GetConfigInt(string key, int def)
            {
                if (existing.SourceConfig.TryGetValue(key, out var v))
                {
                    if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.GetInt32();
                    if (v is int i) return i;
                }
                return def;
            }

            if (existing.CollectionType == "mdblist")
                mdbLimitBox.Value = GetConfigInt("limit", 100);

            if (existing.CollectionType == "tmdb")
            {
                var preset = GetConfigStr("preset");
                for (int i = 0; i < tmdbPresetCombo.Items.Count; i++)
                    if (tmdbPresetCombo.Items[i] is ComboBoxItem ci && (string)ci.Tag == preset) { tmdbPresetCombo.SelectedIndex = i; break; }
                var media = GetConfigStr("media_type");
                for (int i = 0; i < tmdbMediaCombo.Items.Count; i++)
                    if (tmdbMediaCombo.Items[i] is ComboBoxItem ci && (string)ci.Tag == media) { tmdbMediaCombo.SelectedIndex = i; break; }
            }
        }

        // Toggle visibility based on type selection
        void UpdateSourceConfigVisibility()
        {
            var selType = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            mdbConfigPanel.Visibility = selType == "mdblist" ? Visibility.Visible : Visibility.Collapsed;
            tmdbConfigPanel.Visibility = selType == "tmdb" ? Visibility.Visible : Visibility.Collapsed;
        }
        typeCombo.SelectionChanged += (_, _) => UpdateSourceConfigVisibility();
        UpdateSourceConfigVisibility();

        var featuredGroup = new StackPanel { Spacing = 6 };
        featuredGroup.Children.Add(new TextBlock
        {
            Text = "Featured",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        featuredGroup.Children.Add(featuredSwitch);
        form.Children.Add(featuredGroup);

        // === Image fields (poster + backdrop source URLs) ===
        // Webui: ImageUploadField — source URL input + preview. The server
        // fetches and stores the image when a source URL is provided on create/update.

        var posterSourceUrlBox = new TextBox
        {
            PlaceholderText = "https://image.tmdb.org/t/p/w500/...",
            Text = existing?.PosterUrl ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };
        // Show current poster preview when editing
        var posterPreview = new StackPanel { Spacing = 4 };
        if (existing != null && !string.IsNullOrEmpty(existing.PosterUrl))
        {
            try
            {
                var previewImg = new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(existing.PosterUrl)),
                    MaxHeight = 80,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                };
                var previewBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Child = previewImg,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                posterPreview.Children.Add(previewBorder);
            }
            catch { }
        }
        posterPreview.Children.Add(posterSourceUrlBox);
        // File picker button for local upload (after save)
        byte[]? posterFileBytes = null;
        string? posterFileName = null;
        string? posterContentType = null;
        var posterPickBtn = new Button
        {
            Content = "Browse...",
            FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var posterPickStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        posterPickBtn.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
                posterFileBytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buf);
                posterFileName = file.Name;
                posterContentType = file.ContentType;
                posterPickStatus.Text = file.Name;
                posterSourceUrlBox.Text = ""; // Clear URL when file selected
            }
        };
        var posterPickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        posterPickRow.Children.Add(posterPickBtn);
        posterPickRow.Children.Add(posterPickStatus);
        posterPreview.Children.Add(posterPickRow);
        AddField("Poster Image", posterPreview);

        var backdropSourceUrlBox = new TextBox
        {
            PlaceholderText = "https://image.tmdb.org/t/p/original/...",
            Text = existing?.BackdropUrl ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };
        var backdropPreview = new StackPanel { Spacing = 4 };
        if (existing != null && !string.IsNullOrEmpty(existing.BackdropUrl))
        {
            try
            {
                var previewImg = new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(existing.BackdropUrl)),
                    MaxHeight = 60,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                };
                var previewBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Child = previewImg,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                backdropPreview.Children.Add(previewBorder);
            }
            catch { }
        }
        backdropPreview.Children.Add(backdropSourceUrlBox);
        byte[]? backdropFileBytes = null;
        string? backdropFileName = null;
        string? backdropContentType = null;
        var backdropPickBtn = new Button
        {
            Content = "Browse...",
            FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var backdropPickStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        backdropPickBtn.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
                backdropFileBytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buf);
                backdropFileName = file.Name;
                backdropContentType = file.ContentType;
                backdropPickStatus.Text = file.Name;
                backdropSourceUrlBox.Text = "";
            }
        };
        var backdropPickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        backdropPickRow.Children.Add(backdropPickBtn);
        backdropPickRow.Children.Add(backdropPickStatus);
        backdropPreview.Children.Add(backdropPickRow);
        AddField("Backdrop Image", backdropPreview);

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

            // Library IDs from multi-select checkboxes
            var selectedLibIds = libCheckboxes
                .Where(x => x.Check.IsChecked == true)
                .Select(x => x.LibId)
                .ToList();
            int? libraryId = selectedLibIds.Count > 0 ? selectedLibIds[0] : null;

            string? syncSchedule = null;
            if (syncCombo.SelectedItem is ComboBoxItem syncItem && syncItem.Tag is string sched && !string.IsNullOrEmpty(sched))
                syncSchedule = sched;

            string? groupId = null;
            if (groupCombo?.SelectedItem is ComboBoxItem groupItem && groupItem.Tag is string selectedGroupId)
                groupId = selectedGroupId;

            // Build source config based on type
            Dictionary<string, object>? sourceConfig = null;
            if (type == "mdblist" && !double.IsNaN(mdbLimitBox.Value))
                sourceConfig = new() { ["limit"] = (int)mdbLimitBox.Value };
            else if (type == "tmdb")
            {
                var preset = (tmdbPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "trending";
                var mediaType = (tmdbMediaCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "movie";
                sourceConfig = new() { ["preset"] = preset, ["media_type"] = mediaType };
            }

            // Image source URLs (server will fetch these)
            string? posterSrcUrl = string.IsNullOrWhiteSpace(posterSourceUrlBox.Text) ? null : posterSourceUrlBox.Text.Trim();
            string? backdropSrcUrl = string.IsNullOrWhiteSpace(backdropSourceUrlBox.Text) ? null : backdropSourceUrlBox.Text.Trim();

            return new ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest
            {
                Title = title,
                Description = string.IsNullOrEmpty(descBox.Text) ? null : descBox.Text.Trim(),
                LibraryId = libraryId,
                LibraryIds = selectedLibIds.Count > 0 ? selectedLibIds : null,
                CollectionType = type,
                Visibility = visibility,
                GroupId = groupId,
                SourceUrl = string.IsNullOrEmpty(sourceUrlBox.Text) ? null : sourceUrlBox.Text.Trim(),
                Featured = featuredSwitch.IsOn,
                SyncSchedule = syncSchedule,
                SourceConfig = sourceConfig,
                PosterSourceUrl = posterSrcUrl,
                BackdropSourceUrl = backdropSrcUrl,
            };
        }

        // Expose file upload data for post-save upload
        (byte[]? Bytes, string? Name, string? ContentType) GetPosterFile() => (posterFileBytes, posterFileName, posterContentType);
        (byte[]? Bytes, string? Name, string? ContentType) GetBackdropFile() => (backdropFileBytes, backdropFileName, backdropContentType);

        return (form, GetBody, GetPosterFile, GetBackdropFile);
    }

    // ===== Image Upload Helper =====

    private static async Task UploadCollectionImagesAsync(
        ContinuumPlayer.Core.Api.AdminApi adminApi, string collectionId,
        (byte[]? Bytes, string? Name, string? ContentType) poster,
        (byte[]? Bytes, string? Name, string? ContentType) backdrop)
    {
        if (poster.Bytes != null && poster.Name != null && poster.ContentType != null)
            await adminApi.UploadCollectionImageAsync(collectionId, "poster", poster.Bytes, poster.Name, poster.ContentType);
        if (backdrop.Bytes != null && backdrop.Name != null && backdrop.ContentType != null)
            await adminApi.UploadCollectionImageAsync(collectionId, "backdrop", backdrop.Bytes, backdrop.Name, backdrop.ContentType);
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
