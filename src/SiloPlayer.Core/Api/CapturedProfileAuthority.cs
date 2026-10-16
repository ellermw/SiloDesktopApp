using System.Net.Http.Headers;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Api;

/// <summary>Short-lived in-memory grant. Never serialize or persist this capability.</summary>
public sealed class CapturedProfileAuthority
{
    internal CapturedProfileAuthority(SiloApiClient client, ApiRequestContext owner, string access, string id, string? proof)
    { Client = client; Owner = owner; Access = access; ProfileId = id; Proof = proof; }
    internal SiloApiClient Client { get; }
    internal ApiRequestContext Owner { get; }
    internal string Access { get; }
    internal string? Proof { get; }
    public string ProfileId { get; }
    public bool IsCurrent => Client.IsCurrentContext(Owner);
    public override string ToString() => nameof(CapturedProfileAuthority);
}

public partial class SiloApiClient
{
    private static readonly HttpRequestOptionsKey<bool> DetachedProfileOption = new("SiloPlayer.DetachedProfile");

    public async Task<VerifyPinResponse> VerifyProfilePinForManagementAsync(ApiRequestContext owner,
        string profileId, string pin, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var request = CreateContextBoundRequest(owner, HttpMethod.Post,
            $"/api/v2/profiles/{Uri.EscapeDataString(profileId)}/verify-pin", new VerifyPinRequest { Pin = pin });
        request.Options.Set(DetachedProfileOption, true);
        var result = await SendAsync<VerifyPinResponse>(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!IsCurrentContext(owner)) throw new OperationCanceledException("The profile session changed.", ct);
        return result;
    }

    public CapturedProfileAuthority CapturePrimaryAuthority(ApiRequestContext owner, Profile primary, string? proof)
    {
        lock (_authStateGate)
        {
            if (!IsCurrentContext(owner)) throw new OperationCanceledException("The profile session changed.");
            if (string.IsNullOrWhiteSpace(_accessToken)) throw new InvalidOperationException("Your session ended. Sign in again.");
            if (!primary.IsPrimary || string.IsNullOrWhiteSpace(primary.Id)) throw new InvalidOperationException("The primary profile is required.");
            if (primary.HasPin && string.IsNullOrWhiteSpace(proof)) throw new InvalidOperationException("The primary profile PIN must be verified.");
            return new(this, owner, _accessToken, primary.Id, primary.HasPin ? proof : null);
        }
    }

    private HttpRequestMessage CreateDetachedRequest(CapturedProfileAuthority authority, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_authStateGate)
        {
            if (!ReferenceEquals(authority.Client, this) || !authority.IsCurrent)
                throw new OperationCanceledException("The profile session changed.", ct);
            var origin = new Uri(authority.Owner.BaseUrl.TrimEnd('/') + "/");
            var target = new Uri(origin, path);
            if (target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port)
                throw new ArgumentException("Profile requests must use the connected server.", nameof(path));
            var request = new HttpRequestMessage(method, target);
            request.Options.Set(AuthenticationGenerationOption, authority.Owner.AuthenticationGeneration);
            request.Options.Set(RequestContextGenerationOption, authority.Owner.RequestContextGeneration);
            request.Options.Set(DetachedProfileOption, true);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authority.Access);
            request.Headers.Add("X-Profile-Id", authority.ProfileId);
            if (authority.Proof != null) request.Headers.Add("X-Profile-Token", authority.Proof);
            AddDeviceHeadersUnsafe(request);
            if (body != null) request.Content = CreateJsonContent(body);
            return request;
        }
    }

    public async Task<T> SendCapturedProfileAsync<T>(CapturedProfileAuthority authority, HttpMethod method, string path, object? body, CancellationToken ct = default)
    {
        using var request = CreateDetachedRequest(authority, method, path, body, ct);
        var result = await SendAsync<T>(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        EnsureCapturedCurrent(authority, ct);
        return result;
    }

    public async Task<T> UploadCapturedProfileAsync<T>(CapturedProfileAuthority authority, string path,
        string fileName, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        using var request = CreateDetachedRequest(authority, HttpMethod.Put, path, null, ct);
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(file, "avatar", fileName); request.Content = multipart;
        var result = await SendAsync<T>(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        EnsureCapturedCurrent(authority, ct);
        return result;
    }

    private static void EnsureCapturedCurrent(CapturedProfileAuthority authority, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!authority.IsCurrent) throw new OperationCanceledException("The profile session changed.", ct);
    }
}
