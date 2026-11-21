using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>How one autosaved write ended. Only <see cref="Saved"/> advances the acknowledged value.</summary>
public enum DraftSaveOutcome
{
    /// <summary>The server stored the value.</summary>
    Saved,
    /// <summary>The server definitely refused it (a 4xx other than a timeout). Nothing was stored.</summary>
    Refused,
    /// <summary>It may or may not have been stored (timeout, 5xx, lost transport). Never replayed.</summary>
    Unknown,
    /// <summary>The page, account or server changed first. Nothing was sent, or nobody is waiting for the answer.</summary>
    Retired,
}

/// <summary>The answer to one autosaved write, with the text to show for a refusal or an unknown outcome.</summary>
public readonly record struct DraftSaveResult(DraftSaveOutcome Outcome, string? Message = null)
{
    public static DraftSaveResult Saved { get; } = new(DraftSaveOutcome.Saved);
    public static DraftSaveResult Retired { get; } = new(DraftSaveOutcome.Retired);
    public static DraftSaveResult Refused(string? message) => new(DraftSaveOutcome.Refused, message);
    public static DraftSaveResult Unknown(string? message) => new(DraftSaveOutcome.Unknown, message);
}

/// <summary>
/// Local draft for an editor that saves every change as it is made (no Save button), so the draft never claims a value the server did
/// not store. Official <c>useAutosavedDraft</c> (web/src/hooks/useAutosavedDraft.ts, a84b2551b2): each edit shows at once; saves go out one
/// at a time and edits made while one is in flight merge into a single queued patch; every request is built when it is sent from the last
/// acknowledged value plus that patch, so a refused edit is dropped instead of riding along in the next save; a failed save puts its own
/// fields back to the last acknowledged value without undoing newer edits; closing drops edits not sent yet while a save already in
/// flight still finishes.
///
/// Native additions: an unknown outcome is never replayed. It drops the queued edits, locks the draft so nothing more is sent, and the
/// view returns to the last acknowledged value until the caller reads the server again. Single-threaded: call on the UI thread.
/// </summary>
public sealed class AutosavedDraft<T> where T : class
{
    private readonly Func<T, T> _clone;
    private readonly Func<T, Task<DraftSaveResult>> _save;
    private T _confirmed;
    private Action<T>? _sending, _queued;
    private Task _inFlight = Task.CompletedTask;
    private bool _closed;

    /// <param name="initial">The value the editor opened with: the last acknowledged one.</param>
    /// <param name="clone">An independent copy; patches are applied to copies, never to a stored value.</param>
    /// <param name="save">Writes one complete value. Called for one value at a time.</param>
    public AutosavedDraft(T initial, Func<T, T> clone, Func<T, Task<DraftSaveResult>> save)
    {
        _clone = clone; _save = save; _confirmed = clone(initial);
    }

    /// <summary>Raised after the view, the saving flag, the lock or the failure text changed.</summary>
    public event Action? Changed;
    public bool Saving => _sending != null;
    /// <summary>An unknown outcome happened: nothing more is sent until the caller reads the server and starts a new draft.</summary>
    public bool Locked { get; private set; }
    /// <summary>The text for the latest refusal or unknown outcome; cleared by the next accepted save and by a new edit.</summary>
    public string? Failure { get; private set; }
    /// <summary>The last value the server acknowledged.</summary>
    public T Confirmed => _clone(_confirmed);
    /// <summary>Completes when no save is in flight and none is about to start.</summary>
    public Task Settled => _inFlight;

    /// <summary>What the editor shows: the acknowledged value with the saving and the queued edits on top.</summary>
    public T Value
    {
        get
        {
            var view = _clone(_confirmed); _sending?.Invoke(view); _queued?.Invoke(view); return view;
        }
    }

    /// <summary>Applies an edit to the view and saves it, or queues it behind the save in flight.</summary>
    public void Edit(Action<T> patch)
    {
        if (_closed || Locked) return;
        Failure = null;
        if (_sending != null) _queued += patch;
        else Send(patch);
        Changed?.Invoke();
    }

    /// <summary>Starts from a freshly read server value; edits still pending stay on top of it.</summary>
    public void Reset(T value) { _confirmed = _clone(value); Changed?.Invoke(); }

    /// <summary>Drops edits not sent yet, for when the stored value is about to be cleared: a queued save would land after the clear and write it back.</summary>
    public void DiscardQueued() { _queued = null; Changed?.Invoke(); }

    /// <summary>The editor closed: nothing queued is sent. A save already in flight finishes.</summary>
    public void Close() { _closed = true; _queued = null; }

    private void Send(Action<T> patch) => _inFlight = RunAsync(patch);

    /// <summary>One chain of writes: the edit given, then whatever queued up while each one ran, until nothing is left to send.</summary>
    private async Task RunAsync(Action<T> patch)
    {
        while (true)
        {
            var next = _clone(_confirmed); patch(next); _sending = patch;
            DraftSaveResult result;
            try { result = await _save(next); }
            catch { result = DraftSaveResult.Unknown(null); }
            _sending = null;
            var queued = _queued; _queued = null;
            switch (result.Outcome)
            {
                case DraftSaveOutcome.Saved: _confirmed = next; Failure = null; break;
                case DraftSaveOutcome.Refused: Failure = result.Message ?? ""; break;
                case DraftSaveOutcome.Unknown: Failure = result.Message ?? ""; Locked = true; queued = null; break;
                default: queued = null; _closed = true; break;
            }
            Changed?.Invoke();
            // The edits made while that save ran go out next, built from what the server acknowledged (a refused edit is not in it).
            if (queued == null || _closed || Locked) return;
            patch = queued;
        }
    }
}

/// <summary>Patches for the admin subtitle appearance override editor.</summary>
public static class AdminSubtitleDraft
{
    /// <summary>The edit that turns <paramref name="before"/> into <paramref name="after"/>: only the fields that differ, so a later edit of another field never overwrites it.</summary>
    public static Action<SubtitleAppearance> Between(SubtitleAppearance before, SubtitleAppearance after)
    {
        Action<SubtitleAppearance> patch = _ => { };
        if (before.FontSize != after.FontSize) { var v = after.FontSize; patch += s => s.FontSize = v; }
        if (before.FontFamily != after.FontFamily) { var v = after.FontFamily; patch += s => s.FontFamily = v; }
        if (before.FontColor != after.FontColor) { var v = after.FontColor; patch += s => s.FontColor = v; }
        if (before.TextOpacity != after.TextOpacity) { var v = after.TextOpacity; patch += s => s.TextOpacity = v; }
        if (before.BackgroundColor != after.BackgroundColor) { var v = after.BackgroundColor; patch += s => s.BackgroundColor = v; }
        if (before.BackgroundStyle != after.BackgroundStyle) { var v = after.BackgroundStyle; patch += s => s.BackgroundStyle = v; }
        if (before.BackgroundOpacity != after.BackgroundOpacity) { var v = after.BackgroundOpacity; patch += s => s.BackgroundOpacity = v; }
        if (before.TextOutline != after.TextOutline) { var v = after.TextOutline; patch += s => s.TextOutline = v; }
        if (before.TextOutlineColor != after.TextOutlineColor) { var v = after.TextOutlineColor; patch += s => s.TextOutlineColor = v; }
        if (before.Position != after.Position) { var v = after.Position; patch += s => s.Position = v; }
        return patch;
    }

    public static AutosavedDraft<SubtitleAppearance> Create(SubtitleAppearance initial, Func<string, Task<DraftSaveResult>> saveJson)
        => new(initial, value => value.Clone(), value => saveJson(value.ToJson()));
}
