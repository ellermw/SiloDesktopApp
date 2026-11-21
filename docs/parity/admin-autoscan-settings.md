# Autoscan › Settings — native parity note, November 20, 2026

## Source and scope

- Native page: `/admin/settings/autoscan` (Manage › Autoscan › Settings), `AdminAutoscanSettingsPage`.
- Official reference: `Silo-Server/silo-server` commit `66b836f82a32de2fb3e5dfae6c285c19eea1096d`, read with `git show` from
  `D:/SiloPlayer-work/server-upstream-nov14`. The first native source was written against `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.
- Diff between the two pins for this page: `AutoscanSettings.tsx`, `ConnectionsPanel.tsx`, `useAutoscan.ts`,
  `adminAutoscanConnections.ts`, `adminAutoscanInspection.ts`, `SaveBar.tsx`, `SettingsPageHeader.tsx`, the autoscan keys of `en.json` and the
  autoscan connection/settings handlers under `internal/apiv2` and `internal/api/handlers` are **unchanged**. What changed nearby and does not
  affect this page: `SettingField` gained an environment-managed (`managedByEnv`) row state that Autoscan does not use (these are not
  `server_settings` rows), `schema.ts` regenerated, and several unrelated test files were removed upstream.
- Out of scope (other families): Autoscan Sources, Autoscan Activity, the global enable switch, Run now and webhook setup.

## Official page, section by section

| Official | Native |
|---|---|
| Title "Autoscan" and subtitle (`search.autoscan.description`) | Same text. |
| Skeleton "Loading settings" while the settings read is pending | Skeleton with that accessible name; the connections list shows "Loading…" on its own. |
| "Autoscan settings could not be loaded. Reload before editing." | Same text in an alert with a Reload button (native addition: the web page re-fetches on re-entry). Connections stay usable. |
| Group "Polling defaults" (+ description), rows "Default check interval" and "Debounce" (+ description), unit "seconds", dirty dots | Same groups, rows, descriptions, units, per-row and group dirty dots. |
| `Number(value) \|\| 300` and `\|\| 0` coercion on every keystroke; no range check | `AdminAutoscanNumber.Coerce` reproduces it (empty/NaN/0 interval reads 300, empty debounce 0, exponent and fractions kept). Native extra: values outside 1–2147483647 / 0–2147483647 are refused before sending with a message; the server enforces the same ranges. |
| One SaveBar (count, Save, Discard), unsaved-changes guard | `AdminSettingsSaveBar` and `AdminLeaveGuard`. |
| Save sends `{...saved, ...draft}` (`enabled` comes from the cached read) | Native re-reads the settings first, sends the newest `enabled` with the two defaults, and refuses to overwrite polling defaults that changed on the server since the page loaded them (the PUT has no revision precondition). |
| Success toast, plus warning toasts for `reschedule_state` `failed` / `not_configured`; error toast "Settings persistence could not be confirmed…" for any failure | Status lines with the same texts. A 4xx is shown with the server's own detail and the draft stays editable; only a timeout, transport loss, 5xx or malformed answer shows the official unconfirmed text and blocks writes until Reload. |
| Connections group "Connections" + description, "Add connection" action in the group heading | Same, `SetAction` in the heading. |
| Table Name / Kind / Source / URL, Sonarr/Radarr labels, "Reused from Requests" / "Own" badges, "—" without a URL, edit and delete icon buttons | Same columns and badges, native icon buttons with names and tooltips. Saved URLs are displayed without embedded credentials, query or fragment; the editor keeps the raw stored value. |
| "Loading…" and "No connections yet." | Same. A failed list read is shown as a failure with Reload, not as an empty list (native fix). |
| Add dialog: "Connection type" (reuse disabled without Requests Sonarr/Radarr servers), Requests server select with `{name} ({kind})`, no-server text with link to Settings › Requests, name / kind / base URL / API key, inline Test result, Cancel, "Add connection" / "Save changes" / "Saving…" | Same, native `ContentDialog`. Reuse is the default when a Requests arr server exists. The Requests link opens the real `/admin/settings/requests` route. |
| Edit: no mode picker, "(leave blank to keep existing)" with a masked placeholder when `has_api_key`; save needs name, URL and a key or a saved key | Same. Stored kind or URL that fail a client check stay in the fields and save unchanged. |
| Test body branches (`connection_id` for saved records unless a new URL and key were typed; `request_integration_id` for new reuse; `base_url` plus optional `api_key_ref`) | Same four branches. The server's fixed failure text is shown (redacted, bounded); advisory only, never blocks Save. |
| Delete AlertDialog "Delete connection?" with source-binding warning, "Delete" | Same text. A refusal keeps the row and may be retried; an unknown outcome locks until Reload. |
| Create / update / delete success toasts and "…could not be confirmed…" toasts | Same texts as status lines. |
| Settings search keys (`pollingDefaults`, `defaultCheckInterval`, `debounce`, `connections`, `addConnection`) | Already in `AdminRouteCatalog`; the page is registered so the route opens. |

Not in the official page and therefore not built: a global enable switch, Run now, source list. The official Settings page has no links to Sources or
Activity, so no disabled links exist.

## Requests, credentials and writes

- Requests are `GET/PUT /autoscan/settings`, `GET/POST/PUT/DELETE /autoscan/connections[/{id}]`, `POST /autoscan/connections/test` and, for the reuse
  list, `GET /request-integrations` plus one `GET` per integration (service kind from `plugin_config.service_kind` only; no credential field is read).
- Bodies are `Dictionary<string, object?>` so they are serialized as JSON with exactly the official fields. Limits follow the server (name 1024,
  kind 256, base URL 8192, key 8192, id 256).
- A key the server did not return is never echoed or cached. An untouched key field sends nothing (there is no clear operation in this API). A typed
  key survives a refused save so the corrected retry sends it; it is cleared on success, on an unknown outcome, on a changed target, when the
  dialog closes and when the page, server or account is retired. Refusal and check texts are redacted (the typed key, then recognizable credentials
  and URLs) before they are truncated to 500 characters.
- Writes are sent once, without token-refresh replay. 4xx other than 408 is a refusal; timeout, 408, 5xx, transport loss, a 2xx without a usable
  record, or a delete not answered with 204 is an unknown outcome that blocks writes until Reload re-reads the full list. Editing or deleting an
  existing row first re-reads the list and refuses if that row changed.
- Reads are bounded: 1 MiB per response, 100 connections per page and at most 10 pages, 50 Requests summaries per page and at most 4 pages.
  A read answered with 401 refreshes the access token once and is replayed, so an expired token does not leave the page unloadable.

## Verification

See the November 20 entry at the top of `docs/current-development.md` for exact runs, counts and limits. Wide and narrow captures plus four dialog
captures are produced by the `admin-autoscan-settings` native selector and were viewed for layout; visual parity with the web page, all DPIs and
a live Silo server were not verified.
