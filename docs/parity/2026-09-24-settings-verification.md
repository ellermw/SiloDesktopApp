# Settings, identity and integrations verification — September 24, 2026 UTC

## Scope and evidence

Audit only. Compared the current desktop working tree with official Silo Server
`main` at **d4e35ba9df416e747822c6c9f2193b89c6b7e9fb**, freshly fetched by the
coordinating audit and checked out read-only at
`.codex-tmp/silo-server-audit-d4e35ba9`. `web/` and `internal/` references below
are relative to that checkout; `src/` and `libs/` are desktop files. Line numbers
refer to the audited source. Read `AGENTS.md` and
`docs/parity/2026-09-24-status.md`; older audit assertions were not treated as
current evidence.

This is a bounded reachable-source and request-contract comparison. No live
account changes, imports, connections, notifications, downloads or playback were
started. No UI controls were operated and no tests were run by this sub-audit.
Source-confirmed differences below are not claims of installed reproduction or
visual parity. The coordinating audit identifies local source as 1.1.103 and the
running installed build as 1.1.102-api-v2.2; active playback prevented navigation
for installed checks. The deployed server version is not established by this checkout.

## Confirmed work candidates

### S1 — P2: Add the current Account password settings flow

- **Desktop:** `src/SiloPlayer/Views/SettingsPage.xaml.cs:992` builds the Account
  overview with Profiles and Notifications; the full tab inventory at
  `src/SiloPlayer/Views/SettingsPage.xaml:282` adds Profiles, Notifications,
  Plugins and Sessions, with no password page. A source-wide search for
  `ChangePassword`, `current_password`, `new_password` and `account/password`
  found no implementation.
- **Upstream:** `web/src/pages/SettingsLayout.tsx:417` exposes Account;
  `web/src/pages/settings/AccountSettings.tsx:15` implements the form and
  capability/error states; `web/src/hooks/queries/account.ts:7` and `:21`
  define `GET /api/v2/account/password/capability` and
  `POST /api/v2/account/password`.
- **Effect:** A user who can change the household account password in WebUI
  cannot do so in desktop. The existing `SettingsPage.Account.cs` name denotes
  device/connect-app code, not this feature.
- **Acceptance:** For a permitted primary profile, show current/new/confirm
  fields, server-provided character/byte limits, pending/error/success states,
  and clear password fields after success. Match capability restrictions for
  secondary profiles and externally managed accounts. Bind submission to the
  originating profile. Verify in a disposable account rather than changing a
  real user's password during an audit.

### S2 — P2: Support three-way intro skipping throughout settings and playback

- **Desktop:** `src/SiloPlayer/ViewModels/SettingsViewModel.cs:270`, `:538`,
  `:655` and `src/SiloPlayer/Views/SettingsPage.xaml:698` expose/read/write the
  deprecated boolean. Device settings also request it at
  `src/SiloPlayer/Views/SettingsPage.Account.cs:27`.
  `src/SiloPlayer/Services/PlayerService.cs:5173` resolves only that boolean;
  `libs/mpv/scripts/silo-osc.lua:3669` automatically skips when true, while
  `:3702` offers Skip Intro for an intro range regardless of a Never preference.
  The enum key in `SettingsV2Values.cs` alone does not implement this flow.
- **Upstream:** `web/src/lib/settingsContract.ts:587` defines `never`, `ask`,
  `always`; `web/src/pages/settings/PlaybackSettings.tsx:409` gates the mode UI
  on capabilities and preserves unknown capability failures rather than
  treating them as legacy. `web/src/player/hooks/useIntroSkipPrompt.ts:261`
  suppresses Never, `:282` offers Ask, and `:289` starts the undo prompt for
  automatic skipping. `internal/settingscontract/mirror.go:84` documents that
  Never and Ask both map to false for old clients.
- **Effect:** Never selected in WebUI still yields a desktop Skip Intro prompt;
  desktop cannot select Never, and automatic skipping has no corresponding
  WebUI undo flow. Changing the desktop boolean can replace a deliberate Never
  preference through the server's compatibility mirror.
