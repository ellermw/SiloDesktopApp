using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Integrated acceptance fixture: requires the coordinator's production Appearance
/// registration/page. Activates that page through the real shell and Frame, then
/// exercises the real retained editor. Authored source only; no run is claimed.
/// </summary>
internal static class AdminAppearanceOverlayNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        using var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var settings = new SettingsApi(client);
        var overlays = new CardOverlayService(settings);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(settings).AddSingleton(overlays).AddSingleton(new CatalogApi(client))
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService())
            .AddSingleton(new SettingsService(Program.ResultDirectory));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!
            .GetMethod("Add", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = servicesField.GetValue(null);
        var previousContent = window.Content;
        var previousName = DocumentTitle.AppName;
        Frame? outer = null;
        FrameworkElement? page = null;
        try
        {
            servicesField.SetValue(null, services);
            new ThemeService(new SettingsService(Program.ResultDirectory), settings).ApplyTheme("midnight-cinema");
            await overlays.EnsureLoadedAsync();
            window.AppWindow.ResizeClient(new(1440, 1000));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            AdminShellPage.TryOpenHref("/admin/settings/appearance");
            await Until(() => outer.Content is AdminShellPage, "Appearance shell activation");
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)shell.GetType().GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!;
            await Until(() => inner.Content?.GetType().Name == "AdminAppearanceSettingsPage", "production Appearance registration");
            page = (FrameworkElement)inner.Content;
            await Until(() => Descendants(page).Any(element => Id(element) == "AdminAppearanceOverlayEditor" && element.IsLoaded), "overlay editor mounted");
            var editor = Descendants(page).Single(element => Id(element) == "AdminAppearanceOverlayEditor");
            var type = editor.GetType();
            var bind = type.GetMethod("Bind")!;
            var capabilities = type.GetMethod("SetCapabilities")!;
            var builtIns = (string)type.GetProperty("BuiltInDefaults", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var images = new[] { new OverlayLibraryImage { Ref = server.Reference, Name = "Synthetic image", Width = 1, Height = 1 } };
            capabilities.Invoke(editor, [true, new HashSet<string> { "rt_critic", "rt_audience" }]);
            var changes = new List<string>();
            type.GetEvent("Changed")!.AddEventHandler(editor, (Action<string>)(json => changes.Add(json)));
            bind.Invoke(editor, [builtIns, true, true, 29, images]);
            await Task.Delay(60);
            Check(changes.Count == 0 && server.Writes == 0, "binding does not stage or write a viewer profile");
            Check(!Descendants(editor).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal)), "official editor labels resolve");

            // These interactions flow into the parent controller through Changed.
            var year = Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-year");
            year.IsOn = true;
            await Until(() => changes.Count > 0, "native toggle stages defaults");
            Check(Items(changes.Last()).GetProperty("year").GetProperty("enabled").GetBoolean(), "actual year toggle reaches the serialized document");
            Invoke(Find<Button>(editor, "AppearanceOverlayPreset-plate"));
            await Until(() => Root(changes.Last()).GetProperty("preset").GetString() == "plate", "plate preset stages");
            Find<Slider>(editor, "AppearanceOverlaySize").Value = 1.5;
            await Until(() => Root(changes.Last()).TryGetProperty("style", out var style) && style.GetProperty("size").GetDouble() == 1.5, "size stages");
            Check(server.Writes == 0, "preset, look and toggle edits remain staged");

            var variant = Find<ComboBox>(editor, "AppearancePreviewVariant");
            variant.SelectedIndex = 1;
            var status = Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-show_status"); status.IsOn = true;
            var network = Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-network"); network.IsOn = true;
            await Task.Delay(80);
            var preview = Find<FrameworkElement>(editor, "AppearanceOverlayPreview");
            Check(Descendants(preview).OfType<TextBlock>().Any(text => text.Text == "HBO"), "live show preview uses the native badge renderer and show-only sample");

            var imageButton = Find<Button>(editor, "AppearanceOverlayImages-resolution");
            var flyout = (Flyout)imageButton.Flyout;
            flyout.ShowAt(imageButton);
            await Until(() => flyout.Content is ScrollViewer { IsLoaded: true }, "image flyout opened");
            var content = (FrameworkElement)flyout.Content;
            var key = Find<AutoSuggestBox>(content, "AppearanceOverlayValueKey-resolution");
            key.Text = "2160p";
            var localImage = Descendants(content).OfType<ComboBox>().Last();
            localImage.SelectedItem = localImage.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == server.Reference);
            key.Focus(FocusState.Keyboard);
            var focus = FocusManager.GetFocusedElement(editor.XamlRoot);
            var before = changes.Count;
            var staged = changes.Last();
            bind.Invoke(editor, [staged, true, true, 29, images]);
            await Task.Delay(60);
            Check(changes.Count == before && ReferenceEquals(key, Find<AutoSuggestBox>(content, "AppearanceOverlayValueKey-resolution")) && key.Text == "2160p",
                "rebinding keeps open image controls and pending key without staging");
            Check(ReferenceEquals(focus, FocusManager.GetFocusedElement(editor.XamlRoot)), "rebinding retains native keyboard focus");
            Check((variant.SelectedItem as ComboBoxItem)?.Tag as string == "show", "rebinding keeps local show preview selection");
            Invoke(Find<Button>(content, "AppearanceOverlayAddValue-resolution"));
            await Until(() => Items(changes.Last()).GetProperty("resolution").TryGetProperty("valueImages", out var map) && map.TryGetProperty("2160p", out _), "value image added");
            Check(Items(changes.Last()).GetProperty("resolution").GetProperty("valueImages").GetProperty("2160p").GetString() == server.Reference,
                "exact raw API key assigns a whole-badge image");
            flyout.Hide();
            await Until(() => !content.IsLoaded, "image flyout closed");
            variant.SelectedIndex = 0;
            await Until(() => Descendants(preview).OfType<Border>().Any(badge => badge.Tag as string == "resolution" && badge.Child is Image), "whole-value image decoded in live native preview");
            Check(true, "live preview renders the selected image through the existing authenticated renderer");
            before = changes.Count;
            bind.Invoke(editor, [changes.Last(), true, true, 29, Array.Empty<OverlayLibraryImage>()]);
            await Task.Delay(80);
            Check(!Descendants(preview).OfType<Border>().Any(badge => badge.Tag as string == "resolution" && badge.Child is Image), "deleted library reference falls back to the native text badge");
            Check(changes.Count == before, "image-library refresh never edits stored assignments");

            before = changes.Count;
            bind.Invoke(editor, [builtIns, true, true, 24, images]);
            Check(Find<Button>(editor, "AppearanceOverlayPreset-band").Visibility == Visibility.Collapsed, "old manifests hide advanced presets");
            Check(Find<Button>(editor, "AppearanceOverlayImages-resolution").Visibility == Visibility.Collapsed, "old manifests hide image editors");
            Check(Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-availability").IsEnabled == false ||
                !VisibleThroughParents(Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-availability")), "new overlay categories respect manifest support");
            Check(changes.Count == before, "capability changes do not emit settings changes");

            bind.Invoke(editor, [builtIns, false, true, 29, images]);
            Check(!year.IsEnabled, "server overlay toggle makes defaults controls inert");
            Check(Find<Button>(editor, "AppearanceOverlayUpload").IsEnabled, "image library stays usable when overlays are disabled");
            capabilities.Invoke(editor, [false, new HashSet<string>()]);
            Check(Find<Button>(editor, "AppearanceOverlayUpload").Visibility == Visibility.Collapsed, "missing image storage hides upload");
            Check(Find<Button>(editor, "AppearanceOverlayDelete-" + server.Reference).IsEnabled, "existing images can be removed without upload storage");
            Check(!VisibleThroughParents(Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-rating_rt")), "ratings capability hides restricted source controls");
            Check(VisibleThroughParents(Find<ToggleSwitch>(editor, "AppearanceOverlayToggle-rating_imdb")), "IMDb remains offered without a restricted ratings source");
            bind.Invoke(editor, [builtIns, true, false, 29, images]);
            before = changes.Count; year.IsOn = !year.IsOn;
            Check(changes.Count == before && server.Writes == 0, "read-only binding rejects programmatic control edits");
            type.GetMethod("SetImageLibraryEditable")!.Invoke(editor, [true]);
            Check(Find<Button>(editor, "AppearanceOverlayDelete-" + server.Reference).IsEnabled && !year.IsEnabled,
                "managed defaults can remain read-only while admin image operations stay available");
            bind.Invoke(editor, [builtIns, true, true, 29, images]);
            client.SetProfile("retired", "synthetic-retired");
            year.IsOn = !year.IsOn;
            Check(changes.Count == before && server.ProfileWrites == 0, "retired owner cannot stage or write profile settings");
            await MediaParityNativeFixture.CaptureAsync(page, "admin-appearance-overlay-editor.png");
        }
        finally
        {
            try
            {
                if (page?.XamlRoot is { } root)
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root)) popup.IsOpen = false;
                if (outer != null) outer.Content = null;
                window.Content = previousContent;
                if (page != null) await Until(() => !page.IsLoaded, "Appearance page unloaded before service restoration");
            }
            finally
            {
                servicesField.SetValue(null, previousServices);
                DocumentTitle.AppName = previousName;
            }
        }
    }

    private static JsonElement Root(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private static JsonElement Items(string json) => Root(json).GetProperty("items");
    private static string Id(DependencyObject element) => AutomationProperties.GetAutomationId(element);
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants(root).OfType<T>().Single(element => Id(element) == id);
    private static bool VisibleThroughParents(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> condition, string label)
    { for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25); if (!condition()) throw new InvalidOperationException("Timed out: " + label); }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Program.Log("PASS: " + label); }

    private sealed class Server : HttpMessageHandler
    {
        private static readonly byte[] Image = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");
        internal string Reference => Convert.ToHexString(SHA256.HashData(Image)).ToLowerInvariant() + ".webp";
        internal int Writes, ProfileWrites;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                if (path.Contains("/settings/values/", StringComparison.Ordinal)) ProfileWrites++;
            }
            if (path.StartsWith("/api/v2/overlay-images/", StringComparison.Ordinal))
            {
                var image = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Image) };
                image.Content.Headers.ContentType = new("image/webp"); return Task.FromResult(image);
            }
            object body = path switch
            {
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 29, supports_overlay_images = true, supports_batched_effective = true },
                "/api/v2/settings/overlay-config" => new { enabled = true, image_uploads_available = true, images = new[] { new { @ref = Reference, name = "Synthetic image", width = 1, height = 1 } } },
                "/api/v2/theme/branding" => new { storage_available = true },
                "/api/v2/admin/settings/effective" => new Dictionary<string, string>
                {
                    ["branding.accent_color"] = "", ["overlays.enabled"] = "true", ["defaults.card_overlays"] = "", ["defaults.ui.title_art"] = "true",
                },
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                "/api/v2/capabilities/ratings" => new { state = "available", sources = new[] { new { source = "rt_critic" }, new { source = "rt_audience" } } },
                _ => new { items = Array.Empty<object>() },
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new("\"appearance-fixture-v1\"");
            return Task.FromResult(response);
        }
    }
}
