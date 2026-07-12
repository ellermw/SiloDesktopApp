namespace SiloPlayer.Core.Services;

public static class VirtualGridScrollGate
{
    public static int GetFirstVisibleRow(double verticalOffset, double rowHeight)
    {
        if (rowHeight <= 0)
            return 0;

        return Math.Max(0, (int)Math.Floor(Math.Max(0, verticalOffset) / rowHeight));
    }

    public static bool HasFirstVisibleRowChanged(
        int? previousRow,
        double verticalOffset,
        double rowHeight,
        out int currentRow)
    {
        currentRow = GetFirstVisibleRow(verticalOffset, rowHeight);
        return previousRow != currentRow;
    }

    public static double GetRelativeItemTop(
        int itemIndex,
        int columns,
        int windowStartRow,
        double rowHeight)
    {
        if (columns <= 0 || rowHeight <= 0)
            return 0;

        var itemRow = Math.Max(0, itemIndex / columns);
        return Math.Max(0, itemRow - Math.Max(0, windowStartRow)) * rowHeight;
    }

    public static int GetVisibleRowCount(double viewportHeight, double rowHeight)
    {
        if (viewportHeight <= 0 || rowHeight <= 0)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(viewportHeight / rowHeight));
    }

    public static int GetMaxFirstVisibleRow(int totalItems, int columns, int visibleRows)
    {
        if (totalItems <= 0 || columns <= 0)
            return 0;

        var totalRows = (int)Math.Ceiling(totalItems / (double)columns);
        return Math.Max(0, totalRows - Math.Max(1, visibleRows));
    }

    public static (int StartIndex, int EndIndex) ClampRealizedItemRange(
        int totalItems,
        int columns,
        int firstVisibleRow,
        int startIndex,
        int endIndex,
        int maxRealizedItems)
    {
        if (totalItems <= 0 || columns <= 0 || maxRealizedItems <= 0 || endIndex < startIndex)
            return (startIndex, endIndex);

        if (endIndex - startIndex + 1 <= maxRealizedItems)
            return (startIndex, endIndex);

        var visibleStartIndex = Math.Min(totalItems - 1, Math.Max(0, firstVisibleRow) * columns);
        var clampedStartIndex = Math.Clamp(visibleStartIndex, 0, totalItems - 1);
        var clampedEndIndex = Math.Min(totalItems - 1, clampedStartIndex + maxRealizedItems - 1);
        return (clampedStartIndex, clampedEndIndex);
    }

    public static (int StartIndex, int EndIndex) GetWindowedItemRange(
        int totalItems,
        int columns,
        int firstVisibleRow,
        int visibleRows,
        int overscanRows,
        int maxItems)
    {
        if (totalItems <= 0 || columns <= 0 || maxItems <= 0)
            return (0, -1);

        var clampedFirstRow = Math.Max(0, firstVisibleRow);
        var windowStartRow = Math.Max(0, clampedFirstRow - Math.Max(0, overscanRows));
        var rowCount = Math.Max(1, visibleRows) + Math.Max(0, overscanRows) * 2;
        var startIndex = Math.Min(totalItems - 1, windowStartRow * columns);
        var endIndex = Math.Min(totalItems - 1, ((windowStartRow + rowCount) * columns) - 1);

        return ClampRealizedItemRange(
            totalItems,
            columns,
            clampedFirstRow,
            startIndex,
            endIndex,
            Math.Max(columns, maxItems));
    }

    public static (double Left, double Top) GetSlotPosition(
        int slotIndex,
        int columns,
        double itemWidth,
        double columnGap,
        double rowHeight)
    {
        if (slotIndex < 0 || columns <= 0 || itemWidth <= 0 || rowHeight <= 0)
            return (0, 0);

        var column = slotIndex % columns;
        var row = slotIndex / columns;
        return (column * (itemWidth + columnGap), row * rowHeight);
    }
}