- **Acceptance:** On a server advertising the mode key, support all three values
  at profile/device scope and consume the effective value in native playback.
  Verify no prompt for Never, one appropriate offer for Ask, automatic skip and
  undo for Always, paused-state preservation, marker re-entry and host/guest
  control restrictions. Capability failure must not allow a legacy write that
  accidentally changes the enum. Older supported servers may retain the switch.
  This is separate from the seek-interval gap owned by the playback report.

### S3 — P2: Report a failed device-override clear when saving/resetting defaults

- **Desktop:** `src/SiloPlayer/ViewModels/SettingsViewModel.cs:866` saves the
  profile value, catches **every** exception clearing `profile_device`, and
  still reports “Setting saved”; the reset helper at `:881` does the same.
  Quality/bitrate, audio language and automatic marker settings use this path.
- **Upstream:** `web/src/hooks/queries/profileDefaults.ts:44` saves the profile
  then clears the shadowing device row. Only a missing-value error is ignored
  (`:55`); reset applies the same distinction at `:65`.
- **Effect:** When the profile write succeeds but the device DELETE fails,
  desktop reports success while the old device override still controls effective
  playback. This is a confirmed failure-path difference, not evidence that the
  server currently returns such an error.
- **Acceptance:** Inject successful profile PUT followed by device-clear 500 or
  network failure with a known overriding value. Display a truthful partial-save
  error and reconcile the displayed effective value; retry must be possible.
  A genuinely missing override is harmless. Apply the same behavior to reset.

### S4 — P2: Clear consumed Emby Connect state after starting history import

- **Desktop:** `src/SiloPlayer/ViewModels/SettingsViewModel.cs:1868` enables
  Start from the stored Connect session/server; `:1926` sends them; successful
  creation at `:1956` updates the run but leaves session, server and password
  intact, then clears `IsImporting`.
- **Upstream:** `web/src/pages/settings/HistoryImportSettings.tsx:251` creates
  the run and `:257` clears the consumed session/server/password.
  `internal/historyimport/repo.go:300` rejects consumed sessions.
- **Effect:** After one successful Connect import, the desktop still presents
  an apparently usable authorization and allows another start with a session
  that the server has consumed. That attempt fails instead of prompting fresh
  sign-in.
- **Acceptance:** A successful Connect run clears only its consumed authorization
  state and disables another start until fresh login. A failed run-creation
  request must preserve usable state where the server did not consume it.
  Verify other source modes are unaffected.

### S5 — P3: Do not double-count skipped items in import progress

- **Desktop:** `src/SiloPlayer/Views/SettingsPage.xaml.cs:3926` computes
  `(Matched + Unmatched + Skipped) / Fetched`; `:3939` repeats that numerator in
  the processed label.
- **Upstream:** `web/src/pages/settings/HistoryImportSettings.tsx:829` explains
  that skipped items were already matched and uses `matched + unmatched`.
- **Effect:** Active imports with skipped items appear farther along than they
  are; the progress bar can reach 100% early and the text can exceed the fetched
  total. Example: fetched 100, matched 60, unmatched 10, skipped 40 displays
  110/100 instead of 70/100. The separate Skipped metric should remain.
- **Acceptance:** Render that fixture as 70/100 and 70%, retaining the Skipped 40
  metric; cover queued/running/completed and zero fetched states.

### S6 — P2: Surface native download save/delete failures to the user

- **Desktop:** Downloads is reachable from
  `src/SiloPlayer/MainWindow.xaml:248` and `MainWindow.xaml.cs:3184`.
  `src/SiloPlayer/Views/DownloadsPage.xaml.cs:269` catches all save failures and
  writes only to `Debug.WriteLine`. Delete failures are silently swallowed at
  `src/SiloPlayer/ViewModels/DownloadsViewModel.cs:76`.
- **Upstream comparison:** `web/src/components/DownloadVersionPicker.tsx:44`
  routes the direct-download attempt through `launchDirectDownload` and `:55`
  displays a failure toast. The native registry/save page has no identical
  current WebUI page; this finding is native flow correctness, not a claim that
  WebUI implements a full native transfer manager.
- **Effect:** A denied/missing download or failed disk/network transfer gives no
  visible error after choosing a destination; delete may appear to do nothing.
  A partial local file can be left after a failed copy.
