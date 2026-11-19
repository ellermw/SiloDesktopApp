using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Synthetic contracts pinned to official 217ff25; authored before production, runtime unobserved.</summary>
public sealed class AdminNotificationSettingsTests
{
    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminNotificationSettingsController Controller;
        internal string Effective = """{"notifications.ui_enabled":"true","notifications.email.digest_hour":"8","discord.client_id":"app","discord.bot_token":"must-not-retain","email.enabled":"true","email.smtp_host":"smtp.example.test","email.from_address":"silo@example.test"}""";
        internal string Sensitive = """{"configured":["discord.client_secret","discord.bot_token"],"managed_by_env":[]}""";
        internal string Channels = """{"items":[],"page":{"has_more":false}}""";
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Read;
        internal Harness()
        {
            Authority = new(Kit.Client, Kit.Auth);
            var settings = new AdminServerSettingsApi(Kit.Client);
            Store = new(settings, Authority, AdminNotificationSettingsController.Keys, AdminNotificationSettingsController.Secrets);
            Controller = new(settings, Store, new AdminNotificationSettingsApi(Kit.Client), Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Write?.Invoke(request) ?? Task.FromResult(Saved("{}"));
                if (Read != null) return Read(request);
                var path = request.RequestUri!.AbsolutePath;
                return Task.FromResult(Reply(path.EndsWith("effective") ? Effective : path.EndsWith("sensitive-status") ? Sensitive :
                    path.EndsWith("server-channels") ? Channels : """{"keys":[],"prefixes":[]}"""));
            };
        }
        internal string[] Writes => Kit.Http.Requests.Where(r => !r.StartsWith("GET")).ToArray();
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }
    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(body, status); response.Headers.ETag = new("\"one\""); return response;
    }
    private static HttpResponseMessage Saved(string body)
    {
        var response = Reply($$$"""{"values":{{{body}}},"restart_required":false}"""); response.Headers.ETag = new("\"two\""); return response;
    }
    private const string Channel = """{"id":"c1","name":"Community","type":"generic","url_host":"example.test","enabled":true,"notify_new_movies":true,"notify_new_episodes":true,"notify_new_audiobooks":true,"notify_new_ebooks":true,"notify_request_submitted":false,"notify_request_approved":false,"notify_request_declined":false,"notify_request_fulfilled":false,"consecutive_failures":2,"disabled_reason":null,"last_success_at":null,"last_failure_at":"2026-11-17T12:00:00Z","last_failure_status":503,"last_failure_message":"Unavailable"}""";

    [Fact]
    public async Task LoadIsProjectedAndCredentialsAreWriteOnly()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.Loaded);
        Assert.Equal("", h.Controller.State.Values["discord.bot_token"]);
        Assert.DoesNotContain("discord.bot_token", h.Controller.State.PersistedValues.Keys);
        Assert.Equal(3, AdminNotificationSettingsController.Secrets.Count);
        Assert.Contains("notifications.push_relay_api_key", AdminNotificationSettingsController.Secrets);
        Assert.Equal(4, h.Kit.Http.Requests.Count);
        Assert.True(h.Controller.State.MailReady);
        Assert.True(h.Controller.State.DiscordReady);
        Assert.Throws<ArgumentException>(() => h.Controller.Set("email.smtp_host", "other"));
        Assert.Throws<ArgumentException>(() => h.Controller.Set("notifications.push_relay_url", "https://other.test"));
    }

    [Fact]
    public async Task SharedSaveExcludesDiscordAndRelayWhileDiscordSaveExcludesPageDraft()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Controller.Set("notifications.ui_enabled", "false");
        h.Controller.Set("discord.client_id", "new-app"); h.Controller.Set("discord.bot_token", "synthetic-token");
        h.Controller.SetRelayUrl("https://relay.example.test");
        string? tag = null;
        h.Write = r => { tag = r.Headers.IfMatch.Single().ToString(); return Task.FromResult(Saved("{}")); };
        await h.Controller.SaveAsync();
        Assert.Equal("\"one\"", tag);
        using (var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last()))
            Assert.Equal(["notifications.ui_enabled"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        Assert.Contains("discord.bot_token", h.Controller.State.DirtyKeys);
        await h.Controller.SaveDiscordAsync();
        Assert.Equal("\"two\"", tag);
        Assert.Equal("", h.Controller.State.Values["discord.bot_token"]);
        Assert.True(h.Controller.State.RelayUrlDirty);
        Assert.Equal(2, h.Writes.Length);
    }

    [Theory]
    [InlineData(412, true, false)]
    [InlineData(503, false, true)]
    public async Task SettingsConflictOrUncertainSaveBlocksReplayAndPreservesDraft(int status, bool conflict, bool unknown)
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Controller.Set("notifications.ui_enabled", "false");
        h.Write = _ => Task.FromResult(Reply("""{"type":"precondition_failed","title":"Failed","status":412}""", (HttpStatusCode)status));
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.Single(h.Writes); Assert.Equal(conflict, h.Controller.State.Conflict); Assert.Equal(unknown, h.Controller.State.OutcomeUnknown);
        await h.Controller.ReloadAsync(true);
        Assert.False(h.Controller.State.Conflict); Assert.False(h.Controller.State.OutcomeUnknown);
        Assert.Equal("false", h.Controller.State.Values["notifications.ui_enabled"]);
    }

    [Fact]
    public async Task LateEditSurvivesAcknowledgementAndSecretTypingFreezesDuringSave()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set("notifications.email.digest_hour", "9");
        var save = h.Controller.SaveAsync();
        h.Controller.Set("notifications.email.digest_hour", "10"); h.Controller.Set("discord.bot_token", "synthetic-late");
        pending.SetResult(Saved("""{"notifications.email.digest_hour":"9"}""")); await save;
        Assert.Equal("10", h.Controller.State.Values["notifications.email.digest_hour"]);
        Assert.Equal("9", h.Controller.State.PersistedValues["notifications.email.digest_hour"]);
        Assert.Equal("", h.Controller.State.Values["discord.bot_token"]);
    }

    [Fact]
    public async Task ManagedAndUnavailableSensitiveStatusProtectAllCredentialChanges()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":["discord.client_id","notifications.ui_enabled"]}""" };
        await h.Controller.OpenAsync(); h.Controller.Set("notifications.ui_enabled", "false"); h.Controller.Set("discord.client_id", "blocked");
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Read = r => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("sensitive-status") ? Reply("{}", HttpStatusCode.ServiceUnavailable) :
            Reply(r.RequestUri.AbsolutePath.EndsWith("effective") ? h.Effective : r.RequestUri.AbsolutePath.EndsWith("server-channels") ? h.Channels : """{"keys":[],"prefixes":[]}"""));
        await h.Controller.ReloadAsync(true);
        h.Controller.Set("discord.bot_token", "synthetic-blocked"); await h.Controller.ClearDiscordAsync();
        Assert.True(h.Controller.State.SensitiveStatusError); Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task DiscordTestRequiresSavedCompleteCredentialsAndIgnoresDoubleActivation()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Controller.Set("discord.client_id", "draft"); await h.Controller.TestDiscordAsync(); Assert.Empty(h.Writes);
        h.Controller.Discard();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        var test = h.Controller.TestDiscordAsync(); await h.Controller.TestDiscordAsync();
        Assert.Equal(["POST /api/v2/admin/notifications/discord/test"], h.Writes);
        pending.SetResult(Reply("""{"ok":false,"duration_ms":12,"message":"https://example.test/?token=synthetic-token"}""")); await test;
        Assert.False(h.Controller.State.TestResult!.Ok);
        Assert.DoesNotContain("synthetic-token", h.Controller.State.TestResult.Message ?? "");
    }

    [Fact]
    public async Task RelayOperationUsesEndpointOnlyAndRetainsNewerUrlTyping()
    {
        using var h = new Harness(); await h.Controller.OpenAsync(); h.Controller.SetRelayUrl(" https://relay.example.test ");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task; var op = h.Controller.RegisterRelayAsync();
        h.Controller.SetRelayUrl("https://next.example.test");
        Assert.Equal(["POST /api/v2/admin/notifications/push/relay/register"], h.Writes);
        using (var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last())) Assert.Equal("https://relay.example.test", body.RootElement.GetProperty("relay_url").GetString());
        pending.SetResult(Reply("""{"relay_url":"https://relay.example.test","api_key_configured":true,"deployment_id":"deployment","key_prefix":"prefix","expires_at":"2027-01-01T00:00:00Z"}""")); await op;
        Assert.Equal("https://next.example.test", h.Controller.State.RelayUrl);
        Assert.True(h.Controller.State.RelayUrlDirty);
    }

    [Fact]
    public async Task ChannelReadRefreshesAuthenticationOnceWithoutLosingAuthority()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        var refreshes = 0;
        kit.Client.SetTokenRefresher(_ =>
        {
            refreshes++;
            kit.Client.SetAccessToken("synthetic-refreshed");
            return Task.FromResult(true);
        });
        kit.Http.Respond = (request, _) => Task.FromResult(request.Headers.Authorization?.Parameter == "synthetic-refreshed"
            ? Reply("""{"items":[],"page":{"has_more":false}}""")
            : Reply("{}", HttpStatusCode.Unauthorized));

        var page = await new AdminNotificationSettingsApi(kit.Client).GetChannelsAsync(owner);

        Assert.Empty(page.Items);
        Assert.True(owner.IsCurrent);
        Assert.Equal(1, refreshes);
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task NextDiscordTestClearsEarlierResultBeforeAwaitingAndAfterFailure()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Reply("""{"ok":true,"duration_ms":1}"""));
        await h.Controller.TestDiscordAsync();
        Assert.True(h.Controller.State.DiscordTestResult!.Ok);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;

        var operation = h.Controller.TestDiscordAsync();
        try { Assert.Null(h.Controller.State.DiscordTestResult); }
        finally { pending.TrySetResult(Reply("{}", HttpStatusCode.ServiceUnavailable)); await operation; }
        Assert.NotNull(h.Controller.State.Error);
        Assert.Null(h.Controller.State.DiscordTestResult);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NextRelayOperationClearsEarlierReceiptBeforeAwaitingAndAfterFailure(bool clear)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.SetRelayUrl("https://relay.example.test");
        h.Write = _ => Task.FromResult(Reply("""{"relay_url":"https://relay.example.test","api_key_configured":true,"deployment_id":"deployment","key_prefix":"prefix","expires_at":"2027-01-01T00:00:00Z"}"""));
        await h.Controller.RegisterRelayAsync();
        Assert.NotNull(h.Controller.State.RelayRegistration);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;

        var operation = clear ? h.Controller.ClearRelayAsync() : h.Controller.RegisterRelayAsync();
        try { Assert.Null(h.Controller.State.RelayRegistration); }
        finally { pending.TrySetResult(Reply("{}", HttpStatusCode.ServiceUnavailable)); await operation; }
        Assert.NotNull(h.Controller.State.Error);
        Assert.Null(h.Controller.State.RelayRegistration);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(429)] [InlineData(503)]
    public async Task ChannelCommandIsNeverRetriedEvenOnAuthOrTransportFailure(int status)
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":false}}""" };
        await h.Controller.OpenAsync(); h.Write = _ => Task.FromResult(Reply("{}", (HttpStatusCode)status));
        var refreshes = 0; h.Kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        await h.Controller.TestChannelAsync("c1");
        Assert.Equal(["POST /api/v2/admin/notifications/server-channels/c1/test"], h.Writes);
        Assert.NotNull(h.Controller.State.Error);
        Assert.Equal(0, refreshes);
        if (status == 503) { await h.Controller.TestChannelAsync("c1"); Assert.Single(h.Writes); Assert.True(h.Controller.State.OperationUnknown); }
    }

    [Fact]
    public async Task ChannelCreateEditToggleRotateDeleteAndOneTimeSecretFollowExactRoutes()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":false}}""" }; await h.Controller.OpenAsync();
        h.Write = r => Task.FromResult(r.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) :
            Reply(r.RequestUri!.AbsolutePath.EndsWith("rotate-secret") ? """{"signing_secret":"synthetic-once"}""" : Channel[..^1] + ",\"signing_secret\":\"synthetic-once\"}"));
        await h.Controller.SaveChannelAsync(null, new() { ["name"] = "New", ["url"] = "https://example.test/hook" });
        Assert.Equal("synthetic-once", h.Controller.TakeSigningSecret()); Assert.Null(h.Controller.TakeSigningSecret());
        await h.Controller.SaveChannelAsync("c1", new() { ["name"] = "Changed" });
        await h.Controller.SaveChannelAsync("c1", new() { ["enabled"] = false });
        await h.Controller.RotateChannelAsync("c1"); Assert.Equal("synthetic-once", h.Controller.TakeSigningSecret());
        await h.Controller.DeleteChannelAsync("c1");
        Assert.Equal(["POST /api/v2/admin/notifications/server-channels", "PUT /api/v2/admin/notifications/server-channels/c1", "PUT /api/v2/admin/notifications/server-channels/c1", "POST /api/v2/admin/notifications/server-channels/c1/rotate-secret", "DELETE /api/v2/admin/notifications/server-channels/c1"], h.Writes);
        Assert.True(h.Controller.State.Channels.Single().Failing);
    }

    [Fact]
    public async Task PaginationIsExplicitBoundedAndRepeatedCursorCannotLoop()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":true,"next_cursor":"next"}}""" }; await h.Controller.OpenAsync();
        Assert.Single(h.Kit.Http.Requests.Where(r => r.Contains("server-channels")));
        await h.Controller.LoadMoreChannelsAsync();
        Assert.NotNull(h.Controller.State.ChannelsError);
        Assert.Single(h.Controller.State.Channels);
        Assert.Equal(2, h.Kit.Http.Requests.Count(r => r.Contains("server-channels")));
    }

    [Fact]
    public async Task RetirementCancelsLateOperationsAndClearsSecretsDraftsAndResults()
    {
        using var h = new Harness(); await h.Controller.OpenAsync(); h.Controller.Set("discord.bot_token", "synthetic-private");
        h.Controller.SetRelayUrl("https://draft.test");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        var op = h.Controller.RegisterRelayAsync(); h.Controller.Suspend();
        pending.SetResult(Reply("""{"deployment_id":"late"}""")); await op;
        Assert.Empty(h.Controller.State.Values); Assert.Empty(h.Controller.State.Channels);
        Assert.Null(h.Controller.TakeSigningSecret()); Assert.Null(h.Controller.State.RelayRegistration);
        await h.Controller.DeleteChannelAsync("c1"); Assert.Single(h.Writes);
    }

    [Fact]
    public async Task PageDiscardKeepsIndependentSetupDraftsAndAllDefaultsRemainDisplayOnly()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Controller.Set("notifications.email.digest_hour", "8");
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.Set("notifications.fanout.settle_seconds", "30"); Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.Set("notifications.ui_enabled", "false"); h.Controller.Set("discord.bot_token", "synthetic-draft"); h.Controller.SetRelayUrl("https://draft.test");
        h.Controller.DiscardPage();
        Assert.Equal(["discord.bot_token"], h.Controller.State.DirtyKeys);
        Assert.True(h.Controller.State.RelayUrlDirty); h.Controller.Discard(); Assert.False(h.Controller.State.Dirty);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ok\":true}")]
    [InlineData("{\"ok\":true,\"duration_ms\":-1}")]
    public async Task MalformedTestAcknowledgementRequiresReviewBeforeAnotherSend(string response)
    {
        using var h = new Harness(); await h.Controller.OpenAsync(); h.Write = _ => Task.FromResult(Reply(response));
        await h.Controller.TestDiscordAsync(); await h.Controller.TestDiscordAsync();
        Assert.Single(h.Writes); Assert.True(h.Controller.State.OperationUnknown);
        await h.Controller.ReloadAsync(true); Assert.False(h.Controller.State.OperationUnknown);
    }

    [Fact]
    public async Task RelayCannotReportSuccessWithoutItsCurrentSchemaCredentialReceipt()
    {
        using var h = new Harness(); await h.Controller.OpenAsync(); h.Write = _ => Task.FromResult(Reply("""{"deployment_id":"incomplete"}"""));
        await h.Controller.RegisterRelayAsync(); await h.Controller.RegisterRelayAsync();
        Assert.Single(h.Writes); Assert.True(h.Controller.State.OperationUnknown); Assert.Null(h.Controller.State.RelayRegistration);
    }

    [Fact]
    public async Task ClearRelayUsesDeleteAndNoSettingsWriteAndRequiresReconciliationAfterLostAnswer()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Write = _ => throw new HttpRequestException("synthetic transport loss");
        await h.Controller.ClearRelayAsync(); await h.Controller.ClearRelayAsync();
        Assert.Equal(["DELETE /api/v2/admin/notifications/push/relay"], h.Writes);
        Assert.True(h.Controller.State.OperationUnknown);
    }

    [Fact]
    public async Task ClearDiscordSendsExactCredentialSubsetAndNeverSubmitsThePageDraft()
    {
        using var h = new Harness(); await h.Controller.OpenAsync(); h.Controller.Set("notifications.ui_enabled", "false");
        await h.Controller.ClearDiscordAsync();
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last());
        var values = body.RootElement.GetProperty("values"); Assert.Equal(3, values.EnumerateObject().Count());
        Assert.All(values.EnumerateObject(), p => Assert.Equal("", p.Value.GetString()));
        Assert.Equal(["notifications.ui_enabled"], h.Controller.State.DirtyKeys); Assert.False(h.Controller.State.DiscordReady);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(503)]
    public async Task FailedDiscordClearIsNotCarriedIntoALaterDiscordSave(int status)
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Reply("""{"type":"precondition_failed","title":"Failed","status":412}""", (HttpStatusCode)status));
        await h.Controller.ClearDiscordAsync();
        Assert.NotNull(h.Controller.State.Error); Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("app", h.Controller.State.Values["discord.client_id"]); Assert.True(h.Controller.State.DiscordReady);
        await h.Controller.ReloadAsync(true);
        h.Controller.Set("discord.bot_token", "synthetic-new"); h.Write = null; await h.Controller.SaveDiscordAsync();
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last());
        Assert.Equal(["discord.bot_token"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, h.Writes.Length);
    }

    [Fact]
    public async Task ChannelSaveReportsARefusalSoNothingUnsentCanLookSaved()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":false}}""" }; await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Reply(Channel));
        Assert.False(await h.Controller.SaveChannelAsync("retired", new() { ["name"] = "Changed" }));
        Assert.Empty(h.Writes); Assert.Null(h.Controller.State.Error);
        Assert.True(await h.Controller.SaveChannelAsync("c1", new() { ["name"] = "Changed" }));
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task MissingSettingsValidatorDoesNotRenderAnEditableGuessedSnapshot()
    {
        using var h = new Harness(); h.Read = r => Task.FromResult(AdminMonitorKit.Json(r.RequestUri!.AbsolutePath.EndsWith("effective") ? h.Effective :
            r.RequestUri.AbsolutePath.EndsWith("sensitive-status") ? h.Sensitive : r.RequestUri.AbsolutePath.EndsWith("server-channels") ? h.Channels : """{"keys":[],"prefixes":[]}"""));
        await h.Controller.OpenAsync(); h.Controller.Set("notifications.ui_enabled", "false"); await h.Controller.SaveAsync();
        Assert.False(h.Controller.State.Loaded); Assert.Empty(h.Controller.State.Values); Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task FailedChannelReadNeverEnablesCommandsAgainstRetainedRows()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":false}}""" }; await h.Controller.OpenAsync();
        h.Channels = "{}"; await h.Controller.ReloadAsync(true); await h.Controller.DeleteChannelAsync("c1");
        Assert.NotNull(h.Controller.State.ChannelsError); Assert.False(h.Controller.State.ChannelsLoaded); Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task EnvironmentOwnedRelayCannotBeRegisteredOrCleared()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":["notifications.push_relay_api_key"]}""" };
        await h.Controller.OpenAsync(); await h.Controller.RegisterRelayAsync(); await h.Controller.ClearRelayAsync(); Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task SavedChannelTestResultsSurviveTestingAnotherChannelAndDisappearOnRetirement()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}},{{{Channel.Replace("c1", "c2")}}}],"page":{"has_more":false}}""" };
        await h.Controller.OpenAsync(); h.Write = _ => Task.FromResult(Reply("""{"ok":true,"duration_ms":3}"""));
        await h.Controller.TestChannelAsync("c1"); await h.Controller.TestChannelAsync("c2");
        Assert.Equal(2, h.Controller.State.ChannelTestResults.Count); h.Kit.SetRole("user");
        Assert.Empty(h.Controller.State.ChannelTestResults); Assert.Empty(h.Controller.State.Values);
    }
    [Fact]
    public void OfficialNestedStatusMessagesAndPluralDestinationsRenderWithoutUnexpandedArguments()
    {
        Assert.Equal("1 destination", AdminNotificationText.T("web.admin.settings.notifications.serverChannelsCard.destinations", ("count", 1), ("count_text", 1)));
        Assert.Equal("2 destinations", AdminNotificationText.T("web.admin.settings.notifications.serverChannelsCard.destinations", ("count", 2), ("count_text", 2)));
        Assert.Equal("Test failed (HTTP 503, 9ms)", AdminNotificationText.T("web.admin.settings.notifications.serverChannels.testResult", ("outcome", "failed"), ("status", "503"), ("duration_ms", 9)));
        Assert.Equal("Test succeeded (HTTP 200, 123ms) — Delivered", AdminNotificationText.T("web.admin.settings.notifications.serverChannels.testResultWithMessage", ("outcome", "ok"), ("status", "200"), ("duration_ms", 123), ("message", "Delivered")));
        Assert.Equal("Test failed (9ms)", AdminNotificationText.T("web.admin.settings.notifications.serverChannels.testResult", ("outcome", "failed"), ("status", "none"), ("duration_ms", 9)));
        Assert.Equal("Success (7ms)", AdminNotificationText.T("web.admin.settings.notifications.discord.testResult", ("outcome", "ok"), ("duration_ms", 7)));
    }

    [Fact]
    public async Task DestinationCapStopsFurtherReadsAndKeepsAnExplicitContinuation()
    {
        using var h = new Harness(); var page = 0;
        h.Read = r =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("server-channels")) return Task.FromResult(Reply(r.RequestUri.AbsolutePath.EndsWith("effective") ? h.Effective : r.RequestUri.AbsolutePath.EndsWith("sensitive-status") ? h.Sensitive : """{"keys":[],"prefixes":[]}"""));
            var rows = string.Join(",", Enumerable.Range(page * 100, 100).Select(i => Channel.Replace("c1", "c" + i)));
            page++;
            return Task.FromResult(Reply($$$"""{"items":[{{{rows}}}],"page":{"has_more":true,"next_cursor":"page-{{{page}}}"}}"""));
        };
        await h.Controller.OpenAsync();
        for (var attempt = 0; attempt < 12; attempt++) await h.Controller.LoadMoreChannelsAsync();
        Assert.Equal(10, page); Assert.Equal(1000, h.Controller.State.Channels.Count); Assert.NotNull(h.Controller.State.NextCursor);
    }

    [Fact]
    public async Task LateChannelReadCannotRestoreRowsAfterRetirement()
    {
        using var h = new Harness();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Read = r => r.RequestUri!.AbsolutePath.EndsWith("server-channels") ? pending.Task : Task.FromResult(Reply(r.RequestUri.AbsolutePath.EndsWith("effective") ? h.Effective : r.RequestUri.AbsolutePath.EndsWith("sensitive-status") ? h.Sensitive : """{"keys":[],"prefixes":[]}"""));
        var open = h.Controller.OpenAsync(); h.Controller.Suspend();
        pending.SetResult(Reply($$$"""{"items":[{{{Channel}}}],"page":{"has_more":true,"next_cursor":"late"}}""")); await open;
        Assert.Empty(h.Controller.State.Channels); Assert.Empty(h.Controller.State.Values); Assert.Null(h.Controller.State.NextCursor);
    }

    [Fact]
    public async Task ChannelUpdateWithAnotherIdentityIsUnknownAndCannotBeResent()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":false}}""" }; await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Reply("""{"id":"another"}"""));
        await h.Controller.SaveChannelAsync("c1", new() { ["enabled"] = false });
        await h.Controller.SaveChannelAsync("c1", new() { ["enabled"] = false });
        Assert.Single(h.Writes); Assert.True(h.Controller.State.OperationUnknown);
    }

    [Fact]
    public async Task DeleteProblemBodiesAreBoundedAndMissingRelayRouteIsAnError()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Reply("{}", HttpStatusCode.NotFound));
        await h.Controller.ClearRelayAsync(); Assert.NotNull(h.Controller.State.Error);
        h.Write = _ => Task.FromResult(Reply(new string('x', 512 * 1024 + 1), HttpStatusCode.ServiceUnavailable));
        await h.Controller.ClearRelayAsync(); Assert.True(h.Controller.State.OperationUnknown);
        await h.Controller.ClearRelayAsync(); Assert.Equal(2, h.Writes.Length);
    }

    [Fact]
    public async Task WriteRetiredBeforeItsAnswerBlocksTheFreshOwnerUntilExplicitReload()
    {
        using var h = new Harness { Channels = $$$"""{"items":[{{{Channel}}}],"page":{"has_more":false}}""" }; await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        var op = h.Controller.RotateChannelAsync("c1");
        var write = AdminNotificationSettingsController.InterruptedWrite(h.Controller.State);
        h.Controller.Dispose(); pending.SetResult(Reply("""{"signing_secret":"synthetic-lost"}""")); await op;
        Assert.Equal("channel-rotate", write);
        using var fresh = new AdminNotificationSettingsController(new AdminServerSettingsApi(h.Kit.Client), h.Store, new AdminNotificationSettingsApi(h.Kit.Client), h.Authority);
        await fresh.OpenAsync(); fresh.RestoreInterrupted(write, false);
        Assert.True(fresh.State.OperationUnknown); Assert.Contains("signing secret", fresh.State.Error ?? "");
        Assert.Null(fresh.TakeSigningSecret());
        await fresh.RotateChannelAsync("c1"); Assert.Single(h.Writes);
        await fresh.ReloadAsync(true);
        Assert.False(fresh.State.OperationUnknown); Assert.Null(fresh.State.Error);
    }

    [Fact]
    public async Task OnlyServerChangingWritesAreCarriedAndClearedCredentialsAreReportedWithoutBlocking()
    {
        Assert.Null(AdminNotificationSettingsController.InterruptedWrite(new() { OperationPending = true, ActiveOperation = "channel-test" }));
        Assert.Null(AdminNotificationSettingsController.InterruptedWrite(new() { OperationPending = true, ActiveOperation = "discord-test" }));
        Assert.Null(AdminNotificationSettingsController.InterruptedWrite(new()));
        Assert.Equal("relay-register", AdminNotificationSettingsController.InterruptedWrite(new() { OperationPending = true, ActiveOperation = "relay-register" }));
        Assert.Equal("save", AdminNotificationSettingsController.InterruptedWrite(new() { Saving = true }));
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Controller.RestoreInterrupted(null, true);
        Assert.NotNull(h.Controller.State.Error); Assert.False(h.Controller.State.OperationUnknown); Assert.False(h.Controller.State.OutcomeUnknown);
        h.Controller.RestoreInterrupted("save", false);
        Assert.True(h.Controller.State.OutcomeUnknown); Assert.False(h.Controller.State.OperationUnknown);
        h.Controller.Set("notifications.ui_enabled", "false"); await h.Controller.SaveAsync(); Assert.Empty(h.Writes);
        await h.Controller.ReloadAsync(true);
        Assert.False(h.Controller.State.OutcomeUnknown); Assert.Equal("false", h.Controller.State.Values["notifications.ui_enabled"]);
    }
}
