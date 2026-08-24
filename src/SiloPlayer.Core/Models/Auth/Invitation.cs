namespace SiloPlayer.Core.Models.Auth;

/// <summary>
/// Public invitation preview returned before an account exists. This mirrors
/// the current WebUI InvitationLookupResponse contract.
/// </summary>
public sealed class InvitationLookupResponse
{
    public string Email { get; set; } = "";
    public string? InviterName { get; set; }
    public string ServerName { get; set; } = "Silo";
    public string ExpiresAt { get; set; } = "";
    public bool ShowTour { get; set; }
}

public sealed class AcceptInvitationRequest
{
    public string Password { get; set; } = "";
}
