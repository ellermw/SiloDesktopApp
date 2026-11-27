using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// "Add to dashboard" for the page (official <c>InsightsDashboardPinsProvider</c>). The page holds exactly one arrangement owner, a
/// <see cref="DashboardLayoutSync"/>, for its visit; every card's button reads and writes through it. The Dashboard page holds its own for
/// its visit, and two owners must never overlap (each would keep its own copy and the later save would be refused or overwrite the
/// earlier). <see cref="DashboardLayoutWrites"/> orders them: this page registers its departure there so the dashboard's first read waits
/// for a pending add, and it waits for a departing dashboard before its own first read.
/// </summary>
public sealed partial class AdminInsightsPage
{
    private readonly DashboardLayoutWrites? _writes = App.Services.GetService<DashboardLayoutWrites>();
    private AdminInsightsDashboardPins? _pins;
    private MonitorAuthorityLease? _pinsLease;

    /// <summary>The layout owner's state changed (it loaded, an add was made, saving was blocked): the buttons on screen follow it.</summary>
    internal event Action? PinsChanged;

    /// <summary>Nothing for a widget official does not offer here, nothing until the layout loaded and while saving is blocked.</summary>
    internal AdminInsightsPinState PinState(string widgetId) => _pins?.StateOf(widgetId) ?? AdminInsightsPinState.Hidden;

    /// <summary>For fixtures: the page's one arrangement owner.</summary>
    internal AdminInsightsDashboardPins? Pins => _pins;

    private void StartPins()
    {
        if (!_active || _pins != null || _authority?.IsAvailable != true) return;
        var api = App.Services.GetService<AdminMonitorApi>();
        if (api == null) return;
        MonitorAuthorityLease lease;
        try { lease = _authority.Capture(); }
        catch (OperationCanceledException) { return; }
        var cache = App.Services.GetService<IDashboardLayoutCache>() ?? AdminDashboardPage.DefaultLayoutCache();
        var time = App.Services.GetService<TimeProvider>() ?? TimeProvider.System;
        var sync = new DashboardLayoutSync(lease, new AdminMonitorDashboardLayoutServer(api), cache, App.Services.GetService<SiloApiClient>()?.BaseUrl ?? "", time);
        var pins = new AdminInsightsDashboardPins(sync);
        pins.Changed += RaisePinsChanged;
        pins.SaveUnconfirmed += OnPinsSaveUnconfirmed;
        _pins = pins;
        _pinsLease = lease;
        _ = LoadPinsAsync(pins, lease);
    }

    private async Task LoadPinsAsync(AdminInsightsDashboardPins pins, MonitorAuthorityLease lease)
    {
        // The page being left registers its departure as it unloads, which can be a moment after this page is created: take that turn first.
        var turn = new TaskCompletionSource();
        if (!DispatcherQueue.TryEnqueue(() => turn.TrySetResult())) turn.TrySetResult();
        await turn.Task;
        if (_writes != null) await _writes.WaitForIdleAsync(lease.Token);
        if (!ReferenceEquals(_pins, pins) || !lease.IsCurrent) return;
        await pins.Sync.LoadAsync(lease.Token);
        if (ReferenceEquals(_pins, pins)) RaisePinsChanged();
    }

    /// <summary>
    /// Gives the arrangement owner up. A departure sends the queued add under the still-valid lease and is registered, so the Dashboard
    /// (or a new Insights visit) does not read the layout before it is saved; an authority change sends nothing.
    /// </summary>
    private void RetirePins(bool departing)
    {
        var pins = _pins;
        var lease = _pinsLease;
        _pins = null;
        _pinsLease = null;
        if (pins == null) return;
        var sync = pins.Sync;
        pins.Changed -= RaisePinsChanged;
        pins.SaveUnconfirmed -= OnPinsSaveUnconfirmed;
        pins.Dispose();
        if (departing && lease is { IsCurrent: true })
            _writes?.Track(DepartPinsAsync(sync, lease));
        else
        {
            sync.Dispose();
            lease?.Dispose();
        }
        RaisePinsChanged();
    }

    private static async Task DepartPinsAsync(DashboardLayoutSync sync, MonitorAuthorityLease lease)
    {
        try { await sync.DepartAsync(); }
        catch (Exception) { }
        finally
        {
            sync.Dispose();
            lease.Dispose();
        }
    }

    private void RaisePinsChanged() => PinsChanged?.Invoke();

    private void OnPinsSaveUnconfirmed()
        => App.Services.GetService<ToastService>()?.Error(DashboardText.T("web.admin.dashboard.hooks.dashboardLayout.saveUnconfirmed"));

    /// <summary>A card's "Add to dashboard": places the widget, opens it on the card's window when it offers it, and says so.</summary>
    internal void AddToDashboard(string widgetId, string chartName)
    {
        if (_pins?.Add(widgetId, Filters.Range) != true) return;
        App.Services.GetService<ToastService>()?.Success(AdminInsightsDashboardPins.AddedMessage(chartName));
    }

    internal void OpenDashboard() => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Dashboard));
}
