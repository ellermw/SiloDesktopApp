using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

public sealed class PlayingNextLayoutTests
{
    // 16:9, 21:9 and 32:9 desktops, and windows between: the gap between the finishing picture and
    // its column is on the window's centre line, so the pair is centred whatever the aspect ratio.
    [Theory]
    [InlineData(1200)]
    [InlineData(1366)]
    [InlineData(1536)]
    [InlineData(1920)]
    [InlineData(2560)]
    [InlineData(3440)]
    [InlineData(3840)]
    [InlineData(5120)]
    public void WideLayoutCentresTheGapBetweenPictureAndColumn(double windowWidth)
    {
        var layout = PlayingNextLayout.For(windowWidth);
        Assert.True(layout.Wide);
        var pictureRight = layout.LeftMargin + layout.SlotWidth;
        var columnLeft = pictureRight + PlayingNextLayout.Gutter;
        Assert.Equal(windowWidth / 2, (pictureRight + columnLeft) / 2, 6);
        Assert.True(layout.LeftMargin >= PlayingNextLayout.EdgeMargin);
        Assert.True(columnLeft + layout.ColumnWidth <= windowWidth - PlayingNextLayout.EdgeMargin + 1e-6);
    }

    // The owner's report: at 3440 wide the picture sat 48 from the left edge with the screen's right half empty.
    [Fact]
    public void UltrawidePictureLeavesTheLeftEdge()
    {
        var layout = PlayingNextLayout.For(3440);
        Assert.Equal(832, layout.SlotWidth);
        Assert.Equal(864, layout.LeftMargin);
        Assert.Equal(544, layout.ColumnWidth);
    }

    // Too narrow to centre the gap without pushing the picture off the margin: it keeps the margin.
    [Theory]
    [InlineData(1024)]
    [InlineData(1100)]
    [InlineData(1199)]
    public void NarrowWideLayoutKeepsTheEdgeMargin(double windowWidth)
    {
        var layout = PlayingNextLayout.For(windowWidth);
        Assert.True(layout.Wide);
        Assert.Equal(PlayingNextLayout.EdgeMargin, layout.LeftMargin);
        Assert.Equal(windowWidth * .44, layout.SlotWidth, 6);
        Assert.Equal(windowWidth - layout.SlotWidth - 144, layout.ColumnWidth, 6);
    }

    [Theory]
    [InlineData(1023, 320, 544)]
    [InlineData(800, 320, 544)]
    [InlineData(340, 292, 292)]
    [InlineData(260, 212, 240)]
    public void CompactLayoutStacksThePictureAboveTheColumn(double windowWidth, double slot, double column)
    {
        var layout = PlayingNextLayout.For(windowWidth);
        Assert.False(layout.Wide);
        Assert.Equal(slot, layout.SlotWidth);
        Assert.Equal(column, layout.ColumnWidth);
    }

    // No jump as the window is dragged across the width where centring starts.
    [Fact]
    public void CentringStartsWithoutAJump()
    {
        var before = PlayingNextLayout.For(1199.5);
        var after = PlayingNextLayout.For(1200.5);
        Assert.InRange(after.LeftMargin - before.LeftMargin, 0, 0.1);
        Assert.InRange(after.SlotWidth - before.SlotWidth, 0, 0.5);
    }
}
