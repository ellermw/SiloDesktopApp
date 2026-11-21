using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// Visual evidence for the Libraries page refreshed against official Silo Server e130f262d (web/src/pages/AdminLibraries.tsx and
/// components/admin/libraries/*): the page captured at 1440, 1024, 800 and 560 device-independent pixels wide in each main state
/// (populated list with scans running, diagnostics, scan queue, editor tabs, a confirmation, empty, load error with Retry).
/// Every capture waits for the element it shows instead of sampling at one instant (a re-created ScrollViewer's children join the
/// visual tree one layout pass later). Synthetic server only; the captures are read by a person, they are not pixel comparisons.
/// </summary>
internal static partial class AdminLibrariesNativeFixture
{
    internal static readonly (int Width, int Height)[] MatrixViewports = [(1440, 900), (1024, 768), (800, 600), (560, 800)];

    /// <summary>Renders a popup's own content element (the presenter itself cannot be rasterized by this host); a NOTE, not a pass, when it still renders nothing.</summary>
    private static async Task CapturePopupContentAsync(FrameworkElement? content, string name)
    {
        if (content == null) { Check(false, name + " has popup content to capture"); return; }
        try
        {
            content.UpdateLayout();
            await Task.Delay(150);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(content);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) { Program.Log("NOTE: " + name + " not captured: the popup content rendered no pixels"); return; }
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
            var file = await folder.CreateFileAsync(name, Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();
            Program.Log("PASS: " + name + " captured (" + bitmap.PixelWidth + "x" + bitmap.PixelHeight + ")");
        }
        catch (ArgumentException) { Program.Log("NOTE: " + name + " not captured: the popup layer cannot be rendered to a bitmap here"); }
    }

