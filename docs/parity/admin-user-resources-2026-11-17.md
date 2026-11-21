# Admin user resources — Access Groups, Devices, History Import

Scope: Manage → Users → Access groups (`/admin/access-groups`, `/:id`), Devices (`/admin/devices`, `/:userId/:deviceId?profile=`)
and History import (`/admin/history-import?source=`). Manage → Users itself is a separate, merged family.
Branch `t3/admin-access-groups-devices-history-import`. Official source: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`, read with
`git show` from `D:/SiloPlayer-work/server-upstream-nov14`. Versions are untouched.

## What is implemented

- **Access groups.** List cards (default marker, member count, libraries/downloads/streams/requests facts, the group's own request terms),
  empty and failed states with Reload, create dialog, and the full editor: name, description, default switch (locked on the default group),
  library access (all, or a chosen set; unknown stored ids stay listed), playback quality preset, remote/local bitrate as
  Unlimited / preset / custom Mbps (stored as kbps), downloads with the server-switch notice, request approval and limit
  (inherit / custom / unlimited, with inherited text and the zero-limit hint), stream and transcode limits, household profiles,
  assignable permissions, members (search, select, move with a confirmation naming what changes, Add users, per-account failures), Save /
  Discard / Delete (confirmation names where members move). Save writes the group first and the request limit second; a failed second
  request is reported as such and the first is not repeated.
- **Devices.** Fleet pulse (counts, density, group-by), All/With-overrides scope, filter rail (saved views, platform, override count, last
  seen) with counts, grouped collapsible list with anomaly marks and override gauges, per-device detail (identity, connections with learned
  limits, readout cells, profile tabs, settings scope, per-profile "Reset all"), per-row controls generated from the manifest (switch,
  select, stepper, text, JSON, subtitle appearance), per-row override/inherited badges and values, extreme/conflict/stale row flags and
  per-row Reset. Every write targets one user, profile, device and key through the canonical values route.
- **History import.** Source selector with add / edit / token / delete dialogs (Plex sign-in or pasted token, write-only credentials),
  Plex limits note, user discovery that stays open to map several users, mapping table with account names, lookback choice (Jellyfin),
  run, import-all with per-mapping outcomes, run list with filter, expandable details, cancel, load more and bounded live progress.

## Contracts checked against the pin

- Both embedded resources were compared with the pinned source by script: `admin-user-resources.en.json` holds 556 messages, all identical to
  the pinned `web/src/locales/en.json` ICU text; `admin-device-settings.json` holds the 42 device-scoped definitions of
  `contracts/settings/v1/manifest.json` (revision 29), identical except `3.0` written as `3`.
- Request bodies match the pinned OpenAPI request types (group body: 16 fields; limit: 4; mapping; source create/update; run lookback).
- Device rows follow `deviceOverrides.tsx` (all device-scoped keys except the retired `ui.theme`, in manifest order, filtered only by the
  manifest's advisory `platforms`), not the profile's own device screen.

## Failure model

A 4xx answer other than 408 is a refusal: the server's own problem title, detail and field errors are shown (text that looks like an address
or a secret is dropped) and the draft stays editable. Only a timeout, 5xx or lost transport after sending a write is an unknown outcome, which
blocks further writes until the page is reloaded; a create answered without a usable id is unknown too. Reads that fail keep what is on
screen, show the failure, and are retried by Reload; a later success of the same read clears its message. Responses are applied only while the
page, server and account are unchanged.

## Deliberate differences and limits

- The official phone "save bar" and group toast are not reproduced; Save/Discard/Delete sit at the end of the editor at every width.
- History progress is a bounded poll (at most five active runs per tick, honoring the server's retry hint), not the WebUI event stream.
- Lists are bounded: groups, accounts and mappings fail closed above 2,000 rows (20 pages), devices above 10,000 (100 pages, official message).
- Account names in mapping/run rows are display only; if the account list cannot be read they show "User #id".
- Locale digit shaping and the Ctrl+K search shortcut text are not localized/wired; English text only.
- Visual parity was inspected only through the native fixture's synthetic captures at 1440 and 800 px; no live-server or all-DPI evidence.
