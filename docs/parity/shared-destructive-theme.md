# Shared destructive (red) theme parity record

Official reference: Silo Server `02a9d1d3e1b48d86437ad10c0b66ed0e3645d098` (read with `git show`/`git grep` on the blobless clone; nothing fetched or checked out).
Native base: `origin/beta` at `e02c0e2a0` (1.2.1351-beta.1); merged forward with beta `28f3e8bf4` (merge commit `b63eb369a`) for the review fixes.
This record states what was read and observed. It does not claim visual or pixel parity.
**Section 11 (independent review, November 21) supersedes sections 4 (contrast themes, server overrides), 5 (two figures) and 7 where they differ; the corrected statements are marked below.**

## 1. Official tokens

`web/src/app.css` defines **one** theme, `midnight-cinema` (`[data-theme="midnight-cinema"]`, line 626; also the only `data-theme` value, `app.css:35`), plus one global accessibility mode, `html[data-high-contrast="true"]` (line 830). No other file defines these tokens (`git grep` for `destructive-base|destructive-fill|--destructive:` finds only `app.css`, `badge.tsx`, `button.tsx`, `RequestQueue.tsx`, `design-system.ts` and the design doc). The admin branding (`BrandingProvider.tsx`) only sets the accent/primary and ring; it never sets a red.

| Token | Where | Value |
|---|---|---|
| `--destructive` | `app.css:619` `:root` alias of `--destructive-base` | role: danger/error text, icons, borders |
| `--destructive-base` | `app.css:670`, midnight-cinema | `#ef4444` |
| `--destructive-foreground` | `app.css:671`, midnight-cinema | `#ffffff` |
| `--destructive-fill` | `app.css:672`, midnight-cinema | `#dc2626` (white on it 4.83:1, stated at `app.css:669`) |
| `--destructive-label` | `app.css:595` `:root` | `color-mix(in srgb, var(--contrast-boost) 25%, var(--destructive))` |
| `--contrast-boost` | `app.css:607-610` `:root`, midnight-cinema `:636` | `#ffffff` |
| high contrast `--destructive` | `app.css:844` | `color-mix(in srgb, var(--contrast-boost) 65%, var(--destructive-base))` |
| high contrast `--destructive-label` | `app.css:853` | `var(--destructive)` (the lightened red) |

Not redefined by any theme (inherited by definition, since there is one theme): nothing. High contrast leaves `--destructive-fill` and `--destructive-foreground` unchanged.

How official components use them:

| Official use | Token | Source |
|---|---|---|
| Destructive button | `bg-destructive-fill text-destructive-foreground`, `hover:bg-destructive-fill/90` (no lighter state, no separate pressed state), `disabled:opacity-50`, `focus-visible:ring-destructive/20` | `components/ui/button.tsx:14`, base classes line 8 |
| Destructive badge | `bg-destructive-fill text-destructive-foreground`, `[a&]:hover:bg-destructive-fill/90` | `components/ui/badge.tsx:15` |
| Request queue solid badge | `bg-destructive-fill text-destructive-foreground` | `pages/admin-requests/RequestQueue.tsx:391` |
| Error text, icons, status dots | `text-destructive` | e.g. `pages/Login.tsx:543`, `ActivateDevice.tsx:499`, `InviteClaim.tsx:373` |
| Error panel (sign-in) | `border-destructive/30 bg-destructive/10 text-destructive` | `pages/Login.tsx:543, 551` |
| Invalid control border/ring | `aria-invalid:border-destructive aria-invalid:ring-destructive/20` | `button.tsx:8`, `badge.tsx:8` |
| Red label on its own tint | `text-destructive-label bg-destructive/10` (`border-destructive/30`) | `admin-downloads/downloadStoragePresentation.ts:458`, `adminActivityPresentation.ts:154`, `AdminDiagnostics.tsx:712`, `AdminDevices.tsx:1287,1782`, `deviceOverrides.tsx`, `PolicySimulatePanel.tsx:57`, `WatchProvidersWidget.tsx:83`, autoscan pollEventDisplay |

## 2. Native resources: before and after

Before (`origin/beta` e02c0e2a0). `DarkTheme.xaml` holds design-time defaults; `ThemeService.ApplyTheme` overwrites the live brushes in place at start-up and on every appearance change, so the values a user sees are the ThemeService ones.

| Resource | XAML default | Live value (ApplyTheme) | Role it served |
|---|---|---|---|
| `ErrorColor` / `ErrorBrush` | `#EF6B73` (rose) | `#EF4444` | text, icon, border **and** the destructive button fill |
| `DestructiveForegroundColor` / `Brush` | `#FFFFFF` | `#FFFFFF` | label on the button |
| `WebUiDestructiveHoverBrush` | `ErrorColor` at opacity 0.9 | `#EF4444` at 0.9 (copied from `ErrorBrush`) | PointerOver **and** Pressed of the button |
| `DestructiveButtonStyle.Background` | `ErrorBrush` | `#EF4444` | fill at rest |

So at runtime the text red already matched the official `--destructive`; the defect was that the same colour was also the fill. White on `#EF4444` is 3.76:1, on the hover (`#EF4444` at 90% over the page = `#D93F40`) 4.43:1, below 4.5:1 in both. The 3:1 figure in the brief is the XAML default `#EF6B73` (2.99:1 at rest, 3.56:1 hover), which only shows before ThemeService runs (for example in a fixture that does not call it: the "before" Subtitle Files capture shows the rose). The hover did not get lighter; it darkened a little but stayed below 4.5:1.

