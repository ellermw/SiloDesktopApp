using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public sealed record RecommendationSectionNavigationArgs(string Kind, string? Key, string Title);

public partial class RecommendationSectionViewModel : ObservableObject
{
    private readonly RecommendationsApi _recommendationsApi;
    private CancellationTokenSource? _loadCts;
    private RecommendationSectionNavigationArgs? _currentArgs;

    public RecommendationSectionViewModel(RecommendationsApi recommendationsApi)
    {
        _recommendationsApi = recommendationsApi;
        WeakReferenceMessenger.Default.Register<NotInterestedChange>(this, (_, change) =>
        {
            if (!_recommendationsApi.IsCurrentContext(change.Context) || _currentArgs == null) return;
            if (change.Marked == true)
                for (var index = Items.Count - 1; index >= 0; index--)
                    if (Items[index].ContentId == change.ItemId) Items.RemoveAt(index);
            _ = LoadAsync(_currentArgs);
        });
    }

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty] private string _title = "Recommendations";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public async Task LoadAsync(RecommendationSectionNavigationArgs args, CancellationToken ct = default)
    {
        _currentArgs = args;
        var context = _recommendationsApi.CaptureContext();
        var owner = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = Interlocked.Exchange(ref _loadCts, owner);
        previous?.Cancel();

        IsLoading = Items.Count == 0;
        ErrorMessage = null;
        Title = string.IsNullOrWhiteSpace(args.Title) ? GetFallbackTitle(args.Kind, args.Key) : args.Title;

        try
        {
            var response = await _recommendationsApi.GetSectionAsync(args.Kind, args.Key, owner.Token);
            owner.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _loadCts), owner) || !_recommendationsApi.IsCurrentContext(context)) return;

            if (!string.IsNullOrWhiteSpace(response.Label))
                Title = response.Label;

            MediaItemCollectionReconciler.Apply(Items, response.Items.Where(item => item.UserState?.NotInterested != true).ToArray());
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // Superseded or navigated away.
        }
        catch (Exception ex)
        {
            if (!_recommendationsApi.IsCurrentContext(context) || !ReferenceEquals(Volatile.Read(ref _loadCts), owner)) return;
            System.Diagnostics.Debug.WriteLine($"Recommendation section load failed: {ex}");
            if (Items.Count == 0) ErrorMessage = "Failed to load this section.";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, owner), owner))
                IsLoading = false;
            owner.Dispose();
        }
    }

    public void CancelLoad()
    {
        _currentArgs = null;
        Interlocked.Exchange(ref _loadCts, null)?.Cancel();
        IsLoading = false;
    }

    internal static string GetFallbackTitle(string kind, string? key) => RecommendationPresentation.FallbackTitle(kind, key);
}
