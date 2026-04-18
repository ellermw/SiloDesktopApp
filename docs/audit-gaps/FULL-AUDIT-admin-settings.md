# Admin Settings — Full Audit

Scope: every sub-page under `web/src/pages/admin-settings/` vs. the single desktop page `AdminSettingsDetailPage` and its ViewModel.

## Files read (100% line-by-line)

Webui (all in `F:/continuum-server/web/src/pages/admin-settings/`):
- `AdminSettingsLayout.tsx` — 127 lines
- `GeneralSettings.tsx` — 92 lines
- `DatabaseSettings.tsx` — 178 lines
- `DatabaseSettings.test.tsx` — 72 lines
- `DownloadSettings.tsx` — 87 lines
- `FieldGroup.tsx` — 20 lines
- `IntegrationsSettings.tsx` — 275 lines
- `InviteCodesTab.tsx` — 249 lines
- `JellyfinSettings.tsx` — 100 lines
- `LogRetentionSettings.tsx` — 378 lines
- `OverlaySettings.tsx` — 201 lines
- `PlaybackSettings.tsx` — 210 lines
- `PluginsSettings.tsx` — 379 lines
- `RateLimitSettings.tsx` — 404 lines
- `SaveBar.tsx` — 73 lines
- `ScannerSettings.tsx` — 100 lines
- `SettingField.tsx` — 172 lines
- `StorageSettings.tsx` — 379 lines
- `StorageSettings.test.tsx` — 46 lines
- `ThemeSettings.tsx` — 200 lines
- `logRetentionPolicy.ts` — 85 lines
- `recommendationsSettings.ts` — 157 lines

Desktop:
- `src/ContinuumPlayer/Views/Admin/AdminSettingsDetailPage.xaml` — 188 lines
- `src/ContinuumPlayer/Views/Admin/AdminSettingsDetailPage.xaml.cs` — 2586 lines
- `src/ContinuumPlayer/Views/Admin/AdminShellPage.xaml` (sidebar routing context)
- `src/ContinuumPlayer/ViewModels/Admin/AdminSettingsDetailViewModel.cs` — 218 lines
- `src/ContinuumPlayer.Core/Api/AdminApi.cs` (settings section, lines 119–148 verified via grep)

## Files sandbox-blocked (UNVERIFIED claims below rely on inference from call sites + tests)

- `F:/continuum-server/web/src/hooks/useSettingsForm.ts` — blocked. Shape inferred from component usage + `DatabaseSettings.test.tsx` / `StorageSettings.test.tsx` mocks: `{ isLoading, getValue(key), setValue(key, value), dirtyCount, save(), discard(), isSaving, restartRequired, sensitiveConfigured: string[], sensitiveManagedByEnv: string[], buildConnectionCheckRequest(keys) }`.
- `F:/continuum-server/web/src/components/admin/ConnectionCheckAction.tsx` — blocked. Contract inferred from call sites: `{ onClick, result: ConnectionCheckResponse|null, isPending, disabled }`. Renders a Check Connection button + inline success/failure text.
- `F:/continuum-server/web/src/hooks/queries/admin/settings.ts` — blocked. Endpoint shape confirmed via desktop `AdminApi.cs`: `GET /admin/settings`, `PUT /admin/settings/{key} {value}`, `GET /admin/settings/sensitive-status`, `POST /admin/settings/check/{kind}`. `useAdminServerSettings` / `useUpdateServerSetting` / `useCheckAdminSettingsConnection` hooks are called by several tabs.
- `F:/continuum-server/web/src/app.css` — blocked. Tailwind class-to-brush mapping below is the best-effort reading from classnames (`surface-panel`, `surface-panel-subtle`, `page-title`, `page-subtitle`, `rounded-[1.8rem]`, `rounded-2xl`, `rounded-xl`, etc.).
- `F:/continuum-server/web/src/hooks/queries/admin/rateLimits.ts`, `subtitles.ts`, `plugins.ts` — blocked. Shapes inferred from component usage.
- `F:/continuum-server/web/src/components/theme/{TokenEditor,RawCssEditor,ThemePreviewCard}.tsx` — blocked. Absent on desktop (just a JSON textarea + preview stub). Unverified structural claims marked.

---

## Navigation / shell structure

**Webui** (`AdminSettingsLayout.tsx`)
- Single route `/admin-settings` with a `?tab=<id>` search param switching inner content. `useSearchParams` persists deep-linkable tabs.
- Outer layout:
  - Page header: `h1.page-title` (Tailwind `text-[clamp(2rem,4vw,3rem)]`) + `p.page-subtitle` ("Configure server-wide settings. Most changes require a server restart to take effect.") — `space-y-3` wrapper.
  - Surface panel: `div.surface-panel flex min-h-[500px] flex-col overflow-hidden rounded-[1.8rem] border-0 lg:flex-row`. Corner radius **`1.8rem` = 28.8px** (≈ 29).
  - Left sub-nav sidebar (`nav` with `role=tablist`), `lg:w-56` (= 14rem = **224 px**), `lg:flex-shrink-0`, right border `lg:border-r`, padding `lg:px-0` (full-bleed), mobile → horizontal scroll tab strip.
  - Right content area: `p-4 sm:p-6` (**16 / 24 px**), `overflow-y-auto`.
- 12 nav items, in this order:
  1. `general` — "General" — icon `Settings2`
  2. `theming` — "Theming" — `Paintbrush`
  3. `playback` — "Playback" — `PlayCircle`
  4. `scanner` — "Scanner & Matcher" — `ScanSearch`
  5. `rate-limiting` — "Rate Limiting" — `Gauge`
  6. `downloads` — "Downloads" — `Download`
  7. `integrations` — "Integrations" — `Puzzle`
  8. `jellyfin` — "Jellyfin Compat" — `MonitorPlay`
  9. `database` — "Database" — `Database`
  10. `storage` — "Storage" — `HardDrive`
  11. `log-retention` — "Log Retention" — `ScrollText`
  12. `overlays` — "Card Overlays" — `Layers`
- Nav button: `relative flex min-w-max items-center gap-2.5 rounded-xl px-4 py-2.5 text-left text-[13px] font-medium` = **12 px corner radius, 16 / 10 px padding, 13 px text, weight 500, gap 10 px**. Inactive `text-muted-foreground`; active `text-foreground bg-accent`; icon 16 px.
- Active-indicator pill (`span`): on desktop `lg:h-[18px] lg:w-[3px] lg:rounded-r-sm` — a **3×18 px** vertical accent bar pinned to the left edge (background `var(--primary)`). On mobile `h-[3px] w-8` — a horizontal underline.
- **PluginsSettings is NOT in the layout nav** (confirmed — `PluginsSettings.tsx` exists on disk but `AdminSettingsLayout.tsx` does not import it). Plugins are a separate admin route elsewhere.
- **InviteCodesTab is NOT in the layout nav** — it lives in the Users page tabset, not Settings. Confirmed absent from `SETTINGS_NAV`.
- **RecommendationsSettings are NOT in the layout nav** — `recommendationsSettings.ts` is pure data and is rendered by a different page (admin recommendations).

**Desktop** (`AdminSettingsDetailPage.xaml` + `.xaml.cs` lines 24–38)
- Invoked from `AdminShellPage.NavSettings_Click` → `Navigate(typeof(AdminSettingsDetailPage))`. No deep-linking — there is no query-string or navigation-parameter equivalent. Refresh resets to "General".
- Outer layout:
  - Header: `TextBlock FontSize={FontSizeDisplayLarge} Bold` ("Settings") + 14-px secondary text. Padding `40,28,40,40` with `Spacing="24"`.
  - Card: `Border Background=CardBackgroundBrush CornerRadius="28" MinHeight="500"` — corner radius **28 px** (vs. web ≈ 29 px). Close.
  - Two-column Grid: **left column Width="224"** (matches web), right column star. Left sidebar `BorderThickness="0,0,1,0"` matches `lg:border-r`. Right scroll viewer `Padding="28,24,28,28"` (web `p-4 sm:p-6`).
- 12 tabs in same order and labels as web (lines 24–38) with Segoe Fluent glyphs standing in for the Lucide icons. Tab button (`BuildSidebarNavButton`): `Padding 14,9,12,9`, `CornerRadius 10` (web uses 12), text size 13/FontWeights.Medium, icon 14 (web 16), gap column-spacing 10.

### Shell gaps

| # | Aspect | Webui | Desktop | Severity |
|---|---|---|---|---|
| NAV-1 | Deep-linkable tab | `?tab=playback` URL persists | No state persisted across navigations — always re-opens on General | functional |
| NAV-2 | Active indicator pill | 3×18 px left accent bar (`var(--primary)`) | No pill; active state is only background color change | visual |
| NAV-3 | Nav button corner radius | `rounded-xl` = 12 px | 10 px (`CornerRadius(10)`) | visual drift |
| NAV-4 | Nav icon size | 16 px (`h-4 w-4`) | 14 px (`FontSize = 14`) | visual drift |
| NAV-5 | Outer surface corner radius | `rounded-[1.8rem]` ≈ 29 px | 28 px | acceptable (within 1 px) |
| NAV-6 | Mobile behavior (horizontal nav strip) | yes via `flex gap-1 lg:block` | n/a on desktop client | n/a |
| NAV-7 | `role="tab"`, `aria-selected`, `aria-controls` | all set | none of these accessibility attrs wired on WinUI Buttons | functional (a11y) |
| NAV-8 | Page header text | "Configure server-wide settings. Most changes require a server restart to take effect." | Same verbatim | match |

---

## Shared-primitives audit

### useSettingsForm (web) vs. AdminSettingsDetailViewModel (desktop)

