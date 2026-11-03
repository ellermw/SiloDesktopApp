using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

/// <summary>Optional search section, owned independently from the ordinary catalog window.</summary>
public sealed class BoxSetSearchState(CatalogApi catalog) : ObservableObject
{
    private sealed record Scope(string Query, int? Library, ApiRequestContext Context, long Language);
    private sealed record Cached(Scope Scope, List<BoxSetSummary> Items, DateTimeOffset At);
    private CancellationTokenSource? _pending;
    private Scope? _scope;
    private Cached? _cache;
    private Func<bool>? _isCurrent;
    private bool _isLoading;
    private bool _policyValidated;
    private readonly ResultCollection _items = [];
    public ObservableCollection<BoxSetSummary> Items => _items;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public long Generation { get; private set; }

    public bool CanOpen(BoxSetSummary set, long generation) => generation == Generation && _policyValidated && IsCurrentScope() && Items.Contains(set);

    private bool IsCurrentScope() => _scope is { } scope && catalog.IsCurrentContext(scope.Context)
        && catalog.MetadataLanguageGeneration == scope.Language && (_isCurrent?.Invoke() ?? true);

    public void Retire()
    {
        Generation++;
        CancelPending();
        _scope = null; _isCurrent = null; _policyValidated = false; Replace([]); IsLoading = false;
    }

    private void CancelPending()
    {
        var pending = _pending; _pending = null; pending?.Cancel(); pending?.Dispose();
    }

    public async Task SearchAsync(string query, int? libraryId = null, bool enabled = true, Func<bool>? isCurrent = null, bool refresh = false)
    {
        query = query.Trim();
        var scope = new Scope(query, libraryId, catalog.CaptureContext(), catalog.MetadataLanguageGeneration);
        // Keep the current keys present throughout a same-scope refresh. The
        // selection reducer can then follow newer input, without saving and
        // restoring an obsolete selection after the request completes.
        if (enabled && query.Length > 0 && _scope == scope && IsCurrentScope())
        {
            Generation++; CancelPending(); _policyValidated = false;
        }
        else Retire();
        if (!enabled || query.Length == 0) return;
        _scope = scope;
        _isCurrent = isCurrent;
        var owner = _pending = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var ct = owner.Token;
        bool Current() => ReferenceEquals(_pending, owner) && !ct.IsCancellationRequested && IsCurrentScope();
        IsLoading = true;
        try
        {
            // Recheck viewer policy even for fresh cache data. The server owns
            // box_sets_in_search; that admin flag is absent from viewer libraries.
            var libraries = await catalog.GetSearchBoxSetLibraryIdsAsync(libraryId, ct).WaitAsync(ct);
            if (!Current()) return;
            if (libraries.Count == 0) { _cache = null; Replace([]); return; }
            _policyValidated = true;
            var cached = _cache?.Scope == scope ? _cache : null;
            if (cached != null)
            {
                Replace(cached.Items.Where(set => libraries.Contains(set.LibraryId)));
                if (!refresh && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(60)) return;
            }
            var response = await catalog.SearchBoxSetsAsync(query, libraryId, 5, ct).WaitAsync(ct);
            if (!Current()) return;
            // Keep the server's ranking. Inaccessible/disabled library summaries
            // cannot create an aggregate destination even from a cached response.
            var items = response.Items.Where(set => libraries.Contains(set.LibraryId)
                && !string.IsNullOrWhiteSpace(set.Id)).ToList();
            _cache = new(scope, items, DateTimeOffset.UtcNow);
            Replace(items);
        }
        catch (OperationCanceledException) { }
        catch { /* Optional discovery has no retries or primary-search error. */ }
        finally
        {
            if (ReferenceEquals(_pending, owner))
            {
                if (!IsCurrentScope() || !_policyValidated) Replace([]);
                _pending = null; IsLoading = false;
            }
            owner.Dispose();
        }
    }

    private void Replace(IEnumerable<BoxSetSummary> items)
    {
        _items.Replace(items);
    }

    private sealed class ResultCollection : ObservableCollection<BoxSetSummary>
    {
        public void Replace(IEnumerable<BoxSetSummary> items)
        {
            var snapshot = items.ToArray();
            CheckReentrancy();
            Items.Clear(); foreach (var set in snapshot) Items.Add(set);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
