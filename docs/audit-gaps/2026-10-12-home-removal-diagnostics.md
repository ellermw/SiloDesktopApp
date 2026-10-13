# Home removal error investigation — local 1.2.244 candidate

The owner reports an intermittent Object reference error toast after Next Up → three dots → Drop show. The item disappears despite the error. South Park is a concrete example; the failure is not show-specific. Do not ask the owner to remove additional real shows to test this.

## Official reference

Fetched directly from `https://github.com/Silo-Server/silo-server` main on October 12, 2026: `f095e0cadfd5a23ae3963269f86985362f56e2a2`. Use this FETCH_HEAD rather than the stale working-tree HEAD in `.codex-tmp/silo-server-current`.

`web/src/components/MediaItemMenu.tsx` uses **Remove from Next Up** and surface-specific Continue Watching/Listening/Reading labels. `web/src/hooks/queries/homeDismissals.ts` still sends the series identity to the v2 dismissal route, removes episode/series entries from both progress rows, and confirms **Removed from Continue Watching and Next Up** with Undo. The desktop now uses those current labels and confirmations, preserving the existing v2 operation and both-row Undo.

The old Drop show wording was present in official upstream commit `5e49cc8d376d2aaa1a7b4601ff876895f8d82efe`; desktop history first contains it in `854a277`. Its historical origin does not justify the missed current-source comparison.

## Established defects and repairs

- Caught card-menu failures previously passed only `ex.Message` to the toast service, discarding the exception stack. An isolated native messenger failure after successful HTTP removal demonstrated this logging gap. The repaired action records HTTP timing/status, the stage reached, whether the server accepted removal, and the complete exception chain.
- Every error-toast entry point now persists its displayed text and caller. Caught exception call sites pass the exception itself, preserving type, HRESULT, stack, nested/aggregate causes and API status when available. Logging occurs even without a registered toast container.
- The queued toast callback previously dereferenced the mutable service container. The callback now retains its original container and skips it if it was unregistered/replaced. A queued-unregister lifecycle fixture passes on the candidate. This defect is not established as the cause of the owner's intermittent removal error.
- Home dispatcher failures and previously silent dismissal/Undo catches retain exception diagnostics. Fatal and unobserved-task diagnostics use the same bounded logger. Fatal logging excludes arbitrary Exception.Data values.

## Automatic evidence

Files in `%LOCALAPPDATA%\SiloPlayer`:

- `errors.txt`: displayed error text, app version, UTC time, caller file/method/line, exception chain/stack/HRESULT, API status and recent Home UI breadcrumbs.
- `home_actions.txt`: removal stage and whether HTTP removal succeeded before a local failure.
- `api_diagnostics.txt`: fixed route category, HTTP method, attempt, status, elapsed header/transport time and numeric context generations. Home removal attempts carry the same random action ID as their UI/error records; token-refresh retry is distinguished as attempt 2.
- `crash.txt`: fatal error chain and Home breadcrumbs.

These diagnostics are always enabled locally; no monitor or manual marking is required. Each file uses the existing 2 MiB rotation threshold and two archives. Complete URLs, credentials, named secrets and exception Data are excluded; HTTP headers, bodies and media/account identifiers are not written by the new request diagnostics. Successful routine API reads are not logged. Disk-write failures do not replace the application error.

## Evidence and remaining uncertainty

Evidence is retained in `.codex-tmp/home-dismissal-oct12`. Frozen 1.2.243 normal removal/Undo fixtures passed, including 30 cycles and scrolling. Therefore they did not reproduce the owner's intermittent exception. The injected failure is a diagnostic test, not a reproduction of its root cause. The baseline label test failed and the caught-stack test failed before repair; both corresponding candidate checks pass. The baseline toast-unregister fixture terminated with a native stowed exception without a managed result log, so that run does not identify the user's failure either.

No existing retained log or bounded observer capture identified the offending method for the owner's actual occurrence. Its root cause remains **unproven**. Do not declare it fixed from the labeling, logging or lifecycle changes. New automatic records preserve the evidence needed to distinguish API rejection, Undo snapshot, surface recipient/UI update and toast rendering failures if it recurs during normal use. No real shows were removed by this investigation.

The installed 1.2.243 app remains open and unchanged. The candidate requires user installation before its built-in diagnostics can capture installed-app failures. This work is uncommitted and unpublished.
