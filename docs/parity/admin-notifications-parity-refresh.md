# Configure > Integrations > Notifications — refresh to official 4a1ee058e, November 21, 2026

Branch `t3/admin-notifications-parity-refresh`, from Beta `2a4c135c845315c26e78bee7a3e517107959d056` (`1.2.1324-beta.1`). The page
(`/admin/settings/notifications`, `AdminRoute.NotificationSettings`) was verified against official `217ff25abc` in
`docs/parity/admin-notifications.md`; this record refreshes it against the newest official main
`4a1ee058eb3cfc74941f93a4aa57db6b617ca5f0` (read with `git show <sha>:<path>` and `git log <old>..<new> -- <path>` in
`D:/SiloPlayer-work/server-upstream-nov14`; that clone was not fetched, checked out or modified). Version numbers are unchanged.

## Upstream changes that reach this page (`217ff25abc..4a1ee058e`)

`git log` over the page, `ServerNotificationChannels.tsx`, `web/src/api/v2/notification*.ts`, the channel hooks, the notification
components, `SigningSecretDialog.tsx`, and over the shared settings components the page imports (`SecretField`, `SettingField`,
`AdvancedSection`, `useSettingsForm`, `alert-dialog`, `button`, `badge`, `ConfirmDialog`), plus the OpenAPI operations and `en.json`:

| Commit | What it did | Effect here |
| --- | --- | --- |
| `f42b93271` #4596 | Removed the Processing section, its paused banner and seven settings; migration `20261119184931` deletes the rows; selective restore skips them | Removed from page, catalog, text and tests (below) |
| `11693425e` #4665 | Backups, Discord, MDBList… settable from the environment; the Discord card keeps env-set keys out of its saves and clear and ignores their drafts | **Handled by #234** (presentation, locked rows, drop of a typed secret). See "Reported, not changed" |
| `160a4425e` #4704 | Relay-clear and Discord-clear confirms use the destructive variant (the channel delete already did) | Done: page-local destructive style |
| `5537df46a` #4766 | Relay URL field and the server-channel URL field read left to right in RTL | Done |
| `bd40bd9eb` #3550 | Plugin-provided notification destinations | **Not on this page.** They are the viewer's own destinations (`web/src/pages/settings/PluginDestinationsSection.tsx`, `/api/v2/notifications/plugin-*`); the admin page, its imports and the `/api/v2/admin/notifications/*` operations are unchanged. Out of scope (viewer Settings) |
| `3e96eb85d`, `40fb29325` | Dialog titles and descriptions wrap anywhere | Native confirmation and editor titles wrap (`TextBlock`, `TextWrapping.Wrap`), bounded and neutralized |
| `f6ed24869` | `--destructive-fill` for red buttons and badges | The `#dc2626` fill used by the confirmations |
| `aa06983a6`, `6b57a44be`, `e8de1117e`, `9820f17e1`, `6ddee8d39`, `4cf51bb51`, `f465d2d9a`, `dbceadcaf`, `a1ef6efc5` | Hidden-draft saves on other pages, `SecretField` dirty marker (the page does not pass it), env-managed drafts (#234), status-line wrapping and icon alignment, text-field font minimum, bars above the web playback bar, test pruning | Nothing to do, or browser-only (see "Cannot be done in WinUI") |

OpenAPI: all nineteen `/api/v2/admin/settings*` and `/api/v2/admin/notifications/*` operations are byte-identical between the two commits,
and so is every schema they reference, except `AdminSensitiveStatus` (gained required `managed_by_env_vars`, which is #234's contract) and
`AdminSettingsCheckResult` (gained `message_key`/`message_args`, used by other pages' connection checks, not by this one). `en.json`:
no message under `web.admin.settings.notifications.*`, `web.notifications.pushPrivacy.*` or `web.common.signingSecret.*` changed or was
added; 23 were removed (below). The two APNs/FCM test operations exist in the contract but the official page does not call them.

## The removed Processing settings

Seven keys: `notifications.release_events_enabled`, `notifications.fanout_enabled`, `notifications.fanout.settle_seconds`,
`notifications.fanout.max_event_age_hours`, `notifications.retention.read_days`, `notifications.retention.unread_days`,
`notifications.retention.event_days`. Established from `f42b93271` and the server source at `4a1ee058e`:

- **Reads.** They are gone from `adminSettingDefaults`, so `GET /admin/settings/effective` no longer lists them; the migration deletes stored rows and a backup restore skips them (`retiredSettings`). A client that finds one in an older server's answer simply never asks for it.
- **Writes.** There is no server-side refusal: `updateAdminSettings` has no key allow-list, and `NormalizeAdminSetting` returns an unrecognized key unvalidated, so a `PUT` of one would be stored as an inert row that nothing reads. (Read from source; not run against a server.) The server settings contract's `unknown_setting` answer applies to the viewer settings API, not to this one.
- **Native.** Removed from `AdminNotificationSettingsController.PageKeys` and `Display` defaults, so they are no longer form keys: `AdminServerSettingsApi.Project` drops them from any answer, `AdminSettingsForm` never tracks them, `Set` throws `ArgumentException` for them and `PutAsync` refuses them (`allowed` list) before a request is built. A stale draft or default therefore cannot reach a request body; unit and native tests assert this.
- **Older server** (one that still has them): the new page neither shows nor changes them and never sends them; their stored values and defaults keep applying on that server and cannot be edited from this page, and the paused-pipeline banner is gone. The official client behaves the same, so this is accepted.
- **References.** A search of the whole source found them only in the controller (`PageKeys`, `Display`), the page (`BuildProcessing`, `IsProcessing`, `_processing`, `_paused`, the advanced-key switch), `AdminNotificationText` (21 strings plus `unit.days`, `unit.hours`, and the unused `processing.maxPerShow*`) and one unit-test line. No other page, the settings search, the shared store key lists (`AdminConfigureServices` concatenates `Keys`, which shrank) or any catalog referenced them. All were removed; `unit.seconds` stays (used by the batch window).

## Difference table, official `4a1ee058e` versus native (this branch)

Outcome: **fixed** (changed in this pass), **right** (already matched), **adapted** (deliberate native difference), **cannot** (WinUI).

| Area | Official | Native | Outcome |
| --- | --- | --- | --- |
| Section order | Delivery channels group with seven cards: In-App, Web Push, Silo Push Relay, Email, Discord, Personal Webhooks, Server Channels; nothing after | Same; Processing group and paused banner removed | **fixed** |
| Channel switches | `Enable {channel}` label; card body opens on enabling | Same | right |
| Status pills | Rounded-full border pills; neutral, positive (emerald), warning (amber); email has two pills, server has "destinations" and "failing" | Same tones and the same separate pills; a pill with no text is collapsed (no space, no gap, not in the automation tree) | **fixed** |
| Pill wrapping | Title and pills wrap together | Side by side; very narrow headers clip instead of wrapping | adapted |
| Relay card | Privacy disclosure, Advanced (APNs, FCM, Relay URL, Deployment ID, register/rotate/clear) | Same; Relay URL reads left to right | **fixed** (LTR) |
| Relay clear confirmation | Destructive action (`hover:bg-destructive-fill/90`, no separate pressed state), `Clearing...` while pending | `Clear credential` in `#dc2626`; hover is the same fill at 90 % opacity (`#E6DC2626`); pressed `#991B1B` is **native-only** (a WinUI affordance the official button does not have); in a Windows contrast theme the dialog's own button (system colors) is used instead of the fixed red; busy state disables and blocks a repeat | **fixed** |
| Relay register success/failure | Toast `Push relay registered` / inline error | Toast `Push relay registered`; failures stay inline in the page alert | **fixed** (success toast) |
| Email card | Status line with link to Email page, per-episode switch, Advanced digest hour | Same, link is a native action | right |
| Discord card | Setup guide, Client ID, invite, secret and token, Save, Test, Clear, delivery rows | Same | right |
| Discord Save/Clear toasts | `Discord credentials saved` / `cleared` | Both toasts shown only after the server acknowledged that write for the owner that sent it (review below) | **fixed** |
| Discord Clear | Clears the editable (not environment-managed) keys; button shown while an editable key is stored; one fixed confirm text | Same: the managed keys are never in the body; button shown and enabled only while a clearable key is stored; the official title and description, unchanged | **fixed** (review below) |
| Discord clear confirmation | Destructive | `#dc2626` as above; after a conflict, an unknown outcome or blocked access the button stays disabled | **fixed** |
| Discord test result | Green when ok, red when failed | Same colours | **fixed** |
| Discord env-managed rows | Read-only with variable name; env keys left out of save/clear | **Handled by #234** | not here |
| Personal Webhooks | Advanced limits, private-destination warning (amber) | Same; warning amber | **fixed** (colour) |
| Server Channels list | Card per channel: name, type badge, host, Enabled/Disabled switch, events, last post / failure, Test, Edit, Rotate secret (generic only), Delete (the official buttons carry no per-channel accessible name) | Same information; host and server failure text neutralized and bounded; each button keeps its visible label and is named "{label} {channel}" for screen readers; type and host on a second line; failure line amber with an alert icon; card radius 16 px (rounded-xl) and border at 60 % opacity (was 12 px, full colour) | **fixed** (radius, colour, icon); type badge and button icons **adapted** |
| Channel empty / failed read | Empty text; a failed read shows the same empty text (no retry) | Empty text only after a read that found none; a failed read says so and offers Retry | adapted (native is stricter) |
| Add / edit editor | Name (max 64), URL (create) / replace URL (edit, optional), eight event switches in two groups, Create / Save | Same; label and switch share one row at every width (they stacked at narrow widths); URL reads left to right when typed or creating; create needs a non-empty URL (official shows a toast instead) | **fixed** (rows, LTR); URL check adapted |
| Channel create / test / toggle / delete / rotate | Single sends | Single sends, never replayed | right |
| Delete confirmation | Destructive `ConfirmDialog`, title `Delete "{name}"?` | `#dc2626`; title wraps; name neutralized and bounded (below) | **fixed** |
| Signing secret dialog | Shown once | Shown once in its own dialog, cleared on close | right |
| Loading | Skeleton | Skeleton | right |
| Failed settings read | Alert: "Couldn't load notification settings", description, Reload | Same two messages in the alert, radius 20 px, Reload below it (official puts the button inside the alert and uses a muted description) | **fixed** (wording); button position adapted |
| Save bar | Floating bar with count, Discard, Save | Same shared bar | right |
| Test result wording | `HTTP {status}` runs into the duration ("HTTP 200123ms", noted in the official source) | Writes `HTTP {status}, {duration}` in the two server-channel test-result messages: the text is **not** the official string, deliberately, for readability (the class comment now says so; it used to say "verbatim") | adapted |
| Rich text | Bold/links/code in privacy and setup guide | Plain text; links are native launch actions | adapted |
| Wide layout | Two-column group (label left, controls right) at 1120 px | Same shared group, 1440 and 1024 wide | right |
| Playback-bar offset, browser font-size rules, `wrap-anywhere`, status icon `has-[>svg]` rules | Browser CSS | Not applicable | cannot |
| Plugin destinations | Viewer's Settings page | Not on the admin page | out of scope |

### Strings

All 170 `web.admin.settings.notifications.*`, `web.notifications.pushPrivacy.*` and `web.common.signingSecret.*` messages of the official
catalog were compared with the table in `AdminNotificationText` by rendering the English AST back to ICU. Identical except: the rich-text
tags (`<b>`, `<link>`, `<code>`) are plain text by design (eight messages: `discord.guideBot/Grant/Intro/Invite/OAuth`,
`email.mailMissingStatus/mailReadyStatus`, `pushPrivacy.relay`), and the two server-channel test-result messages keep the separator
(`HTTP {status}, {duration_ms}ms`) where the official AST has none. The 23 messages removed upstream are removed here. Native-only
messages (bounded reads, uncertain outcomes, "Nothing was sent…", "Loading server channels…") are unchanged and are not in the official
catalog. Unused-by-design official messages: `serverChannels.lastFailure` (unreachable: a failing channel always has a failure time),
`serverChannels.saving` (screen-reader text), `serverChannels.createFailed`, `testFailed`, `urlRequired`, `relay.registerFailed`,
`clearFailed` (failures are inline in the page alert, not toasts).

## What changed on the page

- Processing group, paused banner, seven settings and 23 messages removed.
- Relay-clear, Discord-clear and channel-delete confirmations use a page-local destructive style (`DestructiveConfirmStyle`, `#dc2626`, white text, hover `#E6DC2626` = the official 90 % fill, pressed `#991B1B` native-only; null in a Windows contrast theme; the shared `DestructiveButtonStyle` and `DarkTheme.xaml` are untouched). The first pass of this record said hover was `#B91C1C`; that was a darker red than the official `/90`, now corrected. `ConfirmAsync` takes an action that returns whether anything was sent; a command that was not sent (page busy, or the channel changed) leaves the dialog open with a message and no way to repeat it, where before the dialog closed as if it had succeeded.
- Third-party text (channel names, hosts, server failure text and disabled reason, test-result messages) goes through `AdminNotificationText.Plain`: control, format (including bidirectional override and isolate), line and paragraph separator characters become single spaces, runs of white space collapse, and the result is cut to a bound (64 for names) without splitting a surrogate pair. U+200C and U+200D are kept where they join something (emoji sequences, Persian). The name sent when editing is the raw name.
- Test, rotate and delete act on the channel snapshot that was on screen: `TestChannelAsync(channel)`, `RotateChannelAsync(channel)` and `DeleteChannelAsync(channel)` send nothing when the listed row for that id no longer has the same name, type and host. The guard compares only those three: the server sends no revision for a channel, so a destination URL replaced by another URL on the same host cannot be detected, and the earlier comment "never acts on a different destination" overstated it. The string-id overloads remain.
- `RegisterRelayAsync`, `ClearRelayAsync`, `ClearDiscordAsync`, `DeleteChannelAsync`, `TestChannelAsync` and `RotateChannelAsync` return `Task<bool>` (false means nothing was sent). `SaveDiscordAsync` returns `Task<bool>`: true only when that write was acknowledged for the owner that sent it.
- LTR relay URL and channel URL fields; success toasts for relay registration and clear, channel creation, Discord save and clear.
- Pill tones, card radius/border, amber failure line, event rows in the editor, load-failure wording, no empty-list text after a failed read.

Request bodies: unchanged from beta, except one: **Discord Clear when some but not all Discord keys are environment-managed** now sends only the editable keys (all emptied) where beta refused to send anything (see the review). With no managed key the body is the same three keys as before. The Processing keys were never sent unless edited, and cannot be now.
Settings `PUT` keeps `If-Match`; channel writes keep one attempt and no authentication replay.

## Shared files touched

None. Page-local: `AdminNotificationSettingsPage*.cs`, `AdminNotificationSettingsController.cs`, `AdminNotificationText.cs`. No shared control, theme, catalog or manifest changed (`docs/parity/admin-monitor-surfaces.json` already lists these files; no new source file). The wrapping title row of `DashboardWrapPanel` was tried and dropped (a pill that first appeared inside an Expander header was never given a size); pills now stay in a plain row.

## Overlap with #234 (environment-managed settings)

Environment-managed Discord rows: **handled by #234**. Lines of #234 not touched here: the `LoadSettings` update block (the sensitive-status read and `_form.Reload`/`ApplyEnvironment` area), the `ManagedByEnv` state, the `Render` description/managed lines, the Notifications server fixture's sensitive-status answer and the tail of `AdminNotificationSettingsTests`. A dry merge (`git merge-tree --write-tree --name-only`) of this branch with #234's head (`origin/t3/admin-env-managed-settings` fetched 2026-11-21) conflicts only in `docs/current-development.md`; every source and test file merges. The review pass first conflicted in the native fixture (both branches added fields and the sensitive-status answer next to each other); the fixture was changed to add its managed-key hook in separate places, and the conflict is gone. The merged result was **not built or run**.

### Discord Clear with an environment-managed key (was "reported, not changed"; fixed in the review pass)

On beta `ClearDiscordAsync` refused when **any** Discord key was environment-managed and the button was disabled, so an admin with one managed key could not remove the editable stored secrets anywhere in the app. Now it follows official `11693425e` (`clearAll` clears `editableKeys`; the button is shown while `anyStoredEditable`):

- Clearable keys = Discord keys that are not environment-managed **and** stored (client id by its saved text, a secret by the server's configured list): `AdminNotificationSettingsController.ClearableDiscordKeys`.
- Sent: every editable key, emptied (official sends all editable keys, stored or not). None managed: all three, as before. One managed: the other two; the managed key never appears in the body. All managed, or no editable key stored: nothing is offered and nothing is sent.
- The button's visibility and enabled state use the same rule. The confirmation text is the official one, which does not vary by case (`clearTitle` "Clear Discord app credentials?", `clearDescription` "Account linking and Discord direct messages stop working immediately."); nothing was added.
- This uses the page's existing managed-key set (`ManagedByEnv`); no environment-managed presentation was added (that is #234's).

## Independent review and its fixes (2026-11-21)

An independent review of the first pass found no P1: no secret is shown, logged or sent unintentionally, no command acts on the wrong channel, twice
or unconfirmed, and no retired key can reach a request body. Its twelve other findings, and what was done (source commits `aec1d90d4`, `d5691e1df`, `d48704007`):

| # | Finding | Result | Guarded by |
| --- | --- | --- | --- |
| 1 | "Discord credentials saved" could show for a save whose outcome was never observed (the toast read the controller's state after the call; an account or server change while the PUT was in flight reset that state to one that looks saved) | **Fixed.** `SaveDiscordAsync` returns true only when that write was acknowledged for the owner that sent it (set inside the owner-checked update); the toast follows it. The relay-register toast now also requires the same owner. Clear and the other confirmations already checked the captured owner before their toast. The first pass's sentence that both toasts were "shown after an acknowledged write" was true only of the normal path; corrected in the table | Unit `ADiscordSaveWhoseOwnerWasRetiredWhileItWasInFlightIsNotAcknowledged`, `ADiscordSaveIsAcknowledgedOnlyWhen…`, `ADiscordClearWhoseOwnerWasRetired…`; native: the saved toast appears after an acknowledged save, and none appears for a save held on the wire while the owner is retired (a fixed 750 ms wait: there is no event when an outcome is dropped, so that check can miss a late toast but cannot fail wrongly; the positive case shows the path works) |
| 2 | With one environment-managed Discord key the editable secrets could not be cleared at all | **Fixed**; see "Discord Clear with an environment-managed key". One request body changes: the one-managed case now sends the two editable keys | Unit: none managed (three keys), one managed (two keys; managed key and a typed draft absent from the body; Clear gone afterwards), all managed, no editable key stored; native: one managed key (button offered, official title and text, body has exactly the two editable keys emptied, toast, button gone afterwards) |
| 3 | The retry button came back after a Discord Clear that ended in a conflict or unknown outcome, then said the item changed on the server | **Fixed.** After a sent command that ended in an error the button is enabled only when `!(OperationUnknown \|\| OutcomeUnknown \|\| Conflict \|\| AccessBlocked)` (access blocked added: `CanOperate` refuses it too). A command that sent nothing now says why: an earlier change is unresolved, the page is busy, the channel changed, or access is blocked (`NotSentMessage`) | Native: after a 412 and after a 503 the dialog stays open, the button is disabled, the message is the conflict text and not a "nothing was sent" claim |
| 4 | Test/Rotate refusals were silent; the guard's claim was overstated | **Fixed.** A refused Test or Rotate shows a warning toast ("Nothing was sent. This channel changed on the server…") and reloads the channels when the listed row differs from the card. The `IsShown` doc and this record now say the guard compares only name, type and host (see above) | Unit: both return false and send nothing, `IsShown`; native: with rendering held so the card stays stale, a refused Test and a refused Rotate each show the message, send nothing, and the card then shows the current channel |
| 5 | A relay clear the server accepted but whose settings read-back failed left the dialog open with a load error, the button enabled and "Nothing was sent" on a second press | **Fixed** for the confirmation flow: when the command was sent and the page is left not loaded with an error, the confirmation closes and a warning toast says the change was accepted but the settings could not be read again; the page keeps its load failure with Reload. (Relay *register* has no dialog; its failed read-back already shows the load failure and no success toast.) | Native: relay clear with the settings read failing: dialog closes, exactly one DELETE, toast, load failure and Reload offered, Reload recovers |
| 6 | `Plain` turned U+200C/U+200D into spaces | **Fixed.** Kept where they join something; dropped at the start, after a gap, repeated or trailing; they count toward the bound. Direction controls (U+200E/F, U+202A-E, U+2066-9, U+061C), other format characters, control characters and line/paragraph separators are still a space | Unit: a Persian ZWNJ word, a three-person ZWJ emoji sequence and a ZWJ inside a sequence survive; each neutralized class; joiner edge cases and the bound |
| 7 | Host and server text shown raw | **Fixed.** Card subtitle (type, host 253), failure line (server message and disabled reason, 200), editor placeholder host, and the test-result message (300; not in the review) go through `Plain` | Native: a host and a failure message carrying U+202E are neutralized in the card and in the editor hint |
| 8 | Card buttons not named for their channel | **Fixed.** The official buttons have no per-channel accessible name either (read in `ServerNotificationChannels.tsx`), so there is no official string: the name is "{label} {channel}" (for example "Delete Synthetic community"); the visible label is unchanged | Native: names and visible label for Test, Edit, Rotate secret and Delete, with a neutralized name |
| 9 | Empty pills stayed in the automation tree and a trailing one added 8 px | **Fixed at the cause.** `SetChip`/`SetChipTone` looked the pill up through `TextBlock.Parent`, which is null while the pill sits in an Expander header whose template has not been applied (a diagnostic run showed `Parent` null on every call for the three empty pills). The guard returned silently, so a pill that first appeared there was never shown or given a size: the fault the earlier collapse-and-invalidate experiments chased. Pills are now found through the page's own table; an empty pill is collapsed (no space, no trailing gap, no automation presence) and a pill that gets text is measured again | Native: empty pills are collapsed, no row reserves space, a pill that gets its text later (private destinations allowed) is drawn with a size and goes back to collapsed; the first-layout pill checks are kept |
| 10 | Destructive style | **Fixed / recorded.** Hover is now the official `/90` fill (the first pass used a darker red); pressed `#991B1B` is native-only and kept; in a Windows contrast theme there is no fixed red (the dialog's default button, system colors). Checked through a seam standing in for the contrast setting (`HighContrastProbe`): the confirm has no `DestructiveRoot` and is not `#dc2626`. **A real contrast theme was not switched on**, so system-color rendering was not observed | Native |
| 11 | Test weaknesses | **Fixed.** The "not echoed" checks search every text, text box, password box, automation name, help text, full description, item status and tooltip of the page, plus the toast host, for the typed credential, the destination URL and the signing secret. The older-server unit test no longer asserts on a key list that cannot hold the retired keys: it asserts the wire answer has them, the projection the page reads drops them, a write answer that echoes them reaches no later request, and no body of the session names one; the native server now answers like an older server (all seven keys, also in write answers) so the page is rendered against it. The 50 ms waits after `GoToState` and the 300 ms waits after a resize are waits on the visual state and on the window width. Other fixed delays remain (100 ms after expanding, two 100 ms waits for held answers, and the 750 ms above) | Unit and native |
| 12 | Class comment said "verbatim" | **Fixed in the comment**, not the strings: the two server-channel test-result messages keep `, ` between status and duration on purpose (the official text runs them together, a quirk its source calls out), and the comment says so | Source comment |

Not verified against unfixed code: the new tests were written with the fixes. Several cannot compile without them (return types), and the empty-pill native checks were observed failing on the first runs of this pass (`notiffix-native-r1`…`-r4`) before the lookup was fixed, but I did not run the old source against the finished tests.

## Tests

- Unit (`AdminNotificationSettingsRefreshTests`: eight facts and the ten-row `ThirdPartyNamesAreNeutralizedForConfirmations` theory): retired keys are not page keys and can neither be staged nor written (controller and API); an older server's answer holding all seven loads without them and a save never echoes them; a newer answer loads and each remaining key has a display value; the text table has no Processing/paused/days/hours messages; `Plain` neutralizes and bounds; delete, test and rotate of a changed channel send nothing and each confirmed delete sends once; clear and register report whether anything was sent. The one existing line using a retired key (`PageDiscardKeepsIndependentSetupDraftsAndAllDefaultsRemainDisplayOnly`) now uses `notifications.webhooks.max_per_profile`.
- Native `admin-configure-notifications`: 95 checks, was 35 `PASS` lines on beta. New: no Processing control or text; email and other status pills drawn with a size at first layout; relay URL and destination URL left to right; relay-clear, Discord-clear and delete confirmations show `#dc2626`, `#B91C1C` on hover, `#991B1B` pressed; a third-party channel name with an override and a line separator is neutralized in the delete title; a refused (409) delete stays open, an unknown (503) delete blocks a repeat until state is read again, and an accepted delete is sent once to the confirmed channel and never to another; cancelled confirmations send nothing; Discord save sends only the typed credential and the page save only the edited key; no request body ever carries a retired key; a name-only edit omits the untouched URL and sends all eight events; create sends name, URL and eight events once; empty list, failed channel read (Retry, no "empty" claim) and failed settings read (official title/description, Retry reloads) at 1440, 1024, 800 and 560 px.
- Review pass: unit `AdminNotificationSettingsRefreshTests` gained the Discord acknowledgement, Clear (four cases), changed-channel refusal, joiner and bound cases and an older-server test that can fail; the focused set is 142 cases. Native `admin-configure-notifications` is 134 `PASS` lines (was 95).
- Checks removed: none were removed. Beta's fixture had no check on the Processing section, so there was nothing to take out; the removal is covered by the new absence checks, with `f42b93271` as the reason.

## Runs (lane `D:/SiloPlayer-build/admin-completion-lane-a`)

- Baseline on unchanged beta `2a4c135c8`: `notifref-baseline-r1`, `publish-native`, passed; `admin-configure-notifications` 35 `PASS` lines (including the completion line), 0 FAIL.
- Intermediate runs: `notifref-focused-r1` failed to compile my first test file (control characters written literally); `-r2` passed 61 focused cases; `notifref-native-r1` failed to compile (missing `using`), `-r2` failed at a new pill check, `-r3` passed; `-r4`…`-r13` failed (`-r7` on a fixture compile error, the rest at the status-pill layout check) while I found that a pill that goes from collapsed to visible inside an Expander header is never measured (also visible as missing pills in the `-r3` 1440 captures); `-r14` passed. Every label was used once.
- Acceptance: `notifref-final-r1` (`-Stage final`) on source commit `2d6d65f48`: focused 127 passed, 0 failed (filter `AdminNotification|AdminServerSettings|AdminSettingsForm|AdminConfigureNavigation`); full 10,767 passed, 0 failed; native `admin-configure-notifications` 95, `admin-configure-email` 297, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113 `PASS`, 0 FAIL, each with the completion line. Lease held 2026-11-21 10:32:26Z to 10:39:21Z; the receipt is `receipt-notifref-final-r1.json`. None of the listed known-failing or intermittent groups or tests were run or failed.

### Runs of the review pass (same lane, labels `notiffix-*`)

Focused `-r1` passed (142). Native: `-r1` failed at my first empty-pill check, `-r2` and `-r3` failed at the same check with diagnostics (that is how the null `Parent` was found), `-r4` failed because a collapsed element keeps a stale `ActualWidth`, `-r5` because the conflict message itself contains "changed on the server" (my check was wrong), `-r6` passed. Acceptance `notiffix-final-r1` passed on `d5691e1df`; the dry merge with #234 then showed a fixture conflict, the fixture was changed (commit `d48704007`, no other source) and the acceptance was repeated as `notiffix-final-r2` (reason: the fixture changed after `-r1`).

- `notiffix-final-r2` (`-Stage final`) on source commit `d48704007`: focused 142 passed, 0 failed (filter `AdminNotification|AdminServerSettings|AdminSettingsForm|AdminConfigureNavigation`); full 10,782 passed, 0 failed; native `admin-configure-notifications` 134, `admin-configure-email` 297, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113 `PASS`, 0 `FAIL`, each ending with the completion line. The known-failing native groups were not run; the intermittent `admin-playback-administration`, `AdminStorageSettingsControllerTests` and `LibraryBackgroundRefreshTests` did not fail in this run. Receipt `receipt-notiffix-final-r2.json`.

## Captures opened

Review pass: `notifications-channels.png` from `notiffix-native-r6-…` was opened (pills drawn with their tones, empty pills absent); the rest of that run's captures were not opened.

From `results/notifref-final-r1-admin-configure-notifications` (same source as `-r14`): `notifications-channels-560.png`,
`notifications-load-failed-560.png`; from `notifref-native-r14-…`: `notifications-settings-1440.png`, `notifications-channels-1440.png`,
`notifications-empty-560.png`, `notifications-editor-800.png`; from `notifref-native-r3-…` (earlier source): `notifications-confirm-1440.png`
(delete confirmation, `#dc2626` fill, wrapped title), `notifications-load-failed-800.png`, `notifications-editor-560.png`,
`notifications-channels-failed-1024.png`; from the baseline: `notifications-channels.png`, `notifications-server-channels-wide.png`.
The other captures (settings, channels, editor, confirmation, empty, failed channel read, failed settings read at 1440/1024/800/560) were
produced by the run and not opened. Confirmation and editor captures render the dialog element; the popup layer itself was checked through
automation (fill, hover and pressed colours, button state), not a screenshot.

## Not established

- No live server or official WebUI was available: nothing was compared with a rendered official page; layout is read from source. No claim of visual or pixel parity.
- That a `PUT` of a retired key is stored inertly on a new server was read from source only. Older-server behaviour was exercised with synthetic answers.
- The review pass's contrast-theme behaviour was checked through a seam, not a real contrast theme; the merged result of this branch with #234 was not built or run; the toast and refusal messages were read from the toast host, not heard through Narrator.
- Hover and pressed colours were set through visual states by automation, not by a real pointer. Narrator, IME, right-to-left rendering of the LTR fields, light theme, all-DPI rendering, live relay, Discord or webhook delivery and the external browser launch were not observed.
- Not done: button icons and the type badge on channel cards, pill wrapping in very narrow headers, the Reload button inside the load-failure alert with a muted description, failure toasts (they are inline).
- 35 versus the briefed 38 baseline checks: the baseline results file has 35 `PASS` lines; this record reports what was observed.

## Files created outside the worktree

`D:/SiloPlayer-build/admin-completion-lane-a` run outputs and results for the labels above (`notifref-*`); the storage-guard state file
`C:/Users/Michael/.agents/state/silo-storage/task-notifref.json` and `task-notiffix.json` (review pass; run outputs `notiffix-*`); scratch files in the session scratchpad
(`C:/Users/Michael/AppData/Local/Temp/claude/fb5292c745ca8314/67ef881d-8656-4afa-bc83-23c9b81aee06/scratchpad`) and small Node edit
scripts in the system temporary directory. Nothing under `%LOCALAPPDATA%\SiloPlayer`, `C:\Program Files\Silo Desktop Player` or `D:\SiloPlayer`.
