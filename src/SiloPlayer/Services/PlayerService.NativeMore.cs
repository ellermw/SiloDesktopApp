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
        var statsShown = IsPlaybackInfoShown;
        // More lists only what its rail does not show (#3993): a compact rail
        // keeps subtitles, settings and fullscreen; a full rail also keeps audio,
        // chapters and mute, plus the volume slider on wide rails.
        var compact = request.Layout == "compact";
        var volumeRows = request.Layout != "full";
        // Catalog labels where the WebUI has the same control; desktop-only rows
        // and states (panel visibility, volume steps, mute toggle) stay English.
        PlaybackMenuChoice[] AllChoices() =>
        [
            new("audio", HudText("web.player.audioMenu.trigger", "Audio tracks"), "", [], IsEnabled: audioAvailable,
                DisabledReason: audioAvailable ? "" : HudText("web.player.audioMenu.onlyOneTrack", "Only one audio track")),
            new("chapters", HudText("web.player.chaptersMenu.trigger", "Chapters"), "", [], IsEnabled: chaptersAvailable,
                DisabledReason: chaptersAvailable ? "" : "No chapters available."),
            // Playback Info is the OSC's own drawn panel, not a native list, so
            // say what this row does and whether the panel is shown.
            new("stats", HudText("web.player.controls.playbackInfo", "Playback info"), statsShown ? "On-screen panel shown" : "On-screen panel hidden", [],
                IsCurrent: statsShown),
            new("pip", HudText("web.player.controls.pictureInPictureItem", "Picture in picture"), "", []),
            new("fill", fill ? "Contain video" : HudText("web.player.controls.fillScreen", "Fill screen"), "", [], IsCurrent: fill),
            new("volume-down", "Volume -", $"{volume:0}%", [], IsEnabled: volume > 0,
                DisabledReason: volume > 0 ? "" : "Minimum volume."),
            new("volume-up", "Volume +", $"{volume:0}%", [], IsEnabled: volume < 100,
                DisabledReason: volume < 100 ? "" : "Maximum volume."),
            new("mute", "Mute / unmute", mute ? "Muted" : "Unmuted", [], IsCurrent: mute),
        ];
        PlaybackMenuChoice[] Choices() => AllChoices().Where(choice => choice.Id switch
        {
            "audio" or "chapters" or "mute" => compact,
            "volume-down" or "volume-up" => volumeRows,
            _ => true,
        }).ToArray();
        var choices = Choices();
        var menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, HudText("web.player.controls.moreOptions", "More player options"), choices,
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
        menu.Localize(() => HudText("web.player.controls.moreOptions", "More player options"), Choices);
        return menu;
    }
}

