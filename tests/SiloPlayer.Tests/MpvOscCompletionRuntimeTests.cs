namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Fact]
    public void CompletionCreditsIntentCarriesItsSourceAndNativeLoadOwner()
    {
        var result = CaptureStats(1920,1080,1920,1080,false,setup:"""
            local s=osc_test_state
            s.osc_disabled,s.current_alpha=false,1
            handlers['osc-set-markers']('{"content_id":"owned-episode","load_generation":42,"play_generation":7,"credits_start":90,"credits_end":99}')
            s.duration,s.time_pos=100,91
            s.next_ep_detail={title='Next'}; s.next_ep_available=true
            osc_test_check_countdown(); s.next_ep_countdown_started_at=mp.get_time()-11; osc_test_check_countdown()
            local nexts=sent_matching('silo-next-episode|')
            assert(#nexts==1 and nexts[1]=='silo-next-episode|automatic|owned-episode|42|7','Late native intents cannot be distinguished from a successor preparation')
            mp.set_property('user-data/completion','source-owned advance')
            """,rawAss:true,resultProperty:"user-data/completion",beforeScript:AutoplayHudProbe);
        Assert.Equal("source-owned advance",result);
    }
    [Fact]
    public void CompletionSoloOutageLabelsOnlyTheActualReconnectWait()
    {
        var result = CaptureStats(1920,1080,1920,1080,false,setup:"""
            local s=osc_test_state
            s.osc_disabled,s.playback_buffering=false,true
            assert(handlers['osc-set-connection-outage'],'No actual transport-outage signal reaches the wait surface')
            handlers['osc-set-connection-outage']('true'); osc_test_render_wait()
            assert(s.playback_wait_overlay.data:find('Reconnecting',1,true),'Solo outage is unlabeled')
            handlers['osc-set-watch-party']('{"visible":true,"catching_up":true,"connection_state":"connected"}')
            osc_test_render_wait()
            assert(s.playback_wait_overlay.data:find('Catching up',1,true) and not s.playback_wait_overlay.data:find('Reconnecting',1,true),'Room catch-up did not take precedence')
            handlers['osc-set-watch-party']('null'); handlers['osc-set-connection-outage']('false'); osc_test_render_wait()
            assert(not s.playback_wait_overlay.data:find('Reconnecting',1,true),'Recovered transport retained a reconnect label')
            mp.set_property('user-data/completion','owned outage')
            """,rawAss:true,resultProperty:"user-data/completion",beforeScript:AutoplayHudProbe);
        Assert.Equal("owned outage",result);
    }
    [Theory]
    [InlineData("000000", "808080")]
    [InlineData("555555", "808080")]
    [InlineData("808080", "808080")]
    public void CompletionAccentKeepsTextReadableOnThePlayerPanel(string stored, string expected)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            handlers['osc-set-theme']('{{stored}}')
            osc_test_state.osc_disabled, osc_test_state.current_alpha = false, 1
            osc_test_state.duration, osc_test_state.time_pos = 100, 40
            osc_test_layout(); osc_test_render_hud()
            assert(osc_test_state.osc_overlay.data:find('\\1c&H{{expected}}&',1,true), 'Dark/mid-grey accent is unreadable or unused on the timeline')
            mp.set_property('user-data/completion','readable')
            """, rawAss: true, resultProperty: "user-data/completion", beforeScript: AutoplayHudProbe);
        Assert.Equal("readable", result);
    }

    [Fact]
    public void CompletionChapterSegmentsKeepRealSeekAndHoverRouting()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s=osc_test_state
            s.osc_disabled,s.current_alpha=false,1
            s.duration,s.time_pos=100,40
            handlers['osc-set-chapters']('[{"index":0,"start_seconds":0,"end_seconds":25,"title":"Opening"},{"index":1,"start_seconds":25,"end_seconds":75,"title":"Middle"},{"index":2,"start_seconds":75,"end_seconds":100,"title":"Finale"}]')
            osc_test_layout(); local sb=s.layout.seek_bar
            s.mouse_in_bar,s.mouse_x,s.mouse_y=true,sb.x1+(sb.x2-sb.x1)*0.5,sb.draw_y
            osc_test_render_hud()
            assert(s.seek_segments and #s.seek_segments==3,'Chapter rail is still ticks instead of three separated segments')
            for i=2,3 do assert(s.seek_segments[i].x1>s.seek_segments[i-1].x2,'Chapter segments have no visible gap') end
            assert(s.osc_overlay.data:find('Middle',1,true),'Segment hover lost chapter metadata')
            osc_test_mouse_down()
            assert(s.dragging_seek and math.abs(s.seek_drag_pos-0.5)<0.001,'Segment gaps changed the continuous seek target')
            mp.set_property('user-data/completion','segments and seek')
            """, rawAss: true, resultProperty: "user-data/completion", beforeScript: AutoplayHudProbe);
        Assert.Equal("segments and seek", result);
    }

    [Fact]
    public void CompletionWaitLabelsDescribeOnlyPartyReconnectOrCatchUp()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s=osc_test_state
            s.osc_disabled,s.playback_buffering=false,true
            handlers['osc-set-watch-party']('{"visible":true,"connection_state":"reconnecting","catching_up":false}')
            osc_test_render_wait()
            assert(s.playback_wait_overlay.data:find('Reconnecting',1,true),'Reconnect wait has no label')
            handlers['osc-set-watch-party']('{"visible":true,"connection_state":"connected","catching_up":true}')
            osc_test_render_wait()
            assert(s.playback_wait_overlay.data:find('Catching up',1,true),'Catch-up wait has no label')
            handlers['osc-set-watch-party']('{"visible":true,"connection_state":"connected","catching_up":false}')
            osc_test_render_wait()
            assert(not s.playback_wait_overlay.data:find('Catching up',1,true) and not s.playback_wait_overlay.data:find('Reconnecting',1,true),'Ordinary cache wait received a party label')
            handlers['osc-set-watch-party']('{"visible":false,"connection_state":"reconnecting","catching_up":true}')
            osc_test_render_wait()
            assert(not s.playback_wait_overlay.data:find('Reconnecting',1,true),'Retired room leaked its label')
            mp.set_property('user-data/completion','specific wait labels')
            """, rawAss: true, resultProperty: "user-data/completion", beforeScript: AutoplayHudProbe);
        Assert.Equal("specific wait labels", result);
    }

    [Fact]
    public void CompletionCreditsStillIsOwnedAndRemovedWithTheCard()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s=osc_test_state
            s.osc_disabled,s.current_alpha=false,1
            s.duration,s.credits_start,s.credits_end,s.time_pos=100,90,99,91
            handlers['osc-set-next-episode-detail']('{"title":"Next","still_key":"owner-one","still_url":"fixture"}')
            s.next_ep_available=true; osc_test_check_countdown()
            assert(#sent_matching('silo-next-episode-still-request|owner-one')==1,'Card never requested its owned still')
            assert(handlers['osc-set-next-episode-still'],'Card has no owned image receiver')
            handlers['osc-set-next-episode-still']('old-owner','stale',112,63,448)
            assert(not s.next_ep_still,'Old owner adopted artwork')
            handlers['osc-set-next-episode-still']('owner-one','safe-blurred.bgra',112,63,448)
            assert(s.next_ep_still and s.next_ep_still_shown,'Still did not appear beside the episode title')
            local card=s.next_ep_still_rect
            assert(card and card.w==math.floor(112*osc_test_scale()) and card.h==math.floor(63*osc_test_scale()),'Credits still lost its reference dimensions')
            local cancel=s.next_ep_countdown_actions[2]
            s.mouse_x,s.mouse_y=cancel.x+5,cancel.y+5; osc_test_mouse_down()
            assert(not s.next_ep_still_shown,'Cancelled card retained an overlay above the video')
            handlers['osc-set-next-episode-detail']('null')
            assert(not s.next_ep_still,'Retired episode retained its bitmap')
            mp.set_property('user-data/completion','owned still')
            """, rawAss: true, resultProperty: "user-data/completion", beforeScript: AutoplayHudProbe);
        Assert.Equal("owned still", result);
    }
}
