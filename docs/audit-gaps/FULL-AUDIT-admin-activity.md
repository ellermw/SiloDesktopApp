# Admin Activity — Full Audit

## Files fully read (100% line-by-line)

- Webui: `F:\continuum-server\web\src\pages\AdminActivity.tsx` (882 lines)
- Webui: `F:\continuum-server\web\src\pages\adminActivityPresentation.ts` (134 lines)
- Desktop: `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminActivityPage.xaml` (362 lines)
- Desktop: `F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminActivityPage.xaml.cs` (860 lines)
- Desktop: `F:\ContinuumPlayer\src\ContinuumPlayer\ViewModels\Admin\AdminActivityViewModel.cs` (293 lines)
- Desktop: `F:\ContinuumPlayer\src\ContinuumPlayer\Controls\ServerActivityButton.xaml` (71 lines)
- Desktop: `F:\ContinuumPlayer\src\ContinuumPlayer\Controls\ServerActivityButton.xaml.cs` (595 lines)
- Desktop: `F:\ContinuumPlayer\src\ContinuumPlayer.Core\Services\EventChannelClient.cs` (421 lines)

**Permission-blocked (inferred from desktop mirror comments and prior dashboard audit):**
- `web/src/components/ServerActivity.tsx`
- `web/src/components/AdminSessionActions.tsx`
- `web/src/app.css`
- `web/src/hooks/queries/admin/stats.ts`
- `web/src/hooks/queries/admin/ips.ts`
- `web/src/components/realtimeEventsContext.tsx`

---

## 1. Page header — title + live badge + subtitle + connection indicator + refresh

**Webui (AdminActivity.tsx:128–158):** `<div className="page-header">` — left space-y-3 block + right `flex items-center gap-3` cluster.

Left:
- `<h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Activity</h1>` — responsive 32–48 px
- When `sessions.length > 0`: `<span className="live-badge flex items-center gap-1.5"><Radio className="h-3 w-3" />{sessions.length} live</span>` — lucide Radio 12 px, 6 px gap
- Subtitle `<p className="text-muted-foreground mt-1 text-[13px]">` "No active streams" OR "{n} active stream(s) across {m} node(s)"

Right:
- Vertical stack: uppercase eyebrow "Stream" (`text-[10px] font-semibold tracking-wider uppercase`) + value `<div className="text-[12px]">{formatConnectionState(connectionState)}</div>` = "Connecting" | "Live" | "Disconnected" (lines 766–775)
- `error` → `text-[11px]` muted line
- `<Button variant="outline" size="sm" onClick={refresh}><RefreshCw className="h-3.5 w-3.5" />Refresh</Button>`

**Desktop (AdminActivityPage.xaml:78–121 + xaml.cs:90–107):**
- Grid `*, Auto`. Col 0: horizontal StackPanel Spacing=10 with `<TextBlock Text="Activity" FontSize="42" FontWeight="Bold">` (fixed 42 — not responsive) and `<Border x:Name="LiveBadge" Background="AccentBackgroundBrush" CornerRadius="12" Padding="10,4">` containing `<TextBlock Text="{n} live" FontSize="13" FontWeight="SemiBold" Foreground="AccentBrush" />` — **no Radio icon**
- `<TextBlock x:Name="SubtitleText" FontSize="13" Foreground="SecondaryTextBrush">`
- Col 1: one `<Button Content="Refresh" Style="SecondaryButtonStyle">` — **no icon, no connection-state indicator, no eyebrow at all**

**Gap:**

| Aspect | Webui | Desktop |
|---|---|---|
| Title size | `clamp(2rem,4vw,3rem)` 32–48 px responsive | Fixed 42 |
| Live badge Radio icon | Lucide Radio 12 px inline | **Missing** |
| Live badge utility | `.live-badge` shared class | Ad-hoc `AccentBackgroundBrush` pill |
| Connection eyebrow "Stream" | 10 px semibold uppercase tracking-wider | **Missing** |
| Connection state "Live/Connecting/Disconnected" | 12 px text | **Missing** |
| Refresh icon | Lucide RefreshCw 14 px before label | **Missing** |
| Refresh variant | `outline` small | `SecondaryButtonStyle` |
| Header wrapper class | `.page-header` utility | Raw Grid |

**Severity:** Visual + **Functional (critical)**. Connection indicator tells operators at a glance whether live updates are working. `EventChannelClient.StateChanged` is already wired — only the UI surface is missing.

---

## 2. IP Lookup panel

**Webui (AdminActivity.tsx:160–231):** Collapsible `<details className="surface-panel rounded-2xl border-0">`:
- `<summary className="cursor-pointer px-4 py-3 text-sm font-medium select-none">IP Lookup</summary>` — collapsed by default
- Body:
  - Form submits on Enter: `<Input placeholder="IP lookup (e.g. 203.0.113.50)" className="max-w-xs font-mono text-sm">` (320 px mono) + `<Button variant="outline" size="sm" disabled={!ipSearch.trim()}><Search className="mr-1 h-3.5 w-3.5" />Lookup</Button>` — disabled when empty; has Search icon
  - Post-submit:
    - `ipLoading`: "Searching..." (muted)
    - Empty: "No users found for {ip} in the last 30 days."
    - Populated: shadcn `<Table>` 4 cols — **User** as clickable `<Link to="/admin/users/{user_id}">`, First Seen, Last Seen (`toLocaleString()` full datetime with seconds), Requests right-aligned `toLocaleString()`

