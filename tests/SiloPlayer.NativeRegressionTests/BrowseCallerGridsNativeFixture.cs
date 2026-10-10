using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BrowseCallerGridsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField=typeof(SiloPlayer.App).GetField("_services",BindingFlags.NonPublic|BindingFlags.Static)!;var previous=(IServiceProvider)serviceField.GetValue(null)!;
        using var wire=new Wire();using var http=new HttpClient(wire);var client=new SiloApiClient(http);client.SetBaseUrl("https://browse-grids.invalid");
        using var auth=new AuthService(client,new AuthApi(client));auth.SetCurrentUser(new(){Id="fixture",Role="user"});auth.SelectProfile("grid-profile",profile:new(){Id="grid-profile",Name="Fixture"});
        var catalog=new CatalogApi(client);var settings=new SettingsApi(client);var presentation=new UICustomizationService(settings);var overlays=new CardOverlayService(settings);var navigation=new NavigationService();
        using var events=new EventChannelClient(client,auth);using var images=new ImageService(Path.Combine(Program.ResultDirectory,"grid-images"));
        serviceField.SetValue(null,new Services(previous,new()
        {
            [typeof(SiloApiClient)]=()=>client,[typeof(AuthService)]=()=>auth,[typeof(CatalogApi)]=()=>catalog,[typeof(SettingsApi)]=()=>settings,
            [typeof(CollectionsApi)]=()=>new CollectionsApi(client),[typeof(NavigationService)]=()=>navigation,[typeof(SettingsService)]=()=>new SettingsService(Path.Combine(Program.ResultDirectory,"grid-settings")),
            [typeof(UICustomizationService)]=()=>presentation,[typeof(CardOverlayService)]=()=>overlays,[typeof(EventChannelClient)]=()=>events,
            [typeof(ImageService)]=()=>images,[typeof(HttpClient)]=()=>http,[typeof(LibraryViewModel)]=()=>new LibraryViewModel(catalog),
            [typeof(SearchViewModel)]=()=>new SearchViewModel(catalog,new PeopleApi(client),new RequestsApi(client),settings),
            [typeof(RecommendationSectionViewModel)]=()=>new RecommendationSectionViewModel(new RecommendationsApi(client)),
        }));
        var owner=new Grid();var window=new Window{Content=owner};Frame? frame=null;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000,-20000));window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(900,900));window.AppWindow.Show(false);await Task.Delay(100);
            foreach(var viewportWidth in new[]{1400,900,500})
            foreach(var size in new[]{"standard","compact","large"})
            foreach(var kind in viewportWidth==900?new[]{"Catalog","Library","Search","Recommendation"}:new[]{"Catalog"})
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(viewportWidth,900));await Task.Delay(80);
                await presentation.SaveCardPresentationAsync(new(){PosterSize=size,Caption="title_metadata"});
                frame=new Frame();owner.Children.Add(frame);navigation.Frame=frame;
                Page page;
                if(kind=="Catalog"){frame.Navigate(typeof(CatalogPage),new CatalogNavigation("favorites","Favorites"));page=(Page)frame.Content;await Until(()=>((ItemsRepeater)page.FindName("ItemsRepeater")).ItemsSource is System.Collections.ICollection items&&items.Count==30);}
                else if(kind=="Library"){frame.Navigate(typeof(LibraryPage),new Library{Id=22,Name="Movies",Type="movie"});page=(Page)frame.Content;await Until(()=>((LibraryPage)page).ViewModel.Items.Count==30&&!((LibraryPage)page).ViewModel.IsLoading);}
                else if(kind=="Search"){frame.Navigate(typeof(SearchPage),new SearchNavigation("Grid query","video"));page=(Page)frame.Content;await Until(()=>((SearchPage)page).ViewModel.Results.Count==30&&!((SearchPage)page).ViewModel.IsLoading);}
                else{frame.Navigate(typeof(RecommendationSectionPage),new RecommendationSectionNavigationArgs("popular",null,"Grid recommendations"));page=(Page)frame.Content;await Until(()=>((RecommendationSectionPage)page).ViewModel.Items.Count==30&&!((RecommendationSectionPage)page).ViewModel.IsLoading);}
                FrameworkElement[] Cards()=>kind=="Library"?Descendants<LibraryGridCard>(page).Where(card=>card.Visibility==Visibility.Visible&&card.ActualWidth>0&&card.ActualHeight>0).Cast<FrameworkElement>().ToArray():Descendants<PosterCard>(page).Where(card=>card.Visibility==Visibility.Visible&&card.ActualWidth>0&&card.ActualHeight>0).Cast<FrameworkElement>().ToArray();
                await Until(()=>Cards().Length>=6);page.UpdateLayout();await Task.Delay(120);
                var positions=Cards().Select(card=>(Card:card,Point:card.TransformToVisual(page).TransformPoint(new()))).OrderBy(value=>value.Point.Y).ThenBy(value=>value.Point.X).ToArray();
                var first=positions[0];var adjacent=positions.FirstOrDefault(value=>Math.Abs(value.Point.Y-first.Point.Y)<.5&&value.Point.X>first.Point.X+.5);
                if(kind=="Library")
                {
                    var row=positions.Where(value=>Math.Abs(value.Point.Y-first.Point.Y)<.5).ToArray();
                    if(row.Length!=presentation.GetPosterColumnCount(viewportWidth)) throw new InvalidOperationException($"Library/{size}/{viewportWidth} uses content width rather than the full viewport for its grid breakpoint: {row.Length} columns.");
                    if(Math.Abs(first.Point.Y-177)>.6) throw new InvalidOperationException($"Library/{size}/{viewportWidth} begins its cards at{first.Point.Y}, expected current header+toolbar rhythm177.");
                }
                var below=positions.FirstOrDefault(value=>value.Point.Y>first.Point.Y+first.Card.ActualHeight*.5&&Math.Abs(value.Point.X-first.Point.X)<.5);
                if(adjacent.Card==null||below.Card==null)throw new InvalidOperationException($"Mounted{kind}/{size} did not realize two actual rows/columns for its bounded gap proof.");
                if(kind=="Recommendation")
                {
                    var grid=(ItemsRepeater)page.FindName("ItemsGrid"); var layout=(UniformGridLayout)page.FindName("ItemsLayout");
                    Program.Log($"Recommendation actual grid: page={page.ActualWidth:R}, grid={grid.ActualWidth:R}, minItem={layout.MinItemWidth:R}, maxColumns={layout.MaximumRowsOrColumns}, gap={layout.MinColumnSpacing:R}, stretch={layout.ItemsStretch}, firstX={first.Point.X:R}, adjacentX={adjacent.Point.X:R}.");
                }
                if(kind=="Catalog")
                {
                    var grid=(ItemsRepeater)page.FindName("ItemsRepeater");var layout=(UniformGridLayout)page.FindName("CatalogGridLayout");
                    var row=positions.Where(value=>Math.Abs(value.Point.Y-first.Point.Y)<.5).ToArray();
                    Program.Log($"Catalog actual grid/{viewportWidth}/{size}: page={page.ActualWidth:R}, grid={grid.ActualWidth:R}, minItem={layout.MinItemWidth:R}, maxColumns={layout.MaximumRowsOrColumns}, firstX={first.Point.X:R}, adjacentX={adjacent.Point.X:R}, actualColumns={row.Length}.");
                    var expectedColumns=presentation.GetPosterColumnCount(viewportWidth);
                    if(row.Length!=expectedColumns)throw new InvalidOperationException($"Mounted Catalog/{size}/{viewportWidth} rendered {row.Length} columns, expected {expectedColumns}.");
                }
                var horizontal=adjacent.Point.X-first.Point.X-first.Card.ActualWidth;var vertical=below.Point.Y-first.Point.Y-first.Card.ActualHeight;var expected=size=="large"?16d:12d;
                Program.Log($"Mounted{kind}/{size}/{viewportWidth}: card={first.Card.ActualWidth:R}x{first.Card.ActualHeight:R}, horizontalGap={horizontal:R}, verticalGap={vertical:R}.");
                if(Math.Abs(horizontal-expected)>.6||Math.Abs(vertical-expected)>.6)throw new InvalidOperationException($"Mounted{kind}/{size} must render its actual source gap{expected} in both axes.");
                if(kind=="Library"&&size=="standard")
                {
                    var factory=typeof(LibraryPage).GetMethod("CreateCollectionCard",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    var fallback=(FrameworkElement)factory.Invoke(page,[new LibraryTabCollectionDisplay{Id="missing-art",Title="A long user collection title that should wrap across the missing artwork",IsUserCollection=true,ItemCount=3},160d])!;
                    var overlay=new Grid{HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};overlay.Children.Add(fallback);owner.Children.Add(overlay);page.UpdateLayout();await Task.Delay(100);
                    var label=Descendants<TextBlock>(fallback).Single(text=>text.Text.StartsWith("A long user collection")&&text.TextWrapping==TextWrapping.Wrap);
                    var caption=Descendants<TextBlock>(fallback).Single(text=>text.Text=="User collection");
                    if(label.ActualHeight<=label.FontSize*1.5||caption.FontSize!=12||!Descendants<FontIcon>(fallback).Any(icon=>icon.Glyph=="\uE77B"))throw new InvalidOperationException("Mounted missing-art collection must show wrapped fallback title, user glyph and12px user caption.");
                    owner.Children.Remove(overlay);Program.Log("PASS: actual Library collection renderer's missing artwork wraps its title and shows user glyph/12px caption.");
                    var libraryPage = (LibraryPage)page;
                    var collectionSection = new LibraryTabSection { Title = "My collections", Kind = "personal" };
                    foreach (var n in Enumerable.Range(0, 10)) collectionSection.Collections.Add(new() { Id = "collection-" + n, Title = "Collection " + n, ItemCount = 3 });
                    libraryPage.ViewModel.CollectionSections.Add(collectionSection);
                    var collectionScroll = (ScrollViewer)page.FindName("CollectionsPanel");
                    collectionScroll.Visibility = Visibility.Visible;
                    foreach (var collectionViewport in new[] { 2566, 900 })
                    {
                        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(collectionViewport, 900));
                        await Task.Delay(80);
                        typeof(LibraryPage).GetMethod("BuildCollectionCards", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                        page.UpdateLayout(); await Task.Delay(80);
                        var body = (FrameworkElement)collectionScroll.Content;
                        var expectedBodyWidth = Math.Min(1320, collectionViewport - (collectionViewport >= 1024 ? 80 : 48));
                        if (Math.Abs(body.ActualWidth - expectedBodyWidth) > .6)
                            throw new InvalidOperationException($"Library collections body at{collectionViewport} must fill its centered{expectedBodyWidth}px shell, actual{body.ActualWidth}.");
                        var host = (StackPanel)page.FindName("CollectionSectionsHost");
                        var wrap = (SiloPlayer.Controls.WrapPanel)((StackPanel)host.Children[0]).Children[^1];
                        var collectionRow = wrap.Children.Cast<FrameworkElement>().Where(card => card.TransformToVisual(wrap).TransformPoint(new()).Y < 1).ToArray();
                        if (collectionRow.Length != presentation.GetPosterColumnCount(collectionViewport))
                            throw new InvalidOperationException("Library collection columns must use the full viewport breakpoint.");
                    }
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(viewportWidth, 900));
                }
                frame.Navigate(typeof(Page));await Task.Delay(150);owner.Children.Remove(frame);frame=null;
            }
            Program.Log("PASS: actual mounted Library/Search/recommendation compact/standard/large12/12/16 grid gaps and missing-art user collection fallback.");
        }
        finally{frame?.Navigate(typeof(Page));serviceField.SetValue(null,previous);await Task.Delay(250);navigation.Frame=null;owner.Children.Clear();window.Close();}
    }
    private static async Task Until(Func<bool> ready){for(var n=0;n<160&&!ready();n++)await Task.Delay(25);if(!ready())throw new InvalidOperationException("Actual mounted caller grid did not settle.");}
    private static IEnumerable<T> Descendants<T>(DependencyObject parent)where T:DependencyObject{for(var n=0;n<VisualTreeHelper.GetChildrenCount(parent);n++){var child=VisualTreeHelper.GetChild(parent,n);if(child is T match)yield return match;foreach(var match2 in Descendants<T>(child))yield return match2;}}
    private sealed class Services(IServiceProvider fallback,Dictionary<Type,Func<object>> values):IServiceProvider{public object? GetService(Type type)=>values.TryGetValue(type,out var factory)?factory():fallback.GetService(type);}
    private sealed class Wire:HttpMessageHandler
    {
        private object[] Items()=>Enumerable.Range(0,30).Select(n=>(object)new{content_id="grid-"+n,title="Grid feature "+n,type="movie",year=2025}).ToArray();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri?.Host!="browse-grids.invalid")throw new InvalidOperationException("Grid fixture attempted external networking.");var path=request.RequestUri.AbsolutePath;object body;
            if(request.Method==HttpMethod.Put&&path.StartsWith("/api/v2/settings/values/")){using var payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));body=new{key="ui.card_presentation",value=payload.RootElement.GetProperty("value").Clone(),source="profile_client"};}
            else if(path.EndsWith("/settings/values/effective"))body=new{items=new object[]{new{key="ui.remember_library_page_state",value=(object)true},new{key="ui.library_page_state",value=(object)new{version=1,libraries=new Dictionary<string,object>{["22"]=new{search="?tab=library"}}}}}};
            else if(path.StartsWith("/api/v2/settings/"))body=new{key="search.media_scope",value="video",items=Array.Empty<object>()};
            else if(path=="/api/v2/catalog")body=new{items=Items(),total=30,window_cursor="grid-window",page=new{has_more=false}};
            else if(path.StartsWith("/api/v2/recommendations/section/"))body=new{items=Items(),label="Grid recommendations"};
            else if(path=="/api/v2/catalog/filters")body=new{genres=Array.Empty<string>()};
            else if(path=="/api/v2/requests/status")body=new{requests_enabled=false};
            else if(path=="/api/v2/catalog/search/capabilities")body=new{people_media_scope=true};
            else if(path=="/api/v2/catalog/people")body=new{items=Array.Empty<object>()};
            else throw new InvalidOperationException("Unexpected mounted grid route: "+path);
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(body))};
        }
    }
}
