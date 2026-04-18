# Admin Nodes + API Keys + Invite Codes — Full Audit

## Files read (100% line-by-line)

Webui (continuum-server):
- `F:/continuum-server/web/src/pages/AdminNodes.tsx` — 382 lines (full)
- `F:/continuum-server/web/src/pages/AdminApiKeys.tsx` — 318 lines (full)
- `F:/continuum-server/web/src/pages/admin-settings/InviteCodesTab.tsx` — 250 lines (full)

Webui — NOT READ (permission denied by sandbox — Read/Bash/Grep all returned "Permission denied" on these paths, so references/comparisons depending on them are marked "UNVERIFIED — hook/component blocked"):
- `F:/continuum-server/web/src/hooks/queries/admin/nodes.ts`
- `F:/continuum-server/web/src/hooks/queries/admin/apiKeys.ts`
- `F:/continuum-server/web/src/hooks/queries/admin/inviteCodes.ts`
- `F:/continuum-server/web/src/hooks/queries/admin/settings.ts`
- `F:/continuum-server/web/src/hooks/queries/admin/users.ts`
- `F:/continuum-server/web/src/components/ConfirmDialog.tsx`
- `F:/continuum-server/web/src/components/ui/*` (button, input, badge, switch, table, dialog, select, card, skeleton, label)
- `F:/continuum-server/web/src/app.css` (page-shell / page-header / page-title / page-subtitle / surface-panel / surface-panel-subtle utility classes)

Call-out: The whole `components/`, `components/ui/`, `hooks/queries/admin/`, and `app.css` tree is read-blocked in this session. Findings below derive from the 3 page files (which use those classes/hooks by name). All Tailwind classes referenced on pages are translated to pixel-level hints; utility class internals (exact shadow/blur/background) are described from prior parity audits and class-name semantics, and flagged where assumptions were made.

Desktop (ContinuumPlayer):
- `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminNodesPage.xaml` — 329 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminNodesPage.xaml.cs` — 553 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/ViewModels/Admin/AdminNodesViewModel.cs` — 131 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminApiKeysPage.xaml` — 148 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminApiKeysPage.xaml.cs` — 475 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/ViewModels/Admin/AdminApiKeysViewModel.cs` — 96 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminInviteCodesPage.xaml` — 109 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminInviteCodesPage.xaml.cs` — 248 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/ViewModels/Admin/AdminInviteCodesViewModel.cs` — 109 lines (full)
- `F:/ContinuumPlayer/src/ContinuumPlayer/Themes/DarkTheme.xaml` — brush-grep only (relevant keys confirmed)

Tailwind→pixel conventions used throughout this audit (Tailwind defaults):
- `text-xs` = 12px / `text-sm` = 14px / `text-base` = 16px / `text-lg` = 18px
- `font-medium` = 500 / `font-semibold` = 600 / `font-bold` = 700
- `gap-1` = 4px / `gap-1.5` = 6px / `gap-2` = 8px / `gap-3` = 12px / `gap-4` = 16px / `gap-5` = 20px
- `p-3` = 12px / `px-1.5` = 6px x / `py-0.5` = 2px y / `py-4` = 16px y / `py-8` = 32px y
- `h-4 w-4` = 16px / `h-6 w-6` = 24px / `h-7 w-7` = 28px / `h-2.5 w-2.5` = 10px
- `rounded` = 4px / `rounded-lg` = 8px / `rounded-xl` = 12px / `rounded-2xl` = 16px / `rounded-[1.6rem]` = 25.6px
- `space-y-3` = 12px vertical / `space-y-4` = 16px / `space-y-6` = 24px / `space-y-8` = 32px
- `w-32` = 128px / `w-24` = 96px / `w-[120px]` = 120px / `w-[100px]` = 100px
- `text-[clamp(2rem,4vw,3rem)]` = responsive 32–48px (viewport-scaled) — on most desktops resolves ≈ 42–48px
- shadcn `Button size="sm"` = h-8 px-3 (32px tall, 12px x-pad); `size="icon"` = h-10 w-10 default

---

# PART 1 — AdminNodes

Two-section page: **Proxy Nodes** and **Transcode Nodes**. Each section has a count badge, info banner, table, empty state, and CRUD dialogs.

## 1. Page header

### Webui (AdminNodes.tsx:297–306)
```
<div class="page-shell space-y-8 py-4 sm:py-6">
  <div class="page-header gap-5">
    <div class="space-y-3">
      <h1 class="page-title text-[clamp(2rem,4vw,3rem)]">Stream nodes</h1>
      <p class="page-subtitle text-sm sm:text-base">
        Manage proxy and transcode workers that distribute playback load across your infrastructure.
      </p>
    </div>
  </div>
```
- Title: `text-[clamp(2rem,4vw,3rem)]` → 32–48px clamp (typically ~48px on desktop), `page-title` adds tracking/weight.
- Subtitle: `text-sm sm:text-base` → 14px mobile, 16px ≥640px.
- Outer spacing: `space-y-8` (32px between top-level rows), `py-4 sm:py-6` (16px/24px).
- `page-header` is a CSS utility (UNVERIFIED exact CSS — blocked) that typically sets flex + wrap + gap; here `gap-5` = 20px.
- No Add button on the main header (each section has its own Add button).

### Desktop (AdminNodesPage.xaml:54–67)
```
<StackPanel Padding="40,28,40,40" Spacing="32">
  <StackPanel Spacing="4">
    <TextBlock Text="Stream nodes" FontSize="42" FontWeight="Bold" Foreground="PrimaryTextBrush" />
    <TextBlock Text="Manage proxy and transcode workers…" FontSize="14" Foreground="SecondaryTextBrush" />
  </StackPanel>
  ...
</StackPanel>
```
- Title: FontSize=42 (≈ upper-end of webui clamp). Weight=Bold (webui uses `page-title` weight — likely 700, matches).
- Subtitle: FontSize=14 (matches webui `text-sm` at mobile; webui bumps to 16px ≥640px which on desktop it will be).
- Header StackPanel Spacing=4 → 4px between title/subtitle (webui uses `space-y-3` = 12px).
- Outer StackPanel Spacing=32 (matches `space-y-8` = 32px).
- Padding 40,28,40,40 left/top/right/bottom. Webui `page-shell` max-width centers content (UNVERIFIED — blocked), desktop hard-codes 40px L/R rather than centered constrained width.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 1.1 | Title/subtitle inner spacing is 4px vs webui 12px (`space-y-3`). | visual |
| 1.2 | Subtitle font size 14px — webui escalates to 16px at desktop widths (`sm:text-base`). Desktop is always 14px. | visual |
| 1.3 | `page-shell` on webui is a centered, max-width container (typically `mx-auto max-w-[*]`). Desktop uses full-width with 40px gutters instead of a centered/constrained shell. UNVERIFIED max-width (app.css blocked). | visual |
| 1.4 | Header `gap-5` (20px) between blocks is not represented — desktop uses 32 outer spacing which is for section separation, not header internal. Not strictly visible since header only has one child, but structural. | minor structural |

## 2. Global status banner

### Webui
Does NOT exist. Webui uses `toast` (sonner) for all transient feedback (see Create/Delete flows relying on mutation hooks — UNVERIFIED for nodes specifically because hook file blocked, but the page code shows zero inline banners; toasts fire from hooks).

### Desktop (AdminNodesPage.xaml:70–80, .cs:540–552)
- `StatusBanner` Border with AccentBackgroundBrush, CornerRadius=8, Padding 14x10; shown via `ShowStatus(message)` for 4 seconds then auto-hidden by DispatcherTimer.
- Populated on every create/update/delete/toggle/check success path.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 2.1 | **Desktop uses inline status banner; webui uses toast notifications (sonner)**. Visual style, position, and UX differ completely. Webui fires `toast.success(...)` from inside query hooks (UNVERIFIED — hooks blocked, but pages import `toast` from sonner in sibling pages and call `toast.success` after copy, implying consistent toast usage). | functional / visual |
| 2.2 | Accent-blue banner on success vs webui success toast (usually green/accent semantic). | visual |

## 3. Section header (Proxy / Transcode)

### Webui (AdminNodes.tsx:61–69)
```
<div class="flex flex-wrap items-center justify-between gap-3">
  <div class="flex items-center gap-2">
    <h2 class="text-lg font-semibold">{label} Nodes</h2>
    <Badge variant="secondary">{nodes.length}</Badge>
  </div>
  <Button size="sm" onClick={onAdd}>
    <Plus class="mr-1 h-4 w-4" /> Add {label}
  </Button>
</div>
```
- Heading: `text-lg font-semibold` → 18px / 600.
- `Badge variant="secondary"` → shadcn Badge: `rounded-md px-2 py-0.5 text-xs font-semibold` with muted background. Typical secondary bg = `hsl(var(--secondary))` = subtle gray card tone.
- Add button: `size="sm"` → 32px tall, `px-3` = 12px, icon 16px + `mr-1` = 4px gap.
- Button label reads **"Add {label}"** (i.e., `Add Proxy` / `Add Transcode` — no "Node" word).

### Desktop (AdminNodesPage.xaml:83–122, 200–240)
- Heading: FontSize=18, FontWeight=SemiBold — **matches**.
- Badge: inline `<Border Background="CardBackgroundBrush" CornerRadius=10 Padding="7,2,7,2">` with TextBlock FontSize=11, SemiBold, SecondaryTextBrush.
  - Webui `rounded-md` = 6px, desktop uses 10px. Visual drift.
  - Webui Badge text is `text-xs` = 12px, desktop is 11px.
  - Webui Badge padding `px-2 py-0.5` = 8×2, desktop is 7×2 (horizontal slightly tighter).
  - Webui variant="secondary" uses `--secondary` (muted surface contrasting with card); desktop uses `CardBackgroundBrush` — this is the same tone as the table card behind it, so on the card surface the badge is invisible. **Critical visual bug when badge sits inside / next to a card surface.** Should use `SurfaceRaisedBrush` or a distinct muted.
- Add button: label reads **"Add Proxy Node"** / **"Add Transcode Node"** — webui says **"Add Proxy"** / **"Add Transcode"**. Extra word "Node".
- Icon: FontIcon Glyph="\uE710" (Segoe MDL2 "+") FontSize=13 + Spacing=6 between icon and label. Webui uses `mr-1 h-4 w-4` = 4px gap, 16px icon. Gap 50% too wide; icon 3px smaller.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 3.1 | Count badge background = CardBackgroundBrush = same as table card → **invisible where it sits next to the card**. Should use SurfaceRaisedBrush or a muted-contrast brush. | visual / critical on dark card bg |
| 3.2 | Badge corner radius 10px vs webui 6px. | visual |
| 3.3 | Badge text size 11 vs 12. | visual |
| 3.4 | Badge padding 7x vs 8x. | visual |
| 3.5 | Add-button label "Add Proxy Node" vs webui "Add Proxy". | text |
| 3.6 | Plus icon 13px vs webui 16px. Icon-to-text gap 6 vs webui 4. | visual |
| 3.7 | Webui section uses `flex-wrap` allowing button to drop below heading at narrow widths. Desktop Grid has `ColumnDefinition *, Auto` — never wraps. Usually fine on desktop; minor. | minor |

