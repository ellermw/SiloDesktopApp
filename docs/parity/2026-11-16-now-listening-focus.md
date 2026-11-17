# Now Listening focus containment and return (official #4296)

## Upstream reference

- Repository: `https://github.com/Silo-Server/silo-server`, official `main` fetched 2026-11-16 at `5b3dcaa2fdfec6de2a537a2e10897352a1078689`.
- Ported commit: `385d907551342bd41643e9f28a136d65d15874fe` (`fix(web): keep keyboard focus inside the tour, subtitle appearance panel, and Now Listening (#4296)`), an ancestor of that `main`.
- Desktop base: `origin/beta` `212333915` (`1.2.1168-beta.1`).

Scope is only the Now Listening part (`web/src/pages/audiobooks/player/{NowListening,AudiobookPlayer,MiniBar,CoverExpandTile}.tsx`). The tour and subtitle appearance panel parts are not ported, because desktop already presents both as modal `ContentDialog`s.

## Upstream behaviour

- While Now Listening is open, it sets `inert` on every sibling of its root. This takes the covered app out of the tab order and the accessibility tree. Siblings that were already inert are skipped and are not un-inerted on close.
- On open, it focuses the collapse ("Back to player") button.
- The mini bar remounts on collapse, and its cover expand tile takes focus through `autoFocus` once Now Listening has been opened.

## Desktop behaviour

WinUI has no `inert`. The port does this:

- **Covered shell.** `NowListeningFocusScope.Open` covers every `RootGrid` sibling that paints beneath Now Listening (`Canvas.ZIndex` 55): a lower ZIndex, or an equal ZIndex earlier in document order. In practice that is `NavView` (navigation pane, header and `ContentFrame`), the Server Activity button, the loading, player and Playing Next overlays, toasts and the standalone update button.
  - Each covered element gets `AutomationProperties.AccessibilityView=Raw`.
  - Each covered `Control` gets `IsEnabled=false`. That prevents descendant keyboard focus via Tab, Shift+Tab, access keys and programmatic focus. It does not remove descendants from UIA; non-`Control` text can still be read (see the recorded deviation below).
  - The mini player (z60) and the buffering pill (z65) draw above Now Listening and are left alone. The mini player is collapsed while Now Listening is open anyway.
- **Containment.** `AudiobookNowListening` sets `TabFocusNavigation="Cycle"`, so Tab and Shift+Tab wrap inside it.
- **Open.** Focus goes to the Back control (`CollapseButton`, through `FocusCollapseButton`). The first attempt is synchronous. If the control is not focusable until layout, one low-priority retry follows.
- **Collapse (Minimized).** Focus returns to the mini player's "Open Now Listening" cover tile (`MiniPlayerBar.FocusNowListeningOpener`). Only after both the synchronous attempt and the post-layout retry fail does it try the element focused before expansion, then the first focusable element of `ContentFrame`. A page opener must not preempt the cover tile's retry.
- **Stop while expanded (Idle).** The shell is restored. With no mini player left, focus goes to the opener if it is still focusable, otherwise to the first control of the current page.
- **PiP or video takeover.** The shell is restored and focus is not moved, because that surface owns focus.
- **Inactive window.** Inertness is always applied/restored, but focus is requested only when `_isWindowActive || _playerService.IsPlaybackSurfaceForeground`, matching Playing Next. The predicate is checked again before the low-priority retry. Inactive transitions skip focus rather than queueing a target for activation.
- **Repeated expansion.** An expand while already open is a no-op: the original opener is kept and focus is not taken again. Every open/collapse cycle returns focus to the cover tile.
- **Exact, leak-free restore.** `ReversibleInertness` records each write it made and reverses them newest-first.
  - A target that was already inert is skipped.
  - A value that another owner changed while the scope was open is not overwritten.
  - A failing write does not stop the others.
  - No event handlers are attached. The opener is held through a `WeakReference`, and a generation counter drops focus retries that a later open or close has superseded.
- **Wiring.** `MainWindow.OnPlayerStateChanged` captures the opener before the mini player collapses. After every state case, it calls `SyncNowListeningFocusScope`, so open, collapse, stop, PiP and the video takeover all go through one path.
- **No motion.** No page-navigation, slide or entrance animation was added.

