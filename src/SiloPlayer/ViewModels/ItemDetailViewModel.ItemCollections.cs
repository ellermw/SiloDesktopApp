using SiloPlayer.Core.Models.Catalog;
namespace SiloPlayer.ViewModels;

/// <summary>
/// The "Also in" row of a movie or series page (silo-server #4528): the collections that hold the title.
/// The read runs beside the rest of the page, is cancelled when the page is left or moves to another title,
/// and its answer is dropped when the title, library, profile or account changed while it was in flight.
/// Nothing shows while it loads or when the first read fails; a failed refetch keeps the last answer.
/// </summary>
public partial class ItemDetailViewModel
{
    private CancellationTokenSource? _itemCollectionsCts;
    private ItemCollections? _alsoIn;
    private string? _alsoInContentId;
    private int? _alsoInLibrary;
    private SiloPlayer.Core.Api.ApiIdentityContext _alsoInOwner;
    private long _alsoInLanguage;

    /// <summary>The collections holding the shown title; null until a read answers, and for a title that is not a movie or series.</summary>
    public ItemCollections? AlsoIn { get => _alsoIn; private set => SetProperty(ref _alsoIn, value); }

    private bool CanKeepAlsoIn(string contentId) => _alsoIn != null && _alsoInContentId == contentId && _alsoInLibrary == LibraryId
        && _catalogApi.IsCurrentIdentityContext(_alsoInOwner) && _alsoInLanguage == _catalogApi.MetadataLanguageGeneration;

    private void CancelItemCollectionsRead()
    {
        _itemCollectionsCts?.Cancel(); _itemCollectionsCts?.Dispose(); _itemCollectionsCts = null;
    }

    private void RetireItemCollections()
    {
        CancelItemCollectionsRead();
        _alsoInContentId = null; AlsoIn = null;
    }

    public async Task RefreshItemCollectionsAsync()
    {
        CancelItemCollectionsRead();
        if (Item is not { Type: "movie" or "series" } item || string.IsNullOrWhiteSpace(item.ContentId))
        { _alsoInContentId = null; AlsoIn = null; return; }
        if (!CanKeepAlsoIn(item.ContentId)) AlsoIn = null;
        var library = LibraryId; var identity = _catalogApi.CaptureIdentityContext();
        var context = _catalogApi.CaptureContext(); var language = _catalogApi.MetadataLanguageGeneration;
        var pending = _itemCollectionsCts = new(); var ct = pending.Token;
        bool Current() => ReferenceEquals(_itemCollectionsCts, pending) && !ct.IsCancellationRequested && ReferenceEquals(Item, item)
            && LibraryId == library && _catalogApi.IsCurrentIdentityContext(identity)
            && _catalogApi.IsCurrentContext(context) && _catalogApi.MetadataLanguageGeneration == language;
        try
        {
            var answer = await _catalogApi.GetItemCollectionsAsync(item.ContentId, library is > 0 ? library : null, ct);
            if (!Current()) return;
            _alsoInContentId = item.ContentId; _alsoInLibrary = library; _alsoInOwner = identity; _alsoInLanguage = language;
            AlsoIn = answer.Mode == ItemCollectionsRowMode.None ? null : answer;
        }
        catch (OperationCanceledException) { }
        catch
        {
            // A failed read shows no row rather than an error on the title's page. A refetch that fails keeps what the last read answered.
        }
        finally { if (ReferenceEquals(_itemCollectionsCts, pending)) { _itemCollectionsCts = null; pending.Dispose(); } }
    }
}
