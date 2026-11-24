using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.Converters;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class NotificationSeriesControl : UserControl
{
    public NotificationSeriesState State { get; }
    private readonly NotificationsApi _api;
    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private CancellationTokenSource? _search;
    private bool _ready, _opened, _syncingRows;
    private string _filter = "all";

    public NotificationSeriesControl()
    {
        _api = App.Services.GetRequiredService<NotificationsApi>(); State = new(_api);
        InitializeComponent(); _ready = true;
        State.PropertyChanged += (_, _) => Update();
        Unloaded += (_, _) => Close();
        AppLocalization.Register(this, RefreshInlineChoices);
    }

    public async Task OpenAsync()
    {
        _opened = true;
        await State.OpenAsync();
        if (!_opened) return;
        _ready = false; SearchBox.Text = State.Options.Q;
        _filter = State.Options.Status == "muted" ? "muted" : "all";
        SortCombo.SelectedItem = SortCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == State.Options.Sort);
        _ready = true; Update();
    }

    public void Close() { _opened = false; _search?.Cancel(); _search?.Dispose(); _search = null; State.Close(); }
    public async Task PreferencesChangedAsync() { if (_opened) await State.OpenAsync(); }
    private async void Options_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && _opened) await ApplyOptionsAsync(); }
    private async void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (!_opened || sender is not Button { Tag: string filter } || filter == "muted" && !State.CanBulkMute) return;
        _filter = filter; await ApplyOptionsAsync();
    }
    private async void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || !_opened) return;
        State.ClearSelection();
        _search?.Cancel(); _search?.Dispose(); var owner = _search = new();
        try { await Task.Delay(300, owner.Token); if (_opened) await ApplyOptionsAsync(); }
        catch (OperationCanceledException) when (owner.IsCancellationRequested || !_opened) { }
    }
    private Task ApplyOptionsAsync() => State.SetOptionsAsync(new(SearchBox.Text,
        (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "title", _filter == "muted" ? "muted" : "all"));
    private async void Retry_Click(object sender, RoutedEventArgs e) => await State.RetryAsync();
    private async void More_Click(object sender, RoutedEventArgs e) => await State.LoadMoreAsync();
    private void SelectShown_Click(object sender, RoutedEventArgs e) => State.ToggleLoadedSelection();
    public Task LoadMoreAsync() => State.LoadMoreAsync();
    public Task LoadMoreAutomaticallyAsync() => State.LoadMoreAsync(automatic: true);

    public async Task SetSelectedMutedAsync(bool muted)
    {
        if (State.Context is not { } context) return;
        var receipt = await State.SetManyMutedAsync(State.SelectedIds.ToArray(), muted, context);
        if (receipt != null && State.IsCurrent(context)) ShowBulkMuteToast(receipt, muted, context);
        else if (State.IsCurrent(context) && State.MutationError is { } error) App.Services.GetRequiredService<ToastService>().Error(error);
    }

    public void ShowBulkMuteToast(NotificationSeriesMuteReceipt receipt, bool muted, ApiRequestContext context)
    {
        if (!_api.IsCurrentContext(context)) return;
        var toast = App.Services.GetRequiredService<ToastService>();
        var count = receipt.Count;
        var message = AppStrings.Get(muted ? "web.notifications.queries.mutedMany" : "web.notifications.queries.unmutedMany",
            new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString(AppStrings.FormatCulture) },
            $"{(muted ? "Muted" : "Unmuted")} {count} series");
        var applied = receipt.AppliedIds;
        if (!muted || applied.Length == 0) { toast.Success(message); return; }
        toast.Success(message, AppStrings.Get("web.notifications.queries.undo", fallback: "Undo"), async () =>
        {
            if (!_api.IsCurrentContext(context)) return;
            try
            {
                var results = await _api.SetManySeriesMutedAsync(context, applied, false);
                if (!_api.IsCurrentContext(context)) return;
                if (State.IsCurrent(context)) await State.ReloadAsync();
                if (_api.IsCurrentContext(context)) ShowBulkMuteToast(new(results), false, context);
            }
            catch (OperationCanceledException) when (!_api.IsCurrentContext(context)) { }
            catch (Exception ex)
            {
                if (_api.IsCurrentContext(context)) toast.Error(ex, AppStrings.Get("web.notifications.queries.unmuteManyFailed", fallback: "Couldn’t unmute the selected series"));
            }
        });
    }

    private void Update()
    {
        if (!_ready) return;
        _syncingRows = true;
        try
        {
            SeriesBusy.IsActive = State.IsLoading; SeriesBusy.Visibility = State.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            SeriesError.Text = State.Error ?? ""; SeriesErrorPanel.Visibility = State.Error != null ? Visibility.Visible : Visibility.Collapsed;
            SeriesRetry.IsEnabled = !State.IsLoading && !State.IsLoadingMore;
            MutationErrorText.Text = State.MutationError ?? ""; MutationErrorText.Visibility = State.MutationError != null ? Visibility.Visible : Visibility.Collapsed;
            // Without the Muted tab, All is the only one and the tabs are not shown.
            MutedFilter.Visibility = FilterScroll.Visibility = State.CanBulkMute ? Visibility.Visible : Visibility.Collapsed;
            SelectShown.Visibility = State.CanBulkMute ? Visibility.Visible : Visibility.Collapsed;
            SelectShown.IsEnabled = !State.SelectionLocked && State.Rows.Count > 0;
            var selected = State.Rows.Count(row => State.SelectedIds.Contains(row.Item.ContentId));
            SelectShown.IsChecked = selected == 0 ? false : selected == State.Rows.Count ? true : null;
            foreach (var button in FilterTabs.Children.OfType<Button>())
            {
                var active = button.Tag as string == (State.Options.Status == "muted" ? "muted" : "all");
                button.Style = (Style)Application.Current.Resources[active ? "SecondaryButtonStyle" : "GhostButtonStyle"];
                AutomationProperties.SetItemStatus(button, active ? AppStrings.Get("web.player.quality.selected", fallback: "Selected") : "");
            }
            SyncRows();
            SeriesEmpty.Visibility = State.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            var filtered = !string.IsNullOrWhiteSpace(State.Options.Q) || State.Options.Status == "muted";
            SeriesEmptyTitle.Text = filtered ? AppStrings.Get("web.notifications.series.noMatchFilter", fallback: "No series match. Try another search or filter.") : AppStrings.Get("web.notifications.series.empty", fallback: "No series notify you yet");
            SeriesEmptyCaption.Visibility = filtered ? Visibility.Collapsed : Visibility.Visible;
            SeriesLoadMore.Visibility = State.HasMore ? Visibility.Visible : Visibility.Collapsed;
            SeriesLoadMore.IsEnabled = State.Available && State.Error == null && !State.IsLoading && !State.IsLoadingMore && !State.IsSelectingAll;
            SeriesLoadMore.Content = State.IsLoadingMore ? AppStrings.Get("web.item.menu.loading", fallback: "Loading…") : AppStrings.Get("web.notifications.inbox.loadMore", fallback: "Load more");
            ApplyWidths(TableHeader);
            NotifiesHeader.Visibility = ActualWidth >= 640 ? Visibility.Visible : Visibility.Collapsed;
            NotifyHeader.Visibility = State.CanMute ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncingRows = false; }
    }

    private void SyncRows()
    {
        var desired = new List<UIElement>();
        foreach (var model in State.Rows)
        {
            var key = model.Item.ContentId;
            if (!_rows.TryGetValue(key, out var row)) { row = CreateRow(); _rows[key] = row; }
            row.Model = model; row.Title.Text = model.Item.Title;
            row.Explanation.Text = row.CompactExplanation.Text = model.Explanation(State.Preferences);
            row.Title.Opacity = row.Poster.Opacity = model.Notifies ? 1 : .5;
            row.Root.Opacity = State.IsLoading ? .6 : 1;
            row.Root.Background = State.SelectedIds.Contains(key) ? Brush("SurfaceBrush") : null;
            if (row.Poster.Tag as string != model.Item.PosterUrl)
            {
                row.Poster.Tag = model.Item.PosterUrl;
                row.Image.Source = (ImageSource?)new UrlToImageSourceConverter().Convert(model.Item.PosterUrl!, typeof(ImageSource), null!, "");
            }
            row.Selection.Visibility = State.CanBulkMute ? Visibility.Visible : Visibility.Collapsed;
            row.Selection.IsChecked = State.SelectedIds.Contains(key); row.Selection.IsEnabled = !State.SelectionLocked;
            AutomationProperties.SetName(row.Selection, AppStrings.Get("web.notifications.series.selectSeries", new Dictionary<string, object?> { ["title"] = model.Item.Title }, "Select " + model.Item.Title));
            row.Action.Visibility = State.CanMute ? Visibility.Visible : Visibility.Collapsed;
            row.Action.IsEnabled = !State.IsPending(key) && !State.IsBulkMutating && !State.IsLoading;
            row.Action.IsOn = !model.Muted;
            AutomationProperties.SetName(row.Action, AppStrings.Get("web.notifications.series.notifySwitch", new Dictionary<string, object?> { ["title"] = model.Item.Title }, "Notify me about new episodes of " + model.Item.Title));
            ToolTipService.SetToolTip(row.Action, model.Muted ? AppStrings.Get("web.notifications.series.unmuteHint", fallback: "Notify me about new episodes again") : AppStrings.Get("web.notifications.series.muteHint", fallback: "Stop notifying me about new episodes"));
            AutomationProperties.SetName(row.Link, model.Item.Title + ", " + row.Explanation.Text);
            ApplyWidths(row.Root);
            row.Explanation.Visibility = ActualWidth >= 640 ? Visibility.Visible : Visibility.Collapsed;
            row.CompactExplanation.Visibility = ActualWidth < 640 ? Visibility.Visible : Visibility.Collapsed;
            desired.Add(row.Root);
        }
        foreach (var key in _rows.Keys.Except(State.Rows.Select(row => row.Item.ContentId)).ToArray()) _rows.Remove(key);
        if (SeriesRows.Children.SequenceEqual(desired)) return;
        SeriesRows.Children.Clear(); foreach (var element in desired) SeriesRows.Children.Add(element);
    }

    private Row CreateRow()
    {
        var row = new Row();
        foreach (var width in new[] { new GridLength(32), new GridLength(1, GridUnitType.Star), new GridLength(240), new GridLength(48) })
            row.Root.ColumnDefinitions.Add(new() { Width = width });
        row.Selection.Click += (_, _) => State.ToggleSelected(row.Model.Item.ContentId);
        var content = new Grid { ColumnSpacing = 12 }; content.ColumnDefinitions.Add(new() { Width = new(26) }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Poster.Child = row.Image; content.Children.Add(row.Poster);
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center }; info.Children.Add(row.Title); info.Children.Add(row.CompactExplanation); Grid.SetColumn(info, 1); content.Children.Add(info);
        row.Link.Content = content; row.Link.Style = (Style)Application.Current.Resources["GhostButtonStyle"];
        row.Link.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(row.Model.Item.ContentId);
        row.Action.Style = (Style)Application.Current.Resources["WebUiToggleSwitchStyle"];
        row.Action.Toggled += async (_, _) => { if (!_syncingRows) await MuteAsync(row.Model, !row.Action.IsOn); };
        var elements = new UIElement[] { row.Selection, row.Link, row.Explanation, row.Action };
        for (var index = 0; index < elements.Length; index++) { Grid.SetColumn((FrameworkElement)elements[index], index); row.Root.Children.Add(elements[index]); }
        return row;
    }

    private async Task MuteAsync(NotificationSeries model, bool muted)
    {
        if (State.Context is not { } context || !await State.SetMutedAsync(model, muted, context) || !State.IsCurrent(context)) return;
        // The WebUI confirms only a mute, which offers Undo; an unmute shows no toast.
        if (!muted) return;
        var toast = App.Services.GetRequiredService<ToastService>();
        toast.Success(AppStrings.Get("web.notifications.queries.muted", new Dictionary<string, object?> { ["title"] = model.Item.Title }, "Muted " + model.Item.Title), AppStrings.Get("web.notifications.queries.undo", fallback: "Undo"), async () =>
        {
            if (!_api.IsCurrentContext(context)) return;
            try { await _api.SetSeriesMutedAsync(context, model.Item.ContentId, false); if (State.IsCurrent(context)) await State.ReloadAsync(); }
            catch (OperationCanceledException) when (!_api.IsCurrentContext(context)) { }
            catch (Exception ex) { if (_api.IsCurrentContext(context)) toast.Error(ex, AppStrings.Get("web.notifications.queries.unmuteFailed", fallback: "Failed to unmute the series")); }
        });
    }

    private void ApplyWidths(Grid grid)
    {
        grid.ColumnDefinitions[0].Width = new(State.CanBulkMute ? 32 : 0);
        grid.ColumnDefinitions[2].Width = new(ActualWidth >= 640 ? 240 : 0);
        grid.ColumnDefinitions[3].Width = new(State.CanMute ? 48 : 0);
    }
    private void Control_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 640; Grid.SetRow(SortCombo, narrow ? 1 : 0); Grid.SetColumn(SortCombo, narrow ? 0 : 1); Update();
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private sealed class Row
    {
        public NotificationSeries Model = new();
        public Grid Root = new() { ColumnSpacing = 12, Padding = new(12, 8, 12, 8), BorderThickness = new(0, 0, 0, 1), BorderBrush = Brush("BorderBrush") };
        public CheckBox Selection = new() { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        public Button Link = new() { MinWidth = 0, Padding = new(0), BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        public Border Poster = new() { Width = 26, Height = 39, CornerRadius = new(4), Background = Brush("SurfaceBrush") };
        public Image Image = new() { Stretch = Stretch.UniformToFill, FlowDirection = FlowDirection.LeftToRight };
        public TextBlock Title = new() { FontSize = 14, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
        public TextBlock Explanation = new() { FontSize = 13, Foreground = Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        public TextBlock CompactExplanation = new() { FontSize = 12, Foreground = Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        public ToggleSwitch Action = new() { MinWidth = 0, Width = 32, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
    }

    private void RefreshInlineChoices()
    {
        var previous = _ready; _ready = false;
        try { Localized.RefreshItems(SortCombo); }
        finally { _ready = previous; }
        Update();
    }
}
