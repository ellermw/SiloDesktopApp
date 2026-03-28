# Phase 2: Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add media playback to the Continuum Desktop Player using libmpv, supporting direct play of HEVC/AV1/HDR/DolbyVision content with subtitle rendering and full player controls.

**Architecture:** libmpv is loaded via P/Invoke from `libmpv-2.dll`. It renders into a native HWND hosted inside WinUI 3. A PlaybackManager orchestrates the session lifecycle (start/progress/stop) against the Continuum server API. A PlayerPage provides overlay controls.

**Tech Stack:** libmpv (P/Invoke), WinUI 3 + .NET 8, ContinuumApiClient (existing)

---

## Prerequisites

- `libmpv-2.dll` must be in `libs/mpv/` (already downloaded)
- The DLL must be copied to build output on every build

---

## File Structure

```
src/ContinuumPlayer.Player/                    # New project for mpv integration
    MpvInterop.cs                               # P/Invoke declarations for libmpv
    MpvPlayer.cs                                # High-level mpv wrapper
    MpvPlayerHost.cs                            # WinUI control hosting the native window

src/ContinuumPlayer.Core/
    Models/Playback/
        PlaybackStartRequest.cs                 # POST /playback/start body
        PlaybackStartResponse.cs                # Response with session info
        WatchDetailResponse.cs                  # GET /watch/{id} response
        TranscodeStartRequest.cs                # POST /playback/transcode/start
        TranscodeStartResponse.cs               # Transcode response
    Api/
        PlaybackApi.cs                          # Playback API endpoints
    Services/
        PlaybackManager.cs                      # Session lifecycle orchestration

src/ContinuumPlayer/
    Views/
        PlayerPage.xaml                         # Player UI (fills window or fullscreen)
        PlayerPage.xaml.cs
    ViewModels/
        PlayerViewModel.cs                      # Player state management
```

---

### Task 1: ContinuumPlayer.Player Project + libmpv P/Invoke

**Files:**
- Create: `src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj`
- Create: `src/ContinuumPlayer.Player/MpvInterop.cs`

- [ ] **Step 1: Create the Player project**

```bash
cd F:/ContinuumPlayer
dotnet new classlib -n ContinuumPlayer.Player -o src/ContinuumPlayer.Player --framework net8.0-windows10.0.22621.0
dotnet sln add src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj
dotnet add src/ContinuumPlayer/ContinuumPlayer.csproj reference src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj
```

Delete the auto-generated `Class1.cs`.

- [ ] **Step 2: Configure the project to copy libmpv-2.dll to output**

Add to `src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj`:
```xml
<ItemGroup>
    <None Include="..\..\libs\mpv\libmpv-2.dll">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        <Link>libmpv-2.dll</Link>
    </None>
</ItemGroup>
```

- [ ] **Step 3: Create MpvInterop.cs with P/Invoke declarations**

Create `src/ContinuumPlayer.Player/MpvInterop.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ContinuumPlayer.Player;

/// <summary>
/// Raw P/Invoke bindings to libmpv-2.dll.
/// See: https://mpv.io/manual/master/#c-api
/// </summary>
internal static class MpvInterop
{
    private const string LibMpv = "libmpv-2.dll";

    // ── Handle lifecycle ──────────────────────────────
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_destroy(IntPtr ctx);

    // ── Commands ──────────────────────────────────────
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr ctx, IntPtr[] args);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

    // ── Properties ────────────────────────────────────
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        out IntPtr data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out double data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out int data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, ref long data);

    // ── Events & observation ──────────────────────────
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(IntPtr ctx);

    // ── Memory ────────────────────────────────────────
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(IntPtr data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_error_string(int error);

    // ── Format constants ──────────────────────────────
    public const int MPV_FORMAT_NONE = 0;
    public const int MPV_FORMAT_STRING = 1;
    public const int MPV_FORMAT_OSD_STRING = 2;
    public const int MPV_FORMAT_FLAG = 3;
    public const int MPV_FORMAT_INT64 = 4;
    public const int MPV_FORMAT_DOUBLE = 5;
    public const int MPV_FORMAT_NODE = 6;

    // ── Event IDs ─────────────────────────────────────
    public const int MPV_EVENT_NONE = 0;
    public const int MPV_EVENT_SHUTDOWN = 1;
    public const int MPV_EVENT_LOG_MESSAGE = 6;
    public const int MPV_EVENT_END_FILE = 7;
    public const int MPV_EVENT_FILE_LOADED = 8;
    public const int MPV_EVENT_PROPERTY_CHANGE = 22;

    // ── Event structs ─────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr Name;
        public int Format;
        public IntPtr Data;
    }

    // ── Win32 for hosting ─────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const int SW_SHOW = 5;
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Player/ ContinuumPlayer.sln
git commit -m "feat: add ContinuumPlayer.Player project with libmpv P/Invoke bindings"
```

