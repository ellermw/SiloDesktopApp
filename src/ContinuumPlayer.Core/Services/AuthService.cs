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

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _authApi.LoginAsync(username, password, ct);
        _apiClient.SetAccessToken(response.AccessToken);
        RefreshToken = response.RefreshToken;
        CurrentUser = response.User;
        ScheduleRefresh(response.ExpiresIn);
        return response;
    }

    public void SetTokens(string accessToken, string refreshToken, int expiresIn)
    {
        _apiClient.SetAccessToken(accessToken);
        RefreshToken = refreshToken;
        ScheduleRefresh(expiresIn);
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
            TokenRefreshed?.Invoke();
            return true;
        }
        catch
        {
            Logout();
            return false;
        }
    }

    private void ScheduleRefresh(int expiresInSeconds)
    {
        _refreshTimer?.Dispose();
        var refreshIn = TimeSpan.FromSeconds(expiresInSeconds * 0.8);
        _refreshTimer = new Timer(async _ => { await TryRefreshAsync(); }, null, refreshIn, Timeout.InfiniteTimeSpan);
    }
}
