# Admin Providers + Subtitle Providers — Full Audit

> Audit date: 2026-04-15
> Scope: `AdminProvidersPage` and `AdminSubtitleProvidersPage` on the desktop client, compared against the webui.

---

## Files read (100% line-by-line)

| File | Lines | Status |
|---|---|---|
| `F:/continuum-server/web/src/pages/admin-settings/IntegrationsSettings.tsx` | 275 | Read completely |
| `F:/continuum-server/web/src/pages/AdminPlugins.tsx` | 791 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminProvidersPage.xaml` | 129 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminProvidersPage.xaml.cs` | 270 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminSubtitleProvidersPage.xaml` | 56 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Views/Admin/AdminSubtitleProvidersPage.xaml.cs` | 247 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/ViewModels/Admin/AdminProvidersViewModel.cs` | 85 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/ViewModels/Admin/AdminSubtitleProvidersViewModel.cs` | 70 | Read completely |
| `F:/ContinuumPlayer/src/ContinuumPlayer/Themes/DarkTheme.xaml` | (brush grep) | Relevant brush keys confirmed (lines 64–467) |
| `F:/ContinuumPlayer/src/ContinuumPlayer.Core/Api/AdminApi.cs` | (provider endpoints) | Verified |

## Files sandbox-blocked / non-existent

| File | Reason |
|---|---|
| `F:/continuum-server/web/src/pages/AdminProviders.tsx` | **Does not exist.** No such page in the webui — directory listing confirmed via Glob (`F:/continuum-server/web/src/pages/*.tsx`). |
| `F:/continuum-server/web/src/pages/AdminSubtitleProviders.tsx` | **Does not exist.** No such page in the webui. Subtitle providers live inside `admin-settings/IntegrationsSettings.tsx`. |
| `F:/continuum-server/web/src/app.css` | Read + Grep blocked by sandbox (same as prior audits). Tailwind utility definitions (`page-title`, `page-subtitle`, `surface-panel-subtle`, etc.) inferred from usage + prior audits. |
| `F:/continuum-server/web/src/hooks/queries/admin/subtitles.ts` | Read blocked by sandbox. Hook names (`useSubtitleProviders`, `useUpdateSubtitleProvider`, `useTestSubtitleProvider`) and their payload shapes inferred from usage in `IntegrationsSettings.tsx`. |
| `F:/continuum-server/web/src/components/ui/*` | Grep blocked. Component internals not directly inspected; class semantics inferred from usage + prior audits. |

---

# EXECUTIVE SUMMARY — READ THIS FIRST

This audit uncovered a **structural mismatch far more severe than visual drift**:

1. **`AdminProvidersPage` has NO webui counterpart.**
   The desktop built a full metadata-provider CRUD page (`/api/v1/admin/providers`) with slug/type/settings/enabled fields, but the webui has no such page. In the webui, metadata providers are not a first-class admin page — they are managed indirectly via:
   - `AdminPlugins.tsx` — installs plugins that *contribute* metadata-provider capabilities (`metadata_provider.v1`).
   - `AdminLibraries.tsx` — per-library provider-chain ordering (`/api/v1/libraries/{id}/providers`).
   The raw `/api/v1/admin/providers` endpoint is exposed by the server but intentionally not surfaced as an end-user admin page in the webui.
   **Impact:** the desktop page is a fabricated screen with no 1:1 webui parity, violating the "pixel-level clone of the webui" hard rule.

2. **`AdminSubtitleProvidersPage` is a promoted tab, not a page.**
   In the webui, subtitle providers are a section inside `admin-settings/IntegrationsSettings.tsx` (the `SubtitlesContent` component, 6 bullet items deep in an Integrations settings pane). The desktop elevated it to a full top-level page with a 42px hero title "Subtitle providers" — mirroring the legacy top-level-page pattern instead of the webui's embedded-subsection pattern. This is the same structural class of bug found in the Nodes/ApiKeys/Invites audit ("page vs tab" divergence).

3. Beyond structure, the Subtitle Providers page also misses multiple visual and functional details of the webui card (status icons vs pill badges, test result inline text, eye/eye-off show/hide toggle on API key inputs, error messaging location, enabled toggle placement, correct Save button behavior for empty fields).

Treat `AdminProvidersPage` as the P0 finding: either delete it entirely or justify its existence with explicit Mike approval. The webui has no such surface.

---

# PART 1 — AdminProviders (Metadata Providers)

## 1.0 Existence

- **Webui:** No page. Searched `F:/continuum-server/web/src/pages/` via Glob and found no `AdminProviders*.tsx`. The closest webui surface is `AdminPlugins.tsx` (an entirely different concept — plugin catalog, installation, repository management). `AdminLibraries.tsx` also contains per-library provider-chain controls, but those use a different endpoint (`/libraries/{id}/providers`) and UI pattern.
- **Desktop:** Full standalone `AdminProvidersPage` rendering CRUD against `/api/v1/admin/providers` (AdminApi.cs:355–365).
- **Gap:** Entire page is fabricated. The desktop client has invented a first-class admin page that does not exist in the webui.
- **Severity:** **CRITICAL — structural / 1:1-clone violation.**

## 1.1 Page header

### Webui

No webui header exists (no page). Nearest cognate is `AdminPlugins.tsx:688–705`:
```
<div class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
  <div class="space-y-3">
    <h1 class="page-title text-[clamp(2rem,4vw,3rem)]">Plugins</h1>
    <p class="page-subtitle text-sm sm:text-base">
      Extend Continuum with community and first-party plugins.
    </p>
  </div>
  <Button variant="outline" ...>Check for updates</Button>
</div>
```
- Title: `text-[clamp(2rem,4vw,3rem)]` ≈ 32–48px responsive clamp, `page-title` utility (CSS UNVERIFIED — blocked) typically adds weight 700, tight tracking, primary text color.
- Subtitle: `text-sm sm:text-base` = 14px mobile / 16px ≥640px, `page-subtitle` typically uses muted foreground.
- Header spacing: `space-y-3` = 12px between title and subtitle.
- Right side button: `variant="outline"` (bordered, transparent bg).

### Desktop (AdminProvidersPage.xaml:52–79)

