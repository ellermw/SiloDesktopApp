using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Finite original E05/E06 acceptance. Uses only a fake API and its own Window.
internal static class CollectionsAcceptanceNativeFixture
{
    private static readonly List<string> Failures = [];
    internal static async Task RunAsync(StackPanel unused)
    {
        Failures.Clear();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        await using var artwork = new ArtworkAsset(); Wire.PosterUrl = artwork.Url;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collections-acceptance.invalid");
        Check(client.ResolveServerUrl("/artwork/template.png") == "https://collections-acceptance.invalid/artwork/template.png", "relative server artwork URI resolution changed");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("fixture", "fixture", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client);
        var navigation = new NavigationService(); var frame = new Frame { Width = 1280, Height = 900 };
        var window = new Window { Content = frame }; navigation.Frame = frame;
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth,
            [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections, [typeof(SettingsApi)] = () => settings,
            [typeof(NavigationService)] = () => navigation, [typeof(ToastService)] = () => new ToastService(),
            [typeof(CollectionsViewModel)] = () => new CollectionsViewModel(collections, catalog),
            [typeof(CollectionEditorViewModel)] = () => new CollectionEditorViewModel(collections, catalog, settings, auth),
        }));
        try
        {
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1280, 900)); window.AppWindow.Show(false);
            await Task.Delay(160);
            await Case("template rendered contract", () => TemplateAsync(frame, window));
            await Case("MDBList actual debounce/Top supersession", () => DiscoveryAsync(frame, window, wire));
            await Case("manual actual failed add/remove retention", () => ManualAsync(frame, window, wire));
            await Case("imported successful Save stays clean", () => ImportedSaveAsync(frame, window, wire, navigation));
            await Case("artwork draft preview and Delete Cancel", () => ArtworkAsync(frame, window, wire));
            if (Failures.Count != 0) throw new InvalidOperationException(string.Join("; ", Failures));
            Program.Log("PASS: finite collection template, discovery, mutation, imported Save and artwork acceptance.");
        }
        finally { wire.SearchGate?.TrySetResult(Reply(new { configured = true, items = Array.Empty<object>() })); wire.PosterGate?.TrySetResult(Reply(new { message = "Fixture teardown" }, HttpStatusCode.UnprocessableEntity)); if (frame.Content is CollectionEditorPage pending) for (var n = 0; n < 40 && pending.ViewModel.IsSaving; n++) await Task.Delay(25); frame.Content = null; navigation.Frame = null; window.Close(); field.SetValue(null, previous); }
    }
    private static async Task Case(string name, Func<Task> action)
    { var previousFailures = Failures.Count; try { await action(); Program.Log((Failures.Count == previousFailures ? "PASS case " : "FAIL checks ") + name); } catch (Exception ex) { Failures.Add(name + ": " + ex.Message); Program.Log("FAIL case " + name + ": " + ex); } }
    private static async Task TemplateAsync(Frame frame, Window window)
    {
        foreach (var width in new[] { 1280, 460 })
        {
            await Viewport(frame, window, width); var page = new CollectionsPage(); frame.Content = page; await Layout();
            await page.ViewModel.LoadTemplateFlowAsync();
            var showing = (Task)Call(page, "ShowCollectionTemplateGalleryAsync")!;
            await Until(() => Read(page, "_templateDialog") is ContentDialog dialog && All<Button>(dialog).Any(b => b.Name == "CloseButton"));
            var dialog = (ContentDialog)Read(page, "_templateDialog")!;
            try
            {
                await Layout(); await Capture(dialog, $"collections-template-gallery-{width}.png");
                var shell = All<Border>(dialog).Single(b => b.Name == "BackgroundElement");
                var title = All<TextBlock>(dialog).Single(t => t.Text == Wire.Template.Title);
                var card = All<Button>(dialog).Single(b => AutomationProperties.GetName(b) == "Use " + Wire.Template.Title + " collection template");
                var surface = (Border)card.Content;
                Program.Log($"TRACE collection gallery viewport={frame.XamlRoot.Size}, shell={shell.ActualWidth}x{shell.ActualHeight}, title={title.FontSize}/{title.FontWeight.Weight}, radius={surface.CornerRadius.TopLeft}, pad={surface.Padding.Left}");
                for (DependencyObject? ancestor = shell; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
                    if (ancestor is FrameworkElement element) Program.Log($"TRACE gallery ancestor {element.GetType().Name}/{element.Name} actual={element.ActualWidth}, width={element.Width}, margin={element.Margin}, min={element.MinWidth}, max={element.MaxWidth}, border={(element as Border)?.BorderThickness}");
                Probe(Math.Abs(shell.ActualWidth - Math.Min(width - 32, width >= 1024 ? 896 : 768)) < 1, "template gallery width is not the official viewport-minus32/896 contract");
                Probe(title.FontSize == 14 && title.FontWeight.Weight == 500 && surface.CornerRadius.TopLeft == 10 && surface.Padding.Left == 16, "template card title/tile/padding/corners retain the obsolete hierarchy");
                Probe(All<Border>(card).Any(b => b.Width == 40 && b.Height == 40) && All<TextBlock>(card).Any(t => t.Text == "Profile") && All<TextBlock>(card).Any(t => t.Text.Contains("syncs daily")), "template40px icon/Profile badge/sync summary missing");
                Click(card); await Layout(); await Capture(dialog, $"collections-template-config-{width}.png");
                var posterShell = All<Border>(dialog).Single(b => b.Width == 56 && b.Height == 80 && b.Child is Image); var poster = (Image)posterShell.Child; posterShell.StartBringIntoView(); await Layout(); Program.Log($"TRACE template poster shell={posterShell.ActualWidth}x{posterShell.ActualHeight}, image={poster.ActualWidth}x{poster.ActualHeight}, uri={(poster.Source as BitmapImage)?.UriSource}"); Check(posterShell.ActualWidth == 56 && posterShell.ActualHeight == 80, "template poster wrapper is not the rendered56x80 role"); var posterClip = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(posterShell).Clip as Microsoft.UI.Composition.CompositionGeometricClip; Probe(posterClip?.Geometry is Microsoft.UI.Composition.CompositionRoundedRectangleGeometry crop && crop.Size.X == 56 && crop.Size.Y == 80 && crop.CornerRadius.X == 10, "template decoded artwork overflows its56x80 rounded crop");
                Check(poster.Source is BitmapImage bitmap && bitmap.UriSource.AbsoluteUri == Wire.PosterUrl, "template poster is not resolved to the fake server URL");
                await Until(() => poster.Source is BitmapImage { PixelWidth: 256, PixelHeight: 256 });
                await Capture(dialog, $"collections-template-config-artwork-{width}.png");
                Check(All<Button>(dialog).Any(b => Equals(b.Content, "Create Collection") && b.ActualWidth > 0), "template primary action is not realized");
            }
            finally { dialog.Hide(); await showing; frame.Content = null; }
        }
    }
    private static async Task DiscoveryAsync(Frame frame, Window window, Wire wire)
    {
        await Viewport(frame, window, 460); var page = new CollectionsPage();
        var title = new TextBox(); var url = new TextBox(); var browser = (FrameworkElement)Call(page, "BuildMDBListBrowser", title, url)!;
        frame.Content = browser; await Layout(); var search = All<TextBox>(browser).Single();
        wire.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); search.Text = "ab";
        await Until(() => wire.SearchCalls == 1); Check(wire.SearchQuery == "?q=ab", "two-character typed search did not issue the exact scoped query");
        Click(All<Button>(browser).Single(b => Equals(b.Content, "Top lists"))); await Until(() => wire.TopCalls == 1 && !page.ViewModel.IsSearchingMdblist);
        var cleared = search.Text.Length == 0;
        wire.SearchGate.TrySetResult(Reply(new { configured = true, items = new[] { Wire.List("stale", "Stale search") } }));
        await Layout(); await Capture(frame, "collections-mdblist-top-after-deferred-search-460.png");
        Check(page.ViewModel.MdblistResults.Single().Id == 2 && !All<TextBlock>(browser).Any(t => t.Text.Contains("Stale search")), "late search replaced Top results");
        Probe(cleared, "Top lists did not clear the typed query as the pinned mode transition requires");
        Click(All<Button>(browser).Single(b => Equals(b.Content, "Use")));
        Check(title.Text == "Top list" && url.Text == "https://mdblist.com/lists/fixture/top/json", "actual discovery pick did not fill title and JSON URL");
        wire.SearchGate = null; frame.Content = null;
    }
    private static async Task<CollectionEditorPage> Editor(Frame frame, Window window, string id)
    {
        await Viewport(frame, window, 460); var page = new CollectionEditorPage(); frame.Content = page;
        Set(page, "_editorActive", true); await (Task)Call(page, "LoadEditorAsync", id)!; await Layout();
        Check(page.ViewModel.ErrorMessage == null && !page.ViewModel.IsReadOnly, "fixture collection did not load editable"); return page;
    }
    private static async Task ManualAsync(Frame frame, Window window, Wire wire)
    {
        var page = await Editor(frame, window, "manual"); var original = page.ViewModel.ManualItems.Single();
        await page.ViewModel.SearchItemsCommand.ExecuteAsync("new"); await Layout();
        wire.RejectManual = true; var add = All<Button>((DependencyObject)page.FindName("SearchResultsPanel")).Single(b => Equals(b.Content, "Add"));
        Click(add); await Until(() => wire.AddCalls == 1 && !page.ViewModel.IsManualMutationPending); await Layout();
        Check(page.ViewModel.ErrorMessage != null && page.ViewModel.ManualItems.Single() == original, "failed actual add changed retained manual rows");
        var remove = All<Button>((DependencyObject)page.FindName("ManualItemsPanel")).Single(b => ToolTipService.GetToolTip(b)?.ToString() == "Remove");
        Click(remove); await Until(() => wire.RemoveCalls == 1 && !page.ViewModel.IsManualMutationPending); await Layout();
        Check(page.ViewModel.ErrorMessage != null && page.ViewModel.ManualItems.Single() == original, "failed actual remove discarded existing manual row");
        await Capture(frame, "collections-manual-failed-add-remove-460.png"); wire.RejectManual = false; frame.Content = null;
    }
    private static async Task ImportedSaveAsync(Frame frame, Window window, Wire wire, NavigationService navigation)
    {
        foreach (var width in new[] { 1280, 460 })
        {
            var initial = await Editor(frame, window, "imported"); await Viewport(frame, window, width);
            await Capture(frame, $"collections-imported-initial-{width}.png"); var heading = (TextBlock)initial.FindName("PageTitle"); var name = (TextBox)initial.FindName("NameTextBox"); var fieldGrid = (Grid)initial.FindName("DisplayFilterGrid"); var filters = (ComboBox)initial.FindName("MediaFilterCombo"); var body = initial.FindName("ImportedEditorSurface") as Border; Program.Log($"TRACE imported layout viewport={frame.XamlRoot.Size}, title={heading.FontSize}/{heading.LineHeight}, name={name.ActualWidth}x{name.ActualHeight}, filters=col{Grid.GetColumn(filters)}/row{Grid.GetRow(filters)}, bodyCorner={body?.CornerRadius.TopLeft}, sectionCorner={((Border)initial.FindName("BasicInfoSection")).CornerRadius.TopLeft}, padding={((Border)initial.FindName("BasicInfoSection")).Padding}"); Probe(heading.FontSize == (width < 640 ? 32 : 48) && name.ActualHeight == 44 && name.HorizontalAlignment == HorizontalAlignment.Stretch && body?.CornerRadius.TopLeft == 24 && ((Border)initial.FindName("BasicInfoSection")).CornerRadius.TopLeft == 0, "imported heading/full Name/single divided source panel mismatch"); Probe(Grid.GetRow(filters) == (width < 640 ? 1 : 0) && Grid.GetColumn(filters) == (width < 640 ? 0 : 1), "imported Watch/Content responsive grid mismatch"); frame.Content = null;
        }
        await Viewport(frame, window, 460); navigation.Navigate<CollectionsPage>(); await Layout(); navigation.Navigate<CollectionEditorPage>("imported");
        await Until(() => frame.Content is CollectionEditorPage p && !p.ViewModel.IsLoading && p.ViewModel.CollectionId == "imported"); await Layout();
        var page = (CollectionEditorPage)frame.Content; ((TextBox)page.FindName("NameTextBox")).Text = "Saved imported name"; await Layout();
        Click((Button)page.FindName("DockSaveButton")); await Until(() => wire.SaveCalls == 1 && !page.ViewModel.IsSaving); await Layout();
        await Capture(frame, "collections-imported-after-save-460.png");
        Probe(ReferenceEquals(frame.Content, page), "successful imported Save navigated away; pinned source stays in the editor");
        Probe(((FrameworkElement)page.FindName("CollectionDirtyDock")).Visibility == Visibility.Collapsed && page.ViewModel.Name == "Saved imported name", "successful imported Save did not establish a clean saved baseline");
        frame.Content = null;
    }
    private static async Task ArtworkAsync(Frame frame, Window window, Wire wire)
    {
        var page = await Editor(frame, window, "imported");
        // Local file bytes only. Native file picker chrome is not automated by this test.
        var bytes = ArtworkAsset.Bytes;
        var filePath = Path.Combine(Program.ResultDirectory, "collection-local-poster.png"); await File.WriteAllBytesAsync(filePath, bytes);
        await (Task)Call(page, "ApplyPosterFileAsync", await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath)))!; await Layout();
        var preview = (Image)page.FindName("ImportedPosterPreview"); await Until(() => preview.Source is BitmapImage { PixelWidth: 256 }); preview.StartBringIntoView(); await Layout(); Program.Log($"TRACE artwork initial source={preview.Source?.GetType().Name}, pixels={(preview.Source as BitmapImage)?.PixelWidth}, geometry={preview.ActualWidth}x{preview.ActualHeight}, visibility={preview.Visibility}");
        await Capture(frame, "collections-artwork-draft-460.png");
        var previewFrame = page.FindName("ImportedPosterPreviewFrame") as Border; var previewClip = previewFrame == null ? null : Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(previewFrame).Clip as Microsoft.UI.Composition.CompositionGeometricClip; Program.Log($"TRACE previewFrame={previewFrame?.ActualWidth}x{previewFrame?.ActualHeight}, clip={previewClip?.Geometry?.GetType().Name}"); Probe(previewFrame?.ActualHeight == 128 && previewClip?.Geometry is Microsoft.UI.Composition.CompositionRoundedRectangleGeometry geometry && geometry.Size.Y == 128 && geometry.CornerRadius.X == 12, "picked local artwork is not cropped inside the actual128px rounded draft preview");
        Call(page, "DeleteCollection_Click", page, new RoutedEventArgs());
        await Until(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).Any(p => All<ContentDialog>(p.Child).Any()));
        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).SelectMany(p => All<ContentDialog>(p.Child)).Single();
        Click(All<Button>(dialog).Single(b => b.Name == "CloseButton")); await Layout();
        Check(wire.Deletes == 0 && page.ViewModel.PosterFileBytes?.Length == bytes.Length, "Delete Cancel wrote or discarded artwork draft");
        Click((Button)page.FindName("ImportedRemoveSelectedPoster")); await Layout();
        Check(page.ViewModel.PosterFileBytes == null && ((Image)page.FindName("ImportedPosterPreview")).Source == null && wire.Deletes == 0,
            "Remove selected file deleted saved artwork or retained the local preview");
        var drop = new Windows.ApplicationModel.DataTransfer.DataPackage();
        drop.SetStorageItems(new[] { await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath)) });
        await (Task)Call(page, "ApplyPosterDropAsync", drop.GetView())!; await Layout();
        Check(page.ViewModel.PosterFileBytes?.SequenceEqual(bytes) == true && ((Image)page.FindName("ImportedPosterPreview")).Source is BitmapImage { PixelWidth: 256 },
            "actual storage-item drop callback did not produce the selected local artwork draft");
        preview = (Image)page.FindName("ImportedPosterPreview"); preview.StartBringIntoView(); await Layout();
        await Capture(frame, "collections-artwork-drop-preview-460.png");
        wire.PosterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = (Button)page.FindName("DockSaveButton"); Program.Log($"TRACE artwork beforeSave enabled={save.IsEnabled}, dock={((FrameworkElement)page.FindName("CollectionDirtyDock")).Visibility}, readOnly={page.ViewModel.IsReadOnly}, saving={page.ViewModel.IsSaving}, rulesValid={Read(page, "_rulesEditor")?.GetType().GetProperty("IsValid")?.GetValue(Read(page, "_rulesEditor"))}, name={page.ViewModel.Name}, limit={page.ViewModel.MaxItemsText}, file={page.ViewModel.PosterFileName}"); Click(save); await Layout(); Program.Log($"TRACE artwork afterSave saving={page.ViewModel.IsSaving}, error={page.ViewModel.ErrorMessage}, saveCalls={wire.SaveCalls}, posterCalls={wire.PosterCalls}"); await Until(() => wire.PosterCalls == 1); await Layout();
        Check(!save.IsEnabled && page.ViewModel.PosterFileBytes?.SequenceEqual(bytes) == true, "pending artwork Save permits duplicate writes or discards its draft");
        wire.PosterGate.TrySetResult(Reply(new { message = "Fixture poster rejected" }, HttpStatusCode.UnprocessableEntity));
        await Until(() => !page.ViewModel.IsSaving); await Layout();
        Check(page.ViewModel.ErrorMessage != null && ReferenceEquals(frame.Content, page) && page.ViewModel.PosterFileBytes?.SequenceEqual(bytes) == true && preview.Source != null,
            "rejected poster write lost the editor or selected preview");
        wire.PosterGate = null; Click(save); await Until(() => wire.PosterCalls == 2 && !page.ViewModel.IsSaving && !page.ViewModel.IsLoading); await Layout();
        Check(page.ViewModel.ErrorMessage == null && page.ViewModel.PosterFileBytes == null && ((FrameworkElement)page.FindName("CollectionDirtyDock")).Visibility == Visibility.Collapsed,
            "successful artwork retry did not establish a clean saved editor");
        await (Task)Call(page, "ApplyPosterDropAsync", drop.GetView())!; await Layout();
        Call(page, "Discard_Click", page, new RoutedEventArgs()); await Until(() => !page.ViewModel.IsLoading && page.ViewModel.PosterFileBytes == null); await Layout();
        Check(((Image)page.FindName("ImportedPosterPreview")).Source == null && wire.PosterCalls == 2, "Discard retained selected artwork or wrote another poster");
        frame.Content = null;
    }
    private static async Task Viewport(Frame frame, Window window, int width)
    { window.AppWindow.ResizeClient(new(width, 900)); frame.Width = width; await Layout(); var actual = window.AppWindow.ClientSize; Check(actual.Width == width && actual.Height == 900 && Math.Abs(frame.XamlRoot.Size.Width - width) < 1, "collection fixture viewport is clamped or mislabeled"); }
    private static async Task Layout() { await Task.Delay(250); }
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 200 && !ready(); n++) await Task.Delay(25); Check(ready(), "native collection operation did not settle"); }
    private static void Probe(bool ok, string message) { if (!ok) { Failures.Add(message); Program.Log("FAIL assertion: " + message); } }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static object? Call(object obj, string method, params object?[] args) => obj.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, args);
    private static object? Read(object obj, string name) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(obj, value);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T match) yield return match; for (var n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++) foreach (var item in All<T>(VisualTreeHelper.GetChild(root, n))) yield return item; }
    private static async Task Capture(FrameworkElement frame, string name)
    {
        await Layout(); frame.Clip = new RectangleGeometry { Rect = new(0, 0, frame.ActualWidth, frame.ActualHeight) }; var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(frame);
        Program.Log($"TRACE {name} actualFrame={frame.ActualWidth}x{frame.ActualHeight}, bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}, XamlRoot={frame.XamlRoot.Size}");
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && (frame is not Frame || bitmap.PixelWidth == (int)frame.Width && bitmap.PixelHeight == 900), "collection screenshot is not the actual rendered surface/client boundary");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }
    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) { var reply = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body)) }; reply.Headers.ETag = new("\"collections-fixture\""); return reply; }
    // An isolated loopback image endpoint exercises the real WinUI BitmapImage pipeline.
    // The controlled PNG is shared with the reference fixture; no production artwork is accessed.
    private sealed class ArtworkAsset : IAsyncDisposable
    {
        internal static readonly byte[] Bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAQAAAAEACAYAAABccqhmAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAM6SURBVHhe7daxEYJQAARRsRUCHUMicjqmD5ugCA0/+W9h3wuvgJ1bfsd7PMj6bOc8EfKcB6BDACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBs+X/3MY90vK51ngjxACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACBMACDsBoKICZScmj7NAAAAAElFTkSuQmCC");
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        internal string Url { get; }
        internal ArtworkAsset()
        {
            _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/collection-poster.png";
            _serving = ServeAsync();
        }
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var stream = connection.GetStream();
                    var request = new byte[8192]; await stream.ReadAsync(request, _stop.Token);
                    var headers = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {Bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _stop.Token); await stream.WriteAsync(Bytes, _stop.Token); await stream.FlushAsync(_stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Net.Sockets.SocketException) when (_stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _listener.Stop(); await _serving; _stop.Dispose(); }
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal static string PosterUrl = "";
        internal static readonly CollectionTemplate Template = new() { Id = "fixture", Title = "A current collection template with a long title", Description = "Collection template description", Source = "tmdb", MediaKind = "movie", Icon = "🎬", RequiresProfile = true, DefaultSyncSchedule = "0 0 * * *", DefaultLimit = 100, PosterPath = "/artwork/template.png" };
        internal TaskCompletionSource<HttpResponseMessage>? SearchGate, PosterGate; internal int SearchCalls, TopCalls, AddCalls, RemoveCalls, SaveCalls, Deletes, PosterCalls; internal string? SearchQuery; internal bool RejectManual; internal string ImportedName = "Imported fixture";
        internal static object List(string id, string name) => new { id = id == "top" ? 2 : 1, name, user_name = "Fixture", mediatype = "movie", items = 12, likes = 3, url = "https://mdblist.com/lists/fixture/" + id };
        private object Collection(string id) => new { id, name = id == "manual" ? "Manual fixture" : ImportedName, collection_type = id == "manual" ? "manual" : "mdblist", creator_profile_id = "fixture", source_url = "https://mdblist.com/lists/fixture/top/", source_config = new { limit = 100 }, updated_at = "2026-10-01T00:00:00Z" };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri?.Host == "collections-acceptance.invalid", "collection fixture attempted external network"); var path = request.RequestUri!.AbsolutePath; Program.Log($"TRACE collection wire {request.Method} {path}");
            if (path == "/api/v2/collections/imported/poster")
            {
                PosterCalls++; Check(request.Method == HttpMethod.Put && request.Content is MultipartFormDataContent, "artwork save is not the canonical multipart PUT");
                var poster = ((MultipartFormDataContent)request.Content!).Single();
                Check(poster.Headers.ContentDisposition?.Name?.Trim('"') == "poster" && poster.Headers.ContentType?.MediaType == "image/png" && (await poster.ReadAsByteArrayAsync(ct)).Length > 0,
                    "artwork payload lost its file field, MIME type or bytes");
                return PosterGate is null ? Reply(Collection("imported")) : await PosterGate.Task.WaitAsync(ct);
            }
            if (path.EndsWith("/mdblist/search")) { SearchCalls++; SearchQuery = request.RequestUri.Query; return await SearchGate!.Task.WaitAsync(ct); }
            if (path.EndsWith("/mdblist/top")) { TopCalls++; return Reply(new { configured = true, items = new[] { List("top", "Top list") } }); }
            if (path == "/api/v2/collections/capabilities") return Reply(new { item_reorder = false, imports = true, import_sources = new[] { "tmdb", "mdblist" }, artwork = true });
            if (path == "/api/v2/collections/templates") return Reply(new { categories = new[] { new { category = "featured", label = "Featured", templates = new[] { new { id = Template.Id, title = Template.Title, description = Template.Description, source = "tmdb", media_kind = "movie", icon = Template.Icon, requires_profile = true, default_sync_schedule = Template.DefaultSyncSchedule, default_limit = 100, poster_path = PosterUrl } } } } });
            if (path == "/api/v2/collections") return Reply(new { items = new[] { Collection("manual"), Collection("imported") }, groups = Array.Empty<object>() });
            if (path == "/api/v2/collections/manual/items") return Reply(new { items = new[] { new { media_item_id = "one", content_id = "one", title = "Retained title", type = "movie", position = 0 } }, page = new { has_more = false } });
            if (path.StartsWith("/api/v2/collections/manual/items/")) { if (request.Method == HttpMethod.Put) AddCalls++; else if (request.Method == HttpMethod.Delete) RemoveCalls++; return Reply(new { message = "Fixture manual write rejected" }, RejectManual ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK); }
            if (path is "/api/v2/collections/manual" or "/api/v2/collections/imported")
            {
                if (request.Method == HttpMethod.Delete) Deletes++;
                if (request.Method == HttpMethod.Patch) { SaveCalls++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); if (body.RootElement.TryGetProperty("name", out var name)) ImportedName = name.GetString()!; }
                return Reply(Collection(path.EndsWith("manual") ? "manual" : "imported"));
            }
            if (path == "/api/v2/catalog") return Reply(new { items = new[] { new { content_id = "new", title = "New title", type = "movie", year = 2026 } }, page = new { has_more = false } });
            return Reply(new { items = Array.Empty<object>(), libraries = Array.Empty<object>(), page = new { has_more = false } });
        }
    }
}
