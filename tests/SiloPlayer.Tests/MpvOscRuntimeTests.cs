using System.Runtime.InteropServices;

namespace SiloPlayer.Tests;

public sealed class MpvOscRuntimeTests
{
    [Fact]
    public void BundledLibMpv_LoadsOscWithoutLuaErrors()
    {
        var root = FindRepositoryRoot();
        var libraryPath = Path.Combine(root, "libs", "mpv", "libmpv-2.dll");
        var scriptPath = Path.Combine(root, "libs", "mpv", "scripts", "silo-osc.lua");
        var logPath = Path.Combine(Path.GetTempPath(), $"silo-osc-probe-{Guid.NewGuid():N}.log");

        Assert.True(File.Exists(libraryPath), $"Missing bundled libmpv: {libraryPath}");
        Assert.True(File.Exists(scriptPath), $"Missing OSC script: {scriptPath}");

        var library = NativeLibrary.Load(libraryPath);
        var create = LoadDelegate<MpvCreate>(library, "mpv_create");
        var setOption = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
        var initialize = LoadDelegate<MpvInitialize>(library, "mpv_initialize");
        var terminate = LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy");
        var handle = create();
        Assert.NotEqual(IntPtr.Zero, handle);

        try
        {
            Set("vo", "null");
            Set("ao", "null");
            Set("idle", "yes");
            Set("osc", "no");
            Set("log-file", logPath);
            Set("msg-level", "all=v");
            Set("scripts", scriptPath);

            Assert.True(initialize(handle) >= 0, "The bundled libmpv instance did not initialize.");
            Thread.Sleep(350);
        }
        finally
        {
            terminate(handle);
            NativeLibrary.Free(library);
        }

        try
        {
            var log = File.ReadAllText(logPath);
            Assert.Contains("silo-osc.lua", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("syntax error", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("error loading script", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stack traceback", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(logPath);
        }

        void Set(string name, string value) =>
            Assert.True(setOption(handle, name, value) >= 0, $"libmpv rejected {name}={value}");
    }

    private static T LoadDelegate<T>(IntPtr library, string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SiloPlayer repository root.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MpvCreate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvSetOptionString(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvInitialize(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpvTerminateDestroy(IntPtr handle);
}