- **Acceptance:** Inject non-success HTTP, mid-stream failure, disk-write failure
  and DELETE failure. Show an actionable error, preserve registry state on
  failed deletion, and clearly handle incomplete local output. Do not treat
  cancellation as a failure or imply transfer completion merely from launch.

### S7 — P3: Preserve the actual download filename/container

- **Desktop:** `src/SiloPlayer/Views/DownloadsPage.xaml.cs:248` chooses `.mp4`
  only for transcode and `.mkv` for every other delivery, before making the
  request. `:250` uses content/episode ID plus a download-ID prefix as the name;
  `:261` obtains the response but never consumes its filename metadata.
- **Upstream:** `internal/downloads/service.go:1282` sends Content-Disposition
  and the actual path's MIME type for local files; `:1350` supplies attachment
  naming for remote artifacts. Browser launching in
  `web/src/api/v2/directDownloads.ts:44` lets delivery determine the filename.
- **Effect:** An original MP4 or other non-MKV source is saved with a misleading
  `.mkv` extension and an opaque identifier name. This is a confirmed native
  output mismatch; some players can still sniff/play the bytes.
- **Acceptance:** Save original MP4/MKV and prepared/remux/transcoded samples.
  Suggest a sanitized server-provided filename and correct extension (or a
  verified media-metadata fallback), while allowing a user-selected filename.
  Avoid relying on delivery format alone as the container.

## Coverage and matching implementation found

“Present” below means reachable source exists and the inspected contract matches;
it does not establish every state or visual detail.

