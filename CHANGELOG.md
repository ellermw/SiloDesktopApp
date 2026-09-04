# Changelog

Historical release notes for Silo Desktop Player. The current installer and project status are documented in [README.md](README.md).

## 1.1.94 (user-only desktop client)

- Removed the desktop Admin shell, routes, pages, view models, dialogs, server-management API clients, and admin-only data contracts. Server administration now remains exclusively in the Silo WebUI.
- Preserved only the approved permission-gated media maintenance actions: Match/Fix Match for movies and series, plus Quick/Complete Refresh Metadata; removed the in-player marker editor and its write endpoints.
- Replaced the native first-run server setup wizard with a focused handoff that opens the connected server's WebUI and can retry once setup is complete.
- Matched the current WebUI missing-source playback behavior: a selected unavailable source receives one playback attempt, displays the WebUI's terminal 404 message with Go Back, and invalidates stale watch-prefetch data so a later manual Play obtains a fresh server plan.
- Removed desktop-only file cycling; valid alternate-version selection remains the responsibility of Silo's playback planner, matching the WebUI.
- Coordinated direct-stream byte-range reconnection with the playback watchdog so a recoverable upstream idle event is not prematurely escalated to remux. Upstream media requests now negotiate HTTP/2 with HTTP/1.1 fallback, and route-recovery deferral remains explicitly bounded.
- Audited against official Silo Server `main` commit `7c1cb2d3f34e7a37d2864b63735386300e81ef31`.
- Verified 788 passing tests, a zero-warning x64 Release build, native libmpv loading and hash validation, a successful installer build, and CodeRabbit's zero-issue review of PR #6.
- Installer SHA-256:
  `F486FB9301D7674A971D16289A4B37F76209E0F0E7A7BB9AF71246923BC22CE9`.

## 1.1.93 (user-facing browse and detail milestone)

- Reconciled mounted Home and library recommendation sections in place so
  background refreshes retain usable content instead of rebuilding the page.
- Kept Search input focused, canceled superseded work, rejected stale-query
  results, and separated optional discovery work from the first media results.
- Aligned shared poster, landscape, and virtual-library cards with the audited
  WebUI action set, progress geometry, episode state, touch/long-press policy,
  and personal-source presentation.
- Added bounded detail prefetching and expanded movie, series, season, episode,
  person, audiobook, ebook, and manga behavior, including navigation-safe
  watched-state and translation updates.
- Routed additional artwork and light/dark server branding through the byte and
  disk cache rather than retaining expiring presigned URLs.
- Hardened ebook reader origins, external browser URLs, server URL identity,
  and isolated QA-instance naming without changing production single-instance
  behavior.
- Audited the milestone against official Silo Server commit
  `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca`. Official `main` advanced to
  `b29aaf94cc4d05083230a59388a35e9ee8cbd49e` during release packaging; those
  later WebUI changes remain part of the next parity-drift pass.
- Verified 964 passing tests, a zero-warning x64 Release build, native libmpv
  loading and hash validation, a successful installer build, and CodeRabbit's
  zero-issue re-audit of PR #5.
- Installer SHA-256:
  `C305CF9A8065606F895FE7CE35E5CE9898810AA09CDFCB5B2CA1C4172A3B1625`.

## 1.1.92 (player reliability and session lifecycle release)

- Matched the current WebUI protocol-v3 recovery chain with bounded,
  deduplicated attempted-plan history and typed terminal server decisions.
- Added realtime `plan_invalidated` handling with stale-plan protection and a
  serialized background recovery path that does not block WebSocket commands.
- Replaced expired sessions after progress-reporting failures instead of
  reopening dead stream URLs, while preserving playback position and pause
  intent.
- Kept terminal recovery failures inside the active player and added an
  input-safe Retry/Exit overlay rather than abruptly returning to Home.
- Restored OSC visibility after autoplay and stream replacement, and moved
  fullscreen reconciliation entirely onto the UI thread.
- Centralized current WebUI playback-failure descriptions and preserved
  specific source-missing and transcoding-policy guidance.
- Revalidated against official Silo Server commit
  `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca`.
- Verified 863 passing tests, a zero-warning x64 Release build, native libmpv
  loading and hash validation, a successful installer build, and CodeRabbit's
  zero-issue re-audit of PR #4.
- Installer SHA-256:
  `9DEE4460C94EBD486A872B5E6D3906ADC072B49AE6456104905A798E4E63C0C6`.

## 1.1.91 (Home refresh stability release)

- Replaced overlapping Home-page realtime reloads with a classified,
  debounced refresh path and cooldown.
- Reconciled existing Home sections and media items in place, avoiding page
  teardown and preserving stable card presentation while progress, watched
  state, and newly scanned content update.
- Refreshed the featured hero when item-only reconciliation changes its source
  collection, including the snapshot behavior identified by CodeRabbit.
- Added regression tests for refresh gating, in-place reconciliation, and hero
  refresh behavior.
- Corrected the Windows App Runtime installer hook's Pascal formatting so the
  current Inno Setup compiler accepts the audited packaging change.
- Verified 829 passing tests, a clean CodeRabbit review, native libmpv loading
  and hash validation, and a successful x64 installer build.
- Installer SHA-256:
  `6723DB1233FC03363F2034ED4ABBD43C20B8FE868C67B4941C9BB561AB535276`.

## 1.1.90 (current-server parity and playback hardening release)

- Expanded current Silo API contracts for invitations, household onboarding,
  UI customization, diagnostics, hardware acceleration, notifications,
  settings, collections, and playback protocol v3.
