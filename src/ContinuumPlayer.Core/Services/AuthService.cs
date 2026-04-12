using System.Text;
using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;

namespace ContinuumPlayer.Core.Services;

public class AuthService
{
    private readonly ContinuumApiClient _apiClient;
    private readonly AuthApi _authApi;
    private Timer? _refreshTimer;

    public AuthService(ContinuumApiClient apiClient, AuthApi authApi)
    {
        _apiClient = apiClient;
        _authApi = authApi;
    }

    public bool IsLoggedIn => CurrentUser != null;
    public UserInfo? CurrentUser { get; private set; }
    public string? RefreshToken { get; private set; }
    public string? SelectedProfileId { get; private set; }

    public event Action? LoggedOut;
    public event Action? TokenRefreshed;
    /// <summary>
    /// Fires whenever CurrentUser transitions from null (or a different user)
    /// to a non-null authenticated user. Subscribers should re-evaluate any
    /// role-gated UI (e.g. admin-only indicators) that may have mounted before
    /// login completed.
    /// </summary>
    public event Action? UserChanged;

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _authApi.LoginAsync(username, password, ct);
        _apiClient.SetAccessToken(response.AccessToken);
        RefreshToken = response.RefreshToken;
        CurrentUser = response.User;
        ScheduleRefresh(response.ExpiresIn);
        UserChanged?.Invoke();
        return response;
    }

    public void SetTokens(string accessToken, string refreshToken, int expiresIn)
    {
        _apiClient.SetAccessToken(accessToken);
        RefreshToken = refreshToken;
        ScheduleRefresh(expiresIn);
    }

    public void SetCurrentUser(UserInfo user)
    {
        CurrentUser = user;
        UserChanged?.Invoke();
    }

    public void SelectProfile(string profileId, string? profileToken = null)
    {
        SelectedProfileId = profileId;
        _apiClient.SetProfile(profileId, profileToken);
    }

    public void Logout()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _apiClient.ClearAuth();
        CurrentUser = null;
        RefreshToken = null;
        SelectedProfileId = null;
        LoggedOut?.Invoke();
        UserChanged?.Invoke();
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        if (RefreshToken == null) return false;
        try
        {
            var response = await _authApi.RefreshAsync(RefreshToken, ct);
            _apiClient.SetAccessToken(response.AccessToken);
            RefreshToken = response.RefreshToken;
            ScheduleRefresh(response.ExpiresIn);

            // Extract user info from JWT claims if not already set (thread-safe check)
            var currentUser = CurrentUser;
            bool userChanged = false;
            if (currentUser == null)
            {
                var user = TryParseUserFromJwt(response.AccessToken);
                if (user != null)
                {
                    CurrentUser = user;
                    userChanged = true;
                }
            }

            TokenRefreshed?.Invoke();
            // Must fire UserChanged AFTER setting CurrentUser so subscribers
            // (e.g. ServerActivityButton) observe the new admin state — without
            // this, auto-login leaves role-gated UI stuck at null until the next
            // polling tick catches up.
            if (userChanged)
                UserChanged?.Invoke();
            return true;
        }
        catch
        {
            Logout();
            return false;
        }
    }

    /// <summary>Extracts user info (username, role) from a JWT access token payload.</summary>
    private static UserInfo? TryParseUserFromJwt(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            // JWT payload is base64url-encoded
            var payload = parts[1];
            // Pad to multiple of 4
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var user = new UserInfo();

            if (root.TryGetProperty("sub", out var sub))
                user.Username = sub.GetString() ?? "";
            if (root.TryGetProperty("username", out var username))
                user.Username = username.GetString() ?? "";
            if (root.TryGetProperty("role", out var role))
                user.Role = role.GetString() ?? "user";
            if (root.TryGetProperty("user_id", out var userId) && userId.TryGetInt32(out var uid))
                user.Id = uid;

            return user;
        }
        catch
        {
            return null;
        }
    }

    private void ScheduleRefresh(int expiresInSeconds)
    {
        _refreshTimer?.Dispose();
        if (expiresInSeconds <= 0) return;
        var refreshIn = TimeSpan.FromSeconds(expiresInSeconds * 0.8);
        _refreshTimer = new Timer(async _ =>
        {
            try { await TryRefreshAsync(); }
            catch { /* TryRefreshAsync handles failure internally via Logout() */ }
        }, null, refreshIn, Timeout.InfiniteTimeSpan);
    }
}
