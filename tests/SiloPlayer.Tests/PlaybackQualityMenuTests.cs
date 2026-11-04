using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Tests;

public sealed class PlaybackQualityMenuTests
{
    private static readonly FileVersion[] Versions =
    [new() { FileId = 41, Resolution = "1080p", CodecVideo = "hevc" },
     new() { FileId = 71, Resolution = "2160p", CodecVideo = "av1", Hdr = true }];

    private static PlaybackStartResponse Session(string active = "720p") => new()
    {
        ProtocolVersion = 3, MediaFileId = 41, ActiveQuality = active,
        AvailableQualities = [new() { Label = "original", PreservesSource = true },
            new() { Label = "720p", DisplayName = "720p • balanced", BitrateKbps = 2500 },
            new() { Label = "server-custom", DisplayName = "Server option", BitrateKbps = 900 }],
    };

    [Fact]
    public void AutoCheckmarkRepresentsIntentAndDeliveryRemainsExplicit()
    {
        var rows = PlaybackQualityMenuModel.Build(Versions, Session(), "auto", 71, false, false, "2 Mbps after buffering");
        var auto = Assert.Single(rows.Where(r => r.Id == "quality:auto"));
        Assert.True(auto.IsCurrent);
        Assert.Contains("limited after buffering", auto.Title);
        Assert.Contains("720p", auto.Metadata);
        Assert.False(Assert.Single(rows.Where(r => r.Id == "quality:720p")).IsCurrent);
        Assert.DoesNotContain(rows, r => r.Id == "quality:original");
        Assert.Contains(rows, r => r.Id == "quality:server-custom");
        Assert.False(Assert.Single(rows.Where(r => r.Id == "quality:server-custom")).IsEnabled);
    }

    [Fact]
    public void AcceptedAndRequestedVersionsAreDifferentStates()
    {
        var rows = PlaybackQualityMenuModel.Build(Versions, Session(), "720p", 71, false, true, "");
        var playing = Assert.Single(rows.Where(r => r.Id == "file:41"));
        var requested = Assert.Single(rows.Where(r => r.Id == "file:71"));
        Assert.True(playing.IsCurrent); Assert.False(playing.IsPending);
        Assert.False(requested.IsCurrent); Assert.True(requested.IsPending);
        Assert.Contains("Playing", playing.Badges); Assert.Contains("Requested", requested.Badges);
        Assert.All(rows.Where(r => r.Kind != PlaybackQualityMenuOptionKind.Information), r => Assert.False(r.IsEnabled));
    }

    [Fact]
    public void RoomExplainsLockedVersionButRetainsViewerQuality()
    {
        var rows = PlaybackQualityMenuModel.Build(Versions, Session(), "720p", 41, true, false, "");
        Assert.DoesNotContain(rows, r => r.Kind == PlaybackQualityMenuOptionKind.Version);
        Assert.Contains(rows, r => r.Kind == PlaybackQualityMenuOptionKind.Information && r.Metadata.Contains("room", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Id == "quality:720p" && r.IsEnabled && r.IsCurrent);
    }

    [Fact]
    public void ProtocolThreeEmptyLadderDoesNotInventLegacyQualityOptions()
    {
        var session = Session(); session.AvailableQualities.Clear();
        var rows = PlaybackQualityMenuModel.Build(Versions, session, "", 41, false, false, "");
        Assert.DoesNotContain(rows, r => r.Kind == PlaybackQualityMenuOptionKind.Quality);
    }

    [Fact]
    public void DeliveredUnadvertisedRungDoesNotLightRequestedFixedChoice()
    {
        var rows = PlaybackQualityMenuModel.Build(Versions, Session("original"), "", 41, false, false, "");
        Assert.DoesNotContain(rows, r => r.Kind == PlaybackQualityMenuOptionKind.Quality && r.IsCurrent);
    }

    [Fact]
    public void VersionLabelsNeverExposeSourcePathsAndRetainServerFileIds()
    {
        var versions = Versions.Select(v => new FileVersion { FileId = v.FileId, Resolution = v.Resolution,
            CodecVideo = v.CodecVideo, FileName = "private-file", FilePath = "private-path", Hdr = v.Hdr }).ToArray();
        var rows = PlaybackQualityMenuModel.Build(versions, Session(), "auto", 41, false, false, "");
        Assert.Equal([41, 71], rows.Where(r => r.Kind == PlaybackQualityMenuOptionKind.Version).Select(r => r.FileId));
        Assert.All(rows, r => { Assert.DoesNotContain("private", r.Title); Assert.DoesNotContain("private", r.Metadata); });
        Assert.Contains(rows, r => r.Title.Contains("AV1") && r.Title.Contains("HDR"));
    }
}
