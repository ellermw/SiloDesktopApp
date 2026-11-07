using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private bool CanPresentNativeSpeed => !IsAudiobook && !HasRemoteAuthority && _videoPlaybackRateOwner?.Invoke() == true;

    private PlaybackChoiceMenu? CreateNativeSpeedMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        if (!CanPresentNativeSpeed) return null;
        bool Current() => owner.IsCurrent() && CanPresentNativeSpeed && _nativeMenuGate.IsCurrent(request);
        IReadOnlyList<PlaybackSpeedMenuOption> Options() => PlaybackSpeedMenuModel.Build(DesiredVideoPlaybackRate,
            CanChangeVideoPlaybackRate, IsWatchTogetherPlayback, VideoPlaybackRatePending);
        IReadOnlyList<PlaybackMenuChoice> Choices() => Options().Select(row => new PlaybackMenuChoice(row.Id,
            row.Title, row.Metadata, [], row.IsCurrent, row.IsEnabled, row.DisabledReason,
            CloseOnInvoke: false, IsPending: row.IsPending)).ToArray();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(12, 4, 12, 12) };
        PlaybackChoiceMenu? menu = null;
        void Refresh()
        {
            if (!Current()) { menu?.Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            error.Text = VideoPlaybackRateError ?? "";
            error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            menu?.UpdateChoices(Choices());
        }
        void Changed()
        {
            if (_nativeMenuDispatcher?.HasThreadAccess == true) Refresh();
            else _nativeMenuDispatcher?.TryEnqueue(Refresh);
        }
        menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, "Playback speed", Choices(), Current,
            id =>
            {
                var option = Options().FirstOrDefault(row => row.Id == id && row.IsEnabled);
                if (Current() && option != null) _ = SetVideoPlaybackRateAsync(option.Rate);
            }, reason => { VideoPlaybackRateChanged -= Changed; finish(reason); },
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root), footer: error);
        VideoPlaybackRateChanged += Changed;
        Refresh();
        return menu;
    }
}
