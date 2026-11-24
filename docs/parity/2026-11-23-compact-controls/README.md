# Desktop player and OSC comparison

Compared against [official Silo Server main at 28533796d](https://github.com/Silo-Server/silo-server/tree/28533796da8fa1e8a8045c718b6953503222695f/web/src/player). The responsive rules come from the [official HUD stylesheet](https://github.com/Silo-Server/silo-server/blob/28533796da8fa1e8a8045c718b6953503222695f/web/src/styles/player-hud.css). Headless Edge rendered the official React components with local media data, the English catalog, default text size and device scale 1. The desktop comparison used the published x64 app, its real WinUI menus and its embedded libmpv renderer. Beta base was `dd48c63ae` (1.2.1677-beta.1).

## Results

| Surface | Result |
| --- | --- |
| Settings | The trigger shows the current resolution where it fits. At 960 px it shows **4K** and the **1.5×** badge. At 640 px the badge remains. The menu uses the [native Settings panel from #284](https://github.com/ellermw/SiloDesktopApp/pull/284), including the speed and quality choices. |
| Compact transport | Both players place a 60 px play button at (450, 460) in a 960×540 client and at (290, 150) in a 640×360 client. The centered button accepts a click at the new position. Skip labels use the configured interval. |
| Touch transport and menus | Play moves into the letterbox dock on tall surfaces and back into the row on short surfaces. The native sheet supports portrait, landscape, scrolling, wrapped speed choices, closing, and reopening with a mouse. Tests used synthetic pointer input. |
| Volume and More | The wide slider has a 96×44 target above mute. The native HUD fixture drags it through both endpoints and checks keyboard input. Compact mouse puts volume in More. Touch uses device volume. Empty audio and chapter inventories are omitted. |
| Audio, chapters and speed | Audio row titles are covered separately by [#285](https://github.com/ellermw/SiloDesktopApp/pull/285). Chapters refreshes a row when its thumbnail arrives. File replacement retires a stale speed menu. Native tests exercise real selections and ownership changes. |
| Subtitles | The menu hands off to Appearance, Add Subtitles and AI Translate. Text opacity now sits with the text controls and both opacity fields accept percentages. The appearance body follows the viewport height. Online search results and translations depend on server capabilities. |
| Conditional player surfaces | Compared loading, ordinary buffering, catching up, reconnecting, pause feedback, credits and Up Next. Ordinary buffering uses the smaller indicator and labeled stalls center the indicator and label together. The open touch menu covers the top chrome. |
| Font rendering | libass sizes the bundled Plus Jakarta Sans using its Windows ascent plus descent (1652 units for a 1000-unit em). The HUD compensates for this. The bold instance was generated from the bundled OFL variable font at weight 700 with FontTools 4.58.5, `instantiateVariableFont(..., updateFontNames=True)`. |

## Visual evidence

The Settings pair uses two source files, Auto plus two streaming tiers, and normal playback speed. The native popup uses the Windows acrylic backdrop where supported, with the WebUI's 93% dark tint. The official panel measured 352×425.5 CSS pixels and the final desktop panel measured 352×426 client pixels. The browser capture crops to the panel. The WinUI capture renders the menu root, so its compositor shadow and the video behind it are outside the image. [Official Settings](web-settings.png) and [desktop Settings](desktop-settings.png).

The compact captures use the same sample title, navigation, quality, speed, elapsed time and viewport size. Screen coordinates are client pixels. The fixture media background and the displayed finish time can differ. Native window shadows, text antialiasing and focus visuals use the Windows renderer.

| Viewport | WebUI | Desktop |
| --- | --- | --- |
| 960×540 | [WebUI](web-960.png) | [Desktop](desktop-960.png) |
| 640×360 | [WebUI](web-640.png) | [Desktop](desktop-640.png) |

## Submission verification

The final Windows x64 publish at `1bba5afb8` passed 461 focused tests and the native Quality, touch Settings and menu geometry suites. The preceding publish also passed the real HUD and More suites. The final product change adjusted four pixels of popup header spacing.

## Integration verification

On local integration build `985abb885`, the source included the proposed changes from [#280](https://github.com/ellermw/SiloDesktopApp/pull/280), [#283](https://github.com/ellermw/SiloDesktopApp/pull/283), [#284](https://github.com/ellermw/SiloDesktopApp/pull/284) and [#285](https://github.com/ellermw/SiloDesktopApp/pull/285). All 679 focused tests passed, as did nine native suites covering the HUD, seek previews, Settings, subtitles and timing, chapters, More, Speed, touch Settings and Audio. The Audio suite exercised 27 cases. The seek test checked the actual decoded tile color and the Skip Recap bounds after resizing the player from 1440×900 to 960×540 and then 640×360. The Settings comparison used an additional fixture response for a saved speed of 1×.

Additional local captures and logs are in `D:/SiloPlayer-build/parity-nov23/osc-compact-touch`. The reference captures include the main menus, subtitle dialogs, Playback Info and Watch Party with synthetic data. Separate native runs covered Watch Party membership and host actions, subtitle dialog ownership, and the shared appearance editor saving successive opacity edits. Physical touch, a live multi-device Watch Party and a physical picture-in-picture transition remain unverified.
