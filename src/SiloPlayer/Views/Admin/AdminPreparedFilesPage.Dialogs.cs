using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private async void EditLocation(AdminDownloadStorageLocation location, AdminDownloadStorage storage)
    {
        if (_dialog != null || XamlRoot == null) return;
        if (location.Kind != "node")
        {
            var details = new StackPanel { Spacing = 8 };
            details.Children.Add(Text("Directory", 12, FontWeights.SemiBold));
            details.Children.Add(Text(location.Dir, 13));
            details.Children.Add(Text("Default budget", 12, FontWeights.SemiBold));
            details.Children.Add(Text(storage.DefaultBudgetBytes > 0 ? AdminPreparedFilesPresentation.FormatBytes(storage.DefaultBudgetBytes) : "No budget", 13));
            details.Children.Add(Text($"Cached files are kept for {storage.CacheHours} hours. Clean-up starts at {storage.DiskCeilingPercent}% disk use.", 13));
            var info = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Server prepared-file location", Content = details,
                PrimaryButtonText = "Open Download settings", CloseButtonText = "Close",
            };
            _dialog = info;
            try
            {
                if (await info.ShowAsync() == ContentDialogResult.Primary)
                    AdminShellPage.Open(new AdminRouteRequest(AdminRoute.DownloadSettings));
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(_dialog, info)) _dialog = null; }
            return;
        }

        MonitorAuthorityLease action;
        try { action = Controller.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            IReadOnlyList<AdminPreparedFilesNode> nodes;
            try { nodes = await Controller.ListNodesAsync(action); }
            catch (Exception) { await ShowLocationError("The node configuration could not be read. Reload and try again."); return; }
            if (!Controller.IsActionCurrent(action)) return;
            var node = nodes.FirstOrDefault(x => x.Id == location.NodeId);
            if (node == null) { await ShowLocationError("This storage node is no longer in the current node list."); return; }

            var directory = new TextBox { Text = node.DownloadArtifactDirOverride ?? "", PlaceholderText = storage.Locations.FirstOrDefault(x => x.Kind == "server")?.Dir ?? "/data/downloads" };
            AutomationProperties.SetAutomationId(directory, "PreparedFilesLocationDirectory");
            var initialMode = node.DownloadArtifactMaxBytesOverride == null ? "default" : node.DownloadArtifactMaxBytesOverride == 0 ? "none" : "custom";
            var budgetMode = Select("PreparedFilesLocationBudgetMode",
                new[] { ("default", "Use default"), ("custom", "Custom"), ("none", "No budget") }, initialMode, _ => { });
            var budget = new TextBox
            {
                Text = node.DownloadArtifactMaxBytesOverride is > 0 ? (node.DownloadArtifactMaxBytesOverride.Value / 1_000_000_000d).ToString("0.###", CultureInfo.CurrentCulture) : "",
                PlaceholderText = "GB", InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } },
            };
            AutomationProperties.SetAutomationId(budget, "PreparedFilesLocationBudget");
            void ShowBudget()
            {
                var selectedMode = (budgetMode.SelectedItem as ComboBoxItem)?.Tag as string;
                budget.Visibility = selectedMode == "custom" ? Visibility.Visible : Visibility.Collapsed;
            }
            budgetMode.SelectionChanged += (_, _) => ShowBudget(); ShowBudget();
            var error = Text("", 12); error.Foreground = Brush("SystemFillColorCriticalBrush", Microsoft.UI.Colors.IndianRed); error.Visibility = Visibility.Collapsed;
            var content = new StackPanel { Spacing = 9 };
            content.Children.Add(Text("Directory", 13, FontWeights.SemiBold)); content.Children.Add(directory);
            content.Children.Add(Text("Leave blank to follow the server directory. Changing a directory does not move existing prepared files.", 12));
            if (location.Usage?.SharesScratch == true) content.Children.Add(Text("This directory currently shares a disk with transcode scratch.", 12));
            content.Children.Add(Text("Prepared-file budget", 13, FontWeights.SemiBold)); content.Children.Add(budgetMode); content.Children.Add(budget);
            content.Children.Add(Text($"This location currently holds {AdminPreparedFilesPresentation.FormatBytes(location.InUseBytes + location.CachedBytes)} of indexed prepared files.", 12));
            content.Children.Add(error);

            while (Controller.IsActionCurrent(action))
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = $"Prepared files on {location.Name}", Content = content,
                    PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
                };
                _dialog = dialog;
                ContentDialogResult result;
                try { result = await dialog.ShowAsync(); }
                catch (OperationCanceledException) { return; }
                finally { dialog.Content = null; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
                if (result != ContentDialogResult.Primary) return;

                var trimmed = directory.Text.Trim();
                var mode = (budgetMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "default";
                long? budgetBytes = null;
                if (trimmed.Length > 0 && (!trimmed.StartsWith('/') || trimmed.Length > 4096))
                {
                    error.Text = "Use an absolute path on the node, beginning with /."; error.Visibility = Visibility.Visible; continue;
                }
                if (mode == "none") budgetBytes = 0;
                else if (mode == "custom")
                {
                    if (!double.TryParse(budget.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var gb) || !double.IsFinite(gb) || gb <= 0 || gb > long.MaxValue / 1_000_000_000d)
                    {
                        error.Text = "Enter a custom budget greater than zero in GB."; error.Visibility = Visibility.Visible; continue;
                    }
                    budgetBytes = checked((long)Math.Round(gb * 1_000_000_000d));
                }
                var desiredDirectory = trimmed.Length == 0 ? null : trimmed;
                try
                {
                    await Controller.SaveNodeLocationAsync(action, node, desiredDirectory, budgetBytes);
                    return;
                }
                catch (Exception)
                {
                    if (!Controller.IsActionCurrent(action)) return;
                    IReadOnlyList<AdminPreparedFilesNode> current;
                    try { current = await Controller.ListNodesAsync(action); }
                    catch (Exception) { await ShowLocationError("The node save could not be reconciled. Your draft was not sent again; reopen the location after it can be reloaded."); return; }
                    var refreshed = current.FirstOrDefault(x => x.Id == node.Id);
                    if (refreshed == null) { await ShowLocationError("This storage node is no longer in the current node list."); return; }
                    if (refreshed.DownloadArtifactDirOverride == desiredDirectory && refreshed.DownloadArtifactMaxBytesOverride == budgetBytes)
                        return;
                    node = refreshed;
                    error.Text = "The node changed or the save was not applied. Its current validator was reloaded; review the preserved draft before choosing Save again.";
                    error.Visibility = Visibility.Visible;
                }
            }
        }
    }

    private async Task ShowLocationError(string message)
    {
        if (_dialog != null || XamlRoot == null) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Location not saved", Content = Text(message), CloseButtonText = "Close" };
        _dialog = dialog;
        try { await dialog.ShowAsync(); } catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
}
