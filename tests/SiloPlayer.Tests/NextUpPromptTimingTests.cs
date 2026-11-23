using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Next Up is timed from the END OF THE FILE by playback.next_up_prompt_seconds.
/// Reference: official silo-server 3b2b32168 (#4502, commit 7019d69dd),
/// web/src/playback/nextUpPrompt.ts and nextUpPrompt.test.ts.
/// </summary>
public sealed class NextUpPromptTimingTests
{
    // Ported from the official nextUpPrompt.test.ts, case for case.
    [Theory]
    [InlineData(1290, 1320, 30, true, "exactly N seconds left")]
    [InlineData(1289, 1320, 30, false, "more than N seconds left")]
    [InlineData(1319.9, 1320, 0, false, "At end waits for ended")]
    [InlineData(0, 20, 30, false, "not yet started")]
    [InlineData(10, 0, 30, false, "duration unknown")]
    public void WindowMatchesTheOfficialCases(double at, double duration, int seconds, bool expected, string why)
        => Assert.True(NextUpPromptTiming.IsInWindow(at, duration, seconds) == expected, why);

    [Theory]
    [InlineData(1290, 1320, true)]
    [InlineData(1319.9, 1320, true)]
    [InlineData(1320, 1320, true)]
    [InlineData(1320.5, 1320, false)]
    [InlineData(double.NaN, 1320, false)]
    [InlineData(100, double.NaN, false)]
    [InlineData(100, double.PositiveInfinity, false)]
    [InlineData(-5, 1320, false)]
    public void WindowEdges(double at, double duration, bool expected)
        => Assert.Equal(expected, NextUpPromptTiming.IsInWindow(at, duration, 30));

    [Fact]
    public void AnUnloadedSettingNeverOpensNextUpEarly()
    {
        // Official waits for the effective setting before opening early, so a viewer who
        // chose "At end" and resumes near the end is not shown Next Up on the default.
        Assert.False(NextUpPromptTiming.IsInWindow(1319, 1320, null));
        Assert.False(NextUpPromptTiming.IsDue(null, 1320, 1319, 0, 1));
    }

    [Theory]
    [InlineData(10, 1309, false)]
    [InlineData(10, 1310, true)]
    [InlineData(60, 1259, false)]
    [InlineData(60, 1260, true)]
    [InlineData(120, 1199, false)]
    [InlineData(120, 1200, true)]
    [InlineData(0, 1319.99, false)]
    public void EveryChoiceOpensItsLeadTimeBeforeTheEndOfTheFile(int seconds, double at, bool due)
        => Assert.Equal(due, NextUpPromptTiming.IsDue(seconds, 1320, at, 0, 1));

