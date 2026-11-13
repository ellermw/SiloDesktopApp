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

## Second review follow-up candidate

Prepared locally on pipeline head `a7c4393409d46d819e2d9768c050bcab44df7205`
while the pipeline owns its next fix round. The duplicate progressed-series
comments identify one issue: the fallback now uses the catalog's translated
Play Next action without changing the playback target resolver. Home and library
View All routes carry the original title key, arguments and keyed-title snapshot.
The catalog resolves these again on each locale refresh; user-renamed titles
retain their raw text. Navigation takes independent argument copies.

Candidate checks: successful Release publish, 9 focused unit tests, and passing
`media-catalog-localization` and `oct20-home-rows` native groups. The new catalog
case verifies title/window refresh in German, Arabic and Japanese, Back, raw
renamed titles, and the progressed-series label. Actual Home/library buttons
verify complete title metadata and independent arguments. Initial fixture-only
failures came from applying personal-list subtitle/sort expectations to section
catalogs; those assertions remain in place for personal-list routes.

This candidate is not yet pushed: wait for the pipeline's active fix round, then
integrate its commit and verify the combined source. The remaining dropdown
finding must be checked across owners after that integration. Existing broad
verification receipts do not certify this newer source. Receipt:
`D:/SiloPlayer-build/review-followup-receipt.json`.
