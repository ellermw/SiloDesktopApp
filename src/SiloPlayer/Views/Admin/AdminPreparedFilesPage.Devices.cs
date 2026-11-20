using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private const string DevKey = "web.admin.downloads.deviceCopies";

    /// <summary>The Device copies tab (official <c>DeviceCopiesTab.tsx</c>): devices across accounts, expandable to their downloads.</summary>
    private sealed class DevicesView : TabView
    {
        private readonly StackPanel _root = Stack(16);
        private readonly LogsWrapPanel _filters = new() { Spacing = 8 };
        private readonly SearchField _search;
        private readonly ToggleButton _stale = new() { MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8) };
        private readonly Choice _sort;
        private readonly StackPanel _dynamic = Stack(16);
        private readonly HashSet<string> _selectedEntries = new(StringComparer.Ordinal);
        private bool _syncing;
        private string? _expanded;
        private AdminDownloadDeviceQuery _seen = new();

        public DevicesView(AdminPreparedFilesPage page) : base(page)
        {
            _search = new SearchField(T(DevKey + ".search"), "PreparedFilesDeviceSearch", 224, value => Change(q => q with { Search = value.Length == 0 ? null : value }));
            AutomationProperties.SetAutomationId(_stale, "PreparedFilesStaleFilter");
            _stale.Checked += (_, _) => { if (!_syncing) Change(q => q with { Stale = true }); };
            _stale.Unchecked += (_, _) => { if (!_syncing) Change(q => q with { Stale = false }); };
            _sort = new Choice("PreparedFilesDeviceSort", T(DevKey + ".sort"), 210, value => Change(q => q with { Sort = value }));
            _filters.Children.Add(_search.Root); _filters.Children.Add(_stale); _filters.Children.Add(_sort.Box);
            _root.Children.Add(_filters); _root.Children.Add(_dynamic);
        }

        public override UIElement Root => _root;

        public override void Reset() { _search.Reset(); _selectedEntries.Clear(); _expanded = null; _seen = new(); }

        private void Change(Func<AdminDownloadDeviceQuery, AdminDownloadDeviceQuery> edit)
        {
            _expanded = null; _selectedEntries.Clear();
            _ = P.Controller.SetDeviceQueryAsync(edit(P.Controller.State.DeviceQuery));
        }

        public override void Update(AdminPreparedFilesSnapshot state)
        {
            var query = state.DeviceQuery;
            if (_seen != query) { _seen = query; _expanded = null; _selectedEntries.Clear(); }
            var days = state.Storage.Value?.StaleDeviceDays ?? 14;
            _syncing = true;
            try
            {
                _stale.Content = new TextBlock { Text = T(DevKey + ".staleFilter", new { days, days_text = days }), FontSize = 13 };
                AutomationProperties.SetName(_stale, T(DevKey + ".staleFilter", new { days, days_text = days }));
                if (_stale.IsChecked != query.Stale) _stale.IsChecked = query.Stale;
                _stale.BorderBrush = query.Stale ? Tint("WarningBrush", 0.5) : Brush("BorderBrush");
                _stale.Background = query.Stale ? Tint("WarningBrush", 0.1) : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            finally { _syncing = false; }
            _sort.Show([("last_seen", T(DevKey + ".sortLastSeen")), ("size", T(DevKey + ".sortSize"))], query.Sort);

            _dynamic.Children.Clear();
            var devices = state.Devices;
            var rows = devices.Items;
            if (devices.Error != null && rows.Count > 0) _dynamic.Children.Add(StaleBanner("PreparedFilesDevicesReload", () => _ = P.Controller.ReloadDevicesAsync()));
            var table = new LogsNativeTable(["", T(DevKey + ".column.device"), T(DevKey + ".column.person"), T(DevKey + ".column.lastSeen"), T(DevKey + ".column.copies"), T(DevKey + ".column.onDevice"), ""],
                [44, 220, 200, 140, 280, 120, 150]);
            table.AlignEnd(5);
            if (!devices.Loaded && devices.Error == null)
                for (var i = 0; i < 3; i++) table.AddFull(Text("…", 14, true), 40);
            else if (rows.Count == 0)
            {
                var empty = Stack(12);
                var message = Text(devices.Error != null ? T(DevKey + ".loadError") : query.Stale ? T(DevKey + ".emptyStale") : T(DevKey + ".empty"), 14, true);
                message.HorizontalAlignment = HorizontalAlignment.Center; message.TextAlignment = TextAlignment.Center;
                empty.Children.Add(message);
                if (devices.Error != null)
                {
                    var reload = Button(T("common.tryAgain"), "PreparedFilesDevicesReload");
                    reload.HorizontalAlignment = HorizontalAlignment.Center;
                    reload.Click += (_, _) => _ = P.Controller.ReloadDevicesAsync();
                    empty.Children.Add(reload);
                }
                table.AddFull(empty, 120);
            }
            else
                foreach (var device in rows)
                {
                    AddDevice(table, device, state);
                    if (_expanded == AdminPreparedFilesController.DeviceKey(device)) table.AddFull(Entries(device, state), 40);
                }
            _dynamic.Children.Add(Panel(table, 16, 4, true));
            if (devices.NextCursor != null) _dynamic.Children.Add(LoadMore(devices.Loading, () => _ = P.Controller.LoadMoreDevicesAsync()));
            if (devices.Truncated) _dynamic.Children.Add(Text(T("configure.native.preparedFiles.listTruncated", new { count = rows.Count }), 12, true));
        }

        private void AddDevice(LogsNativeTable table, AdminDownloadDevice device, AdminPreparedFilesSnapshot state)
        {
            var key = AdminPreparedFilesController.DeviceKey(device);
            var open = _expanded == key;
            var name = string.IsNullOrEmpty(device.DeviceName) ? device.DeviceId : device.DeviceName;
            var expander = new Button { Content = new FontIcon { Glyph = open ? "" : "", FontSize = 12 }, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0) };
            AutomationProperties.SetAutomationId(expander, "PreparedFilesDeviceExpand_" + key.Replace('/', '_'));
            AutomationProperties.SetName(expander, open ? T(DevKey + ".hideEntries", new { device = device.DeviceName }) : T(DevKey + ".showEntries", new { device = device.DeviceName }));
            AutomationProperties.SetItemStatus(expander, open ? "expanded" : "collapsed");
            expander.Click += (_, _) =>
            {
                _selectedEntries.Clear();
                _expanded = open ? null : key;
                if (!open) _ = P.Controller.LoadEntriesAsync(device);
                Update(P.Controller.State);
            };
            var deviceCell = Stack(0);
            deviceCell.Children.Add(Strong(name, 14));
            deviceCell.Children.Add(Text(string.IsNullOrEmpty(device.Platform) ? T(DevKey + ".unknownPlatform") : device.Platform, 12, true));
            var person = Stack(0);
            person.Children.Add(Text(string.IsNullOrEmpty(device.ProfileName) ? device.ProfileId : device.ProfileName));
            person.Children.Add(Text(device.Username, 12, true));
            var seen = Cell(Presentation.Relative(device.LastSeenAt) ?? T(DevKey + ".neverSeen"));
            if (device.Stale) seen.Foreground = Brush("WarningBrush");
            var copies = Stack(0);
            copies.Children.Add(RichText(AdminPreparedFilesText.Rich(DevKey + ".copiesSummary", new { copies = device.Copies, summary = Presentation.DeviceSummary(device) }), 14));
            if (device.Revoked > 0)
            {
                var pending = Text(T(DevKey + ".revokedPending", Presentation.Count(device.Revoked)), 12); pending.Foreground = Brush("WarningBrush");
                copies.Children.Add(pending);
            }
            else if (device.Monitors > 0) copies.Children.Add(Text(T(DevKey + ".monitors", Presentation.Count(device.Monitors)), 12, true));
            var revokeAll = Button(T(DevKey + ".revokeAll"), "PreparedFilesRevokeAll_" + key.Replace('/', '_'));
            AutomationProperties.SetName(revokeAll, T(DevKey + ".revokeAllLabel", new { device = name }));
            revokeAll.IsEnabled = device.Copies > 0 && !state.ActionPending && !state.DestructiveBlocked;
            revokeAll.Click += (_, _) => P.ConfirmRevoke(device, null, () => { });
            table.Add([expander, deviceCell, person, seen, copies, Cell(Presentation.FormatBytes(device.BytesOnDevice)), revokeAll]);
        }

        private UIElement Entries(AdminDownloadDevice device, AdminPreparedFilesSnapshot state)
        {
            var key = AdminPreparedFilesController.DeviceKey(device);
            var entries = state.Entries.GetValueOrDefault(key);
            var root = Stack(8);
            root.Margin = new Thickness(36, 4, 0, 8);
            if (entries == null || !entries.Loaded && entries.Error == null) { root.Children.Add(Text("…", 14, true)); return root; }
            var rows = entries.Items;
            var revocable = rows.Where(r => r.Status != "revoked").ToArray();
            var known = revocable.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            _selectedEntries.RemoveWhere(id => !known.Contains(id));
            var chosen = revocable.Where(r => _selectedEntries.Contains(r.Id)).ToArray();
            if (entries.Error != null)
            {
                var message = Text(rows.Count == 0 ? T("configure.native.preparedFiles.entriesLoadError") : T("configure.native.preparedFiles.backgroundFailed"), 12, true);
                var reload = Button(T("configure.native.preparedFiles.reload"), "PreparedFilesEntriesReload", "GhostButtonStyle");
                reload.Click += (_, _) => _ = P.Controller.LoadEntriesAsync(device);
                var line = Inline(8); line.Children.Add(message); line.Children.Add(reload);
                root.Children.Add(line);
            }
            if (chosen.Length > 0)
            {
                var bar = Inline(8);
                bar.Children.Add(Text(T(DevKey + ".selected", Presentation.Count(chosen.Length)), 12));
                var revoke = Button(T(DevKey + ".revokeSelected", Presentation.Count(chosen.Length)), "PreparedFilesRevokeSelected");
                revoke.IsEnabled = !state.ActionPending && !state.DestructiveBlocked;
                var captured = chosen.ToArray();
                revoke.Click += (_, _) => P.ConfirmRevoke(device, captured, () => _selectedEntries.Clear());
                bar.Children.Add(revoke);
                root.Children.Add(bar);
            }
            foreach (var row in rows)
            {
                var status = Presentation.EntryStatus(row);
                var title = Presentation.EntryTitle(row);
                var line = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
                line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 160 });
                line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new() { Width = new(90) });
                line.ColumnDefinitions.Add(new() { Width = new(90) });
                var select = Check("", "PreparedFilesEntrySelect_" + row.Id, row.Status != "revoked" && _selectedEntries.Contains(row.Id), row.Status != "revoked" && !state.ActionPending, on =>
                {
                    if (on) _selectedEntries.Add(row.Id); else _selectedEntries.Remove(row.Id);
                    Update(P.Controller.State);
                });
                AutomationProperties.SetName(select, T(DevKey + ".selectEntry", new { title }));
                line.Children.Add(select);
                var text = Stack(0);
                var heading = Strong(title, 14); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
                text.Children.Add(heading);
                text.Children.Add(Text(string.Join(" · ", new[]
                {
                    row.Episode?.Title, Presentation.QualityLabel(row.EffectiveQuality),
                    string.IsNullOrEmpty(row.LocationName) ? null : T(DevKey + ".preparedOn", new { location = row.LocationName }),
                }.Where(x => !string.IsNullOrEmpty(x))), 12, true));
                Grid.SetColumn(text, 1); line.Children.Add(text);
                var pill = Pill(status.Label, status.Tone == "neutral" ? "neutral" : status.Tone);
                Grid.SetColumn(pill, 2); line.Children.Add(pill);
                var size = Text(Presentation.FormatBytes(row.FileSize), 14); size.TextAlignment = TextAlignment.Right; size.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(size, 3); line.Children.Add(size);
                if (row.Status != "revoked")
                {
                    var revoke = new HyperlinkButton { Content = T(DevKey + ".revoke"), FontSize = 12 };
                    AutomationProperties.SetAutomationId(revoke, "PreparedFilesRevokeEntry_" + row.Id);
                    AutomationProperties.SetName(revoke, T(DevKey + ".revokeEntry", new { title }));
                    revoke.IsEnabled = !state.ActionPending && !state.DestructiveBlocked;
                    var only = row;
                    revoke.Click += (_, _) => P.ConfirmRevoke(device, [only], () => _selectedEntries.Remove(only.Id));
                    Grid.SetColumn(revoke, 4); line.Children.Add(revoke);
                }
                else
                {
                    var sent = Text(T(DevKey + ".sent"), 12, true);
                    Grid.SetColumn(sent, 4); line.Children.Add(sent);
                }
                root.Children.Add(line);
            }
            if (entries.NextCursor != null)
            {
                var more = Button(entries.Loading ? T(DevKey + ".loadingMore") : T(DevKey + ".showMore"), "PreparedFilesEntriesMore", "GhostButtonStyle");
                more.IsEnabled = !entries.Loading;
                more.HorizontalAlignment = HorizontalAlignment.Left;
                more.Click += (_, _) => _ = P.Controller.LoadEntriesAsync(device, append: true);
                root.Children.Add(more);
            }
            return root;
        }
    }

    /// <summary>
    /// The official revoke dialog for a whole device (<paramref name="entries"/> null) or exactly the listed downloads. What the dialog
    /// names is what is sent, whatever the selection became while it was open.
    /// </summary>
    private async void ConfirmRevoke(AdminDownloadDevice device, IReadOnlyList<AdminDownloadEntry>? entries, Action afterRevoke)
    {
        if (Controller.State.ActionPending || Controller.State.DestructiveBlocked) return;
        var wholeDevice = entries == null;
        var count = wholeDevice ? device.Copies : entries!.Count;
        var bytes = wholeDevice ? device.BytesOnDevice : entries!.Sum(e => e.Status == "completed" ? e.FileSize : 0);
        if (count == 0) return;
        var only = !wholeDevice && entries!.Count == 1 ? entries[0] : null;
        var titleDevice = string.IsNullOrEmpty(device.DeviceName) ? T(DevKey + ".revokeDialog.thisDevice") : device.DeviceName;
        var textDevice = string.IsNullOrEmpty(device.DeviceName) ? T(DevKey + ".revokeDialog.deviceFallback") : device.DeviceName;
        var description = !string.IsNullOrEmpty(device.ProfileName)
            ? T(DevKey + ".revokeDialog.descriptionProfile", new { count, profile = device.ProfileName, device = textDevice })
            : T(DevKey + ".revokeDialog.description", new { count, device = textDevice });
        if (device.Stale)
        {
            var seen = Presentation.Relative(device.LastSeenAt);
            description = T(DevKey + ".revokeDialog.withStale", new
            {
                description, stale = seen != null ? T(DevKey + ".revokeDialog.staleSeen", new { when = seen }) : T(DevKey + ".revokeDialog.staleUnknown"),
            });
        }
        var storage = Controller.State.Storage.Value;
        var content = Stack(12);
        content.Children.Add(Text(description));
        var effect = Stack(2);
        var frees = Text(T(DevKey + ".revokeDialog.frees", new { size = Presentation.FormatBytes(bytes) })); frees.FontWeight = FontWeights.Medium;
        effect.Children.Add(frees);
        effect.Children.Add(Text(storage == null ? T(DevKey + ".revokeDialog.cacheUnknown") : T(DevKey + ".revokeDialog.cacheHours", Presentation.Count(storage.CacheHours)), 12, true));
        content.Children.Add(Panel(effect, 12, 12, true));
        CheckBox? pause = null;
        if (wholeDevice && device.Monitors > 0)
        {
            var box = Stack(2);
            pause = new CheckBox { IsChecked = true, Content = box, MinWidth = 0 };
            box.Children.Add(Text(T(DevKey + ".revokeDialog.pauseMonitors", Presentation.Count(device.Monitors))));
            box.Children.Add(Text(T(DevKey + ".revokeDialog.pauseHint"), 12, true));
            AutomationProperties.SetAutomationId(pause, "PreparedFilesPauseMonitors");
            AutomationProperties.SetName(pause, T(DevKey + ".revokeDialog.pauseMonitors", Presentation.Count(device.Monitors)));
            content.Children.Add(pause);
        }
        content.Children.Add(RichText(AdminPreparedFilesText.Rich(DevKey + ".revokeDialog.reason", new { }), 14));
        var reason = new TextBox { MaxLength = 500, PlaceholderText = T(DevKey + ".revokeDialog.reasonPlaceholder"), AcceptsReturn = false };
        AutomationProperties.SetAutomationId(reason, "PreparedFilesRevokeReason");
        AutomationProperties.SetName(reason, AdminPreparedFilesText.Plain(DevKey + ".revokeDialog.reason", new { }));
        content.Children.Add(reason);
        content.Children.Add(Text(T(DevKey + ".revokeDialog.oldApps"), 12, true));
        var title = only != null
            ? T(DevKey + ".revokeDialog.titleEntry", new { title = Presentation.EntryTitle(only), device = titleDevice })
            : T(DevKey + ".revokeDialog.titleCount", Presentation.With(Presentation.Count(count), ("device", titleDevice)));
        var dialog = NewDialog(title, content, T(DevKey + ".revokeDialog.confirm", Presentation.Count(count)), destructive: true);
        AutomationProperties.SetAutomationId(dialog, "PreparedFilesRevokeDialog");
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var target = wholeDevice
            ? AdminDownloadRevokeTarget.ForDevice(device, pause?.IsChecked == true)
            : AdminDownloadRevokeTarget.ForIds(entries!.Select(e => e.Id).ToArray());
        afterRevoke();
        await Controller.RevokeAsync(target, reason.Text.Trim(), device.DeviceName);
    }
}
