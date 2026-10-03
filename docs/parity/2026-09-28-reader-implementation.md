# Reader progress compatibility — September 28, 2026

Scope: ranked package 2 / PB-C4. Desktop baseline
`3488a4942ee0d03448bd609d04334e74b963bc7d`; authoritative public Silo Server
reference `ad899be9d4fd9f33d4b9e9ac6873166026661c6d`, inspected with `git show`
from the parent's freshly fetched official GitHub reference. No private legacy
source, live API, credentials, installed-user playback or production changes.

## Implementation

- EPUB and PDF use the exact WebUI Foliate renderer and PDF.js 6.2.108, bundled
  locally under `Assets/Reader`. Dependency provenance, hashes, licenses and the
  three small embedding/security adaptations are recorded in
  `Assets/Reader/THIRD-PARTY-NOTICES.md`.
- EPUB progress/bookmarks save CFI; selected highlights save CFI ranges in both
  `location` and `cfi_range`. Whole-book fraction navigation uses Foliate's section
  byte sizes, including unequal chapter lengths and non-linear spine sections.
  `progress` follows WebUI `progressFromRelocate`: `(current + 1) / total`, capped
  at one. It is deliberately not an independently invented chapter/page ratio.
- Legacy `chapter:N;fraction:F` is accepted and resolved through extracted chapter
  paths into the original spine. Existing highlight text remains displayed via
  an in-memory CFI overlay without rewriting its persisted legacy location.
  Legacy PDF's single Document location restores its original beginning. FB2,
  CBZ and CBR retain their existing renderer/extraction path.
- PDF pages are observable renderer sections. Page controls, progress text,
  bookmarks, progress saves and resume use the common renderer and shared CFI
  semantics (for example page three is `epubcfi(/6/6)`).
- Unknown/invalid locations produce an error and do not save over the old
  position. Explicit chapter/slider navigation recovers. Malformed `fraction:`
  locations fall back to valid numeric saved progress, matching WebUI.
- EPUB search, text-to-speech, settings, annotations and native toolbar actions
  now address the renderer's content documents. PDF supports common search and
  selected-text annotations in addition to page progress.
- Shell generations prevent canceled opens from resurrecting state. A separate
  native file generation is included in trusted bridge messages; stale document
  messages and late snapshot results cannot mutate the replacement file.
  Progress and annotation creation bind requests to the originating API/profile
  context; departed/stale page preference saves are suppressed.
- EPUB frames are script-disabled. The trusted shell has a restrictive CSP;
  WebView2 additionally denies external resources, permissions and new windows.
  Raw book data is mapped at a separate origin that cannot send trusted host
  messages. PDF scripting/eval remain disabled.

## Verification and reproducibility

`node tests/reader-fixtures/locations.test.mjs` passes the location contract
checks. It initially failed with the expected missing parser assertion before
implementation.

`node tests/reader-fixtures/make-fixtures.mjs` reproducibly creates an EPUB with
a short opening and 100-paragraph long chapter, plus a four-page PDF.

`node tests/reader-fixtures/run-browser.mjs` runs the actual shipped modules in
a separate headless Edge profile against a local fixture-only server. Latest run
passes 29 checks: unequal-length fraction navigation, CFI exact text-node resume,
selection CFI range, both-direction annotation navigation with an independent
WebUI-compatible renderer, legacy bookmarks/highlights, unknown/out-of-range
rejection, malformed fraction fallback, explicit invalid-restore recovery,
close/open cancellation, script/network denial, and PDF multi-page progress,
bookmark and reopen. Recorded values:

```text
EPUB: epubcfi(/6/4!/4,/102[p50]/1:79,/106[p52]/1:304)
Selection: epubcfi(/6/4!/4/102[p50],/1:13,/1:35)
PDF page 3: epubcfi(/6/6)
PDF progress: 0.6666666666666666 (unchanged WebUI location formula)
```

`ReaderInteropFixture` runs the same script in native WinUI WebView2 with the
actual virtual-host/resource policy and isolated test-result user data. It also
constructs the real `EbookReaderPage` with isolated fixture APIs and verifies the
page's message bridge, save and bookmark request bodies, EPUB/PDF restore,
stale-generation message rejection, and profile-change write rejection. The
parent coordinated native compilation/execution; the final suite exited 0.

The native raw WebView2 run passed all 29 renderer checks. Its fixture window is
shown without activation offscreen because a never-loaded hidden WinUI window
cannot initialize WebView2; environment/control startup is bounded and logs each
stage. Constructing the actual page then exposed a pre-existing unresolved XAML
`DividerBrush` resource. Integration replaced it with the defined `BorderBrush`.
The real-page format-switch fixture was corrected to reload the shell after
changing its virtual-host mapping, exactly as production `OpenVersionAsync` does.
WebView2 keeps mappings for the lifetime of the current document.

Final native evidence:
`.codex-tmp/native-artwork-tests/2177e344e776473cbda20a3d06a9ddda/results.txt`.
All 29 renderer checks passed, followed by actual `EbookReaderPage` EPUB legacy
restore and CFI progress/bookmark saves; PDF page-three CFI restore and
progress/bookmark saves; old-generation bridge message rejection; and stale
profile progress/bookmark denial. Test execution used isolated fixture APIs,
book/cache files and WebView2 user data under the test result directory.

`EbookReaderInteropTests` covers v2 request CFI preservation, string `file_id`,
event timestamp, stale-profile progress rejection and raw-book origin policy.
The parent owns full Release test/build and installer verification. The initial
full Release run reported 1169 passed; final combined build/test/installer
evidence is recorded in the parent integration report.

## Limits

Fixtures prove renderer and host behavior with deterministic local documents;
they are not a live two-client server round trip or a visual-parity certification.
No production libraries, credentials or reader state were accessed. Arbitrary
third-party EPUB/PDF layouts, encrypted PDFs, font packages and complex image
decoders still require representative user-file acceptance. Existing formats
outside EPUB/PDF were preserved rather than claimed newly interoperable.
