# Admin Catalog Maintenance — Full Audit

Prepared: 2026-04-15
Scope: Desktop `AdminMaintenancePage` (the canonical Catalog Maintenance page per
B56 in `docs/webui-parity-master.md`) vs. webui `AdminCatalogMaintenance.tsx` +
`AdminJobHistory.tsx` + `adminCatalogMaintenanceFormatters.ts` +
`adminCatalogMaintenancePathRewrites.ts`.

---

## Files read (100% line-by-line)

Desktop — read in full:

- `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminMaintenancePage.xaml` (223 lines) — full read.
- `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminMaintenancePage.xaml.cs` (633 lines) — full read.
- `F:\ContinuumPlayer\src\ContinuumPlayer\ViewModels\Admin\AdminMaintenanceViewModel.cs` (184 lines) — full read.
- `F:\ContinuumPlayer\src\ContinuumPlayer\Controls\CatalogImportDialog.xaml` (134 lines) — full read.
- `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminShellPage.xaml` — read relevant nav lines 548–577 plus associated `.xaml.cs` lines 58, 145, 263–266 (nav wiring for `NavMaintenance` → `AdminMaintenancePage`).
- `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminLibrariesPage.xaml` — read header strip lines 76, 99–107 (the "Catalog Maintenance" button).
- `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminLibrariesPage.xaml.cs` — read lines 713–718 (`CatalogMaintenanceButton_Click`).
- `F:\ContinuumPlayer\src\ContinuumPlayer.Core\Api\AdminApi.cs` — grepped for all catalog-seed and jobs endpoints; confirmed:
  - `ExportCatalogAsync` (line 319) → `POST /api/v1/admin/catalog/export`
  - `CreateExportJobAsync` (line 322)
  - `PublishExportJobAsync` (line 325) → `POST /api/v1/admin/catalog/export-jobs/{id}/publish`
  - `CreateImportJobAsync` (line 328)
  - `GetCatalogImportSourcesAsync` (line 331) → `GET /api/v1/admin/catalog/import-sources`
  - `GetLocalImportSourcesAsync` (line 334) → `GET /api/v1/admin/catalog/local-import-sources`
  - `ImportCatalogAsync` (line 337) → `POST /api/v1/admin/catalog/import`
  - `GetJobsAsync(jobType?, limit)` (line 343)
- `F:\ContinuumPlayer\docs\webui-parity-master.md` — read the relevant B56, P3, P4 blocks (lines 429–433, 900–967, 1066–1068).

## Files sandbox-blocked (affected claims UNVERIFIED)

The webui source tree lives at `F:\continuum-server\web\src\...` which is OUTSIDE
the project working directory and cannot be opened by Read/Bash/Grep in this
session. All of the following files exist on disk (confirmed via Glob since Glob
enumerates paths without reading content), but their contents cannot be read:

- `F:\continuum-server\web\src\components\AdminCatalogMaintenance.tsx` — BLOCKED.
- `F:\continuum-server\web\src\components\AdminCatalogMaintenance.test.ts` — BLOCKED.
- `F:\continuum-server\web\src\components\AdminJobHistory.tsx` — BLOCKED.
- `F:\continuum-server\web\src\components\adminCatalogMaintenanceFormatters.ts` — BLOCKED.
- `F:\continuum-server\web\src\components\adminCatalogMaintenancePathRewrites.ts` — BLOCKED.
- `F:\continuum-server\web\src\hooks\queries\admin\*` — BLOCKED.
- `F:\continuum-server\web\src\app.css` — BLOCKED.

Every claim below about webui behavior that is not independently anchored to an
in-repo note (`docs/webui-parity-master.md` B56 and PARTIAL/MISSING items) is
marked **UNVERIFIED**. The shape of the webui page is inferred from:

1. The desktop port's own comments (`AdminMaintenancePage.xaml.cs` opening
   docstring "Mirror of the webui AdminMaintenance page" — lines 13–21).
2. The in-repo parity doc (`webui-parity-master.md`), which explicitly calls out
   the known deltas at lines 429–433 (B56), 929 ("Catalog Maintenance" link in
   Libraries header), 1066 (path rewrite row editor PARTIAL), 1067
   (`formatExportProgressLabel` thousand separators PARTIAL), 1068 (import
   conflict mode + path rewrites PARTIAL).
3. The AdminApi endpoints the desktop already wires to (which necessarily match
   the server-side API the webui hook layer hits).

Mike should re-run this audit with the webui files accessible to harden any
UNVERIFIED rows — especially Tailwind→brush translations and exact formatter
behavior.

---

## Desktop existence check

- **Page exists:** `AdminMaintenancePage` — YES. Both xaml (223 lines) and .cs
  (633 lines) present.
