using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;

internal static partial class PlaybackHudCompletionNativeFixture
{
    private static async Task VerifyCreditsAutoPlay(PlayerService service, Wire wire)
    {
        var mpv=service.Mpv!;
        async Task Refresh() => await (Task)Call(service,"RefreshCreditsAutoPlayPreferenceAsync",CancellationToken.None)!;
        async Task Expect(string expected)
        {
            var until=DateTime.UtcNow.AddSeconds(4);
            while(DateTime.UtcNow<until)
            {
                if((await QueryOsc(mpv)).GetProperty("autoplay").GetString()==expected) return;
                await Task.Delay(25);
            }
            throw new InvalidOperationException("Current autoplay preference did not reach production OSC: "+expected);
        }

        var mode=Environment.GetEnvironmentVariable("SILO_HUD_COMPLETION_CASE");
        var api=SiloPlayer.App.Services.GetRequiredService<SiloApiClient>();
        if(mode=="credits-autoplay-failure")
        {
            wire.FailAutoPlayRead=true;
            await Refresh();await Expect("true");
            Program.Log("PASS: optional failed autoplay read resolves to the WebUI enabled default.");
            return;
        }
        if(mode=="credits-autoplay-reconnect")
        {
            wire.AutoPlay=false;await Refresh();await Expect("false");
            Check(api.TryInvalidateAccessContext(api.CaptureContext(),out _,transportCatchUp:true),"Transport catch-up was not accepted.");
            Call(service,"PublishPlaybackHudPreferences",mpv);await Expect("false");
            wire.AutoPlayReadEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.AutoPlayReadRelease=pendingRelease;
            var pendingRead=Refresh();await wire.AutoPlayReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Check(api.TryInvalidateAccessContext(api.CaptureContext(),out _,transportCatchUp:true),"In-flight transport catch-up was not accepted.");
            pendingRelease.TrySetResult();await pendingRead;await Expect("false");
            Program.Log("PASS: cached and in-flight autoplay preferences survive routine transport catch-up.");
            return;
        }

        service.SetNextEpisodeHint(service.ContentId!,"episode-2","Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:2);
        Call(service,"PublishCreditsEpisodeDetail",false);
        wire.AutoPlayDeviceOverride=true; await Refresh(); await Expect("true");
        var settings=SiloPlayer.App.Services.GetRequiredService<SettingsViewModel>();
        settings.AutoPlayNext=false;
        await Until(()=>wire.AutoPlayProfileWrites==1 && wire.AutoPlayOverrideDeletes==1,
            "The actual Settings toggle did not save and clear the device override.");
        await Expect("false");
        var starts=wire.Starts;
        mpv.SendScriptMessage("completion-expire-credits"); await Task.Delay(150);
        Check(wire.Starts==starts,"Effective autoplay off still advanced through the production credits route.");
        Program.Log("PASS: effective profile autoplay off reaches production OSC and suppresses automatic credits navigation.");

        settings.AutoPlayNext=true;
        await Until(()=>wire.AutoPlayProfileWrites==2 && wire.AutoPlayOverrideDeletes==2,
            "The actual Settings toggle did not persist re-enabled autoplay.");
        await Expect("true");
        Program.Log("PASS: actual Settings property saves notify active credits after device-override cleanup in both directions.");
        wire.AutoPlay=false; await Refresh(); await Expect("false");

        var reads=wire.AutoPlayReads;
        wire.AutoPlayEndpointMissing=true;
        await Refresh(); await Expect("true");
        Check(wire.AutoPlayReads==reads+1,"An unsupported effective endpoint did not resolve the enabled default immediately.");
        wire.AutoPlayEndpointMissing=false;
        wire.AutoPlayReadFailures=1;
        reads=wire.AutoPlayReads;
        await Refresh(); await Expect("false");
        Check(wire.AutoPlayReads==reads+2,"A transient autoplay failure was not retried to the explicit disabled value.");
        wire.ThrowAutoPlayReads=true;
        reads=wire.AutoPlayReads;
        await Refresh(); await Expect("true");
        Check(wire.AutoPlayReads==reads+3,"Persistent autoplay failures did not stop after the bounded retry budget.");
        wire.ThrowAutoPlayReads=false;
        Program.Log("PASS: unsupported autoplay defaults on; transient errors retry and exhausted errors use the WebUI enabled default.");

        // Cancellation of the loaded-media settings pipeline must not strand
        // its independent playback-owned autoplay read when no new file loads.
        wire.AutoPlayReadEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializerRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.AutoPlayReadRelease=initializerRelease;
        using(var initializerCancellation=new CancellationTokenSource())
        {
            var initialization=(Task)Call(service,"SendAutoSkipSettingsToOscAsync",initializerCancellation.Token)!;
            await wire.AutoPlayReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await Expect("unknown");
            initializerCancellation.Cancel();
            wire.AutoPlayReadRelease=null;
            initializerRelease.TrySetResult();
            try { await initialization; }
            catch(OperationCanceledException) when(initializerCancellation.IsCancellationRequested) { }
            await Expect("false");
        }

        // An access revision must cause a fresh read, never publication of the
        // old enabled response or a permanently unresolved preference.
        wire.AutoPlay=true;
        wire.AutoPlayReadEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var contextRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.AutoPlayReadRelease=contextRelease;
        reads=wire.AutoPlayReads;
        var contextRefresh=Refresh();
        await wire.AutoPlayReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        service.ActiveAuthority.ApiClient.InvalidateAccessContext();
        wire.AutoPlay=false; wire.AutoPlayReadRelease=null;
        contextRelease.TrySetResult();
        await contextRefresh; await Expect("false");
        Check(wire.AutoPlayReads==reads+2,"An access revision did not re-arm the current playback's preference read.");
        Program.Log("PASS: initializer cancellation without a successor and access-context revisions both resolve current autoplay.");

        wire.AutoPlayReadCancellations=0;
        wire.AutoPlayReadEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.AutoPlayReadRelease=release;
        var retired=Refresh(); await wire.AutoPlayReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await Expect("unknown");
        mpv.SendScriptMessage("completion-expire-credits"); await Task.Delay(100);
        Check(wire.Starts==starts,"An unresolved preference advanced through the production credits route.");

        // Retire the exact production owner while the old HTTP response is held.
        var generation=(long)Field(service,"_playRequestGeneration")!;
        typeof(PlayerService).GetField("_playRequestGeneration",Flags)!.SetValue(service,generation+1);
        Call(service,"SendMarkersToOsc"); await WaitForOscOwner(service);
        wire.AutoPlayReadRelease=null; wire.AutoPlay=true;
        await Refresh(); await Expect("true");
        release.TrySetResult(); await retired; await Expect("true");
        Check(wire.AutoPlayReadCancellations==1,"A newer preference read did not cancel its superseded HTTP work.");
        Program.Log("PASS: delayed retired preference cannot overwrite the successor playback owner.");

        mpv.SetProperty("pause","yes"); await Task.Delay(100);
        mpv.SendScriptMessage("completion-expire-credits"); await Task.Delay(100);
        Check(wire.Starts==starts,"Paused playback automatically advanced through the production credits route.");
        mpv.SetProperty("pause","no"); await Task.Delay(100);
        Call(service,"PublishCreditsEpisodeDetail",false);
        mpv.SendScriptMessage("completion-expire-credits");
        await Until(()=>wire.Starts==starts+1,"Enabled playing credits did not navigate through PlayerService.");
        await Task.Delay(150);
        Check(wire.Starts==starts+1,"Credits expiry submitted duplicate playback plans.");
        Program.Log("PASS: pause holds native credits; enabled playing expiry starts exactly one successor plan.");
    }
}
