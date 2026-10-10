using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private ShufflePlaybackController? _shuffleController;
    private ShufflePlaybackController ShuffleController => _shuffleController ??= new(new ShufflesApi(_apiClient), _apiClient);
    public int? ActiveLibraryId { get; private set; }
    public string? ActiveShuffleId => _shuffleController?.IsActive == true ? _shuffleController.Id : null;
    public bool IsShufflePlayback => ActiveShuffleId != null;
    public Shuffle? ActiveShuffle => IsShufflePlayback ? _shuffleController?.Snapshot : null;
    public MediaItem? ShuffleNext => _shuffleController?.NextFor(ContentId);
    public string? ShuffleScopeLabel => ActiveShuffle?.Scope.Label;
    public event Action? ShuffleChanged;
    public event Action<string>? ShuffleFailed;

    private void ReportShuffleFailure(string message)
    {
        ErrorMessage = message;
        InvokeSubscribersSafely(ShuffleFailed, message, nameof(ShuffleFailed));
    }
    private bool ShuffleAwaitsLaterPart => IsShufflePlayback && PlaybackPartSequence.NextFileId(WatchDetail, ActiveMediaFileId) != null;

    private Task ContinueVideoPartAsync(string contentId, int fileId)
        => PlayAsync(contentId, fromStart: true, fileId: fileId, libraryId: ActiveLibraryId, shuffleId: ActiveShuffleId);

    public async Task StartShuffleAsync(ShuffleScopeRequest scope, CancellationToken ct = default)
    {
        if (IsWatchTogetherPlayback || _closing) return;
        var generation = Volatile.Read(ref _playRequestGeneration);
        var authority = _apiClient.CaptureContext();
        bool Current() => generation == Volatile.Read(ref _playRequestGeneration) && !_closing && _apiClient.IsCurrentContext(authority) && !ct.IsCancellationRequested;
        try
        {
            var shuffle = await ShuffleController.StartAsync(scope, ct);
            if (!Current())
            {
                if (_shuffleController?.Id == shuffle.Id) _shuffleController.Leave();
                return;
            }
            int? libraryId = scope.Kind == "library" && int.TryParse(scope.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var library) ? library : null;
            await PlayAsync(shuffle.Current.ContentId, fromStart: true, libraryId: libraryId, shuffleId: shuffle.Id);
        }
        catch (OperationCanceledException) { }
        catch (ApiException error) when (error.StatusCode == 409) { if (Current()) ReportShuffleFailure("Nothing here can be played."); }
        catch { if (Current()) ReportShuffleFailure("Couldn't start shuffle."); }
    }

    public async Task RefreshShuffleNextAsync()
    {
        if (!IsShufflePlayback) return;
        var shuffleId = ActiveShuffleId;
        try { await ShuffleController.RefreshAsync(); }
        catch (OperationCanceledException) { return; }
        catch { /* A transient read failure keeps the last server pick. */ }
        if (ActiveShuffleId == shuffleId) InvokeSubscribersSafely(ShuffleChanged, nameof(ShuffleChanged));
    }

    public async Task PickAnotherShuffleItemAsync()
    {
        if (!IsShufflePlayback || IsWatchTogetherPlayback) return;
        var shuffleId = ActiveShuffleId; var generation = Volatile.Read(ref _playRequestGeneration);
        bool Current() => ActiveShuffleId == shuffleId && generation == Volatile.Read(ref _playRequestGeneration) && !_closing;
        try { await ShuffleController.PickAnotherAsync(); if (Current()) InvokeSubscribersSafely(ShuffleChanged, nameof(ShuffleChanged)); }
        catch (OperationCanceledException) { }
        catch { if (Current()) ReportShuffleFailure("Couldn't pick another."); }
    }

    public async Task StopShuffleAsync()
    {
        if (!IsShufflePlayback) return;
        var stop = ShuffleController.StopAsync(); // Retire authority before awaiting delete.
        CancelPlayingNext();
        try { await stop; } catch { /* Stop is locally final even if deletion fails. */ }
    }

    private async Task ContinueShuffleAsync()
    {
        if (ContentId == null || !IsShufflePlayback) return;
        var contentId = ContentId; var shuffleId = ActiveShuffleId;
        var generation = Volatile.Read(ref _playRequestGeneration);
        var authority = _apiClient.CaptureContext();
        var libraryId = ActiveLibraryId;
        bool Current() => ContentId == contentId && generation == Volatile.Read(ref _playRequestGeneration)
            && ActiveShuffleId == shuffleId && !_closing && _apiClient.IsCurrentContext(authority);
        var restoreFullscreen = _restoreFullscreenAfterPostRollContinue || State == PlayerState.Fullscreen || _videoWindow?.IsFullscreen == true;
        try
        {
            var shuffle = await ShuffleController.AdvanceAsync(contentId);
            if (shuffle == null || !Current()) return;
            await PlayAsync(shuffle.Current.ContentId, fromStart: true, libraryId: libraryId, shuffleId: shuffle.Id);
            if (restoreFullscreen) RestoreFullscreenAfterPostRollContinue();
        }
        catch (OperationCanceledException) { }
        catch { if (Current()) ReportShuffleFailure("Couldn't continue the shuffle."); }
        finally { _restoreFullscreenAfterPostRollContinue = false; }
    }

    private static string WatchPreparationKey(string contentId, int? libraryId, int? fileId)
        => libraryId == null && fileId == null ? contentId : $"{contentId}\0library:{libraryId}\0file:{fileId}";
}
