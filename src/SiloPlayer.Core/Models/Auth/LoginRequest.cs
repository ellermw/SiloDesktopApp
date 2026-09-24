namespace SiloPlayer.Core.Models.Auth;
public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; set; }
}
