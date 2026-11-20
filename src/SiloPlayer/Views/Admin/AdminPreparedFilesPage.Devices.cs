using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private UIElement BuildDevices(AdminPreparedFilesSnapshot state)
    {
        var root = Section("Device copies", "Prepared downloads copied to each person's device.");
        var query = state.DeviceQuery;
        var stale = Button(query.Stale ? "Stale only (on)" : "Stale only", "PreparedFilesDevicesStale", () =>
        {
            _expandedDevices.Clear(); _selectedEntries.Clear();
            var next = !Controller.State.DeviceQuery.Stale;
            var request = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["tab"] = "devices" });
            if (next) request = request.WithQuery("stale", "1");
            if (AdminShellPage.TryReplace(this, request)) _request = request;
            _ = Controller.SetDeviceQueryAsync(Controller.State.DeviceQuery with { Stale = next });
        });
        root.Children.Add(Horizontal(Toolbar(
            SearchBox("PreparedFilesDevicesSearch", "Search people or devices", query.Search, value =>
            {
                _expandedDevices.Clear(); _selectedEntries.Clear();
                _ = Controller.SetDeviceQueryAsync(Controller.State.DeviceQuery with { Search = value });
            }),
            stale,
            Select("PreparedFilesDevicesSort", new[] { ("last_seen", "Last seen"), ("size", "Size on device") }, query.Sort, value =>
            {
                _expandedDevices.Clear(); _selectedEntries.Clear();
                _ = Controller.SetDeviceQueryAsync(Controller.State.DeviceQuery with { Sort = value });
            }))));

        if (state.Devices.Loading && !state.Devices.Loaded) root.Children.Add(Message("Reading device copies..."));
        else if (state.Devices.Error != null) root.Children.Add(Message(state.Devices.Error, "Try again", () => _ = Controller.ReloadDevicesAsync()));
        else if (state.Devices.Items.Count == 0) root.Children.Add(Message(query.Stale ? "No stale device copies." : "No device copies."));
        else
        {
            foreach (var device in state.Devices.Items) root.Children.Add(BuildDeviceRow(device, state));
            if (state.Devices.NextCursor != null)
            {
                var more = Button(state.Devices.Loading ? "Loading..." : "Load more", "PreparedFilesDevicesLoadMore", () => _ = Controller.LoadMoreDevicesAsync());
                more.IsEnabled = !state.Devices.Loading; root.Children.Add(more);
            }
            if (state.Devices.Truncated) root.Children.Add(Message("Only the first 500 devices are retained here. Narrow the filters to continue."));
        }
        return root;
    }

    private UIElement BuildDeviceRow(AdminDownloadDevice device, AdminPreparedFilesSnapshot state)
    {
        var key = AdminPreparedFilesController.DeviceKey(device);
        var open = _expandedDevices.Contains(key);
        var root = new StackPanel { Spacing = 9 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var expand = Button(open ? "Hide" : "Show", "PreparedFilesExpand_" + device.DeviceId, () => ToggleDevice(device));
        header.Children.Add(expand);
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(Text(string.IsNullOrWhiteSpace(device.DeviceName) ? "Unnamed device" : device.DeviceName, 15, FontWeights.SemiBold));
        identity.Children.Add(Text($"{(string.IsNullOrWhiteSpace(device.ProfileName) ? device.Username : device.ProfileName)} · {(string.IsNullOrWhiteSpace(device.Platform) ? "Unknown platform" : device.Platform)} · Last seen {AdminPreparedFilesPresentation.Relative(device.LastSeenAt)}", 12));
        identity.Children.Add(Text($"{device.Copies} copies · {device.Finished} finished, {device.Waiting} waiting, {device.Failed} failed · {AdminPreparedFilesPresentation.FormatBytes(device.BytesOnDevice)} on device", 12));
        Grid.SetColumn(identity, 1); header.Children.Add(identity);
        var revokeAll = Button("Revoke all", "PreparedFilesRevokeAll_" + device.DeviceId, () => ConfirmRevoke(device, null));
        revokeAll.IsEnabled = !state.ActionPending && device.Copies > 0;
        Grid.SetColumn(revokeAll, 2); header.Children.Add(revokeAll);
        root.Children.Add(header);

        if (open)
        {
            var entries = state.Entries.TryGetValue(key, out var found) ? found : new AdminPreparedPagedState<AdminDownloadEntry> { Loading = true };
            if (entries.Loading && !entries.Loaded) root.Children.Add(Text("Reading downloads...", 12));
            else if (entries.Error != null) root.Children.Add(Message(entries.Error, "Try again", () => _ = Controller.LoadEntriesAsync(device)));
            else if (entries.Items.Count == 0) root.Children.Add(Text("No downloads remain on this device.", 12));
            else
            {
                var selected = entries.Items.Where(x => _selectedEntries.Contains(x.Id)).ToArray();
                if (selected.Length > 0)
                {
                    var bulk = Toolbar(Text($"{selected.Length} selected", 12),
                        Button("Revoke selected", "PreparedFilesRevokeSelected_" + device.DeviceId, () => ConfirmRevoke(device, selected)),
                        Button("Clear", "PreparedFilesEntriesClear_" + device.DeviceId, () => { foreach (var entry in selected) _selectedEntries.Remove(entry.Id); Render(Controller.State); }));
                    root.Children.Add(Card(bulk, new Thickness(8)));
                }
                foreach (var entry in entries.Items) root.Children.Add(BuildEntryRow(device, entry, state.ActionPending));
                if (entries.NextCursor != null)
                {
                    var more = Button(entries.Loading ? "Loading..." : "Show more", "PreparedFilesEntriesMore_" + device.DeviceId,
                        () => _ = Controller.LoadEntriesAsync(device, append: true));
                    more.IsEnabled = !entries.Loading; root.Children.Add(more);
                }
                if (entries.Truncated) root.Children.Add(Text("Only the first 500 downloads are retained for this device.", 12));
            }
        }
        var card = Card(root);
        AutomationProperties.SetAutomationId(card, "PreparedFilesDevice_" + device.DeviceId);
        return card;
    }

    private async void ToggleDevice(AdminDownloadDevice device)
    {
        var key = AdminPreparedFilesController.DeviceKey(device);
        if (!_expandedDevices.Add(key)) { _expandedDevices.Remove(key); Render(Controller.State); return; }
        _expandedDevices.Clear(); _expandedDevices.Add(key); _selectedEntries.Clear();
        Render(Controller.State);
        await Controller.LoadEntriesAsync(device);
    }

    private UIElement BuildEntryRow(AdminDownloadDevice device, AdminDownloadEntry entry, bool busy)
    {
        var root = new Grid { ColumnSpacing = 9 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chosen = _selectedEntries.Contains(entry.Id);
        var select = Button(chosen ? "Selected" : "Select", "PreparedFilesEntrySelect_" + entry.Id, () =>
        {
            if (!_selectedEntries.Add(entry.Id)) _selectedEntries.Remove(entry.Id);
            Render(Controller.State);
        });
        root.Children.Add(select);
        var content = new StackPanel { Spacing = 2 };
        var title = entry.Episode == null ? entry.Title : $"{entry.Title} · S{entry.Episode.SeasonNumber:00}E{entry.Episode.EpisodeNumber:00} {entry.Episode.Title}";
        content.Children.Add(Text(string.IsNullOrWhiteSpace(title) ? "Unknown title" : title, 14, FontWeights.SemiBold));
        var quality = entry.EffectiveQuality.Length > 0 ? entry.EffectiveQuality : entry.Quality;
        content.Children.Add(Text($"{entry.Status} · {quality} · {AdminPreparedFilesPresentation.FormatBytes(entry.FileSize)}" +
            (entry.Location.Length > 0 ? $" · prepared on {LocationName(Controller.State.Storage.Value, entry.Location)}" : ""), 12));
        Grid.SetColumn(content, 1); root.Children.Add(content);
        var revoke = Button("Revoke", "PreparedFilesRevoke_" + entry.Id, () => ConfirmRevoke(device, [entry]));
        revoke.IsEnabled = !busy && entry.RevokedAt == null;
        Grid.SetColumn(revoke, 2); root.Children.Add(revoke);
        var card = Card(root, new Thickness(10));
        AutomationProperties.SetAutomationId(card, "PreparedFilesEntry_" + entry.Id);
        return card;
    }

    private async void ConfirmRevoke(AdminDownloadDevice device, IReadOnlyList<AdminDownloadEntry>? entries)
    {
        var count = entries?.Count ?? device.Copies;
        if (count <= 0) return;
        var bytes = entries?.Sum(x => x.FileSize) ?? device.BytesOnDevice;
        var prompt = AdminPreparedFilesPresentation.RevokePrompt(device.DeviceName, string.IsNullOrWhiteSpace(device.ProfileName) ? device.Username : device.ProfileName,
            count, bytes, device.Stale, Controller.State.Storage.Value?.CacheHours ?? 0);
        var reason = new TextBox { PlaceholderText = "Reason (optional)", MaxLength = 500 };
        AutomationProperties.SetAutomationId(reason, "PreparedFilesRevokeReason");
        var extra = new StackPanel { Spacing = 8 };
        extra.Children.Add(Text(prompt.CacheEffect, 12));
        if (prompt.StaleEffect.Length > 0) extra.Children.Add(Text(prompt.StaleEffect, 12));
        CheckBox? pause = null;
        if (entries == null && device.Monitors > 0)
        {
            pause = new CheckBox { Content = $"Pause {device.Monitors} monitored {(device.Monitors == 1 ? "download" : "downloads")}", IsChecked = true };
            AutomationProperties.SetAutomationId(pause, "PreparedFilesRevokePauseMonitors");
            extra.Children.Add(pause);
            extra.Children.Add(Text("This prevents the same titles from being downloaded again automatically.", 12));
        }
        extra.Children.Add(reason);
        extra.Children.Add(Text("Older Silo apps may not remove revoked copies until they next refresh their download list.", 12));
        if (!await ConfirmAsync(prompt.Title, prompt.Description, prompt.ConfirmLabel, extra)) return;
        var target = entries == null ? AdminDownloadRevokeTarget.ForDevice(device, pause?.IsChecked == true)
            : AdminDownloadRevokeTarget.ForIds(entries.Select(x => x.Id).ToArray());
        if (entries != null) foreach (var entry in entries) _selectedEntries.Remove(entry.Id);
        await Controller.RevokeAsync(target, reason.Text);
        if (_expandedDevices.Contains(AdminPreparedFilesController.DeviceKey(device))) await Controller.LoadEntriesAsync(device);
    }
}
