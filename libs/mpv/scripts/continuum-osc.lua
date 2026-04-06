-- Continuum Player - Custom OSC (On-Screen Controller)
-- A clean, modern control bar for the Continuum media player.
-- Renders via mpv's ASS/OSD overlay system with mouse input handling.

local mp = require 'mp'
local assdraw = require 'mp.assdraw'
local msg = require 'mp.msg'

--------------------------------------------------------------------------------
-- Configuration
--------------------------------------------------------------------------------
local config = {
    bar_height          = 80,
    gradient_height     = 40,
    bar_padding_x       = 20,
    bar_padding_bottom  = 12,

    -- Colors in ASS BGR hex (no # prefix)
    bar_bg_color        = "1A1A1A",
    accent_color        = "FCAE78",   -- #78AEFC in BGR
    text_color          = "FFFFFF",
    dim_text_color      = "999999",
    seek_bg_color       = "555555",
    seek_buffered_color = "777777",
    volume_bg_color     = "555555",

    -- Alpha (hex): 00=opaque, FF=transparent
    bar_bg_alpha        = "C8",       -- ~78% opaque
    gradient_alpha_top  = "FF",       -- fully transparent at gradient top
    gradient_alpha_bot  = "C8",       -- matches bar alpha at gradient bottom
    button_alpha        = "00",
    text_alpha          = "00",
    dim_text_alpha      = "44",

    -- Seek bar
    seek_height         = 4,
    seek_hover_height   = 8,
    seek_thumb_radius   = 7,
    seek_y_offset       = 38,         -- from bottom of bar area

    -- Volume
    volume_bar_width    = 60,
    volume_bar_height   = 4,
    volume_thumb_radius = 5,

    -- Timing
    hide_timeout        = 2.5,
    fade_duration       = 0.25,

    -- Stats overlay
    stats_padding       = 14,
    stats_line_height   = 22,
    stats_font_size     = 16,
    stats_bg_alpha      = "B0",

    -- Font sizes
    font_size_time      = 17,
    font_size_button    = 28,
    font_size_small_btn = 20,

    -- Button dimensions
    button_size         = 48,
    small_button_size   = 40,
}

--------------------------------------------------------------------------------
-- State
--------------------------------------------------------------------------------
local state = {
    -- Visibility / animation
    visible         = false,
    target_alpha    = 0,       -- 0..1
    current_alpha   = 0,       -- 0..1 (animated)
    last_fade_time  = 0,

    -- Mouse tracking
    mouse_x         = -1,
    mouse_y         = -1,
    mouse_in_bar    = false,
    mouse_in_window = true,

    -- Drag state
    dragging_seek   = false,
    dragging_volume = false,
    seek_drag_pos   = 0,       -- 0..1 ratio while dragging
    volume_drag_val = 0,

    -- Stats overlay
    stats_visible   = false,

    -- Properties from mpv
    time_pos        = 0,
    duration        = 0,
    pause           = false,
    volume          = 100,
    mute            = false,
    fullscreen      = false,
    idle            = true,
    track_list      = {},
    media_title     = "",
    sub_track       = 0,       -- current subtitle track ID (0 = none)
    filename        = "",
    video_params    = nil,
    video_codec     = "",
    audio_codec     = "",
    file_size       = 0,
    video_bitrate   = 0,
    audio_bitrate   = 0,
    demuxer_cache   = nil,

    -- OSD dimensions
    osd_width       = 1920,
    osd_height      = 1080,

    -- Timers
    hide_timer      = nil,
    tick_timer      = nil,

    -- Overlays
    osc_overlay     = nil,
    stats_overlay   = nil,

    -- Cache layout rects for hit-testing
    layout          = {},

    -- Hover tooltip
    seek_hover_time = -1,

    -- Mini-bar mode (OSC disabled)
    osc_disabled    = false,

    -- Media info (from host via osc-set-media-info)
    media_info      = nil,
    play_method_str = "",
    stream_type_str = "",
    protocol_str    = "",
    stream_codec_video = "",
    stream_codec_audio = "",

    -- Subtitle menu
    subtitle_tracks = {},
    active_subtitle = -1,
    subtitle_menu_visible = false,
    subtitle_menu_overlay = nil,
    subtitle_menu_items = {},
}

--------------------------------------------------------------------------------
-- Utility Functions
--------------------------------------------------------------------------------

-- Format seconds to H:MM:SS or M:SS
local function format_time(seconds)
    if not seconds or seconds < 0 then return "0:00" end
    local s = math.floor(seconds)
    local h = math.floor(s / 3600)
    local m = math.floor((s % 3600) / 60)
    local sec = s % 60
    if h > 0 then
        return string.format("%d:%02d:%02d", h, m, sec)
    else
        return string.format("%d:%02d", m, sec)
    end
end

-- Clamp value between min and max
local function clamp(val, lo, hi)
    if val < lo then return lo end
    if val > hi then return hi end
    return val
end

-- Check if point (px, py) is inside rectangle {x, y, w, h}
local function point_in_rect(px, py, rect)
    return px >= rect.x and px <= rect.x + rect.w
       and py >= rect.y and py <= rect.y + rect.h
end

-- Lerp
local function lerp(a, b, t)
    return a + (b - a) * t
end

-- ASS color tag from BGR hex string
local function ass_color(bgr_hex)
    return "\\1c&H" .. bgr_hex .. "&"
end

-- ASS border/outline color
local function ass_bord_color(bgr_hex)
    return "\\3c&H" .. bgr_hex .. "&"
end

-- ASS alpha tag (primary fill)
local function ass_alpha(hex_alpha)
    return "\\1a&H" .. hex_alpha .. "&"
end

-- ASS full alpha (all components)
local function ass_all_alpha(hex_alpha)
    return "\\1a&H" .. hex_alpha .. "&\\2a&H" .. hex_alpha .. "&\\3a&H" .. hex_alpha .. "&\\4a&H" .. hex_alpha .. "&"
end

-- Blend alpha hex with master opacity (0..1)
local function blend_alpha(base_hex, master_opacity)
    local base = tonumber(base_hex, 16) or 0
    -- base: 00=opaque, FF=transparent
    -- Effective transparency = base + (1-base/255)*(1-master_opacity)*255
    local base_opacity = 1.0 - (base / 255.0)
    local final_opacity = base_opacity * master_opacity
    local final_alpha = math.floor((1.0 - final_opacity) * 255 + 0.5)
    final_alpha = clamp(final_alpha, 0, 255)
    return string.format("%02X", final_alpha)
end

-- Get current OSD dimensions
local function update_osd_dimensions()
    local dim = mp.get_property_native("osd-dimensions")
    if dim then
        if dim.w and dim.w > 0 then state.osd_width = dim.w end
        if dim.h and dim.h > 0 then state.osd_height = dim.h end
    end
end

--------------------------------------------------------------------------------
-- ASS Drawing Helpers
--------------------------------------------------------------------------------

-- Draw a filled rectangle
local function draw_rect(ass, x1, y1, x2, y2, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}m %d %d l %d %d %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(x1), math.floor(y1),
        math.floor(x2), math.floor(y1),
        math.floor(x2), math.floor(y2),
        math.floor(x1), math.floor(y2)
    ))
end

-- Draw a filled rounded rectangle (approximated with sharp corners for simplicity)
local function draw_rounded_rect(ass, x1, y1, x2, y2, r, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    r = math.min(r, math.floor((x2 - x1) / 2), math.floor((y2 - y1) / 2))
    -- Use bezier curves for rounded corners
    local k = 0.5522847498  -- bezier control point factor for circle approximation
    local kr = math.floor(r * k)
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d " ..       -- start at top-left + r
        "l %d %d " ..       -- top edge to top-right - r
        "b %d %d %d %d %d %d " ..  -- top-right corner
        "l %d %d " ..       -- right edge to bottom-right - r
        "b %d %d %d %d %d %d " ..  -- bottom-right corner
        "l %d %d " ..       -- bottom edge to bottom-left + r
        "b %d %d %d %d %d %d " ..  -- bottom-left corner
        "l %d %d " ..       -- left edge to top-left + r
        "b %d %d %d %d %d %d " ..  -- top-left corner
        "{\\p0}",
        ass_color(color), ass_alpha(a),
        -- start: top-left corner, offset right by r
        math.floor(x1 + r), math.floor(y1),
        -- top-right - r
        math.floor(x2 - r), math.floor(y1),
        -- top-right corner bezier
        math.floor(x2 - r + kr), math.floor(y1),
        math.floor(x2), math.floor(y1 + r - kr),
        math.floor(x2), math.floor(y1 + r),
        -- bottom-right - r
        math.floor(x2), math.floor(y2 - r),
        -- bottom-right corner bezier
        math.floor(x2), math.floor(y2 - r + kr),
        math.floor(x2 - r + kr), math.floor(y2),
        math.floor(x2 - r), math.floor(y2),
        -- bottom-left + r
        math.floor(x1 + r), math.floor(y2),
        -- bottom-left corner bezier
        math.floor(x1 + r - kr), math.floor(y2),
        math.floor(x1), math.floor(y2 - r + kr),
        math.floor(x1), math.floor(y2 - r),
        -- top-left + r (back to near start)
        math.floor(x1), math.floor(y1 + r),
        -- top-left corner bezier
        math.floor(x1), math.floor(y1 + r - kr),
        math.floor(x1 + r - kr), math.floor(y1),
        math.floor(x1 + r), math.floor(y1)
    ))
