using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Every editor label and title is derived from the controller's current snapshot at render time.
/// Adopting a newer reviewed revision replaces that snapshot, so nothing here may be cached by the page.
/// </summary>
public static class AdminPolicyPresentation
{
    public static string Title(AdminPolicySnapshot snapshot) => snapshot.Value.Name;
    public static string OverrideCan(AdminPolicySnapshot snapshot) => AdminText.T("web.admin.access.policy.domain." + snapshot.Value.Domain + ".overrideCan");
    public static string ToggleLabel(AdminPolicySnapshot snapshot) =>
        AdminText.T(snapshot.Value.Enabled ? "web.admin.access.policy.enabled.disable" : "web.admin.access.policy.enabled.enable");
    public static string Status(AdminPolicyDocument document) =>
        AdminText.T(document.Enabled && document.ActiveVersionId != null ? "web.admin.access.policy.status.active"
            : document.Enabled ? "web.admin.access.policy.status.draft" : "web.admin.access.policy.status.disabled");
    public static string EnabledState(AdminPolicySnapshot snapshot) => snapshot.Value.Enabled ? "enabled" : "disabled";
    public static string HasVersion(AdminPolicySnapshot snapshot) => snapshot.Value.ActiveVersionId != null ? "yes" : "no";
}
