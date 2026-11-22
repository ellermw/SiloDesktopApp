# Test stability, second pass — November 22, 2026

Scope: five checks that failed for reasons unrelated to the change under test, so that the automated pipeline retried or rejected good pull requests.
Branch `t3/test-stability-2` (from Beta `fb74de97c`). Lane `D:/SiloPlayer-build/admin-completion-lane-lead`, run labels `stab2-*`.
No assertion was deleted or loosened, no blind sleep or blanket retry was added. One item (E) was a product defect and is fixed in the product; one more
(B) documents a product static that tests must not be the first to touch. All values are synthetic.

## A. Native group `plugin-cutover` — "Plugin fixture timed out waiting for the real WebView request"

**Cause: the machine's overlay hook crashes the web view's browser process (test environment, not product, not page).**
The Crashpad report of the failing run (`admin-completion-lane-b/results/stab-lead-r1-plugin-cutover/plugin-webview/EBWebView/Crashpad/reports/533cc336-….dmp`) was read
without a debugger (a small script over the minidump's exception and module streams): the process image is `…\EdgeWebView\Application\157.0.4341.60\msedgewebview2.exe`
(the browser process), the exception is `0xC0000005` at `0x1801490AF`, which lies in `C:\Program Files (x86)\RivaTuner Statistics Server\RTSSHooks64.dll` at
offset `+0x1490AF` — the same module and offset as the nine reports recorded for `admin-plugin-apps` (see `native-test-stability.md`). The fixture had already passed its
checks up to "…main navigation stay blocked" and then waited for the form post (`PluginCutoverNativeFixture.cs`, `Until` for `server.FormWrites == 1`) on a web view whose
browser process was gone. Ten reports are on file for these two groups: this one (read here) and the nine of `native-test-stability.md` (read by the earlier pass); all name that module. No other group's web view folder that is still on disk holds a report
(`reader-webview` ×4, `reader-page-webview` ×2, `comic-webview` ×2, `reader-language-webview` ×1: none).

Fix (test only): new shared helper `tests/SiloPlayer.NativeRegressionTests/WebViewBrowserLossWatch.cs` (a new file; `AdminPluginAppsNativeFixture.cs`, which the open
pull request owns, is not touched and keeps its own private copy). The watch attaches to the control (`AttachWhenReady`, also before the browser exists), records whether a
module named `RTSSHooks*` is loaded in that browser process, and keeps the process handle. `WebViewBrowserLossWatch.RunAsync` runs a part of a fixture that takes everything
from scratch, and starts it again (at most three starts in all, logged as a `NOTE:` line) only when the part failed **and** the hook was loaded **and** that process ended
with exit code `0xC0000005` (waited for, bounded to 10 s, on the process handle itself, because a crashing process stays alive while its report is written; the control's
own `ProcessFailed` notification counts too). Anything else — no hook, another exit code, a process that is still running — rethrows unchanged. `plugin-cutover` now builds
its servers, account, frame and web view data folder inside one start (`RunOnceAsync`; starts after the first use `plugin-webview-2`, `-3`), so a restart is a clean run.

Other native fixtures that open a real WebView2, and whether they needed the guard: `AdminPluginAppsNativeFixture` (guarded by the open pull request, not changed here);
`PluginCutoverNativeFixture` (guarded here); `ReaderInteropFixture`, `ReaderOpeningFixture`, `ReaderPartyLanguageNativeFixture`, `SvgComicSecurityNativeFixture`
(no loss recorded, no report in any retained web view folder: **not guarded**; they use the helper's pieces only if a loss is ever seen). `Program.cs` mentions WebView2 only in a comment.

Evidence: dump facts above. `stab2-final-r1` (below) passed `plugin-cutover` (7 checks). **Not established:** the restart path itself was not exercised on this
machine in my runs (no crash occurred); it cannot be provoked without changing the RivaTuner setup, which was not touched. Whether a user running RivaTuner can hit the
crash on a plugin page is not known; the product reacts as designed (load-failed state with Reload).

## B. Eleven unit tests with Arabic decimal separators (`directconn-final-r1`)

**Cause: a process-wide static that a test thread's culture can seed (`LocaleService`), and tests that set a thread culture before touching it.**
`src/SiloPlayer.Core/Localization/LocaleService.cs:22-23` reads `CultureInfo.CurrentCulture.Name` of whichever thread first touches the class (its static
`EnglishResolution`, then `Shared`), and `AppLocale` (`AppLocale.cs:69-72`) builds its `FormatCulture` from that thread's culture object. `AppStrings.FormatCulture` of every
test that does not pin its own locale is therefore that first thread's culture for the life of the process. In `directconn-final-r1` the 11 failures (`AdminTasksTextTests`
×2, `AdminHomeSectionsPresentationTests`, `AdminPreparedFilesPresentationTests` ×4, `PlaybackSpeedMenuTests`, `AdminAutoscanActivityDisplayTests`, and `AdminMaintenanceTests.ResultsFollowTheOfficialPerKindSummaries`
whose English list join follows `FormatCulture.TwoLetterISOLanguageName`) all started within the first four seconds, right after `CalendarCultureTests` (ar-SA / th-TH, which calls
`new LocaleService()` with the thread set to that culture) — in `directconn-final-r2` those tests ran about 2,700 positions later and the leak did not happen. Proven directly
(temporary probe, removed): a second copy of Core loaded in a fresh `AssemblyLoadContext`, first touched on a thread set to ar-SA, keeps `ar-SA` and formats 7.5 as `7٫5`
(`stab2-b-probe-r1`). Thread cultures themselves were always restored; nothing in `tests/SiloPlayer.Tests` sets `DefaultThreadCurrentCulture`, `LocaleService.Shared.Apply` or
any other process-wide culture/language setting (the only process-wide state is that first-touch capture), so no non-parallel collection is needed. The leaking sites are the culture-changing tests that can be the
first to reach `LocaleService`: `CalendarCultureTests` (2 methods, ar-SA/th-TH/fa-IR), `AdminCollectionNamesTests.CountsUseTheCurrentCulture` (de-DE),
`AdminBackupsRefreshTests.BlockedGroupTitlesUseTheSingularFormForOneItem` (en-US), `AdminSearchSettingsControllerTests.SemanticRatioIsShownWithTwoDecimals` (invariant),
`MpvCoordinateFormatTests` (nb-NO/fi-FI/de-DE); and `AdminDashboardFormattingTests`, whose constructor set the thread to en-US and never restored it.

Fix (test only): `tests/SiloPlayer.Tests/TestCulture.cs` — a `[ModuleInitializer]` touches `LocaleService` before any test code runs (while the thread still has the machine's
culture), and `TestCulture.Use(...)` is the one scoped set-and-restore (current and, on request, UI culture; idempotent dispose), which also forces the baseline before it
changes anything. All seven sites use it; `AdminDashboardFormattingTests` is now `IDisposable`. `CultureIsolationTests` pins the helper (restore after an exception, nesting, an
await that resumes on another thread) and that the process format culture is the machine's, also while a thread is scoped to ar-SA.

Product risk: the static is read once at start-up on the UI thread in the app, where the culture is the user's own, and then read-only (`LocaleService.Apply` swaps an immutable
snapshot under a lock; `AppStrings.Current` reads it with `Volatile.Read`), so formatting threads see a consistent locale while the language changes. No product change. **Not
established:** I did not reproduce the 11 failures in the suite itself: `stab2-b-base-r1` (the 11 classes plus the culture-changing ones, unchanged tests) passed, as did six runs
with the baseline pin disabled (`stab2-b-nofix-01..06`; the order of two parallel classes decides it). The cause rests on the code, the run ordering and the probe above.
After the fix: `stab2-b-loop-01..10`, each running the previously failing classes, every culture-changing class, `CultureIsolationTests`, `AppUpdateExitTests`,
`LateReadFromRetiredAuthorityIsDropped`, `UpdateTransactionLifecycleTests` and `FullInstallerFallbackTests`: 10 of 10 passed (458 tests each).

