using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels;

public partial class LibraryViewModel
{
    [ObservableProperty]
    private CatalogLettersResponse? _letterIndex;
    private string? _letterIndexKey;
    private Task? _letterIndexTask;
    private long _letterJumpVersion;
    public event Action<int>? LetterJumpRequested;

    private string LetterQueryKey() => JsonSerializer.Serialize(new
    {
        _queryVersion, context = _catalogApi.CaptureContext(), language = _catalogApi.FilterCacheGeneration,
        library = Library?.Id, SelectedSort, SelectedOrder, SelectedType, UseAdvancedRules,
        SelectedGenre, SelectedStudio, SelectedContentRating, SelectedCountry, SelectedResolution,
        SelectedAudioLanguage, SelectedYearMin, SelectedYearMax,
        rules = BuildExtraRules(), groups = BuildAdvancedGroups(), AdvancedRulesMatch,
    });

    public Task EnsureLetterIndexAsync()
    {
        if (Library is null || SelectedSort != "title")
        {
            ResetLetterIndex();
            return Task.CompletedTask;
        }
        var key = LetterQueryKey();
        if (_letterIndexKey == key) return _letterIndexTask ?? Task.CompletedTask;
        LetterIndex = null;
        _letterIndexKey = key;
        return _letterIndexTask = LoadLetterIndexAsync(key, _catalogQueryCts.Token);
    }

    private async Task LoadLetterIndexAsync(string key, CancellationToken ct)
    {
        var libraryId = Library!.Id;
        var order = SelectedOrder;
        var type = SelectedType;
        var groups = BuildAdvancedGroups();
        var queryFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        try
        {
            var index = await _catalogApi.GetCatalogLettersAsync(libraryId, order,
                genre: UseAdvancedRules ? null : SelectedGenre,
                studio: UseAdvancedRules ? null : SelectedStudio,
                contentRating: UseAdvancedRules ? null : SelectedContentRating,
                country: UseAdvancedRules ? null : SelectedCountry,
                resolution: UseAdvancedRules ? null : SelectedResolution,
                audioLanguage: UseAdvancedRules ? null : SelectedAudioLanguage,
                yearMin: UseAdvancedRules ? null : SelectedYearMin,
                yearMax: UseAdvancedRules ? null : SelectedYearMax, type: type,
                extraRules: BuildExtraRules(), extraRulesMatch: AdvancedRulesMatch,
                queryGroups: groups, queryGroupsMatch: AdvancedRulesMatch, ct: ct);
            if (ct.IsCancellationRequested || key != LetterQueryKey()) return;
            LetterIndex = index;
            if (index is not null)
            {
                // The index supplies a provisional extent, never authority
                // over an exact total returned by the catalog itself.
                ApplyLetterIndexTotal(index.Total);
                WindowLoaded?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            if (_letterIndexKey == key) _letterIndexKey = null;
        }
        catch (Exception ex)
        {
            SiloPlayer.Core.Services.LocalLog.AppendException("catalog_errors.txt", ex,
                $"catalog_letters library={libraryId} order={order} type={type ?? "all"} group_count={groups?.Count ?? 0} query_fingerprint={queryFingerprint}");
            // Keep browsing usable. Only explicit query/return/jump requests
            // retry this optional aid; publishing card windows must not retry it.
            if (_letterIndexKey == key)
            {
                _letterIndexKey = null;
                _letterIndexTask = null;
                LetterIndex = null;
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task JumpToLetterAsync(string letter)
    {
        var jump = ++_letterJumpVersion;
        if (string.IsNullOrEmpty(letter) || SelectedSort != "title") return;
        await EnsureLetterIndexAsync();
        var index = LetterIndex;
        if (jump != _letterJumpVersion || index is null || _letterIndexKey != LetterQueryKey()) return;
        var entry = index.Letters.FirstOrDefault(x => x.Letter == letter.ToUpperInvariant());
        if (entry is null || entry.Position >= TotalCount) return;
        var key = _letterIndexKey;
        var previousStart = WindowStartIndex;
        var previousItems = _windowItems;
        var previousWindows = _recentWindows.ToArray();
        var previousHasMore = HasMore;
        var load = LoadWindowAsync(entry.Position, PageSize, force: true);
        var viewportVersion = _viewportRequestVersion;
        await load;
        if (jump != _letterJumpVersion || key != LetterQueryKey() || !ReferenceEquals(index, LetterIndex)
            || viewportVersion != _viewportRequestVersion) return;
        if (GetWindowItem(entry.Position) is null)
        {
            // Index and page reads are not a snapshot. A missing target must
            // not strand the unchanged viewport on an empty/distant window.
            _windowItems = previousItems;
            WindowStartIndex = previousStart;
            _recentWindows.Clear();
            foreach (var window in previousWindows) _recentWindows.AddLast(window);
            HasMore = previousHasMore;
            WindowLoaded?.Invoke();
            return;
        }
        if (entry.Position < TotalCount)
            LetterJumpRequested?.Invoke(entry.Position);
    }

    private void ResetLetterIndex()
    {
        _letterJumpVersion++;
        _letterIndexKey = null;
        _letterIndexTask = null;
        LetterIndex = null;
    }

    private void ApplyLetterIndexTotal(int total)
    {
        if (_hasExactTotal) return;
        // Do not carry a no-total runway estimate into the indexed extent.
        // A non-exact terminal page may be older than the index. Retain both
        // the index extent and any larger extent established by real cards.
        _estimatedTotalItems = Math.Max(total, Math.Max(_catalogEndTotal ?? 0, _observedCatalogEnd));
        TotalCount = _estimatedTotalItems;
        Items.SetCount(TotalCount);
        // DisplayTotalCount remains unknown until the catalog supplies a count.
    }

    private void ApplyExactCatalogTotal(int total)
    {
        // Catalog counts are authoritative even if an older index or estimate
        // was larger (for example after items are removed during a scan).
        _estimatedTotalItems = Math.Max(0, total);
        _hasExactTotal = true;
        TotalCount = DisplayTotalCount = _estimatedTotalItems;
        Items.SetCount(TotalCount);
    }
}
