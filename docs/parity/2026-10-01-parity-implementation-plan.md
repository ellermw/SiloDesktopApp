# Full user-facing parity implementation plan

User authorization: October 1, 2026 — implement the audit, keep reporting package completions, count changes for 1.2.xxx, then build installer, push main and update README/download. The [audit and its 14 packages](2026-10-01-user-visual-functional-audit.md) are the specification. Official main fetched again at execution start remains `8e2e840474a085c6df6571a5a2850f7eb996810c`.

Goal: resolve the recorded user-facing visual and functional differences while preserving native libmpv decoding, fast loading, virtualized browsing and stable playback. Reuse the current isolated candidate worktree and preserve its existing 1.2.0 changes. No production infrastructure changes, credential publication or closing the installed player.

Execution: root implements shell, Requests and integration; independent browse, account and media domains can execute concurrently with explicit file ownership. Root owns MainWindow, DarkTheme, release versions, README, CHANGELOG and final builds. Agents submit integration needs for owned shared files instead of editing them. Test/build processes use one coordinator to prevent shared output races. No release while any required implementation or acceptance remains open.

## Version and progress rules

- Record each repaired finding ID, behavior/layout changed, affected files and evidence in the implementation ledger.
- Count a verified product correction once, even when multiple audit findings describe the same cause. Cosmetic file movement, docs, test additions, and repeated verification do not inflate the count.
- Final version is `1.2.N`, N = verified distinct corrections in this implementation pass. Until verification, entries are implemented/unverified and do not count.
- Emit a user progress update when each package passes its stated checks. A package's implementation check is separate from final rendered/end-to-end acceptance.

## Tasks and ownership

For each task: inspect pinned official declarations/handlers and native current behavior; add a failing meaningful runtime/interaction regression where behavior changes; observe the failure; implement the current contract; run that regression and relevant neighbors; check representative rendered states for visual changes; record findings and remaining acceptance before marking it complete. Pure visual sizing/color adjustments use rendered measurement rather than tests mirroring XAML strings.

| Package | Files / boundaries | Implementation and verification |
|---|---|---|
| 1 root | Views/CatalogPage.xaml(.cs), Views/NotificationsPage.xaml.cs, Core/Services/NotificationNavigation.cs, tests/NotificationNavigationTests.cs, native personal-list fixture | Keep actual Catalog sidebar route; add In library/Titles tabs and external title attention count with existing RequestsApi/Watchlist models; resolve request notification TMDB destinations before ordinary catalog targets. Test approved/declined movie/TV targets, malformed data and ordinary episode/series fallback; native verify reachable tab, removal and navigation. |
| 2 root | Themes/DarkTheme.xaml, Services/ThemeService.cs, MainWindow.xaml(.cs), shared icons/control helpers | Align button size/variant and switch/slider/dialog defaults; replace differing shell icon shapes; remove retired theme residue; responsive flyout and Back styles. Measure shared normal/hover/focus/disabled states; protect transparent poster targets. |
| 3 root | Views/RequestsPage.xaml(.cs), RequestBrowsePage.xaml(.cs), RequestDetailPage.xaml(.cs), Dialogs/RequestSeasonsDialog.cs, request models/services/native fixture | Reuse matching card actions/status/metadata/theme/hero and current hub/Yours layout, confirmation, corner Watchlist, downloads, append paging and season contract. Isolated actual request/cancel/watchlist payloads; hovered poster, Back, whole-series/future/selected season states. |
| 4 browse | Controls/QueryRulesEditor.cs, catalog/library/search/collection filter regions, Core query editing contracts | Share typed/grouped/guided editors, people search, ranges/multi-select/refinement chips, lossless mode transitions and visible preview windows. Test complex config round trips and actual request payloads; native exercise filter inputs. Coordinate Catalog edits with root. |
| 5 browse | HomePage, LibraryPage, HeroCarousel, NowListeningHero, relevant VMs | Current hero/card/gap/responsive/Now Listening contracts; remember library state and cancel/replacement loads. Delayed A→B grouped request regression, profile setting on/off restore and mounted-card refresh stability. |
| 6 browse | Controls/GlobalSearchDialog.xaml(.cs), Search VM | Match quick dialog sizing, direct-play and people artwork, loading/error/keyboard behavior. Native actual hover/click/keyboard fixture. |
| 7 browse | CollectionsPage, CollectionEditorPage, SmartCollectionWizardPage, VMs | Current galleries/import/editor/default/manual mutation/reorder/artwork/dock contracts. Group preservation and cancel/save/error/query tests; preview pagination beyond100. |
| 8 browse | PersonDetailPage, CalendarPage, NotificationsPage/VM, MediaItemMenu | Responsive Person and refresh, calendar watched/size/focus, notification recovery/pending/cutoff. Coordinate notification and shared menu regions with root; no overlapping writes. |
| 9 media | WatchTogether views/VMs/services, Core party contracts; detail callback integration supplied to root | Current start sheet, staged selection/readiness, guest suggestions, mode changes, rejoin/shelves/member state/room rail. Local delayed/error host/guest fixtures and protocol tests; no live multi-user mutations. |
| 10 media | ItemDetailPage/VM, audiobook controls, manga detail and ThemeMusicService | Current bounded detail/season layout, responsive audio, narration picker, active Pause/Resume, files and fade contracts. Verify transport targets without restarting current book; responsive native captures. |
| 11 media | libs/mpv/scripts/silo-osc.lua, PlayerService preference integration, EbookReaderPage/assets | Compact/Fill controls, responsive reader, settings persistence error/coalescing/local recovery. Lua/player runtime and reader bridge/error fixtures; original native codec path retained. |
| 12 account | SettingsPage partials/XAML/VM, profile-device/current setting helpers, RecipeGalleryDialog, provider models/API | Current shell/rows/gates/device definitions/inheritance, overlays/default restoration, Home editor/transfer, language/dormancy/schema/preview contracts. Mocked rejection/locked/unsupported/range/tag/schema fixtures and native settings state captures. |
| 13 account | Auth/recovery/profile/taste views/VMs, FeatureTourDialog | Shared branded layout, reset route/sign-out, picker/editor/taste and guarded tour progress. Failed Next/finish keeps current step; profile/state/malformed deep-link runtime checks. |
| 14 root | Metadata/person editor dialogs, MatchItemDialog, RefreshMetadataDialog, MangaFilesDialog and related APIs | Role-correct entry, type-specific metadata fields/locks/reset/translation/images/person diff save, match truncation/applying guard, responsive/pending/file formatting. Isolated model/payload and native dialog action fixtures. |

## Final acceptance and publication

- Review all changed domains and unresolved audit rows; never convert a source-only result into 100% visual acceptance.
- Build current official WebUI locally with isolated matching fixtures where needed; compare rendered states with native regression host (ordinary native CUA being disabled does not prevent fixture rendering). Record viewport, DPI, theme and source/candidate fingerprint. Dynamic installed plugins and hardware/live multi-client checks remain explicit limitations until exercised.
- Run full Release tests, native regression host, published playback service regressions, Release publish and installer build. Fix integration failures before counting/releasing.
- Set project/installer/changelog version to ledger count, verify payload versions and installer SHA256, document exact validation and residual limitations.
- Review whole change set, preserve unrelated artifacts, integrate into main without force/destructive reset; create GitHub release and upload installer; verify asset download hash; update README to actual release URL; push all authorized current-build changes. User has authorized publication; do not request the same approval again.
