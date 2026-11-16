# Configure Network Access — C7

Status: **Final local GREEN verified; independent Standards and Spec reviews clear; qualifying Beta PR submission ready.** The complete feature is reconciled with Beta `3350f1b90073af39762f9be849bc5949dbc79c50` (1.2.1162-beta.1; numeric 1.2.1161.1). Official Silo Server main was fetched directly on November 16, 2026 and remains `abd85d0cadac28f4ab7db6821dad9634fc114790`. The pipeline owns version changes.

The earlier r6 behavioral RED established two capability-priority defects and a retained provider-heading defect. Their repairs preserve background admission and update the retained visible/accessibility heading in place. Historical r8 completed all eight native groups (1,591 PASS, zero FAIL). Later specification review identified early action wrapping; r10 on November 16, 09:13:59.4118378–09:16:20.0811558 UTC produced exactly four expected action-placement failures at 1440×900, 1440×600, 1920×600 and 800×600. The narrow measure repair now lets details shrink beside the nonshrinking action whenever the action and gap fit. Final r14 verifies that repair on the reconciled source. A later Standards finding identified feedback that claimed a queued status read had already completed. The corrected r13 fixture produced exactly six complete-message assertion failures; the three messages now say that a status refresh was requested.

## Delivered behavior

The page is registered through the production `AdminConfigureServices.Add` entry point at `/admin/settings/network-access` in Configure > Server, with its sidebar leaf and search entry. It opens without a page transition and uses the full-width gutter scroller with a left-aligned 1120 settings column. It is a tool page: no settings draft, SaveBar or leave guard.

- Header "Network Access" with the settings description, then the official intro paragraph (prose width).
- Capability loading skeleton (one-third bar and full-width block); a failed capability read shows "Could not read network access providers." as an assertive alert with a native Retry. Capabilities are never polled.
- No providers: the "No providers installed" group with the official body.
- One FieldGroup per installed provider, in server order, titled with its display name and "Access Silo through your {name} network without port forwarding." Each group has its own skeleton, status error ("Could not read provider status." or a specific permission/limit message), "No host runs this provider yet." or the host list. One provider's failure never hides another's hosts.
- Each host row: "{name} · API server/Proxy node" (the role alone for an unnamed host), the state text with an 8 DIP dot (emerald connected; amber connecting/waiting; red error; muted disconnected/plugin not running), an outline provider-version badge, "Clients reach this host at {origin}" with a link (plus "({hostname})" when it differs), hostname alone when there is no origin, comma-joined addresses, "Open the authorization page ↗ to finish enrolling this host." only while waiting for authorization, the host error as a polite status line, and "Last heard from {date, time}".
- Connected, connecting and waiting hosts offer outline Disconnect; others offer filled Connect, disabled for a host whose plugin is not running, with "Start the plugin from the Plugins page first." as its help text and as the tooltip on its wrapper. A pending command disables only that host's action and shows the Loader2 spinner. Actions carry host-specific accessible names ("Connect Living Room · API server").

## Wire, safety and resource bounds

`AdminNetworkAccessApi` is a separate domain API on the bounded Monitor transport: every call runs under a captured admin lease, has a deadline (capabilities 15 s; status and commands 30 s, the server asks each host with a 10 s timeout), reads at most 1 MiB and never replays authentication. Responses are validated: a mismatched provider, duplicate or empty host id, missing state or hosts list, duplicate or unsafe provider slug, or a list over its bound (32 providers, 256 hosts, 64 addresses) is reported as an explicit error, never silently trimmed. Display text is bounded with a visible ellipsis. Provider slugs are validated against the server pattern before any path is built.

Connect and Disconnect send exactly `{"hosts":["<captured host id>"]}` once. A host list is never omitted or empty (which would mean every host). A 202 acknowledgement must name the same provider and include the commanded host; it replaces that provider's status immediately and a follow-up status read is queued. An older read already in flight is discarded. A transport failure, timeout, 408 or 5xx response, or an unusable acknowledgement is an unknown outcome: the page says so and reconciles with a status read, never a resend. Definite rejections keep the last known state; an unknown host or provider re-reads the list.

