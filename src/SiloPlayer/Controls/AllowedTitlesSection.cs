using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Controls;

/// <summary>Profile Access management. Allowance writes save independently of the profile form.</summary>
public sealed class AllowedTitlesSection : UserControl
{
    private readonly TitleAllowanceSession _owner;
    private readonly string _profile;
    private readonly StackPanel _titles = new() { Spacing = 8 }, _collections = new() { Spacing = 8 }, _results = new() { Spacing = 8 };
    private readonly TextBlock _titleError = new() { TextWrapping = TextWrapping.Wrap }, _collectionError = new() { TextWrapping = TextWrapping.Wrap }, _searchError = new() { TextWrapping = TextWrapping.Wrap }, _mutationError = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _more = new() { Content = AppStrings.Get("web.profiles.allowedTitles.showMore", fallback: "Show more"), Visibility = Visibility.Collapsed }, _titleRetry = new() { Content = "Retry titles", Visibility = Visibility.Collapsed }, _collectionRetry = new() { Content = "Retry collections", Visibility = Visibility.Collapsed }, _searchRetry = new() { Content = AppStrings.Get("web.catalog.page.retrySearch", fallback: "Retry search"), Visibility = Visibility.Collapsed };
    private readonly TextBox _search = new() { PlaceholderText = "Search movies and series", Header = "Add allowed title" };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<MediaItem> _loadedTitles = [];
    private string? _cursor;
    private bool _busy;
    private object? _titleRead, _collectionRead;
    private bool _readingTitles => _titleRead != null;
    private bool _readingCollections => _collectionRead != null;
    private long _searchVersion;