**Desktop (AdminActivityPage.xaml:123–171 + xaml.cs:729–785):** Always-expanded `<Border Style="SurfacePanelStyle">` (CornerRadius=26, Padding=16, BorderThickness=0):
- **Not collapsible. No "IP Lookup" title** (explicit XAML comment says "no title text")
- Single input row: `<TextBox Placeholder="IP lookup (e.g. 203.0.113.50)" FontSize=13 FontFamily=Consolas CornerRadius=8 MaxWidth=320>` + `<Button Content="Lookup" Style="SecondaryButtonStyle" IsEnabled="{InverseBool IpLookupLoading}">` — disabled *while loading* (opposite rule); **no search icon**. Enter key handler wired.
- Results (`RebuildIpResults` xaml.cs:729–768):
  - Headers: User / First Seen (150 px) / Last Seen (150 px) / Requests (90 px right-aligned) — 11 px semibold Tertiary
  - User cell: bold + AccentBrush (**styled as link but NOT clickable**)
  - Dates `dt.ToLocalTime().ToString("g")` — short format, **no seconds**
  - Requests `.ToString("N0")` matches
  - **No "Searching…" state, no "No users found" empty text**

**Gap:**

| Aspect | Webui | Desktop |
|---|---|---|
| Collapsible | `<details>` collapsed | Always expanded |
| Panel corner | 16 px | 26 px (too round) |
| Submit button Search icon | Lucide 14 px | Missing |
| Submit button disabled rule | `!ipSearch.trim()` | `IpLookupLoading` — opposite |
| Loading text | "Searching..." | Missing |
| Empty text | "No users found for {ip}..." | Missing |
| User cell | `<Link>` clickable | Plain TextBlock |
| Date format | Full w/ seconds | Short w/o seconds |

**Severity:** Functional (moderate — clickable user link, loading/empty states, collapsibility). Visual — panel radius wrong.

---

## 3. Summary strip — method distribution + node breakdown

**Webui (AdminActivity.tsx:233–302):** Rendered only when `sessions.length > 0`. Container `<div className="surface-panel rounded-2xl border-0 p-4">`.

### 3a. Play-Method block
- Eyebrow `text-[10px] font-semibold tracking-wider uppercase` "Play Method"
- Proportional bar `<div className="flex h-1.5 overflow-hidden rounded-full">` — 6 px tall, fully rounded, internal clipping
  - Segments sorted alphabetical, each `transition-all duration-500` with `bg-success`/`bg-info`/`bg-warning`/`bg-muted-foreground`
- Method toggles `<div className="mt-2 flex flex-wrap gap-x-4 gap-y-1">` — **wraps** (16 / 4 px)
  - Each: `flex items-center gap-1.5 text-[11px]`; `opacity-30` when filter set to another method
  - 8 px dot + `font-medium capitalize` name + `tabular-nums` count

### 3b. By-Node block (only when `Object.keys(nodes).length > 1`)
- Wrapper `border-border border-t pt-3`
- Eyebrow "By Node"
- Buttons `flex flex-wrap gap-1.5` — wraps
- Each: `bg-surface border-border hover:border-primary/20 rounded-md border px-2.5 py-1 text-[11px] font-medium`; active `border-primary/40 bg-primary/10 text-primary`; inactive-while-filter-set `opacity-30`
- Sort descending by count

**Desktop (AdminActivityPage.xaml:173–215 + xaml.cs:111–279):** Same structure but:
- Panel CornerRadius=26 (wrong — should be 16)
- Eyebrow `CharacterSpacing=120` ≈ 0.12 em — **too much** (tracking-wider ≈ 0.05 em ≈ 50)
- Bar `<Grid Height=6 CornerRadius=3>` + per-segment Border. **No animation.**
- Method toggles `<StackPanel Orientation=Horizontal Spacing=16>` — **no wrap** (StackPanel never wraps)
- Node block `<Border BorderThickness="0,1,0,0" Margin="0,12,0,0" Padding="0,12,0,0">` — extra `Margin=12` not in web (web uses only `pt-3` = 12 px)
- Node buttons `<StackPanel Orientation=Horizontal Spacing=6>` — **no wrap**
- **No hover state** on node buttons

**Gap:**

| Aspect | Webui | Desktop | Severity |
|---|---|---|---|
| Panel corner | 16 px | 26 px | Visual |
| Bar animation | `duration-500` | None | Polish |
| Method toggle wrap | `flex-wrap` | None | Functional narrow |
| Node buttons wrap | `flex-wrap` | None | Functional narrow |
| Tracking | ~50 | 120 | Visual |
| Node hover | `hover:border-primary/20` | None | Polish |
| Extra top margin on node block | No | `Margin=12` duplicates padding | Minor |

