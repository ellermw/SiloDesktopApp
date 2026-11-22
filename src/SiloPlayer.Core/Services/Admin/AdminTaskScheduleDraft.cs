using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminTaskIntervalUnit { Seconds, Minutes, Hours }

/// <summary>
/// One editor row. <see cref="Id"/> is the row's stable identity, so removing a row never hands its
/// typed text or unit to the row below (official <c>draftKeys</c>). <see cref="IntervalText"/>,
/// <see cref="Unit"/> and <see cref="RuntimeText"/> are exactly what was typed and picked.
/// </summary>
public sealed record AdminTaskTriggerRow(long Id, AdminTaskTrigger Trigger, string IntervalText, AdminTaskIntervalUnit Unit, string RuntimeText)
{
    /// <summary>An interval trigger needs an <c>interval_ms</c> the API accepts.</summary>
    public bool IntervalValid => Trigger.Type != AdminTaskTriggerTypes.Interval || Trigger.IntervalMs is >= 1 and <= AdminTasksApi.MaxDurationMs;

    /// <summary>Empty means no limit; anything else must be a number of minutes the API can hold.</summary>
    public bool RuntimeValid => RuntimeText.Trim().Length == 0 || AdminTaskScheduleDraft.RuntimeToMs(RuntimeText) != null;

    public bool TimeValid => Trigger.Type is not (AdminTaskTriggerTypes.Daily or AdminTaskTriggerTypes.Weekly) || AdminTasksApi.IsTimeOfDay(Trigger.TimeOfDay);

    public bool IsValid => AdminTaskTriggerTypes.All.Contains(Trigger.Type) && IntervalValid && RuntimeValid && TimeValid;
}

/// <summary>
/// The schedule editor's draft (official <c>TaskSchedulePanel</c> and <c>TriggerFormRow</c>): stable row
/// identities, the values each trigger type shows until edited, the typed interval unit, and
/// minutes for max runtime. Immutable; every edit returns a new draft.
/// </summary>
public sealed record AdminTaskScheduleDraft(IReadOnlyList<AdminTaskTriggerRow> Rows, long NextId)
{
    /// <summary>The trigger a new row starts with: hourly.</summary>
    public static AdminTaskTrigger AddedTrigger { get; } = new(AdminTaskTriggerTypes.Interval, 3_600_000, null, null, null);
    /// <summary>The interval a row switched to Interval shows until edited.</summary>
    public const long SwitchedIntervalMs = 60_000;
    public const string DefaultTime = "00:00";
    private const long SecondMs = 1000, MinuteMs = 60_000, HourMs = 3_600_000;

    public static AdminTaskScheduleDraft From(IReadOnlyList<AdminTaskTrigger> triggers)
    {
        var rows = new List<AdminTaskTriggerRow>(triggers.Count);
        long next = 0;
        foreach (var trigger in triggers) rows.Add(Row(next++, trigger));
        return new(rows.AsReadOnly(), next);
    }

    public bool CanAdd => Rows.Count < AdminTasksApi.MaxTriggers;
    public bool IsValid => Rows.Count <= AdminTasksApi.MaxTriggers && Rows.All(row => row.IsValid);

    public AdminTaskScheduleDraft Add() => CanAdd ? this with { Rows = Rows.Append(Row(NextId, AddedTrigger)).ToArray(), NextId = NextId + 1 } : this;

    public AdminTaskScheduleDraft Remove(long id) => this with { Rows = Rows.Where(row => row.Id != id).ToArray() };

    public AdminTaskScheduleDraft SetType(long id, string type)
    {
        if (!AdminTaskTriggerTypes.All.Contains(type)) throw new ArgumentException("Unknown trigger type.", nameof(type));
        return Update(id, row =>
        {
            var trigger = row.Trigger with { Type = type };
            if (type == AdminTaskTriggerTypes.Interval && trigger.IntervalMs == null) trigger = trigger with { IntervalMs = SwitchedIntervalMs };
            if (type is AdminTaskTriggerTypes.Daily or AdminTaskTriggerTypes.Weekly && trigger.TimeOfDay == null) trigger = trigger with { TimeOfDay = DefaultTime };
            if (type == AdminTaskTriggerTypes.Weekly && trigger.DayOfWeek == null) trigger = trigger with { DayOfWeek = 0 };
            if (trigger.IntervalMs != row.Trigger.IntervalMs)
            {
                var (text, unit) = IntervalDraft(trigger.IntervalMs);
                return row with { Trigger = trigger, IntervalText = text, Unit = unit };
            }
            return row with { Trigger = trigger };
        });
    }

    public AdminTaskScheduleDraft SetInterval(long id, string text, AdminTaskIntervalUnit unit) =>
        Update(id, row => row with { Trigger = row.Trigger with { IntervalMs = DraftToMs(text, unit) }, IntervalText = text, Unit = unit });

    public AdminTaskScheduleDraft SetTime(long id, string time) => Update(id, row => row with { Trigger = row.Trigger with { TimeOfDay = time } });

