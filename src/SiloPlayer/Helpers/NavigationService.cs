using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;

namespace SiloPlayer.Helpers;

public class NavigationService
{
    private Frame? _frame;
    private Type? _currentPageType;
    private object? _currentParameter;

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
                _frame.Navigated += OnNavigated;
        }
    }

    public event EventHandler<NavigationEventArgs>? Navigated;

    public bool CanGoBack => Frame?.CanGoBack ?? false;

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
            Frame.GoBack(new SuppressNavigationTransitionInfo());
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
        if (Frame == null) return false;
        if (_currentPageType == pageType && ParametersEqual(_currentParameter, parameter))
            return false;

        // Frame's stock directional animation makes top-level route changes
        // look like a full control teardown. The shell swaps routes atomically
        // and PageTransitionHelper normalizes the incoming root without
        // applying a second whole-page animation.
        return Frame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());
    }

    public bool Navigate<TPage>(object? parameter = null) where TPage : Page
    {
        return Navigate(typeof(TPage), parameter);
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        _currentPageType = e.SourcePageType;
        _currentParameter = e.Parameter;
        Navigated?.Invoke(this, e);
    }

    private static bool ParametersEqual(object? left, object? right) =>
        ReferenceEquals(left, right) || Equals(left, right);
}
