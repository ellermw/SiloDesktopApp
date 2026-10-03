using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SiloPlayer.Services;

public sealed class ThemeMusicService : IThemeAudioSink, IDisposable
{
    private readonly SiloApiClient _client;
    private readonly SettingsApi _settings;
    private readonly AuthService _auth;
    private readonly PlayerService _playback;
    private readonly ThemeMusicSession _session;
    private MediaPlayer? _audio;
    private CancellationTokenSource? _fade;
    private bool _stopImmediately;
    private DispatcherQueue? _dispatcher;
    private long _generation;
    private string? _detailOwner, _suppressedOwner;
    private (long Session, string Server, string? Profile) _authority;
    public ThemeMusicService(SiloApiClient client, SettingsApi settings, AuthService auth, PlayerService playback)
    {
        _client = client; _settings = settings; _auth = auth; _playback = playback;
        _session = new(AuthorizeAsync, this); _authority = Authority();
        auth.UserChanged += AccountChanged; auth.LoggedOut += Stop;
        playback.StateChanged += PlaybackChanged;
    }
    private (long, string, string?) Authority() => (_auth.SessionGeneration, _client.BaseUrl, _auth.SelectedProfileId);
    private void AccountChanged() { if (_authority != Authority()) { _authority = Authority(); Stop(); _session.ResetAuthority(); _detailOwner = _suppressedOwner = null; } }
    private void PlaybackChanged(PlayerState state) { if (state != PlayerState.Idle) Interrupt(); }
    public async Task SelectAsync(MediaItemDetail item, CancellationToken ct)
    {
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        AccountChanged();
        _detailOwner = item.Themes?.OwnerId;
        if (_detailOwner != _suppressedOwner) _suppressedOwner = null;
        if (_detailOwner != null && _detailOwner == _suppressedOwner) return;
        var generation = ++_generation; var authority = Authority();
        try
        {
            var prefs = await _settings.GetEffectiveSettingsAsync(["ui.theme_music_enabled", "ui.theme_music_loop"], ct);
            if (generation != _generation || authority != Authority() || ct.IsCancellationRequested) return;
            if (prefs.Settings.FirstOrDefault(s => s.Key == "ui.theme_music_enabled")?.EffectiveValue != "true" ||
                _playback.State != PlayerState.Idle || item.Themes == null || string.IsNullOrEmpty(authority.Item3)) { Stop(); return; }
            var capability = await _client.GetAsync<Capability>("/api/v2/catalog/themes/capabilities", ct);
            if (generation != _generation || authority != Authority() || ct.IsCancellationRequested) return;
            if (capability.State != "available" || capability.Allowed == false) { Stop(); return; }
            await _session.SelectAsync(item.Themes, prefs.Settings.FirstOrDefault(s => s.Key == "ui.theme_music_loop")?.EffectiveValue == "true", ct);
        }
        catch (OperationCanceledException) { }
        catch { if (generation == _generation) Stop(); /* Optional audio errors never expose grants or block playback. */ }
    }
    private async Task<ThemeAudioGrant> AuthorizeAsync(string owner, string theme, CancellationToken ct)
    {
        var authority = Authority(); var context = _client.CaptureContext();
        var grant = await _client.SendRequestAsync<ThemeAudioGrant>(context, HttpMethod.Post,
            $"/api/v2/catalog/items/{Uri.EscapeDataString(owner)}/themes/{Uri.EscapeDataString(theme)}/playback",
            new { accepted_formats = new[] { new { container = "mp3", audio_codec = "mp3" }, new { container = "mp4", audio_codec = "aac" }, new { container = "m4a", audio_codec = "aac" } } }, ct);
        if (authority != Authority()) throw new OperationCanceledException("Theme owner changed.");
        var url = new Uri(new Uri(_client.BaseUrl.TrimEnd('/') + "/"), grant.Url);
        if (url.Scheme is not ("https" or "http") || url.UserInfo.Length != 0) throw new InvalidDataException("Invalid theme delivery.");
        grant.Url = url.AbsoluteUri; return grant;
    }
    public void Load(ThemeAudioGrant grant, bool loop)
    {
        // This optional player advertises only formats Windows decodes; the server converts others to AAC.
        StopAudio(); var player = _audio = new MediaPlayer { Volume = 0, IsLoopingEnabled = loop, IsVideoFrameServerEnabled = false };
        player.MediaEnded += (_, _) => _dispatcher?.TryEnqueue(async () =>
        { if (_audio != player) return; try { await _session.EndedAsync(); } catch { Stop(); } });
        player.MediaFailed += (_, _) => _dispatcher?.TryEnqueue(() => { if (_audio == player) Stop(); });
        player.Source = MediaSource.CreateFromUri(new Uri(grant.Url)); player.Play();
        _ = FadeAsync(player, .35);
    }
    public void Pause() { if (_audio is { } player) _ = FadeAsync(player, 0, () => player.Pause()); }
    public void SetLoop(bool loop) { if (_audio != null) _audio.IsLoopingEnabled = loop; }
    public void Resume() { if (_audio is { } player) { player.Play(); _ = FadeAsync(player, .35); } }
    public void Suspend() { ++_generation; _session.Suspend(); }
    public void Interrupt() { if (_detailOwner != null) _suppressedOwner = _detailOwner; ++_generation; _stopImmediately = true; try { _session.Interrupt(); } finally { _stopImmediately = false; } }
    private void StopAudio() { _fade?.Cancel(); var player = _audio; _audio = null; if (player == null) return; player.Pause(); player.Source = null; player.Dispose(); }
    void IThemeAudioSink.Stop()
    {
        if (_stopImmediately || _audio == null) { StopAudio(); return; }
        var player = _audio;
        _ = FadeAsync(player, 0, () => { if (_audio == player) StopAudio(); });
    }
    private async Task FadeAsync(MediaPlayer player, double target, Action? completed = null)
    {
        _fade?.Cancel(); _fade?.Dispose();
        var fade = _fade = new CancellationTokenSource();
        var ct = fade.Token;
        var start = player.Volume;
        try
        {
            for (var step = 1; step <= 12; step++)
            {
                await Task.Delay(25, ct);
                if (_audio != player || ct.IsCancellationRequested) return;
                player.Volume = Math.Clamp(start + (target - start) * step / 12, 0, 1);
            }
            completed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch { if (_audio == player) StopAudio(); }
    }
    public void Stop() { ++_generation; _stopImmediately = true; try { _session.Stop(); } finally { _stopImmediately = false; } }
    public void Dispose()
    { _auth.UserChanged -= AccountChanged; _auth.LoggedOut -= Stop; _playback.StateChanged -= PlaybackChanged; StopAudio(); _session.Dispose(); }
    private sealed class Capability { public string State { get; set; } = ""; public bool? Allowed { get; set; } }
}
