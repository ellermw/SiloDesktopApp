using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Services;

internal static class SvgComicSecurityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var root = Path.Combine(Program.ResultDirectory, "comic");
        var tall = Environment.GetEnvironmentVariable("SILO_NATIVE_SVG_TALL") == "1";
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("pages/0 2&.svg").Open()))
            writer.Write(tall
                ? """<svg xmlns="http://www.w3.org/2000/svg" width="400" height="4000" viewBox="0 0 400 4000"><rect width="400" height="4000" fill="red"/><script>window.__svgExecuted=true;</script></svg>"""
                : """<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40"><rect width="80" height="40" fill="red"/><script><![CDATA[window.__svgExecuted=true;if(window.chrome?.webview)chrome.webview.postMessage('untrusted-svg-message');]]></script></svg>""");
        var type = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.EbookPackageExtractor")!;
        var extract = type.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static, null, [typeof(byte[]), typeof(string), typeof(string)], null)!;
        var book = extract.Invoke(null, [stream.ToArray(), root, "cbz"])!;
        var chapters = (IEnumerable)book.GetType().GetProperty("Chapters")!.GetValue(book)!;
        var chapter = chapters.Cast<object>().Single();
        var path = (string)chapter.GetType().GetProperty("RelativePath")!.GetValue(chapter)!;
        var web = new WebView2 { Width = 900, Height = 700 }; parent.Children.Add(web);
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Program.ResultDirectory, "comic-webview"), null).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            await web.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            var core = web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.SetVirtualHostNameToFolderMapping("silo-reader.local", root, CoreWebView2HostResourceAccessKind.DenyCors);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && uri.Host == "silo-reader.local") return;
                e.Response = core.Environment.CreateWebResourceResponse(new Windows.Storage.Streams.InMemoryRandomAccessStream(), 403, "Blocked", "Content-Type: text/plain");
            };
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://silo-reader.local/", StringComparison.Ordinal)) e.Cancel = true; };
            var messages = 0; core.WebMessageReceived += (_, _) => messages++;
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new InvalidOperationException("Comic navigation failed.")); };
            web.Source = new Uri("https://silo-reader.local/" + path);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(150);
            var executed = await core.ExecuteScriptAsync("window.__svgExecuted === true");
            if (executed == "true" || messages != 0) throw new InvalidOperationException("Comic SVG executed active content in the trusted reader origin.");
            var json = await core.ExecuteScriptAsync("JSON.stringify((()=>{const i=document.querySelector('img');if(!i)return null;const r=i.getBoundingClientRect();return {loaded:i.complete&&i.naturalWidth>0,x:r.x,y:r.y,w:r.width,h:r.height,vw:innerWidth,vh:innerHeight};})())");
            var inner = JsonSerializer.Deserialize<string>(json);
            using var result = JsonDocument.Parse(inner ?? "null");
            if (result.RootElement.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("Comic SVG was not presented as an inert image.");
            var p = result.RootElement;
            if (!p.GetProperty("loaded").GetBoolean()) throw new InvalidOperationException("Wrapped SVG image did not load.");
            var dx = Math.Abs(p.GetProperty("x").GetDouble() + p.GetProperty("w").GetDouble() / 2 - p.GetProperty("vw").GetDouble() / 2);
            var dy = Math.Abs(p.GetProperty("y").GetDouble() + p.GetProperty("h").GetDouble() / 2 - p.GetProperty("vh").GetDouble() / 2);
            if (dx > 1 || dy > 1) throw new InvalidOperationException($"SVG page is not centered: dx={dx:F1}, dy={dy:F1}.");
            if (p.GetProperty("w").GetDouble() > p.GetProperty("vw").GetDouble() + 1 || p.GetProperty("h").GetDouble() > p.GetProperty("vh").GetDouble() + 1)
                throw new InvalidOperationException("SVG image overflows the viewport.");
            using var preview = File.Create(Path.Combine(Program.ResultDirectory, "security-svg-comic.png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview.AsRandomAccessStream());
            Program.Log("PASS: actual WebView2 loads the SVG comic image centered, with no SVG script execution or trusted-origin bridge messages.");
        }
        finally { web.Close(); parent.Children.Remove(web); }
    }
}
