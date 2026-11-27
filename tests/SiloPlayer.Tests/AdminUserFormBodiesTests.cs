using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

/// <summary>What the profile, invitation, invite-code and identity forms send (official ProfileEditorDialog, profile-management.ts, CreateInvitationForm, InviteCodesTab, LinkIdentityDialog).</summary>
public sealed class AdminUserFormBodiesTests
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();
    private static readonly AdminProfileSupport All = new(true, true, true, true);
    private static readonly AdminProfileSupport None = new(false, false, false, false);

    [Fact]
    public void ANewProfileSendsOnlyWhatItSetsAndNoLibraryLimitOnAPrimary()
    {
        var draft = AdminProfileDraft.From(null) with { Name = "  Kid  " };
        var body = draft.CreateBody(All, false);
        Assert.Equal(["name", "is_child", "library_restrictions_enabled", "allowed_library_ids"], body.Select(pair => pair.Key));
        Assert.Equal("Kid", body["name"]!.GetValue<string>());
        var limited = (draft with { LibraryRestrictions = true, AllowedLibraries = ["3", "1"] }).CreateBody(All, false);
        Assert.True(limited["library_restrictions_enabled"]!.GetValue<bool>());
        var primary = (draft with { LibraryRestrictions = true, AllowedLibraries = ["1"] }).CreateBody(All, true);
        Assert.False(primary["library_restrictions_enabled"]!.GetValue<bool>());
        Assert.Empty(primary["allowed_library_ids"]!.AsArray());
    }

    [Fact]
    public void AdvisoryAgeAndRequestsAreNeverSentToAServerThatDoesNotKnowThem()
    {
        var draft = AdminProfileDraft.From(null) with { Name = "Kid", MaxAdvisoryAge = 12, RequireAdvisoryAge = true, RequestsAllowed = false, Quality = "4k", MaxContentRating = "PG", Pin = "1234" };
        var old = draft.CreateBody(None, false);
        Assert.False(old.ContainsKey("max_advisory_age") || old.ContainsKey("require_advisory_age") || old.ContainsKey("requests_allowed"));
        var current = draft.CreateBody(All, false);
        Assert.Equal(12, current["max_advisory_age"]!.GetValue<long>());
        Assert.True(current["require_advisory_age"]!.GetValue<bool>());
        Assert.False(current["requests_allowed"]!.GetValue<bool>());
        Assert.Equal("2160p", current["max_playback_quality"]!.GetValue<string>());
        Assert.Equal("PG", current["max_content_rating"]!.GetValue<string>());
        Assert.Equal("1234", current["pin"]!.GetValue<string>());
    }

    [Fact]
    public void AnEditSendsOnlyTheMembersThatChangedAndLeavesUnreportedOnesAlone()
    {
        var saved = Json("""{"id":"p3","name":"Second","is_primary":false,"is_child":false,"has_pin":false,"avatar":null}""");
        var draft = AdminProfileDraft.From(saved) with { Name = "Second Renamed" };
        var body = draft.UpdateBody(saved, All, false);
        Assert.Equal(["name"], body.Select(pair => pair.Key));
        var full = Json("""{"id":"p3","name":"Second","is_child":false,"avatar":"preset:dicebear:identicon:x","max_content_rating":"PG","max_playback_quality":"1080p","library_restrictions_enabled":true,"allowed_library_ids":["2","1"],"requests_allowed":true,"max_advisory_age":null}""");
        var same = AdminProfileDraft.From(full);
        Assert.Empty(same.UpdateBody(full, All, false));
        var cleared = (same with { MaxContentRating = "", Quality = "any", Avatar = "", LibraryRestrictions = false }).UpdateBody(full, All, false);
        Assert.Null(cleared["max_content_rating"]); Assert.Null(cleared["max_playback_quality"]); Assert.Null(cleared["avatar"]);
        Assert.False(cleared["library_restrictions_enabled"]!.GetValue<bool>());
        Assert.Empty(cleared["allowed_library_ids"]!.AsArray());
    }

    [Fact]
    public void PinsAreSentOnlyWhenTypedAndRemovedWithNull()
    {
        var saved = Json("""{"id":"p2","name":"Primary","has_pin":true}""");
        var draft = AdminProfileDraft.From(saved);
        Assert.False(draft.UpdateBody(saved, All, true).ContainsKey("pin"));
        Assert.Equal("4321", (draft with { Pin = "4321" }).UpdateBody(saved, All, true)["pin"]!.GetValue<string>());
        var removed = (draft with { ClearPin = true }).UpdateBody(saved, All, true);
        Assert.True(removed.ContainsKey("pin")); Assert.Null(removed["pin"]);
        Assert.True(AdminProfileDraft.PinOk("0123")); Assert.False(AdminProfileDraft.PinOk("123")); Assert.False(AdminProfileDraft.PinOk("12a4"));
    }

    [Fact]
    public void TheProfileRulesNeedANameAFourDigitPinAndALibraryWhenLimited()
    {
        var draft = AdminProfileDraft.From(null);
        Assert.Equal((true, false, false), draft.Problems(false));
        Assert.Equal((false, true, false), (draft with { Name = "A", Pin = "12" }).Problems(false));
        Assert.Equal((false, false, true), (draft with { Name = "A", LibraryRestrictions = true }).Problems(false));
        Assert.Equal((false, false, false), (draft with { Name = "A", LibraryRestrictions = true }).Problems(true));
    }

    [Fact]
    public void TheKidsSwitchSeedsASaferProfileAndAnythingSetByHandStays()
    {
        var draft = AdminProfileDraft.From(null) with { Name = "Kid" };
        var kids = draft.WithKids(true, false, false, false, false);
        Assert.Equal(("PG", true, false), (kids.MaxContentRating, kids.LibraryRestrictions, kids.RequestsAllowed));
        var kept = draft.WithKids(true, true, true, true, false);
        Assert.Equal(("", false, true), (kept.MaxContentRating, kept.LibraryRestrictions, kept.RequestsAllowed));
        var off = kids.WithKids(false, false, false, false, false);
        Assert.Equal(("", false, true, false), (off.MaxContentRating, off.LibraryRestrictions, off.RequestsAllowed, off.IsChild));
        Assert.False(draft.WithKids(true, false, false, false, true).LibraryRestrictions);
    }

    [Fact]
    public void AnInvitationSendsTheAddressOnlyForEmailAndNeverAGroupForAnAdmin()
    {
        var email = AdminUserFormBodies.Invitation("email", " a@b.test ", "user", "g1", ["1", "2"], true, true, "  hi  ");
        Assert.Equal(["delivery", "email", "role", "access_group_id", "library_ids", "create_profile", "show_tour", "note"], email.Select(pair => pair.Key));
        Assert.Equal("a@b.test", email["email"]!.GetValue<string>()); Assert.Equal("hi", email["note"]!.GetValue<string>());
        var link = AdminUserFormBodies.Invitation("link", "ignored@b.test", "admin", "g1", null, false, false, " ");
        Assert.Equal(["delivery", "role", "create_profile", "show_tour"], link.Select(pair => pair.Key));
        Assert.False(link["create_profile"]!.GetValue<bool>());
    }

    [Fact]
    public void TheInvitationResultSaysWhatHappenedToTheEmail()
    {
        Assert.Equal(("web.admin.settings.invitations.delivery.linkCreated", (string?)null), AdminUserFormBodies.InvitationDelivery(Json("""{"delivery_status":"not_requested","invitation":{"email":""}}""")));
        Assert.Equal(("web.admin.settings.invitations.delivery.newLinkForEmail", (string?)"a@b.test"), AdminUserFormBodies.InvitationDelivery(Json("""{"delivery_status":"not_requested","invitation":{"email":"a@b.test"}}""")));
        Assert.Equal(("web.admin.settings.invitations.delivery.sent", (string?)"a@b.test"), AdminUserFormBodies.InvitationDelivery(Json("""{"delivery_status":"sent","invitation":{"email":"a@b.test"}}""")));
        Assert.Equal("web.admin.settings.invitations.delivery.notConfigured", AdminUserFormBodies.InvitationDelivery(Json("""{"delivery_status":"not_configured","invitation":{}}""")).Key);
        Assert.Equal("web.admin.settings.invitations.delivery.failed", AdminUserFormBodies.InvitationDelivery(Json("""{"delivery_status":"failed","invitation":{}}""")).Key);
    }

    [Fact]
    public void InviteCodeUsesMustBeAPositiveWholeNumberWithinTheServerLimit()
    {
        Assert.Equal(5, AdminUserFormBodies.PositiveUses(" 5 "));
        Assert.Null(AdminUserFormBodies.PositiveUses("0")); Assert.Null(AdminUserFormBodies.PositiveUses("-1")); Assert.Null(AdminUserFormBodies.PositiveUses("1.5")); Assert.Null(AdminUserFormBodies.PositiveUses(""));
        Assert.Equal(2147483647, AdminUserFormBodies.PositiveUses("2147483647")); Assert.Null(AdminUserFormBodies.PositiveUses("2147483648"));
    }

    [Fact]
    public void InviteCodeBodiesCarryOnlyWhatTheFormOwns()
    {
        var create = AdminUserFormBodies.CodeCreate(" BETA ", " Testers ", 10);
        Assert.Equal(["code", "label", "max_uses"], create.Select(pair => pair.Key)); Assert.Equal("BETA", create["code"]!.GetValue<string>());
        var saved = Json("""{"id":"c1","code":"BETA","label":"Testers","max_uses":10,"enabled":true}""");
        Assert.Empty(AdminUserFormBodies.CodeUpdate(saved, "Testers", 10));
        Assert.Equal(["max_uses"], AdminUserFormBodies.CodeUpdate(saved, "Testers", 12).Select(pair => pair.Key));
        Assert.Equal(["label"], AdminUserFormBodies.CodeUpdate(saved, "Beta", 10).Select(pair => pair.Key));
        Assert.Equal(3, AdminUserFormBodies.CodeTopUp(3)["additional_uses"]!.GetValue<long>());
    }

    [Fact]
    public void AnIdentityKeepsTheExactSubjectAndOnlyTypedNames()
    {
        var body = AdminUserFormBodies.Identity("7", "  issuer|abc  ", "", " a@b.test ", "");
        Assert.Equal(["installation_id", "external_subject", "email"], body.Select(pair => pair.Key));
        Assert.Equal("issuer|abc", body["external_subject"]!.GetValue<string>());
        Assert.Equal(5, AdminUserFormBodies.Identity("7", "s", "u", "e", "d").Count);
    }
}
