# Playback startup failure leaves a blank native window

## Installed incident

On October 25, 2026, at 10:03:17–10:03:23 America/Chicago (15:03 UTC), installed 1.2.311 accepted the direct/original plan for American Horror Story, season 13 episode 12, file 42336304. Requested resume position: 649.4703333333334 seconds. No `FileLoaded` or `PlaybackRestarted` occurred.

The completed diagnostic `stall-20261025-150319774-4d7c957a.json` contains 36 range requests across two native loads. Every request asked for bytes 0–33554431; every recorded response was HTTP 200, HTTP/2, Content-Length 1267398497, with no Content-Range. The strict native reader rejected these headers as `range_not_honored` before consuming media bytes. These were not recorded 401/403 responses. The first startup retry incorrectly used position zero; the second failure entered the terminal player state.

A non-suspending managed stack observation of PID 42904 showed the UI thread in the native application message pump, not a managed deadlock. Read-only native inspection showed the `Silo Player` popup above the populated Home shell. The terminal handler sent its error to the Lua/ASS video overlay and hid the XAML loading overlay, although the renderer had never produced output. Consequently the blank native popup covered the shell with no usable error/exit surface. This is a reproduced desktop presentation/lifecycle bug.

The installed process subsequently exited without our controlling or closing it. A later independent header-only request recovered the same URL in memory from the existing 10:03:17 log entry, without writing or displaying it. At 15:19:05 UTC both fresh HTTP/2 and HTTP/1.1 requests returned 401. That later observation cannot establish why the original live requests returned 200. No full response body was downloaded. Do not claim that the original delivery failure, the origin of the missing ranges, or historic buffering is resolved.

## Reproduction and repair

`PlaybackStartupFailureNativeFixture` exercises real libmpv, the actual native popup and MainWindow against an isolated loopback server that returns the recorded HTTP 200/full-length headers to range requests. Frozen 1.2.311 reproduces 36 rejected requests and ends with `firstOutput=False`, `state=Expanded`, `nativeVisible=True`, `errorVisible=False`, zero session stops. Evidence: `.codex-tmp/playback-start-oct25/startup-red2-311`. The initial harness setup failure is preserved separately.

Version 1.2.312 handles a terminal video load failure before first output through the existing XAML startup-error surface. A queued, generation/manager-checked action closes the failed playback session, hides the native popup and presents a dismissible error. Failures after first output retain the existing native Retry/Exit presentation. The intended transport start position is retained until output exists; initial retries, pause reporting and close use that position rather than the decoder's initial zero. The ordinary two-second recovery rewind still applies after actual output.

The exact 312 payload reproduces the same transport rejection and now ends `state=Idle`, `nativeVisible=False`, `errorVisible=True`, one session stop. The native test invokes Go Back, navigates successfully into Settings and checks that final progress was sent at the retained resume position. Additional actual native-output cases check that a queued failure from an older load cannot close a newer load, and that post-output failure keeps native Retry/Exit. Existing profile-menu navigation and actual slow-native-reader adaptive recovery are also checked.

Published-service regression checks rejected the first 312 candidate because an uninitialized start-position field replaced a synthetic remote session's captured final progress. The fallback now requires an explicitly populated, nullable pending start position and is cleared on close. The original candidate installer is preserved under `installer/output/startup-312-before-pending-position-guard`; its payload remains `installer/publish-1.2.312-startup`. Final inputs and artifacts use `verified` names and are checked again. Do not distribute the earlier candidate.

Evidence lives under `.codex-tmp/playback-start-oct25`; preserve failed intermediate harness runs. One harness refinement waits for either the transient service error or the displayed XAML error because successful teardown clears the former. This test-only refinement did not change the packaged app. The initially attempted published-service host referenced a payload lacking build-only app-logo assets; the successful host references the hash-matched native mirror with those source assets overlaid. This did not change the installer payload.

The final packaged-service harness's existing viewer-retry assertions now explicitly set `firstOutput=true`: they simulate retries after output at 687.3 seconds and previously had no output-state setup because the presentation policy did not distinguish startup. Without that setup the new startup branch intentionally has no viewer-retry state. This test-only change is separately hashed after packaging; the actual native fixture independently verifies both pre-output and post-output behavior. The remote final-progress assertion remains unchanged and passes with the nullable pending-position repair. No product change followed the final package build.

## Scope and source

Official Silo main was fetched directly at `6de6d5e5e285a9b423e95a1122cb39e5bb9ca93b`. Its proxy direct-play protocol continues to document standard range/conditional handling and HTTP 206. This was a focused startup-failure investigation, not a new full upstream/parity pass. No server configuration or production infrastructure was changed. No native engine change, range-validation relaxation, forced remux or automatic installation was made. The earlier 311 profile-menu repair remains included; public main remains 1.2.310 pending owner approval.

See `docs/releases/1.2.312.md` and the versioned installer receipt for final checks and hashes.
