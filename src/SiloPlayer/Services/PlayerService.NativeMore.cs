using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
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
        var audioTrackCount = ActiveVersion?.AudioTracks?.Count ?? 0;
        var audioAvailable = audioTrackCount > 1;
        var chaptersAvailable = (ActiveVersion?.Chapters?.Count ?? 0) > 0;
        var statsShown = IsPlaybackInfoShown;
        // More lists only what its rail does not show (#3993): a compact rail
        // keeps subtitles, settings and fullscreen; a full rail also keeps audio,
        // chapters and mute, plus the volume slider on wide rails.
        var compact = request.Layout == "compact";
        var showVolume = request.Layout != "full" && !owner.Video.UsesTouchInput;
        bool Live() => owner.IsCurrent() && _nativeMenuGate.IsCurrent(request);
        var volumeHeader = showVolume ? CreateVolumeHeader() : default;
        (Grid Header, Action Localize) CreateVolumeHeader()
        {
            var volumeLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var muteButton = new Button { Width = 44, Height = 44, Padding = new Thickness(0) };
            PlaybackMenuStyle.Paint(muteButton, PlaybackMenuStyle.Clear, PlaybackMenuStyle.Hover, PlaybackMenuStyle.Paper);
            var slider = new Slider { Width = 72, Height = 44, Minimum = 0, Maximum = 100, Value = mute ? 0 : volume, SmallChange = 1 };
            AudiobookSliderAppearance.Apply(slider);
            var header = new Grid { Padding = new Thickness(16, 4, 16, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(volumeLabel);
            Grid.SetColumn(muteButton, 1); header.Children.Add(muteButton);
            Grid.SetColumn(slider, 2); header.Children.Add(slider);
            void VolumeLabels()
            {
                volumeLabel.Text = HudText("web.player.controls.volume", "Volume");
                AutomationProperties.SetName(slider, volumeLabel.Text);
                AutomationProperties.SetName(muteButton, mute ? HudText("web.player.volume.unmute", "Unmute") : HudText("web.player.volume.mute", "Mute"));
                muteButton.Content = WebUiIcon.Create(mute ? "volume-x" : "volume-2", 18, PlaybackMenuStyle.Paper);
            }
            bool updatingSlider = false;
            muteButton.Click += (_, _) =>
            {
                if (!Live()) return;
                mute = !owner.Mpv.GetMute();
                owner.Mpv.SetMute(mute);
                IsMuted = mute;
                SaveVolumeState();
                updatingSlider = true;
                try { slider.Value = mute ? 0 : owner.Mpv.GetPropertyDouble("volume"); }
                finally { updatingSlider = false; }
                VolumeLabels();
            };
            slider.ValueChanged += (_, e) =>
            {
                if (updatingSlider || !Live()) return;
                Volume = e.NewValue;
                owner.Mpv.SetVolume(Volume);
                if (mute) { owner.Mpv.SetMute(false); IsMuted = mute = false; VolumeLabels(); }
                SaveVolumeState();
            };
            VolumeLabels();
            return (header, VolumeLabels);
        }
        PlaybackMenuChoice[] AllChoices() =>
        [
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
            "audio" => compact && audioTrackCount > 0,
            "chapters" => compact && chaptersAvailable,
            _ => true,
        }).ToArray();
        var choices = Choices();
        var menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, HudText("web.player.controls.more", "More"), choices,
            Live,
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
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root))
            { SelectOnOpen = false, PanelWidth = 288, ListHeader = volumeHeader.Header };
        menu.Localize(() => { volumeHeader.Localize?.Invoke(); return HudText("web.player.controls.more", "More"); }, Choices);
        return menu;
    }
}

