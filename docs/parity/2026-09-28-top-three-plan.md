# September 28 implementation: ranked packages 1–3

User instruction: “Knock out #1 through 3.” Implement Watch Party correctness,
reader progress compatibility and shared appearance using the existing desktop
subsystems. The September 27/28 audits under D:/SiloPlayer/docs/parity are the
requirements/evidence, with official upstream ad899be9d4fd9f33d4b9e9ac6873166026661c6d
freshly fetched for this work. Do not expand into other ranked packages.

## Design and execution

- Keep native libmpv and existing v3 playback. Watch Party requests go through
  permission-aware room intents; server-applied commands do not feed back as user
  requests. Reconnect reattaches the same session, lifecycle follows room state,
  and readiness/catch-up/stall handling preserves user playback preferences.
- Keep the existing WebView2 reader host, but use compatible document locations
  and a PDF renderer with observable page progress. Preserve legacy desktop
  bookmarks; resolve CFI and common fraction locations without silently jumping
  to chapter zero. Use maintained local renderer components where needed, with
  source provenance/licenses and no external scripts at reading time.
- Keep ThemeService as the native resource boundary. Cinema Dark is the base;
  supported shared server color tokens become native brushes with safe fallback.
  Remove obsolete profile theme editing from reachable UI, preserve accessibility
  and date/time preferences. Arbitrary browser CSS/admin editing stay in WebUI.

Implementation uses an isolated managed worktree. Separate domain agents own
non-overlapping implementation areas; root owns packaging/project integration and
combined verification. No installation, production writes, user playback control,
commit or push. Existing primary-checkout audit changes remain untouched.

## Tasks and ownership

1. Watch Party: coordinator, room view models, PlayerService/overlay/Lua transport
   integrations, room models/API and dedicated behavioral tests. Verify permitted
   and denied controls, no command echo, reconnect attachment, initial lobby versus
   playing-to-lobby, readiness at target, transient/sustained stalls, catch-up and
   quality offer/reload bounds with deterministic fixtures.
2. Readers: EbookReaderPage, package extraction, reader API/models/core helpers,
   local reader assets and dedicated tests/fixtures. Verify unequal chapter lengths,
   EPUB CFI/fraction/legacy bookmarks and PDF multi-page progress/bookmarks/resume,
   unknown/invalid locations, annotations and lifecycle cancellation.
3. Appearance: ThemeService, settings APIs/models/view model/page and a separate
   theme lifecycle helper if needed. Verify mapped colors and invalid/unsupported
   CSS fallback, no deprecated writes, server/profile switch/reset/failure behavior,
   accessibility/date-time retention and scaled native UI.
4. Integration: review all three against acceptance, run focused then full Release
   tests and native build, exercise fixture-based native smoke checks, fix issues,
   build version 1.1.106 local installer and record exact verification limits.

No shared production files between domains except project integration, owned by
root. Root applies required csproj/content changes. Agents report needed startup
hooks before changing App/MainWindow. Build/test processes are coordinated to avoid
shared output contention. Tests must exercise behavior, not just source strings.

## Progress / decisions

- Baseline: desktop 3488a4942ee0d03448bd609d04334e74b963bc7d (1.1.105).
- Ruling: the user selected concrete audited packages for implementation; proceed
  within that authorization without another approval round for routine design.
- Ruling: independent domain implementation follows the parallel-domain skill;
  root serializes integration/builds and reviews shared interfaces.
- Task 1: implemented; 18 focused checks and three two-client controller cases
  pass in the full suite. Independent source review completed.
- Task 2: implemented; 29 real Edge renderer checks pass. Review found and
  implementation corrected stale book-generation and annotation-context writes.
- Task 3: implemented; ten behavioral tests pass; independent Watch Party review
  completed by this domain's implementer. Root reviewed appearance integration.
- Task 4: full Release suite passes 1,172 tests, zero failed/skipped; six
  published playback-service checks pass. Native integration/artifact validation
  results are recorded in the combined implementation report.
