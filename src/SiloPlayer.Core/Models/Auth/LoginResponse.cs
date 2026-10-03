namespace SiloPlayer.Core.Models.Auth;
public class LoginResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public UserInfo User { get; set; } = new();
}
public class UserInfo
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public bool DownloadAllowed { get; set; }
    public bool PasswordChangeRequired { get; set; }
    public AccountImpersonation? Impersonation { get; set; }

}
