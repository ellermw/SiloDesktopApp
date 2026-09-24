using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SiloPlayer.Player;

class Program
{
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", Path.Combine(AppContext.BaseDirectory, "logs"));
        var width = args.Contains("--1080p") ? 1920 : 640;
        var height = args.Contains("--1080p") ? 1080 : 360;
        var path = Path.Combine(AppContext.BaseDirectory, "fixture.y4m");
        using (var f = File.Create(path))
        {
            f.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F24:1 Ip A1:1 C420jpeg\n"));
            var frame = new byte[width * height * 3 / 2];
            for (int i = 0; i < width * height; i++) frame[i] = (byte)(16 + ((i % width) / 8 % 2) * 200);
            Array.Fill(frame, (byte)128, width * height, width * height / 2);
            for (int n = 0; n < 48; n++) { f.Write(Encoding.ASCII.GetBytes("FRAME\n")); f.Write(frame); }
        }
        var hwnd = CreateWindowEx(0, "STATIC", "Silo GPU fixture", 0x90000000, -10000, 0, width * 2, height * 2, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new Exception("Fixture window failed");
        try
        {
            using var player = new MpvPlayer();
            player.UpscalingStatusChanged += Console.WriteLine;
            player.InitializeWithWindow(hwnd, upscalingMode: VideoUpscalingMode.Fsrcnnx);
            player.SetProperty("loop-file", "inf");
            player.LoadFile(path);
            Pump(6500);
            string? Read(string name) => (string?)typeof(MpvPlayer).GetMethod("ReadUpscalingProperty", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(player, [name]);
            void Cmd(params string[] values) => typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(player, [values]);
            Console.WriteLine("PARAMS " + Read("video-params"));
            Console.WriteLine("SHADERS " + Read("glsl-shaders"));
            var lua = Path.Combine(AppContext.BaseDirectory,"passes.lua");
            File.WriteAllText(lua, "local mp=require 'mp'; local u=require 'mp.utils'; mp.add_timeout(0.1,function() mp.set_property('user-data/probe-passes',u.format_json(mp.get_property_native('vo-passes',{}))) end)");
            Cmd("load-script", lua); Pump(500);
            var passes = Read("user-data/probe-passes") ?? "";
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"passes.json"),passes);
            Console.WriteLine("PASSES " + passes.Substring(0, Math.Min(600,passes.Length)));
            using var encoded = JsonDocument.Parse(passes);
            using var decoded = JsonDocument.Parse(encoded.RootElement.ValueKind == JsonValueKind.String
                ? encoded.RootElement.GetString()! : passes);
            var reconstructionRan = decoded.RootElement.GetProperty("fresh").EnumerateArray().Any(pass =>
                pass.GetProperty("desc").GetString() == "aggregation" && pass.GetProperty("count").GetInt32() > 0);
            if (!(Read("glsl-shaders") ?? "").Contains("FSRCNNX") || !reconstructionRan)
                throw new Exception("Neural reconstruction was not verified in the actual renderer");
            Console.WriteLine("PASS actual GPU neural shader passes present");
            player.SetProperty("video-aspect-override", "no");
            SetWindowPos(hwnd, IntPtr.Zero, -10000, 0, width, height, 0x14); Pump(2500);
            if ((Read("glsl-shaders") ?? "").Contains("FSRCNNX")) throw new Exception("Shader remained enabled at native size");
            Console.WriteLine("PASS shader removed at native size");
            SetWindowPos(hwnd, IntPtr.Zero, -10000, 0, width * 2, height * 2, 0x14); Pump(2500);
            if (!(Read("glsl-shaders") ?? "").Contains("FSRCNNX")) throw new Exception("Shader did not return after resize");
            Console.WriteLine("PASS shader restored after enlargement");
            Console.WriteLine("Dropped frames: " + player.GetPropertyDouble("frame-drop-count"));
            player.Dispose();
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "libs", "mpv", "shaders", "FSRCNNX_x2_8-0-4-1.glsl");
            var backup = shaderPath + ".fixture-backup";
            File.Move(shaderPath, backup);
            try
            {
                using var fallback = new MpvPlayer();
                fallback.InitializeWithWindow(hwnd, upscalingMode: VideoUpscalingMode.Fsrcnnx);
                var status = (string?)typeof(MpvPlayer).GetMethod("ReadUpscalingProperty", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(fallback, ["user-data/silo-upscaling-status"]);
                if (status?.Contains("shader is missing") != true) throw new Exception("Missing-shader reason absent from Playback Info");
                fallback.LoadFile(path); Pump(1000);
                if (fallback.GetPropertyDouble("time-pos") <= 0) throw new Exception("Missing shader prevented normal playback");
                Console.WriteLine("PASS missing shader reports fallback and normal playback continues");
            }
            finally { File.Move(backup, shaderPath); }
        }
        finally { DestroyWindow(hwnd); }
    }
    static void Pump(int milliseconds)
    {
        var until=Environment.TickCount64+milliseconds;
        while(Environment.TickCount64 < until)
        {
            while(PeekMessage(out var msg,IntPtr.Zero,0,0,1)){TranslateMessage(ref msg);DispatchMessage(ref msg);}
            Thread.Sleep(10);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Msg { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x,y; public uint priv; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex,string cls,string name,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Msg msg,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Msg msg);
}
