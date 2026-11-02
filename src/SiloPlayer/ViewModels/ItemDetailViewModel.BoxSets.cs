using SiloPlayer.Core.Models.Catalog;
namespace SiloPlayer.ViewModels;
public partial class ItemDetailViewModel
{
    private CancellationTokenSource? _boxSetCts;
    private BoxSetDetail? _boxSet;
    private bool _boxSetRetired;
    private long _boxSetLifetime;
    public BoxSetDetail? BoxSet { get => _boxSet; private set => SetProperty(ref _boxSet, value); }
    private async Task RefreshBoxSetAfterPlaybackAsync(SiloPlayer.Messaging.PlaybackProgressUpdated message)
    {
        var item = Item; var library = LibraryId; var lifetime = _boxSetLifetime; var owner = _catalogApi.CaptureContext();
        try { await message.Persisted; } catch { return; }
        if (ReferenceEquals(Item, item) && LibraryId == library && !_boxSetRetired && _boxSetLifetime == lifetime && _catalogApi.IsCurrentContext(owner)) await RefreshBoxSetAsync();
    }
    private void RetireBoxSet() { _boxSetRetired = true; _boxSetLifetime++; CancelBoxSet(); }
    private void CancelBoxSet()
    {
        _boxSetCts?.Cancel(); _boxSetCts?.Dispose(); _boxSetCts = null; BoxSet = null;
    }
    public async Task RefreshBoxSetAsync()
    {
        CancelBoxSet();
        if (_boxSetRetired || Item is not { Type: "movie" } item) return;
        var library = LibraryId; var context = _catalogApi.CaptureContext(); var language = _catalogApi.MetadataLanguageGeneration;
        var pending = _boxSetCts = new(); var ct = pending.Token;
        bool Current() => ReferenceEquals(_boxSetCts, pending) && !ct.IsCancellationRequested && ReferenceEquals(Item, item)
            && LibraryId == library && _catalogApi.IsCurrentContext(context) && _catalogApi.MetadataLanguageGeneration == language;
        try
        {
            if (!await _catalogApi.BoxSetsEnabledAsync(library, ct) || !Current()) return;
            var response = await _catalogApi.GetItemBoxSetAsync(item.ContentId, library, ct);
            if (Current() && response.BoxSet is { Items.Count: > 0 } detail) BoxSet = detail;
        }
        catch (OperationCanceledException) { }
        catch { /* Membership is optional. Ordinary detail remains usable. */ }
    }
}