```
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="Auto" />
  </Grid.ColumnDefinitions>
  <StackPanel Grid.Column="0" Spacing="4">
    <TextBlock Text="Metadata providers" FontSize="42" FontWeight="Bold"
               Foreground="{StaticResource PrimaryTextBrush}" />
    <TextBlock Text="Configure external metadata sources..."
               FontSize="14" Foreground="{StaticResource SecondaryTextBrush}" />
  </StackPanel>
  <Button x:Name="CreateButton" Grid.Column="1"
          Style="{StaticResource AccentButtonStyle}" ...>
    <StackPanel Orientation="Horizontal" Spacing="6">
      <FontIcon Glyph="&#xE710;" FontSize="13" />
      <TextBlock Text="Add Provider" />
    </StackPanel>
  </Button>
</Grid>
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.1.a | **No webui page exists** — entire header is a fabrication. | critical |
| 1.1.b | Title Spacing=4px (between title & subtitle) vs webui cognate `space-y-3`=12px. | visual |
| 1.1.c | Add button uses `AccentButtonStyle` (filled accent). Webui cognate (`AdminPlugins.tsx:697`) uses `variant="outline"` for the header-right action. | visual |
| 1.1.d | "Add Provider" glyph E710 is Segoe's generic plus. Webui uses lucide `Plus` icon at `mr-1.5 h-3.5 w-3.5` (14px icon, 6px right margin). Visual match acceptable; size 13px vs 14px off by 1. | visual |

## 1.2 Loading state

### Webui
No equivalent. `AdminPlugins.tsx:674–686` shows a centered plain-text "Loading plugins..." inside the normal page header block (header still rendered). No ProgressRing.

### Desktop (AdminProvidersPage.xaml:20–25)
Centered ProgressRing at 48×48, completely replaces page content (the header disappears).

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.2.a | Loading state hides the whole page (including header). Webui keeps header visible and only swaps the body. | visual |
| 1.2.b | Uses ProgressRing spinner vs webui's plain text "Loading plugins..." or `Skeleton` rows. | visual |

## 1.3 Error state

### Webui
Not present on this surface (page doesn't exist). Peer pages (`AdminPlugins.tsx`) do not show an error overlay — errors surface via toast / mutation state.

### Desktop (AdminProvidersPage.xaml:27–44)
Center-screen StackPanel with red text, Retry button, replaces entire page.

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.3.a | Whole-page error overlay has no webui precedent. | visual |

## 1.4 Status banner (transient success toast)

### Webui
Not present; webui uses shadcn `toast()` / `sonner` notifications (not inspected, but mutation success patterns throughout `AdminPlugins.tsx` and `IntegrationsSettings.tsx` use invisible success — the mutation state flips from "Saving..." back to "Save").

### Desktop (AdminProvidersPage.xaml:82–92, .cs:262–269)
Accent-colored inline banner at top of content, `CornerRadius="8" Padding="14,10"`, hides after 4 seconds via DispatcherTimer.

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.4.a | Inline success banner instead of toast; webui has no such banner anywhere. | visual |
| 1.4.b | 4-second auto-dismiss without a close button; no dismiss control. | functional |

## 1.5 Table container / card

### Webui
No table equivalent. `AdminPlugins.tsx:547` uses:
```
<div class="divide-border surface-panel-subtle divide-y overflow-hidden rounded-xl">
```
- `surface-panel-subtle`: muted translucent card (UNVERIFIED exact CSS — sandbox-blocked). Prior audits established this as `bg-[--surface-subtle]` with a subtle 1px border.
- `rounded-xl` = 12px radius.
- `divide-y` = horizontal 1px divider between children.

### Desktop (AdminProvidersPage.xaml:95–125)
```
<Border Background="{StaticResource CardBackgroundBrush}"
        CornerRadius="26" BorderThickness="0" Padding="0">
```
- `CardBackgroundBrush` (DarkTheme.xaml:66) — opaque card background.
- **CornerRadius=26** (vs webui's 12px `rounded-xl`).
- BorderThickness=0 (webui: 1px border via `surface-panel-subtle`).

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.5.a | Corner radius 26px vs webui 12px — over double. Every other desktop panel in the app uses 20–26 for cards; webui standard here is 12. | visual |
| 1.5.b | No border (`BorderThickness="0"`). Webui `surface-panel-subtle` class includes a subtle border. | visual |
| 1.5.c | Uses solid `CardBackgroundBrush` vs webui's translucent `surface-panel-subtle`. | visual |

## 1.6 Table header row

### Webui
No equivalent.

### Desktop (AdminProvidersPage.xaml:101–114)
5-column grid: SLUG (2*), TYPE (1.5*), STATUS (80px), SETTINGS (3*), ACTIONS (90px).
- FontSize=11, FontWeight=SemiBold, `TertiaryTextBrush`, UPPERCASE.
- Padding `20,14,20,14`, ColumnSpacing 12.
- Followed by `<Border BorderBrush BorderThickness="0,1,0,0" />` hairline divider.

### Gaps
- Entire concept of a column-header table row is desktop-invented. Webui uses a divided card list pattern, not a table header.
- Uppercase header convention is common in prior desktop admin pages, so this is consistent internally — but **inconsistent with the webui pattern** (which never uses table-style column headers for short CRUD lists of <100 rows).
- Severity: **visual/structural**.

## 1.7 Row rendering

### Webui
No equivalent. Cognate row pattern in `AdminPlugins.tsx:547–583` (repositories):
```
<div class="flex flex-col gap-3 px-5 py-3.5 sm:flex-row sm:items-center sm:justify-between">
  <div class="min-w-0 space-y-0.5">
    <div class="flex items-center gap-2">
      <p class="truncate text-sm font-medium">{repo.display_name}</p>
      <span class={`inline-block h-1.5 w-1.5 rounded-full ${repo.enabled ? "bg-success" : "bg-muted-foreground"}`} />
    </div>
    <p class="text-muted-foreground truncate font-mono text-xs">{repo.url}</p>
  </div>
  <div class="flex shrink-0 gap-2">
    <Button variant="outline" size="xs">{repo.enabled ? "Disable" : "Enable"}</Button>
    <Button variant="ghost" size="xs" class="text-muted-foreground hover:text-destructive">
      <Trash2 class="h-3 w-3" />
    </Button>
  </div>
