# Player Backlog

Active player regressions to address within the next major player milestone. Items remain open until verified during real playback in the installed Windows application.

## P1 — Episodic Next Episode state appears during movie post-roll

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

Root cause is not yet confirmed. Verify session/content ownership before implementing the fix, then add regression coverage for clearing and gating all episode-only state.
