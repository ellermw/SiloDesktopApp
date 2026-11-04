using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
namespace SiloPlayer.ViewModels;

public sealed class BoxSetViewModel(CatalogApi catalog) : ObservableObject
{
    private CancellationTokenSource? _pending;
    private CancellationTokenSource? _playLifetime;
    private ApiRequestContext _owner;
    private ApiIdentityContext _identity;
    private long _language;
    private string? _id, _library;
    private BoxSetDetail? _detail;
    private bool _loading;
    private DetailFailureKind _failure;
    private sealed record ValidatedRead(CancellationTokenSource Pending, CancellationToken Token,
        ApiRequestContext Owner, long Language, BoxSetDetail Detail);
    private ValidatedRead? _validatedRead;
    public BoxSetDetail? Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public bool IsLoading { get => _loading; private set => SetProperty(ref _loading, value); }
    public DetailFailureKind Failure { get => _failure; private set => SetProperty(ref _failure, value); }
    public bool IsCurrent => _pending is { IsCancellationRequested: false } && catalog.IsCurrentContext(_owner) && catalog.MetadataLanguageGeneration == _language;
    public bool CanPlay => _playLifetime is { IsCancellationRequested: false } && catalog.IsCurrentIdentityContext(_identity)
        && catalog.MetadataLanguageGeneration == _language;
    public void Cancel()
    {
        _pending?.Cancel(); _pending?.Dispose(); _pending = null;
        _playLifetime?.Cancel(); _playLifetime?.Dispose(); _playLifetime = null;
        _id = null; _library = null; _validatedRead = null; Detail = null; IsLoading = false;
    }
    public Task RefreshAsync() => RefreshReadAsync();
    private Task<ValidatedRead?> RefreshReadAsync() => _id is {} id && _library is {} library
        ? ReadAsync(id, library) : Task.FromResult<ValidatedRead?>(null);
    public Task LoadAsync(string id, string library)
    {
        Cancel();
        return ReadAsync(id, library);
    }
    private async Task<ValidatedRead?> ReadAsync(string id, string library)
    {
        // Access revisions replace read ownership, not this viewer's accepted
        // playback lifetime or the last successfully mounted detail.
        if (_playLifetime == null || !catalog.IsCurrentIdentityContext(_identity))
        {
            Cancel();
            _identity = catalog.CaptureIdentityContext();
            _playLifetime = new();
        }
        _owner = catalog.CaptureContext();
        _pending?.Cancel(); _pending?.Dispose();
        _id = id; _library = library;
        var pending = _pending = new(); var ct = pending.Token;
        _language = catalog.MetadataLanguageGeneration;
        var owner = _owner; var language = _language;
        bool Current() => ReferenceEquals(_pending, pending) && !ct.IsCancellationRequested && catalog.IsCurrentContext(owner) && catalog.MetadataLanguageGeneration == language;
        IsLoading = true; Failure = DetailFailureKind.None;
        try
        {
            if (!int.TryParse(library, out var libraryId) || libraryId <= 0)
            { if (Current()) { Detail = null; Failure = DetailFailureKind.NotFound; } return null; }
            var eligibleLibraries = await catalog.GetBoxSetLibraryIdsAsync(ct, throwOnCapabilityFailure: true);
            if (!eligibleLibraries.Contains(libraryId))
            { if (Current()) { Detail = null; Failure = DetailFailureKind.NotFound; } return null; }
            if (!Current()) return null;
            var result = await catalog.GetBoxSetAsync(id, library, ct);
            if (!Current()) return null;
            if (result.Id != id || !int.TryParse(result.LibraryId, out var returnedLibrary)
                || returnedLibrary != libraryId || !eligibleLibraries.Contains(returnedLibrary))
            { Detail = null; Failure = DetailFailureKind.AccessDenied; return null; }
            Detail = result;
            if (!Current()) return null;
            return _validatedRead = new(pending, ct, owner, language, result);
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
        // A retired read still owns its loading flag until a successor takes
        // the request slot. Context retirement must not strand that flag.
        finally { if (ReferenceEquals(_pending, pending)) IsLoading = false; }
        return null;
    }
    private bool IsValidatedReadCurrent(ValidatedRead? read) => read != null && !IsLoading
        && ReferenceEquals(_pending, read.Pending) && !read.Token.IsCancellationRequested
        && catalog.IsCurrentContext(read.Owner) && catalog.MetadataLanguageGeneration == read.Language
        && ReferenceEquals(Detail, read.Detail);
    public async Task PlayAsync(Func<string, int, ApiRequestContext, CancellationToken, Task> play)
    {
        if (!CanPlay)
            throw new InvalidOperationException("This box set is no longer available for playback. Reopen it and try again.");
        var lifetime = _playLifetime!;
        var read = _validatedRead;
        if (!IsValidatedReadCurrent(read) || Failure != DetailFailureKind.None)
        {
            // An access revision retires reads, not this viewer's intent. Re-read
            // under current authority before selecting a movie or entering a room.
            var identity = _identity;
            read = await RefreshReadAsync();
            if (!catalog.IsCurrentIdentityContext(identity) || !CanPlay || !ReferenceEquals(lifetime, _playLifetime))
                throw new InvalidOperationException("This box set is no longer available for playback. Reopen it and try again.");
            if (!IsValidatedReadCurrent(read) || Failure != DetailFailureKind.None)
                throw new InvalidOperationException("Could not refresh this box set for playback. Please try again.");
        }
        if (read?.Detail is not {} detail || detail.PlayTarget is not {} movie || !int.TryParse(detail.LibraryId, out var library))
            throw new InvalidOperationException("This box set is no longer available. Refresh it and try again.");
        // Refreshes (including the final save from the session being replaced) do
        // not own an accepted playback intent. Only visit/authority retirement does.
        await play(movie.ContentId, library, read.Owner, lifetime.Token);
    }
}
