using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;
using Windows.Foundation;

/// <summary>
/// Checks for the Libraries review fixes: the destructive buttons' resolved colours in rest, hover and pressed, the stop button's 20 px visual inside its
/// 44 px hit area, the layout primitives (<c>FlexWrapStretchPanel</c>, <c>CardActionRow</c>) on their own, and the editor's close button and phone footer
/// positions. The panel and helper types are internal to the app, so they are reached by reflection.
/// </summary>
internal static partial class AdminLibrariesNativeFixture
{
    private static Type UiType(string name) => typeof(AdminLibrariesPage).Assembly.GetType("SiloPlayer.Views.Admin.AdminLibrariesUi" + name, throwOnError: true)!;

    // ── Destructive buttons ──────────────────────────────────────────────

    /// <summary>The template's root border of a button (the shape its background states drive).</summary>
    private static Border? TemplateRoot(Button button, string name) =>
        Descendants(button).OfType<Border>().FirstOrDefault(border => border.Name == name);

    /// <summary>The colour the root shows in a visual state: the brush colour with its alpha and the brush opacity, over <paramref name="backdrop"/>.</summary>
    private static (AdminLibrariesPalette.Rgb Shown, double Alpha)? ResolveBackground(Border root, AdminLibrariesPalette.Rgb backdrop)
    {
        if (root.Background is not SolidColorBrush brush) return null;
        var alpha = brush.Color.A / 255d * brush.Opacity;
        return (AdminLibrariesPalette.Over(new(brush.Color.R, brush.Color.G, brush.Color.B), alpha, backdrop), alpha);
    }

    /// <summary>
    /// A destructive button resolves, in rest, hover and pressed, to the official fill (#dc2626; hover and pressed at 90%), and white text on each holds 4.5:1 over the
    /// lightest panel surface. Drives the real visual states of the rendered button.
    /// </summary>
    private static void CheckDestructiveStates(Button? button, string label)
    {
        if (button == null) { Check(false, label + " exists"); return; }
        var root = TemplateRoot(button, "WebUiRoot");
        if (root == null) { Check(false, label + " has the page's destructive template (WebUiRoot)"); return; }
        var fill = AdminLibrariesPalette.DestructiveFill;
        foreach (var (state, opacity) in new[] { ("Normal", 1d), ("PointerOver", AdminLibrariesPalette.DestructiveHoverOpacity), ("Pressed", AdminLibrariesPalette.DestructiveHoverOpacity) })
        {
            VisualStateManager.GoToState(button, state, false);
            button.UpdateLayout();
            if (ResolveBackground(root, AdminLibrariesPalette.RaisedSurface) is not { } resolved) { Check(false, $"{label} {state} has a solid background"); continue; }
            // The brush carries the opacity as an 8-bit alpha channel (90% is 230/255).
            var expected = AdminLibrariesPalette.Over(fill, AdminLibrariesPalette.AlphaByte(opacity) / 255d, AdminLibrariesPalette.RaisedSurface);
            Check(Math.Abs(resolved.Alpha - opacity) < 0.01 && resolved.Shown == expected, $"{label} {state} resolves to {fill.Hex} at {opacity:0.##} (shown {resolved.Shown.Hex}, expected {expected.Hex})");
            var ratio = AdminLibrariesPalette.ContrastRatio(resolved.Shown, AdminLibrariesPalette.DestructiveForeground);
            Check(ratio >= 4.5, $"{label} {state}: white text on {resolved.Shown.Hex} is {ratio:0.00}:1");
            Check(button.Foreground is SolidColorBrush { Color: { R: 255, G: 255, B: 255 } }, $"{label} {state} keeps white text");
        }
        VisualStateManager.GoToState(button, "Normal", false);
    }

