using System.Text.Json;
using SiloPlayer.Player;

internal static partial class PlaybackHudCompletionNativeFixture
{
    private static async Task LoadProductionOsc(MpvPlayer mpv)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args) { if (args.Length > 0 && args[0] == "silo-completion-ready") ready.TrySetResult(); }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var path = Path.Combine(Program.ResultDirectory, "completion-observer.lua");
            await File.WriteAllTextAsync(path, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs/mpv/scripts/silo-osc.lua")) + """

                mp.register_script_message('completion-query', function(id)
                    mp.commandv('script-message','silo-completion-query',id,require('mp.utils').format_json({
                        cover=state.loading_backdrop_shown~=nil, disabled=state.osc_disabled, next_title=state.next_ep_detail and state.next_ep_detail.title or "",
                        content=state.marker_content_id or '', load=state.marker_load_generation or 0, play=state.marker_play_generation or 0,
                        still=state.next_ep_still_shown~=nil, countdown=state.next_ep_countdown_active,
                        text=(state.playback_wait_overlay and state.playback_wait_overlay.data or ''):gsub('{[^}]*}','')}))
                end)
                -- Accelerate only the fixture clock, then execute the actual
                -- production countdown, host intent and service/manager start.
                mp.register_script_message('completion-expire-credits',function()
                    state.osc_disabled,state.post_roll_active=false,false
                    state.duration,state.time_pos,state.credits_start,state.credits_end=100,91,90,99
                    state.next_ep_available,state.next_ep_countdown_cancelled=true,false
                    state.next_ep_countdown_active=false
                    check_next_episode_countdown()
                    if state.next_ep_countdown_active then
                        state.next_ep_countdown_started_at=mp.get_time()-11
                        check_next_episode_countdown()
                    end
                end)
                mp.commandv('script-message','silo-completion-ready')
                """);
            Check(typeof(MpvPlayer).GetMethod("Command", Flags)!.Invoke(mpv, [(object)new[] { "load-script", path }]) is true,
                "Published production OSC could not load in the actual service player.");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { mpv.ScriptMessageReceived -= Message; }
    }

    private static async Task<JsonElement> QueryOsc(MpvPlayer mpv)
    {
        var id = Guid.NewGuid().ToString("N");
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args)
        {
            if (args.Length == 3 && args[0] == "silo-completion-query" && args[1] == id)
                reply.TrySetResult(JsonDocument.Parse(args[2]).RootElement.Clone());
        }
        mpv.ScriptMessageReceived += Message;
        try { mpv.SendScriptMessage("completion-query", id); return await reply.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
        finally { mpv.ScriptMessageReceived -= Message; }
    }

    private static async Task WaitForOscOwner(SiloPlayer.Services.PlayerService service)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            var data = await QueryOsc(service.Mpv!);
            if (data.GetProperty("content").GetString() == service.ContentId &&
                data.GetProperty("load").GetInt64() == (long)Field(service, "_fileLoadGeneration")! &&
                data.GetProperty("play").GetInt64() == (long)Field(service, "_playRequestGeneration")!) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Production OSC owner publication did not settle before accelerating the fixture clock.");
    }
}
