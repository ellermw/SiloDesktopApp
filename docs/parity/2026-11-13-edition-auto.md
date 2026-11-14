# Edition-scoped Auto selection - November 13, 2026

Upstream: Silo-Server/silo-server 7edef3ddc4cdd5c12f26c23a895b41f31782f286 (#4053).
Desktop base: e438c30d9e70915c2c3b2934dca6d3c09878d68c, Beta 1.2.1016-beta.1
(numeric 1.2.1015.1). Version stamps remain owned by the Beta pipeline.

Detail Auto now ranks actual first-part files within the selected edition. It
uses the quality cap (including native tier suffixes), ranks 8K/4320p, ignores
unknown resolutions when known files exist, chooses the closest above-cap file
when none fits, and breaks equal-resolution ties by HDR then primary audio codec.
Watch history selects the edition rather than pinning a file or a later part.
The original VersionRanking entry points retain card/next-up Resume behavior.

Edition options group same-key presentations, treating the empty Standard key as
a real choice. Edition selection clears a manual file pin and computes Auto.
The version menu exposes Auto plus explicitly selectable first-part files and
shows the active mode. A settled quality read initializes the edition once;
subsequent quality changes stay inside it. Failed initial settings reads remain
provisional. Navigation and identity changes retire selection authority; queued
quality responses carry navigation, request, revision and selection guards.

A download uses the selected presentation and one quality-ranked file for each
remaining part, preserving part order. Other callers of the download dialog
retain their original sorting. Audio/subtitle choices survive same-file repaints
and reset on a file or identity change. Provider-subtitle inventory responses
also check identity and revision so retired responses cannot restore old tracks.
Native source-version pins, Play from Start and single-file version-pill hiding
remain intact. Runtime summaries retain multipart total duration.

## Verification

- Before implementation, 8 of 10 new ranking cases failed against the old detail
  policy, covering cap/history, Standard, 8K, unknown resolution and later parts.
- New ranking/state tests cover provisional settings recovery, edition retention,
  manual-to-Auto changes, grouped presentations, multipart download order and
  separate card Resume behavior. Existing parity source assertions were updated
  to check the grouped-edition integration rather than the retired local variable.
- Full unit run: 6,103 passed, zero failed/skipped; 2.3822 minutes.
- Release publish and native regression-host build passed.
- Native oct24-track-choices passed: actual Auto and edition menu invocations,
  quality-cap updates in Standard, identity-scoped pins, same-file retention and
  changed-file reset of pre-play track choices, plus existing persistence cases.
- Native detail-action-hover passed: complete primary/progress paint, 12 label
  and progress combinations, hover/press/focus/accessibility and lifecycle states.

The original native extension first held an intent from before profile retirement;
the fixture now obtains the current owner before setting its synthetic quality.
Original failure text is retained. Initial publish type errors and the obsolete
source assertion were corrected before the final verification above.

Product DLL SHA256: 8033D5599A402C9B7175344475DD1712B9E94BAE2415B5FC2E3E1612C9293AAD.
Text evidence lives in D:/SiloPlayer-build/edition-auto-*.log and the two native
results directories. No installer was run and the installed player was untouched.
This does not certify live account/server interoperability or exhaustive visual
parity. The independent review found same-file inventory refresh and playback-return
track retirement defects; both are corrected and covered by native behavior checks.

## Review corrections and added coverage

Same-file watch refreshes now reload provider subtitle inventory without clearing
current choices. Returning from playback explicitly retires pre-play choices so
in-player preference changes are not overwritten on the next Play. Watch-detail
presentation checks navigation and request ownership across its async reads.
Provisional profile qualities are normalized (including hd/sd aliases); 360p
uses the closest above-cap file when needed. Auto and edition changes preserve
the catalog Resume action while /watch is pending. Empty file metadata uses a
file-ID summary instead of an empty separator. Settings-driven repaints are
limited to movie/episode detail surfaces, preserving hidden audiobook badges.

Native checks exercise the actual settings HTTP client with held responses:
failed read followed by recovery, stale revision, retired content and retired
profile. They also verify same-file downloaded inventory, same-file choice
retention, playback-return reset, pre-watch Resume, metadata-free summary and
audiobook badge visibility. Additional units cover alias/low-tier caps, multiple
above-cap files and an empty history edition key. The alias/360p cases reproduced
three failures before correction. Existing original failure logs are retained.
A fixture initially used settings instead of the API's items response envelope;
that fixture response was corrected without changing product deserialization.
## Storage

One existing build/publish/native payload was reused. Output is 1.226 GiB;
measured free C: 194.76 GiB, D: 480.98 GiB. Previous automatic approval review
blocked cleanup of generated test directories with "blocked by policy". Deletion
was not retried; recovered space is 0 GiB on C: and D:. Source, logs and earlier
review fixes remain preserved. Localization PR #145 is owned by the pipeline and
was not modified by this work.

Receipt: D:/SiloPlayer-build/edition-auto-delivery-receipt.json. All 1904 source/test/native inputs are frozen in D:/SiloPlayer-build/edition-auto-source-inputs.json (SHA256 042E45BFFA184928453635C519553123ECF8F906179AE93ADED2DCC71391ECE8). Publish, native reference and test-host product DLLs match.

