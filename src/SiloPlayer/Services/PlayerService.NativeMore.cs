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
        // PlayerControlsParts.tsx MoreMenuItems, in the official order: the compact
        // rail's audio and chapters, then Fill screen and Playback info as switches
        // and Picture in picture as an action, each led by its icon. The volume
        // rows stand in for the web's slider on rails that have none.
        PlaybackMenuChoice[] AllChoices() =>
        [
            new("volume-down", HudText("web.player.controls.volume", "Volume") + " −", "", [], IsEnabled: volume > 0)
                { Icon = "volume-1", Trailing = $"{volume:0}%" },
            new("volume-up", HudText("web.player.controls.volume", "Volume") + " +", "", [], IsEnabled: volume < 100)
                { Icon = "volume-2", Trailing = $"{volume:0}%" },
            new("mute", mute ? HudText("web.player.volume.unmute", "Unmute") : HudText("web.player.volume.mute", "Mute"), "", [], IsCurrent: mute)
                { Icon = mute ? "volume-x" : "volume-2" },
            new("audio", HudText("web.player.audioMenu.trigger", "Audio tracks"), "", [], IsEnabled: audioAvailable,
                DisabledReason: audioAvailable ? "" : HudText("web.player.audioMenu.onlyOneTrack", "Only one audio track")) { Icon = "audio-lines" },
            new("chapters", HudText("web.player.chaptersMenu.trigger", "Chapters"), "", [], IsEnabled: chaptersAvailable,
                DisabledReason: chaptersAvailable ? "" : "No chapters available.") { Icon = "list-video" },
            new("fill", HudText("web.player.controls.fillScreen", "Fill screen"), "", [], IsCurrent: fill) { Icon = "scaling", Switch = fill },
            new("stats", HudText("web.player.controls.playbackInfo", "Playback info"), "", [], IsCurrent: statsShown) { Icon = "info", Switch = statsShown },
            new("pip", HudText("web.player.controls.pictureInPictureItem", "Picture in picture"), "", []) { Icon = "picture-in-picture-2" },
        ];
        PlaybackMenuChoice[] Choices() => AllChoices().Where(choice => choice.Id switch
        {
            "audio" or "chapters" or "mute" => compact,
            "volume-down" or "volume-up" => volumeRows,
            _ => true,
        }).ToArray();
        var choices = Choices();
        var menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, HudText("web.player.controls.more", "More"), choices,
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
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root)) { SelectOnOpen = false, PanelWidth = 288 };
        menu.Localize(() => HudText("web.player.controls.more", "More"), Choices);
        return menu;
    }
}