    [Fact]
    public void ASeekBackOutOfTheWindowIsNotDueAgain()
    {
        Assert.True(NextUpPromptTiming.IsDue(30, 1320, 1300, 0, 1));
        Assert.False(NextUpPromptTiming.IsDue(30, 1320, 600, 0, 1));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, false)]
    [InlineData(1, 2, true)]
    [InlineData(0, 3, false)]
    [InlineData(1, 3, false)]
    [InlineData(2, 3, true)]
    public void ASplitItemHoldsNextUpUntilItsLastPart(int partIndex, int partCount, bool due)
    {
        Assert.Equal(due, NextUpPromptTiming.IsDue(120, 1320, 1319, partIndex, partCount));
        Assert.Equal(!due, NextUpPromptTiming.HoldsForLaterPart(partIndex, partCount));
    }

    [Fact]
    public void PartPositionComesFromTheVariantHoldingThePlayingFile()
    {
        var detail = new WatchDetailResponse { PlaybackVariants =
        [
            new() { Parts = [new() { PartIndex = 0, Versions = [new() { FileId = 99 }] }] },
            new() { Parts =
            [
                new() { PartIndex = 2, Versions = [new() { FileId = 42 }] },
                new() { PartIndex = 1, Versions = [new() { FileId = 11 }] },
                new() { PartIndex = 0, Versions = [new() { FileId = 10 }] },
            ] },
        ] };
        Assert.Equal((0, 1), PlaybackPartSequence.PartOf(detail, 99));
        Assert.Equal((0, 3), PlaybackPartSequence.PartOf(detail, 10));
        Assert.Equal((1, 3), PlaybackPartSequence.PartOf(detail, 11));
        Assert.Equal((2, 3), PlaybackPartSequence.PartOf(detail, 42));
        Assert.Null(PlaybackPartSequence.PartOf(detail, 500));
        Assert.Null(PlaybackPartSequence.PartOf(detail, null));
        Assert.Null(PlaybackPartSequence.PartOf(null, 10));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("10", 10)]
    [InlineData("45", 45)]
    [InlineData("120", 120)]
    [InlineData("500", 120)]
    [InlineData("-3", 0)]
    [InlineData("29.6", 30)]
    [InlineData("\"30\"", 30)]
    [InlineData("null", 30)]
    [InlineData("true", 30)]
    public void StoredValuesReadLikeOfficialsNumberOfValueOrDefault(string json, int expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, NextUpPromptTiming.Parse(document.RootElement));
    }

    [Fact]
    public void AMissingKeyReadsAsTheContractDefault()
    {
        // A server whose contract has no such key answers without it; official reads the default (30).
        Assert.Equal(30, NextUpPromptTiming.DefaultSeconds);
        Assert.Equal(30, NextUpPromptTiming.Read(new ContractEffectiveSettingsResponse()));
        var answered = JsonSerializer.Deserialize<ContractEffectiveSettingsResponse>(
            """{"items":[{"key":"playback.next_up_prompt_seconds","value":0,"source":"profile"}],"revision":17}""")!;
        Assert.Equal(0, NextUpPromptTiming.Read(answered));
    }

    [Fact]
    public void TheChoicesAreOfficialsFiveAndNamedLikeOfficial()
    {
        Assert.Equal([0, 10, 30, 60, 120], NextUpPromptTiming.Choices);
        Assert.Equal(["At end", "10 seconds before end", "30 seconds before end", "1 minute before end", "2 minutes before end"],
            NextUpPromptTiming.Choices.Select(NextUpPromptTiming.EnglishLabel));
        Assert.Equal(("settings.nextUp.atEnd", (int?)null), NextUpPromptTiming.LabelKey(0));
        Assert.Equal(("settings.nextUp.secondsBeforeEnd", (int?)10), NextUpPromptTiming.LabelKey(10));
        Assert.Equal(("settings.nextUp.secondsBeforeEnd", (int?)45), NextUpPromptTiming.LabelKey(45));
        Assert.Equal(("settings.nextUp.minutesBeforeEnd", (int?)1), NextUpPromptTiming.LabelKey(60));
        Assert.Equal(("settings.nextUp.minutesBeforeEnd", (int?)2), NextUpPromptTiming.LabelKey(120));
        Assert.Equal("1 second before end", NextUpPromptTiming.EnglishLabel(1));
        Assert.Equal("1 minute before end", NextUpPromptTiming.EnglishLabel(60));
    }

    [Fact]
    public void ThePreviewKeyHasNoControlAndStaysOffTheDeviceScreen()
    {
        // Official removed the Start next at preview toggle and hid the key from device settings;
        // the server key stays so other clients are unaffected.
        Assert.DoesNotContain(DeviceSettingDisplay.ForRevision(30), definition => definition.Key == "playback.auto_play_next_preview");
        Assert.Contains(DeviceSettingDisplay.ForRevision(30), definition => definition.Key == "playback.next_up_prompt_seconds");
    }

    [Fact]
    public void ThePlayerOpensNextUpFromTheSettingAndHoldsForLaterPartsAndWaitsForTheSetting()
    {
        var source = Read("src", "SiloPlayer", "Services", "PlayerService.NextUpPrompt.cs");
        Assert.Contains("NextUpPromptTiming.IsDue(NextUpPromptSeconds, CurrentMediaDuration, mediaPosition", source);
        Assert.Contains("PlaybackPartSequence.PartOf(WatchDetail, ActiveMediaFileId)", source);
        // Unloaded until the effective read answers; a failed read or a server without the key is the default.
        Assert.Contains("NextUpPromptUnloaded = -1", source);
        Assert.Contains("NextUpPromptTiming.DefaultSeconds", source);
        Assert.Contains("[NextUpPromptTiming.SettingKey]", source);
        var service = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("RefreshNextUpPromptSecondsAsync(_authorityLifetimeCts.Token, reset: true)", service);
        var changed = Read("src", "SiloPlayer", "Services", "PlayerService.MarkerDetails.cs");
        Assert.Contains("key == NextUpPromptTiming.SettingKey", changed);
    }

    [Fact]
    public void SettingsPlaybackOffersShowNextUpAndSavesThroughTheContractPath()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        Assert.Contains("loc:Localized.Text=\"settings.nextUp.title\"", xaml);
        Assert.Contains("x:Name=\"NextUpPromptComboBox\"", xaml);
        Assert.DoesNotContain("Start next at preview", xaml);
        var model = Read("src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs");
        Assert.Contains("SaveContractProfileSettingAsync(NextUpPromptTiming.SettingKey, value)", model);
        Assert.DoesNotContain("AutoPlayNextPreview", model);
        // A device override on this PC resolves before the profile value: the player is told again
        // once it is deleted, so a read that raced the delete cannot leave the old lead time in effect.
        var save = model[model.IndexOf("private async Task<bool> SaveContractProfileSettingAsync", StringComparison.Ordinal)..];
        var cleared = save.IndexOf("await ClearSupportedDeviceOverrideAsync(key);", StringComparison.Ordinal);
        Assert.True(cleared >= 0);
        Assert.True(save.IndexOf("if (key == NextUpPromptTiming.SettingKey) _settingsApi.NotifyLocalizedSettingChanged(key);",
            cleared, StringComparison.Ordinal) > cleared);
        var page = Read("src", "SiloPlayer", "Views", "SettingsPage.NextUpPrompt.cs");
        Assert.Contains("NextUpPromptTiming.Choices", page);
    }

    [Fact]
    public void TheEndedScreenWaitsForTheAutoPlaySettingBeforeCountingDown()
    {
        var cover = Read("src", "SiloPlayer", "MainWindow.PlaybackCover.cs");
        Assert.Contains("_playingNextAutoPlayLoaded && _playingNextAutoPlay", cover);
        var window = Read("src", "SiloPlayer", "MainWindow.xaml.cs");
        Assert.Contains("_playingNextAutoPlayLoaded = true;", window);
        Assert.Contains("_playingNextAutoPlayLoaded = false;", window);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "SiloPlayer.Core")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