- Added invitation claiming, household setup, admin diagnostics and command
  palette surfaces, account/settings workflows, recipe and feature-tour
  dialogs, and richer collection and metadata actions.
- Hardened direct-stream proxy and relay recovery, native playback capability
  negotiation, session replacement, audio/subtitle handling, chapter
  thumbnails, player overlays, and literary-media playback and reading flows.
- Refreshed navigation, search, shared cards, item details, recommendations,
  requests, notifications, calendar, and admin surfaces with expanded
  interaction and current-source parity coverage.
- Replaced application artwork and updated the installer to register native
  `silo:` invitation links.
- Verified 787 passing tests, a zero-warning x64 Release build, native libmpv
  loading and hash validation, and a successful self-contained installer
  build.
- Installer SHA-256:
  `79E35357660FFB515F0FAFB60C0D7702A2F497E27B7ED63718562F56BB3ABF65`.

## 1.1.85 (Search input alignment QA hotfix)

- Corrected the 56 px Search field's visibly top-aligned text and oversized
  lower gap by giving the WinUI text presenter explicit balanced vertical
  padding instead of relying on `VerticalContentAlignment` alone.
- Retained the 1.1.84 Search navigation fix and single persistent input.
- Removed self-signed Authenticode signatures from the normal QA build path
  after runtime Code Integrity evidence showed Smart App Control assigning
  those signatures signing level 1 and blocking them, while the same freshly
  built unsigned installer and app launched with zero Code Integrity blocks.
- Self-signed packaging now requires the explicit
  `-AllowSelfSignedSignatures` override; a publicly trusted certificate remains
  supported normally.

## 1.1.84 (Search navigation hotfix QA build)

- Fixed Search failing to open after installing 1.1.83. The persistent search
  field now stays in one WinUI visual parent instead of being removed and
  appended to another children collection during `Frame.Navigate`.
- Preserved the single-input focus, caret, 100 ms debounce, relevance ordering,
  query-scoped filters, and incremental result behavior introduced in 1.1.83.
- Added a regression assertion that forbids reparenting the active SearchBox.

## 1.1.83 (search responsiveness and regression-polish QA build)

- Revalidated Search behavior against public Silo Server GitHub `main` commit
  `c1cac4ece9f4a95e1c555305ca9dd29b6cedc292`.
- Matched the current WebUI's 56 px search field and compact rounded scope
  selector without WinUI's distorted extreme-radius geometry.
- Replaced the split empty/results text boxes with one persistent native input,
  preserving focus, caret, selection, and keyboard/IME state as results open.
- Made relevance the explicit default sort for text searches; exact and strong
  title matches now use the server's indexed search provider instead of the
  desktop-only date ordering.
- Stopped an empty, whole-catalog facet request from competing with the first
  search; filter metadata now loads lazily and is scoped to the active query
  and media type.
- Added privacy-safe Search timing diagnostics that record latency and result
  counts without recording query text, authentication data, or credentials.
- Publishes each result page as one collection update and starts optional
  outside-library discovery only after local results are visible.
- Retained the late-progress episode resume correction and the autoplay OSC
  ownership/input-state fixes for installed direct-play, remux, and transcode
  verification.

## 1.1.82 (user-facing regression stabilization QA build)

- Revalidated the active work against public Silo Server GitHub `main` commit
  `73488d1bfaf12c2ac2bc8a24ef7e04dbddfe06a7`.
- Fixed late-progress episodes and in-flight rewatches starting at 0:00 when
  the server returned both a nonzero resume position and `played: true`.
- Preserved the explicitly chosen desktop sidebar state across navigation and
  application relaunch; pointer hover and page selection remain unable to
  change it.
- Fixed a startup-time Library filter crash caused by XAML firing a ComboBox
  selection event before the full advanced-filter panel existed.
- Retained the pending packaged-runtime fixes for autoplay OSC restoration,
  opaque subtitle menus, chapter thumbnails, Playing Next close behavior,
  synchronized fullscreen transitions, card play-hover styling, and bounded
  activity badges.

## 1.1.81 (persistent desktop sidebar QA fix)

- Made the explicit desktop sidebar toggle the only state-changing input; pane
  lifecycle events can no longer reinterpret a navigation-driven close as a
  user preference.
- Added a desktop pane-state invariant that immediately reconciles WinUI's
  display mode and open state after framework property changes.
- Prevented movie, series, season, episode, and person detail navigation from
  collapsing or briefly reopening the sidebar.
- Preserved an explicitly collapsed sidebar while navigating through Home,
  libraries, search, and media-detail pages.
- Runtime-verified both expanded and collapsed navigation paths in the rebuilt
  Windows application.

## 1.1.80 (deterministic user-controlled sidebar QA fix)

- Removed pointer-hover expansion and pointer-exit collapse from the desktop
  sidebar.
- Removed route-driven pane changes, so opening movie, series, season, episode,
  person, Home, library, and search pages no longer changes sidebar width.
- Made the explicit desktop pane toggle the sole authority for open/closed
  state and preserved that selection across navigation, player transitions,
  profile flyouts, and temporary Admin-shell ownership.
- Corrected the desktop NavigationView mode from always-expanded `Left` to
  `LeftCompact`, preventing WinUI from reopening a collapsed pane during Home
  navigation and then visibly closing it again.
- Applied the opaque, theme-aware Silo sidebar brush to both NavigationView pane
  modes and paired the open state with stable `Left` mode and the closed state
  with `LeftCompact`, preventing navigation-triggered auto-dismissal while
  preserving normal explicit toggle behavior.
