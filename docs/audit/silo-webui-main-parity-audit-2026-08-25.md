# Silo Desktop vs. Silo Server WebUI — current `main` parity audit

**Audit date:** 2026-08-25

**Desktop baseline:** `ellermw/SiloDesktopApp@b322ddc9088571f58608071585ce7e48fb2674ca`

**Server/WebUI baseline:** `Silo-Server/silo-server@20ae82ae05edcfef151a02738e323cf1a97034ef`

**Scope:** Source-level parity, API/workflow alignment, visual-contract review, security, performance, logic, dead-code and maintainability review.

**Out of scope:** Modifying application code; claiming pixel-perfect parity without an installed Windows runtime comparison.

## Executive summary

The desktop application has broad feature coverage. All current WebUI route families have either a desktop page or a defensible native equivalent, and the current source contains many features that older audit documents still call missing. It is not yet supportable to call the application a 1:1 copy.

The largest remaining gap is verification, not route count. The desktop README correctly keeps most major surfaces at **Substantial** or **Functional; visual parity incomplete**. Source inspection cannot prove pixel geometry, focus order, responsive breakpoints, animation, virtualization behavior, image timing, or playback behavior in an installed WinUI application.

The highest-priority current risks are:

1. **Known runtime regressions remain open:** Movies > Library scrolling can freeze; 4K HDR playback can stop/pause; Admin Scan All can stall; and Playing Next close/restoration still requires confirmation (`AGENTS.md:539-543`, `AGENTS.md:640`, `README.md:31`).
2. **The parity evidence is internally inconsistent:** `docs/audit-workflow.md` and `docs/audit-baseline.json` still point at the legacy Continuum/GitLab source and old SHAs, while current policy prohibits that source. This can misdirect future audits.
3. **Performance-sensitive image paths bypass the shared loader/cache:** at least 17 direct `new BitmapImage(new Uri(...))` constructions remain, including request browse/detail, global search, profile editing, item trailers, and watch-tonight surfaces.
4. **Failure observability is weak:** 163 compact empty `catch { }` sites exist. Many are valid best-effort cancellation/disposal paths, but others surround page loads, admin refreshes, image work, and player operations where silent failure can turn real regressions into blank or stale UI.
5. **Maintainability is limiting auditability:** major pages and services are extremely large code-behind units. `ItemDetailPage.xaml.cs`, `SettingsPage.xaml.cs`, `PlayerService.cs`, and several admin pages combine transport, mapping, state, view creation, and interaction logic. This increases regression probability and makes meaningful parity review harder.
6. **Visual drift remains plausible:** the desktop uses 343 literal XAML colors in addition to shared resources, while the WebUI is theme-token driven. Literal values are not automatically wrong, but they create a large surface that will not follow upstream theme/token changes.
7. **Two trust-boundary hardening gaps are confirmed:** server-supplied activation URLs are shell-opened without an HTTP(S) allowlist, and active EPUB content runs in a broadly mapped WebView origin without explicit navigation/origin restrictions.

No new confirmed credential disclosure, command injection, path traversal, or authorization bypass was found in this source-only pass. That is not a penetration-test result.

## Method and confidence

This audit used fresh isolated checkouts of both repositories and pinned the exact commits above. It reviewed:

- the WebUI route tree in `web/src/App.tsx`;
- WebUI page components, layouts, navigation definitions, query hooks, shared components, theme/design tokens, and server API route registrations;
- all 63 desktop XAML pages, their code-behind, view models, API clients, services, controls, resources, tests, and existing audit records;
- route, navigation, settings, media-card, detail, player, realtime, admin, authentication, and error-state contracts;
- static security/performance/maintainability patterns.

Quantitative inventory at the audited commits:

| Inventory | Count |
|---|---:|
| Desktop XAML page files | 63 |
| Desktop C# files | 182 |
| Desktop XAML files | 90 |
| Non-test WebUI files under `web/src/pages` | 217 |
| Desktop test attributes found statically | 741 |
| Compact empty catches in desktop source | 163 |
| Direct `BitmapImage(Uri)` constructions | 17 |
| Literal hex colors in desktop XAML | 343 |

### Important limitations

- Ubuntu cannot build, launch, or inspect the WinUI application. Build/test claims from another machine were not re-certified here.
- Visual conclusions are contract comparisons, not screenshots. Every item marked **runtime validation** belongs in the Windows side-by-side pass.
- The WebUI has many subcomponents per route; a one-to-one source-file count is meaningless.
- CodeRabbit authentication is healthy, but a fresh whole-tree CodeRabbit run failed twice at the service handshake with `WebSocket closed`. The previously committed CodeRabbit audit and remediation evidence were reviewed, but this report does **not** claim that a new CodeRabbit pass succeeded. The parity findings below come from direct source analysis.
- Older `docs/audit-gaps/FULL-AUDIT-*` files are historical leads, not current truth. Findings were not carried forward unless current source or current project documentation supported them.

## Overall disposition

| Area | Current source coverage | 1:1 status | Main reason not complete |
|---|---|---|---|
| Authentication, activation, signup, setup, profiles | Broad | Runtime validation | Installed visuals, OAuth/device-flow and recovery QA |
| Shared shell/navigation/search | Broad | Runtime validation | Responsive geometry, focus, animation, Playing Next restoration |
| Home/catalog/library/search | Broad | Not complete | Known library freeze, literary browsing details, responsive/card tuning |
| Item/person details | Broad | Not complete | Installed comparison and complex media/edition/episode edge cases |
| Audiobook/ebook/manga | Broad | Not complete | Format compatibility, reader behavior, image/metadata detail |
| Collections | Broad | Not complete | Template/guided-rule/import/collage/scheduling detail |
| Requests/recommendations/calendar/notifications | Broad | Runtime validation | Current-server edge cases and visual tuning |
| User settings | Broad | Runtime validation | Upstream drift, device-specific behavior, focus/responsiveness |
| Admin suite | Broad | Not complete | Explicit visual-parity-incomplete status, provider/policy edges |
| Video player | Broad | Not complete | Known 4K stop/pause risk and real-media geometry/focus validation |
| Watch Together | Broad | Not complete | Installed multi-client synchronization and recovery testing |

## Route and page comparison

### Public/authentication surfaces

| WebUI route | Desktop equivalent | Disposition | Validation still required |
|---|---|---|---|
| `/login` | `LoginPage` | Covered | Branding, provider buttons, keyboard flow, errors, token refresh |
| `/login/oauth-complete` | OAuth completion handled inside login/native callback flow | Native equivalent | Success/cancel/expired callback and app restart |
| `/activate` | `ActivateDevicePage` | Covered | QR/code timing, polling cancellation and expired code |
| `/setup` | `SetupWizardPage` | Covered | Current eight-step order, responsive form geometry, error recovery |
| `/signup` | `SignupPage` | Covered | Policy/provider combinations and exact validation copy |
| `/invite/:token` | `InviteClaimPage` | Covered | Invalid/expired/used invite states |
| `/household-setup` | `HouseholdSetupPage` | Covered | Branding/background loading and role/profile edge cases |

