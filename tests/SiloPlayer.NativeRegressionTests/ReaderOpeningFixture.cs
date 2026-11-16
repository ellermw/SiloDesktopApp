using System.Net;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static class ReaderOpeningFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new ReaderWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://reader-opening.invalid");
        client.SetProfile("reader-opening");
        using var services = new ServiceCollection().AddSingleton(new EbooksApi(client))
            .AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        var serviceField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = serviceField.GetValue(null);
        serviceField.SetValue(null, services);
        var frame = new Frame { Width = 1100, Height = 740 };
        parent.Children.Add(frame);
        EbookReaderPage? page = null;
        try
        {
            frame.Navigate(typeof(EbookReaderPage), new EbookReaderNavigation("fixture-book", 42));
            page = (EbookReaderPage)frame.Content;
            var directory = Path.Combine(Program.ResultDirectory, "reader-settings");
            Directory.CreateDirectory(directory);
            typeof(EbookReaderPage).GetField("_readerSettingsDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, directory);
            wire.CatalogReady.SetResult();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            var readyField = typeof(EbookReaderPage).GetField("_rendererReady", BindingFlags.Instance | BindingFlags.NonPublic)!;
            while (DateTime.UtcNow < deadline && !(bool)readyField.GetValue(page)!)
                await Task.Delay(100);
            var loading = (TextBlock)page.FindName("LoadingText");
            if (!(bool)readyField.GetValue(page)!)
                throw new InvalidOperationException("Read navigation did not render the fixture " + wire.Format + ". Loading state: " + loading?.Text);
            var web = (WebView2)page.FindName("ReaderWebView");
            var result = await web.ExecuteScriptAsync("JSON.stringify({ready:!!window.siloReader?.snapshot(),text:window.siloReader?.text()})");
            if (wire.Format is "cbz" or "cbr")
            {
                var image = await web.ExecuteScriptAsync("document.querySelector('foliate-view').renderer.getContents().some(({doc})=>[...doc.images].some(image=>image.complete&&image.naturalWidth>0))");
                if (image != "true") throw new InvalidOperationException("Comic image was not decoded in the sandboxed renderer.");
                var next = typeof(EbookReaderPage).GetField("_nextMangaChapter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)
                    as SiloPlayer.Core.Models.Catalog.MangaChapter;
                if (next?.ContentId != "fixture-chapter-2" || ((Button)page.FindName("NextChapterButton")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Comic Next must advance from loose Chapter 1 to Volume 1 Chapter 2.");
            }
            else if (!result.Contains(wire.Format == "pdf" ? "PDF fixture" : "Short opening", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Read navigation did not display the fixture chapter: " + result);
            var sandbox = await web.ExecuteScriptAsync("document.querySelector('foliate-view').renderer.getContents().every(({doc})=>!doc.defaultView.frameElement.sandbox.contains('allow-scripts')) && !window.__kindleScriptRan");
            if (sandbox != "true") throw new InvalidOperationException("Book frame enabled publication scripts.");
            await (Task)typeof(EbookReaderPage).GetMethod("SaveProgressAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            if (wire.ProgressSaves != 1) throw new InvalidOperationException("Opened reader did not save progress.");
            await AssertRuntimeResilienceAsync(page, web, client, wire, readyField);
            var savesBeforeRetirement = wire.ProgressSaves;
            client.SetProfile("replacement");
            await Task.Delay(700);
            if ((bool)readyField.GetValue(page)!) throw new InvalidOperationException("Retired reader remained ready after profile replacement.");
            await (Task)typeof(EbookReaderPage).GetMethod("SaveProgressAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            if (wire.ProgressSaves != savesBeforeRetirement) throw new InvalidOperationException("Retired reader saved into its replacement profile.");
            Program.Log("PASS: reader-opening " + wire.Format + " actual navigation, v2 download, materialization, packaged rendering, script sandbox, progress and profile retirement.");
        }
        finally
        {
            if (page != null)
            {
                typeof(EbookReaderPage).GetField("_leaving", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
                ((DispatcherTimer)typeof(EbookReaderPage).GetField("_progressTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).Stop();
                ((DispatcherTimer)typeof(EbookReaderPage).GetField("_contextTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).Stop();
                ((WebView2)page.FindName("ReaderWebView")).Close();
            }
            parent.Children.Remove(frame);
            serviceField.SetValue(null, previousServices);
        }
    }

    private static async Task AssertRuntimeResilienceAsync(EbookReaderPage page, WebView2 web,
        SiloApiClient client, ReaderWire wire, FieldInfo readyField)
    {
        await web.ExecuteScriptAsync("setTimeout(() => { throw new Error('Fixture runtime failure'); }, 0); Promise.reject(new Error('Fixture runtime rejection'));");
        await Task.Delay(250);
        if (!(bool)readyField.GetValue(page)!)
            throw new InvalidOperationException("A runtime script error retired an already opened reader.");
        foreach (var transportCatchUp in new[] { true, false })
        {
            var identity = client.CaptureIdentityContext();
            if (!client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp))
                throw new InvalidOperationException("Fixture could not invalidate the current request context.");
            await Task.Delay(700);
            if (!client.IsCurrentIdentityContext(identity) || !(bool)readyField.GetValue(page)!)
                throw new InvalidOperationException("Request retirement closed the current reader identity: transportCatchUp=" + transportCatchUp);
            await web.ExecuteScriptAsync("window.__fixtureReaderNavigation = null; window.siloReader.go('fraction:0.25').then(value => window.__fixtureReaderNavigation = value, () => window.__fixtureReaderNavigation = false);");
            var navigationDeadline = DateTime.UtcNow.AddSeconds(3);
            var navigated = "null";
            while (DateTime.UtcNow < navigationDeadline && navigated == "null")
            {
                await Task.Delay(50);
                navigated = await web.ExecuteScriptAsync("window.__fixtureReaderNavigation");
            }
            if (navigated != "true")
                throw new InvalidOperationException("The reader cannot navigate after request retirement.");
            var previousSaves = wire.ProgressSaves;
            await (Task)typeof(EbookReaderPage).GetMethod("SaveProgressAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            if (wire.ProgressSaves != previousSaves + 1)
                throw new InvalidOperationException("The current reader did not save after request retirement.");
        }
        Program.Log("PASS: an opened reader survives runtime script errors, transport catch-up and access refresh, then navigates and saves under its original identity.");
    }

    private sealed class ReaderWire : HttpMessageHandler
    {
        internal string Format { get; } = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FORMAT") ?? "epub";
        internal int ProgressSaves { get; private set; }
        internal TaskCompletionSource CatalogReady { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Headers.GetValues("X-Profile-Id").Single() != "reader-opening")
                throw new InvalidOperationException("Reader sent a request under the replacement profile.");
            if (path.StartsWith("/api/v2/catalog/items/", StringComparison.Ordinal))
            {
                await CatalogReady.Task.WaitAsync(cancellationToken);
                if (path.EndsWith("/fixture-series", StringComparison.Ordinal))
                    return Json("""{"content_id":"fixture-series","type":"manga","title":"Fixture series","manga":{"chapters":[{"content_id":"fixture-chapter-2","title":"Second chapter","volume":"v01","chapter_index":2},{"content_id":"fixture-book","title":"First loose chapter","chapter_index":1}]}}""");
                var format = Format == "svg-epub" ? "epub" : Format;
                var seriesId = Format is "cbz" or "cbr" ? "\"fixture-series\"" : "null";
                return Json($$"""{"content_id":"fixture-book","type":"ebook","title":"Opening fixture","series_id":{{seriesId}},"versions":[{"file_id":"42","file_name":"fixture.{{format}}","container":"{{format}}"}]}""");
            }
            if (path.EndsWith("/read", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(BookBytes()) };
            if (path.EndsWith("/reader-config", StringComparison.Ordinal))
            {
                var response = Json("""{"content_id":"fixture-book","config":{}}""");
                response.Headers.ETag = new("\"fixture-config\"");
                return response;
            }
            if (path.EndsWith("/annotations", StringComparison.Ordinal)) return Json("""{"items":[],"page":{"has_more":false}}""");
            if (path.EndsWith("/progress", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Put)
                {
                    ProgressSaves++;
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    using var value = JsonDocument.Parse(body);
                    if (value.RootElement.GetProperty("file_id").GetString() != "42" || string.IsNullOrWhiteSpace(value.RootElement.GetProperty("location").GetString()))
                        throw new InvalidOperationException("Reader progress did not retain file and location.");
                    return Json("{\"progress\":" + body + "}");
                }
                return Json("""{"progress":null}""");
            }
            throw new InvalidOperationException("Unexpected fixture route: " + path);
        }

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
            { Content = new StringContent(value, Encoding.UTF8, "application/json") };

        private byte[] BookBytes()
        {
            if (Format is "mobi" or "azw" or "azw3") return Mobi();
            if (Format is "fb2" or "fbz")
            {
                var fb2 = """<?xml version="1.0" encoding="UTF-8"?><FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0"><description><title-info><book-title>Fixture</book-title><lang>en</lang></title-info></description><body><section><title><p>Fixture chapter</p></title><p>Short opening FB2 fixture.</p></section></body></FictionBook>"""u8.ToArray();
                return Format == "fb2" ? fb2 : Zip(("fixture.fb2", fb2));
            }
            if (Format is "cbz" or "cbr")
            {
                var image = File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "audiobook-cover.png"));
                return Format == "cbz" ? Zip(("001.png", image), ("002.png", image)) : Rar("001.png", image);
            }
            if (Format == "svg-epub")
                return Zip(
                    ("META-INF/container.xml", """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OPS/package.opf"/></rootfiles></container>"""u8.ToArray()),
                    ("OPS/package.opf", """<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">fixture</dc:identifier><dc:title>Fixture</dc:title><meta property="rendition:layout">pre-paginated</meta></metadata><manifest><item id="page" href="page.svg" media-type="image/svg+xml"/></manifest><spine><itemref idref="page"/></spine></package>"""u8.ToArray()),
                    ("OPS/page.svg", """<svg xmlns="http://www.w3.org/2000/svg" width="500" height="700"><rect width="500" height="700" fill="white"/><text x="30" y="80">Short opening SVG fixture.</text><script>parent.__kindleScriptRan=true</script></svg>"""u8.ToArray()));
            return File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, Format == "pdf" ? "four-pages.pdf" : "unequal.epub"));
        }

        private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                foreach (var entry in entries)
                {
                    using var output = archive.CreateEntry(entry.Name).Open();
                    output.Write(entry.Bytes);
                }
            return stream.ToArray();
        }

        private static byte[] Rar(string name, byte[] bytes)
        {
            var filename = Encoding.UTF8.GetBytes(name);
            using var output = new MemoryStream();
            output.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00 });
            var main = new byte[13];
            main[2] = 0x73;
            BinaryPrimitives.WriteUInt16LittleEndian(main.AsSpan(5), (ushort)main.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(main, (ushort)Crc(main.AsSpan(2)));
            output.Write(main);
            var header = new byte[32 + filename.Length];
            header[2] = 0x74;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(3), 0x8000);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)header.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(7), (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(11), (uint)bytes.Length);
            header[15] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), Crc(bytes));
            header[24] = 20;
            header[25] = 0x30;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)filename.Length);
            filename.CopyTo(header.AsSpan(32));
            BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)Crc(header.AsSpan(2)));
            output.Write(header);
            output.Write(bytes);
            return output.ToArray();
        }

        private static uint Crc(ReadOnlySpan<byte> bytes)
        {
            var crc = uint.MaxValue;
            foreach (var value in bytes)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            return ~crc;
        }

        private static byte[] Mobi()
        {
            var text = "<html><head><title>Fixture</title></head><body><p>Short opening Kindle fixture.</p><script>parent.__kindleScriptRan=true</script></body></html>"u8.ToArray();
            var bytes = new byte[350 + text.Length];
            "Fixture"u8.CopyTo(bytes);
            "BOOKMOBI"u8.CopyTo(bytes.AsSpan(60));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76), 2);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(78), 94);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(86), 350);
            var header = bytes.AsSpan(94, 256);
            BinaryPrimitives.WriteUInt16BigEndian(header, 1);
            BinaryPrimitives.WriteUInt16BigEndian(header[8..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(header[10..], 4096);
            "MOBI"u8.CopyTo(header[16..]);
            BinaryPrimitives.WriteUInt32BigEndian(header[20..], 232);
            BinaryPrimitives.WriteUInt32BigEndian(header[24..], 2);
            BinaryPrimitives.WriteUInt32BigEndian(header[28..], 65001);
            BinaryPrimitives.WriteUInt32BigEndian(header[36..], 6);
            BinaryPrimitives.WriteUInt32BigEndian(header[84..], 248);
            BinaryPrimitives.WriteUInt32BigEndian(header[88..], 7);
            BinaryPrimitives.WriteUInt32BigEndian(header[108..], uint.MaxValue);
            BinaryPrimitives.WriteUInt32BigEndian(header[244..], uint.MaxValue);
            "Fixture"u8.CopyTo(header[248..]);
            text.CopyTo(bytes.AsSpan(350));
            return bytes;
        }
    }
}
