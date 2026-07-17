-- Silo Player - Custom OSC (On-Screen Controller)
-- A clean, modern control bar for the Silo media player.
-- Renders via mpv's ASS/OSD overlay system with mouse input handling.

local mp = require 'mp'
local assdraw = require 'mp.assdraw'
local msg = require 'mp.msg'

--------------------------------------------------------------------------------
-- Configuration
--------------------------------------------------------------------------------
local config = {
    bar_height          = 80,        -- retained for floating action compatibility
    hud_height          = 172,
    gradient_height     = 72,
    bar_padding_x       = 24,
    bar_padding_bottom  = 20,

    -- Colors in ASS BGR hex (no # prefix)
    bar_bg_color        = "1A1A1A",
    accent_color        = "6EB8E8",   -- #E8B86E projector-lamp amber in BGR
    text_color          = "FFFFFF",
    dim_text_color      = "999999",
    seek_bg_color       = "FFFFFF",
    seek_buffered_color = "FFFFFF",
    volume_bg_color     = "555555",

    -- Alpha (hex): 00=opaque, FF=transparent
    bar_bg_alpha        = "20",       -- strong only at the bottom edge of the gradient
    gradient_alpha_top  = "FF",       -- fully transparent at gradient top
    gradient_alpha_bot  = "C8",       -- matches bar alpha at gradient bottom
    button_alpha        = "00",
    text_alpha          = "00",
    dim_text_alpha      = "44",

    -- Seek bar
    seek_height         = 3,
    seek_hover_height   = 5,
    seek_thumb_radius   = 7,
    seek_y_offset       = 106,        -- visual center above the transport row

    -- Volume
    volume_bar_width    = 96,
    volume_bar_height   = 3,
    volume_thumb_radius = 6,

    -- Timing
    hide_timeout        = 3.0,
    fade_duration       = 0.30,

    -- Stats overlay
    stats_padding       = 14,
    stats_line_height   = 22,
    stats_font_size     = 16,
    stats_bg_alpha      = "B0",

    -- Top title bar
    top_gradient_height = 80,
    font_size_title     = 18,
    font_size_subtitle  = 14,

    -- Font sizes
    font_size_time      = 11,
    font_size_button    = 28,
    font_size_small_btn = 20,

    -- Button dimensions
    button_size         = 56,
    small_button_size   = 44,
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
    marker_edit_available = false,
    marker_editor_active = false,
    marker_editor_visible = false,
    marker_editor_kind = "",
    marker_editor_start = -1,
    marker_editor_end = -1,
    marker_editor_original = {},
    marker_editor_draft = {},
    marker_editor_actions = {},
    marker_editor_panel_rect = nil,
    marker_editor_panel_x = nil,
    marker_editor_panel_y = nil,
    marker_editor_header_rect = nil,
    dragging_marker_panel = false,
    marker_panel_drag_start_x = 0,
    marker_panel_drag_start_y = 0,
    marker_panel_drag_origin_x = 0,
    marker_panel_drag_origin_y = 0,
    marker_editor_handle_rects = {},
    dragging_marker_edge = nil,

    -- Properties from mpv
    time_pos        = 0,
    duration        = 0,
    raw_time_pos    = 0,
    raw_duration    = 0,
    timeline_offset = 0,
    media_duration  = 0,
    can_seek_anywhere = true,
    pause           = false,
    volume          = 100,
    mute            = false,
    fullscreen      = false,
    picture_in_picture = false,
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
    last_subtitle = -1,
    subtitle_menu_visible = false,
    subtitle_menu_overlay = nil,
    subtitle_menu_items = {},
    subtitle_menu_offset = 1,
    subtitle_ai_available = false,

    -- Audio track menu
    audio_tracks = {},
    active_audio = -1,
    audio_menu_visible = false,
    audio_menu_overlay = nil,
    audio_menu_items = {},
    audio_menu_offset = 1,

    -- Chapters menu
    chapters = {},
    chapter_menu_visible = false,
    chapter_menu_overlay = nil,
    chapter_menu_items = {},
    chapter_menu_offset = 1,
    chapter_thumbnails = {},
    chapter_thumbnail_requested = {},
    chapter_thumbnail_overlay_visible = {},
    chapter_thumbnail_overlay_signature = {},

    -- Current WebUI Watch Party panel and room-sync state.
    watch_party = nil,
    watch_party_actions = {},
    watch_party_end_confirm = false,

    -- Live AI subtitle translation pauses only long enough for the first cue
    -- batch. This independent overlay mirrors VideoPlayer.tsx and remains
    -- visible while the transport HUD fades.
    translation_buffering = false,
    translation_buffering_label = "translated",
    translation_buffering_overlay = nil,
    translation_spinner_frame = -1,

    -- Quality menu
    quality_info        = nil,
    active_quality      = "original",
    quality_switching   = false,
    quality_menu_visible = false,
    quality_menu_overlay = nil,
    quality_menu_items  = {},
    keyboard_menu_kind = nil,
    keyboard_menu_index = -1,

    -- Skip markers (intro/credits)
    intro_start     = 0,
    intro_end       = 0,
    recap_start     = 0,
    recap_end       = 0,
    credits_start   = 0,
    credits_end     = 0,
    preview_start   = 0,
    preview_end     = 0,
    skip_visible    = false,
    skip_label      = "",
    skip_target     = 0,
    skip_overlay    = nil,
    skip_rect       = nil,

    -- Episode navigation. The host resolves the previous/next episode and
    -- tells us when the current item belongs to a series.
    series_context    = false,
    prev_ep_available = false,

    -- Next Episode button — shown when the host has a queued next episode
    -- AND we're either in the credits range or the final 5% of duration.
    -- Clicking it sends "silo-next-episode" to the host, which jumps
    -- straight to the next episode without waiting for end-of-file.
    next_ep_available = false,      -- set by osc-set-next-episode
    next_ep_visible   = false,
    next_ep_overlay   = nil,
    next_ep_rect      = nil,
    next_ep_detail    = nil,
    next_ep_countdown_active = false,
    next_ep_countdown_cancelled = false,
    next_ep_countdown_started_at = 0,
    next_ep_countdown_remaining = 10,
    next_ep_countdown_overlay = nil,
    next_ep_countdown_actions = {},

    -- Pause center indicator — 64x64 translucent circle with a play icon.
    -- Drawn via ASS overlay, shown on pause, hidden on resume.
    pause_indicator_overlay = nil,
    pause_indicator_shown   = false,
    ignore_video_click_until = 0,

    -- Content title (from host via osc-set-title)
    content_title   = "",
    content_subtitle = "",

    -- Notice overlay (admin messages)
    notice_visible  = false,
    notice_title    = "",
    notice_message  = "",
    notice_tone     = "info",
    notice_timer    = nil,
    notice_overlay  = nil,
}

local function update_media_timeline()
    local offset = math.max(tonumber(state.timeline_offset) or 0, 0)
    state.time_pos = math.max(tonumber(state.raw_time_pos) or 0, 0) + offset
    local authoritative = tonumber(state.media_duration) or 0
    if authoritative > 0 then
        state.duration = authoritative
    else
        local raw_duration = tonumber(state.raw_duration) or 0
        state.duration = raw_duration > 0 and (raw_duration + offset) or 0
    end
end

local function credits_marker_is_plausible()
    local dur = state.duration
    if dur <= 0 then return false end
    if state.credits_end <= state.credits_start then return false end
    if state.credits_start < 0 or state.credits_start >= dur then return false end

    -- Server-provided credits markers can be wildly early on some episodes.
    -- Treat credits as actionable only if the marker starts near the tail:
    -- at least within the last 5 minutes, with a 10% allowance for long files.
    local allowed_window = math.max(300, dur * 0.10)
    return (dur - state.credits_start) <= allowed_window
end

local quality_tiers = {
    { id = "original",   label = "Original" },
    { id = "auto",       label = "Auto" },
    { id = "1080p-high", label = "1080p High",  sublabel = "~10 Mbps" },
    { id = "1080p",      label = "1080p",        sublabel = "~6 Mbps" },
    { id = "720p-high",  label = "720p High",    sublabel = "~4 Mbps" },
    { id = "720p",       label = "720p",          sublabel = "~2 Mbps" },
    { id = "480p",       label = "480p",          sublabel = "~1.5 Mbps" },
    { id = "420p",       label = "420p",          sublabel = "~720 kbps" },
}

local quality_resolution_height = {
    ["2160p"] = 2160, ["1440p"] = 1440, ["1080p"] = 1080,
    ["720p"] = 720, ["480p"] = 480, ["420p"] = 420, ["360p"] = 360,
}

local function visible_quality_tiers()
    local result = {}
    local resolution = state.media_info and state.media_info.resolution or ""
    local native_height = quality_resolution_height[resolution] or 0
    for _, tier in ipairs(quality_tiers) do
        local copy = { id = tier.id, label = tier.label, sublabel = tier.sublabel }
        if tier.id == "original" and resolution ~= "" then
            copy.label = "Original (" .. (resolution == "2160p" and "4K" or resolution) .. ")"
            local details = {}
            if state.play_method_str and state.play_method_str ~= "" then
                table.insert(details, state.play_method_str)
            end
            local bitrate = state.media_info and state.media_info.bitrate or 0
            if bitrate > 0 then
                if bitrate >= 1000 then
                    local mbps = bitrate / 1000
                    table.insert(details, mbps % 1 == 0
                        and string.format("%d Mbps", mbps)
                        or string.format("%.1f Mbps", mbps))
                else
                    table.insert(details, string.format("%d kbps", bitrate))
                end
            end
            copy.sublabel = table.concat(details, " · ")
        end
        local tier_resolution = string.match(tier.id, "^(%d+p)")
        local tier_height = quality_resolution_height[tier_resolution or ""] or 0
        if tier.id == "original" or tier.id == "auto" or native_height == 0 or tier_height < native_height then
            table.insert(result, copy)
        end
    end
    return result
end

local function active_quality_label()
    if state.quality_switching then return "…" end
    for _, tier in ipairs(visible_quality_tiers()) do
        if tier.id == state.active_quality then return tier.label end
    end
    return "Quality"
end

local function consume_video_click()
    state.ignore_video_click_until = mp.get_time() + 0.5
end

local function resume_after_seek()
    mp.set_property_bool("pause", false)
    mp.add_timeout(0.10, function() mp.set_property_bool("pause", false) end)
    mp.add_timeout(0.35, function() mp.set_property_bool("pause", false) end)
    mp.add_timeout(0.75, function() mp.set_property_bool("pause", false) end)
end

local function seek_and_resume(target, flags)
    local was_paused = mp.get_property_bool("pause")
    mp.commandv("script-message", "silo-seek-absolute", tostring(target), tostring(not was_paused))
end

local function seek_relative_and_resume(delta)
    local was_paused = mp.get_property_bool("pause")
    mp.commandv("script-message", "silo-seek-relative", tostring(delta), tostring(not was_paused))
end

--------------------------------------------------------------------------------
-- Utility Functions
--------------------------------------------------------------------------------

-- DPI scale factor for 4K+ displays — scales font sizes in menus/stats/overlays
-- Based on OSD width: 1920 = 1.0x, 2560 = 1.33x, 3840 = 2.0x
local function ui_scale()
    local w = state.osd_width
    if w <= 1920 then return 1.0 end
    -- Dampened scaling: sqrt-based so 4K gets ~1.41x instead of 2x.
    -- Linear 2x made text too large and caused quality menu overlap.
    return math.sqrt(w / 1920)
end

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
        draw_rect(ass, x1, y1 + i * h, x2, y1 + (i + 1) * h, color, alpha_hex, master_alpha)
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

local function ass_escape_text(value)
    local text = tostring(value or "")
    text = text:gsub("\\", "\\\\")
    text = text:gsub("{", "\\{")
    text = text:gsub("}", "\\}")
    text = text:gsub("[\r\n]+", " ")
    return text
end

local function chapter_at_time(seconds)
    local last = nil
    for _, chapter in ipairs(state.chapters or {}) do
        local chapter_start = tonumber(chapter.start_seconds) or 0
        local chapter_end = tonumber(chapter.end_seconds) or math.huge
        last = chapter
        if seconds >= chapter_start and seconds < chapter_end then
            return chapter
        end
    end
    return last
end

local marker_regions = {
    { key = "intro", label = "Intro", color = "F8BD38" },
    { key = "recap", label = "Recap", color = "FA8BA7" },
    { key = "credits", label = "Credits / Outro", color = "24BFFB" },
    { key = "preview", label = "Preview", color = "99D334" },
}

local show_osc -- forward declaration; marker editor can open before input setup
local function request_tick()
    -- The periodic renderer normally paints within 33 ms. Resuming is a
    -- harmless immediate nudge when a callback fires while the timer is idle.
    if state.tick_timer then state.tick_timer:resume() end
end

local function clone_marker_range(range)
    if not range then return nil end
    return { start = range.start, finish = range.finish }
end

local function current_marker_range(kind)
    local marker_start = tonumber(state[kind .. "_start"]) or 0
    local marker_end = tonumber(state[kind .. "_end"]) or 0
    if marker_end <= marker_start then return nil end
    return { start = marker_start, finish = marker_end }
end

local function sync_active_marker_editor_range()
    local range = state.marker_editor_draft[state.marker_editor_kind]
    state.marker_editor_active = state.marker_editor_visible
    state.marker_editor_start = range and range.start or -1
    state.marker_editor_end = range and range.finish or -1
end

local function open_marker_editor()
    state.marker_editor_original = {}
    state.marker_editor_draft = {}
    state.marker_editor_panel_x = nil
    state.marker_editor_panel_y = nil
    state.marker_editor_header_rect = nil
    state.dragging_marker_panel = false
    for _, marker in ipairs(marker_regions) do
        local range = current_marker_range(marker.key)
        state.marker_editor_original[marker.key] = clone_marker_range(range)
        state.marker_editor_draft[marker.key] = clone_marker_range(range)
    end
    state.marker_editor_kind = "intro"
    state.marker_editor_visible = true
    sync_active_marker_editor_range()
    show_osc()
    request_tick()
end

local function close_marker_editor()
    state.marker_editor_visible = false
    state.marker_editor_active = false
    state.marker_editor_kind = ""
    state.marker_editor_start = -1
    state.marker_editor_end = -1
    state.marker_editor_original = {}
    state.marker_editor_draft = {}
    state.marker_editor_actions = {}
    state.marker_editor_panel_rect = nil
    state.marker_editor_header_rect = nil
    state.dragging_marker_panel = false
    state.marker_editor_handle_rects = {}
    state.dragging_marker_edge = nil
    request_tick()
end

local function marker_ranges_equal(left, right)
    if left == nil or right == nil then return left == nil and right == nil end
    return math.abs(left.start - right.start) < 0.001
        and math.abs(left.finish - right.finish) < 0.001
end

local function marker_editor_dirty()
    for _, marker in ipairs(marker_regions) do
        if not marker_ranges_equal(state.marker_editor_original[marker.key],
            state.marker_editor_draft[marker.key]) then return true end
    end
    return false
end

local function update_active_marker_draft_from_handles()
    if not state.marker_editor_visible or state.marker_editor_kind == "" then return end
    if state.marker_editor_start >= 0 and state.marker_editor_end > state.marker_editor_start then
        state.marker_editor_draft[state.marker_editor_kind] = {
            start = state.marker_editor_start,
            finish = state.marker_editor_end,
        }
    else
        state.marker_editor_draft[state.marker_editor_kind] = nil
    end
end

local function update_marker_panel_drag(mx, my)
    if not state.dragging_marker_panel or not state.marker_editor_panel_rect then return end
    local rect = state.marker_editor_panel_rect
    local margin = 8
    state.marker_editor_panel_x = clamp(
        state.marker_panel_drag_origin_x + (mx - state.marker_panel_drag_start_x),
        margin, math.max(margin, state.osd_width - rect.w - margin))
    state.marker_editor_panel_y = clamp(
        state.marker_panel_drag_origin_y + (my - state.marker_panel_drag_start_y),
        margin, math.max(margin, state.osd_height - rect.h - margin))
    request_tick()
end

local function update_marker_edge_drag(mx, seek_bar)
    if not state.dragging_marker_edge or not seek_bar or state.duration <= 0 then return end
    local ratio = clamp((mx - seek_bar.x1) / (seek_bar.x2 - seek_bar.x1), 0, 1)
    local seconds = ratio * state.duration
    if state.dragging_marker_edge == "start" then
        state.marker_editor_start = math.min(seconds, state.marker_editor_end - 0.5)
    else
        state.marker_editor_end = math.max(seconds, state.marker_editor_start + 0.5)
    end
    update_active_marker_draft_from_handles()
    request_tick()
end

local function render_marker_editor_panel(ass, W, H, ma, sc)
    state.marker_editor_actions = {}
    if not state.marker_editor_visible then return end

    local panel_w = math.floor(352 * sc)
    local padding = math.floor(12 * sc)
    local header_h = math.floor(58 * sc)
    local collapsed_h = math.floor(36 * sc)
    local active_h = math.floor(74 * sc)
    local footer_h = math.floor(48 * sc)
    local segment_h = 0
    for _, marker in ipairs(marker_regions) do
        segment_h = segment_h + (marker.key == state.marker_editor_kind and active_h or collapsed_h)
    end
    local panel_h = header_h + segment_h + footer_h + padding
    local anchored_x = math.floor(16 * sc)
    local anchored_y = math.max(math.floor(8 * sc), H - math.floor(176 * sc) - panel_h)
    local margin = math.floor(8 * sc)
    local panel_x = clamp(state.marker_editor_panel_x or anchored_x,
        margin, math.max(margin, W - panel_w - margin))
    local panel_y = clamp(state.marker_editor_panel_y or anchored_y,
        margin, math.max(margin, H - panel_h - margin))
    state.marker_editor_panel_x = panel_x
    state.marker_editor_panel_y = panel_y
    state.marker_editor_panel_rect = {
        x = panel_x, y = panel_y, w = panel_w, h = panel_h,
    }

    draw_rounded_rect(ass, panel_x - 1, panel_y - 1,
        panel_x + panel_w + 1, panel_y + panel_h + 1,
        math.floor(16 * sc), config.text_color, "E6", ma)
    draw_rounded_rect(ass, panel_x, panel_y,
        panel_x + panel_w, panel_y + panel_h,
        math.floor(16 * sc), "171717", "0D", ma)

    -- Draggable header geometry from MarkerEditPanel.tsx.
    draw_text(ass, panel_x + padding, panel_y + math.floor(20 * sc), "=",
        math.floor(15 * sc), config.text_color, "B0", ma, 4, "Consolas", true)
    draw_text(ass, panel_x + math.floor(34 * sc), panel_y + math.floor(19 * sc),
        "Edit markers", math.floor(14 * sc), config.text_color, "00", ma, 4, nil, true)
    draw_text(ass, panel_x + math.floor(34 * sc), panel_y + math.floor(39 * sc),
        "Drag the timeline handles, or set points to the playhead.",
        math.floor(10 * sc), config.text_color, "99", ma, 4, nil, false)
    local close_rect = {
        x = panel_x + panel_w - math.floor(36 * sc), y = panel_y + math.floor(8 * sc),
        w = math.floor(28 * sc), h = math.floor(28 * sc), action = "cancel",
    }
    state.marker_editor_header_rect = {
        x = panel_x, y = panel_y, w = close_rect.x - panel_x, h = header_h,
    }
    draw_text(ass, close_rect.x + close_rect.w / 2, close_rect.y + close_rect.h / 2,
        "x", math.floor(15 * sc), config.text_color, "66", ma, 5, "Consolas", false)
    table.insert(state.marker_editor_actions, close_rect)

    local cy = panel_y + header_h
    for _, marker in ipairs(marker_regions) do
        local active = marker.key == state.marker_editor_kind
        local row_h = active and active_h or collapsed_h
        if active then
            draw_rounded_rect(ass, panel_x + math.floor(8 * sc), cy,
                panel_x + panel_w - math.floor(8 * sc), cy + row_h - math.floor(2 * sc),
                math.floor(12 * sc), config.text_color, "EF", ma)
        end
        local range = state.marker_editor_draft[marker.key]
        draw_circle(ass, panel_x + math.floor(24 * sc), cy + math.floor(18 * sc),
            active and math.floor(5 * sc) or math.floor(4 * sc), marker.color,
            active and "00" or "70", ma)
        draw_text(ass, panel_x + math.floor(38 * sc), cy + math.floor(18 * sc),
            marker.label, math.floor(13 * sc), config.text_color,
            active and "00" or "48", ma, 4, nil, true)
        local range_text = range
            and (format_time(range.start) .. " - " .. format_time(range.finish))
            or "Not set"
        draw_text(ass, panel_x + panel_w - math.floor(20 * sc), cy + math.floor(18 * sc),
            range_text, math.floor(10 * sc), config.text_color,
            range and "48" or "99", ma, 6, "Consolas", false)
        table.insert(state.marker_editor_actions, {
            x = panel_x + math.floor(8 * sc), y = cy,
            w = panel_w - math.floor(16 * sc), h = collapsed_h,
            action = "select", kind = marker.key,
        })

        if active then
            local by = cy + math.floor(40 * sc)
            local button_h = math.floor(24 * sc)
            local start_rect = { x = panel_x + math.floor(38 * sc), y = by,
                w = math.floor(66 * sc), h = button_h, action = "set-start", kind = marker.key }
            local end_rect = { x = start_rect.x + start_rect.w + math.floor(6 * sc), y = by,
                w = math.floor(62 * sc), h = button_h, action = "set-end", kind = marker.key }
            for _, button in ipairs({ start_rect, end_rect }) do
                draw_rounded_rect(ass, button.x, button.y, button.x + button.w,
                    button.y + button.h, math.floor(6 * sc), config.text_color, "EE", ma)
                table.insert(state.marker_editor_actions, button)
            end
            draw_text(ass, start_rect.x + start_rect.w / 2, by + button_h / 2,
                "Set start", math.floor(10 * sc), config.text_color, "28", ma, 5, nil, true)
            draw_text(ass, end_rect.x + end_rect.w / 2, by + button_h / 2,
                "Set end", math.floor(10 * sc), config.text_color, "28", ma, 5, nil, true)

            local original = state.marker_editor_original[marker.key]
            if not marker_ranges_equal(original, range) then
                local reset_rect = { x = panel_x + panel_w - math.floor(68 * sc), y = by,
                    w = math.floor(28 * sc), h = button_h, action = "reset", kind = marker.key }
                draw_text(ass, reset_rect.x + reset_rect.w / 2, by + button_h / 2,
                    "R", math.floor(10 * sc), config.text_color, "66", ma, 5, nil, true)
                table.insert(state.marker_editor_actions, reset_rect)
            end
            if range then
                local clear_rect = { x = panel_x + panel_w - math.floor(38 * sc), y = by,
                    w = math.floor(28 * sc), h = button_h, action = "clear", kind = marker.key }
                draw_text(ass, clear_rect.x + clear_rect.w / 2, by + button_h / 2,
                    "x", math.floor(11 * sc), "7A7AEF", "30", ma, 5, "Consolas", true)
                table.insert(state.marker_editor_actions, clear_rect)
            end
        end
        cy = cy + row_h
    end

    local dirty = marker_editor_dirty()
    local footer_y = panel_y + panel_h - footer_h
    draw_rect(ass, panel_x, footer_y, panel_x + panel_w, footer_y + 1,
        config.text_color, "EF", ma)
    draw_text(ass, panel_x + padding, footer_y + footer_h / 2,
        format_time(state.time_pos), math.floor(10 * sc), config.text_color,
        "99", ma, 4, "Consolas", false)

    local save_rect = { x = panel_x + panel_w - math.floor(66 * sc),
        y = footer_y + math.floor(10 * sc), w = math.floor(54 * sc),
        h = math.floor(28 * sc), action = "save" }
    local cancel_rect = { x = save_rect.x - math.floor(60 * sc), y = save_rect.y,
        w = math.floor(54 * sc), h = save_rect.h, action = "cancel" }
    draw_text(ass, cancel_rect.x + cancel_rect.w / 2, cancel_rect.y + cancel_rect.h / 2,
        "Cancel", math.floor(11 * sc), config.text_color, "48", ma, 5, nil, true)
    table.insert(state.marker_editor_actions, cancel_rect)
    draw_rounded_rect(ass, save_rect.x, save_rect.y, save_rect.x + save_rect.w,
        save_rect.y + save_rect.h, math.floor(7 * sc), config.text_color,
        dirty and "00" or "99", ma)
    draw_text(ass, save_rect.x + save_rect.w / 2, save_rect.y + save_rect.h / 2,
        "Save", math.floor(11 * sc), "171717", dirty and "00" or "88", ma, 5, nil, true)
    if dirty then table.insert(state.marker_editor_actions, save_rect) end

    if dirty then
        local reset_all_rect = { x = cancel_rect.x - math.floor(72 * sc), y = save_rect.y,
            w = math.floor(68 * sc), h = save_rect.h, action = "reset-all" }
        draw_text(ass, reset_all_rect.x + reset_all_rect.w / 2,
            reset_all_rect.y + reset_all_rect.h / 2, "Reset all",
            math.floor(10 * sc), config.text_color, "66", ma, 5, nil, true)
        table.insert(state.marker_editor_actions, reset_all_rect)
    end
end

local function marker_at_time(seconds)
    local match = nil
    for _, marker in ipairs(marker_regions) do
        local marker_start = tonumber(state[marker.key .. "_start"]) or 0
        local marker_end = tonumber(state[marker.key .. "_end"]) or 0
        if marker_end > marker_start and seconds >= marker_start and seconds <= marker_end then
            if not match or (marker_end - marker_start) < (match.finish - match.start) then
                match = {
                    label = marker.label,
                    color = marker.color,
                    start = marker_start,
                    finish = marker_end,
                }
            end
        end
    end
    return match
end

-- Next episode icon — filled SkipForward glyph matching the WebUI's
-- lucide SkipForward control: one play triangle followed by an end bar.
-- Keep this deliberately separate from draw_skip_fwd_icon so the next-
-- episode action can never be mistaken for another timed seek button.
local function draw_next_episode_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local half_h = size * 0.42
    local left = cx - size * 0.36
    local point = cx + size * 0.16
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(left), math.floor(cy - half_h),
        math.floor(point), math.floor(cy),
        math.floor(left), math.floor(cy + half_h)
    ))
    local bar_w = math.max(2, size * 0.12)
    draw_rect(ass,
        cx + size * 0.28, cy - half_h,
        cx + size * 0.28 + bar_w, cy + half_h,
        color, alpha, master_alpha)