Webui `useSettingsForm({ keys })` exposes (inferred — UNVERIFIED for exact signatures):
- `isLoading`, `isSaving`, `restartRequired`, `dirtyCount`
- `getValue(key)`, `setValue(key, value)`
- `save()`, `discard()`
- `sensitiveConfigured: string[]` — which sensitive keys the server reports as already set
- `sensitiveManagedByEnv: string[]` — which sensitive keys are overridden by env vars (shows "Managed by environment" badge + disables input)
- `buildConnectionCheckRequest(keys: string[])` — packages the *current* (incl. dirty) values for `POST /admin/settings/check/{kind}`

Desktop `AdminSettingsDetailViewModel` (file:218 lines):
- `IsLoading`, `IsSaving`, `DirtyCount`, `HasDirtyChanges`, `StatusMessage`, `ErrorMessage`
- `GetSetting(key)`, `SetSetting(key, value)`
- `SaveCommand`, `DiscardCommand`
- `IsSensitiveConfigured(key)` ✓
- `GetEffectiveSettings()`, `GetDirtyKeys()` (used to build connection-check body)
- **NO `restartRequired` flag** — desktop always shows the same static "Server restart may be required for changes to take effect." subtitle (`xaml:144`) regardless of which keys changed.
- **NO `sensitiveManagedByEnv` tracking** — desktop has no concept of env-locked settings; the Redis managed-by-env badge path in the webui cannot render on desktop.
- Desktop only scopes one global settings bag — it does not hit `keys` filter like the web does. Every tab operates over the same `_settings` Dictionary and every tab submits its own dirty keys. That part is fine.

**Shared-primitive gaps**

| # | Primitive | Webui | Desktop | Severity |
|---|---|---|---|---|
| PRIM-1 | `restartRequired` per-tab flag | Set by `useSettingsForm` based on which keys are dirty; SaveBar shows the amber warning + "Restart Server" button only when relevant | Static hint always visible; no Restart Server button | critical |
| PRIM-2 | "Restart Server" button in SaveBar | Calls `POST /admin/server/restart`; wrapped in `ConfirmDialog` ("The server will restart to apply configuration changes. Active streams will be interrupted.") | Not implemented | critical (functional) |
| PRIM-3 | `sensitiveManagedByEnv` "Managed by environment" badge + disabled fields | Present (Database tab for `REDIS_URL`) | Never rendered | functional |
| PRIM-4 | "Managed by environment" explainer text | "Redis is configured by the `REDIS_URL` environment variable. Change your deployment configuration and restart the server to update or disable Redis." | Missing | functional |
| PRIM-5 | `sensitiveConfigured` placeholder | Displays `"configured"` text inside the password input (via `SettingField.tsx` password branch) | Desktop shows `"•••• configured"` placeholder + "configured" badge next to label — actually *more* visible than web | visual (positive divergence) |
| PRIM-6 | `sensitiveConfigured` badge next to label | None on web | Desktop adds a small badge ("configured") next to the label | visual (divergence) — remove badge on desktop, rely on placeholder text to match 1:1 |
| PRIM-7 | Discard button semantics | Reverts form dirty map | Desktop reloads the entire active tab UI (`ShowTab(_activeTab)` in `DiscardButton_Click`) — flashes the scroll position to top and rebuilds every control | functional |
| PRIM-8 | Dirty count text | `{dirtyCount} unsaved change{s}` — space, no period | Same format (`AdminSettingsDetailPage.xaml.cs:2554`) | match |

### SettingField component

Webui `SettingField.tsx` (172 lines) is the single shared field primitive with these types:
- `toggle` — `Label` + `Switch` in `flex-row sm:items-center py-3`; hint text `text-xs text-muted-foreground`; text **13 px medium** label.
- `select` — `Label` + `Select` (trigger width `sm:w-fit`); label **13 px medium**; `py-2`.
- `password` — `Label` + `Input type=password`; placeholder = `"configured"` when `sensitiveConfigured=true` else hint or `"Not configured"`; input `max-w-md` (= 28rem = **448 px**).
- `number` — `Label` + `Input type=number`; width `sm:w-40` (= 10rem = **160 px**).
- `text` / `duration` — `Label` + `Input type=text max-w-md`; duration uses `placeholder=hint` plus a `<p class="text-muted-foreground text-xs">` hint below.

Desktop equivalents live as ad-hoc builder methods on the page (lines 2068–2547): `AddTextField`, `AddPasswordField`, `AddNumberField`, `AddToggleField`, `AddDurationField`, `AddSelectField`, `AddMultilineTextField`, `AddConditionalToggleWithNumberField`. They broadly mirror spacing and 13-px labels, with these drifts:

| # | Aspect | Webui | Desktop | Severity |
|---|---|---|---|---|
| FLD-1 | Text/duration input max width | `max-w-md` = 448 px | `MaxWidth = 460` — 12 px wider | visual (minor) |
| FLD-2 | Number input width | `sm:w-40` = 160 px | 180 px (`Width = 180`) | visual (minor) |
| FLD-3 | Label font size | 14 px (`text-sm`) | 13 px | visual drift (typography 1 px small) |
| FLD-4 | Hint font size | 12 px (`text-xs`) | 11 px | visual drift (typography 1 px small) |
| FLD-5 | Toggle row layout | `flex-col sm:flex-row` with label stack left, Switch right, gap 12 px (`gap-3`) | Grid two-column; label stack left, ToggleSwitch right; web spacing close | match |
| FLD-6 | "Hint below input" for `duration` | Hint rendered as `<p>` below — and is also the placeholder | Desktop renders it both as placeholder and as a line below (match) | match |
| FLD-7 | Toggle "OnContent"/"OffContent" text | Web Switch has no inline label | Desktop uses `OnContent=""/OffContent=""` — but on subtitle provider card (line 764) uses `"Enabled"/"Disabled"` text. Web uses a separate `<Label>` showing "Enabled"/"Disabled" next to Switch, so this matches | match |
| FLD-8 | Discard rebuild | Web: field simply re-reads from form store | Desktop: rebuilds entire tab; `AddPasswordField` explicitly opts out (`_fieldRebuilders.Add(() => { /* don't refill */ })`) | functional drift |

### SaveBar (web) vs. bottom save bar (desktop)

Webui `SaveBar.tsx` (73 lines):
- Wrapper: `surface-panel-subtle mt-6 rounded-xl p-4` — **12-px radius, 16-px padding, top margin 24 px**.
- When `restartRequired=true`: amber `AlertTriangle` + "Server restart required for changes to take effect." inline.
- Footer row: dirty text `"{dirtyCount} unsaved change(s)"` (left) + buttons (right): **Restart Server** (outline, only when `restartRequired`) | **Discard** (outline, disabled when dirtyCount=0) | **Save Changes** (primary, disabled when dirtyCount=0 or saving).
- Restart: confirm dialog ("Restart server?" / "The server will restart to apply configuration changes. Active streams will be interrupted.") — destructive variant.
- Toasts: Save-success uses `sonner` `toast.success("Server is restarting...")` / `toast.error("Could not restart server. Please restart manually.")`.

Desktop save bar (`AdminSettingsDetailPage.xaml:120–185`):
- Fixed bottom strip as Grid.Row="1" in page Grid — **outside** the card panel. Web's save bar lives inside the scrolling tab content.
- Padding `24,14`; border-top `1px`; visibility bound to `HasDirtyChanges`.
- Left text stack: dynamic dirty-count + always-visible "Server restart may be required for changes to take effect." line.
- Right: **Discard** | **Save Changes** only.
- Status toast: a floating `Border` pinned `VerticalAlignment=Top` with hard-coded `#22C55E` green background — see parity doc B27 (line 242) already flagging this.

| # | Aspect | Webui | Desktop | Severity |
|---|---|---|---|---|
| SB-1 | Save bar location | Inline at end of tab content (scrolls with it) | Fixed bottom of page regardless of scroll | functional/visual |
| SB-2 | Corner radius | 12 px (`rounded-xl`) | No rounded corners (full-width strip with top-border) | visual |
| SB-3 | "Restart Server" button | Present when restartRequired | Missing | critical |
| SB-4 | Restart confirmation dialog | Present | N/A | critical |
| SB-5 | Conditional restart banner | Only when restartRequired | Always visible | visual/functional |
| SB-6 | Save-success toast | `sonner` toast | Hard-coded green `#22C55E` inline strip (3-sec auto-hide) | visual (known B27) |
| SB-7 | Disabled-button states | `disabled={dirtyCount === 0}` + `disabled={dirtyCount === 0 || isSaving}` | WinUI Buttons do not visually dim like web (no `OutlineButtonStyle` disabled variant audited) | functional |
| SB-8 | "Saving..." button text | Yes | Not rendered (Save button content never swaps) | functional |

### ConnectionCheckAction (web) vs. `AddConnectionCheckButton` (desktop)

Webui contract (inferred, UNVERIFIED): button + inline success/fail message. Success green / failure red. Pending spinner state via `isPending`.

Desktop (lines 1908–1976): Button + trailing `TextBlock` result; success `#4ADE80`, failure `#EF6B73`; "Checking..." label while pending.

| # | Aspect | Webui | Desktop | Severity |
|---|---|---|---|---|
| CC-1 | Button style | `Button` (default shadcn) | default WinUI Button + hand-tuned padding 14,6/CornerRadius 6 — web default is ≈ 16 px radius | visual drift |
| CC-2 | Request body | `buildConnectionCheckRequest(keys)` — only the *subset* of keys relevant to that check | `ViewModel.GetEffectiveSettings()` sends **every** settings value, not scoped — server must filter. Works but less efficient | functional (minor) |
| CC-3 | Result icon | Unverified (likely `CircleCheck` / `CircleAlert` inline) | No icon — text-only | visual |
| CC-4 | Failure styling | Unverified | Red text | likely match |

