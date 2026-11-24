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
            handlers['osc-set-auto-play-next']('true', '7', '42')
            handlers['osc-set-auto-play-next']('false', '6', '41')
            -- The Next Episode pill (final 5%, outside credits) is the viewer's own start.
            s.duration,s.time_pos=100,99.5
            s.next_ep_detail={title='Next'}; s.next_ep_available=true
            osc_test_layout(); osc_test_check_next(); osc_test_render_next()
            local pill=s.next_ep_rect
            s.mouse_x,s.mouse_y=pill.x+pill.w/2,pill.y+pill.h/2; osc_test_mouse_down()
            local nexts=sent_matching('silo-next-episode|')
            assert(#nexts==1 and nexts[1]=='silo-next-episode|viewer|owned-episode|42|7','Late native intents cannot be distinguished from a successor preparation')
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

    [Theory]
    [InlineData("intro", "FFD474", "Intro")]
    [InlineData("recap", "FFB4C4", "Recap")]
    [InlineData("credits", "ADA1FF", "Credits / Outro")]
    [InlineData("preview", "B5E95E", "Preview")]
    public void CompletionMarkerRegionsAndHoverUseCurrentKindPalette(string kind, string bgr, string label)
    {
        // Server #4033 uses the locked Tailwind 4.2.1 300 palette. ASS is sRGB/BGR.
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s=osc_test_state
            s.osc_disabled,s.current_alpha=false,1
            s.duration,s.time_pos=100,0
            s.marker_segments={ {kind='{{kind}}',start_seconds=10,end_seconds=20} }
            s.mouse_in_bar,s.mouse_x,s.mouse_y=false,-100,-100
            osc_test_layout(); osc_test_render_hud()
            assert(s.osc_overlay.data:find('\\1c&H{{bgr}}&\\1a&H8C&',1,true), 'Normal region lost its kind tint or 45 percent opacity')
            local sb=s.layout.seek_bar
            s.mouse_in_bar,s.mouse_x,s.mouse_y=true,sb.x1+(sb.x2-sb.x1)*0.15,sb.draw_y
            osc_test_render_hud()
            local ass=s.osc_overlay.data
            assert(ass:find('\\1c&H{{bgr}}&\\1a&H40&',1,true), 'Active region lost its matching brighter tint')
            assert(ass:find('{{label}}',1,true) and ass:find('\\1c&H{{bgr}}&\\1a&H00&',1,true), 'Hover label/dot lost the shared kind color')
            osc_test_mouse_down()
            assert(s.dragging_seek and math.abs(s.seek_drag_pos-0.15)<0.001,'Palette changed the continuous seek target')
            mp.set_property('user-data/completion','kind palette and seek')
            """, rawAss: true, resultProperty: "user-data/completion", beforeScript: AutoplayHudProbe);
        Assert.Equal("kind palette and seek", result);
    }
}
