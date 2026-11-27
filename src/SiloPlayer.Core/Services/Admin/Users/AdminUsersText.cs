using System.Text.Json;

namespace SiloPlayer.Core.Services.Admin.Users;

/// <summary>
/// The official Users page wording: the <c>web.admin.users.*</c> messages of the pinned Silo WebUI catalog (with the invitation, invite-code, sign-in
/// card and signed-in session messages the same pages use), converted to plain ICU text. Wording the official pages do not have (native-only
/// actions and states) lives in <see cref="NativeOnly"/>, apart from the catalog copy.
/// </summary>
public static class AdminUsersText
{
    private static readonly IReadOnlyDictionary<string, string> English = Load();
    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = typeof(AdminUsersText).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Admin.admin-users.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    /// <summary>Wording for actions and states only the native page has, or that the official catalog words differently.</summary>
    public static readonly IReadOnlyDictionary<string, string> NativeOnly = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["admin.native.users.reload"] = "Reload",
        ["admin.native.users.reloadName"] = "Reload / review current server state",
        ["admin.native.users.sort"] = "Sort",
        ["admin.native.users.descending"] = "Descending",
        ["admin.native.users.rows"] = "Rows",
        ["admin.native.users.noRecords"] = "No records.",
        ["admin.native.users.noSnapshot"] = "No successful snapshot is loaded.",
        ["admin.native.users.loadMore"] = "Load next bounded page",
        ["admin.native.users.loadMoreGroups"] = "Load next bounded group page",
        ["admin.native.users.partial"] = "Partial list: {count} retained. Narrow filters when the 1,000-row/10-page bound is reached.",
        ["admin.native.users.protectedNote"] = "This server does not currently allow account management.",
        ["admin.native.users.exact.label"] = "Exact username or email",
        ["admin.native.users.invitations.unavailable"] = "Invitations are unavailable: {state}",
        ["admin.native.users.codes.unavailable"] = "Invite codes are unavailable: {state}",
        ["admin.native.users.codes.note"] = "Invite codes are sensitive. Copies are explicit; no generated values are logged.",
        ["admin.native.users.codes.copy"] = "Copy code",
        ["admin.native.users.codes.editTitle"] = "Edit invite code",
        ["admin.native.users.codes.unconfirmed"] = "We couldn't confirm that the invite code was saved. Close this window and check the list before trying again.",
        ["admin.native.users.codes.saveFailed"] = "Couldn't save the invite code.",
        ["admin.native.users.profileSaveFailed"] = "Couldn't save this profile.",
        ["admin.native.users.codes.edit"] = "Edit",
        ["admin.native.users.header.passwordLink"] = "Password reset link",
        ["admin.native.users.header.passwordEmail"] = "Email password reset",
        ["admin.native.users.access.editLimit"] = "Edit limit",
        ["admin.native.users.choicesFailed"] = "Couldn't load the choices for this form. Reload and try again.",
        ["admin.native.users.refreshedNotice"] = "This page was refreshed because something changed. Open it again.",
        ["admin.native.users.access.readOnly"] = "This access policy is read only. Only the server owner may change an admin policy, including their own.",
        ["admin.native.users.overview.partialLive"] = "Live statistics are partial or unavailable, not complete account counts.",
        ["admin.native.users.activity.applyFilters"] = "Apply",
        ["admin.native.users.activity.openDevice"] = "Open exact account/device scope",
        ["admin.native.users.downloads.partial"] = "Partial device copies. Counts and groups describe retained pages only.",
        ["admin.native.users.downloads.loadMore"] = "Load next device-copy page",
        ["admin.native.users.downloads.open"] = "Open Download Management for this account",
        ["admin.native.users.downloads.disclaimer"] = "Device reports are not live storage inspection. A revoked row is not proof the device has deleted its bytes.",
        ["admin.native.users.signin.passwordNote"] = "Setting a password re-enables local password sign-in. Linking a non-network external provider normally turns it off unless this is a break-glass admin. The server refuses removal of the last viable sign-in method or break-glass admin.",
        ["admin.native.users.signin.ownerOnly"] = "Only the Owner can re-enable their own disabled local password sign-in. Use the active external method or ask the Owner to recover this account.",
        ["admin.native.users.signin.setMyPassword"] = "Set my account password",
        ["admin.native.users.signin.setPassword"] = "Set account password",
        ["admin.native.users.signin.breakGlass"] = "Configure break-glass",
        ["admin.native.users.signin.linkIdentity"] = "Link external identity",
        ["admin.native.users.signin.loadProviders"] = "Load next provider page",
        ["admin.native.users.signin.identitiesUnsupported"] = "External identity management is unsupported or disallowed for this authority.",
        ["admin.native.users.signin.sessionsUnsupported"] = "Login-session administration is unsupported or disallowed. Device registration is a different capability.",
        ["admin.native.users.signin.signOutAll"] = "Sign out all login sessions",
        ["admin.native.users.signin.signOutOne"] = "Sign out this login session",
        ["admin.native.users.prefs.unavailable"] = "Preferences are unavailable for this authority/server.",
        ["admin.native.users.prefs.note"] = "Client-family order: TV / mobile / tablet / desktop / web. Every edit or reset includes all displayed identity qualifiers. An explicit null value is distinct from removing the stored row.",
        ["admin.native.users.prefs.chooseLevel"] = "Choose a new device/library/series level",
        ["admin.native.users.prefs.addSetting"] = "Add setting",
        ["admin.native.users.prefs.addSelected"] = "Add selected setting",
        ["admin.native.users.prefs.viewJson"] = "View JSON",
        ["admin.native.users.prefs.viewStored"] = "View stored JSON",
        ["admin.native.users.prefs.edit"] = "Edit",
        ["admin.native.users.prefs.afterReset"] = "After reset: {value}",
        ["admin.native.users.prefs.stored"] = "Empty value (stored)",
        ["admin.native.users.prefs.partial"] = "Partial settings: inherited labels may omit unloaded overrides. Add, edit and reset stay blocked until a complete bounded baseline is loaded.",
        ["admin.native.users.prefs.loadMore"] = "Load next settings page",
        ["admin.native.users.prefs.unknownCard"] = "Stored setting not editable in this manifest",
        ["admin.native.users.prefs.shown"] = "Shown",
        ["admin.native.users.prefs.hidden"] = "Hidden",
        ["admin.native.users.prefs.show"] = "Show",
        ["admin.native.users.prefs.hide"] = "Hide",
        ["admin.native.users.prefs.moveUp"] = "Move up",
        ["admin.native.users.prefs.moveDown"] = "Move down",
        ["admin.native.users.prefs.resetHome"] = "Reset this profile Home layout",
    };

    public static string T(string key, params (string Name, object? Value)[] args)
    {
        var values = args.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        if (NativeOnly.TryGetValue(key, out var native)) return args.Length == 0 ? native : AdminText.Format(native, values);
        return English.TryGetValue(key, out var pattern) ? AdminText.Format(pattern, values) : key;
    }
    public static bool Has(string key) => NativeOnly.ContainsKey(key) || English.ContainsKey(key);
}
