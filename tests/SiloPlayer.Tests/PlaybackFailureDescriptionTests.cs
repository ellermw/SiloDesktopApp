using SiloPlayer.Core.Services;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class PlaybackFailureDescriptionTests
{
    public static TheoryData<string, string, string> WebUiTerminalReasons => new()
    {
        { "transcoding_disabled", "Transcoding is disabled", "Transcoding is disabled for your user. Ask your server administrator for access." },
        { "audio_transcoding_disabled", "Audio transcoding is disabled", "This item requires audio conversion, but audio transcoding is disabled for your user." },
        { "source_unavailable", "This video is no longer available", "The file needed to play it can't be found right now. Go back and try another version if one is available." },
        { "source_metadata_incomplete", "This file hasn't finished scanning", "Silo doesn't know enough about this file yet to plan playback. Try again once the scan finishes." },
        { "adaptation_exhausted", "No playable version found", "Silo couldn't find a way to play this file on this device. Try another version if one is available." },
        { "dv_conversion_unsupported", "This HDR format can't be converted", "This file's dynamic range can't be converted for this device, and it can't be played as-is." },
        { "capacity_unavailable", "The server is busy", "There's no capacity to convert this file right now. Please try again in a moment." },
        { "stream_limit_reached", "Streaming limit reached", "Stop anything else playing or paused on your account, then try again." },
        { "transcode_limit_reached", "Video conversion limit reached", "Stop anything else playing or paused on your account, then try again." },
        { "session_expired", "Playback session expired", "This playback session is no longer active. Start it again to keep watching." },
        { "policy_denied", "Playback unavailable", "You do not have permission to play this item." },
        { "subtitle_track_unavailable", "That subtitle track can't be used", "Silo couldn't prepare the selected subtitles for this device. Try a different track." },
    };

    [Theory]
    [MemberData(nameof(WebUiTerminalReasons))]
    public void TerminalReasonsMatchCurrentWebUiCopy(string reason, string title, string message)
    {
        var description = PlaybackFailureDescription.Describe(
            new PlaybackPlanTerminalException(reason, null, retryable: true));

        Assert.Equal(title, description.Title);
        Assert.Equal(message, description.Message);
        Assert.True(description.CanRetry);
    }

    [Fact]
    public void ServerMessageIsPreservedForUnknownTerminalReasons()
    {
        var description = PlaybackFailureDescription.Describe(
            new PlaybackPlanTerminalException("future_reason", "New server guidance", retryable: false));

        Assert.Equal("Playback unavailable", description.Title);
        Assert.Equal("New server guidance", description.Message);
        Assert.False(description.CanRetry);
    }

    [Theory]
    [InlineData("stream_limit_reached", "Streaming limit reached", true)]
    [InlineData("transcode_limit_reached", "Video conversion limit reached", false)]
    public void AccountLimitMessagesPreserveTrimmedServerGuidanceAndRetryability(
        string reason, string title, bool retryable)
    {
        var description = PlaybackFailureDescription.Describe(
            new PlaybackPlanTerminalException(reason, "  End another account session first.\r\n", retryable));

        Assert.Equal(title, description.Title);
        Assert.Equal("End another account session first.", description.Message);
        Assert.Equal(retryable, description.CanRetry);
    }

    [Theory]
    [InlineData("stream_limit_reached", "")]
    [InlineData("stream_limit_reached", " \r\n\t")]
    [InlineData("transcode_limit_reached", "")]
    [InlineData("transcode_limit_reached", " \r\n\t")]
    public void AccountLimitMessagesUseViewerGuidanceWhenServerTextIsBlank(string reason, string message)
    {
        var description = PlaybackFailureDescription.Describe(
            new PlaybackPlanTerminalException(reason, message, retryable: true));

        Assert.Equal("Stop anything else playing or paused on your account, then try again.", description.Message);
        Assert.True(description.CanRetry);
    }

    public static IEnumerable<object[]> AccountLimitTransportFailures()
    {
        foreach (var alias in new[] { "too_many_streams", "stream_limit_reached", "too_many_transcodes", "transcode_limit_reached" })
        foreach (var code in new[] { alias, " \t" + alias.ToUpperInvariant() + "\r\n" })
        foreach (var status in new[] { 400, 401, 403, 404, 426, 429, 500, 503 })
        foreach (var message in new[] { "", " \r\n\t", "  End another account session first.\r\n" })
            yield return [code, status, message];
    }

    [Theory]
    [MemberData(nameof(AccountLimitTransportFailures))]
    public void TransportAccountLimitsMatchTerminalGuidanceBeforeGenericStatusHandling(
        string code, int status, string message)
    {
        var reason = code.Trim().ToLowerInvariant() is "too_many_streams" or "stream_limit_reached"
            ? "stream_limit_reached" : "transcode_limit_reached";
        var expected = PlaybackFailureDescription.Describe(
            new PlaybackPlanTerminalException(reason, message, retryable: true));

        var actual = PlaybackFailureDescription.DescribeTransport(new ApiException(code, message, status));

        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object?[]> AccountLimitExceptionMessages()
    {
        foreach (var alias in new[] { "stream_limit_reached", "transcode_limit_reached", "too_many_streams", "too_many_transcodes" })
        foreach (var reason in new[] { alias, " \t" + alias.ToUpperInvariant() + "\r\n" })
        foreach (var message in new string?[] { null, "", " \r\n\t", "  End another account session first.\r\n" })
        foreach (var retryable in new[] { false, true })
            yield return [reason, message, retryable];
    }

    [Theory]
    [MemberData(nameof(AccountLimitExceptionMessages))]
    public void AccountLimitExceptionMessageUsesTheSameViewerGuidanceAsThePresentation(
        string reason, string? message, bool retryable)
    {
        var error = new PlaybackPlanTerminalException(reason, message, retryable);
        var description = PlaybackFailureDescription.Describe(error);

        Assert.Equal($"{description.Title}: {description.Message}", error.Message);
        Assert.DoesNotContain("compatible playback route", error.Message);
        Assert.DoesNotContain(reason, error.Message);
        Assert.Equal(reason.Trim(), error.Reason);
        Assert.Equal(string.IsNullOrWhiteSpace(message) ? null : message.Trim(), error.ServerMessage);
        Assert.Equal(retryable, error.Retryable);
    }

    [Theory]
    [InlineData("stream_limit_reached", true)]
    [InlineData("too_many_streams", true)]
    [InlineData("transcode_limit_reached", false)]
    [InlineData("too_many_transcodes", false)]
    public void AccountLimitRecognitionSharesAliasesAndOnlyIncludesConversionWhenRequested(string alias, bool streamLimit)
    {
        foreach (var reason in new[] { alias, " \t" + alias.ToUpperInvariant() + "\r\n" })
        {
            Assert.True(PlaybackFailureDescription.IsAccountLimitReason(reason));
            Assert.Equal(streamLimit, PlaybackFailureDescription.IsAccountLimitReason(reason, includeTranscode: false));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("capacity_unavailable")]
    [InlineData("transcoding_disabled")]
    [InlineData("future_reason")]
    public void OtherReasonsAreNotAccountLimits(string? reason)
    {
        Assert.False(PlaybackFailureDescription.IsAccountLimitReason(reason));
        Assert.False(PlaybackFailureDescription.IsAccountLimitReason(reason, includeTranscode: false));
    }

    [Fact]
    public void OtherTerminalExceptionMessagesKeepTheirDiagnosticFallback()
    {
        var error = new PlaybackPlanTerminalException("future_reason", null, retryable: false);

        Assert.Equal("The server could not find a compatible playback route. (future_reason)", error.Message);
    }

    [Theory]
    [InlineData(426, "client_upgrade_required", "Update required")]
    [InlineData(404, "playback_session_not_found", "Playback session expired")]
    [InlineData(404, "not_found", "This item is no longer available")]
    [InlineData(403, "forbidden", "Playback unavailable")]
    [InlineData(500, "internal_error", "Playback unavailable")]
    public void TransportFailuresMatchCurrentWebUiCopy(int status, string code, string title)
    {
        var description = PlaybackFailureDescription.DescribeTransport(
            new ApiException(code, "server detail", status));

        Assert.NotNull(description);
        Assert.Equal(title, description.Title);
    }

    [Theory]
    [InlineData(403, "transcoding_disabled", "Transcoding is disabled")]
    [InlineData(403, "audio_transcoding_disabled", "Audio transcoding is disabled")]
    [InlineData(404, "not_found", "This item is no longer available")]
    public void SpecificTransportFailuresAreNotShadowedByGenericStatusHandling(
        int status,
        string code,
        string title)
    {
        var serverMessage = status == 404 ? "Source media file is missing" : "server detail";

        var description = PlaybackFailureDescription.DescribeTransport(
            new ApiException(code, serverMessage, status));

        Assert.NotNull(description);
        Assert.Equal(title, description.Title);
    }

    [Fact]
    public void MissingSourceTransportFailureMatchesWebUiCopyAndCannotRetryInPlace()
    {
        var description = PlaybackFailureDescription.DescribeTransport(
            new ApiException("not_found", "Source media file is missing", 404));

        Assert.NotNull(description);
        Assert.Equal("This item is no longer available", description.Title);
        Assert.Equal(
            "The file needed to play this item can't be found right now. Go back and try another version if one is available.",
            description.Message);
        Assert.False(description.CanRetry);
    }
}
