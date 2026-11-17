using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersRequestTermsTests
{
    private static JsonObject Defaults() => JsonNode.Parse("""{"requests_enabled":true,"global_max_requests":5,"global_window_days":7,"global_auto_approval_enabled":false}""")!.AsObject();
    private static JsonObject User(string role = "user") => new() { ["role"] = role, ["access_group_id"] = "g1", ["effective_policy"] = new JsonObject { ["requests_allowed"] = true } };
    private static JsonObject Inherit() => JsonNode.Parse("""{"limit_mode":"inherit","approval_mode":"inherit","max_requests":null,"window_days":null}""")!.AsObject();

    [Fact]
    public void AdminSkipsGroupForQuotaAndApproval()
    {
        var group = JsonNode.Parse("""{"limit_mode":"unlimited","approval_mode":"auto"}""")!.AsObject();
        var text = AdminUserRequestTerms.Describe(User("admin"), Inherit(), group, Defaults());
        Assert.Contains("5 per 7 days", text); Assert.Contains("Manual approval", text); Assert.DoesNotContain("Group", text);
    }

    [Fact]
    public void GroupQuotaAndAccountApprovalResolveIndependently()
    {
        var account = Inherit(); account["approval_mode"] = "auto";
        var group = JsonNode.Parse("""{"limit_mode":"custom","max_requests":0,"window_days":14,"approval_mode":"manual"}""")!.AsObject();
        var text = AdminUserRequestTerms.Describe(User(), account, group, Defaults());
        Assert.Contains("0 per 14 days", text); Assert.Contains("not an account block", text); Assert.Contains("Group #g1", text); Assert.Contains("Automatic approval · Account", text);
    }

    [Fact]
    public void MissingGroupCannotSilentlyBecomeServerDefaults() =>
        Assert.Contains("unavailable", AdminUserRequestTerms.Describe(User(), Inherit(), null, Defaults()));

    [Fact]
    public void LegacyBlockIsPreservedForDisplayButNotOfferedAsNewChoice()
    {
        var saved = Inherit(); saved["limit_mode"] = "blocked"; saved["approval_mode"] = "auto";
        Assert.Contains("Legacy blocked", AdminUserRequestTerms.Describe(User("admin"), saved, null, Defaults()));
        var cleared = AdminUserRequestTerms.ClearLegacy(saved);
        Assert.Equal("inherit", cleared.Text("limit_mode")); Assert.Equal("auto", cleared.Text("approval_mode"));
        Assert.True(cleared.ContainsKey("max_requests")); Assert.Null(cleared["max_requests"]);
        Assert.DoesNotContain(AdminUserRequestTerms.EditorSchema()["properties"]!["limit_mode"]!["enum"]!.AsArray(), value => value?.GetValue<string>() == "blocked");
    }

    [Fact]
    public void DottedProfileAndDeviceIdsCannotCollideInLevelSelection()
    {
        var first = AdminUserPreferences.LevelId(new("profile_device", "p.a", DeviceId: "b"));
        var second = AdminUserPreferences.LevelId(new("profile_device", "p", DeviceId: "a.b"));
        Assert.NotEqual(first, second);
    }
}