- Kept narrow-window navigation overlay behavior isolated from the desktop
  sidebar preference.

## 1.1.78 (Home and media-detail parity QA build)

- Re-audited Home, movie, series, season, and episode detail surfaces directly
  against the live WebUI and public Silo Server GitHub `main` commit
  `63e18cf37f2a42c08320c06245f9223d74a40a37`.
- Fixed Home and detail-card overlays initially rendering built-in defaults
  instead of the selected profile's server configuration.
- Applied server-defined overlay order, current per-corner limits, and the
  WebUI's poster/wide-card edge spacing across every shared card surface.
- Corrected movie and episode rewatch progress so a nonzero saved position
  remains resumable even when the historical watched flag is still set.
- Matched the current pre-play subtitle selector's readable language/format
  labels, Embedded/External/Downloaded grouping, and saved series overrides;
  removed the obsolete duplicate Add subtitles row.

## 1.1.77 (autoplay OSC lifecycle QA build)

- Fixed intermittent loss of the on-screen controls after an automatic episode
  advance without returning to the browsing shell.
- Made same-state episode transitions explicitly restore the native video popup
  and Lua OSC instead of relying on a later fullscreen transition.
- Reset stale mouse capture, hover, drag, and transport-menu state when the
  reusable playback surface returns from Playing Next.
- Claimed the successor content transition before retiring the outgoing episode
  state and rejected stale queued post-roll presentations, preventing a final
  old-episode position event from hiding the new episode's controls.
- Retained accurate fullscreen restoration and avoided restarting, seeking, or
  changing pause state solely to recover the control surface.
- Revalidated against public Silo Server GitHub `main` commit
  `5d6f6323d514faed4edbf0222839ce03a32067ae`.

## 1.1.76 (shared shell/navigation parity QA build)

- Re-audited the shared shell directly against public Silo Server GitHub `main`
  commit `5d6f6323d514faed4edbf0222839ce03a32067ae`.
- Matched the current 260/64 px sidebar states, row/icon typography, brand
  crossfade, compact library rail, profile-flyout placement, keyboard-focusable
  theme controls, and direct pinned-item removal behavior.
- Staged browse/detail route commitment with native pane transitions so the
  populated outgoing page no longer visibly squeezes, flashes, or reloads while
  the sidebar changes width.
- Added realtime, profile-filtered notification counts with expanded numeric and
  compact-dot states; corrected server-activity badge clipping, active color,
  and accessibility text.
- Canceled and generation-owned profile/server shell hydration to prevent stale
  libraries, pins, plugins, themes, and notification counts from leaking across
  account changes.
- Retained Downloads as an intentional native desktop extension while matching
  the current WebUI everywhere the two shells share functionality.
- Logged intermittent OSC loss after automatic episode advance as an open P1 for
  the next dedicated player/OSC milestone.

## 1.1.75 (player milestone closure QA build)

- Revalidated the player contract against public Silo Server GitHub `main` commit `5d6f6323d514faed4edbf0222839ce03a32067ae`.
- Required the authoritative `/watch/{id}` payload to report `type: "episode"` before episode navigation or Playing Next can appear, adding a final movie post-roll safety boundary.
- Expanded transition coverage across episode→movie, episode→different series, movie→episode, and movie→movie ownership changes.
- Closed the reported 4K OSC typography regression after installed real-playback confirmation.

## 1.1.74 (QA package launch-policy fix)

- Corrected the executable file and assembly versions so the package no longer ships changed binaries that still identify themselves as `1.1.72.0`.
- Added a trusted RFC 3161 timestamp to every locally signed application binary and to the installer.
- Configured Inno Setup to timestamp-sign its generated uninstaller as part of compilation instead of leaving `unins000.exe` unsigned.
- Preserved the existing multi-file, self-contained Windows package and all 1.1.73 player changes.

## 1.1.73 (player content-isolation and 4K OSC QA build)

- Re-audited episode and movie controls against the signed-in live WebUI and public Silo Server GitHub `main` commit `271a2e1741e1c1737d54f9fae00c466883363c0d`.
- Bound Previous Episode, Next Episode, credits countdown, and Playing Next metadata to the content ID that produced them, preventing a completed episode or late asynchronous lookup from publishing episodic actions into a movie session.
- Reset the reusable mpv/Lua episode context synchronously at every content transition and defensively on every file load.
- Enlarged the bottom-left title, season/episode label, and current/total time responsively on true 4K playback surfaces without globally inflating subtitle, quality, chapter, or playback-info menus.
- Adopted the current server/WebUI media-timeline contract so seeks before an exposed HLS/remux window trigger a transport re-anchor instead of being silently clamped to the window start.

## 1.1.72 (startup regression QA fix)

- Fixed the WinUI startup crash introduced by declaring the controller B button as a XAML `KeyboardAccelerator`; WinUI could not parse `GamepadB` while constructing the main window.
- Preserved controller Back behavior through a runtime routed-key handler, while keeping the XAML startup surface limited to values the WinUI loader can construct.
- Added a source regression guard that rejects another `GamepadB` XAML accelerator.

## 1.1.71 (player reliability and WebUI parity QA build)

