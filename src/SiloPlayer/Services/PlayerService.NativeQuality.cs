using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private long _nativeQualitySelection;

    private PlaybackChoiceMenu CreateNativeQualityMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var session = _playbackManager?.CurrentSession
            ?? throw new InvalidOperationException("The playback session has ended.");
        var options = PlaybackQualityMenuModel.Build(Versions.ToArray(), session, SelectedQualityMenuId,
            RequestedMediaFileId, IsWatchTogetherPlayback, IsQualitySwitchActive || _switchingContent,
            BufferingQualityPlaybackInfo);
        var choices = options.Select(row => new PlaybackMenuChoice(row.Id, row.Title, row.Metadata,
            row.Badges, row.IsCurrent, row.IsEnabled,
            row.Kind == PlaybackQualityMenuOptionKind.Information ? "" : row.DisabledReason.Length > 0
                ? row.DisabledReason : row.IsEnabled ? "" : "A playback change is already in progress.",
            IsPending: row.IsPending)).ToArray();
        return new PlaybackChoiceMenu(owner.Video, request.Anchor, "Quality", choices,
            () => owner.IsCurrent() && !IsLoading && !_switchingContent && _nativeMenuGate.IsCurrent(request), id =>
            {
                var option = options.FirstOrDefault(row => row.Id == id && row.IsEnabled);
                if (option != null && owner.IsCurrent() && !IsLoading && !_switchingContent)
                {
                    var selection = Interlocked.Increment(ref _nativeQualitySelection);
                    _ = SelectNativeQualityAsync(owner, option, selection);
                }
            }, finish, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root));
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
