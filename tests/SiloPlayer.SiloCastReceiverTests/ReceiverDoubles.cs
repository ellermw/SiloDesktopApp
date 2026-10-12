using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue;
}

namespace SiloPlayer.Services
{
    public enum PlayerState { Idle, Expanded, Fullscreen, Minimized, PictureInPicture }

    // Only the player boundary is doubled; receiver ownership, queueing, TLS,
    // handoff HTTP validation and temporary credentials remain production code.
    public sealed class PlayerService
    {
        private readonly PlaybackAuthorityScope _local;
        public PlayerService(PlaybackAuthorityScope local) { _local = local; ActiveAuthority = local; }
        public PlaybackAuthorityScope ActiveAuthority { get; private set; }
        public bool HasRemoteAuthority => !ReferenceEquals(ActiveAuthority, _local);
        public PlayerState State { get; private set; }
        public string? ContentId { get; private set; }
        public ConcurrentQueue<(string Content, string? Profile)> Launches { get; } = [];
        public ConcurrentQueue<(string Command, string? Profile)> Controls { get; } = [];
        public int CloseCalls { get; private set; }
        public event Action? ContentLoaded;
        public event Action<string>? SessionStarted;
        public event Action<PlayerState>? StateChanged;
        public event Action<PlaybackAuthorityScope>? RemoteAuthorityEnded;
        public void EnterFullscreen() { State = PlayerState.Fullscreen; StateChanged?.Invoke(State); }
        public void Launch(JsonObject playback, PlaybackAuthorityScope scope)
        {
            ActiveAuthority = scope;
            ContentId = playback["contentId"]!.GetValue<string>();
            Launches.Enqueue((ContentId, scope.ApiClient.ProfileId));
            SessionStarted?.Invoke("synthetic-session");
            State = PlayerState.Expanded;
            StateChanged?.Invoke(State);
            ContentLoaded?.Invoke();
        }
        public void PlayLocal(string content)
        {
            ActiveAuthority = _local;
            ContentId = content;
            State = PlayerState.Expanded;
            StateChanged?.Invoke(State);
        }
        public void Close()
        {
            CloseCalls++;
            var previous = ActiveAuthority;
            ActiveAuthority = _local;
            ContentId = null;
            State = PlayerState.Idle;
            StateChanged?.Invoke(State);
            if (!ReferenceEquals(previous, _local)) RemoteAuthorityEnded?.Invoke(previous);
        }
    }
}

namespace SiloPlayer.Services.SiloCast
{
    public static class SiloCastNetwork
    {
        public static IReadOnlyList<IPAddress> GetEligibleAddresses() => [IPAddress.Loopback];
    }

    public sealed class SiloCastAdvertisement : IAsyncDisposable
    {
        public ConcurrentQueue<IReadOnlyDictionary<string, string>> Publications { get; } = [];
        public int Port { get; private set; }
        public Task StartAsync(int port, IReadOnlyDictionary<string, string> txt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Port = port;
            Publications.Enqueue(new Dictionary<string, string>(txt));
            return Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class SiloCastPlayerAdapter(PlayerService player, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        { _ = dispatcher; ct.ThrowIfCancellationRequested(); return action(); }
        public Task<JsonObject> StateAsync(CancellationToken ct = default) => Task.FromResult(new JsonObject
        { ["contentId"] = player.ContentId, ["title"] = player.ContentId ?? "Ready", ["isPlaying"] = player.State != PlayerState.Idle });
        public Task<bool> LaunchAsync(JsonObject playback, PlaybackAuthorityScope scope, Func<bool> stillOwner,
            CancellationToken ct, string controllerName = "Phone", string profileName = "Remote profile")
        {
            ct.ThrowIfCancellationRequested();
            if (!stillOwner()) return Task.FromResult(false);
            player.Launch(playback, scope);
            return Task.FromResult(true);
        }
        public Task<bool> ControlAsync(JsonObject command, Func<bool> stillOwner, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!stillOwner()) return Task.FromResult(false);
            player.Controls.Enqueue((command["name"]!.GetValue<string>(), player.ActiveAuthority.ApiClient.ProfileId));
            return Task.FromResult(true);
        }
        public Task<bool> CloseAsync(PlaybackAuthorityScope? scope = null)
        {
            if (scope != null && ReferenceEquals(player.ActiveAuthority, scope)) player.Close();
            return Task.FromResult(true);
        }
    }
}
