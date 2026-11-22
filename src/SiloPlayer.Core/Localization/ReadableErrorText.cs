using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// The text a failed save, upload, download or search shows (official <c>readableErrorText</c>, #4537). A failure that carries words
/// written for the user is shown as it is: a server problem detail through <see cref="ApiProblemText"/>, or an error the action threw
/// on purpose. A failure written in developer English is not: a request that never got an answer (no connection, a refused or reset
/// socket, a timeout or cancellation), a response that was not a problem document ("HTTP 502" from a proxy page), or a body that did
/// not parse. Those get the action's own message, which is already translated.
/// </summary>
public static class ReadableErrorText
{
    /// <summary>The message to show for <paramref name="error"/>, or <paramref name="fallback"/> when its own text is developer English or empty.</summary>
    public static string Get(Exception? error, string fallback)
    {
        if (error is null) return fallback;
        if (error is AggregateException { InnerExceptions.Count: 1 } wrapper) return Get(wrapper.InnerExceptions[0], fallback);
        if (IsDeveloperText(error)) return fallback;
        var text = error is ApiException api ? ApiProblemText.Display(api) : error.Message;
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    /// <summary>Whether the error's own message is developer English the user cannot act on.</summary>
    public static bool IsDeveloperText(Exception error) => error switch
    {
        // A response without a problem body carries only its status line ("HTTP 502").
        ApiException api => api.ProblemType is null && string.IsNullOrWhiteSpace(api.ProblemDetail)
            && string.Equals(api.Message, "HTTP " + api.StatusCode.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
        // OperationCanceledException covers timeouts (TaskCanceledException) and a superseded request's cancellation.
        HttpRequestException or OperationCanceledException or TimeoutException or SocketException or IOException or JsonException or ObjectDisposedException => true,
        _ => false,
    };
}
