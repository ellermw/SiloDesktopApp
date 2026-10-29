using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class TaskbarIdentityNativeFixture
{
    internal static async Task RunAsync()
    {
        const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
        var servicesField = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = servicesField.GetValue(null);
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://taskbar-fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "settings"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; local.Save(prefs);
        await using var provider = new ServiceCollection()
            .AddSingleton(api).AddSingleton(auth).AddSingleton(local).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        servicesField.SetValue(null, provider);
        MainWindow? window = null;
        try
        {
            window = new MainWindow(); // Never show or activate this synthetic window.
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", instance)!.CreateDelegate(typeof(RoutedEventHandler), window);
            typeof(MainWindow).GetField("_hydratedShellKey", instance)!.SetValue(window, "fixture");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var before = IconFingerprint(hwnd);
            var failures = new List<string>();
            if (ReadProperty(hwnd, 5) != "Silo.DesktopPlayer") failures.Add("Running window has no stable Silo AppUserModelID.");
            if (ReadProperty(hwnd, 2) != "\"" + Environment.ProcessPath + "\"") failures.Add("Unregistered/development relaunch must retain its own executable.");
            if (ReadProperty(hwnd, 4) != "Silo Desktop Player") failures.Add("Pinned display name is not Silo Desktop Player.");
            if (ReadProperty(hwnd, 3) != Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Assets", "app.ico") + ",0")
                failures.Add("Pinned icon is not the bundled Silo icon.");
            var load = typeof(MainWindow).GetMethod("LoadShellBrandingAsync", instance)!;
            foreach (var server in new[] { "tavern", "different-server" })
            {
                wire.Server = server;
                await (Task)load.Invoke(window, ["fixture", CancellationToken.None])!;
                await Task.Delay(450); // Drain the old fire-and-forget favicon publication.
                if (IconFingerprint(hwnd) != before) failures.Add($"Server {server} replaced the Silo window/taskbar icon.");
            }
            typeof(MainWindow).GetMethod("ResetShellBranding", instance)!.Invoke(window, null);
            if (IconFingerprint(hwnd) != before) failures.Add("Branding reset did not preserve Silo icon.");
            if (wire.FaviconRequests != 0) failures.Add($"Shell branding unnecessarily downloaded {wire.FaviconRequests} server favicons.");
            CheckVersionIndependentRelaunch(failures);
            CheckAcceptedCloseCleanup(failures);
            foreach (var failure in failures) Program.Log("FAIL: " + failure);
            if (failures.Count != 0) throw new InvalidOperationException("Taskbar identity/icon regression: " + failures.Count + " failures.");
            Program.Log("PASS: actual hidden MainWindow keeps the Silo icon across two server-branding loads/reset; Shell reads stable ID, relaunch/name/icon and no favicon downloads.");
        }
        finally
        {
            window?.Close();
            servicesField.SetValue(null, previous);
        }
    }

    private static void CheckAcceptedCloseCleanup(List<string> failures)
    {
        var window = new Window();
        typeof(MainWindow).Assembly.GetType("SiloPlayer.Helpers.TaskbarIdentity")!
            .GetMethod("ApplyToWindow", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [window]);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (ReadProperty(hwnd, 5) != "Silo.DesktopPlayer") failures.Add("Secondary synthetic window identity missing.");
        var observed = false;
        window.Closed += (_, _) =>
        {
            observed = true;
            if (new uint[] { 2, 3, 4, 5 }.Any(id => ReadProperty(hwnd, id) is not null))
                failures.Add("Accepted close retained window-owned taskbar properties.");
        };
        window.Close();
        if (!observed) failures.Add("Synthetic accepted-close cleanup was not observed.");
    }

    private static void CheckVersionIndependentRelaunch(List<string> failures)
    {
        var method = typeof(MainWindow).Assembly.GetType("SiloPlayer.Helpers.TaskbarIdentity")!
            .GetMethod("CreateLaunchInfo", BindingFlags.Static | BindingFlags.NonPublic)!;
        var root = Path.Combine(Program.ResultDirectory, "Synthetic Silo Install");
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        File.WriteAllText(Path.Combine(root, "SiloPlayer.exe"), "dummy launcher; never execute");
        File.Copy(Path.Combine(Program.AppDirectory, "Assets", "app.ico"), Path.Combine(root, "Assets", "app.ico"));
        (string Exe, string Icon) Read(string exe, string? registration)
        {
            var result = method.Invoke(null, [exe, registration])!;
            return ((string)result.GetType().GetProperty("Executable")!.GetValue(result)!,
                (string)result.GetType().GetProperty("Icon")!.GetValue(result)!);
        }
        foreach (var version in new[] { "1.2.511.0", "1.2.514.0" })
        {
            var exe = Path.Combine(root, "versions", version, "SiloPlayer.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "dummy version; never execute");
            var launch = Read(exe, root);
            if (launch.Exe != Path.Combine(root, "SiloPlayer.exe") || launch.Icon != Path.Combine(root, "Assets", "app.ico"))
                failures.Add("Registered version did not relaunch through stable root launcher/icon: " + version);
            var unregistered = Read(exe, null);
            if (unregistered.Exe != exe) failures.Add("Unregistered version guessed an installation root.");
            if (Read(exe, Path.Combine(root, "unrelated")).Exe != exe) failures.Add("Unrelated registered root hijacked a development launch.");
        }
        var legacy = Read(Path.Combine(root, "SiloPlayer.exe"), root);
        if (legacy.Exe != Path.Combine(root, "SiloPlayer.exe")) failures.Add("Legacy root launch changed target.");
        File.Delete(Path.Combine(root, "SiloPlayer.exe"));
        var fallback = Path.Combine(root, "versions", "1.2.514.0", "SiloPlayer.exe");
        if (Read(fallback, root).Exe != fallback) failures.Add("Missing stable launcher produced a broken relaunch target.");
        Program.Log("PASS: synthetic registered versions share a stable launcher/icon; legacy, missing-launcher, development and unrelated-root cases retain safe targets.");
    }

    internal static string IconFingerprint(IntPtr hwnd, nint size = 1)
    {
        var icon = SendMessage(hwnd, 0x007F, size, 0); // WM_GETICON / ICON_BIG or ICON_SMALL
        if (icon == IntPtr.Zero || !GetIconInfo(icon, out var info)) throw new InvalidOperationException("No real window icon.");
        try
        {
            if (GetObject(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap) == 0) throw new InvalidOperationException("Icon bitmap missing.");
            var pixels = new byte[bitmap.WidthBytes * bitmap.Height];
            if (GetBitmapBits(info.Color, pixels.Length, pixels) != pixels.Length) throw new InvalidOperationException("Icon bitmap unreadable.");
            return Convert.ToHexString(SHA256.HashData(pixels));
        }
        finally { DeleteObject(info.Color); DeleteObject(info.Mask); }
    }

    internal static string? ReadProperty(IntPtr hwnd, uint id)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
        try
        {
            var key = new PropertyKey { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id };
            store.GetValue(ref key, out var value);
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out Variant value);
        void SetValue(ref PropertyKey key, ref Variant value); void Commit();
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint X, Y; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, Bits; public IntPtr Data; }
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr bitmap, int size, out Bitmap value);
    [DllImport("gdi32.dll")] private static extern int GetBitmapBits(IntPtr bitmap, int length, byte[] data);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);

    private sealed class Wire : HttpMessageHandler
    {
        public string Server = "tavern";
        public int FaviconRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/favicon.svg")
            {
                FaviconRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'><rect width='16' height='16' fill='red'/></svg>", System.Text.Encoding.UTF8, "image/svg+xml") });
            }
            object body = request.RequestUri.AbsolutePath == "/api/v2/theme/branding"
                ? new { server_name = Server, favicon_url = "https://taskbar-fixture.invalid/favicon.svg?v=" + Server }
                : new { items = Array.Empty<object>() };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
