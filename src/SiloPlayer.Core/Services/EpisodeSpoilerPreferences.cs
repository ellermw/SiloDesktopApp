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
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private ApiRequestContext? _context;
    private Task? _load;
    private CancellationTokenSource? _scope;
    private bool _hideImages, _hideOverviews, _loaded, _loadFailed, _knownHideOverviews;
    private DateTimeOffset _retryAt;
    private TimeSpan _retryDelay = TimeSpan.FromSeconds(1);
    private bool _disposed;
    public event EventHandler? Changed;
    public bool IsLoadedForCurrentContext => _context is { } context && _settings.IsCurrentContext(context) && _loaded;
    public bool HideImages => HasCurrentIdentity && _hideImages;
    public bool HideOverviews => HasCurrentIdentity && _hideOverviews;

    private bool HasCurrentIdentity => _context is { } context && SameIdentity(context, _settings.CaptureContext());
    private static bool SameIdentity(ApiRequestContext previous, ApiRequestContext current) =>
        previous.AuthenticationGeneration == current.AuthenticationGeneration &&
        previous.BaseUrl == current.BaseUrl && previous.ProfileId == current.ProfileId;

    // Protect unknown/in-flight reads, but not a failed optional read without a known hide value.
    public bool ShouldHideOverviews
    {
        get
        {
            lock (_sync)
                return _context is not { } context || !_settings.IsCurrentContext(context) ||
                    _knownHideOverviews || (!_loaded && !_loadFailed);
        }
    }

    public EpisodeSpoilerPreferences(SettingsApi settings) : this(settings, TimeProvider.System) { }

    internal EpisodeSpoilerPreferences(SettingsApi settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
        _settings.LocalProfileSettingChanged += SettingChanged;
    }

    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        Task load;
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            var context = _settings.CaptureContext();
            if (_context != context || _load == null ||
                (_loadFailed && _load.IsCompleted && _time.GetUtcNow() >= _retryAt))
            {
                _scope?.Cancel(); _scope?.Dispose();
                _scope = new();
                _loaded = false;
                // A same-scope retry must not turn a failed optional read back into an unknown value.
                if (_context != context || _load == null) _loadFailed = false;
                if (_context != context)
                {
                    // Access refreshes retire reads, not the same viewer's last-known protection.
                    if (_context is not { } previous || !SameIdentity(previous, context))
                        _hideImages = _hideOverviews = _knownHideOverviews = false;
                    _retryDelay = TimeSpan.FromSeconds(1);
                }
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
            _loaded = _loadFailed = false;
            _load = null;
            _retryDelay = TimeSpan.FromSeconds(1);
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
                    _hideImages = _hideOverviews = _knownHideOverviews = false;
                    _loaded = true;
                    _loadFailed = false;
                    _retryDelay = TimeSpan.FromSeconds(1);
                }
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            var values = await _settings.GetContractEffectiveSettingsAsync(Keys, ct: ct).ConfigureAwait(false);
            lock (_sync)
            {
                if (ct.IsCancellationRequested || _context != context || !_settings.IsCurrentContext(context)) return;
                bool Read(string key) => values.Settings.FirstOrDefault(entry => entry.Key == key)?.Value.ValueKind == System.Text.Json.JsonValueKind.True;
                _hideImages = Read(Keys[0]); _hideOverviews = _knownHideOverviews = Read(Keys[1]);
                _loaded = true;
                _loadFailed = false;
                _retryDelay = TimeSpan.FromSeconds(1);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            bool changed;
            lock (_sync)
            {
                if (ct.IsCancellationRequested || _context != context || !_settings.IsCurrentContext(context)) return;
                var hidden = ShouldHideOverviews;
                // Keep the completed task through the backoff, including synchronous failures.
                _loadFailed = true;
                _retryAt = _time.GetUtcNow().Add(_retryDelay);
                _retryDelay = TimeSpan.FromSeconds(Math.Min(30, _retryDelay.TotalSeconds * 2));
                changed = hidden != ShouldHideOverviews;
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }
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
