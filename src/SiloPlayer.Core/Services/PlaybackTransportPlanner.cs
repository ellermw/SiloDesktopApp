using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Raised when a playback-start response describes an impossible transport
/// combination for the native client.
/// </summary>
public sealed class PlaybackTransportContractException(string message)
    : InvalidOperationException(message);

/// <summary>
/// Converts the server's semantic playback decision and stream descriptor into
/// the concrete transport the desktop player must prepare.
/// </summary>
public static class PlaybackTransportPlanner
{
    public static PlaybackTransportPlan Plan(PlaybackStartResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Plan(response.PlayMethod, response.StreamUrl, response.PlaybackInfo?.StreamType);
    }

    public static PlaybackTransportPlan Plan(
        string? playMethod,
        string? streamUrl,
        string? reportedStreamType = null)
    {
        var semanticMethod = ParseSemanticMethod(playMethod);
        var hlsTransport = IsHlsStreamUrl(streamUrl) ||
            string.Equals(reportedStreamType?.Trim(), "hls", StringComparison.OrdinalIgnoreCase);

        return semanticMethod switch
        {
            PlaybackSemanticMethod.Direct when hlsTransport =>
                throw new PlaybackTransportContractException(
                    "A direct-play session cannot be delivered over an HLS transport."),

            PlaybackSemanticMethod.Direct => new PlaybackTransportPlan(
                semanticMethod,
                PlaybackTransportKind.DirectProgressive,
                RequiresTranscodeStartPreparation: false),

            PlaybackSemanticMethod.Remux when hlsTransport => new PlaybackTransportPlan(
                semanticMethod,
                PlaybackTransportKind.RemuxHls,
                RequiresTranscodeStartPreparation: true),

            PlaybackSemanticMethod.Remux => new PlaybackTransportPlan(
                semanticMethod,
                PlaybackTransportKind.RemuxProgressive,
                RequiresTranscodeStartPreparation: false),

            // A transcode start response always needs POST /playback/transcode/start.
            // The stream_url on POST /playback/start is only a descriptor/placeholder.
            PlaybackSemanticMethod.Transcode => new PlaybackTransportPlan(
                semanticMethod,
                PlaybackTransportKind.TranscodeHls,
                RequiresTranscodeStartPreparation: true),

            _ => throw new PlaybackTransportContractException(
                $"Unsupported playback method '{playMethod}'."),
        };
    }

    /// <summary>
    /// Detects HLS from the URL itself. URL evidence deliberately wins over a
    /// stale <c>playback_info.stream_type</c>; distributed remux currently returns
    /// a <c>/stream/transcode/.../master.m3u8</c> URL while reporting
    /// <c>progressive</c> metadata.
    /// </summary>
    public static bool IsHlsStreamUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
            return false;

        var value = streamUrl.Trim();
        string path;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            path = absoluteUri.AbsolutePath;
        }
        else
        {
            var delimiter = value.IndexOfAny(['?', '#']);
            path = delimiter >= 0 ? value[..delimiter] : value;
        }

        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/stream/transcode/", StringComparison.OrdinalIgnoreCase);
    }

    private static PlaybackSemanticMethod ParseSemanticMethod(string? playMethod)
    {
        return playMethod?.Trim().ToLowerInvariant() switch
        {
            "direct" => PlaybackSemanticMethod.Direct,
            "remux" => PlaybackSemanticMethod.Remux,
            "transcode" => PlaybackSemanticMethod.Transcode,
            _ => throw new PlaybackTransportContractException(
                $"Unsupported playback method '{playMethod ?? "<null>"}'."),
        };
    }
}
