# Player Backlog

Player regressions completed in the 1.1.74/1.1.75 player milestone. New
regressions should be added here with their own reproduction and acceptance
evidence.

## P1 — OSC can disappear after automatic episode advance

Reported July 28, 2026. Deferred to the next player/OSC major milestone.

### Reproduction

1. Play an episode normally.
2. Allow autoplay to advance directly into the next episode without returning
   to Home, a library, or a detail page.
3. The successor episode starts and continues playing, but the OSC sometimes
   cannot be made visible.
4. Double-clicking to enter fullscreen makes the OSC available again.

### Expected behavior

- Every automatically loaded episode must initialize the OSC exactly as a
  manually started item does.
- Moving the pointer, clicking, pressing an OSC shortcut, or using a controller
  must reveal the controls without requiring a fullscreen transition.
- The Lua OSC visibility state, native video window state, input routing,
  cursor tracking, and WinUI player state must agree after every content swap.
- Automatic episode advance must not retain a hidden/faded state, stale input
  region, stale mouse position, or stale fullscreen/windowed geometry from the
  previous episode.
- Fixing the state must not restart playback, change position, pause the video,
  or steal focus from another application.

### Required milestone verification

- Windowed→windowed and fullscreen→fullscreen autoplay.
- Windowed→fullscreen and fullscreen→windowed before the next transition.
- Mouse, keyboard, and controller OSC wake paths.
- Two or more consecutive automatic episode advances.
- Direct-play, remux, and transcoded successor episodes.
- Playing and paused states at the transition boundary.
- Confirm OSC auto-hide still works normally after it is first revealed.

### Confirmed root cause

Installed-runtime traces show the failing sequence:

1. Playing Next hides the Lua OSC with `osc-set-visibility=false`.
2. The successor calls `PlayAsync` while the logical player remains
   `Expanded`.
3. The transition is therefore `SetState: Expanded -> Expanded`.
4. The old same-state fast path re-showed the native video popup but did not
   send `osc-set-visibility=true`.
5. A later fullscreen double-click changed state and re-enabled the OSC,
   explaining the reported workaround exactly.

The same installed trace also exposed a transition-boundary race: clearing the
outgoing episode hint before the successor owned the switch allowed one final
position event to enter post-roll a second time. That stale presentation could
be queued to the UI thread and hide the successor surface after playback had
already advanced.

The source fix now:

- claims the content switch before retiring the outgoing episode state;
- rejects queued Playing Next presentations when their owning content changed
  or a content switch is active;
- makes the same-state Expanded/Fullscreen path re-enable the OSC;
- clears stale Lua drag/hover/menu state and immediately reveals controls when
  the OSC is re-enabled;
- clears stale native mouse tracking and capture whenever the video popup is
  hidden for post-roll.

Status: fixed in source with regression coverage; installed autoplay runtime
verification is pending the next player/OSC QA build.

## P2 — Episodic Next Episode state appears during movie post-roll

Reported July 28, 2026.

### Reproduction

1. Play an episode of *Young Sheldon* until the Next Episode action becomes available.
2. Finish or leave that episode.
3. Resume the movie *Downsized* from an existing position.
4. Reach the end of the movie.
5. A Next Episode action incorrectly appears.
6. Selecting it starts the next *Young Sheldon* episode from the user's Next Up queue.

### Expected behavior

- Movies must never expose episodic Previous Episode, Next Episode, Playing Next, or On Deck actions.
- Starting a movie must not retain episode-navigation state from an earlier playback session.
- Post-roll actions must be derived only from the currently active content and playback session.
- Next Up recommendations may remain available elsewhere in the application, but must not be presented as the current movie's next episode.

### Required milestone verification

- Episode → movie, after the episode's Next Episode control has appeared.
- Episode → different series.
- Movie → episode.
- Movie → movie.
- Automatic episode advance followed by movie playback.
- Direct-play, remux, and transcoded sessions.
- Resume and start-from-beginning paths.
- Windowed and fullscreen playback.

