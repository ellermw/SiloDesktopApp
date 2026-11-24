# Player controls and menu parity, November 23

Official Server main was `cce98c5fc3e6301232471fc851c6f6bd6ff3e8e5`. The player components and HUD stylesheet at that revision have the same contents as the reference for PR #284. The Desktop baseline was Beta `f8645a675`. For the remaining layout differences below, the comparison uses the **proposed** controls in #284 at `af4701a1d`. The audio-title change builds independently on Beta. [Official player](https://github.com/Silo-Server/silo-server/tree/cce98c5fc3e6301232471fc851c6f6bd6ff3e8e5/web/src/player/components) · [HUD stylesheet](https://github.com/Silo-Server/silo-server/blob/cce98c5fc3e6301232471fc851c6f6bd6ff3e8e5/web/src/styles/player-hud.css) · [Desktop PR #284](https://github.com/ellermw/SiloDesktopApp/pull/284)

## Differences to act on

| Order | Surface | Observation and acceptance check |
| --- | --- | --- |
| 1 | Audio track title | Fixed in this change. With language `ja`, title `AAC (Advanced Audio Coding)`, and two channels, the official Audio row starts with **Japanese** and shows AAC and stereo below. The native fixture verifies the visible and spoken row title. Keep authored names such as “Director’s commentary” and “1”, and keep the codec title when language is missing. [Official Audio capture](web-audio-1280.png) |
| 2 | Settings button | The WebUI keeps the 1.5× badge throughout the tested widths. At 960 px it can show **4K** and **1.5×** together. At 640 px the badge remains when the resolution disappears. The Lua layout in #284 suppresses both for every compact width. Its Settings icon draws a ring and eight separate rays. The official icon is a continuous gear outline. Check 1× and 1.5× at both widths, including the surrounding buttons’ click areas. [960 px](web-osc-960.png) · [640 px](web-osc-640.png) |
| 3 | Compact play and skip buttons | With a mouse and default text size, the WebUI keeps a 60 px play button on the bottom row at 960×540. At 640×360 its center is at (320, 180), over the picture. Desktop uses a 40-unit play button along the bottom in both compact cases. Check pointer and keyboard activation after crossing the 48rem breakpoint in both directions. [960 px](web-osc-960.png) · [640 px](web-osc-640.png) |
| 4 | Touch menus and dock | The WebUI chooses its compact controls by pointer capability as well as width. With a coarse pointer at 460×700, Settings spans all 460 px, touches the bottom edge, has a grab handle and close button, and wraps speed choices into two rows. Desktop positions its menu window above the opener. Reuse the existing menu contents and selection handlers. Check the sheet, outside-tap dismissal, scrolling, rotation, and switching back to mouse input. [Touch Settings capture](web-settings-460.png) |

## Checks to finish after integration

| Surface | What to compare |
| --- | --- |
| Full controls and primary menus | With the same title, language, audio layout, source versions, quality, speed, accent and time, capture 1920×1080 and 1280×720. Compare Settings, Audio, Subtitles, Chapters, More and Watch Party, in pointer-open and keyboard-open states. Include a long list and a single choice. Capture the whole video window so the panel edge, shadow, anchor and focus ring are visible. |
| Volume | At 1152 px the WebUI puts a 96×44 slider target immediately above the 44×44 mute button on hover. Move from mute to slider, drag through both ends, tab to it, and leave the group. Compare visibility, value and mute state on native. |
| Submenus and conditional overlays | Open subtitle Appearance, Search, Translate and Timing where available, Playback info and Watch Party actions. Compare the parent-to-child transition and return focus. Exercise loading, buffering, reconnecting, skip markers, next episode and credits on matching content. Earlier captures cover some native states. Each still needs its corresponding WebUI capture. |
| Overlapping changes | Exercise seek-preview thumbnails and Skip Recap with the combined Beta control bar, including a player resize. [Seek previews](https://github.com/ellermw/SiloDesktopApp/pull/280) · [Skip Recap](https://github.com/ellermw/SiloDesktopApp/pull/283) |
| Outstanding native failures | Rerun chapter-thumbnail decoding and speed-setting reread, both previously observed before #284. Compare the audio test’s outer-window resize with the measured video client size on unchanged Beta. The passing audio-title case exercises the row before that resize. |

## Evidence

The reference fixture mounted the official React PlayerControls with the official CSS, fonts and English catalog in headless Edge, using a synthetic media inventory, default text size and device scale 1. Captures covered 1920×1080, 1280×720, 1152×648, 960×540 and 640×360 with a mouse, plus 460×700 with emulated touch. The controls and menus rendered locally. Physical touch, full VideoPlayer overlays, live playback and the combined Beta build remain to be compared.

The audio change passed a Windows x64 publish, 24 focused tests, and the native Audio bitrate case. That case renders a real menu from a synthetic session and now captures the selected Japanese row. Local evidence is in `D:/SiloPlayer-build/parity-nov23/osc-audio-label-audit`. For each remaining fix, record the same content and input state on both players and retain the native after-capture alongside its reference.
