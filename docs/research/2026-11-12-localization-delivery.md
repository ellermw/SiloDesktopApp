# Desktop interface localization — November 12, 2026

## Scope and baseline

The remaining Server/WebUI alignment work applies the existing eight-language
catalog across the native shell, sign-in, browsing, personal lists, Requests,
Collections, Settings and editors, Reader, Watch Party and playback chrome.
Raw account data, saved names, query values and API identifiers remain data.
Retained pages and open controls update without replacing navigation state.

The integration includes Beta `a82fc9e5` (**1.2.1015-beta.1**, numeric
**1.2.1014.1**). Its #144 corrections to withdrawn automatic advances,
quoted-hour clock formats, bounded loading-cover fades and first-output
ordering are retained. Version stamps are inherited from the pipeline.
At the delivery check, 1015 is a source stamp; its release is not yet published.
The latest published Stable and Beta are 1.2.1007 and 1.2.1007-beta.1.
Earlier household contracts/Featured hero #143 and HUD/autoplay #144 are
already delivered dependencies, rather than new changes in this submission.

## Official source recheck

Official GitHub main was fetched directly and pinned to
`fb165fa931ea0f9e99b9a3a6f339af3322bc5524`. The latest observed successful
[Docker build 2214](https://github.com/Silo-Server/silo-server/actions/runs/43287131544)
uses `c5ce6b2dc2d7b679b465ea7325273849213c3e5f`.

The newer source commits are:

- `98033bd9`: scanner file-row preservation through rename/retitle, including
  authorization of prepared downloads. This changes server storage behavior;
  it adds no Desktop request or viewer control.
- `5f7bbe09`: WebUI administrator access-group membership tools, paging and
  request-policy confirmation. Server administration remains in the WebUI;
  Desktop has no access-group administration page or matching mutation path.
- `612a343b`: contributor guidance for message catalogs, translations and raw
  data. This changes no runtime contract or viewer interface.
- `fb165fa9`: metadata link-write fencing after a file moves. It changes
  server-internal scanner/matching persistence, with no new Desktop request.

The current WebUI metadata editor has no series Status field. The native
fixture's earlier expectation was removed after checking the pinned source.
API v1 retirement remains covered by the earlier audit: Desktop already uses v2.

## Review and integration corrections

Previous independent reviews identified translated API arguments, lost Webhook
drafts, profile defaults reset by relabeling, stale retained titles, rule-control
accessibility labels, interface/regional casing differences, and stale HUD text.
The lead integrated the corrections and retained the associated regression checks.

The resumed native run also reproduced a metadata-editor timing error:
translation controls used a visual Parent before WinUI assigned it. They now
use the editor's stored field-layout ownership, matching the existing ratings
insertion. Wide and narrow native translation cases exercise that path.

A separate native regression reproduced a message key replaced while its
control was detached, followed by reload in the same locale. The localization
registration incorrectly treated its old generation as already current.
Unloading now invalidates that presentation generation; open dialogs retain
their existing public open lifetime. The regression failed before this change
and passes afterwards, along with the open collection-dialog error-label case.

Fixture integration updates register the services required by real Settings
controls, look up translated rule accessibility names, and use a fresh editor
after permission revocation permanently retires its predecessor. The metadata
Save control is disabled on revocation; the person Save handler rejects the
write. Both behaviors retain no-write assertions.

## Verification and delivery

Final verification: 6,219/6,219 units, fresh Release publish and 23 native
groups on published app SHA-256
`A10DB7A2769098DDC0572EEC6D46CBB477C7E59091DDBF671986AA2E514CB687`.
The Collections scope label failed in the combined batch and passed an isolated
rerun with unchanged assertions; the cause is not established. This is recorded
as retry-passing, rather than claiming every check passed on its first run.
The final native fixture adds only diagnostics after the frozen full run.
No product input changed. The 2,009-input manifest and aggregate receipt are
`D:/SiloPlayer-build/{verified-source-inputs,verification-receipt}.json`.

Automatic approval review rejected both cleanup commands, including the
individual generated artifacts folder. They did not execute; recovered space
is 0 GiB on each drive, with about 1.1 GiB generated output still present.
Cleanup remains outstanding. Source, logs, screenshots and hashes are retained.

Native fixtures use synthetic accounts, settings, transport and media. They do
not certify live server/account persistence, phone or room interoperability,
every WebUI page/pixel, physical DPI/text scaling, or the full default native
suite's historical platform limitations. The installed player remains open.

## PR #145 review correction

The completed review reported one P1: localizing the subtitle appearance
Slider header threw during XAML construction. Added the missing
`Slider.HeaderProperty` mapping. A native Slider regression reproduced the
exception before the fix and passed after it, including retained-locale refresh
and preserving its numeric value. The subtitle-preferences native fixture also
registers current Settings language services and PlaybackApi; its initial setup
failures are retained separately from the subsequent passing run.

Follow-up verification is separate from the baseline above: all 6,219 unit tests
passed, plus localized-properties, subtitle-preferences (actual dialog, revision
18 and 19), and playback-localization native groups on the corrected publish.
See `D:/SiloPlayer-build/slider-review-receipt.json` for final source/payload
binding and logs. The earlier 23-group result is not a new run on this correction.
PR #145 still requires pipeline review, merge and Beta delivery.

## Integrated review follow-up candidate

Integrated pipeline c8f5fb28 and the local navigation/series fixes. Home and
library View All preserve title key, copied arguments and keyed-title snapshot;
Catalog resolves these on each locale change and keeps user-renamed titles raw.
The progressed legacy series label uses translated Play Next without changing
its playback target resolver. All 35 XAML inline localized ComboBoxes have owner
refresh calls and selection-event guards. Ordinary cached-owner reloads retain
the applied generation; detached key changes invalidate only their binding.
The cached-owner regression was observed red before the fix, then green while
the prior detached-key regression remained passing.

Current checks: Release publish, **6,220/6,220 units** (zero failed/skipped), and
seven native groups on the final product DLL: localized-properties,
media-localization, media-catalog-localization, oct20-home-rows,
settings-localization, calendar-culture, notification-series. All 2,008 frozen
implementation/verification inputs match. Evidence is bound separately in
`D:/SiloPlayer-build/combined-review-receipt.json`; earlier 23-group evidence
belongs to its original source. Initial expanded-fixture failures were resolved
by registering NotificationsApi and applying native templates before checking
closed selection displays on controls under collapsed panels.

The broader personal-lists overlay fixture remains failing: its initial setup
omits the request-status capability revision; a diagnostic revision-26 run
then hit an inner-badge alignment expectation. Diagnostic changes were restored
without changing product geometry or assertions. Failures are retained. Actual
Favorites/Watchlist inline choice localization passes within media-localization.
No full personal-lists/default-suite, live-interoperability or exhaustive visual
parity claim is made. Cleanup is still blocked by automatic approval, with
0 GiB reclaimed on either drive and about 1.08 GiB reused generated output.
The installed player remains open; PR #145 delivery is still pending.

## Regional picker, activation title and webhook review follow-up

Matched regional tags reuse shipped picker options without automatic writes;
unsupported tags remain explicit. The activation document-title map now uses
its catalog key. Webhook editor titles interpolate the original raw name and
refresh across de/ar/ja without changing the draft. A native normal-entry test
also reproduced missing SettingsGroupStyle when opening Settings →
Notifications; the control now owns the same brush/style values rather than
requiring its creator's resource scope. That test was red before the resource
fix and green afterwards.

The carousel's reported English metadata was overwritten immediately by its
existing localized presentation method. Removed the redundant assignments;
rendered numbered episode, runtime and raw title now have retained-language
coverage. This cleanup does not claim a previously visible carousel text bug.

Final follow-up checks: Release publish, 6,220 passing units (zero failed/skipped)
and settings-localization, shell-localization, media-localization native groups.
All 2,008 frozen inputs match. See `D:/SiloPlayer-build/review-third-receipt.json`.
Earlier broad receipts remain source-specific baselines; this run does not
re-certify the earlier seven/23 groups or the failing broad personal-lists
fixture. Pipeline delivery, live/physical visual limits and the approval-blocked
cleanup remain pending. The installed player was not restarted or installed.