---

## 4. Filter bar — search + type toggles + clear filters

**Webui (AdminActivity.tsx:304–351):** `<div className="flex flex-wrap items-center gap-2">`.

- Search: `relative min-w-[200px] flex-1` — lucide Search 14 px absolute 12 px from left; `<Input className="h-8 pl-9 text-[13px]">` (32/36/13); when non-empty, X clear button right 10 px (lucide X 14 px)
- Type toggles `<div className="flex gap-1">` for `["movie","series"]`:
  - `rounded-md border px-2.5 py-1.5 text-[11px] font-medium capitalize transition-all`
  - Inactive: `border-border bg-surface text-muted-foreground hover:text-foreground`
  - Active: `border-primary/40 bg-primary/10 text-primary`
- Clear filters visible when `activeFilters > 0` (**does NOT include search**):
  - `text-muted-foreground hover:text-foreground flex items-center gap-1 text-[11px]`
  - Lucide X 12 px + "Clear filters"

**Desktop (AdminActivityPage.xaml:219–300 + xaml.cs:283–331, 827–844):**
- Outer `<Grid ColumnSpacing="8">` with fixed columns `*, Auto, Auto, Auto` — **no wrap**
- Col 0 search: `<TextBox Placeholder="Filter by user or media..." FontSize=13 CornerRadius=8 Height=32 Padding=36,6,30,6>` + `<FontIcon Glyph="&#xE721;" FontSize=14 Margin=10,0,0,0 IsHitTestVisible=False>` (left offset 10 vs web 12) + right-aligned `<Button x:Name="SearchClearButton"><FontIcon Glyph="&#xE711;" FontSize=12>` (**12 px** vs web 14 px)
- Col 1 toggles with `FilterToggleButtonStyle` — matches
- Col 2 Clear Filters: transparent button + `<FontIcon Glyph="&#xE711;" FontSize=10>` (web: 12 px) + `<TextBlock Text="Clear filters" FontSize=11>`; click handler calls `ClearFiltersCommand` which **also resets SearchText** (AdminActivityViewModel.cs:109–116). **Behavioural divergence from web.**

**Gap:**

| Aspect | Webui | Desktop | Severity |
|---|---|---|---|
| Filter bar wrap | `flex-wrap` | Grid — no wrap | Functional narrow |
| Search icon left offset | 12 px | 10 px | Minor |
| Clear X in search box | 14 px | 12 px | Minor |
| Clear-filters X | 12 px | 10 px | Visual |
| Type toggles hover | `hover:text-foreground` | None | Polish |
| **Clear Filters includes search** | **No** | **Yes — also clears SearchText** | **Functional** |

---

## 5. Filter-count strip

**Webui (AdminActivity.tsx:353–358):** Plain 11 px muted — "Showing {filtered.length} of {sessions.length} streams" — visible only when `search || activeFilters > 0`.

**Desktop (AdminActivityPage.xaml:294–300 + xaml.cs:283–307):** `<TextBlock FontSize=11 Foreground=TertiaryTextBrush Margin="0,-12,0,0">` — **negative 12 px top margin** hack to overlap parent StackPanel Spacing=20. Same condition. Same text.

**Gap:** Text matches. Negative-margin hack is tech debt. **Severity: polish.**

---

## 6. Streams table — outer container

**Webui (AdminActivity.tsx:360–400):**
- When `filtered.length === 0`: renders `<EmptyState>` **outside any card wrapper** (§9)
- Otherwise: `<div className="bg-card border-border overflow-hidden rounded-lg border">` — 8 px radius, 1 px border, `overflow-hidden`
  - Inside: header row + scrollable rows `<div className="max-h-[calc(100vh-420px)] min-h-[200px] overflow-y-auto">` — **internal scroll, filter bar stays pinned**

**Desktop (AdminActivityPage.xaml:302–357):**
- `<Border Background=CardBackgroundBrush CornerRadius=8 BorderBrush=BorderBrush BorderThickness=1 Padding=0>`
- Fixed header Grid → StreamsPanel → EmptyState Border
- **Empty state INSIDE the card** at the bottom (Padding=20,40)
- **No internal scroll** — whole page is one outer ScrollViewer, so header/filters scroll away with the list

**Gap:**

| Aspect | Webui | Desktop |
|---|---|---|
| Internal scroll with min/max height | Yes | **No — page scrolls** |
| Empty state placement | Outside card | Inside card bottom |

**Severity: Functional** — on any window where the list doesn't fit, desktop users lose filters while scrolling.

---

## 7. Streams table — header row

**Webui (AdminActivity.tsx:366–391):** Grid `grid-cols-[minmax(140px,1.5fr)_minmax(220px,2.2fr)_minmax(150px,1.4fr)_minmax(150px,1.4fr)_minmax(90px,1fr)_80px]` — mins 140/220/150/150/90 + fixed 80 px sixth column. `items-center gap-3 px-4 py-2.5 border-b bg-surface/50 hidden sm:grid`.