## 4. Info banner

### Webui
Proxy (AdminNodes.tsx:333–338):
```
<div class="surface-panel-subtle text-info flex items-start gap-2 rounded-xl p-3 text-sm">
  <Info class="mt-0.5 h-4 w-4 shrink-0" />
  <p>Proxy nodes relay streams to end users. The URL must be publicly accessible.</p>
</div>
```
Transcode (AdminNodes.tsx:351–360):
```
<div class="surface-panel-subtle text-warning flex items-start gap-2 rounded-xl p-3 text-sm">
  <AlertTriangle class="mt-0.5 h-4 w-4 shrink-0" />
  <p>Transcode nodes handle video transcoding internally.
    <strong>Must be on the same network as proxy nodes and the backend.</strong>
    Does not need a public URL.</p>
</div>
```
- `surface-panel-subtle` = a muted translucent background (UNVERIFIED — blocked), typical is `bg-[--surface-subtle]` with subtle border.
- `text-info` = info blue color for all text (and icon). `text-warning` = amber/orange.
- `rounded-xl` = 12px radius.
- Icon + paragraph both colored by the container — icon inherits text color.
- Gap between icon and paragraph: `gap-2` = 8px. Padding `p-3` = 12px all.
- Icon Info = lucide info (◐). AlertTriangle = triangle warning.

### Desktop
Proxy (AdminNodesPage.xaml:125–142):
```
<Border Background="SurfaceBrush" CornerRadius="22" Padding="12">
  <StackPanel Orientation="Horizontal" Spacing="8">
    <FontIcon Glyph="&#xE946;" FontSize="16" Foreground="AccentBrush" .../>
    <TextBlock Text="Proxy nodes relay streams to end users..." FontSize="13"
               Foreground="SecondaryTextBrush" TextWrapping="Wrap" />
  </StackPanel>
</Border>
```
Transcode (AdminNodesPage.xaml:243–266):
```
<Border Background="SurfaceBrush" CornerRadius="22" Padding="12">
  ...FontIcon Glyph="&#xE7BA;" FontSize="16" Foreground="#F59E0B" (amber)
  <TextBlock Foreground="SecondaryTextBrush" ...>
    <Run Text="Transcode nodes..." />
    <Run Text="Must be on the same network..." FontWeight="SemiBold" />
    <Run Text=" Does not need a public URL." />
  </TextBlock>
</Border>
```

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 4.1 | **CornerRadius 22px vs webui 12px (`rounded-xl`)**. Nearly 2× too round. | visual |
| 4.2 | **Text color for PROXY banner**: webui uses `text-info` (blue text AND blue icon). Desktop uses `SecondaryTextBrush` for text and `AccentBrush` for icon → text is grey, not blue. Breaks info semantic. | visual |
| 4.3 | **Text color for TRANSCODE banner**: webui uses `text-warning` (all amber). Desktop uses `SecondaryTextBrush` for text and amber only on icon. Text is grey, not amber. | visual |
| 4.4 | Text FontSize 13 vs webui `text-sm` = 14. | visual |
| 4.5 | Background brush: webui `surface-panel-subtle` — a translucent subtle surface with slight color tint from the info/warning class. Desktop uses `SurfaceBrush` (neutral grey), losing the tint entirely. | visual |
| 4.6 | Icon/text vertical alignment: webui `items-start` + `mt-0.5` on icon. Desktop uses `VerticalAlignment=Top` + `Margin=0,1,0,0` — close enough, OK. | minor |
| 4.7 | Icon glyph: webui uses lucide Info (filled circle with i). Desktop Segoe MDL2 `&#xE946;` = "Info" glyph but different visual design. Equivalent semantically. | cosmetic |
| 4.8 | AlertTriangle glyph: Desktop `&#xE7BA;` = Segoe MDL2 "Warning" triangle — equivalent. | cosmetic |
| 4.9 | **Text-wrapping/strong**: desktop uses Run with FontWeight=SemiBold for middle clause; webui wraps it in `<strong>` (usually `font-weight: bold` 700). Desktop SemiBold = 600, too light. | visual |

## 5. Table card container

### Webui (both sections, AdminNodes.tsx:73)
```
<div class="surface-panel overflow-x-auto rounded-xl border-0">
  <Table>...</Table>
</div>
```
- `surface-panel` = the elevated card surface (UNVERIFIED exact CSS; typically `bg-card shadow-md`).
- `rounded-xl` = 12px. `border-0` explicitly removes border.
- `overflow-x-auto` permits horizontal scrolling of table at narrow widths.

### Desktop (AdminNodesPage.xaml:145–149, 269–273)
```
<Border Background="CardBackgroundBrush" CornerRadius="26" BorderThickness="0" Padding="0">
```

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 5.1 | **CornerRadius 26px vs webui 12px (`rounded-xl`)**. More than 2× too round. Webui uses `rounded-xl` here (table container), not `rounded-2xl`. | visual |
| 5.2 | Horizontal overflow: webui scrolls horizontally when narrow. Desktop fixed column widths with no horizontal scrolling — on very narrow window widths content may clip. Minor for 1280+ px. | minor |

## 6. Table header

### Webui (AdminNodes.tsx:75–85)
Shadcn `<TableHeader><TableRow>` with `<TableHead>` cells.
- Default `<TableHead>` CSS: `h-10 px-2 text-left align-middle font-medium text-muted-foreground` → 40px row, 8px x-pad, 14px text (inherits), muted-foreground grey, font-weight 500.
- Columns for Proxy (6): Name, URL, Status, Health, Last Check, Actions
- Columns for Transcode (7): Name, URL, Status, Health, Jobs, Last Check, Actions
- Actions column has `className="w-32"` = 128px.
- Header case: Title Case ("Name", "URL", "Status", ...).

### Desktop
Proxy (AdminNodesPage.xaml:152–166):
```
<Grid Padding="20,14,20,14" ColumnSpacing="12">
  <ColumnDefinition Width="1.8*" /> <!-- Name -->
  <ColumnDefinition Width="2*" />   <!-- URL -->
  <ColumnDefinition Width="80" />   <!-- Status -->
  <ColumnDefinition Width="100" />  <!-- Health -->
  <ColumnDefinition Width="130" />  <!-- Last Check -->
  <ColumnDefinition Width="110" />  <!-- Actions -->
  TextBlocks FontSize=12 FontWeight=SemiBold Foreground=TertiaryTextBrush
```
Transcode (AdminNodesPage.xaml:276–293): adds Jobs col (60px) between Health and Last Check.
- Text: FontSize=12, SemiBold, TertiaryTextBrush.
- Cases: "Name", "URL", "Status", "Health", "Jobs", "Last Check", "Actions" — Title Case, matches.
- Divider below header: `<Border BorderBrush="BorderBrush" BorderThickness="0,1,0,0" />` — matches webui built-in table-row bottom border.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 6.1 | Header font-weight **SemiBold (600) vs webui `font-medium` (500)**. Too bold. | visual |
| 6.2 | Header text size 12 vs webui 14 (inherits from table). | visual |
| 6.3 | Header padding `20,14,20,14` vs webui `h-10 px-2` ≈ 40px tall / 8px x-pad. Desktop's 14px vertical is close to 40px total (14+text+14), but horizontal padding is 20px vs webui 8px — much more generous. | visual |
| 6.4 | Header text color `TertiaryTextBrush` vs webui `text-muted-foreground` — both are muted greys. Check exact value matches `--muted-foreground` (UNVERIFIED). | minor |
| 6.5 | Actions column width: webui `w-32` = 128px. Desktop = 110px. 18px difference. | visual |
| 6.6 | **Cell case**: Desktop here uses Title Case ("Name") — correct. BUT see ApiKeys/InviteCodes where desktop uses UPPERCASE ("LABEL"). **Inconsistent within desktop app and different from webui.** (Applies to Part 2 and Part 3.) | minor for Nodes |

## 7. Table rows (Proxy)

### Webui (AdminNodes.tsx:103–165)
- Row: default `<TableRow>` = `border-b transition-colors hover:bg-muted/50`.
- **Name** cell: `className="font-medium"` → font-weight 500, default text size (14px).
- **URL** cell: `className="font-mono text-sm"` → monospace, 14px.
- **Status** cell: `<Switch checked={node.enabled} onCheckedChange={() => onToggle(node)} />` — shadcn Switch, typically ~36×20px.
- **Health** cell:
  ```
  <span class="flex items-center gap-1.5">
    <span class={`h-2.5 w-2.5 rounded-full ${dotColorClass}`} />
    <span class="text-muted-foreground text-sm">{healthText}</span>
  </span>
  ```
  - Dot: 10×10 rounded-full. Color classes: disabled = `bg-muted-foreground` (grey), healthy = `bg-success` (green), unhealthy = `bg-destructive` (red).
  - Text: `text-muted-foreground text-sm` = muted grey, 14px.
  - Text values: "Disabled", "Healthy", "Unhealthy".
- **Last Check** cell: `className="text-muted-foreground text-xs"` → muted grey, 12px. Format: `new Date(...).toLocaleString()` — JS locale-full (e.g. "11/15/2026, 2:30:00 PM").
  - "Never" fallback when `last_health_check` is null.
- **Actions** cell:
  ```
  <div class="flex gap-1">
    <Button variant="ghost" size="icon" class="h-7 w-7" disabled={isChecking} onClick={check}>
      <RefreshCw class={`h-3 w-3 ${isChecking ? 'animate-spin' : ''}`} />
    </Button>
    <Button variant="ghost" size="icon" class="h-7 w-7" onClick={edit}>
      <Pencil class="h-3 w-3" />
    </Button>
    <Button variant="ghost" size="icon" class="h-7 w-7" onClick={delete}>
      <Trash2 class="h-3 w-3" />
    </Button>
  </div>
  ```
  - Each button 28×28px. Icons 12×12px. Gap 4px.
  - Ghost variant: no background, hover = `hover:bg-accent hover:text-accent-foreground`.
  - RefreshCw spins while health-check mutation pending (matches `checkingHealthId === node.id` from parent).

