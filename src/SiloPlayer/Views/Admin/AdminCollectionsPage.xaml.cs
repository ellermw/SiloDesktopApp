using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage : Page
{
    public AdminCollectionsViewModel ViewModel { get; }
    private readonly ToastService _toastService;
    private readonly DispatcherTimer _templateJobPollTimer;

    // Track whether the picker is being updated programmatically so we don't re-trigger a load
    private bool _suppressPickerChange;
    private bool _rebuildPending;
    private LibraryCollection? _editingCollection;
    private Func<CreateLibraryCollectionRequest?>? _editorGetBody;
    private Func<(byte[]? Bytes, string? Name, string? ContentType)>? _editorGetPosterFile;
    private Func<(byte[]? Bytes, string? Name, string? ContentType)>? _editorGetBackdropFile;
    private readonly Dictionary<string, string> _groupViewModes = [];
    private readonly HashSet<string> _selectedCollectionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Grid> _selectionRows = new(StringComparer.Ordinal);
    private string? _selectionAnchorId;
    private string? _selectionSectionId;
    private bool _selectionIsUserCollection;
    private bool _loaded;

    public AdminCollectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminCollectionsViewModel>();
        _toastService = App.Services.GetRequiredService<ToastService>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        _templateJobPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _templateJobPollTimer.Tick += async (_, _) => await RefreshTemplateApplyJobAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        ApplyResponsiveLayout(ActualWidth);
        try
        {
            ViewModel.Collections.CollectionChanged += Collections_CollectionChanged;
            ViewModel.CollectionGroups.CollectionChanged += Collections_CollectionChanged;
            if (ViewModel.Collections.Count > 0 || ViewModel.CollectionGroups.Count > 0)
            {
                PopulateLibraryPicker();
                BuildCollectionRows();
            }
            else
            {
                BuildLoadingSkeletons();
            }
            await Task.WhenAll(
                ViewModel.LoadCommand.ExecuteAsync(null),
                RefreshTemplateApplyJobAsync());
            PopulateLibraryPicker();
            BuildCollectionRows();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loaded = false;
        ViewModel.Collections.CollectionChanged -= Collections_CollectionChanged;
        ViewModel.CollectionGroups.CollectionChanged -= Collections_CollectionChanged;
        _templateJobPollTimer.Stop();
        ViewModel.CancelLoad();
        base.OnNavigatedFrom(e);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var compact = width < 900;
        var narrow = width < 600;
        var gutter = narrow ? 16 : compact ? 24 : 40;
        AdminPageContent.Padding = new Thickness(gutter, compact ? 24 : 32, gutter, 40);
        CollectionsTitle.FontSize = narrow ? 34 : compact ? 40 : 48;
        EditorTitle.FontSize = CollectionsTitle.FontSize;

        Grid.SetRow(CollectionsHeaderActions, compact ? 1 : 0);
        Grid.SetColumn(CollectionsHeaderActions, compact ? 0 : 1);
        Grid.SetColumnSpan(CollectionsHeaderActions, compact ? 2 : 1);
        CollectionsHeaderActions.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        CollectionsHeaderActions.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        LibraryPicker.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        Grid.SetRow(ChangeSourceButton, compact ? 1 : 0);
        Grid.SetColumn(ChangeSourceButton, compact ? 0 : 1);
        Grid.SetColumnSpan(ChangeSourceButton, compact ? 2 : 1);
        ChangeSourceButton.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private async Task RefreshTemplateApplyJobAsync()
    {
        try
        {
            var response = await App.Services.GetRequiredService<AdminApi>()
                .GetJobsAsync("template_bundle_apply", 1);
            var job = response.Jobs.FirstOrDefault();
            if (job is null || !IsActiveTemplateJob(job) && !IsRecentTemplateJob(job))
            {
                TemplateApplyJobBanner.Visibility = Visibility.Collapsed;
                _templateJobPollTimer.Stop();
                return;
            }

            TemplateApplyJobBanner.Visibility = Visibility.Visible;
            TemplateApplyJobProgress.Visibility = IsActiveTemplateJob(job) ? Visibility.Visible : Visibility.Collapsed;
            if (job.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                TemplateApplyJobIcon.Glyph = "\uEA39";
                TemplateApplyJobIcon.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
                TemplateApplyJobTitle.Text = "Collection defaults apply failed";
                TemplateApplyJobMessage.Text = job.ErrorMessage ?? job.Message ?? "The job failed.";
            }
            else if (job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                TemplateApplyJobIcon.Glyph = "\uE73E";
                TemplateApplyJobIcon.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
                TemplateApplyJobTitle.Text = "Collection defaults applied";
                TemplateApplyJobMessage.Text = TemplateBundleApplySummary(job);
            }
            else
            {
                TemplateApplyJobIcon.Glyph = "\uE895";
                TemplateApplyJobIcon.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
                TemplateApplyJobTitle.Text = "Applying collection defaults";
                TemplateApplyJobMessage.Text = string.IsNullOrWhiteSpace(job.Message) ? "Working..." : job.Message;
            }

            if (IsActiveTemplateJob(job))
            {
                if (!_templateJobPollTimer.IsEnabled) _templateJobPollTimer.Start();
            }
            else
            {
                _templateJobPollTimer.Stop();
            }
        }
        catch
        {
            // Collection browsing remains usable if the optional job-status request fails.
        }
    }

    private static bool IsActiveTemplateJob(AdminJob job)
        => job.Status is "queued" or "running";

    private static bool IsRecentTemplateJob(AdminJob job)
    {
        var timestamp = job.CompletedAt ?? job.RequestedAt;
        return DateTimeOffset.TryParse(timestamp, out var parsed) &&
               DateTimeOffset.UtcNow - parsed.ToUniversalTime() < TimeSpan.FromMinutes(10);
    }

    private static string TemplateBundleApplySummary(AdminJob job)
    {
        var created = JobResultCount(job, "created");
        var skipped = JobResultCount(job, "skipped");
        var failed = JobResultCount(job, "failed");
        var queued = JobResultCount(job, "sync_queued");
        var featured = JobResultCount(job, "featured");
        var parts = new List<string> { $"Created {created}", $"skipped {skipped}" };
        if (failed > 0) parts.Add($"failed {failed}");
        if (queued > 0) parts.Add($"queued {queued} initial syncs");
        if (featured > 0) parts.Add($"featured {featured}");
        return string.Join("; ", parts);
    }

    private static int JobResultCount(AdminJob job, string key)
    {
        if (job.ResultPayload is null || !job.ResultPayload.TryGetValue(key, out var value) || value is null)
            return 0;
        if (value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } element)
            return element.GetArrayLength();
        if (value is System.Collections.ICollection collection) return collection.Count;
        return 0;
    }

    private void Collections_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => ScheduleRebuild();

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
        var selectedLibraryId = ViewModel.SelectedLibraryId;

        LibraryPicker.Items.Clear();
        LibraryPicker.Items.Add(new ComboBoxItem
        {
            Content = $"All libraries ({ViewModel.AllCollections.Count})",
            Tag = (int?)null
        });

        foreach (var lib in ViewModel.Libraries)
        {
            var count = ViewModel.AllCollections.Count(c => CollectionLibraryIds(c).Contains(lib.Id));
            LibraryPicker.Items.Add(new ComboBoxItem { Content = $"{lib.Name} ({count})", Tag = (int?)lib.Id });
        }

        var selectedIndex = 0;
        if (selectedLibraryId.HasValue)
        {
            for (var index = 1; index < LibraryPicker.Items.Count; index++)
            {
                if (LibraryPicker.Items[index] is ComboBoxItem { Tag: int id } && id == selectedLibraryId.Value)
                {
                    selectedIndex = index;
                    break;
                }
            }
        }
        LibraryPicker.SelectedIndex = selectedIndex;
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
        _selectionRows.Clear();
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
                    try
                    {
                        await ViewModel.MoveCollectionsToAsync(draggedIds, sectionId);
                        ClearCollectionSelection();
                        BuildCollectionRows();
                    }
                    catch (Exception ex) { _toastService.Error(ex.Message); }
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
                    try { await ViewModel.MoveGroupSectionToAsync(token[8..], capturedSectionId); }
                    catch (Exception ex) { _toastService.Error(ex.Message); }
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
        if (showReorder)
            _selectionRows[col.Id] = row;
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
                    try
                    {
                        await ViewModel.MoveCollectionsToAsync(draggedIds, capturedSectionId, capturedCollectionId);
                        ClearCollectionSelection();
                        BuildCollectionRows();
                    }
                    catch (Exception ex) { _toastService.Error(ex.Message); }
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
                try
                {
                    await ViewModel.SyncCollectionCommand.ExecuteAsync(col.Id);
                    SurfaceMutationResult($"Sync started for {col.Title}.");
                }
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
        var previouslySelected = _selectedCollectionIds.ToHashSet(StringComparer.Ordinal);
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
        foreach (var id in previouslySelected.Concat(_selectedCollectionIds).Distinct(StringComparer.Ordinal))
        {
            if (_selectionRows.TryGetValue(id, out var row))
            {
                row.Background = _selectedCollectionIds.Contains(id)
                    ? new SolidColorBrush(Color.FromArgb(34, 99, 102, 241))
                    : (Brush)Application.Current.Resources["CardBackgroundBrush"];
            }
        }
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
        if (DateTimeOffset.TryParse(col.UpdatedAt, out var updatedDt))
            updatedText = SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(updatedDt);
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
                SurfaceMutationResult($"Sync started for {capturedCol.Title}.");
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

    private async void BrowseTemplatesButton_Click(object sender, RoutedEventArgs e)
        => await ShowAdminTemplateGalleryAsync();

    private async Task ShowAdminTemplateGalleryAsync()
    {
        var api = App.Services.GetRequiredService<AdminApi>();
        var dialog = new ContentDialog
        {
            Title = "Browse Collection Templates",
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
            MaxWidth = 960,
        };
        dialog.Content = new StackPanel
        {
            MinWidth = 760,
            Height = 320,
            Children =
            {
                new ProgressRing { IsActive = true, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "Loading templates…", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] },
            }
        };

        var showTask = dialog.ShowAsync().AsTask();
        try
        {
            var templatesTask = api.GetAdminCollectionTemplatesAsync();
            var bundlesTask = api.GetAdminCollectionTemplateBundlesAsync();
            await Task.WhenAll(templatesTask, bundlesTask);
            var catalog = await templatesTask;
            var bundles = (await bundlesTask).Bundles;
            BuildAdminTemplateGalleryRoot(dialog, catalog, bundles);
        }
        catch (Exception ex)
        {
            dialog.Content = new TextBlock
            {
                Text = $"Failed to load templates: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                MinWidth = 560,
                Margin = new Thickness(0, 16, 0, 16),
            };
        }
        await showTask;
    }

    private void BuildAdminTemplateGalleryRoot(ContentDialog dialog, CollectionTemplateCatalog catalog, IReadOnlyList<CollectionTemplateBundle> bundles)
    {
        dialog.Title = "Browse Collection Templates";
        var search = new TextBox { PlaceholderText = "Search templates", HorizontalAlignment = HorizontalAlignment.Stretch };
        var results = new StackPanel { Spacing = 20 };
        var root = new StackPanel { Spacing = 14, MinWidth = 760 };
        root.Children.Add(new TextBlock
        {
            Text = "Pick a curated source — TMDB, Trakt, or MDBList — or apply a complete server template bundle.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(search);
        root.Children.Add(results);

        void Rebuild()
        {
            results.Children.Clear();
            var term = search.Text.Trim();
            if (bundles.Count > 0 && term.Length == 0)
            {
                results.Children.Add(new TextBlock { Text = "TEMPLATE BUNDLES", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
                var bundleGrid = NewTemplateCardGrid();
                foreach (var bundle in bundles)
                    bundleGrid.Children.Add(BuildTemplateCard("\uE81E", bundle.Title, bundle.Description, $"{bundle.TemplateIds.Count} templates", async () => await ShowBundleApplyViewAsync(dialog, catalog, bundles, bundle)));
                results.Children.Add(bundleGrid);
            }

            var matchCount = 0;
            foreach (var group in catalog.Categories)
            {
                var matches = group.Templates.Where(template => term.Length == 0 ||
                    template.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    template.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    template.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count == 0) continue;
                matchCount += matches.Count;
                results.Children.Add(new TextBlock { Text = group.Label.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
                var grid = NewTemplateCardGrid();
                foreach (var template in matches)
                    grid.Children.Add(BuildTemplateCard(template.Icon, template.Title, template.Description, $"{template.Source.ToUpperInvariant()} · {TemplateMediaLabel(template.MediaKind)}", async () => await ShowAdminTemplateConfigAsync(dialog, catalog, bundles, template)));
                results.Children.Add(grid);
            }
            if (matchCount == 0)
                results.Children.Add(new TextBlock { Text = "No templates match your filters.", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 36, 0, 36), Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        }

        search.TextChanged += (_, _) => Rebuild();
        Rebuild();
        dialog.Content = new ScrollViewer { Content = root, MaxHeight = 650, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static VariableSizedWrapGrid NewTemplateCardGrid() => new()
    {
        Orientation = Orientation.Horizontal,
        ItemWidth = 236,
        ItemHeight = 136,
        MaximumRowsOrColumns = 3,
    };

    private static Button BuildTemplateCard(string icon, string title, string description, string footer, Func<Task> open)
    {
        var button = new Button
        {
            Width = 226,
            Height = 126,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(13),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        var stack = new StackPanel { Spacing = 5 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(icon) ? "✦" : icon, FontSize = 18, Width = 24 });
        header.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock { Text = description, FontSize = 11, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        stack.Children.Add(new TextBlock { Text = footer, FontSize = 10, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"], Margin = new Thickness(0, 2, 0, 0) });
        button.Content = stack;
        button.Click += async (_, _) => await open();
        return button;
    }

    private async Task ShowAdminTemplateConfigAsync(ContentDialog dialog, CollectionTemplateCatalog catalog, IReadOnlyList<CollectionTemplateBundle> bundles, CollectionTemplate template)
    {
        dialog.Title = "Template Details";
        var panel = new StackPanel { Spacing = 14, MinWidth = 700 };
        panel.Children.Add(BackToTemplateGalleryButton(dialog, catalog, bundles));
        panel.Children.Add(BuildTemplateSummary(template));

        if (template.Source is "tmdb_collection" or "tmdb_discover")
        {
            panel.Children.Add(new TextBlock
            {
                Text = "This backend-driven blueprint is applied through Template Bundles so management keys, library scoping, synchronization, and featured sections remain consistent.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            dialog.Content = panel;
            return;
        }

        var eligibleLibraries = ViewModel.Libraries.Where(library => TemplateEligibleForLibrary(template, library)).ToList();
        var libraryChecks = eligibleLibraries.Select(library => (Library: library, Check: new CheckBox
        {
            Content = library.Name,
            IsChecked = ViewModel.SelectedLibraryId.HasValue ? library.Id == ViewModel.SelectedLibraryId.Value : true,
        })).ToList();
        var libraryPanel = new StackPanel { Spacing = 4 };
        foreach (var pair in libraryChecks) libraryPanel.Children.Add(pair.Check);
        panel.Children.Add(LabeledTemplateField("Libraries", libraryPanel));

        var titleBox = new TextBox { Text = template.Title };
        var descriptionBox = new TextBox { Text = template.Description };
        var limitBox = new NumberBox { Value = template.DefaultLimit > 0 ? template.DefaultLimit : double.NaN, Minimum = 1, Maximum = 500, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var schedule = NewTemplateScheduleCombo(template.DefaultSyncSchedule);
        var featured = new ToggleSwitch { IsOn = template.Featured, OnContent = "Featured", OffContent = "Not featured" };
        var posterBox = new TextBox { Text = template.PosterPath ?? "", PlaceholderText = "Optional poster URL" };
        panel.Children.Add(LabeledTemplateField("Collection Title", titleBox));
        panel.Children.Add(LabeledTemplateField("Description", descriptionBox));
        TextBox? mdbUrlBox = null;
        if (template.Source == "mdblist")
        {
            mdbUrlBox = new TextBox { Text = template.Mdblist?.Url ?? "", PlaceholderText = "https://mdblist.com/lists/user/slug" };
            panel.Children.Add(LabeledTemplateField("MDBList URL", mdbUrlBox));
        }

        ComboBox? profileCombo = null;
        if (template.RequiresProfile)
        {
            profileCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            try
            {
                var profiles = (await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync()).Profiles;
                foreach (var profile in profiles) profileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
                if (profileCombo.Items.Count > 0) profileCombo.SelectedIndex = 0;
            }
            catch { }
            panel.Children.Add(LabeledTemplateField("Profile", profileCombo));
        }

        var twoColumn = new Grid { ColumnSpacing = 12 };
        twoColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        twoColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var limitField = LabeledTemplateField("Max Items", limitBox);
        var featuredField = LabeledTemplateField("Featured", featured);
        Grid.SetColumn(featuredField, 1);
        twoColumn.Children.Add(limitField);
        twoColumn.Children.Add(featuredField);
        panel.Children.Add(twoColumn);
        panel.Children.Add(LabeledTemplateField("Auto Refresh", schedule));
        panel.Children.Add(LabeledTemplateField("Poster", posterBox));
        var error = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], FontSize = 12 };
        panel.Children.Add(error);
        var create = new Button { Content = "Create Collection", HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        panel.Children.Add(create);
        create.Click += async (_, _) =>
        {
            var libraryIds = libraryChecks.Where(pair => pair.Check.IsChecked == true).Select(pair => pair.Library.Id).ToList();
            if (libraryIds.Count == 0) { error.Text = "Choose at least one library."; error.Visibility = Visibility.Visible; return; }
            create.IsEnabled = false;
            error.Visibility = Visibility.Collapsed;
            try
            {
                var adminApi = App.Services.GetRequiredService<AdminApi>();
                var limit = double.IsNaN(limitBox.Value) ? null : (int?)Math.Clamp((int)limitBox.Value, 1, 500);
                var syncSchedule = (schedule.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                var poster = string.IsNullOrWhiteSpace(posterBox.Text) ? null : posterBox.Text.Trim();
                switch (template.Source)
                {
                    case "tmdb" when template.Tmdb != null:
                        await adminApi.ImportTMDBCollectionAsync(new ImportTMDBCollectionRequest
                        {
                            LibraryIds = libraryIds, Title = titleBox.Text.Trim(), Description = descriptionBox.Text.Trim(),
                            Preset = template.Tmdb.Preset, MediaType = template.Tmdb.MediaType, TimeWindow = template.Tmdb.TimeWindow,
                            Limit = limit, Featured = featured.IsOn, PosterUrl = poster, SyncSchedule = syncSchedule,
                        });
                        break;
                    case "trakt" when template.Trakt != null:
                        await adminApi.ImportTraktCollectionAsync(new ImportTraktCollectionRequest
                        {
                            LibraryIds = libraryIds, Title = titleBox.Text.Trim(), Description = descriptionBox.Text.Trim(),
                            Preset = template.Trakt.Preset, MediaType = template.Trakt.MediaType, ProfileId = (profileCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                            Limit = limit, Featured = featured.IsOn, PosterUrl = poster, SyncSchedule = syncSchedule,
                        });
                        break;
                    case "mdblist" when template.Mdblist != null:
                        if (string.IsNullOrWhiteSpace(mdbUrlBox?.Text)) throw new InvalidOperationException("Enter an MDBList URL.");
                        await adminApi.ImportMDBListCollectionAsync(new ImportMDBListCollectionRequest
                        {
                            LibraryIds = libraryIds, Title = titleBox.Text.Trim(), Description = descriptionBox.Text.Trim(), Url = mdbUrlBox.Text.Trim(),
                            Limit = limit, Featured = featured.IsOn, PosterUrl = poster, SyncSchedule = syncSchedule,
                        });
                        break;
                    default: throw new InvalidOperationException("This template can only be applied through a template bundle.");
                }
                dialog.Hide();
                await ViewModel.LoadCommand.ExecuteAsync(null);
                BuildCollectionRows();
            }
            catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
            finally { create.IsEnabled = true; }
        };
        dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 650, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private Task ShowBundleApplyViewAsync(ContentDialog dialog, CollectionTemplateCatalog catalog, IReadOnlyList<CollectionTemplateBundle> bundles, CollectionTemplateBundle bundle)
    {
        dialog.Title = "Apply Template Bundle";
        var panel = new StackPanel { Spacing = 14, MinWidth = 720 };
        panel.Children.Add(BackToTemplateGalleryButton(dialog, catalog, bundles));
        panel.Children.Add(new Border
        {
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = bundle.Title, FontSize = 15, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = bundle.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] },
                    new TextBlock { Text = $"{bundle.TemplateIds.Count} templates", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] },
                }
            }
        });
        if (bundle.Id == "all_defaults")
            panel.Children.Add(new TextBlock { Text = "Collections are created first; initial syncs are queued so this large bundle can finish without waiting on every source.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });

        var templateMap = catalog.Categories.SelectMany(group => group.Templates).ToDictionary(template => template.Id, StringComparer.Ordinal);
        var libraryChecks = ViewModel.Libraries.Select(library => (Library: library, Check: new CheckBox
        {
            Content = library.Name,
            IsChecked = ViewModel.SelectedLibraryId.HasValue ? library.Id == ViewModel.SelectedLibraryId.Value : true,
        })).ToList();
        var libraryPanel = new StackPanel { Spacing = 4 };
        foreach (var pair in libraryChecks) libraryPanel.Children.Add(pair.Check);
        panel.Children.Add(LabeledTemplateField("Libraries", libraryPanel));

        var featuredPanel = new StackPanel { Spacing = 10 };
        featuredPanel.Children.Add(new TextBlock { Text = "Featured Sections", FontSize = 14, FontWeight = FontWeights.SemiBold });
        featuredPanel.Children.Add(new TextBlock { Text = "Create one hero section for Home and one for each selected library.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        var homeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        homeCombo.Items.Add(new ComboBoxItem { Content = "No home hero", Tag = null });
        foreach (var pair in libraryChecks)
            foreach (var template in EligibleBundleTemplates(bundle, templateMap, pair.Library))
                homeCombo.Items.Add(new ComboBoxItem { Content = $"{pair.Library.Name} / {template.Title}", Tag = new HomeFeatureChoice(pair.Library.Id, template.Id) });
        homeCombo.SelectedItem = homeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is HomeFeatureChoice choice && choice.TemplateId is "tmdb_trending_movies_week" or "tmdb_trending_tv_week") ?? homeCombo.Items[0];
        featuredPanel.Children.Add(LabeledTemplateField("Home Hero", homeCombo));

        var libraryFeatureCombos = new Dictionary<int, ComboBox>();
        foreach (var pair in libraryChecks)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add(new ComboBoxItem { Content = "No library hero", Tag = null });
            foreach (var template in EligibleBundleTemplates(bundle, templateMap, pair.Library))
                combo.Items.Add(new ComboBoxItem { Content = template.Title, Tag = template.Id });
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is string id && id is "tmdb_trending_movies_week" or "tmdb_trending_tv_week" or "tmdb_popular_movies" or "tmdb_popular_tv") ?? combo.Items[0];
            combo.IsEnabled = pair.Check.IsChecked == true;
            pair.Check.Click += (_, _) => combo.IsEnabled = pair.Check.IsChecked == true;
            libraryFeatureCombos[pair.Library.Id] = combo;
            featuredPanel.Children.Add(LabeledTemplateField(pair.Library.Name, combo));
        }
        panel.Children.Add(new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], Child = featuredPanel });

        var deleteExisting = new ToggleSwitch { Header = "Delete Existing Server Collections", OffContent = "Keep existing collections", OnContent = "Delete before applying" };
        panel.Children.Add(deleteExisting);
        var resultPanel = new StackPanel { Spacing = 6 };
        panel.Children.Add(resultPanel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var preview = new Button { Content = "Preview" };
        var apply = new Button { Content = "Apply Defaults", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        actions.Children.Add(preview);
        actions.Children.Add(apply);
        panel.Children.Add(actions);

        ApplyCollectionTemplateBundleRequest Request(bool dryRun)
        {
            var ids = libraryChecks.Where(pair => pair.Check.IsChecked == true).Select(pair => pair.Library.Id).ToList();
            var featured = new ApplyCollectionTemplateBundleFeaturedRequest();
            if (homeCombo.SelectedItem is ComboBoxItem { Tag: HomeFeatureChoice home })
                featured.Home = new ApplyCollectionTemplateBundleHomeFeaturedRequest { LibraryId = home.LibraryId, TemplateId = home.TemplateId };
            var libraryFeatured = new Dictionary<string, string>();
            foreach (var libraryId in ids)
                if (libraryFeatureCombos[libraryId].SelectedItem is ComboBoxItem { Tag: string templateId })
                    libraryFeatured[libraryId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = templateId;
            featured.Libraries = libraryFeatured.Count > 0 ? libraryFeatured : null;
            return new ApplyCollectionTemplateBundleRequest
            {
                LibraryIds = ids,
                DryRun = dryRun ? true : null,
                DeleteExisting = deleteExisting.IsOn,
                Featured = featured.Home != null || featured.Libraries != null ? featured : null,
            };
        }

        preview.Click += async (_, _) =>
        {
            var request = Request(true);
            if (request.LibraryIds.Count == 0) { ShowBundleError(resultPanel, "Choose at least one library."); return; }
            preview.IsEnabled = apply.IsEnabled = false;
            resultPanel.Children.Clear();
            resultPanel.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
            try
            {
                var result = await App.Services.GetRequiredService<AdminApi>().ApplyCollectionTemplateBundleAsync(bundle.Id, request);
                RenderBundleResult(resultPanel, result);
            }
            catch (Exception ex) { ShowBundleError(resultPanel, ex.Message); }
            finally { preview.IsEnabled = apply.IsEnabled = true; }
        };
        apply.Click += async (_, _) =>
        {
            var request = Request(false);
            if (request.LibraryIds.Count == 0) { ShowBundleError(resultPanel, "Choose at least one library."); return; }
            preview.IsEnabled = apply.IsEnabled = false;
            try
            {
                await App.Services.GetRequiredService<AdminApi>().QueueCollectionTemplateBundleApplyAsync(bundle.Id, request);
                dialog.Hide();
                _toastService.Success("Collection defaults apply queued.");
                await RefreshTemplateApplyJobAsync();
                await ViewModel.LoadCommand.ExecuteAsync(null);
                BuildCollectionRows();
            }
            catch (Exception ex) { ShowBundleError(resultPanel, ex.Message); preview.IsEnabled = apply.IsEnabled = true; }
        };
        dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 650, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return Task.CompletedTask;
    }

    private Button BackToTemplateGalleryButton(ContentDialog dialog, CollectionTemplateCatalog catalog, IReadOnlyList<CollectionTemplateBundle> bundles)
    {
        var back = new Button { Content = "‹ Back", HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        back.Click += (_, _) => BuildAdminTemplateGalleryRoot(dialog, catalog, bundles);
        return back;
    }

    private static Border BuildTemplateSummary(CollectionTemplate template) => new()
    {
        Padding = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], Background = (Brush)Application.Current.Resources["SurfaceBrush"],
        Child = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = $"{template.Icon}  {template.Title}", FontSize = 15, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = $"{template.Source.ToUpperInvariant()} · {TemplateMediaLabel(template.MediaKind)}", FontSize = 10, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] },
                new TextBlock { Text = template.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] },
            }
        }
    };

    private static StackPanel LabeledTemplateField(string label, FrameworkElement control)
    {
        var field = new StackPanel { Spacing = 5 };
        field.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        field.Children.Add(control);
        return field;
    }

    private static ComboBox NewTemplateScheduleCombo(string? selected)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in new[] { ("None", ""), ("Every hour", "0 * * * *"), ("Every 6 hours", "0 */6 * * *"), ("Every 12 hours", "0 */12 * * *"), ("Daily", "0 0 * * *"), ("Weekly", "0 0 * * 0") })
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.Ordinal)) ?? combo.Items[0];
        return combo;
    }

    private static bool TemplateEligibleForLibrary(CollectionTemplate template, Library library)
    {
        var type = library.Type.Trim().ToLowerInvariant();
        return template.MediaKind switch
        {
            "movie" => type is "movie" or "movies" or "mixed",
            "tv" => type is "series" or "tv" or "shows" or "mixed",
            _ => true,
        };
    }

    private static IEnumerable<CollectionTemplate> EligibleBundleTemplates(CollectionTemplateBundle bundle, IReadOnlyDictionary<string, CollectionTemplate> map, Library library)
        => bundle.TemplateIds.Select(id => map.GetValueOrDefault(id)).Where(template => template != null && TemplateEligibleForLibrary(template, library))!;

    private static string TemplateMediaLabel(string mediaKind) => mediaKind switch { "tv" => "TV", "movie" => "Movies", _ => "Mixed" };

    private static void ShowBundleError(StackPanel panel, string message)
    {
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], FontSize = 12 });
    }

    private static void RenderBundleResult(StackPanel panel, ApplyCollectionTemplateBundleResponse result)
    {
        panel.Children.Clear();
        var createLabel = result.DryRun ? "Would create" : "Created";
        var summary = $"{createLabel} {result.Created.Count}; skipped {result.Skipped.Count}; failed {result.Failed.Count}";
        if (result.DeleteExisting == true)
            summary = $"{(result.DryRun ? "Would delete" : "Deleted")} {result.Deleted.Count}; delete skipped {result.DeleteSkipped.Count}; delete failed {result.DeleteFailed.Count}; {summary}";
        panel.Children.Add(new TextBlock { Text = summary, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (result.SyncQueued.Count > 0)
            panel.Children.Add(new TextBlock { Text = $"Initial syncs queued: {result.SyncQueued.Count}", FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        foreach (var entry in result.Failed.Take(8))
            panel.Children.Add(new TextBlock { Text = $"{entry.LibraryName} / {entry.TemplateTitle}: {entry.Reason}", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] });
        foreach (var entry in result.FeaturedFailed.Take(8))
            panel.Children.Add(new TextBlock { Text = $"Featured {entry.Surface} / {entry.TemplateTitle}: {entry.Reason}", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] });
    }

    private sealed record HomeFeatureChoice(int LibraryId, string TemplateId);

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
            try
            {
                await ViewModel.CreateGroupAsync(nameBox.Text, sortMode);
                SurfaceMutationResult("Collection group created.");
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = ex.Message;
                _toastService.Error(ex.Message);
            }
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
            try
            {
                await ViewModel.UpdateGroupAsync(group, nameBox.Text, sortMode);
                SurfaceMutationResult("Collection group updated.");
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = ex.Message;
                _toastService.Error(ex.Message);
            }
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
            try
            {
                await ViewModel.DeleteGroupAsync(group);
                SurfaceMutationResult("Collection group deleted.");
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = ex.Message;
                _toastService.Error(ex.Message);
            }
        }
    }

    private async Task MoveGroupAsync(LibraryCollectionGroup group, int delta)
    {
        try { await ViewModel.MoveGroupAsync(group, delta); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; _toastService.Error(ex.Message); }
    }

    private async Task MoveCollectionInGroupAsync(LibraryCollection collection, int delta)
    {
        try { await ViewModel.MoveCollectionInGroupAsync(collection, delta); }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; _toastService.Error(ex.Message); }
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
            var isCreating = _editingCollection is null;
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
            _toastService.Success(isCreating ? "Collection created." : "Collection updated.");
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
            EditorError.Text = ex.Message;
            EditorError.Visibility = Visibility.Visible;
            _toastService.Error(ex.Message);
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
                SurfaceMutationResult("Collection deleted.");
            }
            catch (Exception ex) { _toastService.Error(ex.Message); }
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

    private bool SurfaceMutationResult(string fallbackSuccess)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
        {
            _toastService.Error(ViewModel.ErrorMessage);
            return false;
        }

        _toastService.Success(ViewModel.StatusMessage ?? fallbackSuccess);
        return true;
    }
}