end

-- Previous episode icon — mirrored SkipBack glyph (end bar + play triangle).
local function draw_prev_episode_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local half_h = size * 0.42
    local point = cx - size * 0.16
    local right = cx + size * 0.36
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format(
        "{\\an7\\bord0\\shad0%s%s\\p1}" ..
        "m %d %d l %d %d %d %d{\\p0}",
        ass_color(color), ass_alpha(a),
        math.floor(right), math.floor(cy - half_h),
        math.floor(point), math.floor(cy),
        math.floor(right), math.floor(cy + half_h)
    ))
    local bar_w = math.max(2, size * 0.12)
    draw_rect(ass,
        cx - size * 0.28 - bar_w, cy - half_h,
        cx - size * 0.28, cy + half_h,
        color, alpha, master_alpha)
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
    local s = size * 0.28
    local t = math.max(size * 0.07, 2)  -- line thickness
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
    local s = size * 0.22
    local t = size * 0.06
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

-- Quality/settings icon (three horizontal lines)
local function draw_quality_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local hw = size * 0.28   -- half-width of lines
    local ht = math.max(size * 0.05, 2)  -- half-thickness
    local gap = size * 0.18  -- vertical gap between lines
    for i = -1, 1 do
        local ly = cy + i * gap
        draw_rect(ass, cx - hw, ly - ht, cx + hw, ly + ht, color, alpha, master_alpha)
    end
end

--------------------------------------------------------------------------------
-- Layout Computation
--------------------------------------------------------------------------------

local _layout_w, _layout_h = 0, 0
local _layout_next_ep = nil
local _layout_prev_ep = nil
local _layout_series = nil
local _layout_marker_edit = nil
local _layout_audio_count = -1
local _layout_chapter_count = -1
local function compute_layout()
    update_osd_dimensions()
    local W = state.osd_width
    local H = state.osd_height
    -- Skip recomputation if dimensions unchanged
    if W == _layout_w and H == _layout_h and _layout_next_ep == state.next_ep_available
        and _layout_prev_ep == state.prev_ep_available
        and _layout_series == state.series_context
        and _layout_marker_edit == state.marker_edit_available
        and _layout_audio_count == #state.audio_tracks
        and _layout_chapter_count == #state.chapters
        and state.layout.bar then return end
    _layout_w, _layout_h = W, H
    _layout_next_ep = state.next_ep_available
    _layout_prev_ep = state.prev_ep_available
    _layout_series = state.series_context
    _layout_marker_edit = state.marker_edit_available
    _layout_audio_count = #state.audio_tracks
    _layout_chapter_count = #state.chapters
    local L = state.layout
    local sc = ui_scale()
    local pad = math.floor(config.bar_padding_x * sc)
    local main_size = math.floor(config.button_size * sc)
    local small_size = math.floor(config.small_button_size * sc)
    local gap = math.floor(12 * sc)
    local controls_y = H - math.floor(config.bar_padding_bottom * sc) - main_size / 2
    local seek_y = H - math.floor(config.seek_y_offset * sc)

    -- The current WebUI uses a transparent cinema HUD, not an opaque bar.
    L.bar = { x = 0, y = H - math.floor(config.hud_height * sc), w = W, h = math.floor(config.hud_height * sc) }
    L.gradient = { x = 0, y = L.bar.y - math.floor(config.gradient_height * sc), w = W, h = L.bar.h + math.floor(config.gradient_height * sc) }

    -- Main transport is locked to the exact frame center.
    L.btn_play = {
        x = W / 2 - main_size / 2, y = controls_y - main_size / 2,
        w = main_size, h = main_size, cx = W / 2, cy = controls_y
    }
    L.btn_skip_back = {
        x = L.btn_play.x - gap - small_size, y = controls_y - small_size / 2,
        w = small_size, h = small_size,
        cx = L.btn_play.x - gap - small_size / 2, cy = controls_y
    }
    L.btn_skip_fwd = {
        x = L.btn_play.x + main_size + gap, y = controls_y - small_size / 2,
        w = small_size, h = small_size,
        cx = L.btn_play.x + main_size + gap + small_size / 2, cy = controls_y
    }
    if state.series_context and state.prev_ep_available then
        L.btn_prev_ep = {
            x = L.btn_skip_back.x - gap - small_size, y = controls_y - small_size / 2,
            w = small_size, h = small_size,
            cx = L.btn_skip_back.x - gap - small_size / 2, cy = controls_y
        }
    else
        L.btn_prev_ep = nil
    end
    if state.next_ep_available then
        L.btn_next_ep = {
            x = L.btn_skip_fwd.x + small_size + gap, y = controls_y - small_size / 2,
            w = small_size, h = small_size,
            cx = L.btn_skip_fwd.x + small_size + gap + small_size / 2, cy = controls_y
        }
    else
        L.btn_next_ep = nil
    end

    -- Top-left chrome matches VideoPlayer.tsx: circular minimize + Exit pill.
    local top = math.floor(16 * sc)
    L.btn_minimize = { x = top, y = top, w = small_size, h = small_size, cx = top + small_size / 2, cy = top + small_size / 2 }
    local exit_w = math.floor(82 * sc)
    L.btn_exit = { x = top + small_size + math.floor(12 * sc), y = top, w = exit_w, h = small_size,
        cx = top + small_size + math.floor(12 * sc) + exit_w / 2, cy = top + small_size / 2 }

    -- Right utility rail, in the same order as the WebUI controls that the
    -- native client currently exposes.
    local utility_size = math.floor(40 * sc)
    local utility_gap = math.floor(2 * sc)
    local rx_cursor = W - pad
    local function place_utility(width)
        width = width or utility_size
        rx_cursor = rx_cursor - width
        local rect = { x = rx_cursor, y = controls_y - utility_size / 2, w = width, h = utility_size,
            cx = rx_cursor + width / 2, cy = controls_y }
        rx_cursor = rx_cursor - utility_gap
        return rect
    end
    L.btn_fullscreen = place_utility()
    L.btn_pip = place_utility()
    L.btn_stats = place_utility()
    L.btn_marker_edit = state.marker_edit_available and place_utility() or nil
    local show_quality_label = W >= math.floor(640 * sc)
    L.btn_quality = place_utility(show_quality_label and math.floor(140 * sc) or utility_size)
    L.btn_quality.show_label = show_quality_label
    L.btn_cc = place_utility()
    L.btn_chapters = #state.chapters > 0 and place_utility() or nil
    L.btn_audio = #state.audio_tracks > 0 and place_utility() or nil

    rx_cursor = rx_cursor - math.floor(6 * sc) - math.floor(config.volume_bar_width * sc)
    L.volume_bar = {
        x = rx_cursor, y = controls_y - math.floor(config.volume_bar_height * sc) / 2,
        w = math.floor(config.volume_bar_width * sc), h = math.floor(config.volume_bar_height * sc), cy = controls_y
    }
    L.utility_divider_x = L.volume_bar.x + L.volume_bar.w + math.floor(3 * sc)
    rx_cursor = rx_cursor - math.floor(4 * sc)
    L.btn_volume = place_utility()

    -- Seek rail spans the frame above all three HUD columns.
    local seek_x1 = pad + math.floor(8 * sc)
    local seek_x2 = W - pad - math.floor(8 * sc)
    L.seek_bar = {
        x = seek_x1, y = seek_y - math.floor(22 * sc),
        w = seek_x2 - seek_x1, h = math.floor(44 * sc),
        draw_y = seek_y,
        x1 = seek_x1,
        x2 = seek_x2
    }
    local left_transport_x = L.btn_prev_ep and L.btn_prev_ep.x or L.btn_skip_back.x
    L.metadata = { x = pad, y = controls_y, max_w = math.max(0, left_transport_x - pad - math.floor(20 * sc)) }
    L.bar_hit = { x = 0, y = L.gradient.y, w = W, h = H - L.gradient.y }
end

-- Picture-in-picture icon — outlined display with a floating inset frame.
local function draw_pip_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local hw = size * 0.36
    local hh = size * 0.28
    local t = math.max(size * 0.06, 2)
    draw_rect(ass, cx - hw, cy - hh, cx + hw, cy - hh + t, color, alpha, master_alpha)
    draw_rect(ass, cx - hw, cy + hh - t, cx + hw, cy + hh, color, alpha, master_alpha)
    draw_rect(ass, cx - hw, cy - hh, cx - hw + t, cy + hh, color, alpha, master_alpha)
    draw_rect(ass, cx + hw - t, cy - hh, cx + hw, cy + hh, color, alpha, master_alpha)
    draw_rect(ass,
        cx + size * 0.02, cy + size * 0.01,
        cx + hw - t, cy + hh - t,
        color, "40", master_alpha)
end

-- Lucide-style overlapping tag outlines used by the WebUI marker editor.
local function draw_marker_tags_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local a = blend_alpha(alpha, master_alpha)
    local function tag(dx, dy, scale)
        local left = cx + dx - size * 0.34 * scale
        local top = cy + dy - size * 0.30 * scale
        local shoulder = cx + dx + size * 0.08 * scale
        local tip_x = cx + dx + size * 0.34 * scale
        local tip_y = cy + dy
        local bottom = cy + dy + size * 0.30 * scale
        ass:new_event()
        ass:pos(0, 0)
        ass:append(string.format(
            "{\\an7\\bord1.5\\shad0\\1a&HFF&\\3c&H%s&\\3a&H%s&\\p1}" ..
            "m %d %d l %d %d %d %d %d %d %d %d{\\p0}",
            color, a,
            math.floor(left), math.floor(top),
            math.floor(shoulder), math.floor(top),
            math.floor(tip_x), math.floor(tip_y),
            math.floor(shoulder), math.floor(bottom),
            math.floor(left), math.floor(bottom)
        ))
        draw_circle(ass, left + size * 0.12 * scale, cy + dy,
            math.max(1.2, size * 0.035), color, alpha, master_alpha)
    end
    tag(-size * 0.09, size * 0.08, 0.86)
    tag(size * 0.07, -size * 0.07, 0.86)
end

local chapter_thumbnail_hover_overlay_id = 63
local chapter_thumbnail_menu_overlay_first = 40
local chapter_thumbnail_menu_overlay_last = 51

local function remove_chapter_thumbnail_overlay(overlay_id)
    overlay_id = overlay_id or chapter_thumbnail_hover_overlay_id
    if not state.chapter_thumbnail_overlay_visible[overlay_id] then return end
    pcall(mp.command_native, { name = "overlay-remove", id = overlay_id })
    state.chapter_thumbnail_overlay_visible[overlay_id] = nil
    state.chapter_thumbnail_overlay_signature[overlay_id] = nil
end

local function remove_chapter_thumbnail_menu_overlays()
    for overlay_id = chapter_thumbnail_menu_overlay_first, chapter_thumbnail_menu_overlay_last do
        remove_chapter_thumbnail_overlay(overlay_id)
    end
end

local function request_chapter_thumbnail(chapter)
    local chapter_index = chapter and tonumber(chapter.index) or -1
    if chapter_index < 0 or not chapter.thumbnail_url or chapter.thumbnail_url == "" then return end
    if state.chapter_thumbnails[chapter_index] or state.chapter_thumbnail_requested[chapter_index] then return end
    state.chapter_thumbnail_requested[chapter_index] = true
    mp.commandv("script-message", "silo-chapter-thumbnail-request", tostring(chapter_index))
end

local function update_chapter_thumbnail_overlay(chapter, x, y, display_w, display_h, overlay_id)
    overlay_id = overlay_id or chapter_thumbnail_hover_overlay_id
    local chapter_index = chapter and tonumber(chapter.index) or -1
    local raw = state.chapter_thumbnails[chapter_index]
    if not raw or tonumber(raw.index) ~= chapter_index or not raw.file
        or tonumber(raw.width) <= 0 or tonumber(raw.height) <= 0
        or tonumber(raw.stride) <= 0 then
        remove_chapter_thumbnail_overlay(overlay_id)
        return false
    end

    local signature = table.concat({
        tostring(raw.index), tostring(math.floor(x)), tostring(math.floor(y)),
        tostring(math.floor(display_w)), tostring(math.floor(display_h)), tostring(raw.file)
    }, "|")
    if signature ~= state.chapter_thumbnail_overlay_signature[overlay_id] then
        local ok = pcall(mp.command_native, {
            name = "overlay-add",
            id = overlay_id,
            x = math.floor(x),
            y = math.floor(y),
            file = raw.file,
            offset = 0,
            fmt = "bgra",
            w = tonumber(raw.width),
            h = tonumber(raw.height),
            stride = tonumber(raw.stride),
            dw = math.floor(display_w),
            dh = math.floor(display_h),
        })
        if not ok then
            remove_chapter_thumbnail_overlay(overlay_id)
            return false
        end
        state.chapter_thumbnail_overlay_signature[overlay_id] = signature
    end
    state.chapter_thumbnail_overlay_visible[overlay_id] = true
    return true