</div>
```
- `px-5 py-3.5` = 20px / 14px padding.
- Status shown as a 6×6px color **dot**, not a pill.
- Actions are `size="xs"` buttons labeled "Enable"/"Disable", not icon-only.

### Desktop (AdminProvidersPage.xaml.cs:55–126)
5-column grid matching header: SLUG / TYPE / STATUS pill / SETTINGS (masked with `***`) / ACTIONS (edit icon, delete icon).
- Padding 20,14,20,14 (matches webui cognate).
- Status as pill: `MakeBadge` with 40α bg + 255α fg colors.
  - Enabled: `rgb(34,197,94)` green (matches Tailwind `green-500`).
  - Disabled: `rgb(160,160,160)` grey (vs webui `bg-muted-foreground` dot — color roughly matches).
- Settings column: `string.Join(", ", keys.Select(k => $"{k}: ***"))`.
- Edit button: glyph `\uE70F` 28×28, transparent bg.
- Delete button: glyph `\uE74D` 28×28, fg color `rgb(220,90,90)`.

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.7.a | Status shown as pill badge instead of webui's 6px color dot (per-peer-page convention). | visual |
| 1.7.b | Actions are icon-only (`\uE70F` edit, `\uE74D` delete). Webui uses labeled "Enable/Disable" + icon-only ghost Trash2 — more discoverable. | visual |
| 1.7.c | Custom hard-coded red `#DC5A5A` for delete icon — no `DestructiveBrush` in theme. Webui uses `text-destructive` Tailwind token. | visual |
| 1.7.d | Settings column masks values as `key: ***` — reveals key names. Webui plugin pattern hides config entirely until Configure dialog opens. | functional/privacy |
| 1.7.e | Row separator is a `<Border BorderThickness="0,1,0,0" />` element inserted between rows (AdminProvidersPage.xaml.cs:45–49). Webui uses `divide-y` CSS class on parent (continuous 1px divider, no gap). Desktop approach works but adds extra XAML elements. | visual (minor) |
| 1.7.f | No hover state on rows (webui plugin rows get `hover:bg-accent` via `surface-panel-subtle group` classes). Desktop row has zero hover feedback. | visual |
| 1.7.g | No per-row enable/disable toggle inline. Webui repos show "Enable"/"Disable" buttons directly. Desktop requires opening Edit dialog to toggle. | functional |

## 1.8 Empty state

### Webui cognate (`AdminPlugins.tsx:586–590`):
```
<p class="text-muted-foreground py-4 text-center text-sm">
  No repositories configured. Add one to browse available plugins.
</p>
```
- `py-4` = 16px vertical padding.
- `text-sm` = 14px.
- `text-muted-foreground` = muted grey.

### Desktop (AdminProvidersPage.xaml:117–123)
```
<Border x:Name="EmptyState" Padding="20,40" Visibility="Collapsed">
  <TextBlock Text="No metadata providers configured." FontSize="13"
             Foreground="{StaticResource TertiaryTextBrush}"
             HorizontalAlignment="Center" />
</Border>
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 1.8.a | Padding `20,40` (40px vertical) vs webui `py-4` (16px). 2.5× more vertical space. | visual |
| 1.8.b | FontSize=13 vs webui `text-sm`=14. | visual |
| 1.8.c | `TertiaryTextBrush` used; webui `text-muted-foreground` is more like `SecondaryTextBrush`. | visual |

## 1.9 Create / Edit dialogs

### Webui
No webui dialog equivalent for `/admin/providers`. Cognate dialog is `AdminPlugins.tsx:224–425` (ConfigureDialog) but that is for configuring *installed plugins*, with accordion sections for global config, auth bindings, task bindings, admin routes.

### Desktop Create (AdminProvidersPage.xaml.cs:128–168)
- ContentDialog with title "Add Metadata Provider".
- Fields: Slug (TextBox), Provider Type (TextBox), Settings (multi-line TextBox, key=value).
- PrimaryButton "Create", CloseButton "Cancel".
- Uses `AddField` helper with 12px SemiBold label + 6px gap between label and control.
- Dialog content Width=380.
- `ParseKeyValues` splits on `\n`, expects `key=value`.

### Desktop Edit (AdminProvidersPage.xaml.cs:170–211)
- Adds Enabled ToggleSwitch, Slug is read-only.
- Settings field pre-populated with `key=value` lines.

### Gaps (vs webui conventions)
| # | Gap | Severity |
|---|---|---|
| 1.9.a | Entire dialog is fabricated — no webui analog. | critical |
| 1.9.b | Settings as free-form `key=value` textarea is an unusual webui pattern. Webui typically uses typed schema-driven `PluginConfigForm` (`AdminPlugins.tsx:43`, imported from `components/admin/plugins/PluginConfigForm`). | functional |
| 1.9.c | No validation beyond "slug not empty". No regex/slug-format enforcement, no type enum validation, no duplicate-slug check. | functional |
| 1.9.d | Delete confirmation dialog text is generic `Delete provider "{slug}"?` — missing the "This action cannot be undone" / "All associated data will be removed" safety clause that peer pages in both platforms include. | functional |
| 1.9.e | No inline error display inside the dialog — errors bubble to page-level `ErrorMessage`, which is hidden by the dialog overlay. | functional |
| 1.9.f | Dialog Width=380 is arbitrary; webui dialogs use `sm:max-w-2xl` (672px) per `AdminPlugins.tsx:251`. | visual |

## 1.10 Icon glyph choices

Segoe MDL2 glyphs used:
- `\uE710` = plus (header Add button).
- `\uE70F` = edit pencil.
- `\uE74D` = delete bin.

Webui uses lucide icons: `Plus`, `Trash2`, `Settings2`, `Shield`, `Download`, `Upload`, `ExternalLink`, `Blocks`, `Package`, `CircleDot`, `Loader2`, `X`, `CircleCheck`, `CircleAlert`, `Eye`, `EyeOff`. None of the Segoe glyphs are a visual equivalent to lucide's stroke-style icons — lucide is typically 1.5px stroke, transparent fill; Segoe is filled/semi-filled.

- **Severity:** visual (established project-wide convention; not unique to this page).

## 1.11 Accessibility

- Desktop buttons have ToolTipService tooltips for edit/delete (AdminProvidersPage.xaml.cs:258) — good.
- Webui uses `htmlFor` Label associations on inputs — no equivalent on the desktop dialog (labels are plain TextBlocks, not associated with controls).
- **Severity:** functional (minor).

## Prioritized Fix List — AdminProviders

### P0 (structural / must decide first)
1. **Decide whether `AdminProvidersPage` should exist at all.** The webui has no such page. Options:
   - **Delete** the page and remove its route/sidebar entry. Metadata providers are managed via plugins.
   - **Keep** the page but demote to parity with the `AdminPlugins` "repositories" section pattern: muted `surface-panel-subtle` card, 12px rounded, divided list, inline enable/disable buttons, no settings-key masking, no fabricated status banner.
   - Flag this to Mike for explicit approval per the "No appearance changes" hard rule.

### P1 (visible 1:1 clone violations, conditional on P0)
2. Remove Status banner; adopt toast pattern instead (consistent with peer admin pages).
3. Replace StatusPill with the 6px color dot + label pattern from `AdminPlugins.tsx:557`.
4. Change card CornerRadius 26 → 12, add 1px border.
5. Remove table header row entirely; adopt plain divided-list layout.
6. Change icon-only action buttons to labeled "Enable"/"Disable" + ghost Trash2 icon.
7. Remove loading overlay; show skeleton rows inline while keeping header visible.
8. Remove error overlay; surface mutation errors per-row via toast.
9. Add delete confirmation safety clause ("This action cannot be undone…").
10. Widen Create/Edit dialog from 380 → ~672 (to match `sm:max-w-2xl`) or adopt schema-driven `PluginConfigForm`-equivalent.

### P2 (small visual polish)
11. Empty state `py-4` (16px) not `20,40` (40px); FontSize 14 not 13; SecondaryText not Tertiary.
12. Title/subtitle Spacing 4 → 12.
13. Add Provider button → outline variant (not accent).
14. Add hover state on rows.

---

# PART 2 — AdminSubtitleProviders

## 2.0 Existence

- **Webui:** No top-level page. `SubtitlesContent` section embedded in `admin-settings/IntegrationsSettings.tsx:215–262`, which is itself a tab within `/admin/settings` (the server Settings layout). The outer header says "Integrations" (`IntegrationsSettings.tsx:268`) with `page-subtitle`-class subtitle "Subtitle providers".
- **Desktop:** Full top-level `AdminSubtitleProvidersPage` with 42px "Subtitle providers" hero.
- **Gap:** Desktop elevated an embedded subsection into a first-class page.
- **Severity:** **CRITICAL — same "page vs tab" class bug as AdminNodes/ApiKeys/Invites.**

## 2.1 Page header

### Webui (`IntegrationsSettings.tsx:264–273`)
```
<div class="flex h-full flex-col">
  <div class="mb-6">
    <h2 class="text-lg font-semibold">Integrations</h2>
    <p class="text-muted-foreground text-sm">Subtitle providers</p>
  </div>
  <SubtitlesContent />