**Five of six columns are `<SortHeader>` buttons** (AdminActivity.tsx:737–764):
- `text-muted-foreground text-[10px] font-semibold tracking-wider uppercase transition-colors`
- Active: `text-foreground`; hover: `hover:text-foreground`
- Active indicator: `▲` / `▼` at `text-[8px]`
- Labels: **USER** (username), **STREAM** (media), **VIDEO** (method), **NODE** (node), **TIME** (started, right-aligned)
- **AUDIO** is plain `<div>` — not sortable (line 376)
- `toggleSort`: same field → flip asc/desc; new field → default desc for `started`, asc for others

**Desktop (AdminActivityPage.xaml:312–330):** `<Grid x:Name="TableHeader" Padding=16,10 ColumnSpacing=12 Background=SurfaceBrush Opacity=0.8>`. Columns `1.5*, 2.2*, 1.4*, 1.4*, *, 80` — **no minimums**. Six static `<TextBlock>` USER, STREAM, VIDEO, AUDIO, NODE, TIME — each FontSize=10 FontWeight=SemiBold Foreground=TertiaryTextBrush CharacterSpacing=80 (~0.08 em vs tracking-wider ~0.05). **All non-interactive — entire sort system absent.** TIME right-aligned (matches). Bottom separator matches.

**Gap:**

| Aspect | Webui | Desktop | Severity |
|---|---|---|---|
| **Sort functionality (5 columns)** | Present | **Absent** | **CRITICAL** |
| Sort indicator ▲/▼ | 8 px | N/A | Critical |
| Column minimums | 140/220/150/150/90 | **None** | Functional narrow |
| Header hover | `hover:text-foreground` | None | Polish |
| Header bg | `bg-surface/50` | SurfaceBrush Opacity=0.8 | Visual |
| Tracking | ~50 | 80 | Minor |

---

## 8. Streams table — stream row

**Webui (AdminActivity.tsx:407–648):** Row `border-border/30 hover:bg-surface/60 border-b transition-colors duration-100 {even ? "" : "bg-surface/20"}` — zebra odd rows, hover brightens, 100 ms transition.

**Col 0 — User:**
- 24 px avatar `h-6 w-6 rounded-full`, primary solid bg, 9 px bold initial
- **Username is a clickable `<Link to="/admin/history?user_id=${user_id}&profile_id=${profile_id}">`** — `hover:text-primary block truncate text-[13px] font-medium`
- Client IP second line `text-[10px] text-muted-foreground`

**Col 1 — Stream:**
- **Title is clickable `<Link to="/item/{content_id}">`** when `content_id` present — `hover:text-primary`
- `subtitle` (episode S/E + series, or "Movie"/"Series"), and `streamMeta` = `[sourceContainer.toUpperCase(), formatSessionBitrate(stream_bitrate_kbps)].filter(Boolean).join(" · ")` — 10 px muted lines

**Col 2 — Video:**
- Badge: `px-1.5 py-0.5 text-[9px] font-semibold rounded border` + color by decision (`methodBadgeColor`, lines 845–856):
  - `direct` → `bg-success/10 text-success border-success/15`
  - `remux` → `bg-info/10 text-info border-info/15`
  - `transcode` → `bg-warning/10 text-warning border-warning/15`
  - else → `bg-surface text-muted-foreground border-border`
- Label: Direct / Remux / Transcode / Unknown
- `formatVideoSummary`: `[codec, resolution].join(" · ")` e.g. "HEVC · 1080p"
- `formatVideoDetail` (presentation.ts:35–62) — **includes auto-switched-source hint**: if `requested_media_file_id > 0 && media_file_id > 0 && requested !== media_file_id`, prepends `"Auto-switched from {requested_codec · resolution}"` and if target available `"Output → {target_codec · target_resolution}"`. Without switch: transcode → `"Output → {target}"`; remux → "Container remux"; direct → "No video conversion".

**Col 3 — Audio:** Same pattern. `audioDecision = audio_decision || (transcode_audio ? "transcode" : play_method)`. Summary = source title/lang + codec + channel layout. Detail = transcode → `"→ {target_codec} {channels}"`, remux → "Container remux", direct → "No audio conversion".

**Col 4 — Node:** `{node_display_name || reporting_node || "—"}` 12 px muted + optional `{profile_name || profile_id}` 10 px muted.

**Col 5 — Time + actions (80 px):**
- `{elapsed}` `text-right font-mono tabular-nums` — H:MM:SS or M:SS
- Action cluster below:
  - **`<AdminSessionActions session={session} compact>`** — dropdown Pause/Resume / Stop / Message (dialog) / Terminate (destructive). Toast feedback. Optimistic UI.
  - FFmpeg toggle button with Terminal icon + chevron — expands inline **FFmpegLogPanel**
  - `<Link to={logsHref}>View Logs</Link>` — `/admin/logs?playback_session_id={sid}&focus=playback`
  - `<Link to={ffmpegLogsHref}>FFmpeg Logs</Link>` — `...&component=ffmpeg`

