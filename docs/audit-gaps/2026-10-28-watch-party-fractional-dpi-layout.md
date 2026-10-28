# Watch Party freeze — fractional-DPI layout feedback

Owner-authorized scope: reproduce the Watch Party freeze, repair the actual cause, verify and provide a local test installer. No push, merge, publication or installation. Preserve326's complete reviewed baseline and the held15 phone-control decision. Computer control of user applications remains paused; fixtures operate only their own windows.

## Live evidence and missed test condition

Installed1.2.326 (PID48796 at inspection) matches the sealed326 DLL SHA256 `3BDD1865E11685B454D0321893B3F6AA9ECFA4DA4C8CC96F5B01277AE6DE6662`. Read-only window queries show144dpi/150% scale, client2560x1369. At October28 **02:31:25CDT /07:31:25UTC**, crash.txt logs `Microsoft.UI.Xaml.LayoutCycleException`, HRESULT `0x802B0014`, `Layout cycle detected. Layout could not complete.` The global exception handler marks it handled; that does not repair the failed layout. This fits the reported continuing title/navigation handling with frozen page visuals.

Earlier fixtures were entirely outside all monitors and actually ran at100%. Moving farther offscreen did not reproduce150%; the new expected-scale assertion rejected that setup rather than counting it as a bug reproduction. To exercise the physical150% display without controlling the user's application, the native fixture makes only its own HWND entirely transparent, click-through and nonactivating before placing it on that display. It checks HWND ownership and XamlRoot.RasterizationScale. No display settings or installed-player window are changed.

Evidence root: `D:\SiloPlayer\.codex-tmp\watch-party-cause-oct28`.

## Reproduction and isolation

The unchanged, frozen326 payload fails with the same layout-cycle exception in `baseline-150-monitor-home`, `baseline-150-blank`, and `baseline-regression-150-library`. Blank→Watch Party is sufficient; no media playback, populated Home data, server API success or previous route is necessary. Failure records identify phase `watch-party` and actual raster scale1.5.

Ranked hypotheses: (1) decorative-image measurement feedback; (2) responsive Page_SizeChanged rewrites; (3) text rounding independent of the decoration. Probes changed one variable. Hiding only the glow makes the150% fixture pass (`probe-hide-glow`), with the same text. Detaching Page_SizeChanged still fails (`probe-detach-responsive`). The latter trace shows the image Source remains false throughout the growth, so async bitmap arrival is not required.

The trace is decisive: card height375.333→376.667→378.000→378.667→380.000…, while image.Height increases375→376→377… . `RenderCreatePartyGlowAsync` responds to the card's SizeChanged and sets the direct Grid-child Image height to `Ceiling(card.ActualHeight - border)`. At fractional DPI this larger image contributes to the auto-sized card's desired height. WinUI's physical-pixel rounding then yields another fractional card height, triggering the loop until layout aborts. It is a desktop measurement dependency, not a Watch Party network wait.

## Focused repair and regression

Wrap the unchanged decorative Image in a non-hit-test Canvas within the existing Grid. The content continues to own the card size, while the image can render at rounded raster bounds without contributing to parent desired size. Preserve blur, color, image name, draw order and controls. The code-behind change only explains that measurement boundary. No server/room/SiloCast/playback behavior changes.

`WatchPartyLayoutNativeFixture` requires no layout exceptions or dispatcher stall, actual visible bitmap source, a laid-out card, and successful Library/Home layout after leaving Watch Party. It also changes the decorative image dimensions and asserts that the card remains the same size. This invariant **fails against original326 even on100%** (`baseline-regression-100`: `Decorative glow resize changed the auto-sized Watch Party card.`), so future regression detection does not require a fractional-DPI monitor. Native transparent-window support is fixture-only. Temporary hypothesis switches were removed from final fixture source; preserved probe logs remain evidence.

The focused diagnostic candidate passes the original150%-Home reproduction with the visible decoration and post-party Library/Home navigation (`fixed-150-home`). Independent read-only review found no actionable issue in the product fix; its nonblocking JSON-timing observation was corrected before final installer verification so success is only recorded after navigation checks and restored layout. Final327 packaging/tests are recorded below once complete. Diagnostic candidate version326 is never substituted for the frozen326 installer.

## Final1.2.327 verification and delivery

The unchanged packaged327 candidate passed **2,251 main tests,16 native groups and33 published-service groups**, plus receiver/discovery/upscaling consumer compilation. The native groups cover the complete326 selected set and add150%-Home,150%-Library, a second150%-Home run, real window resizing through900x600/1920x1080/3440x1440/3840x2160/2560x1369, and the existing three-width Watch Party presentation matrix. Those actual150% runs all verify XamlRoot.RasterizationScale1.5; the original failure phases/scale and100% invariant failure are also checked by the sealing script. Success JSON is written after navigation-away checks and a restored-layout pass. Default100% Home/Library tests retain the decoration-size invariant. Original326 fails; fixed327 passes the same symptom path. This establishes the reported fractional-DPI freeze cause and repair, not the cause of unrelated historical crashes.

Actual appearance checks verify nonzero glow alpha pixels (1280:61,024;600:60,594;500:60,967), control bounds and responsive hub/room presentation. The1280 hub capture was visually inspected; the theme-derived decoration remains visible without covering the controls. No server room was created on production. All account/API/reader inputs are synthetic and isolated. The entirely transparent real-monitor fixture controls only its own verified process window; the installed player is not clicked, closed or replaced.

All572 packaged files match native mirror/native host/published host, excluding intentional fixture-host hostfxr handling. All1,496 source/test/build/library inputs remain unchanged after packaging. Comparing all326 recorded inputs confirms only the focused two page files, version metadata and native regression fixture support changed; all other accepted review source is preserved. New transparent-window support is test-only. PR15 remains held and receiver control source is unchanged from main. Independent review found no actionable product issue.

Installer `D:\SiloPlayer\installer\output\SiloInstaller-1.2.327-Setup.exe`,172,774,059bytes; SHA256 `2541CB2E8DE9960E745814DB53A7342869F2FCFAF7E2C996A08751FE79F3C545`. App DLL SHA256 `AD428DBF050EC118A8DABCE80C95109B02B49F8560BFFEE86D5C81E5EBE1CBF4`. Payload `D:\SiloPlayer\installer\publish-1.2.327-watch-party-verified`. Receipt `D:\SiloPlayer\installer\output\SiloInstaller-1.2.327-verification.json`. No installed-app testing, installation, commit, push, merge or publication occurred. Owner testing of327 is the next step.
