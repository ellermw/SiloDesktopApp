using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Views.Admin;
using Windows.Foundation;

/// <summary>
/// Admin > Logging settings must fit its page at every window width the official page does: every control and every
/// text lies inside what the viewer can see of the page, and no text is cut with an ellipsis where the official page wraps.
/// </summary>
internal static partial class AdminMonitorLogsNativeFixture
{
    private static readonly int[] LoggingFitWidths = [560, 640, 800, 1024];

    /// <summary>Resizes the window to each width, waits for the layout to settle, and records a check for that width.</summary>
    private static async Task CheckLoggingFitsAsync(Window window, AdminLoggingSettingsPage page, string label, bool expandAdvanced)
    {
        var original = window.AppWindow.ClientSize;
        var openedServer = false; var openedRetention = false;
        if (expandAdvanced)
        {
            var controller = page.Controller!;
            if (!controller.State.ServerAdvancedOpen) { Invoke(Find<Button>(page, "LoggingAdvanced_server")); openedServer = true; await Until(() => controller.State.ServerAdvancedOpen); }
            if (!controller.State.RetentionAdvancedOpen) { Invoke(Find<Button>(page, "LoggingAdvanced_retention")); openedRetention = true; await Until(() => controller.State.RetentionAdvancedOpen); }
        }
        foreach (var width in LoggingFitWidths)
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
            var settled = await LoggingLayoutSettledAsync(window, page, width);
            var problems = settled ? LoggingFitProblems(page) : ["layout did not settle at " + width];
            Check(problems.Count == 0, $"{label}: every Logging control and text fits the page at {width}px" + (problems.Count == 0 ? "" : " (" + string.Join("; ", problems.Take(12)) + ")"));
        }
        if (openedServer) { Invoke(Find<Button>(page, "LoggingAdvanced_server")); await Until(() => !page.Controller!.State.ServerAdvancedOpen); }
        if (openedRetention) { Invoke(Find<Button>(page, "LoggingAdvanced_retention")); await Until(() => !page.Controller!.State.RetentionAdvancedOpen); }
        window.AppWindow.ResizeClient(original);
        await LoggingLayoutSettledAsync(window, page, original.Width);
    }

    /// <summary>Waits for the window content to reach the width and for the page's own bounds to stop changing.</summary>
    private static async Task<bool> LoggingLayoutSettledAsync(Window window, FrameworkElement page, int width)
    {
        string? previous = null; var identical = 0;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            page.UpdateLayout();
            if (window.Content is FrameworkElement content && Math.Abs(content.ActualWidth - width) <= 1)
            {
                var signature = string.Join("|", All(page).OfType<FrameworkElement>().Where(element => element is TextBlock or Control)
                    .Select(element => $"{element.ActualWidth:0.#}x{element.ActualHeight:0.#}@{Bounds(element, page).Left:0.#},{Bounds(element, page).Top:0.#}"));
                identical = signature == previous ? identical + 1 : 0; previous = signature;
                if (identical >= 4) return true;
            }
            await Task.Delay(25);
        }
        return false;
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement page)
        => element.TransformToVisual(page).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool OnScreen(FrameworkElement element, FrameworkElement page)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? current = element; current != null && !ReferenceEquals(current, page); current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible } || current is UIElement { Opacity: <= 0 }) return false;
        return true;
    }

    /// <summary>
    /// What the viewer sees of the page is its scroll viewport (the page's own width less any scroll bar) narrowed by every
    /// ancestor that is larger than the slot its parent gave it, because the layout cuts such a child at its slot. A control
    /// whose bounds go past that is cut, whatever its own width says. One pixel is allowed.
    /// </summary>
    private static List<string> LoggingFitProblems(AdminLoggingSettingsPage page)
    {
        var problems = new List<string>();
        var scroll = All(page).OfType<ScrollViewer>().First();
        var viewport = Bounds(scroll, page);
        var left = Math.Max(0, viewport.Left); var pageRight = Math.Min(page.ActualWidth, viewport.Left + scroll.ViewportWidth);
        // Only the page content is judged; the scroll bar and the floating save bar are other surfaces.
        foreach (var element in All((DependencyObject)scroll.Content).OfType<FrameworkElement>().Where(element => element is TextBlock or Control && OnScreen(element, page)))
        {
            var bounds = Bounds(element, page);
            var right = Math.Min(pageRight, SlotClipRight(element, page));
            var name = element switch { TextBlock text => "text '" + (text.Text.Length > 24 ? text.Text[..24] : text.Text) + "'", _ => element.GetType().Name + " " + Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element) };
            if (bounds.Left < left - 1 || bounds.Right > right + 1) problems.Add($"{name} spans {bounds.Left:0}..{bounds.Right:0}, visible {left:0}..{right:0}");
            else if (element is TextBlock { IsTextTrimmed: true }) problems.Add($"{name} is trimmed");
        }
        return problems;
    }

    /// <summary>The right edge, in page coordinates, at which the layout cuts this element or one of its ancestors for being wider than its slot.</summary>
    private static double SlotClipRight(FrameworkElement element, FrameworkElement page)
    {
        var clip = double.PositiveInfinity;
        for (var current = element; current != null && !ReferenceEquals(current, page); current = VisualTreeHelper.GetParent(current) as FrameworkElement)
        {
            if (VisualTreeHelper.GetParent(current) is not FrameworkElement parent) break;
            var slot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(current);
            if (current.ActualWidth > slot.Width + 1)
                clip = Math.Min(clip, parent.TransformToVisual(page).TransformPoint(new Point(slot.Right, 0)).X);
        }
        return clip;
    }
}
