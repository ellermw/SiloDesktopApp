using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Views;

public sealed record CustomizeHomeNavigationArgs(string CollectionId, string Name, int? LibraryId = null);

/// <summary>Compact profile Home/library editor sharing the same row mutations and responsive form.</summary>
public sealed class CustomizeHomePage : Page
{
    private SettingsPage? _editor;
    private bool _active;
    private int _navigationRevision;
    public CustomizeHomePage() { NavigationCacheMode = NavigationCacheMode.Required; }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        var revision = ++_navigationRevision;
        var args = e.Parameter as CustomizeHomeNavigationArgs;
        var api = App.Services.GetRequiredService<SettingsApi>(); var context = api.CaptureContext();
        bool Current() => _active && revision == _navigationRevision && api.IsCurrentContext(context);
        _editor ??= new SettingsPage();
        var error = new TextBlock { Text = "Loading saved rows…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        Content = error;
        try
        {
            var content = await _editor.CreateCustomizeHomeContentAsync(args?.LibraryId);
            if (Current())
            {
                _editor.Content = content; Content = _editor;
                if (args != null)
                {
                    await Task.Yield();
                    if (Current()) await _editor.OpenCollectionRowEditorAsync(args);
                }
            }
        }
        catch (Exception ex)
        {
            if (!Current()) return;
            error.Text = $"Could not load saved rows: {ex.Message}";
            var retry = new Button { Content = "Retry" };
            retry.Click += async (_, _) =>
            {
                if (_editor == null || !Current()) return;
                retry.IsEnabled = false;
                try
                {
                    var content = await _editor.CreateCustomizeHomeContentAsync(args?.LibraryId);
                    if (!Current()) return;
                    _editor.Content = content; Content = _editor;
                    if (args != null) { await Task.Yield(); if (Current()) await _editor.OpenCollectionRowEditorAsync(args); }
                }
                catch (Exception failure) { if (Current()) error.Text = $"Could not load saved rows: {failure.Message}"; }
                finally { retry.IsEnabled = true; }
            };
            Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { error, retry } };
        }
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _active = false; ++_navigationRevision; _editor?.DeactivateCustomizeHome(); base.OnNavigatedFrom(e); }
}