After:

| Resource | XAML default | Live value | Role |
|---|---|---|---|
| `ErrorColor` / `ErrorBrush` (kept) | `#EF4444` | `#EF4444`; high contrast `#F9BEBE` | `--destructive`: text, icons, borders |
| `DestructiveFillColor` / `DestructiveFillBrush` (new) | `#DC2626` | `#DC2626` (unchanged in high contrast) | `--destructive-fill`: solid fill |
| `DestructiveForegroundColor` / `Brush` (kept) | `#FFFFFF` | `#FFFFFF` | `--destructive-foreground` |
| `ErrorLabelColor` / `ErrorLabelBrush` (new) | `#F37373` | `#F37373`; high contrast `#F9BEBE` | `--destructive-label` |
| `ErrorTintBrush` (new) | `ErrorColor` at opacity 0.1 | follows `ErrorBrush` | `bg-destructive/10` |
| `WebUiDestructiveHoverBrush` | `DestructiveFillColor` at 0.9 | follows `DestructiveFillBrush` | PointerOver: the fill at 90% over the surface behind the button, as `hover:bg-destructive-fill/90` |
| `WebUiDestructivePressedBrush` (new) | `DestructiveFillColor` at 0.8 | follows `DestructiveFillBrush` | Pressed. Official has no pressed state; I chose the fill at 80% so pressed is a step darker than hover |

No key was renamed or removed. `ErrorBrush` consumers are untouched.

Shared styles and templates changed: `DestructiveButtonStyle` (Background `ErrorBrush` to `DestructiveFillBrush`) and `WebUiDestructiveButtonTemplate` (Pressed uses the new pressed brush; PointerOver keeps `WebUiDestructiveHoverBrush`, now the darker fill; Disabled stays opacity 0.5 = `disabled:opacity-50`; `UseSystemFocusVisuals` unchanged). `ExternalTitleCard.StatusBadge` ("Failed" badge) now fills with `DestructiveFillBrush` instead of `ErrorBrush`. No other shared style, template, InfoBar or toast paints a red fill under light text (searched `Background`/`Fill`/`Tint`/`Chip` uses of `ErrorBrush` and every `DestructiveButtonStyle` use).

## 3. Every way the red is consumed (whole-source search, beta e02c0e2a0, before the change)

Counts are `git grep` over `src`, `tests`, `installer`; the 100 files that mention `ErrorBrush`, `ErrorColor`, `DestructiveButtonStyle`, `WebUiDestructive` or `EF6B73` include tests.

| Search | Files | References |
|---|---|---|
| `ErrorBrush` (src) | 83 | 139 |
| `ErrorColor` (src) | 4 | 8 (theme 2, ThemeService 3, `DashboardUi`, `AdminMonitorUi` x2) |
| `DestructiveButtonStyle` (src) | 29 | 37 (the definition plus 36 uses: 23 dialog `PrimaryButtonStyle`, 13 page or inline buttons) |
| `WebUiDestructive*` (src) | 2 | 6 (theme, ThemeService) |
| `EF6B73` (src) | 8 | 8 (the theme default and 7 literal `#2DEF6B73` panel tints) |
| hard-coded rose RGB in code | 3 | `AdminAutoscanActivityUi` fallback (239,107,115), `AdminMonitorUi` x2 (`0xEF,0x6B,0x73`) |

By kind of use (approximate, by reading each match):

| Kind | Count before | After |
|---|---|---|
| Destructive button fill (`DestructiveButtonStyle`) | 37 | same sites, now the fill |
| Badge / pill fill under light text | 1 (`ExternalTitleCard` "Failed") | moved to the fill |
| Dialog primary fill set by hand | 1 (`CollectionEditorPage.Departure`) | moved to the fill |
| Error text (`Foreground`, XAML 44, code about 77) | about 121 | unchanged key, now official `--destructive` text |
| Error icon / stroke / dot / marker (`Fill`, `Stroke`, `Marker`) | about 8 | unchanged |
| Border / outline (`BorderBrush`) | 3 | unchanged |
| Tinted background | 7 literal `#2DEF6B73` panels plus 3 `Tint("ErrorBrush")` | 7 panels use `ErrorTintBrush`; `AdminPreparedFilesPage` "danger" pill uses `ErrorLabelBrush` over a 10% tint |
| Other | 1 `SystemFillColorCriticalBrush` text (`ProfileEditorDialog`), 1 literal `#EF4444` icon in `WatchTonightDialog` | critical text now `ErrorBrush`; literal icon left (equals the official value) |

## 4. How themes are applied at runtime now

`ThemeService.ApplyTheme` still writes shared brushes and colors in place (no brush is replaced, no second mechanism). The new `ApplyDestructive` runs after the in-app high contrast block and before `RefreshDerivedControlResources` and `ApplySystemContrast`:

