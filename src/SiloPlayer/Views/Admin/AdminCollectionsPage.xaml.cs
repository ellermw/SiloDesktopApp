using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage : Page
{
    public AdminCollectionsViewModel ViewModel { get; }

    // Track whether the picker is being updated programmatically so we don't re-trigger a load
    private bool _suppressPickerChange;
    private bool _rebuildPending;
    private LibraryCollection? _editingCollection;
    private Func<CreateLibraryCollectionRequest?>? _editorGetBody;
    private Func<(byte[]? Bytes, string? Name, string? ContentType)>? _editorGetPosterFile;
    private Func<(byte[]? Bytes, string? Name, string? ContentType)>? _editorGetBackdropFile;
    private readonly Dictionary<string, string> _groupViewModes = [];
    private readonly HashSet<string> _selectedCollectionIds = new(StringComparer.Ordinal);
    private string? _selectionAnchorId;
    private string? _selectionSectionId;
    private bool _selectionIsUserCollection;

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
            BuildLoadingSkeletons();
            await ViewModel.LoadCommand.ExecuteAsync(null);
            PopulateLibraryPicker();
            BuildCollectionRows();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void ScheduleRebuild()
    {
        if (ViewModel.IsLoading) return;
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
        LibraryPicker.Items.Add(new ComboBoxItem
        {
            Content = $"All libraries ({ViewModel.Collections.Count})",
            Tag = (int?)null
        });

        foreach (var lib in ViewModel.Libraries)
        {
            var count = ViewModel.Collections.Count(c => CollectionLibraryIds(c).Contains(lib.Id));
            LibraryPicker.Items.Add(new ComboBoxItem { Content = $"{lib.Name} ({count})", Tag = (int?)lib.Id });
        }

        LibraryPicker.SelectedIndex = 0;
        _suppressPickerChange = false;
    }

    private async void LibraryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPickerChange) return;
        if (LibraryPicker.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedLibraryId = item.Tag as int?;
            ClearCollectionSelection();
            BuildLoadingSkeletons();
            await ViewModel.LoadCommand.ExecuteAsync(null);
            BuildCollectionRows();
        }
    }

    private void BuildLoadingSkeletons()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        CollectionsPanel.Children.Clear();
        for (var index = 0; index < 5; index++)
        {
            CollectionsPanel.Children.Add(new Border
            {
                Height = index == 0 ? 40 : 52,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                Opacity = index == 0 ? 0.72 : 0.5,
            });
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

        foreach (var library in ViewModel.Libraries)
        {
            var collections = ViewModel.Collections
                .Where(c => CollectionLibraryIds(c).Contains(library.Id))
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Title)
                .ToList();
            if (collections.Count == 0) continue;

            CollectionsPanel.Children.Add(BuildCollectionSection(
                library.Name,
                $"{collections.Count} collection{(collections.Count == 1 ? "" : "s")}",
                collections,
                showReorder: false));
        }
    }

    private void BuildCollectionGroupBoard()
    {
        var ungrouped = ViewModel.Collections
            .Where(c => string.IsNullOrEmpty(c.GroupId))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Title)
            .ToList();

        var sections = ViewModel.CollectionGroups
            .Select(group => (Id: group.Id, Order: group.SortOrder, Group: (LibraryCollectionGroup?)group))
            .Append((Id: "ungrouped", Order: ViewModel.UngroupedSortOrder, Group: (LibraryCollectionGroup?)null))
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Group?.Name ?? "\uffff", StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections)
        {
            if (section.Group == null)
            {
                CollectionsPanel.Children.Add(BuildCollectionSection(
                    "Ungrouped",
                    null,
                    ungrouped,
                    showReorder: true,
                    group: null,
                    sectionId: "ungrouped",
                    collectionDragEnabled: true));
                continue;
            }

            var group = section.Group;
            var viewMode = _groupViewModes.TryGetValue(group.Id, out var currentMode)
                ? currentMode
                : group.DefaultSortMode;
            var items = ApplyCollectionViewSort(
                ViewModel.Collections.Where(collection => collection.GroupId == group.Id),
                viewMode);
            CollectionsPanel.Children.Add(BuildCollectionSection(
                group.Name,
                null,
                items,
                showReorder: true,
                group,
                group.Id,
                collectionDragEnabled: string.Equals(viewMode, "manual", StringComparison.Ordinal)));
        }
    }

    private static List<LibraryCollection> ApplyCollectionViewSort(
        IEnumerable<LibraryCollection> collections,
        string mode)
    {
        return mode switch
        {
            "name_asc" => collections.OrderBy(collection => collection.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            "name_desc" => collections.OrderByDescending(collection => collection.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            "recent" => collections.OrderByDescending(collection => collection.UpdatedAt, StringComparer.Ordinal).ToList(),
            "most_items" => collections.OrderByDescending(collection => collection.ItemCount).ThenBy(collection => collection.Title).ToList(),
            _ => collections.OrderBy(collection => collection.SortOrder).ThenBy(collection => collection.Title).ToList(),
        };
    }

    private FrameworkElement BuildCollectionSection(
        string title,
        string? badge,
        IReadOnlyList<LibraryCollection> collections,
        bool showReorder,
        LibraryCollectionGroup? group = null,
        string? sectionId = null,
        bool collectionDragEnabled = true)
    {
        var content = new StackPanel { Spacing = 0 };
        content.Children.Add(BuildGroupHeader(title, group, badge, showReorder, sectionId));
        var rows = new StackPanel
        {
            Spacing = showReorder ? 8 : 0,
            Padding = showReorder ? new Thickness(12) : new Thickness(0)
        };
        if (collections.Count == 0)
        {
            var emptyText = sectionId == "ungrouped"
                ? "Drop collections here to remove them from any group. They'll appear on the library tab at this section's position."
                : string.Equals(group?.Kind, "user_collections", StringComparison.OrdinalIgnoreCase)
                    ? "Reserved slot for user-published collections. Drag this group to set where they'd appear on the library tab."
                    : "Drag a collection here, or add one with + New collection.";
            rows.Children.Add(BuildEmptyGroupRow(emptyText, showReorder));
        }
        else
        {
            for (var index = 0; index < collections.Count; index++)
            {
                if (!showReorder && index > 0)
                    rows.Children.Add(new Border
                    {
                        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                        BorderThickness = new Thickness(0, 1, 0, 0)
                    });
                rows.Children.Add(BuildCompactCollectionRow(
                    collections[index],
                    showReorder,
                    sectionId,
                    collections,
                    collectionDragEnabled,
                    string.Equals(group?.Kind, "user_collections", StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (showReorder && sectionId != null)
        {
            rows.AllowDrop = true;
            rows.DragOver += (_, args) =>
            {
                if (args.DataView.Contains(StandardDataFormats.Text))
                    args.AcceptedOperation = DataPackageOperation.Move;
            };
            rows.Drop += async (_, args) =>
            {
                if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                var token = await args.DataView.GetTextAsync();
                var draggedIds = ParseDraggedCollectionIds(token);
                if (draggedIds.Count > 0)
                {
                    args.Handled = true;
                    await ViewModel.MoveCollectionsToAsync(draggedIds, sectionId);
                    ClearCollectionSelection();
                    BuildCollectionRows();
                }
            };
        }
        content.Children.Add(rows);

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
    }

    private FrameworkElement BuildGroupHeader(
        string title,
        LibraryCollectionGroup? group,
        string? badgeText,
        bool showReorder,
        string? sectionId)
    {
        var header = new Grid
        {
            Padding = new Thickness(16, 12, 16, 12),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        if (showReorder && string.Equals(group?.Kind, "user_collections", StringComparison.OrdinalIgnoreCase))
        {
            var userBadge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            userBadge.Children.Add(new FontIcon { Glyph = "\uE716", FontSize = 11 });
            userBadge.Children.Add(new TextBlock { Text = "User Collections", FontSize = 11 });
            titlePanel.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 2, 7, 2),
                Child = userBadge
            });
        }

        FrameworkElement? dragGrip = null;
        if (showReorder && sectionId != null)
        {
            dragGrip = new TextBlock
            {
                Text = "\u22ee\u22ee",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                CanDrag = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            ToolTipService.SetToolTip(dragGrip, $"Drag {title}");
            var capturedSectionId = sectionId;
            dragGrip.DragStarting += (_, args) =>
            {
                args.Data.SetText($"section:{capturedSectionId}");
                args.Data.RequestedOperation = DataPackageOperation.Move;
            };
            header.AllowDrop = true;
            header.DragOver += (_, args) =>
            {
                if (args.DataView.Contains(StandardDataFormats.Text))
                    args.AcceptedOperation = DataPackageOperation.Move;
            };
            header.Drop += async (_, args) =>
            {
                if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                var token = await args.DataView.GetTextAsync();
                if (token.StartsWith("section:", StringComparison.Ordinal))
                {
                    args.Handled = true;
                    await ViewModel.MoveGroupSectionToAsync(token[8..], capturedSectionId);
                }
            };
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (!showReorder && !string.IsNullOrWhiteSpace(badgeText))
        {
            actions.Children.Add(MakeBadgeOutline(badgeText));
        }
        else if (showReorder && group != null)
        {
            actions.Children.Add(new TextBlock
            {
                Text = "View:",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            var viewCombo = new ComboBox { Width = 155, FontSize = 12, CornerRadius = new CornerRadius(6) };
            foreach (var (value, label) in new[]
            {
                ("manual", "Manual"),
                ("name_asc", "Name A\u2013Z"),
                ("name_desc", "Name Z\u2013A"),
                ("recent", "Recently Updated"),
                ("most_items", "Most Items")
            })
                viewCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            var selectedMode = _groupViewModes.TryGetValue(group.Id, out var mode) ? mode : group.DefaultSortMode;
            viewCombo.SelectedItem = viewCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, selectedMode, StringComparison.Ordinal))
                ?? viewCombo.Items[0];
            var capturedGroup = group;
            viewCombo.SelectionChanged += (_, _) =>
            {
                if (viewCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string nextMode) return;
                if (_groupViewModes.TryGetValue(capturedGroup.Id, out var existing) && existing == nextMode) return;
                _groupViewModes[capturedGroup.Id] = nextMode;
                ScheduleRebuild();
            };
            actions.Children.Add(viewCombo);
            var settings = MakeIconButton("\uE712", "Group settings");
            settings.Click += async (_, _) => await OpenEditGroupDialogAsync(capturedGroup);
            actions.Children.Add(settings);
        }

        if (dragGrip != null)
        {
            Grid.SetColumn(dragGrip, 0);
            header.Children.Add(dragGrip);
        }
        Grid.SetColumn(titlePanel, 1);
        Grid.SetColumn(actions, 2);
        header.Children.Add(titlePanel);
        header.Children.Add(actions);
        return header;
    }

    private static FrameworkElement BuildEmptyGroupRow(string text, bool cardStyle)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Padding = cardStyle ? new Thickness(20, 16, 20, 16) : new Thickness(20, 14, 20, 14),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextAlignment = cardStyle ? TextAlignment.Center : TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap
        };
        return cardStyle
            ? new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = textBlock
            }
            : textBlock;
    }

    private FrameworkElement BuildCompactCollectionRow(
        LibraryCollection col,
        bool showReorder,
        string? sectionId,
        IReadOnlyList<LibraryCollection> sectionCollections,
        bool collectionDragEnabled,
        bool isUserCollection)
    {
        var row = new Grid
        {
            Padding = showReorder ? new Thickness(8) : new Thickness(16, 12, 16, 12),
            ColumnSpacing = 12,
            Background = showReorder && _selectedCollectionIds.Contains(col.Id)
                ? new SolidColorBrush(Color.FromArgb(34, 99, 102, 241))
                : showReorder ? (Brush)Application.Current.Resources["CardBackgroundBrush"] : null,
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = showReorder ? new Thickness(1) : new Thickness(0),
            CornerRadius = showReorder ? new CornerRadius(6) : new CornerRadius(0)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = showReorder ? new GridLength(18) : new GridLength(0) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement? dragGrip = null;
        if (showReorder && sectionId != null)
        {
            dragGrip = new TextBlock
            {
                Text = "\u22ee\u22ee",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                CanDrag = collectionDragEnabled,
                Opacity = collectionDragEnabled ? 1 : 0.4
            };
            var capturedCollectionId = col.Id;
            dragGrip.DragStarting += (_, args) =>
            {
                var draggedIds = _selectedCollectionIds.Contains(capturedCollectionId)
                    ? ViewModel.Collections
                        .OrderBy(collection => collection.SortOrder)
                        .ThenBy(collection => collection.Title)
                        .Where(collection => _selectedCollectionIds.Contains(collection.Id))
                        .Select(collection => collection.Id)
                        .ToList()
                    : [capturedCollectionId];
                args.Data.SetText(draggedIds.Count == 1
                    ? $"collection:{draggedIds[0]}"
                    : $"collections:{string.Join(',', draggedIds)}");
                args.Data.RequestedOperation = DataPackageOperation.Move;
            };
            row.AllowDrop = true;
            row.DragOver += (_, args) =>
            {
                if (args.DataView.Contains(StandardDataFormats.Text))
                    args.AcceptedOperation = DataPackageOperation.Move;
            };
            var capturedSectionId = sectionId;
            row.Drop += async (_, args) =>
            {
                if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                var token = await args.DataView.GetTextAsync();
                var draggedIds = ParseDraggedCollectionIds(token);
                if (draggedIds.Count > 0)
                {
                    args.Handled = true;
                    await ViewModel.MoveCollectionsToAsync(draggedIds, capturedSectionId, capturedCollectionId);
                    ClearCollectionSelection();
                    BuildCollectionRows();
                }
            };

            row.Tapped += (_, args) =>
            {
                if (IsInsideButton(args.OriginalSource as DependencyObject)) return;
                UpdateCollectionSelection(
                    capturedCollectionId,
                    capturedSectionId,
                    sectionCollections,
                    isUserCollection);
            };
        }

        FrameworkElement artwork;
        if (Uri.TryCreate(col.PosterUrl, UriKind.Absolute, out var posterUri))
        {
            artwork = new Image
            {
                Width = 32,
                Height = 48,
                Stretch = Stretch.UniformToFill,
                Source = new BitmapImage(posterUri)
            };
        }
        else
        {
            artwork = new Border
            {
                Width = 32,
                Height = 48,
                CornerRadius = new CornerRadius(4),
                Background = (Brush)Application.Current.Resources["SurfaceBrush"]
            };
        }

        var info = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        titleLine.Children.Add(new TextBlock
        {
            Text = col.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 620
        });
        if (col.Featured) titleLine.Children.Add(MakeBadgeSecondary("Featured"));
        if (string.Equals(col.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
            titleLine.Children.Add(MakeBadgeOutline("Hidden"));
        info.Children.Add(titleLine);

        var metadata = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        metadata.Children.Add(new TextBlock
        {
            Text = $"{col.ItemCount} items",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
        });
        metadata.Children.Add(MakeBadgeOutline(col.CollectionType));
        if (showReorder && !string.IsNullOrWhiteSpace(col.LastSyncStatus))
            metadata.Children.Add(MakeBadgeOutline(col.LastSyncStatus));
        var libraryNames = showReorder ? "" : string.Join(", ", CollectionLibraryIds(col)
            .Select(id => ViewModel.Libraries.FirstOrDefault(l => l.Id == id)?.Name ?? $"Library {id}"));
        if (!showReorder && !string.IsNullOrWhiteSpace(libraryNames))
            metadata.Children.Add(new TextBlock
            {
                Text = libraryNames,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420
            });
        info.Children.Add(metadata);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (!string.Equals(col.CollectionType, "manual", StringComparison.OrdinalIgnoreCase))
        {
            var sync = MakeIconButton("\uE72C", $"Sync {col.Title}");
            sync.Click += async (_, _) =>
            {
                sync.IsEnabled = false;
                try { await ViewModel.SyncCollectionCommand.ExecuteAsync(col.Id); }
                finally { sync.IsEnabled = true; }
            };
            actions.Children.Add(sync);
        }
        var edit = MakeIconButton("\uE70F", $"Edit {col.Title}");
        edit.Click += async (_, _) => await OpenEditDialogAsync(col);
        actions.Children.Add(edit);
        var delete = MakeIconButton("\uE74D", $"Delete {col.Title}");
        delete.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
        delete.Click += async (_, _) => await OpenDeleteDialogAsync(col);
        actions.Children.Add(delete);

        if (dragGrip != null)
        {
            Grid.SetColumn(dragGrip, 0);
            row.Children.Add(dragGrip);
        }
        Grid.SetColumn(artwork, 1);
        Grid.SetColumn(info, 2);
        Grid.SetColumn(actions, 3);
        row.Children.Add(artwork);
        row.Children.Add(info);
        row.Children.Add(actions);
        return row;
    }

    private void UpdateCollectionSelection(
        string collectionId,
        string sectionId,
        IReadOnlyList<LibraryCollection> sectionCollections,
        bool isUserCollection)
    {
        var controlDown = IsKeyDown(VirtualKey.Control);
        var shiftDown = IsKeyDown(VirtualKey.Shift);
        if (_selectedCollectionIds.Count > 0 && _selectionIsUserCollection != isUserCollection)
            ClearCollectionSelection();

        if (shiftDown && _selectionSectionId == sectionId && _selectionAnchorId != null)
        {
            var anchorIndex = sectionCollections.ToList().FindIndex(item => item.Id == _selectionAnchorId);
            var currentIndex = sectionCollections.ToList().FindIndex(item => item.Id == collectionId);
            if (anchorIndex >= 0 && currentIndex >= 0)
            {
                for (var index = Math.Min(anchorIndex, currentIndex); index <= Math.Max(anchorIndex, currentIndex); index++)
                    _selectedCollectionIds.Add(sectionCollections[index].Id);
            }
        }
        else if (controlDown)
        {
            if (!_selectedCollectionIds.Add(collectionId))
                _selectedCollectionIds.Remove(collectionId);
            _selectionAnchorId = collectionId;
            _selectionSectionId = sectionId;
        }
        else
        {
            _selectedCollectionIds.Clear();
            _selectedCollectionIds.Add(collectionId);
            _selectionAnchorId = collectionId;
            _selectionSectionId = sectionId;
        }

        _selectionIsUserCollection = isUserCollection;
        BuildCollectionRows();
    }

    private void ClearCollectionSelection()
    {
        _selectedCollectionIds.Clear();
        _selectionAnchorId = null;
        _selectionSectionId = null;
        _selectionIsUserCollection = false;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static IReadOnlyList<string> ParseDraggedCollectionIds(string token)
    {
        if (token.StartsWith("collection:", StringComparison.Ordinal))
            return [token[11..]];
        if (token.StartsWith("collections:", StringComparison.Ordinal))
            return token[12..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [];
    }

    private static IReadOnlyList<int> CollectionLibraryIds(LibraryCollection collection) =>
        collection.LibraryIds.Count > 0 ? collection.LibraryIds : [collection.LibraryId];

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
        await OpenCollectionTypePickerAsync();
    }

    private async Task OpenCollectionTypePickerAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Choose a Collection Type",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            MaxWidth = 720
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = "Smart/manual collections open the full query builder. Imports keep their source-specific setup.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 0, 0, 4)
        });

        AddChoice("\uE9D9", "Smart / Manual", "Build rules with the full collection query wizard.", () =>
        {
            App.Services.GetRequiredService<NavigationService>().Navigate<SmartCollectionWizardPage>(
                new SmartCollectionWizardNavigationArgs(IsAdmin: true, LibraryId: ViewModel.SelectedLibraryId));
        });
        AddChoice("\uE8B7", "MDBList", "Import and keep a public MDBList collection synchronized.", () => _ = OpenCreateDialogAsync("mdblist"));
        AddChoice("\uE8B7", "TMDB", "Create a collection from a TMDB discovery preset.", () => _ = OpenCreateDialogAsync("tmdb"));
        AddChoice("\uE8B7", "Trakt", "Sync a Trakt discovery feed or public list.", () => _ = OpenCreateDialogAsync("trakt"));
        AddChoice("\uE9D9", "Templates", "Start from Silo's curated collection recipes.", () => BrowseTemplatesButton_Click(this, new RoutedEventArgs()));

        void AddChoice(string glyph, string title, string description, Action action)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 11, 14, 11),
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.Resources["SurfaceBrush"]
            };
            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            var copy = new StackPanel { Spacing = 2 };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            copy.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);
            button.Content = content;
            button.Click += (_, _) => { dialog.Hide(); action(); };
            panel.Children.Add(button);
        }

        dialog.Content = panel;
        await dialog.ShowAsync();
    }

    private void BrowseTemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<CollectionsPage>(new CollectionsNavigationArgs(OpenTemplates: true, ReturnAfterTemplates: true));
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

        var (content, nameBox, sortCombo) = BuildGroupDialogContent(null);

        var dialog = new ContentDialog
        {
            Title = "New group",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = content,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var sortMode = (sortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "manual";
            try { await ViewModel.CreateGroupAsync(nameBox.Text, sortMode); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async Task OpenEditGroupDialogAsync(LibraryCollectionGroup group)
    {
        var (content, nameBox, sortCombo) = BuildGroupDialogContent(group);
        var canDelete = !string.Equals(group.Kind, "user_collections", StringComparison.OrdinalIgnoreCase);

        var dialog = new ContentDialog
        {
            Title = "Edit group",
            PrimaryButtonText = "Save",
            SecondaryButtonText = canDelete ? "Delete group" : "",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = content,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary && canDelete)
        {
            await OpenDeleteGroupDialogAsync(group);
        }
        else if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var sortMode = (sortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? group.DefaultSortMode;
            try { await ViewModel.UpdateGroupAsync(group, nameBox.Text, sortMode); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private static (StackPanel Content, TextBox NameBox, ComboBox SortCombo) BuildGroupDialogContent(
        LibraryCollectionGroup? group)
    {
        var panel = new StackPanel { Spacing = 14, Width = 390 };
        var nameBox = new TextBox
        {
            Text = group?.Name ?? "",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"],
        };
        panel.Children.Add(new TextBlock { Text = "Name", FontSize = 13, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(nameBox);

        var sortCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(6) };
        foreach (var (value, label) in new[]
        {
            ("manual", "Manual (drag-drop order)"),
            ("name_asc", "Name A\u2013Z"),
            ("name_desc", "Name Z\u2013A"),
            ("recent", "Recently Updated"),
            ("most_items", "Most Items")
        })
            sortCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        var selectedMode = group?.DefaultSortMode ?? "manual";
        sortCombo.SelectedItem = sortCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selectedMode, StringComparison.Ordinal))
            ?? sortCombo.Items[0];
        panel.Children.Add(new TextBlock
        {
            Text = "Default sort (end-user view)",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0)
        });
        panel.Children.Add(sortCombo);
        return (panel, nameBox, sortCombo);
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

    private Task OpenCreateDialogAsync(string initialType = "manual")
    {
        OpenEditorWorkspace(null, initialType);
        return Task.CompletedTask;
    }

    // ===== Edit Dialog =====

    private Task OpenEditDialogAsync(LibraryCollection col)
    {
        OpenEditorWorkspace(col, col.CollectionType);
        return Task.CompletedTask;
    }

    private void OpenEditorWorkspace(LibraryCollection? collection, string sourceType)
    {
        var (content, getBody, getPosterFile, getBackdropFile) = BuildCollectionForm(collection, sourceType);
        _editingCollection = collection;
        _editorGetBody = getBody;
        _editorGetPosterFile = getPosterFile;
        _editorGetBackdropFile = getBackdropFile;
        EditorContent.Content = content;
        EditorTitle.Text = collection == null
            ? sourceType switch
            {
                "mdblist" => "Import MDBList Collection",
                "tmdb" => "Import TMDB Collection",
                "trakt" => "Import Trakt Collection",
                _ => "Add Collection"
            }
            : $"Edit {collection.Title}";
        EditorSubtitle.Text = collection == null
            ? "Build the collection in a full-page editor instead of a cramped dialog."
            : "Keep rules, artwork, scheduling, and source configuration visible while editing.";
        ChangeSourceButton.Visibility = collection == null ? Visibility.Visible : Visibility.Collapsed;
        BrowsePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        EditorError.Visibility = Visibility.Collapsed;
        EditorError.Text = "";
    }

    private void CloseEditorWorkspace()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        BrowsePanel.Visibility = Visibility.Visible;
        EditorContent.Content = null;
        _editingCollection = null;
        _editorGetBody = null;
        _editorGetPosterFile = null;
        _editorGetBackdropFile = null;
    }

    private void EditorBackButton_Click(object sender, RoutedEventArgs e) => CloseEditorWorkspace();

    private async void ChangeSourceButton_Click(object sender, RoutedEventArgs e)
    {
        CloseEditorWorkspace();
        await OpenCollectionTypePickerAsync();
    }

    private async void EditorSaveButton_Click(object sender, RoutedEventArgs e)
    {
        var body = _editorGetBody?.Invoke();
        if (body == null) return;

        EditorSaveButton.IsEnabled = false;
        ViewModel.ErrorMessage = null;
        try
        {
            var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
            var id = _editingCollection?.Id;
            if (_editingCollection == null)
            {
                var created = await adminApi.CreateCollectionAsync(body);
                id = created.Id;
            }
            else
            {
                await adminApi.UpdateCollectionAsync(_editingCollection.Id, body);
            }

            if (!string.IsNullOrEmpty(id))
            {
                await UploadCollectionImagesAsync(
                    adminApi,
                    id,
                    _editorGetPosterFile?.Invoke() ?? default,
                    _editorGetBackdropFile?.Invoke() ?? default);
            }
            CloseEditorWorkspace();
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
            EditorError.Text = ex.Message;
            EditorError.Visibility = Visibility.Visible;
        }
        finally
        {
            EditorSaveButton.IsEnabled = true;
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

    private (FrameworkElement Content, Func<SiloPlayer.Core.Models.Admin.CreateLibraryCollectionRequest?> GetBody,
        Func<(byte[]? Bytes, string? Name, string? ContentType)> GetPosterFile,
        Func<(byte[]? Bytes, string? Name, string? ContentType)> GetBackdropFile) BuildCollectionForm(LibraryCollection? existing, string initialType = "manual")
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
        typeCombo.Items.Add(new ComboBoxItem { Content = "Trakt", Tag = "trakt" });
        typeCombo.SelectedIndex = 0;
        foreach (ComboBoxItem item in typeCombo.Items)
            if (item.Tag is string initial && initial == initialType) { typeCombo.SelectedItem = item; break; }
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

        var form = new StackPanel
        {
            MaxWidth = 900,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

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

        SiloPlayer.Core.Models.Admin.CreateLibraryCollectionRequest? GetBody()
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

            return new SiloPlayer.Core.Models.Admin.CreateLibraryCollectionRequest
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
        SiloPlayer.Core.Api.AdminApi adminApi, string collectionId,
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