end

-- Draw a filled circle
local function draw_circle(ass, cx, cy, r, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local k = math.floor(r * 0.5522847498)
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d " ..
        "b %d %d %d %d %d %d " ..
        "b %d %d %d %d %d %d " ..
        "b %d %d %d %d %d %d " ..
        "b %d %d %d %d %d %d " ..
        "{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx), math.floor(cy - r),
        -- top to right
        math.floor(cx + k), math.floor(cy - r),
        math.floor(cx + r), math.floor(cy - k),
        math.floor(cx + r), math.floor(cy),
        -- right to bottom
        math.floor(cx + r), math.floor(cy + k),
        math.floor(cx + k), math.floor(cy + r),
        math.floor(cx), math.floor(cy + r),
        -- bottom to left
        math.floor(cx - k), math.floor(cy + r),
        math.floor(cx - r), math.floor(cy + k),
        math.floor(cx - r), math.floor(cy),
        -- left to top
        math.floor(cx - r), math.floor(cy - k),
        math.floor(cx - k), math.floor(cy - r),
        math.floor(cx), math.floor(cy - r)
    ))
end

-- Draw text with ASS tags
local function draw_text(ass, x, y, text, font_size, color, alpha, master_alpha, alignment, font_name, bold)
    local a = blend_alpha(alpha, master_alpha)
    local an = alignment or 5  -- default center
    local fn = font_name or ""
    local b_tag = ""
    if bold then b_tag = "\\b1" end
    local fn_tag = ""
    if fn ~= "" then fn_tag = "\\fn" .. fn end
    ass:new_event()
    ass:pos(math.floor(x), math.floor(y))
    ass:append(string.format(
        "{\\an%d\\bord0\\shad0\\fs%d%s%s%s%s}%s",
        an,
        font_size,
        fn_tag, b_tag,
        ass_color(color), ass_alpha(a),
        text
    ))
end

-- Draw text with border/shadow for readability
local function draw_text_bordered(ass, x, y, text, font_size, color, alpha, bord_color, bord_alpha, master_alpha, alignment, font_name, bold)
    local a = blend_alpha(alpha, master_alpha)
    local ba = blend_alpha(bord_alpha, master_alpha)
    local an = alignment or 5
    local fn = font_name or ""
    local b_tag = ""
    if bold then b_tag = "\\b1" end
    local fn_tag = ""
    if fn ~= "" then fn_tag = "\\fn" .. fn end
    ass:new_event()
    ass:pos(math.floor(x), math.floor(y))
    ass:append(string.format(
        "{\\an%d\\bord1.5\\shad0\\fs%d%s%s%s%s%s%s}%s",
        an,
        font_size,
        fn_tag, b_tag,
        ass_color(color), ass_alpha(a),
        ass_bord_color(bord_color), "\\3a&H" .. ba .. "&",
        text
    ))
end

-- Draw a gradient rectangle (vertical, from top_alpha to bot_alpha)
local function draw_gradient(ass, x1, y1, x2, y2, color, top_alpha, bot_alpha, master_alpha, steps)
    steps = steps or 16
    local h = (y2 - y1) / steps
    for i = 0, steps - 1 do
        local t = i / steps
        -- Interpolate alpha
        local ta = tonumber(top_alpha, 16)
        local ba = tonumber(bot_alpha, 16)
        local ia = math.floor(ta + (ba - ta) * t + 0.5)
        local alpha_hex = string.format("%02X", ia)
        draw_rect(ass, x1, y1 + i * h, x2, y1 + (i + 1) * h + 1, color, alpha_hex, master_alpha)
    end
end

--------------------------------------------------------------------------------
-- Icon Drawing (using ASS draw primitives for clean geometric icons)
--------------------------------------------------------------------------------

-- Play triangle (pointing right)
local function draw_play_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local half = size / 2
    -- Slightly offset to the right for visual centering
    local ox = size * 0.1
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx - half * 0.7 + ox), math.floor(cy - half),
        math.floor(cx + half * 0.8 + ox), math.floor(cy),
        math.floor(cx - half * 0.7 + ox), math.floor(cy + half)
    ))
end

-- Pause icon (two vertical bars)
local function draw_pause_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local half = size / 2
    local bar_w = size * 0.22
    local gap = size * 0.15
    -- Left bar
    draw_rect(ass,
        cx - gap - bar_w, cy - half * 0.85,
        cx - gap, cy + half * 0.85,
        color, alpha, master_alpha)
    -- Right bar
    draw_rect(ass,
        cx + gap, cy - half * 0.85,
        cx + gap + bar_w, cy + half * 0.85,
        color, alpha, master_alpha)
end

-- Skip backward icon (two left-pointing triangles + "10" text)
local function draw_skip_back_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local s = size * 0.35
    -- Left triangle
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx + 1), math.floor(cy - s),
        math.floor(cx - s + 1), math.floor(cy),
        math.floor(cx + 1), math.floor(cy + s)
    ))
    -- Right triangle
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx + s + 2), math.floor(cy - s),
        math.floor(cx + 2), math.floor(cy),
        math.floor(cx + s + 2), math.floor(cy + s)
    ))
    -- "10" label
    draw_text(ass, cx, cy + size * 0.55, "10", math.floor(size * 0.45), color, alpha, master_alpha, 8)
end

-- Skip forward icon (two right-pointing triangles + "30" text)
local function draw_skip_fwd_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local s = size * 0.35
    -- Left triangle
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx - s - 2), math.floor(cy - s),
        math.floor(cx - 2), math.floor(cy),
        math.floor(cx - s - 2), math.floor(cy + s)
    ))
    -- Right triangle
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx - 1), math.floor(cy - s),
        math.floor(cx + s - 1), math.floor(cy),
        math.floor(cx - 1), math.floor(cy + s)
    ))
    -- "30" label
    draw_text(ass, cx, cy + size * 0.55, "30", math.floor(size * 0.45), color, alpha, master_alpha, 8)
end

-- Volume icon (speaker shape)
local function draw_volume_icon(ass, cx, cy, size, color, alpha, master_alpha, vol, is_muted)
    local a = blend_alpha(alpha, master_alpha)
    local s = size * 0.4
    -- Speaker body (small rectangle + cone)
    -- Rectangle part
    local rw = s * 0.35
    local rh = s * 0.6
    draw_rect(ass,
        cx - s * 0.5, cy - rh / 2,
        cx - s * 0.5 + rw, cy + rh / 2,
        color, alpha, master_alpha)
    -- Cone part (triangle)
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx - s * 0.5 + rw), math.floor(cy - rh / 2),
        math.floor(cx + s * 0.15), math.floor(cy - s * 0.75),
        math.floor(cx + s * 0.15), math.floor(cy + s * 0.75),
        math.floor(cx - s * 0.5 + rw), math.floor(cy + rh / 2)
    ))
    if is_muted then
        -- Draw X for mute
        local xo = s * 0.55
        local xs = s * 0.35
        -- Line 1 of X (top-left to bottom-right, drawn as thin rect rotated... use two thick lines)
        draw_text(ass, cx + xo + xs * 0.5, cy, "x", math.floor(size * 0.4), color, alpha, master_alpha, 5, nil, true)
    else
        -- Sound waves (arcs approximated as curved lines)
        if vol > 0 then
            -- Small arc
            local arc_x = cx + s * 0.3
            local arc_r = s * 0.35
            ass:new_event()
            ass:pos(0, 0)
            ass:append(string.format(
                "{\\an7\\bord%.1f\\shad0%s%s\\1a&HFF&%s\\p1}" ..
                "m %d %d b %d %d %d %d %d %d{\\p0}",
                size * 0.08,
                ass_color(color), "\\3a&H" .. a .. "&",
                ass_bord_color(color),
                math.floor(arc_x), math.floor(cy - arc_r),
                math.floor(arc_x + arc_r * 0.9), math.floor(cy - arc_r * 0.5),
                math.floor(arc_x + arc_r * 0.9), math.floor(cy + arc_r * 0.5),
                math.floor(arc_x), math.floor(cy + arc_r)
            ))
        end
        if vol > 50 then
            -- Larger arc
            local arc_x = cx + s * 0.3
            local arc_r = s * 0.65
            ass:new_event()
            ass:pos(0, 0)
            ass:append(string.format(
                "{\\an7\\bord%.1f\\shad0%s%s\\1a&HFF&%s\\p1}" ..
                "m %d %d b %d %d %d %d %d %d{\\p0}",
                size * 0.08,
                ass_color(color), "\\3a&H" .. a .. "&",
                ass_bord_color(color),
                math.floor(arc_x), math.floor(cy - arc_r),
                math.floor(arc_x + arc_r * 0.9), math.floor(cy - arc_r * 0.5),
                math.floor(arc_x + arc_r * 0.9), math.floor(cy + arc_r * 0.5),
                math.floor(arc_x), math.floor(cy + arc_r)
            ))
        end
    end
