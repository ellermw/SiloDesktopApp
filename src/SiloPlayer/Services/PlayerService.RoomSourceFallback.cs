using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>A room member's playback start the server refused for one specific file.</summary>
public sealed record RoomPlaybackRefusal(string ContentId, int FileId, string Reason, string? PlaybackAttemptId);

public partial class PlayerService
{
    // How long the cover waits for the replacement start before the original refusal shows instead.
    private static readonly TimeSpan RoomSourceReplacementGrace = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Set by the Watch Party coordinator. Returns the pending fallback for a refused room playback start,
    /// or null when none may be sent.
    /// </summary>
    internal Func<RoomPlaybackRefusal, Task<SourceFallbackOutcome>?>? RoomSourceFallback { get; set; }

    /// <summary>
    /// Called where a failed start would show its refusal. When the room takes the refusal over, the cover
    /// stays up with the official wait text and the refusal is held back; returns false when it must show now.
    /// </summary>
    private bool TryBeginRoomSourceFallback(Exception error, bool roomPlayback, string contentId, object loadingOwner, string title, string detail)
    {
        if (!roomPlayback || HasRemoteAuthority || RoomSourceFallback is not { } begin ||
            error is not PlaybackPlanTerminalException terminal || _requestedMediaFileId is not int failedFileId)
            return false;
        Task<SourceFallbackOutcome>? pending;
        try { pending = begin(new RoomPlaybackRefusal(contentId, failedFileId, terminal.Reason, terminal.PlaybackAttemptId)); }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Watch Party source fallback not started: {ex.GetType().Name}"); return false; }
        if (pending == null) return false;
        var generation = Volatile.Read(ref _playRequestGeneration);
        LogToFile("state_trace.txt", $"Watch Party source fallback requested: reason={terminal.Reason} file={failedFileId}");
        App.MainWindowInstance?.ShowLoadingStatus(loadingOwner,
            AppStrings.Get("web.player.watch.findingCompatibleVersion", fallback: "Finding a compatible version for everyone..."));
        _ = FinishRoomSourceFallbackAsync(pending, loadingOwner, generation, terminal.Reason, title, detail);
        return true;
    }

    private async Task FinishRoomSourceFallbackAsync(Task<SourceFallbackOutcome> pending, object loadingOwner,
        long generation, string reason, string title, string detail)
    {
        var outcome = SourceFallbackOutcome.Failed;
        try { outcome = await pending; }
        catch (Exception) { }
        LogToFile("state_trace.txt", $"Watch Party source fallback finished: reason={reason} outcome={outcome}");
        bool Owned() => generation == Volatile.Read(ref _playRequestGeneration);
        // Another start already owns the screen: the replacement source, or anything the viewer chose.
        if (!Owned()) return;
        var window = App.MainWindowInstance;
        switch (outcome)
        {
            case SourceFallbackOutcome.Abandoned:
                window?.HideLoadingOverlay(loadingOwner);
                break;
            case SourceFallbackOutcome.Replaced or SourceFallbackOutcome.Moved:
                // The room's new selection starts through the usual room path. If it never does, do not
                // leave the viewer on the cover.
                await Task.Delay(RoomSourceReplacementGrace);
                if (Owned()) window?.ShowPlaybackError(title, detail);
                break;
            default:
                window?.ShowPlaybackError(title, detail);
                break;
        }
    }
}