- Re-audited the player against public Silo Server GitHub `main` commit `02203d9e408c8f53ecbf8adf672d84d434180389` and the live WebUI player.
- Matched the current WebUI OSC hierarchy, labels, utility order, episode-navigation slots, subtitle tools, playback information, keyboard/controller focus, and Playing Next behavior.
- Fixed chapter thumbnails so each asynchronously decoded image repaints the already-open Chapters menu instead of leaving a permanent placeholder.
- Kept Search Online, Translate with AI, subtitle Appearance, and marker editing over the active playback surface while preserving stream, position, pause state, tracks, fullscreen state, and pointer isolation.
- Made subtitle, audio, and quality changes preserve the user’s playing/paused state and honor the server’s authoritative stream origin, timeline offset, and transport-local start.
- Reworked incomplete copy-HLS seeking so every manual seek reanchors the server transport; this prevents replaying the initial keyframe segment before playback continues.
- Hardened the Playing Next close button against the owned native video HWND by moving/hiding the video surface before exposing WinUI controls and accepting close on pointer-down, Escape, or controller Back.
- Preserved actual fullscreen state across dialogs, post-roll, automatic episode changes, and repeated fullscreen/windowed transitions without taskbar activation or stale button state.
- Confirmed native session identity requests report `Silo for Windows`, omit a version suffix, and remain outside the server’s Jellyfin/JF client classification.
- Passed 695 automated regressions and a zero-warning x64 Release build before packaging.

## 1.1.70 (local QA build)

- Made video fullscreen/windowed transitions snap as one compositor update instead of visibly resizing the native video and WinUI shell on separate timelines.
- Unified keyboard, XAML, and native OSC fullscreen commands on the mpv playback popup; the main application window now retains its existing geometry behind playback.
- Prevented owner-window resize events from pulling a fullscreen video surface through intermediate window sizes.
- Disabled DWM owned-window transition animation for the dedicated playback surface and discarded stale pre-resize client pixels.

## 1.1.69 (local QA build)

- Completed the shared shell/navigation milestone against public Silo Server GitHub `main` commit `02203d9e408c8f53ecbf8adf672d84d434180389`.
- Fixed the Playing Next close button by placing it above the overlay scroller's hit-test surface and routing close/Back behavior through one cancellation path.
- Synchronized the selected sidebar route after direct navigation, Back, dynamic library/pin rebuilds, plugin routes, and returns from Admin or playback.
- Added the current WebUI's narrow-window header and drawer behavior, including Silo branding, Search, admin activity, playback-settings access, and Calendar scroll-direction auto-hide.
- Added server profile artwork to the desktop sidebar with initial fallback and suppressed duplicate username text exactly as the current WebUI does.
- Matched selected navigation foregrounds and notification badge theme colors, and delayed realtime connection warnings for four seconds to avoid transient reconnect flicker.
- Added Alt+Left, mouse Back, and controller Back navigation without stealing input from expanded/fullscreen playback.
- Kept the package multi-file and clean-upgrade compatible. This build has not been published.

## 1.1.68 (local QA build)

- Matched the live WebUI's `bg-black/90` player-menu surfaces so subtitle and quality labels remain readable over bright or visually busy video.
- Kept Subtitle, Audio, and Chapters menus above the seek timeline's complete pointer target at windowed and fullscreen sizes.
- Added the current WebUI's 150 ms hover expansion to the compact 64 px detail-page sidebar, including keeping it expanded while the profile flyout is open and restoring the normal 260 px sidebar on browse routes.
- Retained the 1.1.67 WebP chapter-thumbnail decoder correction and added regression coverage for the expanded menu/shell behavior. This build has not been published.

## 1.1.67 (local QA build)

- Kept the subtitle selector completely above the seek timeline's interactive region, including in shorter windowed playback layouts, so the menu cannot obscure or click through to the seek control.
- Restored chapter-menu preview images for the server's current WebP artwork by decoding cached image bytes independently of the cache's generic `.img` filename extension.
- Added regression coverage for both subtitle-menu geometry and the chapter-thumbnail decode/overlay path. This build has not been published.

## 1.1.66 (local QA build)

- Re-verified the native player controls directly against the signed-in live WebUI and public Silo Server GitHub `main` commit `02203d9e408c8f53ecbf8adf672d84d434180389`.
- Kept Search Online, Translate with AI, and subtitle Appearance inside the active playback surface, preserving the session, timestamp, selected tracks, fullscreen state, and prior playing/paused state.
- Hardened subtitle search/download cancellation, prevented concurrent subtitle downloads, and restored keyboard/controller focus to the subtitle button after nested dialogs close.
- Fixed bitmap-subtitle transport replacement so text/bitmap track switches wait for the replacement stream's real load boundary and do not spuriously pause, seek, or expose stale frames.
- Corrected Playback Info source/video/audio bitrate units, sample-rate formatting, and Color range to match the current WebUI contract.
- Retained the recent fullscreen, picture-in-picture, auto-next focus, rapid-seek, direct-stream reconnect, quality-switch, and playback-error recovery work in the locally signed multi-file QA package. This build has not been published.

## 1.1.65 (local QA build)

- Re-audited Home, Search, Library, and movie/series/season/episode detail surfaces against public Silo Server GitHub `main` commit `02203d9e408c8f53ecbf8adf672d84d434180389` from July 26, 2026.
- Matched the current Search empty state, 56 px prominent field, responsive 32–56 px results title, 1400 px page shell, vertical rhythm, and unified Media/Audiobooks/All scope control.
- Preserved the active query and search-field focus while changing the query or scope.
- Added WebUI-style removable active-filter badges and an active count on the Filters action, including visible restoration of retained Genre, Content Rating, Resolution, and Country state.
- Kept “Explore all” conditional: it appears only for browse-supported sections whose server `total_count` exceeds `item_limit`, exactly like the current WebUI.
- Built as the existing locally signed, multi-file QA package. This build has not been published.

## 1.1.64 (test build)