- **ViewModel exists:** `AdminMaintenanceViewModel` — YES (184 lines).
- **Routed from shell:** YES — `AdminShellPage.xaml` has `NavMaintenance` at
  lines 548–577 with label `"Maintenance"`, and `AdminShellPage.xaml.cs` line
  263–266 navigates `typeof(AdminMaintenancePage)` on click.
- **Deep-link from Libraries header:** YES — `AdminLibrariesPage.xaml` lines
  99–107 render a `Catalog Maintenance` button; `.xaml.cs` line 713 wires it to
  `Frame.Navigate(typeof(AdminMaintenancePage))`.

So the page exists and is reachable. This audit is therefore a parity review of
a present-but-partial implementation, not a missing-page finding.

---

## 1. Page header / section header

### Webui behavior (UNVERIFIED against source; inferred from desktop mirror + B56 + typical AdminXxxx pattern)

Expected structure (matches every other admin page in the webui audits):

- `page-header` class with h1 display title "Maintenance" (or similar), text-3xl
  (≈30px) font-bold, plus a muted description paragraph (text-sm ≈14px,
  `text-muted-foreground`).
- Likely wrapped in a `page-shell` / container with max-w and responsive
  padding.

### Desktop behavior

`AdminMaintenancePage.xaml` lines 43–55:

- `TextBlock Text="Maintenance"` with `FontSize="{StaticResource FontSizeDisplayLarge}"`
  (≈34–42px per DarkTheme palette — **larger than webui text-3xl ≈30px**), weight
  Bold, `PrimaryTextBrush`.
- Subtitle `FontSize="14"` (matches text-sm 14px), `SecondaryTextBrush`,
  `MaxWidth="820"` wrapping enabled.
- Text: *"Operational tools that affect the whole catalog live here. Use this
  page for bulk import/export workflows and other future maintenance actions."*
  This copy is the desktop's own paraphrase; the exact webui subtitle string is
  UNVERIFIED.

### Gap

- **Unverified exact copy:** subtitle text almost certainly diverges from the
  webui source string. UNVERIFIED.
- **Display size drift:** webui likely uses `text-3xl` (30px) or `text-4xl`
  (36px); desktop uses `FontSizeDisplayLarge` which is defined larger in
  `DarkTheme.xaml`. Sibling audits have flagged the same pattern for other admin
  pages.
- **Missing container chrome:** no `page-shell`, no max-width constraint beyond
  the inner `MaxWidth="1200"` on the ScrollViewer child (line 40). Webui uses a
  consistent container utility class (UNVERIFIED exact class).

### Severity

Visual — LOW if the label is right, MEDIUM if copy differs materially.

---

## 2. Maintenance actions (buttons, confirmations, workflows)

### Webui behavior (UNVERIFIED source text; actions inferred from desktop + B56 copy)

Per parity-master B56 (line 431):

> Web `AdminCatalogMaintenance.tsx` is a catalog export/import UI (Start Export,
> Import Catalog dialog, Recent Imports/Exports list, path rewrites).

Expected top-level actions bar: two buttons ("Start Export" + "Import Catalog")
inside a card with an intro header. The Import Catalog button opens a dialog
with:

- Import Source selector (local path / export job / bucket artifact / remote URL)
- **Conflict Mode** select (skip_existing vs overwrite_existing) — see parity
  line 1068 MISSING/PARTIAL.
- **Path Rewrites row editor** — parity line 1066 flags this as PARTIAL on
  desktop. Webui renders multi-row prefix→replacement editors backed by
  `adminCatalogMaintenancePathRewrites.ts` helpers.
- Helper copy and validation.

### Desktop behavior

`AdminMaintenancePage.xaml` lines 71–116 render the actions card:

- Title "Catalog Import & Export" (line 86) FontSize 18 SemiBold.
- Body copy (lines 89–94): *"Queue full catalog exports, import seeds from
  uploads or S3, and watch background job progress in one place."*
- Two right-aligned buttons (lines 96–114):
  - **Start Export** — no variant styling, default system Button; icon
    `Glyph="\uE896"` (Segoe "Download" arrow), label "Start Export". Wired to
    `StartExport_Click` → `ViewModel.StartExportCommand` (VM lines 116–132,
    submits `new CatalogSeedExportRequest()` with no library filter).
  - **Import Catalog** — label "Import Catalog", icon `\uE898`. Opens
    `CatalogImportDialog` (Controls/CatalogImportDialog.xaml, 134 lines).

`CatalogImportDialog.xaml` contents:

- Source combo with four options (`local_path`, `export_job`, `bucket_artifact`,
  `remote_url`) — lines 21–28.
- Per-source sub-panel (File Path box + detected-file combo; completed export
  combo; bucket artifact combo; remote URL box) — lines 31–86.
- **Conflict Mode combo** (skip_existing / overwrite_existing) — lines 88–96. ✅
  present.