</div>
```
- Title: `h2` with `text-lg` = 18px, `font-semibold` = 600.
- Subtitle: `text-sm` = 14px, muted.
- **Title is "Integrations" — "Subtitle providers" is the subtitle.**
- Mb-6 = 24px below header.

### Desktop (AdminSubtitleProvidersPage.xaml:38–41)
```
<StackPanel Spacing="4">
  <TextBlock Text="Subtitle providers" FontSize="42" FontWeight="Bold"
             Foreground="{StaticResource PrimaryTextBrush}" />
  <TextBlock Text="Configure external subtitle download sources."
             FontSize="14" Foreground="{StaticResource SecondaryTextBrush}" />
</StackPanel>
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.1.a | **Title is "Subtitle providers" (42px Bold) — should be "Integrations" (18px SemiBold) with "Subtitle providers" as the 14px subtitle.** | critical |
| 2.1.b | Title size 42 vs webui `text-lg` 18 — **24px too large.** | critical (visual) |
| 2.1.c | Subtitle text differs: "Configure external subtitle download sources." vs webui "Subtitle providers". Additionally, the section-body intro paragraph "Configure external subtitle search providers. Credentials are stored securely and never returned by the API." (`IntegrationsSettings.tsx:245–248`) is **missing entirely** on desktop. | visual + informational |
| 2.1.d | No accompanying integrations/settings chrome (e.g., in webui this is embedded inside `SettingsLayout.tsx` with a left sidebar of settings categories). Desktop treats it as a first-class standalone page. | structural |

## 2.2 Body intro paragraph

### Webui (`IntegrationsSettings.tsx:245–248`)
```
<p class="text-muted-foreground max-w-3xl text-sm">
  Configure external subtitle search providers. Credentials are stored securely and never
  returned by the API.
</p>
```
- `max-w-3xl` = 768px max width.
- `text-sm` = 14px.
- `text-muted-foreground`.

### Desktop
**Missing.**

### Gap
| # | Gap | Severity |
|---|---|---|
| 2.2.a | Intro paragraph explaining credential security is absent. Informational safety message about credential handling missing. | functional (informational safety) |

## 2.3 Provider list container

### Webui (`IntegrationsSettings.tsx:250–259`)
```
<div class="max-w-2xl space-y-4">
  {sorted.map((provider) => (<SubtitleProviderCard key={...} config={provider} />))}
  {sorted.length === 0 && (<div class="border-border bg-surface rounded-lg border px-5 py-4">
    <p class="text-muted-foreground text-sm">No subtitle providers configured.</p>
  </div>)}
</div>
```
- `max-w-2xl` = 672px maximum width, so the whole card column is capped at 672px.
- `space-y-4` = 16px vertical gap between cards.

### Desktop (AdminSubtitleProvidersPage.xaml:48)
```
<StackPanel x:Name="ProvidersPanel" Spacing="12" />
```
- **No max-width constraint** — card stretches full width of scroll viewer minus 40px page padding (likely 1400px+ on a wide monitor).
- Spacing=12 vs webui `space-y-4`=16.

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.3.a | **Missing max-width 672px.** Cards are enormous on wide monitors. | visual (major) |
| 2.3.b | Spacing 12 vs 16 between cards. | visual |

## 2.4 Sort order

### Webui (`IntegrationsSettings.tsx:213, 234–241`)
```
const SUBTITLE_PROVIDER_ORDER = ["opensubtitles", "subdl", "subsource"];
const sorted = [...providers].sort((a, b) => {
  const ai = SUBTITLE_PROVIDER_ORDER.indexOf(a.provider_name);
  const bi = SUBTITLE_PROVIDER_ORDER.indexOf(b.provider_name);
  if (ai === -1 && bi === -1) return 0;
  if (ai === -1) return 1;
  if (bi === -1) return -1;
  return ai - bi;
});
```

