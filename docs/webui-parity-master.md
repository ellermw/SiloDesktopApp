# Continuum Desktop Player — WebUI Parity Master Roadmap

**Generated from:** 7 exhaustive gap analysis documents in `docs/audit-gaps/`
**Source audits:** 7 exhaustive WebUI reference documents in `docs/audit/`
**Goal:** Complete visual, behavioral, and functional parity between desktop and WebUI.

This document supersedes `docs/webui-parity-audit.md`. Every gap tagged `MISSING`, `PARTIAL`, or `BUG` in the 7 source gap documents is consolidated here, deduplicated, prioritized, and grouped by area. File/line references from the source audits are preserved wherever they were provided.

---

## Summary statistics

| Category | Count |
|---|---|
| Total BUGS (broken functionality) | 58 |
| Foundational infrastructure gaps (unblock many features) | 12 |
| Critical missing features (high user-impact) | 94 |
| Medium-priority gaps | 112 |
| Low-priority / cosmetic gaps | 66 |
| Desktop-exclusive features to preserve | 24 |
| Cross-cutting themes | 14 |

Source line totals: 01=468, 02=694, 03=556, 04=797, 05=622, 06=464, 07=413 — 4014 total audit lines reviewed.

---

## PRIORITY 1 — BUGS (broken functionality)

Every item here is something that doesn't work as intended or produces incorrect output. These should be fixed before any new feature work.

### ✅ Bugs fixed so far

**Batch 1:** B50 (codec capabilities — verified `play_method=direct`).

**Batch 2:** B1 (access token persistence), B3 (PUT /profiles), B4 (profile editing wired),
B5 (OAuth provider buttons), B6 (collection image type), B7 (api-keys tier body),
B8 (library poster path + multipart), B28 (Thumbhash full DCT decode).

**Batch 3:** B16 (default theme), B17/B18 (theme display name dedupe),
B22 (AccentButton FontWeight), B25 (SidebarWidth 260), B30 (Hero CTA label),
B35 (CrewList separator), B36 (Writers exact-match), B37 (heart icon),
B38 (watchlist plus icon), B39 (impersonation banner restyle), B51 (seek slider drag-end).

**Batch 4:** B9 (libraries/providers path), B10 (sections scope), B11 (jobs filter),
B12 (RefreshItemMetadata wired).

**Batch 5:** B47 (setup step from state), B48 (hardware-accel Auto default),
B49 (MetaDB+S3 metadata providers).

**Batch 6:** B33 (string person IDs end-to-end), B34 (cast portrait cards 110×165).

**Batch 7:** B29 (TimeAgo helper consolidated to `Core.Helpers.TimeAgo`).

**Batch 8:** B54 (PlaybackQuality helper extracted to `Core.Helpers.PlaybackQuality`).

**Batch 9:** B19 (RadiusXS/SM/MD/LG/XL/Full tokens + corrected aliases),
B20 (BadgeStyle padding `8,2` and radius 6), B21 (DarkTextBoxStyle px-3 py-1 / h-9),
B23 (CardVerticalPadding + CardHeader/Content/Footer tokens — sub-styles for migration),
B24 (SettingsGroupStyle on RadiusLG), B26 (AdminLogs + AdminPlaybackHistory TableCardStyle radius 8),
B46 (EpisodeRow progress bar `rounded-r-sm`).

**Batch 10:** B57 (HomeSectionsReset confirm dialog), B58 (RemoveSection confirm dialog).

**Batch 11:** B42 (LibraryPage tab + filter persistence), B43 (sort tags `sort_title` / `recently_added`).

**Build pipeline:** `installer/build.ps1` now wipes `obj/Release` + `bin/Release` before publishing
to prevent stale-XBF crashes (fixed a SettingsPage `RangeBase.Value` regression caused by
incremental build skipping XBF regeneration after `XamlTypeInfo.g.cs` was rebuilt).

**Still open from P1:** B2 (impersonation full wiring), B13/B14/B15 (mutation cache — needs F4),
B27 (toast hardcode — needs F2), B31/B32 (LandscapeCard hierarchy + dual click target),
B40 (CollectionsPage browse — needs new CollectionBrowsePage), B41 (LibraryPage in-place mutation),
B44/B45 (SectionRow scroll arrows + ScrollViewer dedupe), B52 (HlsProxy initial transcode),
B53 (dead render code cleanup), B55 (SubtitleAppearance shape), B56 (AdminMaintenance content swap).

---


### Authentication / Security

#### B1: Access token persisted to Windows Credential Manager
**Where:** `LoginViewModel`, `SetupWizardViewModel`, `SignupViewModel` — all call `CredentialStore.SaveCredential(..., "access_token", ...)`
**Problem:** Desktop persists the 24h access token to Windows Credential Manager; WebUI keeps it in-memory only and re-mints on every boot via the refresh token. Security regression.
**Fix:** Stop persisting the access token. Add `BootstrapAccessTokenAsync()` on `AuthService` and call it at launch to mint a fresh token from the refresh token (like `bootstrapAccessToken` in web).
**Source:** 01-api-lib-utils-gaps.md (API client)

#### B2: Impersonation feature broken end-to-end
**Where:** `Controls/ImpersonationBanner.xaml.cs`, `MainWindow.xaml.cs`, `AdminUsersViewModel`
**Problem:** `AdminApi.ImpersonateUserAsync(int)` exists but **no UI calls it** — the admin users page has no Impersonate button. `ImpersonationBanner` raises an `EndImpersonationRequested` event that no page handles. `MainWindow.xaml.cs` `ImpersonationBanner_EndRequested` calls `_authService.Logout()` and navigates back to server select — forcing the admin to re-login.  There is no `POST /auth/impersonation/end` method, no `saveStoredImpersonationAdminSession` / `returnPath` preservation, and `UserInfo.impersonation` is not parsed from the JWT.
**Fix:** Wire the Impersonate button in `AdminUsersPage` / `AdminUserDetailPage`, implement `EndImpersonationWithRecoveryAsync` that preserves `{accessToken, refreshToken, returnPath}` in a storage layer before switching and restores on end; add `ImpersonationInfo` to `UserInfo` and parse from JWT.
**Source:** 01, 02, 04, 06

#### B3: `PUT /profiles/{id}` uses PATCH method
**Where:** `SettingsApi.UpdateProfileAsync` (`SettingsApi.cs:27`)
**Problem:** Desktop uses `PATCH`; WebUI uses `PUT`. Server may accept both but inconsistent.
**Fix:** Change to PUT.
**Source:** 04-hooks-gaps.md

#### B4: Profile editing from ProfileSelectPage is a no-op
**Where:** `Views/ProfileSelectPage.xaml.cs:247`
**Problem:** `if (isEdit) { await ViewModel.LoadProfilesCommand.ExecuteAsync(null); }` — only reloads; never calls an update API.
**Fix:** Wire to `SettingsApi.UpdateProfileAsync` with full field set.
**Source:** 05-pages-user-gaps.md

#### B5: Login OAuth provider buttons have no Click handler
**Where:** `Views/LoginPage.xaml(.cs)`, `ViewModel.AuthProviders` ItemsControl
**Problem:** Provider buttons render but clicking does nothing. Web opens the provider's OAuth authorize URL.
**Fix:** Add Click handler; redirect to provider authorize URL.
**Source:** 05-pages-user-gaps.md

### Admin API bugs (wrong endpoints / wrong bodies)

#### B6: `DELETE /admin/collections/{id}/image` missing `?type=` query param
**Where:** `AdminApi.DeleteCollectionImageAsync` (`AdminApi.cs:403-404`)
**Problem:** Desktop sends no `?type=poster|backdrop` query — server will 400 or delete the wrong image.
**Fix:** Add `string type` parameter; append `?type={type}`.
**Source:** 04-hooks-gaps.md

#### B7: `PUT /admin/api-keys/{id}/tier` wrong body key
**Where:** `AdminApi.UpdateAPIKeyTierAsync` (`AdminApi.cs:151`)
**Problem:** Desktop sends `{rate_tier: value}`; WebUI sends `{tier: value}`. Likely 400.
**Fix:** Send `{tier: value}`.
**Source:** 04-hooks-gaps.md

#### B8: `PUT/DELETE /admin/libraries/{id}/poster` wrong path + wrong content type
**Where:** `AdminApi.SetLibraryPosterAsync` / `DeleteLibraryPosterAsync`
**Problem:** Desktop path is `/api/v1/admin/libraries/{id}/poster`; WebUI path is `/api/v1/libraries/{id}/poster` (no `/admin/`). Desktop sends JSON; server expects `multipart/form-data` with a `poster` file — will 415 or 400.
**Fix:** Change path + use FormData multipart upload.
**Source:** 04-hooks-gaps.md

#### B9: `GET/PUT /admin/libraries/{id}/providers` wrong path
**Where:** `AdminApi.GetLibraryProvidersAsync` / `UpdateLibraryProvidersAsync`
**Problem:** Desktop uses `/admin/libraries/{id}/providers`; WebUI uses `/libraries/{id}/providers`.
**Fix:** Drop `/admin` prefix.
**Source:** 04-hooks-gaps.md

#### B10: `GET /admin/sections?scope=<libraryId>` wrong scope format
**Where:** `AdminApi.GetSectionsAsync`, `AdminSectionsViewModel.cs:43-45`
**Problem:** Desktop sends `?scope={libraryId}`; WebUI sends `?scope=library&library_id={N}`. Library-scoped sections likely broken.
**Fix:** Send `scope=library&library_id={N}`.
**Source:** 04-hooks-gaps.md

#### B11: `GET /admin/jobs` cannot filter by `job_type`
**Where:** `AdminApi.GetJobsAsync` (`AdminApi.cs:327-328`)
**Problem:** Desktop takes no args; WebUI passes `?job_type={type}&limit={N}`. Admin catalog seed flows rely on this filter.
**Fix:** Add `string? jobType, int limit` parameters.
**Source:** 04-hooks-gaps.md

#### B12: `useRefreshItemMetadata` never wired
**Where:** `AdminApi.RefreshItemMetadataAsync` (`AdminApi.cs:296`)
**Problem:** API method exists but no ViewModel calls it — admin cannot refresh an item's metadata from desktop. `PersonDetailViewModel.RefreshMetadataAsync` calls the *person* endpoint instead.
**Fix:** Wire a "Refresh Metadata" More menu item on `ItemDetailPage` (admin-only).
**Source:** 04, 06

### Mutation state management bugs

#### B13: Optimistic updates are missing + empty "revert on failure" catch blocks
**Where:** `ItemDetailViewModel.ToggleFavoriteAsync` (lines 283-305), `ToggleWatchlistAsync` (307-329), `ToggleWatchedAsync` (158-179)
**Problem:** No optimistic state. On mutation error, the catch block is empty but labeled `// Revert on failure`. On a network error the UI state is stale. No cross-surface invalidation either — toggling from ItemDetail doesn't refresh FavoritesViewModel, WatchlistViewModel, or HomeViewModel Continue Watching.
**Fix:** Set the boolean/int first, await API, on exception revert the state; additionally publish a `MediaSurfaceChanged` messenger event (see F4).
**Source:** 04-hooks-gaps.md

#### B14: Catalog export sessions stale across navigation
**Where:** All ViewModels — no query cache
**Problem:** Mutations don't invalidate related screens. Finishing playback doesn't refresh progress/home, rating doesn't refresh recommendations, creating a collection doesn't refresh catalog pages, marking a series watched doesn't cascade to seasons/episodes. See F4 below for the foundational fix.
**Source:** 04-hooks-gaps.md (entire `mediaSurfaceRefresh` section)

#### B15: `HomeViewModel` stale after playback
**Where:** `PlaybackManager.StopSessionAsync`, `PlayerService.CloseAsync`, `HomeViewModel._lastLoadedAt`
**Problem:** No `applyPlaybackProgressToCache` equivalent — final position is sent to server but no VM is notified. Returning to ItemDetailPage shows pre-playback `UserData.position_seconds` until a full refetch.
**Fix:** Publish a `PlaybackProgressUpdated(snapshot)` messenger event on stop; subscribe in HomeViewModel / ItemDetailViewModel / HistoryViewModel.
**Source:** 04-hooks-gaps.md (`playbackProgressCache`)

### Theme / styling bugs

#### B16: Wrong default theme
**Where:** `ThemeService.CurrentTheme`, `Themes/DarkTheme.xaml`
**Problem:** Default is `cobalt-studio`; WebUI `DEFAULT_THEME = "midnight-cinema"`.
**Fix:** Change default to `midnight-cinema`.
**Source:** 01-api-lib-utils-gaps.md

#### B17: Theme display-name map is inconsistent with `ThemeInfos`
**Where:** `ThemeService.ThemeDisplayNames` vs `ThemeInfos`
**Problem:** `ThemeDisplayNames` uses "Midnight Cinema", "Cobalt Studio"; `ThemeInfos` uses "Cinema Dark", "Cobalt". Desktop has two label dictionaries with mismatched strings. WebUI labels: Cinema Dark, Cinema Light, Cobalt, Oxblood, Ember, Evergreen, Verdant Ink, Catppuccin, Gruvbox, Void Space, Charcoal, Graphite, Obsidian.
**Fix:** Reconcile `ThemeDisplayNames` with web labels; delete duplicate map.
**Source:** 01

