# Shell / Navigation — Current WebUI Audit

## Reference

- Source of truth: `https://github.com/Silo-Server/silo-server`
- Branch: `main`
- Verified commit: `73488d1bfaf12c2ac2bc8a24ef7e04dbddfe06a7`
- Commit subject: `feat(metadata): add original-language preferences (#526)`
- Verified: 2026-08-01
- WebUI files inspected:
  - `web/src/components/AppSidebar.tsx`
  - `web/src/components/AppSidebar.logic.ts`
  - `web/src/components/ServerActivity.tsx`
  - `web/src/components/Layout.tsx`
  - `web/src/lib/sidebarItemNavigation.ts`
  - `web/src/hooks/useImmediateSidebarCollapse.ts`
  - `web/src/app.css`

The legacy Continuum/GitLab implementation was not used.

## Status

The current desktop implementation has source-level coverage for the shared
shell/navigation contracts below and passes the full automated suite. This is
not a claim of complete visual parity: final authenticated side-by-side runtime
verification remains required on the packaged build.

## Implemented current-source contracts

- 260 px expanded sidebar and 64 px compact rail.
- 1024 px mobile breakpoint with mobile menu, search, activity, and profile
  controls.
- Pointer-hover expansion has been removed from the intended desktop behavior,
  but a packaged-build runtime regression still causes rapid open/close
  transitions while hovering ordinary page content. This remains an open P1
  shell-navigation defect scheduled for the next major milestone.
- Paired pane/route transition staging so a populated outgoing page is not
  visibly squeezed during sidebar collapse or expansion.
- 380 ms safety fallback for interrupted pane transitions.
- Suppressed stock `Frame` page animations and persistent cache for the
  high-frequency top-level pages.
- 96 px brand area with current WebUI mark/wordmark sizing and crossfade.
- 42 px navigation rows, 18 px icons, 13 px labels, 12 px radius, and current
  section-heading typography.
- Search keyboard badge, using the correct Windows chord (`Ctrl K`).
- Capability-gated Requests and Notifications entries.
- Collapsible Libraries section; compact mode continues to show library icons.
- Nested collection/section pins under each library.
- Direct hover/focus Unpin action on pinned items, including an accessible name.
- Plugin navigation category grouping, with `Other` sorted last.
- Profile menu identity, theme label, five focusable theme controls, hover/focus
  preview, profile-scoped persistence, Settings, Switch Profile, and Logout.
- Profile flyout opens above the expanded footer and to the right of the compact
  rail.
- Numeric unread notification badge while expanded, compact dot while
  collapsed, `99+` cap, notification snapshot/event handling, and profile
  filtering.
- Server-activity trigger geometry that keeps the count badge inside its
  clipping bounds, current active accent color, delayed connection warning,
  dynamic accessible name, and `99+` cap.
- Library/plugin/theme/notification shell hydration is generation-owned and
  canceled during server or profile changes, preventing old-account UI from
  leaking into the active shell.
- Back navigation through UI, `Alt+Left`, mouse XButton1, Escape/controller Back
  where appropriate, and selected-item synchronization for cached routes.

## Intentional native extension

- `Downloads` remains a desktop-only top-level destination because offline
  downloads are a native-client feature and the current WebUI has no equivalent.
  It is intentionally additive and is not being removed as part of WebUI parity.

## Native implementation differences

- The WebUI uses compositor transforms while the desktop uses a native
  `NavigationView`. The desktop stages route commitment until the pane reaches
  its target width, preventing the outgoing page from being resized under the
  transition. This implements the same visible goal through the native control.
- The WebUI shows a Command-key search hint on macOS-oriented layouts. The
  Windows app correctly shows `Ctrl K`.

## Remaining runtime verification

Verify the packaged Windows build against the authenticated WebUI at the commit
above:

1. Expanded browse sidebar geometry, spacing, labels, footer, and activity badge.
2. Browse-to-detail collapse and Back-to-browse expansion without page flashes,
   stale selection, or visibly squeezed content.
3. Compact and expanded sidebar state changes only through the explicit toggle;
   pointer hover and page navigation do not alter the state.
4. Profile flyout placement in expanded and compact states.
5. Theme preview by pointer and keyboard, cancel on leave, and persistence after
   reopening the app.
6. Libraries collapse/expand, nested pin navigation, and direct Unpin.
7. Notification count/dot behavior during a real notification event.
8. Mobile shell behavior immediately below and above 1024 px.
9. Profile/server switching with no old libraries, pins, themes, plugin links,
   or notification counts appearing in the new shell.

## Deferred player issue

The intermittent loss of OSC controls after automatic episode advance is tracked
as an open P1 in `PLAYER-BACKLOG.md`. Per the milestone policy, it remains queued
for the next dedicated player/OSC milestone rather than being mixed into this
shell/navigation release.

## P1 - Sidebar opens and closes on pointer hover

Originally reported July 28, 2026 against QA build 1.1.78. Re-reported July 31,
2026 against what the user believes is the newest installed QA build. Confirm
the installed executable version before reproduction; do not assume it is
1.1.81 merely because that is the newest locally built installer.

### Observed behavior

- Moving the pointer across the desktop shell rapidly expands and collapses the
  navigation sidebar.
- The content viewport repeatedly changes width, causing cards, text, and page
  layout to shift while the user is navigating.
- Sidebar state feels transient rather than controlled by the user.
- The regression can be triggered by hovering objects in the page content, not
  only by crossing the sidebar itself.

### Required behavior

- Sidebar state is changed only by the explicit open/close control.
- A closed sidebar remains closed until the user opens it.
- An open sidebar remains open until the user closes it.
- Pointer enter, pointer leave, focus, selection, and navigation between pages
  must not expand or collapse the sidebar.
- Hovering the compact rail may show normal item tooltips, but it must not
  change the rail width or page layout.
- The chosen sidebar state must remain stable across Home, library, search,
  detail, and other end-user routes.
- Content must not shift merely because the pointer crosses the sidebar.

### Required next-major-milestone verification

- Repeatedly sweep the pointer between the sidebar and content in both states.
- Navigate among Home, libraries, search, and every media-detail type.
- Exercise profile, notification, activity, library, and nested-pin controls.
- Verify mouse, keyboard, controller, and touch input.
- Verify fullscreen-player exit and application relaunch state handling.
- Compare wide, ultrawide, and narrow window layouts without reintroducing
  hover-driven resizing.

Source status: corrected in 1.1.81 source and directly exercised in the unpackaged
1.1.81 runtime across Home, movie detail, and series detail with the pane both
open and closed. The user-visible reproduction was then traced to the installed
`C:\Program Files\Silo Desktop Player\SiloPlayer.exe`, which is still version
1.1.78. Keep this issue open until a fresh packaged build replaces 1.1.78 and
the installed executable passes the full matrix above.