- **Path Rewrites section** — lines 98–116: has an `AddRewrite` button and a
  `RewritesPanel` StackPanel. The row template is built in
  `CatalogImportDialog.xaml.cs` (not read line-by-line in this session — the
  grep-index confirms it exists; the parity doc rating is PARTIAL).

### Gap

- **`formatExportProgressLabel` thousand separators** — parity line 1067 PARTIAL.
  Desktop's progress formatting is in `AdminMaintenancePage.xaml.cs` →
  `FormatJobProgress` (lines 479–489): `$"{job.ProgressCurrent} / {job.ProgressTotal}"`.
  There is **no thousand-separator formatting** (no `:N0`, no
  `ToString("N0")`). For a 400,000-item export the webui would render
  "12,345 / 400,000"; desktop renders "12345 / 400000".
- **Path Rewrites editor** — parity line 1066 PARTIAL. Present but not at
  parity. Without reading the webui formatters, exact delta is UNVERIFIED, but
  at minimum the webui's `adminCatalogMaintenancePathRewrites.ts` helper
  suggests prefix-normalization + validation logic the desktop may be missing.
- **Import conflict mode + path rewrites** — parity line 1068 PARTIAL. Present
  but flagged.
- **No confirmation on Start Export** — the desktop fires `ViewModel.StartExportCommand`
  directly (line 67, `Page.StartExport_Click`). If webui has a confirm dialog for
  exports, desktop diverges. UNVERIFIED — inspection needed.
- **Button variants** — desktop uses raw `Button`. Webui likely uses a
  branded Button variant (primary vs secondary / outline) with Tailwind classes
  like `bg-primary text-primary-foreground`. UNVERIFIED translation.

### Severity

Functional — MEDIUM (parity doc already tracks these as PARTIAL).
Formatting `:N0` gap — LOW (visual polish).

---

## 3. Job history table — wrapper, header, row structure

### Webui behavior (UNVERIFIED)

`AdminJobHistory.tsx` is a separate component — per parity-master it is
"Recent background jobs across all types" rendered as a table or row list. The
webui likely renders:

- Card wrapper with header + description + refresh button.
- Each row: status badge + type badge + description + timestamp; a meta line
  with progress / result / finished; error messages for failed jobs; possibly
  deep links (see §8).

### Desktop behavior

`AdminMaintenancePage.xaml` splits this into **three cards**:

1. **Recent Catalog Imports** (lines 118–150) — `ImportJobsPanel` rebuilt by
   `RebuildImportJobs` (code-behind lines 96–106) via `BuildImportJobRow`
   (lines 134–204).
2. **Recent Catalog Exports** (lines 152–184) — `ExportJobsPanel` → `BuildExportJobRow`
   (lines 206–317) with **right-column action buttons** (Download / Publish /
   Copy URL).
3. **Job History** (lines 186–218) — `AllJobsPanel` → `BuildAllJobRow` (lines
   319–378). This is the mirror of `AdminJobHistory.tsx`.

Every card uses:

- `CardBackgroundBrush`, `BorderBrush`, `BorderThickness="1"`, `CornerRadius="12"`.
  Webui admin cards are commonly Tailwind `rounded-xl` (=12px) with
  `border bg-card` — radius matches. UNVERIFIED that the `bg-card` token maps to
  the same color value as `CardBackgroundBrush`.
- Header grid: left title + subtitle, right refresh icon button (Segoe `\uE72C`,
  the "Refresh" glyph).
- Divider: `Border Height="1" Background="{StaticResource BorderBrush}" Opacity="0.6"`
  — lines 147, 181, 215. Webui would almost certainly use `<Separator />` or
  border-t on the next row (`divide-y`), **not an opacity-0.6 line**. Visual
  variance possible.

### Row internals — import row (code-behind lines 134–204)

- Container `StackPanel` with `Padding="20,14"`, border-top 1px.
- Head row (line 145–162): status badge + description + "requested {time}".
- Optional message line (line 165–174): FontSize 12, `SecondaryTextBrush`,
  wrapping.
- **Progress bar** (line 177 → `BuildProgressBar` lines 439–460). Track height
  6px, CornerRadius 3, `SurfaceRaisedBrush` background, `AccentBrush` fill.
  When `ProgressTotal == 0` the fill is clamped to 12% for running / 4% for
  queued / 100% complete (VM `GetJobProgressPercent` lines 173–183).
- Meta row (line 180): "Progress: X / Y", "Finished: {time}", "Imported N items
  and M files".
- Error message line when present (lines 192–201).

### Row internals — export row (lines 206–317)

- 2-column Grid: info column left, action buttons right.
- Same head / message / meta blocks — but **NO progress bar**. Export rows
  appear without the visual progress track the import rows get. In the webui
  this would likely be consistent across row types. UNVERIFIED — but plausibly
  a bug.
