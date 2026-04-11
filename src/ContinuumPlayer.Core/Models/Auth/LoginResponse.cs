namespace ContinuumPlayer.Core.Models.Auth;
public class LoginResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public UserInfo User { get; set; } = new();
}
public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";

    /// <summary>
    /// Present when the current session is impersonating another user.
    /// Null when not impersonating. Populated from the JWT or /auth/me response.
    /// </summary>
    public ImpersonationInfo? Impersonation { get; set; }
}

/// <summary>
/// Info about an active impersonation session — who the admin is impersonating and who the
/// impersonator was. Matches the WebUI <c>User.impersonation</c> shape.
/// </summary>
public class ImpersonationInfo
{
    public bool Active { get; set; }
    public int ImpersonatorUserId { get; set; }
    public string ImpersonatorUsername { get; set; } = "";
}
