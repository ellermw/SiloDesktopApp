using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public sealed record SeekPreferences(int VideoBack = 10, int VideoForward = 30, int AudiobookBack = 10, int AudiobookForward = 30)
{
    public static readonly int[] Choices = [5, 10, 15, 30, 45, 60, 90];
    public static readonly string[] Keys = ["player.video_skip_back_seconds", "player.video_skip_forward_seconds",
        "player.audiobook_skip_back_seconds", "player.audiobook_skip_forward_seconds"];
    public static SeekPreferences Read(ContractEffectiveSettingsResponse response)
    {
        int Read(string key, int fallback)
        {
            var value = response.Settings.FirstOrDefault(x => x.Key == key)?.Value;
            return value.HasValue && value.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                && value.Value.TryGetInt32(out var seconds) && Choices.Contains(seconds) ? seconds : fallback;
        }
        return new(Read(Keys[0], 10), Read(Keys[1], 30), Read(Keys[2], 10), Read(Keys[3], 30));
    }
    public static bool SupportsDeviceOverride(string key) => key.StartsWith("playback.", StringComparison.Ordinal);
}