- Re-audited the active end-user surfaces against the public Silo Server GitHub `main` branch at commit `a0507c78eb5a6caf8d91fb23836ee0376ccdddda` from July 22, 2026.
- Fixed Search so typing retains keyboard focus, stale or optional discovery work cannot replace the active query, and late background completion no longer causes a delayed whole-page refresh.
- Hardened large-library and catalog navigation with bounded realization, deferred artwork, cancellation, and incremental updates to keep very large libraries responsive.
- Refined Home and item-detail behavior, including stable incremental Home rows, preserved carousel state, episode-detail metadata, and correct still artwork for the current episode carousel.
- Corrected native video-window ownership and fullscreen/next-episode restoration so the mouse and OSC remain interactive after automatic episode transitions.
- Improved direct/remux/transcode startup and recovery behavior, playback session metadata, and realtime teardown.
- Reports the stable native identity `Silo for Windows` without a release number. The companion Silo Server/WebUI change classifies first-party native clients, displays a branded `Silo` badge, and omits their IP from Activity rows while retaining the purple `JF` badge for Jellyfin-compatible clients.
- Kept the QA package as a signed multi-file installation and verified installation, saved-session restoration, Home startup, real playback, OSC interaction, and clean return from playback on Windows 11.
- Current shared-shell/sidebar, activity-badge, hover-state, and navigation-transition parity work remains in progress and is not represented as complete.

## 1.1.55 (test build)

- Fixed a fullscreen/post-roll next-episode transition bug where the native mpv video window could remain in a stale fullscreen/z-order state after auto-playing the next episode, leaving the video above the interactive mouse/OSC layer.
- Captured fullscreen intent before Playing Next/post-roll hides or shrinks the native video surface, exits stale native fullscreen before hiding, and restores fullscreen intentionally after the next episode starts.
- Reset native cursor visibility during post-roll and next-episode restore so the mouse remains visible and player controls stay clickable after automatic episode transitions.
- Published the QA installer as `SiloInstaller-Windows-x64.exe` for GitHub Releases with SHA-256 `A49C3060F1D0AE79BD39DC136E610CE0CF4A1B5B13B80CDFD3D3F05F2DC58FC0`.

## 1.1.54 (local QA build)

- Preserved Home carousel scroll position through incremental background refresh, progress updates, and dismissals.
- Avoided unnecessary Hero carousel restarts when refreshed data describes the same active slide.
- Added pre-play subtitle menu parity refinements for Auto/Off, empty subtitle states, and Add Subtitles behavior.
- Refined detail-page trailer overlay visibility, media-info formatting, and fallback back-navigation behavior.
- Corrected library header overlay evaluation after returning to already-scrolled hero layouts.

## 1.1.53 (test build)

- Refreshed the parity reference against the current public Silo Server GitHub `main` branch at commit `0a914441ea54d02ffc7bcdd24f5b8e3b8353d06a`.
- Continued the end-user parity pass with Home carousel edge-fade polish, compact Taste Seed typography behavior, and detail-page spacing refinements for Media Locations.
- Tightened movie/show/episode detail parity by matching the current WebUI crew-role rows, preserving selected episode centering, and correcting version-selector sort and container fallback display.
- Preserved the recent playback-control hardening work for in-player subtitle/search/translation surfaces, subtitle-state changes, fullscreen synchronization, and mouse/OSC interactivity during playback.
- Restored authenticated navigation pane expansion after login/profile restore so the sidebar does not strand itself in a clipped compact state.
- Replaced one hard-coded detail-date formatter with the shared user date-preference formatter and hardened source tests for environments without `git.exe`.
- Added private-use/all-rights-reserved project licensing language for the now-private GitHub repository.

## 1.1.52 (test build)

- Fixed the installed Search crash caused by compiled XAML attempting to assign nullable request-provider poster URL strings directly to `Image.Source`; all remaining direct nullable poster bindings now use a validated image-source converter.
- Removed Search's duplicate custom clear buttons and retained the TextBox's single native clear control.
- Made the primary catalog request start after the 100 ms typing debounce without waiting for settings/filter warmup, and publish local results without waiting for optional outside-library discovery.
- Isolated slow request-provider discovery behind a six-second bound with stale-query ownership checks, so it can populate later but cannot delay, overwrite, or crash primary results.
- Added focused Search runtime regressions. The complete suite now passes 604 tests with a zero-warning x64 build.

## 1.1.51 (test build)

- Fixed Search, Home-return, library, catalog, and collection navigation failures caused by browse pages referencing a removed XAML button style; sidebar navigation failures are now contained and logged instead of damaging the remaining navigation session.
- Matched the current WebUI's quality-switch cache isolation by assigning every HLS transport a unique manifest URL and explicitly bypassing stale playlist caches.
- Kept the outgoing transport paused until mpv confirms the replacement file is loaded, preventing old/pre-seek frames from leaking into a quality change and replaying the opening transcode segment.
- Added dedicated navigation, HLS cache, and transport-replacement regressions. The complete suite now passes 601 tests with a zero-warning x64 build.

## 1.1.50 (test build)

