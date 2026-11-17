# Accessibility and motion (official #4294, #4311, #4308, #4297)

Reference: official Silo Server main `8f93594ce19723dde8a204be21ba59a24ef06e3b`
(fetched 2026-11-16 from https://github.com/Silo-Server/silo-server.git).
Desktop base: origin/beta `bdd033c1cab7cd601d6771bb853f4b0af044d67e` (1.2.1165).
Source: server-review-nov15 viewer audit, section 8, "Accessibility and motion".

| Upstream | SHA | Desktop scope |
| --- | --- | --- |
| #4294 icon-only buttons get an accessible name | `9e7cf8a9c1d76fa768552c28b4adab50731bedbd` | Section-row pin button |
| #4311 stop motion on paths that ignored reduced motion | `e6646ba6b7f99ec07bf2c567323c115323be819a` | Scripted row, rail, detail-strip, calendar and requests scrolling |
| #4308 form fields get a visible edge; follow system contrast | `07dac873160ec3ce0f3be121514b933a2a786ab7` | Input-border token; Windows contrast themes |
| #4297 focus, switch state and selection in Windows High Contrast | `5d3b053c5e52fed4d7726173f54c6b670d8515d6` | System-colour focus, switch, slider and selection |

Not ported: #4310 (dialog/menu open-close animation). WinUI flyouts and dialogs
already animate natively and follow Windows animation effects. Page navigation
keeps the owner rule: pages swap in place, with no entrance, slide or opacity
animation. This change adds no page motion.

## Behaviour

### Section pin name (#4294)

Upstream `SectionPinButton` keeps the `web.home.sectionRow.pin`/`unpin` title.
It adds the same text as `aria-label` and points `aria-describedby` at the row's
shown (translated) heading. The desktop had a hard-coded English
"Pin to sidebar"/"Unpin from sidebar" and no row context.

`SectionRow.UpdatePinLabel()` now:

- sets the tooltip and `AutomationProperties.Name` from
  `AppStrings.Get("web.home.sectionRow.pin" | "…unpin")`.
- sets `AutomationProperties.HelpText` to `SectionTitle.Text`, the row's
  displayed title. Narrator reads HelpText after the name, which is the native
  counterpart of `aria-describedby`.
- runs from `UpdatePinVisual` (pin state) and at the end of `UpdateTitle`, so an
  interface-language change (the existing `AppLocalization.Register` callback) or
  a refreshed display title renames the button.

The stored pin label (`Section.Title`) is unchanged. All eight shipped catalogs
already carry both keys.

Merge note: open PR #185 adds `RefreshTitle()` just above `UpdateTitle`. This
change touches only the end of `UpdateTitle` (one added call), `NavigateTo` and
the pin-label method. The hunks do not overlap, and #185's `RefreshTitle` also
refreshes the pin description through `UpdateTitle`.

### Reduced motion (#4311)

`Helpers/SystemMotion.AnimationsEnabled` mirrors upstream's
`prefersReducedMotion()`/`motionScrollBehavior()` using Windows "Animation
effects" (`UISettings.AnimationsEnabled`). It reads the setting once into a
`volatile` field and updates it from `AnimationsEnabledChanged` (Windows 10 2004+;
older builds read on demand). There is one process-wide `UISettings`, so a scroll
never makes a WinRT query. `ArtworkReveal.AnimationsEnabled` now delegates to it
instead of reading WinRT per call.

With animation effects off, these scripted scrolls jump instead of sliding:

- `SectionRow.NavigateTo` (arrow paging, keyboard paging, Home/End).
- `CarouselRail`: Home, End and arrow/keyboard paging. Pointer dragging already
  passed `disableAnimation: true`.
- `ItemDetailPage`: keyboard strip paging, detail-carousel arrows, season strip
  arrows. This matches upstream's `useCarouselEmbla`/`useScrollStrip`.
- `CalendarPage.SelectDay` day jump (upstream `Calendar.tsx`).
- `RequestsPage` search paging scroll (upstream `RequestToAddSection.tsx`).

When animation effects are on, behaviour is unchanged.

### Field edge contrast (#4308)

Upstream raised `--input-base` from `#1c1c20` to `#6a6a74`, which is 3.44:1 on
the page and 3.18:1 on cards. It pointed text fields, selects, checkboxes and the
unchecked switch track at it. `--border` stays the hairline. In-app high contrast
mixes 16% `--contrast-boost` (`#fff`) into `--input`, giving `#82828A`.

The desktop's `Input` token is the field fill (`TextControlBackground`), so a new
token carries the edge:

- `ThemeColors.InputBorder = "#6A6A74"`, plus `InputBorderColor` and
  `InputBorderBrush` in `DarkTheme.xaml`. ThemeService sets it and switches it to
  `#82828A` under the in-app high-contrast preference.
- `TextControlBorderBrush`, `ComboBoxBorderBrush`, `ToggleSwitchFillOff` and
  `ToggleSwitchFillOffPointerOver` derive from `InputBorderBrush`, not
  `BorderBrush`. This applies both in XAML and in `RefreshDerivedControlResources`.
- `DarkTextBoxStyle` and `DarkPasswordBoxStyle` use `InputBorderBrush`, matching
  upstream's `border-input bg-background`.
- Buttons, dialogs, menus, combo drop-downs and dividers keep the hairline
  `BorderBrush`.

### Windows contrast themes (#4297, #4308 system contrast)

Upstream follows `forced-colors` (the browser repaints with the system palette).
#4297 restores what forced colours lose: a Highlight focus outline, a visible
switch thumb and state, slider and progress fills, and Highlight/HighlightText
for the current page, selected tab and chosen radio. #4308 seeds the in-app
high-contrast setting from `prefers-contrast: more` when no choice is stored.

On the desktop, `ThemeService.ApplyTheme` ends with `ApplySystemContrast`. When
`AccessibilitySettings.HighContrast` is on, it reads the system
`UIElementColor`s:

- Surface tokens (app, sidebar, card, surface, hover, raised, popover, field
  fill, secondary, muted, accent background, sidebar accent) take **Window**.
- Text and edge tokens, including `Border`, `InputBorder` and `SidebarBorder`,
  take **WindowText**.
- Accent, ring, sidebar primary and sidebar ring take **Highlight**, with
  **HighlightText** on the accent and sidebar-primary foregrounds. As a result:
  - the focus visual is Highlight;
  - checked switches, slider fills and focused field borders are Highlight;
  - the checked switch knob is HighlightText.
- After re-deriving control resources, it sets:
  - the selected NavigationView item, selected ComboBox item and list
    selection to Highlight/HighlightText;
  - the unchecked switch track to Window, so its WindowText thumb shows;
  - the slider track to GrayText.
- It applies after the app palette, shared appearance overrides and the in-app
  high-contrast palette, so the system palette wins, as forced colours do.
- `UISettings.ColorValuesChanged` (marshalled to the UI thread) reapplies the
  theme. This happens only when a contrast theme is or was active, which covers
  turning contrast on or off and switching between contrast themes.
- Every key it writes is one `ApplyTheme` writes from the theme, so leaving the
  contrast theme restores the app palette exactly.

The desktop has no unset/stored distinction for `UiHighContrast`. The system
palette therefore applies whenever a contrast theme is on, whatever the in-app
choice. This matches forced colours overriding the WebUI's in-app setting.

`AccessibilityService.Apply` no longer rewrites four text/border brushes after
`ApplyTheme`. ThemeService already applies the same in-app high-contrast values
from the just-saved setting. Those late writes would otherwise have painted
white text over a light contrast theme.

## Files changed

- `src/SiloPlayer/Helpers/SystemMotion.cs` (new)
- `src/SiloPlayer/Helpers/ArtworkReveal.cs`
- `src/SiloPlayer/Controls/SectionRow.xaml.cs`
- `src/SiloPlayer/Controls/CarouselRail.cs`
- `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`
- `src/SiloPlayer/Views/CalendarPage.xaml.cs`
- `src/SiloPlayer/Views/RequestsPage.xaml.cs`
- `src/SiloPlayer/Services/ThemeService.cs`
- `src/SiloPlayer/Services/AccessibilityService.cs`
- `src/SiloPlayer/Themes/DarkTheme.xaml`
- `tests/SiloPlayer.Tests/AccessibilityMotionParityTests.cs` (new)
- `tests/SiloPlayer.NativeRegressionTests/AppLanguageNativeFixture.cs`
  (Arabic pin name and row description)

## Verification

Local results on this source:

- **Unit tests:** all 7,490 pass, including `AccessibilityMotionParityTests`.
- **Native, passing:** `app-language` (with the Arabic pin name and
  description), `shared-appearance`, `shared-controls`,
  `sidebar-review-pin-owner-read`, `sidebar-review-pin-owner-write`,
  `home-row-window` and `oct20-home-rows`.
- **Already failing on Beta:** `home-row-review` (25 passes, then "Removal
  confirmation must match the current WebUI") and `artwork-motion` (24 passes,
  then a readiness timeout at `ArtworkMotionNativeFixture.cs:170`) fail
  identically on clean Beta `bdd033c1c`. This change does not cause them.
- **XAML comments:** XAML forbids a double hyphen inside a comment, and the
  WinUI XAML compiler exits without a WMC message when it finds one.
  `DarkTheme.xaml`'s new comment avoids it.
- **Contrast themes:** native fixtures assume Windows contrast themes are off.
  With one on, the `shared-appearance` colour assertions would see system
  colours.

Still needs manual checks:

- Narrator on a Home/library row: the pin should announce the localized
  "Pin to sidebar" or "Unpin from sidebar", then the row title. Repeat after a
  language change.
- Windows Settings › Accessibility › Visual effects › Animation effects **off**:
  row arrows, rail Home/End, detail strips, calendar day jump and requests paging
  should jump. Toggle the setting while the app runs to confirm the live update.
- Windows contrast themes (Aquatic, Desert, Dusk, Night sky), entered and left
  while running. Check:
  - surfaces and text;
  - the focus visual;
  - checked and unchecked ToggleSwitch;
  - Slider fill and track;
  - the selected sidebar item;
  - ComboBox selection;
  - text-field edges;
  - that leaving restores the dark palette.
- A visual check of the brighter field edge on Settings and dialog text fields.

Known limits (not changed here):

- Controls and pages that set hard-coded colours or private brushes keep them
  under a contrast theme. Examples: artwork scrims, hero gradients, badges,
  chart colours, `ErrorBrush`, `AccentHoverBrush`/`AccentPressedBrush` and
  `ButtonForegroundDisabled`. Only the shared theme tokens and WinUI control
  resources above follow the system palette.
- Some animated vertical scrolls are outside the ported upstream paths and remain
  animated:
  - `CatalogPage`, `SearchPage` and `EditMetadataDialog` scroll-to-top;
  - the Playing Next on-deck scroller;
  - `StartBringIntoView` calls without `AnimationDesired = false`.
- The server appearance override for `input` still maps to the desktop field fill
  (`InputBrush`), not to the new edge token.
