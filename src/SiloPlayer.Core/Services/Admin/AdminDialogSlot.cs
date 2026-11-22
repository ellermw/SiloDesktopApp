namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The one place a page's modal dialog can be. Two dialogs are never open at once, but a request made while the previous dialog is closing is not
/// lost: a native dialog is gone from the screen (Closed) a moment before its owner has finished with it (ShowAsync returning), and a button pressed
/// in between used to be dropped without a sound. <see cref="AcquireAsync"/> waits for that closing dialog, for a bounded time, and the wait ends at
/// once when the page is left or retired (<c>cancel</c>) or when the state the request was built from changed (<c>current</c>).
/// Single-threaded: call on the UI thread.
/// </summary>
public sealed class AdminDialogSlot
{
    private TaskCompletionSource? _finished;
    private bool _closed;
    private Func<bool>? _gone;

    /// <summary>How long a request waits for a closing dialog to finish before it is dropped.</summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>A dialog holds the slot: showing, or closed but not yet finished with.</summary>
    public bool Occupied => _finished != null;
    /// <summary>A dialog is showing (not yet closed): no other may open.</summary>
    public bool Showing => _finished != null && !_closed && !(_gone?.Invoke() ?? false);

    /// <summary>Why a request did not get the slot.</summary>
    public enum Refusal
    {
        /// <summary>The request got the slot.</summary>
        None,
        /// <summary>What the request was built from changed (before or after any wait), or the page's own check said no.</summary>
        Stale,
        /// <summary>A dialog is showing: never two at once.</summary>
        Showing,
        /// <summary>The closing dialog did not finish within <see cref="Patience"/>: the one refusal the admin is told about.</summary>
        TimedOut,
        /// <summary>The page was left, reloaded or retired while the request waited.</summary>
        Cancelled,
        /// <summary>Another request took the slot first while this one waited.</summary>
        Taken,
    }

    /// <summary>
    /// Takes the slot, or returns null. Null when <paramref name="current"/> says the request is stale (before or after any wait), when a dialog is
    /// still showing (never two), when the closing dialog did not finish within <see cref="Patience"/>, when <paramref name="cancel"/> fired, or when
    /// another request took the slot first.
    /// </summary>
    public async Task<Lease?> AcquireAsync(Func<bool> current, CancellationToken cancel = default) => (await TryAcquireAsync(current, cancel)).Lease;

    /// <summary>The same, saying why a request got nothing (see <see cref="Refusal"/>).</summary>
    public async Task<(Lease? Lease, Refusal Refusal)> TryAcquireAsync(Func<bool> current, CancellationToken cancel = default)
    {
        if (cancel.IsCancellationRequested) return (null, Refusal.Cancelled);
        if (!current()) return (null, Refusal.Stale);
        if (_finished != null)
        {
            if (Showing) return (null, Refusal.Showing);
            try { await _finished.Task.WaitAsync(Patience, cancel); }
            catch (TimeoutException) { return (null, Refusal.TimedOut); }
            catch (OperationCanceledException) { return (null, Refusal.Cancelled); }
            if (cancel.IsCancellationRequested) return (null, Refusal.Cancelled);
            if (_finished != null) return (null, Refusal.Taken);
            if (!current()) return (null, Refusal.Stale);
        }
        _closed = false; _gone = null; _finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return (new Lease(this, _finished), Refusal.None);
    }

    /// <summary>The slot a dialog holds. Dispose it when the dialog's owner has finished, after the dialog has closed.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly AdminDialogSlot _slot; private readonly TaskCompletionSource _finished; private bool _released;
        internal Lease(AdminDialogSlot slot, TaskCompletionSource finished) { _slot = slot; _finished = finished; }
        /// <summary>The dialog has closed: a request may now wait for it to finish instead of being refused.</summary>
        public void MarkClosed() { if (!_released && ReferenceEquals(_slot._finished, _finished)) _slot._closed = true; }
        /// <summary>
        /// How to tell that the dialog has already left the screen before its Closed event and its owner's continuation have run: a press made in that
        /// gap finds the dialog gone and waits, instead of being refused as "a dialog is showing".
        /// </summary>
        public void GoneWhen(Func<bool> gone) { if (!_released && ReferenceEquals(_slot._finished, _finished)) _slot._gone = gone; }
        public void Dispose()
        {
            if (_released) return; _released = true;
            if (ReferenceEquals(_slot._finished, _finished)) { _slot._finished = null; _slot._closed = false; _slot._gone = null; }
            _finished.TrySetResult();
        }
    }
}
