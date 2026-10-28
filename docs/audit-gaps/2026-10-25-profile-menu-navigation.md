# Profile menu and shell navigation failure — October 25, 2026

## Report and installed evidence

The owner reports that Settings, Switch Profile and Logout from the bottom profile popup do not work. After attempting these actions, Home and library navigation also stop working. The installed baseline is 1.2.310; the running app was preserved throughout investigation and testing.

The installed exception log at 09:41:11 CDT (14:41:11 UTC) records `ArgumentException`, HRESULT `0x80070057`, at `TextBlock.FontSize` inside `SettingsPage.ReflowSettingsShell`. Its caller chain is `ShowSettingsDetail` → `SetShellSidebarHosted` → `MainWindow.SynchronizeSettingsSidebar` → the frame's navigated callback. Later exceptions at 09:41:16 and 09:41:20 reach the same font setter through `DetachSettingsSidebar` / `RestoreAppSidebar` / `HideMainNavigation`; the final stack is in the logout callback. A native `COMException 0x8000FFFF` also appears at the first failure.

Sanitized stack evidence is preserved under `.codex-tmp/profile-menu-oct25/installed-exceptions-redacted.txt`. No settings values, credentials or signed URLs were copied.

## Cause

`SettingsViewportWidth` fell back to `Frame.Width` when the page had not yet been measured. A frame sized automatically by its parent has a requested Width of `double.NaN`, even though its measured `ActualWidth` is valid. During the frame navigation callback the new page does not yet have its own measured width or XamlRoot, so the viewport helper returned that NaN. `Math.Clamp(NaN, 32, 48)` remains NaN; assigning it to a WinUI TextBlock's FontSize fails.

The exception interrupts Settings sidebar initialization. Subsequent actions attempt to detach that page and hit the same invalid layout calculation, interrupting shell navigation and the logout/profile transition. The menu's click handlers are wired; this is a page-layout exception reached through those handlers.

## Original-build reproduction

`ProfileMenuNavigationNativeFixture` uses a separate hidden/offscreen native window, isolated settings directories and a synthetic HTTP handler that rejects external requests. Its synthetic AuthService has no credential store. It opens the actual profile flyout and invokes the real Settings button.

The frozen 1.2.310 payload fails this test. Immediately before the click at a 1920px client width, the fixture records `Frame.Width=NaN` and `Frame.ActualWidth=1659`, then receives the native `0x8000FFFF` failure. Evidence: `.codex-tmp/profile-menu-oct25/profile-menu-red3` and `profile-menu-red4`.

Earlier harness iterations corrected a Popup API name and waited for the footer's native XamlRoot before opening it; these are fixture setup failures, not product failures. All evidence directories are preserved.

## Repair and verification scope

Use the measured page width once available. Before that, ask the attached Frame for its measured width and XamlRoot, allowing the shared helper to recover the whole client viewport. A detached page without a Frame uses finite zero until layout provides a size. No requested Width or invalid font size is propagated; no catch-and-ignore navigation workaround is added.

The regression traverses actual profile-popup Settings, library data loading, populated Home, Switch Profile, actual profile reselection and Logout at 1920, 3440, 3840 and 900px client widths. Existing responsive Settings and protected-profile checks remain relevant. The earlier Settings visual fixture mounted pages through `Frame.Content` and waited for layout; that did not exercise the failing pre-measure Frame.Navigate callback.

Final exact-payload runs pass at all four widths in separate host processes, plus existing responsive Settings, PIN-protected profile and library-filter regressions. Candidate harness refinements wait for popup close animation and realized controls. Repeatedly constructing application-global MainWindows in one host caused later popup realization failures; each final test uses the real app's single-MainWindow lifecycle. These test-only refinements and their intermediate failures are recorded separately from the unchanged packaged repair.

Version 1.2.311 is one correction group beyond published 1.2.310. The engine, playback and server contracts are unchanged. Official Silo main was freshly fetched at `6de6d5e5e285a9b423e95a1122cb39e5bb9ca93b`; this focused repair is not a complete new upstream comparison. Final candidate hashes, checks and limits are recorded in [the release record](../releases/1.2.311.md).
