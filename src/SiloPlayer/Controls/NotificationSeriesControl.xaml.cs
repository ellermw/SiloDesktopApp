using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
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
    public event Action? OpenPreferences;
    private readonly NotificationsApi _api;
    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private CancellationTokenSource? _search;
    private bool _ready, _opened;
    public NotificationSeriesControl()
    {
        _api = App.Services.GetRequiredService<NotificationsApi>(); State = new(_api);
        InitializeComponent(); _ready = true;
        State.PropertyChanged += (_, _) => Update();
        Unloaded += (_, _) => Close();
    }
    public async Task OpenAsync()
    {
        _opened = true;
        await State.OpenAsync();
        _ready = false; SearchBox.Text = State.Options.Q;
        ReasonCombo.SelectedItem = ReasonCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == (State.Options.Reason ?? "all"));
        SortCombo.SelectedItem = SortCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == State.Options.Sort);
        _ready = true; Update();
    }
    public void Close() { _opened = false; _search?.Cancel(); _search?.Dispose(); _search = null; State.Close(); }
    public async Task PreferencesChangedAsync() { if (_opened) await State.OpenAsync(); }
    private void Preferences_Click(Hyperlink sender, HyperlinkClickEventArgs args) => OpenPreferences?.Invoke();
    private async void Options_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && _opened) await ApplyOptionsAsync(); }
    private async void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || !_opened) return;
        _search?.Cancel(); _search?.Dispose(); var owner = _search = new();
        try { await Task.Delay(300, owner.Token); if (_opened) await ApplyOptionsAsync(); }
        catch (OperationCanceledException) when (owner.IsCancellationRequested || !_opened) { }
    }
    private Task ApplyOptionsAsync() => State.SetOptionsAsync(new(SearchBox.Text,
        (ReasonCombo.SelectedItem as ComboBoxItem)?.Tag is string reason && reason != "all" ? reason : null,
        (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "title"));
    private async void Retry_Click(object sender, RoutedEventArgs e) => await State.RetryAsync();
    private async void More_Click(object sender, RoutedEventArgs e) => await State.LoadMoreAsync();
    public Task LoadMoreAsync() => State.LoadMoreAsync();
    public Task LoadMoreAutomaticallyAsync() => State.LoadMoreAsync(automatic: true);

    private void Update()
    {
        if (!_ready) return;
        SeriesBusy.IsActive = State.IsLoading; SeriesBusy.Visibility = State.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SeriesError.Text = State.Error ?? ""; SeriesErrorPanel.Visibility = State.Error != null ? Visibility.Visible : Visibility.Collapsed;
        SeriesRetry.IsEnabled = !State.IsLoading && !State.IsLoadingMore;
        MutationErrorText.Text = State.MutationError ?? ""; MutationErrorText.Visibility = State.MutationError != null ? Visibility.Visible : Visibility.Collapsed;
        MuteIntro.Text = State.CanMute ? ", or mute one series." : ".";
        ActiveRows.Opacity = InactiveRows.Opacity = State.IsLoading ? .6 : 1;
        SyncRows(ActiveRows, State.Active); SyncRows(InactiveRows, State.Inactive);
        InactiveSection.Visibility = State.ShowInactive && State.Inactive.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SeriesEmpty.Visibility = State.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        var filtered = !string.IsNullOrWhiteSpace(State.Options.Q) || State.Options.Reason != null;
        SeriesEmptyTitle.Text = filtered ? "No series match. Try another search or reason." : "No series notify you yet";
        SeriesEmptyCaption.Visibility = filtered ? Visibility.Collapsed : Visibility.Visible;
        SeriesLoadMore.Visibility = State.HasMore ? Visibility.Visible : Visibility.Collapsed;
        SeriesLoadMore.IsEnabled = !State.IsLoading && !State.IsLoadingMore;
        SeriesLoadMore.Content = State.IsLoadingMore ? "Loading…" : "Load more";
        foreach (var stale in _rows.Keys.Except(State.Active.Concat(State.Inactive).Select(row => row.Item.ContentId)).ToArray()) _rows.Remove(stale);
    }
    private void SyncRows(StackPanel host, IEnumerable<NotificationSeries> models)
    {
        var desired = new List<UIElement>();
        foreach (var model in models)
        {
            var key = model.Item.ContentId;
            if (!_rows.TryGetValue(key, out var row)) { row = CreateRow(); _rows[key] = row; }
            row.Model = model; row.Title.Text = model.Item.Title; row.Explanation.Text = model.Explanation(State.Preferences);
            row.Title.Opacity = row.Poster.Opacity = model.Notifies ? 1 : .5;
            if (row.Poster.Tag as string != model.Item.PosterUrl)
            { row.Poster.Tag = model.Item.PosterUrl; row.Image.Source = (ImageSource?)new UrlToImageSourceConverter().Convert(model.Item.PosterUrl!, typeof(ImageSource), null!, ""); }
            row.Action.Visibility = State.CanMute ? Visibility.Visible : Visibility.Collapsed;
            row.Action.IsEnabled = !State.IsPending(key); row.Label.Text = model.Muted ? "Unmute" : "Mute";
            row.Icon.Child = WebUiIcon.Create(model.Muted ? "bell" : "bell-off", 16);
            AutomationProperties.SetName(row.Action, $"{row.Label.Text} {model.Item.Title}");
            ToolTipService.SetToolTip(row.Action, model.Muted ? "Notify me about new episodes again" : "Stop notifying me about new episodes");
            AutomationProperties.SetName(row.Link, model.Item.Title + ", " + row.Explanation.Text);
            var reasons = model.Reasons.Labels(model.ActiveReasons, model.Muted).ToArray();
            var signature = string.Join("|", reasons.Select(reason => reason.Label + reason.Active));
            if (row.Reasons.Tag as string != signature)
            {
                row.Reasons.Tag = signature; row.Reasons.Children.Clear();
                foreach (var reason in reasons)
                {
                    var text = new TextBlock { Text = reason.Label, FontSize = 10, FontWeight = FontWeights.Medium,
                        TextDecorations = reason.Active ? Windows.UI.Text.TextDecorations.None : Windows.UI.Text.TextDecorations.Strikethrough, Foreground = Brush("SecondaryTextBrush") };
                    AutomationProperties.SetName(text, reason.Label + (reason.Active ? "" : " (off)"));
                    row.Reasons.Children.Add(new Border { Child = text, Padding = new(8, 2, 8, 2), CornerRadius = new(10), BorderThickness = new(reason.Active ? 0 : 1), BorderBrush = Brush("BorderBrush"), Background = reason.Active ? Brush("SurfaceBrush") : null });
                }
            }
            row.Label.Visibility = ActualWidth < 640 ? Visibility.Collapsed : Visibility.Visible;
            desired.Add(row.Root);
        }
        if (!host.Children.SequenceEqual(desired))
        {
            host.Children.Clear();
            foreach (var element in desired) { if (element is FrameworkElement { Parent: Panel previous }) previous.Children.Remove(element); host.Children.Add(element); }
        }
    }
    private Row CreateRow()
    {
        var row = new Row();
        row.Root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.Root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Link.Style = (Style)Application.Current.Resources["GhostButtonStyle"]; row.Link.Padding = new(12); row.Link.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.Link.VerticalContentAlignment = VerticalAlignment.Top;
        var content = new Grid { ColumnSpacing = 12 }; content.ColumnDefinitions.Add(new() { Width = new(44) }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Poster.Child = row.Image; content.Children.Add(row.Poster);
        var info = new StackPanel { Spacing = 2 }; info.Children.Add(row.Title); info.Children.Add(row.Explanation); info.Children.Add(row.Reasons); Grid.SetColumn(info, 1); content.Children.Add(info); row.Link.Content = content;
        row.Link.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(row.Model.Item.ContentId);
        var actionContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; actionContent.Children.Add(row.Icon); actionContent.Children.Add(row.Label);
        row.Action.Content = actionContent; row.Action.Style = (Style)Application.Current.Resources["GhostButtonStyle"]; Grid.SetColumn(row.Action, 1);
        row.Action.Click += async (_, _) => await MuteAsync(row.Model, !row.Model.Muted);
        row.Root.Children.Add(row.Link); row.Root.Children.Add(row.Action); return row;
    }
    private async Task MuteAsync(NotificationSeries model, bool muted)
    {
        if (State.Context is not { } context) return;
        if (!await State.SetMutedAsync(model, muted, context)) return;
        var toast = App.Services.GetRequiredService<ToastService>();
        if (muted) toast.Success($"Muted {model.Item.Title}", "Undo", async () =>
        {
            if (!_api.IsCurrentContext(context)) { toast.Error("Switch back to the profile that muted this to undo it"); return; }
            try
            {
                await _api.SetSeriesMutedAsync(context, model.Item.ContentId, false);
                if (State.IsCurrent(context)) await State.ReloadAsync();
            }
            catch (OperationCanceledException) when (!_api.IsCurrentContext(context)) { }
            catch (Exception ex) { if (_api.IsCurrentContext(context)) toast.Error(ex, "Failed to unmute the series"); }
        });
    }
    private void Control_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 640; Grid.SetRow(FilterControls, narrow ? 1 : 0); Grid.SetColumn(FilterControls, narrow ? 0 : 1);
        foreach (var row in _rows.Values) row.Label.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private sealed class Row
    {
        public NotificationSeries Model = new();
        public Grid Root = new() { CornerRadius = new(12), ColumnSpacing = 4 };
        public Button Link = new() { MinWidth = 0, BorderThickness = new(0) };
        public Border Poster = new() { Width = 44, Height = 64, CornerRadius = new(10), Background = Brush("SurfaceBrush") };
        public Image Image = new() { Stretch = Stretch.UniformToFill };
        public TextBlock Title = new() { FontSize = 14, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
        public TextBlock Explanation = new() { FontSize = 14, Foreground = Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        public WrapPanel Reasons = new() { HorizontalSpacing = 4, VerticalSpacing = 4, Margin = new(0, 4, 0, 0) };
        public Button Action = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), Padding = new(8), MinWidth = 32 };
        public Border Icon = new(); public TextBlock Label = new() { FontSize = 14 };
    }
}
