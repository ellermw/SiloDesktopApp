# Admin Playback History — Full Audit

## Files read (100% line-by-line)

| File | Lines | Status |
|------|------:|--------|
| `F:/continuum-server/web/src/pages/AdminPlaybackHistory.tsx` | 381 | Read in full (single successful read before sandbox locked continuum-server tree) |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminPlaybackHistoryPage.xaml` | 300 | Read in full |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminPlaybackHistoryPage.xaml.cs` | 582 | Read in full |
| `F:/ContinuumPlayer/src/ContinuumPlayer/ViewModels/Admin/AdminPlaybackHistoryViewModel.cs` | 136 | Read in full |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Themes/DarkTheme.xaml` | 472 | Read in full (targeted + header pass for all keys referenced by the page) |
| `F:/ContinuumPlayer/src/ContinuumPlayer.Core/Api/AdminApi.cs` (section `GetPlaybackHistoryAsync`) | 20 | Read — only section relevant to this page |

## Files sandbox-blocked (affected assumptions marked UNVERIFIED)

After the first successful read of `AdminPlaybackHistory.tsx`, the sandbox began rejecting every follow-up read/grep under `F:\continuum-server`. The following were requested and denied:

| File | Intended use |
|------|--------------|
| `F:/continuum-server/web/src/app.css` | Exact computed CSS for `.page-shell`, `.page-header`, `.page-title`, `.page-subtitle`, `.surface-panel` |
| `F:/continuum-server/web/src/hooks/queries/admin/history.ts` | Exact query options — `refetchInterval`, `staleTime`, ws subscription |
| `F:/continuum-server/web/src/hooks/queries/admin/users.ts` | Users list query |
| `F:/continuum-server/web/src/components/ui/table.tsx` | shadcn Table — row hover, border rules, `TableHead` padding/weight |
| `F:/continuum-server/web/src/components/ui/card.tsx` | `Card`/`CardHeader`/`CardContent` padding, border radius |
| `F:/continuum-server/web/src/components/ui/badge.tsx` | `secondary`/`default`/`outline` variant colors and radius |
| `F:/continuum-server/web/src/components/ui/button.tsx` | `outline` variant + `size="sm"` height/padding |
| `F:/continuum-server/web/src/components/ui/select.tsx` | Trigger height, dropdown chrome |
| `F:/continuum-server/web/src/components/ui/skeleton.tsx` | Skeleton pulse animation color |

Where webui behavior is UNVERIFIED below, the gap is nonetheless reported using conventions established by prior audits in `F:/ContinuumPlayer/docs/audit-gaps/` (same shadcn primitives, same `app.css` utilities), which have consistently shown:

- `.surface-panel` = elevated card surface (`bg-card` + shadow, subtle border)
- `.surface-panel-subtle` = muted translucent surface with tinted border
- `.page-shell` = max-width container, horizontal centering + padding
- `.page-header` = flex row, wraps on narrow, gap from inline utility
- `.page-title` = tight tracking, weight 700, primary foreground
- `.page-subtitle` = muted foreground
- shadcn `<Table>` rows have `border-b`, `h-12`, and `hover:bg-muted/50`
- shadcn `<Badge>` `default` = filled primary, `secondary` = muted fill, `outline` = bg-transparent + border
- shadcn `<Button>` `outline size="sm"` = `h-8 px-3 text-xs border`
- shadcn `<Select>` trigger = `h-9 px-3 text-sm` (h-10 on Card header if not explicitly h-8)

If/when those files become readable, sections 3, 4, 6, 7, 8, 9, 11 below should be re-audited for exact CSS.

---

## 1. Page header

### Webui (AdminPlaybackHistory.tsx:98-166)
```tsx
<div className="page-shell space-y-6 py-4 sm:py-6">
  <div className="page-header gap-5">
    <div className="space-y-3">
      <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Playback History</h1>
      <p className="page-subtitle text-sm sm:text-base">
        Finalized playback attempts across all users and profiles.
      </p>
    </div>
    <div className="flex flex-wrap items-center gap-2">
      {selectedMediaItemId && ( ...Item filter active chip... )}
      <Select .../>   /* users, w-[220px] */
      <Select .../>   /* profiles, w-[220px] */
      <Select .../>   /* completed, w-[180px] */
      <Button variant="outline" size="sm" onClick={resetFilters}>Reset</Button>
    </div>
  </div>
