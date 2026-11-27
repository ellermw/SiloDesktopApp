using System.Globalization;
using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

/// <summary>The request bodies of the invitation, invite-code and sign-in identity forms, as the official forms build them.</summary>
public static class AdminUserFormBodies
{
    /// <summary>
    /// Create invitation (official <c>CreateInvitationForm</c>): the delivery, the address only for an email, the role, the group unless the role is admin (admins are never grouped),
    /// the libraries only when chosen (omitted inherits), whether to create a profile, the tour, and the note only when it has text.
    /// </summary>
    public static JsonObject Invitation(string delivery, string email, string role, string? accessGroupId, IReadOnlyList<string>? libraryIds, bool createProfile, bool showTour, string note)
    {
        var body = new JsonObject { ["delivery"] = delivery };
        if (delivery == "email") body["email"] = email.Trim();
        body["role"] = role;
        if (AdminUserCreatePolicy.EffectiveAccessGroupId(role, accessGroupId) is { } group) body["access_group_id"] = group;
        if (libraryIds != null) body["library_ids"] = new JsonArray(libraryIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        body["create_profile"] = createProfile;
        body["show_tour"] = showTour;
        if (note.Trim().Length > 0) body["note"] = note.Trim();
        return body;
    }

    /// <summary>The words the invitation result starts with (official <c>deliveryMessage</c>), as a key and its arguments.</summary>
    public static (string Key, string? Email) InvitationDelivery(JsonObject result)
    {
        var status = result.Text("delivery_status");
        var email = (result["invitation"] as JsonObject)?.Text("email") ?? "";
        if (status == "not_requested" && email.Length > 0) return ("web.admin.settings.invitations.delivery.newLinkForEmail", email);
        return status switch
        {
            "not_requested" => ("web.admin.settings.invitations.delivery.linkCreated", null),
            "sent" => ("web.admin.settings.invitations.delivery.sent", email),
            "not_configured" => ("web.admin.settings.invitations.delivery.notConfigured", null),
            _ => ("web.admin.settings.invitations.delivery.failed", null),
        };
    }

    public const long MaxUsesLimit = 2147483647;

    /// <summary>A whole number from 1 to the server's limit, else null.</summary>
    public static long? PositiveUses(string text) => long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= MaxUsesLimit ? value : null;

    public static JsonObject CodeCreate(string code, string label, long maxUses) => new() { ["code"] = code.Trim(), ["label"] = label.Trim(), ["max_uses"] = maxUses };

    /// <summary>Edit invite code: only the label and the number of uses that changed.</summary>
    public static JsonObject CodeUpdate(JsonObject saved, string label, long maxUses)
    {
        var body = new JsonObject();
        if (label.Trim() != saved.Text("label")) body["label"] = label.Trim();
        if (maxUses != saved.Count("max_uses")) body["max_uses"] = maxUses;
        return body;
    }

    public static JsonObject CodeTopUp(long additionalUses) => new() { ["additional_uses"] = additionalUses };

    /// <summary>Link external identity: the exact subject (only outer whitespace from a paste is dropped) and the optional names only when typed.</summary>
    public static JsonObject Identity(string installationId, string subject, string username, string email, string displayName)
    {
        var body = new JsonObject { ["installation_id"] = installationId, ["external_subject"] = subject.Trim() };
        if (username.Trim().Length > 0) body["username"] = username.Trim();
        if (email.Trim().Length > 0) body["email"] = email.Trim();
        if (displayName.Trim().Length > 0) body["display_name"] = displayName.Trim();
        return body;
    }
}
