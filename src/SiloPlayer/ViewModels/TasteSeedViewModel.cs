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
    public string SelectionSummary => SelectedCount == 0
        ? $"Select at least {MinimumPicks} titles to personalize your recommendations"
        : $"{SelectedCount} selected{(SelectedCount < MinimumPicks ? $" — {MinimumPicks - SelectedCount} more to continue" : "")}";

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    [RelayCommand]
    public async Task LoadAsync()
    {
        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, ownerCts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
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
                ErrorMessage = ex.Message;
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
        IsLoadingMore = true;
        try
        {
            var page = await recommendationsApi.GetTasteSeedItemsAsync(30, offset);
            var known = Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in page.Items)
                if (known.Add(item.ContentId))
                    Items.Add(new TasteSeedItemViewModel(item, item.UserState?.IsFavorite == true));
            _nextOffset = page.NextOffset;
            HasMore = _nextOffset.HasValue;
            HasItems = Items.Count > 0;
            UpdateSelectedCount();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
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
        ErrorMessage = null;
        try
        {
            var result = await recommendationsApi.SubmitTasteSeedAsync(newPicks);
            StatusMessage = result.Added == 1
                ? "Added 1 favorite — personalizing your home"
                : $"Added {result.Added} favorites — personalizing your home";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
}