---

### Task 2: MpvPlayer High-Level Wrapper

**Files:**
- Create: `src/ContinuumPlayer.Player/MpvPlayer.cs`

- [ ] **Step 1: Create MpvPlayer.cs**

```csharp
using System.Runtime.InteropServices;

namespace ContinuumPlayer.Player;

/// <summary>
/// High-level wrapper around libmpv. Manages the mpv instance lifecycle,
/// provides Play/Pause/Seek/Volume controls, and raises events for
/// position, duration, and playback state changes.
/// </summary>
public class MpvPlayer : IDisposable
{
    private IntPtr _mpvHandle;
    private IntPtr _videoWindow;
    private Thread? _eventThread;
    private volatile bool _disposed;

    // Property observation reply IDs
    private const ulong REPLY_TIME_POS = 1;
    private const ulong REPLY_DURATION = 2;
    private const ulong REPLY_PAUSE = 3;
    private const ulong REPLY_EOF = 4;
    private const ulong REPLY_IDLE = 5;

    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action? PlaybackEnded;
    public event Action<string>? Error;
    public event Action? FileLoaded;

    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool IsPaused { get; private set; } = true;
    public bool IsPlaying => !IsPaused && !_disposed;

    /// <summary>
    /// Initialize mpv and attach to a parent HWND for video output.
    /// Call this from the UI thread before loading any media.
    /// </summary>
    public void Initialize(IntPtr parentHwnd, int width, int height)
    {
        _mpvHandle = MpvInterop.mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create mpv instance");

        // Create a child window for video rendering
        _videoWindow = MpvInterop.CreateWindowExW(
            0, "Static", "MpvVideo",
            MpvInterop.WS_CHILD | MpvInterop.WS_VISIBLE,
            0, 0, width, height,
            parentHwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_videoWindow == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create video window");

        // Configure mpv options BEFORE initialize
        SetOption("wid", _videoWindow.ToInt64().ToString());
        SetOption("vo", "gpu");
        SetOption("gpu-api", "d3d11");
        SetOption("hwdec", "d3d11va-copy");
        SetOption("keep-open", "yes");
        SetOption("idle", "yes");
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "no");
        SetOption("osc", "no");
        SetOption("osd-level", "0");
        SetOption("force-window", "no");
        SetOption("terminal", "no");
        SetOption("cache", "yes");
        SetOption("demuxer-max-bytes", "150MiB");
        SetOption("demuxer-max-back-bytes", "50MiB");

        int err = MpvInterop.mpv_initialize(_mpvHandle);
        if (err < 0)
            throw new InvalidOperationException($"mpv_initialize failed: {GetError(err)}");

        // Observe properties for events
        MpvInterop.mpv_observe_property(_mpvHandle, REPLY_TIME_POS, "time-pos", MpvInterop.MPV_FORMAT_DOUBLE);
        MpvInterop.mpv_observe_property(_mpvHandle, REPLY_DURATION, "duration", MpvInterop.MPV_FORMAT_DOUBLE);
        MpvInterop.mpv_observe_property(_mpvHandle, REPLY_PAUSE, "pause", MpvInterop.MPV_FORMAT_FLAG);
        MpvInterop.mpv_observe_property(_mpvHandle, REPLY_EOF, "eof-reached", MpvInterop.MPV_FORMAT_FLAG);
        MpvInterop.mpv_observe_property(_mpvHandle, REPLY_IDLE, "idle-active", MpvInterop.MPV_FORMAT_FLAG);

        // Start event processing thread
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "MpvEventLoop" };
        _eventThread.Start();
    }

    /// <summary>
    /// Load and play a media URL.
    /// </summary>
    public void LoadFile(string url, string? authHeader = null)
    {
        if (authHeader != null)
        {
            SetProperty("http-header-fields", $"Authorization: {authHeader}");
        }

        Command("loadfile", url, "replace");
    }

    public void Play() => SetProperty("pause", "no");
    public void Pause() => SetProperty("pause", "yes");
    public void TogglePause() => Command("cycle", "pause");

    public void Seek(double seconds)
    {
        Command("seek", seconds.ToString("F3"), "absolute");
    }

    public void SetVolume(double volume)
    {
        SetProperty("volume", Math.Clamp(volume, 0, 100).ToString("F0"));
    }

    public void SetMute(bool mute)
    {
        SetProperty("mute", mute ? "yes" : "no");
    }

    /// <summary>
    /// Load an external subtitle file/URL.
    /// </summary>
    public void AddSubtitle(string url, string? title = null, string? lang = null)
    {
        if (title != null)
            Command("sub-add", url, "auto", title, lang ?? "");
        else
            Command("sub-add", url, "auto");
    }

    public void SetSubtitleTrack(int index)
    {
        SetProperty("sid", index <= 0 ? "no" : index.ToString());
    }

    public void SetAudioTrack(int index)
    {
        SetProperty("aid", (index + 1).ToString());
    }

    public void ResizeVideoWindow(int width, int height)
    {
        if (_videoWindow != IntPtr.Zero)
            MpvInterop.MoveWindow(_videoWindow, 0, 0, width, height, true);
    }

    // ── Private helpers ──────────────────────────────

    private void SetOption(string name, string value)
    {
        MpvInterop.mpv_set_option_string(_mpvHandle, name, value);
    }

    private void SetProperty(string name, string value)
    {
        MpvInterop.mpv_set_property_string(_mpvHandle, name, value);
    }

    private void Command(params string[] args)
    {
        var pointers = new IntPtr[args.Length + 1];
        var handles = new GCHandle[args.Length];

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(args[i] + "\0");
                handles[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                pointers[i] = handles[i].AddrOfPinnedObject();
            }
            pointers[args.Length] = IntPtr.Zero; // NULL terminator

            MpvInterop.mpv_command(_mpvHandle, pointers);
        }
        finally
        {
            foreach (var h in handles)
                if (h.IsAllocated) h.Free();
        }
    }

    private void EventLoop()
    {
        while (!_disposed)
        {
            var evtPtr = MpvInterop.mpv_wait_event(_mpvHandle, 0.5);
            if (evtPtr == IntPtr.Zero) continue;

            var evt = Marshal.PtrToStructure<MpvInterop.MpvEvent>(evtPtr);

            switch (evt.EventId)
            {
                case MpvInterop.MPV_EVENT_NONE:
                    break;

                case MpvInterop.MPV_EVENT_SHUTDOWN:
                    return;

                case MpvInterop.MPV_EVENT_FILE_LOADED:
                    FileLoaded?.Invoke();
                    break;

                case MpvInterop.MPV_EVENT_END_FILE:
                    PlaybackEnded?.Invoke();
                    break;

                case MpvInterop.MPV_EVENT_PROPERTY_CHANGE:
                    HandlePropertyChange(evt);
                    break;
            }
        }
    }

    private void HandlePropertyChange(MpvInterop.MpvEvent evt)
    {
        if (evt.Data == IntPtr.Zero) return;

        var prop = Marshal.PtrToStructure<MpvInterop.MpvEventProperty>(evt.Data);

        switch (evt.ReplyUserdata)
        {
            case REPLY_TIME_POS when prop.Format == MpvInterop.MPV_FORMAT_DOUBLE:
                Position = Marshal.PtrToStructure<double>(prop.Data);
                PositionChanged?.Invoke(Position);
                break;

            case REPLY_DURATION when prop.Format == MpvInterop.MPV_FORMAT_DOUBLE:
                Duration = Marshal.PtrToStructure<double>(prop.Data);
                DurationChanged?.Invoke(Duration);
                break;

            case REPLY_PAUSE when prop.Format == MpvInterop.MPV_FORMAT_FLAG:
                IsPaused = Marshal.PtrToStructure<int>(prop.Data) != 0;
                PauseChanged?.Invoke(IsPaused);
                break;

            case REPLY_EOF when prop.Format == MpvInterop.MPV_FORMAT_FLAG:
                if (Marshal.PtrToStructure<int>(prop.Data) != 0)
                    PlaybackEnded?.Invoke();
                break;
        }
    }

    private static string GetError(int code)
    {
        var ptr = MpvInterop.mpv_error_string(code);
        return Marshal.PtrToStringUTF8(ptr) ?? $"Error {code}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_mpvHandle != IntPtr.Zero)
        {
            MpvInterop.mpv_wakeup(_mpvHandle);
            _eventThread?.Join(2000);
            MpvInterop.mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
        }

        if (_videoWindow != IntPtr.Zero)
        {
            MpvInterop.DestroyWindow(_videoWindow);
            _videoWindow = IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer.Player/
git commit -m "feat: add MpvPlayer high-level wrapper with Play/Pause/Seek/Volume/Subtitles"
```

