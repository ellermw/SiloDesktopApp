using System.Runtime.InteropServices;

namespace SiloPlayer.Helpers;

internal static class AppUpdateNotice
{
    internal static void Show() => MessageBox(IntPtr.Zero, "Silo is installing an update. Please try opening it again shortly.", "Silo update", 0x40);
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
