using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

/// <summary>The rules of the official Create user form (CreateUserForm.tsx, UserPolicyFields.tsx, email.ts, streamBitrateLimit.ts).</summary>
public sealed class AdminUserCreatePolicyTests
{
    private static JsonObject Group(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject Body(bool requireChange = false, string role = "user", string? group = null, UserPolicyState? policy = null, IEnumerable<string>? permissions = null,
        bool sendGroup = true, bool sendMaxProfiles = true) =>
        AdminUserCreatePolicy.BuildBody("alice", "alice@example.com", "Secret-Password-1", requireChange, role, permissions ?? AdminUserCreatePolicy.DefaultPermissions, true,
            policy ?? new UserPolicyState(), group, sendGroup, sendMaxProfiles);

    [Fact]
    public void ABareFormSendsTheAccountFieldsTheDefaultPermissionAndNothingElse()
    {
        var body = Body();
        Assert.Equal(["username", "email", "password", "role", "permissions", "create_default_profile"], body.Select(pair => pair.Key));
        Assert.Equal("user", body["role"]!.GetValue<string>());
        Assert.Equal(["marker_edit"], body["permissions"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.True(body["create_default_profile"]!.GetValue<bool>());
    }

    [Fact]
    public void RequirePasswordChangeIsSentOnlyWhenItIsOn()
    {
        Assert.False(Body(requireChange: false).ContainsKey("require_password_change"));
        Assert.True(Body(requireChange: true)["require_password_change"]!.GetValue<bool>());
    }

    [Fact]
    public void OnlyOverriddenPolicyFieldsAreSent()
    {
        var policy = new UserPolicyState { MaxStreams = 2, DownloadAllowed = false, MaxPlaybackQuality = "", LibraryIds = ["3", "5"], MaxRemoteStreamBitrateKbps = 8000 };
        var body = Body(policy: policy);
        Assert.Equal(2, body["max_streams"]!.GetValue<long>());
        Assert.False(body["download_allowed"]!.GetValue<bool>());
        Assert.Equal("", body["max_playback_quality"]!.GetValue<string>());
        Assert.Equal(["3", "5"], body["library_ids"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(8000, body["max_remote_stream_bitrate_kbps"]!.GetValue<long>());
        foreach (var inherited in new[] { "max_transcodes", "max_local_stream_bitrate_kbps", "max_profiles", "transcode_allowed", "audio_transcode_allowed", "download_transcode_allowed", "requests_allowed" })
            Assert.False(body.ContainsKey(inherited), inherited + " inherits and is not sent");
    }

    [Fact]
    public void AnExplicitEmptyLibraryListIsSentAndInheritingIsNot()
    {
        Assert.Empty(Body(policy: new UserPolicyState { LibraryIds = [] })["library_ids"]!.AsArray());
        Assert.False(Body(policy: new UserPolicyState { LibraryIds = null }).ContainsKey("library_ids"));
    }

    [Fact]
    public void AnAdminIsNeverGroupedAndAUserIsGroupedWhenOneApplies()
    {
        Assert.False(Body(role: "admin", group: "4").ContainsKey("access_group_id"));
        Assert.Equal("4", Body(role: "user", group: "4")["access_group_id"]!.GetValue<string>());
        Assert.False(Body(role: "user", group: null).ContainsKey("access_group_id"));
        Assert.Null(AdminUserCreatePolicy.EffectiveAccessGroupId("admin", "4"));
        Assert.Equal("4", AdminUserCreatePolicy.EffectiveAccessGroupId("user", "4"));
    }

    [Fact]
    public void AnOlderServerWithoutGroupsOrProfileLimitsIsNotSentThem()
    {
        var body = Body(group: "4", policy: new UserPolicyState { MaxProfiles = 3, MaxStreams = 1 }, sendGroup: false, sendMaxProfiles: false);
        Assert.False(body.ContainsKey("access_group_id"));
        Assert.False(body.ContainsKey("max_profiles"));
        Assert.True(body.ContainsKey("max_streams"));
    }

    [Fact]
    public void ThePermissionSwitchesAddAndRemoveTheirOwnPermissionOnly()
    {
        var permissions = AdminUserCreatePolicy.SetPermission(AdminUserCreatePolicy.DefaultPermissions, AdminUserCreatePolicy.MetadataCuration, true).ToList();
        Assert.Equal(["marker_edit", "metadata_curation"], permissions);
        Assert.Equal(["metadata_curation"], AdminUserCreatePolicy.SetPermission(permissions, AdminUserCreatePolicy.MarkerEdit, false));
        Assert.Equal(["marker_edit"], AdminUserCreatePolicy.SetPermission(["marker_edit"], "marker_edit", true));
    }

    [Fact]
    public void TheGroupPickerStartsOnTheDefaultGroup()
    {
        var groups = new[] { Group("""{"id":"1","name":"Family"}"""), Group("""{"id":"2","name":"Everyone","is_default":true}""") };
        Assert.Equal("2", AdminUserCreatePolicy.DefaultGroupId(groups));
        Assert.Null(AdminUserCreatePolicy.DefaultGroupId([groups[0]]));
    }

    [Fact]
    public void InheritedValuesComeFromTheSelectedGroupAndAreUnknownOtherwise()
    {
        var groups = new[] { Group("""{"id":"2","name":"Everyone","library_ids":["1","2"],"max_streams":3,"max_playback_quality":"1080p","download_allowed":true,"max_remote_stream_bitrate_kbps":0}""") };
        var hints = AdminUserCreatePolicy.InheritHints("user", "2", groups)!;
        Assert.Equal(["1", "2"], hints.LibraryIds);
        Assert.True(hints.LibraryIdsKnown);
        Assert.Equal(3, hints.MaxStreams);
        Assert.Equal("1080p", hints.MaxPlaybackQuality);
        Assert.True(hints.DownloadAllowed);
        Assert.Equal(0, hints.MaxRemoteStreamBitrateKbps);
        Assert.Null(hints.TranscodeAllowed);
        Assert.Null(AdminUserCreatePolicy.InheritHints("admin", "2", groups));
        Assert.Null(AdminUserCreatePolicy.InheritHints("user", null, groups));
        Assert.Null(AdminUserCreatePolicy.InheritHints("user", "9", groups));
    }

    [Fact]
    public void AGroupFromAnOlderServerWithoutPolicyFieldsHasNoKnownValues()
    {
        var hints = UserPolicyHints.FromGroup(Group("""{"id":"2","name":"Everyone"}"""));
        Assert.False(hints.LibraryIdsKnown);
        Assert.False(hints.MaxPlaybackQualityKnown);
        Assert.Null(hints.MaxStreams);
        Assert.Null(hints.RequestsAllowed);
    }

    [Fact]
    public void AGroupWithNullLibrariesMeansAllLibraries()
    {
        var hints = UserPolicyHints.FromGroup(Group("""{"id":"2","library_ids":null}"""));
        Assert.True(hints.LibraryIdsKnown);
        Assert.Null(hints.LibraryIds);
    }

    [Theory]
    [InlineData(UserPolicyDefaultSource.Admin, "admin", "3")]
    [InlineData(UserPolicyDefaultSource.Server, "user", null)]
    [InlineData(UserPolicyDefaultSource.Group, "user", "3")]
    public void TheDefaultSourceIsTheAdminDefaultTheServerDefaultOrTheGroup(UserPolicyDefaultSource expected, string role, string? group) =>
        Assert.Equal(expected, AdminUserCreatePolicy.DefaultSource(role, AdminUserCreatePolicy.EffectiveAccessGroupId(role, group)));

    [Theory]
    [InlineData("5", 0, 5)]
    [InlineData("0", 0, 0)]
    [InlineData(" 12 ", 1, 12)]
    [InlineData("0", 1, null)]
    [InlineData("", 0, null)]
    [InlineData("  ", 0, null)]
    [InlineData("-1", 0, null)]
    [InlineData("1.5", 0, null)]
    [InlineData("abc", 0, null)]
    public void ALimitBoxHoldsAWholeNumberAtOrAboveItsMinimumOrNothing(string draft, int min, int? expected) =>
        Assert.Equal((long?)expected, AdminUserCreatePolicy.ParseLimit(draft, min));

    [Theory]
    [InlineData("8", 8000)]
    [InlineData("1.5", 1500)]
    [InlineData("0.25", 250)]
    [InlineData("12,5", 12500)]
    [InlineData(".5", 500)]
    [InlineData(" 20 ", 20000)]
    [InlineData("0", null)]
    [InlineData("0.", null)]
    [InlineData("", null)]
    [InlineData("1.2345", null)]
    [InlineData("abc", null)]
    [InlineData("-3", null)]
    public void ACustomBitrateIsAPositiveNumberOfMbpsWithThreeDecimalsAtMost(string text, int? expected) =>
        Assert.Equal((long?)expected, AdminUserCreatePolicy.ParseStreamBitrateMbps(text));

    [Fact]
    public void BitrateCapsRoundTripAndLowOnesAreFlagged()
    {
        Assert.Equal("8", AdminUserCreatePolicy.StreamBitrateMbpsText(8000));
        Assert.Equal("1.5", AdminUserCreatePolicy.StreamBitrateMbpsText(1500));
        Assert.Equal("0.25", AdminUserCreatePolicy.StreamBitrateMbpsText(250));
        Assert.True(AdminUserCreatePolicy.IsLowStreamBitrate(500));
        Assert.False(AdminUserCreatePolicy.IsLowStreamBitrate(1000));
        Assert.False(AdminUserCreatePolicy.IsLowStreamBitrate(0));
    }

    [Theory]
    [InlineData("alice@example.com", true)]
    [InlineData("  alice@example.com  ", true)]
    [InlineData("a@b.co", true)]
    [InlineData("alice@[192.168.0.1]", true)]
    [InlineData("alice@[::1]", true)]
    [InlineData("admin@siloserver", false)]
    [InlineData("alice@", false)]
    [InlineData("@example.com", false)]
    [InlineData("alice@@example.com", false)]
    [InlineData("alice@exa mple.com", false)]
    [InlineData("alice@example.", false)]
    [InlineData("alice@.com", false)]
    [InlineData("alice@[IPv6:::1]", false)]
    [InlineData("alice@[example]", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EmailsAreCheckedLikeTheWebUi(string? email, bool valid) => Assert.Equal(valid, AdminUserCreatePolicy.IsValidEmail(email));
}
