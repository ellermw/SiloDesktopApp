using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContinuumPlayer.Core.Json;

/// <summary>
/// Deserializes a JSON property into a <see cref="string"/> regardless of
/// whether the source token is a string OR a number. Writes back as a string.
///
/// Why: the Continuum server returns <c>Person.id</c> as a JSON number
/// (<c>int64</c>) but the desktop client models it as a string end-to-end
/// (cast/crew person_id arrives as a string from a different code path, and
/// we want the whole pipeline uniform). Without this converter, numeric
/// <c>id</c> payloads blow up deserialization (B59 regression).
/// </summary>
public sealed class StringOrNumberJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException($"Expected string or number token, got {reader.TokenType}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
