# Home return and navigation investigation — October24

Status: repaired and verified in local1.2.304. This is not a claim that all historical Home errors or playback buffering have been resolved.

## Preserved installed evidence

Sanitized copies of installed Home, navigation, crash, API and UI-lag logs are under `.codex-tmp/oct24-implementation/saved-*.txt`. URLs, credentials and token patterns were removed before saving. The installed application has remained open and unchanged.

The October23 `sidebar_navigation` stack records WinUI `COMException0x80004005` from `NavigationView.PaneDisplayMode.set` inside `MainWindow.NavView_PaneOpening`, reached during frame navigation and `OnNavigated_SynchronizeShellChrome`. The app changed WinUI pane mode while WinUI was delivering its pane-opening callback. Pane reconciliation is now queued after the callback and the frame uses the existing responsive mode-before-open path. The exact rare COM failure has not yet been reproduced in the fixture; repeated authenticated shell transitions and a direct non-reentrant callback regression are being verified separately.

Home logs also contain HTTP connection-refused/origin-empty-response errors and older collection/projection exceptions. A recorded HTTP failure is distinct from the native navigation stack. Older null-section handling already exists in286; historical entries must not be counted as newly repaired defects without a current reproduction.

## Reproduced released-build return failure

`home-red/results.txt` runs the actual frozen286 payload with an isolated HTTP handler and native Frame. It loads Home, navigates away, delivers the real `PlaybackProgressUpdated` notification and goes Back. It fails: **Playback-return rebuilt Home's mounted row controls.**

`HomePage.Page_Loaded` used any changed `RenderRevision` as a reason to call `BuildContent`. That method clears all SectionRows and resets the hero even though the page itself is cached. Progress notifications change that revision while Home is inactive; the next return therefore discards the mounted row surface. Cache invalidation is not equivalent to changing account/profile authority or replacing the layout.

The repair reconciles current rows by section ID, preserves unchanged row/source identity, patches changed metadata/order and retires queued UI work after departure. Full reconstruction is reserved for a different captured request authority. SectionRow also avoids setting an identical ItemsRepeater template or source again.

The first repaired native iteration retained rows but exposed additional item-source/artwork-retention edges. A metadata slot copied its existing item collection, forcing an unnecessary repeater rebind. That collection is now retained. An unchanged Frame return keeps native card containers, but their unload handlers release decoded artwork; a bounded per-UI-thread working set now retains only visible decoded artwork (at most32 entries/24MiB), with account/profile, exact URL, decode width and spoiler state in its identity.

`home-queued-row-red` reproduces an actual native crash when a collection callback is queued and its row's Section is cleared before delivery. The callback now captures the section/activation and checks both before touching the control. Full stack logging was retained for Home load failures.

Further native checks found two remaining cases in the first303 candidate. Already-decoded blurred bitmaps do not reliably emit the ImageOpened notification used to populate the new cache; they are now also recorded immediately after assigning the completed bitmap. `home-catchup-final303-red` proves intentional transport catch-up retained VM rows but changed the request generation, which the page incorrectly treated as requiring reconstruction on return. The page now compares a surface-retirement generation that advances only when the VM actually clears obsolete authority data. Ordinary access/profile retirement still rebuilds; deliberate same-owner transport catch-up retains the visual surface.

The first303 installer/payload are preserved under `pre-home-cache-repair`, with installer SHA256 `DC26D001EEBDFD5FECB9D8265BAF5FC960DE284897A27C0EA85A3FA74BC96412`. It was never installed or delivered as verified. The corrected303 candidate was built from `installer/publish-1.2.303-verified`; final304 supersedes both303 candidates and its receipt distinguishes their evidence.

Native fixture teardown drains unload callbacks before disposing fake services. Extra disposed-service lines after early failed iterations belong to test teardown, not installed-app evidence. Duplicate artwork URLs can legitimately reuse another already-decoded instance of identical artwork; the fixture verifies membership in the pre-return decoded working set rather than requiring one arbitrarily chosen instance.

## Verification record

- Released286 original failure: `.codex-tmp/oct24-implementation/home-red`.
- Initial repaired iterations: `home-checkpoint` and `home-checkpoint2`; these are intermediate failures, preserved deliberately.
- Membership API correction: seven tests, three original failures and seven passing after repair, under `membership-red`/`membership-green`. Current server capability/membership routes replace old expected404 probes while older servers retain their existing behavior. This does not prove the cause of historical recurring buffering.
- Final304 passes all32 native runs,2,120 main tests and24 published-service playback groups. `native-304-verified-home`, `native-304-verified-home-catchup` and `native-304-verified-home-guards` verify row/card/source/decoded-artwork retention, scroll, metadata patching, retries, empty/error states, authority retirement, stale/null callbacks and intentional reconnect versus ordinary access retirement. Actual card-based tests exercise32-entry/24MiB eviction and hidden/revealed artwork isolation.
- Final installer/payload/source hashes and exact scope are recorded in `installer/output/SiloInstaller-1.2.304-verification.json` and [the304 release record](../releases/1.2.304.md). Earlier303 candidates retain their original evidence; final304 was not installed or published in this pass.