## C. `AppUpdateExitTests.ExitHelperVerificationAcceptsOnlyTheExpectedPackageWithoutLaunchingAnything(validateGate: True)` — "Another Silo process cannot be checked. Update postponed."

**Cause: shared machine state between two test classes.** With `-ValidateExitGate` the update helper (`src/SiloPlayer/Updates/UpdateOnExit.ps1`, the loop over
`[Diagnostics.Process]::GetProcessesByName('SiloPlayer')`, "cannot be checked" branch) examines every process on the machine with that image name and defers, by design,
when one has no readable image and has not exited. `UpdateTransactionLifecycleTests` starts and kills real processes with that image name (a copy of `ping.exe`
saved as `SiloPlayer.exe`, `Start`/`Kill` in that file, whose own comment notes that a started process has no stable image yet); it is in the xUnit collection
"Synthetic Silo process lifetime", `AppUpdateExitTests` was not, so the two ran in parallel. Seen twice in 547 recorded TRX files, both times the `validateGate: True`
case (the only one that reaches the scan), both in full runs (`directconn-final-r2`, `autosrc-final-r2`).
Fix (test only): `AppUpdateExitTests` joins that collection. The helper's fail-closed behaviour is right and unchanged (`FullInstallerProcessScanTests` pins it).
**Not established:** which exact synthetic process was in flight in those two runs (nothing records it); another lane's SiloPlayer-named process could cause the same
deferral, which the collection cannot prevent. Evidence: `stab2-b-loop-01..10` passed with the class and its collection partners.
Also seen in `directconn-final-r2` and not mine: `AdminDirectConnectionsControllerTests.ItPollsEveryFifteenSecondsOnlyWhileActive` ("Timed out: activation read").

