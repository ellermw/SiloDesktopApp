using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.SiloCast;

namespace SiloPlayer.Tests;

public sealed class SiloCastLocalPlaybackScopeTests
{
    private static JsonObject PlayingState() => new()
    {
        ["contentId"] = "movie-42", ["sessionId"] = "session-7", ["title"] = "Private Title", ["subtitle"] = "S1 E2",
        ["isPlaying"] = true, ["isLoading"] = false, ["isBuffering"] = false, ["currentTime"] = 1234.5, ["duration"] = 5400,
        ["audioTracks"] = new JsonArray(new JsonObject { ["kind"] = "audio", ["trackId"] = 1, ["title"] = "English" }),
        ["subtitleTracks"] = new JsonArray(new JsonObject { ["kind"] = "subtitle", ["trackId"] = 1, ["title"] = "English" }),
        ["selectedAudioTrackId"] = 1, ["selectedSubtitleTrackId"] = 1,
        ["qualityOptions"] = new JsonArray(new JsonObject { ["id"] = "original", ["label"] = "Original" }),
        ["activeQualityId"] = "original", ["isQualitySwitching"] = false, ["playbackSpeed"] = 1.0,
        ["videoGravity"] = "fit", ["hdrEnabled"] = true, ["supportsVideoGravity"] = true, ["supportsHDRToggle"] = false,
        ["subtitleSyncMs"] = 0, ["subtitlePosition"] = 100, ["supportsSubtitleDelay"] = true, ["supportsSubtitlePosition"] = true,
        ["volume"] = 0.5, ["isMuted"] = false, ["hasNextEpisode"] = true, ["nextEpisodeTitle"] = "Next Private Title", ["error"] = null,
    };

    [Fact]
    public void ControllerWithoutHandedOffSessionSeesAnIdleReceiver()
    {
        var state = SiloCastStatePolicy.ForController(PlayingState(), controllerOwnsPlayback: false);
        var json = state.ToJsonString();

        Assert.DoesNotContain("movie-42", json);
        Assert.DoesNotContain("session-7", json);
        Assert.DoesNotContain("Private Title", json);
        Assert.Equal("Ready", state["title"]!.GetValue<string>());
        Assert.False(state["isPlaying"]!.GetValue<bool>());
        Assert.Empty(state["audioTracks"]!.AsArray());
        // Protocol shape is unchanged for phone clients.
        Assert.Equal(PlayingState().Select(p => p.Key).Order(), state.Select(p => p.Key).Order());
    }

    [Fact]
    public void ControllerOwningTheHandedOffSessionSeesFullState()
    {
        var original = PlayingState();
        Assert.Same(original, SiloCastStatePolicy.ForController(original, controllerOwnsPlayback: true));
    }

    [Fact]
    public void ReceiverControlsAndStateAreScopedToTheRemoteSession()
    {
        var receiver = ReadRepoFile("src", "SiloPlayer", "Services", "SiloCast", "SiloCastReceiverService.cs").ReplaceLineEndings("\n");
        Assert.Contains("_remoteScope != null && ReferenceEquals(action.Authority, _remoteScope);", receiver);
        Assert.Contains("SiloCastStatePolicy.ForController(\n                        await _adapter!.StateAsync(c.Lifetime.Token), RemotePlaybackActive());", receiver);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