- Completed the integrated native-player hardening pass against public Silo Server commit `b96e359b4ebe3e6aea68ce327d180578bf0b04d0`, covering direct play, progressive remux, HLS fallback, byte-range recovery, file-load deadlines, progress keepalive recovery, premature EOF, and teardown.
- Kept audio as the stable playback clock across application focus changes, added automatic deinterlacing, bitrate-aware buffering, D3D11 hardware decode, HDR output hints, and safe passthrough handling for high-bitrate HEVC/HDR/Dolby Vision and lossless audio workflows.
- Matched the current WebUI player transport layout and active controls, including distinct previous/next episode actions, curved 10/30-second controls, responsive HUD geometry, exact fullscreen/PiP state, keyboard/menu dismissal, marker editing, rich track/quality menus, playback information, notices, post-roll, and watch-party surfaces.
- Added profile/device-effective intro, recap, and credits auto-skip through the same transport-aware seek path, with per-media reset and watch-party host authority.
- Prevented stale reload workers and subtitle-window races during rapid quality, version, audio, and bitmap-subtitle switches; external subtitles now load on demand instead of delaying startup.
- Added a real bundled-libmpv/Lua initialization test, transient CDN status recovery for direct/HLS startup, and repaired repository-root discovery in older parity tests. The complete suite now passes 597 tests with a zero-warning x64 build.

## 1.1.49

- Re-audited the active native video HUD against public Silo Server commit `b96e359b4ebe3e6aea68ce327d180578bf0b04d0`, including current transport order, gradients, icons, hover states, notices, and first-frame behavior.
- Added current recap skipping alongside intro and credits actions, corrected the floating skip pill's geometry and interaction states, and kept episode navigation visually distinct from timed seeking.
- Made every open player surface reflow immediately during window/fullscreen changes and forced fullscreen/PiP icons to repaint on the exact host state transition.
- Rebuilt playback notices and playback information around the current WebUI bounds, wrapping, tints, responsive height, wheel scrolling, and click isolation.
- Delayed loading dismissal until mpv's first rendered playback restart event instead of hiding it at file-open time.
- Added direct-stream upstream-idle recovery so a hung CDN read resumes from the last delivered byte instead of leaving high-bitrate playback permanently paused.
- Isolated automated-test logs from installed runtime diagnostics and expanded regression coverage to 567 passing tests with a zero-warning x64 build.

## 1.1.48

- Matched the current WebUI audiobook behavior with remembered per-book playback rates, 0.5–3× controls, keyboard rate shortcuts, WebUI skip intervals, sleep-timer extension, and live countdowns.
- Corrected ebook/reader format resolution for server containers, ZIP/RAR packages, and `.fb2.zip`/FBZ files while retaining the current EPUB/PDF/MOBI/AZW/AZW3/CBZ/CBR/FB2/FBZ contract.
- Rebuilt Media Info from a text dump into current-style multi-version General, Video, Audio, and Subtitle spec sheets, including audio stream profiles such as Dolby TrueHD + Dolby Atmos.
- Changed Split Versions to automatically debounce dry-run previews, reject stale preview responses, show reattribution impact inline, and use one final action instead of a manual review/confirmation chain.
- Applied the user's date and 12/24-hour preferences across admin libraries, activity, logs, autoscan, tasks, users, nodes, requests, policy history, subtitles, maintenance, and Playing Next.
- Fixed an Admin Libraries provider-chain empty-state edge and re-audited the release against public Silo Server commit `b96e359b4ebe3e6aea68ce327d180578bf0b04d0`.
- Expanded regression coverage to 553 passing tests with a zero-warning x64 build.

## 1.1.47

- Added the current WebUI's QR/device-authorization login flow, locally generated QR codes, public server branding, and cancel-safe polling without exposing credentials to a third-party QR service.
- Kept favorites, watchlist, watched state, and playback progress synchronized in place across active Library and Catalog cards without reloading or losing scroll position.
- Updated Settings History Import for the current Watchlist and Favorites counters and continued the source audit against public Silo Server commit `b96e359b4ebe3e6aea68ce327d180578bf0b04d0`.
- Fixed focus-loss video acceleration by keeping audio as mpv's stable playback clock, and made direct-play stalls first reopen the existing byte-range session before falling back to a slower replacement-session request.
- Verified selected-version detail metadata against the current WebUI contract, including multipart duration and selected-file resolution, HDR/Dolby Vision, and best audio badges.
- Expanded regression coverage to 532 passing tests with a zero-warning x64 build.

## 1.1.46

- Re-audited the desktop app against current public Silo Server commit `b96e359b4ebe3e6aea68ce327d180578bf0b04d0` from July 16, 2026.
- Expanded current Home, Library, Catalog, Collections, Calendar, Search, Recommendations, Requests, Notifications, Settings, Watch Party, and admin route contracts and presentation.
- Hardened direct/remux/HLS playback startup, prefetched watch data, session recovery, realtime token refresh, track switching, subtitle inventory, remote volume behavior, and current cinema-control geometry.
- Removed whole-page navigation flashes, retained populated top-level routes during refresh and return navigation, fixed the sidebar's stranded compact state, and coalesced expensive collection/card rebuilds.
- Expanded regression coverage to 466 passing tests with a zero-warning x64 build.

## 1.1.45

- Fixed the Home-page XAML failure after profile selection by replacing the undefined `AccentSubtleBrush` reference with the application theme's defined accent-background token.
- Kept a successfully restored authentication/profile session alive if a destination page fails to construct instead of discarding its rotated refresh token and forcing another login.
- Added an automated audit requiring every Home `StaticResource` reference to resolve from Home or the application theme; the release passes 452 tests.

## 1.1.44

- Fixed the profile-selection transition so Home is created before supplemental navigation-shell work begins; theme dots, profile decoration, plugin links, library pins, and player prewarming can no longer block login.
- Removed background-thread mutation risk from the sidebar library collection and kept library/pin hydration on the owning UI dispatcher.
- Added a safe Home fallback if first-run taste setup cannot be opened, plus redacted stage-specific navigation diagnostics in `%LOCALAPPDATA%\SiloPlayer\navigation_errors.txt`.
- Replaced the oversized exception/stack-trace dialog with a concise recovery message and expanded the regression suite to 451 passing tests.

