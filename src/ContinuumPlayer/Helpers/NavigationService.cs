using Microsoft.UI.Xaml.Navigation;

namespace ContinuumPlayer.Helpers;

public class NavigationService
{
    private Frame? _frame;

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
            Frame.GoBack();
    }

    public bool Navigate(Type pageType, object? parameter = null)
    {
        if (Frame == null) return false;
        return Frame.Navigate(pageType, parameter);
    }

    public bool Navigate<TPage>(object? parameter = null) where TPage : Page
    {
        return Navigate(typeof(TPage), parameter);
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        Navigated?.Invoke(this, e);
    }
}
