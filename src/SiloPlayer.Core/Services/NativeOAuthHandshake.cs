using System.Security.Cryptography;
using System.Text;

namespace SiloPlayer.Core.Services;

/// <summary>One native OAuth attempt pinned to the server on which it started.</summary>
public sealed class NativeOAuthHandshake
{
    private readonly object _gate = new();
    private bool _consumed;
    public string ServerBase { get; }
    public string ServerId { get; }
    public string State { get; }
    public string CodeVerifier { get; }
    public Uri StartUri { get; }
    public bool IsLinking { get; }
    private readonly string _origin;

    public NativeOAuthHandshake(string serverBase, string serverId, int installationId,
        string nativeStartPath, bool selectAccount = false, string? linkTicket = null)
    {
        if (!Uri.TryCreate(serverBase, UriKind.Absolute, out var server) ||
            server.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(server.UserInfo) ||
            !string.IsNullOrEmpty(server.Query) || !string.IsNullOrEmpty(server.Fragment) ||
            string.IsNullOrWhiteSpace(serverId) || installationId <= 0 ||
            nativeStartPath != $"/api/v2/auth/oauth/{installationId}/native/start")
            throw new ArgumentException("The provider does not offer native sign-in at this server.");
        ServerBase = server.AbsoluteUri.TrimEnd('/'); ServerId = serverId;
        _origin = Origin(server); IsLinking = !string.IsNullOrEmpty(linkTicket);
        CodeVerifier = Encode(RandomNumberGenerator.GetBytes(32));
        State = Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier)));
        var query = $"code_challenge={challenge}&code_challenge_method=S256&app_state={State}";
        if (selectAccount) query += "&prompt=select_account";
        if (IsLinking) query += "&link_ticket=" + Uri.EscapeDataString(linkTicket!);
        StartUri = new Uri(ServerBase + nativeStartPath + "?" + query);
    }

    /// <summary>Rejects foreign or malformed callbacks without consuming this attempt.</summary>
    public bool TryConsumeCallback(string? uriText, out NativeOAuthCallback callback)
    {
        callback = new("", "");
        if (!ValidEscapes(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ||
            uri.Scheme != "org.siloserver.silo" || uri.Host.Length != 0 ||
            uri.AbsolutePath != "/auth/callback" || uri.Fragment.Length != 0 ||
            !TryQuery(uri.Query, out var values) ||
            values.GetValueOrDefault("iss") != _origin ||
            values.GetValueOrDefault("server") != ServerId ||
            !FixedEquals(values.GetValueOrDefault("state"), State)) return false;
        var code = values.GetValueOrDefault("code") ?? "";
        var error = values.GetValueOrDefault("error") ?? "";
        if (string.IsNullOrWhiteSpace(code) == string.IsNullOrWhiteSpace(error)) return false;
        // The official server's linking success carries link=1; failures carry
        // only error/state/iss/server and cannot redeem either kind of code.
        if (!string.IsNullOrWhiteSpace(code) &&
            (IsLinking ? values.GetValueOrDefault("link") != "1" : values.ContainsKey("link"))) return false;
        if (!string.IsNullOrWhiteSpace(error) && values.ContainsKey("link")) return false;
        lock (_gate)
        {
            if (_consumed) return false;
            _consumed = true; callback = new(code, error); return true;
        }
    }

    public void Cancel() { lock (_gate) _consumed = true; }
    private static string Origin(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant();
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool FixedEquals(string? actual, string expected) => actual != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    private static bool ValidEscapes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        for (var n = 0; n < text.Length; n++)
            if (text[n] == '%') { if (n + 2 >= text.Length || !Uri.IsHexDigit(text[n + 1]) || !Uri.IsHexDigit(text[n + 2])) return false; n += 2; }
        return true;
    }
    private static bool TryQuery(string query, out Dictionary<string, string> values)
    {
        values = new(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2 || !values.TryAdd(Uri.UnescapeDataString(pieces[0]), Uri.UnescapeDataString(pieces[1].Replace('+', ' ')))) return false;
        }
        return true;
    }
}

public sealed record NativeOAuthCallback(string Code, string Error);
