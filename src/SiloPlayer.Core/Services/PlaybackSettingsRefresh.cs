using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>Coalesces invalidations of the settings for one playing file.</summary>
public sealed class PlaybackSettingsRefresh
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;

    public Task RequestAsync(string? changedProfile, SettingsIdentityContext owner, CancellationToken lifetime,
        Func<bool> isCurrent, Func<CancellationToken, Task<ContractEffectiveSettingsResponse>> read,
        Func<Action, CancellationToken, Task> dispatch, Action<ContractEffectiveSettingsResponse> apply)
    {
        if ((changedProfile is not null && changedProfile != owner.Identity.ProfileId) ||
            lifetime.IsCancellationRequested || !isCurrent()) return Task.CompletedTask;

        CancellationTokenSource pending;
        lock (_gate)
        {
            if (!isCurrent()) return Task.CompletedTask;
            _pending?.Cancel();
            pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _pending = pending;
        }
        return RefreshAsync(pending, isCurrent, read, dispatch, apply);
    }

    public void Cancel()
    {
        lock (_gate) _pending?.Cancel();
    }

    private async Task RefreshAsync(CancellationTokenSource pending, Func<bool> isCurrent,
        Func<CancellationToken, Task<ContractEffectiveSettingsResponse>> read,
        Func<Action, CancellationToken, Task> dispatch, Action<ContractEffectiveSettingsResponse> apply)
    {
        var token = pending.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(175), token).ConfigureAwait(false);
            if (!isCurrent()) return;
            pending.CancelAfter(TimeSpan.FromSeconds(5));
            var settings = await read(token).ConfigureAwait(false);
            await dispatch(() =>
            {
                lock (_gate)
                    if (ReferenceEquals(pending, _pending) && !token.IsCancellationRequested && isCurrent())
                        apply(settings);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }
        catch (ApiException) { }
        catch (System.Text.Json.JsonException) { }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(pending, _pending)) _pending = null;
                pending.Dispose();
            }
        }
    }
}
