using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ItemMaintenanceActionPolicyTests
{
    [Theory]
    [InlineData("movie")]
    [InlineData("series")]
    [InlineData("audiobook")]
    public void CuratorsCanMatchAndRefreshSupportedTitles(string itemType)
    {
        Assert.Equal(
            new ItemMaintenanceActions(CanMatch: true, CanRefreshMetadata: true),
            ItemMaintenanceActionPolicy.Resolve(true, itemType));
    }

    [Theory]
    [InlineData("episode")]
    [InlineData("season")]
    [InlineData("manga")]
    [InlineData("ebook")]
    public void CuratorsCanRefreshOtherMediaWithoutMatch(string itemType)
    {
        Assert.Equal(
            new ItemMaintenanceActions(CanMatch: false, CanRefreshMetadata: true),
            ItemMaintenanceActionPolicy.Resolve(true, itemType));
    }

    [Fact]
    public void UsersWithoutMetadataPermissionGetNoMaintenanceActions()
    {
        Assert.Equal(
            default,
            ItemMaintenanceActionPolicy.Resolve(false, "movie"));
    }
}