#### B18: `ThemeInfos` duplicate label source
**Where:** `ThemeService.cs`
**Problem:** Two dictionaries (`ThemeDisplayNames` and `ThemeInfos`) coexist with inconsistent labels. Dead code + inconsistency risk.
**Fix:** Collapse into a single source.
**Source:** 01

#### B19: Corner-radius tokens do not match shadcn scale
**Where:** `Themes/DarkTheme.xaml` `SmallCornerRadius=4`, `MediumCornerRadius=8`
**Problem:** Shadcn `rounded-xs=2`, `rounded-sm=2`, `rounded-md=6`, `rounded-lg=8`, `rounded-full=9999`. Desktop has no XS (2), conflates SM=4 (should be 2), MD=8 (should be 6). Wrong twice.
**Fix:** Re-number: `RadiusXS=2`, `RadiusSM=2`, `RadiusMD=6`, `RadiusLG=8`, `RadiusXL=12`, `RadiusFull=9999`. Audit every `CornerRadius=` reference.
**Source:** 03-ui-primitives-gaps.md (§0.5)

#### B20: `BadgeCornerRadius=4`, `BadgeStyle` padding wrong, style unused
**Where:** `DarkTheme.xaml` lines 278-283
**Problem:** `BadgeCornerRadius=4` should be 6 (`rounded-md`). Padding `8,4` should be `8,2` (`px-2 py-0.5`). PosterCard hard-codes its own badges (`ResolutionBadge` line 72, `AudioBadge` line 84) instead of using `BadgeStyle` — the resource is dead code.
**Fix:** Correct values; retrofit PosterCard badges to use the style.
**Source:** 03 (§2)

#### B21: `DarkTextBoxStyle` corner/padding wrong
**Where:** `DarkTheme.xaml` lines 255-263
**Problem:** `CornerRadius=4` should be 6. Padding `12,10` should be `12,4` (`px-3 py-1`). No `MinHeight=36` (`h-9`). `Background=SurfaceBrush` should be `AppBackgroundBrush` or `InputBrush`.
**Fix:** Correct values; add MinHeight=36; fix background.
**Source:** 03 (§6)

#### B22: `AccentButtonStyle.FontWeight = SemiBold` should be Medium
**Where:** `DarkTheme.xaml` line 220 + `HeaderTextStyle` line 177, `TitleTextStyle` line 184
**Problem:** Web uses `font-medium` (500). Desktop uses SemiBold (600). All primary CTAs look one tier heavier than web.
**Fix:** Change to Medium.
**Source:** 03 (§1, §23)

#### B23: `CardPadding=12,12,12,12` should be vertical-only
**Where:** `DarkTheme.xaml` `CardStyle` lines 246-252
**Problem:** Web `Card` is `py-6` with children handling horizontal `px-6`. Desktop pads uniform 12 on all sides — half vertical, half horizontal.
**Fix:** Change to `Padding="0,24"`. Add sub-styles for `CardHeader`, `CardTitle`, `CardDescription`, `CardFooter`.
**Source:** 03 (§16)

#### B24: `SettingsGroupStyle.CornerRadius=22` arbitrary
**Where:** `SettingsPage.xaml` lines 40-46
**Problem:** 22px is not any web radius token. Padding `24,20` also incorrect vs web 24 top/bottom + 24 via children.
**Fix:** Base on `CardStyle`; use `RadiusLG=8`.
**Source:** 03 (§16)

#### B25: `SidebarWidth=240` off from web 260
**Where:** `DarkTheme.xaml` `SidebarWidth`
**Problem:** Web `LAYOUT.sidebar = { width: 260, collapsedWidth: 64 }`. Desktop = 240. Off by 20px.
**Fix:** Set to 260.
**Source:** 01-api-lib-utils-gaps.md (`design-system.ts`)

#### B26: `AdminLogsPage` `TableCardStyle.CornerRadius=24` wrong
**Where:** `Views/Admin/AdminLogsPage.xaml` line 35
**Problem:** Web `rounded-lg=8`. Desktop 24 is 3x too round.
**Fix:** Shared `TableContainerStyle` with CornerRadius=8.
**Source:** 03 (§20)

#### B27: `StatusToast` in AdminSettingsDetailPage hard-coded color
**Where:** `AdminSettingsDetailPage.xaml` lines 170-186
**Problem:** Uses literal hex `#22C55E` (green) instead of theme resource. Not reusable.
**Fix:** Replace with `ToastService.Success(...)` (see F2).
**Source:** 03 (§18)

### Thumbhash / image decoder bugs

#### B28: `ThumbhashDecoder.Decode` returns DC-only (solid color)
**Where:** `ContinuumPlayer.Core/Services/ThumbhashDecoder.cs`
**Problem:** WebUI returns a blurred image data URL. Desktop only decodes the `lDc/pDc/qDc` header bytes and fills the buffer with one color. Every pixel identical. Thumbhash placeholders on desktop are solid colored rectangles, not blurred approximations.
**Fix:** Implement the full thumbhash algorithm (frequency coefficients → DCT → RGBA) or port the reference JS impl to C#.
**Source:** 01 (`Thumbhash decoder`)

### TimeAgo bugs

#### B29: `FormatRelative` helpers scattered and wrong
**Where:** `AdminPlaybackHistoryViewModel.FormatRelative`, `AdminUserDetailViewModel.FormatRelative`, `AdminTasksPage.FormatRelativeTime`
**Problem:** Three separate implementations. Output is `"5m ago"` / `"3h ago"` instead of web's `"Added 5 minutes ago"`. No "Added" prefix, no pluralization, no null-at-30-days threshold, no negative-time clamp.
**Fix:** Consolidate to `ContinuumPlayer.Core/Helpers/TimeAgo.cs` matching WebUI output format.
**Source:** 01

### Item detail bugs

#### B30: HeroCarousel "Play" button label misleading
**Where:** `Controls/HeroCarousel.xaml`, `HeroCarousel.xaml.cs`
**Problem:** Button labeled "Play" but navigates to ItemDetailPage, does not actually start playback. Web shows "Open details" as the pill-primary CTA.
**Fix:** Rename to "Open details" OR wire to playback. Remove the duplicate "Details" secondary button (web has only one CTA).
**Source:** 02-components-gaps.md (HeroBanner)

#### B31: ContinueWatchingCard inverted heading hierarchy
**Where:** `Controls/LandscapeCard.xaml[.cs]`
**Problem:** Web makes series title the main heading + "Season 1 Episode 1 · Pilot" as meta. Desktop makes episode `Title` the main heading and drops series into subtitle — inverts the hierarchy.
**Fix:** Match web hierarchy — series title primary, episode context secondary.
**Source:** 02

#### B32: ContinueWatchingCard single click target
**Where:** `Controls/LandscapeCard.xaml.cs`
**Problem:** Web has two links: image → `/watch/{id}` (playback) and text → `/item/{id}` (details). Desktop has one Tapped handler → ItemDetailPage; tapping the image never starts playback.
**Fix:** Split hit targets.
**Source:** 02

#### B33: Cast/Crew person ID requires int parsing
**Where:** `ItemDetailPage.xaml.cs` `CastCard_Tapped`, `BuildCrew`
**Problem:** Uses `int.TryParse(personIdStr, out int personId) && personId > 0`. WebUI uses string IDs (e.g., `"person-001"`). Non-integer person IDs silently become non-clickable.
**Fix:** Use string person IDs; update `PersonDetailPage` signature to accept `string`.
**Source:** 02, 06

#### B34: CastCarousel circles not portraits
**Where:** `ItemDetailPage.xaml` `BuildCast`
**Problem:** Desktop uses `64×64 Border CornerRadius=32` (circle). Web uses `aspect-[2/3]` portrait frames 110×165.
**Fix:** Change to portrait cards, not circular avatars.
**Source:** 02, 06

#### B35: CrewList trailing-space separator
**Where:** `ItemDetailPage.xaml.cs` `BuildCrew`
**Problem:** `"Directed by  " + panel.Children` uses two trailing spaces as separator. Web uses single space.
**Fix:** Use single space.
**Source:** 06

#### B36: Writers synthesis lumps Story/Screenplay under "Written by"
**Where:** `ItemDetailPage.xaml.cs` `BuildCrew`
**Problem:** Desktop groups `Writer`, `Screenplay`, `Story` jobs together. Web groups by exact `job` value.
**Fix:** Use exact `job` match.
**Source:** 02

#### B37: FavoriteButton uses star glyphs, not heart
**Where:** `ItemDetailPage.xaml`
**Problem:** Desktop uses `E735` (filled star) / `E734` (outline star) with `IndianRed`. Web uses a `Heart` icon with `text-red-400 fill-current`.
**Fix:** Use a heart glyph (or SegoeIcon `\uEB52` heart filled / `\uEB51` outline).
**Source:** 06

#### B38: WatchlistButton uses checkmark glyph
**Where:** `ItemDetailPage.xaml`
**Problem:** Uses `E73E` (check) for in-watchlist and `E8B7` otherwise. Confuses with watched state.
**Fix:** Use `Check` for primary and `Plus` for not-in-watchlist, matching web.
**Source:** 06

#### B39: ImpersonationBanner text missing "as requested by"
**Where:** `Controls/ImpersonationBanner.xaml[.cs]`
**Problem:** Desktop: "Impersonating {username}". Web: "Impersonating **{userName}** as requested by **{impersonatorName}**". Also banner is dark gold/brown + yellow warning; web is `bg-muted/40` neutral.
**Fix:** Add `ImpersonatorName` DP; change banner to muted neutral (not alarming gold).
**Source:** 02

### Library / Collection bugs

#### B40: CollectionsPage card click opens editor, not catalog
**Where:** `Views/CollectionsPage.xaml.cs:201-205`
**Problem:** Desktop navigates to `CollectionEditorPage(collection.Id)`. Web navigates to `/catalog?source=user_collection&collection_id={id}&title={name}` — the browse view. Only the pencil icon opens the editor. Desktop has no way to browse a collection without entering the editor.
**Fix:** Navigate to catalog browse view on card click; expose an Edit pencil button for editor entry.
**Source:** 05

#### B41: LibraryPage library-collection card mutates state in place
**Where:** `Views/LibraryPage.xaml.cs:505-521`
**Problem:** Clicking a library-collection card replaces the Library tab items in place and rewrites `LibraryTitle.Text`. Web navigates to a new `/catalog?source=library_collection&collection_id=X` URL, leaving the library grid intact and giving a back button. Desktop mutates state with no back path.
**Fix:** Navigate to a new catalog view.
**Source:** 05

#### B42: LibraryPage tab resets on navigation
**Where:** `Views/LibraryPage.xaml.cs:100`, lines 74-94
**Problem:** Tab is reset to Recommended on every `OnNavigatedTo`. Filters cleared. Web `parseLibraryPageState` preserves `?tab=library|collections` and filters across navigations.
**Fix:** Persist tab + filters in a per-page state store (until URL routing arrives).
**Source:** 05

#### B43: LibraryPage sort tag mismatch with WebUI
**Where:** `LibraryPage.xaml` `SortComboBox`
**Problem:** Desktop uses `title`, `added_at`, `release_date`, `year`, `rating_imdb`. Web uses `sort_title`, `recently_added`, `year`, `rating_imdb`. Saved filters don't bridge.
**Fix:** Align tags to web.
**Source:** 05

#### B44: SectionRow two ScrollViewers always present
**Where:** `Controls/SectionRow.xaml`
**Problem:** Creates both `PosterScrollViewer` and `LandscapeScrollViewer` in XAML, collapses one at a time. Visual-tree bloat.
**Fix:** Single ScrollViewer with content template switch.
**Source:** 02

#### B45: SectionRow scroll arrows always visible
**Where:** `Controls/SectionRow.xaml`
**Problem:** Arrows always rendered at full opacity regardless of `canScrollPrev`/`canScrollNext` or hover state. Web hides until hover AND only when scroll is possible.
**Fix:** Hover-reveal + scroll-bound logic.
**Source:** 02

#### B46: EpisodeRow progress bar corner radius wrong
**Where:** `ItemDetailPage.xaml.cs` `CreateEpisodeRow`
**Problem:** Uses `CornerRadius(1.5)`; should have `rounded-r-sm` end cap behavior.
**Fix:** Adjust corner radius logic.
**Source:** 02

### Setup wizard bugs

#### B47: Setup wizard is linear-index, not state-derived
**Where:** `Views/SetupWizardPage.xaml.cs`
**Problem:** Desktop uses `CurrentStep = 1..5`. Web derives `currentStep` from state (`accountComplete / profileComplete / libraryComplete / serverRequired / libraryStepSkipped / serverStepDone`). If the server already has an admin account, web jumps to step 2; desktop always starts at 1.
**Fix:** Derive step from state; persist skip flags.
**Source:** 05

#### B48: Setup server step hardware-accel missing "Auto" + defaults to "None"
**Where:** `SetupWizardPage.xaml` hardware-accel ComboBox
**Problem:** Desktop offers `None / VAAPI / QSV / NVENC`. Web offers `Auto / VAAPI / NVENC / QSV / None` with Auto as default. Clicking Next with default disables hardware decoding.
**Fix:** Add "Auto"; default to Auto.
**Source:** 05

