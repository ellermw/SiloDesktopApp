using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Converters;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class NotificationsPage
{
    private readonly HashSet<string> _expandedInbox = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InboxRow> _inboxRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (StackPanel Root, TextBlock Heading, StackPanel Rows)> _inboxSections = new(StringComparer.Ordinal);
    private bool _renderingInbox;

    private async void Inbox_Click(object sender, RoutedEventArgs e)
    {
        _showSeries = false; SeriesView.Close(); UpdateVisuals();
        await ViewModel.LoadCommand.ExecuteAsync(null); UpdateVisuals();
    }
    private void InboxSelect_Click(object sender, RoutedEventArgs e) => ViewModel.SetInboxSelecting(!ViewModel.IsSelectingInbox);
    private async void SelectionMarkRead_Click(object sender, RoutedEventArgs e)
        => await ViewModel.MarkInboxReadAsync(ViewModel.SelectedInboxEntries.SelectMany(entry => entry.Notifications).ToArray());
    private async void SelectionMute_Click(object sender, RoutedEventArgs e)
    {
        if (_showSeries) { await SeriesView.SetSelectedMutedAsync(true); return; }
        if (ViewModel.InboxContext is not { } context) return;
        var ids = ViewModel.SelectedInboxEntries.Where(entry => entry.SeriesId != null).Select(entry => entry.SeriesId!).ToArray();
        var receipt = await ViewModel.SetInboxSeriesMutedAsync(ids, true, context);
        if (receipt != null && IsLoaded) SeriesView.ShowBulkMuteToast(receipt, true, context);
        else if (IsLoaded && _client?.IsCurrentContext(context) == true && ViewModel.SelectionErrorMessage is { } error)
            App.Services.GetRequiredService<ToastService>().Error(error);
    }
    private async void SelectionAllMatching_Click(object sender, RoutedEventArgs e) => await SeriesView.State.SelectAllMatchingAsync();
    private async void SelectionUnmute_Click(object sender, RoutedEventArgs e) => await SeriesView.SetSelectedMutedAsync(false);
    private void SelectionClear_Click(object sender, RoutedEventArgs e)
    {
        if (_showSeries) SeriesView.State.ClearSelection(); else ViewModel.ClearInboxSelection();
    }

    private void UpdateSelectionBar()
    {
        var entries = ViewModel.SelectedInboxEntries;
        var count = _showSeries ? SeriesView.State.SelectedIds.Count : entries.Length;
        SelectionBar.Visibility = count > 0 && (_showSeries || ViewModel.IsSelectingInbox) ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(SelectionBar, AppStrings.Get(_showSeries ? "web.notifications.selection.seriesLabel" : "web.notifications.selection.inboxLabel", fallback: _showSeries ? "Selected series" : "Selected notifications"));
        SelectionSummary.Text = CountText("web.notifications.selection.count", count, $"{count} selected");
        if (!_showSeries)
        {
            var notifications = entries.Sum(entry => entry.Notifications.Count);
            SelectionSummary.Text += " " + CountText("web.notifications.selection.notificationCount", notifications, notifications == 1 ? "· 1 notification" : $"· {notifications} notifications");
        }
        SelectionMarkRead.Visibility = _showSeries ? Visibility.Collapsed : Visibility.Visible;
        SelectionMarkRead.IsEnabled = !ViewModel.IsMarkingSelectedRead && entries.Any(entry => entry.Notifications.Any(row => row.IsUnread));
        SelectionMute.Visibility = (_showSeries ? SeriesView.State.CanBulkMute : ViewModel.CanBulkMuteSeries) ? Visibility.Visible : Visibility.Collapsed;
        SelectionMute.IsEnabled = _showSeries ? !SeriesView.State.BulkLocked : !ViewModel.IsMutingSelectedSeries && entries.Any(entry => entry.SeriesId != null);
        SelectionUnmute.Visibility = _showSeries && SeriesView.State.CanBulkMute ? Visibility.Visible : Visibility.Collapsed;
        SelectionUnmute.IsEnabled = !SeriesView.State.BulkLocked;
        var series = SeriesView.State;
        SelectionAllMatching.Visibility = _showSeries && count > 0 && series.CanBulkMute &&
            (series.HasMore || series.Rows.Any(row => !series.SelectedIds.Contains(row.Item.ContentId))) ? Visibility.Visible : Visibility.Collapsed;
        SelectionAllMatching.IsEnabled = !series.BulkLocked && !series.IsLoadingMore && series.Error == null;
        SelectionAllBusy.IsActive = _showSeries && series.IsSelectingAll;
        SelectionAllBusy.Visibility = SelectionAllBusy.IsActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderInbox()
    {
        if (_renderingInbox) return;
        _renderingInbox = true;
        try
        {
            var sections = ViewModel.InboxSections;
            var keys = sections.SelectMany(section => section.Entries).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _inboxRows.Keys.Where(key => !keys.Contains(key)).ToArray()) _inboxRows.Remove(key);
            var sectionRoots = new List<UIElement>();
            foreach (var section in sections)
            {
                if (!_inboxSections.TryGetValue(section.Day, out var host))
                {
                    host = (new StackPanel { Spacing = 4 }, new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = InboxBrush("SecondaryTextBrush"), Margin = new(12, 16, 12, 4) }, new StackPanel { Spacing = 4 });
                    AutomationProperties.SetHeadingLevel(host.Heading, AutomationHeadingLevel.Level2);
                    host.Root.Children.Add(host.Heading); host.Root.Children.Add(host.Rows); _inboxSections[section.Day] = host;
                }
                var (key, fallback) = section.Day switch
                {
                    "today" => ("dayToday", "Today"), "yesterday" => ("dayYesterday", "Yesterday"),
                    "week" => ("dayWeek", "Last 7 days"), _ => ("dayOlder", "Older")
                };
                host.Heading.Text = AppStrings.Get("web.notifications.inbox." + key, fallback: fallback);
                var rows = new List<UIElement>();
                foreach (var entry in section.Entries)
                {
                    var grouped = entry.Notifications.Count > 1;
                    if (!_inboxRows.TryGetValue(entry.Key, out var row) || (row.Expander != null) != grouped)
                    { row = CreateInboxRow(entry, grouped); _inboxRows[entry.Key] = row; }
                    row.Entry = entry; UpdateInboxRow(row); rows.Add(row.Root);
                }
                SyncInboxChildren(host.Rows, rows); sectionRoots.Add(host.Root);
            }
            SyncInboxChildren(InboxGroupsHost, sectionRoots);
        }
        finally { _renderingInbox = false; }
    }

    private InboxRow CreateInboxRow(NotificationInboxEntry entry, bool grouped)
    {
        var row = new InboxRow { Entry = entry };
        row.Layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Selection.Click += (_, _) => ViewModel.ToggleInboxSelection(row.Entry.Key);
        row.Layout.Children.Add(row.Selection);
        row.Summary.ColumnDefinitions.Add(new() { Width = new(48) }); row.Summary.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.Summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Poster.Child = row.Image; row.Summary.Children.Add(row.Poster);
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new Grid { ColumnSpacing = 8 };
        titleLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        titleLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titleLine.Children.Add(row.Title);
        row.RequestTagBody.Children.Add(WebUiIcon.Create("film", 12)); row.RequestTagBody.Children.Add(row.RequestTagText);
        row.RequestTag.Child = row.RequestTagBody; Grid.SetColumn(row.RequestTag, 1); titleLine.Children.Add(row.RequestTag);
        info.Children.Add(titleLine); info.Children.Add(row.Description); info.Children.Add(row.Badges); Grid.SetColumn(info, 1); row.Summary.Children.Add(info);
        var trailing = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center }; trailing.Children.Add(row.Time); trailing.Children.Add(row.Unread); Grid.SetColumn(trailing, 2); row.Summary.Children.Add(trailing);
        FrameworkElement body;
        if (grouped)
        {
            row.Expander = new Expander { Header = row.Summary, Content = row.Episodes, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0), BorderThickness = new(0) };
            row.Expander.Expanding += (_, _) => { if (!_renderingInbox) { _expandedInbox.Add(row.Entry.Key); RenderInbox(); } };
            row.Expander.Collapsed += (_, _) => { if (!_renderingInbox) { _expandedInbox.Remove(row.Entry.Key); RenderInbox(); } };
            row.SelectionLink = new Button { Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Padding = new(0), BorderThickness = new(0) };
            row.SelectionLink.Click += (_, _) => ViewModel.ToggleInboxSelection(row.Entry.Key);
            var groupBody = new Grid(); groupBody.Children.Add(row.Expander); groupBody.Children.Add(row.SelectionLink); body = groupBody;
        }
        else
        {
            var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            // The ghost button's rounded corners clip its content: padding with the equal negative margin keeps the row where it was and gives its edges room.
            row.Link = new Button { Content = row.Summary, Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Padding = new(6, 2, 6, 2), Margin = new(-6, -2, -6, -2), CornerRadius = new(4), BorderThickness = new(0) };
            row.Link.Click += (_, _) => { if (ViewModel.IsSelectingInbox) ViewModel.ToggleInboxSelection(row.Entry.Key); else NavigateInboxNotification(row.Entry.Latest); };
            content.Children.Add(row.Link); Grid.SetColumn(row.MarkRead, 1); content.Children.Add(row.MarkRead);
            row.MarkRead.Click += async (_, _) => await ViewModel.MarkReadCommand.ExecuteAsync(row.Entry.Latest);
            body = content;
        }
        Grid.SetColumn(body, 1); row.Layout.Children.Add(body); row.Root.Child = row.Layout;
        return row;
    }

    private void UpdateInboxRow(InboxRow row)
    {
        var entry = row.Entry; var notification = entry.Latest;
        row.Root.DataContext = entry;
        var unread = entry.Notifications.Count(item => item.IsUnread);
        var selected = ViewModel.SelectedInboxKeys.Contains(entry.Key);
        row.Selection.Visibility = ViewModel.IsSelectingInbox ? Visibility.Visible : Visibility.Collapsed;
        row.Selection.IsChecked = selected;
        AutomationProperties.SetName(row.Selection, AppStrings.Get("web.notifications.selection.select", fallback: "Select") + " " + NotificationTitle(notification));
        row.Root.Background = selected || unread > 0 ? InboxBrush("SurfaceBrush") : null;
        row.Title.Text = NotificationTitle(notification);
        row.Title.FontWeight = unread > 0 ? FontWeights.SemiBold : FontWeights.Medium;
        row.Description.Text = entry.Notifications.Count > 1 ? EpisodeRange(entry.Notifications) : NotificationSubtitle(notification);
        row.Time.Text = NotificationTime(notification);
        row.Unread.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(row.Unread, AppStrings.Get("web.notifications.inbox.unread", fallback: "Unread"));
        if (row.Poster.Tag as string != notification.PosterUrl)
        {
            row.Poster.Tag = notification.PosterUrl;
            row.Image.Source = (ImageSource?)new UrlToImageSourceConverter().Convert(notification.PosterUrl!, typeof(ImageSource), null!, "");
        }
        row.MarkRead.Visibility = unread > 0 && !ViewModel.IsSelectingInbox ? Visibility.Visible : Visibility.Collapsed;
        row.MarkRead.IsEnabled = !ViewModel.IsMarkingSelectedRead;
        AutomationProperties.SetName(row.MarkRead, AppStrings.Get("web.notifications.inbox.markRead", fallback: "Mark as read") + " " + row.Title.Text);
        ToolTipService.SetToolTip(row.MarkRead, AppStrings.Get("web.notifications.inbox.markRead", fallback: "Mark as read"));
        row.RequestTagText.Text = AppStrings.Get("web.notifications.inbox.requestTag", fallback: "Request");
        row.RequestTag.Visibility = notification.Type.StartsWith("request.", StringComparison.Ordinal) && ActualWidth >= 640 ? Visibility.Visible : Visibility.Collapsed;
        row.Badges.Text = entry.Notifications.Count > 1 && unread > 0 ? CountText("web.notifications.inbox.newCount", unread, $"{unread} new") : "";
        row.Badges.Visibility = row.Badges.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (row.Link != null) AutomationProperties.SetName(row.Link, row.Title.Text + ", " + row.Description.Text + ", " + row.Time.Text);
        if (row.Expander == null) return;
        AutomationProperties.SetName(row.Expander, row.Title.Text + ", " + row.Description.Text);
        AutomationProperties.SetName(row.SelectionLink!, row.Title.Text + ", " + row.Description.Text);
        if (ViewModel.IsSelectingInbox)
        {
            row.Expander.Header = null;
            row.SelectionLink!.Content = row.Summary;
        }
        else
        {
            row.SelectionLink!.Content = null;
            row.Expander.Header = row.Summary;
        }
        row.Expander.Visibility = ViewModel.IsSelectingInbox ? Visibility.Collapsed : Visibility.Visible;
        row.SelectionLink.Visibility = ViewModel.IsSelectingInbox ? Visibility.Visible : Visibility.Collapsed;
        row.Expander.IsExpanded = _expandedInbox.Contains(entry.Key);
        row.Episodes.Visibility = ViewModel.IsSelectingInbox ? Visibility.Collapsed : Visibility.Visible;
        row.Episodes.Margin = AppStrings.IsRightToLeft ? new(0, 0, 36, 4) : new(36, 0, 0, 4);
        var signature = string.Join("|", entry.Notifications.Select(item => item.Id + item.ReadAt + NotificationSubtitle(item) + NotificationTime(item)));
        if (!row.Expander.IsExpanded || row.EpisodeSignature == signature) return;
        row.EpisodeSignature = signature; row.Episodes.Children.Clear();
        foreach (var episode in entry.Notifications)
        {
            // Rows survive reset loads, so handlers must resolve the current instance.
            var episodeId = episode.Id;
            var line = new Grid { ColumnSpacing = 8 }; line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = new TextBlock { Text = NotificationSubtitle(episode), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13, FontWeight = episode.IsUnread ? FontWeights.SemiBold : FontWeights.Medium };
            var link = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            link.Click += (_, _) =>
            {
                if (row.Entry.Notifications.FirstOrDefault(item => item.Id == episodeId) is { } current)
                    NavigateInboxNotification(current);
            };
            line.Children.Add(link);
            var time = new TextBlock { Text = NotificationTime(episode), FontSize = 12, Foreground = InboxBrush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(time, 1); line.Children.Add(time);
            var mark = new Button { Content = WebUiIcon.Create("check", 16), Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Visibility = episode.IsUnread ? Visibility.Visible : Visibility.Collapsed, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            AutomationProperties.SetName(mark, AppStrings.Get("web.notifications.inbox.markRead", fallback: "Mark as read") + " " + text.Text);
            mark.Click += async (_, _) =>
            {
                if (row.Entry.Notifications.FirstOrDefault(item => item.Id == episodeId) is { } current)
                    await ViewModel.MarkReadCommand.ExecuteAsync(current);
            };
            Grid.SetColumn(mark, 2); line.Children.Add(mark);
            row.Episodes.Children.Add(line);
        }
    }

    private void NavigateInboxNotification(AppNotification notification)
    {
        if (notification.IsUnread) _ = ViewModel.MarkReadCommand.ExecuteAsync(notification);
        var navigation = App.Services.GetRequiredService<NavigationService>();
        if (NotificationNavigation.IsDestinationDisabledNotice(notification)) { navigation.Navigate<SettingsPage>("NotificationsSettings"); return; }
        var destination = NotificationNavigation.Resolve(notification);
        if (destination?.ContentId is string contentId) navigation.Navigate<ItemDetailPage>(contentId);
        else if (destination is { MediaType: string mediaType, TmdbId: int tmdbId }) navigation.Navigate<RequestDetailPage>(new RequestDetailNavigation(mediaType, tmdbId));
    }

    private static string EpisodeRange(List<AppNotification> notifications)
    {
        var sorted = notifications.OrderBy(item => item.SeasonNumber ?? 0).ThenBy(item => item.EpisodeNumber ?? 0).ToArray();
        var first = sorted[0]; var last = sorted[^1];
        if (!first.SeasonNumber.HasValue || !first.EpisodeNumber.HasValue || !last.SeasonNumber.HasValue || !last.EpisodeNumber.HasValue)
            return CountText("web.notifications.inbox.episodeCount", notifications.Count, $"{notifications.Count} new episodes");
        var firstCode = EpisodeCode(first); var lastCode = EpisodeCode(last);
        var args = new Dictionary<string, object?> { ["first"] = firstCode, ["last"] = lastCode, ["title"] = last.EpisodeTitle };
        return string.IsNullOrWhiteSpace(last.EpisodeTitle)
            ? AppStrings.Get("web.notifications.inbox.episodeRange", args, $"{firstCode} – {lastCode}")
            : AppStrings.Get("web.notifications.inbox.episodeRangeLatest", args, $"{firstCode} – {lastCode} · latest “{last.EpisodeTitle}”");
    }
    private static string EpisodeCode(AppNotification notification) => AppStrings.Get("web.notifications.queries.episodeCode",
        new Dictionary<string, object?> { ["season"] = notification.SeasonNumber, ["episode"] = notification.EpisodeNumber }, $"S{notification.SeasonNumber}E{notification.EpisodeNumber}");
    private static string CountText(string key, int count, string fallback) => AppStrings.Get(key,
        new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString(AppStrings.FormatCulture) }, fallback);
    private static Brush InboxBrush(string key) => (Brush)Application.Current.Resources[key];
    private static void SyncInboxChildren(StackPanel panel, List<UIElement> children)
    {
        if (panel.Children.SequenceEqual(children)) return;
        panel.Children.Clear();
        foreach (var child in children)
        {
            if (child is FrameworkElement { Parent: Panel previous } && previous != panel) previous.Children.Remove(child);
            panel.Children.Add(child);
        }
    }

    private sealed class InboxRow
    {
        public NotificationInboxEntry Entry = null!;
        public Border Root = new() { Name = "NotificationRowRoot", CornerRadius = new(12), Padding = new(12, 8, 12, 8) };
        public Grid Layout = new() { ColumnSpacing = 8 };
        public CheckBox Selection = new() { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        public Grid Summary = new() { ColumnSpacing = 12, MinHeight = 70 };
        public Border Poster = new() { Width = 44, Height = 64, CornerRadius = new(8), Background = InboxBrush("SurfaceBrush") };
        public Image Image = new() { Stretch = Stretch.UniformToFill, FlowDirection = FlowDirection.LeftToRight };
        public Border RequestTag = new() { CornerRadius = new(12), Padding = new(8, 2, 8, 2), Background = InboxBrush("SurfaceBrush"), VerticalAlignment = VerticalAlignment.Center };
        public StackPanel RequestTagBody = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        public TextBlock RequestTagText = new() { FontSize = 11, Foreground = InboxBrush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center };
        public TextBlock Title = new() { Name = "NotificationTitleText", FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        public TextBlock Description = new() { Name = "NotificationSubtitleText", FontSize = 13, Foreground = InboxBrush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        public TextBlock Time = new() { Name = "NotificationTimeText", FontSize = 12, Foreground = InboxBrush("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Right };
        public TextBlock Badges = new() { FontSize = 11, Foreground = InboxBrush("SecondaryTextBrush") };
        public Microsoft.UI.Xaml.Shapes.Ellipse Unread = new() { Width = 8, Height = 8, Fill = InboxBrush("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right };
        public Button MarkRead = new() { Name = "InlineMarkReadButton", Content = WebUiIcon.Create("check", 16), Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"], VerticalAlignment = VerticalAlignment.Center };
        public Expander? Expander;
        public Button? Link;
        public Button? SelectionLink;
        public StackPanel Episodes = new() { Spacing = 4, Margin = new(36, 0, 0, 4) };
        public string? EpisodeSignature;
    }
}