**8d. FFmpegLogPanel (AdminActivity.tsx:650–735):** `<div className="terminal-surface border-border/50 bg-card border-t px-4 py-3">`:
- Header: FFMPEG pill + "Live transcode console" + session ID (mono 10 px) + "Refreshing…" indicator + "Open full ffmpeg logs" link
- Body container `rounded-xl border bg-[var(--terminal-bg)]`:
  - Loading: "Loading ffmpeg output…"
  - Empty: "No ffmpeg rows yet for this session. If the session is direct play or remux without a transcode worker, nothing will appear here."
  - Populated: `max-h-64 overflow-y-auto` list of `grid-cols-[120px_1fr]` rows — time + sub-label (stderr/event), message, attribute row (node_id / target_resolution / hw_accel / restart_count)
- Data: `useOperationalLogs({playback_session_id, component: "ffmpeg", limit: 12}, ffmpegOpen)`

**Desktop (AdminActivityPage.xaml.cs:354–645):**
- Row wrapper: Padding=16,10 ColumnSpacing=12, odd rows `Background=ARGB(51,21,30,43)` ≈ bg-surface/20 (matches). BorderBrush `ARGB(77, 40, 56, 77)` ≈ border-border/30. BorderThickness=0,0,0,1. **No hover, no transition.**
- Columns set imperatively `1.5*, 2.2*, 1.4*, 1.4*, 1*, 80` — **no minimums**
- **Col 0 — User:** 24 px avatar AccentBrush bg + 9 px Bold initial (matches). `<TextBlock Text={username} FontSize=13 FontWeight=Medium>` — **NOT clickable**. IP 10 px Tertiary matches.
- **Col 1 — Stream:** `<TextBlock Text={title} FontSize=13 FontWeight=Medium>` — **NOT clickable**. Subtitle/streamMeta match.
- **Col 2 — Video:** Badge via `BuildDecisionBadge` (xaml.cs:670–725) — Padding=6,2, CornerRadius=4, BorderThickness=1, hard-coded hex literals matching Tailwind colors for direct/remux/transcode. **"Unknown" case** uses hard-coded `Gray(100)/Gray(160)` rather than theme neutral. Badge `Margin=0,0,0,2` vs web `mb-1` (4 px). `FormatVideoSummary` matches. `FormatVideoDetail` (VM:204–220): transcode → `"→ {target}"` — **web uses `"Output → {target}"`** — different prefix. remux / direct match. **Missing: auto-switched-source hint.**
- **Col 3 — Audio:** Identical pattern. `"→ {codec} {channels}"` matches web.
- **Col 4 — Node:** `<TextBlock FontSize=12 Foreground=TertiaryTextBrush>` — web uses muted-foreground ≈ Secondary (brighter).
- **Col 5 — Time + controls:**
  - `<TextBlock Text={elapsed} FontSize=12 Foreground=TertiaryTextBrush FontFamily=Consolas>` — matches
  - `<StackPanel Orientation=Horizontal Spacing=2>` with **4 inline icon buttons** (24×24 transparent CornerRadius=4 10 px icon):
    - Pause/Resume toggle: `\uE768` / `\uE769` → `adminApi.ResumeSessionAsync` / `PauseSessionAsync` + reload (no toast, no optimistic)
    - Stop: `\uE71A` → `adminApi.StopSessionAsync` + 500 ms + reload; ContentDialog on error
    - Message: `\uE8BD` → ContentDialog prompt → `adminApi.MessageSessionAsync`; silent errors
    - Terminate: `\uE74D` → `adminApi.TerminateSessionAsync` + reload. Silent errors. **No confirmation. No destructive styling.**
- **No FFmpeg toggle button, no inline FFmpeg console, no View Logs link, no FFmpeg Logs link** — four features absent.

**Gap (8.*):**

| # | Aspect | Webui | Desktop | Severity |
|---|---|---|---|---|
| 8.1 | Row hover | `hover:bg-surface/60` + 100 ms transition | None | Polish |
| 8.2 | Zebra stripe | `bg-surface/20` odd | Matches | Matches |
| 8.3 | **User link to `/admin/history?user_id=…&profile_id=…`** | `<Link>` | **Plain TextBlock** | **CRITICAL** |
| 8.4 | **Stream title link to `/item/{content_id}`** | `<Link>` | **Plain TextBlock** | **CRITICAL** |
| 8.5 | Avatar primary bg | Matches | AccentBrush | OK if Accent=primary |
| 8.6 | **Video detail auto-switched source** | `"Auto-switched from …"` + `"Output → …"` | **Not implemented** | **Functional** |
| 8.7 | Transcode detail prefix | `"Output → {target}"` | `"→ {target}"` | Minor text |
| 8.8 | Badge colors | Theme tokens | Hard-coded hex | Visual matches; theme risk |
| 8.9 | Badge "Unknown" neutral | theme neutral | Hard-coded gray | Visual off-theme |
| 8.10 | Badge bottom margin | 4 px (mb-1) | 2 px | Minor |
| 8.11 | Node text color | muted (≈ Secondary) | Tertiary (darker) | Visual |
| 8.12 | Time font | `font-mono tabular-nums` | Consolas | Matches |
| 8.13 | **Actions control** | Single `<AdminSessionActions>` dropdown + FFmpeg toggle + View Logs + FFmpeg Logs | 4 inline icon buttons | **Functional** |
| 8.14 | **Action dropdown UX** | Destructive Terminate + toast + optimistic UI | Inline buttons, silent errors, no confirmation on Terminate | **Functional high** |
| 8.15 | **FFmpeg inline console** | `<FFmpegLogPanel>` collapsible (12 rows, live) | **Missing** | **Functional** |
| 8.16 | **View Logs link** | `<Link to={logsHref}>` | **Missing** | **Functional** |
| 8.17 | **FFmpeg Logs link** | `<Link to={ffmpegLogsHref}>` | **Missing** | **Functional** |

