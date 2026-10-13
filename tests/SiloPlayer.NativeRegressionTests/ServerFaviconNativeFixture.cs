using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Services;
using Windows.Graphics.Imaging;

internal static class ServerFaviconNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        var loader = typeof(SiloPlayer.MainWindow).Assembly.GetType("SiloPlayer.Services.ServerWindowIcon")?.GetMethod("GetIconPathAsync")
            ?? throw new InvalidOperationException("Server favicon conversion is missing; the window still only uses the bundled icon.");
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "favicon-cache"));
        async Task<string?> Load(string scope, string? url) => await (Task<string?>)loader.Invoke(null, [images, http, scope, url, CancellationToken.None])!;
        wire.Body = await Png();
        var pngPath = await Load("server-a", "https://server-a.invalid/favicon?v=png") ?? throw new InvalidOperationException("PNG favicon did not convert.");
        await CheckPngIcon(pngPath, red: true);
        window.AppWindow.SetIcon(pngPath);
        wire.Body = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'><rect x='2' y='2' width='12' height='12' fill='blue'/></svg>");
        var svgPath = await Load("server-b", "https://server-b.invalid/favicon?v=svg") ?? throw new InvalidOperationException("SVG favicon did not convert.");
        await CheckPngIcon(svgPath, red: false); window.AppWindow.SetIcon(svgPath);
        if (svgPath == pngPath || wire.Requests != 2) throw new InvalidOperationException("Server B reused Server A's favicon.");
        wire.Body = File.ReadAllBytes(pngPath);
        var icoPath = await Load("server-c", "https://server-c.invalid/favicon?v=ico") ?? throw new InvalidOperationException("ICO favicon did not load.");
        if (!File.ReadAllBytes(icoPath).SequenceEqual(wire.Body)) throw new InvalidOperationException("Existing ICO was not preserved.");
        window.AppWindow.SetIcon(icoPath);
        // Generated locally with Pillow's lossless WebP encoder: opaque red 16x16.
        wire.Body = Convert.FromBase64String("UklGRhwAAABXRUJQVlA4TA8AAAAvD8ADAAcQ/Y/+ByKi/wEA");
        var webpPath = await Load("server-d", "https://server-d.invalid/favicon?v=webp") ?? throw new InvalidOperationException("WebP favicon did not convert.");
        await CheckPngIcon(webpPath, red: true); window.AppWindow.SetIcon(webpPath);
        wire.Status = HttpStatusCode.NotFound;
        if (await Load("server-missing", "https://missing.invalid/favicon") is not null) throw new InvalidOperationException("Missing favicon must use the bundled fallback.");
        wire.Status = HttpStatusCode.OK; wire.Body = [1, 2, 3];
        if (await Load("server-corrupt", "https://corrupt.invalid/favicon") is not null) throw new InvalidOperationException("Malformed favicon must use the bundled fallback.");
        var reads = wire.Requests;
        if (await Load("server-none", null) is not null || wire.Requests != reads) throw new InvalidOperationException("Missing branding must not fetch another server's icon.");
        window.AppWindow.SetIcon(Path.Combine(Program.AppDirectory, "Assets", "app.ico"));
        Program.Log("PASS: PNG/SVG/ICO/WebP favicon files accepted by the actual AppWindow; distinct server assets, absent/failed/malformed fallback verified.");
    }
    private static async Task CheckPngIcon(string path, bool red)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 5)
            throw new InvalidOperationException("Converted favicon must be a Windows ICO with five DPI sizes.");
        var dimensions = new[] { 16, 32, 48, 64, 256 };
        foreach (var size in dimensions)
        {
            if (reader.ReadByte() != (byte)(size == 256 ? 0 : size) || reader.ReadByte() != (byte)(size == 256 ? 0 : size)) throw new InvalidOperationException("Bad ICO dimensions.");
            reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt16();
            var length = reader.ReadUInt32(); var offset = reader.ReadUInt32();
            if (length < 8 || offset + length > reader.BaseStream.Length) throw new InvalidOperationException("Bad ICO frame bounds.");
            var nextEntry = reader.BaseStream.Position;
            reader.BaseStream.Position = offset;
            using var png = new MemoryStream(reader.ReadBytes((int)length));
            var decoder = await BitmapDecoder.CreateAsync(png.AsRandomAccessStream());
            var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var center = (size / 2 * size + size / 2) * 4;
            if (decoder.PixelWidth != size || decoder.PixelHeight != size || pixels[center + 3] != 255 ||
                pixels[center + (red ? 2 : 0)] < 240 || pixels[center + (red ? 0 : 2)] > 16)
                throw new InvalidOperationException("Converted favicon lost its source color or pixel dimensions.");
            reader.BaseStream.Position = nextEntry;
        }
    }
    private static async Task<byte[]> Png(Guid? format = null)
    {
        using var stream = new MemoryStream();
        var encoder = await BitmapEncoder.CreateAsync(format ?? BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
        var pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 0, 0, 255, 255 }).ToArray();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 16, 16, 96, 96, pixels);
        await encoder.FlushAsync(); return stream.ToArray();
    }
    private sealed class Wire : HttpMessageHandler
    {
        public byte[] Body = [];
        public int Requests;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests++; return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) }); }
    }
}
