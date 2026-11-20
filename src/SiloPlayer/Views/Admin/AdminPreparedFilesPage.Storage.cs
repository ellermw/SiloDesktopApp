using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private UIElement BuildStorage(AdminPreparedFilesSnapshot state)
    {
        if (state.Storage.Loading && !state.Storage.Loaded) return Message("Reading prepared-file storage...");
        if (state.Storage.Error != null || state.Storage.Value == null)
            return Message(state.Storage.Error ?? "Download storage could not be read.", "Try again", () => _ = Controller.ReloadStorageAsync());

        var storage = state.Storage.Value;
        var root = new StackPanel { Spacing = 16 };
        var onDisk = storage.Locations.Sum(x => x.Usage == null ? 0 : x.Usage.Bytes + x.Usage.PartialBytes + x.Usage.OtherBytes);
        var inUse = storage.Locations.Sum(x => x.InUseBytes);
        var cached = storage.Locations.Sum(x => x.CachedBytes);
        var untracked = storage.Locations.Sum(x => x.UntrackedBytes);
        var files = storage.Locations.Sum(x => x.Usage?.Files ?? x.InUseFiles + x.CachedFiles);

        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stats.Children.Add(Stat("On disk", AdminPreparedFilesPresentation.FormatBytes(onDisk), $"{storage.Locations.Count} locations · {files} files"));
        stats.Children.Add(Stat("In use", AdminPreparedFilesPresentation.FormatBytes(inUse), "Needed by active downloads"));
        stats.Children.Add(Stat("Cached", AdminPreparedFilesPresentation.FormatBytes(cached), $"Kept for {storage.CacheHours} hours"));
        stats.Children.Add(Stat("Untracked", AdminPreparedFilesPresentation.FormatBytes(untracked), "Not known to Silo"));
        stats.Children.Add(Stat("Freed", AdminPreparedFilesPresentation.FormatBytes(storage.FreedLast30DaysBytes),
            storage.PreparingJobs > 0 ? $"{storage.PreparingJobs} preparing now" : "Nothing preparing"));
        root.Children.Add(new ScrollViewer { Content = stats, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled });

        foreach (var warning in AdminPreparedFilesPresentation.StorageWarnings(storage))
            root.Children.Add(BuildStorageWarning(warning, storage));

        var locations = new StackPanel { Spacing = 12 };
        foreach (var location in storage.Locations) locations.Children.Add(BuildLocationCard(location, storage, state.ActionPending));
        root.Children.Add(locations);
        root.Children.Add(Text($"Disk ceiling: {storage.DiskCeilingPercent}% · Node scratch admission stops at 95%", 12));
        return root;
    }

    private UIElement BuildStorageWarning(AdminStorageWarning warning, AdminDownloadStorage storage)
    {
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(Text(warning.Title, 14, FontWeights.SemiBold));
        row.Children.Add(Text(warning.Body, 13));
        if (warning.Action != null)
        {
            var label = warning.Action switch
            {
                "edit_location" => "Edit location", "review_untracked" => "Review",
                "review_devices" => "Review devices", "view_location" => "View files", _ => "Review",
            };
            row.Children.Add(Button(label, "PreparedFilesWarning_" + warning.Id, () =>
            {
                var location = warning.Location == null ? null : storage.Locations.FirstOrDefault(x => x.Key == warning.Location);
                if (warning.Action == "review_devices") OpenDevices(stale: true);
                else if (warning.Action == "view_location" && location != null) OpenFilesAt(location.Key);
                else if (warning.Action == "review_untracked" && location != null) ReviewUntracked(location);
                else if (warning.Action == "edit_location" && location != null) EditLocation(location, storage);
            }));
        }
        var card = Card(row, new Thickness(14));
        card.BorderBrush = Brush(warning.Tone == "warning" ? "SystemFillColorCautionBrush" : "SystemFillColorAttentionBrush",
            warning.Tone == "warning" ? Microsoft.UI.Colors.DarkGoldenrod : Microsoft.UI.Colors.SteelBlue);
        return card;
    }

    private UIElement BuildLocationCard(AdminDownloadStorageLocation location, AdminDownloadStorage storage, bool busy)
    {
        var root = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(Text(location.Name, 17, FontWeights.SemiBold));
        heading.Children.Add(Text(location.Kind == "node" ? location.Online ? "Online" : "Offline" : "Server", 12));
        header.Children.Add(heading);
        var cleanup = Button("Clean up", "PreparedFilesCleanup_" + location.Key, () => _ = Controller.CleanupLocationAsync(location));
        cleanup.IsEnabled = !busy && (location.Kind != "node" || location.Online);
        Grid.SetColumn(cleanup, 1); header.Children.Add(cleanup);
        root.Children.Add(header);
        root.Children.Add(Text(string.IsNullOrWhiteSpace(location.Dir) ? "Default node data directory" : location.Dir, 12));
        if (!string.IsNullOrWhiteSpace(location.PendingDir))
            root.Children.Add(Text($"Will use {location.PendingDir} after {location.Name} restarts.", 12));

        if (location.Usage is { } usage && usage.FSTotalBytes > 0)
        {
            var fill = AdminPreparedFilesPresentation.DiskFillPercent(location) ?? 0;
            root.Children.Add(Text($"Disk used {AdminPreparedFilesPresentation.FormatBytes(usage.FSUsedBytes)} of {AdminPreparedFilesPresentation.FormatBytes(usage.FSTotalBytes)} ({Math.Round(fill)}%)", 12));
            root.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = fill, Height = 8 });
            if (usage.Stale || (location.Kind == "node" && !location.Online))
                root.Children.Add(Text("Measurement " + AdminPreparedFilesPresentation.Relative(usage.MeasuredAt), 11));
        }
        else root.Children.Add(Text(location.Kind == "node" && !location.Online ? "No disk measurement while this node is offline." : "Disk measurement unavailable.", 12));

        root.Children.Add(Text($"In use  {AdminPreparedFilesPresentation.FormatBytes(location.InUseBytes)} ({location.InUseFiles})   ·   Cached  {AdminPreparedFilesPresentation.FormatBytes(location.CachedBytes)} ({location.CachedFiles})", 13));
        root.Children.Add(Text($"Untracked  {AdminPreparedFilesPresentation.FormatBytes(location.UntrackedBytes)} ({location.UntrackedFiles})   ·   Waiting  {location.WaitingDownloads}", 13));
        root.Children.Add(Text(location.BudgetBytes > 0
            ? $"Budget {AdminPreparedFilesPresentation.FormatBytes(location.BudgetBytes)} ({(location.BudgetSource == "override" ? "custom" : "default")})"
            : "No prepared-file budget", 12));

        var actions = Toolbar();
        actions.Children.Add(Button("Browse files", "PreparedFilesBrowse_" + location.Key, () => OpenFilesAt(location.Key)));
        var edit = Button("Edit location", "PreparedFilesEditLocation_" + location.Key, () => EditLocation(location, storage));
        edit.IsEnabled = !busy; actions.Children.Add(edit);
        if (location.UntrackedBytes > 0)
        {
            var untracked = Button("Delete untracked", "PreparedFilesReviewUntracked_" + location.Key, () => ReviewUntracked(location));
            untracked.IsEnabled = !busy; actions.Children.Add(untracked);
        }
        root.Children.Add(actions);

        var card = Card(root);
        AutomationProperties.SetAutomationId(card, "PreparedFilesStorageCard_" + location.Key);
        return card;
    }

    private async void ReviewUntracked(AdminDownloadStorageLocation location)
    {
        var description = $"Delete {location.UntrackedFiles} untracked {(location.UntrackedFiles == 1 ? "file" : "files")} ({AdminPreparedFilesPresentation.FormatBytes(location.UntrackedBytes)}) from {location.Name}? " +
            "This only deletes files no prepared-file record accounts for. It cannot be undone.";
        if (await ConfirmAsync($"Delete untracked files on {location.Name}?", description, "Delete untracked files"))
            await Controller.DeleteUntrackedAsync(location);
    }
}
