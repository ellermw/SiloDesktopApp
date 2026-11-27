using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// What every data tab of the Insights page does the same way (official <c>use*Tab</c> + <c>insightRefreshProps</c>): reads only while it is
/// the mounted tab, under the lease it captured when it was mounted; shows a skeleton until the first answer, the load error (with Retry)
/// when there is none, and otherwise the result. A result for another window stays on screen while a new one loads, with its links
/// disabled and a sweep along its top edge; a failed Refresh keeps the last result and says so. A tab belongs to one authority lease: when
/// the account, server or profile changes the page drops the tab and builds a new one, so nothing is shown from the previous identity.
/// </summary>
internal abstract class AdminInsightsTabBase<T> : IAdminInsightsTabContent, IDisposable where T : class
{
    private readonly AdminInsightsResultHost _host = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly MonitorAuthorityLease? _lease;
    private readonly AdminInsightsReader<T>? _reader;
    private AdminInsightsFilterState? _filters;
    private (object?, bool, bool, object?, object?, AdminInsightsFilterState?) _rendered;
    private bool _renderQueued;
    private bool _disposed;

    protected AdminInsightsTabBase(AdminInsightsPage page)
    {
        Page = page;
        _lease = page.CaptureLease();
        if (_lease == null) return;
        _reader = new AdminInsightsReader<T>(_lease, keepPrevious: true);
        _reader.Changed += OnReaderChanged;
    }

    protected AdminInsightsPage Page { get; }
    public FrameworkElement View => _host;
    protected AdminInsightsFilterState Filters => _filters ?? Page.Filters;

    /// <summary>The result is a placeholder for another window: nothing may navigate from it.</summary>
    protected bool IsPlaceholder { get; private set; }

    /// <summary>Everything this read depends on except the authority: the same key is the same result.</summary>
    protected abstract string KeyFor(AdminInsightsFilterState filters);
    protected abstract Task<T> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct);
    protected abstract FrameworkElement BuildBody(T data, AdminInsightsFilterState filters);
    protected abstract FrameworkElement BuildSkeleton();

    /// <summary>
    /// The load error of a first read that failed. A tab whose controls must stay on screen to recover (People keeps its search box) draws
    /// its own around the error; everything else shows the section error in place of the tab.
    /// </summary>
    protected virtual FrameworkElement BuildError(Exception error, Action retry) => AdminInsightsUi.SectionError(error.Message, retry);

    /// <summary>A link or click that opens something from a result, ignored while the result is only a placeholder for the previous window.</summary>
    protected Action? Guarded(Action? action) => action == null ? null : () => { if (!IsPlaceholder) action(); };

    public void ApplyFilters(AdminInsightsFilterState filters)
    {
        if (_disposed) return;
        _filters = filters;
        _ = Request(force: false);
        Render();
    }

    public Task RefreshAsync() => _disposed ? Task.CompletedTask : Request(force: true);

    private Task Request(bool force)
    {
        if (_reader == null || _lease == null || _filters == null) return Task.CompletedTask;
        var filters = _filters;
        return _reader.Request(KeyFor(filters), ct => LoadAsync(_lease, filters, Page.RefreshGate?.ShouldRefresh ?? false, ct), force);
    }

    /// <summary>A choice the tab itself owns (the Titles ranking) changed what it reads: read it and draw it again.</summary>
    protected void Reload()
    {
        _rendered = default;
        _ = Request(force: false);
        Render();
    }

    /// <summary>Re-draws from the current state.</summary>
    protected void Redraw()
    {
        _rendered = default;
        Render();
    }

    private void OnReaderChanged()
    {
        if (_renderQueued || _disposed) return;
        _renderQueued = true;
        _dispatcher.TryEnqueue(() =>
        {
            _renderQueued = false;
            Render();
        });
    }

    private void Render()
    {
        if (_disposed || _filters == null) return;
        var filters = _filters;
        var view = _reader?.View ?? AdminInsightsReadView<T>.Idle;
        var signature = ((object?)view.Data, view.IsPlaceholder, view.IsLoading, (object?)view.Error, (object?)view.RefreshError, (AdminInsightsFilterState?)filters);
        if (_rendered.Equals(signature)) return;
        _rendered = signature;
        IsPlaceholder = view.IsPlaceholder;
        if (view.Data is { } data)
        {
            var body = BuildBody(data, filters);
            if (view.RefreshError != null)
                body = AdminInsightsLayout.Stack(AdminInsightsUi.Notice(AdminInsightsText.T("web.admin.insights.loadError") + " " + view.RefreshError.Message), body);
            _host.SetResult(body, AdminInsightsLayout.Links(body));
            _host.SetRefreshing(view.IsPlaceholder);
        }
        else if (view.ShowsError)
        {
            _host.SetResult(BuildError(view.Error!, () => _ = Request(force: true)));
        }
        else
        {
            _host.SetResult(BuildSkeleton());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader?.Dispose();
        _lease?.Dispose();
    }
}
