using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

/// <summary>Verify only the fixture's current UI thread; never inject system input.</summary>
internal static class PlaybackMenuKeyboardNativeFixture
{
    internal static void Verify(object menu)
    {
        var type = menu.GetType();
        while (type.GetMethod("HandleKeyboardInput", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            type = type.BaseType ?? throw new InvalidOperationException("Native keyboard bridge missing.");
        var dispatch = type.GetMethod("HandleKeyboardInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var map = type.GetMethod("KeyboardAction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var list = (ListView)type.GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var hwnd = (IntPtr)type.GetProperty("Hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var thread = GetCurrentThreadId();
        Require(GetWindowThreadProcessId(hwnd, out var process) == thread && process == (uint)Environment.ProcessId,
            "Keyboard fixture target is outside its own process/UI thread.");
        var saved = new byte[256];
        Require(GetKeyboardState(saved), "Cannot save this UI thread's keyboard state.");
        var initialSelection = list.SelectedIndex;
        try
        {
            var local = (byte[])saved.Clone();
            foreach (var shift in new[] { true, false })
            {
                // SetKeyboardState changes only the calling thread's state. No
                // SendInput, global key event, foreground or external HWND call.
                foreach (var key in new[] { 0x10, 0xA0, 0xA1 })
                    local[key] = (byte)((local[key] & 0x7F) | (shift ? 0x80 : 0));
                Require(SetKeyboardState(local), "Cannot set isolated UI-thread modifiers.");
                list.SelectedIndex = 1;
                Require(dispatch.Invoke(menu, [VirtualKey.Tab]) is true, "Native Tab was not consumed.");
                Require(list.SelectedIndex == (shift ? 0 : 2), "Native Shift+Tab/Tab moved in the wrong direction.");
            }
            var before = list.SelectedIndex;
            Require(dispatch.Invoke(menu, [VirtualKey.A]) is false && list.SelectedIndex == before,
                "Unrelated native key changed menu selection.");
            foreach (var (key, shift, expected) in new (VirtualKey, bool, string)[]
            {
                (VirtualKey.Tab, true, "up"), (VirtualKey.Tab, false, "down"),
                (VirtualKey.Up, true, "up"), (VirtualKey.Down, true, "down"),
                (VirtualKey.Home, false, "home"), (VirtualKey.End, true, "end"),
                (VirtualKey.Escape, false, "cancel"), (VirtualKey.Enter, true, "activate"),
                (VirtualKey.GamepadB, true, "cancel"), (VirtualKey.GamepadA, false, "activate"),
            })
                Require(Equals(map.Invoke(null, [key, shift]), expected), $"Keyboard mapping changed for {key}/{shift}.");
        }
        finally
        {
            Require(GetCurrentThreadId() == thread && SetKeyboardState(saved), "Cannot restore the fixture UI thread's keyboard state.");
            list.SelectedIndex = initialSelection;
        }
        Program.Log("PASS: actual native menu keyboard bridge reads isolated UI-thread Shift state: Shift+Tab moves back, Tab forward, unrelated keys stay unhandled, existing navigation/activation mappings preserved. No physical/global input asserted.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
}
