using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>A captured viewer owns an entire cursor chain. Failed refreshes keep the last useful rows.</summary>
public sealed partial class NotInterestedListState(CatalogApi catalog) : ObservableObject
{
    public ObservableCollection<MediaItem> Items { get; } = [];
    [ObservableProperty] private bool _supported;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _moreError;
    private ApiRequestContext? _context;
    private string? _cursor;
    private long _generation;
    private readonly HashSet<string> _cursors = new(StringComparer.Ordinal);
    private CancellationTokenSource? _load;

    public void Cancel()
    {
        _generation++;
        _load?.Cancel(); _load?.Dispose(); _load = null;
        IsLoading = IsLoadingMore = false;
    }

    public Task LoadAsync() => ReadAsync(false);
    public Task LoadMoreAsync() => ReadAsync(true);

    private async Task ReadAsync(bool more)
    {
        if (more && (IsLoading || IsLoadingMore || !HasMore || string.IsNullOrEmpty(_cursor))) return;
        if (more && (!_context.HasValue || !catalog.IsCurrentContext(_context.Value))) { await ReadAsync(false); return; }
        _load?.Cancel(); _load?.Dispose();
        using var owner = new CancellationTokenSource(); _load = owner;
        var generation = ++_generation;
        var context = catalog.CaptureContext();
        bool Current() => generation == _generation && catalog.IsCurrentContext(context) && !owner.IsCancellationRequested;
        if (_context != context) { Items.Clear(); HasMore = false; Supported = false; _cursor = null; _context = context; }
        if (more) IsLoadingMore = true; else IsLoading = true;
        Error = MoreError = null;
        try
        {
            var supported = await catalog.SupportsNotInterestedAsync(context, owner.Token);
            if (!Current()) return;
            Supported = supported;
            if (!supported) { Items.Clear(); HasMore = false; return; }
            var requestedCursor = more ? _cursor : null;
            var page = await catalog.GetNotInterestedAsync(context, requestedCursor, owner.Token);
            if (!Current()) return;
            if (!more) { Items.Clear(); _cursors.Clear(); }
            var ids = Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in page.Items) if (ids.Add(item.ContentId)) Items.Add(item);
            if (requestedCursor != null) _cursors.Add(requestedCursor);
            _cursor = page.Page?.NextCursor;
            HasMore = page.Page?.HasMore == true && !string.IsNullOrEmpty(_cursor) && !_cursors.Contains(_cursor);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception)
        {
            if (Current())
            {
                // A capability transport failure is retryable, distinct from explicit unsupported.
                Supported = true;
                if (more) MoreError = "Couldn't load more titles.";
                else Error = Items.Count == 0 ? "Couldn't load your Not interested titles." : "Couldn't refresh this list; it may be out of date.";
            }
        }
        finally
        {
            if (generation == _generation) { IsLoading = IsLoadingMore = false; _load = null; }
        }
    }
}
