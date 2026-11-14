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
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(12, 4, 12, 12),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,189,186,179)) };
        PlaybackChoiceMenu? menu = null;
        void Refresh()
        {
            if (!Current()) { menu?.Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            error.Text = VideoPlaybackRateError ?? (IsWatchTogetherPlayback ? "Watch Party controls playback speed." : "");
            error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            menu?.UpdateChoices(Choices());
        }
        void Changed()
        {
            if (_nativeMenuDispatcher?.HasThreadAccess == true) Refresh();
            else _nativeMenuDispatcher?.TryEnqueue(Refresh);
        }
        menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, HudText("web.player.speed.heading", "Speed"), Choices(), Current,
            id =>
            {
                var option = Options().FirstOrDefault(row => row.Id == id && row.IsEnabled);
                if (Current() && option != null) _ = SetVideoPlaybackRateAsync(option.Rate);
            }, reason => { VideoPlaybackRateChanged -= Changed; finish(reason); },
            root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root), footer: error, allowDisabledTraversal: true, segmented: true);
        VideoPlaybackRateChanged += Changed;
        menu.Localize(() => HudText("web.player.speed.heading", "Speed"), () => { Refresh(); return Choices(); });
        Refresh();
        return menu;
    }
}