---

## SECTION 1: General Settings (tab `general`)

Webui `GeneralSettings.tsx` (92 lines) keys: `auth.access_token_expiry`, `auth.refresh_token_expiry`, `server.log_level`, `server.log_quiet`.
- Header h2 "General" + "Authentication, token lifetimes, and server logging behavior."
- Skeleton loading state: two FieldGroup-worth of skeleton rectangles (8-row header + two groups of two `h-10` rows).
- FieldGroup "Authentication":
  - Access Token Expiry — duration, hint "e.g. 1h, 30m"
  - Refresh Token Expiry — duration, hint "e.g. 30d, 720h"
- FieldGroup "Logging":
  - Log Level — select `[debug, info, warn, error]`
  - Quiet Subsystems — text, hint "Comma-separated subsystem prefixes to silence"
- SaveBar with `restartRequired` from form.

Desktop `BuildGeneralTab()` (lines 341–356) — identical labels, keys, hints, and order.

### 1.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| GEN-1 | Loading skeleton | Per-field `Skeleton` stack | Page-wide `ProgressRing` | critical — matches parity doc pattern "Entire page hidden by loading overlays" |
| GEN-2 | Error state | Webui shows per-field fallback (nothing blocks content) | Centered `ErrorMessage` + Retry button that blocks the whole page | functional |
| GEN-3 | Log Level options labels | Capitalized via explicit `label` field ("Debug", "Info", "Warn", "Error") | `AddSelectField` auto-capitalizes first letter (matches) | match |
| GEN-4 | Quiet Subsystems hint | `hint` below input | Rendered correctly by desktop text-field | match |

---

## SECTION 2: Theming (tab `theming`)

Webui `ThemeSettings.tsx` (200 lines) — settings keys: `ui.admin_theme_vars`, `ui.admin_custom_css`, `theme.catalog_url`, `branding.server_name`, `branding.login_subtitle`.
- Top banner: amber `rounded-xl border border-amber-500/20 bg-amber-500/5 p-4`; `AlertTriangle` icon; title "Server-wide theme customization"; body "These overrides apply to all users as a base layer. Individual users can further customize on top of these settings."
- Sections:
  - **Preview**: live `ThemePreviewCard` driven by the current `vars`; when `hasOverrides`, a small "Reset all" button bottom-right (clears both vars and raw CSS).
  - **Token Overrides**: `TokenEditor` — structured GUI with per-token color pickers. Debounced persist (500 ms) per token.
  - **Custom CSS**: `RawCssEditor`. Debounced persist (1000 ms). Sanitized via `sanitizeCss()` before save.
  - **Branding**: Server Name + Login Page Subtitle; commits on `onBlur`. Placeholders "Continuum" / "Sign in with an existing account.".
  - **Theme Catalog URL**: text field with default `https://raw.githubusercontent.com/ContinuumApp/continuum-themes/main/catalog.json`, commits on blur.
- Saving strategy: **no SaveBar** — every field auto-saves inline (blur / debounce).

Desktop `BuildThemingTab()` (lines 195–270):
- Warning banner — matches (same title, text, icon).
- **Branding card** — Server Name + Login Subtitle, commits via the regular dirty-tracking + SaveBar pattern (NOT blur-commit).
- **Theme Catalog card** — same key.
- **Custom CSS** — rendered as multi-line textbox with monospace 12-px font. No `sanitizeCss` pipeline; no debounce; relies on SaveBar.
- **Token Overrides** — rendered as a plain multi-line JSON textbox tied to `ui.admin_theme_vars`. The webui's `TokenEditor` component (color pickers, per-token reset) and `ThemePreviewCard` live preview are **missing**.
- **No Preview card / Reset-all button.**

### 2.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| TH-1 | Live theme preview card | Yes | Missing | critical |
| TH-2 | Token editor (color pickers, per-token reset) | Yes (`TokenEditor`) | Raw JSON textarea (admits in the header text that "The visual token editor is only available in the web UI.") | critical |
| TH-3 | "Reset all" button | Yes (when overrides exist) | Missing | functional |
| TH-4 | CSS sanitizer | Yes (`sanitizeCss`) | None | security (CSS injection) |
| TH-5 | Debounced auto-save (500 / 1000 ms) + onBlur saves | Yes | No — relies on global SaveBar | functional/UX |
| TH-6 | Branding onBlur auto-commit | Yes | No — waits for SaveBar | functional/UX |
| TH-7 | Warning banner | amber, `border-amber-500/20 bg-amber-500/5`, `rounded-xl`, `p-4 = 16px` | Color matches (`#14FBBF24` / `#33FBBF24`), radius 12 px, padding 14/12 | match (within 2 px) |
| TH-8 | Text "Sign in with an existing account." placeholder | Yes | Matches | match |

---

## SECTION 3: Playback (tab `playback`)

Webui `PlaybackSettings.tsx` (210 lines) keys: `playback.ffmpeg_path`, `playback.transcode_dir`, `playback.hw_accel`, `playback.transcode_enabled`, `playback.allow_hevc_encoding`, `allow_4k_transcode`, `enable_transcode_throttle`, `transcode_throttle_seconds`, `playback.transcode_ahead_segments`, `playback.segment_duration`, `playback.chapter_thumbnail_workers`, `playback.chapter_thumbnail_execution`, `playback.chapter_thumbnail_node_capacity`, `playback.chapter_thumbnail_hdr_policy`, `playback.watched_threshold`, `playback.min_resume_threshold`.

Three FieldGroups:
- **Transcoding**: FFmpeg Path, Transcode Directory, Hardware Acceleration (select `[auto/qsv/vaapi/none]` with labels "Auto", "Intel Quick Sync (QSV)", "VA-API", "Software"), Transcoding Enabled, Allow HEVC Encoding, Allow 4K Transcoding, Enable Transcode Throttling (when true → Throttle Buffer (seconds), hint "How many seconds ahead FFmpeg transcodes before pausing. Minimum: 60.").
- **HW Accel resolved indicator** (lines 66–82): when `hw_accel=auto` and `useHWAccelDetection` returns data, shows a tiny colored dot (emerald when resolved ≠ "none", amber when "none") + text "{formatResolved} — {render_devices[0]} (transcode node)" — real-time diagnostic. Loading state: "Detecting hardware...".
- **Segments**: Transcode Ahead Segments, Segment Duration, Chapter Thumbnail Workers (with long hint), Chapter Thumbnail Execution (select `[local / prefer_transcode_nodes / transcode_nodes_only]` with human labels), Chapter Thumbnail Node Capacity, HDR Chapter Thumbnail Policy (select `[best_effort / disabled]`).
- **Behavior**: Watched Threshold (%) hint "Mark as watched after this % is played (default: 90)", Min Resume Threshold (%) hint "Ignore progress below this % of duration (default: 5)".

Desktop `BuildPlaybackTab()` (lines 358–401) mirrors labels and keys; uses `AddSelectField` with an auto-label mechanism that just capitalizes the first letter.

### 3.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| PB-1 | Hardware Acceleration select labels | Explicit human labels ("Auto", "Intel Quick Sync (QSV)", "VA-API", "Software") | `AddSelectField` just capitalizes values → shows "Auto", "Qsv", "Vaapi", "None" | visual/UX |
| PB-2 | Chapter Thumbnail Execution labels | "Local only", "Prefer transcode nodes", "Transcode nodes only" | Autocapitalized tokens ("Local", "Prefer_transcode_nodes", "Transcode_nodes_only") | visual/UX |
| PB-3 | HDR Chapter Thumbnail Policy labels | "Best effort tone mapping", "Disable HDR/DV thumbnails" | "Best_effort", "Disabled" | visual/UX |
| PB-4 | Auto-HW-accel resolved indicator | Live dot + text ("Intel Quick Sync (QSV) — /dev/dri/renderD128 (transcode node)") | Missing entirely — no `useHWAccelDetection` equivalent | functional |
| PB-5 | "Detecting hardware..." loading state | Yes | Missing | functional |
| PB-6 | Conditional "Throttle Buffer (seconds)" field | Rendered only when `enable_transcode_throttle === "true"` | Desktop implements via `AddConditionalToggleWithNumberField` — matches | match |
| PB-7 | Throttle Buffer hint | "How many seconds ahead FFmpeg transcodes before pausing. Minimum: 60." | Exact match (line 375) | match |
| PB-8 | Field order | Transcoding → Segments → Behavior | Same order | match |
| PB-9 | Watched/resume hints | "default: 90" / "default: 5" | Exact match | match |

---

## SECTION 4: Scanner & Matcher (tab `scanner`)

Webui (100 lines) keys: `scanner.workers`, `scanner.file_removal_grace`, `matcher.workers`, `matcher.batch_size`, `metadata.cache_images`.
- Header + subtitle "Configure scanner performance and metadata matching. Startup and recurring scans are managed in Scheduled Tasks."
- Skeleton: three per-group skeleton blocks while loading.
- FieldGroups:
  - **Scanner**: Scanner Workers (number), File Removal Grace (duration, hint "e.g. 24h").
  - **Matcher**: Matcher Workers (number), Matcher Batch Size (number).
  - **Metadata**: Cache Images to S3 (toggle, hint "Download artwork from metadata providers and store resized variants in public asset S3 storage. Private bucket + presigned URLs is fully supported.").

Desktop `BuildScannerTab()` (lines 403–425): same keys/labels/order.

