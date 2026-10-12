using System.Text;
using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.SiloCast;

public static class SiloCastProtocol
{
    public const int Version = 2;

    public static JsonObject CreateMessage(string type, string? payloadName = null, JsonObject? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var message = new JsonObject { ["v"] = Version, ["type"] = type };
        if (payloadName is not null)
            message[payloadName] = payload?.DeepClone();
        return message;
    }

    // Apple registry compatibility: preserve scheme/host/path casing and port.
    public static string ServerId(string url) => Convert.ToBase64String(Encoding.UTF8.GetBytes(url.Trim().TrimEnd('/')))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