---

### Task 3: Playback API Models + Endpoints

**Files:**
- Create: `src/ContinuumPlayer.Core/Models/Playback/PlaybackStartRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Playback/PlaybackStartResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Playback/WatchDetailResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Playback/TranscodeStartRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Playback/TranscodeStartResponse.cs`
- Create: `src/ContinuumPlayer.Core/Api/PlaybackApi.cs`

- [ ] **Step 1: Create playback models**

Create `src/ContinuumPlayer.Core/Models/Playback/PlaybackStartRequest.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Playback;

public class PlaybackStartRequest
{
    public int FileId { get; set; }
    public string ProfileId { get; set; } = "";
    public double? StartPosition { get; set; }
    public int? AudioTrackIndex { get; set; }
    public List<string> CodecsVideo { get; set; } = ["h264", "hevc", "av1", "vp9"];
    public List<string> CodecsAudio { get; set; } = ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"];
    public List<string> Containers { get; set; } = ["mp4", "mkv"];
    public string MaxResolution { get; set; } = "2160p";
    public bool Hdr { get; set; } = true;
}
```

Create `src/ContinuumPlayer.Core/Models/Playback/PlaybackStartResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Playback;

public class PlaybackStartResponse
{
    public string SessionId { get; set; } = "";
    public int MediaFileId { get; set; }
    public string PlayMethod { get; set; } = "";
    public double Position { get; set; }
    public bool IsPaused { get; set; }
    public string StreamUrl { get; set; } = "";
    public int AudioTrackIndex { get; set; }
    public double? DurationSeconds { get; set; }
    public List<SubtitleTrackInfo> SubtitleUrls { get; set; } = [];
    public PlaybackInfo? PlaybackInfo { get; set; }
}

public class SubtitleTrackInfo
{
    public int Index { get; set; }
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string Label { get; set; } = "";
    public string? Source { get; set; }
    public string Url { get; set; } = "";
    public bool Forced { get; set; }
}

public class PlaybackInfo
{
    public string StreamType { get; set; } = "";
    public bool TranscodeAudio { get; set; }
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
}
```

