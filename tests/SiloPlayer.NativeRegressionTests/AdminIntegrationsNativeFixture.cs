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
        internal bool Conflict, FailRead, FailProviderAction, EnvironmentOwnedWeb, EnvironmentOwnedApi, StaleWebRead, LostWebOperation, FailedWebInstallation;
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal bool ChatConnected = true, LoginPending, WebOperationRunning, CreatedProvider, UsagePaused;
        /// <summary>The deployment declares the provider "declared" (official #4668), and with <see cref="TakeOverOpenAi"/> the one named "openai" as well; the variable is synthetic and as long as a name can be.</summary>
        internal bool DeclaredProvider, TakeOverOpenAi;
        internal static readonly string DeclaredName = "SILO_FIXTURE_VALUE_" + string.Concat(Enumerable.Repeat("LONG_NAME_", 10)) + "END";
        /// <summary>The recommendations status reports an embedding lock conflict with its catalog key (official #4442); the English text of the server is deliberately different, so the page can be seen to use the catalog.</summary>
        internal string LockKey = "";
        internal string LastBody = "";
        /// <summary>When set, the text model check answers with exactly this JSON (a failed check with its reason key, or an older server's bare message).</summary>
        internal string? ChatCheckAnswer;
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
                LastBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
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
                if (path.EndsWith("/settings/check/ai_chat", StringComparison.Ordinal))
                {
                    UsagePaused = false;
                    if (ChatCheckAnswer != null) return Json(JsonDocument.Parse(ChatCheckAnswer).RootElement.Clone());
                    return Json(new { success = true, message = "Usage checked" });
                }
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
                "/api/v2/admin/settings/sensitive-status" => EnvironmentOwnedApi
                    ? new { configured = Array.Empty<string>(), managed_by_env = new[] { "jellyfin_compat.enabled" }, managed_by_env_vars = new Dictionary<string, string> { ["jellyfin_compat.enabled"] = "SILO_JELLYFIN_ENABLED" } }
                    : EnvironmentOwnedWeb && NamedVariables
                    ? new { configured = Array.Empty<string>(), managed_by_env = new[] { "jellyfin_compat.web_enabled" }, managed_by_env_vars = new Dictionary<string, string> { ["jellyfin_compat.web_enabled"] = "SILO_JELLYFIN_WEB_ENABLED" } }
                    : (object)new { configured = Array.Empty<string>(), managed_by_env = EnvironmentOwnedWeb ? new[] { "jellyfin_compat.web_enabled" } : Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/admin/ai/capabilities" => new { reasoning_levels = new[] { "minimal", "high" }, openai_service_tiers = new[] { "default", "priority", "ultrafast" }, chatgpt_sign_in = true },
                "/api/v2/admin/ai/chatgpt" => ChatStatus(),
                "/api/v2/admin/ai/chatgpt/models" => new { models = new[] { new { id = "gpt-test", name = "Synthetic ChatGPT model" } } },
                "/api/v2/admin/ai/providers" => new { providers = ProviderList(), types = new[] { new { type = "openai", label = "OpenAI", default_base_url = "https://example.invalid/v1", capabilities = new[] { "chat", "embeddings" }, key_required = true } } },
                "/api/v2/admin/ai/providers/openai/models" => new { success = true, models = new[] { new { id = "gpt-test", kind = "chat" }, new { id = "embedding-test", kind = "embeddings" }, new { id = "unsupported-image-model", kind = "" } } },
                "/api/v2/admin/ai/providers/created/models" => new { success = true, models = new[] { new { id = "gpt-test", kind = "chat" } } },
                "/api/v2/admin/recommendations/status" => new { embeddings = new { running = false, count = 10, total = 10 }, taste_profiles = new { running = false, count = 2 }, cowatch = new { running = false, count = 2 }, recommendations = new { running = false, count = 2 }, lock_conflict = LockKey == "" ? "" : LockKey == "unknown" ? "Synthetic conflict text from the server." : "Synthetic English text from the server.", lock_conflict_key = LockKey == "" ? null : LockKey == "unknown" ? "web.admin.settings.fields.managedByEnv" : LockKey,
                    lock_conflict_args = LockKey == "" ? null : new Dictionary<string, string> { ["locked_model"] = "model-a", ["model"] = "model-b", ["env"] = "X" }, stale_sweep = new { pending = 0 } },
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
            object Provider(string id, string name, string? declaredBy = null)
            {
                var item = new Dictionary<string, object?> { ["id"] = id, ["type"] = "openai", ["name"] = name, ["base_url"] = "https://example.invalid/v1", ["has_api_key"] = true, ["capabilities"] = new[] { "chat", "embeddings" }, ["used_by"] = Array.Empty<string>() };
                if (declaredBy != null) item["managed_by_env_var"] = declaredBy;
                return item;
            }
            var list = new List<object> { Provider("openai", "OpenAI", TakeOverOpenAi ? DeclaredName : null) };
            if (CreatedProvider) list.Add(Provider("created", "Created"));
            if (DeclaredProvider) list.Add(Provider("declared", "Declared by the deployment", DeclaredName));
            return list.ToArray();
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
            var textAdvanced = Find<Expander>(page, "AdminIntegrationTextModelAdvanced");
            textAdvanced.IsExpanded = true;
            await Until(() => Descendants(page).OfType<ComboBox>().Any(combo => AutomationProperties.GetAutomationId(combo) == "AdminIntegration-ai.service_tier"), "service tier is in Advanced");
            var tier = Find<ComboBox>(page, "AdminIntegration-ai.service_tier");
            tier.SelectedItem = tier.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "priority");
            textProvider.SelectedItem = textProvider.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "");
            textProvider.SelectedItem = textProvider.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "__chatgpt");
            var draft = (AdminIntegrationSettingsController)Field(page, "_controller")!;
            var tierRow = Find<FrameworkElement>(page, "AdminIntegrationServiceTierRow");
            Check(textAdvanced.IsExpanded && IsVisibleInTree(tierRow) && Descendants(tierRow).OfType<Border>().Any(b => IsVisibleInTree(b) && AutomationProperties.GetName(b) == "Unsaved change"), "ChatGPT keeps the staged service tier in open Advanced with an unsaved mark");
            Check(draft.State.Values["ai.text_provider_id"] == "openai" && !draft.State.DirtyKeys.Contains("ai.text_provider_id"), "ChatGPT restores the saved API-key provider");
            textAdvanced.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .25 });
            await Task.Delay(300);
            await CaptureBoth(window, outer, "integrations-ai-tier-draft");
            draft.Discard();
            await Idle(page);
            await SelectionKeepsControl(page, handler, textProvider, "a text provider");
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
            // Official #5082: the text model check says why it failed. The server's answer names its text (message_key, message_args); an older server sends none.
            {
                string Status() => ((TextBlock)Field(page, "_domainStatus")!).Text;
                async Task Run(string? answer, string label)
                {
                    handler.ChatCheckAnswer = answer;
                    Invoke(Find<Button>(page, "AdminAiTextCheck")); await Idle(page);
                    ReviewCheck(Status().StartsWith("Connection failed. ", StringComparison.Ordinal) || answer == null, label + " (failure shown as a failed connection)");
                }
                foreach (var (reasonKey, english) in new[]
                {
                    ("textAIUnauthorized", "Text AI provider rejected the credentials. Replace the provider's API key, or sign in with ChatGPT again."),
                    ("textAINotFound", "Text AI endpoint or model is unavailable. Check the provider's base URL and the model."),
                    ("textAIBilling", "Text AI provider has no available credit, or a usage or spending limit was reached. Check the provider balance and limits, or ChatGPT usage settings."),
                    ("textAIRateLimited", "Text AI provider is rate limiting requests. Try again later."),
                    ("textAIUnreachable", "Silo could not reach the text AI provider. Check the provider's base URL and that this server can connect to it."),
                    ("textAITimedOut", "Text AI connection check timed out. Check provider availability and try again."),
                    ("textAIPlanIneligible", "The connected ChatGPT account can't share its plan usage. Check ChatGPT usage settings, or connect another account."),
                })
                {
                    await Run("{\"success\":false,\"message\":\"server wording\",\"message_key\":\"web.admin.common.connectionCheck.result." + reasonKey + "\"}", reasonKey);
                    ReviewCheck(Status() == "Connection failed. " + english, "the text model check shows the official reason for " + reasonKey);
                }
                await Run("{\"success\":false,\"message\":\"server wording\",\"message_key\":\"web.admin.common.connectionCheck.result.textAIRejected\",\"message_args\":{\"status\":\"422\"}}", "textAIRejected");
                ReviewCheck(Status() == "Connection failed. Text AI provider rejected the check request (status 422). Check the model, reasoning level, and service tier. The server log has the provider's error.",
                    "a rejected request names its status from the message arguments");
                await Run("{\"success\":false,\"message\":\"Text AI connection check failed: the provider returned an error\"}", "an older server");
                ReviewCheck(Status() == "Connection failed. Text AI connection check failed: the provider returned an error", "an older server that sends no reason keeps its generic message");
                await Run("{\"success\":false,\"message\":\"A newer server reason.\",\"message_key\":\"web.admin.common.connectionCheck.result.somethingNewer\"}", "an unknown key");
                ReviewCheck(Status() == "Connection failed. A newer server reason.", "a reason key this client does not know keeps the server message");
                handler.ChatCheckAnswer = null;
            }
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

            // Official #4816: only a tuning value the save sends is checked, and a number shows what it holds, empty included.
            handler.Values["subtitle_ai.asr_chunk_seconds"] = "700"; handler.Values["subtitle_ai.batch_size"] = ""; handler.Values["ai.auth_mode"] = "api_key";
            page = await Open(outer, "/admin/settings/ai"); await Idle(page);
            {
                var tuningController = (AdminIntegrationSettingsController)Field(page, "_controller")!;
                var chunk = Find<TextBox>(page, "AdminIntegration-subtitle_ai.asr_chunk_seconds");
                var jobs = Find<TextBox>(page, "AdminIntegration-ai.max_concurrent_jobs");
                var batch = Find<TextBox>(page, "AdminIntegration-subtitle_ai.batch_size");
                int Puts() => handler.Writes.Count(write => write == "PUT /api/v2/admin/settings");
                ReviewCheck(chunk.Text == "700" && batch.Text == "" && jobs.Text == "2", "a stored value shows as it is, and a number with nothing saved shows no default");
                // A number with nothing saved: typing then erasing it stages nothing.
                batch.Text = "50"; await Until(() => tuningController.State.DirtyKeys.Contains("subtitle_ai.batch_size"), "tuning number typed");
                batch.Text = ""; await Until(() => tuningController.State.DirtyKeys.Count == 0, "tuning number erased");
                await Idle(page);
                ReviewCheck(batch.Text == "" && tuningController.State.DirtyKeys.Count == 0, "erasing a number typed with nothing saved stages nothing and shows no default of 40");
                // An untouched stored value a client check would refuse (700 seconds) does not stop an unrelated save.
                var onView = Find<ComboBox>(page, "AdminIntegration-metadata_ai.on_view");
                onView.SelectedItem = onView.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "button");
                await Until(() => tuningController.State.DirtyKeys.SequenceEqual(["metadata_ai.on_view"]), "unrelated setting staged");
                var putsBefore = Puts();
                await Until(() => Enabled(page, "AdminIntegrationsSave"), "save offered"); Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
                ReviewCheck(Puts() == putsBefore + 1 && Field(page, "_domainError") is null && tuningController.State.DirtyKeys.Count == 0 && handler.Values["subtitle_ai.asr_chunk_seconds"] == "700",
                    "an untouched out-of-range tuning value does not block an unrelated save and is not sent");
                // A value the save sends is still checked, with the official message.
                chunk.Text = "59"; await Until(() => tuningController.State.DirtyKeys.Contains("subtitle_ai.asr_chunk_seconds"), "chunk edited");
                putsBefore = Puts();
                await Until(() => Enabled(page, "AdminIntegrationsSave"), "save offered"); Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
                ReviewCheck(Puts() == putsBefore && Field(page, "_domainError") is "Transcription chunk length must be between 60 and 600 seconds.", "an edited chunk length outside 60 to 600 is refused with the official message and nothing is sent");
                Invoke(Find<Button>(page, "AdminIntegrationsDiscard")); await Until(() => tuningController.State.DirtyKeys.Count == 0, "chunk discarded");
                await Idle(page); await Task.Delay(150);
                jobs.Text = ""; await Until(() => tuningController.State.DirtyKeys.Contains("ai.max_concurrent_jobs"), "jobs emptied");
                await Idle(page);
                ReviewCheck(jobs.Text == "", "an emptied saved number stays empty and shows no default of 2");
                putsBefore = Puts();
                await Until(() => Enabled(page, "AdminIntegrationsSave"), "save offered"); Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
                ReviewCheck(Puts() == putsBefore && Field(page, "_domainError") is "Max concurrent jobs must be a positive whole number.", "an emptied jobs number is refused with the official message and nothing is sent");
                Invoke(Find<Button>(page, "AdminIntegrationsDiscard")); await Until(() => tuningController.State.DirtyKeys.Count == 0 && jobs.Text == "2", "jobs discarded");
            }
            handler.Values["subtitle_ai.asr_chunk_seconds"] = "600"; handler.Values["subtitle_ai.batch_size"] = "40"; handler.Values["metadata_ai.on_view"] = "off";
            await Unload(outer, page); page = null;

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
            await SelectionKeepsControl(page, handler, Find<ComboBox>(page, "AdminIntegration-recommendations.embedding_provider_id"), "an embedding provider");
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
            var webSetting = Find<ToggleSwitch>(page, "AdminIntegration-jellyfin_compat.web_enabled");
            Check(EnvironmentNoteProbe.Verify(page, webSetting, EnvironmentNoteProbe.Named("SILO_JELLYFIN_WEB_ENABLED")) is null, "the environment-owned web setting is disabled, shows the official note naming the variable and carries it in its help text");
            Check(EnvironmentNoteProbe.VerifyEditable(Find<TextBox>(page, "AdminIntegration-jellyfin_compat.server_name")) is null, "an editable Integrations row carries no environment note");
            handler.NamedVariables = false; Invoke(Find<Button>(page, "AdminIntegrationsReloadDiscard")); await Idle(page);
            Check(EnvironmentNoteProbe.Verify(page, webSetting, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the setting and shows the official field message");
            handler.NamedVariables = true; Invoke(Find<Button>(page, "AdminIntegrationsReloadDiscard")); await Idle(page);
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
            // The environment holds the Jellyfin API switch Off while the web player is saved On. A ToggleSwitch reports its state again after it is realized, also
            // when it is locked: opening the page afresh must stage nothing for either switch (the dependent web-off is the admin's choice, never the lock's).
            handler.EnvironmentOwnedWeb = false; handler.EnvironmentOwnedApi = true;
            handler.Values["jellyfin_compat.enabled"] = "false"; handler.Values["jellyfin_compat.web_enabled"] = "true";
            await Unload(outer, page); page = null;
            page = await Open(outer, "/admin/settings/compatibility"); await Idle(page);
            var lockedApi = Find<ToggleSwitch>(page, "AdminIntegration-jellyfin_compat.enabled");
            var lockedController = (AdminIntegrationSettingsController)Field(page, "_controller")!;
            await Until(() => lockedController.State.ManagedByEnvironment.Contains("jellyfin_compat.enabled") && !lockedApi.IsEnabled && lockedApi.IsLoaded, "the managed API switch is locked and realized");
            // A dispatcher fence, twice, lets any report queued by realization run before the assertion.
            for (var fence = 0; fence < 2; fence++)
            {
                var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lockedApi.DispatcherQueue.TryEnqueue(() => ran.TrySetResult()); await ran.Task.WaitAsync(TimeSpan.FromSeconds(6));
            }
            await Idle(page);
            Check(!lockedApi.IsOn && lockedController.State.DirtyKeys.Count == 0 && lockedController.State.Values["jellyfin_compat.web_enabled"] == "true",
                "a locked API switch stages nothing after realization: no web-off, no draft");
            Check(EnvironmentNoteProbe.Verify(page, lockedApi, EnvironmentNoteProbe.Named("SILO_JELLYFIN_ENABLED")) is null, "the managed API switch is disabled, shows the note naming the variable and carries it in its help text");
            handler.EnvironmentOwnedApi = false;

            // A server from before #4466 sends no value for the same-address key (it applied the rule unconditionally and its own page has no such switch): no row, nothing
            // staged or written, rather than an "Off" that misstates what that server does.
            handler.Values["jellyfin_compat.enabled"] = "true"; handler.Values["jellyfin_compat.web_enabled"] = "true"; handler.Values[AdminIntegrationsSettings.AddressStreamGrant] = "";
            await Unload(outer, page); page = null;
            page = await Open(outer, "/admin/settings/compatibility"); await Idle(page);
            {
                var olderController = (AdminIntegrationSettingsController)Field(page, "_controller")!;
                var olderSwitch = Find<ToggleSwitch>(page, "AdminIntegration-" + AdminIntegrationsSettings.AddressStreamGrant);
                await Until(() => olderController.State.Loaded, "the settings of a server that sends no same-address value are loaded");
                await Task.Delay(200);
                var olderWrites = handler.Writes.Count;
                Check(olderSwitch.Parent is FrameworkElement { Visibility: Visibility.Collapsed } && !Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Play without sign-in from the same address" && IsVisibleInTree(text)),
                    "a server that sends no value for the same-address switch gets no row for it");
                olderSwitch.IsOn = true; await Task.Delay(150);
                Check(olderController.State.DirtyKeys.Count == 0 && handler.Writes.Count == olderWrites, "nothing is staged or written for a same-address switch the server does not have");
            }

            // Server #4466: "Play without sign-in from the same address" opens off, shows the official warning, and only the admin's own action on its switch changes it.
            handler.Values["jellyfin_compat.enabled"] = "true"; handler.Values["jellyfin_compat.web_enabled"] = "true"; handler.Values[AdminIntegrationsSettings.AddressStreamGrant] = "false";
            await Unload(outer, page); page = null;
            page = await Open(outer, "/admin/settings/compatibility"); await Idle(page);
            var settingsController = (AdminIntegrationSettingsController)Field(page, "_controller")!;
            var grantKey = AdminIntegrationsSettings.AddressStreamGrant;
            var grantSwitch = Find<ToggleSwitch>(page, "AdminIntegration-" + grantKey);
            await Until(() => grantSwitch.IsLoaded && grantSwitch.IsEnabled && settingsController.State.Loaded, "the same-address stream switch is realized and editable");
            // A ToggleSwitch reports its state again after it is realized: a dispatcher fence, twice, lets any such report run before the assertions.
            for (var fence = 0; fence < 2; fence++)
            {
                var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                grantSwitch.DispatcherQueue.TryEnqueue(() => ran.TrySetResult()); await ran.Task.WaitAsync(TimeSpan.FromSeconds(6));
            }
            await Idle(page);
            var writesBeforeGrant = handler.Writes.Count;
            Check(!grantSwitch.IsOn && settingsController.State.DirtyKeys.Count == 0 && AutomationProperties.GetName(grantSwitch) == "Play without sign-in from the same address",
                "the same-address stream switch opens off with its official label and its realization stages nothing");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("Some Jellyfin apps, such as Findroid, open direct-play and download links without signing in.", StringComparison.Ordinal)
                    && text.Text.Contains("Anyone who shares that address", StringComparison.Ordinal) && text.Text.EndsWith("Turn it on only if you use one of those apps.", StringComparison.Ordinal)),
                "the official warning is shown with the same-address stream switch");
            await settingsController.ReloadAsync(false); await Idle(page);
            await Task.Delay(150);
            Check(!grantSwitch.IsOn && settingsController.State.DirtyKeys.Count == 0 && handler.Writes.Count == writesBeforeGrant,
                "re-rendering from a reload never stages or writes the same-address stream switch");
            grantSwitch.IsOn = true;
            await Until(() => settingsController.State.DirtyKeys.SequenceEqual([grantKey]), "the admin's own action stages the same-address stream switch");
            await Until(() => Enabled(page, "AdminIntegrationsSave"), "Save enabled for the same-address stream switch");
            Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
            Check(handler.Values[grantKey] == "true" && handler.Writes.Skip(writesBeforeGrant).SequenceEqual(["PUT /api/v2/admin/settings"]),
                "saving the same-address stream switch sends one settings write and no compatibility patch");
            grantSwitch.IsOn = false;
            await Until(() => settingsController.State.DirtyKeys.SequenceEqual([grantKey]), "switching it off again is staged");
            await Until(() => Enabled(page, "AdminIntegrationsSave"), "Save enabled for switching it off again");
            Invoke(Find<Button>(page, "AdminIntegrationsSave")); await Idle(page);
            Check(handler.Values[grantKey] == "false" && !grantSwitch.IsOn && settingsController.State.DirtyKeys.Count == 0, "the same-address stream switch is saved off again");

            // Official #4696: Jellyfin Web's switch-off rides with an API switch-off and is taken back when the API switch flips back.
            var apiSwitch = Find<ToggleSwitch>(page, "AdminIntegration-jellyfin_compat.enabled");
            var webSwitchOnPage = Find<ToggleSwitch>(page, "AdminIntegration-jellyfin_compat.web_enabled");
            await Until(() => apiSwitch.IsOn && apiSwitch.IsEnabled && webSwitchOnPage.IsOn, "API and web player saved on, switch editable");
            apiSwitch.IsOn = false;
            await Until(() => settingsController.State.DirtyKeys.Order().SequenceEqual(["jellyfin_compat.enabled", "jellyfin_compat.web_enabled"]), "an API switch-off stages the web player off with it");
            apiSwitch.IsOn = true;
            await Until(() => settingsController.State.DirtyKeys.Count == 0, "flipping the API switch back takes the web switch-off back");
            await Idle(page);
            Check(apiSwitch.IsOn && webSwitchOnPage.IsOn && settingsController.State.Values["jellyfin_compat.web_enabled"] == "true",
                "after flipping the API switch back nothing is staged and the web player stays on");
            page = await CheckDeclaredProviderAndLockConflictAsync(window, outer, handler, page);
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

    /// <summary>
    /// Official #4668 and #4442. A provider the deployment declares has a read-only panel that names the variable and offers no save or removal, a provider of an admin beside it still edits,
    /// and a provider taken over while its editor is open shows the stored provider with no draft. The embedding lock conflict is shown from the catalog text for its key, with the
    /// English text of the server as the fallback. The note holds at every width.
    /// </summary>
    private static async Task<AdminIntegrationsPage> CheckDeclaredProviderAndLockConflictAsync(Window window, Frame outer, Server handler, AdminIntegrationsPage page)
    {
        handler.DeclaredProvider = true;
        await Unload(outer, page);
        page = await Open(outer, "/admin/settings/ai");
        await Until(() => Enabled(page, "AdminAiProviderEdit-declared"), "declared provider row");
        var expected = AdminText.T("web.admin.settings.fields.managedByEnv", new { env = Server.DeclaredName });
        Check(!Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminAiProviderDelete-declared"), "a declared provider has no remove action");
        Check(Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminAiProviderDelete-openai"), "a provider of an admin beside it keeps its remove action");

        // The panel of the declared provider: read-only, named, no save, the stored provider tested as stored.
        Invoke(Find<Button>(page, "AdminAiProviderEdit-declared"));
        await Until(() => Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "AdminAiProviderEnvironmentNote"), "the note of the declared provider");
        var note = Find<TextBlock>(page, "AdminAiProviderEnvironmentNote");
        Check(note.Text == expected, "the panel of the declared provider names the variable as plain text (" + note.Text.Length + " characters)");
        Check(!Find<TextBox>(page, "AdminAiProviderName").IsEnabled && !Find<TextBox>(page, "AdminAiProviderUrl").IsEnabled && !Find<PasswordBox>(page, "AdminAiProviderKey").IsEnabled, "the fields of a declared provider are read-only");
        Check(Find<TextBox>(page, "AdminAiProviderName").Text == "Declared by the deployment", "the panel shows the stored provider");
        Check(!Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminAiProviderSave") && Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminAiProviderCancel"), "the panel of a declared provider has no Save and can be closed");
        var writes = handler.Writes.Count;
        Invoke(Find<Button>(page, "AdminAiProviderTest")); await Idle(page);
        Check(handler.Writes.Skip(writes).SequenceEqual(["POST /api/v2/admin/ai/providers/check"]) && handler.LastBody.Contains("\"id\":\"declared\"") && !handler.LastBody.Contains("\"api_key\":\""), "testing a declared provider checks the stored provider and keeps its saved key");
        foreach (var width in new[] { 1440, 1024, 800, 560 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); await Task.Delay(500); page.UpdateLayout();
            var bounds = note.TransformToVisual(page).TransformBounds(new Windows.Foundation.Rect(0, 0, note.ActualWidth, note.ActualHeight));
            Check(bounds.Left >= -1 && bounds.Right <= page.ActualWidth + 1 && !note.IsTextTrimmed, $"the note of the declared provider fits the page at {width}px (note {bounds.Left:0}..{bounds.Right:0}, page {page.ActualWidth:0})");
            if (width == 560) Check(note.ActualHeight > note.FontSize * 2, "the long variable name wraps onto more than one line at 560px (" + Math.Round(note.ActualHeight) + "px high)");
            await Capture(outer, "integrations-ai-declared-" + width + ".png");
        }
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Until(() => outer.ActualWidth == 1440, "restore wide size");
        Invoke(Find<Button>(page, "AdminAiProviderCancel")); await Idle(page);

        // The provider of an admin still edits and saves.
        Invoke(Find<Button>(page, "AdminAiProviderEdit-openai"));
        await Until(() => Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminAiProviderSave"), "the panel of the provider of an admin");
        Check(Find<TextBox>(page, "AdminAiProviderName").IsEnabled && !Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "AdminAiProviderEnvironmentNote"), "the panel of the provider of an admin is editable and has no note");
        // The deployment takes it over while the editor holds an edit: the panel is built again from the stored provider, with no draft and no Save.
        Find<TextBox>(page, "AdminAiProviderName").Text = "Typed before the takeover";
        handler.TakeOverOpenAi = true;
        await Refresh(page); await Idle(page);
        await Until(() => Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "AdminAiProviderEnvironmentNote"), "the panel of the taken-over provider is locked");
        Check(Find<TextBox>(page, "AdminAiProviderName").Text == "OpenAI" && !Find<TextBox>(page, "AdminAiProviderName").IsEnabled && !Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminAiProviderSave"),
            "a provider taken over while its editor is open shows the stored provider, read-only, with no draft");
        Invoke(Find<Button>(page, "AdminAiProviderCancel")); await Idle(page);
        handler.TakeOverOpenAi = false; handler.DeclaredProvider = false;

        // The embedding lock conflict is shown from the catalog text for its key; without a usable key the English text of the server stays.
        handler.LockKey = "web.admin.common.connectionCheck.result.embeddingModelMismatch";
        await Unload(outer, page);
        page = await Open(outer, "/admin/recommendations"); await Idle(page);
        const string catalogText = "Embeddings were created with model \"model-a\", not \"model-b\". Reset embeddings before switching models.";
        await Until(() => Descendants(page).OfType<TextBlock>().Any(text => text.Text == catalogText), "the keyed lock conflict");
        Check(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.Contains("Synthetic English text from the server.", StringComparison.Ordinal)), "the page shows the catalog text for the key, not the English text of the server");
        await Capture(outer, "integrations-recommendations-lock-conflict.png");
        handler.LockKey = "unknown";
        await Unload(outer, page);
        page = await Open(outer, "/admin/recommendations"); await Idle(page);
        await Until(() => Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Synthetic conflict text from the server."), "the unkeyed lock conflict");
        Check(true, "a key outside the lock-conflict texts falls back to the English text of the server");
        handler.LockKey = "";
        return page;
    }

    private static bool IsVisibleInTree(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is UIElement ui; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current)) if (ui.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static void ReviewCheck(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) ReviewFailures.Add(label);
    }
    /// <summary>Changes a provider selection, holds the read it starts, then restores the saved selection.</summary>
    private static async Task SelectionKeepsControl(AdminIntegrationsPage page, Server handler, ComboBox combo, string label)
    {
        await Idle(page);
        var saved = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        // Focus is only asserted where the fixture window can take it; enablement is asserted everywhere.
        var focusHeld = combo.Focus(FocusState.Keyboard) && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), combo);
        handler.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "");
        await Until(() => handler.InFlight == 1, "held read after choosing " + label);
        ReviewCheck(combo.IsEnabled, "choosing " + label + " leaves its selector enabled while the read it starts is in flight");
        handler.ReadGate.SetResult(); handler.ReadGate = null; await Idle(page);
        ReviewCheck(combo.IsEnabled && (!focusHeld || ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), combo)), "the selector used to choose " + label + " keeps focus until that read settles");
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == saved); await Idle(page);
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
