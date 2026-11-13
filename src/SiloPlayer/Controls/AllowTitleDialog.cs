using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Controls;

/// <summary>Immediate-save title allowances; all standing comes from the server.</summary>
public sealed class AllowTitleDialog : ContentDialog
{
    private readonly TitleAllowanceSession _owner;
    private readonly string _itemId;
    private readonly StackPanel _rows = new() { Spacing = 10 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _retry = new() { Content = AppStrings.Get("common.retry", fallback: "Retry"), Visibility = Visibility.Collapsed };
    private bool _open, _busy, _loaded;

    public AllowTitleDialog(TitleAllowanceSession owner, string itemId, string title)
    {
        _owner = owner; _itemId = itemId;
        Title = AppStrings.Get("web.detail.actions.allowFor", fallback: "Allow for…") + " " + title; CloseButtonText = AppStrings.Get("common.done", fallback: "Done");
        var content = new StackPanel { Spacing = 12, MinWidth = 280, MaxWidth = 440 };
        content.Children.Add(new TextBlock { Text = "Allow these profiles to watch this title above their content limits. Changes save immediately. Library access still applies.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_rows); content.Children.Add(_error); content.Children.Add(_retry);
        Content = new ScrollViewer { Content = content, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += async (_, _) => { _open = true; await RefreshAsync(); };
        Closed += (_, _) => _open = false;
        _retry.Click += async (_, _) => await RefreshAsync();
    }
    private bool Current => _open && _owner.IsCurrent;
    private async Task RefreshAsync()
    {
        if (!Current || _busy) return;
        _busy = true; _loaded = false; _retry.Visibility = Visibility.Collapsed;
        try
        {
            var rows = await _owner.RunAsync((api, owner, ct) => api.GetTitleProfilesAsync(owner, _itemId, ct));
            if (!Current) return;
            _error.Text = ""; Render(rows.Items); _loaded = true;
        }
        catch (OperationCanceledException) { if (_open) { _rows.IsHitTestVisible = false; _error.Text = "The profile or access changed. Reopen this dialog."; } }
        catch { if (Current) { _error.Text = "Couldn’t load title allowances. Retry to refresh their current state."; _retry.Visibility = Visibility.Visible; _rows.IsHitTestVisible = false; } }
        finally { _busy = false; }
    }
    private void Render(IEnumerable<TitleAllowanceProfile> profiles)
    {
        _rows.Children.Clear(); _rows.IsHitTestVisible = true;
        foreach (var profile in profiles.Where(profile => profile.State != "within_limits"))
        {
            var state = TitleAllowanceSession.RowState(profile.State);
            var labels = new StackPanel { Spacing = 2 };
            labels.Children.Add(new TextBlock { Text = profile.Name, TextWrapping = TextWrapping.Wrap });
            labels.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { TitleAllowanceSession.DescribeLimit(profile.MaxContentRating, profile.MaxAdvisoryAge), state.Note }.Where(text => text.Length > 0)), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var check = new CheckBox { Content = labels, IsChecked = state.Checked, IsEnabled = state.Enabled, Tag = profile.ProfileId };
            check.Click += async (_, _) =>
            {
                if (!Current || _busy || !_loaded) { check.IsChecked = state.Checked; return; }
                var allowed = check.IsChecked == true;
                check.IsChecked = state.Checked; _busy = true; _rows.IsHitTestVisible = false;
                string? failure = null;
                try { await _owner.RunAsync((api, owner, ct) => api.SetTitleAsync(owner, profile.ProfileId, _itemId, allowed, ct)); }
                catch (OperationCanceledException) { }
                catch { failure = "Couldn’t save the allowance. Retry to refresh before changing it again."; }
                finally { _busy = false; }
                if (!Current) return;
                await RefreshAsync();
                if (Current && failure != null) { _error.Text = failure; _retry.Visibility = Visibility.Visible; }
            };
            _rows.Children.Add(check);
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(new TextBlock { Text = AppStrings.Get("web.profiles.allow.everyoneCanWatch", fallback: "Everyone can already watch this title."), TextWrapping = TextWrapping.Wrap });
    }
}