### 4.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| SCAN-1 | Metadata.cache_images hint wording | "…in public asset S3 storage. Private bucket + presigned URLs is fully supported." | "…store resized variants in S3. Requires General Purpose S3 storage to be configured." — different phrasing referring to desktop's unique "General Purpose" tab naming | visual/UX (minor) |
| SCAN-2 | Skeleton | Yes | Full-page ProgressRing | critical (pattern) |
| SCAN-3 | Section subtitle | "Startup and recurring scans are managed in Scheduled Tasks." | Exact match | match |

---

## SECTION 5: Rate Limiting (tab `rate-limiting`)

Webui `RateLimitSettings.tsx` (404 lines):
- Uses a **dedicated** `useRateLimitConfig` / `useUpdateRateLimitConfig` hook pair (separate from `useSettingsForm`).
- Maintains a local `configState` with a `key = JSON.stringify(hydratedConfig)` reset trick — whenever the server config changes, the key changes and the local state resets to hydrated.
- DEFAULT_CONFIG fallback (lines 31–47) matches desktop's `BuildDefaultRateLimitConfig` (ViewModel:187–206).
- Sections (each wrapped in `surface-panel rounded-2xl border-0 px-5 py-4` → **16 px radius, 20/16 px padding**):
  1. **Enable Rate Limiting** toggle with subtitle "When disabled, no rate limits are enforced.".
  2. **Backend** select `["memory" → "In-Memory"], ["redis" → "Redis"]` + hint "Requires a restart to take effect. Redis is recommended for multi-instance deployments.".
  3. **Global Settings** — "Global Requests Per Second" number input + hint "Maximum requests per second across all clients combined.".
  4. **Per-IP Limits** — subtitle "Applied to all authenticated requests from a single IP address." + 3-column grid: Requests/Second, Requests/Minute, Burst.
  5. For each tier (`standard`, `elevated` — `TIER_LABELS`):
     - Title "{Label} Tier" + subtitle "Per API key limits for the {label} tier."
     - 3-column grid RPS / RPM / Burst.
  6. **Auth Endpoint Limits** — subtitle "Per-IP limits for authentication endpoints to prevent brute-force attacks."; for each of `login`, `signup`, `setup` a sub-header + 2-col RPM / Burst grid.
- Bottom: single "Save Changes" button (`updateConfig.mutate(config)`) — **this tab does not use the shared SaveBar at all.** No discard button.
- Entire form is left-aligned in `max-w-2xl` (= 42rem = **672 px**).

Desktop `BuildRateLimitTab()` (lines 427–632):
- Pulls `DirtyRateLimitConfig ?? RateLimitConfig`. Works on a mutable copy, calls `MarkDirty()` on each edit → ViewModel DirtyCount integrates with shared SaveBar.
- Sections mirror webui: Enable toggle (no explicit section header, matches); Backend select (with human labels "In-Memory"/"Redis"); Global Settings; Per-IP Limits; each Tier; Auth Endpoint Limits (per-endpoint sub-header + 2-col grid).
- Uses `NumberBox` with `Minimum=1` for most fields (matches web `min={1}`).

### 5.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| RL-1 | Save mechanism | Per-tab "Save Changes" button at the bottom of the tab content | Shared SaveBar at page bottom | functional (divergent) — web intentionally isolates rate-limit save |
| RL-2 | Discard button | Absent on web | Present via shared SaveBar | functional (additive — but divergent) |
| RL-3 | Form max width | `max-w-2xl` (672 px) | Rate limit cards span full content area | visual |
| RL-4 | Backend select width | `sm:w-40` (160 px) | 180 px | visual (minor) |
| RL-5 | Global RPS input width | `sm:w-40` (160 px) | 180 px | visual (minor) |
| RL-6 | Section "Enable Rate Limiting" standalone card (no section header) | Yes | First card is a `BeginCard`/`EndCard` inline toggle + Backend (combined) — wraps both in one card | visual |
| RL-7 | Disabled backend restart-required hint | "Requires a restart to take effect. Redis is recommended for multi-instance deployments." | Exact match (line 486) | match |
| RL-8 | Tier labels | "Standard Tier" / "Elevated Tier" | "Standard Tier" / "Elevated Tier" | match |
| RL-9 | Auth-endpoint labels | "Login" / "Signup" / "Setup" | Match | match |
| RL-10 | Invalid-input clamping | Web silently discards `< 1`, `NaN` | Desktop `NumberBox` enforces via Minimum | match |
| RL-11 | Failure feedback on save | Webui mutation toast (via `useUpdateRateLimitConfig` — unverified) | Desktop flows into shared SaveBar StatusMessage/error | match-ish |

---

## SECTION 6: Downloads (tab `downloads`)

Webui (87 lines) keys: `download.enabled`, `download.server_bandwidth_mbps`, `download.user_bandwidth_mbps`, `download.max_concurrent_per_user`, `download.max_per_period`, `download.period_duration`.
- Header + subtitle "Configure download permissions, bandwidth limits, and quotas."
- FieldGroups:
  - **General**: Downloads Enabled (toggle, hint "Allow users to download media files").
  - **Bandwidth Limits**: Server Bandwidth (Mbps) hint "Total download bandwidth for the entire server in megabits/sec. 0 = unlimited."; Per-User Bandwidth (Mbps) hint "Max download bandwidth per user, shared across active downloads. 0 = unlimited.".
  - **Quantity Limits**: Max Concurrent Downloads Per User ("…can have active at once. 0 = unlimited."); Max Downloads Per Period ("Total downloads a user can create per period. 0 = unlimited."); Period Duration ("Rolling window for the per-period limit (e.g., 24h, 168h, 720h)").
- `SaveBar` with `restartRequired={false}` — downloads changes never require restart.

Desktop `BuildDownloadsTab()` (lines 313–339): same keys, labels, hints. Note: webui uses plain `SettingField` (text) for bandwidth / limits — doesn't mark them as `type=number`, so negative/decimal input is accepted at the client level. Desktop uses `AddTextField` too — matches.

### 6.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| DL-1 | `restartRequired` hard-coded to false | Yes — SaveBar never shows amber banner | Desktop's static subtitle always claims "Server restart may be required" → misleading for Downloads | functional (critical) |
| DL-2 | Skeleton | Plain `"Loading..."` placeholder (webui `DownloadSettings` has no `Skeleton`) | ProgressRing | match (both minimal) |
| DL-3 | Field ordering / labels / hints | Match verbatim | Match | match |

---

## SECTION 7: Integrations (tab `integrations`) — Subtitle providers

Webui `IntegrationsSettings.tsx` (275 lines) — **subtitle providers only**, separate from server settings. Uses `useSubtitleProviders` / `useUpdateSubtitleProvider` / `useTestSubtitleProvider` hooks.

- Header: `h2 text-lg font-semibold "Integrations"` + small "Subtitle providers" subtitle (note **lg** not xl — unique among settings pages).
- `SUBTITLE_PROVIDER_NAMES`: `opensubtitles → OpenSubtitles`, `subdl → SubDL`, `subsource → SubSource`.
- Providers displayed in `SUBTITLE_PROVIDER_ORDER = [opensubtitles, subdl, subsource]`.
- Each provider rendered by `SubtitleProviderCard` — `border-border bg-surface rounded-lg border px-5 py-4 space-y-4` (**8-px radius**, 20/16 px padding):
  - Header row: `font-semibold text-sm` name + `SubtitleCredentialStatus` pill (green `CircleCheck` "Configured" or amber `CircleAlert` "Not configured") → right side: Label "Enabled"/"Disabled" + Switch.
  - Body — for OpenSubtitles: Username + Password text inputs, placeholders "Leave blank to keep current" / "OpenSubtitles username". Password field uses `type=password` (no eye-toggle). For other providers (`subdl`, `subsource`): single API Key input `type={showApiKey ? "text" : "password"}`, with an **Eye/EyeOff toggle button** next to it (`Button variant=ghost size=icon`).
  - Actions row: `Button variant=outline "Test Connection"` (label swaps to "Testing...") + primary `Button "Save"` ("Saving..."); trailing result text green-500/red-500.
- Loading: skeleton (3 `h-32` cards inside `space-y-4`).
- Empty state: `No subtitle providers configured.` in a bordered card.
- Top body: max-width `max-w-3xl` (= 48rem = **768 px**) for subtitle text; card list `max-w-2xl` (672 px).

Desktop `BuildIntegrationsTab()` (lines 645–672) + `BuildInlineSubtitleProviderCard` (lines 727–889):
- Single card ("Subtitle Providers" section header) wrapped in BeginCard/EndCard — web uses `flex h-full flex-col` without an outer card.
- Loading: text placeholder "Loading providers..." (not a skeleton block).
- Provider card: `SurfaceRaisedBrush` background, `BorderBrush` 1 px border, **12-px CornerRadius** (web uses 8 px).
- Per provider: name + status pill (Configured green / Not configured amber) + ToggleSwitch with `OnContent="Enabled"/OffContent="Disabled"`.
- OpenSubtitles: Username TextBox + Password PasswordBox. Other providers: API Key PasswordBox **without** an Eye/EyeOff toggle — PasswordBox on WinUI has a built-in reveal button but the Eye-toggle interaction pattern is different.
- Actions: "Test Connection" button → runs `_subsVm.TestProviderAsync` → flashes success/error color. "Save" (AccentButtonStyle) → runs `UpdateProviderAsync` → flashes "Saved." green.
- No empty state rendering of a styled card — just a text line "No subtitle providers configured.".

