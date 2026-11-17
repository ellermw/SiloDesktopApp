# Configure Search — C4

Status: **Verified for separate Beta submission on deliberate base `bdd033c1cab7cd601d6771bb853f4b0af044d67e` (pipeline-owned 1.2.1165).** Official Silo Server main was fetched directly at `5b3dcaa2fdfec6de2a537a2e10897352a1078689`; the Search contracts inspected at `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8` remain unchanged. The Beta pipeline owns version stamping.

## Delivered behavior

The page is registered through the production `AdminConfigureServices.Add` entry point at `/admin/settings/search` in Configure > Server, with the sidebar leaf and search entry that the catalog already carried (keywords include `meilisearch`, `typo`, `reindex`). It reuses the guarded settings API, the shared snapshot store, `AdminSettingsForm`, the settings row/group/SaveBar primitives and the full-width gutter scroller (1120 column). No page transition.

One **Search** group (official description, group dirty dot, restart-all text, per-row chips) holds:

- **Search engine** select: Built-in (Postgres) / Meilisearch. A blank saved value displays Postgres. While Meilisearch is the selected engine and status is not degraded, a muted live line shows `Last sync {date} · {n} items indexed`.
- A warning line when the environment-status read failed ("Couldn't check…"), with Retry; provider, URL and API key are then protected and excluded from writes until it succeeds (an API key draft is never kept across that gap).
- A degraded line (server reason or the official fallback) while Meilisearch is selected and the status is degraded; when a rebuild is required it adds the official "Automatic search maintenance…" sentence, with a link to the sync task when Scheduled Tasks is a native page and without it otherwise.
- **Meilisearch rows**, shown while it is selected and kept reachable while any staged Meilisearch edit exists (so Save never counts a change the admin cannot see): URL (hint `http://localhost:7700`); write-only **API key** SecretField (masked placeholder from sensitive-status presence only, explicit Clear saved value / Keep saved value before the input, typing stages a replacement, emptying keeps the saved key, erased after an acknowledged save, frozen while saving); **Check Connection**; **When a search has several words** (Drop trailing words / Require every word; blank displays the first); **Match by meaning as well as words** with the index-format warning when an unsaved change enables it.
- **Environment management**: provider, URL and API key are disabled and read `Managed by SILO_SEARCH_PROVIDER.` / `…SILO_MEILISEARCH_URL.` / `…SILO_MEILISEARCH_API_KEY.`, and are never written.
- **Advanced** disclosure (remembered under the `search` preference; forced open while an Advanced field is dirty): Index name prefix, Query timeout (ms unit slot), Items sent to the index per batch and Meaning-based share of results, with the official defaults displayed for blank values (a box being typed in keeps its text), then the **Search status** panel.
- **Search status** (read only): degraded notice, Answering searches, Health, Active index (schema or "rebuild required"), Documents, Indexed types, Compressed vectors, Meaning-based search, share, Items with vectors, the semantic readiness/coverage/embedding-model rows and per-type coverage, Pending events, Dropped events (only when > 0), Last sync and Last fallback. Loading shows skeleton bars; a failed read says "Couldn't load search status: {reason}" with a native Retry instead of an endless skeleton; the previous rows are replaced by the error, as the official panel does.
- **Rebuild index** and **Automatic maintenance** below the status, shown whether or not it loaded.

## Check Connection

`POST /api/v2/admin/settings/check/meilisearch` with the form's value for all eight Meilisearch keys and only the dirty (and not environment-managed) keys as `dirty_keys`, so a saved API key is used server-side and never read back. The result is a polite live line (green/red). Any edit to a Meilisearch field, Discard, Reload or a newer check hides it, and an answer for an older draft is dropped. A typed API key and URL credentials are removed from the returned message; a transport or server failure shows the generic "Connection check failed." and never raw server text. The check is unavailable for Postgres and while saving.

## Status polling and bounds

`GET /api/v2/admin/catalog/search/status` (1 MiB bound, 20 s deadline, no authentication replay). It is read only while the page is visible and focused **and** its data is wanted: Meilisearch is the selected engine, or the Advanced disclosure is open. A read is reused for 15 s (also after a failure, so a failing endpoint is not hit per keystroke), and activation reads only stale data. A 2 s poll runs only while the last status says `rebuild_required`, only for the visible page, and each tick is skipped while playback is loading. Hidden/deactivated windows drop the timer, departure or authority change cancels reads, checks and saves, and Save re-reads the status. Explicit Retry, Save and Reload invalidations during an existing status read coalesce into one follow-up GET; an invalidation remains pending while inactive and is discarded on authority retirement. Periodic ticks do not queue behind a slow read. Lists are bounded (64 types / coverage rows) and over-long metadata ends with an ellipsis. Degraded, fallback and semantic diagnostic fields preserve only exact known safe messages; arbitrary provider/proxy text is replaced with "Additional provider diagnostics are withheld." Unknown saved credentials cannot safely be removed with regex matching alone.

