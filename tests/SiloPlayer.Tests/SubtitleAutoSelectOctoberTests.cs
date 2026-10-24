using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleAutoSelectOctoberTests
{
    [Theory]
    [InlineData("en", "es", "en", true, 3)]
    [InlineData("eng", "en-US", "es", true, 2)]
    [InlineData("en-GB", "eng", null, false, null)]
    [InlineData(null, "en", "en", true, 1)]
    [InlineData("und", "en", "en", true, 1)]
    [InlineData("original", "en", "en", true, 1)]
    [InlineData("x-silo-original", "en", "en", true, 1)]
    [InlineData("en", null, "es", true, 2)]
    [InlineData("en", "", "en", true, null)]
    [InlineData(null, null, "en", true, null)]
    [InlineData("fra-CA", "fr-FR", "en", true, 5)]
    [InlineData("jpn", "fra-CA", "en", true, 4)]
    public void AutoUsesSubtitlePreferenceAndActualAudio(string? audio, string? subtitle,
        string? profile, bool forced, int? expected)
    {
        var tracks = Tracks();
        Assert.Equal(expected, SubtitleAutoSelect.Resolve(new("auto", tracks, subtitle, audio, profile, forced)));
    }

    [Theory]
    [InlineData("off", "en", true, 2)]
    [InlineData("off", "und", true, null)]
    [InlineData("off", "en", false, null)]
    [InlineData("always", "en", false, 3)]
    public void OffAndAlwaysRetainTheirOwnSemantics(string mode, string audio, bool forced, int? expected)
        => Assert.Equal(expected, SubtitleAutoSelect.Resolve(new(mode, Tracks(), "es", audio, "en", forced)));

    private static SubtitleAutoSelect.SubtitleCandidate[] Tracks() =>
    [
        new(1, "eng", "embedded", false, "srt"),
        new(2, "en", "embedded", true, "srt"),
        new(3, "spa", "embedded", false, "srt"),
        new(4, "fre", "embedded", false, "srt"),
        new(5, "fra", "embedded", true, "srt"),
    ];
}
