using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

public class AuthService : IDisposable
{
    private readonly SiloApiClient _apiClient;
    private readonly AuthApi _authApi;
    private readonly ICredentialStore? _credentialStore;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _stateGate = new();
    private readonly object _credentialGate = new();
    private CancellationTokenSource? _refreshScheduleCancellation;
    private Task? _refreshScheduleTask;
    private readonly SemaphoreSlim _refreshGuard = new(1, 1);
    private long _sessionGeneration;
    private string? _sessionServerUrl;
    private PreservedAdminSession? _preservedAdminSession;
    private static readonly TimeSpan TransientRefreshRetryDelay = TimeSpan.FromSeconds(30);
    private const string ImpersonationAdminRefreshKey = "impersonation_admin_refresh_token";
    private const string ImpersonationReturnPathKey = "impersonation_return_path";

    private sealed record PreservedAdminSession(
        string? AccessToken,
        string RefreshToken,
        UserInfo? User,
        string ReturnPath);

    public AuthService(SiloApiClient apiClient, AuthApi authApi, ICredentialStore? credentialStore = null)
        : this(apiClient, authApi, credentialStore, Task.Delay)
    {
    }

    public AuthService(
        SiloApiClient apiClient,
        AuthApi authApi,
        ICredentialStore? credentialStore,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        _apiClient = apiClient;
        _authApi = authApi;
        _credentialStore = credentialStore;
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    public bool IsLoggedIn => CurrentUser != null;
    public string ConfiguredServerUrl => _apiClient.BaseUrl;
    public UserInfo? CurrentUser { get; private set; }
    public string? RefreshToken { get; private set; }
    public string? SelectedProfileId { get; private set; }
    public Profile? SelectedProfile { get; private set; }
    public bool IsImpersonating => CurrentUser?.Impersonation?.Active == true;

    public event Action? LoggedOut;
    public event Action? TokenRefreshed;
    public event Action? ProfileVerificationRequired;
    public event Action<Exception>? CredentialStoreFailed;
    /// <summary>
    /// Fires whenever CurrentUser transitions from null (or a different user)
    /// to a non-null authenticated user. Subscribers should re-evaluate any
    /// role-gated UI (e.g. admin-only indicators) that may have mounted before
    /// login completed.
    /// </summary>
    public event Action? UserChanged;

    public void ConfigureServer(string serverUrl)
    {
        var normalized = ServerUrlIdentity.Normalize(serverUrl);
        if (string.Equals(_apiClient.BaseUrl, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        // Switching servers invalidates only live in-memory state. Saved credentials
        // remain scoped to the old server so the user can return to it later.
        ClearSession(
            expectedGeneration: null,
            expectedRefreshToken: null,
            deletePersistedCredentials: false,
            notifyListeners: false);
        _apiClient.SetBaseUrl(normalized);
    }

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await LoginAsync(username, password, provider: null, ct);
        return response;
    }

    public async Task<LoginResponse> LoginAsync(string username, string password, string? provider, CancellationToken ct = default)
    {
        var serverUrl = _apiClient.BaseUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new InvalidOperationException("A Silo server must be configured before login.");
        var response = await _authApi.LoginAsync(serverUrl, username, password, provider, ct);
        StartSession(
            response.AccessToken,
            response.RefreshToken,
            response.ExpiresIn,
            response.User,
            expectedServerUrl: serverUrl);
        RaiseSafely(UserChanged);
        return response;
    }

    public long SetTokens(
        string accessToken,
        string refreshToken,
        int expiresIn,
        bool preserveStoredProfile = false,
        string? expectedServerUrl = null)
    {
        var generation = StartSession(
            accessToken,
            refreshToken,
            expiresIn,
            user: null,
            preserveStoredProfile: preserveStoredProfile,
            expectedServerUrl: expectedServerUrl);
        RaiseSafely(UserChanged);
        return generation;
    }

    public bool SetCurrentUser(UserInfo user, long? expectedGeneration = null)
    {
        lock (_stateGate)
        {
            if (expectedGeneration.HasValue && _sessionGeneration != expectedGeneration.Value)
                return false;
            CurrentUser = user;
        }
        RaiseSafely(UserChanged);
        return true;
    }

    public void SelectProfile(string profileId, string? profileToken = null, Profile? profile = null)
    {
        string? serverUrl;
        long generation;
        lock (_stateGate)
        {
            SelectedProfileId = profileId;
            SelectedProfile = profile?.Id == profileId ? profile : SelectedProfile?.Id == profileId ? SelectedProfile : null;
            _apiClient.SetProfile(profileId, profileToken);
            serverUrl = _sessionServerUrl;
            generation = _sessionGeneration;
        }

        if (string.IsNullOrWhiteSpace(serverUrl))
            return;
        if (!string.IsNullOrWhiteSpace(profileToken))
            PersistProfileSession(serverUrl, profileId, profileToken, generation);
        else
            DeletePersistedProfile(serverUrl);
    }

    public (string ProfileId, string ProfileToken)? LoadPersistedProfileSession(string serverUrl)
    {
        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return null;

        lock (_credentialGate)
        {
            try
            {
                var profileId = _credentialStore.LoadCredential(serverUrl, "profile_id");
                var profileToken = _credentialStore.LoadCredential(serverUrl, "profile_token");
                if (!string.IsNullOrWhiteSpace(profileId) && !string.IsNullOrWhiteSpace(profileToken))
                    return (profileId, profileToken);

                _credentialStore.DeleteCredential(serverUrl, "profile_id");
                _credentialStore.DeleteCredential(serverUrl, "profile_token");
            }
            catch (Exception ex)
            {
                ReportCredentialStoreFailure(ex);
            }
        }
        return null;
    }

    public void HandleProfileVerificationRequired(ProfileVerificationContext context)
    {
        string? serverUrl;
        bool hadProfile;
        lock (_stateGate)
        {
            if (!_apiClient.TryClearProfile(context))
                return;

            hadProfile = SelectedProfileId != null;
            serverUrl = _sessionServerUrl;
            SelectedProfileId = null;
            SelectedProfile = null;
        }

        if (!string.IsNullOrWhiteSpace(serverUrl))
            DeletePersistedProfile(serverUrl);
        if (hadProfile)
            RaiseSafely(ProfileVerificationRequired);
    }

    public void ClearSelectedProfile()
    {
        string? serverUrl;
        bool hadProfile;
        lock (_stateGate)
        {
            hadProfile = SelectedProfileId != null || _apiClient.ProfileId != null;
            serverUrl = _sessionServerUrl;
            SelectedProfileId = null;
            SelectedProfile = null;
            _apiClient.ClearProfile();
        }

        if (!string.IsNullOrWhiteSpace(serverUrl))
            DeletePersistedProfile(serverUrl);
        if (hadProfile)
            RaiseSafely(ProfileVerificationRequired);
    }

    public void Logout()
    {
        ClearSession(
            expectedGeneration: null,
            expectedRefreshToken: null,
            deletePersistedCredentials: true,
            notifyListeners: true);
    }

    /// <summary>
    /// Switches to the server-issued impersonation token pair while preserving the
    /// administrator refresh credential in Windows Credential Manager. This mirrors
    /// the WebUI's preserved-admin-session recovery flow and survives an app restart.
    /// </summary>
    public void BeginImpersonation(ImpersonationResponse response, string returnPath)
    {
        if (string.IsNullOrWhiteSpace(response.AccessToken) ||
            string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            throw new InvalidOperationException("The server returned an incomplete impersonation session.");
        }

        string serverUrl;
        PreservedAdminSession preserved;
        lock (_stateGate)
        {
            serverUrl = _sessionServerUrl ?? _apiClient.BaseUrl;
            if (string.IsNullOrWhiteSpace(RefreshToken))
                throw new InvalidOperationException("The administrator session cannot be preserved.");

            preserved = new PreservedAdminSession(
                _apiClient.AccessToken,
                RefreshToken,
                CurrentUser,
                string.IsNullOrWhiteSpace(returnPath) ? "/admin/users" : returnPath);
            _preservedAdminSession = preserved;
        }

        PersistImpersonationAdminSession(serverUrl, preserved);
        StartSession(
            response.AccessToken,
            response.RefreshToken,
            response.ExpiresIn,
            response.User,
            expectedServerUrl: serverUrl);
        RaiseSafely(UserChanged);
    }

    /// <summary>
    /// Ends impersonation and restores the original administrator session. A 401 or
    /// not_impersonating response is recoverable when a preserved administrator
    /// refresh credential exists, matching the current WebUI behavior.
    /// </summary>
    public async Task<string> EndImpersonationAsync(CancellationToken ct = default)
    {
        string serverUrl;
        lock (_stateGate)
            serverUrl = _sessionServerUrl ?? _apiClient.BaseUrl;

        try
        {
            await _authApi.EndImpersonationAsync(ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (
            ex.StatusCode == 401 ||
            string.Equals(ex.ErrorCode, "not_impersonating", StringComparison.Ordinal))
        {
            // The impersonated session may already be gone. The preserved admin
            // session remains authoritative and is recovered below.
        }

        var preserved = LoadPreservedAdminSession(serverUrl);
        if (preserved == null)
        {
            ClearSession(
                expectedGeneration: null,
                expectedRefreshToken: null,
                deletePersistedCredentials: true,
                notifyListeners: true);
            throw new InvalidOperationException(
                "The original administrator session could not be recovered. Please sign in again.");
        }

        var returnPath = preserved.ReturnPath;
        try
        {
            // Prefer the still-valid in-memory access token for an instant restore.
            // If the app restarted or it expired, rotate the preserved admin refresh
            // token and load the authoritative user before exposing admin UI.
            if (!string.IsNullOrWhiteSpace(preserved.AccessToken))
            {
                try
                {
                    var user = await _authApi.GetMeAsync(
                        serverUrl,
                        preserved.AccessToken,
                        ct).ConfigureAwait(false);
                    StartSession(
                        preserved.AccessToken,
                        preserved.RefreshToken,
                        0,
                        user,
                        expectedServerUrl: serverUrl);
                    ClearPersistedImpersonationAdminSession(serverUrl);
                    RaiseSafely(UserChanged);
                    return returnPath;
                }
                catch (ApiException ex) when (ex.StatusCode is 401 or 403)
                {
                    // Fall through to refresh-token recovery.
                }
            }

            StartSession(
                accessToken: null,
                preserved.RefreshToken,
                expiresInSeconds: 0,
                user: null,
                expectedServerUrl: serverUrl);
            var restored = await TryRefreshAsync(ct).ConfigureAwait(false);
            if (!restored || CurrentUser == null)
                throw new InvalidOperationException("The administrator session could not be refreshed.");

            ClearPersistedImpersonationAdminSession(serverUrl);
            RaiseSafely(UserChanged);
            return returnPath;
        }
        catch
        {
            ClearSession(
                expectedGeneration: null,
                expectedRefreshToken: null,
                deletePersistedCredentials: true,
                notifyListeners: true);
            throw;
        }
    }

    /// <summary>
    /// Clears a partially restored in-memory session while retaining its rotated
    /// refresh credential for a later retry. Used only when startup restoration is
    /// interrupted by a transient network failure or an explicit timeout.
    /// </summary>
    public void AbandonRestoreAttempt(long expectedGeneration)
    {
        ClearSession(
            expectedGeneration,
            expectedRefreshToken: null,
            deletePersistedCredentials: false,
            notifyListeners: false);
    }

    /// <summary>Rolls back an incomplete login/OAuth/signup transaction.</summary>
    public void AbortAuthenticationAttempt(long expectedGeneration)
    {
        ClearSession(
            expectedGeneration,
            expectedRefreshToken: null,
            deletePersistedCredentials: true,
            notifyListeners: false);
    }

    /// <summary>
    /// Invalidates local state first, then performs bounded best-effort server
    /// revocation with the captured token. A late response can never clear a newer
    /// login because no state mutation happens after the await.
    /// </summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        string? serverUrl;
        string? accessToken;
        string? refreshToken;
        long generation;
        lock (_stateGate)
        {
            serverUrl = _sessionServerUrl;
            accessToken = _apiClient.AccessToken;
            refreshToken = RefreshToken;
            generation = _sessionGeneration;
        }

        ClearSession(
            generation,
            refreshToken,
            deletePersistedCredentials: true,
            notifyListeners: true);

        try
        {
            if (!string.IsNullOrWhiteSpace(serverUrl) && !string.IsNullOrWhiteSpace(accessToken))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await _authApi.LogoutAsync(serverUrl, accessToken, timeout.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Local token and credential removal is authoritative for this device.
        }
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        string? refreshTokenSnapshot;
        string? serverUrlSnapshot;
        long sessionGenerationSnapshot;
        lock (_stateGate)
        {
            refreshTokenSnapshot = RefreshToken;
            serverUrlSnapshot = _sessionServerUrl;
            sessionGenerationSnapshot = _sessionGeneration;
        }
        if (refreshTokenSnapshot == null || string.IsNullOrWhiteSpace(serverUrlSnapshot))
            return false;
        if (!string.Equals(_apiClient.BaseUrl, serverUrlSnapshot, StringComparison.OrdinalIgnoreCase))
        {
            ClearSession(
                sessionGenerationSnapshot,
                refreshTokenSnapshot,
                deletePersistedCredentials: false,
                notifyListeners: true);
            return false;
        }

        await _refreshGuard.WaitAsync(ct).ConfigureAwait(false);
        var refreshGuardHeld = true;
        try
        {
            lock (_stateGate)
            {
                // Another waiter may already have refreshed this session. A replaced
                // session must not be treated as a successful refresh for the old one.
                if (_sessionGeneration != sessionGenerationSnapshot)
                    return false;
                if (!string.Equals(_sessionServerUrl, serverUrlSnapshot, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(RefreshToken, refreshTokenSnapshot, StringComparison.Ordinal))
                    return RefreshToken != null && _apiClient.AccessToken != null;
            }

            var response = await _authApi.RefreshAsync(serverUrlSnapshot, refreshTokenSnapshot, ct);
            var parsedUser = TryParseUserFromJwt(response.AccessToken);
            bool provisionalUserChanged;
            lock (_stateGate)
            {
                // Logout or a replacement login may have completed while the network
                // request was in flight. In that case the refresh response is stale.
                if (_sessionGeneration != sessionGenerationSnapshot ||
                    !string.Equals(_sessionServerUrl, serverUrlSnapshot, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(RefreshToken, refreshTokenSnapshot, StringComparison.Ordinal))
                {
                    return false;
                }

                _apiClient.SetAccessToken(response.AccessToken);
                RefreshToken = response.RefreshToken;
                ScheduleRefreshUnsafe(TimeSpan.FromSeconds(response.ExpiresIn * 0.8));

                provisionalUserChanged = CurrentUser == null && parsedUser != null;
                if (provisionalUserChanged)
                    CurrentUser = parsedUser;
            }

            PersistRefreshToken(response.RefreshToken, sessionGenerationSnapshot, serverUrlSnapshot);
            _refreshGuard.Release();
            refreshGuardHeld = false;
            RaiseSafely(TokenRefreshed);
            // Must fire UserChanged AFTER setting CurrentUser so subscribers
            // (e.g. ServerActivityButton) observe the new admin state — without
            // this, auto-login leaves role-gated UI stuck at null until the next
            // polling tick catches up.
            if (provisionalUserChanged)
                RaiseSafely(UserChanged);

            // The rotated token pair is already durable. Fetch authoritative account
            // fields afterward so cancellation cannot lose a successful renewal.
            try
            {
                var authoritativeUser = await _authApi.GetMeAsync(
                    serverUrlSnapshot,
                    response.AccessToken,
                    ct).ConfigureAwait(false);
                bool applied;
                lock (_stateGate)
                {
                    applied = _sessionGeneration == sessionGenerationSnapshot &&
                        string.Equals(_sessionServerUrl, serverUrlSnapshot, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(RefreshToken, response.RefreshToken, StringComparison.Ordinal);
                    if (applied)
                        CurrentUser = authoritativeUser;
                }
                if (applied)
                    RaiseSafely(UserChanged);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ApiException ex) when (ex.StatusCode is 401 or 403)
            {
                ClearSession(
                    sessionGenerationSnapshot,
                    response.RefreshToken,
                    deletePersistedCredentials: true,
                    notifyListeners: true);
                return false;
            }
            catch
            {
                // Keep the last authoritative user during a temporary /auth/me outage.
            }
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation belongs to the caller (startup timeout, navigation, app
            // shutdown). It must never destroy an otherwise valid refresh session.
            throw;
        }
        catch (ApiException ex) when (ex.StatusCode is 400 or 401 or 403)
        {
            // Invalid, revoked, or forbidden refresh credentials are terminal for this
            // session. Clear only if it is still the session that issued the request.
            _refreshGuard.Release();
            refreshGuardHeld = false;
            ClearSession(
                sessionGenerationSnapshot,
                refreshTokenSnapshot,
                deletePersistedCredentials: true,
                notifyListeners: true);
            return false;
        }
        catch
        {
            // Network outages, server errors, and malformed transient responses should
            // not sign the user out. Keep the refresh token and retry shortly.
            ScheduleTransientRetry(sessionGenerationSnapshot, refreshTokenSnapshot);
            return false;
        }
        finally
        {
            if (refreshGuardHeld)
                _refreshGuard.Release();
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

    private long StartSession(
        string? accessToken,
        string refreshToken,
        int expiresInSeconds,
        UserInfo? user,
        bool preserveStoredProfile = false,
        string? expectedServerUrl = null)
    {
        var serverUrl = _apiClient.BaseUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new InvalidOperationException("A Silo server must be configured before starting an authentication session.");
        if (!string.IsNullOrWhiteSpace(expectedServerUrl) &&
            !string.Equals(serverUrl, expectedServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The configured Silo server changed before authentication completed.");
        }

        long sessionGeneration;
        lock (_stateGate)
        {
            _sessionGeneration++;
            sessionGeneration = _sessionGeneration;
            _apiClient.BeginAuthenticationSession(accessToken);
            RefreshToken = refreshToken;
            CurrentUser = user;
            SelectedProfileId = null;
            SelectedProfile = null;
            _sessionServerUrl = serverUrl;
            ScheduleRefreshUnsafe(
                expiresInSeconds > 0
                    ? TimeSpan.FromSeconds(expiresInSeconds * 0.8)
                    : Timeout.InfiniteTimeSpan);
        }

        PersistRefreshToken(refreshToken, sessionGeneration, serverUrl);
        if (!preserveStoredProfile)
            DeletePersistedProfile(serverUrl);
        return sessionGeneration;
    }

    private bool ClearSession(
        long? expectedGeneration,
        string? expectedRefreshToken,
        bool deletePersistedCredentials,
        bool notifyListeners)
    {
        bool hadSession;
        string? credentialServerUrl;
        long clearedGeneration;
        lock (_stateGate)
        {
            if (expectedGeneration.HasValue && _sessionGeneration != expectedGeneration.Value)
                return false;
            if (expectedRefreshToken != null &&
                !string.Equals(RefreshToken, expectedRefreshToken, StringComparison.Ordinal))
            {
                return false;
            }

            hadSession = CurrentUser != null || RefreshToken != null ||
                _apiClient.AccessToken != null || SelectedProfileId != null;
            credentialServerUrl = _sessionServerUrl;

            _sessionGeneration++;
            clearedGeneration = _sessionGeneration;
            CancelRefreshScheduleUnsafe();
            _apiClient.ClearAuth();
            CurrentUser = null;
            RefreshToken = null;
            SelectedProfileId = null;
            SelectedProfile = null;
            _sessionServerUrl = null;
            _preservedAdminSession = null;
        }

        if (deletePersistedCredentials && !string.IsNullOrWhiteSpace(credentialServerUrl))
            DeletePersistedCredentials(credentialServerUrl, clearedGeneration);
        if (hadSession && notifyListeners)
        {
            RaiseSafely(LoggedOut);
            RaiseSafely(UserChanged);
        }
        return true;
    }

    private void ScheduleTransientRetry(long expectedGeneration, string expectedRefreshToken)
    {
        lock (_stateGate)
        {
            if (_sessionGeneration != expectedGeneration ||
                !string.Equals(RefreshToken, expectedRefreshToken, StringComparison.Ordinal))
            {
                return;
            }

            ScheduleRefreshUnsafe(TransientRefreshRetryDelay);
        }
    }

    private void ScheduleRefreshUnsafe(TimeSpan dueTime)
    {
        CancelRefreshScheduleUnsafe();
        if (dueTime == Timeout.InfiniteTimeSpan || dueTime <= TimeSpan.Zero)
            return;

        _refreshScheduleCancellation = new CancellationTokenSource();
        _refreshScheduleTask = RunScheduledRefreshAsync(
            dueTime,
            _refreshScheduleCancellation.Token);
    }

    private async Task RunScheduledRefreshAsync(TimeSpan dueTime, CancellationToken ct)
    {
        try
        {
            await _delayAsync(dueTime, ct).ConfigureAwait(false);
            await TryRefreshAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch
        {
            // TryRefreshAsync classifies terminal and transient failures. Scheduled
            // work is observed here so no background exception escapes the process.
        }
    }

    private void CancelRefreshScheduleUnsafe()
    {
        _refreshScheduleCancellation?.Cancel();
        _refreshScheduleCancellation?.Dispose();
        _refreshScheduleCancellation = null;
        _refreshScheduleTask = null;
    }

    private void PersistRefreshToken(
        string refreshToken,
        long expectedGeneration,
        string serverUrl)
    {
        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return;

        lock (_credentialGate)
        {
            lock (_stateGate)
            {
                if (_sessionGeneration != expectedGeneration ||
                    !string.Equals(_sessionServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(RefreshToken, refreshToken, StringComparison.Ordinal))
                {
                    return;
                }
            }

            try
            {
                _credentialStore.SaveCredential(serverUrl, "refresh_token", refreshToken);
                // Access tokens must remain memory-only. Remove leftovers from older builds.
                _credentialStore.DeleteCredential(serverUrl, "access_token");
            }
            catch (Exception ex)
            {
                // Credential Manager failure should not invalidate an active in-memory
                // session; the next launch will simply require sign-in.
                ReportCredentialStoreFailure(ex);
            }
        }
    }

    private void PersistProfileSession(
        string serverUrl,
        string profileId,
        string profileToken,
        long expectedGeneration)
    {
        if (_credentialStore == null)
            return;

        lock (_credentialGate)
        {
            lock (_stateGate)
            {
                if (_sessionGeneration != expectedGeneration ||
                    !string.Equals(_sessionServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(SelectedProfileId, profileId, StringComparison.Ordinal) ||
                    !string.Equals(_apiClient.ProfileToken, profileToken, StringComparison.Ordinal))
                {
                    return;
                }
            }

            try
            {
                _credentialStore.SaveCredential(serverUrl, "profile_id", profileId);
                _credentialStore.SaveCredential(serverUrl, "profile_token", profileToken);
            }
            catch (Exception ex)
            {
                try
                {
                    _credentialStore.DeleteCredential(serverUrl, "profile_id");
                    _credentialStore.DeleteCredential(serverUrl, "profile_token");
                }
                catch
                {
                    // Preserve the original persistence error.
                }
                ReportCredentialStoreFailure(ex);
            }
        }
    }

    private void DeletePersistedProfile(string serverUrl)
    {
        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return;

        lock (_credentialGate)
        {
            try
            {
                _credentialStore.DeleteCredential(serverUrl, "profile_id");
                _credentialStore.DeleteCredential(serverUrl, "profile_token");
            }
            catch (Exception ex)
            {
                ReportCredentialStoreFailure(ex);
            }
        }
    }

    private void DeletePersistedCredentials(string serverUrl, long clearedGeneration)
    {
        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return;

        lock (_credentialGate)
        {
            lock (_stateGate)
            {
                // A replacement session for the same server may have started while
                // the old session was clearing. Never delete the newer credentials.
                if (_sessionGeneration > clearedGeneration &&
                    string.Equals(_sessionServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase) &&
                    RefreshToken != null)
                {
                    return;
                }
            }

            try
            {
                _credentialStore.DeleteAllForServer(serverUrl);
            }
            catch (Exception ex)
            {
                // The in-memory session has already been cleared. Do not resurrect it or
                // suppress logout because the OS credential vault is unavailable.
                ReportCredentialStoreFailure(ex);
            }
        }
    }

    private void PersistImpersonationAdminSession(
        string serverUrl,
        PreservedAdminSession session)
    {
        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return;

        lock (_credentialGate)
        {
            try
            {
                _credentialStore.SaveCredential(
                    serverUrl,
                    ImpersonationAdminRefreshKey,
                    session.RefreshToken);
                _credentialStore.SaveCredential(
                    serverUrl,
                    ImpersonationReturnPathKey,
                    session.ReturnPath);
            }
            catch (Exception ex)
            {
                try
                {
                    _credentialStore.DeleteCredential(serverUrl, ImpersonationAdminRefreshKey);
                    _credentialStore.DeleteCredential(serverUrl, ImpersonationReturnPathKey);
                }
                catch
                {
                    // Preserve the original secure-store failure.
                }
                ReportCredentialStoreFailure(ex);
            }
        }
    }

    private PreservedAdminSession? LoadPreservedAdminSession(string serverUrl)
    {
        lock (_stateGate)
        {
            if (_preservedAdminSession != null)
                return _preservedAdminSession;
        }

        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return null;

        lock (_credentialGate)
        {
            try
            {
                var refreshToken = _credentialStore.LoadCredential(
                    serverUrl,
                    ImpersonationAdminRefreshKey);
                if (string.IsNullOrWhiteSpace(refreshToken))
                    return null;

                var returnPath = _credentialStore.LoadCredential(
                    serverUrl,
                    ImpersonationReturnPathKey);
                return new PreservedAdminSession(
                    AccessToken: null,
                    refreshToken,
                    User: null,
                    string.IsNullOrWhiteSpace(returnPath) ? "/admin/users" : returnPath);
            }
            catch (Exception ex)
            {
                ReportCredentialStoreFailure(ex);
                return null;
            }
        }
    }

    private void ClearPersistedImpersonationAdminSession(string serverUrl)
    {
        lock (_stateGate)
            _preservedAdminSession = null;

        if (_credentialStore == null || string.IsNullOrWhiteSpace(serverUrl))
            return;

        lock (_credentialGate)
        {
            try
            {
                _credentialStore.DeleteCredential(serverUrl, ImpersonationAdminRefreshKey);
                _credentialStore.DeleteCredential(serverUrl, ImpersonationReturnPathKey);
            }
            catch (Exception ex)
            {
                ReportCredentialStoreFailure(ex);
            }
        }
    }

    private void ReportCredentialStoreFailure(Exception exception)
    {
        var handlers = CredentialStoreFailed;
        if (handlers == null)
            return;
        foreach (Action<Exception> handler in handlers.GetInvocationList())
        {
            try { handler(exception); }
            catch { }
        }
    }

    private static void RaiseSafely(Action? handlers)
    {
        if (handlers == null)
            return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            CancelRefreshScheduleUnsafe();
        }
    }
}
