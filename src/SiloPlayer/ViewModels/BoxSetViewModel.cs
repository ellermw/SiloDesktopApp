using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
namespace SiloPlayer.ViewModels;

public sealed class BoxSetViewModel(CatalogApi catalog) : ObservableObject
{
    private CancellationTokenSource? _pending;
    private ApiRequestContext _owner;
    private long _language;
    private string? _id, _library;
    private BoxSetDetail? _detail;
    private bool _loading;
    private DetailFailureKind _failure;
    public BoxSetDetail? Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public bool IsLoading { get => _loading; private set => SetProperty(ref _loading, value); }
    public DetailFailureKind Failure { get => _failure; private set => SetProperty(ref _failure, value); }
    public bool IsCurrent => _pending is { IsCancellationRequested: false } && catalog.IsCurrentContext(_owner) && catalog.MetadataLanguageGeneration == _language;
    public void Cancel() { _pending?.Cancel(); _pending?.Dispose(); _pending = null; _id = null; _library = null; Detail = null; IsLoading = false; }
    public Task RefreshAsync() => _id is {} id && _library is {} library ? LoadAsync(id, library) : Task.CompletedTask;
    public async Task LoadAsync(string id, string library)
    {
        Cancel(); _id = id; _library = library;
        var pending = _pending = new(); var ct = pending.Token;
        _owner = catalog.CaptureContext(); _language = catalog.MetadataLanguageGeneration;
        var owner = _owner; var language = _language;
        bool Current() => ReferenceEquals(_pending, pending) && !ct.IsCancellationRequested && catalog.IsCurrentContext(owner) && catalog.MetadataLanguageGeneration == language;
        IsLoading = true; Failure = DetailFailureKind.None;
        try
        {
            if (!int.TryParse(library, out var libraryId) || !await catalog.BoxSetsEnabledAsync(libraryId, ct))
            { if (Current()) Failure = DetailFailureKind.NotFound; return; }
            if (!Current()) return;
            var result = await catalog.GetBoxSetAsync(id, library, ct);
            if (Current()) Detail = result;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (Current()) Failure = DetailFailurePolicy.Classify(ex); }
        finally { if (Current()) IsLoading = false; }
    }
    public async Task PlayAsync(Func<string, int, ApiRequestContext, CancellationToken, Task> play)
    {
        if (!IsCurrent || Detail is not {} detail || detail.PlayTarget is not {} movie || !int.TryParse(detail.LibraryId, out var library)) return;
        await play(movie.ContentId, library, _owner, _pending!.Token);
    }
}