end

--------------------------------------------------------------------------------
-- Render the OSC
--------------------------------------------------------------------------------

local function render_osc()
    if state.current_alpha <= 0.01 then
        remove_chapter_thumbnail_overlay()
        remove_chapter_thumbnail_menu_overlays()
        state.watch_party_actions = {}
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

    local sc = ui_scale()

    -- 1a. Soft top scrim and the WebUI's separate minimize / Exit controls.
    local top_gh = math.floor(config.top_gradient_height * sc)
    draw_gradient(ass, 0, 0, W, top_gh, "000000", "66", "FF", ma, 20)

    local bm = L.btn_minimize
    draw_circle(ass, bm.cx, bm.cy, bm.w / 2, "000000", "66", ma)
    draw_text(ass, bm.cx, bm.cy - math.floor(2 * sc), "⌄",
        math.floor(28 * sc), config.text_color, "00", ma, 5, "Segoe UI", false)

    local be = L.btn_exit
    draw_rounded_rect(ass, be.x, be.y, be.x + be.w, be.y + be.h,
        be.h / 2, "000000", "66", ma)
    draw_exit_icon(ass, be.x + math.floor(20 * sc), be.cy,
        math.floor(22 * sc), config.text_color, "00", ma)
    draw_text(ass, be.x + math.floor(36 * sc), be.cy, "Exit",
        math.floor(14 * sc), config.text_color, "00", ma, 4, nil, false)

    -- 1b. Transparent cinema HUD gradient. There is intentionally no solid
    -- bottom bar: the picture remains visible behind every control.
    draw_gradient(ass,
        L.gradient.x, L.gradient.y,
        L.gradient.x + L.gradient.w, L.gradient.y + L.gradient.h,
        "000000", "FF", config.bar_bg_alpha, ma, 32)

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
    local current_seek_h = math.max(2, math.floor(
        (is_seek_hover and config.seek_hover_height or config.seek_height) * sc))

    -- Seek track background
    draw_rounded_rect(ass,
        sb.x1, seek_draw_y - current_seek_h / 2,
        sb.x2, seek_draw_y + current_seek_h / 2,
        current_seek_h / 2,
        config.seek_bg_color, "D9", ma)

    -- Buffered range
    local cache_state = state.demuxer_cache
    if cache_state and state.duration > 0 then
        local ranges = cache_state["seekable-ranges"]
        if ranges then
            for _, range in ipairs(ranges) do
                local rs = clamp((range["start"] + state.timeline_offset) / state.duration, 0, 1)
                local re = clamp((range["end"] + state.timeline_offset) / state.duration, 0, 1)
                local bx1 = sb.x1 + (sb.x2 - sb.x1) * rs
                local bx2 = sb.x1 + (sb.x2 - sb.x1) * re
                if bx2 > bx1 + 1 then
                    draw_rounded_rect(ass,
                        bx1, seek_draw_y - current_seek_h / 2,
                        bx2, seek_draw_y + current_seek_h / 2,
                        current_seek_h / 2,
                        config.seek_buffered_color, "B3", ma)
                end
            end
        end
    end

    -- Marker regions sit below the played fill, matching the WebUI seek rail.
    -- Hovering a region brightens it and adds a fine white outline.
    local hover_time = nil
    local hover_marker = nil
    if is_seek_hover and state.duration > 0 then
        local hover_ratio = clamp((state.mouse_x - sb.x1) / (sb.x2 - sb.x1), 0, 1)
        hover_time = hover_ratio * state.duration
        hover_marker = marker_at_time(hover_time)
    end
    local function draw_marker_region(start_seconds, end_seconds, color, alpha, is_hovered)
        if state.duration <= 0 or not start_seconds or not end_seconds
            or end_seconds <= start_seconds then return end
        local rx1 = sb.x1 + (sb.x2 - sb.x1) * clamp(start_seconds / state.duration, 0, 1)
        local rx2 = sb.x1 + (sb.x2 - sb.x1) * clamp(end_seconds / state.duration, 0, 1)
        if rx2 > rx1 + 1 then
            local region_h = is_hovered and math.max(current_seek_h, math.floor(8 * sc)) or current_seek_h
            if is_hovered then
                draw_rounded_rect(ass, rx1 - 1, seek_draw_y - region_h / 2 - 1,
                    rx2 + 1, seek_draw_y + region_h / 2 + 1, region_h / 2 + 1,
                    config.text_color, "70", ma)
            end
            draw_rounded_rect(ass, rx1, seek_draw_y - region_h / 2,
                rx2, seek_draw_y + region_h / 2, region_h / 2,
                color, is_hovered and "48" or alpha, ma)
        end
    end
    for _, marker in ipairs(marker_regions) do
        local marker_start = tonumber(state[marker.key .. "_start"]) or 0
        local marker_end = tonumber(state[marker.key .. "_end"]) or 0
        draw_marker_region(marker_start, marker_end, marker.color, "99",
            hover_marker and hover_marker.label == marker.label)
    end

    -- Marker editor handles mirror SeekBar.tsx. The visible grip is narrow,
    -- while its cached hit target remains comfortably pointer-friendly.
    state.marker_editor_handle_rects = {}
    if state.marker_editor_active and state.duration > 0
        and state.marker_editor_start >= 0
        and state.marker_editor_end > state.marker_editor_start then
        local editor_start_x = sb.x1 + (sb.x2 - sb.x1)
            * clamp(state.marker_editor_start / state.duration, 0, 1)
        local editor_end_x = sb.x1 + (sb.x2 - sb.x1)
            * clamp(state.marker_editor_end / state.duration, 0, 1)
        local grip_h = math.floor(16 * sc)
        local grip_w = math.max(5, math.floor(6 * sc))
        local hit_w = math.max(20, math.floor(20 * sc))
        local function draw_editor_handle(edge, x)
            draw_rounded_rect(ass, x - grip_w / 2, seek_draw_y - grip_h / 2,
                x + grip_w / 2, seek_draw_y + grip_h / 2,
                grip_w / 2, config.text_color, "00", ma)
            state.marker_editor_handle_rects[edge] = {
                x = x - hit_w / 2, y = seek_draw_y - math.floor(14 * sc),
                w = hit_w, h = math.floor(28 * sc),
            }
        end
        draw_editor_handle("start", editor_start_x)
        draw_editor_handle("end", editor_end_x)

        if state.dragging_marker_edge then
            local seconds = state.dragging_marker_edge == "start"
                and state.marker_editor_start or state.marker_editor_end
            local x = state.dragging_marker_edge == "start" and editor_start_x or editor_end_x
            local tooltip_w = math.floor(92 * sc)
            local tooltip_h = math.floor(42 * sc)
            local tooltip_x = clamp(x, sb.x1 + tooltip_w / 2, sb.x2 - tooltip_w / 2)
            local tooltip_bottom = seek_draw_y - math.floor(12 * sc)
            draw_rounded_rect(ass, tooltip_x - tooltip_w / 2,
                tooltip_bottom - tooltip_h, tooltip_x + tooltip_w / 2,
                tooltip_bottom, math.floor(8 * sc), "171717", "0D", ma)
            draw_text(ass, tooltip_x, tooltip_bottom - math.floor(27 * sc),
                capitalize(state.marker_editor_kind) .. " " .. state.dragging_marker_edge,
                math.floor(10 * sc), config.text_color, "73", ma, 5, nil, true)
            draw_text(ass, tooltip_x, tooltip_bottom - math.floor(12 * sc),
                format_time(seconds), math.floor(12 * sc), config.text_color,
                "00", ma, 5, "Consolas", true)
        end
    end

    -- Chapter boundaries are one-pixel, ten-pixel-high ticks in the WebUI.
    if state.duration > 0 then
        for _, chapter in ipairs(state.chapters or {}) do
            local chapter_start = tonumber(chapter.start_seconds) or 0
            if chapter_start > 0 and chapter_start < state.duration then
                local chapter_x = sb.x1 + (sb.x2 - sb.x1) * (chapter_start / state.duration)
                draw_rect(ass, chapter_x - 0.5, seek_draw_y - math.floor(5 * sc),
                    chapter_x + 0.5, seek_draw_y + math.floor(5 * sc),
                    config.text_color, "73", ma)
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
            config.text_color, "00", ma)
    end

    -- The WebUI thumb stays hidden until hover/drag.
    if is_seek_hover or state.dragging_seek then
        draw_circle(ass, progress_x, seek_draw_y,
            config.seek_thumb_radius * sc, config.text_color, "00", ma)
    end

    -- Seek hover tooltip
    if is_seek_hover and not state.dragging_seek and state.duration > 0 then
        local hover_chapter = chapter_at_time(hover_time)
        local rich = hover_chapter ~= nil or hover_marker ~= nil
        local tooltip_w = rich and math.floor(176 * sc) or math.floor(60 * sc)
        local tooltip_x = clamp(state.mouse_x, sb.x1 + tooltip_w / 2, sb.x2 - tooltip_w / 2)
        local tooltip_bottom = seek_draw_y - math.floor(12 * sc)
        local content_h = rich and math.floor(56 * sc) or math.floor(28 * sc)
        local image_h = hover_chapter and math.floor(99 * sc) or 0
        local tooltip_top = tooltip_bottom - content_h - image_h

        draw_rounded_rect(ass,
            tooltip_x - tooltip_w / 2, tooltip_top,
            tooltip_x + tooltip_w / 2, tooltip_bottom,
            math.floor(8 * sc), "171717", "0D", ma)

        if hover_chapter then
            request_chapter_thumbnail(hover_chapter)

            local image_x = tooltip_x - tooltip_w / 2
            if not update_chapter_thumbnail_overlay(
                hover_chapter, image_x, tooltip_top, tooltip_w, image_h) then
                -- Same 16:9 placeholder used by the WebUI while no generated
                -- chapter thumbnail exists or while the native bitmap loads.
                draw_rect(ass, image_x, tooltip_top,
                    image_x + tooltip_w, tooltip_top + image_h,
                    "242424", "08", ma)
                draw_text(ass, tooltip_x, tooltip_top + image_h / 2,
                    "CHAPTER", math.floor(10 * sc), config.text_color, "D6", ma,
                    5, nil, true)
            end
        else
            remove_chapter_thumbnail_overlay()
        end

        local text_left = tooltip_x - tooltip_w / 2 + math.floor(10 * sc)
        local cursor_y = tooltip_top + image_h + math.floor(13 * sc)
        if hover_marker then
            draw_circle(ass, text_left + math.floor(3 * sc), cursor_y,
                math.max(2, math.floor(3 * sc)), hover_marker.color, "00", ma)
            draw_text(ass, text_left + math.floor(11 * sc), cursor_y,
                ass_escape_text(hover_marker.label), math.floor(11 * sc),
                config.text_color, "00", ma, 4, nil, true)
            draw_text(ass, tooltip_x + tooltip_w / 2 - math.floor(10 * sc), cursor_y,
                format_time(hover_marker.start) .. "-" .. format_time(hover_marker.finish),
                math.floor(10 * sc), config.text_color, "73", ma, 6, "Consolas", false)
            cursor_y = cursor_y + math.floor(16 * sc)
        end

        draw_text(ass, text_left, cursor_y, format_time(hover_time),
            math.floor(12 * sc), config.text_color, "00", ma, 4, "Consolas", true)
        if hover_chapter then
            draw_text(ass, text_left, cursor_y + math.floor(16 * sc),
                ass_escape_text(hover_chapter.title or ("Chapter " .. tostring(hover_chapter.index or ""))),
                math.floor(11 * sc), config.text_color, "73", ma, 4, nil, false)
        end

        -- Small downward caret centered over the seek rail.
        local caret = math.floor(6 * sc)
        local a = blend_alpha("0D", ma)
        ass:new_event()
        ass:pos(0, 0)
        ass:append(string.format(
            "{\\an7\\bord0\\shad0%s%s\\p1}m %d %d l %d %d %d %d{\\p0}",
            ass_color("171717"), ass_alpha(a),
            math.floor(tooltip_x - caret), math.floor(tooltip_bottom),
            math.floor(tooltip_x + caret), math.floor(tooltip_bottom),
            math.floor(tooltip_x), math.floor(tooltip_bottom + caret)))
    else
        remove_chapter_thumbnail_overlay()
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

    -- 4. Bottom-left title, episode label, and mono timecode.
    local md = L.metadata
    if state.content_title ~= "" then
        draw_text(ass, md.x, md.y - math.floor(9 * sc), state.content_title,
            math.floor(16 * sc), config.text_color, "00", ma, 4, nil, true)
    end
    local meta_y = md.y + math.floor(14 * sc)
    if state.content_subtitle ~= "" then
        draw_text(ass, md.x, meta_y, state.content_subtitle,
            math.floor(10 * sc), config.dim_text_color, "30", ma, 4, nil, false)
    end
    local time_x = md.x
    if state.content_subtitle ~= "" then
        time_x = md.x + math.min(md.max_w * 0.55, math.floor(220 * sc))
    end
    local time_str = format_time(state.time_pos) .. "  /  " .. format_time(state.duration)
    draw_text(ass, time_x, meta_y, time_str,
        math.floor(config.font_size_time * sc), config.text_color, "40", ma, 4, "Consolas", false)

    -- 5. Center transport cluster: glass secondaries around a glossy white disc.
    local function draw_secondary_disc(rect)
        draw_circle(ass, rect.cx, rect.cy, rect.w / 2, config.text_color, "E6", ma)
        draw_circle(ass, rect.cx, rect.cy, rect.w / 2 - math.max(1, sc), config.text_color, "EF", ma)
    end

    local bsb = L.btn_skip_back
    draw_secondary_disc(bsb)
    draw_skip_back_icon(ass, bsb.cx, bsb.cy, bsb.w * 0.7, config.text_color, "10", ma)

    if L.btn_prev_ep then
        draw_secondary_disc(L.btn_prev_ep)
        draw_prev_episode_icon(ass, L.btn_prev_ep.cx, L.btn_prev_ep.cy,
            L.btn_prev_ep.w * 0.58, config.text_color, "10", ma)
    end

    local bp = L.btn_play
    draw_circle(ass, bp.cx, bp.cy, bp.w / 2, config.text_color, "00", ma)
    if state.pause then
        draw_play_icon(ass, bp.cx, bp.cy, bp.w * 0.48, "0B0B0A", "00", ma)
    else
        draw_pause_icon(ass, bp.cx, bp.cy, bp.w * 0.48, "0B0B0A", "00", ma)
    end

    local bsf = L.btn_skip_fwd
    draw_secondary_disc(bsf)
    draw_skip_fwd_icon(ass, bsf.cx, bsf.cy, bsf.w * 0.7, config.text_color, "10", ma)

    if L.btn_next_ep then
        draw_secondary_disc(L.btn_next_ep)
        draw_next_episode_icon(ass, L.btn_next_ep.cx, L.btn_next_ep.cy,
            L.btn_next_ep.w * 0.58, config.text_color, "10", ma)
    end

    -- 6. Utility rail. Hover produces the same faint circular wash as the
    -- WebUI; active secondary panels get an amber status dot.
    local function utility_hover(rect)
        return state.mouse_x >= rect.x and state.mouse_x <= rect.x + rect.w
           and state.mouse_y >= rect.y and state.mouse_y <= rect.y + rect.h
    end
    local function draw_utility_state(rect, active)
        if utility_hover(rect) then
            draw_circle(ass, rect.cx, rect.cy, rect.w / 2, config.text_color, "EB", ma)
        end
        if active then
            draw_circle(ass, rect.cx, rect.y + rect.h - math.floor(4 * sc),
                math.max(2, math.floor(2.2 * sc)), config.accent_color, "00", ma)
        end
    end

    local bv = L.btn_volume
    draw_utility_state(bv, state.mute)
    draw_volume_icon(ass, bv.cx, bv.cy, bv.w * 0.72,
        config.text_color, "38", ma, state.mute and 0 or state.volume, state.mute)

    local vb = L.volume_bar
    local vol_ratio = state.dragging_volume
        and clamp(state.volume_drag_val / 100, 0, 1)
        or clamp(state.volume / 100, 0, 1)
    local volume_hover = state.dragging_volume
        or (state.mouse_x >= vb.x and state.mouse_x <= vb.x + vb.w
            and state.mouse_y >= vb.cy - math.floor(12 * sc)
            and state.mouse_y <= vb.cy + math.floor(12 * sc))
    local volume_h = math.max(3, math.floor((volume_hover and 5 or config.volume_bar_height) * sc))
    draw_rounded_rect(ass, vb.x, vb.cy - volume_h / 2, vb.x + vb.w, vb.cy + volume_h / 2,
        volume_h / 2, config.text_color, "D9", ma)
    local vol_fill_x = vb.x + vb.w * vol_ratio
    if vol_fill_x > vb.x + 1 then
        draw_rounded_rect(ass, vb.x, vb.cy - volume_h / 2, vol_fill_x, vb.cy + volume_h / 2,
            volume_h / 2, config.text_color, "00", ma)
    end
    if volume_hover then
        draw_circle(ass, vol_fill_x, vb.cy, math.floor(config.volume_thumb_radius * sc),
            config.text_color, "00", ma)
    end

    -- The WebUI separates the always-visible volume group from the rest of
    -- the utility rail with a subtle vertical divider.
    if L.utility_divider_x then
        local divider_x = L.utility_divider_x
        local divider_half_h = math.floor(16 * sc)
        draw_gradient(ass, divider_x, controls_y - divider_half_h,
            divider_x + math.max(1, math.floor(sc)), controls_y,
            config.text_color, "FF", "DB", ma, 4)
        draw_gradient(ass, divider_x, controls_y,
            divider_x + math.max(1, math.floor(sc)), controls_y + divider_half_h,
            config.text_color, "DB", "FF", ma, 4)
    end

    local bcc = L.btn_cc
    local cc_active = state.sub_track > 0 or state.subtitle_menu_visible
    draw_utility_state(bcc, cc_active)
    draw_text(ass, bcc.cx, bcc.cy, "CC", math.floor(14 * sc),
        config.text_color, cc_active and "00" or "38", ma, 5, nil, true)

    local bchap = L.btn_chapters
    if bchap then
        draw_utility_state(bchap, state.chapter_menu_visible)
        draw_text(ass, bchap.cx, bchap.cy, "☷", math.floor(20 * sc),
            config.text_color, "38", ma, 5, "Segoe UI Symbol", false)
    end

    local baudio = L.btn_audio
    if baudio then
        draw_utility_state(baudio, state.audio_menu_visible)
        draw_text(ass, baudio.cx, baudio.cy, "≋", math.floor(22 * sc),
            config.text_color, #state.audio_tracks > 1 and "38" or "A0", ma, 5, "Segoe UI Symbol", true)
    end

    local bq = L.btn_quality
    if utility_hover(bq) then
        draw_rounded_rect(ass, bq.x, bq.y, bq.x + bq.w, bq.y + bq.h,
            bq.h / 2, config.text_color, "EB", ma)
    end
    if state.quality_menu_visible then
        draw_circle(ass, bq.cx, bq.y + bq.h - math.floor(4 * sc),
            math.max(2, math.floor(2.2 * sc)), config.accent_color, "00", ma)
    end
    if bq.show_label then
        draw_text(ass, bq.x + math.floor(21 * sc), bq.cy, "⚙", math.floor(18 * sc),
            config.text_color, state.quality_menu_visible and "00" or "38", ma, 5, "Segoe UI Symbol", false)
        draw_text(ass, bq.x + math.floor(41 * sc), bq.cy, active_quality_label(), math.floor(11 * sc),
            config.text_color, state.quality_menu_visible and "00" or "38", ma, 4, nil, true)
    else
        draw_text(ass, bq.cx, bq.cy, "⚙", math.floor(18 * sc),
            config.text_color, state.quality_menu_visible and "00" or "38", ma, 5, "Segoe UI Symbol", false)
    end

    if L.btn_marker_edit then
        draw_utility_state(L.btn_marker_edit, state.marker_editor_visible)
        draw_marker_tags_icon(ass, L.btn_marker_edit.cx, L.btn_marker_edit.cy,
            L.btn_marker_edit.w * 0.68, config.text_color, "38", ma)
    end

    local bst = L.btn_stats
    draw_utility_state(bst, state.stats_visible)
    draw_text(ass, bst.cx, bst.cy, "ⓘ", math.floor(20 * sc),
        config.text_color, state.stats_visible and "00" or "38", ma, 5, "Segoe UI Symbol", false)

    local bpip = L.btn_pip
    draw_utility_state(bpip, state.picture_in_picture)
    draw_pip_icon(ass, bpip.cx, bpip.cy, bpip.w * 0.66,
        config.text_color, "20", ma)

    local bf = L.btn_fullscreen
    draw_utility_state(bf, state.fullscreen)
    draw_fullscreen_icon(ass, bf.cx, bf.cy, bf.w * 0.68,
        config.text_color, "20", ma, state.fullscreen)

    render_marker_editor_panel(ass, W, H, ma, sc)

    -- Watch Party panel mirrors WatchTogetherPanel.tsx: top-right glass card,
    -- live connection status, room code/viewer count, policy, and host actions.
    state.watch_party_actions = {}
    local party = state.watch_party
    if party and party.visible then
        local panel_w = math.floor(224 * sc)
        local panel_h = math.floor((party.is_host and 132 or 88) * sc)
        local panel_x = W - math.floor(16 * sc) - panel_w
        local panel_y = math.floor(16 * sc)
        draw_rounded_rect(ass, panel_x - 1, panel_y - 1,
            panel_x + panel_w + 1, panel_y + panel_h + 1,
            math.floor(12 * sc), config.text_color, "E8", ma)
        draw_rounded_rect(ass, panel_x, panel_y,
            panel_x + panel_w, panel_y + panel_h,
            math.floor(12 * sc), "000000", "33", ma)

        local connection = tostring(party.connection_state or "disconnected")
        local connection_label = connection == "connected" and "Connected"
            or connection == "connecting" and "Connecting"
            or connection == "reconnecting" and "Reconnecting"
            or "Disconnected"
        local dot_color = connection == "connected" and "55C878"
            or (connection == "connecting" or connection == "reconnecting") and "3FC5F0"
            or "8A8A8A"
        draw_text(ass, panel_x + math.floor(12 * sc), panel_y + math.floor(17 * sc),
            "WATCH PARTY", math.floor(10 * sc), config.text_color, "66", ma, 4, nil, true)
        draw_circle(ass, panel_x + math.floor(105 * sc), panel_y + math.floor(17 * sc),
            math.max(2, math.floor(3 * sc)), dot_color, "00", ma)
        draw_text(ass, panel_x + math.floor(113 * sc), panel_y + math.floor(17 * sc),
            connection_label, math.floor(10 * sc), config.text_color, "66", ma, 4, nil, false)

        draw_text(ass, panel_x + math.floor(12 * sc), panel_y + math.floor(43 * sc),
            ass_escape_text(party.code or "..."), math.floor(18 * sc),
            config.text_color, "00", ma, 4, "Consolas", true)
        local viewers = tonumber(party.member_count) or 0
        draw_text(ass, panel_x + math.floor(92 * sc), panel_y + math.floor(43 * sc),
            tostring(viewers) .. (viewers == 1 and " viewer" or " viewers"),
            math.floor(11 * sc), config.text_color, "80", ma, 4, nil, false)

        local policy_label
        if party.is_host then
            policy_label = party.guest_control_policy == "guest_play_pause"
                and "Guests can pause & resume" or "Only you control playback"
        else
            policy_label = party.can_control_transport
                and "You can pause & resume" or "Host controls playback"
        end
        draw_text(ass, panel_x + math.floor(12 * sc), panel_y + math.floor(64 * sc),
            policy_label, math.floor(11 * sc), config.text_color, "80", ma, 4, nil, false)

        if party.is_host then
            draw_rect(ass, panel_x + math.floor(12 * sc), panel_y + math.floor(82 * sc),
                panel_x + panel_w - math.floor(12 * sc), panel_y + math.floor(83 * sc),
                config.text_color, "EB", ma)
            local function party_button(action, label, x, width, danger)
                local y = panel_y + math.floor(94 * sc)
                local h = math.floor(25 * sc)
                draw_rounded_rect(ass, x, y, x + width, y + h, math.floor(6 * sc),
                    danger and "3434EF" or config.text_color,
                    danger and "B8" or "DE", ma)
                draw_text(ass, x + width / 2, y + h / 2, label,
                    math.floor(11 * sc), danger and "D8D8FF" or config.text_color,
                    danger and "10" or "28", ma, 5, nil, true)
                table.insert(state.watch_party_actions,
                    { x = x, y = y, w = width, h = h, action = action })
            end
            local invite_x = panel_x + math.floor(12 * sc)
            party_button("invite", "Invite", invite_x, math.floor(50 * sc), false)
            party_button("toggle-policy",
                party.guest_control_policy == "guest_play_pause" and "Host Only" or "Allow Pause",
                invite_x + math.floor(56 * sc), math.floor(82 * sc), false)
            party_button("end", "End", panel_x + panel_w - math.floor(52 * sc),
                math.floor(40 * sc), true)
        end

        if party.playback_state == "waiting" then
            local sync_w = math.floor(180 * sc)
            local sync_h = math.floor(68 * sc)
            local sync_x = W / 2 - sync_w / 2
            local sync_y = H / 2 - sync_h / 2
            draw_rounded_rect(ass, sync_x - 1, sync_y - 1,
                sync_x + sync_w + 1, sync_y + sync_h + 1,
                math.floor(8 * sc), config.text_color, "D9", ma)
            draw_rounded_rect(ass, sync_x, sync_y,
                sync_x + sync_w, sync_y + sync_h,
                math.floor(8 * sc), "000000", "4C", ma)
            draw_text(ass, W / 2, H / 2 - math.floor(9 * sc), "SYNCING",
                math.floor(10 * sc), config.text_color, "68", ma, 5, nil, true)
            draw_text(ass, W / 2, H / 2 + math.floor(13 * sc), "Syncing playback",
                math.floor(14 * sc), config.text_color, "00", ma, 5, nil, true)
        end

        if party.is_host and state.watch_party_end_confirm then
            local confirm_w = math.floor(330 * sc)
            local confirm_h = math.floor(142 * sc)
            local confirm_x = W / 2 - confirm_w / 2
            local confirm_y = H / 2 - confirm_h / 2
            draw_rounded_rect(ass, confirm_x - 1, confirm_y - 1,
                confirm_x + confirm_w + 1, confirm_y + confirm_h + 1,
                math.floor(10 * sc), config.text_color, "D8", ma)
            draw_rounded_rect(ass, confirm_x, confirm_y,
                confirm_x + confirm_w, confirm_y + confirm_h,
                math.floor(10 * sc), "101010", "0C", ma)
            draw_text(ass, confirm_x + math.floor(18 * sc), confirm_y + math.floor(28 * sc),
                "End watch party?", math.floor(17 * sc), config.text_color, "00", ma, 4, nil, true)
            draw_text(ass, confirm_x + math.floor(18 * sc), confirm_y + math.floor(57 * sc),
                "This disconnects everyone in the room.", math.floor(12 * sc),
                config.text_color, "72", ma, 4, nil, false)
            local button_y = confirm_y + math.floor(92 * sc)
            local button_h = math.floor(32 * sc)
            local cancel_x = confirm_x + confirm_w - math.floor(174 * sc)
            local end_x = confirm_x + confirm_w - math.floor(92 * sc)
            draw_rounded_rect(ass, cancel_x, button_y, cancel_x + math.floor(72 * sc), button_y + button_h,
                math.floor(6 * sc), config.text_color, "DE", ma)
            draw_text(ass, cancel_x + math.floor(36 * sc), button_y + button_h / 2, "Cancel",
                math.floor(12 * sc), config.text_color, "28", ma, 5, nil, true)
            draw_rounded_rect(ass, end_x, button_y, end_x + math.floor(74 * sc), button_y + button_h,
                math.floor(6 * sc), "3434EF", "86", ma)
            draw_text(ass, end_x + math.floor(37 * sc), button_y + button_h / 2, "End Party",
                math.floor(12 * sc), "D8D8FF", "08", ma, 5, nil, true)
            table.insert(state.watch_party_actions,
                { x = cancel_x, y = button_y, w = math.floor(72 * sc), h = button_h, action = "cancel-end" })
            table.insert(state.watch_party_actions,
                { x = end_x, y = button_y, w = math.floor(74 * sc), h = button_h, action = "confirm-end" })
        end
    end

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

    local sc = ui_scale()
    local fs = math.floor(14 * sc)
    local fs_small = math.max(math.floor(12 * sc), 10)
    local padding = math.floor(16 * sc)
    local line_h = math.floor(20 * sc)
    local section_gap = 12
    local header_h = line_h + 4
    local box_w = math.floor(320 * sc)
    local box_x = math.floor(16 * sc)
    local box_y = math.floor(48 * sc)

    local fallback = "\xe2\x80\x94"
    local function shown(value)
        if value == nil or tostring(value) == "" then return fallback end
        return tostring(value)
    end

    -- Build all sections
    local sections = {}

    -- Section 1: Player
    local s1 = { header = "PLAYER", rows = {} }
    table.insert(s1.rows, { label = "Player", value = "libmpv (GPU)" })
    table.insert(s1.rows, { label = "Play method", value = shown(state.play_method_str) })
    table.insert(s1.rows, { label = "Protocol", value = shown(state.protocol_str) })
    table.insert(s1.rows, { label = "Stream type", value = shown(state.stream_type_str) })
    if state.media_info and state.media_info.requested_source and state.media_info.requested_source ~= "" then
        table.insert(s1.rows, { label = "Auto-switched from", value = state.media_info.requested_source })
    end
    table.insert(sections, s1)

    -- Section 2: Video Info (live)
    local s2 = { header = "VIDEO INFO", rows = {} }
    table.insert(s2.rows, { label = "Player dimensions", value = string.format("%dx%d", W, H) })
    local vw = mp.get_property_number("video-params/w")
    local vh = mp.get_property_number("video-params/h")
    table.insert(s2.rows, { label = "Video resolution", value =
        vw and vh and vw > 0 and string.format("%dx%d", vw, vh) or fallback })
    local dropped = (mp.get_property_number("frame-drop-count") or 0)
                  + (mp.get_property_number("decoder-frame-drop-count") or 0)
    table.insert(s2.rows, { label = "Dropped frames", value = tostring(dropped) })
    table.insert(s2.rows, { label = "Corrupted frames", value = "0" })
    table.insert(sections, s2)

    -- Section 3: Playback Stream Info
    local s3 = { header = "PLAYBACK STREAM INFO", rows = {} }
    table.insert(s3.rows, { label = "Video codec", value = shown(state.stream_codec_video) })
    table.insert(s3.rows, { label = "Audio codec", value = shown(state.stream_codec_audio) })
    table.insert(sections, s3)

    -- Section 4: current source file, matching playback-info.ts.
    local s4 = { header = "CURRENT SOURCE FILE", rows = {} }
    local mi = state.media_info
    if mi then
        table.insert(s4.rows, { label = "Container", value = shown(mi.container) })
        table.insert(s4.rows, { label = "Size", value = mi.file_size and mi.file_size > 0 and
            format_file_size(mi.file_size) or fallback })
        table.insert(s4.rows, { label = "Bitrate", value = mi.bitrate and mi.bitrate > 0 and
            format_bitrate(mi.bitrate * 1000) or fallback })
        local video_codec = mi.codec_video and string.upper(mi.codec_video) or ""
        if video_codec ~= "" and mi.video_profile and mi.video_profile ~= "" then
            video_codec = video_codec .. " " .. mi.video_profile
        end
        table.insert(s4.rows, { label = "Video codec", value = shown(video_codec) })
        table.insert(s4.rows, { label = "Video bitrate", value = mi.video_bitrate and mi.video_bitrate > 0 and
            format_bitrate(mi.video_bitrate) or fallback })
        table.insert(s4.rows, { label = "Video range type", value = shown(mi.video_range) })
        local audio_codec = mi.audio_title and mi.audio_title ~= "" and mi.audio_title or
            (mi.codec_audio and string.upper(mi.codec_audio) or "")
        table.insert(s4.rows, { label = "Audio codec", value = shown(audio_codec) })
        table.insert(s4.rows, { label = "Audio bitrate", value = mi.audio_bitrate and mi.audio_bitrate > 0 and
            format_bitrate(mi.audio_bitrate) or fallback })
        table.insert(s4.rows, { label = "Audio channels", value = mi.audio_channels and mi.audio_channels > 0 and
            tostring(mi.audio_channels) or fallback })
        local sample_rate = fallback
        if mi.audio_sample_rate and mi.audio_sample_rate > 0 then
            sample_rate = string.format(mi.audio_sample_rate % 1000 == 0 and "%.0f kHz" or "%.1f kHz",
                mi.audio_sample_rate / 1000)
        end
        table.insert(s4.rows, { label = "Audio sample rate", value = sample_rate })
    else
        for _, label in ipairs({ "Container", "Size", "Bitrate", "Video codec", "Video bitrate",
            "Video range type", "Audio codec", "Audio bitrate", "Audio channels", "Audio sample rate" }) do
            table.insert(s4.rows, { label = label, value = fallback })
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
        "000000", "26", 1.0)

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