The old setup-wizard audit is stale: current desktop source contains Integrations, Downloads, and Recommendations steps. Do not re-file those as missing.

### Profile, playback and reader entry surfaces

| WebUI route | Desktop equivalent | Disposition | Validation still required |
|---|---|---|---|
| `/profiles` | `ProfilesPage` | Covered | PIN, impersonation, avatar fallbacks, resume after restart |
| `/taste-seed` | Taste/personalization surface in settings | Covered, relocated | Exact cards, selection feedback and completion navigation |
| `/watch/:id` | `WatchPage` + native player/overlay | Broad | Real HDR/DV, multi-version, markers, PiP/fullscreen, failure recovery |
| `/reader/ebook/:contentId` | `EbookReaderPage` | Broad | EPUB/PDF/MOBI/AZW/CBZ/CBR/FB2 behavior and progress persistence |

### User application surfaces

| WebUI route/family | Desktop equivalent | Disposition | Notable remaining work |
|---|---|---|---|
| `/` | `HomePage` | Broad | Installed hero/rail/card comparison; incremental refresh edges |
| `/catalog` | `CatalogPage` | Broad | Narrow layouts, filter population cost, literary media |
| `/library/:id` | `LibraryPage` | Broad, risk | Reproduce/fix known scrolling freeze; stress filters/images/virtualization |
| `/search`, `/browse` redirects | Global search + `SearchPage`/catalog navigation | Native equivalent | Result grouping, keyboard/focus and empty/error states |
| `/item/:id` | `ItemDetailPage` | Broad | Episode/sibling navigation, edition/version, extras, metadata geometry |
| `/person/:id` | `PersonDetailPage` | Broad | Filmography grouping, images, empty biography/credits |
| `/rooms/:roomId`, `/rooms/join` | Watch Together pages/view models | Broad | Multi-client sync, reconnect, host transfer and auto-start |
| Favorites/watchlist/history redirects | Dedicated desktop pages and shared library modes | Native extension | Counts, pagination, navigation state and server ordering |
| `/collections` | `CollectionsPage` | Broad | Source grouping, imports, artwork/collage and scheduling |
| collection create/edit/detail | Collection editor/detail surfaces | Broad | Template and guided-rule equivalence; invalid rule recovery |
| `/requests` and detail | `RequestsPage`, `RequestDetailPage` | Broad | Per-target states, router-schema forms and image loading |
| request browse facets | `RequestBrowsePage` | Covered | Studio/network/genre routing, logos, empty states |
| recommendations + section routes | `RecommendationsPage` | Broad | Ranking labels, pagination and seed/empty states |
| `/calendar` | `CalendarPage` | Covered | Locale/date, timezone, dense/narrow layouts |
| `/notifications` | `NotificationsPage` | Covered | Realtime/pagination/read-state recovery |
| `/profile/customize-home` | Home-screen settings/customization | Covered, relocated | Drag/reorder, scope, save conflict and disabled sections |

Desktop-only Downloads, Favorites, History, Search, Server Select and Watchlist pages are acceptable native extensions. They should not change server semantics or hide the WebUI-equivalent entry paths.

### User settings

The WebUI exposes individual nested routes; desktop consolidates them into a single searchable settings workspace. This is an acceptable native information-architecture difference if the content and behavior remain equivalent.

| WebUI setting | Desktop section | Disposition |
|---|---|---|
| Appearance | Appearance | Covered |
| Interface | Navigation & Cards | Covered |
| Theme editor | Theme Editor | Covered; runtime preview/reset validation |
| Accessibility | Accessibility | Covered |
| Playback | Playback | Covered; desktop adds native HDMI passthrough |
| Profiles | Profiles | Covered |
| Libraries | Libraries | Covered |
| History import | History Import | Covered |
| Webhook sync | Webhook Sync | Covered |
| Watch providers | Watch Providers | Covered |
| Subtitle appearance | Subtitles | Covered |
| Home screen | Home Screen | Covered |
| Card overlays | Card Overlays | Covered, including `edition` in current source |
| Personalize | Personalize | Covered |
| Devices | Your Devices | Covered |
| Notifications | Notifications | Covered |
| Connect apps | Connect Apps | Covered |

Desktop also exposes Plugins and Sessions/account functions in this workspace. Verify these are intentional native extensions and that permissions/capability gates match the server.

### Admin navigation and pages

The route coverage is strong. Current desktop navigation includes all current WebUI groups and items: Overview, Content, Automation, Users, and System. The desktop XAML labels some group headings differently (`SERVER` where the WebUI uses `SYSTEM` and places automation items), even though code later reorders navigation. This needs installed verification because source-time order and runtime order differ.

| WebUI admin surface | Desktop equivalent | Disposition | Remaining focus |
|---|---|---|---|
| Dashboard | `AdminDashboardPage` | Broad | Explicitly not visually 1:1; live/scan/activity geometry |
| Activity | `AdminActivityPage` | Broad | Explicitly not visually 1:1; table width, actions, realtime status |
| Logs | `AdminLogsPage` | Broad | Stream restart/reconnect, FFmpeg focus and detail sheets |
| Diagnostics | `AdminDiagnosticsPage` | Covered | Upload detail, redaction, empty/error states |
| Libraries | `AdminLibrariesPage` | Broad, risk | Explicitly not visually 1:1; Scan All stall; large rows/diagnostics |
| Collections + editor | `AdminCollectionsPage` + editor | Broad | Exact source-specific imports, templates, artwork/collage |
| Sections | `AdminSectionsPage` | Broad | Recipe-specific controls and drawer geometry |
| Requests | `AdminRequestsPage` | Broad | Plugin-schema-specific forms and target error states |
| Autoscan | `AdminAutoscanPage` | Broad | Event/reconnect behavior and high-volume tables |
| Scheduled Tasks + detail | `AdminTasksPage`, `AdminTaskDetailPage` | Broad | Run history, cancellation, task-specific metadata |
| Subtitles | `AdminSubtitlesPage` | Broad | Ten-column layout, paging, narrow width and actions |
| Marker History | `AdminMarkerHistoryPage` | Broad | Filtering, paging, item/log links |
| Recommendations | `AdminRecommendationsPage` | Broad | Capability/provider states, jobs and connection tests |
| Users + detail | User admin pages | Broad | Invite/access/profile interactions and permission gates |
| Access Groups | `AdminAccessGroupsPage` | Broad | Responsive editor and inherited/default semantics |
| Devices + detail | `AdminDevicesPage` | Broad | 1920px console, saved views, keyboard/link refinements |
| Playback History | `AdminPlaybackHistoryPage` | Broad | Deep-link filters, polling and large history paging |
| History Import | `AdminHistoryImportPage` | Broad | Source-specific auth/mapping/run recovery |
| Settings | Admin settings shell/detail | Broad | Provider/integration edges, token preview and upstream drift |
| Plugins + dynamic settings | Plugin pages/settings | Broad | Schema rendering, auth, update and failure rollback |
| Policy | `AdminPolicyPage` | Capability-gated | OPA/vendor edge cases, simulation/error output |
| Nodes | `AdminNodesPage` | Broad | Realtime/load/capacity and command failure feedback |
| API Keys | `AdminApiKeysPage` | Broad | Secret reveal/copy lifetime, paging and role/tier gates |
| Maintenance | `AdminMaintenancePage` | Broad | Destructive confirmation, progress, cancellation and recovery |

