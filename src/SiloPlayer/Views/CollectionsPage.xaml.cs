using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

public sealed partial class CollectionsPage : Page
{
    private const string CollectionDragPrefix = "collection:";
    private const string GroupDragPrefix = "group:";
    public CollectionsViewModel ViewModel { get; }
    private SiloPlayer.Controls.NewCollectionDialog? _creationDialog;
    private bool _openTemplatesOnLoad;
    private bool _returnAfterTemplates;
    private bool _collectionBuildQueued;
    private int _collectionColumnCount;
    private int _serverCardSizeClass;

    public CollectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<CollectionsViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ViewModel.Collections.CollectionChanged += (_, _) => QueueCollectionBuild();
        ViewModel.Groups.CollectionChanged += (_, _) => QueueCollectionBuild();
        ViewModel.ServerLibraries.CollectionChanged += (_, _) => QueueCollectionBuild();
        ViewModel.Profiles.CollectionChanged += (_, _) => QueueCollectionBuild();

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsEmpty))
                DispatcherQueue.TryEnqueue(UpdateEmptyState);
            else if (args.PropertyName == nameof(ViewModel.IsLoadingServerCollections))
                DispatcherQueue.TryEnqueue(BuildServerCollectionRows);
            else if (args.PropertyName == nameof(ViewModel.Capabilities)) QueueCollectionBuild();
        };
        SizeChanged += CollectionsPage_SizeChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCollectionsCommand.ExecuteAsync(null);
        BuildCollectionCards();
        if (_openTemplatesOnLoad)
        {
            _openTemplatesOnLoad = false;
            var opened = await OpenNewCollectionPickerAsync();
            if (_returnAfterTemplates && !opened)
                App.Services.GetRequiredService<NavigationService>().GoBack();
        }
    }

    private void QueueCollectionBuild()
    {
        if (_collectionBuildQueued) return;
        _collectionBuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _collectionBuildQueued = false;
            BuildCollectionCards();
        });
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is CollectionsNavigationArgs args)
        {
            _openTemplatesOnLoad = args.OpenTemplates;
            _returnAfterTemplates = args.ReturnAfterTemplates;
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = Visibility.Collapsed;
    }

    private async void CreateCollection_Click(object sender, RoutedEventArgs e)
    {
        await OpenNewCollectionPickerAsync();
    }

    private async Task<bool> OpenNewCollectionPickerAsync()
    {
        if (_creationDialog != null || XamlRoot == null) return false;
        var dialog = new SiloPlayer.Controls.NewCollectionDialog(
            App.Services.GetRequiredService<SiloPlayer.Core.Api.CollectionsApi>(), App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>()) { XamlRoot = XamlRoot };
        _creationDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { _creationDialog = null; }
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (dialog.SelectedKind == "manual") nav.Navigate<CollectionEditorPage>();
        else if (dialog.SelectedKind == "smart") nav.Navigate<CollectionEditorPage>(new CollectionEditorNavigationArgs("smart"));
        else if (dialog.SelectedKind == "synced") nav.Navigate<CollectionEditorPage>(new CollectionEditorNavigationArgs("synced"));
        return dialog.SelectedKind != null;
    }

    private void SmartWizard_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<SmartCollectionWizardPage>(new SmartCollectionWizardNavigationArgs());
    }

    private async void BrowseTemplates_Click(object sender, RoutedEventArgs e)
    {
        await OpenNewCollectionPickerAsync();
    }

    private void CollectionsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1024;
        CollectionsHeaderActions.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CreateCollectionDock.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        PersonalCollectionsHeading.FontSize = e.NewSize.Width < 640 ? 20 : 24;
        var gutter = e.NewSize.Width < 640 ? 16 : e.NewSize.Width < 1024 ? 24 : 40;
        var top = e.NewSize.Width < 640 ? 32 : e.NewSize.Width < 1024 ? 40 : 56;
        CollectionsPageShell.Padding = new Thickness(gutter, top, gutter, compact ? 110 : 40);
        CollectionsPageShell.MaxWidth = 1000 + gutter * 2; CollectionsPageShell.Width = Math.Min(e.NewSize.Width, CollectionsPageShell.MaxWidth);
        CollectionsTitle.FontSize = Math.Clamp(e.NewSize.Width * .04, 32, 48); CollectionsTitle.LineHeight = CollectionsTitle.FontSize * .95; CollectionsTitle.CharacterSpacing = -50;
        CollectionsSubtitle.FontSize = e.NewSize.Width < 640 ? 14 : 16; CollectionsSubtitle.LineHeight = e.NewSize.Width < 640 ? 20 : 24; ServerCollectionsHeading.FontSize = e.NewSize.Width < 640 ? 20 : 24;
        BuildCollectionSkeletons();
        CollectionsHeaderGrid.ColumnDefinitions[1].Width = compact
            ? new GridLength(0)
            : GridLength.Auto;
        Grid.SetRow(CollectionsHeaderActions, compact ? 1 : 0);
        Grid.SetColumn(CollectionsHeaderActions, compact ? 0 : 1);
        Grid.SetColumnSpan(CollectionsHeaderActions, compact ? 2 : 1);
        CollectionsHeaderActions.HorizontalAlignment = compact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;

        var columnCount = e.NewSize.Width >= 1024 ? 7 : e.NewSize.Width >= 640 ? 5 : 3;
        var serverCardSizeClass = e.NewSize.Width >= 1024 ? 2 : e.NewSize.Width >= 640 ? 1 : 0;
        if (_collectionColumnCount != columnCount || _serverCardSizeClass != serverCardSizeClass)
        {
            _collectionColumnCount = columnCount;
            _serverCardSizeClass = serverCardSizeClass;
            QueueCollectionBuild();
        }
    }

    private void BuildCollectionCards()
    {
        CollectionsGrid.Children.Clear();
        EmptyState.Visibility = AddGroupButton.Visibility = AddGroupEditor.Visibility = Visibility.Collapsed;
        var profileId = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().SelectedProfileId;
        var partition = SiloPlayer.Core.Services.PersonalCollectionOwnership.Split(ViewModel.Collections, profileId, ViewModel.Profiles.ToArray());
        PersonalCollectionsCount.Text = partition.Own.Count.ToString();
        PersonalCollectionsNote.Visibility = partition.Own.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var own = BuildPosterBoard(partition.Own.Select(BuildCollectionCard).Cast<FrameworkElement>().ToArray());
        if (partition.Own.Count == 0) own.Children.Add(NewCollectionCard());
        CollectionsGrid.Children.Add(own);
        SharedCollectionsPanel.Children.Clear();
        var multiple = ViewModel.Profiles.Count > 1;
        CollectionsSubtitle.Text = multiple ? "Yours, the ones other profiles share with you, and the server's." : "Yours and the server's.";
        SharedCollectionsPanel.Visibility = multiple && partition.Shared.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (multiple && partition.Shared.Count > 0)
        {
            var sharedHeader = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 16, VerticalSpacing = 4 };
            var sharedTitle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sharedTitle.Children.Add(new TextBlock { Text = "Shared with me", FontSize = ActualWidth < 640 ? 20 : 24, FontWeight = FontWeights.SemiBold });
            sharedTitle.Children.Add(new TextBlock { Text = partition.Shared.Sum(group => group.Collections.Count).ToString(), FontSize = 14, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 3) });
            sharedHeader.Children.Add(sharedTitle);
            sharedHeader.Children.Add(new TextBlock { Text = "Read-only. Other profiles on this account made these.", FontSize = 13, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 3) });
            SharedCollectionsPanel.Children.Add(sharedHeader);
            var owners = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 14, VerticalSpacing = 20 };
            foreach (var group in partition.Shared)
            {
                var count = Math.Min(PosterColumnCount, group.Collections.Count);
                var body = new StackPanel { Spacing = 12, Width = count * PosterWidth + (count - 1) * 14 };
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                heading.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new(999), Background = new SolidColorBrush(((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color) { Opacity = .2 }, Child = new TextBlock { Text = string.IsNullOrEmpty(group.Name) ? "?" : group.Name[..1].ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["AccentBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                var by = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
                by.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "by " }); by.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = group.Name, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] }); heading.Children.Add(by);
                body.Children.Add(heading);
                var cards = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 14, VerticalSpacing = 20 };
                foreach (var collection in group.Collections) { var card = BuildCollectionCard(collection); card.Width = PosterWidth; cards.Children.Add(card); }
                body.Children.Add(cards); owners.Children.Add(body);
            }
            SharedCollectionsPanel.Children.Add(owners);
        }
        if (ViewModel.Capabilities?.ItemReorder == true && partition.Own.Count > 1)
            CollectionsGrid.Children.Add(new TextBlock { Text = "Drag a poster to change the order, or focus its handle and press Space, then the arrow keys.", FontSize = 13, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        BuildServerCollectionRows();
    }

    private Border BuildLegacyCollectionCard(Collection collection)
    {
        var currentProfileId = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().SelectedProfileId;
        var canManage = !string.IsNullOrWhiteSpace(currentProfileId) &&
            string.Equals(collection.CreatorProfileId, currentProfileId, StringComparison.Ordinal);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        badges.Children.Add(BuildCollectionBadge(FormatCollectionType(collection.CollectionType), true));
        if (collection.IsShared) badges.Children.Add(BuildCollectionBadge("Shared"));
        if (!string.IsNullOrWhiteSpace(collection.SyncSchedule))
            badges.Children.Add(BuildCollectionBadge(collection.SyncSchedule));
        if (collection.LastSyncStatus is "warning" or "failed")
            badges.Children.Add(BuildCollectionBadge(collection.LastSyncStatus == "failed" ? "Sync failed" : "Sync warning"));

        var details = new StackPanel
        {
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = collection.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                badges
            }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        if (canManage && CanSyncCollection(collection))
            actions.Children.Add(BuildCollectionActionButton("\uE895", "Sync collection", async () =>
                await ViewModel.SyncCollectionCommand.ExecuteAsync(collection.Id)));
        if (canManage) actions.Children.Add(BuildCollectionActionButton("\uE70F", "Edit collection", () =>
        {
            NavigateToCollectionEditor(collection);
            return Task.CompletedTask;
        }));
        if (canManage) actions.Children.Add(BuildCollectionActionButton("\uE74D", "Delete collection", async () =>
            await ShowDeleteConfirmationAsync(collection)));

        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = new FontIcon
        {
            Glyph = "\uE700",
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        Grid.SetColumn(grip, 0);
        Grid.SetColumn(details, 1);
        content.Children.Add(grip);
        content.Children.Add(details);

        var openButton = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(openButton, $"Open {collection.Name}");
        openButton.Click += (_, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Name,
                Subtitle = collection.IsShared ? "Shared collection" : "Personal collection",
                IsUserCollection = true,
            });
        };

        var cardLayer = new Grid();
        cardLayer.Children.Add(openButton);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        cardLayer.Children.Add(actions);

        var card = new Border
        {
            MinHeight = 116,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20, 16, 14, 16),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Child = cardLayer,
            Tag = collection
        };

        // Hover effect
        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
            {
                b.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
                b.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
                actions.Opacity = 1;
                grip.Opacity = 1;
            }
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
            {
                b.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
                b.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
                actions.Opacity = 0;
                grip.Opacity = 0;
            }
        };
        actions.GotFocus += (_, _) =>
        {
            actions.Opacity = 1;
            grip.Opacity = 1;
        };

        // Right-click context menu
        var menuFlyout = new MenuFlyout();
        if (!canManage) { card.ContextFlyout = menuFlyout; return card; }

        if (canManage && CanSyncCollection(collection))
        {
            var syncItem = new MenuFlyoutItem
            {
                Text = "Sync now",
                Icon = new FontIcon { Glyph = "\uE895" }
            };
            syncItem.Click += async (_, _) =>
            {
                await ViewModel.SyncCollectionCommand.ExecuteAsync(collection.Id);
            };
            menuFlyout.Items.Add(syncItem);
        }

        var editItem = new MenuFlyoutItem
        {
            Text = canManage ? "Edit" : "View details",
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        editItem.Click += (_, _) =>
        {
            NavigateToCollectionEditor(collection);
        };

        var deleteItem = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += async (_, _) =>
        {
            await ShowDeleteConfirmationAsync(collection);
        };

        menuFlyout.Items.Add(editItem);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        var moveMenu = new MenuFlyoutSubItem { Text = "Move to group", Icon = new FontIcon { Glyph = "\uE8B7" } };
        var ungroupedItem = new MenuFlyoutItem { Text = "Ungrouped" };
        ungroupedItem.Click += async (_, _) => await ViewModel.MoveCollectionToGroupAsync(collection, null);
        moveMenu.Items.Add(ungroupedItem);
        foreach (var group in ViewModel.Groups)
        {
            var targetGroup = group;
            var groupItem = new MenuFlyoutItem { Text = group.Name };
            groupItem.Click += async (_, _) => await ViewModel.MoveCollectionToGroupAsync(collection, targetGroup.Id);
            moveMenu.Items.Add(groupItem);
        }
        menuFlyout.Items.Add(moveMenu);
        var moveUp = new MenuFlyoutItem { Text = "Move earlier", Icon = new FontIcon { Glyph = "\uE70E" } };
        moveUp.Click += async (_, _) => await ViewModel.MoveCollectionAsync(collection, -1);
        var moveDown = new MenuFlyoutItem { Text = "Move later", Icon = new FontIcon { Glyph = "\uE70D" } };
        moveDown.Click += async (_, _) => await ViewModel.MoveCollectionAsync(collection, 1);
        menuFlyout.Items.Add(moveUp);
        menuFlyout.Items.Add(moveDown);
        if (canManage) menuFlyout.Items.Add(deleteItem);
        card.ContextFlyout = menuFlyout;
        ConfigureCollectionDrag(card, collection);

        return card;
    }

    private void ConfigureCollectionDrag(Border card, Collection collection)
    {
        card.CanDrag = true;
        card.AllowDrop = true;
        card.DragStarting += (sender, args) =>
        {
            _ = ViewModel.BeginCollectionDragAsync();
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText($"{CollectionDragPrefix}{collection.Id}");
        };
        card.DragOver += (_, args) => args.AcceptedOperation = DataPackageOperation.Move;
        card.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            args.Handled = true;
            var payload = await args.DataView.GetTextAsync();
            if (!payload.StartsWith(CollectionDragPrefix, StringComparison.Ordinal)) return;

            var sourceId = payload[CollectionDragPrefix.Length..];
            await ViewModel.DropCollectionAsync(sourceId, collection.Id, collection.GroupId);
        };
    }

    private static void NavigateToCollectionEditor(Collection collection)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<CollectionEditorPage>(new CollectionEditorNavigationArgs { CollectionId = collection.Id, PosterUrl = collection.PosterUrl, PosterIsCollage = collection.PosterIsCollage });
    }

    private UIElement BuildCollectionGroupSection(CollectionGroup? group, string title, IReadOnlyList<Collection> items)
    {
        var section = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 28) };
        section.AllowDrop = true;
        section.DragOver += (_, args) => args.AcceptedOperation = DataPackageOperation.Move;
        section.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            args.Handled = true;
            var payload = await args.DataView.GetTextAsync();
            if (payload.StartsWith(CollectionDragPrefix, StringComparison.Ordinal))
            {
                await ViewModel.DropCollectionAsync(payload[CollectionDragPrefix.Length..], null, group?.Id);
            }
            else if (group != null && payload.StartsWith(GroupDragPrefix, StringComparison.Ordinal))
            {
                await ViewModel.DropGroupAsync(payload[GroupDragPrefix.Length..], group.Id);
            }
        };
        var header = new Grid
        {
            Padding = new Thickness(0, 0, 0, 9),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (group != null)
        {
            var groupGrip = new FontIcon
            {
                Glyph = "\uE700",
                FontSize = 14,
                Opacity = 0,
                CanDrag = true,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            groupGrip.DragStarting += (_, args) =>
            {
                args.Data.RequestedOperation = DataPackageOperation.Move;
                args.Data.SetText($"{GroupDragPrefix}{group.Id}");
            };
            header.PointerEntered += (_, _) => groupGrip.Opacity = 1;
            header.PointerExited += (_, _) => groupGrip.Opacity = 0;
            heading.Children.Add(groupGrip);
        }
        var headingTitle = new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeights.Light,
            Foreground = group == null
                ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
                : (Brush)Application.Current.Resources["PrimaryTextBrush"]
        };
        heading.Children.Add(headingTitle);
        heading.Children.Add(new TextBlock
        {
            Text = items.Count.ToString(),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
        });
        header.Children.Add(heading);

        if (group != null)
        {
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Opacity = 0
            };
            var renameBox = new TextBox
            {
                Text = group.Name,
                Width = 300,
                MaxWidth = 300,
                Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
            };
            var renameEditor = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Visibility = Visibility.Collapsed
            };
            renameEditor.Children.Add(renameBox);

            void CancelRename()
            {
                renameBox.Text = group.Name;
                renameEditor.Visibility = Visibility.Collapsed;
                heading.Visibility = Visibility.Visible;
                controls.Visibility = Visibility.Visible;
            }

            async Task CommitRenameAsync()
            {
                if (!await ViewModel.RenameGroupAsync(group, renameBox.Text)) return;
                CancelRename();
            }

            renameEditor.Children.Add(BuildCollectionActionButton("\uE73E", "Save group name", CommitRenameAsync));
            renameEditor.Children.Add(BuildCollectionActionButton("\uE711", "Cancel", () =>
            {
                CancelRename();
                return Task.CompletedTask;
            }));
            renameBox.KeyDown += async (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    args.Handled = true;
                    await CommitRenameAsync();
                }
                else if (args.Key == Windows.System.VirtualKey.Escape)
                {
                    args.Handled = true;
                    CancelRename();
                }
            };
            header.Children.Add(renameEditor);

            controls.Children.Add(BuildCollectionActionButton("\uE70F", "Rename group", () =>
            {
                controls.Visibility = Visibility.Collapsed;
                heading.Visibility = Visibility.Collapsed;
                renameEditor.Visibility = Visibility.Visible;
                renameBox.Focus(FocusState.Programmatic);
                renameBox.SelectAll();
                return Task.CompletedTask;
            }));
            controls.Children.Add(BuildCollectionActionButton("\uE74D", "Delete group", () => DeleteGroupAsync(group)));
            header.PointerEntered += (_, _) => controls.Opacity = 1;
            header.PointerExited += (_, _) => controls.Opacity = 0;
            controls.GotFocus += (_, _) => controls.Opacity = 1;
            Grid.SetColumn(controls, 1);
            header.Children.Add(controls);
        }
        section.Children.Add(header);

        if (items.Count == 0)
        {
            section.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 22, 16, 22),
                Child = new TextBlock
                {
                    Text = "Drop a collection here",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"]
                }
            });
            return section;
        }

        var repeater = new ItemsRepeater
        {
            ItemsSource = items.Select(BuildCollectionCard).ToList(),
            Layout = new UniformGridLayout
            {
                MaximumRowsOrColumns = Math.Max(1, _collectionColumnCount),
                MinItemWidth = 280,
                MinColumnSpacing = 16,
                MinRowSpacing = 16,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill
            }
        };
        section.Children.Add(repeater);
        return section;
    }

    private static Border BuildCollectionBadge(string text, bool accent = false)
        => new()
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Background = accent
                ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                : (Brush)Application.Current.Resources["SurfaceBrush"],
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = accent
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };

    private static Button BuildCollectionActionButton(string glyph, string tooltip, Func<Task> action)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(9),
            Content = new FontIcon { Glyph = glyph, FontSize = 13 }
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private void BuildLegacyServerCollectionRows()
    {
        ServerCollectionsRows.Children.Clear();
        ServerCollectionsSection.Visibility = ViewModel.IsLoadingServerCollections || ViewModel.ServerLibraries.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var library in ViewModel.ServerLibraries)
        {
            var row = new StackPanel { Spacing = 14 };
            var rowHeader = new Grid();
            rowHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Button
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = library.LibraryName,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
                }
            };
            title.Click += (_, _) => NavigateToLibraryCollections(library);
            rowHeader.Children.Add(title);

            if (library.TotalCount > library.Collections.Count)
            {
                var exploreAll = new Button
                {
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    Content = "Explore all"
                };
                exploreAll.Click += (_, _) => NavigateToLibraryCollections(library);
                Grid.SetColumn(exploreAll, 1);
                rowHeader.Children.Add(exploreAll);
            }
            row.Children.Add(rowHeader);

            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            foreach (var collection in library.Collections)
                cards.Children.Add(BuildServerCollectionCard(library, collection));

            row.Children.Add(new ScrollViewer
            {
                Content = cards,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            ServerCollectionsRows.Children.Add(row);
        }
    }

    private static void NavigateToLibraryCollections(ServerCollectionsLibrary library)
    {
        var libraryModel = App.MainWindowInstance?.FindLibrary(library.LibraryId)
            ?? new SiloPlayer.Core.Models.Catalog.Library
            {
                Id = library.LibraryId,
                Name = library.LibraryName
            };
        App.Services.GetRequiredService<NavigationService>()
            .Navigate<LibraryPage>(new LibraryPage.NavigationArgs(libraryModel, "Collections"));
    }

    private Border BuildLegacyServerCollectionCard(ServerCollectionsLibrary library, ServerCollectionSummary collection)
    {
        var cardWidth = _serverCardSizeClass switch
        {
            0 => 130d,
            1 => 150d,
            _ => 178d,
        };
        var poster = new Border
        {
            Width = cardWidth,
            Height = cardWidth * 1.5,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"]
        };
        if (!string.IsNullOrWhiteSpace(collection.PosterUrl) &&
            Uri.TryCreate(collection.PosterUrl, UriKind.Absolute, out var posterUri))
        {
            poster.Child = new Image
            {
                Source = new BitmapImage(posterUri),
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            poster.Child = new TextBlock
            {
                Text = collection.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16),
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            };
        }

        var count = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            Child = new TextBlock
            {
                Text = collection.ItemCount.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
            }
        };
        var posterGrid = new Grid();
        posterGrid.Children.Add(poster);
        posterGrid.Children.Add(count);

        var isPinned = App.MainWindowInstance?.IsSidebarPin(
            library.LibraryId, "collection", collection.Id) == true;
        var pinIcon = new FontIcon
        {
            Glyph = isPinned ? "\uE841" : "\uE840",
            FontSize = 14
        };
        var pinButton = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 0, 0, 0)),
            Content = pinIcon,
            Opacity = isPinned ? 1 : 0
        };
        ToolTipService.SetToolTip(pinButton, isPinned ? "Unpin from sidebar" : "Pin to sidebar");
        AutomationProperties.SetName(pinButton, isPinned
            ? $"Unpin {collection.Title} from sidebar"
            : $"Pin {collection.Title} to sidebar");
        pinButton.Tapped += (_, args) => args.Handled = true;
        pinButton.Click += async (_, _) =>
        {
            if (App.MainWindowInstance is not MainWindow window) return;
            pinButton.IsEnabled = false;
            try
            {
                var nowPinned = await window.ToggleSidebarPinAsync(
                    library.LibraryId, "collection", collection.Id, collection.Title);
                pinIcon.Glyph = nowPinned ? "\uE841" : "\uE840";
                pinButton.Opacity = nowPinned ? 1 : 0;
                ToolTipService.SetToolTip(pinButton, nowPinned ? "Unpin from sidebar" : "Pin to sidebar");
                AutomationProperties.SetName(pinButton, nowPinned
                    ? $"Unpin {collection.Title} from sidebar"
                    : $"Pin {collection.Title} to sidebar");
            }
            finally
            {
                pinButton.IsEnabled = true;
            }
        };
        var stack = new StackPanel
        {
            Width = cardWidth,
            Spacing = 9,
            Children =
            {
                posterGrid,
                new TextBlock
                {
                    Text = collection.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
                }
            }
        };
        var openButton = new Button
        {
            Content = stack,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(openButton, $"Open {collection.Title}");
        openButton.Click += (_, _) => App.Services.GetRequiredService<NavigationService>()
            .Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Title,
                Subtitle = $"{library.LibraryName} collection",
                IsUserCollection = false,
                LibraryId = library.LibraryId
            });

        var cardLayer = new Grid();
        cardLayer.Children.Add(openButton);
        cardLayer.Children.Add(pinButton);
        var card = new Border { Child = cardLayer, Tag = collection };
        card.PointerEntered += (_, _) => pinButton.Opacity = 1;
        card.PointerExited += (_, _) =>
        {
            if (App.MainWindowInstance?.IsSidebarPin(
                    library.LibraryId, "collection", collection.Id) != true &&
                pinButton.FocusState == FocusState.Unfocused)
                pinButton.Opacity = 0;
        };
        pinButton.GotFocus += (_, _) => pinButton.Opacity = 1;
        return card;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        AddGroupButton.Visibility = Visibility.Collapsed;
        AddGroupEditor.Visibility = Visibility.Visible;
        AddGroupNameBox.Text = "";
        AddGroupNameBox.Focus(FocusState.Programmatic);
    }

    private async void AddGroupCommit_Click(object sender, RoutedEventArgs e)
    {
        if (!await ViewModel.CreateGroupAsync(AddGroupNameBox.Text)) return;
        CloseAddGroupEditor();
    }

    private void AddGroupCancel_Click(object sender, RoutedEventArgs e) => CloseAddGroupEditor();

    private async void AddGroupNameBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (await ViewModel.CreateGroupAsync(AddGroupNameBox.Text))
                CloseAddGroupEditor();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CloseAddGroupEditor();
        }
    }

    private void CloseAddGroupEditor()
    {
        AddGroupNameBox.Text = "";
        AddGroupEditor.Visibility = Visibility.Collapsed;
        AddGroupButton.Visibility = ViewModel.Collections.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task RenameGroupAsync(CollectionGroup group)
    {
        var input = new TextBox
        {
            Text = group.Name,
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(input);
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "Rename group",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                error.Text = "Enter a group name.";
                error.Visibility = Visibility.Visible;
                input.Focus(FocusState.Programmatic);
                return;
            }

            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Saving...";
                error.Visibility = Visibility.Collapsed;
                if (await ViewModel.RenameGroupAsync(group, input.Text))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The group could not be renamed.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Save";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        dialog.Opened += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };
        await dialog.ShowAsync();
    }

    private async Task DeleteGroupAsync(CollectionGroup group)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"Delete group \"{group.Name}\"? Its collections will move to Ungrouped.",
            TextWrapping = TextWrapping.Wrap
        });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = "Delete group",
            Content = content,
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Deleting...";
                error.Visibility = Visibility.Collapsed;
                if (await ViewModel.DeleteGroupAsync(group))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The group could not be deleted.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Delete";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private static bool CanSyncCollection(Collection collection)
        => collection.CollectionType is "mdblist" or "tmdb" or "tmdb_list" or "trakt";

    private static string FormatCollectionType(string type) => type switch
    {
        "tmdb_list" => "TMDB List",
        "smart" => "Smart",
        "mdblist" => "MDBList",
        "tmdb" => "TMDB",
        "trakt" => "Trakt",
        _ => "Manual"
    };

    private static string FormatCollectionInfo(Collection collection)
    {
        var count = collection.ItemCount > 0
            ? $"{collection.ItemCount:N0} items"
            : $"{FormatCollectionType(collection.CollectionType)} collection";

        return string.IsNullOrWhiteSpace(collection.LastSyncStatus)
            ? count
            : $"{count} - sync {collection.LastSyncStatus}";
    }

    private async Task ShowDeleteConfirmationAsync(Collection collection)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = collection.IsShared ? "It's removed for you and every profile you share it with. This can't be undone." : "This can't be undone.",
            TextWrapping = TextWrapping.Wrap
        });
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = $"Delete \"{collection.Name}\"?",
            Content = content,
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Deleting...";
                error.Visibility = Visibility.Collapsed;
                await ViewModel.DeleteCollectionCommand.ExecuteAsync(collection.Id);
                if (ViewModel.Collections.All(candidate => candidate.Id != collection.Id))
                {
                    args.Cancel = false;
                    return;
                }

                error.Text = ViewModel.ErrorMessage ?? "The collection could not be deleted.";
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.PrimaryButtonText = "Delete";
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }
}

public sealed record CollectionsNavigationArgs(bool OpenTemplates = false, bool ReturnAfterTemplates = false);
