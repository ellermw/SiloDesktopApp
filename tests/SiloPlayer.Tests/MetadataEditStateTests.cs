using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using Xunit;

namespace SiloPlayer.Tests;

public class MetadataEditStateTests
{
    [Fact]
    public void ClearsDatesWithNullButTimezoneWithEmptyStringAndSendsOnlyChangedFields()
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "series", Title = "Original", FirstAirDate = "2020-01-01", AirTimezone = "America/Chicago" });
        state.Set("first_air_date", ""); state.Set("air_timezone", "");
        var changes = state.Changes([]);
        Assert.Null(changes["first_air_date"]); Assert.Equal("", changes["air_timezone"]);
        Assert.False(changes.ContainsKey("title"));
    }

    [Fact]
    public void EditedFieldsAutoLockAndMergeWithNewImageLocksWhileEditorIsOpen()
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "movie", Title = "Original", LockedFields = [2] });
        state.Set("title", "Updated"); state.SetLock(2, false);
        Assert.Equal(new[] { 0, 10 }, (int[])state.Changes([2, 10])["locked_fields"]!);
    }

    [Fact]
    public void NoChangesSendNothingAndSeasonsDoNotAutoLock()
    {
        var item = new MediaItemDetail { Type = "season", Title = "Season 1", SeasonNumber = 1 };
        var state = new MetadataEditState(item);
        Assert.Empty(state.Changes([])); state.Set("title", "Specials");
        Assert.Equal("Specials", state.Changes([])["title"]); Assert.False(state.Changes([]).ContainsKey("locked_fields"));
    }
}
