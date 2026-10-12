using Microsoft.UI.Dispatching;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services.SiloCast;

/// <summary>Single dispatcher boundary for remote player access.</summary>
public sealed class SiloCastPlayerAdapter(PlayerService player, DispatcherQueue dispatcher)
{
    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
        {
            try { ct.ThrowIfCancellationRequested(); completion.TrySetResult(await action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(ct); }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new InvalidOperationException("The application is closing."));
        return completion.Task;
    }

    public Task<JsonObject> StateAsync(CancellationToken ct = default)
        => InvokeAsync(() => Task.FromResult(player.CaptureSiloCastState()), ct);

    public Task<bool> ControlAsync(JsonObject command, Func<bool> stillOwner, CancellationToken ct)
        => InvokeAsync(async () =>
        {
            if (!stillOwner()) return false;
            await player.ApplySiloCastControlAsync(command);
            return true;
        }, ct);

    public Task<bool> LaunchAsync(JsonObject playback, PlaybackAuthorityScope scope, Func<bool> stillOwner, CancellationToken ct,
        string controllerName = "Phone", string profileName = "Remote profile")
        => InvokeAsync(async () =>
        {
            if (!stillOwner()) return false;
            var contentId = playback["contentId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(contentId)) throw new ArgumentException("Missing content.");
            var position = playback["resumePosition"]?.GetValue<double>();
            if (position.HasValue && (!double.IsFinite(position.Value) || position < 0))
                throw new ArgumentException("Invalid resume position.");
            App.MainWindowInstance?.Activate();
            await player.PlayAsync(contentId, fromStart: playback["startFromBeginning"]?.GetValue<bool>() ?? false,
                fileId: playback["fileId"]?.GetValue<int>(), audioTrackIndex: playback["audioTrackIndex"]?.GetValue<int>(),
                subtitleSelection: playback["subtitleTrackIndex"]?.GetValue<int>(), startPositionOverride: position,
                authority: scope, cancellationToken: ct);
            if (!ReferenceEquals(player.ActiveAuthority, scope)) return false;
            if (player.Manager?.CurrentSession == null || player.State == PlayerState.Idle) return false;
            if (player.ErrorMessage != null) throw new InvalidOperationException(player.ErrorMessage);
            player.ShowSiloCastNotice(controllerName, profileName);
            return true;
        }, ct);

    public Task<bool> CloseAsync(PlaybackAuthorityScope? scope = null)
        => InvokeAsync(async () =>
        {
            if (scope != null && ReferenceEquals(player.ActiveAuthority, scope)) await player.CloseAsync();
            return true;
        });
}
