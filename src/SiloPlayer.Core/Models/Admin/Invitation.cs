namespace SiloPlayer.Core.Models.Admin;

public sealed class Invitation
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "user";
    public long? AccessGroupId { get; set; }
    public List<int> LibraryIds { get; set; } = [];
    public bool CreateProfile { get; set; }
    public bool ShowTour { get; set; }
    public string? Note { get; set; }
    public long InvitedBy { get; set; }
    public string? InvitedByName { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset ExpiresAt { get; set; }
    public string? AcceptedAt { get; set; }
    public long? AcceptedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateInvitationRequest
{
    public string Email { get; set; } = "";
    public string Role { get; set; } = "user";
    public long? AccessGroupId { get; set; }
    public List<int>? LibraryIds { get; set; }
    public bool CreateProfile { get; set; } = true;
    public bool ShowTour { get; set; } = true;
    public string? Note { get; set; }
}

public sealed class SendInvitationResponse
{
    public Invitation Invitation { get; set; } = new();
    public bool EmailSent { get; set; }
    public string? ClaimUrl { get; set; }
}
