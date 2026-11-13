using System.Text.Json;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

// Interface-language changes for an already open player. Only display text is
// re-rendered: the session, menus, panels, wire values and playback ownership
// stay exactly as they are.
public partial class PlayerService
{
    private EventHandler<AppLocaleChangedEventArgs>? _playbackLocaleChanged;
    // Re-describes the visible failure panel in the current language.
    private Func<PlaybackFailurePresentation>? _playbackFailureText;

    /// <summary>Raised after playback-owned text was re-rendered for a new interface language.</summary>
    public event Action? PlaybackTextChanged;

    private void AttachPlaybackLocalization()
    {
        _playbackLocaleChanged = (_, _) =>
        {
            var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
            if (dispatcher == null || dispatcher.HasThreadAccess) RefreshPlaybackText();
            else dispatcher.TryEnqueue(RefreshPlaybackText);
        };
        LocaleService.Shared.Changed += _playbackLocaleChanged;
    }

    private void DetachPlaybackLocalization()
    {
        if (_playbackLocaleChanged != null) LocaleService.Shared.Changed -= _playbackLocaleChanged;
        _playbackLocaleChanged = null;
    }

    // Season/episode numbers and the raw episode title, kept so the HUD line
    // (Subtitle) re-renders in a newly chosen interface language.
    private (int Season, int Episode, string? Title)? _hudEpisode;

    /// <summary>"S2 · E10 — Title" (VideoPlayer.tsx HUD copy); the episode title stays raw.</summary>
    private string? HudEpisodeLine()
    {
        if (_hudEpisode is not { } episode) return null;
        var numbers = new Dictionary<string, object?> { ["season"] = episode.Season, ["episode"] = episode.Episode };
        if (string.IsNullOrWhiteSpace(episode.Title))
            return AppStrings.Get("web.player.hud.episode", numbers, fallback: $"S{episode.Season} · E{episode.Episode}");
        numbers["title"] = episode.Title;
        return AppStrings.Get("web.player.hud.episodeWithTitle", numbers,
            fallback: $"S{episode.Season} · E{episode.Episode} — {episode.Title}");
    }

    /// <summary>The OSC's explicit localized text payload (ordinary script-message strings).</summary>
    private static void PublishOscLocalization(MpvPlayer mpv)
        => mpv.SendScriptMessage([OscLocalization.MessageName, .. OscLocalization.Build(AppStrings.Current)]);

    private void RefreshPlaybackText()
    {
        if (_closing) return;
        try
        {
            if (_hudEpisode != null) Subtitle = HudEpisodeLine();
            // Playback Info rows are keyed by their (now translated) labels: speak the next update whole.
            _playbackInfoSpoken = null;
            var mpv = _mpv;
            if (mpv != null)
            {
                PublishPlaybackHudPreferences(mpv);
                SendTitleToOsc();
                SendMediaInfoToOsc();
                SendSubtitleSyncState();
                if (_playbackTerminalState != null && _playbackFailureText is { } describe)
                {
                    var description = describe();
                    ErrorMessage = description.Message;
                    mpv.SendScriptMessage("osc-update-playback-failure", JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["title"] = description.Title,
                        ["message"] = description.Message,
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Playback text refresh skipped: {ex.Message}");
        }
        InvokeSubscribersSafely(PlaybackTextChanged, nameof(PlaybackTextChanged));
    }
}
