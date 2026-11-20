# Admin Configure > Appearance — parity record

Route `/admin/settings/appearance` (Configure > Server > Appearance), registered in
`AdminConfigureServices` and reachable through the real Admin navigation and search (it was never in the
catalog's unsupported list; the registration makes it openable).

Official source compared: Silo Server `66b836f82a32de2fb3e5dfae6c285c19eea1096d`
(`web/src/pages/admin-settings/AppearanceSettings.tsx`, `components/admin/BrandingAssetField.tsx` and
`brandingAssetSpecs.ts`, `components/overlays/**`, `lib/overlays/**`, `hooks/useOverlayPrefs.ts`,
`hooks/useSettingsForm.ts`, `hooks/queries/admin/{branding,overlayImages}.ts`, `pages/admin-settings/SettingField.tsx`,
`locales/en.json`; server `internal/apiv2/{admin_branding_assets,branding,overlay_images}.go`,
`internal/branding/assets.go`, `internal/overlays/images.go`, `internal/config/{admin_settings,env_settings}.go`).
The earlier source was written against `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.

## What changed upstream between 217ff25 and 66b836f82 for this page

- `AppearanceSettings.tsx`, `BrandingAssetField.tsx`, `components/overlays/*` and `lib/overlays/*` source: unchanged
  (only test files were removed). Every Appearance string in `en.json` is unchanged.
- `useSettingsForm`: the form now stays loading until the sensitive-settings status answers (a failed status ends
  the wait) and exposes `envVar(key)`; `SettingField` gained the env-managed row ("Set by {env} in the server's
  environment."). The Appearance page passes no `managedByEnv`, and no Appearance key is in the server's
  `envSettingVars` list, so no row here shows that state. The native page reads the status like the official form
  and a status failure blocks nothing.
- `useOverlayPrefs`/`useTheme`: offline handling for viewer profile writes and text-scale writes; the admin page's
  `images`, `imageUploadsAvailable` and `supportsStyles` are unchanged.
- Server: no change to the branding asset, overlay image or `defaults.*` handlers, limits or responses.

## Implemented (matching the official page)

- **Logos and icons**: wordmark, mark, favicon, login background. Accepted types and caps from
  `internal/branding/assets.go` (PNG/JPEG/WebP 8 MiB; favicon PNG/WebP/ICO/SVG 1 MiB; login background 12 MiB).
  Preview with the bundled default dimmed and captioned "Default"/"Theme gradient"; Upload/Replace; Remove.
  Storage warning ("needs artwork storage" or "Restart the server…" when `s3.public_bucket` is set).
  Type and size are checked from the picked file's metadata before any byte is read. An SVG favicon is accepted
  and stored but never handed to an image decoder (a neutral glyph stands in). Uploads and removals are sent once
  (`allowTokenRefresh: false`); a 4xx shows the server's field error or problem title; a timeout, transport
  failure, 5xx or unproven acknowledgement is an unknown outcome that blocks every image write until Reload.
  After an image write the branding and the settings snapshot are re-read (a branding write changes the settings
  ETag); a draft whose saved value changed meanwhile becomes a conflict.
- **Colors**: theme-default swatch, the eight official presets, Custom color; exactly one is selected. Restart
  chip from restart keys.
- **Card overlays**: Restore defaults (group action, disabled while the stored document is the built-in one,
  confirmation dialog, stages the official `BUILT_IN_OVERLAY_DEFAULTS`; Save writes it, Discard undoes it), the
  "Show badges on poster art" switch, Badge style (seven presets, five below manifest revision 25), the custom look
  (size, shape, weight, letters, shadow, fill, text, border, reset) at revision ≥ 25, the live preview with the
  Movie/Show/Requested samples at the admin's own poster size, and every registry overlay grouped in the four
  official categories with position (five official positions), image popover and switch. RT rating badges are
  offered only for sources the ratings capability shows. While badges are off the style, preview and rows are
  inert at half opacity.
- **Per-badge images** (revision ≥ 25): icon image and per-value images with the official suggestions, 32-value
  and 64-code-point limits; a reference the library no longer lists shows as "Removed image" so it can be cleared.
- **Badge images** group (revision ≥ 25): choose, edit the name, Upload (PNG/JPEG/WebP, 4 MB, refused locally with
  "Pick an image of 4 MB or less."), the library list and Delete with the official confirmation. Deleting an
  image that overlay settings reference neither refuses nor clears references, as officially: the server deletes
  it, badges fall back to text, and stored references remain for the admin to clear.
- **Title pages**: "Show title art" default (`defaults.ui.title_art`).
- **Save model**: the four keys (`branding.accent_color`, `overlays.enabled`, `defaults.card_overlays`,
  `defaults.ui.title_art`) save as one strong-ETag batch with only changed keys; a refusal keeps the draft for a
  corrected retry; a conflict or unknown outcome blocks Save until Reload; Discard; the leave guard.
- **Stored JSON the editor does not understand** is preserved on an edit (unknown root/item/style fields, the
  official passthrough ids `imdb_top_250` and `rt_certified_fresh`, newer items); only edited fields change. This
  keeps more than the official parser, which drops unknown fields; the Restore-defaults state compares the
  understood document, as officially.

## Deliberate differences and limits

- The badge-image upload keeps the picked file handle (not its bytes) until Upload; bytes are read only when the
  upload is sent and are not kept afterwards. A refused upload keeps the handle and name for a retry, as
  officially; an acknowledged or unknown upload, leaving the page or an authority change releases it.
- The size slider stages on every step instead of on release; staging is local and nothing is written until Save.
- Admin preview positions and offered overlays follow the viewer overlay service's manifest mapping (identical on
  revision ≥ 29 servers).
- Not observed: a live server, a real OS file picker, real image decoding of uploaded branding assets (the
  synthetic host has no reachable image origin), DPI/IME/Narrator, complete side-by-side visual parity.

## Shared code changed (additive or official-default corrections)

- `SettingsApi.cs`: `OverlayConfigResponse.ImageUploadsAvailable` (`image_uploads_available`, official
  `useOverlayPrefs.imageUploadsAvailable`). Additive.
- `CardOverlayService.cs`: `partial` (for `CardOverlayService.Editor.cs`); registry defaults corrected to the
  official registry (IMDb/TMDB/RT/RT Audience `left-edge`, Show Status `right-edge`, Advisory Age `bottom-right`
  and placed after Age Rating). The page's built-in defaults and Restore defaults are derived from this registry
  and must equal the official `BUILT_IN_OVERLAY_DEFAULTS` (unit test with the exact official string). This also
  changes the viewer's built-in fallback positions for profiles with neither a stored preference nor an admin
  default, to the official ones. Added `OverlayRegistry.ResolveValueImage`: the raw API value first (unchanged
  behaviour), then the canonical show-status key the official editor stores ("Returning" etc.).
- `OverlayBadgeRenderer.cs`: per-value image lookup through `ResolveValueImage`. Additive.
- `CardOverlayRevisionTests.cs`: one assertion changed (`rating_imdb` default `left-edge`/`top-left` instead of
  `right-edge`/`top-right`) because the official registry at 66b836f82 defaults it to `left-edge`.
- `OverlayImageWireTests.cs`: one added test for `ResolveValueImage`. `SiloPlayer.Tests.csproj`: links
  `CardOverlayService.Editor.cs`.

## Tests

Unit: `AdminAppearanceOverlayTests`, `AdminAppearanceSettingsControllerTests`, `AdminAppearanceApiTests`,
`AdminAppearanceAssetPolicyTests`. Native: `admin-configure-appearance` (loading, failed read and Reload, all
four slots including SVG, refusal, oversize and unknown outcome, accent, save with refusal/conflict/unknown,
restore defaults, leave guard, wide/compact captures, authority retirement) and `admin-appearance-overlays`
(categories and rating gating, switch and position, presets and look, the three preview samples, icon and value
images with a re-render while the popover is open, library upload/oversize/delete, badges off, one batched save,
revision 24). Acceptance (label appearance-final-r1, commit 37b87e02f): focused 297/297 and full 10,207/10,207 unit cases; native admin-configure-appearance 113 PASS, admin-appearance-overlays 75, overlay-revisions 91, overlay-pill 2, admin-manage-foundation, admin-configure-general and admin-monitor passed. `advanced-overlays` is excluded: it fails in its own WatchlistPage step (missing RequestsApi in the fixture provider, Beta #184), unrelated to this branch.
