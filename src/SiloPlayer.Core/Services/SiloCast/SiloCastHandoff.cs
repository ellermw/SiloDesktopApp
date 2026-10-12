using System.Text.Json.Nodes;
using System.Net.Http.Json;
using System.Text;
using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services.SiloCast;

public sealed class SiloCastHandoffException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class SiloCastIdentity : IDisposable
{
    private int _disposed;
    internal CancellationTokenSource Lifetime { get; } = new();
    public HttpClient HttpClient { get; internal set; } = null!;
    public SiloApiClient ApiClient { get; internal set; } = null!;
    public AuthService Auth { get; internal set; } = null!;
    public PlaybackApi PlaybackApi { get; internal set; } = null!;
    public CatalogApi CatalogApi { get; internal set; } = null!;
    public SettingsApi SettingsApi { get; internal set; } = null!;
    public DateTimeOffset ExpiresAt { get; internal set; }
    public string ServerId { get; internal set; } = "";
    public string? ServerIdentity { get; internal set; }
    public string ProfileId { get; internal set; } = "";
    public string ProfileName { get; internal set; } = "";
    public string ServerName { get; internal set; } = "";
    public string? ControllerDeviceId { get; set; }
    public string RequestId { get; internal set; } = "";
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Lifetime.Cancel();
        Auth.Logout();
        Auth.Dispose();
        HttpClient.Dispose();
        Lifetime.Dispose();
    }
}

