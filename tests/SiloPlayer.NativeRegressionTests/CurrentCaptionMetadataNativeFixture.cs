using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class CurrentCaptionMetadataNativeFixture
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(StackPanel parent)
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_CAPTION_METADATA_MODE") ?? "all";
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://caption-metadata.fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api), credentialStore: null);
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "admin" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "caption-metadata-local"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; local.Save(prefs);
        await using var services = new ServiceCollection()
            .AddSingleton(api).AddSingleton(http).AddSingleton(auth).AddSingleton(local)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<MediaMaintenanceApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>()
            .AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "caption-metadata-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = slot.GetValue(null); slot.SetValue(null, services);
        // Reuse the host's hidden root. Never construct a second MainWindow or
        // run its installed-account Loaded handler in this process.
        try
        {
            if (mode is "all" or "captions") await CheckCaptionsAsync(parent, wire, services.GetRequiredService<UICustomizationService>());
            if (mode is "all" or "metadata" or "reset") await CheckResetAsync(parent, wire, failUnlock: false);
            if (mode is "all" or "metadata" or "failed-unlock") await CheckResetAsync(parent, wire, failUnlock: true);
            if (mode is "all" or "metadata" or "images" or "pending-image") await CheckImagesAsync(parent, wire, mode != "pending-image");
            Program.Log("PASS: current caption/metadata native fixture completed: " + mode);
        }
        finally { wire.ReleaseImage.TrySetResult(); slot.SetValue(null, previous); }
    }

    private static async Task CheckCaptionsAsync(StackPanel parent, Wire wire, UICustomizationService customization)
    {
        var page = new SettingsPage { Width = 1100, Height = 800 };
        parent.Children.Add(page);
        try
        {
            await Until(() => page.IsLoaded, "Settings page was not mounted.");
            // Settings initially displays its overview. Invoking the hidden
            // sidebar tab toggles only the inner panel and leaves the entire
            // detail layout collapsed; enter via the actual visible overview.
            bool IsOverviewChoice(Button button) => Equals(button.Tag, "Interface") && button.Name != "InterfaceTab" && IsVisible(button);
            await Until(() => Descendants(page).OfType<Button>().Any(IsOverviewChoice), "Navigation & Cards overview choice did not appear.");
            Click(Descendants(page).OfType<Button>().Single(IsOverviewChoice));
            var panel = (FrameworkElement)page.FindName("InterfacePanel");
            await Until(() => panel.Visibility == Visibility.Visible && customization.IsSupported,
                "Actual Navigation & Cards selection did not load supported settings.");
            foreach (var retired in new[] { "BalancedCardPreset", "CompactCardPreset", "CinemaCardPreset", "ArtworkCardPreset", "InterfaceCaptionChoices", "InterfaceCaptionCombo" })
                if (page.FindName(retired) is FrameworkElement element && IsVisible(element))
                    throw new InvalidOperationException("Retired caption/preset control is still visible: " + retired);
            if (Descendants(panel).OfType<TextBlock>().Any(text => IsVisible(text) && text.Text is "Caption" or "Artwork only"))
                throw new InvalidOperationException("Navigation & Cards still displays the retired caption choice.");
            foreach (var saved in new[] { "artwork", "title" })
            {
                wire.Caption = saved; customization.Invalidate();
                await (Task)Call(page, "LoadInterfaceCustomizationAsync")!;
                var previewTitle = (TextBlock)page.FindName("NavigationPreviewCaption");
                var previewDetails = (TextBlock)page.FindName("NavigationPreviewYear");
                page.UpdateLayout();
                await Until(() => previewTitle.ActualWidth > 0 && previewTitle.ActualHeight > 0 && previewDetails.ActualWidth > 0 && previewDetails.ActualHeight > 0,
                    "Settings preview did not acquire a visible layout after actual overview navigation.");
                TraceVisibility("preview-title " + saved + " text=" + previewTitle.Text, previewTitle);
                TraceVisibility("preview-details " + saved + " text=" + previewDetails.Text, previewDetails);
                Check(IsVisible(previewTitle) && !string.IsNullOrWhiteSpace(previewTitle.Text) && IsVisible(previewDetails) && !string.IsNullOrWhiteSpace(previewDetails.Text),
                    "Saved " + saved + " hides the actual Settings title/details preview.");
                var card = new PosterCard { MediaItem = new MediaItem { ContentId = "fixture-movie", Type = "movie", Title = "Fixture Visible Title", Year = 2026 } };
                card.SetCatalogGridLayout(176); parent.Children.Add(card);
                try
                {
                    await Until(() => card.IsLoaded, "Poster card did not mount.");
                    var title = (TextBlock)card.FindName("TitleText"); var details = (TextBlock)card.FindName("SubtitleText");
                    Check(IsVisible(title) && title.Text == "Fixture Visible Title" && IsVisible(details) && details.Text.Contains("2026", StringComparison.Ordinal),
                        "Saved " + saved + " hides actual poster-card title/details.");
                }
                finally { parent.Children.Remove(card); }
            }
            Program.Log("PASS: real Navigation & Cards omits retired caption/preset controls; saved artwork/title retain Settings preview and native poster title/details.");
        }
        finally { parent.Children.Remove(page); await Task.Delay(200); }
    }

    private static async Task CheckResetAsync(StackPanel parent, Wire wire, bool failUnlock)
    {
        wire.Mutations.Clear(); wire.FailUnlock = failUnlock;
        var dialog = new EditMetadataDialog(Item()) { XamlRoot = parent.XamlRoot };
        var closed = false; dialog.Closed += (_, _) => closed = true;
        var showing = dialog.ShowAsync().AsTask();
        try
        {
            await Until(() => dialog.IsLoaded, "Metadata dialog did not open.");
            var reset = (Button)Get(dialog, "_reset")!;
            Click(reset);
            var flyout = (Flyout)reset.Flyout;
            await Until(() => ((FrameworkElement)flyout.Content).IsLoaded, "Reset confirmation did not open.");
            var confirm = Descendants((DependencyObject)flyout.Content).OfType<Button>().Single(button => Equals(button.Content, "Reset"));
            Click(confirm);
            await Until(() => wire.Mutations.Count > 0, "Reset confirmation did not issue its mutation.");
            Program.Log($"TRACE Reset failUnlock={failUnlock}: closed={closed}, resetEnabled={reset.IsEnabled}, saved={dialog.HasSaved}, mutations={string.Join(",", wire.Mutations.Select(mutation => mutation.Method + " " + mutation.Path))}");
            // The async handler reenables controls in finally immediately
            // after Hide starts; Closed arrives after the native animation.
            await Until(() => failUnlock ? reset.IsEnabled : closed, "Reset did not settle.");
            Check(wire.Mutations[0].Method == "PATCH" && wire.Mutations[0].Path.EndsWith("/metadata", StringComparison.Ordinal)
                && wire.Mutations[0].Body.TryGetProperty("locked_fields", out var locks) && locks.GetArrayLength() == 0,
                "Actual Reset did not first PATCH an empty locked_fields array.");
            if (failUnlock)
                Check(!closed && wire.Mutations.Count == 1 && !dialog.HasSaved, "Failed unlock closed the editor or still POSTed refresh.");
            else
                Check(closed && wire.Mutations.Count == 2 && wire.Mutations[1].Method == "POST"
                    && wire.Mutations[1].Path.EndsWith("/refresh-metadata", StringComparison.Ordinal), "Reset did not refresh only after successful unlock.");
            Program.Log("PASS: actual metadata Reset confirmation " + (failUnlock ? "keeps failed unlock open without refresh." : "unlocks before refresh and closes on success."));
        }
        finally { ((Button)Get(dialog, "_reset")!).Flyout?.Hide(); if (!closed) dialog.Hide(); await showing; wire.FailUnlock = false; }
    }

    private static async Task CheckImagesAsync(StackPanel parent, Wire wire, bool checkCurrent)
    {
        wire.Mutations.Clear(); wire.HoldImage = true;
        var dialog = new EditMetadataDialog(Item()) { XamlRoot = parent.XamlRoot };
        var closed = false; dialog.Closed += (_, _) => closed = true;
        var showing = dialog.ShowAsync().AsTask();
        try
        {
            await Until(() => dialog.IsLoaded, "Image editor did not open.");
            // A queued Save must have a real visible draft to submit; without
            // this edit, a missing pending guard could look like a harmless no-op.
            ((Dictionary<string, TextBox>)Get(dialog, "_textInputs")!)["title"].Text = "Fixture pending draft";
            var nav = (StackPanel)Get(dialog, "_navigation")!;
            Click(nav.Children.OfType<Button>().Single(button => Equals(button.Tag, "images")));
            await Until(() => Descendants(dialog).OfType<Button>().Any(button => Equals(button.Tag, "upload://opaque-current")), "Uploaded image choice never appeared.");
            var current = Descendants(dialog).OfType<Button>().Single(button => Equals(button.Tag, "upload://opaque-current"));
            if (checkCurrent) Check(Descendants(current).OfType<TextBlock>().Any(text => text.Text == "Current" && IsVisible(text)), "Uploaded local artwork is not visibly Current.");
            var picture = Descendants(current).OfType<Image>().Single();
            Check(picture.Source != null, "Uploaded artwork choice has no Image.Source.");
            await Until(() => wire.ImagePaths.Contains("/art/current.png"), "Current image did not use its returned normal URL.");
            // Image.Source can reuse a previously decoded cached bitmap from
            // earlier dialogs. Observe the real bitmap, rather than a helper's
            // task keyed by one particular managed ImageSource wrapper.
            await Until(() => picture.Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 }, "Returned current artwork bytes did not decode on the actual image control.");
            Program.Log($"TRACE current image source={picture.Source?.GetType().FullName}, pixels={(picture.Source is BitmapImage bitmap ? bitmap.PixelWidth + "x" + bitmap.PixelHeight : "unavailable")}, requests={string.Join(",", wire.ImagePaths)}");
            Check(wire.UploadRequests == 0, "Opaque upload identity was requested as an image URL.");
            var alternate = Descendants(dialog).OfType<Button>().Single(button => Equals(button.Tag, "https://provider.fixture/alternate"));
            Click(alternate);
            var apply = Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "Apply Poster"));
            Click(apply);
            await Until(() => wire.Mutations.Any(mutation => mutation.Path.EndsWith("/images/apply", StringComparison.Ordinal)), "Actual Apply did not start image mutation.");
            var reset = (Button)Get(dialog, "_reset")!;
            Check(!reset.IsEnabled && !dialog.IsPrimaryButtonEnabled, "Pending image apply leaves Reset/Save enabled.");
            // Admit a stale queued UI invocation after proving the actual
            // disabled presentation. Keep the real image operation pending;
            // only the control enablement is changed by the fixture, so the
            // real event handlers must independently refuse overlapping work.
            reset.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true;
            Click(reset);
            var resetFlyout = (Flyout)reset.Flyout;
            await Until(() => ((FrameworkElement)resetFlyout.Content).IsLoaded, "Queued Reset confirmation did not realize.");
            var confirm = Descendants((DependencyObject)resetFlyout.Content).OfType<Button>().Single(button => Equals(button.Content, "Reset"));
            Click(confirm); resetFlyout.Hide();
            var primary = Descendants(dialog).OfType<Button>().FirstOrDefault(button => button.Name == "PrimaryButton")
                ?? throw new InvalidOperationException("Actual dialog primary button was not realized.");
            Click(primary);
            await Task.Delay(100);
            Check(wire.Mutations.Count == 1 && !closed, "Pending image apply permitted a queued Reset/Save mutation or closed the draft.");
            reset.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            wire.ReleaseImage.TrySetResult();
            await Until(() => reset.IsEnabled && dialog.IsPrimaryButtonEnabled, "Image completion did not restore editor controls.");
            Program.Log("PASS: uploaded opaque local artwork is Current and displays returned image bytes; pending real Apply blocks both queued Reset and Save handlers.");
        }
        finally { wire.ReleaseImage.TrySetResult(); dialog.Hide(); await showing; wire.HoldImage = false; }
    }

    private static MediaItemDetail Item() => new() { ContentId = "fixture-movie", Type = "movie", Title = "Fixture Manual Title", Tagline = "Fixture manual tagline", Overview = "Fixture overview", Year = 2026, LockedFields = [0, 1, 10] };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static object? Get(object target, string name) => target.GetType().GetField(name, Instance)!.GetValue(target);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Instance)!.Invoke(target, args);
    private static async Task Until(Func<bool> ready, string message) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); Check(ready(), message); }
    private static bool IsVisible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return element.Visibility == Visibility.Visible;
    }
    private static void TraceVisibility(string label, FrameworkElement element)
    {
        var chain = new List<string>();
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement frame) chain.Add($"{frame.GetType().Name}#{frame.Name}={frame.Visibility},loaded={frame.IsLoaded},size={frame.ActualWidth:0.#}x{frame.ActualHeight:0.#}");
        Program.Log("TRACE " + label + ": " + string.Join(" > ", chain));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
        if (count == 0 && root is ContentControl { Content: DependencyObject content }) { yield return content; foreach (var next in Descendants(content)) yield return next; }
        if (count == 0 && root is Panel panel) foreach (var child in panel.Children) { yield return child; foreach (var next in Descendants(child)) yield return next; }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal string Caption = "artwork";
        internal bool FailUnlock, HoldImage;
        internal int UploadRequests;
        internal List<string> ImagePaths = [];
        internal List<(string Method, string Path, JsonElement Body)> Mutations = [];
        internal TaskCompletionSource ReleaseImage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Scheme == "upload") { UploadRequests++; throw new InvalidOperationException("Opaque upload identity cannot be fetched."); }
            if (request.RequestUri.Host != "caption-metadata.fixture.invalid") throw new InvalidOperationException("Native metadata fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.StartsWith("/art/", StringComparison.Ordinal))
            {
                ImagePaths.Add(path);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1cAAAAASUVORK5CYII=")) };
            }
            if (request.Method is { } method && (method == HttpMethod.Patch || method == HttpMethod.Post) && path.StartsWith("/api/v2/admin/items/", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Mutations.Add((method.Method, path, doc.RootElement.Clone()));
                if (path.EndsWith("/metadata", StringComparison.Ordinal) && FailUnlock) return Json(new { detail = "Fixture unlock refused", status = 403 }, HttpStatusCode.Forbidden);
                if (path.EndsWith("/images/apply", StringComparison.Ordinal) && HoldImage) await ReleaseImage.Task.WaitAsync(ct);
                return Json(path.EndsWith("/metadata", StringComparison.Ordinal) ? Item() : new { outcome = "queued" });
            }
            object body = path switch
            {
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 22, client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_idempotent_writes = true },
                "/api/v2/settings/values/effective" => new { items = new[] { new { key = "ui.card_presentation", source = "profile_client", value = new { poster_size = "standard", caption = Caption } } } },
                "/api/v2/admin/items/fixture-movie/images" => new { items = new[] {
                    new { type = "poster", original_url = "upload://opaque-current", provider_id = "local", url = "https://caption-metadata.fixture.invalid/art/current.png" },
                    new { type = "poster", original_url = "https://provider.fixture/alternate", provider_id = "fixture", url = "https://caption-metadata.fixture.invalid/art/alternate.png" } },
                    current = new { poster_url = "/art/current.png" }, provider_errors = new { }, page = new { has_more = false } },
                "/api/v2/catalog/items/fixture-movie" => Item(),
                _ => new { items = Array.Empty<object>(), settings = Array.Empty<object>(), providers = Array.Empty<object>(), state = "unavailable", sections = Array.Empty<object>(), devices = Array.Empty<object>() },
            };
            return Json(body);
        }
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), System.Text.Encoding.UTF8, "application/json") };
    }
}
