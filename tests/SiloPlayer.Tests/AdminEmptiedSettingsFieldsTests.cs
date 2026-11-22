using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Official silo-server #4816 (ca51b57f0, "stop emptied settings fields from staging values they don't show") and #5082 (10e67d836, "say why
/// the text model check failed"), against the native Admin settings. Each test names the official case it ports, or the rule it pins.
/// Official source: web/src/hooks/useSettingsForm.test.ts and web/src/components/settings/SecretField.test.tsx at c90e406db71ee468bffdfa5733cd97d1d50b4019.
/// </summary>
public sealed class AdminEmptiedSettingsFieldsTests
{
    private const string Secret = "email.smtp_password";

    private static AdminSettingsForm Form(string[]? configured, params (string Key, string Value)[] saved)
    {
        var form = new AdminSettingsForm(["branding.server_name", "branding.accent_color", "s3.public_read_endpoint", "s3.public_token_secret", Secret],
            ["s3.public_token_secret", Secret], saved.ToDictionary(entry => entry.Key, entry => entry.Value), "\"v1\"");
        form.ApplyConfigured(configured);
        return form;
    }

    // ---------------------------------------------------------------- useSettingsForm.test.ts

    [Fact] // "guards the next save with the current snapshot after an edit is undone in place"
    public void GuardsTheNextSaveWithTheCurrentSnapshotAfterAnEditIsUndoneInPlace()
    {
        var form = Form([], ("branding.server_name", "Silo"));
        // The Default accent with none saved: nothing to stage.
        form.Set("branding.accent_color", "#ff0000");
        form.Set("branding.accent_color", "");
        Assert.Empty(form.DirtyKeys);
        // Another write (a branding upload) moves the settings on, and the page reads them again.
        form.Reload(new Dictionary<string, string> { ["branding.server_name"] = "Silo", ["branding.logo_ref"] = "logo" }, "\"v2\"", preserveDraft: true);
        form.Set("branding.server_name", "Casa");
        Assert.Equal("\"v2\"", form.CaptureSave().ETag);
    }

    [Fact] // "releases an edit changed back to its saved value, but not a staged secret clear"
    public void ReleasesAnEditChangedBackToItsSavedValueButNotAStagedSecretClear()
    {
        var form = Form([Secret], ("branding.server_name", "Silo"));
        form.Set("branding.server_name", "Casa");
        Assert.Single(form.DirtyKeys);
        form.Set("branding.server_name", "Silo");
        Assert.Empty(form.DirtyKeys);

        // The snapshot leaves out a setting with nothing saved, secret or not, so emptying what was typed into one gets it back to its saved state.
        foreach (var key in new[] { "s3.public_read_endpoint", "s3.public_token_secret" })
        {
            form.Set(key, "typed");
            Assert.Contains(key, form.DirtyKeys);
            form.Set(key, "");
        }
        Assert.Empty(form.DirtyKeys);

        // It leaves out a set secret too, and emptying that is a clear to save.
        form.Set(Secret, "");
        Assert.Equal(new[] { Secret }, form.DirtyKeys);
        Assert.True(form.IsClearStaged(Secret));
        Assert.Equal("", form.CaptureSave().Values[Secret]);
    }

    [Fact]
    public void WhileTheSecretStatusHasNotArrivedAnEmptySecretStaysAClearToSave()
    {
        // Official savedValue: "before the status (configured) has arrived, the saved value is unknown, and an empty draft is a clear to save."
        var form = Form(null);
        form.Set("s3.public_token_secret", "");
        Assert.Equal(new[] { "s3.public_token_secret" }, form.DirtyKeys);
        form.ApplyConfigured([]);
        form.Set("s3.public_token_secret", "typed");
        form.Set("s3.public_token_secret", "");
        Assert.Empty(form.DirtyKeys);
    }

