namespace SiloPlayer.Core.Services;

public static class ServerUrlIdentity
{
    public static string Normalize(string? serverUrl)
    {
        var trimmed = serverUrl?.Trim() ?? "";
        if (trimmed.Length == 0)
            return "";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return trimmed.TrimEnd('/');
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Query = "",
            Fragment = "",
            Path = uri.AbsolutePath.TrimEnd('/'),
        };

        if (uri.IsDefaultPort)
            builder.Port = -1;

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static IReadOnlyList<string> CredentialAliases(string? serverUrl)
    {
        var raw = serverUrl?.Trim() ?? "";
        var normalized = Normalize(raw);
        var aliases = new List<string>();

        Add(normalized);
        Add(raw);
        Add(raw.TrimEnd('/'));
        if (raw.Length > 0 && !raw.EndsWith('/'))
            Add(raw + "/");

        return aliases;

        void Add(string value)
        {
            if (value.Length > 0 && !aliases.Contains(value, StringComparer.Ordinal))
                aliases.Add(value);
        }
    }
}

public sealed record ResolvedSavedSession(
    string ServerUrl,
    string RefreshToken,
    bool MigratedLegacyCredential);

public sealed class SavedSessionCredentialResolver(ICredentialStore credentialStore)
{
    private static readonly string[] ProfileCredentialKeys = ["profile_id", "profile_token"];

    public ResolvedSavedSession? Resolve(string? serverUrl)
    {
        var canonicalUrl = ServerUrlIdentity.Normalize(serverUrl);
        if (canonicalUrl.Length == 0)
            return null;

        var aliases = ServerUrlIdentity.CredentialAliases(serverUrl);
        foreach (var alias in aliases)
        {
            var refreshToken = credentialStore.LoadCredential(alias, "refresh_token");
            if (string.IsNullOrWhiteSpace(refreshToken))
                continue;

            var migrated = !string.Equals(alias, canonicalUrl, StringComparison.Ordinal);
            if (migrated)
            {
                credentialStore.SaveCredential(canonicalUrl, "refresh_token", refreshToken);
                foreach (var key in ProfileCredentialKeys)
                {
                    var value = credentialStore.LoadCredential(alias, key);
                    if (!string.IsNullOrWhiteSpace(value))
                        credentialStore.SaveCredential(canonicalUrl, key, value);
                }

                credentialStore.DeleteCredential(alias, "refresh_token");
                foreach (var key in ProfileCredentialKeys)
                    credentialStore.DeleteCredential(alias, key);
            }

            foreach (var candidate in aliases)
                credentialStore.DeleteCredential(candidate, "access_token");

            return new ResolvedSavedSession(canonicalUrl, refreshToken, migrated);
        }

        foreach (var candidate in aliases)
            credentialStore.DeleteCredential(candidate, "access_token");
        return null;
    }
}