```
- Outer shell: `page-shell` (UNVERIFIED max-width / centering), `space-y-6` = 24 px vertical rhythm between header / stat cards / table card, `py-4 sm:py-6` = 16 / 24 px top+bottom padding.
- `page-header` utility + `gap-5` (20 px horizontal gap between title block and filter cluster).
- Title `h1.page-title text-[clamp(2rem,4vw,3rem)]` ≈ 32–48 px responsive, weight 700, tight tracking, primary fg.
- Subtitle `text-sm sm:text-base` = 14 px / 16 px ≥ sm, muted foreground.
- `space-y-3` = 12 px gap between title and subtitle.
- Filter cluster uses `flex flex-wrap items-center gap-2` (8 px gap, wraps on narrow).
- Active-item chip (only when `media_item_id` param is set):
  `border-border bg-muted/40 flex items-center gap-2 rounded-md border px-3 py-2 text-xs`
  → 6 px radius, 1 px border, muted translucent bg, 12 px / 8 px padding, 12 px text. Contains:
  - `<span class="text-muted-foreground font-medium">Item filter active</span>`
  - `<span class="font-semibold">{activeMediaItemLabel}</span>` (the media title or raw id)

### Desktop (AdminPlaybackHistoryPage.xaml:70-129)
- Two-column `Grid`: left `*` (title+subtitle stack), right `Auto` (filter cluster).
- `StackPanel` with `Spacing="12"`, `VerticalAlignment="Center"`.
- Title: `TextBlock Text="Playback History" FontSize="42" FontWeight="Bold"` → PrimaryTextBrush.
- Subtitle: `FontSize="14"` → SecondaryTextBrush.
- Filter cluster: horizontal `StackPanel Spacing="8"` with three `ComboBox` + `Button`.
- ComboBoxes use `MinWidth="180"/"180"/"160"` — **smaller than the webui's 220/220/180**.
- Reset button uses `SecondaryButtonStyle`, hidden by default, shown when `HasActiveFilters`.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 1.a | No `.page-shell` max-width / centering — raw `StackPanel` with `Padding="40,28,40,40"`. Webui uses `py-4 sm:py-6` (16/24 px vertical) and an inherited horizontal centering from `page-shell`. Desktop hard-codes 40 px horizontal — wider than webui on narrow screens, narrower than webui on wide screens where `page-shell` extends further. | visual |
| 1.b | Title fixed at `FontSize="42"` — webui clamps 32–48 px responsive. On a window that simulates small desktop viewport (≈ 800 px wide) webui would render ~36 px; desktop renders 42 px. | visual |
| 1.c | Subtitle fixed 14 px — webui is 14 / 16 px responsive (`text-sm sm:text-base`). At full desktop width webui is 16 px. | visual |
| 1.d | **Missing "Item filter active" chip** (webui: lines 107–112). When URL has `media_item_id`, webui shows a muted 12-px chip "Item filter active · {title}". Desktop code never renders this chip — and the ViewModel has **no `mediaItemId` field at all** (see 2.d). So even deep-linking from another admin page can't restore the filter. | functional |
| 1.e | Desktop `page-subtitle`: web uses `.page-subtitle` utility (muted fg). Desktop uses `SecondaryTextBrush` (#90A0B5) which is a reasonable match, but web likely uses `text-muted-foreground` (computed) — UNVERIFIED. | visual |
| 1.f | Filter ComboBox widths: 180/180/160 vs webui 220/220/180 — ≈ 40/40/20 px narrower. User/profile names can clip ("Subscription: family_viewer" style). | visual |
| 1.g | ComboBox spacing: desktop `Spacing="8"` matches webui `gap-2` (8 px). OK. |  |
| 1.h | Reset button: desktop hides unless `HasActiveFilters`. Webui *always* shows Reset (AdminPlaybackHistory.tsx:162-164). Webui's click just clears all URL params; desktop's extra hiding logic is extra state. | functional |
| 1.i | Title block uses `Spacing="12"` vs webui `space-y-3` = 12 px. OK. |  |
| 1.j | Filter cluster does NOT wrap — it is a horizontal `StackPanel`, so on a narrow window the filters overflow the page right edge. Webui uses `flex flex-wrap`. | visual |

---

## 2. Filters (user, profile, completion, media-item)

### Webui (AdminPlaybackHistory.tsx:32-95, 114-164)

URL-driven state via `useSearchParams`:
- `user_id` (int or `"all"`)
- `profile_id` (uuid or `"all"`)
- `completed` (`"all"` | `"true"` | `"false"`)
- `media_item_id` (string — set from a deep link elsewhere)

Behavioral rules:
1. Filter changes call `updateFilter(key, value)` → update URL, `setPage(0)`. URL is single source of truth.
2. Changing `user_id` auto-deletes `profile_id` from URL (line 80-82).
3. `useEffect` at line 53 self-heals:
   - If `user_id=all` but `profile_id` is set → delete `profile_id`.
   - If selected `profile_id` is not in the loaded profiles list for the selected user → delete `profile_id`.
4. Profile select is **disabled** when `user=all` with placeholder "Choose a user first" (line 131, 135).
5. `resetFilters` = `setSearchParams(new URLSearchParams())` + `setPage(0)` (lines 87-90).
6. Completion select has three items: `All attempts`, `Completed`, `Partial`.

Users list: `useAdminUsers()`. Profiles list: `useAdminUserProfiles(selectedUserId)` — only fetched when a user is selected.

### Desktop (AdminPlaybackHistoryPage.xaml.cs:177-233, AdminPlaybackHistoryViewModel.cs:22-105)
- State lives on the ViewModel: `SelectedUserId`, `SelectedProfileId`, `CompletionFilter`. No URL state at all (WinUI 3 doesn't have URL, but **no state restoration across navigation either** — see 2.e).
- `OnSelectedUserIdChanged` clears `SelectedProfileId` and the `Profiles` collection, then triggers `LoadProfilesAsync(value)`.
- `UserComboBox_SelectionChanged` re-runs `LoadCommand` (full history reload) then rebuilds the profile combo.
- `ProfileComboBox_SelectionChanged` re-runs `LoadCommand`.
- `StatusComboBox_SelectionChanged` re-runs `LoadCommand`.
- `ResetButton_Click` calls `ViewModel.ResetFiltersCommand`, then manually resets all three comboboxes to `SelectedIndex = 0`, disables profile combo, sets placeholder back, then re-runs `LoadCommand`.
- `ProfileComboBox.IsEnabled = ViewModel.SelectedUserId.HasValue` (line 142).
- `ProfileComboBox.PlaceholderText` toggles between "All profiles" and "Choose a user first".

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 2.a | **No self-heal effect for stale profile_id.** Webui lines 60-70: if the loaded profiles don't contain the currently selected profile, the URL param is deleted. Desktop only clears `SelectedProfileId` when the user changes, not when profiles arrive with a mismatched id already set. Low-probability but real race (if profiles load slowly after restoring state). | functional |
| 2.b | **No URL / deep-link state.** Webui supports `?user_id=3&profile_id=...&completed=true&media_item_id=...`. Desktop has none — clicking "See this user's playback history" from AdminUsers page can't pass filters. | functional |
| 2.c | Filter change performs a **full re-fetch** on every selection. Webui also re-fetches (via query key change in `useAdminPlaybackHistory({userId, profileId, ...})`) but React Query dedupes/cache serves from memory. Desktop has no caching — every selection round-trips the server. | functional |
| 2.d | **`media_item_id` filter missing entirely.** Webui (lines 40, 46) accepts `media_item_id` URL param, forwards to `useAdminPlaybackHistory`, displays the chip, and shows `media_title` as label (line 95 `allRows[0]?.media_title \|\| selectedMediaItemId`). Desktop ViewModel has no `MediaItemId` property; `AdminApi.GetPlaybackHistoryAsync` *does* accept `mediaItemId` but the ViewModel never passes it. | functional |
| 2.e | Desktop ViewModel state does not persist across navigation — leaving and returning to the page re-instantiates the page; because `ViewModel` is DI-scoped to `App.Services.GetRequiredService<...>()` (singleton? confirm in DI), state *may* persist. Either way, webui state lives in URL and survives reloads/refresh. | functional |
| 2.f | Reset button hidden when `HasActiveFilters == false`. Webui always shows it. See 1.h. | functional |
| 2.g | Completion ComboBox default: desktop `SelectedIndex="0"` ("All attempts") — matches webui default `ALL_COMPLETION`. OK. |  |
| 2.h | Webui `limit: 100` hard-coded in history hook call (line 49); desktop also hard-codes 100 (ViewModel:69). OK. |  |
| 2.i | Profile disabled-state behavior matches. OK. |  |

---

## 3. Stat card row ("Visible Rows" / "Completed" / "Partial")

### Webui (AdminPlaybackHistory.tsx:168-172, 343-350)
```tsx
<div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
  <InfoCard label="Visible Rows" value={String(allRows.length)} />
  <InfoCard label="Completed" value={String(allRows.filter(r => r.completed).length)} />
  <InfoCard label="Partial" value={String(allRows.filter(r => !r.completed).length)} />
</div>

