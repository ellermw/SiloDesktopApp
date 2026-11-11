using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using Xunit;

namespace SiloPlayer.Tests;

public class MetadataEditStateTests
{
    [Theory]
    [InlineData("movie")]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("episode")]
    public void AppliedArtworkKeepsItsLockWhenTheFollowingDetailReadFails(string type)
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = type, Title = "Original" });
        state.SetLock(10, true);
        Assert.Equal(new[] { 10 }, (int[])state.Changes([])["locked_fields"]!);
        Assert.Contains(10, state.EffectiveLocks([]));
        state.Set("title", "Edited");
        Assert.Equal(new[] { 0, 10 }, (int[])state.Changes([])["locked_fields"]!);
    }

    [Theory]
    [InlineData("season", "title", 0)]
    [InlineData("season", "overview", 1)]
    [InlineData("season", "air_date", 13)]
    [InlineData("episode", "air_date", 13)]
    [InlineData("episode", "runtime", 7)]
    public void ChildEditsAcquireOnlyTheirOwnLock(string type, string key, int field)
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = type });
        state.Set(key, key == "runtime" ? 42d : "Edited");
        Assert.Equal(new[] { field }, state.Changes([])["locked_fields"]);
        state.SetLock(field, false);
        Assert.False(state.Changes([]).ContainsKey("locked_fields"));
    }

    [Theory]
    [InlineData("season")]
    [InlineData("episode")]
    public void ChildNumbersCannotAcquireLocksButArtworkAndUnlockOnlyAreSent(string type)
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = type });
        state.Set("season_number", 2d); state.Set("episode_number", 3d);
        state.SetLock(4, true); state.SetLock(2, true);
        Assert.False(state.Changes([]).ContainsKey("locked_fields"));
        state.SetLock(10, true);
        Assert.Equal(new[] { 10 }, (int[])state.Changes([])["locked_fields"]!);
        state.SetLock(10, false);
        state.SetLock(0, false);
        Assert.Empty((int[])state.Changes([0])["locked_fields"]!);
        Assert.Empty((int[])state.Changes([0, 10])["locked_fields"]!);
        Assert.DoesNotContain("runtime", new MetadataEditState(new MediaItemDetail { Type = "season" }).FieldLockMap.Keys);
    }

    [Theory]
    [InlineData("season")]
    [InlineData("episode")]
    public void ChildSavesPreserveServerLocksOutsideTheEditableMap(string type)
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = type, Title = "Before" });
        Assert.Empty(state.Changes([10]));
        Assert.Empty(state.Changes([2, 10, 99]));
        state.Set("title", "After");
        Assert.Equal(new[] { 0, 2, 10, 99 }, (int[])state.Changes([2, 10, 99])["locked_fields"]!);
        state.SetLock(0, false);
        Assert.Equal(new[] { 2, 10, 99 }, (int[])state.Changes([0, 2, 10, 99])["locked_fields"]!);
    }

    [Fact]
    public void EditingOnlyTaglineLocksOverviewWithoutLockingTheTitle()
    {
        var state = new MetadataEditState(new MediaItemDetail { Type = "movie", Title = "Unchanged", Tagline = "Before" });
        state.Set("tagline", "After");
        var changes = state.Changes([]);
        Assert.Equal(new[] { 1 }, (int[])changes["locked_fields"]!);
        Assert.False(changes.ContainsKey("title"));
    }

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
    public void NoChangesSendNothingAndSeasonTitleAutoLocks()
    {
        var item = new MediaItemDetail { Type = "season", Title = "Season 1", SeasonNumber = 1 };
        var state = new MetadataEditState(item);
        Assert.Empty(state.Changes([])); state.Set("title", "Specials");
        Assert.Equal("Specials", state.Changes([])["title"]); Assert.Equal(new[] { 0 }, state.Changes([])["locked_fields"]);
    }
}
