using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Json;

/// <summary>
/// Accepts the timestamp shapes used by current and older Silo endpoints.
/// Empty/invalid legacy values are treated as unknown instead of rejecting
/// the complete response.
/// </summary>
public sealed class FlexibleNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var epoch))
        {
            try
            {
                // Millisecond epochs are already thirteen digits at modern dates.
                return Math.Abs(epoch) >= 100_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                    : DateTimeOffset.FromUnixTimeSeconds(epoch);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        using var ignored = JsonDocument.ParseValue(ref reader);
        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}
