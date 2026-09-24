# Player Backlog

## P1 — September 5 progressive-stream buffering/recovery stalls

Reported on installed 1.1.100 (e1ba550). Read-only diagnosis; no playback
interruption, server mutation, production code change, or packaging performed.

Latest observed episode: episode-tvdb-379169-1-3, file 775321. Direct started at
22:47:26; relay resumed at five offsets from 1,106,935,969 through 5,472,879,512.
Successive failures followed another 1055.7, 1030.4, 1029.4, 1039.5, 1064.3 MiB.
At 23:21:11 mpv buffered; 23:21:42 stall recovery began; progressive remux
loaded at 23:21:47. That relay failed at 23:28:49; mpv buffered at 23:29:20;
the stall detector fired at 23:29:50; remux HLS loaded at 23:29:56 and advanced
30 seconds by 23:30:26. Prior episode followed the same route ladder. This
supports the reported repeated progressive failures and HLS recovery, not a
claim that the server's file is corrupt or that the full root cause is proven.

Confirmed client weaknesses:

- DirectStreamRelay forwards open-ended ranges, holding large responses open
  until completion or failure instead of issuing bounded byte ranges.
- DirectStreamProxy uses an infinite HttpClient timeout. RelayAsync's
  ResponseHeadersRead SendAsync has only session cancellation; the configured
  upstream idle timeout protects body reads, not connection/header acquisition.
- Reproduced this timeout gap twice with real production relay source and a
  deterministic fixture: 4/8 media bytes delivered, second request waits on
  headers, 50ms upstream timeout never fires, outer 2s safety cancellation exits
  with failure. Harness is local-only .codex-tmp/relay-stall-diagnostic.
- Terminal relay logs omit inner read errors/status/validator details, so the
  exact large-response termination and a previous entity-change rejection
  cannot be assigned a cause from current evidence. The shared mpv log was
  overwritten by an unrelated GPU probe; it is not tonight's viewing trace.

Proposed scoped correction pending prioritization: bounded sequential byte-range
fetches for direct media (unchanged bytes/codecs, no forced transcode), bounded
header acquisition/reconnect, and safe diagnostic context for terminal failures.
Tests must cover continuous output, offsets/seeking, validator consistency,
ignored ranges, header/body stalls, cancellation, and bounded work. Preserve
real entity-change protection. Progressive remux cannot use source byte-range
stitching; keep its recovery path separate. Verify against an extended live
stream before declaring the user's buffering resolved.

Player regressions completed in the 1.1.74/1.1.75 player milestone. New
regressions should be added here with their own reproduction and acceptance
evidence.

## P1 — Selected English audio still plays Korean in Tomb Raider King

Reported September 4, 2026. User explicitly prioritized an immediate fix and
approved the focused audio-selection correction. Implemented for QA 1.1.100;
runtime track-selection checks passed, awaiting audible user confirmation.

### Reported reproduction

1. Open **Tomb Raider King** from the **Anime** library and play an episode.
2. The file has two audio tracks: Korean first, English second.
3. The app automatically shows English selected, but the audible track is Korean.
4. The user reports that it continues playing the first track regardless of selection.

Confirmed from the actual local playback trace: episode-tvdb-452039-1-1,
file 18495166, v3 DirectProgressive. mpv repeatedly selected Korean aid=1 while
the OSC announced source index 1 (English; available as aid=2). The v3 audio
replan reloaded the original file without applying the selected audio locally;
the legacy direct-selection branch was bypassed by the v3 branch.

The load-completion path now maps the source audio ordinal through mpv's actual
audio inventory, applies it, and verifies aid before restoring play/pause.
Packaged transports select their delivered audio instead of reusing the source
ordinal. Each new load resets stale aid selection. Missing/rejected selection
surfaces an error rather than silently continuing with the default language.

Verification: the original log reproduced three English-OSC/Korean-mpv
mismatches. Nine focused tests cover original/packaged selection, non-contiguous
native IDs, missing/rejected selection, and silent video; all 837 x64 Release
tests passed. Official upstream reference: 658be10eb03615f104790fba0431a4d19fd02d15,
playback-protocol-v3 original_http/client_selected_audio_track_v1 contract.

September 4 runtime QA (separate 1.1.100 process, real episode via normal OSC):

- Automatic English on resume: sourceIndex=1, mpvAid=2, two source audio tracks.
- Manual Korean while paused: sourceIndex=0, mpvAid=1; remained paused at 6:00.
- Manual English while playing: sourceIndex=1, mpvAid=2; playback continued.
- 720p-high quality: TranscodeHls, sourceIndex=1, mpvAid=1, one delivered track.
- Return to Original: DirectProgressive, sourceIndex=1, mpvAid=2; remained paused.
- mpv's own log confirms active --alang=en for aid=2, not merely the OSC label.
- Not audibly verified by the agent; remux and episode-autoplay cases have not
  been runtime-tested in this focused pass. Remux mapping has unit coverage.
- Existing quality-menu behavior briefly resumed the outgoing paused stream
  before restoring pause after loading, and showed generic 'Quality' for the
  transcode. Keep those separate from this audio-selection fix for player work.

QA installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.100-Setup.exe`.
SHA-256: `40168FBF973D5BC2032DE71D8B5A8464CBA41B2191C8D0D7C232F306F9290D9B`.
Built with the existing multi-file packaging workflow, no Windows security or
certificate changes. Published binaries launched and played during QA; installer
installation itself was not repeated. No commit, push, or PR was performed.

### Diagnosis and acceptance checks

- Reproduce automatic English preference and manual Korean/English switching.
- Compare the selected file's track metadata, playback start/switch request,
  server response, mpv track list and actual selected mpv audio ID. Distinguish
  server stream indices from mpv track IDs rather than assuming they match.
- Confirm audibly that English selection plays English and Korean plays Korean;
  the OSC label alone is not sufficient verification.
- Check direct play, remux and transcode where available; repeat after resume
  and automatic episode advance. Preserve timestamp and playing/paused state.
- Add a regression at the real failing seam once the cause is established.


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
# Initial-interruption diagnosis follow-up (September 5–6)

See [the controlled playback-stall investigation](2026-09-05-playback-stall-diagnosis.md).
Two playback-paced HTTP reads outside the relay/mpv failed at ~1.099 GB. An
independent curl read failed at ~1.090 GB with 2.895 GB missing. Sequential
32 MiB ranges passed 1,200 MiB at the same pace (38 requests, 400 seconds).
The likely long-response buffering/write-timeout interaction is now supported
by a live reproduction; exact deployed edge configuration remains unverified.
Do not describe relay removal alone as the proven fix. No application or server
configuration change was applied during this investigation.
