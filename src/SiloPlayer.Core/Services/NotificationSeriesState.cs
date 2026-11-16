using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

public sealed class NotificationSeriesState(NotificationsApi api) : ObservableObject
{
    private CancellationTokenSource? _query, _lifetime, _selection;
    private long _generation, _selectionRun;
    private string? _cursor;
    private bool _more;
    private readonly HashSet<string> _cursors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    public ObservableCollection<NotificationSeries> Rows { get; } = [];
    public ObservableCollection<NotificationSeries> Active => Rows;
    public bool ShowInactive => !HasMore;
    public IReadOnlySet<string> SelectedIds => _selected;
    public ApiRequestContext? Context { get; private set; }
    public NotificationSeriesOptions Options { get; private set; } = new();
    public NotificationPreferences? Preferences { get; private set; }
    public bool Available { get; private set; }
    public bool CanMute { get; private set; }
    public bool CanBulkMute { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsLoadingMore { get; private set; }
    public bool IsSelectingAll { get; private set; }
    public bool IsBulkMutating { get; private set; }
    public string? Error { get; private set; }
    public string? MutationError { get; private set; }
    public bool HasMore => _more;
    public bool IsEmpty => !IsLoading && !HasMore && Rows.Count == 0 && Error == null;
    public bool SelectionLocked => IsLoading || IsSelectingAll;
    public bool BulkLocked => SelectionLocked || IsBulkMutating;
    public bool IsCurrent(ApiRequestContext context) => _lifetime is { IsCancellationRequested: false } && Context == context && api.IsCurrentContext(context);
    public bool IsPending(string id) => _pending.Contains(id);
    private void Notify() => OnPropertyChanged(string.Empty);

    public async Task OpenAsync()
    {
        Close();
        var previous = Context; Context = api.RequestContext;
        _lifetime = new(); var ct = _lifetime.Token; var context = Context.Value;
        if (previous != context) { Options = new(); Rows.Clear(); }
        Available = CanMute = CanBulkMute = false; Error = MutationError = null; IsLoading = true; Notify();
        try
        {
            var capability = await api.GetCapabilityAsync(context, ct);
            if (!IsCurrent(context)) return;
            Available = capability.Series?.Available == true;
            CanMute = Available && capability.Series?.Mute == true;
            CanBulkMute = Available && capability.Series?.BulkMute == true;
            if (!Available) return;
            if (Options.Status == "muted" && !CanBulkMute) Options = Options with { Status = "all" };
            try { Preferences = await api.GetPreferencesAsync(context, ct); }
            catch (OperationCanceledException) { throw; }
            catch { Preferences = null; }
            if (IsCurrent(context)) await ReloadAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !IsCurrent(context)) { }
        catch { if (!ct.IsCancellationRequested && IsCurrent(context)) Error = Text("loadFailed", "Could not load the series that notify you. Retry."); }
        finally { if (!ct.IsCancellationRequested && IsCurrent(context)) { IsLoading = false; Notify(); } }
    }

    private bool _moreFailed;
    public Task RetryAsync() => !Available ? OpenAsync() : _moreFailed ? LoadMoreAsync() : ReloadAsync();

    public async Task SetOptionsAsync(NotificationSeriesOptions options)
    {
        options = options with { Q = options.Q.Trim(), Status = options.Status == "muted" && CanBulkMute ? "muted" : "all" };
        if (Options == options) return;
        ClearSelection(); Options = options; Rows.Clear();
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (!Available || Context is not { } context || !IsCurrent(context)) return;
        if (IsSelectingAll) ClearSelection();
        _query?.Cancel(); _query?.Dispose(); _query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime!.Token);
        var ct = _query.Token; var generation = ++_generation; var options = Options;
        _cursor = null; _more = false; _cursors.Clear(); _moreFailed = false;
        IsLoading = true; IsLoadingMore = false; Error = null; Notify();
        try
        {
            var page = await api.GetSeriesAsync(context, options.Status, options, ct: ct);
            if (!Current()) return;
            Apply(page, true);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch { if (Current()) Error = Text("loadFailed", "Could not load the series that notify you. Retry."); }
        finally { if (Current()) { IsLoading = false; Notify(); } }
        bool Current() => !ct.IsCancellationRequested && generation == _generation && IsCurrent(context);
    }

    public async Task LoadMoreAsync(bool automatic = false, CancellationToken selectionToken = default)
    {
        if (automatic && (Error != null || IsSelectingAll)) return;
        if (IsLoading || IsLoadingMore || !HasMore || Context is not { } context || !IsCurrent(context)) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_query!.Token, selectionToken);
        var ct = request.Token; var generation = _generation; var options = Options;
        IsLoadingMore = true; Error = null; _moreFailed = false; Notify();
        try
        {
            var page = await api.GetSeriesAsync(context, options.Status, options, _cursor, ct);
            if (Current()) Apply(page, false);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch { if (Current()) { Error = Text("loadMoreFailed", "Could not load more series. Retry."); _moreFailed = true; } }
        finally { if (generation == _generation && IsCurrent(context)) { IsLoadingMore = false; Notify(); } }
        bool Current() => !ct.IsCancellationRequested && generation == _generation && IsCurrent(context);
    }

    private void Apply(NotificationSeriesPage page, bool reset)
    {
        var next = page.Page!.HasMore == true ? page.Page.NextCursor : null;
        if (next != null && !_cursors.Add(next)) throw new InvalidDataException("Series cursor repeated. Reload notifications.");
        if (reset) Rows.Clear();
        foreach (var row in page.Items!) if (!Rows.Any(existing => existing.Item.ContentId == row.Item.ContentId)) Rows.Add(row);
        _cursor = next; _more = next != null;
    }

    public void ToggleSelected(string id)
    {
        if (!CanBulkMute || SelectionLocked || !Rows.Any(row => row.Item.ContentId == id)) return;
        if (!_selected.Remove(id)) _selected.Add(id);
        Notify();
    }

    public void ToggleLoadedSelection()
    {
        if (!CanBulkMute || SelectionLocked) return;
        if (Rows.Count > 0 && Rows.All(row => _selected.Contains(row.Item.ContentId))) ClearSelection();
        else { foreach (var row in Rows) _selected.Add(row.Item.ContentId); Notify(); }
    }

    public void ClearSelection()
    {
        ++_selectionRun; _selection?.Cancel(); _selection?.Dispose(); _selection = null;
        _selected.Clear(); IsSelectingAll = false; Notify();
    }

    public async Task SelectAllMatchingAsync()
    {
        if (!CanBulkMute || BulkLocked || IsLoadingMore || Error != null || Context is not { } context || !IsCurrent(context)) return;
        _selection?.Cancel(); _selection?.Dispose();
        var run = ++_selectionRun; var generation = _generation;
        _selection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime!.Token);
        var ct = _selection.Token; IsSelectingAll = true; Notify();
        try
        {
            while (HasMore && Current())
            {
                await LoadMoreAsync(selectionToken: ct);
                if (Error != null) return;
            }
            if (Current()) foreach (var row in Rows) _selected.Add(row.Item.ContentId);
        }
        finally { if (run == _selectionRun) { IsSelectingAll = false; Notify(); } }
        bool Current() => !ct.IsCancellationRequested && run == _selectionRun && generation == _generation && IsCurrent(context);
    }