    [Fact]
    public void AnEditMadeWhileASaveIsInFlightStaysStagedEvenWhenItEqualsTheSavedValue()
    {
        // Official "keeps a change back to the old value staged while that save is in flight".
        var form = Form([], ("branding.server_name", "Silo"));
        form.Set("branding.server_name", "First");
        var intent = form.CaptureSave();
        form.Set("branding.server_name", "Silo");
        form.Set("s3.public_read_endpoint", "");
        Assert.Contains("branding.server_name", form.DirtyKeys);
        Assert.Contains("s3.public_read_endpoint", form.DirtyKeys);
        form.EndSave(intent);
    }

    // ---------------------------------------------------------------- SecretField.test.tsx

    [Fact] // "goes back to nothing staged when an input with nothing saved is emptied"
    public void EmptyingASecretWithNothingSavedGoesBackToNothingStaged()
    {
        var form = Form([]);
        form.SetSecret("s3.public_token_secret", "x");
        Assert.Equal(new[] { "s3.public_token_secret" }, form.DirtyKeys);
        form.SetSecret("s3.public_token_secret", "");
        Assert.Empty(form.DirtyKeys);
        Assert.False(form.IsClearStaged("s3.public_token_secret"));
        Assert.Empty(form.CaptureSave().Values);
    }

    [Fact] // "delegates emptying the input to onKeep so the parent's draft stays authoritative"
    public void EmptyingAReplacementTypedOverASavedSecretKeepsTheSavedSecret()
    {
        var form = Form([Secret]);
        form.SetSecret(Secret, "replacement");
        form.SetSecret(Secret, "");
        Assert.Empty(form.DirtyKeys);
        Assert.False(form.IsClearStaged(Secret));
        Assert.Empty(form.CaptureSave().Values);
    }

    [Fact] // "says a staged clear will be saved, and offers to keep the value instead" + "stages a replacement when the admin types over a staged clear"
    public void AStagedClearIsExplicitAndTypingOverItStagesAReplacement()
    {
        var form = Form([Secret]);
        form.Set(Secret, "", stage: true);
        Assert.True(form.IsClearStaged(Secret));
        form.SetSecret(Secret, "replacement");
        Assert.False(form.IsClearStaged(Secret));
        Assert.Equal("replacement", form.CaptureSave().Values[Secret]);
        form.Discard();
        form.Set(Secret, "", stage: true);
        form.Reset(Secret); // "Keep" takes the clear back
        Assert.Empty(form.DirtyKeys);
    }

    [Fact]
    public void SetSecretRefusesASettingThatIsNotASecret()
    {
        Assert.Throws<ArgumentException>(() => Form([]).SetSecret("branding.server_name", "x"));
    }

    [Fact]
    public void AnAcknowledgedSaveUpdatesWhichSecretsAreSet()
    {
        var form = Form([]);
        form.SetSecret(Secret, "first");
        var save = form.CaptureSave();
        form.Acknowledge(save, new Dictionary<string, string>(), "\"v2\"", false);
        Assert.Equal(true, form.IsConfigured(Secret));
        // Now saved: emptying is a clear, not an undo.
        form.Set(Secret, "");
        Assert.True(form.IsClearStaged(Secret));
        var clear = form.CaptureSave();
        form.Acknowledge(clear, new Dictionary<string, string>(), "\"v3\"", false);
        Assert.Equal(false, form.IsConfigured(Secret));
        form.Set(Secret, "typed");
        form.Set(Secret, "");
        Assert.Empty(form.DirtyKeys);
    }

    [Fact]
    public void AFormNeverToldTheStatusKeepsTheEarlierBehaviourForSecrets()
    {
        // Pages whose controllers are not part of this change (their forms never get ApplyConfigured) still stage an emptied secret.
        var form = new AdminSettingsForm(["a", "s"], ["s"], new Dictionary<string, string>(), "\"v1\"");
        Assert.Null(form.IsConfigured("s"));
        form.Set("s", "");
        Assert.Equal(new[] { "s" }, form.DirtyKeys);
    }

    // ---------------------------------------------------------------- controllers over a server that enforces If-Match

