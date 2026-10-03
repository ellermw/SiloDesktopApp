using SiloPlayer.Core.Models.Auth;
namespace SiloPlayer.Core.Api;
public partial class AuthApi
{
    public Task<AccountIdentityCollection> GetAccountIdentitiesAsync(ApiRequestContext context, CancellationToken ct = default)
        => client.SendRequestAsync<AccountIdentityCollection>(context, HttpMethod.Get, "/api/v2/account/identities", null, ct);
    public Task<AccountIdentity> LinkAccountIdentityWithCredentialsAsync(ApiRequestContext context, int installationId,
        string password, string username, string directoryPassword, CancellationToken ct = default)
        => client.SendRequestWithoutRefreshAsync<AccountIdentity>(context, HttpMethod.Post,
            "/api/v2/account/identities/link-credentials", new { installation_id = installationId.ToString(System.Globalization.CultureInfo.InvariantCulture), password, username, directory_password = directoryPassword }, ct);
    public Task<AccountIdentityLinkTicket> CreateAccountIdentityLinkTicketAsync(ApiRequestContext context, int installationId,
        string password, CancellationToken ct = default)
        => client.SendRequestWithoutRefreshAsync<AccountIdentityLinkTicket>(context, HttpMethod.Post,
            "/api/v2/account/identities/link-ticket", new { installation_id = installationId.ToString(System.Globalization.CultureInfo.InvariantCulture), password }, ct);
    public Task CompleteAccountIdentityLinkAsync(ApiRequestContext context, string code, string codeVerifier, CancellationToken ct = default)
        => client.SendNoContentRequestWithoutRefreshAsync(context, HttpMethod.Post, "/api/v2/account/identities/link-complete",
            new { code, code_verifier = codeVerifier }, ct);
    public Task UnlinkAccountIdentityAsync(ApiRequestContext context, string id, CancellationToken ct = default)
        => client.SendNoContentRequestWithoutRefreshAsync(context, HttpMethod.Delete,
            "/api/v2/account/identities/" + Uri.EscapeDataString(id), null, ct);
}
