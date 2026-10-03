using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class BrowseWizardPresentationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)serviceField.GetValue(null)!;
        var cover = await CoverBytes();
        using var artwork = new ArtworkServer(cover);
        using var wire = new Wire(cover, artwork.Url);
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://wizard-presentation.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("wizard-owner", profile: new() { Id = "wizard-owner", Name = "Fixture" });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client);
        var navigation = new NavigationService();
        var localSettings = new SettingsService(Path.Combine(Program.ResultDirectory, "wizard-presentation-settings"));
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "wizard-presentation-images"));
        var presentation = new UICustomizationService(new SettingsApi(client));
        var overlays = new CardOverlayService(new SettingsApi(client));
        serviceField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth,
            [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections,
            [typeof(SmartCollectionWizardViewModel)] = () => new SmartCollectionWizardViewModel(catalog, collections, new AuthApi(client), auth),
            [typeof(NavigationService)] = () => navigation, [typeof(SettingsService)] = () => localSettings,
            [typeof(UICustomizationService)] = () => presentation, [typeof(CardOverlayService)] = () => overlays,
            [typeof(HttpClient)] = () => http, [typeof(ImageService)] = () => images,
        }));
        var owner = new Grid(); var window = new Window { Content = owner }; Frame? frame = null; ContentDialog? activeDialog = null;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 900)); window.AppWindow.Show(false); await Task.Delay(100);
            foreach (var width in new[] { 460, 1280 })
            {
                var scale = owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                wire.Reset(); frame = new Frame(); owner.Children.Add(frame); navigation.Frame = frame;
                frame.Navigate(typeof(Page));
                frame.Navigate(typeof(SmartCollectionWizardPage), new SmartCollectionWizardNavigationArgs(CollectionId: "presentation-smart"));
                var page = (SmartCollectionWizardPage)frame.Content; var model = page.ViewModel;
                var poster = (Image)page.FindName("WizardPosterImage");
                await Until(() => !model.IsLoading && !model.IsPreviewing && model.PreviewMediaItems.Count == 1 && poster.Source is BitmapImage);
                page.UpdateLayout(); await Task.Delay(120);
                if (Math.Abs(page.ActualWidth - width) > .5 || model.IsReadOnly || model.MediaScope != "audiobook")
                    throw new InvalidOperationException("Wizard must mount its editable audiobook collection at the actual requested logical width.");
                Invoke((Button)page.FindName("FiltersButton"));
                var sheet = (SlideSheet)page.FindName("WizardFiltersSheet");
                await Until(() => sheet.IsOpen && sheet.ActualWidth > 0); await Task.Delay(250);
                var expectedSheetWidth = width == 460 ? 345d : 448d;
                if (Math.Abs(sheet.PreferredWidth - expectedSheetWidth) > .5)
                    throw new InvalidOperationException("Mounted Wizard filter sheet must use75% narrow and448px wide source dimensions.");
                if (!Descendants<TextBlock>(sheet).Any(text => text.Text == "Author") || Descendants<TextBlock>(sheet).Any(text => text.Text == "Minimum IMDb Rating"))
                    throw new InvalidOperationException("Mounted audiobook Wizard filters must show Author and omit video-only IMDb input.");
                await Capture(owner, $"browse-wizard-filters-audiobook-{width}.png");
                var done = Descendants<Button>(sheet).Single(button => button.Content?.ToString() == "Done"); Invoke(done);
                await Until(() => !sheet.IsOpen); await Task.Delay(250);
                Invoke((Button)page.FindName("ContinueButton"));
                var details = (Border)page.FindName("DetailsStepPanel");
                await Until(() => details.Visibility == Visibility.Visible && poster.ActualWidth > 0 && poster.Source is BitmapImage { PixelWidth: > 0 });
                page.UpdateLayout(); await Task.Delay(120);
                var detailsGrid = (Grid)page.FindName("DetailsGrid"); var posterPanel = (FrameworkElement)page.FindName("PosterDetailsPanel");
                Program.Log($"Wizard mounted {width}: sheet={sheet.PreferredWidth:R}, detailsColumn={Grid.GetColumn(posterPanel)}, row={Grid.GetRow(posterPanel)}, columns={detailsGrid.ColumnDefinitions.Count}, existingPoster={poster.ActualWidth:R}x{poster.ActualHeight:R}.");
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_WIZARD_ARTWORK_ACTIONS") == "1")
                {
                    var remove = page.FindName("WizardRemoveSelectedPoster") as Button;
                    var deleteImage = page.FindName("WizardDeletePoster") as Button;
                    if (remove == null || deleteImage == null) throw new InvalidOperationException("Wizard lacks the separate current Remove selected file and Delete image actions.");
                    model.SetPosterFile("browse-cover.png", cover, "image/png");
                    await (Task)typeof(SmartCollectionWizardPage).GetMethod("UpdatePosterPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                    page.UpdateLayout(); await Task.Delay(120); Invoke(remove);
                    await Until(() => model.PosterFileBytes == null && poster.Source is BitmapImage { UriSource: not null });
                    if (wire.ImageDeletes != 0 || wire.Deletes != 0) throw new InvalidOperationException("Removing a local draft must not delete saved artwork or its collection.");
                    page.UpdateLayout(); await Task.Delay(120); Invoke(deleteImage);
                    await Until(() => wire.ImageDeletes == 1 && !model.IsSaving && poster.Source == null);
                    if (wire.Deletes != 0 || ((Button)page.FindName("WizardPosterDropTarget")).Visibility != Visibility.Visible) throw new InvalidOperationException("Deleting saved artwork must reveal the upload chooser and retain its collection.");
                    Program.Log($"PASS: actual Wizard{width} local removal restores saved image without writes; saved image DELETE clears preview without collection DELETE.");
                    frame.Navigate(typeof(Page)); await Task.Delay(150); owner.Children.Remove(frame); frame = null;
                    continue;
                }
                if (width == 460 ? Grid.GetRow(posterPanel) != 1 : Grid.GetColumn(posterPanel) != 1)
                    throw new InvalidOperationException("Mounted narrow Wizard details must stack artwork; wide details must keep the artwork column.");
                var uploadFrame = page.FindName("WizardPosterFrame") as Border;
                if (uploadFrame == null || Math.Abs(uploadFrame.ActualHeight - 128) > .5 || Math.Abs(uploadFrame.ActualWidth - posterPanel.ActualWidth) > .5)
                    throw new InvalidOperationException("Wizard artwork must use the current full-width128px upload/preview frame instead of a tall portrait chooser.");
                if (Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(uploadFrame).Clip is not Microsoft.UI.Composition.CompositionGeometricClip crop
                    || crop.Geometry is not Microsoft.UI.Composition.CompositionRoundedRectangleGeometry rounded || Math.Abs(rounded.Size.Y - 128) > .5)
                    throw new InvalidOperationException("Decoded Wizard artwork must remain clipped inside its128px rounded frame.");
                await Capture(owner, $"browse-wizard-details-existing-{width}.png");
                Invoke((Button)page.FindName("WizardDeleteButton"));
                await Until(() => Dialogs(owner.XamlRoot).Any(dialog => dialog.Title?.ToString() == "Delete collection?"));
                activeDialog = Dialogs(owner.XamlRoot).Single(dialog => dialog.Title?.ToString() == "Delete collection?");
                if (activeDialog.XamlRoot != page.XamlRoot || activeDialog.DefaultButton != ContentDialogButton.Close)
                    throw new InvalidOperationException("Actual Wizard delete dialog lost its page owner or default Cancel action.");
                Invoke(Descendants<Button>(activeDialog).Single(button => button.Name == "CloseButton"));
                await Until(() => !Dialogs(owner.XamlRoot).Contains(activeDialog)); activeDialog = null;
                if (wire.Deletes != 0 || !ReferenceEquals(frame.Content, page)) throw new InvalidOperationException("Actual Delete→Cancel wrote or left the Wizard.");
                var title = Descendants<TextBox>(details).Single(box => box.PlaceholderText == "e.g. Saturday Night Movies");
                title.Text = "Saved audiobook fixture"; await Until(() => model.Title == title.Text);
                // Payload boundary shared by the actual picker/drop tails. This does not claim OS picker automation.
                model.SetPosterFile("browse-cover.png", cover, "image/png");
                await (Task)typeof(SmartCollectionWizardPage).GetMethod("UpdatePosterPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                await Until(() => poster.Source is BitmapImage { PixelWidth: 256 } && page.CanSave);
                var draftBitmap = poster.Source;
                await Capture(owner, $"browse-wizard-details-selected-{width}.png");
                wire.FailPosterOnce = true; Invoke((Button)page.FindName("SaveButton"));
                await Until(() => wire.PosterWrites == 1 && !model.IsSaving && model.ErrorMessage != null);
                if (!ReferenceEquals(frame.Content, page) || !ReferenceEquals(model.PosterFileBytes, cover) || model.PosterFileName != "browse-cover.png" || !ReferenceEquals(poster.Source, draftBitmap) || model.Title != "Saved audiobook fixture")
                    throw new InvalidOperationException("Actual failed Wizard artwork save must retain title/file bytes/current preview and remain editable on its page.");
                Invoke((Button)page.FindName("SaveButton"));
                await Until(() => wire.PosterWrites == 2 && frame.Content is Page && frame.Content is not SmartCollectionWizardPage && !model.IsSaving);
                if (wire.MetadataWrites != 2 || wire.Deletes != 0 || model.PosterFileBytes != null || model.PosterFileName != null)
                    throw new InvalidOperationException("Actual Wizard retry must write retained artwork once more, clear saved file draft and navigate back to its origin.");
                Program.Log($"PASS: actual Wizard{width} audiobook filter/details composition, existing+selected rendered artwork, Delete→Cancel0writes, poster503draft retention, retry multipart exact bytes and real back destination. OS picker/drop input is outside this payload-boundary proof.");
                frame.Navigate(typeof(Page)); await Task.Delay(150); owner.Children.Remove(frame); frame = null;
            }
        }
        finally
        {
            activeDialog?.Hide(); frame?.Navigate(typeof(Page)); serviceField.SetValue(null, previous);
            await Task.Delay(250); navigation.Frame = null; owner.Children.Clear(); window.Close();
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 200 && !ready(); n++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Actual Wizard presentation/save did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var n = 0; n < VisualTreeHelper.GetChildrenCount(parent); n++) { var child = VisualTreeHelper.GetChild(parent, n); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static IEnumerable<ContentDialog> Dialogs(XamlRoot root)
    {
        var found = new HashSet<ContentDialog>();
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            foreach (var dialog in new[] { popup.Child }.Concat(Descendants<DependencyObject>(popup.Child)).OfType<ContentDialog>()) found.Add(dialog);
            for (DependencyObject? node = popup.Child; node != null; node = VisualTreeHelper.GetParent(node)) if (node is ContentDialog dialog) found.Add(dialog);
        }
        return found;
    }
    private static async Task<byte[]> CoverBytes()
    {
        var pixels = new byte[256 * 256 * 4]; var colors = new[] { (R: 246, G: 67, B: 40), (R: 32, G: 117, B: 218), (R: 247, G: 205, B: 56), (R: 30, G: 175, B: 91) };
        for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++) { var color = colors[(y / 128) * 2 + x / 128]; var at = (y * 256 + x) * 4; pixels[at] = (byte)color.B; pixels[at + 1] = (byte)color.G; pixels[at + 2] = (byte)color.R; pixels[at + 3] = 255; }
        using var stream = new MemoryStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream()); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 256, 256, 96, 96, pixels); await encoder.FlushAsync(); return stream.ToArray();
    }
    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element); var pixels = await bitmap.GetPixelsAsync();
        if (bitmap.PixelWidth == 0 || pixels.Length == 0) throw new InvalidOperationException("Mounted Wizard has no rendered capture pixels.");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> values) : IServiceProvider
    { public object? GetService(Type type) => values.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }
    private sealed class Wire(byte[] cover, string posterUrl) : HttpMessageHandler
    {
        internal int MetadataWrites, PosterWrites, Deletes, ImageDeletes; internal bool FailPosterOnce; private string name = "Fixture audiobook collection";
        internal void Reset() { MetadataWrites = PosterWrites = Deletes = ImageDeletes = 0; FailPosterOnce = false; name = "Fixture audiobook collection"; }
        private object Collection() => new { id = "presentation-smart", creator_profile_id = "wizard-owner", name, collection_type = "smart", poster_url = ImageDeletes == 0 ? posterUrl : null, include_in_server_collections = true, query_definition = new { library_ids = new[] { 11 }, media_scope = "audiobook", match = "all", groups = new[] { new { match = "all", rules = new[] { new { field = "author", op = "is", value = "Fixture Author" } } } }, sort = new { field = "title", order = "asc" }, limit = 10 } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "wizard-presentation.invalid") throw new InvalidOperationException("Wizard API fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath; var status = HttpStatusCode.OK; object body;
            Program.Log("Wizard presentation actual HTTP: " + request.Method + " " + request.RequestUri.PathAndQuery);
            if (path == "/api/v2/collections/presentation-smart" && request.Method == HttpMethod.Patch)
            {
                if (!request.Headers.IfMatch.Any()) throw new InvalidOperationException("Wizard metadata update omitted its collection revision.");
                var payload = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
                name = payload.GetProperty("name").GetString()!;
                if (name != "Saved audiobook fixture" || payload.GetProperty("query_definition").GetProperty("media_scope").GetString() != "audiobook") throw new InvalidOperationException("Wizard save payload lost title or audiobook draft scope.");
                MetadataWrites++; body = Collection();
            }
            else if (path == "/api/v2/collections/presentation-smart/poster" && request.Method == HttpMethod.Put)
            {
                var part = ((MultipartFormDataContent)request.Content!).Single(value => value.Headers.ContentDisposition?.Name?.Trim('"') == "poster");
                if (part.Headers.ContentType?.MediaType != "image/png" || part.Headers.ContentDisposition?.FileName?.Trim('"') != "browse-cover.png" || !(await part.ReadAsByteArrayAsync(ct)).SequenceEqual(cover)) throw new InvalidOperationException("Wizard multipart retry altered the selected artwork payload.");
                PosterWrites++;
                if (FailPosterOnce) { FailPosterOnce = false; status = HttpStatusCode.ServiceUnavailable; body = new { error = "fixture_poster", message = "retry poster save" }; }
                else body = Collection();
            }
            else if (path == "/api/v2/collections/presentation-smart/image" && request.Method == HttpMethod.Delete) { ImageDeletes++; body = new { }; }
            else if (path == "/api/v2/collections/presentation-smart" && request.Method == HttpMethod.Delete) { Deletes++; body = new { }; }
            else if (path == "/api/v2/collections/presentation-smart") body = Collection();
            else if (path == "/api/v2/user/libraries") body = new { items = new[] { new { id = 11, name = "Audiobooks", type = "audiobook" } } };
            else if (path == "/api/v2/profiles") body = new { profiles = new[] { new { id = "wizard-owner", name = "Fixture" } } };
            else if (path == "/api/v2/catalog/filters") body = new { authors = new[] { "Fixture Author" }, narrators = Array.Empty<string>(), genres = Array.Empty<string>() };
            else if (path == "/api/v2/catalog") body = new { items = new[] { new { content_id = "fixture-audio", type = "audiobook", title = "Fixture audiobook", author = "Fixture Author" } }, total = 1, window_cursor = "wizard-presentation", page = new { has_more = false } };
            else if (path.StartsWith("/api/v2/settings/")) body = new { items = Array.Empty<object>() };
            else throw new InvalidOperationException("Unexpected Wizard presentation API path: " + path);
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body)) }; response.Headers.ETag = new("\"wizard-fixture\""); return response;
        }
    }
    private sealed class ArtworkServer : IDisposable
    {
        private readonly HttpListener listener = new(); private readonly byte[] cover;
        internal string Url { get; }
        internal ArtworkServer(byte[] cover)
        {
            this.cover = cover; var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            var prefix = $"http://127.0.0.1:{port}/"; Url = prefix + "browse-cover.png"; listener.Prefixes.Add(prefix); listener.Start(); _ = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (listener.IsListening)
                {
                    var request = await listener.GetContextAsync();
                    if (request.Request.Url?.AbsolutePath != "/browse-cover.png" || request.Request.HttpMethod != "GET") { request.Response.StatusCode = 404; request.Response.Close(); continue; }
                    request.Response.ContentType = "image/png"; request.Response.ContentLength64 = cover.Length; await request.Response.OutputStream.WriteAsync(cover); request.Response.Close();
                }
            }
            catch (HttpListenerException) { } catch (ObjectDisposedException) { }
        }
        public void Dispose() { listener.Close(); }
    }
}
