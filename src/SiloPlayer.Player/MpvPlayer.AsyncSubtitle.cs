using System.Runtime.InteropServices;
using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    private readonly object _asyncSubtitleGate = new();
    private readonly Dictionary<ulong, TaskCompletionSource<bool>> _asyncSubtitleCommands = [];
    private ulong _asyncSubtitleCommandId;
    private readonly SemaphoreSlim _asyncSubtitleAddLane = new(1, 1);
    private long _asyncSubtitleFileGeneration;
    private long _asyncSubtitleFileRequest;
    private Task _asyncSubtitleFileTransition = Task.CompletedTask;
    private long _explicitSubtitleRevision;
    private string _explicitSubtitleSid = "no";

    /// <summary>
    /// Downloads through mpv's existing URL reader without blocking the caller
    /// or selecting the result. The file/selection owner must be checked again
    /// before this SID is cached or selected.
    /// </summary>
    public async Task<int> AddSubtitleUnselectedAsync(string url, string? title, string? language, CancellationToken ct)
    {
        var pending = AddSubtitleInLaneAsync(url, title, language, ct);
        try
        {
            var result = await pending.WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return result.Sid;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Abort is a request, not a guarantee that native I/O has stopped.
            // Release the caller now, but keep the lane and late-SID cleanup
            // alive until the actual reply so a retry cannot claim its track.
            _ = CleanupRetiredSubtitleAddAsync(pending);
            throw;
        }
    }

    private async Task<(int Sid, long FileGeneration)> AddSubtitleInLaneAsync(string url, string? title, string? language, CancellationToken ct)
    {
        long request;
        Task transition;
        lock (_asyncSubtitleGate)
        {
            request = _asyncSubtitleFileRequest;
            transition = _asyncSubtitleFileTransition;
        }
        await transition.WaitAsync(ct).ConfigureAwait(false);
        await _asyncSubtitleAddLane.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Task<(int Sid, long FileGeneration)> pending;
            lock (_asyncSubtitleGate)
            {
                if (request != _asyncSubtitleFileRequest) throw new OperationCanceledException();
                pending = AddSubtitleUnselectedCoreAsync(url, title, language, ct);
            }
            return await pending.ConfigureAwait(false);
        }
        finally { _asyncSubtitleAddLane.Release(); }
    }

    private void SubmitSubtitleFileTransition(Action submit)
    {
        lock (_asyncSubtitleGate)
        {
            var request = ++_asyncSubtitleFileRequest;
            using (var lease = EnterNative())
                foreach (var id in _asyncSubtitleCommands.Keys)
                    if (lease.Active) mpv_abort_async_command(_mpvHandle, id);
            // Abort only requests cancellation. Keep the outgoing file epoch
            // until the real reply and exact-URL/SID cleanup have drained.
            if (_asyncSubtitleAddLane.Wait(0))
            {
                try
                {
                    _asyncSubtitleFileTransition = Task.CompletedTask;
                    _asyncSubtitleFileGeneration++;
                    submit();
                }
                finally { _asyncSubtitleAddLane.Release(); }
                return;
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _asyncSubtitleFileTransition = completion.Task;
            _ = CompleteSubtitleFileTransitionAsync(request, submit, completion);
        }
    }

    private async Task CompleteSubtitleFileTransitionAsync(long request, Action submit, TaskCompletionSource completion)
    {
        await _asyncSubtitleAddLane.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_asyncSubtitleGate)
            {
                if (request != _asyncSubtitleFileRequest || _disposed) return;
                _asyncSubtitleFileGeneration++;
                submit();
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { ReportErrorSafely($"Deferred file transition failed: {ex.Message}"); }
        finally
        {
            _asyncSubtitleAddLane.Release();
            completion.TrySetResult();
        }
    }

    private async Task CleanupRetiredSubtitleAddAsync(Task<(int Sid, long FileGeneration)> pending)
    {
        try
        {
            var (sid, fileGeneration) = await pending.ConfigureAwait(false);
            // Also cover cancellation racing a successfully completed add.
            lock (_asyncSubtitleGate)
            {
                if (sid > 0 && fileGeneration == _asyncSubtitleFileGeneration)
                    Command("sub-remove", sid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { ReportErrorSafely($"Retired subtitle add cleanup failed: {ex.Message}"); }
    }

    private async Task<(int Sid, long FileGeneration)> AddSubtitleUnselectedCoreAsync(string url, string? title, string? language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong id;
        long fileGeneration, fileRequest, selectionRevision;
        int previousSid;
        HashSet<int> previousIds;
        var submissionError = 0;
        lock (_asyncSubtitleGate)
        {
            ThrowIfNotInitialized();
            fileGeneration = _asyncSubtitleFileGeneration;
            fileRequest = _asyncSubtitleFileRequest;
            selectionRevision = _explicitSubtitleRevision;
            previousSid = (int)Math.Round(GetPropertyDouble("sid"));
            previousIds = GetSubtitleTrackIds(externalOnly: true).ToHashSet();
            id = ++_asyncSubtitleCommandId;
            _asyncSubtitleCommands.Add(id, completion);
            // auto cannot outlive this native file: replacement commands drain
            // this lane and its cleanup first. Completion preserves applied SID.
            var args = new[] { "sub-add", url, "auto", title ?? "", language ?? "" };
            var pointers = new IntPtr[args.Length + 1];
            try
            {
                for (var i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
                using var lease = EnterNative();
                // mpv copies the arguments before returning. Only submission,
                // not the network read, owns this lock/native lease.
                if (!lease.Active || (submissionError = mpv_command_async(_mpvHandle, id, pointers)) < 0)
                {
                    _asyncSubtitleCommands.Remove(id);
                    completion.TrySetResult(false);
                }
            }
            catch
            {
                _asyncSubtitleCommands.Remove(id);
                throw;
            }
            finally
            {
                foreach (var pointer in pointers)
                    if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            }
        }
        if (submissionError < 0)
            ReportErrorSafely($"mpv_command_async(sub-add) failed: {GetErrorString(submissionError)}");
        using var cancellation = ct.Register(() =>
        {
            using var lease = EnterNative();
            if (lease.Active) mpv_abort_async_command(_mpvHandle, id);
        });
        try
        {
            var added = await completion.Task.ConfigureAwait(false);
            var sid = 0;
            lock (_asyncSubtitleGate)
            {
                // An ordinal from the previous file must never remove or
                // select a successor file's equally numbered native track.
                if (fileGeneration == _asyncSubtitleFileGeneration)
                {
                    sid = FindExternalSubtitleSid(url, previousIds);
                    if (sid > 0)
                    {
                        if ((int)Math.Round(GetPropertyDouble("sid")) == sid)
                        {
                            // auto may select a new default while Off. Preserve
                            // a newer explicit choice instead of replaying ours.
                            var restore = selectionRevision == _explicitSubtitleRevision
                                ? previousSid > 0 ? previousSid.ToString(System.Globalization.CultureInfo.InvariantCulture) : "no"
                                : _explicitSubtitleSid;
                            using var lease = EnterNative();
                            if (lease.Active) mpv_set_property_string(_mpvHandle, "sid", restore);
                        }
                        if (ct.IsCancellationRequested || fileRequest != _asyncSubtitleFileRequest || !added)
                        {
                            Command("sub-remove", sid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            sid = 0;
                        }
                    }
                }
            }
            // Return the identity even if cancellation raced the cleanup check.
            // The caller's cancellation path can then remove that exact late SID.
            return (added ? sid : 0, fileGeneration);
        }
        finally
        {
            lock (_asyncSubtitleGate) _asyncSubtitleCommands.Remove(id);
        }
    }

    private int FindExternalSubtitleSid(string url, HashSet<int> previousIds)
    {
        using var lease = EnterNative();
        if (!lease.Active || mpv_get_property_int(_mpvHandle, "track-list/count", MPV_FORMAT_INT64, out var count) < 0) return 0;
        var matches = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var prefix = $"track-list/{i}/";
            if (Read(prefix + "type") == "sub" && Read(prefix + "external") == "yes" &&
                Read(prefix + "external-filename") == url &&
                mpv_get_property_int(_mpvHandle, prefix + "id", MPV_FORMAT_INT64, out var trackId) >= 0 && trackId > 0 &&
                !previousIds.Contains(checked((int)trackId)))
                matches.Add(checked((int)trackId));
        }
        // Concurrent cue/AI adds do not share our lane. Never treat every new
        // inventory ID, or an ambiguous same-URL pair, as this command's SID.
        return matches.Count == 1 ? matches[0] : 0;

        string? Read(string name)
        {
            var pointer = mpv_get_property_string(_mpvHandle, name);
            try { return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer); }
            finally { if (pointer != IntPtr.Zero) mpv_free(pointer); }
        }
    }

    private void CompleteAsyncSubtitleCommand(MpvEvent reply)
    {
        TaskCompletionSource<bool>? completion;
        lock (_asyncSubtitleGate)
            _asyncSubtitleCommands.Remove(reply.ReplyUserdata, out completion);
        if (completion == null) return;
        if (reply.Error < 0)
            ReportErrorSafely($"mpv sub-add reply failed: {GetErrorString(reply.Error)}");
        completion.TrySetResult(reply.Error >= 0);
    }

    private void CancelAsyncSubtitleCommands()
    {
        lock (_asyncSubtitleGate)
        {
            using var lease = EnterNative();
            foreach (var (id, completion) in _asyncSubtitleCommands)
            {
                if (lease.Active) mpv_abort_async_command(_mpvHandle, id);
                completion.TrySetResult(false);
            }
            _asyncSubtitleCommands.Clear();
        }
    }
}
