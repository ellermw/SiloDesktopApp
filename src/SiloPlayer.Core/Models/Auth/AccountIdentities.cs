using System.Text.Json.Serialization;
namespace SiloPlayer.Core.Models.Auth;
public sealed class AccountIdentity
{
    public string Id { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int InstallationId { get; set; }
    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset? LinkedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
}
public sealed class AccountIdentityCollection
{
    public List<AccountIdentity> Items { get; set; } = [];
    public bool CanUnlink { get; set; }
}
public sealed class AccountIdentityLinkTicket
{
    public string Ticket { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class AccountImpersonation
{
    public bool Active { get; set; }
}