### Recorded accessibility deviation from `inert`

This port provides **keyboard containment, not Narrator/automation-tree containment**. `AccessibilityView=Raw` removes only the element itself from the UIA control and content views. Descendants are re-parented to the nearest included ancestor and remain reachable in those views. Disabling a `Control` does not hide its descendants, and non-`Control` elements such as page text, headings and named images are not disabled. Narrator scan mode and item navigation can therefore still announce the covered page and sidebar while Now Listening is open. This is a parity gap, not equivalent to upstream `inert`.

The native fixture explicitly records this deviation by adding covered page text and asserting that its peer remains a UIA content element while the navigation parent is Raw and disabled. The existing disabled/focusability assertions establish keyboard behavior only; they do not establish screen-reader isolation. Subtree suppression would need a separate automation-peer or dialog design and Narrator verification. Collapsing the shell is not done here because it would re-measure the whole page and navigation pane, and `MainWindow` deliberately does not hide `NavView`.

Covered non-`Control` panels (the loading and Playing Next overlay `Grid`s) can only get `Raw`, not `IsEnabled=false`. They are normally collapsed while Now Listening is open, and Tab cycling keeps keyboard focus out of them in any case.

## Files changed

- `src/SiloPlayer/Helpers/ReversibleInertness.cs` (new): the framework-free apply/restore core and the `IsCovered` layering rule.
- `src/SiloPlayer/Helpers/NowListeningFocusScope.cs` (new): the WinUI scope (Raw view, disabled controls, opener, deferred focus).
- `src/SiloPlayer/MainWindow.NowListeningFocus.cs` (new): `CaptureNowListeningOpener` and `SyncNowListeningFocusScope`.
- `src/SiloPlayer/MainWindow.xaml.cs`: `OnPlayerStateChanged` captures the opener and resyncs the scope after the switch.
- `src/SiloPlayer/Controls/AudiobookNowListening.xaml`: `TabFocusNavigation="Cycle"` and `x:Name="CollapseButton"`.
- `src/SiloPlayer/Controls/AudiobookNowListening.xaml.cs`: `FocusCollapseButton()`.
- `src/SiloPlayer/Controls/MiniPlayerBar.xaml.cs`: `FocusNowListeningOpener()`.
- `tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj`: links `ReversibleInertness.cs`.
- `tests/SiloPlayer.Tests/NowListeningFocusTests.cs` (new): unit and source tests.
- `tests/SiloPlayer.NativeRegressionTests/NowListeningFocusNativeFixture.cs` (new) and `MediaParityNativeFixture.cs`: the native case `now-listening-focus`.

## Verification

Original PR verification (before the review fixes below):

- **Unit tests:** all 7,548 pass, including `NowListeningFocusTests`.
- **`media-parity` with `SILO_NATIVE_TEST_MEDIA_ACTION_CASE=now-listening-focus`:**
  passes. Across four rounds, Tab and Shift+Tab each make 14 stops, all inside
  Now Listening. Back is focused on open, and the cover tile on collapse and
  repeated expansion. The shell and the owner's state are restored after stop.
- **`media-parity` with `volume-visible`:** passes.
- **UIA behavior seen in this WinUI version:** a disabled control whose own
  `AccessibilityView` is `Raw` keeps a peer reporting `IsEnabled=true`
  (NavigationView and the Server activity button). Such controls are still
  outside the UIA control view and their peers are not keyboard focusable. The
  fixture accepts that combination only for controls that are themselves
  `Raw`. Covered controls inside them must report disabled.

Review fixes add native cases for a page opener whose cover tile needs a retry,
inactive open/close and deactivation before a queued retry, and the recorded
UIA content-view deviation. These changes were inspected only; builds and tests
were not run during review fixing and must be run by the pipeline.

Still needs manual checks in the installed app:

- **Keyboard:** Tab and Shift+Tab never reach the sidebar or page, and
  Escape, E and Back return focus to the cover tile.
- **Narrator:** it lands on "Back to player" on an active-window open. Confirm
  the recorded limitation: scan/item navigation can still reach the covered
  sidebar and page; keyboard Tab containment does not imply Narrator isolation.