The WebUI `/admin/stats` route is only a redirect to Dashboard and needs no separate desktop page.

## API and workflow alignment

### Positive findings

- Desktop API clients cover the major auth, profile, catalog, library, item, playback, collection, request, settings, notification and admin contracts.
- Current desktop source includes modern server concepts that old audit files miss: recommended-for-you sections, edition overlays, theme token editing/preview, auto-switched stream detail, current setup steps, history import, plugin settings, policy gating and device overrides.
- Player progress/session heartbeats and admin realtime streams are explicitly represented.
- Capability-gated routes such as Policy are not blindly displayed when unsupported.
- OAuth completion is adapted to a native callback rather than copied as a browser page, which is appropriate.

### Findings and required validation

#### P0 — known playback/session reliability is not closed

Project instructions still identify random stop/pause during 4K HDR playback. The player is a stateful combination of mpv callbacks, HTTP/HLS proxying, WebSocket progress, markers, subtitles, watch-together coordination, fullscreen/PiP and shell restoration. A source-only review cannot clear this.

Required Windows validation matrix:

- direct play, remux and transcode;
- SDR, HDR10, Dolby Vision profiles supported by the target machine;
- one-hour-plus playback with seeks, pause/resume and track switches;
- server reconnect and short network interruption;
- EOF vs temporary cache stall;
- app minimize/restore, fullscreen and PiP transitions;
- progress heartbeat continuity and exactly-once session stop;
- next-episode preview, credits countdown, Playing Next close and shell return.

Capture client logs, server playback session events and mpv property/event traces together. Do not “fix” this by changing heartbeat or EOF behavior without reproducing the protocol sequence.

#### P0 — library scroll freeze remains a release blocker

The project still records a reproducible Movies > Library scrolling freeze and notes UI-thread image/control realization as a continuing risk. Current source has substantial virtualization and cancellation work, but that does not supersede the open runtime report.

Validate with large libraries and aggressive repeated scrolling while varying:

- poster density and card presets;
- overlays on/off and metadata captions;
- populated genre/studio/network/language filters;
- cold vs warm image cache;
- resizing and switching tabs/sort/filter during load;
- remote/slow image responses and failed URLs.

Collect UI-thread stalls, realized element count, image decode count, memory growth and outstanding cancellation/load tasks.

#### P1 — Scan All and long-running admin work need end-to-end cancellation tests

Admin Libraries has separate scan/metadata work rows and queue state, but the project still records Scan All stalls/freezes. Validate that starting, polling, reconnecting, cancelling and leaving the page cannot block the UI thread or orphan subscriptions. Confirm progress remains monotonic and stale events from an earlier operation cannot overwrite a newer scan.

#### P1 — direct remote image construction bypasses shared policy

At least 17 direct `BitmapImage(Uri)` constructions remain. Notable surfaces include:

- `Views/RequestsPage.xaml.cs`
- `Views/RequestBrowsePage.xaml.cs`
- `Views/RequestDetailPage.xaml.cs`
- `Views/ItemDetailPage.xaml.cs` (YouTube thumbnails)
- `Controls/GlobalSearchDialog.xaml.cs`
- `Controls/WatchTonightDialog.xaml.cs`
- profile/household and audiobook controls

Risks: unbounded decode dimensions, duplicate requests, inconsistent cancellation, stale-image races after recycling, no negative caching, and UI-thread work. Route all high-cardinality remote images through the shared loader/cache with decode sizing, cancellation and placeholder/error behavior. Local-file previews may remain direct where appropriate.

#### P1 — API drift detection is weaker than feature coverage

The desktop has a large hand-written API surface, while the WebUI consumes typed query hooks close to the server source. Existing parity tests often assert strings, route names or source structure; they do not demonstrate that payloads deserialize correctly against the current server.

Add contract fixtures or generated-schema checks for high-risk payloads:

- playback decisions, versions, chapters/markers and session events;
- admin Activity and Logs realtime messages;
- library scan/queue/diagnostic responses;
- settings values with optional/new fields;
- plugin-provided schemas and request-router configuration;
- history import sources/runs/mappings;
- device overrides and access-group inheritance.

Unknown JSON fields should remain forward-compatible; missing required fields should produce a visible, diagnosable error.

#### P2 — consolidated native navigation must preserve deep-link semantics

Settings and some redirected WebUI routes are consolidated or relocated in desktop. Verify that notifications, item links, admin log links and restart restoration can still select the exact nested section. A route being “present somewhere” is not parity if external navigation cannot reach the relevant state.

## Visual and interaction comparison

### Shared shell

- WebUI app sidebar is 260px and collapses to a 64px offset; desktop documentation and source target the same dimensions.
- WebUI AdminSidebar is 240px (`AdminLayout` uses a 240px desktop offset), not 260px. Validate the desktop admin shell against 240px before treating the shared 260px shell requirement as universal.
- Validate selected indicator size/location, icon stroke/size, group label casing/spacing, avatar treatment, library pin ordering, notification/activity badges, mobile/narrow behavior and immersion-rail timing.
- Runtime navigation must match visual order after the desktop's code-behind reordering; inspecting XAML alone is insufficient.

### Theme and design tokens

The WebUI primarily derives colors, radii and surfaces from CSS variables and component variants. Desktop has a shared `DarkTheme.xaml`, but 343 literal XAML colors remain. Audit each literal into one of:

The canonical desktop `cobalt-studio` resource values were compared directly with the WebUI theme block. The core background, foreground, card, popover, primary, secondary, muted, accent, destructive, border, input, ring, chart, sidebar and surface hex values match exactly (case aside); both use Outfit and a 12px base radius. The desktop user shell is 260px/64px, matching WebUI, and its admin shell is 240px, matching `AdminLayout`/`AdminSidebar`. This is strong token/geometry evidence for the base theme—not proof that each control consumes the correct resource or that hover/focus/animation states render identically.