### Desktop (AdminSubtitleProvidersPage.xaml.cs:25, 54–63)
```csharp
private static readonly List<string> ProviderOrder = new() { "opensubtitles", "subdl", "subsource" };
sorted.Sort((a, b) => {
  int ai = ProviderOrder.IndexOf(a.ProviderName?.ToLowerInvariant() ?? "");
  int bi = ProviderOrder.IndexOf(b.ProviderName?.ToLowerInvariant() ?? "");
  if (ai == -1 && bi == -1) return 0;
  if (ai == -1) return 1;
  if (bi == -1) return -1;
  return ai - bi;
});
```

**Match.** Desktop additionally lowercases provider names (webui does not, but provider_name from API is always lowercase by convention — so functionally equivalent).

## 2.5 Provider card container

### Webui (`IntegrationsSettings.tsx:114`)
```
<div class="border-border bg-surface space-y-4 rounded-lg border px-5 py-4">
```
- `rounded-lg` = 8px radius.
- `border` + `border-border` = 1px subtle border.
- `bg-surface` = surface color (slightly raised card).
- `px-5 py-4` = 20px horizontal / 16px vertical padding.
- `space-y-4` = 16px vertical spacing between inner children.

### Desktop (AdminSubtitleProvidersPage.xaml.cs:71–80)
```csharp
var card = new Border {
    Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
    CornerRadius = new CornerRadius(20),
    Padding = new Thickness(20, 16, 20, 16)
};
var layout = new StackPanel { Spacing = 12 };
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.5.a | **CornerRadius 20 vs webui 8 (`rounded-lg`).** Massive rounded-pill look instead of subtle rounded rectangle. | visual (major) |
| 2.5.b | No border (desktop relies on Background contrast only). Webui has explicit 1px `border-border`. | visual |
| 2.5.c | Inner Spacing=12 vs webui `space-y-4`=16. | visual |
| 2.5.d | Padding matches (20 horizontal, 16 vertical = `px-5 py-4`). OK. | — |

## 2.6 Card header row — layout + status + toggle

### Webui (`IntegrationsSettings.tsx:116–133`)
```
<div class="flex items-center justify-between">
  <div class="flex items-center gap-3">
    <span class="text-sm font-semibold">{displayName}</span>
    <SubtitleCredentialStatus configured={isOpenSubtitles ? config.has_credentials : config.has_api_key} />
  </div>
  <div class="flex items-center gap-2">
    <Label htmlFor={...} class="text-sm font-medium">
      {form.enabled ? "Enabled" : "Disabled"}
    </Label>
    <Switch id={...} checked={form.enabled} onCheckedChange={...} />
  </div>
</div>
```

**Structure:** left side has provider name + credential status; right side has "Enabled"/"Disabled" label + Switch. **One row.** No Test button here. Enabled toggle reacts live and is saved on Save.

#### `SubtitleCredentialStatus` (IntegrationsSettings.tsx:49–64):
```
<span class="text-muted-foreground inline-flex items-center gap-1 text-xs">
  <CircleCheck class="h-3.5 w-3.5 text-green-500" />  // or CircleAlert h-3.5 w-3.5 text-yellow-500
  Configured    // or "Not configured"
</span>
```
- Icon 14px (`h-3.5 w-3.5`).
- Text `text-xs` = 12px, `text-muted-foreground`.
- CircleCheck icon color `text-green-500` = `rgb(34,197,94)`.
- CircleAlert icon color `text-yellow-500` = `rgb(234,179,8)`.
- Icon + text are `inline-flex` with `gap-1` = 4px.
- **Icon is a lucide stroke icon, not a filled background pill.**

### Desktop (AdminSubtitleProvidersPage.xaml.cs:83–137)
```csharp
var headerRow = new Grid { ColumnSpacing = 12 };
// col0 = name + status badges, col1 = Test button

var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, ... };
nameRow.Children.Add(new TextBlock {
    Text = displayName, FontSize = 16, FontWeight = FontWeights.SemiBold,
    Foreground = PrimaryTextBrush
});

if (configured)
    nameRow.Children.Add(MakeBadge("Configured", 40α rgb(34,197,94), 255α rgb(34,197,94)));
else
    nameRow.Children.Add(MakeBadge("Not configured", 40α rgb(234,179,8), 255α rgb(234,179,8)));

if (provider.Enabled)
    nameRow.Children.Add(MakeBadge("Enabled", 40α rgb(59,130,246), 255α rgb(96,165,250)));
else
    nameRow.Children.Add(MakeBadge("Disabled", 40α rgb(120,120,120), 255α rgb(160,160,160)));

// Test button in col1
var testBtn = new Button { Content = "Test", Style = OutlineButtonStyle, ... };
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.6.a | **Test button is in the header row.** In the webui, Test Connection is in the actions row at the bottom, next to Save. | visual/structural |
| 2.6.b | Display name FontSize=16 vs webui `text-sm font-semibold` = 14px. **+2px.** | visual |
| 2.6.c | **"Configured"/"Not configured" rendered as a filled pill badge** (8×2 padding, 40α background). Webui uses a lucide `CircleCheck`/`CircleAlert` icon (14px stroke icon in green/yellow) followed by plain 12px muted text, no background pill. | visual (major) |
| 2.6.d | **"Enabled"/"Disabled" rendered as a separate badge in the header.** Webui has no Enabled *badge* — it uses a **Switch** (toggle) on the right side of the header, with a dynamic "Enabled"/"Disabled" **label** beside it (`Label` of `text-sm font-medium`). Desktop's badge is static (reflects server state) and the actual toggle is moved to a ToggleSwitch further down in the layout (line 140). | functional (major) |
| 2.6.e | Blue "Enabled" badge color rgb(96,165,250) has no webui cognate — pure invention. | visual |
| 2.6.f | `headerRow` uses Grid with two columns + Test button. Webui uses `flex items-center justify-between` — functionally similar but semantically different (web has label+switch on right, desktop has Test button on right). | structural |

## 2.7 Enabled toggle placement

### Webui
Inline in header row (see 2.6), bound to `form.enabled` state. Live-updates on click. Saved only when Save pressed.

### Desktop (AdminSubtitleProvidersPage.xaml.cs:140–141)
```csharp
var enabledToggle = new ToggleSwitch { IsOn = provider.Enabled, Header = "Enabled" };
layout.Children.Add(enabledToggle);
```
- ToggleSwitch as a separate row between header and credential fields.
- `Header = "Enabled"` (text above the switch).

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.7.a | Toggle placed in body (below header) instead of in header-right. | structural |
| 2.7.b | WinUI ToggleSwitch renders with `Header` text *above* the switch; webui label is *beside* the switch. | visual |
| 2.7.c | Header="Enabled" is static ("Enabled" always) — does not flip to "Disabled" when off like webui does (`{form.enabled ? "Enabled" : "Disabled"}`). | functional |

