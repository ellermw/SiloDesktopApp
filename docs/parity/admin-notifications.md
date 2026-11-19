# Configure > Integrations > Notifications — verified Beta candidate, November 17, 2026

The complete Notifications family at `/admin/settings/notifications` is prepared
on branch `t3/admin-notification-settings` from exact Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`. The checkout is
`C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-e5b9c3f0-9ff0d304c0303aaf`.
Version remains `1.2.1181-beta.1` / numeric `1.2.1180.1`, owned by the pipeline.
The scoped implementation, review corrections, unit suite and six native groups
are verified below. An additional unchanged Downloads fixture stops at its
retained-limit assertion; its full selector is not certified by this pass.

## Official reference and scope

The coordinator supplied freshly fetched official GitHub main
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` in
`D:/SiloPlayer-work/server-upstream-nov14`. All protocol/UI reads used `git show`
at that pin, without changing or cloning that repository. Primary references:

- `web/src/pages/admin-settings/NotificationsAdminSettings.tsx`: seven delivery
  cards, per-card setup/Advanced, paused pipeline, digest/grouping/flood-control,
  processing and retention settings; separate Discord credential save/test and
  relay URL lifecycle.
- `web/src/pages/admin-settings/ServerNotificationChannels.tsx`: destination
  cards, eight event switches, create/edit, enable, test, delete confirmation,
  generic signing-secret rotation and one-time secret dialog.
- `web/src/api/v2/notificationRelay.ts`, `notificationDiscord.ts`,
  `notificationDestinations.ts`, and
  `web/src/hooks/queries/admin/serverNotificationChannels.ts`: captured authority,
  authentication refresh for safe destination GETs, one-shot mutation routes and
  explicit request bodies.
- `internal/apiv2/notification_relay.go`, `admin_notification_discord.go`,
  `notification_destinations.go`, `notification_destination_create.go`,
  `notification_destination_tests.go`, `notification_server_channel_update.go`:
  exact current v2 wire shapes. `internal/notifications/settings.go` supplies
  the defaults; `relay_credentials.go` leaves origin allowlisting to the server.
- `web/src/components/notifications/MobilePushPrivacyDisclosure.tsx`,
  `web/src/components/SigningSecretDialog.tsx`, `web/src/locales/en.json`:
  complete privacy/setup/signing-secret strings. The 193 relevant English AST
  messages are represented in the Admin domain table; existing interface
  catalogs deliberately exclude Admin messages. Rich emphasis becomes native
  plain text and hyperlinks are native launch actions.
- WebUI `web/package.json` names Lucide `^0.576.0`. The six newly needed
  geometries were read from `lucide-icons/lucide` tag `0.576.0` `icons/*.svg`;
  rectangles/circles are expressed as native paths, like the existing helper.

Viewer inbox #187 remains separate. Personal webhook CRUD and mobile device
test dispatch are not controls on this pinned Admin page. This page provides
the official APNs/FCM switches/privacy/relay setup, personal-webhook policy
settings, Discord bot verification, server-channel delivery tests, and the
released Email settings link (which owns SMTP setup and its saved-settings
test). It does not invent a new push-device selector or duplicate Email.

## Implemented behavior and safeguards

Production `AdminConfigureServices.Add` registers the actual Page/controller/API,
adds its keys/secrets to the existing shared settings store, and makes the
existing navigation/search leaf available. The XBF allowlist and approved-source
manifest include the page. Existing no-slide navigation and gutter interfaces
are reused. The page has a 1120-pixel settings column, responsive native rows,
channel status pills, setup disclosures, pause warning, loading/failure/reload
states, dirty/restart indicators, sticky page Save/Discard, scroll restoration
and the normal dirty-departure guard.

The guarded form owns 25 notification settings plus the three Discord application
keys and read-only mail/relay readiness. Page Save writes only dirty page keys;
Discord Save/Clear writes only its three keys using the same current strong ETag.
The Discord test uses saved credentials and is disabled for incomplete, unknown,
managed or unsaved setup. Page Discard leaves the independent setup drafts alone;
navigation Discard clears all drafts. Canonical acknowledgements preserve newer
nonsecret typing. Credentials freeze during writes; replacements clear after
acknowledgement. Clearing the Discord application requires confirmation.

Stored secrets are never read, rendered or logged. Only presence is retained.
Missing sensitive status disables protected setup and offers explicit Reload.
Environment-managed fields cannot be changed through these commands. Relay URL
typing is separate from the settings form: registration captures the submitted
URL and preserves newer typing while the request is pending. Register/rotate,
re-register, expiry/prefix/status, privacy, and confirmed credential clearing use
the official credential endpoints. The server remains the origin allowlist
authority. No reusable relay API key is returned to this page.