| Owned area | Current source coverage / matching behavior | Remaining evidence |
|---|---|---|
| Authentication and profile selection | Desktop `LoginViewModel.cs:126`, `:157`, `:183` loads branding/providers and starts phone login; `ProfileSelectViewModel.cs:90`, `:113` handles PIN selection. Corresponds to WebUI `pages/Login.tsx:89`, `:143`, `pages/Profiles.tsx:37`. AuthService has generation-guarded sessions, scoped profile credentials and scheduled refresh (`AuthService.cs:74`, `:136`, `:569`). | Real login/OAuth/device success, denial, cancellation, expiry, restoration and PIN-expiry recovery; no new blanket “authentication absent” claim. |
| Shell/navigation | Desktop `MainWindow.xaml.cs:1784`, `:3184` includes Downloads, notification routing and return/navigation handling; settings overview is implemented at `SettingsPage.xaml.cs:893`, not a stub. | Same profile/permissions, Back/focus/controller, profile switches, return from playback, narrow and high-DPI geometry. Source coverage is not side-by-side visual proof. |
| Account, profiles and sessions | Profiles management gates on acting-admin/primary (`SettingsPage.xaml.cs:961`), blocks active/primary/last profile deletion (`:6204`); session controls exist in `SettingsViewModel.cs:1012`. WebUI gating is `SettingsLayout.tsx:533`. | S1; verify current/other session revocation and permissions with disposable profiles. |
| Playback defaults and devices | Effective contract reads, typed profile writes and device overrides exist (`SettingsViewModel.cs:525`; `SettingsPage.Account.cs:46`, `:239`). WebUI counterparts are `settings/PlaybackSettings.tsx:73` and `settings/DeviceSettings.tsx`. | S2/S3; profile-wide seek intervals are reported by the playback audit. Verify saved versus inherited values after reload/device switch. |
| Subtitle appearance | Load/save/reset paths already exist (`SettingsViewModel.cs:1368`, `:1412`, `:1447`), matching the font/outline/background/position controls in `settings/SubtitleAppearanceSettings.tsx:355`. | Visual preview, native subtitle render, discarded edits, inheritance and failure states. |
| Appearance, date/time, accessibility, theme editor | Desktop loads profile theme/date/time and effective accessibility (`SettingsViewModel.cs:450`, `:473`), implements theme token editor/import/export/community themes (`SettingsPage.xaml.cs:569`, `:645`, `:661`, `:781`). Corresponding WebUI settings pages are present. | Same theme and text-scale screenshots, reset/inheritance across profiles; CSS is a WebUI artifact and must not imply arbitrary CSS styles native WinUI. |
| Navigation/cards and overlays | Scoped menu/card read/save/reset and device-override clearing (`SettingsPage.xaml.cs:1292`, `:1408`, `:1427`, `:1451`, `:1652`) and overlay editing (`:1739`, `:2136`) exist, matching WebUI Interface/CardOverlay settings surfaces. | Reorder, pin/unpin, preset and device-family inheritance; verify consumers and narrow/large library rendering. |
| Home, personalization and libraries | Section load/save/reset (`SettingsViewModel.cs:1478`, `:1517`, `:1597`), library visibility/order (`:590`, `:708`) and personalization summary (`SettingsPage.xaml.cs:1696`) exist; WebUI `HomeScreenSettings`, `PersonalizeSettings`, `LibrarySettings` provide counterparts. | Live profile-specific save/reset, section order and recommendation refresh. Detailed browse consumer behavior belongs to the browse audit. |
| Notification inbox | Desktop cursor pagination/filtering and cutoff-based mark-all are implemented (`NotificationsViewModel.cs:62`, `:225`; `NotificationsApi.cs:64`). WebUI also uses an observed cutoff (`pages/Notifications.tsx:325`) rather than indiscriminate mark-all. | Incoming events, read rollback/error visibility, unread counts and profile switch isolation. |
| Notification delivery settings | Capability-gated email/Discord/webhook/browser-subscription controls exist (`NotificationSettingsViewModel.cs:78`; `NotificationsApi.cs:80`). Email verification intents and webhook revisions are tracked. `NotificationSettingsControl.xaml:48` explicitly explains browser subscriptions are created in WebUI. | Actual email/Discord/webhook delivery and verification, expiry, stale revisions; native closed-app delivery is not demonstrated by browser subscription management. |
| Downloads | v2 capability/registry pagination, batch cursor progress protection, native authenticated file save (`DownloadsApi.cs:15`, `:38`, `:41`; `DownloadsPage.xaml.cs:232`). WebUI currently exposes direct file selection (`DownloadVersionPicker.tsx:44`). | S6/S7; permission/quality rejection, preparing/ready lifecycle, transfer progress, retry and profile changes require runtime checks. |
| Connect Apps and plugin preferences | Device/compat connection details (`SettingsPage.Account.cs:423`) and schema-based plugin values (`SettingsViewModel.cs:2089`, `:2127`) are present; WebUI counterparts are `ConnectAppsSettings`/`PluginSettings`. | Real provider capabilities, account/profile restrictions, secret reveal/copy behavior and plugin schema/error variants. |
| Watch providers | Dynamic capability-driven connections with API key/device-code auth, manual sync, settings and run history (`SettingsViewModel.cs:1103`, `:1163`, `:1190`, `:1247`, `:1269`); revision-protected writes in `WatchProvidersApi.cs:77` correspond to WebUI WatchProviders settings. | Actual authorization/poll expiry/revocation, per-provider available toggles, sync conflicts and live status. No broad missing-provider claim. |
| Webhook sync | Create/update/rotate/delete, default profile, actor mappings, provider setup and event detail/filter/paging exist (`SettingsPage.xaml.cs:5478`, `:5521`, `:5554`, `:5717`, `:5955`; `WebhookSyncApi.cs:8`). WebUI counterpart `WebhookSyncSettings.tsx:842` also offers rotation. | Real delivery outcomes/mappings, empty/error states, server-relative receiver links, profile changes; no webhook was sent. |
| History import | Emby Connect/saved, Plex authorization/saved and direct Jellyfin request construction (`SettingsViewModel.cs:1911`) uses v2 (`HistoryImportApi.cs:7`). Run merging, warnings, unmatched samples and all additive counters are implemented (`SettingsViewModel.cs:2054`; `SettingsPage.xaml.cs:3876`). | S4/S5; verify real progress/reconnect/selected history, favorites/watch dates. Server-side import matching/date repairs do not need a duplicate desktop implementation. |

## Verification-only work

Perform installed side-by-side checks for these areas at normal, narrow and
high-DPI sizes: shared profile/data, loading/empty/failure, focus/Back, tab search,
dialogs, scroll containment, labels/spacing/colors, and native control behavior.
For settings and integration writes, use isolated fixtures or explicitly approved
test accounts. Check results from a second client and after reload to distinguish
an optimistic local display from persisted effective state.

Source findings S1–S7 are candidates for user selection, not permission to fix them.
No completeness percentage or visually complete classification follows from this
pass.
