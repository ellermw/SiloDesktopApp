using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class MediaLatestNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://media-latest-fixture.invalid"); client.SetProfile("latest-profile");
        using var images = new ImageService(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "latest-title-cache"));
        field.SetValue(null, new LocalServices(original, client, http, images));
        var differences = new List<string>();
        try
        {
            var owner = new Grid(); var window = new Window { Content = owner };
            try
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 720)); window.AppWindow.Show(false); await Task.Delay(100);
                var scale = owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(720 * scale)));
                var page = new ItemDetailPage { Width = 1280, Height = 720 }; owner.Children.Add(page); await Task.Delay(100);
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "similar-layout")
                {
                    page.ViewModel.Item = new() { ContentId = "similar-source", Type = "movie", Title = "Recommendation source" };
                    page.GetType().GetField("_currentContentId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, "similar-source");
                    await (Task)Call(page, "LoadSimilarItemsAsync")!;
                    var panel = page.FindName("SimilarPanel") as StackPanel;
                    var scroll = page.FindName("SimilarScroller") as ScrollViewer;
                    if (panel == null || scroll == null) throw new InvalidOperationException("More Like This must use one horizontal carousel rather than a wrapping poster grid.");
                    if (panel.Children.Count != 12) differences.Add("Recommendation rail must retain exactly twelve cards.");
                    foreach (var width in new[] { 460d, 900d, 1280d, 2566d, 3440d, 3840d })
                    {
                        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(720 * scale)));
                        page.Width = width; Call(page, "UpdateResponsiveLayout", width); await Task.Delay(100); page.UpdateLayout();
                        var first = (FrameworkElement)panel.Children[0];
                        var last = (FrameworkElement)panel.Children[^1];
                        var firstBounds = Bounds(first, panel); var lastBounds = Bounds(last, panel);
                        Program.Log($"TRACE: similar/{width}: first={firstBounds}, last={lastBounds}, scrollable={scroll.ScrollableWidth}.");
                        if (Math.Abs(firstBounds.Y - lastBounds.Y) > 1 || first.ActualWidth < 120 || first.ActualWidth > 220 || panel.Spacing != 20)
                            differences.Add($"Recommendation rail at {width} must retain bounded cards and a single horizontal row with twenty-pixel gaps.");
                        if (Descendants(first).OfType<TextBlock>().Any(text => text.Text is "2026" or "SERIES" or "1080p"))
                            differences.Add("Recommendation card must not inherit catalog metadata or overlay badges.");
                        if (width == 460 && scroll.ScrollableWidth <= 0) differences.Add("Narrow recommendation rail must remain horizontally scrollable.");
                        await MediaParityNativeFixture.CaptureAsync(owner, $"similar-carousel-{width}.png");
                    }
                    if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
                    Program.Log("PASS: SIMILAR_CAROUSEL_COMPLETED bounded twelve-card horizontal recommendation rail at six widths.");
                    return;
                }
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "ratings-layout")
                {
                    await CheckRatingsLayoutAsync(window, owner, page, differences);
                    if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
                    Program.Log("PASS: RATINGS_LAYOUT_COMPLETED populated movie/series ratings reserve height above overview at wide/narrow widths.");
                    return;
                }
                handler.HoldSetting = true;
                page.ViewModel.Item = new MediaItemDetail { Type = "movie", ContentId = "pending", Title = "Pending title", LogoUrl = "https://media-latest-fixture.invalid/logo-pending" };
                Call(page, "UpdateUI"); page.UpdateLayout(); await Task.Delay(100);
                var pending = page.FindName("TitleArtPending") as FrameworkElement;
                if (pending?.Visibility != Visibility.Visible || pending.Height != 112 || pending.MaxWidth != 480 || handler.LogoRequests != 0 || ((TextBlock)page.FindName("TitleText")).Visibility != Visibility.Collapsed)
                    differences.Add("Latest title-art pending must hold112x480 desktop box without fetching a logo or flashing text.");
                handler.SettingRelease.TrySetResult(false); await WaitAsync(() => ((TextBlock)page.FindName("TitleText")).Visibility == Visibility.Visible);
                if (handler.SettingRequests == 0 || handler.LogoRequests != 0 || ((Image)page.FindName("HeroLogoImage")).Source != null)
                    differences.Add("Latest false title-art must render text without a logo request.");
                handler.HoldSetting = false; handler.Value = true;
                page.ViewModel.Item = new MediaItemDetail { Type = "movie", ContentId = "true-art", Title = "Logo title", LogoUrl = "https://media-latest-fixture.invalid/logo-true" };
                Call(page, "UpdateUI"); await WaitAsync(() => ((Image)page.FindName("HeroLogoImage")).Source != null);
                if (((Image)page.FindName("HeroLogoImage")).Source is not BitmapImage || ((TextBlock)page.FindName("TitleText")).Visibility != Visibility.Collapsed
                    || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName((Image)page.FindName("HeroLogoImage")) != "Logo title")
                    differences.Add("Latest true title-art must decode the actual logo and hide plain text.");
                page.UpdateLayout();
                await MediaParityNativeFixture.CaptureAsync(owner, "media-latest-decoded-logo-1280.png");
                handler.Missing = true;
                page.ViewModel.Item = new MediaItemDetail { Type = "movie", ContentId = "missing-art", Title = "Default title", LogoUrl = "https://media-latest-fixture.invalid/logo-default" };
                Call(page, "UpdateUI"); await WaitAsync(() => ((Image)page.FindName("HeroLogoImage")).Source != null);
                if (((Image)page.FindName("HeroLogoImage")).Source == null) differences.Add("Missing title-art key must resolve to contract default true.");
                handler.Missing = false; handler.Fail = true;
                page.ViewModel.Item = new MediaItemDetail { Type = "movie", ContentId = "failed-art", Title = "Failed default title", LogoUrl = "https://media-latest-fixture.invalid/logo-failed-default" };
                Call(page, "UpdateUI"); await WaitAsync(() => ((Image)page.FindName("HeroLogoImage")).Source != null);
                if (((Image)page.FindName("HeroLogoImage")).Source == null) differences.Add("Failed title-art key read must resolve to contract default true.");
                handler.Fail = false; handler.HoldSetting = true; handler.SettingRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                page.ViewModel.Item = new MediaItemDetail { Type = "movie", ContentId = "stale", Title = "Stale title", LogoUrl = "https://media-latest-fixture.invalid/logo-stale" };
                var before = handler.LogoRequests; Call(page, "UpdateUI"); await Task.Delay(75); client.SetProfile("next-profile"); handler.SettingRelease.TrySetResult(true); await Task.Delay(150);
                if (handler.LogoRequests != before || ((Image)page.FindName("HeroLogoImage")).Source != null)
                    differences.Add("Old profile title-art completion must not fetch or paint under new authority.");
                client.SetProfile("latest-profile"); handler.HoldSetting = false;
                page.ViewModel.Item = JsonSerializer.Deserialize<MediaItemDetail>(Handler.DetailJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
                Call(page, "UpdateUI"); page.UpdateLayout(); await Task.Delay(75);
                var modelRatings = typeof(MediaItemDetail).GetProperty("Ratings");
                if (modelRatings?.GetValue(page.ViewModel.Item) is not IEnumerable values || values.Cast<object>().Count() != 3)
                    differences.Add("Latest detail JSON must preserve all three ordered server ratings including unknown source.");
                var scores = (StackPanel)page.FindName("ScoresPanel");
                var labels = Descendants(scores).OfType<TextBlock>().Select(text => text.Text).ToArray();
                if (scores.Visibility != Visibility.Visible || !labels.SequenceEqual(new[] { "Letterboxd", "4.2", "8.1", "RT", "91%" }))
                    differences.Add("Latest rendered scores must preserve server order/name/display and use official TMDB logo without legacy scalar fallback.");
                var tmdb = Descendants(scores).OfType<Image>().SingleOrDefault(image => image.Source is SvgImageSource);
                if (tmdb == null || tmdb.Height != 10) differences.Add("Latest detail TMDB mark must use the official10px SVG asset.");
                if (tmdb != null)
                {
                    await Task.Delay(100); var raster = new RenderTargetBitmap(); await raster.RenderAsync(tmdb);
                    var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await raster.GetPixelsAsync());
                    if (!Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4 + 3] > 0)) differences.Add("Official TMDB asset must paint actual nonempty pixels, not only a URI reference.");
                }
                var helper = typeof(MediaItemDetail).Assembly.GetType("SiloPlayer.Core.Services.RatingPresentation")?.GetMethod("PrimaryCardRating");
                var decimalRating = helper?.Invoke(null, new object?[] { 7.35d, 9.5d });
                var fallbackRating = helper?.Invoke(null, new object?[] { double.NaN, 8.1d });
                if (decimalRating?.GetType().GetProperty("Display")?.GetValue(decimalRating) as string != "7.4"
                    || fallbackRating?.GetType().GetProperty("Source")?.GetValue(fallbackRating) as string != "tmdb"
                    || helper?.Invoke(null, new object?[] { 0d, -1d }) != null)
                    differences.Add("Primary card rating must use valid IMDb first, valid TMDB fallback, finite positive range and half-away decimal display.");
                var eyebrow = page.FindName("HeroEyebrow") as FrameworkElement;
                if (eyebrow == null || ((TextBlock)page.FindName("HeroContextText")).Text != "MOVIE" || ((FrameworkElement)page.FindName("HeroEyebrowDot")).Visibility != Visibility.Visible)
                    differences.Add("Latest plain type/studio must share uppercase eyebrow with a conditional3px dot.");
                await MediaParityNativeFixture.CaptureAsync(owner, "media-latest-ordered-ratings-1280.png");
                page.ViewModel.Item = new MediaItemDetail { Type = "movie", ContentId = "empty-ratings", Title = "No ratings", RatingImdb = 9, RatingRtCritic = 95 };
                Call(page, "UpdateUI");
                if (scores.Visibility != Visibility.Collapsed) differences.Add("Latest absent ordered ratings must hide scores even when old scalar fields are populated.");
                await MediaParityNativeFixture.CaptureAsync(owner, "media-latest-title-ratings-1280.png"); owner.Children.Remove(page);
                await CheckNaturalRailAsync(window, owner, differences);
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(720 * scale))); await Task.Delay(100);
                var room = new WatchTogetherRoomPage { Width = 1280, Height = 720 }; owner.Children.Add(room); await Task.Delay(100);
                try
                {
                    var candidate = new MediaItem { ContentId = "ordered-detail", Title = "Candidate", Type = "movie" };
                    Call(room, "ShowCandidateSpotlight", candidate); await Task.Delay(150); room.UpdateLayout();
                    var ratings = room.FindName("CandidateRatings") as SiloPlayer.Controls.WrapPanel;
                    if (ratings == null || ratings.Children.Count != 3 || ratings.HorizontalSpacing != 6 || !Descendants(ratings).OfType<TextBlock>().Any(text => text.Text == "Letterboxd" && text.FontSize == 12))
                        differences.Add("Latest real Party catalog detail must render ordered small rating pills without modifying readiness/selection state.");
                    if (handler.DetailRequests < 1) differences.Add("Party ratings must consume real isolated CatalogApi detail rather than synthetic summary scores.");
                    await MediaParityNativeFixture.CaptureAsync(owner, "media-latest-party-ratings-1280.png");
                }
                finally { room.ViewModel.Dispose(); owner.Children.Remove(room); }
            }
            finally { window.Close(); }
            if (differences.Count > 0) throw new InvalidOperationException("Latest478 media acceptance differences:\n" + string.Join("\n", differences));
            Program.Log("PASS: MEDIA_LATEST478_COMPLETED actual pending/false/true/default/stale title art, ordered/empty detail and Party ratings, desktop natural rail boundaries.");
        }
        finally { field.SetValue(null, original); }
    }
    private static async Task CheckRatingsLayoutAsync(Window window, Grid owner, ItemDetailPage page, List<string> differences)
    {
        var displays = Microsoft.UI.Windowing.DisplayArea.FindAll();
        var observedScales = new HashSet<double>();
        for (var displayIndex = 0; displayIndex < displays.Count; displayIndex++)
        {
        if (displayIndex != 0 && displayIndex != displays.Count - 1) continue;
        var display = displays[displayIndex];
        window.AppWindow.Move(new Windows.Graphics.PointInt32(display.OuterBounds.X + 100, display.OuterBounds.Y - 1000));
        await Task.Delay(150);
        observedScales.Add(owner.XamlRoot.RasterizationScale);
        Program.Log($"TRACE: display={display.OuterBounds.X},{display.OuterBounds.Y},{display.OuterBounds.Width},{display.OuterBounds.Height}; rasterization={owner.XamlRoot.RasterizationScale}.");
        foreach (var type in new[] { "movie", "series" })
        foreach (var width in new[] { 1280d, 460d })
        foreach (var multiple in new[] { false, true })
        foreach (var font in new[] { "theme", "Segoe UI" })
        {
            var scale = owner.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(720 * scale)));
            page.Width = width;
            page.ViewModel.IsSeries = type == "series";
            page.ViewModel.Item = new MediaItemDetail
            {
                ContentId = $"ratings-{type}-{width}-{multiple}", Type = type, Title = "Rating alignment",
                Overview = "A sheltered girl with telekinetic powers is bullied, leading to a horrific incident due to her mother's influence.",
                Ratings = multiple
                    ? [new() { Source = "imdb", Name = "IMDb", Display = "6.6" }, new() { Source = "tmdb", Name = "TMDB", Display = "7.5" }, new() { Source = "rt_critic", Name = "RT", Display = "91%" }, new() { Source = "letterboxd", Name = "Letterboxd", Display = "4.2" }, new() { Source = "other", Name = "Another provider", Display = "8.8" }]
                    : [new() { Source = "imdb", Name = "IMDb", Display = "6.6" }, new() { Source = "tmdb", Name = "TMDB", Display = "7.5" }]
            };
            Call(page, "UpdateUI");
            var scores = (StackPanel)page.FindName("ScoresPanel");
            if (font != "theme") foreach (var text in Descendants(scores).OfType<TextBlock>()) text.FontFamily = new FontFamily(font);
            await Task.Delay(150); page.UpdateLayout();
            if (!multiple)
            {
                var tmdb = Descendants(scores).OfType<Image>().Single(image => image.Source is SvgImageSource);
                var raster = new RenderTargetBitmap(); await raster.RenderAsync(tmdb);
                var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await raster.GetPixelsAsync());
                if (!Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4 + 3] > 128 && pixels[index * 4 + 1] > 100))
                    differences.Add($"{type}/{width}/{font}: TMDB provider mark renders black instead of its visible green/cyan gradient.");
            }
            var overview = (FrameworkElement)page.FindName("OverviewText");
            var scoreBounds = Bounds(scores, page);
            var overviewBounds = Bounds(overview, page);
            Program.Log($"TRACE: {type}/{width}/multiple={multiple}/font={font}: ratings={scoreBounds}, overview={overviewBounds}.");
            if (scoreBounds.Width > overviewBounds.Width + 1)
                differences.Add($"{type}/{width}/{multiple}/{font}: ratings overflow the available hero width.");
            foreach (var entry in Descendants(scores).OfType<StackPanel>().Where(panel => panel.Children.OfType<TextBlock>().Any()))
            {
                var entryBounds = Bounds(entry, page);
                Program.Log($"TRACE: {Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(entry)}={entryBounds}.");
                if (entryBounds.Bottom > scoreBounds.Bottom + 1 || entryBounds.Bottom > overviewBounds.Top + 1)
                    differences.Add($"{type}/{width}/{multiple}: rating {Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(entry)} extends below reserved score row into overview.");
                if (entryBounds.Left < scoreBounds.Left - 1 || entryBounds.Right > scoreBounds.Right + 1)
                    differences.Add($"{type}/{width}/{multiple}: rating extends outside the available score row width.");
                foreach (var child in entry.Children.OfType<FrameworkElement>())
                {
                    var childBounds = Bounds(child, page);
                    if (childBounds.Top < entryBounds.Top - 1 || childBounds.Bottom > entryBounds.Bottom + 1)
                        differences.Add($"{type}/{width}/{multiple}: provider/score overflows its rating entry.");
                    if (childBounds.Left < entryBounds.Left - 1 || childBounds.Right > entryBounds.Right + 1)
                        differences.Add($"{type}/{width}/{multiple}: provider/score overflows its rating entry width.");
                }
            }
            await MediaParityNativeFixture.CaptureAsync(owner, $"ratings-layout-{type}-{width}-{multiple}-{font.Replace(' ', '-')}-{scale}.png");
        }
        }
        if (!observedScales.Contains(1d) || !observedScales.Contains(1.5d))
            differences.Add("This regression requires offscreen windows on the configured100% and150% displays; both scales must actually be exercised.");
    }
    private static Windows.Foundation.Rect Bounds(FrameworkElement element, UIElement relativeTo)
        => element.TransformToVisual(relativeTo).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
    private static async Task CheckNaturalRailAsync(Window window, Grid owner, List<string> differences)
    {
        foreach (var (width, height) in new[] { (1024d,650d), (1024d,651d), (1440d,900d) })
        foreach (var single in new[] { false, true })
        {
            var scale = owner.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale))); await Task.Delay(100);
            if (Math.Abs(owner.XamlRoot.Size.Width - width) > 2 || Math.Abs(owner.XamlRoot.Size.Height - height) > 2) throw new InvalidOperationException("Latest rail requires the true requested native client bounds.");
            var page = new ItemDetailPage { Width = width, Height = height }; owner.Children.Add(page); await Task.Delay(75);
            page.ViewModel.Seasons.Clear(); page.ViewModel.Episodes.Clear();
            page.ViewModel.Item = new MediaItemDetail { Type = "series", ContentId = "rail", Title = "Rail fixture", Overview = string.Concat(Enumerable.Repeat("Long natural hero copy. ", 200)) };
            page.ViewModel.IsSeries = true;
            for (var index=1; index <= (single ? 1 : 5); index++) page.ViewModel.Seasons.Add(new Season { ContentId=$"season-{index}", SeasonNumber=index, Title=$"Season {index}" });
            if (single) for (var number = 1; number <= 3; number++) page.ViewModel.Episodes.Add(new Episode { ContentId = $"episode-{number}", EpisodeNumber = number, Title = $"Episode {number}" });
            Call(page, "UpdateUI"); Call(page, "BuildSeasonCards"); Call(page, "BuildEpisodeRows");
            ((FrameworkElement)page.FindName("SeasonsSection")).Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)page.FindName("SeasonsLoadingSkeleton")).Visibility = Visibility.Collapsed;
            ((FrameworkElement)page.FindName("SeasonsScrollViewer")).Visibility = Visibility.Visible;
            Call(page.FindName("OverviewText"), "Toggle");
            Call(page, "UpdateResponsiveLayout", width);
            page.UpdateLayout(); await Task.Delay(100);
            var viewport = Field<Grid>(page,"_tvViewport"); var navigation = Field<ScrollViewer>(page,"_tvNavigation"); var poster = (FrameworkElement)page.FindName("HeroPosterContainer");
            var natural = height >= 651;
            Program.Log($"TRACE: latest rail {width}x{height}/single={single}: declared={viewport.Height}; min={viewport.MinHeight}; actual={viewport.ActualHeight}; nav={navigation.ActualHeight}; scroll={navigation.VerticalScrollMode}; poster={poster.ActualHeight}.");
            if (natural ? !double.IsNaN(viewport.Height) || viewport.MinHeight != height || navigation.VerticalScrollMode != ScrollMode.Disabled || Math.Abs(poster.ActualHeight-330)>2
                : double.IsNaN(viewport.Height) || navigation.VerticalScrollMode != ScrollMode.Enabled)
                differences.Add($"{width}x{height}/single={single}: current series and season navigation must grow naturally above650px; short desktop remains bounded.");
            if (natural && navigation.ActualHeight < navigation.DesiredSize.Height-2) differences.Add("Latest natural season rail cannot clip its measured content.");
            var cards = single ? ((Grid)page.FindName("EpisodesPanel")).Children.Count : ((StackPanel)page.FindName("SeasonsPanel")).Children.Count;
            if (cards != (single ? 3 : 5) || navigation.ActualHeight <= 24) differences.Add("Current natural-flow acceptance must expose the real populated navigation, not hidden24px padding.");
            if (natural && viewport.ActualHeight <= height + 2) differences.Add("Expanded long copy plus populated navigation must actually grow past the tall desktop viewport.");
            await MediaParityNativeFixture.CaptureAsync(owner,$"media-latest-rail-{width}x{height}-{(single ? "single" : "multi")}.png"); owner.Children.Remove(page);
        }
    }
    private static async Task WaitAsync(Func<bool> completed) { for(var attempt=0; attempt<60 && !completed(); attempt++) await Task.Delay(25); }
    private static T Field<T>(object value,string name) => (T)value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value)!;
    private static object? Call(object value,string name,params object?[] args) => value.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(value,args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){ var child=VisualTreeHelper.GetChild(root,i); yield return child; foreach(var nested in Descendants(child))yield return nested; } }
    private sealed class LocalServices(IServiceProvider original,SiloApiClient client,HttpClient http,ImageService images):IServiceProvider
    {
        public object? GetService(Type type) => type==typeof(ItemDetailViewModel)?new ItemDetailViewModel(new CatalogApi(client),new ItemDetailPrefetchCache((id,ct)=>new CatalogApi(client).GetItemDetailAsync(id,ct))):type==typeof(SiloApiClient)?client:type==typeof(SettingsApi)?new SettingsApi(client):type==typeof(CatalogApi)?new CatalogApi(client):type==typeof(HttpClient)?http:type==typeof(ImageService)?images:type==typeof(WatchTogetherRoomViewModel)?new WatchTogetherRoomViewModel(new PlaybackApi(client),client):original.GetService(type);
    }
    private sealed class Handler:HttpMessageHandler
    {
        internal const string DetailJson="""{"content_id":"ordered-detail","type":"movie","title":"Ordered ratings","studios":["Fixture Studio"],"rating_imdb":9.9,"ratings":[{"source":"letterboxd","name":"Letterboxd","score":84,"display":"4.2"},{"source":"tmdb","name":"TMDB","score":81,"display":"8.1"},{"source":"rt_critic","name":"RT","score":91,"display":"91%"}]}""";
        internal int SettingRequests,LogoRequests,DetailRequests; internal bool HoldSetting,Value,Missing,Fail;
        internal TaskCompletionSource<bool> SettingRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
              if(path.StartsWith("/api/v2/recommendations/similar/")) return Json(JsonSerializer.Serialize(new { items=Enumerable.Range(0,15).Select(i=>new {content_id="recommendation-"+i,type="movie",title="Recommendation "+i,year=2026,poster_url="https://media-latest-fixture.invalid/logo-recommendation",play_content_id="play-"+i}) }));
              if(path.StartsWith("/api/v2/catalog/items/recommendation-")) return Json(JsonSerializer.Serialize(new {content_id=path.Split('/')[^1],type="movie",title="Recommendation",year=2026,poster_url="https://media-latest-fixture.invalid/logo-recommendation",play_content_id="play-fixture"}));
            if(path.StartsWith("/logo-")){ Interlocked.Increment(ref LogoRequests); return new(HttpStatusCode.OK){Content=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!,"audiobook-cover.png"),ct))}; }
            if(path=="/api/v2/settings/values/effective") { SettingRequests++; if(Fail) return new(HttpStatusCode.InternalServerError){ Content=new StringContent("{\"error\":\"fixture_failure\",\"message\":\"setting failed\"}",Encoding.UTF8,"application/json") }; var value=HoldSetting?await SettingRelease.Task:Value; return Json(Missing?"{\"items\":[]}":JsonSerializer.Serialize(new {items=new[]{new{key="ui.title_art",value,source="profile"}}})); }
            if(path=="/api/v2/catalog/items/ordered-detail") {DetailRequests++;return Json(DetailJson);}
            throw new InvalidOperationException("Unexpected latest media fixture request: "+path);
        }
        private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    }
}