### 7.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| INT-1 | Header font size | `text-lg` (18 px) | 20 px (`FontSize = 20` via `AddTabHeader`) — inconsistent with other tabs too | visual |
| INT-2 | Card corner radius | 8 px (`rounded-lg`) | 12 px | visual drift |
| INT-3 | API Key eye-toggle (subdl, subsource) | Explicit Eye/EyeOff `Button variant=ghost` next to input | Desktop uses WinUI `PasswordBox` with its native reveal button (hold-to-show), which is the opposite interaction | functional/visual |
| INT-4 | Status pill colors | Lucide `CircleCheck` green-500 + "Configured" / `CircleAlert` yellow-500 + "Not configured", inline small | Desktop pill `#4ADE80` / `#FBBF24` — matches; rounded 9 px height 18 | match (close) |
| INT-5 | Loading skeleton | 3 × `h-32 w-full` skeleton cards | Text "Loading providers..." | functional/UX |
| INT-6 | Empty state | Bordered card "No subtitle providers configured." | Plain text line | visual |
| INT-7 | Save button auto-reload | `useUpdateSubtitleProvider.mutate` implicitly invalidates query → refresh | Desktop explicitly calls `LoadCommand` + `RebuildSubtitleProviderCards` | match |
| INT-8 | Test feedback text | Webui last-word colors: "Connection successful" green / error red | "Connection successful" / error message — match | match |

---

## SECTION 8: Jellyfin Compat (tab `jellyfin`)

Webui (100 lines) keys: `jellyfin_compat.public_url`, `jellyfin_compat.server_name`, `jellyfin_compat.server_id`, `jellyfin_compat.emulated_server_version`, `jellyfin_compat.session_ttl`, `jellyfin_compat.playback_session_ttl`.
- Loading skeleton: one section with 4 skeleton rows + second section with 2 rows.
- Sections:
  - **Server Identity**: Public URL (text), Server Name (text), Server ID (text), Emulated Server Version (text).
  - **Session Lifetimes**: Session TTL (duration, hint "e.g. 24h"), Playback Session TTL (duration, hint "e.g. 6h").

Desktop `BuildJellyfinTab()` (lines 917–934): identical keys/labels/ordering.

### 8.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| JF-1 | Skeleton | Yes | ProgressRing | critical (pattern) |
| JF-2 | Hint text | "e.g. 24h" / "e.g. 6h" | Match | match |

---

## SECTION 9: Database (tab `database`)

Webui `DatabaseSettings.tsx` (178 lines) keys: `database.max_connections`, `redis.url`, `userdb.backend`, `userdb.pool_max_open`, `userdb.idle_timeout`, `userdb.litestream_sync`, `userdb.stale_grace_seconds`.
- Uses both `useSettingsForm` and `useCheckAdminSettingsConnection` (connection check kind `"redis"`).
- State:
  - `redisManagedByEnv = form.sensitiveManagedByEnv.includes("redis.url")`
  - `redisConfigured = redisUrl.trim() !== "" || form.sensitiveConfigured.includes("redis.url")`
  - `redisEnabledOverride` = explicit user toggle state (so they can toggle "Enable Redis" off without losing the existing URL until save).
- FieldGroups:
  - **Main Database**: Max Connections (number).
  - **Redis**:
    - When `redisManagedByEnv`: divider-bordered info block with `Badge variant=outline "Managed by environment"` + explainer "Redis is configured by the `REDIS_URL` environment variable. Change your deployment configuration and restart the server to update or disable Redis.".
    - **Enable Redis** toggle (derived). Hint text varies: "This setting is controlled by REDIS_URL" if managed; else "Leave disabled to run without Redis".
    - Toggling off clears `redis.url` via `form.setValue("redis.url", "")`.
    - When `effectiveRedisEnabled`: Connection URL (password, hint `redisManagedByEnv ? "Value supplied by REDIS_URL" : "redis://host:6379"`) + `ConnectionCheckAction`.
  - **User Database**: User DB Backend (text, hint "postgres or sqlite"), Pool Max Open (number), Idle Timeout (duration, "e.g. 12h"), Litestream Sync Interval (duration, "e.g. 1s"), Stale Grace Seconds (number).

Desktop `BuildDatabaseTab()` (lines 936–959) + `AddRedisSection` (961–1095):
- Main Database card: Max Connections — match.
- Redis card: toggle + conditional URL field; the URL field is a `PasswordBox` with:
  - Placeholder `"•••• configured"` or `"redis://host:6379"`.
  - Hint textblock below the input text "redis://host:6379".
  - "configured" badge next to label when `IsSensitiveConfigured`.
- Connection check button (`AddConnectionCheckButton("redis", ...)`) — present.
- User Database card: Backend (text, hint "postgres or sqlite"), Pool Max Open (number), Idle Timeout (duration, "e.g. 12h"), Litestream Sync Interval (duration, "e.g. 1s"), Stale Grace Seconds (number) — all match.

### 9.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| DB-1 | "Managed by environment" badge + explainer | Renders when `REDIS_URL` env var is set | Desktop has no `sensitiveManagedByEnv` tracking → never rendered | critical (functional) |
| DB-2 | "This setting is controlled by REDIS_URL" dynamic hint | Yes | Never rendered | functional |
| DB-3 | Disabled inputs when managed | `disabled={redisManagedByEnv}` on URL input, toggle, connection-check | No disabling path | functional |
| DB-4 | Enable-Redis toggle semantics | Override state — flipping off doesn't destroy URL until save (the dirty-tracking comparison treats explicit "" value as the dirty edit) | Desktop calls `SetSetting("redis.url", "")` immediately on toggle-off | functional drift (web is safer) |
| DB-5 | Hint "redis://host:6379" | Inline hint text in SettingField | Desktop adds a separate `TextBlock` below the input | visual (minor) |
| DB-6 | Connection URL password placeholder | `"configured"` text only | `"•••• configured"` — desktop shows a visual "configured" more prominently | divergence |
| DB-7 | "configured" badge | Not on webui | Added on desktop (line 1030+) | divergence |

---

## SECTION 10: Storage (tab `storage`)

Webui `StorageSettings.tsx` (379 lines) — **very complex**, with Tabs-within-Tab.
- 3 inner sub-tabs (`Tabs defaultValue="public"`):
  1. **Public Assets** (active).
  2. **Private Internal**.
  3. **User DB** — `disabled title="Reserved for future Litestream replication"`.
- Inner TabsList: `surface-panel-subtle h-auto gap-1 rounded-[1.1rem] border-0 bg-transparent p-1` — **17.6 px radius**.
- Keys — **dotted S3 namespace** (`s3.public_*`, `s3.private_*`, `s3.user_db_*`):
  - Public: `s3.public_endpoint`, `s3.public_region`, `s3.public_path_style`, `s3.public_bucket`, `s3.public_key_prefix`, `s3.public_access_key`, `s3.public_secret_key`, `s3.public_read_endpoint`, `s3.public_url_auth`, `s3.public_token_secret`, `s3.public_token_param`, `s3.public_token_ttl`.
  - Private: `s3.private_endpoint`, `s3.private_region`, `s3.private_path_style`, `s3.private_bucket`, `s3.private_key_prefix`, `s3.private_access_key`, `s3.private_secret_key`.
  - User DB: `s3.user_db_endpoint`, `s3.user_db_region`, `s3.user_db_path_style`, `s3.user_db_bucket`, `s3.user_db_key_prefix`, `s3.user_db_access_key`, `s3.user_db_secret_key`.

- Public Assets content:
  - Blurb "Stores client-facing assets such as artwork, chapter thumbnails, and subtitle files."
  - Secondary blurb "This bucket does not need to be public. Most installs should keep it private and use presigned URLs. Only use Public or Cloudflare Token modes if you want direct CDN/object access."
  - Fields: Endpoint, Region, Path Style (toggle), Bucket, **KeyPrefixField** (custom — label "Key Prefix", placeholder "continuum/dev", hint "Optional. Stores all Continuum objects under this folder inside the bucket. Leave blank to use the bucket root."), Access Key (password, sensitiveConfigured), Secret Key (password, sensitiveConfigured).
  - `ConnectionCheckAction` kind=`s3_public` with `buildConnectionCheckRequest([...PUBLIC_S3_KEYS])`.
  - **"Asset URL Authentication"** sub-section (inside same tab):
    - H3 "Asset URL Authentication" + blurb "Controls how client-facing asset URLs are generated. Presigned URLs are recommended and work with private buckets."
    - Select "URL Auth Method" options `[presigned → "S3 Presigned URLs (Recommended)", public → "Public (no auth)", cloudflare_token → "Cloudflare Token Auth"]`.
    - When not presigned: Read Endpoint text field, hint "https://cdn.example.com".
    - When cloudflare_token: Token Secret (password, sensitive), Token Param (text, default "verify", hint "verify"), Token TTL (seconds, number, default "10800").

- Private Internal content:
  - Blurb "Stores non-public Continuum objects such as imports, exports, and internal artifacts."
  - Same S3 fields (no URL Auth section) + ConnectionCheck kind=`s3_private`.

- User DB content:
  - `opacity-50` container; all fields `disabled`. Blurb "Reserved for Litestream user database replication. Not currently in use.".

Desktop `BuildStorageTab()` (lines 1097–1140) + `AddS3UrlAuthFields` (1175–1257):
- Sub-tab switcher: **"General Purpose"** (active) | **"User DB"** (disabled, tooltip "Reserved for future Litestream replication"). **No "Public Assets" / "Private Internal" separation** — desktop uses a single set of keys named `s3.operational_*` (dotted).
- S3 keys used by desktop: `s3.operational_endpoint`, `s3.operational_region`, `s3.operational_path_style`, `s3.operational_bucket`, `s3.operational_key_prefix`, `s3.operational_access_key`, `s3.operational_secret_key`, `s3.operational_url_auth`, `s3.operational_public_endpoint`, `s3.operational_token_secret`, `s3.operational_token_param`, `s3.operational_token_ttl`.
- Below the S3 fields: a "Public URL Authentication" section with same select options ("S3 Presigned URLs" / "Public (no auth)" / "Cloudflare Token Auth") + conditional Read Endpoint / Token Secret / Token Param / Token TTL.
- Connection check kind=`s3_operational`.

