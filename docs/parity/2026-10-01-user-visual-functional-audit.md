# Non-admin WebUI parity audit — October 1, 2026

## Requirement and current verdict

The user's requirement, from the start of the project, is **100% visual and functional matching of all non-admin user-facing pages, menus, dialogs, buttons and features against the current Silo WebUI**. This includes small details and every interaction state. A feature existing or a functional test passing does not fulfill this requirement.

**The current desktop candidate does not meet that requirement.** Source comparison identifies substantial page composition differences, missing reachable controls, different menu/dialog structures and presentation drift across several domains. Prior implementation/regression milestones are retained as evidence of their actual scope; they must not be presented as completed visual parity.

This document is the audit index and acceptance register. The attached domain reports contain exact source citations and expected/actual differences. It is not a visual-parity completion certificate or a claim that every discovered control has been rendered and exercised.

## Reproducible baseline

- Official public GitHub main fetched directly on October 1: [`8e2e840474a085c6df6571a5a2850f7eb996810c`](https://github.com/Silo-Server/silo-server/commit/8e2e840474a085c6df6571a5a2850f7eb996810c). All comparisons use exact Git objects; the older checkout was not mistaken for current main. No legacy/private GitLab source was used.
- Desktop: `C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`, branch `codex/parity-top-three`, uncommitted **1.2.0** candidate; base `3488a4942ee0d03448bd609d04334e74b963bc7d`. Released main remains 1.1.105. Installed version and deployed server revision are not assumed.
- Prepared candidate installer from the preceding repair: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.0-Setup.exe`; SHA-256 `BE5BFE9C8379B7F76204E4C9270398EAC1DC81426648DA5BC9B41A58EC486195`. This audit changes documentation/discovery tooling only; it does not repair those candidate UI gaps.
- [Discovery inventory](2026-10-01-user-surface-inventory.json): **70 route expressions**, **323 WebUI page/shared-component TSX files**, **132 native view/control files**, with 566 WebUI and 1,328 native matched control-source lines. These are discovery counts, not unique pages/buttons or completed comparisons. Aliases, wildcards, conditional and multiline controls require manual expansion. Lua, embedded readers and plugin-generated surfaces are accounted for separately in domain acceptance lists.
- Rebuild discovery with `pwsh -NoProfile -File docs/parity/Build-UserSurfaceInventory.ps1`. The inventory reads the pinned official reference and native source; it never reads credentials or calls live mutation endpoints.

## What was and was not verified

**Source-confirmed:** JSX/CSS/component structure, native XAML/control construction, explicit sizes/layout/labels/icons, capability and action wiring, route entry points and their reachable destinations. Domain reports distinguish structural findings from source inferences requiring a runtime reproduction.

**Historical runtime evidence retained:** 1.2.0 passed 1,267 automated tests, the native regression host and six published playback-service checks. Its request-specific native fixture verified loaded posters survive hover/press, Back restores the original search, and the actual season dialog sends only `[2]` to an isolated endpoint. These checks do not compare all visuals or certify all current-server workflows. No broad suite was rerun merely to audit source.

**Rendered evidence in this pass: none.** Browser discovery found no existing tabs, and native app inspection is disabled in this session. Thus there are no same-data, same-size, same-theme WebUI/native screenshot pairs. Every surface below retains pending rendered acceptance. The user clarified that the requested comparison should use the pulled server's WebUI code; no live URL, login or browser session is required to complete this source pass. A later rendered acceptance pass remains a separate gate before certifying visual parity.

**No scope substitutions:** OS-native decoding is required, but does not excuse different user controls. An inaccessible action implemented on an old page does not count as functional parity on the current navigation route. A native platform constraint must be recorded as a difference with a specific resolution, not silently accepted as “close enough.”

## Detailed reports

| Domain | Report | Scope |
|---|---|---|
| Shared shell and Requests | [Requests/shell findings](2026-10-01-requests-shell-visual-audit.md) | Navigation/sidebar/profile menu, shared presentation, Requests hub/search/discovery/Yours, brand and discover grids, all request-card actions, external-title details, cancellation/follow/watchlist, seasons, download states |
| Shared control contracts | [Shared controls findings](2026-10-01-shared-controls-visual-audit.md) | 21 primitive contracts, native shared styles and framework boundaries: button/input/select/person search/switch/slider/tabs/badge/card/dialog/alert/sheet/popover/menu/avatar/separator/skeleton/table/tooltip/accordion |
| Browse, catalog and discovery | [Browse findings](2026-10-01-browse-visual-audit.md) | Home, library tabs, catalog sources, shared cards/rails/menus, search and quick search, favorites/watchlist/history, collections/editors/imports, recommendations, person, calendar, notifications |
| Account, profiles and settings | [Account/settings findings](2026-10-01-account-settings-visual-audit.md) | Auth/recovery/OAuth/invite/activation, household/profile/taste/customize-home, every current non-admin settings destination, dialogs, inherited settings/capability states |
| Media, playback, reader and party | [Media/playback findings](2026-10-01-media-playback-visual-audit.md) | All media-detail variants and menus, native video controls, subtitles, audiobook mini/expanded/chapter/narrator, EPUB/PDF/manga, Watch Party hub/start/join/room/stage/picker/rail |
| Conditional ordinary-page dialogs | [Conditional dialog findings](2026-10-01-conditional-dialogs-visual-audit.md) | Edit Metadata, Match, Refresh Metadata, Manga Files and Edit Person interiors, including nested editor fields/locks/reset/translation/images and their distinct permission gates |

Source finding groups currently recorded: **25 Requests/shell**, **45 browse**, **26 account/settings**, **29 media/playback**, **10 conditional-dialog findings**, plus **5 shared-default groups** (one is supplemental legacy-style debt). These counts overlap and are not a unique bug total. Account's 38 custom imported component rows and 19 UI primitive rows now have selected source contracts recorded; none is credited as exhaustive dependency behavior or runtime acceptance. Media's 75 route/control rows include eight explicitly partial mappings. Dynamic schemas/plugins, vendor/framework internals, generated helper contracts and exhaustive API failure branches remain documented source-depth limits. The five-dialog supplement does not silently claim other maintenance dialog interiors such as Split/Merge/Redetect were inspected.

## Coverage and acceptance register

All rows are **source comparison recorded; rendered and end-to-end acceptance open**, except where a report explicitly marks an imported component or conditional behavior not yet inspected. Coverage is per family here; the domain reports expand its menus, controls and conditional states. Discovery JSON is the line-level cross-check for overlooked controls, not proof of manual review.

| User-facing family | Owner report | Subsurfaces that must be accepted together |
|---|---|---|
| Shell and navigation | Requests/shell | Rail/expanded/hover/mobile drawer; branding; library children and pins; primary-menu customization; plugin links; profile flyout; selected/unread states; Back/cold entry; keyboard/focus; toast/global chrome |
| Login and signup | Account/settings | Server selection/branding; credential/provider/OAuth/device-code modes; reveal/validation/pending/error; recovery/signup/invite links; required-password transitions |
| Recovery/reset/choose password | Account/settings | Capability gates, reset lookup, expired/replaced/used/error states, confirmation, uncertain settlement and retry, logged-in/sign-out path, branded layout and return |
| Invite/activation/household | Account/settings | Lookup/loading/expired, account/device identity and match code, approve/deny/claim, first profile setup, deep-link handling |
| Profile picker/editor/PIN | Account/settings | Empty/sole/multiple/kids/primary/PIN; create/edit/delete; avatar presets/upload; content/advisory/library/quality limits; validation and scope |
| Taste seeding and compact home customization | Account/settings | First/returning picker, favorites/defaults, selection/loading/paging/dismissal; distinct customize-home route, editor and reset |
| Home | Browse | Every section type, layout/skeleton/error/empty, continue/next-up/dismiss/drop/Undo, refresh/realtime identity and scroll, feature tour, card actions |
| Libraries | Browse | Home/recommended/browse/collections tabs; all media types; audiobook Now Listening; filters/sort/view/preferences, paging/scroll/cancellation |
| Catalog and Search | Browse | Library/search/filter/favorites/watchlist/history/user and server collections; people and Request to add; typed filters/group editor; quick-search rows/loading/keyboard; independent paging and tab counts |
| Personal favorites/watchlist/history | Browse | Current sidebar route, library/external watchlist tabs and actions, source authority, row/card states, delete/drop/reset and recovery |
| Collections | Browse | Capability-filtered templates, manual/smart/imported, editor/tree groups/scopes/artwork, import/source edit/resync/delete, wizard preview/navigation/validation |
| Recommendations | Browse | Hub and every kind/key section, shared rails/grid, explanation/taste, paging/empty/error/unavailable |
| Person | Browse | Portrait/bio/roles and filmography, biography expansion, lists/card actions, unavailable/transient/retry |
| Calendar | Browse | Empty/dense/month navigation, today/filters/library scope, day/event popovers/actions, dates/time settings, missing/past/upcoming metadata |
| Notifications | Browse | Unread/count/read/all/filter/dismiss, item/request/party targets, timestamps/realtime/empty/error/loading |
| Requests hub/Discover/Yours | Requests/shell | Compact header/shared search, tabs/count, rails/explore/brand variants, status-list ordering and row details/help/download/cancel, all empty/error/retry/loading states |
| Request browse/search/external cards | Requests/shell and Browse | Center Request, watchlist corner, Library/status overlay, pending/focus/disabled, shared size/caption, missing art/scrim/dimming, append paging/sentinel/retry |
| External title and season request | Requests/shell | Shared hero/actions, accessible/inaccessible/transient library promotion, follow/watchlist/cancel, download, external links, seasons/cast/recommendations; whole series/default/explicit/latest/upcoming/all-season picker |
| Movie/series/season/episode details | Media/playback | Shared hero/title/logo/metadata/scores/overview, authoritative Play/Resume/party target; user actions/more/version/audio/subtitle/info/trailer/extras; seasons/episodes/availability/markers |
| Audiobook details/player | Media/playback and Browse | Narrator/recording/chapter/related, library Now Listening, mini/expanded/cover/settings/speed/sleep/volume/seeks/queue, active Pause and keyboard/error states |
| Ebook/manga details/readers | Media/playback | Formats/download/open, EPUB/PDF renderer toolbar/TOC/search/bookmarks/appearance/progress/resume/errors; native comic equivalents where applicable |
| Video playback | Media/playback | OSC toolbar/timeline/title/volume/fullscreen/PiP/stop/minimize, settings and all nested quality/audio/subtitle menus, buffering/error/recovery, skip intro/credits/Undo, next episode/end behavior, remote controls |
| Watch Party | Media/playback | Hub/recent/create/join/invite, detail Start party sheet, room/lobby/ready/playing, browse/Together/Watchlists/Recent, series picker/member watched/spoilers, people/activity/chat/queue/permissions/reconnect |
| Settings index and shell | Account/settings | Grouping/icons/route defaults, side rail/content container, save/cancel/unsaved guards, unknown/inherited/locked/supported states |
| Settings Account/Profiles/Devices | Account/settings | Credentials/deletion/session/recovery; all profile editor fields; device overrides/inheritance/reset/forget/appearance and local-only settings |
| Settings Interface/Accessibility | Account/settings | Poster/caption/layout/primary-menu/behavior; date/time/motion/health/keyboard/focus; current theme ownership |
| Settings Playback/Subtitle appearance | Account/settings and Media/playback | Auto-play/intro/quality/audio/subtitle/seek/resume/volume; all appearance fields/palettes/text opacity/defaults/locked/device differences |
| Settings Libraries/Home/Card overlays | Account/settings | Visibility/sorts/language exceptions, section/recipe editor/gallery/reset/transfer/mapping, General quick actions and master badges, every badge/position/accent/default restoration |
| Settings Imports/Providers/Webhook sync | Account/settings | Allowed actor/profile/source/import steps/conflicts/results; provider activation/config/connect/reconnect/options/drop; webhook mappings/events/rotate/delete/setup/copy/details |
| Settings Requests/Notifications/Connect apps/Personalize | Account/settings | Auto-request inheritance/manifest gates, notification channels/reasons/frequencies/verification/secret dialogs, supported clients/connection copy, feature tour/replay |
| User plugin surfaces | Account/settings and shell | Enabled installation/manifest grants/navigation/embedded frame and declared user settings. Plugin-generated content cannot be certified from the server tree alone; enumerate actual enabled installations without exposing credentials |
| Native-only destinations | Respective domain | Server connection, Downloads/Sessions/desktop decoder options and other additional surfaces must be labeled and checked. Absence of a WebUI route is not permission to claim visual equivalence or remove useful functionality without a scoped decision |

`/setup` and `/admin/*` server administration are excluded from this user-facing pass. Admin/curator actions appearing within an ordinary user's page are inventoried as conditional controls; their authorized-user presentation must still be checked. Legacy aliases (`/search`, `/browse`, `/favorites`, `/watchlist`, `/history`, request-detail paths, retired settings paths) are navigation acceptance cases, not automatically distinct visual pages.

## Required completion gate

For **each** page, imported menu/dialog and discoverable action, store:

1. Official source SHA, candidate/source fingerprint, fixture/scenario identity and capability/profile/theme settings.
2. WebUI and native screenshots using identical data, images and content viewport dimensions. Record Windows scaling and browser zoom separately; compare narrow/medium/wide layouts and 100%/150%/200% DPI where supported.
3. Measured placement, size, spacing, radius, resolved foreground/background/border colors, font family/weight/size/line height/tracking, icon paths/strokes, shadows, cropping/gradients and transitions. Color-token names alone are insufficient.
4. Normal, hovered, pressed, keyboard-focused, disabled, pending, selected, menu-open, validation, loading, empty, partial, unavailable and error states. Include long/absent text, missing/expired art, touch/coarse pointer and reduced motion where relevant.
5. Actual input/navigation/state/API evidence: click and keyboard behavior, payload/authority/capability gates, cancellation/close/retry, paging/scroll restoration, live updates and multi-profile safety. Use isolated writable fixtures; observe live production read-only unless a specific mutation is authorized.
6. Every difference resolved and rerendered, or explicitly retained as an accepted exception with its reason. No implicit platform exceptions and no “substantial”/“implemented” status standing in for acceptance.

Keep these independent status columns: **source reviewed / visual accepted / functional accepted / regression protected**. A page can be finalized only when all applicable gates pass. Counts of tests, routes or source controls never substitute for those gates.

## Repair order after completing source triage

1. Repair missing reachable actions and different mutation contracts first: current external Watchlist access, request-card watchlist, confirmation, request/notification targets, missing detail/party actions and capability-gated settings.
2. Align shared primitives and shell: fonts/tokens already implemented must be verified; exact icon assets, buttons, hit targets, cards/captions, input/select/tabs, popover/menu/dialog chrome, focus/pending/disabled behavior. These changes affect many pages and need representative regression captures before broad rollout.
3. Rebuild Requests around the current shared components/layout. Treat hub, Yours list, search entry, full grids, cards, title and season dialog as one acceptance package; fixing only Request's position would leave major mismatches.
4. Apply shared patterns to Home/catalog/library/search/collections/recommendations/person/calendar/notifications, with scroll and refresh stability preserved.
5. Finish detail/media/player/reader/party and account/settings surfaces using the detailed domain findings; preserve native decoding/direct play and feature behavior while matching the user controls.
6. Run the complete matched-render and interaction matrix, then fix every residual discrepancy before marking any area complete. Every identified mismatch remains tracked even if it is visually small.

This is a repair sequence, not a new claim that prior features must all be rewritten. It does not authorize installation, closing playback, GitHub publication or production changes. The user's requested comparison is the active scope; implementation findings remain open until repaired and verified.

## Consolidated repair packages

This index groups the detailed findings so the work can be executed and reported by package. Finding IDs are report-local: account F01 and media F01 are different findings. Counts across reports are not unique defect totals because shared controls and their page consequences overlap. Every cited finding retains its evidence and acceptance requirements in its domain report.

| Order | Package / concrete outcome | Finding references |
|---|---|---|
| 1 | Restore reachable external Watchlist and request-notification title navigation | Browse B10, B40 |
| 2 | Align shared control defaults, icons, profile flyout and Back chrome; preserve transparent poster hit targets | Shared C01–C05; Requests/shell SH-01–SH-03 |
| 3 | Match the complete Requests hub/Yours/discovery/card/title/season workflow, including centered Request, corner Watchlist, confirmation, progress and append paging | Requests RQ-01–RQ-22 |
| 4 | Use current typed/guided/advanced filter contracts across catalog, library, search, collections and smart preview | Browse B07, B11–B16, B31–B32; Account F24 |
| 5 | Align Home/library hero, cards, remembered state, audiobook grouped-load replacement, Now Listening and browse recovery | Browse B01–B06, B08–B09, B17, B45 |
| 6 | Match quick-search dialog, direct-play overlay and person artwork | Browse B18–B20 |
| 7 | Finish collection/template/import/editor layout and save/reorder/source/artwork behavior | Browse B21–B30, B33 |
| 8 | Match Person, Calendar and Notifications layout, actions, state and recovery; retain conditional card menu actions | Browse B34–B39, B41–B44; conditional dialog report |
| 9 | Finish Watch Party detail entry, start sheet, lobby/readiness, recent/rejoin, selection/suggestions, member state and room rail | Media F01, F06–F15, V10–V11 |
| 10 | Finish TV/detail/audiobook/narration/mini-player/manga layouts and active transport; match theme-music fades | Media V01–V06, V08–V09, F02–F03, P01; Browse B03–B04 |
| 11 | Finish video compact/Fill controls and reader narrow layout/settings failure, write coalescing and local recovery | Media V07, V12, F04–F05, F16 |
| 12 | Match Settings shell/search/rows, current device keys/capabilities/inheritance, interface choices, subtitle sections, overlay masters/default reset, Home transfer/customization, custom language tags, device lists, provider schemas and preview fidelity | Account F01–F10, F17–F22, F25–F26 |
| 13 | Match branded authentication/recovery/reset routes, profile picker/editor, taste seeding and tour save barriers | Account F11–F16, F23 |
| 14 | Match conditional metadata/person maintenance and manga-file dialog interiors reachable on ordinary pages | Conditional D01–D10; Browse B44 and Media V09 overlaps |

After each package: report the repaired items and the actual visual/functional evidence, retain unrelated open findings, and update the per-surface acceptance state. Do not use another global “substantial” label in place of these checks.

## Audit artifact verification

Final checks passed across seven audit documents: **240 distinct official source files and 89 native linked files**, with no missing references or out-of-range linked lines. Local relative report links and documentation whitespace checks also passed; `git diff --check` passed for the tracked documentation changes. These checks validate evidence locations and document integrity; they are not application tests or visual acceptance. All source and rendered limitations above remain open.
