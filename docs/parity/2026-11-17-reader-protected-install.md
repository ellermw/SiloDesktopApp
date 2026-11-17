# Ebook/comic reader startup in protected installations

## Baseline and reproduced cause

New follow-up to the November 17 report and screenshot; this does not update or
reopen any previously submitted PR. Source base is Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`, version `1.2.1181-beta.1`,
numeric `1.2.1180.1`. Version stamping remains pipeline-owned.

The installed player runs unpackaged from Program Files. Reader startup called
`EnsureCoreWebView2Async()` without providing a writable browser data directory.
WebView2's unpackaged default can therefore try to create its profile beside
the executable in a protected installation. The native exception is converted
to the generic centered "Unable to open ebook" before any book is downloaded.
The same startup path serves ebooks and comics.

The initial synthetic EPUB/CBZ tests passed because their launcher supplied
`WEBVIEW2_USER_DATA_FOLDER` pointing to a writable fixture folder. That override
masked the installed-app condition. The lead reproduced the reported error by
removing the override and denying file/subdirectory creation on the owned test
host directory. Both real detail Read handoffs then failed with the exact
message, with `browserInitialized=False` and `downloaded=False` where recorded.
Neither the installed application nor its permissions/data were changed.

Microsoft documents that unpackaged WinUI 3 apps in protected install
directories must specify a custom user-data location:
https://learn.microsoft.com/microsoft-edge/webview2/concepts/user-data-folder

## Fix and regression coverage

The reader creates an explicit WebView2 environment using
`%LOCALAPPDATA%/SiloPlayer/reader-webview` before initializing the control.
`SILOPLAYER_READER_WEBVIEW_DIRECTORY` is a process-local isolation override used
by the native fixture. No installed settings, account credentials or browser
security policies are changed. Existing identity retirement checks remain.

`ReaderOpeningFixture` now defaults to the actual ebook detail Read action or
comic chapter-row Read action instead of direct reader navigation. It checks
the retained library scope, removes/restores the SDK folder override, isolates
the reader's browser directory, and checks the actual selected folder. Optional
protected-install mode proves that the default environment fails with
`UnauthorizedAccessException`, HRESULT `80070005` (`E_ACCESSDENIED`), while the
product's explicit environment opens the same book. Synthetic-only startup,
resource/MIME and module-failure diagnostics are retained in the fixture.

## Executed verification

Evidence root: `D:/SiloPlayer-build/books-reader-followup-20261117`.
The local `verify.ps1` uses a finite five-second `SiloOct18Build` acquisition,
bounded child processes, numeric exits, completion sentinels and finally-based
release. Protected mode changes only its owned host directory's create rights;
the original ACL is restored in `finally` and verified afterward.

- RED: installed Beta assembly and assets, protected host, ordinary EPUB through
  ebook detail Read: exit 1, "Loading state: Unable to open ebook".
  Receipt: `results/protected-install-red-epub/results.txt`.
- RED: installed Beta assembly and assets, protected host, CBZ through comic
  chapter-row Read: exit 1, same message, browser/download both false.
  Receipt: `results/protected-install-comic-red-cbz/results.txt`.
- Release publish and final native host build: numeric exit 0.
- GREEN: final source/payload, identical protected-host conditions, EPUB and CBZ
  actual detail Read: both exit 0 and native completion sentinel present.
  Receipts: `results/protected-install-epub-green-epub/` and
  `results/protected-install-comic-green-cbz/`.
- Both GREEN runs first demonstrate default-folder `E_ACCESSDENIED`, then verify
  explicit writable profile selection, rendered text/decoded comic images,
  script sandbox, progress saving, reconnect/access refresh survival and safe
  profile retirement.
- App publish/host DLL hashes match, and both production and fixture source
  SHA256 checksums occur in their corresponding final PDBs.
- `git diff --check` passes. Installed process 39668 remains open and untouched.

Final application DLL SHA256:
`F5067E0DF51E7B3A3381A75A6D01E43E3511FE6691B0CB4FB02BBC2EB3FBA8BE`.
Final native host DLL SHA256:
`4B4CBC6AE3FB2B8A3F19678BE928C3C5DA632DFAEDFF81462EE14B9A3BDF3835`.
Production document SHA256:
`3C4D17302DB95558841DE582DA3D73FC7DED266863E89DC76285176A794346DE`.
Fixture document SHA256:
`5A43132C7D413AFA2138E656D2292450CD0469BCD2C1E8160FD7B144CBDB2747`.

## Limits and ownership

The full unit run did not start: its finite mutex acquisition failed because
another runtime owner had the slot. No retry loop or further runtime followed.
Pipeline QA owns that broader verification. The final native runs use synthetic
books/services and equivalent protected-directory permissions, not the owner's
private book bytes or a restarted installed player. Other formats, plugin
WebViews and complete visual parity are not certified by this narrow fix.

The last owned runtime lease released at `2026-11-17T10:55:49.7653626Z`.
One reusable output root and current publish/host pair are retained. No old
outputs, evidence, source or denied cleanup targets were deleted. Submission
ends local ownership; the Beta pipeline handles review, fixes, QA and delivery.

Storage readback: the task output is 0.711 GiB. The conservative whole-tree
inventory includes 52.0566 GiB of source, history, evidence and generated files;
excluding 4,197,665,684 bytes of known static source and 1,078,386,538 bytes of
shared Git history leaves a 47.1428 GiB upper bound on generated output, below
the owner's 50 GiB cap. Free space is C 170.238 GiB / D 461.045 GiB. Source,
history and evidence were preserved; this accounting did not reclaim outputs.
