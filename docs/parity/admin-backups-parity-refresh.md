# Configure > Server > Backups — parity refresh against official main 8d253adbf

Status: source and tests complete for the contract, the restore flow and the list; visual parity is **not** certified (no official
server or screenshots to compare with; layout is read from the official source). Scope: `/admin/backups`,
`/admin/backups/:id/restore`, `/admin/backups/uploads/:uploadId/restore` and their dialogs.

- Official reference: Silo Server/WebUI `8d253adbf00dda7b9714f336dae767c7281b1081` (newest official main at the start of this pass),
  read through the blobless clone with `git show`/`git grep`/`git log` only.
- Previous pin recorded in [admin-backups.md](admin-backups.md): `92ee051878c2a791940ec9598e2bdd0f04890b3e`.
- Desktop base: `origin/beta` `5b4e102b13b9f5e5fbbaa838b95b0b3b63b1cf9f`.
- Evidence: `backupsref-baseline-r1` (unchanged beta, `admin-configure-backups`: 36 checks and the completion record, all PASS),
  `backupsref-final-r4` (see "Verification").

## Upstream changes that reach this area (`92ee05187..8d253adbf`)

`git log` over the pages, `admin-backups/*`, the restore API types, `admin_backup*.go`, `internal/backup/{selective_*,users_plan,listing}.go`,
the OpenAPI document and `en.json`: 50 commits touch the area's web and handler files. Grouped by what they change:

| Group | Upstream commits | Effect on the native page |
| --- | --- | --- |
| **Contract** | #4757 restore change lists are codes (`changes` enums on home sections and template collections, `missing_collection_items`); #4948 `local_username` carries the email when the account has no username | Fixed (below) |
| **Behavior** | #4678 no next-run forecast while backups are paused; #4682 no "Back up now" in the empty state while it cannot run; #4855 an unreadable destination must not read as "No backups yet."; #4717 the old schedule's next run is hidden after a Start time save; #4737/#4693 toggle wording follows the number of items (`amount` select, `count` plural); #4928 an invited account's email once; #4948 merge target named by email; #4963 focus and scroll on a restore step change | Fixed (below) |
| **Strings** | 26 catalog strings changed (singular pronouns in the blocked-group titles, "Show it/Hide it", exact-count toggle labels), 25 added (change labels, environment-managed restore strings, `schedule.timeDescriptionLocal`) | Catalog regenerated and pinned by a test |
| **Layout, wrapping, bidirectional, focus** | #4680 #4694 #4723 #4746 #4760 #4724 (markers and icons stay on the first line), #4708 #4715 #4738 #4755 #4788 #4848 #4849 #4875 #4907 #4921 #4947 #4969 (long names wrap, clip or isolate), #4711 #4878 #4942 #4979 #4766 (left-to-right emails, paths and URLs in right-to-left), #4758 #4798 #4803 #4810 #4740 #4709 #4797 (status lines, refusal button, footer note, phone header, sticky panels), #4824 (plain folder paths) | Mapped row by row in the table below; what a WinUI control cannot do is listed as such |
| **Environment** | #4665 backup settings and SILO_BACKUP_* from the environment (read-only fields with their variable) | Not re-implemented: pull request #234 owns it; the page already locks keys the sensitive-status read lists (`ManagedByEnv`) |
| **Server only** | #4670 nightly slot recorded for the lock, #4706 daily triggers keep wall-clock time across DST, #4602 plugin provisioning, #4409/#4411/#4398 and other features touching backup tables | No client contract change (OpenAPI diff of every Backup schema and path: only the change codes, `missing_collection_items` and one description) |

## Contract differences and how each is handled

