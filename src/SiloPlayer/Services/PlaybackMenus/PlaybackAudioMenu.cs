using Microsoft.UI.Xaml;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackAudioChoice(int SourceIndex, string Title, string Metadata,
    string Codec, string Channels, bool IsDefault, bool IsCurrent);

internal sealed class PlaybackAudioMenu : PlaybackChoiceMenu
{
    internal PlaybackAudioMenu(MpvVideoWindow owner, PlaybackMenuAnchor anchor,
        IReadOnlyList<PlaybackAudioChoice> choices, Func<bool> isCurrent,
        Action<int> selected, Action<PlaybackMenuCloseReason> closed, Action<DependencyObject>? applyTypography = null)
        : base(owner, anchor, "Audio", choices.Select(choice => new PlaybackMenuChoice(
            choice.SourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), choice.Title, choice.Metadata,
            new[] { choice.Codec, choice.Channels, choice.IsDefault ? "Default" : "" }.Where(s => s.Length > 0).ToArray(), choice.IsCurrent)).ToArray(),
            isCurrent, id => selected(int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)), closed, applyTypography)
    { }
}
