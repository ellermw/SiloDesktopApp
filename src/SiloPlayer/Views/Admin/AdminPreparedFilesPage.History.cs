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

    /// <summary>The History tab (official <c>HistoryTab.tsx</c>): clean-up and revocation events with the storage policy summary.</summary>
    private sealed class HistoryView : TabView
    {
        private readonly StackPanel _root = Stack(16);
        private readonly StackPanel _stats = Stack(0);
        private readonly LogsWrapPanel _filters = new() { Spacing = 8 };
        private readonly Choice _reason, _location, _days;
        private readonly StackPanel _dynamic = Stack(16);

        public HistoryView(AdminPreparedFilesPage page) : base(page)
        {
            _reason = new Choice("PreparedFilesHistoryReason", T(HistKey + ".reasonLabel"), 220, value => Change(q => q with { Reason = value.Length == 0 ? null : value }));
            _location = new Choice("PreparedFilesHistoryLocation", T(HistKey + ".locationLabel"), 190, value => Change(q => q with { Location = value.Length == 0 ? null : value }));
            _days = new Choice("PreparedFilesHistoryDays", T(HistKey + ".whenLabel"), 170, value =>
            {
                if (int.TryParse(value, out var days)) Change(q => q with { Days = days });
            });
            foreach (var choice in new[] { _reason, _location, _days }) _filters.Children.Add(choice.Box);
            _root.Children.Add(_stats); _root.Children.Add(_filters); _root.Children.Add(_dynamic);
        }

        public override UIElement Root => _root;

        private void Change(Func<AdminDownloadHistoryQuery, AdminDownloadHistoryQuery> edit) =>
            _ = P.Controller.SetHistoryQueryAsync(edit(P.Controller.State.HistoryQuery));

        public override void Update(AdminPreparedFilesSnapshot state)
        {
            var settings = state.Storage.Value;
            _stats.Children.Clear();
            var tiles = new LogsMetricGrid(4, 2);
            tiles.Add(Tile(T(HistKey + ".freed"), settings != null ? Presentation.FormatBytes(settings.FreedLast30DaysBytes) : "—", Text(T(HistKey + ".freedDetail"), 12, true)));
            tiles.Add(Tile(T(HistKey + ".cacheFor"), settings != null ? T(HistKey + ".cacheHours", new { count = settings.CacheHours, count_text = settings.CacheHours }) : "—", Text(T(HistKey + ".cacheDetail"), 12, true)));
            tiles.Add(Tile(T(HistKey + ".diskCeiling"), settings != null ? T(HistKey + ".diskCeilingValue", new { percent = settings.DiskCeilingPercent }) : "—", Text(T(HistKey + ".diskCeilingDetail"), 12, true)));
            tiles.Add(Tile(T(HistKey + ".kept"), T(HistKey + ".keptValue", new { count = 90 }), Text(T(HistKey + ".keptDetail"), 12, true)));
            _stats.Children.Add(tiles);

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

            _dynamic.Children.Clear();
            var history = state.History;
            var rows = history.Items;
            if (history.Error != null && rows.Count > 0) _dynamic.Children.Add(StaleBanner("PreparedFilesHistoryReload", () => _ = P.Controller.ReloadHistoryAsync()));
            var table = new LogsNativeTable([T(HistKey + ".column.when"), T(HistKey + ".column.whatHappened"), T(HistKey + ".column.item"), T(HistKey + ".column.location"), T(HistKey + ".column.size"), T(HistKey + ".column.by")],
                [180, 200, 320, 160, 110, 160]);
            table.AlignEnd(4);
            if (!history.Loaded && history.Error == null)
                for (var i = 0; i < 4; i++) table.AddFull(Text("…", 14, true), 36);
            else if (rows.Count == 0)
            {
                var empty = Stack(12);
                var message = Text(history.Error != null ? T(HistKey + ".loadFailed") : T(HistKey + ".empty"), 14, true);
                message.HorizontalAlignment = HorizontalAlignment.Center; message.TextAlignment = TextAlignment.Center;
                empty.Children.Add(message);
                if (history.Error != null)
                {
                    var reload = Button(T("common.tryAgain"), "PreparedFilesHistoryReload");
                    reload.HorizontalAlignment = HorizontalAlignment.Center;
                    reload.Click += (_, _) => _ = P.Controller.ReloadHistoryAsync();
                    empty.Children.Add(reload);
                }
                table.AddFull(empty, 120);
            }
            else
                foreach (var item in rows)
                {
                    var itemCell = Stack(0);
                    itemCell.Children.Add(Text(Presentation.StorageEventItem(item)));
                    if (!string.IsNullOrEmpty(item.Detail) && item.Reason != "untracked") itemCell.Children.Add(Text(item.Detail, 12, true));
                    else if (item.Reason == "revoked")
                    {
                        var waiting = Text(T(HistKey + ".waitingForDevice"), 12); waiting.Foreground = Brush("WarningBrush");
                        itemCell.Children.Add(waiting);
                    }
                    var by = item.Actor?.Username ?? (item.Reason == "device_removed" ? T(HistKey + ".deviceConfirmed") : T(HistKey + ".automatic"));
                    var grid = table.Add([Cell(Format(item.OccurredAt)), Text(Presentation.StorageEventReasonLabel(item.Reason)), itemCell,
                        Cell(Presentation.StorageEventLocation(item)), Cell(Presentation.FormatBytes(item.Bytes)), Cell(by, true)]);
                    AutomationProperties.SetAutomationId(grid, "PreparedFilesHistoryRow_" + item.Id);
                }
            _dynamic.Children.Add(Panel(table, 16, 4, true));
            if (history.NextCursor != null) _dynamic.Children.Add(LoadMore(history.Loading, () => _ = P.Controller.LoadMoreHistoryAsync()));
            if (history.Truncated) _dynamic.Children.Add(Text(T("configure.native.preparedFiles.listTruncated", new { count = rows.Count }), 12, true));
        }
    }
}
