using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class HouseholdContractsNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://profiles-metadata.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "admin" });
        Primary(auth);
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "household-local"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; local.Save(prefs);
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(http).AddSingleton(auth).AddSingleton(local)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<WebhookSyncApi>().AddSingleton<MediaMaintenanceApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>()
            .AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<ProfileSelectViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "household-images")))
            .AddSingleton<ItemDetailPrefetchCache>()
            // SettingsPage now hosts the interface-language group (Task4/5); register the production localization services.
            .AddSingleton(sp => new SiloPlayer.Core.Localization.AppLanguagePreference(sp.GetRequiredService<SettingsApi>()))
            .AddSingleton(sp => new SiloPlayer.Core.Localization.AppLocaleCoordinator(sp.GetRequiredService<SiloPlayer.Core.Localization.AppLanguagePreference>(),
                SiloPlayer.Core.Localization.LocaleService.Shared, sp.GetRequiredService<SettingsService>(), () => ["en-US"], "en-US", AppLocalization.PostTo(parent.DispatcherQueue)))
            .BuildServiceProvider();
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = slot.GetValue(null); slot.SetValue(null, services);
        try
        {
            var only = Environment.GetEnvironmentVariable("SILO_TASK1_CASE");
            if (only is null or "access") await Access(parent, wire, auth);
            if (only is null or "early-route") await EarlyRoute(parent);
            if (only is null or "overview-retire") await OverviewRetirement(parent);
            if (only is null or "retained") await Retained(parent, wire, auth);
            if (only is null or "mutations") await Mutations(parent, wire, auth);
            if (only is null or "refusal") await Refusal(parent, wire);
            if (only is null or "metadata") await Metadata(parent, wire, auth);
            if (only is null or "reconnect-webhook") await ReconnectWebhook(parent, client);
            if (only is null or "reconnect-editor") await ReconnectEditor(parent, client);
            if (only is null or "curator-reset") await CuratorReset(parent, wire, auth);
            if (only is null or "reset-copy") await ResetCopy(parent);
        }
        finally { wire.Gate?.TrySetResult(); slot.SetValue(null, previous); }
    }
    private static void Primary(AuthService auth) => auth.SelectProfile("primary", profile: new() { Id = "primary", Name = "Primary", IsPrimary = true });
    private static async Task ReconnectWebhook(StackPanel parent, SiloApiClient client)
    {
        using var plexLifetime = new CancellationTokenSource();
        var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(80);
        try
        {
            page.SelectSettingsSection("WebhookSync");
            await Until(() => ((TextBox)page.FindName("WebhookUrlBox")).Text.Contains("synthetic-secret"));
            var draft = (TextBox)page.FindName("WebhookManualServerName"); draft.Text = "Unfinished receiver draft";
            page.GetType().GetField("_webhookPlexAuthCts", Private)!.SetValue(page, plexLifetime);
            Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Reconnect did not advance read context.");
            await Task.Delay(100);
            Check(((FrameworkElement)page.FindName("WebhookSyncPanel")).Visibility == Visibility.Visible, "Reconnect ejected household manager.");
            Check(draft.Text == "Unfinished receiver draft", "Reconnect discarded receiver draft.");
            Check(!plexLifetime.IsCancellationRequested, "Reconnect canceled same-owner Plex setup.");
            await Call(page, "LoadWebhookConnectionsAsync");
            Check(draft.Text == "Unfinished receiver draft", "Same-owner refresh discarded receiver draft.");
        }
        finally { parent.Children.Remove(page); await Task.Delay(100); }
        Program.Log("PASS reconnect preserves active Webhook Sync, draft, setup lifetime and fresh reads.");
    }
    private static async Task ReconnectEditor(StackPanel parent, SiloApiClient client)
    {
        var dialog = new EditMetadataDialog(new() { ContentId = "movie", Type = "movie", Title = "Original title" }) { XamlRoot = parent.XamlRoot };
        var showing = dialog.ShowAsync();
        try
        {
            var reset = Field<Button>(dialog, "_reset"); await Until(() => reset.IsEnabled);
            var title = Field<Dictionary<string, TextBox>>(dialog, "_textInputs")["title"]; title.Text = "Unsaved title";
            Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Reconnect did not advance editor read context.");
            await Until(() => dialog.IsPrimaryButtonEnabled && reset.IsEnabled);
            Check(title.Text == "Unsaved title", "Reconnect replaced metadata draft.");
            Check(!Field<CancellationTokenSource>(dialog, "_lifetime").IsCancellationRequested, "Reconnect permanently retired editor.");
        }
        finally { dialog.Hide(); await showing; await Task.Delay(80); }
        Program.Log("PASS reconnect preserves metadata draft and revalidates Reset without disabling Save.");
    }
    private static async Task CuratorReset(StackPanel parent, Wire wire, AuthService auth)
    {
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Permissions = ["metadata_curation"] });
        wire.CapabilityForbidden = true;
        try
        {
            foreach (var unsupported in new[] { false, true })
            {
                wire.ResetUnsupported = unsupported; wire.MetadataOrder.Clear();
                var dialog = new EditMetadataDialog(new() { ContentId = "season", Type = "season" }) { XamlRoot = parent.XamlRoot };
                var showing = dialog.ShowAsync();
                try
                {
                    var reset = Field<Button>(dialog, "_reset"); await Until(() => reset.IsEnabled);
                    Click(reset); var confirmation = (StackPanel)((Flyout)reset.Flyout).Content; await Until(() => confirmation.IsLoaded);
                    Click(Descendants(confirmation).OfType<Button>().Single(button => Equals(button.Content, "Reset")));
                    await Until(() => wire.MetadataOrder.Count > 0 && !Field<bool>(dialog, "_pending"));
                    Check(wire.MetadataOrder.SequenceEqual(new[] { "POST:reset" }), "Curator reset used an unlock or legacy fallback.");
                    Check(dialog.HasSaved == !unsupported, "Curator reset reported wrong acceptance state.");
                }
                finally { dialog.Hide(); await showing; await Task.Delay(80); }
            }
        }
        finally { wire.CapabilityForbidden = wire.ResetUnsupported = false; auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" }); }
        Program.Log("PASS curator reset uses atomic-only admission and no mutation fallback on unsupported server.");
    }
    private static async Task ResetCopy(StackPanel parent)
    {
        foreach (var type in new[] { "season", "episode" })
        {
            var dialog = new EditMetadataDialog(new() { ContentId = "child", Type = type }) { XamlRoot = parent.XamlRoot };
            var showing = dialog.ShowAsync();
            try
            {
                await Until(() => Field<Button>(dialog, "_reset").IsEnabled);
                var copy = Field<TextBlock>(dialog, "_resetExplanation").Text;
                Check(!copy.Contains("including artwork locks", StringComparison.OrdinalIgnoreCase), "Child confirmation promises artwork reset.");
                Check(type != "season" || copy.Contains("series", StringComparison.OrdinalIgnoreCase) && copy.Contains("stays locked", StringComparison.OrdinalIgnoreCase), "Season confirmation omits series-owned artwork protection.");
            }
            finally { dialog.Hide(); await showing; await Task.Delay(80); }
        }
        Program.Log("PASS season and episode reset confirmations match row locks and series-owned artwork.");
    }
    private static async Task EarlyRoute(StackPanel parent)
    {
        var page = new SettingsPage(); page.SelectSettingsSection("WebhookSync");
        parent.Children.Add(page);
        try { await Until(() => ((TextBox)page.FindName("WebhookUrlBox")).Text.Contains("synthetic-secret")); }
        finally { parent.Children.Remove(page); await Task.Delay(100); }
        Program.Log("PASS household authorized route selected before Loaded loads owned connections.");
    }
    private static async Task OverviewRetirement(StackPanel parent)
    {
        var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(80);
        try
        {
            page.SelectSettingsSection("WebhookSync"); await Until(() => ((TextBox)page.FindName("WebhookUrlBox")).Text.Contains("synthetic-secret"));
            Invoke(page, "ShowSettingsOverview");
            Check(((TextBox)page.FindName("WebhookUrlBox")).Text == "" && Field<object?>(page, "_selectedWebhookConnection") == null, "Settings overview retained webhook secret/details.");
        }
        finally { parent.Children.Remove(page); await Task.Delay(80); }
        Program.Log("PASS household returning to directory retires secrets/details.");
    }
    private static async Task Access(StackPanel parent, Wire wire, AuthService auth)
    {
        foreach (var role in new[] { "admin", "user" })
        {
            auth.SetCurrentUser(new() { Id = "fixture", Role = role });
            auth.SelectProfile("secondary", profile: new() { Id = "secondary", Name = "Secondary" });
            var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(100);
            try
            {
                Check(!page.SidebarEntries.Any(e => e.Section == "WebhookSync"), "Secondary profile exposes Webhook Sync in directory/sidebar.");
                ((TextBox)page.FindName("SettingsSearchBox")).Text = "webhook";
                page.SelectSettingsSection("WebhookSync");
                Check(((FrameworkElement)page.FindName("WebhookSyncPanel")).Visibility == Visibility.Collapsed, "Deep link exposed webhook panel.");
                await Call(page, "LoadWebhookConnectionsAsync");
                Check(wire.WebhookRequests == 0, "Secondary direct load reached server.");
                foreach (var action in new[] { "WebhookCreateConnection_Click", "WebhookRotate_Click", "WebhookSaveActors_Click", "WebhookDelete_Click" })
                    Invoke(page, action, page, new RoutedEventArgs());
                Check(wire.WebhookRequests == 0, "Secondary retained action reached server.");
                Check(((Button)page.FindName("WebhookSyncTab")).Visibility == Visibility.Collapsed, "Search revealed forbidden tab.");
            }
            finally { parent.Children.Remove(page); await Task.Delay(80); }
        }
        auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" }); Primary(auth);
        Program.Log("PASS household access: directory/search/deep-link/direct-load/mutations denied to secondary admin and user.");
    }
    private static async Task Retained(StackPanel parent, Wire wire, AuthService auth)
    {
        var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(100);
        try
        {
            page.SelectSettingsSection("WebhookSync"); await Until(() => ((TextBox)page.FindName("WebhookUrlBox")).Text.Contains("synthetic-secret"));
            wire.HoldPath = "/events"; wire.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Call(page, "SelectWebhookConnectionAsync", new SiloPlayer.Core.Models.Settings.WebhookSyncConnection { Id = "c1", ServerName = "Old owner", WebhookUrl = "https://profiles-metadata.fixture.invalid/synthetic-secret" });
            await Until(() => wire.Held);
            auth.SelectProfile("secondary", profile: new() { Id = "secondary", Name = "Secondary" }); await Task.Delay(80);
            Check(((TextBox)page.FindName("WebhookUrlBox")).Text == "", "Retirement retained receiver secret.");
            Check(((Panel)page.FindName("WebhookConnectionsPanel")).Children.Count == 0, "Retirement retained connections.");
            wire.Gate.TrySetResult(); await pending;
            Check(((Panel)page.FindName("WebhookEventsHost")).Children.Count == 0, "Late events repopulated retired page.");
            Check(((FrameworkElement)page.FindName("WebhookConnectionDetail")).Visibility == Visibility.Collapsed, "Retirement kept detail visible.");
        }
        finally { wire.Gate?.TrySetResult(); wire.Gate = null; wire.HoldPath = null; parent.Children.Remove(page); await Task.Delay(80); Primary(auth); }
        Program.Log("PASS household retained page: immediate secret clearing and cancellation-ignoring late read retirement.");
    }
    private static async Task Mutations(StackPanel parent, Wire wire, AuthService auth)
    {
        foreach (var action in new[] { "WebhookCreateConnection_Click", "WebhookSaveConnection_Click", "WebhookRotate_Click", "WebhookDelete_Click", "WebhookSaveActors_Click" })
        {
            Primary(auth); wire.Held = false;
            var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(80);
            try
            {
                page.SelectSettingsSection("WebhookSync"); await Until(() => ((TextBox)page.FindName("WebhookUrlBox")).Text.Contains("synthetic-secret"));
                var provider = (ComboBox)page.FindName("WebhookProviderCombo");
                provider.SelectedItem = provider.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "emby"));
                ((TextBox)page.FindName("WebhookManualServerName")).Text = "Synthetic server";
                var profiles = (ComboBox)page.FindName("WebhookDefaultProfileCombo");
                profiles.SelectedItem = profiles.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "primary"));
                wire.HoldPath = action == "WebhookCreateConnection_Click" ? "/connections" : action == "WebhookRotate_Click" ? "/webhook/rotate" : action == "WebhookSaveActors_Click" ? "/profile-mappings" : "/c1";
                wire.HoldWrites = true; wire.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Program.Log("TRACE pending webhook mutation: " + action + ", defaultProfileSelected=" + (((ComboBox)page.FindName("WebhookDefaultProfileCombo")).SelectedItem is ComboBoxItem { Tag: string id } && id.Length > 0));
                Invoke(page, action, new Button(), new RoutedEventArgs());
                await Task.Delay(80);
                if (!wire.Held) Program.Log("TRACE mutation did not dispatch: " + action + ", error=" + page.ViewModel.ErrorMessage);
                await Until(() => wire.Held);
                auth.SelectProfile("secondary", profile: new() { Id = "secondary" });
                page.ViewModel.StatusMessage = "Replacement owner's status";
                wire.Gate.TrySetResult(); await Task.Delay(200);
                Check(((TextBox)page.FindName("WebhookUrlBox")).Text == "", "Late mutation restored receiver secret: " + action);
                Check(page.ViewModel.StatusMessage == "Replacement owner's status", "Late mutation replaced status: " + action);
                Check(Field<object?>(page, "_selectedWebhookConnection") == null, "Late mutation retained old connection: " + action);
            }
            finally { wire.Gate?.TrySetResult(); wire.Gate = null; wire.HoldPath = null; wire.HoldWrites = false; parent.Children.Remove(page); await Task.Delay(80); }
        }
        Primary(auth);
        Program.Log("PASS household retained create/update/rotate/delete/mapping writes retire without secrets or completion under replacement owner.");
    }
    private static async Task Refusal(StackPanel parent, Wire wire)
    {
        foreach (var code in new[] { "profile_verification_required", "permission_denied" })
        {
            wire.Refusal = code; var page = new SettingsPage(); parent.Children.Add(page); await Task.Delay(80);
            try
            {
                await Call(page, "LoadWebhookConnectionsAsync");
                var text = ((TextBlock)page.FindName("NoConnectionsText")).Text;
                Check(text.Contains("primary", StringComparison.OrdinalIgnoreCase) && (code != "profile_verification_required" || text.Contains("PIN")), "Authoritative refusal lacks primary/PIN guidance: " + text);
            }
            finally { parent.Children.Remove(page); await Task.Delay(80); wire.Refusal = null; }
        }
        Program.Log("PASS household authoritative PIN and permission refusal guidance.");
    }
    private static async Task Metadata(StackPanel parent, Wire wire, AuthService auth)
    {
        foreach (var supported in new[] { true, false })
        foreach (var type in new[] { "movie", "series", "season", "episode" })
        {
            wire.SupportsReset = supported; wire.MetadataOrder.Clear();
            var dialog = new EditMetadataDialog(new() { ContentId = "child", Type = type, Title = "Manual", LockedFields = [0, 10, 99] }) { XamlRoot = parent.XamlRoot };
            var showing = dialog.ShowAsync();
            try
            {
                var reset = Field<Button>(dialog, "_reset"); await Until(() => reset.IsLoaded && reset.IsEnabled);
                var confirmation = (StackPanel)((Flyout)reset.Flyout).Content;
                var copy = string.Join(" ", Descendants(confirmation).OfType<TextBlock>().Select(t => t.Text));
                Check(supported ? copy.Contains("all locks", StringComparison.OrdinalIgnoreCase) && copy.Contains("no value", StringComparison.OrdinalIgnoreCase) : copy.Contains("legacy", StringComparison.OrdinalIgnoreCase) && copy.Contains("fail", StringComparison.OrdinalIgnoreCase), "Reset confirmation does not describe selected semantics: " + copy);
                if (supported && type == "series") Check(copy.Contains("Locked seasons and episodes"), "Series reset omits descendant protection.");
                Click(reset); await Until(() => confirmation.IsLoaded);
                Click(Descendants(confirmation).OfType<Button>().Single(b => Equals(b.Content, "Reset")));
                await Until(() => dialog.HasSaved); await showing;
                Check(supported ? wire.MetadataOrder.SequenceEqual(new[] { "POST:reset" }) : wire.MetadataOrder.Last() == "POST:quick" && wire.MetadataOrder.First().StartsWith("PATCH:"), "Wrong reset request sequence.");
            }
            finally { dialog.Hide(); await showing; await Task.Delay(60); }
        }
        foreach (var boundary in new[] { "provider-failure", "pending-owner", "pending-close" })
        {
            wire.SupportsReset = true; wire.MetadataOrder.Clear(); wire.ProviderFails = boundary == "provider-failure";
            var dialog = new EditMetadataDialog(new() { ContentId = "child", Type = "season", LockedFields = [0, 10] }) { XamlRoot = parent.XamlRoot };
            var showing = dialog.ShowAsync();
            try
            {
                var reset = Field<Button>(dialog, "_reset"); await Until(() => reset.IsLoaded && reset.IsEnabled);
                if (!wire.ProviderFails) { wire.HoldPath = "/refresh-metadata"; wire.Held = false; wire.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
                Click(reset); var confirmation = (StackPanel)((Flyout)reset.Flyout).Content; await Until(() => confirmation.IsLoaded);
                Click(Descendants(confirmation).OfType<Button>().Single(b => Equals(b.Content, "Reset")));
                if (wire.ProviderFails) await Until(() => wire.MetadataOrder.Count > 0 && !Field<bool>(dialog, "_pending"));
                else
                {
                    await Until(() => wire.Held);
                    if (boundary == "pending-owner") auth.SelectProfile("secondary", profile: new() { Id = "secondary" });
                    else { dialog.Hide(); await showing; }
                    wire.Gate!.TrySetResult(); await Task.Delay(150);
                }
                Check(!dialog.HasSaved, "Failed/canceled/retired job was reported successful: " + boundary);
                Check(wire.MetadataOrder.SequenceEqual(new[] { "POST:reset" }), "Atomic failure/cancellation emitted unlock or restore.");
            }
            finally { wire.Gate?.TrySetResult(); dialog.Hide(); await showing; wire.Gate = null; wire.HoldPath = null; wire.ProviderFails = false; Primary(auth); await Task.Delay(80); }
        }
        wire.SupportsReset = true; wire.MetadataOrder.Clear(); wire.HoldPath = "/capabilities"; wire.Held = false; wire.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = new EditMetadataDialog(new() { ContentId = "child", Type = "season" }) { XamlRoot = parent.XamlRoot };
        var retiredShowing = retired.ShowAsync();
        try
        {
            await Until(() => wire.Held);
            auth.SelectProfile("secondary", profile: new() { Id = "secondary" }); wire.Gate.TrySetResult(); await Task.Delay(150);
            Check(!Field<Button>(retired, "_reset").IsEnabled, "Late capability enabled reset under replacement authority.");
            Check(wire.MetadataOrder.Count == 0 && !retired.HasSaved, "Retired metadata operation wrote or reported success.");
        }
        finally { retired.Hide(); await retiredShowing; wire.Gate = null; wire.HoldPath = null; Primary(auth); }
        Program.Log("PASS atomic/legacy metadata native controls, target-wide/descendant copy and retired capability.");
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static Task Call(object owner, string name, params object[] args) => (Task)Invoke(owner, name, args)!;
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 240 && !ready(); i++) await Task.Delay(20); Check(ready(), "Native household operation did not settle."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++) { var child = VisualTreeHelper.GetChild(owner, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private sealed class Wire : HttpMessageHandler
    {
        internal int WebhookRequests;
        internal bool SupportsReset = true, Held, HoldWrites, ProviderFails, CapabilityForbidden, ResetUnsupported;
        internal string? Refusal, HoldPath;
        internal TaskCompletionSource? Gate;
        internal List<string> MetadataOrder = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "profiles-metadata.fixture.invalid", "External request blocked.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/admin/catalog/capabilities" && CapabilityForbidden)
                return Json(new { detail = "Only acting admins can read capabilities" }, HttpStatusCode.Forbidden);
            if (HoldPath != null && path.EndsWith(HoldPath) && (!HoldWrites || request.Method != HttpMethod.Get)) { Held = true; await Gate!.Task; }
            if (path.Contains("/webhook-sync/"))
            {
                WebhookRequests++;
                if (Refusal != null) return Json(new { type = "https://fixture.invalid/docs/api/v2/problems/" + Refusal, detail = "Fixture refused", status = 403 }, HttpStatusCode.Forbidden);
                var connection = new { id = "c1", provider = "plex", server_name = "Synthetic connection", webhook_url = "/synthetic-secret" };
                if (request.Method == HttpMethod.Delete) return Json(new { }, HttpStatusCode.NoContent);
                if (request.Method != HttpMethod.Get) return Json(new { id = "c1", connection, webhook_url = "/synthetic-secret" });
                if (path.EndsWith("/connections")) return Json(new { items = new[] { connection }, page = new { has_more = false } });
                return Json(new { items = Array.Empty<object>(), mappings = Array.Empty<object>(), actors = Array.Empty<object>() });
            }
            if (path.EndsWith("/metadata") || path.EndsWith("/refresh-metadata"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                MetadataOrder.Add(request.Method == HttpMethod.Patch ? "PATCH:" + string.Join(",", body.RootElement.GetProperty("locked_fields").EnumerateArray().Select(v => v.GetInt32())) : "POST:" + body.RootElement.GetProperty("mode").GetString());
                if (ResetUnsupported && path.EndsWith("/refresh-metadata")) return Json(new { detail = "Unsupported mode" }, HttpStatusCode.UnprocessableEntity);
                if (ProviderFails) return Json(new { detail = "Synthetic provider unavailable" }, HttpStatusCode.BadGateway);
                return Json(new { id = "job", state = "queued" });
            }
            return Json(path switch
            {
                "/api/v2/profiles" => new { items = new[] { new { id = "primary", name = "Primary", is_primary = true } } },
                "/api/v2/admin/catalog/capabilities" => new { item_metadata_reset = SupportsReset },
                _ => (object)new { items = Array.Empty<object>(), state = "unavailable", sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            });
        }
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    }
}
