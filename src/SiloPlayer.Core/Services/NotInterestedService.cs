using System.Collections.Concurrent;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public sealed record NotInterestedOperation(ApiRequestContext Context, string ItemId, string Type, bool Marked);
public sealed record NotInterestedChange(ApiRequestContext Context, string ItemId, bool? Marked);

/// <summary>One captured profile/item owns the write, rollback, publication and optional Undo.</summary>
public sealed class NotInterestedService(CatalogApi catalog)
{
    private readonly ConcurrentDictionary<(ApiRequestContext, string), byte> _pending = new();
    public event Action<NotInterestedChange>? Changed;
    public bool IsCurrent(ApiRequestContext context) => catalog.IsCurrentContext(context);
    public NotInterestedOperation Capture(MediaItem item, bool marked) => new(catalog.CaptureContext(), item.ContentId, item.Type, marked);

    public async Task ApplyAsync(NotInterestedOperation operation, MediaItem? item = null, CancellationToken ct = default)
    {
        if (!IsCurrent(operation.Context)) throw new OperationCanceledException("Switch back to the profile that marked this to undo it.");
        if (operation.Type is not ("movie" or "series")) throw new ArgumentException("Only movies and series can be marked Not interested.");
        var key = (operation.Context, operation.ItemId);
        if (!_pending.TryAdd(key, 0)) throw new InvalidOperationException("This title is already being updated.");
        var target = item?.ContentId == operation.ItemId ? item : null;
        var previous = target?.UserState?.NotInterested ?? false;
        try
        {
            if (target != null) (target.UserState ??= new()).NotInterested = operation.Marked;
            await catalog.SetNotInterestedAsync(operation.Context, operation.ItemId, operation.Type, operation.Marked, ct);
            if (!IsCurrent(operation.Context)) throw new OperationCanceledException("Not interested profile changed.");
            Changed?.Invoke(new(operation.Context, operation.ItemId, operation.Marked));
        }
        catch
        {
            if (target != null && IsCurrent(operation.Context)) (target.UserState ??= new()).NotInterested = previous;
            throw;
        }
        finally { _pending.TryRemove(key, out _); }
    }

    /// <summary>Dispatch the server's actual user_state.changed envelope; consumers marshal to their UI thread.</summary>
    public void ReceiveRealtime(string channel, string eventName, JsonElement data)
    {
        if (channel != "user_state" || eventName != "user_state.changed" || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("change", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "not_interested"
            || !data.TryGetProperty("content_id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) return;
        var context = catalog.CaptureContext();
        if (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
            && profile.GetString() != context.ProfileId) return;
        bool? marked = data.TryGetProperty("not_interested", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean() : null;
        // A delayed echoed event must not reverse the local pending intent.
        if (_pending.ContainsKey((context, id.GetString()!))) return;
        Changed?.Invoke(new(context, id.GetString()!, marked));
    }
}

public static class NotInterestedPolicy
{
    public static void FilterMarked(HomeSectionWithItems section)
    {
        if (!HidingTypes.Contains(section.SectionType)) return;
        for (var index = section.Items.Count - 1; index >= 0; index--)
            if (section.Items[index].UserState?.NotInterested == true) section.Items.RemoveAt(index);
    }

    private static readonly HashSet<string> HidingTypes = new(StringComparer.Ordinal)
    {
        "recommended_for_you", "because_you_watched", "similar_users_liked", "taste_match", "hidden_gems",
        "critically_acclaimed", "award_winners", "forgotten_favorites", "format_showcase", "editorial_spotlight",
        "seasonal_themed", "mood_collection", "trending_on_server", "most_watched", "trending_discover",
        "genre_roulette", "anniversaries", "short_watches",
    };

    public static bool Apply(IEnumerable<HomeSectionWithItems> sections, string itemId, bool marked)
    {
        var changed = false;
        foreach (var section in sections.Distinct())
        {
            for (var index = section.Items.Count - 1; index >= 0; index--)
            {
                var item = section.Items[index];
                if (item.ContentId != itemId) continue;
                (item.UserState ??= new()).NotInterested = marked;
                if (marked && HidingTypes.Contains(section.SectionType)) { section.Items.RemoveAt(index); changed = true; }
            }
        }
        return changed;
    }
}