Server channels preserve host-only destinations, failure/disabled diagnostics,
last-success status and per-channel test results. Create/edit dialogs expose all
eight official events; a blank replacement URL is omitted. Toggle PUTs contain
only `enabled`. Delete is confirmed; only generic destinations offer secret
rotation. Newly minted signing secrets have their dedicated one-time, read-only
reveal/copy dialog. That transient value is consumed once and cleared on close or
authority retirement; it never enters the retained channel list or settings.

Settings PUTs carry strong `If-Match`. The official server-channel and relay
operations have no ETag contract, so none is fabricated. All notification
commands use the authority-bound transport with authentication replay disabled,
one pending-operation gate, and one HTTP attempt. Unknown outcomes block later
commands until explicit successful reconciliation; no write is automatically
replayed. Conflicts retain drafts and require Reload/review before Save. A failed
channel read cannot authorize commands against retained rows. A known rejected
enable change returns to the observed row; an uncertain change stays blocked
until readback. Notification transport exceptions use the existing fixed
settings diagnostic boundary, so a handler cannot echo credentials into logs.

Settings reads retain existing shared single-flight 15-second/8-MiB bounds.
Notification calls have a 20-second deadline and 512-KiB response bound. Channel
reads request 100 rows, load additional pages only on explicit Load more, reject
invalid/repeated cursors or duplicate identities, and retain at most 1,000 rows.
Loaded-count/cap text explains partial lists. There is no polling, media-catalog
walk or background work after retirement. Every read, dialog and action belongs
to page/server/account/profile authority; retirement clears private UI, drafts,
results and one-time secrets and discards late completions.

Native differences: WinUI Expanders and native dialogs replace WebUI disclosure
and dialog primitives; safe failures/results use inline live text. Destination
pagination is explicit rather than the WebUI's eager up-to-100-page walk. The
1,000-row native cap is deliberately visible. Native-only messages explain
bounded reads, uncertain outcomes, permission/status recovery and dialog failures.
Wide/compact page and actual-dialog captures were inspected. The native
primitive differences and inspected synthetic states do not certify complete
WebUI visual parity.

## Review corrections and observed RED

The independent review found four P2 issues, all addressed in the final source:

- Destination GETs use a dedicated GET-only, refresh-enabled transport with the
  captured authority, 20-second deadline and 512-KiB bound. Mutations retain
  one-shot, no-refresh behavior.
- Discord tests and relay actions clear their own rendered success receipt at
  admission, including failure and unknown-outcome paths.
- Modal ownership waits for actual Unloaded before a successor signing-secret
  dialog can consume its one-time secret; elapsed time never authorizes it.
- Page/root/window visibility retirement cancels the owner, clears private UI
  and dialogs, and reactivates under fresh authority when visible again.
  Nonsecret unsaved typing is held by the page across that hide/restore and
  restaged only when the authority epoch and account are unchanged; secrets,
  dialogs, results and in-flight commands are still dropped. A settings save or
  server-changing command retired that way may still have reached the server,
  so under the same epoch and account the fresh owner starts with it as an
  unknown outcome: commands stay blocked until an explicit successful Reload,
  and the notice says that a signing secret minted by a create or rotation was
  not shown and must be rotated. Dropped credential typing is reported on
  return without blocking. A dropped delivery test changes no server
  configuration and is not carried.

The contained `review-red-r4` unit run observed four behavioral assertion
failures before the fixes. `review-native-red-host-r3` observed the mounted,
hidden-host retirement failure before the production visibility fix. Normal
actual dialog unload and successor-secret reveal are exercised by the final
native run. A deliberately delayed unload longer than one second was not
observed as a runtime RED; no such claim is made.

The first full suite also detected two candidate source-policy violations:
unvalidated external-browser launch and two date displays bypassing preferences.
The final source uses `ExternalBrowserUrlPolicy.TryGetSafeUri` and
`DateTimeDisplay.FormatDateTime`; both source guards pass in the full suite.

## Final source, payload and unit verification

Evidence root: `D:/SiloPlayer-build/admin-completion-notifications`.
The `final-r2` contained chain performs a fresh nonincremental unit build,
focused/full tests, nonincremental app build, publish and native-host build.

| Check | Observed result |
| --- | --- |
| Focused Notifications/shared-settings/source-policy units | 76 passed, 0 failed, 0 skipped |
| Complete main unit suite | 8,061 passed, 0 failed, 0 skipped |
| App build, publish and native host | Passed |
| Source-root and physical Core portable-PDB source proof | Passed |
| Published payload | 582 files, SHA-256 manifest `78bed03fee3c65e2759843bf81a5721cd442a4831eb680afa9385ad0f4acbf28` |
| Initial compiled-input seal | 2,321 files, manifest `521371f60fde7869ff45cd368b25d3a6bbd8b2e04177fa8567019bc5e5c7cdd8` |
| Final host/native source seal | 2,321 files, manifest `cf841dc7960e445f377883956027488949eb4c7868cf1b0bf0e4cc16287b8e3b` |

