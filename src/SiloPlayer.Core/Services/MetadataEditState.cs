using SiloPlayer.Core.Models.Catalog;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

public sealed class MetadataEditState
{
    public Dictionary<string, object?> Fields { get; } = [];
    private readonly Dictionary<string, object?> _original;
    private readonly Dictionary<int, bool> _overrides = [];
    private readonly bool _lockable;
    public static readonly IReadOnlyDictionary<string, int> LockMap = new Dictionary<string, int>
    {
        ["title"]=0,["sort_title"]=0,["original_title"]=0,["tagline"]=0,["overview"]=1,
        ["genres"]=2,["studios"]=3,["networks"]=3,["countries"]=8,["runtime"]=7,["content_rating"]=9,
        ["rating_imdb"]=6,["rating_tmdb"]=6,["rating_rt_critic"]=6,["rating_rt_audience"]=6,
        ["air_time"]=11,["air_timezone"]=11,["year"]=13,["release_date"]=13,["first_air_date"]=13,["last_air_date"]=13,
    };
    public MetadataEditState(MediaItemDetail item)
    {
        _lockable = item.Type is "movie" or "series";
        var serialized = JsonSerializer.SerializeToElement(item, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        foreach (var key in new[] { "title","sort_title","original_title","overview","tagline","content_rating","year","runtime","genres","studios","networks","countries","release_date","first_air_date","last_air_date","air_time","air_timezone","air_date","status","rating_imdb","rating_tmdb","rating_rt_critic","rating_rt_audience","imdb_id","tmdb_id","tvdb_id","season_number","episode_number" })
        {
            var value = serialized.GetProperty(key);
            Fields[key] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.Array => value.EnumerateArray().Select(v => v.GetString()!).ToArray(),
                _ => key.StartsWith("rating_") || key.EndsWith("_number") ? null : "",
            };
        }
        if (!string.IsNullOrEmpty(item.ShowStatus)) Fields["status"] = item.ShowStatus;
        _original = new Dictionary<string, object?>(Fields);
    }
    public void Set(string key, object? value)
    {
        Fields[key] = value;
        if (_lockable && LockMap.TryGetValue(key, out var field)) _overrides[field] = true;
    }
    public void SetLock(int field, bool locked) { if (_lockable) _overrides[field] = locked; }
    public HashSet<int> EffectiveLocks(IReadOnlyCollection<int> currentLocks)
    {
        var locks = currentLocks.ToHashSet();
        foreach (var (field, locked) in _overrides) { if (locked) locks.Add(field); else locks.Remove(field); }
        return locks;
    }
    public Dictionary<string, object?> Changes(IReadOnlyCollection<int> currentLocks)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in Fields)
        {
            if (JsonSerializer.Serialize(value) == JsonSerializer.Serialize(_original[key])) continue;
            result[key] = key is "release_date" or "first_air_date" or "last_air_date" or "air_time" or "air_date" && value is "" ? null : value;
        }
        var locks = EffectiveLocks(currentLocks).Order().ToArray();
        if (_lockable && !locks.SequenceEqual(currentLocks.Order())) result["locked_fields"] = locks;
        return result;
    }
}
