using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private PlaybackChoiceMenu CreateNativeMoreMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var selected = false;
        var volume = Math.Clamp(owner.Mpv.GetPropertyDouble("volume"), 0, 100);
        var fill = owner.Mpv.GetPropertyDouble("panscan") > 0;
        var mute = owner.Mpv.GetMute();
        var audioAvailable = (ActiveVersion?.AudioTracks?.Count ?? 0) > 1;
        var chaptersAvailable = (ActiveVersion?.Chapters?.Count ?? 0) > 0;
        // This is the existing compact overflow order. Subtitle, settings and
        // fullscreen remain on the compact rail, so there are no duplicates.
        PlaybackMenuChoice[] choices =
        [
            new("audio", "Audio tracks", "", [], IsEnabled: audioAvailable,
                DisabledReason: audioAvailable ? "" : "No alternate audio tracks."),
            new("chapters", "Chapters", "", [], IsEnabled: chaptersAvailable,
                DisabledReason: chaptersAvailable ? "" : "No chapters available."),
            new("stats", "Playback info", "", []),
            new("pip", "Picture in picture", "", []),
            new("fill", fill ? "Contain video" : "Fill screen", "", [], IsCurrent: fill),
            new("volume-down", "Volume -", $"{volume:0}%", [], IsEnabled: volume > 0,
                DisabledReason: volume > 0 ? "" : "Minimum volume."),
            new("volume-up", "Volume +", $"{volume:0}%", [], IsEnabled: volume < 100,
                DisabledReason: volume < 100 ? "" : "Maximum volume."),
            new("mute", "Mute / unmute", mute ? "Muted" : "Unmuted", [], IsCurrent: mute),
        ];
        return new PlaybackChoiceMenu(owner.Video, request.Anchor, "More player options", choices,
            () => owner.IsCurrent() && _nativeMenuGate.IsCurrent(request),
            id =>
            {
                // Selection closes/releases the parent first. Lua validates
                // the matching one-use closed request before forwarding to
                // its existing utility handler or opening a child menu.
                if (!selected || !owner.IsCurrent() ||
                    !choices.Any(choice => choice.Id == id && choice.IsEnabled)) return;
                owner.Mpv.SendScriptMessage("osc-native-more-action", owner.Token,
                    request.Sequence.ToString(CultureInfo.InvariantCulture), id);
            },
            reason =>
            {
                selected = reason is PlaybackMenuCloseReason.Selection or PlaybackMenuCloseReason.PointerSelection;
                finish(reason);
            },
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root));
    }
}

