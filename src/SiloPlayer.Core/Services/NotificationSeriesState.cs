using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

/// <summary>Owns the two server-ordered streams independently of the inbox read cutoff.</summary>
public sealed class NotificationSeriesState(NotificationsApi api) : ObservableObject
{
    private sealed class Stream
    {
        public string? Cursor;
        public bool More;
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
        public void Reset() { Cursor = null; More = false; Seen.Clear(); }
    }
    private readonly Stream _active = new(), _inactive = new();
    private CancellationTokenSource? _query, _lifetime;
    private long _generation;
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    public ObservableCollection<NotificationSeries> Active { get; } = [];
    public ObservableCollection<NotificationSeries> Inactive { get; } = [];
    public ApiRequestContext? Context { get; private set; }
    public NotificationSeriesOptions Options { get; private set; } = new();
    public NotificationPreferences? Preferences { get; private set; }
    public bool Available { get; private set; }
    public bool CanMute { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsLoadingMore { get; private set; }
    public string? Error { get; private set; }
    public string? MutationError { get; private set; }
    public bool HasMore => _active.More || _inactive.More;
    public bool ShowInactive => !_active.More;
    public bool IsEmpty => !IsLoading && !HasMore && Active.Count == 0 && Inactive.Count == 0 && Error == null;
    public bool IsCurrent(ApiRequestContext context) => _lifetime is { IsCancellationRequested: false } && Context == context && api.IsCurrentContext(context);
    public bool IsPending(string id) => _pending.Contains(id);
    private void Notify() => OnPropertyChanged(string.Empty);

    public async Task OpenAsync()
    {
        Close();
        var previous = Context; Context = api.RequestContext;
        _lifetime = new(); var ct = _lifetime.Token; var context = Context.Value;
        if (previous != context) { Options = new(); Active.Clear(); Inactive.Clear(); }
        Available = CanMute = false; Error = MutationError = null; IsLoading = true; Notify();
        try
        {
            var capability = await api.GetCapabilityAsync(context, ct);
            if (!IsCurrent(context)) return;
            Available = capability.Series?.Available == true; CanMute = Available && capability.Series?.Mute == true;
            if (!Available) return;
            try { Preferences = await api.GetPreferencesAsync(context, ct); }
            catch (OperationCanceledException) { throw; }
            catch { Preferences = null; }
            if (IsCurrent(context)) await ReloadAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !IsCurrent(context)) { }
        catch { if (!ct.IsCancellationRequested && IsCurrent(context)) Error = "Could not load the series that notify you. Retry."; }
        finally { if (!ct.IsCancellationRequested && IsCurrent(context)) { IsLoading = false; Notify(); } }
    }

    public Task RetryAsync() => !Available ? OpenAsync()
        : Error?.StartsWith("Couldn’t load more", StringComparison.Ordinal) == true ? LoadMoreAsync() : ReloadAsync();

    public async Task SetOptionsAsync(NotificationSeriesOptions options)
    {
        if (Options == options) return;
        Options = options with { Q = options.Q.Trim() };
        Active.Clear(); Inactive.Clear();
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (!Available || Context is not { } context || !IsCurrent(context)) return;
        _query?.Cancel(); _query?.Dispose(); _query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime!.Token);
        var ct = _query.Token; var generation = ++_generation; var options = Options;
        _active.Reset(); _inactive.Reset(); IsLoading = true; IsLoadingMore = false; Error = null; Notify();
        try
        {
            var active = api.GetSeriesAsync(context, "active", options, ct: ct);
            var inactive = api.GetSeriesAsync(context, "inactive", options, ct: ct);
            await Task.WhenAll(active, inactive);
            if (!Current()) return;
            Apply(_active, Active, await active, true); Apply(_inactive, Inactive, await inactive, true);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch { if (Current()) Error = "Could not load the series that notify you. Retry."; }
        finally { if (Current()) { IsLoading = false; Notify(); } }
        bool Current() => !ct.IsCancellationRequested && generation == _generation && IsCurrent(context);
    }

    public async Task LoadMoreAsync(bool automatic = false)
    {
        if (automatic && Error != null) return;
        if (IsLoading || IsLoadingMore || !HasMore || Context is not { } context || !IsCurrent(context)) return;
        var stream = _active.More ? _active : _inactive; var rows = _active.More ? Active : Inactive;
        var status = _active.More ? "active" : "inactive"; var generation = _generation; var ct = _query!.Token;
        IsLoadingMore = true; Error = null; Notify();
        try
        {
            var page = await api.GetSeriesAsync(context, status, Options, stream.Cursor, ct);
            if (Current()) Apply(stream, rows, page, false);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch { if (Current()) Error = "Couldn’t load more series. Retry."; }
        finally { if (Current()) { IsLoadingMore = false; Notify(); } }
        bool Current() => !ct.IsCancellationRequested && generation == _generation && IsCurrent(context);
    }

    private static void Apply(Stream stream, ObservableCollection<NotificationSeries> rows, NotificationSeriesPage page, bool reset)
    {
        var next = page.Page!.HasMore == true ? page.Page.NextCursor : null;
        if (next != null && !stream.Seen.Add(next)) throw new InvalidDataException("Series cursor repeated. Reload notifications.");
        if (reset) rows.Clear();
        foreach (var row in page.Items!) if (!rows.Any(existing => existing.Item.ContentId == row.Item.ContentId)) rows.Add(row);
        stream.Cursor = next; stream.More = next != null;
    }

    public async Task<bool> SetMutedAsync(NotificationSeries row, bool muted, ApiRequestContext context)
    {
        if (!CanMute || !IsCurrent(context) || !_pending.Add(row.Item.ContentId)) return false;
        var lifetime = _lifetime!; var ct = lifetime.Token;
        bool Current() => ReferenceEquals(_lifetime, lifetime) && !ct.IsCancellationRequested && IsCurrent(context);
        var wasMuted = row.Muted; var notified = row.Notifies;
        row.Muted = muted; row.Notifies = !muted && (row.ActiveReasons.Favorite || row.ActiveReasons.Watchlist || row.ActiveReasons.ContinueWatching || row.ActiveReasons.NextUp);
        MutationError = null; Notify();
        try { await api.SetSeriesMutedAsync(context, row.Item.ContentId, muted, ct); }
        catch (OperationCanceledException) when (!Current()) { return false; }
        catch
        {
            if (Current()) { row.Muted = wasMuted; row.Notifies = notified; MutationError = muted ? "Failed to mute the series. Try again." : "Failed to unmute the series. Try again."; }
            return false;
        }
        finally { if (Current()) { _pending.Remove(row.Item.ContentId); Notify(); } }
        // A receipt is accepted before refreshing. A read failure cannot roll it back or replay the write.
        if (!Current()) return false;
        await ReloadAsync(); return Current();
    }

    public void Close()
    {
        ++_generation; _query?.Cancel(); _query?.Dispose(); _query = null;
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        IsLoading = IsLoadingMore = false; _pending.Clear();
    }
}