    public AllowedTitlesSection(TitleAllowanceSession owner, string profileId)
    {
        _owner = owner; _profile = profileId;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = AppStrings.Get("web.profiles.allowedTitles.heading", fallback: "Allowed titles"), FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = "Movies and series this profile may watch above its limits. Changes save immediately, separately from Save profile. Library access still applies.", TextWrapping = TextWrapping.Wrap });
        foreach (var child in new UIElement[] { _search, _searchError, _searchRetry, _results, _titleError, _titleRetry, _titles, _more,
            new TextBlock { Text = AppStrings.Get("web.profiles.allowedTitles.collectionsHeading", fallback: "Allowed collections"), FontSize = 15 }, _collectionError, _collectionRetry, _collections, _mutationError }) panel.Children.Add(child);
        Content = panel;
        _search.TextChanged += (_, _) => { ++_searchVersion; _debounce.Stop(); _results.Children.Clear(); _searchError.Text = ""; _searchRetry.Visibility = Visibility.Collapsed; if (!string.IsNullOrWhiteSpace(_search.Text)) _debounce.Start(); };
        // Enter belongs to search, never the containing profile form's default Save command.
        _search.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) args.Handled = true; };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        Unloaded += (_, _) => { ++_searchVersion; _debounce.Stop(); };
        _more.Click += async (_, _) => await LoadTitlesAsync(true);
        _titleRetry.Click += async (_, _) => await LoadTitlesAsync(false);
        _collectionRetry.Click += async (_, _) => await LoadCollectionsAsync();
        _searchRetry.Click += async (_, _) => await SearchAsync();
    }

    public async Task LoadAsync()
    {
        await Task.WhenAll(LoadTitlesAsync(false), LoadCollectionsAsync());
    }

    private async Task LoadTitlesAsync(bool more)
    {
        if (!_owner.IsCurrent || _readingTitles) return;
        var read = new object(); _titleRead = read;
        _more.IsEnabled = false; _titleRetry.Visibility = Visibility.Collapsed;
        try
        {
            var page = await _owner.RunAsync((api, owner, ct) => api.GetAllowedTitlesAsync(owner, _profile, more ? _cursor : null, ct));
            if (!_owner.IsCurrent || !ReferenceEquals(_titleRead, read)) return;
            if (!more) _loadedTitles.Clear();
            foreach (var title in page.Items) if (_loadedTitles.All(item => item.ContentId != title.ContentId)) _loadedTitles.Add(title);
            _cursor = page.Page is { HasMore: true } ? page.Page.NextCursor : null;
            _more.Visibility = !string.IsNullOrEmpty(_cursor) ? Visibility.Visible : Visibility.Collapsed;
            _titleError.Text = ""; RenderTitles();
        }
        catch (OperationCanceledException) { }
        catch { if (_owner.IsCurrent && ReferenceEquals(_titleRead, read)) { _titleError.Text = _loadedTitles.Count > 0 ? "Couldn’t refresh allowed titles. Showing the previous list." : AppStrings.Get("web.profiles.allowedTitles.titlesFailed", fallback: "Couldn’t load allowed titles."); _titleRetry.Visibility = Visibility.Visible; } }
        finally { if (ReferenceEquals(_titleRead, read)) { _titleRead = null; _more.IsEnabled = true; } }
    }
    private void RenderTitles()
    {
        _titles.Children.Clear();
        foreach (var item in _loadedTitles)
            _titles.Children.Add(Row(item.Title + (item.Year > 0 ? $" · {item.Year}" : ""), "Stop allowing", item.ContentId,
                () => MutateAsync((api, owner, ct) => api.SetTitleAsync(owner, _profile, item.ContentId, false, ct))));
        // Filtered-out cards can leave an empty page while a following cursor still exists.
        if (_loadedTitles.Count == 0 && string.IsNullOrEmpty(_cursor)) _titles.Children.Add(new TextBlock { Text = "No titles allowed individually." });
    }
    private async Task LoadCollectionsAsync()
    {
        if (!_owner.IsCurrent || _readingCollections) return;
        var read = new object(); _collectionRead = read; _collectionRetry.Visibility = Visibility.Collapsed;
        try
        {
            var result = await _owner.RunAsync((api, owner, ct) => api.GetAllowedCollectionsAsync(owner, _profile, ct));
            if (!_owner.IsCurrent || !ReferenceEquals(_collectionRead, read)) return;
            _collectionError.Text = ""; _collections.Children.Clear();
            foreach (var collection in result.Items)
                _collections.Children.Add(Row($"{collection.Name} · {collection.ItemCount} titles" + (collection.Live ? "" : " · Paused"), "Stop allowing", collection.CollectionId,
                    () => MutateAsync((api, owner, ct) => api.SetCollectionAsync(owner, _profile, collection.CollectionId, false, ct))));
            if (result.Items.Count == 0) _collections.Children.Add(new TextBlock { Text = "No collections allowed." });
        }
        catch (OperationCanceledException) { }
        catch { if (_owner.IsCurrent && ReferenceEquals(_collectionRead, read)) { _collectionError.Text = "Couldn’t refresh allowed collections. Any previous list may be out of date."; _collectionRetry.Visibility = Visibility.Visible; } }
        finally { if (ReferenceEquals(_collectionRead, read)) _collectionRead = null; }
    }
    private async Task SearchAsync()
    {
        var query = _search.Text.Trim(); var version = _searchVersion;
        if (!_owner.IsCurrent || query.Length == 0) return;
        _searchRetry.Visibility = Visibility.Collapsed;
        try
        {
            var result = await _owner.RunAsync((api, owner, ct) => api.SearchAsync(owner, query, ct));
            if (!_owner.IsCurrent || version != _searchVersion) return;
            _searchError.Text = ""; _results.Children.Clear();
            foreach (var item in result.Items.Where(item => item.Type is "movie" or "series"))
            {
                var already = _loadedTitles.Any(title => title.ContentId == item.ContentId);
                var row = Row($"{item.Title} · {item.Year} · {item.Type}", already ? AppStrings.Get("web.profiles.allowedTitles.allowed", fallback: "Allowed") : AppStrings.Get("web.profiles.allowedTitles.allow", fallback: "Allow"), item.ContentId,
                    () => version == _searchVersion ? MutateAsync((api, owner, ct) => api.SetTitleAsync(owner, _profile, item.ContentId, true, ct)) : Task.CompletedTask);
                if (already) row.Children.OfType<Button>().Single().IsEnabled = false;
                _results.Children.Add(row);
            }
            if (_results.Children.Count == 0) _searchError.Text = AppStrings.Get("web.profiles.allowedTitles.noMatch", fallback: "No matching movies or series.");
        }
        catch (OperationCanceledException) { }
        catch { if (_owner.IsCurrent && version == _searchVersion) { _searchError.Text = "Couldn’t search titles. Previous results may be out of date."; _searchRetry.Visibility = Visibility.Visible; } }
    }
    private static StackPanel Row(string label, string command, string id, Func<Task> action)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        var button = new Button { Content = command, Tag = id };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, command + " " + label);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } };
        row.Children.Add(button); return row;
    }
    private async Task MutateAsync(Func<TitleAllowancesApi, ApiRequestContext, CancellationToken, Task> action)
    {
        if (_busy || !_owner.IsCurrent) return;
        _busy = true; _mutationError.Text = ""; string? failure = null;
        try
        {
            try { await _owner.RunAsync(action); }
            catch (OperationCanceledException) { }
            catch { failure = "Couldn’t save the allowance. Refresh its current state before trying again."; }
            if (!_owner.IsCurrent) return;
            // Retire pre-write reads before starting a guaranteed first-page refresh.
            // Their publication, errors and cleanup cannot affect the replacement read.
            _titleRead = _collectionRead = null;
            await LoadAsync();
            if (_owner.IsCurrent) { _mutationError.Text = failure ?? ""; if (!string.IsNullOrWhiteSpace(_search.Text)) await SearchAsync(); }
        }
        finally { _busy = false; }
    }
}