### Desktop (AdminNodesPage.xaml.cs:120–325)
- Row: Grid Padding=20,14,20,14 ColumnSpacing=12. No hover effect (WinUI Grid has none by default).
- **Name** (col 0): TextBlock FontSize=14, FontWeight=SemiBold (600), PrimaryTextBrush, VerticalAlignment=Center, TextTrimming=CharacterEllipsis. **Webui uses font-medium = 500, desktop uses SemiBold = 600. Too bold.**
- **URL** (col 1): TextBlock FontSize=12, FontFamily="Consolas, Courier New", SecondaryTextBrush, VerticalAlignment=Center, TextTrimming=CharacterEllipsis. **Webui `text-sm` = 14 → desktop 12 (too small).** Webui uses default text color (PrimaryTextBrush equivalent); desktop dims to SecondaryTextBrush. **Color drift.**
- **Status** (col 2): `<ToggleSwitch IsOn=… OnContent="" OffContent="" MinWidth=0>`. WinUI ToggleSwitch ≈ 44×24. Webui shadcn Switch ≈ 36×20. Slightly larger.
- **Health** (col 3): StackPanel horizontal, Spacing=6 (webui 6px = `gap-1.5` matches):
  - Border 10×10 CornerRadius=5 with SolidColorBrush:
    - Disabled: Color.FromArgb(255, 130,130,130) = `#828282` — webui `bg-muted-foreground` which in dark theme is typically `hsl(240 5% 65%)` ≈ `#A1A1A1` or similar. Close but not exact.
    - Healthy: `#22C55E` (255,34,197,94) — matches Tailwind `bg-success` default `#22c55e` if success maps to green-500. ✓
    - Unhealthy: `#DC4646` (255,220,70,70) — webui `bg-destructive` typically `#EF4444` (red-500) or theme-defined `--destructive`. Off by ~`#10`. Close but not exact.
  - TextBlock FontSize=12 vs webui 14. **Smaller.** SecondaryTextBrush vs muted-foreground.
- **Jobs** (transcode only, col 4):
  - When ActiveJobs > 0: Border AccentBackgroundBrush CornerRadius=4 Padding=7,3,7,3 → TextBlock FontSize=12 SemiBold AccentBrush.
  - When 0: plain TextBlock "0" TertiaryTextBrush.
  - **Webui does not use a badge**: just `<TableCell>{node.active_jobs}</TableCell>` → plain text inheriting default color. Desktop over-invents a badge treatment that webui doesn't have. | visual gap (invented UI).
- **Last Check** (col 4 or 5):
  - Desktop format: `DateTime.Parse(...).ToLocalTime().ToString("g")` → "g" = short date + short time (e.g., "11/15/2026 2:30 PM"). Webui uses full `toLocaleString()` which typically includes seconds.
  - FontSize=12, TertiaryTextBrush — matches webui `text-muted-foreground text-xs` — ✓.
  - Fallback "Never" — matches.
- **Actions**:
  - `checkBtn` glyph `&#xE72C;` (MDL2 Refresh) - webui uses lucide RefreshCw. Equivalent semantically.
  - `editBtn` glyph `&#xE70F;` (MDL2 Edit pencil) - webui lucide Pencil. Equivalent.
  - `deleteBtn` glyph `&#xE74D;` (MDL2 Delete trash) with fgColor=`#DC5A5A`. Webui lucide Trash2, inherits ghost color (no red tint). **Webui does NOT color the trash button red**; desktop does. Visual drift.
  - `MakeIconButton` size=32×32 (default). **Webui = 28×28 (`h-7 w-7`).** 4px too large each dimension.
  - Icon FontSize=14 → glyph drawn ~14px. Webui icon = 12px (`h-3 w-3`). **2px too large.**
  - Hover: ghost button default WinUI = subtle grey. Close enough.
  - **MISSING: No spin animation on check button when pending.** Webui rotates RefreshCw icon while `isChecking === true`. Desktop disables the button (`checkBtn.IsEnabled = false`) during the await but provides **no visual "working" indicator**. | functional / visual.
  - Actions gap=4px → webui `gap-1` = 4px ✓.

### Gaps (Part 7 — Proxy rows; most apply to Transcode rows too)
| # | Gap | Severity |
|---|-----|----------|
| 7.1 | Name cell FontWeight=SemiBold (600) vs webui `font-medium` (500). | visual |
| 7.2 | URL cell FontSize=12 vs webui `text-sm` = 14. | visual |
| 7.3 | URL cell Foreground=SecondaryTextBrush vs webui default (PrimaryText). | visual |
| 7.4 | No row hover effect vs webui `hover:bg-muted/50`. | visual |
| 7.5 | Health text FontSize=12 vs webui 14. | visual |
| 7.6 | Health dot colors: healthy `#22C55E` ✓. unhealthy `#DC4646` vs theme-likely `#EF4444`. disabled `#828282` vs `bg-muted-foreground` ≈ `#A1A1A1`. | visual |
| 7.7 | **Jobs cell in transcode section uses a pill badge**; webui renders plain text. Invented UI. | visual / structural |
| 7.8 | Last Check format: `ToString("g")` = "11/15/2026 2:30 PM" vs webui `toLocaleString()` which includes seconds. | minor |
| 7.9 | Icon button size 32×32 vs webui 28×28. | visual |
| 7.10 | Icon glyph size 14 vs webui 12. | visual |
| 7.11 | **Delete icon colored red** (`#DC5A5A`) — webui ghost button inherits neutral color (no red). | visual |
| 7.12 | **No spin animation** on check-health button during pending mutation. Webui `animate-spin` class rotates icon. | functional — user sees no "working" feedback |
| 7.13 | Row border: desktop code inserts a `<Border>` with `BorderThickness="0,1,0,0"` between rows. Webui `<TableRow>` uses `border-b` (bottom border on each row EXCEPT last). Visual result similar, but note that desktop's approach skips border on first/last row which matches webui's built-in table styling. | ok |
| 7.14 | Toggle switch visual difference: WinUI ToggleSwitch has a wider track and larger thumb than shadcn Switch. | visual |
| 7.15 | **Status toggle** sits alone in Status column; webui shadcn Switch has a subtle on/off visual (green track when on). WinUI ToggleSwitch uses accent color when on — usually matches. Verify accent brush = webui `--primary`. | minor |

## 8. Empty state (each section)

### Webui (AdminNodes.tsx:87–101)
```
<TableRow>
  <TableCell colSpan={colCount} class="text-muted-foreground py-8 text-center">
    <div class="space-y-2">
      <p>No proxy nodes configured. Add a proxy node to enable distributed stream delivery.</p>
      <Button variant="outline" size="sm" onClick={onAdd}>
        <Plus class="mr-1 h-4 w-4" /> Add {label}
      </Button>
    </div>
  </TableCell>
</TableRow>
```
- Text: muted-foreground, 14px (inherited), centered, `py-8` = 32px top/bottom pad.
- Gap between text and button: `space-y-2` = 8px.
- Button: `variant="outline" size="sm"` = outlined border, 32px tall.
- Transcode variant: "No transcode nodes configured. Add a transcode node to offload video transcoding from the main server."

### Desktop (AdminNodesPage.xaml:173–195, 300–321)
```
<Border Padding="20,32" Visibility="Collapsed">
  <StackPanel HorizontalAlignment="Center" Spacing="8">
    <TextBlock Text="No proxy nodes..." FontSize=13 Foreground=TertiaryTextBrush MaxWidth=420 />
    <Button x:Name="ProxyEmptyAddButton" Style="OutlineButtonStyle" HorizontalAlignment="Center">
      <StackPanel Orientation="Horizontal" Spacing="6">
        <FontIcon Glyph="&#xE710;" FontSize="12" />
        <TextBlock Text="Add Proxy" FontSize="13" />
      </StackPanel>
    </Button>
  </StackPanel>
</Border>
```

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 8.1 | Text FontSize=13 vs webui 14. | visual |
| 8.2 | Color TertiaryTextBrush vs webui muted-foreground — close enough if brushes align. | minor |
| 8.3 | Empty-state button icon size 12, text 13 — webui 16/14. | visual |
| 8.4 | Empty-state button label FontSize=13 (smaller than webui 14). | visual |
| 8.5 | Padding `20,32` horizontal x vertical. Webui `py-8` = 32 vertical (matches), horizontal inherited from cell (`px-2` = 8). Desktop has 20 extra side padding. Minor. | minor |
| 8.6 | Spacing between text and button: 8 (= webui `space-y-2` ✓). | ok |

## 9. Add / Edit dialog

### Webui (AdminNodes.tsx:363–378 + NodeForm 174–246)
- `<Dialog>` shadcn (Radix-based), `<DialogContent>` default `sm:max-w-lg` (512px).
- Title: "Add Proxy Node", "Add Transcode Node", or "Edit Node".
- Form fields: Name, Type (read-only Badge with `variant="secondary"`), URL.
- Each field: `<Label>` + `<Input>`, with `space-y-2` = 8px between label and input, and `space-y-4` = 16px between fields.
- URL hint below input:
  - Proxy: "Must be publicly accessible by streaming clients."
  - Transcode: "Must be reachable from proxy nodes and the backend server. A private/internal IP or localhost is fine — no public URL needed."
  - Note: webui transcode hint says "the backend server" and "or localhost is fine".
- Submit button: full-width (`className="w-full"`), disabled while `isPending`, label "Saving..." when pending else "Save".
- On success: `onClose` closes dialog.

### Desktop (AdminNodesPage.xaml.cs:341–511)
- `ContentDialog` with Title, PrimaryButtonText="Save", CloseButtonText="Cancel", DefaultButton=Primary.
- Form: `StackPanel Width=380 Spacing=16`.
- Fields via `AddField(label, control, hint?)`:
  - Label: TextBlock FontSize=12 SemiBold SecondaryTextBrush.
  - Control: TextBox with CornerRadius=8 FontSize=13 and placeholder.
- Fields: Name, Type (read-only Border badge), URL with hint.
- URL hint (transcode): "Must be reachable from proxy nodes and the backend. A private/internal IP is fine — no public URL needed." — **"backend" (not "backend server"), and "or localhost" omitted**.
- No full-width Save button inside the form — Save is the dialog's PrimaryButton (ContentDialog chrome).
- No "Saving..." loading state on the button (ContentDialog Primary button does not auto-show loading; code awaits synchronously).

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 9.1 | Webui uses shadcn Dialog (custom backdrop, close X, sm:max-w-lg=512px). Desktop uses WinUI ContentDialog (system-styled, different chrome). Structural difference expected; both OK. | structural/ok |
| 9.2 | Field label FontSize=12 vs webui `<Label>` default 14. SemiBold (600) vs webui label `text-sm font-medium` (500). | visual |
| 9.3 | Input FontSize=13 vs webui Input default 14. | visual |
| 9.4 | Input CornerRadius=8 vs webui Input `rounded-md` = 6. | visual |
| 9.5 | Form width 380 vs webui default DialogContent 512px. Tighter. | visual |
| 9.6 | Field spacing 16 (matches webui `space-y-4` ✓). Label-to-input spacing 6 vs webui 8. | visual |
| 9.7 | Type badge: desktop wraps in `Border` with 1px `BorderBrush` border. Webui `Badge variant="secondary"` has no border — solid muted bg only. | visual |
| 9.8 | Transcode URL hint text deviates slightly: missing "server" and "or localhost is fine". | text |
| 9.9 | **No loading state** on Save button in desktop (`"Saving..."` text during mutation). | functional/minor |
| 9.10 | **No form validation shown** — desktop silently returns without feedback if name/url is whitespace. Webui uses native `required` attribute → browser error tooltip. | functional |
| 9.11 | Edit flow: webui preserves `node.url` and `node.name` defaults. Desktop does too (`existingNode?.Name ?? ""`). ✓ But Edit dialog title: webui says "Edit Node", desktop says "Edit Node" ✓. | ok |

## 10. Delete confirmation