    /// <summary>A settings server that refuses a write whose If-Match is not its current version, as the real one does.</summary>
    private sealed class SettingsServer
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Configured { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Secrets { get; init; } = new(StringComparer.Ordinal);
        public int Version { get; set; } = 1;
        public List<string> Puts { get; } = [];
        public int Refusals { get; private set; }
        public string ETag => $"\"v{Version}\"";

        public HttpResponseMessage Handle(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                var response = path.EndsWith("effective") ? AdminMonitorKit.Json(JsonSerializer.Serialize(Values))
                    : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json(JsonSerializer.Serialize(new { configured = Configured.ToArray(), managed_by_env = Array.Empty<string>() }))
                    : path.EndsWith("server-channels") ? AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}""")
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new(ETag);
                return response;
            }
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (request.Headers.IfMatch.ToString() != ETag) { Refusals++; return AdminMonitorKit.Json("""{"type":"precondition_failed","status":412}""", HttpStatusCode.PreconditionFailed); }
            Puts.Add(body);
            var returned = new Dictionary<string, string>();
            using (var document = JsonDocument.Parse(body))
                foreach (var entry in document.RootElement.GetProperty("values").EnumerateObject())
                {
                    var value = entry.Value.GetString() ?? "";
                    if (Secrets.Contains(entry.Name)) { if (value.Length == 0) Configured.Remove(entry.Name); else Configured.Add(entry.Name); }
                    else { Values[entry.Name] = value; returned[entry.Name] = value; }
                }
            Version++;
            var saved = AdminMonitorKit.Json(JsonSerializer.Serialize(new { values = returned, restart_required = false }));
            saved.Headers.ETag = new(ETag);
            return saved;
        }
    }

    private sealed class EmailRig : IDisposable
    {
        public readonly SettingsServer Server = new() { Secrets = { Secret } };
        private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
        private readonly AdminMonitorAuthority _authority;
        private readonly AdminServerSettingsStore _store;
        public readonly AdminEmailSettingsController Controller;
        public EmailRig(bool passwordSaved)
        {
            Server.Values["email.smtp_host"] = "smtp.example.test";
            Server.Values["email.from_name"] = "Silo";
            if (passwordSaved) Server.Configured.Add(Secret);
            _authority = new(_kit.Client, _kit.Auth);
            var api = new AdminServerSettingsApi(_kit.Client);
            _store = new(api, _authority, AdminEmailSettingsController.Keys, AdminEmailSettingsController.Secrets);
            Controller = new(api, _store, _authority);
            _kit.Http.Respond = (request, _) => Task.FromResult(Server.Handle(request));
        }
        public void Dispose() { Controller.Dispose(); _store.Dispose(); _authority.Dispose(); _kit.Dispose(); }
    }

    [Fact]
    public async Task ATypedThenErasedPasswordWithNothingSavedStagesNothingAndSendsNothing()
    {
        using var rig = new EmailRig(passwordSaved: false);
        await rig.Controller.OpenAsync();
        rig.Controller.SetPassword("typed");
        Assert.Contains(Secret, rig.Controller.State.DirtyKeys);
        rig.Controller.SetPassword("");
        Assert.Empty(rig.Controller.State.DirtyKeys);
        Assert.False(rig.Controller.State.DirtyKeys.Contains(Secret));
        await rig.Controller.SaveAsync();
        Assert.Empty(rig.Server.Puts);
    }

    [Fact]
    public async Task ErasingAReplacementTypedOverASavedPasswordKeepsTheSavedPassword()
    {
        using var rig = new EmailRig(passwordSaved: true);
        await rig.Controller.OpenAsync();
        rig.Controller.SetPassword("replacement");
        rig.Controller.SetPassword("");
        Assert.Empty(rig.Controller.State.DirtyKeys);
        await rig.Controller.SaveAsync();
        Assert.Empty(rig.Server.Puts);
        Assert.True(rig.Controller.State.PasswordConfigured);
        Assert.Contains(Secret, rig.Server.Configured);
    }

    [Fact]
    public async Task OnlyTheExplicitClearOfASetPasswordIsStagedAndSentAsAnEmptyValue()
    {
        using var rig = new EmailRig(passwordSaved: true);
        await rig.Controller.OpenAsync();
        rig.Controller.ClearPassword();
        Assert.Equal(new[] { Secret }, rig.Controller.State.DirtyKeys);
        await rig.Controller.SaveAsync();
        Assert.Equal("""{"values":{"email.smtp_password":""}}""", rig.Server.Puts.Single());
        Assert.False(rig.Controller.State.PasswordConfigured);
        // With nothing saved now, typing then erasing is an undo again.
        rig.Controller.SetPassword("typed");
        rig.Controller.SetPassword("");
        Assert.Empty(rig.Controller.State.DirtyKeys);
    }

    [Fact] // rule 6: the save baseline follows the snapshot, also after an edit is undone in place
    public async Task AnEditUndoneInPlaceDoesNotLeaveTheNextSaveGuardedByAnOldSnapshot()
    {
        using var rig = new EmailRig(passwordSaved: false);
        await rig.Controller.OpenAsync();
        rig.Controller.SetPassword("typed");
        rig.Controller.SetPassword("");
        Assert.Empty(rig.Controller.State.DirtyKeys);
        // Another admin saves; this page reads the settings again, then the admin edits something else.
        rig.Server.Values["email.from_name"] = "Elsewhere";
        rig.Server.Version++;
        await rig.Controller.ReloadAsync(preserveDraft: true);
        rig.Controller.Set("email.smtp_host", "smtp2.example.test");
        await rig.Controller.SaveAsync();
        Assert.Equal(0, rig.Server.Refusals);
        Assert.False(rig.Controller.State.Conflict);
        Assert.Equal("""{"values":{"email.smtp_host":"smtp2.example.test"}}""", rig.Server.Puts.Single());
    }

    [Fact]
    public async Task ASaveOverASettingsChangeThePageNeverReadIsRefusedAsAConflict()
    {
        using var rig = new EmailRig(passwordSaved: false);
        await rig.Controller.OpenAsync();
        rig.Server.Version++;
        rig.Controller.Set("email.smtp_host", "smtp2.example.test");
        await rig.Controller.SaveAsync();
        Assert.Equal(1, rig.Server.Refusals);
        Assert.True(rig.Controller.State.Conflict);
        Assert.Contains("email.smtp_host", rig.Controller.State.DirtyKeys);
    }

    // ---------------------------------------------------------------- General: the Redis URL and the save outcome

    private sealed class GeneralRig : IDisposable
    {
        public readonly SettingsServer Server = new() { Secrets = { "redis.url" } };
        private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
        private readonly AdminMonitorAuthority _authority;
        private readonly AdminServerSettingsStore _store;
        public readonly AdminGeneralSettingsController Controller;
        public GeneralRig(bool urlSaved)
        {
            Server.Values["branding.server_name"] = "Silo";
            Server.Values["redis.db"] = "2";
            if (urlSaved) Server.Configured.Add("redis.url");
            _authority = new(_kit.Client, _kit.Auth);
            var api = new AdminServerSettingsApi(_kit.Client);
            _store = new(api, _authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
            Controller = new(api, _store, _authority);
            _kit.Http.Respond = (request, _) => Task.FromResult(Server.Handle(request));
        }
        public void Dispose() { Controller.Dispose(); _store.Dispose(); _authority.Dispose(); _kit.Dispose(); }
    }

    [Fact]
    public async Task ATypedThenErasedRedisUrlWithNothingSavedLeavesNothingStaged()
    {
        using var rig = new GeneralRig(urlSaved: false);
        await rig.Controller.OpenAsync();
        rig.Controller.StageRedisUrl("redis://synthetic.invalid:6379");
        Assert.Contains("redis.url", rig.Controller.State.DirtyKeys);
        rig.Controller.KeepSavedRedisUrl(); // what emptying the URL box does
        Assert.Empty(rig.Controller.State.DirtyKeys);
        Assert.False(await rig.Controller.SaveAsync());
        Assert.Empty(rig.Server.Puts);
    }

    [Fact]
    public async Task EmptyingTheUrlWhileSwitchingRedisOffWithNothingSavedStagesNothing()
    {
        using var rig = new GeneralRig(urlSaved: false);
        await rig.Controller.OpenAsync();
        rig.Controller.StageRedisUrl("");
        Assert.DoesNotContain("redis.url", rig.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task SwitchingRedisOffWithAUrlSavedStagesTheClearAndTheShownNumber()
    {
        using var rig = new GeneralRig(urlSaved: true);
        await rig.Controller.OpenAsync();
        rig.Controller.DisableRedis();
        Assert.Equal(new[] { "redis.db", "redis.url" }, rig.Controller.State.DirtyKeys.Order());
        Assert.True(await rig.Controller.SaveAsync());
        Assert.Equal("""{"values":{"redis.url":"","redis.db":"2"}}""", rig.Server.Puts.Single());
    }

    [Fact] // the page ends the Use Redis switch's own choice only on a save the server acknowledged
    public async Task SaveReportsWhetherTheServerAcknowledgedTheWrite()
    {
        using var rig = new GeneralRig(urlSaved: false);
        await rig.Controller.OpenAsync();
        Assert.False(await rig.Controller.SaveAsync()); // nothing staged
        rig.Controller.Set("branding.server_name", "Casa");
        rig.Server.Version++; // changed elsewhere since this page read it
        Assert.False(await rig.Controller.SaveAsync());
        Assert.True(rig.Controller.State.Conflict);
        await rig.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(await rig.Controller.SaveAsync());
        Assert.Empty(rig.Controller.State.DirtyKeys);
    }

    // ---------------------------------------------------------------- Notifications: no default is shown for an emptied number

    [Theory]
    [InlineData("notifications.email.digest_hour")]
    [InlineData("notifications.discord.digest_hour")]
    [InlineData("notifications.webhooks.max_per_profile")]
    [InlineData("notifications.webhooks.deliveries_per_minute_per_profile")]
    [InlineData("notifications.server_channels.batch_seconds")]
    public void AnEmptyNumberShowsNothingInsteadOfADefault(string key)
    {
        Assert.Equal("", AdminNotificationSettingsController.Display(key, ""));
        Assert.Equal("", AdminNotificationSettingsController.Display(key, null));
        Assert.Equal("9", AdminNotificationSettingsController.Display(key, "9"));
    }

    [Theory]
    [InlineData("notifications.discord.poster_mode", "provider")]
    [InlineData("notifications.webhooks_enabled", "false")]
    [InlineData("notifications.ui_enabled", "true")]
    public void ASwitchOrAChoiceStillShowsItsDefault(string key, string expected) =>
        Assert.Equal(expected, AdminNotificationSettingsController.Display(key, ""));

    [Fact]
    public async Task AnEmptiedNumberWithNothingSavedLeavesNothingStaged()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new SettingsServer { Secrets = { "discord.bot_token", "discord.client_secret" } };
        kit.Http.Respond = (request, _) => Task.FromResult(server.Handle(request));
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminNotificationSettingsController.Keys, AdminNotificationSettingsController.Secrets);
        using var controller = new AdminNotificationSettingsController(api, store, new AdminNotificationSettingsApi(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("notifications.email.digest_hour", "9");
        Assert.Contains("notifications.email.digest_hour", controller.State.DirtyKeys);
        controller.Set("notifications.email.digest_hour", "");
        Assert.Empty(controller.State.DirtyKeys);
        controller.Set("discord.bot_token", "typed");
        Assert.Contains("discord.bot_token", controller.State.DirtyKeys);
        controller.Set("discord.bot_token", "");
        Assert.Empty(controller.State.DirtyKeys);
        await controller.SaveAsync();
        Assert.Empty(server.Puts);
    }

    // ---------------------------------------------------------------- AI Services: only a value the save sends is checked

    private static AdminIntegrationSettingsState AiState(IReadOnlyDictionary<string, string> values, params string[] dirty) => new()
    {
        Loaded = true, Values = values, PersistedValues = values, DirtyKeys = dirty,
    };

    [Fact]
    public void AnUntouchedTuningFieldIsNotValidated()
    {
        // Stored values a client check would refuse stay as they are while the admin changes something else.
        var values = new Dictionary<string, string>
        {
            ["metadata_ai.on_view"] = "button", ["subtitle_ai.asr_chunk_seconds"] = "700", ["ai.max_concurrent_jobs"] = "", ["subtitle_ai.batch_size"] = "0",
        };
        Assert.Null(AdminIntegrationPolicy.ValidateAi(AiState(values, "metadata_ai.on_view"), [], null, null, []));
    }

    [Theory]
    [InlineData("ai.max_concurrent_jobs", "", "Max concurrent jobs must be a positive whole number.")]
    [InlineData("ai.max_concurrent_jobs", "0", "Max concurrent jobs must be a positive whole number.")]
    [InlineData("subtitle_ai.batch_size", "abc", "Subtitle batch size must be a positive whole number.")]
    [InlineData("subtitle_ai.context_neighbors", "-1", "Subtitle context lines must be zero or a positive whole number.")]
    [InlineData("subtitle_ai.asr_chunk_seconds", "59", "Transcription chunk length must be between 60 and 600 seconds.")]
    [InlineData("subtitle_ai.asr_chunk_seconds", "601", "Transcription chunk length must be between 60 and 600 seconds.")]
    [InlineData("subtitle_ai.transcribe_quota_jobs", "", "Transcription limit must be zero or a positive whole number.")]
    public void ATuningValueTheSaveSendsIsCheckedWithTheOfficialMessage(string key, string value, string message)
    {
        var values = new Dictionary<string, string> { [key] = value };
        Assert.Equal(message, AdminIntegrationPolicy.ValidateAi(AiState(values, key), [], null, null, []));
    }

    [Theory]
    [InlineData("ai.max_concurrent_jobs", "1")]
    [InlineData("subtitle_ai.batch_size", "40")]
    [InlineData("subtitle_ai.context_neighbors", "0")]
    [InlineData("subtitle_ai.asr_chunk_seconds", "60")]
    [InlineData("subtitle_ai.asr_chunk_seconds", "600")]
    [InlineData("subtitle_ai.transcribe_quota_jobs", "0")]
    public void AValidTuningValueTheSaveSendsPasses(string key, string value)
    {
        var values = new Dictionary<string, string> { [key] = value };
        Assert.Null(AdminIntegrationPolicy.ValidateAi(AiState(values, key), [], null, null, []));
    }

    // ---------------------------------------------------------------- the text model check says why it failed (#5082)

    private static async Task<AdminSettingsConnectionResult> Check(string kind, string answer)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(answer));
        var keys = kind == "ai_chat" ? AdminIntegrationsSettings.TextKeys : AdminIntegrationsSettings.SpeechKeys;
        return await new AdminIntegrationsApi(kit.Client).CheckModelAsync(lease, kind, keys.ToDictionary(key => key, _ => ""), []);
    }

    private static string Answer(string key, string message, string? args = null) =>
        "{\"success\":false,\"message\":" + JsonSerializer.Serialize(message) + ",\"message_key\":" + JsonSerializer.Serialize(key) + (args == null ? "" : ",\"message_args\":" + args) + "}";

    [Theory] // The official English of each reason, web/src/locales/en.json at the pin.
    [InlineData("textAIBilling", "Text AI provider has no available credit, or a usage or spending limit was reached. Check the provider balance and limits, or ChatGPT usage settings.")]
    [InlineData("textAIForbidden", "Text AI access was denied. Check that the API key or ChatGPT account may use this model.")]
    [InlineData("textAINotFound", "Text AI endpoint or model is unavailable. Check the provider's base URL and the model.")]
    [InlineData("textAIPlanIneligible", "The connected ChatGPT account can't share its plan usage. Check ChatGPT usage settings, or connect another account.")]
    [InlineData("textAIRateLimited", "Text AI provider is rate limiting requests. Try again later.")]
    [InlineData("textAIRefused", "Text AI provider refused the check request. Check the model, the reasoning level, and the account's usage settings. The server log has the provider's error.")]
    [InlineData("textAITimedOut", "Text AI connection check timed out. Check provider availability and try again.")]
    [InlineData("textAIUnauthorized", "Text AI provider rejected the credentials. Replace the provider's API key, or sign in with ChatGPT again.")]
    [InlineData("textAIUnavailable", "Text AI provider is temporarily unavailable. Try again later.")]
    [InlineData("textAIUnreachable", "Silo could not reach the text AI provider. Check the provider's base URL and that this server can connect to it.")]
    [InlineData("textAIUnusableResponse", "Text model returned a response Silo cannot use. Choose another model.")]
    [InlineData("textAIFailed", "Text AI connection failed. Check the model, reasoning level, service tier, account connection, and provider usage limits.")]
    [InlineData("chatGPTSignInRequired", "Sign in with ChatGPT in AI Services settings.")]
    public async Task TheTextModelCheckShowsTheOfficialReasonForItsMessageKey(string key, string english)
    {
        var result = await Check("ai_chat", Answer("web.admin.common.connectionCheck.result." + key, "server wording that is not shown"));
        Assert.False(result.Success);
        Assert.Equal(english, result.Message);
    }

    [Fact]
    public async Task ARejectedRequestNamesItsStatusFromTheMessageArguments()
    {
        var result = await Check("ai_chat", Answer("web.admin.common.connectionCheck.result.textAIRejected", "server wording", """{"status":"400"}"""));
        Assert.Equal("Text AI provider rejected the check request (status 400). Check the model, reasoning level, and service tier. The server log has the provider's error.", result.Message);
    }

    [Fact]
    public async Task AReasonWhoseArgumentsAreMissingKeepsTheServerMessage()
    {
        var result = await Check("ai_chat", Answer("web.admin.common.connectionCheck.result.textAIRejected", "Text AI provider rejected the check request (status 400)."));
        Assert.Equal("Text AI provider rejected the check request (status 400).", result.Message);
    }

    [Fact]
    public async Task AnOlderServerThatSendsNoReasonKeyKeepsItsGenericMessage()
    {
        var result = await Check("ai_chat", """{"success":false,"message":"Text AI connection check failed: provider returned an error"}""");
        Assert.False(result.Success);
        Assert.Equal("Text AI connection check failed: provider returned an error", result.Message);
    }

    [Fact]
    public async Task AKeyThisClientDoesNotKnowKeepsTheServerMessage()
    {
        var result = await Check("ai_chat", Answer("web.admin.common.connectionCheck.result.somethingNewer", "A newer server reason."));
        Assert.Equal("A newer server reason.", result.Message);
    }

    [Fact]
    public async Task TheSpeechCheckShowsItsOfficialReasonsThroughTheSamePath()
    {
        var result = await Check("ai_transcription", Answer("web.admin.common.connectionCheck.result.speechUnauthorized", "server wording"));
        Assert.Equal("Speech-to-text authentication failed. Replace the provider's API key in AI Services.", result.Message);
    }

    [Fact]
    public async Task AReasonIsStillRedactedLikeAnyOtherCheckMessage()
    {
        var result = await Check("ai_chat", """{"success":false,"message":"failed at https://example.invalid/v1?key=synthetic-secret-value in the log"}""");
        Assert.DoesNotContain("synthetic-secret-value", result.Message);
    }

    [Fact]
    public void EveryCatalogKeyIsInTheOfficialNamespaceAndTheLookupAgreesWithIt()
    {
        Assert.Equal(34, AdminConnectionCheckText.Keys.Count);
        foreach (var key in AdminConnectionCheckText.Keys)
        {
            Assert.StartsWith("web.admin.common.connectionCheck.result.", key);
            Assert.True(AdminConnectionCheckText.Has(key));
        }
        Assert.False(AdminConnectionCheckText.Has("web.admin.common.connectionCheck.result.nope"));
        Assert.False(AdminConnectionCheckText.Has(null));
    }
}
