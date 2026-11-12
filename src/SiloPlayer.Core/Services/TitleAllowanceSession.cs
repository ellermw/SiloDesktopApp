using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>A single page/dialog visit's authority. It cannot follow a changed viewer or access policy.</summary>
public sealed class TitleAllowanceSession
{
    private readonly SiloApiClient _client;
    private readonly Func<bool> _navigationCurrent;
    private readonly CancellationToken _ct;
    public TitleAllowanceSession(SiloApiClient client, Profile? actor, Func<bool> navigationCurrent, CancellationToken ct = default)
    {
        _client = client; _navigationCurrent = navigationCurrent; _ct = ct;
        Owner = client.CaptureContext();
        IsPrimary = actor is { IsPrimary: true } && actor.Id == Owner.ProfileId;
        Api = new(client);
    }
    public ApiRequestContext Owner { get; }
    public TitleAllowancesApi Api { get; }
    public bool IsPrimary { get; }
    public bool IsCurrent => IsPrimary && !_ct.IsCancellationRequested && _client.IsCurrentContext(Owner) && _navigationCurrent();
    public IReadOnlyList<Profile> LimitedProfiles { get; private set; } = [];
    public bool Available { get; private set; }
    public static bool HasLimit(Profile profile) => !string.IsNullOrWhiteSpace(profile.MaxContentRating) || profile.MaxAdvisoryAge > 0;
    public static bool HasAllowableProfile(IEnumerable<TitleAllowanceProfile> rows)
        => rows.Any(row => row.State is "above_limit" or "allowed" or "allowed_by_collection");
    public static (bool Checked, bool Enabled, string Note) RowState(string state) => state switch
    {
        "allowed" => (true, true, ""),
        "above_limit" => (false, true, ""),
        "allowed_by_collection" => (true, false, "Allowed by a collection"),
        "no_library_access" => (false, false, "No library access"),
        _ => (false, false, ""),
    };
    public static string DescribeLimit(string rating, int? age)
        => string.Join(" · ", new[] { rating, age > 0 ? $"Age {age}+" : "" }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public void EnsureCurrent()
    {
        _ct.ThrowIfCancellationRequested();
        if (!IsCurrent) throw new OperationCanceledException("Reopen allowances for the current primary profile.");
    }
    public async Task<T> RunAsync<T>(Func<TitleAllowancesApi, ApiRequestContext, CancellationToken, Task<T>> operation)
    {
        EnsureCurrent();
        var result = await operation(Api, Owner, _ct);
        EnsureCurrent();
        return result;
    }
    public async Task RunAsync(Func<TitleAllowancesApi, ApiRequestContext, CancellationToken, Task> operation)
    {
        EnsureCurrent(); await operation(Api, Owner, _ct); EnsureCurrent();
    }
    public async Task<bool> LoadAsync()
    {
        if (!IsCurrent) return false;
        Available = false;
        var household = await RunAsync((api, owner, ct) => api.GetProfilesAsync(owner, ct));
        // Revalidate primary against the household read rather than trusting a retained profile model.
        if (!household.Profiles.Any(profile => profile.Id == Owner.ProfileId && profile.IsPrimary)) return false;
        LimitedProfiles = household.Profiles.Where(profile => profile.Id != Owner.ProfileId && HasLimit(profile)).ToArray();
        if (LimitedProfiles.Count == 0) return false;
        var capability = await RunAsync((api, owner, ct) => api.GetCapabilityAsync(owner, ct));
        return Available = capability.State == "available";
    }
}
