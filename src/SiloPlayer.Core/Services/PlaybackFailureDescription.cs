using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services;

public sealed record PlaybackFailurePresentation(string Title, string Message, bool CanRetry);

/// <summary>Current Silo WebUI parity mapping for protocol-v3 terminal decisions.</summary>
public static class PlaybackFailureDescription
{
    // Display text only: titles and messages follow the interface language with
    // the WebUI's catalog keys; desktop wording without a catalog equivalent
    // and every server-supplied message stay as they are.
    private static string T(string key, string english) => AppStrings.Get(key, fallback: english);

    public static PlaybackFailurePresentation? DescribeTransport(ApiException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var accountLimit = DescribeAccountLimit(error.ErrorCode, error.Message, retryable: true);
        if (accountLimit != null)
            return accountLimit;
        if (error.StatusCode == 426 || error.ErrorCode == "client_upgrade_required")
        {
            return new PlaybackFailurePresentation(
                T("web.player.error.upgradeRequired.title", "Update required"),
                "This server speaks a newer playback protocol than this app. Install the current Silo for Windows release to continue.",
                CanRetry: false);
        }
        if (error.ErrorCode == "playback_session_not_found")
        {
            return new PlaybackFailurePresentation(
                T("web.player.error.sessionExpired.title", "Playback session expired"),
                T("web.player.error.sessionExpired.message", "This playback session is no longer active. Start it again to keep watching."),
                CanRetry: true);
        }
        if (error.StatusCode == 403 && error.ErrorCode == "transcoding_disabled")
        {
            return new PlaybackFailurePresentation(
                T("web.player.error.transcodingDisabled.title", "Transcoding is disabled"),
                T("web.player.error.transcodingDisabled.message", "Transcoding is disabled for your user. Ask your server administrator for access."),
                CanRetry: false);
        }
        if (error.StatusCode == 403 && error.ErrorCode == "audio_transcoding_disabled")
        {
            return new PlaybackFailurePresentation(
                T("web.player.error.audioTranscodingDisabled.title", "Audio transcoding is disabled"),
                T("web.player.error.audioTranscodingDisabled.message", "This item requires audio conversion, but audio transcoding is disabled for your user."),
                CanRetry: false);
        }
        if (error.StatusCode == 404)
        {
            return new PlaybackFailurePresentation(
                T("web.player.error.itemUnavailable.title", "This item is no longer available"),
                T("web.player.error.itemUnavailable.message", "The file needed to play this item can't be found right now. Go back and try another version if one is available."),
                CanRetry: false);
        }
        if (error.StatusCode is 401 or 403)
        {
            return new PlaybackFailurePresentation(
                T("web.player.watch.playbackUnavailable", "Playback unavailable"),
                T("web.player.error.notPermitted.message", "You do not have permission to play this item."),
                CanRetry: false);
        }
        if (error.StatusCode >= 500)
        {
            return new PlaybackFailurePresentation(
                T("web.player.watch.playbackUnavailable", "Playback unavailable"),
                T("web.player.error.serverError.start", "Silo could not start playback right now. Please try again."),
                CanRetry: true);
        }
        return null;
    }

