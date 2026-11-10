using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Services.Updates;

// Files/FileCount and FileBytes/FileTotal are only set on "download" frames: the number of
// objects actually fetched and the object being fetched now. Older peers ignore them.
[method: JsonConstructor]
public sealed record UpdateHostMessage(string Kind, string? TransactionId = null, string? Text = null,
    long Completed = 0, long Total = 0, int ProcessId = 0, long StartUtcTicks = 0, DateTimeOffset? CooldownUntil = null,
    int Files = 0, int FileCount = 0, long FileBytes = 0, long FileTotal = 0)
{
    // Optional parameters do not preserve the original constructor for reflection or compiled callers.
    public UpdateHostMessage(string Kind, string? TransactionId, string? Text, long Completed, long Total,
        int ProcessId, long StartUtcTicks, DateTimeOffset? CooldownUntil)
        : this(Kind, TransactionId, Text, Completed, Total, ProcessId, StartUtcTicks, CooldownUntil, 0, 0, 0, 0) { }
}

/// <summary>Length-bounded frames; pipe endpoints separately authenticate process identity.</summary>
public static class UpdateHostProtocol
{
    private const int MaximumFrame = 65536;
    // "download" and "downloaded" travel from the client to its own update window while the
    // client downloads the package that the window then installs. "restart" and "later" travel
    // back: the user's answer once everything is downloaded. Client and window are one build.
    private static readonly HashSet<string> Kinds = ["visible", "progress", "staged", "close", "cancel", "activated", "ready", "confirmed", "failed",
        "download", "downloaded", "restart", "later"];
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
            || message.Completed < 0 || message.Total < 0 || message.Files < 0 || message.FileCount < 0
            || message.FileBytes < 0 || message.FileTotal < 0)
            throw new InvalidDataException("Invalid update message.");
    }
}

public static class UpdateWindowPlacement
{
    // Native physical coordinates throughout; never mix XAML DIPs with screen pixels.
    public static (int X,int Y) Center(int x,int y,int width,int height,int dialogWidth,int dialogHeight) =>
        (checked(x+(width-dialogWidth)/2),checked(y+(height-dialogHeight)/2));
}