### 10.x Gaps — SEVERE divergence

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| ST-1 | Inner sub-tabs | **3 sub-tabs** (Public / Private / User DB) | **2 sub-tabs** (General Purpose / User DB) | CRITICAL structural |
| ST-2 | Setting-key namespace | `s3.public_*`, `s3.private_*`, `s3.user_db_*` (three distinct buckets) | `s3.operational_*` (single bucket), plus `s3.user_db_*` disabled | CRITICAL — desktop cannot configure the two webui bucket roles |
| ST-3 | "Public Assets" sub-tab blurbs | Two paragraphs distinguishing public vs. private deployment recommendations | Single blurb "General-purpose storage for operational tasks such as catalog import/export." | CRITICAL content |
| ST-4 | Private Internal tab | Exists with its own keys + its own ConnectionCheck | Does not exist | CRITICAL functional |
| ST-5 | Private Internal blurb | "Stores non-public Continuum objects such as imports, exports, and internal artifacts." | N/A | critical |
| ST-6 | URL Auth section title | "Asset URL Authentication" (Public Assets sub-tab only) | "Public URL Authentication" | visual/UX |
| ST-7 | Presigned label | "S3 Presigned URLs (Recommended)" | "S3 Presigned URLs" | visual (missing qualifier) |
| ST-8 | Read Endpoint label | "Read Endpoint" | "Public Endpoint" | visual/label |
| ST-9 | Read Endpoint key | `s3.public_read_endpoint` | `s3.operational_public_endpoint` | CRITICAL — dotted-vs-`operational` rename |
| ST-10 | Token Secret key | `s3.public_token_secret` | `s3.operational_token_secret` | CRITICAL key mismatch |
| ST-11 | Token Param key | `s3.public_token_param` | `s3.operational_token_param` | CRITICAL key mismatch |
| ST-12 | Token TTL key | `s3.public_token_ttl` | `s3.operational_token_ttl` | CRITICAL key mismatch |
| ST-13 | Sub-tab pill radius | `rounded-[1.1rem]` = **17.6 px** | 14 px | visual drift |
| ST-14 | KeyPrefix custom field (label + placeholder "continuum/dev") | Yes, two-paragraph description | Plain AddTextField with hint as placeholder | visual |
| ST-15 | Path Style (toggle) field | Yes (sensible for path-style buckets) | Yes | match |
| ST-16 | "Cache Images" migration consequence | Web's "Cache Images to S3" on Scanner tab implies Public Assets bucket is in use | Desktop's scanner hint says "Requires General Purpose S3 storage" — reinforces the naming mismatch | critical (docs incoherent) |
| ST-17 | User DB tab content | All S3 fields shown but disabled | Desktop sub-tab exists but content is not populated | functional (neither works, but desktop is emptier) |

**Note — the setting-key divergence is a first-order defect.** Until the desktop uses `s3.public_*` / `s3.private_*` / `s3.user_db_*` exactly, every save from the desktop Storage tab silently lands in a key namespace the server does not read. This is effectively dead functionality.

---

## SECTION 11: Log Retention (tab `log-retention`)

Webui `LogRetentionSettings.tsx` (378 lines) + helpers (`logRetentionPolicy.ts`, 85 lines):
- Uses raw `useAdminServerSettings` / `useUpdateServerSetting` (no `useSettingsForm`). Custom dirty-state via local `Set<string>`.
- Keys (from `logRetentionPolicy.ts`):
  - `opslog.retention_days`
  - `opslog.max_rows`
  - `opslog.max_size_mb`
  - `opslog.bucket_policies` — JSON-serialized array of `{component, level, retention_days, max_rows, max_size_mb}`
  - Also defined but **not** used in this tab: `opslog.cleanup_interval_minutes` (scheduled-tasks managed).
- `LOG_LEVEL_OPTIONS = ["info", "warn", "error"]` — **no `"debug"`**.
- `DEFAULT_BUCKET_POLICIES`:
  1. `{metadata, info, 1 day, 100k rows, 128 MB}`
  2. `{scanner,  info, 2 days, 150k rows, 192 MB}`
  3. `{metadata, warn, 7 days, 250k rows, 256 MB}`
  4. `{scanner,  warn, 7 days, 250k rows, 256 MB}`
- Sections:
  - Tab subtitle: "Prune oldest operational logs by global caps and per-bucket overrides. Bucket rules match on component and level. **Cleanup cadence and startup runs are configured in Scheduled Tasks.**"
  - **Global Limits** FieldGroup: Retention Days, Max Rows, Max Size (MB) — all numbers with long hints.
  - **Bucket Overrides** FieldGroup:
    - Top row: description "Use tighter rules for noisy buckets like `metadata/info`. Set a bucket limit to `0` to disable that bucket-specific cap." + buttons "Restore Recommended Rules" (`RotateCcw` icon, outline variant) + "Add Rule" (`Plus` icon, primary).
    - Parse-error banner when the stored JSON is malformed: `border-warning/30 bg-warning/10 text-warning rounded-[1rem] border px-3 py-2 text-sm` — "Existing bucket policy JSON could not be parsed. The editor loaded the recommended rules so you can recover cleanly. Details: …".
    - Save-error text below.
    - Table inside `surface-panel-subtle overflow-x-auto rounded-[1rem]`:
      - Columns: Component (text), Level (select from `LOG_LEVEL_OPTIONS`), Days (number), Max Rows (number, width 140), Max Size (MB) (number, width 140), trailing 60-px icon column.
      - Empty state: "No bucket overrides configured." colspan=6.
      - Delete button: outline `size="icon-sm"` with `Trash2`, aria-label `Remove {component} rule`.
    - Footer blurb "Matching rows are pruned oldest-first when they exceed the bucket rule. Global caps still apply afterward, so noisy buckets cannot crowd out playback or error logs."

Desktop `BuildLogRetentionTab()` (lines 1281–1435) + helpers:
- `_bucketRows` list of `BucketRow{Component, Level, RetentionDays, MaxRows, MaxSizeMb}`.
- `DefaultBucketPolicies` DIFFERS from web:
  - `(metadata, info, 1, 100000, 128)`
  - `(playback, info, 7, 500000, 256)`
  - `(scanner,  info, 3, 300000, 256)`
  - `(scanner,  warn, 30, 100000, 128)`
  - `(scanner,  error, 90, 50000, 64)`
- Log level ComboBox options: `["debug", "info", "warn", "error"]` — includes `debug`, which web intentionally excludes.
- Sections:
  - Tab subtitle: "Prune oldest operational logs by global caps and per-bucket overrides. Bucket rules match on component and level." — missing the "Cleanup cadence and startup runs…" sentence.
  - Global Limits card: Retention Days / Max Rows / Max Size (MB) — matches.
  - Bucket Overrides card: description + "Restore Recommended" button + "Add Rule" (AccentButtonStyle) + table (Component TextBox, Level ComboBox, Days NumberBox, Max Rows NumberBox, Max Size (MB) NumberBox, delete icon button).
  - Footer blurb matches.
- No parse-error banner; `catch { /* malformed */ }` silently returns an empty list.
- No save-error distinct text.

### 11.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| LR-1 | Default bucket policies content | 4 rules: (metadata info), (scanner info), (metadata warn), (scanner warn) | 5 rules with playback added + scanner error extended to 90 days | CRITICAL — "Restore Recommended" clicks produce different data |
| LR-2 | Level options | `["info", "warn", "error"]` | `["debug", "info", "warn", "error"]` | data-integrity — saving a `"debug"` row could be rejected server-side; desktop will also parse server-sent rules and UP-project debug to info when round-tripping |
| LR-3 | Parse-error recovery banner | Yes — amber banner with recovery message + details | Desktop silently parses into empty list, no user feedback | functional |
| LR-4 | Save-error message | Explicit red text "Failed to save some settings. Please try again." when any mutation fails | None — flows into shared SaveBar ErrorMessage | functional |
| LR-5 | "Restore Recommended Rules" label | Exact wording | Desktop button reads "Restore Recommended" (missing "Rules") | visual (text) |
| LR-6 | Tab subtitle sentence | "Cleanup cadence and startup runs are configured in Scheduled Tasks." | Missing | visual/discoverability |
| LR-7 | Save semantics | Only the dirty keys within the tab (`Promise.all(requests)`) | Global SaveBar batches everything | functional divergence |
| LR-8 | `restartRequired` signal | Web sets `setRestartRequired(true)` after save (line 166) | Desktop never flips this | functional |
| LR-9 | Table corner radius | `rounded-[1rem]` = **16 px** | 8 px (`CornerRadius(8,8,0,0)` / `(0,0,8,8)`) | visual drift |
| LR-10 | Column widths | Web: sensible wide cells (Max Rows `w-[140px]`, Max Size `w-[140px]`, Days `w-[110px]`, Level `w-[120px]`, delete `w-[60px]`) | Desktop widths 120/80/110/110/36 — delete column much narrower and Days column narrower | visual drift |
| LR-11 | Delete button a11y | `aria-label="Remove {component} rule"` | No aria label (ToolTipService only) | a11y |

---

## SECTION 12: Card Overlays (tab `overlays`)

