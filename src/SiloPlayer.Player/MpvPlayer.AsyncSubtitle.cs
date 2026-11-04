using System.Runtime.InteropServices;
using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    private readonly object _asyncSubtitleGate = new();
    private readonly Dictionary<ulong, TaskCompletionSource<bool>> _asyncSubtitleCommands = [];
    private ulong _asyncSubtitleCommandId;

    /// <summary>
    /// Downloads through mpv's existing URL reader without blocking the caller
    /// or selecting the result. The file/selection owner must be checked again
    /// before this SID is cached or selected.
    /// </summary>
    public async Task<int> AddSubtitleUnselectedAsync(string url, string? title, string? language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong id;
        lock (_asyncSubtitleGate)
        {
            ThrowIfNotInitialized();
            id = ++_asyncSubtitleCommandId;
            _asyncSubtitleCommands.Add(id, completion);
            var args = new[] { "sub-add", url, "auto", title ?? "", language ?? "" };
            var pointers = new IntPtr[args.Length + 1];
            try
            {
                for (var i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
                using var lease = EnterNative();
                // mpv copies the arguments before returning. Only submission,
                // not the network read, owns this lock/native lease.
                if (!lease.Active || mpv_command_async(_mpvHandle, id, pointers) < 0)
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
        using var cancellation = ct.Register(() =>
        {
            using var lease = EnterNative();
            if (lease.Active) mpv_abort_async_command(_mpvHandle, id);
        });
        try
        {
            var added = await completion.Task.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return added ? FindExternalSubtitleSid(url) : 0;
        }
        finally
        {
            lock (_asyncSubtitleGate) _asyncSubtitleCommands.Remove(id);
        }
    }

    private int FindExternalSubtitleSid(string url)
    {
        using var lease = EnterNative();
        if (!lease.Active || mpv_get_property_int(_mpvHandle, "track-list/count", MPV_FORMAT_INT64, out var count) < 0) return 0;
        var sid = 0;
        for (var i = 0; i < count; i++)
        {
            var prefix = $"track-list/{i}/";
            if (Read(prefix + "type") == "sub" && Read(prefix + "external") == "yes" &&
                Read(prefix + "external-filename") == url &&
                mpv_get_property_int(_mpvHandle, prefix + "id", MPV_FORMAT_INT64, out var trackId) >= 0 && trackId > sid)
                sid = checked((int)trackId);
        }
        return sid;

        string? Read(string name)
        {
            var pointer = mpv_get_property_string(_mpvHandle, name);
            try { return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer); }
            finally { if (pointer != IntPtr.Zero) mpv_free(pointer); }
        }
    }

    private void CompleteAsyncSubtitleCommand(MpvEvent reply)
    {
        lock (_asyncSubtitleGate)
            if (_asyncSubtitleCommands.Remove(reply.ReplyUserdata, out var completion))
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
