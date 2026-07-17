namespace SiloPlayer.Core.Models.Auth;

public class ImpersonationResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public UserInfo User { get; set; } = new();
}
