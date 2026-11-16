# Admin Configure: Network access

Reference: official Silo Server/WebUI `abd85d0cadac28f4ab7db6821dad9634fc114790`, freshly fetched from GitHub main on November 15, 2026. `NetworkAccessSettings.tsx`, its test, `hooks/queries/admin/networkAccess.ts` (and test), `usePageActivity.ts`, `internal/apiv2/network_access.go`, the network-access contract fixtures and the English catalog are unchanged since `c11b0a968f0d5980b7005579fb95acc2d11d8321`. Read at the explicit SHA, not the clone's historical HEAD. Desktop baseline is Beta `2c33599f751e1bb7e18a56ee378ffe918710d74d` (`1.2.1145-beta.1`, numeric `1.2.1144.1`); the pipeline owns stamping.

## Status

Network access (C7) is **In progress**: implemented and verified locally, not yet pushed. The lead reviews it and opens the Beta PR. It is independent of Security PR175. The other Configure leaves remain unfinished. Nothing here is merged or released.

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

See `docs/current-development.md` and `D:/SiloPlayer-build/server-update-nov14/configure-network-access/` for exact test counts, hashes, captures and limits. All checks use a synthetic server at this machine's DPI. There is no live Tailscale/NetBird plugin, live server, complete visual or all-DPI parity, Narrator certification or measured performance claim.
