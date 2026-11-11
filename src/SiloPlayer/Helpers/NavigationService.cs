using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;

namespace SiloPlayer.Helpers;

public class NavigationService
{
    private Frame? _frame;
    private Type? _currentPageType;
    private object? _currentParameter;
    private Replacement? _replacement;
    private readonly List<WeakReference<Page>> _localizedPages = [];
    private sealed record Replacement(Frame Frame, Type? Origin, object? OriginParameter, Type Target, object? TargetParameter);

    /// <summary>
    /// Gives the shared shell one opportunity to stage a route transition
    /// before the Frame swaps its content. Returning true means the request was
    /// accepted and will be committed later through <see cref="NavigateImmediately"/>.
    /// </summary>
    public Func<Type, object?, bool>? NavigationRequestHandler { get; set; }
    public Func<bool>? BackNavigationRequestHandler { get; set; }

    public Frame? Frame
    {
        get => _frame;
        set
        {
            if (_frame != null)
                _frame.Navigated -= OnNavigated;
            _frame = value;
            if (_frame != null)
            {
                _frame.Navigated += OnNavigated;
                if (_frame.Content is Page page) PageBackdrop.SetEnabled(page, true);
            }
        }
    }

    public event EventHandler<NavigationEventArgs>? Navigated;

    public bool CanGoBack => Frame?.CanGoBack ?? false;
    public bool IsRefreshingCurrentEntry { get; private set; }

    public void InvalidateLocalizedPageCaches()
    {
        _localizedPages.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _localizedPages)
            if (reference.TryGetTarget(out var page) && !ReferenceEquals(page, Frame?.Content)
                && page is not SiloPlayer.Views.SettingsPage)
                page.NavigationCacheMode = NavigationCacheMode.Disabled;
    }
    public bool IsCurrentEntry(Type pageType, object? parameter) =>
        _currentPageType == pageType && ReferenceEquals(_currentParameter, parameter);

    public void GoBack()
    {
        if (Frame?.CanGoBack != true)
            return;
        if (BackNavigationRequestHandler?.Invoke() == true)
            return;

        GoBackImmediately();
    }

    public void GoBackImmediately()
    {
        if (Frame?.CanGoBack == true)
            Frame.GoBack(PageTransitionHelper.ForPageChange());
    }

    public bool Navigate(Type pageType, object? parameter = null)
    {
        if (Frame == null) return false;
        if (_currentPageType == pageType && ParametersEqual(_currentParameter, parameter))
            return false;

        if (NavigationRequestHandler?.Invoke(pageType, parameter) == true)
            return true;

        return NavigateImmediately(pageType, parameter);
    }

    /// <summary>
    /// Commits a navigation that has already been staged by the shared shell.
    /// This deliberately bypasses <see cref="NavigationRequestHandler"/>.
    /// </summary>
    public bool NavigateImmediately(Type pageType, object? parameter = null)
    {
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure("navigation-commit", 16);
        if (Frame == null) return false;
        if (_currentPageType == pageType && ParametersEqual(_currentParameter, parameter))
            return false;

        // The incoming page eases in (PageTransitionHelper); an in-place refresh
        // of the current route below stays an instant swap.
        return Frame.Navigate(pageType, parameter, PageTransitionHelper.ForPageChange());
    }

    public bool Navigate<TPage>(object? parameter = null) where TPage : Page
    {
        return Navigate(typeof(TPage), parameter);
    }

    /// <summary>Reread the active route after access changes without adding a history entry.</summary>
    public bool RefreshCurrentEntry()
    {
        var frame = Frame;
        var pageType = _currentPageType;
        var parameter = _currentParameter;
        if (frame == null || pageType == null) return false;
        if (pageType == typeof(SiloPlayer.Views.BoxSetPage) && frame.Content is SiloPlayer.Views.BoxSetPage boxSet)
        {
            // Replacing this route would cancel its accepted Play while the player
            // is still preparing in Idle/Minimized. The model refreshes read authority
            // in place, retaining mounted detail and retiring a changed viewer itself.
            _ = boxSet.ViewModel.RefreshAsync();
            return true;
        }
        if (pageType == typeof(SiloPlayer.Views.RequestsPage) && frame.Content is SiloPlayer.Views.RequestsPage requests)
        {
            // Access changes and event-channel catch-up retire reads, not an open removal
            // confirmation or its outcome. Keep the visit mounted while rereading fresh authority.
            requests.ViewModel.InvalidateCache();
            _ = requests.ViewModel.RefreshAsync();
            return true;
        }
        var forward = frame.ForwardStack.ToArray();
        if (frame.Content is Page current) current.NavigationCacheMode = NavigationCacheMode.Disabled;
        _replacement = new(frame, pageType, parameter, pageType, parameter);
        IsRefreshingCurrentEntry = true;
        try
        {
            var accepted = frame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());
            if (!accepted) _replacement = null;
            if (accepted && ReferenceEquals(Frame, frame) && _currentPageType == pageType && ParametersEqual(_currentParameter, parameter))
            {
                frame.ForwardStack.Clear();
                foreach (var entry in forward) frame.ForwardStack.Add(entry);
            }
            return accepted;
        }
        catch { _replacement = null; throw; }
        finally { IsRefreshingCurrentEntry = false; }
    }

    /// <summary>Resolve an intermediate route without leaving a redirect in Back history.
    /// The replacement is applied when the Frame commits, including shell-staged navigation.</summary>
    public bool NavigateReplacingCurrentEntry<TPage>(object? parameter = null) where TPage : Page
    {
        if (Frame == null) return false;
        _replacement = new(Frame, _currentPageType, _currentParameter, typeof(TPage), parameter);
        try
        {
            var accepted = Navigate<TPage>(parameter);
            if (!accepted) _replacement = null;
            return accepted;
        }
        catch { _replacement = null; throw; }
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        // WinUI implicit Page styles do not apply to application-derived Pages.
        // Navigation is the shared attachment seam; the helper protects auth/custom backgrounds.
        if (e.Content is Page page)
        {
            PageBackdrop.SetEnabled(page, true);
            _localizedPages.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (!_localizedPages.Any(reference => reference.TryGetTarget(out var tracked) && ReferenceEquals(tracked, page)))
                _localizedPages.Add(new(page));
        }
        var replacement = _replacement;
        _replacement = null;
        if (replacement != null && ReferenceEquals(Frame, replacement.Frame) && e.NavigationMode == NavigationMode.New &&
            e.SourcePageType == replacement.Target && ParametersEqual(e.Parameter, replacement.TargetParameter) &&
            Frame.BackStack.LastOrDefault() is { } previous && previous.SourcePageType == replacement.Origin &&
            ParametersEqual(previous.Parameter, replacement.OriginParameter))
            Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
        _currentPageType = e.SourcePageType;
        _currentParameter = e.Parameter;
        Navigated?.Invoke(this, e);
    }

    private static bool ParametersEqual(object? left, object? right) =>
        ReferenceEquals(left, right) || Equals(left, right);
}