    private static async Task RunVisualMatrixAsync(Window window, Frame outer, AdminShellPage shell, AdminLibrariesPage page, Server server)
    {
        var controller = page.Controller!;
        var scroll = (ScrollViewer)Field(page, "_scroll")!;
        var diagnostics = (FrameworkElement)Field(page, "_diagnostics")!;
        foreach (var (width, height) in MatrixViewports)
        {
            var tag = $"{width}x{height}";
            var wide = width >= 1024;
            var kept = server.Libraries;
            try
            {
                Resize(window, width, height);
                await Until(() => Find<FrameworkElement>(shell, wide ? "LibrariesTable" : "LibrariesCards") is { IsLoaded: true }
                                  && Find<FrameworkElement>(shell, wide ? "LibrariesCards" : "LibrariesTable") == null && controller.State.Scans.Count > 0, 8000);
                scroll.ChangeView(null, 0, null, true);
                await Task.Delay(250);
                Check(controller.State.Scans.Count > 0, $"[{tag}] scans are running in the populated list");
                await CaptureAsync(outer, $"libraries-matrix-{tag}-list.png");

                // Geometry the captures are checked against (official e130f262d: rounded-md badges, 44 px / flex-1 header actions below lg).
                var enabledBadge = Descendants(shell).OfType<Border>().FirstOrDefault(border => border.Child is TextBlock { Text: "Enabled" });
                Check(enabledBadge is { CornerRadius.TopLeft: 10 }, $"[{tag}] status badges use the theme rounded-md radius (10)");
                var addButton = Find<Button>(shell, "LibrariesAdd");
                if (!wide)
                {
                    var actions = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(addButton!);
                    Check(actions.ActualWidth > 0 && Math.Abs(addButton!.ActualWidth - actions.ActualWidth) < 1 && addButton.ActualHeight == 44,
                        $"[{tag}] Add library takes a full 44 px row below 1024 px (basis-full)");
                    var scanAllButton = Find<Button>(shell, "LibrariesScanAll")!;
                    var siblings = Descendants(actions).OfType<Button>().Where(button => button.Parent == actions && !ReferenceEquals(button, addButton)).ToList();
                    var lineTop = siblings.Min(button => button.TransformToVisual(actions).TransformPoint(new(0, 0)).Y);
                    var firstLine = siblings.Where(button => Math.Abs(button.TransformToVisual(actions).TransformPoint(new(0, 0)).Y - lineTop) < 1).ToList();
                    Check(Math.Abs(firstLine.Sum(button => button.ActualWidth) + 8 * (firstLine.Count - 1) - actions.ActualWidth) < 1.5,
                        $"[{tag}] the header actions on a line share its full width (flex-1)");
                    Check(scanAllButton.ActualHeight == 44, $"[{tag}] header actions are 44 px tall below 1024 px");
                }
                else
                {
                    var actionsHeader = Descendants(shell).OfType<TextBlock>().FirstOrDefault(text => text.Text == AdminLibrariesText.T("web.admin.libraries.column.actions"));
                    Check(actionsHeader?.Parent is Border { HorizontalAlignment: HorizontalAlignment.Left }, $"[{tag}] the Actions column header is start-aligned (TableHead text-start)");
                    // TableHead is h-10 px-2 py-3 with a 20 px line (text-sm): a table cell's height is a minimum, so 24 px of padding plus the line is 44 px, not 40.
                    Check(actionsHeader?.Parent is Border { ActualHeight: >= 43.5 and <= 44.5 }, $"[{tag}] the table header cells are 44 px tall (h-10 is a minimum; py-3 + a 20 px line) (is {(actionsHeader?.Parent as Border)?.ActualHeight})");
                }

                // The stop button keeps its hit area (20 px, 44 px on touch) but draws its states on a 20 px square; the card's Stop-scan button is the page's destructive style.
                await Until(() => Find<Button>(shell, "LibrariesStopScans_1") is { IsLoaded: true, ActualWidth: > 0 });
                CheckStopButtonShape(Find<Button>(shell, "LibrariesStopScans_1"), $"[{tag}] the Stop scans button", wide ? 20 : 44);
                if (!wide)
                {
                    await Until(() => Find<Button>(shell, "LibrariesCardScan_1") is { IsLoaded: true, ActualWidth: > 0 });
                    CheckDestructiveStates(Find<Button>(shell, "LibrariesCardScan_1"), $"[{tag}] the card Stop button");
                }

                // The diagnostics sections under the list.
                await Until(() => diagnostics.IsLoaded && diagnostics.ActualHeight > 0);
                var offset = diagnostics.TransformToVisual((UIElement)scroll.Content).TransformPoint(new(0, 0)).Y;
                scroll.ChangeView(null, Math.Max(0, offset - 8), null, true);
                await Task.Delay(300);
                await CaptureAsync(outer, $"libraries-matrix-{tag}-diagnostics.png");
                scroll.ChangeView(null, 0, null, true);
                await Task.Delay(150);

                // The active scan queue popover.
                await Until(() => Find<Button>(shell, "LibrariesScanQueue") is { IsLoaded: true });
                await InvokeAsync(Find<Button>(shell, "LibrariesScanQueue"));
                await Until(() => FindInPopups<FrameworkElement>(window, "LibrariesScanQueuePopover") != null);
                await CaptureAsync(outer, $"libraries-matrix-{tag}-scan-queue.png");
                await CapturePopupAsync(window, $"libraries-matrix-{tag}-scan-queue-popup.png");
                await CapturePopupContentAsync(FindInPopups<FrameworkElement>(window, "LibrariesScanQueuePopover"), $"libraries-matrix-{tag}-scan-queue-content.png");
                ClosePopups(window);
                await Task.Delay(150);

                // Editor: General, Folders, Metadata, Advanced.
                await Until(() => Find<Button>(shell, "LibrariesAdd") is { IsLoaded: true, IsEnabled: true });
                await InvokeAsync(Find<Button>(shell, "LibrariesAdd"));
                await Until(() => Find<FrameworkElement>(shell, "LibraryEditor") != null && Find<TextBox>(shell, "LibraryEditorName") is { IsLoaded: true });
                await CaptureAsync(outer, $"libraries-matrix-{tag}-editor-general.png");
                CheckEditorCloseButton(shell, tag, phone: width < 640);
                foreach (var section in new[] { "Folders", "Metadata", "Advanced" })
                {
                    await InvokeAsync(Find<Button>(shell, "LibraryEditorTab_" + section));
                    await Until(() => AutomationProperties.GetItemStatus(Find<Button>(shell, "LibraryEditorTab_" + section)!) == "selected");
                    await Task.Delay(400);
                    await CaptureAsync(outer, $"libraries-matrix-{tag}-editor-{section.ToLowerInvariant()}.png");
                }
                if (width < 640) await CheckPhoneFooterAsync(shell, tag);
                await InvokeAsync(Find<Button>(shell, "LibraryEditorCancel"));
                await Until(() => page.EditorSession == null && Find<FrameworkElement>(shell, "LibraryEditor") == null);

                // A destructive confirmation (the table's row button, or the card's menu below 1024 px).
                if (wide) await InvokeAsync(Find<Button>(shell, "LibrariesDelete_1"));
                else
                {
                    await Until(() => Find<Button>(shell, "LibrariesMenu_1") is { IsLoaded: true });
                    await InvokeAsync(Find<Button>(shell, "LibrariesMenu_1"));
                    await Until(() => FindInPopups<Button>(window, "LibrariesMenuDelete_1") != null);
                    await CaptureAsync(outer, $"libraries-matrix-{tag}-card-menu.png");
                    await CapturePopupAsync(window, $"libraries-matrix-{tag}-card-menu-popup.png");
                    var menuItem = FindInPopups<Button>(window, "LibrariesMenuDelete_1");
                    DependencyObject? menuHost = menuItem;
                    while (menuHost != null && !(menuHost is StackPanel { Width: 256 })) menuHost = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(menuHost);
                    await CapturePopupContentAsync(menuHost as FrameworkElement, $"libraries-matrix-{tag}-card-menu-content.png");
                    await InvokeAsync(FindInPopups<Button>(window, "LibrariesMenuDelete_1"));
                }
                await Until(() => OpenDialog(window) != null);
                CheckDestructiveStates(DialogControl<Button>(window, "LibrariesDeleteConfirm"), $"[{tag}] the delete confirmation's destructive button");
                await CaptureAsync(outer, $"libraries-matrix-{tag}-delete-confirm.png");
                await CapturePopupAsync(window, $"libraries-matrix-{tag}-delete-confirm-popup.png");
                OpenDialog(window)?.Hide();
                await Until(() => OpenDialog(window) == null);

                // Empty list.
                server.Libraries = [];
                try
                {
                    _ = controller.ReloadAsync();
                    await Until(() => controller.State.Libraries.Count == 0 && !controller.State.Fetching);
                    await Task.Delay(300);
                    scroll.ChangeView(null, 0, null, true);
                    await CaptureAsync(outer, $"libraries-matrix-{tag}-empty.png");
                }
                finally { server.Libraries = kept; } // a failure above must not leave the later viewports on an empty server list

                // A failed read shows the error with a reachable Retry; Retry recovers once the server answers again.
                server.LibrariesReadFails = true;
                _ = controller.ReloadAsync();
                await Until(() => controller.State.Error != null && Find<Button>(shell, "LibrariesRetry") is { IsLoaded: true });
                Check(Find<Button>(shell, "LibrariesRetry") is { IsEnabled: true }, $"[{tag}] a failed list read offers Retry");
                scroll.ChangeView(null, 0, null, true);
                await Task.Delay(250);
                await CaptureAsync(outer, $"libraries-matrix-{tag}-error.png");
                server.LibrariesReadFails = false;
                await InvokeAsync(Find<Button>(shell, "LibrariesRetry"));
                await Until(() => controller.State.Error == null && controller.State.Libraries.Count == kept.Count);
                Check(controller.State.Error == null && controller.State.Libraries.Count == kept.Count, $"[{tag}] Retry recovers the list");
            }
            catch (Exception error) when (error is InvalidOperationException)
            {
                Check(false, $"[{tag}] the visual matrix step failed: {error.Message}");
                server.LibrariesReadFails = false;
                ClosePopups(window);
                OpenDialog(window)?.Hide();
                if (page.EditorSession != null) await InvokeAsync(Find<Button>(shell, "LibraryEditorCancel"));
            }
            finally
            {
                // Whatever happened above, the next viewport starts from the full list on a working server.
                server.Libraries = kept;
                server.LibrariesReadFails = false;
                if (controller.State.Libraries.Count != kept.Count || controller.State.Error != null)
                {
                    _ = controller.ReloadAsync();
                    try { await Until(() => controller.State.Error == null && controller.State.Libraries.Count == kept.Count && !controller.State.Fetching); }
                    catch (InvalidOperationException) { Check(false, $"[{tag}] the list recovers after a failed matrix step"); }
                }
            }
        }
        Resize(window, 1440, 900);
        await Until(() => Find<FrameworkElement>(shell, "LibrariesTable") != null);
    }
}
