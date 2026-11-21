# Admin user resources — parity refresh against official `a84b2551b2`

Scope: Manage → Users → Access groups (`/admin/access-groups`, `/:id`), Devices (`/admin/devices`, `/:userId/:deviceId`) and History import
(`/admin/history-import`). The first pass ([admin-user-resources-2026-11-17.md](admin-user-resources-2026-11-17.md)) was built against
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` and merged as #218. This pass compares the pages with the newest official main,
`a84b2551b23646380e1eecef45fdbfdbeecdfed3`, read from the blobless clone with `git show` / `git log` / `git diff` only. Branch
`t3/admin-user-resources-parity-refresh`, started from beta `aca3dcb212f3f85458de4e7e6c875368237bb54c`. No version number is changed.
There is no official server or screenshot to compare with: nothing here claims visual or pixel parity.

## What was compared

- **Source.** The three pages, their imports (a 198-file transitive closure of `web/src`) and the commits between the two pins
  (`git log 217ff25abc..a84b2551b -- <closure>`).
- **Wording.** All 562 messages in `admin-user-resources.en.json` were rendered from the official `en.json` syntax tree and compared
  one by one, after ICU quote normalisation: 0 differences, 0 removed keys, 0 official keys under the three pages' prefixes missing.
  Six official keys (`web.admin.common.subtitleAppearanceDialog.*`) were added to the catalog (556 → 562); nothing is hard-coded.
- **Contract.** 53 operations the pages call (`/admin/access-groups*`, `/admin/devices*`, `/admin/history-import*`, `/admin/history-imports*`,
  `/history-imports/*`, `/admin/request-groups/*`, `/admin/request-settings`, `/admin/users*`, `/settings/values*`) and the 102 schemas they
  reference, `217ff25abc` against `a84b2551b2`: the only differences are `AdminUserDownload` and `AdminUserWatchSummary`, reached only by
  the user-detail downloads and watch-summary reads, which these pages never call. The settings manifest has no diff. The server
  (`internal/`) has no change under access groups, devices or history import; the only device-related Go change is `internal/auth/device_login.go`,
  unrelated.
- **Request bodies and URLs.** Unchanged and not touched: no file under `src/SiloPlayer.Core/Api` changed. A subtitle appearance write is the
  same `PUT /api/v2/admin/users/{id}/settings/values/playback.subtitle_appearance?scope=profile_device&profile_id=…&device_id=…` with the same JSON
  object value as before.

## Upstream changes that reach the pages

| Commit | Change | Page | Outcome |
|---|---|---|---|
| `58e649e1a` #4543 | A failed subtitle appearance save is dropped from the editor (`useAutosavedDraft`) | Devices | **fixed**, see below |
| `c726de0ac` #4864 | Segmented controls: named group, `aria-pressed`, selected bar | Devices | **fixed** |
| `3ebeb42eb` #4826, `34ce1844c` #4845, `5b0ae88ee` #4811, `b1edb85ee` #4850 | Dimmed text, counts, checkbox border, anomaly badge colour; rows without an override no longer dimmed | Devices | row dimming **fixed**; counts **fixed** (inherit the button colour); anomaly colours **already right** (verified); checkbox border **not changed** (system CheckBox) |
| `eea8cce6c` #4889 | Firefox focus scroll under sticky header | Devices | **not applicable** (browser only) |
| `980e4555b` #4910, `a7f067168` #5016, `fd1835468` #4783, `b30088046` #4735 | Keep the start of long names/emails when cut, `dir="auto"` | Devices, Access groups | **fixed** |
| `43d38e533` #4998, `5d27f225c` #5027 | Whole first character, upper-cased in the interface language | Devices (profile tabs) | **fixed** |
| `8e4c8f7c0` #4887 | Add users: usernames readable beside long group names | Access groups | **fixed** |
| `f6ed24869` #4700, `160a4425e` #4704 | Red buttons use `--destructive-fill #dc2626`; delete/disconnect confirms use the destructive style | all three | **fixed** (page-local style) |
| `dbceadcaf` #4750 | Primary button label readable on hover | all three | **already right**: the native hover (accent at 90% over the page) reads at the ratio logged by the fixture |
| `ee462aeb0` #4911 | History import run status labels use the `*-label` colours | History import | **already right**: native chips are untinted; error, warning and success text on the page surface were measured by the fixture (≥ 4.5:1) |
| `5537df46a` #4766 | Server URL fields and displays read left to right | History import | **fixed** |
| `a886951f4` #5034 | Token dialog footer buttons wrap instead of being cut off | History import | **not applicable**: native dialogs use the system command bar; the token dialog has no separate Remove button (see differences) |
| `3e96eb85d` #4959, `297dc7daf` #5002 | Dialog titles and descriptions wrap; titles clear of the close button | all three | titles now carry bounded plain text; the shared dialog template is unchanged |
| `4cf51bb51` #4926 | Fields grow with a larger minimum font size | all three | **not applicable** (browser setting) |
| `40fb29325` #4968, `a266b6b3b` #4971, `ea131face` #4983, `6674324d8` #4997 | Allow-for dialogs, profile editor lists, allowed-titles lists | — | **not applicable**: none of the three pages imports those components (import closure checked); they belong to the Users profile editor |
| `d9515d397` #4727, `6ddee8d39` #4810, `8ae4f4f5a` #5026, `4b1713ec1` #4753 | Path fields, settings paths, device names in other pages, copy without invisible characters | — | **not applicable**: not rendered by these pages |
| `deviceSettingGroups.ts` (`playback.auto_play_next_preview` hidden) | The viewer's own Settings → Devices hides a key with no effect | — | **not applicable**: the admin override rows (`deviceOverrides.tsx`) do not use that grouping and still offer the key; `AdminDevicePresentation` follows `deviceOverrides.tsx` |

## #4543: a device setting whose save fails

**Official after #4543 (a84b2551b2).** Only the subtitle appearance editor changed. `AdminSubtitleAppearanceDialog` keeps its value in
`useAutosavedDraft`: every edit shows at once and saves in the background, one request at a time; edits made while one is in flight merge
into one queued patch; every request is built when it is sent from the last value the server acknowledged plus that patch; a failed save
puts its own fields back to the acknowledged value without undoing newer edits (the caller reports the failure); unmounting drops edits not
sent yet while a save already in flight finishes; "Reset override" discards queued edits before deleting. The other device rows
(`RegistrySettingControl`) were not changed: each control writes one change and shows the stored value after the read.

**Native before.** The shared `SubtitleAppearanceDialog` kept the value that was typed, debounced a save, and on a failed save kept the rejected value in
the preview with "Failed to save", refused to close until a retry succeeded, and the next change saved the rejected value along with it.
The ordinary rows already followed the saved value (`RenderTabsAndRows` after a refused write).

**Native after.** The Devices page opts into a draft mode of the dialog (`SaveDraftAsync`; the in-player and profile editors never set it and
behave as before). The state machine is `AutosavedDraft<T>` in Core, with unit tests:

| Case | What happens |
|---|---|
| An edit | Shown at once; saved immediately when none is in flight, else merged into the queued patch |
| A save is refused (4xx other than 408) | Its fields leave the view; the server's safe problem title/detail/field errors are shown in the editor's status line; the draft stays editable; the failed write is not repeated; the next save is built from the acknowledged value, so the rejected edit is not in it |
| Edits made during the refused save | Kept in the view and sent next, built from the acknowledged value |
| A timeout, 5xx or lost answer after the write was sent (unknown outcome) | Never replayed. The edit leaves the view, queued edits are dropped, the editor locks, the page shows "The change may have reached the server. Reload and review…" with Reload, and the page does not write again until reloaded |
| Closing the editor | Edits not sent are dropped; a save in flight finishes; the page reads the server only after it has answered |
| Reset override | Queued edits are dropped, the clear waits for the save in flight, then one DELETE; the editor closes (official) |
| Other rows | unchanged: switch, select, number and text controls write one change; a refusal or unknown outcome returns the control to the saved value; number settings still write only a real change at the step's precision |

Also now official: the eyebrow names profile and device (`profile · device`, names as bounded plain text), "Reset override", and the status line
"Saving…" / "Changes saved automatically" / "Changing a value creates an override".

## Difference tables

Outcome: **fix**, **right** (already right), **cannot** (cannot be done in WinUI, with the reason), **kept** (a deliberate difference from the first pass).

### Devices

| Area | Official | Native | Outcome |
|---|---|---|---|
| Fleet pulse | stats, density and Group by wrap (`flex-wrap`) | `WrapPanel`: Group by moves to a second line at 560 px instead of running out of the page (asserted at 1440, 1024, 800, 560) | fix |
| Group-by control | group "Group by", buttons `aria-pressed` | `SegmentedRow` is a UIA Group named "Group by"; each option a toggle button reporting On/Off; fixture reads ToggleState and the group peer | fix |
| Scope controls (All/With overrides; All settings/Overrides) | `aria-pressed`, tooltip for no profile / no overrides | same toggle buttons in a named group; tooltips `settingsScope.noProfile` / `noOverrides` on the disabled segment | fix |
| Selected segment | surface-hover fill + 2 px foreground bar; underline in forced colors | surface-hover fill under the foreground label (not the system accent), 2 px bar, underline in a Windows contrast theme | fix |
| Saved views, profile tabs | counts and labels readable | label/counts inherit the button colour, so selected and hover stay readable; tab chips likewise | fix |
| Facet checkboxes | `border-input` border | the system CheckBox; the captures draw an unchecked box with a dash glyph, not investigated | cannot (not restyled) |
| Group header | sticky, opaque | scrolls with the list; label cut with an ellipsis | label fix, sticky kept |
| Device row | name `dir=auto truncate`, anomaly dot after it, platform line `truncate` | `NameWithMark` keeps the dot and cuts the name; reading order from the text; line breaks read as spaces | fix |
| Row without an override | not dimmed (#4850) | no 0.85 opacity; hint italic | fix |
| Detail header | title wraps, facts `flex-wrap` | `WrapPanel` title and facts beside the tile | fix |
| Profile tab | `getInitial(name)`, 120 px `truncate dir=auto` | `AdminNameText.Initial` (whole grapheme, interface-language upper-casing), 120 px cut | fix |
| Anomaly badges and reason | `destructive-label` on a 10% tint | untinted chips; `ErrorBrush` on the surface measured ≥ 4.5:1 | right |
| Per-row controls, badges, inherited sources, Reset | as in the first pass | unchanged | right |
| Subtitle appearance editor | autosaved draft (#4543), eyebrow, status | see above | fix |
| Reset one / Reset all confirmations | destructive fill; profile name in title | `#dc2626` fill, 90% on hover, contrast theme uses system colours; profile name as bounded plain text | fix |
| States | loading, empty, failed read with Retry | unchanged; Reload stays reachable | right |
| Layout | three panes wide, stacked narrow | unchanged (wide ≥ 1050 px); list height fixed at 640 wide, official `max(100vh-22rem, 10rem)` | kept |
| Wording, URLs, bodies | — | 562 messages identical; requests unchanged | right |

### Access groups

| Area | Official | Native | Outcome |
|---|---|---|---|
| Card name | `truncate`; two lines below `lg`; Default badge `shrink-0` | one cut line from 1024 px, two lines below, badge never cut | fix |
| Card description | `dir="auto"`, `line-clamp-2` | two lines, reading order from the text | fix |
| Delete button | ghost, `text-destructive` | outlined button with a red label | fix |
| Delete confirmation | destructive action; title names the group | destructive fill; group and default-group names as bounded plain text | fix |
| Members list | username `dir=auto truncate`, email `emailDir` + `truncate` | same; `AdminNameText.EmailIsLeftToRight` ports `emailDir` | fix |
| Add users | names share the row in equal parts, each up to its full width | `SharedNamesRow` (equal shares up to natural width, group name at the row's end) | fix |
| Add users / move confirmations | group names in titles | bounded plain text | fix |
| Phone save bar, toast | pinned bar, toast | Save/Discard/Delete at the end of the editor at every width | kept |
| Create, editor fields, request terms, member moves | as in the first pass | unchanged; request bodies unchanged | right |

### History import

| Area | Official | Native | Outcome |
|---|---|---|---|
| Server address | `dir="ltr"` input | the input and the shown address lay out left to right | fix |
| Run status | `*-label` colours on a tint | untinted chips; error/warning/success measured ≥ 4.5:1 | right |
| Delete source, remove mapping | destructive confirm; names in text | destructive fill; names as bounded plain text | fix |
| Token dialog | separate destructive "Remove" button, wrapping footer | a "Remove API key" checkbox with Save | kept (no red button exists to restyle) |
| Run list title | wraps | `WrapPanel`: status, source and mode badges wrap at 560 px | fix |
| Discovered users | id `truncate` | id cut with an ellipsis | fix |
| Sources, mappings, runs, polling, bodies | as in the first pass | unchanged | right |

## Avatar initials and name cutting

There is no shared helper in the native app. Search found separate copies: `AdminDevicesPage.Detail.cs` (the only avatar initial on these
three pages; now `AdminNameText.Initial`), `Views/Admin/AdminActivityPage.Rows.cs:200` (`username[..1].ToUpper`, splits a surrogate pair),
`Converters/StringToInitialConverter.cs` (`s[0]`, used by the profile-select and household-setup pages), `Views/PersonDetailPage.xaml.cs` and
`Views/LibraryPage.xaml.cs` (`GroupInitials`). Only the Devices page is changed; the others are reported, not touched. The Users and Dashboard
pages were not changed (overlap with #228/#231).

`AdminNameText` (Core) is used by these three pages only: `Initial` (first extended grapheme cluster of the trimmed name, upper-cased by
the interface language: Turkish `i` → `İ`, `ß` → `SS`; `""` when there is none, shown as `?`), `LeadingGraphemes`, `SingleLine` (line breaks as
spaces, as HTML collapses them), `Label` (control, line/paragraph separator and direction characters → one space, white space collapsed,
at most 80 characters cut at a whole character with `…`; emoji joiners kept) and `EmailIsLeftToRight`.

## Shared files touched

| File | Other users | Change |
|---|---|---|
| `Controls/SubtitleAppearanceDialog.xaml(.cs)` | in-player appearance (`PlayerOverlay`), viewer Settings, `PlayerService` | opt-in draft mode (`SaveDraftAsync`, `Eyebrow`, `ResetLabel`, `WhenSavesSettledAsync`); `x:Name` on the reset label. Nothing changes unless `SaveDraftAsync` is set, which only the Devices page does; the `subtitle-preferences` fixture path (`SaveOverrideAsync`) is unchanged |
| `Views/Admin/AdminUserResourceUi.cs` | none (the three pages only) | helpers, destructive style, segmented group, name rows |
| `Models/Admin/admin-user-resources.en.json` | none | six official keys |
| `docs/parity/admin-monitor-surfaces.json` | architecture test | two Core files listed |

No shared destructive style, `DarkTheme.xaml`, Users page or `admin-users` fixture was edited.

## Safety kept

Refusals show the server's safe detail and keep the draft; only a timeout, 5xx or lost transport after a write is an unknown outcome and
is never replayed; the Add server dialog still keeps a typed API key until the save is accepted or the dialog closes and never shows or logs it;
removing a member, deleting a group or device override and deleting or cancelling an import still act on exactly the confirmed record, once.
All existing native checks stay (none weakened). Names, emails, device names and server messages are plain text; confirmations and titles
neutralise control, line/paragraph and direction characters and bound the length.

## Verification

All runs used the shared script in lane `D:/SiloPlayer-build/admin-completion-lane-a` (task `admin-families-nov20`).

**Baseline on unchanged beta `aca3dcb21`.** `userresref-baseline-r1` (publish-native, admin-user-resources and history-import-auth) failed in
`admin-user-resources`: five scenarios failed, so the second selector did not run. `userresref-baseline-r2` (host-native, same published payload) failed in
the same five scenarios, so this is not an intermittent. In each, the fixture clicked a button to open a second dialog in the same scenario
and the next step found no content: "Missing control NewGroupName" (groups: create refused then accepted), "Sequence contains no matching element"
(groups: delete confirmation), "reset confirmation opened again" (devices: detail, writes, resets), "Missing control ImportSourceName" (history
import: refused create keeps the key), "Sequence contains no elements" (history import: edit and token dialogs). `userresref-baseline-r3`
(host-native, `history-import-auth` alone) passed, 25 PASS lines. The brief's statement that the group passes on beta (230 checks) was not
reproduced on this machine. The cause is fixture synchronisation: a click made while an earlier dialog is closing is dropped, and a popup
exists before its content is in the visual tree. The fixture now waits until no dialog is showing, repeats the click until the next one is open
and lets one layout pass realise it (`OpenDialogAsync`). No assertion was removed or loosened.

**Final run: `userresref-final-r4`**, `-Stage final`, commit `19df811ba`, build lease 2026-11-21T12:57:11Z–13:04:39Z. Focused 115/115 and full 10,871/10,871
unit cases (0 failures, 0 skipped); app build, publish and native host built; native selectors `admin-user-resources` 437 PASS / 0 FAIL,
`history-import-auth` 25, `admin-manage-foundation` 36, `admin-configure-general` 188, `admin-monitor` 113, each with 0 FAIL; source and payload
seals verified. Earlier final runs: `userresref-final-r1` stopped at the unit build (a syntax error in a test I had just added); `userresref-final-r2`
(commit `404b4884e`) failed the full unit suite once, in the known intermittent
`LibraryBackgroundRefreshTests.ContentRefreshRejectsPreMutationViewportAndHeldWindowSnapshots(mutation: "watched", pausedAt: 560)`; `userresref-final-r3`
(same commit, unchanged source) passed everything (10,871/10,871; native 433 / 25 / 36 / 188 / 113); `userresref-final-r4` is the run after the last change
(the fleet pulse wraps so Group by stays reachable at 560 px). Not in the final run: the native groups listed as failing on beta for other reasons.

**Tests added.** `AdminUserResourceParityRefreshTests` (38 cases): the autosaved draft (a refused save drops its fields and is not repeated, the next save is built
without it, edits in flight merge, a refused save keeps newer edits, an unknown outcome or a throwing save is never replayed and locks the draft, discard,
close, retired, reset, patches carry only changed fields), initials (ASCII, trimmed, accented, CJK, a surrogate pair, a joined emoji, a flag, a combining mark, empty
and white space, Turkish and German special casing, the interface language when no culture is given), grapheme counting, single-line text, bounded plain
text (control, line, paragraph and direction characters, joiners kept, whole-character bound) and the email direction rule. They compute behavior; none searches
source text. Native fixture (`AdminUserResourcesNativeFixture.Refresh.cs`; the whole group logs 437 PASS lines): the theme's status colours and the hover of a primary button
(measured 5.61, 6.64, 7.36 and 6.29:1 on the surface; 6.66:1 on hover); the Devices segmented controls (toggle state On/Off, group peer and name, moving the
selection, regrouping); the subtitle override editor (eyebrow, first save body, refused save drops its edit and shows the server's text, next save built without it,
edits in flight merge into one save, an unknown outcome locks and is sent once, closing drops unsent edits and the page reads after the save in flight, reset waits
and discards queued edits); long device, group, username, email and profile names (cut lines, heights, widths, tab initials, tab width, wrapping title, Add users shares);
plain bounded titles and descriptions in four confirmations; the danger fill, its label (4.83:1) and its 90% hover in five confirmations; left-to-right address; captures.

**Captures.** The fixture writes 20 PNGs per run (`refresh-{groups-list,groups-editor,devices-list,devices-detail,history-import}-{1440,1024,800,560}.png`) and the two
long-name captures, with the app background applied. Opened and compared with the official layout as read from source: `admin-devices-detail-wide.png` (baseline-r2),
`refresh-devices-detail-1440`, `refresh-devices-detail-560` (before and after the background was applied), `refresh-devices-list-1440` (r7, no background),
`refresh-groups-list-560`, `refresh-history-import-560` (before and after the run rows wrapped), `admin-access-groups-long-names-compact`, `refresh-groups-editor-800`,
`refresh-devices-list-1024`, `refresh-devices-list-560` (before and after the fleet pulse wrapped). Not opened: the other 1440/1024/800 pictures of groups-list,
groups-editor, devices-detail and history-import, `refresh-devices-list-800`, and `admin-access-groups-long-names-wide`; their layout rules are shared with the opened ones and the
fixture's width and height checks passed. The Group by control sitting inside the page at all four widths is asserted, not only looked at.
Popup layers cannot be captured: dialogs and the subtitle editor were checked through automation.

## Deliberate differences and limits

- The phone save bar and group toast, the sticky list headers, the token dialog's separate Remove button and the viewer-style list heights are
  not reproduced (see the tables).
- The captures are of the page element with the app background applied; popup layers (dialogs, the subtitle editor) cannot be captured and
  were checked through automation: dialog text, button fill, hover state, toggle state, group peer, widths.
- Not verified: a live server, real phones, all-DPI, Narrator and IME behaviour, right-to-left rendering of the actual interface
  (the helper and the properties are tested; no Arabic or Hebrew interface was run), a Windows contrast theme (the style branch is not exercised),
  and the facet checkbox glyph in the captures. A passing synthetic test does not certify any of these.