#### B49: Setup metadata provider list wrong
**Where:** `SetupWizardPage.xaml` provider step
**Problem:** Desktop offers TMDB/TVDB/IMDB. Web (and server) use MetaDB/S3/TMDB/TVDB. **MetaDB and S3 are missing** — Continuum's primary providers. IMDB is extra.
**Fix:** Replace list with `metadb`, `s3`, `tmdb`, `tvdb` + per-provider field maps (MetaDB URL+api_key, S3 bucket, TMDB shared API key, TVDB api_key+pin) + MetaDB S3 credentials subpanel.
**Source:** 05

### Player bugs

#### B50: ✅ FIXED — `PlaybackStartRequest` does not declare codec capabilities
**Where:** `PlaybackManager.StartSessionAsync`
**Problem:** Only sent `FileId, ProfileId, StartPosition, AudioTrackIndex`. Did NOT send `codecs_video`, `codecs_audio`, `containers`, `max_resolution`, `hdr`. (Note: the PlaybackStartRequest model had C# default initializers for these fields, so they were being serialized — but it wasn't explicit at the call site and untestable without verification.)
**Fix applied:** Explicitly set all codec capability fields in `StartSessionAsync` — `codecs_video: ["h264","hevc","av1","vp9"]`, `codecs_audio: ["aac","flac","opus","eac3","ac3","dts","truehd"]`, `containers: ["mp4","mkv"]`, `hdr: true`, `max_resolution: "2160p"`. Added diagnostic logging of request + response play_method to state_trace.txt.
**Verification:** Mike played HEVC content post-fix. Log confirms `play_method=direct`. No transcoding. Direct-play working as intended.
**Source:** 07-player-gaps.md (highest-impact gap) — RESOLVED.

#### B51: Seek slider commits continuously during drag
**Where:** `PlayerOverlay.xaml.cs` `SeekSlider.ValueChanged`
**Problem:** Fires continuously during drag; calls `_mpv.Seek(e.NewValue)` for every intermediate value → seek storms on mpv.
**Fix:** Use `ManipulationStarted` / `ManipulationCompleted` or `Thumb.DragCompleted`. Commit on release only.
**Source:** 07

#### B52: `HlsProxy` only used for quality-tier transcodes, not initial transcode
**Where:** `PlayerService.PlayAsync` → `HandleTranscodeFallbackAsync`
**Problem:** Initial transcode returns the remote manifest URL directly. If initial transcode hits 404s on segments not yet encoded, mpv fails; the proxy's 45s retry ladder would mask the issue.
**Fix:** Route initial transcode through `HlsProxy`.
**Source:** 07

#### B53: Dead render code in PlayerOverlay and MiniPlayerBar
**Where:** `PlayerOverlay.xaml.cs:146-239`, `MiniPlayerBar.xaml.cs:78-135`
**Problem:** `PresentFrameAsync`, `OnFrameReady`, `_swBitmaps[]`, `_frameTimes[]`, `_snapBuffer` etc. Player now uses GPU-rendered mpv popup; SW pipeline is dead. `PlayerOverlay.xaml:17` `<Image x:Name="VideoFrame">` is also dead.
**Fix:** Delete dead code.
**Source:** 07

#### B54: PlaybackQuality helper duplicated across admin pages
**Where:** `AdminUserDetailPage.xaml.cs`, `AdminUsersPage.xaml.cs`
**Problem:** `CanonicalPlaybackQuality`, `PlaybackQualityPresetFromValue`, `PlaybackQualityValueFromPreset`, `PlaybackQualityOptions` are duplicated.
**Fix:** Extract to `ContinuumPlayer.Core/Helpers/PlaybackQuality.cs`.
**Source:** 01

#### B55: SubtitleAppearance model shape mismatch + wrong defaults
**Where:** `Core/Models/Settings/SubtitleAppearance.cs`, `SettingsViewModel.ResetSubtitleAppearance`
**Problem:** Desktop `FontSize: int?` vs web `"small"|"medium"|"large"|"xlarge"`. Desktop `Outline: string?` vs web `textOutline: bool`. Defaults wrong: `SubFontFamily="default"` (should be "sans-serif"), `SubOutlineEnabled=true` (should be false), `SubBackgroundOpacity=0.8` (should be 75 on 0-100 scale).
**Fix:** Change model shape to match web; align defaults and use 0-100 integer opacity.
**Source:** 01

#### B56: AdminMaintenance page content mismatch
**Where:** `Views/Admin/AdminMaintenancePage.xaml`
**Problem:** Desktop shows "Stale metadata IDs / Skipped library roots / Unmatched items" tables. Web `AdminCatalogMaintenance.tsx` is a catalog export/import UI (Start Export, Import Catalog dialog, Recent Imports/Exports list, path rewrites). Completely different page sharing the same name.
**Fix:** Implement the catalog import/export feature as the canonical AdminMaintenance; move stale/skipped/unmatched tables to a separate admin page or diagnostics area.
**Source:** 02, 06

#### B57: `HomeSectionsReset_Click` has no confirm dialog
**Where:** `SettingsPage.xaml.cs` HomeScreen settings
**Problem:** Clicking Reset calls `ResetHomeSectionsCommand` directly with no confirmation. Destructive action with no safety.
**Fix:** Add ConfirmDialog matching web copy "Reset section customizations" / "Reset all section customizations to defaults? This action cannot be undone."
**Source:** 05

#### B58: `RemoveSection` has no confirm dialog
**Where:** `SettingsPage.xaml.cs` HomeScreen settings
**Problem:** Custom/system section removal has no confirm. Web distinguishes `Delete custom section?` vs `Remove section?`.
**Fix:** Add confirm dialogs matching web.
**Source:** 05

---

## PRIORITY 2 — FOUNDATIONAL INFRASTRUCTURE

These underpin many features. Fixing them unlocks downstream work.

### F1: Theme system overhaul — ControlTemplates + VisualStateManager + theme brush overrides

**Problem:** `DarkTheme.xaml` is a palette file, not a component library. Zero `ControlTemplate` overrides, zero `VisualStateManager` blocks, zero theme brush overrides (`ButtonBackgroundPointerOver`, `TextControlBackground`, `ToggleSwitchFillOn`, `FocusVisualPrimaryBrush`, `ContentDialogBackground`, `MenuFlyoutPresenterBackground`, etc.). Every `Button`, `ComboBox`, `TextBox`, `Slider`, `ToggleSwitch`, `ContentDialog`, `MenuFlyout`, `ScrollViewer` in the app falls back to WinUI stock Fluent visuals using system colors — they do not match the cobalt-studio palette. On pointer-over buttons flash to Windows system accent blue, not `AccentHoverBrush`.

**Fix:**
1. Override the WinUI theme brush table: `ButtonBackground/BackgroundPointerOver/BackgroundPressed/BackgroundDisabled`, `TextControlBackground/BackgroundFocused`, `ComboBoxBackground/BackgroundPointerOver/DropDownBackground/BorderBrushFocused`, `ToggleSwitchFillOn/Off/KnobFillOn/Off/OuterEllipseWidth/Height`, `MenuFlyoutPresenterBackground/BorderBrush/Padding/CornerRadius`, `ContentDialogBackground/BorderBrush/SmokeFill/TopOverlay/OverlayCornerRadius`, `MenuFlyoutItemBackgroundPointerOver`, `SliderTrackFill/TrackValueFill/ThumbBackground/ThumbWidth/Height/TrackThemeHeight`, `SystemControlHighlightAccentBrush`, `FocusVisualPrimaryBrush/SecondaryBrush/PrimaryThickness/Margin`.
2. Author custom `ControlTemplate`s for primitives with non-trivial structure: Button variants (Destructive, Link, Glass, Xs/Sm/Lg/Icon sizes), Select with right-side check (vs DropdownMenu left), Dialog with 200ms fade+scale-from-0.95, ComboBoxItem with right-aligned check indicator.
3. Add transition tokens: `Storyboard`/`ThemeTransition` resources at 150ms color, 200ms modal fade+scale, 300ms close slide, 500ms open slide, 200ms accordion chevron.
4. Add shadow hierarchy: `ShadowXS`, `ShadowSM`, `ShadowMD`, `ShadowLG` via `ThemeShadow` or `DropShadowEffect`.
5. Add missing design tokens: `RadiusXS/SM/MD/LG/XL/Full`, `FontSizeXs/Sm/Base`, `IconSizeXs/Standard/Md/Lg`, `DurationInstant=0`, `DurationDrift=2000`, easing curves, motion keyframes (`fade-in`, `slide-up`, `scale-in`, `pulse-opacity`, `ken-burns-a/b`), shadow tokens, `Ambient` + `AmbientGlowOpacity` tokens, hero backdrop filter tokens (`hero-backdrop-brightness/saturate/text-shadow`).
6. Rename `GlassCardStyle` (no glass effect currently) or add real acrylic backdrop.

**Source:** 03-ui-primitives-gaps.md (§0.1, §0.2, §0.5, §0.6, §0.7), 01 (global CSS/theming)

**Unblocks:** All visual parity work for ComboBox, Slider, ToggleSwitch, TextBox, MenuFlyout, ContentDialog, ScrollViewer, Button variants, Badge, Card, Separator, Tabs, Accordion, Avatar, Table.

### F2: Toast notification system

**Problem:** No central toast system. Only "toast" is an inline `StatusToast` Border in `AdminSettingsDetailPage.xaml` (hard-coded `#22C55E` green, not reusable). Every mutation, error, save, undo, creation confirm that shows a toast in web has no equivalent on desktop — pages rely on persistent `StatusMessage`/`ErrorMessage` bindings next to controls. User cannot show notifications from anywhere (e.g., "Mark Watched" confirmation from HomePage, error from failed playback).

**Fix:** Author `ToastService` with methods `Success(message)`, `Error(message)`, `Info(message)`, `Warning(message)`. Host `ToastContainer` UserControl on `MainWindow` (Canvas-positioned). Variants keyed by `PopoverBrush`, `SuccessBrush`, `ErrorBrush`, `WarningBrush`. 300ms slide-in, 4s auto-dismiss, 200ms fade-out. Consider wrapping WinUI `InfoBar`. Replace inline `StatusToast` call sites.

**Source:** 02, 03 (§18), 04, 05

**Unblocks:** All mutation success/error feedback; undo banners; 20+ toast call sites across pages.

### F3: URL routing / page state persistence

**Problem:** Desktop has no URL routing. Every page loses tab/filter/query/selection state on navigation. No deep links. No back/forward history. No refresh-to-restore. Affects every page that has filter/tab state: Library, Search, Favorites, Watchlist, History, PersonDetail, Settings, Admin pages.

**Fix:** Either:
- **Option A (full routing):** Add a route state store per-page that serializes filter/tab state to `AppSettings` under a `last_state_{page}` key; restore on `OnNavigatedTo`.
- **Option B (minimum viable):** Per-page state objects passed via navigation parameters; keep state for the current session; don't persist.

Either approach should enable:
1. Library tab/filters preserved across navigations.
2. Settings deep-link to a specific tab (`settings:playback`).
3. Signup invite code pre-fill (`?code=X`).
4. WatchRoute `fileId` query param.
5. Admin playback history filter by `media_item_id`.
6. Admin user filter links from playback history.

**Source:** 05 (shell-level omission + cross-cutting), 04, 06

**Unblocks:** Filter/tab persistence on every content page; deep linking; back navigation; admin page cross-linking.

### F4: Cross-surface cache invalidation messenger

**Problem:** Single largest parity gap in the hooks layer. WebUI `invalidateMediaSurfaceQueries`, `invalidatePlaybackSurfaceQueries`, `invalidateRatingSurfaceQueries` mark stale `itemKeys`, `catalogKeys`, `sectionKeys`, `progressKeys`, `historyKeys`, `favoriteKeys`, `watchlistKeys`, `recKeys`, `personKeys`, `admin/playbackHistory` — all the surfaces that display the affected data. Desktop has no query cache to invalidate. Observed UX gaps directly caused:

1. Marking an episode watched in ItemDetail does not refresh Home Continue Watching.
2. Toggling a favorite does not refresh FavoritesViewModel.
3. Toggling a watchlist does not refresh WatchlistViewModel.
4. Rating a movie does not refresh recommendations rows.
5. Creating a collection does not refresh catalog pages.
6. Finishing playback does not refresh progress or recommendations.
7. Admin actions (refresh metadata, delete item) do not refresh any page.
8. Admin collection edits do not refresh library-collections tab.

**Fix:** Add `CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger` bus. Define messages: `MediaSurfaceChanged(itemId, surfaces[])`, `PlaybackProgressUpdated(snapshot)`, `CollectionChanged(collectionId, libraryId?)`, `RatingChanged(itemId)`, `LibraryListChanged`, `HomeDismissalChanged(surface, itemId)`. Publish from mutation sites; subscribe in relevant VMs to invalidate `_lastLoadedAt` and re-run `LoadAsync` when next shown.

**Source:** 04 (`mediaSurfaceRefresh`, `playbackProgressCache`), 02

**Unblocks:** Optimistic updates, live data freshness, watched cascade, collection editing flow.

### F5: Unified query client / staleTime cache

**Problem:** No query cache. React Query's `staleTime`, `gcTime`, `retry`, `refetchOnReconnect`, `placeholderData`, `keepPreviousData` have no desktop equivalents. Only two ad-hoc caches exist (`CatalogApi._librariesCache` 5 min, `HomeViewModel._lastLoadedAt` 5 min). Web defaults are `staleTime 120_000`, `gcTime 600_000`, `retry 1`, `refetchOnWindowFocus false`, `refetchOnReconnect true`. Impact: every page navigation refetches; Settings re-pulls every setting each open; filters reload every library visit; ratings refetch each item detail; similar items refetch each detail load; etc.

**Fix:** Add a `StaleCache<T>` helper with `TimeSpan` expiry + `Invalidate()` method. Apply to ViewModels to replace ad-hoc `_lastLoadedAt` fields. Standardize stale time to 2 minutes (web default). Add network-change listener (`NetworkStatusChanged`) that refetches active queries.

**Source:** 04 (`catalog.ts`, `collectionPreviews`, `ratings`, `recommendations`, `favorites`, `sections`, everything — staleTime gaps are listed in nearly every hook)

**Unblocks:** Fast page revisits, proper cache semantics, retry policy, progress freshness.

### F6: Skeleton loading component + skeleton states

**Problem:** `grep` finds 0 matches for `Skeleton`, `Shimmer`, `animate-pulse`. Desktop uses `ProgressRing` spinners everywhere (SearchPage line 250, SettingsPage line 75, MatchItemDialog line 42, etc.). Web extensively uses skeleton placeholders for home sections, poster rows, item detail pages, cast carousels, library grids, collections. Significant perceived-performance gap.

**Fix:** Author `SkeletonBox` UserControl — `Border Background={AccentBackgroundBrush} CornerRadius=6` with a `Storyboard` cycling Opacity 1 → 0.5 → 1 over 2s infinitely. Variants: `SkeletonText`, `SkeletonCircle`, `SkeletonPoster` (aspect 2:3), `SkeletonBackdrop` (aspect 16:9). Replace `ProgressRing` in at minimum: HomePage, ItemDetailPage, LibraryPage, SearchPage results, PosterCard image load, SectionRow while loading, Admin tables.

Add `HomePageSkeleton`, `ItemDetailSkeleton`, `SectionLoadingRow`, `SectionErrorRow`, `LibraryCollectionsLoadingGrid` (24 cards).

**Source:** 02, 03 (§12), 05, 06

### F7: Document title / window title management

**Problem:** `MainWindow.xaml` sets `Title="Continuum"` statically. No ViewModel updates window title on navigation. Web uses `document.title = "{label} · Continuum"` with U+00B7 separator. Empty/whitespace → `"Continuum"`. Web has `SETTINGS_TITLES` map (7 entries) and `ADMIN_TITLES` map (12 entries).

**Fix:** Add `Helpers/DocumentTitle.cs` with `APP_DOCUMENT_TITLE="Continuum"`, `FormatWindowTitle(label)`, `SettingsTitles`, `AdminTitles`. Hook `NavigationService.Navigated` to map current page type to a label and set `AppWindow.Title = $"{label} · Continuum"`.

**Source:** 01, 04, 05, 06

### F8: Sheet panel (slide-out drawer)

**Problem:** Web uses `Sheet` extensively for filter panels, settings drawers, cast details, quick-info panels, section editor (SectionDrawer), admin edit forms (UserForm, CollectionEditor, LibraryProviderChain). `grep` finds no slide-out panel, no `SplitView.Pane` overlay use, no `TranslateTransform` side panels. Every modal on desktop is a centered `ContentDialog` or an inline `StackPanel` toggled via `Visibility`.

**Fix:** Author `SheetPanel` UserControl or use `SplitView` in `Overlay` mode. Support 4 sides (top/right/bottom/left). 500ms open / 300ms close `TranslateTransform` + Opacity on overlay. `bg-black/50` overlay rectangle. Max width 384px on Left/Right. Close button `rounded-xs=2` at 70% opacity → hover 100%. Keyboard: Escape closes, focus trap, focus return on close.

**Source:** 02 (SectionDrawer, CatalogFilterSheet), 03 (§11), 05, 06

### F9: Searchable select / autosuggest primitive

**Problem:** No typeahead filter anywhere. `grep` finds 0 `AutoSuggestBox` references. Catalog filter bar has 6-10 searchable selects in web (genre, studio, country, language, rating, resolution, etc.) with hundreds of possible values. Desktop uses plain `ComboBox` which is unusable for those lists. Collection picker in AdminSectionEditor uses a plain ComboBox (no search, no grouping).

**Fix:** Build `SearchableSelect` UserControl on `AutoSuggestBox` with chevron + selected-state rendering. Build `SearchableMultiSelect` using `AutoSuggestBox + ListView SelectionMode=Multiple` with pill-display in trigger showing up to 3 selected values then "+N". Style to `BorderBrush=BorderBrush`, `CornerRadius=6`, `Height=36`, popup `MaxHeight=240`, `Background=PopoverBrush`.

**Source:** 02, 03 (§19), 06

### F10: MediaItemMenu (3-dot context menu on cards)

**Problem:** **Highest-impact UX gap.** Web provides `<MediaItemMenu>` with variants `poster` and `wide` used on PosterCard, ContinueWatchingCard, SectionItemCard. Desktop has no context menu on any card anywhere. Users on desktop have no way to do any of these from a card:
- Mark as watched / unwatched
- Add/remove favorite
- Add/remove watchlist
- Dismiss from Continue Watching
- Dismiss from Next Up
- View Play History (admin)
- Refresh Metadata (admin)

On desktop the only way to perform these is to open ItemDetailPage and use the action bar — drastically increasing friction. Additionally, the `UndoBanner` in HomePage exists but is unreachable because the dismissal workflow has no UI trigger.

**Fix:** Author `MediaItemMenu` UserControl — right-click or kebab-button flyout with all actions. Add to PosterCard (right-bottom corner, `size-8`, hidden until card hover), LandscapeCard/ContinueWatchingCard (top-right of image, `variant="wide"`), and wire admin-only entries when user role is admin. Hook dismissal for `continue_watching` / `next_up` surfaces.

**Source:** 02 (HIGHEST gap), 04, 05

### F11: Ambient color pipeline (thumbhash → average color → glow)

**Problem:** WebUI `useAmbientColor(backdropThumbhash)` → `getAverageColor()` with Rec.709 luminance guard + saturation guard → hex → CSS `--ambient` var → `.ambient-glow` div behind hero. Desktop has no `--ambient` equivalent, no per-backdrop glow rendering, no `getAverageColor` exposed. Hero feels static vs web.

**Fix:** Implement full thumbhash decoder (see B28). Add `ThumbhashDecoder.GetAverageColor(hash)` with luminance/saturation guards. Add `AmbientBrush` resource updated when hero item changes. Render radial-gradient Border behind hero. Add `AmbientGlowOpacity` theme token.

**Source:** 01, 02, 04, 06

### F12: Skeleton + home layout-first loading

**Problem:** Desktop `HomeViewModel.LoadAsync` calls `GetSectionsAsync()` in one shot. Web calls `useHomeLayout()` first (`/home/layout`) for fast skeleton, then batches section items via `planNextHomeSectionBatch` with `MAX_CONCURRENT_SECTION_REQUESTS = 5`, seeding from `collectCachedHomeSections`. `HomeApi.GetHomeLayoutAsync` exists at `HomeApi.cs:8` but is never called. Slower time-to-first-paint, no skeleton, no batching, no per-section error recovery.

**Fix:** Wire two-phase load: `GetLayoutAsync()` → render skeleton → batch `GetSectionItemsAsync` per section with concurrency cap of 5 → render as each resolves. Implement `buildHomeSectionViewModel` state machine: `loading / ready / empty / error`. Add `SectionLoadingRow`, `SectionErrorRow` components with per-section retry.

**Source:** 04, 05

---

## PRIORITY 3 — CRITICAL MISSING FEATURES

High-impact user-facing features grouped by area.

### Card interactions (affects every page displaying media)

- [MISSING] `MediaItemMenu` 3-dot overflow menu on all cards (PosterCard, LandscapeCard, ContinueWatchingCard). **See F10.**
- [MISSING] Play overlay on ContinueWatchingCard hover — web fades in `bg-black/30` + circular `bg-primary` play icon (`h-11 w-11`, scale+opacity). Desktop only changes background color on hover. [02]
- [MISSING] Image scale-on-hover (`group-hover/play:scale-105`) on ContinueWatchingCard, PosterCard, EpisodeRow thumbnail, CastCard photo. [02]
- [MISSING] Card fade-in on image load (`opacity-0 → opacity-100 duration-300`). Desktop sets Opacity=0→1 in code without Storyboard — appears as pop, not fade. [02]
- [MISSING] PosterCard thumbhash background layer — web shows decoded thumbhash while image loads; desktop explicitly skips (perf note). [02]
- [MISSING] PosterCard sort-driven meta line — web shows different subtitle per sort: `recently_added → "3d ago"`, `rating_imdb → "★ 8.4 / 10"`, `release_date → locale date`, else `"2024 · Series"`. Desktop always shows the default variant. [02]
- [MISSING] PosterCard `status: "pending" | "unmatched"` badges ("Scanning" glass-subtle; "Unmatched" red-tinted). [02]
- [MISSING] PosterCard fallback title panel when no poster — web renders centered title `line-clamp-3`; desktop shows empty placeholder. [02]
- [MISSING] LandscapeCard "No Image" fallback placeholder. [02]
- [MISSING] LandscapeCard time-left label below text ("58 min left" or "Next Episode") as a third line — desktop shows it as a corner badge instead. [02]
- [MISSING] LandscapeCard "Next Episode" label for `item_source === "next_up"`. [02]
- [MISSING] ContinueWatchingCard progress bar transition (`transition-all duration-300`). [02]
- [MISSING] ContinueWatchingCard episode meta format "Season 1 Episode 1 · Pilot" one line below series title. Desktop uses `"{SeriesTitle} · S1 E3"` different format. [02]
- [MISSING] PosterCard/LandscapeCard split click targets (image → play/watch, text → details). [02]

### Hero banner / hero carousel

- [MISSING] Crossfade between hero backdrops (`transition-opacity duration-1000 ease-in-out`). Desktop swaps `BackdropImage.Source` — hard cut. [02, 05, 06]
- [MISSING] Ken Burns pan/zoom animation (`animate-ken-burns-a/b`) alternating by index parity. [02, 05, 06]
- [MISSING] Ambient color extraction (see F11). [02]
- [MISSING] Thumbhash-decoded CSS background layer on hero. [02]
- [MISSING] Saturation/brightness filter (`brightness(0.78) saturate(0.95)`). Desktop approximates with a flat `#101722` overlay. [02]
- [MISSING] Title text-shadow (`textShadow: var(--hero-text-shadow, 0 1px 3px rgb(0 0 0 / 40%))`) — hero text unreadable over bright backdrops. [01, 02, 06]
- [MISSING] Hover-reveal nav arrows (`lg:opacity-0 lg:group-hover:opacity-100`). Desktop arrows always at 0.7 opacity. [02]
- [MISSING] Hero `maxSlides={hero.layout.item_limit}` cap — desktop concatenates items from all featured sections into one carousel. [02, 05]
- [MISSING] Hero metadata row as `.metadata-badge` pills (year, IMDb badge with 1-decimal score, first 3 genres). Desktop shows plain text year and comma-joined genres, no IMDb rating, no pill styling. [02]
- [MISSING] Hero "Open details" single CTA pill — desktop has misleading "Play" + "Details" two-button pair. [02] (See B30.)
- [MISSING] Hero keyboard arrow navigation (`KeyDown` handler). [02, 05]
- [MISSING] Hero error placeholder (gradient-filled box with retry button). [05]
- [MISSING] Detail hero poster column — desktop never shows a poster on `ItemDetailPage`; web shows `w-[170px]` portrait poster alongside title. [06]
- [MISSING] Detail hero logo image support (`logoUrl`) — web replaces title with `<img>` + `sr-only` text. [06]
- [MISSING] Detail hero ambient glow layer (`.ambient-glow` div). [06]
- [MISSING] Detail hero studio/network kicker (`mb-2 text-xs font-semibold uppercase tracking-[0.16em]`). [06]
- [MISSING] Detail hero tagline italic styling. [06]
- [MISSING] Detail hero compact variant (for `SeasonContent`). [06]
- [MISSING] Detail hero breadcrumb for Episode + Season pages (`DetailBreadcrumb`). [06]

### Home page

- [MISSING] Skeleton-first two-phase loading (see F12). [05]
- [MISSING] `SECTION_STALE_TIME = 5*60*1000` per-section (vs whole-page cache). [05]
- [MISSING] `HomePageSkeleton` — hero skeleton + 3 row skeletons of 7 aspect-[2/3] poster placeholders. [05]
- [MISSING] `SectionLoadingRow`, `SectionErrorRow` with per-section Retry. [05]
- [MISSING] `buildHomeSectionViewModel` state machine (`loading/ready/empty/error`) so loaded sections stay visible while others refresh. [05]
- [MISSING] Empty layout state card "No sections configured. Ask your administrator to set up the homepage." [05]
- [MISSING] Live refresh after playback (no `applyPlaybackProgressToCache`). [04, 05]
- [MISSING] "No visible libraries" empty state with link to `/settings/libraries`. [02]
- [MISSING] SectionRow loading skeletons (7 cards at `w-[130px]/150px/178px`). [02]
- [MISSING] SectionRow "Explore all" outline button when `onViewAll` provided. [02]
- [MISSING] SectionRow title link wrapping with hover color transition. [02]
- [MISSING] SectionRow pin-to-sidebar button (`SectionPinButton`). [02]
- [MISSING] SectionRow scroll arrow fade gradient (`bg-gradient-to-r from-background/80 to-transparent 40px`). [02]
- [MISSING] SectionRow drag-to-scroll (Embla `cursor-grab`). Desktop `HorizontalScrollMode="Disabled"`. [02]
- [MISSING] SectionRow `headerActions` slot. [02]
- [MISSING] SectionRow return-null on empty. [02]
- [MISSING] Dismiss X button on Continue Watching / Next Up cards — `UndoBanner` exists but unreachable. [02, 05]
- [MISSING] Home document title sync. [05]

### Item detail page

- [MISSING] Poster column in hero (see hero section above). [06]
- [MISSING] "More" kebab menu on action bar (Mark Watched, Add/Remove Favorites, Add/Remove Watchlist, Download, admin: View Play History, Refresh Metadata). [06]
- [MISSING] Type-aware watched label (`Mark Series Watched` / `Mark Season Watched` / `Mark Watched`). [06]
- [MISSING] Watched cascade invalidation after toggle (season/episode/progress all go stale). [06] (Part of F4.)
- [MISSING] Version flyout `buildQualitySummary` formatting ("2160p · HEVC · HDR · TrueHD" with middle dots). Desktop shows "2160p HEVC HDR (30Mbps)". [06]
- [MISSING] Version flyout subtitle line with file size + source hint ("45.0 GB · Remux"). [06]
- [MISSING] Version flyout `extractSourceHint(file_name)` regex (Remux/WEB-DL/WEBRip/BluRay/BDRip/HDTV/DVDRip). [06]
- [MISSING] Version flyout `sortByResolution` descending. [06]
- [MISSING] Version flyout circular play-icon avatar (`size-7 rounded-full bg-accent/70`) per menu item. [06]
- [MISSING] Audio codec label normalization (`mapAudioLabel`): `TrueHD`, `Atmos`, `DTS-HD`. Desktop shows raw `TRUEHD`. [06]
- [MISSING] `pickBestAttributes(versions, qualityPreference)` respecting profile quality preference — desktop always picks highest. [06]
- [MISSING] Resume button resolution/HDR indicator (`resumeResolution`, `resumeHdr`) pulled from `user_data.last_resolution/last_hdr`. [06]
- [MISSING] Star rating widget hover preview (`hoverValue` state highlights stars up to hovered). [02, 06]
- [MISSING] Star rating glass-subtle rounded-full container capsule. [02]
- [MISSING] Star rating role=radio / aria-checked / aria-label accessibility. [02]
- [MISSING] Star rating toggle-off (clicking active star clears). [02]
- [MISSING] Star rating visibility rules (hide for seasons — web tests explicitly assert). [06]
- [MISSING] Cast carousel `ViewTransitionLink` to person catalog via string person_id. [02]
- [MISSING] Cast initials fallback. Desktop shows a generic Contact glyph. [02]
- [MISSING] Cast carousel scroll buttons with gradient overlays on hover-reveal. [06]
- [MISSING] Cast sort by `order` field (desktop `.Take(20)` with no sort). [02]
- [MISSING] Cast lazy image loading / virtualization. [06]
- [MISSING] Cast character name distinct styling (desktop uses same Caption for both). [06]
- [MISSING] Crew "Crew" section header + dl grid (Directors/Writers/Producers side-by-side). [02]
- [MISSING] Crew Producers group (desktop skips entirely). [02]
- [MISSING] Crew deduplication by name within each job. [02]
- [MISSING] Crew integrated INSIDE hero as bottom caption line (vs below action bar). [06]
- [MISSING] Crew `jobLabel="Created by"` for series override. [06]
- [MISSING] Crew genre middle-dot chaining in hero caption. [06]
- [MISSING] Crew person link with string person_id. [06]
- [MISSING] EpisodeRow episode-number column (`w-7` left gutter). [02]
- [MISSING] EpisodeRow hover background (`hover:bg-white/6`). [02]
- [MISSING] EpisodeRow in-progress `bg-accent/5` row tint. [02]
- [MISSING] EpisodeRow thumbnail zoom (`group-hover:scale-[1.03]`). [02]
- [MISSING] EpisodeRow green watched checkmark SVG (desktop uses "Watched" text badge). [02]
- [MISSING] EpisodeRow star rating display (`rating.toFixed(1)`). [02]
- [MISSING] EpisodeRow meta line `air_date · runtime` separator. [02]
- [MISSING] EpisodeRow grid layout (web `grid-cols-1/2/3/4/5` of aspect-video cards; desktop vertical list). [06]
- [MISSING] Episode count header with "N total" counter. [06]
- [MISSING] SeasonAccordion "Season Page" link button. [02]
- [MISSING] SeasonAccordion "Specials" label (web detects `is_specials || season_number === 0`). [02, 06]
- [MISSING] Current season poster + title + air_date + overview + stats line panel. [02]
- [MISSING] Pill-tab season selector (desktop uses poster cards). [02]
- [MISSING] Season stats line ("5 watched / 3 left / 1 in progress"). [02]
- [MISSING] Season completed green checkmark badge overlay (top-right `bg-green-500/90`). [06]
- [MISSING] Season progress bar overlay (3px bar at bottom, green when completed, accent otherwise). [06]
- [MISSING] Season card hover scale animation. [06]
- [MISSING] Season "X of Y episodes" subtext. [06]
- [MISSING] Dedicated `SeasonContent` page with breadcrumb + compact hero + episode grid. Desktop uses in-page swap, breaking deep-links to `/item/{seasonId}`. [06]
- [MISSING] `EpisodeContent` page with breadcrumb + `S{N} · E{N}` context label + sibling `EpisodeCarousel` with scroll-to-current. [06]
- [MISSING] Episode link state threading (`parentSeasonHref`, `parentSeasonLabel`). [06]
- [MISSING] `episodeLinkState` for breadcrumb back-resolution. [06]
- [MISSING] Similar items response caching — desktop fetches full `GetItemDetailAsync` for each similar item (15 extra HTTP calls per page load, violates speed priority). [06]
- [MISSING] Metadata badges: season count ("N Season(s)"), episode count, series status (returning/ended). [06]
- [MISSING] Score row layout (separate `flex flex-wrap gap-5` block after metadata, before overview). Desktop inlines scores in metadata row. [06]
- [MISSING] IMDb score with yellow Star icon + big bold value. [06]
- [MISSING] Skeleton loading for detail page (see F6). [06]
- [MISSING] Item detail document title. [05, 06]

### Player

- [MISSING] **Codec capabilities declaration (B50 — highest-impact player gap).** [07]
- [MISSING] Next episode overlay with 10-second countdown card ("Up Next in Xs" + `S:E — title` + Play Now + Cancel). Desktop `OnPlaybackEnded` just minimizes. Requires series episode list + `findNextEpisode` logic on `PlaybackManager`/`PlayerService`. [07]
- [MISSING] Subtitle search modal (language dropdown, results list with score/provider badges, spinner, Escape to close). Desktop auto-downloads English only via `SearchAndDownloadSubtitlesAsync`. [07]
- [MISSING] Subtitle auto-select (`resolveSubtitleAutoSelect` with off/auto/always, forced-track when audio matches profile language, preferred-language matching). Desktop always starts subtitles off. [07]
- [MISSING] Per-series subtitle preference persistence (`PUT /subtitle-prefs/{series_id ?? content_id}` with `{subtitle_language, subtitle_track_index, subtitle_mode}`). `PlaybackApi.SaveSubtitlePrefsAsync` exists but never called. [07]
- [MISSING] Per-series audio preference persistence (`PUT /audio-prefs/{series_id}`). `CatalogApi.SetAudioPrefsAsync` exists but never called. [07]
- [MISSING] Subtitle appearance applied to mpv. Settings saved to server via `PutSettingAsync("subtitle_appearance",...)` but never passed to mpv's `sub-font-size`, `sub-font`, `sub-color`, `sub-back-color`, `sub-border-color`, `sub-shadow-color`, `sub-align-y`. [01, 07]
- [MISSING] Resume hints — trait-based version matching via `matchByTraits()`. Desktop always picks highest resolution. Store `lastFileId/lastResolution/lastHDR/lastCodecVideo` per content ID. [07]
- [MISSING] Quality preference carry-over (ignore user's saved `quality` in version selection). [07]
- [MISSING] Quality tier menu in XAML overlay (only in Lua OSC — inconsistent UX). [07]
- [MISSING] Quality menu error display (`text-red-400 text-xs` at top of dropdown for transcode errors). [07]
- [MISSING] Quality menu transcoding indicator ("…" trigger label during transcode start). [07]
- [MISSING] Quality menu Version/Quality split with header + divider. [07]
- [MISSING] Quality menu active tier indicator. [07]
- [MISSING] Original option sublabel with `(resolution · codec · HDR)`. [07]
- [MISSING] 4K transcode guard error ("No lower resolution version available for transcoding") on 422. [07]
- [MISSING] `switched_file_id` UI reflection — desktop logs to `state_trace.txt` but doesn't update the menu's active selection. [07]
- [MISSING] Seek bar buffered range indicator in XAML (present in Lua OSC). [07]
- [MISSING] Seek bar hover time tooltip in XAML. [07]
- [MISSING] Seek bar `pendingSeekTime` / settle tolerance. Desktop snaps backward during long seeks. [07]
- [MISSING] Seek out-of-transcode-range detection → re-kick transcoder. [07]
- [MISSING] Volume + mute persistence across sessions. Desktop starts at 100% every launch. [01, 07]
- [MISSING] Volume scroll-to-adjust (`handleWheel ± 5%`). [07]
- [MISSING] Audio track menu header. [07]
- [MISSING] Audio track menu disabled when only 1 track. [07]
- [MISSING] Audio track menu label format `"{language} · {layout/channels} · {CODEC}"`. [07]
- [MISSING] Subtitle source badges (External/Embedded/Downloaded uppercase). [07]
- [MISSING] Subtitle source-based sorting (external > downloaded > embedded). [07]
- [MISSING] Subtitle detail row with truncated label when different from language. [07]
- [MISSING] Subtitle "Search Online…" footer row with modal trigger. [07]
- [MISSING] Subtitle captions glyph swap (`Captions` active vs `CaptionsOff` inactive). [07]
- [MISSING] C/K keyboard shortcuts (toggle captions / toggle play-pause). [07]
- [MISSING] Pause-state center overlay (`64x64 circle bg-black/50 + Play icon`). [07]
- [MISSING] Top-left back button + two-line title header (desktop title is in bottom control bar). [07]
- [MISSING] Buffering spinner defer (500ms setTimeout to avoid brief-stall flashes). [07]
- [MISSING] Loading overlay "Preparing playback" + "Loading stream details, subtitles, and resume state." pre-player screen. [05]
- [MISSING] Error overlay "Playback unavailable" + Go Back button. [05, 07]
- [MISSING] Click-video → play/pause on the XAML overlay (currently inconsistent with mpv popup clicks). [07]
- [MISSING] WebSocket auto-reconnect with backoff `[500,1000,2000,5000]ms`. [07]
- [MISSING] WebSocket fresh-token-per-reconnect closure. [07]
- [MISSING] WebSocket command de-duplication (Set of seen `command_id`s). [07]
- [MISSING] Info (i) button for PlaybackInfoOverlay modal (desktop has Stats with different data). [07]
- [MISSING] PlaybackInfoOverlay 4-section layout (Player / Video Info / Playback Stream Info / Original Media Info). Desktop StatsOverlay is simpler. [07]
- [MISSING] Live stats polling (dropped/corrupted frame counts, file size, video/audio bitrate split, sample rate, range type). [07]
- [MISSING] `fileId` query-param support on WatchRoute. [05, 07]
- [MISSING] Intro button semi-transparent black style (desktop uses AccentButtonStyle solid). [07]
- [MISSING] Initial transcode routed through HlsProxy (B52). [07]
- [MISSING] Progress reporting cadence alignment (desktop 7s vs web 10s — minor). [07]

### Library page

- [MISSING] Hero banner from first featured section (`splitLibrarySections`) in Recommended tab. [05]
- [MISSING] `PinnedCollections` tail in Recommended tab (part of sidebar pins feature). [05]
- [MISSING] Filter panel `CatalogFiltersPanel` / `CatalogFilterSheet` slide-out drawer. [02, 05]
- [MISSING] Guided vs Advanced filter editor mode toggle. [02]
- [MISSING] Advanced filter rule groups (nested `match: all/any`). [02]
- [MISSING] Type filter (Movies/Series/All) when `libraryType === "mixed"`. [02, 05]
- [MISSING] SearchableMultiSelect for genres (typeahead). [02, 05]
- [MISSING] Min IMDb rating number input (0-10, step 0.1). [02, 05]
- [MISSING] `originalLanguage` filter. [02, 05]
- [MISSING] `addedInLast` / `releasedInLast` text inputs ("30d", "6m"). [02, 05]
- [MISSING] `status` filter (pending/matched/unmatched). [02, 05]
- [MISSING] `network` filter. [02, 05]
- [MISSING] Filters button with active count Badge. [02]
- [MISSING] `isLocked` filter notice for certain sources. [02]
- [MISSING] Windowed pagination (`pageSize=60` with 50ms debounced `visibleRange` tracker). [04, 05]
- [MISSING] Multi-page windowed prefetch (`useQueries` around visible range ±1). [04]
- [MISSING] `keepPreviousData` placeholder on filter change (desktop clears then awaits). [04, 05]
- [MISSING] Library hidden fallback + link to /settings/libraries. [05]
- [MISSING] Library document title = library name. [05]
- [MISSING] Collections grid: `CollectionPosterCard` 2:3 aspect with thumbhash placeholder, fade-in, fallback title, count badge. [05]
- [MISSING] Collections grid 24-card loading skeleton. [05]
- [MISSING] Collections grid responsive columns. [05]
- [MISSING] Year input validation (`sanitizeYear` strips zero and non-positive). [05]

### Catalog (search / favorites / watchlist / history / person)

- [MISSING] Unified Catalog.tsx dispatcher over 7+ sources with shared `CatalogFiltersPanel`. Desktop splits into discrete pages with no filters. [05]
- [MISSING] Sort/order selector on Search results. [05]
- [MISSING] Filters on Favorites / Watchlist / History / PersonDetail (type, genre, year range, content_rating, sort, order). [05]
- [MISSING] `buildCatalogApiSearchParams` / `parseCatalogSearchParams` equivalent for advanced filter serialization. [05]
- [MISSING] `useCatalogWindow` windowed pagination with `pageSize=60`. [04, 05]
- [MISSING] `title` URL param preservation for instant-snap titles. [05]
- [MISSING] Document title per catalog mode. [05]
- [MISSING] Available-now header count panel on Favorites/Watchlist/History/Person. [05]

### Collections

- [MISSING] Clicking a collection opens catalog browse view (not editor) — B40. [05]
- [MISSING] Shared badge on collection cards. [05]
- [MISSING] Hover lift animation. [05]
- [MISSING] Hover-visible Edit + Delete icon buttons in card header. [05]
- [MISSING] Access control: per-profile `allowed_profile_ids[]` list. Desktop has `SharedToggle` only. [02, 05]
- [MISSING] `UserCollectionSummary` sidebar card. [05]
- [MISSING] Read-only mode for non-creator collections. [05]
- [MISSING] Live preview updates on rule change. [02, 05]
- [MISSING] Rule groups (nested match all/any). [02, 05]
- [MISSING] Match-mode toggle (all/any). [05]
- [MISSING] Value picker variants (searchable dropdown for genre/content_rating; numeric for year). Desktop uses plain TextBox for every field. [05]
- [MISSING] Field list completeness (`rating_imdb`, `runtime`, `added_at`, `overview`). [05]
- [MISSING] `between` operator for year ranges. [05]
- [MISSING] `gte`, `lte`, `in`, `not_in` operators. [05]
- [MISSING] `supportsRange` + dual-input rendering. [02]
- [MISSING] Boolean field select (True/False) for watched/favorited/in_watchlist. [02]
- [MISSING] Sort by + Order config on rules. [02, 05]
- [MISSING] Drag-and-drop reordering (desktop uses arrow buttons). [05]
- [MISSING] Bulk "Remove all" action in manual items. [05]
- [MISSING] Back link text label "Back to Collections". [05]
- [MISSING] Not-found Card state ("Collection not found"). [05]
- [MISSING] Manual items picker uses `CollectionSearchableSelect` with typeahead. [02]
- [MISSING] `CollectionOrderingEditor` (sort by / order / manual pins). [02]
- [MISSING] `createCollectionBuilderValue` defaults. [02]
- [MISSING] Description textarea. [02, 06]
- [MISSING] Visibility select (visible/hidden). [02, 06]
- [MISSING] Featured switch. [02, 06]
- [MISSING] Multi-library scope (`library_ids` array). [06]
- [MISSING] Preview layout with sidebar (`previewLayout: "sidebar"`). [02, 06]

### Settings page

- [MISSING] `Accessibility` tab entirely — Text size (Default/Large/Extra Large), Text weight (Default/Bolder), Contrast (Standard/High Contrast). [01, 04, 05]
- [MISSING] Route-based deep linking for settings subpages. [05]
- [MISSING] Per-tab document title. [05]
- [MISSING] `settings/libraries` summary "{visibleCount} of {total} libraries visible" + Show all / Hide all buttons. [05]
- [MISSING] Library settings "Profile default" inherit hints below each override dropdown. [05]
- [MISSING] HomeScreen Library-scoped customization — web lets you pick `home` or `library:{id}`. Desktop `HomeScopeComboBox` has only `Home`. [05]
- [MISSING] HomeScreen drag-and-drop reordering (desktop uses arrows). [05]
- [MISSING] HomeScreen `Add Section` button + SectionDrawer to create custom sections. [02, 05]
- [MISSING] HomeScreen Edit Section button per row. [05]
- [MISSING] HomeScreen Featured badge + Visible/Hidden badge + `{sectionType} · {item_limit} items` secondary line. [05]
- [MISSING] HomeScreen auto-save on mutation (desktop has explicit Save button). [05]
- [MISSING] HomeScreen rollback on error. [05]
- [MISSING] SubtitleAppearance disable opacity+bg when style≠box. [05]
- [MISSING] SubtitleAppearance reactive containerStyle positioning (top/bottom). [05]
- [MISSING] HistoryImport Saved Server sub-mode inside Emby (admin-defined servers). [05]
- [MISSING] HistoryImport target profile picker (required for start). [05]
- [MISSING] HistoryImport `canStartImport` validation. [05]
- [MISSING] HistoryImport `RunSummary` panel with metric grid and unmatched samples. [05]
- [MISSING] HistoryImport recent imports rich row layout. [05]
- [MISSING] HistoryImport auto-polling of in-progress runs every 2s. [04, 05]
- [MISSING] Library disabled server setting (`disabled_library_ids`) — desktop stores in local `HiddenLibraryIds`, doesn't sync across devices. [04]
- [MISSING] Sidebar visibility filter honoring `HiddenLibraryIds` (desktop sidebar doesn't apply the filter). [04]
- [MISSING] LibraryPlaybackPrefs profile-scoped key segmentation + in-place cache update. [04]
- [MISSING] Profile delete UI. [04]
- [MISSING] Server-side `ui_text_scale`, `ui_text_weight`, `ui_high_contrast` persistence. [01, 04]
- [MISSING] Preview-on-hover theme picker (`previewTheme(id)` on hover, `resetPreviewTheme` on leave). [02, 05]
- [MISSING] Theme switcher inside profile dropdown menu. [02]

### Admin

- [MISSING] Admin log WebSocket streaming entirely (`useAdminLogStream`). `AdminLogsViewModel` uses REST only. Desktop `ConnectionState` string updates locally but isn't backed by a stream. [04, 06]
- [MISSING] Admin session WebSocket streaming (`useAdminSessionStream`). Activity page is REST-poll only. No live "n live" badge. `NavActivityBadge` visibility is hardcoded to Collapsed. [02, 04, 06]
- [MISSING] `AdminSessionStreamProvider` global provider for admin pages. [02, 06]
- [MISSING] `AdminSessionActions` component (pause/resume/stop/terminate/send message). Desktop has individual buttons per row, no compact DropdownMenu, no toast feedback, no optimistic paused state. [02, 06]
- [MISSING] Admin stats auto-refresh (30s refetchInterval). Dashboard and sessions go stale until navigation. [04, 06]
- [MISSING] Admin playback history auto-refresh (15s refetchInterval with `refetchIntervalInBackground: true`). [04, 06]
- [MISSING] Admin logs staleTime 5s. [04, 06]
- [MISSING] Admin recommendations unconditional 5s polling (desktop only polls when a job is running). [04]
- [MISSING] Admin tasks idle 30s polling (desktop only polls when active). [04]
- [MISSING] Admin nodes / providers / api-keys / invite-codes / subtitle providers / rate limits / user defaults 30s staleTime. [04]
- [MISSING] AdminMaintenance = catalog import/export UI (B56). [02, 06]
- [MISSING] AdminStats page entirely (4 stat cards + active sessions table). [06]
- [MISSING] AdminSubtitleProvidersPage in nav (page exists, unreachable). [06]
- [MISSING] AdminProvidersPage in nav (page exists, unreachable). [06]
- [MISSING] AdminUserDetail EditUserForm sub-tabs (Account/Access/Limits). [06]
- [MISSING] `LibraryAccessSelector` component (multi-select with all/none helpers). [02, 06]
- [MISSING] `LibraryMultiSelect` with "Movies + 2 more" summary helper. [02]
- [MISSING] User Downloads / Download Transcode switches. [06]
- [MISSING] Max Playback Quality preset select. [06]
- [MISSING] Max Streams / Max Transcodes with "0 = unlimited" hint. [06]
- [MISSING] User Permissions & Limits display panel. [06]
- [MISSING] Public Signups toggle Card with description. [06]
- [MISSING] Invite Codes: `use_count / max_uses` column with red text when exhausted. [06]
- [MISSING] Invite Codes: copy-to-clipboard button. [06]
- [MISSING] Invite Codes: uppercased auto-transform. [06]
- [MISSING] Invite Codes: validation messages. [06]
- [MISSING] AdminUsers History icon link per row (→ filtered playback history). [06]
- [MISSING] AdminCollectionEditor: `SourceTypeSelector` (Manual / MDBList / TMDB). [06]
- [MISSING] AdminCollectionEditor: `MDBListImportForm` (URL, Max Items, Featured, artwork). [06]
- [MISSING] AdminCollectionEditor: `TMDBPresetForm` (preset, media type, time window, limit, featured, artwork). [06]
- [MISSING] AdminCollectionEditor: `CollectionEditForm` for MDBList/TMDB edit. [06]
- [MISSING] AdminCollectionEditor: `ImageUploadField` for poster + backdrop. [02, 06]
- [MISSING] AdminCollectionEditor: visibility select, featured switch, multi-library scope. [06]
- [MISSING] AdminCollectionEditor: `AdminCollectionSummary` sidebar panel. [06]
- [MISSING] AdminCollections list: Featured badge, Visibility badge, Description line-clamp-2, Sync status column, Sync button with spinner, Delete confirm. [06]
- [MISSING] AdminLibraries: Empty Root guard banner + Check Mount + Cleanup button. [06]
- [MISSING] AdminLibraries: scan_warning_message in Last Scanned. [06]
- [MISSING] AdminLibraries: "Catalog Maintenance" link in header. [06]
- [MISSING] AdminLibraries: `LibraryPosterSection` (Upload/Replace/Delete poster). [02, 06]
- [MISSING] AdminLibraries: Provider Priority Chain editor (numbered list with up/down/remove + add-provider select). [06]
- [MISSING] AdminLibraries: Refresh button animation + Scan button animation (desktop tracks state but icons don't spin). [06]
- [MISSING] AdminLogs: click-to-open Sheet detail (desktop uses inline panel). [06]
- [MISSING] AdminLogs: "View related playback session logs" button in detail. [06]
- [MISSING] AdminLogs: FFmpeg-only filter toggle. [06]
- [MISSING] AdminLogs: cursor pagination indicator. [06]
- [MISSING] AdminLogs: row click highlighting. [06]
- [MISSING] AdminActivity: `FFmpegLogPanel` inline expansion per session. [06]
- [MISSING] AdminActivity: "View Logs" / "FFmpeg Logs" quick links per row. [06]
- [MISSING] AdminActivity: sort headers with ▲/▼ indicators. [06]
- [MISSING] AdminActivity: connection state display. [06]
- [MISSING] AdminActivity: Radio icon on live badge. [06]
- [MISSING] AdminSections: filter rules editor for filter-based section types. [06]
- [MISSING] AdminSections: collection picker when `sectionType === "collection"`. [02, 06]
- [MISSING] AdminSections: "Filter by Type" select + "Filter by Library" LibraryMultiSelect. [06]
- [MISSING] AdminSections: media scope + library badges in Type column. [06]
- [MISSING] AdminSections: drag-and-drop reordering (desktop uses arrow buttons — acceptable but diverges). [06]
- [MISSING] AdminSettingsDetail: Log Retention bucket editor (per-bucket Component/Level/Days/MaxRows/MaxSize, Restore Recommended, `parseBucketPolicies`/`serializeBucketPolicies`). Currently stubbed out. [06]
- [MISSING] AdminSettingsDetail: Integrations sub-tabs (Services / Subtitles / Providers). Desktop only has MetaDB + TMDB API keys. [06]
- [MISSING] AdminSettingsDetail: `SaveBar` conditional restart-required warning (desktop shows static subtitle). [06]
- [MISSING] AdminSettingsDetail: sensitive "configured" placeholder beyond Redis URL. [06]
- [MISSING] AdminSettingsDetail: Storage User DB tab scope (3rd tab). [06]
- [MISSING] AdminHistoryImportSources admin CRUD UI (API present). [04]
- [MISSING] AdminCatalogExport / AdminCatalogImport / AdminCatalogImportSources UIs (API present). [04]
- [MISSING] AdminJobs filter by `job_type` (B11 + missing UI). [04]
- [MISSING] `useLibraryDeleteJobs` 2s active polling. [04]
- [MISSING] AdminApiKeys "Copy your API key now" reveal-once flow after creating. [06]
- [MISSING] AdminApiKeys ConfirmDialog for revoke. [06]
- [MISSING] Library providers editor UI (`GetLibraryProvidersAsync`, `UpdateLibraryProvidersAsync` API present). [04]
- [MISSING] AdminUserDetail impersonation confirmation dialog. [06]
- [MISSING] AdminTaskDetail history row expand-to-show-result-data click. [06]
- [MISSING] AdminTaskDetail cancelling progress bar color (amber when state=cancelling). [06]
- [MISSING] AdminNodes Jobs column for transcode nodes. [06]
- [MISSING] AdminNodes info banner color differentiation (proxy=blue, transcode=amber). [06]
- [MISSING] AdminPlaybackHistory "Item filter active" pill + URL param sync. [06]
- [MISSING] AdminPlaybackHistory "View Logs" / "FFmpeg Logs" action links in Logs column. [06]
- [MISSING] Remove redundant standalone AdminInviteCodesPage (web keeps it only within Users page). [06]

### Profile / Auth / Setup

- [MISSING] Profile `avatar`, `max_content_rating`, `language` fields in model + UI. [01]
- [MISSING] Profile avatar image support on ProfileSelectPage. [05]
- [MISSING] Profile "Kids" label under name for `is_child`. [05]
- [MISSING] Profile PIN entry as modal (desktop replaces grid with inline PIN entry). [05]
- [MISSING] ProfileSelectPage delete red destructive styling. [05]
- [MISSING] SetupWizard admin-exists auto-skip (B47). [05]
- [MISSING] SetupWizard Confirm Password field in Account step. [05]
- [MISSING] SetupWizard Library "Skip for now" button + helper text. [05]
- [MISSING] SetupWizard Server "Current value: X" hints. [05]
- [MISSING] SetupWizard Server field hydration from existing settings. [05]
- [MISSING] SetupWizard Server password-masked Redis URL input. [05]
- [MISSING] SetupWizard Server "Skip for now". [05]
- [MISSING] SetupWizard Metadata provider field maps (MetaDB URL/api_key/S3 creds, S3 bucket, TMDB shared key, TVDB api_key/pin). [05] (B49)
- [MISSING] SetupWizard "Attach to {primary_library}" toggle. [05]
- [MISSING] SetupWizard "Go to admin" escape hatch. [05]
- [MISSING] SetupWizard step-completion toasts. [05]
- [MISSING] Login setup-required redirect. [05]
- [MISSING] Login restart banner after auth providers refresh. [05]
- [MISSING] Signup disabled-signup Card state when `enabled === false`. [05]
- [MISSING] Signup `?code=X` URL param pre-fill. [05]
- [MISSING] Signup password match validation toast. [05]
- [MISSING] `restoreUserSession<TUser>` pure API method. [01]
- [MISSING] `bootstrapAccessToken` pure function extracted from MainWindow ad-hoc logic. [01]
- [MISSING] Impersonation session preservation + returnPath. [01, 04]
- [MISSING] UserInfo `impersonation?: ImpersonationInfo` field parsed from JWT. [01]
- [MISSING] Profile-unverified event + `onProfileUnverified` listener — detect 403 profile_unverified, clear profile token, fire event for PIN re-entry. [01]

---

## PRIORITY 4 — MEDIUM GAPS

Features/polish that matter but aren't critical-path.

### Navigation / Shell

- [MISSING] Sidebar collapse/expand hover-to-expand (`w-16` → `w-[260px]` on hover with 150ms timer). [02]
- [MISSING] Sidebar detail-immersion auto-collapse on item routes. [02]
- [MISSING] Libraries chevron expand/collapse section header. [02]
- [MISSING] Per-library pinned items (sidebar pins feature entirely — see cross-cutting). [02, 04, 05]
- [MISSING] Per-library chevron toggle for pin list. [02]
- [MISSING] Active-link left indicator strip (3px vertical accent bar). [02]
- [MISSING] Profile menu Theme section (with ThemeSwitcher). [02]
- [MISSING] View transition navigation (`ViewTransitionLink` / `document.startViewTransition`). [01, 02, 04, 05]
- [MISSING] Search in Discover section grouping. [02]
- [MISSING] Layout top background glow (`from-primary/8 to-transparent blur-3xl`). [02]
- [MISSING] `ScrollToTopButton` floating affordance (right-6 bottom-6, visible when scrollY > 600). [02, 05]
- [MISSING] Page-shell max widths (1400 / 1520). Desktop stretches to full window minus sidebar. [01]
- [MISSING] Responsive page padding. Desktop fixed 24,24,24,24. [01]
- [MISSING] Breakpoint-driven poster grid columns (2/3/4/5/6 at mobile→xl). Desktop uses ItemsWrapGrid with fixed card width. [01]

### Search

- [MISSING] Search submit-on-Enter → catalog route navigation (desktop does in-place search). [02]
- [MISSING] Search `autoFocus` prop. [02]

### Dialogs / Overlays

- [MISSING] Reusable `ConfirmDialog` wrapper component with title/description/confirmLabel/variant/onConfirm. Desktop has ad-hoc ContentDialog instances. [02]
- [MISSING] `variant="destructive"` red button on all delete confirmations (Collections, Profiles, Admin Users, Admin Collections, Admin API Keys, Admin Invite Codes). [02, 05]
- [MISSING] `ErrorBoundary` component-level catch with user-facing "Something went wrong" fallback + Refresh button. Desktop's `UnhandledException` silently writes `crash.txt` and sets `args.Handled=true`. [02]
- [MISSING] `AlertDialogMedia` icon slot (64px bg-muted rounded-md) for confirm dialogs. [03]
- [MISSING] Dialog `size="sm"` variant (`max-w-xs=320`). [03]
- [MISSING] Dialog `rounded-xs` close-button convention (top-right X with opacity 70→100 on hover). WinUI ContentDialog uses bottom-footer buttons. [03]

### Typography / Icons

- [MISSING] Display font family distinct from body (web `--font-display`). Desktop `FontFamily` and `DisplayFontFamily` always same. [01]
- [MISSING] FontSize tokens at 30/36/48/60/72/96 (Tailwind 3xl-8xl) for cinematic headers. Desktop caps at 42. [01]
- [MISSING] `FontSizeXs=10`, `FontSizeSm=12`, `FontSizeBase=16` tokens matching `text-xs/sm/base`. [03]
- [MISSING] `IconSizeXs=12`, `IconSizeStandard=16`, `IconSizeMd=14`, `IconSizeLg=32` resources. [03]
- [MISSING] `Z_INDEX` scale (dropdown 10 / sticky 20 / fixed 30 / overlay 40 / modal 50 / popover 60 / toast 70 / tooltip 80). [01]

### Cache / Polling

- [MISSING] `refetchOnReconnect` listener (no network-change listener). [01, 04]
- [MISSING] Retry policy for GET queries (1 retry, 0 on mutations). [01]
- [MISSING] `useHistoryImportRun` 2s polling while queued/running. [04, 05]
- [MISSING] Session-count badge increment via shell 15s timer (only implemented for that one case; dashboard sessions don't refresh). [04]

### Admin (medium)

- [PARTIAL] Admin Users tab layout (3-column: User avatar+name/email, Role, Status). [06]
- [PARTIAL] AdminDashboard Recent Activity card with `AdminSessionActions compact`. [06]
- [PARTIAL] AdminDashboard libraries card `{type} · N paths` subtitle. [06]
- [PARTIAL] AdminActivity summary strip (play method distribution + by-node stacked bar). [06]
- [PARTIAL] AdminActivity mobile responsive block layout. [06]
- [PARTIAL] AdminActivity search clear (X) button. [06]
- [PARTIAL] AdminActivity type filter active/inactive visual state. [06]
- [PARTIAL] AdminSettings number/text/password field onBlur commit vs immediate mutation. [06]
- [PARTIAL] AdminRecommendations section collapse state persistence. [06]
- [PARTIAL] AdminRecommendations progress percentage calculation per job. [06]
- [PARTIAL] AdminRecommendations Loader2 spinner animation while running. [06]
- [PARTIAL] AdminTaskDetail trigger form (4 trigger types: interval/daily/weekly/startup + Max runtime + Remove). [06]
- [PARTIAL] AdminSubtitleProviders "Test Connection" button + Eye/EyeOff API key visibility toggle. [06]
- [PARTIAL] AdminProviders MetadataProviderForm per-provider field maps. [06]
- [PARTIAL] AdminCatalogMaintenance path rewrite row editor. [02]
- [PARTIAL] AdminCatalogMaintenance `formatExportProgressLabel` thousand separators. [02]
- [PARTIAL] AdminCatalogMaintenance import conflict mode + path rewrites. [02]

### Hooks / API parity (medium)

- [PARTIAL] `DELETE /profiles/{id}` missing. [04]
- [PARTIAL] `GET /catalog/series/{seriesId}/seasons/{num}` single-season detail missing. [04]
- [PARTIAL] `GET /catalog/items/{id}/episodes` (by content_id for season) missing. [04]
- [PARTIAL] `GET /watch/{id}?fileId=N` — no fileId param support. [04]
- [PARTIAL] `GET /progress?library_id=N` missing library filter. [04]
- [PARTIAL] `GET /profile/sections?scope=...&library_id=...` missing scope params. [04]
- [PARTIAL] `DELETE /profile/sections/reset?scope=...` missing params. [04]
- [PARTIAL] `GET /recommendations/recently-added?days=N` missing days param. [04]
- [PARTIAL] `GET /ratings` "my ratings" list UI missing (endpoint exists). [04]
- [PARTIAL] CatalogApi.GetCatalogAsync URL shape mismatch (hard-codes `library_id=` instead of `source=library&source_id=`). [04]

### Thumbhash / image

- [MISSING] Thumbhash module-level Map caches (data URL + hex). Desktop decodes every time. [01]
- [MISSING] Thumbhash empty-input short-circuit (desktop throws). [01]
- [MISSING] `getAverageColor(base64)` export. [01]
- [MISSING] Thumbhash Rec.709 luminance guard. [01]
- [MISSING] Thumbhash saturation guard. [01]

### Centralized helpers / utilities

- [MISSING] Storage key unification: volume/muted, current_profile cache. [01]
- [MISSING] People API signature alignment (string PersonId; limit=24; no sort=year&order=desc). [01]
- [MISSING] `PLAYBACK_QUALITY_OPTIONS` global export (desktop embeds locally). [01]
- [MISSING] `SECTION_TYPES` const + `sectionTypeLabel` + `isFilterSectionConfig` guard. [01]
- [MISSING] `createEmptyQueryDefinition` / `normalizeQueryDefinition` / legacy `rating → rating_imdb` migration. [01]
- [MISSING] `queryDefinitionFromSectionConfig` / `queryDefinitionToSectionConfig` legacy field handling. [01]
- [MISSING] Centralized `TimeAgo` helper (see B29). [01]
- [MISSING] `formatFileSize(bytes)` helper. [06]
- [MISSING] `formatRelativeTime` / `formatNextRun` helpers for Admin Tasks. [06]
- [MISSING] `formatConnectionState(state)` helper. [06]
- [MISSING] `QueryCacheManager` profile-switch cache clear (favorites, watchlist, history, collections, libraryPlaybackPreferences, progress, sections). [01]
- [MISSING] `SetupGate` / `RequireAuth` / `RequireProfile` / `RequireAdmin` route guards — desktop does ad-hoc. [01]
- [MISSING] Setup status check at launch (call `GET /auth/setup`). [01]

### Types / models

- [MISSING] `ImpersonationInfo` type. [01]
- [MISSING] `CreateProfileRequest` full field set (desktop only has Name). [01]
- [MISSING] `FileVersion` extended fields: `file_path`, `added_at`, `video_tracks[]`, `subtitle_tracks[]`, `VersionVideoTrack`, `VersionAudioTrack`, `VersionSubtitleTrack` full shapes (profile, level, bit_depth, pixel_format, color primaries/space/transfer, reference_frames, frame_rate, bitrate). [01]
- [MISSING] `SubtitleInfo` extras: `index?`, `embedded_title?`, `resolution?`, `default?`, `hearing_impaired?`, `external?`, `file_name?`. [01]
- [MISSING] `MediaItemDetail.subtitles`, `MediaItemDetail.intro`, `MediaItemDetail.credits` fields. [01]
- [MISSING] `ItemDetailUserData` discriminated union (Leaf vs Season). [01]
- [MISSING] `BrowseItem.status` field. [01]
- [MISSING] `SettingsSectionEntry` / `SettingsSectionsResponse` / `CollectionSectionConfig` / `PageSectionConfig` / `PageSectionListResponse`. [01]
- [MISSING] `FilterRule` / `FilterGroup` / `FilterConfig` separate types with sort/order. [01]

---

## PRIORITY 5 — LOW PRIORITY / COSMETIC

Copy mismatches, minor styling differences, and polish items.

### Copy / text

- [MINOR] Collections subtitle copy mismatch. Web: "Build personal or shared shelves around moods, series arcs, or anything else worth grouping." Desktop: "Organize your media into custom collections and smart lists." [05]
- [MINOR] Collections empty state copy mismatch (web terse one-line; desktop richer title+subtitle+CTA). [05]
- [MINOR] Collections loading text "Loading collections...". [05]
- [MINOR] CollectionEditor header subtitle missing. [05]
- [MINOR] CollectionEditor save button label ("Save Collection" vs "Create Collection" / "Save Changes"). [05]
- [MINOR] Recommendations subtitle copy mismatch. [05]
- [MINOR] Signup description copy mismatch. [05]
- [MINOR] SectionRow section type label strings. [02]
- [MINOR] ImpersonationBanner button text casing ("End Impersonation" vs "End impersonation"). [02]
- [MINOR] Settings tab label differences (Import vs History Import; Subtitles vs Subtitles). [05]
- [MINOR] Appearance settings "Hover to preview..." hint. [05]
- [MINOR] Various "sent" / "command sent" toast wording (web toasts; desktop silent). [02]

### Styling

- [MINOR] Collections delete dialog title casing (web lowercase). [05]
- [MINOR] ProfileSelectPage delete red destructive styling. [05]
- [MINOR] `NoCollections` empty state copy richness difference. [05]
- [MINOR] EpisodeRow `rounded-r-sm` end-cap on progress bar. [02]
- [MINOR] Hero navigation arrows styled different (desktop always 0.7 opacity). [02]
- [MINOR] AppSidebar "Media server" subtitle + brand block. [02]
- [MINOR] AppSidebar "truncate" transition on collapse. [02]
- [MINOR] Setup wizard step indicator Check icon on completed steps (desktop only lowers opacity). [05]
- [MINOR] Intro button style (desktop uses AccentButtonStyle instead of semi-transparent black). [07]
- [MINOR] Stats overlay keyboard shortcut I (desktop advantage to keep). [07]
- [MINOR] Minimize keyboard shortcut N (desktop advantage to keep). [07]

### Design tokens / dead code

- [MINOR] `BadgeStyle` dead code — unused (PosterCard rolls its own). [03]
- [MINOR] `RadiusPill=9999` unused. [03]
- [MINOR] `RingBrush` unused outside declaration. [03]
- [MINOR] `MutedBrush`, `Chart1Brush-Chart5Brush` unused. [03]
- [MINOR] `SidebarForeground/Primary/Ring` brushes unused outside declarations. [03]
- [MINOR] `PopoverForegroundBrush`, `CardForegroundBrush`, `DestructiveForegroundBrush` declared but referenced ~0 times. [03]
- [MINOR] `FontSizeBranding*` / `FontSizeDisplayLarge` partial usage. [03]
- [MINOR] Duration tokens declared but no Storyboards use them. [03]
- [MINOR] `Chart1-5 brushes` — no chart widgets. [03]
- [MINOR] Dead `TrimHlsManifestAsync` helper in HlsProxy — never called. [07]

### Minor audio/codec labels

- [MINOR] Audio flyout label format dash vs middle-dot separators. [07]
- [MINOR] Audio switch stops mpv before loading new URL (visible black frame). [07]
- [MINOR] `bufferAppendError` unmount-before-delete workaround. [07]
- [MINOR] Volume persistence scale (float vs int — internal only). [07]
- [MINOR] Volume slider fade-on-hover (design difference). [07]
- [MINOR] `controlsVisible` sync between XAML overlay and Lua OSC. [07]

### Minor admin

- [MINOR] Admin Libraries error message in Last Scanned column. [06]
- [MINOR] Admin Nodes count badge on section header. [06]
- [MINOR] Admin Sections helper text ("Use the arrow buttons" is fine). [06]
- [MINOR] Admin Recommendations Password onBlur commit. [06]
- [MINOR] Admin ApiKeys rate tier dropdown active highlight. [06]
- [MINOR] AdminActivity connection state wiring. [06]

### Minor hooks / API

- [MINOR] History Import Emby Connect helper text. [05]
- [MINOR] Settings pending / "Loading..." / "Verifying..." button label states. [05]
- [MINOR] Toast "Profile created" on setup wizard step 2. [05]
- [MINOR] Toast "Setting saved" after NextUp change. [05]
- [MINOR] Library `sanitizeEnum(VALID_GENRES)` validation. [05]

### Minor player

- [MINOR] Title location top-left (desktop in bottom bar). [07]
- [MINOR] Play/Pause icon size parity (20dip vs h-5 w-5). [07]
- [MINOR] Seek bar thickness 1px → 2px on hover. [07]
- [MINOR] Seek bar white thumb opacity-0→100 on hover. [07]
- [MINOR] Mini player bar thumbnail empty Grid cleanup. [07]
- [MINOR] Mini bar `OnFrameReady`/`_miniBitmap`/`PresentFrame` dead code removal. [07]
- [MINOR] "Go Back" button in error overlay styling. [07]

---

## DESKTOP-EXCLUSIVE FEATURES (preserve these)

Features the desktop has that WebUI does NOT. **Do not remove these during parity work.**

1. **Timer-based proactive token refresh** at 80% of `expires_in` (`AuthService.ScheduleRefresh`) — web relies purely on lazy 401 + bootstrap. Better for long-running playback.
2. **JWT claim parsing fallback** for role/username (`AuthService.TryParseUserFromJwt`).
3. **Credential Manager** integration via `advapi32.dll` for refresh token storage (more secure than localStorage).
4. **Crash log** at `%LocalAppData%/ContinuumPlayer/crash.txt` on unhandled exceptions.
5. **Player lifecycle hooks** on MainWindow (`HandleWindowResize`, `HandleWindowMinimized`) for the owned-popup mpv window.
6. **State trace log** (`MainWindow.LogState`).
7. **`HlsProxy`** for local-port HLS proxying with 45s segment retry.
8. **`SmoothScrollHelper`** vertical mouse wheel smoothing.
9. **Settings persisted as `settings.json`** (single file camelCase) vs web localStorage key-by-key.
10. **Service-provider DI with `IServiceProvider`** — cleaner than ad-hoc module state.
11. **Mini player bar** — WebUI does NOT have one (navigation stops playback).
12. **Server selection (multi-server)** — web has no concept of switching servers.
13. **OAuth provider section on login** — WebUI has it too but desktop also ships providers slot wiring.
14. **Downloads page** — desktop-only user-facing page.
15. **Plugins settings tab** — desktop-only.
16. **Sessions settings tab** — desktop-only.
17. **Admin History Import page (standalone)** — desktop-only (web folds into admin maintenance).
18. **Letter jump / alphabet nav** — confirmed in library views.
19. **`AdminPluginsPage`** — desktop-only admin page.
20. **Full-window `LoadingOverlay` + `PlayerOverlay` + `MiniPlayerBar`** z-stacked layers above NavigationView.
21. **Popup player architecture** (owned popup over the main window) vs web's in-page video element.
22. **Session ID display in stats** (advantage over WebUI).
23. **Live bandwidth from mpv `cache-speed`** (advantage over WebUI).
24. **Keyboard shortcuts I (toggle stats) and N (minimize)** — desktop-exclusive advantages.
25. **HomeViewModel 5-min cache** (longer than web's 2 min — slightly stale but faster revisits).
26. **Settings `HiddenLibraryIds`** desktop-only preference for hiding libraries from sidebar (should be synced, see 04).
27. **`SubtitleAppearance`/Subtitles Settings local apply** to mpv (once wired — advantage over web which applies to DOM).
28. **`GetEmbeddingLockCard` / `RecEmbeddingLockCard`** detailed model fields.
29. **Hardware-accelerated GPU mpv rendering** (`vo=gpu`, D3D11VA, DXVA2, Vulkan).

---

## CROSS-CUTTING THEMES

High-level patterns that emerge across multiple audits. Each of these unlocks or correlates with many individual gaps.

1. **No URL routing / deep linking** — affects Library tabs, Settings subpages, Signup invite codes, WatchRoute fileId, AdminPlaybackHistory mediaItemId, Back/Forward navigation, Catalog state, and roughly 40+ individual gap items. See F3.
2. **No document title / window title sync** — every page surface would update this. See F7.
3. **No toast notification system** — 20+ mutation sites across Collections, Settings, HomeScreen, HistoryImport, Admin rely on transient feedback. See F2.
4. **No skeleton loading states** — every page with async data loads. See F6.
5. **No drag-and-drop anywhere** — HomeScreen section reorder, Collection manual item reorder, Admin Sections reorder. All use arrow buttons.
6. **No sidebar pin system** — `useSidebarPins`, `useToggleSidebarPin`, `PinnedCollections`, `PinnedCollectionCarousel`, `SectionPinButton` all missing. Affects Library page, AppSidebar, SectionRow header.
7. **No ambient color extraction pipeline** — HeroBanner, DetailHero, card thumbhash backgrounds. See F11.
8. **No view transitions** — `ViewTransitionLink`, `document.startViewTransition`, reduced-motion support all missing.
9. **No cross-surface invalidation** — every mutation leaves stale data in other ViewModels. See F4.
10. **No `ControlTemplate` overrides on WinUI primitives** — every control uses stock Fluent visuals with system accent, making the app visually inconsistent with the cobalt-studio palette. See F1.
11. **No optimistic updates** — zero optimistic mutations in the entire desktop app. Every mutation awaits server round-trip before updating UI state.
12. **No accessibility settings** — text scale, text weight, high contrast entirely absent.
13. **No responsive breakpoints** — desktop pages are fixed-width. Web uses `sm:` `md:` `lg:` `xl:` throughout.
14. **WebUI-exclusive feature gap: images/artwork upload** — `ImageUploadField` (collections, libraries, users posters/backdrops/avatars) doesn't exist. Every admin edit form in desktop has no way to upload artwork.

---

## APPENDIX: Source audit index

| File | Lines | Covers |
|------|-------|--------|
| `docs/audit-gaps/01-api-lib-utils-gaps.md` | 468 | App root/routing, global CSS/theming, API client, query caching, thumbhash, impersonation storage, playback quality helpers, subtitle appearance model, themes definition, TimeAgo, document title, storage utility, carousel helper, design system constants, types parity |
| `docs/audit-gaps/02-components-gaps.md` | 694 | HeroBanner, SectionRow/MediaCarousel, ContinueWatchingCard, ItemCard (PosterCard), CastCarousel, CrewList, EpisodeRow, SeasonAccordion, StarRating, MediaItemMenu, AppSidebar, AdminSidebar, Layout/AdminLayout, ImpersonationBanner, FilterBar/AdvancedFilterBar, ScrollToTopButton, SearchBar, ConfirmDialog, ErrorBoundary, HomeRedirect, ImageUploadField, InfoCard, LibraryAccessSelector/Multi, RecommendationGrid, SectionDrawer, CollectionSearchableSelect, ThemeSwitcher, ViewTransitionLink, FilterRuleEditor, AdminCatalogMaintenance, AdminSessionActions/StreamProvider, collections/* editors, settings/SettingsGroup |
| `docs/audit-gaps/03-ui-primitives-gaps.md` | 556 | 22 shadcn/Radix primitives + global theme surface: Button, Badge, Dialog, DropdownMenu, Select, Input, Label, ScrollArea, Slider, Switch, Sheet, Skeleton, Tabs, Accordion, Avatar, Card, Separator, Sonner, SearchableSelect, Table, indicator positions, SVG icon sizing, typography, asChild pattern, design token summary |
| `docs/audit-gaps/04-hooks-gaps.md` | 797 | 79 WebUI hook files vs desktop ViewModels/Services/API clients: theme, auth, impersonation, current profile, document title, carousel embla, catalog, collectionPreviews, collectionSurfaceRefresh, episodes, favorites, history-import, history, homeDismissals, items/watchDetail/refresh metadata, libraries, libraryCollections, libraryPlaybackPreferences, mediaSurfaceRefresh/playbackSurfaceRefresh/ratingsSurfaceRefresh, playbackProgressCache, profiles, progress, ratings, recommendations, sections (home/admin/profile), settings, sidebarPins, useAllUserCollections, watchlist, admin/apiKeys, admin/collections, admin/history-import-sources, admin/history, admin/inviteCodes, admin/ips, admin/libraries, admin/logStream, admin/logs, admin/nodes, admin/providers, admin/rateLimits, admin/recommendations, admin/settings, admin/stats, admin/subtitles, admin/tasks, admin/users |
| `docs/audit-gaps/05-pages-user-gaps.md` | 622 | Every user-facing page: Catalog (unified Search/Favorites/Watchlist/History/Person), CollectionEditor, Collections, Home, Library (Recommended/Browse/Collections tabs), Recommendations, Login, Signup, ProfileSelect, SetupWizard (all 5 steps), WatchRoute, SettingsLayout, all 7 settings subpages (Appearance, Accessibility, Playback, Libraries, HistoryImport, SubtitleAppearance, HomeScreen) |
| `docs/audit-gaps/06-pages-detail-admin-gaps.md` | 464 | ItemDetailPage (Movie/Series/Season/Episode content), DetailHero, action bar, metadata badges, score row, cast carousel, crew list, season navigation, episode row, season/episode content, version flyout, similar items, skeletons, and every admin page: Shell nav, Dashboard, Activity, Users, UserDetail, ApiKeys, Collections (list + editor), Libraries, Logs, Maintenance, Nodes, PlaybackHistory, Recommendations, Sections, Stats, Tasks, TaskDetail, InviteCodes, SubtitleProviders, Providers, SettingsDetail (all tabs), Plugins, HistoryImport |
| `docs/audit-gaps/07-player-gaps.md` | 413 | Player controls layout, seek bar, volume, quality menu, audio track menu, subtitle menu, subtitle rendering/appearance, intro/credits skip, next episode overlay, playback info/stats overlay, playback notice overlay, keyboard shortcuts, mouse behavior, progress reporting, WebSocket realtime control, HLS streaming, direct play, codec declaration, resume hints, mini player bar, session/loading/error, render/visibility, API endpoints |

---

*End of master parity roadmap. See individual gap documents for file/line references not repeated here.*
