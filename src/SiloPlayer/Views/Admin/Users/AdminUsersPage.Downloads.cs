using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private void RenderDeviceCopies(IEnumerable<JsonObject> filtered)
    {
        var rows = filtered.ToArray();
        if (rows.Length == 0) _body.Children.Add(Text(_rows.ContainsKey("downloads") ? "No matching device copies." : "No successful download snapshot."));
        foreach (var deviceRows in rows.GroupBy(row => row.Text("device_id")))
        {
            var device = Rows("devices").FirstOrDefault(row => row.Text("device_id") == deviceRows.Key);
            var android = device.Text("device_platform").Contains("android", StringComparison.OrdinalIgnoreCase);
            var content = Panel();
            content.Children.Add(Text("Device #" + deviceRows.Key + " · " + device.Text("device_platform") + " · Last reported " + device?["last_seen_at"].Display()));
            content.Children.Add(Text("Device reports are not live storage inspection. A revoked row is not proof the device has deleted its bytes."));
            if (android) content.Children.Add(Text("Android ready/downloading means Requested; completion and storage cannot be inferred from delivery."));
            if (device != null) DeviceLink(content, device);
            foreach (var group in deviceRows.GroupBy(row => row.Text("episode_id").Length == 0 ? "item:" + row.Text("id") : "series:" + row.Text("profile_id") + ":" + row.Text("content_id")))
            {
                var first = group.First();
                var series = first.Text("episode_id").Length > 0;
                var monitored = series && Rows("subscriptions").Any(monitor => monitor.Flag("active") && monitor.Text("device_id") == deviceRows.Key && monitor.Text("profile_id") == first.Text("profile_id") && monitor.Text("series_id") == first.Text("content_id"));
                var entries = Panel();
                foreach (var row in group.OrderBy(row => row["episode"].Count("season_number")).ThenBy(row => row["episode"].Count("episode_number")).ThenBy(row => row.Text("id"), StringComparer.Ordinal))
                {
                    var entry = Panel(4);
                    entry.Children.Add(Text(DownloadStatus(row.Text("status"), android), 16));
                    foreach (var key in new[] { "episode", "profile_id", "quality", "effective_quality", "delivery_format", "target_bitrate_kbps", "file_size", "created_at", "updated_at", "completed_at", "status_event_at" })
                        entry.Children.Add(Text(Label(key) + ": " + row[key].Display()));
                    entries.Children.Add(entry);
                }
                content.Children.Add(new Expander
                {
                    Header = first.Text("title", first.Text("content_id")) + " · Profile #" + first.Text("profile_id") + (series ? " · " + group.Count() + " episodes" : "") + (monitored ? " · Monitored" : ""),
                    Content = entries, IsExpanded = group.Any(row => row.Text("status") != "completed"), HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }
            _body.Children.Add(Card(device.Text("device_name", "Unknown or no longer registered device"), content));
        }
        if (_rows.GetValueOrDefault("downloads") is { Complete: false } state)
        {
            _body.Children.Add(Text("Partial device copies. Counts and groups describe retained pages only."));
            _body.Children.Add(Action("Load next device-copy page", () => MoreAsync("downloads"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
        }
    }
    private static string DownloadStatus(string status, bool android) => status switch
    {
        "completed" => "On device (reported)",
        "ready" or "downloading" when android => "Requested on Android (not proof of storage)",
        "ready" => "Waiting for device",
        "downloading" => "Downloading",
        "preparing" => "Server preparing",
        "failed" => "Failed",
        "revoked" => "Revoked",
        _ => status
    };
}