1. required semantic/status color;
2. media-specific branding color;
3. exact upstream token snapshot;
4. accidental drift that should use a resource.

Compare at minimum background tiers, panel/border opacity, muted text, accent/focus, danger/warning/success, badge variants, hover/pressed/selected states, disabled opacity and high-contrast behavior. Theme parity includes light/custom themes and live preview, not only the default dark screenshot.

### Cards, images and metadata

For every poster, landscape, square/audiobook and hero variant validate:

- aspect ratio and crop (`cover`/uniform-fill equivalence);
- decode resolution and placeholder/error image;
- title truncation/wrapping and reserved caption height;
- year, duration, rating, media type, episode numbering and progress;
- watched/favorite/watchlist state;
- HDR/resolution/audio/container/edition/multi-audio/subtitle overlays;
- overlay corner, stacking, collision and theme styling;
- hover/focus/selection geometry and controller/keyboard activation;
- skeleton-to-image transition without card reflow or flashing;
- stale-image protection when virtualized cards are recycled.

The current desktop source includes the `edition` overlay and current overlay registry. The older audit claiming it is absent is stale.

### Page geometry

The Windows comparison should record viewport size and scale, then compare:

- shell/content offsets and maximum content width;
- header baseline, breadcrumb/back placement and action alignment;
- section spacing and rail/card gaps;
- table column widths, sticky headers and internal scrolling;
- drawer/sheet/modal width, overlay, close target and focus trap;
- empty/loading/error/permission-denied states;
- narrow window wrapping and minimum widths;
- 100%, 125%, 150% and 200% display scaling;
- reduced motion and high contrast.

Screenshots of only the happy loaded state are insufficient. Capture loading, empty, error, long text, missing artwork, many badges, active operation and destructive confirmation states.

## Security review

### Positive observations

- Authentication tokens are handled through dedicated auth/config services rather than intentionally embedded in repository source.
- Native OAuth/device flow is separated from ordinary page navigation.
- Admin/policy/plugin surfaces use capability or authorization-aware navigation.
- API-key and provider credential flows use password-style controls in relevant places.
- No committed token value was found during this review.

### Security findings

#### S1 — stale audit instructions violate the current source-of-truth boundary (high process risk)

`docs/audit-workflow.md` directs developers to a legacy `continuum-server`, an absolute `F:\continuum-server` path and GitLab. `docs/audit-baseline.json` retains `continuum_server_sha` and numerous stale missing/partial statuses. Current `AGENTS.md` explicitly forbids using the legacy GitLab repository.

Impact: a future agent can compare against unauthorized/stale code, implement obsolete behavior, or leak environment-specific paths into reports. Replace the workflow and baseline with the GitHub `Silo-Server/silo-server` reference and current SHA scheme. Until fixed, README/current source must override them.

#### S2 — swallowed exceptions can conceal security-relevant failures (medium)

The 163 compact empty catches include benign cancellation/disposal paths, but the same pattern also occurs in admin pages, login/player/page loads, history import and image handling. Silent failures can hide failed authorization refresh, incomplete destructive operations, stale admin data or dropped realtime state.

Classify each catch:

- expected cancellation/disposal: narrow exception type and comment;
- best-effort UI decoration: low-level diagnostic with no secret-bearing values;
- network/auth/mutation: visible error or structured telemetry;
- invariant violation: do not suppress.

Never log passwords, refresh/access tokens, provider secrets, API-key values, signed media URLs or full credential-bearing request bodies.

#### S3 — embedded reader/web content requires runtime boundary testing (medium)

The ebook reader executes script in WebView content and processes multiple archive/document formats. Validate that extracted content cannot escape its cache root, navigate to privileged/local origins, invoke unintended host objects, or persist active script beyond the intended reader sandbox. Test malformed archives, `../` entries, oversized/compressed-bomb inputs, remote resources and script-bearing EPUB content.

#### S4 — destructive admin commands need uniform confirmation and idempotency (medium)

Maintenance, scan cancellation, node/device/session controls, key deletion, user/invite changes and collection/library deletion should all require the same authorization semantics as WebUI, expose an unmistakable destructive state, disable duplicate submission, and reconcile after timeout. A client timeout must not invite a blind second destructive request.

## Performance review

### P1 — large UI code-behind and runtime object construction

Several high-traffic pages construct controls and data presentation imperatively. Combined with large source files and remote images, this makes it easy to perform layout, mapping, filtering and image initialization on the UI thread. The documented library freeze confirms this is not theoretical.

Recommended direction (incremental, not a rewrite):

- keep view models as immutable/light display records;
- move mapping/filtering off the UI thread;
- use templates and virtualization-safe recycling instead of rebuilding trees;
- centralize image loading/decode/cancellation;
- measure allocations and frame stalls before/after each change;
- preserve stable item keys and scroll position during refresh.

### P1 — lifecycle/realtime work needs one ownership model

Activity, logs, notifications, watch together, playback and scan progress all combine polling, WebSockets/event channels, cancellation sources and page navigation. Require a consistent contract: one owner, one generation/version, cancellation on unload, stale-result rejection after await, serialized restart and bounded retry with visible connection state.

### P2 — parallel requests are useful but error handling must be partial-result aware

The source frequently starts multiple tasks and reads `.Result` after `Task.WhenAll`, which is valid after successful completion. When one optional request fails, `WhenAll` can discard otherwise useful results unless explicitly handled. Settings/admin/dashboard pages should distinguish essential from optional data and render partial content with a targeted warning.

### P2 — literal and repeated UI construction increases startup/page cost

Repeated brushes, converters, menu rows, badges and dynamically constructed controls should be profiled and, when measurable, shared through resources/templates. Do not refactor purely to reduce line count; prioritize measured hot paths.

## Code quality and maintainability

### Q1 — oversized mixed-responsibility units (high)

`ItemDetailPage.xaml.cs`, `SettingsPage.xaml.cs`, `PlayerService.cs` and several admin code-behind files contain multiple independent domains. Consequences include merge conflicts, broad regression blast radius, duplicated formatting/mapping, and tests that fall back to source-string assertions.

Extract by behavior, keeping public behavior stable:

- item media/version/episode/collection presenters;
- settings section controllers and schema/value adapters;
- player session, mpv event, subtitle, marker, next-up and shell-state coordinators;
- admin table row mappers, filters and mutation coordinators.

Each extracted unit needs behavioral tests; file splitting alone provides little value.

### Q2 — historical audit files are being mistaken for live backlog (high)

Current source disproves several old claims: setup Integrations/Downloads/Recommendations, recommended-for-you sections, edition overlays, theme token editing/preview and auto-switched stream detail now exist. Add lifecycle metadata to audit documents: audited desktop SHA, server SHA, status, superseded-by link and whether each finding was revalidated.

