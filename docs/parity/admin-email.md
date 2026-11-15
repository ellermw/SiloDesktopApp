# Admin Configure: Email

Reference: official Silo Server/WebUI `c11b0a968f0d5980b7005579fb95acc2d11d8321`, freshly fetched from GitHub main on November 15, 2026 (main unchanged). Read at that explicit SHA: `web/src/pages/admin-settings/EmailSettings.tsx` and its test, `web/src/components/admin/EmailTestRow.tsx`, `web/src/components/settings/SecretField.tsx`, `web/src/lib/email.ts`, `web/src/hooks/useSettingsForm.ts`, `FieldGroup`/`SettingField`, the English catalog and `internal/apiv2/admin_email_test_send.go`. Desktop parent is General PR173 head `905f9ba2741798b1f707773a1132a60d013dd761` on Beta `a06cc8b3867f1ab4d490324e1ce5696238173043`; version `1.2.1081-beta.1` remains under pipeline control.

## Status

Email (C2a) is **In progress**: implemented and verified locally, awaiting lead review and a pushed Beta PR. It depends on unmerged General PR173. Security (C2b) and the other 17 leaves are unfinished. No Configure change is merged or released.

## Delivered behavior

Email is registered through the production `AdminConfigureServices.Add` entry point at `/admin/settings/email` in Configure > Integrations, with its own navigation/search entry. Page transitions stay suppressed. The page reuses General's guarded settings API, the shared snapshot store, `AdminSettingsForm`, the settings row/group/SaveBar primitives and the full-width gutter scroller (1120 settings column at the left gutter of the centered 1560 frame).

One Mail server group carries the official description, group dirty dot and restart-all treatment; rows carry their own dirty dots and server-marked restart chips. All eight rows remain visible whatever `email.enabled` says:

- `email.enabled` toggle (Send email from this server); `from_address`, `from_name`, `smtp_host` text fields with the official example hints; `smtp_port` number field.
- `email.smtp_security` select with STARTTLS / TLS (implicit) / None (insecure). An empty saved value displays STARTTLS without staging an edit; choosing STARTTLS again clears the edit rather than writing the default.
- `email.smtp_username` with its empty-for-relay description.
- `email.smtp_password` write-only SecretField: presence only from sensitive status, a masked placeholder, typing stages a replacement, erasing a replacement keeps a saved password, Clear saved value stages an explicit empty write and Keep saved value or Discard withdraws it. The action sits before the input; there is no Replace step and no per-field dirty dot (as upstream). With no saved password, an emptied input remains a staged empty value (official `useSettingsForm`).

Saves send one strong If-Match write of the dirty Email keys. Conflict and unknown outcomes keep the draft and require explicit Reload; there is no authentication replay or automatic retry. The password editor and its Clear/Keep action freeze during a save; later nonsecret edits stay dirty over the acknowledged canonical values. A failed sensitive-status read is never treated as "no password": the password is reset and protected, a polite live-region description explains the state and Retry reloads it, while unrelated drafts stay saveable. An environment-managed key is disabled, explained and excluded from writes.

The test row (Test email recipient, outline Send test with send/spinner icon, result line, saved-settings hint) issues one `POST /api/v2/admin/email/test` whose body is only `{"to": trimmed recipient}` under the captured admin/profile authority. It uses the saved server settings even while an SMTP draft exists, and the hint is shown whether or not the page is dirty. Empty input disables Send; malformed input reports the official invalid-email message before any request (official `lib/email.ts`: one "@", no whitespace, domain with a non-edge dot, or a bracketed dotted IPv4 / bare IPv6 literal). A pending test suppresses duplicates. Success shows the green `Delivered to the mail server in {duration_ms}ms.`; `ok:false` shows the redacted, bounded server message or `Test failed.` in amber. HTTP refusals map to safe native messages without raw server detail. A timeout, transport failure, 408/502/504 or malformed acknowledgement is an unknown outcome and is never resent. Departure or authority replacement cancels owned work and clears the recipient, result and drafts.

