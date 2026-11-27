using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using Windows.Foundation;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The shadcn <c>Table</c> the Insights tabs draw (People, Devices matrix, Bandwidth by node): a header row and body rows over shared
/// columns. Like a CSS auto table, every column takes the width of its widest cell and spare room is shared out in proportion; a table
/// wider than its card scrolls sideways inside its own area and never runs past the card's edge. Cells trim their own text.
/// </summary>
internal sealed class AdminInsightsTable : Grid
{
    private const double ColumnSlack = 10;
    private readonly StackPanel _rows = new();
    private readonly List<Grid> _grids = [];
    private readonly Dictionary<int, HorizontalAlignment> _alignment = [];
    private readonly int _columns;
    private Border? _lastRule;

    internal AdminInsightsTable(int columns)
    {
        _columns = columns;
        Children.Add(new ScrollViewer
        {
            Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, IsTabStop = false,
        });
    }

    /// <summary>A column's content sits at the start (the default), the end (numbers) or the centre of its cell.</summary>
    internal AdminInsightsTable Align(int column, HorizontalAlignment alignment)
    {
        _alignment[column] = alignment;
        foreach (var row in _grids) ApplyAlignment(row, column, alignment);
        return this;
    }

    private static void ApplyAlignment(Grid row, int column, HorizontalAlignment alignment)
    {
        if (column >= row.Children.Count || row.Children[column] is not Border { Child: FrameworkElement child }) return;
        child.HorizontalAlignment = alignment;
        if (child is TextBlock text) text.TextAlignment = alignment == HorizontalAlignment.Right ? TextAlignment.Right : alignment == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left;
    }

    internal static TextBlock HeaderText(string text)
    {
        var label = A.Text(text, 12, A.Muted, FontWeights.Medium);
        label.HorizontalAlignment = HorizontalAlignment.Left;
        return label;
    }

    /// <summary>The header row (<c>TableHead</c>: muted 12px medium over a rule).</summary>
    internal void AddHeader(IReadOnlyList<UIElement> cells)
    {
        var grid = Row(cells);
        grid.Padding = new Thickness(0, 10, 0, 10);
        _rows.Children.Add(Rule(grid, bottom: true));
    }

    /// <summary>A body row; <paramref name="tinted"/> is the lighter wash a profile row under its account takes.</summary>
    internal Grid AddRow(IReadOnlyList<UIElement> cells, bool tinted = false, double verticalPadding = 8)
    {
        var grid = Row(cells);
        grid.Padding = new Thickness(0, verticalPadding, 0, verticalPadding);
        if (tinted) grid.Background = A.Solid(A.Foreground, 0.04);
        if (_lastRule != null) _lastRule.BorderThickness = new Thickness(0, 0, 0, 1);
        _lastRule = Rule(grid, bottom: false);
        _rows.Children.Add(_lastRule);
        InvalidateMeasure();
        return grid;
    }

    protected override Size MeasureOverride(Size available)
    {
        var widths = new double[_columns];
        foreach (var row in _grids)
            for (var index = 0; index < row.Children.Count; index++)
            {
                var cell = (FrameworkElement)row.Children[index];
                cell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                widths[index] = Math.Max(widths[index], cell.DesiredSize.Width);
            }
        // A cell trims its text with an ellipsis at exactly its rounded desired width, which a scaled display can leave a fraction short:
        // every column gets a pixel more there, and none at a whole-number scale, where a table that fits exactly must not start to scroll.
        // A cell's natural width, measured without a limit, came out a few pixels short of what its text and badges need once drawn (a trimmed
        // header read "Data se…", a figure wrapped, a badge lost its last letter), at any scale: every column gets that much more.
        var room = Math.Max(LayoutTolerance.ColumnRoom(XamlRoot?.RasterizationScale ?? 1), ColumnSlack);
        for (var index = 0; index < widths.Length; index++) widths[index] += room;
        var intrinsic = widths.Sum();
        var width = Math.Max(intrinsic, double.IsFinite(available.Width) ? available.Width : intrinsic);
        var ratio = intrinsic > 0 ? width / intrinsic : 1;
        _rows.Width = width;
        foreach (var row in _grids)
            for (var index = 0; index < _columns; index++)
            {
                var next = Math.Max(1, widths[index] * ratio);
                if (Math.Abs(row.ColumnDefinitions[index].Width.Value - next) > 0.01) row.ColumnDefinitions[index].Width = new GridLength(next);
            }
        return base.MeasureOverride(available);
    }

    private Grid Row(IReadOnlyList<UIElement> cells)
    {
        var grid = new Grid();
        for (var index = 0; index < _columns; index++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = new Border { Child = cells[index], Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            SetColumn(cell, index);
            grid.Children.Add(cell);
        }
        _grids.Add(grid);
        foreach (var (column, alignment) in _alignment) ApplyAlignment(grid, column, alignment);
        return grid;
    }

    private static Border Rule(UIElement child, bool bottom) => new()
    {
        Child = child, BorderBrush = A.Solid(A.Border, 0.5), BorderThickness = new Thickness(0, 0, 0, bottom ? 1 : 0),
    };

    // ── Small pieces the tables share ────────────────────────

    /// <summary>The Lucide ArrowUpDown, which the shared icon set does not have: the sort icon of a column that is not the active one.</summary>
    internal static FrameworkElement SortNone(double size, Brush foreground)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
            "<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M21 16l-4 4-4-4 M17 20V4 M3 8l4-4 4 4 M7 4v16' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground;
        path.StrokeThickness = 2;
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>A small outlined badge (<c>Badge variant="outline"</c> at 10px): the "3 profiles" and node-type chips.</summary>
    internal static Border OutlineBadge(string text)
    {
        var badge = A.Badge(text, A.Foreground, fontSize: 10, backgroundAlpha: 0, borderAlpha: 0.25, radius: 999);
        badge.Padding = new Thickness(6, 1, 6, 1);
        return badge;
    }

    /// <summary>The Jellyfin "JF" pill beside a client name (<c>JellyfinSessionPill</c>).</summary>
    internal static Border JellyfinPill()
    {
        var pill = A.Badge(AdminText.T("web.player.jellyfinSession.label"), A.Jellyfin, backgroundAlpha: 0.15, borderAlpha: 0.30);
        ToolTipService.SetToolTip(pill, AdminText.T("web.player.jellyfinSession.title"));
        return pill;
    }
}