Webui `OverlaySettings.tsx` (201 lines) keys: `overlays.enabled`, `defaults.card_overlays`.
- Top header "Card Overlays" + subtitle "Configure the default overlay badges shown on poster cards. Users can override these in their personal settings."
- Section **General**: `Card Overlays Enabled` toggle + hint "When disabled, no overlay badges appear for any user regardless of their personal settings.".
- Section **Default Configuration**:
  - `pointer-events-none opacity-50` wrapper when disabled.
  - Blurb "These defaults apply to users who have not customized their overlay settings." (text-xs).
  - Two-column layout (`flex-col gap-6 lg:flex-row`):
    - Left: `DefaultsEditor` — per-overlay row with Label + Description (`text-xs`), Position Select (width 130 px), and Switch. Disabled Position when `!config.enabled`.
    - Right: `DefaultsPreview` — a 140×210 (ratio 2:3) poster preview `bg-muted/40 overflow-hidden rounded-xl border`, with corner-pinned badges using the shared `BADGE_CLASS` / `BADGE_STYLE` tokens from `@/lib/cardOverlays`. Inner sample text "Preview" centered (`text-muted-foreground/30 text-xs font-medium`).
- `OVERLAY_REGISTRY` and `OVERLAY_POSITIONS` (from `@/lib/cardOverlays` — not read, UNVERIFIED content):
  - Inferred from preview-code uses (sampleData): `resolution (2160P)`, `hdr (DV HDR10)`, `audio (Atmos)`, `release_type (REMUX)`, `edition (Extended)`, `rating_imdb (8.7)`, `rating_tmdb (8.5)`, `rating_rt (96%)`, `rating_rt_audience (92%)`, `original_language (EN)`. **Note the `edition` entry.**
- `POSITION_OPTIONS = [top-left, top-right, bottom-left, bottom-right]`.

Desktop `BuildOverlaysTab()` (lines 1621–1675) + helpers:
- `OverlayRegistry`:
  - `resolution` / `hdr` / `audio` / `release_type` / `rating_imdb` / `rating_tmdb` / `rating_rt` / `rating_rt_audience` / `original_language`
  - **Missing `edition`.**
- `OverlayPositions` matches.
- Section "General": Card Overlays Enabled toggle + same hint.
- Section "Default Configuration": blurb matches.
- Left editor column: each overlay as a Grid row — label (13 px Medium), description (11 px), Position ComboBox (130 px), ToggleSwitch. Position ComboBox `IsEnabled=current.Enabled` — matches webui's disable-when-off behavior.
- Right preview: 140×210 container with 2:3 ratio, linear-gradient dark teal → near-black background (`(0x1F29,0x29,0x3A)` → `(0x14,0x19,0x24)` → `(0x09,0x0B,0x10)`), film-icon `\uE714` + "PREVIEW" caption, corner-pinned badges (`Color.FromArgb(0xCC,0x00,0x00,0x00)`, 4-px radius, 5/2 padding, 9-px white SemiBold text).

### 12.x Gaps

| # | Feature | Webui | Desktop | Severity |
|---|---|---|---|---|
| OV-1 | `edition` overlay | Present (sampleData shows "Extended") | Missing — desktop will parse/round-trip `edition` key but never expose it in editor or preview | CRITICAL functional |
| OV-2 | Preview styling | Plain `bg-muted/40` poster background (neutral gray) | Custom dark teal gradient mock — looks different | visual |
| OV-3 | Preview badge styling | Uses shared `BADGE_CLASS` + `BADGE_STYLE` tokens from `@/lib/cardOverlays` (token-controlled across the app) | Hard-coded `(0xCC, 0x00, 0x00, 0x00)` black 80% pill + 4-px radius | visual |
| OV-4 | Card/preview corner radius | `rounded-xl` = 12 px | 12 px container matches | match |
| OV-5 | Disabled-when-overlay-off Position select | `disabled={!config.enabled}` on Select | `posCombo.IsEnabled = current.Enabled` | match |
| OV-6 | Serialization format | `serializeOverlayPrefs` from `@/lib/cardOverlays` (UNVERIFIED exact format; component builds `{ id: {enabled, position} }` JSON in preview) | Same shape (`SerializeOverlayPrefs`) | likely match |
| OV-7 | Loading state | Plain `"Loading..."` | ProgressRing | match (both minimal) |

---

## Cross-cutting findings against prior-audit patterns

| Pattern | Occurrence in Admin Settings |
|---|---|
| Entire page hidden by loading/error overlays | **Yes** — `AdminSettingsDetailPage.xaml:20–46` renders ProgressRing + centered Error+Retry that blocks the whole card. Web renders per-tab skeletons and never blocks the surface. Confirmed for General, Jellyfin, Scanner, Integrations (and implicitly every tab since IsLoading gates the main grid). |
| No realtime refresh despite "auto-refreshing" claims | Not applicable here — settings are explicitly not realtime. But the HW-accel resolved indicator (PlaybackSettings) is a near-realtime diagnostic that desktop omits entirely. |
| Dotted vs underscored setting keys | **Yes (CRITICAL)** — Storage desktop uses `s3.operational_*` but server expects `s3.public_*` / `s3.private_*`. Also `s3.operational_public_endpoint` vs `s3.public_read_endpoint`. |
| Missing pagination | N/A |
| Silent clipboard actions | N/A on this surface (InviteCodesTab, which has copy-to-clipboard via `toast.success("Copied to clipboard")`, is NOT in the Settings layout — but if/when it lands on desktop, make sure clipboard has a toast). |
| Missing "This action cannot be undone" safety clauses | InviteCodesTab dialog says "This action cannot be undone." — but it lives on Users page. The restart-server confirm dialog (SaveBar) says "The server will restart… Active streams will be interrupted." — desktop lacks this dialog entirely. |
| Page vs tab structural divergence | **Yes (CRITICAL)** — Storage has 3 sub-tabs (Public/Private/UserDB) on web, only 2 (GeneralPurpose/UserDB) on desktop. |
| Corner radius drift | 26 vs 16 or 8 vs 16: here it's mostly minor drift (10 vs 12 nav buttons; 8 vs 12 provider cards; 17.6 vs 14 sub-tab pill; 16 vs 8 log-retention table). |
| Typography 1 px smaller than webui | **Yes** — field labels 13 vs 14 px, hints 11 vs 12 px throughout desktop. |
| Missing realtime event channel integration | Settings don't use event channel. |
| Inline action buttons vs overflow menus | Not relevant to settings. |
| Sensitive fields permanently masked instead of Eye/EyeOff toggle | **Yes** — subtitle API keys on desktop are plain `PasswordBox` with built-in reveal; web uses explicit Eye/EyeOff custom toggle. Behavioral divergence (hold-vs-click). |

---

## Prioritized Fix List

### P0 — critical correctness

1. **Storage: rename all setting keys from `s3.operational_*` to `s3.public_*`, and add the Private Internal sub-tab with `s3.private_*` keys** (ST-1, ST-2, ST-3, ST-4, ST-9 – ST-12). Without this, every Storage save is a no-op from the server's perspective.
2. **Add restartRequired tracking + Restart Server button + confirm dialog** (PRIM-1, PRIM-2, SB-3, SB-4). Currently admins must manually figure out whether a change needs restart.
3. **Overlay `edition` registry entry** (OV-1) — missing from desktop means admins can't toggle it, and any defaults containing it will be invisible in the editor.
4. **Log Retention default bucket policies must match the webui list** (LR-1). Current desktop "Restore Recommended" produces rules the server operators did not design.
5. **Log Retention level options must exclude `debug`** (LR-2) to match server behavior.
6. **Loading state: replace full-page ProgressRing with per-tab skeletons** (GEN-1, JF-1, SCAN-2, LR-3, plus all others). Blocks the surface unnecessarily.
7. **Error state: don't block the whole card with a centered error — let each tab render its own error banner inline** (same area as GEN-1 above).
8. **Database: implement `sensitiveManagedByEnv` badge + explainer + disabled fields** (DB-1, DB-2, DB-3).
9. **Theme: wire `sanitizeCss` equivalent or reject dangerous payloads** (TH-4). Security concern.

### P1 — high-impact UX / functional

10. **Theme: build a live preview card + token color-picker editor + Reset All button** (TH-1, TH-2, TH-3). Today the desktop admins must hand-author JSON.
11. **Playback: add HW-accel resolved live indicator** (PB-4, PB-5). Non-trivial but high-value diagnostic.
12. **Rate Limit tab: decouple save from shared SaveBar; add per-tab "Save Changes" button and remove Discard** (RL-1, RL-2) OR explicitly document the divergence. Either way, pick one.
13. **Playback / Scanner / other selects: emit human-friendly option labels** (PB-1, PB-2, PB-3, SCAN-1). Don't just uppercase the enum token.
14. **Integrations: replace PasswordBox native reveal with custom Eye/EyeOff ToggleButton + TextBox** (INT-3) for API keys.
15. **Status toast: replace hard-coded `#22C55E` inline strip with reusable toast service** (SB-6, tracks webui parity doc B27).
16. **Nav deep-link: persist active tab in Frame parameter or local settings** (NAV-1).
17. **Nav active-pill indicator: add 3×18 px left accent bar** (NAV-2).
18. **Log Retention: show parse-error recovery banner + save-error text** (LR-3, LR-4).
19. **Log Retention: include missing subtitle sentence "Cleanup cadence and startup runs are configured in Scheduled Tasks."** (LR-6).
20. **Theme: implement onBlur / debounced auto-save for Branding + Catalog + CSS + Token fields** (TH-5, TH-6) OR document divergence. Web does NOT use SaveBar here.
21. **SaveBar: swap "Save Changes" label to "Saving..." during isSaving** (SB-8).

