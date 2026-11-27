using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>
/// What a preference page shows after a save that did not succeed (official #5204, web <c>useShowThenSave</c>).
/// A change is shown before it is saved. If the server refused it (a 4xx other than 408) nothing was stored, so the
/// last stored value is shown again at once. If the outcome is unknown (a 5xx, a timeout, a dropped connection) the
/// value is left on screen until every save made through this tracker has settled, and the caller then reads what the
/// server holds and shows that. A read that failed shows nothing and is tried again after the next save settles.
/// Saves are tracked per owner (server, account and profile) and per key, so one profile's save never decides
/// what another profile shows.
/// </summary>
public sealed class SettingSaveReconciler
{
    private sealed class Pending
    {
        public object Latest = new();
        public int Saving;
        public int Made;
        public string StoredValue = "";
        public int StoredChange;
        public bool Restored;
    }

    private readonly object _gate = new();
    private readonly Dictionary<(object Owner, string Key), Pending> _pending = [];
    private readonly List<object> _owed = [];

    /// <summary>One save, from <see cref="Begin"/> until <see cref="Succeeded"/> or <see cref="Failed"/>.</summary>
    public sealed class Change
    {
        internal Change(object owner, string key, string value, int number, object token, object pending)
        {
            Owner = owner; Key = key; Value = value; Number = number; Token = token; Pending = pending;
        }
        public object Owner { get; }
        public string Key { get; }
        public string Value { get; }
        internal int Number { get; }
        internal object Token { get; }
        internal object Pending { get; }
        internal bool Settled { get; set; }
    }

    /// <summary>What to do once a save has settled: the value to show again at once, and whether to read the stored values now.</summary>
    public readonly record struct Settlement(string? Show, bool Reread);

    /// <summary>Whether the server refused the change, so it certainly stored nothing: a 4xx other than 408 (a 408 or 5xx can come from a gateway after the write was applied).</summary>
    public static bool IsDefinitiveRejection(Exception error) =>
        error is ApiException { StatusCode: >= 400 and < 500 and not 408 };

    /// <summary>Whether any save made through this tracker has not settled.</summary>
    public bool HasPending
    {
        get { lock (_gate) return _pending.Count > 0; }
    }

    /// <summary>Starts a save of <paramref name="value"/> for <paramref name="key"/>; <paramref name="previous"/> is what was stored (and shown) before.</summary>
    public Change Begin(object owner, string key, string value, string previous)
    {
        lock (_gate)
        {
            var identity = (owner, key);
            if (!_pending.TryGetValue(identity, out var pending))
                _pending[identity] = pending = new Pending { StoredValue = previous };
            var token = new object();
            var number = ++pending.Made;
            pending.Latest = token;
            pending.Saving++;
            pending.Restored = false;
            return new Change(owner, key, value, number, token, pending);
        }
    }

    /// <summary>The save landed. A change that landed after a newer one does not replace it; one that landed after the newest was refused is shown now.</summary>
    public Settlement Succeeded(Change change, object currentOwner)
    {
        lock (_gate)
        {
            var pending = Settle(change);
            string? show = null;
            if (change.Number >= pending.StoredChange)
            {
                pending.StoredValue = change.Value;
                pending.StoredChange = change.Number;
                if (pending.Restored) show = pending.StoredValue;
            }
            return Result(show, currentOwner);
        }
    }

    /// <summary>The save failed. Only the newest change to a key acts on it; a newer change settles the key instead.</summary>
    public Settlement Failed(Change change, Exception error, object currentOwner)
    {
        lock (_gate)
        {
            var pending = Settle(change);
            string? show = null;
            if (ReferenceEquals(pending.Latest, change.Token))
            {
                if (IsDefinitiveRejection(error)) { pending.Restored = true; show = pending.StoredValue; }
                else if (!_owed.Contains(change.Owner)) _owed.Add(change.Owner);
            }
            return Result(show, currentOwner);
        }
    }

    /// <summary>The read after an unknown outcome did not succeed: nothing was shown, so the owner is owed one more read.</summary>
    public void OweReread(object owner)
    {
        lock (_gate) if (!_owed.Contains(owner)) _owed.Add(owner);
    }

    private Pending Settle(Change change)
    {
        var pending = (Pending)change.Pending;
        if (change.Settled) return pending;
        change.Settled = true;
        if (--pending.Saving == 0) _pending.Remove((change.Owner, change.Key));
        return pending;
    }

    // A read is owed to the owner showing now, and only once nothing is saving: another preference still saving would be put back.
    private Settlement Result(string? show, object currentOwner)
    {
        if (_pending.Count > 0 || !_owed.Contains(currentOwner)) return new(show, false);
        _owed.Clear();
        return new(show, true);
    }
}
