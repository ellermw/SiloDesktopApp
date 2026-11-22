using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using Windows.Foundation;

/// <summary>
/// Layout that must hold on a display scaled to 125% or 150% while this machine runs at 100%. Selector <c>scaled-display-layout</c>.
/// Desired sizes are layout-rounded to the device pixel grid, so a gap, border or padding that is not on the grid makes a panel's rounded
/// width a fraction smaller than what its children need. Here a fraction stands in for that: a probe element that needs 100.4 reports a rounded
/// desired width of 100, a gap of 0.4 stands in for a 1px gap at 125%, and a host is a fraction short. Each check lays the panel out in the live
/// tree (an element outside the tree is not arranged), reads what the panel did with its children, and removes the host again.
/// Mechanism 1 is a child measured again at exactly its own desired width, or a fit decision made with an exact comparison.
/// Mechanism 2 is text inside a button whose rounded corners clip it (padding smaller than the corner radius).
/// </summary>
internal static class ScaledDisplayLayoutNativeFixture
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly App = typeof(SiloPlayer.Views.Admin.AdminShellPage).Assembly;
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    /// <summary>
    /// An element that needs <see cref="Natural"/> to show its content untrimmed and trims below it (like a no-wrap text with an ellipsis).
    /// It records the width it was last offered, so a panel that measured it at exactly its rounded desired width shows up as trimmed.
    /// </summary>
    private sealed class Probe : Panel
    {
        internal Probe(double natural, double tall = 10) { Natural = natural; Tall = tall; }
        internal double Natural { get; }
        internal double Tall { get; }
        internal double LastWidth { get; private set; } = double.NaN;
        internal double ArrangedWidth { get; private set; } = double.NaN;
        internal bool Trimmed => LastWidth < Natural - 0.001;
        protected override Size MeasureOverride(Size availableSize) { LastWidth = availableSize.Width; return new Size(Math.Min(Natural, availableSize.Width), Tall); }
        protected override Size ArrangeOverride(Size finalSize) { ArrangedWidth = finalSize.Width; return finalSize; }
        public override string ToString() => $"probe need {Natural:0.##}: offered {LastWidth:0.###}, desired {DesiredSize.Width:0.###}, arranged {ArrangedWidth:0.###}, slot {Slot(this).Width:0.###}";
    }

    private static Rect Slot(UIElement element) => LayoutInformation.GetLayoutSlot((FrameworkElement)element);
    private static Border Box(double width, double height) => new() { Width = width, Height = height };
    private static Type AppType(string name) => App.GetType(name) ?? throw new InvalidOperationException("Missing type " + name);
    private static object Make(string typeName, params object?[] args) => Activator.CreateInstance(AppType(typeName), Any, null, args, null)!;
    private static void Set(object target, string property, object value) => (target.GetType().GetProperty(property, Any) ?? throw new InvalidOperationException(property)).SetValue(target, value);
    private static T Field<T>(object target, string name) => (T)(target.GetType().GetField(name, Any) ?? throw new InvalidOperationException(name)).GetValue(target)!;

    private static async Task<Grid> Show(StackPanel parent, UIElement content, double width)
    {
        var host = new Grid { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(content);
        parent.Children.Add(host);
        await Settle(host);
        return host;
    }

    private static async Task Settle(UIElement host) { host.UpdateLayout(); await Task.Delay(60); host.UpdateLayout(); }
    private static async Task Resize(Grid host, double width) { host.Width = width; await Settle(host); }
    private static void Hide(StackPanel parent, Grid host) => parent.Children.Remove(host);

    private static async Task Scenario(string name, Func<Task> run)
    {
        try { await run(); }
        catch (Exception error) { Check(false, $"{name}: threw {error.GetType().Name}: {error.Message}"); }
    }

    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        Failures.Clear();
        await Scenario("rounding", () => Rounding(parent));
        await Scenario("share row", () => ShareRow(parent));
        await Scenario("wrap panel", () => WrapPanelRows(parent));
        await Scenario("shrink panel", () => ShrinkPanel(parent));
        await Scenario("dashboard wrap", () => DashboardWrap(parent));
        await Scenario("dashboard widget header", () => DashboardWidgetHeader(parent));
        await Scenario("dashboard tag viewport", () => TagViewport(parent));
        await Scenario("logs page header", () => LogsPageHeader(parent));
        await Scenario("logs summary layout", () => LogsSummary(parent, window));
        await Scenario("logs table", () => LogsTable(parent));
        await Scenario("settings label", () => SettingsLabel(parent));
        await Scenario("settings action", () => SettingsAction(parent));
        await Scenario("settings rate flow", () => SettingsRateFlow(parent));
        await Scenario("network flex row", () => NetworkFlexRow(parent));
        await Scenario("network flow line", () => NetworkFlowLine(parent));
        await Scenario("status row", () => StatusRow(parent));
        await Scenario("overlay band marks", () => BandMarks(parent));
        await Scenario("overlay badge limit", OverlayLimit);
        await Scenario("flex wrap stretch", () => FlexWrapStretch(parent));
        await Scenario("link button", () => LinkButton(parent));
        await Scenario("text link", () => TextLink(parent));
        await Scenario("text room", () => TextRoom(parent));
        if (Failures.Count > 0) throw new InvalidOperationException($"{Failures.Count} scaled-display layout check(s) failed: " + string.Join("; ", Failures));
        Program.Log("PASS: scaled-display layout hardening (panels keep their children whole and on one line within a fraction of a pixel; buttons give text room).");
    }

    // ---- precondition ----------------------------------------------------------------------------------------------------------------

    private static async Task Rounding(StackPanel parent)
    {
        var probe = new Probe(100.4);
        var host = await Show(parent, probe, 400);
        Program.Log($"TRACE rounding: {probe}");
        Check(probe.DesiredSize.Width < 100.4, $"a child that needs 100.4 reports a rounded desired width below that ({probe.DesiredSize.Width:0.###}), the fault's precondition");
        Hide(parent, host);
    }

    // ---- Controls --------------------------------------------------------------------------------------------------------------------

    private static async Task ShareRow(StackPanel parent)
    {
        // The two children's desired widths (100 and 60, from needs of 100.4 and 60.4) and the 8 gap exactly fill 168; 167 is one pixel short.
        foreach (var width in new[] { 168.0, 167.0 })
        {
            Probe first = new(100.4), second = new(60.4);
            var panel = new ShareRowPanel { Spacing = 8 }; panel.Children.Add(first); panel.Children.Add(second);
            var host = await Show(parent, panel, width);
            Program.Log($"TRACE share row {width}: {first} / {second}");
            Check(!first.Trimmed && !second.Trimmed, $"a share row {width} wide whose children fit within a pixel measures neither child narrower than it needs");
            Check(Slot(first).Width >= 99.99 && Slot(second).Width >= 59.99, $"a share row {width} wide arranges both children at their own desired widths ({Slot(first).Width:0.##}, {Slot(second).Width:0.##})");
            Hide(parent, host);
        }
        {
            Probe first = new(100.4), second = new(60.4);
            var panel = new ShareRowPanel { Spacing = 8 }; panel.Children.Add(first); panel.Children.Add(second);
            var host = await Show(parent, panel, 130);
            Check(first.Trimmed || second.Trimmed, "a share row that really is too narrow still trims a child");
            Check(Slot(first).Width + 8 + Slot(second).Width <= 130.01, $"a share row that really is too narrow still keeps its children inside it ({Slot(first).Width:0.##} + {Slot(second).Width:0.##})");
            Hide(parent, host);
        }
    }

    private static async Task WrapPanelRows(StackPanel parent)
    {
        WrapPanel NewPanel(HorizontalAlignment alignment, out Border first, out Border second)
        {
            first = Box(90, 10); second = Box(75, 10);
            var panel = new WrapPanel { HorizontalSpacing = 0.4, VerticalSpacing = 6, HorizontalAlignment = alignment };
            panel.Children.Add(first); panel.Children.Add(second);
            return panel;
        }
        {
            // The two need 165.4; the host offers 165.
            var panel = NewPanel(HorizontalAlignment.Stretch, out var first, out var second);
            var host = await Show(parent, panel, 165);
            Check(Math.Abs(Slot(first).Y - Slot(second).Y) < .01 && panel.ActualHeight < 15, $"a wrap panel keeps two children on one line when they are a fraction wider than the room (height {panel.ActualHeight:0.##})");
            Hide(parent, host);
        }
        {
            // Measured at 200 the children sit on one row and the panel reports 165.4, which is rounded to 165 and arranged at that width:
            // arranging must not break the row again from its own width.
            var panel = NewPanel(HorizontalAlignment.Left, out var first, out var second);
            var host = await Show(parent, panel, 200);
            Program.Log($"TRACE wrap panel: panel {panel.ActualWidth:0.###}x{panel.ActualHeight:0.###}, slots {Slot(first)} / {Slot(second)}");
            Check(Math.Abs(Slot(first).Y - Slot(second).Y) < .01 && panel.ActualHeight < 15, $"a wrap panel arranges the rows it measured, not rows worked out again from its rounded width ({Slot(first).Y:0.##}, {Slot(second).Y:0.##})");
            Hide(parent, host);
        }
        {
            var panel = NewPanel(HorizontalAlignment.Stretch, out var first, out var second);
            var host = await Show(parent, panel, 150);
            Check(Slot(second).Y > Slot(first).Y + 9 && panel.ActualHeight >= 25, "a wrap panel that really is too narrow still wraps");
            Hide(parent, host);
        }
    }

    private static async Task ShrinkPanel(StackPanel parent)
    {
        var type = AppType("SiloPlayer.Controls.AdminShrinkPanel");
        Panel NewPanel(out Probe shrinker)
        {
            var panel = (Panel)Activator.CreateInstance(type, true)!;
            Set(panel, "Spacing", 8.0);
            panel.Children.Add(Box(50, 10));
            shrinker = new Probe(100.4);
            type.GetMethod("AddShrinking", Any)!.Invoke(panel, [shrinker]);
            return panel;
        }
        {
            // The shrinker's desired width is 100 and the room it is left is 99 (one pixel short).
            var panel = NewPanel(out var shrinker);
            var host = await Show(parent, panel, 50 + 8 + 99);
            Program.Log($"TRACE shrink panel: {shrinker}");
            Check(!shrinker.Trimmed, "a shrink panel does not shrink a child that fits within a pixel");
            var clip = panel.Clip as RectangleGeometry;
            Check(clip != null && clip.Rect.Width >= panel.ActualWidth + .99, $"a shrink panel's clip reaches a pixel past its width ({clip?.Rect.Width:0.##} for {panel.ActualWidth:0.##})");
            Hide(parent, host);
        }
        {
            var panel = NewPanel(out var shrinker);
            var host = await Show(parent, panel, 50 + 8 + 60);
            Check(shrinker.Trimmed && Slot(shrinker).Width <= 61, $"a shrink panel still shrinks a child that really does not fit ({Slot(shrinker).Width:0.##})");
            Hide(parent, host);
        }
    }

    private static async Task DashboardWrap(StackPanel parent)
    {
        foreach (var (width, wraps) in new[] { (165.0, false), (150.0, true) })
        {
            Border first = Box(90, 10), second = Box(75, 10);
            var panel = (Panel)Make("SiloPlayer.Controls.Admin.DashboardWrapPanel");
            Set(panel, "Spacing", 0.4); panel.Children.Add(first); panel.Children.Add(second);
            var host = await Show(parent, panel, width);
            Check((Math.Abs(Slot(first).Y - Slot(second).Y) < .01) == !wraps, wraps
                ? "a dashboard flow that really is too narrow still wraps"
                : "a dashboard flow keeps two children on one line when they are a fraction wider than the room");
            Hide(parent, host);
        }
    }

    private static async Task DashboardWidgetHeader(StackPanel parent)
    {
        foreach (var (shortBy, wraps) in new[] { (1.0, false), (20.0, true) })
        {
            var title = new TextBlock { Text = "Alpha", FontSize = 16, TextWrapping = TextWrapping.Wrap };
            var actions = (Panel)Make("SiloPlayer.Controls.Admin.DashboardWrapPanel"); actions.Children.Add(Box(100, 36));
            var header = (Panel)Make("SiloPlayer.Controls.Admin.DashboardWidgetHeader", title, actions);
            var host = await Show(parent, header, 1000);
            var probe = new TextBlock { FontFamily = title.FontFamily, FontSize = title.FontSize, FontWeight = title.FontWeight, CharacterSpacing = title.CharacterSpacing, Text = "Alpha" };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var need = probe.DesiredSize.Width + 8 + actions.DesiredSize.Width;
            await Resize(host, Math.Ceiling(need) - shortBy);
            Program.Log($"TRACE widget header: need {need:0.###}, width {host.Width:0.###}, header {header.ActualHeight:0.###}, title {title.ActualHeight:0.###}x{title.ActualWidth:0.###}, actions {actions.ActualHeight:0.###}");
            if (wraps) Check(header.ActualHeight > actions.ActualHeight + 8, "a widget header that really is too narrow still puts the actions under the title");
            else
            {
                Check(header.ActualHeight <= Math.Max(title.ActualHeight, actions.ActualHeight) + .5, $"a widget header keeps the actions beside the title when they fit within a pixel ({header.ActualHeight:0.##})");
                Check(title.ActualHeight <= probe.DesiredSize.Height + .5, "the widget header's title keeps its longest word whole");
            }
            Hide(parent, host);
        }
    }

    private static async Task TagViewport(StackPanel parent)
    {
        // A tag row of two pills that need 165.4 in a 165 room, the second taller than the first: the viewport's first-row height must follow the
        // row the wrap panel really builds, or the taller pill is clipped.
        var viewport = (Panel)Make("SiloPlayer.Controls.Admin.DashboardTagViewport", new List<FrameworkElement> { Box(90, 10), Box(75, 20) });
        Field<WrapPanel>(viewport, "_wrap").HorizontalSpacing = 0.4;
        var host = await Show(parent, viewport, 165);
        var clip = viewport.Clip as RectangleGeometry;
        Program.Log($"TRACE tag viewport: viewport {viewport.ActualHeight:0.###}, clip {clip?.Rect}");
        Check(clip != null && clip.Rect.Height >= 19.99, $"a tag viewport's first row includes a pill that fits within a pixel ({clip?.Rect.Height:0.##})");
        Hide(parent, host);
    }

    // ---- Views / Admin ---------------------------------------------------------------------------------------------------------------

    private static async Task LogsPageHeader(StackPanel parent)
    {
        foreach (var (shortBy, wraps) in new[] { (1.0, false), (30.0, true) })
        {
            var actions = Box(100, 36);
            var header = (Panel)Make("SiloPlayer.Views.Admin.LogsPageHeader", "Title", "", actions);
            var host = await Show(parent, header, 2000);
            var titles = Field<StackPanel>(header, "_titles");
            var need = titles.DesiredSize.Width + 24 + 100;
            await Resize(host, Math.Ceiling(need) - shortBy);
            Program.Log($"TRACE logs header: need {need:0.###}, width {host.Width:0.###}, actions slot {Slot(actions)}");
            Check((Slot(actions).Y < 1) == !wraps, wraps
                ? "a page header that really is too narrow still puts the actions under the title"
                : "a page header keeps the actions beside the title when they fit within a pixel");
            Hide(parent, host);
        }
    }

    private static async Task LogsSummary(StackPanel parent, Window window)
    {
        // The narrow layout (window under 768) measures each metric at the smaller of its own desired width and the row.
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(700, 900));
        await Task.Delay(300);
        Check(parent.XamlRoot.Size.Width < 768, $"the window is narrower than the summary's wide breakpoint ({parent.XamlRoot.Size.Width:0})");
        foreach (var (width, trims) in new[] { (300.0, false), (90.0, true) })
        {
            var action = new Button { Content = "Action", Width = 250, Height = 36 };
            var layout = (Panel)Make("SiloPlayer.Views.Admin.LogsSummaryLayout", action);
            Probe first = new(100.4), second = new(100.4);
            layout.Children.Add(first); layout.Children.Add(second);
            var host = await Show(parent, layout, width);
            Program.Log($"TRACE logs summary {width}: {first} / {second}");
            if (trims) Check(first.Trimmed, "a summary metric wider than the row is still trimmed to it");
            else Check(!first.Trimmed && !second.Trimmed, "a summary metric that keeps its own desired width is not measured again at exactly that width");
            Hide(parent, host);
        }
    }

    private static async Task LogsTable(StackPanel parent)
    {
        // One column whose widest cell needs 100.4 plus 24 of padding: its rounded desired width is 124, and the table is offered 124.
        var cell = new Border { Padding = new Thickness(12, 0, 12, 0), Child = new Probe(100.4) };
        cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = cell.DesiredSize.Width;
        var table = (Grid)Make("SiloPlayer.Views.Admin.LogsNativeTable", new[] { "H" }, new[] { 1.0 });
        var probe = new Probe(100.4);
        table.GetType().GetMethod("Add", Any)!.Invoke(table, [new UIElement[] { probe }, null, null, false]);
        var host = await Show(parent, table, width);
        Program.Log($"TRACE logs table: cell desired {width:0.###}, {probe}");
        Check(!probe.Trimmed, $"a table column is not made exactly as wide as its widest cell's rounded desired width ({probe.LastWidth:0.##} offered for a need of 100.4)");
        Hide(parent, host);
    }

    private static async Task SettingsLabel(StackPanel parent)
    {
        foreach (var (shortBy, wraps) in new[] { (1.0, false), (20.0, true) })
        {
            var label = new TextBlock { Text = "Label", TextWrapping = TextWrapping.Wrap };
            var dot = new Border { Width = 6, Height = 6, Visibility = Visibility.Collapsed };
            var restart = Box(40, 16);
            var panel = (Panel)Make("SiloPlayer.Controls.AdminSettingsLabel", label, dot, restart);
            var host = await Show(parent, panel, 600);
            var need = label.DesiredSize.Width + 40 + 8;
            await Resize(host, Math.Ceiling(need) - shortBy);
            Check((Slot(restart).Y < 1) == !wraps, wraps
                ? "a settings label that really is too narrow still puts the restart badge under it"
                : "a settings label keeps the restart badge beside it when it fits within a pixel");
            Hide(parent, host);
        }
    }

    private static async Task SettingsAction(StackPanel parent)
    {
        foreach (var (width, wraps) in new[] { (171.0, false), (150.0, true) })
        {
            Border button = Box(100, 20), result = Box(60, 20);
            var panel = (Panel)Make("SiloPlayer.Controls.AdminSettingsActionContent", button, result);
            var host = await Show(parent, panel, width);
            Check((Slot(result).Y < 1) == !wraps, wraps
                ? "an action row that really is too narrow still puts the result under the button"
                : "an action row keeps the result beside the button when it fits within a pixel");
            Hide(parent, host);
        }
    }

    private static async Task SettingsRateFlow(StackPanel parent)
    {
        foreach (var (width, wraps) in new[] { (174.0, false), (150.0, true) })
        {
            Border first = Box(90, 10), second = Box(75, 10);
            var panel = (Panel)Make("SiloPlayer.Controls.AdminSettingsRateFlow");
            panel.Children.Add(first); panel.Children.Add(second);
            var host = await Show(parent, panel, width);
            Check((Math.Abs(Slot(first).Y - Slot(second).Y) < .01) == !wraps, wraps
                ? "a rate flow that really is too narrow still wraps"
                : "a rate flow keeps its children on one line when they fit within a pixel");
            Hide(parent, host);
        }
    }

    private static async Task NetworkFlexRow(StackPanel parent)
    {
        foreach (var (width, wraps) in new[] { (123.0, false), (100.0, true) })
        {
            Border info = Box(50, 20), actions = Box(100, 30);
            var panel = (Panel)Make("SiloPlayer.Views.Admin.NetworkAccessFlexRow", info, actions);
            var host = await Show(parent, panel, width);
            Check((Slot(actions).Y < 1) == !wraps, wraps
                ? "a network row that really is too narrow still puts the actions under the info"
                : "a network row keeps the actions beside the info when they fit within a pixel");
            Hide(parent, host);
        }
    }

    private static async Task NetworkFlowLine(StackPanel parent)
    {
        foreach (var (width, wraps) in new[] { (165.0, false), (150.0, true) })
        {
            Border first = Box(90, 10), second = Box(75, 10);
            var panel = (Panel)Make("SiloPlayer.Views.Admin.NetworkAccessFlowLine", 0.4, 6.0);
            panel.Children.Add(first); panel.Children.Add(second);
            var host = await Show(parent, panel, width);
            Check((Math.Abs(Slot(first).Y - Slot(second).Y) < .01) == !wraps, wraps
                ? "a network flow line that really is too narrow still wraps"
                : "a network flow line keeps two children on one line when they are a fraction wider than the room");
            Hide(parent, host);
        }
    }

    private static async Task StatusRow(StackPanel parent)
    {
        var type = typeof(SiloPlayer.Views.Admin.AdminSearchSettingsPage).GetNestedType("StatusRowPanel", BindingFlags.NonPublic) ?? throw new InvalidOperationException("StatusRowPanel");
        foreach (var (shortBy, wraps) in new[] { (1.0, false), (30.0, true) })
        {
            var panel = (Panel)Activator.CreateInstance(type, Any, null, ["Label text", "Value text", null], null)!;
            var host = await Show(parent, panel, 800);
            var label = Field<TextBlock>(panel, "_label"); var value = Field<FrameworkElement>(panel, "_value");
            value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var need = label.DesiredSize.Width + 16 + value.DesiredSize.Width;
            await Resize(host, Math.Ceiling(need) - shortBy);
            var wrapped = Field<bool>(panel, "_wrap");
            Program.Log($"TRACE status row: need {need:0.###}, width {host.Width:0.###}, wrapped {wrapped}, value slot {Slot(value)}");
            Check(wrapped == wraps, wraps
                ? "a status row that really is too narrow still puts the value under the label"
                : "a status row keeps the value beside the label when it fits within a pixel");
            if (!wraps) Check(Slot(value).Width >= value.DesiredSize.Width - .01, "a status row that fits within a pixel keeps the value at the width it asked for");
            Hide(parent, host);
        }
    }

    private static async Task FlexWrapStretch(StackPanel parent)
    {
        // A single item whose rounded natural width (100) is exactly the line: it must be offered more than that to measure in.
        var panel = (Panel)Activator.CreateInstance(AppType("SiloPlayer.Views.Admin.AdminLibrariesUi+FlexWrapStretchPanel"), true)!;
        var probe = new Probe(100.4);
        panel.Children.Add(probe);
        var host = await Show(parent, panel, 100);
        Program.Log($"TRACE flex wrap stretch: {probe}");
        Check(!probe.Trimmed, $"a flex item held at exactly its natural width is offered a pixel more to measure in ({probe.LastWidth:0.##} for a need of 100.4)");
        Hide(parent, host);
    }

    // ---- Overlay ---------------------------------------------------------------------------------------------------------------------

    private static async Task BandMarks(StackPanel parent)
    {
        // 90 and 60 with the 7 gap need 157; at 156.6 the second still fits (within the measure's half pixel) and must be arranged whole.
        // Layout rounding is off here so the panel really is arranged 156.6 wide, as a fractional pixel grid does on a scaled display.
        foreach (var (width, shown) in new[] { (156.6, true), (140.0, false) })
        {
            Probe first = new(90), second = new(60);
            var panel = (Panel)Make("SiloPlayer.Controls.OverlayBandMarks");
            panel.Children.Add(first); panel.Children.Add(second);
            panel.UseLayoutRounding = false; first.UseLayoutRounding = false; second.UseLayoutRounding = false;
            var host = await Show(parent, panel, width);
            host.UseLayoutRounding = false; await Settle(host);
            Program.Log($"TRACE band marks {width}: {first} / {second}, opacity {second.Opacity}");
            if (shown) Check(second.Opacity > .5 && Slot(second).Width >= 59.99, $"the last band mark that fits within a pixel is arranged at its own width ({Slot(second).Width:0.##} of 60)");
            else Check(second.Opacity < .5, "a band mark that really does not fit is hidden");
            Hide(parent, host);
        }
        await Task.CompletedTask;
    }

    private static Task OverlayLimit()
    {
        var limit = AppType("SiloPlayer.Controls.OverlayBadgeMeasure").GetMethod("Limit", AnyStatic)!;
        var element = new Border { Child = new StackPanel() };
        limit.Invoke(null, [element, 100.0]);
        Check(element.MaxWidth >= 100.5 - .0001, $"an overlay badge is limited to half a pixel more than the room it is offered ({element.MaxWidth:0.##} for 100)");
        return Task.CompletedTask;
    }

    // ---- Buttons ---------------------------------------------------------------------------------------------------------------------

    private static bool TextHasRoom(Button button, TextBlock label, UIElement host)
    {
        var corner = Math.Max(button.CornerRadius.TopLeft, button.CornerRadius.TopRight);
        Program.Log($"TRACE button: padding {button.Padding}, margin {button.Margin}, radius {button.CornerRadius}");
        return button.Padding.Left + .001 >= corner && button.Padding.Right + .001 >= corner && !label.IsTextTrimmed;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static double LabelLeft(TextBlock label, UIElement host) => label.TransformToVisual(host).TransformPoint(new Point(0, 0)).X;

    private static async Task LinkButton(StackPanel parent)
    {
        var label = new TextBlock { Text = "Link text" };
        var button = (Button)AppType("SiloPlayer.Controls.AdminMonitorUi").GetMethod("LinkButton", AnyStatic)!.Invoke(null, [label, "Link", Microsoft.UI.Colors.White, true])!;
        var host = await Show(parent, button, 300);
        var border = Descendants(button).OfType<Border>().First();
        Program.Log($"TRACE link button: padding {button.Padding}, button radius {button.CornerRadius}, template border radius {border.CornerRadius}");
        Check(border.CornerRadius.TopLeft <= button.Padding.Left + .001 && border.CornerRadius.TopRight <= button.Padding.Right + .001,
            $"a text link's template corners ({border.CornerRadius.TopLeft:0.##}) do not cut into its label (padding {button.Padding.Left:0.##})");
        Check(!label.IsTextTrimmed && Math.Abs(LabelLeft(label, host)) < .5, $"a text link keeps its label whole and in place ({LabelLeft(label, host):0.##})");
        Hide(parent, host);
    }

    private static async Task TextLink(StackPanel parent)
    {
        var button = (Button)AppType("SiloPlayer.Controls.Admin.DashboardUi").GetMethod("TextLink", AnyStatic)!
            .Invoke(null, ["Manage", 14.0, new RoutedEventHandler((_, _) => { }), null, false])!;
        var label = (TextBlock)button.Content;
        var host = await Show(parent, button, 300);
        Check(TextHasRoom(button, label, host), $"a dashboard text link's padding ({button.Padding.Left:0.##}) is at least its corner radius ({button.CornerRadius.TopLeft:0.##})");
        Check(Math.Abs(LabelLeft(label, host)) < .5 && Math.Abs(button.Margin.Left + button.Padding.Left) < .001, $"a dashboard text link keeps its label where it was ({LabelLeft(label, host):0.##})");
        Hide(parent, host);
    }

    private static async Task TextRoom(StackPanel parent)
    {
        var admin = AppType("SiloPlayer.Controls.AdminMonitorUi");
        var label = new TextBlock { Text = "View all" };
        var plain = (Button)admin.GetMethod("Plain", AnyStatic)!.Invoke(null, [label, "View all", null])!;
        var button = (Button)admin.GetMethod("TextRoom", AnyStatic)!.Invoke(null, [plain])!;
        var host = await Show(parent, button, 300);
        Check(TextHasRoom(button, label, host), $"a chrome-less text button's padding ({button.Padding.Left:0.##}) is at least its corner radius ({button.CornerRadius.TopLeft:0.##})");
        Check(Math.Abs(LabelLeft(label, host)) < .5 && Math.Abs(button.Margin.Left + button.Padding.Left) < .001, $"a chrome-less text button keeps its label where it was ({LabelLeft(label, host):0.##})");
        Hide(parent, host);
    }
}