public sealed class SiloCastHandoff
{
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public SiloCastHandoff(Func<HttpMessageHandler>? handlerFactory = null, Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _handlerFactory = handlerFactory ?? (() => new SocketsHttpHandler { AllowAutoRedirect = false });
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    /// <summary>Obtains a fresh memory-only authority. The caller fences controller ownership and activates it after closing outgoing playback.</summary>
    public async Task<SiloCastIdentity> PrepareAsync(JsonObject offer, string deviceName, string deviceId,
        Func<JsonObject, Task> challenge, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(challenge);
        ct.ThrowIfCancellationRequested();
        var requestId = RequiredText(offer, "requestId", 128);
        var profileId = RequiredText(offer, "profileId", 128);
        var serverId = RequiredText(offer, "serverId", 4096);
        var offeredUrl = ValidateUrl(RequiredText(offer, "serverURL", 2048));
        if (RegistryKey(offeredUrl) != serverId) throw Failure("invalid_offer", "The phone sent an invalid server identity.");
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 128 || deviceName.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128 || deviceId.Any(char.IsControl))
            throw Failure("invalid_offer", "The Windows device identity is invalid.");

        var inner = _handlerFactory();
        if (inner is HttpClientHandler httpHandler) httpHandler.AllowAutoRedirect = false;
        if (inner is SocketsHttpHandler socketsHandler) socketsHandler.AllowAutoRedirect = false;
        var guard = new AuthorityHandler(inner, _utcNow);
        var http = new HttpClient(guard) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("X-Silo-Device-Id", deviceId);
        http.DefaultRequestHeaders.Add("X-Silo-Device-Name", deviceName);
        http.DefaultRequestHeaders.Add("X-Silo-Device-Platform", "windows");
        http.DefaultRequestHeaders.Add("X-Silo-Client", SiloApiClient.DefaultClientName);
        http.DefaultRequestHeaders.Add("X-Silo-Client-Family", SiloApiClient.ClientFamily);
        var api = new SiloApiClient(http);
        api.SetDeviceMetadata(deviceId, deviceName, "windows");
        string? deviceCode = null;
        bool supportsCancel = false;
        SiloCastIdentity? identity = null;
        try
        {
            var serverUrl = await ResolveUrlAsync(http, offer, offeredUrl, ct).ConfigureAwait(false);
            api.SetBaseUrl(serverUrl); // Do not normalize via the persisted credential URL rules.
            var capability = await BootstrapAsync(http, serverUrl, "/api/v2/auth/device/capability", null, ct).ConfigureAwait(false);
            if (capability["state"]?.GetValue<string>() != "available" || capability["allowed"]?.GetValue<bool>() == false || capability["remote_playback_handoff"]?.GetValue<bool>() != true ||
                capability["protocol_versions"] is not JsonArray versions || !versions.Any(v => v?.GetValue<int>() == 2))
                throw Failure("unsupported_server", "Update the Silo server to use profile handoff.");
            supportsCancel = capability["cancel"]?.GetValue<bool>() == true;
            var startResponse = await BootstrapAsync(http, serverUrl, "/api/v2/auth/device/start", new JsonObject
            {
                ["device_name"] = deviceName, ["device_platform"] = "windows",
                ["client_purpose"] = "remote_playback", ["temporary"] = true,
            }, ct, System.Net.HttpStatusCode.Created).ConfigureAwait(false);
            var started = startResponse.Deserialize<DeviceLoginStartResponse>(ApiJson) ?? throw Failure("invalid_response", "Invalid device request.");
            deviceCode = started.DeviceCode;
            var now = _utcNow();
            if (!started.Temporary || started.ClientPurpose != "remote_playback" ||
                !ValidToken(deviceCode) || string.IsNullOrWhiteSpace(started.UserCode) || string.IsNullOrWhiteSpace(started.MatchCode) ||
                started.ExpiresIn <= 0 || started.ExpiresIn > 3600 || started.Interval <= 0 || started.Interval > 3600 ||
                !TryInstant(started.ExpiresAt, out var expiresAt) || expiresAt <= now)
                throw Failure("invalid_response", "The server returned an invalid profile handoff.");
            var deadline = now.AddSeconds(started.ExpiresIn);
            if (expiresAt < deadline) deadline = expiresAt;
            ct.ThrowIfCancellationRequested();
            await challenge(new JsonObject { ["requestId"] = requestId, ["userCode"] = started.UserCode,
                ["matchCode"] = started.MatchCode, ["expiresAt"] = started.ExpiresAt }).WaitAsync(ct).ConfigureAwait(false);
            var poll = await AwaitApprovalAsync(http, serverUrl, deviceCode, started.Interval, deadline, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            now = _utcNow();
            var tokens = poll["tokens"]?.Deserialize<LoginResponse>(ApiJson);
            var proof = Text(poll, "profile_token");
            if (poll["temporary"]?.GetValue<bool>() != true || Text(poll, "profile_id") != profileId ||
                tokens == null || !ValidToken(tokens.AccessToken) || !ValidToken(tokens.RefreshToken) || tokens.ExpiresIn <= 0 ||
                string.IsNullOrWhiteSpace(tokens.User?.Id) || !ValidToken(proof) ||
                !TryInstant(Text(poll, "session_expires_at"), out var sessionExpiry) || sessionExpiry <= now || sessionExpiry > now.AddHours(24))
                throw Failure("invalid_response", "The server returned an invalid profile handoff.");

            guard.Authorize(serverUrl, sessionExpiry);
            identity = new SiloCastIdentity { HttpClient = http, ApiClient = api, ExpiresAt = sessionExpiry,
                RequestId = requestId, ServerId = serverId, ServerIdentity = Text(offer, "serverIdentity")?.Trim(), ProfileId = profileId,
                ProfileName = Text(offer, "profileName") ?? "", ServerName = Text(offer, "serverName") ?? "",
                PlaybackApi = new PlaybackApi(api), CatalogApi = new CatalogApi(api), SettingsApi = new SettingsApi(api) };
            var scope = identity;
            // The existing AuthService schedules proactive 80% renewals. Its delay is
            // bounded by the absolute temporary expiry, independently of later token TTLs.
            async Task BoundedRefreshDelay(TimeSpan delay, CancellationToken refreshCt)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(refreshCt, scope.Lifetime.Token);
                var remaining = scope.ExpiresAt - _utcNow();
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(delay < remaining ? delay : remaining, linked.Token).ConfigureAwait(false);
                if (_utcNow() >= scope.ExpiresAt)
                {
                    scope.Auth.Logout();
                    throw new OperationCanceledException(linked.Token);
                }
            }
            identity.Auth = new AuthService(api, new AuthApi(api), credentialStore: null, BoundedRefreshDelay);
            api.SetTokenRefresher(async refreshCt =>
            {
                if (_utcNow() >= scope.ExpiresAt) { scope.Auth.Logout(); return false; }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(refreshCt, scope.Lifetime.Token);
                linked.CancelAfter(scope.ExpiresAt - _utcNow());
                return await scope.Auth.TryRefreshAsync(linked.Token).ConfigureAwait(false);
            });
            api.SetProfileVerificationRequiredHandler(identity.Auth.HandleProfileVerificationRequired);
            identity.Auth.SetTokens(tokens!.AccessToken, tokens.RefreshToken, tokens.ExpiresIn);
            identity.Auth.SetCurrentUser(tokens.User);
            identity.Auth.SelectProfile(profileId, proof);
            ct.ThrowIfCancellationRequested();
            return identity;
        }
        catch (Exception exception)
        {
            if (deviceCode != null && supportsCancel && identity == null)
            {
                // Withdraw only this abandoned request, with no bearer and a bounded
                // independent token so caller cancellation cannot leave an approvable code.
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await BootstrapAsync(http, api.BaseUrl, "/api/v2/auth/device/cancel",
                        new JsonObject { ["device_code"] = deviceCode }, cleanup.Token).ConfigureAwait(false);
                }
                catch { }
            }
            if (identity != null) identity.Dispose(); else http.Dispose();
            ct.ThrowIfCancellationRequested();
            if (exception is SiloCastHandoffException or OperationCanceledException) throw;
            throw Failure("handoff_failed", "The server could not complete profile handoff.");
        }
    }

    private async Task<string> ResolveUrlAsync(HttpClient http, JsonObject offer, string offeredUrl, CancellationToken ct)
    {
        var expected = Text(offer, "serverIdentity")?.Trim();
        if (string.IsNullOrEmpty(expected)) return offeredUrl;
        var candidates = new List<string> { offeredUrl };
        if (offer["serverEndpoints"] is JsonArray endpoints)
        {
            if (endpoints.Count > 32) throw Failure("invalid_offer", "Too many server endpoints.");
            foreach (var endpoint in endpoints)
                candidates.Add(ValidateUrl(RequiredText(endpoint?.AsObject() ?? throw Failure("invalid_offer", "Invalid endpoint."), "url", 2048)));
        }
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            JsonObject document;
            try
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probe.CancelAfter(TimeSpan.FromSeconds(8));
                document = await BootstrapAsync(http, candidate, "/api/v2/system/identity", null, probe.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
            catch (HttpRequestException) { continue; }
            catch (SiloCastHandoffException) { continue; }
            catch (SiloCastHttpException) { continue; }
            catch (JsonException) { continue; }
            var reported = Text(document, "server_id")?.Trim();
            if (string.IsNullOrEmpty(reported)) continue;
            if (reported != expected) throw Failure("identity_mismatch", "The address belongs to a different Silo server.");
            return candidate;
        }
        throw Failure("server_unreachable", "This PC cannot reach the phone's Silo server.");
    }

    private async Task<JsonObject> AwaitApprovalAsync(HttpClient http, string url, string deviceCode, int interval,
        DateTimeOffset deadline, CancellationToken ct)
    {
        var wait = TimeSpan.FromSeconds(interval);
        while (_utcNow() < deadline)
        {
            ct.ThrowIfCancellationRequested();
            JsonObject? poll = null;
            try
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bounded.CancelAfter(deadline - _utcNow());
                poll = await BootstrapAsync(http, url, "/api/v2/auth/device/poll", new JsonObject { ["device_code"] = deviceCode }, bounded.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (SiloCastHttpException ex) when (ex.StatusCode is 408 or 429 or >= 500) { }
            ct.ThrowIfCancellationRequested();
            if (_utcNow() >= deadline) break;
            if (poll != null)
            {
                switch (Text(poll, "status"))
                {
                    case "approved": return poll;
                    case "pending":
                        var seconds = poll["poll_after"]?.GetValue<int>() ?? interval;
                        if (seconds <= 0 || seconds > 3600) throw Failure("invalid_response", "Invalid polling interval.");
                        wait = TimeSpan.FromSeconds(seconds);
                        break;
                    case "denied": throw Failure("denied", "Profile handoff was denied.");
                    case "expired": case "consumed": case "canceled": throw Failure("expired", "Profile handoff ended.");
                    default: throw Failure("invalid_response", "Invalid profile handoff status.");
                }
            }
            var remaining = deadline - _utcNow();
            if (remaining > TimeSpan.Zero) await _delayAsync(wait < remaining ? wait : remaining, ct).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        throw Failure("expired", "Profile handoff expired.");
    }

    private static async Task<JsonObject> BootstrapAsync(HttpClient http, string url, string path, JsonObject? body, CancellationToken ct,
        System.Net.HttpStatusCode expectedStatus = System.Net.HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url + path);
        request.Headers.Accept.ParseAdd("application/json");
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != expectedStatus)
            throw new SiloCastHttpException((int)response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false)) ?? throw Failure("invalid_response", "Empty server response.");
    }

    private static string RequiredText(JsonObject obj, string name, int maxLength)
    {
        var value = Text(obj, name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw Failure("invalid_offer", "The phone sent an invalid server or profile.");
        return value;
    }
    private static string? Text(JsonObject obj, string name) => obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static bool ValidToken(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length <= 32768 && !token.Any(char.IsWhiteSpace) && !token.Any(char.IsControl);
    private static bool TryInstant(string? text, out DateTimeOffset instant) => DateTimeOffset.TryParseExact(text,
        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant);
    private static string ValidateUrl(string raw)
    {
        var value = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || value.Any(char.IsControl))
            throw Failure("invalid_offer", "The phone sent an invalid server URL.");
        return value;
    }
    private static string RegistryKey(string normalizedUrl) => Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedUrl)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static SiloCastHandoffException Failure(string code, string message) => new(code, message);

    private sealed class SiloCastHttpException(int statusCode) : Exception("The Silo server refused profile handoff.")
    {
        public int StatusCode { get; } = statusCode;
    }

    private sealed class AuthorityHandler(HttpMessageHandler inner, Func<DateTimeOffset> now) : DelegatingHandler(inner)
    {
        private Uri? _origin;
        private DateTimeOffset _expiresAt;
        public void Authorize(string url, DateTimeOffset expiresAt) { _origin = new Uri(url); _expiresAt = expiresAt; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_origin != null || request.Headers.Authorization != null || request.Headers.Contains("X-Profile-Token"))
            {
                var target = request.RequestUri!;
                if (_origin == null || target.Scheme != _origin.Scheme || target.Host != _origin.Host || target.Port != _origin.Port)
                    throw new InvalidOperationException("Temporary credentials must remain on the approved server origin.");
                var remaining = _expiresAt - now();
                if (remaining <= TimeSpan.Zero) throw new OperationCanceledException("Temporary playback authority expired.");
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bounded.CancelAfter(remaining);
                var response = await base.SendAsync(request, bounded.Token).ConfigureAwait(false);
                if (now() >= _expiresAt) { response.Dispose(); throw new OperationCanceledException("Temporary playback authority expired."); }
                return response;
            }
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
