using SiloPlayer.Core.Api;

namespace SiloPlayer.Helpers;

/// <summary>
/// The entry a kept page holds: the navigation parameter, the account, profile and access it was
/// read under, and whether it went stale while the page was away.
/// </summary>
public sealed class KeptPageEntry
{
    /// <summary>The WebUI's catalog staleTime: a kept list older than this rereads in place on Back.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);

    private object? _parameter;
    private ApiRequestContext _context;
    private DateTime _keptAt;
    private bool _stale;

    public bool HasEntry => _parameter is not null;

    /// <summary>Records what the page now shows. Called when a read of <paramref name="parameter"/> lands.</summary>
    public void Keep(object parameter, ApiRequestContext context, DateTime now)
    {
        _parameter = parameter; _context = context; _keptAt = now; _stale = false;
    }

    /// <summary>The page shows nothing it can hand back unchanged (a new entry is loading, or a read is unfinished).</summary>
    public void Forget() { _parameter = null; _stale = false; }

    /// <summary>Something the page shows changed while it was away (a title marked watched, an edit).</summary>
    public void MarkStale() { if (_parameter is not null) _stale = true; }

    /// <summary>Same entry, same account, profile and access. Another identity never sees this page's data.</summary>
    public bool Holds(object? parameter, ApiRequestContext current) =>
        _parameter is not null && Equals(parameter, _parameter) && _context == current;

    public bool NeedsRefresh(DateTime now) => _stale || now - _keptAt >= FreshFor;
}
