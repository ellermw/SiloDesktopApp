using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Mirrors the current WebUI permissions policy. An admin account only acts as
/// admin while no profile is selected or while its primary profile is active;
/// non-primary profiles retain only explicitly assigned account permissions.
/// </summary>
public static class AuthorizationPolicy
{
    public const string MetadataCuration = "metadata_curation";
    public const string MarkerEdit = "marker_edit";

    public static bool IsActingAdmin(AuthService auth)
        => IsActingAdmin(auth.CurrentUser, auth.SelectedProfile);

    public static bool IsActingAdmin(UserInfo? user, Profile? profile)
        => string.Equals(user?.Role, "admin", StringComparison.OrdinalIgnoreCase)
           && (profile == null || profile.IsPrimary);

    public static bool HasPermission(AuthService auth, string permission)
        => HasPermission(auth.CurrentUser, auth.SelectedProfile, permission);

    public static bool HasPermission(UserInfo? user, Profile? profile, string permission)
        => IsActingAdmin(user, profile)
           || (user?.Permissions?.Contains(permission, StringComparer.OrdinalIgnoreCase) ?? false);

    public static bool CanCurateMetadata(AuthService auth)
        => HasPermission(auth, MetadataCuration);

    public static bool CanEditMarkers(AuthService auth)
        => HasPermission(auth, MarkerEdit);
}
