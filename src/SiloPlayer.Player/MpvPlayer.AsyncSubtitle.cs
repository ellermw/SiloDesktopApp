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
    private long _explicitSubtitleRevision;
    private string _explicitSubtitleSid = "no";

    /// <summary>
    /// Downloads through mpv's existing URL reader without blocking the caller
    /// or selecting the result. The file/selection owner must be checked again
    /// before this SID is cached or selected.
    /// </summary>
    public async Task<int> AddSubtitleUnselectedAsync(string url, string? title, string? language, CancellationToken ct)
    {
        await _asyncSubtitleAddLane.WaitAsync(ct).ConfigureAwait(false);
        try { return await AddSubtitleUnselectedCoreAsync(url, title, language, ct).ConfigureAwait(false); }
        finally { _asyncSubtitleAddLane.Release(); }
    }

    private async Task<int> AddSubtitleUnselectedCoreAsync(string url, string? title, string? language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong id;
        long fileGeneration, selectionRevision;
        int previousSid;
        HashSet<int> previousIds;
        var submissionError = 0;
        lock (_asyncSubtitleGate)
        {
            ThrowIfNotInitialized();
            fileGeneration = _asyncSubtitleFileGeneration;
            selectionRevision = _explicitSubtitleRevision;
            previousSid = (int)Math.Round(GetPropertyDouble("sid"));
            previousIds = GetSubtitleTrackIds(externalOnly: true).ToHashSet();
            id = ++_asyncSubtitleCommandId;
            _asyncSubtitleCommands.Add(id, completion);
            // auto is supported; completion below preserves the applied SID
            // even if mpv's default-selection rule temporarily selects the add.
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
                        if (ct.IsCancellationRequested || !added)
                        {
                            Command("sub-remove", sid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            sid = 0;
                        }
                    }
                }
            }
            ct.ThrowIfCancellationRequested();
            return added ? sid : 0;
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