end

-- Fullscreen icon (four corner brackets)
local function draw_fullscreen_icon(ass, cx, cy, size, color, alpha, master_alpha, is_fullscreen)
    local a = blend_alpha(alpha, master_alpha)
    local s = size * 0.4
    local t = math.max(size * 0.1, 3)  -- line thickness, minimum 3px
    local corner_len = s * 0.6

    if not is_fullscreen then
        -- Expand: corners pointing outward
        -- Top-left
        draw_rect(ass, cx - s, cy - s, cx - s + corner_len, cy - s + t, color, alpha, master_alpha)
        draw_rect(ass, cx - s, cy - s, cx - s + t, cy - s + corner_len, color, alpha, master_alpha)
        -- Top-right
        draw_rect(ass, cx + s - corner_len, cy - s, cx + s, cy - s + t, color, alpha, master_alpha)
        draw_rect(ass, cx + s - t, cy - s, cx + s, cy - s + corner_len, color, alpha, master_alpha)
        -- Bottom-left
        draw_rect(ass, cx - s, cy + s - t, cx - s + corner_len, cy + s, color, alpha, master_alpha)
        draw_rect(ass, cx - s, cy + s - corner_len, cx - s + t, cy + s, color, alpha, master_alpha)
        -- Bottom-right
        draw_rect(ass, cx + s - corner_len, cy + s - t, cx + s, cy + s, color, alpha, master_alpha)
        draw_rect(ass, cx + s - t, cy + s - corner_len, cx + s, cy + s, color, alpha, master_alpha)
    else
        -- Collapse: elbows near center, arms extend outward toward corners
        local g = s * 0.35  -- gap from center for elbow positions
        -- Top-left: elbow at (cx-g, cy-g), arms extend left and up
        draw_rect(ass, cx - g - corner_len, cy - g, cx - g, cy - g + t, color, alpha, master_alpha)
        draw_rect(ass, cx - g, cy - g - corner_len, cx - g + t, cy - g, color, alpha, master_alpha)
        -- Top-right: elbow at (cx+g, cy-g), arms extend right and up
        draw_rect(ass, cx + g, cy - g, cx + g + corner_len, cy - g + t, color, alpha, master_alpha)
        draw_rect(ass, cx + g - t, cy - g - corner_len, cx + g, cy - g, color, alpha, master_alpha)
        -- Bottom-left: elbow at (cx-g, cy+g), arms extend left and down
        draw_rect(ass, cx - g - corner_len, cy + g - t, cx - g, cy + g, color, alpha, master_alpha)
        draw_rect(ass, cx - g, cy + g, cx - g + t, cy + g + corner_len, color, alpha, master_alpha)
        -- Bottom-right: elbow at (cx+g, cy+g), arms extend right and down
        draw_rect(ass, cx + g, cy + g - t, cx + g + corner_len, cy + g, color, alpha, master_alpha)
        draw_rect(ass, cx + g - t, cy + g, cx + g, cy + g + corner_len, color, alpha, master_alpha)
    end
end

-- Exit/close icon (X)
local function draw_exit_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local s = size * 0.32
    local t = size * 0.09
    -- Two rotated rectangles forming X, using ASS drawing
    -- Diagonal 1: top-left to bottom-right
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx - s - t), math.floor(cy - s),
        math.floor(cx - s + t), math.floor(cy - s),
        math.floor(cx + s + t), math.floor(cy + s),
        math.floor(cx + s - t), math.floor(cy + s)
    ))
    -- Diagonal 2: top-right to bottom-left
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(cx + s - t), math.floor(cy - s),
        math.floor(cx + s + t), math.floor(cy - s),
        math.floor(cx - s + t), math.floor(cy + s),
        math.floor(cx - s - t), math.floor(cy + s)
    ))
end

-- Minimize icon (horizontal bar)
local function draw_minimize_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local hw = size * 0.32   -- half-width
    local ht = math.max(size * 0.06, 2)  -- half-thickness, minimum 2px
    draw_rect(ass, cx - hw, cy - ht, cx + hw, cy + ht, color, alpha, master_alpha)
end

--------------------------------------------------------------------------------
-- Layout Computation
--------------------------------------------------------------------------------

