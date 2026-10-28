using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Services.SiloCast;

/// <summary>
/// The receiver's TLS key is a public protocol constant, so any device on the
/// LAN can connect and claim the advertised server. Until a controller owns a
/// server-approved handoff session, it sees an idle receiver rather than what
/// the PC is playing locally.
/// </summary>
public static class SiloCastStatePolicy
{
    public static JsonObject ForController(JsonObject state, bool controllerOwnsPlayback)
    {
        if (controllerOwnsPlayback) return state;
        var idle = (JsonObject)state.DeepClone();
        idle["contentId"] = null;
        idle["sessionId"] = null;
        idle["title"] = "Ready";
        idle["subtitle"] = null;
        idle["isPlaying"] = false;
        idle["isLoading"] = false;
        idle["isBuffering"] = false;
        idle["currentTime"] = 0;
        idle["duration"] = 0;
        idle["audioTracks"] = new JsonArray();
        idle["subtitleTracks"] = new JsonArray();
        idle["selectedAudioTrackId"] = null;
        idle["selectedSubtitleTrackId"] = null;
        idle["qualityOptions"] = new JsonArray();
        idle["activeQualityId"] = null;
        idle["isQualitySwitching"] = false;
        idle["hdrEnabled"] = false;
        idle["supportsVideoGravity"] = false;
        idle["supportsSubtitleDelay"] = false;
        idle["supportsSubtitlePosition"] = false;
        idle["hasNextEpisode"] = false;
        idle["nextEpisodeTitle"] = null;
        idle["error"] = null;
        return idle;
    }
}