Create `src/ContinuumPlayer.Core/Models/Playback/WatchDetailResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Playback;

public class WatchDetailResponse
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public List<FileVersion> Versions { get; set; } = [];
    public List<SubtitleInfo> Subtitles { get; set; } = [];
    public WatchUserData? UserData { get; set; }
    public TimeRange? Intro { get; set; }
    public TimeRange? Credits { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EffectiveSubtitleLanguage { get; set; }
    public string? EffectiveSubtitleMode { get; set; }
    public bool? EffectiveShowForcedSubtitles { get; set; }
}

public class FileVersion
{
    public int FileId { get; set; }
    public string? FileName { get; set; }
    public string Resolution { get; set; } = "";
    public string CodecVideo { get; set; } = "";
    public string CodecAudio { get; set; } = "";
    public bool Hdr { get; set; }
    public string Container { get; set; } = "";
    public long FileSize { get; set; }
    public double Duration { get; set; }
    public int Bitrate { get; set; }
    public int? AudioChannels { get; set; }
    public List<AudioTrack>? AudioTracks { get; set; }
}

public class AudioTrack
{
    public string? Title { get; set; }
    public string? EmbeddedTitle { get; set; }
    public string? Language { get; set; }
    public string? Codec { get; set; }
    public string? Layout { get; set; }
    public int? Channels { get; set; }
    public bool Default { get; set; }
}

public class SubtitleInfo
{
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string? Title { get; set; }
    public string Source { get; set; } = "";
    public bool Forced { get; set; }
}

public class WatchUserData
{
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public bool Played { get; set; }
    public int? LastFileId { get; set; }
    public string? LastResolution { get; set; }
    public bool? LastHdr { get; set; }
    public string? LastCodecVideo { get; set; }
}

public class TimeRange
{
    public double Start { get; set; }
    public double End { get; set; }
}
```

Create `src/ContinuumPlayer.Core/Models/Playback/TranscodeStartRequest.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Playback;

public class TranscodeStartRequest
{
    public string SessionId { get; set; } = "";
    public double SeekSeconds { get; set; }
    public string TargetResolution { get; set; } = "1080p";
    public string TargetCodecVideo { get; set; } = "h264";
    public string TargetCodecAudio { get; set; } = "aac";
    public int TargetBitrateKbps { get; set; } = 8000;
    public int SegmentDuration { get; set; } = 2;
    public int SubtitleTrackIndex { get; set; } = -1;
    public bool SubtitleBurnIn { get; set; }
}
```

