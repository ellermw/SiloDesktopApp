using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private int _sidebarPublicationQueued;
    private bool _sidebarCustomizationReady;
    private bool _sidebarPinsReady;
    private long _sidebarRetirementGeneration;
    private long _sidebarFocusRestoreGeneration;

    private void QueueSidebarPublication()
    {
        if (Interlocked.Exchange(ref _sidebarPublicationQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _sidebarPublicationQueued, 0);
            TryShellAction("sidebar_publication", PublishSidebar);
        })) Interlocked.Exchange(ref _sidebarPublicationQueued, 0);
    }

    private void RetireSidebarNavigation(bool retireCustomization = true)
    {
        Interlocked.Increment(ref _sidebarRetirementGeneration);
        Interlocked.Increment(ref _sidebarFocusRestoreGeneration);
        if (retireCustomization)
        {
            _sidebarCustomizationReady = false;
            HomeNavItem.Visibility = Visibility.Visible;
            RecommendationsNavItem.Visibility = Visibility.Visible;
            CalendarNavItem.Visibility = Visibility.Visible;
            BuildPluginApps([]);
            AppsHeader.Visibility = Visibility.Collapsed;
        }
        _sidebarPins = [];
        _sidebarPinsReady = false;
        _viewModel.RetireLibraries();
        RemoveDynamicLibraryNavItems();
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>()
                     .Where(item => item.Tag is PrimaryMenuNavTag tag && (retireCustomization || tag.Item.Type != "builtin")).ToArray())
            NavView.MenuItems.Remove(item);
        // Settings temporarily detaches the app tree. Retire that copy too.
        if (_appSidebarItems != null)
        {
            if (retireCustomization)
            {
                var apps = Array.IndexOf(_appSidebarItems, AppsHeader);
                if (apps >= 0) _appSidebarItems = _appSidebarItems.Take(apps + 1).ToArray();
            }
            _appSidebarItems = _appSidebarItems.Where(item => item is not NavigationViewItem { Tag: Library } &&
                (item is not NavigationViewItem { Tag: PrimaryMenuNavTag tag } || (!retireCustomization && tag.Item.Type == "builtin"))).ToArray();
        }
    }

    private void PublishSidebar()
    {
        var header = NavView.MenuItems.IndexOf(LibrariesHeader);
        if (header < 0 || _hydratedShellKey is not { } key ||
            !IsCurrentShellHydration(key, _shellHydrationCts?.Token ?? CancellationToken.None)) return;

        var focused = NavView.XamlRoot == null ? null : FocusManager.GetFocusedElement(NavView.XamlRoot) as FrameworkElement;
        var focusedOwner = FindSidebarFocusOwner(focused);
        var focusState = focused?.FocusState ?? FocusState.Unfocused;
        var selected = NavView.SelectedItem;
        var hidden = _settingsService.Load().HiddenLibraryIds.ToHashSet();
        var oldLibraryItems = NavView.MenuItems.OfType<NavigationViewItem>()
            .Where(item => item.Tag is Library).ToArray();
        var oldLibraries = oldLibraryItems.GroupBy(item => ((Library)item.Tag).Id)
            .ToDictionary(group => group.Key, group => group.First());
        var desired = new List<NavigationViewItem>();
        foreach (var library in _viewModel.Libraries.DistinctBy(library => library.Id)
                     .Where(library => !hidden.Contains(library.Id)))
        {
            oldLibraries.TryGetValue(library.Id, out var item);
            item ??= new NavigationViewItem();
            SetSidebarIcon(item, LibraryNavigationIconName(library.Type));
            if (!Equals(item.Content, library.Name)) item.Content = library.Name;
            item.Tag = library;
            item.Visibility = NavView.IsPaneOpen && !_librariesExpanded ? Visibility.Collapsed : Visibility.Visible;
            ReconcilePins(item, library.Id);
            desired.Add(item);
        }
        ReconcileSidebarItems(NavView.MenuItems, header + 1, oldLibraryItems, desired);
        PublishPrimaryMenu();

        // Native removal/moves may reset selection/focus. Only surviving nodes
        // retain them; retired routes use the existing route synchronization.
        if (selected is NavigationViewItem selectedItem && ContainsSidebarItem(selectedItem))
        {
            if (!ReferenceEquals(NavView.SelectedItem, selectedItem)) NavView.SelectedItem = selectedItem;
        }
        else ResynchronizeSelectedNavigationItem();
        if (focused != null && focusedOwner != null && ContainsSidebarItem(focusedOwner))
        {
            var fallback = FocusManager.GetFocusedElement(NavView.XamlRoot);
            var retirement = _sidebarRetirementGeneration;
            var restore = Interlocked.Increment(ref _sidebarFocusRestoreGeneration);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (restore != _sidebarFocusRestoreGeneration || retirement != _sidebarRetirementGeneration ||
                    !ContainsSidebarItem(focusedOwner) || !focused.IsLoaded || focused.XamlRoot != NavView.XamlRoot ||
                    !IsCurrentShellHydration(key, _shellHydrationCts?.Token ?? CancellationToken.None)) return;
                // A newer explicit focus choice wins over layout restoration.
                if (ReferenceEquals(FocusManager.GetFocusedElement(NavView.XamlRoot), fallback)) focused.Focus(focusState);
            });
        }
    }

    private static NavigationViewItem? FindSidebarFocusOwner(DependencyObject? focused)
    {
        for (var node = focused; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is NavigationViewItem item) return item;
        return null;
    }

    private bool ContainsSidebarItem(NavigationViewItem item) =>
        NavView.MenuItems.Contains(item) || NavView.MenuItems.OfType<NavigationViewItem>().Any(parent => parent.MenuItems.Contains(item));

    private void ReconcilePins(NavigationViewItem library, int libraryId)
    {
        static (string Type, string Id) Key(SidebarPinNavTag tag) => (tag.PinType.ToLowerInvariant(), tag.PinId);
        var old = library.MenuItems.OfType<NavigationViewItem>().ToArray();
        var byKey = old.Where(item => item.Tag is SidebarPinNavTag)
            .GroupBy(item => Key((SidebarPinNavTag)item.Tag)).ToDictionary(group => group.Key, group => group.First());
        var desired = new List<NavigationViewItem>();
        if (_sidebarPins.TryGetValue(libraryId.ToString(), out var pins))
        foreach (var pin in pins)
        {
            byKey.TryGetValue((pin.Type.ToLowerInvariant(), pin.Id), out var item);
            if (item == null)
            {
                var tag = new SidebarPinNavTag { LibraryId = libraryId, PinType = pin.Type, PinId = pin.Id, Label = pin.Label };
                item = new NavigationViewItem { Tag = tag };
                item.Content = BuildPinnedSidebarContent(item, tag);
            }
            else
            {
                // The content's handlers retain this tag. Mutate it in place,
                // preserving focused descendants and keeping callbacks current.
                var tag = (SidebarPinNavTag)item.Tag;
                tag.LibraryId = libraryId; tag.PinType = pin.Type; tag.PinId = pin.Id; tag.Label = pin.Label;
                var content = (Grid)item.Content;
                content.Children.OfType<TextBlock>().Single().Text = pin.Label;
                AutomationProperties.SetName(content.Children.OfType<Button>().Single(), $"Unpin {pin.Label}");
            }
            var icon = string.Equals(pin.Type, "collection", StringComparison.OrdinalIgnoreCase) ? "folder-open" : "layout-grid";
            if (!Equals(item.Icon?.Tag, icon))
            {
                item.Icon = Controls.WebUiIcon.Navigation(icon, 14);
                item.Icon.Tag = icon;
                item.Icon.Opacity = .6;
            }
            desired.Add(item);
        }
        ReconcileSidebarItems(library.MenuItems, 0, old, desired);
    }

    private void PublishPrimaryMenu()
    {
        var old = NavView.MenuItems.OfType<NavigationViewItem>().Where(item => item.Tag is PrimaryMenuNavTag).ToArray();
        var byKey = old.ToDictionary(item => ((PrimaryMenuNavTag)item.Tag).Item.SemanticKey, StringComparer.Ordinal);
        var menu = _sidebarCustomizationReady && _uiCustomizationService.IsSupported ? _uiCustomizationService.PrimaryMenu : null;
        var customized = menu is { Items.Count: > 0 };
        HomeNavItem.Visibility = customized ? Visibility.Collapsed : Visibility.Visible;
        RecommendationsNavItem.Visibility = customized ? Visibility.Collapsed : Visibility.Visible;
        CalendarNavItem.Visibility = customized ? Visibility.Collapsed : Visibility.Visible;
        var desired = new List<NavigationViewItem>();
        if (customized)
        foreach (var source in menu!.Items)
        {
            if (source.Type != "builtin" && !_viewModel.HasCurrentLibrarySnapshot) continue;
            byKey.TryGetValue(source.SemanticKey, out var item);
            if (ResolvePrimaryMenuItem(source, item) is { } resolved) desired.Add(resolved);
        }
        // Locate the fixed anchor after retiring missing entries, then place
        // only custom items. Other section vectors remain untouched.
        foreach (var item in old.Where(item => !desired.Contains(item))) NavView.MenuItems.Remove(item);
        var insertion = NavView.MenuItems.IndexOf(SearchNavItem);
        if (insertion < 0) insertion = NavView.MenuItems.IndexOf(LibrariesHeader);
        insertion -= old.Count(item => desired.Contains(item) && NavView.MenuItems.IndexOf(item) < insertion);
        ReconcileSidebarItems(NavView.MenuItems, insertion, old, desired);
    }

    private static void SetSidebarIcon(NavigationViewItem item, string name)
    {
        if (Equals(item.Icon?.Tag, name)) return;
        item.Icon = Controls.WebUiIcon.Navigation(name);
        item.Icon.Tag = name;
    }

    private static void ReconcileSidebarItems(IList<object> vector, int start,
        IReadOnlyList<NavigationViewItem> old, IReadOnlyList<NavigationViewItem> desired)
    {
        foreach (var item in old.Where(item => !desired.Contains(item))) vector.Remove(item);
        for (var index = 0; index < desired.Count; index++)
        {
            var position = start + index;
            var item = desired[index];
            if (position < vector.Count && ReferenceEquals(vector[position], item)) continue;
            var previous = vector.IndexOf(item);
            if (previous >= 0) vector.RemoveAt(previous);
            vector.Insert(position, item);
        }
    }
}
