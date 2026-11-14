using Microsoft.UI.Xaml;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackAudioChoice(int SourceIndex, string Title, string Metadata,
    string Codec, string Channels, bool IsDefault, bool IsCurrent)
{
    internal bool IsEnabled { get; init; } = true;
    internal string DisabledReason { get; init; } = "";
}

internal sealed class PlaybackAudioMenu : PlaybackChoiceMenu
{
    internal PlaybackAudioMenu(MpvVideoWindow owner, PlaybackMenuAnchor anchor,
        IReadOnlyList<PlaybackAudioChoice> choices, Func<bool> isCurrent,
        Action<int> selected, Action<PlaybackMenuCloseReason> closed, Action<DependencyObject>? applyTypography = null,
        bool allowDisabledTraversal = false)
        : base(owner, anchor, Heading(), Rows(choices),
            isCurrent, id => selected(int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)), closed, applyTypography,
            allowDisabledTraversal: allowDisabledTraversal)
    { }

    internal static string Heading() => AppStrings.Get("web.player.audioMenu.heading", fallback: "Audio");

    /// <summary>Rows keyed by the source track index; only display text varies with the interface language.</summary>
    internal static IReadOnlyList<PlaybackMenuChoice> Rows(IReadOnlyList<PlaybackAudioChoice> choices) => choices.Select(choice => new PlaybackMenuChoice(
            choice.SourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), choice.Title, choice.Metadata,
            new[] { choice.Codec, choice.Channels, choice.IsDefault ? AppStrings.Get("web.player.audioMenu.defaultBadge", fallback: "Default") : "" }
                .Where(s => s.Length > 0).ToArray(), choice.IsCurrent,
            choice.IsEnabled, choice.DisabledReason)).ToArray();
}