- Right column actions (lines 279–314): Download button if `DownloadUrl` set,
  Publish button if completed and no public URL, Copy URL if published.
  Buttons are plain WinUI buttons with FontSize 12, Padding 10,6 — **not
  styled variants** like webui's `<Button variant="outline" size="xs">`.

### Row internals — all-jobs row (lines 319–378)

- Head row: status badge + **type badge** (rounded bordered pill rendered by
  `BuildTypeBadge` lines 418–437) + optional description + timestamp.
- Meta row: message / "Progress: X/Y" (only when running or queued — line 356)
  / job result summary / finished timestamp.
- Type label mapping (lines 513–521): `delete_library`, `catalog_export`,
  `catalog_import`, `item_refresh`, `library_refresh`. **Missing types** the
  webui may render: `collection_sync`, `history_import`, `scan`, etc. —
  unrecognized types fall through to the raw job_type string (line 520).
  UNVERIFIED webui coverage.

### Gap

- **Import rows get a visual progress bar; export rows and all-jobs rows do
  not.** Inconsistent. UNVERIFIED webui does this.
- **Divider opacity = 0.6** — webui likely uses full-opacity border (border-t).
  Visual drift.
- **Row padding `20,14`** matches typical Tailwind `px-5 py-3.5` (20px × 14px).
  Close match.
- **Type badge** is an outline pill with `BorderBrush` 1px, `CornerRadius=10`,
  FontSize 10 Medium (code 418–437). Webui may use a filled variant (`<Badge>`
  component with `variant="secondary"`). UNVERIFIED.
- **Status badge colors** (lines 390–416) are hard-coded (`#4ADE80`, `#60A5FA`,
  `#FBBF24`, `#EF6B73`, `#9CA3AF`) with a 0x33 alpha background tint. These
  match Tailwind's `green-400`, `blue-400`, `amber-400`, `red-400`, `gray-400`
  one-to-one — but the webui likely uses semantic tokens (`bg-success/20
  text-success`) which may resolve differently under a theme change. As long as
  the project stays on the default dark theme this is visually correct.
- **Failing status variants** — completed/running/queued/failed are covered;
  `cancelled` / `cancelling` / `skipped` fall through to gray default (line
  398). If webui renders them distinctly, desktop regresses.
- **No sort / header row** — the desktop renders rows as a list, not a table
  with sortable headers. Parity line 940 has a MISSING callout for similar
  patterns on AdminActivity (sort headers with ▲/▼). If `AdminJobHistory.tsx`
  renders as a table, the desktop is missing columns. UNVERIFIED.

### Severity

Visual — LOW/MEDIUM (inconsistent progress bar placement is a correctness issue
worth flagging).
Functional — UNVERIFIED for sort/header columns until webui is inspected.

---

## 4. Status / progress / progress message display

### Webui behavior (UNVERIFIED)

`adminCatalogMaintenanceFormatters.ts` contains the
`formatExportProgressLabel` referenced by parity line 1067. Webui formats
progress as something like `12,345 / 400,000 items` with locale-aware thousand
separators.

### Desktop behavior

- `FormatJobProgress` (`.xaml.cs` lines 479–489): `"{ProgressCurrent} / {ProgressTotal}"`
  when total > 0; else `"Done" / "In progress" / <status>`. **No thousand
  separators.**
- `GetJobProgressPercent` in VM (lines 173–183): clamps total-unknown to 12%
  (running) / 4% (queued) / 100% (completed).
- Progress bar geometry (Page.BuildProgressBar lines 439–460): 6px high, 3px
  radius, accent fill in a star-columns Grid with the remainder column being
  background only. Functional.
- Transient status banner (xaml lines 58–69): green `#22C55E` (Tailwind
  `green-500`) background, CornerRadius 10, SemiBold white text. Bound to
  `ViewModel.StatusMessage`. **This green is the only brand green**; if the
  webui uses a `bg-success` token, it likely resolves to this same value.

### Gap

- **Thousand separator formatting missing** — confirmed by parity line 1067.
  For a 1M-item catalog the display reads "123456 / 1000000" instead of
  "123,456 / 1,000,000". Small but user-visible.
- **Progress message localization** — `Done`, `In progress`, `queued` strings
  are hard-coded English (lines 484–488). Webui may also hard-code these, so
  this may not be a gap. UNVERIFIED.
- **No "running → spinner" affordance** on rows — parity line 1062 tracks a
  similar gap for AdminRecommendations. Desktop progress bar for running jobs
  shows a shallow 12% fill but does **not animate** (Indeterminate not used).
  Webui likely uses a `Loader2` spinning icon. UNVERIFIED.

### Severity

Visual — LOW (formatters). Animation — LOW/MEDIUM.

