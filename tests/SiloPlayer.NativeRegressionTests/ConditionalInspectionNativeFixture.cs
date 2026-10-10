using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class ConditionalInspectionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalServices = serviceField.GetValue(null);
        var window = new Window { Content = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] } };
        var owner = (Grid)window.Content;
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://inspection-fixture.invalid");
        var toast = new ToastService(); var container = new ToastContainer(); owner.Children.Add(container); toast.Register(container, owner.DispatcherQueue);
        using var services = new ServiceCollection().AddSingleton(new CatalogApi(client)).AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(toast).BuildServiceProvider();
        serviceField.SetValue(null, services);
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1000, 800)); window.AppWindow.Show(false); await Task.Delay(160);
            await RefreshAsync(owner, "wide");
            await MatchAsync(owner, "wide");
            await FilesAsync(owner, handler, "populated", "wide");
            await FilesAsync(owner, handler, "empty", "wide");
            await FilesAsync(owner, handler, "failed", "wide");
            await FilesAsync(owner, handler, "delayed", "wide");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 480)); await Task.Delay(160);
            await RefreshAsync(owner, "narrow-short");
            await MatchAsync(owner, "narrow-short");
            await FilesAsync(owner, handler, "populated", "narrow-short");
            Program.Log("PASS conditional refresh choice/pending/corner dismissal/retry and manga loading/error/empty/size/dividers/viewport/cancellation native cases");
        }
        finally { toast.Unregister(); serviceField.SetValue(null, originalServices); window.Close(); }
        await ConditionalEditorsNativeFixture.RunAsync(parent);
    }
    private static async Task MatchAsync(FrameworkElement owner, string size)
    {
        var dialog = new MatchItemDialog("fixture-movie", "Fixture movie", 2020, "movie", null,
            [new FileVersion { FileId = 1, Resolution = "1080p", CodecVideo = "h264", CodecAudio = "dts", FilePath = "D:/Fixtures/Film/An exceptionally long filename that must remain readable when the dialog narrows.mkv" }], null)
            { XamlRoot = owner.XamlRoot };
        var showing = dialog.ShowAsync(); await Task.Delay(120);
        try
        {
            var shell = Descendants<Border>(dialog).Single(border => border.Name == "BackgroundElement");
            if (Math.Abs(dialog.ActualHeight - owner.XamlRoot.Size.Height) > 1)
                throw new InvalidOperationException($"Match Item shrinks its centering surface: actual{dialog.ActualHeight}, viewport{owner.XamlRoot.Size.Height}.");
            if (Math.Abs(shell.ActualWidth - Math.Min(512, owner.XamlRoot.Size.Width)) > 1)
                throw new InvalidOperationException("Match Item width differs from the rendered WebUI512px desktop/full-width mobile frame.");
            if (dialog.CloseButtonText.Length > 0 || dialog.Title is not Grid || !Descendants<Button>(dialog).Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Close"))
                throw new InvalidOperationException("Match Item must use the corner Close action without a spare Cancel footer.");
            var title = (TextBox)dialog.FindName("TitleBox");
            if (Math.Abs(title.ActualWidth - Math.Min(458, owner.XamlRoot.Size.Width - 54)) > 1 || title.FontSize != 14)
                throw new InvalidOperationException("Match Item search field density differs from the current WebUI.");
            if (Math.Abs(title.ActualHeight - 50) > 1)
                throw new InvalidOperationException($"Match Item headers add unwanted vertical space: actual{title.ActualHeight}, expected50px including the14px label and36px field.");
            if (!Descendants<TextBlock>((DependencyObject)dialog.Content).Any(text => text.Text == "1080p · H264 · DTS"))
                throw new InvalidOperationException("Match Item local media omits the audio format.");
            if (shell.ActualHeight > owner.XamlRoot.Size.Height * .85 + 1)
                throw new InvalidOperationException("Match Item exceeds its85percent viewport bound.");
            var scroller = Descendants<ScrollViewer>((DependencyObject)dialog.Content).First();
            scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(80);
            var search = (Button)dialog.FindName("SearchButton");
            var point = search.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
            if (point.Y < 0 || point.Y + search.ActualHeight > shell.ActualHeight + 1)
                throw new InvalidOperationException("Match Item final Search action cannot be reached in the short viewport.");
            await CaptureAsync(dialog, $"inspection-match-{size}.png");
            var close = Descendants<Button>(dialog).Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Close");
            var closePoint = close.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
            if (Math.Abs(closePoint.Y - 16) > 1 || Math.Abs(closePoint.X + close.ActualWidth - (shell.ActualWidth - 17)) > 1)
                throw new InvalidOperationException("Match Item Close icon escapes or is clipped by the title presenter.");
            Invoke(close); await showing;
            if (dialog.HasAppliedMatch) throw new InvalidOperationException("Read-only Match Item dismissal applied metadata.");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task RefreshAsync(FrameworkElement owner, string size)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var dialog = new RefreshMetadataDialog(mode => { if (mode != "complete") throw new InvalidOperationException("Wrong refresh mode."); calls++; return pending.Task; }) { XamlRoot = owner.XamlRoot };
        var showing = dialog.ShowAsync();
        await Task.Delay(120);
        if (size == "wide")
        {
            var quick = Field<Button>(dialog, "_quickButton"); var completeChoice = Field<Button>(dialog, "_completeButton");
            if (Math.Abs(quick.ActualHeight - 98) > 1 || Math.Abs(completeChoice.ActualHeight - 98) > 1)
                throw new InvalidOperationException("Refresh choice descriptions do not use the WebUI20px line boxes/98px rows.");
            if (Math.Abs(((Grid)dialog.Title).ActualWidth - 462) > 1)
                throw new InvalidOperationException("Refresh header shrinks around its title and leaves Close in the middle.");
        }
        await CaptureAsync(dialog, $"inspection-refresh-{size}.png");
        var complete = Field<Button>(dialog, "_completeButton"); var cancel = Field<Button>(dialog, "_cancelButton");
        Invoke(complete); await UntilAsync(() => calls == 1);
        if (complete.IsEnabled || Field<Button>(dialog, "_quickButton").IsEnabled || cancel.IsEnabled)
            throw new InvalidOperationException("Refresh choices or Cancel stayed interactive while pending.");
        if (Descendants<ProgressRing>((DependencyObject)dialog.Content).Count(ring => ring.IsActive && ring.Visibility == Visibility.Visible) != 2)
            throw new InvalidOperationException("Refresh did not show both pending indicators.");
        await CaptureAsync(dialog, $"inspection-refresh-pending-{size}.png");
        Invoke(Descendants<Button>(dialog).Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Close")); await showing;
        if (pending.Task.IsCompleted) throw new InvalidOperationException("Corner dismissal canceled the queued refresh operation.");
        pending.SetResult(); await Task.Delay(100);
        if (calls != 1) throw new InvalidOperationException("Refresh dispatched duplicate work.");

        var failures = 0;
        dialog = new RefreshMetadataDialog(_ => ++failures == 1 ? Task.FromException(new InvalidOperationException("Fixture refresh rejected")) : Task.CompletedTask) { XamlRoot = owner.XamlRoot };
        showing = dialog.ShowAsync(); await Task.Delay(100);
        Invoke(Field<Button>(dialog, "_quickButton"));
        await UntilAsync(() => failures == 1 && Field<Button>(dialog, "_quickButton").IsEnabled);
        if (!Field<Button>(dialog, "_cancelButton").IsEnabled) throw new InvalidOperationException("Refresh rejection stranded Cancel.");
        await CaptureAsync(dialog, $"inspection-refresh-retry-{size}.png");
        Invoke(Field<Button>(dialog, "_quickButton")); await showing;
        if (failures != 2) throw new InvalidOperationException("Refresh retry was not dispatched.");
    }
    private static async Task FilesAsync(FrameworkElement owner, Wire handler, string scenario, string size)
    {
        handler.Scenario = scenario; handler.Pending = scenario == "delayed" ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        var dialog = new MangaFilesDialog("fixture-series", "An exceptionally long manga title to exercise the inspector heading constraints") { XamlRoot = owner.XamlRoot };
        var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            var body = Field<StackPanel>(dialog, "_body");
            if (scenario == "delayed")
            {
                if (!Field<ProgressRing>(dialog, "_progress").IsActive) throw new InvalidOperationException("Manga loading indicator absent.");
                await CaptureAsync(dialog, $"inspection-manga-loading-{size}.png");
                dialog.Hide(); await showing;
                await UntilAsync(() => handler.Canceled);
                return;
            }
            await UntilAsync(() => !Field<ProgressRing>(dialog, "_progress").IsActive);
            var texts = Descendants<TextBlock>(body).Select(text => text.Text).ToList();
            if (scenario == "populated")
            {
                if (!texts.Contains("1.0 KB") || !texts.Contains("1.5 KB") || !texts.Contains("Volume 13") || !texts.Contains("Chapter 2.125"))
                    throw new InvalidOperationException("Manga file size/volume/chapter presentation differs from the current inspector.");
                if (!Descendants<Border>(body).Any(border => border.Height == 1)) throw new InvalidOperationException("Manga list has no row divider.");
                if (dialog.CloseButtonText.Length > 0) throw new InvalidOperationException("Manga inspector exposes an extra footer Close action.");
                var root = Field<Grid>(dialog, "_root");
                if (root.ActualWidth > owner.XamlRoot.Size.Width - 32 || dialog.MaxHeight > owner.XamlRoot.Size.Height * .85 + 1)
                    throw new InvalidOperationException("Manga inspector exceeds its responsive viewport.");
            }
            if (scenario == "empty" && !texts.Contains("No files found.")) throw new InvalidOperationException("Manga empty state absent.");
            if (scenario == "failed" && !texts.Contains("Couldn't load file details. Try again later.")) throw new InvalidOperationException("Manga error state absent.");
            await CaptureAsync(dialog, $"inspection-manga-{scenario}-{size}.png");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task UntilAsync(Func<bool> ready) { for (var attempt = 0; attempt < 150; attempt++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Conditional inspection fixture did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Inspection dialog was not rendered for capture.");
        var pixels = await bitmap.GetPixelsAsync(); var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private sealed class Wire : HttpMessageHandler
    {
        public string Scenario = "populated"; public bool Canceled; public TaskCompletionSource<HttpResponseMessage>? Pending;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Scenario == "delayed")
            {
                try { return await Pending!.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            }
            return new HttpResponseMessage(Scenario == "failed" ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new StringContent(Scenario == "populated" ? """{"folder_paths":["D:/Manga/Exceptionally long folder name with a very long unbroken filenamecomponentforwrappingchecks/Books"],"items":[{"content_id":"1","volume":"v13","file_name":"A long first-volume filename.cbz","file_size":1024},{"content_id":"2","chapter_index":2.125,"file_name":"A long second-volume filename.cbz","file_size":1536}]}""" : "{\"items\":[]}") };
        }
    }
}
