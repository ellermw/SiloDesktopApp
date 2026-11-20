using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersRulesTests
{
    private static JsonObject Account(string id = "2", string role = "user", bool owner = false, bool enabled = true) =>
        new() { ["id"] = id, ["role"] = role, ["is_owner"] = owner, ["enabled"] = enabled };

    [Theory]
    [InlineData("2", "user", false, false, true, true)]
    [InlineData("2", "admin", false, false, false, false)]
    [InlineData("1", "admin", false, false, true, false)]
    [InlineData("2", "admin", false, true, true, true)]
    [InlineData("2", "admin", true, true, false, false)]
    public void OwnerAndSelfAccountPolicyAreDifferent(string id, string role, bool targetOwner, bool viewerOwner, bool manage, bool policy)
    {
        var target = Account(id, role, targetOwner);
        Assert.Equal(manage, AdminUsersRules.CanManage(target, "1", viewerOwner));
        Assert.Equal(policy, AdminUsersRules.CanChangePolicy(target, "1", viewerOwner));
    }

    [Fact]
    public void NonOwnerSelfCannotChangeAdminProfilesOrPins()
    {
        Assert.True(AdminUsersRules.CanManage(Account("1", "admin"), "1", false));
        Assert.False(AdminUsersRules.CanManageProfiles(Account("1", "admin"), "1", false));
    }

    [Theory]
    [InlineData("2", "admin", false, true, true)]
    [InlineData("1", "admin", false, true, false)]
    [InlineData("2", "admin", true, true, false)]
    [InlineData("2", "admin", false, false, false)]
    [InlineData("2", "user", false, true, false)]
    public void TransferEligibilityIsExact(string id, string role, bool owner, bool enabled, bool expected) =>
        Assert.Equal(expected, AdminUsersRules.CanTransfer(Account(id, role, owner, enabled), "1", true));

    [Theory]
    [InlineData("account", null, null, null, null, null)]
    [InlineData("profile", "p2", null, null, null, null)]
    [InlineData("profile_device", "p2", null, "device/a", null, null)]
    [InlineData("profile_client", "p2", "tv", null, null, null)]
    [InlineData("profile_library", "p2", null, null, "17", null)]
    [InlineData("profile_series", "p2", null, null, null, "series/a")]
    public void PreferenceIdentityRoundTripsEveryQualifier(string scope, string? profile, string? family, string? device, string? library, string? series)
    {
        var identity = new AdminUserSettingIdentity(scope, profile, family, device, library, series);
        identity.Validate();
        var query = identity.Query();
        Assert.Contains("scope=" + scope, query);
        if (profile != null) Assert.Contains("profile_id=" + Uri.EscapeDataString(profile), query);
        if (family != null) Assert.Contains("client_family=" + family, query);
        if (device != null) Assert.Contains("device_id=" + Uri.EscapeDataString(device), query);
        if (library != null) Assert.Contains("library_id=" + library, query);
        if (series != null) Assert.Contains("series_id=" + Uri.EscapeDataString(series), query);
        Assert.Equal(identity, AdminUserSettingIdentity.FromRow(identity.ToRow()));
    }

    [Theory]
    [InlineData("profile_device", "p2", null, null, null, null)]
    [InlineData("account", "p2", null, null, null, null)]
    [InlineData("profile", "p2", null, "device", null, null)]
    [InlineData("profile_client", "p2", "console", null, null, null)]
    [InlineData("profile_library", "p2", null, null, "17", "series")]
    public void InvalidOrWidenedIdentitiesFailBeforeTransport(string scope, string? profile, string? family, string? device, string? library, string? series) =>
        Assert.Throws<ArgumentException>(() => new AdminUserSettingIdentity(scope, profile, family, device, library, series).Validate());

    [Fact]
    public void NullFalseZeroAndEmptyLibrariesRemainDistinct()
    {
        var schema = AdminUsersContract.Schema("AdminAccountUpdateBody");
        var body = JsonNode.Parse("{\"max_streams\":0,\"transcode_allowed\":false,\"library_ids\":[]}")!.AsObject();
        AdminUsersContract.Validate(schema, body);
        Assert.Equal(0, body["max_streams"]!.GetValue<int>());
        Assert.False(body["transcode_allowed"]!.GetValue<bool>());
        Assert.Empty(body["library_ids"]!.AsArray());
        body["library_ids"] = null;
        AdminUsersContract.Validate(schema, body);
        Assert.True(body.ContainsKey("library_ids"));
    }

    [Fact]
    public void ConstructedIntegersUseWireNumericSemantics()
    {
        AdminUsersContract.Validate(AdminUsersContract.Schema("AdminInviteCodeTopUpInputBody"), new JsonObject { ["additional_uses"] = 1 });
        Assert.Equal(2, new JsonObject { ["position"] = 2 }.Count("position"));
        Assert.Throws<ArgumentException>(() => AdminUsersContract.Validate(AdminUsersContract.Schema("AdminInviteCodeTopUpInputBody"), new JsonObject { ["additional_uses"] = 0 }));
    }

    [Fact]
    public void QuotaInheritIsNotCustomZero()
    {
        AdminUsersRules.ValidateQuota(JsonNode.Parse("{\"limit_mode\":\"inherit\",\"max_requests\":null,\"window_days\":null,\"approval_mode\":\"inherit\"}")!.AsObject());
        AdminUsersRules.ValidateQuota(JsonNode.Parse("{\"limit_mode\":\"custom\",\"max_requests\":0,\"window_days\":7,\"approval_mode\":\"manual\"}")!.AsObject());
        Assert.Throws<ArgumentException>(() => AdminUsersRules.ValidateQuota(JsonNode.Parse("{\"limit_mode\":\"custom\",\"max_requests\":null,\"window_days\":null,\"approval_mode\":\"auto\"}")!.AsObject()));
    }

    [Fact]
    public void ProfilePrimaryAndPinBoundariesRejectBeforeTransport()
    {
        Assert.Throws<ArgumentException>(() => AdminUsersRules.ValidateProfile(new JsonObject { ["pin"] = "" }, false));
        Assert.Throws<ArgumentException>(() => AdminUsersRules.ValidateProfile(new JsonObject { ["pin"] = new string('é', 37) }, false));
        Assert.Throws<ArgumentException>(() => AdminUsersRules.ValidateProfile(new JsonObject { ["library_restrictions_enabled"] = true }, true));
        AdminUsersRules.ValidateProfile(new JsonObject { ["pin"] = null }, true);
    }

    [Fact]
    public void IndependentCapabilitiesNeverInheritUsersAvailable()
    {
        var capabilities = new JsonObject { ["available"] = true, ["allowed"] = true, ["state"] = "available" };
        Assert.False(AdminUsersRules.Feature(capabilities, "profile_management"));
        Assert.False(AdminUsersRules.Feature(capabilities, "admin_identities"));
        Assert.False(AdminUsersRules.Feature(capabilities, "admin_management"));
    }

    [Fact]
    public void SectionChangesPreserveUnknownRemovedRowsAndOnlyOverrideChangedFields()
    {
        var settings = JsonNode.Parse("[{\"id\":\"system-1\",\"position\":0,\"hidden\":false},{\"id\":\"system-2\",\"position\":1,\"hidden\":false}]")!.AsArray();
        var saved = JsonNode.Parse("[{\"id\":\"prior\",\"section_id\":\"gone\",\"removed\":true,\"config\":{\"future\":1}}]")!.AsArray();
        var result = AdminUserHomeSections.Change(settings, saved, "system-2", hidden: true);
        Assert.True(JsonNode.DeepEquals(saved[0], result[0]));
        Assert.Single(result.OfType<JsonObject>().Where(row => row["section_id"]?.GetValue<string>() == "system-2"));
        Assert.DoesNotContain(result.OfType<JsonObject>(), row => row["section_id"]?.GetValue<string>() == "system-1");
    }
}
