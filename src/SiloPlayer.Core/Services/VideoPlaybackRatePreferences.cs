using SiloPlayer.Core.Api;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

public sealed class VideoPlaybackRatePreferences(SettingsApi api)
{
    public const string Key = "player.playback_speed";
    private readonly SemaphoreSlim _writes = new(1, 1);
    public static IReadOnlyList<double> Presets { get; } = Array.AsReadOnly(new[] { 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3 });
    public static bool IsValidRate(double value) => double.IsFinite(value) && value is >= 0.25 and <= 3 &&
        Math.Abs(value * 20 - Math.Round(value * 20)) < 0.00000001;
    public VideoPlaybackRateSession CreateSession(Func<bool> isCurrent, Func<bool> canEdit,
        Action<double> apply, Action changed)
        => new(this, api.CaptureSettingsIdentityContext(), isCurrent, canEdit, apply, changed);

    internal bool IsCurrent(SettingsIdentityContext owner) => api.IsCurrentSettingsIdentityContext(owner);

    internal async Task<double> ReadAsync(SettingsIdentityContext owner, CancellationToken ct)
    {
        // A new playback owner may start while a previous accepted save settles.
        // Only the optional read waits; playback preparation never awaits this.
        await _writes.WaitAsync(ct).ConfigureAwait(false);
        _writes.Release();
        EnsureCurrent(owner, ct);
        var response = await SendAsync(owner, context => api.GetContractEffectiveSettingsAsync(context, [Key],
            deviceId: owner.DeviceId, profileId: context.ProfileId, ct: ct), ct).ConfigureAwait(false);
        var entry = response.Settings.FirstOrDefault(setting => setting.Key == Key);
        return entry?.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDouble(out var rate) && IsValidRate(rate)
            ? rate : 1;
    }

    internal async Task SaveAsync(SettingsIdentityContext owner, double rate)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureCurrent(owner, default);
            // Accepted preferences belong to the profile/device, not the playback
            // session. Persist them in order even after that session retires;
            // session-state publication is guarded separately by the caller.
            await SendAsync(owner, async context =>
            {
                await api.SetContractSettingValueAsync(context, Key, "profile_device", rate,
                    deviceId: owner.DeviceId, profileId: context.ProfileId).ConfigureAwait(false);
                return true;
            }, default).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    // Access refreshes retire request authorization, not the accepted
    // profile/device owner. Each request captures fresh authority for that
    // owner and retries once when only the access revision changed.
    private async Task<T> SendAsync<T>(SettingsIdentityContext owner, Func<ApiRequestContext, Task<T>> send, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var context = api.CaptureSettingsContext(owner)?.Request
                ?? throw new OperationCanceledException("Playback speed owner changed.", ct);
            try
            {
                var result = await send(context).ConfigureAwait(false);
                EnsureCurrent(owner, ct);
                return result;
            }
            catch (OperationCanceledException) when (attempt == 0 && !ct.IsCancellationRequested
                && IsCurrent(owner) && !api.IsCurrentContext(context)) { }
        }
    }

    private void EnsureCurrent(SettingsIdentityContext owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsCurrent(owner)) throw new OperationCanceledException("Playback speed owner changed.", ct);
    }
}

/// <summary>Viewer desire for one authorized playback owner, independent of native room correction rates.</summary>
public sealed class VideoPlaybackRateSession
{
    private readonly VideoPlaybackRatePreferences _preferences;
    private readonly SettingsIdentityContext _owner;
    private readonly Func<bool> _isCurrent, _canEdit;
    private readonly Action<double> _apply;
    private readonly Action _changed;
    private readonly object _gate = new();
    private long _intent;
    private bool _retired, _pending, _hasDesiredRate;
    private double _desired = 1;
    private string? _error;

    internal VideoPlaybackRateSession(VideoPlaybackRatePreferences preferences, SettingsIdentityContext owner,
        Func<bool> isCurrent, Func<bool> canEdit, Action<double> apply, Action changed)
        => (_preferences, _owner, _isCurrent, _canEdit, _apply, _changed)
            = (preferences, owner, isCurrent, canEdit, apply, changed);

    private bool Current => !_retired && _preferences.IsCurrent(_owner) && _isCurrent();
    public bool CanChange { get { lock (_gate) return Current && _canEdit(); } }
    public double DesiredRate { get { lock (_gate) return Current ? _desired : 1; } }
    public string? Error { get { lock (_gate) return Current ? _error : null; } }
    public bool IsSavePending { get { lock (_gate) return Current && _pending; } }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        long intent;
        lock (_gate) { if (!Current) return; intent = _intent; }
        try
        {
            var rate = await _preferences.ReadAsync(_owner, ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (!Current || intent != _intent) return;
                _desired = rate; _hasDesiredRate = true; _error = null;
                if (_canEdit()) _apply(rate);
                _changed();
            }
        }
        catch (Exception)
        {
            lock (_gate)
            {
                if (!Current || intent != _intent || ct.IsCancellationRequested) return;
                _error = "Playback speed could not load. Choose a speed to retry.";
                _changed();
            }
        }
    }

    public Task SetAsync(double rate)
    {
        if (!VideoPlaybackRatePreferences.IsValidRate(rate)) throw new ArgumentOutOfRangeException(nameof(rate));
        long intent;
        lock (_gate)
        {
            if (!Current || !_canEdit()) return Task.CompletedTask;
            intent = ++_intent;
            _desired = rate; _hasDesiredRate = true; _pending = true; _error = null;
            _apply(rate); _changed();
        }
        return SaveAsync(rate, intent);
    }

    private async Task SaveAsync(double rate, long intent)
    {
        // Eligibility gates accepting a click, not persisting accepted viewer
        // intent after a replan or playback-session retirement.
        string? error = null;
        try { await _preferences.SaveAsync(_owner, rate).ConfigureAwait(false); }
        catch (Exception) { error = "Playback speed could not save. Choose a speed to retry."; }
        lock (_gate)
        {
            if (!Current || intent != _intent) return;
            _pending = false; _error = error;
            _changed();
        }
    }

    /// <summary>Reapply desire after a same-session replan or room departure without a settings write.</summary>
    public void Restore()
    {
        // Until a read or manual choice establishes desire, the default is
        // only a display fallback, not an accepted rate to restore.
        lock (_gate) { if (Current && _hasDesiredRate && _canEdit()) _apply(_desired); }
    }

    public void Retire() { lock (_gate) _retired = true; }
}