## 2.8 Credential fields — OpenSubtitles (username + password)

### Webui (`IntegrationsSettings.tsx:136–166`)
```
<div class="space-y-1">
  <Label htmlFor={`${providerName}-username`} class="text-sm font-medium">Username</Label>
  <Input id={...} type="text" placeholder={config.has_credentials ? "Leave blank to keep current" : "OpenSubtitles username"} value={...} onChange={...} />
</div>
<div class="space-y-1">
  <Label htmlFor={`${providerName}-password`} class="text-sm font-medium">Password</Label>
  <Input id={...} type="password" placeholder={config.has_credentials ? "Leave blank to keep current" : "OpenSubtitles password"} value={...} onChange={...} />
</div>
```
- `space-y-1` = 4px gap between label and input.
- Label text `text-sm font-medium` = 14px weight 500.
- Input is shadcn `<Input>` (full-width by default).

### Desktop (AdminSubtitleProvidersPage.xaml.cs:149–179)
```csharp
var usernameBox = new TextBox { PlaceholderText = ..., CornerRadius = 8, FontSize = 13 };
var passwordBox = new PasswordBox { PlaceholderText = ..., CornerRadius = 8, FontSize = 13 };

// Grouped with 4px spacing and 12px SemiBold Secondary-colored label
var userGroup = new StackPanel { Spacing = 4 };
userGroup.Children.Add(new TextBlock {
    Text = "Username", FontSize = 12, FontWeight = FontWeights.SemiBold,
    Foreground = SecondaryTextBrush
});
userGroup.Children.Add(usernameBox);
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.8.a | Label FontSize=12 vs webui `text-sm`=14. **-2px.** | visual |
| 2.8.b | Label FontWeight=SemiBold (600) vs webui `font-medium`=500. | visual |
| 2.8.c | Label uses `SecondaryTextBrush` vs webui default `text-foreground` (primary). | visual |
| 2.8.d | Label-to-input gap 4px matches webui `space-y-1`=4px. ✓ | — |
| 2.8.e | No `htmlFor`/`AutomationProperties.LabeledBy` linkage. | accessibility |

## 2.9 Credential field — non-OpenSubtitles (API key) with show/hide

### Webui (`IntegrationsSettings.tsx:168–191`)
```
<div class="space-y-1">
  <Label htmlFor={`${providerName}-api-key`} class="text-sm font-medium">API Key</Label>
  <div class="flex items-center gap-2">
    <Input id={...} type={form.showApiKey ? "text" : "password"}
           placeholder={config.has_api_key ? "Leave blank to keep current" : "Enter API key"}
           value={form.api_key} onChange={...} class="flex-1" />
    <Button variant="ghost" size="icon" type="button" onClick={toggle showApiKey}>
      {form.showApiKey ? <EyeOff class="h-4 w-4" /> : <Eye class="h-4 w-4" />}
    </Button>
  </div>
</div>
```

### Desktop (AdminSubtitleProvidersPage.xaml.cs:144–148, 181–190)
```csharp
var apiKeyBox = new PasswordBox {
    PlaceholderText = provider.HasApiKey ? "Leave blank to keep current" : "Enter API key",
    CornerRadius = new CornerRadius(8), FontSize = 13
};
// ...
var apiKeyGroup = new StackPanel { Spacing = 4 };
apiKeyGroup.Children.Add(new TextBlock {
    Text = "API Key", FontSize = 12, FontWeight = FontWeights.SemiBold, ...
});
apiKeyGroup.Children.Add(apiKeyBox);
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.9.a | **No Eye / EyeOff show/hide toggle button.** Webui allows toggling the API key to visible text for easier verification. **Functional gap.** | functional |
| 2.9.b | Desktop uses WinUI `PasswordBox` (always masked). Webui uses `<Input type="password">` that swaps to `type="text"` when eye-toggle pressed. | functional |
| 2.9.c | Same label sizing gaps as 2.8.a–c (FontSize 12 vs 14, SemiBold vs medium, Secondary vs primary). | visual |
| 2.9.d | No horizontal flex container holding input + button side-by-side. | visual |

## 2.10 Actions row — Test Connection + Save + inline result

### Webui (`IntegrationsSettings.tsx:193–208`)
```
<div class="flex items-center gap-3 pt-1">
  <Button variant="outline" onClick={handleTest} disabled={testProvider.isPending}>
    {testProvider.isPending ? "Testing..." : "Test Connection"}
  </Button>
  <Button onClick={handleSave} disabled={updateProvider.isPending}>
    {updateProvider.isPending ? "Saving..." : "Save"}
  </Button>
  {testResult !== null && (
    <span class={`text-sm ${testResult.success ? "text-green-500" : "text-red-500"}`}>
      {testResult.success ? "Connection successful" : (testResult.error ?? "Connection failed")}
    </span>
  )}
</div>
```
- Row gap `gap-3` = 12px, top padding `pt-1` = 4px.
- **Test Connection button** — outline variant, label "Test Connection" (not "Test"). On pending: "Testing…".
- **Save** — default/primary variant. On pending: "Saving…".
- **Inline test result text** — green on success "Connection successful", red on failure "{error}" or "Connection failed".

### Desktop (AdminSubtitleProvidersPage.xaml.cs:115–137, 192–223)
- Test button lives **in the header** (2.6), not in the actions row.
- Test button label transitions: "Test" → "Testing..." → "Passed"/"Failed" → (2-sec delay) → "Test".
- Save button is in actions row alone:
  ```csharp
  var saveBtn = new Button { Style = AccentButtonStyle, Padding = 16,8,16,8, Content = TextBlock "Save" };
  ```
- No inline test-result text; result surfaces via the 4-second StatusBanner at top-of-page.

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.10.a | **Test button not in actions row.** Webui shows it inline beside Save. | structural |
| 2.10.b | Test button label "Test" vs webui **"Test Connection"**. | visual |
| 2.10.c | "Testing..." state matches. ✓ | — |
| 2.10.d | On result, desktop flips content to "Passed"/"Failed" **for 2 seconds then resets**. Webui persists the result as inline text ("Connection successful" / "{error}") until the next test or save — more informative and durable. | functional (major) |
| 2.10.e | Desktop button-disabled state uses `testBtn.IsEnabled = false`. Webui uses `disabled={testProvider.isPending}`. Functionally similar. | — |
| 2.10.f | Save button has no pending/saving visible state ("Saving…" text not shown). Only `saveBtn.IsEnabled = false` for the request duration. | functional |
| 2.10.g | Save uses `AccentButtonStyle`; webui Save uses default primary. Visually similar. Save has padding `16,8,16,8`; webui button default padding differs. | visual (minor) |
| 2.10.h | No inline error text next to action buttons. Webui shows red "{error}" or "Connection failed" inline on test failure. Desktop shows it only via the StatusBanner (4-sec). | functional |

