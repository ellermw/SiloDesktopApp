using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views.Admin;

namespace SiloPlayer;

/// <summary>
/// Admin Monitor chrome in the app shell:
/// <list type="bullet">
/// <item>the acting-admin-only "Admin" footer item directly above Profile (AppSidebar.tsx);</item>
/// <item>the main-shell Server Activity indicator: fixed 20px/24px top-right at ≥1024,
/// in the narrow header between Search and the avatar below 1024; hidden while idle
/// (hideWhenEmpty), until Home's first rows have loaded, and while the full-window
/// player, loading cover, Playing Next or the reader covers the shell; shown with the
/// mini player;</item>
/// <item>the admin shell replacing the app shell (no 260px sidebar or app header while
/// <see cref="AdminShellPage"/> owns the frame; the shell shows its own indicator).</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private AdminMonitorAuthority? _adminAuthority;
    private bool _criticalContentLoaded;
    private HomeViewModel? _criticalContentSource;
    private bool _criticalContentSawLoading;

    private bool IsAdminShellActive => ContentFrame.Content is AdminShellPage;

    /// <summary>Called from the constructor after InitializeComponent.</summary>
    private void InitializeAdminMonitor()
    {
        try { _adminAuthority = App.Services.GetService<AdminMonitorAuthority>(); }
        catch (InvalidOperationException) { _adminAuthority = null; }
        if (_adminAuthority != null) _adminAuthority.AvailabilityChanged += OnAdminAvailabilityChanged;
        // The shared store is app-lifetime: resolving it here starts the admin channel
        // lease for an acting admin even before any indicator is shown.
        try { _ = App.Services.GetService<ServerActivityStore>(); }
        catch (InvalidOperationException) { }
        MainServerActivityButton.HideWhenEmpty = true;
        MobileServerActivityButton.HideWhenEmpty = true;
        MobileServerActivityButton.ButtonSize = 36;
        AdminFooterButton.Click += AdminFooter_Click;
        _navigationService.Navigated += OnNavigated_AdminMonitor;
        ContentFrame.Navigating += OnAdminNavigating;
        // The full-window loading cover, Playing Next and the audiobook player cover the
        // shell like the WebUI's standalone /watch route: the indicator hides under them.
        foreach (var cover in new UIElement[] { PlayingNextOverlay, LoadingOverlay, AudiobookNowListeningControl })
            cover.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateAdminChrome());
        UpdateAdminFooterPresentation(NavView.IsPaneOpen);
        UpdateAdminChrome();
    }

    /// <summary>The collapsed 64px rail keeps the Shield aligned with the avatar slot and hides the label.</summary>
    private void UpdateAdminFooterPresentation(bool isOpen)
    {
        AdminFooterLabel.Opacity = isOpen ? 1 : 0;
        AdminFooterLabel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        AdminFooterContent.Spacing = isOpen ? 10 : 0;
        AdminFooterIconSlot.Margin = isOpen ? new Thickness(0) : new Thickness(5, 0, 5, 0);
        AdminFooterButton.HorizontalAlignment = isOpen ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        AdminFooterButton.Width = isOpen ? double.NaN : 40;
        AdminFooterButton.Margin = isOpen ? new Thickness(12, 0, 12, 0) : new Thickness(12, 0, 0, 0);
        AdminFooterButton.Padding = isOpen ? new Thickness(12) : new Thickness(0, 12, 0, 12);
        AdminFooterButton.HorizontalContentAlignment = isOpen ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        // Active (any /admin path): bg-sidebar-accent/90; the shell replaces the sidebar, so this is
        // visible only transiently, but it keeps the item's state truthful.
        AdminFooterButton.Background = IsAdminShellActive
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SidebarAccentBrush"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void DisposeAdminMonitor()
    {
        if (_adminAuthority != null) _adminAuthority.AvailabilityChanged -= OnAdminAvailabilityChanged;
        _navigationService.Navigated -= OnNavigated_AdminMonitor;
        ContentFrame.Navigating -= OnAdminNavigating;
        DetachCriticalContentSource();
    }

    private bool _adminNavigationApproved;
    private bool _adminNavigationPending;

    private async void OnAdminNavigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        if (_adminNavigationApproved || ContentFrame.Content is not AdminShellPage shell || !shell.RequiresLeaveConfirmation || _adminAuthority?.IsAvailable != true) return;
        e.Cancel = true;
        if (_adminNavigationPending) return;
        var type = e.SourcePageType;
        var parameter = e.Parameter;
        var mode = e.NavigationMode;
        _adminNavigationPending = true;
        try
        {
            if (!await shell.CanLeaveAsync() || !ReferenceEquals(ContentFrame.Content, shell)) return;
            _adminNavigationApproved = true;
            if (mode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back && ContentFrame.CanGoBack) _navigationService.GoBackImmediately();
            else if (mode == Microsoft.UI.Xaml.Navigation.NavigationMode.Forward && ContentFrame.CanGoForward) ContentFrame.GoForward();
            else _navigationService.NavigateImmediately(type, parameter);
        }
        finally { _adminNavigationApproved = false; _adminNavigationPending = false; }
    }

    private void OnAdminAvailabilityChanged() => DispatcherQueue.TryEnqueue(UpdateAdminChrome);

    private void AdminFooter_Click(object sender, RoutedEventArgs e)
    {
        if (_isNarrowShell) CloseMobileNavigationPane();
        AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Dashboard));
    }

    private void OnNavigated_AdminMonitor(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (e.Content is HomePage home && !_criticalContentLoaded) AttachCriticalContentSource(home.ViewModel);
        else if (e.Content is not HomePage)
        {
            // Every route other than Home releases the deferred widgets on mount.
            DetachCriticalContentSource();
            if (e.Content is not (ProfileSelectPage or LoginPage or ServerSelectPage)) _criticalContentLoaded = true;
        }
        UpdateAdminChrome();
    }

    private void AttachCriticalContentSource(HomeViewModel viewModel)
    {
        DetachCriticalContentSource();
        _criticalContentSource = viewModel;
        _criticalContentSawLoading = viewModel.IsLoading;
        viewModel.PropertyChanged += OnCriticalContentChanged;
        viewModel.Sections.CollectionChanged += OnCriticalSectionsChanged;
        viewModel.FeaturedSections.CollectionChanged += OnCriticalSectionsChanged;
        EvaluateCriticalContent();
    }

    private void DetachCriticalContentSource()
    {
        if (_criticalContentSource is not { } viewModel) return;
        viewModel.PropertyChanged -= OnCriticalContentChanged;
        viewModel.Sections.CollectionChanged -= OnCriticalSectionsChanged;
        viewModel.FeaturedSections.CollectionChanged -= OnCriticalSectionsChanged;
        _criticalContentSource = null;
    }

    private void OnCriticalContentChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(EvaluateCriticalContent);
    private void OnCriticalSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(EvaluateCriticalContent);

    /// <summary>Home's first rows (or its settled empty/error state) release the indicator, as markCriticalContentLoaded.</summary>
    private void EvaluateCriticalContent()
    {
        if (_criticalContentSource is not { } vm) return;
        if (vm.IsLoading) _criticalContentSawLoading = true;
        var loaded = vm.Sections.Count + vm.FeaturedSections.Count > 0 ||
                     !string.IsNullOrEmpty(vm.ErrorMessage) ||
                     (_criticalContentSawLoading && !vm.IsLoading);
        if (!loaded) return;
        _criticalContentLoaded = true;
        DetachCriticalContentSource();
        UpdateAdminChrome();
    }

    /// <summary>Re-evaluates every Admin Monitor surface of the app shell.</summary>
    internal void UpdateAdminChrome()
    {
        var acting = _adminAuthority?.IsAvailable == true && CanExposeAuthenticatedNavigation;
        var adminShell = IsAdminShellActive;
        AdminFooterButton.Visibility = acting ? Visibility.Visible : Visibility.Collapsed;
        if (!CanExposeAuthenticatedNavigation)
        {
            _criticalContentLoaded = false;
            DetachCriticalContentSource();
        }

        var shellContext = acting && !adminShell && _criticalContentLoaded &&
                           ContentFrame.Content is not (EbookReaderPage or ProfileSelectPage or PasswordRecoveryPage);
        MainServerActivityButton.Eligible = shellContext && !_isNarrowShell;
        MobileServerActivityButton.Eligible = shellContext && _isNarrowShell;
        var covered = _playerService is { State: PlayerState.Expanded or PlayerState.Fullscreen } ||
                      PlayingNextOverlay.Visibility == Visibility.Visible ||
                      LoadingOverlay.Visibility == Visibility.Visible ||
                      AudiobookNowListeningControl.Visibility == Visibility.Visible;
        MainServerActivityButton.Suppressed = covered;
        MobileServerActivityButton.Suppressed = covered;

        if (adminShell)
        {
            // The admin area replaces the whole app shell.
            ScheduleAdminNavigationHide();
            NavView.SelectedItem = null;
            if (MobileShellHeader.Visibility != Visibility.Collapsed) MobileShellHeader.Visibility = Visibility.Collapsed;
        }
    }

    private bool _adminNavigationHideQueued;

    private void ScheduleAdminNavigationHide()
    {
        if (_adminNavigationHideQueued) return;
        _adminNavigationHideQueued = true;
        // Navigated runs inside the frame/pane transition. Leave that callback
        // before hiding, and use the shared COM guard and bounded retries.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _adminNavigationHideQueued = false;
            if (_windowClosed || !IsAdminShellActive) return;
            if (NavView.IsPaneVisible || NavView.IsPaneOpen || _mainNavigationHidePending)
                ApplyMainNavigationHidden();
        })) _adminNavigationHideQueued = false;
    }

    /// <summary>Back inside Admin walks admin pages before leaving the shell.</summary>
    private bool TryAdminShellBack() => ContentFrame.Content is AdminShellPage shell && shell.TryGoBack();

    /// <summary>Ctrl+K inside Admin opens the scoped admin search.</summary>
    private bool TryAdminShellSearch() => ContentFrame.Content is AdminShellPage shell && shell.TryOpenSearch();
}
