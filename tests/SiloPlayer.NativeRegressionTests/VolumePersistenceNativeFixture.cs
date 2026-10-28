using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

internal static class VolumePersistenceNativeFixture
{
    internal static async Task RunAsync()
    {
        using var http = new HttpClient(new RejectNetwork());
        var client = new SiloApiClient(http); client.SetBaseUrl("https://volume.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "volume-settings"));
        var initial = settings.Load(); initial.PlayerVolume = 75; initial.PlayerMuted = false; settings.Save(initial);
        PlayerService Create() => new(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, new SettingsApi(client));
        // A real MainWindow supplies the dispatcher; otherwise SaveVolumeState
        // takes its immediate fallback and would not exercise the deferred write.
        if (SiloPlayer.App.MainWindowInstance == null) throw new InvalidOperationException("Volume test requires a real shell dispatcher.");
        using (var player = Create())
        {
            for (var value = 10; value <= 30; value++) { player.Volume = value; player.SaveVolumeState(); }
            if (settings.Load().PlayerVolume != 75) throw new InvalidOperationException("Volume writes were not deferred during input.");
            await Task.Delay(750);
            if (settings.Load().PlayerVolume != 30) throw new InvalidOperationException("Debounced final volume did not persist.");
            player.Volume = 0; player.IsMuted = false; player.SaveVolumeState();
            player.Dispose();
            if (settings.Load().PlayerVolume != 0 || settings.Load().PlayerMuted)
                throw new InvalidOperationException("Immediate shutdown lost pending zero/unmuted volume.");
        }
        await Task.Delay(650);
        if (settings.Load().PlayerVolume != 0) throw new InvalidOperationException("A stale timer overwrote shutdown volume.");
        using (var reopened = Create())
            if (reopened.Volume != 0 || reopened.IsMuted) throw new InvalidOperationException("Reopened player did not restore zero/unmuted volume.");
        Program.Log("PASS: actual shell dispatcher defers volume bursts, flushes zero/unmuted before500ms at shutdown and restores it without stale timer writes.");
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