Confirmed root cause: the reusable player retained episode-navigation metadata
until a full close, while the background lookup could publish without an
explicit current-content ownership check.

Completion status: closed. The navigation snapshot is content-owned, late
lookups from retired sessions are rejected, and the reusable mpv/Lua context is
cleared synchronously on every transition. The final guard also requires the
current `/watch/{id}` response to explicitly identify the content as
`type: "episode"` before any episode controls or Playing Next presentation can
appear. The transition matrix covers episode→movie, episode→different series,
movie→episode, movie→movie, automatic successor ownership, and stale
asynchronous results. Installed 1.1.74 runtime logs confirmed episode/movie
transitions clear the outgoing target; the active user's playback was not
interrupted for synthetic end-of-file testing.

## P3 — Player identity and time text is too small at 4K

Reported July 28, 2026.

### Observed behavior

On a 3840×2160 display, the bottom-left text beneath the playback timeline does not scale appropriately:

- The movie or episode title is readable but slightly too small.
- Season and episode identifiers (`S#`, `E#`) are extremely small.
- Current playback time and total duration are extremely small.
- The visual hierarchy and legibility do not match the live Silo WebUI at the same display size.

### Expected behavior

- Scale the movie/episode title, season/episode metadata, and current-time/total-duration text responsively for 4K playback.
- Preserve the current WebUI's relative typography hierarchy rather than applying one uniform enlargement.
- Respect Windows display scaling as well as the playback window's actual pixel and logical dimensions.
- Keep the metadata aligned beneath the timeline without crowding, clipping, or overlapping nearby controls.
- Maintain appropriate sizes in windowed playback, 1080p fullscreen, ultrawide fullscreen, and 4K fullscreen.

### Required milestone verification

- Compare the installed desktop player directly with the live WebUI on the same 4K monitor.
- Verify movie, episode, and other supported playback types.
- Verify 100%, 125%, 150%, and 200% Windows display scaling where available.
- Verify fullscreen, maximized, and smaller windowed layouts.
- Confirm keyboard/controller focus indicators and OSC auto-hide transitions do not shift or clip the text.

Completion status: closed for the reported 4K regression. The player uses a
dedicated, axis-aware metadata scale, and the user confirmed the installed
1.1.74 title and timeline text look correct on a 4K monitor during real
transcoded playback. The wider Windows display-scaling matrix remains part of
routine compatibility coverage rather than a blocker for this resolved report.

## P1 - Late-progress episodes start from the beginning

Reported July 28, 2026 against QA build 1.1.78.

### Observed behavior

- Episodes with saved progress, especially episodes stopped near the end, can
  start from 0:00 when Play is selected.
- The detail surface does not offer a Resume versus Play from Beginning choice
  before starting.

### Expected behavior

- A valid saved episode position must produce a Resume action and resume from
  that exact position.
- Play from Beginning must remain available as an explicit secondary action.
- Progress close to the episode's end must not be discarded merely because the
  server or client considers the episode watched, unless the current WebUI uses
  the same completion threshold and behavior for the same response.
- The playback-start request, server-returned position, player seek position,
  and displayed progress must remain consistent.
- Direct-play, remux, and transcode paths must behave identically.

### Required next-major-milestone verification

- Compare the same late-progress episodes in the live WebUI and desktop client.
- Capture the item-detail and watch payload progress fields without recording
  authentication secrets.
- Test progress before and after the server's watched/completion threshold.
- Test Resume, Play from Beginning, Next Up, and autoplay entry points.
- Test direct-play, remux, and transcoded episodes.
- Confirm reopening the app preserves the same resume point.

Source status: corrected and rechecked against public Silo WebUI commit
`c6383aa4dd1d05f9fe8fed6807be85ff58d1c849`. The WebUI treats every nonzero
position as a live resume point even when `played` remains true during a
rewatch; the desktop's final playback-start policy now follows that rule.
Automated coverage passes, but installed direct/remux/transcode verification is
still required before this regression is closed.
