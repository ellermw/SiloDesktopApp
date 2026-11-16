using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private const string AutoPlayNextSettingKey = "playback.auto_play_next";
    private readonly object _creditsAutoPlayGate = new();
    private sealed record CreditsAutoPlayOwner(PlaybackAuthorityScope Authority, ApiRequestContext Context,
        long Playback, long File, long Revision);
    private CreditsAutoPlayOwner? _creditsAutoPlayOwner;
    private long _creditsAutoPlayRevision;
    private bool? _creditsAutoPlay;
    private CancellationTokenSource? _creditsAutoPlayRead;

    private bool IsCreditsAutoPlayCurrent(CreditsAutoPlayOwner owner) => !_closing &&
        owner.Revision == _creditsAutoPlayRevision && ReferenceEquals(owner.Authority, _activeAuthority) &&
        owner.Authority.ApiClient.IsCurrentContext(owner.Context) &&
        owner.Playback == Volatile.Read(ref _playRequestGeneration) &&
        owner.File == Volatile.Read(ref _fileLoadGeneration);

    private CreditsAutoPlayOwner BeginCreditsAutoPlayRefresh()
    {
        lock (_creditsAutoPlayGate)
        {
            var authority = _activeAuthority;
            var owner = new CreditsAutoPlayOwner(authority, authority.ApiClient.CaptureContext(),
                Volatile.Read(ref _playRequestGeneration), Volatile.Read(ref _fileLoadGeneration), ++_creditsAutoPlayRevision);
            _creditsAutoPlayOwner = owner;
            _creditsAutoPlay = null;
            if (_mpv is { } mpv) PublishCreditsAutoPlay(mpv);
            return owner;
        }
    }

    private void CompleteCreditsAutoPlayRefresh(CreditsAutoPlayOwner owner, bool? enabled, CancellationToken ct)
    {
        lock (_creditsAutoPlayGate)
        {
            if (ct.IsCancellationRequested || !IsCreditsAutoPlayCurrent(owner)) return;
            _creditsAutoPlay = enabled;
            if (_mpv is { } mpv) PublishCreditsAutoPlay(mpv);
        }
    }

    private void PublishCreditsAutoPlay(MpvPlayer mpv)
    {
        lock (_creditsAutoPlayGate)
        {
            var owner = _creditsAutoPlayOwner;
            var current = owner is not null && IsCreditsAutoPlayCurrent(owner);
            var value = current && _creditsAutoPlay.HasValue ? _creditsAutoPlay.Value ? "true" : "false" : "unknown";
            mpv.SendScriptMessage("osc-set-auto-play-next", value,
                Volatile.Read(ref _playRequestGeneration).ToString(CultureInfo.InvariantCulture),
                Volatile.Read(ref _fileLoadGeneration).ToString(CultureInfo.InvariantCulture));
        }
    }

    private async Task RefreshCreditsAutoPlayPreferenceAsync(CancellationToken ct)
    {
        CancellationTokenSource read;
        CreditsAutoPlayOwner owner;
        lock (_creditsAutoPlayGate)
        {
            _creditsAutoPlayRead?.Cancel();
            read = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _creditsAutoPlayRead = read;
            owner = BeginCreditsAutoPlayRefresh();
        }
        try
        {
            var effective = await owner.Authority.SettingsApi.GetEffectiveSettingsAsync([AutoPlayNextSettingKey], read.Token).ConfigureAwait(false);
            CompleteCreditsAutoPlayRefresh(owner, ReadCreditsAutoPlay(effective), read.Token);
        }
        catch (OperationCanceledException) { }
        catch { } // Keep unresolved after a failed live change; a stale preference must not advance playback.
        finally
        {
            lock (_creditsAutoPlayGate)
                if (ReferenceEquals(_creditsAutoPlayRead, read)) _creditsAutoPlayRead = null;
            read.Dispose();
        }
    }

    private static bool ReadCreditsAutoPlay(EffectiveSettingsResponse settings) =>
        !string.Equals(settings.Settings.FirstOrDefault(item => item.Key == AutoPlayNextSettingKey)?.EffectiveValue?.Trim(),
            "false", StringComparison.OrdinalIgnoreCase);
}
