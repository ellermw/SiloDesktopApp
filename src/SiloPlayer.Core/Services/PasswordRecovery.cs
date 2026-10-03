using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

public sealed class PasswordResetLookup
{
    public string Username { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}
public sealed class PasswordResetCompletion
{
    public string Status { get; set; } = "";
    public string Username { get; set; } = "";
    public string LoginStatus { get; set; } = "";
    public LoginResponse? Tokens { get; set; }
}
public sealed class PasswordResetCapability { public string State { get; set; } = ""; }

/// <summary>Public recovery never inherits an account token or replays a spent link.</summary>
public sealed class PasswordRecovery
{
    private readonly AuthService _auth;
    private readonly SiloApiClient _client;
    private readonly long _generation;
    private readonly ApiRequestContext _context;
    private string? _verifiedToken;
    private bool _busy;
    public bool NeedsReload { get; private set; }

    public PasswordRecovery(AuthService auth, SiloApiClient client)
    { _auth = auth; _client = client; _generation = auth.SessionGeneration; _context = client.CaptureContext(); }
    private void Check(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_auth.SessionGeneration != _generation || !_client.IsCurrentContext(_context) || _client.AccessToken != null)
            throw new OperationCanceledException("Sign out before using a password reset link.");
    }
    /// <summary>Only a full selected-server WebUI reset URL is an activation route. Raw tokens belong in the form.</summary>
    public static bool TryParseActivation(string? argument, string selectedServer, out string link)
    {
        link = "";
        var candidate = argument?.Trim().Trim('"');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(selectedServer)) return false;
        try
        {
            _ = ParseToken(candidate!, selectedServer);
            link = candidate!;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UriFormatException)
        {
            return false;
        }
    }
    public static string ParseToken(string value, string server)
    {
        value = value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var link))
        {
            var origin = new Uri(server.TrimEnd('/') + "/");
            var prefix = origin.AbsolutePath.TrimEnd('/') + "/reset-password/";
            if (link.Scheme != origin.Scheme || link.Authority != origin.Authority ||
                !link.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || link.UserInfo.Length != 0)
                throw new InvalidOperationException("Use a reset link from the selected server.");
            value = Uri.UnescapeDataString(link.AbsolutePath[prefix.Length..]);
        }
        if (value.Length == 0 || value.Length > 1024 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidOperationException("Paste a valid password reset link or token.");
        return value;
    }
    public async Task<bool> CanRequestAsync(CancellationToken ct = default)
    {
        Check(ct);
        var result = await _client.GetUnauthenticatedAsync<PasswordResetCapability>("/api/v2/capabilities/password-reset", ct);
        Check(ct); return result.State == "available";
    }
    public async Task RequestAsync(string login, CancellationToken ct = default)
    {
        Check(ct);
        if (string.IsNullOrWhiteSpace(login)) throw new InvalidOperationException("Enter your username or email.");
        if (!await CanRequestAsync(ct)) throw new InvalidOperationException("Email recovery is unavailable. Ask your administrator for a reset link.");
        await _client.PostUnauthenticatedNoContentAsync(_context.BaseUrl, "/api/v2/password-resets", new { login = login.Trim() }, ct);
        Check(ct);
    }
    public async Task<PasswordResetLookup> LookupAsync(string link, CancellationToken ct = default)
    {
        Check(ct); _verifiedToken = null;
        var token = ParseToken(link, _context.BaseUrl);
        var result = await _client.GetUnauthenticatedAsync<PasswordResetLookup>($"/api/v2/password-resets/{Uri.EscapeDataString(token)}", ct);
        Check(ct);
        if (string.IsNullOrWhiteSpace(result.Username) || string.IsNullOrWhiteSpace(result.ServerName) ||
            !DateTimeOffset.TryParse(result.ExpiresAt, out _)) throw new InvalidOperationException("The server returned an incomplete reset link.");
        _verifiedToken = token; NeedsReload = false;
        return result;
    }
    public async Task<PasswordResetCompletion> CompleteAsync(string link, string password, string confirmation, CancellationToken ct = default)
    {
        Check(ct);
        if (_busy || NeedsReload || _verifiedToken == null || _verifiedToken != ParseToken(link, _context.BaseUrl))
            throw new InvalidOperationException("Reload this link before saving a password.");
        if (password.Length < 8 || password != confirmation)
            throw new InvalidOperationException("Enter matching passwords of at least 8 characters.");
        _busy = true;
        try
        {
            var result = await _client.PostUnauthenticatedAsync<PasswordResetCompletion>(_context.BaseUrl,
                $"/api/v2/password-resets/{Uri.EscapeDataString(_verifiedToken)}/complete", new { password }, ct);
            Check(ct);
            NeedsReload = true; _verifiedToken = null;
            if (result.Status != "completed" || string.IsNullOrEmpty(result.Username))
                throw new InvalidOperationException("The server did not confirm the password change.");
            if (result.LoginStatus == "sign_in_required" && result.Tokens == null) return result;
            var pair = result.Tokens;
            if (result.LoginStatus != "signed_in" || pair == null || string.IsNullOrEmpty(pair.AccessToken) ||
                string.IsNullOrEmpty(pair.RefreshToken) || pair.User.Username != result.Username ||
                !_auth.CompleteRecoveryLogin(pair, _generation, _context.BaseUrl))
                throw new InvalidOperationException("Password changed. Sign in using the new password.");
            return result;
        }
        catch (ApiException ex) when (ex.ErrorCode == "validation_failed") { throw; }
        catch { NeedsReload = true; _verifiedToken = null; throw; }
        finally { _busy = false; }
    }
}
