namespace SiloPlayer.Core.Services;

/// <summary>
/// Which top-level navigation decides the state of an Admin plugin page. Once the page is shown, the
/// newest navigation does. While the page is still opening, plugin script can start further navigations
/// before the one that opens the page has completed (a router setting its fragment at startup, once or
/// several times), and they complete in any order: a same-document navigation never decides, and the
/// newest navigation that loads a document does. Pure bookkeeping: no web view, no network.
/// </summary>
public sealed class AdminPluginPageNavigations
{
    /// <summary>How a navigation completed. A refused redirect is a failure without an HTTP status.</summary>
    public readonly record struct Result(bool IsSuccess, int HttpStatusCode, bool TimedOut = false)
    {
        /// <summary>A fragment or history navigation: it succeeds without an HTTP exchange.</summary>
        public bool IsSameDocument => IsSuccess && HttpStatusCode == 0;
    }

    // Navigations a newer one took over from while the page was opening and that have not completed, oldest first.
    private readonly List<ulong> _earlier = [];
    // How the newest of them that was not a same-document navigation completed.
    private Result? _earlierResult;

    /// <summary>The navigation whose completion is awaited.</summary>
    public ulong? Pending { get; private set; }

    /// <summary>An allowed navigation started. A redirect starts the same navigation again.</summary>
    public void Started(ulong id, bool opening)
    {
        if (opening)
        {
            // A redirect of a navigation a newer one already took over from: it keeps its place.
            if (_earlier.Contains(id)) return;
            if (Pending is { } earlier && earlier != id) _earlier.Add(earlier);
        }
        Pending = id;
    }

    /// <summary>
    /// A navigation completed. Returns the result that decides the page's state, or null when this completion
    /// decides nothing: a navigation that was refused, superseded or belongs to an earlier load, or one made while
    /// the page is opening whose decision another navigation still has to give.
    /// </summary>
    public Result? Completed(ulong id, Result result, bool opening)
    {
        if (Pending != id)
        {
            var index = opening ? _earlier.IndexOf(id) : -1;
            if (index < 0) return null;
            if (result.IsSameDocument) _earlier.RemoveAt(index);
            else
            {
                // It never completes again, so its result is kept for the newer same-document navigations still to
                // complete. The navigations it took over from no longer matter.
                _earlierResult = result;
                _earlier.RemoveRange(0, index + 1);
            }
            return null;
        }
        if (opening && result.IsSameDocument)
        {
            // The navigation it took over from decides: when that completes, or with the result it completed with.
            if (_earlier.Count > 0)
            {
                Pending = _earlier[^1];
                _earlier.RemoveAt(_earlier.Count - 1);
                return null;
            }
            if (_earlierResult is { } earlier) result = earlier;
        }
        Clear();
        return result;
    }

    /// <summary>Forgets every navigation: none of them decides anything afterwards.</summary>
    public void Clear()
    {
        Pending = null;
        _earlier.Clear();
        _earlierResult = null;
    }
}
