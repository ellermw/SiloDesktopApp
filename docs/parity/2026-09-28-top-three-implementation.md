# September 28 packages 1–3 — local build 1.1.106

Implements the user's selected first three packages from the
[ranked list](2026-09-28-ranked-work.md). Official upstream was fetched directly
from public GitHub main at `ad899be9d4fd9f33d4b9e9ac6873166026661c6d`.
Desktop base is `3488a4942ee0d03448bd609d04334e74b963bc7d` (1.1.105).
Work is isolated on `codex/parity-top-three`; the primary checkout's audit edits
were preserved and copied forward as documentation.

## Delivered behavior

1. **Watch Party:** permission-aware user intents, separate server commands with
   no echo, same-session reconnect attachment, selection ownership, target-aware
   readiness, buffering grace, bounded catch-up, lower-quality offers, and room
   completion. Default mpv shortcuts must respect room authority. Standalone
   transport remains independent. See the [domain record](2026-09-28-watchparty-implementation.md)
   and [independent review](2026-09-28-watchparty-review.md).
2. **Readers:** local EPUB/PDF rendering with shared CFI locations, size-weighted
   EPUB fractions, PDF page progress, bookmarks, annotations, legacy location
   recovery, explicit invalid-location handling, and book/profile lifecycle
   guards. Books cannot execute scripts or fetch external resources. See the
   [domain record](2026-09-28-reader-implementation.md) and
   [independent review](2026-09-28-reader-review.md).
3. **Appearance:** server-wide supported color tokens update native resources,
   with Cinema Dark fallback and stale-response rejection. Removed obsolete
   profile theme editing and shell theme dots; retained accessibility and
   date/time settings. High contrast has precedence. See the
   [domain record](2026-09-28-appearance-implementation.md).

## Verification boundaries

Automated fixtures use isolated settings, local documents and simulated HTTP or
room traffic. They do not certify a live two-device party, real remux decoding
across server failure, every supported book, every-page visual parity, or HDR and
audio hardware. Arbitrary server CSS is intentionally not interpreted by WinUI;
native mapping supports current shared hex color tokens. Administration stays
in the WebUI.

The separate recurring standalone buffering investigation remains open. No
installed app, user playback or production infrastructure was changed, and this
candidate has not been committed, pushed or published.

## Integrated verification and artifact

- **1,172 Release tests passed, zero failed/skipped**, including the real bundled
  mpv input-routing test, 13 native direct-streaming cases, ten shared-appearance
  cases and three two-client room-relay scenarios. Result:
  `.codex-tmp/test-results/top-three-final.trx`.
- **Native WinUI regression runner passed (exit 0)** against the published
  1.1.106 binaries. All 29 renderer checks passed in WebView2. The actual
  `EbookReaderPage` restored EPUB legacy locations and PDF page 3, saved shared
  CFI progress, created matching bookmarks, and rejected stale-generation events
  and profile writes. Actual coordinator/player attachment, reconnect, intents,
  no-echo and standalone isolation passed. Shared color resources, focus colors,
  high contrast, text scale, date/time persistence and wide/narrow controls passed.
  Existing Home/artwork/calendar/Account/subtitle/watched regressions also passed.
  Evidence: `.codex-tmp/native-artwork-tests/2177e344e776473cbda20a3d06a9ddda/results.txt`.
- Visually inspected `appearance-wide.png` and
  `appearance-narrow-highcontrast.png` in that result directory. Visible controls
  fit, narrow choices stack and long descriptions wrap; the settings page scrolls.
- **Six published PlayerService checks passed** after the final publish:
  signed direct delivery, remux timeline, authenticated delivery and bounded
  startup/recovery routes. Log: `.codex-tmp/playback-tests-final.log`.
- Clean publish succeeded; a subsequent final publish included the native-test
  resource repair and obsolete-field cleanup with no warnings in its log.
  All **234 reader asset files** match the published copies by SHA-256.
  Published assembly file version is **1.1.106.0**. `git diff --check` is clean.
- Inno Setup compiled the final candidate successfully. Installer:
  `D:\SiloPlayer\installer\output\SiloInstaller-1.1.106-Setup.exe`
  (169,381,659 bytes). The worktree also retains an identical copy under
  `installer/output`; SHA-256:
  `A44DE70031BC0A6C338C5118F1B9CEE8B76F8620E814D349EE283732C3E2323F`.
  It remains unsigned, following the existing packaging policy.

## Integration findings resolved

- Reader review identified stale host messages/snapshots and context-unpinned
  annotation creation. Both were repaired and exercised by the actual-page
  fixture. Review findings are closed for that covered behavior.
- Native construction exposed an existing undefined `DividerBrush` reference in
  `EbookReaderPage.xaml`; replaced it with the existing `BorderBrush`. The actual
  page then opened and completed EPUB/PDF save/restore checks.
- Room review found default mpv shortcuts could bypass explicit room intents.
  They are disabled only during room authority and restored afterward; forced
  OSC handlers remain enabled and tested with bundled mpv.
- Fixture setup corrections were kept separate from product fixes: complete
  isolated settings dependencies, a nonactivated off-screen native window for
  WebView2 initialization, bounded waits, and fresh-document navigation when
  changing virtual book mappings. No user's app was closed or controlled.
- Fresh-worktree libmpv initially contained a Git LFS pointer. The existing
  full binary was verified against its tracked hash and materialized unchanged;
  no new native binary is introduced by this work.
