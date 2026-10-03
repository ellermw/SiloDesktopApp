using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Messaging;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class WatchlistViewModel : ObservableObject, IRecipient<MediaSurfaceChanged>
{
    private readonly CatalogApi _catalogApi;
    private readonly RequestsApi _requestsApi;
    private bool _loadInProgress;
    private int _offset;
    private const int PageSize = 50;

    public WatchlistViewModel(CatalogApi catalogApi, RequestsApi requestsApi)
    {
        _catalogApi = catalogApi;
        _requestsApi = requestsApi;
        // F4: react to watchlist add/remove events from anywhere in the app.
        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<MediaItem> Items { get; } = [];
    public ObservableCollection<WatchlistTitle> ExternalTitles { get; } = [];
    public bool ExternalTitlesSupported { get; private set; }
    public bool WatchlistRequests { get; private set; }
    public string? ExternalTitlesError { get; private set; }
    private CancellationTokenSource? _externalLoad;

    public void InvalidateExternalTitles() => ExternalTitles.Clear();

    public async Task LoadExternalTitlesAsync()
    {
        var load = new CancellationTokenSource();
        Interlocked.Exchange(ref _externalLoad, load)?.Cancel();
        try
        {
            ExternalTitlesError = null;
            var status = await _requestsApi.GetStatusAsync(load.Token);
            if (_externalLoad != load || load.IsCancellationRequested) return;
            ExternalTitlesSupported = status.RequestsEnabled && status.WatchlistTitlesSupported;
            WatchlistRequests = status.WatchlistRequests;
            var titles = ExternalTitlesSupported ? await _requestsApi.GetWatchlistTitlesAsync(load.Token) : [];
            if (_externalLoad != load || load.IsCancellationRequested) return;
            ExternalTitles.Clear();
            foreach (var title in titles.OrderBy(t => RequestViewerPolicy.WatchlistPresentation(t).Rank)
                         .ThenBy(t => DateOnly.TryParseExact(t.ReleaseDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.None, out var day) ? day : DateOnly.MaxValue)
                         .ThenByDescending(t => DateTimeOffset.TryParse(t.AddedAt, out var added) ? added : DateTimeOffset.MinValue))
                ExternalTitles.Add(title);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_externalLoad == load) ExternalTitlesError = $"Couldn't load titles outside your library: {ex.Message}"; }
        finally { Interlocked.CompareExchange(ref _externalLoad, null, load); load.Dispose(); }
    }

    public void CancelExternalLoad() => Interlocked.Exchange(ref _externalLoad, null)?.Cancel();

    public async Task RemoveExternalTitleAsync(WatchlistTitle title)
    {
        await _requestsApi.RemoveWatchlistTitleAsync(title.MediaType, title.TmdbId);
        ExternalTitles.Remove(title);
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private bool _isLoadingMore;

    public void Receive(MediaSurfaceChanged message)
    {
        switch (message.Kind)
        {
            case MediaSurfaceChangeKind.WatchlistRemoved:
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (Items[i].ContentId == message.ContentId)
                    {
                        Items.RemoveAt(i);
                        _offset = Math.Max(0, _offset - 1);
                        break;
                    }
                }
                break;
            case MediaSurfaceChangeKind.WatchlistAdded:
                // Item added elsewhere — need a refresh since the messenger
                // event doesn't carry the full MediaItem payload.
                if (!Items.Any(x => x.ContentId == message.ContentId))
                    _ = LoadAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loadInProgress) return;

        _offset = 0;
        HasMore = false;
        await LoadPageAsync(replace: true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || _loadInProgress) return;
        await LoadPageAsync(replace: false);
    }

    private async Task LoadPageAsync(bool replace)
    {
        _loadInProgress = true;
        IsLoading = replace && Items.Count == 0;
        IsLoadingMore = !replace;
        ErrorMessage = null;

        try
        {
            var response = await _catalogApi.GetWatchlistAsync(PageSize, _offset);
            if (replace)
                Items.Clear();

            var existing = Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in response.Items)
            {
                if (existing.Add(item.ContentId))
                    Items.Add(item);
            }

            // The server computes has_more before hidden-series filtering, so
            // the transport offset advances by the requested raw page size.
            _offset += PageSize;
            HasMore = response.HasMore;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load watchlist: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
            _loadInProgress = false;
        }
    }
}