    public static PlaybackFailurePresentation Describe(PlaybackPlanTerminalException terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var accountLimit = DescribeAccountLimit(terminal.Reason, terminal.ServerMessage, terminal.Retryable);
        if (accountLimit != null)
            return accountLimit;
        var fallback = terminal.ServerMessage;
        var (title, message) = terminal.Reason switch
        {
            "transcoding_disabled" => (
                T("web.player.error.transcodingDisabled.title", "Transcoding is disabled"),
                T("web.player.error.transcodingDisabled.message", "Transcoding is disabled for your user. Ask your server administrator for access.")),
            "audio_transcoding_disabled" => (
                T("web.player.error.audioTranscodingDisabled.title", "Audio transcoding is disabled"),
                T("web.player.error.audioTranscodingDisabled.message", "This item requires audio conversion, but audio transcoding is disabled for your user.")),
            "source_unavailable" => (
                T("web.player.error.sourceUnavailable.title", "This video is no longer available"),
                T("web.player.error.sourceUnavailable.message", "The file needed to play it can't be found right now. Go back and try another version if one is available.")),
            "source_metadata_incomplete" => (
                T("web.player.error.sourceMetadataIncomplete.title", "This file hasn't finished scanning"),
                T("web.player.error.sourceMetadataIncomplete.message", "Silo doesn't know enough about this file yet to plan playback. Try again once the scan finishes.")),
            // Desktop wording: the catalog's HLS messages name a browser.
            "client_hls_unsupported" => (
                "This device can't play the stream",
                "Playing this file needs HLS, which this device doesn't support."),
            "adaptation_exhausted" or "adaptation_unavailable" => (
                T("web.player.error.noPlayableVersion.title", "No playable version found"),
                T("web.player.error.noPlayableVersion.message", "Silo couldn't find a way to play this file on this device. Try another version if one is available.")),
            "no_alternate_version" => (
                T("web.player.error.noPlayableVersion.title", "No playable version found"),
                fallback ?? T("web.player.error.noPlayableVersion.message", "Silo couldn't find a way to play this file on this device. Try another version if one is available.")),
            "hdr_transcode_unsupported" or "dv_conversion_unsupported" => (
                T("web.player.error.hdrConversionUnsupported.title", "This HDR format can't be converted"),
                T("web.player.error.hdrConversionUnsupported.message", "This file's dynamic range can't be converted for this device, and it can't be played as-is.")),
            "video_conversion_unsupported" or "audio_conversion_unsupported" => (
                T("web.player.error.conversionUnsupported.title", "This file can't be converted"),
                T("web.player.error.conversionUnsupported.message", "Silo can't convert this file into something this device can play.")),
            "conversion_tool_unavailable" or "transcode_node_unavailable" or
                "transcode_node_capability_unavailable" or "transcode_start_failed" => (
                T("web.player.watch.playbackUnavailable", "Playback unavailable"),
                fallback ?? T("web.player.error.conversionUnavailable.message", "The server couldn't start converting this file. Please try again.")),
            "capacity_unavailable" => (
                T("web.player.error.capacityUnavailable.title", "The server is busy"),
                T("web.player.error.capacityUnavailable.message", "There's no capacity to convert this file right now. Please try again in a moment.")),
            "session_expired" => (
                T("web.player.error.sessionExpired.title", "Playback session expired"),
                T("web.player.error.sessionExpired.message", "This playback session is no longer active. Start it again to keep watching.")),
            "policy_denied" => (
                T("web.player.watch.playbackUnavailable", "Playback unavailable"),
                T("web.player.error.notPermitted.message", "You do not have permission to play this item.")),
            "subtitle_burn_in_source_unsupported" or "subtitle_codec_unsupported" or
                "subtitle_conversion_unsupported" or "subtitle_track_invalid" or
                "subtitle_track_unavailable" or "subtitle_unavailable_in_version" or
                "subtitle_artifact_unavailable" => (
                T("web.player.watch.subtitleTrackUnusable", "That subtitle track can't be used"),
                fallback ?? T("web.player.error.subtitleUnusable.message", "Silo couldn't prepare the selected subtitles for this device. Try a different track.")),
            "recovery_exhausted" => (
                T("web.player.error.recoveryExhausted.title", "Playback failed"),
                T("web.player.error.recoveryExhausted.message", "Playback failed after repeated recovery attempts.")),
            _ => (T("web.player.watch.playbackUnavailable", "Playback unavailable"), fallback ?? T("web.player.watch.startFailed", "Silo could not start playback.")),
        };

        return new PlaybackFailurePresentation(title, message, terminal.Retryable);
    }

    /// <summary>Recognizes account-limit aliases; conversion limits only help when a transcode is retiring.</summary>
    public static bool IsAccountLimitReason(string? reason, bool includeTranscode = true)
    {
        var normalizedReason = NormalizeAccountLimitReason(reason);
        return normalizedReason == "stream_limit_reached" ||
            (includeTranscode && normalizedReason == "transcode_limit_reached");
    }

    private static string? NormalizeAccountLimitReason(string? reason) => reason?.Trim().ToLowerInvariant() switch
    {
        "stream_limit_reached" or "too_many_streams" => "stream_limit_reached",
        "transcode_limit_reached" or "too_many_transcodes" => "transcode_limit_reached",
        _ => null,
    };

    internal static PlaybackFailurePresentation? DescribeAccountLimit(string reason, string? serverMessage, bool retryable)
    {
        var title = NormalizeAccountLimitReason(reason) switch
        {
            "stream_limit_reached" => T("web.player.error.streamLimitReached.title", "Streaming limit reached"),
            "transcode_limit_reached" => T("web.player.error.transcodeLimitReached.title", "Video conversion limit reached"),
            _ => null,
        };
        if (title == null)
            return null;
        var message = string.IsNullOrWhiteSpace(serverMessage)
            ? T("web.player.error.limitReached.message", "Stop anything else playing or paused on your account, then try again.")
            : serverMessage.Trim();
        return new PlaybackFailurePresentation(title, message, retryable);
    }
}
