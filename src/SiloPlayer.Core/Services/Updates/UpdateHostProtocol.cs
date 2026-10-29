using System.Buffers.Binary;
using System.Text.Json;

namespace SiloPlayer.Core.Services.Updates;

public sealed record UpdateHostMessage(string Kind, string? TransactionId = null, string? Text = null,
    long Completed = 0, long Total = 0, int ProcessId = 0, long StartUtcTicks = 0, DateTimeOffset? CooldownUntil = null);

/// <summary>Length-bounded frames; pipe endpoints separately authenticate process identity.</summary>
public static class UpdateHostProtocol
{
    private const int MaximumFrame = 65536;
    private static readonly HashSet<string> Kinds = ["visible", "progress", "staged", "close", "cancel", "activated", "ready", "confirmed", "failed"];
    public static async Task WriteAsync(Stream stream, UpdateHostMessage message, CancellationToken ct)
    {
        Validate(message);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payload.Length > MaximumFrame) throw new InvalidDataException("Update message exceeds limit.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header,payload.Length);
        await stream.WriteAsync(header,ct).ConfigureAwait(false);
        await stream.WriteAsync(payload,ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
    public static async Task<UpdateHostMessage> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header,ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumFrame) throw new InvalidDataException("Invalid update message length.");
        var payload = new byte[length]; await stream.ReadExactlyAsync(payload,ct).ConfigureAwait(false);
        var message = JsonSerializer.Deserialize<UpdateHostMessage>(payload) ?? throw new InvalidDataException("Empty update message.");
        Validate(message); return message;
    }
    private static void Validate(UpdateHostMessage message)
    {
        if (!Kinds.Contains(message.Kind) || message.Text?.Length > 2048 || message.TransactionId?.Length > 64
            || message.Completed < 0 || message.Total < 0)
            throw new InvalidDataException("Invalid update message.");
    }
}

public static class UpdateWindowPlacement
{
    // Native physical coordinates throughout; never mix XAML DIPs with screen pixels.
    public static (int X,int Y) Center(int x,int y,int width,int height,int dialogWidth,int dialogHeight) =>
        (checked(x+(width-dialogWidth)/2),checked(y+(height-dialogHeight)/2));
}
