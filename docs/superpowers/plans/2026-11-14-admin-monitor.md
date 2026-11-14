# Admin Monitor parity implementation — November 14, 2026

Owner scope: restore Admin in the sidebar footer above Profile, acting-admin only; implement Monitor Dashboard, Activity and all Logs subsections; restore global Server Activity with exact WebUI visibility. Match current official WebUI appearance and behavior, preserving explicit no-slide preference. No installed-app modification.

Baseline: desktop Beta 8f0808361faf2f6a4923f28c11dc2f22173a06a7, version1.2.1017-beta.1 unchanged. Official source1a99f7beffcea3850f067df000c319f2704fd23b, read Git objects in C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-f1f2c73e-c918e95b35dce2ac/.codex-tmp/upstream-review-2026-11-12/silo-server. Audit by Map WebUI Admin Monitor screens and navigation parity provides layout/state/behavior oracle; full source remains authoritative.

## Architecture and file ownership

Use native WinUI3 pages and Core HTTP/socket/state services; never embed the WebUI. Existing AuthorizationPolicy.IsActingAdmin, SiloApiClient request contexts and EventChannelClient supply authority/transport boundaries. Every async read/action belongs to its captured account/profile/server/access epoch; retire on access loss, profile/account change, navigation or unload. Never show previous authority's data. Use bounded page/poll/socket lifetimes, no catalog preload.

- Shared integration (lead): AdminMonitorApi.cs constructor; App DI; explicit Monitor architecture/installer allowlist; shared styles/helpers if needed; integration and final docs. Class namespace SiloPlayer.Core.Api; partial class AdminMonitorApi(SiloApiClient client). Partial files can use primary-constructor `client`. Page-owned typed models stay in Models/Admin. Keep method names prefixed by surface when collisions would arise.
- Shell/indicator/Activity writer: MainWindow admin footer/header wiring, AdminShellPage, AdminActivityPage, ServerActivityButton, its API partials/models/state, required tests/native fixtures. Shell owns route enum and navigation. Final pages namespace SiloPlayer.Views.Admin: AdminDashboardPage, AdminActivityPage, AdminLogsPage, AdminDiagnosticsPage, AdminLoggingSettingsPage. Use those routes; do not create placeholder implementations for others' pages. Constructor patterns use App.GetService<T>() like current pages.
- Logs writer: those three Logs pages plus API/log websocket services/models/viewmodels/tests; no shell or App edits. Server Log, Client Diagnostics, Logging Settings exact upstream state/actions including ticket handshake and bounded live/history entries, filters, details, diagnostics collection/download/delete, one-batch settings save/unsaved/restart behavior.
- Dashboard writer: AdminDashboardPage plus widgets/grid/ranges/server layout persistence/customization/API/models/tests; no shell or App edits. All28 widgets, including15defaults and13Add-only options, actual charts and range behavior. ETag layout revision/debounce/save-blocked handling. Match responsive native layout and keyboard customization.

Each writer gets separate isolated worktree at Beta8f080836, produces uncommitted exact diff/receipt, never pushes/commits/PRs. Lead integrates after inspection. API partial owned filenames AdminMonitorApi.Activity.cs, .Indicator.cs, .Logs.cs, .Dashboard.cs. Shared root constructor supplied by lead; writers may add identical root for standalone compilation but exclude it from patch. Avoid broad resurrection of removed admin surfaces. Removed d3aeac5b^ code is structural reference only; v1 APIs obsolete.

## Required tests and verification

- Acting-admin gate, unresolved/nonprimary profiles, access loss mid-read/action, late retired completions and socket closure. No live-account fixtures.
- Footer location; global icon idle main-hidden/admin-visible, active count/methods/99+/500+lower-bound, four-second connection dot, popover sections/kept-open-on-empty, route-close, hidden player overlays/shown mini, narrow ordering.
- Activity filters/session controls and truthful capabilities, message/IP/FFmpeg states.
- Logs streaming/ticket cancellation/reconnect/history bounds/filter/detail/playback-summary/audit; diagnostics status/toggle ownership/filter/details/delete/download; logging load/dirty/advanced/save/restart/unsaved navigation.
- Dashboard all widget data/empty/error/ranges plus grid breakpoints, add/remove/reorder/resize/reset/persist and ETag conflict.
- Compare pinned WebUI and native synthetic fixtures at1440x900,1280x800,1024x768,1023x768,800x900; loading/empty/error/populated/interaction states. Synthetic comparison does not prove live parity. Existing Beta lacks localization foundation: preserve localization-ready source keys and avoid importing unrelated unreleased foundation; reconcile with localization PR dependency before claiming locale parity.
- Before any build check C and D >=50GiB, total active outputs<=10GiB. Shared reusable D:/SiloPlayer-build payload, serialized publish/native only by lead. Preserve earlier evidence. Automatic cleanup rejection remains; no retry/bypass. Unit verification by workers only with bounded reusable output and mutual exclusion; report exact source/test scope.

## Delivery

Independent review final integrated source, fix evidenced findings, rerun applicable final-source units/native/visual checks. Split coherent PRs at existing seams if200-file/250-commit intake limits approached. Qualifying non-draft same-repo Beta PRs only after verification; exact upstream/source hashes, evidence and limits in docs/current-development.md. Push then immediately link PR; pipeline owns review/fix/merge/package thereafter.

## Review focus

Never infer admin authority from cached role alone; never expose private data after revocation. Avoid new subscription per indicator instance; admin channels app-lifetime gated shared ownership. Preserve fullscreen/mini playback and existing main-shell geometry. Bound log/dashboard buffers and coalesce realtime refresh. Do not ship dead navigation to out-of-scope admin pages. Do not mistake functionally implemented controls for visual parity.

## Final shared authority contract (assigned to shell writer)

Core.Services.AdminMonitorAuthority(SiloApiClient client, AuthService auth) : IDisposable. Exposes bool IsAvailable; MonitorAuthorityLease Capture(CancellationToken ct=default); bool IsCurrent(MonitorAuthorityLease lease); event Action? AvailabilityChanged; void Retire(). Shell writer owns this service, tests and connection to AccessChanged; lead registers singleton. It retires all leases on role/profile/account/API-context changes, including same-identity loss/regain. Benign token refresh is not retirement.
Core.Services.MonitorAuthorityLease : IDisposable. Exposes ApiRequestContext Context; string AccountId; long Epoch; CancellationToken Token; bool IsCurrent; void ThrowIfRetired(). Captured-before-send and after-response checks required, also inside dispatcher/file publication. Callers capture from shared authority; no elevated detached authority. Pages/clients may take lease and CT. Non-overlapping API partials use primary-constructor client for context-bound requests. No automatic mutation/ticket retry.

Each writer may copy root AdminMonitorApi.cs and the eventual shared authority files into its isolated checkout for compilation; exclude those copies from delivered diff. Shell worker owns authority implementation. Other writers proceed with agreed interface and test compilation after it arrives; no homemade incompatible replacement.