### Q3 — source-shape tests overstate confidence (medium)

Tests that search XAML/C# text for labels, endpoints or method names are useful drift alarms, but they do not prove binding, navigation, serialization, permissions, focus, virtualization or runtime behavior. Label them as structural tests and supplement them with:

- pure presenter/view-model behavioral tests;
- JSON contract fixtures from the current server;
- navigation/deep-link state tests;
- Windows UI automation for critical workflows;
- long-running player/library stress tests.

### Q4 — inconsistent error presentation (medium)

Some surfaces use banners/status messages, others silently ignore failures, and some dynamically created controls cannot express a retry state. Define shared loading/empty/error/permission/partial-data patterns and use them on every page and nested panel.

### Q5 — no current-source parity manifest (medium)

The repo needs a generated or easily audited manifest mapping every current WebUI route/component/API area to the desktop page, implementation state, last compared server SHA and required runtime scenario. The existing baseline cannot serve this role while it references the wrong repository and stale states.

## Dead-code and stale-code review

No simple `TODO`/`FIXME` marker identifies an obviously abandoned current feature. The larger stale-code risk is documentation and compatibility surface:

- legacy Continuum/GitLab audit names and paths;
- historical gap reports without superseded status;
- legacy route redirects that may still be required for server compatibility;
- desktop-only navigation pages whose semantics overlap redirected WebUI filters.

Do not delete compatibility routes or native pages based only on apparent duplication. First instrument usage and confirm server notification/deep-link behavior. Documentation referring to prohibited source-of-truth locations should be corrected promptly.

## Exhaustive source-surface ledger

This ledger closes the ambiguity in the earlier route summary. Every one of the 217 non-test modules under current `web/src/pages` was inventoried. A **mapped** disposition means the behavior is represented in current desktop source; it does not mean screenshot/runtime parity has been proven.

### Entry, setup and library surfaces

| WebUI modules reviewed | Desktop evidence | Disposition |
|---|---|---|
| `ActivateDevice`, `Login`, `OAuthComplete`, `Signup`, `InviteClaim`, `HouseholdSetup` | corresponding pages/view models; `AuthApi`; `AuthService`; native callback handling | Mapped; auth recovery and browser/device flows need Windows integration tests |
| `SetupWizard`; `StepIndicator`; `WizardContext`; `setupStorage`; `AccountStep`; `ProfileStep`; `ServerStorageStep`; `IntegrationsStep`; `DownloadsStep`; `RecommendationsStep`; `LibraryStep`; `NodesFinishStep`; `useWizardSteps` | `SetupWizardPage`, `SetupWizardViewModel` | All current steps map in the same runtime order; visual/error-state validation remains |
| `Profiles`, `TasteSeed`, `ProfileCustomizeHome` | profile/taste pages and view models; Settings Home Screen/Personalize/Profile sections | Mapped; Taste/Home customization is intentionally relocated |
| `Home`; `homeSectionCache`; `homeSectionQueue`; `homeSectionState`; `homeSurfaceRefresh` | `HomePage`, `HomeViewModel`, `HeroCarousel`, `SectionRow`, shared event channel | Mapped, including incremental section loading and refresh; runtime card/image timing remains |
| `Catalog`, `catalogSearchParams` | `CatalogPage`, catalog API/view model | Mapped; query/deep-link and dense-result runtime validation remains |
| `LibraryPage`, `LibraryBrowse`, `LibraryCollections`, `LibraryRecommended`; `libraryPageSearchParams`; `librarySectionLayout` | `LibraryPage`, library modes, filters, virtual range/card binding, recommendation and collection tabs | Mapped but not cleared: the documented scrolling freeze is a release blocker |
| `Collections`, `CollectionEditor`, `SmartCollectionWizard`, `ImportedCollectionEditor`, `userCollectionsShared` | collection list/detail/editor/wizard; imported-provider and template flows | Broad mapping; invalid rules, provider failures, scheduling and artwork need end-to-end testing |
| `Calendar` | `CalendarPage`, calendar models/API | Mapped; locale/timezone/dense layout runtime validation remains |

### Item detail, requests and recommendations

| WebUI modules reviewed | Desktop evidence | Disposition |
|---|---|---|
| `ItemDetail/index`, `MovieContent`, `SeriesContent`, `SeasonContent`, `EpisodeContent`, `AudiobookContent`, `EbookContent`, `MangaContent`, `DetailHero` | `ItemDetailPage`, `ItemDetailViewModel`, `CatalogApi`, native audiobook/reader entry | Every media subtype maps; complex runtime and exact layout remain unproven |
| `ActionBar`, `DetailBreadcrumb`, `HeroCrewLine`, `MetadataBadges`, `QualityBadges`, `ScoreRow`, `SectionSkeletons` | item-detail action/hero/crew/rating/quality/loading builders | Mapped; visual state-by-state comparison required |
| `AudioTracksPopover`, `SubtitlesPopover`, `SubtitleSearchDialog`, `VersionDropdown`, `VersionFlyout` | version/edition/audio/subtitle menus, subtitle search/download and pre-play selection | Mapped; mixed-edition and server-error cases need integration tests |
| `EpisodeCarousel`, `SeasonCarousel`, `SeasonEpisodeGrid` | season/episode/sibling navigation and grids | Mapped; navigation restoration and long-series performance remain |
| `ExtrasSection`, `TrailersSection`, `TrailerModal`, `MediaInfoDialog` | extras/trailers/media-info dialogs and actions | Mapped; YouTube thumbnail loading bypasses shared image policy |
| `mediaSpecSections`, `prePlaySelection`, `selectedMediaSummary`, `versionFormatUtils`, `versionRankingUtils`, `versionSubtitleInventory`, `itemDetailLayout`, `watchedState` | native media-spec mapping, ranking, selection summary, watched-state refresh | Mapped; contract fixtures are incomplete |
| `Requests`, `RequestBrowse`, `RequestDetail`, `requestExclusivity`, `requestIntegrationMediaTypes` | request list/browse/detail, router/provider forms and target states | Mapped; several high-cardinality images bypass the shared loader |
| `Recommendations`, `RecommendationsSection` | recommendation page/section view models and API | Mapped; ranking/seed/empty/pagination runtime cases remain |
| `PersonDetail` | `PersonDetailPage`, `PersonDetailViewModel` | Mapped; empty biography/credits and filmography grouping need runtime comparison |
| `Notifications` | `NotificationsPage`, notification API/view model, shell badge/event handling | Mapped; reconnect/pagination/read-state integration remains |

### Playback, audiobook, reader and Watch Together

