# Admin Monitor correction candidate — November 17, 2026

Source: prepared worktree `t3-34a4a55a-46179de38e52767b`, branch
`t3/admin-activity-branding-fixes`, base Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`. Version remains
`1.2.1181-beta.1` / numeric `1.2.1180.1`; version stamping belongs to the
Beta pipeline. The coordinator completed acceptance and prepares the scoped Beta
submission; installer, stable release and installed-app actions are excluded.

Official reference is pinned object `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`
in `D:/SiloPlayer-work/server-upstream-nov14`. Its working HEAD is newer;
references were read with `git show <pin>:<path>`, never from working files.
The owner's supplied WebUI/native sidebar screenshots establish custom branding
versus bundled Silo. Their unmatched scale does not establish a pixel-size defect.

## Implemented source

- Admin desktop/drawer branding uses `SettingsApi`, `BrandingAssetSelector`, the
  existing image cache and the server-relative URL resolver. The current asset
  follows authority retirement and settings acknowledgements. Reads have a
  ten-second deadline; canceled/retired results cannot publish. The official
  112×48 logo box and mobile header glyph remain as defined by `AdminSidebar`
  and `AdminLayout`. Missing custom assets use the bundled default.
- Activity follows current `AdminActivity.tsx`, `adminActivityPresentation.ts`
  and `adminActivityReasons.ts`: compact filters and subtitle totals, IP Lookup
  dialog, media/viewer/playback/bitrate/time columns, a conditional Route column,
  62rem/70rem container boundaries, poster/progress cards, compact session actions,
  keyboard details expansion, and source/delivered, client and session facts.
  Server reasons preserve the closed vocabulary and server order; unknown codes
  are omitted, with no inferred causes. The first reason is visible and `+N`
  opens every known reason with its explanation. Existing authority/command,
  gutter and nested-scroll ownership is retained; navigation stays in place.
- Dashboard Concurrent streams has the official saved Lines/Stacked/Total
  presentations. Lines use independent method peaks; bands use
  `streams_by_method` at the total's peak minute and an optional Other remainder.
  Older replicas omit Stacked and render Lines while retaining a saved choice.
  Existing layout revision/persistence ownership is reused. The newer catalog
  formatting for units, durations and percentages is applied to affected widgets.
- The Integrations puzzle and new Activity film/audio/globe symbols have actual
  Lucide paths instead of the generic plus fallback. This adds no Plugin Apps
  workflow; that implementation belongs to its separate worker.

## Dashboard and Logs audit

Comparison from the previous Monitor pin `1a99f7be` to the required pin covered
`AdminDashboard`, every dashboard component/widget and the following Logs surfaces:

| Surface | Confirmed result |
| --- | --- |
| Dashboard layout/load/departure, Egress view | Current Beta already has retained pre-load drafts, departure flushing and placement views. Existing regressions remain in scope. |
| Concurrent streams | Missing method presentations and peak-minute split corrected here. |
| Now Playing | Pipeline-owned #200 is deliberately unchanged. Its row correction is outside this candidate. |
| Other dashboard widgets/formatters | Confirmed catalog-unit/duration/percentage changes applied; other compared contracts retained. |
| Server Log / Application, history/live stream, filters, detail and playback summary | Pinned source has no structural delta from the prior Monitor pin; existing bounded/ticketed workflows retained. |
| Server Log / Audit, history/live stream, filters and detail | No structural upstream delta; existing safe audit wording and detail workflow retained. |
| Client Diagnostics list/filter/history/detail, manifest, download/delete and Client uploads | Only confirmed upstream delta is catalog-based byte units, applied through the shared presentation formatter. |
| Logging Settings: output/runtime settings, retention and advanced disclosures, draft/save/conflict/restart | Confirmed MB unit catalog change applied; existing revision-guarded write-once workflows retained. |

No missing Manage/Configure topic is represented by an empty page. Existing page
registrations and the five Monitor XBF resources are reused; no Page/XAML type
was added. The three new production partial/helper files are included in
`admin-monitor-surfaces.json`. A static registration audit found no unexpected or
missing approved source and no new Page XAML.

## Observed verification and review corrections

All task-local evidence is retained under
`D:/SiloPlayer-build/admin-completion-monitor`. The stopped writer's original
worktree and `admin-monitor-correction-20261117` output remain unchanged.

### RED and corrective iterations

Unchanged Beta production reproduced missing Activity Bitrate/reason/+N controls
at 1800, 1600, 1440, 1120 and 800 logical pixels, bundled branding instead of a
custom wordmark, and missing Concurrent streams view controls. Baseline app/host
portable-PDB source binding and actual captures were checked. Synthetic data did
not reproduce a separate text-line-height defect.

Initial continuations fail-stopped on an xUnit `1` versus nullable-double test
datum, unsupported custom-panel Padding, and a native fixture local-name clash;
each was corrected before its next attempt. The actual IP Lookup submission then
exposed COM `0x800F1000` when the clicked button's sibling input was reparented.
The final implementation retains the form/TextBox/Button for the dialog lifetime
and replaces only results. One dialog now passes two actual Lookup invocations
and an owned-HWND Enter submission with distinct results, unchanged mounted
controls/parents, retained focus, and prior-result replacement.

Independent review also identified and corrected per-row quadratic Route
variation (now cached per accepted snapshot), an exposed Terminate shortcut (now
menu-only), and shared image downloads surviving a caller timeout. The underlying
shared transfer now has its own deadline across semaphore admission, headers,
body and cache writes; caller cancellation does not cancel another consumer.
Both stalled-header/body saturation regressions failed before that production
fix and passed afterward. The bounded final-delta reviewer reported no findings;
its six scoped file hashes and complete 2,315-file seal matched before/after.

### Final unit and payload provenance

- `final-r7`: fresh nonincremental main-unit build; 562 focused and **8,057
  full** cases passed, zero failures or skips. Its app compile stopped at a
  Color/Brush view error, subsequently corrected without changing unit inputs.
- `final-r8`: all 2,057 Core/unit/linked inputs matched r7 (digest
  `671f784e396617cbb4e49d4fa2481a743ede34cefc704340ba5df8696755de97`).
  All 527 affected/source-reading cases passed again. Nonincremental app build,
  publish and native host build passed. `final-r8-source.json` and
  `final-r8-payload.json` identify the product used for final native acceptance.
- r9/r10 diagnosed native preconditions without changing production: the restart
  banner probe had requested scrolling before layout settled; Logs used the
  wrong text catalog and repeated profile verification without reselecting a
  profile. The fixtures now await real scroll range/offset, use AdminText for the
  shell banner, and establish a selected synthetic profile before each probe.
- `final-r11`: r7 unit-input seal passed; every r8 published product byte was
  rehashed unchanged. Exactly the two documented Monitor/Logs fixture files
  differed from r8. Nonincremental host build passed with zero errors. Product,
  referenced and host DLL/PDB/PRI equality and source/PDB proofs passed. Final
  source readback verified all **2,315** sealed files without a mismatch.

The final app DLL SHA-256 is
`B47B3E499CD227F8F303105FF86702C476A9F800D580B744CFD2AC5C4461B8AF`;
PDB SHA-256 is
`C4D67CABC3BBC1C38EECAE5F59BF90E63702D02646AD8CB53160145148027D1D`.
Portable-PDB proofs distinguish physical checkout checksums, generated documents
and packaged bootstrap source. See each native group's `source-pdb-proof.json`
and `binding.json` rather than inferring an unqualified all-file identity.

### Final native acceptance

Every group completed against that same product. PASS lines include assertion,
capture and completion entries; they are not unit-test counts.

| final-r11 group | PASS lines | FAIL lines | PNGs |
| --- | ---: | ---: | ---: |
| Activity corrections / retained IP Lookup | 866 | 0 | 11 |
| Custom shell branding and retirement | 9 | 0 | 3 |
| Concurrent streams presentations | 20 | 0 | 6 |
| Existing Monitor shell/Activity/navigation | 113 | 0 | 23 |
| Existing Dashboard | 184 | 0 | 22 |
| Existing Logs/Diagnostics/Logging Settings | 232 | 0 | 77 |
| Manage foundation | 36 | 0 | 0 |
| Configure General | 188 | 0 | 11 |

Wide/compact Activity, expanded reasons, repeated IP Lookup, custom branding,
Concurrent streams, Logging Settings, Server Log detail, Now Playing and General
captures were opened and inspected. Native assertions also cover the 62rem
boundary, conditional routes, keyboard focus, full reason explanations, actual
restart-banner movement, existing nested scroll ownership and authority changes.

The runner `.codex-tmp/admin-completion-monitor/verify.ps1` uses one nonblocking
`SiloOct18Build.WaitOne(0)` per attempt, fail-stop commands, owned Windows jobs,
deadlines and finally release. Final actual ACQ was
`2026-11-17T12:49:55.3843759Z`; REL was
`2026-11-17T12:52:27.7185261Z`, PID 44140. No owned runtime, holder or waiter
remains. Inclusive preflight measured **27.9610 GiB** against the 50 GiB cap;
both used drives remained above the 50 GiB free floor. No cleanup was performed.

## Delivery and limits

The coordinator prepares this independently usable increment for a non-draft
same-repository Beta PR. Version stamps remain pipeline-owned and unchanged.
Only the two documentation records change after the final source seal; compiled
production and fixture inputs remain sealed. Other Manage/Configure families
are separate candidates, not certified by this result. Submitted PRs are owned
by the Beta pipeline and are not revisited by this worker.

Synthetic captures do not certify live-server operation, every theme/locale/DPI,
IME/Narrator, long-running traffic, or complete pixel-level visual parity. The
installed player (PID 39668), its settings, other workers, frozen payloads, prior
evidence and pipeline-owned PRs remain untouched. No installer was built or run,
and no stable release or direct merge is included.