`auth_url` is plugin-provided, unvalidated enrollment data. It stays in memory for the current owner, is excluded from `ToString`, logs, receipts and failure text, and is cleared on retirement or departure. A click re-reads the current owner's state, requires a host still waiting for authorization and a safe HTTP(S) URL (`ExternalBrowserUrlPolicy`), then uses the page launcher. A non-web origin is shown as text, not a link. Native fixtures replace the launcher before any data arrives, so synthetic URLs are captured and never opened.

The page-owned controller runs at most two status reads at once, coalesces a provider that is already queued or running, and polls every 15 s only while the page is open and its window is visible and active. Hidden or deactivated windows drop the timer and queued reads. Activation refreshes only statuses older than 15 s and capabilities older than 30 s. A poll tick is skipped while playback is loading. Departure, outer Admin suspension and authority change cancel reads and commands, discard late results and clear rows and links. Polling updates rows in place by host id, so focus and hover survive; a command's busy state returns focus to that host's action when the user has not moved it.

## Native differences

- Capability failure offers Retry (the WebUI's react-query refetches on window focus only).
- Command failures use the app toast with native wording for unknown outcomes, unknown hosts and permission/profile/transport failures instead of raw server or plugin text.
- Status reads are not retried automatically (react-query's default three retries); the next 15 s poll is the retry.
- Polling also pauses while the window is deactivated (the WebUI's `canApplyRealtimeUpdates` uses visibility only), to keep background work subordinate.
- The `rounded-md` small button is 10 DIP at the 0.75rem theme radius, matching General and Email.

## Verification and limits

Final r14 held one continuous nonblocking build lease on November 16, 2026, **10:13:24.3001843–10:20:55.7413531 UTC**. Nonincremental unit, app and native-host builds completed with zero compiler errors. The actual xUnit source-root probe passed 1/1; 952 focused and 7,465 full unit tests passed with zero failures/skips. The full run excludes only the separately executed source-root probe. PDB checksum binding verified 519 test, 476 Core, 15 Player, 986 app and 238 fixture source documents.

Eight native groups completed with **1,602 PASS / zero FAIL**: Network Access 346, Security 374, Email 297, General 188, Manage foundation 36, Subtitles 64, Monitor 113 and Dashboard 184. Result hashes, completion sentinels and final authority-retirement assertions were independently read back. All 2,206 frozen runtime inputs remained unchanged. Current publish/reference/host DLL and PDB bytes matched; product DLL SHA256 is `6A1789F06CF6B98D26DEF83BCB1EF1827B35A842FDA112AF823DF97DFCBBB102`.

The root reviewed synthetic captures at 1440×900, 1440×600, 1920×600 and 800×600, including the long-detail/action layout. Receipt and immutable logs remain under `D:/SiloPlayer-build/server-update-nov14/configure-network-access/network-final-green-beta3350-r14-receipt.json`. The r14 seal's inherited review inventory contains four stale hashes, recorded explicitly in `network-r14-root-postrelease-readback.json`; its separate complete runtime inventory matches the tested files. Final reviews use a fresh per-path inventory. Historical Security input failures remain unclassified; this successful run does not establish their cause.

All checks use a synthetic server at this machine's DPI. There is no live Tailscale/NetBird plugin, live server, complete visual or all-DPI parity, IME, Narrator certification or measured performance claim.

## Final source review and submission

On November 16, 2026, independent Standards and complete-Spec reviews each cleared the final 23-path candidate against Beta `3350f1b90073af39762f9be849bc5949dbc79c50` and official `abd85d0cadac28f4ab7db6821dad9634fc114790`. Both verified all 2,206 runtime inputs and 23 review-path hashes before and after review. The root rechecked those sources and all eight native result hashes/completion sentinels before submission. Only this scoped record and the Configure checklist changed after those reviews; no tested runtime input changed.

Current remote Beta `5afc579310fae248d1249ef590578b8ababb74c6` differs from the tested baseline only in README content. Version stamps and shared development history remain under pipeline ownership. The qualifying PR and exact submitted head are recorded in the external delivery receipt; merge and release are separate pipeline outcomes.
