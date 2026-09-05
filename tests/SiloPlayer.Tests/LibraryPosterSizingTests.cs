using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class LibraryPosterSizingTests
{
    [Theory]
    [InlineData(2200, 120, 16)]
    [InlineData(2200, 260, 8)]
    [InlineData(3500, 120, 26)]
    [InlineData(80, 120, 1)]
    public void DensityScalesWithAvailableWidth(double width, double size, int columns)
        => Assert.Equal(columns, LibraryPosterSizing.GetColumnCount(width, size));
}