## 2.11 Loading state (card level)

### Webui (`IntegrationsSettings.tsx:218–229`)
```
<div class="space-y-6" role="status" aria-label="Loading settings">
  <Skeleton class="h-8 w-48" />  {/* title skeleton */}
  <div class="space-y-4">
    <Skeleton class="h-32 w-full" />
    <Skeleton class="h-32 w-full" />
    <Skeleton class="h-32 w-full" />
  </div>
  <span class="sr-only">Loading settings</span>
</div>
```
- Three 128px-tall skeletons (one per known provider) + a title skeleton.
- Screen-reader live region.

### Desktop (AdminSubtitleProvidersPage.xaml:20–23)
Centered 48×48 ProgressRing replacing entire page body (header disappears).

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.11.a | **No skeleton cards.** Desktop shows a generic spinner. | visual |
| 2.11.b | Loading overlay replaces header too (webui keeps header visible). | visual |
| 2.11.c | No ARIA / accessibility announcement. | accessibility |

## 2.12 Error state

### Webui
No page-level error UI in `IntegrationsSettings.tsx`. Errors surface via mutation error handlers (e.g., `onError` in `handleTest`) or tanstack-query's built-in error states — not a full-page takeover.

### Desktop (AdminSubtitleProvidersPage.xaml:25–31)
Full-screen StackPanel with red text and Retry button, covers page when `ErrorMessage != null`.

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.12.a | Full-page error takeover has no webui analog. | visual |

## 2.13 Empty state

### Webui (`IntegrationsSettings.tsx:254–258`)
```
<div class="border-border bg-surface rounded-lg border px-5 py-4">
  <p class="text-muted-foreground text-sm">No subtitle providers configured.</p>
</div>
```
- Renders as a **provider-card-shaped placeholder** (same border/bg/padding as a real card).

### Desktop (AdminSubtitleProvidersPage.xaml:50–52)
```
<Border x:Name="EmptyState" Padding="20,40" Visibility="Collapsed">
  <TextBlock Text="No subtitle providers configured." FontSize="14"
             Foreground="{StaticResource SecondaryTextBrush}"
             HorizontalAlignment="Center" />
</Border>
```

### Gaps
| # | Gap | Severity |
|---|---|---|
| 2.13.a | Desktop empty state has no border/background — just centered text in a bare Border. Webui renders a card-shaped empty state (rounded-lg, border, bg-surface, px-5 py-4). | visual |
| 2.13.b | Padding `20,40` (40px vertical) vs webui `px-5 py-4` (16px vertical). | visual |
| 2.13.c | Text centered on desktop; webui left-aligned inside the card. | visual |

## 2.14 Status banner (page-level)

### Webui
Not present.

### Desktop (AdminSubtitleProvidersPage.xaml:43–45, .cs:239–246)
Accent-colored inline banner, 4-second auto-dismiss.

### Gap
| # | Gap | Severity |
|---|---|---|
| 2.14.a | Same fabricated banner pattern as Providers page. Webui shows test results **inline in the actions row** and save results via toast. | visual/functional |

## 2.15 Save request payload

### Webui (`IntegrationsSettings.tsx:86–96`)
```js
updateProvider.mutate({
  provider: providerName,
  config: {
    enabled: form.enabled,
    ...(isOpenSubtitles
      ? { username: form.username, password: form.password }
      : { api_key: form.api_key }),
  },
});
```
**Key behavior:** when the user leaves a field blank (to keep current credential), webui still sends `username: ""`, `password: ""`, or `api_key: ""`. The server must treat empty string as "don't change". (UNVERIFIED — subtitles.ts hook is read-blocked.)

### Desktop (AdminSubtitleProvidersPage.xaml.cs:206–219)
```csharp
var request = new SubtitleProviderUpdateRequest {
  Enabled = enabledToggle.IsOn,
  ApiKey = isOpenSubtitles || string.IsNullOrWhiteSpace(apiKeyBox.Password) ? null : apiKeyBox.Password,
  Username = !isOpenSubtitles || string.IsNullOrWhiteSpace(usernameBox.Text) ? null : usernameBox.Text,
  Password = !isOpenSubtitles || string.IsNullOrWhiteSpace(passwordBox.Password) ? null : passwordBox.Password
};
```
**Desktop explicitly sends `null`** when a field is blank, relying on server null-check to mean "keep existing". This is arguably cleaner, but differs from webui's empty-string behavior. **Could cause divergent server behavior** if the server differentiates `null` vs `""` (e.g., `null` = "keep current", `""` = "clear credential").

### Gap
| # | Gap | Severity |
|---|---|---|
| 2.15.a | Payload semantics differ: webui sends `""`, desktop sends `null`. May or may not matter depending on server contract — **needs verification against the server handler** to confirm equivalence. | functional (potential) |

## 2.16 Dynamic status colors & palette

Desktop palette used in this page:
- Green (Configured): `rgb(34,197,94)` = Tailwind `green-500` ✓ matches webui.
- Yellow (Not configured): `rgb(234,179,8)` = Tailwind `yellow-500` ✓ matches webui.
- Blue (Enabled): `rgb(96,165,250)` / `rgb(59,130,246)` = `blue-400`/`blue-500` — **no webui analog** (webui uses a Switch, not a colored badge).
- Grey (Disabled): `rgb(160,160,160)` / `rgb(120,120,120)` — no direct Tailwind token, but ≈ `muted-foreground`.

## 2.17 Reorder / priority controls

### Webui
No reorder/priority UI — subtitle providers are sorted by a hardcoded `SUBTITLE_PROVIDER_ORDER` constant.

### Desktop
Same — no reorder UI; same hardcoded sort.

**Match.** ✓

## Prioritized Fix List — AdminSubtitleProviders

### P0 (structural)
1. **Demote from top-level page to an "Integrations" section.** Either:
   - Embed inside an `AdminSettingsPage` with sidebar navigation (mirror `SettingsLayout.tsx`), and render as the "Integrations → Subtitle providers" subsection.
   - Or, if the desktop keeps it as a page, at minimum fix the header: title "Integrations" at 18px SemiBold, subtitle "Subtitle providers" at 14px muted.
