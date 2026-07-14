using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class SubtitleAutoSelectCurrentParityTests
{
    [Fact]
    public void AlwaysModePrefersExactPersistedTrackSignature()
    {
        var tracks = new[]
        {
            new SubtitleAutoSelect.SubtitleCandidate(1, "eng", "external", false, "srt", "English"),
            new SubtitleAutoSelect.SubtitleCandidate(2, "eng", "embedded", false, "ass", "Signs & Songs"),
        };
        var signature = new SubtitleTrackSignature
        {
            Source = "embedded",
            Language = "eng",
            Codec = "ass",
            Label = "Signs & Songs",
        };

        var selected = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            "always", tracks, "en", "jpn", null, true, signature));

        Assert.Equal(2, selected);
    }

    [Fact]
    public void SameSourceTextTrackBeatsBitmapTrack()
    {
        var tracks = new[]
        {
            new SubtitleAutoSelect.SubtitleCandidate(4, "eng", "external", false, "pgs", "English PGS"),
            new SubtitleAutoSelect.SubtitleCandidate(7, "eng", "external", false, "srt", "English SRT"),
        };

        var selected = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            "always", tracks, "eng", "jpn", null, true));

        Assert.Equal(7, selected);
    }

    [Fact]
    public void SessionSubtitlePayloadBuildsFullParityCandidates()
    {
        var candidates = SubtitleAutoSelect.BuildCandidates(
        [
            new SubtitleTrackInfo
            {
                Index = 9,
                Language = "eng",
                Source = "downloaded",
                Codec = "srt",
                Label = "English SDH",
                HearingImpaired = true,
            },
        ]);

        var candidate = Assert.Single(candidates);
        Assert.Equal(9, candidate.OriginalIndex);
        Assert.Equal("downloaded", candidate.Source);
        Assert.Equal("srt", candidate.Codec);
        Assert.Equal("English SDH", candidate.Label);
        Assert.True(candidate.HearingImpaired);
    }
}
