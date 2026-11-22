using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Schedule editor rules (official AdminTaskDetail TaskSchedulePanel and AdminTaskTriggerForm).</summary>
public sealed class AdminTaskScheduleDraftTests
{
    private static AdminTaskTrigger Interval(long? milliseconds) => new(AdminTaskTriggerTypes.Interval, milliseconds, null, null, null);

    [Fact]
    public void RowsKeepStableIdentitiesWhenARowAboveIsRemoved()
    {
        var draft = AdminTaskScheduleDraft.From([Interval(120_000), Interval(7_200_000)]);
        var second = draft.Rows[1].Id;
        draft = draft.Remove(draft.Rows[0].Id);
        var row = Assert.Single(draft.Rows);
        Assert.Equal(second, row.Id);
        Assert.Equal(("2", AdminTaskIntervalUnit.Hours), (row.IntervalText, row.Unit));
        var added = draft.Add();
        Assert.NotEqual(second, added.Rows[1].Id);
        Assert.DoesNotContain(draft.Rows[0].Id, added.Rows.Skip(1).Select(entry => entry.Id));
    }

    [Fact]
    public void AddTriggerDefaultsToAnHourlyInterval()
    {
        var row = Assert.Single(AdminTaskScheduleDraft.From([]).Add().Rows);
        Assert.Equal(Interval(3_600_000), row.Trigger);
        Assert.Equal(("1", AdminTaskIntervalUnit.Hours), (row.IntervalText, row.Unit));
    }