Create `src/ContinuumPlayer.Core/Models/Playback/TranscodeStartResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Playback;

public class TranscodeStartResponse
{
    public string SessionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string ManifestUrl { get; set; } = "";
    public double DurationSeconds { get; set; }
    public double PlayerStartSeconds { get; set; }
    public bool CanSeekAnywhere { get; set; }
    public int? SwitchedFileId { get; set; }
}
```

- [ ] **Step 2: Create PlaybackApi.cs**

Create `src/ContinuumPlayer.Core/Api/PlaybackApi.cs`:
```csharp
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Core.Api;

public class PlaybackApi(ContinuumApiClient client)
{
    public Task<WatchDetailResponse> GetWatchDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<WatchDetailResponse>($"/api/v1/watch/{contentId}", ct);

    public Task<PlaybackStartResponse> StartPlaybackAsync(PlaybackStartRequest request, CancellationToken ct = default)
        => client.PostAsync<PlaybackStartResponse>("/api/v1/playback/start", request, ct);

    public Task ReportProgressAsync(string sessionId, double position, bool isPaused, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/playback/{sessionId}/progress",
            new { position, is_paused = isPaused }, ct);

    public Task StopPlaybackAsync(string sessionId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/playback/{sessionId}", ct);

    public Task<TranscodeStartResponse> StartTranscodeAsync(TranscodeStartRequest request, CancellationToken ct = default)
        => client.PostAsync<TranscodeStartResponse>("/api/v1/playback/transcode/start", request, ct);

    public Task<ChangeAudioResponse> ChangeAudioTrackAsync(string sessionId, int trackIndex, double position, CancellationToken ct = default)
        => client.PatchAsync<ChangeAudioResponse>($"/api/v1/playback/{sessionId}/audio",
            new { audio_track_index = trackIndex, position }, ct);
}

public class ChangeAudioResponse
{
    public int AudioTrackIndex { get; set; }
    public string PlayMethod { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string SwitchMode { get; set; } = "";
    public PlaybackInfo? PlaybackInfo { get; set; }
}
```

- [ ] **Step 3: Register PlaybackApi in DI**

In `src/ContinuumPlayer/App.xaml.cs`, add:
```csharp
services.AddSingleton<PlaybackApi>(sp => new PlaybackApi(sp.GetRequiredService<ContinuumApiClient>()));
```

Create the `Models/Playback` directory:
```bash
mkdir -p src/ContinuumPlayer.Core/Models/Playback
```

- [ ] **Step 4: Verify build**

Run: `dotnet build ContinuumPlayer.sln`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Core/Models/Playback/ src/ContinuumPlayer.Core/Api/PlaybackApi.cs src/ContinuumPlayer/App.xaml.cs
git commit -m "feat: add playback API models and endpoints"
```

---

### Task 4: PlaybackManager (Session Orchestrator)

**Files:**
- Create: `src/ContinuumPlayer.Core/Services/PlaybackManager.cs`

- [ ] **Step 1: Create PlaybackManager.cs**

```csharp
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Core.Services;

