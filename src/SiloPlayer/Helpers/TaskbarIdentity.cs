using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Helpers;

/// <summary>One desktop identity, independent of server branding and version-directory paths.</summary>
internal static class TaskbarIdentity
{
    internal const string AppId = "Silo.DesktopPlayer";

    internal static void InitializeProcess()
    {
        try { Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId)); }
        catch (Exception ex) { LocalLog.AppendException("shell.txt", ex, "taskbar_process_identity"); }
    }

    internal static void ApplyToWindow(Window window)
    {
        // The window icon always comes from the app, never from a signed-in server.
        try { window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); }
        catch (Exception ex) { LocalLog.AppendException("shell.txt", ex, "taskbar_window_icon"); }

        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
            var launch = CreateLaunchInfo(executable, UpdateInstallation.FindRegisteredRoot(executable));
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            // Closed is raised for the accepted close, while the HWND is still valid;
            // a canceled AppWindow.Closing must retain the identity of the open app.
            window.Closed += (_, _) => ClearWindowProperties(hwnd);
            var iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
            try
            {
                // Relaunch properties precede the explicit window ID. Pins must target the
                // stable launcher so removing an old immutable version cannot break them.
                SetString(store, 2, "\"" + launch.Executable + "\"");
                SetString(store, 4, "Silo Desktop Player");
                SetString(store, 3, launch.Icon + ",0");
                SetString(store, 5, AppId);
                store.Commit();
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (Exception ex) { LocalLog.AppendException("shell.txt", ex, "taskbar_window_identity"); }
    }

    internal static LaunchInfo CreateLaunchInfo(string executable, string? registeredRoot)
    {
        executable = Path.GetFullPath(executable);
        var folder = Path.GetDirectoryName(executable)!;
        var icon = Path.Combine(folder, "Assets", "app.ico");
        if (registeredRoot is not null && UpdateInstallation.MatchRegisteredRoot(executable, [registeredRoot]) is { } root)
        {
            var launcher = Path.Combine(root, "SiloPlayer.exe");
            if (File.Exists(launcher))
            {
                executable = launcher;
                var stableIcon = Path.Combine(root, "Assets", "app.ico");
                if (File.Exists(stableIcon)) icon = stableIcon;
            }
        }
        return new(executable, icon);
    }

    internal sealed record LaunchInfo(string Executable, string Icon);

    private static void ClearWindowProperties(IntPtr hwnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
            try
            {
                foreach (var id in new uint[] { 2, 3, 4, 5 })
                {
                    var key = new PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id };
                    var empty = new PropertyVariant(); // VT_EMPTY releases the window-owned value.
                    store.SetValue(ref key, ref empty);
                }
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (Exception ex) { LocalLog.AppendException("shell.txt", ex, "taskbar_window_identity_clear"); }
    }

    private static void SetString(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id };
        var value = new PropertyVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) }; // VT_LPWSTR
        try { store.SetValue(ref key, ref value); }
        finally { PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    // x64 PROPVARIANT includes a 16-byte union following its 8-byte header.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropertyVariant value);
        void SetValue(ref PropertyKey key, ref PropertyVariant value);
        void Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropertyVariant value);
}