## Search maintenance task links

Both official controls navigate: **Rebuild index** targets `/admin/tasks/rebuild_catalog_search_index` and **Automatic maintenance** targets `/admin/tasks/sync_catalog_search_index`. Search does not POST a task command, acknowledge a task as started, or present a substitute confirmation dialog. Both buttons require a loaded current owner and registered Scheduled Tasks surface. Until that surface exists, they are disabled with explanatory tooltip and accessibility help. The degraded notice retains its explanatory plain text and withholds its maintenance link until registration. Every task-link activation rechecks the current owner before navigation.

## Native differences and limits

- The sidebar attention dot for an unhealthy/degraded Meilisearch is shell health work and is not part of this increment; General and Email likewise produce no dot.
- Scheduled Tasks is not native yet: both maintenance buttons remain disabled; their official destinations are preserved rather than replaced with direct commands.
- Indexed-type lists use a comma join rather than locale list formatting; `Documents`, `Items with vectors` and the event counts are plain integers as in the official panel.
- The unchanged hidden tier (rebuild batch size, task queue depth, index types, embedder, binary quantization) has no control, as official.

## Verification and review repairs

The bounded source review found unsafe provider diagnostics, lost forced refreshes behind an existing read, and unsupported direct rebuild execution. The candidate uses an exact safe-message allowlist, coalesced foreground invalidation (including inactivity), and official task navigation only. Added unit regressions pass; their original authoring did not establish an observed RED and is not represented as test-first proof.

- `search-full-r2`: nonincremental unit build, source/PDB binding, source proof **1/1**, focused **187/187**, full **7537/7537**, with zero failures or skips. App build/publish and native host build passed. Receipt SHA256 `5C41A5B511BBD3A988B010A12273274395209DE7907A7996E813931DC8D35C82`.
- The first native Search attempt failed on a fixture expectation. Subsequent fixture-only corrections preserve explicit-default dirty behavior, disabled task destinations, isolated in-memory disclosure preferences, real Cancel completion, and Page/Frame Unloaded barriers. Historical failed attempts remain preserved and are not counted as passes.
- Final `search-native-r6`: retained the exact R2 unit/product source and all **666** published app files, rebuilt the final native host, and passed unit/app/host source binding. All eight native groups exited **0** with one suite completion each: Search **48**, General **188**, Email **297**, Security **374**, Network access **346**, Manage foundation **36**, Monitor **113**, Dashboard **184** PASS markers (including completion markers).
- Actual R6 lease: **2026-11-16T21:22:15.8735975Z** to **2026-11-16T21:25:39.0469867Z**. Receipt SHA256 `54E551A535135E7D4F038637D39DD0A7B0EB1E2BBBD574C777E112AB4572BAC7`. The released audit qualified **2576/2576** source entries, **666/666** payload files and all **11** exact inline/disk captures, numeric exits and contained cleanup. Evidence is under `.codex-tmp/admin-search-verification/`; `search-native-r6/root-released-audit.json` records the retained-unit relationship.

Only delivery documentation changed after this acceptance. No installer, live Meilisearch, physical-input, Narrator, every-DPI or comprehensive visual verification is claimed. The fixture capture helper was unused, so there are no screenshot captures. New copy is English-only. Sidebar health dots and Scheduled Tasks remain separate increments; both maintenance buttons explain why they are disabled until native task registration exists.

## Preservation and delivery scope

The immutable pre-repair checkpoint `.codex-tmp/admin-search-source-review-20261116/before.json` (SHA256 `4AEB18AACFEF5FBEB97C602BA80C0EE0E40A2DC698FE64A6F1266FFDAEE04248`) and failed run evidence remain historical baselines. This is one Search-only PR, not combined with Libraries, Users, Storage or Dashboard. Installed player/settings and retained Requests output were untouched. Serialized runtime scheduling and the owner-authorized 50 GiB generated-output cap remain in force.
