# Desktop application updates

## Intent and authorization

The owner requested update notifications, a notification-only/automatic choice, release notes inside the app, and a conspicuous green indicator. They authorized implementation, chose background download followed by installation on normal app exit, and clarified that both modes must always notify and allow manual updating before automatic installation. Existing playback must remain stable and uninterrupted. The audit PR24–29 batch is already merged into main8c1b238; this feature stays local for an owner-test installer before publication.

## Behavior

- Notification-only is the default. Both modes check published stable GitHub releases asynchronously after startup and every six hours. A source push alone does not notify users; publishing a release does.
- Both modes show a green top-right success notification with a View update action and a persistent green Update Available entry. The persistent entry remains after dismissing a toast/dialog and can always reopen release notes. The profile menu also exposes Check for updates when there is no newer release.
- The update dialog shows installed/new versions, publication date and release notes as readable text, a mode selector, Check for updates, download progress, Update Now and Later. Release text is data, not executable HTML. Settings exposes the same preferences/actions in a separate desktop-updates card.
- Automatic mode downloads in the background while playback is inactive. Starting playback cancels/defer the automatic transfer; partial bytes are retained for safe range resume. Both modes permit an explicit manual download/update. Manual installation uses the normal close path and offers clear consent if playback is active. Automatic installation happens only during normal app exit, after receiver and playback shutdown.
- A downloaded package is reverified before scheduling installation. The small helper waits for the original application process to exit; it never kills playback or another process. It launches the existing installer with no reboot/no force-close arguments. Windows administrator approval can still appear because the existing installer writes Program Files. Automatic exit updates do not reopen the app. Update Now can restart after success as the original unelevated user.
- Errors are logged with stage, version, HTTP status/bytes/hash outcome and failure category in updates diagnostics. Automatic-check failures do not spam toasts or affect Silo authentication/navigation. Manual errors are actionable. Interrupted/canceled installation preserves the staged update and is reported on next launch.

## Approach and decisions

Retain the existing Inno installer and public GitHub release pipeline. This is the implementation choice: it avoids a new updater service, account/login, installer migration or framework. A separate updater framework would require a packaging/signing migration; replacing the installer with MSIX would change distribution and installation policy. Neither is needed for the requested behavior.

Use a focused Core update service for release selection, bounded HTTP downloads, digest verification and staging. Use a MainWindow partial for native notifications/dialogs/lifecycle, a SettingsPage partial for the desktop card, and a fixed packaged PowerShell exit helper copied to the update staging directory. The helper stays outside the application directory while the installer replaces it and can restart the app without inheriting installer elevation. It does not accept arbitrary executable names or shell commands.

## Trust and limits

- Fixed repository: ellermw/SiloDesktopApp. No Silo server/account credentials are used for release requests.
- Only public, non-draft, non-prerelease releases with a valid newer numeric version and the exact Windows-x64 installer asset are eligible. Require GitHub's SHA256 digest, uploaded state, HTTPS release URL under the fixed repository/tag, and bounded metadata/installer sizes. Reject malformed, mismatched, truncated or oversized content. Download into a partial file and promote only after size/hash verification.
- Validate cached metadata again and bind partial bytes to the expected digest/size. Reject reparse points/path traversal; stage and cleanup only known generated files under this user's Silo update directory.
- Exit helper rechecks size/hash and parent process identity. It invokes the fixed installer with structured/quoted arguments, waits for success and restarts only the known installed Silo executable when requested. It cannot suppress Windows UAC. No privileged resident service is introduced.
- A per-install global handoff gate blocks new instances during installation. If another instance exists or cannot be inspected, installation is deferred without closing it. The normal uncanceled close path qualifies; crash and Windows session shutdown/logoff do not. Only a registered installed directory is eligible, never a development/portable folder.
- Persist an atomic attempt/result journal and distinguish UAC cancellation, deferred/busy, launch failure, installer failure, reboot-required and installed-version verification. The existing installer replaces its payload; do not promise rollback. Retain staged installers for retry/repair. A failed/withdrawn metadata check or switching automatic mode off disarms automatic installation; manual consent remains separate.
- New installer version will be1.2.334: six audit groups plus the updater feature after published1.2.327. Frozen earlier installers and the running installed player remain untouched. Do not publish this feature before the owner's test/approval.

## Verification

Core tests cover release filtering/version comparison/trust boundaries; HTTP failure, oversized/truncated streams and bad hashes; partial range resume and canceled transfers; cached package revalidation; notification-only defaults, always-notify/manual availability and playback deferral. Test the exit-helper verification path with synthetic files without launching an installer. Native fixtures verify actual green notification/action/dialog, preferences, readable release notes, narrow/1080p/ultrawide/4K layout and close scheduling with an injected noninstalling launcher. Run the appropriate full tests, WinUI publish and published/native playback regressions; seal a new installer and receipt for owner testing.

References: [GitHub release REST contract](https://docs.github.com/en/rest/releases/releases), [Inno command-line options](https://jrsoftware.org/ishelp/topic_setupcmdline.htm). The release contract supplies notes, stable/draft flags, asset size, download URL and digest. Inno supports silent setup and explicit no-reboot/no-force-close behavior.
