# Playback delta — September 30, 2026

Bounded source audit of official public Silo Server GitHub main:
`ad899be9d4fd9f33d4b9e9ac6873166026661c6d` →
`8e2e840474a085c6df6571a5a2850f7eb996810c` (50 commits fetched by the parent).
Upstream objects were read with exact-ref git commands in
`D:\SiloPlayer\.codex-tmp\silo-server-current`; no checkout/fetch was performed
by this audit. Desktop means the actual **uncommitted 1.1.106 candidate** in
`C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`, not the older
primary checkout. Read its top-three delivery and WatchParty implementation
records. No implementation, builds, tests, app control, production access or
private GitLab access. Existing test sources were inspected, not re-executed.

## New actionable desktop delta

**PB-30-1 — P2, small: subtitle text opacity and Gray selection.** Upstream #1652
adds `textOpacity`, an integer **1–100**, default **100**, independently from
background opacity. Rendering applies alpha to the text color; Gray is
`#9ca3af`. The editor uses typed percentages and commits edits on blur/Enter
and before closing with Escape. Primary sources:
[schema](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/contracts/settings/v1/schemas/subtitle-appearance.json#L32),
[default/contract revision](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/contracts/settings/v1/manifest.json#L453),
[palette, parsing and rendering](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/subtitleAppearance.ts#L79),
[editor behavior](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/settings/SubtitleAppearancePanelView.tsx#L63).

Candidate `src/SiloPlayer.Core/Models/Settings/SubtitleAppearance.cs` has no
TextOpacity in its model, parser, clone or explicit JSON writer. Thus a WebUI
opacity preference is ignored, and saving appearance from desktop loses that
field. `PlayerService.cs:779` sets fully opaque `sub-color`; the dialog palette
at `Controls/SubtitleAppearanceDialog.xaml.cs:77` lacks Gray. Both the
in-player appearance dialog and SettingsPage/SettingsViewModel expose only
background opacity. Gray already parses as a valid hex color; its missing
picker entry is the presentation gap.

Recommended scope: model/round-trip/clone, both native editing surfaces,
preview and live mpv text alpha; preserve independent background/outline
behavior. Match typed percentage semantics where native controls permit.
Acceptance: sparse old settings default to 100, valid 1/100 boundaries, invalid
values, profile/device save/reset, Gray round-trip, closing with an uncommitted
edit, and live subtitle text at differing background opacities. This is separate
from the completed server-color appearance package.

## WatchParty changes: covered candidate behavior plus new-server acceptance

| Upstream delta | Candidate evidence and disposition |
| --- | --- |
| #1673 host pause delivered by `state_report` | Server now updates `PlaybackState` and `ResumeOnReady` with `IsPaused` and emits the authoritative transport command, including delivery through another API server. [Service implementation](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/watchtogether/service.go#L866). Candidate `WatchPartySyncController.cs:261` already reports actual position/pause state, and `:313` applies play/pause commands through the separate native boundary. This is a server correction, not a new desktop wire contract. Accept system/admin pause that arrives only through a report; guest correction, buffering barrier and reconnect must stay paused. Test host resume and members on separate API servers as well. |
| #1657 command replay after socket renewal | WebUI clears the previous socket's transport command; server marks the old room command superseded after fresh member sync. [Web hook](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/player/hooks/useWatchTogetherRoomConnection.ts#L453), [server attach sync](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/watchtogether/service.go#L2370). Candidate `WatchPartySyncController.cs:103` clears pending/applied state on reconnect and pending state on disconnect, then reattaches the unchanged session. ViewModel emits commands as events rather than retaining a render-state command (`WatchTogetherViewModels.cs:706`), and awaits dispatcher delivery before reading another frame (`:606`). Existing `WatchPartySyncTests.cs:26` and `WatchPartyTwoClientTests.cs` cover reattachment and disconnected input. No source-confirmed need to reopen the package. Accept renewal with an old future command pending, changed room position during outage, profile authority replacement, and fresh targeted sync against the new server. |
| #1655 episode-end return to room | WebUI suppresses early post-roll inside rooms to avoid a black player page after lobby return. [Change and test](https://github.com/Silo-Server/silo-server/commit/c0fea4e779677063488b404b2227aee4b06331a9). Candidate `PlayerService.cs:3807` retains final reporting until room lifecycle changes; early post-roll and local next-episode paths are guarded by room authority (`:3886`, `:5501`). `WatchPartySyncController.cs:68` closes matching playback on playing→lobby; coordinator closes it while retaining room membership (`WatchTogetherCoordinator.cs:37`). Existing `WatchPartySyncTests.cs:54` covers that transition. Keep a real room episode-end acceptance check, including no next episode; do not duplicate completed implementation. |

The completed package's documented 1,172-test/native-fixture verification is
prior evidence, not a September 30 rerun or a live multi-device certification.
None of these upstream fixes closes the separate standalone signed-stream
404/buffering investigation.

## Bitrate and download delivery: server-owned changes

- **#1659 shared bitrate/resolution ladder and hardware caps:** the server now
  shares bitrate budgets across auto streaming and download presets, fits aspect
  ratio without enlargement, bounds decoders and source bitrate, and enforces
  ceilings on hardware encoders. Explicit streaming menu rungs retain their own
  table. [Authoritative ladder](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/docs/architecture/quality-ladder.md).
  Candidate `PlaybackManager.cs:281` consumes server `AvailableQualities` and
  native v3 capabilities retain `VideoEvidence = "declared"`; do not duplicate
  server algorithms or fabricate attested decoder facts. Accept cropped/high
  frame-rate sources, low bandwidth/source bitrate, policy limits, effective
  dimensions/codec and HEVC fallback on the updated server.
- **#1630 version preference:** when no explicit file is supplied, server download
  creation picks the profile's last-played version, then series similarity, then
  resolution/id fallback. [Selection contract](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/docs/downloads-api.md#L311).
  Desktop request already supports explicit `media_file_id`. No new required
  field; a future version-specific create UI should send the shown file.
- **#1681 prepared multi-track downloads:** server keeps every audio track,
  carries plain-text subtitles inside MP4 and advertises ASS/PGS sidecars;
  legacy artifacts retain their true old inventory.
  [Prepared file/manifest contract](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/docs/downloads-api.md#L134).
  Candidate currently saves managed file bytes via HEAD/GET file-proxy
  (`DownloadsPage.xaml.cs:267`, `DownloadsApi.cs:48`). It has no manifest/offline
  library importer, subtitle asset fetch or download-create quality UI. The
  file saver inherits multitrack bytes; it does **not** thereby gain offline ASS/
  PGS sidecar preservation. Treat sidecar import as part of an explicitly scoped
  future offline-download package, not a newly broken existing importer.
- **#1648/#1647 artwork dependency errors and sendfile:** artwork now answers
  503 with Retry-After: 5 on retryable storage failures; file routes preserve
  sendfile. [Delivery behavior](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/apiv2/download_delivery.go#L100),
  [retry contract](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/docs/downloads-api.md#L625).
  No candidate artwork importer consumes that route. Keep file save/range/error
  acceptance; no standalone native artwork-retry implementation is established.

New `quality_options` metadata is absent from `DownloadCapability`, while
`quality_presets` remains compatible. [Capability contract](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/docs/downloads-api.md#L256).
Because this candidate offers no download-create preset selector and upstream
only adds the typed data shape in this window, this is future download UI work,
not evidence the current file saver needs a new ladder.

## Carried work and recommended sequence

Readers: no reader implementation/contract delta was identified in this
50-commit window. Keep the completed reader package's existing format and live
interoperability acceptance boundary; do not restore it as unfinished code work.

The earlier live-marker issue remains present in this candidate:
`PlayerService.cs:6900` applies legacy ranges but does not adopt `marker_segments`
or resend them to Lua; `SendMarkersToOsc` still has only its startup caller at
`:2267`. Carry the [September 28 marker package](2026-09-28-playback-delta.md),
including empty/withdrawn arrays. Subtitle translation source labels still use
language/origin alone at `SubtitleAiDialog.xaml.cs:123`; carry that small polish
item. Profile seek/intro preferences, SRT negotiation and real first-frame
telemetry remain their earlier packages; this delta does not certify them.

Sequence within playback: (1) keep standalone buffering diagnosis a separate
highest-impact reliability track; (2) fix the carried live-marker integration;
(3) add subtitle opacity/Gray with translation-source polish as one bounded
subtitle-settings change; (4) perform targeted new-server WatchParty/quality/
download acceptance during candidate acceptance. Keep completed WatchParty,
reader and shared-color implementation removed from the coding queue unless
acceptance reveals a concrete defect. The wider product ranking belongs to the
parent consolidated report.