## 1.1.43

- Fixed upgrade session restoration so saved refresh credentials survive server URL formatting changes and are considered across all saved servers by most-recent use.
- Added bounded retries for transient startup refresh/bootstrap failures without deleting a still-valid saved session.
- Enforced an authenticated-shell invariant: library names, pinned collections, plugin routes, and the navigation pane are removed and cannot reappear until both authentication and profile selection succeed.
- Prevented concurrent desktop instances from racing rotating refresh tokens, and configured installer upgrades to close the old running version before launching the replacement.
- Added secret-safe startup authentication diagnostics and upgrade/security regressions; the release passes 449 tests and a zero-warning x64 build.

## 1.1.42

- Re-audited the application against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe`, including current catalog, home, collections, settings, admin, item-detail, and player contracts.
- Added current audiobook grouping/cards, literary-media detail contracts and actions, edition/version selection, trailers/extras, item split and marker workflows, and centralized current permission rules.
- Reworked library virtualization, filter construction, deferred artwork, cancellation, and UI-stall diagnostics to keep large libraries responsive during hard scrolling.
- Hardened playback sessions, WebSocket teardown, progress/EOF handling, chapter thumbnails, quality/audio/subtitle menus, live AI subtitle translation, watch parties, credits countdown, post-roll, On Deck, and the current multi-section playback-info overlay.
- Updated loading, buffering, error, subtitle-delay, quality-label, and marker-editor behavior toward the current WebUI.
- Expanded regression coverage; this release passes 379 tests and a zero-warning x64 build.

- Re-audited Settings, Plugins, Nodes, API Keys, Maintenance, Access Groups, and Devices against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` and the signed-in live WebUI.
- Replaced the obsolete Devices card list with the current fleet console, including saved views, facet filters, grouping pivots, grouped rows, and an in-place detail editor.
- Aligned Access Group cards and editor toggle rows, API-key page geometry and pagination, and Maintenance job counts/result formatting with their current WebUI counterparts.
- Expanded parity regression coverage; the release passes 268 tests and a zero-warning x64 build.

## 1.1.41

- Re-audited Autoscan, Scheduled Tasks, Subtitles, Marker History, Recommendations, Users, Playback History, and History Import against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` and the signed-in live WebUI.
- Rebuilt Subtitles from stacked desktop cards into the current ten-column management table with provider filters, language/uploader filters, relative dates, item links, and edit/download/delete actions.
- Added the missing recommendation provider presets and live embedding connection check, plus current job, lock, schedule, and advanced configuration geometry.
- Corrected Users with the current Access Groups entry point, Created column, sortable headers, and retained user/history/invite actions.
- Added Playback History manual refresh alongside safe polling and updated History Import to display the real server URL and `has_admin_token` state with current discovery/mapping controls.
- Expanded parity regression coverage; the release passes 258 tests and a zero-warning x64 build.

## 1.1.40

- Re-audited Admin Activity, Logs, Collections, Sections, and Requests against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` and the signed-in live WebUI.
- Rebuilt Activity around the current User / Stream / Playback / Node / Time / Actions table, including grouped container/video/audio delivery summaries and current inline actions.
- Fixed Logs so playback-session and FFmpeg filters consistently restart the live WebSocket stream, serialize reconnects, and cannot reopen after leaving the page.
- Added stable skeleton loading, parallel requests, and latest-selection-wins guards to Collections and Sections, eliminating blank/empty flashes and unnecessary sequential waits.
- Replaced the obsolete single-list Media Requests admin page with the current Queue, Settings, Integrations, and User Overrides experience, including per-target fulfillment failures and plugin-backed routing configuration.
- Expanded source-parity regression coverage; the release passes 240 tests and a zero-warning x64 build.

## 1.1.39