    public AdminTaskScheduleDraft SetDay(long id, int day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 6);
        return Update(id, row => row with { Trigger = row.Trigger with { DayOfWeek = day } });
    }

    /// <summary>
    /// Fractional minutes keep sub-minute limits; a typed 0 stays 0 (no limit); only an empty box clears it. The box keeps what was
    /// typed (official #5077), except that a negative entry shows the 0 that Save sends.
    /// </summary>
    public AdminTaskScheduleDraft SetRuntime(long id, string text)
    {
        var shown = IsNegativeNumber(text) ? "0" : text;
        return Update(id, row => row with { Trigger = row.Trigger with { MaxRuntimeMs = shown.Trim().Length == 0 ? null : RuntimeToMs(shown) }, RuntimeText = shown });
    }

    /// <summary>
    /// The admin left the Max runtime box. An entry that rounds to no limit (under half a millisecond, "0.0", "-0") saves as 0, so the
    /// box shows that 0 now, not while they type: "0.00001" can still be entered (official #5077 onBlur).
    /// </summary>
    public AdminTaskScheduleDraft LeaveRuntime(long id)
    {
        var row = Rows.FirstOrDefault(entry => entry.Id == id);
        return row is { Trigger.MaxRuntimeMs: 0 } && row.RuntimeText != "0" ? Update(id, entry => entry with { RuntimeText = "0" }) : this;
    }

    private static bool IsNegativeNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number < 0;

    /// <summary>The triggers to send. Only a valid draft can be sent.</summary>
    public IReadOnlyList<AdminTaskTrigger> ToTriggers() =>
        IsValid ? Array.AsReadOnly(Rows.Select(row => row.Trigger).ToArray()) : throw new InvalidOperationException("The schedule draft is not valid.");

    /// <summary>Whether this draft is valid and would send exactly <paramref name="triggers"/>.</summary>
    public bool Matches(IReadOnlyList<AdminTaskTrigger> triggers) => IsValid && Rows.Select(row => row.Trigger).SequenceEqual(triggers);

    /// <summary>Official <c>intervalToDraft</c>: the largest unit that divides the stored interval evenly.</summary>
    public static (string Text, AdminTaskIntervalUnit Unit) IntervalDraft(long? milliseconds)
    {
        if (milliseconds is not { } value) return ("", AdminTaskIntervalUnit.Minutes);
        if (value >= HourMs && value % HourMs == 0) return ((value / HourMs).ToString(CultureInfo.InvariantCulture), AdminTaskIntervalUnit.Hours);
        if (value >= MinuteMs && value % MinuteMs == 0) return ((value / MinuteMs).ToString(CultureInfo.InvariantCulture), AdminTaskIntervalUnit.Minutes);
        return (((double)value / SecondMs).ToString("R", CultureInfo.InvariantCulture), AdminTaskIntervalUnit.Seconds);
    }

    /// <summary>Official <c>draftToMs</c>: null unless the box holds an interval the API accepts.</summary>
    public static long? DraftToMs(string text, AdminTaskIntervalUnit unit)
    {
        if (text.Trim().Length == 0 || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
        var milliseconds = Math.Floor(number * UnitMs(unit) + 0.5);
        return double.IsFinite(milliseconds) && milliseconds >= 1 && milliseconds <= AdminTasksApi.MaxDurationMs ? (long)milliseconds : null;
    }

    /// <summary>Minutes to milliseconds; negative input means 0, as the official form clamps it. Null when unusable.</summary>
    public static long? RuntimeToMs(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || !double.IsFinite(minutes)) return null;
        var milliseconds = Math.Max(0, Math.Floor(minutes * MinuteMs + 0.5));
        return milliseconds <= AdminTasksApi.MaxDurationMs ? (long)milliseconds : null;
    }

    /// <summary>
    /// A stored limit in minutes, rounded to five decimals so a millisecond count that is not a round number of minutes does not fill the
    /// box with a long fraction. Five decimals of a minute is 0.6 ms, so the text still rounds back to the same millisecond count
    /// (official <c>maxRuntimeToDraft</c>). Plain digits, never an exponent.
    /// </summary>
    public static string RuntimeText(long? milliseconds) =>
        milliseconds is { } value ? Math.Round(value / (decimal)MinuteMs, 5, MidpointRounding.AwayFromZero).ToString("0.#####", CultureInfo.InvariantCulture) : "";

    private static long UnitMs(AdminTaskIntervalUnit unit) => unit switch
    {
        AdminTaskIntervalUnit.Seconds => SecondMs,
        AdminTaskIntervalUnit.Hours => HourMs,
        _ => MinuteMs,
    };

    private static AdminTaskTriggerRow Row(long id, AdminTaskTrigger trigger)
    {
        var (text, unit) = IntervalDraft(trigger.IntervalMs);
        return new(id, trigger, text, unit, RuntimeText(trigger.MaxRuntimeMs));
    }

    private AdminTaskScheduleDraft Update(long id, Func<AdminTaskTriggerRow, AdminTaskTriggerRow> change) =>
        Rows.Any(row => row.Id == id) ? this with { Rows = Rows.Select(row => row.Id == id ? change(row) : row).ToArray() } : this;

    public bool Equals(AdminTaskScheduleDraft? other) => other is not null && NextId == other.NextId && Rows.SequenceEqual(other.Rows);
    public override int GetHashCode() => HashCode.Combine(NextId, Rows.Count);
}