| WebUI modules reviewed | Desktop evidence | Disposition |
|---|---|---|
| `WatchRoute`, `watchRouteHelpers` | `WatchPage`, `PlayerService`, `PlaybackManager`, transport planner | Broad mapping; known 4K HDR stop/pause risk prevents parity sign-off |
| `AudiobookPlayer`, `CoverExpandTile`, `MiniBar`, `NowListening`, `PlayerSettingsMenu`, `SkipIcon`, `SpeedControl` | audiobook mini/expanded controls, now-listening hero, speed/sleep/skip/preferences | Mapped; long-form progress and interruption recovery need runtime tests |
| `audiobookPlaybackContext`, `smartRewind`, `useAudiobookKeyboardShortcuts`, `useAudiobookPlayback`, `useAudiobookPrefs`, `playerTestUtils` | native audiobook state/preferences, smart rewind and keyboard transport | Behavioral mapping present; cross-device preference and resume tests remain |
| `ChaptersSection`, `NarratorCard`, `NarratorPicker`, `RelatedRail` | item detail chapters/narrator/related surfaces | Mapped; imagery, focus and empty-state comparison remains |
| `EbookReader` | `EbookReaderPage`, `EbookPackageExtractor` | EPUB/PDF/FB2/comic/native reader support is broad; sandbox, format and rendering tests remain |
| `WatchTogetherJoin`, `WatchTogetherRoomPage`, `WatchTogetherSuggestionPanel` | join/room pages, room view model, coordinator and dedicated WebSocket | Mapped; multi-client sync/reconnect/host-transfer is runtime-only |

### User settings modules

| WebUI modules reviewed | Desktop evidence | Disposition |
|---|---|---|
| `SettingsLayout`; `AppearanceSettings`; `InterfaceSettings`; `ThemeEditorSettings`; `AccessibilitySettings` | consolidated Settings workspace, theme service/editor and accessibility service | Mapped; native IA difference is intentional |
| `PlaybackSettings`; `libraryPlaybackPreferences`; `SubtitleAppearanceSettings` | Playback, Libraries and Subtitle sections/dialogs | Mapped; device/player integration remains |
| `ProfilesSettings`; `LibrarySettings`; `HomeScreenSettings`; `CardOverlaySettings`; `PersonalizeSettings` | corresponding Settings sections and controls | Mapped, including current `edition` overlay |
| `HistoryImportSettings`, `HistoryImportSettings.utils` | source auth, mappings, runs and realtime status | Mapped; provider/reconnect integration remains |
| `WebhookSyncSettings` | connections, Plex auth, profiles/mappings, filters, events, setup, secret rotation/deletion | Broad mapping; lifecycle and destructive-state testing remains |
| `WatchProvidersSettings`, `watchProviderConnectionConfig` | provider cards, device/API flows, connections and sync runs | Mapped; external activation URL handling needs hardening |
| `NotificationsSettings` | relationship, email, Discord, browser-registration management and webhooks | Mapped; browser enrollment itself is correctly browser-only |
| `DeviceSettings`; `ConnectAppsSettings`, `connectApps`; `PluginSettings` | Your Devices, Connect Apps, Sessions and Plugins sections | Mapped with native extensions; capability/deep-link tests remain |

### Admin top-level modules

| WebUI modules reviewed | Desktop evidence | Disposition |
|---|---|---|
| `AdminDashboard`, `AdminStats`, `adminActivityPresentation` | Dashboard page/view model; Stats is only a Web redirect | Broad, but README explicitly says visual parity is incomplete |
| `AdminActivity` | activity page, event-channel subscription, sessions/jobs/details/actions | Broad; realtime recovery/table geometry need Windows validation |
| `AdminLogs` | logs page and dedicated `AdminLogStreamClient` | Mapped; reconnect/filter/detail behavior needs integration tests |
| `AdminDiagnostics` | diagnostics page/API | Mapped; uploads/redaction/error cases remain |
| `AdminLibraries`, `adminLibraryOrder` | libraries page, scan/queue/diagnostics/unmatched controls | Broad but known Scan All stall and visual incompleteness remain blockers |
| `AdminCollections`, `AdminCollectionEditor`, `adminCollectionsShared` | collections page/editor, provider/template/artwork/sync actions | Broad; provider/template/schedule runtime edges remain |
| `AdminSections`, `adminSectionOrder` | section list/editor/preview/order/recipe controls | Broad; exact recipe schemas/drawer geometry need runtime validation |
| `AdminRequests` | router/provider configuration, schema controls and request table/actions | Broad; plugin-schema and target-failure integration remains |
| `AdminAutoscan` | Autoscan Sources/Activity plus native Connections/Settings organization | Behavior maps; tab placement is an intentional native IA difference |
| `AdminTasks`, `AdminTaskDetail` | task list/detail/run/cancel/history and event/poll fallback | Mapped; cancellation/reconnect/task-specific payloads remain |
| `AdminSubtitles` | subtitle table/search/paging/actions | Mapped; dense/narrow table validation remains |
| `AdminMarkerHistory`, `AdminPlaybackHistory` | corresponding pages, filters, paging and links | Mapped; deep-link and long-history behavior remains |
| `AdminRecommendations` | provider/capability/jobs/settings page | Mapped; provider connection/job failure cases remain |
| `AdminUsers`, `AdminUserDetail` | user list/detail, invites/profiles/access/session controls | Broad; destructive/permission behavior needs end-to-end tests |
| `AdminAccessGroups` | access-group page/view model | Mapped; inherited/default policy semantics need contract tests |
| `AdminDevices` | device console/detail/saved views/actions | Broad; very-wide layout and override/action recovery remain |
| `AdminHistoryImport` | source/mapping/run/detail and event-channel page | Mapped; provider auth/reconnect integration remains |
| `AdminPlugins` | installed/catalog/settings/routes/update/remove pages | Broad; schema rollback and WebView navigation boundaries need hardening |
| `AdminNodes` | node status/capacity/toggle/actions | Mapped; stale-event and command-failure behavior remains |
| `AdminApiKeys` | key list/create/reveal/copy/revoke | Mapped; secret lifetime, role and paging tests remain |
| `AdminMaintenance` | maintenance operations, confirmations and progress/event handling | Mapped; destructive idempotency/cancellation needs integration tests |

### Admin policy, settings and Autoscan nested modules

