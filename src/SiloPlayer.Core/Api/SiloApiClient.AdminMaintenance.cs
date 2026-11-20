using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Configure › Maintenance transport: the bounded, lease-checked Admin library
/// transport, additionally exposing the response status and <c>Location</c>
/// that 202 catalog receipts and 409 active-job conflicts carry. It adds no
/// deadline, size bound or retry policy of its own. Only reads may refresh an
/// expired access token; every catalog command is sent exactly once.
/// </summary>
public partial class SiloApiClient
{
    internal Task<AdminMaintenanceResponse<T>> AdminMaintenanceSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, CancellationToken ct = default) => AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, method, path, body);
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var location = response.Headers.Location?.OriginalString;
        if (!response.IsSuccessStatusCode)
        {
            try { await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false); }
            catch (ApiException error) when (error.StatusCode == (int)HttpStatusCode.Conflict)
            {
                throw new AdminCatalogConflictException(error, location);
            }
            lease.ThrowIfRetired();
        }
        if (response.StatusCode == HttpStatusCode.NoContent) throw new InvalidDataException("The server answered without the expected maintenance body.");
        var bytes = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibraryJsonLimit, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty maintenance response.");
        lease.ThrowIfRetired();
        return new AdminMaintenanceResponse<T>((int)response.StatusCode, location, value);
    });
}

/// <summary>A decoded 2xx maintenance answer with its status and <c>Location</c> header (null when absent).</summary>
public sealed record AdminMaintenanceResponse<T>(int Status, string? Location, T Body);

/// <summary>
/// A 409 from a catalog command. <see cref="JobLocation"/> is the server's
/// <c>Location</c> for the active catalog job, when it named one. Still an
/// <see cref="ApiException"/> so shared failure classification applies.
/// </summary>
public sealed class AdminCatalogConflictException : ApiException
{
    public AdminCatalogConflictException(ApiException source, string? jobLocation)
        : base(source.ErrorCode, source.Message, source.StatusCode, source.ErrorLocation, source.RetryAfterSeconds)
    {
        ProblemType = source.ProblemType;
        ProblemTitle = source.ProblemTitle;
        ProblemDetail = source.ProblemDetail;
        FieldErrors = source.FieldErrors;
        JobLocation = jobLocation;
    }

    public string? JobLocation { get; }
}
