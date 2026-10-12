using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.SiloCast;

public static class SiloCastFrameCodec
{
    public const int MaxFrameBytes = 1 << 20;

    public static async Task<JsonObject?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), ct).ConfigureAwait(false);
        if (first == 0)
            return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaxFrameBytes)
            throw new InvalidDataException("Invalid SiloCast frame length.");
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(payload) as JsonObject
                ?? throw new InvalidDataException("SiloCast frames must contain JSON objects.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Malformed SiloCast JSON frame.", exception);
        }
    }

    public static async Task WriteAsync(Stream stream, JsonObject message, CancellationToken ct)
    {
        using var payload = new LimitedPayloadStream();
        JsonSerializer.Serialize(payload, message);
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload.GetBuffer().AsMemory(0, (int)payload.Length), ct).ConfigureAwait(false);
    }

    private sealed class LimitedPayloadStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > MaxFrameBytes)
                throw new InvalidDataException("SiloCast frame exceeds 1 MiB.");
            base.Write(buffer, offset, count);
        }
    }
}
