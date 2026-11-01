using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public sealed partial class AppUpdateService
{
    public ReleaseVersion? InstalledReleaseVersion { get; init; }
    public Action<AppUpdateChannel>? PersistChannel { private get; init; }
    private readonly object _channelGate = new();
    private long _channelEpoch;
    private int _channel, _changingChannel;
    public AppUpdateChannel Channel
    {
        get => (AppUpdateChannel)Volatile.Read(ref _channel);
        init => _channel = (int)(Enum.IsDefined(value) ? value : AppUpdateChannel.Stable);
    }
    public bool IsChangingChannel => Volatile.Read(ref _changingChannel) != 0;
    public string InstalledDisplayVersion => InstalledReleaseVersion?.Text ?? currentVersion.ToString(currentVersion.Revision > 0 ? 4 : 3);

    private static AppUpdateState UncheckedChannelState() => new(Error: "The selected update channel has not been checked yet. Check for updates.");

    public async Task SetChannelAsync(AppUpdateChannel channel, CancellationToken ct = default)
    {
        channel = Enum.IsDefined(channel) ? channel : AppUpdateChannel.Stable;
        long epoch;
        lock (_channelGate)
        {
            if (Channel == channel && !IsChangingChannel) return;
            // Commit the preference before publishing the channel. Cancellation
            // below only abandons the drain, never an already-saved selection.
            // A failed save leaves runtime authority and the epoch untouched.
            PersistChannel?.Invoke(channel);
            epoch = ++_channelEpoch;
            Volatile.Write(ref _channel, (int)channel);
            Volatile.Write(ref _changingChannel, 1);
            // Withdraw consent/authority immediately, before waiting out old IO.
            // Latest is shared by both channels; only catalog authority is channel-specific.
            _lastCheck = _lastCheck?.Channel == AppUpdateChannel.Beta ? _lastCheck.Stable : _lastCheck;
            Interlocked.Exchange(ref _lastSuccessfulCheckStamp, 0);
            Volatile.Write(ref _state, UncheckedChannelState());
        }
        NotifyChanged(State);
        var entered = false;
        try
        {
            await StopAsync().ConfigureAwait(false);
            await _operation.WaitAsync(ct).ConfigureAwait(false); entered = true;
            if (epoch != Volatile.Read(ref _channelEpoch)) return;
            SetState(UncheckedChannelState(), expectedChannelEpoch: epoch);
        }
        finally
        {
            if (entered) _operation.Release();
            lock (_channelGate) if (epoch == _channelEpoch) Volatile.Write(ref _changingChannel, 0);
            NotifyChanged(State);
        }
    }

    private string? ChannelNotice(AppRelease? release) => release is null && Channel == AppUpdateChannel.Stable
        && InstalledReleaseVersion?.IsPrerelease == true
            ? "This beta is ahead of the available Stable channel. Silo will wait for a suitable stable release without a downgrade."
            : null;
}
