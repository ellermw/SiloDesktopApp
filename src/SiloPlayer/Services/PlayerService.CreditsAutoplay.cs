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
        ApiIdentityContext Identity, long Playback, long File, long Revision);
    private CreditsAutoPlayOwner? _creditsAutoPlayOwner;
    private long _creditsAutoPlayRevision;
    private bool? _creditsAutoPlay;
    private CancellationTokenSource? _creditsAutoPlayRead;

    private bool OwnsCreditsAutoPlay(CreditsAutoPlayOwner owner) => !_closing &&
        owner.Revision == _creditsAutoPlayRevision && ReferenceEquals(owner.Authority, _activeAuthority) &&
        owner.Authority.ApiClient.IsCurrentIdentityContext(owner.Identity) &&
        owner.Playback == Volatile.Read(ref _playRequestGeneration) &&
        owner.File == Volatile.Read(ref _fileLoadGeneration);

    private bool IsCreditsAutoPlayCurrent(CreditsAutoPlayOwner owner) =>
        OwnsCreditsAutoPlay(owner) && owner.Authority.ApiClient.IsCurrentContext(owner.Context);

    private CreditsAutoPlayOwner BeginCreditsAutoPlayRefresh()
    {
        lock (_creditsAutoPlayGate)
        {
            var authority = _activeAuthority;
            var owner = new CreditsAutoPlayOwner(authority, authority.ApiClient.CaptureContext(),
                authority.ApiClient.CaptureIdentityContext(), Volatile.Read(ref _playRequestGeneration),
                Volatile.Read(ref _fileLoadGeneration), ++_creditsAutoPlayRevision);
            _creditsAutoPlayOwner = owner;
            _creditsAutoPlay = null;
            if (_mpv is { } mpv) PublishCreditsAutoPlay(mpv);
            return owner;
        }
    }

    private bool CompleteCreditsAutoPlayRefresh(CreditsAutoPlayOwner owner, bool enabled, CancellationToken ct)
    {
        lock (_creditsAutoPlayGate)
        {
            if (ct.IsCancellationRequested || !IsCreditsAutoPlayCurrent(owner)) return false;
            _creditsAutoPlay = enabled;
            if (_mpv is { } mpv) PublishCreditsAutoPlay(mpv);
            return !ct.IsCancellationRequested && IsCreditsAutoPlayCurrent(owner);
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
            read = CancellationTokenSource.CreateLinkedTokenSource(ct,
                _authorityLifetimeCts.Token, _playbackCts?.Token ?? CancellationToken.None);
            _creditsAutoPlayRead = read;
            owner = BeginCreditsAutoPlayRefresh();
        }
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                read.Token.ThrowIfCancellationRequested();
                lock (_creditsAutoPlayGate)
                {
                    if (!OwnsCreditsAutoPlay(owner)) return;
                    // Access/transport revisions retire a response, not playback's
                    // preference read. Retry under fresh context for the same identity.
                    owner = owner with { Context = owner.Authority.ApiClient.CaptureContext() };
                    _creditsAutoPlayOwner = owner;
                }
                bool? enabled = null;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(read.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    if (string.IsNullOrWhiteSpace(owner.Context.ProfileId)) enabled = true;
                    else
                    {
                        var effective = await owner.Authority.SettingsApi.GetContractEffectiveSettingsAsync(
                            owner.Context, [AutoPlayNextSettingKey], ct: deadline.Token).ConfigureAwait(false);
                        enabled = ReadCreditsAutoPlay(effective);
                    }
                }
                catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501)
                {
                    // The older contract cannot report this preference; match
                    // the established enabled default rather than wait forever.
                    enabled = true;
                }
                catch (OperationCanceledException) when (read.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    LogToFile("state_trace.txt", $"Credits autoplay settings unavailable: {ex.Message}");
                }
                lock (_creditsAutoPlayGate)
                {
                    if (!OwnsCreditsAutoPlay(owner)) return;
                    if (enabled.HasValue && CompleteCreditsAutoPlayRefresh(owner, enabled.Value, read.Token)) return;
                }
                if (attempt < 2) await Task.Delay(250 << attempt, read.Token).ConfigureAwait(false);
            }
            lock (_creditsAutoPlayGate)
            {
                if (!OwnsCreditsAutoPlay(owner)) return;
                owner = owner with { Context = owner.Authority.ApiClient.CaptureContext() };
                _creditsAutoPlayOwner = owner;
                // Exhausted transient failures resolve conservatively to manual
                // Play now, never to an unverified enabled/stale preference.
                CompleteCreditsAutoPlayRefresh(owner, false, read.Token);
            }
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        finally
        {
            lock (_creditsAutoPlayGate)
                if (ReferenceEquals(_creditsAutoPlayRead, read)) _creditsAutoPlayRead = null;
            read.Dispose();
        }
    }

    private static bool ReadCreditsAutoPlay(ContractEffectiveSettingsResponse settings) =>
        !string.Equals(settings.Settings.FirstOrDefault(item => item.Key == AutoPlayNextSettingKey)?.Value.ToString().Trim(),
            "false", StringComparison.OrdinalIgnoreCase);
}