show_osc = function()
    state.visible = true
    state.target_alpha = 1
    -- Reset hide timer
    if state.hide_timer then
        state.hide_timer:kill()
    end
    -- The timer re-arms itself while hide is blocked (mouse hovering the bar,
    -- dragging, or paused). Without re-arming, a blocked hide means the OSC
    -- can be stuck visible forever when stale state (e.g. mouse_in_bar) is
    -- left over after the cursor has actually left the window.
    local function check_hide()
        if state.pause or state.dragging_seek or state.dragging_volume
            or state.dragging_marker_edge or state.marker_editor_visible then
            state.hide_timer = mp.add_timeout(config.hide_timeout, check_hide)
            return
        end
        if state.mouse_in_bar and state.mouse_in_window then
            state.hide_timer = mp.add_timeout(config.hide_timeout, check_hide)
            return
        end
        hide_osc()
    end
    state.hide_timer = mp.add_timeout(config.hide_timeout, check_hide)
end

hide_osc = function()
    if state.dragging_seek or state.dragging_volume or state.dragging_marker_edge then return end
    if state.marker_editor_visible then return end
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
    -- Tell the host app whether to show the cursor (only on change). The
    -- "silo-" prefix is required: MpvPlayer.HandleClientMessage filters
    -- out everything else as host→Lua echo-backs.
    if visible == last_cursor_visible then return end
    last_cursor_visible = visible
    if visible then
        mp.commandv("script-message", "silo-cursor-visible")
    else
        mp.commandv("script-message", "silo-cursor-hidden")
    end
end