    /// <summary>The stop button keeps its hit area but its visible hover, pressed and focus shape is the 20 px square (official <c>h-5 w-5</c>, <c>hover:bg-destructive/10</c>).</summary>
    private static void CheckStopButtonShape(Button? button, string label, double hitArea)
    {
        if (button == null) { Check(false, label + " exists"); return; }
        var shape = TemplateRoot(button, "StopShape");
        if (shape == null) { Check(false, label + " has the stop template (StopShape)"); return; }
        Check(Math.Abs(button.ActualWidth - hitArea) < 0.5 && Math.Abs(button.ActualHeight - hitArea) < 0.5, $"{label} keeps its {hitArea} px hit area (is {button.ActualWidth}x{button.ActualHeight})");
        Check(Math.Abs(shape.ActualWidth - 20) < 0.5 && Math.Abs(shape.ActualHeight - 20) < 0.5, $"{label} draws its state shape at 20x20 (is {shape.ActualWidth}x{shape.ActualHeight})");
        var centre = shape.TransformToVisual(button).TransformPoint(new(shape.ActualWidth / 2, shape.ActualHeight / 2));
        Check(Math.Abs(centre.X - button.ActualWidth / 2) < 0.5 && Math.Abs(centre.Y - button.ActualHeight / 2) < 0.5, $"{label} centres the shape in the hit area");
        VisualStateManager.GoToState(button, "PointerOver", false);
        Check(shape.Background is SolidColorBrush { Color.A: var alpha } && alpha == AdminLibrariesPalette.AlphaByte(AdminLibrariesPalette.StopHoverOpacity), $"{label} hover shows the destructive colour at 10% on the 20 px shape");
        VisualStateManager.GoToState(button, "Pressed", false);
        Check(shape.Background is SolidColorBrush { Color.A: var pressed } && pressed == AdminLibrariesPalette.AlphaByte(AdminLibrariesPalette.StopHoverOpacity), $"{label} pressed shows the same 10% on the 20 px shape");
        VisualStateManager.GoToState(button, "Normal", false);
        VisualStateManager.GoToState(button, "Focused", false);
        Check(shape.BorderBrush is SolidColorBrush { Color.A: > 0 }, $"{label} focus draws its ring on the 20 px shape");
        var hit = Descendants(button).OfType<Grid>().FirstOrDefault(grid => grid.Name == "HitArea");
        Check(hit is { Background: SolidColorBrush { Color.A: 0 } } && hit.ActualWidth >= hitArea - 0.5, $"{label} paints nothing across its {hitArea} px hit area (transparent, but hit-testable)");
        VisualStateManager.GoToState(button, "Unfocused", false);
        VisualStateManager.GoToState(button, "Normal", false);
    }

    // ── Layout primitives ────────────────────────────────────────────────

