namespace ContinuumPlayer.Core.Helpers;

public static class UrlHelper
{
    /// <summary>
    /// Appends an auth token as a query parameter to a URL.
    /// Returns the URL unchanged if token is null.
    /// </summary>
    public static string AppendToken(string url, string? token)
    {
        if (token == null) return url;
        return url + (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
    }
}