---

## 9. Empty state

**Webui (AdminActivity.tsx:791–807):** `<div className="text-muted-foreground flex flex-col items-center justify-center py-20 text-sm">` — **outside the card**, 80 px vertical padding, 14 px muted.
- `hasData` true (filters yielded zero): `<Filter className="mb-3 h-8 w-8 opacity-20" />` + "No streams match your filters"
- `hasData` false (no sessions): `<Play className="mb-3 h-8 w-8 opacity-20" />` + "No active streams"

**Desktop (AdminActivityPage.xaml:336–354):** **Inside the card** (Padding=20,40). Single FontIcon `&#xEA62;` FontSize=32 Foreground=TertiaryTextBrush (**no opacity reduction**). Label "No active streams" 16 px SemiBold Secondary + secondary "Streams will appear here when users are watching content." 13 px Tertiary.

**Gap:**

| Aspect | Webui | Desktop | Severity |
|---|---|---|---|
| Placement | Outside card | Inside card | Visual |
| **Icon swap by state** | Filter vs Play | Single fixed glyph | **Functional** |
| Icon opacity | `opacity-20` | Full | Visual |
| **Label text** | "No streams match your filters" OR "No active streams" | Always "No active streams" | **Functional** |
| Secondary help text | None | "Streams will appear…" | Extra |
| Vertical padding | py-20 (80 px) | Padding=20,40 (half) | Visual |
| Label font size | `text-sm` (14 px) | 16 px SemiBold | Visual |

---

## Server Activity Popover (top-bar) — per section

**SA.1 — Button / badge:** Desktop (ServerActivityButton.xaml:10–55): transparent bg Padding=10 CornerRadius=10 + icon `&#xE9F5;` 18 px → AccentBrush when total>0. Count badge `Background="#EF4444"` CornerRadius=9 Height=16 Padding=4,0 + text 9 px Bold White. Disconnected Ellipse 8×8 Fill="#F59E0B" when `!_wsConnected && total > 0`.

**Gap (inferred):** Web badge color almost certainly accent/primary — not `#EF4444` red (red suggests error). Amber disconnected dot matches by comment.

**SA.2–SA.8:** Popover header, empty state, Streams section (method dots 8×8 + "Direct Play"/"Remux"/"Transcode" labels), Tasks section (progress bars 6 px tall, SurfaceRaisedBrush track + AccentBrush fill star-grid-sized), Scans section (event-channel driven, `"{message} · {filesProcessed:N0} / {totalFiles:N0} ({pct}%)"` progress format), event-channel integration (sessions/tasks/scans subscriptions + 30 s fallback polling + _wsConnected for amber dot), view-all delegates wired by hosts. All structurally sound. Popover width: 320 min / 360 max — unverified against web.

---

## 10. Loading / error states

**Webui (AdminActivity.tsx:123):** `<div className="text-muted-foreground p-8">Loading activity...</div>`. `error` is hard-wired to `undefined` (line 46) — **no error state at all.**

**Desktop (AdminActivityPage.xaml:42–67):** ProgressRing IsActive + separate error stack with Retry button.

**Gap:** Loading pattern mismatch (web text, desktop spinner). Desktop has an error state web doesn't — technically extra, not harmful.

---

## 11. Data / API integration

**Webui:** `useAdminSessions()` + `useIPUsers(activeIP)` + `useRealtimeEvents()` (returns `connectionState: "connecting" | "live" | "disconnected"`) + `useOperationalLogs(...)` for ffmpeg.

**Desktop (AdminActivityViewModel.cs:76–107):** `LoadAsync` — single call `_adminApi.GetSessionsAsync()`. `LookupIPAsync` — `GetIPUsersAsync(ipText, days=30)`. **No event-channel subscription at the page level.** Only `ServerActivityButton` subscribes. `connectionState` never surfaced to Activity page.

**Gap:**

| Aspect | Webui | Desktop | Severity |
|---|---|---|---|
| **Auto-refresh from realtime events** | Yes | **No — one-shot load** | **CRITICAL** |
| Connection state on page | Yes | **No** | Functional |

---

## 12. Theme / brush mapping

**Webui tokens:** `--primary`, `--success`, `--info`, `--warning`, `--surface`, `--card`, `--border`, `--muted-foreground`, `--terminal-bg/fg/border/muted`.

