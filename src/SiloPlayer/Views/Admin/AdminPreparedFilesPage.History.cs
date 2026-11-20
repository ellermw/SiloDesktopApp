using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private const string HistKey = "web.admin.downloads.history";

    /// <summary>
    /// The History tab (official <c>HistoryTab.tsx</c>): clean-up and revocation events with the storage policy summary. An event never
    /// changes once recorded, so each row is built once per event and kept; the summary tiles are redrawn only when the policy changed.
    /// </summary>
    private sealed class HistoryView : TabView
    {
        private static readonly double[] Weights = [180, 200, 320, 160, 110, 160];

        private readonly StackPanel _root = Stack(16);
        private readonly ContentControl _stats = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly LogsWrapPanel _filters = new() { Spacing = 8 };
        private readonly Choice _reason, _location, _days;
        private readonly StatusHost _banner = new(), _placeholder = new();
        private readonly StableTable _table;
        private readonly Button _more;
        private readonly TextBlock _truncated = Text("", 12, true);
        private readonly Dictionary<string, CellRow> _rows = new(StringComparer.Ordinal);
        private (long, int, int, bool)? _statsKey;

        public HistoryView(AdminPreparedFilesPage page) : base(page)
        {
            _reason = new Choice("PreparedFilesHistoryReason", T(HistKey + ".reasonLabel"), 220, value => Change(q => q with { Reason = value.Length == 0 ? null : value }));
            _location = new Choice("PreparedFilesHistoryLocation", T(HistKey + ".locationLabel"), 190, value => Change(q => q with { Location = value.Length == 0 ? null : value }));
            _days = new Choice("PreparedFilesHistoryDays", T(HistKey + ".whenLabel"), 170, value =>
            {
                if (int.TryParse(value, out var days)) Change(q => q with { Days = days });
            });
            foreach (var choice in new[] { _reason, _location, _days }) _filters.Children.Add(choice.Box);
            _table = new StableTable([HeaderText(T(HistKey + ".column.when")), HeaderText(T(HistKey + ".column.whatHappened")), HeaderText(T(HistKey + ".column.item")),
                HeaderText(T(HistKey + ".column.location")), HeaderText(T(HistKey + ".column.size"), end: true), HeaderText(T(HistKey + ".column.by"))], Weights);
            var inside = Stack(0);
            inside.Children.Add(_table); inside.Children.Add(_placeholder);
            var tablePanel = Panel(inside, 16, 4, true);
            _more = NewLoadMore(() => _ = P.Controller.LoadMoreHistoryAsync());
            foreach (UIElement child in new UIElement[] { _stats, _filters, _banner, tablePanel, _more, _truncated }) _root.Children.Add(child);
            _truncated.Visibility = Visibility.Collapsed;
        }

        public override UIElement Root => _root;

        public override void Reset()
        {
            base.Reset();
            _rows.Clear(); _table.Body.Children.Clear(); _statsKey = null;
        }

        private void Change(Func<AdminDownloadHistoryQuery, AdminDownloadHistoryQuery> edit) =>
            _ = P.Controller.SetHistoryQueryAsync(edit(P.Controller.State.HistoryQuery));

        protected override object Inputs(AdminPreparedFilesSnapshot state) => (state.HistoryQuery, state.History, state.Storage.Value);

        public override void Update(AdminPreparedFilesSnapshot state)
        {
            var settings = state.Storage.Value;
            var key = settings == null ? ((long, int, int, bool)?)null : (settings.FreedLast30DaysBytes, settings.CacheHours, settings.DiskCeilingPercent, true);
            if (_statsKey != key || _stats.Content == null)
            {
                _statsKey = key;
                var tiles = new LogsMetricGrid(4, 2);
                tiles.Add(Tile(T(HistKey + ".freed"), settings != null ? Presentation.FormatBytes(settings.FreedLast30DaysBytes) : "—", Text(T(HistKey + ".freedDetail"), 12, true)));
                tiles.Add(Tile(T(HistKey + ".cacheFor"), settings != null ? T(HistKey + ".cacheHours", new { count = settings.CacheHours, count_text = settings.CacheHours }) : "—", Text(T(HistKey + ".cacheDetail"), 12, true)));
                tiles.Add(Tile(T(HistKey + ".diskCeiling"), settings != null ? T(HistKey + ".diskCeilingValue", new { percent = settings.DiskCeilingPercent }) : "—", Text(T(HistKey + ".diskCeilingDetail"), 12, true)));
                tiles.Add(Tile(T(HistKey + ".kept"), T(HistKey + ".keptValue", new { count = 90 }), Text(T(HistKey + ".keptDetail"), 12, true)));
                _stats.Content = tiles;
            }

            var query = state.HistoryQuery;
            var reasons = new List<(string, string)> { ("", T(HistKey + ".everyReason")) };
            reasons.AddRange(Presentation.StorageEventReasons.Select(r => (r, Presentation.StorageEventReasonLabel(r))));
            _reason.Show(reasons, query.Reason ?? "");
            var locations = new List<(string, string)> { ("", T(HistKey + ".allLocations")) };
            locations.AddRange((settings?.Locations ?? []).Select(l => (l.Key, l.Name)));
            locations.Add(("device", T(HistKey + ".devices")));
            _location.Show(locations, query.Location ?? "");
            _days.Show([("1", T(HistKey + ".lastDays", new { count = 1 })), ("7", T(HistKey + ".lastDays", new { count = 7 })), ("30", T(HistKey + ".lastDays", new { count = 30 })),
                ("0", T(HistKey + ".allKept"))], query.Days.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var history = state.History;
            var rows = history.Items;
            if (history.Error != null && rows.Count > 0) _banner.Show("stale", () => StaleBanner("PreparedFilesHistoryReload", () => _ = P.Controller.ReloadHistoryAsync()));
            else _banner.Hide();

            var wanted = new List<UIElement>();
            if (!history.Loaded && history.Error == null) _placeholder.Show("loading", () => Skeleton(4));
            else if (rows.Count == 0)
            {
                if (history.Error != null) _placeholder.Show("error", () => Placeholder(T(HistKey + ".loadFailed"), "PreparedFilesHistoryReload", () => _ = P.Controller.ReloadHistoryAsync()));
                else _placeholder.Show("empty", () => Placeholder(T(HistKey + ".empty")));
            }
            else
            {
                _placeholder.Hide();
                var listed = rows.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var id in _rows.Keys.Where(id => !listed.Contains(id)).ToArray()) _rows.Remove(id);
                foreach (var item in rows)
                {
                    if (!_rows.TryGetValue(item.Id, out var row)) _rows[item.Id] = row = Build(item);
                    wanted.Add(row.Rule);
                }
            }
            SyncChildren(_table.Body, wanted);
            SyncLoadMore(_more, history.NextCursor != null, history.Loading);
            _truncated.Visibility = history.Truncated ? Visibility.Visible : Visibility.Collapsed;
            if (history.Truncated) SetText(_truncated, T("configure.native.preparedFiles.listTruncated", new { count = rows.Count }));
        }

        private static CellRow Build(AdminDownloadStorageEvent item)
        {
            var row = new CellRow(Weights);
            var itemCell = Stack(0);
            itemCell.Children.Add(Text(Presentation.StorageEventItem(item)));
            if (!string.IsNullOrEmpty(item.Detail) && item.Reason != "untracked") itemCell.Children.Add(Text(item.Detail, 12, true));
            else if (item.Reason == "revoked")
            {
                var waiting = Text(T(HistKey + ".waitingForDevice"), 12); waiting.Foreground = Brush("WarningBrush");
                itemCell.Children.Add(waiting);
            }
            var by = item.Actor?.Username ?? (item.Reason == "device_removed" ? T(HistKey + ".deviceConfirmed") : T(HistKey + ".automatic"));
            row.Hold(0, Cell(Format(item.OccurredAt)));
            row.Hold(1, Text(Presentation.StorageEventReasonLabel(item.Reason)));
            row.Hold(2, itemCell);
            row.Hold(3, Cell(Presentation.StorageEventLocation(item)));
            var size = Cell(Presentation.FormatBytes(item.Bytes)); size.HorizontalAlignment = HorizontalAlignment.Right; size.TextAlignment = TextAlignment.Right;
            row.Hold(4, size);
            row.Hold(5, Cell(by, true));
            AutomationProperties.SetAutomationId(row.Grid, "PreparedFilesHistoryRow_" + item.Id);
            return row;
        }
    }
}
