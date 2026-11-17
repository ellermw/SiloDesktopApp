using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.Admin.Users;

public static class AdminUserHomeSections
{
    private static readonly HashSet<string> Writable = new(StringComparer.Ordinal)
    {
        "id", "section_id", "position", "hidden", "removed", "section_type", "title", "featured", "item_limit", "config",
        "is_user_added", "user_section_type", "user_title", "user_config"
    };
    public static JsonArray Change(JsonArray settings, JsonArray saved, string sectionId, bool? hidden = null, int direction = 0)
    {
        if (settings.Count > 500 || saved.Count > 500) throw new InvalidOperationException("Narrow the Home Sections set before editing.");
        var result = new JsonArray();
        foreach (var node in saved)
        {
            var row = node?.DeepClone().AsObject() ?? throw new InvalidDataException("Invalid section override.");
            row.Remove("created_at"); row.Remove("updated_at");
            if (row.Any(pair => !Writable.Contains(pair.Key))) throw new InvalidDataException("A section has unknown writable metadata. Reload/review with a compatible client.");
            result.Add(row);
        }
        var ordered = settings.OfType<JsonObject>().OrderBy(row => row.Count("position")).ToList();
        var index = ordered.FindIndex(row => row.Text("id") == sectionId);
        if (index < 0) throw new ArgumentException("Section no longer exists.");
        JsonObject Override(JsonObject source)
        {
            var existing = result.OfType<JsonObject>().FirstOrDefault(row => row.Text("section_id") == source.Text("id") ||
                row.Flag("is_user_added") && row.Text("id") == source.Text("id"));
            if (existing != null) return existing;
            var created = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["section_id"] = source.Text("id"), ["hidden"] = source.Flag("hidden"), ["removed"] = false, ["is_user_added"] = false };
            result.Add(created);
            return created;
        }
        if (hidden.HasValue) Override(ordered[index])["hidden"] = hidden.Value;
        if (direction != 0)
        {
            var next = Math.Clamp(index + Math.Sign(direction), 0, ordered.Count - 1);
            if (next != index)
            {
                (ordered[index], ordered[next]) = (ordered[next], ordered[index]);
                for (var position = 0; position < ordered.Count; position++)
                    Override(ordered[position])["position"] = position;
            }
        }
        return result;
    }
}
