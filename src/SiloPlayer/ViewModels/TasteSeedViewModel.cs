using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels;

public partial class TasteSeedViewModel(RecommendationsApi recommendationsApi) : ObservableObject
{
    private const int MinimumPicks = 3;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _loadMoreCts;
    private int? _nextOffset;

    public ObservableCollection<TasteSeedItemViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _showEmptyState;
    [ObservableProperty] private bool _hasMore;

    public bool CanSubmit => SelectedCount >= MinimumPicks && !IsSaving;
    public bool HasReachedEnd => HasItems && !HasMore && !IsLoadingMore;
    public string SelectionSummary => SelectedCount == 0
        ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.tasteSeed.minPicks", new Dictionary<string, object?> { ["count"] = MinimumPicks })
        : SiloPlayer.Core.Localization.AppStrings.Get(SelectedCount < MinimumPicks ? "web.home.tasteSeed.selectedCountMore" : "web.home.tasteSeed.selectedCount",
            new Dictionary<string, object?> { ["count"] = SelectedCount, ["remaining"] = MinimumPicks - SelectedCount });

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnHasItemsChanged(bool value) => OnPropertyChanged(nameof(HasReachedEnd));
    partial void OnHasMoreChanged(bool value) => OnPropertyChanged(nameof(HasReachedEnd));
    partial void OnIsLoadingMoreChanged(bool value) => OnPropertyChanged(nameof(HasReachedEnd));

    [RelayCommand]
    public async Task LoadAsync()
    {
        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, ownerCts);
        previous?.Cancel();
        previous?.Dispose();
        CancelLoadMore();
        IsLoading = true;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        SetLocalizedMessage(nameof(StatusMessage), () => null, value => StatusMessage = value ?? "");
        ShowEmptyState = false;
        try
        {
            var page = await recommendationsApi.GetTasteSeedItemsAsync(30, 0, ownerCts.Token);
            ownerCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, ownerCts)) return;
            Items.Clear();
            foreach (var item in page.Items)
                Items.Add(new TasteSeedItemViewModel(item, item.UserState?.IsFavorite == true));
            _nextOffset = page.NextOffset;
            HasMore = _nextOffset.HasValue;
            HasItems = Items.Count > 0;
            ShowEmptyState = Items.Count == 0;
            UpdateSelectedCount();
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, ownerCts))
            {
                SetLocalizedMessage(nameof(ErrorMessage), () => (ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message), value => ErrorMessage = value);
                ShowEmptyState = false;
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, ownerCts), ownerCts))
                IsLoading = false;
            ownerCts.Dispose();
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsLoadingMore || _nextOffset is not int offset) return;
        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadMoreCts, ownerCts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoadingMore = true;
        try
        {
            var page = await recommendationsApi.GetTasteSeedItemsAsync(30, offset, ownerCts.Token);
            ownerCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadMoreCts, ownerCts)) return;
            var known = Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in page.Items)
                if (known.Add(item.ContentId))
                    Items.Add(new TasteSeedItemViewModel(item, item.UserState?.IsFavorite == true));
            _nextOffset = page.NextOffset;
            HasMore = _nextOffset.HasValue;
            HasItems = Items.Count > 0;
            UpdateSelectedCount();
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadMoreCts, ownerCts)) SetLocalizedMessage(nameof(ErrorMessage), () => (ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message), value => ErrorMessage = value);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadMoreCts, null, ownerCts), ownerCts))
                IsLoadingMore = false;
            ownerCts.Dispose();
        }
    }

    public void Toggle(TasteSeedItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
        UpdateSelectedCount();
    }

    public async Task<bool> SubmitAsync()
    {
        if (!CanSubmit) return false;
        var newPicks = Items.Where(item => item.IsSelected && !item.WasFavorite)
            .Select(item => item.ContentId).ToArray();
        if (newPicks.Length == 0) return true;

        IsSaving = true;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        try
        {
            var result = await recommendationsApi.SubmitTasteSeedAsync(newPicks);
            SetLocalizedMessage(nameof(StatusMessage), () => SiloPlayer.Core.Localization.AppStrings.Get("web.home.tasteSeed.added", new Dictionary<string, object?> { ["count"] = result.Added }), value => StatusMessage = value ?? "");
            return true;
        }
        catch (Exception ex)
        {
            SetLocalizedMessage(nameof(ErrorMessage), () => (ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message), value => ErrorMessage = value);
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        CancelLoadMore();
    }

    private void CancelLoadMore()
    {
        var cts = Interlocked.Exchange(ref _loadMoreCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoadingMore = false;
    }

    private void UpdateSelectedCount() => SelectedCount = Items.Count(item => item.IsSelected);
}

public partial class TasteSeedItemViewModel(MediaItem item, bool selected) : ObservableObject
{
    public MediaItem Item { get; } = item;
    public string ContentId => Item.ContentId;
    public string Title => Item.Title;
    public bool WasFavorite { get; } = item.UserState?.IsFavorite == true;
    [ObservableProperty] private bool _isSelected = selected;
    public string AccessibleName => SiloPlayer.Core.Localization.AppStrings.Get(IsSelected ? "web.home.tasteSeed.deselectTitle" : "web.home.tasteSeed.selectTitle", new Dictionary<string, object?> { ["title"] = Title });
    internal void RefreshLocalization() => OnPropertyChanged(nameof(AccessibleName));

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(AccessibleName));
}