## D. `ServerActivityStoreTests.LateReadFromRetiredAuthorityIsDropped`

**Cause: timing in the test's gate, not in the product.** The harness counts a request (`Hits`) and only afterwards the handler decides, with
`Interlocked.Increment(ref sessionCalls) == 1`, which sessions read holds the gate. The test went on when `Hits == 1` was seen. If the first request's thread was
preempted between those two steps (heavy load), the test thread invalidated the authority and the *second* read reached the handler first, took the gate, and was then
released with the "old" session: that read is the current authority's, the store applies it correctly, and `Assert.Empty` fails. The product (`ServerActivityStore.Apply`:
lease identity and resource version) drops reads of a retired lease as intended.
Fix (test only): the handler signals when it has handed out the gate and the test waits for that signal, with a timeout, instead of for the hit count. Assertions unchanged.
This file is owned by the open stability pull request; the edit is one separate commit whose hunk is far from that pull request's (verified with `git merge-tree`: it merges
cleanly), so it can be dropped on its own. **Not established:** the failure itself was not reproduced (not without artificial CPU load, which is not allowed here); the test
also cannot observe that a late response *was processed* before it asserts (it asserts after the newer read has applied), which is unchanged.

## E. Native group `synced-sort-direction` — `COMException 0x800F1000` "No installed components were detected"

**Cause: a product ordering defect in the saved-synced editor, exposed on this machine's Windows build.** Reproduced on unchanged Beta (`stab2-e-base-r1`, exit 1): the call is
`UIElementCollection.Add(UIElement)` in `CollectionEditorPage.Field` (`CollectionEditorPage.Current.cs:84`) ← `BuildSyncedOrder` (`CollectionEditorPage.SyncedPresentation.cs:97`, the Direction field) ←
`BuildSavedSyncedContents` (`CollectionEditorPage.SavedSynced.cs:80`) ← the "Change link" click handler (`SavedSynced.cs:114`). It is not an image decoder or SVG source. A diagnostic run
(`stab2-e-diag-r1`, instrumentation removed) showed the reused Direction `ComboBox` with no parent, not loaded, but still with a `XamlRoot`, and that adding it to any new panel
fails with `0x800F1000` while a fresh `ComboBox` or `TextBlock` is added fine. `BuildSavedSyncedContents` detached the other reused controls (`ImportedDefaultSortCombo`, `_titleLimit`,
`_savedSchedule`, …) *before* replacing `ImportedSourceSection.Child`, but not `_syncedDirection`: that one was dropped out of the live tree with the old body and removed from its
already-orphaned old parent only later, inside `BuildSyncedOrder`. On this machine (Windows 11 build 10.0.26300) a ComboBox whose dropdown was opened and that is
removed that way cannot be added again. A real user can take the same path (open Direction, then "Change link" on a saved synced collection), where the exception is thrown from a click handler.
Fix (product): `Detach(_syncedDirection)` joins the other detaches at the top of `BuildSavedSyncedContents` (`SavedSynced.cs:20`) and in the new-synced build (`Synced.cs:103`), so the control is
removed from the live tree like its siblings. Evidence: `stab2-e-fix-r1` passed (whole group, "PASS: synced collection field/direction controls, capabilities and responsive layout"); acceptance below.
**Not established:** whether the failure occurs on other Windows builds (the brief says the group fails on this machine only; I did not run it elsewhere); the exact Windows component behind the HRESULT.

## Not done / limits

- `AdminPluginAppsNativeFixture.cs`, `MediaLocalizationNativeFixture*.cs`, the xUnit runner configuration, project files and the build scripts were not changed (the one edit to `ServerActivityStoreTests.cs` is described in D).
- Not mine and not touched: the `admin-user-resources` contrast check and `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling`; `DashboardSessionsKeepSharedReadErrorsAndAnyEarlierData` fails in
  focused runs that include its whole class on this base (fixed by the open pull request).
- Created outside the worktree: only the lane's build and result files under `D:/SiloPlayer-build/admin-completion-lane-lead` (`stab2-*`) and the scratch files of the session.

## Acceptance

`stab2-final-r1` (`-Stage final`, passed; source `5e300c707`): focused 758 passed / 0 failed (filter: the touched classes plus `Collection*`), full unit suite 12,064 passed / 0 failed (the first full run of this branch,
so the known intermittent tests were not rerun), app build, publish and native host build passed; native `plugin-cutover` 7 PASS, `admin-plugins` 364, `synced-sort-direction` 2, `shell-localization` 4, `admin-manage-foundation` 36,
`admin-monitor` 1,151 PASS lines, 0 FAIL and no `NOTE:` restart line in any (the restart path of A was not needed in this run). Counts are `PASS` lines of each `results.txt`.