    public async Task<bool> SetMutedAsync(NotificationSeries row, bool muted, ApiRequestContext context)
    {
        if (!CanMute || IsBulkMutating || !IsCurrent(context) || !_pending.Add(row.Item.ContentId)) return false;
        var lifetime = _lifetime!; var ct = lifetime.Token;
        bool Current() => ReferenceEquals(_lifetime, lifetime) && !ct.IsCancellationRequested && IsCurrent(context);
        var wasMuted = row.Muted; var notified = row.Notifies;
        SetMuted(row, muted); MutationError = null; Notify();
        try { await api.SetSeriesMutedAsync(context, row.Item.ContentId, muted, ct); }
        catch (OperationCanceledException) when (!Current()) { return false; }
        catch
        {
            if (Current()) { row.Muted = wasMuted; row.Notifies = notified; MutationError = MuteError(muted); }
            return false;
        }
        finally { if (Current()) { _pending.Remove(row.Item.ContentId); Notify(); } }
        if (!Current()) return false;
        await ReloadAsync(); return Current();
    }

    public async Task<NotificationSeriesMuteReceipt?> SetManyMutedAsync(IEnumerable<string> seriesIds, bool muted, ApiRequestContext context)
    {
        var ids = seriesIds.Distinct(StringComparer.Ordinal).ToArray();
        if (!CanBulkMute || BulkLocked || ids.Length == 0 || ids.Any(_pending.Contains) || !IsCurrent(context)) return null;
        var lifetime = _lifetime!; var ct = lifetime.Token; var selectionRun = _selectionRun;
        NotificationSeriesMuteReceipt? receipt = null;
        bool Current() => ReferenceEquals(_lifetime, lifetime) && !ct.IsCancellationRequested && IsCurrent(context);
        IsBulkMutating = true; MutationError = null; foreach (var id in ids) _pending.Add(id); Notify();
        try
        {
            var results = await api.SetManySeriesMutedAsync(context, ids, muted, ct);
            if (!Current()) return null;
            foreach (var result in results.Where(result => result.Outcome != "not_found"))
                foreach (var row in Rows.Where(row => row.Item.ContentId == result.SeriesId)) SetMuted(row, muted);
            if (selectionRun == _selectionRun) _selected.ExceptWith(ids);
            receipt = new(results);
        }
        catch (OperationCanceledException) when (!Current()) { return null; }
        catch { if (Current()) MutationError = MuteError(muted, true); return null; }
        finally
        {
            if (Current())
            {
                foreach (var id in ids) _pending.Remove(id);
                await ReloadAsync();
                if (Current()) { IsBulkMutating = false; Notify(); }
            }
        }
        return Current() ? receipt : null;
    }

    private static void SetMuted(NotificationSeries row, bool muted)
    {
        row.Muted = muted;
        row.Notifies = !muted && (row.ActiveReasons.Favorite || row.ActiveReasons.Watchlist || row.ActiveReasons.ContinueWatching || row.ActiveReasons.NextUp);
    }
    private static string Text(string key, string fallback) => AppStrings.Get("web.notifications.series." + key, fallback: fallback);
    private static string MuteError(bool muted, bool many = false) => AppStrings.Get("web.notifications.queries." +
        (muted ? "mute" : "unmute") + (many ? "ManyFailed" : "Failed"),
        fallback: many ? muted ? "Couldn’t mute the selected series" : "Couldn’t unmute the selected series"
            : "Could not change series notifications. Try again.");

    public void Close()
    {
        ++_generation; _query?.Cancel(); _query?.Dispose(); _query = null;
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        ClearSelection(); IsLoading = IsLoadingMore = IsBulkMutating = false; _pending.Clear();
    }
}
