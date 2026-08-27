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
}
