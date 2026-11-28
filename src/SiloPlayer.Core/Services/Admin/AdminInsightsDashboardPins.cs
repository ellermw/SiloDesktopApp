using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The one place that knows whether a page's dashboard-layout owner is still writing (official <c>useDashboardLayoutWrites</c>,
/// <c>dashboardLayout.ts</c> at c59798116). Each page that edits the arrangement (the Dashboard, and the Insights page for "Add to
/// dashboard") owns one <see cref="DashboardLayoutSync"/> for its visit and sends its queued edit when it departs. The owners must not
/// overlap: a second reader taking the server's validator while the first still has an edit to send would either save over it or be
/// refused by it. So a departing owner registers its departure here, and an owner about to read the layout first waits until none is
/// running. Safe to call from any thread; waiters resume on the caller's context.
/// </summary>
public sealed class DashboardLayoutWrites
{
    private readonly object _gate = new();
    private readonly List<Task> _running = [];

    /// <summary>True while a departing owner is still sending its edit.</summary>
    public bool IsRunning { get { lock (_gate) return _running.Count > 0; } }

    /// <summary>Registers an owner's departure (<see cref="DashboardLayoutSync.DepartAsync"/>). The returned task is the departure itself.</summary>
    public Task Track(Task departure)
    {
        lock (_gate) _running.Add(departure);
        _ = departure.ContinueWith(finished =>
        {
            lock (_gate) _running.Remove(finished);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return departure;
    }

    /// <summary>Completes when no departure is running, including ones registered while it waited. Never throws for a failed write.</summary>
    public async Task WaitForIdleAsync(CancellationToken ct = default)
    {
        while (true)
        {
            Task[] pending;
            lock (_gate) pending = [.. _running];
            if (pending.Length == 0) return;
            try { await Task.WhenAll(pending).WaitAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // A write that failed is the owner's to report; the next reader only needs it finished.
            }
            // A task is removed by its own continuation, which may run just after the await returned.
            lock (_gate) _running.RemoveAll(task => task.IsCompleted);
        }
    }
}

public enum AdminInsightsPinState
{
    /// <summary>No button: the widget is unknown, the layout has not loaded, or saving is blocked.</summary>
    Hidden,
    /// <summary>"Add to dashboard".</summary>
    Add,
    /// <summary>"On dashboard", a link to the dashboard.</summary>
    OnDashboard,
}

/// <summary>
/// "Add to dashboard" for the Insights cards (official <c>InsightsDashboardPins</c>). The page holds one of these over its one
/// <see cref="DashboardLayoutSync"/>, so every card shares one copy of the arrangement and one debounced, revision-checked save: a
/// second sync per card would keep its own copy and the copies would overwrite each other.
/// </summary>
public sealed class AdminInsightsDashboardPins : IDisposable
{
    private readonly DashboardLayoutSync _sync;
    private bool _disposed;

    public AdminInsightsDashboardPins(DashboardLayoutSync sync)
    {
        _sync = sync;
        _sync.Changed += OnChanged;
        _sync.SaveUnconfirmed += OnSaveUnconfirmed;
    }

    /// <summary>The placed widgets or whether adding is possible changed.</summary>
    public event Action? Changed;

    /// <summary>A write was refused or its outcome is unknown; the dashboard may not have the add.</summary>
    public event Action? SaveUnconfirmed;

    public DashboardLayoutSync Sync => _sync;

    /// <summary>
    /// False until the server layout has loaded and while saving is blocked (a failed save, or a newer layout from another device): an add
    /// could not reach the server then, so the buttons stay hidden rather than confirm a change the dashboard never gets.
    /// </summary>
    public bool CanAdd => !_disposed && _sync.IsLoaded && !_sync.ServerSaveBlocked;

    public bool IsPlaced(string widgetId) => _sync.Entries.Any(entry => string.Equals(entry.Id, widgetId, StringComparison.Ordinal));

    public AdminInsightsPinState StateOf(string widgetId)
    {
        if (_disposed || DashboardRegistry.Find(widgetId) == null) return AdminInsightsPinState.Hidden;
        if (IsPlaced(widgetId)) return AdminInsightsPinState.OnDashboard;
        return CanAdd ? AdminInsightsPinState.Add : AdminInsightsPinState.Hidden;
    }

    /// <summary>
    /// Places the widget and, when it offers the card's window, opens it on that window (a custom range has no widget equivalent).
    /// False when the button was not offered, so nothing changed.
    /// </summary>
    public bool Add(string widgetId, AdminInsightsRange range)
    {
        if (StateOf(widgetId) != AdminInsightsPinState.Add) return false;
        _sync.AddWidget(widgetId);
        if (WidgetRange(widgetId, range) is { } widgetRange) _sync.SetWidgetRange(widgetId, widgetRange);
        return true;
    }

    /// <summary>The widget range an add opens on: the card's window as a widget range, only when the widget offers it.</summary>
    public static DashboardWidgetRange? WidgetRange(string widgetId, AdminInsightsRange range)
    {
        var wire = AdminInsightsRules.InsightsRangeToWidgetRange(range);
        return DashboardRanges.Parse(wire) is { } parsed && DashboardRegistry.Find(widgetId)?.Ranges?.Allowed.Contains(parsed) == true ? parsed : null;
    }

    /// <summary>The toast after an add (<c>web.admin.insights.addToDashboard.added</c>).</summary>
    public static string AddedMessage(string chartName) => AdminInsightsText.T("web.admin.insights.addToDashboard.added", ("chart", chartName));

    private void OnChanged() => Changed?.Invoke();
    private void OnSaveUnconfirmed() => SaveUnconfirmed?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sync.Changed -= OnChanged;
        _sync.SaveUnconfirmed -= OnSaveUnconfirmed;
        Changed = null;
        SaveUnconfirmed = null;
    }
}
