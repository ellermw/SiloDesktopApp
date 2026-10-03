using System.Collections.ObjectModel;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

public sealed class RequestBrowseSession
{
    public ObservableCollection<RequestMediaResult> Results { get; } = [];
    public DiscoverBrowseResponse? FirstPage { get; private set; }
    public bool HasMore { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsLoadingMore { get; private set; }
    public Exception? Error { get; private set; }
    public Exception? MoreError { get; private set; }
    public event Action? Changed;
    private Func<int, CancellationToken, Task<(DiscoverBrowseResponse Response, int? Next)>>? _fetch;
    private CancellationTokenSource? _owner;
    private int? _next;
    private long _generation;
    public async Task ResetAsync(Func<int, CancellationToken, Task<(DiscoverBrowseResponse Response, int? Next)>> fetch)
    {
        Cancel(); _fetch = fetch; FirstPage = null; Results.Clear(); _next = 1; HasMore = false;
        Error = null; MoreError = null;
        await ReadAsync(first: true);
    }
    public Task LoadMoreAsync() => HasMore && !IsLoading && !IsLoadingMore ? ReadAsync(first: false) : Task.CompletedTask;
    private async Task ReadAsync(bool first)
    {
        if (_fetch == null || _next is not int page) return;
        var generation = _generation; var owner = new CancellationTokenSource(); _owner = owner;
        if (first) { IsLoading = true; Error = null; } else { IsLoadingMore = true; MoreError = null; }
        Changed?.Invoke();
        try
        {
            var (response, next) = await _fetch(page, owner.Token);
            if (generation != _generation || owner.IsCancellationRequested || _owner != owner) return;
            if (first) FirstPage = response;
            var seen = Results.Select(item => (item.MediaType, item.TmdbId)).ToHashSet();
            foreach (var item in response.Results)
                if (seen.Add((item.MediaType, item.TmdbId))) Results.Add(item);
            _next = next is > 0 && next > page && next <= 500 ? next : null;
            HasMore = _next != null;
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation == _generation && _owner == owner)
            { if (first) Error = ex; else MoreError = ex; }
        }
        finally
        {
            if (generation == _generation && _owner == owner)
            { _owner = null; IsLoading = false; IsLoadingMore = false; Changed?.Invoke(); }
            owner.Dispose();
        }
    }
    public void Cancel()
    {
        ++_generation; Interlocked.Exchange(ref _owner, null)?.Cancel();
        IsLoading = false; IsLoadingMore = false;
    }
}
