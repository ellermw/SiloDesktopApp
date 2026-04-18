# Shell / Navigation — Full Audit (Deep Re-audit)

Webui: Layout.tsx (161) + AppSidebar.tsx (762) + GlobalSearch component
Desktop: MainWindow.xaml (~240) + MainWindow.xaml.cs

## Status: Moderate parity — significant structural differences in sidebar footer

### Already matching
- Logo (▶ + server name + "Media server" subtitle)
- Home nav item with active indicator pill
- LIBRARIES section header with dynamic library items
- DISCOVER section: Search, Recommendations, Calendar
- YOUR STUFF section: Favorites, Watchlist, Watch Party, Collections, History
- Admin button in footer (admin only)
- Profile avatar button with Switch Profile + Logout
- Active indicator pill (3px accent bar, left-aligned)
- Section headers (uppercase, tracked, 10px)

### Gaps

| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P1 | Settings moved to profile dropdown | Settings is inside the user avatar dropdown (line 723-731), not a top-level nav item | Settings is a standalone footer button always visible | medium |
| 2 | P1 | Profile dropdown restructured | Avatar dropdown contains: avatar+name+username, Theme switcher (5 color dots), Settings link, Switch Profile, Logout | Desktop dropdown: Switch Profile, Logout only | medium |
| 3 | P1 | Theme switcher in profile dropdown | 5 curated theme color dots (CURATED_THEME_IDS) with hover preview (previewTheme/resetPreviewTheme), live switching | Not present anywhere in sidebar | medium |
| 4 | P1 | Libraries section collapsible | "LIBRARIES" header is a chevron toggle button that collapses/expands library list (line 314-321) | Static header, always expanded | small |
| 5 | P1 | Pinned items under libraries | Each library expands to show pinned collections/sections as sub-items with unpin button (lines 379-432) | Not present | medium |
| 6 | P2 | Sidebar auto-collapse on detail pages | Collapses to 64px (lg:ml-16) on item detail routes, hover-to-expand with 150ms delay (lines 173-192) | Fixed sidebar width always | medium |
| 7 | P2 | Global search overlay (⌘K) | GlobalSearch modal triggered by Cmd+K, shown as keyboard badge next to Search nav item (line 462) | Search is a nav page only, no global shortcut | medium |
| 8 | P2 | Sidebar hover-expand when collapsed | Collapsed sidebar expands on hover (150ms delay), shadow overlay, profile menu keeps it open | Not applicable (sidebar never collapses) | n/a |
| 9 | P2 | Search ⌘K keyboard badge | Search nav item shows `⌘K` badge in sidebar (lines 461-464) | No keyboard shortcut badge | small |
| 10 | P2 | Sidebar backdrop blur | `backdrop-blur-2xl` on sidebar (line 257) | No blur effect | small |

### Notes
- Desktop has Downloads nav item — webui has no user-facing Downloads page
- Desktop uses dedicated pages for Favorites/Watchlist/History; webui routes through /catalog?source=X
- Desktop has ServerActivityButton in header (matches webui's ServerActivity, fixed top-right on desktop)
- Desktop has MiniPlayerBar at bottom (webui uses persistent background player bar)
- Webui sidebar width: 260px expanded, 64px collapsed. Desktop: fixed 260px always.
- Webui profile dropdown opens "side" based on collapsed state (getProfileMenuSide)
