# Continuum Desktop — Change Tracking Workflow

This doc describes how the desktop app tracks parity against the live `continuum-server` WebUI + API so that when the server updates, we can diff efficiently and port changes accurately.

## Why

Early on, the desktop was audited against a **frozen snapshot** of the WebUI (`continuum-webui-ref/` under the desktop repo). That snapshot drifted out of date within days — the server team added Watch Together, CardOverlay settings, WebhookSync settings, ActivateDevice, Calendar, Theme Editor, Plugin Settings, Profiles Settings, and a 998-line rewrite of the Import tab. Our desktop rebuild was matching 373-line-old code while the live server ran 998-line-new code, and whole features/modes were missing.

This workflow prevents that by always auditing against a **live clone** of the server repo, tagged with the exact commit SHA we last audited.

## Single source of truth

`docs/audit-baseline.json` — JSON file that records:

- **`continuum_server_sha`** — the full SHA of the last-audited server commit.
- **`audited_at`** — ISO timestamp of the bump.
- **`areas`** — a map of `"area/name"` → metadata:
  - `description`: what the feature does
  - `web_sources`: list of files in `continuum-server/web/src/` that define it
  - `server_sources`: list of files in `continuum-server/internal/` that define the API/types it consumes
  - `line_counts`: `{file → line count}` at last bump, used purely for eyeball drift detection
  - `desktop_files`: the desktop counterparts that need to stay in sync
  - `parity`: one of `full`, `partial`, `rebuilding`, `missing`, `desktop-only`
  - `notes`: free text

The fresh checkout lives at `F:\continuum-server\` (gitlab.zenterprise.org/quick/continuum).

## Parity values

| Value | Meaning |
|---|---|
| `full` | Desktop matches the WebUI 1:1 for this area at the recorded SHA |
| `partial` | Some features implemented, others missing or different |
| `rebuilding` | Active work in progress; desktop will match the new SHA when done |
| `missing` | WebUI has this feature; desktop has nothing yet |
| `desktop-only` | Desktop has this feature; WebUI doesn't (e.g., native libmpv playback pipeline) |

## Weekly workflow

```powershell
# 1. Pull the latest server.
cd F:\continuum-server
git pull

# 2. From the desktop repo, scan for changes since the last bump.
cd F:\ContinuumPlayer
pwsh scripts\audit-scan.ps1
```

`audit-scan.ps1` produces `docs/audit-reports/YYYY-MM-DD-HHmm-changes.md`. Read it. The report groups changes by priority:

- **P0** — files in areas already tracked by `audit-baseline.json`. These are where the desktop has parity claims that are now stale. Work these first.
- **P1** — new files inside a directory an existing area already tracks (e.g., a new hook file in `web/src/hooks/queries/`). Likely a sub-feature extension of that area. Integrate into the existing area or flag as a new area.
- **P2** — changes in directories no area claims. Could be a brand-new feature area that warrants a new entry in `audit-baseline.json`, or just noise (tests, formatting).

## Bumping an area after re-sync

When the desktop has been updated to match the new server version for an area:

```powershell
# Mark settings/history-import as fully synced with the current server HEAD.
pwsh scripts\audit-bump.ps1 -Area settings/history-import -Parity full
```

This:
1. Refreshes the area's `line_counts` from the files on disk.
2. Sets its `parity` to the value you pass.
3. Updates the top-level `continuum_server_sha` to the current HEAD.

Commit `audit-baseline.json` so the next `audit-scan.ps1` diffs against this new point.

If you just want to mark "we've reviewed everything and nothing needs changing" without bumping a specific area, use `-All`:

```powershell
pwsh scripts\audit-bump.ps1 -All
```

That only moves the root SHA forward without touching parity values.

## Adding a new tracked area

When `audit-scan.ps1` flags P2 changes that turn out to be a brand-new feature worth tracking, edit `docs/audit-baseline.json` and add an entry:

```json
"player/watch-together": {
  "description": "Group watch rooms with playback sync via realtime events.",
  "web_sources": [
    "web/src/player/components/WatchTogetherPanel.tsx",
    "web/src/player/hooks/useWatchTogetherRoom.ts"
  ],
  "server_sources": [],
  "line_counts": {},
  "desktop_files": [],
  "parity": "missing",
  "notes": "New feature in 94c2f32. Complex — reuses event channel for sync."
}
```

Once the desktop has something implemented, run `audit-bump.ps1 -Area player/watch-together -Parity partial` (or `full`) to capture the current line counts.

## Typical desktop change session

1. `pwsh scripts/audit-scan.ps1` — see what's changed.
2. Open the newest report in `docs/audit-reports/`.
3. Pick a P0 item. Read its `web_sources` and `server_sources` in the live `F:\continuum-server` tree.
4. Update the desktop counterparts (`desktop_files`).
5. Build, test.
6. `pwsh scripts/audit-bump.ps1 -Area <name> -Parity full`.
7. Commit the baseline change along with the desktop code change.

## Why not just use git submodules or a vendored copy?

- A submodule would bake a specific SHA into our working tree and prevent us from testing against newer server code without a submodule update — losing the "Mike's live server runs newer" ground truth.
- A vendored copy drifts silently, which is exactly the bug this workflow is fixing.
- The live clone at `F:\continuum-server` is already Mike's canonical reference; pointing at it via absolute path is the simplest truth-preserving option.