Native differences: upstream reports test errors and success through toasts; the native page uses the inline result line as a live region. Native-only text covers env management, the protected password-status retry and the unknown/refusal test outcomes.

## Shared changes

`AdminSettingsUi` now owns the dirty-departure dialog, alert and skeleton previously private to General (moved unchanged), a settings select, a row layout notification and a SaveBar automation prefix (General keeps `GeneralSave`/`GeneralDiscard`). The shared settings store reads General and Email keys together. A queued page render no longer overwrites password typing whose asynchronous `PasswordChanged` has not yet reached the controller; the native regression for this failed on the earlier Email payload and passes on the final one. General has the same PasswordBox echo pattern for its Redis URL; it is not changed here.

## Resource and lifetime bounds

Reads use General's lazily shared 30-second effective/sensitive and five-minute restart-key snapshots with 8 MiB bounds and 15-second deadlines; the test command has a 45-second deadline (server SMTP deadline 30 s). Writes and tests run on page-owned action leases that die with the page or authority. No polling, hidden-page work, library enumeration or prefetch.

## Verification and limits

Final code checkpoint `e7727bbfa2bb77d967b41cde695e526cd2f5a2b7` (clean tree): 616 focused Admin/architecture tests and all 7,071 main unit tests pass (52 new Email/navigation tests), with no failures or skips. Release publish and the matching native host build succeed. Product `SiloPlayer.dll` SHA256 `E529A1C90A34DC0A9BD996F9B580FECB3B7A73D60AB3232C48369935BF28879C` (identical in publish, native-reference and host output); native fixture DLL `D955DBF83AB754BD44923C18058719B249210FDD1C68629EBE61A7003041E786`.

The new `admin-configure-email` group opens Email through `AdminShellPage.TryOpenHref` and the production registration into the shell Frame. It records 296 PASS lines including completion. Coverage includes catalog text, sidebar/search placement, the enable toggle with all rows visible, select popup and selection, password clear/keep/replace/erase, the queued-render race, guarded save with credential freeze and late edit, and the real Send test row. That row covers invalid, pending/duplicate, success, ok:false, fallback and malformed-unknown outcomes. The group also covers the Cancel/Discard dialog, managed/status-failure/retry states, conflict/reload, permission denial, departure during a pending test, Back and authority retirement. Layout checks at 1440x900, 1440x600, 1920x600 and 800x600 cover horizontal overflow, the 1120 column at the frame gutter, side-gutter scroll ownership and left labels. They also cover control containment/names, muted/25 borders and the 448px test row. Normal/PointerOver/Pressed/Disabled/keyboard-focus captures cover Send test, Clear/Keep, Save and Discard, plus select focus and its popup. The lead-facing report lists the captures that were inspected.

The same bytes pass `admin-configure-general` (183 PASS), `admin-manage-foundation` (36), `admin-subtitles` (64), `admin-monitor` (113) and `admin-dashboard` (184), suffix `-email-final2`. Failed candidates stay separate and are not counted. Run `-email-final` on the same source had one unrelated full-suite failure: `DirectHttpReaderTests.Http2TricklingConnectionIsReplacedAndEveryRecoveredByteMatches(2700)` could not open its temp `playback-current.jsonl` because another process held it. The clean rerun passed. Earlier fixture iterations `-email-a`/`-b` and the deliberate red run `-email-c-prefix-red` on pre-fix bytes are also kept separate.

Evidence: `D:/SiloPlayer-build/server-update-nov14/configure-email/` (implementation report, logs, capture hashes); captures under `D:/SiloPlayer-build/results/admin-configure-email-email-final2/`.

These are synthetic-server checks at this machine's DPI. No live mail server, live-server, complete visual/all-DPI parity, Narrator certification, measured wheel delta or performance claim. Historical `admin-monitor-logs` (10 failures/208 passes) is a separate baseline and was not run or changed here.
