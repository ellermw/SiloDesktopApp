# Issue8 — protected-profile login investigation

GitHub report: [#8, Logiin](https://github.com/ellermw/SiloDesktopApp/issues/8), posted October15,2026. The reporter says protected profiles fail while unprotected profiles work. They provided no application version, server revision, error text or logs. This investigation therefore distinguishes an independently reproduced shipped defect from confirmation of the reporter's exact environment.

## Concrete historical failure

The actual frozen released1.2.247 assembly (`installer/publish-1.2.247-final/SiloPlayer.dll`, SHA256 `4C20B72CA226983D1DC0BA4E562C8362ED9A8CDC58C3E848E346FAAF6D547B6C`) fails in `ProfileSelectPage.ShowPinDialogAsync` before displaying the PIN dialog or sending a verification request. A tiny isolated WinUI host loads this exact assembly and invokes the actual method. It produces `System.ArgumentException`, HRESULT `0x80070057`: the input scope is unsupported on `PasswordBox`. Verification HTTP request count is zero.

The old dialog initializer sets `InputScopeNameValue.Number`. WinUI's password input requires a supported password/PIN scope; `NumericPin` works. Unprotected profiles never construct this dialog, which explains the asymmetric failure in this reproduced case. It is a desktop UI failure before authentication, not a password/PIN encoding requirement or server PIN refusal.

Source comparison: desktop commit `1536209` (release1.2.247) contains the unsupported value. Commit `f41b56524c101305c4c1f13fdb28a1857d4a959c` (release1.2.268, October16) replaced it with `NumericPin`. Released1.2.286 retains that correction. The issue predates268; its missing version prevents asserting that the reporter used247 or that their particular environment is resolved.

## Current native API and verification

Official Silo main was freshly fetched for this focused comparison: `174fb9a44f02bd020d1f62f287609a911c7ed388`. Exact Git objects were inspected because the reference checkout's working-tree HEAD is historical. The current WebUI uses a separate `POST /api/v2/profiles/{id}/verify-pin` with a `pin` body. An accepted response supplies `valid` and `profile_token`; subsequent profile reads use `X-Profile-Id` and `X-Profile-Token`. The account password stays in account authentication. No Jellyfin-compatible password/PIN concatenation is required or added. This was a focused authentication comparison, not a new full upstream parity pass.

The released286 assembly (SHA256 `C579544272A3EB2CE62DF9FAC71782CAAA03F601535FE0894BCFAD9B62A54118`) passes actual native tests:

- Protected primary and secondary profile: wrong PIN displays rejection without selecting a profile; correct separate PIN activates the profile and closes the dialog on navigation.
- Cancel while verification is pending: a late successful response cannot activate or navigate, and the picker remains reusable.
- Full shell path: real profile card and dialog, original `OnProfileSelected`, real `MainWindow.TryEnterAuthenticatedPage`, real `HomePage` and authenticated navigation. One verification request; issued proof authorizes subsequent reads with zero refused reads. This covers the shell destination deliberately substituted by the smaller dialog fixtures.
- Controlled replay of the exact old `Number` initializer throws the same native exception, while the supported current initializer proceeds successfully in the same host.

New persistent regression coverage: `ProtectedProfileLoginTests`, `ProtectedProfileIssue8NativeFixture` and `ProtectedProfileShellNativeFixture`. The first pair of unit tests checks actual ViewModel/API serialization and valid/invalid token handling. The native tests exercise the actual published controls, HTTP boundary and page lifecycle, rather than only inspecting source strings. Test servers use synthetic data; native windows are offscreen. The installed app and real account/profile data remain unchanged.

Fresh complete main suite: **2,007 passed, zero failures/skips**, recorded in `.codex-tmp/issue8-oct21/main-results/issue8-main.trx`. Current native host build succeeded with17 existing warnings; the isolated legacy host built with zero warnings/errors. All939 non-test release inputs and the approved installer remain unchanged. Only regression tests, their native selectors and investigation documentation were added locally.

## Artifact and disposition

The existing owner-approved1.2.286 installer already contains the working repair:

- `D:\SiloPlayer\installer\output\SiloInstaller-1.2.286-Setup.exe`
- SHA256 `1FEF85B4F680658D27D7573B2237BCD37CBF8133163753F3CC878A4025A5F624`

No new product patch, version bump or installer rebuild is justified by this reproduction. Retest with this exact installer before treating the report as resolved. If the reporter still fails on286, obtain its exception/HTTP diagnostic and app/server versions; the current reproduction does not identify another failure. No issue reply/closure, source commit or GitHub push was performed during this investigation.

Evidence: `.codex-tmp/issue8-oct21`, including actual legacy failure, current primary/secondary/cancellation runs and the fresh main-suite result. Full-shell result: `.codex-tmp/upstream-oct20/issue8-shell`. Machine evidence summaries are preserved with these directories; historical release manifests and installer bytes remain frozen.