local function compute_layout()
    update_osd_dimensions()
    local W = state.osd_width
    local H = state.osd_height
    local L = state.layout

    -- Bar area
    L.bar = {
        x = 0, y = H - config.bar_height,
        w = W, h = config.bar_height
    }

    -- Gradient area (above bar)
    L.gradient = {
        x = 0, y = H - config.bar_height - config.gradient_height,
        w = W, h = config.gradient_height
    }

    -- Controls Y center line (within bar, above seek bar)
    local controls_y = H - config.bar_padding_bottom - 16  -- center of bottom row controls
    local seek_y = H - config.seek_y_offset

    -- Left-side buttons
    local x_cursor = config.bar_padding_x

    -- Play/Pause
    L.btn_play = {
        x = x_cursor, y = controls_y - config.button_size / 2,
        w = config.button_size, h = config.button_size,
        cx = x_cursor + config.button_size / 2,
        cy = controls_y
    }
    x_cursor = x_cursor + config.button_size + 8

    -- Skip back 10s
    L.btn_skip_back = {
        x = x_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = x_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    x_cursor = x_cursor + config.small_button_size + 8

    -- Skip forward 30s
    L.btn_skip_fwd = {
        x = x_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = x_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    x_cursor = x_cursor + config.small_button_size + 14

    -- Right-side buttons (work backwards from right edge)
    local rx_cursor = W - config.bar_padding_x

    -- Exit button
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_exit = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 12

    -- Fullscreen button
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_fullscreen = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 12

    -- Minimize button
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_minimize = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 12

    -- Volume bar
    rx_cursor = rx_cursor - config.volume_bar_width
    L.volume_bar = {
        x = rx_cursor, y = controls_y - config.volume_bar_height / 2,
        w = config.volume_bar_width, h = config.volume_bar_height,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 6

    -- Volume icon
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_volume = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 10

    -- Stats info button ("i")
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_stats = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 6

    -- Subtitle CC button
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_cc = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 10

    -- Seek bar (fills remaining space between skip buttons and right-side buttons)
    local seek_x1 = x_cursor
    local seek_x2 = rx_cursor
    L.seek_bar = {
        x = seek_x1, y = seek_y - config.seek_hover_height / 2,
        w = seek_x2 - seek_x1, h = config.seek_hover_height + 12,  -- expanded hit area
        draw_y = seek_y,
        x1 = seek_x1,
        x2 = seek_x2
    }

    -- Centered time display above seek bar
    local seek_center_x = (seek_x1 + seek_x2) / 2
    L.time_center = {
        x = seek_center_x,
        y = seek_y - 10,  -- 10px above seek bar track
    }

    -- Hit test area for the entire bar (includes gradient for generous hover detection)
    L.bar_hit = {
        x = 0, y = H - config.bar_height - config.gradient_height / 2,
        w = W, h = config.bar_height + config.gradient_height / 2
    }
end

--------------------------------------------------------------------------------
-- Render the OSC
--------------------------------------------------------------------------------

local function render_osc()
    if state.current_alpha <= 0.01 then
        if state.osc_overlay then
            state.osc_overlay.data = ""
            state.osc_overlay:update()
        end
        return
    end

    compute_layout()

    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height
    local L = state.layout
    local ma = state.current_alpha  -- master alpha

    -- 1. Gradient fade (above bar)
    draw_gradient(ass,
        L.gradient.x, L.gradient.y,
        L.gradient.x + L.gradient.w, L.gradient.y + L.gradient.h,
        config.bar_bg_color,
        config.gradient_alpha_top, config.gradient_alpha_bot,
        ma, 20)

    -- 2. Bar background
    draw_rect(ass,
        L.bar.x, L.bar.y,
        L.bar.x + L.bar.w, L.bar.y + L.bar.h,
        config.bar_bg_color, config.bar_bg_alpha, ma)

    -- 3. Seek bar
    local seek_ratio = 0
    if state.duration > 0 then
        if state.dragging_seek then
            seek_ratio = state.seek_drag_pos
        else
            seek_ratio = state.time_pos / state.duration
        end
    end
    seek_ratio = clamp(seek_ratio, 0, 1)

    local sb = L.seek_bar
    local seek_draw_y = sb.draw_y
    local is_seek_hover = state.mouse_in_bar and
        state.mouse_y >= sb.y and state.mouse_y <= sb.y + sb.h and
        state.mouse_x >= sb.x1 and state.mouse_x <= sb.x2
    local current_seek_h = is_seek_hover and config.seek_hover_height or config.seek_height

    -- Seek track background
    draw_rounded_rect(ass,
        sb.x1, seek_draw_y - current_seek_h / 2,
        sb.x2, seek_draw_y + current_seek_h / 2,
        current_seek_h / 2,
        config.seek_bg_color, "00", ma)

    -- Buffered range
    local cache_state = state.demuxer_cache
    if cache_state and state.duration > 0 then
        local ranges = cache_state["seekable-ranges"]
        if ranges then
            for _, range in ipairs(ranges) do
                local rs = clamp(range["start"] / state.duration, 0, 1)
                local re = clamp(range["end"] / state.duration, 0, 1)
                local bx1 = sb.x1 + (sb.x2 - sb.x1) * rs
                local bx2 = sb.x1 + (sb.x2 - sb.x1) * re
                if bx2 > bx1 + 1 then
                    draw_rounded_rect(ass,
                        bx1, seek_draw_y - current_seek_h / 2,
                        bx2, seek_draw_y + current_seek_h / 2,
                        current_seek_h / 2,
                        config.seek_buffered_color, "00", ma)
                end
            end
        end
    end

    -- Seek progress fill
    local progress_x = sb.x1 + (sb.x2 - sb.x1) * seek_ratio
    if progress_x > sb.x1 + 1 then
        draw_rounded_rect(ass,
            sb.x1, seek_draw_y - current_seek_h / 2,
            progress_x, seek_draw_y + current_seek_h / 2,
            current_seek_h / 2,
            config.accent_color, "00", ma)
    end

    -- Seek thumb
    draw_circle(ass,
        progress_x, seek_draw_y,
        (is_seek_hover or state.dragging_seek) and config.seek_thumb_radius or config.seek_thumb_radius - 2,
        config.accent_color, "00", ma)

    -- Seek hover tooltip
    if is_seek_hover and not state.dragging_seek and state.duration > 0 then
        local hover_ratio = clamp((state.mouse_x - sb.x1) / (sb.x2 - sb.x1), 0, 1)
        local hover_time = hover_ratio * state.duration
        local tooltip_text = format_time(hover_time)
        local tooltip_x = clamp(state.mouse_x, sb.x1 + 30, sb.x2 - 30)
        local tooltip_y = seek_draw_y - 24

        -- Tooltip background
        local tw = #tooltip_text * 8 + 16
        draw_rounded_rect(ass,
            tooltip_x - tw / 2, tooltip_y - 13,
            tooltip_x + tw / 2, tooltip_y + 13,
            6,
            config.bar_bg_color, "90", ma)
        -- Tooltip text
        draw_text(ass, tooltip_x, tooltip_y, tooltip_text,
            14, config.text_color, "00", ma, 5)
    end

    -- Seek drag tooltip (follows thumb during drag)
    if state.dragging_seek and state.duration > 0 then
        local drag_time = state.seek_drag_pos * state.duration
        local tooltip_text = format_time(drag_time)
        local tooltip_x = clamp(progress_x, sb.x1 + 30, sb.x2 - 30)
        local tooltip_y = seek_draw_y - 24

        local tw = #tooltip_text * 8 + 16
        draw_rounded_rect(ass,
            tooltip_x - tw / 2, tooltip_y - 13,
            tooltip_x + tw / 2, tooltip_y + 13,
            6,
            config.bar_bg_color, "90", ma)
        draw_text(ass, tooltip_x, tooltip_y, tooltip_text,
            14, config.accent_color, "00", ma, 5)
    end

    -- 4. Play/Pause button
    local bp = L.btn_play
    if state.pause then
        draw_play_icon(ass, bp.cx, bp.cy, config.button_size * 0.6, config.text_color, "00", ma)
    else
        draw_pause_icon(ass, bp.cx, bp.cy, config.button_size * 0.6, config.text_color, "00", ma)
    end

    -- 5. Skip back 10s
    local bsb = L.btn_skip_back
    draw_skip_back_icon(ass, bsb.cx, bsb.cy, config.small_button_size * 0.7, config.text_color, "00", ma)

    -- 6. Skip forward 30s
    local bsf = L.btn_skip_fwd
    draw_skip_fwd_icon(ass, bsf.cx, bsf.cy, config.small_button_size * 0.7, config.text_color, "00", ma)

    -- 7. Centered time display above seek bar ("10:34 / 1:27:03")
    local tc = L.time_center
    local time_str = format_time(state.time_pos) .. " / " .. format_time(state.duration)
    draw_text(ass, tc.x, tc.y, time_str,
        config.font_size_time, config.dim_text_color, "00", ma, 2, nil, false)

    -- 8. CC (subtitle) button
    local bcc = L.btn_cc
    local cc_color = config.dim_text_color
    local cc_alpha = config.dim_text_alpha
    if state.sub_track > 0 then
        cc_color = config.accent_color
        cc_alpha = "00"
    end
    draw_text(ass, bcc.cx, bcc.cy, "CC",
        config.font_size_small_btn * 0.75, cc_color, cc_alpha, ma, 5, nil, true)

    -- 8b. Stats info button ("i")
    local bst = L.btn_stats
    local stats_color = config.dim_text_color
    local stats_alpha = config.dim_text_alpha
    if state.stats_visible then
        stats_color = config.accent_color
        stats_alpha = "00"
    end
    draw_text(ass, bst.cx, bst.cy, "i",
        config.font_size_small_btn, stats_color, stats_alpha, ma, 5, nil, true)

    -- 9. Volume icon
    local bv = L.btn_volume
    draw_volume_icon(ass, bv.cx, bv.cy, config.small_button_size,
        config.text_color, "00", ma,
        state.mute and 0 or state.volume, state.mute)

    -- 10. Volume bar
    local vb = L.volume_bar
    local vol_ratio = clamp(state.volume / 100, 0, 1)
    if state.dragging_volume then
        vol_ratio = clamp(state.volume_drag_val / 100, 0, 1)
    end

    -- Volume track background
    draw_rounded_rect(ass,
        vb.x, vb.cy - config.volume_bar_height / 2,
        vb.x + vb.w, vb.cy + config.volume_bar_height / 2,
        config.volume_bar_height / 2,
        config.volume_bg_color, "00", ma)

    -- Volume fill
    local vol_fill_x = vb.x + vb.w * vol_ratio
    if vol_fill_x > vb.x + 1 then
        draw_rounded_rect(ass,
            vb.x, vb.cy - config.volume_bar_height / 2,
            vol_fill_x, vb.cy + config.volume_bar_height / 2,
            config.volume_bar_height / 2,
            config.text_color, "00", ma)
    end

    -- Volume thumb
    draw_circle(ass, vol_fill_x, vb.cy, config.volume_thumb_radius,
        config.text_color, "00", ma)

    -- 11. Fullscreen button
    local bf = L.btn_fullscreen
    draw_fullscreen_icon(ass, bf.cx, bf.cy, config.small_button_size,
        config.text_color, "00", ma, state.fullscreen)

    -- 12. Minimize button
    local bm = L.btn_minimize
    draw_minimize_icon(ass, bm.cx, bm.cy, config.small_button_size,
        config.text_color, "00", ma)

    -- 13. Exit button
    local be = L.btn_exit
    draw_exit_icon(ass, be.cx, be.cy, config.small_button_size,
        config.text_color, "00", ma)

    -- Update overlay
    if not state.osc_overlay then
        state.osc_overlay = mp.create_osd_overlay("ass-events")
    end
    state.osc_overlay.data = ass.text
    state.osc_overlay.res_x = W
    state.osc_overlay.res_y = H
    state.osc_overlay.z = 50
    state.osc_overlay:update()
end

--------------------------------------------------------------------------------
-- Stats Overlay
--------------------------------------------------------------------------------

-- Format bytes to human-readable size
local function format_file_size(bytes)
    if not bytes or bytes <= 0 then return "0 B" end
    if bytes >= 1073741824 then return string.format("%.1f GiB", bytes / 1073741824) end
    if bytes >= 1048576 then return string.format("%.1f MiB", bytes / 1048576) end
    if bytes >= 1024 then return string.format("%.1f KiB", bytes / 1024) end
    return string.format("%d B", bytes)
end

-- Format bitrate
local function format_bitrate(bps)
    if not bps or bps <= 0 then return "" end
    if bps >= 1000000 then return string.format("%.1f Mbps", bps / 1000000) end
    if bps >= 1000 then return string.format("%.0f kbps", bps / 1000) end
    return string.format("%d bps", bps)
end

local function render_stats()
    if not state.stats_visible then
        if state.stats_overlay then
            state.stats_overlay.data = ""
            state.stats_overlay:update()
        end
        return
    end

    update_osd_dimensions()
    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local fs = config.stats_font_size
    local fs_small = math.max(fs - 2, 10)
    local padding = config.stats_padding
    local line_h = config.stats_line_height
    local section_gap = 12
    local header_h = line_h + 4
    local box_w = 380
    local box_x = 20
    local box_y = 50  -- below any top UI

    -- Build all sections
    local sections = {}

    -- Section 1: Player
    local s1 = { header = "PLAYER", rows = {} }
    table.insert(s1.rows, { label = "Player", value = "libmpv (GPU)" })
    if state.play_method_str ~= "" then
        table.insert(s1.rows, { label = "Play method", value = state.play_method_str })
    end
    if state.protocol_str ~= "" then
        table.insert(s1.rows, { label = "Protocol", value = state.protocol_str })
    end
    if state.stream_type_str ~= "" then
        table.insert(s1.rows, { label = "Stream type", value = state.stream_type_str })
    end
    table.insert(sections, s1)

    -- Section 2: Video Info (live)
    local s2 = { header = "VIDEO INFO", rows = {} }
    table.insert(s2.rows, { label = "Player dimensions", value = string.format("%dx%d", W, H) })
    local vw = mp.get_property_number("video-params/w")
    local vh = mp.get_property_number("video-params/h")
    if vw and vh and vw > 0 then
        table.insert(s2.rows, { label = "Video resolution", value = string.format("%dx%d", vw, vh) })
    end
    local dropped = (mp.get_property_number("vo-delayed-frame-count") or 0)
                  + (mp.get_property_number("decoder-frame-drop-count") or 0)
    table.insert(s2.rows, { label = "Dropped frames", value = tostring(dropped) })
    table.insert(s2.rows, { label = "Corrupted frames", value = "0" })
    table.insert(sections, s2)

    -- Section 3: Playback Stream Info
    local s3 = { header = "PLAYBACK STREAM INFO", rows = {} }
    if state.stream_codec_video ~= "" then
        table.insert(s3.rows, { label = "Video codec", value = state.stream_codec_video })
    end
    if state.stream_codec_audio ~= "" then
        table.insert(s3.rows, { label = "Audio codec", value = state.stream_codec_audio })
    end
    table.insert(sections, s3)

    -- Section 4: Original Media Info
    local s4 = { header = "ORIGINAL MEDIA INFO", rows = {} }
    local mi = state.media_info
    if mi then
        if mi.container and mi.container ~= "" then
            table.insert(s4.rows, { label = "Container", value = mi.container })
        end
        if mi.file_size and mi.file_size > 0 then
            table.insert(s4.rows, { label = "Size", value = format_file_size(mi.file_size) })
        end
        if mi.bitrate and mi.bitrate > 0 then
            table.insert(s4.rows, { label = "Bitrate", value = format_bitrate(mi.bitrate) })
        end
        if mi.codec_video and mi.codec_video ~= "" then
            table.insert(s4.rows, { label = "Video codec", value = string.upper(mi.codec_video) })
        end
        -- Video bitrate from mpv (live)
        local vb = mp.get_property_number("video-bitrate")
        if vb and vb > 0 then
            table.insert(s4.rows, { label = "Video bitrate", value = format_bitrate(vb) })
        end
        -- HDR / range type
        local vp = state.video_params
        if vp then
            local gamma = vp.gamma or ""
            local range_str = "SDR"
            if gamma == "pq" then range_str = "HDR10"
            elseif gamma == "hlg" then range_str = "HLG"
            end
            if mi.hdr and range_str == "SDR" then range_str = "HDR" end
            table.insert(s4.rows, { label = "Video range type", value = range_str })
        end
        if mi.audio_title and mi.audio_title ~= "" then
            table.insert(s4.rows, { label = "Audio codec", value = mi.audio_title })
        elseif mi.codec_audio and mi.codec_audio ~= "" then
            table.insert(s4.rows, { label = "Audio codec", value = string.upper(mi.codec_audio) })
        end
        -- Audio bitrate from mpv (live)
        local ab = mp.get_property_number("audio-bitrate")
        if ab and ab > 0 then
            table.insert(s4.rows, { label = "Audio bitrate", value = format_bitrate(ab) })
        end
        if mi.audio_channels and mi.audio_channels > 0 then
            table.insert(s4.rows, { label = "Audio channels", value = tostring(mi.audio_channels) })
        end
    end
    table.insert(sections, s4)

    -- Calculate total height
    local total_h = padding  -- top padding
    total_h = total_h + line_h + 8  -- header row ("Playback Info" + close X)
    for _, sec in ipairs(sections) do
        total_h = total_h + section_gap + header_h  -- section header
        total_h = total_h + #sec.rows * line_h      -- rows
    end
    total_h = total_h + padding  -- bottom padding

    -- Background
    draw_rounded_rect(ass,
        box_x, box_y,
        box_x + box_w, box_y + total_h,
        8,
        config.bar_bg_color, config.stats_bg_alpha, 1.0)

    local cy = box_y + padding

    -- Header: "Playback Info"
    draw_text(ass, box_x + padding, cy + line_h / 2, "Playback Info",
        fs + 1, config.text_color, "00", 1.0, 4, nil, true)
    -- Close X button (top-right)
    draw_text(ass, box_x + box_w - padding - 10, cy + line_h / 2, "✕",
        fs, config.dim_text_color, "00", 1.0, 6)
    -- Store close button hit area for click detection
    state.stats_close_rect = {
        x = box_x + box_w - padding - 30,
        y = cy,
        w = 40, h = line_h
    }
    cy = cy + line_h + 8

    -- Draw sections
    for _, sec in ipairs(sections) do
        cy = cy + section_gap
        -- Section header (uppercase, small, dim)
        draw_text(ass, box_x + padding, cy + header_h / 2, sec.header,
            fs_small, config.dim_text_color, "40", 1.0, 4, nil, true)
        cy = cy + header_h

        -- Rows
        for _, row in ipairs(sec.rows) do
            -- Label (left, dim)
            draw_text(ass, box_x + padding, cy + line_h / 2, row.label,
                fs, config.dim_text_color, "00", 1.0, 4)
            -- Value (right-aligned, bright)
            local val = row.value
            if #val > 38 then val = string.sub(val, 1, 35) .. "..." end
            draw_text(ass, box_x + box_w - padding, cy + line_h / 2, val,
                fs, config.text_color, "00", 1.0, 6)
            cy = cy + line_h
        end
    end

    -- Update overlay
    if not state.stats_overlay then
        state.stats_overlay = mp.create_osd_overlay("ass-events")
    end
    state.stats_overlay.data = ass.text
    state.stats_overlay.res_x = W
    state.stats_overlay.res_y = H
    state.stats_overlay.z = 60
    state.stats_overlay:update()
end

--------------------------------------------------------------------------------
-- Visibility / Animation
--------------------------------------------------------------------------------

local hide_osc  -- forward declaration

local function show_osc()
    state.visible = true
    state.target_alpha = 1
    -- Reset hide timer
    if state.hide_timer then
        state.hide_timer:kill()
    end
    state.hide_timer = mp.add_timeout(config.hide_timeout, function()
        if not state.mouse_in_bar and not state.pause and not state.dragging_seek and not state.dragging_volume then
            hide_osc()
        end
    end)
end

hide_osc = function()
    if state.dragging_seek or state.dragging_volume then return end
    if state.pause then return end  -- Stay visible when paused
    state.visible = false
    state.target_alpha = 0
    if state.hide_timer then
        state.hide_timer:kill()
        state.hide_timer = nil
    end
end

local last_cursor_visible = nil
local function request_cursor_visibility(visible)
    -- Tell mpv / the host app whether to show the cursor (only on change)
    if visible == last_cursor_visible then return end
    last_cursor_visible = visible
    if visible then
        mp.commandv("script-message", "osc-cursor-visible")
    else
        mp.commandv("script-message", "osc-cursor-hidden")
    end
end

local function tick()
    if state.osc_disabled then return end
    -- Animate alpha
    local now = mp.get_time()
    local dt = now - state.last_fade_time
    state.last_fade_time = now

    if config.fade_duration > 0 and dt > 0 then
        local speed = dt / config.fade_duration
        if state.current_alpha < state.target_alpha then
            state.current_alpha = math.min(state.current_alpha + speed, state.target_alpha)
        elseif state.current_alpha > state.target_alpha then
            state.current_alpha = math.max(state.current_alpha - speed, state.target_alpha)
        end
    else
        state.current_alpha = state.target_alpha
    end

    -- Render
    render_osc()

    -- Cursor visibility
    if state.current_alpha > 0.1 then
        request_cursor_visibility(true)
    else
        request_cursor_visibility(false)
    end
end

--------------------------------------------------------------------------------
-- Input Handling
--------------------------------------------------------------------------------

local function get_mouse_pos()
    -- When embedded with wid=, mp.get_property_number("mouse-pos/x") returns -1.
    -- Mouse position is tracked via script-message "osc-mouse-move" from the host app,
    -- or updated by handle_mouse_move() when mpv's mouse_move binding fires.
    -- Try mp.get_property_native("mouse-pos") which returns a table.
    local mpos = mp.get_property_native("mouse-pos")
    if mpos and mpos.x and mpos.y and mpos.x >= 0 and mpos.y >= 0 then
        return mpos.x, mpos.y
    end
    -- Fall back to state (set by script-message or previous calls)
    return state.mouse_x, state.mouse_y
end

local function handle_mouse_move()
    local mx, my = get_mouse_pos()
    if mx == nil or my == nil or mx < 0 or my < 0 then return end

    state.mouse_x = mx
    state.mouse_y = my

    -- Show OSC on any mouse movement
    show_osc()

    -- Check if mouse is over the bar area
    compute_layout()
    local L = state.layout
    if L.bar_hit then
        state.mouse_in_bar = point_in_rect(mx, my, L.bar_hit)
    end

    -- Handle seek drag
    if state.dragging_seek then
        local sb = L.seek_bar
        local ratio = clamp((mx - sb.x1) / (sb.x2 - sb.x1), 0, 1)
        state.seek_drag_pos = ratio
    end

    -- Handle volume drag
    if state.dragging_volume then
        local vb = L.volume_bar
        local ratio = clamp((mx - vb.x) / vb.w, 0, 1)
        state.volume_drag_val = ratio * 100
        mp.commandv("set", "volume", tostring(state.volume_drag_val))
    end
end

--------------------------------------------------------------------------------
-- Subtitle Menu
--------------------------------------------------------------------------------

-- Map language codes to display names
local function lang_name(code)
    if not code or code == "" then return "Unknown" end
    local map = {
        en = "English", eng = "English", es = "Spanish", spa = "Spanish",
        fr = "French", fre = "French", fra = "French", de = "German", ger = "German", deu = "German",
        it = "Italian", ita = "Italian", pt = "Portuguese", por = "Portuguese",
        ru = "Russian", rus = "Russian", ja = "Japanese", jpn = "Japanese",
        ko = "Korean", kor = "Korean", zh = "Chinese", chi = "Chinese", zho = "Chinese",
        ar = "Arabic", ara = "Arabic", hi = "Hindi", hin = "Hindi",
        tr = "Turkish", tur = "Turkish", pl = "Polish", pol = "Polish",
        nl = "Dutch", dut = "Dutch", nld = "Dutch", sv = "Swedish", swe = "Swedish",
        da = "Danish", dan = "Danish", fi = "Finnish", fin = "Finnish",
        no = "Norwegian", nob = "Norwegian", nor = "Norwegian",
        cs = "Czech", cze = "Czech", ces = "Czech", hu = "Hungarian", hun = "Hungarian",
        ro = "Romanian", rum = "Romanian", ron = "Romanian",
        bg = "Bulgarian", bul = "Bulgarian", hr = "Croatian", hrv = "Croatian",
        el = "Greek", gre = "Greek", ell = "Greek", he = "Hebrew", heb = "Hebrew",
        th = "Thai", tha = "Thai", vi = "Vietnamese", vie = "Vietnamese",
        id = "Indonesian", ind = "Indonesian", uk = "Ukrainian", ukr = "Ukrainian",
    }
    return map[code:lower()] or code:upper()
end

-- Source badge sort priority
local function source_priority(src)
    if src == "external" then return 0 end
    if src == "downloaded" then return 1 end
    return 2  -- embedded
end

-- Capitalize first letter
local function capitalize(s)
    if not s or s == "" then return "" end
    return s:sub(1,1):upper() .. s:sub(2)
end

local function render_subtitle_menu()
    if not state.subtitle_menu_visible then
        if state.subtitle_menu_overlay then
            state.subtitle_menu_overlay.data = ""
            state.subtitle_menu_overlay:update()
        end
        state.subtitle_menu_items = {}
        return
    end

    update_osd_dimensions()
    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local fs = config.stats_font_size
    local fs_small = math.max(fs - 2, 10)
    local padding = config.stats_padding
    local item_h = config.stats_line_height + 4
    local menu_w = 280

    -- Sort tracks by source priority
    local sorted = {}
    for _, t in ipairs(state.subtitle_tracks) do
        table.insert(sorted, t)
    end
    table.sort(sorted, function(a, b)
        return source_priority(a.source or "embedded") < source_priority(b.source or "embedded")
    end)

    -- Calculate menu height: header + "Off" + divider + tracks + divider + "Search Online..."
    local num_items = 1 + #sorted + 1  -- Off + tracks + Search
    local menu_h = padding * 2 + item_h + 4 + (#sorted * item_h) + 4 + item_h + 8  -- header area + items

    -- Position: above the CC button (bottom-right area)
    compute_layout()
    local L = state.layout
    local menu_x = W - padding - menu_w - 40
    local menu_y = H - config.bar_height - menu_h - 10
    if L.btn_cc then
        menu_x = L.btn_cc.x - menu_w / 2
        menu_y = L.btn_cc.y - menu_h - 10
    end
    -- Clamp to screen
    if menu_x < 10 then menu_x = 10 end
    if menu_y < 10 then menu_y = 10 end

    -- Background
    draw_rounded_rect(ass, menu_x, menu_y, menu_x + menu_w, menu_y + menu_h,
        8, config.bar_bg_color, config.stats_bg_alpha, 1.0)

    local cy = menu_y + padding
    state.subtitle_menu_items = {}

    -- Header
    draw_text(ass, menu_x + padding, cy + item_h / 2, "Subtitles",
        fs, config.text_color, "00", 1.0, 4, nil, true)
    cy = cy + item_h

    -- "Off" option
    local off_active = (state.active_subtitle <= 0)
    local off_color = off_active and config.text_color or config.dim_text_color
    if off_active then
        draw_text(ass, menu_x + padding, cy + item_h / 2, "✓",
            fs, config.text_color, "00", 1.0, 4)
    end
    draw_text(ass, menu_x + padding + 24, cy + item_h / 2, "Off",
        fs, off_color, "00", 1.0, 4)
    table.insert(state.subtitle_menu_items, {
        x = menu_x, y = cy, w = menu_w, h = item_h, action = "off"
    })
    cy = cy + item_h + 4  -- divider space

    -- Track items
    for _, track in ipairs(sorted) do
        local is_active = (track.index == state.active_subtitle)
        local text_color = is_active and config.text_color or config.dim_text_color

        -- Checkmark
        if is_active then
            draw_text(ass, menu_x + padding, cy + item_h / 2, "✓",
                fs, config.text_color, "00", 1.0, 4)
        end

        -- Language name
        local display = lang_name(track.language or "")
        if track.forced then display = display .. " (Forced)" end
        draw_text(ass, menu_x + padding + 24, cy + item_h / 2, display,
            fs, text_color, "00", 1.0, 4)

        -- Source badge (right-aligned)
        local badge = capitalize(track.source or "embedded")
        draw_text(ass, menu_x + menu_w - padding, cy + item_h / 2, badge,
            fs_small, config.dim_text_color, "40", 1.0, 6)

        table.insert(state.subtitle_menu_items, {
            x = menu_x, y = cy, w = menu_w, h = item_h,
            action = "select", index = track.index
        })
        cy = cy + item_h
    end

    cy = cy + 4  -- divider space

    -- "Search Online..." button
    draw_text(ass, menu_x + padding + 24, cy + item_h / 2, "Search Online...",
        fs, "6495ED", "00", 1.0, 4)  -- blue tint
    table.insert(state.subtitle_menu_items, {
        x = menu_x, y = cy, w = menu_w, h = item_h, action = "search"
    })

    -- Update overlay
    if not state.subtitle_menu_overlay then
        state.subtitle_menu_overlay = mp.create_osd_overlay("ass-events")
    end
    state.subtitle_menu_overlay.data = ass.text
    state.subtitle_menu_overlay.res_x = W
    state.subtitle_menu_overlay.res_y = H
    state.subtitle_menu_overlay.z = 70
    state.subtitle_menu_overlay:update()
end

-- Toggle stats (defined here so handle_mouse_down can reference it)
local function toggle_stats()
    state.stats_visible = not state.stats_visible
    render_stats()
end

local function handle_mouse_down()
    if state.osc_disabled then return end
    local mx = state.mouse_x
    local my = state.mouse_y

    -- Stats overlay close button (always check, even if OSC is hidden)
    if state.stats_visible and state.stats_close_rect then
        local r = state.stats_close_rect
        if mx >= r.x and mx <= r.x + r.w and my >= r.y and my <= r.y + r.h then
            state.stats_visible = false
            render_stats()
            return
        end
    end

    -- Subtitle menu click handling
    if state.subtitle_menu_visible then
        local handled = false
        for _, item in ipairs(state.subtitle_menu_items) do
            if point_in_rect(mx, my, item) then
                if item.action == "off" then
                    mp.commandv("script-message", "continuum-subtitle-select", "-1")
                elseif item.action == "select" then
                    mp.commandv("script-message", "continuum-subtitle-select", tostring(item.index))
                elseif item.action == "search" then
                    mp.commandv("script-message", "continuum-subtitle-search")
                end
                state.subtitle_menu_visible = false
                render_subtitle_menu()
                return
            end
        end
        -- Click outside menu — close it
        state.subtitle_menu_visible = false
        render_subtitle_menu()
        return
    end

    if state.current_alpha < 0.1 then return end

    compute_layout()
    local L = state.layout

    -- Check seek bar
    if L.seek_bar then
        local sb = L.seek_bar
        if mx >= sb.x1 and mx <= sb.x2 and my >= sb.y and my <= sb.y + sb.h then
            state.dragging_seek = true
            local ratio = clamp((mx - sb.x1) / (sb.x2 - sb.x1), 0, 1)
            state.seek_drag_pos = ratio
            return
        end
    end

    -- Check volume bar (extend hit area slightly)
    if L.volume_bar then
        local vb = L.volume_bar
        local hit = { x = vb.x - 4, y = vb.cy - 12, w = vb.w + 8, h = 24 }
        if point_in_rect(mx, my, hit) then
            state.dragging_volume = true
            local ratio = clamp((mx - vb.x) / vb.w, 0, 1)
            state.volume_drag_val = ratio * 100
            mp.commandv("set", "volume", tostring(state.volume_drag_val))
            return
        end
    end

    -- Check play/pause
    if L.btn_play and point_in_rect(mx, my, L.btn_play) then
        mp.commandv("cycle", "pause")
        return
    end

    -- Check skip back
    if L.btn_skip_back and point_in_rect(mx, my, L.btn_skip_back) then
        mp.commandv("seek", "-10", "relative")
        return
    end

    -- Check skip forward
    if L.btn_skip_fwd and point_in_rect(mx, my, L.btn_skip_fwd) then
        mp.commandv("seek", "30", "relative")
        return
    end

    -- Check CC (subtitle menu toggle)
    if L.btn_cc and point_in_rect(mx, my, L.btn_cc) then
        state.subtitle_menu_visible = not state.subtitle_menu_visible
        render_subtitle_menu()
        return
    end

    -- Check stats toggle
    if L.btn_stats and point_in_rect(mx, my, L.btn_stats) then
        toggle_stats()
        return
    end

    -- Check volume icon (toggle mute)
    if L.btn_volume and point_in_rect(mx, my, L.btn_volume) then
        mp.commandv("cycle", "mute")
        return
    end

    -- Check minimize
    if L.btn_minimize and point_in_rect(mx, my, L.btn_minimize) then
        mp.commandv("script-message", "continuum-minimize")
        return
    end

    -- Check fullscreen
    if L.btn_fullscreen and point_in_rect(mx, my, L.btn_fullscreen) then
        mp.commandv("script-message", "continuum-fullscreen-toggle")
        return
    end

    -- Check exit
    if L.btn_exit and point_in_rect(mx, my, L.btn_exit) then
        mp.commandv("script-message", "continuum-exit")
        return
    end
end

local function handle_mouse_down_right()
    if state.osc_disabled then return end
    local mx = state.mouse_x
    local my = state.mouse_y

    if state.current_alpha < 0.1 then return end

    compute_layout()
    local L = state.layout

    -- Right-click on CC button: cycle subtitle backward
    if L.btn_cc and point_in_rect(mx, my, L.btn_cc) then
        mp.commandv("cycle", "sub", "down")
        return
    end
end

local function handle_mouse_up()
    if state.osc_disabled then return end
    -- Complete seek drag
    if state.dragging_seek then
        state.dragging_seek = false
        if state.duration > 0 then
            local target_time = state.seek_drag_pos * state.duration
            mp.commandv("seek", tostring(target_time), "absolute")
        end
    end

    -- Complete volume drag
    if state.dragging_volume then
        state.dragging_volume = false
    end
end

local function handle_mouse_leave()
    state.mouse_in_window = false
    state.mouse_in_bar = false
    if not state.dragging_seek and not state.dragging_volume then
        -- Start hide timer (shorter since mouse left window)
        if state.hide_timer then state.hide_timer:kill() end
        state.hide_timer = mp.add_timeout(0.5, function()
            if not state.pause then
                hide_osc()
            end
        end)
    end
end

local function handle_mouse_enter()
    state.mouse_in_window = true
end

-- Scroll wheel for volume
local function handle_wheel_up()
    if state.current_alpha > 0.1 and state.mouse_in_bar then
        mp.commandv("add", "volume", "5")
    else
        mp.commandv("add", "volume", "2")
    end
    show_osc()
end

local function handle_wheel_down()
    if state.current_alpha > 0.1 and state.mouse_in_bar then
        mp.commandv("add", "volume", "-5")
    else
        mp.commandv("add", "volume", "-2")
    end
    show_osc()
end

-- Double-click for fullscreen (track timing of clicks)
local last_click_time = 0
local function handle_mbtn_left_dbl()
    mp.commandv("script-message", "continuum-fullscreen-toggle")
end

--------------------------------------------------------------------------------
-- Property Observers
--------------------------------------------------------------------------------

local function observe_properties()
    mp.observe_property("time-pos", "number", function(_, val)
        state.time_pos = val or 0
    end)

    mp.observe_property("duration", "number", function(_, val)
        state.duration = val or 0
    end)

    mp.observe_property("pause", "bool", function(_, val)
        state.pause = val or false
        if state.pause then
            show_osc()
        else
            -- Restart hide timer
            show_osc()
        end
    end)

    mp.observe_property("volume", "number", function(_, val)
        state.volume = val or 100
    end)

    mp.observe_property("mute", "bool", function(_, val)
        state.mute = val or false
    end)

    -- Fullscreen state is managed by the host — listen for its updates
    mp.register_script_message("osc-fullscreen-state", function(val)
        state.fullscreen = (val == "true")
    end)

    mp.register_script_message("osc-set-visibility", function(val)
        state.osc_disabled = (val == "false")
        if state.osc_disabled then
            if state.osc_overlay then
                state.osc_overlay.data = ""
                state.osc_overlay:update()
            end
            if state.stats_overlay then
                state.stats_overlay.data = ""
                state.stats_overlay:update()
            end
            if state.subtitle_menu_overlay then
                state.subtitle_menu_overlay.data = ""
                state.subtitle_menu_overlay:update()
            end
            state.stats_visible = false
            state.subtitle_menu_visible = false
        end
    end)

    mp.register_script_message("osc-set-media-info", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.media_info = data
        end
    end)

    mp.register_script_message("osc-set-stream-info", function(play_method, stream_type, protocol, video_codec, audio_codec)
        state.play_method_str = play_method or ""
        state.stream_type_str = stream_type or ""
        state.protocol_str = protocol or ""
        state.stream_codec_video = video_codec or ""
        state.stream_codec_audio = audio_codec or ""
    end)

    mp.register_script_message("osc-set-subtitles", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.subtitle_tracks = data
        end
    end)

    mp.register_script_message("osc-set-active-subtitle", function(idx)
        state.active_subtitle = tonumber(idx) or -1
    end)

    mp.observe_property("idle-active", "bool", function(_, val)
        state.idle = val or true
    end)

    mp.observe_property("media-title", "string", function(_, val)
        state.media_title = val or ""
    end)

    mp.observe_property("filename", "string", function(_, val)
        state.filename = val or ""
    end)

    mp.observe_property("video-params", "native", function(_, val)
        state.video_params = val
        if state.stats_visible then render_stats() end
    end)

    mp.observe_property("video-codec", "string", function(_, val)
        state.video_codec = val or ""
        if state.stats_visible then render_stats() end
    end)

    mp.observe_property("audio-codec-name", "string", function(_, val)
        state.audio_codec = val or ""
        if state.stats_visible then render_stats() end
    end)

    mp.observe_property("demuxer-cache-state", "native", function(_, val)
        state.demuxer_cache = val
    end)

    mp.observe_property("track-list", "native", function(_, val)
        state.track_list = val or {}
    end)

    mp.observe_property("current-tracks/sub/id", "number", function(_, val)
        state.sub_track = val or 0
    end)

    mp.observe_property("osd-dimensions", "native", function(_, val)
        if val then
            if val.w and val.w > 0 then state.osd_width = val.w end
            if val.h and val.h > 0 then state.osd_height = val.h end
        end
    end)
end

--------------------------------------------------------------------------------
-- Key Bindings
--------------------------------------------------------------------------------

local function setup_key_bindings()
    -- Mouse movement
    mp.add_key_binding("mouse_move", "continuum-osc-mouse-move", handle_mouse_move)

    -- Mouse button down/up
    -- mbtn_left click handled via osc-mouse-down/osc-mouse-up script messages
    -- from the host app. Do NOT also bind mbtn_left here — it would double-fire
    -- handle_mouse_down and the drag would never release.

    -- Right-click (for CC subtitle cycle backward)
    mp.add_key_binding("mbtn_right", "continuum-osc-mbtn-right", function()
        handle_mouse_move()  -- update position first
        handle_mouse_down_right()
    end)

    mp.add_key_binding("mbtn_left_dbl", "continuum-osc-mbtn-left-dbl", function()
        -- Double-click toggles fullscreen only if NOT on the control bar
        if not state.mouse_in_bar or state.current_alpha < 0.1 then
            handle_mbtn_left_dbl()
        end
    end)

    -- We need to detect mouse-up to finish drags
    -- mpv doesn't have a direct mouse-up event, so we poll via key binding
    -- The approach: use mbtn_left with complex binding that detects release
    -- Actually, for libmpv embedded, the host sends discrete press/release events
    -- We handle release via the repeatable binding pattern

    -- For mouse release detection, we use a timer-based approach
    -- When dragging, check each tick if the button is still held
    -- This is handled in the tick function by checking mouse state

    -- Scroll wheel for volume
    mp.add_key_binding("wheel_up", "continuum-osc-wheel-up", handle_wheel_up)
    mp.add_key_binding("wheel_down", "continuum-osc-wheel-down", handle_wheel_down)

    -- Mouse leave/enter
    mp.add_key_binding("mouse_leave", "continuum-osc-mouse-leave", handle_mouse_leave)
    mp.add_key_binding("mouse_enter", "continuum-osc-mouse-enter", handle_mouse_enter)

    -- Stats toggle
    mp.add_key_binding("i", "continuum-osc-toggle-stats", toggle_stats)
    mp.add_key_binding("I", "continuum-osc-toggle-stats-shift", toggle_stats)

    -- Override F key — prevent mpv's default "cycle fullscreen" from firing
    mp.add_forced_key_binding("f", "continuum-fs-override", function()
        mp.commandv("script-message", "continuum-fullscreen-toggle")
    end)

    -- Space for play/pause (as backup, mpv usually handles this)
    -- mp.add_key_binding("space", "continuum-osc-space", function() mp.commandv("cycle", "pause") end)
end

--------------------------------------------------------------------------------
-- Script Messages (for host app communication)
--------------------------------------------------------------------------------

local function setup_script_messages()
    -- Host app can send mouse coordinates explicitly
    -- This is the primary mouse tracking method when embedded with wid=
    mp.register_script_message("osc-mouse-move", function(x, y)
        local mx = tonumber(x)
        local my = tonumber(y)
        if not mx or not my then return end
        state.mouse_x = mx
        state.mouse_y = my

        -- Show OSC on any mouse movement
        show_osc()

        -- Check if mouse is over the bar area
        compute_layout()
        local L = state.layout
        if L.bar_hit then
            state.mouse_in_bar = point_in_rect(mx, my, L.bar_hit)
        end

        -- Handle seek drag
        if state.dragging_seek then
            local sb = L.seek_bar
            local ratio = clamp((mx - sb.x1) / (sb.x2 - sb.x1), 0, 1)
            state.seek_drag_pos = ratio
        end

        -- Handle volume drag
        if state.dragging_volume then
            local vb = L.volume_bar
            local ratio = clamp((mx - vb.x) / vb.w, 0, 1)
            state.volume_drag_val = ratio * 100
            mp.commandv("set", "volume", tostring(state.volume_drag_val))
        end
    end)

    -- Host app can send mouse button events
    mp.register_script_message("osc-mouse-down", function(x, y)
        state.mouse_x = tonumber(x) or state.mouse_x
        state.mouse_y = tonumber(y) or state.mouse_y
        handle_mouse_down()
    end)

    mp.register_script_message("osc-mouse-down-right", function(x, y)
        state.mouse_x = tonumber(x) or state.mouse_x
        state.mouse_y = tonumber(y) or state.mouse_y
        handle_mouse_down_right()
    end)

    mp.register_script_message("osc-mouse-up", function(x, y)
        if x and y then
            state.mouse_x = tonumber(x) or state.mouse_x
            state.mouse_y = tonumber(y) or state.mouse_y
        end
        handle_mouse_up()
    end)

    mp.register_script_message("osc-mouse-leave", function()
        handle_mouse_leave()
    end)

    mp.register_script_message("osc-mouse-enter", function()
        handle_mouse_enter()
    end)

    -- Host app can set play method info for stats display
    mp.register_script_message("osc-set-play-method", function(method)
        mp.set_property("user-data/play-method", method or "")
    end)

    -- Host can toggle visibility
    mp.register_script_message("osc-show", function()
        show_osc()
    end)

    mp.register_script_message("osc-hide", function()
        hide_osc()
    end)

    -- Host can toggle stats
    mp.register_script_message("osc-toggle-stats", function()
        toggle_stats()
    end)

    -- Host can query if mouse is over OSC (to decide whether to pass clicks to video)
    mp.register_script_message("osc-hit-test", function(x, y)
        local mx = tonumber(x) or 0
        local my = tonumber(y) or 0
        compute_layout()
        local L = state.layout
        local hit = false
        if state.current_alpha > 0.1 and L.bar_hit then
            hit = point_in_rect(mx, my, L.bar_hit)
        end
        mp.commandv("script-message", "osc-hit-test-result", tostring(hit))
    end)
end

--------------------------------------------------------------------------------
-- Initialization
--------------------------------------------------------------------------------

local function init()
    msg.info("Continuum OSC initializing...")

    -- Disable built-in OSC
    mp.commandv("set", "options/osc", "no")

    -- Initialize state
    state.last_fade_time = mp.get_time()
    state.current_alpha = 0
    state.target_alpha = 0

    -- Set up property observers
    observe_properties()

    -- Set up key bindings
    setup_key_bindings()

    -- Set up script messages for host app communication
    setup_script_messages()

    -- Create overlays
    state.osc_overlay = mp.create_osd_overlay("ass-events")
    state.stats_overlay = mp.create_osd_overlay("ass-events")

    -- Main tick timer (60fps target for smooth animation)
    local tick_interval = 1 / 30  -- 30fps for efficiency, still smooth enough
    state.tick_timer = mp.add_periodic_timer(tick_interval, tick)

    -- Show OSC briefly on file load
    mp.register_event("file-loaded", function()
        state.idle = false
        -- Reset drag state from previous session (mpv instance is reused)
        state.dragging_seek = false
        state.dragging_volume = false
        state.seek_drag_pos = nil
        show_osc()
    end)

    -- Clean up on shutdown
    mp.register_event("shutdown", function()
        if state.osc_overlay then
            state.osc_overlay:remove()
        end
        if state.stats_overlay then
            state.stats_overlay:remove()
        end
        if state.tick_timer then
            state.tick_timer:kill()
        end
        if state.hide_timer then
            state.hide_timer:kill()
        end
    end)

    msg.info("Continuum OSC initialized successfully")
end

-- Run initialization
init()
