using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private readonly SettingsApi _stateSettings = App.Services.GetRequiredService<SettingsApi>();
    private SettingsIdentityContext? _stateIdentity;
    private object? _stateEntryParameter;
    private LibraryPageStateSnapshot? _stateSnapshot;
    private long _stateVisit, _stateEdit, _stateLanguage;
    // The navigation visit whose restored view is applied (read by native fixtures).
    internal long _stateAppliedVisit;
    private string? _retainedSearch;
    private bool _libraryStateApplied;
    // An explicit genre may temporarily replace an unrepresentable outer OR
    // query. This visit must never overwrite that retained settings document.
    private bool _temporaryGenreView;
    private bool _applyingLibraryState, _stateEventsAttached, _stateRefreshQueued, _stateRefreshAgain;
    private DispatcherTimer? _stateSaveTimer;
    private CancellationTokenSource? _stateRestoreCancellation;
    private readonly record struct LibraryStateSave(int Id, string Search, SettingsIdentityContext Owner, long Edit);
    // The shell replaces page instances on catch-up. Pending intent belongs to the
    // injected settings client and stable identity, not to a page/read generation.
    // This queue is accessed only on the UI thread, including save continuations.
    private sealed class LibraryStateSaveQueue
    {
        internal readonly Dictionary<(SettingsIdentityContext Owner, int Id), LibraryStateSave> Pending = new();
        internal readonly HashSet<LibraryStateSave> InFlight = new();
        internal readonly Dictionary<(SettingsIdentityContext Owner, int Id), long> Edits = new();
        internal long NextEdit;
    }
    private static readonly ConditionalWeakTable<SettingsApi, LibraryStateSaveQueue> StateSaveQueues = new();
    private LibraryStateSaveQueue StateSaveQueue => StateSaveQueues.GetValue(_stateSettings, _ => new());
    private Dictionary<(SettingsIdentityContext Owner, int Id), LibraryStateSave> _pendingStateSaves => StateSaveQueue.Pending;
    private HashSet<LibraryStateSave> _stateSavesInFlight => StateSaveQueue.InFlight;
    private long CurrentLibraryStateEdit(SettingsIdentityContext owner, int id)
        => StateSaveQueue.Edits.GetValueOrDefault((owner, id));
    private long AdvanceLibraryStateEdit(SettingsIdentityContext owner, int id)
        => StateSaveQueue.Edits[(owner, id)] = ++StateSaveQueue.NextEdit;
    private bool HasLibraryFilterDraft => _yearDebounceTimer != null || _advancedFilterDebounceTimer != null ||
        NullIfWhiteSpace(ActorBox.Text) != ViewModel.SelectedActor ||
        NullIfWhiteSpace(DirectorBox.Text) != ViewModel.SelectedDirector ||
        NullIfWhiteSpace(WriterBox.Text) != ViewModel.SelectedWriter ||
        NullIfWhiteSpace(ProducerBox.Text) != ViewModel.SelectedProducer ||
        NullIfWhiteSpace(AuthorBox.Text) != ViewModel.SelectedAuthor ||
        NullIfWhiteSpace(NarratorBox.Text) != ViewModel.SelectedNarrator ||
        NullIfWhiteSpace(SeriesBox.Text) != ViewModel.SelectedSeries;
    private bool HasUnsavedLibraryState(int id, SettingsIdentityContext owner)
        => _pendingStateSaves.ContainsKey((owner, id));

    private bool IsLibraryStateVisitCurrent(long visit, SettingsIdentityContext owner, int libraryId)
        => _isNavigated && visit == _stateVisit && _activeLibraryId == libraryId &&
            _stateSettings.IsCurrentSettingsIdentityContext(owner) && _stateLanguage == _stateSettings.LocalizedReads.Generation;

    private async Task<LibraryPageStateSnapshot?> RestoreLibraryStateAsync(int libraryId, long visit, SettingsIdentityContext owner)
    {
        if (_stateRestoreCancellation is not { } cancellation) return null;
        var edit = _stateEdit;
        while (IsLibraryStateVisitCurrent(visit, owner, libraryId) && !cancellation.IsCancellationRequested)
        {
            // Access/cache generations authorize individual reads, not the retained visit.
            var requestOwner = _stateSettings.CaptureSettingsContext();
            if (!IsLibraryStateVisitCurrent(visit, owner, libraryId)) return null;
            try
            {
                var snapshot = await _stateSettings.LibraryPageState.ReadSnapshotAsync(libraryId, cancellation.Token);
                if (!_stateSettings.IsCurrentSettingsContext(requestOwner)) continue;
                return snapshot;
            }
            catch (Exception) when (IsLibraryStateVisitCurrent(visit, owner, libraryId) &&
                !cancellation.IsCancellationRequested && !_stateSettings.IsCurrentSettingsContext(requestOwner))
            { /* Retry with current authorization, never with a different identity. */ }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                if (IsLibraryStateVisitCurrent(visit, owner, libraryId) && edit == _stateEdit)
                    App.Services.GetRequiredService<ToastService>().Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex, $"Could not restore library view: {ex.Message}"));
                return null;
            }
        }
        return null;
    }

    private void AttachLibraryStateEvents()
    {
        _stateRestoreCancellation?.Cancel();
        _stateRestoreCancellation?.Dispose();
        _stateRestoreCancellation = new();
        if (_stateEventsAttached) return;
        _stateEventsAttached = true;
        _stateSettings.LibraryPageState.Invalidated += LibraryState_Invalidated;
        _stateSettings.LocalizedReads.Invalidated += LibraryState_LanguageChanged;
    }

    private LibraryViewState? ParseOwnedLibrarySearch(string? search, long visit, SettingsIdentityContext owner, int libraryId)
    {
        try { return ParseLibrarySearch(search); }
        catch (Exception ex)
        {
            if (IsLibraryStateVisitCurrent(visit, owner, libraryId))
                App.Services.GetRequiredService<ToastService>().Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex, $"Could not restore library view: {ex.Message}"));
            return null;
        }
    }

    private void DetachLibraryStateEvents()
    {
        _stateRestoreCancellation?.Cancel();
        _stateRestoreCancellation?.Dispose();
        _stateRestoreCancellation = null;
        _stateEventsAttached = false;
        _stateRefreshQueued = false;
        _stateRefreshAgain = false;
        _stateSettings.LibraryPageState.Invalidated -= LibraryState_Invalidated;
        _stateSettings.LocalizedReads.Invalidated -= LibraryState_LanguageChanged;
    }

    private void LibraryState_LanguageChanged(long generation)
    {
        // The shell refreshes the mounted page in place. Keep this visit armed;
        // invalidation retires the old document and makes pending reads revalidate.
        _stateLanguage = generation;
        _stateSettings.LibraryPageState.Invalidate();
    }

    private void LibraryState_Invalidated()
    {
        var visit = _stateVisit;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (!_isNavigated || !_libraryStateApplied || visit != _stateVisit || _stateIdentity is not { } owner ||
                !_stateSettings.IsCurrentSettingsIdentityContext(owner) || _activeLibraryId is not { } id) return;
            if (_stateRefreshQueued) { _stateRefreshAgain = true; return; }
            _stateRefreshQueued = true;
            var edit = _stateEdit;
            try
            {
                await FlushLibraryStateSaveAsync();
                var snapshot = await RestoreLibraryStateAsync(id, visit, owner);
                if (snapshot == null || !IsLibraryStateVisitCurrent(visit, owner, id) || edit != _stateEdit) return;
                var previous = _stateSnapshot;
                _stateSnapshot = snapshot;
                // Opt-out changes future activation/save eligibility, never current user intent.
                if (_temporaryGenreView || !snapshot.Value.Remember || snapshot == previous || snapshot.Value.Search == _retainedSearch ||
                    HasLibraryFilterDraft || HasUnsavedLibraryState(id, _stateSettings.CaptureSettingsIdentityContext())) return;
                var state = ParseOwnedLibrarySearch(snapshot.Value.Search, visit, owner, id);
                if (state == null) return;
                ApplyLibraryViewState(state);
                _retainedSearch = FormatLibrarySearch(CaptureLibraryViewState(_currentTab));
                _libraryCatalogLoaded = false;
                _recommendedLoaded = false;
                _collectionsLoaded = false;
                if (_currentTab == "Library")
                {
                    if (_currentAudiobookAxis != "books") await LoadAudiobookGroupsAsync(true);
                    else { await EnsureLibraryCatalogLoadedAsync(); await FillViewportAsync(); }
                }
                else if (_currentTab == "Collections") await LoadCollectionsAsync();
                else await LoadRecommendationsAsync();
            }
            finally
            {
                if (visit == _stateVisit)
                {
                    _stateRefreshQueued = false;
                    if (_stateRefreshAgain) { _stateRefreshAgain = false; LibraryState_Invalidated(); }
                }
            }
        });
    }

    private void ScheduleLibraryStateSave(int id, LibraryViewState state)
    {
        if (!_isNavigated || !_libraryStateApplied || id != _activeLibraryId || _applyingLibraryState || _stateIdentity is not { } owner || !_stateSettings.IsCurrentSettingsIdentityContext(owner)) return;
        var search = FormatLibrarySearch(state);
        if (search == _retainedSearch &&
            (!_pendingStateSaves.TryGetValue((owner, id), out var pending) || pending.Edit == CurrentLibraryStateEdit(owner, id))) return;
        _retainedSearch = search;
        ++_stateEdit;
        var edit = AdvanceLibraryStateEdit(owner, id);
        if (_temporaryGenreView) return;
        if (_stateSnapshot is { Remember: false } ||
            _stateSettings.LibraryPageState.TryGetConfirmed(id, out var confirmed) && !confirmed.Remember) return;
        _pendingStateSaves[(owner, id)] = new(id, search, owner, edit);
        _stateSaveTimer ??= CreateStateSaveTimer();
        _stateSaveTimer.Stop(); _stateSaveTimer.Start();
    }

    private void MarkLibraryStateEdited()
    {
        if (_isNavigated && !_suppressFilterEvents && !_applyingLibraryState)
        {
            ++_stateEdit;
            if (_stateIdentity is { } owner && _activeLibraryId is { } id && _stateSettings.IsCurrentSettingsIdentityContext(owner))
                AdvanceLibraryStateEdit(owner, id);
        }
    }

    private DispatcherTimer CreateStateSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => FlushLibraryStateSave(); return timer;
    }

    private async void FlushLibraryStateSave() => await FlushLibraryStateSaveAsync();

    private async Task FlushLibraryStateSaveAsync()
    {
        _stateSaveTimer?.Stop();
        var pending = _pendingStateSaves.Values.Where(save => !_stateSavesInFlight.Contains(save)).ToArray();
        await Task.WhenAll(pending.Select(WriteLibraryStateSaveAsync));
    }

    private async Task WriteLibraryStateSaveAsync(LibraryStateSave save)
    {
        var key = (save.Owner, save.Id);
        if (!_stateSettings.IsCurrentSettingsIdentityContext(save.Owner)) { _pendingStateSaves.Remove(key); return; }
        _stateSavesInFlight.Add(save);
        try
        {
            // Departure saves intentionally outlive the visit that captured them.
            // Rebind only across read invalidations, never account/profile/device changes.
            while (true)
            {
                if (!_stateSettings.IsCurrentSettingsIdentityContext(save.Owner)) { _pendingStateSaves.Remove(key); return; }
                if (!_pendingStateSaves.TryGetValue(key, out var pending) || pending != save) return;
                var requestOwner = _stateSettings.CaptureSettingsContext();
                if (!_stateSettings.IsCurrentSettingsIdentityContext(save.Owner)) continue;
                try { await _stateSettings.LibraryPageState.WriteAsync(requestOwner, save.Id, save.Search); break; }
                catch (Exception) when (_stateSettings.IsCurrentSettingsIdentityContext(save.Owner) &&
                    !_stateSettings.IsCurrentSettingsContext(requestOwner)) { /* Retry with current authorization. */ }
            }
            if (_pendingStateSaves.TryGetValue(key, out var latest) && latest == save)
                _pendingStateSaves.Remove(key);
            if (_stateIdentity == save.Owner && _activeLibraryId == save.Id &&
                _stateSettings.LibraryPageState.TryGetConfirmed(save.Id, out var confirmed) &&
                (!confirmed.Remember || confirmed.Search == save.Search))
                _stateSnapshot = confirmed;
        }
        catch (OperationCanceledException) when (!_stateSettings.IsCurrentSettingsIdentityContext(save.Owner)) { _pendingStateSaves.Remove(key); }
        catch (Exception ex)
        {
            if (!_stateSettings.IsCurrentSettingsIdentityContext(save.Owner)) { _pendingStateSaves.Remove(key); return; }
            // Leave the latest edit queued for the next activation/departure, even while detached.
            if (_pendingStateSaves.TryGetValue(key, out var latest) && latest == save &&
                save.Edit == CurrentLibraryStateEdit(save.Owner, save.Id))
                App.Services.GetRequiredService<ToastService>().Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex, $"Could not remember library view: {ex.Message}"));
        }
        finally { _stateSavesInFlight.Remove(save); }
    }

    private static string FormatLibrarySearch(LibraryViewState state)
    {
        var parts = new List<string>();
        void Param(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"); }
        Param("tab", state.Tab.ToLowerInvariant()); Param("sort", state.Sort); Param("order", state.Order); Param("match", state.Match);
        Param("type", state.Axis == "author" ? "authors" : state.Axis == "narrator" ? "narrators" : state.Axis != "books" ? state.Axis : state.MediaType);
        var groups = state.QueryGroups;
        if (groups.Count == 0)
        {
            var rules = new List<QueryRule>();
            void Rule(string field, object? value, string op = "is") { if (QueryRuleValues.HasValue(value)) rules.Add(new() { Field = field, Op = op, Value = value }); }
            foreach (var genre in state.Genres.Count > 0 ? state.Genres : [state.Genre ?? ""]) Rule("genre", genre);
            foreach (var (field, value) in new[] { ("type", state.MediaType), ("content_rating", state.ContentRating), ("studio", state.Studio), ("country", state.Country), ("resolution", state.Resolution), ("audio_language", state.AudioLanguage), ("actor", state.Actor), ("director", state.Director), ("writer", state.Writer), ("producer", state.Producer), ("author", state.Author), ("narrator", state.Narrator), ("series", state.Series), ("network", state.Network), ("status", state.MatchStatus) }) Rule(field, value);
            Rule("year", state.YearMin, "gte"); Rule("year", state.YearMax, "lte"); Rule("rating_imdb", state.MinimumRating, "gte");
            Rule("added_at", state.AddedInLast, "in_last"); Rule("release_date", state.ReleasedInLast, "in_last");
            if (state.FourK) Rule("resolution", "2160p"); if (state.Hdr) Rule("hdr", true); if (state.DolbyVision) Rule("dolby_vision", true);
            if (state.WatchStatus == "watched") Rule("watched", true);
            else if (state.WatchStatus == "in_progress") Rule("in_progress", true);
            else if (state.WatchStatus == "unwatched") { Rule("watched", false); Rule("in_progress", false); }
            groups = [new() { Rules = rules }];
            var languages = state.OriginalLanguages.Count > 0 ? state.OriginalLanguages : [state.OriginalLanguage ?? ""];
            var languageRules = languages.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => new QueryRule { Field = "original_language", Op = "is", Value = value }).ToList();
            if (languageRules.Count > 0) groups.Add(new() { Match = "any", Rules = languageRules });
        }
        for (var i = 0; i < groups.Count; i++)
        {
            Param($"groups[{i}][match]", groups[i].Match);
            for (var j = 0; j < groups[i].Rules.Count; j++)
            {
                var rule = groups[i].Rules[j]; var prefix = $"groups[{i}][rules][{j}]";
                Param(prefix + "[field]", rule.Field); Param(prefix + "[op]", rule.Op);
                var json = JsonSerializer.SerializeToElement(rule.Value);
                if (json.ValueKind == JsonValueKind.Array) { var k = 0; foreach (var value in json.EnumerateArray()) Param(prefix + $"[value][{k++}]", QueryRuleValues.Format(value)); }
                else Param(prefix + "[value]", QueryRuleValues.Format(rule.Value));
            }
        }
        return "?" + string.Join("&", parts);
    }

    private static LibraryViewState ParseLibrarySearch(string? search)
    {
        var state = new LibraryViewState(); if (string.IsNullOrWhiteSpace(search)) return state;
        var values = search.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2).GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.Ordinal).ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.Last()[1].Replace('+', ' ')), StringComparer.Ordinal);
        string? Value(string key) => values.GetValueOrDefault(key);
        state.Tab = Value("tab") switch { "library" => "Library", "collections" => "Collections", _ => "Recommended" };
        state.Sort = Value("sort") ?? "title"; state.Order = Value("order") == "desc" ? "desc" : "asc"; state.Match = Value("match") == "any" ? "any" : "all";
        state.Axis = Value("type") switch { "authors" => "author", "narrators" => "narrator", "books" => "books", "series" => "series", _ => "books" };
        state.MediaType = Value("type") is "authors" or "narrators" or "books" ? null : Value("type");
        state.Genre = Value("genre"); state.YearMin = Value("year_min"); state.YearMax = Value("year_max"); state.ContentRating = Value("content_rating");
        var groupIndexes = values.Keys.Select(key => Regex.Match(key, @"^groups\[(\d+)\]")).Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value)).Where(index => index < 128).Distinct().Order();
        foreach (var i in groupIndexes)
        {
            var group = new QueryGroup { Match = Value($"groups[{i}][match]") == "any" ? "any" : "all" };
            var ruleIndexes = values.Keys.Select(key => Regex.Match(key, $@"^groups\[{i}\]\[rules\]\[(\d+)\]\[field\]$")).Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value)).Where(index => index < 512).Distinct().Order();
            foreach (var j in ruleIndexes)
            {
                var prefix = $"groups[{i}][rules][{j}]"; var field = Value(prefix + "[field]")!; var op = Value(prefix + "[op]") ?? "is";
                var indexed = values.Where(pair => pair.Key.StartsWith(prefix + "[value][", StringComparison.Ordinal)).OrderBy(pair => int.Parse(pair.Key[(prefix.Length + 8)..^1])).Select(pair => pair.Value).ToArray();
                try { group.Rules.Add(new() { Field = field, Op = op, Value = QueryRuleValues.Parse(field, op, indexed.Length > 0 ? string.Join(",", indexed) : Value(prefix + "[value]") ?? "") }); }
                catch (FormatException) { group.Rules.Add(new() { Field = field, Op = op, Value = Value(prefix + "[value]") }); }
            }
            state.QueryGroups.Add(group);
        }
        return state;
    }
}