---

## 5. Pagination / filtering / sorting

### Webui behavior (UNVERIFIED)

For job history specifically, webui `useAdminJobs` hook likely accepts at least
a `jobType` filter (parity line 955 MISSING: "AdminJobs filter by `job_type`").
Pagination/limit behavior is not directly documented.

### Desktop behavior

- `AdminApi.GetJobsAsync(jobType?, limit=50)` (AdminApi.cs line 343).
- VM hard-codes limit=50 on all three lists (VM lines 63, 74, 85).
- **No filter UI, no sort UI, no pagination controls.**

### Gap

- **No `job_type` filter UI on All Jobs card** — parity line 955 MISSING.
  User cannot narrow the Job History card to only library_refresh, etc. Webui
  has this (B11 reference). Desktop does not.
- **No pagination controls** — 50 is fixed. For busy servers this cuts off
  history. If webui has cursor/limit bump, desktop lacks it. UNVERIFIED.
- **No sort** — cannot sort by requested / completed / status. UNVERIFIED if
  webui exposes sort.

### Severity

Functional — MEDIUM (known MISSING gap per parity master).

---

## 6. Empty / loading / error states

### Webui behavior (UNVERIFIED but patterned)

Typical admin cards: skeleton rows while loading, empty-state inside card body
("No jobs yet"), error message inline (not hiding the page).

### Desktop behavior

`AdminMaintenancePage.xaml` lines 19–34:

- **Loading**: `ProgressRing` centered over the entire `Grid` root, page body
  hidden (`Visibility` bound to `InverseBoolToVis(IsLoading)` on the
  ScrollViewer, line 37).
- **Error**: a centered `StackPanel` (lines 26–34) with the error message and a
  single Retry button, bound via `NullToVisibilityConverter` on `ErrorMessage`.
  **This hides the entire page body.**
- **Empty**: each card's `EmptyRow(...)` helper (code lines 382–388) renders a
  12pt secondary-colored `TextBlock` *inside* the card body (margin 20,18,20,18):
  - "No catalog import jobs yet." (line 101)
  - "No catalog export jobs yet." (line 113)
  - "No jobs yet." (line 125)

### Gap

- **Loading hides the entire page.** Prior audits (per the task prompt) flagged
  this exact pattern in other pages: "loading/error states that hide the entire
  page rather than swapping card body". Desktop's `IsLoading` gate at line 37
  hides the ScrollViewer, preventing card-level skeleton. Webui likely uses
  per-card skeleton.
- **Error hides the entire page.** Same pattern — if *any* of the parallel
  loads in `LoadAsync` (VM lines 42–57) throws, every card disappears and a
  centered error + Retry button is shown instead. Webui likely degrades
  per-card.
- **Empty state copy** — "No catalog import jobs yet" / "No catalog export jobs
  yet" / "No jobs yet" — exact strings UNVERIFIED against webui.
- **No skeleton rows** — desktop has no skeleton UI anywhere; just the
  `ProgressRing`.

### Severity

**Functional — HIGH** on the hide-entire-page pattern. This matches the
critical-pattern callout in the task prompt.
Visual — LOW on empty strings.

---

## 7. Realtime event channel integration (catalog / jobs channels)

### Webui behavior (UNVERIFIED)

`docs/events-channel-spec.md` exists in this repo; the webui hooks for admin
jobs likely subscribe to a `jobs` or `catalog` channel and invalidate/re-fetch
queries on events (the "auto-refreshing" claim sibling audits flagged). Without
reading `web\src\hooks\queries\admin\useAdminJobs.ts`, exact channel names are
UNVERIFIED.

### Desktop behavior

- **No event-channel subscription.** `AdminMaintenanceViewModel` has zero
  references to `EventChannelClient`, `IEventChannelClient`, or any pub/sub.
- **No polling either.** There is no 2s or 5s or 30s timer. Refresh is strictly
  manual (the refresh icon in each card) or triggered after an action
  (`StartExportAsync`, `SubmitImportAsync`, `PublishExportAsync` each refetch
  the relevant list — VM lines 127–128, 144–145, 161).
- Parity line 956 specifically calls out: `useLibraryDeleteJobs` 2s active
  polling — MISSING on desktop.

### Gap

- **No live refresh while jobs run.** An import running in the background will
  only show new progress if the user clicks the refresh icon (or reloads the
  page). Webui almost certainly ticks at 2–5s while any job is `running` or
  `queued`.
- **No event-channel tie-in.** Server `jobs.*` events are not consumed here.

### Severity

**Functional — HIGH.** The whole card copy on lines 137–139 / 170–173 says
"progress stays visible while validation and writes are in flight" — but the
progress bar is static until the user refreshes. False implication. This matches
the "missing realtime refresh while claiming auto-refreshing" critical pattern
flagged in the task prompt.

