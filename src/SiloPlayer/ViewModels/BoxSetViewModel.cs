using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
namespace SiloPlayer.ViewModels;

public sealed class BoxSetViewModel(CatalogApi catalog) : ObservableObject
{
    private CancellationTokenSource? _pending;
    private CancellationTokenSource? _playLifetime;
    private ApiIdentityContext _playOwner;
    private ApiRequestContext _owner;
    private long _language;
    private string? _id, _library;
    private BoxSetDetail? _detail;
    private bool _loading;
    private DetailFailureKind _failure;
    public BoxSetDetail? Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public bool IsLoading { get => _loading; private set => SetProperty(ref _loading, value); }
    public DetailFailureKind Failure { get => _failure; private set => SetProperty(ref _failure, value); }
    public bool IsCurrent => _pending is { IsCancellationRequested: false } && catalog.IsCurrentIdentityContext(_playOwner)
        && catalog.IsCurrentContext(_owner) && catalog.MetadataLanguageGeneration == _language;
    public void Cancel()
    {
        _pending?.Cancel(); _pending?.Dispose(); _pending = null;
        _playLifetime?.Cancel(); _playLifetime?.Dispose(); _playLifetime = null;
        _id = null; _library = null; Detail = null; IsLoading = false;
    }
    public Task RefreshAsync() => _id is {} id && _library is {} library ? ReadAsync(id, library) : Task.CompletedTask;
    public Task LoadAsync(string id, string library)
    {
        Cancel();
        return ReadAsync(id, library);
    }
    private async Task ReadAsync(string id, string library)
    {
        if (_playLifetime == null || !catalog.IsCurrentIdentityContext(_playOwner))
        {
            Cancel();
            _playOwner = catalog.CaptureIdentityContext();
            _playLifetime = new();
        }
        _pending?.Cancel(); _pending?.Dispose();
        // Access revisions retire reads, not this viewer's visit or accepted play.
        _owner = catalog.CaptureContext();
        _id = id; _library = library;
        var pending = _pending = new(); var ct = pending.Token;
        _language = catalog.MetadataLanguageGeneration;
        var owner = _owner; var playOwner = _playOwner; var language = _language;
        bool Current() => ReferenceEquals(_pending, pending) && !ct.IsCancellationRequested && catalog.IsCurrentIdentityContext(playOwner)
            && catalog.IsCurrentContext(owner) && catalog.MetadataLanguageGeneration == language;
        IsLoading = true; Failure = DetailFailureKind.None;
        try
        {
            if (!int.TryParse(library, out var libraryId) || !await catalog.BoxSetsEnabledAsync(libraryId, ct, throwOnCapabilityFailure: true))
            { if (Current()) { Detail = null; Failure = DetailFailureKind.NotFound; } return; }
            if (!Current()) return;
            var result = await catalog.GetBoxSetAsync(id, library, ct);
            if (Current()) Detail = result;
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception ex)
        {
            if (Current())
            {
                var failure = DetailFailurePolicy.Classify(ex);
                // Authoritative removal/access loss retires content; a failed background
                // read leaves the last successful detail mounted and retryable.
                if (failure is DetailFailureKind.NotFound or DetailFailureKind.AccessDenied) Detail = null;
                Failure = failure;
            }
        }
        finally
        {
            // Read ownership, not context validity, determines who may end
            // loading. An abandoned context must still expose a retry.
            if (ReferenceEquals(_pending, pending))
            {
                if (!Current())
                {
                    if (!catalog.IsCurrentIdentityContext(playOwner)) Detail = null;
                    Failure = DetailFailureKind.Transient;
                }
                IsLoading = false;
            }
        }
    }
    public async Task PlayAsync(Func<string, int, ApiRequestContext, CancellationToken, Task> play)
    {
        if (!IsCurrent || Detail is not {} detail || detail.PlayTarget is not {} movie || !int.TryParse(detail.LibraryId, out var library)) return;
        // Refreshes (including the final save from the session being replaced) do
        // not own an accepted playback intent. Only visit/authority retirement does.
        await play(movie.ContentId, library, _owner, _playLifetime!.Token);
    }
}
