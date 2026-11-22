using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Email settings (official EmailSettings.tsx / EmailTestRow.tsx / lib/email.ts at c11b0a96) on synthetic data.</summary>
public sealed class AdminEmailSettingsControllerTests
{
    private const string Password = AdminEmailSettingsController.Password;
    private const string Security = AdminEmailSettingsController.Security;

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsApi Api;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminEmailSettingsController Controller;
        internal string Effective = """{"email.enabled":"true","email.from_address":"silo@example.com","email.from_name":"Silo","email.smtp_host":"smtp.example.com","email.smtp_port":"587","email.smtp_security":"","email.smtp_username":"mailer","branding.server_name":"Other page"}""";
        internal string Sensitive = """{"configured":["email.smtp_password"],"managed_by_env":[]}""";
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Api = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(Api, Authority, AdminEmailSettingsController.Keys, AdminEmailSettingsController.Secrets);
            Controller = new AdminEmailSettingsController(Api, Store, Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Write?.Invoke(request) ?? Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
                var path = request.RequestUri!.AbsolutePath;
                var response = path.EndsWith("effective") ? AdminMonitorKit.Json(Effective)
                    : path.EndsWith("sensitive-status") ? (Sensitive == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Sensitive))
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new("\"one\"");
                return Task.FromResult(response);
            };
        }
        internal string[] Writes => Kit.Http.Requests.Where(r => !r.StartsWith("GET")).ToArray();
        internal string LastBody => Kit.Http.Bodies.Last();
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static HttpResponseMessage Saved(string values, string etag = "\"two\"")
    {
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":false}""");
        response.Headers.ETag = new(etag);
        return response;
    }

    [Fact]
    public void ScopeIsExactlyTheEightOfficialKeysWithOnePasswordSecret()
    {
        Assert.Equal(["email.enabled", "email.from_address", "email.from_name", "email.smtp_host", "email.smtp_port", "email.smtp_security", "email.smtp_username", "email.smtp_password"],
            AdminEmailSettingsController.Keys);
        Assert.Equal([Password], AdminEmailSettingsController.Secrets);
    }

    [Fact]
    public async Task LoadProjectsOnlyEmailKeysAndSaveSendsOneStrongIfMatchWriteOfDirtyKeys()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var state = h.Controller.State;
        Assert.True(state.Loaded && state.PasswordConfigured);
        Assert.Equal(8, state.Values.Count);
        Assert.DoesNotContain("branding.server_name", state.Values.Keys);
        Assert.Equal("", state.Values[Password]);
        string? ifMatch = null;
        h.Write = request => { ifMatch = request.Headers.IfMatch.Single().ToString(); return Task.FromResult(Saved("""{"email.from_name":"Silo Mail"}""")); };
        h.Controller.Set("email.from_name", "Silo Mail");
        await h.Controller.SaveAsync();
        Assert.Equal("\"one\"", ifMatch);
        Assert.Equal(["PUT /api/v2/admin/settings"], h.Writes);
        using (var body = JsonDocument.Parse(h.LastBody))
            Assert.Equal(["email.from_name"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("Silo Mail", h.Controller.State.PersistedValues["email.from_name"]);
    }

    [Fact]
    public async Task EmptySavedSecurityDisplaysStarttlsWithoutStagingAnEdit()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        Assert.Equal("starttls", AdminEmailSettingsController.DisplaySecurity(h.Controller.State.Values[Security]));
        h.Controller.Set(Security, "starttls");
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.Set(Security, "tls");
        Assert.Equal([Security], h.Controller.State.DirtyKeys);
        h.Controller.Set(Security, "starttls");
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values[Security]);
    }

    [Fact]
    public async Task SavedPasswordReplaceEraseToKeepExplicitClearKeepAndDiscard()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.SetPassword("synthetic-pass");
        Assert.Equal([Password], h.Controller.State.DirtyKeys);
        h.Controller.SetPassword("");
        Assert.Empty(h.Controller.State.DirtyKeys); // erasing a replacement keeps the saved secret
        h.Controller.ClearPassword();
        Assert.Equal([Password], h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values[Password]);
        h.Controller.KeepPassword();
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.ClearPassword(); h.Controller.Discard();
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task WithoutASavedPasswordClearIsUnavailableAndErasingATypedPasswordLeavesNothingStaged()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":[]}""" };
        await h.Controller.OpenAsync();
        h.Controller.ClearPassword();
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.SetPassword("synthetic-pass");
        h.Controller.SetPassword("");
        // Official #4816: an emptied secret goes back to what is saved, and with nothing saved that leaves nothing staged (it was a staged empty value before).
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AcknowledgedReplacementAndClearUpdatePresenceAndNeverRetainTheSecret()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":[]}""" };
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Saved("{}"));
        h.Controller.SetPassword("synthetic-pass");
        await h.Controller.SaveAsync();
        Assert.Contains("\"email.smtp_password\":\"synthetic-pass\"", h.LastBody);
        Assert.True(h.Controller.State.PasswordConfigured);
        Assert.Equal("", h.Controller.State.Values[Password]);
        Assert.DoesNotContain(Password, h.Controller.State.PersistedValues.Keys);
        h.Controller.ClearPassword();
        await h.Controller.SaveAsync();
        Assert.Contains("\"email.smtp_password\":\"\"", h.LastBody);
        Assert.False(h.Controller.State.PasswordConfigured);
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task CredentialEditsFreezeDuringSaveWhileLateNonsecretEditsStayDirtyOverCanonicalValues()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.SetPassword("first-pass");
        h.Controller.Set("email.from_name", "First");
        var save = h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Saving);
        h.Controller.SetPassword("late-pass");
        h.Controller.ClearPassword();
        Assert.Equal("first-pass", h.Controller.State.Values[Password]);
        h.Controller.Set("email.from_name", "Later");
        pending.SetResult(Saved("""{"email.from_name":"Canonical first"}"""));
        await save;
        Assert.Equal("Later", h.Controller.State.Values["email.from_name"]);
        Assert.Equal("Canonical first", h.Controller.State.PersistedValues["email.from_name"]);
        Assert.Equal(["email.from_name"], h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values[Password]);
    }

    [Fact]
    public async Task FailedPasswordStatusIsRetryableAndNeverTreatedAsNoSavedPassword()
    {
        using var h = new Harness { Sensitive = "fail" };
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.SensitiveStatusError);
        h.Controller.SetPassword("x"); h.Controller.ClearPassword();
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Write = _ => Task.FromResult(Saved("""{"email.smtp_host":"mail.example.com"}"""));
        h.Controller.Set("email.smtp_host", "mail.example.com");
        await h.Controller.SaveAsync();
        Assert.DoesNotContain(Password, h.LastBody);
        h.Sensitive = """{"configured":["email.smtp_password"],"managed_by_env":[]}""";
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.SensitiveStatusError);
        Assert.True(h.Controller.State.PasswordConfigured);
        h.Controller.ClearPassword();
        Assert.Equal([Password], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task EnvironmentManagedKeysCannotBeEditedOrWritten()
    {
        using var h = new Harness { Sensitive = """{"configured":["email.smtp_password"],"managed_by_env":["email.smtp_password","redis.url"]}""" };
        await h.Controller.OpenAsync();
        Assert.Equal([Password], h.Controller.State.ManagedByEnv);
        h.Controller.SetPassword("x"); h.Controller.ClearPassword();
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ConflictRetainsDraftBlocksSaveAndExplicitReloadReconciles()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"type":"precondition_failed","status":412}""", HttpStatusCode.PreconditionFailed));
        h.Controller.Set("email.smtp_port", "2525");
        await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Conflict);
        await h.Controller.SaveAsync();
        Assert.Single(h.Writes);
        Assert.Equal("2525", h.Controller.State.Values["email.smtp_port"]);
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.Conflict);
        Assert.Equal(["email.smtp_port"], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task UnknownSaveOutcomeIsNeverReplayed()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromException<HttpResponseMessage>(new HttpRequestException());
        h.Controller.Set("email.from_address", "mail@example.com");
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.OutcomeUnknown);
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task RetiredAuthorityIgnoresLateSaveAndTestReplies()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set("email.from_name", "Changed");
        var save = h.Controller.SaveAsync();
        h.Authority.Retire();
        pending.SetResult(Saved("{}"));
        await save;
        Assert.Empty(h.Controller.State.Values);
        Assert.False(h.Controller.State.TestPending);
        Assert.Null(h.Controller.State.TestResult);
    }

    [Fact]
    public async Task TestSendsOnlyTheTrimmedRecipientEvenWhileSmtpDraftExists()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set("email.smtp_host", "staged.example.com");
        HttpRequestMessage? sent = null;
        h.Write = request => { sent = request; return Task.FromResult(AdminMonitorKit.Json("""{"ok":true,"duration_ms":7}""")); };
        await h.Controller.SendTestAsync("  admin@example.test \t");
        Assert.Equal(["POST /api/v2/admin/email/test"], h.Writes);
        Assert.Equal("""{"to":"admin@example.test"}""", h.LastBody);
        Assert.Empty(sent!.Headers.IfMatch);
        Assert.Equal(new AdminEmailTestFeedback(AdminEmailTestOutcome.Delivered, 7), h.Controller.State.TestResult);
        Assert.Equal("Delivered to the mail server in 7ms.", AdminText.T("web.admin.common.emailTest.delivered", new { duration_ms = 7L }));
        Assert.Equal(["email.smtp_host"], h.Controller.State.DirtyKeys); // the draft is untouched
    }

    [Fact]
    public async Task InvalidOrEmptyRecipientsSendNothing()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        await h.Controller.SendTestAsync("   ");
        Assert.Null(h.Controller.State.TestResult);
        await h.Controller.SendTestAsync("admin@siloserver");
        Assert.Equal(AdminEmailTestOutcome.InvalidRecipient, h.Controller.State.TestResult!.Outcome);
        Assert.Empty(h.Writes);
        Assert.Equal("Enter a valid email address, like name@example.com", AdminText.T("web.auth.invalidEmail"));
    }

    [Fact]
    public async Task PendingTestSuppressesDuplicateCommands()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        var first = h.Controller.SendTestAsync("admin@example.test");
        Assert.True(h.Controller.State.TestPending);
        await h.Controller.SendTestAsync("other@example.test");
        pending.SetResult(AdminMonitorKit.Json("""{"ok":true,"duration_ms":12}"""));
        await first;
        Assert.Single(h.Writes);
        Assert.False(h.Controller.State.TestPending);
    }

    [Theory]
    [InlineData("""{"ok":false,"duration_ms":30,"message":"535 authentication failed"}""", "535 authentication failed")]
    [InlineData("""{"ok":false,"duration_ms":30}""", null)]
    public async Task RejectedTestUsesServerMessageOrOfficialFallback(string json, string? message)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json(json));
        await h.Controller.SendTestAsync("admin@example.test");
        Assert.Equal(AdminEmailTestOutcome.Rejected, h.Controller.State.TestResult!.Outcome);
        Assert.Equal(message, h.Controller.State.TestResult.Message);
        Assert.Equal("Test failed.", AdminText.T("web.admin.common.emailTest.failed"));
    }

    [Theory]
    [InlineData("""{"ok":true}""", HttpStatusCode.OK)]
    [InlineData("""{"duration_ms":4}""", HttpStatusCode.OK)]
    [InlineData("""{"ok":"yes","duration_ms":4}""", HttpStatusCode.OK)]
    [InlineData("""{"ok":true,"duration_ms":-1}""", HttpStatusCode.OK)]
    [InlineData("not json", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.GatewayTimeout)]
    [InlineData("{}", HttpStatusCode.RequestTimeout)]
    public async Task MalformedOrAmbiguousAcknowledgementIsAnUnknownOutcomeAndNeverResent(string json, HttpStatusCode status)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json(json, status));
        await h.Controller.SendTestAsync("admin@example.test");
        Assert.Equal(AdminEmailTestOutcome.Unknown, h.Controller.State.TestResult!.Outcome);
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task TransportFailureIsUnknownAndAuthenticationIsNotReplayed()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic"));
        await h.Controller.SendTestAsync("admin@example.test");
        Assert.Equal(AdminEmailTestOutcome.Unknown, h.Controller.State.TestResult!.Outcome);
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"type":"authentication_required","status":401}""", HttpStatusCode.Unauthorized));
        await h.Controller.SendTestAsync("admin@example.test");
        Assert.Equal(AdminEmailTestOutcome.RequestFailed, h.Controller.State.TestResult!.Outcome);
        Assert.Equal(2, h.Writes.Length);
        Assert.DoesNotContain(h.Kit.Http.Requests, r => r.Contains("/auth/"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "permission_denied", AdminFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", AdminFailureKind.Server)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "validation_failed", AdminFailureKind.Validation)]
    public async Task ServerRefusalsAreReportedWithoutRawDetail(HttpStatusCode status, string type, AdminFailureKind kind)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json($$"""{"type":"{{type}}","title":"secret-host.internal detail","status":{{(int)status}}}""", status));
        await h.Controller.SendTestAsync("admin@example.test");
        var result = h.Controller.State.TestResult!;
        Assert.Equal(AdminEmailTestOutcome.RequestFailed, result.Outcome);
        Assert.Equal(kind, result.Failure);
        Assert.Null(result.Message);
        Assert.Single(h.Writes);
    }

    [Theory]
    [InlineData("admin@example.test", true)]
    [InlineData("  admin@example.test  ", true)]
    [InlineData("a@b.c", true)]
    [InlineData("first.last+tag@sub.example.co", true)]
    [InlineData("admin@[192.168.1.20]", true)]
    [InlineData("admin@[::1]", true)]
    [InlineData("admin@[2001:db8::1]", true)]
    [InlineData("admin@siloserver", false)]
    [InlineData("@example.com", false)]
    [InlineData("admin@@example.com", false)]
    [InlineData("a@b@example.com", false)]
    [InlineData("ad min@example.com", false)]
    [InlineData("admin@.com", false)]
    [InlineData("admin@example.", false)]
    [InlineData("admin@[256.1.1.1]", false)]
    [InlineData("admin@[IPv6:::1]", false)]
    [InlineData("admin@[:]", false)]
    [InlineData("admin@[192.168.1.20", false)]
    [InlineData("admin@[example.com]", false)]
    [InlineData("admin@[١٢.1.1.1]", false)]
    [InlineData("", false)]
    public void MailboxValidationFollowsOfficialRule(string value, bool valid) => Assert.Equal(valid, AdminEmailAddress.IsValid(value));

    private const string HostAndPasswordManaged = """{"configured":["email.smtp_password"],"managed_by_env":["email.smtp_host","email.smtp_password"],"managed_by_env_vars":{"email.smtp_host":"SILO_SMTP_HOST","email.smtp_password":"SILO_SMTP_PASSWORD"}}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatFindsKeysManagedDropsTheirDraftsTheTypedPasswordIncludedAndAFailedStatusReadAfterwardsKeepsThemLockedAndOutOfTheSave(bool statusThenFails)
    {
        using var h = new Harness();
        h.Write = _ => Task.FromResult(Saved("""{"email.from_name":"Silo Mail"}"""));
        await h.Controller.OpenAsync();
        h.Controller.SetPassword("typed-synthetic-password");
        h.Controller.Set("email.smtp_host", "mail.draft.invalid");
        h.Controller.Set("email.from_name", "Silo Mail");
        Assert.Equal(3, h.Controller.State.DirtyKeys.Count);

        h.Sensitive = HostAndPasswordManaged;
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.Equal(["email.from_name"], h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values[Password]);
        Assert.Equal("smtp.example.com", h.Controller.State.Values["email.smtp_host"]);
        Assert.Equal("Set by SILO_SMTP_HOST in the server’s environment.", h.Controller.State.EnvManaged.Note("email.smtp_host"));
        if (statusThenFails)
        {
            h.Sensitive = "fail";
            await h.Controller.ReloadAsync(preserveDraft: true);
            Assert.True(h.Controller.State.Loaded);
            Assert.True(h.Controller.State.SensitiveStatusError);
            Assert.Equal(["email.smtp_host", Password], h.Controller.State.ManagedByEnv);
        }
        h.Controller.Set("email.smtp_host", "mail.later.invalid");
        h.Controller.SetPassword("typed-again");
        Assert.Equal(["email.from_name"], h.Controller.State.DirtyKeys);

        await h.Controller.SaveAsync();
        Assert.Equal(["email.from_name"], EnvBodies.PutKeys(h.Kit));
    }

    [Fact]
    public async Task OpeningEmailAgainStartsFromNothingWhateverTheLastServerManaged()
    {
        using var h = new Harness { Sensitive = HostAndPasswordManaged };
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.EnvManaged.IsManaged("email.smtp_host"));
        h.Controller.Suspend();
        h.Store.Invalidate(); // an account or server change invalidates the shared snapshot (AvailabilityChanged)
        h.Sensitive = "fail";
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.Loaded);
        Assert.Empty(h.Controller.State.EnvManaged.Keys);
        Assert.Empty(h.Controller.State.ManagedByEnv);
    }
}
