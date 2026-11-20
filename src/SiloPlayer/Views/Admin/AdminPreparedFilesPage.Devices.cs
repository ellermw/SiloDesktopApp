using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private const string DevKey = "web.admin.downloads.deviceCopies";

    /// <summary>
    /// The Device copies tab (official <c>DeviceCopiesTab.tsx</c>): devices across accounts, expandable to their downloads. Device
    /// rows, the expand and revoke buttons, and every download row's check box and revoke link are built once and updated in place.
    /// </summary>
    private sealed class DevicesView : TabView
    {
        private static readonly double[] Weights = [44, 220, 200, 140, 280, 120, 150];

        private readonly StackPanel _root = Stack(16);
        private readonly LogsWrapPanel _filters = new() { Spacing = 8 };
        private readonly SearchField _search;
        private readonly ToggleButton _stale = new() { MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8) };
        private readonly Chip _staleChip;
        private readonly TextBlock _staleLabel = new() { FontSize = 13 };
        private readonly Choice _sort;
        private readonly StatusHost _banner = new(), _placeholder = new();
        private readonly StableTable _table;
        private readonly Border _tablePanel;
        private readonly Button _more;
        private readonly TextBlock _truncated = Text("", 12, true);
        private readonly Dictionary<string, DeviceRow> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EntriesPanel> _panels = new(StringComparer.Ordinal);
        private readonly HashSet<string> _selectedEntries = new(StringComparer.Ordinal);
        private string? _expanded;
        private AdminDownloadDeviceQuery _seen = new();

        public DevicesView(AdminPreparedFilesPage page) : base(page)
        {
            _search = new SearchField(T(DevKey + ".search"), "PreparedFilesDeviceSearch", 224, value => Change(q => q with { Search = value.Length == 0 ? null : value }));
            AutomationProperties.SetAutomationId(_stale, "PreparedFilesStaleFilter");
            _stale.Content = _staleLabel;
            _staleChip = new Chip(_stale, on => Change(q => q with { Stale = on }));
            _sort = new Choice("PreparedFilesDeviceSort", T(DevKey + ".sort"), 210, value => Change(q => q with { Sort = value }));
            _filters.Children.Add(_search.Root); _filters.Children.Add(_stale); _filters.Children.Add(_sort.Box);
            _table = new StableTable([null, HeaderText(T(DevKey + ".column.device")), HeaderText(T(DevKey + ".column.person")), HeaderText(T(DevKey + ".column.lastSeen")),
                HeaderText(T(DevKey + ".column.copies")), HeaderText(T(DevKey + ".column.onDevice"), end: true), null], Weights);
            var inside = Stack(0);
            inside.Children.Add(_table); inside.Children.Add(_placeholder);
            _tablePanel = Panel(inside, 16, 4, true);
            _more = NewLoadMore(() => _ = P.Controller.LoadMoreDevicesAsync());
            foreach (UIElement child in new UIElement[] { _filters, _banner, _tablePanel, _more, _truncated }) _root.Children.Add(child);
            _truncated.Visibility = Visibility.Collapsed;
        }

        public override UIElement Root => _root;

        public override void Reset()
        {
            base.Reset();
            _search.Reset(); _selectedEntries.Clear(); _expanded = null; _seen = new(); _rows.Clear(); _panels.Clear(); _table.Body.Children.Clear();
        }

        private void Change(Func<AdminDownloadDeviceQuery, AdminDownloadDeviceQuery> edit)
        {
            _expanded = null; _selectedEntries.Clear();
            _ = P.Controller.SetDeviceQueryAsync(edit(P.Controller.State.DeviceQuery));
        }

        protected override object Inputs(AdminPreparedFilesSnapshot state) =>
            (state.DeviceQuery, state.Devices, state.Entries, state.Storage.Value?.StaleDeviceDays, state.Storage.Value?.CacheHours, state.ActionPending, state.DestructiveBlocked);

        private void Refresh() => Update(P.Controller.State);

        public override void Update(AdminPreparedFilesSnapshot state)
        {
            var query = state.DeviceQuery;
            if (_seen != query) { _seen = query; _expanded = null; _selectedEntries.Clear(); }
            var days = state.Storage.Value?.StaleDeviceDays ?? 14;
            var label = T(DevKey + ".staleFilter", new { days, days_text = days });
            SetText(_staleLabel, label);
            AutomationProperties.SetName(_stale, label);
            _staleChip.Sync(query.Stale, 1);
            _stale.BorderBrush = query.Stale ? Tint("WarningBrush", 0.5) : Brush("BorderBrush");
            _stale.Background = query.Stale ? Tint("WarningBrush", 0.1) : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _sort.Show([("last_seen", T(DevKey + ".sortLastSeen")), ("size", T(DevKey + ".sortSize"))], query.Sort);

            var devices = state.Devices;
            var rows = devices.Items;
            if (devices.Error != null && rows.Count > 0) _banner.Show("stale", () => StaleBanner("PreparedFilesDevicesReload", () => _ = P.Controller.ReloadDevicesAsync()));
            else _banner.Hide();

            var keys = rows.Select(AdminPreparedFilesController.DeviceKey).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _rows.Keys.Where(key => !keys.Contains(key)).ToArray()) _rows.Remove(key);
            foreach (var key in _panels.Keys.Where(key => key != _expanded).ToArray()) _panels.Remove(key);

            var wanted = new List<UIElement>();
            if (!devices.Loaded && devices.Error == null) _placeholder.Show("loading", () => Skeleton(3));
            else if (rows.Count == 0)
            {
                if (devices.Error != null) _placeholder.Show("error", () => Placeholder(T(DevKey + ".loadError"), "PreparedFilesDevicesReload", () => _ = P.Controller.ReloadDevicesAsync()));
                else if (query.Stale) _placeholder.Show("emptyStale", () => Placeholder(T(DevKey + ".emptyStale")));
                else _placeholder.Show("empty", () => Placeholder(T(DevKey + ".empty")));
            }
            else
            {
                _placeholder.Hide();
                foreach (var device in rows)
                {
                    var key = AdminPreparedFilesController.DeviceKey(device);
                    if (!_rows.TryGetValue(key, out var row)) _rows[key] = row = new DeviceRow(this, key);
                    row.Apply(device, _expanded == key, state);
                    wanted.Add(row.Root);
                    if (_expanded == key)
                    {
                        if (!_panels.TryGetValue(key, out var panel)) _panels[key] = panel = new EntriesPanel(this);
                        panel.Sync(device, state);
                        wanted.Add(panel.Root);
                    }
                }
            }
            SyncChildren(_table.Body, wanted);
            SyncLoadMore(_more, devices.NextCursor != null, devices.Loading);
            _truncated.Visibility = devices.Truncated ? Visibility.Visible : Visibility.Collapsed;
            if (devices.Truncated) SetText(_truncated, T("configure.native.preparedFiles.listTruncated", new { count = rows.Count }));
        }

        private sealed record DeviceCells(string Name, string Platform, string Person, string Username, string Seen, bool Stale, long Copies, string Summary, string Revoked, string Monitors, string OnDevice);

        /// <summary>One device row. The expand button and Revoke-all button are built once; the text cells are rebuilt only when their values change.</summary>
        private sealed class DeviceRow
        {
            private readonly DevicesView _view;
            private readonly string _key;
            private readonly CellRow _cells = new(Weights);
            private readonly Button _expander, _revokeAll;
            private readonly FontIcon _glyph = new() { FontSize = 12 };
            private bool? _open;
            private string _names = "";

            public DeviceRow(DevicesView view, string key)
            {
                _view = view; _key = key;
                _expander = new Button { Content = _glyph, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0) };
                AutomationProperties.SetAutomationId(_expander, "PreparedFilesDeviceExpand_" + key.Replace('/', '_'));
                _expander.Click += (_, _) =>
                {
                    var open = view._expanded == key;
                    view._selectedEntries.Clear();
                    view._expanded = open ? null : key;
                    if (!open) _ = view.P.Controller.LoadEntriesAsync(Device);
                    view.Refresh();
                };
                _cells.Hold(0, _expander);
                _revokeAll = Button(T(DevKey + ".revokeAll"), "PreparedFilesRevokeAll_" + key.Replace('/', '_'));
                _revokeAll.Click += (_, _) => view.P.ConfirmRevoke(Device, null, () => { });
                _cells.Hold(6, _revokeAll);
            }

            public AdminDownloadDevice Device { get; private set; } = null!;
            public Border Root => _cells.Rule;

            public void Apply(AdminDownloadDevice device, bool open, AdminPreparedFilesSnapshot state)
            {
                Device = device;
                var name = string.IsNullOrEmpty(device.DeviceName) ? device.DeviceId : device.DeviceName;
                var cells = new DeviceCells(name, string.IsNullOrEmpty(device.Platform) ? T(DevKey + ".unknownPlatform") : device.Platform,
                    string.IsNullOrEmpty(device.ProfileName) ? device.ProfileId : device.ProfileName, device.Username,
                    Presentation.Relative(device.LastSeenAt) ?? T(DevKey + ".neverSeen"), device.Stale, device.Copies, Presentation.DeviceSummary(device),
                    device.Revoked > 0 ? T(DevKey + ".revokedPending", Presentation.Count(device.Revoked)) : "",
                    device.Revoked == 0 && device.Monitors > 0 ? T(DevKey + ".monitors", Presentation.Count(device.Monitors)) : "",
                    Presentation.FormatBytes(device.BytesOnDevice));
                if (_open != open)
                {
                    _open = open;
                    _glyph.Glyph = open ? "" : "";
                    AutomationProperties.SetItemStatus(_expander, open ? "expanded" : "collapsed");
                }
                var names = open + "\n" + device.DeviceName + "\n" + name;
                if (names != _names)
                {
                    _names = names;
                    AutomationProperties.SetName(_expander, open ? T(DevKey + ".hideEntries", new { device = device.DeviceName }) : T(DevKey + ".showEntries", new { device = device.DeviceName }));
                    AutomationProperties.SetName(_revokeAll, T(DevKey + ".revokeAllLabel", new { device = name }));
                }
                _revokeAll.IsEnabled = device.Copies > 0 && !state.ActionPending && !state.DestructiveBlocked;
                _cells.Set(1, (cells.Name, cells.Platform), () =>
                {
                    var stack = Stack(0);
                    stack.Children.Add(Strong(cells.Name, 14));
                    stack.Children.Add(Text(cells.Platform, 12, true));
                    return stack;
                });
                _cells.Set(2, (cells.Person, cells.Username), () =>
                {
                    var stack = Stack(0);
                    stack.Children.Add(Text(cells.Person));
                    stack.Children.Add(Text(cells.Username, 12, true));
                    return stack;
                });
                _cells.Set(3, (cells.Seen, cells.Stale), () =>
                {
                    var seen = Cell(cells.Seen);
                    if (cells.Stale) seen.Foreground = Brush("WarningBrush");
                    return seen;
                });
                _cells.Set(4, (cells.Copies, cells.Summary, cells.Revoked, cells.Monitors), () =>
                {
                    var copies = Stack(0);
                    copies.Children.Add(RichText(AdminPreparedFilesText.Rich(DevKey + ".copiesSummary", new { copies = cells.Copies, summary = cells.Summary }), 14));
                    if (cells.Revoked.Length > 0)
                    {
                        var pending = Text(cells.Revoked, 12); pending.Foreground = Brush("WarningBrush");
                        copies.Children.Add(pending);
                    }
                    else if (cells.Monitors.Length > 0) copies.Children.Add(Text(cells.Monitors, 12, true));
                    return copies;
                });
                _cells.Set(5, cells.OnDevice, () => { var cell = Cell(cells.OnDevice); cell.HorizontalAlignment = HorizontalAlignment.Right; cell.TextAlignment = TextAlignment.Right; return cell; });
            }
        }

        private sealed record EntryCells(string Title, string Detail, string Status, string Tone, string Size);

        /// <summary>The expanded downloads of one device. Its strips, buttons and every download row's controls are built once.</summary>
        private sealed class EntriesPanel
        {
            private readonly DevicesView _view;
            private readonly StackPanel _stack = Stack(8);
            private readonly TextBlock _loading = Text("…", 14, true);
            private readonly StackPanel _error = Inline(8);
            private readonly TextBlock _errorText = Text("", 12, true);
            private readonly Button _errorReload;
            private readonly StackPanel _bar = Inline(8);
            private readonly TextBlock _barText = Text("", 12);
            private readonly Button _revokeSelected;
            private readonly StackPanel _rows = Stack(0);
            private readonly Button _more;
            private readonly Dictionary<string, EntryRow> _entryRows = new(StringComparer.Ordinal);
            private AdminDownloadDevice _device = null!;
            private IReadOnlyList<AdminDownloadEntry> _chosen = [];

            public EntriesPanel(DevicesView view)
            {
                _view = view;
                _stack.Margin = new Thickness(36, 4, 0, 8);
                _errorReload = Button(T("configure.native.preparedFiles.reload"), "PreparedFilesEntriesReload", "GhostButtonStyle");
                _errorReload.Click += (_, _) => _ = view.P.Controller.LoadEntriesAsync(_device);
                _error.Children.Add(_errorText); _error.Children.Add(_errorReload);
                _revokeSelected = Button("", "PreparedFilesRevokeSelected");
                _revokeSelected.Click += (_, _) => view.P.ConfirmRevoke(_device, _chosen.ToArray(), () => view._selectedEntries.Clear());
                _bar.Children.Add(_barText); _bar.Children.Add(_revokeSelected);
                _more = Button(T(DevKey + ".showMore"), "PreparedFilesEntriesMore", "GhostButtonStyle");
                _more.HorizontalAlignment = HorizontalAlignment.Left;
                _more.Click += (_, _) => _ = view.P.Controller.LoadEntriesAsync(_device, append: true);
                foreach (UIElement child in new UIElement[] { _loading, _error, _bar, _rows, _more }) _stack.Children.Add(child);
                Root = new Border { Child = _stack, Padding = new Thickness(16), MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
            }

            public Border Root { get; }

            public void Sync(AdminDownloadDevice device, AdminPreparedFilesSnapshot state)
            {
                _device = device;
                var entries = state.Entries.GetValueOrDefault(AdminPreparedFilesController.DeviceKey(device));
                if (entries == null || !entries.Loaded && entries.Error == null)
                {
                    _loading.Visibility = Visibility.Visible; _error.Visibility = Visibility.Collapsed; _bar.Visibility = Visibility.Collapsed;
                    _rows.Visibility = Visibility.Collapsed; _more.Visibility = Visibility.Collapsed;
                    return;
                }
                _loading.Visibility = Visibility.Collapsed; _rows.Visibility = Visibility.Visible;
                var rows = entries.Items;
                var revocable = rows.Where(r => r.Status != "revoked").ToArray();
                var known = revocable.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                _view._selectedEntries.RemoveWhere(id => !known.Contains(id));
                var listed = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var id in _entryRows.Keys.Where(id => !listed.Contains(id)).ToArray()) _entryRows.Remove(id);
                var chosen = revocable.Where(r => _view._selectedEntries.Contains(r.Id)).ToArray();
                _chosen = chosen;
                _error.Visibility = entries.Error != null ? Visibility.Visible : Visibility.Collapsed;
                if (entries.Error != null) SetText(_errorText, rows.Count == 0 ? T("configure.native.preparedFiles.entriesLoadError") : T("configure.native.preparedFiles.backgroundFailed"));
                _bar.Visibility = chosen.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (chosen.Length > 0)
                {
                    SetText(_barText, T(DevKey + ".selected", Presentation.Count(chosen.Length)));
                    var label = T(DevKey + ".revokeSelected", Presentation.Count(chosen.Length));
                    if (_revokeSelected.Content as string != label) { _revokeSelected.Content = label; AutomationProperties.SetName(_revokeSelected, label); }
                    _revokeSelected.IsEnabled = !state.ActionPending && !state.DestructiveBlocked;
                }
                var wanted = new List<UIElement>(rows.Count);
                foreach (var entry in rows)
                {
                    if (!_entryRows.TryGetValue(entry.Id, out var row)) _entryRows[entry.Id] = row = new EntryRow(_view, entry.Id);
                    row.Apply(_device, entry, _view._selectedEntries.Contains(entry.Id), state);
                    wanted.Add(row.Root);
                }
                SyncChildren(_rows, wanted);
                _more.Visibility = entries.NextCursor != null ? Visibility.Visible : Visibility.Collapsed;
                var more = entries.Loading ? T(DevKey + ".loadingMore") : T(DevKey + ".showMore");
                if (_more.Content as string != more) { _more.Content = more; AutomationProperties.SetName(_more, more); }
                _more.IsEnabled = !entries.Loading;
            }
        }

        /// <summary>One download of an expanded device.</summary>
        private sealed class EntryRow
        {
            private readonly DevicesView _view;
            private readonly Grid _grid = new() { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            private readonly Tick _select;
            private readonly ContentControl _text = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            private readonly ContentControl _pill = new();
            private readonly TextBlock _size = Text("", 14);
            private readonly HyperlinkButton _revoke;
            private readonly TextBlock _sent = Text(T(DevKey + ".sent"), 12, true);
            private EntryCells? _model;

            public EntryRow(DevicesView view, string id)
            {
                _view = view;
                _grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                _grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 160 });
                _grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                _grid.ColumnDefinitions.Add(new() { Width = new(90) });
                _grid.ColumnDefinitions.Add(new() { Width = new(90) });
                _select = new Tick("", "PreparedFilesEntrySelect_" + id, on =>
                {
                    if (on) view._selectedEntries.Add(id); else view._selectedEntries.Remove(id);
                    view.Refresh();
                });
                _grid.Children.Add(_select.Box);
                Grid.SetColumn(_text, 1); _grid.Children.Add(_text);
                Grid.SetColumn(_pill, 2); _grid.Children.Add(_pill);
                _size.TextAlignment = TextAlignment.Right; _size.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(_size, 3); _grid.Children.Add(_size);
                _revoke = new HyperlinkButton { Content = T(DevKey + ".revoke"), FontSize = 12 };
                AutomationProperties.SetAutomationId(_revoke, "PreparedFilesRevokeEntry_" + id);
                _revoke.Click += (_, _) => view.P.ConfirmRevoke(Device, [Entry], () => view._selectedEntries.Remove(Entry.Id));
                Grid.SetColumn(_revoke, 4); _grid.Children.Add(_revoke);
                Grid.SetColumn(_sent, 4); _grid.Children.Add(_sent);
                Root = _grid;
            }

            public AdminDownloadDevice Device { get; private set; } = null!;
            public AdminDownloadEntry Entry { get; private set; } = null!;
            public Grid Root { get; }

            public void Apply(AdminDownloadDevice device, AdminDownloadEntry row, bool selected, AdminPreparedFilesSnapshot state)
            {
                Device = device; Entry = row;
                var status = Presentation.EntryStatus(row);
                var title = Presentation.EntryTitle(row);
                var revoked = row.Status == "revoked";
                var cells = new EntryCells(title, string.Join(" · ", new[]
                {
                    row.Episode?.Title, Presentation.QualityLabel(row.EffectiveQuality),
                    string.IsNullOrEmpty(row.LocationName) ? null : T(DevKey + ".preparedOn", new { location = row.LocationName }),
                }.Where(x => !string.IsNullOrEmpty(x))), status.Label, status.Tone == "neutral" ? "neutral" : status.Tone, Presentation.FormatBytes(row.FileSize));
                _select.Sync(!revoked && selected, !revoked && !state.ActionPending, T(DevKey + ".selectEntry", new { title }));
                if (_model?.Title != cells.Title || _model?.Detail != cells.Detail)
                {
                    var stack = Stack(0);
                    var heading = Strong(cells.Title, 14); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
                    stack.Children.Add(heading);
                    stack.Children.Add(Text(cells.Detail, 12, true));
                    _text.Content = stack;
                }
                if (_model?.Status != cells.Status || _model?.Tone != cells.Tone) _pill.Content = Pill(cells.Status, cells.Tone);
                SetText(_size, cells.Size);
                _model = cells;
                _revoke.Visibility = revoked ? Visibility.Collapsed : Visibility.Visible;
                _sent.Visibility = revoked ? Visibility.Visible : Visibility.Collapsed;
                AutomationProperties.SetName(_revoke, T(DevKey + ".revokeEntry", new { title }));
                _revoke.IsEnabled = !state.ActionPending && !state.DestructiveBlocked;
            }
        }
    }

    /// <summary>
    /// The official revoke dialog for a whole device (<paramref name="entries"/> null) or exactly the listed downloads. What the dialog
    /// names is what is sent, whatever the selection became while it was open, and only under the authority it was opened under.
    /// </summary>
    private async void ConfirmRevoke(AdminDownloadDevice device, IReadOnlyList<AdminDownloadEntry>? entries, Action afterRevoke)
    {
        if (Controller.State.ActionPending || Controller.State.DestructiveBlocked) return;
        var wholeDevice = entries == null;
        var count = wholeDevice ? device.Copies : entries!.Count;
        var bytes = wholeDevice ? device.BytesOnDevice : entries!.Sum(e => e.Status == "completed" ? e.FileSize : 0);
        if (count == 0) return;
        MonitorAuthorityLease lease;
        try { lease = Controller.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (lease)
        {
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
            // The downloads it named belong to the authority it opened under: if that is no longer current nothing is sent.
            if (!Controller.IsActionCurrent(lease)) return;
            var target = wholeDevice
                ? AdminDownloadRevokeTarget.ForDevice(device, pause?.IsChecked == true)
                : AdminDownloadRevokeTarget.ForIds(entries!.Select(e => e.Id).ToArray());
            afterRevoke();
            await Controller.RevokeAsync(target, reason.Text.Trim(), device.DeviceName, lease);
        }
    }
}
