using Microsoft.UI.Xaml.Navigation;

namespace SiloPlayer.Views;

/// <summary>Compact profile Home editor, sharing the same section mutations and right-side sheet.</summary>
public sealed class CustomizeHomePage : Page
{
    private SettingsPage? _editor;
    private bool _active;
    public CustomizeHomePage() { NavigationCacheMode = NavigationCacheMode.Required; }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _editor ??= new SettingsPage();
        var error = new TextBlock { Text = "Loading Home sections…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        Content = error;
        try { var content = await _editor.CreateCustomizeHomeContentAsync(); if (_active) { _editor.Content = content; Content = _editor; } }
        catch (Exception ex)
        {
            error.Text = $"Could not load Home sections: {ex.Message}";
            var retry = new Button { Content = "Retry" };
            retry.Click += async (_, _) => { if (_editor != null) { var content = await _editor.CreateCustomizeHomeContentAsync(); if (_active) { _editor.Content = content; Content = _editor; } } };
            Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { error, retry } };
        }
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _active = false; _editor?.DeactivateCustomizeHome(); base.OnNavigatedFrom(e); }
}