### Webui (AdminNodes.tsx:308–321)
Uses `<ConfirmDialog>` component (UNVERIFIED internals — blocked). API:
- `title="Delete node"`
- `description={`Delete stream node "${node.name}"? This action cannot be undone.`}`
- `confirmLabel="Delete"`
- `variant="destructive"` → likely renders Delete button with red/destructive styling.

### Desktop (AdminNodesPage.xaml.cs:410–432)
- `ContentDialog` Title="Delete node", Content = description string.
- PrimaryButtonText="Delete", CloseButtonText="Cancel".
- DefaultButton=Close (focus on Cancel — a safety feature).
- **Primary button NOT styled destructive.** WinUI ContentDialog's PrimaryButton uses AccentButton style (blue), not red/destructive.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 10.1 | **Delete button not destructive-styled (red)** on desktop; webui renders red. | visual |
| 10.2 | Default focus on Cancel (desktop) vs likely Confirm on webui. Safety call — actually better UX on desktop. | ok |

## 11. Loading state

### Webui (AdminNodes.tsx:294)
```
if (isLoading) return <div class="page-shell py-8">Loading nodes...</div>;
```
- Simple plain text in centered shell, no skeleton (unlike ApiKeys).

### Desktop (AdminNodesPage.xaml:22–27)
- Centered ProgressRing 48×48 — WinUI spinner.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 11.1 | Webui shows "Loading nodes..." text; desktop shows spinner. Divergent UX, but both acceptable. | minor |

## 12. Error state

### Webui
No explicit error state — react-query errors surface via toast (UNVERIFIED — hook blocked).

### Desktop (AdminNodesPage.xaml:30–47)
- Centered StackPanel with error TextBlock (ErrorBrush red) + Retry button.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 12.1 | Desktop has dedicated error view with Retry. Webui lacks one. Desktop is actually better here, but structurally diverges from webui. | functional (extra feature) |

## 13. Realtime / refetch behavior

### Webui
UNVERIFIED — `useAdminNodes` hook file is read-blocked. Based on sibling admin hooks in this project (observed in prior audits), it likely:
- Polls every ~5–10s via `refetchInterval` OR
- Uses event-channel SSE/WebSocket invalidation.

### Desktop (AdminNodesViewModel.cs)
- `LoadAsync` called once on Page_Loaded.
- After each mutation (create/update/delete/toggle/checkHealth), `LoadAsync()` is re-invoked to refresh.
- **No polling, no SSE subscription.** Health status will NOT refresh unless user hits Check Health on a node manually.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| 13.1 | **Probable MISSING refetchInterval / realtime updates**. If webui auto-refreshes node health, desktop does not. Until webui hook is verified, flagged as likely gap. | functional — UNVERIFIED |

## Prioritized Fix List — Nodes

### P0 (critical / correctness)
- [N-P0-1] Count badge `CardBackgroundBrush` equals table card bg → badge invisible on the card. Switch to `SurfaceRaisedBrush` (7.3 / 3.1).
- [N-P0-2] Missing spin animation on check-health button; user gets no "working" feedback during network call (7.12). Add rotating RefreshCw via Storyboard or animated icon.
- [N-P0-3] Verify & implement realtime/polling refresh if webui has one (13.1).

### P1 (high-visibility visual drift)
- [N-P1-1] Info/warning banner **text color is grey, not `text-info` / `text-warning`** (4.2, 4.3). Add `InfoTextBrush` and `WarningTextBrush` and apply to both icon and paragraph.
- [N-P1-2] Banner corner radius 22 → 12 (4.1).
- [N-P1-3] Table card corner radius 26 → 12 (5.1).
- [N-P1-4] Badge corner 10 → 6, text 11 → 12, padding 7 → 8 (3.2–3.4).
- [N-P1-5] Add-button label "Add Proxy Node" → "Add Proxy" (3.5). Plus icon 13 → 16, gap 6 → 4 (3.6).
- [N-P1-6] Table header font-weight SemiBold → Medium (500), size 12 → 14 (6.1–6.2).
- [N-P1-7] Name cell SemiBold → Medium. URL cell size 12 → 14, color SecondaryTextBrush → PrimaryTextBrush, keep monospace (7.1–7.3).
- [N-P1-8] Health text size 12 → 14 (7.5).
- [N-P1-9] Action icon buttons 32 → 28, icon 14 → 12 (7.9–7.10).
- [N-P1-10] Remove red color from delete trash icon in row actions (7.11).
- [N-P1-11] Remove pill-badge for Jobs > 0 — use plain text (7.7).
- [N-P1-12] Delete confirmation "Delete" button should be destructive-styled red (10.1).
- [N-P1-13] Inline status banner → toast system (2.1, 2.2). Either keep banner project-wide or convert — align with other pages.

### P2 (nitpick / polish)
- [N-P2-1] Title/subtitle spacing 4 → 12 (1.1). Subtitle 14 → 16 at ≥640px (1.2).
- [N-P2-2] `page-shell` max-width container (1.3).
- [N-P2-3] Health dot unhealthy color `#DC4646` → `#EF4444`; disabled `#828282` → `#A1A1A1` (7.6).
- [N-P2-4] Row hover state `hover:bg-muted/50` (7.4).
- [N-P2-5] Last Check format include seconds (7.8).
- [N-P2-6] Info banner uses `surface-panel-subtle` tinted background, not plain `SurfaceBrush` (4.5).
- [N-P2-7] Font-weight of "Must be on the same network..." clause bump to Bold (700), not SemiBold (600) (4.9).
- [N-P2-8] Transcode URL hint: match exact wording incl. "backend server" and "or localhost is fine" (9.8).
- [N-P2-9] Dialog label size 12 → 14, weight SemiBold → Medium; input size 13 → 14, corner 8 → 6 (9.2–9.4).
- [N-P2-10] Dialog form width 380 → 512 (9.5).
- [N-P2-11] Dialog Save button "Saving..." loading state (9.9); inline validation feedback (9.10).
- [N-P2-12] Empty state text size 13 → 14 (8.1); button icon 12 → 16, text 13 → 14 (8.3–8.4).

---

# PART 2 — AdminApiKeys

Single-table page with header, create dialog (two-step: form → reveal), per-row actions (tier dropdown, delete), and pagination.

## 1. Page header

### Webui (AdminApiKeys.tsx:95–115)
```
<div class="page-header gap-5">
  <div class="space-y-3">
    <h1 class="page-title text-[clamp(2rem,4vw,3rem)]">API keys</h1>
    <p class="page-subtitle text-sm sm:text-base">
      Create and manage machine credentials for integrations, automation, and internal tools.
    </p>
  </div>
  <Dialog open={createOpen} onOpenChange={setCreateOpen}>
    <DialogTrigger asChild>
      <Button size="sm"><Plus class="mr-1 h-4 w-4" /> Create Key</Button>
    </DialogTrigger>
    <DialogContent>
      <DialogHeader><DialogTitle>Create API Key</DialogTitle></DialogHeader>
      <CreateApiKeyForm onClose={...} />
    </DialogContent>
  </Dialog>
</div>
```
- Same title/subtitle pattern as Nodes.
- **Create Key button lives inside `page-header`** — right side of header row, aligned center-vertical by page-header flex. Label "Create Key".

### Desktop (AdminApiKeysPage.xaml:54–86)
- Grid with two columns: title+subtitle in col 0, Create Key button in col 1.
- Title FontSize=42 Bold; subtitle FontSize=14 SecondaryTextBrush.
- Create button Style=AccentButtonStyle, VerticalAlignment=Center, Icon `&#xE710;` FontSize=13 + "Create Key".

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K1.1 | Title StackPanel Spacing=4 vs webui `space-y-3` = 12px. | visual |
| K1.2 | Subtitle no responsive resize — stays 14 regardless of width. | visual |
| K1.3 | Create button icon 13 vs webui 16 (`h-4 w-4`). Gap 6 vs webui 4 (`mr-1`). | visual |
| K1.4 | Header `gap-5` (20px between title block and button) — desktop uses ColumnSpacing=0 on the Grid, so button sits flush right. Usually fine, but not explicit. | minor |

## 2. Status banner

### Webui
Does not exist. Feedback via `toast` (`toast.success("Copied to clipboard")`, line 66 and 258).

### Desktop (AdminApiKeysPage.xaml:89–99)
Same inline banner pattern as Nodes (AccentBackgroundBrush, 4-sec auto-hide).

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K2.1 | Inline banner vs toast. Same gap as Nodes 2.1. | functional/visual |

## 3. Table card container

