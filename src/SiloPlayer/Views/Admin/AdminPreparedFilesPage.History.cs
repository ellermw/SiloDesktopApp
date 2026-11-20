using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private UIElement BuildHistory(AdminPreparedFilesSnapshot state)
    {
        var root = Section("History", "Prepared files freed by expiry, budgets, administrators, and devices.");
        var storage = state.Storage.Value;
        if (storage != null)
        {
            var stats = Toolbar(
                Stat("Freed", AdminPreparedFilesPresentation.FormatBytes(storage.FreedLast30DaysBytes), "In the last 30 days"),
                Stat("Cache for", storage.CacheHours + " hours", "Before an unused file expires"),
                Stat("Disk ceiling", storage.DiskCeilingPercent + "%", "Clean-up starts here"),
                Stat("History kept", "90 days", "Server retention"));
            root.Children.Add(new ScrollViewer { Content = stats, HorizontalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled });
        }
        var query = state.HistoryQuery;
        var locations = new List<(string, string)> { ("", "All locations") };
        if (storage != null) locations.AddRange(storage.Locations.Select(x => (x.Key, x.Name)));
        locations.Add(("device", "Devices"));
        var reasons = new[]
        {
            ("", "Every reason"), ("cache_expired", "Expired from cache"), ("budget", "Over budget"),
            ("disk_ceiling", "Disk ceiling reached"), ("admin_delete", "Deleted by an administrator"),
            ("untracked", "Deleted untracked files"), ("missing", "Files found missing"),
            ("revoked", "Revoked downloads"), ("device_removed", "Removed from device"),
        };
        var filters = Toolbar(
            Select("PreparedFilesHistoryReason", reasons, query.Reason ?? "", value => _ = Controller.SetHistoryQueryAsync(Controller.State.HistoryQuery with { Reason = value.Length == 0 ? null : value })),
            Select("PreparedFilesHistoryLocation", locations, query.Location ?? "", value => _ = Controller.SetHistoryQueryAsync(Controller.State.HistoryQuery with { Location = value.Length == 0 ? null : value })),
            Select("PreparedFilesHistoryDays", new[] { ("1", "Last day"), ("7", "Last 7 days"), ("30", "Last 30 days"), ("0", "All kept history") }, query.Days.ToString(),
                value => _ = Controller.SetHistoryQueryAsync(Controller.State.HistoryQuery with { Days = int.Parse(value) })));
        root.Children.Add(Horizontal(filters));

        if (state.History.Loading && !state.History.Loaded) root.Children.Add(Message("Reading prepared-file history..."));
        else if (state.History.Error != null) root.Children.Add(Message(state.History.Error, "Try again", () => _ = Controller.ReloadHistoryAsync()));
        else if (state.History.Items.Count == 0) root.Children.Add(Message("No prepared-file history matches these filters."));
        else
        {
            foreach (var item in state.History.Items) root.Children.Add(BuildHistoryRow(item));
            if (state.History.NextCursor != null)
            {
                var more = Button(state.History.Loading ? "Loading..." : "Load more", "PreparedFilesHistoryLoadMore", () => _ = Controller.LoadMoreHistoryAsync());
                more.IsEnabled = !state.History.Loading; root.Children.Add(more);
            }
            if (state.History.Truncated) root.Children.Add(Message("Only the first 500 history rows are retained here. Narrow the filters to continue."));
        }
        return root;
    }

    private UIElement BuildHistoryRow(AdminDownloadStorageEvent item)
    {
        var root = new StackPanel { Spacing = 3 };
        root.Children.Add(Text(AdminPreparedFilesPresentation.HistoryReason(item.Reason), 14, FontWeights.SemiBold));
        var subject = item.Reason is "revoked" or "device_removed"
            ? $"{item.Count} {(item.Count == 1 ? "download" : "downloads")} on {(string.IsNullOrWhiteSpace(item.LocationName) ? "unknown device" : item.LocationName)}"
            : item.Count == 1 && item.Titles.Count > 0 ? item.Titles[0]
            : !string.IsNullOrWhiteSpace(item.Detail) && item.Reason == "untracked" ? item.Detail : $"{item.Count} files";
        root.Children.Add(Text(subject, 13));
        if (!string.IsNullOrWhiteSpace(item.Detail) && item.Reason != "untracked") root.Children.Add(Text(item.Detail, 12));
        if (item.Reason == "revoked") root.Children.Add(Text("Waiting for the device to confirm deletion.", 12));
        var location = item.Location == "device" ? "Device" : LocationName(Controller.State.Storage.Value, item.Location);
        var actor = item.Actor?.Username ?? (item.Reason == "device_removed" ? "Device confirmed" : "Automatic");
        root.Children.Add(Text($"{item.OccurredAt.ToLocalTime():g} · {location} · {AdminPreparedFilesPresentation.FormatBytes(item.Bytes)} · {actor}", 12));
        var card = Card(root, new Thickness(11));
        AutomationProperties.SetAutomationId(card, "PreparedFilesHistory_" + item.Id);
        return card;
    }
}
