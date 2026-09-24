namespace SiloPlayer.Core.Models.Auth;

public sealed class AccountPasswordCapability
{
    public string State { get; set; } = "";
    public bool Allowed { get; set; }
    public bool RequiresCurrentPassword { get; set; } = true;
    public int MinimumPasswordLength { get; set; }
    public int MaximumPasswordBytes { get; set; }
}
