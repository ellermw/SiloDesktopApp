using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Resolves Resume versus Start Over without carrying file-bound track choices to another part.</summary>
public sealed record DetailPlaybackIntent(FileVersion? File, int? AudioTrackIndex, int? SubtitleIndex, SubtitleTrackSignature? SubtitleSignature)
{
    public static DetailPlaybackIntent Create(FileVersion? selected, IReadOnlyList<PlaybackVariant>? variants,
        string? quality, bool fromStart, int? audioIndex, int? subtitleIndex, SubtitleTrackSignature? subtitle)
    {
        var target = fromStart ? DetailVersionRanking.StartVersion(selected, variants, quality) : selected;
        if (target?.FileId == selected?.FileId) return new(target, audioIndex, subtitleIndex, subtitle);
        // Language/source/flags are portable. Index, codec/title identity and
        // downloaded/external labels describe only the previously selected file.
        var portable = subtitle == null ? null : new SubtitleTrackSignature
        {
            Source = subtitle.Source, Language = subtitle.Language,
            Forced = subtitle.Forced, HearingImpaired = subtitle.HearingImpaired,
        };
        return new(target, null, subtitleIndex == -1 ? -1 : null, portable);
    }
}
