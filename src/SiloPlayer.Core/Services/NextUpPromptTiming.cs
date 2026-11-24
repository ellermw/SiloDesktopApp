using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>
/// When the Next Up screen opens, from the end of the file.
/// Mirrors official silo-server 3b2b32168 (#4502): <c>web/src/playback/nextUpPrompt.ts</c>
/// (<c>isInNextUpPromptWindow</c>) and the later-part hold in
/// <c>WatchPlaybackChrome.tsx</c>. The viewer's
/// <c>playback.next_up_prompt_seconds</c> is the lead time before the END OF THE FILE;
/// 0 ("At end") never opens it early, the player's end of playback does.
/// </summary>
public static class NextUpPromptTiming
{
    public const string SettingKey = "playback.next_up_prompt_seconds";

    /// <summary>The contract default, also used when the effective read fails.</summary>
    public const int DefaultSeconds = 30;

    /// <summary>Contract maximum for the setting.</summary>
    public const int MaximumSeconds = 120;

    /// <summary>The Show Next Up choices, in seconds before the end; 0 is "At end".</summary>
    public static IReadOnlyList<int> Choices { get; } = [0, 10, 30, 60, 120];

    /// <summary>
    /// Whether position is within <paramref name="promptSeconds"/> of the end of the file.
    /// A null value (the setting has not loaded yet) or 0 never opens Next Up early.
    /// </summary>
    public static bool IsInWindow(double position, double duration, int? promptSeconds)
    {
        if (promptSeconds is not > 0) return false;
        if (!double.IsFinite(duration) || duration <= 0) return false;
        if (!double.IsFinite(position) || position <= 0) return false;
        var remaining = duration - position;
        return remaining >= 0 && remaining <= promptSeconds.Value;
    }

    /// <summary>
    /// An item with a later part plays that part when this one ends, so Next Up
    /// waits for the last part instead of offering to skip the rest.
    /// <paramref name="partIndex"/> is zero-based; a file that is not split is part 0 of 1.
    /// </summary>
    public static bool HoldsForLaterPart(int partIndex, int partCount) => partIndex < partCount - 1;

    /// <summary>Whether Next Up opens early now: inside the window, and on the last part.</summary>
    public static bool IsDue(int? promptSeconds, double duration, double position, int partIndex, int partCount)
        => IsInWindow(position, duration, promptSeconds) && !HoldsForLaterPart(partIndex, partCount);

    /// <summary>The setting from an effective read; a missing or unreadable value is the contract default.</summary>
    public static int Read(ContractEffectiveSettingsResponse settings)
    {
        var entry = settings.Settings.FirstOrDefault(item => item.Key == SettingKey);
        return entry is null ? DefaultSeconds : Parse(entry.Value);
    }

    public static int Parse(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
            return Math.Clamp((int)Math.Round(number), 0, MaximumSeconds);
        return DefaultSeconds;
    }

    /// <summary>Catalog key and count for a choice, as official's picker names it.</summary>
    public static (string Key, int? Count) LabelKey(int seconds)
    {
        if (seconds <= 0) return ("settings.nextUp.atEnd", null);
        return seconds % 60 == 0
            ? ("settings.nextUp.minutesBeforeEnd", seconds / 60)
            : ("settings.nextUp.secondsBeforeEnd", seconds);
    }

    /// <summary>Official English wording, the fallback when the catalog has no entry.</summary>
    public static string EnglishLabel(int seconds)
    {
        var (key, count) = LabelKey(seconds);
        return key switch
        {
            "settings.nextUp.atEnd" => "At end",
            "settings.nextUp.minutesBeforeEnd" => count == 1
                ? "1 minute before end" : string.Create(CultureInfo.InvariantCulture, $"{count} minutes before end"),
            _ => count == 1
                ? "1 second before end" : string.Create(CultureInfo.InvariantCulture, $"{count} seconds before end"),
        };
    }
}