### Webui (AdminApiKeys.tsx:117)
```
<div class="surface-panel overflow-x-auto rounded-2xl border-0">
```
- `rounded-2xl` = 16px (**different from Nodes' `rounded-xl`** — ApiKeys uses the larger radius).
- `border-0`.

### Desktop (AdminApiKeysPage.xaml:103–107)
- Border Background=CardBackgroundBrush CornerRadius=26 BorderThickness=0.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K3.1 | CornerRadius 26 vs webui 16 (`rounded-2xl`). **XAML comment claims `rounded-[1.6rem]` (25.6px) which is NOT in the TSX — webui uses `rounded-2xl` = 16px**. Desktop misquotes webui in its own comment and uses 26. | visual |

## 4. Table header

### Webui (AdminApiKeys.tsx:119–129)
Columns: Label, User, Key, Tier, Created, Last Used, Actions.
- Actions col `className="w-24"` = 96px.
- Default shadcn TableHead styling (`font-medium text-muted-foreground`).
- Header text case: Title Case.

### Desktop (AdminApiKeysPage.xaml:110–128)
```
<Grid Padding="20,14,20,14" ColumnSpacing="12">
  1.6*, 1.2*, 2*, 110, 110, 110, 90
```
- Text: FontSize=11 FontWeight=SemiBold TertiaryTextBrush.
- **Cases: "LABEL", "USER", "KEY", "TIER", "CREATED", "LAST USED", "ACTIONS" — all UPPERCASE.**
- Actions col=90px.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K4.1 | **ALL-CAPS header text** — webui uses Title Case. | visual / text |
| K4.2 | FontSize 11 vs webui 14. | visual |
| K4.3 | FontWeight SemiBold (600) vs webui font-medium (500). | visual |
| K4.4 | Actions col 90 vs webui `w-24` = 96. | visual minor |
| K4.5 | Tier col 110 vs webui — no explicit width, sized by content (`<Select>` trigger `w-[120px]`). Close. | ok |

## 5. Table rows

### Webui (AdminApiKeys.tsx:138–188)
- **Label**: `font-medium` (500), default 14px.
- **User** (username): default 14px, no extra classes.
- **Key**:
  ```
  <div class="flex items-center gap-1.5">
    <code class="bg-muted rounded px-1.5 py-0.5 font-mono text-xs">{maskKey(key.key)}</code>
    <Button variant="ghost" size="icon" class="h-6 w-6" onClick={copy}>
      <Copy class="h-3 w-3" />
    </Button>
  </div>
  ```
  - `<code>` = monospace 12px (`text-xs`), bg-muted (muted surface), rounded (4px), padding 6×2.
  - Copy button 24×24, icon 12.
  - `maskKey`: first 6 + "..." + last 4.
- **Tier** (dropdown):
  ```
  <Select value={key.rate_tier} onValueChange={tier => updateTier.mutate({id, tier})}>
    <SelectTrigger class="w-[120px]"><SelectValue /></SelectTrigger>
    <SelectContent>
      <SelectItem value="standard">Standard</SelectItem>
      <SelectItem value="elevated">Elevated</SelectItem>
    </SelectContent>
  </Select>
  ```
  - Trigger 120px wide, default 40px tall.
- **Created**: `text-muted-foreground text-xs` → muted grey 12px. Format `new Date(created_at).toLocaleDateString()` ("11/15/2026").
- **Last Used**: same styling. Format `toLocaleDateString()` else "Never".
- **Actions**: single ghost icon button 28×28 with Trash2 icon 12×12. No red color.

### Desktop (AdminApiKeysPage.xaml.cs:77–233)
- **Label** (col 0): FontSize=14 FontWeight=SemiBold PrimaryTextBrush TextTrimming=Ellipsis. **SemiBold vs webui Medium.**
- **User** (col 1): FontSize=13 SecondaryTextBrush Ellipsis. **FontSize=13 vs webui 14. Color drift to secondary.**
- **Key** (col 2): StackPanel horizontal Spacing=6.
  - Border SurfaceRaisedBrush CornerRadius=4 Padding=6,2,6,2 → TextBlock FontSize=12 Consolas SecondaryTextBrush.
  - **SecondaryTextBrush for code text** — webui `<code>` inherits default (PrimaryText). Color drift.
  - Copy button size=24 (matches `h-6 w-6` ✓), icon FontSize=12 glyph `\uE8C8` (MDL2 Copy). Webui lucide Copy, icon 12 ✓.
- **Tier** (col 3): ComboBox Width=120 FontSize=13 CornerRadius=8. Items "Standard" tag="standard", "Elevated" tag="elevated".
  - `SelectionChanged` fires update only when changed ✓.
  - WinUI ComboBox height ~32px; webui default 40. Slightly shorter.
- **Created** (col 4): FontSize=12 TertiaryTextBrush. Format `ToString("d")` = short date ("11/15/2026"). ✓ for format.
- **Last Used** (col 5): same. Fallback "Never" ✓.
- **Actions** (col 6): single icon button size=28 (✓), icon FontSize=12 (✓), **fgColor=`#DC5A5A` (red) — webui has no red**. Glyph `\uE74D` = MDL2 Delete.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K5.1 | Label FontWeight SemiBold → Medium. | visual |
| K5.2 | User FontSize 13 → 14; color Secondary → Primary. | visual |
| K5.3 | Key `<code>` Foreground Secondary → Primary. | visual |
| K5.4 | ComboBox CornerRadius 8 vs webui Select `rounded-md` = 6. | visual |
| K5.5 | Delete icon **red** on desktop; webui ghost button neutral. | visual |
| K5.6 | No row hover effect. | visual |

## 6. Pagination

### Webui (AdminApiKeys.tsx:191–235)
```
{total > pageSize && (
  <div class="flex items-center justify-between px-4 py-4">
    <div class="flex items-center gap-4">
      <span class="text-muted-foreground text-sm">
        Showing {page*pageSize+1}-{Math.min((page+1)*pageSize, total)} of {total}
      </span>
      <Select value={String(pageSize)} onValueChange={v => {setPageSize(Number(v)); setPage(0);}}>
        <SelectTrigger class="h-8 w-[100px]"><SelectValue /></SelectTrigger>
        <SelectContent>
          {["25","50","100"].map(size => <SelectItem key={size} value={size}>{size} rows</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
    <div class="flex gap-2">
      <Button variant="outline" size="sm" disabled={page === 0} onClick={() => setPage(p => p-1)}>Previous</Button>
      <Button variant="outline" size="sm" disabled={(page+1)*pageSize >= total} onClick={() => setPage(p => p+1)}>Next</Button>
    </div>
  </div>
)}
```
- Default page size 25, options 25/50/100.
- Only rendered when `total > pageSize`.
- "Showing X-Y of Z" text + rows-per-page dropdown + Previous/Next outline buttons.

### Desktop
**COMPLETELY MISSING.** No pagination UI in AdminApiKeysPage.xaml or .cs. `ViewModel.ApiKeys` binds the full list to `KeysPanel` with no slicing.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K6.1 | **CRITICAL: entire pagination block missing.** When a server has >25 keys, desktop shows all rows at once — no "Showing 1-25 of N", no rows-per-page selector, no Previous/Next buttons. | CRITICAL — functional |

## 7. Empty state

### Webui (AdminApiKeys.tsx:131–137)
```
<TableRow>
  <TableCell colSpan={7} class="text-muted-foreground text-center">
    No API keys yet. Create one to get started.
  </TableCell>
</TableRow>
```
- Text only. 14px default (inherits from table). No call-to-action button. No extra padding.

### Desktop (AdminApiKeysPage.xaml:134–141)
```
<Border x:Name="EmptyState" Padding="20,40" Visibility="Collapsed">
  <TextBlock Text="No API keys yet. Create one to get started."
             FontSize=13 Foreground=TertiaryTextBrush TextAlignment="Center" />
</Border>
```

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K7.1 | FontSize 13 vs webui 14. | visual |
| K7.2 | Padding 20,40 — webui has no explicit padding (just cell default). Adds extra top/bottom space. Minor. | minor |

## 8. Loading state

### Webui (AdminApiKeys.tsx:69–77)
```
<div class="page-shell space-y-3 py-4 sm:py-6">
  <Skeleton class="h-10 w-full rounded-lg" />
  {Array.from({ length: 5 }).map((_, i) => (<Skeleton class="h-12 w-full rounded-lg" />))}
</div>
```
- Skeleton page: 40px top bar + 5 × 48px row skeletons, `rounded-lg` = 8px.

### Desktop (AdminApiKeysPage.xaml:22–27)
- Single centered ProgressRing 48×48.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K8.1 | **Webui renders skeleton rows** for smoother load transition; desktop shows a spinner. Major UX feel difference. | visual / functional |

## 9. Create dialog — Step 1 (form)

### Webui (AdminApiKeys.tsx:286–316)
```
<form onSubmit={handleSubmit} class="space-y-4">
  <div class="space-y-2">
    <Label>Label</Label>
    <Input value={label} onChange={...} placeholder="e.g. CI/CD Pipeline" required />
  </div>
  <div class="space-y-2">
    <Label>User</Label>
    <Select value={userId} onValueChange={setUserId}>
      <SelectTrigger><SelectValue /></SelectTrigger>
      <SelectContent>{users.map(u => <SelectItem value={String(u.id)}>{u.username}</SelectItem>)}</SelectContent>
    </Select>
  </div>
  <Button type="submit" class="w-full" disabled={createMutation.isPending}>
    {createMutation.isPending ? "Creating..." : "Create"}
  </Button>
</form>
```
- Default User = current user (`user.id` from `useAuth`).
- Full-width Create button inside the form with "Creating..." loading state.

### Desktop (AdminApiKeysPage.xaml.cs:244–323)
- ContentDialog Title="Create API Key", PrimaryButtonText="Create", CloseButtonText="Cancel".
- Form: Width=380 Spacing=16.
- TextBox for Label, ComboBox for User (no default-to-current-user; uses `SelectedIndex = 0` = first in list).
- No loading state.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K9.1 | **User default: webui defaults to current authenticated user; desktop defaults to first user in the list.** | functional |
| K9.2 | No "Creating..." loading state on Create button. | minor |
| K9.3 | Field label 12 SemiBold vs webui 14 Medium (same pattern as Nodes 9.2). | visual |
| K9.4 | Input size 13 → 14, corner 8 → 6. | visual |
| K9.5 | Form width 380 vs 512. | visual |
| K9.6 | No inline validation for empty Label (desktop silently returns; webui uses `required` attr). | functional |

## 10. Create dialog — Step 2 (reveal)

### Webui (AdminApiKeys.tsx:272–284)
```
<div class="space-y-4">
  <p class="text-muted-foreground text-sm">Copy your API key now. You won't be able to see the full key again.</p>
  <code class="bg-muted block rounded p-3 font-mono text-xs break-all">{createdKey}</code>
  <Button class="w-full" onClick={handleCopyAndClose}>
    <Copy class="mr-1 h-4 w-4" /> Copy & Close
  </Button>
</div>
```
- Warning text, 14px, muted.
- Code block: bg-muted, rounded=4, p-3=12px, font-mono, text-xs=12px, `break-all` (wraps anywhere).
- "Copy & Close" button full-width with Copy icon + label.
- **Both steps use the same DialogContent** (no title change). Webui toggles `createdKey` state within form — same dialog, replaces form body.

### Desktop (AdminApiKeysPage.xaml.cs:327–394)
- **Separate ContentDialog** titled "API Key Created" with CloseButtonText="Close".
- RevealPanel Width=420 Spacing=14.
- Warning TextBlock FontSize=13 SecondaryTextBrush.
- Key Border SurfaceRaisedBrush CornerRadius=6 Padding=12,10,12,10 with TextBlock FontSize=12 Consolas PrimaryTextBrush TextWrapping=Wrap IsTextSelectionEnabled=true.
- "Copy & Close" button AccentButtonStyle HorizontalAlignment=Stretch with inline icon+text.
- Copy action copies key and hides dialog.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K10.1 | **Two separate dialogs on desktop vs single dialog with state transition on webui.** User sees a dialog close and a new one open — more jarring. | functional |
| K10.2 | Webui dialog title stays "Create API Key" throughout; desktop changes to "API Key Created". | text |
| K10.3 | Warning text 13 vs 14. | visual |
| K10.4 | Code block bg: webui `bg-muted` — desktop `SurfaceRaisedBrush` — likely the right mapping. | ok |
| K10.5 | Webui has no explicit selection-enabled on code (browsers auto-enable); desktop explicitly enables. ✓ | ok |
| K10.6 | Copy button full-width AccentButtonStyle ✓. Icon glyph `\uE8C8` MDL2 Copy ≈ lucide Copy ✓. | ok |
| K10.7 | Code `break-all` wraps at any character; desktop TextWrapping=Wrap wraps at word boundaries — long unbroken key may not wrap the same way. | minor |

## 11. Delete / Revoke confirmation

### Webui (AdminApiKeys.tsx:81–94)
ConfirmDialog:
- `title="Revoke API key"` (sentence case)
- `description={`Revoke API key "${confirmRevokeKey?.label}"? This action cannot be undone.`}`
- `confirmLabel="Revoke"`
- `variant="destructive"`

### Desktop (AdminApiKeysPage.xaml.cs:398–420)
ContentDialog:
- Title="Revoke API Key" (Title Case — "Key" capitalized)
- Content=`$"Revoke API key \"{key.Label}\"? This action cannot be undone."`
- PrimaryButtonText="Revoke"
- DefaultButton=Close
- **No destructive styling on Revoke button.**

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K11.1 | Title case: "Revoke API Key" (desktop) vs "Revoke API key" (webui) — trailing "key" lowercase on webui. | text |
| K11.2 | Revoke button not red. | visual |

## 12. Copy-to-clipboard feedback

### Webui (AdminApiKeys.tsx:64–67)
```
function handleCopy(text: string) {
  navigator.clipboard.writeText(text);
  toast.success("Copied to clipboard");
}
```
Toast fires every copy.

### Desktop (AdminApiKeysPage.xaml.cs:430–435)
```
private static void CopyToClipboard(string text) {
  var package = new DataPackage();
  package.SetText(text);
  Clipboard.SetContent(package);
}
```
**NO feedback toast/banner after copy.** User has no confirmation the key landed on clipboard.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| K12.1 | **No "Copied to clipboard" feedback after clicking the copy button on a row** (or in reveal dialog for mask copy). Silent behavior can make users click repeatedly or doubt the action. | functional / UX |

## 13. Realtime / refetch

UNVERIFIED for webui (hook blocked). Desktop re-loads after mutations only, no polling. Same concern as Nodes 13.1.

## Prioritized Fix List — API Keys

### P0
- [K-P0-1] **Implement pagination** (page size selector + Prev/Next + "Showing X-Y of Z") (K6.1).
- [K-P0-2] **Add copy feedback toast/banner** after clipboard copy (K12.1).

### P1
- [K-P1-1] Table header **ALL-CAPS → Title Case** (K4.1). Size 11→14, weight SemiBold→Medium (K4.2–K4.3).
- [K-P1-2] Card CornerRadius 26 → 16 (K3.1).
- [K-P1-3] Label cell SemiBold → Medium. User cell size 13→14, color Secondary→Primary. Key code Foreground Secondary→Primary (K5.1–K5.3).
- [K-P1-4] Delete icon remove red tint (K5.5).
- [K-P1-5] Unify Create dialog into a single dialog with state transition instead of two (K10.1).
- [K-P1-6] Default User combobox to current logged-in user (K9.1).
- [K-P1-7] Revoke button destructive-red (K11.2).
- [K-P1-8] Loading state: replace ProgressRing with skeleton rows matching webui (K8.1).
- [K-P1-9] Replace status banner with toast (K2.1).
- [K-P1-10] Verify / add realtime refresh (K13).

### P2
- [K-P2-1] Title-block inner spacing 4 → 12 (K1.1); subtitle responsive 14→16 (K1.2).
- [K-P2-2] Create button icon 13→16, gap 6→4 (K1.3).
- [K-P2-3] ComboBox corner 8→6 (K5.4); Input corner 8→6, size 13→14 (K9.4); Label 12 SemiBold → 14 Medium (K9.3).
- [K-P2-4] Form width 380 → 512 (K9.5).
- [K-P2-5] "Creating..." loading state on Create button (K9.2); inline validation (K9.6).
- [K-P2-6] Row hover (K5.6).
- [K-P2-7] Empty state size 13→14, remove extra padding (K7.1–K7.2).
- [K-P2-8] Revoke dialog title "Revoke API Key" → "Revoke API key" (K11.1).
- [K-P2-9] Reveal warning text 13 → 14 (K10.3).
- [K-P2-10] Reveal dialog title stays "Create API Key" not "API Key Created" (K10.2).

---

# PART 3 — AdminInviteCodes

Webui source is `admin-settings/InviteCodesTab.tsx` — it's a **TAB inside admin settings**, not a standalone page. Desktop implements it as a **standalone page** (`AdminInviteCodesPage`). This is already a structural divergence.

## 1. Page container / tab vs page

### Webui (InviteCodesTab.tsx:72–194)
```
<div class="space-y-6">
  <ConfirmDialog ... />
  <div class="flex justify-end"> <!-- Create Code button -->
    <Dialog>...</Dialog>
  </div>
  <Card> <!-- Public Signups -->
    <CardHeader><CardTitle class="text-base">Public Signups</CardTitle><CardDescription>...</CardDescription></CardHeader>
    <CardContent>...</CardContent>
  </Card>
  <Table>...</Table>
</div>
```
- **No page header / title / subtitle** — the surrounding settings layout provides the section title.
- Vertical spacing between top-level blocks: `space-y-6` = 24px.
- Create Code button sits **alone on a flex row aligned end (right)** ABOVE the Public Signups card.
- Public Signups is a `<Card>` with CardHeader/CardContent (shadcn Card).
- Table is **unwrapped** (no surface-panel container) — sits directly on the tab's background.

### Desktop (AdminInviteCodesPage.xaml:37–106)
- ScrollViewer + StackPanel Padding=40,28,40,40 Spacing=24.
- **Has a page header** with title "Invite codes" FontSize=42 Bold + subtitle "Manage invite codes for user registration." + Create Code button on the right of the header.
- Public Signups card: Border CardBackgroundBrush CornerRadius=22.
- Invite codes table: Border CardBackgroundBrush CornerRadius=26 **WRAPPED in a panel** (unlike webui which renders bare Table).

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I1.1 | **Structural: webui is a tab inside admin settings; desktop is a standalone top-level admin page with its own title.** If desktop wants parity, this should be a section within an Admin Settings layout (not its own navbar entry). | CRITICAL structural |
| I1.2 | Desktop invents a title ("Invite codes") and subtitle ("Manage invite codes for user registration.") — **webui has neither**. These are fabricated strings. | structural / text |
| I1.3 | Create Code button location: webui places it on its own right-aligned row ABOVE the Public Signups card. Desktop places it inline with the page title (top-right of header). Different visual anchor. | visual/structural |
| I1.4 | Invite codes Table is **wrapped in a Card surface** on desktop; webui renders `<Table>` bare (no wrapping card). Desktop adds an invented card. | visual / structural |
| I1.5 | `space-y-6` (24px) matches desktop Spacing=24 ✓. | ok |

## 2. Public Signups card

### Webui (InviteCodesTab.tsx:105–122)
```
<Card>
  <CardHeader>
    <CardTitle class="text-base">Public Signups</CardTitle>
    <CardDescription>When enabled, users with a valid invite code can create their own accounts.</CardDescription>
  </CardHeader>
  <CardContent>
    <div class="flex items-center gap-3">
      <Switch checked={signupEnabled} onCheckedChange={handleToggleSignup} disabled={updateSetting.isPending} />
      <Label>{signupEnabled ? "Enabled" : "Disabled"}</Label>
    </div>
  </CardContent>
</Card>
```
- `Card` shadcn default: `rounded-xl border bg-card text-card-foreground shadow`. `rounded-xl` = 12px, border-1.
- `CardHeader`: `flex flex-col space-y-1.5 p-6` — 24px padding, 6px vertical spacing title↔description.
- `CardTitle class="text-base"` → 16px font, font-semibold (default Card title).
- `CardDescription`: `text-sm text-muted-foreground` → 14px muted grey.
- `CardContent`: `p-6 pt-0` — 24px all, except no top padding (to avoid doubled gap with header).
- Inside content: flex row, gap 12px, Switch + Label showing "Enabled"/"Disabled" text dynamically.
- Switch has `disabled={updateSetting.isPending}` — disabled while mutation pending.

### Desktop (AdminInviteCodesPage.xaml:61–79)
```
<Border Background="CardBackgroundBrush" CornerRadius="22" BorderThickness="0" Padding="20,14,20,14">
  <StackPanel Spacing="12">
    <StackPanel Spacing="4">
      <TextBlock Text="Public Signups" FontSize="14" FontWeight="SemiBold" ... />
      <TextBlock Text="When enabled..." FontSize="12" Foreground="SecondaryTextBrush" />
    </StackPanel>
    <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
      <ToggleSwitch x:Name="SignupToggle" OnContent="Enabled" OffContent="Disabled" Toggled="SignupToggle_Toggled" />
    </StackPanel>
  </StackPanel>
</Border>
```

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I2.1 | CornerRadius 22 vs webui 12 (`rounded-xl`). | visual |
| I2.2 | BorderThickness 0 vs webui border-1 (Card has default border). | visual |
| I2.3 | Title FontSize 14 vs webui `text-base` = 16. | visual |
| I2.4 | Description FontSize 12 vs webui `text-sm` = 14. | visual |
| I2.5 | Padding `20,14,20,14` vs webui `p-6` (24 all). Title-content spacing 12 vs webui ~24 (p-6 + pt-0). | visual |
| I2.6 | Title↔description inner spacing 4 vs webui `space-y-1.5` = 6. | visual |
| I2.7 | Content row: ToggleSwitch alone; no separate `<Label>` showing "Enabled"/"Disabled". **WinUI ToggleSwitch has built-in OnContent/OffContent labels**, so desktop gets the label for free — but the styling differs from webui's separate Switch + Label layout. Text "Enabled"/"Disabled" appears on the switch, but webui renders a standalone Label beside a Switch (spacing, color, font-weight differ). | visual |
| I2.8 | **No `disabled` state** while settings mutation is pending (webui disables the switch). Desktop allows double-clicks during the API call. | functional |

## 3. Create Code button

### Webui (InviteCodesTab.tsx:89–103)
```
<div class="flex justify-end">
  <Dialog>
    <DialogTrigger asChild>
      <Button size="sm"><Plus class="mr-1 h-4 w-4" /> Create Code</Button>
    </DialogTrigger>
    ...
  </Dialog>
</div>
```
- Right-aligned row above the Public Signups card.

### Desktop (AdminInviteCodesPage.xaml:47–54)
- Inside the page header Grid, col 1 (right of title).

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I3.1 | Location: webui places it on its own right-aligned row **above** Public Signups; desktop places it **in the header** with the title. | structural |
| I3.2 | Icon/gap/size drift same as other pages (13 vs 16, 6 vs 4). | visual |

## 4. Invite codes table

### Webui (InviteCodesTab.tsx:124–192)
Bare `<Table>` (no surrounding card), columns: Code, Label, Usage, Status, Created, Actions. Actions col `w-24`.

### Desktop (AdminInviteCodesPage.xaml:81–105)
Wrapped in Border CornerRadius=26; columns: Code(2*), Label(2*), Usage(100), Status(100), Created(120), Actions(90).

### Table Header
- Webui: default Title Case, `font-medium text-muted-foreground`, 14px.
- Desktop: UPPERCASE ("CODE", "LABEL", "USAGE", "STATUS", "CREATED", "ACTIONS"), FontSize=11 SemiBold TertiaryTextBrush.

### Row cells

**Code cell**

Webui (InviteCodesTab.tsx:145–159):
```
<div class="flex items-center gap-1.5">
  <code class="bg-muted rounded px-1.5 py-0.5 font-mono text-xs">{code.code}</code>
  <Button variant="ghost" size="icon" class="h-6 w-6" onClick={copy}><Copy class="h-3 w-3" /></Button>
</div>
```
- Code pill + copy button 24×24 with Copy icon 12×12.

Desktop (AdminInviteCodesPage.xaml.cs:83–96):
- Border SurfaceRaisedBrush CornerRadius=4 Padding=6,2,6,2 TextBlock FontSize=12 Consolas PrimaryTextBrush.
- **NO COPY BUTTON.** Just the code text.

| # | Gap | Severity |
|---|-----|----------|
| I4.1 | **CRITICAL: Copy button MISSING from desktop code cell.** Users can't copy invite codes. | CRITICAL functional |

**Label cell**

Webui (InviteCodesTab.tsx:160–162):
```
{code.label || <span class="text-muted-foreground">-</span>}
```
- Blank label renders as muted grey "-". Non-blank renders plain default text.

Desktop: `new TextBlock { Text = IsNullOrEmpty ? "-" : code.Label, FontSize=13, Foreground=SecondaryTextBrush }`.
- **Always SecondaryTextBrush**, even for non-empty labels. Webui only muddies the color when the label is empty (the "-" case).

| # | Gap | Severity |
|---|-----|----------|
| I4.2 | Non-empty label rendered in muted grey on desktop (webui uses default foreground). | visual |
| I4.3 | FontSize 13 vs webui 14. | visual |

**Usage cell**

Webui (InviteCodesTab.tsx:163–167):
```
<span class={code.use_count >= code.max_uses ? "text-destructive" : ""}>
  {code.use_count} / {code.max_uses}
</span>
```
- Plain text "N / M" colored red when maxed.
- **No "unlimited" branch** — webui just shows whatever max_uses is (e.g., "5 / 0" if max_uses=0).

Desktop (AdminInviteCodesPage.xaml.cs:107–119):
```
bool maxedOut = code.MaxUses > 0 && code.UseCount >= code.MaxUses;
var usageText = code.MaxUses > 0 ? $"{code.UseCount} / {code.MaxUses}" : $"{code.UseCount} / \u221E";
```
- Shows "∞" (U+221E infinity) when MaxUses=0.
- Red when maxed (excluding unlimited).
- FontSize=13 vs webui 14.

| # | Gap | Severity |
|---|-----|----------|
| I4.4 | Desktop shows ∞ for max_uses=0; webui shows "0". **Desktop is arguably more correct** but diverges. | functional / text |
| I4.5 | FontSize 13 vs 14. | visual |
| I4.6 | maxedOut red color: desktop uses ErrorBrush; webui `text-destructive`. Likely match. | ok |

**Status cell**

Webui (InviteCodesTab.tsx:168–175):
```
<div class="flex items-center gap-2">
  <Switch checked={code.enabled} onCheckedChange={() => handleToggleCode(code)} />
  <Badge variant={code.enabled ? "outline" : "secondary"}>{code.enabled ? "Active" : "Disabled"}</Badge>
</div>
```
- **Switch + Badge side-by-side.** Badge variant: "outline" (transparent bg, border) when enabled, "secondary" (muted bg) when disabled.

Desktop (AdminInviteCodesPage.xaml.cs:122–125):
```
var statusBadge = code.Enabled
  ? MakeBadge("Active", green-translucent bg, green fg)
  : MakeBadge("Disabled", grey-translucent bg, grey fg);
```
- **Badge ONLY. No inline Switch.**
- Toggle moved into Actions column as an icon button (see below).
- Active badge: bg `Color.FromArgb(40, 34,197,94)` (transparent green), fg `Color.FromArgb(255, 34,197,94)` (green-500).
- Disabled badge: bg `Color.FromArgb(40, 120,120,120)`, fg `Color.FromArgb(255, 160,160,160)`.

| # | Gap | Severity |
|---|-----|----------|
| I4.7 | **CRITICAL: Inline Switch for enabling/disabling MISSING from desktop status column.** Webui puts Switch + Badge in Status column. | CRITICAL functional |
| I4.8 | Badge variants: webui "Active" uses outline variant (transparent bg, bordered); desktop uses translucent green fill. Different visual. "Disabled" webui secondary (muted bg); desktop translucent grey. Semantics match, appearance differs. | visual |

**Created cell**

Webui (InviteCodesTab.tsx:176–178): `text-muted-foreground text-xs`, `toLocaleDateString()`.
Desktop: FontSize=12 TertiaryTextBrush, `ToString("d")`. ✓ matches.

**Actions cell**

Webui (InviteCodesTab.tsx:179–188):
```
<Button variant="ghost" size="icon" class="h-7 w-7" onClick={handleDelete}>
  <Trash2 class="h-3 w-3" />
</Button>
```
- **ONLY delete button.** Toggle is handled via the Switch in Status column (above).

Desktop (AdminInviteCodesPage.xaml.cs:138–168):
- **Two buttons**: Toggle (glyph `&#xE8FB;` or `&#xE73E;` — "Checkmark Filled" / "Unavailable") + Delete.
- Toggle click fires `ToggleInviteCodeCommand` with `capturedCode`.
- Delete click: opens ContentDialog confirmation then fires `DeleteInviteCodeCommand`.
- Delete icon size=28 ✓. Toggle icon size=28.

| # | Gap | Severity |
|---|-----|----------|
| I4.9 | **Desktop has TWO action buttons (Toggle + Delete); webui has ONE (Delete).** Toggle lives in Status column on webui. | functional / visual |
| I4.10 | **Toggle button label logic backwards**: the glyph shows based on current state, but the status message says "Code disabled." / "Code enabled." — this is BEFORE the toggle completes, so the displayed message reflects the PREVIOUS state, not the new one. `ShowStatus(capturedCode.Enabled ? "Code disabled." : "Code enabled.")` — `capturedCode.Enabled` is the old value, so after flipping it says correctly "Code disabled." when it was enabled. Actually this is correct logically. | ok |
| I4.11 | Toggle button glyphs `&#xE8FB;` (CheckmarkFilled) and `&#xE73E;` (Unavailable) — neither matches a standard "toggle" affordance. Webui has no toggle button; the ToggleSwitch is self-explanatory. | visual / UX |

## 5. Empty state

### Webui (InviteCodesTab.tsx:136–142)
```
<TableRow>
  <TableCell colSpan={6} class="text-muted-foreground text-center">
    No invite codes yet. Create one to get started.
  </TableCell>
</TableRow>
```

### Desktop (AdminInviteCodesPage.xaml:101–103)
```
<Border x:Name="EmptyState" Padding="20,40" Visibility="Collapsed">
  <TextBlock Text="No invite codes yet." FontSize=13 Foreground=TertiaryTextBrush HorizontalAlignment="Center" />
</Border>
```

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I5.1 | **Empty state text truncated**: webui says "No invite codes yet. Create one to get started." — desktop says only "No invite codes yet." | text |
| I5.2 | FontSize 13 vs webui 14. | visual |

## 6. Loading state

### Webui (InviteCodesTab.tsx:70)
```
if (isLoading) return <div>Loading invite codes...</div>;
```
Plain text.

### Desktop (AdminInviteCodesPage.xaml:20–23)
Centered ProgressRing 48×48.

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I6.1 | Text vs spinner. Minor. | minor |

## 7. Create dialog

### Webui (InviteCodesTab.tsx:197–249)
```
<form class="space-y-4">
  Label: "Code (optional, auto-generated if empty)"
  <Input value={code} onChange={e => setCode(e.target.value.toUpperCase())} placeholder="e.g. BETA2026" />

  Label: "Label"
  <Input value={label} onChange=... placeholder="e.g. Beta testers" />

  Label: "Max uses"
  <Input type="number" min="1" value={maxUses} onChange=... required />

  <Button type="submit" class="w-full">{isPending ? "Creating..." : "Create"}</Button>
</form>
```
- Code input **auto-uppercases on change** (`e.target.value.toUpperCase()`).
- Max uses: `type="number" min="1" required` — integer ≥1, form-level required.
- Validation before submit: `if (isNaN(max) || max <= 0) { toast.error("Max uses must be a positive number"); return; }`.
- **Webui does NOT support max_uses = 0 (unlimited).** Minimum = 1.

### Desktop (AdminInviteCodesPage.xaml.cs:173–201)
```
codeBox: TextBox PlaceholderText="Leave blank to auto-generate"
labelBox: TextBox PlaceholderText="e.g. Friends & Family"
maxUsesBox: NumberBox Value=1 Minimum=0 SpinButtonPlacementMode=Compact

AddField(form, "Code (optional)", codeBox);
AddField(form, "Label", labelBox);
AddField(form, "Max Uses (0 = unlimited)", maxUsesBox);
```
- Code input **no auto-uppercase** — user typing "beta2026" stays lowercase.
- Max uses: **NumberBox Minimum=0**, desktop **allows 0 = unlimited**.
- Max uses label says "(0 = unlimited)" — webui does not support this.
- No pre-submit validation/toast for invalid entry (NumberBox blocks invalid numeric input).

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I7.1 | **Code field does NOT auto-uppercase on input.** Webui enforces uppercase via onChange. Users may submit mixed case and the server may reject or normalize. | functional |
| I7.2 | **Max uses semantics different**: webui min=1, desktop min=0 with "0=unlimited". If server supports unlimited, desktop exposes it; webui does not. Could be a desktop-only feature the backend supports, or desktop could be passing invalid data. UNVERIFIED server behavior for max_uses=0. | functional |
| I7.3 | Code placeholder: webui "e.g. BETA2026"; desktop "Leave blank to auto-generate". | text |
| I7.4 | Label field placeholder: webui "e.g. Beta testers"; desktop "e.g. Friends & Family". | text |
| I7.5 | Field label: webui "Code (optional, auto-generated if empty)"; desktop "Code (optional)" — shorter hint. | text |
| I7.6 | Field label "Max uses" (lowercase u, webui) vs "Max Uses" (title case, desktop) (webui line 235). | text |
| I7.7 | No full-width Create button inside form, no "Creating..." loading state (ContentDialog chrome). Same as other dialogs. | visual / minor |
| I7.8 | No inline validation (e.g., max_uses=0 with negative value) — webui shows toast "Max uses must be a positive number". Desktop NumberBox blocks by range, but no explicit feedback. | functional minor |

## 8. Delete confirmation

### Webui (InviteCodesTab.tsx:74–87)
ConfirmDialog: title="Delete invite code", description=`Delete invite code "${code.code}"? This action cannot be undone.`, confirmLabel="Delete", variant="destructive".

### Desktop (AdminInviteCodesPage.xaml.cs:153–161)
ContentDialog: Title="Delete Invite Code" (Title Case), Content=`Delete invite code "{code.Code}"?` — **missing "This action cannot be undone." clause.**

### Gaps
| # | Gap | Severity |
|---|-----|----------|
| I8.1 | Title case: "Delete Invite Code" (desktop) vs "Delete invite code" (webui). | text |
| I8.2 | **"This action cannot be undone." missing** from desktop description. | text / functional (safety hint) |
| I8.3 | Not destructive-red styled. | visual |

## 9. Copy feedback

### Webui (InviteCodesTab.tsx:65–68)
`navigator.clipboard.writeText(text); toast.success("Copied to clipboard");`

### Desktop
**N/A — no copy button in rows.** See I4.1.

## 10. Status banner

Same pattern as Nodes/ApiKeys — inline banner on desktop; webui uses toast via hooks / `toast.success` after signup toggle (see line 52) and after other mutations (UNVERIFIED — hooks blocked).

## 11. Realtime / refetch

UNVERIFIED webui. Desktop re-loads after mutations only.

## Prioritized Fix List — Invite Codes

### P0 (critical)
- [I-P0-1] **Add Copy button to Code cell** (I4.1).
- [I-P0-2] **Add inline Switch to Status column** (I4.7). Remove Toggle icon button from Actions — match webui Switch+Badge layout.
- [I-P0-3] **Structural: remove fabricated page header / title** (I1.1, I1.2). Either integrate as a tab within an Admin Settings shell, or at minimum remove the invented "Invite codes" title + subtitle strings since webui has none.
- [I-P0-4] **Code input auto-uppercase on every keystroke** (I7.1).
- [I-P0-5] **Reconcile max_uses semantics**: either remove the 0=unlimited option to match webui (min=1) OR confirm with backend that 0 is supported — don't silently diverge (I7.2).
- [I-P0-6] **Restore "This action cannot be undone." text** in delete dialog (I8.2).

### P1
- [I-P1-1] Remove wrapping card around invite codes Table (I1.4).
- [I-P1-2] Move Create Code button to its own right-aligned row above Public Signups (I3.1).
- [I-P1-3] Public Signups Card — CornerRadius 22→12, add 1px border, title 14→16, description 12→14, padding 20→24 (I2.1–I2.5).
- [I-P1-4] Public Signups — disable Switch during pending mutation (I2.8).
- [I-P1-5] Public Signups — render `<Switch>` + separate `<Label>` showing "Enabled"/"Disabled" (match webui layout) instead of relying on ToggleSwitch on/off content (I2.7).
- [I-P1-6] Table header ALL-CAPS → Title Case, 11→14, SemiBold→Medium.
- [I-P1-7] Label cell Foreground default for non-empty labels (I4.2); FontSize 13→14 (I4.3).
- [I-P1-8] Usage FontSize 13→14 (I4.5).
- [I-P1-9] Status badge match webui variants (outline vs secondary) instead of green/grey fills (I4.8).
- [I-P1-10] Create dialog code placeholder "e.g. BETA2026"; label placeholder "e.g. Beta testers"; field label "Code (optional, auto-generated if empty)"; field label "Max uses" lowercase (I7.3–I7.6).
- [I-P1-11] Delete dialog destructive-red button; title lowercase "Delete invite code" (I8.1, I8.3).
- [I-P1-12] Replace status banner with toast; add copy feedback.
- [I-P1-13] Verify / implement realtime refresh.

### P2
- [I-P2-1] Empty state text "No invite codes yet. Create one to get started." (I5.1); size 13→14 (I5.2).
- [I-P2-2] Loading state minor alignment.
- [I-P2-3] Create form width 380→512, label/input size tuning.
- [I-P2-4] Max uses "Max uses must be a positive number" toast before submit (I7.8).

---

# Overall Summary Table

| Page | Feature | Webui | Desktop | Gap | Severity |
|------|---------|-------|---------|-----|----------|
| Nodes | Count badge bg | Badge variant="secondary" (muted) | CardBackgroundBrush = same as table card | Badge invisible | CRITICAL |
| Nodes | Check-health spin animation | RefreshCw animate-spin while pending | No animation, just disabled | No "working" feedback | P0 |
| Nodes | Realtime refresh | UNVERIFIED (hook blocked) | None beyond post-mutation reload | Likely missing | P0 (unverified) |
| Nodes | Banner color text | text-info / text-warning | SecondaryTextBrush (grey) | Info/warning semantic lost | P1 |
| Nodes | Banner corner radius | rounded-xl (12px) | 22px | Too round | P1 |
| Nodes | Card corner radius | rounded-xl (12px) | 26px | Too round | P1 |
| Nodes | Table header typography | 14 Medium muted | 12 SemiBold tertiary | Too bold/small | P1 |
| Nodes | Name cell weight | Medium (500) | SemiBold (600) | Too bold | P1 |
| Nodes | URL cell | 14 default color | 12 SecondaryText | Too small/dim | P1 |
| Nodes | Jobs cell | Plain text | Pill badge when >0 | Invented UI | P1 |
| Nodes | Action icon size | 28×28 icon 12 | 32×32 icon 14 | Too big | P1 |
| Nodes | Row trash icon color | ghost neutral | red tint | Invented red | P1 |
| Nodes | Delete destructive button | red | accent blue | Not destructive | P1 |
| Nodes | Add button label | "Add Proxy" / "Add Transcode" | "Add Proxy Node" / "Add Transcode Node" | Extra word | P1 |
| Nodes | Feedback | toast | inline banner | System-wide drift | P1 |
| Nodes | Dialog field sizes | 14 label / 14 input / rounded-md (6) | 12 label / 13 input / 8 | Too small/round | P2 |
| Nodes | Dialog loading "Saving..." | yes | no | Missing | P2 |
| Nodes | Header inner space | space-y-3 (12px) | 4px | Too tight | P2 |
| Nodes | Transcode URL hint text | "...backend server. ...or localhost is fine" | "...backend. ...fine" | Text drift | P2 |
| ApiKeys | Pagination | Prev/Next + page-size + "Showing X-Y of Z" | MISSING | Entire block absent | CRITICAL |
| ApiKeys | Copy feedback | toast "Copied to clipboard" | silent | No confirmation | CRITICAL UX |
| ApiKeys | Table header case | Title Case | UPPERCASE | Different | P1 |
| ApiKeys | Card corner radius | rounded-2xl (16px) | 26px | Too round (XAML comment lies) | P1 |
| ApiKeys | Label cell weight | Medium | SemiBold | Too bold | P1 |
| ApiKeys | User cell | 14 default | 13 Secondary | Too small/dim | P1 |
| ApiKeys | Key code text color | default | SecondaryText | Dim | P1 |
| ApiKeys | Row trash red | ghost neutral | red | Invented red | P1 |
| ApiKeys | Create dialog user default | current auth user | first in list | Wrong default | P1 |
| ApiKeys | Reveal dialog | Same dialog, state transition | Separate dialog | Jarring UX | P1 |
| ApiKeys | Revoke destructive styling | red | accent | Not destructive | P1 |
| ApiKeys | Loading state | skeleton rows | spinner | UX drift | P1 |
| ApiKeys | Feedback | toast | inline banner | System-wide drift | P1 |
| ApiKeys | Revoke title case | "Revoke API key" | "Revoke API Key" | Text drift | P2 |
| InviteCodes | Page structure | tab inside admin-settings, no title | standalone page with invented title | STRUCTURAL | CRITICAL |
| InviteCodes | Copy button in Code cell | yes | MISSING | Can't copy codes | CRITICAL |
| InviteCodes | Inline Switch in Status col | yes (Switch+Badge) | MISSING (icon btn in Actions) | Wrong interaction location | CRITICAL |
| InviteCodes | Code input auto-uppercase | yes (on each change) | no | Case-sensitivity risk | P0 |
| InviteCodes | max_uses=0 (unlimited) | not supported (min=1) | allowed (NumberBox min=0) | Divergent semantics | P0 |
| InviteCodes | Delete "cannot be undone" | yes | MISSING | Safety text lost | P0 |
| InviteCodes | Invite codes Table wrapper | bare Table | wrapped in card | Invented wrapper | P1 |
| InviteCodes | Public Signups card | rounded-xl border p-6 text-base/sm | 22px no border 20px p 14/12 | Multiple visual drifts | P1 |
| InviteCodes | Public Signups switch disabled | yes (while pending) | no | Double-click risk | P1 |
| InviteCodes | Table header case | Title Case 14 Medium | UPPERCASE 11 SemiBold | Different | P1 |
| InviteCodes | Status badge variants | outline (active) / secondary (disabled) | green fill / grey fill | Different visuals | P1 |
| InviteCodes | Create button location | separate right-aligned row above card | in page header | Different anchor | P1 |
| InviteCodes | Create placeholders / labels | "e.g. BETA2026" / "e.g. Beta testers" / "Max uses" | divergent placeholders + title-case | Text drift | P1 |
| InviteCodes | Empty state text | full sentence | truncated to half | Missing call-to-action | P2 |
| All 3 | Title/subtitle inner spacing | space-y-3 (12) | 4 | Tight | P2 |
| All 3 | Subtitle responsive size | text-sm sm:text-base | always 14 | No responsive | P2 |
| All 3 | page-shell max-width container | yes | raw 40px L/R padding | Not centered | P2 |
| All 3 | Add-type button icon | 16px + 4px gap | 13px + 6px gap | Too small + too spread | P2 |
| All 3 | Dialog field typography | 14 Medium label / 14 input / rounded-md 6 | 12 SemiBold / 13 input / 8 | Multiple drifts | P2 |
| All 3 | Dialog form width | 512 (sm:max-w-lg) | 380/420 | Tighter | P2 |
| All 3 | Dialog Save "Creating..." / "Saving..." loading | yes | no | Missing | P2 |
| All 3 | Realtime refetch behavior | UNVERIFIED (hooks blocked) | post-mutation only | Possibly missing | P0 unverified |

---

## Known unknowns (flagged for re-audit once permissions restored)

The following files were required by the original prompt but could not be read due to sandbox permission denials for `F:/continuum-server/web/src/hooks/queries/admin/*.ts`, `F:/continuum-server/web/src/components/ConfirmDialog.tsx`, `F:/continuum-server/web/src/components/ui/*` and `F:/continuum-server/web/src/app.css`:

1. `useAdminNodes`, `useCreateNode`, `useUpdateNode`, `useDeleteNode`, `useCheckNodeHealth`, `useToggleNode` — whether these use `refetchInterval`, emit `toast.success`, or subscribe to the events-channel SSE.
2. `useAdminApiKeys`, `useAdminCreateApiKey`, `useAdminDeleteApiKey`, `useAdminUpdateApiKeyTier`, `useAdminUsers` — same questions.
3. `useAdminInviteCodes`, `useCreateInviteCode`, `useUpdateInviteCode`, `useDeleteInviteCode`, `useAdminServerSettings`, `useUpdateServerSetting` — same questions.
4. `ConfirmDialog.tsx` — exact rendering of destructive variant (red button styling, layout).
5. shadcn `ui/*` primitives — exact default paddings, radii, shadow values of Button sizes ("sm"/"icon"), Card, Switch, Badge variants (secondary, outline), Table row hover.
6. `app.css` — exact CSS for `.page-shell` (max-width? centering?), `.page-header` (flex/gap/wrap?), `.page-title` (size/weight/tracking?), `.page-subtitle` (color?), `.surface-panel` (bg/shadow/blur?), `.surface-panel-subtle` (tint background?), `.text-info` / `.text-warning` / `.text-success` / `.text-destructive` / `.bg-success` / `.bg-destructive` color values.

When those files become readable, re-audit sections 2.x (toast vs banner), 7.6 (health dot colors), 13.x (realtime), and all `rounded-*` / `surface-panel*` references to replace "UNVERIFIED" assumptions with exact diffs.
