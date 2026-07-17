using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;

namespace SiloPlayer.Helpers;

public class NavigationService
{
    private Frame? _frame;
    private Type? _currentPageType;
    private object? _currentParameter;

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
        if (Frame?.CanGoBack == true)
            Frame.GoBack(new SuppressNavigationTransitionInfo());
    }

    public bool Navigate(Type pageType, object? parameter = null)
    {
        if (Frame == null) return false;
        if (_currentPageType == pageType && ParametersEqual(_currentParameter, parameter))
            return false;

        // Frame's stock directional animation makes top-level route changes
        // look like a full control teardown. MainWindow applies the WebUI's
        // short fade/slide entrance after navigation instead.
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
