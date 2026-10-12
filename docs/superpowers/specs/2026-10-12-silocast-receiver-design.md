# Windows SiloCast Receiver Design

Status: approved by the user. Implementation is being migrated to Windows 1.2.243 using the complete 1.2.242 candidate baseline. The earlier 1.1.106 SiloCast artifact used a stale checkout and is obsolete. Actual phone acceptance remains pending.

## Intended result

An existing Silo iPhone or Android app discovers Silo for Windows on the local network, launches selected movies or episodes on Windows, and controls native libmpv playback. The phone supplies its verified profile through the existing server handoff protocol. The Windows saved login, selected browsing profile, and credentials remain intact.

The user requested SiloCast support following discussion of Windows HTPC use. This design covers the receiver and playback integration. TV Mode, directional browsing from the phone, remote OS wake, and a Windows controller for other TVs are outside this change.

## Official references

Fetched directly from official GitHub main on October 12, 2026:

- Server: `fae012b75d8c9f7511ef3f3ca82fefb2e54782b9`.
- Apple: `860bb6f7d8f536744006854f69005b6ff6a2f241`.
- Android: `726684df7f4c023cc99ea3abab3629fa4dfe9421`.

Protocol and receiver sources:

- [Apple message models and command names](https://github.com/Silo-Server/silo-apple/blob/860bb6f7d8f536744006854f69005b6ff6a2f241/iosApp/iosApp/Control/SiloControlProtocol.swift).
- [Apple TLS transport](https://github.com/Silo-Server/silo-apple/blob/860bb6f7d8f536744006854f69005b6ff6a2f241/iosApp/iosApp/Control/SiloControlSession.swift).
- [Apple framing](https://github.com/Silo-Server/silo-apple/blob/860bb6f7d8f536744006854f69005b6ff6a2f241/iosApp/iosApp/Networking/LAN/SiloFrame.swift).
- [Apple receiver lifecycle](https://github.com/Silo-Server/silo-apple/blob/860bb6f7d8f536744006854f69005b6ff6a2f241/iosApp/iosApp/Control/tvOS/TVControlReceiver.swift).
- [Apple temporary identity handoff](https://github.com/Silo-Server/silo-apple/blob/860bb6f7d8f536744006854f69005b6ff6a2f241/iosApp/iosApp/Control/tvOS/RemotePlaybackIdentityManager.swift).
- [Apple URL registry identity](https://github.com/Silo-Server/silo-apple/blob/860bb6f7d8f536744006854f69005b6ff6a2f241/iosApp/iosApp/Networking/ServerRegistry.swift).
- [Android TLS interoperability](https://github.com/Silo-Server/silo-android/blob/726684df7f4c023cc99ea3abab3629fa4dfe9421/android-shared/src/androidMain/kotlin/org/siloserver/silo/common/lan/SiloCastTls.kt).
- [Server device handoff contract](https://github.com/Silo-Server/silo-server/blob/fae012b75d8c9f7511ef3f3ca82fefb2e54782b9/internal/apiv2/device_login.go).

## User experience

Add an off-by-default toggle under Settings > Desktop playback: **Allow SiloCast control**. Supporting text: **Use Silo on your phone to play and control videos on this PC. Keep both devices on the same local network.**

When enabled, advertise the machine's existing device name and stable device ID while the app is running and has a configured server. Continue listening when minimized. Show receiver status and the connected controller name in this settings section. Offer **Disconnect phone** while connected. The UI reports listener or discovery failures without blocking ordinary playback.

Remote launches enter fullscreen after the native playback surface is ready. Ordinary desktop launches keep their existing behavior. A denied, canceled, invalid, or expired handoff leaves current playback untouched. A valid replacement launch closes the previous media session under its original authority before switching.

Disabling SiloCast withdraws discovery, cancels pending handoffs, sends a bounded graceful close, and ends any temporary remote playback session. Closing remote playback ends its temporary session; subsequent local playback uses the desktop identity. A dropped phone connection ends control but lets already-authorized playback continue until the user stops it or the temporary session expires. Reconnection requires a fresh connection handshake; stale messages cannot regain ownership.

Expose a remote-session banner/notice with the controller and profile names and a local stop/disconnect action. Do not navigate the desktop's browsing pages into the temporary profile.

## Architecture and approach

Use managed Bouncy Castle TLS with Win32 DNS-SD registration. Bouncy Castle provides a C# PSK server API; the Android implementation supplies the transport interoperability reference. Windows `DnsServiceRegister` supports desktop service advertising without requiring a separate Bonjour installation. Keep the TLS dependency in the transport boundary and pin its exact verified version in the implementation plan.

Alternatives considered:

- Native OpenSSL transport: compatible in principle, but adds native binaries and lifetime/interoperability code to each deployment architecture.
- Server playback WebSocket alone: useful for existing active-session administration, but cannot satisfy the phones' LAN discovery and idle launch protocol.

The managed approach minimizes packaging changes and keeps the protocol independently testable. Actual iPhone TLS and DNS-SD interoperability is a release requirement, not a conclusion drawn from unit tests.

### Components

| Component | Responsibility |
| --- | --- |
| `SiloPlayer.Core/Services/SiloCast/SiloCastProtocol` | Exact camelCase JSON models, message parsing, and command validation |
| `SiloPlayer.Core/Services/SiloCast/SiloCastFrameCodec` | Four-byte big-endian length framing, partial reads, frame limits |
| `SiloPlayer.Core/Services/SiloCast/SiloCastSession` | Serialized outbound messages, heartbeat, bounded TLS connection lifetime |
| `SiloPlayer/Services/SiloCast/SiloCastTlsTransport` | Accepted TCP connection and Bouncy Castle TLS-PSK adapter |
| `SiloPlayer/Services/SiloCast/SiloCastAdvertisement` | Win32 service registration, TXT updates, and network/interface changes |
| `SiloPlayer.Core/Services/SiloCast/SiloCastHandoff` | Isolated server approval polling and temporary identity lifecycle |
| `SiloPlayer/Services/SiloCast/SiloCastReceiverService` | Listener lifecycle, controller ownership, handoff/launch/control state machine |
| `SiloPlayer/Services/SiloCast/SiloCastPlayerAdapter` | Dispatcher-safe player operations and live state/track mapping |
| `SiloPlayer/Services/PlaybackAuthorityScope` | Local or temporary playback API/auth dependencies captured for a media session |

Register the receiver with app services. Integrate listener shutdown with window/application shutdown, network changes, setting changes, server changes, and logout. Keep transport and approval work off the WinUI thread. Use dispatcher marshalling only for player and UI state changes.

## Discovery and wire compatibility

- Advertise `_silocast._tcp.local` with a dynamically bound TCP port and TXT fields `v`, `name`, `id`, `server`, `serverName`, `playing`, and verified `serverIdentity` when available. The phone expects receiver role `tv` even though Windows is the platform.
- Use protocol version 2. Advertise `supportedVersions: [2]`; reject peers without version 2 explicitly. There is no need to support legacy v1 launches.
- `server`/`serverId` uses base64url UTF-8 of the Apple-normalized URL (trim surrounding whitespace and trailing slashes), without padding. Keep this compatibility identity separate from desktop credential URL normalization.
- Use the same fixed protocol PSK and PSK identity as the official apps. Support TLS 1.2 `TLS_PSK_WITH_AES_128_GCM_SHA256` and TLS 1.3 `TLS_AES_128_GCM_SHA256` external PSK. Supply a fresh key buffer for every handshake. Complete the handshake with a 10-second deadline and promptly send receiver hello.
- TLS here supplies interoperability and opportunistic encryption, not unique controller authentication. Server-verified profile handoff supplies authority for launching remote content. Do not label a matching hello/server ID as verified account identity.
- Frames are four-byte big-endian unsigned byte lengths plus UTF-8 JSON, limited to 1 MiB. Reject malformed JSON, oversized lengths, and incomplete frames at EOF. Unknown control names produce an error/ignored command without tearing down an otherwise valid connection.
- Preserve all required state fields, including `supportsHDRToggle: false` for older v2 decoders. Use separate camelCase JSON options; the desktop API client's snake_case defaults are unsuitable.
- Send ping every three seconds and detect three missed pong responses. Only pong refreshes heartbeat response accounting. Require valid phone hello within five seconds.
- One active controller owns commands. Bound candidate handshakes so incomplete connections cannot displace an authorized controller. Validate a replacement hello before retiring the previous connection. Every callback and handoff is fenced by connection and playback generation.
- Publish state on important changes and at most every 500 ms while connected. Coalesce redundant state updates; bound outgoing queues so an unread phone cannot accumulate memory or stall playback.

## Server handoff and playback authority

Use a dedicated API client and AuthService without a credential store for temporary remote playback. Do not call the persisted local login's `SetTokens`, `ConfigureServer`, or `SelectProfile` with phone credentials.

1. Validate handoff request ID, profile ID, HTTP(S) server origin, and the offered URL's round-trip registry key. Never treat display names as authority.
2. When an offered deployment identity is present, verify it against the server identity endpoint before accepting an alternative offered endpoint. Without it, use the offered server URL exactly. Send no local desktop bearer credential to offered URLs, redirected origins, or candidate endpoints.
3. Check `/api/v2/auth/device/capability` for remote playback handoff and protocol 2.
4. Start `/api/v2/auth/device/start` with `client_purpose: remote_playback`, `temporary: true`, and the Windows device identity.
5. Send the correlated `handoff_challenge` with server-issued user/match codes. The phone performs approval; no manually entered PIN is invented by Windows.
6. Poll `/api/v2/auth/device/poll` at the server-prescribed interval until approval, denial, cancellation, or expiry. Require a temporary response, the exact offered profile, valid tokens, profile proof when needed, and a future bounded session expiry.
7. Capture the approved scope, then close outgoing playback under its original scope. Publish `handoff_ready` only while the same controller and request still own the handoff.
8. Accept launch only after matching approved handoff; map content/file/audio/subtitle/resume fields to the existing `PlayerService.PlayAsync` path. Reuse an unexpired scope only for the same controller, server, and profile.

`PlayerService` currently holds singleton catalog, playback, auth, and API dependencies; a media session needs a captured authority scope. Route watch detail, item metadata, preferences, quality/audio/subtitle replans, stream token access, control WebSocket tickets, progress, next episode, and final stop through that scope. Browsing services continue using the local scope. Clear/fence watch-detail prefetches at scope changes. Local Play takes over by closing temporary playback before using local credentials.

Refresh access tokens proactively in memory only, bounded by the temporary session's absolute expiry. Stop temporary playback on expiry or revoked authority, then dispose its auth/HTTP objects and release tokens. Pending approval failure must not cancel current media. Credentials, profile proofs, device codes, and raw frames never go into logs or repo files.

## Playback commands and state

Implement `play`, `pause`, `play_pause`, `seek`, `stop`, `select_audio_track`, `select_subtitle_track`, `set_playback_speed`, `set_quality`, `set_video_gravity`, `set_subtitle_sync_ms`, `set_subtitle_position`, `set_volume`, `set_muted`, and `play_next` against existing player behavior.

Reject nonfinite numbers, unavailable track/quality choices, and missing command arguments. Clamp valid seek/volume/delay inputs to existing player limits. Preserve explicit subtitle-off and automatic subtitle-selection semantics. Phone track IDs represent advertised native player track choices; translate them explicitly to server track indexes when a replan is needed. Never assume these index spaces are identical.

State includes current content/session/title, position/duration, playing/loading/buffering flags, advertised audio/subtitle choices, selected IDs, quality options and switching status, speed, aspect, HDR status, supported aspect/subtitle controls, volume/mute, next episode availability, and user-readable errors. Advertise only actionable options. HDR is reported; no remote HDR toggle is introduced.

## Network and packaging behavior

Listen/advertise only on eligible local interfaces; avoid VPN/virtual/public interfaces by default. Reconcile Wi-Fi/Ethernet changes without duplicate advertisements or connection leaks. Treat Windows firewall blocking as a visible setup condition. Never disable the firewall, create a public-network rule, or require admin rights for ordinary browsing/playback. Verify the shipped installer and portable publish behavior; include private-network application rules only through explicit installer opt-in.

The receiver exists only while Silo for Windows is running. It does not wake the PC, start a closed app, or make a receiver reachable across an isolated guest Wi-Fi/VLAN.

## Verification and acceptance

Automated tests must cover fragmented/coalesced frames, 1 MiB limits, invalid and unknown commands, exact Apple/Android JSON fixtures, TLS handshake failure and repeated reconnects, PSK buffer reuse, handshake/hello deadlines, heartbeat timeout, bounded queues, stale-controller messages, replaced/canceled handoffs, denial and expiry, exact profile proof, and local credential preservation.

Playback integration tests cover remote launch/resume, audio/subtitle ID translation, progress and stop under the temporary profile, next episode authority, local takeover, access refresh/expiry, fullscreen timing, and unchanged ordinary playback/session WebSocket behavior. DNS-SD tests cover native callback lifetime, registration failure, unregister, and network changes.

Build core/native regression targets and publish Windows x64 Release. Inspect settings layout in the running test build without closing the user's existing app. Before claiming phone interoperability, test actual iPhone and Android discovery, launch, pause/resume, seek, tracks, volume, quality, reconnect and disconnect with a Windows receiver on the same LAN. Record app versions and equipment. If phones are unavailable, report that specific acceptance step as unverified.

No commit, push, release, or production server change is included in the build request; preserve existing README edits and untracked audit artifacts.
