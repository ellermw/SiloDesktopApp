using System.Globalization;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    public void ShowSiloCastNotice(string controller, string profile)
        => ShowNotice("SiloCast connected", $"{controller} is playing as {profile}. Use Settings > Desktop playback to disconnect.", "info");
    private string _castVideoGravity = "fit";
    private string _castSubtitlePosition = "bottom";
    private int _castSubtitleSyncMs;

    // Wire IDs are stable choices from the server inventory; mpv IDs can change on reload.
    public JsonObject CaptureSiloCastState()
    {
        var audio = new JsonArray();
        foreach (var (t, index) in (ActiveVersion?.AudioTracks ?? []).Select((t, i) => (t, i)))
            audio.Add(new JsonObject { ["kind"] = "audio", ["trackId"] = index + 1,
                ["title"] = t.Title ?? t.Language ?? $"Audio {index + 1}", ["detail"] = t.Codec });
        var subtitles = new JsonArray();
        foreach (var t in _playbackManager?.CurrentSession?.SubtitleUrls ?? [])
            subtitles.Add(new JsonObject { ["kind"] = "subtitle", ["trackId"] = t.Index + 1,
                ["title"] = t.Label ?? t.Language ?? $"Subtitle {t.Index + 1}", ["detail"] = t.Codec });
        var qualities = new JsonArray();
        foreach (var q in AvailableQualities)
            qualities.Add(new JsonObject { ["id"] = q.Label, ["label"] = q.Label == "original" ? "Original" : q.Label });
        var mpv = _mpv;
        var volume = mpv?.GetPropertyDouble("volume") ?? Volume;
        var speed = mpv?.GetPropertyDouble("speed") ?? 1;
        return new JsonObject
        {
            ["contentId"] = ContentId, ["sessionId"] = _playbackManager?.SessionId,
            ["title"] = State == PlayerState.Idle ? "Ready" : Title, ["subtitle"] = Subtitle,
            ["isPlaying"] = State != PlayerState.Idle && !IsPaused && !IsLoading,
            ["isLoading"] = IsLoading, ["isBuffering"] = IsBufferingForCache,
            ["currentTime"] = CurrentMediaPosition, ["duration"] = CurrentMediaDuration,
            ["audioTracks"] = audio, ["subtitleTracks"] = subtitles,
            ["selectedAudioTrackId"] = _playbackManager?.CurrentSession is { } session ? session.AudioTrackIndex + 1 : null,
            ["selectedSubtitleTrackId"] = State == PlayerState.Idle || _activeSubtitleServerIndex < 0 ? null : _activeSubtitleServerIndex + 1,
            ["qualityOptions"] = qualities, ["activeQualityId"] = ActiveQualityTier,
            ["isQualitySwitching"] = IsQualitySwitchActive, ["playbackSpeed"] = speed > 0 ? speed : 1,
            ["videoGravity"] = _castVideoGravity, ["hdrEnabled"] = ActiveVersion?.Hdr ?? false,
            ["supportsVideoGravity"] = State != PlayerState.Idle, ["supportsHDRToggle"] = false,
            ["subtitleSyncMs"] = (int)Math.Round((mpv?.GetPropertyDouble("sub-delay") ?? 0) * 1000),
            ["subtitlePosition"] = _castSubtitlePosition,
            ["supportsSubtitleDelay"] = State != PlayerState.Idle,
            ["supportsSubtitlePosition"] = State != PlayerState.Idle,
            ["volume"] = Math.Clamp(volume / 100, 0, 1), ["isMuted"] = mpv?.GetMute() ?? IsMuted,
            ["hasNextEpisode"] = HasNextEpisodeForCurrentPlayback, ["nextEpisodeTitle"] = NextEpisodeTitle,
            ["error"] = ErrorMessage
        };
    }

    public async Task ApplySiloCastControlAsync(JsonObject command)
    {
        var name = command["name"]?.GetValue<string>() ?? throw new ArgumentException("Missing command name.");
        if (name == "stop") { await CloseAsync(); return; }
        if (_mpv == null || State == PlayerState.Idle) throw new InvalidOperationException("The player is not ready.");
        static double Number(JsonObject c, string key)
        {
            var n = c[key]?.GetValue<double>() ?? throw new ArgumentException($"Missing {key}.");
            if (!double.IsFinite(n)) throw new ArgumentException($"Invalid {key}.");
            return n;
        }
        switch (name)
        {
            case "play": SetPaused(false); break;
            case "pause": SetPaused(true); break;
            case "play_pause":
                SetPaused(_idleRemuxResume.IsReopening ? !_idleRemuxResume.DesiredPaused : !_mpv.IsPaused);
                break;
            case "seek": SeekFastTo(Math.Clamp(Number(command, "seconds"), 0, Math.Max(0, CurrentMediaDuration))); break;
            case "select_audio_track":
                var aid = command["trackId"]?.GetValue<int>() ?? throw new ArgumentException("Missing audio track.");
                if (aid < 1 || ActiveVersion?.AudioTracks?.ElementAtOrDefault(aid - 1) == null)
                    throw new ArgumentException("This audio track is unavailable.");
                await SwitchAudioTrackAsync(aid - 1);
                break;
            case "select_subtitle_track":
                var sid = command["trackId"]?.GetValue<int>();
                if (sid.HasValue && !(_playbackManager?.CurrentSession?.SubtitleUrls.Any(t => t.Index + 1 == sid) ?? false))
                    throw new ArgumentException("This subtitle track is unavailable.");
                await SelectSubtitleByServerIndexAsync(sid.HasValue ? sid.Value - 1 : -1);
                break;
            case "set_playback_speed":
                _mpv.SetProperty("speed", Math.Clamp(Number(command, "speed"), 0.25, 4).ToString(CultureInfo.InvariantCulture)); break;
            case "set_quality":
                var quality = command["value"]?.GetValue<string>();
                if (quality == null || !AvailableQualities.Any(q => q.Label == quality))
                    throw new ArgumentException("This quality is unavailable.");
                await SelectQualityAsync(quality); break;
            case "set_video_gravity":
                var gravity = command["value"]?.GetValue<string>();
                if (gravity is not ("fit" or "fill" or "stretch")) throw new ArgumentException("Invalid aspect mode.");
                _castVideoGravity = gravity;
                _mpv.SetProperty("keepaspect", gravity == "stretch" ? "no" : "yes");
                _mpv.SetProperty("panscan", gravity == "fill" ? "1" : "0");
                break;
            case "set_subtitle_sync_ms":
                _subtitleOffsetSessionOverride = true;
                _subtitleOffsetDelay?.Cancel(); _subtitleOffsetDraft = null;
                Interlocked.Increment(ref _subtitleOffsetEdit);
                _castSubtitleSyncMs = (int)Math.Clamp(Number(command, "milliseconds"), -600000, 600000);
                _subtitleOffsetMs = _castSubtitleSyncMs;
                PublishSubtitlePreferenceStatus();
                _mpv.SetProperty("sub-delay", (_castSubtitleSyncMs / 1000d).ToString(CultureInfo.InvariantCulture)); break;
            case "set_subtitle_position":
                var position = command["value"]?.GetValue<string>();
                if (position is not ("bottom" or "lower-third" or "top")) throw new ArgumentException("Invalid subtitle position.");
                _castSubtitlePosition = position;
                _mpv.SetProperty("sub-align-y", position == "top" ? "top" : "bottom");
                _mpv.SetProperty("sub-margin-y", position == "lower-third" ? "160" : "22"); break;
            case "set_volume":
                Volume = Math.Clamp(Number(command, "volume"), 0, 1) * 100;
                _mpv.SetVolume(Volume); SaveVolumeState(); break;
            case "set_muted":
                IsMuted = command["enabled"]?.GetValue<bool>() ?? throw new ArgumentException("Missing mute value.");
                _mpv.SetMute(IsMuted); SaveVolumeState(); break;
            case "play_next":
                if (!HasNextEpisodeForCurrentPlayback) throw new InvalidOperationException("No next episode is available.");
                await ContinuePlayingNextAsync(); break;
            default: throw new ArgumentException("This command is not supported.");
        }
    }
}
