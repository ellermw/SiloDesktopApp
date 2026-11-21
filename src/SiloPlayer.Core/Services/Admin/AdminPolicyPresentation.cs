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
    /// <summary>Number of lifecycle stages already reached: 1 draft, 2 validated, 3 saved, 4 active (live).</summary>
    public static int LifecycleProgress(AdminPolicySnapshot snapshot, AdminPolicyVersion? saved, string draft, bool validated)
    {
        var current = saved is { CompiledOk: true } && saved.Source == draft;
        if (current && snapshot.Value.ActiveVersionId == saved!.Id) return 4;
        if (current) return 3;
        return validated ? 2 : 1;
    }
    public static string LifecycleRail(int reached, int? liveVersion)
    {
        var labels = new[]
        {
            AdminText.T("web.admin.access.policy.status.draft"), AdminText.T("web.admin.access.policy.editor.stage.validated"), AdminText.T("web.admin.access.policy.editor.stage.saved"),
            reached >= 4 && liveVersion != null ? AdminText.T("web.admin.access.policy.editor.stage.activeVersion", new { version = liveVersion }) : AdminText.T("web.admin.access.policy.status.active"),
        };
        return string.Join(" → ", labels.Select((label, index) => (index < reached ? "✓ " : "") + label));
    }
}
