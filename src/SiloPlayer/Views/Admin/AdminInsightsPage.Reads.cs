using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>What the tabs and panels reach the server and the page through: the reads, their refresh window and the page's own navigation.</summary>
public sealed partial class AdminInsightsPage
{
    private readonly AdminInsightsApi? _insights = App.Services.GetService<AdminInsightsApi>();
    private readonly Dictionary<AdminInsightsTab, IDisposable> _tabReaders = [];
    private MonitorAuthorityLease? _tabReadsLease;

    internal (MonitorAuthorityLease? Lease, AdminInsightsReader<T>? Reader) GetTabReader<T>() where T : class
    {
        if (_mountedTab is not { } tab) return default;
        _tabReadsLease ??= CaptureLease();
        if (_tabReadsLease is not { IsCurrent: true } lease) return default;
        if (!_tabReaders.TryGetValue(tab, out var reader))
            _tabReaders.Add(tab, reader = new AdminInsightsReader<T>(lease, keepPrevious: true));
        return (lease, (AdminInsightsReader<T>)reader);
    }

    private void RetireTabReads()
    {
        foreach (var reader in _tabReaders.Values) reader.Dispose();
        _tabReaders.Clear();
        _tabReadsLease?.Dispose();
        _tabReadsLease = null;
    }

    /// <summary>The Insights reads. A host without them (nothing registered) fails each read with a load error rather than showing nothing.</summary>
    internal AdminInsightsApi Insights => _insights ?? throw new InvalidOperationException("This server does not provide Insights for this view.");

    /// <summary>After a Refresh, reads started in the next seconds send <c>refresh=true</c> (<see cref="AdminInsightsRefreshGate"/>).</summary>
    internal AdminInsightsRefreshGate? RefreshGate => _refreshGate;

    /// <summary>
    /// A lease for the visit's tab reads or one panel's reads: it retires with the account, server, profile or role it was taken under, and the content
    /// that holds it is dropped with the page. Null when the monitor authority is not available (nothing is read then).
    /// </summary>
    internal MonitorAuthorityLease? CaptureLease()
    {
        if (_authority?.IsAvailable != true) return null;
        try { return _authority.Capture(); }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>A panel's read or names changed: draw it again.</summary>
    internal void RefreshPanel() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_active && IsLoaded) RenderDetail();
    });

    /// <summary>The account's username when the account picker has been read, else null.</summary>
    internal string? AccountName(long userId) => _accounts.FirstOrDefault(account => account.Id == userId)?.Label
        ?? (_selectedAccount is { } selected && selected.Id == userId ? selected.Label : null);

    /// <summary>True once a person panel is registered: until then a viewer's name is text, not a link to a panel that does not exist.</summary>
    internal bool CanOpenPerson => _panelFactories.ContainsKey(AdminInsightsPanelKind.Person);

    /// <summary>A title row of a tab opens the title panel for everyone: any person scope in the URL closes (official <c>person: null, profile: null</c>).</summary>
    internal void OpenTitleFromList(string contentId) => ReplaceLocation(AdminInsightsFilters.WithTitle(AdminInsightsFilters.CloseDetail(Location), contentId));

    /// <summary>A library row narrows the whole page to that library and keeps everything else, an open panel included.</summary>
    internal void NarrowToLibrary(long libraryId) => ReplaceLocation(AdminInsightsFilters.WithLibrary(Location, libraryId));

    /// <summary>"Show everyone": the title panel stays on its title, without the person.</summary>
    internal void ShowEveryone() => ReplaceLocation(Location.WithQuery("person", null).WithQuery("profile", null));
}