| WebUI modules reviewed | Desktop evidence | Disposition |
|---|---|---|
| `AdminPolicyLayout`; `PolicyDocumentList`; `PolicyEditorPanel`; `PolicySimulatePanel`; `PolicyVendorViewer`; `PolicyVersionHistory`; `PolicyDecisionLogTable`; `policyExamples`; `policyPageUtils`; `policyPresentation`; `policyTestUtils`; `vendorBaseline` | `AdminPolicyPage` capability gate, documents/editor/draft validation/activation, simulation, vendor view, versions and decision log | Broad mapping; OPA/vendor/capability/error output needs current-server testing |
| `AdminSettingsLayout`; `GeneralSettings`; `BrandingSettings`; `ThemeSettings`; `OverlaySettings` | admin settings shell/detail for general, branding, theme variables/CSS and overlays | Mapped; live preview/reset and asset upload states remain |
| `ScannerSettings`; `SearchSettings`; `IntroSettings`; `SubtitlesSettings`; `AIServicesSettings` | corresponding admin settings groups | Mapped; provider/model and long-running validation remains |
| `PlaybackSettings`, `playbackSettings.utils`; `DownloadSettings`; `WatchProvidersSettings` | corresponding admin setting groups and typed adapters | Mapped; playback/provider integration remains |
| `IntegrationsSettings`; `EmailSettings`; `NotificationsAdminSettings`; `ServerNotificationChannels` | integrations/email/notification/channel editors and test actions | Mapped; credential and send-test failure cases remain |
| `CompatibilityProxiesSettings`; `RateLimitSettings`; `DatabaseSettings`, `databaseSettingOptions`; `StorageSettings`; `LogRetentionSettings`, `logRetentionPolicy` | corresponding admin settings groups | Mapped; validation/restart/retention edge cases remain |
| `InvitationsTab`; `InviteCodesTab`; `CredentialStatus`; `FieldGroup`; `SettingField`; `SaveBar`; `RestartServerButton`; `recommendationsSettings` | shared dynamic controls and admin-user/settings surfaces | Mapped; focus, permission and partial-save behavior remains |
| `ActivityPanel`; `ConnectionsPanel`; `SourcesPanel`; `ChoiceCard`; `InlineConnectionPicker`; `SourceConfigForm`; `WebhookSetupStep`; `sourceDescriptor`; `sourceTargets`; `webhookSetup` | `AdminAutoscanPage` sources/connections/activity/setup/config rendering | Broad mapping; exact grouping differs natively, high-volume event tests remain |

Every basename from the 217-module inventory is explicitly named above. Shared components outside `web/src/pages` were also sampled where they determine shell, cards, dialogs, themes, activity and player behavior; they are not included in the 217 count.

## Realtime and lifecycle contract ledger

### HTTP client inventory

The desktop has 19 focused API wrappers plus the shared client. Static extraction found 453 literal API-path occurrences (including duplicates and interpolated variants), distributed across Admin (223), Settings (51), Catalog (45), Playback (40), Auth (33), Notifications (30), Collections (28), Requests (25), Plugins (22), Recommendations (14), Ebooks (9), Watch Providers (9), History Import (7), Downloads (6), Home (5), API Keys (3), People (3), Plex browser auth (3), and Webhook Sync (1; most webhook methods live in `SettingsApi`). This count is an inventory, not a claim that 453 distinct server endpoints exist.

| Contract family | Desktop client(s) | Disposition |
|---|---|---|
| auth, setup, device activation, OAuth, profiles, impersonation | `AuthApi`, `AuthService`, shared client | Broad mapping; generation-safe refresh verified; live identity-provider flows remain |
| home, catalog, libraries, search, item/person metadata | `HomeApi`, `CatalogApi`, `PeopleApi` | Broad mapping; payload drift fixtures remain incomplete |
| playback decision/session/progress/stop, tracks, markers, subtitles, downloads and reader files | `PlaybackApi`, `DownloadsApi`, `EbooksApi` | Broad mapping; real-media protocol sequence remains a blocker |
| collections, requests, recommendations | `CollectionsApi`, `RequestsApi`, `RecommendationsApi` | Broad mapping; provider/plugin schemas need current-server integration |
| effective/user/admin settings, devices, notifications, webhook sync, watch providers | `SettingsApi`, `NotificationsApi`, `WebhookSyncApi`, `WatchProvidersApi` | Broad mapping; external-provider and optional-field drift remain |
| plugins, API keys, history import/Plex | `PluginsApi`, `ApiKeysApi`, `HistoryImportApi`, `PlexBrowserAuthApi` | Broad mapping; schema/secret/auth edge cases remain |
| complete admin suite | `AdminApi` | Broad route coverage; one very large client increases drift and review risk |

Literal path comparison against server router source was deliberately not treated as a missing-endpoint detector: the server composes router prefixes while the client interpolates IDs/query strings, producing false deltas. Reliable closure requires route-expanded server manifests or request/response fixtures, not text `comm` output.

| Concern | WebUI behavior represented | Desktop owner | Audit result |
|---|---|---|---|
| Shared server events | multiplexed authenticated `/events/ws` subscriptions | singleton `EventChannelClient` with subscription handles, reconnect/backoff and state events | Structurally present; stale-result/reconnect behavior needs live-server tests |
| Home state refresh | event-triggered section refresh | `HomePage` subscription and section queue/cache | Present |
| Admin activity/dashboard/scans/tasks/imports/maintenance | event stream with polling fallback where applicable | each page owns subscription handle and timers; singleton transport | Present; verify unload always stops page timers/subscriptions |
| Admin logs | filtered app/audit stream | dedicated per-page `AdminLogStreamClient` | Present with visible connection state/reconnect |
| Playback session | session progress/stop plus playback WebSocket | `PlaybackManager`, `PlaybackWebSocket`, `PlayerService` | Present; known playback regression prevents clearance |
| Watch Together | room-scoped bidirectional WebSocket | `WatchTogetherRoomViewModel` and coordinator | Present; multi-client correctness not source-provable |
| Settings history import | active-run updates | Settings-owned subscription to shared event channel | Present |
| Webhook sync and sessions | periodic refresh | Settings-owned 15s/10s timers | Present; page lifecycle and overlap need runtime validation |
| Search/filter input | debounce/cancellation | per-page timers/CTS in Search, Library, admin tables | Broadly present; cancellation is inconsistent across older code-behind |

## Additional confirmed security findings

#### S5 — server-provided external URLs are shell-opened without scheme validation (medium)

Watch-provider `VerificationUrl` and Plex history-import `AuthUrl` values are passed to `Process.Start(... UseShellExecute = true)` without requiring `http` or `https`. The values originate from the configured server, so exploitation requires a malicious/compromised server or response, but custom URI schemes and local handlers expand impact beyond opening a web page. Parse with `Uri.TryCreate`, allow only `http`/`https`, show the destination host, and fail visibly. Add tests for `file:`, `javascript:`, custom schemes, malformed URLs and control characters.

#### S6 — ebook WebView trusts active package content too broadly (medium)

Archive paths are correctly contained with `Path.GetFullPath`, and generated FB2 text is escaped. However, EPUB HTML is mapped through `silo-reader.local` using `CoreWebView2HostResourceAccessKind.Allow`; navigation is not restricted, scripts in package content are not removed, and messages from the document are accepted based only on JSON shape. A crafted book can execute in its reader origin, load remote resources and send allowed host messages. There is no exposed native host object in current source, limiting direct host compromise, but privacy/network and future-bridge risk remain. Use the narrowest host-resource access, block top-level/external navigation and new windows, disable unnecessary permissions, validate `Source`/origin for messages, and define whether active EPUB scripting is intentionally supported.

