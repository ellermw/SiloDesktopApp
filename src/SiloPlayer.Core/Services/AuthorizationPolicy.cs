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

    public static bool IsActingAdmin(AuthService auth)
        => IsActingAdmin(
            auth.CurrentUser,
            auth.SelectedProfile,
            !string.IsNullOrWhiteSpace(auth.SelectedProfileId));

    /// <summary>
    /// The explicit selected-profile flag mirrors the WebUI's fail-closed
    /// hasSelectedProfile check. A persisted profile id whose profile record
    /// has not resolved yet must never briefly restore account-level admin
    /// powers.
    /// </summary>
    public static bool IsActingAdmin(UserInfo? user, Profile? profile, bool hasSelectedProfile)
        => string.Equals(user?.Role, "admin", StringComparison.OrdinalIgnoreCase)
           && (!hasSelectedProfile || profile?.IsPrimary == true);

    public static bool HasPermission(AuthService auth, string permission)
        => IsActingAdmin(auth)
           || (auth.CurrentUser?.Permissions?.Contains(permission, StringComparer.OrdinalIgnoreCase) ?? false);

    public static bool HasPermission(
        UserInfo? user,
        Profile? profile,
        bool hasSelectedProfile,
        string permission)
        => IsActingAdmin(user, profile, hasSelectedProfile)
           || (user?.Permissions?.Contains(permission, StringComparer.OrdinalIgnoreCase) ?? false);

    public static bool CanCurateMetadata(AuthService auth)
        => HasPermission(auth, MetadataCuration);
}
