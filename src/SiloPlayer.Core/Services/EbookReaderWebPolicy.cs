namespace SiloPlayer.Core.Services;

public static class EbookReaderWebPolicy
{
    public const string ReaderHost = "silo-reader.local";
    public const string BookHost = "silo-book.local";

    public static bool IsTrustedReaderUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.Equals(uri.Host, ReaderHost, StringComparison.OrdinalIgnoreCase) &&
               uri.IsDefaultPort &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    public static bool IsAllowedSubresource(string? value)
    {
        if (IsTrustedReaderUri(value))
            return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme == Uri.UriSchemeHttps && uri.Host == BookHost && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo))
            return true;
        if (uri.Scheme == "data")
            return true;
        if (uri.Scheme != "blob")
            return false;

        var embeddedOrigin = value!["blob:".Length..];
        return IsTrustedReaderUri(embeddedOrigin);
    }
}