    [Theory]
    [InlineData(AdminTaskTriggerTypes.Interval, 60_000L, null, null)]
    [InlineData(AdminTaskTriggerTypes.Daily, null, "00:00", null)]
    [InlineData(AdminTaskTriggerTypes.Weekly, null, "00:00", 0)]
    [InlineData(AdminTaskTriggerTypes.Startup, null, null, null)]
    public void SwitchingTypeFillsThatTypesShownDefaults(string type, long? interval, string? time, int? day)
    {
        var draft = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Startup, null, null, null, null)]);
        var row = draft.SetType(draft.Rows[0].Id, type).Rows[0];
        Assert.Equal(new AdminTaskTrigger(type, interval, time, day, null), row.Trigger);
    }

    [Fact]
    public void SwitchingTypeKeepsValuesAlreadyPresent()
    {
        var draft = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Weekly, null, "05:30", 3, 60_000)]);
        var row = draft.SetType(draft.Rows[0].Id, AdminTaskTriggerTypes.Daily).Rows[0];
        Assert.Equal(new AdminTaskTrigger(AdminTaskTriggerTypes.Daily, null, "05:30", 3, 60_000), row.Trigger);
    }

    [Theory]
    [InlineData(7_200_000L, "2", AdminTaskIntervalUnit.Hours)]
    [InlineData(120_000L, "2", AdminTaskIntervalUnit.Minutes)]
    [InlineData(1500L, "1.5", AdminTaskIntervalUnit.Seconds)]
    [InlineData(null, "", AdminTaskIntervalUnit.Minutes)]
    public void IntervalsShowInTheLargestUnitThatDividesThem(long? milliseconds, string text, AdminTaskIntervalUnit unit) =>
        Assert.Equal((text, unit), AdminTaskScheduleDraft.IntervalDraft(milliseconds));

    [Fact]
    public void TypedIntervalKeepsThePickedUnitAndAnEmptiedBoxBlocksSave()
    {
        var draft = AdminTaskScheduleDraft.From([Interval(120_000)]);
        var id = draft.Rows[0].Id;
        draft = draft.SetInterval(id, "60", AdminTaskIntervalUnit.Minutes);
        Assert.Equal(("60", AdminTaskIntervalUnit.Minutes, 3_600_000L), (draft.Rows[0].IntervalText, draft.Rows[0].Unit, draft.Rows[0].Trigger.IntervalMs));
        draft = draft.SetInterval(id, "", AdminTaskIntervalUnit.Minutes);
        Assert.Equal("", draft.Rows[0].IntervalText);
        Assert.Null(draft.Rows[0].Trigger.IntervalMs);
        Assert.False(draft.IsValid);
        draft = draft.SetInterval(id, "90", AdminTaskIntervalUnit.Minutes);
        Assert.True(draft.IsValid);
        Assert.Equal([Interval(5_400_000)], draft.ToTriggers());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("0.0000001")]
    [InlineData("99999999999999")]
    public void IntervalsTheApiRejectsBlockSave(string text)
    {
        var draft = AdminTaskScheduleDraft.From([Interval(60_000)]);
        draft = draft.SetInterval(draft.Rows[0].Id, text, AdminTaskIntervalUnit.Hours);
        Assert.False(draft.Rows[0].IntervalValid);
        Assert.False(draft.IsValid);
        Assert.Throws<InvalidOperationException>(() => draft.ToTriggers());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("0", 0L)]
    [InlineData("0.5", 30_000L)]
    [InlineData("90", 5_400_000L)]
    public void MaxRuntimeIsEnteredInMinutes(string text, long? milliseconds)
    {
        var draft = AdminTaskScheduleDraft.From([Interval(60_000)]);
        draft = draft.SetRuntime(draft.Rows[0].Id, text);
        Assert.True(draft.IsValid);
        Assert.Equal(milliseconds, draft.Rows[0].Trigger.MaxRuntimeMs);
        Assert.Equal(text, draft.Rows[0].RuntimeText);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1e300")]
    public void MalformedMaxRuntimeBlocksSave(string text)
    {
        var draft = AdminTaskScheduleDraft.From([Interval(60_000)]);
        draft = draft.SetRuntime(draft.Rows[0].Id, text);
        Assert.False(draft.Rows[0].RuntimeValid);
        Assert.False(draft.IsValid);
    }

    [Fact]
    public void StoredRuntimeIsShownInMinutes()
    {
        var row = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Startup, null, null, null, 90_000)]).Rows[0];
        Assert.Equal("1.5", row.RuntimeText);
    }

    // Official #5077 (web/src/pages/AdminTaskDetail.v2.test.tsx "keeps a typed Max runtime as typed, and shows a stored one rounded").
    [Fact]
    public void ATypedMaxRuntimeStaysAsTypedAndAStoredOneIsShownRoundedToFiveDecimals()
    {
        var draft = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Interval, 120_000, null, null, 74_075)]);
        var id = draft.Rows[0].Id;
        Assert.Equal("1.23458", draft.Rows[0].RuntimeText);
        // Showing the stored limit rounded changes nothing that is sent.
        Assert.Equal(74_075, draft.Rows[0].Trigger.MaxRuntimeMs);

        // A negative limit is saved as 0 (no limit), so the box shows the 0 that Save sends.
        draft = draft.SetRuntime(id, "-5");
        Assert.Equal("0", draft.Rows[0].RuntimeText);
        Assert.Equal(0, draft.Rows[0].Trigger.MaxRuntimeMs);

        // A positive limit under half a millisecond saves as 0 too, but is kept while the admin types and shows 0 once they leave the box.
        draft = draft.SetRuntime(id, "0.000008");
        Assert.Equal("0.000008", draft.Rows[0].RuntimeText);
        draft = draft.LeaveRuntime(id);
        Assert.Equal("0", draft.Rows[0].RuntimeText);
        Assert.Equal(0, draft.Rows[0].Trigger.MaxRuntimeMs);

        const string typed = "1.23456";
        for (var length = 1; length <= typed.Length; length++)
        {
            draft = draft.SetRuntime(id, typed[..length]);
            Assert.Equal(typed[..length], draft.Rows[0].RuntimeText);
        }
        Assert.Equal("1.23456", draft.Rows[0].RuntimeText);
        Assert.Equal([new AdminTaskTrigger(AdminTaskTriggerTypes.Interval, 120_000, null, null, 74_074)], draft.ToTriggers());
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1L, "0.00002")]
    [InlineData(30_000L, "0.5")]
    [InlineData(60_000L, "1")]
    [InlineData(74_075L, "1.23458")]
    [InlineData(5_400_000L, "90")]
    public void AStoredRuntimeShowsMinutesRoundedToFiveDecimalsAndStillRoundsBackToTheSameMilliseconds(long milliseconds, string text)
    {
        Assert.Equal(text, AdminTaskScheduleDraft.RuntimeText(milliseconds));
        Assert.Equal(milliseconds, AdminTaskScheduleDraft.RuntimeToMs(text));
    }

    [Theory]
    [InlineData("-0", "0")]
    [InlineData("0.0", "0")]
    [InlineData("-0.0000001", "0")]
    [InlineData("0.5", "0.5")]
    [InlineData("0", "0")]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    public void LeavingTheMaxRuntimeBoxShowsTheZeroThatSaveSendsOnlyWhenItRoundsToNoLimit(string typed, string shownAfterLeaving)
    {
        var draft = AdminTaskScheduleDraft.From([Interval(60_000)]);
        var id = draft.Rows[0].Id;
        draft = draft.SetRuntime(id, typed).LeaveRuntime(id);
        Assert.Equal(shownAfterLeaving, draft.Rows[0].RuntimeText);
    }

    [Fact]
    public void LeavingAnUntouchedMaxRuntimeBoxChangesNothing()
    {
        var draft = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Interval, 60_000, null, null, 74_075)]);
        Assert.Same(draft.Rows[0], draft.LeaveRuntime(draft.Rows[0].Id).Rows[0]);
        Assert.True(draft.LeaveRuntime(draft.Rows[0].Id).Matches([new AdminTaskTrigger(AdminTaskTriggerTypes.Interval, 60_000, null, null, 74_075)]));
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("7:00")]
    [InlineData("")]
    public void DailyTimeMustBeHoursAndMinutes(string time)
    {
        var draft = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Daily, null, "05:00", null, null)]);
        draft = draft.SetTime(draft.Rows[0].Id, time);
        Assert.False(draft.IsValid);
    }

    [Fact]
    public void WeeklyRowsCarryDayAndTime()
    {
        var draft = AdminTaskScheduleDraft.From([new(AdminTaskTriggerTypes.Weekly, null, "05:00", 1, null)]);
        var id = draft.Rows[0].Id;
        draft = draft.SetDay(id, 6).SetTime(id, "23:59");
        Assert.Equal([new AdminTaskTrigger(AdminTaskTriggerTypes.Weekly, null, "23:59", 6, null)], draft.ToTriggers());
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.SetDay(id, 7));
    }

    [Fact]
    public void MatchesTracksWhetherTheDraftDiffersFromItsBaseline()
    {
        IReadOnlyList<AdminTaskTrigger> saved = [Interval(120_000)];
        var draft = AdminTaskScheduleDraft.From(saved);
        Assert.True(draft.Matches(saved));
        var edited = draft.SetInterval(draft.Rows[0].Id, "3", AdminTaskIntervalUnit.Minutes);
        Assert.False(edited.Matches(saved));
        Assert.True(edited.SetInterval(draft.Rows[0].Id, "2", AdminTaskIntervalUnit.Minutes).Matches(saved));
        Assert.False(draft.Remove(draft.Rows[0].Id).Matches(saved));
        Assert.False(draft.SetInterval(draft.Rows[0].Id, "", AdminTaskIntervalUnit.Minutes).Matches(saved));
    }

    [Fact]
    public void ADraftNeverExceedsTheWriteBound()
    {
        var draft = AdminTaskScheduleDraft.From([]);
        for (var count = 0; count < AdminTasksApi.MaxTriggers + 3; count++) draft = draft.Add();
        Assert.Equal(AdminTasksApi.MaxTriggers, draft.Rows.Count);
        Assert.False(draft.CanAdd);
    }
}
