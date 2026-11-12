using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BatchDownloadsNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://batch-native.invalid"); client.SetProfile("fixture");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", DownloadAllowed = true });
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        var cache = new ItemDetailPrefetchCache(catalog, auth);
        var vm = new ItemDetailViewModel(catalog, cache);
        var toasts = new ToastService(); var notices = new SiloPlayer.Controls.ToastContainer();
        using var owned = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(catalog)
            .AddSingleton(vm).AddSingleton(new UICustomizationService(settings)).AddSingleton(toasts).BuildServiceProvider();
        servicesField.SetValue(null, new Services(owned, previous));
        var page = new ItemDetailPage { Width = 900, Height = 720 }; parent.Children.Add(page);
        using var navigation = new CancellationTokenSource();
        typeof(ItemDetailPage).GetField("_navigationCts", Private)!.SetValue(page, navigation);
        try
        {
            await Task.Delay(150);
            foreach (var type in new[] { "series", "season", "specials" })
            {
                wire.Reset();
                var content = type == "series" ? "series-1" : "season-1";
                vm.Item = new MediaItemDetail { ContentId = content, Type = type == "series" ? "series" : "season", Title = "Native fixture", SeriesId = "series-1", SeasonNumber = type == "specials" ? 0 : 1, IsSpecials = type == "specials" };
                typeof(ItemDetailPage).GetField("_currentContentId", Private)!.SetValue(page, content);
                Call(page, "BuildMoreFlyout");
                var menu = (MenuFlyout)page.FindName("MoreFlyout");
                var action = menu.Items.OfType<MenuFlyoutItem>().SingleOrDefault(i => i.Text.StartsWith("Download "));
                Check(action != null, "Official aggregate detail with no Versions must offer the series/season/Specials batch action.");
                var task = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", type == "series" ? null : (object)(type == "specials" ? 0 : 1), "Native fixture")!;
                var dialog = await Dialog(parent);
                await Until(() => Elements<ComboBox>(dialog).Any(c => Equals(c.Header, "Version template")));
                var version = Elements<ComboBox>(dialog).Single(c => Equals(c.Header, "Version template"));
                Check(version.Items.Count == 3, "Version picker must offer automatic plus both accessible episode versions.");
                version.SelectedIndex = 2;
                await MediaParityNativeFixture.CaptureAsync(dialog, "batch-download-" + type + ".png");
                var quality = Elements<ComboBox>(dialog).Single(c => Equals(c.Header, "Quality")); quality.SelectedIndex = 1;
                Invoke(Elements<Button>(dialog).Single(b => Equals(b.Content, "Start Download")));
                await task;
                Check(wire.Bodies.Count == 2, "Actual picker must create both cursor pages.");
                using var first = JsonDocument.Parse(wire.Bodies[0]); using var second = JsonDocument.Parse(wire.Bodies[1]);
                var a=first.RootElement; var b=second.RootElement;
                Check(a.GetProperty("media_file_id").GetString() == "102" && b.GetProperty("media_file_id").GetString() == "102", "Chosen episode template must stay fixed across pages.");
                Check(a.GetProperty("quality").GetString() == "5mbps" && a.GetProperty("batch_id").GetString() == b.GetProperty("batch_id").GetString(), "Quality and batch identity must stay fixed.");
                Check(!a.TryGetProperty("expected_revision", out _), "Batch must omit single-entry guards.");
                Check(type == "series" ? !a.TryGetProperty("season_number", out _) : a.GetProperty("season_number").GetInt32() == (type == "specials" ? 0 : 1), "All-series omission and Specials zero must differ.");
                Check(wire.VersionReads == 1 && wire.AggregateVersionReads == 0, "Template must come from an accessible episode, not aggregate versions.");
                Program.Log("PASS: actual empty-version " + type + " menu/picker, chosen template/quality, stable cursor scope and skip result.");
            }
            // Closing an in-flight picker stops later cursor pages even if a fake wire ignores cancellation.
            wire.Reset(); wire.CreateGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            parent.Children.Add(notices); toasts.Register(notices, parent.DispatcherQueue);
            var held = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!;
            var pending = await Dialog(parent); await Until(() => Elements<Button>(pending).Any(b => Equals(b.Content, "Start Download")));
            Invoke(Elements<Button>(pending).Single(b => Equals(b.Content, "Start Download")));
            await Until(() => wire.Bodies.Count == 1); pending.Hide(); await held;
            wire.CreateGate.TrySetResult(true); await Task.Delay(150);
            Check(wire.Bodies.Count == 1, "Closed picker must not create the next batch page.");
            Program.Log("PASS: closing native picker retires a held creation and suppresses later cursor pages.");
            // The held page may be accepted, so the viewer still learns where to reconcile it.
            await Until(() => Elements<TextBlock>(notices).Any(t => t.Text.Contains("may already be registered")));
            Check(Elements<Button>(notices).Any(b => Equals(b.Content, "View Downloads")), "An abandoned registration must keep a route to Downloads.");
            toasts.Unregister(); parent.Children.Remove(notices);
            Program.Log("PASS: a picker closed during registration reports possible registrations with a route to Downloads.");

            // Single flight begins before capability discovery and Cancel retires that discovery too.
            wire.Reset(); wire.CapabilityGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstOpen = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!;
            var loading = await Dialog(parent); await Until(() => wire.CapabilityReads == 1);
            var secondOpen = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!; await secondOpen;
            Check(wire.CapabilityReads == 1, "Duplicate activation during discovery must coalesce.");
            loading.Hide(); await firstOpen; wire.CapabilityGate.TrySetResult(true); await Task.Delay(100);
            Check(wire.Bodies.Count == 0 && wire.VersionReads == 0, "Closed discovery must not load templates or create downloads.");
            Program.Log("PASS: native duplicate activation is single flight; discovery Cancel retires late capability work.");

            foreach(var refused in new[] { "allowed", "bounded_creation" })
            {
                wire.Reset(); wire.Refused = refused;
                var denied = (Task)Call(page,"ShowBatchDownloadDialogAsync","series-1",(object)0,"Native fixture")!;
                var refusal = await Dialog(parent); await Until(() => Elements<TextBlock>(refusal).Any(t => t.Text.Contains("not available")));
                Check(!Elements<Button>(refusal).Any(b => Equals(b.Content,"Start Download")) && wire.VersionReads==0, "Effective refusal must block template and creation UI.");
                refusal.Hide(); await denied;
            }
            wire.Reset(); wire.BatchVersion=false; wire.BulkQuality=false;
            var legacy=(Task)Call(page,"ShowBatchDownloadDialogAsync","series-1",(object)0,"Native fixture")!;
            var old=await Dialog(parent); await Until(()=>Elements<Button>(old).Any(b=>Equals(b.Content,"Start Download")));
            Check(!Elements<ComboBox>(old).Any() && wire.VersionReads==0,"Legacy capability must use no-template original quality without reading versions.");
            Invoke(Elements<Button>(old).Single(b=>Equals(b.Content,"Start Download"))); await legacy;
            using(var body=JsonDocument.Parse(wire.Bodies[0])) Check(!body.RootElement.TryGetProperty("media_file_id",out _),"Legacy batch must omit template.");
            Program.Log("PASS: effective refusal and legacy batch capability gates exercised through native dialogs.");

            // Reconnect catch-up or an access refresh retires requests, not the open picker.
            wire.Reset();
            var refreshed = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!;
            var open = await Dialog(parent); await Until(() => Elements<Button>(open).Any(b => Equals(b.Content, "Start Download")));
            client.InvalidateAccessContext();
            Invoke(Elements<Button>(open).Single(b => Equals(b.Content, "Start Download"))); await refreshed;
            Check(wire.Bodies.Count == 2, "A refreshed request context must not leave Start Download inert.");

            // A request context retired between cursor pages surfaces reconciliation instead of hanging.
            wire.Reset(); wire.Created = () => client.InvalidateAccessContext();
            var partial = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!;
            var stalled = await Dialog(parent); await Until(() => Elements<Button>(stalled).Any(b => Equals(b.Content, "Start Download")));
            Invoke(Elements<Button>(stalled).Single(b => Equals(b.Content, "Start Download")));
            await Until(() => Elements<Button>(stalled).Any(b => Equals(b.Content, "View Downloads")));
            Check(wire.Bodies.Count == 1 && Elements<TextBlock>(stalled).Any(t => t.Text.Contains("may already be registered")), "A partial batch must direct the viewer to Downloads.");
            stalled.Hide(); await partial;
            Program.Log("PASS: request-context refresh keeps the native picker usable and reports a partial batch.");

            // A request context retired while the last page is in flight still completes the batch.
            wire.Reset(); wire.Created = () => { if (wire.Bodies.Count == 2) client.InvalidateAccessContext(); };
            var finished = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!;
            var last = await Dialog(parent); await Until(() => Elements<Button>(last).Any(b => Equals(b.Content, "Start Download")));
            Invoke(Elements<Button>(last).Single(b => Equals(b.Content, "Start Download")));
            await Until(() => finished.IsCompleted || Elements<Button>(last).Any(b => Equals(b.Content, "View Downloads")));
            Check(finished.IsCompleted && wire.Bodies.Count == 2, "A batch whose last page was accepted must complete, not report an interruption.");
            await finished;
            Program.Log("PASS: a request context retired during the last page still completes the native batch.");

            // Template discovery is optional: a failing catalog read leaves the batch startable without a template.
            wire.Reset(); wire.DiscoveryFails = true;
            var degraded = (Task)Call(page, "ShowBatchDownloadDialogAsync", "series-1", (object)0, "Native fixture")!;
            var plain = await Dialog(parent); await Until(() => Elements<Button>(plain).Any(b => Equals(b.Content, "Start Download")));
            Check(!Elements<ComboBox>(plain).Any(c => Equals(c.Header, "Version template")) && wire.VersionReads == 0, "Failed template discovery must offer no template.");
            Invoke(Elements<Button>(plain).Single(b => Equals(b.Content, "Start Download"))); await degraded;
            Check(wire.Bodies.Count == 2, "Failed template discovery must not block batch creation.");
            using (var body = JsonDocument.Parse(wire.Bodies[0])) Check(!body.RootElement.TryGetProperty("media_file_id", out _), "A batch without discovered templates must omit the template.");
            Program.Log("PASS: failed template discovery keeps the native picker startable without a template.");
        }
        finally { wire.CreateGate?.TrySetResult(true); wire.CapabilityGate?.TrySetResult(true); toasts.Unregister(); parent.Children.Remove(notices); navigation.Cancel(); parent.Children.Remove(page); servicesField.SetValue(null,previous); }
    }
    private static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Private)!.Invoke(target,args);
    private static void Check(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Elements<T>(DependencyObject root) where T:DependencyObject { if(root is T t) yield return t; for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Elements<T>(VisualTreeHelper.GetChild(root,i))) yield return child; }
    private static async Task Until(Func<bool> condition) { var end=DateTime.UtcNow.AddSeconds(12); while(!condition()) { if(DateTime.UtcNow>end) throw new TimeoutException("Batch native condition timed out."); await Task.Delay(20); } }
    private static async Task<ContentDialog> Dialog(StackPanel parent) { ContentDialog? dialog=null; await Until(()=> (dialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Select(p=>p.Child).OfType<ContentDialog>().FirstOrDefault()) != null); await Task.Delay(60); return dialog!; }
    private static void Invoke(Button button) { var peer=FrameworkElementAutomationPeer.CreatePeerForElement(button) as ButtonAutomationPeer; ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke(); }
    private sealed class Services(IServiceProvider own,IServiceProvider previous):IServiceProvider { public object? GetService(Type t)=>own.GetService(t)??previous.GetService(t); }
    private sealed class Wire:HttpMessageHandler
    {
        internal List<string> Bodies=[]; internal int VersionReads,AggregateVersionReads,CapabilityReads;
        internal bool BatchVersion=true,BulkQuality=true; internal string? Refused;
        internal TaskCompletionSource<bool>? CreateGate,CapabilityGate; internal Action? Created; internal bool DiscoveryFails;
        internal void Reset() { Bodies.Clear(); VersionReads=AggregateVersionReads=CapabilityReads=0; BatchVersion=BulkQuality=true; Refused=null; CreateGate=CapabilityGate=null; Created=null; DiscoveryFails=false; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path=="/api/v2/capabilities/downloads") { CapabilityReads++; if(CapabilityGate!=null) await CapabilityGate.Task; return Reply(new { enabled=true,download_allowed=true,allowed=Refused!="allowed",bounded_creation=Refused!="bounded_creation",season_download=true,batch_version=BatchVersion,bulk_quality=BulkQuality,quality_presets=new[]{"original","5mbps"} }); }
            if(DiscoveryFails && path.StartsWith("/api/v2/catalog/")) return new(HttpStatusCode.InternalServerError){Content=new StringContent("{\"title\":\"Server error\"}")};
            if(path.EndsWith("/seasons")) return Reply(new { items=new[]{new {content_id="season-1",season_number=0},new {content_id="season-2",season_number=1}} });
            // Shaped like the real server: the legacy series/number route discards season zero.
            if(path.EndsWith("/seasons/0/episodes")) return Reply(new {items=Array.Empty<object>()});
            if(path.EndsWith("/episodes")) return Reply(new {items=new[]{new {content_id="ep-1",title="First episode",files=new[]{new {file_id="101"},new {file_id="102"}}}}});
            if(path.EndsWith("/versions")) { if(!path.Contains("/ep-1/")) AggregateVersionReads++; VersionReads++; return Reply(new {items=new[]{new {file_id="101",resolution="1080p",codec_video="h264",hdr=false},new {file_id="102",resolution="2160p",codec_video="hevc",hdr=true}}}); }
            if(path=="/api/v2/downloads" && request.Method==HttpMethod.Post) { Bodies.Add(await request.Content!.ReadAsStringAsync(ct)); Created?.Invoke(); if(CreateGate!=null) await CreateGate.Task; return Reply(new {items=new[]{new {id="download-"+Bodies.Count}},skipped=Bodies.Count==2?new[]{new {episode_id="missing",reason="unavailable"}}:[],page=new {has_more=Bodies.Count==1,next_cursor=Bodies.Count==1?"next":null}}); }
            if(path=="/api/v2/downloads") return Reply(new {items=Array.Empty<object>()});
            return Reply(new {});
        }
        private static HttpResponseMessage Reply(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    }
}
