using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public class LogsLiveBuffer<T> where T : LogsEntry
{
    private readonly int _limit;
    private readonly List<T> _rows = [];
    public LogsLiveBuffer(int limit = 100) => _limit = Math.Clamp(limit, 1, 200);
    public IReadOnlyList<T> Rows => _rows;
    public void Snapshot(IEnumerable<T> entries)
    {
        _rows.Clear();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Id) && ids.Add(entry.Id)) _rows.Add(entry);
            if (_rows.Count == _limit) break;
        }
    }
    public void Append(T entry)
    {
        _rows.RemoveAll(row => row.Id == entry.Id);
        _rows.Insert(0, entry);
        if (_rows.Count > _limit) _rows.RemoveRange(_limit, _rows.Count - _limit);
    }
    public void Clear() => _rows.Clear();
}

public sealed class LogsLiveBuffer(int limit = 100) : LogsLiveBuffer<LogsOperationalEntry>(limit);

public sealed class LogsCursorHistory
{
    private readonly List<string?> _cursors = [];
    public bool IsHistory => _cursors.Count > 0;
    public int Page => _cursors.Count;
    public string? Cursor => _cursors.LastOrDefault();
    public bool CanNewer => _cursors.Count > 1;
    public void Start() { _cursors.Clear(); _cursors.Add(null); }
    public void Older(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || _cursors.Contains(cursor, StringComparer.Ordinal) || _cursors.Count >= 100)
            throw new InvalidDataException(LogsText.T("logs.native.logs.cursorRepeated"));
        _cursors.Add(cursor);
    }
    public void Newer() { if (CanNewer) _cursors.RemoveAt(_cursors.Count - 1); }
    public void Clear() => _cursors.Clear();
}

public sealed record LogsSocketFrame
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public string Type { get; init; } = "";
    public LogsOperationalEntry? Entry { get; init; }
    public LogsAuditEntry? AuditEntry { get; init; }
    public LogsOperationalEntry[] Entries { get; init; } = [];
    public LogsAuditEntry[] AuditEntries { get; init; } = [];
    public string? NextCursor { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }

    public static LogsSocketFrame? Parse(string json, string stream)
    {
        try
        {
            if (json.Length > 2 * 1024 * 1024 || stream is not ("app" or "audit")) return null;
            var root = JsonNode.Parse(json)?.AsObject();
            if (root?["stream"]?.GetValue<string>() != stream) return null;
            var type = root["type"]?.GetValue<string>();
            if (type == "error") return new() { Type = type, Code = root["code"]?.GetValue<string>(), Message = root["message"]?.GetValue<string>() };
            if (type is not ("snapshot" or "append")) return null;
            var objects = type == "append" ? new[] { root["entry"] } : root["entries"]?.AsArray().ToArray();
            if (objects == null || objects.Length > 200) return null;
            var app = new List<LogsOperationalEntry>();
            var audit = new List<LogsAuditEntry>();
            foreach (var item in objects)
            {
                if (item is not JsonObject entry || !NormalizeIds(entry)) return null;
                if (stream == "app")
                {
                    var row = entry.Deserialize<LogsOperationalEntry>(Options);
                    if (row == null || string.IsNullOrEmpty(row.Id) || row.Timestamp == default || string.IsNullOrEmpty(row.Level) || string.IsNullOrEmpty(row.Component)) return null;
                    app.Add(row);
                }
                else
                {
                    var row = entry.Deserialize<LogsAuditEntry>(Options);
                    if (row == null || string.IsNullOrEmpty(row.Id) || row.Timestamp == default) return null;
                    audit.Add(row);
                }
            }
            return new() { Type = type, Entry = type == "append" ? app.FirstOrDefault() : null, AuditEntry = type == "append" ? audit.FirstOrDefault() : null, Entries = [.. app], AuditEntries = [.. audit], NextCursor = root["next_cursor"]?.GetValue<string>() };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException) { return null; }
    }

    private static bool NormalizeIds(JsonObject entry)
    {
        foreach (var name in new[] { "id", "user_id", "impersonator_user_id", "target_id" })
        {
            if (entry[name] is not JsonValue value) continue;
            if (value.TryGetValue<string>(out _)) continue;
            if (!value.TryGetValue<long>(out var numeric) || numeric < 1 || numeric > 9007199254740991) return false;
            entry[name] = numeric.ToString(CultureInfo.InvariantCulture);
        }
        return entry["id"] is JsonValue id && id.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text);
    }
}

