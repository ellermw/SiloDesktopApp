using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("SiloPlayer.Tests")]

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    private DirectOpen? _directOpen;
    private DirectRequest? _directRequest;
    private double _directDuration;
    internal Func<HttpMessageHandler>? DirectHttpHandlerFactory { get; set; }
    private sealed class DirectRequest(string url)
    {
        public readonly string Key = "silo-direct://" + Guid.NewGuid().ToString("N");
        public readonly string Url = url;
        public volatile bool Failed;
        public int FailureReported;
        public DirectHttpReader? Reader;
    }

    private bool ReportDirectStreamFailure()
    {
        var request = Volatile.Read(ref _directRequest);
        if (request?.Failed != true) return false;
        if (Interlocked.Exchange(ref request.FailureReported, 1) == 0)
            InvokeSafely(DirectStreamError, "Direct stream could not recover its byte reads.", nameof(DirectStreamError));
        return true;
    }

    private string PrepareNativeDirectInput(string url)
    {
        if (_directOpen == null)
        {
            _directOpen = OpenDirect;
            if (mpv_stream_cb_add_ro(_mpvHandle, "silo-direct", IntPtr.Zero, _directOpen) < 0)
                throw new InvalidOperationException("Cannot register direct file reader.");
        }
        var request = new DirectRequest(url);
        Volatile.Write(ref _directRequest, request);
        Volatile.Write(ref _directDuration, 0);
        return request.Key;
    }

    private int OpenDirect(IntPtr userData, IntPtr uri, IntPtr info)
    {
        try
        {
            var request = Volatile.Read(ref _directRequest);
            if (request == null || Marshal.PtrToStringUTF8(uri) != request.Key) return -13;
            var reader = new DirectHttpReader(request.Url, () => Volatile.Read(ref _directDuration), DirectHttpHandlerFactory, _diagnostics);
            Volatile.Write(ref request.Reader, reader);
            var context = new DirectContext(reader, request);
            var handle = GCHandle.Alloc(context);
            Marshal.StructureToPtr(new DirectInfo
            {
                Cookie = GCHandle.ToIntPtr(handle),
                Read = Marshal.GetFunctionPointerForDelegate(DirectReadCallback),
                Seek = Marshal.GetFunctionPointerForDelegate(DirectSeekCallback),
                Size = Marshal.GetFunctionPointerForDelegate(DirectSizeCallback),
                Close = Marshal.GetFunctionPointerForDelegate(DirectCloseCallback),
                Cancel = Marshal.GetFunctionPointerForDelegate(DirectCancelCallback),
            }, info, false);
            return 0;
        }
        catch { return -13; }
    }

    private sealed class DirectContext(DirectHttpReader reader, DirectRequest request)
    {
        public readonly DirectHttpReader Reader = reader;
        public readonly byte[] Buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        public long Fail() { if (!Reader.IsCanceled) request.Failed = true; return -1; }
    }
    private static DirectContext Context(IntPtr cookie) => (DirectContext)GCHandle.FromIntPtr(cookie).Target!;
    // Root every callback for the full native stream lifetime. Callbacks never
    // call mpv: only its own worker thread consumes these reads and seeks.
    private static readonly DirectRead DirectReadCallback = (cookie, buffer, size) =>
    {
        var context = Context(cookie);
        try
        {
            var count = context.Reader.Read(context.Buffer, (int)Math.Min(size, (ulong)context.Buffer.Length));
            Marshal.Copy(context.Buffer, 0, buffer, count);
            return count;
        }
        catch { return context.Fail(); }
    };
    private static readonly DirectSeek DirectSeekCallback = (cookie, position) =>
    {
        var context = Context(cookie);
        try { return context.Reader.Seek(position); } catch { return context.Fail(); }
    };
    private static readonly DirectSize DirectSizeCallback = cookie =>
    {
        var context = Context(cookie);
        try { return context.Reader.Length; } catch { return context.Fail(); }
    };
    private static readonly DirectClose DirectCloseCallback = cookie =>
    {
        var handle = GCHandle.FromIntPtr(cookie);
        var context = (DirectContext)handle.Target!;
        try { context.Reader.Dispose(); }
        finally { ArrayPool<byte>.Shared.Return(context.Buffer); handle.Free(); }
    };
    private static readonly DirectClose DirectCancelCallback = cookie => Context(cookie).Reader.Cancel();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DirectOpen(IntPtr data, IntPtr uri, IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long DirectRead(IntPtr cookie, IntPtr buffer, ulong size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long DirectSeek(IntPtr cookie, long offset);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long DirectSize(IntPtr cookie);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DirectClose(IntPtr cookie);
    [StructLayout(LayoutKind.Sequential)] private struct DirectInfo
    {
        public IntPtr Cookie, Read, Seek, Size, Close, Cancel;
    }
    [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_stream_cb_add_ro(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string protocol, IntPtr data, DirectOpen open);
}
