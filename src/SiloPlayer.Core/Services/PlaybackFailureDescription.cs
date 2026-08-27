using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public sealed record PlaybackFailurePresentation(string Title, string Message, bool CanRetry);

/// <summary>Current Silo WebUI parity mapping for protocol-v3 terminal decisions.</summary>
public static class PlaybackFailureDescription
{
    public static PlaybackFailurePresentation? DescribeTransport(ApiException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.StatusCode == 426 || error.ErrorCode == "client_upgrade_required")
        {
            return new PlaybackFailurePresentation(
                "Update required",
                "This server speaks a newer playback protocol than this app. Install the current Silo for Windows release to continue.",
                CanRetry: false);
        }
        if (error.ErrorCode == "playback_session_not_found")
        {
            return new PlaybackFailurePresentation(
                "Playback session expired",
                "This playback session is no longer active. Start it again to keep watching.",
                CanRetry: true);
        }
        if (error.StatusCode == 404)
        {
            return new PlaybackFailurePresentation(
                "This item is no longer available",
                "The file needed to play this item can't be found right now. Go back and try another version if one is available.",
                CanRetry: false);
        }
        if (error.StatusCode is 401 or 403)
        {
            return new PlaybackFailurePresentation(
                "Playback unavailable",
                "You do not have permission to play this item.",
                CanRetry: false);
        }
        if (error.StatusCode >= 500)
        {
            return new PlaybackFailurePresentation(
                "Playback unavailable",
                "Silo could not start playback right now. Please try again.",
                CanRetry: true);
        }
        return null;
    }

    public static PlaybackFailurePresentation Describe(PlaybackPlanTerminalException terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var fallback = terminal.ServerMessage;
        var (title, message) = terminal.Reason switch
        {
            "transcoding_disabled" => (
                "Transcoding is disabled",
                "Transcoding is disabled for your user. Ask your server administrator for access."),
            "audio_transcoding_disabled" => (
                "Audio transcoding is disabled",
                "This item requires audio conversion, but audio transcoding is disabled for your user."),
            "source_unavailable" => (
                "This video is no longer available",
                "The file needed to play it can't be found right now. Go back and try another version if one is available."),
            "source_metadata_incomplete" => (
                "This file hasn't finished scanning",
                "Silo doesn't know enough about this file yet to plan playback. Try again once the scan finishes."),
            "client_hls_unsupported" => (
                "This device can't play the stream",
                "Playing this file needs HLS, which this device doesn't support."),
            "adaptation_exhausted" or "adaptation_unavailable" => (
                "No playable version found",
                "Silo couldn't find a way to play this file on this device. Try another version if one is available."),
            "no_alternate_version" => (
                "No playable version found",
                fallback ?? "Silo couldn't find a way to play this file on this device. Try another version if one is available."),
            "hdr_transcode_unsupported" or "dv_conversion_unsupported" => (
                "This HDR format can't be converted",
                "This file's dynamic range can't be converted for this device, and it can't be played as-is."),
            "video_conversion_unsupported" or "audio_conversion_unsupported" => (
                "This file can't be converted",
                "Silo can't convert this file into something this device can play."),
            "conversion_tool_unavailable" or "transcode_node_unavailable" or
                "transcode_node_capability_unavailable" or "transcode_start_failed" => (
                "Playback unavailable",
                fallback ?? "The server couldn't start converting this file. Please try again."),
            "capacity_unavailable" => (
                "The server is busy",
                "There's no capacity to convert this file right now. Please try again in a moment."),
            "session_expired" => (
                "Playback session expired",
                "This playback session is no longer active. Start it again to keep watching."),
            "policy_denied" => (
                "Playback unavailable",
                "You do not have permission to play this item."),
            "subtitle_burn_in_source_unsupported" or "subtitle_codec_unsupported" or
                "subtitle_conversion_unsupported" or "subtitle_track_invalid" or
                "subtitle_track_unavailable" or "subtitle_unavailable_in_version" or
                "subtitle_artifact_unavailable" => (
                "That subtitle track can't be used",
                fallback ?? "Silo couldn't prepare the selected subtitles for this device. Try a different track."),
            "recovery_exhausted" => (
                "Playback failed",
                "Playback failed after repeated recovery attempts."),
            _ => ("Playback unavailable", fallback ?? "Silo could not start playback."),
        };

        return new PlaybackFailurePresentation(title, message, terminal.Retryable);
    }
}