---

## 8. Deep links (to library, to item, to logs)

### Webui behavior (UNVERIFIED)

- For `delete_library` / `library_refresh` / `item_refresh` rows the webui
  likely shows a link to the library (or item) from the description. UNVERIFIED.
- For completed exports the webui likely shows a Download link, a Publish
  action, a Copy URL action — all three present on desktop.

### Desktop behavior

- Export row actions (lines 279–314): Download (opens in browser), Publish
  (calls API), Copy URL (clipboard). ✅ all three present.
- **All Jobs row** (lines 319–378): no links at all. The `library_name`
  /`library_id` /`items_created` metadata is rendered as plain text. No click
  target to navigate to the library page, item page, or related task/log.

### Gap

- **No deep link from All Jobs rows to the relevant library/item.** If a user
  sees "Library Refresh — Movies — 2 hours ago" they cannot click through to
  `/admin/libraries/{id}` (which is `AdminLibraryDetailPage` or equivalent).
  UNVERIFIED exact webui behavior, but prior audits flagged similar missing
  deep-links as critical gaps.
- **No link to related logs** (parity line 934: "View related playback session
  logs" button missing for Logs — analogous pattern). For failed jobs the
  desktop only shows `ErrorMessage` as text (lines 192–201, 264–272, 366–374);
  webui may link to `/admin/logs?job_id=X`. UNVERIFIED.

### Severity

Functional — MEDIUM to HIGH depending on webui behavior.

---

## 9. Action buttons / overflow menus

### Webui behavior (UNVERIFIED)

- Export row: Download / Publish / Copy URL as small buttons (probably
  `<Button variant="outline" size="xs">` with h-7 px-2 text-xs).
- Possibly an overflow (…) menu on rows for less-common actions (Cancel, Retry,
  View details). UNVERIFIED.

### Desktop behavior

- Export row buttons (lines 288–311): plain `Button` with `Padding(10,6,10,6)`
  `FontSize=12`. No variant styling.
- Top-level actions "Start Export" and "Import Catalog" (xaml lines 98–113):
  also plain `Button`. **No primary/accent styling applied** — these should
  arguably be the primary actions of the card (especially Start Export).
- No overflow menus anywhere in the page. No Cancel / Retry on running or
  failed rows.

### Gap

- **No primary button styling** on the two headline actions. Webui likely uses
  `bg-primary text-primary-foreground` at minimum for the first one.
- **No Cancel action** on running jobs. AdminApi has no `CancelJobAsync`
  surfaced here (would need to verify). If webui surfaces cancel, desktop is
  missing it.
- **No Retry action** on failed jobs. If webui offers retry (it likely does
  for `catalog_import` since setup is captured in the request payload),
  desktop is missing it.
- **Small-button sizing** — desktop uses default button padding; webui's
  `size="xs"` is significantly tighter (h-7=28px). Visual drift.

### Severity

Visual — LOW. Functional — MEDIUM if Cancel/Retry exist in webui.

---

## 10. Data / API integration

### Webui behavior (UNVERIFIED, but anchored to server API)

Endpoints the webui uses (per the AdminApi surface already present):

- `POST /api/v1/admin/catalog/export` (sync) or `POST /admin/catalog/export-jobs`
  (async queued — desktop's `CreateExportJobAsync`).
- `POST /api/v1/admin/catalog/export-jobs/{id}/publish`
- `POST /api/v1/admin/catalog/import-jobs` — `CreateImportJobAsync`.
- `GET  /api/v1/admin/catalog/import-sources` — `GetCatalogImportSourcesAsync`.
- `GET  /api/v1/admin/catalog/local-import-sources` — `GetLocalImportSourcesAsync`.
- `GET  /api/v1/admin/jobs?job_type=...&limit=...` — `GetJobsAsync`.

### Desktop behavior

- VM wires all of the above. See `AdminMaintenanceViewModel.cs` lines 59–112
  (refresh methods) and lines 116–164 (mutation methods).
- Errors from `RefreshBucketSourcesAsync` / `RefreshLocalSourcesAsync` are
  silently swallowed (VM lines 100, 111) — "non-fatal — dialog degrades
  gracefully". This is deliberate and likely correct, but if the webui surfaces
  a warning when bucket detection fails it would diverge. UNVERIFIED.
- `SubmitImportAsync` rethrows after setting `ErrorMessage` (line 150); this
  lets the dialog know to stay open. Correct pattern.

### Gap

- **No `GET /api/v1/admin/jobs/{id}` detail fetch** exposed from this page — if
  webui offers a row-click to a detail view, desktop lacks it.
- **No `DELETE /api/v1/admin/jobs/{id}` cancel** wired — if webui exposes
  cancel, desktop lacks it.
- **No use of `useQuery` equivalent with staleTime** — desktop VM has no cache
  layer or stale-time semantics (parity line 901 tracks "admin nodes / providers
  / api-keys / invite-codes / subtitle providers / rate limits / user defaults
  30s staleTime" as MISSING; same pattern applies here).
- **No refetch-on-window-focus** (parity lines 1045).

### Severity

Functional — MEDIUM on cancel/detail, LOW on caching.

---

## Prioritized Fix List

### P0 (critical pattern — matches task-prompt flagged regressions)

1. **Stop hiding the entire page on loading/error.** Move the `ProgressRing` /
   error banner into each card's body (per-card skeleton + per-card error),
   leaving the page header, actions bar, and sibling cards rendered.
   - Files: `AdminMaintenancePage.xaml` lines 19–37 (wrapper Grid + root
     visibility bindings), `AdminMaintenanceViewModel.cs` per-section error
     fields.
2. **Add realtime refresh while jobs are active.** At minimum a 2–5s polling
   timer that ticks while `ImportJobs`/`ExportJobs`/`AllJobs` contains any row
   whose `Status == "running"` or `"queued"`. Better: subscribe to the server's
   `jobs` event channel via `EventChannelClient` (per
   `docs/events-channel-spec.md`) and re-fetch on the matching event types.
   - Files: `AdminMaintenanceViewModel.cs` add a timer or channel subscription;
     start on `Page_Loaded`, stop on `Unloaded`.
3. **Fix `formatExportProgressLabel` to use thousand separators.** Update
   `FormatJobProgress` (`AdminMaintenancePage.xaml.cs` line 479) to use
   `ProgressCurrent.ToString("N0")` / `ProgressTotal.ToString("N0")`.
   - Parity master line 1067.

### P1 (important gaps)

4. **Add `job_type` filter to the Job History card.** A ComboBox above the list
   bound to `AllJobsTypeFilter`, re-calling `RefreshAllJobsAsync(typeFilter)`.
   - Parity master line 955.
5. **Give export and all-jobs rows the same progress bar treatment as import
   rows,** so the visual language is consistent. Move `BuildProgressBar(...)`
   invocation into `BuildExportJobRow` and `BuildAllJobRow`.
   - `AdminMaintenancePage.xaml.cs` lines 206–317 and 319–378.
6. **Primary-action styling on "Start Export" / "Import Catalog".** Apply
   `AccentButtonStyle` (or equivalent primary Button style) so the visual
   weight matches webui `variant="default"`.
   - `AdminMaintenancePage.xaml` lines 98–113.
7. **Harden path rewrite editor + conflict mode** to reach webui parity
   (parity lines 1066, 1068). Without the webui source, the exact delta
   needs a follow-up pass — but at minimum validate prefix normalization and
   ensure rewrites are sent with the import request.
8. **Add deep-links from All Jobs rows** to their subject (library page, item
   page, logs). E.g. `library_refresh` → `AdminLibrariesPage` with the library
   selected; `catalog_export` completed → open download URL.
9. **Verify exact page header copy + subtitle text against webui.** Today's
   subtitle "Operational tools that affect the whole catalog live here..."
   is a desktop-authored paraphrase.

### P2 (polish)

10. **Replace the 0.6-opacity divider** (lines 147, 181, 215) with a solid
    `BorderBrush` separator to match webui `border-t`.
11. **Add "running" spinner** (Indeterminate `ProgressRing` 12px) to the status
    badge when `status == "running"`, matching webui `Loader2` animation.
12. **Add Cancel / Retry affordances** on running and failed rows if webui
    offers them (UNVERIFIED; check webui first).
13. **Apply `xs` button sizing** (height 28, padding 8×4, FontSize 12) to
    row-level action buttons to match webui `size="xs"`.
14. **Localize progress status strings** — `Done` / `In progress` / `queued`
    at `.xaml.cs` lines 484–488.
15. **Verify exact empty-state strings** against webui copy.

---

## Summary Table

| # | Feature | Webui | Desktop | Gap | Severity |
|---|---------|-------|---------|-----|----------|
| 1 | Page header title | `text-3xl` (30px) / bold (UNVERIFIED) | `FontSizeDisplayLarge` (≈34–42px) Bold | Size drift | Visual LOW |
| 2 | Page header subtitle | UNVERIFIED copy | Desktop-authored paraphrase | Likely text mismatch | Visual LOW |
| 3 | Actions bar | Card + Start Export + Import Catalog (B56) | Present | — | OK |
| 4 | Start Export button styling | Primary/accent (UNVERIFIED) | Plain `Button` | No variant styling | Visual LOW |
| 5 | Import Catalog dialog — source selector | Present (UNVERIFIED exact options) | 4 options present | Likely parity | OK/UNVERIFIED |
| 6 | Import Catalog dialog — conflict mode | Present (parity 1068 PARTIAL) | Present | Flagged PARTIAL | Functional MEDIUM |
| 7 | Import Catalog dialog — path rewrites | Present (parity 1066 PARTIAL) | Present | Flagged PARTIAL | Functional MEDIUM |
| 8 | Recent Imports card — progress bar on rows | UNVERIFIED | Present | — | OK |
| 9 | Recent Exports card — progress bar on rows | UNVERIFIED | **Missing** | Inconsistent | Visual MEDIUM |
| 10 | Job History card — progress bar on running rows | UNVERIFIED | Missing | Inconsistent | Visual MEDIUM |
| 11 | Status badge colors | Semantic tokens (UNVERIFIED) | Hard-coded Tailwind-400 hex × 33 alpha | Likely matches | OK |
| 12 | Type badge style | `<Badge>` variant (UNVERIFIED) | Outline pill | Likely drift | Visual LOW |
| 13 | Progress label formatter | `formatExportProgressLabel` with thousand separators (parity 1067) | Raw "X / Y" | **Missing `:N0`** | Visual LOW |
| 14 | `job_type` filter UI | Present (parity 955 MISSING on desktop) | **Missing** | Gap | Functional MEDIUM |
| 15 | Pagination / load-more | UNVERIFIED | Fixed limit=50 | Likely gap | Functional LOW |
| 16 | Sort headers | UNVERIFIED | N/A (list, not table) | Possible gap | UNVERIFIED |
| 17 | Loading state | Per-card skeleton (UNVERIFIED) | Centered ProgressRing **hides entire page** | **Critical pattern** | **Functional HIGH** |
| 18 | Error state | Inline per card (UNVERIFIED) | Centered error **hides entire page** | **Critical pattern** | **Functional HIGH** |
| 19 | Empty state copy | UNVERIFIED | "No catalog import jobs yet" etc. | Exact text unverified | Visual LOW |
| 20 | Realtime refresh | Event channel or 2–5s polling (UNVERIFIED, but parity 956 flags 2s pattern) | **None** | **Critical pattern** | **Functional HIGH** |
| 21 | Deep link: row → library / item | UNVERIFIED | **None** | Gap | Functional MEDIUM |
| 22 | Deep link: failed → logs | UNVERIFIED | **None** | Gap | Functional MEDIUM |
| 23 | Export row actions (Download / Publish / Copy URL) | Present (UNVERIFIED exact) | Present | — | OK |
| 24 | Cancel action on running jobs | UNVERIFIED | Missing | Possible gap | UNVERIFIED |
| 25 | Retry action on failed jobs | UNVERIFIED | Missing | Possible gap | UNVERIFIED |
| 26 | Overflow menu per row | UNVERIFIED | Missing | Possible gap | UNVERIFIED |
| 27 | Card corner radius | `rounded-xl` (12px) (UNVERIFIED token) | `CornerRadius=12` | Match | OK |
| 28 | Card divider | `border-t` / `<Separator>` | `Height=1` + `Opacity=0.6` | Wrong — should be full opacity | Visual LOW |
| 29 | Transient status banner | `bg-success` (UNVERIFIED) | `#22C55E` green | Likely match | OK |
| 30 | API: list jobs | `GET /admin/jobs` | `GetJobsAsync` | — | OK |
| 31 | API: export-jobs publish | `POST .../publish` | `PublishExportJobAsync` | — | OK |
| 32 | API: import sources (bucket + local) | Two GETs | Both wired | — | OK |
| 33 | `useQuery` staleTime / cache | Present (UNVERIFIED exact) | None | Parity gap (line 901 pattern) | Functional LOW |
| 34 | Libraries header "Catalog Maintenance" link | Present (parity 929 MISSING on desktop) | **Present** (xaml L99–107, code L713) | Fixed since parity note | OK |
| 35 | Nav entry in AdminShell | Present | Present (NavMaintenance L548–577) | — | OK |

---

## Closing note

The desktop page **exists and is wired** — B56 ("page content mismatch") has
already been addressed in the sense that the page is now a catalog import/export
UI. The remaining parity work is the set of P0/P1 items above, most importantly:

1. Stop hiding the entire page on loading/error (HIGH).
2. Add realtime refresh for running/queued jobs (HIGH).
3. Reach full parity on path rewrites / conflict mode / progress-label
   thousand separators (MEDIUM, already tracked in parity master).

All rows marked **UNVERIFIED** in this document need a second pass with
`F:\continuum-server\web\src\components\AdminCatalogMaintenance.tsx`,
`AdminJobHistory.tsx`, `adminCatalogMaintenanceFormatters.ts`,
`adminCatalogMaintenancePathRewrites.ts`, and `web\src\app.css` directly
readable. This session's sandbox blocks that tree.