/// <summary>
/// Manages the playback session lifecycle: start, progress reporting, stop.
/// Sits between the player UI and the API layer.
/// </summary>
public class PlaybackManager : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;
    private Timer? _progressTimer;
    private string? _sessionId;
    private double _lastReportedPosition;
    private bool _isPaused;

    public PlaybackManager(PlaybackApi playbackApi, AuthService authService, ContinuumApiClient apiClient)
    {
        _playbackApi = playbackApi;
        _authService = authService;
        _apiClient = apiClient;
    }

    public string? SessionId => _sessionId;
    public WatchDetailResponse? WatchDetail { get; private set; }
    public PlaybackStartResponse? CurrentSession { get; private set; }
    public string? StreamUrl { get; private set; }

    /// <summary>
    /// Fetch watch details for a content item.
    /// </summary>
    public async Task<WatchDetailResponse> GetWatchDetailAsync(string contentId, CancellationToken ct = default)
    {
        WatchDetail = await _playbackApi.GetWatchDetailAsync(contentId, ct);
        return WatchDetail;
    }

    /// <summary>
    /// Select the best version from available versions based on quality preference.
    /// </summary>
    public FileVersion? SelectBestVersion(List<FileVersion> versions, string? qualityPreference = null)
    {
        if (versions.Count == 0) return null;

        // Prefer highest resolution with HDR, then fall back
        var sorted = versions
            .OrderByDescending(v => ResolutionRank(v.Resolution))
            .ThenByDescending(v => v.Hdr)
            .ThenByDescending(v => v.Bitrate)
            .ToList();

        if (qualityPreference != null && qualityPreference != "auto")
        {
            var match = sorted.FirstOrDefault(v =>
                v.Resolution.Equals(qualityPreference, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return sorted.First();
    }

    /// <summary>
    /// Start a playback session with the server.
    /// </summary>
    public async Task<PlaybackStartResponse> StartSessionAsync(int fileId, double startPosition = 0, CancellationToken ct = default)
    {
        var request = new PlaybackStartRequest
        {
            FileId = fileId,
            ProfileId = _authService.SelectedProfileId ?? "",
            StartPosition = startPosition > 0 ? startPosition : null,
        };

        var response = await _playbackApi.StartPlaybackAsync(request, ct);
        _sessionId = response.SessionId;
        CurrentSession = response;

        // Build the full stream URL
        var baseUrl = _apiClient.BaseUrl;
        var streamPath = response.StreamUrl;
        var token = _apiClient.AccessToken;

        var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
        if (token != null)
            url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

        // For remux with start position, add seek param
        if (response.PlayMethod == "remux" && response.Position > 0)
            url += $"&seek={response.Position:F3}";

        StreamUrl = url;

        // Start progress reporting
        StartProgressReporting();

        return response;
    }

    /// <summary>
    /// Report current playback position to the server.
    /// </summary>
    public void UpdatePosition(double position, bool isPaused)
    {
        _lastReportedPosition = position;
        _isPaused = isPaused;
    }

    /// <summary>
    /// Stop the current playback session.
    /// </summary>
    public async Task StopSessionAsync()
    {
        StopProgressReporting();

        if (_sessionId != null)
        {
            try
            {
                await _playbackApi.StopPlaybackAsync(_sessionId);
            }
            catch { /* best effort */ }

            _sessionId = null;
            CurrentSession = null;
            StreamUrl = null;
        }
    }

    /// <summary>
    /// Build full subtitle URLs with auth token.
    /// </summary>
    public List<(SubtitleTrackInfo Track, string FullUrl)> GetSubtitleUrls()
    {
        if (CurrentSession == null) return [];

        var baseUrl = _apiClient.BaseUrl;
        var token = _apiClient.AccessToken;

        return CurrentSession.SubtitleUrls.Select(s =>
        {
            var url = s.Url.StartsWith("http") ? s.Url : $"{baseUrl}{s.Url}";
            if (token != null)
                url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
            return (s, url);
        }).ToList();
    }

    private void StartProgressReporting()
    {
        StopProgressReporting();
        _progressTimer = new Timer(async _ =>
        {
            if (_sessionId == null) return;
            try
            {
                await _playbackApi.ReportProgressAsync(_sessionId, _lastReportedPosition, _isPaused);
            }
            catch { /* best effort -- session will timeout if this fails repeatedly */ }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7));
    }

    private void StopProgressReporting()
    {
        _progressTimer?.Dispose();
        _progressTimer = null;
    }

    private static int ResolutionRank(string res) => res?.ToLower() switch
    {
        "2160p" or "4k" => 4,
        "1440p" => 3,
        "1080p" => 2,
        "720p" => 1,
        "480p" => 0,
        _ => -1
    };

    public void Dispose()
    {
        StopProgressReporting();
        _ = StopSessionAsync();
    }
}
```

- [ ] **Step 2: Register in DI**

In `App.xaml.cs`, add:
```csharp
services.AddTransient<PlaybackManager>();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build ContinuumPlayer.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/PlaybackManager.cs src/ContinuumPlayer/App.xaml.cs
git commit -m "feat: add PlaybackManager for session lifecycle"
```

---

### Task 5: PlayerPage UI

**Files:**
- Create: `src/ContinuumPlayer/Views/PlayerPage.xaml`
- Create: `src/ContinuumPlayer/Views/PlayerPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/PlayerViewModel.cs`

This is the biggest task. The PlayerPage fills the app window with video and has overlay controls that appear on mouse move.

- [ ] **Step 1: Create PlayerViewModel.cs**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly PlaybackManager _playbackManager;

    public PlayerViewModel(PlaybackManager playbackManager)
    {
        _playbackManager = playbackManager;
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _controlsVisible = true;
    [ObservableProperty] private bool _showSkipIntro;
    [ObservableProperty] private bool _showSkipCredits;
    [ObservableProperty] private string _playMethod = "";
    [ObservableProperty] private string _videoCodec = "";
    [ObservableProperty] private string _resolution = "";

    public ObservableCollection<SubtitleTrackInfo> SubtitleTracks { get; } = [];
    public ObservableCollection<FileVersion> Versions { get; } = [];

    public WatchDetailResponse? WatchDetail => _playbackManager.WatchDetail;
    public PlaybackManager Manager => _playbackManager;
    public TimeRange? Intro => WatchDetail?.Intro;
    public TimeRange? Credits => WatchDetail?.Credits;

    public string PositionDisplay => FormatTime(Position);
    public string DurationDisplay => FormatTime(Duration);
    public double SeekMax => Duration > 0 ? Duration : 1;

    public void UpdatePosition(double pos)
    {
        Position = pos;
        _playbackManager.UpdatePosition(pos, !IsPlaying);
        OnPropertyChanged(nameof(PositionDisplay));

        // Check skip markers
        if (Intro != null)
            ShowSkipIntro = pos >= Intro.Start && pos < Intro.End;
        if (Credits != null)
            ShowSkipCredits = pos >= Credits.Start && pos < Credits.End;
    }

    public void UpdateDuration(double dur)
    {
        Duration = dur;
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(SeekMax));
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0) return "0:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
```

- [ ] **Step 2: Create PlayerPage.xaml**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="ContinuumPlayer.Views.PlayerPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="Black"
    KeyDown="Page_KeyDown"
    PointerMoved="Page_PointerMoved"
    Loaded="Page_Loaded"
    Unloaded="Page_Unloaded">

    <Grid>
        <!-- Video host panel (mpv renders here) -->
        <Border x:Name="VideoHost" Background="Black" />

        <!-- Loading overlay -->
        <Grid x:Name="LoadingOverlay" Background="#CC000000">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
                <ProgressRing IsActive="True" Width="48" Height="48" />
                <TextBlock Text="Preparing playback..." Foreground="White" HorizontalAlignment="Center" />
            </StackPanel>
        </Grid>

        <!-- Error overlay -->
        <Grid x:Name="ErrorOverlay" Background="#CC000000" Visibility="Collapsed">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
                <TextBlock x:Name="ErrorText" Foreground="#EF6B73" FontSize="16"
                    HorizontalAlignment="Center" TextWrapping="Wrap" MaxWidth="400" />
                <Button Content="Go Back" Click="BackButton_Click"
                    Style="{StaticResource AccentButtonStyle}" HorizontalAlignment="Center" />
            </StackPanel>
        </Grid>

        <!-- Skip Intro button -->
        <Button x:Name="SkipIntroButton" Content="Skip Intro"
            Visibility="Collapsed"
            Style="{StaticResource AccentButtonStyle}"
            HorizontalAlignment="Right" VerticalAlignment="Bottom"
            Margin="0,0,24,100" Padding="20,10"
            Click="SkipIntro_Click" />

        <!-- Skip Credits button -->
        <Button x:Name="SkipCreditsButton" Content="Skip Credits"
            Visibility="Collapsed"
            Style="{StaticResource AccentButtonStyle}"
            HorizontalAlignment="Right" VerticalAlignment="Bottom"
            Margin="0,0,24,100" Padding="20,10"
            Click="SkipCredits_Click" />

        <!-- Controls overlay (shown on mouse move, hidden after idle) -->
        <Grid x:Name="ControlsOverlay" VerticalAlignment="Bottom" Opacity="0">
            <!-- Gradient behind controls -->
            <Border VerticalAlignment="Bottom" Height="200">
                <Border.Background>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                        <GradientStop Color="Transparent" Offset="0.0" />
                        <GradientStop Color="#CC000000" Offset="1.0" />
                    </LinearGradientBrush>
                </Border.Background>
            </Border>

            <StackPanel VerticalAlignment="Bottom" Margin="24,0,24,16" Spacing="8">
                <!-- Title -->
                <TextBlock x:Name="TitleText" Foreground="White" FontSize="16" FontWeight="SemiBold" />

                <!-- Seek bar -->
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock x:Name="PositionText" Grid.Column="0" Foreground="White" FontSize="12"
                        VerticalAlignment="Center" Margin="0,0,8,0" />
                    <Slider x:Name="SeekSlider" Grid.Column="1"
                        Minimum="0" StepFrequency="1"
                        ValueChanged="SeekSlider_ValueChanged" />
                    <TextBlock x:Name="DurationText" Grid.Column="2" Foreground="White" FontSize="12"
                        VerticalAlignment="Center" Margin="8,0,0,0" />
                </Grid>

                <!-- Control buttons row -->
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <!-- Left: Play/Pause + Volume -->
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8">
                        <Button x:Name="PlayPauseButton" Click="PlayPause_Click"
                            Style="{StaticResource GhostButtonStyle}" Width="40" Height="40" Padding="0">
                            <FontIcon x:Name="PlayPauseIcon" Glyph="&#xE768;" FontSize="20" Foreground="White" />
                        </Button>
                        <Slider x:Name="VolumeSlider" Width="100" Minimum="0" Maximum="100" Value="100"
                            ValueChanged="VolumeSlider_ValueChanged" VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Center: Playback info -->
                    <TextBlock x:Name="PlaybackInfoText" Grid.Column="1" Foreground="#90A0B5"
                        FontSize="11" HorizontalAlignment="Center" VerticalAlignment="Center" />

                    <!-- Right: Subs + Fullscreen -->
                    <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4">
                        <Button x:Name="SubtitleButton" Style="{StaticResource GhostButtonStyle}"
                            Width="40" Height="40" Padding="0">
                            <FontIcon Glyph="&#xED1E;" FontSize="18" Foreground="White" />
                            <Button.Flyout>
                                <MenuFlyout x:Name="SubtitleFlyout" />
                            </Button.Flyout>
                        </Button>
                        <Button x:Name="FullscreenButton" Click="Fullscreen_Click"
                            Style="{StaticResource GhostButtonStyle}" Width="40" Height="40" Padding="0">
                            <FontIcon x:Name="FullscreenIcon" Glyph="&#xE740;" FontSize="18" Foreground="White" />
                        </Button>
                        <Button Click="BackButton_Click"
                            Style="{StaticResource GhostButtonStyle}" Width="40" Height="40" Padding="0">
                            <FontIcon Glyph="&#xE711;" FontSize="18" Foreground="White" />
                        </Button>
                    </StackPanel>
                </Grid>
            </StackPanel>
        </Grid>
    </Grid>
</Page>
```

- [ ] **Step 3: Create PlayerPage.xaml.cs**

This is a large file. It handles: mpv initialization, loading media, controls visibility, keyboard shortcuts, seek slider, subtitles, fullscreen toggle.

Create `src/ContinuumPlayer/Views/PlayerPage.xaml.cs` with the full implementation that:
- On Loaded: initializes MpvPlayer, gets the HWND from VideoHost, starts playback session
- Handles position/duration updates from MpvPlayer events (marshal to UI thread)
- Shows/hides controls overlay on mouse movement with 3-second idle timeout
- Keyboard: Space=play/pause, Left/Right=seek 10s, Up/Down=volume, F=fullscreen, Escape=exit
- Seek slider two-way binding
- Subtitle flyout populated from session subtitle tracks
- Progress reporting via PlaybackManager
- On Unloaded: stops session, disposes player

- [ ] **Step 4: Register PlayerViewModel in DI**

In `App.xaml.cs`: `services.AddTransient<PlayerViewModel>();`

- [ ] **Step 5: Wire Play buttons to navigate to PlayerPage**

In `ItemDetailPage.xaml.cs`, find the Play button click handler and navigate:
```csharp
var nav = App.Services.GetRequiredService<NavigationService>();
nav.Navigate<PlayerPage>(ViewModel.Item.ContentId);
```

- [ ] **Step 6: Verify build**

Run: `dotnet build ContinuumPlayer.sln`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/ContinuumPlayer/Views/PlayerPage.* src/ContinuumPlayer/ViewModels/PlayerViewModel.cs
git commit -m "feat: add PlayerPage with mpv playback, controls, and subtitle support"
```

---

### Task 6: Integration Test

- [ ] **Step 1: Verify full solution builds**

Run: `dotnet build ContinuumPlayer.sln`

- [ ] **Step 2: Run all tests**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests`

- [ ] **Step 3: Launch and test playback**

1. Launch app, login, navigate to a movie
2. Click Play
3. Verify: video plays, controls appear on mouse move, seek works, Space pauses
4. Verify: pressing Escape exits player

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 2 - media playback with libmpv"
```
