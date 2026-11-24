using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private long _nativeQualitySelection;

    private PlaybackChoiceMenu? CreateNativeQualityMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var session = _playbackManager?.CurrentSession
            ?? throw new InvalidOperationException("The playback session has ended.");
        var versionsAtOpen = Versions.ToArray(); var selectedAtOpen = SelectedQualityMenuId;
        var requestedAtOpen = RequestedMediaFileId; var roomAtOpen = IsWatchTogetherPlayback;
        var switchingAtOpen = IsQualitySwitchActive || _switchingContent; var limitAtOpen = BufferingQualityPlaybackInfo;
        IReadOnlyList<PlaybackQualityMenuOption> BuildOptions() => PlaybackQualityMenuModel.Build(versionsAtOpen, session,
            selectedAtOpen, requestedAtOpen, roomAtOpen, switchingAtOpen, limitAtOpen);
        var options = BuildOptions();
        // QualityMenu.tsx: the speeds as one segmented row, then "Source file" and
        // "Streaming quality", each under its own label with a hairline between.
        IReadOnlyList<PlaybackMenuChoice> Choices()
        {
            var rows = new List<PlaybackMenuChoice>();
            if (CanPresentNativeSpeed)
            {
                var speeds = PlaybackSpeedMenuModel.Build(DesiredVideoPlaybackRate, CanChangeVideoPlaybackRate, IsWatchTogetherPlayback, VideoPlaybackRatePending);
                rows.Add(new("speed", HudText("web.player.speed.heading", "Speed"), speeds.FirstOrDefault(speed => speed.IsCurrent)?.Title ?? "", [],
                    IsPending: VideoPlaybackRatePending)
                {
                    Segments = speeds.Select(speed => new PlaybackMenuSegment(speed.Id, speed.Title.TrimEnd('×'), speed.Title, speed.IsCurrent, speed.IsEnabled)).ToArray(),
                });
            }
            PlaybackQualityMenuOptionKind? group = null;
            foreach (var row in options)
            {
                var first = group != row.Kind; group = row.Kind;
                if (row.Kind == PlaybackQualityMenuOptionKind.Information)
                {
                    rows.Add(new(row.Id, HudText("web.player.quality.watchPartyVersionLocked",
                        "Watch Party keeps everyone on the same version. You can adjust your streaming quality below."), "", [],
                        IsEnabled: false) { Note = true, Divider = rows.Count > 0 });
                    continue;
                }
                var quality = row.Kind == PlaybackQualityMenuOptionKind.Quality;
                rows.Add(new(row.Id, row.Title, "", row.Badges, row.IsCurrent, row.IsEnabled,
                    row.DisabledReason.Length > 0 ? row.DisabledReason : row.IsEnabled ? "" : "A playback change is already in progress.",
                    IsPending: row.IsPending)
                {
                    Trailing = row.Rate, SingleLine = !quality,
                    Divider = first && rows.Count > 0 && !rows[^1].Note,
                    Section = !first ? "" : quality ? HudText("web.player.quality.streamingQuality", "Streaming quality")
                        : HudText("web.player.quality.sourceFile", "Source file"),
                });
            }
            return rows;
        }
        var openedIdle = !IsLoading && !_switchingContent;
        bool Current() => owner.IsCurrent() && (openedIdle || _sessionHasFramed) &&
            openedIdle == (!IsLoading && !_switchingContent) && _nativeMenuGate.IsCurrent(request);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(16, 4, 16, 6),
            Foreground = PlaybackMenuStyle.Rgb(255, 0xFC, 0xA5, 0xA5) }; // text-red-300, as the web panel's alert
        PlaybackChoiceMenu? menu = null;
        void Refresh()
        {
            if (!Current()) { menu?.Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            error.Text = VideoPlaybackRateError ?? "";
            error.Visibility = CanPresentNativeSpeed && error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // Speed may be the only row; once it retires the settings surface has nothing to offer.
            var choices = Choices();
            if (choices.Count == 0) { menu?.Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            menu?.UpdateChoices(choices);
        }
        void Changed()
        {
            if (_nativeMenuDispatcher?.HasThreadAccess == true) Refresh();
            else _nativeMenuDispatcher?.TryEnqueue(Refresh);
        }
        var initial = Choices();
        if (initial.Count == 0) return null;
        string Heading() => CanPresentNativeSpeed ? HudText("web.player.quality.playbackSettings", "Playback settings") : HudText("web.player.quality.title", "Quality");
        menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, Heading(), initial,
            Current, id =>
            {
                if (id.StartsWith("rate:", StringComparison.Ordinal))
                {
                    // A press on one of the Speed row's segments; the panel stays open.
                    var speed = PlaybackSpeedMenuModel.Build(DesiredVideoPlaybackRate, CanChangeVideoPlaybackRate, IsWatchTogetherPlayback,
                        VideoPlaybackRatePending).FirstOrDefault(row => row.Id == id && row.IsEnabled && !row.IsCurrent);
                    if (speed != null && Current() && CanPresentNativeSpeed) _ = SetVideoPlaybackRateAsync(speed.Rate);
                    return;
                }
                if (id == "speed")
                {
                    if (owner.IsCurrent() && CanPresentNativeSpeed)
                        owner.Mpv.SendScriptMessage("osc-native-speed-handoff", request.OwnerToken,
                            request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return;
                }
                var option = options.FirstOrDefault(row => row.Id == id && row.IsEnabled);
                if (option != null && owner.IsCurrent() && !IsLoading && !_switchingContent)
                {
                    var selection = Interlocked.Increment(ref _nativeQualitySelection);
                    _ = SelectNativeQualityAsync(owner, option, selection);
                }
            }, reason => { VideoPlaybackRateChanged -= Changed; finish(reason); },
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root), footer: error);
        VideoPlaybackRateChanged += Changed;
        // Same rows (ids/selection), re-rendered in the new interface language.
        menu.Localize(Heading, () => { options = BuildOptions(); return Choices(); });
        Refresh();
        return menu;
    }

    private async Task SelectNativeQualityAsync(NativeMenuOwner owner, PlaybackQualityMenuOption option, long selection)
    {
        bool Accept() => owner.IsCurrent() && selection == Volatile.Read(ref _nativeQualitySelection);
        try
        {
            if (!Accept()) return;
            if (option.Kind == PlaybackQualityMenuOptionKind.Version)
            {
                var version = Versions.FirstOrDefault(row => row.FileId == option.FileId);
                if (version == null || version.FileId == ActiveMediaFileId || IsWatchTogetherPlayback) return;
                await SwitchVersionCoreAsync(version, Accept);
                // The service publishes accepted plan/file metadata from its own load.
                // A retired menu never republishes a requested version as delivery.
            }
            else if (option.Kind == PlaybackQualityMenuOptionKind.Quality && option.QualityId is { } quality)
            {
                if (quality == SelectedQualityMenuId) return;
                await SwitchQualityTierCoreAsync(quality, acceptSelection: Accept);
            }
        }
        catch (Exception)
        {
            if (Accept()) ShowNotice("Quality unavailable", "Please open Quality and try again.", "error");
        }
    }
}
