using System.Text.Json;
using System.Reflection;
using System.Net;
using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.Web.WebView2.Core;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static class ReaderInteropFixture
{
    public static async Task RunAsync(StackPanel parent)
    {
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "reader-controls")
        {
            await RunControlsAsync();
            return;
        }
        var fixtureDirectory = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES");
        if (string.IsNullOrEmpty(fixtureDirectory))
            throw new InvalidOperationException("Set SILO_NATIVE_TEST_READER_FIXTURES to tests/reader-fixtures.");
        var web = new WebView2 { Width = 1100, Height = 740 };
        parent.Children.Add(web);
        try
        {
            Program.Log("Reader fixture: attaching real WebView2 control.");
            web.Measure(new Windows.Foundation.Size(1100, 740));
            web.Arrange(new Windows.Foundation.Rect(0, 0, 1100, 740));
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Program.ResultDirectory, "reader-webview"), null).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            Program.Log("Reader fixture: isolated environment created; initializing control.");
            await web.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            Program.Log("Reader fixture: control initialized; loading trusted shell.");
            var core = web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.SetVirtualHostNameToFolderMapping(EbookReaderWebPolicy.ReaderHost, Path.Combine(Program.AppDirectory, "Assets", "Reader"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping(EbookReaderWebPolicy.BookHost, fixtureDirectory, CoreWebView2HostResourceAccessKind.Allow);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!EbookReaderWebPolicy.IsAllowedSubresource(args.Request.Uri))
                    args.Response = core.Environment.CreateWebResourceResponse(new Windows.Storage.Streams.InMemoryRandomAccessStream(), 403, "Blocked", "Content-Type: text/plain");
            };
            core.NavigationStarting += (_, args) => { if (!EbookReaderWebPolicy.IsTrustedReaderUri(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            var shell = new TaskCompletionSource();
            var result = new TaskCompletionSource<string>();
            core.WebMessageReceived += (_, args) =>
            {
                if (!EbookReaderWebPolicy.IsTrustedReaderUri(args.Source)) return;
                using var message = JsonDocument.Parse(args.TryGetWebMessageAsString());
                var type = message.RootElement.GetProperty("type").GetString();
                if (type == "shell-ready") shell.TrySetResult();
                if (type == "fixture-result") result.TrySetResult(message.RootElement.GetProperty("result").GetRawText());
                if (type == "fixture-failure") result.TrySetException(new InvalidOperationException(message.RootElement.GetProperty("message").GetString()));
            };
            web.Source = new Uri($"https://{EbookReaderWebPolicy.ReaderHost}/index.html");
            await shell.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Program.Log("Reader fixture: trusted shell ready; running EPUB/PDF checks.");
            await web.ExecuteScriptAsync(File.ReadAllText(Path.Combine(fixtureDirectory, "renderer-checks.js")));
            await web.ExecuteScriptAsync($"runReaderChecks('https://{EbookReaderWebPolicy.BookHost}').then(result=>chrome.webview.postMessage(JSON.stringify({{type:'fixture-result',result}}))).catch(e=>chrome.webview.postMessage(JSON.stringify({{type:'fixture-failure',message:e.stack||e.message}})))");
            var evidence = await result.Task.WaitAsync(TimeSpan.FromSeconds(45));
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "reader-interop.json"), evidence);
            Program.Log("PASS: native WebView2 EPUB CFI/annotation/legacy/fraction, PDF pages/bookmark/resume, cancellation and script/network isolation: " + evidence);
        }
        finally
        {
            web.Close();
            parent.Children.Remove(web);
        }
        await RunPageWiringAsync(parent, fixtureDirectory);
    }

    // This gate exercises chrome and real native input only. It does not open a
    // document, initialize WebView2, save settings, or repeat renderer/body gates.
    private static async Task RunControlsAsync()
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalServices = serviceField.GetValue(null);
        using var handler = new ReaderHandler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://reader-controls-fixture.invalid"); client.SetProfile("reader-controls");
        using var services = new ServiceCollection().AddSingleton(new EbooksApi(client)).AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        serviceField.SetValue(null, services);
        var differences = new List<string>();
        try
        {
            foreach (var width in new[] { 460d, 1280d })
            {
                var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], RequestedTheme = ElementTheme.Dark };
                var window = new Window { Content = owner };
                EbookReaderPage? page = null;
                try
                {
                    window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)width, 720)); window.AppWindow.Show(false); await Task.Delay(100);
                    var scale = owner.XamlRoot.RasterizationScale;
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(720 * scale)));
                    page = new EbookReaderPage { Width = width, Height = 720 }; owner.Children.Add(page); await Task.Delay(100);
                    if (Math.Abs(owner.XamlRoot.Size.Width - width) > 2 || Math.Abs(owner.XamlRoot.Size.Height - 720) > 2)
                        throw new InvalidOperationException("Reader controls gate needs the actual requested native client bounds.");
                    ((TextBlock)page.FindName("TitleText")).Text = "The Fixture Book";
                    ((TextBlock)page.FindName("FormatText")).Text = "EPUB";
                    Call(page, "OpenPanel", "settings"); page.UpdateLayout();
                    var section = (StackPanel)page.FindName("ReadingProfilesSection");
                    var profiles = section.Children.OfType<Button>().Where(button => button.Tag is string).ToArray();
                    if (profiles.Length != 3) throw new InvalidOperationException("Reader gate lost the three actual native profile inputs.");
                    foreach (var button in profiles)
                    {
                        Program.Log($"TRACE reader-control {width}/profile={button.Tag}: actual={button.ActualWidth}x{button.ActualHeight}, available={section.ActualWidth}, status={AutomationProperties.GetItemStatus(button)}.");
                        if (Math.Abs(button.ActualWidth - section.ActualWidth) > 2 || button.ActualHeight < 44)
                            differences.Add($"{width}/{button.Tag}: profile must fill available panel width and keep the source min44px row.");
                        InvokeButton(button); await Task.Delay(50); page.UpdateLayout();
                        var expectedSize = Equals(button.Tag, "accessible") ? 126d : Equals(button.Tag, "compact") ? 96d : 112d;
                        if (((Slider)page.FindName("FontSizeSlider")).Value != expectedSize)
                            differences.Add($"{width}/{button.Tag}: actual UIA invoke did not apply the profile font size.");
                        var selected = profiles.Where(profile => AutomationProperties.GetItemStatus(profile) == "Selected").ToArray();
                        if (selected.Length != 1 || !ReferenceEquals(selected[0], button) || !SameColor(button.Background, "SurfaceRaisedBrush"))
                            differences.Add($"{width}/{button.Tag}: selected profile needs exclusive accessible state and source secondary paint.");
                        if (!ControlDescendants(button).OfType<FrameworkElement>().Any(element => element.Visibility == Visibility.Visible && AutomationProperties.GetName(element) == "Selected profile" && Math.Abs(element.ActualWidth - 16) < 1 && Math.Abs(element.ActualHeight - 16) < 1))
                            differences.Add($"{width}/{button.Tag}: selected profile must paint its16px check.");
                    }
                    ((Slider)page.FindName("FontSizeSlider")).Value = 130; await Task.Delay(50);
                    if (profiles.Any(profile => AutomationProperties.GetItemStatus(profile) == "Selected"))
                        differences.Add($"{width}: a custom profile value must clear preset selected state.");
                    var reset = ControlDescendants((DependencyObject)page.FindName("SettingsPanel")).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Reset reader settings");
                    InvokeButton(reset); await Task.Delay(50);
                    if (((Slider)page.FindName("FontSizeSlider")).Value != 112 || profiles.Any(profile => AutomationProperties.GetItemStatus(profile) == "Selected"))
                        differences.Add($"{width}: reset must apply the actual defaults and clear nonmatching preset state.");
                    // Reset applies format applicability too. No book means
                    // Search is correctly disabled; supply real EPUB metadata
                    // before testing the supported-format tab inputs.
                    var extractor = typeof(EbookReaderPage).Assembly.GetType("SiloPlayer.Services.EbookPackageExtractor")!;
                    var epubBytes = File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "unequal.epub"));
                    Set(page, "_book", extractor.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [epubBytes, Path.Combine(Program.ResultDirectory, "reader-controls-epub-" + width), "epub"]));
                    Call(page, "UpdateFormatSpecificChrome"); page.UpdateLayout();
                    var tabs = new[] { "ContentsTab", "SearchTab", "AnnotationsTab", "SettingsTab" }.Select(name => (Button)page.FindName(name)).ToArray();
                    foreach (var tab in tabs)
                    {
                        Program.Log($"TRACE reader-control {width}/tab-input={tab.Name}: enabled={tab.IsEnabled}, loaded={tab.IsLoaded}, visibility={tab.Visibility}.");
                        if (!tab.IsEnabled || !tab.IsLoaded || tab.Visibility != Visibility.Visible)
                            throw new InvalidOperationException($"Supported EPUB tab {tab.Name} is not an applicable mounted native input.");
                        InvokeButton(tab); await Task.Delay(30); page.UpdateLayout();
                        var grid = (Grid)VisualTreeHelper.GetParent(tab);
                        var available = (grid.ActualWidth - grid.Padding.Left - grid.Padding.Right - grid.ColumnSpacing * 3) / 4;
                        Program.Log($"TRACE reader-control {width}/tab={tab.Name}: actual={tab.ActualWidth}x{tab.ActualHeight}, available={available}, opacity={tab.Opacity}.");
                        if (Math.Abs(tab.ActualWidth - available) > 2 || Math.Abs(tab.ActualHeight - 40) > 1)
                            differences.Add($"{width}/{tab.Name}: tab must fill its actual star column and paint40px high.");
                        if (!SameColor(tab.Background, "SurfaceRaisedBrush") || tabs.Any(other => other != tab && SameColor(other.Background, "SurfaceRaisedBrush")) || tabs.Any(other => other.Opacity != 1))
                            differences.Add($"{width}/{tab.Name}: tabs need exclusive source secondary/ghost paint without faded inactive content.");
                    }
                    Call(page, "OpenPanel", "settings"); page.UpdateLayout();
                    // Ruler accessibility intentionally changes to Enable/Disable
                    // after UpdateRulerOverlay. Locate its stable named input,
                    // not the initial XAML label.
                    var toolbar = new[] { "ReadingRulerToolbarButton", "PanelToggleButton", "PreviousButton", "NextButton" }.Select(name => (Button)page.FindName(name)).ToList();
                    var bookmark = ControlDescendants(page).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == "Add bookmark");
                    if (bookmark == null) differences.Add($"{width}: mounted Add bookmark input is missing.");
                    else toolbar.Add(bookmark);
                    foreach (var button in toolbar)
                    {
                        Program.Log($"TRACE reader-control {width}/toolbar={button.Name}/{AutomationProperties.GetName(button)}: actual={button.ActualWidth}x{button.ActualHeight}, border={button.BorderThickness}, enabled={button.IsEnabled}, visibility={button.Visibility}.");
                        if (Math.Abs(button.ActualWidth - 32) > 1 || Math.Abs(button.ActualHeight - 32) > 1 || !button.BorderThickness.Equals(new Thickness(0)) || button.Background is SolidColorBrush { Color.A: > 0 })
                            differences.Add($"{width}/{AutomationProperties.GetName(button)}: toolbar must use visible32px ghost surface.");
                    }
                    if (bookmark != null && (ControlDescendants(bookmark).OfType<FontIcon>().Any(icon => icon.Glyph == "\uE735") || !ControlDescendants(bookmark).OfType<FrameworkElement>().Any(element => element is Microsoft.UI.Xaml.Shapes.Path or Image)))
                        differences.Add($"{width}: bookmark must paint the current outlined bookmark vector, not a star font glyph.");
                    var download = (Button)page.FindName("DownloadButton");
                    if (Math.Abs(download.ActualHeight - 32) > 1 || !download.BorderThickness.Equals(new Thickness(1)))
                        differences.Add($"{width}: File action must retain source32px outline surface.");
                    var bytes = File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "four-pages.pdf"));
                    Set(page, "_book", extractor.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [bytes, Path.Combine(Program.ResultDirectory, "reader-controls-pdf-" + width), "pdf"]));
                    Set(page, "_sharedProgress", 2d / 3); Set(page, "_chapterIndex", 2); Set(page, "_rendererPageCount", 4); Call(page, "UpdateProgressControls");
                    var progress = (TextBlock)page.FindName("ProgressText");
                    if (progress.Text != "67%" || ((TextBlock)page.FindName("HeaderProgressText")).Text != "67%")
                        differences.Add($"{width}: visible PDF progress must be67%, not page copy clipped in the percentage slot.");
                    Program.Log($"TRACE reader-control {width}/PDF progress='{progress.Text}', actual={progress.ActualWidth}x{progress.ActualHeight}.");
                    await MediaParityNativeFixture.CaptureAsync(owner, $"media-reader-controls-{width:0}.png");
                }
                finally
                {
                    if (page != null) { Set(page, "_leaving", true); ((WebView2)page.FindName("ReaderWebView")).Close(); owner.Children.Remove(page); }
                    window.Close();
                }
            }
            if (differences.Count > 0) throw new InvalidOperationException("Reader control parity differences:\n" + string.Join("\n", differences));
            Program.Log("PASS: READER_CONTROLS_COMPLETED actual460/1280 profile/tab native input, full widths, active paint/state,32px toolbar/bookmark and percentage progress.");
        }
        finally { serviceField.SetValue(null, originalServices); }
    }
    private static bool SameColor(Brush? brush, string resource)
        => brush is SolidColorBrush actual && Application.Current.Resources[resource] is SolidColorBrush expected && actual.Color.Equals(expected.Color);
    private static void InvokeButton(Button button)
        => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> ControlDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var nested in ControlDescendants(child)) yield return nested;
        }
    }

    private static async Task RunPageWiringAsync(StackPanel parent, string fixtures)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalServices = serviceField.GetValue(null);
        var readerHandler = new ReaderHandler();
        using var http = new HttpClient(readerHandler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://reader-fixture.invalid");
        client.SetProfile("reader-fixture-profile");
        var api = new EbooksApi(client);
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).BuildServiceProvider();
        serviceField.SetValue(null, services);
        EbookReaderPage? page = null;
        WebView2? web = null;
        try
        {
            page = new EbookReaderPage { Width = 1100, Height = 740 };
            Program.Log("Reader fixture: actual EbookReaderPage constructed.");
            parent.Children.Add(page);
            page.Measure(new Windows.Foundation.Size(1100, 740));
            page.Arrange(new Windows.Foundation.Rect(0, 0, 1100, 740));
            web = (WebView2)page.FindName("ReaderWebView");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Program.ResultDirectory, "reader-page-webview"), null).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            await web.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            Program.Log("Reader fixture: actual page WebView initialized.");
            Call(page, "ConfigureReaderWebView");
            web.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (!EbookReaderWebPolicy.IsTrustedReaderUri(args.Source)) return;
                using var message = JsonDocument.Parse(args.TryGetWebMessageAsString());
                var type = message.RootElement.GetProperty("type").GetString();
                if (type is "shell-ready" or "reader-ready" or "reader-error")
                    Program.Log("Actual reader bridge: " + message.RootElement.GetRawText());
            };
            Set(page, "_contentId", "fixture-book");
            Set(page, "_fileId", 42);
            Set(page, "_readerContext", api.CaptureContext());
            Set(page, "_initialized", true);
            ((TextBlock)page.FindName("TitleText")).Text = "The Fixture Book";
            web.CoreWebView2.SetVirtualHostNameToFolderMapping(EbookReaderWebPolicy.ReaderHost, Path.Combine(Program.AppDirectory, "Assets", "Reader"), CoreWebView2HostResourceAccessKind.DenyCors);
            var generation = 0;
            var tocCase = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "reader-toc";
            foreach (var variant in tocCase ? new[] { "epub", "pdf", "epub-toc" } : new[] { "epub", "pdf" })
            {
                var format = variant == "pdf" ? "pdf" : "epub";
                var file = format == "epub" ? "unequal.epub" : "four-pages.pdf";
                var directory = Path.Combine(Program.ResultDirectory, "reader-page-" + variant);
                var bytes = File.ReadAllBytes(Path.Combine(fixtures, file));
                if (variant == "epub-toc") bytes = WithActualTableOfContents(bytes);
                var extractor = typeof(EbookReaderPage).Assembly.GetType("SiloPlayer.Services.EbookPackageExtractor")!;
                var book = extractor.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [bytes, directory, format]);
                File.WriteAllBytes(Path.Combine(directory, "reader-source." + format), bytes);
                Set(page, "_book", book);
                ((TextBlock)page.FindName("FormatText")).Text = format.ToUpperInvariant();
                Call(page, "BuildContents");
                Call(page, "UpdateFormatSpecificChrome");
                Set(page, "_rendererReady", false);
                Set(page, "_sharedLocation", null);
                Set(page, "_openGeneration", ++generation);
                readerHandler.RestoreLocation = format == "epub" ? "chapter:1;fraction:0.5" : "epubcfi(/6/6)";
                Set(page, "_restoreProgress", await api.GetProgressAsync("fixture-book"));
                web.CoreWebView2.SetVirtualHostNameToFolderMapping(EbookReaderWebPolicy.BookHost, directory, CoreWebView2HostResourceAccessKind.Allow);
                // Like OpenVersionAsync, reload the shell after changing the book
                // mapping; WebView2 retains virtual-host mappings for a document.
                web.Source = new Uri($"https://{EbookReaderWebPolicy.ReaderHost}/index.html?generation={generation}");
                await WaitAsync(() => (bool)Get(page, "_rendererReady")!, "Actual reader page did not become ready for " + format);
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "reader-toc")
                {
                    var actualToc = await web.ExecuteScriptAsync("document.querySelector('foliate-view').book.toc ?? []");
                    using var toc = JsonDocument.Parse(actualToc);
                    var nativeToc = (ListView)page.FindName("ContentsList");
                    Program.Log($"TRACE: actual {format} book.toc={toc.RootElement.GetArrayLength()}, nativeContents={nativeToc.Items.Count}.");
                    if (variant == "epub-toc")
                    {
                        if (toc.RootElement.GetArrayLength() != 1 || nativeToc.Items.Count != 2 ||
                            ((ListViewItem)nativeToc.Items[1]).Tag is not "OPS/long.xhtml#p48" ||
                            ((ListViewItem)nativeToc.Items[1]).Content is not TextBlock { Text: "Nested long passage", Margin.Left: 16 })
                            throw new InvalidOperationException("Actual publication TOC labels, nested depth or href were lost in the native contents.");
                        nativeToc.SelectedItem = nativeToc.Items[0];
                        await WaitAsync(() => (int)Get(page, "_chapterIndex")! == 0, "Real Contents selection did not navigate the short chapter href.");
                        nativeToc.SelectedItem = nativeToc.Items[1];
                        await WaitAsync(() => (int)Get(page, "_chapterIndex")! == 1 && (double)Get(page, "_sharedProgress")! > 0.3,
                            "Real nested Contents selection did not navigate its long passage anchor.");
                        Program.Log("PASS: actual nested publication TOC labels/depth/href navigation and legacy spine restoration coexist.");
                    }
                    else if (toc.RootElement.GetArrayLength() != 0 || nativeToc.Items.Count != 0 || page.FindName("ContentsEmptyText") is not TextBlock { Text: "No contents found.", Visibility: Visibility.Visible })
                        throw new InvalidOperationException("Native reader synthesizes spine/pages instead of the actual empty book.toc and current empty contents message.");
                }
                await (Task)Call(page, "ReadScrollFractionAsync")!;
                await (Task)Call(page, "SaveProgressAsync")!;
                if (readerHandler.LastProgress is not { } saved || !saved.Location.StartsWith("epubcfi(") || saved.FileId != 42 || saved.Progress <= 0)
                    throw new InvalidOperationException("Actual reader page did not save shared " + format + " progress.");
                if (format == "pdf" && (int)Get(page, "_chapterIndex")! != 2)
                    throw new InvalidOperationException("Actual PDF page did not restore page three.");
                await web.ExecuteScriptAsync("chrome.webview.postMessage(JSON.stringify({type:'relocate',hostGeneration:-1,location:'wrong-file',progress:0,index:0,count:1}))");
                await Task.Delay(100);
                if ((string?)Get(page, "_sharedLocation") != saved.Location)
                    throw new InvalidOperationException("Stale document message overwrote current reader position.");
                var bookmarkCount = readerHandler.Bookmarks.Count;
                Call(page, "Bookmark_Click", null, new RoutedEventArgs());
                await WaitAsync(() => readerHandler.Bookmarks.Count > bookmarkCount, "Actual reader bookmark was not sent");
                await WaitAsync(() => ((List<EbookReaderAnnotation>)Get(page, "_annotations")!).Count >= readerHandler.Bookmarks.Count, "Actual reader annotations did not finish loading");
                if (readerHandler.Bookmarks[^1].Location != saved.Location)
                    throw new InvalidOperationException("Bookmark and progress locations diverged.");
                Program.Log($"PASS: actual EbookReaderPage {format} restored {readerHandler.RestoreLocation}, saved {saved.Location}, progress {saved.Progress}, created matching bookmark.");
                foreach (var width in new[] { 1280d, 900d, 460d })
                {
                    Call(page, "OpenPanel", "contents");
                    page.Width = width; page.Height = 720;
                    page.Measure(new Windows.Foundation.Size(width, 720)); page.Arrange(new Windows.Foundation.Rect(0, 0, width, 720)); page.UpdateLayout();
                    Call(page, "UpdateReaderResponsiveLayout");
                    await Task.Delay(100);
                    if (format == "pdf")
                    {
                        var paintReady = false; string diagnostics = "";
                        for (var attempt = 0; attempt < 120 && !paintReady; attempt++)
                        {
                            diagnostics = await web.ExecuteScriptAsync("(()=>{const docs=(document.querySelector('foliate-view')?.renderer?.getContents?.()??[]).map(c=>c.doc).filter(Boolean);return {ready:docs.some(d=>[...d.querySelectorAll('#canvas canvas,#canvas img.silo-pdf-raster')].some(c=>c.tagName==='IMG'?c.complete&&c.naturalWidth>0&&c.naturalHeight>0:c.width>0&&c.height>0)&&d.body.innerText.includes('PDF fixture page')),documents:docs.map(d=>({text:d.body.innerText.slice(0,100),canvas:[...d.querySelectorAll('canvas')].map(c=>[c.width,c.height])}))};})()");
                            using var paint = JsonDocument.Parse(diagnostics); paintReady = paint.RootElement.GetProperty("ready").GetBoolean();
                            if (!paintReady) await Task.Delay(25);
                        }
                        Program.Log("TRACE: actual PDF paint " + diagnostics);
                        if (!paintReady) throw new InvalidOperationException("Actual PDF renderer has no populated canvas/text before document preview.");
                        var geometry = await web.ExecuteScriptAsync("(()=>{const v=document.querySelector('foliate-view');const describe=e=>e?{tag:e.tagName,rect:e.getBoundingClientRect().toJSON(),display:getComputedStyle(e).display,visibility:getComputedStyle(e).visibility,height:getComputedStyle(e).height,inline:e.getAttribute('style')}:null;return {viewport:[innerWidth,innerHeight],root:describe(document.documentElement),body:describe(document.body),view:describe(v),renderer:describe(v?.renderer),frames:(v?.renderer?.getContents?.()??[]).map(({doc})=>{const f=doc.defaultView?.frameElement;return {frame:describe(f),parent:describe(f?.parentElement),paper:describe(doc.querySelector('#canvas')),canvas:describe(doc.querySelector('#canvas canvas,#canvas img.silo-pdf-raster'))};})};})()");
                        Program.Log("TRACE: PDF visible geometry " + geometry);
                        Program.Log($"TRACE: native WebView bounds {web.ActualWidth}x{web.ActualHeight}, visibility={web.Visibility}, opacity={web.Opacity}.");
                        var documentCss = await web.ExecuteScriptAsync("(()=>{const details=e=>({tag:e.tagName,connected:e.isConnected,owner:e.ownerDocument.URL,rect:e.getBoundingClientRect().toJSON(),styles:{display:getComputedStyle(e).display,visibility:getComputedStyle(e).visibility,contentVisibility:getComputedStyle(e).contentVisibility,width:getComputedStyle(e).width,height:getComputedStyle(e).height,position:getComputedStyle(e).position},inline:e.getAttribute('style'),classes:e.className});return (document.querySelector('foliate-view')?.renderer?.getContents?.()??[]).map(({doc})=>({url:doc.URL,root:details(doc.documentElement),body:details(doc.body),canvas:details(doc.querySelector('#canvas canvas,#canvas img.silo-pdf-raster')),canvasAttributes:[...doc.querySelector('#canvas canvas,#canvas img.silo-pdf-raster').attributes].filter(a=>a.name!=='src').map(a=>[a.name,a.value]),styleText:[...doc.querySelectorAll('style')].map(s=>s.textContent.slice(-2000))}));})()");
                        Program.Log("TRACE: PDF iframe document CSS " + documentCss);
                        var paintedPages = await web.ExecuteScriptAsync("(()=>{const v=document.querySelector('foliate-view');return (v?.renderer?.getContents?.()??[]).map(({doc})=>{const c=doc.querySelector('#canvas canvas,#canvas img.silo-pdf-raster');if(!c)return null;const sample=c.tagName==='IMG'?document.createElement('canvas'):c;if(c.tagName==='IMG'){sample.width=c.naturalWidth;sample.height=c.naturalHeight;sample.getContext('2d').drawImage(c,0,0);}const pixels=sample.getContext('2d').getImageData(0,0,sample.width,sample.height).data;let white=0,ink=0;for(let i=0;i<pixels.length;i+=4){if(pixels[i+3]>200){if(pixels[i]>240&&pixels[i+1]>240&&pixels[i+2]>240)white++;else if(pixels[i]<128&&pixels[i+1]<128&&pixels[i+2]<128)ink++;}}return {white,ink,sourceKind:c.tagName,width:sample.width,height:sample.height,text:doc.body.innerText.slice(0,100),canvasRect:c.getBoundingClientRect().toJSON(),bodyRect:doc.body.getBoundingClientRect().toJSON(),visibility:getComputedStyle(c).visibility,display:getComputedStyle(c).display,png:c.tagName==='IMG'?c.src:c.toDataURL('image/png')};}).filter(Boolean);})()");
                        using var pixels = JsonDocument.Parse(paintedPages);
                        var pageNumber = 0;
                        foreach (var painted in pixels.RootElement.EnumerateArray())
                        {
                            if (painted.GetProperty("white").GetInt32() < 1000 || painted.GetProperty("ink").GetInt32() < 20)
                                throw new InvalidOperationException("Actual PDF canvas contains no white paper and dark fixture text pixels.");
                            var png = painted.GetProperty("png").GetString()!;
                            await File.WriteAllBytesAsync(Path.Combine(Program.ResultDirectory, $"media-reader-pdf-canvas-{width:0}-{++pageNumber}.png"), Convert.FromBase64String(png[(png.IndexOf(',') + 1)..]));
                            Program.Log($"TRACE: PDF actual canvas {width:0}/{pageNumber}: " + JsonSerializer.Serialize(new
                            {
                                sourceKind = painted.GetProperty("sourceKind").GetString(), white = painted.GetProperty("white").GetInt32(), ink = painted.GetProperty("ink").GetInt32(),
                                text = painted.GetProperty("text").GetString(), rect = painted.GetProperty("canvasRect"),
                                body = painted.GetProperty("bodyRect"), visibility = painted.GetProperty("visibility").GetString(),
                                display = painted.GetProperty("display").GetString()
                            }));
                            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "reader-real" &&
                                (painted.GetProperty("canvasRect").GetProperty("width").GetDouble() < 1 ||
                                painted.GetProperty("canvasRect").GetProperty("height").GetDouble() < 1))
                                throw new InvalidOperationException("Actual PDF paper/text bitmap exists but its displayed raster has no visible document layout bounds.");
                        }
                    }
                    await MediaParityNativeFixture.CaptureAsync(page, $"media-reader-populated-{variant}-{width:0}.png");
                    using var preview = File.Create(Path.Combine(Program.ResultDirectory, $"media-reader-document-{variant}-{width:0}.png"));
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview.AsRandomAccessStream());
                    Call(page, "OpenPanel", "settings");
                    page.UpdateLayout();
                    if (width == 460 && Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "reader-panel")
                    {
                        var side = (FrameworkElement)page.FindName("SidePanel");
                        var reading = (FrameworkElement)page.FindName("ReadingSurface");
                        Program.Log($"TRACE: actual reader narrow settings panel={side.ActualHeight}, reading={reading.ActualHeight}, body={720 - 96}.");
                        if (format == "epub" ? side.ActualHeight < 620 || reading.ActualHeight > 4
                            : Math.Abs(side.ActualHeight - 312.74) > 4 || Math.Abs(reading.ActualHeight - 311.26) > 4)
                            throw new InvalidOperationException($"Narrow {format} Settings does not match the settled official format-specific auto rows.");
                    }
                    await MediaParityNativeFixture.CaptureAsync(page, $"media-reader-settings-{variant}-{width:0}.png");
                    Call(page, "OpenPanel", "contents");
                }
            }
            var before = readerHandler.ProgressWrites;
            client.SetProfile("replacement-profile");
            await (Task)Call(page, "SaveProgressAsync")!;
            if (readerHandler.ProgressWrites != before) throw new InvalidOperationException("Departing reader wrote into replacement profile.");
            var previousBookmarks = readerHandler.Bookmarks.Count;
            Call(page, "Bookmark_Click", null, new RoutedEventArgs());
            await Task.Delay(200);
            if (readerHandler.Bookmarks.Count != previousBookmarks) throw new InvalidOperationException("Departing reader created bookmark in replacement profile.");
            Program.Log("PASS: actual EbookReaderPage rejects stale-profile progress save.");
        }
        finally
        {
            if (page != null) Set(page, "_leaving", true);
            web?.Close();
            if (page != null) parent.Children.Remove(page);
            serviceField.SetValue(null, originalServices);
        }
    }

    private static byte[] WithActualTableOfContents(byte[] source)
    {
        using var memory = new MemoryStream();
        memory.Write(source);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, leaveOpen: true))
        {
            var package = zip.GetEntry("OPS/package.opf")!;
            string xml;
            using (var reader = new StreamReader(package.Open())) xml = reader.ReadToEnd();
            package.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry("OPS/package.opf").Open()))
                writer.Write(xml.Replace("</manifest>", "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest>"));
            using var navigation = new StreamWriter(zip.CreateEntry("OPS/nav.xhtml").Open());
            navigation.Write("""
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="short.xhtml">Short chapter</a><ol><li><a href="long.xhtml#p48">Nested long passage</a></li></ol></li></ol></nav></body></html>
                """);
        }
        return memory.ToArray();
    }

    private static object? Get(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
    private static void Set(object value, string name, object? fieldValue) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(value, fieldValue);
    private static object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args);
    private static async Task WaitAsync(Func<bool> condition, string message)
    {
        for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); }
        throw new InvalidOperationException(message);
    }
    private sealed class ReaderHandler : HttpMessageHandler
    {
        public string RestoreLocation { get; set; } = "";
        public EbookReaderProgressInput? LastProgress { get; private set; }
        public int ProgressWrites { get; private set; }
        public List<EbookReaderAnnotation> Bookmarks { get; } = [];
        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/progress"))
            {
                if (request.Method == HttpMethod.Put)
                {
                    LastProgress = JsonSerializer.Deserialize<EbookReaderProgressInput>(await request.Content!.ReadAsStringAsync(cancellationToken), Options)!;
                    ProgressWrites++;
                    return Json(new { progress = LastProgress });
                }
                return Json(new { progress = new { file_id = "42", location = RestoreLocation, progress = .5 } });
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/annotations"))
            {
                if (request.Method == HttpMethod.Post)
                {
                    var bookmark = JsonSerializer.Deserialize<EbookReaderAnnotation>(await request.Content!.ReadAsStringAsync(cancellationToken), Options)!;
                    bookmark.ETag = "\"fixture\"";
                    Bookmarks.Add(bookmark);
                    return Json(bookmark);
                }
                return Json(new { items = Bookmarks, page = new { has_more = false } });
            }
            throw new InvalidOperationException("Unexpected reader fixture request: " + request.RequestUri.AbsolutePath);
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, Options), Encoding.UTF8, "application/json") };
    }
}
