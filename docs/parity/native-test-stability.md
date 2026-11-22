# Native and unit test stability — November 22, 2026

Scope: three intermittent or failing checks on Beta that made the automated pipeline retry or fail pull requests that did not cause them.
Branch `t3/native-test-stability` (from Beta `984044bb5`). Each was reproduced on unchanged Beta, traced to its cause and fixed where the
cause is. No assertion was deleted or loosened, and no blind sleep or blanket retry was added. No product source changed.

## A. Native group `admin-plugin-apps` — "no valid CoreWebView2"

**Cause: a third-party overlay hook crashes the web view's browser process; not the product, not the page.**
The product never uses the web view before it is initialised (`PluginRoutePage.Admin.cs` creates the control, awaits
`EnsureCoreWebView2Async` and only then touches `CoreWebView2`; the fixture's reads are guarded the same way). What happens is that the
`msedgewebview2` browser process exits by itself about 700 ms after the page reaches *Ready*, with exit code `0xC0000005`. The WinUI
`WebView2` control then drops its `CoreWebView2`, so the fixture's next `ExecuteScriptAsync` (the "a refused link leaves the plugin page as it
was" check, `AdminPluginAppsNativeFixture.cs` line 138 before this change) throws "valid CoreWebView2 is not present"; when the check reads
state instead of calling the control it fails as "a refused link leaves the plugin page as it was". The product reacts as designed: it shows
the *LoadFailed* state with Reload (`OnAdminProcessFailed`), which the fixture asserts separately in its deliberate-kill step.

How it was established: every failing run leaves exactly one Crashpad report in the run's web view data folder and no passing run does (9 of
9 failures of this kind across the lane's history; 0 of 82 passes). The report's exception record is an access violation at
`RTSSHooks64.dll+0x1490AF` in all nine, the RivaTuner Statistics Server (MSI Afterburner overlay) hook DLL that is injected into
`msedgewebview2.exe` on this machine. A temporary trace of the page's lifecycle in the product showed no close, unload, replacement or
navigation of the control before the exit: `State Ready` → 690–800 ms → `ProcessFailed BrowserProcessExited reason=Unexpected exit=-1073741819`.
It happens in bursts (about 5 of 9 runs in one half hour, none in 28 consecutive runs of another), which is why it looked random.

Fix (fixture only, `AdminPluginAppsNativeFixture.cs`): the part of the run that opens the first plugin page is started again, on a fresh page and
with the server's request record cleared, but only when all of these hold: the browser process exited unexpectedly with `0xC0000005` (read from
the control's own `ProcessFailed`), and the `RTSSHooks*` module was loaded in that browser process when the run attached. At most three starts; any
other failure, any loss without the hook, and a third loss still fail the run. The restart is logged (`NOTE: the web view's browser process was
lost to the overlay hook ...`). It cannot hide a product regression: a regression produces failed checks, not an access violation inside a
third-party DLL.

Second failure mode, same selector (`nothing from the retired page reaches the server afterwards`, 6 of 98 recorded runs, no crash report):
the fixture counted the server's requests as soon as the *new* page's script had fetched its probe, then changed the account's role. It now
waits for `document.readyState === 'complete'` first, so the page's own load is finished before counting, and the check's message names any
request it sees. **Not established:** which request arrives late in those runs; none occurred in the 14 runs after the change. If it recurs the
failure message now says what it was.

## B. Unit test `ServerActivityStoreTests.DashboardSessionsKeepSharedReadErrorsAndAnyEarlierData`

**Cause: the test drove the dashboard data store from several threads; the store is single-thread by design.**
Failing run (`exitcrash-final-r3`): `InvalidOperationException: Operations that change non-concurrent collections must have exclusive access`
at `DashboardDataStore.EntryFor` (`Dictionary` insert) ← `SyncFeed` ← `Start()`. `DashboardDataStore` is confined to the UI thread: it captures the
synchronization context when it is built, posts feed and timer callbacks to it and resumes its reads on it. The test cleared the context
(`SynchronizationContext.SetSynchronizationContext(null)`), so `Post` ran inline. The real `ServerActivityStore` publishes from thread-pool
threads (its reads start with `await Task.Yield()`), and each publish reaches `SyncFeed` through the feed's `Changed` event while `Start()` is still
running `SyncFeed(initial: true)` on the test thread: two writers on `_entries` (and `_realtimePending`, `_activeScans`). No product code runs the
store without its context. 2 of 61 recorded runs of this test failed; it does not fail in a focused run on a quiet machine (300 + 3,600 + 4,800
iterations of the original arrangement, looped in my runs, never failed), which is why it only showed in the full suite.

Fix (test only, `ServerActivityStoreTests.cs`): a small `SerialContext` (one thread, the UI context's contract) owns the dashboard; it is built,
started, read and disposed on that thread, and its reads and fetches are marshalled onto it. The assertions are unchanged. `FakeChannel` (the only
other shared fake in the class that is touched from store threads) now locks its two dictionaries. The other tests in the class do not build a
dashboard store.

## C. Native groups `media-localization` and `media-catalog-localization` — exit code `0xC000027B`

**Cause: the fixture's synthetic server told the Requests browse page that requests are disabled; the page left for Home in the app's own hidden
frame, where the fixture's services cannot build `HomePage`.**
`RequestBrowsePage` leaves for Home as soon as `RequestsApi.DiscoveryAllowed` is false (`RequestBrowsePage.xaml.cs` `EnsureDiscoveryAccess`), as
the WebUI does. `CatalogHeadingWire` answered every unknown route, `/api/v2/requests/status` included, with `{"items":[],...}`, which parses as
`RequestsEnabled = false`. The page's own status read therefore enqueued `NavigationService.NavigateReplacingCurrentEntry<HomePage>()` on the
dispatcher; `HomePage`'s constructor asks the container for `HomeViewModel`, which the fixture's container does not register, so XAML activation
threw `InvalidOperationException: No service for type 'SiloPlayer.ViewModels.HomeViewModel' has been registered` out of a dispatcher callback. That is
the stowed exception `0x80131509`, fail-fast with `0xC000027B`. It is not at shutdown: `results.txt` stops after the keyed-section checks, before the
Requests part completes. Established with a temporary first-chance-exception trace in the fixture (removed): the exception's stack runs
`HomePage..ctor` ← `NavigationService.NavigateReplacingCurrentEntry` ← the page's dispatcher callback.

Fix (fixture only, `MediaLocalizationNativeFixture.Catalog.cs`): the synthetic server answers `/api/v2/requests/status` with
`{"requests_enabled":true}`, which is what the part of the fixture that drives a Requests section assumes, and the fixture now asserts that the page
stayed and the shell frame did not navigate to Home.

Second failure, `media-catalog-localization` only, ~2 of 20 runs after the first fix (cold start, since `media-localization` has run the same code
before): "Open collection dialog failure did not refresh". The failure text of the open dialog is a catalog key on an element inside the dialog and is
applied when that element loads; the fixture checked after a fixed 100 ms pause. It now waits (bounded, 5 s) for the element to be loaded and the
title, direction and text to show the expected language, and then makes the same checks. The checks themselves are unchanged.

## Not established, and limits

- Which request arrives late in the `admin-plugin-apps` "retired page" failures (see A).
- Whether the overlay-hook crash can also be hit by a user of the player who runs RivaTuner/MSI Afterburner and opens a plugin page. The product
  recovers (LoadFailed with Reload); no product change was made for it.
- `ServerActivityStoreTests.LateReadFromRetiredAuthorityIsDropped` failed once (`Assert.Empty` on the sessions) in a run that also executed 4,800
  iterations of an unrelated stress test with twelve CPU-burning processes on the machine. It did not fail in any other run and was not
  investigated further.
- All tests use synthetic values only.
