using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

public sealed class AdminUserViewAs : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _origin;
    private readonly CancellationTokenSource _owner;
    private readonly Func<bool> _current;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _gate = new();
    private int _active;
    private bool _disposed;
    private TaskCompletionSource<bool>? _ending;
    private string? _access;
    private string? _profile;
    private string? _profileToken;
    public string TargetId { get; }

    public AdminUserViewAs(string baseUrl, string targetId, JsonObject pair, CancellationToken ownerToken, Func<bool> current, HttpMessageHandler? handler = null)
    {
        if (pair["user"].Text("id") != targetId || pair.Text("access_token").Length == 0) throw new InvalidDataException("The view-as response did not match its reviewed target.");
        TargetId = targetId;
        _access = pair.Text("access_token");
        _origin = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _owner = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        _current = current;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    }
    internal static Uri ServerUri(string baseUrl, string path) => new(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);
    public Task<JsonObject> ReadAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, ct);
    public async Task SelectProfileAsync(string id, string? pin, CancellationToken ct = default)
    {
        Current();
        string? verification = null;
        if (pin != null)
        {
            var result = await SendAsync(HttpMethod.Post, "/api/v2/profiles/" + Uri.EscapeDataString(id) + "/verify-pin", new JsonObject { ["pin"] = pin }, ct).ConfigureAwait(false);
            if (!result.Flag("valid") || result.Text("profile_token").Length == 0) throw new InvalidOperationException("The profile PIN was not accepted.");
            verification = result.Text("profile_token");
        }
        lock (_gate) { Current(); _profile = id; _profileToken = verification; }
    }
    public async Task EndAsync(CancellationToken ct = default)
    {
        if (!await StartEnding().WaitAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The temporary sign-out outcome is unknown; its local credentials were discarded without replay.");
    }
    private void Current()
    {
        if (_disposed || _access == null || _owner.IsCancellationRequested || !_current()) throw new OperationCanceledException("The scoped view-as session retired.");
    }
    private async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        Current();
        if (!(path.StartsWith("/api/v2/profiles", StringComparison.Ordinal) || path.StartsWith("/api/v2/catalog", StringComparison.Ordinal) ||
            path.StartsWith("/api/v2/home/", StringComparison.Ordinal) || path == "/api/v2/libraries" || path == "/api/v2/auth/logout")) throw new ArgumentException("Unsupported scoped view-as resource.");
        CancellationTokenSource linked;
        lock (_gate)
        {
            Current(); linked = CancellationTokenSource.CreateLinkedTokenSource(_owner.Token, ct); linked.CancelAfter(TimeSpan.FromSeconds(30)); _active++;
        }
        var entered = false;
        try
        {
            await _requests.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
            return await SendOwnedAsync(method, path, body, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            if (entered) _requests.Release();
            linked.Dispose();
            lock (_gate) { _active--; if (_disposed && _active == 0) ReleaseResources(); }
        }
    }
    private async Task<JsonObject> SendOwnedAsync(HttpMethod method, string path, JsonObject? body, CancellationToken token)
    {
        try
        {
            Current();
            var target = ServerUri(_origin.AbsoluteUri, path);
            if (target.Scheme != _origin.Scheme || target.Authority != _origin.Authority) throw new InvalidOperationException("View-as is restricted to the connected server.");
            using var request = new HttpRequestMessage(method, target);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
            if (_profile != null) request.Headers.Add("X-Profile-Id", _profile);
            if (_profileToken != null) request.Headers.Add("X-Profile-Token", _profileToken);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            Current(); token.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Scoped view-as request refused ({(int)response.StatusCode}). Return to admin; nothing was retried.");
            const int maximum = 4 * 1024 * 1024;
            if (response.Content.Headers.ContentLength is > maximum) throw new InvalidDataException("Scoped response exceeded its byte bound.");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[16384];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                Current(); if (count == 0) break;
                if (bytes.Length + count > maximum) throw new InvalidDataException("Scoped response exceeded its byte bound.");
                bytes.Write(buffer, 0, count);
            }
            Current();
            return bytes.Length == 0 ? new JsonObject() : JsonNode.Parse(bytes.ToArray())?.AsObject() ?? throw new InvalidDataException("Invalid scoped response.");
        }
        catch (Exception) when (_disposed || _owner.IsCancellationRequested || !_current()) { throw new OperationCanceledException("The scoped view-as session retired."); }
    }
    private void ReleaseResources() { _http.Dispose(); _owner.Dispose(); _requests.Dispose(); }
    private Task<bool> StartEnding()
    {
        TaskCompletionSource<bool> completion;
        string? access;
        lock (_gate)
        {
            if (_ending != null) return _ending.Task;
            _ending = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposed = true; _active++;
            access = _access; _access = null; _profile = null; _profileToken = null;
            _owner.Cancel();
        }
        _ = RevokeAsync(access, completion);
        return completion.Task;
    }
    private async Task RevokeAsync(string? access, TaskCompletionSource<bool> completion)
    {
        var succeeded = false;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Post, ServerUri(_origin.AbsoluteUri, "/api/v2/auth/logout"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            succeeded = response.IsSuccessStatusCode;
        }
        catch (Exception) { succeeded = false; }
        finally
        {
            lock (_gate) { _active--; if (_active == 0) ReleaseResources(); }
            completion.TrySetResult(succeeded);
        }
    }
    public void Dispose() => _ = StartEnding();
    public override string ToString() => nameof(AdminUserViewAs);
}