- Re-audited Admin Dashboard against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` and the live WebUI.
- Added WebUI-style client/version badges to Now Playing cards and client context to Recent Activity.
- Corrected episode cards to show the episode title with `Sx · Ex — Series` underneath.
- Added paused poster treatment, neutral link colors, exact Activity/Scan Line icon geometry, and richer live scan phase/progress summaries.
- Added immediate Dashboard skeletons for stats, Now Playing, libraries, and users so navigation paints useful structure before network requests finish.

## 1.1.38

- Fixed the overlapping top-right activity indicator by ensuring only the Admin shell owns that control while Admin pages are active.
- Restored the current WebUI's Stale External IDs diagnostic by updating the desktop client to the current public Silo API route.
- Prevented the Libraries table from appearing blank during its initial request by rendering WebUI-style loading rows immediately.
- Corrected the Add/Edit Library editor's 768 px layout, section rail, type-card grid, and right-aligned footer actions.
- Aligned library-row scan, metadata refresh, mount verification, edit, delete, and guarded empty-root actions with the current WebUI order and labels.

## 1.1.37

- Reworked Admin Libraries as a complete surface: live scan and metadata activity now occupy WebUI-style full-width rows beneath each library, while empty-root warnings remain in their own block below normal rows.
- Rebuilt Add/Edit Library General with visible seven-type cards, the WebUI Enabled setting, active section-rail states, matching dialog headers/actions, and validation that keeps the editor open and focuses the invalid section.
- Removed the multi-second blank Libraries page by rendering its primary table immediately and loading diagnostics, providers, and refresh-job data independently.
- Corrected Dashboard scan snapshots/events, active-scan copy, amber status state, stop controls, recent activity refresh, stat-card radius, and header action geometry.
- Fixed Trakt 24-hour counters that incorrectly displayed zero because numeric-suffix JSON fields were not mapped to the server contract.
- Corrected Server Activity's false disconnected marker when it loads after the shared event channel is already connected, and matched the WebUI's 36px trigger and 18px badge geometry.

## 1.1.36

- Corrected the visibly broken top-right server-activity control with the WebUI pulse icon, red count badge, and stable geometry instead of a Windows-version-dependent font glyph.
- Updated Cinema Dark's stale muted-text token to the current WebUI value, improving navigation, subtitles, metadata, and secondary button text across the application.
- Fixed Admin Libraries initial loading so Unmatched Items, Troubleshooting, Stale External IDs, and refresh-job data are present in the first rendered page instead of arriving after the only rebuild.
- Fixed the realtime scan snapshot/event subscription so per-library scan queues and progress rows appear and update like the WebUI.
- Replaced desktop-only move arrows with actual drag-and-drop library ordering, corrected action order/icons, tightened panel radii, and fixed header wrapping/button glyphs.

## 1.1.35

- Began the live Chrome-to-desktop parity workflow and corrected Admin Sections against the signed-in current WebUI rather than treating functional coverage as visual completion.
- Added server-provided admin wordmark and dynamic server naming, while retaining bundled Silo branding as the offline fallback.
- Matched the Sections table's current title scale, labels, row density, recipe names, badges, drag ordering, gallery entry point, and 512px right-side editor geometry.
- Added the WebUI editor field order, Featured helper card, Enabled card, stacked footer actions, audiobook/ebook scopes, and Watching/Listening recipe configuration.
- Rebuilt the Dashboard Trakt summary into the WebUI's three-column footer instead of the compressed desktop-only sentence.
- Sections and Dashboard remain explicitly visual-parity-incomplete until version 1.1.35 is installed and compared side by side with the live WebUI.

## 1.1.34

- Replaced the remaining Admin Collections create/edit dialog with a dedicated full-page workspace inside the Admin route.
- Added WebUI-style back navigation, source-aware editor titles, change-source handling, a wide form surface, persistent save/cancel actions, and visible save errors.
- Preserved collection artwork upload, multi-library assignment, group placement, source configuration, scheduling, visibility, and featured controls.

## 1.1.33

- Replaced the desktop-only Admin Collections table with the current WebUI's library-section and scoped group-board structure.
- Added poster rows, library/count/source/visibility/featured metadata, conditional sync actions, current empty states, and compact WebUI-style actions.
- Restored the actual Browse Templates entry point and added the WebUI source-type chooser for smart/manual, MDBList, TMDB, Trakt, and template flows.
- Kept Collections marked visual-parity-incomplete: its dedicated full-page create/edit/import workspace still needs the next parity pass.

## 1.1.32

- Rebuilt Admin Logs page geometry, typography, tabs, filters, tables, and playback-summary layout against the current WebUI source.
- Added the missing disconnected-stream Reconnect action.
- Replaced the desktop-only inline log detail panel with the WebUI-style light-dismiss 640px right-side detail sheet.
- Preserved live application/audit streaming and added usable cursor loading beyond the WebUI's current server-side-only cursor notice.

## 1.1.31

- Reworked Admin Activity against the current WebUI desktop layout: page width, 52px title, 24px rhythm, red live badge, compact refresh/realtime header, and 20px summary/IP surfaces.
- Matched the current six-column stream table proportions, row gutters, header spacing, and viewport-relative scroll height.
- Preserved the current client/profile/IP presentation, expandable playback decisions, session controls, logs, and FFmpeg inspection from 1.1.29.
- Activity remains marked visual-parity-incomplete until a live side-by-side screenshot pass is available.

## 1.1.30

- Removed the desktop-only Admin header strip and repositioned Server Activity to match the WebUI's floating desktop control.
- Rebuilt the shared Admin sidebar footer to use the WebUI build-card and Back to App structure.
- Corrected Dashboard page width, gutters, vertical rhythm, title/subtitle scale, card radii, action icons, number formatting, and stream-card geometry.
- Corrected Libraries page width, gutters, title/subtitle scale, table surface/row/action sizing, and unified all collapsed diagnostics with the WebUI icon/count/chevron presentation.
- Dashboard, Libraries, and Activity remain explicitly marked visual-parity-incomplete until side-by-side validation is complete.

## 1.1.29

- Updated Admin Activity to the current session contract, including client identity and playback-position fields.
- Expanded search and IP lookup behavior to match the current WebUI.
- Added playing/paused position presentation, expandable container/video/audio decision details, hardware transcode mode, and preserved inline FFmpeg inspection and session controls.
- Removed Claude workspace and instruction artifacts from the public repository and added ignore rules to prevent them from returning.

## 1.1.28

- Rebuilt Admin Dashboard loading so stats, sessions, libraries, and users render independently instead of waiting for the slowest request.
- Added current Trakt activity metrics and accurate movie/show file counts.
- Added active library scan state, progress, scan-to-stop behavior, working navigation links, manual refresh feedback, and active-page refresh.
- Completed the current Admin Libraries milestone introduced in 1.1.27.

## 1.1.27

- Updated Admin Libraries for the current plugin provider-chain contract.
- Replaced the incorrect local Windows folder picker with the remote Silo server filesystem browser.
- Added current metadata fields, manga type, provider defaults, poster actions, stateful scan/refresh controls, immediate Scan All feedback, server-wide unmatched search, and collapsed diagnostics.