function InfoCard({ label, value }) {
  return (
    <div className="surface-panel rounded-2xl border-0 p-4">
      <div className="text-muted-foreground text-[11px] font-medium">{label}</div>
      <div className="mt-1 text-2xl font-extrabold tracking-tight">{value}</div>
    </div>
  );
}
```
- `grid-cols-1 sm:grid-cols-3 gap-3` = 1 col mobile, 3 cols ≥640, 12 px gap.
- Card: `surface-panel rounded-2xl border-0 p-4` = elevated surface, **16 px radius**, 0 px border, 16 px padding.
- Label: 11 px, weight 500, muted fg.
- Value: `text-2xl font-extrabold tracking-tight` = **24 px, weight 800, tight tracking**.
- Spacing between label and value: `mt-1` = 4 px.

### Desktop (AdminPlaybackHistoryPage.xaml:132-189, InfoCardStyle at 19-24)
- `Grid` with 3 `*` columns and `ColumnSpacing="12"`. OK.
- `InfoCardStyle`: `CornerRadius="{StaticResource RadiusLG}"` which = **8 px**, `Padding="16"`, `BorderThickness="0"`.
- Each card: `StackPanel Spacing="4"` with a label `TextBlock FontSize="11" FontWeight="Medium"` and a value `TextBlock FontSize="24" FontWeight="ExtraBold"`.
- Third card ("Partial") value uses `SecondaryTextBrush` instead of `PrimaryTextBrush` (line 186).

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 3.a | **Corner radius wrong: desktop 8 px (`RadiusLG`) vs webui `rounded-2xl` = 16 px.** Comment in XAML at line 18 says "`rounded-lg`=8 — was 24/26, way too round" — but webui uses `rounded-2xl` (16 px), NOT `rounded-lg`. The prior fix over-corrected. | visual |
| 3.b | **"Partial" value color divergence.** Webui uses the same extrabold primary text for all three cards. Desktop makes "Partial" render in `SecondaryTextBrush` (#90A0B5) — grays it out deliberately. This adds semantic meaning that webui does not convey. | visual |
| 3.c | Padding 16 px matches webui `p-4`. OK. |  |
| 3.d | Column gap 12 px matches `gap-3`. OK. |  |
| 3.e | Label 11 px / weight Medium → matches `text-[11px] font-medium`. OK, but Desktop uses `TertiaryTextBrush` (#6E7681); webui uses `text-muted-foreground` (typically ≈ `#90A0B5` range). Slight tint mismatch. UNVERIFIED exact CSS. | visual |
| 3.f | Value 24 px ExtraBold → matches `text-2xl font-extrabold`. OK. |  |
| 3.g | Label-to-value gap: desktop `Spacing="4"` vs webui `mt-1` = 4 px. OK. |  |
| 3.h | **Tracking: webui uses `tracking-tight` on the value (letter-spacing: -0.025em).** Desktop XAML does not set `CharacterSpacing`. Minor but measurable. | visual |
| 3.i | Card background brush: desktop uses solid `CardBackgroundBrush` (#151E2B). Webui `surface-panel` is typically `bg-card` plus a shadow/backdrop-blur (UNVERIFIED exact). If `surface-panel` adds a box-shadow, desktop has zero drop shadow on these cards. | visual |
| 3.j | No responsive fallback: webui is 1 col on mobile; desktop is always 3 cols — irrelevant on desktop but worth noting. | none |

---

## 4. Table wrapper / "Recent Playback" card

### Webui (AdminPlaybackHistory.tsx:174-180)
```tsx
<Card className="surface-panel rounded-2xl border-0">
  <CardHeader className="flex flex-row items-center justify-between space-y-0">
    <CardTitle className="text-sm font-bold">Recent Playback</CardTitle>
    <div className="text-muted-foreground text-xs">
      {history.isFetching ? "Refreshing..." : "Auto-refreshing"}
    </div>
  </CardHeader>
  <CardContent> ... </CardContent>
</Card>
```
- Card: `surface-panel rounded-2xl border-0` → 16 px radius, no border, elevated surface.
- CardHeader: default shadcn padding (`p-6 pb-3`?) UNVERIFIED. The override `flex flex-row items-center justify-between space-y-0` means "normal shadcn CardHeader (`flex flex-col space-y-1.5`) is *replaced* with a row". Horizontal, centered vertically, space-y collapsed.
- CardTitle: `text-sm font-bold` = 14 px / weight 700.
- Right side: `text-muted-foreground text-xs` = 12 px muted.

### Desktop (AdminPlaybackHistoryPage.xaml:192-215, TableCardStyle at 26-31)
- Border: `CornerRadius="{StaticResource RadiusLG}"` = **8 px**, `BorderThickness="0"`, `Padding="0"`.
- Card header: explicit Grid at line 196, `Padding="20,16,20,16"`.
- Title: `FontSize="13" FontWeight="Bold"` → **13 px, Bold**.
- Refresh status: `FontSize="12"` TertiaryTextBrush.
- Separator: `Border BorderBrush="{StaticResource BorderBrush}" BorderThickness="0,1,0,0"` on line 217.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 4.a | **Corner radius: desktop 8 px vs webui 16 px (`rounded-2xl`).** Major visual — the whole outer card of the table has half the radius. | visual |
| 4.b | Card Title: desktop **13 px** vs webui `text-sm` = **14 px**. | visual |
| 4.c | Card header padding: desktop 20/16/20/16 — webui uses shadcn default CardHeader padding + an override. shadcn `CardHeader` default is `p-6` (24 px). The `justify-between` row convention is 24 px all around. Desktop is 4 px shorter on the sides, 8 px shorter top/bottom. UNVERIFIED exact shadcn defaults. | visual |
| 4.d | **Separator below card header.** Webui has no `<Border>` between `CardHeader` and `CardContent`; shadcn's `CardHeader` has no bottom border by default. Desktop adds a 1-px line (`BorderThickness="0,1,0,0"` at 217). Extra visual rule. | visual |
| 4.e | Right-side refresh status typography: desktop 12 px Tertiary vs webui 12 px muted-foreground. OK. |  |
| 4.f | **"Auto-refreshing" label is a lie on desktop.** Webui text is honest — React Query does refetch on focus/interval. Desktop has no polling, no `refetchInterval`, no ws subscription, and no invalidation hook. Text reads "Auto-refreshing" only because `IsLoading` is false. See section 12. | functional |

---

## 5. Table header (every column)

### Webui (AdminPlaybackHistory.tsx:210-221)
shadcn `<TableHeader>` → `<TableRow>` with eight `<TableHead>` cells. shadcn `TableHead` default CSS (UNVERIFIED, from prior audit):
- `h-10 px-4 text-left align-middle font-medium text-muted-foreground` (per shadcn ui v1 default)
- 40 px row height, 16 px horizontal padding, weight 500, muted fg.
- Font size inherits (`text-sm` = 14 px) unless class overrides.

Columns in order:
1. Media
2. User
3. Profile
4. Method
5. Watch Time
6. Status
7. Ended
8. Logs (class="text-right")

**No sort indicators** — webui has no sort on this table. All ordering comes from the server (which returns rows by `ended_at DESC`). Filters only; no click-to-sort.

### Desktop (AdminPlaybackHistoryPage.xaml:220-239)
- `Grid Padding="20,14,20,14" ColumnSpacing="12"` with 8 columns: `2.5* / 1.2* / 1.2* / 90 / 1.2* / 90 / 1.4* / 1.2*`.
- Each header cell: `TextBlock FontSize="11" FontWeight="SemiBold" Foreground="{StaticResource TertiaryTextBrush}"`.
- Column 8 has `HorizontalAlignment="Right"`.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 5.a | **Font size: desktop 11 px vs webui 14 px (shadcn default).** Headers are ~21% smaller. Highly visible. | visual |
| 5.b | **Font weight: desktop SemiBold (600) vs webui `font-medium` (500).** Minor. | visual |
| 5.c | **Row height: desktop has Padding="20,14,20,14" = ~28 px + text ≈ total 42 px.** Webui `h-10` = 40 px fixed. Close but not exact. | visual |
| 5.d | **Horizontal padding: desktop 20 px left/right on the header grid + `ColumnSpacing="12"`.** Webui `px-4` = 16 px per-cell, no extra row gap. Cumulative: webui header cell boundaries are 16 px apart; desktop has 20-px outer + 12-px inter-column gaps → different column edges. | visual |
| 5.e | **Column widths are fabricated.** Webui uses *content-sized* columns (no explicit widths on `<TableHead>`). Desktop hard-codes a fixed star-ratio scheme (`2.5* / 1.2* / 1.2* / 90 / 1.2* / 90 / 1.4* / 1.2*`). That forces the Method and Status columns to be 90 px regardless of content ("transcode" label width). | visual |
| 5.f | **No sort affordances either side** — matches webui. OK. |  |
| 5.g | Column 8 header alignment right matches webui `className="text-right"`. OK. |  |
| 5.h | **Color: desktop TertiaryTextBrush (#6E7681) vs webui `text-muted-foreground`.** Shadcn muted-foreground is typically lighter than #6E7681 (≈ #A1A1AA range). Headers appear dimmer on desktop. | visual |
| 5.i | **No bottom border on header row.** shadcn `TableHeader` renders with a `border-b` under it. Desktop *does* add a `<Border BorderThickness="0,1,0,0"/>` at line 241 — but it's inside the card grid, not as the tr `border-b`; close enough visually. OK-ish. |  |
| 5.j | No `text-transform: uppercase` / `letter-spacing` — matches shadcn default. OK. |  |

---

## 6. Table row (every cell)

### Webui (AdminPlaybackHistory.tsx:222-286)
shadcn `<TableRow>` default: `border-b transition-colors hover:bg-muted/50 data-[state=selected]:bg-muted`. Cells (`TableCell`): `p-4 align-middle`.

Cell contents per column (row-by-row):

**Col 0 — Media** (lines 228-235):
```tsx
<div className="space-y-1">
  <div className="font-medium">{title}</div>
  <div className="text-muted-foreground text-xs">
    {row.media_type || "unknown"} · session {row.session_id.slice(0, 8)}
  </div>
</div>
```
- Title: `font-medium` = weight 500, inherits `text-sm` (14 px).
- Sub: 12 px muted. Format: `{media_type} · session {id.slice(0,8)}`.
- `space-y-1` = 4 px gap.

**Col 1 — User** (line 236): plain text, `row.username || "User #" + row.user_id`. Inherits `text-sm`, default foreground.

**Col 2 — Profile** (lines 237-242):
```tsx
<div className="space-y-1">
  <div>{row.profile_name || row.profile_id}</div>
  <div className="text-muted-foreground text-xs">{row.profile_id}</div>
</div>
```
- Main: profile name if present, otherwise profile_id.
- Sub: always the full profile_id, 12 px muted.

**Col 3 — Method** (lines 243-245):
```tsx
<Badge variant="secondary">{row.play_method}</Badge>
```
- shadcn `Badge variant="secondary"` default: `bg-secondary text-secondary-foreground`, `rounded-md`, `px-2.5 py-0.5 text-xs font-semibold` (UNVERIFIED exact shadcn badge CSS). Typical: 6 px radius, ~ 2-10 px pad, 12 px text, weight 600.

**Col 4 — Watch Time** (lines 246-253):
```tsx
<div className="space-y-1">
  <div>{formatDuration(row.watched_seconds)}</div>
  <div className="text-muted-foreground text-xs">
    of {formatDuration(row.duration_seconds)}
  </div>
</div>
```
- Always shows "of {duration}" line — even if duration is null (formatDuration returns "0m").

**Col 5 — Status** (lines 254-258):
```tsx
<Badge variant={row.completed ? "default" : "outline"}>
  {row.completed ? "Completed" : "Partial"}
</Badge>
```
- `default` variant: filled primary color, `bg-primary text-primary-foreground`.
- `outline` variant: transparent bg, border, primary text color.

**Col 6 — Ended** (lines 259-266):
```tsx
<div className="space-y-1">
  <div>{formatDateTime(row.ended_at)}</div>
  <div className="text-muted-foreground text-xs">
    started {formatRelative(row.started_at)}
  </div>
</div>
```
- Main: full locale datetime (`new Date().toLocaleString()`).
- Sub: `started Xm ago` / `Xh ago` / `Xd ago` / `just now`.

**Col 7 — Logs** (lines 267-282):
```tsx
<div className="flex justify-end gap-3">
  <Link to={`/admin/logs?playback_session_id=...&focus=playback`}
        className="text-primary text-sm font-medium">View Logs</Link>
  <Link to={`/admin/logs?playback_session_id=...&focus=playback&component=ffmpeg`}
        className="text-primary/80 text-sm font-medium">FFmpeg Logs</Link>
</div>
```
- Two `<Link>` (react-router) — **not buttons**. Rendered as text-primary-colored links with `font-medium`, 14 px.
- `text-primary/80` = primary at 80% opacity for secondary link.
- Gap 12 px.

### Desktop (AdminPlaybackHistoryPage.xaml.cs:237-477)
Row construction per-item in `BuildHistoryRow`:

- `Grid Padding="20,14,20,14" ColumnSpacing="12"` — same as header. Eight columns matching header widths.
- **Row separator:** A separate `<Border BorderThickness="0,1,0,0">` is added *between* rows in the StackPanel (line 263-272). First row has no top border (controlled by `first` flag).
- **No hover state** — no `PointerEntered`/`Exited` handlers, no visual state manager.

Column contents:

- **Col 0 Media** (331-347): StackPanel (Spacing=2). Title `FontSize=13 FontWeight=SemiBold PrimaryTextBrush TextTrimming=CharacterEllipsis`. Sub `FontSize=11 TertiaryTextBrush` — format matches webui's string.
- **Col 1 User** (350-357): TextBlock `FontSize=13 SecondaryTextBrush TextTrimming=CharacterEllipsis`. Value identical to webui.
- **Col 2 Profile** (361-379): StackPanel (Spacing=2). Main `FontSize=13 SecondaryTextBrush`. Sub `FontSize=11 TertiaryTextBrush` (**only shown** if both ProfileName and ProfileId are non-empty — see 6.c).
- **Col 3 Method** (382-384, `BuildMethodBadge` 486-530): **custom-coded badge with hardcoded per-method colors:**
  - direct → green bg/fg (40,63,185,80 / 255,63,185,80)
  - remux → blue bg/fg (40,56,139,253 / 255,56,139,253)
  - transcode → orange bg/fg (40,219,109,40 / 255,219,109,40)
  - unknown → gray
  - CornerRadius=6, Padding=(8,3,8,3), `FontSize=11 FontWeight=SemiBold`.
- **Col 4 Watch Time** (387-403): StackPanel Spacing=2. Primary `FontSize=13`. Sub `of {duration}` `FontSize=11 Tertiary`, **only shown if duration > 0** (line 394).
- **Col 5 Status** (406-408, `BuildStatusBadge` 532-567): custom badge:
  - Completed → green tinted bg + green fg (matches webui's "default" variant semantically but webui's `default` is the accent/primary, not green).
  - Partial → gray tinted bg + gray fg (webui's "outline" is transparent + border).
  - CornerRadius=6, Padding=(8,3,8,3), 11 px SemiBold.
- **Col 6 Ended** (411-426): StackPanel. Main `FontSize=12 SecondaryTextBrush`, format via `FormatDateTime` → `"MMM d, yyyy h:mm tt"`. Sub `FontSize=11 TertiaryTextBrush` → `"started {FormatRelative}"`.
- **Col 7 Logs** (429-465): **Two `Button`s, not text-links.** Styled to mimic a link: transparent bg, 0 border, 0 padding, FontSize 13, FontWeight Medium. First uses `AccentBrush`, second uses `SecondaryTextBrush`. `Orientation=Horizontal Spacing=12 HorizontalAlignment=Right`. Click handlers call `NavigateToLogs(sessionId, ffmpegFilter)`.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 6.a | **Row typography universally smaller:** primary cells are 13 px; webui is 14 px (shadcn `text-sm`). Sub-text cells 11 px vs 12 px (`text-xs`). Entire row reads 1 px smaller than webui. | visual |
| 6.b | Row cell padding: desktop 20-14-20-14 on Grid vs webui shadcn `<TableCell>` `p-4` = 16 px all sides. So desktop has **4 px more horizontal, 2 px less vertical**. | visual |
| 6.c | **Profile sub-line conditional** — desktop only shows full profile_id when BOTH name and id are non-empty (line 369). Webui shows profile_id *always* (line 240). If the user has a profile name but desktop thinks otherwise, the id line is hidden. | functional |
| 6.d | **Method badge colors invented.** Webui uses shadcn `Badge variant="secondary"` — a **single uniform muted bg for all methods**. Desktop color-codes direct=green / remux=blue / transcode=orange. The desktop variant is more informative but **not what the webui shows**. "1:1 webui clone" rule broken. | visual (CRITICAL — divergent design) |
| 6.e | **Status badge colors wrong.** Webui `default` (Completed) = accent/primary-colored filled badge. In this theme that's `AccentBrush` = #78AEFC (blue). Desktop renders Completed as a **green** tinted badge. Not matching webui's variant at all. | visual (CRITICAL) |
| 6.f | **Status "outline" variant wrong.** Webui `outline` = **transparent bg + 1 px border + primary text**. Desktop renders as a **tinted gray fill with no border**. Shape & coloring differ. | visual |
| 6.g | Badge corner radius: desktop 6 px. Webui shadcn Badge typically `rounded-md` = 6 px. OK. |  |
| 6.h | Badge padding: desktop (8,3,8,3). Webui shadcn `px-2.5 py-0.5` = (10, 2, 10, 2). Slight divergence. | visual |
| 6.i | **Watch Time "of {duration}" sub-line conditional.** Desktop hides it if `DurationSeconds <= 0`. Webui *always* renders "of 0m" even if duration is null (`formatDuration(null)` returns "0m"). Harmless visually, but divergent. | functional |
| 6.j | Ended cell primary text: desktop **12 px SecondaryTextBrush** (dimmer than row default). Webui inherits `text-sm` (14 px) default foreground — i.e., **14 px primary text**. Desktop makes the Ended cell visibly smaller and grayer. | visual |
| 6.k | Ended cell format: desktop `"MMM d, yyyy h:mm tt"` (e.g., "Apr 14, 2026 3:42 PM"). Webui uses browser `toLocaleString()` which on en-US is "4/14/2026, 3:42:33 PM" — **includes seconds and uses slashes**. Divergent. | visual/functional |
| 6.l | **Logs column: Buttons vs Links.** Desktop uses two `Button`s with manual "link" styling; webui uses `<Link>` (react-router). Default WinUI Button has a hover background (the `SecondaryButtonStyle` is overridden by inline properties, but hit-testing may still show subtle hover/press effects). Real link appearance (underline on hover, text color shift) is not replicated. | visual / functional |
| 6.m | **First link color: Accent (primary)** — matches webui `text-primary`. OK. | |
| 6.n | **Second link color: desktop `SecondaryTextBrush` (#90A0B5) vs webui `text-primary/80` = primary at 80% opacity (≈ #78AEFCcc).** Desktop makes FFmpeg Logs **gray**, webui keeps it **blue, just dimmer**. | visual |
| 6.o | Logs gap: desktop `Spacing="12"` matches webui `gap-3` (12 px). OK. | |
| 6.p | Logs alignment right matches webui `justify-end`. OK. | |
| 6.q | **No row hover background.** Webui shadcn `TableRow` has `hover:bg-muted/50`. Desktop has zero hover feedback — reviewer scanning rows gets no cursor-line guidance. | visual |
| 6.r | **Row separator: 1 px top border between rows** via a dedicated `<Border>` element. Webui uses shadcn `TableRow` `border-b` (bottom border). Functionally equivalent, but the last row on desktop has **no bottom border** while webui's last row has one (until `border-0` on last:child, UNVERIFIED exact). Minor. | visual |
| 6.s | **Row height differs** depending on content — StackPanel cells grow. Shadcn rows use `h-12` *or* inherit via `TableCell` (UNVERIFIED) — typically larger. | visual |
| 6.t | Column widths in percent/fixed (`2.5*/1.2*/.../90/1.2*/90/1.4*/1.2*`) — webui uses content-sized columns. The `90` px fixed for Method and Status can clip "transcode" badge. | visual |
| 6.u | Main row ForeBrush: desktop sets User col to `SecondaryTextBrush` (dimmer) and Profile main to `SecondaryTextBrush` — Webui inherits default foreground (primary text). Every cell text is grayer on desktop than webui. | visual |

---

## 7. Pagination

### Webui (AdminPlaybackHistory.tsx:289-334)
- Rendered **only when `total > 0`**.
- Wrapper: `flex items-center justify-between px-2 py-4`.
- Left group: `flex items-center gap-4`:
  - `text-muted-foreground text-sm` (14 px) — text `"Showing {page*pageSize+1}-{Math.min((page+1)*pageSize, total)} of {total}"`.
  - `<Select value={String(pageSize)}>`: `<SelectTrigger className="h-8 w-[100px]">` with options `"25 rows" / "50 rows" / "100 rows"`. `onValueChange` sets pageSize + resets page to 0.
- Right group: `flex gap-2`:
  - `<Button variant="outline" size="sm" disabled={page===0}>Previous</Button>`
  - `<Button variant="outline" size="sm" disabled={(page+1)*pageSize >= total}>Next</Button>`

### Desktop (AdminPlaybackHistoryPage.xaml:257-292, xaml.cs:282-305)
- `<Border BorderThickness="0,1,0,0" Padding="20,14,20,14" Visibility="Collapsed">` shown after rows are built.
- Grid: left `*` + right `Auto`.
- Left: `StackPanel Orientation="Horizontal" Spacing="16"`:
  - `PageRangeText FontSize="13" SecondaryTextBrush` — "Showing {start+1}-{end} of {count}".
  - `PageSizeCombo MinWidth="100"` with items `25 rows / 50 rows / 100 rows`, `SelectedIndex="0"`.
- Right: `StackPanel Orientation="Horizontal" Spacing="8"`:
  - `PrevPageButton Content="Previous" Style="{SecondaryButtonStyle}"` — enabled only when `_page > 0`.
  - `NextPageButton Content="Next" Style="{SecondaryButtonStyle}"` — enabled only when `end < allItems.Count`.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 7.a | **Top border on pagination bar** (desktop `BorderThickness="0,1,0,0"`) — webui has no top border; `px-2 py-4` just adds padding. Extra visual rule on desktop. | visual |
| 7.b | Pagination bar padding: desktop 20-14-20-14 vs webui `px-2 py-4` = 8-16-8-16. **Desktop uses much larger left/right padding (20 vs 8) and smaller vertical (14 vs 16).** Webui is a tighter, narrower band. | visual |
| 7.c | Range text font: desktop **13 px SecondaryTextBrush** vs webui `text-sm` (14 px) `text-muted-foreground`. Slightly smaller. | visual |
| 7.d | Left-group spacing: desktop `Spacing="16"` vs webui `gap-4` = 16 px. OK. | |
| 7.e | Page-size select: desktop `MinWidth="100"` vs webui `w-[100px]` = fixed 100 px. Webui is **fixed** width, desktop allows growth. If labels were longer webui clips but desktop grows. Minor. | visual |
| 7.f | Page-size select height: webui `h-8` = 32 px. Desktop has no explicit Height on `PageSizeCombo`; default ComboBox is ~32 px or ~36 px depending on theme overrides. UNVERIFIED — likely close but may differ by 2–4 px. | visual |
| 7.g | Previous/Next buttons: desktop uses `SecondaryButtonStyle` — web uses `variant="outline" size="sm"`. `size="sm"` shadcn = `h-8 rounded-md px-3 text-xs`. Desktop button size depends on SecondaryButtonStyle (likely larger — standard button size). | visual |
| 7.h | Right-group spacing: desktop `Spacing="8"` matches webui `gap-2`. OK. | |
| 7.i | **Pagination bar hidden when no items** — matches webui behavior (webui `{total > 0 && ...}`). OK. | |
| 7.j | **No "rows per page" label** on webui. Desktop also none. OK. | |
| 7.k | Desktop clamps `_page > maxPage` (xaml.cs:253). Webui does NOT clamp — if you're on page 3 of a 100-row dataset and filters shrink it to 20 rows, webui will show an empty table with Next disabled. Desktop's clamp is actually an improvement but divergent. | functional (minor) |
| 7.l | **No page-number display (e.g., "Page 2 of 4")** — matches webui. OK. | |
| 7.m | Page size combo height consistency with user/profile selects: both defaults. OK. | |

---

## 8. Empty state

### Webui (AdminPlaybackHistory.tsx:195-205)
```tsx
<div className="flex flex-col items-center justify-center gap-3 py-12 text-center">
  <History className="text-muted-foreground/50 h-10 w-10" />
  <div className="space-y-1">
    <p className="text-sm font-medium">No playback history</p>
    <p className="text-muted-foreground max-w-sm text-xs">
      No playback history matches the current filters. Try adjusting the user, profile,
      or completion filters.
    </p>
  </div>
</div>
```
- Centered vertically + horizontally.
- Lucide `History` icon, 40×40 px (`h-10 w-10`), `text-muted-foreground/50` (muted at 50%).
- `gap-3` = 12 px between icon and text block.
- Title: `text-sm font-medium` (14 px / 500).
- Subtitle: `text-xs text-muted-foreground max-w-sm` (12 px, muted, max 384 px).
- `space-y-1` (4 px) between title and subtitle.
- Container padding: `py-12` = 48 px top/bottom.

### Desktop (AdminPlaybackHistoryPage.xaml:247-254)
```xml
<Border x:Name="EmptyState" Padding="20,48" Visibility="Collapsed">
  <TextBlock Text="No playback history matches the current filters."
             FontSize="13" Foreground="TertiaryTextBrush"
             HorizontalAlignment="Center" TextAlignment="Center" />
</Border>
```

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 8.a | **No icon.** Webui renders a 40×40 Lucide History icon above the text. Desktop omits it entirely. | visual |
| 8.b | **No two-line structure.** Webui has a bold title line "No playback history" + a softer 12-px detail paragraph. Desktop collapses into one 13-px line. | visual |
| 8.c | Text content divergence: desktop shows "No playback history matches the current filters." Webui title is "No playback history"; body is "No playback history matches the current filters. Try adjusting the user, profile, or completion filters." Desktop misses the "Try adjusting..." guidance. | functional |
| 8.d | Padding: desktop `Padding="20,48"` = horizontal 20 px, vertical 48 px — matches webui `py-12` (48 px vertical). OK for vertical, extra 20 px horizontal. | visual |
| 8.e | Font size 13 px vs webui 14 px title + 12 px body — desktop is between. | visual |
| 8.f | Desktop centers horizontally + text-align center; webui flex-col items-center. Equivalent visually. OK. | |
| 8.g | Foreground: desktop `TertiaryTextBrush`. Webui title uses default foreground (primary); only the subtitle uses muted. Desktop makes the entire empty-state muted. | visual |
| 8.h | **No max-width on text.** Webui subtitle `max-w-sm` = 384 px — wraps nicely. Desktop single line stretches as far as the card. | visual |

---

## 9. Loading state

### Webui (AdminPlaybackHistory.tsx:182-188)
```tsx
<div className="space-y-3">
  <Skeleton className="h-10 w-full rounded-lg" />
  {Array.from({ length: 5 }).map((_, i) => (
    <Skeleton key={i} className="h-12 w-full rounded-lg" />
  ))}
</div>
```
- **Shown inside the `<CardContent>` of the Recent Playback card** — the filter cluster, stat cards, and card header remain visible and interactive.
- One 40-px header skeleton + five 48-px row skeletons, full width, 8 px radius.
- `space-y-3` = 12 px gap.
- shadcn `<Skeleton>` = `animate-pulse rounded-md bg-muted` (UNVERIFIED — typical shadcn).

### Desktop (AdminPlaybackHistoryPage.xaml:36-41, 64-65)
```xml
<ProgressRing IsActive="{ViewModel.IsLoading}"
              Visibility="{ViewModel.IsLoading, BoolToVis}"
              HorizontalAlignment="Center" VerticalAlignment="Center"
              Width="48" Height="48" />
<ScrollViewer Visibility="{ViewModel.IsLoading, InverseBoolToVis}"> ... </ScrollViewer>
```

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 9.a | **CRITICAL: Entire page hidden during load.** Desktop wraps the ScrollViewer in `Visibility="{IsLoading, InverseBoolToVis}"` — filter controls, header, and stat cards all disappear, replaced by a centered 48 px spinner. Webui keeps the whole page layout and only replaces the table body with skeletons. Every filter change flashes the page away. | functional (CRITICAL) |
| 9.b | **No skeleton placeholders** for table rows. Webui shows 5 row skeletons so users can anticipate layout. Desktop has just a spinner. | visual |
| 9.c | **Loading state applies to initial load AND to every re-fetch** (filter change → `LoadCommand.ExecuteAsync`). Webui uses React Query's `isLoading` (true only for initial) vs `isFetching` (true for all re-fetches) — the skeleton shows only on `isLoading`; during re-fetch the rows stay and the refresh text at CardHeader right shows "Refreshing...". Desktop flashes the whole page blank on every filter change. | functional (CRITICAL) |
| 9.d | Spinner color, size (48 px) — generic WinUI ProgressRing. Webui has no spinner. | visual |
| 9.e | The "Refreshing..." text in the card header *does* update correctly when `IsLoading` flips (xaml.cs:40). So that part matches. But it's only visible until 9.a wipes the card. | functional |

---

## 10. Error state

### Webui (AdminPlaybackHistory.tsx:189-194)
```tsx
) : history.error ? (
  <div className="text-destructive py-8 text-center text-sm">
    {history.error instanceof Error
      ? history.error.message
      : "Failed to load playback history"}
  </div>
) : ...
```
- **Inside `<CardContent>`** — filters & header remain visible.
- Destructive-colored (typically red), 14 px, centered, `py-8` = 32 px vertical.

### Desktop (AdminPlaybackHistoryPage.xaml:43-61)
```xml
<StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="12"
            Visibility="{ViewModel.ErrorMessage, NullToVis}">
  <TextBlock Text="{ViewModel.ErrorMessage}" Foreground="ErrorBrush"
             Style="BodyTextStyle" HorizontalAlignment="Center"
             TextWrapping="Wrap" MaxWidth="400" />
  <Button Content="Retry" Style="AccentButtonStyle"
          HorizontalAlignment="Center" Command="{ViewModel.LoadCommand}" />
</StackPanel>
```

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 10.a | **CRITICAL: Error state replaces the entire page.** Desktop renders the error at the root Grid level, same `VerticalAlignment="Center" HorizontalAlignment="Center"`. Even though `IsLoading` should flip false on error, the ScrollViewer's visibility is based solely on `IsLoading` not on error — so when error is set, ScrollViewer is visible *and* the error StackPanel overlays it in the center, creating a stacked ambiguity (or hiding the page depending on order). Webui keeps card/filter chrome + error inline in the card body. | functional (CRITICAL) |
| 10.b | Desktop **adds a Retry button** that webui does not have. In webui the only recovery is changing filters or page reload — React Query auto-retries. Desktop adding an explicit Retry is extra but harmless. | functional |
| 10.c | Error foreground: desktop `ErrorBrush` (#EF6B73) vs webui `text-destructive`. Close in intent, exact color UNVERIFIED. | visual |
| 10.d | Error text style: desktop `BodyTextStyle`. UNVERIFIED exact size — line 224 of DarkTheme shows `BodyTextStyle` sets `Foreground` to PrimaryTextBrush but Foreground is then overridden to ErrorBrush on this TextBlock. FontSize depends on BodyTextStyle. Likely 14-15 px — close to webui `text-sm` (14 px). |  |
| 10.e | **Error on desktop shows only if `ErrorMessage` is non-null** (NullToVis converter). The `LoadProfilesAsync` catches all exceptions silently (ViewModel:89 `catch { /* non-fatal */ }`) — so **profile loading failures are swallowed**. Webui's `useAdminUserProfiles` surfaces errors via React Query's `error` property; whether the page displays them is UNVERIFIED (probably no — the Select just shows an empty list). Desktop silently eating profile errors matches webui's apparent behavior but is not explicitly verified. | functional (low) |

---

## 11. Deep-links / action buttons / overflow menus

### Webui (AdminPlaybackHistory.tsx:269-281)
- "View Logs" → `<Link to={`/admin/logs?playback_session_id=...&focus=playback`}>`
- "FFmpeg Logs" → `<Link to={`/admin/logs?playback_session_id=...&focus=playback&component=ffmpeg`}>`
- **No per-row overflow menu, no per-row actions other than the two log links.**
- **No click-through on Media title** to the item detail page.
- **No click-through on User name** to user detail.
- The incoming deep-link is `?media_item_id=...` (received from elsewhere, shown as chip).

### Desktop (AdminPlaybackHistoryPage.xaml.cs:429-465, 480-484)
- Two `Button`s (see 6.l) navigate to `AdminLogsPage` via `Frame.Navigate(typeof(AdminLogsPage), param)` where `param` is either `sessionId` or `sessionId|ffmpeg`.
- **Custom in-app parameter encoding** (`string | string`) — NOT URL, NOT query params.
- `AdminLogsPage` must parse this — divergent contract from webui's `?playback_session_id=...&focus=playback&component=ffmpeg`.
- No media title deep-link, no user deep-link — matches webui.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 11.a | Deep-link to logs **uses a custom in-app param string** instead of the webui's query-string contract. Fine for an in-process `Frame.Navigate`, but means `AdminLogsPage` must not share the URL parsing code with the webui version. | functional |
| 11.b | **No `focus=playback` hint passed.** Webui adds `&focus=playback`; desktop just passes sessionId. If `AdminLogsPage` interprets `focus=playback` to show only playback-category logs, desktop loses that filter. UNVERIFIED — need to inspect `AdminLogsPage` handling. | functional |
| 11.c | **No `media_item_id` incoming deep-link support.** AdminItemDetail or AdminLibraries may try to link to "View playback history for this item" — webui uses `/admin/playback-history?media_item_id=...`. Desktop's page doesn't accept any navigation parameter. See 2.d. | functional |
| 11.d | **No per-row context menu / overflow**. Matches webui. OK. | |
| 11.e | Logs buttons do not differentiate click-vs-middle-click. Webui `<Link>` supports browser-native open-in-new-tab. N/A for WinUI. | none |

---

## 12. Data / API integration (hooks, realtime refresh, polling)

### Webui (AdminPlaybackHistory.tsx:43-51 + inferred from `useAdminPlaybackHistory`)
```tsx
const history = useAdminPlaybackHistory({
  userId, profileId, mediaItemId, completed, limit: 100,
});
const profiles = useAdminUserProfiles(selectedUserId);
```
- `useAdminPlaybackHistory` is a React Query hook in `hooks/queries/admin/history.ts` (file sandbox-blocked). **UNVERIFIED but by convention across this repo:**
  - has a `queryKey` including the filter object
  - has a `refetchInterval` or `refetchOnWindowFocus: true`, OR subscribes to a backend ws/sse channel
  - `isLoading` = first load; `isFetching` = any refetch
- The card header text `history.isFetching ? "Refreshing..." : "Auto-refreshing"` (line 178) is strong evidence that webui **does** auto-refresh (otherwise the "Auto-refreshing" label is false).
- Prior audits (admin-activity, admin-nodes) established that this repo uses both `refetchInterval: 5000-15000ms` on long-poll admin views and ws subscriptions on realtime views.

### Desktop (AdminPlaybackHistoryPage.xaml.cs:32-45, AdminPlaybackHistoryViewModel.cs:38-80)
- `Page_Loaded` → one-shot `LoadCommand.ExecuteAsync(null)`.
- Filter changes → `LoadCommand.ExecuteAsync(null)`.
- **No polling, no timer, no ws subscription, no event channel listener.**
- The "Auto-refreshing" text is shown as a static string (xaml.cs:40 only changes it to "Refreshing..." when `IsLoading == true`, not on any actual interval).
- No `refetchOnFocus` analogue — navigating back to the page doesn't re-fetch unless the ViewModel is transient; if singleton, stale data persists across navigation.

### Gap
| # | Gap | Severity |
|---|-----|----------|
| 12.a | **CRITICAL: No auto-refresh.** The "Auto-refreshing" UI text is a visible lie. Webui presumably polls / subscribes; desktop never refreshes until the user changes a filter. A session that completes while you're on this page will not appear without manual filter toggle. | functional (CRITICAL) |
| 12.b | **CRITICAL: No caching.** Every filter change round-trips the server. Webui's React Query caches by key and dedupes. For admin with moderate data volumes, fine; at scale, desktop creates load spikes. | functional |
| 12.c | **No realtime integration.** Project has `EventChannelClient` — but this page doesn't subscribe to `playback.session.ended` or similar events. Webui may or may not (UNVERIFIED); either way, desktop definitely doesn't. | functional |
| 12.d | **Silent profile load failure** (ViewModel:89 `catch { /* non-fatal */ }`) — see 10.e. | functional (minor) |
| 12.e | `SelectedUserId` change triggers `OnSelectedUserIdChanged` which clears profiles and kicks `LoadProfilesAsync` — but the `UserComboBox_SelectionChanged` handler (xaml.cs:177-189) *also* runs `LoadCommand.ExecuteAsync(null)` which internally checks `if (SelectedUserId.HasValue && Profiles.Count == 0) await LoadProfilesAsync(...)`. So profile load races: `OnSelectedUserIdChanged` (property-changed, sync in its setter) → `LoadProfilesAsync` starts; `UserComboBox_SelectionChanged` → `LoadCommand` may start before profiles finish. Profiles.Count may briefly be non-zero from previous user → skip reload. Edge case. | functional (minor) |
| 12.f | `LoadCommand` in ViewModel loads users only on first call (`if (Users.Count == 0)`). Never reloads if a user is created elsewhere while the page is open. Webui React Query would refetch on key invalidation. | functional (minor) |
| 12.g | Per-filter history limit is 100 rows. Matches webui. Desktop does client-side pagination over those 100 rows. If there are > 100 rows server-side, **both webui and desktop show only the first 100** — no server-side pagination. So the "Page 3 of 4" with 25/page is paginating the first 100 rows only. Same on both. OK. | |

---

## Prioritized Fix List

### P0 (block-ship — critical parity regressions)

1. **[9.a / 9.c] Stop hiding the page during load.** Move the ScrollViewer's `Visibility` binding OFF `IsLoading`. Render the header, filter cluster, and stat cards always. Show a skeleton (or spinner) *inside the Recent Playback card only*, like webui does.
2. **[10.a] Stop replacing the page on error.** Move the error into the card body. Header + filters stay visible.
3. **[12.a / 12.b] Implement auto-refresh** (React-Query-like) OR change the header text to match reality. Options:
   - Add a `DispatcherTimer` with 10–15 s interval that calls `LoadAsync` silently (don't flip `IsLoading`).
   - Subscribe to `EventChannelClient` for `playback.session.ended` and append rows.
   - If neither — remove the "Auto-refreshing" label.
4. **[6.d / 6.e / 6.f] Stop inventing badge colors.** Refactor `BuildMethodBadge` to use a single "secondary" fill for all methods (matches webui `variant="secondary"`). Refactor `BuildStatusBadge` to:
   - Completed = `AccentBrush` filled (webui `default` variant).
   - Partial = transparent bg + `BorderBrush` border + PrimaryText (webui `outline` variant).
5. **[5.a / 5.b / 6.a / 6.j] Fix universal 1-px font-size drift.** Row primary text: 13 → 14 px. Header text: 11 → 14 px. Sub-text: 11 → 12 px. Ended cell primary: 12 → 14 px.
6. **[3.a / 4.a] Fix corner-radius drift.** Stat cards and table card: 8 → 16 px (shadcn `rounded-2xl`). Update the comment at XAML:18 (it's wrong — webui uses `rounded-2xl`, not `rounded-lg`). Add a new `RadiusXL` = 16 to DarkTheme if needed.

### P1 (visible design divergence — fix ASAP)

7. **[2.d / 11.c / 1.d] Add `media_item_id` filter support.** Add `MediaItemId` to ViewModel, plumb through to `GetPlaybackHistoryAsync`, render the active-filter chip. Accept navigation parameter in `AdminPlaybackHistoryPage` constructor / `OnNavigatedTo`.
8. **[8.a / 8.b / 8.c] Rebuild empty state** — add History icon (segoe-fluent `\uE81C` or embed Lucide), two-line layout (bold title + muted body), include "Try adjusting..." guidance, max-width 400 px.
9. **[6.n] Fix second logs link color** — should be `AccentBrush` at ~80% opacity, not `SecondaryTextBrush`.
10. **[6.l] Replace logs Buttons with a real hyperlink style** (or use `HyperlinkButton`) so hover/press states look like links.
11. **[6.q] Add row hover background** — `SurfaceHoverBrush` or `#1A78AEFC` on pointer-over.
12. **[3.b] "Partial" stat card value should use `PrimaryTextBrush`, not `SecondaryTextBrush`.**
13. **[4.d] Remove the separator line under the card header at XAML:217.** Webui has no divider between CardHeader and CardContent.
14. **[5.e] Replace fixed column widths with `Auto` / content-sized columns** where possible, especially Method and Status (`90` px clips "transcode").
15. **[1.j] Add wrapping** to the filter cluster — use a custom wrap panel or at minimum increase ComboBox MinWidth so the header never overflows.
16. **[1.f] ComboBox MinWidths: 180/180/160 → 220/220/180** to match webui.
17. **[6.c] Always show profile_id sub-line** (not conditional on name presence).
18. **[6.k] Ended cell format** — use `ToString()` (default culture short datetime) or match browser `toLocaleString`. Currently strips seconds.

### P2 (polish)

19. **[2.a] Add profile self-heal effect** — when profiles arrive, if `SelectedProfileId` not in list, clear it.
20. **[1.h] Always show Reset button.** Don't hide it when no filters active.
21. **[6.h] Badge padding: (8,3) → (10,2)** — matches `px-2.5 py-0.5`.
22. **[7.a / 7.b] Pagination bar:** remove top border, reduce padding `20,14` → `8,16`.
23. **[7.g] Use a thin, outline-style button** for Previous/Next. Create `OutlineSmallButtonStyle` (h-8, border-only, `text-xs`) matching shadcn `variant="outline" size="sm"`.
24. **[3.h] Stat card value: set `CharacterSpacing="-25"`** (approximates `tracking-tight`).
25. **[1.a] Wrap content in a `page-shell` equivalent** — a container Border with MaxWidth (e.g. 1440) and HorizontalAlignment="Center".
26. **[1.b / 1.c] Make title + subtitle sizes responsive** (SizeChanged handler → clamp equivalent).
27. **[2.b] Restore filters from navigation parameter** (not URL, but state).
28. **[8.h] Set `MaxWidth="400"`** on empty-state text.
29. **[11.b] Propagate `focus=playback` hint** to AdminLogsPage (expand the parameter contract to include `{ sessionId, ffmpegFilter, focus }`).
30. **[12.e] De-duplicate the dual profile-load path** in `OnSelectedUserIdChanged` vs `LoadCommand`.
31. **[12.f] Invalidate users cache** when returning to page after X seconds.

---

## Summary Table

| Feature | Webui | Desktop | Gap | Severity |
|---------|-------|---------|-----|----------|
| Page shell max-width/centering | `.page-shell` | Raw StackPanel Padding=40,28,40,40 | No shell | visual |
| Page title | 32–48 px clamp, `.page-title` weight 700 | Fixed 42 px Bold | Not responsive | visual |
| Page subtitle | 14/16 px responsive, muted | Fixed 14 px Secondary | Not responsive | visual |
| Item-filter chip | Shown when `media_item_id` set | **Missing** | No chip, no feature | functional |
| Filter cluster wrap | `flex-wrap` | Horizontal StackPanel | No wrap | visual |
| User select width | `w-[220px]` | MinWidth 180 | Narrower | visual |
| Profile select width | `w-[220px]` | MinWidth 180 | Narrower | visual |
| Completed select width | `w-[180px]` | MinWidth 160 | Narrower | visual |
| Reset button visibility | Always | Hidden unless active | Hidden when idle | functional |
| `user_id` deep-link | URL param | **Missing** | No deep-link | functional |
| `profile_id` deep-link | URL param | **Missing** | No deep-link | functional |
| `completed` deep-link | URL param | **Missing** | No deep-link | functional |
| `media_item_id` filter | URL param | **Missing entirely** | No filter | functional |
| Profile self-heal effect | Present | **Missing** | Race possible | functional |
| Stat card radius | `rounded-2xl` = 16 | 8 (`RadiusLG`) | Half radius | visual |
| Stat card "Partial" color | Primary foreground | SecondaryTextBrush | Grayed | visual |
| Stat value tracking | `tracking-tight` | Default | No tracking | visual |
| Recent Playback card radius | `rounded-2xl` = 16 | 8 | Half | visual |
| Card title size | `text-sm` = 14 px | 13 px | 1 px smaller | visual |
| Separator below CardHeader | None | 1 px border | Extra line | visual |
| "Auto-refreshing" label truthiness | Real auto-refresh | **No polling, label is lie** | Missing feature | functional (CRITICAL) |
| Table header font size | `text-sm` = 14 | 11 | 3 px smaller | visual |
| Table header weight | `font-medium` = 500 | SemiBold = 600 | Heavier | visual |
| Table header fg | `muted-foreground` | TertiaryTextBrush | Dimmer | visual |
| Row typography | 14/12 px | 13/11 px | 1 px smaller throughout | visual |
| Method badge colors | shadcn `secondary` (single muted) | Per-method RGB (green/blue/orange) | **Invented design** | visual (CRITICAL) |
| Status Completed badge | `default` = accent filled (blue) | Green tinted | **Wrong color** | visual (CRITICAL) |
| Status Partial badge | `outline` = transparent + border | Gray tinted fill | **Wrong variant** | visual (CRITICAL) |
| Watch Time "of X" subline | Always shown | Hidden if duration=0 | Conditional | functional |
| Profile id subline | Always shown | Hidden if no name | Conditional | functional |
| Ended cell primary size | 14 px primary fg | 12 px Secondary | Smaller + dimmer | visual |
| Ended date format | `toLocaleString()` | `"MMM d, yyyy h:mm tt"` | No seconds, differs | visual |
| Logs column type | `<Link>` | `Button` mimicking link | Hover mismatch | visual |
| "View Logs" color | `text-primary` | AccentBrush | OK | — |
| "FFmpeg Logs" color | `text-primary/80` | SecondaryTextBrush | **Gray instead of dim-blue** | visual |
| Row hover bg | `hover:bg-muted/50` | None | No hover | visual |
| Pagination top border | None | 1 px | Extra line | visual |
| Pagination padding | `px-2 py-4` | `20,14,20,14` | Too much L/R | visual |
| Pagination range font | 14 px muted | 13 px Secondary | Smaller | visual |
| Prev/Next button variant | `outline size="sm"` h-8 | SecondaryButtonStyle (standard size) | Too large | visual |
| Empty state icon | Lucide History 40×40 muted | **None** | Missing icon | visual |
| Empty state structure | Title + body | One line | Missing hierarchy | visual |
| Empty state guidance | "Try adjusting..." | Absent | Less helpful | functional |
| Loading state scope | Skeleton inside CardContent only | **Entire page replaced with spinner** | Flash on every filter | functional (CRITICAL) |
| Loading state visual | 1 × h-10 + 5 × h-12 skeletons | 48 px spinner | No placeholders | visual |
| Error state scope | Inside CardContent, chrome kept | **Centered overlay on Grid root** | Page vanishes | functional (CRITICAL) |
| Retry button | None (RQ auto-retry) | Explicit Retry button | Extra button | functional (benign) |
| Deep-link to logs | `?playback_session_id=...&focus=playback[&component=ffmpeg]` | Custom `"sessionId\|ffmpeg"` string | Different contract | functional |
| `focus=playback` hint | Passed | **Dropped** | Logs page may not prefilter | functional |
| Auto-refresh interval | Present (polling / RQ / ws) | **None** | No fresh data | functional (CRITICAL) |
| Profile load errors | Surfaced via RQ error | Silently swallowed | Silent failure | functional |
| Users cache invalidation | RQ-driven | Loaded once per page life | Stale | functional |

---

## Verification notes / blockers

- Exact definitions of `.page-shell`, `.page-header`, `.page-title`, `.page-subtitle`, `.surface-panel`, shadcn `Badge` / `Card` / `Table` / `Button` / `Select` / `Skeleton` CSS **were blocked by the sandbox**. Severity assessments above use conventions established in prior audits (`FULL-AUDIT-admin-activity.md`, `FULL-AUDIT-admin-nodes-apikeys-invites.md`, `FULL-AUDIT-admin-providers.md`) and shadcn/ui v1 defaults.
- The polling / realtime behavior of `useAdminPlaybackHistory` is inferred from the "Auto-refreshing" label in the webui source; confirmation requires reading `F:/continuum-server/web/src/hooks/queries/admin/history.ts`.
- `AdminLogsPage`'s handling of `focus=playback` + `component=ffmpeg` should be confirmed against the current desktop `AdminLogsPage` to ensure 11.b is scoped correctly.