-- Forward declarations for functions defined after tick()
local check_skip_markers
local render_skip_button
local check_next_episode_button
local render_next_episode_button
local check_next_episode_countdown
local render_next_episode_countdown
local render_translation_buffering
local render_pause_indicator

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

    -- Refresh stats overlay ~1/sec (live bitrate, dropped frames, etc.)
    if state.stats_visible then
        state.stats_last_refresh = state.stats_last_refresh or 0
        if now - state.stats_last_refresh >= 1.0 then
            state.stats_last_refresh = now
            render_stats()
        end
    end

    -- Check skip markers (intro/credits)
    check_skip_markers()

    -- Check whether to show the Next Episode button (last 5% or credits range)
    check_next_episode_countdown()
    check_next_episode_button()

    if state.translation_buffering then
        local frame = math.floor(now * 8) % 4
        if frame ~= state.translation_spinner_frame then
            state.translation_spinner_frame = frame
            render_translation_buffering()
        end
    end

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

    update_marker_panel_drag(mx, my)

    -- Marker edge dragging is isolated from normal seeking, matching the
    -- WebUI's dedicated pointer loop.
    if state.dragging_marker_edge then
        update_marker_edge_drag(mx, L.seek_bar)
    end

    -- Handle seek drag
    if state.dragging_seek then
        local sb = L.seek_bar
        if sb then
            local ratio = clamp((mx - sb.x1) / (sb.x2 - sb.x1), 0, 1)
            state.seek_drag_pos = ratio
        end
    end

    -- Handle volume drag
    if state.dragging_volume then
        local vb = L.volume_bar
        if vb then
            local ratio = clamp((mx - vb.x) / vb.w, 0, 1)
            state.volume_drag_val = ratio * 100
            mp.commandv("set", "volume", tostring(state.volume_drag_val))
        end
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

    local sc = ui_scale()
    local fs = math.floor(config.stats_font_size * sc)
    local fs_small = math.max(math.floor((config.stats_font_size - 2) * sc), 10)
    local padding = math.floor(config.stats_padding * sc)
    local item_h = math.floor((config.stats_line_height + 4) * sc)
    local track_item_h = math.floor((config.stats_line_height + 16) * sc)
    local menu_w = math.floor(340 * sc)

    -- Sort tracks by source priority
    local sorted = {}
    for _, t in ipairs(state.subtitle_tracks) do
        table.insert(sorted, t)
    end
    table.sort(sorted, function(a, b)
        return source_priority(a.source or "embedded") < source_priority(b.source or "embedded")
    end)

    -- Keep the popup on-screen even when a file exposes dozens of embedded
    -- subtitle tracks. The wheel moves the bounded track window while the
    -- fixed actions remain reachable at the bottom.
    local action_count = state.subtitle_ai_available and 4 or 3
    local fixed_rows = 1 + action_count -- Off + delay/search/appearance/optional AI
    local max_visible_tracks = math.max(1,
        math.floor((H - math.floor(config.hud_height * sc) - 40 * sc
            - padding * 2 - fixed_rows * item_h - 20) / track_item_h))
    max_visible_tracks = math.min(max_visible_tracks, 8)
    local max_first = math.max(1, #sorted - max_visible_tracks + 1)
    local first = clamp(state.subtitle_menu_offset or 1, 1, max_first)
    local last = math.min(#sorted, first + max_visible_tracks - 1)
    state.subtitle_menu_offset = first
    local visible_tracks = math.max(0, last - first + 1)
    local menu_h = padding * 2 + item_h + 4 + (visible_tracks * track_item_h)
        + 8 + (action_count * item_h) + 8

    -- Position: above the CC button (bottom-right area)
    compute_layout()
    local L = state.layout
    local menu_x = W - padding - menu_w - 40
    local menu_y = H - math.floor(config.hud_height * sc) - menu_h - math.floor(10 * sc)
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

    -- "Off" option
    local off_active = (state.active_subtitle < 0)
    local off_color = off_active and config.text_color or config.dim_text_color
    if state.keyboard_menu_kind == "subtitles" and state.keyboard_menu_index == 1 then
        draw_rounded_rect(ass, menu_x + 2, cy, menu_x + menu_w - 2, cy + item_h,
            4, config.text_color, "E6", 1.0)
    end
    if off_active then
        draw_text(ass, menu_x + padding, cy + item_h / 2, "✓",
            fs, config.text_color, "00", 1.0, 4)
    end
    draw_text(ass, menu_x + padding + math.floor(24 * sc), cy + item_h / 2, "Off",
        fs, off_color, "00", 1.0, 4)
    table.insert(state.subtitle_menu_items, {
        x = menu_x, y = cy, w = menu_w, h = item_h, action = "off"
    })
    cy = cy + item_h + 4  -- divider space

    -- Track items
    for i = first, last do
        local track = sorted[i]
        local keyboard_index = i - first + 2
        local is_active = (track.index == state.active_subtitle)
        local text_color = is_active and config.text_color or config.dim_text_color
        if state.keyboard_menu_kind == "subtitles"
            and state.keyboard_menu_index == keyboard_index then
            draw_rounded_rect(ass, menu_x + 2, cy, menu_x + menu_w - 2,
                cy + track_item_h, 4, config.text_color, "E6", 1.0)
        end

        -- Checkmark
        if is_active then
            draw_text(ass, menu_x + padding, cy + item_h / 2, "✓",
                fs, config.text_color, "00", 1.0, 4)
        end

        -- Language name and WebUI-style codec/source badges.
        local display = lang_name(track.language or "")
        if track.forced then display = display .. " (Forced)" end
        local text_y = cy + track_item_h / 2
        local detail = track.label or ""
        local has_detail = detail ~= "" and detail ~= track.language and detail ~= display
        if has_detail then text_y = text_y - math.floor(7 * sc) end
        draw_text(ass, menu_x + padding + math.floor(24 * sc), text_y, display,
            fs, text_color, "00", 1.0, 4)

        local source_labels = {
            embedded = "EMBEDDED", external = "EXTERNAL", downloaded = "DOWNLOADED",
            ai_generated = "AI GENERATED", generated = "AI GENERATED",
        }
        local source_badge = source_labels[track.source or "embedded"]
            or string.upper(capitalize(track.source or "embedded"))
        local codec_badge = string.upper(track.codec or "")
        local badge = codec_badge ~= "" and (codec_badge .. "  " .. source_badge) or source_badge
        draw_text(ass, menu_x + menu_w - padding, text_y, badge,
            fs_small, config.dim_text_color, "40", 1.0, 6)

        if has_detail then
            local max_detail = 42
            if #detail > max_detail then detail = string.sub(detail, 1, max_detail - 1) .. "…" end
            draw_text(ass, menu_x + padding + math.floor(24 * sc),
                cy + track_item_h / 2 + math.floor(9 * sc), detail,
                fs_small, config.dim_text_color, "58", 1.0, 4)
        end

        table.insert(state.subtitle_menu_items, {
            x = menu_x, y = cy, w = menu_w, h = track_item_h,
            action = "select", index = track.index
        })
        cy = cy + track_item_h
    end

    cy = cy + 4  -- divider space

    -- WebUI delay rail: label, minus, current value, plus, and reset in one row.
    local delay_ms = math.floor((mp.get_property_number("sub-delay") or 0) * 1000 + 0.5)
    local delay_alpha = state.active_subtitle >= 0 and "38" or "A0"
    draw_text(ass, menu_x + padding, cy + item_h / 2, "DELAY",
        fs_small, config.dim_text_color, "58", 1.0, 4, nil, true)
    local minus_x = menu_x + menu_w - math.floor(196 * sc)
    local value_x = menu_x + menu_w - math.floor(139 * sc)
    local plus_x = menu_x + menu_w - math.floor(94 * sc)
    local reset_x = menu_x + menu_w - math.floor(52 * sc)
    draw_text(ass, minus_x, cy + item_h / 2, "−", fs, config.text_color, delay_alpha, 1.0, 5)
    draw_text(ass, value_x, cy + item_h / 2, string.format("%+d ms", delay_ms),
        fs_small, config.text_color, delay_alpha, 1.0, 5, "Consolas", false)
    draw_text(ass, plus_x, cy + item_h / 2, "+", fs, config.text_color, delay_alpha, 1.0, 5)
    draw_text(ass, reset_x, cy + item_h / 2, "Reset", fs_small,
        config.dim_text_color, delay_ms ~= 0 and delay_alpha or "A0", 1.0, 5)
    if state.active_subtitle >= 0 then
        table.insert(state.subtitle_menu_items, {
            x = minus_x - math.floor(16 * sc), y = cy, w = math.floor(32 * sc), h = item_h,
            action = "delay", delta = -0.1
        })
        table.insert(state.subtitle_menu_items, {
            x = plus_x - math.floor(16 * sc), y = cy, w = math.floor(32 * sc), h = item_h,
            action = "delay", delta = 0.1
        })
        if delay_ms ~= 0 then
            table.insert(state.subtitle_menu_items, {
                x = reset_x - math.floor(28 * sc), y = cy, w = math.floor(56 * sc), h = item_h,
                action = "delay_reset"
            })
        end
    end
    cy = cy + item_h + 4

    draw_text(ass, menu_x + padding + math.floor(24 * sc), cy + item_h / 2, "Search Online…",
        fs, config.dim_text_color, "00", 1.0, 4)
    table.insert(state.subtitle_menu_items, {
        x = menu_x, y = cy, w = menu_w, h = item_h, action = "search"
    })
    cy = cy + item_h

    if state.subtitle_ai_available then
        draw_text(ass, menu_x + padding + math.floor(24 * sc), cy + item_h / 2,
            "Translate with AI…", fs, config.dim_text_color, "00", 1.0, 4)
        table.insert(state.subtitle_menu_items, {
            x = menu_x, y = cy, w = menu_w, h = item_h, action = "ai"
        })
        cy = cy + item_h
    end

    draw_text(ass, menu_x + padding + math.floor(24 * sc), cy + item_h / 2, "Appearance…",
        fs, config.dim_text_color, "00", 1.0, 4)
    table.insert(state.subtitle_menu_items, {
        x = menu_x, y = cy, w = menu_w, h = item_h, action = "appearance"
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

--------------------------------------------------------------------------------
-- Quality Menu
--------------------------------------------------------------------------------

local function render_quality_menu()
    if not state.quality_menu_visible then
        if state.quality_menu_overlay then
            state.quality_menu_overlay.data = ""
            state.quality_menu_overlay:update()
        end
        state.quality_menu_items = {}
        return
    end

    update_osd_dimensions()
    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local sc = ui_scale()
    local fs = math.floor(config.stats_font_size * sc)
    local fs_small = math.max(math.floor((config.stats_font_size - 2) * sc), 10)
    local padding = math.floor(config.stats_padding * sc)
    local item_h = math.floor((config.stats_line_height + 4) * sc)
    local menu_w = math.floor(280 * sc)

    local qi = state.quality_info
    local versions = (qi and qi.versions) or {}
    local active_file_id = (qi and qi.active_file_id) or 0
    local requested_file_id = (qi and qi.requested_file_id) or active_file_id
    local has_versions = #versions > 1
    local heading_h = math.floor(24 * sc)
    local tiers = visible_quality_tiers()

    -- Calculate menu height
    local menu_h = padding * 2 + (#tiers * item_h)
    if has_versions then
        menu_h = menu_h + (#versions * item_h) + heading_h * 2 + 8
    end

    -- Position above the quality button
    compute_layout()
    local L = state.layout
    local menu_x = W / 2 - menu_w / 2
    local menu_y = H - math.floor(config.hud_height * sc) - menu_h - math.floor(10 * sc)
    if L.btn_quality then
        menu_x = L.btn_quality.x + L.btn_quality.w - menu_w
    end
    if menu_x < 10 then menu_x = 10 end
    if menu_x + menu_w > W - 10 then menu_x = W - menu_w - 10 end
    if menu_y < 10 then menu_y = 10 end

    -- Background
    draw_rounded_rect(ass, menu_x, menu_y, menu_x + menu_w, menu_y + menu_h,
        8, config.bar_bg_color, config.stats_bg_alpha, 1.0)

    local cy = menu_y + padding
    state.quality_menu_items = {}
    local keyboard_row = 0

    -- Versions section
    if has_versions then
        draw_text(ass, menu_x + padding, cy + heading_h / 2, "VERSION",
            fs_small, config.dim_text_color, "58", 1.0, 4, nil, true)
        cy = cy + heading_h
        for _, ver in ipairs(versions) do
            keyboard_row = keyboard_row + 1
            local is_active = (ver.file_id == active_file_id)
            local is_requested = (ver.file_id == requested_file_id)
            local text_color = (is_active or is_requested) and config.text_color or config.dim_text_color
            if state.keyboard_menu_kind == "quality"
                and state.keyboard_menu_index == keyboard_row then
                draw_rounded_rect(ass, menu_x + 2, cy, menu_x + menu_w - 2,
                    cy + item_h, 4, config.text_color, "E6", 1.0)
            end

            if is_active then
                draw_text(ass, menu_x + padding, cy + item_h / 2, "\226\156\147",
                    fs, config.text_color, "00", 1.0, 4)
            end

            local label = ver.label or ver.resolution or "Unknown"
            draw_text(ass, menu_x + padding + math.floor(24 * sc), cy + item_h / 2, label,
                fs, text_color, "00", 1.0, 4)

            local status = is_active and "Playing" or (is_requested and "Requested" or "")
            if status ~= "" then
                draw_text(ass, menu_x + menu_w - padding, cy + item_h / 2, status,
                    fs_small, config.dim_text_color, "40", 1.0, 6)
            end

            table.insert(state.quality_menu_items, {
                x = menu_x, y = cy, w = menu_w, h = item_h,
                action = "version", file_id = ver.file_id
            })
            cy = cy + item_h
        end

        -- Separator
        cy = cy + 4
        draw_rect(ass, menu_x + padding, cy, menu_x + menu_w - padding, cy + 1,
            config.dim_text_color, "60", 1.0)
        cy = cy + 4

        draw_text(ass, menu_x + padding, cy + heading_h / 2, "QUALITY",
            fs_small, config.dim_text_color, "58", 1.0, 4, nil, true)
        cy = cy + heading_h
    end

    -- Quality tiers
    for _, tier in ipairs(tiers) do
        keyboard_row = keyboard_row + 1
        local is_active = (tier.id == state.active_quality)
        local text_color = is_active and config.text_color or config.dim_text_color
        if state.keyboard_menu_kind == "quality"
            and state.keyboard_menu_index == keyboard_row then
            draw_rounded_rect(ass, menu_x + 2, cy, menu_x + menu_w - 2,
                cy + item_h, 4, config.text_color, "E6", 1.0)
        end

        if is_active then
            draw_text(ass, menu_x + padding, cy + item_h / 2, "\226\156\147",
                fs, config.text_color, "00", 1.0, 4)
        end

        draw_text(ass, menu_x + padding + math.floor(24 * sc), cy + item_h / 2, tier.label,
            fs, text_color, "00", 1.0, 4)

        local sublabel = tier.sublabel

        if sublabel then
            draw_text(ass, menu_x + menu_w - padding, cy + item_h / 2, sublabel,
                fs_small, config.dim_text_color, "40", 1.0, 6)
        end

        table.insert(state.quality_menu_items, {
            x = menu_x, y = cy, w = menu_w, h = item_h,
            action = "quality", tier_id = tier.id
        })
        cy = cy + item_h
    end

    -- Update overlay
    if not state.quality_menu_overlay then
        state.quality_menu_overlay = mp.create_osd_overlay("ass-events")
    end
    state.quality_menu_overlay.data = ass.text
    state.quality_menu_overlay.res_x = W
    state.quality_menu_overlay.res_y = H
    state.quality_menu_overlay.z = 70
    state.quality_menu_overlay:update()
end

--------------------------------------------------------------------------------
-- Notice Overlay (admin messages via WebSocket)
--------------------------------------------------------------------------------

local function render_notice()
    if not state.notice_visible then
        if state.notice_overlay then
            state.notice_overlay.data = ""
            state.notice_overlay:update()
        end
        return
    end

    update_osd_dimensions()
    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local sc = ui_scale()
    local fs = math.floor((config.stats_font_size + 1) * sc)
    local fs_small = math.floor(config.stats_font_size * sc)
    local padding = 20
    local box_w = math.min(500, W - 40)
    local box_x = (W - box_w) / 2
    local box_y = 60

    -- Calculate height based on content
    local title_h = 24
    local msg_h = 20
    local box_h = padding * 2 + title_h + msg_h + 8

    -- Background color based on tone
    local bg_color = state.notice_tone == "warning" and "0040B0" or "B05A00"  -- amber / sky (BGR for ASS)

    -- Background
    draw_rounded_rect(ass, box_x, box_y, box_x + box_w, box_y + box_h,
        10, bg_color, "30", 1.0)

    -- Border
    local border_color = state.notice_tone == "warning" and "0055CC" or "CC7733"
    draw_rounded_rect(ass, box_x, box_y, box_x + box_w, box_y + 2,
        0, border_color, "50", 1.0)

    -- Title
    draw_text(ass, box_x + padding, box_y + padding + title_h / 2, state.notice_title,
        fs, config.text_color, "00", 1.0, 4, nil, true)

    -- Message
    draw_text(ass, box_x + padding, box_y + padding + title_h + 8 + msg_h / 2, state.notice_message,
        fs_small, config.text_color, "20", 1.0, 4)

    -- Update overlay
    if not state.notice_overlay then
        state.notice_overlay = mp.create_osd_overlay("ass-events")
    end
    state.notice_overlay.data = ass.text
    state.notice_overlay.res_x = W
    state.notice_overlay.res_y = H
    state.notice_overlay.z = 80  -- above everything
    state.notice_overlay:update()
end

local function show_notice(title, message, tone)
    state.notice_title = title or ""
    state.notice_message = message or ""
    state.notice_tone = tone or "info"
    state.notice_visible = true
    render_notice()

    -- Auto-dismiss after 8 seconds
    if state.notice_timer then
        state.notice_timer:kill()
    end
    state.notice_timer = mp.add_timeout(8, function()
        state.notice_visible = false
        render_notice()
        state.notice_timer = nil
    end)
end

--------------------------------------------------------------------------------
-- Skip Intro/Credits Button
--------------------------------------------------------------------------------

local function update_skip_button_rect()
    if not state.skip_visible then
        state.skip_rect = nil
        return nil
    end

    update_osd_dimensions()
    local W = state.osd_width
    local H = state.osd_height
    local sc = ui_scale()
    local btn_w = math.floor(180 * sc)
    local btn_h = math.floor(44 * sc)
    local btn_x = W - btn_w - 40
    local btn_y = H - math.floor(96 * sc) - btn_h

    state.skip_rect = { x = btn_x, y = btn_y, w = btn_w, h = btn_h }
    return state.skip_rect
end

--------------------------------------------------------------------------------
-- Audio / Chapters Menus
--------------------------------------------------------------------------------

local function menu_window(count, offset, max_visible)
    if count <= 0 then return 1, 0 end
    max_visible = math.max(1, math.min(max_visible, count))
    offset = clamp(offset or 1, 1, math.max(1, count - max_visible + 1))
    return offset, math.min(count, offset + max_visible - 1)
end

local function render_audio_menu()
    if not state.audio_menu_visible then
        if state.audio_menu_overlay then
            state.audio_menu_overlay.data = ""
            state.audio_menu_overlay:update()
        end
        state.audio_menu_items = {}
        return
    end

    update_osd_dimensions()
    compute_layout()
    local ass = assdraw.ass_new()
    local W, H = state.osd_width, state.osd_height
    local sc = ui_scale()
    local padding = math.floor(12 * sc)
    local header_h = math.floor(30 * sc)
    local item_h = math.floor(56 * sc)
    local menu_w = math.floor(360 * sc)
    local max_visible = math.max(3, math.floor((H - math.floor(config.hud_height * sc) - 50 * sc - header_h) / item_h))
    max_visible = math.min(max_visible, 9)
    local first, last = menu_window(#state.audio_tracks, state.audio_menu_offset, max_visible)
    state.audio_menu_offset = first
    local visible_count = math.max(0, last - first + 1)
    local menu_h = padding * 2 + header_h + visible_count * item_h
    local anchor = state.layout.btn_audio
    local menu_x = clamp(anchor.x + anchor.w - menu_w, 10, W - menu_w - 10)
    local menu_y = math.max(10, anchor.y - menu_h - math.floor(8 * sc))

    draw_rounded_rect(ass, menu_x, menu_y, menu_x + menu_w, menu_y + menu_h,
        math.floor(8 * sc), "101010", "1A", 1.0)
    draw_text(ass, menu_x + padding, menu_y + padding + header_h / 2, "AUDIO",
        math.floor(12 * sc), config.text_color, "70", 1.0, 4, nil, true)

    local cy = menu_y + padding + header_h
    state.audio_menu_items = {}
    local keyboard_row = 0
    for i = first, last do
        keyboard_row = keyboard_row + 1
        local track = state.audio_tracks[i]
        local index = tonumber(track.index) or (i - 1)
        local active = index == state.active_audio
        if state.keyboard_menu_kind == "audio"
            and state.keyboard_menu_index == keyboard_row then
            draw_rounded_rect(ass, menu_x + math.floor(4 * sc), cy + math.floor(2 * sc),
                menu_x + menu_w - math.floor(4 * sc), cy + item_h - math.floor(2 * sc),
                math.floor(5 * sc), config.text_color, "E8", 1.0)
        end
        if active then
            draw_rect(ass, menu_x, cy, menu_x + menu_w, cy + item_h,
                config.text_color, "F2", 1.0)
            draw_text(ass, menu_x + padding, cy + item_h / 2, "\226\156\147",
                math.floor(14 * sc), config.text_color, "00", 1.0, 4)
        end

        local text_x = menu_x + padding + math.floor(22 * sc)
        local title = track.title or track.embedded_title
        local channels = tonumber(track.channels) or 0
        local channel_label = ""
        if channels == 8 then channel_label = "7.1"
        elseif channels == 6 then channel_label = "5.1"
        elseif channels == 2 then channel_label = "STEREO"
        elseif channels > 0 then channel_label = tostring(channels) .. " CH" end

        local codec_label = tostring(track.codec or "")
        local codec_lower = codec_label:lower()
        if codec_lower:find("atmos", 1, true) then codec_label = "ATMOS"
        elseif codec_lower:find("truehd", 1, true) then codec_label = "TRUEHD"
        elseif codec_lower:find("dts%-hd") or codec_lower:find("dts:x", 1, true) then codec_label = "DTS-HD"
        elseif codec_lower:find("dts", 1, true) then codec_label = "DTS"
        elseif codec_lower:find("eac3", 1, true) or codec_lower:find("e%-ac%-3") then codec_label = "EAC3"
        else codec_label = codec_label:upper() end

        if not title or title == "" then
            local title_parts = {}
            if track.language and track.language ~= "" then table.insert(title_parts, lang_name(track.language)) end
            if codec_label ~= "" then table.insert(title_parts, codec_label) end
            if channel_label ~= "" then table.insert(title_parts, channel_label:lower()) end
            title = #title_parts > 0 and table.concat(title_parts, " ") or ("Track " .. tostring(index + 1))
        end
        draw_text(ass, text_x, cy + math.floor(18 * sc),
            ass_escape_text(title), math.floor(14 * sc), config.text_color,
            active and "00" or "24", 1.0, 4, nil, true)

        -- Match the WebUI's compact codec/channel/default pills. Anchoring
        -- them to the right keeps technical identity visible for long titles.
        local badges = {}
        if codec_label ~= "" then table.insert(badges, codec_label) end
        if channel_label ~= "" then table.insert(badges, channel_label) end
        if track.default then table.insert(badges, "DEFAULT") end
        local badge_right = menu_x + menu_w - padding
        for badge_index = #badges, 1, -1 do
            local badge = badges[badge_index]
            local badge_w = math.floor((#badge * 5.6 + 13) * sc)
            local badge_h = math.floor(16 * sc)
            local badge_x = badge_right - badge_w
            local badge_y = cy + math.floor(9 * sc)
            draw_rounded_rect(ass, badge_x, badge_y, badge_right, badge_y + badge_h,
                math.floor(3 * sc), config.text_color,
                badge == "DEFAULT" and "C8" or "DC", 1.0)
            draw_text(ass, badge_x + badge_w / 2, badge_y + badge_h / 2,
                badge, math.floor(9.5 * sc), config.text_color, "48", 1.0, 5, nil, true)
            badge_right = badge_x - math.floor(6 * sc)
        end

        local meta = {}
        local language = track.language and lang_name(track.language) or ""
        if language ~= "" and language:lower() ~= tostring(title):lower() then
            table.insert(meta, language)
        end
        if track.layout and track.layout ~= "" then table.insert(meta, track.layout) end
        local bitrate = tonumber(track.bitrate) or 0
        if bitrate > 0 then table.insert(meta, string.format("%d kbps", math.floor(bitrate + 0.5))) end
        local sample_rate = tonumber(track.sample_rate) or 0
        if sample_rate > 0 then
            -- WebUI mediaFormat.ts presents the source value in hertz.
            table.insert(meta, string.format("%d Hz", math.floor(sample_rate + 0.5)))
        end
        local bit_depth = tonumber(track.bit_depth) or 0
        if bit_depth > 0 then table.insert(meta, tostring(bit_depth) .. "-bit") end
        draw_text(ass, text_x, cy + math.floor(40 * sc),
            table.concat(meta, " · "), math.floor(11 * sc), config.text_color, "72", 1.0, 4)

        table.insert(state.audio_menu_items,
            { x = menu_x, y = cy, w = menu_w, h = item_h, index = index })
        cy = cy + item_h
    end

    if not state.audio_menu_overlay then
        state.audio_menu_overlay = mp.create_osd_overlay("ass-events")
    end
    state.audio_menu_overlay.data = ass.text
    state.audio_menu_overlay.res_x = W
    state.audio_menu_overlay.res_y = H
    state.audio_menu_overlay.z = 70
    state.audio_menu_overlay:update()
end

local function render_chapter_menu()
    if not state.chapter_menu_visible then
        remove_chapter_thumbnail_menu_overlays()
        if state.chapter_menu_overlay then
            state.chapter_menu_overlay.data = ""
            state.chapter_menu_overlay:update()
        end
        state.chapter_menu_items = {}
        return
    end

    update_osd_dimensions()
    compute_layout()
    local ass = assdraw.ass_new()
    local W, H = state.osd_width, state.osd_height
    local sc = ui_scale()
    local padding = math.floor(12 * sc)
    local header_h = math.floor(30 * sc)
    local item_h = math.floor(64 * sc)
    local menu_w = math.floor(300 * sc)
    local max_visible = math.max(3, math.floor((H * 0.60 - header_h - padding * 2) / item_h))
    max_visible = math.min(max_visible, 12)
    local first, last = menu_window(#state.chapters, state.chapter_menu_offset, max_visible)
    state.chapter_menu_offset = first
    local visible_count = math.max(0, last - first + 1)
    local menu_h = padding * 2 + header_h + visible_count * item_h
    local anchor = state.layout.btn_chapters
    local menu_x = clamp(anchor.x + anchor.w - menu_w, 10, W - menu_w - 10)
    local menu_y = math.max(10, anchor.y - menu_h - math.floor(8 * sc))

    draw_rounded_rect(ass, menu_x, menu_y, menu_x + menu_w, menu_y + menu_h,
        math.floor(8 * sc), "101010", "1A", 1.0)
    draw_text(ass, menu_x + padding, menu_y + padding + header_h / 2, "CHAPTERS",
        math.floor(12 * sc), config.text_color, "70", 1.0, 4, nil, true)

    local cy = menu_y + padding + header_h
    state.chapter_menu_items = {}
    local keyboard_row = 0
    for i = first, last do
        keyboard_row = keyboard_row + 1
        local chapter = state.chapters[i]
        local start_seconds = tonumber(chapter.start_seconds) or 0
        local end_seconds = tonumber(chapter.end_seconds) or math.huge
        local active = state.time_pos >= start_seconds and state.time_pos < end_seconds
        if state.keyboard_menu_kind == "chapters"
            and state.keyboard_menu_index == keyboard_row then
            draw_rounded_rect(ass, menu_x + math.floor(4 * sc), cy + math.floor(2 * sc),
                menu_x + menu_w - math.floor(4 * sc), cy + item_h - math.floor(2 * sc),
                math.floor(5 * sc), config.text_color, "E8", 1.0)
        end
        if active then
            draw_rect(ass, menu_x, cy, menu_x + menu_w, cy + item_h,
                config.text_color, "F2", 1.0)
        end

        local thumb_x = menu_x + padding
        local thumb_y = cy + math.floor(8 * sc)
        local thumb_w = math.floor(80 * sc)
        local thumb_h = math.floor(48 * sc)
        local overlay_id = chapter_thumbnail_menu_overlay_first + (i - first)
        request_chapter_thumbnail(chapter)
        if not update_chapter_thumbnail_overlay(
            chapter, thumb_x, thumb_y, thumb_w, thumb_h, overlay_id) then
            draw_rounded_rect(ass, thumb_x, thumb_y, thumb_x + thumb_w, thumb_y + thumb_h,
                math.floor(4 * sc), "242424", "08", 1.0)
            draw_text(ass, thumb_x + thumb_w / 2, thumb_y + thumb_h / 2,
                "CH", math.floor(9 * sc), config.text_color, "D0", 1.0, 5, nil, true)
        end

        local text_x = thumb_x + thumb_w + math.floor(12 * sc)
        draw_text(ass, text_x, cy + math.floor(25 * sc),
            ass_escape_text(chapter.title or ("Chapter " .. tostring(i))), math.floor(14 * sc),
            config.text_color, active and "00" or "28", 1.0, 4, nil, active)
        draw_text(ass, text_x, cy + math.floor(44 * sc),
            format_time(start_seconds), math.floor(11 * sc), config.text_color, "78", 1.0, 4, "Consolas", false)
        table.insert(state.chapter_menu_items,
            { x = menu_x, y = cy, w = menu_w, h = item_h, start_seconds = start_seconds })
        cy = cy + item_h
    end

    local visible_overlays = math.max(0, last - first + 1)
    for overlay_id = chapter_thumbnail_menu_overlay_first + visible_overlays,
        chapter_thumbnail_menu_overlay_last do
        remove_chapter_thumbnail_overlay(overlay_id)
    end

    if not state.chapter_menu_overlay then
        state.chapter_menu_overlay = mp.create_osd_overlay("ass-events")
    end
    state.chapter_menu_overlay.data = ass.text
    state.chapter_menu_overlay.res_x = W
    state.chapter_menu_overlay.res_y = H
    state.chapter_menu_overlay.z = 70
    state.chapter_menu_overlay:update()
end

local function update_next_episode_button_rect()
    if not state.next_ep_visible then
        state.next_ep_rect = nil
        return nil
    end

    update_osd_dimensions()
    local W = state.osd_width
    local H = state.osd_height
    local sc = ui_scale()
    local btn_w = math.floor(180 * sc)
    local btn_h = math.floor(44 * sc)
    local btn_x = W - btn_w - 40
    local btn_y = H - math.floor(96 * sc) - btn_h

    if state.skip_visible then
        btn_y = btn_y - (btn_h + 12)
    end

    state.next_ep_rect = { x = btn_x, y = btn_y, w = btn_w, h = btn_h }
    return state.next_ep_rect
end

local function point_on_skip_button(mx, my)
    local r = update_skip_button_rect()
    return r ~= nil and point_in_rect(mx, my, r)
end

local function point_on_next_episode_button(mx, my)
    local r = update_next_episode_button_rect()
    return r ~= nil and point_in_rect(mx, my, r)
end

local function point_on_next_episode_countdown_action(mx, my)
    for _, item in ipairs(state.next_ep_countdown_actions or {}) do
        if point_in_rect(mx, my, item) then return item.action end
    end
    return nil
end

local function point_on_floating_action_button(mx, my)
    return point_on_skip_button(mx, my) or point_on_next_episode_button(mx, my)
end

render_skip_button = function()
    if not state.skip_visible then
        if state.skip_overlay then
            state.skip_overlay.data = ""
            state.skip_overlay:update()
        end
        state.skip_rect = nil
        return
    end

    local r = update_skip_button_rect()
    if not r then return end

    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local sc = ui_scale()
    local fs = math.floor((config.stats_font_size + 2) * sc)

    -- Background
    draw_rounded_rect(ass, r.x, r.y, r.x + r.w, r.y + r.h,
        8, "FFFFFF", "30", 1.0)

    -- Border
    draw_rounded_rect(ass, r.x, r.y, r.x + r.w, r.y + 1,
        0, "FFFFFF", "60", 1.0)

    -- Text
    draw_text(ass, r.x + r.w / 2, r.y + r.h / 2, state.skip_label,
        fs, config.text_color, "00", 1.0, 5, nil, true)

    if not state.skip_overlay then
        state.skip_overlay = mp.create_osd_overlay("ass-events")
    end
    state.skip_overlay.data = ass.text
    state.skip_overlay.res_x = W
    state.skip_overlay.res_y = H
    state.skip_overlay.z = 55
    state.skip_overlay:update()
end

check_skip_markers = function()
    local pos = state.time_pos
    if pos <= 0 then return end

    local was_visible = state.skip_visible
    state.skip_visible = false

    -- Check intro range. Webui parity (VideoPlayer.tsx): rely on
    -- end > start to mean "marker present with duration"; earlier code
    -- also required start > 0, which wrongly hid Skip Intro whenever
    -- the intro began at position 0 (common on streaming-first shows).
    if state.intro_end > state.intro_start then
        if pos >= state.intro_start and pos < state.intro_end then
            state.skip_visible = true
            state.skip_label = "Skip Intro"
            state.skip_target = state.intro_end
        end
    end

    -- Check credits range. If a next episode exists, that affordance wins;
    -- otherwise Skip Credits is available only for plausible tail markers.
    if not state.next_ep_available and credits_marker_is_plausible() then
        if pos >= state.credits_start and pos < state.credits_end then
            state.skip_visible = true
            state.skip_label = "Skip Credits"
            state.skip_target = state.credits_end
        end
    end

    -- Only re-render if state changed
    if state.skip_visible ~= was_visible then
        render_skip_button()
    end
end

-- Decide whether the Next Episode button should be visible right now.
-- Called every tick. Rules:
--   1. Host has flagged next_ep_available (AutoDetectNextEpisode ran)
--   2. Current position is either inside a plausible credits marker range OR
--      in the final 5% of total duration. Bogus early credits markers are
--      ignored so the button cannot appear while there is still substantial
--      episode runtime left.
check_next_episode_button = function()
    local was_visible = state.next_ep_visible
    state.next_ep_visible = false

    if not state.next_ep_available then
        if state.next_ep_visible ~= was_visible then render_next_episode_button() end
        return
    end

    local pos = state.time_pos
    local dur = state.duration
    if pos <= 0 or dur <= 0 then
        if state.next_ep_visible ~= was_visible then render_next_episode_button() end
        return
    end

    local in_credits = credits_marker_is_plausible()
                   and pos >= state.credits_start
                   and pos < state.credits_end
    local near_end = pos >= dur * 0.95

    -- Credits use the richer WebUI countdown card. Keep this compact button
    -- only as a final-five-percent fallback when no credits region is active.
    if near_end and not in_credits then
        state.next_ep_visible = true
    end

    if state.next_ep_visible and state.skip_visible then
        state.skip_visible = false
        render_skip_button()
    end

    if state.next_ep_visible ~= was_visible then
        render_next_episode_button()
    end
end

-- Match NextEpisodeOverlay.tsx/useNextEpisode.ts: entering the configured
-- credits region starts a ten-second countdown that remains visible even when
-- the transport chrome fades. Cancel suppresses it for this episode; Play Now
-- and expiry both advance through the same host-owned navigation path.
check_next_episode_countdown = function()
    if not state.next_ep_available or not state.next_ep_detail or
       state.next_ep_countdown_cancelled or not credits_marker_is_plausible() then
        if state.next_ep_countdown_active then
            state.next_ep_countdown_active = false
            render_next_episode_countdown()
        end
        return
    end

    local started_now = false
    if not state.next_ep_countdown_active then
        if state.time_pos < state.credits_start then return end
        state.next_ep_countdown_active = true
        state.next_ep_countdown_started_at = mp.get_time()
        state.next_ep_countdown_remaining = 10
        started_now = true
    end

    local previous_remaining = state.next_ep_countdown_remaining
    local elapsed = math.max(0, mp.get_time() - state.next_ep_countdown_started_at)
    local remaining = math.max(0, math.ceil(10 - elapsed))
    state.next_ep_countdown_remaining = remaining

    if remaining <= 0 then
        state.next_ep_countdown_active = false
        state.next_ep_available = false
        state.next_ep_visible = false
        render_next_episode_countdown()
        render_next_episode_button()
        mp.commandv("script-message", "silo-next-episode")
        return
    end

    if started_now or remaining ~= previous_remaining then
        render_next_episode_countdown()
    end
end

render_next_episode_countdown = function()
    if not state.next_ep_countdown_active or not state.next_ep_detail then
        if state.next_ep_countdown_overlay then
            state.next_ep_countdown_overlay.data = ""
            state.next_ep_countdown_overlay:update()
        end
        state.next_ep_countdown_actions = {}
        return
    end

    update_osd_dimensions()
    local W = state.osd_width
    local H = state.osd_height
    local sc = ui_scale()
    local card_w = math.floor(360 * sc)
    local card_h = math.floor(126 * sc)
    local card_x = W - math.floor(24 * sc) - card_w
    local card_y = H - math.floor(96 * sc) - card_h
    local pad = math.floor(16 * sc)
    local gap = math.floor(8 * sc)
    local button_h = math.floor(32 * sc)
    local cancel_w = math.floor(78 * sc)
    local play_x = card_x + pad
    local buttons_y = card_y + card_h - pad - button_h
    local cancel_x = card_x + card_w - pad - cancel_w
    local play_w = cancel_x - gap - play_x

    local ass = assdraw.ass_new()
    draw_rounded_rect(ass, card_x, card_y, card_x + card_w, card_y + card_h,
        math.floor(8 * sc), "000000", "33", 1.0)

    draw_text(ass, card_x + pad, card_y + math.floor(18 * sc),
        "UP NEXT IN " .. tostring(state.next_ep_countdown_remaining) .. "s",
        math.floor(11 * sc), config.text_color, "66", 1.0, 7, nil, false)

    local detail = state.next_ep_detail
    local episode_label = "S" .. tostring(detail.season_number or "?") ..
        ":E" .. tostring(detail.episode_number or "?")
    local title = tostring(detail.title or "")
    if title ~= "" then episode_label = episode_label .. " \xe2\x80\x94 " .. title end
    draw_text(ass, card_x + pad, card_y + math.floor(47 * sc),
        ass_escape_text(episode_label), math.floor(14 * sc),
        config.text_color, "00", 1.0, 7, nil, true)

    draw_rounded_rect(ass, play_x, buttons_y, play_x + play_w, buttons_y + button_h,
        math.floor(5 * sc), "FFFFFF", "00", 1.0)
    draw_text(ass, play_x + play_w / 2, buttons_y + button_h / 2, "Play Now",
        math.floor(13 * sc), "000000", "00", 1.0, 5, nil, true)

    draw_rounded_rect(ass, cancel_x, buttons_y, cancel_x + cancel_w, buttons_y + button_h,
        math.floor(5 * sc), "FFFFFF", "D0", 1.0)
    draw_text(ass, cancel_x + cancel_w / 2, buttons_y + button_h / 2, "Cancel",
        math.floor(13 * sc), config.text_color, "00", 1.0, 5, nil, false)

    state.next_ep_countdown_actions = {
        { action = "play", x = play_x, y = buttons_y, w = play_w, h = button_h },
        { action = "cancel", x = cancel_x, y = buttons_y, w = cancel_w, h = button_h },
    }

    if not state.next_ep_countdown_overlay then
        state.next_ep_countdown_overlay = mp.create_osd_overlay("ass-events")
    end
    state.next_ep_countdown_overlay.data = ass.text
    state.next_ep_countdown_overlay.res_x = W
    state.next_ep_countdown_overlay.res_y = H
    state.next_ep_countdown_overlay.z = 57
    state.next_ep_countdown_overlay:update()
end

render_translation_buffering = function()
    if not state.translation_buffering then
        if state.translation_buffering_overlay then
            state.translation_buffering_overlay.data = ""
            state.translation_buffering_overlay:update()
        end
        return
    end

    update_osd_dimensions()
    local W = state.osd_width
    local H = state.osd_height
    local sc = ui_scale()
    local label = "Preparing " .. tostring(state.translation_buffering_label or "translated") ..
        " subtitles\xe2\x80\xa6"
    local panel_w = math.floor(math.min(520, math.max(330, 150 + #label * 7)) * sc)
    local panel_h = math.floor(48 * sc)
    local panel_x = (W - panel_w) / 2
    local panel_y = (H - panel_h) / 2
    local frames = { "\xe2\x97\x9c", "\xe2\x97\x9d", "\xe2\x97\x9e", "\xe2\x97\x9f" }
    local spinner = frames[(state.translation_spinner_frame % #frames) + 1]

    local ass = assdraw.ass_new()
    draw_rounded_rect(ass, panel_x, panel_y, panel_x + panel_w, panel_y + panel_h,
        math.floor(8 * sc), "000000", "33", 1.0)
    draw_text(ass, panel_x + math.floor(27 * sc), panel_y + panel_h / 2,
        spinner, math.floor(18 * sc), config.text_color, "00", 1.0, 5, nil, false)
    draw_text(ass, panel_x + math.floor(48 * sc), panel_y + panel_h / 2,
        ass_escape_text(label), math.floor(14 * sc), config.text_color, "00", 1.0, 4, nil, false)

    if not state.translation_buffering_overlay then
        state.translation_buffering_overlay = mp.create_osd_overlay("ass-events")
    end
    state.translation_buffering_overlay.data = ass.text
    state.translation_buffering_overlay.res_x = W
    state.translation_buffering_overlay.res_y = H
    state.translation_buffering_overlay.z = 58
    state.translation_buffering_overlay:update()
end

-- Draw a pill "Next Episode ▶" button in the bottom-right corner above
-- the progress bar, offset left of the Skip button if both are visible.
render_next_episode_button = function()
    if not state.next_ep_visible then
        if state.next_ep_overlay then
            state.next_ep_overlay.data = ""
            state.next_ep_overlay:update()
        end
        state.next_ep_rect = nil
        return
    end

    local r = update_next_episode_button_rect()
    if not r then return end

    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local sc = ui_scale()
    local fs = math.floor((config.stats_font_size + 2) * sc)

    -- Background (accent-tinted so it's distinct from the Skip button)
    draw_rounded_rect(ass, r.x, r.y, r.x + r.w, r.y + r.h,
        8, "FFFFFF", "30", 1.0)

    -- Top border highlight
    draw_rounded_rect(ass, r.x, r.y, r.x + r.w, r.y + 1,
        0, "FFFFFF", "60", 1.0)

    -- Label: "Next Episode ▶"
    draw_text(ass, r.x + r.w / 2, r.y + r.h / 2, "Next Episode \xe2\x96\xb6",
        fs, config.text_color, "00", 1.0, 5, nil, true)

    if not state.next_ep_overlay then
        state.next_ep_overlay = mp.create_osd_overlay("ass-events")
    end
    state.next_ep_overlay.data = ass.text
    state.next_ep_overlay.res_x = W
    state.next_ep_overlay.res_y = H
    state.next_ep_overlay.z = 56  -- one above the skip button
    state.next_ep_overlay:update()
end

-- Pause center indicator: 64×64 circle bg-black/50 with a Play triangle.
-- Matches the webui player chrome (64x64 circle bg-black/50 + Play icon).
render_pause_indicator = function()
    local should_show = state.pause and not state.osc_disabled and not state.translation_buffering

    if should_show == state.pause_indicator_shown then return end
    state.pause_indicator_shown = should_show

    if not should_show then
        if state.pause_indicator_overlay then
            state.pause_indicator_overlay.data = ""
            state.pause_indicator_overlay:update()
        end
        return
    end

    update_osd_dimensions()
    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local cx = math.floor(W / 2)
    local cy = math.floor(H / 2)
    local r = 32

    -- Circle background (black 50% opacity)
    draw_rounded_rect(ass, cx - r, cy - r, cx + r, cy + r, r, "000000", "80", 1.0)

    -- Play triangle (white, centered with a slight right offset for optical balance)
    local tri_size = 18
    local ox = 3  -- slight right offset
    ass:new_event()
    ass:pos(0, 0)
    ass:append(string.format("{\\an7\\bord0\\shad0\\1c&HFFFFFF&\\1a&H00&\\p1}"))
    ass:draw_start()
    ass:move_to(cx - tri_size / 2 + ox, cy - tri_size)
    ass:line_to(cx + tri_size + ox, cy)
    ass:line_to(cx - tri_size / 2 + ox, cy + tri_size)
    ass:draw_stop()

    if not state.pause_indicator_overlay then
        state.pause_indicator_overlay = mp.create_osd_overlay("ass-events")
    end
    state.pause_indicator_overlay.data = ass.text
    state.pause_indicator_overlay.res_x = W
    state.pause_indicator_overlay.res_y = H
    state.pause_indicator_overlay.z = 40  -- below the skip/next buttons
    state.pause_indicator_overlay:update()
end

-- Toggle stats (defined here so handle_mouse_down can reference it)
local function toggle_stats()
    state.stats_visible = not state.stats_visible
    render_stats()
end

local function close_transport_menus(except)
    if except ~= "audio" and state.audio_menu_visible then
        state.audio_menu_visible = false
        render_audio_menu()
    end
    if except ~= "chapters" and state.chapter_menu_visible then
        state.chapter_menu_visible = false
        render_chapter_menu()
    end
    if except ~= "subtitles" and state.subtitle_menu_visible then
        state.subtitle_menu_visible = false
        render_subtitle_menu()
    end
    if except ~= "quality" and state.quality_menu_visible then
        state.quality_menu_visible = false
        render_quality_menu()
    end
end

local function current_keyboard_menu()
    if state.subtitle_menu_visible then return "subtitles", state.subtitle_menu_items end
    if state.quality_menu_visible then return "quality", state.quality_menu_items end
    if state.audio_menu_visible then return "audio", state.audio_menu_items end
    if state.chapter_menu_visible then return "chapters", state.chapter_menu_items end
    return nil, {}
end

local function keyboard_menu_items(kind, source)
    local result = {}
    for _, item in ipairs(source or {}) do
        local include = kind == "audio" or kind == "chapters"
            or (kind == "quality" and (item.action == "version" or item.action == "quality"))
            or (kind == "subtitles" and (item.action == "off" or item.action == "select"))
        if include then table.insert(result, item) end
    end
    return result
end

local function render_keyboard_menu(kind)
    if kind == "subtitles" then render_subtitle_menu()
    elseif kind == "quality" then render_quality_menu()
    elseif kind == "audio" then render_audio_menu()
    elseif kind == "chapters" then render_chapter_menu()
    end
end

local function move_keyboard_menu_focus(direction)
    local kind, raw_items = current_keyboard_menu()
    if not kind then return false end
    local items = keyboard_menu_items(kind, raw_items)
    if #items == 0 then return true end
    state.keyboard_menu_kind = kind
    if direction == "home" then
        state.keyboard_menu_index = 1
    elseif direction == "end" then
        state.keyboard_menu_index = #items
    elseif direction == "next" then
        state.keyboard_menu_index = state.keyboard_menu_index < #items
            and state.keyboard_menu_index + 1 or 1
    else
        state.keyboard_menu_index = state.keyboard_menu_index > 1
            and state.keyboard_menu_index - 1 or #items
    end
    render_keyboard_menu(kind)
    return true
end

local function activate_keyboard_menu_item()
    local kind, raw_items = current_keyboard_menu()
    if not kind then return false end
    local items = keyboard_menu_items(kind, raw_items)
    local item = items[state.keyboard_menu_index]
    if not item then return true end
    if kind == "subtitles" then
        local index = item.action == "off" and -1 or item.index
        mp.commandv("script-message", "silo-subtitle-select", tostring(index))
        state.subtitle_menu_visible = false
        render_subtitle_menu()
    elseif kind == "quality" then
        if item.action == "version" then
            mp.commandv("script-message", "silo-version-select", tostring(item.file_id))
        else
            state.quality_switching = true
            mp.commandv("script-message", "silo-quality-select", item.tier_id)
        end
        state.quality_menu_visible = false
        render_quality_menu()
    elseif kind == "audio" then
        mp.commandv("script-message", "silo-audio-select", tostring(item.index))
        state.audio_menu_visible = false
        render_audio_menu()
    elseif kind == "chapters" then
        seek_and_resume(item.start_seconds, "absolute+keyframes")
        state.chapter_menu_visible = false
        render_chapter_menu()
    end
    state.keyboard_menu_kind = nil
    state.keyboard_menu_index = -1
    return true
end

local function close_keyboard_surface()
    local kind = current_keyboard_menu()
    if kind then
        close_transport_menus(nil)
        state.keyboard_menu_kind = nil
        state.keyboard_menu_index = -1
        return true
    end
    if state.marker_editor_visible then
        close_marker_editor()
        return true
    end
    return false
end

local function handle_mouse_down()
    if state.osc_disabled then return end
    local mx = state.mouse_x
    local my = state.mouse_y

    local next_action = point_on_next_episode_countdown_action(mx, my)
    if next_action then
        consume_video_click()
        state.next_ep_countdown_active = false
        if next_action == "cancel" then
            state.next_ep_countdown_cancelled = true
        else
            state.next_ep_available = false
            state.next_ep_visible = false
            render_next_episode_button()
            mp.commandv("script-message", "silo-next-episode")
        end
        render_next_episode_countdown()
        return
    end

    -- Skip intro/credits button
    if point_on_skip_button(mx, my) then
        consume_video_click()
        seek_and_resume(state.skip_target, "absolute+keyframes")
        state.skip_visible = false
        render_skip_button()
        return
    end

    -- Next Episode button — tell the host to advance immediately. The
    -- host owns ContinuePlayingNextAsync which tears down the current
    -- session and starts the next episode.
    if point_on_next_episode_button(mx, my) then
        consume_video_click()
        state.next_ep_visible = false
        state.next_ep_available = false
        render_next_episode_button()
        mp.commandv("script-message", "silo-next-episode")
        return
    end

    -- Marker editor panel actions. This panel is rendered in the native OSC
    -- so the video and editable timeline remain visible while it is open.
    if state.marker_editor_visible then
        for _, item in ipairs(state.marker_editor_actions or {}) do
            if point_in_rect(mx, my, item) then
                consume_video_click()
                local kind = item.kind or state.marker_editor_kind
                if item.action == "select" then
                    update_active_marker_draft_from_handles()
                    state.marker_editor_kind = kind
                    sync_active_marker_editor_range()
                elseif item.action == "set-start" then
                    local range = state.marker_editor_draft[kind]
                    local start_seconds = clamp(state.time_pos, 0, state.duration)
                    local end_seconds = range and range.finish
                        or math.min(state.duration, start_seconds + 60)
                    if end_seconds <= start_seconds then
                        start_seconds = math.max(0, end_seconds - 0.5)
                    end
                    state.marker_editor_draft[kind] = {
                        start = start_seconds, finish = end_seconds,
                    }
                    sync_active_marker_editor_range()
                elseif item.action == "set-end" then
                    local range = state.marker_editor_draft[kind]
                    local end_seconds = clamp(state.time_pos, 0, state.duration)
                    local start_seconds = range and range.start
                        or math.max(0, end_seconds - 60)
                    if end_seconds <= start_seconds then
                        end_seconds = math.min(state.duration, start_seconds + 0.5)
                    end
                    state.marker_editor_draft[kind] = {
                        start = start_seconds, finish = end_seconds,
                    }
                    sync_active_marker_editor_range()
                elseif item.action == "reset" then
                    state.marker_editor_draft[kind] = clone_marker_range(
                        state.marker_editor_original[kind])
                    sync_active_marker_editor_range()
                elseif item.action == "clear" then
                    state.marker_editor_draft[kind] = nil
                    sync_active_marker_editor_range()
                elseif item.action == "reset-all" then
                    for _, marker in ipairs(marker_regions) do
                        state.marker_editor_draft[marker.key] = clone_marker_range(
                            state.marker_editor_original[marker.key])
                    end
                    sync_active_marker_editor_range()
                elseif item.action == "cancel" then
                    close_marker_editor()
                    return
                elseif item.action == "save" then
                    update_active_marker_draft_from_handles()
                    local values = {}
                    for _, marker in ipairs(marker_regions) do
                        local range = state.marker_editor_draft[marker.key]
                        table.insert(values, range and tostring(range.start) or "-1")
                        table.insert(values, range and tostring(range.finish) or "-1")
                    end
                    mp.commandv("script-message", "silo-marker-save", unpack(values))
                    close_marker_editor()
                    return
                end
                show_osc()
                request_tick()
                return
            end
        end
        if state.marker_editor_header_rect
            and point_in_rect(mx, my, state.marker_editor_header_rect) then
            consume_video_click()
            state.dragging_marker_panel = true
            state.marker_panel_drag_start_x = mx
            state.marker_panel_drag_start_y = my
            state.marker_panel_drag_origin_x = state.marker_editor_panel_rect.x
            state.marker_panel_drag_origin_y = state.marker_editor_panel_rect.y
            return
        end
        -- Consume clicks inside decorative panel space. Outside clicks leave
        -- the editor open, matching the WebUI's non-modal floating panel.
        if state.marker_editor_panel_rect
            and point_in_rect(mx, my, state.marker_editor_panel_rect) then
            consume_video_click()
            return
        end
    end

    -- Watch Party host actions live in the same top-right panel as the WebUI.
    for _, item in ipairs(state.watch_party_actions or {}) do
        if point_in_rect(mx, my, item) then
            consume_video_click()
            if item.action == "end" then
                state.watch_party_end_confirm = true
                request_tick()
            elseif item.action == "cancel-end" then
                state.watch_party_end_confirm = false
                request_tick()
            elseif item.action == "confirm-end" then
                state.watch_party_end_confirm = false
                mp.commandv("script-message", "silo-watch-party-action", "end")
                request_tick()
            else
                mp.commandv("script-message", "silo-watch-party-action", item.action)
            end
            return
        end
    end
    if state.watch_party_end_confirm then
        consume_video_click()
        return
    end

    -- Stats overlay close button (always check, even if OSC is hidden)
    if state.stats_visible and state.stats_close_rect then
        local r = state.stats_close_rect
        if mx >= r.x and mx <= r.x + r.w and my >= r.y and my <= r.y + r.h then
            state.stats_visible = false
            render_stats()
            return
        end
    end

    -- Audio menu click handling
    if state.audio_menu_visible then
        for _, item in ipairs(state.audio_menu_items) do
            if point_in_rect(mx, my, item) then
                mp.commandv("script-message", "silo-audio-select", tostring(item.index))
                state.active_audio = item.index
                state.audio_menu_visible = false
                render_audio_menu()
                return
            end
        end
        state.audio_menu_visible = false
        render_audio_menu()
        return
    end

    -- Chapter menu click handling
    if state.chapter_menu_visible then
        for _, item in ipairs(state.chapter_menu_items) do
            if point_in_rect(mx, my, item) then
                seek_and_resume(item.start_seconds, "absolute+keyframes")
                state.chapter_menu_visible = false
                render_chapter_menu()
                return
            end
        end
        state.chapter_menu_visible = false
        render_chapter_menu()
        return
    end

    -- Subtitle menu click handling
    if state.subtitle_menu_visible then
        local handled = false
        for _, item in ipairs(state.subtitle_menu_items) do
            if point_in_rect(mx, my, item) then
                if item.action == "off" then
                    mp.commandv("script-message", "silo-subtitle-select", "-1")
                elseif item.action == "select" then
                    mp.commandv("script-message", "silo-subtitle-select", tostring(item.index))
                elseif item.action == "search" then
                    mp.commandv("script-message", "silo-subtitle-search")
                elseif item.action == "appearance" then
                    mp.commandv("script-message", "silo-subtitle-appearance")
                elseif item.action == "ai" then
                    mp.commandv("script-message", "silo-subtitle-ai")
                elseif item.action == "delay" then
                    local current = mp.get_property_number("sub-delay") or 0
                    mp.set_property_number("sub-delay", clamp(current + item.delta, -10, 10))
                elseif item.action == "delay_reset" then
                    mp.set_property_number("sub-delay", 0)
                end
                -- Delay controls in the WebUI are an interactive rail. Keep
                -- the menu open so repeated +/-100 ms adjustments and Reset
                -- are possible without reopening the popup after every click.
                if item.action == "delay" or item.action == "delay_reset" then
                    render_subtitle_menu()
                else
                    state.subtitle_menu_visible = false
                    render_subtitle_menu()
                end
                return
            end
        end
        -- Click outside menu — close it
        state.subtitle_menu_visible = false
        render_subtitle_menu()
        return
    end

    -- Quality menu click handling
    if state.quality_menu_visible then
        for _, item in ipairs(state.quality_menu_items) do
            if point_in_rect(mx, my, item) then
                if item.action == "version" then
                    mp.commandv("script-message", "silo-version-select", tostring(item.file_id))
                elseif item.action == "quality" then
                    state.quality_switching = true
                    mp.commandv("script-message", "silo-quality-select", item.tier_id)
                end
                state.quality_menu_visible = false
                render_quality_menu()
                return
            end
        end
        -- Click outside menu — close it
        state.quality_menu_visible = false
        render_quality_menu()
        return
    end

    if state.current_alpha < 0.1 then return end

    compute_layout()
    local L = state.layout

    -- Marker handles own the pointer before the seek rail underneath.
    if state.marker_editor_active then
        for _, edge in ipairs({ "start", "end" }) do
            local rect = state.marker_editor_handle_rects[edge]
            if rect and point_in_rect(mx, my, rect) then
                state.dragging_marker_edge = edge
                consume_video_click()
                request_tick()
                return
            end
        end
    end

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
        seek_relative_and_resume(-10)
        return
    end

    if L.btn_prev_ep and point_in_rect(mx, my, L.btn_prev_ep) then
        state.prev_ep_available = false
        mp.commandv("script-message", "silo-prev-episode")
        return
    end

    -- Check skip forward
    if L.btn_skip_fwd and point_in_rect(mx, my, L.btn_skip_fwd) then
        seek_relative_and_resume(30)
        return
    end

    -- Series context reserves the matching next-episode slot to the right of
    -- the transport cluster, mirroring the WebUI's symmetric layout.
    if L.btn_next_ep and point_in_rect(mx, my, L.btn_next_ep) then
        state.next_ep_visible = false
        state.next_ep_available = false
        render_next_episode_button()
        mp.commandv("script-message", "silo-next-episode")
        return
    end

    -- Check audio tracks
    if L.btn_audio and point_in_rect(mx, my, L.btn_audio) and #state.audio_tracks > 1 then
        close_transport_menus("audio")
        state.audio_menu_visible = not state.audio_menu_visible
        state.keyboard_menu_kind = state.audio_menu_visible and "audio" or nil
        state.keyboard_menu_index = -1
        render_audio_menu()
        return
    end

    -- Check chapters
    if L.btn_chapters and point_in_rect(mx, my, L.btn_chapters) and #state.chapters > 0 then
        close_transport_menus("chapters")
        state.chapter_menu_visible = not state.chapter_menu_visible
        state.keyboard_menu_kind = state.chapter_menu_visible and "chapters" or nil
        state.keyboard_menu_index = -1
        render_chapter_menu()
        return
    end

    -- Check CC (subtitle menu toggle)
    if L.btn_cc and point_in_rect(mx, my, L.btn_cc) then
        close_transport_menus("subtitles")
        state.subtitle_menu_visible = not state.subtitle_menu_visible
        state.keyboard_menu_kind = state.subtitle_menu_visible and "subtitles" or nil
        state.keyboard_menu_index = -1
        render_subtitle_menu()
        return
    end

    -- Check quality/settings button
    if L.btn_quality and point_in_rect(mx, my, L.btn_quality) then
        close_transport_menus("quality")
        state.quality_menu_visible = not state.quality_menu_visible
        state.keyboard_menu_kind = state.quality_menu_visible and "quality" or nil
        state.keyboard_menu_index = -1
        render_quality_menu()
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
        mp.commandv("script-message", "silo-minimize")
        return
    end

    -- Check fullscreen
    if L.btn_fullscreen and point_in_rect(mx, my, L.btn_fullscreen) then
        mp.commandv("script-message", "silo-fullscreen-toggle")
        return
    end

    if L.btn_marker_edit and point_in_rect(mx, my, L.btn_marker_edit) then
        if state.marker_editor_visible then
            close_marker_editor()
        else
            close_transport_menus(nil)
            open_marker_editor()
        end
        return
    end

    if L.btn_pip and point_in_rect(mx, my, L.btn_pip) then
        mp.commandv("script-message", "silo-pip-toggle")
        return
    end

    -- Check exit
    if L.btn_exit and point_in_rect(mx, my, L.btn_exit) then
        mp.commandv("script-message", "silo-exit")
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
    if state.dragging_marker_edge then
        state.dragging_marker_edge = nil
        request_tick()
    end
    if state.dragging_marker_panel then
        state.dragging_marker_panel = false
        request_tick()
    end
    -- Complete seek drag
    if state.dragging_seek then
        state.dragging_seek = false
        if state.duration > 0 then
            local target_time = state.seek_drag_pos * state.duration
            seek_and_resume(target_time, "absolute+keyframes")
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
    if not state.dragging_seek and not state.dragging_volume
        and not state.dragging_marker_edge and not state.dragging_marker_panel then
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
    if state.subtitle_menu_visible then
        state.subtitle_menu_offset = math.max(1, state.subtitle_menu_offset - 1)
        render_subtitle_menu()
        return
    end
    if state.chapter_menu_visible then
        state.chapter_menu_offset = math.max(1, state.chapter_menu_offset - 1)
        render_chapter_menu()
        return
    end
    if state.audio_menu_visible then
        state.audio_menu_offset = math.max(1, state.audio_menu_offset - 1)
        render_audio_menu()
        return
    end
    local new_vol = math.min((state.volume or 100) + 5, 100)
    mp.commandv("set", "volume", tostring(new_vol))
    mp.osd_message(string.format("Volume: %d%%", new_vol), 1)
    mp.commandv("script-message", "silo-volume-changed", tostring(new_vol))
    show_osc()
end

local function handle_wheel_down()
    if state.subtitle_menu_visible then
        state.subtitle_menu_offset = state.subtitle_menu_offset + 1
        render_subtitle_menu()
        return
    end
    if state.chapter_menu_visible then
        state.chapter_menu_offset = state.chapter_menu_offset + 1
        render_chapter_menu()
        return
    end
    if state.audio_menu_visible then
        state.audio_menu_offset = state.audio_menu_offset + 1
        render_audio_menu()
        return
    end
    local new_vol = math.max((state.volume or 100) - 5, 0)
    mp.commandv("set", "volume", tostring(new_vol))
    mp.osd_message(string.format("Volume: %d%%", new_vol), 1)
    mp.commandv("script-message", "silo-volume-changed", tostring(new_vol))
    show_osc()
end

-- Double-click for fullscreen (track timing of clicks)
local last_click_time = 0
local function handle_mbtn_left_dbl()
    mp.commandv("script-message", "silo-fullscreen-toggle")
end

--------------------------------------------------------------------------------
-- Property Observers
--------------------------------------------------------------------------------

local function observe_properties()
    mp.observe_property("time-pos", "number", function(_, val)
        state.raw_time_pos = val or 0
        update_media_timeline()
    end)

    mp.observe_property("duration", "number", function(_, val)
        state.raw_duration = val or 0
        update_media_timeline()
    end)

    mp.observe_property("pause", "bool", function(_, val)
        state.pause = val or false
        show_osc()  -- Show OSC on any pause state change
        render_pause_indicator()
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

    mp.register_script_message("osc-pip-state", function(val)
        state.picture_in_picture = (val == "true")
    end)

    mp.register_script_message("osc-set-subtitle-ai-available", function(val)
        state.subtitle_ai_available = (val == "true" or val == "1")
    end)

    mp.register_script_message("osc-set-marker-edit-available", function(val)
        state.marker_edit_available = (val == "true" or val == "1")
    end)

    mp.register_script_message("osc-set-marker-editor", function(kind, start_seconds, end_seconds)
        state.marker_editor_active = kind ~= nil and kind ~= ""
        state.marker_editor_kind = kind or ""
        state.marker_editor_start = tonumber(start_seconds) or -1
        state.marker_editor_end = tonumber(end_seconds) or -1
        state.dragging_marker_edge = nil
        show_osc()
        request_tick()
    end)

    mp.register_script_message("osc-clear-marker-editor", function()
        state.marker_editor_active = false
        state.marker_editor_kind = ""
        state.marker_editor_start = -1
        state.marker_editor_end = -1
        state.marker_editor_handle_rects = {}
        state.dragging_marker_edge = nil
        request_tick()
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
            if state.quality_menu_overlay then
                state.quality_menu_overlay.data = ""
                state.quality_menu_overlay:update()
            end
            if state.audio_menu_overlay then
                state.audio_menu_overlay.data = ""
                state.audio_menu_overlay:update()
            end
            if state.chapter_menu_overlay then
                state.chapter_menu_overlay.data = ""
                state.chapter_menu_overlay:update()
            end
            state.stats_visible = false
            state.subtitle_menu_visible = false
            state.quality_menu_visible = false
            state.audio_menu_visible = false
            state.chapter_menu_visible = false
        end
    end)

    mp.register_script_message("osc-set-title", function(title, subtitle)
        state.content_title = title or ""
        state.content_subtitle = subtitle or ""
    end)

    mp.register_script_message("osc-set-markers", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.intro_start = tonumber(data.intro_start) or 0
            state.intro_end = tonumber(data.intro_end) or 0
            state.recap_start = tonumber(data.recap_start) or 0
            state.recap_end = tonumber(data.recap_end) or 0
            state.credits_start = tonumber(data.credits_start) or 0
            state.credits_end = tonumber(data.credits_end) or 0
            state.preview_start = tonumber(data.preview_start) or 0
            state.preview_end = tonumber(data.preview_end) or 0
        end
    end)

    -- Host tells us whether a next episode is queued. When "true", the
    -- Next Episode button becomes eligible to show in the last 5% of the
    -- current episode OR inside the credits marker range.
    mp.register_script_message("osc-set-next-episode", function(available)
        state.next_ep_available = (available == "true" or available == "1")
        if not state.next_ep_available and state.next_ep_visible then
            state.next_ep_visible = false
            render_next_episode_button()
        end
    end)

    mp.register_script_message("osc-show-notice", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            show_notice(data.title, data.message, data.tone)
        end
    end)

    mp.register_script_message("osc-set-next-episode-detail", function(json_str)
        state.next_ep_countdown_active = false
        state.next_ep_countdown_cancelled = false
        state.next_ep_countdown_started_at = 0
        state.next_ep_countdown_remaining = 10
        state.next_ep_countdown_actions = {}
        if not json_str or json_str == "" or json_str == "null" then
            state.next_ep_detail = nil
        else
            local ok, data = pcall(require("mp.utils").parse_json, json_str)
            state.next_ep_detail = ok and data or nil
        end
        render_next_episode_countdown()
    end)

    mp.register_script_message("osc-set-translation-buffering", function(json_str)
        if not json_str or json_str == "" or json_str == "null" or json_str == "false" then
            state.translation_buffering = false
            state.translation_buffering_label = "translated"
        else
            local ok, data = pcall(require("mp.utils").parse_json, json_str)
            state.translation_buffering = ok and data and data.active == true or false
            state.translation_buffering_label = ok and data and data.label or "translated"
        end
        state.translation_spinner_frame = -1
        render_translation_buffering()
        render_pause_indicator()
    end)

    mp.register_script_message("osc-set-watch-party", function(json_str)
        if not json_str or json_str == "" or json_str == "null" then
            state.watch_party = nil
            state.watch_party_actions = {}
            state.watch_party_end_confirm = false
        else
            local ok, data = pcall(require("mp.utils").parse_json, json_str)
            state.watch_party = ok and data or nil
        end
        request_tick()
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
            state.subtitle_menu_offset = 1
        end
    end)

    mp.register_script_message("osc-set-active-subtitle", function(idx)
        state.active_subtitle = tonumber(idx) or -1
        if state.active_subtitle >= 0 then
            state.last_subtitle = state.active_subtitle
        end
    end)

    mp.register_script_message("osc-set-audio-tracks", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.audio_tracks = data
            state.audio_menu_offset = 1
            if #state.audio_tracks <= 1 then
                state.audio_menu_visible = false
                render_audio_menu()
            end
        end
    end)

    mp.register_script_message("osc-set-episode-navigation", function(series_context, previous, following)
        state.series_context = (series_context == "true" or series_context == "1")
        state.prev_ep_available = (previous == "true" or previous == "1")
        state.next_ep_available = (following == "true" or following == "1")
    end)

    mp.register_script_message("osc-set-active-audio", function(idx)
        state.active_audio = tonumber(idx) or -1
    end)

    mp.register_script_message("osc-set-chapters", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            remove_chapter_thumbnail_overlay()
            remove_chapter_thumbnail_menu_overlays()
            state.chapter_thumbnails = {}
            state.chapter_thumbnail_requested = {}
            state.chapters = data
            state.chapter_menu_offset = 1
            if #state.chapters == 0 then
                state.chapter_menu_visible = false
                render_chapter_menu()
            end
        end
    end)

    mp.register_script_message("osc-set-chapter-thumbnail", function(index, file, width, height, stride)
        local chapter_index = tonumber(index) or -1
        state.chapter_thumbnails[chapter_index] = {
            index = chapter_index,
            file = file,
            width = tonumber(width) or 0,
            height = tonumber(height) or 0,
            stride = tonumber(stride) or 0,
        }
        state.chapter_thumbnail_requested[chapter_index] = true
        request_tick()
    end)

    mp.register_script_message("osc-clear-chapter-thumbnail", function()
        remove_chapter_thumbnail_overlay()
        remove_chapter_thumbnail_menu_overlays()
        state.chapter_thumbnails = {}
        state.chapter_thumbnail_requested = {}
    end)

    mp.register_script_message("osc-set-quality-info", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.quality_info = data
            state.quality_switching = false
            if data.active_quality then
                state.active_quality = data.active_quality
            end
        end
    end)

    mp.register_script_message("osc-set-active-quality", function(tier_id)
        state.active_quality = tier_id or "auto"
        state.quality_switching = false
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
    mp.add_key_binding("mouse_move", "silo-osc-mouse-move", handle_mouse_move)

    -- Mouse button down/up
    -- mbtn_left click handled via osc-mouse-down/osc-mouse-up script messages
    -- from the host app. Do NOT also bind mbtn_left here — it would double-fire
    -- handle_mouse_down and the drag would never release.

    -- Right-click (for CC subtitle cycle backward)
    mp.add_key_binding("mbtn_right", "silo-osc-mbtn-right", function()
        handle_mouse_move()  -- update position first
        handle_mouse_down_right()
    end)

    mp.add_key_binding("mbtn_left_dbl", "silo-osc-mbtn-left-dbl", function()
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
    mp.add_key_binding("wheel_up", "silo-osc-wheel-up", handle_wheel_up)
    mp.add_key_binding("wheel_down", "silo-osc-wheel-down", handle_wheel_down)

    -- Mouse leave/enter
    mp.add_key_binding("mouse_leave", "silo-osc-mouse-leave", handle_mouse_leave)
    mp.add_key_binding("mouse_enter", "silo-osc-mouse-enter", handle_mouse_enter)

    -- Stats toggle
    mp.add_key_binding("i", "silo-osc-toggle-stats", toggle_stats)
    mp.add_key_binding("I", "silo-osc-toggle-stats-shift", toggle_stats)

    -- Subtitle delay nudge (matches mpv defaults but bound explicitly because
    -- input-default-bindings=no in the host). Z slows subs (shows them later
    -- relative to audio); X speeds them up. Each press shifts by 100 ms; the
    -- OSD shows the current absolute delay so the user can dial it in.
    local function nudge_sub_delay(delta)
        mp.commandv("add", "sub-delay", tostring(delta))
        local d = mp.get_property_number("sub-delay") or 0
        mp.osd_message(string.format("Subtitle delay: %+.0f ms", d * 1000), 1.5)
    end
    mp.add_key_binding("z", "silo-sub-delay-back", function() nudge_sub_delay(-0.1) end)
    mp.add_key_binding("x", "silo-sub-delay-fwd",  function() nudge_sub_delay( 0.1) end)

    -- Override F key — prevent mpv's default "cycle fullscreen" from firing
    -- Arrow keys: Left/Right = seek ±10s, Up/Down = volume ±5% (matching web player)
    mp.add_forced_key_binding("LEFT", "silo-seek-back", function()
        if current_keyboard_menu() then return end
        seek_relative_and_resume(-10)
    end)
    mp.add_forced_key_binding("RIGHT", "silo-seek-fwd", function()
        if current_keyboard_menu() then return end
        seek_relative_and_resume(10)
    end)
    mp.add_forced_key_binding("UP", "silo-vol-up", function()
        if move_keyboard_menu_focus("previous") then return end
        mp.commandv("add", "volume", "5")
    end)
    mp.add_forced_key_binding("DOWN", "silo-vol-down", function()
        if move_keyboard_menu_focus("next") then return end
        mp.commandv("add", "volume", "-5")
    end)
    mp.add_forced_key_binding("HOME", "silo-menu-home", function()
        move_keyboard_menu_focus("home")
    end)
    mp.add_forced_key_binding("END", "silo-menu-end", function()
        move_keyboard_menu_focus("end")
    end)
    mp.add_forced_key_binding("ENTER", "silo-menu-activate", function()
        activate_keyboard_menu_item()
    end)
    mp.add_key_binding("ESC", "silo-menu-escape", function()
        close_keyboard_surface()
    end)
    -- M = mute toggle
    mp.add_forced_key_binding("m", "silo-mute-toggle", function()
        mp.commandv("cycle", "mute")
    end)
    mp.add_forced_key_binding("M", "silo-mute-toggle-shift", function()
        mp.commandv("cycle", "mute")
    end)

    local function toggle_play_pause()
        mp.commandv("cycle", "pause")
    end
    mp.add_forced_key_binding("SPACE", "silo-play-pause-space", toggle_play_pause)
    mp.add_forced_key_binding("k", "silo-play-pause-k", toggle_play_pause)
    mp.add_forced_key_binding("K", "silo-play-pause-k-shift", toggle_play_pause)

    local function toggle_captions()
        if state.active_subtitle >= 0 then
            state.last_subtitle = state.active_subtitle
            state.active_subtitle = -1
            mp.commandv("script-message", "silo-subtitle-select", "-1")
        else
            local target = state.last_subtitle
            if target < 0 and #state.subtitle_tracks > 0 then
                target = state.subtitle_tracks[1].index or -1
            end
            if target >= 0 then
                state.active_subtitle = target
                mp.commandv("script-message", "silo-subtitle-select", tostring(target))
            end
        end
    end
    mp.add_forced_key_binding("c", "silo-captions-toggle", toggle_captions)
    mp.add_forced_key_binding("C", "silo-captions-toggle-shift", toggle_captions)

    mp.add_forced_key_binding("f", "silo-fs-override", function()
        mp.commandv("script-message", "silo-fullscreen-toggle")
    end)
    mp.add_forced_key_binding("F", "silo-fs-override-shift", function()
        mp.commandv("script-message", "silo-fullscreen-toggle")
    end)
    mp.add_forced_key_binding("p", "silo-pip-override", function()
        mp.commandv("script-message", "silo-pip-toggle")
    end)
    mp.add_forced_key_binding("P", "silo-pip-override-shift", function()
        mp.commandv("script-message", "silo-pip-toggle")
    end)
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

        update_marker_panel_drag(mx, my)

        -- Embedded libmpv sends pointer motion through this script message,
        -- not mpv's native mouse_move binding. Keep marker grips responsive
        -- on the actual desktop host path as well as standalone mpv.
        if state.dragging_marker_edge then
            update_marker_edge_drag(mx, L.seek_bar)
        end

        -- Handle seek drag
        if state.dragging_seek then
            local sb = L.seek_bar
            if sb then
                local ratio = clamp((mx - sb.x1) / (sb.x2 - sb.x1), 0, 1)
                state.seek_drag_pos = ratio
            end
        end

        -- Handle volume drag
        if state.dragging_volume then
            local vb = L.volume_bar
            if vb then
                local ratio = clamp((mx - vb.x) / vb.w, 0, 1)
                state.volume_drag_val = ratio * 100
                mp.commandv("set", "volume", tostring(state.volume_drag_val))
            end
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

    -- Canonical media timeline supplied by the native host. Progressive
    -- remuxes and copy-HLS windows begin at raw player time zero after a seek,
    -- so their offset must be added for progress, markers, and seek targets.
    mp.register_script_message("osc-set-timeline", function(offset, duration, can_seek_anywhere)
        state.timeline_offset = math.max(tonumber(offset) or 0, 0)
        state.media_duration = math.max(tonumber(duration) or 0, 0)
        state.can_seek_anywhere = can_seek_anywhere == "true"
        update_media_timeline()
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
        local hit = point_on_floating_action_button(mx, my)
        if not hit and state.current_alpha > 0.1 and L.bar_hit then
            hit = point_in_rect(mx, my, L.bar_hit)
        end
        mp.commandv("script-message", "osc-hit-test-result", tostring(hit))
    end)

    -- Host sends this after a clean quick left-click. If the point isn't on
    -- any OSC chrome (bar, menu overlay, stats panel), we toggle pause so
    -- clicking the video body works like every other media player.
    mp.register_script_message("osc-video-click", function(x, y)
        if mp.get_time() <= (state.ignore_video_click_until or 0) then
            return
        end

        local mx = tonumber(x) or 0
        local my = tonumber(y) or 0
        compute_layout()
        local L = state.layout

        -- Floating action buttons are OSC chrome even when the bottom bar is
        -- hidden. A click here must never fall through into pause toggling.
        if point_on_floating_action_button(mx, my) then
            return
        end

        -- Any menu overlay open? Let that handle its own clicks.
        if state.subtitle_menu_visible or state.quality_menu_visible then
            return
        end

        -- Click inside visible OSC bar? The regular osc-mouse-up flow already
        -- handled that element — don't toggle pause.
        if state.current_alpha > 0.1 and L and L.bar_hit
            and point_in_rect(mx, my, L.bar_hit) then
            return
        end

        -- Click on the video body — toggle pause.
        mp.commandv("cycle", "pause")
    end)
end

--------------------------------------------------------------------------------
-- Initialization
--------------------------------------------------------------------------------

local function init()
    msg.info("Silo OSC initializing...")

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
        close_marker_editor()
        -- Reset drag state from previous session (mpv instance is reused)
        state.dragging_seek = false
        state.dragging_volume = false
        state.seek_drag_pos = 0
        state.next_ep_countdown_active = false
        state.next_ep_countdown_cancelled = false
        state.next_ep_countdown_started_at = 0
        state.next_ep_countdown_remaining = 10
        render_next_episode_countdown()
        show_osc()
    end)

    -- Clean up all overlays and timers on shutdown
    mp.register_event("shutdown", function()
        if state.osc_overlay then state.osc_overlay:remove() end
        if state.stats_overlay then state.stats_overlay:remove() end
        if state.subtitle_menu_overlay then state.subtitle_menu_overlay:remove() end
        if state.quality_menu_overlay then state.quality_menu_overlay:remove() end
        if state.notice_overlay then state.notice_overlay:remove() end
        if state.skip_overlay then state.skip_overlay:remove() end
        if state.next_ep_overlay then state.next_ep_overlay:remove() end
        if state.next_ep_countdown_overlay then state.next_ep_countdown_overlay:remove() end
        if state.translation_buffering_overlay then state.translation_buffering_overlay:remove() end
        if state.tick_timer then state.tick_timer:kill() end
        if state.hide_timer then state.hide_timer:kill() end
        if state.notice_timer then state.notice_timer:kill() end
    end)

    msg.info("Silo OSC initialized successfully")
end

-- Run initialization
init()
