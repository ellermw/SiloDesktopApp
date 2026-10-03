using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BrowseWizardRecoveryNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://wizard-recovery.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("wizard-owner", profile: new() { Id = "wizard-owner", Name = "Fixture" });
        var catalog = new CatalogApi(client); var model = new SmartCollectionWizardViewModel(catalog, new CollectionsApi(client), new AuthApi(client), auth);
        var frame = new Frame(); var navigation = new NavigationService { Frame = frame };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth,
            [typeof(CatalogApi)] = catalog, [typeof(SmartCollectionWizardViewModel)] = model,
            [typeof(NavigationService)] = navigation,
            [typeof(UICustomizationService)] = new UICustomizationService(new SettingsApi(client)),
            [typeof(CardOverlayService)] = new CardOverlayService(new SettingsApi(client)),
        }));
        var owner = new Grid(); owner.Children.Add(frame); var window = new Window { Content = owner };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280,900)); window.AppWindow.Show(false); await Task.Delay(100);
            var scale = owner.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(1280*scale),(int)Math.Round(900*scale)));
            frame.Navigate(typeof(SmartCollectionWizardPage), new SmartCollectionWizardNavigationArgs(CollectionId: "retry-smart"));
            var page = (SmartCollectionWizardPage)frame.Content;
            await Until(() => !model.IsLoading && !model.IsPreviewing && model.PreviewMediaItems.Count == 100);
            // The scroll path is the actual mounted preview's pagination trigger.
            var scroll = (ScrollViewer)page.FindName("PreviewScroll"); page.UpdateLayout();
            scroll.ViewChanged += (_, args) => Program.Log($"Wizard actual ViewChanged intermediate={args.IsIntermediate}, offset={scroll.VerticalOffset:R}, extent={scroll.ExtentHeight:R}, viewport={scroll.ViewportHeight:R}, hasMore={model.PreviewHasMore}, previewing={model.IsPreviewing}.");
            await Until(() => scroll.ViewportHeight > 0 && scroll.ScrollableHeight > 0);
            // Virtualized estimates can grow as the viewport realizes cards.
            // Repeat the real end-scroll until the settled viewport reaches the end.
            for (var attempt=0;attempt<30&&!wire.FailedLaterWindow;attempt++)
            {
                page.UpdateLayout();
                Program.Log($"Wizard mounted scroll attempt{attempt}: page={page.ActualWidth:R}x{page.ActualHeight:R}, extent={scroll.ExtentHeight:R}, viewport={scroll.ViewportHeight:R}, max={scroll.ScrollableHeight:R}, offset={scroll.VerticalOffset:R}, count={model.PreviewMediaItems.Count}, hasMore={model.PreviewHasMore}.");
                scroll.ChangeView(null,scroll.ScrollableHeight,null,true); await Task.Delay(100);
            }
            await Until(() => wire.FailedLaterWindow && model.ErrorMessage != null && !model.IsPreviewing);
            var retained = model.PreviewMediaItems.ToArray();
            if (retained.Length != 100) throw new InvalidOperationException("Failed later window discarded the successful preview window.");
            var retry = Descendants<Button>(page).Single(button => button.Content?.ToString() == "Retry preview");
            await Until(() => retry.Visibility == Visibility.Visible && retry.ActualHeight > 0);
            wire.HoldRetry = true;
            ((IInvokeProvider)new ButtonAutomationPeer(retry).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.RetryStarted);
            Program.Log($"Wizard retry diagnostic: offset={wire.RetryOffset}, retained={model.PreviewMediaItems.Count}, expected first-window100.");
            if (model.PreviewMediaItems.Count != 100 || !retained.SequenceEqual(model.PreviewMediaItems))
                throw new InvalidOperationException("Actual Retry preview discarded the successful first window while retrying a failed later window.");
            if (wire.RetryOffset != 100) throw new InvalidOperationException("Actual later-window Retry must resume its failed offset rather than restart the successful first window.");
            wire.ReleaseRetry();
            await Until(() => !model.IsPreviewing && model.PreviewMediaItems.Count == 150 && model.ErrorMessage == null);
            if (!retained.SequenceEqual(model.PreviewMediaItems.Take(100)) || model.PreviewHasMore)
                throw new InvalidOperationException("Recovered preview must append remaining50 and retain original item identity within the150 item limit.");
            Program.Log("PASS: actual mounted Wizard later-window503 → rendered Retry preserves first100 objects during pending retry and appends final50.");
            // A first-window failure has no successful window to preserve.
            scroll.ChangeView(null,0,null,true); await Task.Delay(100);
            wire.HoldRetry = false; wire.FailFirstWindowOnce = true;
            await model.PreviewAsync(); await Until(() => model.ErrorMessage != null && !model.IsPreviewing);
            if (model.PreviewMediaItems.Count != 0) throw new InvalidOperationException("First-load failure should have no retained preview rows.");
            wire.ResetHold();
            ((IInvokeProvider)new ButtonAutomationPeer(retry).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.RetryStarted);
            if (wire.RetryOffset != 0 || model.PreviewMediaItems.Count != 0) throw new InvalidOperationException("Actual first-load Retry must start a fresh first window.");
            wire.ReleaseRetry(); await Until(() => !model.IsPreviewing && model.PreviewMediaItems.Count == 100 && model.ErrorMessage == null);
            // A changed draft cannot reuse the previous query's retained cursor.
            wire.HoldRetry = false; wire.FailedLaterWindow = false;
            await model.LoadMorePreviewAsync(); await Until(() => model.ErrorMessage != null && !model.IsPreviewing);
            model.MediaScope = "series"; wire.ExpectedScope = "series"; wire.ResetHold();
            ((IInvokeProvider)new ButtonAutomationPeer(retry).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.RetryStarted);
            if (wire.RetryOffset != 0 || model.PreviewMediaItems.Count != 0) throw new InvalidOperationException("Edited-query Retry must discard the old query's first window/cursor and use current scope.");
            wire.ReleaseRetry(); await Until(() => !model.IsPreviewing && model.PreviewMediaItems.Count == 100 && model.ErrorMessage == null);
            Program.Log("PASS: actual first-load Retry starts at0; changing the failed draft's scope makes actual Retry reset rather than append the old query.");
        }
        finally
        {
            wire.ReleaseRetry(); frame.Navigate(typeof(Page)); field.SetValue(null, previous); await Task.Delay(250); navigation.Frame = null;
            owner.Children.Remove(frame); window.Close(); field.SetValue(null, previous);
        }
    }
    private static async Task Until(Func<bool> ready) { for (var n=0;n<160&&!ready();n++) await Task.Delay(25); if(!ready()) throw new InvalidOperationException("Actual Wizard preview recovery did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject { for(var n=0;n<VisualTreeHelper.GetChildrenCount(parent);n++){var child=VisualTreeHelper.GetChild(parent,n);if(child is T match)yield return match;foreach(var match2 in Descendants<T>(child))yield return match2;} }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type,object> values):IServiceProvider { public object? GetService(Type type)=>values.TryGetValue(type,out var value)?value:fallback.GetService(type); }
    private sealed class Wire:HttpMessageHandler
    {
        internal bool FailedLaterWindow, HoldRetry, RetryStarted, FailFirstWindowOnce; internal int RetryOffset=-1; internal string ExpectedScope="movie";
        private TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ResetHold(){release=new(TaskCreationOptions.RunContinuationsAsynchronously);HoldRetry=true;RetryStarted=false;RetryOffset=-1;}
        internal void ReleaseRetry()=>release.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri?.Host!="wizard-recovery.invalid")throw new InvalidOperationException("Wizard fixture attempted external networking.");
            var path=request.RequestUri.AbsolutePath; var status=HttpStatusCode.OK; string body;
            switch(path)
            {
                case "/api/v2/user/libraries": body="{\"items\":[{\"id\":1,\"name\":\"Movies\",\"type\":\"movie\"}]}"; break;
                case "/api/v2/profiles":body="{\"profiles\":[{\"id\":\"wizard-owner\",\"name\":\"Fixture\"}]}";break;
                case "/api/v2/collections/retry-smart":body="{\"id\":\"retry-smart\",\"creator_profile_id\":\"wizard-owner\",\"name\":\"Retry smart collection\",\"collection_type\":\"smart\",\"query_definition\":{\"media_scope\":\"movie\",\"match\":\"all\",\"groups\":[{\"match\":\"all\",\"rules\":[{\"field\":\"genre\",\"op\":\"is\",\"value\":\"Drama\"}]}],\"sort\":{\"field\":\"title\",\"order\":\"asc\"},\"limit\":150}}";break;
                case "/api/v2/catalog/filters":body="{\"genres\":[\"Drama\"]}";break;
                case "/api/v2/catalog":
                    var parts=request.RequestUri.Query.TrimStart('?').Split('&').Select(part=>part.Split('=',2)).ToDictionary(part=>part[0],part=>part.Length>1?part[1]:"");
                    if(!parts.TryGetValue("seek",out var value)||!int.TryParse(value,out var offset))throw new InvalidOperationException("Actual Wizard must use the native v2 raw seek contract.");
                    if(parts["source"]!="query"||parts["type"]!=ExpectedScope||parts["query_limit"]!="150"||int.Parse(parts["limit"])!=Math.Min(100,150-offset))throw new InvalidOperationException("Actual Wizard request lost query/scope/limit contract.");
                    if(offset>0&&(!parts.TryGetValue("cursor",out var cursor)||Uri.UnescapeDataString(cursor)!="wizard-snapshot"))throw new InvalidOperationException("Later Wizard window lost its server window_cursor.");
                    Program.Log("Wizard actual catalog request: "+request.RequestUri.Query);
                    if(offset==0&&FailFirstWindowOnce){FailFirstWindowOnce=false;status=HttpStatusCode.ServiceUnavailable;body="{\"message\":\"first preview window failure\"}";break;}
                    if(offset==100&&!FailedLaterWindow){FailedLaterWindow=true;status=HttpStatusCode.ServiceUnavailable;body="{\"message\":\"isolated later preview window failure\"}";break;}
                    if(HoldRetry){RetryStarted=true;RetryOffset=offset;await release.Task.WaitAsync(ct);}
                    body=JsonSerializer.Serialize(new{items=Enumerable.Range(offset,Math.Min(100,150-offset)).Select(n=>new{content_id=$"preview-{n}",type="movie",title=$"Preview {n}"}),total=150,window_cursor="wizard-snapshot",page=new{has_more=offset==0}});break;
                default:
                    if(path.StartsWith("/api/v2/settings/")){body="{\"items\":[],\"key\":\"ui.card_presentation\",\"value\":\"{}\"}";break;}
                    throw new InvalidOperationException("Unexpected Wizard fixture route: "+path);
            }
            return new(status){Content=new StringContent(body)};
        }
    }
}
