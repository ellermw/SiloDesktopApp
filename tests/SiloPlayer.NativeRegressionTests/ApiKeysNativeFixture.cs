using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// The real SettingsPage "API Keys" section against a synthetic server (made-up key values only): listed, paged, empty,
/// creation switched off by the server, load failed, the Create dialog, the one-time secret view and the Revoke confirmation,
/// each captured at 1440 and 800 wide. The secret is never written to the log; only the PNG shows the made-up value.
/// </summary>
internal static class ApiKeysNativeFixture
{
    private const string MadeUpSecret = "silo_made_up_key_for_screenshots_9f8e7d6c5b4a3210";

    internal static async Task RunAsync(Window window)
    {
        var field = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var oldContent = window.Content;
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://api-keys-fixture.invalid");
        var authApi = new AuthApi(client);
        using var auth = new AuthService(client, authApi);
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("p1", profile: new() { Id = "p1", Name = "Primary", IsPrimary = true });
        var api = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "api-keys-settings"));
        var theme = new ThemeService(settings, api);
        var vm = new SettingsViewModel(api, new CatalogApi(client), authApi, new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(api).AddSingleton(settings)
            .AddSingleton(authApi).AddSingleton(new ApiKeysApi(client)).AddSingleton(vm).AddSingleton(new NavigationService()).AddSingleton(new ToastService())
            .AddSingleton<LoginViewModel>().AddSingleton<SignupViewModel>()
            .AddSingleton(new CardOverlayService(api)).AddSingleton(new UICustomizationService(api)).BuildServiceProvider();
        field.SetValue(null, services);
        var host = new Grid();
        SettingsPage? page = null;
        try
        {
            window.Content = host;
            page = new SettingsPage();
            host.Children.Add(page);
            await Resize(window, 1440);
            Invoke(page, "BuildSettingsOverview");

            // Where the section sits in the settings navigation: right after Connect Apps, as in the official list.
            var navigation = (StackPanel)page.FindName("ConnectionsNavGroup");
            var order = navigation.Children.OfType<Button>().Select(button => button.Tag as string).ToList();
            Check(order.IndexOf("ApiKeys") == order.IndexOf("ConnectApps") + 1, "API Keys follows Connect Apps in the Connections group");
            Check(Descendants<Button>(page).Any(button => button.Tag is "ApiKeys" && AutomationProperties.GetName(button).StartsWith("API Keys.", StringComparison.Ordinal)),
                "the settings directory has an API Keys card");

            foreach (var width in new[] { 1440, 800 })
            {
                await Resize(window, width);
                string Name(string state) => $"apikeys-{state}-{width}.png";

                // ── Listed, with a second page ──
                wire.Reset(enabled: true, keys: 3, pageSize: 2);
                await Open(page);
                var texts = Texts(page.FindName("ApiKeysPanel"));
                Check(texts.Contains("API Keys") && texts.Contains("Keys let scripts and apps read and update your Silo data without your password."), "the title and subtitle are shown");
                Check(texts.Contains("Your keys") && texts.Contains("Whole account") && texts.Any(text => text.StartsWith("Created ", StringComparison.Ordinal)), "the list shows each key's access and created date");
                Check(texts.Contains("Never used") && texts.Any(text => text.StartsWith("Last used ", StringComparison.Ordinal)), "the list shows never used and last used");
                Check(Revokes(page).Count == 2 && Find<Button>(page, "ApiKeysLoadMore") is { IsEnabled: true }, "two keys and Load more on the first page");
                Check(((FrameworkElement)page.FindName("ApiKeysCreateButton")).Visibility == Visibility.Visible, "Create Key is offered");
                await Capture((FrameworkElement)page, Name("listed"));
                await CheckNarrowRow(page);
                Click(Find<Button>(page, "ApiKeysLoadMore")!);
                await Until(() => Revokes(page).Count == 3);
                Check(Find<Button>(page, "ApiKeysLoadMore") == null, "Load more goes once the list is complete");
                Check(Texts(page.FindName("ApiKeysPanel")).Any(text => text.StartsWith("Profile: Kids", StringComparison.Ordinal)), "a profile key names its profile");

                // ── Empty ──
                wire.Reset(enabled: true, keys: 0);
                await Open(page);
                Check(Texts(page.FindName("ApiKeysPanel")).Contains("No API keys yet."), "an empty list says so");
                await Capture((FrameworkElement)page, Name("empty"));

                // ── Creation switched off by the server ──
                wire.Reset(enabled: false, keys: 1);
                await Open(page);
                Check(Texts(page.FindName("ApiKeysPanel")).Contains("This server doesn't let users create API keys. Ask its admin to turn that on."), "a server without user keys says so");
                Check(((FrameworkElement)page.FindName("ApiKeysCreateButton")).Visibility == Visibility.Collapsed, "no Create Key when the server turned it off");
                await Capture((FrameworkElement)page, Name("disabled"));

                // ── The limit ──
                wire.Reset(enabled: true, keys: 3, max: 3);
                await Open(page);
                Check(Texts(page.FindName("ApiKeysPanel")).Contains("You've reached the limit of 3 keys. Revoke one to create another.")
                    && ((FrameworkElement)page.FindName("ApiKeysCreateButton")).Visibility == Visibility.Collapsed, "at the limit the page says so and offers no Create Key");

                // ── Load failed ──
                wire.Reset(enabled: true, keys: 2);
                wire.FailReads = true;
                await Open(page);
                Check(Texts(page.FindName("ApiKeysPanel")).Count(text => text == "Couldn't load your API keys.") == 2, "a failed load shows the failure for the settings and for the list");
                Check(((FrameworkElement)page.FindName("ApiKeysCreateButton")).Visibility == Visibility.Collapsed, "no Create Key after a failed load");
                await Capture((FrameworkElement)page, Name("load-failed"));

                // ── The Create dialog ──
                wire.Reset(enabled: true, keys: 2);
                await Open(page);
                Click((Button)page.FindName("ApiKeysCreateButton"));
                var dialog = await NextDialog(page);
                Check(dialog.Title as string == "Create API Key", "the Create dialog is titled Create API Key");
                var primary = Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton");
                var label = Find<TextBox>(dialog, "ApiKeyLabel")!;
                var target = Find<ComboBox>(dialog, "ApiKeyTarget")!;
                Check(!dialog.IsPrimaryButtonEnabled, "Create needs at least one scope");
                Check(target.Items.Count == 3 && target.SelectedIndex == 0 && (target.Items[0] as ComboBoxItem)?.Content as string == "Whole account", "Acts as lists Whole account and every profile");
                var scopes = Descendants<CheckBox>(dialog).ToList();
                Check(scopes.Count == 2 && scopes.All(box => AutomationProperties.GetName(box).Contains(':')), "only the user scopes are offered");
                var dialogTexts = Texts(dialog);
                Check(dialogTexts.Contains("Choose at least one. No key can play, stream or download media."), "the access hint is shown");
                Check(dialogTexts.Any(text => text.StartsWith("A key for one profile always acts as that profile", StringComparison.Ordinal)), "the Acts as hint is shown");
                Check(dialogTexts.Contains("Read the profile's watch history, progress, ratings, favorites, watchlist and hidden titles, and export its data."), "a scope shows its description");
                label.Text = "Backup script";
                scopes[1].IsChecked = true;
                await Until(() => dialog.IsPrimaryButtonEnabled);
                scopes[0].IsChecked = true;
                await Capture(dialog, Name("create"));

                // ── The one-time secret ──
                Click(primary);
                await Until(() => Find<TextBlock>(dialog, "ApiKeySecret") != null);
                var secret = Find<TextBlock>(dialog, "ApiKeySecret")!;
                Check(secret.Text == MadeUpSecret && Find<Button>(dialog, "ApiKeyCopy") != null, "the new key is shown once with Copy & Close");
                var view = Texts(dialog);
                Check(view.Contains("Copy your API key now. You can't see it again.") && view.Contains("How to use this key")
                    && view.Contains("Authorization: Bearer silo_made_u…") && view.Contains("X-Profile-Id: p1") && Find<HyperlinkButton>(dialog, "ApiKeysDocs") != null,
                    "the usage block names the header, the profile header for a whole-account key and the API reference");
                Check(wire.Posts.Count == 1 && wire.Posts[0].GetProperty("label").GetString() == "Backup script"
                    && !wire.Posts[0].TryGetProperty("profile_id", out _) && wire.Posts[0].GetProperty("scopes").GetArrayLength() == 2, "exactly one create request with label and scopes and no profile for a whole-account key");
                await Capture(dialog, Name("secret"));
                dialog.Hide();
                await Until(() => ApiKeyDialog(page) == null);
                Check(secret.Text.Length == 0, "the secret leaves the screen with the dialog");
                await Until(() => Revokes(page).Count == 3);

                // ── The Revoke confirmation ──
                Click(Revokes(page)[0]);
                dialog = await NextDialog(page);
                Check(dialog.Title as string == "Revoke API key" && Texts(dialog).Any(text => text.EndsWith("stops working at once. This can't be undone.", StringComparison.Ordinal)), "the Revoke confirmation says what happens");
                await Capture(dialog, Name("revoke"));
                dialog.Hide();
                await Until(() => ApiKeyDialog(page) == null);
                Check(wire.Deletes.Count == 0, "cancelling the confirmation revokes nothing");
                Click(Revokes(page)[0]);
                dialog = await NextDialog(page);
                Click(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
                await Until(() => ApiKeyDialog(page) == null && wire.Deletes.Count == 1);
                await Until(() => Revokes(page).Count == 2);
            }
            Program.Log("PASS: Settings API Keys verified in the actual SettingsPage at 1440 and 800 wide (listed, paged, empty, disabled, limit, load failed, create, secret, revoke).");
        }
        finally
        {
            if (page != null) host.Children.Remove(page);
            window.Content = oldContent;
            await Task.Delay(300);
            field.SetValue(null, previous);
        }
    }

    private static ContentDialog? ApiKeyDialog(SettingsPage page) => (ContentDialog?)typeof(SettingsPage).GetField("_apiKeyDialog", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page);

    private static async Task<ContentDialog> NextDialog(SettingsPage page)
    {
        await Until(() => ApiKeyDialog(page) is { IsLoaded: true } dialog && Descendants<Button>(dialog).Any(button => button.Name == "CloseButton"));
        return ApiKeyDialog(page)!;
    }

    private static async Task Open(SettingsPage page)
    {
        Invoke(page, "ShowSettingsOverview");
        Invoke(page, "ShowSettingsDetail", page.FindName("ApiKeysTab"));
        var header = (FrameworkElement)page.FindName("ApiKeysHeader");
        await Until(() => header.Visibility == Visibility.Visible && !Texts(page.FindName("ApiKeysPanel")).Contains("Loading API keys…"));
    }

    /// <summary>A key row under 420 wide puts Revoke beneath the key's text, never on top of it. The window does not get that narrow, so the row itself is narrowed.</summary>
    private static async Task CheckNarrowRow(SettingsPage page)
    {
        var revoke = Revokes(page)[0];
        var row = (Grid)revoke.Parent;
        var info = row.Children.OfType<StackPanel>().First();
        var before = Grid.GetRow(revoke);
        row.Width = 360;
        await Until(() => Grid.GetRow(revoke) == 1);
        row.UpdateLayout();
        var top = revoke.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        var textEnd = info.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, info.ActualHeight)).Y;
        Check(revoke.ActualHeight > 0 && top >= textEnd, "in a narrow row Revoke sits beneath the key's text");
        row.ClearValue(FrameworkElement.WidthProperty);
        await Until(() => Grid.GetRow(revoke) == before);
    }

    private static List<Button> Revokes(SettingsPage page)
        => Descendants<Button>((DependencyObject)page.FindName("ApiKeysContent")).Where(button => AutomationProperties.GetName(button).StartsWith("Revoke ", StringComparison.Ordinal)).ToList();

    private static T? Find<T>(DependencyObject root, object id) where T : FrameworkElement
        => Descendants<T>(root).FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == (string)id);

    private static List<string> Texts(object root)
        => Descendants<TextBlock>((DependencyObject)root).Where(text => text.Visibility == Visibility.Visible).Select(text => text.Text).ToList();

    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException("API keys fixture: " + message); }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }

    private static async Task Until(Func<bool> ready)
    {
        for (var i = 0; i < 200; i++)
        {
            try { if (ready()) return; } catch (InvalidOperationException) { }
            await Task.Delay(25);
        }
        throw new TimeoutException("API keys native state timed out");
    }

    private static async Task Resize(Window window, int width, int height = 900)
    {
        await Until(() => window.Content is FrameworkElement { XamlRoot: not null });
        var scale = ((FrameworkElement)window.Content).XamlRoot.RasterizationScale;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        await Until(() => Math.Abs(((FrameworkElement)window.Content).ActualWidth - width) < 2);
        await Task.Delay(250);
    }

    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(200);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "the capture of " + name + " was empty");
        var file = await (await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory))).CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
        Program.Log($"INFO: capture {name} {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly List<(string Id, string Label, string? Profile, string? Used, string[] Scopes)> _keys = [];
        private bool _enabled = true;
        private int _max = 5, _pageSize = 50, _next = 100;
        internal bool FailReads;
        internal readonly List<JsonElement> Posts = [];
        internal readonly List<string> Deletes = [];

        internal void Reset(bool enabled, int keys, int max = 5, int pageSize = 50)
        {
            _keys.Clear(); Posts.Clear(); Deletes.Clear(); FailReads = false;
            _enabled = enabled; _max = max; _pageSize = pageSize;
            var seed = new (string Label, string? Profile, string? Used, string[] Scopes)[]
            {
                ("Home automation", null, null, ["watch:read", "catalog:read"]),
                ("Kids tablet script", "p2", "2026-11-20T18:42:00.000Z", ["libraries:read"]),
                ("Plex watch-history sync with a long descriptive label that has to wrap on a narrow window", null, "2026-11-22T07:05:00.000Z", ["watch:read", "watch:write", "catalog:read", "libraries:read", "requests", "notifications"]),
            };
            for (var i = 0; i < keys; i++) _keys.Add(((i + 1).ToString(), seed[i].Label, seed[i].Profile, seed[i].Used, seed[i].Scopes));
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/profiles")
                return Json(new { items = new object[] { new { id = "p1", name = "Primary", is_primary = true }, new { id = "p2", name = "Kids", is_child = true } }, max_profiles = 5 });
            if (path == "/api/v2/api-keys/scopes")
                return FailReads ? Json(new { title = "Server error", status = 500 }, HttpStatusCode.InternalServerError)
                    : Json(new
                    {
                        available = true, user_keys_enabled = _enabled, max_user_keys = _max,
                        scopes = new object[]
                        {
                            new { name = "catalog:read", description = "Browse the catalog", audience = "user" },
                            new { name = "watch:read", description = "Read watch state", audience = "user" },
                            new { name = "admin:all", description = "Everything", audience = "admin" },
                        },
                    });
            if (path == "/api/v2/api-keys" && request.Method == HttpMethod.Get)
            {
                if (FailReads) return Json(new { title = "Server error", status = 500 }, HttpStatusCode.InternalServerError);
                var query = request.RequestUri.Query;
                var offset = query.Contains("cursor=o") ? int.Parse(query[(query.IndexOf("cursor=o", StringComparison.Ordinal) + 8)..].Split('&')[0]) : 0;
                var page = _keys.Skip(offset).Take(_pageSize).Select(key => new Dictionary<string, object?>
                {
                    ["id"] = key.Id, ["user_id"] = "1", ["label"] = key.Label, ["key_prefix"] = "silo_made", ["rate_tier"] = "standard", ["scopes"] = key.Scopes,
                    ["created_at"] = "2026-11-01T10:00:00.000Z", ["profile_id"] = key.Profile, ["last_used_at"] = key.Used,
                }.Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value)).ToArray();
                var more = offset + _pageSize < _keys.Count;
                return Json(more ? new { items = page, page = new { has_more = true, next_cursor = "o" + (offset + _pageSize) } } : (object)new { items = page, page = new { has_more = false } });
            }
            if (path == "/api/v2/api-keys" && request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Posts.Add(body.RootElement.Clone());
                var label = body.RootElement.GetProperty("label").GetString()!;
                var scopes = body.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()!).ToArray();
                _keys.Add(((_next++).ToString(), label, body.RootElement.TryGetProperty("profile_id", out var profile) ? profile.GetString() : null, null, scopes));
                return Json(new { id = (_next - 1).ToString(), user_id = "1", label, key = MadeUpSecret, scopes, rate_tier = "standard", created_at = "2026-11-23T09:00:00.000Z" }, HttpStatusCode.Created);
            }
            if (path.StartsWith("/api/v2/api-keys/", StringComparison.Ordinal) && request.Method == HttpMethod.Delete)
            {
                var id = path["/api/v2/api-keys/".Length..];
                Deletes.Add(id);
                _keys.RemoveAll(key => key.Id == id);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return Json(new { items = Array.Empty<object>() });
        }
    }
}
