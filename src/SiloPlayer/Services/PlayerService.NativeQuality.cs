using Microsoft.Extensions.DependencyInjection;
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
        var options = PlaybackQualityMenuModel.Build(Versions.ToArray(), session, SelectedQualityMenuId,
            RequestedMediaFileId, IsWatchTogetherPlayback, IsQualitySwitchActive || _switchingContent,
            BufferingQualityPlaybackInfo);
        IReadOnlyList<PlaybackMenuChoice> Choices()
        {
            var rows = options.Select(row => new PlaybackMenuChoice(row.Id, row.Title, row.Metadata,
            row.Badges, row.IsCurrent, row.IsEnabled,
            row.Kind == PlaybackQualityMenuOptionKind.Information ? "" : row.DisabledReason.Length > 0
                ? row.DisabledReason : row.IsEnabled ? "" : "A playback change is already in progress.",
            IsPending: row.IsPending)).ToList();
            if (CanPresentNativeSpeed)
                rows.Add(new("speed", "Playback speed", DesiredVideoPlaybackRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×", [],
                    IsPending: VideoPlaybackRatePending));
            return rows;
        }
        bool Current() => owner.IsCurrent() && !IsLoading && !_switchingContent && _nativeMenuGate.IsCurrent(request);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(12, 4, 12, 12) };
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
        menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, CanPresentNativeSpeed ? "Playback settings" : "Quality", initial,
            Current, id =>
            {
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