#### S7 — invalid configured server schemes are normalized but not rejected (low)

`ServerUrlIdentity.Normalize` canonicalizes `http`/`https`, but returns other absolute schemes or invalid input unchanged. Later HTTP APIs generally fail rather than execute a shell command, so this is primarily validation/diagnostic and credential-scope hardening. Reject anything that is not an absolute HTTP(S) origin before saving or configuring it; prohibit embedded user info and strip query/fragment consistently. Add negative tests.

### Security controls verified in current source

- ZIP/RAR ebook extraction checks normalized output paths remain inside the cache root.
- Plugin route path segments reject `.`/`..` and are escaped.
- Plugin WebView authorization/profile headers are attached only to requests matching the configured server-origin filter; arbitrary external requests do not receive those headers.
- HLS proxying permits only HTTP(S), scopes bearer credentials to the configured origin, and uses loopback route tokens.
- API retry logic snapshots authentication/profile generations so a 401 from an old session cannot be replayed under a replacement user.
- `LocalLog` includes bearer and credential-bearing URL redaction patterns; tests should continue to guard every newly introduced signed query parameter.

## Test-evidence disposition

- Static inventory found **741** `[Fact]`/`[Theory]` cases.
- At least **516** source reads/assertions occur in the suite. These validate labels, route strings, XAML/source structure and previously fixed regressions, but they do not execute WinUI binding, layout, focus, WebView, image virtualization or live-server behavior.
- Stronger behavioral coverage exists for auth generation, authorization policy, API path encoding, serialization converters, playback planning/timeline/recovery/stall detection, stream proxies, image service, subtitle selection, version ranking, calendar, notifications, requests and collection helpers.
- There is no executable test here for a full current server contract, an installed visual snapshot, WebView isolation, multi-client Watch Together, long playback, or the known library/scan UI stalls.
- This Ubuntu VM has no `dotnet` executable and cannot build the Windows App SDK project. No new build or test pass is claimed by this audit.

## CodeRabbit execution record

CodeRabbit CLI `0.7.5` was authenticated in agent mode as the repository owner. A fresh synthetic full-tree comparison against an empty baseline was attempted three times during this audit (including with unrestricted network access). Each attempt reached `connecting_to_review_service` and then failed with `Connection failed: WebSocket closed` / `TRPCWebSocketClosedError`. CodeRabbit returned no review issues. The findings in this document are the direct source comparison described above and are not represented as CodeRabbit output.

## Required Windows runtime audit matrix

The developing Windows session should use this checklist against the exact server SHA above (or record a new SHA if intentionally updating the reference).

For **every route/page and nested tab**:

1. Capture WebUI and desktop at the same viewport width/height and equivalent Windows scale.
2. Record server SHA, desktop SHA, account role, profile, theme, library type and test-data fixture.
3. Compare loaded, loading, empty, error, permission-denied and long-content states.
4. Exercise keyboard, mouse and controller focus/activation; verify Back and deep links.
5. Compare exact text, ordering, icons, colors, radii, gaps, alignment, wrapping, table columns, dialogs/drawers and responsive transition.
6. Verify every visible action through its resulting API mutation and refreshed state.
7. Record screenshots and logs for divergences; do not fix intentional native differences unless behavior is lost.

Priority scenario sets:

| Priority | Scenario set |
|---|---|
| P0 | Library stress scroll/filter/image recycling |
| P0 | Long 4K HDR/DV playback with network/cache disturbance |
| P0 | Player exit/Playing Next/fullscreen/PiP/session cleanup |
| P1 | Admin Libraries Scan All/cancel/reconnect/navigation-away |
| P1 | Admin Activity/Logs realtime reconnect and session actions |
| P1 | Auth refresh, device login, OAuth cancel/expiry, impersonation restart |
| P1 | Item detail across movie/show/season/episode/audiobook/ebook/manga |
| P1 | Collection manual/smart/import/template workflows |
| P1 | Destructive admin actions with timeout/duplicate-submit simulation |
| P2 | All settings at narrow width, high DPI, custom theme and high contrast |
| P2 | Missing/broken/slow artwork across every card family |

## Prioritized remediation plan

### Release blockers

- Reproduce and close the Movies > Library freeze with performance evidence.
- Reproduce and close 4K HDR stop/pause and player session cleanup issues.
- Reproduce and close Admin Scan All UI stall.
- Confirm Playing Next close and shell restoration.
- Run an installed current-SHA side-by-side pass for Dashboard, Libraries and Activity; retain their visual-incomplete status until evidence exists.

### High priority

- Replace the stale GitLab/Continuum audit workflow and baseline.
- Centralize remaining high-cardinality remote image loads.
- Audit silent catches and add safe diagnostics/user-visible errors where needed.
- Add server JSON/realtime contract fixtures for playback and admin high-risk APIs.
- Complete collections template/import/collage/scheduling details.
- Complete provider/policy/plugin-schema edge cases.
- Run multi-client Watch Together recovery/synchronization tests.
- Security-test ebook/archive/WebView boundaries.

### Medium priority

- Reduce mixed responsibilities in the largest page/service units behind behavioral tests.
- Convert accidental literal colors to semantic resources after screenshot comparison.
- Standardize partial-data, retry and permission-denied UI.
- Add deep-link tests for consolidated Settings and redirected native surfaces.
- Complete keyboard, controller, focus, high-DPI, narrow-window and accessibility passes.

## Definition of parity completion

An area should be marked 1:1 only when all of the following are recorded:

- exact desktop and server SHAs;
- route/nested-surface and API contract mapping;
- successful behavior for normal, loading, empty, error and permission states;
- screenshot comparison at agreed viewport/scales/themes;
- keyboard/mouse/controller and accessibility result;
- performance result for data-heavy surfaces;
- no open P0/P1 findings for that area;
- explicit disposition of intentional native differences.

“The page exists,” “the API is called,” and “a source-string test passes” are not sufficient evidence.

## Final assessment

The current desktop `main` is a substantial native implementation of the Silo WebUI, not an early skeleton. Route and feature coverage are broad, and multiple formerly documented gaps are already fixed. It should nevertheless remain labeled pre-release and not 1:1.

The most useful next work is evidence-driven stabilization: close the known freezes/player regressions, repair the parity source-of-truth documentation, harden image/error/realtime lifecycles, and execute the Windows runtime matrix. After those are complete, repeat this source comparison against the then-current server SHA and run a fresh CodeRabbit pass when its review service accepts the connection.
