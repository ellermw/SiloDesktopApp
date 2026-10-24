using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

public sealed record ConnectAppsSignInChoice(string Kind, AuthProvider? Provider = null, string Username = "", string ProviderName = "");

/// <summary>Resolves the sign-in flow actually offered by current server discovery.</summary>
public static class ConnectAppsSignIn
{
    public static ConnectAppsSignInChoice Resolve(bool accountPasswordWorks, bool passwordFormShown,
        IReadOnlyList<AuthProvider> providers, IReadOnlyList<AccountIdentity> identities)
    {
        if (accountPasswordWorks && passwordFormShown) return new("password");
        ConnectAppsSignInChoice? network = null;
        foreach (var identity in identities)
        {
            // Disabled providers deliberately clear provider_id in account identities.
            if (string.IsNullOrEmpty(identity.ProviderId)) continue;
            var provider = providers.FirstOrDefault(entry => entry.Id == identity.ProviderId);
            if (provider?.Mode == "oauth") return new("provider", provider);
            if (provider?.Mode == "credentials" && passwordFormShown) return new("directory", provider, identity.Username);
            if (provider == null || provider.Mode == "network") network ??= new("network", ProviderName: provider?.DisplayName ?? identity.ProviderName);
        }
        return network ?? new("none");
    }
}
