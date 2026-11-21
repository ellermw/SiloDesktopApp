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

    /// <summary>How long a request waits for a closing dialog to finish before it is dropped.</summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>A dialog holds the slot: showing, or closed but not yet finished with.</summary>
    public bool Occupied => _finished != null;
    /// <summary>A dialog is showing (not yet closed): no other may open.</summary>
    public bool Showing => _finished != null && !_closed;

    /// <summary>
    /// Takes the slot, or returns null. Null when <paramref name="current"/> says the request is stale (before or after any wait), when a dialog is
    /// still showing (never two), when the closing dialog did not finish within <see cref="Patience"/>, when <paramref name="cancel"/> fired, or when
    /// another request took the slot first.
    /// </summary>
    public async Task<Lease?> AcquireAsync(Func<bool> current, CancellationToken cancel = default)
    {
        if (cancel.IsCancellationRequested || !current()) return null;
        if (_finished != null)
        {
            if (!_closed) return null;
            try { await _finished.Task.WaitAsync(Patience, cancel); }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return null; }
            if (_finished != null || cancel.IsCancellationRequested || !current()) return null;
        }
        _closed = false; _finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return new Lease(this, _finished);
    }

    /// <summary>The slot a dialog holds. Dispose it when the dialog's owner has finished, after the dialog has closed.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly AdminDialogSlot _slot; private readonly TaskCompletionSource _finished; private bool _released;
        internal Lease(AdminDialogSlot slot, TaskCompletionSource finished) { _slot = slot; _finished = finished; }
        /// <summary>The dialog has closed: a request may now wait for it to finish instead of being refused.</summary>
        public void MarkClosed() { if (!_released && ReferenceEquals(_slot._finished, _finished)) _slot._closed = true; }
        public void Dispose()
        {
            if (_released) return; _released = true;
            if (ReferenceEquals(_slot._finished, _finished)) { _slot._finished = null; _slot._closed = false; }
            _finished.TrySetResult();
        }
    }
}