- It calls `DestructiveTheme.Resolve` (new, `SiloPlayer.Core/Services/DestructiveTheme.cs`, pure and unit tested) with the theme's `Destructive` (= `--destructive-base`), `DestructiveFill` and `DestructiveForeground` and the in-app high contrast setting. *(Corrected, section 11: it first took the server appearance overrides as a fourth input; that input is removed.)*
- A token the theme leaves empty (or that is not a hex color) inherits the midnight-cinema default. The server sends only the branding accent (`SharedAppearanceState.AccentColors`: primary, ring, sidebar-primary and the foregrounds), so no server value reaches a red and `Resolve` has no override input.
- High contrast: text = `color-mix(boost 65%, base)`, label = that text; fill and foreground unchanged, as official.
- It writes `ErrorBrush`/`ErrorColor`, `ErrorTintBrush`, `DestructiveFillBrush`/`Color`, `DestructiveForegroundBrush`/`Color`, `ErrorLabelBrush`/`Color`. `RefreshDerivedControlResources` copies `DestructiveFillBrush` into `WebUiDestructiveHoverBrush` and `WebUiDestructivePressedBrush` (it used to copy `ErrorBrush` into the hover brush).
- The old direct `destructive` and `destructive-foreground` entries of the override map were removed because `ApplyDestructive` now owns them.
- Windows contrast theme: *(corrected, section 11)* the system palette repaints every red role. The first version left them out of the token lists, so every red stayed a fixed red in a contrast theme, which official does not do. The test seam is now the public method `ThemeService.ApplySystemPalette`, not a private field set by reflection.

## 5. Contrast (computed, WCAG 2.x relative luminance; `DestructiveTheme.Contrast`)

Default theme. White on the fill and its states (`Over` = the fill at the stated opacity over the surface behind the button):

| Surface behind | rest `#DC2626` | hover (90%) | pressed (80%) |
|---|---|---|---|
| page `#141417` | 4.83 | `#C82425` 5.62 | `#B42223` 6.58 |
| card `#1C1C20` | 4.83 | `#C92525` 5.56 | `#B62425` 6.43 |
| popover `#18181C` | 4.83 | `#C82525` 5.60 | `#B52324` 6.50 |
| raised `#28282E` | 4.83 | `#CA2627` 5.50 | `#B82628` 6.28 |
| surface hover `#232328` | 4.83 | `#CA2626` 5.50 | `#B72526` 6.35 |
| sidebar `#0F0F12` | 4.83 | `#C82424` 5.62 | `#B32122` 6.65 |

Dialogs use `ContentDialogBackground` = `AppBackgroundBrush` (the page row). Before: white on the old fill `#EF4444` 3.76 at rest and 4.43 over the page when hovered; on the XAML default `#EF6B73` 2.99 and 3.56.

Destructive text `#EF4444` on each surface (this is the official value, kept even where it is below 4.5:1):

| Surface | text | note |
|---|---|---|
| page | 4.89 | official comment `app.css:664` says 4.89 |
| card | 4.51 | official comment says 4.51 |
| popover | 4.70 | |
| sidebar | 5.09 | |
| surface hover | 4.16 | below 4.5:1, official value kept |
| raised | 3.89 | below 4.5:1, official value kept; official notes no red text sits on it |