| Difference | Handling | Older server |
| --- | --- | --- |
| `changes` on `AdminBackupRestoreHomeSection` and `AdminBackupRestoreCollection` are enums of codes (`type`, `title`, `title_translation`, `position`, `featured`, `item_limit`, `enabled`, `web_only`, `collection_replaced`, `configuration`, `collection_items`; `title`, `description`, `visibility`, `featured`, `sort_order`, `item_sort`, `source`, `sync_schedule`, `poster`, `backdrop`, `libraries`) | Each code is shown with the official English word (`web.admin.backups.restore.rows.change.*`); `collection_items` takes its count from `missing_collection_items` (0 when absent, also for a template collection, as the official client does); an unknown code is shown as sent (plain text); a null entry is refused | Supported: an older server's English words ("item limit", "3 collection items") are not codes and show as sent |
| `missing_collection_items` (new field) | Read as `long?`; a negative count is refused | Supported: absent reads as 0 |
| `local_username` is the matched account's username or its email | Merge target = `local_username`, else the backup account's username, else the row's own name | Supported: an older server leaves it empty and the fallbacks name the account |
| Backup task `triggers[type=daily].time_of_day` (already in the schema, now read) | New `GetBackupScheduleAsync`; `GetNextBackupRunAsync` stays and delegates | Supported: no triggers reads as no daily time (the wait ends after 30 s) |
| Home-section kind name | Home row catalog (`AdminHomeSectionsKinds.Label`) instead of a capitalised type key | n/a |

Request bodies: **none changed.** The restore start, backup start, delete, download-link, S3 test, key acknowledge, upload and settings writes
send the same bodies as before; the official request types did not change in this range.

## Difference table (official `8d253adbf` against native on beta)

Outcome: **fixed** (changed in this pass), **right** (already matched), **cannot** (not possible in WinUI, reason given), **n/a**.

