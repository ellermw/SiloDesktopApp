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
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class AdminIntegrationsNativeFixture
{
    private static readonly List<string> ReviewFailures = [];
    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = AdminIntegrationsSettings.AllKeys.ToDictionary(key => key, _ => "");
        internal readonly List<string> Writes = [];
        internal TaskCompletionSource? ReadGate, WriteGate;
        internal bool Conflict, FailRead, FailProviderAction, EnvironmentOwnedWeb, StaleWebRead, LostWebOperation, FailedWebInstallation;
        internal bool ChatConnected = true, LoginPending, WebOperationRunning, CreatedProvider, UsagePaused;
        internal string ActiveAccount = "account-1", LoginResult = "idle";
        internal int InFlight, MaximumInFlight, Reads, Version = 1;
        internal Server()
        {
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["ai.auth_mode"] = "api_key", ["ai.text_provider_id"] = "openai", ["ai.chat_model"] = "gpt-test",
                ["ai.max_concurrent_jobs"] = "2", ["subtitle_ai.batch_size"] = "40", ["subtitle_ai.context_neighbors"] = "2",
                ["subtitle_ai.asr_chunk_seconds"] = "600", ["subtitle_ai.transcribe_quota_jobs"] = "0", ["metadata_ai.on_view"] = "off",
                ["recommendations.enabled"] = "true", ["recommendations.embedding_provider_id"] = "openai",
                ["recommendations.embedding_model"] = "embedding-test", ["recommendations.diversity_lambda"] = "0.7",
                ["jellyfin_compat.web_version"] = "10.11.0", ["jellyfin_compat.web_install_dir"] = "/synthetic",
                ["jellyfin_compat.server_id"] = "synthetic-server-id", ["jellyfin_compat.web_dir"] = "/synthetic/current"
            }) Values[key] = value;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add(request.Method + " " + path);
                if (WriteGate is { } held) await held.Task;
                if (Conflict) return Json(new { }, HttpStatusCode.PreconditionFailed);
                if (path.StartsWith("/api/v2/admin/ai/chatgpt", StringComparison.Ordinal))
                {
                    if (path.EndsWith("/login", StringComparison.Ordinal))
                    {
                        LoginPending = true; LoginResult = "pending";
                        return Json(new { attempt_id = "attempt-1", expires_at = DateTimeOffset.UtcNow.AddMinutes(10), authorization_url = "https://auth.openai.com/authorize?state=synthetic-state", callback_uri = "http://127.0.0.1:51121/auth/callback" });
                    }
                    if (path.EndsWith("/complete", StringComparison.Ordinal))
                    {
                        ChatConnected = true; LoginPending = false; LoginResult = "connected";
                        Values["ai.auth_mode"] = "chatgpt"; Values["ai.chat_model"] = "gpt-test"; Version++;
                        return new(HttpStatusCode.NoContent);
                    }
                    if (path.EndsWith("/select", StringComparison.Ordinal)) { ActiveAccount = path.Contains("account-2", StringComparison.Ordinal) ? "account-2" : "account-1"; Version++; return Json(ChatStatus()); }
                    ChatConnected = false; return Json(new { revocation_confirmed = false });
                }
                if (path.EndsWith("/providers/check", StringComparison.Ordinal) && FailProviderAction) return Json(new { }, HttpStatusCode.BadRequest);
                if (path.EndsWith("/settings/check/ai_chat", StringComparison.Ordinal)) { UsagePaused = false; return Json(new { success = true, message = "Usage checked" }); }
                if (request.Method == HttpMethod.Put || path.EndsWith("/settings", StringComparison.Ordinal))
                {
                    Check(request.Headers.IfMatch.SingleOrDefault()?.ToString() == $"\"fixture-{Version}\"", "canonical strong ETag used");
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    var values = request.Method == HttpMethod.Put ? body.RootElement.GetProperty("values") : body.RootElement;
                    foreach (var property in values.EnumerateObject())
                        Values[request.Method == HttpMethod.Put ? property.Name : "jellyfin_compat." + property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    Version++;
                    return request.Method == HttpMethod.Put ? Tagged(new { values = Values, restart_required = false }) : Tagged(new { });
                }
                if (path.EndsWith("/check", StringComparison.Ordinal)) return Json(new { success = true, models = new[] { new { id = "gpt-test", kind = "chat" } } });
                if (path.EndsWith("/providers", StringComparison.Ordinal))
                {
                    if (FailProviderAction) return Json(new { }, HttpStatusCode.BadRequest);
                    CreatedProvider = true;
                    return Json(new { id = "created", type = "openai", name = "Created", base_url = "https://example.invalid/v1", has_api_key = true, capabilities = new[] { "chat", "embeddings" }, used_by = Array.Empty<string>() });
                }
                if (path.EndsWith("/web/install", StringComparison.Ordinal)) { WebOperationRunning = true; StaleWebRead = true; return Json(new { web_state = "installing", operation = new { id = "operation-1", state = "running", kind = "install", progress_percent = 40 } }, HttpStatusCode.Accepted); }
                if (path.EndsWith("/web/remove", StringComparison.Ordinal)) { FailedWebInstallation = false; return Json(new { web_state = "missing", operation = new { id = "remove-1", state = "succeeded", kind = "remove", progress_percent = 100 } }, HttpStatusCode.Accepted); }
                if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.NoContent);
                return Json(new { status = "started", embeddings = 11, taste_profiles = 12, taste_clusters = 13, cached_rows = 14 });
            }
            object bodyValue = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = EnvironmentOwnedWeb ? new[] { "jellyfin_compat.web_enabled" } : Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/admin/ai/capabilities" => new { reasoning_levels = new[] { "minimal", "high" }, openai_service_tiers = new[] { "default", "priority", "ultrafast" }, chatgpt_sign_in = true },
                "/api/v2/admin/ai/chatgpt" => ChatStatus(),
                "/api/v2/admin/ai/chatgpt/models" => new { models = new[] { new { id = "gpt-test", name = "Synthetic ChatGPT model" } } },
                "/api/v2/admin/ai/providers" => new { providers = ProviderList(), types = new[] { new { type = "openai", label = "OpenAI", default_base_url = "https://example.invalid/v1", capabilities = new[] { "chat", "embeddings" }, key_required = true } } },
                "/api/v2/admin/ai/providers/openai/models" => new { success = true, models = new[] { new { id = "gpt-test", kind = "chat" }, new { id = "embedding-test", kind = "embeddings" }, new { id = "unsupported-image-model", kind = "" } } },
                "/api/v2/admin/ai/providers/created/models" => new { success = true, models = new[] { new { id = "gpt-test", kind = "chat" } } },
                "/api/v2/admin/recommendations/status" => new { embeddings = new { running = false, count = 10, total = 10 }, taste_profiles = new { running = false, count = 2 }, cowatch = new { running = false, count = 2 }, recommendations = new { running = false, count = 2 }, lock_conflict = "", stale_sweep = new { pending = 0 } },
                "/api/v2/admin/jellyfin-compat/status" => WebStatus(),
                _ => new { }
            };
            if (path.EndsWith("/status", StringComparison.Ordinal) || path.EndsWith("/providers", StringComparison.Ordinal))
            {
                Reads++; InFlight++; MaximumInFlight = Math.Max(MaximumInFlight, InFlight);
                try
                {
                    if (ReadGate is { } gate) await gate.Task;
                    if (FailRead) return Json(new { }, HttpStatusCode.ServiceUnavailable);
                    return Tagged(bodyValue);
                }
                finally { InFlight--; }
            }
            return Tagged(bodyValue);
        }
        private object WebStatus()
        {
            var stale = StaleWebRead || LostWebOperation; StaleWebRead = false;
            return new { enabled = true, web_enabled = false, web_state = WebOperationRunning && !stale ? "installing" : FailedWebInstallation ? "failed" : "installed", installed_version = FailedWebInstallation ? null : "10.11.0", install_root = "/synthetic", install_path = "/synthetic/current", installer_ready = true, prerequisites = Array.Empty<object>(), operation = new { id = stale ? "older-operation" : "operation-1", state = WebOperationRunning && !stale ? "running" : "succeeded", kind = "install", progress_percent = WebOperationRunning && !stale ? 40 : 100 }, last_error = FailedWebInstallation ? "Synthetic invalid provenance" : "" };
        }
        private object ChatStatus() => new { active_client_id = ActiveAccount, accounts = new[] { new { client_id = "account-1", email = "first@example.invalid", connected = ChatConnected, needs_login = !ChatConnected, usage_limited = false }, new { client_id = "account-2", email = "second@example.invalid", connected = !UsagePaused, needs_login = false, usage_limited = UsagePaused } }, login_pending = LoginPending, login_result = LoginResult, attempt_id = "attempt-1" };
        private object[] ProviderList()
        {
            object Provider(string id, string name) => new { id, type = "openai", name, base_url = "https://example.invalid/v1", has_api_key = true, capabilities = new[] { "chat", "embeddings" }, used_by = Array.Empty<string>() };
            return CreatedProvider ? [Provider("openai", "OpenAI"), Provider("created", "Created")] : [Provider("openai", "OpenAI")];
        }
        private HttpResponseMessage Tagged(object body)
        {
            var response = Json(body); response.Headers.ETag = new($"\"fixture-{Version}\""); return response;
        }
    }

    private sealed class Browser : IAdminChatGptBrowser
    {
        internal int Opens;
        public Task<bool> OpenAsync(Uri authorizationUrl, MonitorAuthorityLease lease)
        {
            lease.ThrowIfRetired(); Check(authorizationUrl.Host == "auth.openai.com", "browser seam receives only official sign-in origin"); Opens++; return Task.FromResult(true);
        }
    }

    internal static async Task RunAsync(Window window)
    {
        using var handler = new Server(); using var http = new HttpClient(handler); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400); auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" }); auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        var browser = new Browser(); collection.AddSingleton<IAdminChatGptBrowser>(browser);
        await using var services = collection.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = servicesField.GetValue(null); var previousContent = window.Content; var outer = new Frame();
        AdminIntegrationsPage? page = null;
        try
        {
            ReviewFailures.Clear();
            servicesField.SetValue(null, services); window.Content = outer; navigation.Frame = outer;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
            page = await Open(outer, "/admin/settings/ai");
            await Until(() => Enabled(page, "AdminAiProviderAdd"), "provider load");
            var suggestions = Find<ComboBox>(page, "AdminIntegration-ai.chat_model-Models");
            ReviewCheck(suggestions.Items.OfType<ComboBoxItem>().Select(item => item.Tag?.ToString()).SequenceEqual(new[] { "gpt-test" }), "model suggestions require the exact usable kind");
            ReviewCheck(!Find<TextBox>(page, "AdminIntegration-ai.chat_model").IsReadOnly, "provider models retain manual entry");
            await CaptureBoth(window, outer, "integrations-ai");
            var textProvider = Find<ComboBox>(page, "AdminIntegration-ai.text_provider_id");
            textProvider.SelectedItem = textProvider.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "__add");
            var key = Find<PasswordBox>(page, "AdminAiProviderKey"); key.Password = "fixture-private-key";
            Invoke(Find<Button>(page, "AdminAiProviderTest"));
            Check(key.Password == "", "test clears PasswordBox immediately");
            await Idle(page);
            Check(handler.Writes.Count(entry => entry.EndsWith("/providers/check", StringComparison.Ordinal)) == 1, "provider test sent once");
            handler.FailProviderAction = true; key.Password = "fixture-failed-check-key";
            Invoke(Find<Button>(page, "AdminAiProviderTest")); Check(key.Password == "", "failed test clears secret before transport settles"); await Idle(page);
            key.Password = "fixture-failed-save-key"; Invoke(Find<Button>(page, "AdminAiProviderSave")); Check(key.Password == "", "failed save clears secret immediately"); await Idle(page);
            handler.FailProviderAction = false; key.Password = "fixture-created-key"; Invoke(Find<Button>(page, "AdminAiProviderSave")); await Idle(page);
            Check(handler.Writes.Count(entry => entry == "POST /api/v2/admin/ai/providers") == 2, "failed and successful provider saves are explicit one-shot actions");
            Check(((AdminIntegrationSettingsController)Field(page, "_controller")!).State.Values["ai.text_provider_id"] == "created", "created provider ID is selected in the ordinary model workflow");
            Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
            Invoke(Find<Button>(page, "AdminAiProviderAdd"));
            Invoke(Find<Button>(page, "AdminAiProviderCancel"));
            Invoke(Find<Button>(page, "AdminChatGptConnect"));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "ChatGPT manual sign-in dialog");
            var loginDialog = (ContentDialog)Field(page, "_dialog")!;
            await Capture(loginDialog, "integrations-chatgpt-dialog.png");
            // The first poll may join a read that began before the failure was armed; the second cannot.
            handler.FailRead = true; await Poll(page); await Poll(page);
            ReviewCheck(Find<Button>(loginDialog, "AdminChatGptComplete").IsEnabled && Field(page, "_login") != null && Field(page, "_domainLoaded") is true && Field(page, "_callbackError") is TextBlock { Text.Length: > 0 }, "a failed read during sign-in keeps the attempt completable and explains itself inside the dialog");
            handler.FailRead = false; await Poll(page);
            ReviewCheck(Field(page, "_login") != null && Field(page, "_callbackError") is TextBlock { Text: "" }, "the next good read keeps the sign-in attempt and clears its read notice");
            Invoke(Find<Button>(loginDialog, "AdminChatGptOpenBrowser"));
            await Until(() => browser.Opens == 1, "user-owned browser seam");
            var callback = Find<PasswordBox>(loginDialog, "AdminChatGptCallback"); callback.Password = "http://127.0.0.1:51121/auth/callback?state=synthetic-state&code=synthetic-code";
            Invoke(Find<Button>(loginDialog, "AdminChatGptComplete")); Check(callback.Password == "", "manual callback is cleared immediately");
            await Idle(page); Check(!loginDialog.IsLoaded, "matching login attempt closes dialog");
            var accountSelect = Find<ComboBox>(page, "AdminChatGptAccount"); accountSelect.SelectedItem = accountSelect.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "account-2"); await Idle(page);
            Check(handler.Writes.Contains("POST /api/v2/admin/ai/chatgpt/accounts/account-2/select"), "account picker issues official account-specific select");
            handler.UsagePaused = true; await Refresh(page);
            Check(Enabled(page, "AdminAiTextCheck"), "usage-paused authenticated account retains explicit text recheck");
            Invoke(Find<Button>(page, "AdminAiTextCheck")); await Idle(page);
            Check(!handler.UsagePaused, "text connection check rechecks paused plan usage");
            Invoke(Find<Button>(page, "AdminChatGptDisconnect-account-1"));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "disconnect confirmation");
            var disconnectDialog = (ContentDialog)Field(page, "_dialog")!;
            Invoke(Descendants(disconnectDialog).OfType<Button>().First(button => button.Content?.ToString() == "Continue")); await Idle(page);
            Check(handler.Writes.Contains("DELETE /api/v2/admin/ai/chatgpt/accounts/account-1"), "account disconnect uses account-specific body-bearing DELETE");
            Invoke(Find<Button>(page, "AdminChatGptReconnect-account-1"));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "reconnect manual flow");
            var reconnectDialog = (ContentDialog)Field(page, "_dialog")!; var reconnectClosed = Closed(reconnectDialog);
            handler.LoginResult = "failed"; await Refresh(page); await reconnectClosed; await Idle(page);
            ReviewCheck(Field(page, "_domainError") is string loginError && loginError.Contains("sign-in failed", StringComparison.Ordinal) && Descendants(page).OfType<TextBlock>().Any(text => text.Text == loginError), "failed sign-in closes the dialog and keeps its explanation visible");
            handler.LoginResult = "idle";
            handler.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Refresh(page); var second = Refresh(page);
            await Until(() => handler.InFlight == 1, "held provider response");
            Check(handler.MaximumInFlight == 1, "domain reads do not overlap");
            handler.ReadGate.SetResult(); handler.ReadGate = null;
            await Task.WhenAll(first, second); await Idle(page);
            Invoke(Find<Button>(page, "AdminAiProviderDelete-openai"));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "delete confirmation");
            var dialog = (ContentDialog)Field(page, "_dialog")!; var closed = Closed(dialog);
            var heldModel = Find<TextBox>(page, "AdminIntegration-ai.chat_model");
            var heldAccounts = Find<ComboBox>(page, "AdminChatGptAccount");
            var before = handler.Writes.Count;
            page.SuspendAdminPage();
            await closed; await Idle(page);
            Check(handler.Writes.Count == before, "retirement during confirmation cannot issue deletion");
            Check(heldModel.Text == "" && heldAccounts.Items.Count == 0, "retirement clears detached private controls, not only their container");
            Check(!Descendants(page).OfType<PasswordBox>().Any(), "retirement clears private editor tree");
            Check(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.Contains("OpenAI", StringComparison.Ordinal)), "retirement clears domain and status");
            await Unload(outer, page); page = null;

            page = await Open(outer, "/admin/settings/ai"); await Idle(page);
            var retiredController = Field(page, "_controller"); authority.Retire();
            await Until(() => Field(page, "_controller") is AdminIntegrationSettingsController reopened && !ReferenceEquals(reopened, retiredController) && Enabled(page, "AdminAiProviderAdd"), "authority change reloads the shown page under the new authority");
            await Idle(page);
            handler.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var demotedRead = Refresh(page); await Until(() => handler.InFlight == 1, "private response held before demotion");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            handler.ReadGate.SetResult(); handler.ReadGate = null; await demotedRead; await Idle(page);
            Check(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.Contains("example.invalid", StringComparison.Ordinal)), "demotion clears private accounts despite delayed response");
            Check(Field(page, "_connection") == null && Field(page, "_controller") == null, "demotion retires account and settings owners");
            await Unload(outer, page); page = null;
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });

            page = await Open(outer, "/admin/recommendations");
            await Until(() => Enabled(page, "AdminRecommendationRun-Embeddings"), "recommendations ready");
            await CaptureBoth(window, outer, "integrations-recommendations");
            Find<Button>(page, "AdminRecommendationRun-Embeddings").Tag = "held";
            Find<Button>(page, "AdminIntegrationsRetry").Tag = "held"; Find<Button>(page, "AdminIntegrationsReloadKeep").Tag = "held";
            await (Task)page.GetType().GetMethod("ReadDomainAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [true])!;
            ReviewCheck(Find<Button>(page, "AdminRecommendationRun-Embeddings") is { IsEnabled: true, Tag: "held" }, "an unchanged background poll keeps the live job controls");
            ReviewCheck(Find<Button>(page, "AdminIntegrationsRetry") is { IsEnabled: true, Tag: "held" } && Find<Button>(page, "AdminIntegrationsReloadKeep") is { IsEnabled: true, Tag: "held" }, "a background poll keeps the live recovery controls");
            Invoke(Find<Button>(page, "AdminRecommendationRun-Embeddings")); await Idle(page);
            Check(handler.Writes.Count(entry => entry.EndsWith("/trigger/embeddings", StringComparison.Ordinal)) == 1, "real recommendation command");
            handler.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var polling = Refresh(page); await Until(() => handler.InFlight == 1, "delayed recommendation poll");
            var readsBeforeSettlement = handler.Reads;
            await Task.Delay(TimeSpan.FromSeconds(6));
            Check(handler.Reads == readsBeforeSettlement && handler.InFlight == 1, "five-second cadence never overlaps an unsettled read");
            handler.ReadGate.SetResult(); handler.ReadGate = null; await polling;
            handler.FailRead = true; await Refresh(page);
            Check(Find<Button>(page, "AdminIntegrationsRetry").IsEnabled, "visible domain retry");
            handler.FailRead = false; Invoke(Find<Button>(page, "AdminIntegrationsRetry")); await Idle(page);
            var controller = (AdminIntegrationSettingsController)Field(page, "_controller")!;
            var nextCron = Find<TextBox>(page, "AdminIntegration-recommendations.taste_profiles_cron"); var modelPicker = Find<ComboBox>(page, "AdminIntegration-recommendations.embedding_model-Models");
            var pickerReady = modelPicker.IsEnabled;
            // Focus is only asserted where the fixture window can take it; enablement is asserted everywhere.
            var focusHeld = nextCron.Focus(FocusState.Keyboard) && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), nextCron);
            handler.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.Set("recommendations.embeddings_cron", "15 3 * * *");
            var commit = (Task)page.GetType().GetMethod("CommitFieldAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, ["recommendations.embeddings_cron"])!;
            await Until(() => controller.State.Saving && !Enabled(page, "AdminRecommendationRun-Embeddings"), "held field commit");
            ReviewCheck(nextCron.IsEnabled && Find<TextBox>(page, "AdminIntegration-recommendations.embeddings_cron").IsEnabled && Find<ComboBox>(page, "AdminIntegration-recommendations.embedding_provider_id").IsEnabled && (!pickerReady || modelPicker.IsEnabled), "a field commit leaves the other Recommendations inputs enabled");
            handler.WriteGate.SetResult(); handler.WriteGate = null; await commit; await Idle(page);
            ReviewCheck(nextCron.IsEnabled && (!focusHeld || ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), nextCron)), "the field focus moved to keeps it through the commit and its follow-up read");
            handler.Conflict = true; controller.Set("recommendations.diversity_lambda", "0.8"); await controller.SaveFieldsAsync(["recommendations.diversity_lambda"]);
            await Until(() => Enabled(page, "AdminIntegrationsReloadKeep"), "conflict recovery UI");
            handler.Conflict = false; Invoke(Find<Button>(page, "AdminIntegrationsReloadKeep")); await Idle(page);
            Check(controller.State.Values["recommendations.diversity_lambda"] == "0.8", "reload preserves draft");
            Invoke(Find<Button>(page, "AdminIntegrationsReloadDiscard")); await Idle(page);
            Check(controller.State.DirtyKeys.Count == 0, "reload discards draft");
            handler.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var late = Refresh(page); await Until(() => handler.InFlight == 1, "held response before unload");
            var old = page; var unloaded = Unloaded(old); outer.Content = null;
            handler.ReadGate.SetResult(); handler.ReadGate = null;
            await unloaded; await late; await Idle(old); page = null;
            Check(!Descendants(old).OfType<TextBlock>().Any(text => text.Text.Contains("records", StringComparison.Ordinal)), "late response cannot repopulate retired page");

            page = await Open(outer, "/admin/settings/compatibility");
            await Until(() => Enabled(page, "AdminJellyfinWebInstall"), "compatibility ready");
            ReviewCheck(!Descendants(page).OfType<TextBox>().Any(input => AutomationProperties.GetAutomationId(input) == "AdminIntegration-jellyfin_compat.web_dir"), "managed active directory has no unsupported override editor");
            ReviewCheck(Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "AdminIntegration-jellyfin_compat.server_id" && text.Text == "synthetic-server-id"), "advanced settings expose the generated read-only server identity");
            await CaptureBoth(window, outer, "integrations-compatibility");
            var version = Find<TextBox>(page, "AdminIntegration-jellyfin_compat.web_version"); version.Text = "10.11.1";
            await Until(() => !Find<Button>(page, "AdminJellyfinWebInstall").IsEnabled, "staged version blocks install");
            Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
            Check(handler.Writes.Contains("PATCH /api/v2/admin/jellyfin-compat/settings"), "compatibility saves through dedicated transport");
            Invoke(Find<Button>(page, "AdminJellyfinWebInstall")); await Idle(page);
            Check(handler.Writes.Contains("POST /api/v2/admin/jellyfin-compat/web/install"), "saved install command reachable");
            Check(Find<ProgressBar>(page, "AdminJellyfinWebProgress").Value == 40, "accepted installer acknowledgement is rendered as in-progress, not completion");
            Check(!Enabled(page, "AdminJellyfinWebInstall"), "stale replica status cannot retire accepted operation or permit replay");
            var webWrites = handler.Writes.Count;
            foreach (var reload in new[] { "AdminIntegrationsReloadKeep", "AdminIntegrationsReloadDiscard" })
            {
                handler.StaleWebRead = true;
                page.GetType().GetField("_webReceiptDeadline", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, DateTimeOffset.UtcNow.AddSeconds(-1));
                Invoke(Find<Button>(page, reload)); await Idle(page);
                ReviewCheck(Field(page, "_acceptedWebOperation") is AdminJellyfinWebStatus { OperationId: "operation-1", OperationRunning: true } && Field(page, "_commandUnknown") is true && !Enabled(page, "AdminJellyfinWebInstall") && !Enabled(page, "AdminJellyfinWebRemove"), reload + " preserves unresolved identity and command gating after unrelated idle read");
                ReviewCheck(handler.Writes.Count == webWrites, reload + " never replays an accepted write");
            }
            handler.WebOperationRunning = false; await Refresh(page);
            Invoke(Find<Button>(page, "AdminIntegrationsReloadKeep")); await Idle(page);
            Check(Enabled(page, "AdminJellyfinWebInstall"), "settled operation re-enables saved-version install");
            handler.FailedWebInstallation = true; await Refresh(page);
            ReviewCheck(Enabled(page, "AdminJellyfinWebRemove"), "failed installation without a version permits cleanup");
            if (Enabled(page, "AdminJellyfinWebRemove"))
            {
                Invoke(Find<Button>(page, "AdminJellyfinWebRemove"));
                await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "failed-install removal confirmation");
                var removalDialog = (ContentDialog)Field(page, "_dialog")!;
                Invoke(Descendants(removalDialog).OfType<Button>().First(button => button.Content?.ToString() == "Continue")); await Idle(page);
                Check(handler.Writes.Count(write => write == "POST /api/v2/admin/jellyfin-compat/web/remove") == 1, "failed-install cleanup sends one confirmed official removal");
            }
            handler.EnvironmentOwnedWeb = true; Invoke(Find<Button>(page, "AdminIntegrationsReloadDiscard")); await Idle(page);
            Check(!Enabled(page, "AdminJellyfinWebToggle"), "environment-owned web toggle is not actionable");
            ReviewCheck(Enabled(page, "AdminJellyfinWebInstall"), "a settled page can start the install whose operation the server then never shows");
            if (Enabled(page, "AdminJellyfinWebInstall"))
            {
                handler.LostWebOperation = true; Invoke(Find<Button>(page, "AdminJellyfinWebInstall")); await Idle(page);
                var lostWrites = handler.Writes.Count;
                ReviewCheck(Field(page, "_acceptedWebOperation") is AdminJellyfinWebStatus { OperationId: "operation-1" } && !Enabled(page, "AdminJellyfinWebReview"), "an accepted operation still inside its wait cannot be given up");
                page.GetType().GetField("_webReceiptDeadline", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, DateTimeOffset.UtcNow.AddSeconds(-1));
                Invoke(Find<Button>(page, "AdminIntegrationsReloadKeep")); await Idle(page);
                ReviewCheck(Field(page, "_commandUnknown") is true && !Enabled(page, "AdminJellyfinWebInstall") && Enabled(page, "AdminJellyfinWebReview"), "an accepted operation the server never shows stays gated after Reload and offers an explicit review");
                if (Enabled(page, "AdminJellyfinWebReview"))
                {
                    Invoke(Find<Button>(page, "AdminJellyfinWebReview")); await Idle(page);
                    ReviewCheck(Field(page, "_acceptedWebOperation") == null && Field(page, "_commandUnknown") is false && Enabled(page, "AdminJellyfinWebInstall"), "the explicit review adopts the server status and lifts the gate");
                    ReviewCheck(handler.Writes.Count == lostWrites, "the explicit review never replays an accepted write");
                }
            }
            Check(ReviewFailures.Count == 0, "all Integrations review regressions: " + string.Join("; ", ReviewFailures));
        }
        finally
        {
            handler.ReadGate?.TrySetResult(); handler.ReadGate = null;
            handler.WriteGate?.TrySetResult(); handler.WriteGate = null;
            if (page != null) { page.SuspendAdminPage(); await Idle(page); }
            if (outer.Content is FrameworkElement content)
            {
                var unloaded = Unloaded(content); outer.Content = null; await unloaded;
            }
            window.Content = previousContent; servicesField.SetValue(null, previous);
        }
    }

    private static void ReviewCheck(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) ReviewFailures.Add(label);
    }
    private static async Task CaptureBoth(Window window, Frame root, string name)
    {
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        await Until(() => root.ActualWidth == 1440, "wide capture size");
        await Capture(root, name + "-wide.png");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(720, 900));
        await Until(() => root.ActualWidth == 720, "compact capture size");
        await Capture(root, name + "-compact.png");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        await Until(() => root.ActualWidth == 1440, "restore wide size");
    }
    private static async Task Capture(FrameworkElement root, string name)
    {
        root.UpdateLayout(); await Task.Delay(350); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
    private static async Task<AdminIntegrationsPage> Open(Frame outer, string href)
    {
        Check(AdminShellPage.TryOpenHref(href)?.Kind == AdminRouteResolutionKind.Route, "registered " + href);
        await Until(() => outer.Content is AdminShellPage, "admin shell");
        var frame = (Frame)Field(outer.Content!, "_frame")!;
        await Until(() => frame.Content is AdminIntegrationsPage { IsLoaded: true }, href);
        return (AdminIntegrationsPage)frame.Content!;
    }
    private static async Task Unload(Frame outer, FrameworkElement page)
    {
        var unloaded = Unloaded(page); Check(outer.Navigate(typeof(Page)), "leave Admin through owning Frame"); await unloaded; await Idle(page);
    }
    private static Task Unloaded(FrameworkElement element)
    {
        if (!element.IsLoaded) return Task.CompletedTask;
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? handler = null;
        handler = (_, _) => { element.Unloaded -= handler; result.TrySetResult(); };
        element.Unloaded += handler; return result.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static Task Closed(ContentDialog dialog)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => result.TrySetResult(); return result.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static Task Idle(object page) => (Task)page.GetType().GetMethod("WhenSettledAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static Task Refresh(object page) => (Task)page.GetType().GetMethod("RefreshDomainAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static Task Poll(object page) => (Task)page.GetType().GetMethod("ReadDomainAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [true])!;
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants(root).OfType<T>().First(element => AutomationProperties.GetAutomationId(element) == id);
    private static bool Enabled(DependencyObject root, string id) => Descendants(root).OfType<Button>().Any(element => AutomationProperties.GetAutomationId(element) == id && element.IsEnabled);
    private static void Invoke(Button button) { Check(button.IsEnabled, "enabled " + AutomationProperties.GetAutomationId(button)); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); }
    private static void Check(bool condition, string label) { Program.Log((condition ? "PASS: " : "FAIL: ") + label); if (!condition) throw new InvalidOperationException(label); }
    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private static async Task Until(Func<bool> condition, string label) { for (var attempt = 0; attempt < 400 && !condition(); attempt++) await Task.Delay(25); if (!condition()) throw new InvalidOperationException("Timed out: " + label); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
