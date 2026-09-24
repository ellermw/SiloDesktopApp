# Watched action after playback — local 1.1.103

The completion message updated Item.UserData.Played but never the observable
IsWatched value used by the detail action. The page listened only for rating
changes, and removed its property listener on navigation without restoring it.
Its idle handler refreshed the play action, leaving watched state stale. Autoplay
also meant the final close message could refer to a successor, not the displayed
episode. These are desktop state-propagation defects.

The repair synchronizes completion into IsWatched, observes it in the page and
restores the listener on navigation. After playback closes, a targeted state
refresh waits for final writes from both retired and closing sessions. It keeps
the page mounted and reads the server's aggregate for seasons/series instead of
assuming one completed child means a completed parent. Navigation identity,
cancellation and a watched-mutation generation protect against stale responses.

Evidence:

- Four movie/episode completion cases failed before the model repair.
- The native WinUI runner against the 1.1.102 publish failed with the actual
  button still saying Mark Watched after completion.
- The same native test passes against 1.1.103, including return navigation and
  manual state changes. The existing artwork reattachment test also passes.
- 1,062 Release tests and six published playback-service checks pass.
- Clean installer packaging passed native libmpv hash/load and resource guards.
- Focused review identified autoplay and manual-mutation races; both were fixed
  and covered before the final build.

Local installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.103-Setup.exe`.
SHA-256: `455CEC0930753D64188108C2D3329F25E1BEA241DA1E3940C9A0F4C692DD28C6`.
Build/test output is retained under `.codex-tmp/watched-*`.

No running user app was closed or replaced, no production state was changed,
and no commit/push or release publication was performed. The fixture validation
does not substitute for a new installed full-series playback session.
