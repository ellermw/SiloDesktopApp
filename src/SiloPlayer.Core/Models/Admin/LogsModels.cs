using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Models.Admin;

public record LogsEntry
{
    public string Id { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public string? ClientIp { get; init; }
    public string? NodeId { get; init; }
    public string? PlaybackSessionId { get; init; }
    public string? RequestId { get; init; }
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
}

public sealed record LogsOperationalEntry : LogsEntry
{
    public string Level { get; init; } = "";
    public string Component { get; init; } = "";
    public string Message { get; init; } = "";
    public Dictionary<string, JsonElement>? Attrs { get; init; }
}

public sealed record LogsAuditEntry : LogsEntry
{
    public string? Action { get; init; }
    public LogsAuditChange[] Changes { get; init; } = [];
    public string? ImpersonatorUserId { get; init; }
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public string? PathPattern { get; init; }
    public long StatusCode { get; init; }
    public long DurationMs { get; init; }
    public string? TargetId { get; init; }
    public string? TargetType { get; init; }
    public string? UserAgent { get; init; }
}

public sealed record LogsAuditChange
{
    public string Field { get; init; } = "";
    public string? Before { get; init; }
    public string? After { get; init; }
}

public sealed record LogsSocketTicket
{
    public string Ticket { get; init; } = "";
    public int ExpiresIn { get; init; }
    public int MaxConnectionSeconds { get; init; }
    public string Protocol { get; init; } = "";
    public override string ToString() => nameof(LogsSocketTicket);
}

public sealed record LogsQuery
{
    public string? RequestId { get; init; }
    public string? PlaybackSessionId { get; init; }
    public string? Message { get; init; }
    public string? Level { get; init; }
    public string? Component { get; init; }
    public string? NodeId { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public string? Method { get; init; }
    public string? ClientIp { get; init; }
    public string? Action { get; init; }
    public string? ActorUserId { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? StatusCode { get; init; }
    public string? PathPrefix { get; init; }

    public string ToQuery(string stream, string? cursor = null, int limit = 100)
    {
        if (stream is not ("app" or "audit")) throw new ArgumentOutOfRangeException(nameof(stream));
        var query = new LogsQueryBuilder();
        query.Add("request_id", RequestId);
        query.Add("playback_session_id", PlaybackSessionId);
        if (stream == "app")
        {
            query.Add("q", Message);
            query.Add("level", NormalizeAll(Level));
            query.Add("component", NormalizeAll(Component));
            query.Add("node_id", NodeId);
        }
        else
        {
            query.Add("method", Method?.Trim().ToUpperInvariant());
            query.Add("client_ip", ClientIp);
            query.Add("action", Action);
            query.Add("actor_user_id", ActorUserId);
            query.Add("target_type", TargetType);
            query.Add("target_id", TargetId);
            query.Add("status_code", StatusCode);
            query.Add("path_prefix", PathPrefix);
        }
        query.Add("from", From);
        query.Add("to", To);
        query.Add("user_id", UserId);
        query.Add("session_id", SessionId);
        query.Add("cursor", cursor);
        query.Add("limit", Math.Clamp(limit, 1, 200).ToString(CultureInfo.InvariantCulture));
        return query.ToString();
    }

    private static string? NormalizeAll(string? value) => value?.Trim() is "all" or "" ? null : value?.Trim();
}

internal sealed class LogsQueryBuilder
{
    private readonly List<string> _parts = [];
    public void Add(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) _parts.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value.Trim()));
    }
    public override string ToString() => _parts.Count == 0 ? "" : "?" + string.Join("&", _parts);
}
