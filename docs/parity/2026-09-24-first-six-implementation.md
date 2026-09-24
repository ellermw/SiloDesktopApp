# First six finalization packages — 1.1.104

The user selected the six smallest packages from the reordered list. This build
also retains the previously verified 1.1.103 watched-state repair. Implementation
verification did not perform installation, live password changes, history imports
or downloads. The user subsequently authorized publication of this same installer
as GitHub release 1.1.104, with the README version, download link and checksum updated.

Official Silo Server `main` was fetched again before implementation and remained
`d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`. The reference checkout is
`.codex-tmp/silo-server-audit-d4e35ba9`. This does not identify the deployed server version.

## Changes and verification scope

1. **Visual fixes:** Calendar uses one interactive week navigator outside the
   scrolling content, with a spacer and a sticky position. Day selection leaves
   the heading below the navigator. Playback Info uses width-budgeted fixed-pitch
   rows, stacking/wrapping long values without truncating UTF-8 text. The panel
   remains scrollable and clips content away from its heading.
2. **Subtitle providers:** The shared dialog reads provider availability on each
   opening. Only explicit disabled status hides online search; failed or missing
   status fails open. Upload remains available. Requests and focus updates are
   guarded against closure and server/profile changes.
3. **Person refresh:** Follow the upstream observer's three-second polling for
   thirty seconds, then thirty-second backoff while the page remains open.
   Refresh metadata/photo without replacing loaded filmography. Cancel on departure,
   reject stale identities, preserve existing data on transient failure and support
   opaque as well as numeric refresh receipt IDs.
4. **Downloads:** Show save/delete failures and distinguish cancellation. Use HEAD
   metadata for a sanitized filename before opening the destination picker, then
   fetch a fresh body. Stage bytes beside the destination, validate HTTP status and
   content length, and replace the destination only on complete transfer. Remove
   staging files on failure; report any failed cleanup. Unknown containers use `.bin`.
5. **History imports:** Clear only the successfully consumed Emby Connect session,
   server selection and password; preserve retry state on failure. Skipped items
   remain a separate metric and are not added twice to processed progress.
6. **Account password:** Add the Account settings flow with server capability
   restrictions, current/new/confirm fields, Unicode character and UTF-8 byte limits,
   pending/error/success states and retries. Bind API writes to the originating
   authentication/profile context. Clear secrets on success and departure.

The first red run demonstrated silent delete errors, incorrect filenames and
unsafe transfer failure behavior, disabled-on-missing subtitle status, and missing
delayed person updates. A separate real WinUI calendar run against the prior build
scrolled its navigator to Y=-569 at a 700-pixel offset; the repaired native test
passes at narrow and desktop widths. Native UI fixtures use isolated HTTP handlers
and hidden windows, never the installed player or its settings.

## Evidence

The final complete Release suite passed **1,137 tests**, zero failed or skipped.
Native Account/history fixtures caught deferred PasswordChanged
events erasing pending input. The corrected field-specific synchronization and
success-only import clearing pass the real controls, including failure and retry.

- `.codex-tmp/parity-six-red.txt`: initial focused failure run.
- `.codex-tmp/parity-six-native-red.txt`: old calendar layout reproduction.
- `.codex-tmp/parity-six-suite-final.txt`: final complete Release regression result.
- `.codex-tmp/parity-six-publish.txt`: Release x64 publication result.
- `.codex-tmp/parity-six-native.txt`: native control acceptance results.
- `.codex-tmp/subtitle-focus-native.txt`: eight native subtitle cases plus calendar,
  account/import, watched-action and artwork checks, all passing after the final
  declarative tab-order change. Delayed status does not steal keyboard focus.
- `.codex-tmp/parity-six-published-checks.txt`: six published playback-service checks.
- `.codex-tmp/parity-six-package.txt`: clean build, native library hash/load checks
  and installer compilation.
- `tests/SiloPlayer.Tests/TestResults/parity-six-1.1.104-final.trx`: machine-readable suite result.

Final clean package was verified with the full native fixture suite and all six
published playback-service checks. The packaging script verified the bundled
libmpv hash/native loader and forbidden-resource exclusions. Inno Setup succeeded.

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.104-Setup.exe`
(167,601,253 bytes). File version: `1.1.104.0`.
SHA-256: `0E54B19C01707B0C4C864752E7667C87FFCB22F6204B3A9079E64B9E0EA3D6AB`.
The stable local alias has the same hash; a `.sha256` sidecar is alongside the installer.

## Remaining live verification

The implementation is tested against controlled responses. Real provider job
completion, native OS file-picker interactions, a real server password change and
Emby Connect import remain installed acceptance checks. Windows' existing save
picker can create an empty destination placeholder; cancellation/failure explicitly
identifies a remaining empty file instead of silently presenting it as a download.
It is not deleted automatically because it might have existed before selection.

These changes do not close whole-app visual parity, native hardware verification,
Watch Party, reader synchronization or the separate recurring-buffering investigation.
Packages 7–11 from the user's reordered list remain unimplemented in this pass.