public sealed record LogsPlaybackSummary(int ApplicationCount, int FFmpegCount, int AuditCount, DateTimeOffset? FirstSeen, DateTimeOffset? LastSeen, string[] Nodes)
{
    public static LogsPlaybackSummary From(string session, IEnumerable<LogsOperationalEntry> app, IEnumerable<LogsAuditEntry> audit)
    {
        var matchingApp = app.Where(row => row.PlaybackSessionId == session).ToArray();
        var matchingAudit = audit.Where(row => row.PlaybackSessionId == session).ToArray();
        var rows = matchingApp.Cast<LogsEntry>().Concat(matchingAudit).ToArray();
        return new(matchingApp.Length, matchingApp.Count(row => row.Component == "ffmpeg"), matchingAudit.Length,
            rows.Length == 0 ? null : rows.Min(row => row.Timestamp), rows.Length == 0 ? null : rows.Max(row => row.Timestamp),
            rows.Select(row => row.NodeId).Where(node => !string.IsNullOrEmpty(node)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray());
    }
}

public static class LogsPresentation
{
    public static string ShortId(string value) => value.Length <= 12 ? value : value[..8] + "..." + value[^4..];
    public static string ClientIp(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var slash = value.LastIndexOf('/');
        return slash >= 0 && slash < value.Length - 1 && value[(slash + 1)..].All(character => character is >= '0' and <= '9') ? value[..slash] : value;
    }
    public static string Attribute(LogsOperationalEntry entry, string key) => entry.Attrs?.TryGetValue(key, out var value) == true && (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.String && value.GetString()?.Length > 0) ? value.ToString() : "-";
    public static string ChangeSummary(LogsAuditChange change, string? target)
    {
        if (change.Before == null && change.After == null) return AuditText("changed");
        if (target == "profile" && change.Field == "pin")
        {
            if (change.Before != null && change.After != null) return AuditText(change.Before == change.After ? "changed" : change.After == "true" ? "pinSet" : "pinCleared");
            return Pin(change.Before) + " → " + Pin(change.After);
        }
        return ChangeValue(change.Before, target, change.Field) + " → " + ChangeValue(change.After, target, change.Field);
    }
    private static string AuditText(string key) => LogsText.T("web.admin.logs.auditValue." + key);
    private static string Pin(string? value) => AuditText(value == null ? "absent" : value == "true" ? "pinSet" : "pinNotSet");
    private static string ChangeValue(string? text, string? target, string field)
    {
        if (text == null) return AuditText("absent");
        try
        {
            using var document = JsonDocument.Parse(text);
            var value = document.RootElement;
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (field == "access_group_id") return AuditText("none");
                if (target == "access_group") return AuditText(field == "library_ids" ? "allLibraries" : field == "allowed_permissions" ? "allAssignable" : "unset");
                return AuditText("inherit");
            }
            if (value.ValueKind == JsonValueKind.Array)
                return value.GetArrayLength() == 0 ? AuditText(field == "scopes" ? "fullAccess" : "none") : string.Join(", ", value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : JsonSerializer.Serialize(item)));
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : JsonSerializer.Serialize(value);
        }
        catch (JsonException) { return text; }
    }
}

public static class DiagnosticsPresentation
{
    public static string Platform(string value) => value switch { "android" => "Android", "android-tv" => "Android TV", "ios" => "iOS", "tvos" => "tvOS", "roku" => "Roku", _ => Token(value.Replace('-', ' ')) };
    public static string Token(string value) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.Replace('_', ' '));
    public static string Bytes(long? value)
    {
        if (value == null) return "—";
        if (value < 1024) return value.Value.ToString(CultureInfo.CurrentCulture) + " B";
        var size = (double)value / 1024;
        var units = new[] { "KiB", "MiB", "GiB", "TiB" };
        var index = 0;
        while (size >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
        return size.ToString(size >= 10 ? "N0" : "N1", CultureInfo.CurrentCulture) + " " + units[index];
    }
    public static string ManifestValue(JsonElement manifest, string section, string key)
    {
        return manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty(section, out var area) && area.ValueKind == JsonValueKind.Object && area.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
