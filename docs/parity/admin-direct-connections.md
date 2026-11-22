# Configure Network Access — Direct connections section

Status: **built and verified locally (November 22, 2026).** Sections 1–6 were written from the official source before any native code; sections 7–9 record what was built, how it differs and what was observed.

Official source (read only): Silo Server commit `c90e406db71ee468bffdfa5733cd97d1d50b4019`; the feature is commit `b6f104933` (PR #5047, "show administrators whether the host can accept direct connections"). None of the section's files changed between `b6f104933` and the pin. Files read: `web/src/components/admin/DirectConnectionsSection.tsx` and `.test.tsx`, `web/src/hooks/queries/admin/directConnections.ts` and `.test.ts`, `web/src/pages/admin-settings/NetworkAccessSettings.tsx`, `web/src/hooks/useSettingsForm.ts`, `web/src/components/settings/AdvancedSection.tsx`, `web/src/api/v2/schema.ts` (`DirectConnections*`, the two operations), `web/src/locales/en.json` (`web.admin.settings.network.direct.*`), `internal/apiv2/direct_connections.go`, `internal/config/admin_settings.go` (the three settings and `normalizeSTUNServers`), `internal/api/handlers/admin.go` (settings write and refusal answers).

## 1. Where the section sits and when it exists

On the Network access page the section is the last block, after the providers group (a later fix moved it there so the providers intro still points at the provider list). The page keeps the header, the intro, then the provider groups, then this section.

The section **does not exist** unless `direct_connections.enabled` is `"true"` in the saved (effective) settings, or the form has at least one unsaved edit (any of the three keys). An administrator cannot switch the feature on from this page: the server calls the setting hidden and expects it to be enabled through the API. The section stays visible after another administrator turns the feature off while the form holds a draft, so the draft can still be saved or discarded. When the saved value is not `"true"` and nothing is staged, nothing of the section renders and no status request is made. While the settings have not been read, the saved value reads as empty, so the section is absent.

## 2. Elements in order

1. **Group** "Direct connections" with the description "Check this server’s readiness for direct connections." and the unsaved-changes dot while any of the three keys is dirty.
2. **Row "Enable direct connections"** — a switch (`direct_connections.enabled`, no description), unsaved dot when dirty, disabled while a save runs.
3. **Diagnostics block** (padding 16 above and below, 16 between children):
   1. A wrapping line, items centred, space between: the muted note "Readiness checks do not start direct playback." and an outline small button **"Run check"**. The button is disabled while a check runs and shows a spinning loader before the label.
   2. When the last Run check failed: a destructive 14 px alert line with the error text. It stays until the next check starts or succeeds.
   3. The status area: skeleton while the first read is loading (a 20 px bar at two thirds width, a 80 px block, `aria-busy`); a destructive alert line with the error text when the read failed; otherwise the host list (`hosts` of the answer, in order; the server sends exactly one host, `api`).
4. **Advanced disclosure** (`AdvancedSection`, id `network-access.direct-connections`): a full-width row "Advanced" with a chevron and the count 2 on the right, accessible name "Advanced · 2 settings", closed by default, the choice persists per browser, and it is forced open while either advanced key is dirty (a manual collapse does not outrank a new reason to force it open). Inside, under a 2 px accent rail:
   1. **Port mapping** switch (`direct_connections.port_mapping`) with the description "Check the router for UPnP, NAT-PMP, or PCP support."
   2. **STUN servers** text field (`direct_connections.stun_servers`) with the description "A comma-separated list of up to eight host:port entries. Leave it empty to use the defaults." The field is an ordinary text input; there is no list editor.
5. **Save bar** (shared): count of dirty keys, Discard, Save, over the three keys as one batch.

### Host block (one per host)

- Heading (level 3) **"API server"** (always this text for the `api` host; the host's own name is not shown).
- Muted 12 px line **"Last checked {time}"**, time = date and time without seconds.
- Feature list (a definition list, hairline between rows), one row per feature in the server's order. Every row has one layout: the label on the left (wraps), the state on the right on the label line (no wrap), then optional detail lines under the state, right aligned, muted 12 px, wrapping.
  - Label: `direct` "Direct connections", `nat_mapping` "NAT mapping", `nat_hairpin` "NAT hairpin", `ipv4` "IPv4", `ipv6` "IPv6", `port_mapping` "Port mapping", `reachability` "Reachability", `socket_receive_buffer` "Socket receive buffer", `socket_send_buffer` "Socket send buffer", `gso` "GSO", `gro` "GRO", `pmtu_fallback` "PMTU fallback", `cap_net_admin` "NET_ADMIN capability". A feature name the build does not know is shown as sent.
  - State: `on` "On", `off` "Off", `capped` "Capped", `fallback` "Fallback", `degraded` "Degraded", `unknown` "Unknown". A state the build does not know is shown as sent with underscores replaced by spaces.
  - Detail line 1 — **reason** (if present): the reason text below; an unknown reason is shown as sent with underscores replaced by spaces.
  - Detail line 2 — **requested/granted**, when `requested` or `granted` is present: "Requested: {size}; granted: {size}". Sizes use the file-size format (1024-based, one decimal, "32.0 MB"); a value that is absent, not a number or not positive reads "Unknown".
  - Detail line 3 — **detail** (if present): for `reachability` and `nat_mapping` "Observed public address: {address}"; for `port_mapping` the comma-separated protocols as UPnP / NAT-PMP / PCP (any other protocol upper-cased), joined with ", "; for every other feature the text as sent.
  - **NET_ADMIN row exception.** For the `cap_net_admin` feature with state `off` and reason `no_cap_net_admin` or `net_admin_dropped`: no reason line, and the state reads **"Not needed"** unless some other feature row carries one of those two reasons or some listener's send buffer carries the reason `no_net_admin`, in which case the state stays "Off" (the reason is then shown once, on the row that needed the capability).
- **Listeners** subsection, only when the host has at least one listener: heading (level 4) "Listeners" and one row per listener. Label: `api` "API", `jellyfin` "Jellyfin", `abs` "Audiobookshelf", `proxy` "Proxy" (unknown shown as sent). State on the label line: "Congestion control: {state}" where the state is "Off" when the requested algorithm is `off`, else the listener state text. Detail lines:
  - when `requested` is not `off`: "Algorithm: {algorithm}" — the granted algorithm upper-cased, or "requested {requested}; active {active}" (both upper-cased, `off` as "Off") when they differ;
  - the reason (if present), "{reason} ({errno})" when the listener has an `errno`;
  - always "Send buffer: {state}" — "Not needed" when the send buffer's reason is `not_needed`, else its state text;
  - "Requested: {size}; granted: {size}" only when the send buffer's `requested` is greater than 0 (the granted size reads "Unknown" when not positive);
  - the send buffer's reason, "{reason} ({errno})" with its `errno`, unless the reason is `not_needed`.
- **Host tuning** subsection, always shown (heading level 4 "Host tuning"; an empty knob list shows only the heading). One row per knob, the label spanning the two lines so the detail lines sit under the value:
  - Label: the plain name for a known variable (`SILO_NET_TCP_CONGESTION` "TCP congestion control", `SILO_NET_TCP_SEND_BUFFER` "TCP send buffer") with the variable name under it in monospace 12 px muted, or the variable name alone in monospace for an unknown one.
  - Value: "Effective: {value}". Detail lines: "Default: {value}", "Requested: {value}" (when present), "Allowed: {values}" (when the list is not empty, values joined with ", "), then the reason (if present).
  - Value formats: for the congestion variable `off` reads "Off", an algorithm named in `allowed` reads upper-case ("BBR"), anything else shows as sent ("bbrr"); for the send-buffer variable `off` reads "Off", a digits-only value reads as a size ("32.0 MB"), anything else shows as sent ("lots"); other variables show as sent.

### Reason texts

`disabled` "Disabled"; `admin_off` "Disabled by administrator"; `kernel_unsupported` "Not supported by this kernel"; `limit` "Host buffer limit reached"; `force_failed` "Could not increase buffer"; `no_cap_net_admin` and `no_net_admin` "NET_ADMIN capability missing"; `net_admin_dropped` "NET_ADMIN capability dropped"; `unsupported_platform` "Not supported on this platform"; `probe_failed` "Check failed"; `not_available` "Not available"; `no_gateway` "No router found"; `gateway_unsupported` "Router does not support port mapping"; `mapping_varies` "Public address varies by destination"; `not_measured` "Not measured"; `module_missing` "BBR is not available"; `module_not_loaded` "BBR is not loaded or allowed"; `overridden` "Host policy selected another algorithm"; `kernel_error` "System could not apply or read this setting"; `not_allowed` "Permission denied"; `limits_unknown` "Could not read host buffer limits"; `helper_unavailable` "Network tuning helper unavailable"; `granted_below_request` "Buffer is smaller than requested"; `not_needed` "No increase needed"; `invalid` "Invalid value; using off". The catalog also carries reasons the section's table does not list (`engine_start_failed`, `multiple_api_replicas`, `replica_*`, `state_store_unavailable`, `udp_port_*`) for a later engine; the section treats them as unknown reasons and shows them as sent.

### Tone and states

There are no colours: every state word is plain text. Reasons and notes are muted. Only the two error lines (Run check, status read) are destructive. A state is never shown by colour alone.

## 3. Requests

| Request | Sent | Answers |
|---|---|---|
| `GET /api/v2/admin/settings/effective`, `GET /api/v2/admin/settings/sensitive-status`, `GET /api/v2/admin/settings/restart-keys` | When the page opens (the shared settings form; official `useSettingsForm` reads the same) | Effective values for the three keys (defaults are included: enabled `false`, port mapping `true`, STUN `stun.cloudflare.com:3478,stun.l.google.com:19302`), the keys the environment sets, restart keys. |
| `GET /api/v2/admin/direct-connections/status` (no query, no body) | When the section appears (first read), then every 15 s while the page and window are in front, and on activation when the last read is 15 s old | `{enabled, hosts:[{host:{id,name,role}, checked_at, features[], listeners?[], knobs[]}]}`. Polling reads the last completed check and starts no probe. |
| `GET /api/v2/admin/direct-connections/status?refresh=true` | Only when the administrator presses Run check; never retried and never replayed after an authentication refresh | Same body. Starts a check or joins the one in flight; the server may take about 30 s (15 s settings read plus 15 s check). |
| `PUT /api/v2/admin/settings` with `{"values":{<dirty keys only>}}` and `If-Match` | Save | `{values, restart_required}` plus the new `ETag`. A 400 carries the server's message (managed by environment, invalid STUN list); a stale `ETag` is 412. |

The capabilities operation (`GET /api/v2/admin/direct-connections/capabilities`) exists but the section never calls it.

Errors: `GET status` answers 401, 403, 404, 406, 422, 429, 500, 503. 503 is "service missing" or "cannot read settings in time" (temporary). The page shows the error's message under the status area.

## 4. Polling, ordering and cancellation (official `directConnections.ts`)

- The status query is keyed by server origin, auth context version, profile id and profile token generation; a change of any of them hides the old data and starts a new read, and a late answer to the old key is refused.
- Interval 15 s while the page can apply realtime updates (visible); `staleTime` 15 s, so a remount or focus returns the cached data and reads again only when it is older than that. Polling stops when the section is gone (disabled and nothing staged) and when the page is left.
- **Request order decides, not the server's clock.** Each poll remembers the count of refresh results published when it started. A poll result that arrives after a refresh result was published since the poll started is dropped. A refresh result always replaces the cached status.
- Run check cancels any poll in flight for that key, sends the refresh once with no authentication replay, and publishes the result only if the initiating authority is still active. A refresh failure is shown under the button and does not touch the status data.
- A second Run check cannot start while one is pending (the button is disabled).
- A failed poll after data was shown: react-query keeps the data but the section tests `isError` first, so the official section replaces the list with the error until the next poll succeeds. The native page deliberately differs (see "Native differences").

## 5. Behaviour around the form

- **Unsaved changes**: the three keys are one batch. A value equal to the saved value undoes the edit. Diagnostics are not tied to the draft: Run check and polling always read the server's saved settings. Turning the switch off and saving hides the whole section.
- **Saving**: the save bar sends only dirty keys. The server stores the canonical STUN list (lower case, trimmed, de-duplicated, IPv4-mapped IPv6 written as IPv4) and answers the canonical values. A rejected save keeps the draft and shows the server's message as a toast.
- **Environment-managed settings**: the official section passes no `managedByEnv` to its fields, and `useSettingsForm` silently drops a draft for a key that the sensitive-status lists in `managed_by_env_vars`. The native page uses its own established handling (read-only control, the "Set by {env} in the server's environment." note, never sent).
- **Non-owner / non-acting admin**: the official section makes no distinction; both operations are "acting admin" class. A caller without permission gets 401/403 and sees the error text.

## 6. STUN list validation (server, `normalizeSTUNServers`)

The official page does no checking of its own; the server refuses a bad value with 400 and its message as the toast. The rules, in order, for the trimmed text of the setting:

1. Empty text is valid and stored empty ("use the defaults").
2. The text is split on commas. More than **8** entries: "direct_connections.stun_servers accepts at most 8 entries" (counted before de-duplication).
3. Each entry is trimmed and lower-cased, then split as `host:port` (an IPv6 literal needs brackets). Anything else: "direct_connections.stun_servers requires host:port entries: {reason}" (an empty entry, a missing port, too many colons).
4. The port must be an integer from 1 to 65535: "direct_connections.stun_servers requires ports between 1 and 65535".
5. The host must be an IP literal or a DNS name: at most 253 characters; labels of 1–63 characters made of `a–z`, `0–9`, `-`, not starting or ending with `-`; the last label not made only of digits (so `192.168.1.256` is a mistyped address, not a name). Otherwise: "direct_connections.stun_servers contains an invalid IP address or DNS host".
6. An IPv4-mapped IPv6 literal is stored as its IPv4 endpoint; duplicates after normalisation are dropped.

## 7. Delivered native behaviour

- `AdminDirectConnectionsController` (Core) holds the three-key settings form (shared `AdminSettingsForm` over the shared settings store) and the status reads: a read on appearance, a 15 s poll while the page and window are in front and playback is not loading, activation reads only when the last read is a full interval old, Run check, retry. `AdminDirectConnectionsApi` owns the two status requests (lease-bound, deadlines 35 s and 45 s, 1 MiB bound, validated, lists over their bound reported, no authentication replay). `AdminDirectConnectionsPresentation` and `AdminDirectConnectionsRules` hold the wording, state mapping, section existence rule, poll ordering rule and STUN rules. The page section is `AdminNetworkAccessSettingsPage.Direct.cs`; the provider controller, rows and the two lines other pull requests own are untouched.
- Request table: see section 3. The native page sends exactly those four requests (plus the two settings reads the form needs): the status read when the section appears and on every 15 s tick, `?refresh=true` only on Run check, `PUT /api/v2/admin/settings` with only the staged keys.
- Order in the page: header, intro, provider groups, Direct connections group (enable row, diagnostics, advanced), save bar.
- Existence: while the settings have not been read the section is absent; a staged edit keeps it present after another administrator turns the feature off.
- Accessibility: every row has a name that joins label, value and details; Run check keeps its name while it spins; the advanced row announces expanded or collapsed; message lines are live regions; the dirty dot, environment note and error lines belong to their rows.

## 8. Native differences, and what is not implemented

- **A failed poll keeps the data.** The official section tests `isError` first and replaces the list with the error until the next poll succeeds. The native page keeps the last status on screen with "Could not refresh direct connection status. The last result is shown." above it and a Retry, and keeps polling.
- **A failed first read has a Retry** (the official page offers only Run check, which starts probes). Retry is a plain read; it never starts a probe.
- **A failed settings read is visible** with a Reload ("Could not read the direct connection settings."). Officially the section silently does not appear.
- **Messages from the server.** The official section prints the error's message for a failed read or refresh. Native read failures use the app's own localized wording; a refused Run check or a refused save shows the server's one-line detail (bounded, redacted, plain text), and a save refusal is also raised as a toast as the official save does.
- **Unknown Run check outcome.** The official section shows a generic error. Native says the check may or may not have run, reads the state again and never re-sends.
- **Environment-managed keys** are read-only with "Set by {variable} in the server’s environment." and are never sent (the official section silently drops the draft).
- **STUN list** is checked before Save with the server's rules and messages (the official page lets the server refuse it and toasts the message). The server stays the authority: its refusal is shown too, and a saved list that fails the check is left alone until edited.
- **While a save runs** the three controls are disabled, as officially. An unknown save outcome and a stale validator block Save until Reload.
- Not implemented: the capabilities operation (official never calls it from this section); the later `engine`/`connected devices`/`replica` strings of the catalog (the section at the pin does not use them); a way to switch the feature on (the server marks it hidden, officially also unavailable from this page); a localisation catalog for other languages (the admin pages here are English-only).
- The advanced disclosure choice is remembered per machine through the shared `LoggingDisclosurePreferences` store (key `network.direct`), as the other disclosures.

## 9. Verification (November 22, 2026)

Lane A, source on Beta `e21546981`. Final run `directconn-final-r3`: focused unit 314 passed; full unit suite 12,136 passed, 0 failed; native `admin-configure-network-access` 716 PASS (the existing 345 provider checks plus 371 for this section: absence, loading, every state, polling, Run check running/refused/unknown/stale, the settings editor and STUN limits, save/refuse/unknown/conflict, environment-managed keys, switching off, failed read and reload, layout and corner shapes at 1440, 1280, 1024, 800, 640 and 560), `admin-configure-general` 198, `admin-configure-security` 376, `admin-manage-foundation` 36, `admin-monitor` 1,151; no FAIL line in any.

Mutation proof (`directconn-mutation-r1`, focused unit run): with `StateLabel` returning the raw value and the STUN limit and host checks disabled, 23 of the 130 Direct connections tests failed (every state-mapping case, the NET_ADMIN, listener and feature row cases, the STUN limit and host cases and the draft-blocks-Save case); the source was restored and `git status` was clean.

Screenshots read from the native run (loading, loaded, poll failed, checking, refused, unknown, advanced, invalid STUN, refused/unknown save, and the section at 1440, 1024, 800 and 560 top/middle/bottom, plus the 560 error state): defects found and fixed: a long tuning variable name cut off at 560 (it now wraps inside its row) and a hairline under the last advanced row (removed, as the official last row has none). Nothing further was cut off, overlapping or oddly shaped in the final captures.

Not verified: a real Silo Server; displays scaled to 125% and 150%; behaviour against a server that serves more than one API replica (the official note says each replica answers for itself).
