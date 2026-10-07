using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
using SiloPlayer.Views;
using Windows.Graphics.Imaging;

internal static class BrowseQuickInteractionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField=typeof(SiloPlayer.App).GetField("_services",BindingFlags.NonPublic|BindingFlags.Static)!;
        var previous=(IServiceProvider)serviceField.GetValue(null)!;
        using var wire=new Wire(await PortraitBytes()); using var http=new HttpClient(wire);
        var client=new SiloApiClient(http);client.SetBaseUrl("https://quick-interaction.invalid");
        using var auth=new AuthService(client,new AuthApi(client));auth.SetCurrentUser(new(){Id="fixture",Role="user"});
        var catalog=new CatalogApi(client);var settings=new SettingsApi(client);
        using var images=new ImageService(Path.Combine(Program.ResultDirectory,"quick-images"));
        using var player=new PlayerService(new PlaybackApi(client),catalog,auth,client,new SettingsService(Path.Combine(Program.ResultDirectory,"quick-settings")),settings);
        // This test stops at the real playback preparation request; native local
        // transport is verified separately. A dormant mpv avoids creating a video window.
        var dormantMpv=Activator.CreateInstance(typeof(PlayerService).Assembly.GetType("SiloPlayer.Player.MpvPlayer",true)!)!;
        var mpvField=typeof(PlayerService).GetField("_mpv",BindingFlags.NonPublic|BindingFlags.Instance)!;mpvField.SetValue(player,dormantMpv);
        var frame=new Frame();var routes=new List<(Type Type,object? Parameter)>();
        var navigation=new NavigationService{Frame=frame,NavigationRequestHandler=(type,value)=>{routes.Add((type,value));return true;}};
        serviceField.SetValue(null,new Services(previous,new()
        {
            [typeof(SiloApiClient)]=client,[typeof(AuthService)]=auth,[typeof(CatalogApi)]=catalog,
            [typeof(SettingsApi)]=settings,[typeof(RequestsApi)]=new RequestsApi(client),[typeof(PeopleApi)]=new PeopleApi(client),
            [typeof(PlayerService)]=player,[typeof(NavigationService)]=navigation,[typeof(ImageService)]=images,[typeof(HttpClient)]=http,
            [typeof(ThemeMusicService)]=null,
        }));
        var owner=new Grid();owner.Children.Add(frame);var window=new Window{Content=owner};GlobalSearchDialog? active=null;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000,-20000));window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(900,900));window.AppWindow.Show(false);await Task.Delay(100);
            async Task<(GlobalSearchDialog Dialog,Task Closed)> Open(string query)
            {
                var dialog=new GlobalSearchDialog{XamlRoot=owner.XamlRoot};active=dialog;
                var closed=dialog.ShowAsync().AsTask();await Until(()=>((TextBox)dialog.FindName("SearchBox")).IsLoaded);
                var priorReads=wire.CatalogReads; ((TextBox)dialog.FindName("SearchBox")).Text=query;
                await Until(()=>wire.CatalogReads>priorReads); await Task.Delay(150);
                Program.Log($"Quick opened query={query}, catalogReads={wire.CatalogReads}, loading={((FrameworkElement)dialog.FindName("LoadingPanel")).Visibility}, actualButtons={string.Join(" | ",Descendants<Button>(dialog).Select(AutomationProperties.GetName))}.");
                if(query.Length>1)await Until(()=>Descendants<Button>(dialog).Any(button=>AutomationProperties.GetName(button).StartsWith("Open Alpha Feature")));
                return(dialog,closed);
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_QUICK_POINTER") == "1")
            {
                var (pointerDialog, pointerClosed) = await Open("Alpha");
                var input = (TextBox)pointerDialog.FindName("SearchBox");
                input.Focus(FocusState.Programmatic);
                var personRow = Descendants<Button>(pointerDialog).Single(button => AutomationProperties.GetName(button) == "Open Portrait Person, Person");
                // WM_MOUSEMOVE alone does not synthesize WinUI pointer events
                // for an offscreen island. Exercise its palette callback, then
                // deliver actual owned Enter; do not move the user's pointer.
                var pointerCallback = typeof(GlobalSearchDialog).GetMethod("SelectResultByPointer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Quick Search has no pointer-selection callback.");
                pointerCallback.Invoke(pointerDialog, new object[] { "person:portrait-person" });
                await Task.Delay(60);
                var pointerSelection = (SearchSelectionState)typeof(GlobalSearchDialog).GetField("_selection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pointerDialog)!;
                Program.Log($"Quick native pointer callback: selected={pointerSelection.Index}, inputFocus={input.FocusState}.");
                if (pointerSelection.Index != 1 || input.FocusState == FocusState.Unfocused) throw new InvalidOperationException("Quick pointer movement must select the person while retaining input focus.");
                SendKey(0x0D); await pointerClosed;
                if (routes.Count != 1 || routes[0].Type != typeof(PersonDetailPage) || routes[0].Parameter as string != "portrait-person") throw new InvalidOperationException("Enter after pointer selection did not pick that person.");
                Program.Log("PASS: native palette pointer callback preserves input focus and owned Enter picks the highlighted person; physical pointer event delivery is not synthesized.");
                return;
            }
            auth.SelectProfile("transition-profile");
            var (transitionDialog, transitionClosed) = await Open("Alpha");
            var transitionReads = wire.CatalogReads;
            ((TextBox)transitionDialog.FindName("SearchBox")).Text = "Pending";
            await Until(() => wire.CatalogReads > transitionReads);
            if (Descendants<Button>(transitionDialog).Any(button => AutomationProperties.GetName(button).Contains("Alpha Feature")))
                throw new InvalidOperationException("A new pending query retained the previous result rows and their Play action.");
            transitionDialog.Hide(); await transitionClosed;
            auth.SelectProfile("");
            wire.RequestSearches = wire.RequestStatuses = 0;
            var (debounceDialog, debounceClosed) = await Open("Alpha");
            var debouncePlay = Descendants<Button>(debounceDialog).Single(button => AutomationProperties.GetName(button) == "Play Alpha Feature");
            ((TextBox)debounceDialog.FindName("SearchBox")).Text = "Pending";
            ((IInvokeProvider)new ButtonAutomationPeer(debouncePlay).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(80);
            if (debounceClosed.IsCompleted || wire.WatchReads != 0)
                throw new InvalidOperationException("A retired Play action launched the previous item during the next query's debounce.");
            debounceDialog.Hide(); await debounceClosed;
            Program.Log("PASS: populated-to-pending Quick Search removes retired rows; stale Play cannot launch during debounce.");
            var (shortDialog,shortClosed)=await Open("A");await Task.Delay(450);
            if(wire.RequestSearches!=0||wire.RequestStatuses!=0)throw new InvalidOperationException("No-profile/one-character Quick Search must not discover request suggestions.");
            shortDialog.Hide();await shortClosed;
            var (noProfile,noProfileClosed)=await Open("Alpha");await Task.Delay(450);
            if(wire.RequestSearches!=0||wire.RequestStatuses!=0)throw new InvalidOperationException("No-profile Quick Search must not load request capability or discovery for a long query.");
            var detail=Descendants<Button>(noProfile).Single(button=>AutomationProperties.GetName(button)=="Open Alpha Feature, 2025, Movie");
            ((IInvokeProvider)new ButtonAutomationPeer(detail).GetPattern(PatternInterface.Invoke)).Invoke();await noProfileClosed;
            if(routes.Count!=1||(routes[0].Type!=typeof(ItemDetailPage)||routes[0].Parameter as string!="alpha-movie")||wire.WatchReads!=0)throw new InvalidOperationException("Actual neighboring Quick Search row must dismiss into item detail without playback preparation.");
            var (keyboard,keyboardClosed)=await Open("Alpha");var search=(TextBox)keyboard.FindName("SearchBox");search.Focus(FocusState.Programmatic);
            var actualKeys=new List<string>(); search.KeyDown+=(_,args)=>actualKeys.Add("ordinary:"+args.Key+":"+args.Handled); search.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_,args)=>actualKeys.Add("handledToo:"+args.Key+":"+args.Handled)), true); var selection=(SearchSelectionState)typeof(GlobalSearchDialog).GetField("_selection",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(keyboard)!; Program.Log($"Quick keyboard before: focused={search.FocusState}, selected={selection.Index}, rendered={typeof(GlobalSearchDialog).GetField("_renderedQuery",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(keyboard)}, text={search.Text}, routes={routes.Count}.");
            SendKey(0x26); await Task.Delay(60);
            if (selection.Index != -1) throw new InvalidOperationException("Quick Up without a selection must remain at the input.");
            SendKey(0x28); await Task.Delay(60); if (selection.Index != 0) throw new InvalidOperationException("Quick first Down did not select the first row.");
            SendKey(0x26); await Task.Delay(60); if (selection.Index != -1) throw new InvalidOperationException("Quick Up from the first row must clear selection.");
            SendKey(0x28); await Task.Delay(60); SendKey(0x28); await Task.Delay(60);
            if (selection.Index != 1) throw new InvalidOperationException("Quick second Down did not select the person row.");
            SendKey(0x28); await Task.Delay(60); if (selection.Index != 1) throw new InvalidOperationException("Quick Down past the final row must clamp instead of wrap.");
            SendKey(0x26); await Task.Delay(60);
            Program.Log($"Quick keyboard after boundaries: selected={selection.Index}, keys={string.Join(",",actualKeys)}."); SendKey(0x0D);await keyboardClosed; Program.Log($"Quick keyboard afterEnter: selected={selection.Index}, keys={string.Join(",",actualKeys)}, routes={string.Join(" | ",routes.Select(route=>route.Type.Name+":"+route.Parameter))}.");
            if(routes.Count!=2||(routes[^1].Type!=typeof(ItemDetailPage)||routes[^1].Parameter as string!="alpha-movie"))throw new InvalidOperationException("Real Quick Search Down/Enter did not dismiss into the selected item's detail.");
            var (unavailableDialog, unavailableClosed) = await Open("Unavailable");
            if (Descendants<Button>(unavailableDialog).Any(button => AutomationProperties.GetName(button) == "Play Alpha Feature"))
                throw new InvalidOperationException("Quick Search exposed Play despite the server returning no play_content_id.");
            unavailableDialog.Hide(); await unavailableClosed;
            var (playDialog,playClosed)=await Open("Alpha");var play=Descendants<Button>(playDialog).Single(button=>AutomationProperties.GetName(button)=="Play Alpha Feature");play.Focus(FocusState.Programmatic);
            ((IInvokeProvider)new ButtonAutomationPeer(play).GetPattern(PatternInterface.Invoke)).Invoke();await playClosed;
            await Until(()=>wire.WatchReads==1&&player.ContentId=="alpha-playable"&&!player.IsLoading);
            if(routes.Count!=2||wire.LastWatchId!="alpha-playable")throw new InvalidOperationException("Actual thumbnail Play must use play_content_id independently of detail content_id.");
            Program.Log("PASS: real Quick Search no-profile/short-query request gating, neighbor detail UIA invocation, owned Down/Enter destination, and sibling thumbnail actual PlayerService watch request+dismissal.");
            var (portraitDialog,portraitClosed)=await Open("Alpha");
            var person=Descendants<Button>(portraitDialog).Single(button=>AutomationProperties.GetName(button)=="Open Portrait Person, Person");
            var portrait=Descendants<Border>(person).Single(border=>border.Width==40&&border.Height==40);
            var image=(Image)portrait.Child;await Until(()=>image.Source is BitmapImage bitmap&&bitmap.PixelWidth>0&&image.ActualWidth>0);portraitDialog.UpdateLayout();await Task.Delay(100);
            var capture=new RenderTargetBitmap();await capture.RenderAsync(portrait);var pixels=(await capture.GetPixelsAsync()).ToArray();
            if(capture.PixelWidth==0||pixels.Length<4)throw new InvalidOperationException("Actual Quick portrait rendered no decoded pixels.");
            var corner=(1*capture.PixelWidth+1)*4;var center=(capture.PixelHeight/2*capture.PixelWidth+capture.PixelWidth/2)*4;
            Program.Log($"Quick portrait actual={portrait.ActualWidth:R}x{portrait.ActualHeight:R}, radius={portrait.CornerRadius.TopLeft:R}, cornerBGRA={string.Join(",",pixels[corner..(corner+4)])}, centerBGRA={string.Join(",",pixels[center..(center+4)])}.");
            if(portrait.ActualWidth!=40||portrait.ActualHeight!=40||portrait.CornerRadius.TopLeft!=20||pixels[center+3]==0||pixels[corner+3]!=0)throw new InvalidOperationException("Actual loaded40px person portrait must clip its image to a circle with transparent corner pixels.");
            portraitDialog.Hide();await portraitClosed;
            Program.Log("PASS: actual locally decoded Quick Search40px person image has circular clipped corners.");
        }
        catch(Exception error){Program.Log("Quick primary failure: "+error);throw;}
        finally
        {
            active?.Hide();mpvField.SetValue(player,null);await player.CloseAsync();serviceField.SetValue(null,previous);await Task.Delay(200);(dormantMpv as IDisposable)?.Dispose();navigation.Frame=null;owner.Children.Remove(frame);window.Close();serviceField.SetValue(null,previous);
        }
    }
    private static async Task Until(Func<bool> ready){for(var n=0;n<160&&!ready();n++)await Task.Delay(25);if(!ready())throw new InvalidOperationException("Actual Quick Search interaction did not settle.");}
    private static IEnumerable<T> Descendants<T>(DependencyObject parent)where T:DependencyObject{for(var n=0;n<VisualTreeHelper.GetChildrenCount(parent);n++){var child=VisualTreeHelper.GetChild(parent,n);if(child is T match)yield return match;foreach(var match2 in Descendants<T>(child))yield return match2;}}
    private static void SendKey(nuint key){var target=GetFocus();GetWindowThreadProcessId(target,out var process);if(target==0||process!=(uint)Environment.ProcessId)throw new InvalidOperationException("Quick Search key target is not an owned focused HWND.");if(!PostMessage(target,0x0100,key,1)||!PostMessage(target,0x0101,key,unchecked((nint)0xC0000001u)))throw new InvalidOperationException("Could not deliver Quick Search key to own HWND.");}
    [DllImport("user32.dll")]private static extern nint GetFocus();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint target,out uint process);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool PostMessage(nint target,uint message,nuint wParam,nint lParam);
    private static async Task<byte[]> PortraitBytes(){using var stream=new MemoryStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());var pixels=Enumerable.Range(0,64*64).SelectMany(_=>new byte[]{40,67,246,255}).ToArray();encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,64,64,96,96,pixels);await encoder.FlushAsync();return stream.ToArray();}
    private sealed class Services(IServiceProvider fallback,Dictionary<Type,object?> values):IServiceProvider{public object? GetService(Type type)=>values.TryGetValue(type,out var value)?value:fallback.GetService(type);}
    private sealed class Wire(byte[] portrait):HttpMessageHandler
    {
        internal int RequestSearches,RequestStatuses,WatchReads,CatalogReads;internal string? LastWatchId;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri?.Host!="quick-interaction.invalid")throw new InvalidOperationException("Quick Search fixture attempted external networking.");
            var path=request.RequestUri.AbsolutePath;Program.Log("Quick actual HTTP: "+request.RequestUri.PathAndQuery);object body=new{items=Array.Empty<object>()};var status=HttpStatusCode.OK;
            if (request.RequestUri.Query.Contains("q=Pending") && (path == "/api/v2/catalog" || path == "/api/v2/catalog/people" || path.Contains("/requests/search")))
            {
                if (path == "/api/v2/catalog") CatalogReads++;
                return HoldAsync(ct);
            }
            if(path=="/portrait.png")return Task.FromResult(new HttpResponseMessage(status){Content=new ByteArrayContent(portrait)});
            if(path=="/api/v2/catalog/search/capabilities")body=new{people_media_scope=true};
            else if(path=="/api/v2/catalog"){CatalogReads++;body=new{items=new[]{new{content_id="alpha-movie",play_content_id=request.RequestUri.Query.Contains("q=Unavailable")?null:"alpha-playable",title="Alpha Feature",type="movie",year=2025}},page=new{has_more=false}};}
            else if(path=="/api/v2/catalog/people")body=new{items=new[]{new{id="portrait-person",name="Portrait Person",photo_url="https://quick-interaction.invalid/portrait.png"}}};
            else if(path=="/api/v2/requests/status"){RequestStatuses++;body=new{requests_enabled=true};}
            else if(path.Contains("/requests/search")){RequestSearches++;body=new{results=Array.Empty<object>()};}
            else if(path.StartsWith("/api/v2/watch/")){WatchReads++;LastWatchId=path.Split('/').Last();status=HttpStatusCode.ServiceUnavailable;body=new{message="bounded playback preparation failure"};}
            else if(path.StartsWith("/api/v2/settings/"))body=new{key="search.media_scope",value="video",items=Array.Empty<object>()};
            else if(path!="/api/v2/catalog/search/capabilities")throw new InvalidOperationException("Unexpected Quick Search fixture route: "+path);
            return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(JsonSerializer.Serialize(body))});
        }
        private static async Task<HttpResponseMessage> HoldAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Pending fixture unexpectedly completed.");
        }
    }
}