2. Add the credential-security intro paragraph ("Credentials are stored securely and never returned by the API.") — informational/safety.
3. Constrain card column to 672px (`max-w-2xl`).

### P1 (visible clone violations)
4. Card CornerRadius 20 → 8; add 1px border.
5. **Replace "Configured"/"Not configured" pill badges with lucide-style CircleCheck/CircleAlert icon + plain muted text.**
6. **Remove the "Enabled"/"Disabled" badge in the header. Move the ToggleSwitch to the header-right and add a dynamic label ("Enabled"/"Disabled") beside it.**
7. **Add show/hide (Eye/EyeOff) toggle for API key fields.** Replace PasswordBox with a custom TextBox that toggles `PasswordRevealMode`.
8. Move Test button from header into actions row, next to Save. Change label "Test" → "Test Connection".
9. Replace transient "Passed"/"Failed" button label with persistent inline result text (green "Connection successful" / red "{error}"), in the actions row.
10. Add "Saving..." pending state on Save button.
11. Display name FontSize 16 → 14.
12. Field label FontSize 12 → 14, weight SemiBold → Medium (500), color Secondary → Primary.
13. Remove page-level StatusBanner; use toast pattern or inline result text.
14. Skeleton loading (3 cards) instead of ProgressRing.
15. Empty state: render as a card with border/bg, left-aligned text, `py-4`.

### P2 (polish)
16. Card inner spacing 12 → 16.
17. Card stack gap 12 → 16 (`space-y-4`).
18. Remove full-page error takeover; surface errors via toast.
19. Verify save payload semantics (null vs empty string).

---

# Overall Summary Table

| Page | Feature | Webui | Desktop | Gap | Severity |
|---|---|---|---|---|---|
| Providers | Existence of page | No such page | Full page exists | Fabricated | **critical** |
| Providers | Metadata provider mgmt UX | Via plugins (`AdminPlugins.tsx`) | Direct CRUD on `/admin/providers` | Bypasses plugin system | critical |
| Providers | Header "Add Provider" style | N/A (outline in plugins) | AccentButtonStyle | Filled vs outline | visual |
| Providers | Card corner radius | 12px (`rounded-xl`) | 26px | Double radius | visual |
| Providers | Card border | 1px (`surface-panel-subtle`) | None | Missing | visual |
| Providers | Table header row | None (divided list) | UPPERCASE 5-col grid | Fabricated convention | visual |
| Providers | Status column | 6px color dot | Filled pill badge | Wrong component | visual |
| Providers | Actions | "Enable/Disable" buttons + Trash2 | Icon-only edit/delete | Less discoverable | visual |
| Providers | Settings column | N/A (private via dialog) | Masked `key: ***` | Exposes key names | privacy |
| Providers | Status banner | None | 4-sec accent banner | Fabricated | visual |
| Providers | Loading state | Keeps header, shows text/skeleton | Full-page ProgressRing | Different pattern | visual |
| Providers | Error state | Toast/inline | Full-page overlay | Different pattern | visual |
| Providers | Delete confirmation | Generic | Missing safety clause | Inadequate warning | functional |
| Providers | Create dialog width | 672px (`sm:max-w-2xl`) | 380px | Too narrow | visual |
| Providers | Create dialog settings field | Typed schema | Free-form `key=value` | Less safe | functional |
| Sub Providers | Existence as page | Embedded tab in IntegrationsSettings | Standalone page | Promoted | **critical** |
| Sub Providers | Header title | 18px "Integrations" | 42px "Subtitle providers" | Wrong title & size | **critical** |
| Sub Providers | Header subtitle | 14px "Subtitle providers" | 14px "Configure external..." | Wrong text | visual |
| Sub Providers | Intro credential-security paragraph | Present | Missing | Safety info missing | functional |
| Sub Providers | Card column max width | 672px | None | Cards too wide | visual |
| Sub Providers | Card corner radius | 8px (`rounded-lg`) | 20px | 2.5× too big | visual |
| Sub Providers | Card border | 1px | None | Missing | visual |
| Sub Providers | Configured status UI | Lucide icon + plain text | Filled pill badge | Wrong component | visual |
| Sub Providers | Enabled status UI | Switch + dynamic label in header | Static badge + separate toggle below | Wrong component | **major functional** |
| Sub Providers | Toggle label | "Enabled"/"Disabled" dynamic | Static "Enabled" | Doesn't flip | functional |
| Sub Providers | API key show/hide | Eye/EyeOff button | PasswordBox only | Feature missing | functional |
| Sub Providers | Field label size/weight/color | 14px med primary | 12px semibold secondary | 3 separate deltas | visual |
| Sub Providers | Test button placement | Actions row (bottom) | Header row (top-right) | Wrong location | structural |
| Sub Providers | Test button label | "Test Connection" | "Test" | Wrong text | visual |
| Sub Providers | Test result display | Persistent inline text | 2-sec button-content flip | Less informative | functional |
| Sub Providers | Save pending state | "Saving..." button text | Disabled only | Feature missing | functional |
| Sub Providers | Error surfacing | Inline red text | 4-sec StatusBanner only | Wrong location | functional |
| Sub Providers | Loading state | Skeleton cards | Centered ProgressRing | Wrong pattern | visual |
| Sub Providers | Empty state | Card-shaped placeholder | Bare centered text | Wrong shape | visual |
| Sub Providers | Save payload blank fields | `""` | `null` | Needs server verification | potential functional |

---

## Closing Note

The two gravest findings are structural:

1. **`AdminProvidersPage` does not exist in the webui at all.** Before doing any visual work on it, get Mike's explicit sign-off on whether this page should exist. Per the "No appearance changes" hard rule (no appearance or functionality changes without approval), this entire page is an unauthorized invention.

2. **`AdminSubtitleProvidersPage` is a page, but the webui has it as an embedded subsection.** This is the same structural mismatch class found in the `AdminNodes`/`AdminApiKeys`/`AdminInviteCodes` audit. The correct long-term fix is to build an `AdminSettingsPage` with left-sidebar subsections (Appearance, Integrations, Downloads, etc.) that mirrors `SettingsLayout.tsx`.

Everything else in this audit (colors, radii, icons, spacing, button placement, pending states, eye-toggle) is solvable with the structure in place.
