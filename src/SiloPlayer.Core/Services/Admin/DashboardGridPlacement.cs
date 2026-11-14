namespace SiloPlayer.Core.Services.Admin;

/// <summary>The WebUI dashboard grid's three layouts by viewport width.</summary>
public enum DashboardGridMode
{
    /// <summary>Below 640px: one content-sized column.</summary>
    Single,
    /// <summary>640–1023px: two content-sized columns; widgets spanning 6+ keep the full row.</summary>
    Two,
    /// <summary>From 1024px: twelve columns of fixed 100px rows; spans and rows apply.</summary>
    Twelve,
}

/// <summary>A placed widget in grid cells (0-based column/row).</summary>
public readonly record struct DashboardGridCell(string Id, int Column, int ColumnSpan, int Row, int RowSpan);

public readonly record struct DashboardGridRequest(string Id, int Span, int Rows);

/// <summary>
/// Breakpoints and placement for the dashboard grid. Placement follows CSS
/// grid's sparse row auto-placement: the cursor only moves forward, so a wide
/// widget that does not fit leaves a hole rather than back-filling it.
/// </summary>
public static class DashboardGridPlacement
{
    public const int Columns = 12;
    /// <summary>--admin-row-h, 6.25rem.</summary>
    public const double RowHeight = 100;
    /// <summary>The grid gap, 0.875rem.</summary>
    public const double Gap = 14;
    public const double SmallBreakpoint = 640;
    public const double LargeBreakpoint = 1024;
    /// <summary>Below lg a fill-height chart keeps a fixed-height chart's 10rem.</summary>
    public const double StackedChartMinHeight = 160;

    public static DashboardGridMode ModeFor(double viewportWidth)
        => viewportWidth >= LargeBreakpoint ? DashboardGridMode.Twelve
            : viewportWidth >= SmallBreakpoint ? DashboardGridMode.Two
            : DashboardGridMode.Single;

    public static int ColumnCount(DashboardGridMode mode) => mode switch
    {
        DashboardGridMode.Twelve => Columns,
        DashboardGridMode.Two => 2,
        _ => 1,
    };

    /// <summary>`.admin-widget-wide`: span 6 or more keeps the full row on tablets.</summary>
    public static bool IsWide(int span) => span >= 6;

    public static IReadOnlyList<DashboardGridCell> Place(DashboardGridMode mode, IReadOnlyList<DashboardGridRequest> items)
    {
        var columns = ColumnCount(mode);
        var occupied = new List<bool[]>();
        bool Free(int row, int column, int span, int rows)
        {
            for (var r = row; r < row + rows; r++)
            {
                if (r >= occupied.Count) continue;
                for (var c = column; c < column + span; c++)
                    if (occupied[r][c]) return false;
            }
            return true;
        }
        void Occupy(int row, int column, int span, int rows)
        {
            while (occupied.Count < row + rows) occupied.Add(new bool[columns]);
            for (var r = row; r < row + rows; r++)
                for (var c = column; c < column + span; c++)
                    occupied[r][c] = true;
        }
        var result = new List<DashboardGridCell>(items.Count);
        int cursorRow = 0, cursorColumn = 0;
        foreach (var item in items)
        {
            var span = mode switch
            {
                DashboardGridMode.Twelve => Math.Clamp(item.Span, 1, columns),
                DashboardGridMode.Two => IsWide(item.Span) ? 2 : 1,
                _ => 1,
            };
            var rows = mode == DashboardGridMode.Twelve ? Math.Max(1, item.Rows) : 1;
            while (true)
            {
                if (cursorColumn + span > columns)
                {
                    cursorRow++;
                    cursorColumn = 0;
                    continue;
                }
                if (Free(cursorRow, cursorColumn, span, rows)) break;
                cursorColumn++;
            }
            Occupy(cursorRow, cursorColumn, span, rows);
            result.Add(new DashboardGridCell(item.Id, cursorColumn, span, cursorRow, rows));
            cursorColumn += span;
        }
        return result;
    }

    /// <summary>One column's width for a grid of <paramref name="width"/> pixels.</summary>
    public static double ColumnWidth(double width, int columns) => columns <= 0 ? width : Math.Max(0, (width - Gap * (columns - 1)) / columns);

    /// <summary>The pixel height of a fixed-row widget spanning <paramref name="rows"/>.</summary>
    public static double FixedHeight(int rows) => rows * RowHeight + (rows - 1) * Gap;

    /// <summary>A pointer drag's column/row change: one step per column-plus-gap or row-plus-gap moved.</summary>
    public static (int Span, int Rows) ResizeFromDrag(DashboardWidgetDefinition widget, int startSpan, int startRows,
        double deltaX, double deltaY, double gridWidth, bool rightToLeft = false)
    {
        var columnUnit = gridWidth > 0 ? (gridWidth + Gap) / Columns : 1;
        var rowUnit = RowHeight + Gap;
        var span = (int)Math.Clamp(Math.Round(startSpan + deltaX * (rightToLeft ? -1 : 1) / columnUnit, MidpointRounding.AwayFromZero), widget.MinSpan, widget.MaxSpan);
        var rows = (int)Math.Clamp(Math.Round(startRows + deltaY / rowUnit, MidpointRounding.AwayFromZero), widget.MinRows, widget.MaxRows);
        return (span, rows);
    }

    /// <summary>Keyboard resize: left/right walk columns, up/down walk rows, each clamped.</summary>
    public static (int Span, int Rows) ResizeFromKey(DashboardWidgetDefinition widget, int span, int rows, int columnStep, int rowStep)
        => (Math.Clamp(span + columnStep, widget.MinSpan, widget.MaxSpan), Math.Clamp(rows + rowStep, widget.MinRows, widget.MaxRows));
}

/// <summary>Edge auto-scroll during a widget drag (WebUI dragAutoScroll).</summary>
public static class DashboardDragAutoScroll
{
    public const double EdgeZone = 80;
    public const double MaxSpeed = 24;

    /// <summary>Signed pixels per frame: negative scrolls up, positive down, zero outside both zones.</summary>
    public static double EdgeScrollSpeed(double pointerY, double top, double bottom, double zone = EdgeZone, double maxSpeed = MaxSpeed)
    {
        var height = bottom - top;
        if (height <= 0) return 0;
        var effective = Math.Min(zone, height / 3);
        if (effective <= 0) return 0;
        double Ramp(double distance) => maxSpeed * (1 - Math.Min(Math.Max(distance, 0), effective) / effective);
        var topDistance = pointerY - top;
        var bottomDistance = bottom - pointerY;
        if (topDistance < effective && topDistance <= bottomDistance) return -Ramp(topDistance);
        if (bottomDistance < effective) return Ramp(bottomDistance);
        return 0;
    }
}
