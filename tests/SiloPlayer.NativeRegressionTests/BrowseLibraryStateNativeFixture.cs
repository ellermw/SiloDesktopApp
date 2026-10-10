using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Controls;

internal static class BrowseLibraryStateNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field=typeof(SiloPlayer.App).GetField("_services",BindingFlags.NonPublic|BindingFlags.Static)!;
        var previous=(IServiceProvider)field.GetValue(null)!;
        using var wire=new Wire();using var http=new HttpClient(wire);
        var client=new SiloApiClient(http);client.SetBaseUrl("https://library-native-state.invalid");
        using var auth=new AuthService(client,new AuthApi(client));auth.SetCurrentUser(new(){Id="fixture",Role="user"});
        auth.SelectProfile("alpha",profile:new(){Id="alpha",Name="Alpha"});
        var catalog=new CatalogApi(client);var settings=new SettingsApi(client);var navigation=new NavigationService();
        using var events=new EventChannelClient(client,auth);using var images=new ImageService(Path.Combine(Program.ResultDirectory,"library-state-images"));
        field.SetValue(null,new Services(previous,new()
        {
            [typeof(SiloApiClient)]=client,[typeof(AuthService)]=auth,[typeof(CatalogApi)]=catalog,[typeof(SettingsApi)]=settings,
            [typeof(NavigationService)]=navigation,[typeof(SettingsService)]=new SettingsService(Path.Combine(Program.ResultDirectory,"library-state-settings")),
            [typeof(UICustomizationService)]=new UICustomizationService(settings),[typeof(CardOverlayService)]=new CardOverlayService(settings),
            [typeof(EventChannelClient)]=events,[typeof(ImageService)]=images,[typeof(HttpClient)]=http,
        },()=>new LibraryViewModel(catalog)));
        var owner=new Grid();var window=new Window{Content=owner};Frame? frame=null;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> restoreFailure = (_, args) =>
        {
            var trace = args.Exception.StackTrace ?? "";
            if (trace.Contains("RestoreLibraryStateAsync", StringComparison.Ordinal) || trace.Contains("LibraryPageStateStore", StringComparison.Ordinal))
                Program.Log("Library restore exception: " + args.Exception);
        };
        AppDomain.CurrentDomain.FirstChanceException += restoreFailure;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000,-20000));window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280,900));window.AppWindow.Show(false);await Task.Delay(100);
            var stateBeforeMount=await new LibraryPageStateStore(settings).ReadAsync(11); Program.Log("Library preflight actual saved search: "+stateBeforeMount); if(stateBeforeMount!=wire.SavedAlpha)throw new InvalidOperationException("Library fake transport did not return the actual saved document.");
            async Task<LibraryPage> Mount()
            {
                if(frame!=null){frame.Navigate(typeof(Page));await Task.Delay(120);owner.Children.Remove(frame);}
                frame=new Frame();owner.Children.Add(frame);navigation.Frame=frame;
                var restoredSearch=await new LibraryPageStateStore(settings).ReadAsync(11);
                var parsed=typeof(LibraryPage).GetMethod("ParseLibrarySearch",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[restoredSearch])!;
                Program.Log($"Library remount preflight: search={restoredSearch}, parsedTab={parsed.GetType().GetProperty("Tab")!.GetValue(parsed)}, parsedAxis={parsed.GetType().GetProperty("Axis")!.GetValue(parsed)}.");
                frame.Navigate(typeof(LibraryPage),new Library{Id=11,Name="Audiobooks",Type="audiobook"});
                var page=(LibraryPage)frame.Content;
                Program.Log($"Library mounted settings identity={ReferenceEquals(typeof(LibraryPage).GetField("_stateSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page),settings)}, stateContext={typeof(LibraryPage).GetField("_pageStateContext",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)}.");
                for(var attempt=0;attempt<160&&!(Field<string>(page,"_currentTab")=="Library"&&!Field<bool>(page,"_isLoadingAudiobookGroups")&&page.ViewModel.Library?.Id==11);attempt++){if(attempt%40==0)Program.Log($"Library mount trace: tab={Field<string>(page,"_currentTab")}, axis={Field<string>(page,"_currentAudiobookAxis")}, modelLibrary={page.ViewModel.Library?.Id}, navigated={Field<bool>(page,"_isNavigated")}, loaded={page.IsLoaded}, groupsLoading={Field<bool>(page,"_isLoadingAudiobookGroups")}, saved={wire.SavedAlpha}.");await Task.Delay(25);}
                await Until(()=>Field<string>(page,"_currentTab")=="Library"&&!Field<bool>(page,"_isLoadingAudiobookGroups")&&page.ViewModel.Library?.Id==11);
                page.UpdateLayout();await Task.Delay(120);return page;
            }
            void TypedDraft(LibraryPage page)
            {
                if(!page.ViewModel.UseAdvancedRules||page.ViewModel.AdvancedRulesMatch!="any")throw new InvalidOperationException("Mounted Library did not restore the saved grouped query match.");
                var year=page.ViewModel.AdvancedGroups.SelectMany(group=>group.Rules).Single(rule=>rule.Field=="year");
                var value=JsonSerializer.SerializeToElement(year.Value);
                if(year.Op!="between"||value.ValueKind!=JsonValueKind.Array||value[0].GetDouble()!=1980||value[1].GetDouble()!=2001)throw new InvalidOperationException("Mounted Library restart lost a typed year range.");
            }
            var original=await Mount();await Until(()=>Descendants<TextBlock>((DependencyObject)original.FindName("AudiobookGroupsHost")).Any(text=>text.Text=="Author Alpha"));TypedDraft(original);
            if(Field<string>(original,"_currentAudiobookAxis")!="author")throw new InvalidOperationException("Mounted Library failed to restore author axis.");
            var narrator=(Button)original.FindName("AudiobookNarratorsButton");
            ((IInvokeProvider)new ButtonAutomationPeer(narrator).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(()=>wire.StateWrites>0&&Field<string>(original,"_currentAudiobookAxis")=="narrator"&&!Field<bool>(original,"_isLoadingAudiobookGroups"));
            if(!wire.SavedAlpha.Contains("type=narrators")||!wire.SavedAlpha.Contains("groups"))throw new InvalidOperationException("Actual axis selection did not persist version1 library search with its typed groups.");
            var recreated=await Mount();TypedDraft(recreated);
            await Until(()=>Descendants<TextBlock>((DependencyObject)recreated.FindName("AudiobookGroupsHost")).Any(text=>text.Text=="Narrator Alpha"));
            if(Field<string>(recreated,"_currentAudiobookAxis")!="narrator")throw new InvalidOperationException("Fresh Library page did not restore the actual persisted narrator axis.");
            wire.DeferNarrator=true;((TextBox)recreated.FindName("AudiobookGroupSearchBox")).Text="Deferred old profile";
            await Until(()=>wire.DeferredStarted);
            auth.SelectProfile("beta",profile:new(){Id="beta",Name="Beta"});var beta=await Mount();
            await Until(()=>beta.ViewModel.Items.Count>0&&!beta.ViewModel.IsLoading);
            if(Field<string>(beta,"_currentAudiobookAxis")!="books"||beta.ViewModel.UseAdvancedRules)throw new InvalidOperationException("Profile switch leaked Alpha's saved axis/query into Beta's independent Library state.");
            wire.ReleaseDeferred();await Task.Delay(200);
            if(Descendants<TextBlock>(beta).Any(text=>text.Text=="Late wrong narrator")||Descendants<TextBlock>((DependencyObject)recreated.FindName("AudiobookGroupsHost")).Any(text=>text.Text=="Late wrong narrator"))throw new InvalidOperationException("Ignored-cancellation old profile/axis response published after Library navigation.");
            wire.AllowBetaFilterState=true;
            var openFilters=(Button)beta.FindName("OpenFiltersButton");
            ((IInvokeProvider)new ButtonAutomationPeer(openFilters).GetPattern(PatternInterface.Invoke)).Invoke();
            var sheet=(SlideSheet)beta.FindName("FiltersSheet");
            await Until(()=>sheet.IsOpen);
            await Until(()=>wire.FiltersStarted);
            var loadingEditor=Field<QueryFilterEditor>(beta,"_sharedFilterEditor");
            var year=Descendants<NumberBox>(loadingEditor).First();
            year.Value=1999;
            wire.FilterGate.TrySetResult();
            await Until(()=>beta.ViewModel.Genres.Contains("Crime"));
            await Task.Delay(100);
            var genreInput=Descendants<AutoSuggestBox>(loadingEditor).Single(box=>box.Header is TextBlock label&&label.Text=="Genres");
            genreInput.Focus(FocusState.Programmatic);
            await Task.Delay(80);
            if(genreInput.ItemsSource is not System.Collections.IEnumerable available||!available.Cast<object>().Any(value=>value.ToString()=="Crime"))
                throw new InvalidOperationException("Library filter editor never received its delayed available genres.");
            if(!ReferenceEquals(loadingEditor,Field<QueryFilterEditor>(beta,"_sharedFilterEditor"))||!loadingEditor.Query.Groups.SelectMany(group=>group.Rules).Any(rule=>rule.Field=="year"&&QueryRuleValues.Format(rule.Value)=="1999"))
                throw new InvalidOperationException("Delayed Library options replaced the active editor or lost its in-flight year draft.");
            genreInput.IsSuggestionListOpen=false;
            var layoutFailures=new List<string>();
            foreach(var width in new[]{1280,900,500})
            {
                var scale=owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width*scale),(int)Math.Round(900*scale)));
                await Task.Delay(100);beta.UpdateLayout();
                var expectedWidth=width>=640?448:width*.75;
                if(Math.Abs(sheet.PreferredWidth-expectedWidth)>1)layoutFailures.Add($"{width}: Library filter drawer width{sheet.PreferredWidth}, expected{expectedWidth}.");
                var visibleCopy=Descendants<TextBlock>(sheet).Where(text=>text.Visibility==Visibility.Visible).Select(text=>text.Text).ToArray();
                if(visibleCopy.Count(text=>text=="Refine your catalog results")!=1||visibleCopy.Any(text=>text.StartsWith("Build rule groups directly",StringComparison.Ordinal)))
                    layoutFailures.Add($"{width}: Library drawer still includes obsolete/duplicate help before the current editor.");
                var body=(Grid)sheet.SheetContent;
                var scroll=(ScrollViewer)beta.FindName("AdvancedFiltersScroll");
                if(body.RowDefinitions.Count!=2||body.RowSpacing!=16||scroll.Padding.Top!=0)
                    layoutFailures.Add($"{width}: filter body must contain only the scrolling editor and fixed footer with16px gap.");
            }
            sheet.IsOpen=false;
            if(layoutFailures.Count>0)throw new InvalidOperationException(string.Join("\n",layoutFailures));
            Program.Log("PASS: Library filter sheet matches448px desktop/75% narrow width, one header and bounded editor/footer.");
            Program.Log("PASS: actual saved grouped typed Library query/author axis, native Narrators selection persists state, fresh-page restore, separate profile state and deferred ignored-cancellation old-axis rejection.");
        }
        finally{AppDomain.CurrentDomain.FirstChanceException -= restoreFailure;wire.ReleaseDeferred();frame?.Navigate(typeof(Page));field.SetValue(null,previous);await Task.Delay(250);navigation.Frame=null;owner.Children.Clear();window.Close();}
    }
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    private static async Task Until(Func<bool> ready){for(var n=0;n<160&&!ready();n++)await Task.Delay(25);if(!ready())throw new InvalidOperationException("Actual Library saved state/axis did not settle.");}
    private static IEnumerable<T> Descendants<T>(DependencyObject parent)where T:DependencyObject{for(var n=0;n<VisualTreeHelper.GetChildrenCount(parent);n++){var child=VisualTreeHelper.GetChild(parent,n);if(child is T match)yield return match;foreach(var match2 in Descendants<T>(child))yield return match2;}}
    private sealed class Services(IServiceProvider fallback,Dictionary<Type,object> values,Func<LibraryViewModel> factory):IServiceProvider{public object? GetService(Type type)=>type==typeof(LibraryViewModel)?factory():values.TryGetValue(type,out var value)?value:fallback.GetService(type);}
    private sealed class Wire:HttpMessageHandler
    {
        internal string SavedAlpha="?tab=library&type=authors&match=any&groups[0][match]=all&groups[0][rules][0][field]=year&groups[0][rules][0][op]=between&groups[0][rules][0][value][0]=1980&groups[0][rules][0][value][1]=2001";
        internal int StateWrites;internal bool DeferNarrator,DeferredStarted,AllowBetaFilterState,FiltersStarted;
        internal readonly TaskCompletionSource FilterGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource gate=new(TaskCreationOptions.RunContinuationsAsynchronously);internal void ReleaseDeferred()=>gate.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri?.Host!="library-native-state.invalid")throw new InvalidOperationException("Library fixture attempted external networking.");
            var path=request.RequestUri.AbsolutePath;var profile=request.Headers.TryGetValues("X-Profile-Id",out var headers)?headers.Single():"";
            Program.Log("Library actual HTTP: "+profile+" "+request.Method+" "+request.RequestUri.PathAndQuery);
            object body=new{items=Array.Empty<object>()};
            if(path.Contains("/settings/values/")&&request.Method==HttpMethod.Put)
            {
                var payload=JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); Program.Log("Library actual state write: "+payload.GetRawText());
                if(path.EndsWith("ui.library_page_state")){StateWrites++;if(profile!="alpha"&&!(profile=="beta"&&AllowBetaFilterState))throw new InvalidOperationException("Old Alpha state write escaped into another profile.");if(profile=="alpha")SavedAlpha=payload.GetProperty("value").GetProperty("libraries").GetProperty("11").GetProperty("search").GetString()!;}
                body=new{key="ui.library_page_state",value=new{},source="profile"};
            }
            else if(path=="/api/v2/events/ws-ticket")return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"error\":\"fixture_no_socket\",\"message\":\"Local fixture has no events socket\"}")};
            else if(path=="/api/v2/library/11/layout")body=new{sections=Array.Empty<object>()};
            else if(path.EndsWith("/settings/values/effective"))body=new{items=new object[]{new{key="ui.remember_library_page_state",value=(object)true},new{key="ui.library_page_state",value=(object)new{version=1,libraries=new Dictionary<string,object>{["11"]=new{search=profile=="alpha"?SavedAlpha:"?tab=library&type=books"}}}},new{key="ui.card_presentation",value=(object)new{poster_size="standard",caption="title_metadata"}}}};
            else if(path.StartsWith("/api/v2/settings/"))body=new{items=Array.Empty<object>()};
            else if(path=="/api/v2/catalog/audiobook-groups")
            {
                var narrator=request.RequestUri.Query.Contains("group_by=narrator");var name=narrator?"Narrator Alpha":"Author Alpha";
                if(DeferNarrator&&narrator){DeferredStarted=true;await gate.Task;name="Late wrong narrator";}
                body=new{items=new[]{new{name,item_count=1,total_duration_seconds=60,poster_urls=Array.Empty<string>()}},total=1,page=new{has_more=false}};
            }
            else if(path=="/api/v2/catalog")body=new{items=new[]{new{content_id="beta-book",type="audiobook",title="Beta independent book"}},total=1,window_cursor="library-state",page=new{has_more=false}};
            else if(path=="/api/v2/catalog/filters"){FiltersStarted=true;await FilterGate.Task;body=new{genres=new[]{"Crime","Drama"},authors=Array.Empty<string>(),narrators=Array.Empty<string>(),series=Array.Empty<string>()};}
            else if(path=="/api/v2/catalog/search/capabilities")body=new{people_media_scope=true};
            else if(path=="/api/v2/user/libraries")body=new{items=new[]{new{id=11,name="Audiobooks",type="audiobook"}}};
            else throw new InvalidOperationException("Unexpected Library fixture route: "+path);
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(body))};
        }
    }
}
