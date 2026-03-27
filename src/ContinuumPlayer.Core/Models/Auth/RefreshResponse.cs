namespace ContinuumPlayer.Core.Models.Auth;
public class RefreshResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
}
