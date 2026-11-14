using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public sealed class AllowCollectionDialog : ContentDialog
{
    private readonly TitleAllowanceSession _owner;
    private readonly Collection _collection;
    private readonly StackPanel _rows = new() { Spacing = 12 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _retry = new() { Content = AppStrings.Get("common.retry", fallback: "Retry"), Visibility = Visibility.Collapsed };
    private readonly HashSet<string> _uncertainCopies = [];
    private readonly Dictionary<string, int> _added = [];
    private bool _open, _busy, _loaded;

    public AllowCollectionDialog(TitleAllowanceSession owner, Collection collection)
    {
        _owner = owner; _collection = collection;
        void RefreshTitle()
        {
            Title = AppStrings.Get("web.profiles.allow.title", new Dictionary<string, object?> { ["name"] = collection.Name });
            CloseButtonText = AppStrings.Get("common.done", fallback: "Done");
        }
        RefreshTitle(); AppLocalization.Register(this, RefreshTitle);
        var content = new StackPanel { Spacing = 12, MinWidth = 280, MaxWidth = 440 };
        content.Children.Add(new TextBlock { Text = collection.CollectionType == "manual"
            ? "Allow every movie and series in this collection, including titles added later. Library access still applies."
            : "Allow the movies and series this collection holds now. Titles added later are not allowed. Library access still applies.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_rows); content.Children.Add(_error); content.Children.Add(_retry);
        Content = new ScrollViewer { Content = content, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += async (_, _) => { _open = true; await RefreshAsync(); };
        Closed += (_, _) => _open = false;
        _retry.Click += async (_, _) => await RefreshAsync();
    }
    private bool Current => _open && _owner.IsCurrent && _collection.CreatorProfileId == _owner.Owner.ProfileId;
    private async Task RefreshAsync()
    {
        if (!Current || _busy) return;
        _busy = true; _loaded = false; _rows.IsHitTestVisible = false; _retry.Visibility = Visibility.Collapsed;
        try
        {
            // An uncertain copy is never repeated automatically. Read the resulting title list first.
            foreach (var profile in _uncertainCopies.ToArray())
            {
                await _owner.RunAsync((api, owner, ct) => api.GetAllowedTitlesAsync(owner, profile, ct: ct));
                _uncertainCopies.Remove(profile);
            }
            var standing = new Dictionary<string, bool>();
            if (_collection.CollectionType == "manual")
                foreach (var profile in _owner.LimitedProfiles)
                {
                    var collections = await _owner.RunAsync((api, owner, ct) => api.GetAllowedCollectionsAsync(owner, profile.Id, ct));
                    standing[profile.Id] = collections.Items.Any(collection => collection.CollectionId == _collection.Id);
                }
            if (!Current) return;
            _rows.Children.Clear(); _rows.IsHitTestVisible = true; _error.Text = "";
            foreach (var profile in _owner.LimitedProfiles)
            {
                var label = new StackPanel { Spacing = 2 };
                label.Children.Add(new TextBlock { Text = profile.Name, TextWrapping = TextWrapping.Wrap });
                label.Children.Add(new TextBlock { Text = TitleAllowanceSession.DescribeLimit(profile.MaxContentRating, profile.MaxAdvisoryAge), FontSize = 12 });
                if (_added.TryGetValue(profile.Id, out var added)) label.Children.Add(new TextBlock { Text = $"{added} titles newly allowed", FontSize = 12 });
                if (_collection.CollectionType == "manual")
                {
                    var check = new CheckBox { Content = label, IsChecked = standing[profile.Id], Tag = profile.Id };
                    check.Click += async (_, _) => { var desired = check.IsChecked == true; check.IsChecked = standing[profile.Id]; await ChangeAsync(profile.Id, desired); };
                    _rows.Children.Add(check);
                }
                else
                {
                    var button = new Button { Content = AppStrings.Get("web.profiles.allow.allowCurrent", fallback: "Allow current titles"), Tag = profile.Id };
                    button.Click += async (_, _) => await ChangeAsync(profile.Id, true);
                    label.Children.Add(button); _rows.Children.Add(label);
                }
            }
            _loaded = true;
        }
        catch (OperationCanceledException) { if (_open) _error.Text = "The profile or access changed. Reopen this dialog."; }
        catch { if (Current) { _error.Text = "Couldn’t load collection allowances. Retry before changing them."; _retry.Visibility = Visibility.Visible; } }
        finally { _busy = false; }
    }
    private async Task ChangeAsync(string profile, bool allowed)
    {
        if (!Current || _busy || !_loaded) return;
        _busy = true; _rows.IsHitTestVisible = false; string? failure = null;
        try
        {
            if (_collection.CollectionType == "manual")
                await _owner.RunAsync((api, owner, ct) => api.SetCollectionAsync(owner, profile, _collection.Id, allowed, ct));
            else
            {
                _uncertainCopies.Add(profile);
                var result = await _owner.RunAsync((api, owner, ct) => api.CopyCollectionAsync(owner, profile, _collection.Id, ct));
                _uncertainCopies.Remove(profile); _added[profile] = result.Added;
            }
        }
        catch (OperationCanceledException) { }
        catch { failure = "Couldn’t confirm the change. Retry refreshes the state; it does not copy again."; }
        finally { _busy = false; }
        if (!Current) return;
        await RefreshAsync();
        if (Current && failure != null) { _error.Text = failure; _retry.Visibility = Visibility.Visible; }
    }
}
