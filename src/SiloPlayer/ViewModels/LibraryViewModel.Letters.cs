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
        try
        {
            var index = await _catalogApi.GetCatalogLettersAsync(Library!.Id, SelectedOrder,
                genre: UseAdvancedRules ? null : SelectedGenre,
                studio: UseAdvancedRules ? null : SelectedStudio,
                contentRating: UseAdvancedRules ? null : SelectedContentRating,
                country: UseAdvancedRules ? null : SelectedCountry,
                resolution: UseAdvancedRules ? null : SelectedResolution,
                audioLanguage: UseAdvancedRules ? null : SelectedAudioLanguage,
                yearMin: UseAdvancedRules ? null : SelectedYearMin,
                yearMax: UseAdvancedRules ? null : SelectedYearMax, type: SelectedType,
                extraRules: BuildExtraRules(), extraRulesMatch: AdvancedRulesMatch,
                queryGroups: BuildAdvancedGroups(), queryGroupsMatch: AdvancedRulesMatch, ct: ct);
            if (ct.IsCancellationRequested || key != LetterQueryKey()) return;
            LetterIndex = index;
            if (index is not null)
            {
                // Index and cards are separate reads: a scan must not hide any
                // larger extent already observed by the page request.
                ApplyExactCatalogTotal(index.Total);
                WindowLoaded?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            if (_letterIndexKey == key) _letterIndexKey = null;
        }
        catch (Exception)
        {
            // This optional navigation aid must never turn a usable catalog
            // into an error or manufacture approximate positions.
            if (_letterIndexKey == key) LetterIndex = null;
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
        if (entry is null) return;
        var key = _letterIndexKey;
        await LoadWindowAsync(entry.Position, PageSize, force: true);
        if (jump == _letterJumpVersion && key == LetterQueryKey() && ReferenceEquals(index, LetterIndex))
            LetterJumpRequested?.Invoke(entry.Position);
    }

    private void ResetLetterIndex()
    {
        _letterJumpVersion++;
        _letterIndexKey = null;
        _letterIndexTask = null;
        LetterIndex = null;
    }

    private void ApplyExactCatalogTotal(int total)
    {
        // Without an independent letter read, retain the catalog's existing
        // exact-total authority (including rejecting an inconsistent tail).
        var floor = LetterIndex is null ? 0 : Math.Max(LetterIndex.Total, WindowEndIndex + 1);
        if (LetterIndex is not null && _pageResponses.Count > 0)
            floor = Math.Max(floor, _pageResponses.Max(page => page.Key * PageSize + page.Value.Items.Count));
        if (_hasExactTotal && LetterIndex is not null) floor = Math.Max(floor, _estimatedTotalItems);
        _estimatedTotalItems = Math.Max(Math.Max(0, total), floor);
        _hasExactTotal = true;
        TotalCount = DisplayTotalCount = _estimatedTotalItems;
        Items.SetCount(TotalCount);
    }
}