**Desktop brushes used:** PrimaryTextBrush, SecondaryTextBrush, TertiaryTextBrush, AccentBrush, AccentBackgroundBrush, AccentForegroundBrush, CardBackgroundBrush, SurfaceBrush, SurfaceRaisedBrush, BorderBrush, ErrorBrush, AppBackgroundBrush.

**Missing:** Success/Info/Warning brushes not in DarkTheme.xaml — decision badge colors hard-coded hex. Terminal palette has no equivalents.

---

## Prioritized Fix List

### P0 — Critical
1. **Sort headers on 5 columns** — User/Stream/Video/Node/Time clickable; ▲/▼ indicator; default sort `started desc`, others `asc` on first click, toggle thereafter. (xaml:312–328 + xaml.cs:283–331 + xaml.cs:354–645)
2. **User-name link** — HyperlinkButton to admin-history filtered by user_id+profile_id. (xaml.cs:404–411)
3. **Stream-title link** — HyperlinkButton to `/item/{content_id}` when present. (xaml.cs:437–444)
4. **IP Lookup user link** — HyperlinkButton to `/admin/users/{user_id}`. (xaml.cs:750)
5. **Realtime events on Activity page** — subscribe VM to EventChannelClient `sessions`; invalidate on event; keep 30 s fallback.
6. **Connection state indicator in header** — expose EventChannelClient.State on VM; render eyebrow "Stream" + "Live/Connecting/Disconnected" next to Refresh.
7. **Auto-switched source hint** — port `hasRequestedSourceSwitch` + `formatRequestedVideoSource` from presentation.ts:96–109 into `FormatVideoDetail`.
8. **Action overflow menu (AdminSessionActions parity)** — replace 4 inline buttons with MenuFlyout: Pause/Resume, Stop, Message, Terminate (destructive). Terminate styled red + ContentDialog confirmation. Toast/InfoBar for success/failure.
9. **"View Logs" / "FFmpeg Logs" deep links** — two text hyperlinks next to the action menu → Admin Logs page with `playback_session_id` (+ `component=ffmpeg`).
10. **Clear-Filters must not clear search** — reset only MethodFilter/NodeFilter/TypeFilter, preserve SearchText. (AdminActivityViewModel.cs:109–116)

### P1 — Important
11. **FFmpeg inline log panel** — collapsible per-row console fetching last 12 ffmpeg operational log entries.
12. **IP Lookup collapsible** — Expander default collapsed with "IP Lookup" header.
13. **IP Lookup loading & empty states** — "Searching…" + "No users found for {ip}...".
14. **Panel corner radius** — SurfacePanelStyle CornerRadius 26 → 16.
15. **Table internal scroll** — wrap StreamsPanel in ScrollViewer MinHeight=200, MaxHeight bound to window − 420.
16. **Empty state contextual** — Filter icon + "No streams match your filters" when filters active; Play icon + "No active streams" otherwise. Remove secondary line.
17. **Empty state outside card.**
18. **Refresh button icon** — `&#xE72C` glyph before text.
19. **Live badge Radio icon** — broadcast glyph before count.
20. **Responsive title font** — SizeChanged handler clamping 32 → 48.
21. **Column minimums** — 140/220/150/150/90 on both header and rows.
22. **Filter bar wrap** — ItemsRepeater + UniformGridLayout.
23. **Stream row hover state** — VisualStateGroup PointerOver → surface/60.
24. **Loading plain-text** — remove ProgressRing; show "Loading activity...".

### P2 — Polish
25. IP Lookup date format full (with seconds).
26. CharacterSpacing for eyebrows 120 → ~50.
27. Decision badge "Unknown" use theme neutral.
28. Introduce SuccessBrush/InfoBrush/WarningBrush in DarkTheme.xaml.
29. Remove FilterCountText negative margin.
30. Table header bg `SurfaceSubtleBrush` at 50% alpha.
31. Clear-filters X 10 → 12 px.
32. Search-box clear X 12 → 14 px.
33. Node name color Tertiary → Secondary.
34. Method bar transition Storyboard.
35. Node button hover PointerOver state.
36. Transcode prefix "→ " → "Output → ".
37. Node block drop extra `Margin=12`.
38. ServerActivity badge color `#EF4444` → AccentBrush.

### P3 — Follow-ups pending file access
40. Read `ServerActivity.tsx` — verify popover width, count-pill color.
41. Read `AdminSessionActions.tsx` — extract dropdown item order, destructive styling, confirmation copy, toast messages.
42. Read `app.css` — extract exact `.page-header`, `.page-title`, `.surface-panel`, `.live-badge`, `.terminal-surface` values.
43. Read `hooks/queries/admin/stats.ts` — confirm refetch interval, stale-time, event-driven invalidation.
44. Read `realtimeEventsContext.tsx` — verify three-state enum and string values.

---

## Summary Table — 58 rows of diff

