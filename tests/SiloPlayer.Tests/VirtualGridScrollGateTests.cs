using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class VirtualGridScrollGateTests
{
    [Theory]
    [InlineData(24, 8, 472, 1200, 216)]
    [InlineData(16, 8, 472, 1200, 0)]
    [InlineData(25, 8, 472, 1200, 688)]
    [InlineData(100000, 8, 472, 1200, 5898800)]
    public void LastRowIncludingCaptionCanReachViewportBottom(
        int count, int columns, double rowHeight, double viewport, double expected)
    {
        Assert.Equal(expected, VirtualGridScrollGate.GetMaxVerticalOffset(count, columns, rowHeight, viewport));
    }

    [Theory]
    [InlineData(0, 8, 472, 120, -120)]
    [InlineData(8, 8, 472, 120, 352)]
    [InlineData(16, 8, 472, 500, 444)]
    public void CardsFollowExactPixelOffsetEvenWithinSameRow(
        int index, int columns, double rowHeight, double offset, double expected)
    {
        Assert.Equal(expected, VirtualGridScrollGate.GetItemTop(index, columns, rowHeight, offset));
    }

    [Fact]
    public void PartialFirstRowRealizesTheExtraRowAtBottom()
    {
        Assert.Equal(4, VirtualGridScrollGate.GetVisibleRowCount(1200, 472, 400));
    }

    [Theory]
    [InlineData(-120, 120)]
    [InlineData(-30, 30)]
    [InlineData(120, -120)]
    public void WheelPreservesSmallDeltasInsteadOfJumpingViewport(int wheel, double expected)
    {
        Assert.Equal(expected, VirtualGridScrollGate.GetWheelOffsetDelta(wheel));
    }

    [Fact]
    public void LargeCatalogRealizesEveryIntersectingRowWithBoundedCards()
    {
        const int count = 100000;
        const int columns = 8;
        const double rowHeight = 472;
        const double viewportHeight = 1200;
        foreach (var offset in new[] { 0d, 120d, 400d, 472d, 99999d, 5898800d })
        {
            var first = VirtualGridScrollGate.GetFirstVisibleRow(offset, rowHeight);
            var rows = VirtualGridScrollGate.GetVisibleRowCount(viewportHeight, rowHeight, offset);
            var range = VirtualGridScrollGate.GetWindowedItemRange(count, columns, first, rows, 1, 40);
            Assert.InRange(first * columns, range.StartIndex, range.EndIndex);
            Assert.InRange(Math.Min(count - 1, (first + rows) * columns - 1), range.StartIndex, range.EndIndex);
            Assert.InRange(range.EndIndex - range.StartIndex + 1, 1, 40);
        }
    }
}