Final unit TRX files are `unit-results/final-r2-focused.trx` and
`unit-results/final-r2-full.trx`; command outcomes and actual lease timestamps
are retained in `receipt-final-r2.json`. Its ACQ/REL were
`2026-11-17T13:31:42.7610132Z` / `2026-11-17T13:37:36.6953647Z`, PID 22668.

After that seal, the sole compiled-source delta is the Notifications native
fixture: actual-popup captures, additional wide/compact captures and animation
settlement. Final fixture SHA-256 is
`d3b9bc98ca71bebbff3a80e34bee4c8232ed9a8d8869123f73f75df25334a80a`.
All product/Core/main-unit inputs remain identical to the passing unit/app build.
`SiloPlayer.dll`, PDB and PRI match between publish, host and native reference;
the entire original payload was rehashed unchanged before and after the final
native attempt. Documentation-only updates do not change these compiled inputs.

## Native acceptance and capture inspection

`final-r4` builds the host nonincrementally against the unchanged `final-r2`
product. Each selector executes in its own contained process with a deadline.
The following groups complete with the native run-completed marker and no FAIL:

- `admin-configure-notifications`
- `admin-manage-foundation`
- `admin-configure-general`
- `admin-monitor`
- `admin-configure-email`
- `admin-configure-security`

These are six selector groups, not six unit cases. Notifications covers real
production registration/shell/Frame, seven delivery cards, write-only credentials,
saved-settings test admission/failure, all eight event controls, cancelled
deletion, creation and actual-unload-before-secret handoff, conflict/reload,
channel-read retry, mounted hidden-host retirement, fresh visibility recovery,
and discarded late results. No global input or live account is used.

Seven actual PNGs in `results/final-r4-admin-configure-notifications` were opened
and inspected: `notifications-channels.png`, `notifications-relay-wide.png`,
`notifications-server-channels-wide.png`, `notifications-channel-edit.png`,
`notifications-narrow.png`, `notifications-server-channels-narrow.png` and
`notifications-channel-edit-narrow.png`. They show readable wide/compact rows,
expanded relay privacy/setup and server actions, and the actual scrolling editor
with persistent Save/Cancel. The inspected client sizes are 1440x900 and 720x900.

The additional `admin-manage-download-settings` group reached 49 PASS lines,
then timed out at unchanged fixture line 316 waiting for blank bandwidth after
unchecking Unlimited. Earlier in that same fixture, bandwidth 250 was entered;
the unchanged production `RenderLimit`/`ToggleUnlimited` path retains/restores
that last finite limit. Both the fixture and those production files match Beta.
This source trace explains an inconsistent fixture expectation, not an observed
Notifications failure; the complete Downloads selector remains unverified here.
No unrelated Downloads code or assertion was changed to manufacture a pass.
The final-r4 receipt therefore honestly records a fail-stop, not seven passing
groups. Source and original-payload seals were verified separately after it.

Actual final-r4 ACQ/REL: `2026-11-17T13:48:03.0001932Z` /
`2026-11-17T13:50:20.9931849Z`, PID 22020. Inclusive preflight output was
29.0476 GiB, below the 50-GiB global cap; both drives exceeded the 50-GiB floor.
Every attempt used one nonblocking `SiloOct18Build.WaitOne(0)`, owned containment,
deadlines and finally-release. No holder, waiter or task-owned runtime remains.
The installed player PID 39668 was not stopped, restarted or configured.

## Reproduction and retained checkpoints

Use a coordinator-assigned runtime lane and current-source verified payload,
with the same storage/mutex/containment controls, to reproduce:

`dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter "FullyQualifiedName~AdminNotification|FullyQualifiedName~AdminServerSettings|FullyQualifiedName~AdminSettingsForm|FullyQualifiedName~AdminConfigureNavigation"`

`dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release`

Publish the exact source through the supported self-contained win-x64 workflow,
build the host against its DLL/PDB/PRI and run the six selectors listed above.
Retain the source-root proof when executing from an external artifacts directory.

The patches under `.codex-tmp/admin-notifications-20261117` are historical,
pre-review source checkpoints. They do not describe the corrected final source.
Do not reverse/apply them against this candidate as acceptance instructions.

## Limits and delivery

Live relay/APNs/FCM, SMTP, Discord and webhook interoperability, actual external
browser launch, Narrator/IME and all-DPI rendering are not observed. Synthetic
checks and inspected states do not establish complete visual parity. The
unrelated Downloads fixture limitation is explicitly retained above.
This family is submitted independently to the existing Beta pipeline; no direct
merge, stable publication, installer execution or pipeline-owned PR modification
is part of this handoff.