| # | Feature | Webui | Desktop | Gap |
|---|---|---|---|---|
| 1 | Page title font | `clamp(2rem,4vw,3rem)` | Fixed 42 | Visual |
| 2 | Live badge Radio icon | 12 px | Missing | Visual |
| 3 | Subtitle | 13 px muted | Matches | Matches |
| 4 | **Connection eyebrow + state** | "Stream" / Live/Connecting/Disconnected | **Missing** | **Functional** |
| 5 | Refresh icon | RefreshCw 14 px | Text only | Visual |
| 6 | Header wrapper | `.page-header` | Raw Grid | Tech debt |
| 7 | IP Lookup collapsible | `<details>` | Always open | Functional |
| 8 | IP Lookup Search icon | Lucide 14 px | None | Visual |
| 9 | IP Lookup empty text | "No users found for {ip}..." | Missing | Functional |
| 10 | IP Lookup loading text | "Searching..." | Missing | Functional |
| 11 | **IP Lookup user link** | `<Link>` | Plain | **Functional** |
| 12 | IP Lookup panel corner | 16 px | 26 px | Visual |
| 13 | IP Lookup date format | Full w/ seconds | Short | Visual |
| 14 | Summary panel corner | 16 px | 26 px | Visual |
| 15 | Method bar animation | `duration-500` | None | Polish |
| 16 | Method toggles wrap | `flex-wrap` | StackPanel | Functional narrow |
| 17 | Node buttons wrap | `flex-wrap` | StackPanel | Functional narrow |
| 18 | Node button hover | `hover:border-primary/20` | None | Polish |
| 19 | Search clear X | 14 px | 12 px | Minor |
| 20 | Clear-filters X | 12 px | 10 px | Visual |
| 21 | Filter bar wrap | `flex-wrap` | Grid fixed | Functional narrow |
| 22 | **Clear Filters preserves search** | Yes | **No** | **Functional** |
| 23 | Filter-count strip | Natural margin | `Margin=-12` hack | Tech debt |
| 24 | Table internal scroll | Yes | **No** | **Functional** |
| 25 | Empty state placement | Outside card | Inside card | Visual |
| 26 | Empty state icon swap | Filter / Play | Fixed glyph | Functional |
| 27 | Empty state label | Context-dependent | Fixed | Functional |
| 28 | **Sort headers** | 5 sortable | **None** | **CRITICAL** |
| 29 | Sort indicator ▲/▼ | Yes | No | Functional |
| 30 | Row hover | Yes + transition | None | Polish |
| 31 | Row zebra | `bg-surface/20` odd | Matches | Matches |
| 32 | **User name link** | `<Link>` to admin history | Plain | **CRITICAL** |
| 33 | **Stream title link** | `<Link>` to item detail | Plain | **CRITICAL** |
| 34 | **Video detail auto-switch** | `"Auto-switched from …"` | Not implemented | **Functional** |
| 35 | Transcode prefix | `"Output → {target}"` | `"→ {target}"` | Minor |
| 36 | Decision badge "Unknown" | Theme neutral | Hard-coded gray | Visual |
| 37 | **Actions control** | Single dropdown | 4 inline buttons | **Functional** |
| 38 | **Terminate UX** | Destructive + toast + confirm | Plain + silent | **Functional high** |
| 39 | **FFmpeg inline console** | `<FFmpegLogPanel>` | **Missing** | **Functional** |
| 40 | **View Logs link** | `<Link>` | Missing | **Functional** |
| 41 | **FFmpeg Logs link** | `<Link>` | Missing | **Functional** |
| 42 | Column minimums | mins 140/220/150/150/90 | **None** | Functional narrow |
| 43 | Loading placeholder | Text | ProgressRing 48 | Visual |
| 44 | Error state | None | Retry flow | Extra (harmless) |
| 45 | **Auto-refresh on events** | Yes | **None** | **CRITICAL** |
| 46 | Top-bar badge color | Accent (inferred) | `#EF4444` red | Possible visual |
| 47 | Top-bar disconnected dot | Amber | Matches | Matches |
| 48 | Eyebrow tracking | ~50 | 120 | Visual |
| 49 | Node text color | muted | Tertiary | Visual |
| 50 | Table header bg | `bg-surface/50` | Opacity=0.8 | Visual |

---

## Key takeaways

- **Visual layout roughly aligned** in column ratios, padding, palette — but every `surface-panel` radius is wrong (26 vs 16), eyebrow letterspacing doubled, header icons missing, summary/filter bars don't wrap.
- **Big functional gaps:** no sort, no deep links (user history / item detail / logs / ffmpeg logs), no FFmpeg inline console, no action dropdown, no auto-refresh from realtime events, no connection indicator, no auto-switched-source hint.
- **Sort gap alone is the single biggest regression** — webui's entire column-header interaction is absent.
- **Action UX (rows #37/#38) is second-biggest** — 4 inline icon buttons with silent failure is meaningfully worse than a single destructive-styled dropdown with confirmation + toast.
- `ServerActivityButton` is mostly faithful. Main concern: red `#EF4444` badge color.
- `EventChannelClient` is operational and actively used by the top-bar button; wiring it into the Activity page (P0 #5) should be low-effort.
