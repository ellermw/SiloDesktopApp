using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    /// <summary>Series groups the viewer opened or closed; a series with work outstanding opens by default.</summary>
    private readonly Dictionary<string, bool> _openSeries = new(StringComparer.Ordinal);

    private string DeviceName(string id) => Rows("devices").FirstOrDefault(row => row.Text("device_id") == id) is { } device
        ? device.Text("device_name", T("web.admin.users.detail.common.unnamedDevice")) : T("web.admin.users.detail.common.unknownDevice");
    private string ProfileName(string id) => Rows("profiles").FirstOrDefault(row => row.Text("id") == id)?.Text("name") is { Length: > 0 } name ? name : id;
    private bool IsAndroid(string deviceId) => Rows("devices").FirstOrDefault(item => item.Text("device_id") == deviceId)?.Text("device_platform").Contains("android", StringComparison.OrdinalIgnoreCase) == true;

    private FrameworkElement Message(string text) => Surface(new Border { Child = Centered(text), Padding = new(20, 32, 20, 32) });
    private static TextBlock Centered(string text)
    {
        var block = Text(text, 14, true); block.TextAlignment = TextAlignment.Center; block.HorizontalAlignment = HorizontalAlignment.Center; return block;
    }

    private FrameworkElement DownloadsTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        if (!Feature("account_downloads")) { panel.Children.Add(Message(T("web.admin.users.detail.downloads.unavailable"))); return panel; }
        if (!_rows.ContainsKey("downloads") || !_rows.ContainsKey("subscriptions"))
        {
            var waiting = !_loadErrors.ContainsKey("downloads") && !_loadErrors.ContainsKey("subscriptions") &&
                (AwaitingRead("downloads") || AwaitingRead("subscriptions"));
            panel.Children.Add(Message(waiting ? "Loading Users…" : T("web.admin.users.detail.downloads.failed")));
            return panel;
        }
        var downloads = Rows("downloads");
        var monitors = Rows("subscriptions");
        var inProgressStatuses = new[] { "preparing", "ready", "downloading" };
        var onDevice = downloads.Count(row => !IsAndroid(row.Text("device_id")) && row.Text("status") == "completed");
        var inProgress = downloads.Count(row => !IsAndroid(row.Text("device_id")) && inProgressStatuses.Contains(row.Text("status")));
        var anyAndroid = downloads.Any(row => IsAndroid(row.Text("device_id")));
        var requested = downloads.Count(row => IsAndroid(row.Text("device_id")) && row.Text("status") is "ready" or "downloading");
        var tiles = new List<Border>
        {
            StatTile(T("web.admin.users.detail.downloads.status.onDevice"), onDevice.ToString(CultureInfo.CurrentCulture), detail: T("web.admin.users.detail.downloads.stats.onDeviceDetail")),
            StatTile(T("web.admin.users.detail.downloads.inProgress"), inProgress.ToString(CultureInfo.CurrentCulture), detail: T("web.admin.users.detail.downloads.stats.inProgressDetail")),
        };
        if (anyAndroid) tiles.Add(StatTile(T("web.admin.users.detail.downloads.requestedOnAndroid"), requested.ToString(CultureInfo.CurrentCulture), detail: T("web.admin.users.detail.downloads.stats.requestedDetail")));
        tiles.Add(StatTile(T("web.admin.users.detail.downloads.monitoredSeries"), monitors.Count(row => row.Flag("active")).ToString(CultureInfo.CurrentCulture), detail: T("web.admin.users.detail.downloads.stats.monitoredDetail")));
        panel.Children.Add(StatStrip(tiles));
        if (_rows["downloads"] is { Complete: false } || _rows["subscriptions"] is { Complete: false })
        {
            var note = Text(T("admin.native.users.downloads.partial"), 13);
            panel.Children.Add(new Border { Child = note, Padding = new(12, 8, 12, 8), CornerRadius = new(AdminLibrariesUi.RadiusSm), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.6), Background = Tint("MutedBrush", 0.3) });
        }

        // Filters
        var deviceIds = downloads.Select(row => row.Text("device_id")).Distinct().ToList();
        var filters = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        var device = Select(T("web.admin.users.detail.downloads.devicesLabel"), new[] { ("", T("web.admin.users.detail.downloads.allDevices")) }.Concat(deviceIds.Select(id => (id, DeviceName(id)))), _downloadDevice,
            value => { _downloadDevice = value; Render(); }, 200);
        var profile = Select(T("web.admin.users.detail.common.profileFilter"), new[] { ("", T("web.admin.users.detail.common.allProfiles")) }.Concat(Rows("profiles").Select(row => (row.Text("id"), row.Text("name")))), _downloadProfile,
            value => { _downloadProfile = value; Render(); }, 176);
        var status = Select(T("web.admin.users.detail.downloads.statusFilter"), new[]
        {
            ("", T("web.admin.users.detail.downloads.filterAll")), ("on_device", T("web.admin.users.detail.downloads.status.onDevice")), ("in_progress", T("web.admin.users.detail.downloads.inProgress")),
            ("requested", T("web.admin.users.detail.downloads.requestedOnAndroid")), ("failed", T("web.admin.users.detail.downloads.status.failed")), ("revoked", T("web.admin.users.detail.downloads.status.revoked")),
        }, _downloadStatus, value => { _downloadStatus = value; Render(); }, 200);
        filters.Children.Add(device); filters.Children.Add(profile); filters.Children.Add(status);
        if (downloads.Count > 0) panel.Children.Add(filters);

        bool Matches(JsonObject row) => (_downloadDevice.Length == 0 || row.Text("device_id") == _downloadDevice) && (_downloadProfile.Length == 0 || row.Text("profile_id") == _downloadProfile);
        var shown = downloads.Where(row => Matches(row) && AdminUsersPresentation.MatchesDownload(row, _downloadStatus, IsAndroid(row.Text("device_id")))).ToList();
        if (downloads.Count == 0) panel.Children.Add(Message(T("web.admin.users.detail.downloads.empty")));
        else if (shown.Count == 0) panel.Children.Add(Message(T("web.admin.users.detail.downloads.noMatch")));
        foreach (var deviceRows in shown.GroupBy(row => row.Text("device_id"))) panel.Children.Add(DeviceDownloadsCard(deviceRows.Key, deviceRows.ToList(), downloads.Where(row => row.Text("device_id") == deviceRows.Key).ToList(), monitors));
        if (_rows["downloads"] is { Complete: false } state)
        {
            var more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
            more.Children.Add(Text(T("admin.native.users.downloads.partial"), 13, true));
            more.Children.Add(Action(T("admin.native.users.downloads.loadMore"), () => MoreAsync("downloads"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
            panel.Children.Add(more);
        }
        panel.Children.Add(MonitoredSeriesCard(monitors.Where(Matches).ToList(), _downloadDevice.Length > 0 || _downloadProfile.Length > 0));
        panel.Children.Add(Wire(ActionButton(T("admin.native.users.downloads.open"), "arrow-up-right"), () =>
        {
            var request = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["user_id"] = _review!.User.Text("id") });
            if (!AdminShellPage.Open(request)) _notice.Text = "Download Management is not registered yet. Scope: " + request.Href;
            return Task.CompletedTask;
        }));
        return panel;
    }

    private static (string Label, Windows.UI.Color Tone, bool Dot) DownloadBadge(string status, bool android) => status switch
    {
        "completed" => (T("web.admin.users.detail.downloads.status.onDevice"), AdminMonitorUi.ToneColor(ActivityTone.Success), true),
        "ready" or "downloading" when android => (T("web.admin.users.detail.downloads.status.requested"), AdminMonitorUi.Amber400, false),
        "ready" => (T("web.admin.users.detail.downloads.status.waitingForDevice"), AdminMonitorUi.Muted, false),
        "downloading" => (T("web.admin.users.detail.downloads.status.downloading"), AdminMonitorUi.Amber400, false),
        "preparing" => (T("web.admin.users.detail.downloads.status.serverPreparing"), AdminMonitorUi.Amber400, false),
        "failed" => (T("web.admin.users.detail.downloads.status.failed"), AdminMonitorUi.Destructive, false),
        "revoked" => (T("web.admin.users.detail.downloads.status.revoked"), AdminMonitorUi.Muted, false),
        _ => (Label(status), AdminMonitorUi.Muted, false),
    };

    private static FrameworkElement DownloadStatusBadge(string status, bool android)
    {
        var (label, tone, dot) = DownloadBadge(status, android);
        return TintedBadge(label, tone, pill: true, dot: dot);
    }

    private static string DownloadQuality(JsonObject row)
    {
        var effective = row.Text("effective_quality", row.Text("quality"));
        return effective.Length == 0 || effective is "original" ? T("web.admin.users.detail.downloads.quality.original") : effective is "server_prepared" ? T("web.admin.users.detail.downloads.quality.serverPrepared") : effective;
    }

    private static string EpisodeLabel(JsonObject row)
    {
        if (row["episode"] is not JsonObject episode) return "";
        long? Number(string key) => episode[key] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
        return string.Join(" · ", new[] { EpisodeCode(Number("season_number"), Number("episode_number")), episode.Text("title") }.Where(part => part.Length > 0));
    }

    private FrameworkElement DeviceDownloadsCard(string deviceId, List<JsonObject> rows, List<JsonObject> all, IReadOnlyList<JsonObject> monitors)
    {
        var device = Rows("devices").FirstOrDefault(item => item.Text("device_id") == deviceId);
        var android = IsAndroid(deviceId);
        var profiles = all.Select(row => ProfileName(row.Text("profile_id"))).Distinct().ToArray();
        var meta = new List<string>();
        if (device != null)
        {
            meta.Add(PlatformLabel(device.Text("device_platform")));
            if (profiles.Length > 0) meta.Add(T("web.admin.users.detail.downloads.device.profiles", ("names", string.Join(", ", profiles))));
            meta.Add(T("web.admin.users.detail.downloads.device.lastSeen", ("when", LastSeen(device.Text("last_seen_at", device.Text("last_updated"))))));
        }
        else if (profiles.Length > 0) meta.Add(T("web.admin.users.detail.downloads.device.profiles", ("names", string.Join(", ", profiles))));
        var onDevice = all.Count(row => row.Text("status") == "completed");
        var inProgress = all.Count(row => row.Text("status") is "preparing" or "ready" or "downloading");
        var requested = all.Count(row => row.Text("status") is "ready" or "downloading");
        string Counted(string key, int count) => T("web.admin.users.detail.downloads.count." + key, ("count", count), ("count_text", count));
        var totals = android
            ? new List<string> { onDevice > 0 ? Counted("onDevice", onDevice) : "", Counted("requested", requested) }
            : [Counted("onDevice", onDevice), Counted("inProgress", inProgress)];
        totals.Add(Bytes(all.Where(row => row.Text("status") != "revoked").Sum(row => row["file_size"] is JsonValue value && value.TryGetValue<double>(out var size) ? size : 0)));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        if (!_phone) { var summary = Text(string.Join(" · ", totals.Where(part => part.Length > 0)), 12, true); summary.TextWrapping = TextWrapping.NoWrap; actions.Children.Add(summary); }
        if (device != null) actions.Children.Add(Wire(ActionButton(T("web.admin.users.detail.downloads.device.openDevice"), "arrow-up-right", "OutlineButtonStyle", 28, T("admin.native.users.activity.openDevice")), () =>
        {
            var request = new AdminRouteRequest(AdminRoute.Devices) { Segments = [_review!.User.Text("id"), deviceId] };
            if (!AdminShellPage.Open(request)) _notice.Text = "The device lane is not registered yet. Scope: " + request.Href;
            return Task.CompletedTask;
        }));
        var body = new StackPanel();
        body.Children.Add(DownloadTable(rows, monitors, deviceId, android));
        var notes = new StackPanel { Spacing = 4 };
        notes.Children.Add(Text(T("admin.native.users.downloads.disclaimer"), 12, true));
        if (android) notes.Children.Add(Text(T("web.admin.users.detail.downloads.device.androidNote"), 12, true));
        body.Children.Add(new Border { Child = notes, Padding = new(Gutter, 10, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5) });
        var title = device != null ? device.Text("device_name", T("web.admin.users.detail.common.unnamedDevice")) : T("web.admin.users.detail.common.unknownDevice");
        return DetailCard(title, string.Join(" · ", meta.Where(part => part.Length > 0)), actions, body, null, "UsersDevice_" + deviceId);
    }

    private FrameworkElement DownloadTable(List<JsonObject> rows, IReadOnlyList<JsonObject> monitors, string deviceId, bool android)
    {
        var grid = new Grid();
        (GridLength Width, double Min)[] columns = [(new(1, GridUnitType.Star), 160), (GridLength.Auto, 0), (GridLength.Auto, 0), (GridLength.Auto, 0), (GridLength.Auto, 0)];
        foreach (var (width, min) in columns) grid.ColumnDefinitions.Add(new() { Width = width, MinWidth = min });
        var row = 0;
        void Place(UIElement element, int column, double vertical = 8, double left = 12, HorizontalAlignment alignment = HorizontalAlignment.Left)
        {
            var cell = new Border { Child = element, Padding = new(column == 0 ? Gutter + left - 12 : 12, vertical, column == columns.Length - 1 ? Gutter : 12, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment };
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
        }
        void Rule()
        {
            var band = new Border { BorderBrush = Tint("BorderBrush", 0.5), BorderThickness = new(0, 1, 0, 0) };
            Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Length); grid.Children.Add(band);
        }
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string[] heads = ["title", "quality", "size", "status", "added"];
        for (var column = 0; column < heads.Length; column++)
        {
            var head = Medium(T("web.admin.users.detail.downloads.device.column." + heads[column]), 14, true); head.TextWrapping = TextWrapping.NoWrap; head.LineHeight = 20;
            if (column == 2) head.TextAlignment = TextAlignment.Right;
            Place(head, column, 12, alignment: column == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        }
        row++;
        TextBlock SizeText(double bytes) { var block = Text(Bytes(bytes), 14, true); block.TextWrapping = TextWrapping.NoWrap; block.TextAlignment = TextAlignment.Right; return block; }
        double Size(JsonObject item) => item["file_size"] is JsonValue value && value.TryGetValue<double>(out var size) ? size : 0;
        TextBlock When(string value) { var block = Text(ShortDate(value), 14, true); block.TextWrapping = TextWrapping.NoWrap; return block; }
        foreach (var group in rows.GroupBy(item => item.Text("episode_id").Length == 0 ? "item:" + item.Text("id") : "series:" + item.Text("profile_id") + ":" + item.Text("content_id")))
        {
            var first = group.First();
            var series = first.Text("episode_id").Length > 0;
            var episodes = group.OrderBy(item => item["episode"].Count("season_number")).ThenBy(item => item["episode"].Count("episode_number")).ThenBy(item => item.Text("id"), StringComparer.Ordinal).ToList();
            var monitored = series && monitors.Any(monitor => monitor.Flag("active") && monitor.Text("device_id") == deviceId && monitor.Text("profile_id") == first.Text("profile_id") && monitor.Text("series_id") == first.Text("content_id"));
            var title = first.Text("title", first.Text("content_id", T("web.admin.users.detail.downloads.unknownSeries")));
            FrameworkElement Heading(UIElement lead)
            {
                var line = new LogsWrapPanel { Spacing = 8, RowSpacing = 4 };
                line.Children.Add(lead);
                if (monitored) line.Children.Add(AdminLibrariesUi.Badge(T("web.admin.users.detail.downloads.device.monitored"), AdminLibrariesUi.BadgeTone.Outline));
                return line;
            }
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Rule();
            if (!series || episodes.Count == 1)
            {
                var name = new StackPanel();
                var heading = Medium(title, 14); heading.FontWeight = FontWeights.SemiBold; heading.IsTextSelectionEnabled = true;
                name.Children.Add(Heading(heading));
                var code = series ? EpisodeLabel(first) : "";
                if (code.Length > 0) name.Children.Add(Text(code, 12, true));
                Place(name, 0);
                Place(Text(DownloadQuality(first), 14, true), 1);
                Place(SizeText(Size(first)), 2, alignment: HorizontalAlignment.Right);
                Place(DownloadStatusBadge(first.Text("status"), android), 3);
                Place(When(first.Text("created_at")), 4);
                row++;
                continue;
            }
            var key = deviceId + "|" + group.Key;
            var open = _openSeries.TryGetValue(key, out var chosen) ? chosen : episodes.Any(item => item.Text("status") != "completed");
            var toggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            toggle.Children.Add(AdminMonitorUi.Icon(open ? "chevron-down" : "chevron-right", 14, Brush("PrimaryTextBrush")));
            var label = Medium(title, 14); label.FontWeight = FontWeights.SemiBold; label.TextWrapping = TextWrapping.Wrap; toggle.Children.Add(label);
            var button = AdminMonitorUi.TextRoom(AdminMonitorUi.Plain(toggle, title));
            AutomationProperties.SetName(button, title);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            Wire(button, () => { _openSeries[key] = !open; Render(); return Task.CompletedTask; });
            var heading2 = new StackPanel();
            heading2.Children.Add(Heading(button));
            var subtitle = T("web.admin.users.detail.downloads.episodeCount", ("count", episodes.Count), ("count_text", episodes.Count));
            var sub = Text(subtitle, 12, true); sub.Margin = new(16, 0, 0, 0); heading2.Children.Add(sub);
            Place(heading2, 0);
            var qualities = episodes.Select(DownloadQuality).Distinct().ToList();
            Place(Text(qualities.Count == 1 ? qualities[0] : T("web.admin.users.detail.downloads.quality.mixed"), 14, true), 1);
            Place(SizeText(episodes.Sum(Size)), 2, alignment: HorizontalAlignment.Right);
            var statuses = episodes.Select(item => item.Text("status")).Distinct().ToList();
            Place(statuses.Count == 1 ? DownloadStatusBadge(statuses[0], android) : TintedBadge(T("web.admin.users.detail.downloads.status.mixed"), AdminMonitorUi.Muted, pill: true), 3);
            Place(When(episodes.Select(item => item.Text("created_at")).OrderByDescending(value => value, StringComparer.Ordinal).First()), 4);
            row++;
            if (!open) continue;
            foreach (var episode in episodes)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Rule();
                var episodeText = Text(EpisodeLabel(episode) is { Length: > 0 } text ? text : T("web.admin.users.detail.downloads.unknownEpisode"), 14, true);
                Place(episodeText, 0, left: 36);
                Place(Text("", 14), 1);
                Place(SizeText(Size(episode)), 2, alignment: HorizontalAlignment.Right);
                Place(DownloadStatusBadge(episode.Text("status"), android), 3);
                Place(When(episode.Text("created_at")), 4);
                row++;
            }
        }
        return FitTable(grid);
    }

    private string MonitorKeeps(JsonObject monitor)
    {
        var seasons = (monitor["season_numbers"] as JsonArray)?.Select(item => item?.ToString() ?? "").Where(text => text.Length > 0).ToArray() ?? [];
        return monitor.Text("mode") switch
        {
            "all" or "all_episodes" => T("web.admin.users.detail.downloads.monitor.allEpisodes"),
            "new_only" or "new_episodes" => T("web.admin.users.detail.downloads.monitor.newOnly"),
            "latest_season" => monitor.Count("target_season") > 0 ? T("web.admin.users.detail.downloads.monitor.latestSeasonNumber", ("season", monitor.Count("target_season"))) : T("web.admin.users.detail.downloads.monitor.latestSeason"),
            "seasons" or "chosen_seasons" => seasons.Length > 0 ? T("web.admin.users.detail.downloads.seasonList", ("count", seasons.Length), ("seasons", string.Join(", ", seasons))) : T("web.admin.users.detail.downloads.monitor.chosenSeasons"),
            var other => other.Length == 0 ? "—" : Label(other),
        };
    }

    private string MonitorNow(JsonObject monitor)
    {
        if (!monitor.Flag("active")) return T("web.admin.users.detail.downloads.monitor.paused");
        var parts = new List<string>();
        if (monitor.Count("on_device") > 0) parts.Add(T("web.admin.users.detail.downloads.count.onDevice", ("count", monitor.Count("on_device")), ("count_text", monitor.Count("on_device"))));
        if (monitor.Count("in_progress") > 0) parts.Add(T("web.admin.users.detail.downloads.count.inProgress", ("count", monitor.Count("in_progress")), ("count_text", monitor.Count("in_progress"))));
        if (monitor.Count("removed_episodes") > 0) parts.Add(T("web.admin.users.detail.downloads.count.removedByUser", ("count", monitor.Count("removed_episodes")), ("count_text", monitor.Count("removed_episodes"))));
        return parts.Count > 0 ? string.Join(" · ", parts) : T("web.admin.users.detail.downloads.monitor.nothingYet");
    }

    private FrameworkElement MonitoredSeriesCard(IReadOnlyList<JsonObject> monitors, bool filtered)
    {
        UIElement body;
        if (monitors.Count == 0) body = Empty(filtered ? T("web.admin.users.detail.downloads.monitor.noMatch") : T("web.admin.users.detail.downloads.monitor.empty"));
        else
        {
            var grid = new Grid();
            // Below sm the official table keeps only Series, Keeps and Now.
            int[] logical = _phone ? [0, 2, 5] : [0, 1, 2, 3, 4, 5];
            (GridLength Width, double Min)[] all = [(new(1.2, GridUnitType.Star), 96), (new(1.2, GridUnitType.Star), 104), (new(1, GridUnitType.Star), 96), (new(1, GridUnitType.Star), 96), (GridLength.Auto, 0), (new(1.2, GridUnitType.Star), 96)];
            var columns = logical.Select(index => all[index]).ToArray();
            foreach (var (width, min) in columns) grid.ColumnDefinitions.Add(new() { Width = width, MinWidth = min });
            var row = 0;
            void Place(UIElement element, int column, double vertical = 8)
            {
                var position = Array.IndexOf(logical, column);
                if (position < 0) return;
                var cell = new Border { Child = element, Padding = new(position == 0 ? Gutter : 12, vertical, position == columns.Length - 1 ? Gutter : 12, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                Grid.SetRow(cell, row); Grid.SetColumn(cell, position); grid.Children.Add(cell);
            }
            void Rule(bool first)
            {
                var band = new Border { BorderBrush = Tint("BorderBrush", 0.5), BorderThickness = new(0, first ? 0 : 1, 0, 0) };
                Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Length); grid.Children.Add(band);
            }
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            string[] heads = ["series", "deviceProfile", "keeps", "afterWatching", "storageLimit", "now"];
            for (var column = 0; column < heads.Length; column++)
            {
                var head = Medium(T("web.admin.users.detail.downloads.monitor.column." + heads[column]), 14, true); head.TextWrapping = TextWrapping.Wrap; head.LineHeight = 20;
                Place(head, column, 12);
            }
            row++;
            foreach (var monitor in monitors)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Rule(false);
                var name = new StackPanel();
                var title = Medium(monitor.Text("series_title", T("web.admin.users.detail.downloads.unknownSeries")), 14); title.FontWeight = FontWeights.SemiBold; title.IsTextSelectionEnabled = true;
                name.Children.Add(title);
                Place(name, 0);
                var where = Text(DeviceName(monitor.Text("device_id")) + " · " + ProfileName(monitor.Text("profile_id")), 14, true); Place(where, 1);
                Place(Text(MonitorKeeps(monitor), 14), 2);
                Place(Text(monitor.Flag("delete_watched") ? T("web.admin.users.detail.downloads.monitor.deleteAfterWatching") : T("web.admin.users.detail.downloads.monitor.keep"), 14, true), 3);
                var limit = monitor["max_storage_bytes"] is JsonValue value && value.TryGetValue<double>(out var bytes) ? bytes : 0;
                Place(Text(limit > 0 ? StorageLimit(limit) : T("web.admin.users.detail.downloads.monitor.noLimit"), 14, true), 4);
                Place(Text(MonitorNow(monitor), 14, true), 5);
                row++;
            }
            body = FitTable(grid);
        }
        LogsWrapPanel? more = null;
        if (_rows.GetValueOrDefault("subscriptions") is { Complete: false } state)
        {
            more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
            more.Children.Add(Text(T("admin.native.users.partial", ("count", state.Items.Count)), 13, true));
            more.Children.Add(Action(T("admin.native.users.loadMore"), () => MoreAsync("subscriptions"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
        }
        return DetailCard(T("web.admin.users.detail.downloads.monitoredSeries"), T("web.admin.users.detail.downloads.monitor.description"), null, body, more, "UsersMonitoredSeries");
    }

    /// <summary>A storage cap in the binary gigabytes the apps use when one is picked: 10737418240 reads "10 GB".</summary>
    private static string StorageLimit(double bytes)
    {
        var gb = bytes / Math.Pow(1024, 3);
        return gb >= 1 ? T("web.admin.users.detail.common.bytes.gb", ("value", gb.ToString("0.#", CultureInfo.CurrentCulture))) : T("web.admin.users.detail.common.bytes.mb", ("value", Math.Round(bytes / Math.Pow(1024, 2))));
    }
}