    /// <summary>A greedy item: 100 px unconstrained, otherwise as wide as it is given up to 200 (like a stretched grid), so a second measure pass changes its DesiredSize.</summary>
    private sealed class ElasticItem : Panel
    {
        protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 100 : Math.Min(availableSize.Width, 200), 40);
        protected override Size ArrangeOverride(Size finalSize) => finalSize;
    }

    private static Border Box(double width, double height = 40, bool collapsed = false) => new() { Width = width, Height = height, Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible };

    private static Panel NewFlex(double column = 8, double row = 8)
    {
        var type = UiType("+FlexWrapStretchPanel");
        var panel = (Panel)Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("ColumnSpacing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(panel, column);
        type.GetProperty("RowSpacing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(panel, row);
        return panel;
    }

    private static Rect Slot(UIElement element) => LayoutInformation.GetLayoutSlot((FrameworkElement)element);

    private static Size Lay(Panel panel, double width)
    {
        panel.Measure(new(width, double.PositiveInfinity));
        var size = panel.DesiredSize;
        panel.Arrange(new(0, 0, double.IsInfinity(width) ? size.Width : width, size.Height));
        return size;
    }

    private static void RunLayoutPrimitiveChecks()
    {
        // Unbounded width: every item on one line, and the width counts the gaps (3 x 100 + 2 x 8).
        var one = NewFlex();
        for (var i = 0; i < 3; i++) one.Children.Add(Box(100));
        var unbounded = Lay(one, double.PositiveInfinity);
        Check(Math.Abs(unbounded.Width - 316) < 0.01 && Math.Abs(unbounded.Height - 40) < 0.01, $"FlexWrapStretchPanel: infinite width is one line, 316 wide with the gaps (is {unbounded.Width}x{unbounded.Height})");
        Check(one.Children.All(child => Math.Abs(Slot(child).Y) < 0.01), "FlexWrapStretchPanel: infinite width puts every item on the same line");
        var fits = Lay(one, unbounded.Width);
        Check(Math.Abs(fits.Height - 40) < 0.01 && one.Children.All(child => Math.Abs(Slot(child).Y) < 0.01), "FlexWrapStretchPanel: items that exactly fit their measured width do not wrap");

        // Nothing to lay out.
        var none = NewFlex();
        Check(Lay(none, 300).Height == 0 && Lay(none, double.PositiveInfinity).Height == 0, "FlexWrapStretchPanel: no children give height 0");
        var collapsed = NewFlex();
        for (var i = 0; i < 3; i++) collapsed.Children.Add(Box(100, collapsed: true));
        var collapsedSize = Lay(collapsed, 300);
        Check(collapsedSize.Height == 0 && collapsedSize.Width == 0 && Lay(collapsed, double.PositiveInfinity).Height == 0, "FlexWrapStretchPanel: all-collapsed children give height 0 (no row gaps)");

        // A child wider than the panel overflows at its natural width on its own line.
        var wide = NewFlex();
        wide.Children.Add(Box(60)); wide.Children.Add(Box(150));
        var narrow = Lay(wide, 100);
        Check(Math.Abs(narrow.Height - 88) < 0.01, $"FlexWrapStretchPanel: an over-wide child wraps to its own line (height {narrow.Height}, expected 40 + 8 + 40)");
        Check(Math.Abs(Slot(wide.Children[1]).Width - 150) < 0.01 && Math.Abs(Slot(wide.Children[1]).Y - 48) < 0.01, $"FlexWrapStretchPanel: the over-wide child keeps its natural 150 px (is {Slot(wide.Children[1]).Width})");
        Check(Math.Abs(Slot(wide.Children[0]).Width - 100) < 0.01, "FlexWrapStretchPanel: the other item grows to fill its line (flex-1)");

        // Resizing re-wraps in both directions.
        var resize = NewFlex();
        for (var i = 0; i < 3; i++) resize.Children.Add(Box(100));
        var heights = new[] { Lay(resize, 400).Height, Lay(resize, 220).Height, Lay(resize, 110).Height, Lay(resize, 400).Height };
        Check(heights.SequenceEqual([40d, 88d, 136d, 40d]), $"FlexWrapStretchPanel: resizing re-wraps ({string.Join(", ", heights)}; expected 40, 88, 136, 40)");

        // The second measure pass widens greedy items (their DesiredSize changes); arrange must still produce the lines measure chose, from the natural widths cached in the first pass.
        var elastic = NewFlex();
        for (var i = 0; i < 3; i++) elastic.Children.Add(new ElasticItem());
        var greedy = Lay(elastic, 250);
        Check(Math.Abs(greedy.Height - 88) < 0.01, $"FlexWrapStretchPanel: greedy items wrap by natural width at 250 px (height {greedy.Height}, expected 88)");
        Check(Math.Abs(Slot(elastic.Children[0]).Y) < 0.01 && Math.Abs(Slot(elastic.Children[1]).Y) < 0.01 && Math.Abs(Slot(elastic.Children[2]).Y - 48) < 0.01,
            $"FlexWrapStretchPanel: arrange keeps measure's lines when items grow in the second pass (Y {Slot(elastic.Children[0]).Y}, {Slot(elastic.Children[1]).Y}, {Slot(elastic.Children[2]).Y})");

        // CardActionRow keeps every child, one equal column each.
        var source = new StackPanel();
        var first = new Button { Content = "A" }; var note = new TextBlock { Text = "note" }; var last = new Button { Content = "B" };
        source.Children.Add(first); source.Children.Add(note); source.Children.Add(last);
        var row = (Grid)UiType("").GetMethod("CardActionRow", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, [source])!;
        Check(row.Children.Count == 3 && row.ColumnDefinitions.Count == 3 && source.Children.Count == 0, $"CardActionRow keeps a non-button child (children {row.Children.Count}, columns {row.ColumnDefinitions.Count})");
        Check(row.Children.Select(child => Grid.GetColumn((FrameworkElement)child)).SequenceEqual([0, 1, 2]) && ReferenceEquals(row.Children[1], note), "CardActionRow places the children in order, one column each");
        Check(first.HorizontalAlignment == HorizontalAlignment.Stretch && note.HorizontalAlignment == HorizontalAlignment.Stretch, "CardActionRow stretches every child");
    }

    // ── Editor chrome ────────────────────────────────────────────────────

    /// <summary>
    /// From 640 px the dialog's close icon sits at the official <c>end-4 top-4</c>: its centre 24 px in from the padding box's right and top edges (25 px from the border box).
    /// Below it the close button is its own 44 px target. Measured on the rendered editor.
    /// </summary>
    private static void CheckEditorCloseButton(DependencyObject shell, string tag, bool phone)
    {
        var editor = Find<FrameworkElement>(shell, "LibraryEditor");
        var close = editor == null ? null : Find<Button>(editor, "LibraryEditorClose");
        if (editor == null || close == null) { Check(false, $"[{tag}] the editor and its close button exist"); return; }
        if (phone) { Check(Math.Abs(close.ActualWidth - 44) < 0.5 && Math.Abs(close.ActualHeight - 44) < 0.5, $"[{tag}] the phone editor's close button is a 44 px target"); return; }
        var panel = (Border)Field(editor, "_panel")!;
        var centre = close.TransformToVisual(panel).TransformPoint(new(close.ActualWidth / 2, close.ActualHeight / 2));
        var fromRight = panel.ActualWidth - centre.X;
        Check(Math.Abs(fromRight - 25) < 0.6 && Math.Abs(centre.Y - 25) < 0.6, $"[{tag}] the close icon is centred 25 px from the dialog's right and top edges (end-4 top-4) (is {fromRight:0.0} from the right, {centre.Y:0.0} from the top)");
    }

    /// <summary>The phone footer adds no gap above the buttons while there is no error, and the 8 px gap-2 while the error is on its own row.</summary>
    private static async Task CheckPhoneFooterAsync(DependencyObject shell, string tag)
    {
        var save = Find<Button>(shell, "LibraryEditorSave");
        if (save == null || VisualTreeHelper.GetParent(save) is not Grid footer) { Check(false, $"[{tag}] the editor footer exists"); return; }
        var errorText = Find<TextBlock>(shell, "LibraryEditorError");
        Check(errorText is { Visibility: Visibility.Collapsed }, $"[{tag}] no error is showing before Save");
        // border-top 1 + pt-3 12 + 44 px buttons + pb-3 12.
        Check(Math.Abs(footer.ActualHeight - 69) < 0.6 && Math.Abs(footer.RowSpacing) < 0.01, $"[{tag}] the phone footer is 69 px with no error (is {footer.ActualHeight}, row spacing {footer.RowSpacing})");
        await InvokeAsync(save); // an empty create reports what is required in the footer
        await Until(() => Find<TextBlock>(shell, "LibraryEditorError") is { Visibility: Visibility.Visible, Text.Length: > 0 });
        await Task.Delay(150);
        footer.UpdateLayout();
        var error = Find<TextBlock>(shell, "LibraryEditorError")!;
        var expected = 69 + 8 + error.ActualHeight;
        Check(Math.Abs(footer.ActualHeight - expected) < 0.8 && Math.Abs(footer.RowSpacing - 8) < 0.01, $"[{tag}] the phone footer adds the 8 px gap while the error shows (is {footer.ActualHeight}, expected {expected})");
    }
}
