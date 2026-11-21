using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminAccessPage<T>
{
    [JsonRequired] public List<T> Items { get; set; } = [];
    [JsonRequired] public AdminAccessContinuation? Page { get; set; }
}
public sealed class AdminAccessContinuation
{
    [JsonRequired] public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}
public sealed class AdminAccessAccount
{
    [JsonConverter(typeof(AdminAccessStringIdConverter))] public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsOwner { get; set; }
    public bool Enabled { get; set; }
    public bool BreakGlass { get; set; }
    public bool PasswordLogin { get; set; }
    public bool CanManage(string viewerId, bool viewerIsOwner) => Id == viewerId || !IsOwner && (Role != "admin" || viewerIsOwner);
    public override string ToString() => Username;
}
// Users can be numeric in older projections; key/policy/plugin IDs still require strings.
public sealed class AdminAccessStringIdConverter : JsonConverter<string>
{
    public override string Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options) => reader.TokenType switch
    {
        System.Text.Json.JsonTokenType.String => reader.GetString()!,
        System.Text.Json.JsonTokenType.Number when reader.TryGetInt64(out var id) && id > 0 => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new System.Text.Json.JsonException(),
    };
    public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value);
}