Red on its own 10% tint (page / card / popover / raised / surface hover): text 4.45 / 4.10 / 4.28 / 3.53 / 3.78 (below 4.5:1 for normal text: this is the official `text-destructive` on `bg-destructive/10` of the sign-in error panel, kept); label `#F37373` 5.99 / 5.51 / 5.76 / 4.75 / 5.09 (all at least 4.5:1, which is why official uses the label there). *(Corrected, section 11.)* The sign-in panel did not drop from 4.82 to 4.45. The old literal panel was painted with `#2DEF6B73` as the tint, but its text was the live `ErrorBrush` `#EF4444`, not the rose: at runtime it was 3.83 on the page and 3.49 on a card. The new panel is the official `text-destructive` on `bg-destructive/10`: 4.45 on the page and 4.10 on a card, an improvement of 0.62 and 0.61 (still below 4.5:1; the panel's text is the plain red as official, not the label). The rose text on the rose tint (4.82 / 4.40) only existed before ThemeService ran.

High contrast, **official** (`#F9BEBE` text; surfaces lightened as `app.css:830-846`; this is what `DestructiveThemeTests` computes): page 11.50, card `#333336` 7.88, surface hover `#46464A` 5.88, raised `#535358` 4.78 (official comment 4.77); white on the unchanged fill 4.83. **Native** high contrast does not lighten surfaces (ThemeService changes only text, border, input border and ring), so the native values read from the live brushes are different, and higher: text `#F9BEBE` on the page 11.50, on a card 10.63, on the raised surface 9.17; the label on its tint over the page 9.40, a card 8.54, the raised surface 7.26 (`shared-destructive-theme`, label "high contrast red text over the ..."). Official label on tint equals text: 9.40 page, 6.23 card, 4.77 hover, 3.96 raised (below 4.5:1 on raised, as the official block states: no red tint sits on it).

The unit tests compute these numbers from `DestructiveTheme.Contrast` (they are not string assertions) and check that hover and pressed always exceed the rest contrast on every surface, and that the old fills never reached 4.5:1.

## 6. Page-level uses

Fixed (file not in an open pull request):

- `Controls/ExternalTitleCard.cs` "Failed" badge: fill is `DestructiveFillBrush` under `DestructiveForegroundBrush`.
- `Views/CollectionEditorPage.Departure.cs` discard confirmation: primary button background set by hand to `ErrorBrush`; now `DestructiveFillBrush`. The `shared-controls` native check compared that background with `ErrorBrush`; it now compares with `DestructiveFillBrush` (the one existing check that asserted the old red).
- `Views/Admin/AdminPreparedFilesPage.cs` danger pill: label `ErrorLabelBrush` on a 10% tint (was `ErrorBrush` on 12%), as official `text-destructive-label bg-destructive/10`.
- Seven sign-in style error panels (`LoginPage`, `SignupPage`, `InviteClaimPage`, `ActivateDevicePage`, `HouseholdSetupPage`, `WatchTogetherJoinPage`, `WatchTogetherRoomPage`): literal `#2DEF6B73` replaced by `ErrorTintBrush`.
- `Views/Admin/AdminAutoscanActivityUi.cs`: fallback color (239,107,115) to (239,68,68).
- `Views/Dialogs/ProfileEditorDialog.cs`: validation text used the Windows `SystemFillColorCriticalBrush`; now `ErrorBrush` (the Silo error text).

Left, with the open pull request that blocks each (found with `gh pr list --base beta --state open --json number,files`; 14 open PRs, none touches `DarkTheme.xaml` or `ThemeService.cs`):

| File | What | PR |
|---|---|---|
| `Controls/AdminMonitorUi.cs` (2 places) | fallback `0xEF,0x6B,0x73` | #232 |
| `Controls/Admin/DashboardUi.cs` | reads `ErrorColor` with an `#EF4444` fallback: already the official text red | #232 |
| `Views/Admin/AdminDevicesPage*.cs`, `AdminUserResourceUi.cs` (outlined chips and dot with `ErrorBrush`) | outlined text/border/dot, not fills; label-on-tint equivalents would use `ErrorLabelBrush` | #240 |
| `Views/Admin/AdminHistoryImportPage.Mappings.cs`, `AdminAccessGroupsPage.*` | `ErrorBrush` text/chips | #240 |
| `Views/Admin/AdminDiagnosticsPage.cs`, `LogsNativeUi.cs` | `ErrorBrush` text and state color; official uses `text-destructive-label` on tint for some badges | #232 |
| ~~`Views/SettingsPage.xaml.cs` (3903, 4061, 4066)~~ | #230 has merged; the failed status badge is fixed (section 11); the run error box is left on purpose (section 11) | |
| Other `ErrorBrush` text/icon uses in `AdminBackupsPage`, `AdminHomeSectionsPage.Dialogs`, `AdminLibrariesPage.*`, `AdminSettingsUi` | text/icon only, no change needed; the shared `DestructiveButtonStyle` they use is fixed | #236, #235, #229, #234 |

Not changed on purpose: `Controls/WatchTonightDialog.xaml:226` (literal `#EF4444` icon and `#66EF4444` border; equal to the official text red, not a fill); the official `border-destructive/30` on the sign-in error panel (native panels have no border); the page-local `#dc2626` styles in open pull requests.

## 7. Tests

- Unit, `tests/SiloPlayer.Tests/DestructiveThemeTests.cs` (34 cases): defaults equal the official tokens; the built-in theme in `ThemeService` declares them and is the only theme; a theme that omits a token, or whose value is not a hex color, inherits the default; a server theme sets text and fill and inherits the foreground, a fill-only theme keeps the default text, the branding accent leaves every red untouched; high contrast text, label and unchanged fill; `Mix`/`Over` blends; the contrast numbers of section 5 computed (white on the fill 4.83, hover and pressed above rest on every surface, text on each surface, label on tint, high contrast raised surface); the button style and template use the fill, hover 0.9, pressed 0.8, disabled 0.5, focus visuals kept; `ApplyDestructive` order and in-place writes; no red role in the Windows contrast token lists; every red key referenced from XAML is defined by the shared theme before use; no rose left in the theme, `ThemeService` or the view XAML.
- Native, new `shared-destructive-theme` (`SharedDestructiveThemeNativeFixture`, registered in `Program.cs`; 40 checks): resources resolve the official roles; a real `DestructiveButtonStyle` button at rest, PointerOver, Pressed and Disabled (visual states driven, template parts read, rendered pixels sampled and compared with the blend over the surface); white label; error text and the red badge resolve the shared brushes; the real viewer discard confirmation fills with the destructive fill; a theme switch (high contrast, a server text and fill, a fill-only server theme, a branding accent, a cleared server theme) updates the same live elements and brushes in place; a simulated Windows contrast theme repaints surfaces, text and accent with system colors, leaves the reds as before, keeps 4.5:1 on the fill, and leaving it restores the palette. It writes its captures before it checks, and uses only resource keys, so it also runs on a build that predates the change.
- Existing check updated: `SharedControlsNativeFixture` (`shared-controls`) compared the discard confirmation's primary background with `ErrorBrush`; it now compares with `DestructiveFillBrush`. Nothing else asserted the old red (searched for `#EF4444`, `#EF6B73`, `#DC2626`, `ErrorColor`, `Destructive*` in both test projects).
- `AdminSubtitlesNativeFixture` additionally captures the delete confirmation (`subtitles-delete-dialog.png`); no assertion changed.
- Theme- or accessibility-specific native selectors found in `Program.cs`: `shared-appearance`, `shared-controls`, `admin-configure-appearance` (all run), plus `settings-visual-oct24`, `settings-visual-regressions`, `playback-toasts`, `toast-diagnostics`, `admin-appearance-overlays`, `conditional-dialogs` (not run).

## 8. Captures (before = `origin/beta` e02c0e2a0, after = the final source), opened and read

Lane `D:/SiloPlayer-build/admin-completion-lane-a/results`. Before: `redtheme-before-r1-shared-destructive-theme`, `redtheme-before-r4-admin-subtitles`. After: `redtheme-final-r8-shared-destructive-theme`, `rt-e9-admin-subtitles`.

- State matrix (`destructive-theme-states.png`, rest/hover/pressed/disabled, an error line on the page, an error line in a card, a sign-in style error panel, a red label pill and the red badge): before, four buttons in one bright red (`#EF4444`), hover and pressed only slightly different from rest; the error panel a pink-tinted band; the badge the same bright red. After, the buttons are a deeper red (`#DC2626`) and hover and pressed are visibly darker steps; the error text and the card text look the same red as before; the error panel is a darker, less pink tint; the "Failed" pill label is the lighter `#F37373` and reads more clearly on its tint; the badge is the deeper red under white text.
- Viewer destructive confirmation (`destructive-theme-viewer-dialog*.png`, the collection discard dialog, rest and hover): before, "Discard" is the bright red and on hover barely changes; after, "Discard" is the deeper red and the hover is a darker red.
- Admin destructive confirmation (Subtitle Files delete, a page with no page-local style): before, the button is a pink-rose (this fixture does not run `ThemeService`, so it shows the XAML default `#EF6B73`); after, it is the deeper red `#DC2626`. Because the before capture is the XAML default rather than the live `#EF4444`, the visible difference there is larger than the runtime change; the fixture comparison above (rendered pixels) shows the runtime one.
- Error message line: same red text before and after (it was already `#EF4444` at runtime); only its pre-ThemeService default changed from rose to `#EF4444`.
- Red badge: bright red before, deeper red after.
- High contrast (`destructive-theme-highcontrast.png`, after only): error text and the label are a pale rose (`#F9BEBE`), the buttons keep the deeper red.

## 9. Runs (all through `accept-family-v2.ps1`, lane A, task id `admin-families-nov20`, label prefix `redtheme-`)

Final source for the acceptance run: branch `t3/shared-destructive-theme` at `2b3cb4ed8`.

| Label | What | Result |
|---|---|---|
| `redtheme-before-r1` / `-r2` / `-r4` | beta baseline captures (r2 reused the r1 payload; r3 died with exit -1 and no output, repeated as r4) | captures taken; the new check's assertions fail on beta as intended |
| `redtheme-focused-r1` | focused unit | 70/71, my own test's key list was incomplete; fixed |
| `redtheme-final-r1` | final | stopped at the focused unit stage: my XAML-reference test did not accept template/style keys; fixed |
| `redtheme-final-r2` | final | unit focused 34/34, full 11176/11176; native `shared-destructive-theme` failed on brush opacity compared at double precision (Brush.Opacity is float precision); fixture tolerance fixed |
| `redtheme-final-r3`, `-r4` | final | unit pass; native `shared-destructive-theme` (40 checks), `shared-controls`, `shared-appearance`, `admin-configure-appearance`, `shell-localization`, `oct24-ui`, `oct24-shell` pass; `subtitle-preferences` fails (below) |
| `redtheme-betacontrol-r1` | `subtitle-preferences` on unchanged beta | fails the same way |
| `redtheme-final-r5` | final without `subtitle-preferences` | full unit 11175/11176: the known intermittent `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling` |
| `redtheme-final-r6`, `-r7` | the same source rerun | full unit 11176/11176; native stopped at `admin-dashboard` (teardown crash, below) |
| `redtheme-before-r4` | `admin-dashboard` on unchanged beta | same crash |
| `redtheme-final-r8` | final on `2b3cb4ed8`, `admin-dashboard` and `subtitle-preferences` left out | focused unit 34/34, full unit 11176/11176; native groups below; stopped at `admin-configure-integrations` (teardown crash after all checks passed) |
| `rt-e1` to `rt-e10` | each remaining selector on its own, against the r8 payload | below |

Native groups on the final source (checks passed / failed):

| Group | Passed | Failed | Run |
|---|---|---|---|
| shared-destructive-theme | 40 | 0 | r8 |
| shared-controls | 3 | 0 | r8 |
| shared-appearance | 2 | 0 | r8 |
| admin-configure-appearance | 113 | 0 | r8 |
| shell-localization | 4 | 0 | r8 |
| oct24-ui | 2 | 0 | r8 |
| oct24-shell | 2 | 0 | r8 |
| retained-library | 70 | 0 | r8 |
| poster-captions | 2 | 0 | r8 |
| overlay-revisions | 91 | 0 | r8 |
| household-contracts | 12 | 0 | r8 |
| profiles-metadata | 11 | 0 | r8 |
| admin-manage-foundation | 36 | 0 | r8 |
| admin-monitor | 113 | 0 | r8 |
| admin-configure-general | 188 | 0 | r8 |
| admin-configure-email | 297 | 0 | r8 |
| admin-configure-security | 374 | 0 | r8 |
| admin-configure-notifications | 35 | 0 | r8 |
| admin-configure-integrations | 101 | 0 | r8 and rt-e1; process exits with a crash code after "run completed" (below) |
| admin-configure-backups | 37 | 0 | rt-e2; same teardown crash |
| admin-configure-providers | 89 | 0 | rt-e3; same teardown crash |
| admin-collections | 203 | 0 | rt-e4 |
| admin-home-sections | 139 | 0 | rt-e5 |
| admin-signin-security | 172 | 0 | rt-e6 |
| admin-requests-management | 37 | 0 | rt-e7 |
| admin-manage-library-settings | 84 | 0 | rt-e8 |
| admin-subtitles | 65 | 0 | rt-e9 |
| admin-dashboard | 311 | 0 | r6, r7, rt-e10; same teardown crash, and it crashes the same way on unchanged beta (`redtheme-before-r4`) |

Two things that are not mine:

- `subtitle-preferences` fails on this lane with `COMException 0x800700A1 "The specified path is invalid"` in its own `CaptureAsync` (`StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory)` with the harness's mixed-slash path), on this branch (r3, r4, solo r1, r2) and on unchanged beta (`redtheme-betacontrol-r1`). The same error appears in two earlier labels of other trees (`userresfix-final-r1`, `userresfix-sub-r1`). It is excluded from the passing list.
- Several native groups end with process exit code -1073741189 (0xC000027B, faulting module `CoreMessagingXP.dll`, Windows Application log events at 12:31 and 12:42 for `admin-dashboard`) after printing "native regression run completed" with every check passed. The harness reads the exit code as a failure and stops. It reproduces on unchanged beta for `admin-dashboard`; for `admin-configure-integrations`, `-backups` and `-providers` I did not run a beta control, so I cannot say whether it also occurs there without my change. All their checks passed.

## 10. Not established

- No real Windows contrast theme was switched on; `ThemeService.ApplySystemPalette` was called with a dark and a light simulated palette and the live brushes were read back. The code that reads the five colors from the system (`ApplySystemContrast`) was not executed with a real contrast theme.
- No visual or pixel parity with the official WebUI is claimed. The numbers in section 5 are computed from the token values; rendering was checked only through the sampled button pixels and the captures above.
- `LoginPage`, `SignupPage`, `WatchTogetherJoinPage` and `WatchTogetherRoomPage` load the new `ErrorTintBrush` key; the unit test shows the key is defined before use, but I did not confirm that a selector in these runs constructs those four pages. `HouseholdSetupPage`, `InviteClaimPage` and `ActivateDevicePage` are constructed by `shell-localization` (read from its source), which passed.
- The live player's own error toasts and retry prompts were not captured; they use `ErrorBrush`/`DestructiveButtonStyle` (for example `PlayerService.NativeParty`), which are the keys changed.
- Page-local `#dc2626` styles in open pull requests were not touched; whether they match the new shared fill is for those pull requests.
- Beta was merged forward on November 21 (`28f3e8bf4`, contains #229 and #230).

## 11. Independent review (November 21) and fixes

An independent review found no P1 (no page fails to load, nothing throws, no server value can break the UI) and four P2 and several smaller items. Source: `52129002c` (fixes) and `e6e8f6dd2` (existing tests follow the move); beta `28f3e8bf4` merged first (merge commit `b63eb369a`, one conflict in `docs/current-development.md`, both entries kept, this branch's on top).

### Findings

| # | Finding | Result | Where | Guard |
|---|---|---|---|---|
| 1 | In a Windows contrast theme every red stayed a fixed red (white contrast window: text red 3.76:1, sign-in panel text 3.29:1, new label on its tint 2.45:1) | Fixed. See the mapping below | `ThemeService.cs` token lists and `ApplySystemPalette` | unit `A_windows_contrast_theme_repaints_every_red_role_with_a_system_color` and `In_a_contrast_theme_every_red_pairing_is_the_system_pairing_at_4_5_or_more` (dark and light); native `shared-destructive-theme`, "dark/light contrast theme" checks read the live brushes, the live elements and sampled pixels. The old assertions ("`ApplySystemContrast` contains no Error or Destructive", "the red roles are unchanged in a contrast theme") are inverted; `AccessibilityMotionParityTests` (two existing tests) now expect 35 tokens and read `ApplySystemPalette` |
| 2 | Error toast hard-coded `#EF6B73` | Fixed: the toast's icon and border are the live `ErrorLabelBrush` (5.24:1 on the raised surface; `ErrorBrush` is 3.89:1 there) | `Controls/ToastContainer.xaml.cs:123-134` | unit `The_remaining_literal_reds_use_the_shared_roles`; native checks "the error toast icon and border are the shared label brush" in the default, high contrast and both contrast states (reference equality with the resource) |
| 3 | Downloads failed badge below 4.5:1 (plain red on a 15% copy: 4.21 page, 3.85 card, 3.35 raised) | Fixed: `ErrorLabelBrush` on `ErrorTintBrush` (5.99 / 5.51 / 4.75). The other statuses keep their colors | `Views/DownloadsPage.xaml.cs:122-146` | unit; native: the real `DownloadsPage.CreateDownloadRow` for a failed download, checks on the live brushes in every theme state |
| 4 | `ProfileEditorDialog` validation text moved to `ErrorBrush` | Confirmed moot: `ErrorBrush` is now repainted with WindowText in a contrast theme (finding 1, dark and light palette, black 21.00:1 on white). The dialog itself was not constructed | `ProfileEditorDialog.cs:309` | unit scan of the line; the brush behaviour is the native contrast checks |
| 5 | More reds | Fixed (below) or recorded as follow-ups | | unit; native where a selector builds the element |
| 6 | Server override path | Fixed by removing it (smaller): `DestructiveTheme.Resolve` has no overrides parameter, `ThemeService` passes only the theme's tokens, the override map has no red key, the override unit tests are replaced by a test that the signature has no dictionary and that `ThemeService` passes theme tokens only. Native: a server colors map with `destructive`, `destructive-fill` and `destructive-foreground` moves no red role | `DestructiveTheme.cs:46-58`, `ThemeService.cs:292` | `A_theme_supplies_each_red_token_and_nothing_else_can`; native "a server destructive key moves no red role" |
| 7 | Test seam field | Fixed: `_simulatedSystemPalette` is gone. `public void ApplySystemPalette(ResourceDictionary, SystemPalette)` applies the five colors; `ApplySystemContrast` reads the real system colors and calls it; the native fixture calls it with a dark and a light palette, then `ApplyTheme` again to leave. No new compiler warning in the changed files | `ThemeService.cs:343-392` | unit (no `_simulatedSystemPalette` anywhere); native |
| 8 | Native checks that could not fail | Fixed: contrast, hover and pressed checks read the live brushes (fill, foreground, hover, pressed, error, label, tint, surfaces) and the sampled pixels; high contrast checks read the live brushes on the page, card and raised surface; a light contrast palette added | `SharedDestructiveThemeNativeFixture.cs` | the fixture (92 PASS lines in results.txt) |
| 9 | Wrong figures | Corrected in sections 5 and 10 above | | |
| 10 | Prepared files pill tint was a one-time copy | Fixed: `ErrorTintBrush`. No open PR touched the file | `Views/Admin/AdminPreparedFilesPage.cs:378` | unit scan; `admin-prepared-files` native group passes (2 checks, they do not read the pill) |

### Contrast theme mapping (official `@media (forced-colors: active)` shows no red)

| Role | Contrast theme | Dark palette (Window #000000, WindowText #FFFFFF, Highlight #1AEBFF, HighlightText #000000) | Light palette (#FFFFFF, #000000, #37006E, #FFFFFF) |
|---|---|---|---|
| `ErrorBrush`, `ErrorColor`, `ErrorLabelBrush`, `ErrorLabelColor` | WindowText | #FFFFFF, 21.00:1 on page, card, popover and raised | #000000, 21.00:1 |
| `ErrorTintBrush` | Window (the brush keeps its 10% opacity, so the tint over a window is that window) | #000000 | #FFFFFF |
| `DestructiveFillBrush`, `DestructiveFillColor` | Highlight | #1AEBFF | #37006E |
| `DestructiveForegroundBrush`, `DestructiveForegroundColor` | HighlightText | #000000, 14.37:1 on the fill | #FFFFFF, 15.13:1 |
| `WebUiDestructiveHoverBrush`, `WebUiDestructivePressedBrush` | copied from the fill again by `RefreshDerivedControlResources`, which `ApplySystemPalette` runs after the tokens; their 90% and 80% opacities stay | rendered hover #17D3E5; label on hover 11.49:1, pressed 9.10:1 | rendered hover #4B1A7D; label on hover 12.05:1, pressed 9.00:1 |

All of it is written in place (no brush replaced). Every key is one `ApplyTheme`/`ApplyDestructive` writes, so leaving the contrast theme restores the roles; the native check compares every red role with its default after `ApplyTheme`. The two lists in `ThemeService` went from 30 to 35 tokens (the existing `AccessibilityMotionParityTests` token test now expects 35 and reads the red keys from `ApplyDestructive`; `ErrorTint` is a brush only).

### Reds brought onto the roles in this pass

| File | Was | Now |
|---|---|---|
| `Controls/ToastContainer.xaml.cs` | `#EF6B73` | `ErrorLabelBrush` (shared instance) |
| `Views/DownloadsPage.xaml.cs` | `ErrorBrush` text on a 15% copy | `ErrorLabelBrush` on `ErrorTintBrush` |
| `Controls/EpisodeCardCaption.cs` | `IndianRed` "Damaged file", 4.27:1 on a card | `ErrorBrush` (official `UnreadableFileBadge`: `text-destructive`; 4.89 on the page, 4.51 on a card) |
| `Controls/SubtitleSearchDialog.xaml` | `#1AEF4444` panel, pink text `#FFFCA5A5` | `ErrorTintBrush`; the text is `ErrorLabelBrush` (red label on its own tint), so it follows high contrast and a Windows contrast theme. The dialog keeps its literal dark background `#171717` in every theme, so the panel paints `AppBackgroundBrush` under the tint: the label is WindowText on Window in a contrast theme (straight on the literal, a light palette gave black on `#2E2E2E`, about 1.5:1) |
| `Views/Admin/AdminRequestsPage.cs` | `IndianRed` invalid border | `ErrorBrush` |
| `Controls/AdminAppearanceOverlayEditor.Images.cs` | literal `#EF4444` text | `ErrorBrush` |
| `Views/Admin/AdminPreparedFilesPage.cs` | `Tint("ErrorBrush", 0.1)` copy | `ErrorTintBrush` |
| `Views/SettingsPage.xaml.cs` history-import status badge | `#F87171` on `#3A1313`, border from the literal | failed entry: `ErrorLabelBrush` on `ErrorTintBrush`, border the red at 20% (a snapshot copy, as the table's other borders are literals; official `border-destructive/20`). The other statuses keep their literals: official draws every status from tokens (`text-info`, `text-warning`, `text-success`, `text-muted-foreground`), which this red change does not touch, so changing the whole table would be a separate, non-red change. The run's error box below the badge keeps its literals `#1A0E0E`, `#3A1313`, `#F87171`: official's own box uses Tailwind red literals (`border-red-500/30 bg-red-500/[0.06] text-red-100/90`, `text-red-400`), not tokens |

No open pull request (checked with `gh pr list --base beta --state open --json number,files`, 13 open) touches any of these files; #242 touches only `tests/SiloPlayer.NativeRegressionTests/Program.cs`, which this pass does not edit.

### Follow-ups (files in open pull requests, not edited)

`AdminNodesPage.cs` (#238); `AdminGeneralSettingsPage.cs`, `AdminSearchSettingsPage.cs`, `AdminStorageSettingsPage.cs` (#234); Monitor, Logs, Diagnostics and Dashboard helpers (#232); Devices, Access groups and History import chips (#240).

### Runs (lane A, `accept-family-v2.ps1`, task id `admin-families-nov20`)

| Label | What | Result |
|---|---|---|
| `redfix-focused-r1`, `-r2` | focused unit | 38/38; 55/55 (with the two accessibility tests) |
| `redfix-iter-r1` | publish and native `shared-destructive-theme` | 92 PASS lines |
| `redfix-final-r1` | final | full unit 11204/11206: `AccessibilityMotionParityTests` (two tests scanning the contrast code I moved and the 30-token count); the tests were updated, not weakened |
| `redfix-final-r2` | final | stopped by me before it acquired the lease: the background wrapper had a 10-minute cap; no output |
| `redfix-final-r3` | final | full unit 11205/11206: the known intermittent `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling`; no native run |
| `redfix-final-r4` | final, source `e6e8f6dd2` | focused unit 73/73, full unit 11206/11206, 18 native groups, all passed (table below) |
| `redfix-e1`…`e20`, `e21`…`e25` | each remaining group alone with `-Stage native` on the r4 build, and reruns | below |
| `redfix-ctl1`…`ctl6`, `redfix-p1`…`p3` | controls (below) | below |

Native groups on the final source (PASS and FAIL lines in each group's results.txt; "crash" = process exit -1073741189 / 0xC000027B after "native regression run completed" with every check passed, the lead's known teardown issue):

| Group | Passed | Failed | Exit |
|---|---|---|---|
| shared-destructive-theme | 92 | 0 | 0 (r4) |
| shared-controls | 3 | 0 | 0 |
| shared-appearance | 2 | 0 | 0 |
| shell-localization | 4 | 0 | 0 |
| oct24-ui / oct24-shell | 2 / 2 | 0 | 0 |
| retained-library | 70 | 0 | 0 |
| poster-captions | 2 | 0 | 0 |
| overlay-revisions | 91 | 0 | 0 |
| household-contracts | 12 | 0 | 0 |
| profiles-metadata | 11 | 0 | 0 |
| admin-manage-foundation | 36 | 0 | 0 |
| admin-monitor | 113 | 0 | 0 |
| admin-configure-general | 188 | 0 | 0 |
| admin-configure-email | 297 | 0 | 0 |
| admin-configure-security | 374 | 0 | 0 |
| admin-configure-notifications | 35 | 0 | 0 |
| admin-configure-appearance | 113 | 0 | 0 |
| admin-dashboard (e1, rerun e22) | 311 | 0 | crash, crash |
| admin-configure-integrations (e2, e23) | 101 | 0 | crash, crash |
| admin-configure-backups (e3, e24) | 37 | 0 | crash, crash |
| admin-configure-providers (e4, e25, p1-p3) | 89 (e4, p1-p3), 86 (e25) | 0; e25: 2 | crash on e4, p1-p3; e25 crash |
| admin-collections (e5) | 203 | 0 | 0 |
| admin-home-sections (e6) | 139 | 0 | 0 |
| admin-signin-security (e7) | 172 | 0 | 0 |
| admin-requests-management (e8) | 37 | 0 | 0 |
| admin-manage-library-settings (e9) | 84 | 0 | 0 |
| admin-subtitles (e10) | 65 | 0 | 0 |
| admin-libraries (e11) | 611 | 0 | 0 |
| native-subtitle-dialogs (e12; builds the Subtitle search dialog) | 5 | 0 | 0 |
| toast-diagnostics (e13) / playback-toasts (e14) / batch-downloads (e15; builds the toast container) | 2 / 2 / 11 | 0 | 0 |
| admin-prepared-files (e17) | 2 | 0 | 0 |
| admin-appearance-overlays (e18) | 75 | 0 | 0 |
| history-import-auth (e19) | 25 | 0 | 0 |
| save-picker (e20) | 2 | 0 | 0 |
| episode-overview (e16, e21) | 13 | 1 | 1 (fails the same check twice; also on beta, below) |

The four crash groups were rerun once as asked: the same crash after all checks passed. `admin-configure-providers` e25 once failed two checks ("error: the load failure is announced", "error: Reload is reachable") and then passed 89/89 on four further runs of this source (e4, p1, p2, p3) and three of beta (ctl4-ctl6); I treat e25 as a timing flake and have no cause.

`episode-overview` fails "A button post 503 retains the description and offers a manual retry" after 13 passed checks. Controls: with only `ToastContainer` reverted to beta it fails the same way (`redfix-ctl1`); with `src` and `tests` exactly equal to beta `28f3e8bf4` (this branch's new files removed in a throw-away commit, later reverted) it fails the same way (`redfix-ctl3`). So it is a failure of beta, not caused by this change; I did not look for its cause. The throw-away commits and their reverts are in the history (net no change).

Existing groups that build the changed elements and passed: `poster-captions`, `overlay-revisions` and `oct24-ui` (episode captions, not the damaged-file notice), `admin-prepared-files`, `admin-appearance-overlays`, `admin-requests-management`, `history-import-auth`, `native-subtitle-dialogs`. Not exercised by any group, only by this branch's fixture: the Downloads page (no existing selector builds it), the toast's error state. Not run: the full no-selector native suite, the groups in the brief's exclusion list, `subtitle-preferences`.

### Not established

- A real Windows contrast theme, an actual contrast theme switch (`ColorValuesChanged`), Narrator, any second locale, all-DPI, and visual or pixel parity.
- That the damaged-file notice, the Requests invalid border, the overlay library error, the history-import badge and the Prepared files pill render as intended: they were changed to live brushes and covered by source checks and, where noted, by the groups that build their pages; I did not capture them.
- Why `episode-overview` fails on beta, and why `admin-configure-providers` failed once.
