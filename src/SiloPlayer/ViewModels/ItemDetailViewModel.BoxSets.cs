using SiloPlayer.Core.Models.Catalog;
namespace SiloPlayer.ViewModels;
public partial class ItemDetailViewModel
{
    private CancellationTokenSource? _boxSetCts;
    private BoxSetDetail? _boxSet;
    // Survives an in-flight refresh so final playback writes still invalidate this visit.
    private BoxSetDetail? _boxSetMembership;
    private bool _boxSetRetired;
    private long _boxSetLifetime;
    private string? _boxSetContentId;
    private int? _boxSetLibrary;
    private SiloPlayer.Core.Api.ApiRequestContext _boxSetOwner;
    private bool CanPreserveBoxSet(string contentId) => !_boxSetRetired && _boxSetContentId == contentId
        && _boxSetLibrary == LibraryId && _catalogApi.IsCurrentContext(_boxSetOwner);
    private bool IsBoxSetMember(string contentId) => Item is { Type: "movie" } movie
        && (movie.ContentId == contentId || _boxSetMembership?.Items.Any(x => x.ContentId == contentId) == true);
    public BoxSetDetail? BoxSet { get => _boxSet; private set => SetProperty(ref _boxSet, value); }
    private async Task RefreshBoxSetAfterPlaybackAsync(SiloPlayer.Messaging.PlaybackProgressUpdated message)
    {
        var item = Item; var library = LibraryId; var lifetime = _boxSetLifetime; var owner = _catalogApi.CaptureContext();
        try { await message.Persisted; } catch { return; }
        if (Item?.ContentId == item?.ContentId && LibraryId == library && !_boxSetRetired && _boxSetLifetime == lifetime && _catalogApi.IsCurrentContext(owner)) await RefreshBoxSetAsync();
    }
    private void RetireBoxSet()
    {
        _boxSetRetired = true; _boxSetLifetime++; _boxSetContentId = null;
        _boxSetMembership = null; CancelBoxSetRead(); BoxSet = null;
    }
    private void CancelBoxSetRead()
    {
        _boxSetCts?.Cancel(); _boxSetCts?.Dispose(); _boxSetCts = null;
    }
    public async Task RefreshBoxSetAsync()
    {
        CancelBoxSetRead();
        if (_boxSetRetired || Item is not { Type: "movie" } item || LibraryId is <= 0)
        { _boxSetMembership = null; BoxSet = null; return; }
        if (!CanPreserveBoxSet(item.ContentId)) { _boxSetMembership = null; BoxSet = null; }
        var library = LibraryId; var context = _catalogApi.CaptureContext(); var language = _catalogApi.MetadataLanguageGeneration;
        _boxSetContentId = item.ContentId; _boxSetLibrary = library; _boxSetOwner = context;
        var pending = _boxSetCts = new(); var ct = pending.Token;
        bool Current() => ReferenceEquals(_boxSetCts, pending) && !ct.IsCancellationRequested && ReferenceEquals(Item, item)
            && LibraryId == library && _catalogApi.IsCurrentContext(context) && _catalogApi.MetadataLanguageGeneration == language;
        try
        {
            var eligibleLibraries = await _catalogApi.GetBoxSetLibraryIdsAsync(ct, throwOnCapabilityFailure: true);
            if (!Current() || (library.HasValue ? !eligibleLibraries.Contains(library.Value) : eligibleLibraries.Count == 0))
            { if (Current()) { _boxSetMembership = null; BoxSet = null; } return; }
            var response = await _catalogApi.GetItemBoxSetAsync(item.ContentId, library, ct);
            if (Current())
            {
                // Unscoped routes let the server choose membership, but its returned
                // library must have its own flag. Explicit routes also require a match.
                var detail = response.BoxSet;
                if (detail != null && (!int.TryParse(detail.LibraryId, out var returnedLibrary)
                    || !eligibleLibraries.Contains(returnedLibrary) || (library.HasValue && returnedLibrary != library.Value))) detail = null;
                _boxSetMembership = detail;
                BoxSet = detail is { Items.Count: > 0 } ? detail : null;
            }
        }
        catch (OperationCanceledException) { }
        catch (SiloPlayer.Core.Api.ApiException ex) when (ex.StatusCode is 403 or 404)
        { if (Current()) { _boxSetMembership = null; BoxSet = null; } }
        catch { /* Optional refresh failure preserves the last successful row. */ }
    }
}
