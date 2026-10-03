using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>Owns one temporary-password session. A successful password write is never replayed.</summary>
public sealed class RequiredPasswordTransition
{
    private readonly AuthService _auth;
    private readonly SiloApiClient _client;
    private readonly long _generation;
    private readonly string _server;
    private readonly string _userId;
    private bool _busy;
    public bool PasswordSaved { get; private set; }

    public RequiredPasswordTransition(AuthService auth, SiloApiClient client)
    {
        _auth = auth; _client = client;
        _generation = auth.SessionGeneration;
        _server = client.BaseUrl;
        _userId = auth.CurrentUser?.Id ?? "";
    }

    private void CheckCurrent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_auth.SessionGeneration != _generation || _client.BaseUrl != _server ||
            _auth.CurrentUser?.Id != _userId)
            throw new OperationCanceledException("The sign-in session changed.");
    }

    public async Task<bool> SaveAsync(string temporaryPassword, string password, string confirmation, CancellationToken ct = default)
    {
        if (_busy) return false;
        CheckCurrent(ct);
        if (!PasswordSaved && !_auth.PasswordChangeRequired)
            throw new InvalidOperationException("This account does not require a password change.");
        if (!PasswordSaved && (password.Length < 8 || password != confirmation || string.IsNullOrEmpty(temporaryPassword)))
            throw new InvalidOperationException("Enter your temporary password and matching new passwords of at least 8 characters.");
        _busy = true;
        try
        {
            if (!PasswordSaved)
            {
                var token = _client.AccessToken ?? throw new InvalidOperationException("Sign in again.");
                await _client.PostNoContentWithBearerAsync(_server, "/api/v2/account/password", token,
                    new { current_password = temporaryPassword, new_password = password }, ct);
                CheckCurrent(ct);
                PasswordSaved = true;
            }
            // Renew the restricted token, then require a fresh authoritative account read.
            if (!await _auth.TryRefreshAsync(ct))
                throw new InvalidOperationException("Password changed. Retry to finish signing in, or sign in with the new password.");
            CheckCurrent(ct);
            var user = await new AuthApi(_client).GetMeAsync(_server, _client.AccessToken!, ct);
            CheckCurrent(ct);
            if (user.Id != _userId || user.PasswordChangeRequired)
                throw new InvalidOperationException("Password changed, but the server has not confirmed the completed sign-in. Retry.");
            return _auth.SetCurrentUser(user, _generation);
        }
        finally { _busy = false; }
    }
}