| Area | Official | Native before | Outcome |
| --- | --- | --- | --- |
| Restore change list | Words from codes, count for `collection_items`, unknown as sent | Raw strings printed | fixed |
| Blocked-group titles | Singular pronoun for one item | Plural for one | fixed (catalog) |
| "Show them / Hide them" (blocked, never restored, same items) | `count` plural ("Show it" for one) | Always plural | fixed |
| Warning toggles (accounts, files, group files) | `amount` select (exactly one, exactly two, other) | One label | fixed |
| Account row email | Email shown once (not when equal to the name); username shown when different | Email always shown | fixed |
| Account merge target | `local_username` (email when no username), then username, then the row name | `local_username`, then username | fixed |
| Home-section kind in the row detail | Home row catalog name | Capitalised type key (`recently_released` read "Recently released", the catalog says "New releases") | fixed |
| Status line next run | None while paused (no storage, SQLite user store, key not confirmed) | Forecast shown | fixed |
| Status line after a Start time save | No forecast until the daily trigger holds the time or 30 s pass; task read every 2 s meanwhile | Old schedule's forecast | fixed |
| Empty state | Title; sentence and button only while Back up now can run; unconfigured sentence when not configured | Sentence and button always when configured | fixed |
| Unreadable destination | Neither "No backups yet." nor the list; the warning says why; location note hidden | "No backups yet." | fixed |
| Failed first list read | Alert with working Try again | Same | right |
| Phone header (< 640 px) | Actions on their own full-width row, blocker text under the button | Right-aligned column | fixed |
| Saved backups table (< 640 px) | Header hidden; rows stack: date and kind, status at the end, size, actions underneath | Header and columns kept, content ran past the page | fixed |
| Saved backups table version column | From 768 px | From 640 px (a zero-width column wrapped its header into a tall box) | fixed |
| Restore rows (< 42 rem) | Name above its detail | One line, both cut | fixed |
| Restore account badges (< 52 rem) | Icon only from 42 rem, none below; status in the row from 52 rem (a merge naming another account from 42 rem), in the details otherwise | Badge text and count always | fixed |
| Review and Run width | `max-w-[70rem]` (1120 px) | 800 px, left aligned, narrow panels | fixed |
| Summary beside the sections | From `lg` (1024 px) | Same, but the 32 px column gap was kept when stacked | fixed |
| Step change | Page to the top; focus on the current step when it was on the page | Nothing | fixed (a tab stop only while it holds focus) |
| Confirmation button | Filled `--destructive-fill` (#dc2626) | Shared destructive style (lightens on hover) | fixed (page-local style: #dc2626, #b91c1c hover, #991b1b pressed) |
| Third-party names (backups, accounts, profiles, libraries, folders, upload server names, server messages) | Isolated with `<bdi>`/isolates, clipped when they cannot wrap | Shown raw | fixed differently: neutralised (below) |
| Page width | Document fills the viewport minus gutters; the restore sections take the room the summary leaves (`1fr` + 320 px) | Body shrank to its widest child, and at narrow windows the content was wider than the page by the shell's two gutters | fixed (own defect) |
| Breakpoints | `max-sm`, `md`, `lg` ask the viewport; row details ask the row's own container (`@2xl`, `@[52rem]`) | Page width for everything | fixed: window width for the first group (summary beside the sections from a 1024 px window), the content's own width for rows |
| Status line (< 640 px) | Wraps | Ran past the page edge | fixed (next run goes under the last one) |
| Environment-managed settings | Read-only field with the variable | Locks keys from the sensitive-status read | right; the rest is #234 |
| Sticky summary, scroll margins, max-height | CSS sticky | Summary is not sticky | cannot: a `ScrollViewer` child cannot stick; the summary scrolls with the sections |
| Right-to-left mirroring, `<wbr>` breaks, Firefox/Chromium clipping fixes (#4711 #4878 #4942 #4979 #4766 #4921 #4849 #4788) | CSS | Not applicable | cannot: WinUI mirrors with `FlowDirection` and has no per-run isolation like `<bdi>`; names are neutralised instead (direction characters removed), and folder paths and URLs follow the page direction |
| Names that cannot wrap are clipped sideways | CSS overflow | Names wrap (`TextWrapping.Wrap`) or end in an ellipsis beside a detail | cannot (different control behaviour); no name is cut where it has room |
| Toasts | Title and description | Title and description joined | right (recorded earlier) |
| `restore.reason/blocked/caveat/setting.managedByEnvironment` | In the catalog, used by no official code at 8d253adbf | In the catalog | right: strings present; the server emits no such reason at this commit, and the native code shows a sentence for it if one arrives |
| `schedule.timeDescriptionLocal` | In the catalog, used by no official code | In the catalog | right (unused upstream) |

## Safety (unchanged or strengthened)

Every guarantee in [admin-backups.md](admin-backups.md) stands. This pass added:

- A restore, delete or upload acts on the item that was on screen and confirmed; a restore is sent once; a refused restore (422) keeps the
  draft and shows the server's reason; a dropped connection after the request left is an unknown outcome that blocks another restore and
  never re-sends it (native fixture: refusal and unknown-outcome paths; unit tests existed).
- Third-party text (backup, account, profile, library and folder names, upload server names, server messages) is plain display text:
  `AdminBackupText.Plain` removes control characters and bidirectional formatting (embeddings, overrides, isolates, marks), turns line and
  paragraph separators and tabs into one space, replaces invalid UTF-16 and ends a name over 200 characters in an ellipsis (paths 400,
  messages 500). Joiners stay so joined emoji keep their shape. Review sentences, the "Left out" list, the report, confirmations, rows,
  dialogs and the omitted-account lists use it; `SafeServerText` applies it before redacting.
- The destructive confirmation button is page-local; the shared destructive style and `DarkTheme.xaml` are untouched.
- Typed S3 credentials, upload bytes and file names: no code touching them changed.

## Deliberate differences

- No sticky summary on a wide page (see table).
- A name that fits is never cut; the official clipping of an unbreakable run is not reproduced.
- Environment-managed settings are left to pull request #234.
- A restore reason code without a sentence shows nothing (the official client would show a blank or an undefined label).
- The leave-guard "Discard" button on this page is the shared admin dialog's (shared control, not changed).

## Verification

All runs: lane `D:/SiloPlayer-build/admin-completion-lane-b`, task id `admin-families-nov20`, one build lease at a time through
`accept-family-v2.ps1`. Nothing touched the installed player, `%LOCALAPPDATA%\SiloPlayer`, `C:\Program Files\Silo Desktop Player` or
`D:\SiloPlayer`.

| Run | Source | Result |
| --- | --- | --- |
| `backupsref-baseline-r1` (publish-native) | unchanged beta `5b4e102b1` | `admin-configure-backups`: 37 PASS lines (36 checks and the completion record), none failing |
| `backupsref-final-r4` (stage final) | `a997898af` (source hash `cc161e87a31e44c6d13b13a51c4aa3b222d09e6daa41b39a8e1b7b8bcc3f1c77`) | focused `FullyQualifiedName~AdminBackup`: 226 passed, 0 failed, 0 skipped; full unit suite: 10,734 passed, 0 failed, 0 skipped; app build, publish and native host build passed; natives: `admin-configure-backups` 68 PASS lines (67 checks and the completion record), `admin-configure-general` 188, `admin-configure-security` 374, `admin-manage-foundation` 36, `admin-monitor` 113; source and payload seals verified |

Earlier final runs and why they were repeated: `final-r1` passed everything but a new native check I had written for focus on a step change
(the page moved focus only when XAML had dropped it; XAML moves it to another control when the focused one leaves the tree); `final-r2` passed but
captures then showed layout defects fixed afterwards (content as wide as the shell's gutters allow, the saved-backups phone layout, wrapping status
line, window-width breakpoints, body filling its width), so `final-r3` and `final-r4` were run on the final source. The known-intermittent unit tests
(`AdminStorageSettingsControllerTests`, `LibraryBackgroundRefreshTests`, `AdminDashboardStoreTests...`) passed in every full run; none needed a rerun.

New unit tests (`AdminBackupsRefreshTests`, `AdminBackupsControllerTests`): every official change code (section and template collection), the count
form, the absent count, unknown codes, older-server words, control characters in a code, malformed lists refused, new and old contract forms,
account contact and merge destination, home-section kind names, plain-text rules (separators, overrides, isolates, marks, controls, joiners,
bound, lone surrogate), `SafeServerText`, review sentences and caveats with hostile names, upload server names, paused schedule, toggle and
blocked-title wording, the 51 changed or added official strings verbatim, reason codes without a sentence, and the Start time wait (daily
trigger read, settle, expiry after 30 s, nothing waited for after other saves, hidden page, leaving the page).

New native checks (`admin-configure-backups`, 31 added to the earlier 36): change words and count in rows, older-server words as sent, plain names,
email shown once, merge target by email, no control or direction characters anywhere in Choose and Review, narrow row stacking, summary beside the
sections only from a 1024 px window, focus on the current step, refusal keeps the draft and shows the reason, unknown outcome blocks and never
re-sends, the restore page drops its controller on leaving, unreadable destination, failed read with Retry, empty list with and without a blocker,
paused forecast, Start time wait, phone header, the delete confirmation's own red style with its hover and pressed colors. No earlier check was
removed or weakened; the account row's layout was replaced, and its checks (ids, toggles, names) are the same ones.

Captures (PNG, results folder of each run, `backups-<state>-<width>.png`): list, list-unreadable, list-failed, list-empty, restore-choose,
restore-review, restore-run and restore-report, each at 1440, 1024, 800 and 560 px window width. Opened and read against the official source:
list 1440, 800 and 560; unreadable 1440; failed 560; empty 800 and 560; choose 1440, 1024, 800 and 560; review 1440, 800 and 560; run 1024;
report 560 (earlier runs' copies, plus list 1024, choose 560 and unreadable 560 from the final run). Not opened: the other widths of the same states. Popup layers
(the confirmation, download, upload, key and S3 dialogs) cannot be captured by the host; they were checked through automation (buttons, styles,
state colors, ids and results).

## Not verified

- Live server, an official server build, pixel parity and the official screenshots: none available. Layout is read from source.
- Right-to-left rendering, IME, Narrator, high contrast and non-100 % DPI.
- The focus move on a step change was checked for the pointer-focused case; keyboard-only use (Enter on a focused button) was not driven.
- Environment-managed settings behavior beyond what beta already had (pull request #234).
- The OS file pickers and drag and drop (the fixture replaces them with page seams).

## Shared files

None changed. `AdminText.cs`, `DarkTheme.xaml`, the shared destructive style, `AdminSettingsUi` and the shell are untouched. Read only: the Home
row catalog (`AdminHomeSectionsKinds.Label`, used by Manage Home sections) for the section kind names.

## Touching pull request #234's lines

Not touched: `LoadSettingsAsync`, the S3 dialog's form, the `ManagedByEnv` lines. In `AdminBackupsController.cs` the changes are in the state record and
constants (top), `ReadScheduleAsync` (its answer now also carries the daily time), `SaveAsync` (reads the sent Start time under the existing lock and
starts the wait after an accepted save), new methods placed before the "Commands" section, and `SetVisible` and `CancelTimers`/`Clear`.