### P2 — visual polish / minor drift

22. Remove the `"configured"` badge next to sensitive-field labels on desktop; rely solely on placeholder text to match webui (PRIM-6, DB-7).
23. Increase field label typography from 13 → 14 px; hints from 11 → 12 px across all builder helpers (FLD-3, FLD-4).
24. Nav button corner radius 10 → 12 px; icon 14 → 16 px (NAV-3, NAV-4).
25. Card corner radii: provider card 12 → 8 px (INT-2); storage sub-tab pills 14 → 17.6 px (ST-13); log retention table 8 → 16 px (LR-9).
26. Input max-width tweaks: text `MaxWidth=460` → 448; number `Width=180` → 160 (FLD-1, FLD-2, RL-4, RL-5).
27. Rate Limit form: constrain inner content to 672 px max-width (RL-3).
28. Integrations header h2 font: 20 → 18 px to match webui `text-lg` (INT-1).
29. Presigned label text: "S3 Presigned URLs" → "S3 Presigned URLs (Recommended)" (ST-7).
30. Read Endpoint label: "Public Endpoint" → "Read Endpoint" (ST-8).
31. URL Auth section title: "Public URL Authentication" → "Asset URL Authentication" (ST-6).
32. Scanner metadata.cache_images hint: swap to webui wording (SCAN-1).
33. Nav aria attributes (role=tab, aria-selected, aria-controls) — a11y (NAV-7).
34. Delete button aria-label in Log Retention rows (LR-11).
35. Restore Recommended button text "Restore Recommended" → "Restore Recommended Rules" (LR-5).
36. Connection-check icons (green check / red x) next to result text (CC-3).
37. SaveBar: inline inside the tab content card, not a fixed bottom strip (SB-1, SB-2, SB-5).
38. Integrations empty state: render bordered card instead of text line (INT-6).
39. Integrations loading: render skeleton blocks instead of text (INT-5).

---

## Summary Table

| Sub-page | Feature | Webui | Desktop | Gap | Severity |
|---|---|---|---|---|---|
| Shell | Deep-link via `?tab=` | Yes | No | NAV-1 | functional |
| Shell | Active pill 3×18 px | Yes | No | NAV-2 | visual |
| Shell | Nav button corner 12 px | Yes | 10 px | NAV-3 | visual |
| Shell | Nav icon 16 px | Yes | 14 px | NAV-4 | visual |
| Shell | a11y role/aria-selected | Yes | No | NAV-7 | a11y |
| Primitive | restartRequired flag | per-tab | static | PRIM-1 | critical |
| Primitive | Restart Server button + dialog | Yes | No | PRIM-2 | critical |
| Primitive | sensitiveManagedByEnv badge | Yes | No | PRIM-3 | functional |
| Primitive | sensitiveManagedByEnv explainer | Yes | No | PRIM-4 | functional |
| Primitive | Sensitive-configured badge | No | Yes | PRIM-6 | divergence |
| Primitive | Discard-in-place | Yes | Rebuilds tab | PRIM-7 | functional |
| SaveBar | Inline w/ content | Yes | Fixed bottom | SB-1 | visual/UX |
| SaveBar | "Saving..." label | Yes | No | SB-8 | functional |
| SaveBar | Conditional restart banner | Yes | Always-on subtitle | SB-5 | misleading |
| SaveBar | Toast service for save-success | Yes | `#22C55E` inline strip | SB-6 | visual (B27) |
| General | Skeleton loading | Yes | Full-page ProgressRing | GEN-1 | critical |
| General | Error not blocking whole page | Yes | Blocks | GEN-2 | functional |
| Theming | Live preview card | Yes | No | TH-1 | critical |
| Theming | Token editor (pickers) | Yes | Raw JSON textarea | TH-2 | critical |
| Theming | Reset all button | Yes | No | TH-3 | functional |
| Theming | sanitizeCss | Yes | No | TH-4 | security |
| Theming | Debounced/onBlur auto-save | Yes | SaveBar | TH-5/6 | divergence |
| Playback | HW accel labels | Explicit | Auto-cap | PB-1 | UX |
| Playback | Chapter execution labels | Explicit | Auto-cap | PB-2 | UX |
| Playback | HDR thumbnail policy labels | Explicit | Auto-cap | PB-3 | UX |
| Playback | HW accel resolved live indicator | Yes | No | PB-4 | functional |
| Scanner | Cache-images hint wording | Public-asset | General-purpose | SCAN-1 | UX |
| Rate Limit | Per-tab Save button; no Discard | Yes | SaveBar | RL-1/2 | divergence |
| Rate Limit | Max-w-2xl constraint | Yes | Full width | RL-3 | visual |
| Downloads | restartRequired=false hint | Yes | Always-on subtitle | DL-1 | misleading |
| Integrations | `text-lg` header | Yes | 20 px | INT-1 | visual |
| Integrations | Card radius 8 px | Yes | 12 px | INT-2 | visual |
| Integrations | Eye/EyeOff API-key toggle | Yes | PasswordBox native reveal | INT-3 | functional |
| Integrations | Loading skeleton | Yes | Text | INT-5 | UX |
| Integrations | Empty-state card | Yes | Text | INT-6 | visual |
| Database | Managed-by-env badge | Yes | No | DB-1 | critical |
| Database | Managed-by-env explainer | Yes | No | DB-2 | functional |
| Database | Disabled inputs when env-managed | Yes | No | DB-3 | functional |
| Database | Redis toggle off doesn't clear URL until save | Yes | Clears immediately | DB-4 | functional drift |
| Database | sensitive `configured` placeholder | Text only | `••••` + badge | DB-6/7 | divergence |
| Storage | 3 sub-tabs (Public/Private/UserDB) | Yes | 2 sub-tabs | ST-1 | CRITICAL |
| Storage | Key namespace `s3.public_*` / `s3.private_*` | Yes | `s3.operational_*` | ST-2 | CRITICAL |
| Storage | Private Internal content | Yes | Missing | ST-4 | CRITICAL |
| Storage | Asset URL Auth section name | "Asset URL Auth" | "Public URL Auth" | ST-6 | UX |
| Storage | "(Recommended)" on Presigned option | Yes | No | ST-7 | UX |
| Storage | "Read Endpoint" vs "Public Endpoint" | Read | Public | ST-8 | UX |
| Storage | Read-endpoint key | `s3.public_read_endpoint` | `s3.operational_public_endpoint` | ST-9 | CRITICAL |
| Storage | Token keys | `s3.public_token_*` | `s3.operational_token_*` | ST-10/11/12 | CRITICAL |
| Storage | Sub-tab pill radius 17.6 px | Yes | 14 px | ST-13 | visual |
| Log Retention | Default bucket set | 4 rules | 5 different rules | LR-1 | CRITICAL |
| Log Retention | Level options | info/warn/error | debug/info/warn/error | LR-2 | data-integrity |
| Log Retention | Parse-error recovery banner | Yes | Silent fail | LR-3 | functional |
| Log Retention | Save-error text | Yes | None | LR-4 | functional |
| Log Retention | Restore label wording | "Rules" suffix | Missing suffix | LR-5 | UX |
| Log Retention | Subtitle sentence about Scheduled Tasks | Yes | Missing | LR-6 | discoverability |
| Log Retention | Table corner radius 16 px | Yes | 8 px | LR-9 | visual |
| Log Retention | restartRequired post-save | Yes | No | LR-8 | functional |
| Card Overlays | `edition` overlay | Yes | Missing | OV-1 | CRITICAL |
| Card Overlays | Preview background | Neutral gray | Dark teal gradient | OV-2 | visual divergence |
| Card Overlays | Badge token styling | Shared BADGE_STYLE | Hard-coded | OV-3 | visual divergence |

---

## Appendix — Tailwind → brush translation cheat sheet (used above)

- `surface-panel` = desktop `CardBackgroundBrush`
- `surface-panel-subtle` = desktop `SurfaceRaisedBrush`
- `page-title` = ~3rem clamp bold → desktop `FontSizeDisplayLarge` + Bold
- `page-subtitle` = 14 px secondary → desktop 14 px `SecondaryTextBrush`
- `text-muted-foreground` = desktop `SecondaryTextBrush` / `TertiaryTextBrush` (hints)
- `text-foreground` = desktop `PrimaryTextBrush`
- `bg-accent` (nav active) = desktop `AccentBackgroundBrush` (with icon in `AccentBrush`)
- `rounded-xl` = 12 px, `rounded-2xl` = 16 px, `rounded-lg` = 8 px, `rounded-[1rem]` = 16 px, `rounded-[1.1rem]` = 17.6 px, `rounded-[1.8rem]` = 28.8 px.
- `p-4` = 16 px, `p-5` = 20 px, `px-5 py-4` = 20 horizontal / 16 vertical, `p-3` = 12 px.
- `text-sm` = 14 px, `text-xs` = 12 px, `text-[13px]` = 13 px.
- `gap-2.5` = 10 px, `gap-3` = 12 px, `gap-6` = 24 px.
- `max-w-md` = 448 px, `max-w-2xl` = 672 px, `max-w-3xl` = 768 px.
- `w-40` = 160 px, `w-56` = 224 px.
- Amber banner: `border-amber-500/20 bg-amber-500/5` = #FBBF24 20%/5% → desktop `(0x33,0xFB,0xBF,0x24)` / `(0x14,0xFB,0xBF,0x24)` (off-by-15% alpha, close enough).
- Green success: `text-green-500` ≈ #22C55E (desktop `#4ADE80` = green-400 — 1 step off but visually close).
- Red error: `text-red-500` ≈ #EF4444 (desktop `#EF6B73` — close).
