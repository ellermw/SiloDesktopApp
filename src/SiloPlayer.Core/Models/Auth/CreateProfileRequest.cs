namespace SiloPlayer.Core.Models.Auth;

public class CreateProfileRequest
{
    public string Name { get; set; } = "";
    public string? Pin { get; set; }
    public bool IsChild { get; set; }
}
