using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class SeekPreferencesTests
{
    [Fact]
    public void Shared_intervals_use_valid_profile_values_and_ignore_corrupt_values()
    {
        var result = SeekPreferences.Read(new() { Settings = [
            new() { Key = SeekPreferences.Keys[0], Value = JsonSerializer.SerializeToElement(45) },
            new() { Key = SeekPreferences.Keys[1], Value = JsonSerializer.SerializeToElement(13) },
            new() { Key = SeekPreferences.Keys[2], Value = JsonSerializer.SerializeToElement("60") },
            new() { Key = SeekPreferences.Keys[3], Value = JsonSerializer.SerializeToElement(90) }] });
        Assert.Equal(new SeekPreferences(45, 30, 10, 90), result);
    }
    [Theory]
    [InlineData("playback.intro_skip_mode", true)]
    [InlineData("catalog.metadata_language", false)]
    [InlineData("player.audiobook_skip_back_seconds", false)]
    [InlineData("ui.theme_music_enabled", false)]
    public void Profile_only_preferences_do_not_clear_an_unsupported_device_scope(string key, bool expected)
        => Assert.Equal(expected, SeekPreferences.SupportsDeviceOverride(key));
}
