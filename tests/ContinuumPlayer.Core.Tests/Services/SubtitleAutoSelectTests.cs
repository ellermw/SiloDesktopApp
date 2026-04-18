using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class SubtitleAutoSelectTests
{
    // ─── mode normalization ──────────────────────────────────────────────

    [Theory]
    [InlineData("off", "off")]
    [InlineData("auto", "auto")]
    [InlineData("always", "always")]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("bogus", "auto")]
    public void NormalizeSubtitleMode_MapsValidOrDefaultsAuto(string? input, string expected)
    {
        Assert.Equal(expected, SubtitleAutoSelect.NormalizeSubtitleMode(input));
    }

    // ─── findPreferredSubtitleIndex: source priority ─────────────────────

    [Fact]
    public void FindPreferredIndex_PrefersExternalOverDownloadedOverEmbedded()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
            new(OriginalIndex: 1, Language: "eng", Source: "downloaded", Forced: false),
            new(OriginalIndex: 2, Language: "eng", Source: "external", Forced: false),
        };

        Assert.Equal(2, SubtitleAutoSelect.FindPreferredSubtitleIndex(tracks, "eng"));
    }

    [Fact]
    public void FindPreferredIndex_ReturnsMinusOneWhenNoLanguageMatch()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
        };

        Assert.Equal(-1, SubtitleAutoSelect.FindPreferredSubtitleIndex(tracks, "jpn"));
    }

    [Fact]
    public void FindPreferredIndex_MatchesIso639TwoAndThreeLetter()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
        };

        // User pref is 2-letter "en"; track is 3-letter "eng".
        Assert.Equal(0, SubtitleAutoSelect.FindPreferredSubtitleIndex(tracks, "en"));
    }

    // ─── resolve: mode=off ───────────────────────────────────────────────

    [Fact]
    public void Resolve_Off_WithForced_PicksForcedTrackMatchingAudioLang()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
            new(OriginalIndex: 1, Language: "eng", Source: "embedded", Forced: true),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "off",
            Tracks: tracks,
            PreferredLanguage: null,
            AudioLanguage: "eng",
            ProfileLanguage: null,
            ShowForcedSubtitles: true));

        Assert.Equal(1, idx);
    }

    [Fact]
    public void Resolve_Off_NoForcedRequested_ReturnsNull()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: true),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "off",
            Tracks: tracks,
            PreferredLanguage: null,
            AudioLanguage: "eng",
            ProfileLanguage: null,
            ShowForcedSubtitles: false));

        Assert.Null(idx);
    }

    // ─── resolve: mode=always ────────────────────────────────────────────

    [Fact]
    public void Resolve_Always_PicksPreferredLanguageTrack()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
            new(OriginalIndex: 1, Language: "spa", Source: "embedded", Forced: false),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "always",
            Tracks: tracks,
            PreferredLanguage: "spa",
            AudioLanguage: null,
            ProfileLanguage: null,
            ShowForcedSubtitles: false));

        Assert.Equal(1, idx);
    }

    [Fact]
    public void Resolve_Always_NoPreferredLanguage_ReturnsNull()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "always",
            Tracks: tracks,
            PreferredLanguage: null,
            AudioLanguage: null,
            ProfileLanguage: null,
            ShowForcedSubtitles: false));

        Assert.Null(idx);
    }

    // ─── resolve: mode=auto ──────────────────────────────────────────────

    [Fact]
    public void Resolve_Auto_AudioMatchesProfile_ReturnsForcedOnly()
    {
        // Audio is English, user's profile is English → auto means "don't show
        // dialogue subs, only forced subs for foreign-language dialogue".
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
            new(OriginalIndex: 1, Language: "eng", Source: "embedded", Forced: true),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "auto",
            Tracks: tracks,
            PreferredLanguage: "en",
            AudioLanguage: "en",
            ProfileLanguage: "en",
            ShowForcedSubtitles: true));

        Assert.Equal(1, idx);
    }

    [Fact]
    public void Resolve_Auto_AudioDifferentFromProfile_PicksPreferredLanguage()
    {
        // Japanese audio, user prefs are English → auto shows English subs.
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
            new(OriginalIndex: 1, Language: "jpn", Source: "embedded", Forced: false),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "auto",
            Tracks: tracks,
            PreferredLanguage: "en",
            AudioLanguage: "jpn",
            ProfileLanguage: "en",
            ShowForcedSubtitles: true));

        Assert.Equal(0, idx);
    }

    [Fact]
    public void Resolve_Auto_NoLanguagesAtAll_ReturnsNull()
    {
        var tracks = new List<SubtitleAutoSelect.SubtitleCandidate>
        {
            new(OriginalIndex: 0, Language: "eng", Source: "embedded", Forced: false),
        };

        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "auto",
            Tracks: tracks,
            PreferredLanguage: "",      // explicit empty = "off, no auto"
            AudioLanguage: null,
            ProfileLanguage: null,
            ShowForcedSubtitles: false));

        Assert.Null(idx);
    }

    [Fact]
    public void Resolve_EmptyTracks_ReturnsNull()
    {
        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: "auto",
            Tracks: [],
            PreferredLanguage: "en",
            AudioLanguage: "en",
            ProfileLanguage: "en",
            ShowForcedSubtitles: true));

        Assert.Null(idx);
    }
}
