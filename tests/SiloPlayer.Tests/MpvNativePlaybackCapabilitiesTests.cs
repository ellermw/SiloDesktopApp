using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MpvNativePlaybackCapabilitiesTests
{
    [Fact]
    public void WindowedPlayer_UsesGpuNextD3D11AndBoundedFastStartCache()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("SetOption(\"vo\", \"gpu-next,gpu\")", source);
        Assert.Contains("SetOption(\"gpu-api\", \"d3d11\")", source);
        Assert.Contains("SetOption(\"gpu-context\", \"d3d11\")", source);
        Assert.Contains("SetOption(\"target-colorspace-hint\", \"auto\")", source);
        Assert.Contains("SetOption(\"target-colorspace-hint-mode\", \"target\")", source);
        Assert.Contains("SetOption(\"demuxer-max-bytes\", \"256MiB\")", source);
        Assert.Contains("SetOption(\"cache-pause-initial\", \"no\")", source);
        Assert.Contains("SetOption(\"cache-pause-wait\", \"2\")", source);
        Assert.Contains("SetOption(\"deinterlace\", \"auto\")", source);
        Assert.DoesNotContain("SetOption(\"vo\", \"gpu\");", source);
        Assert.DoesNotContain("SetOption(\"demuxer-max-bytes\", \"800MiB\")", source);
        Assert.DoesNotContain("SetOption(\"audio-spdif\"", source);
    }

    [Fact]
    public void Player_SeparatesAccurateSeekFromInteractiveKeyframeSeek()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("Command(\"seek\", FormatSeconds(seconds), \"absolute+exact\")", source);
        Assert.Contains("Command(\"seek\", FormatSeconds(seconds), \"absolute+keyframes\")", source);
    }

    [Fact]
    public void Profile_ContainsMeasuredModernLegacyAndLosslessCodecs()
    {
        var profile = MpvNativePlaybackCapabilities.CreateProfile();

        Assert.Equal("mpv v0.41.0-243-g05fac7f21", profile.VerifiedMpvVersion);
        Assert.Contains("hevc", profile.VideoCodecs);
        Assert.Contains("av1", profile.VideoCodecs);
        Assert.Contains("vc1", profile.VideoCodecs);
        Assert.Contains("mpeg2video", profile.VideoCodecs);
        Assert.Contains("vvc", profile.VideoCodecs);
        Assert.Contains("truehd", profile.AudioCodecs);
        Assert.Contains("dts", profile.AudioCodecs);
        Assert.Contains("flac", profile.AudioCodecs);
        Assert.Contains("pcm_bluray", profile.AudioCodecs);
        Assert.DoesNotContain("ac4", profile.AudioCodecs);
    }

    [Fact]
    public void Profile_IsNormalizedUniqueAndMatchesSiloContainers()
    {
        var profile = MpvNativePlaybackCapabilities.CreateProfile();

        Assert.All(profile.VideoCodecs.Concat(profile.AudioCodecs).Concat(profile.Containers),
            value => Assert.Equal(value.Trim().ToLowerInvariant(), value));
        Assert.Equal(profile.VideoCodecs.Count, profile.VideoCodecs.Distinct().Count());
        Assert.Equal(profile.AudioCodecs.Count, profile.AudioCodecs.Distinct().Count());
        Assert.Equal(profile.Containers.Count, profile.Containers.Distinct().Count());
        Assert.Contains("mp4", profile.Containers);
        Assert.Contains("mkv", profile.Containers);
        Assert.Contains("avi", profile.Containers);
        Assert.Contains("ts", profile.Containers);
        Assert.Contains("wmv", profile.Containers);
    }

    [Fact]
    public void Profile_ClaimsSingleLayerDolbyVisionButNotUnverifiedProfileSeven()
    {
        var details = MpvNativePlaybackCapabilities.CreateProfile().HdrDetails;

        Assert.True(details.Hdr10);
        Assert.True(details.Hdr10Plus);
        Assert.True(details.Hlg);
        Assert.Equal([5, 8], details.DolbyVisionProfiles);
        Assert.DoesNotContain(7, details.DolbyVisionProfiles);
    }

    [Fact]
    public void ApplyTo_UsesFreshCollectionsAndDoesNotClaimPassthroughByDefault()
    {
        var first = new PlaybackStartRequest();
        var second = new PlaybackStartRequest();

        MpvNativePlaybackCapabilities.ApplyTo(first);
        MpvNativePlaybackCapabilities.ApplyTo(second);
        first.CodecsVideo.Remove("hevc");
        first.HdrDetails!.DolbyVisionProfiles.Clear();

        Assert.Contains("hevc", second.CodecsVideo);
        Assert.Equal([5, 8], second.HdrDetails!.DolbyVisionProfiles);
        Assert.Null(first.AudioPassthrough);
        Assert.Null(second.AudioPassthrough);
        Assert.Equal("2160p", second.MaxResolution);
        Assert.True(second.Hdr);
    }

    [Fact]
    public void ApplyTo_SanitizesOnlyExplicitlyVerifiedBitstreamCodecs()
    {
        var request = new PlaybackStartRequest();
        var sink = new AudioPassthroughCapabilities
        {
            PassthroughCodecs = [" TRUEHD ", "dts", "DTS", "aac", "", "eac3"],
            MaxChannels = 99,
            SpatializerEnabled = true,
        };

        MpvNativePlaybackCapabilities.ApplyTo(request, sink);

        Assert.Equal(["truehd", "dts", "eac3"], request.AudioPassthrough!.PassthroughCodecs);
        Assert.Equal(32, request.AudioPassthrough.MaxChannels);
        Assert.True(request.AudioPassthrough.SpatializerEnabled);
    }

    [Fact]
    public void SanitizeVerifiedPassthrough_ReturnsNullWithoutSupportedCodec()
    {
        var sink = new AudioPassthroughCapabilities
        {
            PassthroughCodecs = ["aac", "flac"],
            MaxChannels = 8,
        };

        Assert.Null(MpvNativePlaybackCapabilities.SanitizeVerifiedPassthrough(sink));
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathParts)}.");
    }
}
