using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public static class EpisodeSpoilerPolicy
{
    public static bool IsUnwatched(bool? played, bool? inProgress, double? positionSeconds)
        => played != true && inProgress != true && !(positionSeconds > 0);

    public static bool HideImage(string type, bool? isEpisodeStill, bool? played, bool? inProgress, double? positionSeconds, bool hideImages)
        => hideImages && type == "episode" && isEpisodeStill != false && IsUnwatched(played, inProgress, positionSeconds);
}

/// <summary>One profile-scoped read shared by visible episode surfaces. Consumers cannot cancel one another's load.</summary>
public sealed class EpisodeSpoilerPreferences : IDisposable
{
    private static readonly string[] Keys = ["catalog.hide_unwatched_episode_images", "catalog.hide_unwatched_episode_overviews"];
    private readonly SettingsApi _settings;
    private readonly object _sync = new();
    private ApiRequestContext? _context;
    private Task? _load;
    private CancellationTokenSource? _scope;
    private bool _hideImages, _hideOverviews;
    private bool _disposed;
    public event EventHandler? Changed;
    public bool HideImages => _context is { } context && _settings.IsCurrentContext(context) && _hideImages;
    public bool HideOverviews => _context is { } context && _settings.IsCurrentContext(context) && _hideOverviews;

    public EpisodeSpoilerPreferences(SettingsApi settings)
    {
        _settings = settings;
        _settings.LocalProfileSettingChanged += SettingChanged;
    }

    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        Task load;
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            var context = _settings.CaptureContext();
            if (_context != context || _load == null)
            {
                _scope?.Cancel(); _scope?.Dispose();
                _scope = new();
                if (_context != context) _hideImages = _hideOverviews = false;
                _context = context;
                _load = LoadAsync(context, _scope.Token);
            }
            load = _load;
        }
        return ct.CanBeCanceled ? load.WaitAsync(ct) : load;
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _scope?.Cancel();
            _load = null;
        }
    }

    private async Task LoadAsync(ApiRequestContext context, CancellationToken ct)
    {
        try
        {
            var capability = await _settings.GetContractCapabilitiesAsync(ct).ConfigureAwait(false);
            if (capability.ApiVersion != 1 || capability.Revision < 17 || !capability.SupportsBatchedEffective)
            {
                lock (_sync)
                {
                    if (ct.IsCancellationRequested || _context != context || !_settings.IsCurrentContext(context)) return;
                    _hideImages = _hideOverviews = false;
                }
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            var values = await _settings.GetContractEffectiveSettingsAsync(Keys, ct: ct).ConfigureAwait(false);
            lock (_sync)
            {
                if (ct.IsCancellationRequested || _context != context || !_settings.IsCurrentContext(context)) return;
                bool Read(string key) => values.Settings.FirstOrDefault(entry => entry.Key == key)?.Value.ValueKind == System.Text.Json.JsonValueKind.True;
                _hideImages = Read(Keys[0]); _hideOverviews = Read(Keys[1]);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (ApiException) { }
        catch (HttpRequestException) { }
    }

    private void SettingChanged(string key)
    {
        if (!Keys.Contains(key, StringComparer.Ordinal)) return;
        Invalidate();
        _ = EnsureLoadedAsync();
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _scope?.Cancel(); _scope?.Dispose(); _scope = null; }
        _settings.LocalProfileSettingChanged -= SettingChanged;
    }
}
