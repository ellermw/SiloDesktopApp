namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    private readonly SemaphoreSlim _personalListCapabilityGate = new(1, 1);
    private (ApiRequestContext Context, DateTime At, bool Supported)? _personalListCapability;

    private async Task<bool> GetPersonalListMembershipAsync(string list, string itemId, CancellationToken ct)
    {
        var context = client.CaptureContext();
        var supported = await SupportsPersonalListMembershipAsync(context, ct).ConfigureAwait(false);
        var path = $"/api/v2/{list}/{Uri.EscapeDataString(itemId)}";
        if (supported)
        {
            // On a capable server a nonmember is a successful false response.
            // A404 means inaccessible/missing, not an invitation to probe the
            // obsolete route (whose expected404s can trigger proxy bans).
            var response = await client.SendRequestAsync<PersonalListMembership>(context, HttpMethod.Get, path + "/membership", null, ct).ConfigureAwait(false);
            EnsureMembershipOwner(context, ct);
            return response.IsMember;
        }
        try
        {
            await client.SendRequestAsync<object>(context, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
            EnsureMembershipOwner(context, ct);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            EnsureMembershipOwner(context, ct);
            return false;
        }
    }

    private async Task<bool> SupportsPersonalListMembershipAsync(ApiRequestContext context, CancellationToken ct)
    {
        await _personalListCapabilityGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureMembershipOwner(context, ct);
            if (_personalListCapability is { } cached && cached.Context == context
                && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5))
                return cached.Supported;
            bool supported;
            try
            {
                supported = (await client.SendRequestAsync<PersonalListCapability>(context, HttpMethod.Get,
                    "/api/v2/capabilities/personal-lists", null, ct).ConfigureAwait(false)).MembershipSupported;
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501)
            {
                supported = false;
            }
            EnsureMembershipOwner(context, ct);
            _personalListCapability = (context, DateTime.UtcNow, supported);
            return supported;
        }
        finally { _personalListCapabilityGate.Release(); }
    }

    private void EnsureMembershipOwner(ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Personal list owner changed.", ct);
    }

    private sealed class PersonalListCapability { public bool MembershipSupported { get; set; } }
    private sealed class PersonalListMembership { public bool IsMember { get; set; } }
}
