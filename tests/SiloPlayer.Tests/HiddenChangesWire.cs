using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Shared request-body helpers for the hidden-change tests (synthetic values only).</summary>
internal static class HiddenChangesWire
{
    internal static HttpResponseMessage SavedAck()
    {
        var response = AdminMonitorKit.Json("""{"values":{},"restart_required":false}""");
        response.Headers.ETag = new("\"two\"");
        return response;
    }

    internal static string[] SentKeys(AdminMonitorKit kit)
    {
        using var body = JsonDocument.Parse(kit.Http.Bodies.Last(text => text.Length > 0));
        return body.RootElement.GetProperty("values").EnumerateObject().Select(entry => entry.Name).Order().ToArray();
    }

    internal static string SentBody(AdminMonitorKit kit) => kit.Http.Bodies.Last(text => text.Length > 0);
    internal static int Writes(AdminMonitorKit kit) => kit.Http.Requests.Count(request => !request.StartsWith("GET", StringComparison.Ordinal));
}
