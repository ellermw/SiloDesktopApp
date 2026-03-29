using SkiaSharp;

var assetsDir = Path.Combine(args.Length > 0 ? args[0] : ".", "src", "ContinuumPlayer", "Assets");
Directory.CreateDirectory(assetsDir);

var bgColor = new SKColor(16, 23, 34);     // #101722
var triColor = new SKColor(120, 174, 252);  // #78AEFC

void DrawPlayIcon(SKCanvas canvas, int w, int h)
{
    canvas.Clear(bgColor);

    var paint = new SKPaint
    {
        Color = triColor,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    // Play triangle centered in the square
    float cx = w / 2f;
    float cy = h / 2f;
    float size = Math.Min(w, h) * 0.32f;

    var path = new SKPath();
    path.MoveTo(cx - size * 0.45f, cy - size);
    path.LineTo(cx - size * 0.45f, cy + size);
    path.LineTo(cx + size * 0.75f, cy);
    path.Close();

    canvas.DrawPath(path, paint);
}

void DrawWideIcon(SKCanvas canvas, int w, int h)
{
    canvas.Clear(bgColor);

    var paint = new SKPaint
    {
        Color = triColor,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    float cx = w / 2f;
    float cy = h / 2f;
    float size = h * 0.25f;

    var path = new SKPath();
    path.MoveTo(cx - size * 0.45f, cy - size);
    path.LineTo(cx - size * 0.45f, cy + size);
    path.LineTo(cx + size * 0.75f, cy);
    path.Close();

    canvas.DrawPath(path, paint);
}

void SavePng(string filename, int w, int h, Action<SKCanvas, int, int> draw)
{
    using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    draw(canvas, w, h);

    var fullPath = Path.Combine(assetsDir, filename);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(fullPath, data.ToArray());
    Console.WriteLine($"  {filename} ({w}x{h})");
}

Console.WriteLine("Generating Continuum Player icons...");
Action<SKCanvas, int, int> playDraw = DrawPlayIcon;
Action<SKCanvas, int, int> wideDraw = DrawWideIcon;
SavePng("StoreLogo.png", 50, 50, playDraw);
SavePng("Square44x44Logo.scale-200.png", 88, 88, playDraw);
SavePng("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24, playDraw);
SavePng("Square150x150Logo.scale-200.png", 300, 300, playDraw);
SavePng("LockScreenLogo.scale-200.png", 48, 48, playDraw);
SavePng("SplashScreen.scale-200.png", 620, 300, wideDraw);
SavePng("Wide310x150Logo.scale-200.png", 620, 300, wideDraw);

// Also generate an .ico for the exe (16, 32, 48, 256)
Console.WriteLine("  app.ico (multi-size)");
var icoSizes = new[] { 16, 32, 48, 256 };
using var icoStream = new MemoryStream();
using var writer = new BinaryWriter(icoStream);
writer.Write((short)0); // reserved
writer.Write((short)1); // icon type
writer.Write((short)icoSizes.Length);

var pngDataList = new List<byte[]>();
foreach (var sz in icoSizes)
{
    using var bmp = new SKBitmap(sz, sz, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    DrawPlayIcon(canvas, sz, sz);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    pngDataList.Add(data.ToArray());
}

int offset = 6 + icoSizes.Length * 16;
for (int i = 0; i < icoSizes.Length; i++)
{
    byte sz = icoSizes[i] >= 256 ? (byte)0 : (byte)icoSizes[i];
    writer.Write(sz); // width
    writer.Write(sz); // height
    writer.Write((byte)0); // color count
    writer.Write((byte)0); // reserved
    writer.Write((short)1); // planes
    writer.Write((short)32); // bpp
    writer.Write(pngDataList[i].Length); // size
    writer.Write(offset); // offset
    offset += pngDataList[i].Length;
}
foreach (var png in pngDataList)
    writer.Write(png);

File.WriteAllBytes(Path.Combine(assetsDir, "app.ico"), icoStream.ToArray());

Console.WriteLine("Done!");
