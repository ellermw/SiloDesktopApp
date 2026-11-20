using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersNestedGuardTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly Dictionary<string, JsonObject> Reads = new(StringComparer.Ordinal);
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminUsersApi Api;
        internal int Writes;
        internal Fixture()
        {
            Authority = new(Kit.Client, Kit.Auth); Api = new(Kit.Client);
            var capabilities = JsonNode.Parse("""{"available":true,"allowed":true,"state":"available","guarded_configuration":true,"profile_management":true,"profile_sections":true,"default_profile":true,"profile_limits":true,"access_groups":true,"ownership_transfer":true,"password_reset_link":true,"password_reset_email":true,"login_sessions":true,"admin_management":true,"admin_identities":true,"email_delivery":true,"profileless":true,"client_selected_code":true,"manifest_revision":30,"scopes":["account","profile","profile_client","profile_device","profile_library","profile_series"],"client_families":["tv","mobile","tablet","desktop","web"]}""")!.AsObject();
            foreach (var path in new[] { "/admin/users/capabilities", "/auth/sessions/capabilities", "/auth/external-sign-in/capabilities", "/admin/invitations/capabilities", "/admin/invite-codes/capabilities", "/settings/contract/capabilities" }) Reads["/api/v2" + path] = capabilities.Copy();
            Reads["/api/v2/admin/users/1"] = JsonNode.Parse("""{"id":"1","username":"Owner","role":"admin","is_owner":true,"enabled":true}""")!.AsObject();
            Reads["/api/v2/admin/users/2"] = JsonNode.Parse("""{"id":"2","username":"Target","role":"user","is_owner":false,"enabled":true,"password_login":true,"effective_policy":{"max_profiles":2}}""")!.AsObject();
            Reads["/api/v2/profiles"] = JsonNode.Parse("""{"items":[],"max_advisory_age_supported":true,"require_advisory_age_supported":true,"requests_allowed_supported":true}""")!.AsObject();
            Reads["/api/v2/admin/users/2/profiles/p2"] = JsonNode.Parse("""{"id":"p2","name":"Primary","is_primary":true}""")!.AsObject();
            Reads["/api/v2/settings/contract"] = JsonNode.Parse("""{"definitions":[{"key":"synthetic.setting","introduced_in":1,"persistence":"remote","allowed_scopes":["account","profile","profile_client","profile_device","profile_library","profile_series"],"value_schema":{"type":"integer","nullable":true,"minimum":0},"default_value":7,"resolution_order":["profile_device","profile","account","default"]}]}""")!.AsObject();
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get)
                {
                    Writes++;
                    return Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/impersonate")
                        ? """{"access_token":"synthetic-view-as","user":{"id":"2"}}""" : "{}"));
                }
                if (!Reads.TryGetValue(request.RequestUri!.PathAndQuery, out var document) && !Reads.TryGetValue(request.RequestUri.AbsolutePath, out document))
                    throw new InvalidOperationException("Unexpected synthetic read: " + request.RequestUri.PathAndQuery);
                var reply = AdminMonitorKit.Json(document.ToJsonString()); reply.Headers.ETag = new EntityTagHeaderValue("\"account-v1\"");
                return Task.FromResult(reply);
            };
        }
        internal AdminUsersReadback Snapshot(string path, string json)
        {
            Reads[path] = JsonNode.Parse(json)!.AsObject();
            return new(path, Reads[path].Copy());
        }
        internal async Task Send(AdminUsersWrite write)
        {
            using var lease = Authority.Capture();
            await Api.WriteAsync(lease, await Api.ReviewAsync(lease, "2"), write);
        }
        public void Dispose() { Authority.Dispose(); Kit.Dispose(); }
    }

    [Theory]
    [InlineData(AdminUsersOperation.CreateProfile, "profile_management")]
    [InlineData(AdminUsersOperation.SaveSections, "profile_sections")]
    [InlineData(AdminUsersOperation.Transfer, "ownership_transfer")]
    [InlineData(AdminUsersOperation.RevokeSessions, "login_sessions")]
    public async Task IndependentUserFeatureDenialNeverWrites(AdminUsersOperation operation, string capability)
    {
        using var fixture = new Fixture();
        var profiles = fixture.Snapshot("/api/v2/admin/users/2/profiles", """{"items":[{"id":"p2","is_primary":true}]}""");
        var sections = fixture.Snapshot("/api/v2/admin/users/2/profiles/p2/sections?scope=home", """{"items":[]}""");
        var sessions = fixture.Snapshot("/api/v2/admin/users/2/login-sessions?limit=100", """{"items":[],"page":{"has_more":false}}""");
        if (operation == AdminUsersOperation.Transfer) fixture.Reads["/api/v2/admin/users/2"]["role"] = "admin";
        var write = operation switch
        {
            AdminUsersOperation.CreateProfile => new AdminUsersWrite(operation, new JsonObject { ["name"] = "Synthetic" }, Readbacks: [profiles]),
            AdminUsersOperation.SaveSections => new(operation, new JsonObject { ["overrides"] = new JsonArray() }, "p2", Readbacks: [sections]),
            AdminUsersOperation.RevokeSessions => new(operation, Readbacks: [sessions]),
            _ => new(operation)
        };
        fixture.Reads["/api/v2/admin/users/capabilities"][capability] = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Send(write));
        Assert.Equal(0, fixture.Writes);
        fixture.Reads["/api/v2/admin/users/capabilities"][capability] = true;
        await fixture.Send(write);
        Assert.Equal(1, fixture.Writes);
    }

    [Theory]
    [InlineData("break_glass")]
    [InlineData("password")]
    public async Task NonOwnerCannotChangeBreakGlassOrRecoverTheirOwnDisabledPassword(string field)
    {
        using var fixture = new Fixture();
        fixture.Reads["/api/v2/admin/users/1"]["is_owner"] = false;
        fixture.Reads["/api/v2/admin/users/1"]["password_login"] = false;
        fixture.Reads["/api/v2/auth/external-sign-in/capabilities"]["break_glass"] = true;
        var body = field == "password" ? new JsonObject { [field] = "Synthetic!123" } : new JsonObject { [field] = true };
        using var lease = fixture.Authority.Capture();
        var review = await fixture.Api.ReviewAsync(lease, "1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Api.WriteAsync(lease, review, new(AdminUsersOperation.UpdateAccount, body)));
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("profile")]
    [InlineData("profile_client")]
    [InlineData("profile_device")]
    [InlineData("profile_library")]
    [InlineData("profile_series")]
    public async Task NullablePreferenceWriteKeepsExactQualifiersAndNull(string scope)
    {
        using var fixture = new Fixture();
        var identity = scope switch
        {
            "account" => new AdminUserSettingIdentity(scope),
            "profile_client" => new(scope, "p2", ClientFamily: "tv"),
            "profile_device" => new(scope, "p2", DeviceId: "device/a"),
            "profile_library" => new(scope, "p2", LibraryId: "17"),
            "profile_series" => new(scope, "p2", SeriesId: "series/a"),
            _ => new(scope, "p2")
        };
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/settings/values?limit=200", """{"items":[],"page":{"has_more":false,"next_cursor":null},"revision":30}""");
        await fixture.Send(new(AdminUsersOperation.SetSetting, new JsonObject { ["value"] = null }, Identity: identity, SettingKey: "synthetic.setting", Readbacks: [baseline]));
        Assert.Equal(1, fixture.Writes);
        Assert.Contains("PUT /api/v2/admin/users/2/settings/values/synthetic.setting" + identity.Query(), fixture.Kit.Http.Requests);
        Assert.Equal("{\"value\":null}", fixture.Kit.Http.Bodies.Last());
    }

    [Fact]
    public async Task ResetUnknownStoredKeyRemovesOnlyOneExactIdentity()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/settings/values?limit=200", """{"items":[{"key":"removed.key","scope":"profile_device","profile_id":"p2","device_id":"device/a","value":0}],"page":{"has_more":false,"next_cursor":null}}""");
        var identity = new AdminUserSettingIdentity("profile_device", "p2", DeviceId: "device/a");
        await fixture.Send(new(AdminUsersOperation.ResetSetting, Identity: identity, SettingKey: "removed.key", Readbacks: [baseline]));
        Assert.Contains("DELETE /api/v2/admin/users/2/settings/values/removed.key" + identity.Query(), fixture.Kit.Http.Requests);
        Assert.Equal(1, fixture.Writes);
    }

    [Fact]
    public async Task PartialPreferenceBaselineCannotAuthorizeAWrite()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/settings/values?limit=200", """{"items":[{"key":"first"}],"page":{"has_more":true,"next_cursor":"next"}}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Send(new(AdminUsersOperation.SetSetting, new JsonObject { ["value"] = 0 }, Identity: new("account"), SettingKey: "synthetic.setting", Readbacks: [baseline])));
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task SettingOutsideCurrentManifestScopeFailsBeforeMutation()
    {
        using var fixture = new Fixture();
        fixture.Reads["/api/v2/settings/contract/capabilities"]["scopes"] = new JsonArray("profile");
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/settings/values?limit=200", """{"items":[],"page":{"has_more":false}}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Send(new(AdminUsersOperation.SetSetting, new JsonObject { ["value"] = 0 }, Identity: new("account"), SettingKey: "synthetic.setting", Readbacks: [baseline])));
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task ProfileLimitIsRecheckedBeforeCreating()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/profiles", """{"items":[{"id":"p2","is_primary":true},{"id":"p3","is_primary":false}]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Send(new(AdminUsersOperation.CreateProfile, new JsonObject { ["name"] = "Third" }, Readbacks: [baseline])));
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task FirstProfileCannotStartLibraryRestricted()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/profiles", """{"items":[]}""");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Send(new(AdminUsersOperation.CreateProfile, new JsonObject { ["name"] = "First", ["library_restrictions_enabled"] = true }, Readbacks: [baseline])));
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData(false, "link", false)]
    [InlineData(true, "email", false)]
    [InlineData(true, "link", true)]
    public async Task InvitationRestrictionsRejectWithoutMutation(bool profileless, string delivery, bool emailPresent)
    {
        using var fixture = new Fixture();
        fixture.Reads["/api/v2/admin/invitations/capabilities"]["profileless"] = profileless;
        var body = new JsonObject { ["delivery"] = delivery, ["role"] = "user", ["create_profile"] = false };
        if (emailPresent) body["email"] = "synthetic@example.test";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Send(new(AdminUsersOperation.CreateInvitation, body)));
        Assert.Equal(0, fixture.Writes);
    }

    public static IEnumerable<object[]> SupportedWrites()
    {
        yield return [AdminUsersOperation.CreateUser, "POST", "/api/v2/admin/users"];
        yield return [AdminUsersOperation.UpdateAccount, "PUT", "/api/v2/admin/users/2"];
        yield return [AdminUsersOperation.UpdatePolicy, "PUT", "/api/v2/admin/users/2"];
        yield return [AdminUsersOperation.DeleteAccount, "DELETE", "/api/v2/admin/users/2"];
        yield return [AdminUsersOperation.ViewAs, "POST", "/api/v2/admin/users/2/impersonate"];
        yield return [AdminUsersOperation.Transfer, "POST", "/api/v2/admin/users/2/transfer-ownership"];
        yield return [AdminUsersOperation.ResetLink, "POST", "/api/v2/admin/users/2/password-reset"];
        yield return [AdminUsersOperation.ResetEmail, "POST", "/api/v2/admin/users/2/password-reset"];
        yield return [AdminUsersOperation.CreateProfile, "POST", "/api/v2/admin/users/2/profiles"];
        yield return [AdminUsersOperation.UpdateProfile, "PATCH", "/api/v2/admin/users/2/profiles/p3"];
        yield return [AdminUsersOperation.DeleteProfile, "DELETE", "/api/v2/admin/users/2/profiles/p3"];
        yield return [AdminUsersOperation.LinkIdentity, "POST", "/api/v2/admin/users/2/identities"];
        yield return [AdminUsersOperation.UnlinkIdentity, "DELETE", "/api/v2/admin/users/2/identities/external1"];
        yield return [AdminUsersOperation.RevokeSession, "DELETE", "/api/v2/admin/users/2/login-sessions/session1"];
        yield return [AdminUsersOperation.RevokeSessions, "DELETE", "/api/v2/admin/users/2/login-sessions"];
        yield return [AdminUsersOperation.Quota, "PUT", "/api/v2/admin/request-users/2/limit"];
        yield return [AdminUsersOperation.SetSetting, "PUT", "/api/v2/admin/users/2/settings/values/synthetic.setting?scope=account"];
        yield return [AdminUsersOperation.ResetSetting, "DELETE", "/api/v2/admin/users/2/settings/values/synthetic.setting?scope=account"];
        yield return [AdminUsersOperation.SaveSections, "PUT", "/api/v2/admin/users/2/profiles/p2/sections?scope=home"];
        yield return [AdminUsersOperation.ResetSections, "DELETE", "/api/v2/admin/users/2/profiles/p2/sections?scope=home"];
        yield return [AdminUsersOperation.CreateInvitation, "POST", "/api/v2/admin/invitations"];
        yield return [AdminUsersOperation.ResendInvitation, "POST", "/api/v2/admin/invitations/4/resend"];
        yield return [AdminUsersOperation.RevokeInvitation, "DELETE", "/api/v2/admin/invitations/4"];
        yield return [AdminUsersOperation.CreateCode, "POST", "/api/v2/admin/invite-codes"];
        yield return [AdminUsersOperation.UpdateCode, "PUT", "/api/v2/admin/invite-codes/code1"];
        yield return [AdminUsersOperation.TopUpCode, "POST", "/api/v2/admin/invite-codes/code1/top-up"];
        yield return [AdminUsersOperation.DeleteCode, "DELETE", "/api/v2/admin/invite-codes/code1"];
    }

    [Theory]
    [MemberData(nameof(SupportedWrites))]
    public async Task EverySupportedMutationHasAnExactRouteAndOneSubmission(AdminUsersOperation operation, string method, string expectedPath)
    {
        using var fixture = new Fixture();
        JsonObject Body(string json) => JsonNode.Parse(json)!.AsObject();
        var profiles = fixture.Snapshot("/api/v2/admin/users/2/profiles", """{"items":[{"id":"p2","is_primary":true},{"id":"p3","is_primary":false}]}""");
        if (operation == AdminUsersOperation.CreateProfile) fixture.Reads["/api/v2/admin/users/2"]["effective_policy"]!["max_profiles"] = 3;
        if (operation == AdminUsersOperation.Transfer) fixture.Reads["/api/v2/admin/users/2"]["role"] = "admin";
        var profile = fixture.Snapshot("/api/v2/admin/users/2/profiles/p3", """{"id":"p3","name":"Second","is_primary":false}""");
        var sections = fixture.Snapshot("/api/v2/admin/users/2/profiles/p2/sections?scope=home", """{"items":[]}""");
        var identities = fixture.Snapshot("/api/v2/admin/users/2/identities", """{"items":[{"id":"external1"}]}""");
        var providers = fixture.Snapshot("/api/v2/admin/plugins/installations?limit=100", """{"items":[{"id":"provider1","capabilities":[{"id":"cap1","type":"auth_provider.v1"}],"auth_bindings":[{"capability_id":"cap1"}]}],"page":{"has_more":false}}""");
        var sessions = fixture.Snapshot("/api/v2/admin/users/2/login-sessions?limit=100", """{"items":[{"id":"session1"}],"page":{"has_more":false}}""");
        var settings = fixture.Snapshot("/api/v2/admin/users/2/settings/values?limit=200", """{"items":[{"key":"synthetic.setting","scope":"account","value":1}],"page":{"has_more":false}}""");
        var quota = fixture.Snapshot("/api/v2/admin/request-users/2/limit", """{"user_id":"2","limit_mode":"inherit","max_requests":null,"window_days":null,"approval_mode":"inherit"}""") with { ETag = "\"account-v1\"", Strong = true };
        var invitation = fixture.Snapshot("/api/v2/admin/invitations/4", """{"id":"4","status":"pending","create_profile":true,"email":""}""");
        var codes = fixture.Snapshot("/api/v2/admin/invite-codes?limit=100", """{"items":[{"id":"code1","enabled":true,"max_uses":4}],"page":{"has_more":false}}""");
        var write = operation switch
        {
            AdminUsersOperation.CreateUser => new AdminUsersWrite(operation, Body("""{"username":"Synthetic","email":"fixture@example.test","password":"Synthetic!123","role":"user","create_default_profile":false}""")),
            AdminUsersOperation.UpdateAccount => new(operation, Body("""{"username":"Renamed"}""")),
            AdminUsersOperation.UpdatePolicy => new(operation, Body("""{"requests_allowed":false}""")),
            AdminUsersOperation.CreateProfile => new(operation, Body("""{"name":"Third"}"""), Readbacks: [profiles]),
            AdminUsersOperation.UpdateProfile => new(operation, Body("""{"pin":null}"""), "p3", Readbacks: [profile]),
            AdminUsersOperation.DeleteProfile => new(operation, ResourceId: "p3", Readbacks: [profiles, profile]),
            AdminUsersOperation.LinkIdentity => new(operation, Body("""{"installation_id":"provider1","external_subject":"Issuer|Exact"}"""), Readbacks: [providers]),
            AdminUsersOperation.UnlinkIdentity => new(operation, ResourceId: "external1", Readbacks: [identities]),
            AdminUsersOperation.RevokeSession => new(operation, ResourceId: "session1", Readbacks: [sessions]),
            AdminUsersOperation.RevokeSessions => new(operation, Readbacks: [sessions]),
            AdminUsersOperation.Quota => new(operation, Body("""{"limit_mode":"inherit","max_requests":null,"window_days":null,"approval_mode":"inherit"}"""), Readbacks: [quota]),
            AdminUsersOperation.SetSetting => new(operation, Body("""{"value":0}"""), Identity: new("account"), SettingKey: "synthetic.setting", Readbacks: [settings]),
            AdminUsersOperation.ResetSetting => new(operation, Identity: new("account"), SettingKey: "synthetic.setting", Readbacks: [settings]),
            AdminUsersOperation.SaveSections => new(operation, Body("""{"overrides":[]}"""), "p2", Readbacks: [sections]),
            AdminUsersOperation.ResetSections => new(operation, ResourceId: "p2", Readbacks: [sections]),
            AdminUsersOperation.CreateInvitation => new(operation, Body("""{"role":"user","delivery":"link","create_profile":false}""")),
            AdminUsersOperation.ResendInvitation => new(operation, Body("""{"delivery":"link"}"""), "4", Readbacks: [invitation]),
            AdminUsersOperation.RevokeInvitation => new(operation, ResourceId: "4", Readbacks: [invitation]),
            AdminUsersOperation.CreateCode => new(operation, Body("""{"code":"SYNTHETIC","max_uses":1}""")),
            AdminUsersOperation.UpdateCode => new(operation, Body("""{"enabled":false}"""), "code1", Readbacks: [codes]),
            AdminUsersOperation.TopUpCode => new(operation, Body("""{"additional_uses":1}"""), "code1", Readbacks: [codes]),
            AdminUsersOperation.DeleteCode => new(operation, ResourceId: "code1", Readbacks: [codes]),
            _ => new(operation)
        };
        await fixture.Send(write);
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(method + " " + expectedPath, fixture.Kit.Http.Requests.Last());
    }

    [Fact]
    public async Task QuotaUsesItsIndependentStrongValidator()
    {
        using var fixture = new Fixture();
        var path = "/api/v2/admin/request-users/2/limit";
        var baseline = fixture.Snapshot(path, """{"user_id":"2","limit_mode":"inherit","max_requests":null,"window_days":null,"approval_mode":"inherit"}""");
        baseline = baseline with { ETag = "\"quota-v1\"", Strong = true };
        var body = new JsonObject { ["limit_mode"] = "custom", ["max_requests"] = 0, ["window_days"] = 7, ["approval_mode"] = "manual" };
        var previous = fixture.Kit.Http.Respond;
        fixture.Kit.Http.Respond = async (request, token) =>
        {
            if (request.Method == HttpMethod.Put) Assert.Equal("\"quota-v1\"", request.Headers.IfMatch.Single().ToString());
            var response = await previous(request, token);
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == path) response.Headers.ETag = new EntityTagHeaderValue("\"quota-v1\"");
            return response;
        };
        await fixture.Send(new(AdminUsersOperation.Quota, body, Readbacks: [baseline]));
        Assert.Equal(1, fixture.Writes);
    }

    [Fact]
    public async Task StaleInviteCodeMustBeReadBackAndReviewedInsteadOfReplayed()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/invite-codes?limit=100", """{"items":[{"id":"code-1","enabled":true,"max_uses":4}],"page":{"has_more":false}}""");
        fixture.Reads[baseline.Path]["items"]![0]!["max_uses"] = 9;
        await Assert.ThrowsAsync<AdminUsersReviewRequiredException>(() => fixture.Send(new(AdminUsersOperation.TopUpCode, new JsonObject { ["additional_uses"] = 1 }, "code-1", Readbacks: [baseline])));
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task NonemptyHomeOverridesUseSupportedWriteSchemaAndPreserveOpaqueConfig()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/users/2/profiles/p2/sections?scope=home", """{"items":[]}""");
        var body = JsonNode.Parse("""{"overrides":[{"id":"synthetic-override","section_id":"home1","hidden":true,"removed":false,"is_user_added":false,"position":0,"config":{"future_recipe_value":{"nested":[0,false,null]}}}]}""")!.AsObject();
        await fixture.Send(new(AdminUsersOperation.SaveSections, body, "p2", Readbacks: [baseline]));
        Assert.Equal(1, fixture.Writes);
        Assert.True(JsonNode.DeepEquals(body, JsonNode.Parse(fixture.Kit.Http.Bodies.Last())));
    }

    [Fact]
    public async Task ExternalIdentityRequiresMatchingProviderBindingAndPreservesSubjectCase()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Snapshot("/api/v2/admin/plugins/installations?limit=100", """{"items":[{"id":"provider-1","capabilities":[{"id":"auth-1","type":"auth_provider.v1"}],"auth_bindings":[{"capability_id":"auth-1"}]}],"page":{"has_more":false}}""");
        var body = new JsonObject { ["installation_id"] = "provider-1", ["external_subject"] = "  Issuer|CaseSensitive  " };
        await fixture.Send(new(AdminUsersOperation.LinkIdentity, body, Readbacks: [baseline]));
        Assert.Equal("Issuer|CaseSensitive", JsonNode.Parse(fixture.Kit.Http.Bodies.Last())!.Text("external_subject"));
        fixture.Reads[baseline.Path]["items"]![0]!["auth_bindings"] = new JsonArray();
        var invalid = new AdminUsersReadback(baseline.Path, fixture.Reads[baseline.Path].Copy());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Send(new(AdminUsersOperation.LinkIdentity, body, Readbacks: [invalid])));
        Assert.Equal(1, fixture.Writes);
    }
}
