namespace SiloPlayer.Core.Models.Auth;

/// <summary>
/// Public invitation preview returned before an account exists. This mirrors
/// the current WebUI InvitationLookupResponse contract.
/// </summary>
public sealed class InvitationLookupResponse
{
    public string Email { get; set; } = "";
    /// <summary>True for a link invitation bound to no address; the invitee enters theirs when accepting.</summary>
    public bool EmailRequired { get; set; }
    public string? Note { get; set; }
    public string? InviterName { get; set; }
    public string ServerName { get; set; } = "Silo";
    public string ExpiresAt { get; set; } = "";
    public bool ShowTour { get; set; }
    public bool AcceptanceAvailable { get; set; }
}

public sealed class InvitationAcceptanceResponse
{
    public string Status { get; set; } = "";
    public string LoginStatus { get; set; } = "";
    public string Username { get; set; } = "";
    public LoginResponse? Tokens { get; set; }
}

/// <summary>Public <c>GET /api/v2/invitations/capabilities</c>.</summary>
public sealed class InvitationCapabilitiesResponse
{
    public string State { get; set; } = "";
    public bool DefaultProfile { get; set; }
    public bool Profileless { get; set; }
    /// <summary>
    /// Whether the body-token lookup/accept operations exist. Servers that predate them omit the field,
    /// so an absent value reads as false and the deprecated path operations are used there.
    /// </summary>
    public bool TokenInBody { get; set; }
}

/// <summary>Deprecated path-token acceptance body; the token is in the URL, so only legacy servers see it.</summary>
public sealed class AcceptInvitationRequest
{
    public string Password { get; set; } = "";
    public string? Email { get; set; }
}
