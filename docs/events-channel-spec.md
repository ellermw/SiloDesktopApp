# Continuum Events Channel — WebSocket Protocol Spec

**Source of truth:** `F:\continuum-server\internal\events\types.go`, `F:\continuum-server\internal\api\handlers\events_ws.go`, `F:\continuum-server\internal\api\router.go`, `F:\continuum-server\web\src\components\RealtimeEventsProvider.tsx`

This document describes the server's real-time event channel. The desktop Continuum Player should subscribe for features that need live updates (history_import progress, admin jobs, sessions, scans, tasks, user_state).

## Connection

- **URL:** `wss://{host}/api/v1/events/ws?token={access_token}` (wss for https, ws for http)
- **Auth:** Access token passed as `?token=` query param (WebSocket can't send Authorization headers). Same pattern as HLS stream URLs.
- **Framework:** Go `gorilla/websocket` upgrader on chi router. Registered at `router.go:862` inside the authenticated group.

## Handshake — required sequence

1. **Client connects** via standard WebSocket upgrade.
2. **Server sends `hello`** (schema v1) listing channels the caller is allowed to subscribe to:
   ```json
   {
     "type": "hello",
     "schema_version": 1,
     "connection_id": "01JABC...",
     "available_channels": ["catalog", "history_import", "user_state", "jobs", "sessions", "tasks", "scans"],
     "required_action": "subscribe"
   }
   ```
   Admin role gets all seven channels; non-admin gets `catalog`, `history_import`, `user_state` only.
3. **Client MUST send `subscribe` within 5 seconds** or the server closes with `ClosePolicyViolation`:
   ```json
   {
     "type": "subscribe",
     "request_id": "any-client-generated-id",
     "channels": ["history_import"]
   }
   ```
4. **Server replies with `subscribed`** acknowledging the accepted set:
   ```json
   {
     "type": "subscribed",
     "request_id": "any-client-generated-id",
     "channels": ["history_import"],
     "rejected": [{"channel": "jobs", "code": "forbidden", "message": "Admin access required"}]
   }
   ```
5. **Server sends an initial `snapshot` per accepted channel**, then streams live events.

## Frame formats

### `snapshot`

One per subscribed channel, immediately after `subscribed`. For `history_import` the data is `[]Run` of currently-active runs for the calling user (or all active runs if admin).

```json
{
  "type": "snapshot",
  "channel": "history_import",
  "timestamp": "2026-04-10T21:00:00.000Z",
  "data": [ { /* Run */ }, ... ]
}
```

### `event`

Streamed as server-side things happen. Filtered server-side by `UserID` and `AdminOnly` so the client sees only events it's allowed to see.

```json
{
  "type": "event",
  "channel": "history_import",
  "event": "history_import.updated",
  "event_id": "ulid",
  "timestamp": "2026-04-10T21:00:00.000Z",
  "data": { /* Run */ }
}
```

### `error`

```json
{
  "type": "error",
  "code": "bad_request" | "internal_error" | "forbidden",
  "message": "human-readable"
}
```

## Channels

Constants in `events/types.go`:

| Channel | Who sees it | Notes |
|---|---|---|
| `catalog` | all users | item/library changes |
| `history_import` | authenticated users (own runs), admins (all runs) | this doc's focus |
| `user_state` | authenticated users (self only) | progress, favorite, watchlist, history, watched |
| `jobs` | admin only | admin job updates |
| `sessions` | admin only | active playback sessions; `sessions.replaced` event triggers a fresh snapshot server-side |
| `tasks` | admin only | scheduled task runs |
| `scans` | admin only | library scan progress |

## History Import events

Defined at `events/publishers.go:90-103`. Every event carries the full `Run` struct as `data`.

| Event | Fires when | Throttled? |
|---|---|---|
| `history_import.created` | new run queued | immediate |
| `history_import.updated` | any state change (progress counters, etc.) | max once per 250ms per run |
| `history_import.completed` | run succeeded | immediate |
| `history_import.failed` | run errored | immediate |
| `history_import.cancelled` | user cancelled | immediate |

Publishers.go uses a shared `HistoryImportObserver` with a `map[runID] lastSentAt` so rapid progress updates collapse to a single frame per 250ms window.

## `Run` payload shape

From `historyimport/types.go:135-155`:

```csharp
class Run {
    string Id;
    int UserId;
    string ProfileId;
    string SourceType;        // "emby" | "jellyfin" | "plex"
    string ConnectionMode;    // "connect" | "custom" | "predefined" | "plex_oauth" | "admin_token"
    string Status;            // "queued" | "running" | "completed" | "failed" | "cancelled"
    int? MappingId;
    int Fetched;
    int Matched;
    int Unmatched;
    int ProgressUpdated;
    int HistoryCreated;
    int Skipped;
    List<string> Warnings;
    List<UnmatchedSample> UnmatchedSamples;
    string ErrorMessage;      // omitempty
    DateTime CreatedAt;
    DateTime? StartedAt;
    DateTime? CompletedAt;
}

class UnmatchedSample {
    string Kind;   // "movie" | "series" | "episode"
    string Title;
    int Year;      // omitempty
    string Reason;
}
```

## REST surface for History Imports

All under `/api/v1` (authenticated, same Bearer token as everything else):

| Method | Path | Body | Returns |
|---|---|---|---|
| `GET` | `/history-imports/sources` | — | `Source[]` (admin-configured servers) |
| `POST` | `/history-imports/emby-connect/login` | `{username, password}` | `ConnectSessionLoginResult` |
| `POST` | `/history-imports/plex/auth/pin` | — | `PlexPinResponse` |
| `POST` | `/history-imports/plex/auth/check` | `{session_id}` | `PlexCheckResponse` |
| `GET` | `/history-imports/runs` | — | `Run[]` (bare array) |
| `POST` | `/history-imports/runs` | `CreateRunInput` | `Run` |
| `GET` | `/history-imports/runs/{id}` | — | `Run` |

`CreateRunInput` supports all three sources via optional fields (`historyimport/types.go:194-210`):

- **Emby Connect:** `{profile_id, source:"emby", connect_session_id, server_id}`
- **Emby Saved:** `{profile_id, source:"emby", source_id, username, password}`
- **Jellyfin Direct:** `{profile_id, source:"jellyfin", jellyfin_base_url, jellyfin_username, jellyfin_password}`
- **Plex OAuth:** `{profile_id, source:"plex", plex_session_id, plex_server_id}`
- **Plex Saved:** `{profile_id, source:"plex", plex_base_url, plex_token}` (or via `source_id`)

## Client implementation notes (for the desktop C# `EventChannelClient`)

1. Use `System.Net.WebSockets.ClientWebSocket` (already in the trimmer roots per `csproj`).
2. Build the URL from `_apiClient.BaseUrl` (replace `https://` → `wss://`, `http://` → `ws://`) and append `?token=` from `_authService.AccessToken`.
3. After `ConnectAsync`, await the server's `hello` frame before sending anything.
4. Send the `subscribe` frame with the requested channel(s) inside 5 seconds.
5. Parse incoming frames by `type`, dispatch to per-channel subscribers.
6. Heartbeat: WebSocket ping/pong is handled automatically — the server starts a ping loop via `startWebSocketPingLoop`.
7. Reconnect with exponential backoff on close (matches `RealtimeEventsProvider.tsx` behavior).
8. On token refresh (via `AuthService`), close and reconnect with the new token — WebSocket URLs bake the token in at connection time.
9. Handle `token` rotation in URL param, not in headers (WebSocket upgrade can't change headers mid-connection).
