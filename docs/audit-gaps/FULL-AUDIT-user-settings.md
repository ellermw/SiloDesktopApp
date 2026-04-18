# User Settings Page — Full Audit

Webui: SettingsLayout.tsx + 12 sub-pages (PlaybackSettings, SubtitleAppearanceSettings, AppearanceSettings, ThemeEditorSettings, AccessibilitySettings, HomeScreenSettings, CardOverlaySettings, LibrarySettings, HistoryImportSettings, WebhookSyncSettings, PluginSettings, ProfilesSettings)
Desktop: SettingsPage.xaml + .xaml.cs (8 tabs: Appearance, Playback, Libraries, Subtitles, Home Screen, Import, Plugins, Sessions)

## Status: Moderate parity — webui restructured with grouped sidebar + new tabs

### Already matching
- Playback settings (quality, language, subtitle behavior, skip intros/credits)
- Appearance/Theme settings (theme cards with preview)
- Library visibility toggles + overrides
- Subtitle appearance (font, size, color, outline, background, position, preview)
- Home screen section reordering
- History import (multi-modal: Emby/Jellyfin/Plex)
- Card overlay settings
- Profile-scoped settings

### Desktop extras (not in webui)
- Active sessions management (device list with revoke)

### Webui has, desktop missing
- Theme Editor tab (customize colors and CSS)
- Accessibility tab (readability and contrast)
- Webhook Sync tab (Plex/Emby/Jellyfin webhook intake)
- Profiles tab (admin: names, PINs, access rules)

### Gaps

| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P1 | Grouped sidebar navigation | 4 groups (Playback/Appearance/Library & Data/Account) with sticky sidebar (220px) + mobile horizontal scroll | Flat horizontal tab bar | large |
| 2 | P1 | Theme Editor tab | 3-tab editor (Tokens + Raw CSS + Catalog browser) with live preview, import/export, reset. Uses TokenEditor, RawCssEditor, CatalogBrowser, ThemePreviewCard components | Not present | large |
| 3 | P1 | Accessibility tab | Text size (Default/Large/XL), text weight (Default/Bolder), contrast (Standard/High Contrast), live preview paragraph | Not present | medium |
| 4 | P1 | Webhook Sync tab | Full connection management: create/edit/delete connections, actor discovery + mapping, event log with pagination, webhook URL rotation, copy URL. ~500 lines of complex UI | Not present | large |
| 5 | P1 | Profiles tab | Full profile management: create/edit/delete profiles with ProfileEditorDialog, avatar upload, PIN management, library access per-profile, active profile badge, delete guards | Not present (desktop has basic edit in ProfileSelectPage only) | large |
| 6 | P2 | Tab grouping/ordering | Grouped: Playback→Subtitles, Appearance→ThemeEditor→A11y→HomeScreen→CardOverlays, Library→Import→Webhook, Account→Profiles | Flat: Appearance, Playback, Libraries, Subtitles, HomeScreen, Import, Plugins, Sessions | small |
| 7 | P2 | Back button | Webui has "Back" link (ArrowLeft icon) in header | Not present | small |
