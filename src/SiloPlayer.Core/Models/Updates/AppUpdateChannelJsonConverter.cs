using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Updates;

/// <summary>An unknown channel must not quarantine unrelated profile/playback preferences.</summary>
public sealed class AppUpdateChannelJsonConverter : JsonConverter<AppUpdateChannel>
{
    // Receive JSON null too; the serializer otherwise rejects it for a value type.
    public override bool HandleNull => true;

    public override AppUpdateChannel Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(AppUpdateChannel), number))
            return (AppUpdateChannel)number;
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<AppUpdateChannel>(reader.GetString(), true, out var channel) && Enum.IsDefined(channel))
            return channel;
        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject) reader.Skip();
        return AppUpdateChannel.Stable;
    }
    public override void Write(Utf8JsonWriter writer, AppUpdateChannel value, JsonSerializerOptions options)
        => writer.WriteNumberValue((int)(Enum.IsDefined(value) ? value : AppUpdateChannel.Stable));
}
